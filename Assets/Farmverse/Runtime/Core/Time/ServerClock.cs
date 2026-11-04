#nullable enable
using System;
using System.Collections.Generic;
using Farmverse.Core.Reactive;

namespace Farmverse.Core.Time
{
    /// <summary>
    /// The server's time, estimated on the device.
    ///
    /// Every callable response carries <c>serverTimeMs</c>. Each sample gives an
    /// offset between the server's clock and the device's monotonic timer,
    /// corrected by half the round trip. The clock uses the median of the most
    /// recent samples, so one slow response cannot move it, and it is measured
    /// against a monotonic timer, so changing the device's date does nothing.
    ///
    /// Until the first sample arrives the device's wall clock is used and
    /// <see cref="IsSynced"/> is false: timers shown then are estimates.
    /// </summary>
    public sealed class ServerClock : IClock
    {
        /// <summary>Samples with a longer round trip are too imprecise to use.</summary>
        public static readonly TimeSpan MaxRoundTrip = TimeSpan.FromSeconds(10);

        private const int Window = 5;

        private readonly IMonotonicTimer _timer;
        private readonly List<long> _offsets = new List<long>(Window);
        private readonly long[] _scratch = new long[Window];
        private readonly ReactiveProperty<bool> _isSynced = new ReactiveProperty<bool>(false);
        private long _offsetMs;

        public ServerClock(IMonotonicTimer timer, IClock deviceClock)
        {
            _timer = timer ?? throw new ArgumentNullException(nameof(timer));
            if (deviceClock == null)
            {
                throw new ArgumentNullException(nameof(deviceClock));
            }
            _offsetMs = deviceClock.NowMs - _timer.ElapsedMs;
        }

        public long NowMs => _timer.ElapsedMs + _offsetMs;

        /// <summary>True once at least one server sample has been taken.</summary>
        public IReadOnlyReactiveProperty<bool> IsSynced => _isSynced;

        /// <summary>Read the monotonic timer, to stamp when a request was sent.</summary>
        public long Stamp() => _timer.ElapsedMs;

        /// <summary>
        /// Record a server time sample.
        /// </summary>
        /// <param name="serverTimeMs">The <c>serverTimeMs</c> of the response.</param>
        /// <param name="sentAt">The <see cref="Stamp"/> taken just before the request.</param>
        /// <returns>False when the sample was rejected as too imprecise.</returns>
        public bool Observe(long serverTimeMs, long sentAt)
        {
            var receivedAt = _timer.ElapsedMs;
            var roundTrip = receivedAt - sentAt;
            if (roundTrip < 0 || roundTrip > (long)MaxRoundTrip.TotalMilliseconds || serverTimeMs <= 0)
            {
                return false;
            }

            // the server stamped its time roughly halfway through the round trip
            var offset = serverTimeMs + roundTrip / 2 - receivedAt;
            if (_offsets.Count == Window)
            {
                _offsets.RemoveAt(0);
            }
            _offsets.Add(offset);
            _offsetMs = Median();
            _isSynced.Value = true;
            return true;
        }

        private long Median()
        {
            var count = _offsets.Count;
            _offsets.CopyTo(_scratch, 0);
            Array.Sort(_scratch, 0, count);
            return count % 2 == 1
                ? _scratch[count / 2]
                : (_scratch[count / 2 - 1] + _scratch[count / 2]) / 2;
        }
    }
}
