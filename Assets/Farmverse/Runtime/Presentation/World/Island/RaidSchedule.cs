#nullable enable
using System;
using System.Globalization;
using Farmverse.Domain.Gameplay;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// One raid a day. The server plans the day's raid (session-ping) and the island runs it while the
    /// player is there: a few minutes into a session, or straight away once its planned time has come.
    /// If the player stayed away the server ran it instead; its losses arrive here as <see cref="Pending"/>
    /// for the island to apply and show. Without a server (offline, no backend) the day is kept on the phone.
    /// </summary>
    public static class RaidSchedule
    {
        private const string DayPref = "fv_raid_day";
        private const string AppliedPref = "fv_raid_applied";

        /// <summary>True once the server's plan arrived this session.</summary>
        public static bool Known { get; private set; }
        public static string Day { get; private set; } = "";
        /// <summary>When the server runs the raid itself if the player isn't there (epoch ms).</summary>
        public static long DueMs { get; private set; }
        public static bool Done { get; private set; }

        /// <summary>A raid the server ran while the player was away, not applied yet.</summary>
        public static RaidOutcome? Pending { get; private set; }
        private static long _pendingAt;

        /// <summary>The game ran or skipped today's raid: day, outcome (won, lost, shield, skipped), raiders.</summary>
        public static event Action<string, string, int>? Finished;
        /// <summary>The island applied the server's raid (the server may forget it).</summary>
        public static event Action? Applied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRun()
        {
            Known = false;
            Day = "";
            DueMs = 0;
            Done = false;
            Pending = null;
            _pendingAt = 0;
            Finished = null;
            Applied = null;
        }

        /// <summary>The phone's local day (the server plans by the same offset).</summary>
        public static string Today => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>Today's raid has happened (here, on the server, or was blocked by a shield).</summary>
        public static bool DoneToday => (Known && Day == Today && Done) || PlayerPrefs.GetString(DayPref, "") == Today;

        /// <summary>The planned time has come and the raid hasn't happened: it should start now.</summary>
        public static bool DueNow => Known && Day == Today && !Done && NowMs >= DueMs;

        /// <summary>The server's plan (from session-ping).</summary>
        public static void Set(string day, long dueMs, bool done, RaidOutcome? result, long resultAt)
        {
            Known = true;
            Day = day;
            DueMs = dueMs;
            Done = done;
            if (result != null && PlayerPrefs.GetString(AppliedPref, "") != resultAt.ToString(CultureInfo.InvariantCulture))
            {
                Pending = result;
                _pendingAt = resultAt;
            }
        }

        /// <summary>The island ran (or skipped) today's raid.</summary>
        public static void Finish(string outcome, int size)
        {
            PlayerPrefs.SetString(DayPref, Today);
            PlayerPrefs.Save();
            var day = Known ? Day : Today;
            if (Known && Day == Today) Done = true;
            Finished?.Invoke(day, outcome, size);
        }

        /// <summary>The island applied <see cref="Pending"/>.</summary>
        public static void MarkApplied()
        {
            if (Pending == null) return;
            PlayerPrefs.SetString(AppliedPref, _pendingAt.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
            Pending = null;
            Applied?.Invoke();
        }
    }
}
