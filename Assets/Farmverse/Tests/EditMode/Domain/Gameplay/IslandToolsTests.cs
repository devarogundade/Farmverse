#nullable enable
using System.Linq;
using Farmverse.Domain.Gameplay;
using NUnit.Framework;

namespace Farmverse.Tests.Domain.Gameplay
{
    /// <summary>Drag-and-drop tools used on buildings from anywhere (seeds, sickle, collect, feed, inputs).</summary>
    [TestFixture]
    public sealed class IslandToolsTests
    {
        private const double T0 = 1_800_000_000;
        private const int House = 1, Field = 2, Coop = 3, Mill = 4;

        private static IslandEconomy NewIsland(int wheatInStore = 0)
        {
            var state = new IslandState
            {
                simAt = T0,
                seed = 42,
                buildings =
                {
                    new IslandState.Placed { id = "farmhouse", uid = House, x = 150, z = 150, w = 4, d = 4 },
                    new IslandState.Placed { id = GameplayRules.FieldBuilding, uid = Field, x = 156, z = 150, w = 3, d = 3 },
                    new IslandState.Placed { id = "chicken_coop", uid = Coop, x = 150, z = 156, w = 3, d = 3 },
                    new IslandState.Placed { id = "windmill", uid = Mill, x = 160, z = 156, w = 3, d = 3 },
                },
            };
            var e = IslandSim.Load(state);
            if (wheatInStore > 0) e.GiveToFarmhouses("wheat", wheatInStore);
            return e;
        }

        [Test]
        public void An_empty_field_offers_its_seeds_and_takes_a_seed()
        {
            var e = NewIsland();
            Assert.IsTrue(e.ToolsFor(Field, T0).Contains(Tool.Seed("wheat")));
            Assert.AreEqual(ToolRefusal.Ok, e.CanUse(Field, Tool.Seed("wheat"), T0));
            var coins = e.Coins;
            var use = e.Use(Field, Tool.Seed("wheat"), T0);
            Assert.IsTrue(use.Ok);
            Assert.AreEqual(PlotState.Growing, e.Plots[Field].State(T0 + 1));
            Assert.IsTrue(e.Coins <= coins, "seeds cost coins (or are free)");
        }

        [Test]
        public void A_growing_field_refuses_more_seed_and_a_coop_refuses_seed()
        {
            var e = NewIsland();
            e.Use(Field, Tool.Seed("wheat"), T0);
            Assert.AreEqual(ToolRefusal.NotReady, e.CanUse(Field, Tool.Seed("wheat"), T0 + 1));
            Assert.AreEqual(ToolRefusal.WrongTarget, e.CanUse(Coop, Tool.Seed("wheat"), T0));
        }

        [Test]
        public void The_sickle_harvests_the_whole_field_into_the_farmhouse()
        {
            var e = NewIsland();
            e.Use(Field, Tool.Seed("wheat"), T0);
            var ripe = e.Plots[Field].ReadyAt + 1;
            Assert.AreEqual(ToolRefusal.NotReady, e.CanUse(Field, Tool.Sickle, T0 + 1));
            Assert.IsTrue(e.ToolsFor(Field, ripe).Contains(Tool.Sickle));
            var expected = e.Plots[Field].Left;
            var use = e.Use(Field, Tool.Sickle, ripe);
            Assert.IsTrue(use.Ok);
            Assert.AreEqual("wheat", use.Item);
            Assert.AreEqual(expected, use.Units);
            Assert.AreEqual(expected, e.StoredInFarmhouses("wheat"));
            Assert.AreEqual(PlotState.Empty, e.Plots[Field].State(ripe));
        }

        [Test]
        public void Feed_comes_from_the_farmhouse_and_stops_at_a_full_trough()
        {
            var e = NewIsland(wheatInStore: 100);
            var room = GameplayRules.TroughSize - e.Animals[Coop].Feed;
            Assert.IsTrue(e.ToolsFor(Coop, T0).Contains(Tool.FeedWith("wheat")));
            var use = e.Use(Coop, Tool.FeedWith("wheat"), T0);
            Assert.IsTrue(use.Ok);
            Assert.AreEqual(room, use.Units);   // wheat is worth 1 feed
            Assert.AreEqual(100 - room, e.StoredInFarmhouses("wheat"));
            Assert.AreEqual(ToolRefusal.Full, e.CanUse(Coop, Tool.FeedWith("wheat"), T0));
        }

        [Test]
        public void Feed_needs_stock_and_must_be_something_animals_eat()
        {
            var e = NewIsland();
            Assert.AreEqual(ToolRefusal.NoStock, e.CanUse(Coop, Tool.FeedWith("wheat"), T0));
            Assert.AreEqual(ToolRefusal.WrongTarget, e.CanUse(Coop, Tool.FeedWith("egg"), T0));
        }

        [Test]
        public void The_hand_collects_eggs_into_the_farmhouse()
        {
            var e = NewIsland();
            Assert.AreEqual(ToolRefusal.NotReady, e.CanUse(Coop, Tool.Hand, T0));
            var later = T0 + 3600;
            var made = e.Animals[Coop].StockAt(later);
            Assert.IsTrue(made > 0, "the hens laid within the hour");
            var use = e.Use(Coop, Tool.Hand, later);
            Assert.IsTrue(use.Ok);
            Assert.AreEqual("egg", use.Item);
            Assert.AreEqual(made, use.Units);
            Assert.AreEqual(made, e.StoredInFarmhouses("egg"));
            Assert.AreEqual(0, e.Animals[Coop].StockAt(later));
        }

        [Test]
        public void A_machine_takes_its_inputs_from_the_farmhouse_and_gives_its_output_back()
        {
            var e = NewIsland(wheatInStore: 30);
            Assert.IsTrue(e.ToolsFor(Mill, T0).Contains(Tool.InputOf("wheat")));
            Assert.AreEqual(ToolRefusal.WrongTarget, e.CanUse(Mill, Tool.InputOf("egg"), T0));
            var fill = e.Use(Mill, Tool.InputOf("wheat"), T0);
            Assert.IsTrue(fill.Ok);
            Assert.AreEqual(GameplayRules.HopperSize, fill.Units);
            Assert.AreEqual(30 - GameplayRules.HopperSize, e.StoredInFarmhouses("wheat"));
            var later = T0 + 3600;
            var collect = e.Use(Mill, Tool.Hand, later);
            Assert.IsTrue(collect.Ok);
            Assert.AreEqual("flour", collect.Item);
            Assert.IsTrue(e.StoredInFarmhouses("flour") > 0);
        }

        [Test]
        public void A_full_farmhouse_stops_the_sickle()
        {
            var e = NewIsland();
            e.Use(Field, Tool.Seed("wheat"), T0);
            var ripe = e.Plots[Field].ReadyAt + 1;
            e.GiveToFarmhouses("stone", e.FarmhouseRoom);   // fill it up
            Assert.AreEqual(0, e.FarmhouseRoom);
            Assert.AreEqual(ToolRefusal.StorageFull, e.CanUse(Field, Tool.Sickle, ripe));
            Assert.AreEqual(PlotState.Ripe, e.Plots[Field].State(ripe));
        }

        [Test]
        public void Locked_crops_are_not_offered()
        {
            var e = NewIsland();
            var locked = GameplayRules.Crops.FirstOrDefault(c => !e.CropOpen(c.Id));
            if (locked == null) return;   // every crop is open at level 1: nothing to check
            Assert.IsFalse(e.ToolsFor(Field, T0).Contains(Tool.Seed(locked!.Id)));
            Assert.AreEqual(ToolRefusal.Locked, e.CanUse(Field, Tool.Seed(locked.Id), T0));
        }
    }
}
