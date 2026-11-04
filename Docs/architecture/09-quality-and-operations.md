# 09 — Quality, delivery & operations

## 1. Test pyramid

| Level | What | Tooling | Runs |
|---|---|---|---|
| Unit — Core/Domain/Application (C#) | rules, stores, action queue, paged lists, search | NUnit via `dotnet test` on the engine-free assemblies (no Unity needed) **and** Unity Test Runner EditMode | every push |
| Unit — backend domain/modules (TS) | rules, handlers with an in-memory UnitOfWork | Vitest | every push |
| **Conformance vectors** | identical results from C# and TS for growth, harvest, production, fees, price band, net worth, order board | `contracts/vectors/**/*.json` consumed by both suites | every push |
| Contract | generated C# is current; every schema round-trips (TS parse ↔ C# FromWire/ToWire on sample fixtures) | `contracts:check`, fixture tests | every push |
| Security rules | allow/deny matrix | `@firebase/rules-unit-testing` + Firestore emulator | every push |
| Integration — backend | each action against the emulator: happy path, every error code, retry = one effect, concurrent buyers on one listing, batch prefix semantics | Vitest + Firebase Emulator Suite | every push |
| PlayMode — client | boot to Home against the emulator; tutorial; buy → plant → harvest → sell; market browse/paging | Unity Test Runner PlayMode (GameCI, `-batchmode` with graphics) | nightly + release |
| Load | N players play the opening loop concurrently, then all buy from one listing (worst-case contention); ledger reconciled afterwards | `npm run load:emulator` / `load:memory` (`backend/functions/tools/loadTest.ts`, the real pipeline over the emulator or memory); a staging soak with k6 against deployed callables before launch | before launch & big features |
| Device | FPS, memory, thermals on a low/mid/high device matrix | Firebase Test Lab (Robo + game loop), manual QA | release candidates |

Coverage targets: Domain ≥ 90% lines (C# and TS), Application ≥ 80%,
backend modules ≥ 85%. Every bug fix adds a test that fails without it.

## 2. CI/CD (GitHub Actions)

```
on: pull_request
  contracts:   npm ci · lint · typecheck · test · contracts:check (generated C# up to date)
  backend:     npm ci · lint (import boundaries) · typecheck · vitest (unit) · emulator integration · rules tests · catalogue validate
  client-core: dotnet test (Core, Contracts, Domain, Application + vectors)
  unity:       GameCI test runner EditMode (cache Library) · build Android (dev) on label `build`
on: push main
  all of the above · deploy backend + rules + indexes to STAGING · build staging Android/iOS → Firebase App Distribution
on: tag v*
  deploy to PROD (manual approval) · store builds (Play internal / TestFlight) · Crashlytics symbol upload
```

Branching: trunk-based, short-lived feature branches, squash merges,
conventional commits, required review from a code owner of each touched area
(`CODEOWNERS`: contracts, backend/security, client/core, client/ui, client/world).

## 3. Environments

| | dev | staging | prod |
|---|---|---|---|
| Firebase | Emulator Suite (local) | `farmverse-staging` | `farmverse-prod` |
| Client config | `EnvironmentConfig_Dev` (emulator host) | `_Staging` | `_Prod` |
| App Check | debug provider | debug tokens for QA devices | enforced, real providers |
| Data | seeded fixtures | anonymised snapshot, resettable | live |
| Remote Config | template defaults | staging template | prod template (changes via PR) |

The environment is chosen by **build profile**, never by a runtime switch a
player could flip. `google-services.json` / `GoogleService-Info.plist` per
environment are injected by the build pipeline.

## 4. Observability

| Signal | Tool | Alerts |
|---|---|---|
| Client crashes / ANRs / non-fatals | Crashlytics (custom keys: route, env, clientVersion, lastBatchId) | crash-free < 99.5%, new top issue |
| `prediction_mismatch` (client vs server) | Crashlytics non-fatal + Analytics event | > 0.5% of actions for any kind |
| Function errors / latency | Cloud Logging + Error Reporting + Cloud Monitoring | error rate > 1% (5 min), p95 `actions-submit` > 800 ms |
| Firestore reads/writes per DAU | Cloud Monitoring + billing export | > 2× budget (04 §10) |
| Economy | `system/economy` counters, BigQuery views | mint/burn ratio outside 0.9–1.3 for a day |
| Ledger integrity | `ledger-reconcile` job | any mismatch → page |
| Product analytics | Firebase Analytics → BigQuery (funnel: install → role → first harvest → first sale → D1/D7) | dashboard, weekly review |

Analytics events are typed: every name and parameter lives in
`Application/Features/GameAnalytics.cs` (`AnalyticsEvents`), checked by a test
for GA4 validity (snake_case, ≤ 40 chars). `GameAnalytics` turns application
events into analytics (`session_ready` with cold-start ms, `session_failed`
with the state, `action_refused` by kind and code); presenters send only
`player_created` and `listing_created`. No names or free text are sent.

## 5. Performance budgets (client)

| Budget | Target |
|---|---|
| Cold start → Home (mid-range Android) | ≤ 6 s (≤ 3 s warm) |
| Frame time | 16.6 ms (60 fps) on mid, 33 ms floor on low; UI ≤ 2 ms, world ≤ 6 ms |
| GC allocations per frame (steady state) | 0 B |
| Memory | ≤ 700 MB on 3 GB devices |
| Install size | ≤ 150 MB base; world art via Addressables remote groups |
| Draw calls / tris | ≤ 120 / ≤ 150k |

Verified by an automated profiler capture in the nightly PlayMode run and by
device QA.

### 5.1 Load test results (emulator, Oct 2026)

| Scenario | Result |
|---|---|
| 300 players × 3 rounds, in memory (pipeline cost) | p95 1 ms per batch, 0 failures, 0 ledger mismatches |
| 20 players concurrently on the emulator | opening loop p95 0.57 s; 19 concurrent buyers on one listing all succeed, serialised (p50 10 s) |
| 100 players concurrently on the emulator | opening loop p95 1.3 s; 97 of 99 concurrent buyers on one listing exhaust their retries (`CONFLICT`), no ledger mismatch |

**Hot documents.** Independent players scale; one listing bought by dozens of
players in the same second does not, because every buy locks the listing and
the seller. Correctness holds (the ledger reconciles), and the client retries
`CONFLICT` automatically. Mitigations if telemetry shows real hot listings:
cap concurrent buyers per listing with a short server-side lease, or move
large lots to a purchase-intent queue settled by a trigger. The emulator is far
slower than production under contention, so the staging soak decides.

### 5.2 World budgets

The world rebuilds its snapshot at most once a second and on store changes,
and sends the view only the diff; prefabs come from a pool, so steady play
spawns nothing. A farm of 64 plots is ≈ 1 000 visuals: budget ≤ 1 ms for the
rebuild on the mid device (profile with the PlayMode capture).

## 6. Operations

- **Runbooks** in `Docs/runbooks/`: incident, rollback (functions + rules),
  maintenance mode (`rc.maintenance = true`), forced update (`rc.minClientVersion`),
  economy hotfix (catalogue publish), App Check outage.
- **Backups:** Firestore PITR (7 days) + daily managed export to a locked
  bucket, 30-day retention; restore drill each quarter.
- **Live-ops changes** (catalogue, RC) go through a PR, CI validation, staging
  first, then prod publish by the release owner.
- **TTL policies** (one-time, per project): `gcloud firestore fields ttls update expireAt --collection-group=requests --enable-ttl`, the same for `notifications` and `entries` (weekly leaderboard rows, kept 35 days).
- **Support tools:** `admin-adjustCoins` (grant/clawback through the ledger)
  and `admin-restrictTrading`, both audited; grant the claim with
  `npm run admin:set`.
