#nullable enable
using System;
using System.Linq;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>
    /// Goods and buildings crossing between the island and the chain (IslandExchange): the stock in
    /// farmhouses, and whether a building is empty enough to be picked up.
    /// </summary>
    public sealed partial class IslandEconomy
    {
        /// <summary>How many of <paramref name="item"/> the farmhouses hold between them.</summary>
        public int StoredInFarmhouses(string item) =>
            _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && d.Stock.TryGetValue(item, out _))
                .Sum(d => d.Stock[item]);

        /// <summary>Takes exactly <paramref name="n"/> from the farmhouses (fullest first), or nothing.</summary>
        public bool TakeFromFarmhouses(string item, int n)
        {
            if (n <= 0 || StoredInFarmhouses(item) < n) return false;
            var left = n;
            foreach (var d in _departments.Values
                         .Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && d.Stock.ContainsKey(item))
                         .OrderByDescending(d => d.Stock[item]).ThenBy(d => d.Uid))
            {
                left -= d.TakeStock(item, left);
                if (left == 0) break;
            }
            RefreshBasket();
            return true;
        }

        /// <summary>
        /// Puts redeemed goods into the farmhouse with the most room. They are the player's, so they go in
        /// even past capacity (a full store only stops new deliveries). False when there is no farmhouse.
        /// </summary>
        public bool GiveToFarmhouses(string item, int n)
        {
            if (n <= 0) return false;
            var house = _departments.Values
                .Where(d => d.Levels.Kind == DepartmentKind.Farmhouse)
                .OrderByDescending(d => d.StorageCapacity - d.Stored).ThenBy(d => d.Uid)
                .FirstOrDefault();
            if (house == null) return false;
            house.Store(item, n);
            RefreshBasket();
            return true;
        }

        /// <summary>A building's level: its department's level, or its upgrade level (machines, animals).</summary>
        public int LevelOf(int uid) =>
            Math.Max(_departments.TryGetValue(uid, out var d) ? d.Level : 1, BuildingLevel(uid));

        /// <summary>
        /// Why a building can't be picked up right now, or null when it can: it must be standing and whole,
        /// hold nothing and be doing nothing — so picking it up never destroys goods. Farmhouses and the
        /// market stay: the island's goods and trade depend on them.
        /// </summary>
        public string? WhyNotPickUp(int uid, double now)
        {
            if (!_ids.ContainsKey(uid)) return "no_building";
            if (!IsBuilt(uid)) return "under_construction";
            if (IsRuined(uid)) return "ruined";
            if (_departments.TryGetValue(uid, out var d))
            {
                if (d.Levels.Kind is DepartmentKind.Farmhouse or DepartmentKind.Market) return "core_building";
                if (d.Stored > 0) return "not_empty";
            }
            var save = ToSave();
            if (save.plots.Any(p => p.uid == uid && p.crop != "")) return "not_empty";
            if (save.animals.Any(a => a.uid == uid && (a.stock > 0 || a.sideStock > 0))) return "not_empty";
            if (save.machines.Any(m => m.uid == uid && (m.output > 0 || m.busyUntil > now || m.hopper.Count > 0))) return "busy";
            return null;
        }

        /// <summary>Forgets a picked-up building's upgrade level (it travels with the token instead).</summary>
        internal void ForgetLevel(int uid) => _buildingLevels.Remove(uid);
    }
}
