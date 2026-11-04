#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>Furniture bought for the farmhouse's rooms (coins; kept in the save).</summary>
    public sealed partial class IslandEconomy
    {
        private readonly HashSet<string> _roomItems = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyCollection<string> RoomItemsOwned => _roomItems;
        public bool HasRoomItem(string id) => _roomItems.Contains(id);

        /// <summary>The farmhouse's level (1 when there is none yet).</summary>
        public int FarmhouseLevel
        {
            get
            {
                var best = 1;
                foreach (var d in _departments.Values) if (d.BuildingId == "farmhouse") best = Math.Max(best, d.Level);
                return best;
            }
        }

        /// <summary>Buy a room item for coins; false when unknown, owned, the room isn't open yet, or coins are short.</summary>
        public bool BuyRoomItem(string id)
        {
            var item = GameplayRules.RoomItemById(id);
            if (item == null || _roomItems.Contains(id)) return false;
            if (item.Room >= GameplayRules.RoomsOpen(FarmhouseLevel)) return false;
            if (!SpendCoins(item.Price)) return false;
            _roomItems.Add(id);
            Count("room_items");
            return true;
        }

        // ---- costumes (bought with gems in Customize; the island save keeps them, so every device has them) ----

        private readonly HashSet<string> _looks = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyCollection<string> LooksOwned => _looks;
        public bool HasLook(string id) => _looks.Contains(id);

        /// <summary>A costume the wallet has just been charged for (the gems are the server's; this only remembers it).</summary>
        public void GrantLook(string id) { if (!string.IsNullOrEmpty(id) && _looks.Add(id)) Count("looks_bought"); }

        private void SaveRooms(EconomySave s)
        {
            s.roomItems = _roomItems.OrderBy(x => x, StringComparer.Ordinal).ToList();
            s.looks = _looks.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        private void RestoreRooms(EconomySave s)
        {
            _roomItems.Clear();
            foreach (var id in s.roomItems) if (GameplayRules.RoomItemById(id) != null) _roomItems.Add(id);
            _looks.Clear();
            foreach (var id in s.looks ?? new List<string>()) if (!string.IsNullOrEmpty(id)) _looks.Add(id);
        }
    }
}
