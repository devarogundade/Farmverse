//! Farmverse marketplace — fixed-price sales of whitelisted NFTs for FARM.
//!
//! * **List:** the owner calls `nft_approve(token_id, market, msg: {"price": "<FARM>"})` on the NFT
//!   contract; its `nft_on_approve` lands here and records the listing. Approving again with a new
//!   price relists. Each listing reserves `STORAGE_PER_LISTING` of the seller's storage deposit.
//! * **Buy:** the buyer calls `farm.ft_transfer_call(market, price, msg: {"nft_contract_id": …,
//!   "token_id": …})`. The listing is taken off the book at once, so two buyers can't both win. The
//!   market asks the NFT contract for `nft_transfer_payout` with the sale price minus the fee.
//!   * NFT transfer failed (sold elsewhere, redeemed, approval revoked) → the buyer is refunded in
//!     full, automatically, by the FT standard's unused-amount return.
//!   * NFT transferred → the fee goes to `treasury_id`, royalties and the seller are paid per the
//!     NEP-199 payout. A payment that can't be delivered (receiver not registered with FARM) is
//!     kept as `owed` and can be withdrawn later with `withdraw_owed` — never lost.
//! * **Offers** ([`offers`]): a buyer escrows FARM for a token or for any token of an item; the owner
//!   accepts by approving the market with `{"accept_offer": id}`.
//! * **Rentals** ([`rentals`]): a lender escrows a worker here; a renter pays FARM per day and the
//!   worker works on the renter's island until the rental ends. The FARM waits in the worker's own
//!   balance until the lender withdraws it.
//! * The fee is capped in code at `MAX_FEE_BPS`; the accepted token can't change after init.
//! * Offers and rentals reserve `STORAGE_PER_LISTING` of the maker's storage deposit, like listings.

use near_contract_standards::fungible_token::receiver::FungibleTokenReceiver;
use near_contract_standards::non_fungible_token::TokenId;
use near_contract_standards::non_fungible_token::approval::NonFungibleTokenApprovalReceiver;

pub mod offers;
pub mod rentals;
use offers::Offer;
use rentals::Rental;
use near_sdk::json_types::U128;
use near_sdk::serde::Deserialize;
use near_sdk::serde_json::{self, json};
use near_sdk::store::{IterableMap, IterableSet, LookupMap};
use near_sdk::{
    AccountId, BorshStorageKey, Gas, NearToken, PanicOnDefault, Promise, PromiseError, PromiseOrValue, assert_one_yocto,
    env, ext_contract, log, near, require,
};
use std::collections::HashMap;

pub const MAX_FEE_BPS: u32 = 1_000;
pub const BPS: u128 = 10_000;
/// NEAR each listing reserves from the seller's storage deposit (covers the listing's bytes).
pub const STORAGE_PER_LISTING: NearToken = NearToken::from_millinear(10);
/// Most payees a sale pays (royalty accounts + the owner); keeps the payout gas bounded.
pub const MAX_LEN_PAYOUT: u32 = 6;

const GAS_NFT_TRANSFER_PAYOUT: Gas = Gas::from_tgas(30);
const GAS_RESOLVE_PURCHASE: Gas = Gas::from_tgas(110);
const GAS_FT_TRANSFER: Gas = Gas::from_tgas(7);
const GAS_ON_PAYMENT: Gas = Gas::from_tgas(4);
const GAS_NFT_TOKEN: Gas = Gas::from_tgas(10);
const GAS_RESOLVE_STALE: Gas = Gas::from_tgas(10);
const ONE_YOCTO: NearToken = NearToken::from_yoctonear(1);
const GAS_GET_ITEM: Gas = Gas::from_tgas(10);
const GAS_NFT_TRANSFER: Gas = Gas::from_tgas(15);

#[near(serializers = [borsh, json])]
#[derive(Clone, Debug, PartialEq)]
pub struct Listing {
    pub nft_contract_id: AccountId,
    pub token_id: TokenId,
    pub owner_id: AccountId,
    pub approval_id: u64,
    pub price: U128,
    pub listed_at_ms: u64,
}

#[derive(Deserialize)]
#[serde(crate = "near_sdk::serde")]
struct ListArgs {
    price: U128,
}

#[derive(Deserialize)]
#[serde(crate = "near_sdk::serde")]
struct BuyArgs {
    nft_contract_id: AccountId,
    token_id: TokenId,
}

/// NEP-199 payout as the NFT contract returns it.
#[near(serializers = [json])]
#[derive(Debug)]
pub struct Payout {
    pub payout: HashMap<AccountId, U128>,
}

/// The fields of an NEP-171 token the stale check needs (other fields are ignored).
#[near(serializers = [json])]
pub struct TokenView {
    pub owner_id: AccountId,
    pub approved_account_ids: Option<HashMap<AccountId, u64>>,
}

#[near(serializers = [json])]
pub struct Config {
    pub owner_id: AccountId,
    pub pending_owner_id: Option<AccountId>,
    pub ft_token_id: AccountId,
    pub treasury_id: AccountId,
    pub fee_bps: u32,
    pub nft_contracts: Vec<AccountId>,
    pub paused: bool,
}

#[ext_contract(ext_nft)]
#[allow(dead_code)]
trait NftContract {
    fn nft_transfer_payout(
        &mut self,
        receiver_id: AccountId,
        token_id: TokenId,
        approval_id: Option<u64>,
        memo: Option<String>,
        balance: U128,
        max_len_payout: Option<u32>,
    ) -> Payout;
    fn nft_token(&self, token_id: TokenId) -> Option<TokenView>;
    fn nft_transfer(&mut self, receiver_id: AccountId, token_id: TokenId, approval_id: Option<u64>, memo: Option<String>);
    fn get_item(&self, token_id: TokenId) -> Option<ItemView>;
}

/// The fields of a Farmverse item the market checks (other fields are ignored).
#[near(serializers = [json])]
#[derive(Clone, Debug)]
pub struct ItemView {
    pub kind: String,
    pub item_id: String,
    pub quantity: u64,
}

#[ext_contract(ext_ft)]
#[allow(dead_code)]
trait FtContract {
    fn ft_transfer(&mut self, receiver_id: AccountId, amount: U128, memo: Option<String>);
}

#[derive(BorshStorageKey)]
#[near]
enum StorageKey {
    Listings,
    ListingCount,
    StorageDeposits,
    Owed,
    NftContracts,
    // v2
    Offers,
    OfferKeys,
    Rentals,
    Reserved,
}

/// The state as deployed before offers and rentals (v1). Only [`Contract::migrate`] reads it.
#[near(serializers = [borsh])]
struct ContractV1 {
    owner_id: AccountId,
    pending_owner_id: Option<AccountId>,
    ft_token_id: AccountId,
    treasury_id: AccountId,
    fee_bps: u32,
    nft_contracts: IterableSet<AccountId>,
    listings: IterableMap<String, Listing>,
    listing_count: LookupMap<AccountId, u32>,
    storage_deposits: LookupMap<AccountId, NearToken>,
    owed: LookupMap<AccountId, u128>,
    paused: bool,
}

#[derive(PanicOnDefault)]
#[near(contract_state)]
pub struct Contract {
    owner_id: AccountId,
    pending_owner_id: Option<AccountId>,
    /// FARM. Fixed at init.
    ft_token_id: AccountId,
    treasury_id: AccountId,
    fee_bps: u32,
    nft_contracts: IterableSet<AccountId>,
    /// `"<nft_contract_id>:<token_id>"` → listing (account ids can't contain ':').
    listings: IterableMap<String, Listing>,
    listing_count: LookupMap<AccountId, u32>,
    storage_deposits: LookupMap<AccountId, NearToken>,
    /// FARM a payout couldn't deliver, waiting for `withdraw_owed`.
    owed: LookupMap<AccountId, u128>,
    paused: bool,
    // ---- v2: offers and rentals
    offers: IterableMap<u64, Offer>,
    next_offer_id: u64,
    /// `"<buyer>|<nft_contract_id>:<token_id or #item_id>"` → the buyer's live offer there (one each).
    offer_keys: LookupMap<String, u64>,
    /// `"<nft_contract_id>:<token_id>"` → a worker held for rent.
    rentals: IterableMap<String, Rental>,
    /// Offers + rentals each account has open (each reserves `STORAGE_PER_LISTING`).
    reserved: LookupMap<AccountId, u32>,
}

fn key(nft_contract_id: &AccountId, token_id: &str) -> String {
    format!("{nft_contract_id}:{token_id}")
}

fn emit(event: &str, data: near_sdk::serde_json::Value) {
    let body = json!({ "standard": "farmverse_market", "version": "1.0.0", "event": event, "data": [data] });
    log!("EVENT_JSON:{}", body);
}

#[near]
impl Contract {
    #[init]
    pub fn new(
        owner_id: AccountId,
        ft_token_id: AccountId,
        treasury_id: AccountId,
        fee_bps: u32,
        nft_contracts: Vec<AccountId>,
    ) -> Self {
        require!(!env::state_exists(), "Already initialized");
        require!(fee_bps <= MAX_FEE_BPS, "Fee may be at most 1000 bps");
        let mut this = Self {
            owner_id,
            pending_owner_id: None,
            ft_token_id,
            treasury_id,
            fee_bps,
            nft_contracts: IterableSet::new(StorageKey::NftContracts),
            listings: IterableMap::new(StorageKey::Listings),
            listing_count: LookupMap::new(StorageKey::ListingCount),
            storage_deposits: LookupMap::new(StorageKey::StorageDeposits),
            owed: LookupMap::new(StorageKey::Owed),
            paused: false,
            offers: IterableMap::new(StorageKey::Offers),
            next_offer_id: 1,
            offer_keys: LookupMap::new(StorageKey::OfferKeys),
            rentals: IterableMap::new(StorageKey::Rentals),
            reserved: LookupMap::new(StorageKey::Reserved),
        };
        for c in nft_contracts {
            this.nft_contracts.insert(c);
        }
        this
    }

    /// Upgrades v1 state (listings only) in place. Called by the contract account right after deploying
    /// the new code; a second call fails (v2 state doesn't parse as v1).
    #[private]
    #[init(ignore_state)]
    pub fn migrate() -> Self {
        let old: ContractV1 = env::state_read().unwrap_or_else(|| env::panic_str("No v1 state to migrate"));
        Self {
            owner_id: old.owner_id,
            pending_owner_id: old.pending_owner_id,
            ft_token_id: old.ft_token_id,
            treasury_id: old.treasury_id,
            fee_bps: old.fee_bps,
            nft_contracts: old.nft_contracts,
            listings: old.listings,
            listing_count: old.listing_count,
            storage_deposits: old.storage_deposits,
            owed: old.owed,
            paused: old.paused,
            offers: IterableMap::new(StorageKey::Offers),
            next_offer_id: 1,
            offer_keys: LookupMap::new(StorageKey::OfferKeys),
            rentals: IterableMap::new(StorageKey::Rentals),
            reserved: LookupMap::new(StorageKey::Reserved),
        }
    }

    // ---------------------------------------------------------------- storage

    /// Pays for listing storage (each listing reserves `STORAGE_PER_LISTING`).
    #[payable]
    pub fn storage_deposit(&mut self, account_id: Option<AccountId>) -> U128 {
        let account = account_id.unwrap_or_else(env::predecessor_account_id);
        let deposit = env::attached_deposit();
        require!(deposit >= STORAGE_PER_LISTING, "Deposit at least the cost of one listing");
        let total = self.storage_balance(&account).saturating_add(deposit);
        self.storage_deposits.insert(account, total);
        U128(total.as_yoctonear())
    }

    /// Returns the storage deposit not reserved by active listings.
    #[payable]
    pub fn storage_withdraw(&mut self) -> U128 {
        assert_one_yocto();
        let account = env::predecessor_account_id();
        let free = self.storage_free(&account);
        require!(!free.is_zero(), "Nothing to withdraw");
        let left = self.storage_balance(&account).saturating_sub(free);
        if left.is_zero() {
            self.storage_deposits.remove(&account);
        } else {
            self.storage_deposits.insert(account.clone(), left);
        }
        Promise::new(account).transfer(free).detach();
        U128(free.as_yoctonear())
    }

    pub fn storage_minimum_balance(&self) -> U128 {
        U128(STORAGE_PER_LISTING.as_yoctonear())
    }

    pub fn storage_balance_of(&self, account_id: AccountId) -> U128 {
        U128(self.storage_balance(&account_id).as_yoctonear())
    }

    // ---------------------------------------------------------------- seller actions

    #[payable]
    pub fn delist(&mut self, nft_contract_id: AccountId, token_id: TokenId) {
        assert_one_yocto();
        let listing = self.listing_or_panic(&nft_contract_id, &token_id);
        require!(listing.owner_id == env::predecessor_account_id(), "Only the seller");
        self.take_listing(&nft_contract_id, &token_id);
        emit("market_delist", json!({ "nft_contract_id": nft_contract_id, "token_id": token_id, "reason": "seller" }));
    }

    #[payable]
    pub fn update_price(&mut self, nft_contract_id: AccountId, token_id: TokenId, price: U128) {
        assert_one_yocto();
        self.assert_not_paused();
        require!(price.0 > 0, "price must be positive");
        let k = key(&nft_contract_id, &token_id);
        let listing = self.listings.get_mut(&k).unwrap_or_else(|| env::panic_str("Not listed"));
        require!(listing.owner_id == env::predecessor_account_id(), "Only the seller");
        listing.price = price;
        let snapshot = listing.clone();
        emit("market_list", json!(snapshot));
    }

    /// Anyone may clear a listing whose token moved, burned or lost its approval.
    pub fn remove_stale(&mut self, nft_contract_id: AccountId, token_id: TokenId) -> Promise {
        self.listing_or_panic(&nft_contract_id, &token_id);
        ext_nft::ext(nft_contract_id.clone())
            .with_static_gas(GAS_NFT_TOKEN)
            .nft_token(token_id.clone())
            .then(Self::ext(env::current_account_id()).with_static_gas(GAS_RESOLVE_STALE).resolve_stale(nft_contract_id, token_id))
    }

    #[private]
    pub fn resolve_stale(
        &mut self,
        nft_contract_id: AccountId,
        token_id: TokenId,
        #[callback_result] token: Result<Option<TokenView>, PromiseError>,
    ) -> bool {
        let Some(listing) = self.listings.get(&key(&nft_contract_id, &token_id)).cloned() else { return false };
        let Ok(token) = token else { return false }; // couldn't ask: leave it
        let live = match token {
            None => false,
            Some(t) => {
                t.owner_id == listing.owner_id
                    && t.approved_account_ids
                        .as_ref()
                        .and_then(|a| a.get(&env::current_account_id()))
                        .is_some_and(|id| *id == listing.approval_id)
            }
        };
        if !live {
            self.take_listing(&nft_contract_id, &token_id);
            emit("market_delist", json!({ "nft_contract_id": nft_contract_id, "token_id": token_id, "reason": "stale" }));
        }
        !live
    }

    // ---------------------------------------------------------------- settlement callbacks

    /// Pays a finished sale out; returns the FARM to refund to the buyer (all of it if the NFT
    /// didn't move). Must never panic: a panic here would refund the buyer after they got the NFT.
    #[private]
    pub fn resolve_purchase(
        &mut self,
        buyer_id: AccountId,
        listing: Listing,
        price: U128,
        fee: U128,
        #[callback_result] payout: Result<Payout, PromiseError>,
    ) -> U128 {
        let Ok(payout) = payout else {
            emit("market_sale_failed", json!({ "nft_contract_id": listing.nft_contract_id, "token_id": listing.token_id, "buyer_id": buyer_id, "price": price }));
            return price;
        };
        let valid = self.settle(&listing.owner_id, price.0 - fee.0, fee.0, payout);
        emit(
            "market_sale",
            json!({
                "nft_contract_id": listing.nft_contract_id,
                "token_id": listing.token_id,
                "seller_id": listing.owner_id,
                "buyer_id": buyer_id,
                "price": price,
                "fee": fee,
                "payout_valid": valid,
            }),
        );
        U128(0)
    }

    /// A payment's outcome: an undelivered amount is kept for `withdraw_owed`.
    #[private]
    pub fn on_payment(&mut self, account_id: AccountId, amount: U128, #[callback_result] result: Result<(), PromiseError>) {
        if result.is_err() {
            let owed = self.owed.get(&account_id).copied().unwrap_or(0).saturating_add(amount.0);
            self.owed.insert(account_id.clone(), owed);
            emit("market_owed", json!({ "account_id": account_id, "amount": amount }));
        }
    }

    /// Sends FARM the market owes the caller (register with the FARM contract first).
    pub fn withdraw_owed(&mut self) -> Promise {
        let account = env::predecessor_account_id();
        let amount = self.owed.remove(&account).unwrap_or_else(|| env::panic_str("Nothing owed"));
        self.pay(account, amount)
    }

    // ---------------------------------------------------------------- views

    pub fn get_listing(&self, nft_contract_id: AccountId, token_id: TokenId) -> Option<Listing> {
        self.listings.get(&key(&nft_contract_id, &token_id)).cloned()
    }

    pub fn get_listings(&self, from_index: Option<u32>, limit: Option<u32>) -> Vec<Listing> {
        let from = from_index.unwrap_or(0) as usize;
        let limit = limit.unwrap_or(50).min(100) as usize;
        self.listings.values().skip(from).take(limit).cloned().collect()
    }

    pub fn get_listings_count(&self) -> u32 {
        self.listings.len()
    }

    pub fn listing_count_of(&self, account_id: AccountId) -> u32 {
        self.listing_count.get(&account_id).copied().unwrap_or(0)
    }

    pub fn owed_of(&self, account_id: AccountId) -> U128 {
        U128(self.owed.get(&account_id).copied().unwrap_or(0))
    }

    pub fn get_config(&self) -> Config {
        Config {
            owner_id: self.owner_id.clone(),
            pending_owner_id: self.pending_owner_id.clone(),
            ft_token_id: self.ft_token_id.clone(),
            treasury_id: self.treasury_id.clone(),
            fee_bps: self.fee_bps,
            nft_contracts: self.nft_contracts.iter().cloned().collect(),
            paused: self.paused,
        }
    }

    // ---------------------------------------------------------------- admin (owner, 1 yocto)

    #[payable]
    pub fn set_fee_bps(&mut self, fee_bps: u32) {
        self.assert_owner();
        require!(fee_bps <= MAX_FEE_BPS, "Fee may be at most 1000 bps");
        self.fee_bps = fee_bps;
        emit("config_change", json!({ "field": "fee_bps", "value": fee_bps }));
    }

    #[payable]
    pub fn set_treasury(&mut self, treasury_id: AccountId) {
        self.assert_owner();
        emit("config_change", json!({ "field": "treasury_id", "value": treasury_id }));
        self.treasury_id = treasury_id;
    }

    #[payable]
    pub fn add_nft_contract(&mut self, nft_contract_id: AccountId) {
        self.assert_owner();
        require!(self.nft_contracts.insert(nft_contract_id.clone()), "Already accepted");
        emit("config_change", json!({ "field": "nft_contract_added", "value": nft_contract_id }));
    }

    /// Stops new listings from that contract; existing ones can still sell or be delisted.
    #[payable]
    pub fn remove_nft_contract(&mut self, nft_contract_id: AccountId) {
        self.assert_owner();
        require!(self.nft_contracts.remove(&nft_contract_id), "Not accepted");
        emit("config_change", json!({ "field": "nft_contract_removed", "value": nft_contract_id }));
    }

    /// Stops listing, relisting and buying. Delisting and withdrawals always work.
    #[payable]
    pub fn set_paused(&mut self, paused: bool) {
        self.assert_owner();
        self.paused = paused;
        emit("config_change", json!({ "field": "paused", "value": paused }));
    }

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
        self.owner_id = caller;
        self.pending_owner_id = None;
    }
}

#[near]
impl NonFungibleTokenApprovalReceiver for Contract {
    /// Lists (or relists at a new price) a token its owner just approved this market for.
    fn nft_on_approve(&mut self, token_id: TokenId, owner_id: AccountId, approval_id: u64, msg: String) -> PromiseOrValue<String> {
        self.assert_not_paused();
        let nft_contract_id = env::predecessor_account_id();
        require!(self.nft_contracts.contains(&nft_contract_id), "NFT contract not accepted");
        if let Ok(accept) = serde_json::from_str::<offers::AcceptArgs>(&msg) {
            return self.accept_offer(nft_contract_id, token_id, owner_id, approval_id, accept.accept_offer);
        }
        let args: ListArgs = serde_json::from_str(&msg).unwrap_or_else(|_| env::panic_str("msg must be {\"price\":\"<amount>\"}"));
        require!(args.price.0 > 0, "price must be positive");

        let k = key(&nft_contract_id, &token_id);
        let previous = self.listings.get(&k).cloned();
        if let Some(prev) = previous.as_ref().filter(|p| p.owner_id != owner_id) {
            // The token changed hands since that listing: it's stale, free the old seller's slot.
            self.dec_count(&prev.owner_id);
        }
        let relist_same_owner = previous.as_ref().is_some_and(|p| p.owner_id == owner_id);
        if !relist_same_owner {
            let needed = STORAGE_PER_LISTING.saturating_mul(self.listing_count_of(owner_id.clone()) as u128 + 1);
            require!(self.storage_balance(&owner_id) >= needed, "Deposit storage for the listing first (storage_deposit)");
            self.inc_count(&owner_id);
        }
        let listing = Listing {
            nft_contract_id,
            token_id,
            owner_id,
            approval_id,
            price: args.price,
            listed_at_ms: env::block_timestamp_ms(),
        };
        emit("market_list", json!(listing));
        self.listings.insert(k, listing);
        PromiseOrValue::Value("listed".to_string())
    }
}

#[near]
impl FungibleTokenReceiver for Contract {
    /// Buys a listing with FARM. Panicking refunds the buyer (FT standard).
    fn ft_on_transfer(&mut self, sender_id: AccountId, amount: U128, msg: String) -> PromiseOrValue<U128> {
        require!(env::predecessor_account_id() == self.ft_token_id, "Only FARM is accepted");
        self.assert_not_paused();
        if let Ok(o) = serde_json::from_str::<offers::OfferMsg>(&msg) {
            self.make_offer(sender_id, amount, o.offer);
            return PromiseOrValue::Value(U128(0));
        }
        if let Ok(r) = serde_json::from_str::<rentals::RentMsg>(&msg) {
            self.start_rental(sender_id, amount, r.rent);
            return PromiseOrValue::Value(U128(0));
        }
        let args: BuyArgs = serde_json::from_str(&msg)
            .unwrap_or_else(|_| env::panic_str("msg must be {\"nft_contract_id\":…,\"token_id\":…}"));
        let listing = self.listing_or_panic(&args.nft_contract_id, &args.token_id);
        require!(amount == listing.price, "Amount must equal the listing price");
        require!(sender_id != listing.owner_id, "Seller can't buy their own listing");
        require!(
            env::prepaid_gas().saturating_sub(env::used_gas())
                >= GAS_NFT_TRANSFER_PAYOUT.saturating_add(GAS_RESOLVE_PURCHASE).saturating_add(Gas::from_tgas(10)),
            "Attach more gas (300 TGas recommended)"
        );

        // Off the book before anything async happens: a second buyer now fails and is refunded.
        self.take_listing(&args.nft_contract_id, &args.token_id);
        let fee = self.fee_of(amount.0);
        let net = amount.0 - fee;

        ext_nft::ext(listing.nft_contract_id.clone())
            .with_attached_deposit(ONE_YOCTO)
            .with_static_gas(GAS_NFT_TRANSFER_PAYOUT)
            .nft_transfer_payout(
                sender_id.clone(),
                listing.token_id.clone(),
                Some(listing.approval_id),
                Some("farmverse market sale".to_string()),
                U128(net),
                Some(MAX_LEN_PAYOUT),
            )
            .then(
                Self::ext(env::current_account_id())
                    .with_static_gas(GAS_RESOLVE_PURCHASE)
                    .resolve_purchase(sender_id, listing, amount, U128(fee)),
            )
            .into()
    }
}

impl Contract {
    fn assert_owner(&self) {
        assert_one_yocto();
        require!(env::predecessor_account_id() == self.owner_id, "Only the owner");
    }

    fn assert_not_paused(&self) {
        require!(!self.paused, "Paused");
    }

    fn listing_or_panic(&self, nft_contract_id: &AccountId, token_id: &str) -> Listing {
        self.listings.get(&key(nft_contract_id, token_id)).cloned().unwrap_or_else(|| env::panic_str("Not listed"))
    }

    fn take_listing(&mut self, nft_contract_id: &AccountId, token_id: &str) -> Option<Listing> {
        let listing = self.listings.remove(&key(nft_contract_id, token_id))?;
        self.dec_count(&listing.owner_id);
        Some(listing)
    }

    fn inc_count(&mut self, account: &AccountId) {
        let n = self.listing_count_of(account.clone());
        self.listing_count.insert(account.clone(), n + 1);
    }

    fn dec_count(&mut self, account: &AccountId) {
        let n = self.listing_count_of(account.clone());
        if n <= 1 {
            self.listing_count.remove(account);
        } else {
            self.listing_count.insert(account.clone(), n - 1);
        }
    }

    fn storage_balance(&self, account: &AccountId) -> NearToken {
        self.storage_deposits.get(account).copied().unwrap_or(NearToken::from_yoctonear(0))
    }

    fn storage_free(&self, account: &AccountId) -> NearToken {
        let slots = self.listing_count_of(account.clone()) as u128 + self.reserved_of(account) as u128;
        self.storage_balance(account).saturating_sub(STORAGE_PER_LISTING.saturating_mul(slots))
    }

    fn reserved_of(&self, account: &AccountId) -> u32 {
        self.reserved.get(account).copied().unwrap_or(0)
    }

    /// Reserves one storage slot for an offer or rental (panics without the deposit for it).
    fn reserve(&mut self, account: &AccountId) {
        require!(
            self.storage_free(account) >= STORAGE_PER_LISTING,
            "Deposit storage first (storage_deposit, 0.01 NEAR per open listing, offer or rental)"
        );
        let n = self.reserved_of(account);
        self.reserved.insert(account.clone(), n + 1);
    }

    fn release(&mut self, account: &AccountId) {
        let n = self.reserved_of(account);
        if n <= 1 {
            self.reserved.remove(account);
        } else {
            self.reserved.insert(account.clone(), n - 1);
        }
    }

    /// Pays a finished sale (listing or offer): NEP-199 payout of `net`, the remainder to `seller`,
    /// `fee` to the treasury. Returns whether the payout was valid (a broken one pays the seller all).
    fn settle(&mut self, seller: &AccountId, net: u128, fee: u128, payout: Payout) -> bool {
        let valid = payout.payout.len() as u32 <= MAX_LEN_PAYOUT
            && payout.payout.values().try_fold(0u128, |a, v| a.checked_add(v.0)).is_some_and(|sum| sum <= net);
        let mut payments: Vec<(AccountId, u128)> = if valid {
            payout.payout.into_iter().map(|(a, v)| (a, v.0)).filter(|(_, v)| *v > 0).collect()
        } else {
            vec![]
        };
        let paid: u128 = payments.iter().map(|(_, v)| *v).sum();
        if net > paid {
            payments.push((seller.clone(), net - paid));
        }
        if fee > 0 {
            payments.push((self.treasury_id.clone(), fee));
        }
        for (account, amount) in &payments {
            self.pay(account.clone(), *amount).detach();
        }
        valid
    }

    fn fee_of(&self, amount: u128) -> u128 {
        amount * self.fee_bps as u128 / BPS
    }

    fn pay(&mut self, account: AccountId, amount: u128) -> Promise {
        ext_ft::ext(self.ft_token_id.clone())
            .with_attached_deposit(ONE_YOCTO)
            .with_static_gas(GAS_FT_TRANSFER)
            .ft_transfer(account.clone(), U128(amount), Some("farmverse market".to_string()))
            .then(Self::ext(env::current_account_id()).with_static_gas(GAS_ON_PAYMENT).on_payment(account, U128(amount)))
    }
}

#[cfg(all(test, not(target_arch = "wasm32")))]
mod tests;
