#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Firebase;
using Firebase.Functions;

namespace Farmverse.Infrastructure.Firebase
{
    /// <summary>
    /// <see cref="ICallableTransport"/> on Cloud Functions callables in the game's
    /// region. Only transport is handled here; the envelope, refusals and the clock
    /// are engine-free in Application.CallableClient.
    /// The Functions instance is looked up on the first call, never at
    /// composition: touching the default app before the platform step would
    /// create it ahead of App Check and the dependency check.
    /// </summary>
    public sealed class FunctionsCallableTransport : ICallableTransport
    {
        private readonly FirebaseEnvironment _environment;
        private FirebaseFunctions? _functions;

        public FunctionsCallableTransport(FirebaseEnvironment environment)
        {
            _environment = environment;
        }

        // the same instance the platform step pointed at the emulator (instances are per app and region)
        private FirebaseFunctions Functions => _functions ??= FirebaseFunctions.GetInstance(FirebaseApp.DefaultInstance, _environment.FunctionsRegion);

        /// <summary>
        /// The local emulator starts a fresh Node worker for a function's first call, and on a
        /// cold Windows disk that alone can take most of a minute: give local calls that long.
        /// </summary>
        public static readonly TimeSpan EmulatorTimeout = TimeSpan.FromSeconds(90);

        public async Task<object?> Invoke(string name, object payload, TimeSpan timeout, CancellationToken cancellation)
        {
            if (_environment.UsesEmulators && timeout < EmulatorTimeout) timeout = EmulatorTimeout;
            var call = Functions.GetHttpsCallable(name).CallAsync(payload);
            var delay = Task.Delay(timeout, cancellation);
            var finished = await Task.WhenAny(call, delay);
            if (finished != call)
            {
                if (cancellation.IsCancellationRequested) throw new TransportException(TransportFailure.Cancelled, name + " cancelled");
                throw new TransportException(TransportFailure.Unavailable, name + " timed out after " + timeout.TotalSeconds + "s");
            }
            try
            {
                var result = await call;
                return Values.ToWire(result.Data);
            }
            catch (FunctionsException error)
            {
                throw new TransportException(FailureOf(error.ErrorCode), name + ": " + error.ErrorCode, error);
            }
            catch (AggregateException error) when (error.GetBaseException() is FunctionsException functions)
            {
                throw new TransportException(FailureOf(functions.ErrorCode), name + ": " + functions.ErrorCode, functions);
            }
            catch (Exception error) when (!(error is TransportException))
            {
                throw new TransportException(TransportFailure.Unavailable, name + " failed", error);
            }
        }

        private static TransportFailure FailureOf(FunctionsErrorCode code)
        {
            switch (code)
            {
                case FunctionsErrorCode.Unavailable:
                case FunctionsErrorCode.DeadlineExceeded:
                case FunctionsErrorCode.ResourceExhausted:
                    return TransportFailure.Unavailable;
                case FunctionsErrorCode.Unauthenticated:
                case FunctionsErrorCode.PermissionDenied:
                    return TransportFailure.Unauthenticated;
                case FunctionsErrorCode.Cancelled:
                    return TransportFailure.Cancelled;
                default:
                    return TransportFailure.Internal;
            }
        }
    }
}
