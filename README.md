# Farmverse

A cosy farming and trading game where **the economy is the gameplay**. You grow, raise and build on your island in
Unity (WebGL); anything you make can become an NFT on NEAR, be traded for **FARM** on the marketplace, and be
redeemed back into anyone's island. Merchant NPCs with their own wallets set prices from their stock and demand,
Farmhand workers are NFTs that work on whoever's farm holds or rents them, and players never need NEAR for gas.

**NEAR HACKATHON — track `games` (Games With Real Economies).** Submitted on **NEAR testnet**.

| | |
|---|---|
| Play | https://play.farmverse.world |
| Marketplace | https://marketplace.farmverse.world (Shop, Hire, Market Town, Get FARM with any token) |
| Site | https://farmverse.world |
| API | https://api.farmverse.world |
| Design | [Docs/architecture/15-game-market-economy-plan.md](Docs/architecture/15-game-market-economy-plan.md) · [checklist](Docs/architecture/15-checklist.md) · [13 (web + NEAR)](Docs/architecture/13-near-web3-plan.md) |

## What a judge can try

1. **Play** on play.farmverse.world and sign in with a NEAR testnet wallet (or *Email or Google* once switched on).
   New accounts get a little NEAR, and **Get FARM** gives 100 test FARM a day.
2. **Mint**: Storage → a good → *Mint as NFT*. Choose an amount, see your daily allowance and the coin fee; the
   steps tick off. Mints and redeems are **sponsored**: one approval the first time, then no popup and no gas.
3. **Sell**: *Sell on market* opens the listing; or *Sell to Bea* sells a lot to a merchant NPC at her price.
4. **Redeem**: Storage → NFTs → *Redeem*: goods land in the farmhouse, buildings in place mode, packs open.
5. **Farm Pass**: the pass screen's *Get Premium · 250 FARM* buys and uses it in one approval (on-chain cap).
6. **Farmhands**: hire one on the marketplace (*Hire*), or buy a Genesis one; it walks your farm in its outfit,
   works with its skills and levels up. Rent yours out: the FARM collects in the worker's own balance.
7. **Market Town**: see what Bea, Ruth, Tomas and Ivy hold, what they pay and why their prices moved.

## How it fits the track

| Brief | Farmverse |
|---|---|
| NPCs with wallets | Market Town merchants (own accounts, own FARM, an agent loop every 10 minutes: stock + demand → prices → offers and listings, with a public log of why). Farmhands earn FARM into their own balance while rented |
| Player-owned markets | Listings, on-chain offers (on a token or on any token of an item), rentals, studio drops with on-chain caps |
| Economy that evolves without a patch | Merchants reprice from stock and demand; scarcity from capped drops, fixed Genesis sets, a coin fee on minting, burns on redeem, golden crops (1 %) |
| Players never notice the chain | "Mint", "Redeem" and "FARM" are the only chain words; sponsored mints and redeems (session key + NEP-366 relayer); welcome NEAR; social login wallet |
| NEAR Intents | *Get FARM with any token* (1Click): live quotes now; orders go live on mainnet (the intents contract has no testnet deployment) |

## Testnet contracts

Written by `chain/scripts/deploy-testnet.sh` to [chain/deploy/testnet.env](chain/deploy/testnet.env): FARM
(NEP-141), items (NEP-171 + drops), market (listings, offers, rentals). Sources and tests in [chain/](chain/README.md).

## Repository

| Path | What |
|---|---|
| `Assets/Farmverse` | the game client (see its README) |
| `contracts/` | the single source of the client/server contract (zod → generated C#), the economy and conformance vectors |
| `backend/game` | the game rules, shared by the server and its tests |
| `backend/server` | the game server: API, WebSocket, jobs (backstop, reconcile, merchants, intents), chain indexer, relayer, faucet |
| `chain/` | the NEAR contracts (items, market, farm token), sandbox tests and testnet scripts |
| `web/` | the marketplace, admin and landing sites |
| `deployment/` | the EC2 setup and deploy script |
| `tools/` | `codegen`, `dotnet` (headless C# tests, island engine), `art` |
| `Docs/architecture` | the design |

```bash
npm ci && npm test                                   # contracts, game rules, server
dotnet build tools/dotnet/Farmverse.TestHost -c Release && dotnet tools/dotnet/Farmverse.TestHost/bin/Release/net8.0/Farmverse.TestHost.dll
cd chain && cargo test && scripts/build.sh && cargo test -p farmverse-chain-tests   # Linux/WSL: contracts + sandbox
```

In Unity: **Farmverse ▸ Set Up Boot Scene**, then Play. **Farmverse ▸ Web ▸ Build (testnet on farmverse.world)**
makes the WebGL build; `bash deployment/deploy.sh --web` publishes the sites.

Store packs (ithappy and others) and the content built from them are not in git: see
[Assets/Farmverse/README.md](Assets/Farmverse/README.md) for what to import and which editor menus rebuild it.

## Known limits of the testnet submission

These are accepted for testnet and block mainnet (plan 15 §10):

- The island is still device-authoritative, with an upload guard and server-run mint/redeem moments (13 §7.4).
- The contracts are unaudited; their owner is a single account, not a multisig.
- The voucher signer, relayer, faucet and merchant keys sit in the server's env file, not a KMS.
- Social login needs the team's Privy app; NEAR Intents orders need mainnet.

## Next steps

- **NEP-245 for goods**: goods are NEP-171 tokens with a quantity today; a multi-token contract would make stacks of
  wheat fungible and splittable (a new contract and a migration of minted goods).
- **Mainnet**: audit, multisig owner, server-authoritative island actions, then *Get FARM with any token* goes live
  (`INTENTS_LIVE=on`) and NEAR Auth / social wallets on mainnet.
