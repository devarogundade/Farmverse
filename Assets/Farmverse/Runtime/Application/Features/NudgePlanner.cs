#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Farmverse.Application.Ports;

namespace Farmverse.Application.Features
{
    /// <summary>
    /// Which of the island's own reminders the phone books when the game goes to the background (pure;
    /// tested). The same manners as the server's pushes: topics the player turned off are left out, a
    /// reminder that would land in quiet hours waits until they end, two never come close together, and
    /// only the first few are kept.
    /// </summary>
    public static class NudgePlanner
    {
        public const int MaxNudges = 3;
        public const long MinGapMs = 45 * 60_000L;
        /// <summary>Nothing booked further out than this (a player gone that long hears from the server).</summary>
        public const long HorizonMs = 3 * 24 * 3_600_000L;
        /// <summary>Reminders less than this far ahead aren't worth a notification.</summary>
        public const long MinLeadMs = 10 * 60_000L;

        public static List<LocalNudge> Plan(IEnumerable<LocalNudge> candidates, long nowMs, int offsetMin, int quietStart, int quietEnd, Func<string, bool> topicOn)
        {
            var kept = new List<LocalNudge>();
            foreach (var n in candidates
                         .Where(n => topicOn(n.Topic) && n.AtMs - nowMs >= MinLeadMs && n.AtMs - nowMs <= HorizonMs)
                         .Select(n => Shift(n, offsetMin, quietStart, quietEnd))
                         .OrderBy(n => n.AtMs))
            {
                if (kept.Count >= MaxNudges) break;
                if (kept.Any(k => Math.Abs(k.AtMs - n.AtMs) < MinGapMs)) continue;
                kept.Add(n);
            }
            return kept;
        }

        /// <summary>True when the hour is in quiet hours (wrapping midnight when start &gt; end; equal = none).</summary>
        public static bool Quiet(int hour, int start, int end) =>
            start != end && (start < end ? hour >= start && hour < end : hour >= start || hour < end);

        /// <summary>A reminder due in quiet hours moves to the hour they end.</summary>
        private static LocalNudge Shift(LocalNudge n, int offsetMin, int quietStart, int quietEnd)
        {
            var local = DateTimeOffset.FromUnixTimeMilliseconds(n.AtMs).ToOffset(TimeSpan.FromMinutes(offsetMin));
            if (!Quiet(local.Hour, quietStart, quietEnd)) return n;
            var end = new DateTimeOffset(local.Year, local.Month, local.Day, quietEnd, 0, 0, local.Offset);
            if (end <= local) end = end.AddDays(1);
            return new LocalNudge { Title = n.Title, Body = n.Body, Route = n.Route, Topic = n.Topic, AtMs = end.ToUnixTimeMilliseconds() };
        }
    }
}
