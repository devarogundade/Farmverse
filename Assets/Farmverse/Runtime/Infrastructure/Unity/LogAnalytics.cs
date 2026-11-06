#nullable enable
using System.Collections.Generic;
using System.Linq;
using Farmverse.Application.Ports;
using Farmverse.Core.Logging;

namespace Farmverse.Infrastructure.Unity
{
    /// <summary>
    /// Analytics as log lines (a web analytics provider can replace it behind IAnalytics): events
    /// are logged and left as crash-report breadcrumbs, so call sites and the event
    /// taxonomy are final today and only this adapter changes.
    /// </summary>
    public sealed class LogAnalytics : IAnalytics
    {
        private readonly ICrashReporter _crash;
        private readonly ILog _log;

        public LogAnalytics(ICrashReporter crash, ILog log)
        {
            _crash = crash;
            _log = log.For("Analytics");
        }

        public void Event(string name, IReadOnlyDictionary<string, object>? parameters = null)
        {
            var text = parameters == null ? name : name + " " + string.Join(" ", parameters.Select(p => p.Key + "=" + p.Value));
            _crash.Breadcrumb("event:" + text);
            _log.Debug(text);
        }

        public void SetUserProperty(string name, string value) => _crash.SetKey("prop." + name, value);
    }
}
