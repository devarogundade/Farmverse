#nullable enable
using System;
using System.Collections.Generic;
using Farmverse.Application.Ports;
using Farmverse.Application.Session;
using Farmverse.Core.Reactive;

namespace Farmverse.Application.Features
{
    /// <summary>
    /// The analytics taxonomy (09 §4): every event name and parameter the game
    /// sends, in one place. Names are snake_case, at most 40 characters, with at
    /// most 10 parameters, so they are valid for Firebase/GA4 as they are. Nothing
    /// personal is sent: no names, no free text, uid only via the SDK's user id.
    /// </summary>
    public static class AnalyticsEvents
    {
        public const string SessionReady = "session_ready";
        public const string SessionFailed = "session_failed";

        public static readonly IReadOnlyCollection<string> All = new[] { SessionReady, SessionFailed };

        public static class Params
        {
            public const string State = "state";
            public const string ColdStartMs = "cold_start_ms";
        }
    }

    /// <summary>Turns the session's milestones into analytics events.</summary>
    public sealed class GameAnalytics : IDisposable
    {
        private readonly CompositeDisposable _scope = new CompositeDisposable();
        private readonly IAnalytics _analytics;
        private bool _readyOnce;

        public GameAnalytics(IAnalytics analytics, SessionService session, Func<long> sinceLaunchMs)
        {
            _analytics = analytics;
            _scope.Add(session.State.Subscribe(state =>
            {
                if (state == SessionState.Ready && !_readyOnce)
                {
                    _readyOnce = true;
                    _analytics.Event(AnalyticsEvents.SessionReady, new Dictionary<string, object> { [AnalyticsEvents.Params.ColdStartMs] = sinceLaunchMs() });
                }
                else if (state == SessionState.Failed || state == SessionState.Offline || state == SessionState.Maintenance || state == SessionState.UpdateRequired)
                {
                    _analytics.Event(AnalyticsEvents.SessionFailed, new Dictionary<string, object> { [AnalyticsEvents.Params.State] = state.ToString() });
                }
            }, emitCurrent: false));
        }

        public void Dispose() => _scope.Dispose();
    }
}
