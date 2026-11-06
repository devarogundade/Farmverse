//! Offers: a buyer escrows FARM for one token, or for any token of an item ("any Wheat, at least ×100").
//!
//! * **Make:** `farm.ft_transfer_call(market, amount, msg: {"offer": {"nft_contract_id", "token_id" | "item_id",
//!   "min_quantity"?, "expires_at_ms"}})`. One live offer per buyer per token (or item): a new one replaces the
//!   old, whose FARM goes back to the buyer.
//! * **Accept:** the token's owner calls `nft_approve(token_id, market, msg: {"accept_offer": <id>})`. The NFT
//!   moves to the buyer with `nft_transfer_payout`, and the escrow pays the seller, royalties and the fee. For an
//!   item offer the market first reads the token's item to check it matches. If the transfer fails the offer
//!   stays open.
//! * **Cancel:** the buyer any time (1 yocto), anyone once it expired; the FARM goes back to the buyer.

use near_contract_standards::non_fungible_token::TokenId;
use near_sdk::json_types::U128;
use near_sdk::serde::Deserialize;
use near_sdk::serde_json::json;
use near_sdk::{AccountId, Gas, Promise, PromiseError, PromiseOrValue, assert_one_yocto, env, near, require};

use crate::{
    Contract, ContractExt, GAS_GET_ITEM, GAS_NFT_TRANSFER_PAYOUT, ItemView, MAX_LEN_PAYOUT, ONE_YOCTO, Payout, emit, ext_nft,
    key,
};

/// The longest an offer may stay open.
pub const MAX_OFFER_TTL_MS: u64 = 30 * 24 * 60 * 60 * 1000;
pub const MAX_OFFERS_PAGE: u32 = 100;
const GAS_RESOLVE_OFFER: Gas = Gas::from_tgas(110);
const GAS_CHECK_ITEM_OFFER: Gas = Gas::from_tgas(170);

#[near(serializers = [borsh, json])]
#[derive(Clone, Debug, PartialEq)]
pub struct Offer {
    pub id: u64,
    pub buyer_id: AccountId,
    pub nft_contract_id: AccountId,
    /// An offer on one token …
    pub token_id: Option<TokenId>,
    /// … or on any token holding this item.
    pub item_id: Option<String>,
    /// Item offers: the token must hold at least this many units.
    pub min_quantity: Option<u64>,
    pub amount: U128,
    pub made_at_ms: u64,
    pub expires_at_ms: u64,
}

#[derive(Deserialize)]
#[serde(crate = "near_sdk::serde")]
pub struct OfferArgs {
    pub nft_contract_id: AccountId,
    pub token_id: Option<TokenId>,
    pub item_id: Option<String>,
    pub min_quantity: Option<u64>,
    pub expires_at_ms: u64,
}

#[derive(Deserialize)]
#[serde(crate = "near_sdk::serde")]
pub struct OfferMsg {
    pub offer: OfferArgs,
}

#[derive(Deserialize)]
#[serde(crate = "near_sdk::serde")]
pub struct AcceptArgs {
    pub accept_offer: u64,
}

fn offer_key(buyer: &AccountId, nft_contract_id: &AccountId, token_id: Option<&str>, item_id: Option<&str>) -> String {
    match (token_id, item_id) {
        (Some(t), _) => format!("{buyer}|{}", key(nft_contract_id, t)),
        (None, Some(i)) => format!("{buyer}|{nft_contract_id}:#{i}"),
        (None, None) => env::panic_str("offer needs token_id or item_id"),
    }
}

impl Offer {
    fn key(&self) -> String {
        offer_key(&self.buyer_id, &self.nft_contract_id, self.token_id.as_deref(), self.item_id.as_deref())
    }
    fn live(&self, now_ms: u64) -> bool {
        now_ms < self.expires_at_ms
    }
}

#[near]
impl Contract {
    /// Cancels an offer and returns its FARM to the buyer: the buyer any time (1 yocto), anyone once it expired.
    #[payable]
    pub fn offer_cancel(&mut self, offer_id: u64) -> Promise {
        let offer = self.offers.get(&offer_id).cloned().unwrap_or_else(|| env::panic_str("No such offer"));
        if env::predecessor_account_id() == offer.buyer_id {
            assert_one_yocto();
        } else {
            require!(!offer.live(env::block_timestamp_ms()), "Only the buyer can cancel a live offer");
        }
        self.take_offer(offer_id);
        emit("offer_cancel", json!({ "id": offer_id, "buyer_id": offer.buyer_id, "amount": offer.amount }));
        self.pay(offer.buyer_id, offer.amount.0)
    }

    pub fn get_offer(&self, offer_id: u64) -> Option<Offer> {
        self.offers.get(&offer_id).cloned()
    }

    pub fn get_offers(&self, from_index: Option<u32>, limit: Option<u32>) -> Vec<Offer> {
        let limit = limit.unwrap_or(MAX_OFFERS_PAGE).min(MAX_OFFERS_PAGE) as usize;
        self.offers.values().skip(from_index.unwrap_or(0) as usize).take(limit).cloned().collect()
    }

    pub fn get_offers_count(&self) -> u32 {
        self.offers.len()
    }

    /// Live offers on one token (highest first).
    pub fn offers_for_token(&self, nft_contract_id: AccountId, token_id: TokenId) -> Vec<Offer> {
        self.find_offers(|o| o.nft_contract_id == nft_contract_id && o.token_id.as_ref() == Some(&token_id))
    }

    /// Live offers on any token of an item (highest first).
    pub fn offers_for_item(&self, nft_contract_id: AccountId, item_id: String) -> Vec<Offer> {
        self.find_offers(|o| o.nft_contract_id == nft_contract_id && o.item_id.as_ref() == Some(&item_id))
    }

    pub fn offers_of(&self, account_id: AccountId) -> Vec<Offer> {
        self.find_offers(|o| o.buyer_id == account_id)
    }

    /// Item offers: the item is read; if it matches, the accept goes on like a token offer.
    #[private]
    pub fn check_item_offer(
        &mut self,
        offer_id: u64,
        token_id: TokenId,
        owner_id: AccountId,
        approval_id: u64,
        #[callback_result] item: Result<Option<ItemView>, PromiseError>,
    ) -> PromiseOrValue<String> {
        let Some(offer) = self.offers.get(&offer_id).cloned() else {
            emit("offer_accept_failed", json!({ "id": offer_id, "token_id": token_id, "reason": "gone" }));
            return PromiseOrValue::Value("offer gone".into());
        };
        let matches = match item {
            Ok(Some(i)) => {
                Some(&i.item_id) == offer.item_id.as_ref() && offer.min_quantity.is_none_or(|q| i.quantity >= q)
            }
            _ => false,
        };
        if !matches || !offer.live(env::block_timestamp_ms()) {
            emit("offer_accept_failed", json!({ "id": offer_id, "token_id": token_id, "reason": "no match" }));
            return PromiseOrValue::Value("no match".into());
        }
        self.settle_offer(offer, token_id, owner_id, approval_id)
    }

    /// Pays an accepted offer out, or puts it back if the NFT didn't move. Must never panic.
    #[private]
    pub fn resolve_offer(
        &mut self,
        offer: Offer,
        token_id: TokenId,
        seller_id: AccountId,
        fee: U128,
        #[callback_result] payout: Result<Payout, PromiseError>,
    ) -> bool {
        let Ok(payout) = payout else {
            // The NFT didn't move: the offer is open again (the buyer's slot comes back with it).
            let k = offer.key();
            if !self.offer_keys.contains_key(&k) {
                self.offer_keys.insert(k, offer.id);
                let n = self.reserved_of(&offer.buyer_id);
                self.reserved.insert(offer.buyer_id.clone(), n + 1);
                self.offers.insert(offer.id, offer.clone());
            } else {
                // The buyer made a new offer meanwhile: this one's FARM goes back.
                self.pay(offer.buyer_id.clone(), offer.amount.0).detach();
            }
            emit("offer_accept_failed", json!({ "id": offer.id, "token_id": token_id, "reason": "transfer" }));
            return false;
        };
        let valid = self.settle(&seller_id, offer.amount.0 - fee.0, fee.0, payout);
        emit(
            "offer_accept",
            json!({
                "id": offer.id,
                "nft_contract_id": offer.nft_contract_id,
                "token_id": token_id,
                "seller_id": seller_id,
                "buyer_id": offer.buyer_id,
                "price": offer.amount,
                "fee": fee,
                "payout_valid": valid,
            }),
        );
        true
    }
}

impl Contract {
    pub(crate) fn make_offer(&mut self, buyer: AccountId, amount: U128, args: OfferArgs) {
        require!(self.nft_contracts.contains(&args.nft_contract_id), "NFT contract not accepted");
        require!(amount.0 > 0, "Offer must be above zero");
        require!(args.token_id.is_some() != args.item_id.is_some(), "Offer on a token_id or an item_id, not both");
        if args.token_id.is_some() {
            require!(args.min_quantity.is_none(), "min_quantity is for item offers");
        }
        let now = env::block_timestamp_ms();
        require!(args.expires_at_ms > now, "Offer already expired");
        require!(args.expires_at_ms <= now.saturating_add(MAX_OFFER_TTL_MS), "Offers last at most 30 days");
        if let Some(listing) = args.token_id.as_ref().and_then(|t| self.listings.get(&key(&args.nft_contract_id, t))) {
            require!(listing.owner_id != buyer, "That's your own token");
        }

        let k = offer_key(&buyer, &args.nft_contract_id, args.token_id.as_deref(), args.item_id.as_deref());
        if let Some(old_id) = self.offer_keys.get(&k).copied() {
            // Replaces the buyer's offer there: the old escrow goes back, the slot is reused.
            if let Some(old) = self.offers.remove(&old_id) {
                emit("offer_cancel", json!({ "id": old_id, "buyer_id": old.buyer_id, "amount": old.amount, "reason": "replaced" }));
                self.pay(old.buyer_id, old.amount.0).detach();
            }
        } else {
            self.reserve(&buyer);
        }
        let offer = Offer {
            id: self.next_offer_id,
            buyer_id: buyer,
            nft_contract_id: args.nft_contract_id,
            token_id: args.token_id,
            item_id: args.item_id,
            min_quantity: args.min_quantity,
            amount,
            made_at_ms: now,
            expires_at_ms: args.expires_at_ms,
        };
        self.next_offer_id += 1;
        self.offer_keys.insert(k, offer.id);
        emit("offer_make", json!(offer));
        self.offers.insert(offer.id, offer);
    }

    pub(crate) fn accept_offer(
        &mut self,
        nft_contract_id: AccountId,
        token_id: TokenId,
        owner_id: AccountId,
        approval_id: u64,
        offer_id: u64,
    ) -> PromiseOrValue<String> {
        let offer = self.offers.get(&offer_id).cloned().unwrap_or_else(|| env::panic_str("No such offer"));
        require!(offer.nft_contract_id == nft_contract_id, "Offer is for another collection");
        require!(offer.live(env::block_timestamp_ms()), "Offer expired");
        require!(offer.buyer_id != owner_id, "That's your own offer");
        require!(
            env::prepaid_gas().saturating_sub(env::used_gas()) >= GAS_GET_ITEM.saturating_add(GAS_CHECK_ITEM_OFFER),
            "Attach more gas (300 TGas recommended)"
        );
        match (&offer.token_id, &offer.item_id) {
            (Some(t), _) => {
                require!(*t == token_id, "Offer is for another token");
                self.settle_offer(offer, token_id, owner_id, approval_id)
            }
            _ => ext_nft::ext(nft_contract_id)
                .with_static_gas(GAS_GET_ITEM)
                .get_item(token_id.clone())
                .then(
                    Self::ext(env::current_account_id())
                        .with_static_gas(GAS_CHECK_ITEM_OFFER)
                        .check_item_offer(offer_id, token_id, owner_id, approval_id),
                )
                .into(),
        }
    }

    /// Takes the offer (and any listing of the token) off the book, then moves the NFT to the buyer.
    fn settle_offer(&mut self, offer: Offer, token_id: TokenId, owner_id: AccountId, approval_id: u64) -> PromiseOrValue<String> {
        self.take_offer(offer.id);
        if self.take_listing(&offer.nft_contract_id, &token_id).is_some() {
            emit("market_delist", json!({ "nft_contract_id": offer.nft_contract_id, "token_id": token_id, "reason": "offer_accepted" }));
        }
        let fee = self.fee_of(offer.amount.0);
        ext_nft::ext(offer.nft_contract_id.clone())
            .with_attached_deposit(ONE_YOCTO)
            .with_static_gas(GAS_NFT_TRANSFER_PAYOUT)
            .nft_transfer_payout(
                offer.buyer_id.clone(),
                token_id.clone(),
                Some(approval_id),
                Some("farmverse offer accepted".to_string()),
                U128(offer.amount.0 - fee),
                Some(MAX_LEN_PAYOUT),
            )
            .then(
                Self::ext(env::current_account_id())
                    .with_static_gas(GAS_RESOLVE_OFFER)
                    .resolve_offer(offer, token_id, owner_id, U128(fee)),
            )
            .into()
    }

    fn take_offer(&mut self, offer_id: u64) -> Option<Offer> {
        let offer = self.offers.remove(&offer_id)?;
        self.offer_keys.remove(&offer.key());
        self.release(&offer.buyer_id);
        Some(offer)
    }

    fn find_offers(&self, pred: impl Fn(&Offer) -> bool) -> Vec<Offer> {
        let now = env::block_timestamp_ms();
        let mut found: Vec<Offer> = self.offers.values().filter(|o| o.live(now) && pred(o)).take(MAX_OFFERS_PAGE as usize).cloned().collect();
        found.sort_by(|a, b| b.amount.0.cmp(&a.amount.0));
        found
    }
}
