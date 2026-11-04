#nullable enable
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// Free placement on the island grid. While placing, the building follows the finger or mouse,
    /// snaps to the grid, turns in quarter steps and shows green where it fits and red where it
    /// does not (water, slopes, other buildings). Confirm puts it down and takes its cells.
    /// </summary>
    public sealed class BuildPlacer : MonoBehaviour
    {
        [SerializeField] private IslandGrid? grid;
        [SerializeField] private IslandCamera? islandCamera;
        [SerializeField] private Color okTint = new Color(0.55f, 1f, 0.55f, 1f);
        [SerializeField] private Color badTint = new Color(1f, 0.45f, 0.45f, 1f);

        public bool IsPlacing => _ghost != null;
        public bool CurrentFits { get; private set; }
        /// <summary>Extra placement rules on top of free, flat ground (a fishing shed must be by the sea).</summary>
        public System.Func<BuildingCatalogue.Entry, RectInt, bool>? Rule { get; set; }
        /// <summary>Why the current spot doesn't fit, when a rule says no.</summary>
        public System.Func<BuildingCatalogue.Entry, string?>? RuleHint { get; set; }
        public BuildingCatalogue.Entry? Current => _entry;
        /// <summary>Where the building being placed stands now (for the bar under it), or the origin.</summary>
        public Vector3 GhostPosition => _ghost != null ? _ghost.transform.position : Vector3.zero;
        /// <summary>A building already on the island is being moved (not bought).</summary>
        public bool Moving => _moving != null;
        public event Action<BuildingCatalogue.Entry, PlacedBuilding>? Placed;
        public event Action? Cancelled;

        private BuildingCatalogue.Entry? _entry;
        private GameObject? _ghost;
        private PlacedBuilding? _moving;
        private int _turns;
        private RectInt _cells;
        private MaterialPropertyBlock? _mpb;
        private int _nextUid = 1;


        private void OnEnable() => EnhancedTouchSupport.Enable();

        public void BeginNew(BuildingCatalogue.Entry entry, Vector3 near)
        {
            Cancel();
            _entry = entry;
            _turns = 0;
            _ghost = Instantiate(entry.prefab!);
            SetColliders(_ghost, false);
            MoveTo(near);
        }

        public void BeginMove(PlacedBuilding b, BuildingCatalogue.Entry entry)
        {
            Cancel();
            _entry = entry;
            _moving = b;
            _turns = b.turns;
            grid!.Release(b.cells);
            _ghost = b.gameObject;
            SetColliders(_ghost, false);
            MoveTo(grid.CentreOf(b.cells));
        }

        public void Rotate()
        {
            if (_ghost == null) return;
            _turns = (_turns + 1) % 4;
            MoveTo(grid!.CentreOf(_cells));
        }

        public void Cancel()
        {
            if (_ghost == null) return;
            if (_moving != null)
            {
                // put it back where it was
                grid!.Occupy(_moving.cells, _moving.uid);
                Place(_ghost, _moving.cells, _moving.turns);
                Tint(_ghost, null);
                SetColliders(_ghost, true);
            }
            else Destroy(_ghost);
            _ghost = null; _moving = null; _entry = null;
            Cancelled?.Invoke();
        }

        public bool Confirm()
        {
            if (_ghost == null || _entry == null || !CurrentFits) return false;
            var pb = _moving != null ? _moving : _ghost.AddComponent<PlacedBuilding>();
            if (_moving == null) { pb.uid = _nextUid++; pb.id = _entry.id; }
            pb.cells = _cells;
            pb.turns = _turns;
            grid!.Occupy(_cells, pb.uid);
            Tint(_ghost, null);
            SetColliders(_ghost, true);
            var e = _entry;
            _ghost = null; _moving = null; _entry = null;
            Placed?.Invoke(e, pb);
            return true;
        }

        /// <summary>Puts a building down without the ghost step (loading a saved island, starter pack).</summary>
        public PlacedBuilding Spawn(BuildingCatalogue.Entry entry, RectInt cells, int turns, int uid = 0, bool alt = false)
        {
            alt = alt && entry.altPrefab != null;
            var go = Instantiate(alt ? entry.altPrefab! : entry.prefab!);
            var pb = go.AddComponent<PlacedBuilding>();
            pb.id = entry.id; pb.uid = uid > 0 ? uid : _nextUid++; pb.cells = cells; pb.turns = turns; pb.alt = alt;
            _nextUid = Mathf.Max(_nextUid, pb.uid + 1);
            Place(go, cells, turns);
            if (alt && go.TryGetComponent<BoxCollider>(out var box) && box.size.x > 0.1f && box.size.z > 0.1f)
            {
                // the classic model is sized for the old world: fit it to the same plot
                var cell = grid!.Data.cellSize;
                var fit = Mathf.Min(entry.footprint.x * cell / box.size.x, entry.footprint.y * cell / box.size.z) * 0.95f;
                go.transform.localScale = Vector3.one * Mathf.Clamp(fit, 0.3f, 3f);
            }
            grid!.Occupy(cells, pb.uid);
            return pb;
        }

        /// <summary>A building to look at only (another player's island): placed, not on the grid, not clickable.</summary>
        public GameObject SpawnPreview(BuildingCatalogue.Entry entry, RectInt cells, int turns)
        {
            var go = Instantiate(entry.prefab!);
            Place(go, cells, turns);
            SetColliders(go, false);
            return go;
        }

        public Vector2Int FootprintFor(BuildingCatalogue.Entry e, int turns) =>
            turns % 2 == 0 ? e.footprint : new Vector2Int(e.footprint.y, e.footprint.x);

        private void Update()
        {
            if (islandCamera != null) islandCamera.PanEnabled = _ghost == null || !PointerHeld();
            if (_ghost == null) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.rKey.wasPressedThisFrame) Rotate();
                if (kb.escapeKey.wasPressedThisFrame) { Cancel(); return; }
                if (kb.enterKey.wasPressedThisFrame) { Confirm(); return; }   // not Space: IMGUI uses it to press the focused button
            }
            if (islandCamera != null && islandCamera.PointerOverUi) return;
            if (PointerHeld() && TryPointerGround(out var hit)) MoveTo(hit);
        }

        private static bool PointerHeld()
        {
            if (Touch.activeTouches.Count == 1) return true;
            var m = Mouse.current;
            return m != null && m.leftButton.isPressed;
        }

        private bool TryPointerGround(out Vector3 point)
        {
            point = default;
            Vector2 screen;
            if (Touch.activeTouches.Count > 0) screen = Touch.activeTouches[0].screenPosition;
            else if (Mouse.current != null) screen = Mouse.current.position.ReadValue();
            else return false;
            var cam = islandCamera != null ? islandCamera.GetComponent<Camera>() : Camera.main;
            var ray = cam.ScreenPointToRay(screen);
            foreach (var h in Physics.RaycastAll(ray, 2000f))
                if (h.collider.GetComponentInParent<IslandGround>() != null) { point = h.point; return true; }
            var plane = new Plane(Vector3.up, Vector3.up * grid!.Data.seaLevel);
            if (plane.Raycast(ray, out var enter)) { point = ray.GetPoint(enter); return true; }
            return false;
        }

        private void MoveTo(Vector3 world)
        {
            if (_ghost == null || _entry == null) return;
            var size = FootprintFor(_entry, _turns);
            _cells = grid!.FootprintAt(world, size);
            CurrentFits = grid.CanPlace(_cells) && (Rule == null || Rule(_entry, _cells));
            Place(_ghost, _cells, _turns);
            _ghost.transform.position += Vector3.up * 0.25f;   // lifted while being placed
            Tint(_ghost, CurrentFits ? okTint : badTint);
        }

        private void Place(GameObject go, RectInt cells, int turns)
        {
            go.transform.SetPositionAndRotation(grid!.CentreOf(cells), Quaternion.Euler(0, turns * 90f, 0));
        }

        private void Tint(GameObject go, Color? c)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (c == null) { r.SetPropertyBlock(null); continue; }
                _mpb ??= new MaterialPropertyBlock();
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", c.Value);
                _mpb.SetColor("_Color", c.Value);
                r.SetPropertyBlock(_mpb);
            }
        }

        private static void SetColliders(GameObject go, bool on)
        {
            foreach (var col in go.GetComponentsInChildren<Collider>()) col.enabled = on;
        }
    }
}
