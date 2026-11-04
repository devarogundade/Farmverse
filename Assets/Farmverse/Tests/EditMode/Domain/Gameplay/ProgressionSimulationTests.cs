#nullable enable
using System;
using System.Linq;
using Farmverse.Domain.Gameplay;
using NUnit.Framework;

namespace Farmverse.Tests.Domain.Gameplay
{
    /// <summary>
    /// The level plan checked against the island's own rules (economy plan §11): an island built the way the
    /// plan expects at a level (as many fields as its cap, the best crop it has, workers to work them) is run
    /// for an hour by its farm workers, and what they bring in is compared with the income the prices and the
    /// XP curve are planned on. Workers alone are the floor of what a player earns (the player adds selling,
    /// orders and machines), so the hour must reach a share of the plan, and must not run away from it.
    /// </summary>
    [TestFixture]
    public sealed class ProgressionSimulationTests
    {
        private static readonly string[] Production =
        {
            "chicken_coop", "windmill", "barn", "feed_mill", "bakery_factory", "lumber_yard", "dairy_factory", "pigsty",
            "greenhouse", "cider_press", "fishing_shed", "mine_entrance", "sugar_mill", "packing_shed",
        };

        /// <summary>Coins' worth a minute the workers bring in on a planned island at <paramref name="level"/>.</summary>
        public static double WorkerIncome(int level)
        {
            const double t0 = 10_000;
            var e = new IslandEconomy(1_000_000) { SeasonsOn = false, MarketMoves = false };
            e.SetLevel(level);
            e.AddBuilding(1, "farmhouse", t0);
            var crop = GameplayRules.Crops.Where(c => e.CropOpen(c.Id)).OrderByDescending(c => GameplayRules.PriceOf(c.Id) * c.Yield / (double)c.GrowSeconds).First();
            var fields = GameplayRules.FieldCap(level);
            for (var i = 0; i < fields; i++)
            {
                e.AddBuilding(100 + i, GameplayRules.FieldBuilding, t0);
                e.Plant(100 + i, crop.Id, t0);
            }
            // one of every animal house and machine the level has opened, as a planned island would
            var uid = 300;
            foreach (var b in Production.Where(b => GameplayRules.UnlockLevel(b) <= level)) e.AddBuilding(uid++, b, t0);
            // enough workers: one house per 4 fields or workshops
            for (var i = 0; i < Math.Max(1, (fields + uid - 300 + 3) / 4); i++) e.AddBuilding(200 + i, "workers_house", t0);
            e.Tick(t0);
            var report = e.WorkWhileAway(t0, t0 + 3600);
            var value = report.Gathered.Sum(kv => (double)GameplayRules.PriceOf(kv.Key) * kv.Value) - report.SeedCoins;
            return value / 60.0;
        }

        [Test]
        public void Workers_earn_a_fair_share_of_the_planned_income_at_every_stage()
        {
            foreach (var level in new[] { 1, 5, 10, 20, 30 })
            {
                var have = WorkerIncome(level);
                var plan = GameplayRules.CoinIncome(level);
                Console.WriteLine($"[sim] level {level}: workers {have:0} coins/min, plan {plan:0}");
                Assert.IsTrue(have >= plan * 0.25, $"level {level}: workers bring {have:0}/min, the plan expects {plan:0}: prices or caps too mean");
                Assert.IsTrue(have <= plan * 3, $"level {level}: workers alone bring {have:0}/min against a plan of {plan:0}: prices run away");
            }
        }

        [Test]
        public void The_next_building_is_always_within_reach()
        {
            // at every level to 40, the dearest building that opens there costs at most ~2 h of planned income
            for (var level = 1; level <= 40; level++)
            {
                var price = new[] { "chicken_coop", "windmill", "post_office", "barn", "bakery_factory", "lumber_yard", "dairy_factory", "pigsty",
                    "greenhouse", "cider_press", "packing_shed", "stable", "fishing_shed", "mine_entrance",
                    "sugar_mill", "fish_market", "grocer_shop", "bakery_shop", "police_station", "school", "fire_station", "clinic",
                    "library", "chapel", "community_hall", "bank", "town_hall" }
                    .Where(b => GameplayRules.UnlockLevel(b) == level).Select(b => GameplayRules.BuildingPrice(b, 1000)).DefaultIfEmpty(0).Max();
                Assert.IsTrue(price <= GameplayRules.CoinIncome(level) * 120, $"level {level}: {price} coins is more than 2 hours of income");
            }
        }
    }
}
