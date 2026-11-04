# Farmverse — master UI design prompt

Paste everything between **BEGIN PROMPT** and **END PROMPT** into your UI design AI
tool. It is written for a single full-product generation; if the tool caps output,
run the numbered screen groups in section 11 as separate follow-ups using the same
design-system page.

The values below are the ones the game already ships: `Assets/Farmverse/UI/Styles/tokens.uss`
(colour, type, spacing), `Docs/navigation.md` (all 81 screens and their routes) and
`Docs/production-model.md` (the four role lifecycles the UI must render generically).

---

## BEGIN PROMPT

### 0. Your role

You are a senior mobile game UI/UX designer and art director. Design the **complete
UI for Farmverse**, a cozy multiplayer farming game for iOS and Android, built in
Unity (UI Toolkit) and played in **portrait only** at a reference resolution of
**360 × 780 dp**. Deliver a production-ready design system, the persistent app
shell, the full component library, and **all 81 screens** in every required state.

Do not redesign the information architecture. The screen list, numbering,
navigation and terminology below are fixed. Your job is to make them beautiful,
consistent, legible and buildable.

### 1. What Farmverse is

Farmverse is a warm, hand-painted farming sim with a player-run economy. A player
chooses one of four roles, each living in its own world:

| Role | World | Plot type | Slot | Loop | Accent |
|---|---|---|---|---|---|
| Farmer | farmer world | field | soil | till → plant → water → grow → harvest | grass green |
| Seed Miner | seed miner world | mine | machine pad | install → fuel → mine → pack seed packets | copper + slate blue |
| Food Manufacturer | manufacturer world | workshop | factory pad | set up → supply → run → pack crates | barn red + warm cream |
| Animal Rearer | animal world | pasture | pasture | build pen → stock → feed → collect | hay gold + leaf green |

All four share **one UI language and one component set**. Only the artwork, data
and one accent tint change. Never build a separate interface per role.

The player owns one or more plots (a plot is a grid of up to 36 tiles). Tapping a
tile opens a contextual action menu whose actions come from that unit's current
production stage. The player sells produce to other players in a marketplace,
buys tools and facilities in a shop, upgrades facilities through five levels,
earns a daily bonus, climbs a net-worth leaderboard, and expands into an empire of
plots.

Tone: **cozy, tactile, optimistic, handcrafted**. Think a storybook farm diorama
you can touch — chunky painted wood, warm parchment, candy-gloss buttons, soft
shadows. Not corporate, not flat, not "kids' app" garish.

### 2. Art direction — non-negotiable

- **Materials:** painted wood (dark walnut frames, honey-oak edges), warm
  parchment paper, glazed candy buttons, woven fabric, terracotta, fresh leaves.
- **Shapes:** generous rounded corners, thick 9-slice frames, visible 4 dp bottom
  edge on buttons so they look pressable, soft drop shadows, subtle inner shadow
  on insets.
- **Lighting:** warm afternoon sun from the upper left; gentle vignette; no neon,
  no glassmorphism, no neumorphism, no pure-black shadows.
- **Illustration:** hand-painted 2.5D, visible brush texture, slightly exaggerated
  proportions, thick soft outlines on icons. Consistent across every asset.
- **Icons:** filled, chunky, 2–3 px painted outline, warm highlights, readable at
  24 dp. Never thin line icons, never Material/Font Awesome.
- **Depth:** three clear levels only — world background, content cards, and
  dialogs/popups. Do not stack more than two modal levels.
- **No dark mode.** The game keeps its palette in both device modes.
- **Texture discipline:** texture belongs inside frames and on large surfaces;
  body text always sits on clean parchment or a solid plate, never on busy art.

### 3. Design tokens — use these exact values

#### 3.1 Colour

| Token | Hex | Use |
|---|---|---|
| wood-dark | `#6B3A1E` | frames, headings, body ink, outlines |
| wood-mid | `#A5602B` | frame highlight, secondary wood |
| wood-light | `#D08A45` | edge light, dividers |
| wood-glow | `#E8A862` | rim light, hover warmth |
| parchment | `#FBEBCB` | dialog and card surfaces |
| parchment-shade | `#F0D5A3` | inset wells, bar tracks |
| inset-tan | `#D9A98A` | rows, pressed insets |
| grass-light | `#8BC53F` | positive, selected, growth |
| grass-dark | `#5FA02A` | positive pressed, foliage |
| sky-top | `#5EB8FF` | upper sky, app background |
| sky-bottom | `#BFE6FF` | lower sky |
| leaf | `#4CAF2E` | active segment, selected seed ring |
| coin-gold | `#FFC629` | coin fill, numbers |
| coin-edge | `#C98A00` | coin rim |
| title-top | `#FFE45C` | title gradient top |
| title-bottom | `#FFB300` | title gradient bottom |
| title-stroke | `#8A4B0F` | title outline |
| barn-red | `#C8362B` | brand accent, danger surface |
| success | `#35C24A` | success state |
| warning | `#FF9A2E` | paused, low durability, almost full |
| danger | `#E8452E` | errors, destructive, insufficient |
| info | `#3C8DE8` | neutral information |
| scrim | `rgba(0,0,0,0.55)` | behind dialogs and sheets |
| b3tr-accent | `#2EC4B6` | reserved token layer, off at launch — do not use |

Button ramps (top → bottom → edge):

| Variant | Top | Bottom | Edge | Use |
|---|---|---|---|---|
| green | `#7FE02B` | `#3DB41E` | `#2A7F12` | primary, confirm, plant, harvest |
| cyan | `#5FD6FF` | `#1AA6E8` | `#0A6FA8` | secondary, neutral actions |
| red | `#FF7A5C` | `#E8452E` | `#9E2A18` | destructive, cancel a sale, delete |
| purple | `#B57BFF` | `#8E4CD8` | `#5E2A9E` | premium, special events, rewarded ad |

Semantic rule: never use colour alone. Every warning and danger state also carries
an icon and a label.

#### 3.2 Typography

Two families only:

- **Lilita One** — display, titles, buttons, numbers, badges. Chunky, rounded,
  confident. Always with a wood-dark outline.
- **Nunito** (Bold / SemiBold / ExtraBold) — body, captions, descriptions, inputs.
  Friendly and highly legible.

| Role | Font | Size | Treatment |
|---|---|---|---|
| Display | Lilita One | 40 | title gradient + 3 px wood stroke, soft shadow |
| Screen title | Lilita One | 28 | title gold + 1.5 px stroke, centred |
| Dialog title | Lilita One | 24 | parchment fill + 1 px stroke, centred |
| Button label | Lilita One | 20 (sm 16, xs 13) | white + 1.5 px wood outline |
| Number | Lilita One | 18 (lg 28, xl 40) | coin gold + wood outline, tabular |
| Body | Nunito Bold | 16 | `#5E3218` |
| Body large | Nunito Bold | 18 | `#5E3218` |
| Secondary | Nunito SemiBold | 14 | 80% wood-dark |
| Caption | Nunito SemiBold | 12 | `#5E3218` |
| Micro | Nunito ExtraBold | 10 | white, centred, uppercase allowed |

Numbers are always tabular and right-aligned in tables. Titles may use a two-stop
gold-to-cream gradient on the splash and big rewards only; everywhere else use the
solid title gold with the stroke.

#### 3.3 Spacing, radii, layout

- Spacing scale (base 4): **4, 8, 12, 16, 24, 32, 48**. Use only these.
- Radii: dialog **28**, card **20**, button **18** (small 14), input **16**,
  avatar **22**, badge **10**.
- Frame thickness: dialog **10**, card **8**, header **14** (baked into 9-slice art).
- Layout: reference **360 × 780**, outer margin **16**, content width **296**,
  dialog width **312** (wide **328**), HUD height **56** at top offset **24**,
  tab bar height **72**, list row height **72**, minimum touch target **48**.
- Two-column grids use **138 × 190** cards with 4 dp side margins so two fit 296.
  Horizontally scrolling item cards use **148 × 168** (76 dp icon zone + text zone).

### 4. Persistent app shell

Always present on the five main tabs; never removed by a screen.

**Top HUD (56 dp, 16 dp side margin, 24 dp from the top):**
avatar (44 dp, round, tappable → player card) · plot chip (small wood plate with
plot name and chevron → plot switcher) · spacer · coin pill (30 dp coin, gold
tabular amount, green plus button → wallet) · gear icon button (48 dp → settings).
The HUD sits on a subtle wood-gradient plate so it reads over the farm.

**Bottom tab bar (72 dp, 5 items, equal width):**
Inventory · Shop · **Home (centre, 56 dp, raised, green candy circle with a barn
icon)** · Marketplace · Ranks.
Active tab: green underline/plate and full-opacity icon; inactive: 70% opacity.
Badges: small red 24 dp count bubble, max "9+". The tab bar has a parchment
surface and a 3 dp wood top edge.

**Layering:** world background → screen content → HUD/tab bar → scrim → dialog.
A dialog dims the screen to **55%** but keeps the HUD visible and dimmed, so the
player never loses their place.

**Back rules:** back closes the top dialog first, then the screen. The landing
screen never exits the app without a confirm. No dialog stacks above two levels.

### 5. Component library — draw every state

For each component show: default, pressed, disabled, selected, loading, error,
locked and (where relevant) empty.

1. **Button** — candy ramp, 4 dp bottom edge, 18 dp radius, white Lilita label;
   heights 56 (primary/hero), 48 (secondary), 40 (small). Optional 22 dp leading
   icon. Press = translate down 4 dp and darken 8%. Disabled = 62% opacity and no
   bottom edge. Wide variant fills the content width. Never use a text link as the
   only action on a screen.
2. **Icon button** — 48 dp square (large 56), wood or parchment disc, painted icon
   at 24 dp, same press behaviour.
3. **Card** — 148 × 168 item card: framed parchment, 76 dp icon zone, 2-line name,
   thin divider, value/price row. 138 × 190 grid card for two-up grids: 64 dp icon,
   name, figure, price. Press = scale 0.97. Never let text run outside the frame.
4. **List row** — 72 dp, 10 dp side padding, 40–48 dp icon, title 16, optional
   subtitle 12, right side value + chevron or action. Rows sit on parchment; the
   pressed state is a 6% wood wash.
5. **Chip** — 32 dp tall pill, icon + label, parchment; selected = green fill,
   white label. Used for filters, categories and tags.
6. **Segmented control** — 48 dp, 2–4 equal segments, leaf-green active plate,
   parchment track.
7. **Toggle** — 64 × 32, parchment track, green when on, wood knob with a small
   top highlight.
8. **Progress bar** — 16 dp tall, parchment-shade track, rounded; fill green
   (normal), warning (paused / low), danger (critical). Optional label inside or
   right-aligned.
9. **Coin pill** — coin icon 30, gold tabular number with wood outline, green plus.
10. **Timer pill** — small parchment pill, clock icon 18, "4m 20s".
11. **Level button** — 210 × 64, wood plate, level title 28 and subtitle 14.
12. **Badge / tag / ribbon** — 24 dp count badge, 44 × 19 rotated NEW tag, 30 dp
    ribbon for "Best value" / "Day 7".
13. **Toast** — parchment pill, icon, one line, 3 s, bottom above the tab bar.
14. **Empty state** — centred painted illustration (96 dp), title, one sentence,
    one action; never just "Nothing here".
15. **Locked state** — art at 40% with a padlock and the exact requirement
    ("Farmhouse level 4").
16. **Stepper** — minus / value / plus, 48 dp targets, used for quantities.

### 6. Containers

- **Screen** — dark wood header 56 dp with title 28 and a back arrow (for non-tab
  screens), list/grid content on parchment, tab bar if top-level.
- **Dialog** — 312 or 328 dp wide, parchment body inside a 40 dp painted wood
  frame, title on a wooden sign that overlaps the top edge, one primary and one
  secondary action side by side or stacked, close X at top right, soft 24 dp
  shadow behind. Max height 640; body scrolls.
- **Bottom sheet** — rises from the bottom, grabber, 24 dp top radius, max 80%
  height, title + close, scrollable content, safe-area bottom inset.
- **Anchored popup** — points at the tile or item it belongs to with a small
  parchment arrow; max 280 wide; closes on scrim tap.
- **Overlay** — scrim + coach panel for the tutorial, with a cut-out highlight on
  the real element and a short arrow.
- **System screen** — full bleed, no chrome: splash, loading, maintenance,
  connection lost.

### 7. The four worlds

The shell, components and type never change. For each role, art-direct the
background and one accent:

- **Farmer** — rolling field, red barn, silo, windmill, chickens; grass-green accent.
- **Seed Miner** — terraced quarry, timber headframe, mine cart, lanterns; copper
  and slate-blue accent, keep the same parchment and wood.
- **Food Manufacturer** — stone workshop, chimney, crates, conveyor, hanging herbs;
  barn-red accent.
- **Animal Rearer** — fenced pasture, hay bales, pond, sheep and cows; hay-gold accent.

Accent appears only in: hero art, selected tab underline, plot chips and one
highlight per screen. Never tint the whole UI.

### 8. Screen inventory — design all 81

Each number is fixed and must appear in the frame name (`NN_Screen_Name`). For
every screen show the default state plus the states named in section 10.

#### Part 2a · Launch and onboarding (1–9)

1. **Splash** — full-bleed painted sky, clouds, pines, fence, barn; wordmark on a
   hanging wooden sign with the gold gradient; version and studio microcopy at the
   bottom. No chrome, no buttons except a subtle tap-to-continue after load.
2. **Loading & update check** — barn silhouette at 85%, centred progress bar with
   percentage, rotating gameplay tip in a parchment plate, a small chicken walking
   along the bar. Mandatory-update variant with one green button.
3. **Legal & consent gate** — parchment sheet in the frame, two scrollable consent
   blocks with plain-language summaries, links, green **Agree** and quiet **Not
   now**. No pre-ticked boxes, no dark patterns.
4. **Sign in / create account** — logo, one-line promise, provider buttons
   (Google, Apple, Guest) with painted provider marks, terms line, inline error
   state, loading spinner on the chosen provider.
5. **Welcome story** — three full-bleed comic panels (your land → your role → your
   first harvest), progress dots, Skip and Next; painterly captions, no body copy
   longer than two lines.
6. **Role selection** — 2 × 2 large role cards with portrait, role name, one-line
   fantasy, three loop chips (e.g. Till · Water · Harvest), and a locked
   "Coming soon" treatment for unavailable roles. Selected = green ring + check.
7. **Profile setup** — dialog: 12-avatar grid, display name, farm name, Continue.
   Inline validation, keyboard-safe layout.
8. **Guided tutorial overlay** — scrim with a cut-out on the real target, coach
   panel with a short line, arrow, step dots, Next and Skip. 5–7 steps, never
   blocks the HUD permanently.
9. **Starter pack reward** — dialog with a radial burst, three or four reward rows
   (coins, seeds, tool), Claim. Reward numbers count up on open.

#### Part 2b · Home and the field (10–20)

10. **Home · landing** — the farm diorama behind a parchment panel; wordmark, one
    hero **Play** button (200 dp), a compact stats strip (net worth, farm rating),
    three shortcut tiles (Shop, Inventory, Ranks), daily bonus button with a NEW
    tag. One call to action only: Play.
11. **Gameplay · your farm** — the 2.5D world fills the screen. Top bar: back,
    plot name, one-line hint. The 6 × 6 tile grid sits in perspective with clear
    stage art per tile (empty soil, tilled, seeded, growing, ripe, paused). Bottom
    dock: tools, water can with 3 drops, facilities, notifications bell, daily.
    Tiles are never smaller than 48 dp.
12. **Tile action menu** — anchored popup above the tile: crop icon and name,
    stage badge, 2–4 large actions (Plant, Water, Harvest, Clear) each with icon
    and cost; disabled actions state the missing tool/item/coins inline. Close.
13. **Seed picker** — bottom sheet: seed cards with icon, name, grow time, yield,
    owned count and price; selected card gets a leaf-green ring; link to buy seeds;
    confirm plant.
14. **Crop progress popup** — anchored: crop name, stage badge, growth bar,
    water drops 0–3, time remaining, expected harvest, quality stars, and a warning
    row when paused ("Needs water — growth paused"). Water action.
15. **Build mode** — grid editing state: buildable tray at the bottom, ghost
    placement, green/red validity, rotate if relevant, confirm and cancel. Locked
    tiles show their requirement.
16. **Land expansion map** — top-down plot with owned tiles in colour and locked
    tiles faded; the expandable frontier pulses gently; each next tile shows cost;
    side stat tiles for fertility, water access and road access as 1–5 pip ratings.
17. **Expansion confirm** — dialog: tiles bought, cost, balance after, before/after
    mini-map, Confirm / Cancel.
18. **Inventory & storage** — header with capacity bars per store; segmented
    control Seeds · Produce · Goods · Tools; two-column grid of item cards with
    quantity badges; sort control; Sell on each card; empty and full states.
19. **Item detail** — bottom sheet: large painted icon, name, description, unit
    value, owned quantity, actions Sell / Use / List. Show the market band where
    relevant.
20. **Tool durability & repair** — dialog: tool art, durability bar, "Broken"
    state, repair cost, **Repair** (green) and **Buy new** (cyan), explanation of
    what the tool is used for.

#### Part 3 · Farm management (21–26)

21. **Animal pen view** — painted pen scene at the top, animal cards below with
    name, species, hunger and happiness bars, produce-ready badge; Collect per
    animal and Feed all.
22. **Feed & care** — dialog for one animal: portrait, hunger and happiness,
    feed options with owned counts, **Feed**, link to buy feed when empty.
23. **Processing station** — machine card with status light, recipe selector,
    input requirements vs owned (green/red), batch queue with progress bars,
    Start batch, Collect. Works identically for miner and manufacturer.
24. **Production complete** — dialog: burst, output icon, quantity, quality stars,
    net-worth delta, Collect and List for sale.
25. **Food need / eat** — homestead dialog: food need meter, available meals,
    effect of eating, **Eat**, link to buy food.
26. **Welcome back summary** — after 30+ minutes away: elapsed time, crops ready,
    units paused, goods collected while away, **Collect all**, Continue.

#### Part 3c · Shop (27–32)

27. **Shop home** — featured banner (120 dp) with one offer, four category tiles
    (Tools, Machinery, Structures, Land), coin pill, close.
28. **Shop · tools** — two-column grid of 138 × 190 cards: icon, name, durability,
    price, owned badge; disabled when unaffordable with the shortfall shown.
29. **Shop · machinery** — cards state machine class, required slot, fuel use and
    unlock level; locked cards name the requirement.
30. **Shop · structures & land** — structure cards plus a wide "Expand your land"
    plate with **View map** and **Browse plots**.
31. **Purchase confirm** — dialog: item, quantity stepper, unit price, total,
    balance after, **Buy** and Cancel.
32. **Not enough coins** — dialog: friendly shortfall ("You need 320 more coins"),
    **Sell something** and **Watch a video** (purple), never a dead end.

#### Part 4a · Marketplace (33–43)

33. **Marketplace browse** — search field, filter chips, two-column listing cards:
    item icon, quantity, price per unit, seller name, time left; a small floor/
    ceiling hint; **Sell** shortcut in the header.
34. **Search & filter** — bottom sheet: query, category, price range, sort
    (newest, price, ending soon), Reset and Apply.
35. **Listing detail** — hero item, price per unit, quantity stepper, total, seller
    card, market band (floor–current–ceiling), **Buy**, **Visit farm**.
36. **Buy confirm** — dialog: item, quantity, unit price, total, fee, balance
    after; Confirm and Cancel.
37. **Purchase success** — receipt dialog: check mark, item and quantity, total
    paid, **Go to storage**, Done.
38. **Create listing** — form: item picker, quantity stepper, price per unit with
    floor/ceiling guidance as a slider or band, duration, fee, live "you receive"
    total, **List item**.
39. **My listings** — tabs Active · Sold · Expired; rows with countdown, price,
    views, Promote and Cancel; empty state with a Sell action.
40. **Seller profile** — avatar, farm name, member since, rating, active listings
    grid, **Visit farm**.
41. **Visit world (read-only)** — the same farm view with no action buttons; a
    parchment banner "Visiting Maya's farm"; back and Report. This screen must
    never offer an action that changes another player's world.
42. **Transaction history** — ledger list: icon, reason, date, signed coin amount
    (green +, red −), running balance, receipt chevron.
43. **Promote listing** — dialog: promotion durations and costs, expected reach,
    **Promote**, Cancel.

#### Part 4b · Ranks and rewards (44–50)

44. **Leaderboard** — segmented Global · Friends; top three on a painted podium
    with medals; rows with rank, avatar, name, net worth; the player's own row is
    pinned at the bottom with a leaf-green edge.
45. **Player card** — popup: avatar, name, rank, net-worth breakdown (coins, goods,
    objects), badges; View profile, or Edit profile when it is you.
46. **Edit profile** — dialog: avatar grid, display name, farm name, Save.
47. **Daily bonus** — seven-day grid, today highlighted with a gentle pulse, Day 7
    larger with a ribbon, **Claim**, streak note. Claimed days show a check.
48. **Rewarded ad offer** — dialog: what you get, duration, **Watch** (purple) and
    "Not now"; show the remaining daily views.
49. **Ad reward result** — success or "no reward" state, reward row, Continue.
50. **Notifications centre** — list of sale, batch ready, listing expired and bonus
    events; unread dot, timestamp, deep-link chevron, Mark all read.

#### Part 5 · Wallet, settings, system (51–58)

51. **Wallet** — balance, total earned and spent, net-worth breakdown with three
    bars (coins, goods, objects), transaction history link, token card shown as
    switched off (feature flag), no real-money checkout.
52. **Settings** — sound, music, haptics toggles; **Allow visitors** toggle;
    language, help, account, privacy rows; version and build at the bottom.
53. **Language** — bottom sheet: nine languages with native names, check on the
    selected one, note that right-to-left mirrors the layout, Done.
54. **Help** — search, common-question rows, category and screenshot toggles,
    Contact us, Copy my player id.
55. **Account** — linked providers, Download my data, Delete account (red,
    confirm), Sign out.
56. **Connection lost** — dialog: offline illustration, "You're offline", last
    synced time, **Retry**, quiet Close.
57. **Maintenance / update required** — full bleed: barn in the rain or a closed
    gate, clear message, **Check again** or **Update**.
58. **Generic confirm & error** — the one reusable dialog: title, body, primary
    and secondary actions; success, warning, error and destructive variants.

#### Part 7 · Facilities, five levels (59–71)

59. **Facilities hub** — facility cards/rows for Well, Warehouse, Farmhouse,
    Market Stall, Granary, Cellar and Farm Rating, each with painted icon, level
    badge and one-line benefit; Add a facility and Empire map.
60. **Facility detail & upgrade** — hero art, current level, benefits, next-level
    cost and time, Upgrade; linked panel (water, capacity, slots).
61. **Upgrade confirm** — dialog: cost, time, before → after benefits, Confirm.
62. **Construction in progress** — timer, what is being built, what it unlocks,
    Notify me; no fake speed-up currency.
63. **Upgrade complete** — burst, new level, unlocked benefit, Next upgrade, Done.
64. **Well panel · water** — water capacity, refill timer, current drops, Upgrade.
65. **Warehouse storage panel** — capacity bars per category, contents summary,
    Upgrade, capacity-full link.
66. **Capacity full** — dialog naming the store, how much is over, **Upgrade
    warehouse** and **Sell some**.
67. **Farmhouse · homestead** — level, what it unlocks, food need, Add food,
    Upgrade.
68. **Market stall manager** — three slots, empty vs filled, listing link, stall
    level and reach, Upgrade.
69. **Granary & raw storehouse** — capacity, contents, Buy more, Upgrade.
70. **Cellar · finished goods** — contents, value, Sell, Eat, Upgrade.
71. **Farm rating** — large score dial or seal, breakdown by production,
    facilities, land and reputation, the cheapest next actions with chevrons,
    Expand.

#### Part 8 · Empire, multiple plots (72–81)

72. **Empire map** — illustrated regional map with plot pins, production-ready
    badges, total plots, coins and net worth, Plot switcher, New plots, Progress,
    Dashboard.
73. **Plot switcher** — bottom sheet: every plot with name, type icon, status and
    ready count; current plot checked; Switch, Add plot, Plot detail.
74. **Plot detail** — hero of the plot, type, level, stat tiles (fertility, water,
    road access) as 1–5 pips, facility list, Enter plot and Expand tiles.
75. **Shop · new plots** — plot cards with type, size, price, unlock requirement
    and a mini map; sold-out and locked states.
76. **Plot preview & details** — isometric preview, what is included, starting
    tiles, price, **Buy plot**.
77. **Plot purchase confirm** — cost, balance after, what you get, Confirm.
78. **Plot setup · choose a type** — four plot-type cards (field, mine, workshop,
    pasture) with slot kind, production model and a one-line loop; selected ring;
    Start building.
79. **Welcome to your new plot** — dialog: plot art, name field, "Your first tiles
    are ready", Let's go.
80. **Empire progress & titles** — progress track with milestones, current title,
    next unlock, Upgrade farmhouse, Buy a third plot.
81. **Empire dashboard** — aggregate across plots: production ready, storage used,
    coins, water, **Collect all**, Buy more, Fetch water.

### 9. Component and screen states

Every list or grid screen must include:

- **Loading** — 3 skeleton rows/cards in the same shape, subtle parchment shimmer,
  no spinner-only screens.
- **Empty** — painted illustration, title, one sentence, one action.
- **Error** — inline plate with what failed, Retry, and a support link where useful.
- **Offline** — banner under the HUD, last-synced time, read-only content.
- **Locked** — art at 40%, padlock, exact requirement named.
- **Full / at capacity** — warning bar plus the two valid actions.
- **Insufficient funds** — price turns danger, shortfall shown, primary disabled.
- **Paused / starving / servicing** — warning chip on the tile or row, reason in
  plain words, the restoring action highlighted.
- **Complete / ready** — gold badge, gentle pulse, collect action.
- **Read-only (visit)** — no primary actions, banner, everything tappable only for
  information.

### 10. Motion

- Screen push: slide **24 dp** from the right + fade, **240 ms ease-out**.
- Dialog: scale **0.85 → 1.05 → 1.0**, fade, **200 ms**; scrim fades in with it.
- Press: button translates down **4 dp**, card scales **0.97**, **80 ms**.
- Reward: burst **400–600 ms**, reward numbers count up; confetti only on Day 7,
  starter pack and plot purchase.
- Bars animate to value in **300 ms**; timers tick once per second without layout
  shift.
- Daily bonus pulse: scale 1.035, **450 ms**, ease-in-out, once every few seconds.
- Reduced motion: crossfade **120 ms**, no pulse, no parallax, no scale bounce.

### 11. Deliverables and output contract

Produce, in this order:

1. **`00 Design System`** — colour swatches with names and hex, type ramp, spacing
   rulers, radii, frame thickness, button ramps, icon grid, material samples,
   motion notes, do/don't.
2. **`01 Components`** — every component from section 5 in every state, on a
   parchment background and over a farm background.
3. **`02 Shell`** — HUD, tab bar (all five tabs, active/inactive/badge), dialog,
   bottom sheet, anchored popup, toast, scrim, system screen.
4. **Screen frames, 360 × 780 @2x, portrait**, named `NN_Screen_Name` with the
   exact numbers above. Group them on pages:
   `10_Launch (1–9)`, `20_Home_Field (10–20)`, `30_Management (21–26)`,
   `40_Shop (27–32)`, `50_Market (33–43)`, `60_Ranks (44–50)`,
   `70_System (51–58)`, `80_Facilities (59–71)`, `90_Empire (72–81)`.
5. **Clickable prototype path** — Splash → Role → Home → Play → Tile → Plant →
   Crop progress → Harvest → Inventory → Sell → Marketplace → Buy → Leaderboard.
6. **Handoff page** — tokens as CSS variables / JSON, component redlines, asset
   list with sizes, and a note for each screen that maps it to its route id.

Rules for the output:

- No lorem ipsum. Use the sample data in section 12.
- No stock photos, no emoji as icons, no generic placeholder avatars; if you
  cannot paint an asset, draw a clearly labelled art slot with its exact size and
  a one-line art brief (e.g. "barn, 220 × 170, painterly, warm afternoon").
- Keep body text inside safe areas; respect a 24 dp top and 16 dp bottom safe area.
- Every screen must be buildable with the components you defined. If you invent a
  component, add it to `01 Components` in all states.
- Do not add screens, tabs, currencies, energy systems or features not listed.

### 12. Sample data — use this, not placeholders

- Player: **Maya**, farm **Willow Creek**, rank **#128**, farmhouse level **3**.
- Coins **12,450** · net worth **48,920** (coins 12,450 · goods 21,470 · objects 15,000).
- Plot: **North Field**, **24 / 36** tiles, fertility **4/5**, water **3/5**,
  road access **2/5**.
- Growing: Tomato **ready in 4m 20s**, Potato **ready in 18m**, Maize **paused —
  needs water**, Wheat **ripe**, 8 tomatoes expected, quality **★★☆**.
- Seeds: Tomato ×12, Potato ×7, Maize ×3, Wheat ×0.
- Water can: **2 / 3** drops. Hoe durability **64%**. Axe **broken**.
- Market: Tomato **34 coins/unit**, floor **28**, ceiling **41**, seller
  **Old Oak Farm**, **2h 14m** left.
- Listings: 3 active, 2 sold, 1 expired.
- Notifications: "Your tomatoes sold — +272 coins", "Mill batch ready",
  "Listing expires in 1 hour", "Daily bonus is ready".
- Timers: "4m 20s", "1h 12m", "Ready", "Expired".
- Facility: Warehouse level 2, **86 / 100** capacity; Well level 2, **2 / 3** water;
  Market stall level 2, **2 / 3** slots used.

### 13. Copy rules

- Sentence case everywhere; no exclamation marks; no "Oops!", "Yay!", "Uh-oh".
- Buttons are verbs: Plant, Water, Harvest, Sell, Buy, Upgrade, Collect, Retry.
- Name the missing thing and the fix: "You need 320 more coins." · "Your watering
  can is empty. Refill at the well." · "Warehouse full. Upgrade it or sell 12
  items." · "Locked — Farmhouse level 4."
- Time is short: "4m 20s", "1h 12m", "Ready".
- Numbers use thousands separators and never sit inside a sentence without a unit.
- Currency is always "coins", never "$". Never imply real money at launch.
- Warm, calm, specific. One idea per line.

### 14. Accessibility — treat as acceptance criteria

- Body text **4.5:1** against the pixel actually painted behind it; large text
  **3:1**. When a label sits on wood or art, give it a parchment plate.
- Every interactive element is at least **48 × 48 dp** with 8 dp spacing.
- Warning and danger are never carried by colour alone: icon + label.
- Visible focus: 2 dp cream ring plus 2 dp wood outer ring.
- Support **130% text scaling**: dialogs grow and scroll, never clip; rows grow.
- Right-to-left mirrors lists, arrows and layout, never coin icons or numerals.
- Every image and icon has a content description; decorative art is ignored.
- No flashing above 3 Hz. Honour reduced motion.
- Contrast is measured against the 9-slice frame where a label overlaps it.

### 15. Do not

- Do not redesign the navigation, rename screens, or merge tabs.
- Do not use purple-blue SaaS gradients, glassmorphism, neumorphism, thin
  outlines, flat Material icons, generic dashboard charts, or dark mode.
- Do not put more than one primary action on a screen.
- Do not dead-end the player: every locked, empty, full or poor state offers the
  next valid action.
- Do not use colour as the only signal, or place body text on busy illustration.
- Do not use real money, crypto or the B3TR token in the launch UI.
- Do not stack dialogs more than two levels deep.
- Do not show a spinner as the only loading state.

## END PROMPT

---

## Using it with a specific tool

- **Figma AI / Figma Make, Google Stitch, Uizard, Visily:** paste as-is. Ask for a
  design-system page first, then generate page-by-page with section 11's page
  names. Stitch works best with one flow per prompt; reuse the token section each
  time.
- **v0 / Lovable / Bolt:** append: *"Output React + Tailwind inside a 360 × 780
  phone frame. Put every token in `:root` CSS variables. Build screens as routes,
  with a dev-only screen index. Use inline SVG for icons and CSS gradients for
  placeholder art."* Generate the shell and components first, then the screens.
- **Midjourney / DALL·E / Firefly:** use sections 2 and 7 only. These models
  cannot render reliable UI text; use them for the splash art, world backgrounds,
  empty-state illustrations and icon sheet, then place those assets in Figma.
- **If output is capped:** generate `00`–`02` first, then one group from section 11
  per message, always repeating sections 3–6 verbatim.
