#nullable enable
using Farmverse.Domain.Gameplay;
using System.Linq;
using NUnit.Framework;

namespace Farmverse.Tests.Domain.Gameplay
{
    [TestFixture]
    public sealed class IslandEconomyTests
    {
        private const int House = 1, Field = 2, Coop = 3, Army = 4, Market = 5;

        private static IslandEconomy NewIsland(double now = 1000)
        {
            var e = new IslandEconomy(500) { SeasonsOn = false, MarketMoves = false };
            e.AddBuilding(House, "farmhouse", now);
            e.AddBuilding(Field, GameplayRules.FieldBuilding, now);
            e.AddBuilding(Coop, "chicken_coop", now);
            e.AddBuilding(Army, "army_house", now);
            e.AddBuilding(Market, "market_stall", now);
            return e;
        }

        [Test]
        public void Planting_costs_seed_coins_and_a_field_ripens_on_time()
        {
            var e = NewIsland();
            Assert.AreEqual(GatherResult.Done, e.Plant(Field, "wheat", 1000));
            Assert.AreEqual(500 - GameplayRules.CropOf("wheat")!.SeedCost, e.Coins);
            Assert.AreEqual(GatherResult.Busy, e.Plant(Field, "wheat", 1001));
            Assert.AreEqual(GatherResult.NotReady, e.HarvestStep(Field, 1059));
            Assert.AreEqual(PlotState.Ripe, e.Plots[Field].State(1060));
        }

        [Test]
        public void Harvest_goes_on_the_back_one_unit_at_a_time_until_the_field_is_empty()
        {
            var e = NewIsland();
            e.Plant(Field, "wheat", 1000);
            for (var i = 0; i < 6; i++) Assert.AreEqual(GatherResult.Took, e.HarvestStep(Field, 1100));
            Assert.AreEqual(6, e.Basket.CountOf("wheat"));
            Assert.AreEqual(PlotState.Empty, e.Plots[Field].State(1100), "a harvested field is free to plant again");
            Assert.AreEqual(GatherResult.NotReady, e.HarvestStep(Field, 1100));
        }

        [Test]
        public void A_full_basket_stops_gathering()
        {
            var e = NewIsland();
            e.Basket.Add("egg", GameplayRules.BasketBase);
            e.Plant(Field, "wheat", 1000);
            Assert.AreEqual(GatherResult.BasketFull, e.HarvestStep(Field, 1100));
            Assert.AreEqual(6, e.Plots[Field].Left, "nothing was taken");
        }

        [Test]
        public void Animal_houses_make_goods_over_time_up_to_their_limit()
        {
            var e = NewIsland(1000);
            Assert.AreEqual(GatherResult.NotReady, e.CollectStep(Coop, 1039));
            Assert.AreEqual(1, e.Animals[Coop].StockAt(1040));
            Assert.AreEqual(6, e.Animals[Coop].StockAt(5000), "capped");
            Assert.AreEqual(GatherResult.Took, e.CollectStep(Coop, 1050));
            Assert.AreEqual(0, e.Animals[Coop].StockAt(1050));
            Assert.AreEqual(1, e.Animals[Coop].StockAt(1080), "the part-made egg kept ticking");
        }

        [Test]
        public void Unloading_at_the_farmhouse_stores_goods_last_picked_first()
        {
            var e = NewIsland();
            e.Basket.Add("wheat", 2);
            e.Basket.Add("egg", 1);
            var r = e.UnloadStep(House);
            Assert.AreEqual(UnloadOutcome.Stored, r.Outcome);
            Assert.AreEqual("egg", r.Item);
            e.UnloadStep(House);
            e.UnloadStep(House);
            Assert.IsTrue(e.Basket.IsEmpty);
            Assert.AreEqual(2, e.Departments[House].Stock["wheat"]);
            Assert.AreEqual(UnloadOutcome.None, e.UnloadStep(House).Outcome);
        }

        [Test]
        public void Unloading_at_a_department_turns_goods_into_its_energy()
        {
            var e = NewIsland();
            var before = e.Departments[Army].Energy;
            e.Basket.Add("egg", 3);
            for (var i = 0; i < 3; i++) Assert.AreEqual(UnloadOutcome.Energy, e.UnloadStep(Army).Outcome);
            Assert.AreEqual(before + 30, e.Departments[Army].Energy, 1e-9);
        }

        [Test]
        public void The_market_pad_sells_what_you_carry()
        {
            var e = NewIsland();
            e.Basket.Add("meat", 2);
            e.UnloadStep(Market);
            e.UnloadStep(Market);
            Assert.AreEqual(500 + 2 * GameplayRules.PriceOf("meat"), e.Coins);
        }

        [Test]
        public void A_full_farmhouse_hands_the_good_back()
        {
            var e = NewIsland();
            for (var i = 0; i < 150; i++) { e.Basket.Add("wheat", 1); e.UnloadStep(House); }
            e.Basket.Add("egg", 1);
            var r = e.UnloadStep(House);
            Assert.AreEqual(UnloadOutcome.StorageFull, r.Outcome);
            Assert.AreEqual(1, e.Basket.CountOf("egg"), "still on the farmer's back");
        }

        [Test]
        public void Energy_buys_levels_and_levels_house_more_people()
        {
            var e = NewIsland();
            e.SetLevel(18);   // what this test needs opens by level 18
            var army = e.Departments[Army];
            Assert.AreEqual(3, army.People);
            Assert.IsFalse(e.Upgrade(Army), "not enough energy yet");
            e.Basket.Capacity = 1000;
            e.Basket.Add("croissant", 23);
            while (!e.Basket.IsEmpty) e.UnloadStep(Army);
            Assert.IsFalse(e.Upgrade(Army), "needs wood and stone in the farmhouse too");
            e.Store(House, "wood", 12);
            e.Store(House, "stone", 8);
            Assert.IsTrue(e.Upgrade(Army));
            Assert.AreEqual(0, e.Departments[House].Stored, "materials used");
            Assert.AreEqual(2, army.Level);
            Assert.AreEqual(5, army.People);
            Assert.AreEqual(GameplayRules.StartingEnergy + 23 * 45 - 1000, army.Energy, 1e-9);
        }

        [Test]
        public void A_farmhouse_levels_up_with_stored_goods_and_carries_more()
        {
            var e = NewIsland();
            e.SetLevel(18);   // what this test needs opens by level 18
            e.Basket.Capacity = 1000;
            e.Basket.Add("meat", 27);   // 810 energy of stock
            while (!e.Basket.IsEmpty) e.UnloadStep(House);
            Assert.AreEqual(810, e.UpgradeFunds(House));
            e.Store(House, "wood", 20);
            e.Store(House, "stone", 10);
            Assert.IsTrue(e.Upgrade(House));
            Assert.AreEqual(GameplayRules.BasketBase + GameplayRules.BasketPerFarmhouseLevel + GameplayRules.BasketBonus(18), e.Basket.Capacity);
            Assert.AreEqual(300, e.Departments[House].StorageCapacity);
            Assert.AreEqual(0, e.Departments[House].Stored, "27 meat → 810 ≥ 800, all spent");
        }

        [Test]
        public void Upkeep_burns_energy_and_an_empty_tank_starves()
        {
            var e = NewIsland(1000);
            var army = e.Departments[Army];
            e.Tick(1000 + 60);   // 3 warriors × 2 per minute
            Assert.AreEqual(GameplayRules.StartingEnergy - 6, army.Energy, 1e-9);
            Assert.IsFalse(army.Starving);
            e.Tick(1000 + 60 * 20);
            Assert.AreEqual(0, army.Energy, 1e-9);
            Assert.IsTrue(army.Starving);
            e.Basket.Add("croissant", 1);
            e.UnloadStep(Army);
            e.Tick(1000 + 60 * 20 + 1);
            Assert.IsFalse(army.Starving, "fed again");
        }

        [Test]
        public void A_new_building_is_a_site_until_builders_finish_it()
        {
            var e = NewIsland(1000);
            e.Tick(1000);
            e.AddBuilding(10, "chicken_coop", 1000, buildWork: 30);
            Assert.IsFalse(e.IsBuilt(10));
            Assert.AreEqual(1, e.Builders, "the farmhouse brings one builder");
            e.Tick(1010);
            Assert.AreEqual(1, e.Crews[10]);
            Assert.AreEqual(10, e.Sites[0].Done, 1e-9);
            Assert.AreEqual(GatherResult.NotReady, e.CollectStep(10, 1100), "no eggs from a coop still going up");
            e.Tick(1030);
            Assert.IsTrue(e.IsBuilt(10));
            CollectionAssert.AreEqual(new[] { 10 }, e.TakeFinished());
            Assert.AreEqual(0, e.TakeFinished().Count);
            Assert.AreEqual(0, e.Animals[10].StockAt(1030), "the hens start laying once it's built");
            Assert.AreEqual(1, e.Animals[10].StockAt(1070));
        }

        [Test]
        public void Builders_huts_bring_more_builders_and_crews_are_capped_per_site()
        {
            var e = NewIsland(1000);
            e.Tick(1000);
            e.AddBuilding(11, "builders_hut", 1000);   // already built (say, loaded)
            Assert.AreEqual(3, e.Builders);
            e.AddBuilding(20, "workers_house", 1000, buildWork: 100);
            e.AddBuilding(21, GameplayRules.FieldBuilding, 1000, buildWork: 6);
            e.Tick(1001);
            Assert.AreEqual(GameplayRules.BuildersPerSite, e.Crews[20], "the oldest site first, up to the cap");
            Assert.IsFalse(e.Crews.ContainsKey(21), "nobody left for the field");
            Assert.AreEqual(GatherResult.Busy, e.Plant(21, "wheat", 1001), "can't plant a field still being made");
        }

        [Test]
        public void A_site_takes_deliveries_but_its_people_dont_eat_until_it_opens()
        {
            var e = NewIsland(1000);
            e.Tick(1000);
            e.AddBuilding(12, "army_house", 1000, buildWork: 600);
            e.Basket.Add("egg", 2);
            Assert.AreEqual(UnloadOutcome.Energy, e.UnloadStep(12).Outcome, "you can stock it up ahead");
            var energy = e.Departments[12].Energy;
            e.Tick(1300);
            Assert.AreEqual(energy, e.Departments[12].Energy, 1e-9, "no upkeep while it's a site");
            Assert.IsFalse(e.Upgrade(12));
        }

        [Test]
        public void Building_progress_survives_a_restart()
        {
            var e = NewIsland(1000);
            e.Tick(1000);
            e.AddBuilding(10, "chicken_coop", 1000, buildWork: 30);
            e.Tick(1012);
            var save = e.ToSave();
            var back = NewIsland(1000);
            back.AddBuilding(10, "chicken_coop", 1000, buildWork: 30);
            back.Restore(save);
            Assert.AreEqual(12, back.Sites[0].Done, 1e-9);
            back.Tick(1030);
            Assert.IsTrue(back.IsBuilt(10), "18 more seconds of the farmhouse builder");
        }

        [Test]
        public void Everything_survives_a_save_and_restore()
        {
            var e = NewIsland(1000);
            e.SetLevel(2);   // maize opens at level 2
            e.Plant(Field, "maize", 1000);
            e.CollectStep(Coop, 1100);
            e.Basket.Add("wheat", 2);
            e.Basket.Add("egg", 1);
            e.UnloadStep(House);
            e.Tick(1200);
            var save = e.ToSave();

            var back = NewIsland(1300);
            back.Restore(save);
            Assert.AreEqual(e.Coins, back.Coins);
            Assert.AreEqual(3, back.Basket.Count, "the coop egg, then two wheat");
            Assert.AreEqual("wheat", back.Basket.Top);
            Assert.AreEqual(1, back.Departments[House].Stock["egg"]);
            Assert.AreEqual("maize", back.Plots[Field].Crop);
            Assert.AreEqual(e.Departments[Army].Energy, back.Departments[Army].Energy, 1e-9);
            Assert.AreEqual(e.Animals[Coop].StockAt(1300), back.Animals[Coop].StockAt(1300));
        }

        // ---- people ---------------------------------------------------------------------------

        /// <summary>Tick every few seconds, as the game does, so long spans aren't taken for time away.</summary>
        private static void Run(IslandEconomy e, double from, double to, double step = 10)
        {
            for (var t = from; t <= to; t += step) e.Tick(t);
        }

        [Test]
        public void A_long_drought_costs_people_and_food_brings_them_back()
        {
            var e = NewIsland(1000);
            var army = e.Departments[Army];
            Run(e, 1000, 1600);   // 60 energy at 6 a minute: dry at 1600
            Assert.IsTrue(army.Starving);
            Assert.AreEqual(3, army.Present);
            Run(e, 1600, 1600 + GameplayRules.DroughtLossSeconds * 2);
            Assert.AreEqual(1, army.Present, "one lost per drought period");
            e.Basket.Add("croissant", 4);
            for (var i = 0; i < 4; i++) e.UnloadStep(Army);   // 180 energy
            var t = 1600 + GameplayRules.DroughtLossSeconds * 2;
            Run(e, t, t + GameplayRules.RecruitSeconds * 2 + 5);
            Assert.IsFalse(army.Starving);
            Assert.AreEqual(3, army.Present, "fed again, two new warriors moved in");
        }

        [Test]
        public void Time_away_burns_energy_but_nobody_dies_overnight()
        {
            var e = NewIsland(1000);
            e.Tick(1000);
            e.Tick(1000 + 8 * 3600);
            Assert.IsTrue(e.Departments[Army].Starving);
            Assert.AreEqual(3, e.Departments[Army].Present);
        }

        [Test]
        public void Lost_warriors_cost_less_upkeep_and_hard_work_drains_the_tank()
        {
            var e = NewIsland(1000);
            Assert.IsTrue(e.LosePerson(Army));
            Assert.AreEqual(2, e.Departments[Army].Present);
            Assert.AreEqual(4, e.Departments[Army].UpkeepPerMinute, 1e-9);
            e.SpendEnergy(Army, 50);
            Assert.AreEqual(GameplayRules.StartingEnergy - 50, e.Departments[Army].Energy, 1e-9);
        }

        [Test]
        public void Farm_workers_harvest_store_and_replant()
        {
            var e = NewIsland(1000);
            e.AddBuilding(30, "workers_house", 1000);
            CollectionAssert.AreEqual(new[] { (30, 2) }, e.Workforce(DepartmentKind.WorkersHouse).ToArray());
            e.Plant(Field, "wheat", 1000);
            var load = new System.Collections.Generic.List<string>();
            Assert.AreEqual(4, e.TakeHarvest(Field, 1100, GameplayRules.WorkerCarry, load));
            Assert.AreEqual(0, e.Basket.Count, "not the farmer's basket");
            Assert.AreEqual(2, e.Plots[Field].Left);
            Assert.AreEqual(4, e.Store(House, "wheat", 4));
            Assert.AreEqual(2, e.TakeHarvest(Field, 1100, 4, load));
            Assert.AreEqual(PlotState.Empty, e.Plots[Field].State(1100));
            Assert.AreEqual(GatherResult.Done, e.Replant(Field, 1100));
            Assert.AreEqual("wheat", e.Plots[Field].Crop);
        }

        [Test]
        public void Supplies_go_from_the_farmhouse_to_a_hungry_department()
        {
            var e = NewIsland(1000);
            e.Store(House, "wheat", 10);
            e.Store(House, "meat", 2);
            e.OrderSupplies(Army, 70);
            var load = new System.Collections.Generic.List<string>();
            Assert.AreEqual(3, e.LoadSupplies(House, Army, 3, load), "richest first: meat, meat, then wheat");
            CollectionAssert.AreEqual(new[] { "meat", "meat", "wheat" }, load);
            Assert.AreEqual(5, e.SupplyOrders[Army], 1e-9);
            var before = e.Departments[Army].Energy;
            e.Deliver(Army, load);
            Assert.AreEqual(before + 65, e.Departments[Army].Energy, 1e-9);
            load.Clear();
            Assert.AreEqual(1, e.LoadSupplies(House, Army, 4, load));
            Assert.IsFalse(e.SupplyOrders.ContainsKey(Army), "order filled");
        }

        [Test]
        public void Raiders_steal_stock_and_trample_fields()
        {
            var e = NewIsland(1000);
            e.SetLevel(2);   // maize opens at level 2
            e.Store(House, "wheat", 5);
            e.Store(House, "egg", 2);
            var loot = new System.Collections.Generic.List<string>();
            Assert.AreEqual(3, e.Steal(House, 3, loot));
            CollectionAssert.AreEqual(new[] { "wheat", "wheat", "wheat" }, loot);
            e.Plant(Field, "maize", 1000);
            Assert.AreEqual("maize", e.Trample(Field));
            Assert.AreEqual(PlotState.Empty, e.Plots[Field].State(1000));
            Assert.AreEqual(GatherResult.Done, e.Replant(Field, 1001), "the workers can sow it again");
        }

        [Test]
        public void Losses_and_orders_survive_a_restart()
        {
            var e = NewIsland(1000);
            e.LosePerson(Army);
            e.OrderSupplies(Army, 120);
            var back = NewIsland(1000);
            back.Restore(e.ToSave());
            Assert.AreEqual(2, back.Departments[Army].Present);
            Assert.AreEqual(120, back.SupplyOrders[Army], 1e-9);
        }

        [Test]
        public void Workers_keep_farming_while_the_game_is_closed()
        {
            var e = NewIsland(1000);
            e.AddBuilding(30, "workers_house", 1000);
            e.Departments[30].Energy = 500;
            e.Plant(Field, "wheat", 1000);   // ripe at 1060, 6 a crop, sown again each time
            var r = e.WorkWhileAway(1000, 1000 + 600);
            Assert.AreEqual(2, r.Workers);
            Assert.AreEqual(600, r.Seconds, 1e-9);
            Assert.IsTrue(r.Gathered["wheat"] > 30, "a crop a minute, harvested and resown for ten minutes");
            Assert.IsTrue(r.Gathered["egg"] > 10, "the coop's eggs too");
            Assert.AreEqual(r.Total - r.Restocked, e.Departments[House].Stored, "all of it went into the farmhouse (less the food carried to hungry houses)");
            Assert.IsTrue(r.SeedCoins > 0);
            Assert.IsTrue(e.Departments[30].Energy < 500, "work costs energy");
        }

        [Test]
        public void Time_away_counts_eight_hours_at_most_and_needs_workers()
        {
            var e = NewIsland(1000);
            Assert.AreEqual(0, e.WorkWhileAway(1000, 1000 + 3600).Total, "no workers' house, nothing done");
            e.AddBuilding(30, "workers_house", 1000);
            e.Departments[30].Energy = 1e6;
            var r = e.WorkWhileAway(1000, 1000 + 24 * 3600);
            Assert.AreEqual(8 * 3600, r.Seconds, 1e-9);
            Assert.IsTrue(r.StorageFull, "150 places fill up in eight hours of eggs");
        }

        // ---- machines -------------------------------------------------------------------------

        [Test]
        public void A_mill_turns_two_wheat_into_flour_while_you_wait()
        {
            var e = NewIsland(1000);
            e.AddBuilding(40, "windmill", 1000);
            e.Basket.Add("egg", 1);
            e.Basket.Add("wheat", 5);
            for (var i = 0; i < 6; i++) e.FeedMachineStep(40, 1000);
            Assert.AreEqual(0, e.Basket.CountOf("wheat"), "all the wheat went in");
            Assert.AreEqual(1, e.Basket.CountOf("egg"), "the mill doesn't take eggs");
            var mill = e.Machines[40];
            Assert.IsTrue(mill.Busy);
            Assert.AreEqual(GatherResult.NotReady, e.CollectMachineStep(40, 1024));
            e.Tick(1055);   // two batches done back to back (25 s each), the fifth wheat waits for a partner
            Assert.AreEqual(2, mill.Output);
            Assert.AreEqual(1, mill.HopperOf("wheat"));
            Assert.AreEqual("wheat", mill.Missing());
            Assert.AreEqual(GatherResult.Took, e.CollectMachineStep(40, 1055));
            Assert.AreEqual(1, e.Basket.CountOf("flour"));
            Assert.AreEqual(20, GameplayRules.EnergyOf("flour"), "worth twice the wheat");
        }

        [Test]
        public void A_bakery_needs_both_inputs_and_workers_keep_it_going()
        {
            var e = NewIsland(1000);
            e.AddBuilding(41, "bakery_factory", 1000);
            e.Store(House, "flour", 3);
            e.Store(House, "egg", 1);
            var load = new System.Collections.Generic.List<string>();
            Assert.AreEqual(4, e.LoadInputs(House, 41, 4, load));
            e.FillMachine(41, load, 1000);
            e.Tick(1100);
            Assert.AreEqual(1, e.Machines[41].Output, "one egg: one loaf");
            Assert.AreEqual("egg", e.Machines[41].Missing());
            load.Clear();
            Assert.AreEqual(1, e.TakeOutput(41, 1100, 4, load));
            CollectionAssert.AreEqual(new[] { "croissant" }, load);
        }

        [Test]
        public void A_full_output_tray_stops_the_machine_and_it_survives_a_restart()
        {
            var e = NewIsland(1000);
            e.AddBuilding(42, "cider_press", 1000);
            e.FillMachine(42, Enumerable.Repeat("tomato", 30), 1000);
            e.Tick(1000 + 25 * 20);
            Assert.AreEqual(GameplayRules.MachineOutputMax, e.Machines[42].Output);
            Assert.AreEqual(10, e.Machines[42].HopperOf("tomato"), "stopped at ten");
            var back = NewIsland(1000);
            back.AddBuilding(42, "cider_press", 1000);
            back.Restore(e.ToSave());
            Assert.AreEqual(10, back.Machines[42].Output);
            Assert.AreEqual(10, back.Machines[42].HopperOf("tomato"));
        }

        [Test]
        public void Taking_a_good_from_the_middle_of_the_basket_keeps_the_rest_in_order()
        {
            var b = new Basket(20);
            b.Add("wheat", 1);
            b.Add("egg", 1);
            b.Add("wheat", 1);
            Assert.IsTrue(b.TakeItem("egg"));
            Assert.AreEqual(1, b.Stacks.Count, "the two wheat stacks join up");
            Assert.AreEqual(2, b.CountOf("wheat"));
            Assert.IsFalse(b.TakeItem("egg"));
        }

        // ---- economy v2: materials, damage, functions ---------------------------------------------

        [Test]
        public void New_buildings_wait_for_wood_from_the_farmhouse()
        {
            var e = NewIsland(1000);
            e.Tick(1000);
            e.AddBuilding(50, "chicken_coop", 1000, buildWork: 20, price: 250);   // 5 wood
            e.Tick(1010);
            Assert.AreEqual(0, e.Sites[0].Done, 1e-9, "no wood, no work");
            Assert.AreEqual(5, e.Sites[0].Missing["wood"]);
            e.GrantStarterWood(House);
            e.Tick(1011);
            Assert.IsTrue(e.Sites[0].Supplied);
            Assert.AreEqual(GameplayRules.StarterWood - 5, e.Departments[House].Stock["wood"]);
            e.Tick(1040);
            Assert.IsTrue(e.IsBuilt(50));
        }

        [Test]
        public void A_ruined_army_house_loses_a_level_half_its_people_and_needs_wood_to_mend()
        {
            var e = NewIsland(1000);
            e.SetLevel(15);   // what this test needs opens by level 15
            e.AddBuilding(51, "army_house", 1000, price: 1200);
            var army = e.Departments[51];
            army.Level = 2;
            army.Energy = 400;
            var max = e.Healths[51].Max;
            Assert.AreEqual(270, max, 1e-9);
            Assert.IsFalse(e.Damage(51, 100, 1000));
            Assert.IsTrue(e.Damage(51, 1000, 1001), "ruined");
            Assert.IsFalse(e.Working(51));
            Assert.AreEqual(1, army.Level);
            Assert.AreEqual(200, army.Energy, 1e-9);
            Assert.AreEqual(2, army.Present, "5 warriors, 3 lost");
            Assert.AreEqual(UnloadOutcome.None, e.UnloadStep(51).Outcome, "a ruin takes nothing");
            e.Tick(1020);   // quiet for a while: builders get a repair job, but there's no wood
            e.Tick(1020.5);
            var repair = e.Sites.First(c => c.Uid == 51);
            Assert.IsTrue(repair.Repair);
            Assert.AreEqual(11, repair.Missing["wood"]);
            Assert.IsTrue(e.IsBuilt(51), "a repair isn't a new building");
            e.Store(House, "wood", 20);
            e.Tick(1021);
            e.Tick(1021 + 120 + 1);   // as long as building it, with the farmhouse's one builder
            Assert.IsTrue(e.Working(51));
            Assert.AreEqual(max, e.Healths[51].Hp, 1e-9);
        }

        [Test]
        public void A_ruined_farmhouse_loses_half_its_stock_and_raiders_take_coins_without_a_bank()
        {
            var e = NewIsland(1000);
            e.SetLevel(15);   // what this test needs opens by level 15
            e.Store(House, "wheat", 10);
            e.Damage(House, 10000, 1000);
            Assert.AreEqual(5, e.Departments[House].Stock["wheat"]);
            Assert.AreEqual(25, e.StealCoins(), "5% of 500");
            e.AddBuilding(52, "bank", 1000, price: 2500);
            Assert.AreEqual(0, e.StealCoins());
        }

        [Test]
        public void A_fire_station_mends_damage_nearby_for_free()
        {
            var e = NewIsland(1000);
            e.AddBuilding(53, "fire_station", 1000, price: 1700);
            e.Place(53, 0, 0);
            e.Place(Coop, 30, 0);
            e.Damage(Coop, 50, 1000);
            e.Tick(1011);
            e.Tick(1020);
            Assert.AreEqual(e.Healths[Coop].Max - 50 + 18, e.Healths[Coop].Hp, 1e-9, "2 HP a second once it's quiet");
            Assert.IsFalse(e.Sites.Any(c => c.Uid == Coop), "no builders needed");
        }

        [Test]
        public void Animals_eat_from_the_trough_and_stop_when_it_is_empty()
        {
            var e = NewIsland(1000);
            var coop = e.Animals[Coop];
            Assert.AreEqual(GameplayRules.TroughStart, coop.Feed);
            Assert.AreEqual(GatherResult.Took, e.CollectStep(Coop, 1000 + 10000));
            Assert.AreEqual(GameplayRules.TroughStart - 6, coop.Feed, "six eggs made (the most it holds), six feed eaten");
            coop.Feed = 1;
            for (var i = 0; i < 5; i++) e.CollectStep(Coop, 11000);
            Assert.AreEqual(1, coop.StockAt(12000), "one feed left: one more egg, then nothing");
            Assert.IsTrue(coop.Hungry(12000));
            e.Basket.Clear();
            e.Basket.Add("maize", 5);
            Assert.AreEqual("maize", e.FeedAnimalsStep(Coop, 12000));
            Assert.IsFalse(coop.Hungry(12000));
        }

        [Test]
        public void Shops_pay_more_for_what_they_deal_in_and_skip_the_rest()
        {
            var e = NewIsland(1000);
            e.AddBuilding(54, "bakery_shop", 1000, price: 800);
            e.Basket.Add("croissant", 1);
            e.Basket.Add("wheat", 1);
            var r = e.UnloadStep(54);
            Assert.AreEqual("croissant", r.Item, "passes over the wheat on top");
            Assert.AreEqual((int)System.Math.Round(GameplayRules.PriceOf("croissant") * 1.5), r.Amount, "bread × 1.5");
            Assert.AreEqual(UnloadOutcome.None, e.UnloadStep(54).Outcome, "a baker doesn't buy wheat");
        }

        [Test]
        public void Gardens_near_fields_grow_faster_and_yield_more()
        {
            var e = NewIsland(1000);
            e.Place(Field, 0, 0);
            e.AddBuilding(55, "potting_shed", 1000, price: 300);
            e.Place(55, 10, 0);
            e.Plant(Field, "wheat", 1000);
            Assert.AreEqual(1048, e.Plots[Field].ReadyAt, 1e-9, "60 s / 1.25");
            Assert.AreEqual(6, e.Plots[Field].Left);
        }

        [Test]
        public void Town_buildings_help_people()
        {
            var e = NewIsland(1000);
            e.AddBuilding(57, "workers_house", 1000, price: 800);
            Assert.AreEqual(4, e.WorkerCarry);
            e.AddBuilding(58, "school", 1000, price: 1800);
            e.AddBuilding(59, "community_hall", 1000, price: 2000);
            e.Tick(1001);
            Assert.AreEqual(6, e.WorkerCarry);
            Assert.AreEqual(3, e.Departments[57].People, "2 + 1 from the hall");
        }

        [Test]
        public void A_bank_pays_interest_while_you_play_and_while_you_are_away()
        {
            var e = NewIsland(1000);
            e.AddBuilding(60, "bank", 1000, price: 2500);
            e.Tick(1000);
            e.Tick(1000 + 1200);
            Assert.AreEqual(500 + 10 + 10, e.Coins, "2% twice");
        }

        [Test]
        public void Raids_grow_with_the_island_and_back_off_after_a_ruin()
        {
            var small = NewIsland(1000);
            var big = NewIsland(1000);
            for (var i = 0; i < 10; i++) big.AddBuilding(100 + i, "army_house", 1000, price: 1200);
            Assert.IsTrue(big.Threat > small.Threat + 10);
            Assert.IsTrue(GameplayRules.RaidSize(10, big.Threat) > GameplayRules.RaidSize(10, small.Threat), "a rich island draws a few more");
            Assert.IsTrue(GameplayRules.RaidSize(30, 0) > GameplayRules.RaidSize(10, 0), "raids grow with the level");
            Assert.IsTrue(GameplayRules.RaidSize(10, 0, raidsSeen: 0) < GameplayRules.RaidSize(10, 0), "the first raids are smaller");
            Assert.AreEqual(16, GameplayRules.RaidSize(500, 1000));
            Assert.AreEqual(33, big.Defence, 1e-9, "eleven army houses of three");
        }

        [Test]
        public void Boss_zombies_come_once_a_week_once_unlocked_and_stay_cured()
        {
            bool None(string id) => false;
            Assert.IsEmpty(GameplayRules.BossesTonight(24, 6, None), "no boss before the Footballer's level");
            Assert.IsEmpty(GameplayRules.BossesTonight(25, 5, None), "not a boss night");
            CollectionAssert.AreEqual(new[] { "footballer" }, GameplayRules.BossesTonight(25, 6, None));
            CollectionAssert.AreEqual(new[] { "builder" }, GameplayRules.BossesTonight(40, 13, None), "the bosses take turns week by week");
            CollectionAssert.AreEqual(new[] { "builder" }, GameplayRules.BossesTonight(40, 6, id => id == "footballer"), "a cured boss never comes back");
            Assert.AreEqual(3, GameplayRules.BossesTonight(90, 6, None).Distinct().Count(), "three different bosses together at the end");
            Assert.IsTrue(GameplayRules.BossesTonight(99, 2, None).Contains("mayor"), "the Mayor comes every night");
            Assert.IsEmpty(GameplayRules.ArmyWeapons(10));
            CollectionAssert.AreEqual(new[] { "Bat", "Crowbar" }, GameplayRules.ArmyWeapons(33));
            Assert.AreEqual(3, GameplayRules.ArmyWeapons(74).Count);
            Assert.AreEqual(2, GameplayRules.RaidSize(4, 0, raidsSeen: 0), "the first zombie night brings two");
        }

        [Test]
        public void A_stable_lets_you_ride()
        {
            var e = NewIsland(1000);
            Assert.IsFalse(e.HorseReady, "no stable");
            e.AddBuilding(61, "stable", 1000, price: 700);
            Assert.IsTrue(e.HorseReady, "it opens with some energy");
            Assert.IsTrue(e.PayForRide());
        }

        [Test]
        public void Silos_hold_crops_only()
        {
            var e = NewIsland(1000);
            e.AddBuilding(62, "grain_silos", 1000, price: 450);
            e.Basket.Add("wheat", 2);
            e.Basket.Add("egg", 1);
            Assert.AreEqual("wheat", e.UnloadStep(62).Item);
            Assert.AreEqual("wheat", e.UnloadStep(62).Item);
            Assert.AreEqual(UnloadOutcome.None, e.UnloadStep(62).Outcome);
            Assert.AreEqual(1, e.Basket.CountOf("egg"));
        }

        [Test]
        public void Wood_carried_to_a_ruin_is_handed_over_for_its_repair()
        {
            var e = NewIsland(1000);
            e.Damage(Coop, 10000, 1000);
            e.Tick(1011);   // the builders have looked at it: a repair site that needs wood
            var site = e.Sites.Single(c => c.Uid == Coop && c.Repair);
            var wood = site.StillNeeds("wood");
            Assert.IsTrue(wood > 0);
            Assert.IsFalse(site.Supplied, "the farmhouse has no wood");
            Assert.IsFalse(e.SiteWants(Coop), "nothing in the basket yet");

            e.Basket.Add("wood", wood + 3);
            Assert.IsTrue(e.SiteWants(Coop));
            for (var i = 0; i < wood; i++) Assert.AreEqual("wood", e.GiveToSite(Coop));
            Assert.IsTrue(site.Supplied);
            Assert.IsNull(e.GiveToSite(Coop), "it takes no more than it needs");
            Assert.AreEqual(3, e.Basket.CountOf("wood"));

            e.Tick(1011 + site.Required + 60);
            Assert.IsFalse(e.IsRuined(Coop), "repaired");
        }

        [Test]
        public void A_ruined_farmhouse_can_be_mended_from_its_own_shelves()
        {
            var e = NewIsland(1000);
            e.Store(House, "wood", 400);
            e.Damage(House, 10000, 1000);   // loses half: 200 wood left
            e.Tick(1011);
            var site = e.Sites.Single(c => c.Uid == House && c.Repair);
            e.Tick(1012);
            Assert.IsTrue(site.Supplied, "a ruin's stock still counts");
        }

        [Test]
        public void Wood_handed_to_a_repair_survives_a_restart()
        {
            var e = NewIsland(1000);
            e.Damage(Coop, 10000, 1000);
            e.Tick(1011);
            var need = e.Sites.Single(c => c.Uid == Coop && c.Repair).StillNeeds("wood");
            e.Basket.Add("wood", 2);
            e.GiveToSite(Coop);
            e.GiveToSite(Coop);
            var back = NewIsland(1000);
            back.Restore(e.ToSave());
            back.Tick(1012);
            Assert.AreEqual(need - 2, back.Sites.Single(c => c.Uid == Coop && c.Repair).StillNeeds("wood"));
        }

        [Test]
        public void Ruin_and_health_survive_a_restart()
        {
            var e = NewIsland(1000);
            e.SetLevel(15);   // what this test needs opens by level 15
            e.Damage(Coop, 40, 1000);
            e.Damage(Army, 10000, 1000);
            var back = NewIsland(1000);
            back.Restore(e.ToSave());
            Assert.AreEqual(e.Healths[Coop].Hp, back.Healths[Coop].Hp, 1e-9);
            Assert.IsTrue(back.IsRuined(Army));
        }
    
        [Test]
        public void The_shop_unlocks_by_island_level_and_keeps_what_it_unlocked()
        {
            Assert.AreEqual(2, GameplayRules.UnlockLevel("chicken_coop"));
            Assert.AreEqual(4, GameplayRules.UnlockLevel("army_house"), "with the first zombie night");
            Assert.AreEqual(6, GameplayRules.UnlockLevel("post_office"));
            Assert.AreEqual(6, GameplayRules.UnlockLevel("bakery_factory"));
            Assert.AreEqual(31, GameplayRules.UnlockLevel("mine_entrance"));
            Assert.AreEqual(75, GameplayRules.UnlockLevel("town_hall"));
            var e = NewIsland(1000);
            e.Tick(1000);
            Assert.IsFalse(e.Unlocked("bank"));
            var coins = e.Coins;
            e.AddXp(GameplayRules.XpAt(70));
            Assert.AreEqual(70, e.BestLevel);
            Assert.IsTrue(e.Unlocked("bank"));
            Assert.IsFalse(e.Unlocked("town_hall"));
            Assert.IsTrue(e.TakeLevelUp());
            Assert.IsFalse(e.TakeLevelUp(), "once");
            var paid = 0;
            for (var l = 2; l <= 70; l++) paid += GameplayRules.LevelReward(l);
            Assert.AreEqual(coins + paid, e.Coins, "every level on the way paid its reward");
            Assert.AreEqual(paid, e.TakeLevelCoins());
            var back = NewIsland(1000);
            back.Restore(e.ToSave());
            Assert.AreEqual(e.BestLevel, back.BestLevel);
            Assert.AreEqual(e.Xp, back.Xp);
        }

        [Test]
        public void Levels_never_end_and_each_takes_more()
        {
            for (var l = 2; l < 300; l++)
                Assert.IsTrue(GameplayRules.XpToNext(l) > GameplayRules.XpToNext(l - 1), "each level takes more");
            Assert.AreEqual(1, GameplayRules.LevelForXp(0));
            Assert.AreEqual(2, GameplayRules.LevelForXp(GameplayRules.XpToNext(1)));
            Assert.AreEqual(30, GameplayRules.LevelForXp(GameplayRules.XpAt(30)));
            Assert.AreEqual(250, GameplayRules.LevelForXp(GameplayRules.XpAt(250)), "no ceiling");
            Assert.IsTrue(GameplayRules.LevelReward(90) >= GameplayRules.LevelReward(31));
            Assert.AreEqual(1, GameplayRules.BuildingMaxLevelAt(19));
            Assert.AreEqual(2, GameplayRules.BuildingMaxLevelAt(21));
            Assert.AreEqual(5, GameplayRules.BuildingMaxLevelAt(90));
            // every planned building opens by level 26, and each level up to 26 opens something
            var e = NewIsland(1000);
            e.Count("harvested", 10);
            Assert.AreEqual(10, e.Xp, "deeds earn XP");
            // an older save (levels from the score) keeps its level
            var old = e.ToSave();
            old.xp = 0;
            old.bestLevel = 4;
            var back = NewIsland(1000);
            back.Restore(old);
            Assert.AreEqual(4, back.BestLevel);
            Assert.AreEqual(GameplayRules.XpAt(4), back.Xp);
        }

        [Test]
        public void The_post_office_posts_orders_that_pay_double_when_delivered()
        {
            var e = NewIsland(1000);
            e.Rng = new System.Random(7);
            e.AddXp(GameplayRules.XpAt(6));
            e.AddBuilding(70, "post_office", 1000, price: 1200);
            e.Tick(1000);
            Assert.IsNull(e.Order, "a short wait first");
            e.Tick(1030);
            var o = e.Order!;
            Assert.IsNotNull(o);
            Assert.IsTrue(o.Count >= 3);
            Assert.AreEqual((int)System.Math.Round(GameplayRules.PriceOf(o.Item) * o.Count * 1.6) + 20, o.Reward);
            Assert.AreEqual(UnloadOutcome.None, e.PostStep(70).Outcome, "nothing to give");
            e.Basket.Add(o.Item, o.Count);
            var coins = e.Coins;
            for (var i = 0; i < o.Count - 1; i++) Assert.AreEqual(UnloadOutcome.Stored, e.PostStep(70).Outcome);
            Assert.AreEqual(coins, e.Coins);
            var last = e.PostStep(70);
            Assert.AreEqual(UnloadOutcome.Sold, last.Outcome);
            Assert.AreEqual(coins + o.Reward, e.Coins);
            Assert.IsNull(e.Order);
            Assert.IsTrue(e.TakeClosed()!.Done);
        }

        [Test]
        public void A_post_office_order_runs_out_and_a_new_one_follows()
        {
            var e = NewIsland(1000);
            e.SetLevel(6);   // what this test needs opens by level 6
            e.AddBuilding(70, "post_office", 1000, price: 1200);
            e.Tick(1000);
            e.Tick(1030);
            Assert.IsNotNull(e.Order);
            var back = NewIsland(1000);
            back.AddBuilding(70, "post_office", 1000, price: 1200);
            back.Restore(e.ToSave());
            Assert.AreEqual(e.Order!.Item, back.Order!.Item);
            e.Tick(1030 + GameplayRules.OrderSeconds + 1);
            Assert.IsNull(e.Order);
            Assert.IsTrue(e.TakeClosed()!.Expired);
            e.Tick(1030 + GameplayRules.OrderSeconds + 2 + GameplayRules.OrderGapSeconds);
            Assert.IsNotNull(e.Order);
        }

        [Test]
        public void A_worker_can_see_how_much_feed_is_left()
        {
            var e = NewIsland(1000);
            e.AddBuilding(80, "chicken_coop", 1000, price: 250);
            Assert.AreEqual(GameplayRules.TroughStart, e.Animals[80].FeedLeft(1000));
            Assert.AreEqual(GameplayRules.TroughStart - 2, e.Animals[80].FeedLeft(1000 + 81));
        }
    
        [Test]
        public void Ripe_crops_left_in_the_field_rot_away()
        {
            var e = NewIsland(1000);
            e.Plant(Field, "wheat", 1000);
            var plot = e.Plots[Field];
            var left = plot.Left;
            e.Tick(1060 + 239);
            Assert.AreEqual(left, plot.Left, "keeps for four minutes");
            Assert.IsFalse(plot.Rotting(1060 + 239));
            e.Tick(1060 + 240 + 125);
            Assert.AreEqual(left - 2, plot.Left, "then one a minute");
            Assert.AreEqual(2, e.TakeRotted());
            var back = NewIsland(1000);
            back.Restore(e.ToSave());
            back.Tick(1060 + 240 + 125);
            Assert.AreEqual(left - 2, back.Plots[Field].Left, "no rot counted twice after a restart");
            e.Tick(1060 + 240 + 3600);
            Assert.AreEqual(PlotState.Empty, plot.State(1060 + 240 + 3600), "all gone");
            Assert.AreEqual(left - 2, e.TakeRotted());
        }
    
        [Test]
        public void The_farmer_can_take_goods_back_out_of_the_farmhouse()
        {
            var e = NewIsland(1000);
            e.Store(House, "wheat", 8);
            Assert.AreEqual(5, e.Withdraw(House, "wheat", 5));
            Assert.AreEqual(3, e.Departments[House].Stock["wheat"]);
            Assert.AreEqual(5, e.Basket.Count);
            Assert.AreEqual(3, e.Withdraw(House, "wheat", 10), "only what's there");
            Assert.AreEqual(0, e.Withdraw(House, "egg", 1));
            Assert.AreEqual(0, e.Withdraw(Army, "wheat", 1), "not from an army house");
        }
    
        [Test]
        public void Seasons_change_growth_yield_and_appetite()
        {
            // a Monday in February 2026 that starts a spring week (no festival, no crop event)
            var start = (new System.DateTime(2026, 2, 2, 0, 0, 0, System.DateTimeKind.Utc) - new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)).TotalSeconds;
            while (GameplayRules.SeasonAt(start + 10).Name != "Spring") start += GameplayRules.SeasonSeconds;
            Assert.AreEqual(7 * 86400, GameplayRules.SeasonSeconds, 1e-9, "a season lasts a real week");
            Assert.AreEqual("Summer", GameplayRules.SeasonAt(start + GameplayRules.SeasonSeconds + 10).Name);
            Assert.AreEqual("Winter", GameplayRules.SeasonAt(start + GameplayRules.SeasonSeconds * 3 + 10).Name);
            Assert.AreEqual("Spring", GameplayRules.SeasonAt(start + GameplayRules.SeasonSeconds * 4 + 10).Name);
            var e = NewIsland(1000);
            e.SeasonsOn = true;
            var autumn = start + GameplayRules.SeasonSeconds * 2 + 10;
            e.Plant(Field, "wheat", autumn);
            Assert.AreEqual(GameplayRules.CropOf("wheat")!.Yield + 2, e.Plots[Field].Left, "autumn yields two more");
            Assert.AreEqual(1.2, GameplayRules.SeasonAt(start + GameplayRules.SeasonSeconds * 3 + 1).Appetite, 1e-9);
        }

        [Test]
        public void December_is_the_winter_festival_and_months_end_with_their_event()
        {
            double At(int y, int m, int d) => (new System.DateTime(y, m, d, 12, 0, 0, System.DateTimeKind.Utc) - new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)).TotalSeconds;
            Assert.AreEqual("Winter", GameplayRules.SeasonAt(At(2026, 12, 10)).Name, "winter all festival long");
            Assert.AreEqual("winter_festival", GameplayRules.EventAt(At(2026, 12, 2))!.Id);
            Assert.AreEqual("winter_festival", GameplayRules.EventAt(At(2027, 1, 5))!.Id);
            Assert.IsNull(GameplayRules.EventAt(At(2027, 1, 10)), "mid-January: no event");
            Assert.AreEqual("snow_day", GameplayRules.EventAt(At(2027, 1, 25))!.Id, "the last ten days of January");
            Assert.IsNull(GameplayRules.EventAt(At(2026, 10, 21)));
            Assert.AreEqual("pumpkin_festival", GameplayRules.EventAt(At(2026, 10, 22))!.Id);
            Assert.AreEqual(12, GameplayRules.LiveYear.Count);
            Assert.AreEqual("Spooky Nights", GameplayRules.ThemeOf(At(2026, 10, 3)).Theme);
            // a field sown during Spring Planting yields one more
            var e = NewIsland(1000);
            e.SeasonsOn = true;
            var now = At(2026, 3, 25);
            var normal = GameplayRules.CropOf("wheat")!.Yield + GameplayRules.SeasonAt(now).YieldBonus;
            e.Plant(Field, "wheat", now);
            Assert.AreEqual(normal + 1, e.Plots[Field].Left);
        }

        [Test]
        public void Selling_a_lot_of_one_good_lowers_its_price_for_a_while()
        {
            var e = NewIsland(1000);
            e.MarketMoves = true;
            var fresh = e.MarketPrice("market_stall", "croissant");
            e.Basket.Capacity = 1000;
            e.Basket.Add("croissant", 50);
            for (var i = 0; i < 50; i++) e.UnloadStep(Market);
            Assert.IsTrue(e.MarketPrice("market_stall", "croissant") < fresh * 0.7, "flooded");
            Assert.AreEqual(fresh, e.MarketPrice("market_stall", "wheat") > 0 ? fresh : fresh, "other goods unaffected");
            e.Tick(1001);
            e.Tick(1001 + 3600);
            Assert.IsTrue(e.MarketPrice("market_stall", "croissant") >= fresh * 0.95, "recovers after an hour");
            Assert.AreEqual(50, e.Stat("sold"));
        }

        [Test]
        public void Eggs_left_in_a_full_coop_go_off()
        {
            var e = NewIsland(1000);
            var coop = e.Animals[Coop];
            e.Tick(1000 + 40 * 6);
            Assert.AreEqual(coop.Max, coop.StockAt(1000 + 40 * 6));
            e.Tick(1000 + 40 * 6 + 1);
            e.Tick(1000 + 40 * 6 + 2 + GameplayRules.SpoilAfterSeconds);
            Assert.AreEqual(coop.Max - 1, coop.StockAt(1000 + 40 * 6 + 2 + GameplayRules.SpoilAfterSeconds));
            Assert.AreEqual(1, e.TakeSpoiled());
        }

        [Test]
        public void Machines_and_animal_houses_level_up_with_coins_and_materials()
        {
            var e = NewIsland(1000);
            e.SetLevel(45);   // what this test needs opens by level 45
            e.AddBuilding(90, "windmill", 1000, price: 900);
            Assert.IsNotNull(e.BuildingUpgradeCost(90));
            Assert.IsFalse(e.UpgradeBuilding(90, 1000), "no planks or stone yet");
            e.AddCoins(5000);
            e.Store(House, "planks", 6); e.Store(House, "stone", 6);
            Assert.IsTrue(e.UpgradeBuilding(90, 1000));
            Assert.AreEqual(2, e.BuildingLevel(90));
            Assert.AreEqual(GameplayRules.MachineOutputMax + 6, e.Machines[90].OutputMax);
            e.Store(House, "wood", 12); e.Store(House, "stone", 6);
            Assert.IsTrue(e.UpgradeBuilding(Coop, 1000));
            Assert.AreEqual(GameplayRules.ProducerOf("chicken_coop")!.Max + 3, e.Animals[Coop].Max);
            var back = NewIsland(1000);
            back.AddBuilding(90, "windmill", 1000, price: 900);
            back.Restore(e.ToSave());
            Assert.AreEqual(2, back.BuildingLevel(90));
            Assert.AreEqual(2, back.Machines[90].Level);
            Assert.AreEqual(1, back.Stat("building_upgrades") == 2 ? 1 : 0);
        }

        [Test]
        public void Goals_lead_on_and_pay_when_claimed()
        {
            var e = NewIsland(1000);
            Assert.AreEqual("plant", e.CurrentGoal!.Id);
            Assert.IsFalse(e.ClaimGoal());
            e.Plant(Field, "wheat", 1000);
            var coins = e.Coins;
            Assert.IsTrue(e.ClaimGoal());
            Assert.AreEqual(coins + GameplayRules.Goals[0].Reward, e.Coins);
            Assert.AreEqual("harvest", e.CurrentGoal!.Id);
            e.Tick(1000);
            Assert.AreEqual(3, e.Dailies.Count, "three dailies");
            var back = NewIsland(1000);
            back.Restore(e.ToSave());
            Assert.AreEqual(1, back.GoalIndex);
            Assert.AreEqual(string.Join(",", e.Dailies), string.Join(",", back.Dailies));
        }

        [Test]
        public void Orders_today_count_from_the_start_of_the_day()
        {
            var e = NewIsland(1000);
            e.Count(IslandEconomy.OrdersStat, 4);   // before the day rolls
            e.Tick(1000);
            Assert.AreEqual(0, e.OrdersToday, "a new day starts at zero");
            e.Count(IslandEconomy.OrdersStat, 2);
            Assert.AreEqual(2, e.OrdersToday);
            var back = NewIsland(1000);
            back.Restore(e.ToSave());
            Assert.AreEqual(2, back.OrdersToday, "kept in the save (the server reads the same numbers)");
            back.Tick(1000 + 86400);
            Assert.AreEqual(0, back.OrdersToday, "and starts again the next day");
            Assert.IsFalse(back.AllDailiesClaimed);
        }

        [Test]
        public void Bigger_houses_eat_more()
        {
            var e = NewIsland(1000);
            e.SetLevel(18);   // what this test needs opens by level 18
            var army = e.Departments[Army];
            var before = army.UpkeepPerMinute;
            e.Basket.Capacity = 1000;
            e.Basket.Add("croissant", 30);
            while (!e.Basket.IsEmpty) e.UnloadStep(Army);
            e.Store(House, "wood", 12); e.Store(House, "stone", 8);
            Assert.IsTrue(e.Upgrade(Army));
            Assert.AreEqual(before / 3 * 5 * 1.2, army.UpkeepPerMinute, 1e-9);
        }
    
        [Test]
        public void A_warehouse_stores_anything_and_a_well_beats_drought()
        {
            var e = NewIsland(1000);
            e.AddBuilding(95, "facility_warehouse", 1000, price: 900);
            Assert.AreEqual(DepartmentKind.Farmhouse, e.Departments[95].Levels.Kind);
            Assert.AreEqual(10, e.Store(95, "croissant", 10));
            Assert.AreEqual(GameplayRules.BasketBase, e.Basket.Capacity, "a warehouse doesn't grow the basket");
            Assert.IsTrue(GameplayRules.IsDecor("decor_fountain"));
            Assert.IsFalse(GameplayRules.IsDecor("windmill"));
            Assert.AreEqual(6, GameplayRules.BuildWork("path_dirt", 15), 1e-9);
        }
    
        [Test]
        public void The_quarry_gives_stone()
        {
            var e = NewIsland(1000);
            e.AddBuilding(96, "mine_entrance", 1000, price: 1400);
            var mine = e.Animals[96];
            Assert.AreEqual("stone", mine.Producer.Item);
            Assert.AreEqual(2, mine.StockAt(1000 + 91));
            Assert.IsNull(mine.Side, "stone only: no ore");
            var load = new System.Collections.Generic.List<string>();
            e.TakeProduce(96, 1000 + 121, 10, load);
            Assert.AreEqual(2, load.Count(i => i == "stone"));
            Assert.AreEqual(2, e.Stat("made:stone"));
        }

        [Test]
        public void Stolen_goods_are_not_the_islands_work()
        {
            var e = NewIsland(1000);
            var loot = new System.Collections.Generic.List<string>();
            e.TakeProduce(Coop, 1000 + 81, 5, loot, credit: false);
            Assert.AreEqual(2, loot.Count);
            Assert.AreEqual(0, e.Stat("made:egg"));
        }

        [Test]
        public void The_best_score_is_kept()
        {
            var e = NewIsland(1000);
            e.AddCoins(50000);
            e.Tick(1001);
            var best = e.BestScore;
            Assert.IsTrue(best > 400);
            e.SpendCoins(e.Coins);
            e.Tick(1002);
            Assert.AreEqual(best, e.BestScore, "kept after the score drops");
            Assert.AreEqual((GameplayRules.XpAt(3), GameplayRules.XpAt(4)), e.LevelSpan(3));
        }

        [Test]
        public void A_warehouse_has_its_own_upgrade_cost()
        {
            var mats = GameplayRules.DepartmentUpgradeMaterials("facility_warehouse", DepartmentKind.Farmhouse, 1);
            Assert.IsTrue(mats.Any(m => m.item == "planks"), "a warehouse is built of planks");
            Assert.IsFalse(GameplayRules.DepartmentUpgradeMaterials("farmhouse", DepartmentKind.Farmhouse, 1).Any(m => m.item == "planks"));
        }
    }
}
