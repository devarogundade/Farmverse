#nullable enable
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// The island as the game's UI sees it: coins, the shop, the selected building or tree and what
    /// can be done with it, placing, the farmhouse's rooms, and the settings. Each action is the one
    /// the old panel performed, with the same sounds and saving.
    /// </summary>
    public sealed partial class IslandSandbox
    {
        /// <summary>The economy has started (coins, goals and stations are live).</summary>
        public bool Running => _economyStarted && economy != null;

        /// <summary>The home screen is up: the island keeps living, but nobody walks, taps or pans.</summary>
        public bool Lobby { get; set; }
        public IslandEconomyHost? Host => economy;
        public int CoinsNow => Coins;
        public BuildingCatalogue? Catalogue => catalogue;
        public BuildPlacer? Placer => placer;

        /// <summary>The island level that opens the shop's buildings.</summary>
        public int IslandLevel => _economyStarted ? economy!.Economy.BestLevel : 1;

        /// <summary>What the shop sells: working buildings, decorations and paths (no free farmhouses, no wild trees).</summary>
        public IEnumerable<BuildingCatalogue.Entry> ShopEntries =>
            catalogue == null ? Enumerable.Empty<BuildingCatalogue.Entry>() : catalogue.entries.Where(e => e.prefab != null && e.price > 0 && !e.id.StartsWith("nature_"));

        /// <summary>The held building being placed (free, at its level), or null for a normal purchase.</summary>
        private IslandState.Held? _placingHeld;

        /// <summary>
        /// Start placing a building redeemed from an NFT (<see cref="IslandServerState.Held"/>): no coins,
        /// standing at once, at its level. False when this island doesn't know the building.
        /// </summary>
        public bool PlaceHeld(IslandState.Held held)
        {
            if (placer == null || islandCamera == null || catalogue == null) return false;
            var e = catalogue.Find(held.id);
            if (e == null || e.prefab == null) return false;
            _selected = null;
            _selectedProp = null;
            _placingHeld = held;
            var cam = islandCamera.GetComponent<Camera>();
            var r = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            var p = new Plane(Vector3.up, Vector3.zero);
            placer.BeginNew(e, p.Raycast(r, out var en) ? r.GetPoint(en) : Vector3.zero);
            IslandAudio.Play("pop", null, 0.5f, 0f);
            return true;
        }

        /// <summary>Start placing a new building from the shop, in the middle of the view.</summary>
        public void Buy(BuildingCatalogue.Entry e)
        {
            if (placer == null || islandCamera == null) return;
            _placingHeld = null;
            if (IslandLevel < GameplayRules.UnlockLevel(e.id) || Coins < e.price || CantBuy(e) != null) { IslandAudio.Play("error", null, 0.6f); return; }
            _selected = null;
            _selectedProp = null;
            var cam = islandCamera.GetComponent<Camera>();
            var r = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            var p = new Plane(Vector3.up, Vector3.zero);
            var at = p.Raycast(r, out var en) ? r.GetPoint(en) : Vector3.zero;
            placer.BeginNew(e, at);
            IslandAudio.Play("pop", null, 0.5f, 0f);
        }

        /// <summary>Why this can't be bought right now besides level and coins (the field cap), or null.</summary>
        public string? CantBuy(BuildingCatalogue.Entry e)
        {
            if (e.id != GameplayRules.FieldBuilding || !_economyStarted) return null;
            var have = economy!.Economy.FieldCount;
            var cap = GameplayRules.FieldCap(IslandLevel);
            if (have < cap) return null;
            var next = IslandLevel + 1;
            while (GameplayRules.FieldCap(next) <= cap && next < IslandLevel + 100) next++;
            return GameplayRules.FieldCap(next) > cap ? $"Fields: {have}/{cap}. One more at island level {next}" : $"Fields: {have}/{cap}, the most an island can have";
        }

        /// <summary>Why the building being placed can't go where it is, if there's a rule (by the sea…).</summary>
        public string? PlaceHint => placer != null && placer.Current != null ? placer.RuleHint?.Invoke(placer.Current) : null;


        // ---- the selection -----------------------------------------------------------------------

        public PlacedBuilding? Selected => _selected;
        public NatureProp? SelectedProp => _selectedProp;
        public BuildingCatalogue.Entry? SelectedEntry => _selected != null && catalogue != null ? catalogue.Find(_selected.id) : null;

        /// <summary>Select a building as a tap on it would (tests and tools).</summary>
        public void Select(PlacedBuilding b) { _selected = b; _selectedProp = null; }

        public void Deselect()
        {
            _selected = null;
            _selectedProp = null;
        }

        public void MoveSelected()
        {
            var e = SelectedEntry;
            if (_selected == null || e == null || placer == null) return;
            placer.BeginMove(_selected, e);
            _selected = null;
        }

        /// <summary>What selling the selected building gives back (half its price).</summary>
        public int SellValue => (SelectedEntry?.price ?? 0) / 2;

        /// <summary>The farmhouse can't be sold: it's the island's heart (its stores, the builder, the basket).</summary>
        public bool CanSell => _selected != null && _selected.id != "farmhouse";

        public void SellSelected()
        {
            if (_selected == null || !CanSell) return;
            Earn(SellValue);
            if (_economyStarted) economy!.OnRemoved(_selected);
            grid!.Release(_selected.cells);
            _placed.Remove(_selected);
            Magic.Vanish(_selected.gameObject);
            IslandAudio.Play("coin", null, 0.8f, 0f);
            _selected = null;
            Save();
        }

        public bool CanRestyle => _selected != null && SelectedEntry?.altPrefab != null && _economyStarted && economy!.Economy.Working(_selected.uid);
        public void RestyleSelected() { if (CanRestyle) Restyle(_selected!, SelectedEntry!); }

        public bool CanGoInside => _selected != null && _selected.id == "farmhouse" && (roomPrefabs.Length > 0 || UnityEngine.Application.CanStreamedLevelBeLoaded("Farmhouse"));
        public void GoInside() { if (CanGoInside) EnterFarmhouse(_selected!); }

        /// <summary>Pay to clear the selected tree, bush or rock (or walk up and chop it for free).</summary>
        public void ClearSelectedProp()
        {
            var p = _selectedProp;
            if (p == null || nature == null || Coins < p.clearCost) { IslandAudio.Play("error", null, 0.6f); return; }
            Spend(p.clearCost);
            _cleared.Add(p.index);
            IslandAudio.Play("chop", p.transform.position, 1f, 0.05f, 0f);
            nature.Clear(p, grid!);
            _selectedProp = null;
            Save();
        }

        // ---- inside the farmhouse ------------------------------------------------------------------

        public bool Inside => _insideOf != null;
        public int InsideUid => _insideOf != null ? _insideOf.uid : -1;
        public int RoomShown => _room;
        /// <summary>Rooms open now (the farmhouse's level opens them: living room and kitchen first, then one a level).</summary>
        public int RoomCount => Mathf.Min(_roomViews.Count, economy != null ? Farmverse.Domain.Gameplay.GameplayRules.RoomsOpen(economy.Economy.FarmhouseLevel) : 2);
        public int RoomsBuilt => _roomViews.Count;
        public void SeeRoom(int index) { if (index != _room && index < RoomCount) ShowRoom(index); }

        /// <summary>Buy a piece of furniture for the farmhouse (coins); it appears in its room at once.</summary>
        /// <summary>A costume bought with gems: the island keeps it (saved now).</summary>
        public void GrantLook(string id)
        {
            if (economy == null) return;
            economy.Economy.GrantLook(id);
            Save();
        }

        public bool BuyRoomItem(string id)
        {
            if (economy == null || !economy.Economy.BuyRoomItem(id)) { IslandAudio.Play("error", null, 0.6f); return false; }
            FurnishRooms();
            IslandAudio.Play("coin", null, 0.7f);
            Save();
            return true;
        }
        public void GoOutside() => LeaveFarmhouse();

        // ---- settings and the like -----------------------------------------------------------------

        public bool HasTrain
        {
            get
            {
                if (_train == null) _train = FindAnyObjectByType<IslandTrain>();
                return _train != null;
            }
        }

        public bool FollowingTrain => _followingTrain;

        /// <summary>The farmer's screen position, where the camera can point the HUD's arrows from.</summary>
        public Transform? Farmer => _player != null ? _player.transform : null;

        /// <summary>The farmer is on the horse (the game's UI turns the stick into STOP).</summary>
        public bool Riding => _ride != null && _ride.Riding;

        /// <summary>The Ride button: get on the stable's horse (if there is one ready), or get down.</summary>
        public void ToggleRide()
        {
            if (_ride == null || _player == null) return;
            if (_ride.Riding) { _ride.Dismount(); return; }
            _tapRide = true;   // a refusal says why every time
            _ride.Mount();
            _tapRide = false;
            if (_ride.Riding && islandCamera != null) islandCamera.Follow = _player.transform;
        }
    }
}
