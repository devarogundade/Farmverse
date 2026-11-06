# The world model

> The world is hexagonal lands, each with typed slots, and every object's state is
> derived rather than stored. This is the design; `architecture/13-near-web3-plan.md` says where
> the values come from, and `unity-project-structure.md` says which layer holds what.

**Related:** [`unity-project-structure.md`](unity-project-structure.md) · [`navigation.md`](navigation.md)

---

## 1. The one idea: the hexagon *is* the slot layout

A hexagon has six corners, one centre, six edge midpoints and a perimeter. Those
are the slots:

| Hex feature | Slots | Configuration key |
|---|---|---|
| 6 corners | crops | `land.maximumCrops` |
| centre | machinery | `land.maximumMachinery` |
| 3 far edges | livestock | `land.maximumLivestock` |
| 1 inset vertex | well | `land.maximumWells` |
| the perimeter | fence | `land.maximumFences` |

**The counts are configuration, never code.** Raise `maximumCrops` to 8 and the
seventh and eighth crops spill to an outer ring — the geometry adapts, the layout
code does not change, and no screen knows the number 6.

## 2. The layers

```
server / mock          Data/World/WorldData.cs        the payload: no Unity, no behaviour
        │
        ▼
WorldModel             Logic/World/*.cs               the model: no Unity, no clock
        │              Land, Crop, Livestock, Machine, Well, Fence, HexLayout
        ▼
WorldRenderer          Scene/World3D/*.cs             the drawing: prefabs, hex meshes, camera
        │
        ▼
the world              World_3D.unity                 one additive scene
```

`Data` and `Logic` reference no Unity type, which is why the whole model — every
state machine and every hex anchor — is verified headlessly in milliseconds by
`dotnet run --project Tools/game_test`.

## 3. The payload

```
World
 ├─ Player            role, coins, title, worldTheme
 ├─ Weather, Season   server sky and calendar
 ├─ Configuration     land limits, and the rules per crop / animal / machine,
 │                    plus weather, seasons, recipes, preservation and upkeep
 ├─ Lands[]           one hexagonal site
 │   ├─ Position      axial hex (q, r)
 │   ├─ Terrain, TilledAt, Water, Fertilizer
 │   ├─ Crops[]       type, slot, age, waterConsumed, fertilizerConsumed, quality
 │   ├─ Machinery[]   type, slot, level, fuelConsumed, cycleProgress
 │   ├─ Livestock[]   type, slot, age, feedConsumed, fed, health, pregnancy
 │   ├─ Trees[]       type, slot, age
 │   ├─ Well          level, buckets, capacity
 │   └─ Fence         style, level, durability
 ├─ Contracts[]       buyer, item, amount, reward, expiry
 └─ Decorations[]     scenery: trees, houses, ponds, flowers, rocks
```

Three deliberate choices:

- **A land owns its slots; configuration owns the limits.** The land asks what it
  may hold; it never knows.
- **Everything is a slot occupant with the same shape** — `Id`, `Type`, `Slot`,
  `GetState()` — so the renderer treats them uniformly while each keeps its own
  state machine. Composition, not a subclass tree.
- **Decorations are separate and never simulated.** Scenery must not be able to
  break the economy.

## 4. State is derived, never stored

The server sends `age` and what a crop has consumed; the client derives the stage.
That is what stops the phone, the editor and a headless test disagreeing.

| Object | States |
|---|---|
| Crop | `seeded → germinated → infant → adult → ripe` |
| Livestock | `newborn → young → adult → ready` |
| Machine | `idle → fueling → running → ready` |
| Well | `empty → filling → full` |
| Fence | `intact → worn → broken` |
| Land | `fallow → tilled → planted → full` |

**Age picks the stage; care caps it.** That is the whole "thirst is gentle" rule:

```
progress = age / growSeconds
care     = (waterConsumed/waterRequired + fertilizerConsumed/fertilizerRequired) / 2
```

A crop whose age says *adult* but which has had no water **stalls at infant** and
waits. It never dies and never goes backwards (spec 13, golden rule 5).

Every object answers the same question, so the panel, the tooltip and the renderer
cannot disagree:

```csharp
WorldObjectState state = crop.GetState();
state.Name      // "ripe"      - what the UI shows
state.Progress  // 0..1        - what a bar draws
state.IsReady   // true        - what a button enables
state.NeedsCare // false       - what an attention dot reads
```

## 5. Reusability, from the smallest piece

Every world object is a GameObject with a **`WorldObjectView`**, and the renderer
looks its kind up in the **`WorldPrefabRegistry`** — never a `switch` in the
renderer.

```
"crop_maize" → registry → prefab → WorldObjectView.ApplyState(state)
```

- **One contract.** `ApplyState(name, progress)` is all the model says to the art.
  A crop's four stages and a tree's one stage go through the same door.
- **Composition all the way down.** A pine is a trunk plus three cones; a house is
  walls plus a roof plus a door; a cow is a body plus a head. Each part is itself
  reusable.
- **The registry is the swap point.** It builds from Unity primitives today. When
  the models arrive, the same keys map to prefabs and *nothing above this file
  changes*.

## 6. The renderer draws a diff

`WorldRenderer.Render(model)` walks the model, keeps one instance per thing keyed
by id, updates the ones that are there, and releases the ones that are gone. A
crop that grew is the same GameObject with a new state — which is what makes growth
and feeding cheap enough to watch.

It is also the only place that knows the geometry: the land hands it an anchor
(`land.LocalAnchor(slot, index, size, out x, out z)`) and it places the instance
there. No hex maths above it.

## 7. Where the rules are enforced

| Rule | Where |
|---|---|
| A land holds only what configuration allows | `Land.Capacity` / `CanAdd`, and the test that raises the limit |
| A neglected crop is not punished | `Crop.State`'s care cap, asserted by a check |
| A visitor cannot act | `IWorldView.SetReadOnly` and `CameraRig.SetReadOnly` |
| A tap on the UI is not a tap on the world | `WorldSceneService.IsOverUi`, set by `FarmverseApp` from the panel's own hit test |
| The scene layer never references the UI layer | the `IsOverUi` delegate is the only crossing |

## 8. What is verified, and what is not

**Verified headlessly** (`WorldSelfTest`, 142 checks): capacity from configuration,
a full land refusing one more, all five crop stages on one land, the thirsty crop
stalling, the lowercase state names the UI shows, animals waiting when unfed, the
well/fence/machine states, fallow vs tilled, every occupant answering `GetState()`,
and the hex anchors — the machine at the centre, crops on corners, animals on edges,
the seventh crop on the outer ring.

The world plan's later stages are checked there too: rain filling a crop's water,
drought slowing its clock, the seasons refusing an out-of-season seed, recipes
turning wheat into flour and bread (and milk into cheese, a crop into preserves),
storage sweeping a stack that kept too long while the cellar keeps it longer,
facility and fence wear with repair and demolition, breeding through gestation to
a newborn, ailing animals and the vet, butchering and selling, tree stages and
timber and charcoal, the Trading Post's floor and ceiling, buyer orders that expire,
and the visit door refusing a farm whose owner has visitors turned off.

**Not verified here:** how it looks. The scene is generated by
`Farmverse ▸ Setup ▸ Set Up Project` and captured by `Farmverse ▸ Capture ▸ Screens`,
both of which need the editor.

## 9. The tile grid is retired

`Logic/Farm/Tiles.cs`, `TileState` and `TileStage` are gone. The working field is
the world: `FarmSimulation.World` is the same `WorldModel` the 3D scene draws, and
every screen and rating reads lands, crops and animals from it. A plot still has a
`Tiles` count in the empire catalogue - that is the size of the site a player buys,
not a simulation grid - but there is no parallel tile list to keep in step.
