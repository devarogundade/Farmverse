//! Rentals: a lender hands a worker to the market; renters pay FARM per day to have it work on their island.
//!
//! * **Rent out:** `items.nft_transfer_call(market, token_id, msg: {"rent": {"price_per_day", "max_days"}})`.
//!   The market reads the token's item and keeps only workers (anything else goes straight back).
//!   While nobody rents it, the worker keeps working for the lender (the game reads [`Contract::get_rental`]).
//! * **Rent:** `farm.ft_transfer_call(market, price_per_day × days, msg: {"rent": {"nft_contract_id", "token_id",
//!   "days"}})`. The fee goes to the treasury; the rest is added to the **worker's own balance** (`earned`).
//! * **Withdraw:** the lender moves `earned` out at any time. **End:** once no rental is running, the lender
//!   takes the worker back (with whatever it earned).

use near_contract_standards::non_fungible_token::TokenId;
use near_contract_standards::non_fungible_token::core::NonFungibleTokenReceiver;
use near_sdk::json_types::U128;
use near_sdk::serde::Deserialize;
use near_sdk::serde_json::{self, json};
use near_sdk::{AccountId, Gas, PromiseError, PromiseOrValue, assert_one_yocto, env, near, require};

use crate::{Contract, ContractExt, GAS_GET_ITEM, GAS_NFT_TRANSFER, ItemView, ONE_YOCTO, emit, ext_nft, key};

pub const DAY_MS: u64 = 24 * 60 * 60 * 1000;
pub const MAX_RENT_DAYS: u32 = 30;
pub const MAX_RENTALS_PAGE: u32 = 100;
const GAS_RESOLVE_RENT_LIST: Gas = Gas::from_tgas(15);
const GAS_RESOLVE_RENT_END: Gas = Gas::from_tgas(15);
/// The item kind the market rents (as the items contract writes it).
const WORKER: &str = "worker";

#[near(serializers = [borsh, json])]
#[derive(Clone, Debug, PartialEq)]
pub struct Rental {
    pub nft_contract_id: AccountId,
    pub token_id: TokenId,
    pub lender_id: AccountId,
    pub price_per_day: U128,
    pub max_days: u32,
    /// The current (or last) renter.
    pub renter_id: Option<AccountId>,
    /// The rental runs while `now < rented_until_ms`.
    pub rented_until_ms: u64,
    /// The worker's balance: FARM renters paid, waiting for the lender to withdraw.
    pub earned: U128,
    pub listed_at_ms: u64,
}

impl Rental {
    pub fn rented(&self, now_ms: u64) -> bool {
        now_ms < self.rented_until_ms
    }
}

#[derive(Deserialize)]
#[serde(crate = "near_sdk::serde")]
pub struct RentTerms {
    pub price_per_day: U128,
    pub max_days: u32,
}

#[derive(Deserialize)]
#[serde(crate = "near_sdk::serde")]
struct RentListMsg {
    rent: RentTerms,
}

#[derive(Deserialize)]
#[serde(crate = "near_sdk::serde")]
pub struct RentArgs {
    pub nft_contract_id: AccountId,
    pub token_id: TokenId,
    pub days: u32,
}

#[derive(Deserialize)]
#[serde(crate = "near_sdk::serde")]
pub struct RentMsg {
    pub rent: RentArgs,
}

fn assert_terms(t: &RentTerms) {
    require!(t.price_per_day.0 > 0, "price_per_day must be above zero");
    require!((1..=MAX_RENT_DAYS).contains(&t.max_days), "max_days must be 1-30");
}

#[near]
impl NonFungibleTokenReceiver for Contract {
    /// A lender hands a worker over for rent. Returns `true` (the token goes back) if it isn't a worker.
    fn nft_on_transfer(&mut self, sender_id: AccountId, previous_owner_id: AccountId, token_id: TokenId, msg: String) -> PromiseOrValue<bool> {
        let _ = sender_id;
        self.assert_not_paused();
        let nft_contract_id = env::predecessor_account_id();
        require!(self.nft_contracts.contains(&nft_contract_id), "NFT contract not accepted");
        let terms = serde_json::from_str::<RentListMsg>(&msg)
            .unwrap_or_else(|_| env::panic_str("msg must be {\"rent\":{\"price_per_day\":…,\"max_days\":…}}"))
            .rent;
        assert_terms(&terms);
        // The slot is reserved now (panicking returns the token), and released if the check fails.
        self.reserve(&previous_owner_id);
        ext_nft::ext(nft_contract_id.clone())
            .with_static_gas(GAS_GET_ITEM)
            .get_item(token_id.clone())
            .then(Self::ext(env::current_account_id()).with_static_gas(GAS_RESOLVE_RENT_LIST).resolve_rent_list(
                nft_contract_id,
                token_id,
                previous_owner_id,
                terms.price_per_day,
                terms.max_days,
            ))
            .into()
    }
}

#[near]
impl Contract {
    /// Keeps the token for rent if it is a worker; otherwise it goes back (returns `true`). Never panics.
    #[private]
    pub fn resolve_rent_list(
        &mut self,
        nft_contract_id: AccountId,
        token_id: TokenId,
        lender_id: AccountId,
        price_per_day: U128,
        max_days: u32,
        #[callback_result] item: Result<Option<ItemView>, PromiseError>,
    ) -> bool {
        let is_worker = matches!(&item, Ok(Some(i)) if i.kind == WORKER);
        if !is_worker {
            self.release(&lender_id);
            emit("rent_refused", json!({ "nft_contract_id": nft_contract_id, "token_id": token_id, "lender_id": lender_id }));
            return true;
        }
        let rental = Rental {
            nft_contract_id: nft_contract_id.clone(),
            token_id: token_id.clone(),
            lender_id,
            price_per_day,
            max_days,
            renter_id: None,
            rented_until_ms: 0,
            earned: U128(0),
            listed_at_ms: env::block_timestamp_ms(),
        };
        emit("rent_list", json!(rental));
        self.rentals.insert(key(&nft_contract_id, &token_id), rental);
        false
    }

    /// Changes the price or the longest rental (lender, 1 yocto). A running rental keeps its terms.
    #[payable]
    pub fn rent_update(&mut self, nft_contract_id: AccountId, token_id: TokenId, price_per_day: U128, max_days: u32) -> Rental {
        assert_one_yocto();
        let terms = RentTerms { price_per_day, max_days };
        assert_terms(&terms);
        let k = key(&nft_contract_id, &token_id);
        let r = self.rentals.get_mut(&k).unwrap_or_else(|| env::panic_str("Not for rent"));
        require!(r.lender_id == env::predecessor_account_id(), "Only the lender");
        r.price_per_day = terms.price_per_day;
        r.max_days = terms.max_days;
        let snapshot = r.clone();
        emit("rent_list", json!(snapshot));
        snapshot
    }

    /// Sends the worker's earnings to the lender (lender, 1 yocto).
    #[payable]
    pub fn rent_withdraw(&mut self, nft_contract_id: AccountId, token_id: TokenId) -> U128 {
        assert_one_yocto();
        let k = key(&nft_contract_id, &token_id);
        let r = self.rentals.get_mut(&k).unwrap_or_else(|| env::panic_str("Not for rent"));
        require!(r.lender_id == env::predecessor_account_id(), "Only the lender");
        let amount = r.earned;
        require!(amount.0 > 0, "Nothing earned yet");
        r.earned = U128(0);
        let lender = r.lender_id.clone();
        emit("rent_withdraw", json!({ "nft_contract_id": nft_contract_id, "token_id": token_id, "lender_id": lender, "amount": amount }));
        self.pay(lender, amount.0).detach();
        amount
    }

    /// Takes the worker back once no rental is running, with what it earned (lender, 1 yocto).
    #[payable]
    pub fn rent_end(&mut self, nft_contract_id: AccountId, token_id: TokenId) {
        assert_one_yocto();
        let k = key(&nft_contract_id, &token_id);
        let r = self.rentals.get(&k).cloned().unwrap_or_else(|| env::panic_str("Not for rent"));
        require!(r.lender_id == env::predecessor_account_id(), "Only the lender");
        require!(!r.rented(env::block_timestamp_ms()), "A rental is running until it ends");
        self.rentals.remove(&k);
        self.release(&r.lender_id);
        if r.earned.0 > 0 {
            self.pay(r.lender_id.clone(), r.earned.0).detach();
        }
        let mut back = r.clone();
        back.earned = U128(0);
        ext_nft::ext(nft_contract_id)
            .with_attached_deposit(ONE_YOCTO)
            .with_static_gas(GAS_NFT_TRANSFER)
            .nft_transfer(r.lender_id.clone(), token_id, None, Some("farmverse rental ended".into()))
            .then(Self::ext(env::current_account_id()).with_static_gas(GAS_RESOLVE_RENT_END).resolve_rent_end(back))
            .detach();
    }

    /// If the worker couldn't go back, it stays listed for rent (the lender can try again). Never panics.
    #[private]
    pub fn resolve_rent_end(&mut self, rental: Rental, #[callback_result] result: Result<(), PromiseError>) -> bool {
        let k = key(&rental.nft_contract_id, &rental.token_id);
        if result.is_err() {
            let n = self.reserved_of(&rental.lender_id);
            self.reserved.insert(rental.lender_id.clone(), n + 1);
            self.rentals.insert(k, rental);
            return false;
        }
        emit("rent_end", json!({ "nft_contract_id": rental.nft_contract_id, "token_id": rental.token_id, "lender_id": rental.lender_id }));
        true
    }

    pub fn get_rental(&self, nft_contract_id: AccountId, token_id: TokenId) -> Option<Rental> {
        self.rentals.get(&key(&nft_contract_id, &token_id)).cloned()
    }

    pub fn get_rentals(&self, from_index: Option<u32>, limit: Option<u32>) -> Vec<Rental> {
        let limit = limit.unwrap_or(MAX_RENTALS_PAGE).min(MAX_RENTALS_PAGE) as usize;
        self.rentals.values().skip(from_index.unwrap_or(0) as usize).take(limit).cloned().collect()
    }

    pub fn get_rentals_count(&self) -> u32 {
        self.rentals.len()
    }

    /// Workers this account lent out (rented or not).
    pub fn rentals_of_lender(&self, account_id: AccountId) -> Vec<Rental> {
        self.rentals.values().filter(|r| r.lender_id == account_id).take(MAX_RENTALS_PAGE as usize).cloned().collect()
    }

    /// Workers this account is renting right now.
    pub fn rentals_of_renter(&self, account_id: AccountId) -> Vec<Rental> {
        let now = env::block_timestamp_ms();
        self.rentals
            .values()
            .filter(|r| r.rented(now) && r.renter_id.as_ref() == Some(&account_id))
            .take(MAX_RENTALS_PAGE as usize)
            .cloned()
            .collect()
    }
}

impl Contract {
    pub(crate) fn start_rental(&mut self, renter: AccountId, amount: U128, args: RentArgs) {
        let k = key(&args.nft_contract_id, &args.token_id);
        let now = env::block_timestamp_ms();
        let fee = self.fee_of(amount.0);
        let r = self.rentals.get_mut(&k).unwrap_or_else(|| env::panic_str("Not for rent"));
        require!(renter != r.lender_id, "That's your own worker");
        require!(!r.rented(now), "Someone is renting this worker");
        require!((1..=r.max_days).contains(&args.days), "Rent for 1 to max_days days");
        let price = r.price_per_day.0.checked_mul(args.days as u128).unwrap_or_else(|| env::panic_str("price too large"));
        require!(amount.0 == price, "Amount must equal price_per_day × days");
        r.renter_id = Some(renter.clone());
        r.rented_until_ms = now + args.days as u64 * DAY_MS;
        r.earned = U128(r.earned.0 + amount.0 - fee);
        let snapshot = r.clone();
        emit(
            "rent_start",
            json!({
                "nft_contract_id": snapshot.nft_contract_id,
                "token_id": snapshot.token_id,
                "lender_id": snapshot.lender_id,
                "renter_id": renter,
                "days": args.days,
                "rented_until_ms": snapshot.rented_until_ms,
                "paid": amount,
                "fee": U128(fee),
            }),
        );
        if fee > 0 {
            let treasury = self.treasury_id.clone();
            self.pay(treasury, fee).detach();
        }
    }
}
