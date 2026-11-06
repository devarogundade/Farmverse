# Farmverse game server

Node 22 + TypeScript: **Fastify** (HTTP + WebSocket), **Postgres** through **Prisma 7**,
**Redis** (sessions, rate limits, live fan-out) and **BullMQ** (jobs). It replaces the Firebase
backend; the game rules themselves are shared, unchanged, from `backend/functions/src/{core,game,infra}`.
Design: [Docs/architecture/13-near-web3-plan.md](../../Docs/architecture/13-near-web3-plan.md) §5.

## Run locally

Postgres 17 and Redis 7 run as containers (WSL or any Docker):

```bash
docker run -d --name fv-postgres --restart unless-stopped -e POSTGRES_USER=farmverse -e POSTGRES_PASSWORD=farmverse-dev -e POSTGRES_DB=farmverse -p 127.0.0.1:5432:5432 -v fv-pgdata:/var/lib/postgresql/data postgres:17-alpine
```

```bash
docker run -d --name fv-redis --restart unless-stopped -p 127.0.0.1:6379:6379 -v fv-redisdata:/data redis:7-alpine redis-server --appendonly yes
```

On WSL2 the VM stops when nothing runs in it, taking the containers down. Keep a terminal open
in WSL, or set `vmIdleTimeout=-1` under `[wsl2]` in `%UserProfile%\.wslconfig`.

```bash
cp .env.example .env        # then set JWT_SEED (command in the file)
npm run db:deploy           # apply migrations
npm run dev                 # API on :8787
npm run dev:worker          # jobs
```

## Tests

```bash
npm test                    # unit: access rules, NEP-413, call registry, store conformance (in memory)
npm run test:db             # Postgres + Redis: store conformance, wallet sign-in → calls → live push, jobs
```

The **store conformance suite** (`test/storeConformance.ts`) runs the same scenarios against the
in-memory store the 100+ game tests use and against Postgres: transactions, merge, delete,
queries, and 15 concurrent read-modify-writes on one document. Both passing is what lets the
game tests' results stand for production.

## API

| Route | |
|---|---|
| `POST /v1/auth/nonce` | `{nonce, message, recipient}` for the wallet to sign (NEP-413) |
| `POST /v1/auth/wallet` | `{accountId, publicKey, signature, nonce}` → `{uid, accountId, accessToken (15 min), refreshToken (30 d)}` |
| `POST /v1/auth/refresh` · `/logout` | rotate / revoke; reusing a rotated refresh token ends the whole session |
| `POST /v1/call/{name}` | every callable in `contracts/src/callables.ts` (`island-sync`, `wallet-spend` …); body = request, reply = the usual envelope |
| `GET /v1/doc?path=` | one document the caller may read (rules in `src/access.ts`) |
| `POST /v1/query` | `{collection, filters, orders, limit, startAfter}` — the client's paged lists |
| `PATCH /v1/doc` | `{path, fields}` — only own settings and notification `readAt`, validated as the old rules did |
| `GET /v1/live` (WebSocket) | `{type:"auth",token}` then `sub {id,path}` / `subq {id,query}` / `unsub`; pushes `doc {id,path,exists,rev,data}` and `query {id,docs}` |
| `GET /v1/config`, `/healthz` | |

**Identity.** A wallet signs in with NEP-413; the server checks the signature, that the key is a
full-access key of the account (NEAR RPC), and that the nonce is fresh and unused. The game uid
is `u` + 39 hex of `sha256(accountId)` — NEAR ids contain dots, which uids and document paths
don't allow — and the session carries both.

**Consistency.** Transactions run at `SERIALIZABLE` and retry on serialization failures — the
optimistic model the game code was written for under Firestore. Readers never block each other.

**Jobs** (`src/worker.ts`): `island-wake` and `push-task` (delayed, de-duplicated per minute by job
id, as Cloud Tasks did), `news-fanout` (booked when a `news/*` document is created), and the
weekly `leaderboard-pay-week` (Monday 00:10 UTC).

## Deploy

`npm run build` → `dist/main.js` (API) and `dist/worker.js` (jobs); run `npm run db:deploy` as a
release step. Both are stateless; scale them separately. Needs `DATABASE_URL`, `REDIS_URL`,
`JWT_SEED`, `NEAR_NETWORK`, `NEAR_RPC_URL`, `AUTH_RECIPIENT`, `ALLOWED_ORIGINS`, `ISLAND_ENGINE_URL`.

## Not done yet

* Mint / redeem calls and the mint voucher signer (step 3b).
* The Unity client's web adapters (step 4); until then the shipped client still talks to Firebase.
* `content-manifest` returns no bundles for `webgl` until WebGL Addressables are published.
* Turnstile on sign-in (rate limits are in place).
