#nullable enable
using System;
using System.Linq;
using Farmverse.Domain.Gameplay;
using NUnit.Framework;

namespace Farmverse.Tests.Domain.Gameplay
{
    [TestFixture]
    public sealed class TrainTests
    {
        private static IslandEconomy Island(int level)
        {
            var e = new IslandEconomy(1000) { SeasonsOn = false, MarketMoves = false, Rng = new Random(5) };
            e.SetLevel(level);
            e.AddBuilding(1, "farmhouse", 1000);
            e.AddBuilding(2, GameplayRules.FieldBuilding, 1000);
            return e;
        }

        [Test]
        public void No_train_before_level_5()
        {
            var e = Island(4);
            for (var t = 1000; t < 1000 + 7200; t += 60) e.Tick(t);
            Assert.IsNull(e.Train);
        }

        [Test]
        public void Loaded_crates_pay_materials_and_coins_when_the_train_leaves()
        {
            var e = Island(8);
            e.Tick(1000);
            e.Tick(1040);
            var train = e.Train!;
            Assert.AreEqual(GameplayRules.TrainCrates, train.Crates.Count);
            foreach (var c in train.Crates) Assert.IsTrue(e.Makeable().Contains(c.Item), $"asks for {c.Item}");
            Assert.IsFalse(e.LoadCrate(0), "nothing in store yet");
            foreach (var c in train.Crates) e.Store(1, c.Item, c.Need);
            Assert.IsTrue(e.LoadCrate(0));
            Assert.IsFalse(e.LoadCrate(0), "once");
            var coins = e.Coins;
            var wood = e.InStore("wood") + e.InStore("stone") + e.InStore("planks");
            Assert.IsTrue(e.SendTrain(1100));
            var pay = e.TakeTrainPayout()!;
            Assert.AreEqual(1, pay.Crates);
            Assert.AreEqual(coins + pay.Coins, e.Coins);
            Assert.IsTrue(e.InStore("wood") + e.InStore("stone") + e.InStore("planks") > wood, "materials arrive in the farmhouse");
            Assert.IsNull(e.Train);
            Assert.AreEqual(1, e.Stat("trains"));
            Assert.AreEqual(1100 + GameplayRules.TrainGapSeconds, e.NextTrainAt, 1e-9);
        }

        [Test]
        public void The_train_survives_a_restart_and_leaves_on_time()
        {
            var e = Island(8);
            e.Tick(1000);
            e.Tick(1040);
            foreach (var c in e.Train!.Crates) e.Store(1, c.Item, c.Need);
            e.LoadCrate(1);
            var back = Island(8);
            back.Restore(e.ToSave());
            Assert.IsTrue(back.Train!.Crates[1].Loaded);
            back.Tick(1040 + GameplayRules.TrainStaySeconds + 1);
            Assert.IsNull(back.Train);
            Assert.AreEqual(1, back.Stat("trains"), "it left with the loaded crate and paid");
        }
    }
}
