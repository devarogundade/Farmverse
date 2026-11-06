//! Farmverse's own NEP-297 events (the NEP-171 mint/transfer/burn events come from the standards
//! crate). The indexer and the game server read these from execution outcome logs.

use crate::item::Item;
use near_sdk::serde::Serialize;
use near_sdk::serde_json::json;
use near_sdk::{AccountId, log};

pub const STANDARD: &str = "farmverse_items";
pub const VERSION: &str = "1.0.0";

fn emit<T: Serialize>(event: &str, data: T) {
    let body = json!({ "standard": STANDARD, "version": VERSION, "event": event, "data": [data] });
    log!("EVENT_JSON:{}", body);
}

/// A token was minted: `source` is `voucher` (out of an island), `studio` or `drop`. Title and media
/// ride along so the indexer doesn't depend on the call's arguments (a drop mints inside `ft_on_transfer`).
pub fn item_mint(token_id: &str, owner_id: &AccountId, item: &Item, source: &str, title: &str, media: &str) {
    emit("item_mint", json!({ "token_id": token_id, "owner_id": owner_id, "source": source, "item": item, "title": title, "media": media }));
}

/// A token was burned so its contents can be credited to `owner_id`'s island.
/// The game credits each `token_id` at most once.
pub fn item_redeem(token_id: &str, owner_id: &AccountId, item: &Item) {
    emit("item_redeem", json!({ "token_id": token_id, "owner_id": owner_id, "item": item }));
}

/// An admin setting changed (owner, minter, signers, royalties, pause, base URI).
pub fn config_change(field: &str, value: near_sdk::serde_json::Value) {
    emit("config_change", json!({ "field": field, "value": value }));
}

/// A drop was created or changed (the whole drop, so the indexer can mirror it).
pub fn drop_update(action: &str, drop: &crate::drops::Drop) {
    emit("drop_update", json!({ "action": action, "drop": drop }));
}

/// One edition of a drop was sold (`used`: minted and redeemed at once).
pub fn drop_sale(drop_id: &str, token_id: &str, buyer_id: &AccountId, price: near_sdk::json_types::U128, edition: u64, used: bool) {
    emit("drop_sale", json!({ "drop_id": drop_id, "token_id": token_id, "buyer_id": buyer_id, "price": price, "edition": edition, "used": used }));
}
