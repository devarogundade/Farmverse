#nullable enable
using System;
using System.Linq;
using Farmverse.Domain.Gameplay;
using NUnit.Framework;

namespace Farmverse.Tests.Domain.Gameplay
{
    /// <summary>Workers run the farm (docs: Workers Run the Farm): job switches, food, the offline price and cap.</summary>
    [TestFixture]
    public sealed class WorkOrdersTests
    {
        private const int House = 1, Field = 2, Coop = 3, Army = 4, Market = 5, Workers = 30, Mill = 40;

        private static IslandEconomy NewIsland(double now = 1000)
        {
            var e = new IslandEconomy(5000) { SeasonsOn = false, MarketMoves = false };
            e.AddBuilding(House, "farmhouse", now);
            e.AddBuilding(Field, GameplayRules.FieldBuilding, now);
            e.AddBuilding(Coop, "chicken_coop", now);
            e.AddBuilding(Army, "army_house", now);
            e.AddBuilding(Market, "market_stall", now);
            e.AddBuilding(Workers, "workers_house", now);
            e.Departments[Workers].Energy = 10000;
            e.Departments[Army].Energy = 10000;
            return e;
        }

        [Test]
        public void Each_building_has_its_own_jobs_and_sensible_defaults()
        {
            var e = NewIsland();
            CollectionAssert.AreEqual(new[] { WorkJob.Harvest, WorkJob.Replant }, e.WorkJobs(Field).ToArray());
            CollectionAssert.AreEqual(new[] { WorkJob.Feed, WorkJob.Collect, WorkJob.Repair }, e.WorkJobs(Coop).ToArray());
            CollectionAssert.Contains(e.WorkJobs(Market).ToList(), WorkJob.Sell);
            CollectionAssert.Contains(e.WorkJobs(Workers).ToList(), WorkJob.Restock);
            Assert.IsTrue(e.WorkOn(Field, WorkJob.Harvest));
            Assert.IsTrue(e.WorkOn(Coop, WorkJob.Collect));
            Assert.IsFalse(e.WorkOn(Coop, WorkJob.Feed), "feeding waits for the player, so stores aren't emptied by surprise");
            Assert.IsFalse(e.WorkOn(Market, WorkJob.Sell));
            Assert.IsFalse(e.WorkOn(Field, WorkJob.Sell), "not a job a field has");
        }

        [Test]
        public void Switches_survive_a_restart()
        {
            var e = NewIsland();
            e.SetWork(Field, WorkJob.Harvest, false);
            e.SetWork(Coop, WorkJob.Feed, true);
            e.SetWorkCrop(Field, "maize");
            e.WorkTrain = true;
            var back = NewIsland();
            back.Restore(e.ToSave());
            Assert.IsFalse(back.WorkOn(Field, WorkJob.Harvest));
            Assert.IsTrue(back.WorkOn(Field, WorkJob.Replant), "the others keep their defaults");
            Assert.IsTrue(back.WorkOn(Coop, WorkJob.Feed));
            Assert.AreEqual("maize", back.WorkCrop(Field));
            Assert.IsTrue(back.WorkTrain);
        }

        [Test]
        public void A_switched_off_job_never_runs()
        {
            var e = NewIsland();
            e.Plant(Field, "wheat", 1000);
            e.SetWork(Field, WorkJob.Harvest, false);
            e.SetWork(Coop, WorkJob.Collect, false);
            var r = e.WorkOffline(1000, 1000 + 1800);
            Assert.AreEqual(0, r.Harvested);
            Assert.AreEqual(0, r.Collected);
            Assert.AreEqual(PlotState.Ripe, e.Plots[Field].State(e.Now), "the wheat waits in the field");
        }

        [Test]
        public void Workers_harvest_and_resow_offline_and_it_costs_more_food()
        {
            var e = NewIsland();
            e.Plant(Field, "wheat", 1000);
            e.SetWork(Coop, WorkJob.Collect, false);
            var r = e.WorkOffline(1000, 1000 + 600);
            Assert.IsTrue(r.Harvested >= 30, $"a crop a minute, harvested and resown: {r.Harvested}");
            Assert.IsTrue(r.Replanted >= 5);
            Assert.AreEqual(1.2, r.Factor, 1e-9);
            // each harvest load costs 2 × 1.2, each sowing 1 × 1.2
            var loads = r.Loads;
            Assert.AreEqual(loads * 2 * 1.2 + r.Replanted * 1 * 1.2, r.FoodUsed, 1e-6);
            Assert.AreEqual(2 * 1.2, e.WorkEnergy(WorkJob.Harvest, true), 1e-9);
            Assert.AreEqual(2, e.WorkEnergy(WorkJob.Harvest, false), 1e-9, "on screen: the normal price");
        }

        [Test]
        public void Hungry_workers_stop_and_the_report_says_when()
        {
            var e = NewIsland();
            e.Departments[Workers].Energy = 10;   // a few loads' worth
            e.SetWork(Workers, WorkJob.Restock, false);
            e.Plant(Field, "wheat", 1000);
            var r = e.WorkOffline(1000, 1000 + 3600);
            Assert.AreEqual("food", r.Stopped);
            Assert.IsTrue(r.StoppedAt > 1000 && r.StoppedAt < 1000 + 3600);
            Assert.IsTrue(r.FoodUsed <= 10 + 1e-9);
        }

        [Test]
        public void Restock_keeps_workers_fed_from_the_farmhouse()
        {
            var e = NewIsland();
            e.Departments[Workers].Energy = 0;
            e.Store(House, "wheat", 60);
            e.Plant(Field, "wheat", 1000);
            var r = e.WorkOffline(1000, 1000 + 1200);
            Assert.IsTrue(r.Restocked > 0, "food carried home first");
            Assert.IsTrue(r.Harvested > 0, "then they could work");
        }

        [Test]
        public void Feeding_and_selling_never_touch_the_reserve()
        {
            var e = NewIsland();
            e.SetWork(Market, WorkJob.Sell, true);
            e.Store(House, "egg", 40);
            var r = e.WorkOffline(1000, 1000 + 3600);
            Assert.IsTrue(r.Sold > 0 && r.SoldCoins > 0);
            var left = e.Departments[House].Stock.TryGetValue("egg", out var n) ? n : 0;
            Assert.IsTrue(left >= GameplayRules.WorkReserve + GameplayRules.SellKeep, $"kept {left}");
        }

        [Test]
        public void A_switched_off_repair_waits()
        {
            var e = NewIsland();
            e.SetLevel(GameplayRules.RaidRuinLevel);
            e.AddBuilding(60, "builders_hut", 1000);
            e.Departments[60].Energy = 10000;
            e.Store(House, "wood", 50);
            e.Damage(Coop, 1e6, 1000);
            Assert.IsTrue(e.IsRuined(Coop));
            e.SetWork(Coop, WorkJob.Repair, false);
            e.WorkOffline(1000, 1000 + 3600);
            Assert.IsTrue(e.IsRuined(Coop), "nobody mends what the player said to leave");
            e.SetWork(Coop, WorkJob.Repair, true);
            e.WorkOffline(1000 + 3600, 1000 + 7200);
            Assert.IsFalse(e.IsRuined(Coop));
        }

        [Test]
        public void The_cap_counts_from_leaving_across_wakes_and_grows_with_the_pass()
        {
            var e = NewIsland();
            Assert.AreEqual(8, e.OfflineCapHours, 1e-9);
            var r1 = e.WorkOffline(1000, 1000 + 5 * 3600);
            var r2 = e.WorkOffline(1000 + 5 * 3600, 1000 + 10 * 3600);
            Assert.AreEqual(5 * 3600, r1.Seconds, 31);
            Assert.AreEqual(3 * 3600, r2.Seconds, 31, "the second wake works only the 3 hours left");
            Assert.IsTrue(r2.Capped);
            e.BackOnline();
            e.WorkPass = true;
            Assert.AreEqual(24, e.OfflineCapHours, 1e-9);
            e.WorkPass = false;
            e.StartNightShift();
            Assert.AreEqual(24, e.OfflineCapHours, 1e-9);
            Assert.AreEqual(1, e.OfflineFactor, 1e-9, "a night shift works at the normal food price");
            e.BackOnline();
            Assert.AreEqual(1.2, e.OfflineFactor, 1e-9);
        }

        [Test]
        public void One_long_catch_up_and_a_chain_of_wakes_do_the_same_work()
        {
            IslandEconomy Make()
            {
                var e = NewIsland();
                e.Plant(Field, "wheat", 1000);
                e.SetWork(Coop, WorkJob.Feed, true);
                e.Store(House, "wheat", 20);
                return e;
            }
            var one = Make();
            var a = one.WorkOffline(1000, 1000 + 4 * 3600);
            var chain = Make();
            var total = new AwayReport();
            for (var h = 0; h < 4; h++) total.Add(chain.WorkOffline(1000 + h * 3600, 1000 + (h + 1) * 3600));
            Assert.AreEqual(a.Harvested, total.Harvested);
            Assert.AreEqual(a.Collected, total.Collected);
            Assert.AreEqual(a.FoodUsed, total.FoodUsed, 1e-6);
            Assert.AreEqual(one.Coins, chain.Coins);
        }

        [Test]
        public void Workers_sow_the_chosen_crop()
        {
            var e = NewIsland();
            e.SetLevel(5);
            e.SetWorkCrop(Field, "maize");
            var r = e.WorkOffline(1000, 1000 + 120);
            Assert.IsTrue(r.Replanted >= 1);
            Assert.AreEqual("maize", e.Plots[Field].Crop);
        }
    }
}
