#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>
    /// The live year (plan: Live events and Farm Pass themes), like Hay Day's and Township's: every month has a
    /// theme, and its event runs over the month's last ten days with a bonus on the theme's goods. December is
    /// the Winter Festival instead, from the 1st to 6 January: it snows, the town is decorated, Santa hands out
    /// gifts and the Farm Pass wears the month's theme. Everything follows the UTC calendar, the same for everyone.
    /// </summary>
    public static partial class GameplayRules
    {
        public sealed class LiveEvent
        {
            public LiveEvent(int month, string id, string name, string theme, string[] goods, int yieldBonus, double sellBonus, double xpBonus, string hint)
            {
                Month = month; Id = id; Name = name; Theme = theme; Goods = goods; YieldBonus = yieldBonus; SellBonus = sellBonus; XpBonus = xpBonus; Hint = hint;
            }
            public int Month { get; }
            public string Id { get; }
            public string Name { get; }
            /// <summary>The Farm Pass's theme that month.</summary>
            public string Theme { get; }
            /// <summary>The goods the bonus is on (empty: every crop).</summary>
            public IReadOnlyList<string> Goods { get; }
            /// <summary>Extra units per field sown during the event (the theme's crops, or every crop).</summary>
            public int YieldBonus { get; }
            /// <summary>Shops pay this much more for the theme's goods (0.25 = +25%).</summary>
            public double SellBonus { get; }
            /// <summary>XP multiplier while it runs (1 = none).</summary>
            public double XpBonus { get; }
            public string Hint { get; }

            public bool Covers(string item) => Goods.Count == 0 || Goods.Contains(item);
        }

        /// <summary>Days at the end of each month the month's event runs.</summary>
        public const int EventDays = 10;

        public static readonly IReadOnlyList<LiveEvent> LiveYear = new[]
        {
            new LiveEvent(1, "snow_day", "Snow Day Fair", "Frosty Farm", new[] { "milk", "yogurt" }, 0, 0.25, 1.0, "Shops pay 25% more for milk and yogurt"),
            new LiveEvent(2, "sweet_bakes", "Sweetheart Bakes", "Sweet Treats", new[] { "croissant", "flour" }, 0, 0.3, 1.0, "Shops pay 30% more for croissants and flour"),
            new LiveEvent(3, "spring_planting", "Spring Planting", "Green Shoots", Array.Empty<string>(), 1, 0, 1.0, "Every field sown yields one more"),
            new LiveEvent(4, "egg_hunt", "Egg Hunt", "Spring Chicks", new[] { "egg" }, 0, 0.3, 1.1, "Shops pay 30% more for eggs; 10% more XP"),
            new LiveEvent(5, "flower_show", "Flower Show", "In Bloom", new[] { "sunflower", "flowers" }, 1, 0.3, 1.2, "Sunflowers yield one more and sell for 30% more; 20% more XP"),
            new LiveEvent(6, "picnic_week", "Picnic Week", "Sunny Picnic", new[] { "juice", "veg_box", "soda" }, 0, 0.25, 1.0, "Shops pay 25% more for juice, soda and veg boxes"),
            new LiveEvent(7, "summer_harvest", "Summer Harvest", "Golden Fields", new[] { "tomato", "maize", "beans" }, 2, 0, 1.0, "Tomatoes, maize and beans yield two more"),
            new LiveEvent(8, "county_fair", "County Fair", "Fair Days", Array.Empty<string>(), 0, 0, 1.5, "50% more XP from everything"),
            new LiveEvent(9, "harvest_fair", "Harvest Fair", "Harvest Moon", new[] { "wheat", "flour", "cabbage" }, 1, 0.2, 1.0, "Wheat and cabbage yield one more; wheat, flour and cabbage sell for 20% more"),
            new LiveEvent(10, "pumpkin_festival", "Pumpkin Festival", "Spooky Nights", new[] { "pumpkin" }, 2, 0.3, 1.0, "Pumpkins yield two more and sell for 30% more"),
            new LiveEvent(11, "woodland_week", "Woodland Week", "Lumberjack", new[] { "wood", "planks", "stone" }, 0, 0.25, 1.0, "Shops pay 25% more for wood, planks and stone"),
            new LiveEvent(12, "winter_festival", "Winter Festival", "Winter Wonderland", new[] { "milk", "egg", "croissant" }, 0, 0.25, 1.25, "Snow and decorations; Santa brings coins every day; milk, eggs and croissants sell for 25% more; 25% more XP"),
        };

        public static DateTime UtcOf(double unixSeconds) => new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Math.Max(0, unixSeconds));
        private static double UnixOf(DateTime d) => (d - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        /// <summary>1 December to the end of 6 January.</summary>
        public static bool WinterFestival(double now)
        {
            var d = UtcOf(now);
            return d.Month == 12 || (d.Month == 1 && d.Day <= 6);
        }

        private static double FestivalLeft(double now)
        {
            var d = UtcOf(now);
            var end = new DateTime(d.Month == 12 ? d.Year + 1 : d.Year, 1, 7, 0, 0, 0, DateTimeKind.Utc);
            return Math.Max(0, UnixOf(end) - now);
        }

        /// <summary>This month's theme (shown on the Farm Pass all month).</summary>
        public static LiveEvent ThemeOf(double now) => LiveYear[UtcOf(now).Month - 1];

        /// <summary>The event running now, if any.</summary>
        public static LiveEvent? EventAt(double now)
        {
            if (WinterFestival(now)) return LiveYear[11];
            var d = UtcOf(now);
            var days = DateTime.DaysInMonth(d.Year, d.Month);
            return d.Day > days - EventDays ? LiveYear[d.Month - 1] : null;
        }

        /// <summary>Seconds until the running event ends, or until the next one starts when none runs.</summary>
        public static double EventLeft(double now)
        {
            if (WinterFestival(now)) return FestivalLeft(now);
            var d = UtcOf(now);
            var days = DateTime.DaysInMonth(d.Year, d.Month);
            var monthEnd = new DateTime(d.Year, d.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
            var start = monthEnd.AddDays(-EventDays);
            return Math.Max(0, UnixOf(d.Day > days - EventDays ? monthEnd : start) - now);
        }

        /// <summary>Santa's daily gift during the Winter Festival, in coins (grows over the first twenty days).</summary>
        public static int SantaGift(int dayOfFestival) => 150 + 25 * Math.Min(Math.Max(0, dayOfFestival), 20);
    }
}
