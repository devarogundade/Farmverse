#nullable enable
using System;
using Farmverse.Contracts;

namespace Farmverse.Core.Results
{
    /// <summary>
    /// The outcome of an operation that can fail in an expected way.
    ///
    /// A value type, so returning one allocates nothing on success. Use
    /// <see cref="Result{T}"/> when the success carries a value.
    /// </summary>
    public readonly struct Result
    {
        private readonly AppError? _error;

        private Result(AppError? error)
        {
            _error = error;
        }

        public static Result Ok() => default;

        public static Result Fail(AppError error) =>
            new Result(error ?? throw new ArgumentNullException(nameof(error)));

        public static Result Fail(ErrorCode code) => new Result(AppError.Of(code));

        public static Result Fail(ErrorCode code, params (string Key, string Value)[] parameters) =>
            new Result(AppError.Of(code, parameters));

        public bool IsOk => _error == null;

        public bool IsFailure => _error != null;

        /// <summary>The error. Throws when the result succeeded.</summary>
        public AppError Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

        public static implicit operator Result(AppError error) => Fail(error);

        public override string ToString() => IsOk ? "Ok" : "Fail(" + _error + ")";
    }

    /// <summary>The outcome of an operation that produces a value or fails in an expected way.</summary>
    public readonly struct Result<T>
    {
        private readonly T _value;
        private readonly AppError? _error;

        private Result(T value, AppError? error)
        {
            _value = value;
            _error = error;
        }

        public static Result<T> Ok(T value) => new Result<T>(value, null);

        public static Result<T> Fail(AppError error) =>
            new Result<T>(default!, error ?? throw new ArgumentNullException(nameof(error)));

        public static Result<T> Fail(ErrorCode code) => Fail(AppError.Of(code));

        public bool IsOk => _error == null;

        public bool IsFailure => _error != null;

        /// <summary>The value. Throws when the result failed.</summary>
        public T Value
        {
            get
            {
                if (_error != null)
                {
                    throw new InvalidOperationException("A failed result has no value: " + _error);
                }
                return _value;
            }
        }

        /// <summary>The error. Throws when the result succeeded.</summary>
        public AppError Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

        public bool TryGetValue(out T value)
        {
            value = _value;
            return _error == null;
        }

        public T ValueOr(T fallback) => _error == null ? _value : fallback;

        public Result<TOut> Map<TOut>(Func<T, TOut> map)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }
            return _error == null ? Result<TOut>.Ok(map(_value)) : Result<TOut>.Fail(_error);
        }

        public Result<TOut> Then<TOut>(Func<T, Result<TOut>> next)
        {
            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }
            return _error == null ? next(_value) : Result<TOut>.Fail(_error);
        }

        /// <summary>Drop the value, keeping success or failure.</summary>
        public Result AsResult() => _error == null ? Result.Ok() : Result.Fail(_error);

        public static implicit operator Result<T>(AppError error) => Fail(error);

        public override string ToString() => IsOk ? "Ok(" + _value + ")" : "Fail(" + _error + ")";
    }
}
