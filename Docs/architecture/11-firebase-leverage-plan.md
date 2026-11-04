# 11 — Using every Firebase advantage

For each Firebase product this document says exactly how Farmverse uses it: the
settings, the code that owns it, the phase it lands in (see 10 §2), and the
check that proves it works. It also says what we deliberately do **not** use
and why.

**Rule of thumb:** if Firebase does something well, we use it and write no code
for it. We only build our own layer where Firebase has a real gap, and each of
those gaps is named here.

---

## 1. Firestore — the database

### 1.1 Offline cache and instant start

| Setting | Value | Where |
|---|---|---|
| `Settings.PersistenceEnabled` | `true` (set before the first Firestore call) | `Infrastructure.Firebase/FirebaseInitializer` |
| `Settings.CacheSizeBytes` | 100 MB | same |
| Cold-start read order | listeners attach immediately; the first snapshot comes from the cache, then the server snapshot follows | repositories |

**What it gives us:** the farm appears in under a second on launch, even with no
network, and the "Connection lost" screen is only shown for actions that
need the server. We write **no cache code** for any Firestore data.

**Check (Phase 4):** airplane mode → kill app → relaunch → the farm, inventory
and last marketplace page render from cache, marked "offline".

### 1.2 Realtime listeners instead of polling

| Listener | Scope | Lifetime |
|---|---|---|
| `players/{uid}` | 1 doc | whole session |
| `players/{uid}/state/inventory`, `estate`, `production` | 3 docs | whole session |
| `players/{uid}/plots` | ≤ 50 docs | whole session |
| `players/{uid}/private/settings` | 1 doc | whole session |
| `players/{uid}/notifications` where `readAt == null`, limit 20 | ≤ 20 docs | whole session (feeds the bell badge) |
| marketplace first page | the current query, limit 20 | while the browse screen is open |
| `listings/{id}` | 1 doc | while listing detail / buy confirm is open |
| `marketStats/{itemId}` | 1 doc | while item detail is open |

Listeners use `MetadataChanges.Include` only where the UI shows pending local
writes (settings, §1.4); everywhere else `Exclude`, so we receive no extra
snapshots. Session-wide listeners detach after 60 s in the background and
re-attach on resume (02 §5). This table is the complete list; adding a listener
requires updating it (it is our read budget, 04 §10).

### 1.3 Freshness and refresh

- `SnapshotMetadata.IsFromCache` → `Page.FromCache` / store `Freshness` → the
  "offline" badge and `ListState.IsFromCache`.
- Pull-to-refresh → `GetSnapshotAsync(Source.Server)`; normal loads →
  `Source.Default`. This is `Freshness.Server` / `Freshness.Default` in
  `Farmverse.Core.Lists` — already built in Phase 1.

### 1.4 Direct client writes, only where rules can fully check them

**The gap:** Firestore's instant local update and its offline write queue only
work for writes the client makes **directly**. Writes through Cloud Functions
get neither, which is why we build the action queue with prediction and
rollback (02 §4.4).

**Our use:** fields with **no economic value** are written directly, so they
get instant feedback and offline queuing for free:

| Document | Fields the client may write | Rule |
|---|---|---|
| `players/{uid}/private/settings` | `allowVisitors`, `push.enabled`, `push.quietStart`, `push.quietEnd`, `locale`, `timezone`, `reduceMotion`, `textScale` | own doc only; `affectedKeys().hasOnly([...])`; types and ranges checked |
| `players/{uid}/notifications/{id}` | `readAt` (only from `null` to `request.time`) | own doc only; no other field may change |

```
match /players/{uid}/private/settings {
  allow get: if isSelf(uid);
  allow update: if isSelf(uid)
    && request.resource.data.diff(resource.data).affectedKeys()
         .hasOnly(['allowVisitors', 'push', 'locale', 'timezone', 'reduceMotion', 'textScale'])
    && request.resource.data.allowVisitors is bool
    && request.resource.data.push.quietStart is int && request.resource.data.push.quietStart >= 0 && request.resource.data.push.quietStart <= 23
    && request.resource.data.push.quietEnd   is int && request.resource.data.push.quietEnd   >= 0 && request.resource.data.push.quietEnd   <= 23
    && request.resource.data.locale is string && request.resource.data.locale.size() <= 16;
}
match /players/{uid}/notifications/{id} {
  allow update: if isSelf(uid)
    && resource.data.readAt == null
    && request.resource.data.diff(resource.data).affectedKeys().hasOnly(['readAt'])
    && request.resource.data.readAt == request.time;
}
```

Everything that touches coins, goods, plots, listings, ranks or another player
stays on Cloud Functions. `settings` is created by `player-create`, so the client
can only update it, never create it. Display name changes stay on a function,
because names must be unique and filtered.

**Check (Phase 3):** rules tests prove every allowed write succeeds, and any
other field, other player's doc, wrong type or out-of-range value is denied.

### 1.5 Transactions

`db.runTransaction` with `maxAttempts: 5` around every batch
(`infra/firestoreStore.ts`, built in Phase 1). Firestore re-runs the work on
contention, which is safe because the pipeline is read → decide → write
(`actions/pipeline.ts`). Two buyers on one listing: one wins, the other gets
`QUANTITY_UNAVAILABLE` or `LISTING_CLOSED`.

**Check (Phase 3):** emulator test with 20 concurrent buyers on a 5-unit listing —
exactly 5 units are sold and the coin totals balance.

### 1.6 Queries, indexes and aggregation

- Every marketplace query shape maps to one composite index (06 §4.2); a CI
  script asserts each shape the query builder can produce has an index in
  `backend/firestore.indexes.json`.
- Leaderboard rank uses `count()` aggregation (`netWorth > mine`) — 1 read per
  1,000 entries counted, cached 2 minutes.
- "My active listings" badge uses `count()` instead of downloading listings.
- Single-field index exemptions on maps we never query (04 §8) cut write cost.

### 1.7 Security rules as the authority

Default deny; reads scoped to the owner or to public projections; every list
query capped with `request.query.limit <= 50`; only the two direct writes in §1.4.
Tested with `@firebase/rules-unit-testing` against the emulator in CI (08 §2).

### 1.8 TTL, backups, region

| Feature | Setting |
|---|---|
| TTL | `requests.expireAt` (72 h), `notifications.expireAt` (30 d) — `tools/ci/ttl.sh` runs `gcloud firestore fields ttls update` per project |
| Point-in-time recovery | on, 7 days, prod only |
| Managed export | daily to a locked bucket, 30-day retention; restore drill quarterly |
| Location | one region for prod — decision D1 (10 §5); Functions use the same region |

---

## 2. Cloud Functions — the trusted half

### 2.1 Callables (client → server)

| Function | Options | Phase |
|---|---|---|
| `actions-submit` | `enforceAppCheck`, 512 MiB, concurrency 40, **minInstances 2 in prod** | 1 (pipeline) → 3 (verbs) |
| `player-create`, `player-updateProfile`, `player-setRole` | `enforceAppCheck`, 256 MiB | 3 |
| `session-catchUp` | `enforceAppCheck`, minInstances 1 | 3 |
| `push-registerToken` | `enforceAppCheck` | 7 |
| `visits-getSnapshot` | `enforceAppCheck` | 7 |
| `account-delete`, `account-export` | `enforceAppCheck`, `consumeAppCheckToken` | 7 |
| `admin-*` | `enforceAppCheck` + `admin` custom claim | 7 |

The callable automatically verifies the caller's ID token and App Check
token, so our pipeline (`core/callable.ts`) only reads `request.auth.uid` and
claims — no token code.

**App Check replay protection** (`consumeAppCheckToken`) needs the client to
send limited-use tokens. We enable it on `account-*` first. Whether to enable it
on `actions-submit` depends on Unity SDK support for limited-use tokens, which
we verify in Phase 4; until then, idempotency keys and rate limits cover replays.

### 2.2 Scheduled jobs (Cloud Scheduler via `onSchedule`)

| Job | Schedule | Why a job |
|---|---|---|
| `market-expireListings` | every 5 min | returns goods from expired listings automatically |
| `market-aggregateStats` | every 5 min | price history without a hot document per item |
| `leaderboard-reprice` | daily 03:00 + after a catalogue publish | prices change everyone's net worth |
| `ledger-reconcile` | daily 04:00 | proves balances match ledgers |

NPC orders need **no** job: the day's board is computed from `(date, uid,
catalogue)` on both sides (07 §8).

### 2.3 Triggers

| Trigger | Fires on | Does |
|---|---|---|
| `leaderboard-onPlayerChanged` | `players/{uid}` written with `netWorthDirty == true` | recompute net worth from 3 docs, write leaderboard + public profile |
| `push-onNotificationCreated` | `players/{uid}/notifications/{id}` created | apply push policy, send FCM |
| `auth-onUserDeleted` | Auth user deleted (v1 auth trigger) | recursive delete of the player's data |

### 2.4 Parameters and secrets

| Name | Kind | Used for |
|---|---|---|
| `FARMVERSE_ENV` | `defineString` | dev / staging / prod behaviour (already in Phase 1) |
| `ACTIONS_MIN_INSTANCES` | `defineInt` | warm instances per environment |
| `CURSOR_SECRET` | `defineSecret` | signing callable-list cursors (`core/cursor.ts`) |
| `AD_SSV_PUBLIC_KEYS_URL` | `defineString` | rewarded-ad verification |

The minimum client version moves from a function parameter (Phase 1) to the
**server-side Remote Config template** (§5), so live-ops changes it without a
deploy.

---

## 3. Authentication

| Use | How | Phase |
|---|---|---|
| Play immediately | `SignInAnonymouslyAsync` on first launch | 4 |
| Keep progress across devices | link Google (Google Sign-In plugin → `GoogleAuthProvider.GetCredential`) and Apple (Sign in with Apple plugin, hashed nonce → `OAuthProvider.GetCredential("apple.com", …)`) with `LinkWithCredentialAsync` | 4 |
| Optional platform sign-in | Play Games (Android), Game Center (iOS) — post-launch | 8 |
| Session | the SDK keeps and refreshes the token; `StateChanged` drives `IAuthService.Changes` | 4 |
| Anti-alt-account | listing on the marketplace requires a linked (non-anonymous) account — decision D2 | 3 |
| Admin roles | `admin: true` custom claim set by a CLI script (`tools/admin/grant.ts`) from an MFA-protected Google account; read by functions and rules | 7 |
| Account deletion | `account-delete` re-checks a recent sign-in, deletes the Auth user; `auth-onUserDeleted` cleans data | 7 |

**Check (Phase 4):** install → play anonymously → link Google → reinstall → sign
in with Google → same farm.

---

## 4. App Check

| Platform | Provider |
|---|---|
| Android | Play Integrity |
| iOS | App Attest, falling back to DeviceCheck on older devices |
| Editor / dev builds | debug provider; debug tokens registered per developer and QA device |

Rollout (08 §3): providers ship in Phase 4 builds, console in **monitor** mode;
after 7 days at ≥ 98% verified traffic, enforce on Functions, then Firestore,
then Storage. A failed attestation shows "please update the game from the store",
never a frozen screen.

---

## 5. Remote Config

Economy numbers (prices, yields, fees) stay in the versioned catalogue
document, because the client must predict with exactly the numbers the server
used. Remote Config holds **switches and operational knobs**:

| Key | Type | Purpose |
|---|---|---|
| `min_client_version` | string | forced update (screen 57); read by the client **and** by functions through the server template |
| `maintenance` | bool | maintenance screen; pushed instantly through the realtime listener |
| `catalogue_version` | int | tells the client whether `catalogue/current` changed (§5.1) |
| `feature_b3tr` | bool | token layer, off at launch |
| `feature_visits`, `feature_trading_post`, `feature_orders` | bool | kill switches per feature |
| `rate_actions_per_min`, `rate_market_per_min` | int | tighten abuse limits without a deploy (server template) |
| `push_daily_cap` | int | push policy |
| `ads_daily_cap` | int | rewarded ads |
| `onboarding_variant` | string | A/B test of the tutorial (§9) |

- Client: defaults generated from the contract (`RemoteKeys`), `FetchAndActivateAsync`
  on boot with a 12 h minimum interval (5 min in dev), plus the **realtime
  update listener** so `maintenance` and `min_client_version` arrive within seconds.
- Server: `getRemoteConfig().getServerTemplate()` cached per instance for 60 s.
- Targeting: conditions by platform, app version and country; percentage
  rollouts for new features; one-click rollback from the version history.
- Changes go through a PR to `backend/remoteconfig.template.json`, deployed to
  staging first.

### 5.1 Catalogue: no custom cache

The catalogue is one Firestore document, so it is already cached on the
device. Boot reads `catalogue/current` with `Source.Cache`; if its `version`
equals Remote Config's `catalogue_version` we use it, otherwise we read it once
with `Source.Server`. A catalogue snapshot ships in the build for the very
first offline launch. This replaces the separate `ILocalStore` catalogue cache
in the earlier plan.

---

## 6. Cloud Messaging

| Use | How |
|---|---|
| Ask permission at the right time | after the first harvest, not at launch |
| Token management | `TokenReceived` → `push-registerToken` callable → `players/{uid}/pushTokens/{installationId}`; dead tokens deleted when FCM reports them |
| Personal pushes | crops ready, batch ready, listing sold, animals need feed, daily bonus — one coalesced push per concern via collapse keys, max 3/day, quiet hours (existing policy, rebuilt in Phase 7) |
| Announcements | topics `announcements` and `announcements_<locale>`, subscribed on login; maintenance notices |
| Foreground | a push received while playing becomes a toast + bell dot, never a system notification |
| Deep links | `data.route` + `data.args` → the router opens the screen |
| Platform setup | Android channel `farm_progress` (normal priority); iOS APNs auth key uploaded to the console |

---

## 7. Crashlytics

| Use | How |
|---|---|
| Readable crashes | IL2CPP symbol upload in the build pipeline (Crashlytics CLI step in CI) |
| Context | `SetUserId(uid)` (never email); custom keys `route`, `env`, `clientVersion`, `catalogueVersion`, `lastBatchId` |
| Drift detection | non-fatal `prediction_mismatch{kind, field}` when the server result differs from the client's prediction (02 §4.4) |
| Contract breaks | non-fatal for every `WireFormatException` |
| Release health | crash-free users per version; alert below 99.5% |
| Consent | collection follows the consent screen |

---

## 8. Analytics and BigQuery

**Gap to fix first:** the Firebase Analytics Unity package is not imported yet
(decision D6). Added in Phase 4, same SDK version (14.0.0).

| Use | How |
|---|---|
| Typed events | event and parameter names generated from `contracts/src/analytics.ts` (like the actions), so names cannot drift |
| Core funnel | `first_open → role_chosen → tutorial_complete → first_harvest → first_sale → first_purchase` |
| Economy events | `coins_earned{source}`, `coins_spent{sink}`, `listing_created`, `trade_completed`, `order_fulfilled` |
| User properties | `role`, `title_tier`, `plots_owned_bucket`, `linked_account` |
| Audiences | e.g. "farmers stuck before first sale" → Remote Config tips, A/B tests |
| BigQuery | daily + streaming export; scheduled SQL views for the economy dashboard (mint/burn, price vs reference, sell-through) — 07 §11 |
| Consent | `SetAnalyticsCollectionEnabled` follows the consent screen |

The authoritative economy numbers come from Firestore (ledger, trades) via the
**Stream Firestore to BigQuery** extension on `trades` and `ledger`; Analytics
events are for behaviour.

---

## 9. A/B Testing

Experiments on Remote Config values, measured against Analytics goals:

| Experiment | Variable | Goal metric |
|---|---|---|
| Tutorial length | `onboarding_variant` (short / guided) | `tutorial_complete`, D1 retention |
| Starter pack | starter seeds/coins (via a catalogue variant id) | `first_sale` within 24 h |
| Push timing | permission prompt after harvest 1 vs 3 | opt-in rate, D7 retention |

Economy experiments are never run on prices players trade with — a split
market would be unfair.

---

## 10. Cloud Storage

At launch avatars come from the catalogue (no user uploads), so there is no
moderation burden. Storage is used for:

| Use | Path | Rule |
|---|---|---|
| Account data export | `exports/{uid}/{file}.json` | written by functions only; client gets a 24 h signed URL |
| (Later) custom avatars | `avatars/{uid}/{file}` | own folder, `image/*`, ≤ 1 MB; Resize Images extension makes 128/256 px versions |

The Storage Unity package is imported when custom avatars are built (decision D6).

---

## 11. Emulator Suite — develop and test without a project

| Piece | Setting |
|---|---|
| `firebase.json` emulators | auth 9099, functions 5001, firestore 8080, storage 9199, UI on, `singleProjectMode` |
| Seed data | `backend/functions/test/fixtures/seed.ts` loads a catalogue and three test players |
| Unity dev environment | `EnvironmentConfig_Dev`: Firestore `Settings.Host = "localhost:8080"` with SSL off, `FirebaseFunctions.UseFunctionsEmulator("http://localhost:5001")`; Auth emulator where the Unity SDK supports it (verify in Phase 4) |
| CI | `firebase emulators:exec` runs the backend integration suite and the rules tests on every PR |

---

## 12. The wider Google Cloud

| Service | Use |
|---|---|
| Cloud Logging + Error Reporting | structured logs from `core/logger.ts` (Phase 1); new defects alert the team |
| Cloud Monitoring | p95 latency of `actions-submit`, error rate, Firestore reads per day, alert policies (09 §4) |
| Billing budgets | a budget alert at 50 / 90 / 100% of the monthly target per project |
| Secret Manager | `CURSOR_SECRET`, ad verification keys |
| BigQuery | §8 |

---

## 13. Other Firebase products

| Product | Decision | Why |
|---|---|---|
| App Distribution | **use** | every `main` build goes to internal testers (09 §2) |
| Test Lab | **use** | Robo + game-loop runs on a real device matrix before each release |
| Realtime Database | **later** | cheap presence ("online sellers" filter, 06 §1) using `onDisconnect`, if that filter ships |
| Extensions | **use**: Stream Firestore to BigQuery; Resize Images (when custom avatars arrive) | proven, maintained by Firebase |
| Performance Monitoring | **skip** | limited Unity support; Unity Profiler captures + Cloud Monitoring cover it |
| In-App Messaging | **skip** | conflicts with the no-nagging policy (07, push rules) |
| Data Connect (SQL) | **skip** | BigQuery covers reporting; the game needs no joins |
| Genkit / Vertex AI | **later** | possible future features (e.g. farm tips); not needed for launch |
| Dynamic Links | **skip** | discontinued by Firebase; deep links come from push data and store links |

---

## 14. The gaps we fill ourselves, and only these

| Firebase gap | Our answer | Where |
|---|---|---|
| Rules cannot run game logic | all economy writes through `actions-submit` | backend (Phase 1 pipeline) |
| Function calls have no local prediction or offline queue | `ActionQueue` with prediction, rollback, retry, persisted pending batches | `Farmverse.Application` (Phase 4) |
| No full-text search | catalogue search on the device → `itemId` filters | `Farmverse.Core.Search` (Phase 1) |
| No database constraints (e.g. balance ≥ 0) | invariants in the domain + infra double check + nightly reconcile | backend domain (Phase 2–3) |
| Hot documents (> 1 write/s) | price stats aggregated by a job, not in trades | `market-aggregateStats` (Phase 3) |
| One codebase per side of the rules | contracts generated into C# | `contracts/` (Phase 1) |

## 15. Rollout by phase

| Phase | Firebase work |
|---|---|
| 1 ✅ | callable pipeline on Functions v2, transactions, structured logs, App Check enforced in code |
| 2 | — (pure domain) |
| 3 | Firestore model, rules (+ the two direct writes), indexes, TTL, triggers, scheduled jobs, emulator suite in CI, server Remote Config template |
| 4 | Unity: initialiser, persistence settings, listeners, Auth (anonymous + linking), App Check providers (monitor mode), Remote Config + realtime updates, Crashlytics with symbols, Analytics package + typed events, emulator wiring |
| 5–6 | freshness badges, pull-to-refresh, listener scopes per screen |
| 7 | FCM + push policy + topics, BigQuery export + economy views, admin claims, account deletion/export (Storage), A/B tests |
| 8 | App Check enforcement, PITR + exports, budgets and alerts, Test Lab matrix, minInstances tuning |
