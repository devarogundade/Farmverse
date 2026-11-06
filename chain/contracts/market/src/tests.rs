use super::*;
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
