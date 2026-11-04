# The backend is Firebase

The NestJS server is gone. Firebase is the trusted half of Farmverse, and this is
what that means for each rule the design documents set, where each rule is
enforced, and what changed in the design because of it.

**Related:** [`firebase-integration-plan.md`](firebase-integration-plan.md) — how
each service in the suite is wired on both sides of the trust boundary, the order
it gets built in, and what it costs.

## What replaced what

| Was | Is |
|---|---|
| NestJS modules + controllers | Cloud Functions (`functions/src/actions`, `jobs`, `triggers`) |
| Prisma + Postgres | Firestore |
| Prisma migrations | `firestore.rules`, `firestore.indexes.json` |
| `config/economy.json` served by an API | the same file, emitted as **Remote Config** |
| Socket.IO realtime | Firestore listeners (`onSnapshot`) |
| Server-issued JWTs | **Firebase Auth** |
| A push worker | **Cloud Messaging** |
| — | **App Check**, **Storage**, **Crashlytics** |

The single source of the economy is still `config/economy.json`. `gen_catalogue.py`
reads it and writes the C# table, the TypeScript table, and
`remoteconfig.template.json` — so a price cannot differ between the client, the
Functions and Remote Config, because there is one file.

## Where each golden rule is enforced now

| Rule | Where it lives | Note |
|---|---|---|
| 1. The server is authoritative | `firestore.rules` | **There is no rule that lets a signed-in client write a coin, an item count, a ledger entry, a listing or a rank.** A tampered client cannot mint coins because no path in the rules accepts it. Every mutation is a Function, which runs with admin credentials. |
| 2. Idempotency keys | `functions/src/shared/idempotency.ts` | The key is a document. The transaction reads it first; if it exists the stored result is returned and nothing is applied. The guarantee is a property of the database, not of a handler remembering to check. |
| 3. Trades are atomic | `db.runTransaction` in `submitAction` | **Changed:** Firestore has transactions but no row locks. A transaction re-reads and retries on contention, which is the same guarantee for a design where each tradable thing is its own document — but it is not the same mechanism the document names. |
| 4. Append-only ledger, never below zero | `functions/src/shared/ledger.ts` | **Changed:** Postgres could enforce "never below zero" with a constraint. Firestore cannot, so the invariant lives in the only code that moves coins, and the rules stop anything else trying. The ledger row is written *in the same transaction* as the balance, so a balance cannot exist that no entry explains. |
| 5. Timers run on the server | `catchUp`, `expireListings`, `refreshLeaderboard` | The offline report is the server's account of what happened. The window is bounded by the larder, so an empty one shortens it rather than punishing. |
| 6. No pay-to-win | — | Nothing sells plots, upgrades or timers. Coins buy them, so coins are not sold. |
| 7. Never an investment | — | Plots cannot be transferred; `firestore.rules` has no rule that would allow it. |
| 8. B3TR behind a flag | `Data/Configuration/FeatureFlags.cs` | Off at launch, served through the api set so the server owns it. |
| 9. Visitors are read-only | `firestore.rules` → `worldSnapshots` | The snapshot is a **separate document** the server writes. A visitor reads that, never the owner's own documents — so a sanitized snapshot is the only thing that exists to read. The `allowVisitors` setting is checked in the rules, so every reader obeys it. |
| 10. Security code needs review | — | `firestore.rules`, `ledger.ts` and `idempotency.ts` are the three files that qualify. |

## The data model

Firestore is a document store, so the 102-table design in `Docs/data-model.md`
becomes collections. The shape is chosen so the *rules* can be the authority.

```
players/{playerId}
  coins, meals, hoursPerMeal, offlineWindowHours, allowVisitors, lastSeenSeconds
  title, landTiles, createdAt

  inventory/{itemId}       quantity, reserved          ← reserved is a listing's hold
  ledger/{entryId}         amount, reason, reference, counterparty, at
  plots/{plotId}           level, tiles, maxTiles, type, ground, growth, cropId
  units/{unitId}           gameObjectId, level, count, wear, stored
  requests/{requestId}     result, at                  ← the idempotency key

catalogue/
  objects/{gameObjectId}   category, name, icon, levels[] { level, price, properties, requirements }
  items/{itemId}           category, name, icon, price, essential, floorPct, ceilingPct
  properties/{level}_{key} an admin-set property, so a new one needs no schema change

listings/{listingId}       sellerId, itemId, quantity, unitPrice, quality,
                           promoted, status, createdAt, expiresAt

leaderboard/{playerId}     farmRating, balance, inventory, objects, updatedAt

worldSnapshots/{playerId}  the sanitized projection a visitor may read

catalogueChanges/{id}      actorId, reason, changes, at   ← the admin audit trail
```

Three choices worth naming:

- **`inventory` is a subcollection of the player**, not a top-level one, so the
  rule that a client may not write it is written once, in the player's own block.
- **`requests` is a subcollection too**, which makes the idempotency key a
  document a transaction can read and write atomically with the action.
- **`catalogue/properties` is keyed by level and property name**, so an admin
  adding a property the client has never heard of is a write, not a migration.

## What the client does

Nothing structural. The client's api layer was built as a seam for exactly this:
`IFarmApi`, `ICatalogueApi`, `ITradingPostApi`, `IVisitorApi`, `IClockApi` and
`ILeaderboardApi` are unchanged, and `ClientApis.Mock()` is what the offline build
runs. A Firebase implementation is a second set behind the same interfaces.

The one behavioural difference: `ILeaderboardApi` reads a projection
(`leaderboard/{playerId}`) rather than computing a rank, because a client cannot
see other players' holdings — which is why the projection exists.

## What you have to do, once

1. Create the Firebase project, and put its config in
   `Assets/Farmverse/Resources/Farmverse/firebase-config.json`.
2. Turn on **Auth** (anonymous + Apple + Google), **Firestore**, **Storage**,
   **Cloud Messaging** and **App Check** (Play Integrity and DeviceCheck).
3. Put the App Check debug token in the client for development builds.
4. `firebase deploy --only firestore:rules,firestore:indexes,storage,remoteconfig,functions`
5. Seed the catalogue: run `python Tools/art_gen/gen_catalogue.py`, then the
   `seedCatalogue` job (to be written) reads `catalogue.data.ts` into Firestore.

## What this costs

- **No SQL.** Reporting and reconciliation become collection scans or BigQuery
  export. The metrics document's queries need re-expressing.
- **No row locks.** See golden rule 3 above.
- **No constraints.** See golden rule 4 above.
- **The layout diverges from `CLAUDE.md`.** That document names `/server` and
  `/db/schema.sql`; this repository has `/functions` and `firestore.rules`. The
  divergence is deliberate and recorded here rather than left to be discovered.
