#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Farmverse.Application.Ports
{
    /// <summary>Why a callable never produced an envelope.</summary>
    public enum TransportFailure
    {
        /// <summary>No network, DNS failure, or the request timed out.</summary>
        Unavailable = 1,
        /// <summary>App Check or Auth rejected the call before it reached the handler.</summary>
        Unauthenticated = 2,
        /// <summary>The function crashed or returned something that is not an envelope.</summary>
        Internal = 3,
        /// <summary>The caller cancelled.</summary>
        Cancelled = 4,
    }

    public sealed class TransportException : Exception
    {
        public TransportException(TransportFailure failure, string message, Exception? inner = null)
            : base(message, inner)
        {
            Failure = failure;
        }

        public TransportFailure Failure { get; }
    }

    /// <summary>
    /// Invoke a Cloud Function by name with a wire payload and return the raw wire
    /// result (the envelope). Infrastructure implements it with the Functions SDK;
    /// everything above — envelope, errors, clock, retries — is engine-free.
    /// </summary>
    public interface ICallableTransport
    {
        /// <exception cref="TransportException">When no envelope came back.</exception>
        Task<object?> Invoke(string name, object payload, TimeSpan timeout, CancellationToken cancellation);
    }
}
