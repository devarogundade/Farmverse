#nullable enable
using System.Diagnostics;

namespace Farmverse.Core.Time
{
    /// <summary>
    /// A clock that only moves forward at a steady rate and ignores changes to the
    /// device's date and time. The server clock measures elapsed time with it.
    /// </summary>
    public interface IMonotonicTimer
    {
        /// <summary>Milliseconds since an arbitrary fixed origin.</summary>
        long ElapsedMs { get; }
    }

    public sealed class StopwatchTimer : IMonotonicTimer
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public long ElapsedMs => _stopwatch.ElapsedMilliseconds;
    }

    /// <summary>A monotonic timer tests move by hand.</summary>
    public sealed class ManualTimer : IMonotonicTimer
    {
        public long ElapsedMs { get; set; }

        public void Advance(long ms) => ElapsedMs += ms;
    }
}
