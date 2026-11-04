#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// Playable prototype of the open island: a new island starts with the starter pack placed
    /// near the middle; everything else is bought from the shop and placed anywhere free.
    /// Tap a building to move or sell it. The island is saved on this device (PlayerPrefs) so it
    /// survives a restart. Coins here are a stand-in until it is wired to the real economy.
    /// The panel is drawn with IMGUI on purpose: it is a dev harness, the game UI comes later.
    /// </summary>
    public sealed partial class IslandSandbox : MonoBehaviour
    {
        [SerializeField] private BuildingCatalogue? catalogue;
        [SerializeField] private IslandGrid? grid;
        [SerializeField] private BuildPlacer? placer;
        [SerializeField] private IslandCamera? islandCamera;
        [SerializeField] private IslandNature? nature;
        [SerializeField, Tooltip("Coins a new island starts with: enough for a few first buildings, so choices matter.")] private int startCoins = 1500;
        [SerializeField] private string saveKey = "fv_island_v1";
        [SerializeField, Tooltip("The farmer the player walks around with the thumb-stick (Content/Characters/Prefabs/char_farmer).")] private GameObject? playerPrefab;
        [SerializeField, Tooltip("The walking economy: carrying, pads, energy (added at runtime if missing).")] private IslandEconomyHost? economy;
        [SerializeField, Tooltip("The farmer's horse (Content/Characters/Prefabs/char_horse).")] private GameObject? horsePrefab;
        [SerializeField, Tooltip("A tap on open ground farther than this (m) from the farmer rides the horse there.")] private float rideFrom = 14f;
        [SerializeField, Tooltip("The farmhouse's five rooms (Content/Art/Rooms: living, kitchen, bedroom, bathroom, study), used when the Farmhouse scene isn't in the build.")] private GameObject[] roomPrefabs = new GameObject[0];

        [Serializable] private sealed class SaveItem { public string id = ""; public int uid, x, z, w, d, turns; public bool alt; }
        [Serializable] private sealed class SaveData { public int coins; public List<SaveItem> items = new List<SaveItem>(); public List<int> cleared = new List<int>(); public List<Regrow> regrow = new List<Regrow>(); public int chapter; public int layout; }
        [Serializable] private sealed class Regrow { public int index; public double at; }

        /// <summary>Chopped trees grow back after this long (rocks and bushes too), if nothing's been built there.</summary>
        private const double RegrowSeconds = 1200;
        private readonly List<Regrow> _regrow = new List<Regrow>();
        private float _nextRegrowCheck;

        /// <summary>Wood and stone are renewable: a cleared spot grows its tree (or turns up its rock) again when it's free.</summary>
        private void RegrowNature()
        {
            if (nature == null || grid == null || Time.time < _nextRegrowCheck || _regrow.Count == 0) return;
            _nextRegrowCheck = Time.time + 10f;
            var now = IslandEconomyHost.Clock;
            var changed = false;
            foreach (var r in _regrow.Where(r => now - r.at >= RegrowSeconds).ToList())
            {
                var p = nature.Any(r.index);
                if (p == null) { _regrow.Remove(r); continue; }
                var free = grid.Cells(p.cells).All(c => grid.OwnerAt(c.x, c.y) == 0);
                if (!free) { r.at = now - RegrowSeconds + 300; continue; }   // something stands there: look again later
                _regrow.Remove(r);
                _cleared.Remove(r.index);
                nature.Regrow(p, grid);
                changed = true;
            }
            if (changed) Save();
        }

        private int _coins;   // only until the economy starts; then coins live there
        private bool _economyStarted;
        private readonly List<PlacedBuilding> _placed = new List<PlacedBuilding>();
        private PlacedBuilding? _selected;
        private NatureProp? _selectedProp;
        private IslandTrain? _train;
        private readonly List<int> _cleared = new List<int>();
        private PlayerAvatar? _player;
        private VirtualJoystick? _stick;
        private HorseRide? _ride;
        private bool _hookedChops;
        private bool _fresh;
        private bool _tapRide;
        private float _refusedSaidAt = -100f;
        private bool _followingTrain;
        private IslandChapters? _chapters;
        private int _chapter = 1;

        /// <summary>The chapter the island has grown to (docs: Island by Chapter).</summary>
        public int IslandChapter => _chapter;
        public IslandChapters? Chapters => _chapters;

        /// <summary>For testing (the editor's Farmverse / World / Island Chapter menu): show the island at a chapter now, smaller or bigger.</summary>
        public void DevSetChapter(int chapter)
        {
            if (_chapters == null) return;
            if (chapter < _chapter && nature != null) nature.SinkAbove(chapter, grid!);
            _chapters.Apply(chapter);
            _chapter = _chapters.Chapter;
            Save();
        }

        /// <summary>Play the island's growth to a chapter, then remember it.</summary>
        public void GrowIsland(int to, Action? done = null)
        {
            if (_chapters == null || to <= _chapter || Visiting) { done?.Invoke(); return; }
            _chapters.Grow(to, () =>
            {
                _chapter = _chapters.Chapter;
                Save();
                done?.Invoke();
            });
        }
        private IslandAudio.LoopSound? _trainSound;

        private void OnEnable() => EnhancedTouchSupport.Enable();

        [NonSerialized] private bool _started;

        private void Start()
        {
            _started = true;
            // prices come from the level plan (GameplayRules.BuildingPrice), on a copy so the asset keeps its own
            if (catalogue != null)
            {
                catalogue = Instantiate(catalogue);
                foreach (var e in catalogue.entries) e.price = GameplayRules.BuildingPrice(e.id, e.price);
            }
            IslandShadow.RegisterDeviceKey(saveKey);
            placer!.Placed += OnPlaced;
            // a fishing shed needs the sea within a few cells of its plot
            placer.Rule = (e, cells) => e.id != "fishing_shed" || NearWater(cells, GameplayRules.ShoreCells);
            placer.RuleHint = e => e.id == "fishing_shed" ? "must stand by the sea" : null;
            var save = ReadSave();
            var layout = grid!.Data.layoutVersion;
            // cleared trees are remembered by index: a redesigned island has other trees under those numbers
            if (save != null && save.layout == layout) { _cleared.AddRange(save.cleared); _regrow.AddRange(save.regrow); }
            // the island was redrawn smaller since this save: its buildings move in towards the middle with the land
            if (save != null && save.layout > 0 && save.layout == layout - 1 && Mathf.Abs(grid.Data.previousLayoutScale - 1f) > 0.01f)
            {
                var d = grid.Data;
                foreach (var it in save.items)
                {
                    float cx = d.origin.x + (it.x + it.w * 0.5f) * d.cellSize, cz = d.origin.y + (it.z + it.d * 0.5f) * d.cellSize;
                    cx *= d.previousLayoutScale; cz *= d.previousLayoutScale;
                    it.x = Mathf.RoundToInt((cx - d.origin.x) / d.cellSize - it.w * 0.5f);
                    it.z = Mathf.RoundToInt((cz - d.origin.y) / d.cellSize - it.d * 0.5f);
                }
                save.layout = layout;
                _movedIn = true;
            }
            // the island's chapter: as saved, or (an island from before chapters) the one that holds all its buildings
            _chapters = GetComponent<IslandChapters>();
            _chapter = save == null ? 1 : save.chapter > 0 ? save.chapter
                : _chapters != null ? _chapters.ChapterHolding(save.items.Select(it => new RectInt(it.x, it.z, it.w, it.d))) : 1;
            if (_chapters != null) { _chapters.Apply(_chapter); _chapter = _chapters.Chapter; }
            if (nature != null)
            {
                nature.Setup(grid!, _cleared, _chapter);
                // a rebuilt island can grow new trees or rocks where you'd already built: those make way
                if (save != null)
                    foreach (var p in nature.Props.ToList())
                        if (save.items.Any(it => new RectInt(it.x, it.z, it.w, it.d).Overlaps(p.cells)))
                        {
                            _cleared.Add(p.index);
                            grid!.Release(p.cells);
                            nature.Hide(p);
                        }
            }
            if (save == null || !Load(save)) NewIsland();
            StartEconomy();
            SpawnPlayer();
        }

        private int Coins => _economyStarted ? economy!.Economy.Coins : _coins;

        private void StartEconomy()
        {
            if (economy == null && !TryGetComponent(out economy)) economy = gameObject.AddComponent<IslandEconomyHost>();
            economy!.Nature = nature;
            if (!_hookedChops)
            {
                _hookedChops = true;
                economy.PropCleared += prop =>
                {
                    if (nature == null) return;
                    _cleared.Add(prop.index);
                    _regrow.Add(new Regrow { index = prop.index, at = IslandEconomyHost.Clock });   // it grows back in time
                    nature.Clear(prop, grid!);
                    Save();
                };
            }
            economy.PriceOf = id => catalogue != null ? catalogue.Find(id)?.price ?? 0 : 0;
            economy.Begin(grid!, _coins, _placed.Where(p => p != null));
            _economyStarted = true;
            if (_fresh)
            {
                // a new island's farmhouse comes with a little wood for the first buildings
                _fresh = false;
                var farmhouse = _placed.FirstOrDefault(p => p != null && p.id == "farmhouse");
                if (farmhouse != null) economy.Economy.GrantStarterWood(farmhouse.uid);
                economy.Save();
            }
            if (economy.Life != null)
            {
                economy.Life.RideRequested = at =>
                {
                    if (_ride == null || _player == null) return;
                    _ride.RideTo(at);
                    if (islandCamera != null) islandCamera.Follow = _player.transform;   // the camera rides along
                };
                economy.Life.LookRequested = at => { if (islandCamera != null) { islandCamera.Follow = null; islandCamera.FocusOn(at); } };
            }
        }

        private void Spend(int coins)
        {
            if (_economyStarted) economy!.Economy.SpendCoins(coins); else _coins -= coins;
        }

        private void Earn(int coins)
        {
            if (_economyStarted) economy!.Economy.AddCoins(coins); else _coins += coins;
        }

        /// <summary>The farmer and the thumb-stick that walks them; the camera follows the farmer.</summary>
        private void SpawnPlayer()
        {
            if (playerPrefab == null || islandCamera == null) return;
            var go = Instantiate(playerPrefab);
            go.name = "Player";
            if (!go.TryGetComponent<CharacterController>(out var cc)) cc = go.AddComponent<CharacterController>();
            PlayerAvatar.FitController(cc);
            if (!go.TryGetComponent<PlayerAvatar>(out var avatar)) avatar = go.AddComponent<PlayerAvatar>();
            _player = avatar;
            _stick = gameObject.AddComponent<VirtualJoystick>();
            _stick.Blocked = OverUi;
            _player.Joystick = _stick;
            _player.View = islandCamera.transform;
            _ride = go.AddComponent<HorseRide>();
            _ride.HorsePrefab = horsePrefab;
            _ride.Stick = _stick;
            _ride.Grid = grid;
            _ride.MayCross = owner => true;   // the horse goes through anything but the sea, like everyone
            // the horse lives in a stable and eats from its tank: no stable (or a hungry one), no ride
            _ride.CanRide = () => !_economyStarted || economy!.Economy.HorseReady;
            _ride.Mounted = () =>
            {
                if (!_economyStarted) return;
                economy!.Economy.PayForRide();
                economy.Animals?.HorseTaken();   // the stable's horse is the one that came
            };
            _ride.KeepHorse = horse => _economyStarted && economy!.Animals != null && economy.Animals.AdoptHorse(horse);
            _ride.Refused = () =>
            {
                if (!_economyStarted) return;
                // a full-tilt stick asks every moment: say it once in a while, a tap to ride says it every time
                if (!_tapRide && Time.unscaledTime < _refusedSaidAt + 12f) return;
                _refusedSaidAt = Time.unscaledTime;
                economy!.Say(WhyNoHorse(), 2.5f);
                IslandAudio.Play("error", null, 0.5f, 0f, 2f);
            };
            if (economy?.Animals != null) economy.Animals.Riding = () => _ride != null && _ride.Riding;
            var near = _placed.Count > 0 ? _placed[0].transform.position : Vector3.zero;
            _player.PlaceAt(near + new Vector3(7f, 0, -7f));
            economy?.SetPlayer(_player);
            islandCamera.Follow = go.transform;
            islandCamera.Distance = 24f;   // close enough to see the farmer, wide enough to see the farm
            islandCamera.DragPans = false;
            islandCamera.KeysPan = false;
        }

        private void NewIsland()
        {
            _fresh = true;
            _coins = startCoins;
            var centre = Vector3.zero;
            foreach (var e in catalogue!.entries.Where(e => e.starter && e.prefab != null))
            {
                if (grid!.FindFree(centre, e.footprint, out var cells))
                    _placed.Add(placer!.Spawn(e, cells, 0));
            }
            if (_placed.Count > 0) islandCamera!.FocusOn(_placed[0].transform.position);
            Save();
        }

        private void OnPlaced(BuildingCatalogue.Entry e, PlacedBuilding pb)
        {
            if (!_placed.Contains(pb))
            {
                _placed.Add(pb);
                Spend(e.price);
                if (_economyStarted) economy!.OnBought(pb, e.price);
                IslandAudio.Play("buy", null, 0.8f, 0f);
            }
            else
            {
                if (_economyStarted) economy!.OnMoved(pb);
                IslandAudio.Play("drop", pb.transform.position, 0.8f);
            }
            Save();
        }

        private void Update()
        {
            RegrowNature();
            if (_insideOf != null || Lobby)
            {
                if (_insideOf != null) RoomInput();   // look around the room: drag to pan, pinch or scroll to zoom
                // inside the farmhouse, or on the home screen: the island waits
                if (_stick != null) _stick.Active = false;
                if (_player != null) _player.InputEnabled = false;
                if (Lobby && islandCamera != null) islandCamera.Follow = _player != null ? _player.transform : null;
                return;
            }
            if (_train == null) _train = FindAnyObjectByType<IslandTrain>();
            if (_train != null)
            {
                // the freight train comes in while the economy's train is at the station
                _train.Present = _economyStarted && economy != null && economy.Economy.Train != null && !Visiting;
                if (_trainSound == null && _train.Engine != null)
                {
                    _trainSound = IslandAudio.Loop("truck_loop", true);
                    if (_trainSound != null) _trainSound.Follow = _train.Engine;
                }
                if (_trainSound != null) _trainSound.Target = _train.Moving ? 0.7f : 0f;
            }
            var screen = PointerScreen(out bool down);
            if (islandCamera != null) islandCamera.PointerOverUi = OverUi(screen);
            // the view pulls back while riding, so you can see where you're going, and comes back in on foot
            if (islandCamera != null) islandCamera.ZoomBoost = _ride != null && _ride.Riding ? 1.6f : 1f;
            if (_player != null)
            {
                // while a building is being placed the drag moves it and the camera pans; otherwise a drag in the stick's zone walks the farmer and any other drag pans
                var placing = placer!.IsPlacing;
                _stick!.Active = !placing;
                _player.InputEnabled = !placing && _insideOf == null && (_ride == null || !_ride.Riding);
                if (placing && _ride != null && _ride.Riding) _ride.Dismount();
                islandCamera!.DragPans = placing || !_stick.Holding;
                islandCamera.KeysPan = placing;
                if (!placing && islandCamera.Follow == null && !_followingTrain && _stick.Engaged) islandCamera.Follow = _player.transform;
                if (placing) islandCamera.Follow = null;
                // a tap (press and release without dragging, anywhere off the stick) selects or rides
                var tapped = WorldTap(screen, down);
                if (placing || tapped == null || OverUi(tapped.Value)) return;
                screen = tapped.Value;
            }
            else if (placer!.IsPlacing || !down || islandCamera!.PointerOverUi) return;
            // tap on a building selects it; a tap on open ground far from the farmer rides the horse there
            var cam = islandCamera!.GetComponent<Camera>();
            var ray = cam.ScreenPointToRay(screen);
            _selected = null;
            _selectedProp = null;
            if (Physics.Raycast(ray, out var hit, 2000f))
            {
                _selected = hit.collider.GetComponentInParent<PlacedBuilding>();
                _selectedProp = _selected == null ? hit.collider.GetComponentInParent<NatureProp>() : null;
            }
            if (_selected != null || _selectedProp != null || _player == null || _ride == null || horsePrefab == null) return;
            // open ground (the terrain has no collider): walk the ray down to the ground
            if (!GroundPoint(ray, out var ground)) return;
            if (new Vector2(ground.x - _player.transform.position.x, ground.z - _player.transform.position.z).magnitude <= rideFrom) return;
            if (!_ride.Riding) return;   // a tap far away rides there only when you're already on the horse (Ride button)
            _tapRide = true;
            _ride.RideTo(ground);
            _tapRide = false;
            islandCamera.Follow = _player.transform;
        }

        /// <summary>Swap a building's model for its other look; it keeps its place, its id and everything it holds.</summary>
        private void Restyle(PlacedBuilding old, BuildingCatalogue.Entry e)
        {
            grid!.Release(old.cells);
            var now = placer!.Spawn(e, old.cells, old.turns, old.uid, !old.alt);
            _placed[_placed.IndexOf(old)] = now;
            economy?.ReplaceBuilding(old, now);
            Destroy(old.gameObject);
            Magic.Pop(now.gameObject);
            _selected = now;
            Save();
        }

        // ---- inside the farmhouse -------------------------------------------------------------

        private GameObject? _rooms;
        private Camera? _roomCamera;
        private PlacedBuilding? _insideOf;
        private int _room;   // 0 living room, 1 kitchen
        private readonly List<GameObject> _roomGoods = new List<GameObject>();
        private readonly List<(Transform room, Vector3 open)> _roomViews = new List<(Transform, Vector3)>();

        /// <summary>
        /// Step inside: the farmhouse's rooms are open-sided dioramas (two walls, a floor), so each is
        /// seen from its open corner, a little from above, like a doll's house: the living room or the
        /// kitchen, one at a time, with the farmhouse's stock stacked as crates on the floor. They sit far
        /// off in the sky, out of the island's way, lit warmly, against a soft dark background.
        /// </summary>
        private void EnterFarmhouse(PlacedBuilding house)
        {
            if (islandCamera == null) return;
            if (_rooms == null)
            {
                // the rooms live in their own scene (Scenes/Farmhouse, loaded on top while you're inside); without it, from the prefabs
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("Farmhouse");
                if (!scene.isLoaded && UnityEngine.Application.CanStreamedLevelBeLoaded("Farmhouse") && !_roomsLoading)
                {
                    _roomsLoading = true;
                    _goingInto = house;
                    var op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Farmhouse", UnityEngine.SceneManagement.LoadSceneMode.Additive);
                    if (op != null) op.completed += _ => { _roomsLoading = false; if (_goingInto != null) EnterFarmhouse(_goingInto); };
                    return;
                }
                if (scene.isLoaded)
                    foreach (var go in scene.GetRootGameObjects()) if (go.name == "Farmhouse rooms") _rooms = go;
                if (_rooms == null)
                {
                    if (roomPrefabs.Length == 0) return;
                    _rooms = new GameObject("Farmhouse rooms");
                    _rooms.transform.position = new Vector3(4000f, 300f, 4000f);
                    for (var i = 0; i < roomPrefabs.Length; i++) if (roomPrefabs[i] != null) Room(roomPrefabs[i], new Vector3(i * 40f, 0, 0));
                }
                // each room is open on its front-right corner (walls stand at the back and on the left)
                foreach (Transform room in _rooms.transform)
                    if (room.name.StartsWith("room_")) { _roomViews.Add((room, new Vector3(1f, 0f, -1f))); Light(room); }
                var camGo = new GameObject("Room camera");
                camGo.transform.SetParent(_rooms.transform, false);
                _roomCamera = camGo.AddComponent<Camera>();
                _roomCamera.clearFlags = CameraClearFlags.SolidColor;
                _roomCamera.backgroundColor = new Color(0.17f, 0.12f, 0.09f);
                _roomCamera.fieldOfView = 38f;
                _roomCamera.nearClipPlane = 0.1f;
                _roomCamera.farClipPlane = 200f;
            }
            _goingInto = null;
            _insideOf = house;
            _rooms.SetActive(true);
            _room = Mathf.Clamp(_room, 0, Mathf.Max(0, RoomCount - 1));
            ShowRoom(_room);
            islandCamera.GetComponent<Camera>().enabled = false;
            if (economy != null) economy.HudHidden = true;
            _selected = null;
            IslandAudio.Play("drop", null, 0.4f, 0f, 0.5f, 1.3f);
        }

        private bool _roomsLoading;
        private PlacedBuilding? _goingInto;

        /// <summary>Show the furniture the player owns in every room (each extra is a hidden child opt_&lt;id&gt;).</summary>
        private void FurnishRooms()
        {
            if (_rooms == null || economy == null) return;
            var eco = economy.Economy;
            foreach (var t in _rooms.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("opt_")) t.gameObject.SetActive(eco.HasRoomItem(t.name.Substring(4)));
        }

        private static void Light(Transform room)
        {
            if (room.Find("Lamp") != null) return;
            var lamp = new GameObject("Lamp").AddComponent<Light>();
            lamp.transform.SetParent(room, false);
            lamp.transform.localPosition = new Vector3(0, 2.6f, 0);
            lamp.type = LightType.Point;
            lamp.range = 9f;
            lamp.intensity = 2.2f;
            lamp.color = new Color(1f, 0.86f, 0.66f);
            lamp.shadows = LightShadows.Soft;
        }

        private Transform Room(GameObject prefab, Vector3 at)
        {
            var room = Instantiate(prefab, _rooms!.transform);
            room.transform.localPosition = at;
            room.transform.localRotation = Quaternion.identity;
            foreach (var c in room.GetComponentsInChildren<Collider>()) c.enabled = false;
            return room.transform;
        }

        /// <summary>Frame one room from its open corner so it fills the screen, whatever its shape.</summary>
        private void ShowRoom(int index)
        {
            if (_roomCamera == null || _roomViews.Count == 0) return;
            _room = Mathf.Clamp(index, 0, Mathf.Max(0, RoomCount - 1));
            FurnishRooms();
            var (room, open) = _roomViews[_room];
            var bounds = new Bounds(room.position, Vector3.zero);
            var first = true;
            foreach (var r in room.GetComponentsInChildren<Renderer>()) { if (first) bounds = r.bounds; else bounds.Encapsulate(r.bounds); first = false; }
            var centre = bounds.center - Vector3.up * bounds.extents.y * 0.35f;
            var across = new Vector2(bounds.size.x, bounds.size.z).magnitude;
            var aspect = Mathf.Max(0.3f, (float)Screen.width / Mathf.Max(1, Screen.height));
            var hfov = 2f * Mathf.Atan(Mathf.Tan(_roomCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * aspect);
            var vfov = _roomCamera.fieldOfView * Mathf.Deg2Rad;
            // far enough that the whole floor fits across and the walls fit up
            var dist = Mathf.Max(across * 0.5f / Mathf.Tan(hfov * 0.5f), bounds.size.y * 1.1f / Mathf.Tan(vfov * 0.5f)) * 1.05f;
            var dir = (open.normalized * Mathf.Cos(38f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(38f * Mathf.Deg2Rad)).normalized;
            // the fitted view is where looking around starts (and what a new room resets to)
            _roomCentre = centre;
            _roomDir = dir;
            _roomFit = dist;
            _roomZoom = 1f;
            _roomPan = Vector2.zero;
            _roomExtent = new Vector2(across * 0.5f, bounds.size.y * 0.5f);
            _roomDragAt = null;
            _roomPinch = 0f;
            PlaceRoomCamera();
            ShowRoomGoods(room, bounds, open);
        }

        // ---- looking around a room ---------------------------------------------------------------

        /// <summary>Closest zoom (a fraction of the fitted distance) and furthest (a little wider than the fit).</summary>
        private const float RoomZoomIn = 0.4f, RoomZoomOut = 1.15f;

        private Vector3 _roomCentre, _roomDir;
        private float _roomFit, _roomZoom = 1f;
        private Vector2 _roomPan, _roomExtent;
        private Vector2? _roomDragAt;
        private float _roomPinch;

        /// <summary>The room camera from the fitted view, moved by the pan (across and up the screen) and the zoom.</summary>
        private void PlaceRoomCamera()
        {
            if (_roomCamera == null) return;
            var forward = -_roomDir;
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var up = Vector3.Cross(forward, right).normalized;
            var target = _roomCentre + right * _roomPan.x + up * _roomPan.y;
            _roomCamera.transform.position = target + _roomDir * (_roomFit * _roomZoom);
            _roomCamera.transform.LookAt(target, Vector3.up);
        }

        /// <summary>
        /// Drag (one finger or the mouse) pans, pinch or the scroll wheel zooms. The closer the zoom, the further
        /// the view may pan, so the room never slides out of sight; a press on the game's UI does nothing here.
        /// </summary>
        private void RoomInput()
        {
            if (_roomCamera == null) return;
            var before = (_roomPan, _roomZoom);
            var touches = Touch.activeTouches;
            Vector2? point = null;
            var pressed = false;

            if (touches.Count >= 2)
            {
                var a = touches[0].screenPosition;
                var b = touches[1].screenPosition;
                var gap = Vector2.Distance(a, b);
                var mid = (a + b) * 0.5f;
                if (_roomPinch > 0f && gap > 1f) _roomZoom *= _roomPinch / gap;
                else if (OverUi(mid)) gap = 0f;   // a pinch that starts on the UI isn't ours
                _roomPinch = gap;
                point = _roomPinch > 0f ? mid : (Vector2?)null;
                pressed = point != null && _roomDragAt == null;
            }
            else
            {
                _roomPinch = 0f;
                if (touches.Count == 1)
                {
                    point = touches[0].screenPosition;
                    pressed = touches[0].phase == UnityEngine.InputSystem.TouchPhase.Began;
                }
                else if (Mouse.current != null && Mouse.current.leftButton.isPressed)
                {
                    point = Mouse.current.position.ReadValue();
                    pressed = Mouse.current.leftButton.wasPressedThisFrame;
                }
            }

            if (point == null) _roomDragAt = null;
            else if (pressed || _roomDragAt == null)
            {
                // a drag starts only off the UI, and keeps going even if it slides over it
                _roomDragAt = pressed && !OverUi(point.Value) ? point : _roomDragAt;
            }
            else
            {
                var vfov = _roomCamera.fieldOfView * Mathf.Deg2Rad;
                var perPixel = 2f * _roomFit * _roomZoom * Mathf.Tan(vfov * 0.5f) / Mathf.Max(1, Screen.height);
                _roomPan -= (point.Value - _roomDragAt.Value) * perPixel;
                _roomDragAt = point;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                var scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !OverUi(mouse.position.ReadValue())) _roomZoom *= Mathf.Pow(0.88f, Mathf.Sign(scroll));
            }

            _roomZoom = Mathf.Clamp(_roomZoom, RoomZoomIn, RoomZoomOut);
            var slack = Mathf.Clamp01((1.1f - _roomZoom) / (1.1f - RoomZoomIn));
            _roomPan = new Vector2(
                Mathf.Clamp(_roomPan.x, -_roomExtent.x * slack, _roomExtent.x * slack),
                Mathf.Clamp(_roomPan.y, -_roomExtent.y * slack, _roomExtent.y * slack));
            if (before != (_roomPan, _roomZoom)) PlaceRoomCamera();
        }

        private void LeaveFarmhouse()
        {
            if (_rooms != null) _rooms.SetActive(false);
            _insideOf = null;
            if (economy != null) economy.HudHidden = false;
            if (islandCamera != null) islandCamera.GetComponent<Camera>().enabled = true;
        }

        /// <summary>What's in store, as neat stacks of crates on the open half of the floor (a crate for every five).</summary>
        private void ShowRoomGoods(Transform room, Bounds bounds, Vector3 open)
        {
            foreach (var g in _roomGoods) Destroy(g);
            _roomGoods.Clear();
            if (_insideOf == null || !economy!.Economy.Departments.TryGetValue(_insideOf.uid, out var d)) return;
            var floor = bounds.min.y + 0.22f;
            var o = new Vector3(open.x, 0, open.z).normalized;
            var side = new Vector3(-o.z, 0, o.x);
            var start = new Vector3(bounds.center.x, floor, bounds.center.z) + o * (Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.95f);
            var i = 0;
            foreach (var kv in d.Stock.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(8))
            {
                var pile = new GameObject(kv.Key).transform;
                pile.SetParent(room, true);
                pile.position = start + side * ((i % 4) - 1.5f) * 0.75f - o * (i / 4) * 0.75f;
                pile.rotation = Quaternion.LookRotation(o, Vector3.up);
                var crates = Mathf.Clamp(Mathf.CeilToInt(kv.Value / 5f), 1, 6);
                var model = ItemCatalog.Load()?.Good(kv.Key);
                for (var c = 0; c < crates; c++)
                {
                    GameObject g;
                    if (model != null) { g = Instantiate(model, pile); foreach (var col in g.GetComponentsInChildren<Collider>()) col.enabled = false; }
                    else
                    {
                        g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Destroy(g.GetComponent<Collider>());
                        g.transform.SetParent(pile, false);
                        g.transform.localScale = Vector3.one * 0.32f;
                        g.GetComponent<Renderer>().material.color = IslandEconomyHost.GoodColour(kv.Key);
                    }
                    g.transform.localPosition = new Vector3((c % 2 - 0.5f) * 0.36f, (c / 2) * 0.3f, 0);
                    g.transform.localRotation = Quaternion.Euler(0, UnityEngine.Random.Range(-25f, 25f), 0);
                }
                _roomGoods.Add(pile.gameObject);
                i++;
            }
        }

        /// <summary>Is there sea within <paramref name="reach"/> cells of these cells?</summary>
        private bool NearWater(RectInt cells, int reach)
        {
            if (grid == null) return true;
            for (var z = cells.yMin - reach; z < cells.yMax + reach; z++)
                for (var x = cells.xMin - reach; x < cells.xMax + reach; x++)
                    if (!grid.Dry(x, z)) return true;
            return false;
        }

        /// <summary>Why the horse won't come, from the stable's actual state.</summary>
        private string WhyNoHorse()
        {
            var eco = economy!.Economy;
            var stables = _placed.Where(p => p != null && p.id == "stable").Select(p => p.uid).ToList();
            if (stables.Count == 0) return "Build a stable to keep a horse";
            if (stables.Any(u => eco.Working(u)))
            {
                var d = stables.Where(eco.Working).Select(u => eco.Departments.TryGetValue(u, out var s) ? s : null).FirstOrDefault(s => s != null);
                return d != null && d.Present == 0 ? "The stable has no horse yet — feed it so one moves in" : "The horse is hungry — walk some grain or food into the stable";
            }
            if (stables.Any(eco.IsRuined)) return "The stable was wrecked — builders are fixing it";
            var site = eco.Sites.Where(c => stables.Contains(c.Uid)).OrderByDescending(c => c.Progress).FirstOrDefault();
            if (site == null) return "The stable isn't ready yet";
            return site.Supplied || site.Missing.Count == 0
                ? $"Your stable is being built ({site.Progress:P0}) — the horse comes when it's done"
                : $"Your stable is waiting for materials: {MissingText(site)}";
        }

        private static string MissingText(Construction c) => string.Join(", ", c.Missing.Select(m => $"{m.Value} {m.Key}"));

        /// <summary>Where a screen ray meets the island's ground (stepping along it against the height field).</summary>
        private static bool GroundPoint(Ray ray, out Vector3 at)
        {
            at = default;
            if (ray.direction.y >= -0.01f) return false;
            for (var t = 0f; t < 1500f; t += 1f)
            {
                var p = ray.GetPoint(t);
                if (!GroundHeight.TryAt(p.x, p.z, out var y) || p.y > y) continue;
                at = new Vector3(p.x, y, p.z);
                return true;
            }
            return false;
        }

        private Vector2? _pressAt;
        private float _pressTime;
        private bool _pressMoved;

        /// <summary>
        /// A quick press and release that didn't drag, off the stick and off the panels: where it was, or
        /// null. (Drags pan the camera; presses on the stick walk the farmer.)
        /// </summary>
        private Vector2? WorldTap(Vector2 screen, bool pressedThisFrame)
        {
            var held = Touch.activeTouches.Count > 0 || (Mouse.current != null && Mouse.current.leftButton.isPressed);
            if (pressedThisFrame)
            {
                var onStick = _stick != null && _stick.InZone(screen);
                _pressAt = onStick || OverUi(screen) ? (Vector2?)null : screen;
                _pressTime = Time.unscaledTime;
                _pressMoved = false;
                return null;
            }
            if (_pressAt == null) return null;
            var slop = 14f * Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 720f);
            if (held)
            {
                if ((screen - _pressAt.Value).magnitude > slop) _pressMoved = true;
                return null;
            }
            var at = _pressAt.Value;
            _pressAt = null;
            return !_pressMoved && Time.unscaledTime - _pressTime < 0.5f ? at : (Vector2?)null;
        }

        private static Vector2 PointerScreen(out bool pressedThisFrame)
        {
            pressedThisFrame = false;
            if (Touch.activeTouches.Count > 0)
            {
                var t = Touch.activeTouches[0];
                pressedThisFrame = t.phase == UnityEngine.InputSystem.TouchPhase.Began;
                return t.screenPosition;
            }
            var m = Mouse.current;
            if (m == null) return Vector2.zero;
            pressedThisFrame = m.leftButton.wasPressedThisFrame;
            return m.position.ReadValue();
        }

        /// <summary>A press over the game's UI never walks the farmer, pans the camera or selects a building.</summary>
        private static bool OverUi(Vector2 screen) => IslandHud.Over(screen);


        // ---- visiting another island ------------------------------------------------------------

        private readonly List<GameObject> _visitShown = new List<GameObject>();
        private readonly List<GameObject> _visitHidden = new List<GameObject>();

        /// <summary>True while another player's island is on screen instead of this one.</summary>
        public bool Visiting { get; private set; }

        /// <summary>
        /// Show another player's buildings (id + cells, as the server last ran their island) in place of this
        /// island's: ours are hidden (they keep running, nothing is saved differently), theirs are look-only
        /// models, and trees or rocks where they built step aside. <see cref="EndVisit"/> puts everything back.
        /// </summary>
        public void ShowVisit(IEnumerable<(string id, int x, int z, int w, int d)> buildings)
        {
            EndVisit();
            Visiting = true;
            // their island may be bigger than ours: show as much land as their buildings stand on
            buildings = buildings.ToList();
            if (_chapters != null) _chapters.Apply(Mathf.Max(_chapter, _chapters.ChapterHolding(buildings.Select(b => new RectInt(b.x, b.z, b.w, b.d)))), false);
            Deselect();
            foreach (var p in _placed)
                if (p != null && p.gameObject.activeSelf) { p.gameObject.SetActive(false); _visitHidden.Add(p.gameObject); }
            // our workers, builders and animals stay home too (they keep working, out of sight)
            var bodies = new List<GameObject>();
            if (economy != null && economy.Life != null) bodies.AddRange(economy.Life.ShownBodies);
            if (economy != null && economy.Animals != null) bodies.AddRange(economy.Animals.ShownBodies);
            foreach (var go in bodies) { go.SetActive(false); _visitHidden.Add(go); }
            var cells = new List<RectInt>();
            foreach (var (id, x, z, w, d) in buildings)
            {
                var e = catalogue != null ? catalogue.Find(id) : null;
                if (e == null || e.prefab == null) continue;
                var rect = new RectInt(x, z, w, d);
                var turns = e.footprint.x == w && e.footprint.y == d ? 0 : 1;
                _visitShown.Add(placer!.SpawnPreview(e, rect, turns));
                cells.Add(rect);
            }
            if (nature != null)
                foreach (var prop in nature.Props)
                    if (prop.gameObject.activeSelf && cells.Any(c => c.Overlaps(prop.cells))) { prop.gameObject.SetActive(false); _visitHidden.Add(prop.gameObject); }
            if (_visitShown.Count > 0) islandCamera!.FocusOn(_visitShown[0].transform.position);
        }

        /// <summary>Back to this island.</summary>
        public void EndVisit()
        {
            foreach (var go in _visitShown) if (go != null) Destroy(go);
            foreach (var go in _visitHidden) if (go != null) go.SetActive(true);
            _visitShown.Clear();
            _visitHidden.Clear();
            if (Visiting && _chapters != null && _chapters.Chapter != _chapter) _chapters.Apply(_chapter, false);
            if (Visiting && _placed.Count > 0 && _placed[0] != null) islandCamera!.FocusOn(_placed[0].transform.position);
            Visiting = false;
        }

        // ---- save -------------------------------------------------------------------------------

        private void Save()
        {
            if (!_started) return;   // after a script reload in play mode: never write a half-empty island over the save
            var s = new SaveData { coins = Coins, cleared = new List<int>(_cleared), regrow = new List<Regrow>(_regrow), chapter = _chapter, layout = grid != null ? grid.Data.layoutVersion : 0 };
            foreach (var p in _placed.Where(p => p != null))
                s.items.Add(new SaveItem { id = p.id, uid = p.uid, x = p.cells.x, z = p.cells.y, w = p.cells.width, d = p.cells.height, turns = p.turns, alt = p.alt });
            PlayerPrefs.SetString(saveKey, JsonUtility.ToJson(s));
            PlayerPrefs.Save();
        }

        private SaveData? ReadSave()
        {
            if (!PlayerPrefs.HasKey(saveKey)) return null;
            try { return JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(saveKey)); }
            catch { return null; }
        }

        private bool _movedIn;

        private RectInt? FreeSpotNear(RectInt r)
        {
            for (int ring = 1; ring <= 40; ring++)
                for (int dz = -ring; dz <= ring; dz++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;
                        var c = new RectInt(r.x + dx, r.y + dz, r.width, r.height);
                        if (grid!.CanPlace(c)) return c;
                    }
            return null;
        }

        private bool Load(SaveData s)
        {
            if (s.items.Count == 0) return false;
            _coins = s.coins;
            foreach (var it in s.items)
            {
                var e = catalogue!.Find(it.id);
                if (e == null || e.prefab == null) continue;
                var cells = new RectInt(it.x, it.z, it.w, it.d);
                if (_movedIn && !grid!.CanPlace(cells)) cells = FreeSpotNear(cells) ?? cells;   // moved in with the land: find it room
                _placed.Add(placer!.Spawn(e, cells, it.turns, it.uid, it.alt));
            }
            if (_placed.Count > 0) islandCamera!.FocusOn(_placed[0].transform.position);
            return true;
        }

        public void ResetIsland()
        {
            foreach (var p in _placed) if (p != null) { grid!.Release(p.cells); Destroy(p.gameObject); }
            _placed.Clear();
            _selected = null; _selectedProp = null;
            _cleared.Clear();
            _regrow.Clear();
            if (placer!.IsPlacing) placer.Cancel();
            _chapter = 1;
            _chapters?.Apply(1);
            if (nature != null) nature.RestoreAll(grid!, 1);   // every tree and rock grows back (the island is small again)
            PlayerPrefs.DeleteKey(saveKey);
            economy?.Forget();
            _economyStarted = false;
            NewIsland();
            StartEconomy();
        }

        // ---- panel ------------------------------------------------------------------------------

    }
}
