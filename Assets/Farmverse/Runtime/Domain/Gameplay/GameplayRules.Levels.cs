#nullable enable
using System;
using System.Collections.Generic;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>
    /// Island levels (docs: Farmverse — Using the ITHappy packs, chapters). Levels never end: XP to the next
    /// level comes from one formula, growing with the level up to 100 and by 1% a level after. Content comes in
    /// three chapters of 100 levels: the Homestead (1–20, something every level), the Valley (21–54) and the
    /// Town (55–100); past the last one, levels pay gems and decorations.
    /// Every task the game gives checks this table first, so nothing asks for what the player can't do yet.
    /// </summary>
    public static partial class GameplayRules
    {
        /// <summary>The last level of the chapters this version ships; levels past it keep paying, with no ceiling.</summary>
        public const int ContentLevels = 100;

        /// <summary>Where each chapter ends (its last level pays a bigger reward).</summary>
        public static readonly IReadOnlyList<int> ChapterEnds = new[] { 20, 54, 100 };

        public static string ChapterOf(int level) => level <= ChapterEnds[0] ? "Homestead" : level <= ChapterEnds[1] ? "Valley" : level <= ChapterEnds[2] ? "Town" : "Mastery";

        /// <summary>
        /// The island's own chapter at a level (docs: Island by Chapter): it grows at 21 (river valley, lake,
        /// hills, cove) and 55 (mountains, waterfall, town plateau, harbour), and stays that size after.
        /// </summary>
        public static int IslandChapterFor(int level) => level <= ChapterEnds[0] ? 1 : level <= ChapterEnds[1] ? 2 : 3;

        /// <summary>What a chapter's new land brings, for the chapter card.</summary>
        public static string IslandChapterText(int chapter) => chapter switch
        {
            2 => "The island grows: a river with a lake and bridges, orchard and quarry hills, and a cove for fishing boats",
            3 => "The island grows: mountains with a waterfall, a town plateau, and a harbour with a lighthouse",
            _ => "A small farm island to make your own",
        };

        /// <summary>XP from the start of <paramref name="level"/> to the next: 45·L^1.8 + 30 up to 100, then +1% a level.</summary>
        public static int XpToNext(int level)
        {
            var l = Math.Max(1, level);
            if (l <= 100) return (int)Math.Round((45 * Math.Pow(l, 1.8) + 30) / 10.0) * 10;
            return (int)Math.Round(XpToNext(100) * (1 + 0.01 * (l - 100)) / 10.0) * 10;
        }

        /// <summary>Total XP at which <paramref name="level"/> starts.</summary>
        public static long XpAt(int level)
        {
            long xp = 0;
            for (var l = 1; l < Math.Max(1, level); l++) xp += XpToNext(l);
            return xp;
        }

        /// <summary>The island level for a total of XP (1 to start, no limit).</summary>
        public static int LevelForXp(long xp)
        {
            var level = 1;
            long at = 0;
            while (level < 100_000 && at + XpToNext(level) <= xp) { at += XpToNext(level); level++; }
            return level;
        }

        /// <summary>XP for one of a counted deed (see <see cref="IslandEconomy.Count"/>); a good made is worth its value ÷ 5.</summary>
        public static int XpOf(string stat) => stat switch
        {
            "planted" => 2,
            "harvested" => 1,
            "sold" => 1,
            "chopped" => 3,
            "orders" => 30,
            "upgrades" => 50,
            "building_upgrades" => 50,
            "raids_won" => 60,
            _ when stat.StartsWith("made:", StringComparison.Ordinal) => Math.Max(2, PriceOf(stat.Substring(5)) / 5),
            _ => 0,
        };

        /// <summary>XP for a building when it's finished.</summary>
        public static int XpForBuilding(int price) => Math.Max(5, price / 20);

        /// <summary>XP for claiming a goal (story goals give more than dailies).</summary>
        public static int XpForGoal(int reward) => 20 + reward / 10;

        // ---- the island's income: what prices and rewards are measured in ----------------------------

        /// <summary>Planned coins a minute of active play at a level: 30 at level 1, +15% of that a level, flat after 80.</summary>
        public static double CoinIncome(int level) => 30 * (1 + 0.15 * (Math.Min(Math.Max(1, level), 80) - 1));

        /// <summary>Round a price to a number that reads well (5s, 10s, 50s, 100s, 500s).</summary>
        public static int Nice(double v)
        {
            var step = v < 100 ? 5 : v < 1000 ? 10 : v < 2000 ? 50 : v < 10000 ? 100 : 500;
            return Math.Max(step, (int)(Math.Round(v / step) * step));
        }

        /// <summary>Coins given on reaching <paramref name="level"/>: five minutes of income there, more at a chapter's end.</summary>
        public static int LevelReward(int level) =>
            level <= 1 ? 0 : Nice(CoinIncome(level) * (IsChapterEnd(level) ? 15 : 5));

        /// <summary>Gems for reaching <paramref name="level"/> (paid by the server): 2 a level, 5 at a chapter's end, 3 past the last chapter.</summary>
        public static int LevelGems(int level) => level <= 1 ? 0 : IsChapterEnd(level) ? 5 : level > ContentLevels ? 3 : 2;

        public static bool IsChapterEnd(int level)
        {
            foreach (var e in ChapterEnds) if (e == level) return true;
            return false;
        }

        // ---- what opens when ---------------------------------------------------------------------------

        /// <summary>
        /// The island level a building needs before the shop sells it, in the order the production chains need
        /// them: every input of a recipe opens at or before the recipe.
        /// </summary>
        public static int UnlockLevel(string buildingId) => buildingId switch
        {
            // chapter 1: the Homestead — something every level
            "chicken_coop" or "workers_house" => 2,
            "windmill" or "stable" or "facility_well" => 3,
            "builders_hut" or "army_house" => 4,
            "barn" or "feed_mill" => 5,
            "bakery_factory" or "post_office" => 6,
            "lumber_yard" or "decor_bench" or "decor_flower_bed" or "decor_default" => 7,
            "grain_silos" or "fence_wood" or "fence_white" or "path_dirt" or "path_stone" => 8,
            "pigsty" or "decor_hay_bales" or "decor_lamp_post" => 9,
            "dairy_factory" => 10,
            "facility_warehouse" => 11,
            "pen" => 12,
            "sign_default" or "decor_mailbox" => 13,
            "decor_beehive" or "decor_fountain" => 14,
            "grocer_shop" => 15,
            "greenhouse" => 16,
            "cider_press" => 17,
            "potting_shed" => 18,
            "packing_shed" => 19,
            "bakery_shop" => 20,
            // chapter 2: the Valley and the sea
            "seasonal_default" => 21,
            "sugar_mill" => 23,
            "fishing_shed" => 27,
            "fish_market" => 29,
            "mine_entrance" => 31,
            // chapter 3: the Town on the plateau
            "police_station" => 55,
            "school" => 57,
            "fire_station" => 59,
            "clinic" => 61,
            "library" => 63,
            "chapel" => 65,
            "community_hall" => 67,
            "bank" => 70,
            "town_hall" => 75,
            _ => 1,
        };

        /// <summary>The level a crop can first be planted at.</summary>
        public static int CropUnlockLevel(string cropId) => cropId switch
        {
            "maize" => 2,
            "cabbage" => 4,
            "beans" => 8,
            "pumpkin" => 11,
            "sunflower" => 13,
            "tomato" => 16,
            _ => 1,
        };

        /// <summary>How many fields an island may have: 4 at level 1, then more, up to 24 at level 39.</summary>
        public static int FieldCap(int level)
        {
            var l = Math.Max(1, level);
            return Math.Min(24, l <= 3 ? l + 3 : 6 + (l - 3) / 2);
        }

        /// <summary>Delivery orders open at once: none before the post office's level, then 1, 2 at 10, 3 at 15, 4 at 25, 5 at 55, 6 at 88.</summary>
        public static int OrderSlots(int level) => level < 6 ? 0 : level < 10 ? 1 : level < 15 ? 2 : level < 25 ? 3 : level < 55 ? 4 : level < 88 ? 5 : 6;

        /// <summary>The day's raid comes from this level on (two levels after the army house opens).</summary>
        public const int RaidStartLevel = 4;
        /// <summary>No raid ruins a building before this level.</summary>
        public const int RaidRuinLevel = 15;

        /// <summary>How high machines and animal houses can be levelled at this island level: 2 at 20, 3 at 30, 4 at 70, 5 at 90.</summary>
        public static int BuildingMaxLevelAt(int islandLevel) => islandLevel >= 90 ? 5 : islandLevel >= 70 ? 4 : islandLevel >= 30 ? 3 : islandLevel >= 20 ? 2 : 1;

        /// <summary>How high houses (farmhouse, army, workers, builders, silos, warehouse, police) can go: 2 from 10, 3 from 30, 4 from 60, 5 from 90.</summary>
        public static int DepartmentMaxLevelAt(int islandLevel) => islandLevel >= 90 ? 5 : islandLevel >= 60 ? 4 : islandLevel >= 30 ? 3 : islandLevel >= 10 ? 2 : 1;

        /// <summary>Extra basket room from the island's level: +2 every 5 levels, up to level 100.</summary>
        public static int BasketBonus(int islandLevel) => Math.Min(islandLevel, ContentLevels) / 5 * 2;

        /// <summary>What reaching a level gives besides new buildings (for the level-up screen).</summary>
        public static IReadOnlyList<string> LevelPerks(int level)
        {
            var perks = new List<string>();
            if (LevelReward(level) > 0) perks.Add($"{LevelReward(level)} coins");
            if (LevelGems(level) > 0) perks.Add($"{LevelGems(level)} gems");
            if (FieldCap(level) > FieldCap(level - 1)) perks.Add($"{FieldCap(level)} fields");
            foreach (var c in Crops) if (CropUnlockLevel(c.Id) == level && level > 1) perks.Add($"New crop: {c.Id}");
            if (OrderSlots(level) > OrderSlots(level - 1)) perks.Add(OrderSlots(level) == 1 ? "Delivery orders" : $"{OrderSlots(level)} orders at a time");
            if (level == RaidStartLevel) perks.Add("The zombie nights begin: one raid a day");
            if (level == TrainLevel) perks.Add("The order truck calls at your depot: load crates for building materials");
            if (level <= ContentLevels && level % 5 == 0) perks.Add("+2 basket room");
            if (BuildingMaxLevelAt(level) > BuildingMaxLevelAt(level - 1)) perks.Add($"Machines and animal houses can reach level {BuildingMaxLevelAt(level)}");
            if (DepartmentMaxLevelAt(level) > DepartmentMaxLevelAt(level - 1)) perks.Add($"Houses can reach level {DepartmentMaxLevelAt(level)}");
            if (IsChapterEnd(level)) perks.Add($"Chapter complete: {ChapterOf(level)}");
            if (IslandChapterFor(level) > IslandChapterFor(level - 1)) perks.Add(IslandChapterText(IslandChapterFor(level)));
            return perks;
        }

        // ---- prices -----------------------------------------------------------------------------------

        /// <summary>Minutes of income a building at its unlock level costs: 10 at level 1, +2 a level to 60, then +1.5 a level.</summary>
        public static double PaybackMinutes(int level) => level <= 26 ? Math.Min(60, 10 + 2 * (level - 1)) : 60 + 1.5 * (level - 26);

        /// <summary>
        /// A building's price from its unlock level: what the island earns in <see cref="PaybackMinutes"/> there.
        /// Fields, decorations, fences and paths keep their small catalogue price; the farmhouse is free.
        /// </summary>
        public static int BuildingPrice(string buildingId, int cataloguePrice)
        {
            if (buildingId == FieldBuilding || IsDecor(buildingId) || cataloguePrice <= 0 || buildingId == "farmhouse") return cataloguePrice;
            var l = UnlockLevel(buildingId);
            return Nice(CoinIncome(l) * PaybackMinutes(l));
        }
    }
}
