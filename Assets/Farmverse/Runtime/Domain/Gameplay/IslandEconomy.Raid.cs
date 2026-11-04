#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>
    /// What one raid did, as plain data: the server works it out for an island whose player stayed away
    /// (<see cref="IslandEconomy.RaidAway"/>) and the game applies the very same losses to its own copy
    /// when the player comes back (<see cref="IslandEconomy.ApplyRaid"/>).
    /// </summary>
    [Serializable]
    public sealed class RaidOutcome
    {
        /// <summary>Raiders that came.</summary>
        public int size;
        /// <summary>The island's defence against the raiders' strength (1 or more: they were driven off).</summary>
        public double ratio;
        /// <summary>Nothing was lost.</summary>
        public bool won;
        /// <summary>Goods carried off, per item.</summary>
        public List<EconomySave.Count> stolen = new List<EconomySave.Count>();
        public int coins;
        /// <summary>Fields whose crop was trampled (building uids).</summary>
        public List<int> trampled = new List<int>();
        /// <summary>Buildings knocked down to ruins (building uids).</summary>
        public List<int> ruined = new List<int>();

        public int StolenTotal => stolen.Sum(c => c.n);
    }

    public sealed partial class IslandEconomy
    {
        /// <summary>
        /// The day's raid while nobody was there to fight it (the server runs this at the raid's time).
        /// The island's warriors and guards stand against the raiders' strength: matching it drives them
        /// off; falling short loses a share of what a raid this size wants (goods first, then coins, crops,
        /// and at the worst a building). Deterministic for the same island and <paramref name="rng"/>.
        /// </summary>
        public RaidOutcome RaidAway(double now, Random rng)
        {
            var threat = Threat;
            var size = GameplayRules.RaidSize(BestLevel, threat, Stat("raids_seen"));
            Count("raids_seen");
            var power = size * GameplayRules.RaiderStrength(BestLevel);
            var ratio = power <= 0 ? 1 : Defence / power;
            var o = new RaidOutcome { size = size, ratio = Math.Round(ratio, 3) };
            if (ratio >= 1)
            {
                o.won = true;
                Count("raids_won");
                return o;
            }

            var shortfall = 1 - ratio;   // 0 (almost held) .. 1 (nobody to fight)
            // never more than a tenth of what's in store
            var stored = _departments.Values.Where(d => d.Levels.Kind is DepartmentKind.Farmhouse or DepartmentKind.Silo).Sum(d => d.Stored);
            var wanted = Math.Min((int)Math.Round((6 + 5 * size) * shortfall), (int)Math.Floor(stored * GameplayRules.RaidGoodsCap));
            var stolen = new Dictionary<string, int>(StringComparer.Ordinal);
            var stores = _departments.Values.Where(d => d.Levels.Kind is DepartmentKind.Farmhouse or DepartmentKind.Silo && IsBuilt(d.Uid))
                .OrderBy(d => d.Uid).ToList();
            // a few at a time from each store, biggest piles first, like raiders running in and out
            for (var guard = 0; wanted > 0 && guard < 1000; guard++)
            {
                var took = false;
                foreach (var d in stores)
                {
                    if (wanted <= 0) break;
                    var bag = new List<string>();
                    if (Steal(d.Uid, Math.Min(2, wanted), bag) == 0) continue;
                    took = true;
                    wanted -= bag.Count;
                    foreach (var item in bag) stolen[item] = (stolen.TryGetValue(item, out var n) ? n : 0) + 1;
                }
                if (!took) break;
            }
            o.stolen = stolen.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new EconomySave.Count { item = kv.Key, n = kv.Value }).ToList();

            if (shortfall >= 0.3) o.coins = StealCoins();

            var fields = _plots.Values.Where(p => p.Crop != null).OrderBy(p => p.Uid).Select(p => p.Uid).ToList();
            // a field for every three raiders, once the defence fell well short
            var trample = shortfall < 0.5 ? 0 : Math.Min(fields.Count, (int)Math.Ceiling(size * shortfall / 3));
            for (var i = 0; i < trample; i++)
            {
                var pick = fields[rng.Next(fields.Count)];
                fields.Remove(pick);
                if (Trample(pick) != null) o.trampled.Add(pick);
            }

            if (shortfall >= 0.75 && BestLevel >= GameplayRules.RaidRuinLevel)
            {
                // the worst raids wreck one building (never the farmhouse: the island would stop)
                var targets = _health.Where(kv => !kv.Value.Ruined && IsBuilt(kv.Key) && _ids.TryGetValue(kv.Key, out var id) && id != "farmhouse")
                    .Select(kv => kv.Key).OrderBy(u => u).ToList();
                if (targets.Count > 0)
                {
                    var uid = targets[rng.Next(targets.Count)];
                    if (Damage(uid, double.MaxValue, now)) o.ruined.Add(uid);
                }
            }

            o.won = o.StolenTotal == 0 && o.coins == 0 && o.trampled.Count == 0 && o.ruined.Count == 0;
            if (o.won) Count("raids_won");
            return o;
        }

        /// <summary>
        /// The game's copy takes the losses of a raid the server ran while the player was away. The device may
        /// have moved on a little since its last save reached the server, so everything is taken "up to": a
        /// good that's no longer there isn't taken, a building already ruined isn't ruined again.
        /// </summary>
        public void ApplyRaid(RaidOutcome o, double now)
        {
            foreach (var c in o.stolen)
            {
                var left = c.n;
                foreach (var d in _departments.Values.Where(d => d.Levels.Kind is DepartmentKind.Farmhouse or DepartmentKind.Silo).OrderBy(d => d.Uid))
                {
                    if (left <= 0) break;
                    left -= d.TakeStock(c.item, left);
                }
            }
            if (o.coins > 0) Coins = Math.Max(0, Coins - o.coins);
            foreach (var uid in o.trampled) Trample(uid);
            foreach (var uid in o.ruined) Damage(uid, double.MaxValue, now);
            if (o.won) Count("raids_won");
        }
    }
}
