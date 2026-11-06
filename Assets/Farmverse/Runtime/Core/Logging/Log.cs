#nullable enable
using System;
using System.Collections.Generic;

namespace Farmverse.Core.Logging
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    /// <summary>One structured log entry. Fields are machine-readable key/values.</summary>
    public sealed class LogEntry
    {
        public LogEntry(LogLevel level, string category, string message,
                        IReadOnlyDictionary<string, object?>? fields, Exception? exception)
        {
            Level = level;
            Category = category;
            Message = message;
            Fields = fields;
            Exception = exception;
        }

        public LogLevel Level { get; }

        /// <summary>Which subsystem wrote it, e.g. <c>ActionQueue</c>.</summary>
        public string Category { get; }

        public string Message { get; }

        public IReadOnlyDictionary<string, object?>? Fields { get; }

        public Exception? Exception { get; }
    }

    /// <summary>Where log entries go: the Unity console, crash-report breadcrumbs, a test list.</summary>
    public interface ILogSink
    {
        void Write(LogEntry entry);
    }

    /// <summary>
    /// A category-scoped logger. Never log personal data: uids are fine, display
    /// names and emails are not (Docs/architecture/03 §7).
    /// </summary>
    public interface ILog
    {
        bool IsEnabled(LogLevel level);

        void Write(LogLevel level, string message, IReadOnlyDictionary<string, object?>? fields = null,
                   Exception? exception = null);

        /// <summary>A logger for a sub-category that writes to the same sinks.</summary>
        ILog For(string category);
    }

    public static class LogExtensions
    {
        public static void Debug(this ILog log, string message, IReadOnlyDictionary<string, object?>? fields = null)
        {
            if (log.IsEnabled(LogLevel.Debug)) log.Write(LogLevel.Debug, message, fields);
        }

        public static void Info(this ILog log, string message, IReadOnlyDictionary<string, object?>? fields = null) =>
            log.Write(LogLevel.Info, message, fields);

        public static void Warn(this ILog log, string message, IReadOnlyDictionary<string, object?>? fields = null,
                                Exception? exception = null) =>
            log.Write(LogLevel.Warning, message, fields, exception);

        public static void Error(this ILog log, string message, Exception? exception = null,
                                 IReadOnlyDictionary<string, object?>? fields = null) =>
            log.Write(LogLevel.Error, message, fields, exception);
    }

    /// <summary>The standard logger: filters by level and fans out to sinks.</summary>
    public sealed class Logger : ILog
    {
        private readonly IReadOnlyList<ILogSink> _sinks;
        private readonly LogLevel _minimum;
        private readonly string _category;

        public Logger(IReadOnlyList<ILogSink> sinks, LogLevel minimum, string category = "Farmverse")
        {
            _sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));
            _minimum = minimum;
            _category = category ?? throw new ArgumentNullException(nameof(category));
        }

        public bool IsEnabled(LogLevel level) => level >= _minimum;

        public void Write(LogLevel level, string message, IReadOnlyDictionary<string, object?>? fields = null,
                          Exception? exception = null)
        {
            if (!IsEnabled(level))
            {
                return;
            }
            var entry = new LogEntry(level, _category, message, fields, exception);
            for (var i = 0; i < _sinks.Count; i++)
            {
                try
                {
                    _sinks[i].Write(entry);
                }
                catch
                {
                    // a broken sink must never break the game
                }
            }
        }

        public ILog For(string category) => new Logger(_sinks, _minimum, _category + "." + category);
    }

    /// <summary>A logger that discards everything.</summary>
    public sealed class NullLog : ILog
    {
        public static readonly NullLog Instance = new NullLog();

        private NullLog()
        {
        }

        public bool IsEnabled(LogLevel level) => false;

        public void Write(LogLevel level, string message, IReadOnlyDictionary<string, object?>? fields = null,
                          Exception? exception = null)
        {
        }

        public ILog For(string category) => this;
    }

}
