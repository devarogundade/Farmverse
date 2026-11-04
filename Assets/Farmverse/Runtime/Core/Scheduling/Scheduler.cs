#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Farmverse.Core.Scheduling
{
    /// <summary>
    /// Delays without a dependency on Unity, so debounce and retry backoff are
    /// testable. The Unity adapter uses <c>Awaitable.WaitForSecondsAsync</c>; tests
    /// use <see cref="ManualScheduler"/>.
    /// </summary>
    public interface IScheduler
    {
        /// <summary>Complete after <paramref name="delay"/>, or cancel with the token.</summary>
        Task Delay(TimeSpan delay, CancellationToken cancellation);
    }

    /// <summary>A scheduler backed by <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
    public sealed class TaskDelayScheduler : IScheduler
    {
        public static readonly TaskDelayScheduler Instance = new TaskDelayScheduler();

        private TaskDelayScheduler()
        {
        }

        public Task Delay(TimeSpan delay, CancellationToken cancellation) =>
            delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellation);
    }

    /// <summary>
    /// A scheduler driven by the test. Pending delays complete synchronously, in
    /// due order, when <see cref="Advance"/> passes their due time.
    /// </summary>
    public sealed class ManualScheduler : IScheduler
    {
        private readonly List<Pending> _pending = new List<Pending>();
        private long _sequence;

        public TimeSpan Now { get; private set; }

        public int PendingCount => _pending.Count;

        public Task Delay(TimeSpan delay, CancellationToken cancellation)
        {
            if (cancellation.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellation);
            }
            if (delay <= TimeSpan.Zero)
            {
                return Task.CompletedTask;
            }
            var source = new TaskCompletionSource<bool>();
            var pending = new Pending(Now + delay, _sequence++, source);
            _pending.Add(pending);
            if (cancellation.CanBeCanceled)
            {
                pending.Registration = cancellation.Register(() =>
                {
                    _pending.Remove(pending);
                    source.TrySetCanceled(cancellation);
                });
            }
            return source.Task;
        }

        public void Advance(TimeSpan by)
        {
            if (by < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(by));
            }
            var target = Now + by;
            while (true)
            {
                Pending? next = null;
                foreach (var pending in _pending)
                {
                    if (pending.Due <= target &&
                        (next == null || pending.Due < next.Due ||
                         (pending.Due == next.Due && pending.Sequence < next.Sequence)))
                    {
                        next = pending;
                    }
                }
                if (next == null)
                {
                    break;
                }
                _pending.Remove(next);
                Now = next.Due;
                next.Registration.Dispose();
                next.Source.TrySetResult(true);
            }
            Now = target;
        }

        private sealed class Pending
        {
            public Pending(TimeSpan due, long sequence, TaskCompletionSource<bool> source)
            {
                Due = due;
                Sequence = sequence;
                Source = source;
            }

            public TimeSpan Due { get; }
            public long Sequence { get; }
            public TaskCompletionSource<bool> Source { get; }
            public CancellationTokenRegistration Registration { get; set; }
        }
    }
}
