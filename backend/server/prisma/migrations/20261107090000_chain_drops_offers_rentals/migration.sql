-- Contracts v2 (plan 15 §5, §12): drops on the items contract, offers and rentals on the market.
-- Written by the indexer only, like the rest of schema `chain`.

-- One row per drop, mirrored from drop_update (the whole drop) and kept current by drop_sale.
CREATE TABLE chain.drops (
  contract_id   TEXT          NOT NULL,
  drop_id       TEXT          NOT NULL,
  kind          TEXT          NOT NULL,
  item_id       TEXT          NOT NULL,
  quantity      BIGINT        NOT NULL,
  props         JSONB         NOT NULL DEFAULT '{}',
  title         TEXT          NOT NULL,
  media         TEXT          NOT NULL,
  price         NUMERIC(40,0) NOT NULL,        -- yocto-FARM
  cap           BIGINT,                        -- null: open edition
  sold          BIGINT        NOT NULL DEFAULT 0,
  per_wallet    INT,
  starts_at     TIMESTAMPTZ   NOT NULL,
  ends_at       TIMESTAMPTZ,
  paused        BOOLEAN       NOT NULL DEFAULT false,
  updated_at    TIMESTAMPTZ   NOT NULL,
  PRIMARY KEY (contract_id, drop_id)
);

-- Offers escrowed on the market: on one token, or on any token of an item.
CREATE TABLE chain.offers (
  market_id     TEXT          NOT NULL,
  offer_id      BIGINT        NOT NULL,
  contract_id   TEXT          NOT NULL,        -- the NFT contract
  buyer_id      TEXT          NOT NULL,
  token_id      TEXT,
  item_id       TEXT,
  min_quantity  BIGINT,
  amount        NUMERIC(40,0) NOT NULL,
  made_at       TIMESTAMPTZ   NOT NULL,
  expires_at    TIMESTAMPTZ   NOT NULL,
  status        TEXT          NOT NULL,        -- open | accepted | cancelled
  closed_at     TIMESTAMPTZ,
  PRIMARY KEY (market_id, offer_id)
);
CREATE INDEX offers_token_idx ON chain.offers (contract_id, token_id) WHERE status = 'open';
CREATE INDEX offers_item_idx ON chain.offers (contract_id, item_id) WHERE status = 'open';
CREATE INDEX offers_buyer_idx ON chain.offers (buyer_id);

-- Workers held by the market for rent. The row goes when the lender takes the worker back.
CREATE TABLE chain.rentals (
  contract_id     TEXT          NOT NULL,
  token_id        TEXT          NOT NULL,
  market_id       TEXT          NOT NULL,
  lender_id       TEXT          NOT NULL,
  price_per_day   NUMERIC(40,0) NOT NULL,
  max_days        INT           NOT NULL,
  renter_id       TEXT,
  rented_until    TIMESTAMPTZ,
  earned          NUMERIC(40,0) NOT NULL DEFAULT 0,
  listed_at       TIMESTAMPTZ   NOT NULL,
  updated_at      TIMESTAMPTZ   NOT NULL,
  PRIMARY KEY (contract_id, token_id)
);
CREATE INDEX rentals_lender_idx ON chain.rentals (lender_id);
CREATE INDEX rentals_renter_idx ON chain.rentals (renter_id);
