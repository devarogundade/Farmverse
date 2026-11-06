# 15 — Checklist: game ⇄ market, Farm Pass for FARM, scarcity, Farmhands

Every item of [15-game-market-economy-plan.md](15-game-market-economy-plan.md), in the order it is built.
Contract work comes first so items and market are upgraded on testnet **once**. Tick an item only when its
tests pass (or, for by-hand items, when it was seen working). Section refs (§) point into the plan.

No project reshaping is needed: the monorepo already has a home for every piece (`chain/`, `backend/game`,
`backend/server`, `Assets/Farmverse`, `web/*`, `deployment/`). New files: `chain/scripts/{upgrade-testnet,drops,farmhands}`,
an indexer migration, and the Unity panels.

## A. Contracts (one testnet upgrade)

- [x] A1 items: versioned state + `migrate` (keeps existing tokens) §5.1
- [x] A2 items: drops — `create_drop`, `update_drop`, views, `ft_on_transfer` buy, edition numbers, per-wallet limit, NEAR float guard, FARM to treasury, `drop_sale` event §5.1
- [x] A3 items: buy-and-use (`"use": true` mints and burns in one receipt) §5.1
- [x] A4 market: versioned state + `migrate` §12
- [x] A5 market: offers on tokens and on items — make, replace, accept via `nft_approve`, cancel, expiry refund §12.2
- [x] A6 market: rentals — rent out via `nft_transfer_call`, rent with FARM, worker balance, withdraw, end §12.1
- [x] A7 unit tests for A1–A6 (`cargo test`) and sandbox tests for the cross-contract paths (WSL)
- [x] A8 `chain/scripts/upgrade-testnet.sh`: build, deploy new code to items and market, `migrate`, register items with FARM, top up the items NEAR float, create the `faucet.` account
- [x] A9 `chain/scripts/drops.sh`: S1 Farm Pass, Builder's Pass, starter pack, gem packs ×3, Genesis land deeds (§9 D2, D3, §12.3)
- [x] A10 `chain/scripts/farmhands.mjs`: seeded generator for 500 Genesis Farmhands (outfit parts checked against `CharacterParts`), mint + list the first 100 (§7.2)

## B. Server, indexer, jobs

- [x] B1 Indexer: drop mints from the `item_mint` event, `drop_sale`, offers, rentals → `chain.drops`, `chain.offers`, `chain.rentals` (migration) §5.1 §12
- [x] B2 `chain-redeem` accepts buy-and-use transactions (walks the receipt tree) §5.1
- [x] B3 `redeem-backstop` job (every minute) §3.5
- [x] B4 `chain-reconcile` job (nightly) + admin number §10
- [x] B5 Farm Pass bank: a pass used while premium is on is saved for next season §5.2
- [x] B6 Extra builders reach the island sync §5.2
- [x] B7 Mint fee (2 %, min 5 coins) + lot minimum (10; 1 for golden goods) §6.2
- [x] B8 `chain-pendingMints` ("Finish minting") §3.5
- [x] B9 Faucet route: 100 FARM/day + 1 NEAR once, from `faucet.`, rate-limited §6.6
- [ ] B10 `crew` from owned + rented Farmhands, beds = sum of workers' house levels (max 6) §7.3 §12.1
- [ ] B11 `worker_stats` XP per token from server island runs §7.4
- [x] B12 Market read API: drops, offers, rentals, worker filters (specialty, rarity, level) §5.4 §7.5 §12
- [ ] B13 Land deed redeem → `open-plot` exchange op §12.3
- [ ] B14 Golden crop goods in the catalogue (25× price, mintable) §12.4
- [ ] B15 Notifications: offers received, rentals started/ended, sales §3.4 §12.2

## C. Shared C# rules (device + server engine)

- [ ] C1 Farmhands in the work rules: crew, beds, specialty and trait bonuses, level speed §7.3
- [ ] C2 Golden harvest roll (deterministic hash) §12.4
- [ ] C3 Wild plots: `IslandData.wildPlots`, `open-plot` op, buildability §12.3
- [ ] C4 Engine rebuilt, `rulesVersion` up, engine tests green

## D. Unity game

- [ ] D1 Chain/NFT/wallet strings into `en.json`; remove VeChain coupon steps and "coming soon" sign-in text; all links via `FvOpenTab` §4.3
- [ ] D2 Storage → My NFTs tab: list, Redeem (place mode for buildings/land), Sell, View, listed-token rule §3.1
- [ ] D3 Deep links: outbound (token, sell, explore item, shop) and inbound (`?open=nfts`, `?redeemed=`) §3.2
- [ ] D4 Mint sheet: chips + stepper, allowance bar, fee, four-step progress, Sell/Keep, errors in place, boosts mintable, "Finish minting" §4.1
- [ ] D5 FARM (and low NEAR) in the top bar; "Get FARM" sheet with the faucet §3.3
- [ ] D6 Pass "Get Premium · 250 FARM" (buy-and-use), "Use your Farm Pass"; shop: starter pack, Builder's Pass, gem packs, land deeds with counts left §5.3
- [ ] D7 Farmhands on the island in their outfits; Workers panel Farmhands section, "No bed free", "Rented · n days left", Hire on market §7.3 §12.1
- [ ] D8 Wild plots: locked signs, "Use a land deed", clearing §12.3
- [ ] D9 Golden goods: gold frame in storage, harvest pop §12.4
- [ ] D10 Market live socket → toasts + Inbox (sold, offer, rented) §3.4
- [ ] D11 WebGL build (prod config) passes a smoke run

## E. Websites

- [ ] E1 Marketplace: `pack` and `deed` types with card art; golden frame §5.4
- [ ] E2 Marketplace: deep-link targets (`?sell=1`, `/shop`, explore by item and unit price) and "Use in game" §3.2
- [ ] E3 Marketplace: Shop page (drops: price, sold/cap, time left, Buy) §5.4
- [ ] E4 Marketplace: faucet button §6.6
- [ ] E5 Marketplace: offers live on chain, `VITE_OFFERS` flag removed §12.2
- [ ] E6 Marketplace: rentals — Rent out, Rent tab, Rent dialog, Earned + Withdraw, End §12.1
- [ ] E7 Marketplace: worker filters, rarity/traits/level on cards and pages, floor price and supply per item §6.5 §7.5
- [ ] E8 Landing: FAQ for FARM, mint, redeem, passes, Farmhands, renting, offers, land deeds §9 D15
- [ ] E9 Admin: drops sold card, reconcile numbers §10

## G. Track brief: merchant NPCs and social login (plan §13)

- [ ] G1 Merchant accounts (`<name>.merchants.<master>`), FARM float from the treasury, keys in the server env (script)
- [ ] G2 `merchant-tick` agent loop: stock + demand → prices → item offers and listings; `merchant_log`
- [ ] G3 Game: merchants' offers at the market stall, "Sell to Bea" (mint + accept)
- [ ] G4 Market site: Merchants page (stock, prices over time, why)
- [ ] G5 Social-login wallets: check near-connect on testnet, put them first in the picker
- [ ] G6 README: NEP-245 for goods as the next step; what intents do on mainnet
- [ ] G7 Buy FARM with any token (1Click → NEAR to the treasury → FARM from `sales.`), behind a mainnet switch; live quotes on testnet (§13.3)

## F. Deploy and submit

- [ ] F1 Hosting: play., marketplace., farmverse.world on the EC2 nginx; `deploy.sh --web` §9 D18
- [ ] F2 Run `upgrade-testnet.sh`, `drops.sh`, `farmhands.mjs` on testnet
- [ ] F3 Deploy server, worker, indexer, engine from a clean committed tree
- [ ] F4 Launch checklist §11 by hand, plus: rent a Farmhand and see it work on the renter's farm; make, accept and cancel an offer; redeem a land deed and build on the cleared plot; harvest until a golden crop and mint it
- [ ] F5 README for judges (links, contract accounts, how to run, accepted testnet risks) §10
- [ ] F6 Demo video — **you**
- [ ] F7 Check no paid-pack content in the tree, then make the repo public — **you**
- [ ] F8 Submit before 2026-11-14 23:59 UTC — **you**
