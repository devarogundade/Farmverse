use super::*;
use near_contract_standards::non_fungible_token::core::NonFungibleTokenReceiver;
use rentals::Rental;
use near_sdk::test_utils::{VMContextBuilder, get_logs};
use near_sdk::testing_env;

fn id(s: &str) -> AccountId {
    s.parse().unwrap()
}
fn market() -> AccountId {
    id("market.farmverse.near")
}
fn dao() -> AccountId {
    id("dao.farmverse.near")
}
fn farm() -> AccountId {
    id("farm.farmverse.near")
}
fn items() -> AccountId {
    id("items.farmverse.near")
}
fn treasury() -> AccountId {
    id("treasury.farmverse.near")
}
fn alice() -> AccountId {
    id("alice.near")
}
fn bob() -> AccountId {
    id("bob.near")
}

fn ctx(predecessor: AccountId, deposit: NearToken) -> VMContextBuilder {
    let mut b = VMContextBuilder::new();
    b.current_account_id(market())
        .signer_account_id(predecessor.clone())
        .predecessor_account_id(predecessor)
        .attached_deposit(deposit)
        .prepaid_gas(Gas::from_tgas(300))
        .block_timestamp(1_800_000_000_000 * 1_000_000);
    b
}

fn setup() -> Contract {
    testing_env!(ctx(dao(), NearToken::from_yoctonear(0)).build());
    Contract::new(dao(), farm(), treasury(), 500, vec![items()])
}

fn deposit_storage(c: &mut Contract, who: AccountId, listings: u128) {
    testing_env!(ctx(who, STORAGE_PER_LISTING.saturating_mul(listings)).build());
    c.storage_deposit(None);
}

fn list(c: &mut Contract, owner: AccountId, token: &str, price: u128) {
    testing_env!(ctx(items(), NearToken::from_yoctonear(0)).build());
    let _ = c.nft_on_approve(token.into(), owner, 1, format!("{{\"price\":\"{price}\"}}"));
}

fn buy(c: &mut Contract, buyer: AccountId, token: &str, amount: u128) {
    testing_env!(ctx(farm(), NearToken::from_yoctonear(0)).build());
    let _ = c.ft_on_transfer(buyer, U128(amount), format!("{{\"nft_contract_id\":\"{}\",\"token_id\":\"{token}\"}}", items()));
}

// ------------------------------------------------------------------ listing

#[test]
fn approve_lists_the_token() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    let l = c.get_listing(items(), "g-1".into()).unwrap();
    assert_eq!(l.owner_id, alice());
    assert_eq!(l.price, U128(100));
    assert_eq!(c.listing_count_of(alice()), 1);
    assert!(get_logs().iter().any(|l| l.contains("market_list")));
}

#[test]
#[should_panic(expected = "Deposit storage for the listing first")]
fn listing_needs_storage() {
    let mut c = setup();
    list(&mut c, alice(), "g-1", 100);
}

#[test]
#[should_panic(expected = "Deposit storage for the listing first")]
fn each_listing_reserves_storage() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    list(&mut c, alice(), "g-2", 100);
}

#[test]
fn relisting_changes_price_without_more_storage() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    list(&mut c, alice(), "g-1", 250);
    assert_eq!(c.get_listing(items(), "g-1".into()).unwrap().price, U128(250));
    assert_eq!(c.listing_count_of(alice()), 1);
}

#[test]
#[should_panic(expected = "NFT contract not accepted")]
fn only_whitelisted_nft_contracts() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    testing_env!(ctx(id("evil.near"), NearToken::from_yoctonear(0)).build());
    let _ = c.nft_on_approve("g-1".into(), alice(), 1, "{\"price\":\"1\"}".into());
}

#[test]
#[should_panic(expected = "price must be positive")]
fn zero_price_refused() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 0);
}

#[test]
fn seller_delists_and_gets_storage_back() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    testing_env!(ctx(alice(), NearToken::from_yoctonear(1)).build());
    c.delist(items(), "g-1".into());
    assert!(c.get_listing(items(), "g-1".into()).is_none());
    assert_eq!(c.storage_withdraw(), U128(STORAGE_PER_LISTING.as_yoctonear()));
}

#[test]
#[should_panic(expected = "Only the seller")]
fn only_seller_delists() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    testing_env!(ctx(bob(), NearToken::from_yoctonear(1)).build());
    c.delist(items(), "g-1".into());
}

#[test]
#[should_panic(expected = "Nothing to withdraw")]
fn listed_storage_cannot_be_withdrawn() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    testing_env!(ctx(alice(), NearToken::from_yoctonear(1)).build());
    c.storage_withdraw();
}

// ------------------------------------------------------------------ buying

#[test]
fn buying_takes_the_listing_off_the_book() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    buy(&mut c, bob(), "g-1", 100);
    assert!(c.get_listing(items(), "g-1".into()).is_none());
    assert_eq!(c.listing_count_of(alice()), 0);
}

#[test]
#[should_panic(expected = "Only FARM is accepted")]
fn other_tokens_are_refused() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    testing_env!(ctx(id("usdt.near"), NearToken::from_yoctonear(0)).build());
    let _ = c.ft_on_transfer(bob(), U128(100), format!("{{\"nft_contract_id\":\"{}\",\"token_id\":\"g-1\"}}", items()));
}

#[test]
#[should_panic(expected = "Amount must equal the listing price")]
fn wrong_amount_refused() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    buy(&mut c, bob(), "g-1", 99);
}

#[test]
#[should_panic(expected = "Seller can't buy their own listing")]
fn self_buy_refused() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    buy(&mut c, alice(), "g-1", 100);
}

#[test]
#[should_panic(expected = "Not listed")]
fn second_buyer_fails() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    buy(&mut c, bob(), "g-1", 100);
    buy(&mut c, id("carol.near"), "g-1", 100);
}

#[test]
#[should_panic(expected = "Paused")]
fn paused_stops_buying() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    testing_env!(ctx(dao(), NearToken::from_yoctonear(1)).build());
    c.set_paused(true);
    buy(&mut c, bob(), "g-1", 100);
}

fn listing(price: u128) -> Listing {
    Listing { nft_contract_id: items(), token_id: "g-1".into(), owner_id: alice(), approval_id: 1, price: U128(price), listed_at_ms: 0 }
}

#[test]
fn failed_nft_transfer_refunds_the_buyer_in_full() {
    let mut c = setup();
    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).build());
    let refund = c.resolve_purchase(bob(), listing(1_000), U128(1_000), U128(50), Err(PromiseError::Failed));
    assert_eq!(refund, U128(1_000));
    assert!(get_logs().iter().any(|l| l.contains("market_sale_failed")));
}

#[test]
fn successful_sale_keeps_the_payment_and_logs_it() {
    let mut c = setup();
    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).build());
    let mut p = HashMap::new();
    p.insert(alice(), U128(926));
    p.insert(treasury(), U128(24));
    let refund = c.resolve_purchase(bob(), listing(1_000), U128(1_000), U128(50), Ok(Payout { payout: p }));
    assert_eq!(refund, U128(0));
    let sale = get_logs().into_iter().find(|l| l.contains("market_sale")).unwrap();
    assert!(sale.contains("\"payout_valid\":true"));
}

#[test]
fn oversized_payout_pays_the_seller_instead() {
    let mut c = setup();
    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).build());
    let mut p = HashMap::new();
    p.insert(id("thief.near"), U128(10_000)); // more than the sale
    let refund = c.resolve_purchase(bob(), listing(1_000), U128(1_000), U128(50), Ok(Payout { payout: p }));
    assert_eq!(refund, U128(0));
    assert!(get_logs().iter().any(|l| l.contains("\"payout_valid\":false")));
}

#[test]
fn undelivered_payment_is_owed_and_withdrawable() {
    let mut c = setup();
    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).build());
    c.on_payment(alice(), U128(926), Err(PromiseError::Failed));
    c.on_payment(alice(), U128(4), Err(PromiseError::Failed));
    assert_eq!(c.owed_of(alice()), U128(930));
    testing_env!(ctx(alice(), NearToken::from_yoctonear(0)).build());
    let _ = c.withdraw_owed();
    assert_eq!(c.owed_of(alice()), U128(0));
}

#[test]
fn delivered_payment_is_not_owed() {
    let mut c = setup();
    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).build());
    c.on_payment(alice(), U128(926), Ok(()));
    assert_eq!(c.owed_of(alice()), U128(0));
}

// ------------------------------------------------------------------ stale listings

#[test]
fn stale_listing_is_removed_when_the_owner_changed() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).build());
    let moved = TokenView { owner_id: bob(), approved_account_ids: Some(HashMap::new()) };
    assert!(c.resolve_stale(items(), "g-1".into(), Ok(Some(moved))));
    assert!(c.get_listing(items(), "g-1".into()).is_none());
}

#[test]
fn live_listing_is_kept() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).build());
    let mut approvals = HashMap::new();
    approvals.insert(market(), 1u64);
    let live = TokenView { owner_id: alice(), approved_account_ids: Some(approvals) };
    assert!(!c.resolve_stale(items(), "g-1".into(), Ok(Some(live))));
    assert!(c.get_listing(items(), "g-1".into()).is_some());
}

#[test]
fn burned_token_listing_is_stale() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).build());
    assert!(c.resolve_stale(items(), "g-1".into(), Ok(None)));
}

#[test]
fn new_owner_relisting_frees_the_old_sellers_slot() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    deposit_storage(&mut c, bob(), 1);
    list(&mut c, alice(), "g-1", 100);
    list(&mut c, bob(), "g-1", 300);
    assert_eq!(c.listing_count_of(alice()), 0);
    assert_eq!(c.listing_count_of(bob()), 1);
}

// ------------------------------------------------------------------ admin

#[test]
#[should_panic(expected = "Fee may be at most 1000 bps")]
fn fee_is_capped() {
    let mut c = setup();
    testing_env!(ctx(dao(), NearToken::from_yoctonear(1)).build());
    c.set_fee_bps(1_001);
}

#[test]
#[should_panic(expected = "Only the owner")]
fn only_owner_sets_fee() {
    let mut c = setup();
    testing_env!(ctx(alice(), NearToken::from_yoctonear(1)).build());
    c.set_fee_bps(0);
}

// ------------------------------------------------------------------ offers (v2)

const NOW: u64 = 1_800_000_000_000;
const DAY: u64 = 24 * 60 * 60 * 1000;

fn offer_token(c: &mut Contract, buyer: AccountId, token: &str, amount: u128) {
    testing_env!(ctx(farm(), NearToken::from_yoctonear(0)).build());
    let msg = format!("{{\"offer\":{{\"nft_contract_id\":\"{}\",\"token_id\":\"{token}\",\"expires_at_ms\":{}}}}}", items(), NOW + DAY);
    let _ = c.ft_on_transfer(buyer, U128(amount), msg);
}

fn offer_item(c: &mut Contract, buyer: AccountId, item: &str, min: u64, amount: u128) {
    testing_env!(ctx(farm(), NearToken::from_yoctonear(0)).build());
    let msg = format!(
        "{{\"offer\":{{\"nft_contract_id\":\"{}\",\"item_id\":\"{item}\",\"min_quantity\":{min},\"expires_at_ms\":{}}}}}",
        items(),
        NOW + DAY
    );
    let _ = c.ft_on_transfer(buyer, U128(amount), msg);
}

fn accept(c: &mut Contract, owner: AccountId, token: &str, offer_id: u64) -> PromiseOrValue<String> {
    testing_env!(ctx(items(), NearToken::from_yoctonear(0)).build());
    c.nft_on_approve(token.into(), owner, 7, format!("{{\"accept_offer\":{offer_id}}}"))
}

fn wheat(quantity: u64) -> ItemView {
    ItemView { kind: "good".into(), item_id: "wheat".into(), quantity }
}

#[test]
fn offer_escrows_and_reserves_a_slot() {
    let mut c = setup();
    deposit_storage(&mut c, bob(), 1);
    offer_token(&mut c, bob(), "g-1", 500);
    let o = c.get_offer(1).unwrap();
    assert_eq!(o.buyer_id, bob());
    assert_eq!(o.amount, U128(500));
    assert_eq!(c.offers_for_token(items(), "g-1".into()).len(), 1);
    assert!(get_logs().iter().any(|l| l.contains("offer_make")));
    assert_eq!(c.storage_balance_of(bob()), U128(STORAGE_PER_LISTING.as_yoctonear()));
}

#[test]
#[should_panic(expected = "Deposit storage first")]
fn offer_needs_storage() {
    let mut c = setup();
    offer_token(&mut c, bob(), "g-1", 500);
}

#[test]
fn a_new_offer_replaces_the_old_one_without_a_new_slot() {
    let mut c = setup();
    deposit_storage(&mut c, bob(), 1);
    offer_token(&mut c, bob(), "g-1", 500);
    offer_token(&mut c, bob(), "g-1", 700);
    assert!(c.get_offer(1).is_none());
    assert_eq!(c.get_offer(2).unwrap().amount, U128(700));
    assert_eq!(c.get_offers_count(), 1);
    assert!(get_logs().iter().any(|l| l.contains("\"reason\":\"replaced\"")));
}

#[test]
#[should_panic(expected = "Offer on a token_id or an item_id, not both")]
fn offer_needs_exactly_one_target() {
    let mut c = setup();
    deposit_storage(&mut c, bob(), 1);
    testing_env!(ctx(farm(), NearToken::from_yoctonear(0)).build());
    let msg = format!("{{\"offer\":{{\"nft_contract_id\":\"{}\",\"expires_at_ms\":{}}}}}", items(), NOW + DAY);
    let _ = c.ft_on_transfer(bob(), U128(1), msg);
}

#[test]
#[should_panic(expected = "Offers last at most 30 days")]
fn offer_lifetime_is_capped() {
    let mut c = setup();
    deposit_storage(&mut c, bob(), 1);
    testing_env!(ctx(farm(), NearToken::from_yoctonear(0)).build());
    let msg = format!("{{\"offer\":{{\"nft_contract_id\":\"{}\",\"token_id\":\"g-1\",\"expires_at_ms\":{}}}}}", items(), NOW + 31 * DAY);
    let _ = c.ft_on_transfer(bob(), U128(1), msg);
}

#[test]
fn accepting_a_token_offer_moves_it_and_drops_the_listing() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    deposit_storage(&mut c, bob(), 1);
    list(&mut c, alice(), "g-1", 1_000);
    offer_token(&mut c, bob(), "g-1", 800);
    let r = accept(&mut c, alice(), "g-1", 1);
    assert!(matches!(r, PromiseOrValue::Promise(_)));
    assert!(c.get_offer(1).is_none());
    assert!(c.get_listing(items(), "g-1".into()).is_none());
    assert_eq!(c.listing_count_of(alice()), 0);
}

#[test]
#[should_panic(expected = "Offer is for another token")]
fn token_offer_only_accepts_its_token() {
    let mut c = setup();
    deposit_storage(&mut c, bob(), 1);
    offer_token(&mut c, bob(), "g-1", 800);
    let _ = accept(&mut c, alice(), "g-2", 1);
}

#[test]
fn accepted_offer_pays_out() {
    let mut c = setup();
    deposit_storage(&mut c, bob(), 1);
    offer_token(&mut c, bob(), "g-1", 1_000);
    let offer = c.get_offer(1).unwrap();
    let _ = accept(&mut c, alice(), "g-1", 1);
    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).build());
    let mut p = HashMap::new();
    p.insert(alice(), U128(926));
    p.insert(treasury(), U128(24));
    assert!(c.resolve_offer(offer, "g-1".into(), alice(), U128(50), Ok(Payout { payout: p })));
    assert!(get_logs().iter().any(|l| l.contains("offer_accept\"") && l.contains("\"payout_valid\":true")));
}

#[test]
fn failed_transfer_reopens_the_offer() {
    let mut c = setup();
    deposit_storage(&mut c, bob(), 1);
    offer_token(&mut c, bob(), "g-1", 1_000);
    let offer = c.get_offer(1).unwrap();
    let _ = accept(&mut c, alice(), "g-1", 1);
    assert!(c.get_offer(1).is_none());
    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).build());
    assert!(!c.resolve_offer(offer.clone(), "g-1".into(), alice(), U128(50), Err(PromiseError::Failed)));
    assert_eq!(c.get_offer(1), Some(offer));
    // the slot is held again: a second, different offer needs more storage
    assert_eq!(c.offers_of(bob()).len(), 1);
}

#[test]
fn item_offer_checks_the_item_first() {
    let mut c = setup();
    deposit_storage(&mut c, bob(), 1);
    offer_item(&mut c, bob(), "wheat", 100, 400);
    let r = accept(&mut c, alice(), "g-9", 1);
    assert!(matches!(r, PromiseOrValue::Promise(_)), "reads the item");
    drop(r);
    assert!(c.get_offer(1).is_some(), "nothing taken before the check");

    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).prepaid_gas(Gas::from_tgas(300)).build());
    let small = c.check_item_offer(1, "g-9".into(), alice(), 7, Ok(Some(wheat(50))));
    assert!(matches!(small, PromiseOrValue::Value(_)));
    assert!(c.get_offer(1).is_some(), "too few units: the offer stays");

    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).prepaid_gas(Gas::from_tgas(300)).build());
    let ok = c.check_item_offer(1, "g-9".into(), alice(), 7, Ok(Some(wheat(250))));
    assert!(matches!(ok, PromiseOrValue::Promise(_)));
    assert!(c.get_offer(1).is_none());
}

#[test]
fn buyer_cancels_and_is_refunded() {
    let mut c = setup();
    deposit_storage(&mut c, bob(), 1);
    offer_token(&mut c, bob(), "g-1", 500);
    testing_env!(ctx(bob(), NearToken::from_yoctonear(1)).build());
    let _ = c.offer_cancel(1);
    assert!(c.get_offer(1).is_none());
    assert!(get_logs().iter().any(|l| l.contains("offer_cancel")));
}

#[test]
#[should_panic(expected = "Only the buyer can cancel a live offer")]
fn others_cannot_cancel_a_live_offer() {
    let mut c = setup();
    deposit_storage(&mut c, bob(), 1);
    offer_token(&mut c, bob(), "g-1", 500);
    testing_env!(ctx(alice(), NearToken::from_yoctonear(0)).build());
    let _ = c.offer_cancel(1);
}

#[test]
fn anyone_clears_an_expired_offer() {
    let mut c = setup();
    deposit_storage(&mut c, bob(), 1);
    offer_token(&mut c, bob(), "g-1", 500);
    let mut b = ctx(alice(), NearToken::from_yoctonear(0));
    b.block_timestamp((NOW + 2 * DAY) * 1_000_000);
    testing_env!(b.build());
    let _ = c.offer_cancel(1);
    assert!(c.get_offer(1).is_none());
}

// ------------------------------------------------------------------ rentals (v2)

fn worker() -> ItemView {
    ItemView { kind: "worker".into(), item_id: "farmhand".into(), quantity: 1 }
}

fn rent_out(c: &mut Contract, lender: AccountId, token: &str, per_day: u128, max_days: u32) {
    testing_env!(ctx(items(), NearToken::from_yoctonear(0)).build());
    let _ = c.nft_on_transfer(lender.clone(), lender.clone(), token.into(), format!("{{\"rent\":{{\"price_per_day\":\"{per_day}\",\"max_days\":{max_days}}}}}"));
    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).build());
    assert!(!c.resolve_rent_list(items(), token.into(), lender, U128(per_day), max_days, Ok(Some(worker()))));
}

fn rent(c: &mut Contract, renter: AccountId, token: &str, days: u32, amount: u128) {
    testing_env!(ctx(farm(), NearToken::from_yoctonear(0)).build());
    let msg = format!("{{\"rent\":{{\"nft_contract_id\":\"{}\",\"token_id\":\"{token}\",\"days\":{days}}}}}", items());
    let _ = c.ft_on_transfer(renter, U128(amount), msg);
}

#[test]
fn worker_is_held_for_rent() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    rent_out(&mut c, alice(), "s-w1", 100, 7);
    let r = c.get_rental(items(), "s-w1".into()).unwrap();
    assert_eq!(r.lender_id, alice());
    assert_eq!(r.renter_id, None);
    assert_eq!(c.rentals_of_lender(alice()).len(), 1);
}

#[test]
fn non_workers_go_back() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    testing_env!(ctx(items(), NearToken::from_yoctonear(0)).build());
    let _ = c.nft_on_transfer(alice(), alice(), "g-1".into(), "{\"rent\":{\"price_per_day\":\"100\",\"max_days\":7}}".into());
    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).build());
    assert!(c.resolve_rent_list(items(), "g-1".into(), alice(), U128(100), 7, Ok(Some(wheat(10)))), "returned");
    assert!(c.get_rental(items(), "g-1".into()).is_none());
    // the slot was released: a worker can still be rented out with the same deposit
    rent_out(&mut c, alice(), "s-w1", 100, 7);
}

#[test]
#[should_panic(expected = "Deposit storage first")]
fn renting_out_needs_storage() {
    let mut c = setup();
    testing_env!(ctx(items(), NearToken::from_yoctonear(0)).build());
    let _ = c.nft_on_transfer(alice(), alice(), "s-w1".into(), "{\"rent\":{\"price_per_day\":\"100\",\"max_days\":7}}".into());
}

#[test]
fn renting_pays_the_worker_minus_the_fee() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    rent_out(&mut c, alice(), "s-w1", 100, 7);
    rent(&mut c, bob(), "s-w1", 3, 300);
    let r = c.get_rental(items(), "s-w1".into()).unwrap();
    assert_eq!(r.renter_id, Some(bob()));
    assert_eq!(r.rented_until_ms, NOW + 3 * DAY);
    assert_eq!(r.earned, U128(285), "5 % fee to the treasury");
    assert_eq!(c.rentals_of_renter(bob()).len(), 1);
    assert!(get_logs().iter().any(|l| l.contains("rent_start")));
}

#[test]
#[should_panic(expected = "Someone is renting this worker")]
fn one_renter_at_a_time() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    rent_out(&mut c, alice(), "s-w1", 100, 7);
    rent(&mut c, bob(), "s-w1", 3, 300);
    rent(&mut c, id("carol.near"), "s-w1", 1, 100);
}

#[test]
#[should_panic(expected = "Amount must equal price_per_day × days")]
fn rent_needs_the_exact_amount() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    rent_out(&mut c, alice(), "s-w1", 100, 7);
    rent(&mut c, bob(), "s-w1", 3, 299);
}

#[test]
#[should_panic(expected = "Rent for 1 to max_days days")]
fn rent_is_capped_at_max_days() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    rent_out(&mut c, alice(), "s-w1", 100, 7);
    rent(&mut c, bob(), "s-w1", 8, 800);
}

#[test]
fn lender_withdraws_earnings_any_time() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    rent_out(&mut c, alice(), "s-w1", 100, 7);
    rent(&mut c, bob(), "s-w1", 2, 200);
    testing_env!(ctx(alice(), NearToken::from_yoctonear(1)).build());
    assert_eq!(c.rent_withdraw(items(), "s-w1".into()), U128(190));
    assert_eq!(c.get_rental(items(), "s-w1".into()).unwrap().earned, U128(0));
}

#[test]
#[should_panic(expected = "A rental is running until it ends")]
fn lender_cannot_end_a_running_rental() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    rent_out(&mut c, alice(), "s-w1", 100, 7);
    rent(&mut c, bob(), "s-w1", 2, 200);
    testing_env!(ctx(alice(), NearToken::from_yoctonear(1)).build());
    c.rent_end(items(), "s-w1".into());
}

#[test]
fn rental_ends_after_its_days_and_can_be_rented_again() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    rent_out(&mut c, alice(), "s-w1", 100, 7);
    rent(&mut c, bob(), "s-w1", 1, 100);
    let mut b = ctx(farm(), NearToken::from_yoctonear(0));
    b.block_timestamp((NOW + DAY + 1) * 1_000_000);
    testing_env!(b.build());
    let msg = format!("{{\"rent\":{{\"nft_contract_id\":\"{}\",\"token_id\":\"s-w1\",\"days\":1}}}}", items());
    let _ = c.ft_on_transfer(id("carol.near"), U128(100), msg);
    let r = c.get_rental(items(), "s-w1".into()).unwrap();
    assert_eq!(r.renter_id, Some(id("carol.near")));
    assert_eq!(r.earned, U128(190));
}

#[test]
fn lender_takes_the_worker_back() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    rent_out(&mut c, alice(), "s-w1", 100, 7);
    testing_env!(ctx(alice(), NearToken::from_yoctonear(1)).build());
    c.rent_end(items(), "s-w1".into());
    assert!(c.get_rental(items(), "s-w1".into()).is_none());
    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).build());
    let back = Rental {
        nft_contract_id: items(),
        token_id: "s-w1".into(),
        lender_id: alice(),
        price_per_day: U128(100),
        max_days: 7,
        renter_id: None,
        rented_until_ms: 0,
        earned: U128(0),
        listed_at_ms: NOW,
    };
    assert!(!c.resolve_rent_end(back.clone(), Err(PromiseError::Failed)), "a failed return keeps it listed");
    assert_eq!(c.get_rental(items(), "s-w1".into()), Some(back));
}

// ------------------------------------------------------------------ migrate (v2)

#[test]
fn migrate_keeps_v1_listings() {
    let mut c = setup();
    deposit_storage(&mut c, alice(), 1);
    list(&mut c, alice(), "g-1", 100);
    let Contract {
        owner_id, pending_owner_id, ft_token_id, treasury_id, fee_bps, mut nft_contracts, mut listings, mut listing_count,
        mut storage_deposits, mut owed, paused, ..
    } = c;
    nft_contracts.flush();
    listings.flush();
    listing_count.flush();
    storage_deposits.flush();
    owed.flush();
    let v1 = ContractV1 {
        owner_id, pending_owner_id, ft_token_id, treasury_id, fee_bps, nft_contracts, listings, listing_count, storage_deposits,
        owed, paused,
    };
    env::state_write(&v1);
    testing_env!(ctx(market(), NearToken::from_yoctonear(0)).build());
    let c = Contract::migrate();
    assert_eq!(c.get_listing(items(), "g-1".into()).unwrap().price, U128(100));
    assert_eq!(c.get_config().fee_bps, 500);
    assert_eq!(c.get_offers_count(), 0);
    assert_eq!(c.get_rentals_count(), 0);
}
