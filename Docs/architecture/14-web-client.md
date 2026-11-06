# 14 — Marketplace web client: SDKs, data layer, wallet, realtime

> **Status:** in progress (2026-11-06). Builds on 13 §9–10 and the contracts in `chain/contracts`.

## 1. Libraries

| Need | Choice | Why |
|---|---|---|
| Server state, caching, pagination | **@tanstack/react-query 5** (+ devtools in dev) | Cache keyed by query, infinite queries for cursor pages, mutations with invalidation |
| Wallet connection | **@hot-labs/near-connect** (NEAR Connect) | Maintained successor to near-wallet-selector (last release Dec 2025); zero deps; MyNearWallet, Meteor, HOT, Intear, OKX, WalletConnect…; `testnet` support |
| RPC reads, amounts | **near-api-js 7** (`JsonRpcProvider.callFunction`) | View calls (`ft_balance_of`, `get_listing`, `storage_balance_of`) without a signer |
| Response validation | **zod 4** | Same as `contracts/`; API responses are parsed, not trusted |
| Realtime | **partysocket** (`ReconnectingWebSocket`) | Backoff + reconnect for the Fastify WebSocket (13 §5); works with any WS server |
| Accessible primitives | **@radix-ui** Tabs, Popover, Tooltip | Keyboard + ARIA done right; styled with the game kit (no Radix theme) |
| Toasts | **sonner** | Stacking, swipe-to-dismiss on phones; rendered with game styles |

Not now: TanStack Router (the hash router covers today's routes; revisit when filters/sort move into the URL),
a component library (the game UI kit is the design system).

## 2. Layers (`web/marketplace/src`)

```
config.ts            env: network, contract ids, API/WS URLs, RPC; DATA = 'api' | 'mock'
lib/amount.ts        FARM ⇄ yocto (18 decimals, BigInt)
api/schemas.ts       zod wire schemas (prices as yocto strings) → domain types
api/http.ts          fetch wrapper: base URL, JWT, timeout, ApiError
api/httpApi.ts       MarketApi over the read API (13 §9)
api/mockApi.ts       MarketApi over the in-memory mock (latency, cursor pages)
api/queries.ts       query keys + hooks (useListings infinite, useToken, useOwnerTokens, useOffers, useNotes, useBalance)
chain/near.ts        NearConnector singleton + RPC provider + view()
chain/market.ts      contract calls (below)
chain/wallet.tsx     WalletProvider: account, connect, disconnect (NEAR or mock)
actions/useMarket.ts mutations: chain call → invalidate queries → toast
realtime/socket.ts   WS client (or mock simulator) → cache invalidation + notifications
```

`VITE_API_URL` unset ⇒ everything runs on the mock, so the UI works before the backend and contracts are deployed.

## 3. Contract calls (from `chain/contracts`)

| UI action | Transaction(s) |
|---|---|
| Sell (first listing) | if `market.storage_balance_of(me)` < needed: `market.storage_deposit()` (0.01 NEAR per listing), then `items.nft_approve(token_id, market, msg: {"price": "<yocto>"})` with approval storage deposit |
| Change price | `market.update_price(nft_contract_id, token_id, price)` · 1 yocto |
| Take down | `market.delist(nft_contract_id, token_id)` · 1 yocto |
| Buy | `farm.ft_transfer_call(market, price, msg: {"nft_contract_id", "token_id"})` · 1 yocto · 300 TGas |
| Use in game | `items.redeem(token_id)` · 1 yocto — **not for workers** (contract: "Workers are held, not redeemed") |
| Send | `items.nft_transfer(receiver_id, token_id)` · 1 yocto |
| FARM balance | view `farm.ft_balance_of(account_id)` |

Before buying, the UI re-reads `market.get_listing` (13 §9) so a stale price never reaches the wallet.

**Offers are off-chain** for now: no contract supports them. They live in the API (create / cancel / decline);
accepting one re-lists the item at the offer price and notifies the bidder to complete the purchase.
An escrowed offer contract is a later decision (open question).

## 4. States every screen handles

Loading (skeleton cards / panels, never a blank page), error (game panel + Retry), empty (NPC + next step),
pagination (cursor; "Load more" + auto-load when the sentinel scrolls into view), pending wallet
("Confirm in your wallet…"), and signed-out (connect, then continue the action).

## 5. Realtime

Server → client messages: `listing` / `delist` / `sale` / `redeem` (token id), `offer` (token id, to), `note` (to).
Each invalidates the matching query keys; `note` for the signed-in account also raises a toast.
With no `VITE_WS_URL`, a mock simulator produces the same messages every ~45 s.
