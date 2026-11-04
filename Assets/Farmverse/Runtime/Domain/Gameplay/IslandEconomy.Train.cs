#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>One crate on the freight train: a good, how many, and whether it's loaded.</summary>
    public sealed class TrainCrate
    {
        public TrainCrate(string item, int need, IReadOnlyList<(string item, int count)> pays, int coins)
        {
            Item = item; Need = need; Pays = pays; Coins = coins;
        }

        public string Item { get; }
        public int Need { get; }
        /// <summary>Building materials the crate brings back (into the farmhouse).</summary>
        public IReadOnlyList<(string item, int count)> Pays { get; }
        public int Coins { get; }
        public bool Loaded { get; internal set; }
    }

    /// <summary>The freight train while it waits at the station.</summary>
    public sealed class TrainVisit
    {
        public TrainVisit(IReadOnlyList<TrainCrate> crates, double leavesAt)
        {
            Crates = crates;
            LeavesAt = leavesAt;
        }

        public IReadOnlyList<TrainCrate> Crates { get; }
        public double LeavesAt { get; }
        public bool AllLoaded => Crates.All(c => c.Loaded);
    }

    /// <summary>What a departing train paid.</summary>
    public sealed class TrainPayout
    {
        public int Crates;
        public int Coins;
        public readonly Dictionary<string, int> Materials = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>
    /// The freight train (economy plan: Township's train). From level 5 it stops at the station with three
    /// crates, each asking for goods the island makes; loading a crate takes them from the stores, and the
    /// train pays for loaded crates with building materials (wood and stone early, planks, bricks and iron
    /// as they open) and some coins when it leaves. A full train pays one more crate of materials. Then the
    /// next one comes a while later. It is how an island gets the materials its upgrades need without
    /// making every one itself.
    /// </summary>
    public sealed partial class IslandEconomy
    {
        private TrainVisit? _train;
        private double _nextTrainAt;

        /// <summary>The train at the station, or null while it's away (or before level 5).</summary>
        public TrainVisit? Train => _train;

        /// <summary>When the next train arrives (0 = not scheduled yet).</summary>
        public double NextTrainAt => _nextTrainAt;

        /// <summary>Set when a train left (for the screen to say what it paid, once).</summary>
        public TrainPayout? LastTrain { get; private set; }

        public TrainPayout? TakeTrainPayout()
        {
            var t = LastTrain;
            LastTrain = null;
            return t;
        }

        private void TrainTick(double now)
        {
            if (BestLevel < GameplayRules.TrainLevel) { _train = null; return; }
            if (_train != null)
            {
                if (now >= _train.LeavesAt) Depart(now);
                return;
            }
            if (_nextTrainAt <= 0) _nextTrainAt = now + 30;
            if (now < _nextTrainAt) return;
            _train = NewTrain(now);
        }

        private TrainVisit? NewTrain(double now)
        {
            var goods = Makeable().ToList();
            if (goods.Count == 0) return null;
            var crates = new List<TrainCrate>();
            var picks = goods.OrderBy(_ => Rng.Next()).ToList();
            for (var i = 0; i < GameplayRules.TrainCrates; i++)
            {
                var item = picks[i % picks.Count];
                var need = GameplayRules.TrainCrateCount(item, BestLevel);
                crates.Add(new TrainCrate(item, need, GameplayRules.TrainMaterials(BestLevel, Rng), (int)Math.Round(GameplayRules.PriceOf(item) * need * 0.5)));
            }
            return new TrainVisit(crates, now + GameplayRules.TrainStaySeconds);
        }

        /// <summary>Goods of a kind in all the island's stores (farmhouses, warehouses, silos).</summary>
        public int InStore(string item) =>
            _departments.Values.Where(d => d.Levels.Kind is DepartmentKind.Farmhouse or DepartmentKind.Silo && Working(d.Uid))
                .Sum(d => d.Stock.TryGetValue(item, out var n) ? n : 0);

        /// <summary>Fill a crate from the stores. False if the stores don't hold enough.</summary>
        public bool LoadCrate(int index)
        {
            if (_train == null || index < 0 || index >= _train.Crates.Count) return false;
            var crate = _train.Crates[index];
            if (crate.Loaded || InStore(crate.Item) < crate.Need) return false;
            var left = crate.Need;
            foreach (var d in _departments.Values.Where(d => d.Levels.Kind is DepartmentKind.Farmhouse or DepartmentKind.Silo && Working(d.Uid)).OrderBy(d => d.Uid))
            {
                if (left <= 0) break;
                left -= d.TakeStock(crate.Item, left);
            }
            crate.Loaded = true;
            return true;
        }

        /// <summary>Send the train off now with what's loaded (it pays on leaving).</summary>
        public bool SendTrain(double now)
        {
            if (_train == null || !_train.Crates.Any(c => c.Loaded)) return false;
            Depart(now);
            return true;
        }

        private void Depart(double now)
        {
            var train = _train!;
            _train = null;
            _nextTrainAt = now + GameplayRules.TrainGapSeconds;
            var pay = new TrainPayout();
            var loaded = train.Crates.Where(c => c.Loaded).ToList();
            if (loaded.Count == 0) return;
            var farmhouse = _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && Working(d.Uid)).OrderBy(d => d.Uid).FirstOrDefault();
            void Give(string item, int n)
            {
                if (n <= 0) return;
                pay.Materials[item] = (pay.Materials.TryGetValue(item, out var have) ? have : 0) + n;
                // materials go into the farmhouse; past its room they still count (the store takes what fits)
                if (farmhouse != null) farmhouse.Store(item, n);
            }
            foreach (var c in loaded)
            {
                foreach (var (item, n) in c.Pays) Give(item, n);
                pay.Coins += c.Coins;
            }
            // a full train: one more crate's worth of materials
            if (train.AllLoaded)
                foreach (var (item, n) in GameplayRules.TrainMaterials(BestLevel, Rng)) Give(item, n);
            pay.Crates = loaded.Count;
            Coins += pay.Coins;
            Count("trains");
            AddXp(GameplayRules.TrainXp * loaded.Count);
            LastTrain = pay;
        }

        private void SaveTrain(EconomySave s)
        {
            s.nextTrainAt = _nextTrainAt;
            if (_train == null) return;
            s.trainLeavesAt = _train.LeavesAt;
            foreach (var c in _train.Crates)
                s.trainCrates.Add(new EconomySave.CrateSave
                {
                    item = c.Item, need = c.Need, coins = c.Coins, loaded = c.Loaded,
                    pays = c.Pays.Select(p => new EconomySave.Count { item = p.item, n = p.count }).ToList(),
                });
        }

        private void RestoreTrain(EconomySave s)
        {
            _nextTrainAt = s.nextTrainAt;
            _train = null;
            if (s.trainCrates.Count == 0) return;
            var crates = s.trainCrates.Select(c => new TrainCrate(c.item, c.need, c.pays.Select(p => (p.item, p.n)).ToList(), c.coins) { Loaded = c.loaded }).ToList();
            _train = new TrainVisit(crates, s.trainLeavesAt);
        }
    }
}
