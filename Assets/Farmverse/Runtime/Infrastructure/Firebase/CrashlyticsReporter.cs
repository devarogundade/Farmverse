#nullable enable
using System;
using System.Collections.Generic;
using Farmverse.Application.Ports;
using Firebase.Crashlytics;

namespace Farmverse.Infrastructure.Firebase
{
    /// <summary>
    /// Crashlytics: uid as user id (never email), keys, breadcrumbs, non-fatals (11 §5).
    /// Calls made before <see cref="Enable"/> (the boot's first breadcrumbs, before
    /// the platform step has set up App Check and checked dependencies) are held and
    /// replayed, because touching Crashlytics would create the Firebase app early.
    /// </summary>
    public sealed class CrashlyticsReporter : ICrashReporter
    {
        private const int MaxHeld = 64;
        private readonly object _gate = new object();
        private List<Action>? _held = new List<Action>();

        /// <summary>The platform step succeeded: replay what was held and report directly from now on.</summary>
        public void Enable()
        {
            List<Action>? held;
            lock (_gate)
            {
                held = _held;
                _held = null;
            }
            if (held != null) foreach (var call in held) call();
        }

        public void SetUser(string uid) => Run(() => Crashlytics.SetUserId(uid));

        public void SetKey(string key, string value) => Run(() => Crashlytics.SetCustomKey(key, value));

        public void Breadcrumb(string message) => Run(() => Crashlytics.Log(message));

        public void RecordNonFatal(string name, string message, IReadOnlyDictionary<string, string>? fields = null) => Run(() =>
        {
            if (fields != null) foreach (var pair in fields) Crashlytics.SetCustomKey(name + "." + pair.Key, pair.Value);
            Crashlytics.LogException(new NonFatal(name + ": " + message));
        });

        private void Run(Action call)
        {
            lock (_gate)
            {
                if (_held != null)
                {
                    if (_held.Count < MaxHeld) _held.Add(call);
                    return;
                }
            }
            call();
        }

        private sealed class NonFatal : Exception
        {
            public NonFatal(string message) : base(message) { }
        }
    }
}
