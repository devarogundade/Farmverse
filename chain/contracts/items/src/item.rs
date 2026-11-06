//! What a Farmverse token holds, and the rules every token must satisfy.

use near_contract_standards::non_fungible_token::TokenId;
use near_sdk::serde_json;
use near_sdk::{AccountId, near, require};

/// What sort of game object a token carries. **Borsh order is part of the voucher format**:
/// never reorder or insert in the middle — only append.
#[near(serializers = [borsh, json])]
#[serde(rename_all = "snake_case")]
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum ItemKind {
    /// Crops, animal products, factory products — stackable goods.
    Good,
    /// Fertilizer, rush, shield … — consumable boosts.
    Boost,
    /// A building picked up from an island (with its level in `props`).
    Building,
    /// A land plot.
    Land,
    /// Tractors, axes and other tools.
    Tool,
    /// A studio pack: gems, Farm Pass, bundles.
    Pack,
    /// A worker NFT (studio-minted; hold to use, never redeemed).
    Worker,
}

impl ItemKind {
    /// Workers are used by holding them, so they can't be burned into an island.
    pub fn redeemable(self) -> bool {
        !matches!(self, ItemKind::Worker)
    }
}

/// The game object inside a token. One kind and one `item_id` per token — never mixed.
#[near(serializers = [borsh, json])]
#[derive(Clone, PartialEq, Eq, Debug)]
pub struct Item {
    pub kind: ItemKind,
    /// A catalogue id from `contracts/economy` (e.g. `wheat`, `chicken_coop`).
    pub item_id: String,
    /// How many units the token holds (1 for buildings, land, tools, workers).
    pub quantity: u64,
    /// Item-specific properties as a JSON object (e.g. `{"level":3}`), at most `MAX_PROPS_LEN` bytes.
    pub props: String,
    /// The island (player account) the goods came out of; `None` for studio items.
    pub minted_from: Option<AccountId>,
    pub minted_at_ms: u64,
}

pub const MAX_TOKEN_ID_LEN: usize = 64;
pub const MAX_ITEM_ID_LEN: usize = 40;
pub const MAX_PROPS_LEN: usize = 1024;
pub const MAX_TITLE_LEN: usize = 64;
pub const MAX_MEDIA_LEN: usize = 200;
pub const MAX_QUANTITY: u64 = 1_000_000_000;

/// Token ids from game vouchers start with this; studio ids with [`STUDIO_PREFIX`].
/// Keeping them apart means a studio mint can never collide with a player's voucher.
pub const VOUCHER_PREFIX: &str = "g-";
pub const STUDIO_PREFIX: &str = "s-";

pub fn assert_token_id(token_id: &TokenId, prefix: &str) {
    require!(token_id.len() <= MAX_TOKEN_ID_LEN, "token_id is too long");
    require!(token_id.starts_with(prefix) && token_id.len() > prefix.len(), format!("token_id must start with {prefix}"));
    require!(
        token_id.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'-' || b == b'_'),
        "token_id may only contain letters, digits, '-' and '_'"
    );
}

pub fn assert_item_id(item_id: &str) {
    let b = item_id.as_bytes();
    require!(!b.is_empty() && b.len() <= MAX_ITEM_ID_LEN, "item_id must be 1-40 characters");
    require!(b[0].is_ascii_lowercase(), "item_id must start with a lowercase letter");
    require!(
        b.iter().all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || *c == b'_'),
        "item_id may only contain a-z, 0-9 and '_'"
    );
}

pub fn assert_quantity(kind: ItemKind, quantity: u64) {
    require!((1..=MAX_QUANTITY).contains(&quantity), "quantity must be 1-1000000000");
    if matches!(kind, ItemKind::Building | ItemKind::Land | ItemKind::Worker) {
        require!(quantity == 1, "buildings, land and workers hold exactly one");
    }
}

pub fn assert_props(props: &str) {
    require!(props.len() <= MAX_PROPS_LEN, "props is too long");
    let value: serde_json::Value =
        serde_json::from_str(props).unwrap_or_else(|_| near_sdk::env::panic_str("props must be JSON"));
    require!(value.is_object(), "props must be a JSON object");
}

pub fn assert_title(title: &str) {
    require!(!title.is_empty() && title.chars().count() <= MAX_TITLE_LEN, "title must be 1-64 characters");
}

/// `media` is a path relative to the contract's `base_uri` (e.g. `items/wheat.png`), so moving the
/// art CDN is one metadata update, not a rewrite of every token.
pub fn assert_media(media: &str) {
    require!(!media.is_empty() && media.len() <= MAX_MEDIA_LEN, "media must be 1-200 characters");
    require!(!media.starts_with('/') && !media.contains(".."), "media must be a relative path");
    require!(
        media.bytes().all(|b| b.is_ascii_alphanumeric() || matches!(b, b'-' | b'_' | b'.' | b'/')),
        "media may only contain letters, digits and - _ . /"
    );
}

/// Every field rule at once (vouchers and studio mints share them).
pub fn assert_item(kind: ItemKind, item_id: &str, quantity: u64, props: &str, title: &str, media: &str) {
    assert_item_id(item_id);
    assert_quantity(kind, quantity);
    assert_props(props);
    assert_title(title);
    assert_media(media);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ids() {
        assert_token_id(&"g-01J9ZK".to_string(), VOUCHER_PREFIX);
        assert_item_id("chicken_coop");
        assert_media("items/wheat.png");
        assert_props("{\"level\":3}");
        assert_quantity(ItemKind::Good, 250);
    }

    #[test]
    #[should_panic(expected = "token_id must start with g-")]
    fn studio_id_in_voucher() {
        assert_token_id(&"s-1".to_string(), VOUCHER_PREFIX);
    }

    #[test]
    #[should_panic(expected = "item_id may only contain")]
    fn bad_item_id() {
        assert_item_id("wheat!");
    }

    #[test]
    #[should_panic(expected = "props must be a JSON object")]
    fn props_array() {
        assert_props("[1]");
    }

    #[test]
    #[should_panic(expected = "exactly one")]
    fn two_buildings() {
        assert_quantity(ItemKind::Building, 2);
    }

    #[test]
    #[should_panic(expected = "relative path")]
    fn media_escape() {
        assert_media("../secret.png");
    }

    #[test]
    fn workers_are_not_redeemable() {
        assert!(!ItemKind::Worker.redeemable());
        assert!(ItemKind::Building.redeemable());
    }
}
