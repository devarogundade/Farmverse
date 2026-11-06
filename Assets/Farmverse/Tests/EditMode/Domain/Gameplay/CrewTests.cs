#nullable enable
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using NUnit.Framework;

namespace Farmverse.Tests.Domain.Gameplay
{
    /// <summary>Farmhands, golden harvests and wild plots (docs 15 §7, §12).</summary>
    [TestFixture]
    public sealed class CrewTests
    {
        private const int House = 1, Field = 2, Workers = 30, Workers2 = 31;

        private static IslandEconomy NewIsland(double now = 1000)
        {
            var e = new IslandEconomy(5000) { SeasonsOn = false, MarketMoves = false };
            e.AddBuilding(House, "farmhouse", now);
            e.AddBuilding(Field, GameplayRules.FieldBuilding, now);
            e.AddBuilding(Workers, "workers_house", now);
            e.Departments[Workers].Energy = 10000;
            return e;
        }

        private static Farmhand Hand(string token, string specialty = "fields", int speed = 1, int carry = 1, int stamina = 1, int level = 1) =>
            new Farmhand { token = token, name = token, specialty = specialty, speed = speed, carry = carry, stamina = stamina, level = level };

        [Test]
        public void Beds_follow_the_workers_houses_and_cap_at_six()
        {
            var e = NewIsland();
            e.SetCrew(Enumerable.Range(1, 9).Select(i => Hand($"s-fh-{i}")));
            Assert.AreEqual(1, e.CrewBeds, "a level-1 workers' house has one bed");
            Assert.AreEqual(1, e.WorkingCrew.Count);
            Assert.AreEqual(9, e.Crew.Count, "the rest wait in the wallet");
            e.AddBuilding(Workers2, "workers_house", 1000);
            Assert.AreEqual(2, e.CrewBeds);
        }

        [Test]
        public void Traits_and_levels_add_hands_and_carry()
        {
            var e = NewIsland();
            Assert.AreEqual(0, e.CrewHands);
            e.SetCrew(new[] { Hand("a", speed: 4, stamina: 3, carry: 5, level: 5) });
            Assert.AreEqual(3, e.CrewHands, "one, +1 for speed+stamina ≥ 7, +1 for level 5");
            Assert.AreEqual(2, e.CrewCarryBonus);
            Assert.AreEqual(1, e.SpecialistHands(WorkJob.Harvest));
            Assert.AreEqual(0, e.SpecialistHands(WorkJob.Feed));
        }

        [Test]
        public void Crew_values_are_clamped_and_duplicates_dropped()
        {
            var e = NewIsland();
            e.SetCrew(new[] { Hand("a", speed: 99, level: 50), Hand("a"), new Farmhand() });
            Assert.AreEqual(1, e.Crew.Count);
            Assert.AreEqual(5, e.Crew[0].speed);
            Assert.AreEqual(GameplayRules.FarmhandMaxLevel, e.Crew[0].level);
        }

        [Test]
        public void A_farmhand_makes_offline_work_go_further()
        {
            var plain = NewIsland();
            plain.Plant(Field, "wheat", 1000);
            var crewed = NewIsland();
            crewed.Plant(Field, "wheat", 1000);
            crewed.SetCrew(new[] { Hand("a", speed: 5, stamina: 5, carry: 5, level: 10) });
            var a = plain.WorkOffline(1000, 1000 + 600);
            var b = crewed.WorkOffline(1000, 1000 + 600);
            Assert.IsTrue(b.Workers > a.Workers);
            Assert.IsTrue(b.Harvested >= a.Harvested);
        }

        [Test]
        public void Crew_and_wild_plots_survive_a_save()
        {
            var e = NewIsland();
            e.SetCrew(new[] { Hand("s-fh-7", "animals", 2, 3, 4, 6) });
            Assert.IsTrue(e.OpenWildPlot());
            var state = new IslandState { simAt = 1000, seed = 1, economy = e.ToSave() };
            var back = IslandSim.Load(state);
            Assert.AreEqual("s-fh-7", back.Crew.Single().token);
            Assert.AreEqual(6, back.Crew.Single().level);
            Assert.AreEqual(1, back.WildPlotsOpen);
        }

        [Test]
        public void Wild_plots_open_one_by_one_up_to_the_limit()
        {
            var e = NewIsland();
            for (var i = 0; i < GameplayRules.WildPlots; i++) Assert.IsTrue(e.OpenWildPlot());
            Assert.IsFalse(e.OpenWildPlot());
            Assert.AreEqual(GameplayRules.WildPlots, e.WildPlotsOpen);
        }

        [Test]
        public void Golden_goods_are_rare_deterministic_and_worth_more()
        {
            Assert.AreEqual(GameplayRules.GoldenPriceFactor * GameplayRules.PriceOf("wheat"), GameplayRules.PriceOf("golden_wheat"));
            int Golden()
            {
                var e = new IslandEconomy(5000) { SeasonsOn = false, MarketMoves = false };
                e.AddBuilding(House, "farmhouse", 0);
                for (var f = 0; f < 50; f++) e.AddBuilding(100 + f, GameplayRules.FieldBuilding, 0);
                var into = new List<string>();
                for (var round = 0; round < 20; round++)
                {
                    var t = round * 100_000.0;
                    for (var f = 0; f < 50; f++)
                    {
                        e.Plant(100 + f, "wheat", t);
                        e.TakeHarvest(100 + f, t + 90_000, 1000, into);
                    }
                }
                Assert.IsTrue(into.Count > 1000);
                return into.Count(i => i == "golden_wheat");
            }
            var first = Golden();
            Assert.AreEqual(first, Golden(), "the same island harvests the same golden units");
            Assert.IsTrue(first > 0, "some come up golden");
        }

        [Test]
        public void Exchange_sets_the_crew_and_opens_plots()
        {
            var state = new IslandState
            {
                simAt = 1000,
                seed = 1,
                buildings = { new IslandState.Placed { id = "farmhouse", uid = 1, x = 150, z = 150, w = 4, d = 4 } },
            };
            var (a, done) = IslandExchange.Apply(state, 1001, new[]
            {
                new ExchangeOp { op = "crew", crew = new List<Farmhand> { Hand("s-fh-1"), Hand("s-fh-2") } },
                new ExchangeOp { op = "openPlot" },
            });
            Assert.AreEqual(2, done[0].n);
            Assert.AreEqual(1, done[1].n);
            var e = IslandSim.Load(a.State);
            Assert.AreEqual(2, e.Crew.Count);
            Assert.AreEqual(1, e.WildPlotsOpen);
        }

        [Test]
        public void A_deed_on_a_fully_open_island_does_nothing_but_does_not_block()
        {
            var state = new IslandState { simAt = 1000, seed = 1, buildings = { new IslandState.Placed { id = "farmhouse", uid = 1, x = 150, z = 150, w = 4, d = 4 } } };
            var ops = new List<ExchangeOp>();
            for (var i = 0; i <= GameplayRules.WildPlots; i++) ops.Add(new ExchangeOp { op = "openPlot" });
            ops.Add(new ExchangeOp { op = "give", item = "wheat", n = 5 });
            var (a, done) = IslandExchange.Apply(state, 1001, ops);
            Assert.AreEqual(-1, done[GameplayRules.WildPlots].n);
            Assert.AreEqual(5, IslandSim.Load(a.State).StoredInFarmhouses("wheat"), "the other grants still land");
        }
    }
}
