#nullable enable
using System;
using System.Collections.Generic;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>
    /// Economy v3 rules: seasons and weather, building levels, perishables, market saturation,
    /// upgrade materials, what every building does, and the goals that lead a player through the game.
    /// </summary>
    public static partial class GameplayRules
    {
        // ---- seasons and weather ------------------------------------------------------------------

        /// <summary>A season lasts one real week (Monday to Monday, UTC); the four turn round every four weeks.</summary>
        public const double SeasonSeconds = 7 * 86400;
        /// <summary>Unix time 0 was a Thursday: shift so weeks start on Monday.</summary>
        private const double WeekShift = 3 * 86400;
        /// <summary>Weather changes this often.</summary>
        public const double WeatherSeconds = 180;

        public sealed class Season
        {
            public Season(string name, double growth, int yieldBonus, double appetite, string hint)
            {
                Name = name; Growth = growth; YieldBonus = yieldBonus; Appetite = appetite; Hint = hint;
            }
            public string Name { get; }
            /// <summary>How fast crops grow (1 = normal).</summary>
            public double Growth { get; }
            /// <summary>Extra units per field harvested.</summary>
            public int YieldBonus { get; }
            /// <summary>How much more everyone eats.</summary>
            public double Appetite { get; }
            public string Hint { get; }
        }

        public static readonly IReadOnlyList<Season> Seasons = new[]
        {
            new Season("Spring", 1.15, 0, 1.0, "Crops grow a little faster"),
            new Season("Summer", 1.0, 1, 1.0, "Fields yield one more"),
            new Season("Autumn", 0.9, 2, 1.0, "Harvest time: fields yield two more"),
            new Season("Winter", 0.6, 0, 1.2, "Crops grow slowly and everyone eats more: stock up"),
        };

        /// <summary>The season for everyone at <paramref name="now"/>: a week each, but winter all through the Winter Festival.</summary>
        public static Season SeasonAt(double now)
        {
            if (WinterFestival(now)) return Seasons[3];
            return Seasons[(int)(Math.Floor((Math.Max(0, now) + WeekShift) / SeasonSeconds) % 4)];
        }

        /// <summary>Seconds until the season turns (the end of the festival while it runs).</summary>
        public static double SeasonLeft(double now)
        {
            if (WinterFestival(now)) return FestivalLeft(now);
            return SeasonSeconds - ((Math.Max(0, now) + WeekShift) % SeasonSeconds);
        }

        public enum WeatherKind { Clear, Rain, Drought, Storm }

        /// <summary>The weather for the stretch of time containing <paramref name="now"/>: the same for everyone at that time.</summary>
        public static WeatherKind WeatherAt(double now)
        {
            var slot = (long)Math.Floor(Math.Max(0, now) / WeatherSeconds);
            var r = (int)((ulong)(slot * 2654435761L) >> 8) % 100;
            var season = SeasonAt(now).Name;
            // summer is dry, autumn stormy, spring wet
            var rain = season == "Spring" ? 35 : season == "Summer" ? 10 : 20;
            var drought = season == "Summer" ? 25 : season == "Winter" ? 0 : 8;
            var storm = season == "Autumn" ? 15 : 6;
            if (r < rain) return WeatherKind.Rain;
            if (r < rain + drought) return WeatherKind.Drought;
            if (r < rain + drought + storm) return WeatherKind.Storm;
            return WeatherKind.Clear;
        }

        /// <summary>Rain speeds crops up, drought slows them.</summary>
        public static double WeatherGrowth(WeatherKind w) => w switch { WeatherKind.Rain => 1.25, WeatherKind.Drought => 0.65, _ => 1.0 };

        public static string WeatherText(WeatherKind w) => w switch
        {
            WeatherKind.Rain => "Rain: crops sown now grow faster",
            WeatherKind.Drought => "Drought: crops sown now grow slowly",
            WeatherKind.Storm => "Storm: buildings may be damaged",
            _ => "Clear skies",
        };

        /// <summary>A storm knocks this share of health off one building.</summary>
        public const double StormDamage = 0.2;

        // ---- building levels (machines and animal houses) ---------------------------------------

        /// <summary>The highest level a machine or animal house can ever reach (the island's level opens them: see <see cref="BuildingMaxLevelAt"/>).</summary>
        public const int BuildingMaxLevel = 5;
        /// <summary>Extra stock room per level (machines get twice this in their output tray).</summary>
        public const int BuildingLevelRoom = 3;
        /// <summary>Speed at a level: +30% a level.</summary>
        public static double BuildingLevelSpeed(int level) => 1 + 0.3 * (Math.Max(1, level) - 1);
        /// <summary>Departments cost more to run as they grow: +20% food a level.</summary>
        public const double LevelUpkeep = 0.2;

        /// <summary>Coins and materials to take a machine or animal house from <paramref name="level"/> to the next.</summary>
        public static (int coins, IReadOnlyList<(string item, int count)> materials) BuildingUpgrade(string buildingId, int price, int level)
        {
            // each tier uses only materials the island can make by the level that tier opens at
            // (level 2 at island 10, 3 at 20, 4 at 30, 5 at 45: see BuildingMaxLevelAt)
            var machine = RecipeOf(buildingId) != null;
            var coins = Math.Max(100, price / 2) * (level + 1);
            IReadOnlyList<(string, int)> mats = (machine, level) switch
            {
                (true, <= 1) => new[] { ("planks", 6), ("stone", 6) },
                (true, 2) => new[] { ("planks", 8), ("stone", 8) },
                (true, 3) => new[] { ("planks", 10), ("oil", 4) },
                (true, _) => new[] { ("planks", 12), ("stone", 10), ("oil", 6) },
                (false, <= 1) => new[] { ("wood", 12), ("stone", 6) },
                (false, 2) => new[] { ("planks", 8), ("stone", 6) },
                (false, 3) => new[] { ("planks", 10), ("stone", 8) },
                (false, _) => new[] { ("planks", 12), ("oil", 4) },
            };
            return (coins, mats);
        }

        /// <summary>Materials a department needs from the farmhouse to reach <paramref name="level"/> + 1, besides its energy or stock.</summary>
        public static IReadOnlyList<(string item, int count)> DepartmentUpgradeMaterials(string buildingId, DepartmentKind kind, int level) =>
            buildingId == "facility_warehouse" ? new[] { ("planks", 10), ("stone", 12) } : DepartmentUpgradeMaterials(kind, level);

        /// <summary>Level 2 (from island level 5) takes wood and stone; level 3 (from 18) planks and more stone.</summary>
        public static IReadOnlyList<(string item, int count)> DepartmentUpgradeMaterials(DepartmentKind kind, int level) => (kind, level) switch
        {
            (DepartmentKind.Farmhouse, 1) => new[] { ("wood", 20), ("stone", 10) },
            (DepartmentKind.Farmhouse, _) => new[] { ("planks", 12), ("stone", 10) },
            (DepartmentKind.ArmyHouse, 1) => new[] { ("wood", 12), ("stone", 8) },
            (DepartmentKind.ArmyHouse, _) => new[] { ("planks", 10), ("stone", 8) },
            (_, 1) => new[] { ("wood", 10), ("stone", 6) },
            _ => new[] { ("planks", 8), ("stone", 6) },
        };

        // ---- goods ---------------------------------------------------------------------------------

        /// <summary>Fresh goods that go off if left sitting in a full house.</summary>
        public static bool Perishable(string item) => item is "egg" or "milk" or "fish" or "meat";
        /// <summary>A full house's freshest good goes off after this long uncollected.</summary>
        public const double SpoilAfterSeconds = 300;

        /// <summary>
        /// Selling a lot of one good at once floods the market: the price falls to 1/(1 + sold/25) of
        /// normal (never under 40%), and recovers as the island's sales of it drift away (half in 10 min).
        /// </summary>
        public static double MarketFactor(double recentlySold) => Math.Max(0.4, 1.0 / (1.0 + Math.Max(0, recentlySold) / 25.0));
        public const double MarketHalfLifeSeconds = 600;

        // ---- decorations and the well -------------------------------------------------------------

        /// <summary>Decorations: no work of their own, but people living near one (within <see cref="DecorReach"/>) settle in faster.</summary>
        public static bool IsDecor(string id) => id is "decor_bench" or "decor_flower_bed" or "decor_lamp_post" or "decor_mailbox" or "decor_hay_bales"
            or "decor_fountain" or "decor_beehive" or "decor_default" or "sign_default" or "seasonal_default" or "pen"
            or "fence_wood" or "fence_white" or "path_dirt" or "path_stone";
        public const double DecorReach = 18;
        /// <summary>How much faster lost people come back to a house with a decoration nearby.</summary>
        public const double DecorRecruit = 1.5;
        /// <summary>Bee hives make honey faster with flowers around (wild flowers or flower beds within <see cref="BloomReach"/>): +15% a patch, up to double.</summary>
        public const double BloomReach = 25;
        public static double Bloom(int patches) => Math.Min(2.0, 1 + 0.15 * Math.Max(0, patches));
        /// <summary>A fishing shed has to stand by the sea: water within this many cells of its plot.</summary>
        public const int ShoreCells = 3;

        /// <summary>Fields this close to a well don't suffer drought.</summary>
        public const double WellReach = 30;

        // ---- what every building does ------------------------------------------------------------

        public static string Describe(string id)
        {
            if (RecipeOf(id) is { } r)
                return $"Turns {string.Join(" + ", ListOf(r.Inputs))} into {r.Output.Replace('_', ' ')}";
            if (ProducerOf(id) is { } p)
                return $"Makes {p.Item.Replace('_', ' ')}"
                       + (SecondProducerOf(id) is { } p2 ? $", and {p2.Item.Replace('_', ' ')} now and then" : "")
                       + (p.Eats ? " (feed the animals grain)" : "")
                       + (id == "fishing_shed" ? " (must stand by the sea)" : "");
            return id switch
            {
                "field" => "Grow crops: plant, wait, harvest",
                "farmhouse" => "Stores your goods; upgrade for more room",
                "market_stall" => "Sells anything at the normal price",
                "grocer_shop" => "Pays 40% more for crops, fruit, juice and honey",
                "bakery_shop" => "Pays 50% more for bread, flour, cheese, eggs and milk",
                "fish_market" => "Pays 50% more for fish and smoked goods",
                "grain_silos" => "Stores 300 raw crops",
                "workers_house" => "Farm workers: harvest, carry, feed animals, chop wood",
                "builders_hut" => "More builders: build and repair faster",
                "army_house" => "Warriors who fight the zombies",
                "stable" => "Keeps a horse to ride (feed it)",
                "police_station" => "Guards, and a longer raid warning",
                "fire_station" => "Mends damaged buildings nearby for free",
                "clinic" => "Lost people come back twice as fast",
                "school" => "Farm workers carry 2 more a trip",
                "library" => "Machines work 15% faster",
                "chapel" => "Hunger takes people half as fast",
                "community_hall" => "Workers' houses and builders' huts take one more",
                "post_office" => "Delivery orders that pay better than the market",
                "bank" => "2% interest every 10 min; zombies can't take coins",
                "town_hall" => "20% more people everywhere",
                "potting_shed" => "Fields nearby grow 25% faster",
                "facility_well" => "Fields nearby don't suffer drought",
                "facility_warehouse" => "Stores 250 of any goods (500 upgraded)",
                "decor_fountain" => "A water trough: people nearby settle in faster",
                "fence_wood" or "fence_white" => "A fence to lay out your farm",
                "path_dirt" or "path_stone" => "A path to lay out your farm",
                "pen" => "An animal pen: people nearby settle in faster",
                _ when IsDecor(id) => "Decoration: people nearby settle in faster",
                _ => "",
            };
        }

        private static IEnumerable<string> ListOf(IReadOnlyList<(string item, int count)> items)
        {
            foreach (var (i, n) in items) yield return $"{n} {i.Replace('_', ' ')}";
        }

        // ---- goals: the path through the game ---------------------------------------------------

        public sealed class Goal
        {
            public Goal(string id, string text, string stat, int need, int reward, string hint, int level = 1)
            {
                Id = id; Text = text; Stat = stat; Need = need; Reward = reward; Hint = hint; Level = level;
            }
            public string Id { get; }
            public string Text { get; }
            /// <summary>The counter it reads (see <see cref="IslandEconomy.Stat"/>), "has:&lt;building&gt;" or "level".</summary>
            public string Stat { get; }
            public int Need { get; }
            public int Reward { get; }
            public string Hint { get; }
            /// <summary>The island level from which the goal can be done (the rule tests check it against the unlock schedule).</summary>
            public int Level { get; }
        }

        /// <summary>A goal's reward: minutes of the island's income at the goal's level.</summary>
        private static int GoalPay(int level, double minutes) => Nice(CoinIncome(level) * minutes);

        private static Goal Build(string id, string building, string text, string hint, int level) =>
            new Goal(id, text, "has:" + building, 1, GoalPay(level, 6), hint, level);
        private static Goal Make(string id, string good, int n, string text, string hint, int level) =>
            new Goal(id, text, "made:" + good, n, GoalPay(level, 5), hint, level);
        private static Goal Reach(int level, string hint) =>
            new Goal("level" + level, $"Reach island level {level}", "level", level, GoalPay(level, 8), hint, level - 1);

        /// <summary>
        /// The story goals, in order, through the three chapters (levels 1–100). Each asks only for what its
        /// level opens; a "Reach island level N" goal stands before anything that needs level N. After the
        /// last one, <see cref="GeneratedGoal"/> keeps going: a level every 5.
        /// </summary>
        public static readonly IReadOnlyList<Goal> Goals = new[]
        {
            // chapter 1: the Homestead (1–20)
            new Goal("plant", "Plant a field", "planted", 1, 50, "Buy a field in the shop and walk into it to plant"),
            new Goal("harvest", "Harvest 10 crops", "harvested", 10, 80, "Walk into a ripe field to harvest it"),
            new Goal("store", "Store 20 goods in the farmhouse", "stored", 20, 80, "Walk into the farmhouse with a full basket"),
            new Goal("sell", "Sell 15 goods at the market", "sold", 15, 100, "Walk into the market stall to sell"),
            new Goal("wood", "Chop 5 trees or rocks", "chopped", 5, 100, "Stop at a tree or rock to chop it: wood and stone build things"),
            Reach(2, "Everything you do earns island XP"),
            Build("coop", "chicken_coop", "Build a chicken coop", "Chickens lay eggs if you keep their trough fed", 2),
            Build("workers", "workers_house", "Build a workers' house", "Farm workers harvest, carry and chop for you", 2),
            new Goal("eggs", "Collect 12 eggs", "made:egg", 12, 120, "Walk into the coop to collect", 2),
            Reach(3, "The windmill and the stable open at level 3"),
            Build("mill", "windmill", "Build a windmill", "Wheat in, flour out", 3),
            Make("flour", "flour", 10, "Make 10 flour", "Feed the windmill wheat (workers do it from the farmhouse)", 3),
            Build("well", "facility_well", "Build a water tower", "It keeps nearby fields growing in a drought", 3),
            Reach(4, "The army house opens at level 4: the zombie nights begin"),
            Build("army", "army_house", "Build an army house", "The zombie nights start at level 4: warriors stop them", 4),
            Build("builders", "builders_hut", "Build a builders' hut", "Builders put up and mend buildings", 4),
            new Goal("raid", "Survive a zombie night", "raids_won", 1, GoalPay(4, 8), "Keep the army fed: hungry warriors fight at half strength", 4),
            Reach(5, "The barn opens at level 5"),
            Build("barn", "barn", "Build a barn", "Cows give milk if you keep their trough fed", 5),
            new Goal("train", "Send the order truck", "trains", 1, GoalPay(5, 6), "Load its crates at the depot: it brings back wood and stone", 5),
            Reach(6, "The bakery and the post office open at level 6"),
            Build("bakery", "bakery_factory", "Build a bakery", "Flour and eggs in, croissants out", 6),
            Make("bread", "croissant", 10, "Bake 10 croissants", "Croissants sell well and fill orders", 6),
            Build("post", "post_office", "Build a post office", "Townsfolk send delivery orders there: they pay well", 6),
            new Goal("orders", "Deliver an order", "orders", 1, GoalPay(6, 5), "Carry what the order asks for into the post office", 6),
            Reach(7, "The lumber yard opens at level 7"),
            Build("lumber", "lumber_yard", "Build a lumber yard", "Wood in, planks out: big buildings need planks", 7),
            Make("planks", "planks", 10, "Make 10 planks", "Workers bring wood from the farmhouse", 7),
            Reach(8, "Beans and the grain silos open at level 8"),
            Build("silos", "grain_silos", "Build grain silos", "A second store for crops", 8),
            new Goal("orders5", "Deliver 5 orders", "orders", 5, GoalPay(8, 6), "Orders pay more than the market", 8),
            Reach(9, "Pigs arrive at level 9"),
            Build("pigs", "pigsty", "Build a pigsty", "Pigs give meat for the market", 9),
            Reach(10, "The dairy opens at 10, and the farmhouse can grow"),
            Make("cheese", "yogurt", 10, "Make 10 yogurt", "A dairy turns milk into yogurt", 10),
            new Goal("upgrade", "Upgrade the farmhouse", "upgrades", 1, GoalPay(10, 6), "Stand in the farmhouse: upgrading takes stock, wood and stone", 10),
            Reach(11, "The warehouse and pumpkins open at 11"),
            Build("warehouse", "facility_warehouse", "Build a warehouse", "A second store for any goods", 11),
            Reach(13, "Sunflowers open at 13"),
            new Goal("orders15", "Deliver 15 orders", "orders", 15, GoalPay(13, 6), "Keep the post office busy", 13),
            Reach(15, "The grocer opens at 15"),
            Build("grocer", "grocer_shop", "Build a grocer's", "The grocer pays more for crops", 15),
            Reach(16, "The greenhouse opens at 16"),
            Build("greenhouse", "greenhouse", "Build a greenhouse", "Tomatoes grow under glass", 16),
            Reach(17, "The cider press opens at 17"),
            Make("juice", "juice", 10, "Make 10 juice", "The cider press turns tomatoes into juice", 17),
            Reach(19, "The packing shed opens at 19"),
            Make("bricks", "veg_box", 10, "Pack 10 veg boxes", "The packing shed boxes cabbages and pumpkins", 19),
            Reach(20, "The bakery shop opens at 20: chapter 1 done, and machines can reach level 2"),
            new Goal("machine2", "Upgrade a machine or animal house", "building_upgrades", 1, GoalPay(20, 6), "Faster and roomier: needs planks and stone", 20),
            // chapter 2: the Valley and the sea (21–54)
            Reach(21, "Chapter 2: the island grows a river valley, a lake and hills"),
            Reach(23, "The sugar mill opens at 23"),
            Make("oil", "oil", 10, "Make 10 oil", "The sugar mill presses sunflowers into oil", 23),
            Reach(25, "The Footballer, the first boss zombie, raids once a week from 25"),
            new Goal("raids5", "Beat 5 zombie nights", "raids_won", 5, GoalPay(25, 8), "Upgrade the army house to field more warriors", 25),
            Reach(27, "The sea opens at 27: fishing"),
            Build("fishing", "fishing_shed", "Build a fishing shed", "It must stand by the sea", 27),
            Reach(29, "The fish market opens at 29"),
            Build("fishmarket", "fish_market", "Build a fish market", "It pays more for fish and meat", 29),
            new Goal("orders40", "Deliver 40 orders", "orders", 40, GoalPay(29, 8), "Bigger islands get more orders at once", 29),
            Reach(30, "The cure: from 30 a beaten boss zombie becomes a townsperson; houses can reach level 3"),
            Reach(31, "The quarry opens at 31"),
            Build("mine", "mine_entrance", "Build a quarry", "Stone for building and upgrades", 31),
            Reach(33, "Army weapons: Bat and Crowbar at 33"),
            new Goal("raids10", "Beat 10 zombie nights", "raids_won", 10, GoalPay(33, 8), "Armed warriors hit harder", 33),
            Reach(40, "Halfway up the valley"),
            new Goal("orders80", "Deliver 80 orders", "orders", 80, GoalPay(40, 8), "Orders pay more than the market", 40),
            Reach(50, "The Clerk, the third boss, comes for your coins from 50; army weapons: Pipe and Pipe Wrench"),
            new Goal("raids20", "Beat 20 zombie nights", "raids_won", 20, GoalPay(50, 8), "Keep the army house upgraded and fed", 50),
            Reach(54, "Chapter 2 done: the cured townsfolk point to the ruined town on the plateau"),
            // chapter 3: the Town on the plateau (55–100)
            Reach(55, "Chapter 3: mountains, a waterfall and a town rise from the sea; the police station opens"),
            Build("police", "police_station", "Build the police station", "Longer warning before the zombies come", 55),
            Reach(57, "The school opens at 57"),
            Build("school", "school", "Build a school", "Farm workers carry more", 57),
            Reach(59, "The fire station opens at 59"),
            Build("fire", "fire_station", "Build a fire station", "Builders mend ruined buildings faster", 59),
            Reach(61, "The clinic opens at 61"),
            Build("clinic", "clinic", "Build a clinic", "Lost people come back twice as fast", 61),
            Reach(63, "The library opens at 63"),
            Build("library", "library", "Build a library", "Machines work faster", 63),
            Reach(65, "The chapel opens at 65"),
            Build("chapel", "chapel", "Build a chapel", "Hunger takes people half as fast", 65),
            Reach(67, "The community hall opens at 67"),
            Build("hall", "community_hall", "Build a community hall", "Houses take one more person", 67),
            Reach(70, "The bank opens at 70; the Mime sneaks in at night"),
            Build("bank", "bank", "Build a bank", "Interest, and coins zombies can't take", 70),
            Reach(75, "The town hall opens at 75"),
            Build("town", "town_hall", "Build the town hall", "Elect yourself mayor of the farm", 75),
            new Goal("orders150", "Deliver 150 orders", "orders", 150, GoalPay(75, 8), "The town's orders are the biggest", 75),
            Reach(80, "The Tourist, the last of the six, raids from 80"),
            new Goal("raids40", "Beat 40 zombie nights", "raids_won", 40, GoalPay(80, 8), "The bosses can be cured one by one", 80),
            Reach(90, "Zombie nights at their biggest: three bosses together; every building can reach level 5"),
            Reach(99, "Last stand: the Zombie Mayor's army raids every night"),
            Reach(100, "The island is whole: cure the Zombie Mayor. Every level from here keeps paying"),
        };

        /// <summary>Goals after the written ones: a level every 5, paying more as they go.</summary>
        public static Goal GeneratedGoal(int index)
        {
            var past = Math.Max(0, index - Goals.Count);
            var level = ContentLevels + 5 * (past + 1);
            return Reach(level, "Every level pays gems and coins");
        }

        /// <summary>The goal at a place in the story (written, then generated).</summary>
        public static Goal GoalAt(int index) => index < Goals.Count ? Goals[index] : GeneratedGoal(index);

        /// <summary>The ids of the story goals before economy plan v4, in their old order (old saves kept only the index).</summary>
        public static readonly IReadOnlyList<string> OldGoalIds = new[]
        {
            "plant", "harvest", "store", "sell", "coop", "wood", "workers", "eggs", "army", "level2", "well", "level3", "mill", "flour", "raid",
            "level5", "lumber", "upgrade", "level7", "bread", "orders", "cheese", "level10", "fishing", "mine", "crusher", "machine2", "level15",
            "iron", "orders2", "level20", "raids5", "level25", "bank", "town", "level30",
        };

        public sealed class DailyKind
        {
            public DailyKind(string stat, string text, int need, int minutes, int level = 1, string? needs = null)
            {
                Stat = stat; Text = text; Need = need; Minutes = minutes; Level = level; Needs = needs;
            }
            public string Stat { get; }
            /// <summary>The text at the base amount ("Harvest 40 crops"); the amount grows with level (see <see cref="DailyAmount"/>).</summary>
            public string Text { get; }
            /// <summary>The amount at level 1.</summary>
            public int Need { get; }
            /// <summary>Its reward in minutes of the island's income.</summary>
            public int Minutes { get; }
            /// <summary>The island level from which it can be given.</summary>
            public int Level { get; }
            /// <summary>A building the island must have for it (null: none).</summary>
            public string? Needs { get; }
        }

        /// <summary>Daily goals are picked from these: three a day, from the ones the island can do.</summary>
        public static readonly IReadOnlyList<DailyKind> DailyPool = new[]
        {
            new DailyKind("harvested", "Harvest {0} crops", 30, 3),
            new DailyKind("sold", "Sell {0} goods", 40, 3),
            new DailyKind("made:egg", "Collect {0} eggs", 15, 3, 2, "chicken_coop"),
            new DailyKind("chopped", "Chop {0} trees or rocks", 6, 3),
            new DailyKind("stored", "Store {0} goods", 40, 3),
            new DailyKind("planted", "Plant {0} fields", 10, 3),
            new DailyKind("fed", "Feed animals {0} times", 15, 3, 2, "chicken_coop"),
            new DailyKind("made:flour", "Mill {0} flour", 10, 4, 3, "windmill"),
            new DailyKind("orders", "Deliver {0} orders", 1, 5, 6, "post_office"),
            new DailyKind("made:milk", "Collect {0} milk", 8, 4, 5, "barn"),
            new DailyKind("raids_won", "Beat a zombie night", 1, 6, 4, "army_house"),
            new DailyKind("trains", "Send the order truck", 1, 5, 5),
            new DailyKind("made:croissant", "Bake {0} croissants", 8, 5, 6, "bakery_factory"),
            new DailyKind("made:planks", "Make {0} planks", 10, 4, 7, "lumber_yard"),
            new DailyKind("made:yogurt", "Make {0} yogurt", 6, 5, 10, "dairy_factory"),
            new DailyKind("made:meat", "Collect {0} meat", 6, 4, 9, "pigsty"),
            new DailyKind("made:juice", "Make {0} juice", 6, 5, 17, "cider_press"),
            new DailyKind("made:veg_box", "Pack {0} veg boxes", 6, 5, 19, "packing_shed"),
            new DailyKind("made:oil", "Press {0} oil", 6, 6, 23, "sugar_mill"),
        };

        /// <summary>How many a daily asks for at an island level: more as the island grows (raids and single orders stay 1 + level/10).</summary>
        public static int DailyAmount(DailyKind d, int level)
        {
            var scale = 1 + Math.Max(0, level - 1) / 10.0;
            return d.Stat is "raids_won" or "trains" ? 1 : Math.Max(1, (int)Math.Round(d.Need * scale));
        }

        /// <summary>A daily's coins at an island level: its minutes of income there.</summary>
        public static int DailyReward(DailyKind d, int level) => Nice(CoinIncome(level) * d.Minutes);

        /// <summary>The daily's text with its amount at that level.</summary>
        public static string DailyText(DailyKind d, int level) => d.Text.Replace("{0}", DailyAmount(d, level).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
