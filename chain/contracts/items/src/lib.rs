//! Farmverse items — game goods, buildings, land, tools, packs and workers as NFTs.
//!
//! Standards: NEP-171 (core), NEP-177 (metadata), NEP-178 (approvals), NEP-181 (enumeration),
//! NEP-199 (payouts), NEP-297 (events). One item type per token, never mixed.
//!
//! **How items cross between the game and the chain**
//!
//! * *Mint (island → NFT).* The game server takes the goods off the player's island, then signs a
//!   [`MintVoucher`] with one of the registered `signers` keys. The player submits it with
//!   [`Contract::mint_with_voucher`] from their own wallet and pays the storage. The server's key
//!   can only authorise new tokens for goods it already removed; it can never move, burn or list an
//!   existing token. A voucher works once (its `token_id` can never be minted again, even after a
//!   burn) and only until `expires_at_ms`, after which the server may safely refund the goods.
//! * *Redeem (NFT → island).* The holder calls [`Contract::redeem`]; the token is burned, its storage
//!   refunded to them, and an `item_redeem` event tells the game what to credit — once per token.
//!   Workers are held, not redeemed.
//! * *Studio items* (gem packs, Farm Pass, land, workers for primary sale) are minted by `minter`,
//!   either one by one ([`Contract::studio_mint`]) or sold as *drops*: fixed-price editions bought with
//!   FARM through `ft_transfer_call`, with an optional cap this contract enforces (see [`drops`]).
//!
//! Admin methods need the `owner` (the team multisig) and exactly 1 yoctoNEAR, so they can only be
//! signed with a full-access key.

pub mod drops;
mod events;
pub mod item;
mod standards;

use std::collections::HashMap;

use drops::{BuyMsg, Drop, DropInput, DropUpdate};
use item::{Item, ItemKind, STUDIO_PREFIX, VOUCHER_PREFIX};
use near_contract_standards::fungible_token::core::ext_ft_core;
use near_contract_standards::fungible_token::receiver::FungibleTokenReceiver;
use near_contract_standards::non_fungible_token::metadata::{NFTContractMetadata, NFT_METADATA_SPEC, TokenMetadata};
use near_contract_standards::non_fungible_token::{NonFungibleToken, Token, TokenId};
use near_sdk::collections::LazyOption;
use near_sdk::json_types::{Base64VecU8, U128};
use near_sdk::store::{IterableMap, IterableSet, LookupMap, LookupSet};
use near_sdk::{
    AccountId, BorshStorageKey, CurveType, Gas, NearToken, PanicOnDefault, Promise, PromiseOrValue, PublicKey,
    assert_one_yocto, borsh, env, near, require, serde_json, serde_json::json,
};

/// Signed bytes = `MINT_DOMAIN || borsh(MintVoucher)`. The domain keeps a voucher signature from
/// ever being valid as anything else signed with the same key.
pub const MINT_DOMAIN: &[u8] = b"farmverse:items:mint:v1";
/// The longest a voucher may live (guards against a server bug issuing near-permanent vouchers).
pub const MAX_VOUCHER_TTL_MS: u64 = 7 * 24 * 60 * 60 * 1000;
pub const MAX_SIGNERS: u32 = 10;
pub const MAX_ROYALTY_ACCOUNTS: usize = 5;
/// Royalties in total may take at most 10 % of a sale.
pub const MAX_ROYALTY_BPS: u32 = 1_000;
pub const BPS: u128 = 10_000;
/// Payout map size when the caller doesn't say (royalty accounts + the owner).
pub const DEFAULT_MAX_LEN_PAYOUT: u32 = 10;
/// Drop editions' storage is paid by this contract; sales stop before its free balance drops below this.
pub const DROP_FLOAT_MIN: NearToken = NearToken::from_millinear(500);
pub const MAX_DROPS_PAGE: u32 = 100;
const ONE_YOCTO: NearToken = NearToken::from_yoctonear(1);
const GAS_FT_TRANSFER: Gas = Gas::from_tgas(10);

/// What the game server signs for one mint. **Field order is the borsh layout — append only.**
#[near(serializers = [borsh, json])]
#[derive(Clone, Debug, PartialEq)]
pub struct MintVoucher {
    /// This contract's account id (stops a testnet voucher working on mainnet, or on another contract).
    pub contract_id: AccountId,
    /// Must start with `g-`. Unique forever.
    pub token_id: TokenId,
    /// The wallet the island belongs to; only it may submit the voucher.
    pub receiver_id: AccountId,
    pub kind: ItemKind,
    pub item_id: String,
    pub quantity: u64,
    pub props: String,
    /// Display name, e.g. "Wheat ×250".
    pub title: String,
    /// Icon path under `base_uri`, e.g. `items/wheat.png`.
    pub media: String,
    pub expires_at_ms: u64,
}

/// NEP-199 payout.
#[near(serializers = [json])]
#[derive(Debug, PartialEq)]
pub struct Payout {
    pub payout: HashMap<AccountId, U128>,
}

#[near(serializers = [json])]
pub struct Config {
    pub owner_id: AccountId,
    pub pending_owner_id: Option<AccountId>,
    pub minter_id: AccountId,
    pub signers: Vec<PublicKey>,
    pub royalties: HashMap<AccountId, u32>,
    pub paused: bool,
    /// The FT accepted for drops (FARM) and where the takings go.
    pub farm_id: Option<AccountId>,
    pub treasury_id: Option<AccountId>,
}

#[derive(BorshStorageKey)]
#[near]
enum StorageKey {
    NonFungibleToken,
    Metadata,
    TokenMetadata,
    Enumeration,
    Approval,
    Items,
    Burned,
    Signers,
    // v2
    Drops,
    DropBuys,
}

/// The state as deployed before drops (v1). Only [`Contract::migrate`] reads it.
#[near(serializers = [borsh])]
struct ContractV1 {
    tokens: NonFungibleToken,
    metadata: LazyOption<NFTContractMetadata>,
    items: LookupMap<TokenId, Item>,
    burned: LookupSet<TokenId>,
    signers: IterableSet<PublicKey>,
    owner_id: AccountId,
    pending_owner_id: Option<AccountId>,
    minter_id: AccountId,
    royalties: HashMap<AccountId, u32>,
    paused: bool,
}

#[derive(PanicOnDefault)]
#[near(contract_state)]
pub struct Contract {
    tokens: NonFungibleToken,
    metadata: LazyOption<NFTContractMetadata>,
    /// The authoritative contents of each live token (mirrored into NEP-177 `extra` for wallets).
    items: LookupMap<TokenId, Item>,
    /// Every token id ever burned: a voucher for one of these can never mint again.
    burned: LookupSet<TokenId>,
    /// Game-server ed25519 keys whose vouchers are accepted.
    signers: IterableSet<PublicKey>,
    owner_id: AccountId,
    pending_owner_id: Option<AccountId>,
    minter_id: AccountId,
    /// Account → basis points of every sale (NEP-199).
    royalties: HashMap<AccountId, u32>,
    /// Stops minting and redeeming (transfers and sales always work, so nobody's tokens are frozen).
    paused: bool,
    // ---- v2: drops
    farm_id: Option<AccountId>,
    treasury_id: Option<AccountId>,
    drops: IterableMap<String, Drop>,
    /// `"<drop>:<account>"` → editions that account bought (per-wallet limits).
    drop_buys: LookupMap<String, u32>,
}

#[near]
impl Contract {
    #[init]
    pub fn new(
        owner_id: AccountId,
        minter_id: AccountId,
        signers: Vec<PublicKey>,
        royalties: HashMap<AccountId, u32>,
        metadata: NFTContractMetadata,
    ) -> Self {
        require!(!env::state_exists(), "Already initialized");
        require!(metadata.spec == NFT_METADATA_SPEC, "metadata.spec must be nft-1.0.0");
        metadata.assert_valid();
        Self::assert_royalties(&royalties);
        let mut this = Self {
            tokens: NonFungibleToken::new(
                StorageKey::NonFungibleToken,
                owner_id.clone(),
                Some(StorageKey::TokenMetadata),
                Some(StorageKey::Enumeration),
                Some(StorageKey::Approval),
            ),
            metadata: LazyOption::new(StorageKey::Metadata, Some(&metadata)),
            items: LookupMap::new(StorageKey::Items),
            burned: LookupSet::new(StorageKey::Burned),
            signers: IterableSet::new(StorageKey::Signers),
            owner_id,
            pending_owner_id: None,
            minter_id,
            royalties,
            paused: false,
            farm_id: None,
            treasury_id: None,
            drops: IterableMap::new(StorageKey::Drops),
            drop_buys: LookupMap::new(StorageKey::DropBuys),
        };
        for key in signers {
            this.insert_signer(key);
        }
        this
    }

    /// Upgrades v1 state (before drops) in place: every token, approval and setting stays. Called by the
    /// contract account itself right after deploying the new code. Running it twice fails (v2 state
    /// doesn't parse as v1), so it can't wipe the drops.
    #[private]
    #[init(ignore_state)]
    pub fn migrate() -> Self {
        let old: ContractV1 = env::state_read().unwrap_or_else(|| env::panic_str("No v1 state to migrate"));
        Self {
            tokens: old.tokens,
            metadata: old.metadata,
            items: old.items,
            burned: old.burned,
            signers: old.signers,
            owner_id: old.owner_id,
            pending_owner_id: old.pending_owner_id,
            minter_id: old.minter_id,
            royalties: old.royalties,
            paused: old.paused,
            farm_id: None,
            treasury_id: None,
            drops: IterableMap::new(StorageKey::Drops),
            drop_buys: LookupMap::new(StorageKey::DropBuys),
        }
    }

    // ---------------------------------------------------------------- mint & redeem

    /// Mints the goods a game voucher describes to the caller. Attach enough NEAR for storage
    /// (about 0.01 NEAR; the excess is refunded).
    #[payable]
    pub fn mint_with_voucher(&mut self, voucher: MintVoucher, signer: PublicKey, signature: Base64VecU8) -> Token {
        self.assert_not_paused();
        let caller = env::predecessor_account_id();
        require!(voucher.contract_id == env::current_account_id(), "Voucher is for another contract");
        require!(voucher.receiver_id == caller, "Voucher is for another account");
        let now = env::block_timestamp_ms();
        require!(now <= voucher.expires_at_ms, "Voucher expired");
        require!(voucher.expires_at_ms <= now.saturating_add(MAX_VOUCHER_TTL_MS), "Voucher lives too long");
        require!(self.signers.contains(&signer), "Unknown signer");
        require!(Self::verify(&voucher, &signer, &signature), "Bad voucher signature");

        item::assert_token_id(&voucher.token_id, VOUCHER_PREFIX);
        item::assert_item(voucher.kind, &voucher.item_id, voucher.quantity, &voucher.props, &voucher.title, &voucher.media);
        require!(voucher.kind != ItemKind::Worker && voucher.kind != ItemKind::Pack, "Only the studio mints workers and packs");

        let item = Item {
            kind: voucher.kind,
            item_id: voucher.item_id,
            quantity: voucher.quantity,
            props: voucher.props,
            minted_from: Some(caller.clone()),
            minted_at_ms: now,
        };
        self.mint_item(voucher.token_id, caller.clone(), item, voucher.title, voucher.media, "voucher", Some(caller))
    }

    /// Studio mint (primary-sale items: packs, land, workers …). `minter` only.
    #[payable]
    #[allow(clippy::too_many_arguments)]
    pub fn studio_mint(
        &mut self,
        token_id: TokenId,
        receiver_id: AccountId,
        kind: ItemKind,
        item_id: String,
        quantity: u64,
        props: String,
        title: String,
        media: String,
    ) -> Token {
        self.assert_not_paused();
        require!(env::predecessor_account_id() == self.minter_id, "Only the minter");
        item::assert_token_id(&token_id, STUDIO_PREFIX);
        item::assert_item(kind, &item_id, quantity, &props, &title, &media);
        let item = Item { kind, item_id, quantity, props, minted_from: None, minted_at_ms: env::block_timestamp_ms() };
        self.mint_item(token_id, receiver_id, item, title, media, "studio", Some(env::predecessor_account_id()))
    }

    /// Burns a token so the game credits its contents to the caller's island. Attach 1 yoctoNEAR.
    /// The storage the token used is refunded to the caller.
    #[payable]
    pub fn redeem(&mut self, token_id: TokenId) -> Item {
        assert_one_yocto();
        self.assert_not_paused();
        let caller = env::predecessor_account_id();
        let owner = self.tokens.owner_by_id.get(&token_id).unwrap_or_else(|| env::panic_str("Token not found"));
        require!(owner == caller, "Only the owner can redeem");
        let item = self.items.get(&token_id).cloned().unwrap_or_else(|| env::panic_str("Token has no item"));
        require!(item.kind.redeemable(), "Workers are held, not redeemed");

        let before = env::storage_usage();
        self.burn_redeemed(&token_id, &owner, &item);
        let after = env::storage_usage();

        if before > after {
            let refund = env::storage_byte_cost().saturating_mul((before - after) as u128);
            if !refund.is_zero() {
                Promise::new(owner).transfer(refund).detach();
            }
        }
        item
    }

    // ---------------------------------------------------------------- drops

    /// Creates a drop (minter only). Attach NEAR for the drop's storage; the rest is refunded.
    #[payable]
    pub fn create_drop(&mut self, drop: DropInput) -> Drop {
        require!(env::predecessor_account_id() == self.minter_id, "Only the minter");
        require!(self.farm_id.is_some(), "Set the drop payment token first");
        let d = Drop::from_input(drop);
        require!(!self.drops.contains_key(&d.id), "A drop with this id exists");
        let before = env::storage_usage();
        self.drops.insert(d.id.clone(), d.clone());
        self.drops.flush();
        charge_storage(env::storage_usage().saturating_sub(before), env::predecessor_account_id());
        events::drop_update("create", &d);
        d
    }

    /// Changes a drop's price, cap (down only), wallet limit, window or pause. Minter or owner; 1 yocto.
    #[payable]
    pub fn update_drop(&mut self, id: String, update: DropUpdate) -> Drop {
        assert_one_yocto();
        let caller = env::predecessor_account_id();
        require!(caller == self.minter_id || caller == self.owner_id, "Only the minter or the owner");
        let mut d = self.drops.get(&id).cloned().unwrap_or_else(|| env::panic_str("No such drop"));
        d.apply(update);
        self.drops.insert(id, d.clone());
        events::drop_update("update", &d);
        d
    }

    /// Sends FARM this contract holds (a forward to the treasury that failed) to the treasury. Anyone may call.
    pub fn sweep_farm(&mut self, amount: U128) -> Promise {
        let farm = self.farm_id.clone().unwrap_or_else(|| env::panic_str("No payment token"));
        let treasury = self.treasury_id.clone().unwrap_or_else(|| env::panic_str("No treasury"));
        ext_ft_core::ext(farm)
            .with_attached_deposit(ONE_YOCTO)
            .with_static_gas(GAS_FT_TRANSFER)
            .ft_transfer(treasury, amount, Some("farmverse drop takings".into()))
    }

    pub fn get_drop(&self, id: String) -> Option<Drop> {
        self.drops.get(&id).cloned()
    }

    pub fn get_drops(&self, from_index: Option<u32>, limit: Option<u32>) -> Vec<Drop> {
        let limit = limit.unwrap_or(MAX_DROPS_PAGE).min(MAX_DROPS_PAGE) as usize;
        self.drops.values().skip(from_index.unwrap_or(0) as usize).take(limit).cloned().collect()
    }

    /// Editions of `drop_id` that `account_id` bought (for per-wallet limits).
    pub fn drop_bought(&self, drop_id: String, account_id: AccountId) -> u32 {
        self.drop_buys.get(&Self::buy_key(&drop_id, &account_id)).copied().unwrap_or(0)
    }

    // ---------------------------------------------------------------- NEP-199 payouts

    pub fn nft_payout(&self, token_id: TokenId, balance: U128, max_len_payout: Option<u32>) -> Payout {
        let owner = self.tokens.owner_by_id.get(&token_id).unwrap_or_else(|| env::panic_str("Token not found"));
        self.payout(&owner, balance.0, max_len_payout)
    }

    /// Transfers like `nft_transfer` and returns how `balance` must be split (marketplaces call this).
    #[payable]
    pub fn nft_transfer_payout(
        &mut self,
        receiver_id: AccountId,
        token_id: TokenId,
        approval_id: Option<u64>,
        memo: Option<String>,
        balance: U128,
        max_len_payout: Option<u32>,
    ) -> Payout {
        assert_one_yocto();
        let sender = env::predecessor_account_id();
        let (previous_owner, approvals) =
            self.tokens.internal_transfer(&sender, &receiver_id, &token_id, approval_id, memo);
        let payout = self.payout(&previous_owner, balance.0, max_len_payout);
        if let Some(approvals) = approvals {
            refund_approvals(previous_owner, &approvals);
        }
        payout
    }

    // ---------------------------------------------------------------- views

    pub fn get_item(&self, token_id: TokenId) -> Option<Item> {
        self.items.get(&token_id).cloned()
    }

    pub fn is_burned(&self, token_id: TokenId) -> bool {
        self.burned.contains(&token_id)
    }

    /// The exact bytes a signer must sign for `voucher` (lets the game server prove its encoding).
    pub fn voucher_message(&self, voucher: MintVoucher) -> Base64VecU8 {
        Base64VecU8::from(Self::message(&voucher))
    }

    pub fn get_config(&self) -> Config {
        Config {
            owner_id: self.owner_id.clone(),
            pending_owner_id: self.pending_owner_id.clone(),
            minter_id: self.minter_id.clone(),
            signers: self.signers.iter().cloned().collect(),
            royalties: self.royalties.clone(),
            paused: self.paused,
            farm_id: self.farm_id.clone(),
            treasury_id: self.treasury_id.clone(),
        }
    }

    // ---------------------------------------------------------------- admin (owner, 1 yocto)

    #[payable]
    pub fn add_signer(&mut self, key: PublicKey) {
        self.assert_owner();
        self.insert_signer(key.clone());
        events::config_change("signer_added", json!(key));
    }

    #[payable]
    pub fn remove_signer(&mut self, key: PublicKey) {
        self.assert_owner();
        require!(self.signers.remove(&key), "Not a signer");
        events::config_change("signer_removed", json!(key));
    }

    #[payable]
    pub fn set_minter(&mut self, minter_id: AccountId) {
        self.assert_owner();
        events::config_change("minter", json!(minter_id));
        self.minter_id = minter_id;
    }

    #[payable]
    pub fn set_royalties(&mut self, royalties: HashMap<AccountId, u32>) {
        self.assert_owner();
        Self::assert_royalties(&royalties);
        events::config_change("royalties", json!(royalties));
        self.royalties = royalties;
    }

    #[payable]
    pub fn set_paused(&mut self, paused: bool) {
        self.assert_owner();
        self.paused = paused;
        events::config_change("paused", json!(paused));
    }

    /// The FT drops are paid in (FARM) and the account the takings go to. Register this contract with
    /// the FT (`storage_deposit`) as well, so failed forwards can be swept.
    #[payable]
    pub fn set_drop_payment(&mut self, farm_id: AccountId, treasury_id: AccountId) {
        self.assert_owner();
        events::config_change("drop_payment", json!({ "farm_id": farm_id, "treasury_id": treasury_id }));
        self.farm_id = Some(farm_id);
        self.treasury_id = Some(treasury_id);
    }

    #[payable]
    pub fn set_base_uri(&mut self, base_uri: Option<String>) {
        self.assert_owner();
        let mut metadata = self.metadata.get().expect("metadata is set at init");
        metadata.base_uri = base_uri.clone();
        self.metadata.set(&metadata);
        events::config_change("base_uri", json!(base_uri));
    }

    /// Step one of handing the contract to a new owner; the new owner must `accept_owner`.
    #[payable]
    pub fn propose_owner(&mut self, owner_id: Option<AccountId>) {
        self.assert_owner();
        self.pending_owner_id = owner_id;
    }

    #[payable]
    pub fn accept_owner(&mut self) {
        assert_one_yocto();
        let caller = env::predecessor_account_id();
        require!(self.pending_owner_id.as_ref() == Some(&caller), "Not the proposed owner");
        events::config_change("owner", json!(caller));
        self.owner_id = caller;
        self.pending_owner_id = None;
    }
}

#[near]
impl FungibleTokenReceiver for Contract {
    /// Buys one edition of a drop with FARM (`msg`: `{"drop":"<id>","use":false}`). Panicking returns the
    /// FARM (FT standard); on success all of it is kept and forwarded to the treasury.
    fn ft_on_transfer(&mut self, sender_id: AccountId, amount: U128, msg: String) -> PromiseOrValue<U128> {
        require!(Some(env::predecessor_account_id()) == self.farm_id, "Only FARM is accepted");
        self.assert_not_paused();
        let buy: BuyMsg = serde_json::from_str(&msg).unwrap_or_else(|_| env::panic_str("msg must be {\"drop\":…}"));
        let mut d = self.drops.get(&buy.drop).cloned().unwrap_or_else(|| env::panic_str("No such drop"));
        let key = Self::buy_key(&d.id, &sender_id);
        let bought = self.drop_buys.get(&key).copied().unwrap_or(0);
        let now = env::block_timestamp_ms();
        d.assert_buyable(now, amount.0, bought);
        if buy.use_now {
            require!(d.kind.redeemable(), "Workers are held, not used up");
        }

        let edition = d.sold + 1;
        let token_id = d.token_id(edition);
        let item = Item {
            kind: d.kind,
            item_id: d.item_id.clone(),
            quantity: d.quantity,
            props: d.edition_props(edition),
            minted_from: None,
            minted_at_ms: now,
        };
        self.mint_item(token_id.clone(), sender_id.clone(), item.clone(), d.edition_title(edition), d.media.clone(), "drop", None);
        if buy.use_now {
            self.burn_redeemed(&token_id, &sender_id, &item);
        }
        d.sold = edition;
        self.drops.insert(d.id.clone(), d.clone());
        self.drop_buys.insert(key, bought + 1);
        self.drops.flush();
        self.drop_buys.flush();
        self.assert_float();
        events::drop_sale(&d.id, &token_id, &sender_id, amount, edition, buy.use_now);

        if let Some(treasury) = self.treasury_id.clone() {
            // A failed forward leaves the FARM here; `sweep_farm` sends it on later.
            ext_ft_core::ext(env::predecessor_account_id())
                .with_attached_deposit(ONE_YOCTO)
                .with_static_gas(GAS_FT_TRANSFER)
                .ft_transfer(treasury, amount, Some(format!("drop {}", d.id)))
                .detach();
        }
        PromiseOrValue::Value(U128(0))
    }
}

impl Contract {
    fn buy_key(drop_id: &str, account_id: &AccountId) -> String {
        format!("{drop_id}:{account_id}")
    }

    /// Drop editions are stored at this contract's cost: refuse once the free balance runs low.
    fn assert_float(&self) {
        let locked = env::storage_byte_cost().saturating_mul(env::storage_usage() as u128);
        let free = env::account_balance().saturating_sub(locked);
        require!(free >= DROP_FLOAT_MIN, "The shop is restocking. Try again later");
    }

    /// Burns a redeemed token and announces it (`nft_burn` + `item_redeem`).
    fn burn_redeemed(&mut self, token_id: &TokenId, owner: &AccountId, item: &Item) {
        self.burn(token_id, owner);
        near_contract_standards::non_fungible_token::events::NftBurn {
            owner_id: owner,
            token_ids: &[token_id],
            authorized_id: None,
            memo: Some("redeem"),
        }
        .emit();
        events::item_redeem(token_id, owner, item);
    }

    fn message(voucher: &MintVoucher) -> Vec<u8> {
        let mut bytes = MINT_DOMAIN.to_vec();
        bytes.extend(borsh::to_vec(voucher).unwrap_or_else(|_| env::panic_str("voucher encoding")));
        bytes
    }

    fn verify(voucher: &MintVoucher, signer: &PublicKey, signature: &Base64VecU8) -> bool {
        let Ok(sig): Result<[u8; 64], _> = signature.0.as_slice().try_into() else {
            env::panic_str("signature must be 64 bytes");
        };
        let key: [u8; 32] = signer.as_bytes()[1..].try_into().unwrap_or_else(|_| env::panic_str("bad key"));
        env::ed25519_verify(&sig, Self::message(voucher), &key)
    }

    fn insert_signer(&mut self, key: PublicKey) {
        require!(key.curve_type() == CurveType::ED25519, "Signers must be ed25519 keys");
        require!(self.signers.len() < MAX_SIGNERS, "Too many signers");
        require!(self.signers.insert(key), "Already a signer");
    }

    fn assert_owner(&self) {
        assert_one_yocto();
        require!(env::predecessor_account_id() == self.owner_id, "Only the owner");
    }

    fn assert_not_paused(&self) {
        require!(!self.paused, "Paused");
    }

    fn assert_royalties(royalties: &HashMap<AccountId, u32>) {
        require!(royalties.len() <= MAX_ROYALTY_ACCOUNTS, "Too many royalty accounts");
        let total: u32 = royalties.values().try_fold(0u32, |a, b| a.checked_add(*b)).unwrap_or(u32::MAX);
        require!(total <= MAX_ROYALTY_BPS, "Royalties may total at most 1000 bps");
    }

    /// The token's NEP-177 metadata: title + icon, and the item as JSON in `extra` for wallets.
    fn token_metadata(item: &Item, title: String, media: String) -> TokenMetadata {
        TokenMetadata {
            title: Some(title),
            description: None,
            media: Some(media),
            media_hash: None,
            copies: Some(1),
            issued_at: Some(item.minted_at_ms.to_string()),
            expires_at: None,
            starts_at: None,
            updated_at: None,
            extra: Some(json!(item).to_string()),
            reference: None,
            reference_hash: None,
        }
    }

    /// Mints and charges `payer` for exactly the storage used (the rest of the attached deposit is
    /// refunded). With no payer (drops) the contract's own balance pays.
    #[allow(clippy::too_many_arguments)]
    fn mint_item(
        &mut self,
        token_id: TokenId,
        owner: AccountId,
        item: Item,
        title: String,
        media: String,
        source: &str,
        payer: Option<AccountId>,
    ) -> Token {
        require!(!self.burned.contains(&token_id), "token_id was used before");
        let before = env::storage_usage();
        let metadata = Self::token_metadata(&item, title, media);
        self.items.insert(token_id.clone(), item.clone());
        let token = self.tokens.internal_mint_with_refund(token_id.clone(), owner.clone(), Some(metadata), None);
        self.items.flush();
        let used = env::storage_usage().saturating_sub(before);
        if let Some(payer) = payer {
            charge_storage(used, payer);
        }
        near_contract_standards::non_fungible_token::events::NftMint {
            owner_id: &owner,
            token_ids: &[&token_id],
            memo: Some(source),
        }
        .emit();
        events::item_mint(&token_id, &owner, &item, source);
        token
    }

    /// Removes every trace of a live token and remembers its id forever.
    fn burn(&mut self, token_id: &TokenId, owner: &AccountId) {
        self.tokens.owner_by_id.remove(token_id);
        if let Some(by_id) = &mut self.tokens.token_metadata_by_id {
            by_id.remove(token_id);
        }
        if let Some(per_owner) = &mut self.tokens.tokens_per_owner {
            let mut ids = per_owner.get(owner).unwrap_or_else(|| env::panic_str("owner index missing"));
            ids.remove(token_id);
            if ids.is_empty() {
                per_owner.remove(owner);
            } else {
                per_owner.insert(owner, &ids);
            }
        }
        if let Some(approvals) = &mut self.tokens.approvals_by_id {
            approvals.remove(token_id);
        }
        if let Some(next) = &mut self.tokens.next_approval_id_by_id {
            next.remove(token_id);
        }
        self.items.remove(token_id);
        self.burned.insert(token_id.clone());
        self.items.flush();
    }

    fn payout(&self, owner: &AccountId, balance: u128, max_len_payout: Option<u32>) -> Payout {
        let mut payout: HashMap<AccountId, U128> = HashMap::new();
        let mut paid: u128 = 0;
        for (account, bps) in &self.royalties {
            if account == owner {
                continue; // the owner's royalty is part of the remainder
            }
            let amount = balance
                .checked_mul(*bps as u128)
                .unwrap_or_else(|| env::panic_str("balance too large"))
                / BPS;
            if amount > 0 {
                payout.insert(account.clone(), U128(amount));
                paid += amount;
            }
        }
        payout.insert(owner.clone(), U128(balance - paid));
        let max_len = max_len_payout.unwrap_or(DEFAULT_MAX_LEN_PAYOUT);
        require!(payout.len() as u32 <= max_len, "Payout exceeds max_len_payout");
        Payout { payout }
    }
}

/// Requires the attached deposit to cover `bytes` of storage and refunds the rest to `account_id`
/// (same rule as the standards crate's private helper).
fn charge_storage(bytes: u64, account_id: AccountId) {
    let cost = env::storage_byte_cost().saturating_mul(bytes as u128);
    let attached = env::attached_deposit();
    require!(cost <= attached, format!("Must attach {} to cover storage", cost.exact_amount_display()));
    let refund = attached.saturating_sub(cost);
    if refund.as_yoctonear() > 1 {
        Promise::new(account_id).transfer(refund).detach();
    }
}

/// Returns the storage deposit approvals were paid with, as `nft_transfer_call` does.
fn refund_approvals(account_id: AccountId, approvals: &HashMap<AccountId, u64>) {
    // An approval entry is the account id string (4-byte length + bytes) plus a u64 approval id.
    let bytes: u64 = approvals.keys().map(|a| a.as_str().len() as u64 + 4 + 8).sum();
    let refund = env::storage_byte_cost().saturating_mul(bytes as u128);
    if !refund.is_zero() {
        Promise::new(account_id).transfer(refund).detach();
    }
}

/// Lets the unit tests and the backend's conformance tests build the same signed bytes.
pub fn voucher_signing_bytes(voucher: &MintVoucher) -> Vec<u8> {
    Contract::message(voucher)
}

#[cfg(all(test, not(target_arch = "wasm32")))]
mod tests;
