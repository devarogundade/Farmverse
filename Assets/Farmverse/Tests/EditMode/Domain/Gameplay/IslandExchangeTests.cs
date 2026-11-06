#nullable enable
using System.Linq;
using Farmverse.Domain.Gameplay;
using NUnit.Framework;

namespace Farmverse.Tests.Domain.Gameplay
{
    /// <summary>Goods and buildings leaving the island for the chain and coming back (docs 13 §7).</summary>
    [TestFixture]
    public sealed class IslandExchangeTests
    {
        private const double T0 = 1_800_000_000;

        private static IslandState NewState() => new IslandState
        {
            simAt = T0,
            seed = 42,
            buildings =
            {
                new IslandState.Placed { id = "farmhouse", uid = 1, x = 150, z = 150, w = 4, d = 4 },
                new IslandState.Placed { id = GameplayRules.FieldBuilding, uid = 2, x = 156, z = 150, w = 3, d = 3 },
                new IslandState.Placed { id = "chicken_coop", uid = 3, x = 150, z = 156, w = 3, d = 3, price = 120 },
            },
        };

        /// <summary>The island with <paramref name="n"/> wheat in the farmhouse.</summary>
        private static IslandState WithWheat(int n)
        {
            var (a, _) = IslandExchange.Apply(NewState(), T0, new[] { new ExchangeOp { op = "give", item = "wheat", n = n } });
            return a.State;
        }

        private static int Wheat(IslandState s) => IslandSim.Load(s).StoredInFarmhouses("wheat");

        [Test]
        public void Give_puts_goods_in_the_farmhouse()
        {
            Assert.AreEqual(250, Wheat(WithWheat(250)));
        }

        [Test]
        public void Take_removes_exactly_what_was_asked_and_reports_its_value()
        {
            var (a, done) = IslandExchange.Apply(WithWheat(250), T0 + 1, new[] { new ExchangeOp { op = "take", item = "wheat", n = 100 } });
            Assert.AreEqual(150, Wheat(a.State));
            Assert.AreEqual(100, done.Single().n);
            Assert.AreEqual(GameplayRules.PriceOf("wheat"), done.Single().unitValue);
        }

        [Test]
        public void Taking_more_than_is_stored_changes_nothing()
        {
            var state = WithWheat(10);
            var e = Assert.Throws<ExchangeRefusal>(() => IslandExchange.Apply(state, T0 + 1, new[] { new ExchangeOp { op = "take", item = "wheat", n = 11 } }));
            Assert.AreEqual("not_enough", e!.Code);
            Assert.AreEqual(10, Wheat(state));
        }

        [Test]
        public void Ops_are_all_or_nothing()
        {
            var state = WithWheat(10);
            Assert.Throws<ExchangeRefusal>(() => IslandExchange.Apply(state, T0 + 1, new[]
            {
                new ExchangeOp { op = "take", item = "wheat", n = 5 },
                new ExchangeOp { op = "take", item = "tomato", n = 1 },
            }));
            Assert.AreEqual(10, Wheat(state));
        }

        [Test]
        public void An_empty_building_is_picked_up_with_its_level_and_price()
        {
            var (a, done) = IslandExchange.Apply(NewState(), T0 + 1, new[] { new ExchangeOp { op = "pickUp", uid = 3 } });
            Assert.IsFalse(a.State.buildings.Any(b => b.uid == 3));
            var d = done.Single();
            Assert.AreEqual("chicken_coop", d.id);
            Assert.AreEqual(1, d.level);
            Assert.AreEqual(120, d.price);
        }

        [Test]
        public void The_farmhouse_cannot_be_picked_up()
        {
            var e = Assert.Throws<ExchangeRefusal>(() => IslandExchange.Apply(NewState(), T0 + 1, new[] { new ExchangeOp { op = "pickUp", uid = 1 } }));
            Assert.AreEqual("core_building", e!.Code);
        }

        [Test]
        public void A_missing_building_is_refused()
        {
            var e = Assert.Throws<ExchangeRefusal>(() => IslandExchange.Apply(NewState(), T0 + 1, new[] { new ExchangeOp { op = "pickUp", uid = 99 } }));
            Assert.AreEqual("no_building", e!.Code);
        }

        [Test]
        public void A_redeemed_building_is_held_and_survives_advancing()
        {
            var (a, _) = IslandExchange.Apply(NewState(), T0 + 1, new[] { new ExchangeOp { op = "hold", id = "chicken_coop", level = 3, price = 120 } });
            var later = IslandSim.Advance(a.State, T0 + 3600);
            var held = later.State.held.Single();
            Assert.AreEqual("chicken_coop", held.id);
            Assert.AreEqual(3, held.level);
        }

        [Test]
        public void Unknown_ops_are_refused()
        {
            var e = Assert.Throws<ExchangeRefusal>(() => IslandExchange.Apply(NewState(), T0, new[] { new ExchangeOp { op = "duplicate", item = "wheat", n = 1 } }));
            Assert.AreEqual("bad_op", e!.Code);
        }

        [Test]
        public void Exchange_is_deterministic()
        {
            var ops = new[] { new ExchangeOp { op = "take", item = "wheat", n = 7 } };
            var a = IslandExchange.Apply(WithWheat(20), T0 + 60, ops).advance.State;
            var b = IslandExchange.Apply(WithWheat(20), T0 + 60, ops).advance.State;
            Assert.AreEqual(a.economy!.coins, b.economy!.coins);
            Assert.AreEqual(Wheat(a), Wheat(b));
        }
    }
}
