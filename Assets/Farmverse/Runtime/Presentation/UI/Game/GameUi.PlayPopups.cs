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
    // gameplay.html's popups (Upgrade, Not enough, Raid result) and rewards.html's Welcome back (the away report),
    // plus a small "sell this building?" check.
    public sealed partial class GameUi
    {
        private static readonly bool RegPlay_Upgrade = Register("upgrade", UiLayer.Popup, (ui, c) => ui.PlayUpgrade(c), .55f);
        private static readonly bool RegPlay_NotEnough = Register("notenough", UiLayer.Popup, (ui, c) => ui.PlayNotEnough(c), .55f);
        private static readonly bool RegPlay_RaidResult = Register("raidresult", UiLayer.Popup, (ui, c) => ui.PlayRaidResult(c), .55f);
        private static readonly bool RegPlay_Welcome = Register("welcome", UiLayer.Popup, (ui, c) => ui.PlayWelcome(c), .55f);
        private static readonly bool RegPlay_Sell = Register("sellbuilding", UiLayer.Popup, (ui, c) => ui.PlaySellBuilding(c), .55f);

        /// <summary>What's missing (args of "notenough"): a good or "coins", how many, and what for.</summary>
        public sealed class PlayNeed
        {
            public readonly string Item;
            public readonly long Missing;
            public readonly string Purpose;

            public PlayNeed(string item, long missing, string purpose)
            {
                Item = item;
                Missing = missing;
                Purpose = purpose;
            }
        }

        // ------------------------------------------------------------------ upgrade

        private void PlayUpgrade(Ctx c)
        {
            var p = c.P;
            var eco = Economy;
            if (eco == null || !(c.Args is int uid))
            {
                Back();
                return;
            }

            eco.Departments.TryGetValue(uid, out var dept);
            var buildingId = dept?.BuildingId ?? (eco.Machines.TryGetValue(uid, out var m0) ? m0.BuildingId : eco.Animals.TryGetValue(uid, out var a0) ? a0.BuildingId : "");
            var name = IslandEconomyHost.NameOf(buildingId);
            var level = dept?.Level ?? eco.BuildingLevel(uid);
            var pop = PK.Popup(c.Root, p, 860, 0, "Upgrade " + name, Back);

            // what it costs
            long coins = 0;
            IReadOnlyList<(string item, int count)> mats = Array.Empty<(string, int)>();
            int? funds = null;
            string? blocked = null;
            if (dept != null)
            {
                if (eco.UpgradeCost(uid) is int cost)
                {
                    funds = cost;
                    mats = GameplayRules.DepartmentUpgradeMaterials(dept.BuildingId, dept.Levels.Kind, dept.Level);
                }
                else if (dept.Level < dept.Levels.MaxLevel)
                {
                    // the island's level holds it back: say which level opens the next one
                    var opens = Enumerable.Range(eco.BestLevel + 1, 100).FirstOrDefault(l => GameplayRules.DepartmentMaxLevelAt(l) > dept.Level);
                    blocked = opens > 0 ? $"Opens at island level {opens}" : "Top level";
                }
                else blocked = "Top level";
            }
            else if (eco.BuildingUpgradeCost(uid) is { } bc)
            {
                coins = bc.coins;
                mats = bc.materials;
            }
            else blocked = eco.BuildingLevel(uid) >= GameplayRules.BuildingMaxLevel ? "Top level: as fast and roomy as it gets"
                : $"Level {eco.BuildingLevel(uid) + 1} opens at island level {Enumerable.Range(eco.BestLevel + 1, 100).FirstOrDefault(l => GameplayRules.BuildingMaxLevelAt(l) > eco.BuildingLevel(uid))}";

            var body = PK.Row(pop, p.P(30));
            PK.Pad(body, 0, p.P(26), 0, p.P(10));
            var left = PK.Col(body, p.P(8));
            left.style.alignItems = Align.Center;
            var pic = PK.Centre(PK.Box(left));
            PK.Size(pic, p.P(170), p.P(170));
            pic.style.backgroundColor = PK.Ink;
            Kit.Round(pic, p.U(20));
            PK.Fit(pic, PlayBuilding(buildingId), p.P(130), p.P(130));
            var lv = PK.Row(left, p.P(10));
            PK.T(lv, p, "Lv " + level, 30);
            PlayArrow(lv, p, 20, 28);
            PK.T(lv, p, "Lv " + (level + 1), 30, GameTheme.C("#7dff6a"));

            var right = PK.Col(body, p.P(10));
            right.style.flexGrow = 1;
            right.style.flexShrink = 1;
            foreach (var (k, a, b) in PlayUpgradeRows(uid, level))
            {
                var r = PK.Row(right);
                r.style.justifyContent = Justify.SpaceBetween;
                r.style.backgroundColor = GameTheme.Shade(.18f);
                Kit.Round(r, p.U(10));
                PK.Pad(r, p.P(14), p.P(8), p.P(14), p.P(8));
                PK.N(r, p, k, 19, GameTheme.C("#dff1ff"));
                PK.N(r, p, $"{a} → <color=#7dff6a>{b}</color>", 19, GameTheme.C("#dff1ff"));
            }

            var req = PK.Row(right, p.P(12));
            req.style.marginTop = p.P(6);
            var shortOf = eco.MissingMaterials(mats);
            void Tile(string art, string text, bool ok)
            {
                var t = PK.Col(req, p.P(4));
                t.style.alignItems = Align.Center;
                t.style.backgroundColor = PK.Ink;
                Kit.Round(t, p.U(12));
                PK.Pad(t, p.P(10), p.P(8), p.P(10), p.P(8));
                t.style.minWidth = p.P(92);
                PK.Img(t, art, p.P(44));
                PK.T(t, p, text, 17, ok ? (Color?)null : GameTheme.C("#ff7a8c"));
            }

            if (coins > 0) Tile("ResourceBar_Icon_Gold", PK.Num(coins), eco.Coins >= coins);
            if (funds is int f)
            {
                var have = eco.UpgradeFunds(uid);
                Tile("Icon_Chest", $"{PK.Num(have)} / {PK.Num(f)}", have >= f);
            }

            foreach (var (item, count) in mats)
            {
                var missing = shortOf.Where(x => x.item == item).Sum(x => x.count);
                Tile(PlayGood(item), $"{count - missing} / {count}", missing == 0);
            }

            var buttons = PK.Row(pop, p.P(18));
            buttons.style.justifyContent = Justify.Center;
            buttons.style.marginTop = p.P(12);
            var canPay = blocked == null && eco.Coins >= coins && shortOf.Count == 0 && (funds == null || eco.UpgradeFunds(uid) >= funds);
            var go = PlayBtn(buttons, p, "gray", "Icon_PictoIcon_Timer", blocked == null ? "Upgrade" : "Max level", 330, 74, 24, () =>
            {
                if (!canPay)
                {
                    PlayExplainShort(coins, shortOf, name);
                    return;
                }

                bool done;
                if (dept != null)
                {
                    if (Host!.StationSite != uid)
                    {
                        Say("Walk to its door to upgrade it: it pays from what's stored there.", 3f);
                        return;
                    }

                    done = Host.UpgradeDepartmentHere();
                }
                else done = Host!.LevelUpBuilding(uid);

                if (done)
                {
                    IslandAudio.Play("built", null, 0.8f, 0f, 1f);
                    Back();
                    MarkDirty();
                }
                else IslandAudio.Play("error", null, 0.5f);
            });
            if (blocked != null) PlayEnable(go, false);
            if (blocked == null && (shortOf.Count > 0 || eco.Coins < coins))
                PlayBtn(buttons, p, "purple", null, "How to get it", 250, 74, 24, () => PlayExplainShort(coins, shortOf, name));

            var note = PK.N(pop, p, "", 15, GameTheme.C("#ff9fb0"), TextAnchor.MiddleCenter, true);
            note.style.marginTop = p.P(8);
            if (blocked != null) note.text = Loc.Of(blocked);
            else if (shortOf.Count > 0) note.text = Loc.Of($"Need {shortOf[0].count} more {PlayPretty(shortOf[0].item)} — make or gather it, or fill orders that give it.");
            else if (eco.Coins < coins) note.text = Loc.Of($"Need {PK.Num(coins - eco.Coins)} more coins.");
            else note.style.display = DisplayStyle.None;
        }

        private void PlayExplainShort(long coins, IReadOnlyList<(string item, int count)> shortOf, string name)
        {
            var eco = Economy!;
            if (shortOf.Count > 0) Replace("notenough", new PlayNeed(shortOf[0].item, shortOf[0].count, "upgrade the " + name.ToLowerInvariant()));
            else if (eco.Coins < coins) Replace("notenough", new PlayNeed("coins", coins - eco.Coins, "upgrade the " + name.ToLowerInvariant()));
        }

        /// <summary>What gets better at the next level, from the rules.</summary>
        private List<(string k, string a, string b)> PlayUpgradeRows(int uid, int level)
        {
            var eco = Economy!;
            var rows = new List<(string, string, string)>();
            if (eco.Machines.TryGetValue(uid, out var m))
            {
                rows.Add(("Speed", $"+{(GameplayRules.BuildingLevelSpeed(level) - 1) * 100:0}%", $"+{(GameplayRules.BuildingLevelSpeed(level + 1) - 1) * 100:0}%"));
                rows.Add(("Output room", m.OutputMax.ToString(), (m.OutputMax + GameplayRules.BuildingLevelRoom * 2).ToString()));
            }
            else if (eco.Animals.TryGetValue(uid, out var a))
            {
                rows.Add(("Speed", $"+{(GameplayRules.BuildingLevelSpeed(level) - 1) * 100:0}%", $"+{(GameplayRules.BuildingLevelSpeed(level + 1) - 1) * 100:0}%"));
                rows.Add(("Holds", a.Max.ToString(), (a.Max + GameplayRules.BuildingLevelRoom).ToString()));
            }
            else if (eco.Departments.TryGetValue(uid, out var d))
            {
                var people = d.Levels.People;
                var storage = d.Levels.Storage;
                if (people.Length > 0 && people[Math.Min(level, people.Length) - 1] > 0)
                    rows.Add((PlayPretty(IslandEconomyHost.PeopleOf(d.Levels.Kind)), people[Math.Min(level, people.Length) - 1].ToString(), people[Math.Min(level + 1, people.Length) - 1].ToString()));
                if (storage.Length > 0 && storage[Math.Min(level, storage.Length) - 1] > 0)
                    rows.Add(("Storage", storage[Math.Min(level, storage.Length) - 1].ToString(), storage[Math.Min(level + 1, storage.Length) - 1].ToString()));
            }

            return rows;
        }

        // ------------------------------------------------------------------ not enough

        private void PlayNotEnough(Ctx c)
        {
            var p = c.P;
            if (!(c.Args is PlayNeed need))
            {
                Back();
                return;
            }

            var coins = need.Item == "coins";
            var thing = coins ? "Coins" : PlayPretty(need.Item);
            var pop = PK.Popup(c.Root, p, 680, 0, "Not enough " + thing, Back);
            var col = PK.Col(pop, p.P(16));
            col.style.alignItems = Align.Center;
            PK.Pad(col, 0, p.P(22), 0, p.P(6));
            PK.Slot(col, p, coins ? "ResourceBar_Icon_Gold" : PlayGood(need.Item), PK.Num(need.Missing), "Brown", 110);
            PK.N(col, p, $"You need <color=#ffd23a>{PK.Num(need.Missing)} more {thing}</color> to {need.Purpose}.", 19, GameTheme.C("#dff1ff"), TextAnchor.MiddleCenter, true);
            void Way(string icon, string text, string label, string colour, Action go)
            {
                var r = PK.ListRow(col, p);
                r.style.width = new Length(100, LengthUnit.Percent);
                r.style.height = p.P(80);
                PK.Pad(r, p.P(14), 0, p.P(14), 0);
                PK.Img(r, icon, p.P(50));
                var t = PK.N(r, p, text, 18, Color.white, wrap: true);
                t.style.flexGrow = 1;
                t.style.flexShrink = 1;
                t.style.marginLeft = p.P(14);
                if (colour == "purple") PlayBtn(r, p, colour, "ResourceBar_Icon_Gem", label, 130, 56, 22, go).style.marginLeft = p.P(14);
                else PK.Btn(r, p, colour, label, 130, 56, 22, go).style.marginLeft = p.P(14);
            }

            if (coins)
            {
                Way("Icon_ImageIcon_Map_l", "Fill townsfolk orders for coins", "Go", "blue", () => Open("orders"));
                Way("G_Wheat", "Sell goods at a market stall", "OK", "blue", Back);
                Way("ResourceBar_Icon_Gem", "Get coins in the shop", "Shop", "purple", () => Open("shop", "coins"));
            }
            else
            {
                Way("Icon_ImageIcon_Map_l", $"Fill an order that gives {thing.ToLowerInvariant()}", "Go", "blue", () => Open("orders"));
                Way(GameplayRules.IsCrop(need.Item) ? "Icon_Shovel" : "G_Mine", GameplayRules.IsCrop(need.Item) ? $"Grow it: plant {thing.ToLowerInvariant()} on a field" : $"Make or gather {thing.ToLowerInvariant()} on your island", "OK", "blue", Back);
            }
        }

        // ------------------------------------------------------------------ raid result

        private void PlayRaidResult(Ctx c)
        {
            var p = c.P;
            var life = Life;
            if (life == null)
            {
                Back();
                return;
            }

            var r = life.LastRaidOutcome;
            var away = life.LastRaidAway;
            var pop = PK.Popup(c.Root, p, 720, 0, r.won ? "Zombies driven off!" : away ? "Zombie night while away" : "The night is over", Back, r.won ? "Green" : "Orange");
            if (away)
            {
                var sub = PK.N(pop, p, r.won ? "Zombies came while you were away. Your warriors held them off." : "Zombies came while you were away. Be here next time to fight them!", 17, GameTheme.C("#dff1ff"));
                sub.style.whiteSpace = WhiteSpace.Normal;
                sub.style.unityTextAlign = TextAnchor.MiddleCenter;
                sub.style.marginBottom = p.P(10);
            }
            var col = PK.Col(pop, p.P(16));
            col.style.alignItems = Align.Center;
            PK.Pad(col, 0, p.P(20), 0, p.P(6));
            var tiles = PK.Row(col, p.P(22));
            void Tile(string art, string value, string label)
            {
                var t = PK.Col(tiles, p.P(4));
                t.style.alignItems = Align.Center;
                t.style.backgroundColor = PK.Ink;
                Kit.Round(t, p.U(14));
                PK.Pad(t, p.P(18), p.P(12), p.P(18), p.P(12));
                t.style.minWidth = p.P(150);
                PK.Img(t, art, p.P(60));
                PK.T(t, p, value, 30);
                PK.N(t, p, label, 14, GameTheme.T("#9fd0ff"));
            }

            // the night's boss, with its portrait
            if (!away && life.LastRaidBosses.Length > 0)
            {
                var boss = PK.Row(col, p.P(14));
                boss.style.alignItems = Align.Center;
                var pics = ShopPictures();
                var first = life.LastRaidBosses.Split(',')[0].Trim().Replace("the ", "").Replace("Zombie ", "").ToLowerInvariant();
                var face = PK.Box(boss);
                PK.Size(face, p.P(84), p.P(84));
                Kit.Round(face, p.P(42));
                face.style.backgroundColor = GameTheme.T("#2a1040");
                Kit.Border(face, p.U(3), GameTheme.T("#ff5a7a"));
                var tex = pics?.Person("zombie_" + first);
                if (tex != null) Kit.Contain(face, tex); else PK.Img(face, "Icon_Sword", p.P(44));
                var words = PK.Col(boss, p.P(2));
                PK.T(words, p, "Boss zombie", 16, GameTheme.T("#ff9fb3"));
                PK.T(words, p, life.LastRaidBosses, 24);
                if (life.LastRaidCured > 0) PK.N(words, p, life.LastRaidCured == 1 ? "Cured! A new townsperson joined the island." : $"{life.LastRaidCured} cured! New townsfolk joined the island.", 15, GameTheme.T("#9fffc0"));
            }
            Tile("Icon_Sword", r.size.ToString(), r.won ? "zombies beaten" : "zombies came");
            if (!away) Tile("G_Lost", r.killed.ToString(), r.killed == 1 ? "person lost" : "people lost");
            Tile("G_Wheat", r.stolen > 0 ? "-" + r.stolen : "0", "goods stolen");
            if (r.ruined > 0) Tile("Icon_Hammer", r.ruined.ToString(), r.ruined == 1 ? "building ruined" : "buildings ruined");
            if (r.coins > 0) Tile("ResourceBar_Icon_Gold", "-" + PK.Num(r.coins), "coins taken");
            var note = PK.Row(col, p.P(8));
            PK.Img(note, r.won ? "ResourceBar_Icon_Gold" : "Icon_Shield", p.P(30));
            PK.N(note, p, r.won ? (away ? "One zombie night a day: they'll be back tomorrow." : "They dropped loot — walk over it to pick it up.") : "Build an army house and keep it fed to stop the next night.", 17, GameTheme.C("#dff1ff"));
            var buttons = PK.Row(col, p.P(16));
            PK.Btn(buttons, p, "blue", "OK", 190, 68, 26, Back);
        }

        // ------------------------------------------------------------------ welcome back (the away report)

        private void PlayWelcome(Ctx c)
        {
            var p = c.P;
            var host = Host;
            var r = host?.PendingReport;
            if (host == null || r == null)
            {
                Back();
                return;
            }

            void Collect()
            {
                Host?.DismissReport();
                Back();
            }

            var pop = PK.Popup(c.Root, p, 820, 0, null, Collect);
            pop.style.paddingTop = p.P(70);

            // the purple flag over the top
            var flag = PK.Col(pop);
            PK.Abs(flag, left: (p.P(820) - p.P(420)) / 2f, top: -p.P(58), width: p.P(420), height: p.P(110));
            PK.Centre(flag);
            flag.style.paddingBottom = p.P(18);
            PK.Grad(flag, "pflag", true, (0f, GameTheme.C("#a45cff")), (1f, GameTheme.C("#7a2fe0")));
            Kit.Border(flag, p.U(4), PK.Ink);
            Kit.Round(flag, p.U(12), p.U(12), p.U(22), p.U(22));
            PK.T(flag, p, "Welcome back!", 34);
            PK.N(flag, p, "While you were away", 15, GameTheme.C("#ecdcff")).style.marginTop = p.P(4);

            var tiles = PK.Row(pop, p.P(16));
            tiles.style.justifyContent = Justify.Center;
            tiles.style.marginTop = p.P(20);
            var got = r.Gathered.OrderByDescending(kv => kv.Value).Take(4).ToList();
            if (got.Count == 0) got.Add(new KeyValuePair<string, int>("", 0));
            for (var i = 0; i < got.Count; i++)
            {
                var t = PK.Col(tiles, p.P(6));
                PK.Centre(t);
                PK.Size(t, p.P(170), p.P(180));
                Kit.Round(t, p.U(16));
                Kit.Border(t, p.U(3), i == 0 ? GameTheme.T("#7fd0ff") : PK.Ink);
                if (i == 0) PK.Grad(t, "ptile2sel", true, (0f, GameTheme.T("#1f6fd0")), (1f, GameTheme.T("#1557a8")));
                else PK.Grad(t, "ptile2", true, (0f, GameTheme.T("#123f7c")), (1f, GameTheme.T("#0e3366")));
                var item = got[i].Key;
                PK.Fit(t, item.Length > 0 ? PlayGood(item) : "Icon_Chest", p.P(72), p.P(72));
                PK.T(t, p, item.Length > 0 ? "+" + got[i].Value : "0", 32);
                PK.N(t, p, item.Length > 0 ? PlayPretty(item) : "nothing new", 15, GameTheme.T("#bfe6ff"));
            }

            // the farmhouse's room
            var eco = Economy!;
            var house = eco.Departments.Values.FirstOrDefault(d => d.Levels.Kind == DepartmentKind.Farmhouse);
            if (house != null && house.StorageCapacity > 0)
            {
                var st = PK.Col(pop, p.P(6));
                st.style.marginTop = p.P(20);
                st.style.marginLeft = p.P(20);
                st.style.marginRight = p.P(20);
                var line = PK.Row(st);
                line.style.justifyContent = Justify.SpaceBetween;
                PK.N(line, p, "Storage", 16, GameTheme.T("#9fd0ff"));
                PK.N(line, p, $"{house.Stored} / {house.StorageCapacity}", 16, GameTheme.T("#9fd0ff"));
                var (bar, _, _) = PK.Bar(st, p, 0, 30, house.Stored * 100f / house.StorageCapacity, "");
                bar.style.width = new Length(100, LengthUnit.Percent);
            }

            var buttons = PK.Row(pop, p.P(20));
            buttons.style.justifyContent = Justify.Center;
            buttons.style.marginTop = p.P(22);
            PK.Btn(buttons, p, "blue", "Collect", 240, 74, 30, Collect);

            // what each job did
            var jobs = new List<string>();
            if (r.Harvested > 0) jobs.Add(Loc.Of($"harvested {r.Harvested}"));
            if (r.Replanted > 0) jobs.Add(Loc.Of($"sowed {r.Replanted} fields"));
            if (r.Fed > 0) jobs.Add(Loc.Of($"fed the animals {r.Fed}"));
            if (r.Collected > 0) jobs.Add(Loc.Of($"collected {r.Collected}"));
            if (r.Refilled > 0) jobs.Add(Loc.Of($"refilled machines with {r.Refilled}"));
            if (r.Sold > 0) jobs.Add(Loc.Of($"sold {r.Sold} for {r.SoldCoins} coins"));
            if (r.Crates > 0) jobs.Add(Loc.Of($"loaded {r.Crates} train crates"));
            if (jobs.Count > 0)
            {
                var did = PK.N(pop, p, Loc.Of("Workers") + " " + string.Join(" · ", jobs), 16, GameTheme.T("#dff1ff"), TextAnchor.MiddleCenter, true);
                did.style.marginTop = p.P(12);
            }
            if (r.Stopped == "food" || r.Stopped == "storage" || r.Capped)
            {
                var why = r.Stopped == "food" ? Loc.Of("They ran out of food after") + " " + ShopSpan(Math.Max(60, r.StoppedAt - (IslandEconomyHost.Clock - r.Seconds))) + ". " + Loc.Of("Stock the workers' house before you go.")
                    : r.Stopped == "storage" ? "They stopped when the farmhouse filled up: sell, or upgrade it"
                    : $"They worked their {Mathf.RoundToInt((float)r.CapHours)}-hour shift, then rested";
                var stop = PK.N(pop, p, why, 15, GameTheme.T("#ffcf6b"), TextAnchor.MiddleCenter, true);
                stop.style.marginTop = p.P(6);
            }

            var h = (int)(r.Seconds / 3600);
            var m = (int)(r.Seconds % 3600 / 60);
            var words = $"Away {h}h {m}m · {r.Workers} worker{(r.Workers == 1 ? "" : "s")} carried {r.Loads} load{(r.Loads == 1 ? "" : "s")}"
                        + (r.SeedCoins > 0 ? $" · seeds cost {r.SeedCoins} coins" : "")
                        + (r.StorageFull ? " · the farmhouse filled up" : "");
            if (r.FoodUsed > 0) words = Loc.Of(words) + " · " + Loc.Of($"ate {Mathf.RoundToInt((float)r.FoodUsed)} food");
            var foot = PK.N(pop, p, words, 15, GameTheme.T("#9fd0ff"), TextAnchor.MiddleCenter, true);
            foot.style.marginTop = p.P(10);
            var more = PK.Btn(pop, p, "gray", "Workers", 200, 56, 20, () => { Host?.DismissReport(); Back(); Open("workers"); });
            more.style.alignSelf = Align.Center;
            more.style.marginTop = p.P(8);
        }

        // ------------------------------------------------------------------ sell a building

        private void PlaySellBuilding(Ctx c)
        {
            var p = c.P;
            var s = Sandbox;
            if (s == null || s.Selected == null || !s.CanSell)
            {
                Back();
                return;
            }

            var name = s.SelectedEntry?.displayName ?? IslandEconomyHost.NameOf(s.Selected.id);
            var pop = PK.Popup(c.Root, p, 600, 0, "Sell " + name + "?", Back);
            var col = PK.Col(pop, p.P(16));
            col.style.alignItems = Align.Center;
            PK.Pad(col, 0, p.P(22), 0, p.P(6));
            var row = PK.Row(col, p.P(8));
            PK.Img(row, "ResourceBar_Icon_Gold", p.P(46));
            PK.T(row, p, "+" + PK.Num(s.SellValue), 34);
            PK.N(col, p, "It comes down for half what it cost. What's stored in it is lost.", 17, GameTheme.C("#dff1ff"), TextAnchor.MiddleCenter, true);
            var buttons = PK.Row(col, p.P(16));
            PK.Btn(buttons, p, "gray", "Keep it", 200, 68, 24, Back);
            PK.Btn(buttons, p, "red", "Sell", 200, 68, 26, () =>
            {
                Sandbox?.SellSelected();
                Back();
            });
        }
    }
}
