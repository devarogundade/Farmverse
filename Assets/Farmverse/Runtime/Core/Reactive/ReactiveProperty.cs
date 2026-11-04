#nullable enable
using System;
using System.Collections.Generic;

namespace Farmverse.Core.Reactive
{
    /// <summary>A current value that tells subscribers when it changes.</summary>
    public interface IReadOnlyReactiveProperty<out T>
    {
        T Value { get; }

        /// <summary>
        /// Subscribe to changes. With <paramref name="emitCurrent"/> the handler is
        /// called immediately with the current value, which is what a view binding
        /// wants. Dispose the handle to unsubscribe.
        /// </summary>
        IDisposable Subscribe(Action<T> onChange, bool emitCurrent = true);
    }

    /// <summary>
    /// A mutable <see cref="IReadOnlyReactiveProperty{T}"/>.
    ///
    /// Not thread-safe by design: state is only mutated on the main thread (the
    /// infrastructure layer marshals SDK callbacks there). Setting an equal value
    /// does not notify. Handlers may subscribe or unsubscribe while being notified.
    /// </summary>
    public sealed class ReactiveProperty<T> : IReadOnlyReactiveProperty<T>
    {
        private readonly IEqualityComparer<T> _comparer;
        private Action<T>[] _handlers = Array.Empty<Action<T>>();
        private T _value;

        public ReactiveProperty(T initial, IEqualityComparer<T>? comparer = null)
        {
            _value = initial;
            _comparer = comparer ?? EqualityComparer<T>.Default;
        }

        public T Value
        {
            get => _value;
            set
            {
                if (_comparer.Equals(_value, value))
                {
                    return;
                }
                _value = value;
                Notify(value);
            }
        }

        public int SubscriberCount => _handlers.Length;

        public IDisposable Subscribe(Action<T> onChange, bool emitCurrent = true)
        {
            if (onChange == null)
            {
                throw new ArgumentNullException(nameof(onChange));
            }
            _handlers = Append(_handlers, onChange);
            if (emitCurrent)
            {
                onChange(_value);
            }
            return Disposable.Create(() => _handlers = Remove(_handlers, onChange));
        }

        private void Notify(T value)
        {
            // iterate a snapshot so handlers can (un)subscribe safely
            var handlers = _handlers;
            for (var i = 0; i < handlers.Length; i++)
            {
                handlers[i](value);
            }
        }

        private static Action<T>[] Append(Action<T>[] source, Action<T> item)
        {
            var result = new Action<T>[source.Length + 1];
            Array.Copy(source, result, source.Length);
            result[source.Length] = item;
            return result;
        }

        private static Action<T>[] Remove(Action<T>[] source, Action<T> item)
        {
            var index = Array.IndexOf(source, item);
            if (index < 0)
            {
                return source;
            }
            var result = new Action<T>[source.Length - 1];
            Array.Copy(source, 0, result, 0, index);
            Array.Copy(source, index + 1, result, index, source.Length - index - 1);
            return result;
        }

        public override string ToString() => "Reactive(" + _value + ")";
    }
}
