#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // gameplay.html's bands and strips: Build menu, Placing, the building panel (machines, animal houses,
    // stores, markets, houses, sites), a tree or rock, the seed strip, a conversation, the farmhouse rooms.
    public sealed partial class GameUi
    {
        private static readonly (string id, string label, string icon)[] PlayCats =
        {
            ("farm", "Farm", "Icon_Shovel"), ("animals", "Animals", "Icon_Egg"), ("machines", "Machines", "Icon_GearWheels"),
            ("houses", "Houses", "G_House"), ("defence", "Defence", "Icon_Shield"), ("decor", "Decor", "G_Flower"),
        };

        /// <summary>The design's six build categories over the catalogue's own.</summary>
        private static string PlayCatOf(BuildingCatalogue.Entry e)
        {
            if (e.id == "army_house" || e.id.StartsWith("fence_")) return "defence";
            return e.category switch
            {
                "Farm" or "Orchard" => "farm",
                "Animals" => "animals",
                "Factories" or "Mine" or "Sea" => "machines",
                "People" or "Town" or "Shops" or "Home" => "houses",
                _ => "decor",
            };
        }

        private string? _playTakeItem;
        private int _playTakeCount = 5;
        private string _playSeed = "wheat";

        // ------------------------------------------------------------------ build menu

        private void PlayBuildMenu(Ctx c)
        {
            var p = c.P;
            var s = Sandbox!;
            var band = PK.Box(c.Root);
            band.pickingMode = PickingMode.Position;
            PK.Abs(band, left: 0, right: 0, bottom: 0);   // as tall as its rows: header, cards, the bottom inset
            band.style.backgroundColor = GameTheme.T("#0b2a52");
            Kit.Nine(band, GameArt.Get("Popup_FullWidth03_Single_Navy"), 4, 20, 4, 20, p.U(14) / 20f);
            PK.Pad(band, p.L + p.P(20), p.P(18), p.R + p.P(20), p.B + p.P(10));

            var head = PK.Row(band);
            head.style.height = p.P(58);
            head.style.flexShrink = 0;
            foreach (var (id, label, icon) in PlayCats)
            {
                var on = id == _playBuildCat;
                var cat = PK.Row(head);
                cat.style.height = new Length(100, LengthUnit.Percent);
                PK.Pad(cat, p.P(16), 0, p.P(16), 0);
                cat.style.marginRight = p.P(8);
                Kit.Round(cat, p.U(10));
                if (on) cat.style.backgroundColor = GameTheme.Light(.12f);
                PK.Img(cat, icon, p.P(40));
                PK.T(cat, p, label, 20).style.marginLeft = p.P(8);
                if (on)
                {
                    var u = PK.Box(cat);
                    u.style.position = Position.Absolute;
                    u.style.bottom = 0;
                    u.style.left = new Length(22, LengthUnit.Percent);
                    u.style.right = new Length(22, LengthUnit.Percent);
                    u.style.height = p.U(4);
                    u.style.backgroundColor = Color.white;
                    Kit.Round(u, p.U(4), p.U(4), 0, 0);
                }

                var cid = id;
                PK.Tap(cat, p, () =>
                {
                    _playBuildCat = cid;
                    MarkDirty();
                });
            }

            PK.Grow(head);
            PlayClose(head, p, 60, 66, () =>
            {
                _staggered = null;
                _playBuildMenu = false;
                MarkDirty();
            });

            var scroll = new ScrollView(ScrollViewMode.Horizontal);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.marginTop = p.P(14);
            scroll.style.height = p.P(222);   // the cards (220) never clipped
            scroll.style.flexShrink = 0;
            scroll.contentContainer.style.flexDirection = FlexDirection.Row;
            band.Add(scroll);

            var level = s.IslandLevel;
            var entries = s.ShopEntries.Where(e => PlayCatOf(e) == _playBuildCat)
                .OrderBy(e => level >= GameplayRules.UnlockLevel(e.id) ? 0 : 1).ThenBy(e => GameplayRules.UnlockLevel(e.id)).ThenBy(e => e.price).ToList();
            var first = true;
            foreach (var e in entries)
            {
                var card = PlayBuildCard(scroll.contentContainer, p, e, level);
                if (!first) card.style.marginLeft = p.P(16);
                first = false;
            }

            if (entries.Count == 0) PK.N(scroll.contentContainer, p, "Nothing to build here yet.", 18, GameTheme.T("#bfe6ff"));
            // the cards come in one after another when the menu opens or the tab changes (not on every refresh)
            if (_staggered != "build:" + _playBuildCat) { _staggered = "build:" + _playBuildCat; Kit.Stagger(scroll.contentContainer, p.P(24)); }
        }

        /// <summary>The list last brought in one card at a time (so a refresh doesn't replay it).</summary>
        private string? _staggered;

        private VisualElement PlayBuildCard(VisualElement parent, Pt p, BuildingCatalogue.Entry e, int level)
        {
            var need = GameplayRules.UnlockLevel(e.id);
            var locked = level < need;
            var card = PK.Col(parent);
            card.style.alignItems = Align.Center;
            PK.Size(card, p.P(190), p.P(220));
            card.style.paddingTop = p.P(14);
            Kit.Nine(card, GameArt.Get("CardFrame06_Bg_Blue"), 28, 40, 28, 20, p.U(18) / 40f);

            var pic = PK.Centre(PK.Box(card));
            pic.style.height = p.P(100);
            pic.style.width = new Length(100, LengthUnit.Percent);
            pic.style.flexShrink = 0;
            var dash = PK.Inset(PK.Box(pic), p.P(24), p.P(6), p.P(24), p.P(6));
            Kit.Border(dash, p.U(3), GameTheme.Light(.35f));
            Kit.Round(dash, p.U(12));
            var img = PK.Fit(pic, PlayBuilding(e.id), p.P(96), p.P(88));
            img.style.position = Position.Relative;
            var name = PK.T(card, p, e.displayName, 20, align: TextAnchor.MiddleCenter);
            name.style.marginTop = p.P(8);
            var price = PK.Row(card);
            price.style.marginTop = p.P(10);
            if (locked)
            {
                PK.Img(price, "Icon_ImageIcon_Lock01_s", p.P(30));
                PK.T(price, p, "Lv " + need, 20).style.marginLeft = p.P(4);
                pic.style.opacity = .45f;
                name.style.opacity = .45f;
            }
            else
            {
                PK.Img(price, "ResourceBar_Icon_Gold", p.P(34));
                PK.T(price, p, PK.Num(e.price), 22).style.marginLeft = p.P(4);
            }

            PK.Tap(card, p, () =>
            {
                var s = Sandbox;
                if (s == null) return;
                if (locked)
                {
                    Say($"Opens at island level {need}", 2f);
                    IslandAudio.Play("error", null, 0.5f);
                    return;
                }

                if (s.CantBuy(e) is { } why)
                {
                    Say(why, 2.5f);
                    IslandAudio.Play("error", null, 0.5f);
                    return;
                }

                if (s.CoinsNow < e.price)
                {
                    Open("notenough", new PlayNeed("coins", e.price - s.CoinsNow, "build the " + e.displayName.ToLowerInvariant()));
                    return;
                }

                _playBuildMenu = false;
                s.Buy(e);
                MarkDirty();
            });
            return card;
        }

        // ------------------------------------------------------------------ placing

        private void PlayPlace(Ctx c)
        {
            var p = c.P;
            var s = Sandbox!;
            var placer = s.Placer!;
            var e = placer.Current;
            var B = p.B + p.P(16);

            var banner = PlayTopCentre(PK.Box(c.Root), p.P(80));
            PK.Size(banner, p.P(700), p.P(110));
            Kit.Picture(banner, GameArt.Get("Play_ActionText_Frame_Blue"));
            var words = PlayTopCentre(PK.Col(c.Root, p.P(6)), p.P(96));
            words.style.alignItems = Align.Center;
            var name = e?.displayName ?? "building";
            PK.T(words, p, placer.Moving ? $"Move your {name}" : $"Place your {name}", 30, align: TextAnchor.MiddleCenter);
            var hint = PK.N(words, p, "", 16, GameTheme.C("#dff1ff"), TextAnchor.MiddleCenter);

            var row = PK.Row(c.Root, p.P(28));
            row.style.position = Position.Absolute;
            row.style.left = new Length(50, LengthUnit.Percent);
            row.style.bottom = B + p.P(10);
            row.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            PlayRound(row, p, 100, GameTheme.C("#ff6b6b"), GameTheme.C("#d62f3a"), "x", null, 46, () =>
            {
                placer.Cancel();
                MarkDirty();
            });
            PlayRound(row, p, 100, GameTheme.T("#5fc0ff"), GameTheme.T("#1676d1"), null, "G_Rotate", 0, placer.Rotate);
            if (!placer.Moving && e != null)
            {
                var cost = PK.Row(row);
                cost.style.height = p.P(60);
                PK.Pad(cost, p.P(20), 0, p.P(20), 0);
                Kit.Round(cost, p.U(10));
                PK.Grad(cost, "ppillgold", true, (0f, GameTheme.C("#170d06")), (1f, GameTheme.C("#2b160a")));
                PK.Img(cost, "ResourceBar_Icon_Gold", p.P(46));
                PK.T(cost, p, PK.Num(e.price), 28).style.marginLeft = p.P(6);
            }

            var ok = PlayRound(row, p, 120, GameTheme.C("#7ae04a"), GameTheme.C("#2fa81c"), "v", null, 50, () =>
            {
                if (!placer.Confirm()) IslandAudio.Play("error", null, 0.5f);
                MarkDirty();
            });
            c.Live(() =>
            {
                var fits = placer.CurrentFits;
                hint.text = Loc.Of(fits ? "Drag to move · it fits here" : s.PlaceHint is { } h ? "Can't go here: " + h : "Can't go here · drag it somewhere else");
                ok.style.opacity = fits ? 1f : .5f;
            });
        }

        /// <summary>A round button with a radial fill, an ink edge and a drop shadow, holding a mark or a picture.</summary>
        private static VisualElement PlayRound(VisualElement parent, Pt p, float s, Color a, Color b, string? mark, string? art, float fs, Action onClick)
        {
            var size = p.P(s);
            var e = PK.Centre(PK.Box(parent));
            PK.Size(e, size, size);
            Kit.Round(e, size);
            Kit.Picture(e, GameArt.Radial("pround_" + ColorUtility.ToHtmlStringRGB(a), .5f, .5f, .5f, .5f, (0f, a), (1f, b)));
            Kit.Border(e, p.P(5), PK.Ink);
            PK.DropEdge(e, p.P(5) + p.P(6) * .5f, PK.Ink);
            if (mark == "x") PlayCross(e, p, fs);
            else if (mark == "v") PlayTick(e, p, fs);

            if (art != null) PK.Fit(e, art, size * 1.0f, size * 1.0f);
            PK.Tap(e, p, onClick);
            return e;
        }

        /// <summary>A stroked ✕ (two white bars with an ink edge), about fs points tall: Lilita has no ✕ glyph.</summary>
        private static void PlayCross(VisualElement parent, Pt p, float fs)
        {
            foreach (var deg in new[] { 45f, -45f })
            {
                var bar = PK.Box(parent);
                bar.style.position = Position.Absolute;
                PK.Size(bar, p.P(fs * .26f), p.P(fs * 1.05f));
                bar.style.left = new Length(50, LengthUnit.Percent);
                bar.style.top = new Length(50, LengthUnit.Percent);
                bar.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
                bar.style.backgroundColor = Color.white;
                Kit.Border(bar, p.P(4), PK.Ink);
                Kit.Round(bar, p.P(4));
                bar.style.rotate = new Rotate(new Angle(deg, AngleUnit.Degree));
            }
        }

        /// <summary>A stroked ✓ (a short and a long white bar with an ink edge).</summary>
        private static void PlayTick(VisualElement parent, Pt p, float fs)
        {
            var w = p.P(fs * .26f);
            var shortBar = PK.Box(parent);
            shortBar.style.position = Position.Absolute;
            PK.Size(shortBar, w, p.P(fs * .55f));
            shortBar.style.left = new Length(30, LengthUnit.Percent);
            shortBar.style.top = new Length(38, LengthUnit.Percent);
            var longBar = PK.Box(parent);
            longBar.style.position = Position.Absolute;
            PK.Size(longBar, w, p.P(fs * 1.0f));
            longBar.style.left = new Length(52, LengthUnit.Percent);
            longBar.style.top = new Length(22, LengthUnit.Percent);
            foreach (var (bar, deg) in new[] { (shortBar, -45f), (longBar, 38f) })
            {
                bar.style.backgroundColor = Color.white;
                Kit.Border(bar, p.P(4), PK.Ink);
                Kit.Round(bar, p.P(4));
                bar.style.rotate = new Rotate(new Angle(deg, AngleUnit.Degree));
            }
        }

        // ------------------------------------------------------------------ the building panel

        /// <summary>The left column of a building's band: name and level tag, a status line, food bar, HUNGRY.</summary>
        private VisualElement PlayInfo(Ctx c, VisualElement row, string title, int level, Func<string> status, int deptUid = -1, float width = 240)
        {
            var p = c.P;
            var col = PK.Col(row, p.P(8));
            col.style.width = p.P(width);
            col.style.flexShrink = 0;
            var head = PK.Row(col, p.P(10));
            PK.T(head, p, title, 32).style.flexShrink = 1;
            if (level > 0) PlayTag(head, p, "Lv " + level, 16, GameTheme.C("#ffd23a"), PK.Ink).style.alignSelf = Align.Center;
            var line = PK.Row(col, p.P(8));
            PK.Img(line, "Icon_Friends", p.P(26));
            var st = PK.N(line, p, "", 16, GameTheme.T("#bfe6ff"), wrap: true);
            st.style.flexShrink = 1;
            c.Live(() => st.text = Loc.Of(status()));

            var eco = Economy!;
            if (deptUid >= 0 && eco.Departments.TryGetValue(deptUid, out var d0) && d0.Levels.UpkeepPerPersonPerMinute > 0 && eco.IsBuilt(deptUid))
            {
                var food = PK.Row(col, p.P(8));
                PK.Img(food, "Icon_Energy_Yellow", p.P(28));
                var (_, fill, label) = PK.Bar(food, p, 170, 22, 0, "");
                var hungry = PlayTag(col, p, "HUNGRY", 15, GameTheme.C("#ff3a52"), Color.white);
                c.Live(() =>
                {
                    if (!Economy!.Departments.TryGetValue(deptUid, out var d)) return;
                    var perMin = Math.Max(1.0, d.UpkeepPerMinute);
                    var minutes = d.Energy / perMin;
                    var f = Mathf.Clamp01((float)(minutes / 30.0));
                    PK.SetPct(fill, f * 100f);
                    label.text = Loc.Of(Mathf.RoundToInt(f * 100f) + "%");
                    hungry.style.display = d.Starving ? DisplayStyle.Flex : DisplayStyle.None;
                    hungry.style.opacity = .55f + .45f * Mathf.Abs(Mathf.Cos(Time.unscaledTime * Mathf.PI));
                });
            }

            return col;
        }

        /// <summary>A labelled column (NEEDS / MAKING / READY) in the band.</summary>
        private static VisualElement PlaySection(VisualElement row, Pt p, string label)
        {
            var col = PK.Col(row, p.P(8));
            col.style.alignItems = Align.Center;
            col.style.flexShrink = 0;
            PK.T(col, p, label, 16, GameTheme.T("#9fd0ff"));
            return col;
        }

        private string PlayStatus(int uid)
        {
            var eco = Economy;
            if (eco == null) return "";
            if (!eco.IsBuilt(uid)) return eco.Crews.ContainsKey(uid) ? "Builders at work" : "Waiting for a builder";
            if (eco.IsRuined(uid)) return "Ruined: builders will repair it";
            if (eco.Departments.TryGetValue(uid, out var d))
            {
                if (d.People > 0) return $"{PlayPretty(IslandEconomyHost.PeopleOf(d.Levels.Kind))} {d.Present}/{d.People}{(d.Starving ? " · hungry" : "")}";
                if (d.StorageCapacity > 0) return $"Stored {d.Stored}/{d.StorageCapacity}";
                return GameplayRules.Describe(d.BuildingId);
            }

            if (eco.Machines.TryGetValue(uid, out var m)) return $"Makes {m.Recipe.Output.Replace('_', ' ')} · +{(GameplayRules.BuildingLevelSpeed(m.Level) - 1) * 100:0}% speed";
            if (eco.Animals.TryGetValue(uid, out var a)) return $"{PlayPretty(a.Producer.Item)} every {Mathf.RoundToInt((float)a.Interval)}s";
            if (eco.Plots.TryGetValue(uid, out var plot)) return plot.Crop != null ? PlayPretty(plot.Crop) + " growing" : "Empty field";
            return "Decoration";
        }

        private int PlayLevelOf(int uid)
        {
            var eco = Economy!;
            if (eco.Departments.TryGetValue(uid, out var d)) return d.Level;
            if (eco.Machines.ContainsKey(uid) || eco.Animals.ContainsKey(uid)) return eco.BuildingLevel(uid);
            return 0;
        }

        /// <summary>A tapped building: its band with what it does now and Upgrade · Send food · Move (· Sell · Look · Go inside).</summary>
        // ------------------------------------------------------------------ the building card

        /// <summary>The building whose full panel was asked for with "More" (-1: none; every other building shows its card).</summary>
        private int _playFullUid = -1;

        /// <summary>The building a view is about, if it's one that shows a card (tapped, or the station the farmer stands at).</summary>
        private static int PlayCardUid(string view)
        {
            if (!(view.StartsWith("b:") || view.StartsWith("machine:") || view.StartsWith("animals:") || view.StartsWith("market:") || view.StartsWith("store:"))) return -1;
            var parts = view.Split(':');
            return parts.Length > 1 && int.TryParse(parts[1], out var uid) ? uid : -1;
        }

        /// <summary>
        /// A small card floating over the building: name, level, what it's doing, two quick actions and More (the
        /// full panel). The stick and the buttons stay, so the farmer keeps moving; the card follows the building on
        /// screen, and a tapped building's card closes by itself once the farmer walks about 12 m away.
        /// </summary>
        private void PlayCard(Ctx c, int uid, bool tapped)
        {
            var p = c.P;
            var s = Sandbox!;
            var eco = Economy!;
            var host = Host!;
            var building = host.Building(uid) ?? s.Selected;
            if (building == null) return;
            var name = IslandEconomyHost.NameOf(building.id);
            var w = p.P(340);

            var cardEl = PK.Col(c.Root, p.P(6));
            cardEl.style.position = Position.Absolute;
            cardEl.style.width = w;
            PK.Pad(cardEl, p.P(16), p.P(12), p.P(16), p.P(14));
            cardEl.style.backgroundColor = GameTheme.T("#062a5c", .92f);
            Kit.Round(cardEl, p.U(18));
            Kit.Border(cardEl, p.U(4), GameTheme.T("#0b1d3a"));
            cardEl.pickingMode = PickingMode.Position;
            // the little pointer under the card, at the building
            var tip = PK.Box(cardEl);
            PK.Size(tip, p.P(26), p.P(16));
            tip.style.position = Position.Absolute;
            tip.style.bottom = -p.P(15);
            tip.style.left = w / 2f - p.P(13);
            Kit.Picture(tip, PlayTriangle(GameTheme.T("#062a5c")));
            tip.style.rotate = new Rotate(180);

            var head = PK.Row(cardEl, p.P(8));
            PK.T(head, p, name, 24).style.flexShrink = 1;
            var lvl = PlayLevelOf(uid);
            if (lvl > 0) PlayTag(head, p, "Lv " + lvl, 14, GameTheme.C("#ffd23a"), PK.Ink).style.alignSelf = Align.Center;
            PK.Grow(head);
            if (tapped)
            {
                var x = PK.Centre(PK.Box(head));
                PK.Size(x, p.P(40), p.P(40));
                PK.Img(x, "Icon_PictoIcon_Close", p.P(22));
                PK.Tap(x, p, () => { s.Deselect(); MarkDirty(); }, "whoosh");
            }
            var st = PK.N(cardEl, p, "", 16, GameTheme.T("#bfe6ff"), wrap: true);
            c.Live(() => st.text = Loc.Of(PlayStatus(uid)));

            // two quick actions at most, the rest behind More
            var acts = new List<(string colour, string label, Action act)>();
            if (s.Selected == building && s.CanGoInside) acts.Add(("green", "Go inside", () => { s.GoInside(); MarkDirty(); }));
            if (eco.IsBuilt(uid) && (eco.CanLevel(uid) || eco.UpgradeCost(uid) != null)) acts.Add(("yellow", "Upgrade", () => Open("upgrade", uid)));
            if (eco.IsBuilt(uid) && eco.WorkJobs(uid).Count > 0) acts.Add(("blue", "Workers", () => { if (building.id == "workers_house") Open("workers"); else Open("workjobs", uid); }));
            var row = PK.Row(cardEl, p.P(8));
            row.style.marginTop = p.P(6);
            foreach (var (colour, label, act) in acts.Take(2)) PK.Btn(row, p, colour, label, 0, 46, 17, act).style.flexGrow = 1;
            PK.Btn(row, p, "gray", "More", 0, 46, 17, () =>
            {
                _playFullUid = uid;   // the full panel (the building's, or the station's where the farmer stands)
                MarkDirty();
            }).style.flexGrow = 1;

            var openedAt = s.Farmer != null ? s.Farmer.position : Vector3.zero;
            c.Live(() =>
            {
                var cam = Camera.main;
                var panel = cardEl.panel;
                if (cam == null || panel == null || building == null) return;
                // over the top of the building, kept clear of the HUD's top bar and bottom buttons
                var top = building.transform.position + Vector3.up * PlayCardHeight(building);
                var sp = cam.WorldToScreenPoint(top);
                var at = sp.z > .1f ? RuntimePanelUtils.CameraTransformWorldToPanel(panel, top, cam) : new Vector2(p.W / 2f, p.H / 2f);
                var h = cardEl.resolvedStyle.height;
                if (float.IsNaN(h) || h < 1f) h = p.P(170);
                var x = Mathf.Clamp(at.x - w / 2f, p.L + p.P(12), p.W - p.R - w - p.P(12));
                var y = Mathf.Clamp(at.y - h - p.P(18), p.T + p.P(130), p.H - p.B - h - p.P(250));
                cardEl.style.left = x;
                cardEl.style.top = y;
                tip.style.left = Mathf.Clamp(at.x - x - p.P(13), p.P(18), w - p.P(44));
                // walked away from a tapped building: the card goes
                var farmer = s.Farmer;
                if (tapped && farmer != null && s.Selected == building)
                {
                    var away = Vector3.Distance(new Vector3(farmer.position.x, 0, farmer.position.z), new Vector3(building.transform.position.x, 0, building.transform.position.z));
                    var moved = (farmer.position - openedAt).sqrMagnitude > 4f;
                    if (moved && away > 12f) { s.Deselect(); MarkDirty(); }
                }
            });
            if (_playCardShown != uid) Kit.Enter(cardEl, "popup", p.W);   // pops in once, not on every refresh
            _playCardShown = uid;
            c.Live(() => { });
        }

        private int _playCardShown = -1;

        /// <summary>The height of a building's roof above its base (from its renderers), cached per model.</summary>
        private static float PlayCardHeight(PlacedBuilding b)
        {
            if (PlayHeights.TryGetValue(b, out var h)) return h;
            var rs = b.GetComponentsInChildren<Renderer>();
            h = 3f;
            if (rs.Length > 0)
            {
                var bounds = rs[0].bounds;
                foreach (var r in rs) bounds.Encapsulate(r.bounds);
                h = Mathf.Max(1.5f, bounds.max.y - b.transform.position.y);
            }
            PlayHeights[b] = h;
            return h;
        }

        private static readonly Dictionary<PlacedBuilding, float> PlayHeights = new Dictionary<PlacedBuilding, float>();

        private void PlayBuilding(Ctx c, PlacedBuilding b)
        {
            var p = c.P;
            var s = Sandbox!;
            var eco = Economy!;
            var uid = b.uid;
            var name = s.SelectedEntry?.displayName ?? IslandEconomyHost.NameOf(b.id);
            var row = PlayBand(c.Root, p, p.Phone ? 330 : 300, () =>
            {
                _playFullUid = -1;
                s.Deselect();
                MarkDirty();
            });
            PlayInfo(c, row, name, PlayLevelOf(uid), () => PlayStatus(uid), uid);
            PlayMiddle(c, row, uid, b.id);
            PK.Grow(row);

            var buttons = new List<(string colour, string? icon, string label, float h, float fs, Action act, Func<bool>? on)>();
            if (eco.IsBuilt(uid) && (eco.CanLevel(uid) || eco.UpgradeCost(uid) != null))
                buttons.Add(("yellow", null, "Upgrade", 64, 26, () => Open("upgrade", uid), null));
            if (eco.Departments.TryGetValue(uid, out var dept) && dept.Levels.UpkeepPerPersonPerMinute > 0 && eco.IsBuilt(uid))
            {
                var ordered = eco.SupplyOrders.ContainsKey(uid);
                buttons.Add(("blue", "Icon_Energy_Yellow", ordered ? "Stop food" : "Send food", 60, 22, () => { Host!.SendSupplies(uid, !ordered); MarkDirty(); }, null));
            }

            if (eco.IsBuilt(uid) && eco.WorkJobs(uid).Count > 0)
                buttons.Add(("blue", null, "Workers", 54, 20, () => { if (b.id == "workers_house") Open("workers"); else Open("workjobs", uid); }, null));
            buttons.Add(("gray", null, "Move", 54, 20, () => { s.MoveSelected(); MarkDirty(); }, null));
            if (s.CanGoInside) buttons.Add(("green", null, "Go inside", 54, 20, () => { s.GoInside(); MarkDirty(); }, null));
            if (s.CanRestyle) buttons.Add(("purple", null, b.alt ? "New look" : "Classic look", 54, 20, () => { s.RestyleSelected(); MarkDirty(); }, null));
            if (s.CanSell) buttons.Add(("red", "ResourceBar_Icon_Gold", "Sell " + PK.Num(s.SellValue), 54, 20, () => Open("sellbuilding"), null));
            if (s.CanSell && ChainReady) buttons.Add(("purple", null, "Mint NFT", 54, 20, () => ChainMintBuilding(uid, b.id), null));

            // the design's column of three; more go into a second column beside it
            var cols = new List<VisualElement>();
            for (var i = 0; i < buttons.Count; i++)
            {
                if (i % 3 == 0)
                {
                    var col = PK.Col(row, p.P(12));
                    col.style.justifyContent = Justify.Center;
                    col.style.marginLeft = p.P(16);
                    cols.Add(col);
                }

                var (colour, icon, label, h, fs, act, _) = buttons[i];
                PlayBtn(cols[cols.Count - 1], p, colour, icon, label, 220, h, fs, act);
            }
        }

        /// <summary>The middle of a building's band: what it is doing (site, health, machine, animals, plot, store).</summary>
        private void PlayMiddle(Ctx c, VisualElement row, int uid, string buildingId)
        {
            var p = c.P;
            var eco = Economy!;
            var mid = PK.Row(row, p.P(26));
            mid.style.marginLeft = p.P(26);
            mid.style.alignItems = Align.Center;

            if (!eco.IsBuilt(uid))
            {
                var col = PlaySection(mid, p, "BUILDING");
                var (_, fill, label) = PK.Bar(col, p, 320, 30, 0, "");
                var note = PK.N(col, p, "", 14, GameTheme.T("#9fd0ff"));
                c.Live(() =>
                {
                    var site = Economy!.Sites.FirstOrDefault(x => x.Uid == uid && !x.Repair);
                    var pr = (float)(site?.Progress ?? 0);
                    PK.SetPct(fill, pr * 100f);
                    label.text = Loc.Of(Mathf.RoundToInt(pr * 100f) + "%");
                    note.text = Loc.Of(site != null && site.Missing.Count > 0 ? "Needs " + string.Join(", ", site.Missing.Select(m => $"{m.Value} {m.Key.Replace('_', ' ')}")) : "Builders walk over with what it needs");
                });
                return;
            }

            if (eco.Healths.TryGetValue(uid, out var h0) && (h0.Damaged || h0.Ruined))
            {
                var col = PlaySection(mid, p, h0.Ruined ? "RUINED" : "DAMAGED");
                var (_, fill, label) = PK.Bar(col, p, 260, 26, 0, "");
                fill.style.unityBackgroundImageTintColor = GameTheme.C("#ff6b6b");
                c.Live(() =>
                {
                    if (!Economy!.Healths.TryGetValue(uid, out var h)) return;
                    var f = Mathf.Clamp01((float)(h.Hp / h.Max));
                    PK.SetPct(fill, f * 100f);
                    label.text = Loc.Of(Mathf.RoundToInt(f * 100f) + "%");
                });
            }

            if (eco.Machines.ContainsKey(uid)) PlayMachineFlow(c, mid, uid);
            else if (eco.Animals.ContainsKey(uid)) PlayAnimalFlow(c, mid, uid);
            else if (eco.Plots.TryGetValue(uid, out var plot))
            {
                var col = PlaySection(mid, p, "FIELD");
                var (slot, count) = PlayInSlot(col, p, plot.Crop != null ? PlayGood(plot.Crop) : "Icon_Shovel", "", 96, 56);
                PK.N(col, p, plot.Crop != null ? "Growing: walk in when it's ripe" : "Walk onto it to plant", 14, GameTheme.T("#9fd0ff"));
            }
            else if (eco.Departments.TryGetValue(uid, out var d) && d.StorageCapacity > 0)
            {
                var col = PlaySection(mid, p, "STORED");
                var goods = PK.Row(col, p.P(10));
                foreach (var kv in d.Stock.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(5))
                    PlayInSlot(goods, p, PlayGood(kv.Key), kv.Value.ToString(), 96, 56);
                if (!d.Stock.Any(kv => kv.Value > 0)) PK.N(col, p, "Empty: bring your harvest in", 14, GameTheme.T("#9fd0ff"));
            }
            else
            {
                var about = GameplayRules.Describe(buildingId);
                if (about.Length > 0)
                {
                    var t = PK.N(mid, p, about, 16, GameTheme.T("#dff1ff"), wrap: true);
                    t.style.maxWidth = p.P(420);
                }
            }
        }

        /// <summary>NEEDS (the hopper) → time → MAKING (progress) → READY (what it made).</summary>
        private void PlayMachineFlow(Ctx c, VisualElement mid, int uid)
        {
            var p = c.P;
            var m = Economy!.Machines[uid];
            var needs = PlaySection(mid, p, "NEEDS");
            var slots = PK.Row(needs, p.P(10));
            var hopper = new List<(string item, int count, Label q)>();
            foreach (var (item, count) in m.Recipe.Inputs)
            {
                var (_, q) = PlayInSlot(slots, p, PlayGood(item), "0", 96, 56);
                hopper.Add((item, count, q));
            }

            PK.N(needs, p, string.Join(" + ", m.Recipe.Inputs.Select(i => $"{i.count} {i.item.Replace('_', ' ')}")) + " → 1 " + m.Recipe.Output.Replace('_', ' '), 14, GameTheme.T("#9fd0ff"));

            var arrowCol = PK.Col(mid, p.P(8));
            arrowCol.style.alignItems = Align.Center;
            PlayArrow(arrowCol, p, 32, 44);
            var timer = PK.T(arrowCol, p, "", 20);

            var making = PlaySection(mid, p, "MAKING");
            var (box, _) = PlayInSlot(making, p, PlayGood(m.Recipe.Output), "", 120, 70, gold: true);
            var barHolder = PK.Abs(PK.Box(box), left: p.P(10), right: p.P(10), bottom: p.P(8));
            var (bar, fill, _) = PK.Bar(barHolder, p, 0, 18, 0, "");
            bar.style.width = new Length(100, LengthUnit.Percent);

            var ready = PlaySection(mid, p, "READY");
            var (_, readyCount) = PlayInSlot(ready, p, PlayGood(m.Recipe.Output), "0", 96, 56);
            PK.N(ready, p, "Walk in to collect", 14, GameTheme.T("#9fd0ff"));

            c.Live(() =>
            {
                if (!Economy!.Machines.TryGetValue(uid, out var mm)) return;
                var now = IslandEconomyHost.Clock;
                foreach (var (item, count, q) in hopper)
                {
                    var n = mm.HopperOf(item);
                    q.text = Loc.Of(n.ToString());
                    q.style.color = n < count ? GameTheme.C("#ff7a8c") : Color.white;
                }

                var pr = mm.Busy ? (float)mm.Progress(now) : 0f;
                PK.SetPct(fill, pr * 100f);
                timer.text = Loc.Of(mm.Busy ? PlayClock(Math.Max(0, mm.BusyUntil - now)) : "--:--");
                readyCount.text = Loc.Of($"{mm.Output}");
            });
        }

        /// <summary>An animal house: what's ready, the trough, side goods.</summary>
        private void PlayAnimalFlow(Ctx c, VisualElement mid, int uid)
        {
            var p = c.P;
            var a = Economy!.Animals[uid];
            var item = a.Producer.Item;
            if (a.Producer.Eats)
            {
                var feed = PlaySection(mid, p, "TROUGH");
                var (_, fq) = PlayInSlot(feed, p, PlayGood("feed"), "0", 96, 56);
                PK.N(feed, p, "Bring grain to feed them", 14, GameTheme.T("#9fd0ff"));
                c.Live(() =>
                {
                    if (!Economy!.Animals.TryGetValue(uid, out var h)) return;
                    var now = IslandEconomyHost.Clock;
                    fq.text = Loc.Of($"{h.FeedLeft(now)}");
                    fq.style.color = h.Hungry(now) ? GameTheme.C("#ff7a8c") : Color.white;
                });
                PlayArrow(PK.Col(mid), p, 32, 44);
            }

            var ready = PlaySection(mid, p, "READY");
            var (_, rq) = PlayInSlot(ready, p, PlayGood(item), "0", 120, 70, gold: true);
            var line = PK.N(ready, p, "", 14, GameTheme.T("#9fd0ff"));
            c.Live(() =>
            {
                if (!Economy!.Animals.TryGetValue(uid, out var h)) return;
                var now = IslandEconomyHost.Clock;
                rq.text = Loc.Of($"{h.StockAt(now)}/{h.Max}");
                line.text = Loc.Of(h.Side is { } side ? $"Also {side.Producer.Item.Replace('_', ' ')}: {side.StockAt(now)}/{side.Producer.Max}" :
                    GameplayRules.Perishable(item) ? $"{PlayPretty(item)} goes off if left too long" : "Walk in to collect");
            });
        }

        // ------------------------------------------------------------------ stations (where the farmer stands)

        private void PlayMachine(Ctx c, int site)
        {
            var eco = Economy!;
            var m = eco.Machines[site];
            var row = PlayBand(c.Root, c.P, c.P.Phone ? 330 : 300, PlayDismissStation);
            PlayInfo(c, row, IslandEconomyHost.NameOf(m.BuildingId), m.Level, () => PlayStatus(site));
            var mid = PK.Row(row, c.P.P(26));
            mid.style.marginLeft = c.P.P(26);
            mid.style.alignItems = Align.Center;
            PlayMachineFlow(c, mid, site);
            PK.Grow(row);
            PlayStationButtons(c, row, site);
        }

        private void PlayAnimals(Ctx c, int site)
        {
            var eco = Economy!;
            var a = eco.Animals[site];
            var row = PlayBand(c.Root, c.P, c.P.Phone ? 330 : 300, PlayDismissStation);
            PlayInfo(c, row, IslandEconomyHost.NameOf(a.BuildingId), a.Level, () => PlayStatus(site));
            var mid = PK.Row(row, c.P.P(26));
            mid.style.marginLeft = c.P.P(26);
            mid.style.alignItems = Align.Center;
            PlayAnimalFlow(c, mid, site);
            PK.Grow(row);
            PlayStationButtons(c, row, site);
        }

        private void PlayStationButtons(Ctx c, VisualElement row, int uid)
        {
            var p = c.P;
            var col = PK.Col(row, p.P(12));
            col.style.justifyContent = Justify.Center;
            if (Economy!.CanLevel(uid) || Economy.UpgradeCost(uid) != null) PlayBtn(col, p, "yellow", null, "Upgrade", 220, 64, 26, () => Open("upgrade", uid));
        }

        private void PlayMarket(Ctx c, int site)
        {
            var p = c.P;
            var eco = Economy!;
            var shop = eco.Departments[site];
            var row = PlayBand(c.Root, p, p.Phone ? 330 : 300, PlayDismissStation);
            PlayInfo(c, row, IslandEconomyHost.NameOf(shop.BuildingId), shop.Level, () => GameplayRules.Describe(shop.BuildingId), width: 300);
            var mid = PK.Col(row, p.P(8));
            mid.style.marginLeft = p.P(26);
            mid.style.flexGrow = 1;
            PK.T(mid, p, "SELLS FOR", 16, GameTheme.T("#9fd0ff"));
            var list = PK.Row(mid, p.P(12));
            list.style.flexWrap = Wrap.Wrap;
            var carried = eco.Basket.Stacks.Select(st => st.Item).Distinct().Where(i => eco.MarketPrice(shop.BuildingId, i) > 0).Take(6).ToList();
            if (carried.Count == 0) PK.N(mid, p, "Nothing you carry sells here.", 16, GameTheme.T("#dff1ff"));
            foreach (var item in carried)
            {
                var it = item;
                var col = PK.Col(list, p.P(4));
                col.style.alignItems = Align.Center;
                col.style.backgroundColor = PK.Ink;
                Kit.Round(col, p.U(12));
                PK.Pad(col, p.P(10), p.P(8), p.P(10), p.P(8));
                col.style.minWidth = p.P(110);
                col.style.marginBottom = p.P(8);
                PK.Img(col, PlayGood(it), p.P(44));
                var pr = PK.Row(col, p.P(4));
                PK.Img(pr, "ResourceBar_Icon_Gold", p.P(24));
                var price = PK.T(pr, p, "", 18);
                var note = PK.N(col, p, "", 12, GameTheme.C("#ff9fb0"));
                c.Live(() =>
                {
                    price.text = Loc.Of(Economy!.MarketPrice(shop.BuildingId, it).ToString());
                    var f = Economy.MarketFactor(it);
                    note.text = Loc.Of(f < .95 ? $"flooded {f:P0}" : PlayPretty(it));
                    note.style.color = f < .95 ? GameTheme.C("#ff9fb0") : GameTheme.T("#9fd0ff");
                });
            }

            PK.N(mid, p, "Walk onto the stall to sell. Selling lots of one good lowers its price for a while.", 14, GameTheme.T("#9fd0ff"), wrap: true);
        }

        private List<KeyValuePair<string, int>> PlayStoreGoods(int uid)
        {
            var eco = Economy;
            if (eco == null || !eco.Departments.TryGetValue(uid, out var d)) return new List<KeyValuePair<string, int>>();
            var store = d.Levels.Kind is DepartmentKind.Farmhouse or DepartmentKind.Silo && eco.Working(uid);
            return store ? d.Stock.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(8).ToList() : new List<KeyValuePair<string, int>>();
        }

        /// <summary>A store or house door: its stock to take out (pick, − n +, All, Take) and its upgrade.</summary>
        private void PlayStore(Ctx c, int site)
        {
            var p = c.P;
            var eco = Economy!;
            var dept = eco.Departments[site];
            var row = PlayBand(c.Root, p, p.Phone ? 330 : 300, PlayDismissStation);
            PlayInfo(c, row, IslandEconomyHost.NameOf(dept.BuildingId), dept.Level, () => PlayStatus(site), site);
            var goods = PlayStoreGoods(site);
            var mid = PK.Col(row, p.P(10));
            mid.style.marginLeft = p.P(26);
            mid.style.flexGrow = 1;
            if (goods.Count > 0)
            {
                var head = PK.T(mid, p, "TAKE OUT", 16, GameTheme.T("#9fd0ff"));
                if (_playTakeItem == null || goods.All(g => g.Key != _playTakeItem)) _playTakeItem = goods[0].Key;
                var slots = PK.Row(mid, p.P(10));
                var marks = new List<(string item, VisualElement slot, Label q)>();
                foreach (var g in goods.Take(p.Phone ? 5 : 7))
                {
                    var item = g.Key;
                    var (slot, q) = PlayInSlot(slots, p, PlayGood(item), g.Value.ToString(), 86, 50);
                    PK.Tap(slot, p, () => _playTakeItem = item);
                    marks.Add((item, slot, q));
                }

                var take = PK.Row(mid, p.P(12));
                var minus = PK.Btn(take, p, "gray", "-", 60, 54, 26, () => _playTakeCount = Math.Max(1, _playTakeCount - (_playTakeCount > 10 ? 5 : 1)));
                var value = PK.T(take, p, "", 26);
                value.style.minWidth = p.P(50);
                value.style.unityTextAlign = TextAnchor.MiddleCenter;
                PK.Btn(take, p, "gray", "+", 60, 54, 26, () => _playTakeCount += _playTakeCount >= 10 ? 5 : 1);
                PK.Btn(take, p, "blue", "All", 100, 54, 22, () => _playTakeCount = 9999);
                var takeBtn = PK.Btn(take, p, "green", "Take", 170, 54, 22, () =>
                {
                    if (_playTakeItem != null) Host!.TakeOutHere(_playTakeItem, _playTakeCount);
                });
                var takeText = takeBtn.Q<Label>();
                c.Live(() =>
                {
                    var e = Economy!;
                    if (!e.Departments.TryGetValue(site, out var d)) return;
                    var have = _playTakeItem != null && d.Stock.TryGetValue(_playTakeItem, out var n) ? n : 0;
                    var most = Math.Max(1, Math.Min(have, e.Basket.Free));
                    _playTakeCount = Mathf.Clamp(_playTakeCount, 1, most);
                    value.text = Loc.Of(_playTakeCount.ToString());
                    foreach (var (item, slot, q) in marks)
                    {
                        Kit.Border(slot, p.U(3), item == _playTakeItem ? GameTheme.C("#ffd23a") : Color.clear);
                        q.text = Loc.Of((d.Stock.TryGetValue(item, out var k) ? k : 0).ToString());
                    }

                    if (takeText != null) takeText.text = Loc.Of(e.Basket.Free > 0 ? $"Take {_playTakeCount}" : "Basket full");
                    PlayEnable(takeBtn, e.Basket.Free > 0);
                    head.text = Loc.Of(Host != null && Host.KeepingBasketHere ? "TAKE OUT · your basket stays on your back here" : "TAKE OUT");
                });
            }
            else PK.N(mid, p, GameplayRules.Describe(dept.BuildingId), 16, GameTheme.T("#dff1ff"), wrap: true);

            var col = PK.Col(row, p.P(12));
            col.style.justifyContent = Justify.Center;
            if (eco.UpgradeCost(site) != null) PlayBtn(col, p, "yellow", null, "Upgrade", 220, 64, 26, () => Open("upgrade", site));
        }

        // ------------------------------------------------------------------ a tree, bush or rock

        private void PlayProp(Ctx c, NatureProp prop)
        {
            var p = c.P;
            var s = Sandbox!;
            var yields = IslandEconomyHost.YieldOf(prop.kind);
            var row = PlayBand(c.Root, p, p.Phone ? 300 : 250, () => { s.Deselect(); MarkDirty(); });
            var col = PK.Col(row, p.P(8));
            col.style.width = p.P(360);
            PK.T(col, p, prop.displayName, 32);
            PK.N(col, p, $"Stop beside it to chop it for {yields.Replace('_', ' ')}, free.", 16, GameTheme.T("#bfe6ff"), wrap: true);
            PK.N(col, p, "Or pay to clear the land at once.", 16, GameTheme.T("#bfe6ff"), wrap: true);
            var gives = PlaySection(row, p, "GIVES");
            gives.style.marginLeft = p.P(26);
            PlayInSlot(gives, p, PlayGood(yields), "", 96, 56);
            PK.Grow(row);
            var bcol = PK.Col(row, p.P(12));
            bcol.style.justifyContent = Justify.Center;
            var clear = PlayBtn(bcol, p, "yellow", "ResourceBar_Icon_Gold", "Clear " + PK.Num(prop.clearCost), 260, 64, 24, () =>
            {
                if (s.CoinsNow < prop.clearCost)
                {
                    Open("notenough", new PlayNeed("coins", prop.clearCost - s.CoinsNow, "clear the " + prop.displayName.ToLowerInvariant()));
                    return;
                }

                s.ClearSelectedProp();
                MarkDirty();
            });
        }

        // ------------------------------------------------------------------ the seed strip

        private void PlayPlant(Ctx c)
        {
            var p = c.P;
            var strip = PK.Panel(c.Root, p);
            strip.pickingMode = PickingMode.Position;
            PK.Abs(strip, top: p.P(80), width: p.P(p.Phone ? 360 : 330), right: p.R + p.P(16), bottom: p.B + p.P(16));
            PK.Pad(strip, p.P(16), p.P(16), p.P(16), p.P(16));
            var head = PK.Row(strip);
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.flexShrink = 0;
            PK.T(head, p, "Plant", 28);
            PlayClose(head, p, 50, 55, PlayDismissStation);
            if (!p.Phone)
            {
                var hint = PK.N(strip, p, "Pick a seed to sow this field.", 14, GameTheme.T("#9fd0ff"));
                hint.style.marginTop = p.P(4);
            }

            var season = Economy!.SeasonNow(IslandEconomyHost.Clock);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.flexGrow = 1;
            scroll.style.marginTop = p.P(10);
            strip.Add(scroll);
            var first = true;
            var nextCrop = GameplayRules.Crops.Where(c => !Economy.CropOpen(c.Id)).OrderBy(c => GameplayRules.CropUnlockLevel(c.Id)).FirstOrDefault();
            if (nextCrop != null)
                PK.N(strip, p, $"{PlayPretty(nextCrop.Id)} opens at island level {GameplayRules.CropUnlockLevel(nextCrop.Id)}", 14, GameTheme.T("#9fd0ff")).style.marginTop = p.P(4);
            foreach (var crop in GameplayRules.Crops.Where(c => Economy.CropOpen(c.Id)))
            {
                var cr = crop;
                var sel = cr.Id == _playSeed;
                var r = PK.Row(scroll.contentContainer, p.P(12));
                r.style.height = p.P(p.Phone ? 76 : 100);
                r.style.flexShrink = 0;
                PK.Pad(r, p.P(12), 0, p.P(12), 0);
                if (!first) r.style.marginTop = p.P(10);
                first = false;
                if (sel) PK.Grad(r, "pcropsel", true, (0f, GameTheme.T("#27a9ff")), (1f, GameTheme.T("#1a8de8")));
                else PK.Grad(r, "pcrop", true, (0f, GameTheme.T("#1677cf")), (1f, GameTheme.T("#0f65b5")));
                Kit.Border(r, p.U(3), sel ? Color.white : PK.Ink);
                PK.DropEdge(r, p.U(sel ? 3 : 4), sel ? Color.white : GameTheme.T("#0a4c8f"));
                Kit.Round(r, p.U(14));
                PK.Fit(r, PlayGood(cr.Id), p.P(62), p.P(62));
                var words = PK.Col(r, p.P(6));
                words.style.flexGrow = 1;
                PK.T(words, p, PlayPretty(cr.Id), 22);
                var t = PK.Row(words, p.P(6));
                PK.Img(t, "Icon_PictoIcon_Timer", p.P(20));
                PK.N(t, p, PlayClock(cr.GrowSeconds), 15, GameTheme.C("#dff1ff"));
                var price = PK.Row(r, p.P(4));
                PK.Img(price, "ResourceBar_Icon_Gold", p.P(34));
                PK.T(price, p, cr.SeedCost.ToString(), 22);
                PK.Tap(r, p, () =>
                {
                    _playSeed = cr.Id;
                    if (Economy!.Coins < cr.SeedCost)
                    {
                        Open("notenough", new PlayNeed("coins", cr.SeedCost - Economy.Coins, "plant " + cr.Id));
                        return;
                    }

                    if (Host!.PlantHere(cr.Id)) MarkDirty();
                    else IslandAudio.Play("error", null, 0.5f);
                });
                c.Live(() => r.style.opacity = Economy != null && Economy.Coins >= cr.SeedCost ? 1f : .6f);
            }

            var foot = PK.N(strip, p, $"{season.Name}: {season.Hint}", 13, GameTheme.T("#9fd0ff"), wrap: true);
            foot.style.marginTop = p.P(8);
        }

        // ------------------------------------------------------------------ a conversation

        // the conversation so far with whoever the farmer is talking to (kept across rebuilds; reset per person)
        private readonly List<(bool mine, string text)> _playChat = new List<(bool, string)>();
        private string _playChatWith = "";

        /// <summary>
        /// Talking to a townsperson, as a chat: their portrait and name on the left, the conversation in the
        /// middle (their lines on the left in white bubbles, the farmer's questions on the right in green), and
        /// the questions you can ask as quick-reply chips underneath.
        /// </summary>
        private void PlayTalk(Ctx c)
        {
            var p = c.P;
            var life = Life!;
            if (_playChatWith != life.TalkName || _playChat.Count == 0)
            {
                _playChatWith = life.TalkName;
                _playChat.Clear();
                if (!string.IsNullOrEmpty(life.TalkAnswer)) _playChat.Add((false, life.TalkAnswer));
            }

            var row = PlayBand(c.Root, p, p.Phone ? 330 : 300, PlayTalkBye);

            // who: portrait and name
            var who = PK.Col(row, p.P(8));
            who.style.alignItems = Align.Center;
            who.style.alignSelf = Align.Center;
            var face = PK.Centre(PK.Box(who));
            PK.Size(face, p.P(150), p.P(150));
            face.style.backgroundColor = PK.Ink;
            Kit.Round(face, p.P(150));
            Kit.Border(face, p.U(4), GameTheme.C("#ffd23a"));
            PK.Clip(face);
            PK.Fit(face, PlayPerson(life.TalkWho), p.P(136), p.P(136));
            PlayTag(who, p, life.TalkName, 18, GameTheme.C("#ffd23a"), PK.Ink);

            // the chat
            var col = PK.Col(row, p.P(10));
            col.style.marginLeft = p.P(26);
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            var log = new ScrollView(ScrollViewMode.Vertical);
            log.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            log.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            log.style.flexGrow = 1;
            log.style.flexShrink = 1;
            log.style.minHeight = p.P(60);
            col.Add(log);

            var shownCount = -1;
            string? shownAnswer = null;
            void Draw()
            {
                log.contentContainer.Clear();
                foreach (var (mine, text) in _playChat) PlayTalkBubble(log.contentContainer, p, mine, text);
                log.schedule.Execute(() => log.scrollOffset = new Vector2(0, float.MaxValue));   // newest at the bottom
            }

            c.Live(() =>
            {
                var l = Life;
                if (l == null) return;
                // a new answer arrives after a question: add it as their reply
                var ans = l.TalkAnswer ?? "";
                if (_playChat.Count > 0 && _playChat[_playChat.Count - 1].mine && !string.IsNullOrEmpty(ans) && ans != shownAnswer)
                    _playChat.Add((false, ans));
                shownAnswer = ans;
                if (_playChat.Count == shownCount) return;
                shownCount = _playChat.Count;
                while (_playChat.Count > 12) _playChat.RemoveAt(0);
                Draw();
            });

            // quick replies
            var qs = PK.Row(col, p.P(10));
            qs.style.flexWrap = Wrap.Wrap;
            qs.style.flexShrink = 0;
            var questions = life.TalkQuestions;
            for (var i = 0; i < questions.Count; i++)
            {
                var index = i;
                var q = questions[i];
                PlayTalkChip(qs, p, q, false, () =>
                {
                    var l = Life;
                    if (l == null) return;
                    _playChat.Add((true, q));
                    shownAnswer = null;   // whatever they say next is the reply, even a line they said before
                    l.Ask(index);
                });
            }

            PlayTalkChip(qs, p, "Bye", true, PlayTalkBye);
        }

        private void PlayTalkBye()
        {
            _playChat.Clear();
            _playChatWith = "";
            Life?.SayBye();
        }

        /// <summary>One chat line: theirs on the left (cream, tail at the bottom left), the farmer's on the right (green, tail bottom right).</summary>
        private static void PlayTalkBubble(VisualElement parent, Pt p, bool mine, string text)
        {
            var line = PK.Row(parent);
            line.style.justifyContent = mine ? Justify.FlexEnd : Justify.FlexStart;
            line.style.alignItems = Align.FlexEnd;
            line.style.marginBottom = p.P(8);
            line.style.flexShrink = 0;

            var bubble = PK.Box(line);
            bubble.style.maxWidth = new Length(78, LengthUnit.Percent);
            bubble.style.backgroundColor = mine ? GameTheme.C("#5ad13c") : GameTheme.C("#fff8ea");
            Kit.Border(bubble, p.U(3), PK.Ink);
            var r = p.P(20);
            Kit.Round(bubble, r, r, mine ? p.P(4) : r, mine ? r : p.P(4));   // the corner by the tail stays sharp
            PK.DropEdge(bubble, p.U(5), PK.Ink);
            PK.Pad(bubble, p.P(16), p.P(9), p.P(16), p.P(10));
            var t = PK.N(bubble, p, mine ? text : "\u201c" + text + "\u201d", 19, mine ? Color.white : PK.Ink, wrap: true);
            if (mine)
            {
                t.style.unityTextOutlineWidth = p.U(1.5f);
                t.style.unityTextOutlineColor = GameTheme.C("#1d6b1a");
            }
        }

        /// <summary>A quick-reply chip (a rounded outline pill); Bye is the muted one.</summary>
        private static void PlayTalkChip(VisualElement parent, Pt p, string text, bool muted, Action onTap)
        {
            var chip = PK.Row(parent);
            chip.style.marginBottom = p.P(8);
            chip.style.backgroundColor = muted ? GameTheme.Shade(.25f) : GameTheme.Light(.14f);
            Kit.Border(chip, p.U(2), muted ? GameTheme.Light(.35f) : GameTheme.C("#9be36f"));
            Kit.Round(chip, p.P(40));
            PK.Pad(chip, p.P(16), p.P(7), p.P(16), p.P(8));
            PK.N(chip, p, text, 18, muted ? GameTheme.T("#cfe3ff") : Color.white);
            PK.Tap(chip, p, onTap);
        }

        // ------------------------------------------------------------------ inside the farmhouse

        private void PlayRoom(Ctx c)
        {
            var p = c.P;
            var s = Sandbox!;
            var uid = s.InsideUid;
            var row = PlayBand(c.Root, p, p.Phone ? 330 : 300, () => { s.GoOutside(); MarkDirty(); });
            var lvl = Economy!.Departments.TryGetValue(uid, out var d0) ? d0.Level : 0;
            PlayInfo(c, row, "Farmhouse", lvl, () => Economy!.Departments.TryGetValue(uid, out var d) ? $"Stored {d.Stored}/{d.StorageCapacity}" : "");
            var mid = PK.Col(row, p.P(10));
            mid.style.marginLeft = p.P(26);
            mid.style.flexGrow = 1;
            if (s.RoomsBuilt > 1)
            {
                var names = Farmverse.Domain.Gameplay.GameplayRules.RoomNames;
                var opts = new List<(string, string)>();
                for (var i = 0; i < Mathf.Min(names.Length, s.RoomsBuilt); i++)
                    opts.Add((i.ToString(), i < s.RoomCount ? names[i] : "\U0001F512 " + names[i]));
                var seg = PK.Seg(mid, p, opts.ToArray(), s.RoomShown.ToString(), id =>
                {
                    var i = int.Parse(id);
                    if (i >= s.RoomCount) { Say($"{names[i]} opens at farmhouse level {Farmverse.Domain.Gameplay.GameplayRules.RoomLevel(i)}"); return; }
                    s.SeeRoom(i);
                    MarkDirty();
                }, 18, 48);
                seg.style.width = p.P(p.Phone ? 560 : 720);
            }

            PK.T(mid, p, "FURNISH", 16, GameTheme.T("#9fd0ff"));
            var furn = PK.Row(mid, p.P(10));
            foreach (var it in Farmverse.Domain.Gameplay.GameplayRules.RoomItemsIn(s.RoomShown).Take(p.Phone ? 5 : 7))
            {
                var item = it;
                var owned = Economy!.HasRoomItem(item.Id);
                var (slot, _) = PlayInSlot(furn, p, "Isl_f_" + item.Id, owned ? "\u2713" : PK.Num(item.Price), 86, 54, !owned && Economy.Coins < item.Price, !owned);
                slot.tooltip = item.Name;
                if (!owned) PK.Tap(slot, p, () => { if (s.BuyRoomItem(item.Id)) Say(item.Name + " placed"); MarkDirty(); });
            }

            PK.T(mid, p, "IN STORE", 16, GameTheme.T("#9fd0ff"));
            var goods = PK.Row(mid, p.P(10));
            var stock = d0 != null ? d0.Stock.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(p.Phone ? 6 : 9).ToList() : new List<KeyValuePair<string, int>>();
            foreach (var kv in stock) PlayInSlot(goods, p, PlayGood(kv.Key), kv.Value.ToString(), 86, 50);
            if (stock.Count == 0) PK.N(mid, p, "The stores are empty: bring your harvest in.", 16, GameTheme.T("#dff1ff"));
            var col = PK.Col(row, p.P(12));
            col.style.justifyContent = Justify.Center;
            PlayBtn(col, p, "green", null, "Go outside", 220, 64, 24, () => { s.GoOutside(); MarkDirty(); });
        }
    }
}
