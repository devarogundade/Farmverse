#nullable enable
using Farmverse.Domain.Gameplay;
using NUnit.Framework;

namespace Farmverse.Tests.Domain.Gameplay
{
    /// <summary>The island engine's advance: deterministic, and the same as the game's own catch-up.</summary>
    [TestFixture]
    public sealed class IslandSimTests
    {
        private static IslandState NewState(double now = 1_800_000_000) => new IslandState
        {
            simAt = now,
            seed = 42,
            buildings =
            {
                new IslandState.Placed { id = "farmhouse", uid = 1, x = 150, z = 150, w = 4, d = 4 },
                new IslandState.Placed { id = GameplayRules.FieldBuilding, uid = 2, x = 156, z = 150, w = 3, d = 3 },
                new IslandState.Placed { id = "chicken_coop", uid = 3, x = 150, z = 156, w = 3, d = 3 },
            },
        };

        /// <summary>A fingerprint of everything the economy saves that matters for comparing two runs.</summary>
        private static string Json(IslandState s)
        {
            var e = s.economy!;
            var sb = new System.Text.StringBuilder();
            sb.Append(s.simAt).Append('|').Append(e.coins).Append('|').Append(e.xp).Append('|').Append(e.now).Append('|').Append(e.orderItem).Append(e.orderCount);
            foreach (var p in e.plots) sb.Append("|p").Append(p.uid).Append(p.crop).Append(p.readyAt).Append(p.left);
            foreach (var d in e.departments)
            {
                sb.Append("|d").Append(d.uid).Append(':').Append(d.energy.ToString("R"));
                foreach (var c in d.stock) sb.Append(c.item).Append(c.n);
            }

            foreach (var a in e.animals) sb.Append("|a").Append(a.uid).Append(a.stock).Append(a.feed);
            return sb.ToString();
        }

        [Test]
        public void Advancing_the_same_island_to_the_same_time_gives_the_same_state()
        {
            var a = IslandSim.Advance(NewState(), 1_800_003_600);
            var b = IslandSim.Advance(NewState(), 1_800_003_600);
            Assert.AreEqual(Json(a.State), Json(b.State));
        }

        [Test]
        public void Advancing_in_two_steps_ends_where_one_step_does_when_nothing_is_away()
        {
            var t0 = 1_800_000_000.0;
            var once = IslandSim.Advance(NewState(t0), t0 + 30);
            var first = IslandSim.Advance(NewState(t0), t0 + 10);
            var twice = IslandSim.Advance(first.State, t0 + 30);
            Assert.AreEqual(once.State.economy!.coins, twice.State.economy!.coins);
            Assert.AreEqual(t0 + 30, twice.State.simAt);
        }

        [Test]
        public void A_planted_field_wakes_the_island_when_it_ripens()
        {
            var t0 = 1_800_000_000.0;
            var e = IslandSim.Load(NewState(t0));
            e.Tick(t0);
            Assert.AreEqual(GatherResult.Done, e.Plant(2, "wheat", t0));
            var wake = IslandSim.NextWake(e, t0);
            Assert.AreEqual(e.Plots[2].ReadyAt, wake, 0.001);
        }

        [Test]
        public void An_island_from_newer_rules_is_refused()
        {
            var s = NewState();
            s.rulesVersion = IslandSim.RulesVersion + 1;
            Assert.Throws<System.InvalidOperationException>(() => IslandSim.Load(s));
        }
    }
}
