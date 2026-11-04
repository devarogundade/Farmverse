#nullable enable
using System;

namespace Farmverse.Contracts
{
    /// <summary>
    /// A callable function: its deployed name and how to parse its response data.
    /// The generated <see cref="Callables"/> table holds one per function, so the
    /// client can only call a function that exists, with the request type it takes.
    /// </summary>
    public sealed class CallableSpec<TRequest, TResponse>
        where TRequest : IWireObject
        where TResponse : IWireObject
    {
        private readonly Func<object?, TResponse> _parse;

        public CallableSpec(string name, Func<object?, TResponse> parse)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _parse = parse ?? throw new ArgumentNullException(nameof(parse));
        }

        /// <summary>The deployed function name, e.g. <c>actions-submit</c>.</summary>
        public string Name { get; }

        /// <summary>Parse the <c>data</c> of a successful envelope.</summary>
        public TResponse ParseResponse(object? data) => _parse(data);

        public override string ToString() => Name;
    }
}
