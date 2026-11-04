#nullable enable
using System;
using System.Collections.Generic;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>
    /// The numbers of the walking economy (docs: farmverse-gameplay-design §3–§5): what each good is
    /// worth as energy and in coins, how crops grow, what animal houses make, and what each
    /// department holds and costs per level. Plain data so it can be tuned in one place and later
    /// moved to the shared catalogue when the rules go to the server.
    /// </summary>
    public static partial class GameplayRules
    {
        // ---- goods ------------------------------------------------------------------------------

        private static readonly Dictionary<string, int> EnergyValues = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["wheat"] = 5, ["maize"] = 6, ["pumpkin"] = 8, ["tomato"] = 7, ["cabbage"] = 6, ["beans"] = 6, ["sunflower"] = 5,
            ["egg"] = 10, ["milk"] = 15, ["meat"] = 30, ["flour"] = 20, ["croissant"] = 45, ["oil"] = 25,
            ["mushroom"] = 6, ["wood"] = 2,
            ["yogurt"] = 40, ["juice"] = 18, ["stone"] = 3, ["planks"] = 9,
            ["fish"] = 14, ["veg_box"] = 20,
            ["feed"] = 12, ["flowers"] = 4,
        };

        /// <summary>Raw crops and fruit (what grain silos hold and grocers pay extra for).</summary>
        public static bool IsCrop(string item) => CropOf(item) != null || item is "mushroom" or "flowers";

        /// <summary>
        /// Coins a shop pays for a good, or 0 if that shop doesn't buy it. The market stall buys anything
        /// at the base price; the specialist shops pay more for what they deal in and nothing else.
        /// </summary>
        public static int SellPrice(string shopId, string item) => shopId switch
        {
            "grocer_shop" => IsCrop(item) || item is "juice" or "veg_box" ? (int)Math.Round(PriceOf(item) * 1.4) : 0,
            "bakery_shop" => item is "croissant" or "flour" or "yogurt" or "egg" or "milk" ? (int)Math.Round(PriceOf(item) * 1.5) : 0,
            "fish_market" => item is "fish" or "meat" ? (int)Math.Round(PriceOf(item) * 1.5) : 0,
            _ => PriceOf(item),
        };

        /// <summary>Energy a good gives when fed to a department; unknown goods give 1.</summary>
        public static int EnergyOf(string item) => EnergyValues.TryGetValue(item, out var e) ? e : 1;

        /// <summary>
        /// Coins a good sells for at the market (economy plan §7). Crops: about 25 coins per field-minute at
        /// level 1, 5% more per unlock level, a little less a minute for slow crops. Made goods: their inputs
        /// × 1.3 plus a coin per 10 s of machine time. Everything else: its energy value. Separate from
        /// energy, so prices can be tuned without changing how far food goes.
        /// </summary>
        public static int PriceOf(string item)
        {
            if (Prices.TryGetValue(item, out var p)) return p;
            return Math.Max(1, EnergyOf(item));
        }

        private static readonly Dictionary<string, int> CropPrices = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["wheat"] = 5, ["beans"] = 6, ["maize"] = 7, ["pumpkin"] = 11, ["cabbage"] = 10, ["tomato"] = 16, ["sunflower"] = 18,
        };

        // built on first use: the recipe table lives further down (static fields start in text order)
        private static Dictionary<string, int>? _prices;
        private static Dictionary<string, int> Prices => _prices ??= BuildPrices();

        private static Dictionary<string, int> BuildPrices()
        {
            var prices = new Dictionary<string, int>(CropPrices, StringComparer.Ordinal);
            // made goods: inputs × 1.3 + 1 per 10 s of work; resolved in rounds so chains (flour → bread) settle
            for (var round = 0; round < 4; round++)
                foreach (var kv in Recipes)
                {
                    var r = kv.Value;
                    double inputs = 0;
                    foreach (var (i, n) in r.Inputs) inputs += n * (prices.TryGetValue(i, out var ip) ? ip : Math.Max(1, EnergyOf(i)));
                    prices[r.Output] = (int)Math.Round(inputs * 1.3 + r.Seconds / 10.0);
                }
            return prices;
        }

        // ---- crops ------------------------------------------------------------------------------

        public sealed class Crop
        {
            public Crop(string id, int growSeconds, int yield, int seedCost)
            {
                Id = id;
                GrowSeconds = growSeconds;
                Yield = yield;
                SeedCost = seedCost;
            }

            /// <summary>The crop is also the good it yields (wheat gives wheat).</summary>
            public string Id { get; }
            public int GrowSeconds { get; }
            public int Yield { get; }
            /// <summary>Coins to plant one field.</summary>
            public int SeedCost { get; }
        }

        public static readonly IReadOnlyList<Crop> Crops = new[]
        {
            new Crop("wheat", 60, 6, 4),
            new Crop("beans", 80, 6, 5),
            new Crop("maize", 90, 6, 6),
            new Crop("cabbage", 100, 6, 9),
            new Crop("pumpkin", 120, 6, 10),
            new Crop("sunflower", 140, 5, 14),
            new Crop("tomato", 150, 6, 14),
        };

        public static Crop? CropOf(string id)
        {
            foreach (var c in Crops) if (c.Id == id) return c;
            return null;
        }

        /// <summary>What farm workers sow in a field nobody has planted yet.</summary>
        public const string DefaultCrop = "wheat";

        /// <summary>The building a field is placed as.</summary>
        public const string FieldBuilding = "field";

        // ---- animal houses ----------------------------------------------------------------------

        public sealed class Producer
        {
            public Producer(string item, int intervalSeconds, int max, bool eats = false)
            {
                Item = item;
                IntervalSeconds = intervalSeconds;
                Max = max;
                Eats = eats;
            }

            public string Item { get; }
            public int IntervalSeconds { get; }
            /// <summary>Stock stops growing at this many until collected.</summary>
            public int Max { get; }
            /// <summary>Animals: each thing they make eats one unit of feed from the trough; an empty trough, nothing made.</summary>
            public bool Eats { get; }
        }

        private static readonly Dictionary<string, Producer> Producers = new Dictionary<string, Producer>(StringComparer.Ordinal)
        {
            ["chicken_coop"] = new Producer("egg", 40, 6, eats: true),
            ["barn"] = new Producer("milk", 60, 6, eats: true),
            ["pigsty"] = new Producer("meat", 120, 3, eats: true),
            ["greenhouse"] = new Producer("tomato", 50, 8),
            ["fishing_shed"] = new Producer("fish", 70, 5),
            ["mine_entrance"] = new Producer("stone", 45, 8),
        };

        /// <summary>Feed units a good puts in an animal trough (0 = animals don't eat it).</summary>
        public static int FeedValue(string item) => item switch
        {
            "feed" => 3,
            "wheat" or "maize" or "beans" => 1,
            _ => 0,
        };

        /// <summary>A trough holds this much feed; a new animal house comes with some.</summary>
        public const int TroughSize = 30;
        public const int TroughStart = 20;

        public static Producer? ProducerOf(string buildingId) => Producers.TryGetValue(buildingId, out var p) ? p : null;

        /// <summary>A second thing some producers make on their own clock (none now: the quarry gives stone only).</summary>
        public static Producer? SecondProducerOf(string buildingId) => null;

        // ---- machines ---------------------------------------------------------------------------

        /// <summary>What a machine turns into what: processed goods are worth more energy than what went in.</summary>
        public sealed class Recipe
        {
            public Recipe(string output, int seconds, params (string item, int count)[] inputs)
            {
                Output = output;
                Seconds = seconds;
                Inputs = inputs;
            }

            public string Output { get; }
            public int Seconds { get; }
            public IReadOnlyList<(string item, int count)> Inputs { get; }

            public int Needs(string item)
            {
                foreach (var (i, n) in Inputs) if (i == item) return n;
                return 0;
            }
        }

        private static readonly Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>(StringComparer.Ordinal)
        {
            ["windmill"] = new Recipe("flour", 25, ("wheat", 2)),
            ["feed_mill"] = new Recipe("feed", 20, ("maize", 2)),
            ["bakery_factory"] = new Recipe("croissant", 30, ("flour", 1), ("egg", 1)),
            ["dairy_factory"] = new Recipe("yogurt", 40, ("milk", 2)),
            ["sugar_mill"] = new Recipe("oil", 30, ("sunflower", 3)),
            ["cider_press"] = new Recipe("juice", 25, ("tomato", 2)),
            ["lumber_yard"] = new Recipe("planks", 20, ("wood", 3)),
            ["packing_shed"] = new Recipe("veg_box", 25, ("cabbage", 2), ("pumpkin", 1)),
        };

        public static Recipe? RecipeOf(string buildingId) => Recipes.TryGetValue(buildingId, out var r) ? r : null;

        /// <summary>A machine holds this many of each input, and stops when this many outputs wait to be collected.</summary>
        public const int HopperSize = 20;
        public const int MachineOutputMax = 10;

        // ---- departments ------------------------------------------------------------------------

        public sealed class DepartmentLevels
        {
            public DepartmentLevels(DepartmentKind kind, int[] people, int[] storage, int[] upgradeCost, double upkeepPerPersonPerMinute)
            {
                Kind = kind;
                People = people;
                Storage = storage;
                UpgradeCost = upgradeCost;
                UpkeepPerPersonPerMinute = upkeepPerPersonPerMinute;
            }

            public DepartmentKind Kind { get; }
            /// <summary>People housed per level (index 0 = level 1).</summary>
            public int[] People { get; }
            /// <summary>Items stored per level (farmhouses only).</summary>
            public int[] Storage { get; }
            /// <summary>Energy to go from level n to n+1 (index 0 = level 1 → 2).</summary>
            public int[] UpgradeCost { get; }
            public double UpkeepPerPersonPerMinute { get; }
            public int MaxLevel => People.Length;
        }

        private static readonly Dictionary<string, DepartmentLevels> Departments = new Dictionary<string, DepartmentLevels>(StringComparer.Ordinal)
        {
            ["farmhouse"] = new DepartmentLevels(DepartmentKind.Farmhouse, new[] { 0, 0, 0 }, new[] { 150, 300, 600 }, new[] { 800, 2500 }, 0),
            ["army_house"] = new DepartmentLevels(DepartmentKind.ArmyHouse, new[] { 3, 5, 8 }, new[] { 0, 0, 0 }, new[] { 1000, 3000 }, 2),
            ["workers_house"] = new DepartmentLevels(DepartmentKind.WorkersHouse, new[] { 2, 4, 6 }, new[] { 0, 0, 0 }, new[] { 600, 2000 }, 1),
            ["builders_hut"] = new DepartmentLevels(DepartmentKind.BuildersHut, new[] { 2, 3, 5 }, new[] { 0, 0, 0 }, new[] { 500, 1800 }, 1),
            ["market_stall"] = new DepartmentLevels(DepartmentKind.Market, new[] { 0 }, new[] { 0 }, Array.Empty<int>(), 0),
            ["grocer_shop"] = new DepartmentLevels(DepartmentKind.Market, new[] { 0 }, new[] { 0 }, Array.Empty<int>(), 0),
            ["bakery_shop"] = new DepartmentLevels(DepartmentKind.Market, new[] { 0 }, new[] { 0 }, Array.Empty<int>(), 0),
            ["fish_market"] = new DepartmentLevels(DepartmentKind.Market, new[] { 0 }, new[] { 0 }, Array.Empty<int>(), 0),
            // a warehouse is a second store for any goods (raiders rob it like the farmhouse)
            ["facility_warehouse"] = new DepartmentLevels(DepartmentKind.Farmhouse, new[] { 0, 0 }, new[] { 250, 500 }, new[] { 1500 }, 0),
            ["grain_silos"] = new DepartmentLevels(DepartmentKind.Silo, new[] { 0, 0 }, new[] { 300, 600 }, new[] { 900 }, 0),
            ["stable"] = new DepartmentLevels(DepartmentKind.Stable, new[] { 1 }, new[] { 0 }, Array.Empty<int>(), 0.6),
            ["police_station"] = new DepartmentLevels(DepartmentKind.Watch, new[] { 2, 3 }, new[] { 0, 0 }, new[] { 1500 }, 1.5),
        };

        public static DepartmentLevels? DepartmentOf(string buildingId) => Departments.TryGetValue(buildingId, out var d) ? d : null;

        /// <summary>Energy a new department starts with, so its people don't go hungry the minute it opens.</summary>
        public const int StartingEnergy = 60;

        // ---- building ---------------------------------------------------------------------------

        /// <summary>Builders that come with a farmhouse (gameplay design §5).</summary>
        public const int FarmhouseBuilders = 1;
        /// <summary>At most this many builders work one site; the rest move on to the next.</summary>
        public const int BuildersPerSite = 3;

        /// <summary>Builder-seconds a building takes: bigger, dearer buildings take longer (a field is quick).</summary>
        public static double BuildWork(string buildingId, int price) =>
            buildingId == FieldBuilding || IsDecor(buildingId) ? 6 : Math.Max(15, Math.Min(240, price / 10.0));

        /// <summary>What a new building needs from the farmhouse besides coins: wood for small ones, planks for bigger, stone for the biggest.</summary>
        public static IReadOnlyList<(string item, int count)> MaterialsFor(string buildingId, int price)
        {
            if (buildingId == FieldBuilding || price <= 0) return Array.Empty<(string, int)>();
            // small sheds are all wood; proper buildings stand on stone footings; big ones want sawn
            // planks too. The lumber yard is how planks start, so it can't need them.
            if (price <= 300) return new[] { ("wood", Math.Max(1, price / 50)) };
            if (price <= 1200 || buildingId == "lumber_yard") return new[] { ("wood", price / 60), ("stone", Math.Max(1, price / 150)) };
            if (price <= 2000) return new[] { ("wood", price / 100), ("planks", Math.Max(1, price / 200)), ("stone", price / 150) };
            return new[] { ("planks", price / 150), ("stone", price / 100) };
        }

        /// <summary>Wood a farmhouse starts with on a new island, so the first buildings can go up.</summary>
        public const int StarterWood = 30;
        /// <summary>...and some stone for the first footings.</summary>
        public const int StarterStone = 10;

        // ---- health and damage ------------------------------------------------------------------

        /// <summary>A building's health: tougher the dearer it was; a field is easy to trample.</summary>
        public static int MaxHp(string buildingId, int price) =>
            buildingId == FieldBuilding ? 40 : Math.Min(600, 120 + price / 8);

        /// <summary>Repairs use one wood per this much health restored.</summary>
        public const int HpPerWood = 25;

        // ---- raids ------------------------------------------------------------------------------

        /// <summary>
        /// Raiders in a raid: 1 + level ÷ 4 (at most 16), up to 2 more for a rich island (threat), and half
        /// as many for an island's first 3 raids so the first one can be won.
        /// </summary>
        public static int RaidSize(int level, double threat, int raidsSeen = int.MaxValue)
        {
            var n = 1 + Math.Max(1, level) / 4 + (int)Math.Min(2, Math.Max(0, threat) / 10);
            if (raidsSeen < 3) n = Math.Min(n, 2);   // the first zombie nights: two shamble in
            return Math.Max(1, Math.Min(16, n));
        }

        // ---- zombie nights: the bosses ----------------------------------------------------------

        /// <summary>
        /// The named zombies (the Zombies Mini characters), the level each starts raiding at, and what it does:
        /// the Footballer is fast and steals crops; the Builder breaks things; the Clerk goes for coins; the
        /// Policeman rouses extra zombies; the Mime comes without the warning horn; the Tourist scares the
        /// visitors off (and steals). The Mayor leads the last stand from level 99.
        /// </summary>
        public static readonly (string id, int level)[] Bosses =
        {
            ("footballer", 25), ("builder", 34), ("clerk", 50), ("policeman", 60), ("mime", 70), ("tourist", 80), ("mayor", 99),
        };

        /// <summary>From this level a beaten boss is cured instead of lost: it becomes a townsperson.</summary>
        public const int CureLevel = 30;
        /// <summary>From this level three bosses come together on their night.</summary>
        public const int TripleBossLevel = 90;
        /// <summary>A boss night comes every this many raids (once a week with one raid a day).</summary>
        public const int BossEvery = 7;

        public static string BossName(string id) => id switch
        {
            "footballer" => "the Footballer", "builder" => "the Builder", "clerk" => "the Clerk", "policeman" => "the Policeman",
            "mime" => "the Mime", "tourist" => "the Tourist", "mayor" => "the Zombie Mayor", _ => id,
        };

        /// <summary>
        /// Which bosses join tonight's raid: on every <see cref="BossEvery"/>th raid, one of the bosses unlocked by
        /// <paramref name="level"/> and not yet cured, in turn (three from <see cref="TripleBossLevel"/>); the Mayor
        /// every night once unlocked and uncured.
        /// </summary>
        public static IReadOnlyList<string> BossesTonight(int level, int raidsSeen, Func<string, bool> cured)
        {
            var open = new List<string>();
            foreach (var (id, at) in Bosses) if (level >= at && id != "mayor" && !cured(id)) open.Add(id);
            var tonight = new List<string>();
            if (level >= 99 && !cured("mayor")) tonight.Add("mayor");
            if (open.Count > 0 && raidsSeen % BossEvery == BossEvery - 1)
            {
                var week = raidsSeen / BossEvery;
                var count = level >= TripleBossLevel ? Math.Min(3, open.Count) : 1;
                for (var i = 0; i < count; i++) tonight.Add(open[(week + i) % open.Count]);
            }
            return tonight;
        }

        /// <summary>The weapons the army carries at a level (Zombies Mini weapon prefab names), the best tier open.</summary>
        public static IReadOnlyList<string> ArmyWeapons(int level)
        {
            if (level >= 74) return new[] { "Baton", "RoadSign", "Weapon_005" };
            if (level >= 50) return new[] { "Pipe", "PipeWrench" };
            if (level >= 33) return new[] { "Bat", "Crowbar" };
            return Array.Empty<string>();
        }

        /// <summary>Where a weapon sits in the right hand: local position and euler rotation on the hand bone.</summary>
        public static ((float x, float y, float z) pos, (float x, float y, float z) rot) WeaponGrip(string weapon) => weapon switch
        {
            _ => ((0f, 0.05f, 0.02f), (0f, 0f, 0f)),
        };

        /// <summary>A boss's health, hits and speed next to a common zombie's, and its habit.</summary>
        public static (double hp, double damage, double speed, string habit) BossTraits(string id) => id switch
        {
            "footballer" => (1.6, 1.0, 1.5, "thief"),
            "builder" => (2.5, 1.3, 0.9, "wrecker"),
            "clerk" => (1.5, 1.0, 1.1, "coins"),
            "policeman" => (2.0, 1.4, 1.0, "hunter"),
            "mime" => (1.4, 1.0, 1.2, "thief"),
            "tourist" => (1.5, 0.9, 1.3, "thief"),
            "mayor" => (4.0, 2.0, 1.0, "hunter"),
            _ => (1.0, 1.0, 1.0, "thief"),
        };

        /// <summary>Raider health and hits grow 3% a level.</summary>
        public static double RaiderStrength(int level) => 1 + 0.03 * Math.Max(0, level - 1);

        /// <summary>A lost raid takes at most this share of the goods in store.</summary>
        public const double RaidGoodsCap = 0.10;

        /// <summary>
        /// Seconds until the next raid: sooner as the island is worth more, sooner still when its defence
        /// far outstrips the threat (a prize worth raiding), later after a raid that ruined something.
        /// </summary>
        public static double RaidGap(double threat, double defence, bool lastRaidRuined)
        {
            var gap = Math.Max(180, 540 - threat * 20);
            if (defence > threat * 2 + 4) gap *= 0.8;
            if (lastRaidRuined) gap *= 1.5;
            return gap;
        }

        /// <summary>Energy a ride takes from the stable's tank.</summary>
        public const int RideEnergy = 2;

        /// <summary>Seconds of peace on a new island, and how many buildings it takes before raiders take an interest.</summary>
        public const double GraceSeconds = 600;
        public const int RaidMinBuildings = 4;

        // ---- people -----------------------------------------------------------------------------

        /// <summary>A department that has gone hungry this long (seconds) loses one of its people, and another each time again.</summary>
        public const double DroughtLossSeconds = 90;
        /// <summary>A fed department with people missing takes one back every this many seconds...</summary>
        public const double RecruitSeconds = 45;
        /// <summary>...for this much energy each.</summary>
        public const int RecruitEnergy = 40;
        /// <summary>Energy a workers' house spends per load a farm worker carries (work makes them hungry: overworking drains the tank).</summary>
        public const double WorkEnergyPerLoad = 2;
        /// <summary>Energy an army house spends each time one of its warriors fights.</summary>
        public const double FightEnergy = 3;
        /// <summary>A gap between ticks longer than this (seconds) means the game was closed.</summary>
        public const double AwayGapSeconds = 120;
        /// <summary>While the game is closed, each farm worker brings in a load this often (seconds)...</summary>
        public const double AwayTripSeconds = 30;
        /// <summary>...for at most this many hours.</summary>
        public const double AwayWorkCapHours = 8;
        /// <summary>Offline work costs this many times the food of the same work on screen (docs: Workers Run the Farm).</summary>
        public const double OfflineWorkCost = 1.2;
        /// <summary>Offline work runs this long with the Farm Pass or a Night Shift.</summary>
        public const double OfflineCapPassHours = 24;
        /// <summary>Hours offline work runs by the best workers' house level: 8, 12, 16.</summary>
        public static double OfflineCapHours(int workersHouseLevel) => workersHouseLevel >= 3 ? 16 : workersHouseLevel == 2 ? 12 : AwayWorkCapHours;
        /// <summary>Gems a Night Shift costs (or one rewarded ad a day).</summary>
        public const int NightShiftGems = 15;
        /// <summary>Of each good, this many stay in the farmhouse: workers never refill, load or sell them.</summary>
        public const int WorkReserve = 5;
        /// <summary>...and selling keeps this many more on top.</summary>
        public const int SellKeep = 10;
        /// <summary>Workers top up their own house (and the army's) when its energy falls under this, up to <see cref="RestockTo"/>.</summary>
        public const double RestockBelow = 40;
        public const double RestockTo = 100;
        /// <summary>Energy a Lunch Basket gives each workers' house.</summary>
        public const double LunchEnergy = 300;

        /// <summary>Energy one job (one load) takes from a workers' house on screen; offline it's <see cref="OfflineWorkCost"/> times this.</summary>
        public static double WorkEnergy(string job) => job switch
        {
            WorkJob.Replant or WorkJob.Sell => 1,
            WorkJob.Restock => 0,
            _ => WorkEnergyPerLoad,
        };

        /// <summary>Jobs switched on for a new building: harvest, replant, collect, repair and restock; feeding, refilling and selling wait for the player.</summary>
        public static bool WorkDefault(string job) => job is WorkJob.Harvest or WorkJob.Replant or WorkJob.Collect or WorkJob.Repair or WorkJob.Restock;

        /// <summary>Units a farm worker carries at a time (a school adds <see cref="SchoolCarryBonus"/>).</summary>
        public const int WorkerCarry = 4;
        public const int SchoolCarryBonus = 2;
        /// <summary>A farm worker who carries this many loads in a row must rest at home for <see cref="WorkerRestSeconds"/>.</summary>
        public const int WorkerLoadsBeforeRest = 12;
        public const double WorkerRestSeconds = 20;

        // ---- crops left in the field ------------------------------------------------------------

        /// <summary>How long a ripe crop keeps in the field before it starts to rot: four times its growing time, at least 4 minutes.</summary>
        public static double RipeKeepSeconds(string crop) => Math.Max(240, 4.0 * (CropOf(crop)?.GrowSeconds ?? 60));
        /// <summary>After that, one unit rots away this often (seconds) until the field is bare.</summary>
        public const double RotEverySeconds = 60;

        // ---- post office ------------------------------------------------------------------------

        /// <summary>How long (seconds) a delivery order stays open, and the wait before the next one.</summary>
        public const double OrderSeconds = 1200;
        public const double OrderGapSeconds = 90;

        /// <summary>Units an order asks for: about ten minutes of the island's income in that good, 3 to 30.</summary>
        public static int OrderCount(string item, int islandLevel)
        {
            var each = Math.Max(3, PriceOf(item));
            return Math.Max(3, Math.Min(30, (int)Math.Round(CoinIncome(islandLevel) * 10 / 1.6 / each)));
        }

        /// <summary>Coins an order pays: 1.6× what the goods fetch at a market stall, plus a little for the trip.</summary>
        public static int OrderReward(string item, int count) => (int)Math.Round(PriceOf(item) * count * 1.6) + 20;

        // ---- the freight train -------------------------------------------------------------------

        /// <summary>The train starts stopping at the island's station at this level.</summary>
        public const int TrainLevel = 5;
        public const int TrainCrates = 3;
        /// <summary>How long it waits at the station, and how long until the next one after it leaves.</summary>
        public const double TrainStaySeconds = 7200;
        public const double TrainGapSeconds = 1800;
        public const int TrainXp = 20;

        /// <summary>Units a crate asks for: about six minutes of the island's income in that good, 3 to 25.</summary>
        public static int TrainCrateCount(string item, int islandLevel) =>
            Math.Max(3, Math.Min(25, (int)Math.Round(CoinIncome(islandLevel) * 6 / Math.Max(3, PriceOf(item)))));

        /// <summary>
        /// What one crate brings back: two kinds of building material from those open at the level (wood and
        /// stone from the start, planks from 7), more as the island grows.
        /// </summary>
        public static IReadOnlyList<(string item, int count)> TrainMaterials(int islandLevel, Random rng)
        {
            var pool = new List<(string item, int baseCount)> { ("wood", 10), ("stone", 6) };
            if (islandLevel >= 7) pool.Add(("planks", 4));
            if (islandLevel >= 20) pool.Add(("oil", 2));
            var scale = 1 + islandLevel / 20.0;
            var picked = new List<(string, int)>();
            for (var i = 0; i < 2 && pool.Count > 0; i++)
            {
                var at = rng.Next(pool.Count);
                var (item, n) = pool[at];
                pool.RemoveAt(at);
                picked.Add((item, Math.Max(1, (int)Math.Round(n * scale))));
            }
            return picked;
        }

        // ---- bonuses ----------------------------------------------------------------------------

        /// <summary>How far (m) a potting shed or bee hives reach; how far a fire station's crews go.</summary>
        public const double GardenReach = 25;
        public const double FireReach = 60;
        /// <summary>Health a fire station's crews restore per second to damaged buildings in reach.</summary>
        public const double FireRepairPerSecond = 2;

        // ---- coins ------------------------------------------------------------------------------

        /// <summary>A bank pays this share of your coins every <see cref="InterestSeconds"/>, up to <see cref="InterestCap"/> a time.</summary>
        public const double InterestRate = 0.02;
        public const double InterestSeconds = 600;
        public const int InterestCap = 500;
        /// <summary>Raiders reaching a farmhouse take this share of coins when there's no bank.</summary>
        public const double CoinTheft = 0.05;

        // ---- the farmer's basket ----------------------------------------------------------------

        /// <summary>Basket size with the best farmhouse at level 1; each farmhouse level above adds <see cref="BasketPerFarmhouseLevel"/>.</summary>
        public const int BasketBase = 50;
        public const int BasketPerFarmhouseLevel = 25;
    }

    public enum DepartmentKind
    {
        Farmhouse,
        ArmyHouse,
        WorkersHouse,
        BuildersHut,
        Market,
        /// <summary>Grain silos: store raw crops only.</summary>
        Silo,
        /// <summary>Keeps the horse (fed: rides cost its energy).</summary>
        Stable,
        /// <summary>Police station: guards and an early warning of raids.</summary>
        Watch,
    }
}
