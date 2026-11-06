# 15 — One game, one market: integration, Farm Pass for FARM, scarcity, workers

> **Status:** decided (2026-11-06): every open question is answered in §9. §10 lists the gaps outside the
> four areas below, §11 is the launch checklist. Builds on 13 (web + NEAR). Hackathon closes **2026-11-14 23:59 UTC**.
> **Scope:** close the gaps between the Unity game and the marketplace, sell the Farm Pass (and other
> studio items) for FARM, decide what is scarce and why, make mint and redeem easy in the game, and
> define how farm workers live on chain and get traded.

## 1. Where we are

| Area | Today | Gap |
|---|---|---|
| Mint in game | Storage → good → "Mint N as NFT"; building panel → "Mint NFT". Toasts only (`GameUi.Chain.cs`) | No progress view, no token shown, no "sell it" link, boosts can't be minted from the UI, strings not in `en.json` |
| Redeem in game | `ChainService.Redeem` exists | **No UI calls it.** The game has no list of the player's NFTs. Redeem only happens on the marketplace (being built there now: `web/marketplace/src/api/game.ts`) |
| Links | Settings → "Market" opens the marketplace home page | No deep links either way; the game ignores URL params except `invite` |
| Balances | Coins and gems | No FARM, no NEAR, no owned NFTs in the game |
| Farm Pass | Season track works; premium shows "Coming soon"; server grants premium when a `farm_pass` pack NFT is redeemed | **Nothing mints or sells pass NFTs.** Redeeming one while premium is already on wastes it |
| Builder's Pass, gem packs, starter pack | Entitlements exist on the server | Builder count never reaches the client; no packs exist on chain; marketplace `NFT_TYPES` has no `pack` |
| Studio items | `items.studio_mint` (minter = studio account) | No tool or script mints anything; the market only does player listings (one token, one price) |
| Scarcity | Mint caps: 5,000 coins of value per day, 24 h account age, 30 min vouchers | No mint fee (no sink), no lot minimum, no caps on studio items, no rule for land supply |
| Workers | Generic villagers come with a workers' house (`IslandEconomy.Work.cs`); contract has `kind: worker` (held, never redeemed); the marketplace dressing room previews outfits | No worker is minted, the server never reads who holds one, the island has no named workers |
| FARM supply | 1 B testnet FARM in the treasury; `fund-tester.sh` hands it out | Players have no way to get FARM except selling to each other |

## 2. Rules for the whole plan

1. **The game is where you play, the market is where you trade.** Anything you *own* shows in both. Anything
   you *use* (redeem, place, equip) can be done from the game without visiting the market.
2. **One wallet approval per action the player thinks of as one action.** "Buy the Farm Pass" = one
   approval, not "buy, then redeem".
3. **Scarcity is enforced on chain where it is promised.** If the page says "1,000 made", the contract
   refuses the 1,001st.
4. Chain words in the game stay "Mint", "Redeem", "FARM", "Market". No hashes, no account jargon
   (web-design preferences apply to the game too).

## 3. Seamless game ⇄ market

### 3.1 A "My NFTs" tab in the game (Storage → NFTs)

- Lists the player's tokens from the existing read API `GET /v1/market/owners/{account}/tokens`,
  with the same item icons the storage uses, quantity, and "Listed for 120 FARM" if it is on sale.
- Per token: **Redeem** (goods, boosts, buildings, land, packs), **Sell on market** (deep link),
  **View** (deep link). Workers show **Working on your farm** instead of Redeem (§7).
- Redeem uses `ChainService.Redeem` (already written). After the server credits it, the island reloads
  as it does today; buildings and land go straight into place mode instead of the "NFT buildings to place"
  list.
- Listed tokens can't be redeemed until delisted (the contract allows it, but the listing would go stale);
  show "Take it off the market first" with a link.

### 3.2 Deep links both ways

| From | Link | Does |
|---|---|---|
| Game → market | `/token/{tokenId}` | After a mint ("See it on the market"), from My NFTs |
| Game → market | `/token/{tokenId}?sell=1` | Opens the list flow |
| Game → market | `/explore?item=wheat&sort=unit_price` | "Buy more" from a good's detail in Storage |
| Game → market | `/shop` | Studio drops (§5) |
| Market → game | `play.farmverse.world/?open=nfts` | "Use in game" on a token the player owns |
| Market → game | `play.farmverse.world/?redeemed={tokenId}` | After redeeming on the market: the game shows "It's on your farm" and opens place mode for buildings |

The game reads these in `GameRoot` next to the invite handling, and every outbound link goes through
`FvOpenTab` (also fix the coupon and news links that still use `Application.OpenURL`).

### 3.3 Balances in the HUD

- FARM balance next to coins and gems (tap → "Get FARM" sheet: sell on the market, the testnet faucet).
  Read it in the page with a `ft_balance_of` view call through `farmverse-page.js` (no server load), refreshed
  after every chain action and on the market live socket.
- NEAR balance only when it is too low to mint (< 0.06 NEAR): "You need a little NEAR to mint".

### 3.4 Live news across both

The game subscribes to `/v1/market/live` for the player's own account and turns events into toasts and
Inbox entries: "Your Wheat ×100 sold for 40 FARM", "You received a Farmhand". The notifications endpoint
(`accounts/{account}/notifications`) already exists for the market bell.

### 3.5 Nothing lost between them

- Both clients keep the redeem tx hash until the server credits it (the market already does).
- Server backstop: **add** a repeatable `redeem-backstop` job (every minute). It credits any `item_redeem` in
  `chain.events` that has no `redemptions/{tokenId}` row, mapping the owner account to its uid. It doesn't exist
  yet (the queues are island-wake, push-task, news-fanout, cron, mint-expiry). It closes the "closed the tab" gap
  for both clients at once.
- A "Finish minting" entry for vouchers issued in the last 30 minutes (`chain-pendingMints` call), so a
  player who closed the wallet popup can retry instead of waiting for the refund.

## 4. Mint and redeem UX in the game

### 4.1 One mint sheet for everything

Opened from: Storage good detail, boosts (API already supports them), selected building, and a small
"Mint" badge on anything mintable.

```
┌─────────────────────────────────────┐
│   [item card, same art as market]   │
│   Wheat                              │
│   [ 10 ] [ 50 ] [ 100 ] [ All 340 ]  │  ← quick chips + −/+ stepper
│   Worth about 120 coins              │
│   Today: ████████░░  3,200 / 5,000   │  ← daily allowance
│   Fee: 12 coins · Network: ~0.01 NEAR│
│            [  Mint  ]                │
└─────────────────────────────────────┘
```

Then one progress view with four steps that tick off: **Packing your goods → Approve in your wallet →
Minting → Done**. Done shows the card with **Sell on market** (deep link with `?sell=1`) and **Keep**.
Errors stay inside the sheet with one plain sentence and a button (Try again / Finish minting / Close),
using the existing `ChainError` mapping. Declining in the wallet says "Not minted. Your goods are back on
your farm" once the refund job lands (or immediately, if we add a `chain-cancelMint` that refunds a voucher
the client gives up on before expiry — safe only after the voucher is invalidated, so keep the job).

### 4.2 Redeem

My NFTs → Redeem → small confirm ("Wheat ×100 goes to your farmhouse. The NFT is used up.") → wallet →
fly-to-storage animation. Buildings and land open place mode. Packs show what they gave ("Premium Farm
Pass is on!").

Later: **Redeem all** with one approval (`signAndSendTransactions`, several `redeem` actions in one transaction).

### 4.3 Housekeeping

- Move every mint, NFT and wallet string into `en.json`.
- Remove stale text: VeChain/B3TR coupon steps, "wallet sign-in is coming soon".
- Show the item's token id nowhere; show the edition ("#312 of 1,000") where there is one.

## 5. Farm Pass (and other studio items) for FARM

### 5.1 Contract: drops in the items contract

Add **drops** (fixed-price primary sales with an on-chain cap) to `items`, which becomes a FARM receiver:

```rust
struct Drop {
    id: String,                 // "farm_pass_s1"
    kind: ItemKind,             // pack | land | tool | building …
    item_id: String, quantity: u64, props: String, title: String, media: String,
    price: U128,                // FARM units
    cap: Option<u64>,           // None = open edition
    sold: u64,
    per_wallet: Option<u32>,
    starts_at_ms: u64, ends_at_ms: Option<u64>,
    paused: bool,
}
```

- **Buy:** `farm.ft_transfer_call(items, price, msg: {"drop":"farm_pass_s1","use":false})`. In
  `ft_on_transfer` (only from FARM): check window, cap, per-wallet limit, `amount == price`; mint
  `d-{drop}-{n}` to the buyer with `props.edition = n`; send the FARM to the treasury; emit `drop_sale`.
  Any failure panics, and the FT standard returns the FARM.
- **Buy and use (one approval):** `"use": true` mints and immediately burns in the same receipt, emitting
  `item_mint` and `item_redeem`. The game then calls `chain-redeem` with the tx hash, exactly like a normal
  redeem. This is the in-game "Get Premium" button.
- **Admin (owner or minter):** `create_drop`, `update_drop` (price, window, pause; cap can only go down),
  views `get_drop`, `get_drops`, `drop_sold_by(account, drop)`.
- Needs: register `items` with FARM (`storage_deposit`) in the deploy script; a versioned state with a
  `migrate` entry (the items contract has none yet — add it now, mainnet needs it anyway); sandbox tests for
  wrong token, wrong amount, sold out, closed window, per-wallet limit, use=true.
- `ft_on_transfer` carries no NEAR, so the items contract pays a drop token's storage from its own balance and
  the buyer needs no deposit. Keep a NEAR float on the items account (≈ 0.01 NEAR per token; the deploy script
  tops it up) and refuse drop sales when the float runs low, so the contract can never eat into locked storage.
- Indexer: parse `drop_sale`; `chain.drops` projection for sold/cap counts. `parse.ts` reads the token's
  contents from the `mint_with_voucher` / `studio_mint` call args; a drop mint happens inside `ft_on_transfer`
  instead, so take the contents from the `item_mint` event (it carries the full item).
- Server `chain-redeem`: with `use: true` the transaction goes to the FARM contract, not to `items.redeem`.
  The check must accept a successful tx **signed by the player** whose receipts include `item_redeem` from the
  items contract with `owner_id` = the player, wherever in the receipt tree it is.

Why the items contract and not the market: the mint is synchronous (no cross-contract call, no refund
callback), and the market stays a pure peer-to-peer book. Unique items (each worker different) don't fit
drops; they are pre-minted and listed (§7.2).

### 5.2 Server

- **Fix the wasted pass:** if premium is already on for the current season, bank it
  (`state/purchases.passCredits += 1`) and switch premium on automatically when the player joins the next
  season. A pass NFT is then "one season of premium", whenever it is used. (The burn already happened, so
  refusing is not an option.)
- Builder's Pass: put `extraBuilders` into the island sync so the client's `ExtraBuilders` is real.
- Starter pack: sold as a one-per-wallet drop and shown in the shop for its 48 h window (the shop never shows it
  today).
- Drops are created by a script (`chain/scripts/drops.sh`, near CLI, signed by the studio account). The admin web
  gets a read-only **Drops** card on the dashboard (sold/cap, FARM taken).

### 5.3 Game

- Pass screen: **Get Premium · 250 FARM** (replaces "Coming soon"). One approval (`use: true`), then the
  premium tiers unlock with the existing claim flow. If the player owns a pass NFT, show **Use your Farm Pass**
  instead (redeem).
- Shop: the hero card becomes the current season's pass with "812 of 2,000 left" when capped.
- Not enough FARM → "Get FARM" sheet (§3.3).

### 5.4 Marketplace

- A **Shop** page for drops: Farm Pass, Builder's Pass, starter pack, gem packs, and a link to the Genesis
  Farmhands (§7). Cards
  show price, sold/cap, time left; one Buy button; the bought NFT appears in My items with Redeem and Sell.
- Add `pack` to `NFT_TYPES` and its card art (pass, builder, gems).
- Secondary sales of passes are normal listings: the studio earns the 2.5 % royalty on every resale.

## 6. Scarcity and the market

### 6.1 Where things come from and where they go

| Thing | Comes from | Leaves through | Scarce because |
|---|---|---|---|
| Goods (wheat, eggs …) | Play, then mint | Redeem (burn), use in game | Daily mint value cap, mint fee, play time |
| Boosts | Play/shop, then mint | Redeem | Same caps |
| Buildings, tools | Player picks up and mints | Redeem | One per token, must be built first (coins + time) |
| Land (fields) | Player mints a field | Redeem | See 6.3 |
| Farm Pass, Builder's Pass, gem packs | Drops | Redeem | Per-season cap on chain |
| Farmhands | Studio, fixed count per cohort | Never burned | Fixed forever (Genesis: 500) |
| FARM | Treasury (fixed supply) | Drop sales go back to the treasury; market fees | Fixed supply, no mint |

### 6.2 Controls to add

- **Mint fee in coins** (e.g. 2 % of item value, min 5 coins): burns coins, so minting is a sink, not free.
- **Lot minimum** per good (e.g. 10) so the market isn't flooded with ×1 listings.
- **Catalogue `mintable` flag** per item; starter and quest buildings `false`.
- **Daily cap** stays (admin-tunable, already built); add a per-item daily cap for the most farmable goods
  if prices collapse.
- **Golden crops** (§12.4): a small chance of a golden harvest that is worth far more and is mintable, so it
  becomes a collectible with real scarcity from play.

### 6.3 Land

Today a field can be picked up and minted as `land`, so player land supply grows with play. **Decided:**
that stays as it is for launch. A field costs coins, a level and build time, and the daily mint cap bounds it, so
it is a traded good, not a scarce one. A fixed set of Genesis expansion plots (extra grid space) is sold as
Genesis land deeds (§12.3).

### 6.4 Seasons

Each season (30 days, already in the catalogue) gets: a capped Farm Pass drop, maybe a themed cosmetic
or golden good. Last season's pass NFTs stay tradeable and still work (§5.2 bank), but the cap for new ones
resets — older editions become collectibles.

### 6.5 Market features that make scarcity visible

- Per item: **floor price**, **price per unit** sort for goods, supply minted / burned / listed.
- Edition number on cards; "sold out" drops linking to their secondary listings.
- "Buy cheapest" from the game's storage detail (deep link sorted by unit price).
- Offers on tokens and on whole items, settled on chain (§12.2).

### 6.6 Getting FARM

- Testnet: a **faucet** on the market and in the game's "Get FARM" sheet: 100 test FARM once a day per
  signed-in account, plus 1 test NEAR the first time (enough gas and storage for about 15 mints). Paid by a
  separate `faucet.` account holding a small float (never the treasury key), rate-limited per account and IP in
  Redis. Mainnet has no faucet.
- Mainnet (after): FARM liquidity on a DEX, and weekly leaderboard prizes in FARM paid by a rewards
  contract the player claims with a server-signed voucher (same pattern as mint vouchers; the server
  still holds no spending key).

## 7. Farm workers and worker trading

### 7.1 Two kinds of worker

- **Villagers** (as today): come with a workers' house, unnamed, not NFTs. Nothing changes.
- **Farmhands** (new, NFTs, `kind: worker`): named characters with an outfit (the dressing room's parts),
  a **specialty** (fields, animals, machines, freight), **traits** (speed, carry, stamina) and a
  **rarity**. Held, never redeemed (the contract already refuses).

Token props: `{ name, outfit{…}, specialty, rarity, traits{speed, carry, stamina}, edition, cohort }`.

### 7.2 Supply

- **Genesis cohort:** 500 ever, each pre-generated (outfit + traits by rarity table) by a seeded script so the
  set is reproducible, `studio_mint`ed and **listed on the market at fixed prices by rarity**. Testnet launch
  mints and lists the first 100 (each needs a mint and a listing transaction plus ≈ 0.02 NEAR of storage).
  Rarity: common 60 % (40 FARM), rare 28 % (120), epic 10 % (350), legendary 2 % (1,000).
  Traits: speed, carry, stamina 1–5, rolled higher with rarity; names from a fixed list.
  Outfit part names must be the mesh names in the game's `CharacterParts` asset (the dressing room uses the same
  pack); the script checks every part against that list. Players pick the one they like on the
  Explore page — uniqueness is the point, so this uses listings, not drops. No contract change.
- Later cohorts per season, smaller. No breeding, no in-game recruiting of NFT workers: supply only grows
  when the studio says so.

### 7.3 Using a Farmhand (hold-to-use)

1. On sign-in and every `island-sync`, the server reads the player's workers from `chain.tokens`
   (owner = account, kind = worker; cached in Redis, refreshed by indexer events) and writes
   `crew: [{tokenId, specialty, traits, level}]` into the island it returns.
2. The engine (shared C# domain, `IslandEconomy.Work.cs`) treats each Farmhand as a worker who lives in a
   workers' house bed, eats the same food, and gets trait bonuses on jobs of their specialty (e.g. +1 carry,
   −20 % rest). **Slots:** one Farmhand per workers' house level, so whales can't stack 50.
3. Unity spawns them with their outfit and a name tag; the Workers panel gets a **Farmhands** section with
   their stats and "Hire on market" (deep link to Explore filtered to workers).
4. Sold or sent away → gone from `crew` on the next sync; the game says "Mia left your farm". Listed
   but unsold → keeps working.

Outfits at runtime: the game already dresses people from `CharacterParts` (Presentation/World), the same
asset the `look_*` outfits use, so a Farmhand is the base model plus its six to ten part meshes. The rule change
lives in the shared C# domain, so the server's island engine is rebuilt and `rulesVersion` goes up.

### 7.4 Levels travel with the worker

Loads a Farmhand carries earn XP, stored by the server per **token**, not per player
(`game.worker_stats(token_id, xp, level)`). 1 XP per load; level n needs 100 × n XP; max level 10; each level
adds 5 % work speed. XP is counted from the server's island runs (offline work and sync summaries), never from a
client claim. The buyer gets the level. The market shows level and XP from
the game API on the worker's card and page. Later, `set_worker_state` vouchers (planned in 13 §6.2)
write checkpoints on chain so the level doesn't depend on our server.

### 7.5 Trading

- Normal listings (list, buy, delist) with the 5 % fee and 2.5 % royalty — already built.
- Market filters: specialty, rarity, level; sort by level and price.
- Worker page: 3D model (NftStage already renders outfits), traits, level, "works on {farm name}".

### 7.6 Renting (the "agents with wallets" story; full design in §12.1)

A rental escrow in the market contract: the owner deposits a Farmhand (`nft_transfer_call` with
`{rent: price_per_day, max_days}`), a renter pays FARM (`ft_transfer_call {rent, days}`), the worker
works on the renter's island until `rented_until`, and the FARM sits in the **worker's own balance** in the
contract until the owner withdraws. The server reads `rental_of(token)` alongside ownership. The worker
earns, holds and pays out FARM by itself; that is the agent story for the `agents` track. An LLM
personality for chat is a cosmetic extra.

## 8. Order of work (Nov 7 – 14) — superseded by [15-checklist.md](15-checklist.md)

Four tracks run side by side. The marketplace track is shared with the session already building
marketplace redeem; its uncommitted changes land first.

| Day | Unity | Chain | Server | Web (market, landing, admin) |
|---|---|---|---|---|
| Nov 7 | My NFTs tab + Redeem, deep links in/out, chain strings to `en.json`, stale VeChain/coupon text out | Drops + `migrate` + unit and sandbox tests | Pass bank, `redeem-backstop` job, mint fee + lot minimum, builders in sync | `pack` type + art, deep-link targets (`?sell=1`, `/shop`, "Use in game") |
| Nov 8 | Mint sheet with progress view, "Finish minting" | Upgrade testnet items in place, register with FARM, NEAR float, `faucet.` account | `drop_sale` + drop-mint parsing, `chain-redeem` for buy-and-use, `chain-pendingMints`, faucet route | Shop page (drops), faucet button |
| Nov 9 | Pass "Get Premium" (buy-and-use), starter pack + Builder's Pass in shop, FARM in the top bar, "Get FARM" sheet | `drops.sh`: S1 pass, Builder's Pass, starter pack, gem packs | Static hosting for play., marketplace. and farmverse.world on the EC2 nginx | Landing: FAQ for FARM, mint, redeem, passes, Farmhands |
| Nov 10 | Farmhands on the island + Farmhands section in Workers | `farmhands.mjs`: generate 500, mint + list the first 100 | `crew` from `chain.tokens`, engine bonuses + slots, `rulesVersion` up | Worker filters, rarity and traits on cards |
| Nov 11 | Live toasts + Inbox from the market socket | Reconcile numbers checked by hand | `worker_stats` XP, nightly `chain-reconcile` | Worker level on pages, admin Drops card |
| Nov 12 | **Full testnet loop on the deployed sites** (§11), then fix | | | |
| Nov 13 | Fixes, demo video, README for judges | | | |
| Nov 14 | Submit (deadline 23:59 UTC) | | | |

**Cut first if late,** in this order: live toasts, worker XP (ship fixed traits), "Finish minting" (the refund job
already covers it), admin Drops card, Redeem all, golden goods (already after). **Never cut:** My NFTs + Redeem in
game, mint sheet, pass for FARM in one approval, Shop page, Farmhands on the island and on the market, the faucet,
the backstop job, hosting.

## 9. Decisions (made 2026-11-06)

| # | Question | Decision |
|---|---|---|
| D1 | Network for the submission | **Testnet.** Mainnet waits for an external audit, server-authoritative island actions (13 §7.4) and the multisig owner. The hackathon allows a testnet submission |
| D2 | Farm Pass | 250 FARM. Season 1 "Founders" edition capped at 2,000, max 5 per wallet; later seasons capped at 5,000. A pass NFT is one season of premium whenever it is used (bank, §5.2) |
| D3 | Other drops | Builder's Pass 400 FARM, cap 1,000, max 3 per wallet (the game caps extra builders at 3). Starter pack 30 FARM, open edition, 1 per wallet. Gem packs, open edition: 100 gems 20 FARM, 500 gems 90 FARM, 2,000 gems 320 FARM |
| D4 | Where drops live | In the items contract (`ft_on_transfer`), upgraded in place on testnet with a `migrate` entry, so existing test NFTs and the indexer cursor stay |
| D5 | Land | Fields stay mintable as `land`, as today. Genesis land deeds (§12.3) unlock wild areas: 1,000 ever |
| D6 | Genesis Farmhands | 500 ever; first 100 minted and listed on testnet. Common 60 % at 40 FARM, rare 28 % at 120, epic 10 % at 350, legendary 2 % at 1,000 |
| D7 | Farmhand slots | Sum of workers' house levels, max 6. Extra Farmhands stay in the wallet and show "No bed free" in the Workers panel |
| D8 | Farmhand while listed | Keeps working until sold |
| D9 | Worker levels | Off chain per token (§7.4), shown on the market; on-chain checkpoints after the hackathon |
| D10 | Mint fee and lots | 2 % of item value in coins, min 5; goods mint in lots of at least 10 |
| D11 | Daily mint cap | Stays 5,000 coins of value per day, admin-tunable (already built) |
| D12 | Market fee and royalty | 5 % fee to the treasury + 2.5 % studio royalty (as deployed) |
| D13 | Currencies | FARM only. Offers open, settled on chain in the market contract (§12.2) |
| D14 | Faucet | 100 test FARM a day + 1 test NEAR once, per account, from a separate `faucet.` account |
| D15 | FARM supply (for the FAQ; testnet keeps its 1 B) | 1,000,000,000 fixed: 35 % player rewards, 20 % liquidity, 20 % treasury, 15 % team (2-year vesting), 10 % ecosystem and grants |
| D16 | Redeeming a listed token | The game refuses it ("Take it off the market first") and links to the listing |
| D17 | Drop administration | Scripts signed by the studio account; admin web shows sales read-only |
| D18 | Web hosting | play., marketplace. and farmverse.world as static sites on the same EC2 nginx (Brotli for the WebGL build), added to `DOMAINS` |
| D19 | Renting Farmhands, offers, Genesis land plots, golden crops | **In scope (changed 2026-11-06)**, designed in §12. Mainnet stays after the hackathon |

## 10. Other gaps this plan closes (outside the four areas)

| Gap | Fix | Where |
|---|---|---|
| No `redeem-backstop` job: a redeem whose `chain-redeem` call never arrives is never credited | Add it (§3.5) | Server worker |
| No nightly reconcile (13 §5.3) | `chain-reconcile`: issued − refunded = minted on chain; burned = redeemed; logged, and a number on the admin dashboard | Server worker, admin |
| Only the API is deployed; the game, market and landing have no host | D18; `deploy.sh` gains a `--web` step that builds and uploads the three sites | deployment/ |
| Starter pack and Builder's Pass exist on the server but never reach the player | Drops + shop + builder count in sync (§5) | Server, Unity |
| Indexer reads mint contents from call args; drop mints come from `ft_on_transfer` | Read the `item_mint` event (§5.1) | `chain/parse.ts` |
| Redeem check assumes a direct `items.redeem` call | Walk the receipt tree for `item_redeem` (§5.1) | `mint/` redeem |
| Players arrive with no NEAR for mint gas and storage | Faucet NEAR on testnet; the game warns below 0.06 NEAR. Mainnet: a NEP-366 relayer, after | Server, Unity |
| Chain, NFT and wallet strings hard-coded in C# | `en.json` | Unity |
| Stale VeChain coupons, "wallet sign-in coming soon", links bypassing `FvOpenTab` | Remove and fix | Unity, catalogue coupon types |
| Two Claude sessions share the working tree; `deploy.sh` ships uncommitted files | Commit each track before any deploy; deploy only from a clean tree | Process |

**Known and accepted for the testnet submission (written in the README):** the island is still
device-authoritative with the upload guard (13 §7.4), the contracts are unaudited, the owner is a single account
rather than a multisig, and the voucher signer key sits in the server's env file rather than a KMS. Each one
blocks mainnet, not the testnet submission.

## 11. Launch checklist (Nov 12, on the deployed testnet sites)

Done by hand with fresh testnet wallets; every line passes before submitting.

1. Sign in on play.farmverse.world with a new wallet; claim the faucet; FARM and NEAR show in the top bar.
2. Mint 50 wheat from the mint sheet: the four steps tick, the allowance bar drops, the fee is taken, the token shows
   in My NFTs and on the market within a minute.
3. Close the wallet popup on a second mint: "Finish minting" works; leaving it alone refunds after expiry.
4. Pick up and mint a building; it leaves the island and appears on the market.
5. "Sell on market" from the game opens the list flow; list it for 10 FARM.
6. A second wallet buys it on the market; the seller sees the sale (toast or Inbox) and the FARM.
7. The buyer opens the game from "Use in game" and redeems from My NFTs: wheat lands in storage, a building opens
   place mode.
8. Redeem on the market and close the tab before the credit: the backstop credits it within two minutes.
9. Buy the Farm Pass in the game with one approval: premium tiers unlock and the shop shows how many are left.
   Buy a second pass on the Shop page and redeem it: it's banked for next season.
10. Buy the starter pack, a gem pack and a Builder's Pass: the gems, contents and extra builder arrive.
11. Buy a Genesis Farmhand on the market: it walks on the buyer's island in its outfit with its traits. Sell it and
    it leaves on the next sync. Owning more than the free beds shows "No bed free".
12. A sold-out drop refuses the next buy and returns the FARM in full.
13. Admin web: drop sales show, reconcile numbers match, the audit log has the faucet grants.
14. Repo: README for judges (what it is, links, contract accounts, how to run, the accepted risks above), demo
    video, nothing from the paid packs in the tree. Making the repo public is your step.

## 12. Also in scope: renting, offers, land deeds, golden crops

Added on 2026-11-06 at the user's request. All the contract parts ship in the **same** testnet upgrade as drops,
so items and market are upgraded once. The order of work is the checklist in
[15-checklist.md](15-checklist.md), which replaces the day table in §8.

### 12.1 Renting Farmhands (market contract)

- **Offer for rent:** the owner calls `items.nft_transfer_call(market, token, msg: {"rent":{"price_per_day":"…","max_days":7}})`.
  The market's `nft_on_transfer` (from whitelisted NFT contracts only, workers only) keeps the token in escrow and
  records `rental{token, lender, price_per_day, max_days, renter: None, until: 0, earned: 0}`. While nobody rents
  it, the Farmhand keeps working for the lender.
- **Rent:** the renter calls `farm.ft_transfer_call(market, price_per_day × days, msg: {"rent":{"nft_contract_id","token_id","days"}})`.
  Checks: listed for rent, free (or the last rental ended), `1 ≤ days ≤ max_days`, exact amount, renter ≠ lender.
  The market takes its fee, adds the rest to the **worker's balance** (`earned`), sets `renter` and `until`.
- **The worker's wallet:** `earned` belongs to the token, not to a person. The lender calls `rent_withdraw(token)`
  to move it out (FARM `ft_transfer`; unregistered → `owed`, as for sales). The worker keeps earning from the next
  renter while the lender sleeps: an agent with a wallet.
- **End:** the lender calls `rent_end(token)`: allowed when not rented or `now ≥ until`; pays out `earned` and sends
  the token back. Renters can't take it anywhere: the market owns it.
- **Views:** `get_rental(contract, token)`, `get_rentals(from, limit)`, `rentals_of_lender`, `rentals_of_renter`.
- **Events:** `rent_list`, `rent_start {renter, days, until, paid}`, `rent_withdraw`, `rent_end`.
- **Game server:** a Farmhand works on the **renter's** island while `now < until`, otherwise on the lender's.
  `crew` comes from `chain.tokens` (owned) plus `chain.rentals` (lent but free, or rented to you). Rented
  Farmhands count against the renter's beds; the lender's bed is freed while it is out.
- **Market UI:** "Rent out" on a worker you own; a Rent tab on Explore (price per day, days left); a Rent dialog;
  "Earned 42 FARM · Withdraw" on the worker page. **Game:** rented Farmhands show "Rented · 3 days left".

### 12.2 Offers (market contract, fully on chain)

- **Make an offer on a token:** `farm.ft_transfer_call(market, amount, msg: {"offer":{"nft_contract_id","token_id","expires_at_ms"}})`.
  The FARM stays in the market as escrow; `offer{id, buyer, token or item, amount, expires}`. One live offer per
  buyer per token; a new one replaces the old (the old amount is refunded).
- **Make an offer on an item** ("any Wheat, at least ×100, for 40 FARM"): `msg: {"offer":{"nft_contract_id","item_id","min_quantity","expires_at_ms"}}`.
- **Accept:** the owner calls `items.nft_approve(token, market, msg: {"accept_offer": id})`. In `nft_on_approve` the
  market checks the offer is live and matches the token (for item offers it reads `get_item` first, so accept is a
  two-step promise), then `nft_transfer_payout` to the buyer with the escrowed amount minus fee; on failure the offer
  stays live. A listing of the same token is removed.
- **Cancel:** the buyer calls `offer_cancel(id)` → refund. Expired offers are refunded by `offer_cancel` from anyone
  (permissionless clean-up), so FARM is never stuck.
- **Views and events:** `get_offer`, `offers_for_token`, `offers_for_item`, `offers_of`; `offer_make`,
  `offer_cancel`, `offer_accept`.
- **Server:** the indexer projects `chain.offers`; `/v1/market/tokens/:id/offers` and `/v1/market/offers?item=` read it;
  the 501 write routes are removed (writes are wallet transactions). **Market UI:** the existing offer flows behind
  `VITE_OFFERS` switch to these calls and the flag is removed. **Game:** "You have an offer of 40 FARM for your
  Wheat ×100" in the Inbox with a link.

### 12.3 Genesis land deeds

- The island already has cells that are never buildable (`IslandData.firstChapter = 0`). Mark a few of those areas as
  **wild plots** (`IslandData.wildPlots`: id + cells), on every island the same.
- A **land deed** NFT (`kind: land`, `item_id: "deed"`, studio drop, cap **1,000 ever**, 300 FARM, 2 per wallet)
  is redeemed to clear the next wild plot on your island for good. Redeeming burns it, so the supply only shrinks.
- Server: redeeming a deed adds `wildPlotsOpen: n` to the island (an exchange op `open-plot`), capped at the number
  of wild plots; with all open, the game refuses to redeem it ("All your land is open"; it can still be sold).
- Game: cleared plots lose their trees/rocks and become buildable; a "Wild land" sign with "Use a land deed" on each
  locked plot.

### 12.4 Golden crops

- Each harvest has a **1 %** chance to give the crop's golden form (`golden_wheat`, …), catalogue goods with 25× the
  sell price, `mintable: true`, never sown.
- The roll must give the same answer on the device and on the server's engine, so it is not `System.Random`: it is
  `hash(island seed, field uid, harvest count) < 1 %`.
- Golden goods show with a gold frame in storage and on the market; minting them works like any good (lot minimum
  1 for golden goods).

## 13. The `games` track brief (added 2026-11-06)

The track ("Games With Real Economies") judges the game loop, with a bonus if players never notice the chain. It
asks for NPCs with wallets, player-owned markets and economies that evolve without a dev patch, and names
NEP-245, the near-dapp skill and NEAR Intents as starting points. Its sample idea: *merchant NPCs that are agents
with wallets and set prices from their own stock and demand.*

| Brief asks | This plan | Decision |
|---|---|---|
| Player-owned markets | Listings, offers (§12.2), drops (§5) | Done in the contracts |
| NPCs with wallets | Farmhands earn FARM into their own balance while rented (§12.1) | Keep, **and add merchant NPCs (§13.1)** |
| Economy evolves without a patch | Fixed prices except player listings | **Merchants reprice themselves (§13.1)** |
| Players never notice the chain | One approval per action, "Mint"/"Redeem" only | Keep; add social-login wallets where the network has them (§13.2) |
| NEAR Intents | — | The verifier contract (`intents.near`) has **no testnet deployment** (its docs: "There is no testnet deployment. Use small amounts for testing purposes."). So: **Buy FARM with any token** (§13.3) is built behind a mainnet switch; on testnet the sheet shows live 1Click quotes only |
| NEP-245 multi token | Items are NEP-171 with a quantity | Not in this cut: moving goods to NEP-245 means a new contract and re-minting every good, the market and the indexer. Written in the README as the next step for fungible goods |

### 13.1 Market Town merchants (agent NPCs)

- Four merchants, each its own NEAR account with its own FARM (`bea.merchants.<master>` …): **Bea the baker**
  (wheat, corn, flour, bread), **Ruth the rancher** (eggs, milk, wool), **Tomas the trader** (any good, small
  margins), **Ivy the collector** (golden goods and buildings only).
- An **agent loop** in the worker (`merchant-tick`, every 10 minutes) runs each merchant: read its own stock
  (tokens it owns, from `chain.tokens`) and demand (its fills and the market's sales of each item in the last 24 h,
  from `chain.sales` / offer accepts), then
  - **buy price** per unit = `base × (target / max(stock, 1))^0.5 × demand factor`, clamped to 0.5–2× base;
  - **sell price** = buy price × 1.25;
  - post or replace an **item offer** (buy side, escrowed FARM, §12.2) and **list** what it holds (sell side).
- The merchant's FARM is its own: it can only spend what it earned from sales plus a starting float from the
  treasury. Its key is a hot key on the server, but a merchant can only move its **own** tokens and FARM, never a
  player's (principle 3 stays true). Each tick writes `merchant_log` (what it saw, what it set) for the admin web.
- **Game:** the market stall shows the merchants' offers: "Bea buys Wheat ×100 for 38 FARM". **Sell to Bea** mints the
  lot and accepts her offer — two approvals the first time (mint, then accept), shown as one step.
- **Market site:** a Merchants page with each merchant's stock, prices over time and "why" (low stock, high demand).
- Optional, cosmetic: a line of chat per merchant from NEAR AI Cloud, with the prices still set by the rule.

### 13.3 Buy FARM with any token (NEAR Intents, mainnet)

FARM has no DEX liquidity at launch, so solvers can't route *to* FARM. Instead the studio sells FARM at a fixed
rate and takes payment through intents:

1. The game's "Get FARM" sheet asks the server for a 1Click quote: any supported token on any chain → NEAR,
   recipient = the treasury, with the player's account in the quote's reference.
2. The player sends to the quote's deposit address from whatever wallet holds the token.
3. The server polls 1Click's status for that deposit address; on `SUCCESS` it sends FARM at the fixed rate from a
   `sales.` account (its own float, never the treasury key), once per deposit address.
4. Refunds are 1Click's: a failed swap returns the token to the player's refund address.

Without a mainnet deploy the sheet shows the quote (1Click's `dry` quotes need no funds) and says buying opens
at launch.

### 13.2 Social login

**Decided (2026-11-06): a Privy wallet in our own near-connect manifest.** `near-connect` (already on the game page
and the market) accepts a dApp manifest (`new NearConnector({ manifest: '/manifest.json' })`). The
`@peerfolio/privy-near-connect` package (repo `beneviolabs/privy-near-connect`) builds a NEAR wallet on Privy's
embedded wallets with email / phone / Google login; its manifest declares `signMessage`, `signAndSendTransaction` and
`testnet`, so the NEP-413 sign-in and every flow stay as they are.

- **Needs from the team:** a Privy app (App ID + Client ID). Signing up is the team's step.
- **We build:** a hosted `/sign` page (`initSigningPage`, `allowedOrigins` = our sites, `debug: false`), a
  `manifest.json` with the Privy wallet first ("Continue with email or Google"), the executor URL pinned to a commit.
- **Server:** a Privy wallet is likely an implicit account (64-hex id from its key) that doesn't exist on chain until
  it holds NEAR, so the NEP-413 check can't find its key with `view_access_key`. Accept an implicit account when its id
  is the hex of the signing key; the faucet then gives it its first test NEAR.

**Considered:** NEAR Auth / FastAuth (`@fast-auth-near/*`, Auth0 + MPC keys): signs transactions but not NEP-413
messages, needs approved Auth0 credentials on mainnet and has no documented testnet setup: the mainnet option, not the
hackathon one. A studio-held key per player would break principle 3 (the studio never moves player assets).

## 14. Sponsored fees: players don't need NEAR (added 2026-11-06)

The user asked that players never need NEAR for gas. What is built:

- **Mint and redeem without gas or popups.** The game page keeps a *session key* per account in the browser. The first
  time, the wallet adds it once ("Allow Farmverse to mint and redeem for you"): a function-call key that can only call
  `mint_with_voucher` and `redeem` on the items contract. Then every mint and redeem is a NEP-366 delegate action
  signed by that key and sent to `chain-relay`; the server checks it is exactly one deposit-free call of those methods
  on our items contract for the signed-in account (expiring within 600 blocks, 30 a minute per account) and sends it
  from the relayer account, which pays the gas. Anything else falls back to the wallet.
- **No deposits for those calls.** `mint_with_voucher` with nothing attached is paid from the items contract's own
  NEAR (only server vouchers can mint, under the daily cap); `redeem` no longer needs 1 yoctoNEAR. Storage the contract
  paid (drops, sponsored mints) is never refunded on redeem, so free mints can't be turned into NEAR.
- **Welcome top-up.** At its first sign-in an account holding less than 0.02 NEAR gets 0.05 NEAR once from the relayer,
  enough for the one-time key approval and for FARM payments.
- **What can't be sponsored:** FARM payments (`ft_transfer_call`: drops, market buys, offers, rent) need 1 yoctoNEAR by
  the token standard and FARM can't change, so they stay wallet transactions paid from the welcome NEAR. Every NEAR
  account must hold a little NEAR for its own storage.
- **Relayer key:** `RELAYER_ACCOUNT` / `RELAYER_PRIVATE_KEY` (testnet: the faucet account if unset). It only wraps
  calls the player's own key signed and sends welcome NEAR; it can't move anyone's tokens or FARM.
- **Implicit accounts** (social-login wallets) can sign in before they exist on chain: their id is the hex of the key.
