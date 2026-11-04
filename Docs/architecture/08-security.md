# 08 — Security & anti-abuse

A viral economy game is attacked on day one: scripted clients, coin
laundering between alt accounts, replayed requests, scraping. Each threat has a
named control and a test.

## 1. Threat → control

| Threat | Control | Where |
|---|---|---|
| Client writes coins/goods directly | Rules: no client write to any game-state document; all mutations go through Functions (only own settings and notification `readAt` are client-writable, 11 §1.4) | `firestore.rules` + rules tests |
| Modified / scripted client calls Functions | App Check enforced on every callable; Play Integrity (Android), App Attest (iOS); debug tokens only in dev/staging | `defineCallable`, console enforcement |
| Replay of a captured request | Idempotency (`batchId` doc) + App Check **replay protection** (`consumeAppCheckToken`) on market and shop actions | `actions-submit` |
| Bad / huge payloads | zod schemas with bounds (ids by regex, quantities 1..9,999, arrays ≤ 20, strings ≤ 64) before any read | contracts |
| Request flooding | Per-uid token buckets (actions, market, profile); Functions `maxInstances`; Cloud Armor not needed for callables | `private/limits` |
| Time cheating (device clock) | Server uses its own clock for every decision; client clock only for display | domain takes `now` from the server |
| Coin laundering via the market (alt buys a junk item for millions) | **Price band** per item; new-account listing lock; self-trade refusal; same-device/IP pair flagging; daily trade-value caps for accounts < 7 days old | market domain + `trades` analysis job |
| Wash trading to climb leaderboards | Weekly board ranks net-worth *gain* excluding trades with flagged pairs; review queue | leaderboard job |
| Account takeover | Firebase Auth; linking Google/Apple; re-auth for deletion; no password storage | Auth |
| Data scraping | List queries capped (`limit ≤ 50`); `publicProfiles` exposes only public fields; `players/*` readable only by self | rules |
| Visiting someone who opted out | `visits-getSnapshot` checks `allowVisitors`; snapshot type carries no balances/inventory | visits module |
| Admin misuse | `admin` custom claim set only via a CLI with MFA'd console account; every admin call writes `adminAudit`; catalogue publish requires CI + reviewed PR | admin module |
| Injection into localized strings | Server sends codes + string params; client formats with Smart Strings in a safe mode (no nested formatting from params) | client |
| Secrets in the client | Only the public Firebase config ships; ad SSV keys, HMAC cursor key, IAP keys live in Secret Manager (`defineSecret`) | backend |

## 2. Firestore rules (shape)

```
rules_version = '2';
service cloud.firestore {
  match /databases/{db}/documents {
    function signedIn() { return request.auth != null; }
    function isSelf(uid) { return signedIn() && request.auth.uid == uid; }
    function smallQuery() { return request.query.limit <= 50; }

    match /{document=**} { allow read, write: if false; }          // default deny

    match /players/{uid} {
      allow get: if isSelf(uid);
      match /private/settings     { allow get: if isSelf(uid); }
      match /state/{doc}          { allow get: if isSelf(uid); }
      match /plots/{plotId}       { allow read: if isSelf(uid) && (request.method == 'get' || smallQuery()); }
      match /ledger/{id}          { allow list: if isSelf(uid) && smallQuery(); }
      match /notifications/{id}   { allow list: if isSelf(uid) && smallQuery(); allow get: if isSelf(uid); }
      // requests, pushTokens, private/meta, private/limits: server only (default deny)
    }
    match /publicProfiles/{uid}              { allow get: if signedIn(); allow list: if signedIn() && smallQuery(); }
    match /listings/{id}                     { allow get: if signedIn(); allow list: if signedIn() && smallQuery(); }
    match /marketStats/{itemId}              { allow get: if signedIn(); }
    match /leaderboards/{b}/entries/{uid}    { allow get: if signedIn(); allow list: if signedIn() && smallQuery(); }
    match /trades/{id} {
      allow read: if signedIn() && (resource.data.buyerUid == request.auth.uid
                                 || resource.data.sellerUid == request.auth.uid) && smallQuery();
    }
    match /catalogue/current                 { allow get: if signedIn(); }
  }
}
```

Note `allow write` appears **nowhere** for game state. The only client writes are
the two field-limited updates in 11 §1.4 (own settings, notification `readAt`). Every rule has a test in
`backend/rules-tests` (allowed reads succeed, every write is denied, other
players' data is denied, oversize queries are denied).

## 3. App Check rollout

1. Ship the client with providers enabled; console in **monitor** mode.
2. Watch the verified-request ratio for 7 days (target ≥ 98%).
3. Enforce on Functions, then Firestore, then Storage.
4. Failure UX: a clear "this version can't connect, please update from the
   store" screen, never a dead spinner.

### 3.1 As built (Phase 8)

- **Functions:** every callable is defined through `defineCallable`, which sets
  `enforceAppCheck: true` — there is no unenforced callable. Because the new
  backend ships together with an App Check-enabled client, there is no monitor
  period for Functions; the monitor → enforce sequence above applies to
  **Firestore and Storage**, enforced in the console after 7 days ≥ 98 % verified.
- **Client:** `FirebasePlatformBootstrap` installs Play Integrity (Android) /
  App Attest (iOS) before the default app exists; the debug provider is used
  only when `EnvironmentConfig.appCheck = Debug`. `Environment.prod` refuses
  (editor error) a debug provider, a debug token or emulator host.
- **Replay protection** (`consumeAppCheckToken`) is available per callable
  (`replayProtected`) but off: `actions-submit` is already idempotent by
  `batchId`, and limited-use tokens add a round trip on every batch.
- **Operator access:** `admin-*` callables require the `admin: true` custom
  claim (granted only with `npm run admin:set -- --project <id> --uid <uid>`
  from an operator machine). Every admin change writes `adminAudit/{id}` in the
  same transaction as the change; the collection has no client rule (denied,
  covered by the rules tests).
- **Hot documents:** a transaction that still loses after the SDK's retries is
  returned as `CONFLICT` with a jittered `retryAfterMs`, which the client's
  ActionQueue retries with the same `batchId` — never `INTERNAL` (09 §3).

### 3.2 Secrets in the repository

Nothing secret is committed. `.gitignore` excludes `.env*` (template:
`.env.example`), service-account and Admin SDK keys (functions use Application
Default Credentials), Android keystores, Apple certificates, provisioning
profiles and `.p8` keys, Firebase CLI caches and emulator exports. The App Check
debug token is read in the editor from `FARMVERSE_APPCHECK_DEBUG_TOKEN` or
`UserSettings/appcheck-debug-token.txt`, never from a committed asset. CI runs
`tools/codegen/check_secrets.py`, which fails on secret file types and on
private keys, service-account JSON and common API-token shapes in tracked files.
`google-services.json` / `GoogleService-Info.plist` stay committed: they are
public by design and the API key is restricted in the Google Cloud console.

## 4. Privacy & compliance

- Account deletion in-app (Apple/Google requirement): `account-delete` deletes
  Auth user + recursive delete of `players/{uid}`, `publicProfiles/{uid}`,
  `usernames/*`, anonymises `trades` (names → "Former farmer"), keeps ledger
  aggregates only in BigQuery without uid after 30 days.
- Data export: `account-export` returns one JSON document (player, settings,
  meta, holdings, plots, own listings, last 2 000 ledger rows, last 500
  notifications, devices without their push tokens), flagged `truncated` when a
  history was capped. The client hands it to the OS share sheet. (A Storage +
  signed-URL bundle is only needed if exports outgrow the 4 MB response cap.)
- Push: tokens live in `players/{uid}/pushTokens/*` (server only, never in an
  export); a push is sent only when `pushEnabled` and outside the player's quiet
  hours in their time zone; tokens FCM reports as unregistered are blanked.
- Consent: analytics + crash consent gate (screen 3) sets
  `setAnalyticsCollectionEnabled` and Crashlytics collection accordingly.
- Children: the game is rated for 12+; no chat; display names filtered by a
  denylist + a moderation queue for reported names.
