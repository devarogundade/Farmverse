-- The marketplace's view of the chain (docs 13 §9), written only by the indexer process
-- (src/indexer). It lives in its own schema, which Prisma doesn't model, so `migrate dev`
-- never diffs or drops it. The game never trusts these tables for value-moving decisions.

CREATE SCHEMA IF NOT EXISTS chain;

-- Where each indexer stream has got to (the last fully applied block).
CREATE TABLE chain.cursors (
  name          TEXT PRIMARY KEY,
  block_height  BIGINT      NOT NULL,
  updated_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Every NEP-297 event our contracts emitted in a successful receipt. Append-only; the id makes
-- re-applying a block a no-op.
CREATE TABLE chain.events (
  id            TEXT PRIMARY KEY,              -- '<receipt_id>:<log index>'
  block_height  BIGINT      NOT NULL,
  block_time    TIMESTAMPTZ NOT NULL,
  tx_hash       TEXT,
  receipt_id    TEXT        NOT NULL,
  log_index     INT         NOT NULL,
  contract_id   TEXT        NOT NULL,
  standard      TEXT        NOT NULL,
  event         TEXT        NOT NULL,
  data          JSONB       NOT NULL
);
CREATE INDEX events_block_idx ON chain.events (block_height);
CREATE INDEX events_contract_event_idx ON chain.events (contract_id, event);

-- One row per token ever minted (burned ones stay, with burned_at).
CREATE TABLE chain.tokens (
  contract_id   TEXT        NOT NULL,
  token_id      TEXT        NOT NULL,
  owner_id      TEXT        NOT NULL,
  -- The contract's ItemKind (good, boost, building, land, tool, pack, worker).
  kind          TEXT        NOT NULL,
  -- The marketplace renderer type (worker, tractor, building, land, good, animal); null when the
  -- marketplace has no way to show it (boosts, packs).
  nft_type      TEXT,
  item_id       TEXT        NOT NULL,
  quantity      BIGINT      NOT NULL,
  props         JSONB       NOT NULL DEFAULT '{}',
  title         TEXT        NOT NULL,
  media         TEXT,
  source        TEXT        NOT NULL,          -- voucher | studio
  minted_from   TEXT,
  minted_at     TIMESTAMPTZ NOT NULL,
  burned_at     TIMESTAMPTZ,
  updated_at    TIMESTAMPTZ NOT NULL,
  PRIMARY KEY (contract_id, token_id)
);
CREATE INDEX tokens_owner_idx ON chain.tokens (owner_id) WHERE burned_at IS NULL;
CREATE INDEX tokens_item_idx ON chain.tokens (item_id);

-- The live order book: one row per listed token, removed on delist, sale, transfer or burn.
CREATE TABLE chain.listings (
  contract_id   TEXT          NOT NULL,
  token_id      TEXT          NOT NULL,
  seller_id     TEXT          NOT NULL,
  approval_id   BIGINT        NOT NULL,
  price         NUMERIC(40,0) NOT NULL,        -- yocto-FARM
  listed_at     TIMESTAMPTZ   NOT NULL,
  market_id     TEXT          NOT NULL,
  PRIMARY KEY (contract_id, token_id)
);
CREATE INDEX listings_price_idx ON chain.listings (price, token_id);
CREATE INDEX listings_listed_idx ON chain.listings (listed_at DESC, token_id);

CREATE TABLE chain.sales (
  id            TEXT PRIMARY KEY,              -- the market_sale event id
  contract_id   TEXT          NOT NULL,
  token_id      TEXT          NOT NULL,
  item_id       TEXT          NOT NULL,
  seller_id     TEXT          NOT NULL,
  buyer_id      TEXT          NOT NULL,
  price         NUMERIC(40,0) NOT NULL,
  fee           NUMERIC(40,0) NOT NULL,
  sold_at       TIMESTAMPTZ   NOT NULL
);
CREATE INDEX sales_item_idx ON chain.sales (item_id, sold_at);
CREATE INDEX sales_token_idx ON chain.sales (contract_id, token_id);

-- A token's history as the marketplace shows it, and the source of account notifications.
CREATE TABLE chain.activity (
  id            TEXT PRIMARY KEY,              -- the event id it came from
  contract_id   TEXT          NOT NULL,
  token_id      TEXT          NOT NULL,
  kind          TEXT          NOT NULL,        -- minted | listed | bought | delisted | redeemed | sent
  account_id    TEXT          NOT NULL,        -- who acted (minter, seller, buyer, sender, redeemer)
  counterparty  TEXT,                          -- the other side (seller of a sale, receiver of a send)
  price         NUMERIC(40,0),
  at            TIMESTAMPTZ   NOT NULL,
  block_height  BIGINT        NOT NULL
);
CREATE INDEX activity_token_idx ON chain.activity (contract_id, token_id, at DESC);
CREATE INDEX activity_counterparty_idx ON chain.activity (counterparty, at DESC);
CREATE INDEX activity_account_idx ON chain.activity (account_id, at DESC);
