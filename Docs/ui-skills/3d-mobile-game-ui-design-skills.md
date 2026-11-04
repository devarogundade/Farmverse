# 3D Mobile Game UI — Design Skills & Notes

Learned from 18 screenshots of a top-grossing castle match-3 game (shop, event dialogs, leaderboards, team, settings, home HUD, daily rewards, areas, collection, loading). These notes capture the **techniques**, not the game's art. Farmverse uses its own characters, palette, icons and names.

> Every screenshot is in `references/` next to this file and appears beside the section it illustrates; the full set is in section 15.

> Measurements are read from 590 × 1280 px screenshots and converted to a 360 × 780 dp reference (× 0.61). Treat them as close approximations. Hex values are sampled by eye and marked ≈.

---

## 1. The ten rules that make it feel "3D"

1. **Everything has thickness.** Every button, panel, row, tab and pill has a lighter top face, a darker bottom lip (3–5 dp) and a dark outline. Nothing is a flat rectangle.
2. **Layer, don't box.** Screens are built as stacks: background art → dark frame → panel → cream inset → content. Each layer is a different value, so depth reads without shadows.
3. **Text is an object.** Titles and labels carry a 2–4 dp dark outline, a hard 2–4 dp drop shadow and often a vertical gradient. Plain flat text appears only on cream insets, and even then it's bold and dark.
4. **One saturated hue per job.** Green = go/confirm/buy. Red = close/destructive/"!" alert. Orange = secondary or "special". Purple = events/premium. Gold = reward/rank 1. Blue = chrome/navigation. Never mix the meanings.
5. **Icons are rendered objects.** Every reward, booster, currency and nav icon is a lit 3D render with rim light, often overlapping its container edge.
6. **Big type, few words.** One title, one sentence of description, one CTA per dialog.
7. **Numbers are heroes.** Prices, scores, counts and timers are in the display font, large, outlined, and usually inside a pill or plate.
8. **Every scarce resource has a timer.** Small dark pills with a clock icon and a countdown ("13:09:28", "2d 13h", "5d 13h") sit under or beside the thing they limit.
9. **The world stays visible.** Popups dim and blur the 3D scene behind them; the HUD stays in place, dimmed.
10. **Repetition = learnability.** The same 12–15 components cover every screen. A new screen is a new arrangement, not new parts.

---

## 2. Screen anatomy

<p><img src="references/12_leaderboard_players.jpg" width="200" alt="leaderboard players"> <img src="references/13_settings.jpg" width="200" alt="settings"> <img src="references/04_event_dialog_art_window.jpg" width="200" alt="event dialog art window"> <img src="references/15_daily_rewards.jpg" width="200" alt="daily rewards"></p>

<sub>Full screen · modal screen · event dialog · celebration overlay</sub>

```
┌──────────────────────────────┐
│ HEADER BAR (royal blue)      │  ~56 dp; title centred; close X right; currency pill left
├══════════════════════════════┤  3 dp orange/gold rule under the header
│ TAB STRIP (optional)         │  dark slate track, 4 equal tabs, timer pill under active
│                              │
│ CONTENT on slate background  │  rows / cards / panels, 8–10 dp side margins
│                              │
│ STICKY CTA (optional)        │  "Play With Friends", "Continue", "Request"
├──────────────────────────────┤
│ BOTTOM NAV (5 tabs)          │  ~64 dp; active tab widens and shows its label
└──────────────────────────────┘
```

- **Full screens** (Leaderboard, Team, Areas, Collection): blue header + slate body + bottom nav.
- **Modal screens** (Shop, Settings, Profile): blue header with a red close X; the bottom nav is hidden or dimmed under a scrim.
- **Event dialogs** (pass, competition, team event): a themed frame floating over the dimmed home scene, close X overlapping the top-right corner.
- **Celebration overlays** (Daily Rewards, Welcome Back, Level Complete): no panel at all, or a single framed card; a big gradient title; rewards floating on the scrim; "Tap to Claim/Continue" at the bottom.

---

## 3. Colour system (≈ observed roles)

<p><img src="references/07_leaderboard_teams.jpg" width="200" alt="leaderboard teams"> <img src="references/03_event_dialog_reward_callout.jpg" width="200" alt="event dialog reward callout"> <img src="references/05_event_dialog_team.jpg" width="200" alt="event dialog team"> <img src="references/01_shop_offers.jpg" width="200" alt="shop offers"></p>

<sub>Cool blue/slate chrome; warm actions and rewards; one frame colour per event</sub>

| Role | ≈ Hex | Where |
| --- | --- | --- |
| Header blue | #1F55B8 → #2A6FD6 | top bar, tab bar, dialog frames |
| Header rule | #F2A93B | 3 dp line under every header |
| Slate body | #3E4C66 | screen background behind lists |
| Slate panel | #56657F with #2C374B outline | grouped controls (settings), tab tracks |
| Cream row | #FFF3D6 → #F3E1B8 | list rows, insets, description boxes |
| Cream text ink | #3A2A12 | body text on cream |
| Green CTA | #7BD62B → #3E9E1A, lip #2A6E10 | buy, start, continue, help, join |
| Red | #F04A3A → #C4231A | close X, leave, "!" badges, timer pills |
| Orange | #FFB23A → #F07A12 | team info, special offer, secondary CTA |
| Purple | #8E4BE0 → #5E2AB0 | event frames, active toggle (World), premium |
| Gold | #FFD54A → #F2A01C, outline #8A4B0F | titles, rank 1, rewards, rims |
| Teal/green event | #2E8B57 frame | team event dialog |
| Timer pill | #8C1E2E bg, cream text | countdowns |

**Usage rules**

- Chrome is cool (blue/slate); rewards and actions are warm (gold/green/orange/red). Warm elements pop because the chrome is cool.
- Each event owns one frame colour (purple competition, green team event, orange/red offers) so players recognise it at a glance.
- Rank colours are fixed: 1 gold, 2 silver-blue, 3 bronze-orange. They appear on the podium, the rank circle and the medal.

---

## 4. Typography

<p><img src="references/09_leaderboard_podium.jpg" width="200" alt="leaderboard podium"> <img src="references/15_daily_rewards.jpg" width="200" alt="daily rewards"> <img src="references/04_event_dialog_art_window.jpg" width="200" alt="event dialog art window"></p>

<sub>Gradient outlined titles, outlined button labels, display-font numbers</sub>

**Two families:** a chunky, rounded, heavy display face for titles, buttons, numbers and names, and a bold rounded sans for descriptions.

| Style | Size (dp) | Treatment |
| --- | --- | --- |
| Screen title ("Leaderboard") | 30–34 | cream-to-yellow vertical gradient fill, 3 dp brown outline, 3 dp drop shadow, slight arch on celebration titles |
| Dialog title ("Lightning Rush") | 26–30 | cream fill, dark outline, sits on a curved ribbon band |
| Button label | 18–24 | cream/white fill, outline in the button's darkest tone, 2 dp drop |
| Big CTA ("Start", "Continue") | 28–34 | same as above, extra weight |
| Numbers (prices, scores) | 16–22 | display font, white or cream with outline; inside a pill |
| Row name | 16–18 | display font, dark ink on cream (no outline) |
| Row subtitle (team name) | 11–12 | bold, muted brown |
| Description | 13–14 | bold sans, cream on coloured panel or dark ink on cream; max 3 lines |
| Micro labels ("Score", "Capacity", "Level") | 10–11 | muted, above the number |

**Rules**

- Text on art or colour gets an outline; text on cream doesn't.
- Outline colour = the darkest tone of the surface under it (green button → dark green), never pure black.
- Gradient fill only for screen titles and celebration headlines.
- Title-case on titles and buttons; sentences for descriptions; "!" is allowed in event copy. (Farmverse's own copy rules override this: sentence case, no "!".)

---

## 5. Depth recipe (apply to every raised element)

<p><img src="references/13_settings.jpg" width="200" alt="settings"> <img src="references/02_shop_offer_tiers.jpg" width="200" alt="shop offer tiers"></p>

<sub>Outline + gradient + gloss band + shine dots + bottom lip on every button and card</sub>

```
outline      2–3 dp, darkest tone of the fill
fill         vertical gradient, light top → saturated bottom
gloss        top 35–45% band, white 50% → 5%, inset 5–7 dp, rounded
shine        1–2 small white ovals top-right (buttons only)
lip          3–5 dp darker band along the bottom edge (inner shadow)
drop         0 / 3–4 dp hard shadow in the outline colour
ambient      0 / 8–12 dp soft shadow, 30–40% of a dark hue (only on floating panels)
```

- **Insets** (cream description boxes, value wells, tracks) invert it: a darker top inner shadow and a lighter bottom edge, so they read as pressed into the surface.
- **Pressed state** removes the drop, moves the element down 3–4 dp and darkens it ~8%.
- **Panels** use a thick outer frame (6–10 dp) with a thin gold or light inner rim line, which reads as a bevelled edge.

---

## 6. Component library (what the game reuses)

### Buttons

- **Primary pill/rect:** radius 14–18 dp, height 44–56 dp; big CTAs 60–68 dp tall and 60–70% of the screen width.
- **Price button:** "$1.99" in the label; a smaller second line under it ("1/1 Available").
- **Icon + label:** the icon sits left inside the button (heart + "Request", calendar + "Save your progress").
- **Icon-only round:** close X (red circle, white X, 40–44 dp), info "i" (blue circle, 24–28 dp), plus "+" (small green square on currency pills).
- **Two-up grids:** equal-width buttons with a 10–12 dp gap (Settings: Search Team / Leave Team).
- **Colour = meaning:** green go, blue neutral/system (Save progress), red leave, orange team/info, purple event.

### Pills and counters

- **Currency pill:** dark inset pill, the 3D icon overlapping the left end, the number in the display font, a green "+" at the right end.
- **Status pill:** heart + "5" + "Full"; star + count + red badge.
- **Timer pill:** dark red/maroon, a clock icon overlapping the left, a short countdown.
- **Count badge:** a red circle with a white "!" or number and a white outline, placed at the top-right corner of a tab, button or avatar.
- **Rank circle:** 28–32 dp; gold, silver or bronze for 1–3 with a dark number; plain number text from 4 down.

### Rows (leaderboard / team)

<p><img src="references/07_leaderboard_teams.jpg" width="200" alt="leaderboard teams"> <img src="references/12_leaderboard_players.jpg" width="200" alt="leaderboard players"> <img src="references/11_team_requests.jpg" width="200" alt="team requests"></p>

- A cream card, radius 12–14 dp, 64–72 dp tall, 6–8 dp between rows, with a thin darker outline and a 2–3 dp bottom lip.
- **Left to right:** rank (circle or number), framed avatar (a 48 dp square tile with a coloured frame), name block (display-font name plus a muted subtitle), then a right-aligned stat block (micro label above, number below; sometimes two columns like "Capacity 50/50" and "Score 960118").
- **Team emblem:** a shield shape coloured by team, with an icon inside.
- **Request rows (team):** avatar outside the card on the left with a speech-tail notch; a pinkish header strip ("X asking for lives!"); a progress bar ("2/5") with a big heart icon overlapping its left end; a green "Help" button at the right.

### Tabs and toggles

<p><img src="references/07_leaderboard_teams.jpg" width="200" alt="leaderboard teams"> <img src="references/13_settings.jpg" width="200" alt="settings"></p>

- **Tab strip:** a dark slate track; tabs are lighter slate; the active tab is lighter or bluer with an inner glow; a timer pill hangs under the active tab; a red "!" badge sits on a tab corner.
- **Segmented sub-toggle:** "World / Nigeria". The active side is a saturated purple pill, the inactive side dark slate.
- **On/off switch:** a pill with a green "ON" segment and a blue knob segment, outlined, labelled above in the display font.

### Bottom nav

- Five icons, each a rendered 3D object (book/star, trophy, castle, team shield, cards).
- The **active tab** widens to about 1.6× the others, shows its text label, and its icon is raised so it overlaps the bar top.
- Inactive tabs are icon only.
- A red "!" badge marks a tab that needs attention.

### Progress bars

<p><img src="references/06_areas_list.jpg" width="200" alt="areas list"> <img src="references/17_collection_grid.jpg" width="200" alt="collection grid"> <img src="references/14_home_hud.jpg" width="200" alt="home hud"></p>

- A dark inset track with a green fill and a white "x/y" label centred in the display font. A reward icon or level hexagon caps the right end (key bar, collection bar).
- In list cards a "Do It" button sits right beside the bar.

### Ribbons and banners

- **Section ribbon** ("Weekly Contest", "General Stats"): a teal/blue ribbon with folded tail ends.
- **Corner ribbon** ("Popular", "90% OFF"): a diagonal purple or red sash across a card's top-left corner.
- **Header tab** ("Haunted Pass", "Special Offer"): a label plate that breaks out of the card's top edge.
- **Area title ribbon:** a red banner with a small cream tab above it ("Area 15") and the big title on the ribbon ("Museum").

### Dialog frame (event)

<p><img src="references/03_event_dialog_reward_callout.jpg" width="200" alt="event dialog reward callout"> <img src="references/04_event_dialog_art_window.jpg" width="200" alt="event dialog art window"> <img src="references/05_event_dialog_team.jpg" width="200" alt="event dialog team"></p>

1. Curved title band across the top: the frame colour plus a gold rim and a slight arch.
2. Inner art window: illustrated scene, rounded corners, an info "i" at its top-left, an optional timer pill on its bottom edge.
3. An optional cream callout box with a speech tail, listing rewards as icons joined by "+".
4. A choice row (two tiles, the selected one with a gold frame).
5. Two to three lines of description in cream.
6. One big CTA, sometimes with an icon chip inside (Start + ∞ heart).
7. A red close X overlapping the top-right corner.

### Reward presentation

<p><img src="references/15_daily_rewards.jpg" width="200" alt="daily rewards"> <img src="references/03_event_dialog_reward_callout.jpg" width="200" alt="event dialog reward callout"></p>

- **Item + quantity:** the 3D icon with an outlined "x3", "x10" or "300" overlapping its bottom-right.
- **Timed boosters:** the icon plus a green ribbon tag reading "∞ 24h" or "∞ 10m".
- **Grids:** 3 columns on celebration screens with generous gaps and no cards (Welcome Back), or carded tiles (Daily Rewards).
- **Daily track:** a calendar icon with the current day, then a bar with milestone gift icons at 8/15/22/30.
- **Day grid:** 3 × 2 cards, each with a ribbon header "Day N", a big green check when claimed, and the current day highlighted. Day 7 is a full-width card in a different colour holding several rewards.

### Podium (top 3)

<p><img src="references/09_leaderboard_podium.jpg" width="200" alt="leaderboard podium"> <img src="references/10_leaderboard_podium_finished.jpg" width="200" alt="leaderboard podium finished"></p>

- Three columns: the middle one tallest and gold (1st), the left blue-silver (2nd), the right orange-bronze (3rd).
- Each column shows the avatar on top (overlapping its edge), name and team, the prize icons in an inset, "Score: N", and a rank circle at the bottom.
- The player's own entry gets a green highlight frame.

### Offer cards (shop)

<p><img src="references/01_shop_offers.jpg" width="200" alt="shop offers"> <img src="references/02_shop_offer_tiers.jpg" width="200" alt="shop offer tiers"></p>

- A full-width card with a theme colour per tier (orange special, blue/navy pass, red/maroon "Treasure").
- **Left:** a big coin pile with the amount ("2000", "10 000").
- **Middle:** an inset grid of boosters with "x1/x2" and "∞ 1h".
- **Bottom band:** the offer name on the left, a green price button on the right.
- **Small tiers:** three square tiles (1000 / 2000 / 5000 coins), each with a price button below.
- A "More Offers!" cream pill with a green chevron circle sits at the bottom.

### Collection grid

<p><img src="references/17_collection_grid.jpg" width="200" alt="collection grid"></p>

- Circular medallions (a coloured disc with a gold rim) holding a 3D object.
- Each has a coloured name ribbon across its bottom, then a progress bar ("1/9") with a small reward icon at the end.
- Three columns on a hex-patterned purple background.

### Empty state

<p><img src="references/08_empty_state_free_lives.jpg" width="200" alt="empty state free lives"></p>

- A cream inset with a two-line message ("There are no lives in your inbox. Ask your teammates!") and one CTA at the bottom ("Request").
- The panel stays full size; emptiness is shown by space, not by shrinking.

### Loading / splash

<p><img src="references/16_loading_key_art.jpg" width="200" alt="loading key art"></p>

- Full-bleed seasonal key art (characters in front of the hero location).
- Logo top-left, "Loading" in outlined display text at the bottom.
- No progress bar visible (or a minimal one).

---

## 7. HUD and home screen

<p><img src="references/14_home_hud.jpg" width="200" alt="home hud"></p>

- **Top bar (overlay, no plate):** avatar tile (48 dp) → coin pill with "+" → lives pill (heart + count + "Full") → stars pill with badge → gear button.
- **Main progress bar** under the HUD: a wide meter ("0/50") with a reward icon at the end and a timer pill below.
- **Event buttons** stack in **left and right columns** over the world. Each is a 3D icon on a small pedestal plate with a timer pill underneath ("2d 13h", "13:09:28"). They are tappable objects, not flat buttons.
- **Play button:** a huge green plate at the bottom centre with the level number ("Level 318") and a red "x2" badge, in front of a character peeking over it.
- **Bottom nav** as above, with Home in the middle.

**Lesson:** the home screen is the 3D world. The UI is a ring of objects around its edge, and the centre stays clear for the scene.

---

## 8. Layout metrics (≈, at 360 dp width)

| Item | Value |
| --- | --- |
| Side margin | 8–12 dp |
| Header height | 52–60 dp |
| Bottom nav height | 60–68 dp |
| Row height | 64–72 dp; gap 6–8 dp |
| Button heights | 36 (small), 44–48 (standard), 60–68 (hero) |
| Corner radius | rows 12–14, buttons 14–18, dialogs 20–28, pills fully round |
| Outline | 2–3 dp everywhere |
| Dialog width | 88–94% of the screen |
| Close X | 40–44 dp, overlapping the frame corner by ~40% |
| Icon sizes | 24 (inline), 36–44 (rows/tabs), 64–96 (cards), 140–200 (hero reward) |
| Touch targets | never under 44 dp |

---

## 9. Motion (inferred from the style; not visible in stills)

- Dialogs pop in with an overshoot: scale 0.8 → 1.05 → 1.0 in about 250 ms.
- Buttons squash on press: down 3–4 dp, then spring back.
- Rewards fly to their HUD counter, then the counter ticks up.
- Ready items pulse gently; "!" badges bounce once on appear.
- Shine sweeps pass across gold items and premium offers every few seconds.

---

## 10. Copy and microtext patterns

- Titles are 1–2 words: "Shop", "Free Lives", "Team Treasure".
- Descriptions are one sentence that says what to do and what you get.
- Buttons are verbs: Start, Continue, Request, Help, Join, Activate, Unlock, Claim.
- Status is a single word plus a number: "Full", "Finished", "Popular", "2/5", "x2".
- Countdowns use the shortest form: "2d 13h", "13:08:06".

---

## 11. Iconography rules

- Rendered 3D, three-quarter view, warm key light from the upper left, bright rim light, saturated colours, a thin dark outline tinted to the object.
- Silhouette first: each icon must read at 24 dp as a shape.
- Nav icons are richer objects (castle, trophy, shield, cards) than action glyphs.
- Quantity and state go **on** the icon (x3, ∞ 10m, checkmark, lock), never only in a separate label.
- Avatars are character portraits inside a coloured square frame; the frame colour can carry rank or team.

---

## 12. Applying this to Farmverse

| Learned pattern | Farmverse version |
| --- | --- |
| Royal blue chrome + slate body | Sky-blue chrome + deep teal-slate body, so blue stays the "navigation" colour |
| Castle nav icons | barn (home), crate (inventory), shop, market stall, trophy (ranks) — already generated |
| Event frames by colour | Season events: spring green, harvest orange, winter blue; the marketplace uses gold |
| Lives (heart) | Water drops and food meter in the same pill pattern |
| Team requests for lives | Neighbour help requests for water or tools, same row layout |
| Area cards with progress + "Do It" | Facility cards with upgrade progress + "Upgrade" |
| Collection medallions | Crop or animal collection book (optional later feature) |
| Podium | Net-worth leaderboard podium — screen 44 |
| Daily Rewards grid + Day 7 card | Screen 47 daily bonus, same structure |
| Offer cards with $ prices | **Coins only.** No real-money prices at launch (spec rule); the same card layout sells coin bundles for coins or rewarded ads |
| "!" alert badges | Red count badges with numbers; "!" only for a required action |

**Do**

- Reuse the depth recipe on every element; consistency is what sells "3D".
- Keep each dialog to title + art + one sentence + one CTA.
- Put timers next to anything time-limited.
- Keep the world visible behind every popup.

**Don't**

- Copy the reference game's characters, logo, castle setting, booster designs or names.
- Use flat Material icons, thin outlines or pure-black shadows.
- Put body text directly on busy art without a cream plate.
- Show real-money prices or "$" in Farmverse's launch UI.

---

## 13. Prompt snippets for AI asset generation (derived from these notes)

- **Icon:** "single 3D casual mobile game icon of {subject}, chunky rounded proportions, glossy candy materials, saturated warm colours, warm key light upper-left, bright rim light, thin dark outline tinted to the object's colour, three-quarter view, centred, transparent background, no text"
- **Reward with quantity:** generate the icon only; add "x3", "∞ 10m" and similar labels in-engine as outlined text, never baked into the image.
- **Event art window:** "landscape illustration for an in-game event card, {scene}, stylized 3D, warm light, characters on the left two thirds, no text, rounded-corner safe area"
- **Key art / loading:** "portrait 9:16 key art, {hero location} at golden hour, characters in the lower half, logo space at the top, stylized 3D, no text"

---

## 14. Review checklist (use on every Farmverse screen)

- [ ] Every raised element has outline + gradient + gloss + lip + drop.
- [ ] Screen title uses the gradient display style; other text follows the outline rules.
- [ ] Colours keep their jobs: green go, red close/danger, orange secondary, purple event, gold reward.
- [ ] Exactly one primary CTA, the biggest button on the screen.
- [ ] Every time-limited thing shows a timer pill.
- [ ] Rows follow rank → avatar → name block → stat block.
- [ ] Icons are 3D renders with tinted outlines, readable at 24 dp.
- [ ] Popups keep the world visible and the HUD dimmed.
- [ ] Touch targets ≥ 44 dp; text on art sits on a plate.
- [ ] No reference-game art, names or real-money prices.

---

## 15. Visual reference gallery

Reference only: study the techniques, never copy the art, characters, names or logo.

| | | |
| --- | --- | --- |
| <img src="references/01_shop_offers.jpg" width="220"><br><sub>Shop: themed offer cards, coin tiles, price buttons</sub> | <img src="references/02_shop_offer_tiers.jpg" width="220"><br><sub>Shop: tiered offers, corner ribbon</sub> | <img src="references/03_event_dialog_reward_callout.jpg" width="220"><br><sub>Event dialog: reward callout with + chain</sub> |
| <img src="references/04_event_dialog_art_window.jpg" width="220"><br><sub>Event dialog: art window, timer, choice tiles, hero CTA</sub> | <img src="references/05_event_dialog_team.jpg" width="220"><br><sub>Team event dialog: green frame, single CTA</sub> | <img src="references/06_areas_list.jpg" width="220"><br><sub>Areas: locked card, ribbon titles, progress + Do It</sub> |
| <img src="references/07_leaderboard_teams.jpg" width="220"><br><sub>Leaderboard teams: tabs, sub-toggle, emblem rows</sub> | <img src="references/08_empty_state_free_lives.jpg" width="220"><br><sub>Empty state: message inset + one CTA</sub> | <img src="references/09_leaderboard_podium.jpg" width="220"><br><sub>Podium: gold/silver/bronze columns</sub> |
| <img src="references/10_leaderboard_podium_finished.jpg" width="220"><br><sub>Podium finished state + Continue</sub> | <img src="references/11_team_requests.jpg" width="220"><br><sub>Team: request rows, help bars, invite card</sub> | <img src="references/12_leaderboard_players.jpg" width="220"><br><sub>Players: rank circles, framed avatars, level badges</sub> |
| <img src="references/13_settings.jpg" width="220"><br><sub>Settings: grouped toggles, colour-coded buttons</sub> | <img src="references/14_home_hud.jpg" width="220"><br><sub>Home: HUD pills, event objects with timers, hero Play button</sub> | <img src="references/15_daily_rewards.jpg" width="220"><br><sub>Daily rewards: milestone track, day grid, Day 7 card</sub> |
| <img src="references/16_loading_key_art.jpg" width="220"><br><sub>Loading: full-bleed key art</sub> | <img src="references/17_collection_grid.jpg" width="220"><br><sub>Collection: medallions, ribbons, progress + reward</sub> |  |
