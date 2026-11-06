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
