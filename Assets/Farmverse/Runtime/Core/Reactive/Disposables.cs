#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Farmverse.Core.Reactive
{
    /// <summary>Factory helpers for <see cref="IDisposable"/> handles.</summary>
    public static class Disposable
    {
        /// <summary>A handle that does nothing when disposed.</summary>
        public static readonly IDisposable Empty = new ActionDisposable(null);

        /// <summary>A handle that runs <paramref name="onDispose"/> exactly once.</summary>
        public static IDisposable Create(Action onDispose) =>
            new ActionDisposable(onDispose ?? throw new ArgumentNullException(nameof(onDispose)));

        private sealed class ActionDisposable : IDisposable
        {
            private Action? _onDispose;

            public ActionDisposable(Action? onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose() => Interlocked.Exchange(ref _onDispose, null)?.Invoke();
        }
    }

    /// <summary>
    /// A bag of subscriptions disposed together — what a presenter clears when its
    /// screen hides. Adding to a disposed bag disposes the item immediately.
    /// </summary>
    public sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _items = new List<IDisposable>();
        private bool _disposed;

        public int Count => _items.Count;

        public T Add<T>(T item) where T : IDisposable
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }
            if (_disposed)
            {
                item.Dispose();
                return item;
            }
            _items.Add(item);
            return item;
        }

        /// <summary>Dispose every item but keep the bag usable.</summary>
        public void Clear()
        {
            var items = _items.ToArray();
            _items.Clear();
            DisposeAll(items);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Clear();
        }

        private static void DisposeAll(IDisposable[] items)
        {
            List<Exception>? errors = null;
            // reverse order: later subscriptions may depend on earlier ones
            for (var i = items.Length - 1; i >= 0; i--)
            {
                try
                {
                    items[i].Dispose();
                }
                catch (Exception error)
                {
                    (errors ??= new List<Exception>()).Add(error);
                }
            }
            if (errors != null)
            {
                throw new AggregateException("One or more subscriptions failed to dispose.", errors);
            }
        }
    }
}
