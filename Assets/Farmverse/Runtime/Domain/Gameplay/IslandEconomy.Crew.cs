#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>
    /// A Farmhand: a worker NFT the player holds (or rents). The server writes the island's crew from the
    /// chain; the island only reads it. Traits run 1–5, the level 1–10 (docs 15 §7).
    /// </summary>
    [Serializable]
    public sealed class Farmhand
    {
        public string token = "";
        public string name = "";
        /// <summary><c>fields</c>, <c>animals</c>, <c>machines</c> or <c>freight</c>.</summary>
        public string specialty = "";
        public int speed = 1;
        public int carry = 1;
        public int stamina = 1;
        public int level = 1;
        /// <summary>Rented from someone else (shown as "Rented"; works the same).</summary>
        public bool rented;
    }

    public sealed partial class IslandEconomy
    {
        private readonly List<Farmhand> _crew = new List<Farmhand>();

        /// <summary>Every Farmhand the player holds, in token order (beds decide who works).</summary>
        public IReadOnlyList<Farmhand> Crew => _crew;

        /// <summary>Wild plots cleared with land deeds (docs 15 §12.3), 0..<see cref="GameplayRules.WildPlots"/>.</summary>
        public int WildPlotsOpen { get; private set; }

        /// <summary>The crew as the server last saw it on chain (replaces the old one).</summary>
        public void SetCrew(IEnumerable<Farmhand> crew)
        {
            _crew.Clear();
            foreach (var f in crew.Where(f => !string.IsNullOrEmpty(f.token)).GroupBy(f => f.token, StringComparer.Ordinal).Select(g => g.First())
                         .OrderBy(f => f.token, StringComparer.Ordinal))
            {
                _crew.Add(new Farmhand
                {
                    token = f.token,
                    name = f.name,
                    specialty = f.specialty,
                    speed = Clamp(f.speed, 1, 5),
                    carry = Clamp(f.carry, 1, 5),
                    stamina = Clamp(f.stamina, 1, 5),
                    level = Clamp(f.level, 1, GameplayRules.FarmhandMaxLevel),
                    rented = f.rented,
                });
            }
        }

        /// <summary>Beds for Farmhands: the levels of the built workers' houses added up, at most <see cref="GameplayRules.MaxCrew"/>.</summary>
        public int CrewBeds =>
            Math.Min(GameplayRules.MaxCrew,
                _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.WorkersHouse && IsBuilt(d.Uid)).Sum(d => d.Level));

        /// <summary>The Farmhands with a bed (the first ones in token order); the rest wait in the wallet.</summary>
        public IReadOnlyList<Farmhand> WorkingCrew => _crew.Take(CrewBeds).ToList();

        /// <summary>
        /// Hands the working crew adds to every work step: one each, one more for strong speed + stamina
        /// (their sum ≥ 7), one more at level 5 and at level 10.
        /// </summary>
        public int CrewHands => WorkingCrew.Sum(f => 1 + (f.speed + f.stamina >= 7 ? 1 : 0) + (f.level >= 5 ? 1 : 0) + (f.level >= 10 ? 1 : 0));

        /// <summary>Extra units every load can hold: the best carry trait on the crew (carry 3 → +1, 5 → +2).</summary>
        public int CrewCarryBonus => WorkingCrew.Select(f => (f.carry - 1) / 2).DefaultIfEmpty(0).Max();

        /// <summary>A hand from each working specialist when their kind of job comes up in a step.</summary>
        public int SpecialistHands(string job)
        {
            var specialty = job switch
            {
                WorkJob.Harvest or WorkJob.Replant => "fields",
                WorkJob.Feed or WorkJob.Collect => "animals",
                WorkJob.Refill or WorkJob.Repair => "machines",
                WorkJob.Sell or WorkJob.Restock => "freight",
                _ => "",
            };
            return specialty.Length == 0 ? 0 : WorkingCrew.Count(f => f.specialty == specialty);
        }

        /// <summary>Clears the next wild plot (a redeemed land deed). False when every plot is open.</summary>
        public bool OpenWildPlot()
        {
            if (WildPlotsOpen >= GameplayRules.WildPlots) return false;
            WildPlotsOpen++;
            return true;
        }

        /// <summary>
        /// What one unit off a ripe field is: now and then the crop's golden form (docs 15 §12.4). Decided by a
        /// hash of the field, when it was planted and which unit this is, so the device and the server's engine
        /// always agree.
        /// </summary>
        private static string HarvestItem(int uid, double plantedAt, int unitLeft, string crop)
        {
            unchecked
            {
                var h = 2166136261u;
                foreach (var c in $"{uid}:{(long)Math.Round(plantedAt * 1000)}:{unitLeft}")
                {
                    h ^= c;
                    h *= 16777619u;
                }
                return h % 100u < (uint)GameplayRules.GoldenChancePercent ? GameplayRules.GoldenPrefix + crop : crop;
            }
        }

        private static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));

        private void SaveCrew(EconomySave s)
        {
            s.crew = _crew.Select(f => new Farmhand { token = f.token, name = f.name, specialty = f.specialty, speed = f.speed, carry = f.carry, stamina = f.stamina, level = f.level, rented = f.rented }).ToList();
            s.wildPlotsOpen = WildPlotsOpen;
        }

        private void LoadCrew(EconomySave s)
        {
            SetCrew(s.crew ?? new List<Farmhand>());
            WildPlotsOpen = Clamp(s.wildPlotsOpen, 0, GameplayRules.WildPlots);
        }
    }
}
