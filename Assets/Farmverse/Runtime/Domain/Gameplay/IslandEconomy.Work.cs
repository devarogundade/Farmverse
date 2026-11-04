#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>The jobs farm workers can be switched on or off for, per building (docs: Workers Run the Farm).</summary>
    public static class WorkJob
    {
        public const string Harvest = "harvest";
        public const string Replant = "replant";
        public const string Feed = "feed";
        public const string Collect = "collect";
        public const string Refill = "refill";
        public const string Repair = "repair";
        public const string Sell = "sell";
        public const string Restock = "restock";

        /// <summary>Who decides what workers do when there aren't enough of them: food first, then what spoils, then the rest.</summary>
        public static readonly IReadOnlyList<string> Order = new[] { Restock, Feed, Harvest, Collect, Refill, Repair, Replant, Sell };
    }

    /// <summary>What the farm workers did while the player was away (the welcome-back report and the server's).</summary>
    public sealed class AwayReport
    {
        public double Seconds;
        public int Workers;
        public int Loads;
        public int SeedCoins;
        public bool StorageFull;
        public readonly Dictionary<string, int> Gathered = new Dictionary<string, int>(StringComparer.Ordinal);
        public int Total => Gathered.Values.Sum();

        public int Harvested, Replanted, Fed, Collected, Refilled, Restocked, Sold, SoldCoins, Crates;
        /// <summary>Energy the workers' houses spent on work (not their idle upkeep).</summary>
        public double FoodUsed;
        /// <summary>Why work stopped early: "food", "storage", or "" (it ran to the cap or to now).</summary>
        public string Stopped = "";
        /// <summary>When it stopped (0 if it didn't).</summary>
        public double StoppedAt;
        /// <summary>The hours offline work could run this time, and the food price it ran at.</summary>
        public double CapHours;
        public double Factor = 1;
        /// <summary>The time past the cap that nobody worked.</summary>
        public bool Capped;

        public bool Any => Loads > 0 || Harvested + Replanted + Fed + Collected + Refilled + Restocked + Sold > 0;

        /// <summary>Adds another span's work (a chain of server wakes adds up to one report).</summary>
        public void Add(AwayReport o)
        {
            Seconds += o.Seconds; Workers = Math.Max(Workers, o.Workers); Loads += o.Loads; SeedCoins += o.SeedCoins;
            StorageFull |= o.StorageFull;
            foreach (var kv in o.Gathered) Gathered[kv.Key] = (Gathered.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
            Harvested += o.Harvested; Replanted += o.Replanted; Fed += o.Fed; Collected += o.Collected; Refilled += o.Refilled;
            Restocked += o.Restocked; Sold += o.Sold; SoldCoins += o.SoldCoins; Crates += o.Crates; FoodUsed += o.FoodUsed;
            if (Stopped.Length == 0 && o.Stopped.Length > 0) { Stopped = o.Stopped; StoppedAt = o.StoppedAt; }
            CapHours = Math.Max(CapHours, o.CapHours);
            Factor = o.Factor;
            Capped |= o.Capped;
        }
    }

    /// <summary>
    /// Workers run the farm (docs: Workers Run the Farm). Every building has job switches (fields: harvest,
    /// replant, repair; animal houses: feed, collect, repair; machines: refill, collect, repair; shops: sell;
    /// workers' and army houses: restock food). The same switches steer the workers on screen, the catch-up
    /// when the game opens and the server while the player is away. Work is paid in food: a worker only starts
    /// a job if their house has the energy for it, and offline the same job costs
    /// <see cref="GameplayRules.OfflineWorkCost"/> times as much. Offline work runs for a few hours (more with
    /// a bigger workers' house, 24 with the Farm Pass or a Night Shift).
    /// </summary>
    public sealed partial class IslandEconomy
    {
        private sealed class WorkSwitch
        {
            public readonly HashSet<string> On = new HashSet<string>(StringComparer.Ordinal);
            public string? Crop;
        }

        private readonly Dictionary<int, WorkSwitch> _work = new Dictionary<int, WorkSwitch>();
        private bool _tickAway;

        /// <summary>When the player went away (0 while they're here): the offline cap counts from it across server wakes.</summary>
        public double AwaySince { get; private set; }

        /// <summary>The Farm Pass is owned (offline work runs for up to 24 hours). Set by the game from the wallet.</summary>
        public bool WorkPass { get; set; }

        /// <summary>A Night Shift was bought for this absence: offline work at the normal food cost, for up to 24 hours.</summary>
        public bool NightShift { get; private set; }

        /// <summary>Workers load the freight train's crates when the stores can spare the goods.</summary>
        public bool WorkTrain { get; set; }

        /// <summary>The jobs a building can be switched for, in the order the screens show them.</summary>
        public IReadOnlyList<string> WorkJobs(int uid)
        {
            var jobs = new List<string>();
            if (!_ids.TryGetValue(uid, out var id) || GameplayRules.IsDecor(id)) return jobs;
            if (_plots.ContainsKey(uid)) { jobs.Add(WorkJob.Harvest); jobs.Add(WorkJob.Replant); }
            if (_animals.TryGetValue(uid, out var a)) { if (a.Producer.Eats) jobs.Add(WorkJob.Feed); jobs.Add(WorkJob.Collect); }
            if (_machines.ContainsKey(uid)) { jobs.Add(WorkJob.Refill); jobs.Add(WorkJob.Collect); }
            if (_departments.TryGetValue(uid, out var d))
            {
                if (d.Levels.Kind == DepartmentKind.Market) jobs.Add(WorkJob.Sell);
                if (d.Levels.Kind is DepartmentKind.WorkersHouse or DepartmentKind.ArmyHouse) jobs.Add(WorkJob.Restock);
            }
            if (id != GameplayRules.FieldBuilding) jobs.Add(WorkJob.Repair);
            return jobs;
        }

        /// <summary>Whether workers do this job at this building (the player's switch, else the default).</summary>
        public bool WorkOn(int uid, string job)
        {
            if (_work.TryGetValue(uid, out var w)) return w.On.Contains(job);
            return GameplayRules.WorkDefault(job) && WorkJobs(uid).Contains(job);
        }

        public void SetWork(int uid, string job, bool on)
        {
            if (!WorkJobs(uid).Contains(job)) return;
            if (!_work.TryGetValue(uid, out var w))
            {
                w = new WorkSwitch();
                foreach (var j in WorkJobs(uid)) if (WorkOn(uid, j)) w.On.Add(j);
                _work[uid] = w;
            }
            if (on) w.On.Add(job); else w.On.Remove(job);
        }

        /// <summary>The crop workers sow in this field (null: what grew there last).</summary>
        public string? WorkCrop(int uid) => _work.TryGetValue(uid, out var w) ? w.Crop : null;

        public void SetWorkCrop(int uid, string? cropId)
        {
            if (!_plots.ContainsKey(uid)) return;
            if (!_work.ContainsKey(uid)) SetWork(uid, WorkJob.Replant, WorkOn(uid, WorkJob.Replant));
            _work[uid].Crop = string.IsNullOrEmpty(cropId) ? null : cropId;
        }

        /// <summary>Switch a job for every building that has it (the Workers panel's "all fields" buttons).</summary>
        public void SetWorkAll(string job, bool on, Func<int, bool>? which = null)
        {
            foreach (var uid in _ids.Keys.ToList())
                if ((which == null || which(uid)) && WorkJobs(uid).Contains(job)) SetWork(uid, job, on);
        }

        /// <summary>The player is back on the island: the absence (its cap and any Night Shift) is over.</summary>
        public void BackOnline()
        {
            AwaySince = 0;
            NightShift = false;
        }

        /// <summary>Buy a Night Shift for the next absence (the shop takes the gems or the ad first).</summary>
        public void StartNightShift() => NightShift = true;

        /// <summary>A Lunch Basket: every workers' house gets a full meal's energy.</summary>
        public void Lunch()
        {
            foreach (var d in _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.WorkersHouse && IsBuilt(d.Uid)))
                d.Energy += GameplayRules.LunchEnergy;
        }

        /// <summary>
        /// The jobs the walking workers don't do on screen, done by the island itself every few seconds while
        /// the player plays: selling surplus at markets switched to it, and loading train crates. Normal food price.
        /// </summary>
        public AwayReport OnScreenStep(double now)
        {
            var r = new AwayReport();
            var houses = WorkerHouses();
            var farmhouse = MainFarmhouse();
            if (houses.Count == 0 || farmhouse == null) return r;
            bool Pay(string job)
            {
                var cost = WorkEnergy(job, false);
                var h = houses.Where(x => x.Energy >= cost).OrderByDescending(x => x.Energy).FirstOrDefault();
                if (h == null) return false;
                h.Energy -= cost;
                r.FoodUsed += cost;
                return true;
            }
            foreach (var shop in _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Market && Working(d.Uid) && WorkOn(d.Uid, WorkJob.Sell)).OrderBy(d => d.Uid))
            {
                var spare = farmhouse.Stock.Keys.Where(k => GameplayRules.SellPrice(shop.BuildingId, k) > 0 && Stocked(k) > Reserve(k) + GameplayRules.SellKeep)
                    .OrderByDescending(k => Stocked(k) - Reserve(k)).ThenBy(k => k, StringComparer.Ordinal).FirstOrDefault();
                if (spare == null || !Pay(WorkJob.Sell)) continue;
                var n = Math.Min(WorkerCarry, Stocked(spare) - Reserve(spare) - GameplayRules.SellKeep);
                for (var i = 0; i < n; i++)
                {
                    farmhouse.TakeStock(spare, 1);
                    var coins = MarketPrice(shop.BuildingId, spare);
                    Coins += coins;
                    Sold(spare);
                    r.Sold++;
                    r.SoldCoins += coins;
                }
            }
            if (WorkTrain && _train != null)
                for (var i = 0; i < _train.Crates.Count; i++)
                {
                    var c = _train.Crates[i];
                    if (c.Loaded || InStore(c.Item) - c.Need < Reserve(c.Item) || !Pay(WorkJob.Collect)) continue;
                    if (LoadCrate(i)) r.Crates++;
                }
            return r;
        }

        /// <summary>Hours offline work runs: 8, 12 or 16 by the best workers' house, 24 with the Farm Pass or a Night Shift.</summary>
        public double OfflineCapHours =>
            WorkPass || NightShift ? GameplayRules.OfflineCapPassHours
            : GameplayRules.OfflineCapHours(_departments.Values.Where(d => d.Levels.Kind == DepartmentKind.WorkersHouse && IsBuilt(d.Uid)).Select(d => d.Level).DefaultIfEmpty(1).Max());

        /// <summary>The food price of offline work: 1.2, or 1 on a Night Shift.</summary>
        public double OfflineFactor => NightShift ? 1 : GameplayRules.OfflineWorkCost;

        /// <summary>Energy a job takes from a workers' house: on screen, or offline (dearer).</summary>
        public double WorkEnergy(string job, bool offline) => GameplayRules.WorkEnergy(job) * (offline ? OfflineFactor : 1);

        /// <summary>The farm workers' houses with people in them.</summary>
        private List<Department> WorkerHouses() =>
            _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.WorkersHouse && Working(d.Uid) && d.Present > 0).OrderBy(d => d.Uid).ToList();

        private Department? MainFarmhouse() =>
            _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && Working(d.Uid)).OrderBy(d => d.Uid).FirstOrDefault();

        private int Stocked(string item) =>
            _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && Working(d.Uid)).Sum(d => d.Stock.TryGetValue(item, out var n) ? n : 0);

        /// <summary>Goods kept back from selling, crates and machine refills: what open orders want, and a few of each.</summary>
        public int Reserve(string item)
        {
            var orders = _delivery.Where(o => o.Item == item).Sum(o => o.Left);
            return orders + GameplayRules.WorkReserve;
        }

        // ---- offline work ----------------------------------------------------------------------------

        /// <summary>Old name kept for callers: the offline work from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public AwayReport WorkWhileAway(double from, double to) => WorkOffline(from, to);

        /// <summary>
        /// The island runs on with the player away: the clock steps every <see cref="GameplayRules.AwayTripSeconds"/>
        /// (on a fixed grid, so a chain of server wakes steps at the same moments as one long catch-up), the upkeep
        /// runs as it does while away (hunger doesn't cost people), and at each step the switched-on jobs go to
        /// fed workers in <see cref="WorkJob.Order"/>, each paid for at the offline food price. Work stops at the
        /// cap (counted from when the player left), and the clock runs on to <paramref name="to"/> without it.
        /// </summary>
        public AwayReport WorkOffline(double from, double to)
        {
            var report = new AwayReport { CapHours = OfflineCapHours, Factor = OfflineFactor };
            if (from <= 0 || to <= from) return report;
            if (AwaySince <= 0) AwaySince = from;
            var capEnd = AwaySince + report.CapHours * 3600.0;
            var end = Math.Min(to, capEnd);
            report.Capped = to > capEnd;
            report.Workers = WorkerHouses().Sum(d => d.Present);
            var step = GameplayRules.AwayTripSeconds;
            var t = Math.Floor(from / step) * step + step;
            _tickAway = true;
            try
            {
                for (; t <= end; t += step)
                {
                    Tick(t);
                    WorkStep(t, report);
                    report.Seconds += step;
                }
                if (Now < to) Tick(to);
            }
            finally { _tickAway = false; }
            return report;
        }

        /// <summary>One step of offline work: each fed worker does one job (one load), the most urgent first.</summary>
        private void WorkStep(double now, AwayReport report)
        {
            var houses = WorkerHouses();
            if (houses.Count == 0) return;
            var farmhouse = MainFarmhouse();
            if (farmhouse == null) return;
            var hands = houses.Sum(h => h.Present);
            var carry = WorkerCarry;
            var load = new List<string>();
            var hungry = false;

            // pay for a job from a house that can: false if none can
            bool Pay(string job)
            {
                var cost = WorkEnergy(job, true);
                var h = houses.Where(x => x.Energy >= cost).OrderByDescending(x => x.Energy).FirstOrDefault();
                if (h == null) { hungry = true; return false; }
                h.Energy -= cost;
                report.FoodUsed += cost;
                hands--;
                return true;
            }

            bool Store(List<string> items)
            {
                var all = true;
                foreach (var item in items)
                {
                    if (this.Store(farmhouse.Uid, item, 1) == 1) report.Gathered[item] = (report.Gathered.TryGetValue(item, out var n) ? n : 0) + 1;
                    else { all = false; report.StorageFull = true; }
                }
                return all;
            }

            foreach (var job in WorkJob.Order)
            {
                if (hands <= 0 || hungry) break;
                switch (job)
                {
                    case WorkJob.Restock:
                        foreach (var d in _departments.Values.Where(d => d.Levels.Kind is DepartmentKind.WorkersHouse or DepartmentKind.ArmyHouse && Working(d.Uid)
                                                                         && WorkOn(d.Uid, WorkJob.Restock) && d.Energy < GameplayRules.RestockBelow).OrderBy(d => d.Energy).ToList())
                        {
                            if (hands <= 0) break;
                            load.Clear();
                            var want = GameplayRules.RestockTo - d.Energy;
                            while (load.Count < carry && want > 0)
                            {
                                var food = farmhouse.Stock.Keys.Where(k => GameplayRules.EnergyOf(k) > 0 && Stocked(k) > Reserve(k))
                                    .OrderBy(GameplayRules.EnergyOf).ThenBy(k => k, StringComparer.Ordinal).FirstOrDefault();   // cheapest food first
                                if (food == null) break;
                                farmhouse.TakeStock(food, 1);
                                load.Add(food);
                                want -= GameplayRules.EnergyOf(food);
                            }
                            if (load.Count == 0) continue;
                            hands--;
                            Deliver(d.Uid, load);
                            report.Restocked += load.Count;
                            report.Loads++;
                        }
                        break;
                    case WorkJob.Feed:
                        foreach (var a in _animals.Values.Where(a => a.Producer.Eats && Working(a.Uid) && WorkOn(a.Uid, WorkJob.Feed) && a.FeedLeft(now) < GameplayRules.TroughSize / 2).OrderBy(a => a.FeedLeft(now)).ToList())
                        {
                            if (hands <= 0 || hungry) break;
                            load.Clear();
                            LoadFeed(farmhouse.Uid, a.Uid, carry, load);
                            if (load.Count == 0) continue;
                            if (!Pay(WorkJob.Feed)) { foreach (var i in load) farmhouse.Store(i, 1); break; }
                            FillTrough(a.Uid, load, now);
                            report.Fed += load.Count;
                            report.Loads++;
                        }
                        break;
                    case WorkJob.Harvest:
                        foreach (var p in _plots.Values.Where(p => Working(p.Uid) && WorkOn(p.Uid, WorkJob.Harvest) && p.State(now) == PlotState.Ripe).OrderBy(p => p.Uid).ToList())
                        {
                            if (hands <= 0 || hungry) break;
                            if (farmhouse.Stored >= farmhouse.StorageCapacity) { report.StorageFull = true; break; }
                            if (!Pay(WorkJob.Harvest)) break;
                            load.Clear();
                            TakeHarvest(p.Uid, now, carry, load);
                            report.Harvested += load.Count;
                            report.Loads++;
                            Store(load);
                            // the same worker sows it again once it's bare (as on screen)
                            if (p.State(now) == PlotState.Empty && WorkOn(p.Uid, WorkJob.Replant)) Resow(p.Uid, now);
                        }
                        break;
                    case WorkJob.Collect:
                        foreach (var uid in _animals.Keys.Concat(_machines.Keys).Where(u => Working(u) && WorkOn(u, WorkJob.Collect)).OrderBy(u => u).ToList())
                        {
                            if (hands <= 0 || hungry) break;
                            var ready = _animals.TryGetValue(uid, out var a) ? a.StockAt(now) + (a.Side?.StockAt(now) ?? 0) : _machines[uid].Output;
                            if (ready < 1) continue;
                            if (farmhouse.Stored >= farmhouse.StorageCapacity) { report.StorageFull = true; break; }
                            if (!Pay(WorkJob.Collect)) break;
                            load.Clear();
                            if (a != null) TakeProduce(uid, now, carry, load); else TakeOutput(uid, now, carry, load);
                            report.Collected += load.Count;
                            report.Loads++;
                            Store(load);
                        }
                        break;
                    case WorkJob.Refill:
                        foreach (var m in _machines.Values.Where(m => Working(m.Uid) && WorkOn(m.Uid, WorkJob.Refill) && HopperRoom(m.Uid) >= 1).OrderBy(m => m.Uid).ToList())
                        {
                            if (hands <= 0 || hungry) break;
                            load.Clear();
                            LoadInputs(farmhouse.Uid, m.Uid, carry, load, keep: true);
                            if (load.Count == 0) continue;
                            if (!Pay(WorkJob.Refill)) { foreach (var i in load) farmhouse.Store(i, 1); break; }
                            FillMachine(m.Uid, load, now);
                            report.Refilled += load.Count;
                            report.Loads++;
                        }
                        break;
                    case WorkJob.Replant:
                        foreach (var p in _plots.Values.Where(p => Working(p.Uid) && WorkOn(p.Uid, WorkJob.Replant) && p.State(now) == PlotState.Empty).OrderBy(p => p.Uid).ToList())
                        {
                            if (hands <= 0 || hungry) break;
                            if (Coins < (GameplayRules.CropOf(SowCrop(p.Uid))?.SeedCost ?? int.MaxValue)) break;
                            hands--;
                            Resow(p.Uid, now);
                        }
                        break;
                    case WorkJob.Sell:
                        foreach (var shop in _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Market && Working(d.Uid) && WorkOn(d.Uid, WorkJob.Sell)).OrderBy(d => d.Uid).ToList())
                        {
                            if (hands <= 0 || hungry) break;
                            var spare = farmhouse.Stock.Keys.Where(k => GameplayRules.SellPrice(shop.BuildingId, k) > 0 && Stocked(k) > Reserve(k) + GameplayRules.SellKeep)
                                .OrderByDescending(k => Stocked(k) - Reserve(k)).ThenBy(k => k, StringComparer.Ordinal).FirstOrDefault();
                            if (spare == null) continue;
                            if (!Pay(WorkJob.Sell)) break;
                            var n = Math.Min(carry, Stocked(spare) - Reserve(spare) - GameplayRules.SellKeep);
                            for (var i = 0; i < n; i++)
                            {
                                farmhouse.TakeStock(spare, 1);
                                var coins = MarketPrice(shop.BuildingId, spare);
                                Coins += coins;
                                Sold(spare);
                                report.Sold++;
                                report.SoldCoins += coins;
                            }
                            report.Loads++;
                        }
                        break;
                }
            }
            // the freight train: a crate at a time when the stores can spare it
            // sow a bare field: seed coins and a little food, no extra hand
            void Resow(int uid, double at)
            {
                var crop = SowCrop(uid);
                var seed = GameplayRules.CropOf(crop)?.SeedCost ?? int.MaxValue;
                if (Coins < seed) return;
                var cost = WorkEnergy(WorkJob.Replant, true);
                var h = houses.Where(x => x.Energy >= cost).OrderByDescending(x => x.Energy).FirstOrDefault();
                if (h == null) { hungry = true; return; }
                if (Plant(uid, crop, at) != GatherResult.Done) return;
                h.Energy -= cost;
                report.FoodUsed += cost;
                report.Replanted++;
                report.SeedCoins += seed;
            }

            if (WorkTrain && _train != null && hands > 0 && !hungry)
                for (var i = 0; i < _train.Crates.Count && hands > 0 && !hungry; i++)
                {
                    var c = _train.Crates[i];
                    if (c.Loaded || InStore(c.Item) - c.Need < Reserve(c.Item)) continue;
                    if (!Pay(WorkJob.Collect)) break;
                    if (LoadCrate(i)) report.Crates++;
                }
            if (hungry && report.Stopped.Length == 0 && WorkWaiting(now)) { report.Stopped = "food"; report.StoppedAt = now; }
            if (report.StorageFull && report.Stopped.Length == 0) { report.Stopped = "storage"; report.StoppedAt = now; }
        }

        /// <summary>There is switched-on work nobody could pay for (so "out of food" is a real stop, not an idle farm).</summary>
        private bool WorkWaiting(double now) =>
            _plots.Values.Any(p => Working(p.Uid) && WorkOn(p.Uid, WorkJob.Harvest) && p.State(now) == PlotState.Ripe)
            || _animals.Values.Any(a => Working(a.Uid) && WorkOn(a.Uid, WorkJob.Collect) && a.StockAt(now) > 0)
            || _machines.Values.Any(m => Working(m.Uid) && WorkOn(m.Uid, WorkJob.Collect) && m.Output > 0);

        /// <summary>What workers sow in a field: the chosen crop, else the last one, else the default (an open crop always).</summary>
        public string SowCrop(int uid)
        {
            var chosen = WorkCrop(uid);
            if (chosen != null && CropOpen(chosen)) return chosen;
            var last = _plots.TryGetValue(uid, out var p) ? p.LastCrop : null;
            return last != null && CropOpen(last) ? last : GameplayRules.DefaultCrop;
        }

        /// <summary>
        /// Hours of offline work the food on hand would pay for, from a dry run on a copy of the island
        /// (<paramref name="copy"/> = the island rebuilt from its save; the game makes it). Null when nothing stops it before the cap.
        /// </summary>
        public static double? FoodHours(IslandEconomy copy, double now)
        {
            copy.BackOnline();
            var r = copy.WorkOffline(now, now + copy.OfflineCapHours * 3600.0);
            return r.Stopped == "food" ? Math.Max(0, (r.StoppedAt - now) / 3600.0) : (double?)null;
        }

        // ---- save -------------------------------------------------------------------------------------

        private void SaveWork(EconomySave s)
        {
            s.awaySince = AwaySince;
            s.nightShift = NightShift;
            s.workPass = WorkPass;
            s.workTrain = WorkTrain;
            foreach (var kv in _work.OrderBy(k => k.Key))
                s.work.Add(new EconomySave.WorkSave { uid = kv.Key, on = kv.Value.On.OrderBy(j => j, StringComparer.Ordinal).ToList(), crop = kv.Value.Crop ?? "" });
        }

        private void RestoreWork(EconomySave s)
        {
            AwaySince = s.awaySince;
            NightShift = s.nightShift;
            WorkPass = s.workPass;
            WorkTrain = s.workTrain;
            _work.Clear();
            foreach (var w in s.work ?? new List<EconomySave.WorkSave>())
            {
                var sw = new WorkSwitch { Crop = string.IsNullOrEmpty(w.crop) ? null : w.crop };
                foreach (var j in w.on ?? new List<string>()) sw.On.Add(j);
                _work[w.uid] = sw;
            }
        }
    }
}
