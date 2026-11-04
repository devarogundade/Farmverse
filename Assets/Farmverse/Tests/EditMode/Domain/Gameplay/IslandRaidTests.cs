#nullable enable
using System;
using System.Linq;
using Farmverse.Domain.Gameplay;
using NUnit.Framework;

namespace Farmverse.Tests.Domain.Gameplay
{
    /// <summary>The day's raid when the player is away (the server) and its losses on the phone's copy.</summary>
    [TestFixture]
    public sealed class IslandRaidTests
    {
        private const int House = 1, Field = 2, Army = 4, Mill = 9;

        private static IslandEconomy Island(bool army, double now = 1000)
        {
            var e = new IslandEconomy(1000) { SeasonsOn = false, MarketMoves = false };
            e.AddBuilding(House, "farmhouse", now);
            e.AddBuilding(Field, GameplayRules.FieldBuilding, now);
            e.AddBuilding(Mill, "windmill", now, price: 900);
            if (army) e.AddBuilding(Army, "army_house", now);
            e.Store(House, "wheat", 30);
            e.Store(House, "wood", 10);
            return e;
        }

        [Test]
        public void An_army_that_matches_the_raiders_drives_them_off_and_nothing_is_lost()
        {
            var e = Island(army: true);
            var o = e.RaidAway(1000, new Random(1));
            Assert.IsTrue(o.won);
            Assert.IsTrue(o.ratio >= 1);
            Assert.AreEqual(0, o.StolenTotal);
            Assert.AreEqual(1000, e.Coins);
            Assert.AreEqual(40, e.Departments[House].Stored);
        }

        [Test]
        public void With_nobody_to_fight_they_take_goods_and_coins_and_trample_a_field()
        {
            var e = Island(army: false);
            e.Plant(Field, "wheat", 1000);
            var coins = e.Coins;
            var o = e.RaidAway(1000, new Random(1));
            Assert.IsFalse(o.won);
            Assert.AreEqual(0, o.ratio, 1e-9);
            Assert.AreEqual(System.Math.Min(6 + 5 * o.size, 40 / 10), o.StolenTotal, "a full haul, capped at a tenth of the store");
            Assert.AreEqual(40 - o.StolenTotal, e.Departments[House].Stored);
            Assert.AreEqual((int)Math.Floor(coins * GameplayRules.CoinTheft), o.coins, "5% with no bank");
            Assert.AreEqual(coins - o.coins, e.Coins);
            Assert.AreEqual(1, o.trampled.Count, "its one field");
            CollectionAssert.AreEqual(o.stolen.Select(c => c.item).OrderBy(i => i, StringComparer.Ordinal).ToList(), o.stolen.Select(c => c.item).ToList());
        }

        [Test]
        public void The_same_island_and_day_give_the_same_raid()
        {
            var a = Island(army: false).RaidAway(1000, new Random(7));
            var b = Island(army: false).RaidAway(1000, new Random(7));
            string Key(RaidOutcome o) => $"{o.size}|{o.coins}|{string.Join(",", o.stolen.Select(c => c.item + c.n))}|{string.Join(",", o.trampled)}|{string.Join(",", o.ruined)}";
            Assert.AreEqual(Key(a), Key(b));
        }

        [Test]
        public void The_phone_takes_the_same_losses_but_never_more_than_it_has()
        {
            var server = Island(army: false);
            var o = server.RaidAway(1000, new Random(1));
            var phone = Island(army: false);
            phone.Departments[House].TakeStock("wheat", 25);   // sold most of it before the raid reached it
            phone.ApplyRaid(o, 1000);
            Assert.AreEqual(950, phone.Coins);
            var stolenWheat = o.stolen.Where(c => c.item == "wheat").Sum(c => c.n);
            Assert.AreEqual(System.Math.Max(0, 5 - stolenWheat), phone.Departments[House].Stock.TryGetValue("wheat", out var w) ? w : 0);
            Assert.IsTrue(phone.Departments[House].Stored >= 0);
        }

        [Test]
        public void IslandSim_runs_the_raid_after_catching_the_island_up()
        {
            var state = new IslandState { simAt = 1000, seed = 3 };
            state.buildings.Add(new IslandState.Placed { id = "farmhouse", uid = 1, x = 10, z = 10, w = 2, d = 2 });
            var (advance, raid) = IslandSim.Raid(state, 1600, 20500);
            Assert.AreEqual(1600, advance.State.simAt);
            Assert.IsTrue(raid.size >= 1);
        }
    }
}
