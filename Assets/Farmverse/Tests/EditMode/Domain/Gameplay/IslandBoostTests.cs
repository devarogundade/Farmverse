#nullable enable
using Farmverse.Domain.Gameplay;
using NUnit.Framework;

namespace Farmverse.Tests.Domain.Gameplay
{
    /// <summary>Boosts and purchases that live in the economy (saved with the island, run the same on the server).</summary>
    [TestFixture]
    public sealed class IslandBoostTests
    {
        private const int House = 1, Field = 2, Coop = 3;

        private static IslandEconomy NewIsland(double now = 1000)
        {
            var e = new IslandEconomy(500) { SeasonsOn = false, MarketMoves = false };
            e.AddBuilding(House, "farmhouse", now);
            e.AddBuilding(Field, GameplayRules.FieldBuilding, now);
            return e;
        }

        [Test]
        public void Fertilizer_doubles_growth_only_while_it_runs()
        {
            var e = NewIsland();
            e.FertUntil = 1500;
            e.Plant(Field, "wheat", 1000);
            Assert.AreEqual(PlotState.Ripe, e.Plots[Field].State(1030), "wheat takes 60 s; fertilized, 30 s");
            for (var i = 0; i < 6; i++) e.HarvestStep(Field, 1100);
            e.Plant(Field, "wheat", 2000);
            Assert.AreEqual(PlotState.Growing, e.Plots[Field].State(2030), "after the boost ran out, normal speed");
        }

        [Test]
        public void Extra_builders_and_basket_slots_add_up_and_survive_a_save()
        {
            var e = NewIsland();
            var builders = e.Builders;
            var slots = e.Basket.Capacity;
            e.ExtraBuilders = 2;
            e.AddBasketSlots(10);
            e.FertUntil = 4242;
            Assert.AreEqual(builders + 2, e.Builders);
            Assert.AreEqual(slots + 10, e.Basket.Capacity);
            var back = new IslandEconomy(500) { SeasonsOn = false, MarketMoves = false };
            back.AddBuilding(House, "farmhouse", 1000);
            back.AddBuilding(Field, GameplayRules.FieldBuilding, 1000);
            back.Restore(e.ToSave());
            Assert.AreEqual(builders + 2, back.Builders);
            Assert.AreEqual(slots + 10, back.Basket.Capacity);
            Assert.AreEqual(4242, back.FertUntil);
        }

        [Test]
        public void Instant_build_finishes_the_oldest_site()
        {
            var e = NewIsland();
            e.AddBuilding(Coop, "chicken_coop", 1000, 500);
            Assert.IsFalse(e.IsBuilt(Coop));
            Assert.IsTrue(e.FinishOneSite(1001));
            Assert.IsTrue(e.IsBuilt(Coop));
            Assert.IsFalse(e.FinishOneSite(1002), "nothing left to build");
        }
    }
}
