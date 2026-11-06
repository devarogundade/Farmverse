# Farmverse web

| Package | Site | Dev |
|---|---|---|
| `landing/` | farmverse.world: home, how it works, FAQ, terms / privacy / FARM disclosure | `npm run dev:landing` (port 5175) |
| `marketplace/` | marketplace.farmverse.world: explore, item page, my items, dressing room | `npm run dev:market` (port 5174) |
| `shared/` | NFT metadata schema, 3D renderer, game UI theme, sprites, fonts, models | — |

```bash
cd web
npm install
npm run dev:market
```

## Data modes (marketplace)

Copy `marketplace/.env.example` to `marketplace/.env.local`.

- **Mock (default):** leave `VITE_API_URL` empty. Everything runs in the browser: demo wallet, fake latency,
  cursor pages, and a simulated live feed (a price change or a new offer every ~45 s).
- **Live:** set `VITE_API_URL` (read API + offers), `VITE_WS_URL` (realtime) and the contract ids. Wallets
  connect through NEAR Connect; sells, buys, delists, sends and redeems are real transactions on
  `VITE_NEAR_NETWORK` (testnet by default).

Layers, libraries and the contract-call table are in `Docs/architecture/14-web-client.md`.

## Assets

- **3D models** (`shared/public/models/**/*.glb`) are exported from the ithappy Asset Store packs, whose licence
  doesn't allow redistribution, so they are **not in git**. Rebuild them with Blender 5.x installed:
  `npm run models` (runs `tools/blender/export_marketplace_models.py`). `character.parts.json` is committed.
- **UI kit** (`shared/public/ui`, `shared/public/fonts`, `shared/src/tokens.css`) comes from the game's own
  `Assets/Farmverse/Content/UI/Game/Resources/GameUi`. Re-sync after the game's UI changes:
  `python ../tools/web/sync_game_ui.py`.

## How an NFT becomes 3D

`shared/src/nft/metadata.ts` defines the metadata (`type`, `item_id`, `outfit`, `quantity`, …).
`shared/src/three/NftStage.tsx` renders it: a worker shows the body plus exactly the outfit parts named
in `outfit` (one rig, every part a skinned mesh in `character.glb`); anything else loads
`models/items/<item_id>.glb`, piled up by `quantity` for goods. All stages share one WebGL canvas.
List cards stay still and animate on hover.

## Themes

The four game themes (Orchard default, Meadow, Harvest, Mix) are `[data-theme]` blocks in `tokens.css`,
generated with the same tint rule as `GameTheme.Tint` in Unity. The marketplace's settings button switches them.
