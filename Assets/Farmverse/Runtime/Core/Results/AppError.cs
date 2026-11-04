#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Farmverse.Contracts;

namespace Farmverse.Core.Results
{
    /// <summary>
    /// An expected failure: a code from the contract plus the string parameters its
    /// localized message needs (see Docs/architecture/05 §4).
    ///
    /// Programmer errors are exceptions; anything a player can cause or a network
    /// can produce is an <see cref="AppError"/> carried in a <see cref="Result"/>.
    /// </summary>
    public sealed class AppError
    {
        private static readonly IReadOnlyDictionary<string, string> NoParams =
            new Dictionary<string, string>(0);

        private AppError(ErrorCode code, IReadOnlyDictionary<string, string> parameters,
                         long? retryAfterMs, Exception? cause)
        {
            Code = code;
            Params = parameters;
            RetryAfterMs = retryAfterMs;
            Cause = cause;
        }

        /// <summary>What went wrong.</summary>
        public ErrorCode Code { get; }

        /// <summary>Localization arguments, always strings.</summary>
        public IReadOnlyDictionary<string, string> Params { get; }

        /// <summary>When retrying makes sense (rate limits, maintenance).</summary>
        public long? RetryAfterMs { get; }

        /// <summary>The underlying exception, for logs only. Never shown to players.</summary>
        public Exception? Cause { get; }

        /// <summary>The localization key of the message, e.g. <c>error.INSUFFICIENT_COINS</c>.</summary>
        public string MessageKey => ErrorCatalogue.MessageKey(Code);

        public static AppError Of(ErrorCode code) => new AppError(code, NoParams, null, null);

        public static AppError Of(ErrorCode code, params (string Key, string Value)[] parameters)
        {
            if (parameters == null || parameters.Length == 0)
            {
                return Of(code);
            }
            var map = new Dictionary<string, string>(parameters.Length);
            foreach (var (key, value) in parameters)
            {
                map[key] = value ?? string.Empty;
            }
            return new AppError(code, map, null, null);
        }

        /// <summary>A failure caused by an exception (network, SDK, unexpected).</summary>
        public static AppError FromException(ErrorCode code, Exception cause) =>
            new AppError(code, NoParams, null, cause ?? throw new ArgumentNullException(nameof(cause)));

        /// <summary>A refusal the server sent.</summary>
        public static AppError FromBody(ErrorBody body)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }
            var map = body.Params.Count == 0 ? NoParams : new Dictionary<string, string>(body.Params);
            return new AppError(body.Code, map, body.RetryAfterMs, null);
        }

        /// <summary>A parameter value, or an empty string when absent.</summary>
        public string Param(string key) =>
            Params.TryGetValue(key, out var value) ? value : string.Empty;

        public override string ToString()
        {
            var builder = new StringBuilder(CodeName(Code));
            if (Params.Count > 0)
            {
                builder.Append(" {");
                var first = true;
                foreach (var pair in Params)
                {
                    builder.Append(first ? " " : ", ").Append(pair.Key).Append('=').Append(pair.Value);
                    first = false;
                }
                builder.Append(" }");
            }
            if (Cause != null)
            {
                builder.Append(" (").Append(Cause.GetType().Name).Append(": ").Append(Cause.Message).Append(')');
            }
            return builder.ToString();
        }

        /// <summary>The wire name of a code; <see cref="ErrorCode.Unknown"/> formats as <c>UNKNOWN</c>.</summary>
        public static string CodeName(ErrorCode code) =>
            code == ErrorCode.Unknown ? "UNKNOWN" : ErrorCodeWire.ToWire(code);
    }
}
