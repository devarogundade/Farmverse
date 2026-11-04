#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using NUnit.Framework;

namespace Farmverse.Tests.Domain.Gameplay
{
    /// <summary>
    /// The level plan's safety net (economy plan §11): nothing the game gives a player may need what their
    /// level hasn't opened yet. A goal, daily, recipe or upgrade that breaks this fails the build.
    /// </summary>
    [TestFixture]
    public sealed class ProgressionRulesTests
    {
        /// <summary>Every good the game knows, with the level it can first be made at.</summary>
        private static int GoodLevel(string item)
        {
            if (item is "wood" or "stone" or "mushroom" or "flowers") return 1;   // chopping, and what grows wild
            if (GameplayRules.CropOf(item) != null) return GameplayRules.CropUnlockLevel(item);
            var best = int.MaxValue;
            foreach (var id in AllBuildings())
            {
                if (GameplayRules.RecipeOf(id)?.Output == item || GameplayRules.ProducerOf(id)?.Item == item || GameplayRules.SecondProducerOf(id)?.Item == item)
                    best = Math.Min(best, GameplayRules.UnlockLevel(id));
            }
            return best;
        }

        private static IEnumerable<string> AllBuildings() => new[]
        {
            "field", "farmhouse", "market_stall", "chicken_coop", "workers_house", "builders_hut", "army_house", "facility_well", "windmill",
            "post_office", "barn", "feed_mill", "bakery_factory", "lumber_yard", "grain_silos", "dairy_factory", "pigsty",
            "facility_warehouse", "greenhouse", "potting_shed", "cider_press", "packing_shed", "stable", "pen", "fishing_shed",
            "mine_entrance", "sugar_mill", "fish_market", "grocer_shop",
            "bakery_shop", "police_station", "school", "fire_station", "clinic", "library", "chapel", "community_hall", "bank", "town_hall",
            "decor_bench", "decor_flower_bed", "decor_default", "fence_wood", "fence_white", "path_dirt", "path_stone", "decor_hay_bales",
            "decor_lamp_post", "sign_default", "decor_mailbox", "decor_beehive", "decor_fountain", "seasonal_default",
        };

        [Test]
        public void Every_recipe_opens_after_its_inputs()
        {
            foreach (var id in AllBuildings())
            {
                if (GameplayRules.RecipeOf(id) is not { } r) continue;
                foreach (var (item, _) in r.Inputs)
                    Assert.IsTrue(GoodLevel(item) <= GameplayRules.UnlockLevel(id), $"{id} (level {GameplayRules.UnlockLevel(id)}) needs {item} (level {GoodLevel(item)})");
            }
        }

        [Test]
        public void Every_level_of_chapter_1_opens_something()
        {
            for (var l = 2; l <= GameplayRules.ChapterEnds[0]; l++)
            {
                var opens = AllBuildings().Any(b => GameplayRules.UnlockLevel(b) == l) || GameplayRules.Crops.Any(c => GameplayRules.CropUnlockLevel(c.Id) == l);
                Assert.IsTrue(opens, $"level {l} opens nothing");
                Assert.IsTrue(GameplayRules.LevelPerks(l).Count > 0);
            }
        }

        [Test]
        public void Story_goals_only_ask_for_what_their_level_opens()
        {
            var reached = 1;
            foreach (var g in GameplayRules.Goals)
            {
                if (g.Stat == "level") { Assert.IsTrue(g.Need > reached, $"{g.Id} goes backwards"); reached = g.Need; continue; }
                Assert.IsTrue(g.Level <= reached, $"{g.Id} needs level {g.Level} but only {reached} is asked for before it");
                if (g.Stat.StartsWith("has:", StringComparison.Ordinal))
                    Assert.IsTrue(GameplayRules.UnlockLevel(g.Stat.Substring(4)) <= reached, $"{g.Id}: {g.Stat} opens later than level {reached}");
                if (g.Stat.StartsWith("made:", StringComparison.Ordinal))
                    Assert.IsTrue(GoodLevel(g.Stat.Substring(5)) <= reached, $"{g.Id}: {g.Stat} can't be made by level {reached}");
                if (g.Stat == "orders") Assert.IsTrue(reached >= GameplayRules.UnlockLevel("post_office"), $"{g.Id} asks for orders before the post office");
                if (g.Stat == "raids_won") Assert.IsTrue(reached >= GameplayRules.RaidStartLevel, $"{g.Id} asks for a raid before raids start");
                if (g.Stat == "trains") Assert.IsTrue(reached >= GameplayRules.TrainLevel, $"{g.Id} asks for the train before it comes");
                if (g.Stat == "building_upgrades") Assert.IsTrue(GameplayRules.BuildingMaxLevelAt(reached) >= 2, $"{g.Id} asks for a machine upgrade too early");
                if (g.Stat == "upgrades") Assert.IsTrue(GameplayRules.DepartmentMaxLevelAt(reached) >= 2, $"{g.Id} asks for a house upgrade too early");
            }
            Assert.AreEqual(GameplayRules.ContentLevels, reached, "the written goals run to the end of the last chapter");
            var next = GameplayRules.GoalAt(GameplayRules.Goals.Count);
            Assert.AreEqual("level", next.Stat);
            Assert.IsTrue(next.Need > GameplayRules.ContentLevels, "and then keep going");
        }

        [Test]
        public void Dailies_need_their_building_and_level()
        {
            foreach (var d in GameplayRules.DailyPool)
            {
                if (d.Needs != null) Assert.IsTrue(GameplayRules.UnlockLevel(d.Needs) <= d.Level, $"{d.Stat}: {d.Needs} opens after level {d.Level}");
                if (d.Stat.StartsWith("made:", StringComparison.Ordinal))
                    Assert.IsTrue(GoodLevel(d.Stat.Substring(5)) <= d.Level, $"{d.Stat} can't be made at level {d.Level}");
                if (d.Stat == "orders") Assert.AreEqual("post_office", d.Needs);
            }
            // a new island only gets dailies it can do
            var e = new IslandEconomy(500) { SeasonsOn = false };
            e.AddBuilding(1, "farmhouse", 1000);
            e.Tick(1000);
            foreach (var i in e.Dailies)
            {
                var d = GameplayRules.DailyPool[i];
                Assert.IsTrue(d.Level <= 1 && (d.Needs == null || e.Has(d.Needs)), $"day 1 got {d.Stat}");
            }
            Assert.AreEqual(3, e.Dailies.Count);
        }

        [Test]
        public void Upgrades_use_only_materials_open_by_their_tier()
        {
            for (var level = 1; level <= 4; level++)
            {
                // the island level at which a machine can reach level + 1
                var at = Enumerable.Range(1, 100).First(l => GameplayRules.BuildingMaxLevelAt(l) >= level + 1);
                foreach (var id in new[] { "windmill", "chicken_coop" })
                    foreach (var (item, _) in GameplayRules.BuildingUpgrade(id, 1000, level).materials)
                        Assert.IsTrue(GoodLevel(item) <= at, $"{id} level {level + 1} (island {at}) needs {item} (level {GoodLevel(item)})");
            }
            for (var level = 1; level <= 2; level++)
            {
                var at = Enumerable.Range(1, 100).First(l => GameplayRules.DepartmentMaxLevelAt(l) >= level + 1);
                foreach (var kind in new[] { DepartmentKind.Farmhouse, DepartmentKind.ArmyHouse, DepartmentKind.WorkersHouse })
                    foreach (var (item, _) in GameplayRules.DepartmentUpgradeMaterials(kind, level))
                        Assert.IsTrue(GoodLevel(item) <= at, $"{kind} level {level + 1} (island {at}) needs {item}");
            }
        }

        [Test]
        public void Orders_ask_only_for_what_the_island_can_make()
        {
            var e = new IslandEconomy(500) { SeasonsOn = false, MarketMoves = false, Rng = new Random(3) };
            e.AddBuilding(1, "farmhouse", 1000);
            e.AddBuilding(2, GameplayRules.FieldBuilding, 1000);
            e.AddBuilding(3, "post_office", 1000);
            e.Tick(1000);
            Assert.IsNull(e.Order, "no orders before level 6");
            e.AddXp(GameplayRules.XpAt(6));
            for (var t = 1000; t < 1000 + 3600; t += 30) e.Tick(t);
            Assert.AreEqual(1, e.Orders.Count, "one slot at level 6");
            foreach (var o in e.Orders)
                Assert.IsTrue(o.Item == "wood" || (GameplayRules.CropOf(o.Item) != null && GameplayRules.CropUnlockLevel(o.Item) <= 4), $"asked for {o.Item}");
        }

        [Test]
        public void The_island_grows_with_its_chapters()
        {
            Assert.AreEqual(1, GameplayRules.IslandChapterFor(1));
            Assert.AreEqual(1, GameplayRules.IslandChapterFor(20));
            Assert.AreEqual(2, GameplayRules.IslandChapterFor(21));
            Assert.AreEqual(2, GameplayRules.IslandChapterFor(54));
            Assert.AreEqual(3, GameplayRules.IslandChapterFor(55));
            Assert.AreEqual(3, GameplayRules.IslandChapterFor(400), "and stays full size");
            Assert.IsTrue(GameplayRules.LevelPerks(21).Any(p => p.StartsWith("The island grows", StringComparison.Ordinal)));
            Assert.IsTrue(GameplayRules.LevelPerks(55).Any(p => p.StartsWith("The island grows", StringComparison.Ordinal)));
            Assert.IsFalse(GameplayRules.LevelPerks(22).Any(p => p.StartsWith("The island grows", StringComparison.Ordinal)));
            // what lives on the new land opens with it: fishing and the mine with chapter 2, the town with chapter 3
            Assert.AreEqual(2, GameplayRules.IslandChapterFor(GameplayRules.UnlockLevel("fishing_shed")));
            Assert.AreEqual(2, GameplayRules.IslandChapterFor(GameplayRules.UnlockLevel("mine_entrance")));
            foreach (var town in new[] { "school", "clinic", "library", "chapel", "community_hall", "bank", "town_hall" })
                Assert.AreEqual(3, GameplayRules.IslandChapterFor(GameplayRules.UnlockLevel(town)), town);
        }

        [Test]
        public void Prices_follow_the_plan()
        {
            Assert.AreEqual(410, GameplayRules.BuildingPrice("chicken_coop", 250), "level 2: twelve minutes of income");
            Assert.IsTrue(GameplayRules.BuildingPrice("town_hall", 3000) > GameplayRules.BuildingPrice("bank", 2500));
            Assert.AreEqual(20, GameplayRules.BuildingPrice(GameplayRules.FieldBuilding, 20), "fields keep their price");
            Assert.IsTrue(GameplayRules.PriceOf("croissant") > GameplayRules.PriceOf("flour") + GameplayRules.PriceOf("egg"), "made goods are worth more than what went in");
            Assert.AreEqual(4, GameplayRules.FieldCap(1));
            Assert.AreEqual(24, GameplayRules.FieldCap(40));
            Assert.AreEqual(0, GameplayRules.OrderSlots(3));
            Assert.AreEqual(4, GameplayRules.OrderSlots(25));
        }
    }
}
