#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Farmverse.Application.Features;
using Farmverse.Contracts;
using Farmverse.Domain.Gameplay;
using Farmverse.Presentation.UI.Game;
using Farmverse.Presentation.World.Island;
using UnityEngine;

namespace Farmverse.Bootstrap
{
    /// <summary>
    /// Keeps the server told the game is open (session-ping every minute in front: no pushes while
    /// playing), brings the day's raid plan to the island (<see cref="RaidSchedule"/>), reports raids
    /// the island ran.
    /// </summary>
    public sealed class SessionPinger : MonoBehaviour
    {
        private const float PingSeconds = 60f;

        private ServerCalls? _server;
        private Func<bool> _ready = () => false;
        private float _next;
        private bool _busy;
        private bool _ack;

        public void Init(ServerCalls server, Func<bool> ready)
        {
            _server = server;
            _ready = ready;
            RaidSchedule.Finished -= OnFinished;
            RaidSchedule.Finished += OnFinished;
            RaidSchedule.Applied -= OnApplied;
            RaidSchedule.Applied += OnApplied;
        }

        private void OnFinished(string day, string outcome, int size) => _ = Finish(day, outcome, size);

        private void OnApplied()
        {
            _ack = true;
            _next = 0;
        }

        private void OnDestroy()
        {
            RaidSchedule.Finished -= OnFinished;
            RaidSchedule.Applied -= OnApplied;
        }

        private static int OffsetMin => (int)Math.Round(TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes);

        private void Update()
        {
            if (_server == null || _busy || !_ready() || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + PingSeconds;
            _ = Ping();
        }

        private async Task Ping()
        {
            _busy = true;
            try
            {
                var ack = _ack;
                var result = await _server!.SessionPing(OffsetMin, ack);
                if (!result.IsOk)
                {
                    Debug.LogWarning("[Session] ping failed: " + result.Error);
                    return;
                }
                if (ack) _ack = false;
                Apply(result.Value.Raid);
                if (result.Value.LevelGems is > 0 and var gems)
                    GameUi.PendingSay = $"+{gems} gems for reaching island level {result.Value.LevelPaid}";
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Session] ping: " + e.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task Finish(string day, string outcome, int size)
        {
            if (_server == null) return;
            var kind = outcome switch { "won" => RaidOutcomeKind.Won, "shield" => RaidOutcomeKind.Shield, "skipped" => RaidOutcomeKind.Skipped, _ => RaidOutcomeKind.Lost };
            try
            {
                var result = await _server.RaidFinish(day, kind, size);
                if (result.IsOk) Apply(result.Value.Raid);
                else Debug.LogWarning("[Raid] raid-finish failed: " + result.Error);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Raid] raid-finish: " + e.Message);
            }
        }

        private static void Apply(RaidPlan plan)
        {
            RaidOutcome? outcome = null;
            if (plan.Result is { } r)
                outcome = new RaidOutcome
                {
                    size = r.Size,
                    won = r.Won,
                    coins = (int)r.Coins,
                    stolen = r.Stolen.Select(c => new EconomySave.Count { item = c.Item, n = (int)c.N }).ToList(),
                    trampled = r.Trampled.Select(u => (int)u).ToList(),
                    ruined = r.Ruined.Select(u => (int)u).ToList(),
                };
            RaidSchedule.Set(plan.Day, plan.DueMs, plan.Done, outcome, plan.Result?.AtMs ?? 0);
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused) _next = 0;   // ping straight away: we're back
        }
    }
}
