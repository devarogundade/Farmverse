#nullable enable
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using NUnit.Framework;

namespace Farmverse.Tests.Domain.Gameplay
{
    /// <summary>What an island could have made between two saves: the mint check in shadow mode (docs 13 §7.3).</summary>
    [TestFixture]
    public sealed class IslandEarnedTests
    {
        private const double T0 = 1_800_000_000;
        private const double Hour = 3600;

        private static IslandState NewState(params IslandState.Placed[] more)
        {
            var s = new IslandState
            {
                simAt = T0,
                seed = 42,
                buildings =
                {
                    new IslandState.Placed { id = "farmhouse", uid = 1, x = 150, z = 150, w = 4, d = 4 },
                    new IslandState.Placed { id = GameplayRules.FieldBuilding, uid = 2, x = 156, z = 150, w = 3, d = 3 },
                },
            };
            s.buildings.AddRange(more);
            return s;
        }

        private static IslandState.Placed Coop(int uid = 3) => new IslandState.Placed { id = "chicken_coop", uid = uid, x = 150, z = 156, w = 3, d = 3, price = 120 };

        /// <summary><paramref name="state"/> run to <paramref name="at"/> with these goods put in the farmhouse.</summary>
        private static IslandState With(IslandState state, double at, params (string item, int n)[] goods)
        {
            var ops = goods.Select(g => new ExchangeOp { op = "give", item = g.item, n = g.n }).ToList();
            if (ops.Count == 0) ops.Add(new ExchangeOp { op = "credit", n = 1 });
            return IslandExchange.Apply(state, at, ops).advance.State;
        }

        private static int Of(IDictionary<string, int> d, string item) => d.TryGetValue(item, out var n) ? n : 0;

        [Test]
        public void Ceiling_follows_the_buildings_and_the_time()
        {
            var c = IslandEarned.Ceiling(NewState(Coop()), Hour);
            // a field grows 6 wheat a minute (plus one harvest in hand), ×2 head-room
            Assert.AreEqual(2 * (6 * 60 + 6), Of(c, "wheat"));
            // a coop lays an egg every 40 s (plus a full coop of 6)
            Assert.AreEqual(2 * (3600 / 40 + 6), Of(c, "egg"));
            Assert.IsTrue(Of(c, "golden_wheat") > 0);
            Assert.IsTrue(Of(c, "golden_wheat") < Of(c, "wheat") / 10);
            Assert.AreEqual(0, Of(c, "flour"), "no windmill, no flour");
            Assert.IsTrue(Of(IslandEarned.Ceiling(NewState(Coop()), 60), "wheat") < Of(c, "wheat"));
        }

        [Test]
        public void Honest_growth_counts_in_full()
        {
            var before = With(NewState(Coop()), T0, ("wheat", 20));
            var after = With(before, T0 + Hour, ("wheat", 300), ("egg", 50));
            var r = IslandEarned.Compare(before, after, Hour);
            Assert.AreEqual(300, Of(r.gain, "wheat"));
            Assert.AreEqual(50, Of(r.gain, "egg"));
            Assert.AreEqual(320, Of(r.stock, "wheat"));
            Assert.AreEqual(150, r.room, "a level-1 farmhouse holds 150");
        }

        [Test]
        public void A_jump_counts_only_what_the_buildings_could_make()
        {
            var before = With(NewState(), T0);
            var after = With(before, T0 + 60, ("wheat", 10_000));
            var r = IslandEarned.Compare(before, after, 60);
            Assert.AreEqual(Of(IslandEarned.Ceiling(before, 60), "wheat"), Of(r.gain, "wheat"));
            Assert.AreEqual(2 * (6 + 6), Of(r.gain, "wheat"));
            Assert.AreEqual(10_000, Of(r.stock, "wheat"), "the stock is reported as it is");
        }

        [Test]
        public void Buildings_new_in_the_later_save_add_nothing()
        {
            var before = With(NewState(), T0);
            // the upload adds a coop and 5,000 eggs: nothing on the earlier island lays eggs
            var after = With(NewState(Coop()), T0 + Hour, ("egg", 5_000));
            var r = IslandEarned.Compare(before, after, Hour);
            Assert.AreEqual(IslandEarned.OtherCeiling(Hour), Of(r.gain, "egg"));
            Assert.IsTrue(Of(r.gain, "egg") < 200);
        }

        [Test]
        public void Goods_that_fell_or_held_still_gain_nothing()
        {
            var before = With(NewState(), T0, ("wheat", 100), ("stone", 40));
            var after = With(NewState(), T0 + Hour, ("wheat", 60), ("stone", 40));
            var r = IslandEarned.Compare(before, after, Hour);
            Assert.AreEqual(0, Of(r.gain, "wheat"));
            Assert.AreEqual(0, Of(r.gain, "stone"));
        }

        [Test]
        public void A_long_absence_counts_a_week_at_most()
        {
            var s = NewState();
            Assert.AreEqual(Of(IslandEarned.Ceiling(s, IslandEarned.MaxSeconds), "wheat"), Of(IslandEarned.Ceiling(s, 90 * 24 * Hour), "wheat"));
            Assert.AreEqual(Of(IslandEarned.Ceiling(s, 0), "wheat"), Of(IslandEarned.Ceiling(s, -5), "wheat"));
        }
    }
}
