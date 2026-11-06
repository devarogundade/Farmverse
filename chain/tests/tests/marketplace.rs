//! End-to-end on a local sandbox: FARM, items and the market deployed together, the real
//! cross-contract calls, every money movement checked to the unit.
//!
//! Run (Linux/macOS/WSL): `scripts/build.sh && cargo test -p farmverse-chain-tests`

use ed25519_dalek::{Signer, SigningKey};
use farmverse_items::item::ItemKind;
use farmverse_items::{MintVoucher, voucher_signing_bytes};
use near_sdk::json_types::Base64VecU8;
use near_sdk::{CurveType, PublicKey};
use near_workspaces::network::Sandbox;
use near_workspaces::types::NearToken;
use near_workspaces::{Account, Contract, Worker};
use serde_json::json;
use std::path::PathBuf;

const SUPPLY: u128 = 1_000_000_000 * 10u128.pow(18);
const PRICE: u128 = 1_000;

fn wasm(name: &str) -> Vec<u8> {
    let dir = std::env::var("FV_WASM_DIR").map(PathBuf::from).unwrap_or_else(|_| PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../res"));
    let path = dir.join(format!("{name}.wasm"));
    std::fs::read(&path).unwrap_or_else(|e| panic!("{}: {e} (run scripts/build.sh first)", path.display()))
}

struct World {
    worker: Worker<Sandbox>,
    farm: Contract,
    items: Contract,
    market: Contract,
    treasury: Account,
    dao: Account,
    studio: Account,
    signer: SigningKey,
}

async fn sub(worker: &Worker<Sandbox>, name: &str, near: u128) -> anyhow::Result<Account> {
    Ok(worker.root_account()?.create_subaccount(name).initial_balance(NearToken::from_near(near)).transact().await?.into_result()?)
}

async fn world() -> anyhow::Result<World> {
    let worker = near_workspaces::sandbox().await?;
    let dao = sub(&worker, "dao", 50).await?;
    let studio = sub(&worker, "studio", 50).await?;
    let treasury = sub(&worker, "treasury", 50).await?;
    let signer = SigningKey::from_bytes(&[7; 32]);
    let signer_pk = PublicKey::from_parts(CurveType::ED25519, signer.verifying_key().to_bytes().to_vec()).unwrap();

    let farm = sub(&worker, "farm", 20).await?.deploy(&wasm("farmverse_farm_token")).await?.into_result()?;
    farm.call("new")
        .args_json(json!({
            "treasury_id": treasury.id(),
            "total_supply": SUPPLY.to_string(),
            "metadata": { "spec": "ft-1.0.0", "name": "Farmverse", "symbol": "FARM", "decimals": 18 }
        }))
        .transact()
        .await?
        .into_result()?;

    let items = sub(&worker, "items", 20).await?.deploy(&wasm("farmverse_items")).await?.into_result()?;
    items
        .call("new")
        .args_json(json!({
            "owner_id": dao.id(),
            "minter_id": studio.id(),
            "signers": [signer_pk],
            "royalties": { treasury.id().to_string(): 250 },
            "metadata": { "spec": "nft-1.0.0", "name": "Farmverse Items", "symbol": "FVITEM", "base_uri": "https://cdn.farmverse.world" }
        }))
        .transact()
        .await?
        .into_result()?;

    let market = sub(&worker, "market", 20).await?.deploy(&wasm("farmverse_market")).await?.into_result()?;
    market
        .call("new")
        .args_json(json!({
            "owner_id": dao.id(),
            "ft_token_id": farm.id(),
            "treasury_id": treasury.id(),
            "fee_bps": 500,
            "nft_contracts": [items.id()]
        }))
        .transact()
        .await?
        .into_result()?;

    // The market pays out FARM and holds it mid-sale, so it needs a FARM account.
    register(&farm, market.as_account()).await?;
    Ok(World { worker, farm, items, market, treasury, dao, studio, signer })
}

async fn register(farm: &Contract, who: &Account) -> anyhow::Result<()> {
    who.call(farm.id(), "storage_deposit")
        .args_json(json!({}))
        .deposit(NearToken::from_millinear(10))
        .transact()
        .await?
        .into_result()?;
    Ok(())
}

async fn farm_balance(w: &World, who: &Account) -> anyhow::Result<u128> {
    let b: String = w.farm.view("ft_balance_of").args_json(json!({ "account_id": who.id() })).await?.json::<near_sdk::json_types::U128>()?.0.to_string();
    Ok(b.parse()?)
}

async fn give_farm(w: &World, to: &Account, amount: u128) -> anyhow::Result<()> {
    register(&w.farm, to).await?;
    w.treasury
        .call(w.farm.id(), "ft_transfer")
        .args_json(json!({ "receiver_id": to.id(), "amount": amount.to_string() }))
        .deposit(NearToken::from_yoctonear(1))
        .transact()
        .await?
        .into_result()?;
    Ok(())
}

async fn mint(w: &World, who: &Account, token_id: &str) -> anyhow::Result<()> {
    let now_ms = w.worker.view_block().await?.timestamp() / 1_000_000;
    let voucher = MintVoucher {
        contract_id: w.items.id().as_str().parse()?,
        token_id: token_id.into(),
        receiver_id: who.id().as_str().parse()?,
        kind: ItemKind::Good,
        item_id: "wheat".into(),
        quantity: 250,
        props: "{}".into(),
        title: "Wheat ×250".into(),
        media: "items/wheat.png".into(),
        expires_at_ms: now_ms + 10 * 60 * 1000,
    };
    let sig = Base64VecU8::from(w.signer.sign(&voucher_signing_bytes(&voucher)).to_bytes().to_vec());
    let pk = PublicKey::from_parts(CurveType::ED25519, w.signer.verifying_key().to_bytes().to_vec()).unwrap();
    who.call(w.items.id(), "mint_with_voucher")
        .args_json(json!({ "voucher": voucher, "signer": pk, "signature": sig }))
        .deposit(NearToken::from_millinear(50))
        .max_gas()
        .transact()
        .await?
        .into_result()?;
    Ok(())
}

async fn list(w: &World, who: &Account, token_id: &str, price: u128) -> anyhow::Result<()> {
    who.call(w.market.id(), "storage_deposit")
        .args_json(json!({}))
        .deposit(NearToken::from_millinear(10))
        .transact()
        .await?
        .into_result()?;
    who.call(w.items.id(), "nft_approve")
        .args_json(json!({ "token_id": token_id, "account_id": w.market.id(), "msg": json!({ "price": price.to_string() }).to_string() }))
        .deposit(NearToken::from_millinear(10))
        .max_gas()
        .transact()
        .await?
        .into_result()?;
    Ok(())
}

async fn buy(w: &World, buyer: &Account, token_id: &str, amount: u128) -> anyhow::Result<()> {
    buyer
        .call(w.farm.id(), "ft_transfer_call")
        .args_json(json!({
            "receiver_id": w.market.id(),
            "amount": amount.to_string(),
            "msg": json!({ "nft_contract_id": w.items.id(), "token_id": token_id }).to_string()
        }))
        .deposit(NearToken::from_yoctonear(1))
        .max_gas()
        .transact()
        .await?
        .into_result()?;
    Ok(())
}

async fn owner_of(w: &World, token_id: &str) -> anyhow::Result<Option<String>> {
    let t: Option<serde_json::Value> = w.items.view("nft_token").args_json(json!({ "token_id": token_id })).await?.json()?;
    Ok(t.map(|t| t["owner_id"].as_str().unwrap().to_string()))
}

#[tokio::test]
async fn mint_list_buy_redeem() -> anyhow::Result<()> {
    let w = world().await?;
    let alice = sub(&w.worker, "alice", 20).await?;
    let bob = sub(&w.worker, "bob", 20).await?;
    register(&w.farm, &alice).await?;
    give_farm(&w, &bob, 10_000).await?;
    let treasury_before = farm_balance(&w, &w.treasury).await?;

    mint(&w, &alice, "g-1").await?;
    assert_eq!(owner_of(&w, "g-1").await?.as_deref(), Some(alice.id().as_str()));
    list(&w, &alice, "g-1", PRICE).await?;
    let listing: Option<serde_json::Value> =
        w.market.view("get_listing").args_json(json!({ "nft_contract_id": w.items.id(), "token_id": "g-1" })).await?.json()?;
    assert_eq!(listing.unwrap()["price"], PRICE.to_string());

    buy(&w, &bob, "g-1", PRICE).await?;
    assert_eq!(owner_of(&w, "g-1").await?.as_deref(), Some(bob.id().as_str()));
    // fee 5 % = 50; royalty 2.5 % of the remaining 950 = 23; seller 927.
    assert_eq!(farm_balance(&w, &bob).await?, 10_000 - PRICE);
    assert_eq!(farm_balance(&w, &alice).await?, 927);
    assert_eq!(farm_balance(&w, &w.treasury).await? - treasury_before, 73);
    assert_eq!(farm_balance(&w, w.market.as_account()).await?, 0);

    let out = bob.call(w.items.id(), "redeem").args_json(json!({ "token_id": "g-1" })).deposit(NearToken::from_yoctonear(1)).transact().await?;
    assert!(out.logs().iter().any(|l| l.contains("item_redeem") && l.contains("\"quantity\":250")));
    out.into_result()?;
    assert_eq!(owner_of(&w, "g-1").await?, None);
    let burned: bool = w.items.view("is_burned").args_json(json!({ "token_id": "g-1" })).await?.json()?;
    assert!(burned);
    Ok(())
}

#[tokio::test]
async fn buyer_is_refunded_when_the_token_moved() -> anyhow::Result<()> {
    let w = world().await?;
    let alice = sub(&w.worker, "alice", 20).await?;
    let bob = sub(&w.worker, "bob", 20).await?;
    let carol = sub(&w.worker, "carol", 20).await?;
    register(&w.farm, &alice).await?;
    give_farm(&w, &bob, 10_000).await?;

    mint(&w, &alice, "g-2").await?;
    list(&w, &alice, "g-2", PRICE).await?;
    // Alice gives the token away directly: the market's approval is cleared, the listing is stale.
    alice
        .call(w.items.id(), "nft_transfer")
        .args_json(json!({ "receiver_id": carol.id(), "token_id": "g-2" }))
        .deposit(NearToken::from_yoctonear(1))
        .transact()
        .await?
        .into_result()?;

    buy(&w, &bob, "g-2", PRICE).await?;
    assert_eq!(owner_of(&w, "g-2").await?.as_deref(), Some(carol.id().as_str()));
    assert_eq!(farm_balance(&w, &bob).await?, 10_000, "refunded in full");
    assert_eq!(farm_balance(&w, &alice).await?, 0);
    let listing: Option<serde_json::Value> =
        w.market.view("get_listing").args_json(json!({ "nft_contract_id": w.items.id(), "token_id": "g-2" })).await?.json()?;
    assert!(listing.is_none());
    Ok(())
}

#[tokio::test]
async fn unregistered_seller_is_owed_and_can_withdraw() -> anyhow::Result<()> {
    let w = world().await?;
    let dave = sub(&w.worker, "dave", 20).await?; // never registered with FARM
    let bob = sub(&w.worker, "bob", 20).await?;
    give_farm(&w, &bob, 10_000).await?;

    mint(&w, &dave, "g-3").await?;
    list(&w, &dave, "g-3", PRICE).await?;
    buy(&w, &bob, "g-3", PRICE).await?;
    assert_eq!(owner_of(&w, "g-3").await?.as_deref(), Some(bob.id().as_str()));
    let owed: near_sdk::json_types::U128 = w.market.view("owed_of").args_json(json!({ "account_id": dave.id() })).await?.json()?;
    assert_eq!(owed.0, 927);

    register(&w.farm, &dave).await?;
    dave.call(w.market.id(), "withdraw_owed").max_gas().transact().await?.into_result()?;
    assert_eq!(farm_balance(&w, &dave).await?, 927);
    let owed: near_sdk::json_types::U128 = w.market.view("owed_of").args_json(json!({ "account_id": dave.id() })).await?.json()?;
    assert_eq!(owed.0, 0);
    Ok(())
}

#[tokio::test]
async fn a_voucher_cannot_be_replayed() -> anyhow::Result<()> {
    let w = world().await?;
    let alice = sub(&w.worker, "alice", 20).await?;
    mint(&w, &alice, "g-4").await?;
    alice.call(w.items.id(), "redeem").args_json(json!({ "token_id": "g-4" })).deposit(NearToken::from_yoctonear(1)).transact().await?.into_result()?;
    let again = mint(&w, &alice, "g-4").await;
    assert!(again.is_err(), "a burned token id must never mint again");
    Ok(())
}

// ------------------------------------------------------------------ v2: drops, offers, rentals

const PASS: u128 = 250;

async fn open_shop(w: &World) -> anyhow::Result<()> {
    w.dao
        .call(w.items.id(), "set_drop_payment")
        .args_json(json!({ "farm_id": w.farm.id(), "treasury_id": w.treasury.id() }))
        .deposit(NearToken::from_yoctonear(1))
        .transact()
        .await?
        .into_result()?;
    register(&w.farm, w.items.as_account()).await?;
    w.studio
        .call(w.items.id(), "create_drop")
        .args_json(json!({ "drop": {
            "id": "farm_pass_s1", "kind": "pack", "item_id": "farm_pass", "quantity": 1, "props": "{\"season\":1}",
            "title": "Farm Pass · Founders", "media": "items/farm_pass.png", "price": PASS.to_string(),
            "cap": 2, "per_wallet": 5, "starts_at_ms": 0, "ends_at_ms": null
        }}))
        .deposit(NearToken::from_millinear(100))
        .transact()
        .await?
        .into_result()?;
    Ok(())
}

async fn ft_call(w: &World, from: &Account, to: &near_workspaces::AccountId, amount: u128, msg: serde_json::Value) -> anyhow::Result<near_workspaces::result::ExecutionFinalResult> {
    Ok(from
        .call(w.farm.id(), "ft_transfer_call")
        .args_json(json!({ "receiver_id": to, "amount": amount.to_string(), "msg": msg.to_string() }))
        .deposit(NearToken::from_yoctonear(1))
        .max_gas()
        .transact()
        .await?)
}

#[tokio::test]
async fn drop_sells_editions_and_buy_and_use_redeems_at_once() -> anyhow::Result<()> {
    let w = world().await?;
    open_shop(&w).await?;
    let bob = sub(&w.worker, "bob", 20).await?;
    give_farm(&w, &bob, 10_000).await?;
    let treasury_before = farm_balance(&w, &w.treasury).await?;

    ft_call(&w, &bob, w.items.id(), PASS, json!({ "drop": "farm_pass_s1" })).await?.into_result()?;
    assert_eq!(owner_of(&w, "d-farm_pass_s1-1").await?.as_deref(), Some(bob.id().as_str()));
    assert_eq!(farm_balance(&w, &w.treasury).await? - treasury_before, PASS, "takings forwarded");
    assert_eq!(farm_balance(&w, w.items.as_account()).await?, 0);

    let out = ft_call(&w, &bob, w.items.id(), PASS, json!({ "drop": "farm_pass_s1", "use": true })).await?;
    assert!(out.logs().iter().any(|l| l.contains("item_redeem") && l.contains("farm_pass")));
    out.into_result()?;
    assert_eq!(owner_of(&w, "d-farm_pass_s1-2").await?, None);

    // cap 2: the third buy fails and the FARM comes back
    let _ = ft_call(&w, &bob, w.items.id(), PASS, json!({ "drop": "farm_pass_s1" })).await?;
    assert_eq!(farm_balance(&w, &bob).await?, 10_000 - 2 * PASS, "sold out: refunded");
    let d: serde_json::Value = w.items.view("get_drop").args_json(json!({ "id": "farm_pass_s1" })).await?.json()?;
    assert_eq!(d["sold"], 2);
    Ok(())
}

#[tokio::test]
async fn token_and_item_offers_settle_on_chain() -> anyhow::Result<()> {
    let w = world().await?;
    let alice = sub(&w.worker, "alice", 20).await?;
    let bob = sub(&w.worker, "bob", 20).await?;
    register(&w.farm, &alice).await?;
    give_farm(&w, &bob, 10_000).await?;
    bob.call(w.market.id(), "storage_deposit").args_json(json!({})).deposit(NearToken::from_millinear(20)).transact().await?.into_result()?;
    let now_ms = w.worker.view_block().await?.timestamp() / 1_000_000;
    let expires = now_ms + 24 * 60 * 60 * 1000;

    mint(&w, &alice, "g-5").await?;
    ft_call(&w, &bob, w.market.id(), 800, json!({ "offer": { "nft_contract_id": w.items.id(), "token_id": "g-5", "expires_at_ms": expires } }))
        .await?
        .into_result()?;
    assert_eq!(farm_balance(&w, w.market.as_account()).await?, 800, "escrowed");
    alice
        .call(w.items.id(), "nft_approve")
        .args_json(json!({ "token_id": "g-5", "account_id": w.market.id(), "msg": json!({ "accept_offer": 1 }).to_string() }))
        .deposit(NearToken::from_millinear(10))
        .max_gas()
        .transact()
        .await?
        .into_result()?;
    assert_eq!(owner_of(&w, "g-5").await?.as_deref(), Some(bob.id().as_str()));
    // fee 5 % = 40; royalty 2.5 % of 760 = 19; seller 741
    assert_eq!(farm_balance(&w, &alice).await?, 741);
    assert_eq!(farm_balance(&w, w.market.as_account()).await?, 0);

    // any Wheat ×100 or more
    mint(&w, &alice, "g-6").await?;
    ft_call(&w, &bob, w.market.id(), 500, json!({ "offer": { "nft_contract_id": w.items.id(), "item_id": "wheat", "min_quantity": 100, "expires_at_ms": expires } }))
        .await?
        .into_result()?;
    alice
        .call(w.items.id(), "nft_approve")
        .args_json(json!({ "token_id": "g-6", "account_id": w.market.id(), "msg": json!({ "accept_offer": 2 }).to_string() }))
        .deposit(NearToken::from_millinear(10))
        .max_gas()
        .transact()
        .await?
        .into_result()?;
    assert_eq!(owner_of(&w, "g-6").await?.as_deref(), Some(bob.id().as_str()));
    assert_eq!(farm_balance(&w, w.market.as_account()).await?, 0);
    Ok(())
}

#[tokio::test]
async fn workers_are_rented_and_earn_for_the_lender() -> anyhow::Result<()> {
    let w = world().await?;
    let alice = sub(&w.worker, "alice", 20).await?;
    let bob = sub(&w.worker, "bob", 20).await?;
    register(&w.farm, &alice).await?;
    give_farm(&w, &bob, 10_000).await?;
    alice.call(w.market.id(), "storage_deposit").args_json(json!({})).deposit(NearToken::from_millinear(30)).transact().await?.into_result()?;
    for t in ["s-w1", "s-w2"] {
        w.studio
            .call(w.items.id(), "studio_mint")
            .args_json(json!({ "token_id": t, "receiver_id": alice.id(), "kind": "worker", "item_id": "farmhand", "quantity": 1,
                "props": "{\"name\":\"Mia\"}", "title": "Mia", "media": "items/farmhand.png" }))
            .deposit(NearToken::from_millinear(50))
            .transact()
            .await?
            .into_result()?;
    }
    let rent_out = |token: &'static str| {
        alice
            .call(w.items.id(), "nft_transfer_call")
            .args_json(json!({ "receiver_id": w.market.id(), "token_id": token, "msg": json!({ "rent": { "price_per_day": "100", "max_days": 7 } }).to_string() }))
            .deposit(NearToken::from_yoctonear(1))
            .max_gas()
            .transact()
    };
    rent_out("s-w1").await?.into_result()?;
    assert_eq!(owner_of(&w, "s-w1").await?.as_deref(), Some(w.market.id().as_str()), "held by the market");

    ft_call(&w, &bob, w.market.id(), 200, json!({ "rent": { "nft_contract_id": w.items.id(), "token_id": "s-w1", "days": 2 } }))
        .await?
        .into_result()?;
    let r: serde_json::Value = w.market.view("get_rental").args_json(json!({ "nft_contract_id": w.items.id(), "token_id": "s-w1" })).await?.json()?;
    assert_eq!(r["renter_id"], bob.id().as_str());
    assert_eq!(r["earned"], "190");

    alice
        .call(w.market.id(), "rent_withdraw")
        .args_json(json!({ "nft_contract_id": w.items.id(), "token_id": "s-w1" }))
        .deposit(NearToken::from_yoctonear(1))
        .max_gas()
        .transact()
        .await?
        .into_result()?;
    assert_eq!(farm_balance(&w, &alice).await?, 190);
    let early = alice
        .call(w.market.id(), "rent_end")
        .args_json(json!({ "nft_contract_id": w.items.id(), "token_id": "s-w1" }))
        .deposit(NearToken::from_yoctonear(1))
        .max_gas()
        .transact()
        .await?;
    assert!(early.into_result().is_err(), "can't end a running rental");

    // a worker nobody rented comes straight back
    rent_out("s-w2").await?.into_result()?;
    alice
        .call(w.market.id(), "rent_end")
        .args_json(json!({ "nft_contract_id": w.items.id(), "token_id": "s-w2" }))
        .deposit(NearToken::from_yoctonear(1))
        .max_gas()
        .transact()
        .await?
        .into_result()?;
    assert_eq!(owner_of(&w, "s-w2").await?.as_deref(), Some(alice.id().as_str()));

    // goods can't be rented: the token goes back
    mint(&w, &alice, "g-7").await?;
    alice
        .call(w.items.id(), "nft_transfer_call")
        .args_json(json!({ "receiver_id": w.market.id(), "token_id": "g-7", "msg": json!({ "rent": { "price_per_day": "100", "max_days": 7 } }).to_string() }))
        .deposit(NearToken::from_yoctonear(1))
        .max_gas()
        .transact()
        .await?
        .into_result()?;
    assert_eq!(owner_of(&w, "g-7").await?.as_deref(), Some(alice.id().as_str()));
    Ok(())
}
