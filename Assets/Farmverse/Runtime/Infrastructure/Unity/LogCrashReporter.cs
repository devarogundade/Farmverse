#nullable enable
using System.Collections.Generic;
using System.Linq;
using Farmverse.Application.Ports;
using Farmverse.Core.Logging;

namespace Farmverse.Infrastructure.Unity
{
    /// <summary>
    /// The crash reporter for the web build until a browser error service is wired in (13 §4: Sentry):
    /// breadcrumbs and keys go to the debug log, non-fatals to the warning log. Never personal data.
    /// </summary>
    public sealed class LogCrashReporter : ICrashReporter
    {
        private readonly ILog _log;

        public LogCrashReporter(ILog log) => _log = log.For("Crash");

        public void SetUser(string uid) => _log.Write(LogLevel.Debug, "user " + uid);

        public void SetKey(string key, string value) => _log.Write(LogLevel.Debug, "key " + key + "=" + value);

        public void Breadcrumb(string message) => _log.Write(LogLevel.Debug, message);

        public void RecordNonFatal(string name, string message, IReadOnlyDictionary<string, string>? fields = null) =>
            _log.Write(LogLevel.Warning, name + ": " + message, fields?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value));
    }
}
