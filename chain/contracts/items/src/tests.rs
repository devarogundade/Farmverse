use super::*;
use crate::drops::{DropInput, DropUpdate};
use ed25519_dalek::{Signer, SigningKey};
use near_contract_standards::non_fungible_token::approval::NonFungibleTokenApproval;
use near_contract_standards::non_fungible_token::core::NonFungibleTokenCore;
use near_contract_standards::non_fungible_token::enumeration::NonFungibleTokenEnumeration;
use near_sdk::test_utils::{VMContextBuilder, accounts};
use near_sdk::{NearToken, testing_env};

const NOW_MS: u64 = 1_800_000_000_000;
const DEPOSIT: NearToken = NearToken::from_millinear(100);

fn contract_id() -> AccountId {
    "items.farmverse.near".parse().unwrap()
}
fn owner() -> AccountId {
    "dao.farmverse.near".parse().unwrap()
}
fn minter() -> AccountId {
    "studio.farmverse.near".parse().unwrap()
}
fn treasury() -> AccountId {
    "treasury.farmverse.near".parse().unwrap()
}
fn alice() -> AccountId {
    accounts(1)
}
fn bob() -> AccountId {
    accounts(2)
}

fn key(seed: u8) -> SigningKey {
    SigningKey::from_bytes(&[seed; 32])
}
fn public(k: &SigningKey) -> PublicKey {
    PublicKey::from_parts(CurveType::ED25519, k.verifying_key().to_bytes().to_vec()).unwrap()
}

fn ctx(predecessor: AccountId, deposit: NearToken) -> VMContextBuilder {
    let mut b = VMContextBuilder::new();
    b.current_account_id(contract_id())
        .signer_account_id(predecessor.clone())
        .predecessor_account_id(predecessor)
        .attached_deposit(deposit)
        .block_timestamp(NOW_MS * 1_000_000);
    b
}

fn setup() -> Contract {
    testing_env!(ctx(owner(), NearToken::from_yoctonear(0)).build());
    let mut royalties = HashMap::new();
    royalties.insert(treasury(), 250);
    Contract::new(
        owner(),
        minter(),
        vec![public(&key(7))],
        royalties,
        NFTContractMetadata {
            spec: NFT_METADATA_SPEC.into(),
            name: "Farmverse Items".into(),
            symbol: "FVITEM".into(),
            icon: None,
            base_uri: Some("https://cdn.farmverse.world".into()),
            reference: None,
            reference_hash: None,
        },
    )
}

fn voucher(token_id: &str, receiver: AccountId) -> MintVoucher {
    MintVoucher {
        contract_id: contract_id(),
        token_id: token_id.into(),
        receiver_id: receiver,
        kind: ItemKind::Good,
        item_id: "wheat".into(),
        quantity: 250,
        props: "{}".into(),
        title: "Wheat ×250".into(),
        media: "items/wheat.png".into(),
        expires_at_ms: NOW_MS + 60_000,
    }
}

fn sign(v: &MintVoucher, k: &SigningKey) -> Base64VecU8 {
    Base64VecU8::from(k.sign(&voucher_signing_bytes(v)).to_bytes().to_vec())
}

fn mint(c: &mut Contract, v: MintVoucher) -> Token {
    testing_env!(ctx(v.receiver_id.clone(), DEPOSIT).build());
    let sig = sign(&v, &key(7));
    c.mint_with_voucher(v, public(&key(7)), sig)
}

// ------------------------------------------------------------------ mint

#[test]
fn voucher_mint_creates_the_item() {
    let mut c = setup();
    let token = mint(&mut c, voucher("g-1", alice()));
    assert_eq!(token.owner_id, alice());
    let item = c.get_item("g-1".into()).unwrap();
    assert_eq!(item.kind, ItemKind::Good);
    assert_eq!(item.quantity, 250);
    assert_eq!(item.minted_from, Some(alice()));
    let meta = token.metadata.unwrap();
    assert_eq!(meta.media.as_deref(), Some("items/wheat.png"));
    assert!(meta.extra.unwrap().contains("\"wheat\""));
    assert_eq!(c.nft_supply_for_owner(alice()).0, 1);
}

#[test]
#[should_panic(expected = "Voucher is for another account")]
fn voucher_only_works_for_its_receiver() {
    let mut c = setup();
    let v = voucher("g-1", alice());
    testing_env!(ctx(bob(), DEPOSIT).build());
    let sig = sign(&v, &key(7));
    c.mint_with_voucher(v, public(&key(7)), sig);
}

#[test]
#[should_panic(expected = "Voucher expired")]
fn expired_voucher() {
    let mut c = setup();
    let mut v = voucher("g-1", alice());
    v.expires_at_ms = NOW_MS - 1;
    mint(&mut c, v);
}

#[test]
#[should_panic(expected = "Voucher lives too long")]
fn voucher_ttl_is_capped() {
    let mut c = setup();
    let mut v = voucher("g-1", alice());
    v.expires_at_ms = NOW_MS + MAX_VOUCHER_TTL_MS + 1;
    mint(&mut c, v);
}

#[test]
#[should_panic(expected = "Unknown signer")]
fn unknown_signer() {
    let mut c = setup();
    let v = voucher("g-1", alice());
    testing_env!(ctx(alice(), DEPOSIT).build());
    let sig = sign(&v, &key(9));
    c.mint_with_voucher(v, public(&key(9)), sig);
}

#[test]
#[should_panic(expected = "Bad voucher signature")]
fn tampered_voucher() {
    let mut c = setup();
    let v = voucher("g-1", alice());
    let sig = sign(&v, &key(7));
    let mut forged = v;
    forged.quantity = 10_000;
    testing_env!(ctx(alice(), DEPOSIT).build());
    c.mint_with_voucher(forged, public(&key(7)), sig);
}

#[test]
#[should_panic(expected = "Voucher is for another contract")]
fn voucher_for_another_contract() {
    let mut c = setup();
    let mut v = voucher("g-1", alice());
    v.contract_id = "items.farmverse.testnet".parse().unwrap();
    mint(&mut c, v);
}

#[test]
#[should_panic(expected = "token_id must be unique")]
fn voucher_is_single_use() {
    let mut c = setup();
    mint(&mut c, voucher("g-1", alice()));
    mint(&mut c, voucher("g-1", alice()));
}

#[test]
#[should_panic(expected = "Only the studio mints workers and packs")]
fn vouchers_cannot_mint_workers() {
    let mut c = setup();
    let mut v = voucher("g-1", alice());
    v.kind = ItemKind::Worker;
    v.quantity = 1;
    mint(&mut c, v);
}

#[test]
#[should_panic(expected = "Must attach")]
fn mint_needs_storage_deposit() {
    let mut c = setup();
    let v = voucher("g-1", alice());
    testing_env!(ctx(alice(), NearToken::from_yoctonear(1)).build());
    let sig = sign(&v, &key(7));
    c.mint_with_voucher(v, public(&key(7)), sig);
}

#[test]
#[should_panic(expected = "Paused")]
fn paused_stops_minting() {
    let mut c = setup();
    testing_env!(ctx(owner(), NearToken::from_yoctonear(1)).build());
    c.set_paused(true);
    mint(&mut c, voucher("g-1", alice()));
}

#[test]
fn removed_signer_is_refused_and_rotation_works() {
    let mut c = setup();
    testing_env!(ctx(owner(), NearToken::from_yoctonear(1)).build());
    c.add_signer(public(&key(8)));
    c.remove_signer(public(&key(7)));
    let v = voucher("g-1", alice());
    testing_env!(ctx(alice(), DEPOSIT).build());
    let sig = sign(&v, &key(8));
    c.mint_with_voucher(v, public(&key(8)), sig);
    assert!(c.get_item("g-1".into()).is_some());
}

// ------------------------------------------------------------------ studio

#[test]
fn studio_mints_workers() {
    let mut c = setup();
    testing_env!(ctx(minter(), DEPOSIT).build());
    c.studio_mint("s-w-1".into(), alice(), ItemKind::Worker, "farmhand".into(), 1, "{\"speed\":3}".into(), "Farmhand #1".into(), "workers/farmhand.png".into());
    let item = c.get_item("s-w-1".into()).unwrap();
    assert_eq!(item.kind, ItemKind::Worker);
    assert_eq!(item.minted_from, None);
}

#[test]
#[should_panic(expected = "Only the minter")]
fn only_minter_studio_mints() {
    let mut c = setup();
    testing_env!(ctx(alice(), DEPOSIT).build());
    c.studio_mint("s-1".into(), alice(), ItemKind::Pack, "gems_500".into(), 1, "{}".into(), "500 gems".into(), "packs/gems.png".into());
}

#[test]
#[should_panic(expected = "token_id must start with s-")]
fn studio_ids_are_prefixed() {
    let mut c = setup();
    testing_env!(ctx(minter(), DEPOSIT).build());
    c.studio_mint("g-1".into(), alice(), ItemKind::Pack, "gems_500".into(), 1, "{}".into(), "500 gems".into(), "packs/gems.png".into());
}

// ------------------------------------------------------------------ redeem

#[test]
fn redeem_burns_and_remembers() {
    let mut c = setup();
    mint(&mut c, voucher("g-1", alice()));
    testing_env!(ctx(alice(), NearToken::from_yoctonear(1)).build());
    let item = c.redeem("g-1".into());
    assert_eq!(item.item_id, "wheat");
    assert!(c.get_item("g-1".into()).is_none());
    assert!(c.nft_token("g-1".into()).is_none());
    assert!(c.is_burned("g-1".into()));
    assert_eq!(c.nft_supply_for_owner(alice()).0, 0);
    assert_eq!(c.nft_total_supply().0, 0);
}

#[test]
#[should_panic(expected = "token_id was used before")]
fn burned_voucher_cannot_mint_again() {
    let mut c = setup();
    let v = voucher("g-1", alice());
    mint(&mut c, v.clone());
    testing_env!(ctx(alice(), NearToken::from_yoctonear(1)).build());
    c.redeem("g-1".into());
    mint(&mut c, v);
}

#[test]
#[should_panic(expected = "Only the owner can redeem")]
fn only_owner_redeems() {
    let mut c = setup();
    mint(&mut c, voucher("g-1", alice()));
    testing_env!(ctx(bob(), NearToken::from_yoctonear(1)).build());
    c.redeem("g-1".into());
}

#[test]
fn redeem_works_without_a_deposit_for_session_keys() {
    let mut c = setup();
    mint(&mut c, voucher("g-1", alice()));
    testing_env!(ctx(alice(), NearToken::from_yoctonear(0)).build());
    c.redeem("g-1".into());
    assert!(c.is_burned("g-1".into()));
}

#[test]
#[should_panic(expected = "Attach nothing (or 1 yoctoNEAR)")]
fn redeem_refuses_a_real_deposit() {
    let mut c = setup();
    mint(&mut c, voucher("g-1", alice()));
    testing_env!(ctx(alice(), NearToken::from_millinear(1)).build());
    c.redeem("g-1".into());
}

#[test]
fn sponsored_mint_is_paid_by_the_contract_and_never_refunded() {
    let mut c = setup();
    let v = voucher("g-9", alice());
    testing_env!(ctx(alice(), NearToken::from_yoctonear(0)).build());
    let sig = sign(&v, &key(7));
    c.mint_with_voucher(v, public(&key(7)), sig);
    assert_eq!(c.nft_token("g-9".into()).unwrap().owner_id, alice());
    testing_env!(ctx(alice(), NearToken::from_yoctonear(0)).build());
    c.redeem("g-9".into());
    // no refund promise to the owner: the contract paid that storage
    assert!(near_sdk::test_utils::get_created_receipts().iter().all(|r| r.receiver_id != alice()));
}

#[test]
#[should_panic(expected = "Workers are held, not redeemed")]
fn workers_cannot_be_redeemed() {
    let mut c = setup();
    testing_env!(ctx(minter(), DEPOSIT).build());
    c.studio_mint("s-w-1".into(), alice(), ItemKind::Worker, "farmhand".into(), 1, "{}".into(), "Farmhand".into(), "workers/farmhand.png".into());
    testing_env!(ctx(alice(), NearToken::from_yoctonear(1)).build());
    c.redeem("s-w-1".into());
}

#[test]
fn buyer_can_redeem_after_transfer() {
    let mut c = setup();
    mint(&mut c, voucher("g-1", alice()));
    testing_env!(ctx(alice(), NearToken::from_yoctonear(1)).build());
    c.nft_transfer(bob(), "g-1".into(), None, None);
    testing_env!(ctx(bob(), NearToken::from_yoctonear(1)).build());
    assert_eq!(c.redeem("g-1".into()).quantity, 250);
}

// ------------------------------------------------------------------ payouts

#[test]
fn payout_splits_royalty_and_remainder() {
    let mut c = setup();
    mint(&mut c, voucher("g-1", alice()));
    let p = c.nft_payout("g-1".into(), U128(10_000), None).payout;
    assert_eq!(p.get(&treasury()), Some(&U128(250)));
    assert_eq!(p.get(&alice()), Some(&U128(9_750)));
}

#[test]
#[should_panic(expected = "Payout exceeds max_len_payout")]
fn payout_respects_max_len() {
    let mut c = setup();
    mint(&mut c, voucher("g-1", alice()));
    c.nft_payout("g-1".into(), U128(10_000), Some(1));
}

#[test]
fn transfer_payout_moves_the_token_for_an_approved_market() {
    let mut c = setup();
    mint(&mut c, voucher("g-1", alice()));
    let market: AccountId = "market.farmverse.near".parse().unwrap();
    testing_env!(ctx(alice(), DEPOSIT).build());
    c.nft_approve("g-1".into(), market.clone(), None);
    let approval_id = c.nft_token("g-1".into()).unwrap().approved_account_ids.unwrap()[&market];
    testing_env!(ctx(market.clone(), NearToken::from_yoctonear(1)).build());
    let p = c.nft_transfer_payout(bob(), "g-1".into(), Some(approval_id), None, U128(1_000), Some(10)).payout;
    assert!(!c.nft_is_approved("g-1".into(), market, None), "approvals are cleared on sale");
    assert_eq!(c.nft_token("g-1".into()).unwrap().owner_id, bob());
    assert_eq!(p.get(&alice()), Some(&U128(975)));
}

#[test]
#[should_panic(expected = "Sender not approved")]
fn transfer_payout_needs_approval() {
    let mut c = setup();
    mint(&mut c, voucher("g-1", alice()));
    testing_env!(ctx(bob(), NearToken::from_yoctonear(1)).build());
    c.nft_transfer_payout(bob(), "g-1".into(), None, None, U128(1_000), None);
}

// ------------------------------------------------------------------ admin

#[test]
#[should_panic(expected = "Only the owner")]
fn only_owner_admin() {
    let mut c = setup();
    testing_env!(ctx(alice(), NearToken::from_yoctonear(1)).build());
    c.set_paused(true);
}

#[test]
#[should_panic(expected = "Royalties may total at most 1000 bps")]
fn royalties_are_capped() {
    let mut c = setup();
    let mut r = HashMap::new();
    r.insert(treasury(), 1_001);
    testing_env!(ctx(owner(), NearToken::from_yoctonear(1)).build());
    c.set_royalties(r);
}

#[test]
fn ownership_moves_in_two_steps() {
    let mut c = setup();
    testing_env!(ctx(owner(), NearToken::from_yoctonear(1)).build());
    c.propose_owner(Some(bob()));
    assert_eq!(c.get_config().owner_id, owner());
    testing_env!(ctx(bob(), NearToken::from_yoctonear(1)).build());
    c.accept_owner();
    assert_eq!(c.get_config().owner_id, bob());
    assert_eq!(c.get_config().pending_owner_id, None);
}

#[test]
fn voucher_message_is_domain_plus_borsh() {
    let c = setup();
    let v = voucher("g-1", alice());
    let bytes = c.voucher_message(v.clone()).0;
    assert!(bytes.starts_with(MINT_DOMAIN));
    assert_eq!(&bytes[MINT_DOMAIN.len()..], borsh::to_vec(&v).unwrap().as_slice());
}

/// The game server (backend/server/src/mint/voucher.ts) signs these exact bytes for this voucher:
/// the same hex is pinned in its tests, so a change on either side fails one of them.
#[test]
fn voucher_bytes_match_the_game_server() {
    let v = MintVoucher {
        contract_id: "items.farmverse.near".parse().unwrap(),
        token_id: "g-1".into(),
        receiver_id: "alice.near".parse().unwrap(),
        kind: ItemKind::Building,
        item_id: "chicken_coop".into(),
        quantity: 1,
        props: "{\"level\":3}".into(),
        title: "Chicken coop".into(),
        media: "items/chicken_coop.png".into(),
        expires_at_ms: 1_800_000_060_000,
    };
    let expected = "6661726d76657273653a6974656d733a6d696e743a7631140000006974656d732e6661726d76657273652e6e65617203000000672d310a000000616c6963652e6e656172020c000000636869636b656e5f636f6f7001000000000000000b0000007b226c6576656c223a337d0c000000436869636b656e20636f6f70160000006974656d732f636869636b656e5f636f6f702e706e67603a5d18a3010000";
    let hex: String = voucher_signing_bytes(&v).iter().map(|b| format!("{b:02x}")).collect();
    assert_eq!(hex, expected);
}

// ------------------------------------------------------------------ drops (v2)

fn farm() -> AccountId {
    "farm.farmverse.near".parse().unwrap()
}

const PRICE: u128 = 250 * 10u128.pow(18);

fn pass_drop() -> DropInput {
    DropInput {
        id: "farm_pass_s1".into(),
        kind: ItemKind::Pack,
        item_id: "farm_pass".into(),
        quantity: 1,
        props: "{\"season\":1}".into(),
        title: "Farm Pass · Founders".into(),
        media: "items/farm_pass.png".into(),
        price: U128(PRICE),
        cap: Some(3),
        per_wallet: Some(2),
        starts_at_ms: NOW_MS - 1,
        ends_at_ms: None,
    }
}

fn with_drops() -> Contract {
    let mut c = setup();
    testing_env!(ctx(owner(), NearToken::from_yoctonear(1)).build());
    c.set_drop_payment(farm(), treasury());
    testing_env!(ctx(minter(), DEPOSIT).build());
    c.create_drop(pass_drop());
    c
}

#[allow(unused_must_use)]
fn buy(c: &mut Contract, buyer: AccountId, amount: u128, msg: &str) -> PromiseOrValue<U128> {
    testing_env!(ctx(farm(), NearToken::from_yoctonear(0)).build());
    c.ft_on_transfer(buyer, U128(amount), msg.into())
}

fn kept(r: PromiseOrValue<U128>) -> u128 {
    match r {
        PromiseOrValue::Value(v) => v.0,
        PromiseOrValue::Promise(_) => panic!("expected a value"),
    }
}

#[test]
fn drop_sale_mints_the_next_edition_and_keeps_the_farm() {
    let mut c = with_drops();
    let left = kept(buy(&mut c, alice(), PRICE, "{\"drop\":\"farm_pass_s1\"}"));
    assert_eq!(left, 0, "all the FARM is used");
    let token = c.nft_token("d-farm_pass_s1-1".into()).unwrap();
    assert_eq!(token.owner_id, alice());
    assert_eq!(token.metadata.unwrap().title.as_deref(), Some("Farm Pass · Founders #1"));
    let item = c.get_item("d-farm_pass_s1-1".into()).unwrap();
    assert_eq!(item.kind, ItemKind::Pack);
    assert!(item.props.contains("\"edition\":1") && item.props.contains("\"season\":1"));
    assert_eq!(c.get_drop("farm_pass_s1".into()).unwrap().sold, 1);
    assert_eq!(c.drop_bought("farm_pass_s1".into(), alice()), 1);
    assert!(near_sdk::test_utils::get_logs().iter().any(|l| l.contains("\"drop_sale\"")));
}

#[test]
fn buy_and_use_mints_and_redeems_at_once() {
    let mut c = with_drops();
    let _ = buy(&mut c, alice(), PRICE, "{\"drop\":\"farm_pass_s1\",\"use\":true}");
    assert!(c.nft_token("d-farm_pass_s1-1".into()).is_none());
    assert!(c.is_burned("d-farm_pass_s1-1".into()));
    let logs = near_sdk::test_utils::get_logs();
    assert!(logs.iter().any(|l| l.contains("\"item_redeem\"") && l.contains(alice().as_str())));
    assert!(logs.iter().any(|l| l.contains("\"used\":true")));
    assert_eq!(c.get_drop("farm_pass_s1".into()).unwrap().sold, 1, "a used edition still counts");
}

#[test]
#[should_panic(expected = "Sold out")]
fn drop_cap_is_enforced() {
    let mut c = with_drops();
    let _ = buy(&mut c, alice(), PRICE, "{\"drop\":\"farm_pass_s1\"}");
    let _ = buy(&mut c, bob(), PRICE, "{\"drop\":\"farm_pass_s1\"}");
    let _ = buy(&mut c, bob(), PRICE, "{\"drop\":\"farm_pass_s1\"}");
    let _ = buy(&mut c, accounts(3), PRICE, "{\"drop\":\"farm_pass_s1\"}");
}

#[test]
#[should_panic(expected = "You already have the most this drop allows")]
fn per_wallet_limit_is_enforced() {
    let mut c = with_drops();
    let _ = buy(&mut c, alice(), PRICE, "{\"drop\":\"farm_pass_s1\"}");
    let _ = buy(&mut c, alice(), PRICE, "{\"drop\":\"farm_pass_s1\"}");
    let _ = buy(&mut c, alice(), PRICE, "{\"drop\":\"farm_pass_s1\"}");
}

#[test]
#[should_panic(expected = "Amount must equal the drop price")]
fn drop_needs_the_exact_price() {
    let mut c = with_drops();
    let _ = buy(&mut c, alice(), PRICE - 1, "{\"drop\":\"farm_pass_s1\"}");
}

#[test]
#[should_panic(expected = "Only FARM is accepted")]
fn drops_only_take_farm() {
    let mut c = with_drops();
    testing_env!(ctx(bob(), NearToken::from_yoctonear(0)).build());
    let _ = c.ft_on_transfer(alice(), U128(PRICE), "{\"drop\":\"farm_pass_s1\"}".into());
}

#[test]
#[should_panic(expected = "This drop hasn't started")]
fn drop_window_is_enforced() {
    let mut c = setup();
    testing_env!(ctx(owner(), NearToken::from_yoctonear(1)).build());
    c.set_drop_payment(farm(), treasury());
    testing_env!(ctx(minter(), DEPOSIT).build());
    c.create_drop(DropInput { starts_at_ms: NOW_MS + 1_000, ..pass_drop() });
    let _ = buy(&mut c, alice(), PRICE, "{\"drop\":\"farm_pass_s1\"}");
}

#[test]
#[should_panic(expected = "This drop is paused")]
fn paused_drop_refuses() {
    let mut c = with_drops();
    testing_env!(ctx(minter(), NearToken::from_yoctonear(1)).build());
    c.update_drop("farm_pass_s1".into(), DropUpdate { paused: Some(true), ..Default::default() });
    let _ = buy(&mut c, alice(), PRICE, "{\"drop\":\"farm_pass_s1\"}");
}

#[test]
#[should_panic(expected = "cap can only go down")]
fn drop_cap_never_goes_up() {
    let mut c = with_drops();
    testing_env!(ctx(minter(), NearToken::from_yoctonear(1)).build());
    c.update_drop("farm_pass_s1".into(), DropUpdate { cap: Some(10), ..Default::default() });
}

#[test]
#[should_panic(expected = "Workers are held, not used up")]
fn workers_cannot_be_bought_and_used() {
    let mut c = setup();
    testing_env!(ctx(owner(), NearToken::from_yoctonear(1)).build());
    c.set_drop_payment(farm(), treasury());
    testing_env!(ctx(minter(), DEPOSIT).build());
    c.create_drop(DropInput { id: "crew".into(), kind: ItemKind::Worker, item_id: "farmhand".into(), ..pass_drop() });
    let _ = buy(&mut c, alice(), PRICE, "{\"drop\":\"crew\",\"use\":true}");
}

#[test]
#[should_panic(expected = "Only the minter")]
fn only_the_minter_creates_drops() {
    let mut c = setup();
    testing_env!(ctx(owner(), NearToken::from_yoctonear(1)).build());
    c.set_drop_payment(farm(), treasury());
    testing_env!(ctx(alice(), DEPOSIT).build());
    c.create_drop(pass_drop());
}

#[test]
#[should_panic(expected = "The shop is restocking")]
fn drops_stop_when_the_storage_float_is_low() {
    let mut c = with_drops();
    let mut b = ctx(farm(), NearToken::from_yoctonear(0));
    b.account_balance(NearToken::from_millinear(1));
    testing_env!(b.build());
    let _ = c.ft_on_transfer(alice(), U128(PRICE), "{\"drop\":\"farm_pass_s1\"}".into());
}

#[test]
fn open_edition_titles_have_no_number() {
    let mut c = setup();
    testing_env!(ctx(owner(), NearToken::from_yoctonear(1)).build());
    c.set_drop_payment(farm(), treasury());
    testing_env!(ctx(minter(), DEPOSIT).build());
    c.create_drop(DropInput { id: "gems_100".into(), item_id: "gems_100".into(), title: "100 Gems".into(), cap: None, per_wallet: None, ..pass_drop() });
    let _ = buy(&mut c, alice(), PRICE, "{\"drop\":\"gems_100\"}");
    assert_eq!(c.nft_token("d-gems_100-1".into()).unwrap().metadata.unwrap().title.as_deref(), Some("100 Gems"));
}

#[test]
fn migrate_keeps_v1_tokens_and_settings() {
    // v1 state with one minted token: the same collections, without the v2 fields
    let mut v2 = setup();
    let token = mint(&mut v2, voucher("g-1", alice()));
    let Contract { tokens, metadata, mut items, burned, mut signers, owner_id, pending_owner_id, minter_id, royalties, paused, .. } = v2;
    signers.flush();
    items.flush();
    let v1 = ContractV1 { tokens, metadata, items, burned, signers, owner_id, pending_owner_id, minter_id, royalties, paused };
    env::state_write(&v1);
    testing_env!(ctx(contract_id(), NearToken::from_yoctonear(0)).build());
    let c = Contract::migrate();
    assert_eq!(c.nft_token("g-1".into()).unwrap().owner_id, token.owner_id);
    assert_eq!(c.get_item("g-1".into()).unwrap().item_id, "wheat");
    let cfg = c.get_config();
    assert_eq!(cfg.minter_id, minter());
    assert_eq!(cfg.signers.len(), 1);
    assert!(cfg.farm_id.is_none());
    assert!(c.get_drops(None, None).is_empty());
}
