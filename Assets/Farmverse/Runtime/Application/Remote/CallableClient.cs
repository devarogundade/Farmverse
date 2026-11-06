#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Logging;
using Farmverse.Core.Results;
using Farmverse.Core.Time;

namespace Farmverse.Application.Remote
{
    /// <summary>
    /// Calls a contract callable and unwraps its envelope (05 §2):
    /// <c>{ ok, data | error, serverTimeMs, requestId }</c>. Every successful round
    /// trip feeds the <see cref="ServerClock"/>. Business refusals come back as
    /// <see cref="AppError"/>s with the server's code and params; transport failures
    /// map to client codes (NETWORK_REQUIRED, TIMEOUT, CANCELLED).
    /// </summary>
    public sealed class CallableClient
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

        private readonly ICallableTransport _transport;
        private readonly ServerClock _clock;
        private readonly ILog _log;

        /// <summary>A call was refused because the server is in maintenance (it started after the session did).</summary>
        public event Action? Maintenance;

        public CallableClient(ICallableTransport transport, ServerClock clock, ILog log)
        {
            _transport = transport;
            _clock = clock;
            _log = log.For("Callable");
        }

        public async Task<Result<TResponse>> Call<TRequest, TResponse>(
            CallableSpec<TRequest, TResponse> spec,
            TRequest request,
            CancellationToken cancellation,
            TimeSpan? timeout = null)
            where TRequest : IWireObject
            where TResponse : IWireObject
        {
            var sentAt = _clock.Stamp();
            object? raw;
            try
            {
                raw = await _transport.Invoke(spec.Name, request.ToWire(), timeout ?? DefaultTimeout, cancellation);
            }
            catch (TransportException error)
            {
                _log.Warn("transport failure", null, error);
                return AppError.FromException(CodeFor(error.Failure), error);
            }
            catch (OperationCanceledException error)
            {
                return AppError.FromException(cancellation.IsCancellationRequested ? ErrorCode.Cancelled : ErrorCode.Timeout, error);
            }

            try
            {
                var envelope = Wire.AsMap(raw, spec.Name);
                if (envelope.TryGetValue("serverTimeMs", out var time) && time != null)
                {
                    _clock.Observe(Wire.AsLong(time, "serverTimeMs"), sentAt);
                }
                var ok = Wire.AsBool(envelope.TryGetValue("ok", out var okValue) ? okValue : null, "ok");
                if (!ok)
                {
                    var body = ErrorBody.FromWire(envelope.TryGetValue("error", out var errorValue) ? errorValue : null);
                    var refused = AppError.FromBody(body);
                    if (refused.Code == ErrorCode.Maintenance) Maintenance?.Invoke();
                    return refused;
                }
                return Result<TResponse>.Ok(spec.ParseResponse(envelope.TryGetValue("data", out var data) ? data : null));
            }
            catch (WireFormatException error)
            {
                // the server broke its own contract: report, never crash the caller
                _log.Error("malformed envelope", error);
                return AppError.FromException(ErrorCode.Internal, error);
            }
        }

        private static ErrorCode CodeFor(TransportFailure failure)
        {
            switch (failure)
            {
                case TransportFailure.Unavailable: return ErrorCode.NetworkRequired;
                case TransportFailure.Unauthenticated: return ErrorCode.Unauthenticated;
                case TransportFailure.Cancelled: return ErrorCode.Cancelled;
                default: return ErrorCode.Internal;
            }
        }
    }
}
