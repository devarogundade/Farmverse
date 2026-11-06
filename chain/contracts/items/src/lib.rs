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
//! * *Studio items* (gem packs, Farm Pass, land, workers for primary sale) are minted by `minter`.
//!
//! Admin methods need the `owner` (the team multisig) and exactly 1 yoctoNEAR, so they can only be
//! signed with a full-access key.

mod events;
pub mod item;
mod standards;

use std::collections::HashMap;

use item::{Item, ItemKind, STUDIO_PREFIX, VOUCHER_PREFIX};
use near_contract_standards::non_fungible_token::metadata::{NFTContractMetadata, NFT_METADATA_SPEC, TokenMetadata};
use near_contract_standards::non_fungible_token::{NonFungibleToken, Token, TokenId};
use near_sdk::collections::LazyOption;
use near_sdk::json_types::{Base64VecU8, U128};
use near_sdk::store::{IterableSet, LookupMap, LookupSet};
use near_sdk::{
    AccountId, BorshStorageKey, CurveType, PanicOnDefault, Promise, PublicKey, assert_one_yocto, borsh,
    env, near, require, serde_json::json,
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
        };
        for key in signers {
            this.insert_signer(key);
        }
        this
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
        self.mint_item(voucher.token_id, caller, item, voucher.title, voucher.media, "voucher")
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
        self.mint_item(token_id, receiver_id, item, title, media, "studio")
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
        self.burn(&token_id, &owner);
        let after = env::storage_usage();

        near_contract_standards::non_fungible_token::events::NftBurn {
            owner_id: &owner,
            token_ids: &[&token_id],
            authorized_id: None,
            memo: Some("redeem"),
        }
        .emit();
        events::item_redeem(&token_id, &owner, &item);

        if before > after {
            let refund = env::storage_byte_cost().saturating_mul((before - after) as u128);
            if !refund.is_zero() {
                Promise::new(owner).transfer(refund).detach();
            }
        }
        item
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

impl Contract {
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

    /// Mints and charges the caller for exactly the storage used (the rest of the deposit is refunded).
    fn mint_item(&mut self, token_id: TokenId, owner: AccountId, item: Item, title: String, media: String, source: &str) -> Token {
        require!(!self.burned.contains(&token_id), "token_id was used before");
        let before = env::storage_usage();
        let metadata = Self::token_metadata(&item, title, media);
        self.items.insert(token_id.clone(), item.clone());
        let token = self.tokens.internal_mint_with_refund(token_id.clone(), owner.clone(), Some(metadata), None);
        self.items.flush();
        let used = env::storage_usage().saturating_sub(before);
        charge_storage(used, env::predecessor_account_id());
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
