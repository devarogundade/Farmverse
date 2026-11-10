#nullable enable
using System;
using System.Collections.Generic;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>
    /// The most of each good an island could have made in a span, from the buildings it already had
    /// (docs 13 §7.3). While the device still owns the island (shadow mode) the server can't trust an
    /// uploaded save's stock, so a good only becomes mintable as it appears between two saves the server
    /// saw, and only up to what this says the earlier island's fields, animals and machines could make.
    /// Generous on purpose (levels, a season's or event's yield bonus and boosts fit inside
    /// <see cref="Slack"/>): it is there to stop a save that jumps, not to second-guess a busy player.
    /// </summary>
    public static class IslandEarned
    {
        /// <summary>Head-room over the plain rates: boosts, the season and the month's event.</summary>
        public const double Slack = 2.0;
        /// <summary>Goods no building on the island makes (wood, stone, iron from trees, rocks and the train): per hour.</summary>
        public const double OtherPerHour = 60;
        /// <summary>Goods ready to collect when the span began count once: a field's harvest, a full coop, a machine's tray.</summary>
        public const int OtherInHand = 20;
        /// <summary>Golden harvests come by chance, so a lucky streak gets this much more than the odds.</summary>
        public const double GoldenLuck = 3.0;
        /// <summary>The longest span counted (a phone left for a month doesn't earn a month of crops at once).</summary>
        public const double MaxSeconds = 7 * 24 * 3600;

        /// <summary>The most of each good <paramref name="before"/>'s working buildings could make in <paramref name="seconds"/>.</summary>
        public static SortedDictionary<string, int> Ceiling(IslandState before, double seconds)
        {
            var span = Math.Max(0, Math.Min(MaxSeconds, seconds));
            var e = IslandSim.Load(before);
            var perSecond = new Dictionary<string, double>(StringComparer.Ordinal);
            var inHand = new Dictionary<string, double>(StringComparer.Ordinal);
            void Add(string item, double rate, double ready)
            {
                perSecond[item] = (perSecond.TryGetValue(item, out var r) ? r : 0) + rate;
                inHand[item] = (inHand.TryGetValue(item, out var h) ? h : 0) + ready;
            }

            var fields = 0;
            foreach (var b in before.buildings)
            {
                if (!e.Working(b.uid)) continue;
                if (b.id == GameplayRules.FieldBuilding)
                {
                    fields++;
                    continue;
                }
                var speed = GameplayRules.BuildingLevelSpeed(e.LevelOf(b.uid));
                if (GameplayRules.ProducerOf(b.id) is { } p) Add(p.Item, speed / p.IntervalSeconds, p.Max);
                if (GameplayRules.RecipeOf(b.id) is { } m) Add(m.Output, speed / m.Seconds, GameplayRules.MachineOutputMax);
            }
            // any field grows any crop, so each crop's ceiling is every field growing it
            if (fields > 0)
                foreach (var c in GameplayRules.Crops)
                {
                    Add(c.Id, (double)fields * c.Yield / c.GrowSeconds, fields * c.Yield);
                    Add(GameplayRules.GoldenPrefix + c.Id, GoldenLuck * fields * c.Yield * GameplayRules.GoldenChancePercent / 100.0 / c.GrowSeconds, 1);
                }

            var ceiling = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in perSecond)
                ceiling[kv.Key] = Whole(Slack * (kv.Value * span + inHand[kv.Key]));
            return ceiling;
        }

        /// <summary>The ceiling for a good nothing on the island makes.</summary>
        public static int OtherCeiling(double seconds) =>
            Whole(Slack * (OtherPerHour * Math.Max(0, Math.Min(MaxSeconds, seconds)) / 3600.0 + OtherInHand));

        /// <summary>Rounds up, ignoring floating-point dust (12.000000001 is 12).</summary>
        private static int Whole(double x) => (int)Math.Min(int.MaxValue, Math.Ceiling(x - 1e-6));

        /// <summary>
        /// What <paramref name="after"/> gained in its farmhouses over <paramref name="before"/>, each good capped at
        /// its <see cref="Ceiling"/>; the farmhouse stock after; and how much the earlier island could store.
        /// </summary>
        public static Comparison Compare(IslandState before, IslandState after, double seconds)
        {
            var was = IslandSim.Load(before);
            var stock = IslandSim.Load(after).FarmhouseStock();
            var ceiling = Ceiling(before, seconds);
            var other = OtherCeiling(seconds);
            var gain = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in stock)
            {
                var grew = kv.Value - was.StoredInFarmhouses(kv.Key);
                if (grew <= 0) continue;
                gain[kv.Key] = Math.Min(grew, ceiling.TryGetValue(kv.Key, out var most) ? most : other);
            }
            return new Comparison { gain = gain, stock = stock, room = was.FarmhouseCapacity };
        }

        /// <summary>What <see cref="Compare"/> found (the engine's <c>/v1/earned</c> reply).</summary>
        [Serializable]
        public sealed class Comparison
        {
            /// <summary>Goods that grew, each no more than the island could have made.</summary>
            public SortedDictionary<string, int> gain = new SortedDictionary<string, int>(StringComparer.Ordinal);
            /// <summary>The later island's farmhouse stock.</summary>
            public SortedDictionary<string, int> stock = new SortedDictionary<string, int>(StringComparer.Ordinal);
            /// <summary>How much the earlier island's working farmhouses hold.</summary>
            public int room;
        }
    }
}
