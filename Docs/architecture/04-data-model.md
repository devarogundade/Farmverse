# 04 — Firestore data model

Every shape below is a zod schema in `contracts/src/documents` and a generated
C# DTO. This document is the human-readable index; **the schema is the truth**.

Conventions:

- Game timestamps are **epoch milliseconds (`number`)**: `plantedAt`,
  `expiresAt`, `finishAt`. Audit timestamps are Firestore `Timestamp` written
  with `serverTimestamp()`: `createdAt` on ledger, trades, notifications.
- Every aggregate document carries `schemaVersion` (int) and `rev` (int,
  incremented by the server on every write). The client uses `rev` to retire
  optimistic overlays; the server uses `schemaVersion` for lazy migrations.
- Money is an integer number of coins. Quantities are integers.
- **Only Cloud Functions write** game state. "Client reads" below means via the
  SDK with security rules. The two exceptions — a player's own display settings
  and marking a notification read — carry no economic value and are fully
  checked by rules (11 §1.4).

## 1. Map

```
players/{uid}                                  PlayerDoc            aggregate root (profile + wallet)
  private/settings                             SettingsDoc          allowVisitors, push prefs, locale, timezone
  private/meta                                 PlayerMetaDoc        daily bonus, ads, order fills, trade restrictions (server-only read)
  private/limits                               LimitsDoc            rate-limit buckets (server-only)
  state/inventory                              InventoryDoc         every good the player holds
  state/estate                                 EstateDoc            owned objects: tools, facilities, stall, levels, wear
  state/production                             ProductionDoc        machines and their job queues
  plots/{plotId}                               PlotDoc              one hex plot with all its slots embedded
  ledger/{entryId}                             LedgerEntryDoc       append-only coin history
  notifications/{notificationId}               NotificationDoc      inbox (TTL 30 d)
  requests/{batchId}                           RequestDoc           idempotency record (TTL 72 h, server-only)
  pushTokens/{installationId}                  PushTokenDoc         (server-only)

publicProfiles/{uid}                           PublicProfileDoc     what anyone may see about a player
usernames/{displayNameLower}                   UsernameDoc          uniqueness index → uid (server-only)

listings/{listingId}                           ListingDoc           marketplace offers (public read)
trades/{tradeId}                               TradeDoc             settled trade receipts (read by buyer/seller)
marketStats/{itemId}                           MarketStatsDoc       price reference, 24 h, 7-day buckets (public read)

leaderboards/{boardId}/entries/{uid}           LeaderboardEntryDoc  boardId = "global" | "weekly-2026W43"

catalogue/current                              CatalogueDoc         active economy (public read)
catalogueVersions/{version}                    CatalogueDoc         immutable history (admin read)

system/economy/shards/{0..9}                   EconomyCounterDoc    coins minted/burned by reason (sharded)
system/jobs/{jobName}                          JobStateDoc          cursors for scheduled jobs
system/reconcile/runs/{date}                   ReconcileRunDoc      nightly ledger check
adminAudit/{id}                                AdminAuditDoc        every admin action
```

Why this shape (ADR-004): a player's whole farm loads in **≈7 reads**
(player, settings, inventory, estate, production, N plots), and a farming
batch touches **3 documents**. The alternative — a document per crop, item and
tool — costs 50–150 reads per session start and makes transactions large.

## 2. Player

### `players/{uid}` — PlayerDoc

| Field | Type | Notes |
|---|---|---|
| `schemaVersion`, `rev` | int | |
| `displayName` | string 3–16 | unique via `usernames/` |
| `avatarId`, `frameId` | string | catalogue cosmetics |
| `role` | `"farmer" \| "manufacturer"` | |
| `roleChangedAt` | ms | cooldown source |
| `coins` | int ≥ 0 | the balance; only changes with a ledger row |
| `title` | string | derived from net worth, denormalised for display |
| `onboarding` | `{ step: int, completedAt: ms \| null }` | |
| `counters` | `{ activeListings, plots }` | maintained by the server in the same transactions (unread notifications: a `count()` query) |
| `stats` | `{ harvests, itemsProduced, tradesBought, tradesSold, coinsEarned }` | lifetime, for profile + achievements |
| `lastSeenAt` | ms | written by `session-catchUp` |
| `netWorthDirty` | bool | set when holdings change; the leaderboard trigger clears it |
| `createdAt` | Timestamp | |

### `players/{uid}/private/settings` — SettingsDoc (client reads and updates own)

`allowVisitors`, `pushEnabled`, `quietStart`, `quietEnd` (0–23), `locale`,
`timezone` (IANA), `reduceMotion`, `textScale` (80–150). Flat fields so the
security rule checks each one (11 §1.4); `player-updateProfile` changes them too.

### `players/{uid}/private/meta` — PlayerMetaDoc (server writes; the owner may read)

As built: `dailyBonus: { streakDay, lastClaimDay }`, `orders: { dayKey, filled[] }`,
`post: { dayKey, units }` (Trading Post daily cap), `tradeRestrictedUntil`.
The owner may read it (the daily-bonus screen and the order board need it);
nothing in it is secret, and only the server writes it. Rate-limit buckets live
in `private/limits`, which stays server-only.

## 3. Holdings

### `state/inventory` — InventoryDoc

```json
{ "schemaVersion": 1, "rev": 42,
  "items": { "seed_wheat":   { "qty": 12, "reserved": 0 },
             "crop_wheat":   { "qty": 40, "reserved": 20 },
             "crop_wheat~3": { "qty": 6,  "reserved": 0 } } }
```

Keys are **stack keys**: the item id for quality 1, `itemId~2` / `itemId~3` for
two- and three-star goods (`stackKey`/`parseStackKey` in contracts, `StackKeys`
in C#). Capacity counts every quality; listings sell one quality; recipes,
orders and the Trading Post take the lowest quality first.
`reserved` is the part locked in active listings. `free = qty - reserved`.
Capacity is **not stored**: it is derived from the warehouse level in the
estate and the catalogue, by the same domain function on both sides.

### `state/estate` — EstateDoc

```json
{ "objects": {
    "facility_warehouse": { "level": 2, "acquiredAt": 1792…, "condition": 0.93, "conditionAt": 1792… },
    "tool_hoe":           { "level": 1, "usesLeft": 31 } } }
```

Condition decays by a catalogue rate from `conditionAt` (derived, no job).

### `state/production` — ProductionDoc

```json
{ "machines": {
    "m_01": { "objectId": "machine_mill", "level": 2, "plotId": "p_3", "condition": 1, "conditionAt": 0,
              "queue": [ { "jobId": "j_9", "recipeId": "recipe_flour", "batches": 3,
                           "startedAt": 1792…, "finishAt": 1792… } ] } } }
```

Queue length is capped by machine level (catalogue). A job's inputs are taken
when it is queued; its outputs are added when it is collected after
`finishAt`. A queue is sequential: `startedAt` of job *n+1* = `finishAt` of job *n*.

### `plots/{plotId}` — PlotDoc

```json
{ "schemaVersion": 1, "rev": 17, "q": 0, "r": -1, "name": "North field",
  "terrain": "grass", "tilledAt": 1792…, "soil": 0.85, "soilAt": 1792…,
  "well":  { "level": 1, "buckets": 2, "bucketsAt": 1792… },
  "fence": { "level": 1, "condition": 1, "conditionAt": 1792… },
  "crops": [ { "slot": 0, "type": "crop_wheat", "plantedAt": 1792…, "water": 3, "fertilizer": 1,
               "lastHoedAt": 1792…, "lastSprayedAt": 0, "held": 0 } ],
  "livestock": [ { "slot": 0, "id": "a_1", "type": "livestock_chicken", "bornAt": 1792…,
                   "fedUntil": 1792…, "held": 3, "countedAt": 1792… } ],
  "trees":   [ { "slot": 0, "type": "tree_oak", "plantedAt": 1792… } ],
  "machineId": "m_01" }
```

Slot counts per kind come from the catalogue's plot rules (`maximumCrops` 6,
…), never from code. Arrays are kept sorted by `slot` and are at most ~16
elements, so the document stays under 4 KB.

Well buckets are derived: `available = min(capacity, buckets + floor((now - bucketsAt) / refillMs))`;
using water writes the new `buckets` and `bucketsAt = now`.

## 4. History

### `ledger/{entryId}` — LedgerEntryDoc

`amount` (± int), `balanceAfter`, `reason` (enum: `shop_buy`, `shop_upgrade`,
`market_buy`, `market_sale`, `market_fee`, `market_promote`, `post_sell`,
`post_buy`, `order_reward`, `daily_bonus`, `ad_reward`, `starter_pack`,
`repair`, `admin_grant`, …), `ref: { type, id }`, `counterpartyUid`,
`batchId`, `catalogueVersion`, `createdAt`. Append-only; retention 13 months
in Firestore, forever in BigQuery.

### `notifications/{id}` — NotificationDoc

`kind`, `titleKey`, `bodyKey`, `params` (map of strings — localized on the
client), `route` + `routeArgs`, `createdAt`, `readAt | null`, `expireAt`
(TTL = 30 days).

### `requests/{batchId}` — RequestDoc

`result` (the stored response), `createdAt`, `expireAt` (TTL 72 h).

## 5. Marketplace

### `listings/{listingId}` — ListingDoc

| Field | Type | Notes |
|---|---|---|
| `sellerUid`, `sellerName`, `sellerTitle` | string | name/title denormalised for list rows (updated on rename by a small fan-out) |
| `itemId`, `itemCategory` | string | category denormalised for category filters |
| `quality` | int 1–3 | |
| `quantity`, `remaining` | int | partial fills allowed |
| `unitPrice` | int | within the item's price band at creation |
| `status` | `active \| sold \| cancelled \| expired` | |
| `createdAt`, `expiresAt`, `closedAt` | ms | |
| `promotedUntil` | ms (0 = not promoted) | |

### `trades/{tradeId}` — TradeDoc

`listingId, itemId, quality, quantity, unitPrice, gross, fee, net, buyerUid,
sellerUid, buyerName, sellerName, createdAt`. Written by `market.buyListing`.
Readable by its buyer and seller only.

### `marketStats/{itemId}` — MarketStatsDoc

`referencePrice` (rolling 7-day volume-weighted average, clamped to the
catalogue band), `last`, `low24h`, `high24h`, `avg24h`, `volume24h`,
`days: [{ day, avg, volume }] (7)`, `activeListings`, `lowestActive`,
`updatedAt`. Written only by `market-aggregateStats` (never in the trade
transaction — avoids a hot document per popular item).

## 6. Social

- `publicProfiles/{uid}` — `displayName, avatarId, frameId, role, title,
  netWorth, allowVisitors, stats (public subset), createdAt`. Written by the
  leaderboard trigger and profile updates.
- `leaderboards/{boardId}/entries/{uid}` — `netWorth, displayName, avatarId,
  role, title, updatedAt`. `global` is all-time; `weekly-YYYYWww` ranks the net
  worth **gained** this ISO week (fresh boards keep the top reachable).
- `usernames/{lower}` — `{ uid }`, written in the same transaction as a rename.

## 7. Catalogue

`catalogue/current` holds the whole active economy (items, objects with levels,
recipes, crop/animal/tree rules, plot rules, market rules, trading post band,
order demand table, title ladder) and its `version`. Estimated 120–250 KB, well
under the 1 MiB limit; if it grows past 600 KB it is split into
`catalogue/current/parts/{name}`. Clients learn the version from the Remote
Config key `catalogue_version` and only re-read the document when it changes.

## 8. Indexes

Declared in `backend/firestore.indexes.json`, generated from the query table in
06 §4 so that a query the client can build always has an index.

| Collection | Fields | Used by |
|---|---|---|
| `listings` | status ↑, itemId ↑, unitPrice ↑, __name__ | browse item by price |
| `listings` | status ↑, itemId ↑, unitPrice ↓ | price high→low |
| `listings` | status ↑, itemId ↑, createdAt ↓ | newest for an item |
| `listings` | status ↑, itemId ↑, expiresAt ↑ | ending soon for an item |
| `listings` | status ↑, itemCategory ↑, createdAt ↓ | category, newest |
| `listings` | status ↑, itemCategory ↑, unitPrice ↑ | category, cheapest |
| `listings` | status ↑, createdAt ↓ | all, newest |
| `listings` | status ↑, expiresAt ↑ | all, ending soon; expiry sweep |
| `listings` | status ↑, promotedUntil ↓ | promoted carousel |
| `listings` | sellerUid ↑, status ↑, createdAt ↓ | my listings / seller profile |
| `listings` | status ↑, itemId ↑, quality ↓, unitPrice ↑ | quality filter |
| `trades` | buyerUid ↑, createdAt ↓ | purchase history |
| `trades` | sellerUid ↑, createdAt ↓ | sales history |
| `ledger` (collection group off) | createdAt ↓; reason ↑ createdAt ↓ | wallet history + filter |
| `notifications` | readAt ↑, createdAt ↓ | unread first |
| `entries` (leaderboards) | netWorth ↓, __name__ ↑ | ranking pages, rank counting |

Single-field index exemptions: `players.stats`, `plots.crops`, `plots.livestock`,
`state/*.items|objects|machines` (maps and arrays we never query — exempting
them saves index writes and storage).

## 9. TTL policies

`requests.expireAt` (72 h), `notifications.expireAt` (30 d). Configured once per
project with `gcloud firestore fields ttls update`; recorded in
`tools/ci/ttl.sh`.

## 10. Cost budget (per DAU per day, steady state)

| Activity | Reads | Writes |
|---|---|---|
| Session start ×3 (aggregates, catalogue version check via RC) | 25 | 3 |
| Listener updates from ~40 action batches | 90 | — |
| Action batches (request + aggregates + ledger) | 40 × 4 = 160 | 40 × 5 = 200 |
| Marketplace: 8 pages × 20 + 2 details | 170 | — |
| Leaderboard: 2 pages + my rank (count) | 60 | — |
| Inbox, wallet history | 40 | 5 |
| **Total** | **≈ 545** | **≈ 210** |

At list prices (≈ $0.06 / 100k reads, $0.18 / 100k writes) this is about
**$0.0007 per DAU per day**, plus ~50 function invocations. The design target
is < $0.002. Anything that would multiply reads (unbounded listeners, per-item
documents, polling) is rejected in review.

## 11. Migrations

`schemaVersion` on every aggregate. The server reads any supported version and
upgrades it in memory; it writes only the latest version (lazy migration on
the next write). A bulk backfill job is used only when a query needs a new
field. The client accepts `schemaVersion ≤ supported` and shows "update
required" if a document is newer than it understands.
