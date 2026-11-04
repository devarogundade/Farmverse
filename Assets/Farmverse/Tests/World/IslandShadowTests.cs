#nullable enable
using Farmverse.Domain.Gameplay;
using Farmverse.Presentation.World.Island;
using NUnit.Framework;
using UnityEngine;

namespace Farmverse.Tests.World
{
    /// <summary>Shadow mode's comparison of the game's run with the server's (rollout phase 1).</summary>
    [TestFixture]
    public sealed class IslandShadowTests
    {
        private const double T0 = 1_800_000_000;

        /// <summary>An island that has been played once (so it has an economy save), as the game would offer it.</summary>
        private static IslandState Saved()
        {
            var fresh = new IslandState
            {
                simAt = T0,
                seed = 42,
                buildings =
                {
                    new IslandState.Placed { id = "farmhouse", uid = 1, x = 150, z = 150, w = 4, d = 4 },
                    new IslandState.Placed { id = GameplayRules.FieldBuilding, uid = 2, x = 156, z = 150, w = 3, d = 3 },
                    new IslandState.Placed { id = "chicken_coop", uid = 3, x = 150, z = 156, w = 3, d = 3 },
                },
            };
            return IslandSim.Advance(fresh, T0 + 5).State;
        }

        /// <summary>The state through JSON, as it travels to the server and back.</summary>
        private static IslandState Trip(IslandState s) => JsonUtility.FromJson<IslandState>(JsonUtility.ToJson(s));

        [Test]
        public void The_same_run_on_the_server_matches()
        {
            var before = Saved();
            var server = IslandSim.Advance(Trip(before), T0 + 3600).State;
            var check = IslandShadow.Compare(Trip(before), Trip(server));
            Assert.IsTrue(check.Match, check.Detail);
            Assert.AreEqual(3595, check.AwaySeconds, 1e-6);
        }

        [Test]
        public void A_different_result_is_a_mismatch_and_says_where()
        {
            var before = Saved();
            var server = Trip(IslandSim.Advance(Trip(before), T0 + 3600).State);
            server.economy!.coins += 1;
            var check = IslandShadow.Compare(Trip(before), server);
            Assert.IsFalse(check.Match);
            StringAssert.Contains("coins", check.Detail);
        }

        [Test]
        public void A_clamped_device_clock_is_skipped_not_compared()
        {
            var before = Saved();
            var server = Trip(IslandSim.Advance(Trip(before), T0).State);
            server.simAt = T0 - 10;   // the server's now is before the device's save: the device clock ran ahead
            Assert.IsTrue(IslandShadow.Compare(Trip(before), server).Skipped);
        }

        // ---- cloud save -----------------------------------------------------------------------------

        private static IslandShadow.CloudCopy Cloud(long xp) => new IslandShadow.CloudCopy
        {
            DeviceSave = "{\"files\":[{\"key\":\"fv_economy_v1\",\"value\":\"{\\\"xp\\\":" + xp + "}\"}]}",
        };

        [Test]
        public void The_cloud_xp_is_read_from_the_saved_economy_file()
        {
            Assert.AreEqual(1234, Cloud(1234).Xp);
            Assert.AreEqual(0, new IslandShadow.CloudCopy { DeviceSave = "not json" }.Xp);
        }

        [Test]
        public void A_new_phone_takes_the_cloud_farm_only_when_it_has_gone_further()
        {
            Assert.IsTrue(IslandShadow.ShouldRestore(null, "uid1", 0, Cloud(500)), "fresh install, farm in the cloud");
            Assert.IsTrue(IslandShadow.ShouldRestore("guest", "uid1", 10, Cloud(500)), "switched to the account that owns the farm");
            Assert.IsFalse(IslandShadow.ShouldRestore("uid1", "uid1", 0, Cloud(500)), "this phone already saves to this player");
            Assert.IsFalse(IslandShadow.ShouldRestore(null, "uid1", 900, Cloud(500)), "the phone's island is further along");
            Assert.IsFalse(IslandShadow.ShouldRestore(null, "uid1", 0, null), "nothing in the cloud");
            Assert.IsFalse(IslandShadow.ShouldRestore(null, "uid1", 0, new IslandShadow.CloudCopy()), "a cloud copy without device files");
        }
    }
}
