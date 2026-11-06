#nullable enable
using Farmverse.Domain.Gameplay;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// Drag-and-drop from the UI into the world: a building card dragged out of the build band
    /// (placed where it's released, with an undo), and tools dragged onto buildings (the UI asks
    /// which building is under the finger).
    /// </summary>
    public sealed partial class IslandSandbox
    {
        /// <summary>Holds the camera still while something is dragged out of the UI.</summary>
        public void HoldCamera(bool held)
        {
            if (islandCamera != null) islandCamera.Held = held;
        }

        /// <summary>The building under a screen point, or null (open ground, sea, sky).</summary>
        public PlacedBuilding? BuildingAt(Vector2 screen)
        {
            if (islandCamera == null) return null;
            var ray = islandCamera.GetComponent<Camera>().ScreenPointToRay(screen);
            return Physics.Raycast(ray, out var hit, 2000f) ? hit.collider.GetComponentInParent<PlacedBuilding>() : null;
        }

        /// <summary>
        /// A building card was dragged out into the world: start its ghost under the finger (the placer
        /// then follows the finger while it's held). Same checks as <see cref="Buy"/>.
        /// </summary>
        public bool BeginDragBuy(BuildingCatalogue.Entry e, Vector2 screen)
        {
            if (placer == null || islandCamera == null) return false;
            if (IslandLevel < GameplayRules.UnlockLevel(e.id) || Coins < e.price || CantBuy(e) != null) return false;
            _selected = null;
            _selectedProp = null;
            _placingHeld = null;
            var ray = islandCamera.GetComponent<Camera>().ScreenPointToRay(screen);
            var at = GroundPoint(ray, out var g) ? g : (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var en) ? ray.GetPoint(en) : Vector3.zero);
            placer.BeginNew(e, at);
            return true;
        }

        /// <summary>
        /// The dragged building was let go: placed when it fits and wasn't dropped back on the panel,
        /// else cancelled. Returns the new building (for the undo), or null.
        /// </summary>
        public PlacedBuilding? EndDragBuy(bool droppedOnUi)
        {
            if (placer == null || !placer.IsPlacing) return null;
            if (droppedOnUi || !placer.CurrentFits)
            {
                placer.Cancel();
                return null;
            }
            var count = _placed.Count;
            if (!placer.Confirm() || _placed.Count == count) return null;
            return _placed[_placed.Count - 1];
        }

        /// <summary>Takes back a building just bought by drag (the Undo toast): it goes, the coins come back in full.</summary>
        public bool UndoBuy(PlacedBuilding b, int price)
        {
            if (!_placed.Contains(b) || grid == null) return false;
            if (_economyStarted && !economy!.CanUndoBuy(b.uid)) return false;
            if (_economyStarted) economy!.OnRemoved(b);
            grid.Release(b.cells);
            _placed.Remove(b);
            Magic.Vanish(b.gameObject);
            Earn(price);
            if (_selected == b) _selected = null;
            IslandAudio.Play("whoosh", null, 0.6f, 0f);
            Save();
            return true;
        }
    }
}
