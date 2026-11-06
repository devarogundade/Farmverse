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

/// A token was minted: `source` is `voucher` (out of an island) or `studio`.
pub fn item_mint(token_id: &str, owner_id: &AccountId, item: &Item, source: &str) {
    emit("item_mint", json!({ "token_id": token_id, "owner_id": owner_id, "source": source, "item": item }));
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
