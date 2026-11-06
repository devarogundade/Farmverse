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
    /// <summary>
    /// Workers run the farm (docs: Workers Run the Farm): the job switches of one building ("workjobs", from its
    /// band's Workers button) and the Workers panel ("workers", from a workers' house, or a push) with every
    /// building's switches, the food left for offline work, the offline cap, and the Night Shift and Lunch Basket.
    /// </summary>
    public sealed partial class GameUi
    {
        private static readonly bool RegWorkJobs = Register("workjobs", UiLayer.Popup, (ui, c) => ui.WorkJobsPopup(c), .55f);
        private static readonly bool RegWorkers = Register("workers", UiLayer.Page, (ui, c) => ui.WorkersPage(c), .55f);

        /// <summary>A job's name and what it does, for the switches.</summary>
        private static (string name, string about) WorkWords(string job) => job switch
        {
            WorkJob.Harvest => ("Harvest", "Pick ripe crops and store them"),
            WorkJob.Replant => ("Replant", "Sow the field again (pays the seed)"),
            WorkJob.Feed => ("Feed", "Carry feed from the farmhouse to the trough"),
            WorkJob.Collect => ("Collect", "Take what it made to the farmhouse"),
            WorkJob.Refill => ("Refill", "Bring its ingredients from the farmhouse"),
            WorkJob.Repair => ("Repair", "Builders mend it after damage"),
            WorkJob.Sell => ("Sell surplus", "Sell what the farmhouse has spare"),
            WorkJob.Restock => ("Restock food", "Keep this house fed from the farmhouse"),
            _ => (job, ""),
        };

        /// <summary>Why a building's switches don't do anything yet ("" when they do).</summary>
        private string WorkBlocked(string job)
        {
            var eco = Economy;
            if (eco == null) return "";
            if (job == WorkJob.Repair) return eco.Has("builders_hut") ? "" : "Needs a builders' hut";
            return eco.Has("workers_house") ? "" : "Needs a workers' house";
        }

        // ------------------------------------------------------------------ one building

        private void WorkJobsPopup(Ctx c)
        {
            var p = c.P;
            var eco = Economy;
            var uid = c.Args is int u ? u : -1;
            var b = Host?.Building(uid);
            if (eco == null || b == null) { Back(); return; }
            var jobs = eco.WorkJobs(uid);
            c.Watch(() => string.Join(",", jobs.Select(j => eco.WorkOn(uid, j) ? "1" : "0")) + "|" + eco.WorkCrop(uid) + "|" + eco.BestLevel);
            var pop = PK.Popup(c.Root, p, 820, 0, "Workers: " + IslandEconomyHost.NameOf(b.id), Back, "Blue");
            var col = PK.Col(pop, p.P(12));
            PK.Pad(col, p.P(10), p.P(16), p.P(10), p.P(6));
            if (jobs.Count == 0) PK.N(col, p, "Nothing for workers to do here.", 18, GameTheme.T("#dff1ff"));
            foreach (var job in jobs) WorkSwitchRow(col, p, uid, job);
            if (jobs.Contains(WorkJob.Replant)) WorkCropRow(col, p, uid);
            var foot = PK.N(col, p, "Switched-on jobs run while you play and while you're away. Away, work costs 20% more food.", 15, GameTheme.T("#9fd0ff"), TextAnchor.MiddleCenter, true);
            foot.style.marginTop = p.P(8);
            var row = PK.Row(col, p.P(16));
            row.style.justifyContent = Justify.Center;
            row.style.marginTop = p.P(6);
            PK.Btn(row, p, "blue", "All workers", 240, 64, 22, () => { Back(); Open("workers"); });
        }

        private void WorkSwitchRow(VisualElement parent, Pt p, int uid, string job)
        {
            var eco = Economy!;
            var on = eco.WorkOn(uid, job);
            var (name, about) = WorkWords(job);
            var blocked = WorkBlocked(job);
            var row = PK.ListRow(parent, p, on, false);
            row.style.height = p.P(84);
            PK.Pad(row, p.P(16), 0, p.P(16), 0);
            var words = PK.Col(row, p.P(2));
            words.style.flexGrow = 1;
            PK.T(words, p, name, 22);
            PK.N(words, p, blocked.Length > 0 ? blocked : about, 15, GameTheme.T(blocked.Length > 0 ? "#ffcf6b" : "#bfe6ff"));
            PK.Btn(row, p, on ? "green" : "gray", on ? "On" : "Off", 130, 56, 22, () =>
            {
                Economy?.SetWork(uid, job, !on);
                IslandAudio.Play(on ? "drop" : "pop", null, 0.5f, 0f);
                Host?.Save();
                MarkDirty();
            });
        }

        /// <summary>Which crop workers sow here: the last one, or a chosen open crop.</summary>
        private void WorkCropRow(VisualElement parent, Pt p, int uid)
        {
            var eco = Economy!;
            var row = PK.Row(parent, p.P(10));
            row.style.marginTop = p.P(6);
            row.style.flexWrap = Wrap.Wrap;
            PK.N(row, p, "Sow:", 18, GameTheme.T("#dff1ff"));
            var chosen = eco.WorkCrop(uid);
            PK.Btn(row, p, chosen == null ? "green" : "gray", "Same again", 170, 52, 18, () => { Economy?.SetWorkCrop(uid, null); Host?.Save(); MarkDirty(); });
            foreach (var crop in GameplayRules.Crops.Where(cr => eco.CropOpen(cr.Id)))
            {
                var id = crop.Id;
                var b = PK.Btn(row, p, chosen == id ? "green" : "gray", PlayPretty(id), 130, 52, 17, () => { Economy?.SetWorkCrop(uid, id); Host?.Save(); MarkDirty(); });
                b.style.marginLeft = p.P(4);
            }
        }

        // ------------------------------------------------------------------ the Workers panel

        private void WorkersPage(Ctx c)
        {
            var p = c.P;
            var eco = Economy;
            var host = Host;
            c.Watch(() => eco == null ? "" : string.Join(",", (host?.Buildings ?? Enumerable.Empty<int>()).Select(u => string.Concat(eco.WorkJobs(u).Select(j => eco.WorkOn(u, j) ? "1" : "0"))))
                                              + "|" + eco.WorkTrain + "|" + eco.NightShift + "|" + ShopBoostCount("nightshift") + "|" + ShopBoostCount("lunch"));
            var (body, _, _) = ShopFrame(c, "Workers", null);
            if (eco == null || host == null) return;

            // left: the crew, their food, the offline shift
            var side = PK.Panel(body, p);
            side.style.width = p.P(380);
            side.style.flexShrink = 0;
            side.style.flexDirection = FlexDirection.Column;
            PK.Pad(side, p.P(18), p.P(18), p.P(18), p.P(18));
            var workers = eco.Departments.Values.Where(d => d.Levels.Kind == DepartmentKind.WorkersHouse && eco.Working(d.Uid)).Sum(d => d.Present);
            PK.T(side, p, workers == 0 ? "No farm workers yet" : $"{workers} farm workers", 26);
            var food = PK.N(side, p, "", 18, GameTheme.T("#dff1ff"), wrap: true);
            food.style.marginTop = p.P(10);
            var cap = PK.N(side, p, "", 16, GameTheme.T("#9fd0ff"), wrap: true);
            cap.style.marginTop = p.P(8);
            c.Live(() =>
            {
                var e = Economy;
                var h = Host;
                if (e == null || h == null) return;
                var energy = e.Departments.Values.Where(d => d.Levels.Kind == DepartmentKind.WorkersHouse).Sum(d => d.Energy);
                var left = h.FoodHours();
                food.text = Loc.Of(workers == 0 ? "Build a workers' house: its people run the farm for you."
                    : left == null ? $"Food: {Mathf.RoundToInt((float)energy)} · enough for the whole offline shift"
                    : $"Food: {Mathf.RoundToInt((float)energy)} · runs out after about {WorkHours(left.Value)} of offline work");
                cap.text = Loc.Of($"Away, workers keep going for {Mathf.RoundToInt((float)h.OfflineCapHours)} hours" + (e.NightShift ? " (Night Shift: normal food price)" : " at 20% more food"));
            });
            var tips = PK.N(side, p, "A bigger workers' house works longer offline: 12 hours at level 2, 16 at level 3. The Farm Pass makes it 24.", 14, GameTheme.T("#8ad6ff"), wrap: true);
            tips.style.marginTop = p.P(10);
            WorkCrewSection(side, p, eco);
            var grow = PK.Box(side);
            grow.style.flexGrow = 1;
            WorkBoostButton(side, p, "nightshift", eco.NightShift ? "Night Shift on" : "Night Shift", "Next absence: normal food price, up to 24 h");
            WorkBoostButton(side, p, "lunch", "Lunch Basket", "+" + Mathf.RoundToInt((float)GameplayRules.LunchEnergy) + " food in every workers' house");

            // right: every building's switches
            var panel = PK.Panel(body, p);
            panel.style.flexGrow = 1;
            panel.style.minWidth = 0;
            panel.style.marginLeft = p.P(18);
            panel.style.flexDirection = FlexDirection.Column;
            PK.Pad(panel, p.P(16), p.P(16), p.P(16), p.P(16));
            var bulk = PK.Row(panel, p.P(10));
            bulk.style.flexWrap = Wrap.Wrap;
            bulk.style.marginBottom = p.P(10);
            void Bulk(string label, string job, bool on, Func<int, bool>? which = null) =>
                PK.Btn(bulk, p, on ? "green" : "gray", label, 230, 52, 17, () => { Economy?.SetWorkAll(job, on, which); Host?.Save(); MarkDirty(); });
            Bulk("All fields: harvest", WorkJob.Harvest, true);
            Bulk("All fields: replant", WorkJob.Replant, true);
            Bulk("All animals: feed", WorkJob.Feed, true);
            Bulk("All machines: refill", WorkJob.Refill, true);
            Bulk("Stop all feeding", WorkJob.Feed, false);
            PK.Btn(bulk, p, eco.WorkTrain ? "green" : "gray", eco.WorkTrain ? "Train crates: on" : "Train crates: off", 230, 52, 17, () =>
            {
                if (Economy != null) Economy.WorkTrain = !Economy.WorkTrain;
                Host?.Save();
                MarkDirty();
            });
            var list = ShopScroller(panel, false, p.P(12)).contentContainer;
            var uids = host.Buildings.Where(u => eco.WorkJobs(u).Count > 0 && host.Building(u) != null)
                .OrderBy(u => WorkSortKey(eco, u)).ThenBy(u => u).ToList();
            // number repeats ("Field 1", "Field 2") so each row can be told apart
            var seen = new Dictionary<string, int>();
            var total = uids.GroupBy(u => host.Building(u)!.id).ToDictionary(g => g.Key, g => g.Count());
            foreach (var u in uids)
            {
                var id = host.Building(u)!.id;
                seen[id] = seen.TryGetValue(id, out var k) ? k + 1 : 1;
                var label = Loc.Of(IslandEconomyHost.NameOf(id));
                WorkBuildingRow(list, p, u, total[id] > 1 ? label + " " + seen[id] : label);
            }
        }

        /// <summary>Farmhands (docs 15 §7): who works here, their trade and level; beds come from the workers' houses.</summary>
        private void WorkCrewSection(VisualElement side, Pt p, IslandEconomy eco)
        {
            if (!ChainReady) return;
            var crew = eco.Crew;
            var beds = eco.CrewBeds;
            PK.T(side, p, crew.Count == 0 ? "Farmhands" : $"Farmhands · {System.Math.Min(crew.Count, beds)}/{beds} beds", 22).style.marginTop = p.P(14);
            if (crew.Count == 0)
                PK.N(side, p, "Farmhands are workers you own as NFTs: they work here with their own skills and level up.", 14, GameTheme.T("#9fd0ff"), wrap: true);
            for (var i = 0; i < crew.Count && i < 8; i++)
            {
                var f = crew[i];
                var bed = i < beds;
                var trade = f.specialty switch { "fields" => "Fields", "animals" => "Animals", "machines" => "Machines", "freight" => "Freight", _ => "All-round" };
                var line = $"{f.name} · {trade} · level {f.level}" + (f.rented ? " · rented" : "") + (bed ? "" : " · no bed free");
                PK.N(side, p, line, 15, bed ? Color.white : GameTheme.T("#ffd23a"), wrap: true).style.marginTop = p.P(4);
            }
            var hire = PK.Btn(side, p, "yellow", crew.Count == 0 ? "Hire a Farmhand" : "Hire more", 340, 52, 18, () => GameLinks.Open(GameLinks.Market + "/#/rent", "Open the market"));
            hire.style.marginTop = p.P(8);
        }

        private static int WorkSortKey(IslandEconomy eco, int uid) =>
            eco.Plots.ContainsKey(uid) ? 0 : eco.Animals.ContainsKey(uid) ? 1 : eco.Machines.ContainsKey(uid) ? 2 : 3;

        private static string WorkHours(double h) => h < 1 ? $"{Mathf.Max(1, Mathf.RoundToInt((float)(h * 60)))} min" : $"{h:0.#} h";

        private void WorkBuildingRow(VisualElement parent, Pt p, int uid, string label)
        {
            var eco = Economy!;
            var row = PK.ListRow(parent, p, false, false);
            row.style.minHeight = p.P(76);
            PK.Pad(row, p.P(14), p.P(6), p.P(14), p.P(6));
            var name = PK.T(row, p, label, 19);
            name.style.width = p.P(210);
            name.style.flexShrink = 0;
            var chips = PK.Row(row, p.P(8));
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.flexGrow = 1;
            foreach (var job in eco.WorkJobs(uid))
            {
                var on = eco.WorkOn(uid, job);
                var j = job;
                PK.Btn(chips, p, on ? "green" : "gray", WorkWords(job).name, 150, 50, 16, () =>
                {
                    Economy?.SetWork(uid, j, !on);
                    Host?.Save();
                    MarkDirty();
                });
            }
        }

        /// <summary>Use a held boost, or buy one with gems when there's none.</summary>
        private void WorkBoostButton(VisualElement parent, Pt p, string id, string label, string about)
        {
            var have = ShopBoostCount(id);
            var price = Wallet.Price("boost_" + id);
            var box = PK.Col(parent, p.P(4));
            box.style.marginTop = p.P(10);
            PK.N(box, p, about, 14, GameTheme.T("#bfe6ff"), wrap: true);
            var text = have > 0 ? $"{label} (use · {have} left)" : $"{label} · {price} gems";
            PK.Btn(box, p, have > 0 ? "green" : "yellow", text, 330, 58, 18, () =>
            {
                if (id == "nightshift" && Economy?.NightShift == true) { Say("Your Night Shift is already booked for your next absence"); return; }
                if (ShopBoostCount(id) > 0) ShopUseBoost(id);
                else ShopBuy("boost_" + id, () => ShopUseBoost(id));
            });
        }

        /// <summary>Leaving the island with little food for the switched-on work: a word before they go.</summary>
        private void WorkLeavingHint()
        {
            var h = Host;
            var eco = Economy;
            if (h == null || eco == null || !eco.Has("workers_house")) return;
            var left = h.FoodHours();
            if (left != null && left.Value < 2) Say($"Your workers will run out of food in about {WorkHours(left.Value)} — stock their house before you go", 4f);
        }
    }
}
