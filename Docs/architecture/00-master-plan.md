# Farmverse — Master Architecture & Refactor Plan

> **Status:** approved direction, Phase 1 in progress
> **Owner:** engineering
> **Scope:** the Unity client, the Firebase backend, the contract between them, and the path from today's code to a launch-ready game.

This is the top-level document. It states what we are building, what is wrong
with the current code, the principles every later decision follows, and the
decisions themselves. The detail lives in the numbered documents beside it.

| # | Document | Answers |
|---|---|---|
| 00 | **Master plan** (this file) | What, why, the principles, the decisions |
| 01 | [Repository & module map](01-repository-and-modules.md) | Where every file lives, which layer may depend on which |
| 02 | [Unity client architecture](02-client-architecture.md) | Layers, assemblies, state, UI, 3D world, boot |
| 03 | Backend architecture (removed) | Replaced by 13: `backend/server` running `backend/game` |
| 04 | [Firestore data model](04-data-model.md) | Every collection, document shape, index, TTL, cost |
| 05 | [API contract](05-api-contract.md) | The envelope, every action, every error code, codegen |
| 06 | [Lists: paging, filtering, sorting, search, refresh](06-lists-and-queries.md) | One engine for every list in the game |
| 07 | [Game systems & economy](07-game-systems-and-economy.md) | Roles, loops, formulas, sinks and faucets |
| 08 | [Security & anti-abuse](08-security.md) | Rules, App Check, rate limits, market abuse |
| 09 | [Quality, delivery & operations](09-quality-and-operations.md) | Tests, CI/CD, environments, observability, performance |
| 10 | [Roadmap & migration](10-roadmap-and-migration.md) | Phases, gates, what gets deleted and when |

---

## 1. The product in one paragraph

Farmverse is a 3D mobile farming economy (iOS + Android, portrait, Unity 6 +
URP) where every player is either a **Farmer** or a **Manufacturer**. Both buy
fundamentals from the **game shop** (seeds, fertilizer, tools, machines,
facilities, land). Farmers grow crops and raise animals on hexagonal plots;
Manufacturers run machines that turn raw goods into finished products. Both
sell to each other on a player-run **marketplace**. The whole backend is the
Firebase suite: Auth, App Check, Firestore, Cloud Functions, Remote Config,
Cloud Messaging, Crashlytics, Analytics (+ BigQuery export) and Storage.

The loop that has to be fun and stable at scale:

```
          ┌──────────── game shop (coins → fundamentals) ────────────┐
          ▼                                                          │
   FARMER: plant → care → harvest ──► raw goods ──┐                  │
                                                  ▼                  │
                                   MARKETPLACE (player ↔ player)     │
                                                  ▲                  │
   MANUFACTURER: buy raw → machine → product ─────┘                  │
          │                                                          │
          └── NPC demand (orders, Trading Post floor) → coins ───────┘
```

## 2. Audit of the current code — why a rebuild, not a tidy-up

The existing code was generated quickly and works as a demo, but it cannot carry
a live game. The problems are structural, so moving files around would not fix
them.

### 2.1 Architecture

| Problem | Evidence | Consequence |
|---|---|---|
| One god object owns the game | `FarmSimulation` is a `partial class` spread over 7 files (~150 KB): wallet, storage, tools, well, market, machines, plots, food, empire, roles | Every feature touches every other; nothing is testable in isolation |
| The client *is* the server | `MockFarmApi.Submit` calls `FarmSimulation.Apply` directly; the UI reads `FarmSimulation` fields | The "api seam" is a fiction: there is no network boundary, no async, no failure path, no latency |
| Time is accumulated, not derived | `FarmSimulation.Advance(now)` mutates ages, wear and stock every frame | Cannot match a server that stores timestamps; drift is guaranteed |
| No async anywhere | `IFarmApi.Submit` returns `ActionResult` synchronously | A real callable is 150–800 ms and can fail; every screen would need rewriting |
| Mock data ships in the runtime | `MockData`, `MockWorld`, `ReferenceMarketSeed` are referenced by `UiRouter` and `FarmSimulation` | Fake sellers and fake players in production builds |
| One assembly | All runtime code in `Assembly-CSharp`; "ring purity" is a comment | No enforced layering, full recompiles, accidental `UnityEngine` in rules |
| Code was produced by code | `Tools/art_gen/scaffold_*.py`, `wire_*.py`, `fix_*.py`, `rename_*.py` rewrite C# files with regex | Nobody can safely edit a file a script may overwrite |
| Strings are the protocol | Server throws English sentences (`refuse('That plot is not on your farm.')`) | No localization, no client branching on error type |

### 2.2 Backend correctness (these are bugs today)

| # | Defect | File |
|---|---|---|
| B1 | `stock.ts` writes `players/{uid}/stock/*`, but the rules only allow reading `inventory/*` — the client **cannot read its own goods** | `actions/shared/stock.ts`, `firestore.rules` |
| B2 | `promoteListing` checks `data.status !== 'active'`, but no listing has a `status` field — **promotion always fails** | `marketplace/promoteListing.ts` |
| B3 | The market fee (`feePct: 5`) is configured but never charged; the seller receives 100% | `marketplace/buyListing.ts` |
| B4 | Server harvest scales yield by `water`/`fertilizer`, but no server action ever writes them — **yield is always the minimum**; ripeness ignores the care rule the client uses | `farm/harvest.ts` |
| B5 | Plot position is written as `row/col` by `buy.ts`, typed as `q/r` in `PlotDoc`, documented as `lands/` in `backend-plan.md` | `shop/buy.ts`, `types/documents.ts` |
| B6 | The rating is published for the caller only; the seller of a trade keeps a stale rank | `submitAction.ts` |
| B7 | `onPlayerWrite` **and** `submitAction` both publish the rating, and `computeFarmRating` does 1 + N + M reads per call (N stock items, M owned objects) | `economy/farmRating.ts` |
| B8 | `catchUp` reads `config/crops/{type}` inside a loop per crop — unbounded reads, no transaction, not idempotent | `actions/catchUp.ts` |
| B9 | No input validation: payloads are cast (`as Record<string, unknown>`), numbers are `Number(x ?? 0)` | everywhere |
| B10 | Only 9 of ~45 client verbs exist on the server, so the shipped client cannot work online | `types/actions.ts` vs `Logic/Farm/Actions.cs` |
| B11 | No listing price bounds — two accounts can launder unlimited coins through a 1-item listing priced at 10,000,000 | `marketplace/createListing.ts` |
| B12 | `Firebase.Storage` and `Firebase.Analytics` are not in the Unity SDK import, though the design depends on them | `Assets/Firebase/Plugins` |

### 2.3 What is worth keeping

- **The principles in the docs are good**: server authority, idempotency keys,
  derived time, an append-only ledger, read-only visits, no pay-to-win, no fake
  urgency. We keep every one of them and make them enforceable.
- `config/economy.json` as the single source of economy numbers (it moves into
  the contracts package and gains a validator).
- The UI art pipeline (generated 9-slice art, USS design tokens, fonts) — it is
  presentation assets, not architecture, and is reused as-is.
- The push policy (allow-list, quiet hours, 3/day cap) — rewritten, same rules.
- The screen inventory and navigation map in `Docs/navigation.md` — it becomes
  the product backlog, re-scoped in [10](10-roadmap-and-migration.md).

## 3. Principles (binding)

Every pull request is reviewed against these. When two conflict, the lower
number wins.

1. **The server is the only writer of game state.** The client never writes a
   Firestore document that holds coins, goods, objects, listings, ranks or
   timers. Security rules make this impossible, not just discouraged.
2. **One contract, generated twice.** Every request, response, document and
   error code is defined once in `/contracts` (TypeScript + zod) and generated
   into C#. A field cannot exist on one side only.
3. **Facts are stored, time is derived.** Documents hold timestamps of what
   happened (`plantedAt`, `startedAt`, `expiresAt`). Stages, progress and
   readiness are pure functions of `(facts, rules, now)`, implemented once per
   language and proven identical by shared test vectors.
4. **Every mutation is an idempotent, validated, transactional action.** A
   request id makes a retry free; a zod schema rejects bad input before any
   read; one Firestore transaction moves coins and goods together; a ledger row
   explains every coin.
5. **Layers only point down.** Presentation → Application → Domain → Core.
   Infrastructure implements Application's ports. Enforced by assembly
   definitions (client) and lint import rules (backend).
6. **Errors are codes, not sentences.** The server returns `ErrorCode` +
   parameters; the client localizes. No player-facing English in the backend.
7. **Reads are budgeted.** Every list is paginated, every listener is scoped,
   every query is backed by a declared index. Firestore cost follows reads.
8. **The client is predictive, never authoritative.** It applies an action
   optimistically using the same domain rules, then reconciles to the server's
   documents. A mismatch is a logged metric, not a silent bug.
9. **Nothing ships that is not tested.** Domain rules: unit + conformance
   vectors. Actions: emulator integration tests. Rules: rules-unit-tests. UI
   flows: PlayMode smoke tests. CI blocks a merge on any failure.
10. **Fair by design.** No pay-to-win, no timers sold for money, no fake
    urgency, never punish being offline, visitors are read-only, the B3TR token
    layer stays behind a feature flag.

## 4. Key decisions (ADR summary)

Each decision is final unless reopened with a written reason.

| ADR | Decision | Rejected alternative | Why |
|---|---|---|---|
| 001 | **Firebase-only backend**: Firestore + Cloud Functions v2 (Node 22, TypeScript) | Custom server | User requirement; managed scaling; rules give enforceable authority |
| 002 | **Writes only through one batched callable, `actions-submit`** (plus a handful of non-game callables: player, account, admin) | One callable per verb | One idempotency + transaction + rate-limit path; batching cuts cost and latency of tap-heavy farming |
| 003 | **Reads directly from Firestore** with listeners and the SDK's offline cache; rules allow reading own data and public projections | Read through callables | Realtime for free, offline cache, cheaper, lower latency |
| 004 | **Aggregate documents per player** (one inventory doc, one estate doc, one doc per plot with embedded slots) | A document per crop/item/tool | 5–10× fewer reads; transactions touch few docs; one listener per aggregate |
| 005 | **Contracts in zod (TS), C# generated** via `z.toJSONSchema` → custom emitter | Hand-written DTOs on both sides | Single source of truth, CI fails on drift |
| 006 | **Domain rules written twice (C# + TS), proven by shared JSON test vectors** | Server-only rules (no prediction) / transpiling one language | Prediction is needed for 60 fps feel; vectors make drift a failing test |
| 007 | **Economy catalogue lives in Firestore (`catalogue/current`, versioned)**, published from `/contracts/economy` by a CI script; Remote Config holds only knobs and flags | Economy in Remote Config | RC has no per-value audit, no schema, 2 MB / string-typed; catalogue needs validation + versioning |
| 008 | **Listings expire by a scheduled sweep** (every 5 min) that returns goods automatically; buy also checks `expiresAt` | Manual reclaim only | Queries can filter `status == 'active'` (indexable), sellers never lose goods, UX is simpler |
| 009 | **Marketplace search = client-side catalogue search → `itemId` filters**; seller search by name prefix | Algolia/Typesense on day one | Items are a closed catalogue (<200); full-text adds cost and a vendor with no gain |
| 010 | **UI Toolkit, UXML + USS per screen, MVP (View + Presenter)**, virtualized `ListView` for every list | uGUI / code-built UI | Designer-editable layouts, low draw calls, testable presenters |
| 011 | **Unity Addressables** for screens, world prefabs and art | `Resources/` | Memory control, remote content updates, smaller install |
| 012 | **Firebase Emulator Suite** for dev and integration tests; no hand-written mock backend in runtime code | `MockFarmApi` | One behaviour to test against; mocks only in test assemblies |
| 013 | **Two roles at launch: Farmer, Manufacturer.** Role chosen at onboarding; changeable with a cooldown (see 07) | Four roles | Matches the product; the domain model keeps roles as data so more can be added |
| 014 | **Environments: `dev` (emulator), `staging`, `prod`** as three Firebase projects | One project | Safe deploys; prod data never touched by tests |
| 015 | **Money is integer coins (`int64` C# / `number` safe-integer TS)**; prices, fees and yields are integers; fractions only inside formulas and rounded once, by a named rule | Floats | No rounding drift between C# and TS |

## 5. What "done" means for launch

- A new player installs, signs in anonymously, picks a role, completes the
  tutorial, and makes their first sale within 10 minutes.
- Every action works online, survives a retry, a double tap, an app kill
  mid-request, and a 3-day absence.
- The marketplace browses 100,000 active listings with filters and sorting at
  < 300 ms per page and < 25 reads per page.
- p95 action latency < 600 ms; crash-free sessions ≥ 99.5%; 60 fps on a
  mid-range Android (Snapdragon 7-series), 30 fps floor on low-end.
- Firestore cost < $0.002 per DAU per day at 100k DAU (budget in 04 §9).
- Zero ledger/balance mismatches in the nightly reconciliation.

## 6. Reading order for a new engineer

00 → 01 → 05 (the contract) → 02 or 03 (your side) → 04 → 06 → 07 → 08 → 09 → 10.
