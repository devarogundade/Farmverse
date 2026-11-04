#nullable enable
using System.Linq;
using Farmverse.Core.Logging;
using UnityEngine;

namespace Farmverse.Infrastructure.Unity
{
    /// <summary>Writes structured log entries to the Unity console (and so to device logs).</summary>
    public sealed class UnityLogSink : ILogSink
    {
        public void Write(LogEntry entry)
        {
            var fields = entry.Fields == null || entry.Fields.Count == 0 ? string.Empty : " " + string.Join(" ", entry.Fields.Select(f => f.Key + "=" + f.Value));
            var line = "[" + entry.Category + "] " + entry.Message + fields;
            switch (entry.Level)
            {
                case LogLevel.Error:
                    if (entry.Exception != null) Debug.LogException(entry.Exception);
                    Debug.LogError(line);
                    break;
                case LogLevel.Warning:
                    // a warning's cause is usually the whole story (a refused call, a timeout)
                    Debug.LogWarning(entry.Exception == null ? line : line + "\n" + Causes(entry.Exception));
                    break;
                default:
                    Debug.Log(line);
                    break;
            }
        }

        /// <summary>The exception and its inner causes, one per line: type and message, no stack.</summary>
        private static string Causes(System.Exception error)
        {
            var lines = new System.Collections.Generic.List<string>();
            for (var e = error; e != null && lines.Count < 6; e = e.InnerException)
                lines.Add("  " + e.GetType().Name + ": " + e.Message);
            return string.Join("\n", lines);
        }
    }
}
