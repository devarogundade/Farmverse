# 10 — Roadmap & migration

## 1. Strategy: build the new system beside the old one, then cut over

The old code keeps compiling while the new one is built in new folders with new
assembly definitions (`Assets/Farmverse/Runtime/**`, `backend/`,
`contracts/`). Nothing in the new code references the old. Each phase ends
with a gate; the old code is deleted only when its replacement has passed its
gate. This keeps the project runnable every day.

## 2. Phases

| # | Phase | Deliverables | Gate |
|---|---|---|---|
| **1** | **Foundation** | These documents · `contracts/` (zod, envelope, errors, paging, codegen to C#) · `backend/functions` skeleton (callable pipeline, errors, logging, clock, cursors, idempotency, unit of work, action registry) · Unity `Farmverse.Core` + `Farmverse.Contracts` assemblies (Result/Error, clock, reactive state, paging engine, text search) + EditMode tests | `npm test` and `dotnet test` green; Unity compiles with the new asmdefs beside the old code |
| 2 | Domain & catalogue | `economy.json` moved + schema + validator · `Farmverse.Domain` (catalogue, inventory, farming, production, market, economy) · TS mirror in `backend/functions/src/domain` · conformance vectors for every formula in 07 | both suites pass the same vectors; catalogue validator passes |
| 3 | Backend game server | Firestore model + converters + repositories · all actions in 05 · jobs + triggers · rules + rules tests · indexes generated from 06 · emulator integration suite · `firebase.json` → `backend/` | full emulator suite green; old `functions/` deleted |
| 4 | Client application & infrastructure | ports · stores with optimistic overlay · ActionQueue · ServerClock · SessionService · Firebase adapters (Auth, App Check, Firestore, Functions, RC, Crashlytics, FCM) · list sources · `GameRoot` composition | a headless PlayMode "bot" boots against the emulator, plays plant → harvest → sell → buy, and survives kill/retry |
| 5 | UI rebuild (launch screens, §3) | shell, navigation, component library, `PagedListView`, MVP presenters + UXML for every launch screen, localization tables, accessibility pass | every launch screen renders in the capture tool in all states; presenter tests green |
| 6 | 3D world rebuild | `World_Farm` scene, `WorldPresenter` diff renderer, `WorldVisualCatalog` (Addressables), input + selection, camera rig, time of day, VFX pools, visit mode | 60 fps on the mid device; **old `Assets/Farmverse/Scripts` deleted** |
| 7 | Social & live-ops | leaderboards, notifications + push policy, daily bonus, orders board, Trading Post, visits, typed analytics, admin CLI + catalogue publish | staging soak test 7 days with internal testers |
| 8 | Hardening & launch | load tests, App Check enforcement, store compliance (deletion, privacy labels), cosmetic IAP + receipt validation, performance pass, soft launch in 2–3 markets | KPIs in 00 §5 met in soft launch |

Phases 2 and 4 can overlap once 1 is merged; 5 and 6 can run in parallel
(different engineers) once 4's stores exist.

## 3. Launch screen set (v1.0)

The original specification lists 81 screens. Launch ships **38**; the rest are
live-ops content once the core loop is proven.

| Area | Screens |
|---|---|
| Boot & system (8) | Splash · Loading · Consent · Sign-in/link · Connection lost · Maintenance/Update · Generic error/confirm · Welcome back |
| Onboarding (4) | Welcome · Choose role · Profile setup · Tutorial overlay |
| Farm & world (7) | Home/world · Slot action ring · Seed picker · Crop detail · Harvest result · Animal panel · Well panel |
| Production (3) | Machine panel (queue) · Recipe picker · Job collected |
| Inventory (2) | Inventory (filter/sort/search) · Item detail (price history) |
| Shop (3) | Shop (tabs, filters, search) · Purchase confirm · Not enough coins |
| Market (7) | Browse (search/filter/sort, promoted) · Listing detail · Buy confirm · Create listing · My listings · Seller profile · Trade history |
| Economy (2) | Trading Post · Orders board |
| Social & rewards (4) | Leaderboard · Player card · Daily bonus · Notifications |
| Account (2) | Settings · Account (link, delete, export) |

Deferred: empire map & dashboard, facility hub visuals beyond upgrade sheets,
promoted-listing analytics, cellar/granary specialised views, edit-profile
cosmetics shop, events.

## 4. Migration map (old → new)

| Old | New | Phase |
|---|---|---|
| `Logic/Farm/FarmSimulation*.cs` (god object) | split into Domain rules (pure) + backend modules + Application use cases | 2–4 |
| `Logic/World/*` (accumulating `Advance`) | `Domain/Farming` derived-time rules | 2 |
| `Data/Catalogue/*` + `GameObjectCatalogue.Data.cs` | `Domain/Catalogue` loaded from `catalogue/current` + bundled snapshot | 2, 4 |
| `Api/*` + `Api/Mock/*` | Application ports + Infrastructure.Firebase; fakes only in tests | 4 |
| `Data/Reference/MockData|MockWorld|ReferenceMarketSeed` | emulator seed fixtures in `backend/functions/test/fixtures` | 3 |
| `Platform/LocalSession` (PlayerPrefs session truth) | `SessionService` + Auth; prefs only for device settings | 4 |
| `UI/Shell/UiRouter`, `UI/Composition/*`, `UI/Declarations/*` | `Presentation/UI/Navigation` + MVP screens with UXML | 5 |
| `UI/Rows/*`, `UI/Components/*` | `Presentation/UI/Components` (`PagedListView`, …) | 5 |
| `Scene/World3D/*` | `Presentation/World` | 6 |
| `functions/src/**` | `backend/functions/src/**` | 3 |
| `Tools/art_gen` generators of C# | deleted; art scripts → `tools/art` | 1 (stop using), 3 (delete) |

## 5. Decisions needed from you

| # | Decision | Recommendation |
|---|---|---|
| D1 | Functions + Firestore **region** (one-way door) | `europe-west1` if the launch audience is Africa/Europe; `us-central1` if global/US-first |
| D2 | Guest policy | Play anonymously; **linking an account is required to create a listing** (stops alt-account laundering) |
| D3 | Role change | Free, once every 14 days |
| D4 | Bundle id / Firebase project ids | `com.<studio>.farmverse`, projects `farmverse-dev`/`-staging`/`-prod` |
| D5 | CI host | GitHub Actions + GameCI (needs a Unity license secret) |
| D6 | Add Firebase **Analytics** and **Storage** Unity packages | Yes (same SDK version 14.0.0) |
| D7 | Allow deleting old files at each gate | Yes — the cut-over needs it; nothing is deleted before its replacement passes |

## 6. Risks

| Risk | Mitigation |
|---|---|
| C#/TS rule drift | conformance vectors in CI; `prediction_mismatch` metric in prod |
| Firestore contention on hot listings | per-listing docs, short transactions, `expectedUnitPrice`, retries; market stats off the hot path |
| Cold starts on first action | min instances on `actions-submit` and `session-catchUp` in prod |
| Economy inflation | burned market fee, bounded faucets, daily health dashboard, catalogue hotfix path |
| Scope (81 screens) | launch set of 38 (§3); everything else is live-ops |
| Unity 6.6 + Firebase SDK 14 compatibility | pin versions; smoke-test the SDK on device in Phase 4 before building on it |
| Single engineer bandwidth | phases are vertical and shippable; each gate is a usable state |

## 7. Phase 1 — delivered

| Area | What exists | Verified by |
|---|---|---|
| Plan | `Docs/architecture/00–10` | review |
| Contracts | `contracts/`: primitives, ids, 60 error codes with parameters, envelope, paging, 34 game actions (request + result + role + exclusivity), 7 callables, C# code generator, shared wire fixtures | 21 Vitest tests; `contracts:check` |
| Generated C# | `Assets/Farmverse/Runtime/Contracts/Generated` (98 files) + `Runtime/` (`Wire`, `WireJson`, `CallableSpec`, `GameActionInfo`) | compiles as .NET Standard 2.1 / C# 9 with warnings as errors |
| Unity Core | `Assets/Farmverse/Runtime/Core`: `Result`/`AppError`/`Guard`, `IClock`/`ServerClock`, `ReactiveProperty`, `CompositeDisposable`, `EventBus`, `IScheduler`, logging, `RequestIds`, the list engine (`PagedCollection`, `LocalListSource`, `LocalQuery`), `SearchIndex` | 52 EditMode tests, run headless by `tools/dotnet` |
| Backend | `backend/functions`: callable pipeline, `AppError`, clock, structured logger, ids, signed cursors, token bucket, store port (Firestore + in-memory), `UnitOfWork`, idempotency, aggregate gateway, action registry, the batch pipeline, `actions-submit` | 25 Vitest tests; esbuild bundle loads |
| CI | `.github/workflows/ci.yml` | — |

Nothing in the legacy code was changed or deleted.

## 8. Phase 2 — delivered

| Area | What exists | Verified by |
|---|---|---|
| Catalogue contract | `contracts/src/catalogue`: `CatalogueDoc` and every rule type, `STAT_KEYS`, well-known ids, `validateCatalogue` (cross-references, contiguous ladders, sorted titles, unknown stats) | Vitest; `economy:check` |
| Economy data | `contracts/economy/economy.json` v1: 26 items, 7 crops, 2 animals, 2 trees, 15 objects with level ladders, 8 recipes, plot/market/post/order rules, titles, starter packs, 7-day bonus | `economy:check` in CI |
| Actions | 32 (livestock water/clean removed; `livestock.feed` settles production) | contract tests |
| TS reference domain | `backend/functions/src/domain`: basis-point math, FNV-1a + xorshift32, UTC days, `Catalogue`, crop/soil/well/livestock/tree rules, production queue, warehouse shelves, market fee/reference/band, Trading Post, level requirements, net worth & titles, NPC order board, daily streak | 34 hand-verified Vitest cases |
| Conformance vectors | `contracts/vectors/*.json`: 11 modules, 1,174 seeded cases from `tools/vectors.ts` | `vectors:check` + backend test |
| C# domain | `Assets/Farmverse/Runtime/Domain` (`Farmverse.Domain`, engine-free): the same rules, `GameCatalogue` over the generated `CatalogueDoc` | `ConformanceVectorTests` replays all 1,174 cases byte-for-byte (a 1 ms drift fails 117 livestock cases) |
| Firebase config | `Assets/Farmverse/Content/Config/Firebase` (dev/staging project `farmverse-8deb6`), `.firebaserc` | — |

The legacy `config/economy.json` stays in place for the old code until its
cut-over (strangler rule); the new one is the source of truth for new code.

## 9. Phase 3 — delivered (backend game server)

| Area | What exists | Verified by |
|---|---|---|
| Document contracts | `contracts/src/documents`: Player, PlayerMeta, Settings, Inventory (quality stack keys), Estate, Production, Plot (crop/animal/tree slots, well), Listing, Trade, MarketStats, LedgerEntry, Notification, PublicProfile, LeaderboardEntry → 148 generated C# files | contract tests; `contracts:check` |
| Typed state | `src/game`: aggregate table (key → path → schema → empty), `GameState` + copy-on-write `Draft`, `gameGateway` (parse on load = converter, validate + `rev+1` on save, `netWorthDirty`, follow-up loads for the seller of a listing) | gateway tests (DATA_CORRUPT, changed-only writes, replay) |
| Every action | 32 handlers in `src/actions/handlers` (farming, livestock, trees, production, shop, market, Trading Post, orders, daily bonus, tutorial, plots); coins only via `wallet` (ledger row per movement); storage only via `stock` | 45 scenario and service tests in `test/game` playing the game through `submitBatch`; registry test = every contract kind implemented; Σ ledger == coins asserted |
| Callables | `player-create` (starter packs, unique names), `player-updateProfile`, `player-setRole` (14-day cooldown), `session-catchUp` (offline report), `push-registerToken`, `account-delete` | service tests; surface test (exports == contract names + jobs) |
| Jobs & triggers | `market-expireListings`, `market-aggregateStats` (cursor, 7-day reference price), `ledger-reconcile`, `leaderboard-onPlayerChanged` | job tests in memory |
| Rules & indexes | `backend/firestore.rules`, `storage.rules`, `firestore.indexes.json` (+ TTL overrides), `backend/firebase.json`, `.firebaserc` | `backend/rules-tests` (8 cases) and `test/emulator` — **run in CI's emulator job; the emulator could not be downloaded in the build sandbox, so these have not been executed yet** |
| Tooling | `npm run catalogue:publish -- --project <id>` (refuses invalid or silently changed versions) | — |

Gate status: the in-memory suite is green (107 tests); the emulator suite runs
on the first CI run. The legacy root `firebase.json` and `functions/` stay until
the client cut-over (Phase 4) points at the new callables — deleting them now
would break the shipped client.

## 10. Phase 4 — delivered (client application & infrastructure)

| Area | What exists | Verified by |
|---|---|---|
| Application (engine-free) | ports, `CallableClient` (envelope, server clock), `Store<T>` / `KeyedStore<T>` (server ⊕ overlays retired by rev), `ActionQueue` (coalescing, exclusive batches, persisted outbox, same-batchId retry, rollback from the refused index, offline policy), predictors mirroring the server handlers, `SlotActions`, `GameStateSync`, `CatalogueService`, `SessionService` (boot state machine, pause/resume catch-up), Account/Notification/Visit services, keyset-paged list sources | EditMode tests under plain .NET (TestHost) |
| Infrastructure | Firebase: Firestore store, Functions transport, Auth, Remote Config, Crashlytics, FCM, platform bootstrap (App Check first, persistence, emulators). Unity: file local store, main thread, connectivity, app info, log sink | compile-checked against the real UnityEngine + Firebase assemblies (`tools/dotnet/UnityCheck`) |

## 11. Phase 5 — delivered (UI)

MVP presenters for every launch screen (engine-free, `Presentation/Core`) and
UI Toolkit views built in code from a small component library (`FvButton`,
`Chip`, `QuantityStepper`, `Meter`, `PagedListView`), a `UiShell` with root /
stack / overlay / system / toast layers, design tokens and components in USS
(`Content/UI/Styles`, role themes), and the English string table
(`Content/Localization/en.json`, 308 keys) checked by
`tools/codegen/check_strings.py` against every error code, catalogue entry,
enum label and key used in code. Presenter tests drive fake views.

Deviation: views are built in C# rather than UXML — the layouts are simple,
and code keeps them compile-checked; UXML can replace any view without touching
its presenter.

## 12. Phase 6 — delivered (3D world) and the composition root

See 02 §7.1 and §8. `GameRoot` wires the whole graph; **Farmverse ▸ Set Up Boot
Scene** builds a playable grey-box scene. World logic (hex maths, snapshot,
diff, taps) is unit-tested; the Unity side is compile-checked.

Legacy client, backend and tools removed (01 §4). Not done: VFX, 3D visit mode, real art.

## 13. Phase 7 — delivered (social & live-ops)

Weekly leaderboards (ISO-week board ids identical in C# and TS, TTL'd rows),
push delivery (`push-onNotificationCreated`: opt-in, quiet hours in the player's
time zone, server-rendered copy from `contracts/src/push.ts`, stale-token
pruning), `visits-getSnapshot` (read-only, respects `allowVisitors`, never
coins or stock), operator callables `admin-adjustCoins` / `admin-restrictTrading`
with `adminAudit`, `tools/setAdmin.ts`, and the analytics taxonomy (09 §4).

## 14. Phase 8 — hardening (partly delivered)

Delivered: App Check enforced on every callable (08 §3.1), `account-export`,
contention mapped to retryable `CONFLICT`, load-test tool with measured
results (09 §5.1), rules tests for the new collections, emulator suites now
**executed and green** (rules 10/10, game integration 1/1 — they found and fixed
a real rules bug: `request.query` is undefined on `get`, which broke trade
reads). Designed only: cosmetic IAP with receipt validation (12). Remaining for
launch: device performance pass, store privacy labels, soft launch.
