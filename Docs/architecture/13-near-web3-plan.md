# 13 — Farmverse on the web and on NEAR

> **Status:** proposal, waiting on the decisions in §11
> **Supersedes:** 12 (IAP & cosmetics), the ads sections of 07, Google/Apple sign-in in 02/08
> **Hackathon:** NEAR HACKATHON, development ends **2026-11-14 23:59 UTC** (track ids: `games`, `nfts`, maybe `agents`)

## 1. What changes, in one paragraph

Farmverse stops being a mobile app. It ships as a **Unity WebGL** game at
`play.farmverse.world`, signs players in with a **NEAR wallet** (no Google/Apple/anonymous
auth), and drops **AdMob and in-app purchases** entirely. **Firebase is removed completely**: the
game server becomes a Node/TypeScript service on **Postgres**, with **Redis** for cache, sessions
and pub/sub, and **BullMQ** for jobs (§5.3). The game logic itself (`backend/functions/src/game`,
the C# island engine) carries over through the existing `Store` port. What is new
is a door between the game and NEAR: a player can **mint** goods from their island into an
NFT (the goods leave Firebase), **trade** it for **FARM** on `marketplace.farmverse.world`,
and whoever holds the NFT can **redeem** it back into *their own* island (the NFT is burned).
Revenue moves from ads/IAP to **marketplace fees** and **primary sales** of studio-minted NFTs
(workers, land, packs). `farmverse.world` is the landing page (how to play, FAQ, terms, privacy).

```
            play.farmverse.world (Unity WebGL)               marketplace.farmverse.world (React)
            ─────────────────────────────────                ──────────────────────────────────
  wallet ──► sign-in (NEP-413) ──► session JWT (game API)     browse · filter · sort · my NFTs
            island ──mint request──► game API (Node)           list · delist · buy (FARM)
                                     │ deducts goods, signs                │
                                     ▼ a mint voucher                     │
            wallet signs nft.mint_with_voucher ──────────┐                │
            wallet signs nft.redeem ─────────────────────┤                │
                                                         ▼                ▼
                           NEAR mainnet:  items.farmverse.near   market.farmverse.near   farm.farmverse.near
                                                         │ NEP-297 event logs
                                                         ▼
                                     Goldsky Turbo pipeline ──► Postgres (schema `chain`) ──► game API ──► marketplace UI
            game API verifies redeem tx on RPC ──► credits island in Postgres (once per token)
```

## 2. Principles carried over (and the new ones)

Everything in 00 §3 still binds. Added:

1. **One item exists in one place.** A unit of wheat is either in a player's island *or* inside
   an NFT, never both. Every crossing (mint, redeem) is idempotent and keyed by `token_id`.
2. **The chain is the truth for ownership; Postgres is the truth for gameplay.** The game never
   trusts the indexer for value-moving decisions: redemptions are verified against an RPC
   transaction outcome. The indexer serves browsing only.
3. **The studio's hot key can never move player assets.** The backend only *signs vouchers*;
   it cannot transfer, burn or list anyone's NFT. Contract owners are a multisig.
4. **Players never notice the chain more than they must** (hackathon `games` hint): crops look
   like crops; "Mint" and "Redeem" are the only two chain words in the game.

## 3. The repository after the change

One monorepo (judges review one public repo), three deployables + chain code:

| Path | What | Deploys to |
|---|---|---|
| `Assets/`, `Packages/`, `ProjectSettings/` | Unity game, WebGL target only | `play.farmverse.world` (static host + Brotli) |
| `backend/server` | Node/TS game API (Fastify): HTTP + WebSocket, the `game/*.ts` logic, Postgres store | Container (Cloud Run / Fly / Railway) |
| `backend/worker` | BullMQ workers (same codebase, separate process) | Container |
| `backend/prisma` | Prisma schema + Prisma Migrate migrations (game + auth schemas) | Postgres (Neon / Supabase / RDS) |
| `tools/dotnet/Farmverse.IslandEngine` | C# island engine (unchanged) | Container |
| `contracts/` | zod contract + C# codegen (unchanged role; gains mint/redeem/auth calls) | — |
| `chain/contracts/farm_token` | FARM, NEP-141 (Rust, near-sdk) | `farm.farmverse.near` |
| `chain/contracts/items` | Farmverse items & workers, NEP-171 family (Rust) | `items.farmverse.near` |
| `chain/contracts/market` | Marketplace (Rust) | `market.farmverse.near` |
| `chain/tests` | near-workspaces sandbox tests across all three | CI |
| `chain/indexer` | Goldsky Turbo pipeline YAML + SQL schema | Goldsky + Postgres |
| `web/landing` | React (Vite) landing: home, how to play, FAQ, terms, privacy | `farmverse.world` |
| `web/marketplace` | React (Vite) marketplace | `marketplace.farmverse.world` |
| `web/shared` | wallet connection, contract clients, item art/metadata, design tokens | (package) |

Account names above are placeholders until §11 Q5 is answered.

## 4. Removal list (done first, in its own commit)

| Area | Remove | Replace with |
|---|---|---|
| AdMob | `Packages/com.google.ads.mobile`, `Assets/GoogleMobileAds`, `AdMobRewardedAds.cs`, `IRewardedAds`, `AdsService`, `backend/functions/src/{api,game}/ads.ts`, `AdPlacement`, ad contract types, ad tests | Ad-gated rewards become plain daily claims (or are dropped) — economy review in 07 |
| IAP | `com.unity.purchasing`, `UnityAppStore`, `IAppStore`, `PurchaseService`, `GameUi.Purchases`, `{api,game}/purchases.ts`, `IapProduct/IapKind/IapStore/IapVerify*`, `PurchasesDoc.noAds` | Gem offers either earned in play or sold as NFT packs on the marketplace (§11 Q3) |
| Mobile sign-in | `Plugins/{iOS,Android}/FarmverseGoogleSignIn.*`, `NativeSignIn`, `PlatformLinkFlows`, link rewards | Wallet sign-in (§5) |
| Mobile platform | `FarmverseIosBuild`, `com.unity.mobile.notifications`, `UnityLocalNotifications`, FCM push, Crashlytics, `com.unity.microsoft.gdk*`, Android/iOS/tvOS plugin folders, EDM4U | Web: optional web-push later, Sentry for errors, PostHog for analytics (consent-gated) |
| Firebase (client) | `Assets/Firebase`, `Farmverse.Infrastructure.Firebase` (Auth, Firestore, Functions, Remote Config, Analytics, Installations), `FirebaseComposition`, `FirebaseMetaUpgrade` | `Farmverse.Infrastructure.Web` (§5.1) |
| Firebase (server) | `firebase.json`, `firestore.rules`, `firestore.indexes.json`, `storage.rules`, `remoteconfig.template.json`, `rules-tests/`, `firebase-functions`/`firebase-admin` deps, `infra/firestoreStore.ts`, Cloud Tasks wakes, FCM messenger | Postgres store, BullMQ, config table (§5.3) |
| Docs | 12-iap-cosmetics.md, 11-firebase-leverage-plan.md and the Firebase parts of 03/04/08 marked superseded | this file |

Game logic in `backend/functions/src/game` moves to `backend/server` unchanged apart from imports;
every non-ad, non-IAP action keeps its unit tests, which must stay green after the move.

## 5. The WebGL client

### 5.1 The client's new transport

The client already talks to the backend only through ports (`IAuthService`, `IDocumentStore`,
`ICallableTransport`, remote config, analytics). One new assembly, `Farmverse.Infrastructure.Web`,
implements them over our own API. It works the same in the Editor and in WebGL, so there is no
second adapter set to keep in step.

| Port | Web adapter |
|---|---|
| `IAuthService` | Wallet sign-in (§5.2); holds the session JWT, refreshes it before expiry |
| `ICallableTransport` | `POST /v1/call/{name}` with `UnityWebRequest` (works in WebGL); same envelope, request ids and error codes as today |
| `IDocumentStore.Get` | `GET /v1/doc/{path}` |
| `IDocumentStore.Listen` / `ListenQuery` | One WebSocket per session (`/v1/live`): subscribe/unsubscribe by path or query spec; the server pushes `{path, rev, data}`. WebGL uses a small `.jslib` WebSocket bridge (e.g. NativeWebSocket); the Editor uses `System.Net.WebSockets`. Reconnects resubscribe and drop stale revisions |
| Remote config | `GET /v1/config` (cached, ETag) |
| Analytics / errors | PostHog + Sentry browser SDKs through a tiny `.jslib` |

The documents keep their current paths and shapes (`players/{uid}/state/island`, …), so the
presenters and the reconcile logic in Application don't change.

Also: WebGL build profile (Brotli, decompression fallback off, URP WebGL quality tier, texture
compression ASTC/ETC2 → DXT/ASTC for desktop + mobile web), Addressables served from the same CDN,
portrait UI reflowed for landscape desktop.

### 5.2 Wallet sign-in

1. Game page loads the wallet selector (via `near-api-unity`'s WS template, or our own small
   template in `web/shared` — same API) and the player connects.
2. Client calls `POST /v1/auth/nonce` → server returns a random 32-byte nonce, stored in **Redis**
   with a 5-minute TTL.
3. Wallet signs a **NEP-413** message (`recipient: "play.farmverse.world"`, nonce).
4. Client calls `POST /v1/auth/wallet {accountId, publicKey, signature, nonce}`. Server:
   deletes the nonce atomically (`GETDEL`; a missing nonce means replayed or expired), verifies the
   ed25519 signature over the NEP-413 payload, and checks via RPC `view_access_key` that `publicKey`
   is a **full-access key of `accountId`**. It then returns a short-lived **access JWT** (15 min,
   EdDSA, `sub = uid`, `acc = accountId`) and a rotating **refresh token** (30 days, stored hashed in Postgres,
   revocable).
5. Bot protection: Cloudflare Turnstile on the nonce call, plus per-IP and per-account rate
   limits in Redis.

The marketplace site uses the same sign-in. The island, wallet, goals etc. key off the game uid
(see §5.3) exactly as today. One NEAR account = one island. The contract's `Platform` gained
`webgl`.

### 5.3 The backend: Node/TS + Postgres + Redis + BullMQ

**Processes** (one codebase in `backend/`, two entry points, both stateless containers):

- `server`: Fastify. HTTP API (game calls, document reads, auth, marketplace read API) and the
  WebSocket live channel. zod schemas from `contracts/` validate every request.
- `worker`: BullMQ workers for everything scheduled or delayed.

**Postgres is the source of truth.**

| Schema / table | Holds |
|---|---|
| `game.documents (path PK, uid, rev bigint, data jsonb, updated_at)` | Every game document, path-addressed exactly as Firestore did; `uid` + GIN/expression indexes for the few queries (leaderboards, friends) |
| `game.ledger` | Append-only coin/gem/item ledger (existing ledger rows) |
| `game.idempotency (request_id PK, uid, response jsonb, expires_at)` | Retried calls return the stored response |
| `game.mints`, `game.redemptions`, `game.pickups` | Chain crossings (§7) |
| `auth.refresh_tokens`, `auth.accounts` | Sessions |
| `chain.*` | Goldsky sink (§9), read-only to the game |

**Prisma.** `backend/prisma/schema.prisma` owns the `game` and `auth` schemas (`multiSchema`)
and **Prisma Migrate** creates every migration. The generated client is used with the
`@prisma/adapter-pg` driver adapter so it goes through the same pooler. The `chain` schema is
written by Goldsky, so Prisma does not migrate it. Its tables are pulled in read-only with
`prisma db pull` into a separate `chain.prisma` client, or declared as Prisma `view`s. That keeps
`migrate dev` from ever trying to drop them.

**Store adapter.** `src/store/prismaStore.ts` implements the existing `Store`/`TransactionPort`
(as built, 2026-11-06). Transactions run at **`SERIALIZABLE`** and retry on serialization failure
(exponential backoff, full jitter), mapping to `CONFLICT` only after 12 attempts. This is
Firestore's optimistic model: readers never block (every transaction reads the shared catalogue,
so row locks would have serialized the whole game). Writes are buffered and applied at commit,
and the changed paths go to Redis. Queries are parameterized SQL over `data` JSONB. A
conformance suite runs the same scenarios against the in-memory store the game tests use and
against Postgres.

**Game uid.** NEAR account ids contain dots, which the contract's uid format
(`[A-Za-z0-9]{20,128}`) and document paths don't allow. Each wallet's uid is `u` + 39 hex of
`sha256(accountId)`, stored in `accounts.uid`; sessions carry both.

**Redis**

| Use | Detail |
|---|---|
| Live updates fan-out | On commit, publish `doc:{path}`; every `server` instance subscribes and pushes to its sockets. This works with any number of instances (Postgres `LISTEN/NOTIFY` is limited to one connection per listener and 8 KB payloads) |
| Cache | Catalogue, config, leaderboard pages, NFT ownership per account (for workers, §8), marketplace listing pages; invalidated by event, short TTLs as backstop. **Never** used for balances or anything a transaction decides |
| Auth | Nonces, rate limits (sliding window), JWT deny-list for revoked sessions |
| BullMQ | Its queues |

**BullMQ queues** (replace Cloud Scheduler, Cloud Tasks and Firestore triggers)

| Queue | Kind | Job |
|---|---|---|
| `island-wake` | delayed, `jobId = uid` (one per island, replaced on reschedule) | Advance an island when something is due (was the Cloud Tasks wake) |
| `voucher-expiry` | delayed, at `expires_at` + margin | §7.1 step 4: confirm the mint on RPC, otherwise refund |
| `redeem-backstop` | repeatable, every minute | Credit burns in `chain.redemptions` that nobody claimed |
| `chain-reconcile` | repeatable, nightly | issued − refunded = minted on chain; burned = redeemed; alert on drift |
| `leaderboard`, `daily-reset`, `inbox-expiry` | repeatable | The existing scheduled jobs |

Every job is idempotent (it can run twice), has retries with backoff and a dead-letter queue, and
shows in a Bull Board admin page behind auth.

**Ops:** migrations with `prisma migrate deploy` (a release step, never at server start); PgBouncer (or the host's pooler) in transaction mode;
daily backups + point-in-time recovery; Redis with AOF persistence so BullMQ survives restarts;
structured logs (pino) + OpenTelemetry; `docker compose` for local dev (Postgres, Redis, island
engine, server, worker).

## 6. Smart contracts (Rust, near-sdk 5.x, built with cargo-near, reproducible builds)

Common to all three: owner = a **multisig** (Sputnik DAO / Trezu) on mainnet, `pause` for
user-facing writes, NEP-297 events on every state change, NEP-145 storage accounting (callers pay
for the storage they add, excess refunded), no `unwrap` on user input, all amounts `U128`,
explicit `#[private]` callbacks with failure rollback, versioned state with a `migrate` entry.

### 6.1 `farm_token` — FARM (NEP-141 + NEP-145 + NEP-148)

- Fixed supply minted once at `new` to the treasury; **no mint function** afterwards (§11 Q1).
- Standard `ft_transfer`, `ft_transfer_call`, `storage_deposit`. Nothing custom — the simplest
  token is the safest one, and every wallet/DEX understands it.

### 6.2 `items` — Farmverse items & workers (NEP-171, 177, 178, 181, 199)

**Token shape** — one item type per token, never mixed:

```jsonc
// token_id: "g-<26-char ulid>" for game goods, "w-<n>" for workers, "s-<n>" for studio items
{
  "kind": "good",               // good | worker | studio
  "item_id": "wheat",           // a catalogue id from contracts/economy
  "quantity": 250,              // fixed at mint
  "props": { "quality": 1 },    // item-specific, schema per item_id, small
  "minted_from": "alice.near",  // the island it came from (provenance)
  "minted_at": 1730000000000
}
```

Stored in contract state (so the game and marketplace can trust it) and mirrored into NEP-177
`extra` JSON + `media` = the item's icon on the CDN, so wallets render it.

**Methods**

| Method | Who | What |
|---|---|---|
| `mint_with_voucher(voucher, signature)` *payable* | the player | Verifies `env::ed25519_verify` against a registered **game signer key**, checks `voucher.receiver_id == predecessor`, `voucher.expires_at > now`, `token_id` unused; mints; caller pays storage; emits `nft_mint` |
| `redeem(token_id)` *1 yocto* | current owner | Burns the token (clears approvals), emits `farmverse_redeem {token_id, owner_id, item_id, quantity}` |
| `studio_mint(...)` | studio minter role | Primary-sale items and workers (§8), minted to the studio/market |
| `set_worker_state(token_id, xp, level, …)` | game signer, via voucher | Dynamic worker traits (later) |
| `add_signer / remove_signer`, `pause`, `set_royalty`, `migrate` | owner (multisig) | Admin |
| NEP-171/178/181/199 views and transfers | anyone | Standard |

**Voucher** (Borsh-serialised, signed by the backend's signer key):
`{ token_id, receiver_id, item_id, quantity, props, expires_at, contract_id, chain: "mainnet" }`.
`contract_id` and `chain` stop replay across contracts/networks; `token_id` stops replay itself.

Why a voucher instead of the backend minting: the player pays the storage deposit and gas, the
backend holds **no NEAR-spending hot key**, and the backend's key can only authorise mints that
the game already deducted — it can never touch an existing token.

**Royalties:** NEP-199 payout with a studio royalty on resales (e.g. 2.5 %, §11 Q2).

### 6.3 `market` — the marketplace

- **List:** owner calls `items.nft_approve(token_id, market, msg: {"price": "<FARM yocto>"})`.
  `nft_on_approve` (only from whitelisted NFT contracts) records `listing{token_id, owner,
  approval_id, price, listed_at}`. Seller pre-pays listing storage (NEP-145).
- **Buy:** buyer calls `farm.ft_transfer_call(market, price, msg: {"token_id": …})`. In
  `ft_on_transfer` (only from the FARM contract) the market checks amount == price, then calls
  `items.nft_transfer_payout(buyer, token_id, approval_id, balance=price, max_len_payout)`.
  The callback splits: **fee** (bps) → treasury, royalties → payees, rest → seller, and removes the
  listing. If the NFT transfer failed (stale approval, token redeemed/sold elsewhere), the full
  amount is returned to the buyer by `ft_on_transfer`'s unused-amount return.
- **Delist / update price:** owner only; stale listings (approval revoked, token burned) are
  dropped on buy and by a permissionless `remove_stale(token_id)`.
- **Config:** fee bps (hard cap in code, e.g. ≤ 10 %), treasury, accepted FT (FARM; NEAR later
  if wanted), whitelisted NFT contracts, pause. Offers/bids are a later version.

### 6.4 Testing and release

- Unit tests per contract; **near-workspaces** sandbox tests for every cross-contract path,
  including every failure branch (wrong amount, stale approval, token burned mid-sale, paused,
  storage not paid, voucher expired/replayed/wrong contract/wrong signer).
- Deploy to **testnet** first, run the whole game ↔ marketplace loop there, then mainnet.
- Mainnet deploy is signed by the team, never by CI, never by the agent (hackathon rule too).
- An external audit is strongly recommended before value is at stake; at minimum a second
  reviewer on every contract PR.

## 7. Game ↔ chain flows (backend)

New calls in `contracts/` (zod → C#), implemented in `backend/server/src/game/chain.ts`. Each runs
in one Postgres transaction through the `Store` port:

### 7.1 Mint (island → NFT)

1. `chain-mintRequest {item_id, quantity}` — in **one Postgres transaction**: advance the island,
   check the goods are in storage and the item is mintable (catalogue flag), check limits (min
   quantity, min island level, daily cap), **remove the goods**, write `mints/{token_id}`
   `{uid, item_id, quantity, status: "issued", expires_at}`. Return the signed voucher.
2. Client asks the wallet to sign `items.mint_with_voucher(voucher, sig)`.
3. Client reports the tx hash: `chain-mintConfirm {token_id, tx}` → status `minted` (checked on RPC).
4. A BullMQ `voucher-expiry` job, scheduled when the voucher is issued: for `issued` vouchers past `expires_at`, look up `nft_token(token_id)` on RPC.
   Minted → mark `minted`. Not minted (and now can never be) → **refund the goods** to the island,
   mark `refunded`. Expiry makes the refund decision safe: after it, the voucher is worthless.

### 7.2 Redeem (NFT → island)

1. Client asks the wallet to sign `items.redeem(token_id)`.
2. `chain-redeem {tx_hash}` → server fetches the tx outcome from RPC (`EXPERIMENTAL_tx_status`),
   checks: succeeded, receiver is the items contract, signer is the caller's `uid`, and finds the
   `farmverse_redeem` event. In a transaction keyed `redemptions/{token_id}`: if absent, credit
   the goods to the island (respecting storage caps — overflow goes to the inbox) and write it.
3. Backstop: the indexer's redemption table is swept for burns no one claimed (player closed the
   tab) and credits them the same way, so nothing is lost.

### 7.3 Abuse & economy

Minting turns play-time into money, so bots become profitable. Controls: wallet-only accounts,
reCAPTCHA App Check, min island level to mint, per-day mint caps per item, mint fee (burns
in-game coins, or small FARM), catalogue `mintable` flag per item, ledger rows for every
crossing, a nightly reconciliation (issued − refunded = on-chain minted; burned = redeemed).
Bot protection is Turnstile plus Redis rate limits (Firebase App Check is gone).

## 8. Workers as NFTs ("NPC agents with wallets")

**Recommendation:** yes — but as the second chain feature, after goods mint/redeem works.

- Workers are **studio-minted** (`kind: worker`), sold in primary sales on the marketplace.
  This is the main new revenue line besides fees.
- Each worker has traits (specialty: fields / animals / machines; speed; carry; stamina) and a
  level/xp that grows with work — **dynamic NFT**, updated by game-signed vouchers.
- **Hold-to-use, not redeem:** a worker is never burned. On every island sync the backend checks
  `nft_tokens_for_owner(uid)` (cached) and the island gets those workers; sell the NFT and the
  worker walks off the island. Their stats in the engine come from their traits.
- **The wallet:** each worker owns a balance inside the items contract (a token-bound account; a
  real sub-account like `w-17.workers.farmverse.near` is possible but costs more for little gain).
  Workers can be **rented**: a renter pays FARM per day into the worker's wallet, the worker works
  on the renter's island, the owner withdraws. That is the "NPC with a wallet" loop the `games`
  and `agents` tracks ask for — and it pays without the game minting FARM out of thin air.
- The rule-based worker AI stays (food → spoilables → rest). An LLM personality (NEAR AI) is a
  cosmetic extra, not the work logic.

Players stay human; workers are the agents.

## 9. Indexer (Goldsky) and marketplace data

- **Source:** `near.execution_outcomes` (mainnet, `start_at` the contract deploy block).
- **Transform:** `WHERE executor_id IN (items, market, farm)`, unnest `logs`, keep lines starting
  `EVENT_JSON:`, parse NEP-297 (`nft_mint`, `nft_transfer`, `nft_burn`, `farmverse_redeem`,
  `market_list`, `market_delist`, `market_sale`, `ft_transfer`).
- **Sink:** the **same Postgres** as the game, schema `chain` (its own role with write access to
  `chain` only; the game role reads it). Tables: `events` (append-only), then views/materialised
  tables `tokens(token_id, owner, kind, item_id, quantity, props, burned)`,
  `listings(token_id, seller, price, listed_at, active)`, `sales`, `redemptions`, `mints`.
- **Read API:** routes on the same Fastify `server`, cached in Redis, for the marketplace: `GET /listings?item=wheat&kind=good&sort=price_asc|price_desc|
  newest|qty&min=&max=&cursor=`, `GET /owners/{account}/tokens`, `GET /tokens/{id}`, `GET /stats`.
  The UI re-checks a listing on-chain (`get_listing`) right before buy.

## 10. Websites

**`web/marketplace`** (Vite + React + TypeScript, wallet selector):
browse grid of item cards using the game's own icons/9-slice art and USS colours, filters (kind,
item type, category, price range, quantity), sort (price ↑/↓, newest, price per unit), item page
(metadata, provenance, price history), **My items** (owned NFTs: list / delist / transfer /
"Redeem in game" deep-link), **Activity**, FARM balance + storage deposit helper, primary-sale
page for workers and packs.

**`web/landing`**: home (what Farmverse is, play button, marketplace button), How to play,
Workers, FAQ (wallets, FARM, mint/redeem, fees), Terms of Service, Privacy Policy, Token
disclosure. ToS/Privacy/token text needs a legal review before mainnet launch — a token with
market value plus player-to-player sales has consumer-protection and securities angles.

## 11. Decisions

**Decided (2026-11-04):**

- **Q1 FARM supply:** fixed supply, minted once at `new`, no mint function. The total and the split are still open.
- **Q3 Gems:** sold as studio-minted **gem-pack NFTs** bought with FARM and redeemed into the game.
  The items contract gets `kind: "pack"`, and redeeming one credits gems through the same §7.2 path.
- **Q4 Mintable v1:** **everything**, buildings, land plots and tractors included. A placed object
  has to be **picked up** (taken off the grid, together with its level and upgrades) before it is
  minted. Redeeming gives an *unplaced* object that the player puts down with the existing build
  flow. Open design points: what happens to the crops/animals/machine queues inside a building
  when it is picked up (proposed: storage first, otherwise the pick-up is refused); whether
  starter/quest-locked buildings are mintable (proposed: catalogue `mintable: false`); and land
  plots that other buildings depend on (proposed: the plot must be empty).
- **Backend:** Firebase removed entirely. Node/TS (Fastify) + Postgres via **Prisma** + Redis (cache, sessions,
  pub/sub) + BullMQ (jobs), §5.3.
- **Q7 Network:** testnet first. Mainnet only if the testnet loop is clean by about Nov 12, and
  you sign that deploy yourself.

**Still open:**

| # | Question | Recommendation |
|---|---|---|
| Q1 | FARM supply and distribution: total supply? who gets it (treasury, team, rewards, liquidity)? Is it bought on a DEX (Rhea) or only sold by you? | Fixed supply, no in-contract minting; liquidity on Rhea |
| Q2 | Marketplace fee and studio royalty | 5 % fee + 2.5 % royalty |
| Q3 | Gems (previously bought with money): earn-only, or sold as NFT packs for FARM? | Earn-only gems; sell **packs/land/workers** as NFTs |
| Q4 | What is mintable in v1: goods only, or also placed buildings/land/tractors? | Goods + boosts in v1; buildings later (they sit on the island grid) |
| Q5 | Mainnet account names you own (`farmverse.near`?) | sub-accounts `farm.` `items.` `market.` of it |
| Q6 | Prices only in FARM, or FARM and NEAR? | FARM only in v1 |
| Q7 | Hackathon scope by 2026-11-14 (10 days) | §12 |

## 12. Order of work and the hackathon cut

There are 10 days to the deadline. The full plan is more than that, so the submission ships a
complete **loop**, not every feature:

| # | Step | In hackathon cut |
|---|---|---|
| 0 | Baseline commit + tag `pre-hackathon` (repo has **no commits** yet; hackathon Q-003/Q-004) | ✅ today |
| 1 | Remove ads/IAP/mobile sign-in (§4), tests green | ✅ |
| 2 | Contracts: FARM, items (mint/redeem), market (list/buy/delist) + sandbox tests, testnet | ✅ |
| 3a | Backend move: Fastify server, Prisma schema + Postgres store, Redis live channel, BullMQ jobs; game tests green on Postgres | ✅ (largest risk) |
| 3b | Wallet auth + mint/redeem calls + jobs | ✅ |
| 4 | Unity `Infrastructure.Web` adapters + WebGL build | ✅ |
| 5 | Goldsky pipeline + read API | ✅ |
| 6 | Marketplace site (browse/filter/sort/my items/list/buy) | ✅ |
| 7 | Landing page + legal pages | ✅ minimal |
| 8 | Mainnet deploy (you sign) | ✅ if testnet loop is clean by ~Nov 12; else submit on testnet (Q-010 allows it) |
| 9 | Worker NFTs, rental, dynamic traits | demo-level at most |
| 10 | Offers/bids, NEAR as a second currency, buildings mintable | after |

Steps 2 (contracts), 3a (backend) and 6 (marketplace UI, against a mocked read API) are
independent and can run in parallel. Step 4 needs 3a's HTTP/WebSocket API to be fixed first:
write it into `contracts/` up front so both sides build against it.
