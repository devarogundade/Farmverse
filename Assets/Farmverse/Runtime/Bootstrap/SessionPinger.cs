#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Farmverse.Application.Features;
using Farmverse.Application.Ports;
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
    /// the island ran, and books the island's own reminders on the phone when the game goes to the
    /// background (crops ripe, workshops done, free gift, shield ending), cancelling them on return.
    /// </summary>
    public sealed class SessionPinger : MonoBehaviour
    {
        private const float PingSeconds = 60f;
        private const int QuietStart = 22, QuietEnd = 8;

        private ServerCalls? _server;
        private Func<bool> _ready = () => false;
        private ILocalNotifications? _local;
        private ShopService? _shop;
        private float _next;
        private bool _busy;
        private bool _ack;

        public void Init(ServerCalls server, Func<bool> ready, ILocalNotifications local, ShopService shop)
        {
            _server = server;
            _ready = ready;
            _local = local;
            _shop = shop;
            RaidSchedule.Finished -= OnFinished;
            RaidSchedule.Finished += OnFinished;
            RaidSchedule.Applied -= OnApplied;
            RaidSchedule.Applied += OnApplied;
            local.CancelAll();
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
            if (_local?.TakeOpenedRoute() is { } route) GameUi.PendingRoute = route;
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
            if (paused) BookReminders();
            else
            {
                _local?.CancelAll();
                _next = 0;   // ping straight away: we're back
            }
        }

        private void OnApplicationQuit() => BookReminders();

        /// <summary>The island's own timers, as reminders the phone shows by itself.</summary>
        private void BookReminders()
        {
            if (_local == null || PlayerPrefs.GetInt("fv_push", 0) != 1) return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var list = new List<LocalNudge>();
            var host = FindAnyObjectByType<IslandEconomyHost>();
            if (host != null)
            {
                var eco = host.Economy;
                var growing = eco.Plots.Values.Where(p => p.Crop != null && p.ReadyAt * 1000 > now).ToList();
                if (growing.Count > 0)
                    list.Add(new LocalNudge { Topic = "island", Route = "play", AtMs = (long)(growing.Max(p => p.ReadyAt) * 1000),
                        Title = Loc.Of("Your crops are ready"), Body = Loc.Of("The fields are ripe. Harvest them before they spoil!") });
                var busy = eco.Machines.Values.Where(m => m.BusyUntil * 1000 > now).ToList();
                if (busy.Count > 0)
                    list.Add(new LocalNudge { Topic = "island", Route = "play", AtMs = (long)(busy.Max(m => m.BusyUntil) * 1000),
                        Title = Loc.Of("Your workshops are done"), Body = Loc.Of("Fresh goods are waiting to be collected and sold.") });
            }
            if (_shop?.NextGiftAt is { } gift && gift > now)
                list.Add(new LocalNudge { Topic = "rewards", Route = "shop", AtMs = gift,
                    Title = Loc.Of("Free gift ready"), Body = Loc.Of("Your free coins are waiting in the shop.") });
            var shield = _shop?.ActiveUntil("shield") ?? 0;
            if (shield - 30 * 60_000 > now)
                list.Add(new LocalNudge { Topic = "raids", Route = "shop", AtMs = shield - 30 * 60_000,
                    Title = Loc.Of("Raid Shield ends soon"), Body = Loc.Of("Your shield ends in 30 minutes. Raiders are watching!") });

            var plan = NudgePlanner.Plan(list, now, OffsetMin, QuietStart, QuietEnd, topic => PlayerPrefs.GetInt("fv_push_" + topic, 1) == 1);
            _local.Replace(plan);
        }
    }
}
