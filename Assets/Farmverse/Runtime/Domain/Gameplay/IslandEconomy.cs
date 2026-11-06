#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>
    /// The walking economy of one island, run locally (gameplay first; the server port comes later).
    /// Buildings placed on the island register here by their placement id. The farmer gathers by
    /// standing near work (a step at a time), goods ride on their back in the <see cref="Basket"/>,
    /// and unloading on a department's pad either stores them (farmhouse), turns them into that
    /// department's energy (army house, workers' house, builders' hut) or sells them (market).
    /// Departments burn energy for their people; energy also buys levels. Time is passed in as
    /// seconds (UTC), so growth and upkeep carry on while the game is closed.
    /// </summary>
    public sealed partial class IslandEconomy
    {
        private readonly Dictionary<int, Department> _departments = new Dictionary<int, Department>();
        private readonly Dictionary<int, Plot> _plots = new Dictionary<int, Plot>();
        private readonly Dictionary<int, AnimalHouse> _animals = new Dictionary<int, AnimalHouse>();
        private readonly Dictionary<int, Machine> _machines = new Dictionary<int, Machine>();
        private readonly List<Construction> _sites = new List<Construction>();
        /// <summary>Materials handed to repairs before a load, until the repair sites are made again.</summary>
        private readonly Dictionary<int, Dictionary<string, int>> _givenToRepairs = new Dictionary<int, Dictionary<string, int>>();
        private readonly Dictionary<int, int> _crews = new Dictionary<int, int>();
        private readonly List<int> _finished = new List<int>();
        private readonly Dictionary<int, double> _orders = new Dictionary<int, double>();
        private readonly Dictionary<int, Health> _health = new Dictionary<int, Health>();
        private readonly Dictionary<int, int> _prices = new Dictionary<int, int>();
        private readonly Dictionary<int, string> _ids = new Dictionary<int, string>();
        private readonly Dictionary<int, (double x, double z)> _places = new Dictionary<int, (double, double)>();
        private double _lastInterest;
        private double _nextOrderAt;
        private bool _leveledUp;

        public IslandEconomy(int coins)
        {
            Coins = coins;
            Basket = new Basket(GameplayRules.BasketBase);
        }

        public int Coins { get; private set; }
        public Basket Basket { get; }
        /// <summary>The last time <see cref="Tick"/> ran (seconds).</summary>
        public double Now { get; private set; }

        public IReadOnlyDictionary<int, Department> Departments => _departments;
        public IReadOnlyDictionary<int, Plot> Plots => _plots;
        public IReadOnlyDictionary<int, AnimalHouse> Animals => _animals;
        /// <summary>Mills, bakeries, dairies…: raw goods in, richer goods out.</summary>
        public IReadOnlyDictionary<int, Machine> Machines => _machines;
        /// <summary>Buildings still being built, oldest first.</summary>
        public IReadOnlyList<Construction> Sites => _sites;
        /// <summary>How many builders are working each site right now (by building).</summary>
        public IReadOnlyDictionary<int, int> Crews => _crews;
        /// <summary>Supplies asked for from the farmhouse, by department: energy still to send.</summary>
        public IReadOnlyDictionary<int, double> SupplyOrders => _orders;

        /// <summary>Not a new building still going up (it may be damaged or ruined, or being repaired).</summary>
        public bool IsBuilt(int uid) => !_sites.Exists(c => c.Uid == uid && !c.Repair);

        /// <summary>Built and not ruined: it does its job.</summary>
        public bool Working(int uid) => IsBuilt(uid) && !(_health.TryGetValue(uid, out var h) && h.Ruined);

        public IReadOnlyDictionary<int, Health> Healths => _health;

        public bool IsRuined(int uid) => _health.TryGetValue(uid, out var h) && h.Ruined;

        /// <summary>Builders on the island: one comes with a farmhouse, the rest live in builders' huts (fed ones work).</summary>
        public int Builders
        {
            get
            {
                var n = 0;
                var farmhouse = false;
                foreach (var d in _departments.Values)
                {
                    if (!Working(d.Uid)) continue;
                    if (d.Levels.Kind == DepartmentKind.Farmhouse) farmhouse = true;
                    else if (d.Levels.Kind == DepartmentKind.BuildersHut && !d.Starving) n += d.Present;
                }
                return n + (farmhouse ? GameplayRules.FarmhouseBuilders + ExtraBuilders : 0);
            }
        }

        // ---- boosts and purchases (saved with the island, so the server runs them the same) -----------

        /// <summary>Fertilizer: crops sown before this moment (epoch seconds) grow twice as fast.</summary>
        public double FertUntil { get; set; }

        /// <summary>Worker Rush: until this moment (epoch seconds) builders put up buildings twice as fast (the island's people also walk and work 2×).</summary>
        public double RushUntil { get; set; }

        /// <summary>
        /// Fertilizer starts at <paramref name="now"/> and runs to <paramref name="until"/>: crops already growing grow twice
        /// as fast for the time it overlaps them, and crops sown before it ends grow 2× (see Plant).
        /// </summary>
        public void Fertilize(double now, double until)
        {
            if (until <= FertUntil) return;
            var from = Math.Max(now, FertUntil);   // the part of the window not already counted
            var window = until - from;
            if (window > 0)
                foreach (var plot in _plots.Values)
                {
                    if (plot.Crop == null || plot.ReadyAt <= from) continue;
                    var left = plot.ReadyAt - from;   // seconds at the speed it was sown with
                    plot.ReadyAt = from + (left <= 2 * window ? left / 2 : window + (left - 2 * window));
                }
            FertUntil = until;
        }

        /// <summary>Builders added by Builder's Passes (they live in the farmhouse).</summary>
        public int ExtraBuilders { get; set; }

        /// <summary>Basket slots added by Bigger Basket boosts, for good.</summary>
        public int BasketExtra { get; private set; }

        /// <summary>A Bigger Basket boost was used: more slots for good.</summary>
        public void AddBasketSlots(int n)
        {
            if (n <= 0) return;
            BasketExtra += n;
            RefreshBasket();
        }

        /// <summary>Instant Build: the oldest building site (not a repair) is finished now. False when nothing is being built.</summary>
        public bool FinishOneSite(double now)
        {
            var site = _sites.FirstOrDefault(c => !c.Repair);
            if (site == null) return false;
            site.Supplied = true;
            site.Done = site.Required;
            Build(0, now);
            return true;
        }

        /// <summary>Buildings finished since the last call (for the reveal), oldest first.</summary>
        public IReadOnlyList<int> TakeFinished()
        {
            var done = _finished.ToArray();
            _finished.Clear();
            return done;
        }

        // ---- buildings ----------------------------------------------------------------------------

        /// <summary>
        /// A building was placed (or loaded): give it the part of the economy it takes part in. With
        /// <paramref name="buildWork"/> above zero it starts as a construction site needing that many
        /// builder-seconds; until then it takes deliveries but doesn't work.
        /// </summary>
        public void AddBuilding(int uid, string buildingId, double now, double buildWork = 0, int price = 0)
        {
            _ids[uid] = buildingId;
            _prices[uid] = price;
            var max = GameplayRules.MaxHp(buildingId, price);
            _health[uid] = new Health(max);
            if (buildWork > 0 && !_sites.Exists(c => c.Uid == uid))
                _sites.Add(new Construction(uid, buildingId, buildWork, GameplayRules.MaterialsFor(buildingId, price)));
            if (buildingId == GameplayRules.FieldBuilding) _plots[uid] = new Plot(uid);
            else if (GameplayRules.ProducerOf(buildingId) is { } producer)
            {
                _animals[uid] = new AnimalHouse(uid, buildingId, producer, now);
                if (GameplayRules.SecondProducerOf(buildingId) is { } second) _animals[uid].Side = new SideStock(second, now);
            }
            else if (GameplayRules.DepartmentOf(buildingId) is { } levels) _departments[uid] = new Department(uid, buildingId, levels, now);
            else if (GameplayRules.RecipeOf(buildingId) is { } recipe) _machines[uid] = new Machine(uid, buildingId, recipe);
            RefreshBasket();
        }

        /// <summary>A building was sold or removed. A farmhouse's stock goes with it.</summary>
        public void RemoveBuilding(int uid)
        {
            _plots.Remove(uid);
            _animals.Remove(uid);
            _departments.Remove(uid);
            _machines.Remove(uid);
            _sites.RemoveAll(c => c.Uid == uid);
            _crews.Remove(uid);
            _orders.Remove(uid);
            _health.Remove(uid);
            _prices.Remove(uid);
            _ids.Remove(uid);
            _places.Remove(uid);
            RefreshBasket();
        }

        /// <summary>Where a building stands (metres on the ground), for bonuses that reach so far.</summary>
        public void Place(int uid, double x, double z) => _places[uid] = (x, z);

        /// <summary>Is a working building of this kind within <paramref name="reach"/> metres of a building?</summary>
        private bool NearWorking(int uid, string buildingId, double reach)
        {
            if (!_places.TryGetValue(uid, out var at)) return false;
            foreach (var kv in _ids)
            {
                if (kv.Value != buildingId || !Working(kv.Key) || !_places.TryGetValue(kv.Key, out var p)) continue;
                var dx = p.x - at.x; var dz = p.z - at.z;
                if (dx * dx + dz * dz <= reach * reach) return true;
            }
            return false;
        }

        /// <summary>A working building of this kind anywhere on the island.</summary>
        public bool Has(string buildingId) => _ids.Any(kv => kv.Value == buildingId && Working(kv.Key));

        public void AddCoins(int amount) => Coins = Math.Max(0, Coins + amount);

        public bool SpendCoins(int amount)
        {
            if (amount < 0 || Coins < amount) return false;
            Coins -= amount;
            return true;
        }

        // ---- fields -------------------------------------------------------------------------------

        public GatherResult Plant(int uid, string cropId, double now)
        {
            if (!_plots.TryGetValue(uid, out var plot)) return GatherResult.Nothing;
            if (!Working(uid) || plot.State(now) != PlotState.Empty) return GatherResult.Busy;
            var crop = GameplayRules.CropOf(cropId);
            if (crop == null || !CropOpen(crop.Id)) return GatherResult.Nothing;
            if (!SpendCoins(crop.SeedCost)) return GatherResult.NoCoins;
            plot.Crop = crop.Id;
            plot.LastCrop = crop.Id;
            plot.PlantedAt = now;
            // a potting shed nearby brings crops on faster; bees nearby make them yield more
            var speed = NearWorking(uid, "potting_shed", GameplayRules.GardenReach) ? 1.25 : 1.0;
            // the season and the weather it's sown in matter too
            var season = SeasonNow(now);
            var weather = SeasonsOn ? GameplayRules.WeatherAt(now) : GameplayRules.WeatherKind.Clear;
            if (weather == GameplayRules.WeatherKind.Drought && NearWorking(uid, "facility_well", GameplayRules.WellReach)) weather = GameplayRules.WeatherKind.Clear;   // the well waters it
            speed *= season.Growth * GameplayRules.WeatherGrowth(weather);
            if (now < FertUntil) speed *= 2;   // Fertilizer boost
            plot.ReadyAt = now + crop.GrowSeconds / speed;
            plot.Left = crop.Yield + season.YieldBonus;
            var live = EventNow(now);
            if (live != null && live.YieldBonus > 0 && live.Covers(crop.Id)) plot.Left += live.YieldBonus;   // the month's event
            Count("planted");
            return GatherResult.Done;
        }

        /// <summary>Take one unit off a ripe field into the basket.</summary>
        public GatherResult HarvestStep(int uid, double now)
        {
            if (!_plots.TryGetValue(uid, out var plot)) return GatherResult.Nothing;
            if (plot.State(now) != PlotState.Ripe || plot.Crop == null) return GatherResult.NotReady;
            if (Basket.Add(HarvestItem(uid, plot.PlantedAt, plot.Left, plot.Crop), 1) == 0) return GatherResult.BasketFull;
            Count("harvested");
            plot.Left--;
            if (plot.Left <= 0) plot.Reset();
            return GatherResult.Took;
        }

        /// <summary>Take one unit of what an animal house has made into the basket.</summary>
        public GatherResult CollectStep(int uid, double now)
        {
            if (!_animals.TryGetValue(uid, out var house)) return GatherResult.Nothing;
            if (!Working(uid)) return GatherResult.NotReady;
            if (house.StockAt(now) <= 0)
            {
                // the second good (the mine's ore) when the first has run out
                if (house.Side is not { } side || side.StockAt(now) <= 0) return GatherResult.NotReady;
                if (Basket.Add(side.Producer.Item, 1) == 0) return GatherResult.BasketFull;
                side.Take(now);
                Count("made:" + side.Producer.Item);
                return GatherResult.Took;
            }
            if (Basket.Add(house.Producer.Item, 1) == 0) return GatherResult.BasketFull;
            house.Take(now);
            Count("made:" + house.Producer.Item);
            return GatherResult.Took;
        }

        // ---- machines -----------------------------------------------------------------------------

        /// <summary>The farmer standing in a machine puts one good it uses from the basket into its hopper; null when nothing fits.</summary>
        public string? FeedMachineStep(int uid, double now)
        {
            if (!_machines.TryGetValue(uid, out var m) || !Working(uid)) return null;
            foreach (var (item, _) in m.Recipe.Inputs)
            {
                if (m.HopperOf(item) >= GameplayRules.HopperSize || Basket.CountOf(item) == 0) continue;
                Basket.TakeItem(item);
                m.Fill(item, 1);
                m.Advance(now);
                return item;
            }
            return null;
        }

        /// <summary>The farmer takes one finished good from a machine into the basket.</summary>
        public GatherResult CollectMachineStep(int uid, double now)
        {
            if (!_machines.TryGetValue(uid, out var m) || !Working(uid)) return GatherResult.Nothing;
            m.Advance(now);
            if (m.Output <= 0) return GatherResult.NotReady;
            if (Basket.Add(m.Recipe.Output, 1) == 0) return GatherResult.BasketFull;
            m.Output--;
            Count("made:" + m.Recipe.Output);
            m.Advance(now);   // room again: the next batch can start
            return GatherResult.Took;
        }

        /// <summary>A worker takes finished goods from a machine.</summary>
        public int TakeOutput(int uid, double now, int max, List<string> into, bool credit = true)
        {
            if (!_machines.TryGetValue(uid, out var m) || !Working(uid)) return 0;
            m.Advance(now);
            var n = Math.Min(max, m.Output);
            for (var i = 0; i < n; i++) into.Add(m.Recipe.Output);
            m.Output -= n;
            if (credit) Count("made:" + m.Recipe.Output, n);
            m.Advance(now);
            return n;
        }

        /// <summary>How many more of its inputs a machine's hopper takes (all inputs together).</summary>
        public int HopperRoom(int uid) =>
            _machines.TryGetValue(uid, out var m) ? m.Recipe.Inputs.Sum(i => Math.Max(0, GameplayRules.HopperSize - m.HopperOf(i.item))) : 0;

        /// <summary>A worker loads a machine's inputs at a farmhouse (what the hopper is shortest of first).</summary>
        public int LoadInputs(int farmhouseUid, int machineUid, int maxUnits, List<string> into, bool keep = false)
        {
            if (!_machines.TryGetValue(machineUid, out var m) || !_departments.TryGetValue(farmhouseUid, out var house)) return 0;
            var n = 0;
            var taking = new Dictionary<string, int>(StringComparer.Ordinal);
            while (n < maxUnits)
            {
                string? pick = null;
                var best = int.MaxValue;
                foreach (var (item, _) in m.Recipe.Inputs)
                {
                    var will = m.HopperOf(item) + (taking.TryGetValue(item, out var t) ? t : 0);
                    if (will >= GameplayRules.HopperSize || !house.Stock.ContainsKey(item)) continue;
                    if (keep && Stocked(item) <= Reserve(item)) continue;   // never the last few (the player's reserve)
                    if (will < best) { best = will; pick = item; }
                }
                if (pick == null) break;
                house.TakeStock(pick, 1);
                taking[pick] = (taking.TryGetValue(pick, out var c) ? c : 0) + 1;
                into.Add(pick);
                n++;
            }
            return n;
        }

        /// <summary>Goods a worker brought put into a machine's hopper.</summary>
        public void FillMachine(int uid, IEnumerable<string> items, double now)
        {
            if (!_machines.TryGetValue(uid, out var m)) return;
            foreach (var item in items) if (m.Recipe.Needs(item) > 0) m.Fill(item, 1);
            m.Advance(now);
        }

        // ---- departments --------------------------------------------------------------------------

        /// <summary>Hand one unit from the basket to a department standing on its pad.</summary>
        public UnloadResult UnloadStep(int uid)
        {
            // a site can be stocked up ahead (it opens fed); a ruin takes nothing
            if (!_departments.TryGetValue(uid, out var dept) || Basket.IsEmpty || IsRuined(uid)) return UnloadResult.None;
            if (!IsBuilt(uid) && dept.Levels.Kind is DepartmentKind.Market or DepartmentKind.Silo or DepartmentKind.Farmhouse) return UnloadResult.None;
            // shops and silos only take what they deal in: the topmost good of that kind
            if (dept.Levels.Kind == DepartmentKind.Market)
            {
                var sell = Basket.Stacks.Reverse().Select(st => st.Item).FirstOrDefault(i => GameplayRules.SellPrice(dept.BuildingId, i) > 0);
                if (sell == null) return UnloadResult.None;
                Basket.TakeItem(sell);
                var coins = MarketPrice(dept.BuildingId, sell);
                Coins += coins;
                Sold(sell);
                return new UnloadResult(sell, UnloadOutcome.Sold, coins);
            }
            if (dept.Levels.Kind == DepartmentKind.Silo)
            {
                var crop = Basket.Stacks.Reverse().Select(st => st.Item).FirstOrDefault(GameplayRules.IsCrop);
                if (crop == null) return UnloadResult.None;
                if (dept.Stored >= dept.StorageCapacity) return new UnloadResult(crop, UnloadOutcome.StorageFull, 0);
                Basket.TakeItem(crop);
                dept.Store(crop, 1);
                return new UnloadResult(crop, UnloadOutcome.Stored, 1);
            }
            var item = Basket.TakeOne()!;
            switch (dept.Levels.Kind)
            {
                case DepartmentKind.Farmhouse:
                    if (dept.Stored >= dept.StorageCapacity)
                    {
                        Basket.PutBack(item);
                        return new UnloadResult(item, UnloadOutcome.StorageFull, 0);
                    }
                    dept.Store(item, 1);
                    Count("stored");
                    return new UnloadResult(item, UnloadOutcome.Stored, 1);
                case DepartmentKind.Market:
                    var price = GameplayRules.PriceOf(item);
                    Coins += price;
                    return new UnloadResult(item, UnloadOutcome.Sold, price);
                default:
                    var energy = GameplayRules.EnergyOf(item);
                    dept.Energy += energy;
                    if (item == "iron" && dept.Levels.Kind == DepartmentKind.ArmyHouse) dept.Iron++;
                    return new UnloadResult(item, UnloadOutcome.Energy, energy);
            }
        }

        /// <summary>The farmer takes goods back out of a farmhouse or silo into the basket. Returns how many came out.</summary>
        public int Withdraw(int uid, string item, int count)
        {
            if (!_departments.TryGetValue(uid, out var d) || !Working(uid) || count <= 0) return 0;
            if (d.Levels.Kind is not (DepartmentKind.Farmhouse or DepartmentKind.Silo)) return 0;
            var n = Math.Min(count, Math.Min(d.Stock.TryGetValue(item, out var have) ? have : 0, Basket.Free));
            if (n <= 0) return 0;
            d.TakeStock(item, n);
            Basket.Add(item, n);
            return n;
        }

        /// <summary>Energy (or, for a farmhouse, stored goods worth that much energy) the next level needs; null at the top level.</summary>
        public int? UpgradeCost(int uid) =>
            _departments.TryGetValue(uid, out var d) && d.Level < d.Levels.MaxLevel && d.Level - 1 < d.Levels.UpgradeCost.Length
            && d.Level < GameplayRules.DepartmentMaxLevelAt(BestLevel)
                ? d.Levels.UpgradeCost[d.Level - 1]
                : (int?)null;

        /// <summary>What a department has toward its next level: its energy, or a farmhouse's stock valued as energy.</summary>
        public int UpgradeFunds(int uid)
        {
            if (!_departments.TryGetValue(uid, out var d)) return 0;
            return d.Levels.Kind is DepartmentKind.Farmhouse or DepartmentKind.Silo ? d.StockEnergy : (int)Math.Floor(d.Energy);
        }

        public bool Upgrade(int uid)
        {
            if (!Working(uid) || !_departments.TryGetValue(uid, out var d) || UpgradeCost(uid) is not int cost || UpgradeFunds(uid) < cost) return false;
            // a bigger house needs building materials from the farmhouse too
            var mats = GameplayRules.DepartmentUpgradeMaterials(d.BuildingId, d.Levels.Kind, d.Level);
            if (MissingMaterials(mats).Count > 0) return false;
            TakeMaterials(mats);
            Count("upgrades");
            if (d.Levels.Kind is DepartmentKind.Farmhouse or DepartmentKind.Silo) d.SpendStockAsEnergy(cost);
            else d.Energy -= cost;
            d.Level++;
            RefreshBasket();
            return true;
        }

        // ---- people: losses, work, supplies ---------------------------------------------------------

        /// <summary>One of a department's people is lost (killed in a fight, worn out). False when nobody was left.</summary>
        public bool LosePerson(int uid)
        {
            if (!_departments.TryGetValue(uid, out var d) || d.Present <= 0) return false;
            d.Lost++;
            return true;
        }

        /// <summary>Energy used up by work (a load carried, a fight): working hard drains the tank, and an empty tank starves.</summary>
        public void SpendEnergy(int uid, double energy)
        {
            if (!_departments.TryGetValue(uid, out var d) || energy <= 0) return;
            d.Energy = Math.Max(0, d.Energy - energy);
        }

        /// <summary>People of a kind who can work right now, by house: built, fed, and still there.</summary>
        public IEnumerable<(int house, int people)> Workforce(DepartmentKind kind)
        {
            foreach (var d in _departments.Values)
                if (d.Levels.Kind == kind && Working(d.Uid) && d.Present > 0) yield return (d.Uid, d.Present);
        }

        /// <summary>A farm worker takes up to <paramref name="max"/> units off a ripe field (not into the farmer's basket).</summary>
        public int TakeHarvest(int uid, double now, int max, List<string> into)
        {
            if (!_plots.TryGetValue(uid, out var plot) || plot.State(now) != PlotState.Ripe || plot.Crop == null) return 0;
            var n = Math.Min(max, plot.Left);
            for (var i = 0; i < n; i++) into.Add(HarvestItem(uid, plot.PlantedAt, plot.Left - i, plot.Crop));
            plot.Left -= n;
            Count("harvested", n);
            if (plot.Left <= 0) plot.Reset();
            return n;
        }

        /// <summary>A farm worker (or a raider) takes up to <paramref name="max"/> of what an animal house has made.</summary>
        /// <param name="credit">False for raiders: what they steal isn't the island's work.</param>
        public int TakeProduce(int uid, double now, int max, List<string> into, bool credit = true)
        {
            if (!_animals.TryGetValue(uid, out var house) || !Working(uid)) return 0;
            var n = 0;
            while (n < max && house.StockAt(now) > 0)
            {
                house.Take(now);
                into.Add(house.Producer.Item);
                n++;
            }
            if (credit) Count("made:" + house.Producer.Item, n);
            var m = 0;
            while (n + m < max && house.Side is { } side && side.StockAt(now) > 0)
            {
                side.Take(now);
                into.Add(side.Producer.Item);
                m++;
            }
            if (credit && m > 0) Count("made:" + house.Side!.Producer.Item, m);
            return n + m;
        }

        /// <summary>Plant a field again with what grew there last (farm workers keep fields going while there are coins for seed).</summary>
        public GatherResult Replant(int uid, double now) =>
            _plots.TryGetValue(uid, out var plot)
                ? Plant(uid, SowCrop(uid), now)
                : GatherResult.Nothing;

        /// <summary>The crop can be planted at this island's level.</summary>
        public bool CropOpen(string cropId) => GameplayRules.CropUnlockLevel(cropId) <= BestLevel;

        /// <summary>Fields on the island (built or being built).</summary>
        public int FieldCount => _plots.Count;

        /// <summary>Put goods carried by a worker into a farmhouse; returns how many fit.</summary>
        public int Store(int farmhouseUid, string item, int n)
        {
            if (!_departments.TryGetValue(farmhouseUid, out var d) || d.Levels.Kind != DepartmentKind.Farmhouse || !Working(farmhouseUid)) return 0;
            var fit = Math.Max(0, Math.Min(n, d.StorageCapacity - d.Stored));
            d.Store(item, fit);
            Count("stored", fit);
            return fit;
        }

        /// <summary>Ask the farmhouse to send a department this much energy in goods (farm workers carry it over). Replaces any earlier order.</summary>
        public void OrderSupplies(int uid, double energy)
        {
            if (!_departments.TryGetValue(uid, out var d) || d.Levels.Kind is DepartmentKind.Farmhouse or DepartmentKind.Market) return;
            if (energy <= 0) _orders.Remove(uid); else _orders[uid] = energy;
        }

        /// <summary>
        /// A worker loads goods at a farmhouse for an order: the richest goods first (fewer trips), up to
        /// <paramref name="maxUnits"/>, and no more than the order still needs. The order shrinks as they're loaded.
        /// </summary>
        public int LoadSupplies(int farmhouseUid, int deptUid, int maxUnits, List<string> into)
        {
            if (!_orders.TryGetValue(deptUid, out var wanted) || !_departments.TryGetValue(farmhouseUid, out var house) || house.Levels.Kind != DepartmentKind.Farmhouse) return 0;
            var n = 0;
            while (n < maxUnits && wanted > 0)
            {
                var item = house.Stock.Keys.OrderByDescending(GameplayRules.EnergyOf).ThenBy(k => k, StringComparer.Ordinal).FirstOrDefault();
                if (item == null) break;
                house.TakeStock(item, 1);
                into.Add(item);
                wanted -= GameplayRules.EnergyOf(item);
                n++;
            }
            if (wanted <= 0) _orders.Remove(deptUid); else _orders[deptUid] = wanted;
            return n;
        }

        /// <summary>Goods arriving at a department become its energy.</summary>
        public void Deliver(int deptUid, IEnumerable<string> items)
        {
            if (!_departments.TryGetValue(deptUid, out var d)) return;
            foreach (var item in items)
            {
                d.Energy += GameplayRules.EnergyOf(item);
                if (item == "iron" && d.Levels.Kind == DepartmentKind.ArmyHouse) d.Iron++;
            }
        }

        // ---- while the player was away -------------------------------------------------------------

        // ---- raids ---------------------------------------------------------------------------------

        /// <summary>Raiders carry off up to <paramref name="n"/> stored goods from a farmhouse (the biggest piles first).</summary>
        public int Steal(int farmhouseUid, int n, List<string> into)
        {
            if (!_departments.TryGetValue(farmhouseUid, out var d)) return 0;
            var took = 0;
            while (took < n)
            {
                var item = d.Stock.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key).FirstOrDefault();
                if (item == null) break;
                d.TakeStock(item, 1);
                into.Add(item);
                took++;
            }
            return took;
        }

        /// <summary>Raiders trample a field: whatever was growing is lost. Returns the crop, or null if it was empty.</summary>
        public string? Trample(int uid)
        {
            if (!_plots.TryGetValue(uid, out var plot) || plot.Crop == null) return null;
            var crop = plot.Crop;
            plot.Reset();
            return crop;
        }

        // ---- time ---------------------------------------------------------------------------------

        /// <summary>Run upkeep up to <paramref name="now"/>.</summary>
        public void Tick(double now)
        {
            var dt = Now > 0 ? Math.Max(0, now - Now) : 0;
            // a long gap (the game was closed) burns energy but doesn't wipe anybody out overnight
            var away = _tickAway || dt > GameplayRules.AwayGapSeconds;
            foreach (var d in _departments.Values)
            {
                if (IsBuilt(d.Uid)) d.Upkeep(now, away, Mods(d));
                else d.LastUpkeep = now;   // nobody lives there yet
            }
            RefreshPeople();
            Build(dt, now);
            var fast = Has("library");
            foreach (var m in _machines.Values)
            {
                m.Speed = (fast ? 1 / 0.85 : 1) * GameplayRules.BuildingLevelSpeed(m.Level);
                if (Working(m.Uid)) m.Advance(now);
            }
            Mend(dt, now);
            Rot(now);
            foreach (var a in _animals.Values) if (Working(a.Uid)) _spoiled += a.Spoil(now);
            Weather(now);
            DecayMarket(dt);
            RollDay(now);
            PayInterest(now);
            Post(now);
            TrainTick(now);
            var score = Score;
            if (score > BestScore) BestScore = score;
            CheckLevel();
            Now = now;
        }

        // ---- neglect: ripe crops left standing rot ------------------------------------------------

        private int _rotted;

        /// <summary>Units lost to rot since last asked (for the screen to say so once).</summary>
        public int TakeRotted()
        {
            var n = _rotted;
            _rotted = 0;
            return n;
        }

        private void Rot(double now)
        {
            foreach (var plot in _plots.Values)
            {
                if (plot.Crop == null || now < plot.ReadyAt) continue;
                var start = Math.Max(plot.RotFrom, plot.ReadyAt + GameplayRules.RipeKeepSeconds(plot.Crop));
                if (now <= start) continue;
                var n = (int)Math.Floor((now - start) / GameplayRules.RotEverySeconds);
                if (n <= 0) continue;
                var lost = Math.Min(n, plot.Left);
                plot.Left -= lost;
                plot.RotFrom = start + n * GameplayRules.RotEverySeconds;
                _rotted += lost;
                if (plot.Left <= 0) plot.Reset();
            }
        }

        // ---- bonuses from town buildings ---------------------------------------------------------

        private UpkeepMods Mods(Department d) => new UpkeepMods((Has("clinic") ? 2 : 1) * (NearDecor(d.Uid) ? GameplayRules.DecorRecruit : 1), Has("chapel") ? 2 : 1, SeasonNow(Now).Appetite);

        /// <summary>A decoration stands near this building.</summary>
        public bool NearDecor(int uid) => _ids.Any(kv => GameplayRules.IsDecor(kv.Value) && Working(kv.Key) && Within(uid, kv.Key, GameplayRules.DecorReach));

        /// <summary>A community hall adds one person to every workers' house and builders' hut; a town hall a fifth more everywhere.</summary>
        private void RefreshPeople()
        {
            var hall = Has("community_hall");
            var town = Has("town_hall");
            foreach (var d in _departments.Values)
            {
                var basePeople = d.Levels.People[Math.Min(d.Level, d.Levels.People.Length) - 1];
                var extra = hall && d.Levels.Kind is DepartmentKind.WorkersHouse or DepartmentKind.BuildersHut ? 1 : 0;
                if (town && basePeople > 0) extra += (int)Math.Ceiling(basePeople * 0.2);
                d.Extra = extra;
            }
        }

        /// <summary>How much a farm worker carries (a school teaches them to carry more).</summary>
        public int WorkerCarry => GameplayRules.WorkerCarry + (Has("school") ? GameplayRules.SchoolCarryBonus : 0) + CrewCarryBonus;

        /// <summary>Seconds of warning before raiders land (a police station watches the sea).</summary>
        public double RaidWarning => Has("police_station") ? 60 : 30;

        private void PayInterest(double now)
        {
            if (!Has("bank")) { _lastInterest = 0; return; }
            if (_lastInterest <= 0) { _lastInterest = now; return; }
            for (var i = 0; i < 48 && now - _lastInterest >= GameplayRules.InterestSeconds; i++)
            {
                Coins += (int)Math.Min(GameplayRules.InterestCap, Math.Floor(Coins * GameplayRules.InterestRate));
                _lastInterest += GameplayRules.InterestSeconds;
            }
            if (now - _lastInterest >= GameplayRules.InterestSeconds) _lastInterest = now;   // more than eight hours: the rest is lost
        }

        // ---- the horse ---------------------------------------------------------------------------

        /// <summary>The horse can be ridden: there's a working stable with something in its tank.</summary>
        public bool HorseReady => _departments.Values.Any(d => d.Levels.Kind == DepartmentKind.Stable && Working(d.Uid) && d.Energy >= GameplayRules.RideEnergy);

        /// <summary>A ride costs the stable's energy (the horse has to be fed).</summary>
        public bool PayForRide()
        {
            var stable = _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Stable && Working(d.Uid) && d.Energy >= GameplayRules.RideEnergy)
                .OrderByDescending(d => d.Energy).FirstOrDefault();
            if (stable == null) return false;
            stable.Energy -= GameplayRules.RideEnergy;
            return true;
        }

        // ---- health, damage, ruin and repair -------------------------------------------------------

        /// <summary>
        /// A raider hits a building. Returns true when this blow ruined it: it stops working and loses
        /// what a ruin loses (a level and half the people and energy of a department, half a store's
        /// stock, a machine's hopper and tray, an animal house's stock and feed, a field's crop).
        /// </summary>
        public bool Damage(int uid, double amount, double now)
        {
            if (!_health.TryGetValue(uid, out var h) || h.Ruined || amount <= 0) return false;
            // young islands are hurt, never wrecked
            h.Hp = Math.Max(BestLevel < GameplayRules.RaidRuinLevel ? 1 : 0, h.Hp - amount);
            h.LastHit = now;
            _sites.RemoveAll(c => c.Uid == uid && c.Repair);   // repairs wait until the fighting's over
            if (h.Hp > 0) return false;
            Ruin(uid, now);
            return true;
        }

        /// <summary>What the last ruin cost (for the alert), and when it happened.</summary>
        public List<string> LastRuinLosses { get; } = new List<string>();
        public double LastRuinAt { get; private set; }

        private void Ruin(int uid, double now)
        {
            var h = _health[uid];
            h.Ruined = true;
            h.Hp = 0;
            LastRuinAt = now;
            LastRuinLosses.Clear();
            if (_departments.TryGetValue(uid, out var d))
            {
                // half the people die or flee; the house shrinks a level around the survivors
                var before = d.Present;
                var survivors = before - (int)Math.Ceiling(before / 2.0);
                if (d.Level > 1) { d.Level--; LastRuinLosses.Add("a level"); }
                d.Energy *= 0.5;
                d.Lost = Math.Max(0, d.People - survivors);
                if (before > survivors) LastRuinLosses.Add($"{before - survivors} people");
                foreach (var kv in d.Stock.ToList())
                {
                    var lose = (kv.Value + 1) / 2;
                    d.TakeStock(kv.Key, lose);
                    if (lose > 0) LastRuinLosses.Add($"{lose} {kv.Key}");
                }
            }
            if (_animals.TryGetValue(uid, out var a)) { a.Restore(0, now); a.Feed = 0; LastRuinLosses.Add("the animals' feed"); }
            if (_machines.TryGetValue(uid, out var m)) { m.Wreck(); LastRuinLosses.Add("everything in the machine"); }
            if (_plots.TryGetValue(uid, out var p) && p.Crop != null) { LastRuinLosses.Add("the crop"); p.Reset(); }
            RefreshBasket();
        }

        /// <summary>
        /// Builders mend what's damaged (wood from the farmhouse) once nobody has hit it for a while; a
        /// fire station's crews patch buildings in reach for free.
        /// </summary>
        private void Mend(double dt, double now)
        {
            var fire = _ids.Where(kv => kv.Value == "fire_station" && Working(kv.Key)).Select(kv => kv.Key).ToList();
            foreach (var kv in _health)
            {
                var h = kv.Value;
                if (h.Hp >= h.Max || !IsBuilt(kv.Key) || now - h.LastHit < 10) continue;
                if (!h.Ruined && fire.Count > 0 && fire.Any(f => f != kv.Key && Within(f, kv.Key, GameplayRules.FireReach)))
                {
                    h.Hp = Math.Min(h.Max, h.Hp + GameplayRules.FireRepairPerSecond * dt);
                    if (h.Hp >= h.Max) _sites.RemoveAll(c => c.Uid == kv.Key && c.Repair);
                    continue;
                }
                if (_sites.Exists(c => c.Uid == kv.Key)) continue;
                var id = _ids.TryGetValue(kv.Key, out var b) ? b : "";
                var price = _prices.TryGetValue(kv.Key, out var pr) ? pr : 0;
                var missing = h.Max - h.Hp;
                var work = h.Ruined ? GameplayRules.BuildWork(id, price) : Math.Max(5, missing / 4);
                var wood = (int)Math.Ceiling(missing / GameplayRules.HpPerWood);
                var repair = new Construction(kv.Key, id, work, wood > 0 ? new[] { ("wood", wood) } : Array.Empty<(string, int)>(), repair: true);
                if (_givenToRepairs.TryGetValue(kv.Key, out var given))
                {
                    foreach (var g in given) repair.Given[g.Key] = g.Value;
                    _givenToRepairs.Remove(kv.Key);
                    if (repair.Materials.All(m => repair.StillNeeds(m.item) <= 0)) repair.Supplied = true;
                }
                _sites.Add(repair);
            }
        }

        private bool Within(int a, int b, double reach)
        {
            if (!_places.TryGetValue(a, out var p) || !_places.TryGetValue(b, out var q)) return false;
            var dx = p.x - q.x; var dz = p.z - q.z;
            return dx * dx + dz * dz <= reach * reach;
        }

        // ---- threat and score --------------------------------------------------------------------

        /// <summary>How tempting the island is to raiders: what it's worth, how far it's grown, what it has stored.</summary>
        public double Threat
        {
            get
            {
                double worth = 0;
                foreach (var kv in _prices) if (IsBuilt(kv.Key)) worth += kv.Value;
                var levels = _departments.Values.Sum(d => d.Level - 1);
                var stored = _departments.Values.Sum(d => (double)d.StockEnergy);
                return worth / 1000.0 + levels + stored / 1000.0;
            }
        }

        /// <summary>The island's fighting strength: warriors (half when hungry, more with iron) plus police guards.</summary>
        public double Defence
        {
            get
            {
                double sum = 0;
                foreach (var d in _departments.Values)
                {
                    if (!Working(d.Uid)) continue;
                    if (d.Levels.Kind == DepartmentKind.ArmyHouse) sum += d.Present * (d.Starving ? 0.5 : 1) * (d.Armed ? 1.3 : 1);
                    else if (d.Levels.Kind == DepartmentKind.Watch) sum += d.Present * (d.Starving ? 0.5 : 1);
                }
                return sum;
            }
        }

        /// <summary>World score: buildings, levels, people, stores and coins together.</summary>
        public int Score
        {
            get
            {
                double s = 0;
                foreach (var kv in _prices) if (Working(kv.Key)) s += kv.Value / 10.0;
                foreach (var d in _departments.Values) s += (d.Level - 1) * 100 + d.Present * 20 + d.StockEnergy / 10.0;
                s += Coins / 100.0;   // what you've built counts far more than what you've saved
                return (int)s;
            }
        }

        /// <summary>Island level from the XP earned (see <see cref="GameplayRules.LevelForXp"/>).</summary>
        public int IslandLevel => GameplayRules.LevelForXp(Xp);

        /// <summary>The highest island level reached (the shop keeps what it has unlocked even if the score drops after a raid).</summary>
        public int BestLevel { get; private set; } = 1;

        /// <summary>True once after the island reaches a new level.</summary>
        public bool TakeLevelUp()
        {
            var up = _leveledUp;
            _leveledUp = false;
            return up;
        }

        public bool Unlocked(string buildingId) => GameplayRules.UnlockLevel(buildingId) <= BestLevel;

        // ---- post office -------------------------------------------------------------------------

        private readonly List<DeliveryOrder> _delivery = new List<DeliveryOrder>();

        /// <summary>The post office's open delivery orders (1 at level 4, up to 4 at 25: <see cref="GameplayRules.OrderSlots"/>).</summary>
        public IReadOnlyList<DeliveryOrder> Orders => _delivery;

        /// <summary>The first open order (null when none).</summary>
        public DeliveryOrder? Order => _delivery.Count > 0 ? _delivery[0] : null;

        /// <summary>Picks orders from <see cref="Rng"/>; tests can seed it.</summary>
        public Random Rng { get; set; } = new Random();

        /// <summary>Set when an order was finished or ran out (for the screen to say so once).</summary>
        public DeliveryOrder? LastClosed { get; private set; }

        public DeliveryOrder? TakeClosed()
        {
            var o = LastClosed;
            LastClosed = null;
            return o;
        }

        /// <summary>Goods the island can make right now: crops, and what its animal houses, producers and machines turn out.</summary>
        public IReadOnlyList<string> Makeable()
        {
            // only what this island owns the means to make: crops opened at its level on its own fields,
            // its working animal houses and machines, and wood from chopping
            var goods = new SortedSet<string>(StringComparer.Ordinal) { "wood" };
            if (_plots.Count > 0)
                foreach (var c in GameplayRules.Crops)
                    if (GameplayRules.CropUnlockLevel(c.Id) <= BestLevel) goods.Add(c.Id);
            foreach (var a in _animals.Values) if (Working(a.Uid)) goods.Add(a.Producer.Item);
            foreach (var m in _machines.Values) if (Working(m.Uid)) goods.Add(m.Recipe.Output);
            return goods.ToList();
        }

        private void Post(double now)
        {
            if (!Has("post_office")) { _delivery.Clear(); return; }
            for (var i = _delivery.Count - 1; i >= 0; i--)
            {
                if (now < _delivery[i].Expires) continue;
                _delivery[i].Expired = true;
                LastClosed = _delivery[i];
                _delivery.RemoveAt(i);
                _nextOrderAt = Math.Max(_nextOrderAt, now + GameplayRules.OrderGapSeconds);
            }
            if (_delivery.Count >= GameplayRules.OrderSlots(BestLevel)) return;
            if (_nextOrderAt <= 0) _nextOrderAt = now + 20;
            if (now < _nextOrderAt) return;
            // dearer goods come up more often as the island grows (they pay better); never two orders for one good
            var goods = Makeable().Where(g => _delivery.All(o => o.Item != g)).ToList();
            if (goods.Count == 0) return;
            var pick = goods.OrderByDescending(g => GameplayRules.PriceOf(g) * (0.5 + Rng.NextDouble())).Take(Math.Max(2, BestLevel + 1)).ToList();
            var item = pick[Rng.Next(pick.Count)];
            var count = GameplayRules.OrderCount(item, BestLevel);
            _delivery.Add(new DeliveryOrder(item, count, GameplayRules.OrderReward(item, count), now + GameplayRules.OrderSeconds));
            // the next slot fills a little later
            _nextOrderAt = now + GameplayRules.OrderGapSeconds;
        }

        /// <summary>The farmer standing in the post office hands over one unit of what the order asks for. Coins when it's complete.</summary>
        public UnloadResult PostStep(int uid)
        {
            if (_delivery.Count == 0 || !Working(uid) || !_ids.TryGetValue(uid, out var id) || id != "post_office") return UnloadResult.None;
            // the first open order something in the basket is for
            var order = _delivery.FirstOrDefault(o => Basket.Stacks.Any(st => st.Item == o.Item));
            if (order == null) return UnloadResult.None;
            Basket.TakeItem(order.Item);
            order.Given++;
            if (order.Given < order.Count) return new UnloadResult(order.Item, UnloadOutcome.Stored, 1);
            Coins += order.Reward;
            Count("orders");
            order.Done = true;
            LastClosed = order;
            _delivery.Remove(order);
            _nextOrderAt = Math.Max(_nextOrderAt, Now + GameplayRules.OrderGapSeconds);
            return new UnloadResult(order.Item, UnloadOutcome.Sold, order.Reward);
        }

        /// <summary>Raiders reaching a farmhouse take some coins, unless they're in a bank. Returns what was taken.</summary>
        public int StealCoins()
        {
            if (Has("bank")) return 0;
            var take = (int)Math.Floor(Coins * GameplayRules.CoinTheft);
            Coins -= take;
            return take;
        }

        /// <summary>A new island's farmhouse starts with some wood for its first buildings.</summary>
        public void GrantStarterWood(int farmhouseUid)
        {
            Store(farmhouseUid, "wood", GameplayRules.StarterWood);
            Store(farmhouseUid, "stone", GameplayRules.StarterStone);
        }

        // ---- animals' feed -----------------------------------------------------------------------

        /// <summary>The farmer standing in an animal house tips one good the animals eat from the basket into the trough.</summary>
        public string? FeedAnimalsStep(int uid, double now)
        {
            if (!_animals.TryGetValue(uid, out var a) || !a.Producer.Eats || !Working(uid)) return null;
            foreach (var st in Basket.Stacks.Reverse())
            {
                var v = GameplayRules.FeedValue(st.Item);
                if (v <= 0 || a.Feed + v > GameplayRules.TroughSize) continue;
                var item = st.Item;
                Basket.TakeItem(item);
                a.AddFeed(now, v);
                Count("fed");
                return item;
            }
            return null;
        }

        /// <summary>A worker loads feed for an animal house at a farmhouse (richest feed first).</summary>
        public int LoadFeed(int farmhouseUid, int animalUid, int maxUnits, List<string> into)
        {
            if (!_animals.TryGetValue(animalUid, out var a) || !_departments.TryGetValue(farmhouseUid, out var house)) return 0;
            var room = GameplayRules.TroughSize - a.Feed;
            var n = 0;
            while (n < maxUnits && room > 0)
            {
                var item = house.Stock.Keys.Where(k => GameplayRules.FeedValue(k) > 0 && GameplayRules.FeedValue(k) <= room)
                    .OrderByDescending(GameplayRules.FeedValue).ThenBy(k => k, StringComparer.Ordinal).FirstOrDefault();
                if (item == null) break;
                house.TakeStock(item, 1);
                into.Add(item);
                room -= GameplayRules.FeedValue(item);
                n++;
            }
            return n;
        }

        public void FillTrough(int animalUid, IEnumerable<string> items, double now)
        {
            if (!_animals.TryGetValue(animalUid, out var a)) return;
            foreach (var item in items) a.AddFeed(now, GameplayRules.FeedValue(item));
            Count("fed");
        }

        /// <summary>Builders work the oldest sites first, at most a few to a site; finished buildings open and start working.</summary>
        private void Build(double dt, double now)
        {
            _crews.Clear();
            if (_sites.Count == 0) return;
            var free = Builders;
            foreach (var site in _sites)
            {
                // a repair the player switched off waits for them
                if (site.Repair && !WorkOn(site.Uid, WorkJob.Repair)) continue;
                // builders start once the materials are there; they take them from the farmhouse as they begin
                if (!site.Supplied && !Supply(site)) continue;
                var crew = Math.Min(free, GameplayRules.BuildersPerSite);
                if (crew <= 0) break;
                _crews[site.Uid] = crew;
                free -= crew;
                site.Done = Math.Min(site.Required, site.Done + crew * dt * (Now < RushUntil ? 2 : 1));   // Worker Rush: twice as fast
            }
            for (var i = 0; i < _sites.Count; i++)
            {
                var site = _sites[i];
                if (site.Done < site.Required) continue;
                _sites.RemoveAt(i--);
                _crews.Remove(site.Uid);
                if (site.Repair)
                {
                    if (_health.TryGetValue(site.Uid, out var mended)) { mended.Hp = mended.Max; mended.Ruined = false; }
                    _finished.Add(site.Uid);
                    continue;
                }
                _finished.Add(site.Uid);
                AddXp(GameplayRules.XpForBuilding(_prices.TryGetValue(site.Uid, out var built) ? built : 0));
                // its clock starts now: animals settle in, people move in
                if (_animals.TryGetValue(site.Uid, out var house)) house.Restore(0, now);
                if (_departments.TryGetValue(site.Uid, out var dept)) dept.LastUpkeep = now;
            }
            RefreshBasket();
        }

        /// <summary>
        /// The farmer walks up to a building site (or a damaged building) with materials in the basket:
        /// hand one over. Returns the good given, or null when the site needs nothing the basket holds.
        /// Once everything is there the builders can start.
        /// </summary>
        public string? GiveToSite(int uid)
        {
            var site = _sites.FirstOrDefault(c => c.Uid == uid && !c.Supplied);
            if (site == null) return null;
            foreach (var (item, _) in site.Materials)
            {
                if (site.StillNeeds(item) <= 0 || !Basket.TakeItem(item)) continue;
                site.Given[item] = (site.Given.TryGetValue(item, out var g) ? g : 0) + 1;
                if (site.Materials.All(m => site.StillNeeds(m.item) <= 0)) { site.Supplied = true; site.Missing.Clear(); }
                else if (site.Missing.TryGetValue(item, out var short_)) { if (short_ <= 1) site.Missing.Remove(item); else site.Missing[item] = short_ - 1; }
                RefreshBasket();
                return item;
            }
            return null;
        }

        /// <summary>Could the farmer hand something from the basket to this building's site right now?</summary>
        public bool SiteWants(int uid)
        {
            var site = _sites.FirstOrDefault(c => c.Uid == uid && !c.Supplied);
            return site != null && site.Materials.Any(m => site.StillNeeds(m.item) > 0 && Basket.CountOf(m.item) > 0);
        }

        /// <summary>Take a site's materials from the farmhouses if they have them all (true); otherwise note what's missing.</summary>
        private bool Supply(Construction site)
        {
            // a ruined farmhouse's goods are still on its shelves: builders may take them (else a ruined
            // farmhouse could never be mended, nor anything else while it lies in ruins)
            var stores = _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && IsBuilt(d.Uid)).ToList();
            site.Missing.Clear();
            var needs = site.Materials.Select(m => m.item).Distinct().Select(item => (item, count: site.StillNeeds(item))).ToList();
            foreach (var (item, count) in needs)
            {
                var have = stores.Sum(d => d.Stock.TryGetValue(item, out var n) ? n : 0);
                if (have < count) site.Missing[item] = count - have;
            }
            if (site.Missing.Count > 0) return false;
            foreach (var (item, count) in needs)
            {
                var left = count;
                foreach (var d in stores) { if (left <= 0) break; left -= d.TakeStock(item, left); }
            }
            site.Supplied = true;
            return true;
        }

        private void RefreshBasket()
        {
            var best = 1;
            foreach (var d in _departments.Values) if (d.BuildingId == "farmhouse") best = Math.Max(best, d.Level);
            Basket.Capacity = GameplayRules.BasketBase + (best - 1) * GameplayRules.BasketPerFarmhouseLevel + GameplayRules.BasketBonus(BestLevel) + BasketExtra;
        }

        // ---- save ---------------------------------------------------------------------------------

        public EconomySave ToSave()
        {
            var s = new EconomySave { coins = Coins, now = Now };
            foreach (var st in Basket.Stacks) s.basket.Add(new EconomySave.Count { item = st.Item, n = st.Count });
            foreach (var d in _departments.Values)
            {
                var ds = new EconomySave.DepartmentSave { uid = d.Uid, level = d.Level, energy = d.Energy, starvingSince = d.StarvingSince, lost = d.Lost, iron = d.Iron, order = _orders.TryGetValue(d.Uid, out var o) ? o : 0 };
                foreach (var kv in d.Stock.OrderBy(k => k.Key, StringComparer.Ordinal)) ds.stock.Add(new EconomySave.Count { item = kv.Key, n = kv.Value });
                s.departments.Add(ds);
            }
            foreach (var p in _plots.Values)
                s.plots.Add(new EconomySave.PlotSave { uid = p.Uid, crop = p.Crop ?? "", last = p.LastCrop ?? "", plantedAt = p.PlantedAt, readyAt = p.ReadyAt, left = p.Left, rotFrom = p.RotFrom });
            foreach (var a in _animals.Values)
                s.animals.Add(new EconomySave.AnimalSave { uid = a.Uid, stock = a.Stock, since = a.Since, feed = a.Feed, sideStock = a.Side?.Stock ?? 0, sideSince = a.Side?.Since ?? 0 });
            foreach (var c in _sites)
            {
                var ss = new EconomySave.SiteSave { uid = c.Uid, required = c.Required, done = c.Done, supplied = c.Supplied, repair = c.Repair };
                foreach (var kv in c.Given.OrderBy(k => k.Key, StringComparer.Ordinal)) ss.given.Add(new EconomySave.Count { item = kv.Key, n = kv.Value });
                s.sites.Add(ss);
            }
            foreach (var kv in _health)
                if (kv.Value.Damaged || kv.Value.Ruined) s.health.Add(new EconomySave.HealthSave { uid = kv.Key, hp = kv.Value.Hp, ruined = kv.Value.Ruined });
            s.lastInterest = _lastInterest;
            s.bestLevel = BestLevel;
            s.xp = Xp;
            s.fertUntil = FertUntil;
            s.extraBuilders = ExtraBuilders;
            s.basketExtra = BasketExtra;
            SaveCrew(s);
            if (Order != null) { s.orderItem = Order.Item; s.orderCount = Order.Count; s.orderGiven = Order.Given; s.orderReward = Order.Reward; s.orderExpires = Order.Expires; }
            foreach (var o in _delivery) s.orders.Add(new EconomySave.OrderSave { item = o.Item, count = o.Count, given = o.Given, reward = o.Reward, expires = o.Expires });
            s.nextOrderAt = _nextOrderAt;
            foreach (var m in _machines.Values)
            {
                var ms = new EconomySave.MachineSave { uid = m.Uid, output = m.Output, busyUntil = m.BusyUntil };
                foreach (var kv in m.Hopper.OrderBy(k => k.Key, StringComparer.Ordinal)) ms.hopper.Add(new EconomySave.Count { item = kv.Key, n = kv.Value });
                s.machines.Add(ms);
            }
            SaveProgress(s);
            SaveTrain(s);
            SaveWork(s);
            SaveRooms(s);
            return s;
        }

        /// <summary>Restore state saved by <see cref="ToSave"/> onto buildings already added with <see cref="AddBuilding"/>.</summary>
        public void Restore(EconomySave s)
        {
            Coins = s.coins;
            Now = s.now;
            Basket.Clear();
            foreach (var d in s.departments)
            {
                if (!_departments.TryGetValue(d.uid, out var dept)) continue;
                dept.Level = Math.Max(1, Math.Min(d.level, dept.Levels.MaxLevel));
                dept.Energy = d.energy;
                dept.StarvingSince = d.starvingSince;
                dept.Lost = Math.Max(0, Math.Min(d.lost, dept.People));
                dept.Iron = d.iron;
                dept.LastUpkeep = s.now;
                if (d.order > 0) _orders[d.uid] = d.order;
                foreach (var c in d.stock) dept.Store(c.item, c.n);
            }
            foreach (var p in s.plots)
            {
                if (!_plots.TryGetValue(p.uid, out var plot)) continue;
                if (!string.IsNullOrEmpty(p.last)) plot.LastCrop = p.last;
                if (string.IsNullOrEmpty(p.crop)) continue;
                plot.Crop = p.crop;
                plot.PlantedAt = p.plantedAt;
                plot.ReadyAt = p.readyAt;
                plot.Left = p.left;
                plot.RotFrom = p.rotFrom;
            }
            foreach (var a in s.animals)
                if (_animals.TryGetValue(a.uid, out var house))
                {
                    house.Restore(a.stock, a.since);
                    house.Feed = house.Producer.Eats ? a.feed : 0;
                    if (house.Side != null && a.sideSince > 0) house.Side.Restore(a.sideStock, a.sideSince);
                }
            foreach (var ms in s.machines)
            {
                if (!_machines.TryGetValue(ms.uid, out var m)) continue;
                m.Restore(ms.output, ms.busyUntil);
                foreach (var c in ms.hopper) m.Fill(c.item, c.n);
            }
            // the save knows which buildings were still going up; anything not listed there is finished
            var saved = new Dictionary<int, EconomySave.SiteSave>();
            foreach (var c in s.sites) saved[c.uid] = c;
            _sites.RemoveAll(c => !saved.ContainsKey(c.Uid));
            foreach (var c in _sites)
            {
                c.Done = Math.Min(c.Required, saved[c.Uid].done);
                c.Supplied |= saved[c.Uid].supplied || saved[c.Uid].done > 0;
                foreach (var g in saved[c.Uid].given) c.Given[g.item] = g.n;
            }
            _givenToRepairs.Clear();
            foreach (var r in s.sites)
                if (r.repair && r.given.Count > 0) _givenToRepairs[r.uid] = r.given.ToDictionary(g => g.item, g => g.n, StringComparer.Ordinal);
            foreach (var hs in s.health)
                if (_health.TryGetValue(hs.uid, out var h)) { h.Hp = Math.Max(0, Math.Min(h.Max, hs.hp)); h.Ruined = hs.ruined; }
            _lastInterest = s.lastInterest;
            BestLevel = Math.Max(1, s.bestLevel);
            // XP came after levels: an older save keeps the level it had reached
            Xp = Math.Max(s.xp, GameplayRules.XpAt(BestLevel));
            FertUntil = s.fertUntil;
            ExtraBuilders = Math.Max(0, Math.Min(3, s.extraBuilders));
            BasketExtra = Math.Max(0, s.basketExtra);
            LoadCrew(s);
            _delivery.Clear();
            if (s.orders.Count > 0)
                foreach (var o in s.orders) _delivery.Add(new DeliveryOrder(o.item, o.count, o.reward, o.expires) { Given = o.given });
            else if (!string.IsNullOrEmpty(s.orderItem))
                _delivery.Add(new DeliveryOrder(s.orderItem, s.orderCount, s.orderReward, s.orderExpires) { Given = s.orderGiven });
            _nextOrderAt = s.nextOrderAt;
            RefreshBasket();
            foreach (var c in s.basket) Basket.Add(c.item, c.n);
            RestoreProgress(s, s.now);
            RestoreTrain(s);
            RestoreWork(s);
            RestoreRooms(s);
        }
    }

    /// <summary>A post office delivery order: bring <see cref="Count"/> of <see cref="Item"/> before <see cref="Expires"/> for <see cref="Reward"/> coins.</summary>
    public sealed class DeliveryOrder
    {
        public DeliveryOrder(string item, int count, int reward, double expires)
        {
            Item = item;
            Count = count;
            Reward = reward;
            Expires = expires;
        }

        public string Item { get; }
        public int Count { get; }
        public int Reward { get; }
        public double Expires { get; }
        public int Given { get; internal set; }
        public bool Done { get; internal set; }
        public bool Expired { get; internal set; }
        public int Left => Math.Max(0, Count - Given);
    }

    public enum GatherResult
    {
        Nothing,
        Took,
        Done,
        NotReady,
        Busy,
        BasketFull,
        NoCoins,
    }

    public enum UnloadOutcome
    {
        None,
        Stored,
        Energy,
        Sold,
        StorageFull,
    }

    public readonly struct UnloadResult
    {
        public static readonly UnloadResult None = new UnloadResult(null, UnloadOutcome.None, 0);

        public UnloadResult(string? item, UnloadOutcome outcome, int amount)
        {
            Item = item;
            Outcome = outcome;
            Amount = amount;
        }

        public string? Item { get; }
        public UnloadOutcome Outcome { get; }
        /// <summary>Units stored, energy gained or coins earned.</summary>
        public int Amount { get; }
        public bool Moved => Outcome is UnloadOutcome.Stored or UnloadOutcome.Energy or UnloadOutcome.Sold;
    }

    public enum PlotState
    {
        Empty,
        Growing,
        Ripe,
    }

    /// <summary>A building going up: builders put work into it until it's done.</summary>
    public sealed class Construction
    {
        public Construction(int uid, string buildingId, double required, IReadOnlyList<(string item, int count)>? materials = null, bool repair = false)
        {
            Uid = uid;
            BuildingId = buildingId;
            Required = required;
            Materials = materials ?? Array.Empty<(string, int)>();
            Supplied = Materials.Count == 0;
            Repair = repair;
        }

        public int Uid { get; }
        public string BuildingId { get; }
        /// <summary>Builder-seconds the building needs.</summary>
        public double Required { get; }
        public double Done { get; internal set; }
        public double Progress => Required <= 0 ? 1 : Math.Min(1, Done / Required);
        /// <summary>What it needs from the farmhouse before builders can start (wood, planks, stone).</summary>
        public IReadOnlyList<(string item, int count)> Materials { get; }
        /// <summary>The materials have been taken from the farmhouse.</summary>
        public bool Supplied { get; internal set; }
        /// <summary>What the farmhouse is still short of (empty when supplied or not yet checked).</summary>
        public Dictionary<string, int> Missing { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Materials the farmer carried to the site in the basket; the farmhouse only gives the rest.</summary>
        public Dictionary<string, int> Given { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>How many of a material the site still needs from someone (the basket or the farmhouse).</summary>
        public int StillNeeds(string item)
        {
            var need = 0;
            foreach (var (it, count) in Materials) if (it == item) need += count;
            return Math.Max(0, need - (Given.TryGetValue(item, out var g) ? g : 0));
        }
        /// <summary>Mending a damaged or ruined building rather than putting up a new one.</summary>
        public bool Repair { get; }
    }

    /// <summary>A building's health: raiders knock it down; at nothing it's a ruin until builders repair it.</summary>
    public sealed class Health
    {
        public Health(double max)
        {
            Max = max;
            Hp = max;
        }

        public double Max { get; }
        public double Hp { get; internal set; }
        public bool Ruined { get; internal set; }
        /// <summary>When it was last hit (repairs wait until it's been quiet for a bit).</summary>
        public double LastHit { get; internal set; }
        public bool Damaged => Hp < Max;
    }

    /// <summary>How town buildings change a department's upkeep: a clinic brings people back faster, a chapel keeps them through hunger longer.</summary>
    public readonly struct UpkeepMods
    {
        public UpkeepMods(double recruitSpeed, double droughtSlow, double appetite = 1)
        {
            RecruitSpeed = recruitSpeed;
            DroughtSlow = droughtSlow;
            Appetite = appetite;
        }

        public double RecruitSpeed { get; }
        public double DroughtSlow { get; }
        /// <summary>How much more everyone eats (winter cold: 1.2).</summary>
        public double Appetite { get; }
    }

    /// <summary>A field the farmer plants and harvests.</summary>
    public sealed class Plot
    {
        public Plot(int uid) => Uid = uid;

        public int Uid { get; }
        public string? Crop { get; internal set; }
        /// <summary>What was planted here last (workers replant it).</summary>
        public string? LastCrop { get; internal set; }
        public double PlantedAt { get; internal set; }
        public double ReadyAt { get; internal set; }
        /// <summary>Units still to harvest once ripe.</summary>
        public int Left { get; internal set; }
        /// <summary>Rot counted up to here (0: none yet).</summary>
        public double RotFrom { get; internal set; }

        /// <summary>Ripe and starting to go over (harvest it soon).</summary>
        public bool Rotting(double now) => Crop != null && now >= ReadyAt + GameplayRules.RipeKeepSeconds(Crop);

        public PlotState State(double now) => Crop == null ? PlotState.Empty : now >= ReadyAt ? PlotState.Ripe : PlotState.Growing;

        /// <summary>0..1 from planting to ripe.</summary>
        public double Progress(double now) => Crop == null ? 0 : ReadyAt <= PlantedAt ? 1 : Math.Max(0, Math.Min(1, (now - PlantedAt) / (ReadyAt - PlantedAt)));

        internal void Reset()
        {
            Crop = null;
            PlantedAt = ReadyAt = 0;
            Left = 0;
            RotFrom = 0;
        }
    }

    /// <summary>A coop, shed or sty: makes goods over time up to a limit.</summary>
    public sealed class AnimalHouse
    {
        public AnimalHouse(int uid, string buildingId, GameplayRules.Producer producer, double now)
        {
            Uid = uid;
            BuildingId = buildingId;
            Producer = producer;
            Since = now;
            Feed = producer.Eats ? GameplayRules.TroughStart : 0;
        }

        public int Uid { get; }
        public string BuildingId { get; }
        public GameplayRules.Producer Producer { get; }
        /// <summary>Stock counted up to <see cref="Since"/>.</summary>
        public int Stock { get; private set; }
        public double Since { get; private set; }
        /// <summary>Feed units in the trough (animals only); each thing made eats one.</summary>
        public int Feed { get; internal set; }
        /// <summary>Level 1–3: bigger and quicker (see <see cref="GameplayRules.BuildingLevelSpeed"/>).</summary>
        public int Level { get; private set; } = 1;
        /// <summary>Extra speed from outside (flowers round bee hives).</summary>
        public double Boost { get; private set; } = 1;
        public double Interval => Producer.IntervalSeconds / (GameplayRules.BuildingLevelSpeed(Level) * Boost);

        /// <summary>A second good on its own clock (the mine's ore), or null.</summary>
        public SideStock? Side { get; internal set; }

        internal void SetBoost(double now, double boost)
        {
            if (Math.Abs(boost - Boost) < 1e-6) return;
            Settle(now);
            Boost = Math.Max(0.1, boost);
        }
        public int Max => Producer.Max + GameplayRules.BuildingLevelRoom * (Level - 1);
        /// <summary>When it last filled up (0: not full). Fresh goods left too long go off.</summary>
        public double FullSince { get; internal set; }

        internal void SetLevel(double now, int level)
        {
            Settle(now);
            Level = Math.Max(1, Math.Min(GameplayRules.BuildingMaxLevel, level));
        }

        /// <summary>Perishable goods (eggs, milk, fish, meat) left in a full house go off one at a time. Returns how many spoiled.</summary>
        internal int Spoil(double now)
        {
            if (!GameplayRules.Perishable(Producer.Item)) return 0;
            Settle(now);
            if (Stock < Max) { FullSince = 0; return 0; }
            if (FullSince <= 0) { FullSince = now; return 0; }
            if (now - FullSince < GameplayRules.SpoilAfterSeconds) return 0;
            Stock--;
            Since = now;
            FullSince = 0;
            return 1;
        }

        /// <summary>Things made since <see cref="Since"/>: by time, and (for animals) no more than there was feed for.</summary>
        private int Made(double now)
        {
            var made = (int)Math.Floor(Math.Max(0, now - Since) / Interval);
            return Producer.Eats ? Math.Min(made, Feed) : made;
        }

        public int StockAt(double now) => Math.Min(Max, Stock + Made(now));

        /// <summary>Feed units still in the trough (0 for producers that don't eat).</summary>
        public int FeedLeft(double now) => Producer.Eats ? Math.Max(0, Feed - Made(now)) : 0;

        /// <summary>The animals have run out of feed (nothing more will come until the trough is filled).</summary>
        public bool Hungry(double now) => Producer.Eats && Feed - Made(now) <= 0 && StockAt(now) < Max;

        /// <summary>Seconds until the next one is ready (0 when full or hungry).</summary>
        public double NextIn(double now) => StockAt(now) >= Max || Hungry(now) ? 0 : Interval - (Math.Max(0, now - Since) % Interval);

        /// <summary>Count what's been made into stock (and eaten from the trough), from <see cref="Since"/> to now.</summary>
        private void Settle(double now)
        {
            var made = Made(now);
            var room = Max - Stock;
            var kept = Math.Min(made, room);
            if (Producer.Eats) Feed -= kept;
            Stock += kept;
            // a full or hungry house stops counting, so its clock restarts; otherwise the part-made one keeps ticking
            if (made >= room || (Producer.Eats && Feed <= 0)) Since = now;
            else Since += made * Interval;
        }

        internal void Take(double now)
        {
            Settle(now);
            Stock = Math.Max(0, Stock - 1);
        }

        internal void AddFeed(double now, int units)
        {
            if (units <= 0) return;
            Settle(now);
            Feed = Math.Min(GameplayRules.TroughSize, Feed + units);
        }

        internal void Restore(int stock, double since)
        {
            Stock = Math.Max(0, Math.Min(stock, Max));
            Since = since;
        }
    }

    /// <summary>
    /// A machine: goods go in its hopper, and while it holds a full set and its output tray has room it
    /// works one batch at a time, each taking the recipe's seconds. Time catches up after a break.
    /// </summary>
    /// <summary>A second good a producer makes on its own clock, with no feed (the mine's ore).</summary>
    public sealed class SideStock
    {
        public SideStock(GameplayRules.Producer producer, double now)
        {
            Producer = producer;
            Since = now;
        }

        public GameplayRules.Producer Producer { get; }
        public int Stock { get; private set; }
        public double Since { get; private set; }

        public int StockAt(double now) => Math.Min(Producer.Max, Stock + (int)Math.Floor(Math.Max(0, now - Since) / Producer.IntervalSeconds));

        internal void Take(double now)
        {
            var made = (int)Math.Floor(Math.Max(0, now - Since) / Producer.IntervalSeconds);
            var room = Producer.Max - Stock;
            Stock += Math.Min(made, room);
            if (made >= room) Since = now; else Since += made * (double)Producer.IntervalSeconds;
            Stock = Math.Max(0, Stock - 1);
        }

        internal void Restore(int stock, double since)
        {
            Stock = Math.Max(0, Math.Min(stock, Producer.Max));
            Since = since;
        }
    }

    public sealed class Machine
    {
        private readonly Dictionary<string, int> _hopper = new Dictionary<string, int>(StringComparer.Ordinal);

        public Machine(int uid, string buildingId, GameplayRules.Recipe recipe)
        {
            Uid = uid;
            BuildingId = buildingId;
            Recipe = recipe;
        }

        public int Uid { get; }
        public string BuildingId { get; }
        public GameplayRules.Recipe Recipe { get; }
        public IReadOnlyDictionary<string, int> Hopper => _hopper;
        /// <summary>Finished goods waiting.</summary>
        public int Output { get; internal set; }
        /// <summary>When the batch in hand is done (seconds), or 0 when idle.</summary>
        public double BusyUntil { get; private set; }
        public bool Busy => BusyUntil > 0;
        /// <summary>Work speed (a library makes machines quicker; so does a higher level).</summary>
        public double Speed { get; internal set; } = 1;
        /// <summary>Level 1–3: quicker, and room for more finished goods.</summary>
        public int Level { get; internal set; } = 1;
        public int OutputMax => GameplayRules.MachineOutputMax + GameplayRules.BuildingLevelRoom * 2 * (Level - 1);
        private double Seconds => Recipe.Seconds / Math.Max(0.1, Speed);

        /// <summary>Ruined: the hopper and tray are lost and the batch in hand with them.</summary>
        internal void Wreck()
        {
            _hopper.Clear();
            Output = 0;
            BusyUntil = 0;
        }

        public int HopperOf(string item) => _hopper.TryGetValue(item, out var n) ? n : 0;

        /// <summary>0..1 through the batch in hand.</summary>
        public double Progress(double now) => BusyUntil <= 0 ? 0 : Math.Max(0, Math.Min(1, 1 - (BusyUntil - now) / Seconds));

        /// <summary>What's missing for the next batch (null when it has a full set).</summary>
        public string? Missing()
        {
            foreach (var (item, n) in Recipe.Inputs) if (HopperOf(item) < n) return item;
            return null;
        }

        internal void Fill(string item, int n)
        {
            if (n > 0) _hopper[item] = HopperOf(item) + n;
        }

        internal void Restore(int output, double busyUntil)
        {
            Output = Math.Max(0, output);
            BusyUntil = busyUntil;
        }

        /// <summary>Finish batches that are due and start the next ones, back to back, up to <paramref name="now"/>.</summary>
        internal void Advance(double now)
        {
            var clock = now;
            for (var guard = 0; guard < 10000; guard++)
            {
                if (BusyUntil > 0)
                {
                    if (now < BusyUntil) return;
                    Output++;
                    clock = BusyUntil;   // the next batch starts the moment this one ends
                    BusyUntil = 0;
                }
                if (Output >= OutputMax || Missing() != null) return;
                foreach (var (item, n) in Recipe.Inputs)
                {
                    _hopper[item] = HopperOf(item) - n;
                    if (_hopper[item] <= 0) _hopper.Remove(item);
                }
                BusyUntil = clock + Seconds;
                clock = now;
            }
        }
    }

    /// <summary>A building with a pad: farmhouse, army house, workers' house, builders' hut, market.</summary>
    public sealed class Department
    {
        private readonly Dictionary<string, int> _stock = new Dictionary<string, int>(StringComparer.Ordinal);

        public Department(int uid, string buildingId, GameplayRules.DepartmentLevels levels, double now)
        {
            Uid = uid;
            BuildingId = buildingId;
            Levels = levels;
            Level = 1;
            Energy = levels.UpkeepPerPersonPerMinute > 0 ? GameplayRules.StartingEnergy : 0;
            LastUpkeep = now;
        }

        public int Uid { get; }
        public string BuildingId { get; }
        public GameplayRules.DepartmentLevels Levels { get; }
        public int Level { get; internal set; }
        public double Energy { get; internal set; }
        /// <summary>When the tank ran dry (seconds), or 0 while fed.</summary>
        public double StarvingSince { get; internal set; }
        internal double LastUpkeep { get; set; }

        /// <summary>People the department houses at its level.</summary>
        public int People => Levels.People[Math.Min(Level, Levels.People.Length) - 1] + Extra;
        /// <summary>People added by town buildings (community hall, town hall).</summary>
        public int Extra { get; internal set; }
        /// <summary>Iron delivered to an army house: enough for every warrior arms them (hit harder).</summary>
        public int Iron { get; internal set; }
        public bool Armed => Levels.Kind == DepartmentKind.ArmyHouse && Present > 0 && Iron >= Present;
        /// <summary>People lost (hunger, fights) and not yet replaced.</summary>
        public int Lost { get; internal set; }
        /// <summary>People actually there.</summary>
        public int Present => Math.Max(0, People - Lost);
        public int StorageCapacity => Levels.Storage[Math.Min(Level, Levels.Storage.Length) - 1];
        /// <summary>Food a minute: per person, and a bigger house costs more to run (+20% a level).</summary>
        public double UpkeepPerMinute => Present * Levels.UpkeepPerPersonPerMinute * (1 + GameplayRules.LevelUpkeep * (Level - 1));
        public bool Starving => StarvingSince > 0;
        private int _droughtLosses;
        private double _recruit;

        public IReadOnlyDictionary<string, int> Stock => _stock;
        public int Stored => _stock.Values.Sum();
        public int StockEnergy => _stock.Sum(kv => kv.Value * GameplayRules.EnergyOf(kv.Key));

        internal void Store(string item, int n)
        {
            if (n <= 0) return;
            _stock[item] = (_stock.TryGetValue(item, out var have) ? have : 0) + n;
        }

        /// <summary>Take stock out (selling, sending as supplies); returns how many were there.</summary>
        public int TakeStock(string item, int n)
        {
            if (!_stock.TryGetValue(item, out var have) || n <= 0) return 0;
            var take = Math.Min(have, n);
            if (take == have) _stock.Remove(item); else _stock[item] = have - take;
            return take;
        }

        /// <summary>Spend stored goods worth at least <paramref name="energy"/>, cheapest goods first.</summary>
        internal void SpendStockAsEnergy(int energy)
        {
            var left = energy;
            foreach (var item in _stock.Keys.OrderBy(GameplayRules.EnergyOf).ThenBy(k => k, StringComparer.Ordinal).ToList())
            {
                var each = GameplayRules.EnergyOf(item);
                while (left > 0 && _stock.TryGetValue(item, out var have) && have > 0)
                {
                    TakeStock(item, 1);
                    left -= each;
                }
                if (left <= 0) break;
            }
        }

        internal void Upkeep(double now, bool away = false, UpkeepMods mods = default)
        {
            var dt = now - LastUpkeep;
            LastUpkeep = now;
            if (dt <= 0) return;
            var rate = UpkeepPerMinute / 60.0 * Math.Max(1, mods.Appetite);
            if (rate > 0)
            {
                Energy -= rate * dt;
                if (Energy <= 0)
                {
                    // when exactly the tank ran dry
                    if (StarvingSince <= 0) StarvingSince = now + Energy / rate;
                    Energy = 0;
                }
            }
            if (Energy > 0 && StarvingSince > 0)
            {
                StarvingSince = 0;   // fed again
                _droughtLosses = 0;
            }
            if (Starving)
            {
                _recruit = 0;
                if (away) { StarvingSince = now; _droughtLosses = 0; return; }   // hunger while away isn't counted against them
                var due = (int)Math.Floor((now - StarvingSince) / (GameplayRules.DroughtLossSeconds * Math.Max(1, mods.DroughtSlow)));
                while (_droughtLosses < due && Present > 0)
                {
                    _droughtLosses++;
                    Lost++;
                }
                return;
            }
            // fed, with people missing: new ones move in now and then, for a share of the energy
            if (Lost <= 0 || Levels.UpkeepPerPersonPerMinute <= 0) { _recruit = 0; return; }
            _recruit += dt;
            var every = GameplayRules.RecruitSeconds / Math.Max(1, mods.RecruitSpeed);
            while (_recruit >= every && Lost > 0 && Energy >= GameplayRules.RecruitEnergy)
            {
                _recruit -= every;
                Lost--;
                Energy -= GameplayRules.RecruitEnergy;
            }
            if (Lost <= 0) _recruit = 0;
        }
    }

    /// <summary>Everything the island's economy needs to come back after a restart (plain fields for JSON).</summary>
    [Serializable]
    public sealed class EconomySave
    {
        public int coins;
        public double now;
        public List<Count> basket = new List<Count>();
        public List<DepartmentSave> departments = new List<DepartmentSave>();
        public List<PlotSave> plots = new List<PlotSave>();
        public List<AnimalSave> animals = new List<AnimalSave>();
        public List<SiteSave> sites = new List<SiteSave>();
        public List<MachineSave> machines = new List<MachineSave>();
        public List<HealthSave> health = new List<HealthSave>();
        public double lastInterest;
        public int bestLevel = 1;
        public string orderItem = "";
        public int orderCount;
        public int orderGiven;
        public int orderReward;
        public double orderExpires;
        public double nextOrderAt;
        /// <summary>Every open order (from economy plan v4; the order* fields above keep the first, for older readers).</summary>
        public List<OrderSave> orders = new List<OrderSave>();
        [Serializable] public sealed class OrderSave { public string item = ""; public int count; public int given; public int reward; public double expires; }
        /// <summary>The freight train (from level 5): when the next comes, and the one at the station.</summary>
        public double nextTrainAt;
        public double trainLeavesAt;
        public List<CrateSave> trainCrates = new List<CrateSave>();
        [Serializable] public sealed class CrateSave { public string item = ""; public int need; public int coins; public bool loaded; public List<Count> pays = new List<Count>(); }
        public List<Count> stats = new List<Count>();
        public List<Amount> sold = new List<Amount>();
        public List<Count> buildingLevels = new List<Count>();
        public int goalIndex;
        /// <summary>The current goal's id (from economy plan v4: goals can move in the list).</summary>
        public string goalId = "";
        /// <summary>Which version of the goals and dailies this save was written with (0 = before v4).</summary>
        public int progressVersion;
        /// <summary>The island level today's dailies were sized for.</summary>
        public int dailyLevel = 1;
        public long day;
        public List<int> dailies = new List<int>();
        public List<int> dailyClaimed = new List<int>();
        public List<Count> dayBase = new List<Count>();
        public long weatherSlot;
        public int bestScore;
        public long xp;
        /// <summary>Fertilizer runs until (epoch seconds).</summary>
        public double fertUntil;
        /// <summary>Builders from Builder's Passes.</summary>
        public int extraBuilders;
        /// <summary>Basket slots from Bigger Basket boosts.</summary>
        public int basketExtra;
        /// <summary>Farmhands the player holds (written by the server from the chain).</summary>
        public List<Farmhand> crew = new List<Farmhand>();
        /// <summary>Wild plots cleared with land deeds.</summary>
        public int wildPlotsOpen;

        [Serializable] public sealed class Count { public string item = ""; public int n; }
        [Serializable] public sealed class Amount { public string item = ""; public double v; }
        [Serializable] public sealed class DepartmentSave { public int uid; public int level = 1; public double energy; public double starvingSince; public int lost; public int iron; public double order; public List<Count> stock = new List<Count>(); }
        [Serializable] public sealed class PlotSave { public int uid; public string crop = ""; public string last = ""; public double plantedAt; public double readyAt; public int left; public double rotFrom; }
        [Serializable] public sealed class AnimalSave { public int uid; public int stock; public double since; public int feed = GameplayRules.TroughStart; public int sideStock; public double sideSince; }
        [Serializable] public sealed class SiteSave { public int uid; public double required; public double done; public bool supplied; public bool repair; public List<Count> given = new List<Count>(); }
        [Serializable] public sealed class HealthSave { public int uid; public double hp; public bool ruined; }
        [Serializable] public sealed class MachineSave { public int uid; public int output; public double busyUntil; public List<Count> hopper = new List<Count>(); }

        // workers run the farm (IslandEconomy.Work)
        public double awaySince;
        public bool nightShift;
        public bool workPass;
        /// <summary>Furniture bought for the farmhouse's rooms (IslandEconomy.Rooms).</summary>
        public List<string> roomItems = new List<string>();
        /// <summary>Costumes bought with gems (IslandEconomy.Rooms).</summary>
        public List<string> looks = new List<string>();
        public bool workTrain;
        public List<WorkSave> work = new List<WorkSave>();
        [Serializable] public sealed class WorkSave { public int uid; public List<string> on = new List<string>(); public string crop = ""; }
    }
}
