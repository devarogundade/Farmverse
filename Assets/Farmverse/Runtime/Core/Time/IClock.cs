#nullable enable
using System;

namespace Farmverse.Core.Time
{
    /// <summary>
    /// The time the game runs on, in Unix epoch milliseconds — the same unit every
    /// game timestamp uses on the wire (Docs/architecture/04, conventions).
    /// </summary>
    public interface IClock
    {
        /// <summary>Milliseconds since the Unix epoch.</summary>
        long NowMs { get; }
    }


    /// <summary>The device's wall clock. Only for things that may legitimately use it (logs).</summary>
    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();

        private SystemClock()
        {
        }

        public long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>A clock tests move by hand.</summary>
    public sealed class ManualClock : IClock
    {
        public ManualClock(long startMs = 1_800_000_000_000L)
        {
            NowMs = startMs;
        }

        public long NowMs { get; private set; }

        public void Advance(TimeSpan by)
        {
            if (by < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(by), "time does not go backwards");
            }
            NowMs += (long)by.TotalMilliseconds;
        }

        public void Set(long nowMs) => NowMs = nowMs;
    }
}
