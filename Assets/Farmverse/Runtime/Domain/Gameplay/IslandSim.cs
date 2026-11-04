#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>
    /// One island as the server keeps it: where every building stands and the economy's saved state,
    /// as of <see cref="simAt"/> (seconds since 1970, server clock). The same shape the game saves, so
    /// the game and the island engine (Cloud Run) load it the same way.
    /// </summary>
    [Serializable]
    public sealed class IslandState
    {
        /// <summary>Version of the island rules that wrote this state (the engine refuses newer, migrates older).</summary>
        public int rulesVersion = IslandSim.RulesVersion;
        /// <summary>The moment the economy was last advanced to.</summary>
        public double simAt;
        /// <summary>The island's random seed (orders, picks): the same seed and inputs give the same island.</summary>
        public long seed;
        /// <summary>Coins the island started with (only used before the first save).</summary>
        public int startCoins = 500;
        public List<Placed> buildings = new List<Placed>();
        /// <summary>The economy's own save (null on a brand-new island).</summary>
        public EconomySave? economy;

        /// <summary>A building on the grid: catalogue id, the game's uid, its cells and what it cost.</summary>
        [Serializable]
        public sealed class Placed
        {
            public string id = "";
            public int uid, x, z, w = 1, d = 1;
            public int price;
            /// <summary>Builder-seconds still owed when it was placed (0 = standing).</summary>
            public double buildWork;
        }
    }

    /// <summary>What advancing an island produced: the new state, what happened while nobody watched, and when to look again.</summary>
    public sealed class IslandAdvance
    {
        public IslandAdvance(IslandState state, AwayReport report, double nextWake, IslandEconomy economy)
        {
            State = state;
            Report = report;
            NextWake = nextWake;
            Economy = economy;
        }

        public IslandState State { get; }
        public AwayReport Report { get; }
        /// <summary>The next moment something on the island changes by itself (crops ripen, a machine finishes), capped at an hour.</summary>
        public double NextWake { get; }
        /// <summary>The live economy after the advance (for callers that read more from it).</summary>
        public IslandEconomy Economy { get; }
    }

    /// <summary>
    /// Runs an island forward in time with the game's own rules, exactly as the game does when it opens:
    /// place the buildings, restore the save, let the workers work the time away, then run upkeep to now.
    /// Pure and deterministic: no clock, no unseeded randomness; the same state and time give the same result.
    /// </summary>
    public static class IslandSim
    {
        /// <summary>Raise when a rule change would make old saves advance differently.</summary>
        public const int RulesVersion = 2;

        /// <summary>The longest the server waits before looking at an island again, even if nothing is due.</summary>
        public const double MaxWakeSeconds = 3600;

        /// <summary>The island grid: world metres of cell (0,0)'s corner and the cell size (IslandData on the island scene).</summary>
        public const double OriginX = -320, OriginZ = -320, CellSize = 2;

        /// <summary>Builds the live economy for a saved island, without advancing time.</summary>
        public static IslandEconomy Load(IslandState state)
        {
            if (state.rulesVersion > RulesVersion)
                throw new InvalidOperationException($"island written by rules v{state.rulesVersion}, this engine runs v{RulesVersion}");
            var at = state.economy?.now > 0 ? state.economy!.now : state.simAt;
            var e = new IslandEconomy(state.economy?.coins ?? state.startCoins)
            {
                Rng = new Random(unchecked((int)(state.seed ^ (long)at))),
            };
            foreach (var b in state.buildings)
            {
                e.AddBuilding(b.uid, b.id, at, b.buildWork, b.price);
                e.Place(b.uid, OriginX + (b.x + b.w * 0.5) * CellSize, OriginZ + (b.z + b.d * 0.5) * CellSize);
            }

            if (state.economy != null) e.Restore(state.economy);
            return e;
        }

        /// <summary>
        /// Advances the island to <paramref name="now"/>: the away work (workers bringing in loads) and the
        /// upkeep (energy, machines, building, rot, weather, the day, orders). Returns the new state.
        /// </summary>
        public static IslandAdvance Advance(IslandState state, double now)
        {
            var e = Load(state);
            var from = state.economy?.now ?? 0;
            var report = from > 0 && now > from ? e.WorkOffline(from, now) : new AwayReport();
            e.Tick(now);
            var next = new IslandState
            {
                rulesVersion = RulesVersion,
                simAt = now,
                seed = state.seed,
                startCoins = state.startCoins,
                buildings = state.buildings.Select(Copy).ToList(),
                economy = e.ToSave(),
            };
            return new IslandAdvance(next, report, NextWake(e, now), e);
        }

        /// <summary>
        /// The day's raid on an island whose player stayed away: advance it to <paramref name="now"/>, then let
        /// the raiders in (<see cref="IslandEconomy.RaidAway"/>). <paramref name="salt"/> (the raid's day)
        /// keeps each day's raid its own while the same island and day always give the same raid.
        /// </summary>
        public static (IslandAdvance advance, RaidOutcome raid) Raid(IslandState state, double now, long salt)
        {
            var e = Load(state);
            var from = state.economy?.now ?? 0;
            var report = from > 0 && now > from ? e.WorkOffline(from, now) : new AwayReport();
            e.Tick(now);
            var raid = e.RaidAway(now, new Random(unchecked((int)(state.seed * 31 + salt))));
            var next = new IslandState
            {
                rulesVersion = RulesVersion,
                simAt = now,
                seed = state.seed,
                startCoins = state.startCoins,
                buildings = state.buildings.Select(Copy).ToList(),
                economy = e.ToSave(),
            };
            return (new IslandAdvance(next, report, NextWake(e, now), e), raid);
        }

        /// <summary>The next moment the island changes by itself: a crop ripens or a machine finishes; at most an hour away.</summary>
        public static double NextWake(IslandEconomy e, double now)
        {
            var next = now + MaxWakeSeconds;
            var save = e.ToSave();
            foreach (var p in save.plots)
                if (p.readyAt > now && p.readyAt < next) next = p.readyAt;
            foreach (var m in save.machines)
                if (m.busyUntil > now && m.busyUntil < next) next = m.busyUntil;
            return next;
        }

        private static IslandState.Placed Copy(IslandState.Placed b) => new IslandState.Placed
        {
            id = b.id, uid = b.uid, x = b.x, z = b.z, w = b.w, d = b.d, price = b.price, buildWork = 0,
        };
    }
}
