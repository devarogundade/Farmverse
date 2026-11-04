#nullable enable
using System;
using System.Linq;
using Farmverse.Application.Features;
using Farmverse.Application.Ports;
using NUnit.Framework;

namespace Farmverse.Tests.Application
{
    [TestFixture]
    public sealed class NudgePlannerTests
    {
        private static readonly long Noon = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        private const long Minute = 60_000, Hour = 60 * Minute;

        private static LocalNudge N(string topic, long at, string title = "x") => new LocalNudge { Topic = topic, AtMs = at, Title = title };

        [Test]
        public void Keeps_the_first_three_spaced_out_and_drops_switched_off_topics()
        {
            var plan = NudgePlanner.Plan(new[]
            {
                N("island", Noon + 2 * Hour, "crops"),
                N("island", Noon + 2 * Hour + 10 * Minute, "workshops"),   // too close to crops
                N("rewards", Noon + 1 * Hour, "gift"),
                N("raids", Noon + 4 * Hour, "shield"),
                N("offers", Noon + 3 * Hour, "deal"),
                N("island", Noon + 5 * Minute, "soon"),                   // not worth a notification
            }, Noon, 0, 22, 8, topic => topic != "offers");
            CollectionAssert.AreEqual(new[] { "gift", "crops", "shield" }, plan.Select(n => n.Title).ToList());
        }

        [Test]
        public void Moves_a_reminder_out_of_quiet_hours_in_local_time()
        {
            // 23:30 UTC is 23:30 in London (quiet) → 08:00 next morning; but 08:30 in Tokyo, fine there
            var late = Noon + 11 * Hour + 30 * Minute;
            var london = NudgePlanner.Plan(new[] { N("island", late) }, Noon, 0, 22, 8, _ => true).Single();
            Assert.AreEqual(new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), london.AtMs);
            var tokyo = NudgePlanner.Plan(new[] { N("island", late) }, Noon, 540, 22, 8, _ => true).Single();
            Assert.AreEqual(late, tokyo.AtMs);
        }

        [Test]
        public void Quiet_hours_wrap_midnight()
        {
            Assert.IsTrue(NudgePlanner.Quiet(23, 22, 8));
            Assert.IsTrue(NudgePlanner.Quiet(3, 22, 8));
            Assert.IsFalse(NudgePlanner.Quiet(8, 22, 8));
            Assert.IsFalse(NudgePlanner.Quiet(3, 5, 5));
        }
    }
}
