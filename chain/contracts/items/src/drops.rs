//! Drops: fixed-price primary sales paid in FARM, with an optional cap enforced here.
//!
//! The minter creates a drop (what each token holds, price, cap, window, per-wallet limit). A buyer
//! sends exactly `price` FARM with `farm.ft_transfer_call(items, price, {"drop": id})`; the contract
//! mints the next edition to them in the same receipt and forwards the FARM to the treasury. With
//! `"use": true` the token is minted and redeemed at once (one wallet approval for "buy and use").
//! Any failure panics, and the FT standard returns the FARM to the buyer.

use near_sdk::json_types::U128;
use near_sdk::serde_json::{self, Value};
use near_sdk::{env, near, require};

use crate::item::{self, ItemKind, MAX_TITLE_LEN};

pub const MAX_DROP_ID_LEN: usize = 24;
/// Token ids of drop editions start with this (`d-<drop>-<edition>`), apart from vouchers and studio mints.
pub const DROP_PREFIX: &str = "d-";

#[near(serializers = [borsh, json])]
#[derive(Clone, Debug, PartialEq)]
pub struct Drop {
    pub id: String,
    pub kind: ItemKind,
    pub item_id: String,
    pub quantity: u64,
    /// JSON object; each edition gets `drop` and `edition` added.
    pub props: String,
    /// Edition titles add ` #n` when the drop has a cap.
    pub title: String,
    pub media: String,
    /// FARM units (yocto-FARM, 18 decimals).
    pub price: U128,
    /// `None` = open edition.
    pub cap: Option<u64>,
    pub sold: u64,
    pub per_wallet: Option<u32>,
    pub starts_at_ms: u64,
    pub ends_at_ms: Option<u64>,
    pub paused: bool,
}

/// What the minter sends to create a drop.
#[near(serializers = [json])]
#[derive(Clone, Debug)]
pub struct DropInput {
    pub id: String,
    pub kind: ItemKind,
    pub item_id: String,
    pub quantity: u64,
    pub props: String,
    pub title: String,
    pub media: String,
    pub price: U128,
    pub cap: Option<u64>,
    pub per_wallet: Option<u32>,
    pub starts_at_ms: u64,
    pub ends_at_ms: Option<u64>,
}

/// Changes to a live drop; absent fields stay as they are. The cap may only go down (never below `sold`).
#[near(serializers = [json])]
#[derive(Clone, Debug, Default)]
pub struct DropUpdate {
    pub price: Option<U128>,
    pub cap: Option<u64>,
    pub per_wallet: Option<u32>,
    pub starts_at_ms: Option<u64>,
    /// `Some(0)` removes the end time.
    pub ends_at_ms: Option<u64>,
    pub paused: Option<bool>,
}

/// The `msg` of a FARM `ft_transfer_call` to this contract.
#[near(serializers = [json])]
pub struct BuyMsg {
    pub drop: String,
    /// Mint and redeem in one go (the game's "buy and use").
    #[serde(rename = "use", default)]
    pub use_now: bool,
}

pub fn assert_drop_id(id: &str) {
    let b = id.as_bytes();
    require!(!b.is_empty() && b.len() <= MAX_DROP_ID_LEN, "drop id must be 1-24 characters");
    require!(b[0].is_ascii_lowercase(), "drop id must start with a lowercase letter");
    require!(b.iter().all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || *c == b'_'), "drop id may only contain a-z, 0-9 and '_'");
}

impl Drop {
    pub fn from_input(d: DropInput) -> Self {
        assert_drop_id(&d.id);
        item::assert_item(d.kind, &d.item_id, d.quantity, &d.props, &d.title, &d.media);
        require!(d.price.0 > 0, "price must be above zero");
        if let Some(cap) = d.cap {
            require!(cap > 0, "cap must be above zero");
        }
        if let Some(w) = d.per_wallet {
            require!(w > 0, "per_wallet must be above zero");
        }
        if let Some(end) = d.ends_at_ms {
            require!(end > d.starts_at_ms, "ends_at_ms must be after starts_at_ms");
        }
        Self {
            id: d.id,
            kind: d.kind,
            item_id: d.item_id,
            quantity: d.quantity,
            props: d.props,
            title: d.title,
            media: d.media,
            price: d.price,
            cap: d.cap,
            sold: 0,
            per_wallet: d.per_wallet,
            starts_at_ms: d.starts_at_ms,
            ends_at_ms: d.ends_at_ms,
            paused: false,
        }
    }

    pub fn apply(&mut self, u: DropUpdate) {
        if let Some(price) = u.price {
            require!(price.0 > 0, "price must be above zero");
            self.price = price;
        }
        if let Some(cap) = u.cap {
            require!(self.cap.is_none_or(|c| cap <= c), "cap can only go down");
            require!(cap >= self.sold, "cap can't go below the number sold");
            require!(cap > 0, "cap must be above zero");
            self.cap = Some(cap);
        }
        if let Some(w) = u.per_wallet {
            require!(w > 0, "per_wallet must be above zero");
            self.per_wallet = Some(w);
        }
        if let Some(s) = u.starts_at_ms {
            self.starts_at_ms = s;
        }
        if let Some(e) = u.ends_at_ms {
            self.ends_at_ms = if e == 0 { None } else { Some(e) };
        }
        if let Some(end) = self.ends_at_ms {
            require!(end > self.starts_at_ms, "ends_at_ms must be after starts_at_ms");
        }
        if let Some(p) = u.paused {
            self.paused = p;
        }
    }

    /// Panics unless a buyer who already bought `bought` may buy one now for `amount`.
    pub fn assert_buyable(&self, now_ms: u64, amount: u128, bought: u32) {
        require!(!self.paused, "This drop is paused");
        require!(now_ms >= self.starts_at_ms, "This drop hasn't started");
        require!(self.ends_at_ms.is_none_or(|e| now_ms < e), "This drop has ended");
        require!(self.cap.is_none_or(|c| self.sold < c), "Sold out");
        require!(self.per_wallet.is_none_or(|w| bought < w), "You already have the most this drop allows");
        require!(amount == self.price.0, "Amount must equal the drop price");
    }

    pub fn token_id(&self, edition: u64) -> String {
        format!("{DROP_PREFIX}{}-{edition}", self.id)
    }

    /// The edition's props: the drop's own plus `drop` and `edition`.
    pub fn edition_props(&self, edition: u64) -> String {
        let mut value: Value = serde_json::from_str(&self.props).unwrap_or_else(|_| env::panic_str("props must be JSON"));
        let obj = value.as_object_mut().unwrap_or_else(|| env::panic_str("props must be a JSON object"));
        obj.insert("drop".into(), Value::String(self.id.clone()));
        obj.insert("edition".into(), Value::from(edition));
        let props = value.to_string();
        item::assert_props(&props);
        props
    }

    /// `Farm Pass · Season 1 #12` for capped drops, the plain title otherwise (cut to 64 characters).
    pub fn edition_title(&self, edition: u64) -> String {
        if self.cap.is_none() {
            return self.title.clone();
        }
        let suffix = format!(" #{edition}");
        let room = MAX_TITLE_LEN.saturating_sub(suffix.chars().count());
        let base: String = self.title.chars().take(room).collect();
        format!("{base}{suffix}")
    }
}
