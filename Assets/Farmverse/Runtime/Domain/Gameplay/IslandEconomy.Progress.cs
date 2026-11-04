#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>
    /// Economy v3: what the island has done (stats), the goals that lead the player on (story goals
    /// and three daily ones), building levels for machines and animal houses, market saturation,
    /// spoilage, the weather's storms, and the island's flow of food (made against eaten).
    /// </summary>
    public sealed partial class IslandEconomy
    {
        private readonly Dictionary<string, int> _stats = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _sold = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<int, int> _buildingLevels = new Dictionary<int, int>();
        private readonly Dictionary<string, int> _dayBase = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<int> _dailies = new List<int>();
        private readonly HashSet<int> _dailyClaimed = new HashSet<int>();
        private long _day = -1;
        private long _weatherSlot = -1;
        private int _spoiled;

        private static readonly GameplayRules.Season NoSeason = new GameplayRules.Season("", 1, 0, 1, "");

        /// <summary>Seasons and weather (and storms) on; off gives plain, even conditions (tests, tutorials).</summary>
        public bool SeasonsOn { get; set; } = true;
        /// <summary>Prices fall when a lot of one good is sold; off keeps them fixed.</summary>
        public bool MarketMoves { get; set; } = true;

        public GameplayRules.Season SeasonNow(double now) => SeasonsOn ? GameplayRules.SeasonAt(now) : NoSeason;
        /// <summary>The live event running now (none while seasons are off, as in tests and tutorials).</summary>
        public GameplayRules.LiveEvent? EventNow(double now) => SeasonsOn ? GameplayRules.EventAt(now) : null;

        // ---- outside influences ---------------------------------------------------------------------

        /// <summary>
        /// Flowers near a bee hive (wild ones the island's nature knows about, plus flower beds the
        /// economy knows): the presentation counts the wild ones and passes them in.
        /// </summary>
        public void SetWildFlowers(int uid, int patches, double now)
        {
            if (!_animals.TryGetValue(uid, out var hives) || hives.BuildingId != "bee_hives") return;
            var beds = _ids.Count(kv => kv.Value == "decor_flower_bed" && Working(kv.Key) && Within(uid, kv.Key, GameplayRules.BloomReach));
            hives.SetBoost(now, GameplayRules.Bloom(patches + beds));
        }

        /// <summary>The highest world score this island has reached.</summary>
        public int BestScore { get; private set; }

        /// <summary>Score where the current island level starts and where the next one begins.</summary>
        public (long from, long to) LevelSpan(int level) => (GameplayRules.XpAt(level), GameplayRules.XpAt(level) + GameplayRules.XpToNext(level));

        /// <summary>
        /// What the island is worth, for the leaderboard: coins, every working building at its price
        /// (a ruin counts half), goods in store and on your back at market price, and building levels.
        /// </summary>
        public long NetWorth
        {
            get
            {
                long w = Coins;
                foreach (var kv in _prices) if (IsBuilt(kv.Key)) w += IsRuined(kv.Key) ? kv.Value / 2 : kv.Value;
                foreach (var d in _departments.Values)
                {
                    w += (d.Level - 1) * 150;
                    foreach (var kv in d.Stock) w += (long)kv.Value * GameplayRules.PriceOf(kv.Key);
                }
                foreach (var kv in _buildingLevels) w += (kv.Value - 1) * 150;
                foreach (var st in Basket.Stacks) w += (long)st.Count * GameplayRules.PriceOf(st.Item);
                return w;
            }
        }

        // ---- XP and levels -----------------------------------------------------------------------

        /// <summary>Island XP earned so far: it only grows, and sets the island's level.</summary>
        public long Xp { get; private set; }

        /// <summary>Coins the last level-ups paid (taken by the UI for the level-up screen).</summary>
        public int LevelCoins { get; private set; }

        /// <summary>Put the island at a level without its rewards (tests and the developer tools).</summary>
        public void SetLevel(int level)
        {
            BestLevel = Math.Max(1, level);
            Xp = Math.Max(Xp, GameplayRules.XpAt(BestLevel));
            RefreshBasket();
        }

        public void AddXp(long xp)
        {
            if (xp <= 0) return;
            var live = EventNow(Now);
            if (live != null && live.XpBonus > 1) xp = (long)Math.Round(xp * live.XpBonus);
            Xp += xp;
            CheckLevel();
        }

        /// <summary>A new level from the XP: pay its reward, open what it opens.</summary>
        private void CheckLevel()
        {
            var level = GameplayRules.LevelForXp(Xp);
            if (level <= BestLevel) return;
            for (var l = BestLevel + 1; l <= level; l++)
            {
                var reward = GameplayRules.LevelReward(l);
                Coins += reward;
                LevelCoins += reward;
            }
            BestLevel = level;
            _leveledUp = true;
            RefreshBasket();
        }

        /// <summary>Coins paid by level-ups since the last call.</summary>
        public int TakeLevelCoins()
        {
            var c = LevelCoins;
            LevelCoins = 0;
            return c;
        }

        // ---- stats -----------------------------------------------------------------------------

        /// <summary>Lifetime count of something the island did ("harvested", "sold", "made:egg", "raids_won"…).</summary>
        public int Stat(string name) => _stats.TryGetValue(name, out var n) ? n : 0;

        public void Count(string name, int n = 1)
        {
            if (n <= 0) return;
            _stats[name] = Stat(name) + n;
            AddXp((long)GameplayRules.XpOf(name) * n);
        }

        // ---- story goals -------------------------------------------------------------------------

        /// <summary>How far along the story goals the player is (goals before it are done and paid).</summary>
        public int GoalIndex { get; private set; }

        /// <summary>The goal now: a written one, then generated ones (a level every 5) without end.</summary>
        public GameplayRules.Goal? CurrentGoal => GameplayRules.GoalAt(GoalIndex);

        public int GoalHave(GameplayRules.Goal g) =>
            g.Stat.StartsWith("has:", StringComparison.Ordinal) ? (Has(g.Stat.Substring(4)) ? 1 : 0)
            : g.Stat == "level" ? BestLevel
            : Stat(g.Stat);

        public bool GoalDone(GameplayRules.Goal g) => GoalHave(g) >= g.Need;

        /// <summary>Collect the current goal's reward and move on. False if it isn't done.</summary>
        public bool ClaimGoal()
        {
            var g = CurrentGoal;
            if (g == null || !GoalDone(g)) return false;
            Coins += g.Reward;
            GoalIndex++;
            AddXp(GameplayRules.XpForGoal(g.Reward));
            return true;
        }

        // ---- daily goals ---------------------------------------------------------------------------

        /// <summary>Today's three goals (indexes into <see cref="GameplayRules.DailyPool"/>).</summary>
        public IReadOnlyList<int> Dailies => _dailies;

        public int DailyHave(int pool) => Math.Max(0, Stat(GameplayRules.DailyPool[pool].Stat) - (_dayBase.TryGetValue(GameplayRules.DailyPool[pool].Stat, out var b) ? b : 0));

        /// <summary>The island level today's dailies were sized for (they don't grow mid-day).</summary>
        private int _dailyLevel = 1;

        /// <summary>How many a daily asks for today.</summary>
        public int DailyNeed(int pool) => GameplayRules.DailyAmount(GameplayRules.DailyPool[pool], _dailyLevel);

        /// <summary>A daily's text with today's amount.</summary>
        public string DailyText(int pool) => GameplayRules.DailyText(GameplayRules.DailyPool[pool], _dailyLevel);

        /// <summary>A daily's coins today.</summary>
        public int DailyCoins(int pool) => GameplayRules.DailyReward(GameplayRules.DailyPool[pool], _dailyLevel);

        /// <summary>True when the island can do this daily now: its level and the building it needs.</summary>
        public bool DailyPossible(GameplayRules.DailyKind d) => BestLevel >= d.Level && (d.Needs == null || Has(d.Needs));
        public bool DailyClaimed(int pool) => _dailyClaimed.Contains(pool);

        /// <summary>Stat counted for the order chest: post office orders delivered since today (UTC) began.</summary>
        public const string OrdersStat = "orders";

        /// <summary>
        /// Stats also counted per UTC day (their value at the day's start is kept in the save), for the order
        /// chest and the coupon tasks; the server reads the same numbers from its copy of the island.
        /// </summary>
        public static readonly IReadOnlyList<string> DayStats = new[] { OrdersStat, "harvested", "chopped", "planted", "sold" };

        /// <summary>How much of a stat was counted since today (UTC) began (0 for stats not counted per day).</summary>
        public int Today(string stat) => Math.Max(0, Stat(stat) - (_dayBase.TryGetValue(stat, out var b) ? b : Stat(stat)));

        /// <summary>Post office orders delivered today (the order chest).</summary>
        public int OrdersToday => Today(OrdersStat);

        /// <summary>True when every one of today's daily goals has been collected (the task chest).</summary>
        public bool AllDailiesClaimed => _dailies.Count > 0 && _dailies.All(_dailyClaimed.Contains);

        public bool ClaimDaily(int pool)
        {
            if (!_dailies.Contains(pool) || _dailyClaimed.Contains(pool)) return false;
            if (DailyHave(pool) < DailyNeed(pool)) return false;
            _dailyClaimed.Add(pool);
            var reward = DailyCoins(pool);
            Coins += reward;
            AddXp(GameplayRules.XpForGoal(reward) / 2);
            return true;
        }

        /// <summary>A new day (UTC) brings three new goals, counted from now.</summary>
        private void RollDay(double now)
        {
            if (now <= 0) return;
            var day = (long)Math.Floor(now / 86400.0);
            if (day == _day) return;
            _day = day;
            _dailies.Clear();
            _dailyClaimed.Clear();
            _dayBase.Clear();
            var rng = new Random((int)(day % int.MaxValue));
            _dailyLevel = BestLevel;
            // only what this island can do today: its level, and the building each daily needs
            var picks = Enumerable.Range(0, GameplayRules.DailyPool.Count).OrderBy(_ => rng.Next())
                .Where(i => DailyPossible(GameplayRules.DailyPool[i])).Take(3);
            _dailies.AddRange(picks);
            foreach (var i in _dailies) _dayBase[GameplayRules.DailyPool[i].Stat] = Stat(GameplayRules.DailyPool[i].Stat);
            foreach (var stat in DayStats)
                if (!_dayBase.ContainsKey(stat)) _dayBase[stat] = Stat(stat);
        }

        // ---- market --------------------------------------------------------------------------------

        /// <summary>What a shop pays for one of <paramref name="item"/> right now (falls when a lot was just sold).</summary>
        public int MarketPrice(string shopId, string item)
        {
            var basePrice = GameplayRules.SellPrice(shopId, item);
            if (basePrice <= 0) return 0;
            var live = EventNow(Now);
            if (live != null && live.SellBonus > 0 && live.Goods.Contains(item)) basePrice = (int)Math.Round(basePrice * (1 + live.SellBonus));
            if (!MarketMoves) return basePrice;
            return Math.Max(1, (int)Math.Round(basePrice * GameplayRules.MarketFactor(_sold.TryGetValue(item, out var n) ? n : 0)));
        }

        /// <summary>0..1: how much of the normal price a good fetches now.</summary>
        public double MarketFactor(string item) => GameplayRules.MarketFactor(_sold.TryGetValue(item, out var n) ? n : 0);

        private void Sold(string item)
        {
            _sold[item] = (_sold.TryGetValue(item, out var n) ? n : 0) + 1;
            Count("sold");
        }

        private void DecayMarket(double dt)
        {
            if (dt <= 0 || _sold.Count == 0) return;
            var k = Math.Pow(0.5, dt / GameplayRules.MarketHalfLifeSeconds);
            foreach (var key in _sold.Keys.ToList())
            {
                var v = _sold[key] * k;
                if (v < 0.05) _sold.Remove(key); else _sold[key] = v;
            }
        }

        // ---- spoilage --------------------------------------------------------------------------------

        /// <summary>Perishables that went off since last asked.</summary>
        public int TakeSpoiled()
        {
            var n = _spoiled;
            _spoiled = 0;
            return n;
        }

        // ---- weather -------------------------------------------------------------------------------

        /// <summary>The building a storm just hit (for the screen to show once), or -1.</summary>
        public int StormHit { get; private set; } = -1;

        public int TakeStormHit()
        {
            var u = StormHit;
            StormHit = -1;
            return u;
        }

        private void Weather(double now)
        {
            var slot = (long)Math.Floor(now / GameplayRules.WeatherSeconds);
            if (slot == _weatherSlot) return;
            var first = _weatherSlot < 0;
            _weatherSlot = slot;
            if (first || !SeasonsOn || GameplayRules.WeatherAt(now) != GameplayRules.WeatherKind.Storm) return;
            // a storm knocks one standing building about (never ruins it)
            var standing = _health.Where(kv => Working(kv.Key) && !_plots.ContainsKey(kv.Key) && kv.Value.Hp > 1).Select(kv => kv.Key).OrderBy(u => u).ToList();
            if (standing.Count == 0) return;
            var uid = standing[Rng.Next(standing.Count)];
            var h = _health[uid];
            var hit = Math.Min(h.Hp - 1, h.Max * GameplayRules.StormDamage);
            if (hit <= 0) return;
            h.Hp -= hit;
            h.LastHit = now;
            StormHit = uid;
        }

        // ---- building levels ---------------------------------------------------------------------------

        public int BuildingLevel(int uid) => _buildingLevels.TryGetValue(uid, out var l) ? l : 1;

        public bool CanLevel(int uid) => (_machines.ContainsKey(uid) || _animals.ContainsKey(uid)) && BuildingLevel(uid) < GameplayRules.BuildingMaxLevelAt(BestLevel);

        /// <summary>What the next level of a machine or animal house costs (null at the top or for other buildings).</summary>
        public (int coins, IReadOnlyList<(string item, int count)> materials)? BuildingUpgradeCost(int uid)
        {
            if (!CanLevel(uid) || !_ids.TryGetValue(uid, out var id)) return null;
            return GameplayRules.BuildingUpgrade(id, _prices.TryGetValue(uid, out var p) ? p : 0, BuildingLevel(uid));
        }

        /// <summary>Level up a machine or animal house: coins plus materials from the farmhouse.</summary>
        public bool UpgradeBuilding(int uid, double now)
        {
            if (!Working(uid) || BuildingUpgradeCost(uid) is not { } cost) return false;
            if (Coins < cost.coins || MissingMaterials(cost.materials).Count > 0) return false;
            Coins -= cost.coins;
            TakeMaterials(cost.materials);
            var level = BuildingLevel(uid) + 1;
            _buildingLevels[uid] = level;
            ApplyLevel(uid, now);
            Count("building_upgrades");
            return true;
        }

        private void ApplyLevel(int uid, double now)
        {
            var level = BuildingLevel(uid);
            if (_machines.TryGetValue(uid, out var m)) m.Level = level;
            if (_animals.TryGetValue(uid, out var a)) a.SetLevel(now, level);
        }

        // ---- materials from the farmhouse ---------------------------------------------------------------

        /// <summary>What the working farmhouses together are short of, item by item.</summary>
        public IReadOnlyList<(string item, int count)> MissingMaterials(IEnumerable<(string item, int count)> needs)
        {
            var missing = new List<(string, int)>();
            foreach (var (item, count) in needs)
            {
                var have = _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && Working(d.Uid)).Sum(d => d.Stock.TryGetValue(item, out var n) ? n : 0);
                if (have < count) missing.Add((item, count - have));
            }
            return missing;
        }

        private void TakeMaterials(IEnumerable<(string item, int count)> needs)
        {
            foreach (var (item, count) in needs)
            {
                var left = count;
                foreach (var d in _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && Working(d.Uid)))
                {
                    if (left <= 0) break;
                    var have = d.Stock.TryGetValue(item, out var n) ? n : 0;
                    var take = Math.Min(have, left);
                    if (take > 0) { d.TakeStock(item, take); left -= take; }
                }
            }
        }

        // ---- the island's food flow ------------------------------------------------------------------------

        /// <summary>Energy a minute the island makes (fields growing, animals with feed, machines running) and eats (everyone's upkeep).</summary>
        public (double made, double eaten) Flow(double now)
        {
            double made = 0;
            foreach (var p in _plots.Values)
            {
                if (!Working(p.Uid) || p.Crop == null) continue;
                var crop = GameplayRules.CropOf(p.Crop);
                if (crop == null) continue;
                made += crop.Yield * GameplayRules.EnergyOf(crop.Id) * 60.0 / Math.Max(1, crop.GrowSeconds);
            }
            foreach (var a in _animals.Values)
            {
                if (!Working(a.Uid) || a.Hungry(now) || a.StockAt(now) >= a.Max) continue;
                made += GameplayRules.EnergyOf(a.Producer.Item) * 60.0 / Math.Max(1, a.Interval);
            }
            foreach (var m in _machines.Values)
            {
                if (!Working(m.Uid) || !m.Busy) continue;
                var gain = GameplayRules.EnergyOf(m.Recipe.Output) - m.Recipe.Inputs.Sum(i => GameplayRules.EnergyOf(i.item) * i.count);
                made += Math.Max(0, gain) * 60.0 / Math.Max(1, m.Recipe.Seconds / Math.Max(0.1, m.Speed));
            }
            var appetite = SeasonNow(now).Appetite;
            var eaten = _departments.Values.Where(d => IsBuilt(d.Uid)).Sum(d => d.UpkeepPerMinute) * appetite;
            return (made, eaten);
        }

        // ---- save ------------------------------------------------------------------------------------------

        private void SaveProgress(EconomySave s)
        {
            foreach (var kv in _stats.OrderBy(k => k.Key, StringComparer.Ordinal)) s.stats.Add(new EconomySave.Count { item = kv.Key, n = kv.Value });
            foreach (var kv in _sold) s.sold.Add(new EconomySave.Amount { item = kv.Key, v = kv.Value });
            foreach (var kv in _buildingLevels) s.buildingLevels.Add(new EconomySave.Count { item = kv.Key.ToString(), n = kv.Value });
            s.goalIndex = GoalIndex;
            s.goalId = CurrentGoal?.Id ?? "";
            s.progressVersion = ProgressVersion;
            s.dailyLevel = _dailyLevel;
            s.day = _day;
            s.dailies.AddRange(_dailies);
            s.dailyClaimed.AddRange(_dailyClaimed);
            foreach (var kv in _dayBase) s.dayBase.Add(new EconomySave.Count { item = kv.Key, n = kv.Value });
            s.weatherSlot = _weatherSlot;
            s.bestScore = BestScore;
        }

        /// <summary>Saves before the level plan (v4) keep only their goal's index in the old list.</summary>
        public const int ProgressVersion = 4;

        /// <summary>The new place of an old save's goal: its id in the new list, or the next old goal that still exists.</summary>
        public static int MigrateGoal(int oldIndex)
        {
            var ids = GameplayRules.OldGoalIds;
            if (oldIndex >= ids.Count) return GameplayRules.Goals.Count;
            for (var i = Math.Max(0, oldIndex); i < ids.Count; i++)
                for (var j = 0; j < GameplayRules.Goals.Count; j++)
                    if (GameplayRules.Goals[j].Id == ids[i]) return j;
            return GameplayRules.Goals.Count;
        }

        private void RestoreProgress(EconomySave s, double now)
        {
            _stats.Clear();
            foreach (var c in s.stats) _stats[c.item] = c.n;
            _sold.Clear();
            foreach (var a in s.sold) _sold[a.item] = a.v;
            _buildingLevels.Clear();
            foreach (var c in s.buildingLevels)
                if (int.TryParse(c.item, out var uid)) { _buildingLevels[uid] = Math.Max(1, Math.Min(GameplayRules.BuildingMaxLevel, c.n)); ApplyLevel(uid, now); }
            GoalIndex = Math.Max(0, s.goalIndex);
            _dailyLevel = Math.Max(1, s.dailyLevel);
            _day = s.day == 0 && s.dailies.Count == 0 ? -1 : s.day;
            if (s.progressVersion < ProgressVersion)
            {
                // a save from before the level plan: find its goal in the new list, and roll today's dailies again
                GoalIndex = MigrateGoal(s.goalIndex);
                s.dailies.Clear();
                s.dailyClaimed.Clear();
                _day = -1;
            }
            _dailies.Clear(); _dailies.AddRange(s.dailies);
            _dailyClaimed.Clear(); foreach (var i in s.dailyClaimed) _dailyClaimed.Add(i);
            _dayBase.Clear(); foreach (var c in s.dayBase) _dayBase[c.item] = c.n;
            // saves from before a stat was counted per day: today's count starts now
            if (_day >= 0)
                foreach (var stat in DayStats)
                    if (!_dayBase.ContainsKey(stat)) _dayBase[stat] = Stat(stat);
            _weatherSlot = s.weatherSlot == 0 ? -1 : s.weatherSlot;
            BestScore = Math.Max(BestScore, s.bestScore);
        }
    }
}
