#nullable enable
using System;
using Farmverse.Core.Time;
using NUnit.Framework;

namespace Farmverse.Tests.Core
{
    [TestFixture]
    public sealed class ServerClockTests
    {
        [Test]
        public void UsesTheDeviceClockUntilSynced()
        {
            var timer = new ManualTimer { ElapsedMs = 1_000 };
            var clock = new ServerClock(timer, new ManualClock(5_000_000));
            Assert.IsFalse(clock.IsSynced.Value);
            Assert.AreEqual(5_000_000L, clock.NowMs);
            timer.Advance(250);
            Assert.AreEqual(5_000_250L, clock.NowMs);
        }

        [Test]
        public void CorrectsForHalfTheRoundTrip()
        {
            var timer = new ManualTimer { ElapsedMs = 10_000 };
            var clock = new ServerClock(timer, new ManualClock(1));
            var sent = clock.Stamp();
            timer.Advance(200);                       // 200 ms round trip
            Assert.IsTrue(clock.Observe(9_000_000, sent));
            Assert.IsTrue(clock.IsSynced.Value);
            Assert.AreEqual(9_000_100L, clock.NowMs); // server time + rtt/2
        }

        [Test]
        public void IgnoresChangesToTheDeviceDate()
        {
            var timer = new ManualTimer();
            var device = new ManualClock(1_000);
            var clock = new ServerClock(timer, device);
            clock.Observe(50_000, clock.Stamp());
            device.Set(999_999_999);                  // the player changes the date
            Assert.AreEqual(50_000L, clock.NowMs);
        }

        [Test]
        public void TheMedianResistsOneSlowSample()
        {
            var timer = new ManualTimer();
            var clock = new ServerClock(timer, new ManualClock(0));
            foreach (var server in new long[] { 100_000, 100_000, 100_000 })
            {
                clock.Observe(server, clock.Stamp());
            }
            clock.Observe(160_000, clock.Stamp());    // an outlier
            Assert.AreEqual(100_000L, clock.NowMs);
        }

        [Test]
        public void RejectsImpreciseSamples()
        {
            var timer = new ManualTimer();
            var clock = new ServerClock(timer, new ManualClock(0));
            var sent = clock.Stamp();
            timer.Advance((long)ServerClock.MaxRoundTrip.TotalMilliseconds + 1);
            Assert.IsFalse(clock.Observe(1_000_000, sent));
            Assert.IsFalse(clock.IsSynced.Value);
        }

        [Test]
        public void ManualClockRefusesToGoBack()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ManualClock().Advance(TimeSpan.FromMilliseconds(-1)));
        }
    }
}
