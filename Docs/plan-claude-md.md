# Plan: the client against CLAUDE.md

`CLAUDE.md` is the short version of four design documents. This is what the client
does **not** cover yet, and the order it gets done in: api → data → logic → ui and
scene, because each layer may only depend on the ones before it.

Nothing here invents economy numbers. Where a figure is needed it comes from
`config/economy.json`, and until that file exists the shipped values are marked as
placeholders.

---

## A. Api layer (mock) — **done**

| # | Gap | State |
|---|---|---|
| A1 | IDs are `MAIZE` / `WATER_WELL`; the convention is `crop_maize` / `facility_well` | **done** — the prefix says what a thing is, and both sides regenerate from one file |
| A2 | No Trading Post | **done** — floor and ceiling as the item's own percentages, essentials only |
| A3 | No visitor api | **done** — the snapshot *type* is the sanitization; allow-visitors checked in the api |
| A4 | No server clock | **done** — `IClockApi`; the client reads time from the server's clock and nowhere else |
| A5 | The leaderboard metric is ambiguous | **done** — the ranking is the net-worth figure the model computes, and the entry breaks down into balance, inventory and objects |
| A6 | No allow-visitors setting | **done** — on `IVisitorApi`, so every caller obeys it |

## B. Data layer — **done except B5**

| # | Gap | State |
|---|---|---|
| B1 | Economy numbers in the wrong file | **done** — `config/economy.json`, as the document names it |
| B2 | A plot is "up to 36 tiles"; the ladder had 64 / 100 / 144 | **done** — a site is 36 tiles, and a new plot starts smaller so screen 16 has something to sell |
| B3 | Ledger entries have a reason but no reference | **done** — `LedgerEntry.Reference`, and every coin movement carries one |
| B4 | Raw colours and spacing in USS | **done** — nine tokens declared; no raw colour outside `tokens.uss` |
| B5 | No `db/schema.sql` | **open** — a decision to confirm: Prisma schema, or `schema.sql` as the source of truth |

## C. Logic layer — **done except C5**

| # | Gap | State |
|---|---|---|
| C1 | The leaderboard score is not the farm rating | **done** — one score, computed in `NetWorth` and served by the api |
| C2 | Ledger reference | **done** — enforced in `WalletState`, and a check asserts every entry is traceable |
| C3 | Plot tile cap | **done** — enforced where land is bought, and refused in plain language at 36 |
| C4 | Trading Post floor and ceiling | **done** — the rules live in the mock api over the catalogue's band |
| C5 | B3TR behind a feature flag | **open** — the wallet mentions the token card but nothing gates it |

## D. UI layer

| # | Gap | Why it matters |
|---|---|---|
| D1 | **Design tokens** | Same as B4: a screen may not carry a raw colour. |
| D2 | **Allow visitors** | A6's switch, on the settings screen. |
| D3 | **The token card is not gated** | C5's flag, read by the wallet screen. |
| D4 | **Trading Post entry points** | The Shop and the Marketplace both need to say where the floor is. |
| D5 | **The visit screen is not read-only** | Golden rule 9: it must not offer an action that changes someone else's world. |

## E. Scene layer

| # | Gap | Why it matters |
|---|---|---|
| E1 | **No 3D world** | `IWorldView` exists and `Field2D` implements it; the 3D implementation does not. Three screens are 3D-adjacent (21, 23, 41). |
| E2 | **Visitor camera** | A read-only world needs a camera that cannot act. |

## F. Repository and process

| # | Gap | Why it matters |
|---|---|---|
| F1 | **No `CHANGELOG.md`** | "Update CHANGELOG.md (done, known issues, next task)." |
| F2 | **Nothing is committed** | "Commit each passing milestone." ~700 files are untracked. |
| F3 | **`/server/actions` is not one file per action, and there is no `/server/jobs`** | The layout names both. |
| F4 | **The client is not under `/client`** | The layout names `/client`. Unity needs its own root, so this is a decision to confirm, not a rename. |
| F5 | **`/docs` does not hold the four design documents** | It holds nine documents derived from them. |

---

## Order of work

1. **Api**: A1, A2, A3, A4, A5/A6 — the contract first, because every layer above it is written against it.
2. **Data**: B1, B2, B3, B4, B5.
3. **Logic**: C1–C5.
4. **UI**: D1–D5.
5. **Scene**: E1, E2.
6. **Process**: F1, F2 (and F3–F5 as decisions).

Each step ends with the headless check, the linters, a build, and a render.
