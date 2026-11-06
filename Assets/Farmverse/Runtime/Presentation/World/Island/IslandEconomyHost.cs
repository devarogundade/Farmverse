#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// The walking economy on the island (docs: farmverse-gameplay-design). The farmer does real work
    /// by being there: standing at a ripe field harvests it a bunch at a time, standing at a coop
    /// collects its eggs, and everything gathered rides on their back. Every department (farmhouse,
    /// army house, workers' house, builders' hut, market) has a glowing pad at its door; standing on
    /// it hands the load over — stored, turned into that department's energy, or sold. The rules
    /// live in <see cref="IslandEconomy"/> (engine-free); this draws them and feeds them the farmer's
    /// position and the clock. Saved on the device beside the island's buildings.
    /// </summary>
    public sealed partial class IslandEconomyHost : MonoBehaviour
    {
        [SerializeField] private WorldVisualCatalog? worldVisuals;
        [SerializeField] private string saveKey = "fv_economy_v1";
        [SerializeField, Tooltip("How close to a pad's centre the farmer must stand to unload.")] private float padReach = 1.7f;
        [SerializeField, Tooltip("How far outside a field or animal house the farmer can still work it.")] private float workReach = 1.6f;
        [SerializeField] private float unloadEvery = 0.08f;
        [SerializeField] private float harvestEvery = 0.16f;
        [SerializeField] private float collectEvery = 0.22f;

        private sealed class Site
        {
            public PlacedBuilding Building = null!;
            public GameObject? Pad;
            public PadRing? Ring;
            public GameObject? Crop;
            public string CropShown = "";
            public VisualStage StageShown;
            public GameObject? Dressing;   // the working machine beside a factory (oven by the bakery, saw by the lumber yard)
            public VisualStage DressingShown = (VisualStage)(-1);
        }

        /// <summary>The old world's machine models, set beside the factories that do that work.</summary>
        private static readonly Dictionary<string, string> Dressings = new Dictionary<string, string>();   // the pack buildings carry their own machines

        /// <summary>Tell a model's staged parts which look to show (idle, working, with goods waiting).</summary>
        private static void Stage(GameObject go, int uid, string id, VisualStage stage, bool product)
        {
            var p = go.transform.position;
            var v = new WorldVisual("island/" + uid + "/" + id, "d" + uid, id, stage, product ? VisualFlags.Collectable : VisualFlags.None, new GroundPoint(p.x, p.z), 0);
            foreach (var r in go.GetComponentsInChildren<IVisualStateReceiver>()) r.OnVisualState(v, false);
        }

        private float _nextBloom;
        private float _nextWorkStep;
        private float _foodAt = -100f;
        private double? _foodHours;

        /// <summary>
        /// The work switches on screen: hungry houses with Restock on get a supply order (farm workers carry the
        /// food over), and the island sells surplus and loads train crates where those are switched on.
        /// </summary>
        private void WorkOrders(double now)
        {
            foreach (var d in Economy.Departments.Values)
            {
                if (d.Levels.Kind is not (DepartmentKind.WorkersHouse or DepartmentKind.ArmyHouse) || !Economy.Working(d.Uid)) continue;
                if (!Economy.WorkOn(d.Uid, WorkJob.Restock) || Economy.SupplyOrders.ContainsKey(d.Uid)) continue;
                if (d.Energy < GameplayRules.RestockBelow) Economy.OrderSupplies(d.Uid, GameplayRules.RestockTo - d.Energy);
            }
            var r = Economy.OnScreenStep(now);
            if (r.SoldCoins > 0) Toast($"Workers sold {r.Sold} surplus goods: +{r.SoldCoins} coins", 2.5f);
            if (r.Crates > 0) Toast(r.Crates == 1 ? "Workers loaded a truck crate" : $"Workers loaded {r.Crates} truck crates", 2.5f);
        }

        /// <summary>
        /// Hours of offline work the food on hand would pay for (a dry run on a copy of the island), or null when
        /// food lasts the whole shift. Worked out at most every 20 seconds.
        /// </summary>
        public double? FoodHours()
        {
            if (!_begun) return null;
            if (Time.unscaledTime - _foodAt < 20f) return _foodHours;
            _foodAt = Time.unscaledTime;
            try { _foodHours = IslandEconomy.FoodHours(IslandSim.Load(CurrentState()), Clock); }
            catch { _foodHours = null; }
            return _foodHours;
        }

        /// <summary>The hours offline work would run this time (8 to 24).</summary>
        public double OfflineCapHours => Economy.OfflineCapHours;
        private string _seasonShown = "";
        private MaterialPropertyBlock? _seasonBlock;

        /// <summary>Bee hives count the wild flower patches round them (the economy adds flower beds itself).</summary>
        private void Bloom(double now)
        {
            if (Nature == null) return;
            var flowers = Nature.Props.Where(p => p != null && p.kind == "flowers" && p.gameObject.activeInHierarchy).Select(p => p.transform.position).ToList();
            foreach (var kv in _sites)
            {
                if (kv.Value.Building.id != "bee_hives") continue;
                var at = Centre(kv.Value.Building);
                var near = flowers.Count(f => Flat(f - at).magnitude <= GameplayRules.BloomReach);
                Economy.SetWildFlowers(kv.Key, near, now);
            }
        }

        /// <summary>A seasonal display dresses for the season: blossom pink, summer gold, autumn orange, winter frost.</summary>
        private void SeasonalDisplays(double now)
        {
            var season = Economy.SeasonNow(now).Name;
            var colour = season switch
            {
                "Spring" => new Color(1f, 0.78f, 0.86f),
                "Summer" => new Color(1f, 0.93f, 0.55f),
                "Autumn" => new Color(1f, 0.62f, 0.32f),
                "Winter" => new Color(0.82f, 0.9f, 1f),
                _ => Color.white,
            };
            var key = season + _sites.Values.Count(s => s.Building.id == "seasonal_default");
            if (key == _seasonShown) return;
            _seasonShown = key;
            _seasonBlock ??= new MaterialPropertyBlock();
            foreach (var site in _sites.Values)
            {
                if (site.Building.id != "seasonal_default") continue;
                foreach (var r in site.Building.GetComponentsInChildren<Renderer>())
                {
                    r.GetPropertyBlock(_seasonBlock);
                    _seasonBlock.SetColor("_BaseColor", colour);
                    _seasonBlock.SetColor("_Color", colour);
                    r.SetPropertyBlock(_seasonBlock);
                }
            }
        }

        /// <summary>The building's model changed (restyled): keep everything that hangs off it.</summary>
        public void ReplaceBuilding(PlacedBuilding old, PlacedBuilding now)
        {
            if (!_sites.TryGetValue(old.uid, out var site)) return;
            site.Building = now;
            if (!Economy.Plots.ContainsKey(now.uid)) Stage(now.gameObject, now.uid, now.id, VisualStage.Idle, false);
            OnMoved(now);
            _highlight?.Clear();
        }

        private readonly Dictionary<int, Site> _sites = new Dictionary<int, Site>();
        private IslandGrid? _grid;
        private PlayerAvatar? _player;
        private Transform? _backpack;
        private readonly List<Renderer> _backBoxes = new List<Renderer>();
        private string _backShown = "";
        private float _nextWork;
        private float _nextSave;
        private int _workSite = -1;
        // taking goods out of a store: which good, how many, and the store whose auto-unload is paused
        private int _keepBasketAt = -1;
        private int _lastStoreAt = -1;
        private WorkKind _work;
        private string _toast = "";
        private float _toastUntil;
        private Highlight? _highlight;
        private IslandAudio.LoopSound? _walkSound, _runSound, _gallopSound, _seaSound;
        private Vector3 _lastPlayerAt;
        private float _playerSpeed;
        private int _streak;   // goods moved in a row: each one a touch higher, like coins stacking
        private AwayReport? _report;

        private enum WorkKind { None, Unload, Harvest, Plant, Collect, Machine, Chop, Feed, Post, Build }

        public IslandEconomy Economy { get; private set; } = new IslandEconomy(0);
        /// <summary>
        /// Set by <see cref="Begin"/>. Not kept over a script reload in play mode: the economy itself can't
        /// survive one, so until the island is started again nothing ticks or saves (a blank economy must
        /// never be written over the player's save).
        /// </summary>
        [NonSerialized] private bool _begun;
        public IslandGrid? Grid => _grid;
        /// <summary>A building's catalogue price (its health and materials scale with it).</summary>
        public Func<string, int>? PriceOf { get; set; }
        private readonly Dictionary<int, bool> _ruinShown = new Dictionary<int, bool>();
        /// <summary>The island's trees, bushes and rocks (the farmer chops them for wood and stone).</summary>
        public IslandNature? Nature { get; set; }
        /// <summary>A tree, bush or rock was chopped down (the sandbox frees its land and saves).</summary>
        public event Action<NatureProp>? PropCleared;
        private NatureProp? _chopping;
        private int _chops;
        public PlayerAvatar? Player => _player;
        /// <summary>Workers, builders, warriors and raiders (added with the economy).</summary>
        public IslandLife? Life { get; private set; }
        public IslandAnimals? Animals { get; private set; }
        /// <summary>The farmer is at some work (unloading, harvesting, chopping…), not just standing about.</summary>
        public bool FarmerBusy => _work != WorkKind.None;
        /// <summary>Hide the basket, labels and panels (the farmer has gone indoors).</summary>
        public bool HudHidden { get; set; }

        public static double Clock => (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        // ---- set-up (called by the sandbox) -------------------------------------------------------

        /// <summary>Start the economy over the buildings already on the island; restores the saved state if there is one.</summary>
        public void Begin(IslandGrid grid, int coins, IEnumerable<PlacedBuilding> placed)
        {
            _grid = grid;
            _begun = true;
            var now = Clock;
            Economy = new IslandEconomy(coins);
            foreach (var b in placed) Add(b, now);
            var save = ReadSave();
            IslandShadow.RegisterDeviceKey(saveKey);
            _report = null;
            if (save != null)
            {
                // shadow mode: the server engine runs the same span from the same save (IslandShadow compares)
                IslandShadow.Offer(Snapshot(save, coins, placed));
                Economy.Restore(save);
                // the farm workers kept going while the game was closed
                var report = Economy.WorkOffline(save.now, now);
                if (report.Seconds >= 300 && report.Workers > 0) _report = report;
            }
            Economy.BackOnline();   // the player is here: the next absence starts its own shift
            Economy.Tick(now);
            // a brand-new island: still offered, so a new phone can find the player's farm in the cloud
            if (save == null) IslandShadow.Offer(CurrentState());
            if (Life == null) Life = TryGetComponent(out IslandLife life) ? life : gameObject.AddComponent<IslandLife>();
            Life.Begin(this);
            if (Animals == null) Animals = TryGetComponent(out IslandAnimals animals) ? animals : gameObject.AddComponent<IslandAnimals>();
            Animals.Begin(this);
            // the season and the weather, seen and heard (cosmetic)
            var weather = TryGetComponent(out IslandWeatherFx fx) ? fx : gameObject.AddComponent<IslandWeatherFx>();
            weather.Begin(this);
        }

        /// <summary>The island as saved, in the shape the server engine runs (<see cref="IslandState"/>).</summary>
        /// <summary>The island as it is now, in the shape the server engine runs (for a fresh server copy).</summary>
        public IslandState CurrentState() =>
            Snapshot(Economy.ToSave(), Economy.Coins, _sites.Values.Select(s => s.Building).Where(b => b != null));

        private IslandState Snapshot(EconomySave save, int coins, IEnumerable<PlacedBuilding> placed)
        {
            var state = new IslandState { simAt = save.now, startCoins = coins, economy = JsonUtility.FromJson<EconomySave>(JsonUtility.ToJson(save)) };
            foreach (var b in placed)
                state.buildings.Add(new IslandState.Placed
                {
                    id = b.id, uid = b.uid, x = b.cells.x, z = b.cells.y, w = b.cells.width, d = b.cells.height,
                    price = PriceOf?.Invoke(b.id) ?? 0,
                });
            return state;
        }

        public void SetPlayer(PlayerAvatar player)
        {
            _player = player;
            _backpack = new GameObject("Basket").transform;
            _backpack.SetParent(player.transform, false);
            _backpack.localPosition = new Vector3(0f, PlayerHeight(player) * 0.42f, -0.36f);   // the pile starts at the small of the back, behind it
            _highlight = gameObject.AddComponent<Highlight>();
        }


        /// <summary>A building just bought: it goes up as a construction site the builders work on (a field is quick).</summary>
        public void OnBought(PlacedBuilding b, int price)
        {
            Add(b, Clock, GameplayRules.BuildWork(b.id, price));
            var site = Economy.Sites.FirstOrDefault(c => c.Uid == b.uid);
            if (site != null && site.Materials.Count > 0 && !site.Supplied)
            {
                // say what's short and where it comes from, not just what it needs
                int Have(string item) => Economy.Departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && Economy.Working(d.Uid))
                    .Sum(d => d.Stock.TryGetValue(item, out var n) ? n : 0);
                var short_ = site.Materials.Where(m => Have(m.item) < m.count).ToList();
                if (short_.Count == 0)
                    Toast("Builders take " + string.Join(", ", site.Materials.Select(m => $"{m.count} {Pretty(m.item)}")) + " from the farmhouse");
                else
                {
                    var m = short_[0];
                    var where = m.item switch { "wood" => "chop trees", "planks" => "a lumber yard makes them", "stone" => "break rocks", _ => "bring some" };
                    Toast($"Needs {m.count} {Pretty(m.item)} in the farmhouse (has {Have(m.item)}) — {where}", 3.5f);
                }
            }
            Life?.OnSiteStarted(b);
            Save();
        }

        public void OnRemoved(PlacedBuilding b)
        {
            if (_sites.TryGetValue(b.uid, out var site))
            {
                if (site.Pad != null) Destroy(site.Pad);
                if (site.Crop != null) Destroy(site.Crop);
                if (site.Dressing != null) Destroy(site.Dressing);
                _sites.Remove(b.uid);
            }
            Economy.RemoveBuilding(b.uid);
            Save();
        }

        /// <summary>A building was moved: its pad and crop go with it.</summary>
        /// <summary>Beside the building, on its right, scaled to about 2.5 m across, standing on the ground.</summary>
        private void PlaceDressing(Site site)
        {
            if (site.Dressing == null) return;
            var b = site.Building;
            var size = Size(b);
            var right = b.transform.rotation * Vector3.right;
            var reach = Mathf.Abs(right.x) * size.x * 0.5f + Mathf.Abs(right.z) * size.y * 0.5f + 1.6f;
            var at = Centre(b) + right * reach;
            var d = site.Dressing.transform;
            d.localScale = Vector3.one;
            var bounds = new Bounds(d.position, Vector3.zero);
            var first = true;
            foreach (var r in d.GetComponentsInChildren<Renderer>()) { if (first) bounds = r.bounds; else bounds.Encapsulate(r.bounds); first = false; }
            var across = Mathf.Max(bounds.size.x, bounds.size.z);
            if (across > 0.01f) d.localScale = Vector3.one * Mathf.Clamp(2.5f / across, 0.2f, 2f);
            d.rotation = b.transform.rotation;
            d.position = new Vector3(at.x, GroundHeight.At(at.x, at.z, at.y), at.z);
            d.position -= Vector3.up * GroundHeight.FootOffset(d.gameObject);
        }

        public void OnMoved(PlacedBuilding b)
        {
            if (!_sites.TryGetValue(b.uid, out var site)) return;
            PlaceDressing(site);
            if (site.Pad != null) PlacePad(site);
            var at = Centre(b);
            Economy.Place(b.uid, at.x, at.z);
            if (site.Crop != null) site.Crop.transform.SetPositionAndRotation(Centre(b), b.transform.rotation);
        }

        public void Forget()
        {
            PlayerPrefs.DeleteKey(saveKey);
            foreach (var s in _sites.Values)
            {
                if (s.Pad != null) Destroy(s.Pad);
                if (s.Crop != null) Destroy(s.Crop);
                if (s.Dressing != null) Destroy(s.Dressing);
            }
            _sites.Clear();
            Life?.Clear();
            Animals?.Clear();
        }

        private void Add(PlacedBuilding b, double now, double buildWork = 0)
        {
            Economy.AddBuilding(b.uid, b.id, now, buildWork, PriceOf?.Invoke(b.id) ?? 0);
            var at = Centre(b);
            Economy.Place(b.uid, at.x, at.z);
            var site = new Site { Building = b };
            _sites[b.uid] = site;
            if (Economy.Plots.ContainsKey(b.uid))
            {
                // the soil bed is a staged world model: show its resting look
                var p = b.transform.position;
                var soil = new WorldVisual("island/" + b.uid + "/soil", "p" + b.uid, GameplayRules.FieldBuilding, VisualStage.Idle, VisualFlags.None, new GroundPoint(p.x, p.z), 0);
                foreach (var r in b.GetComponentsInChildren<IVisualStateReceiver>()) r.OnVisualState(soil, false);
            }
            else Stage(b.gameObject, b.uid, b.id, VisualStage.Idle, false);   // staged models (decorations, the well) show their resting look
            if (Dressings.TryGetValue(b.id, out var dressing) && worldVisuals != null && worldVisuals.Prefab(dressing) is { } dp)
            {
                site.Dressing = Instantiate(dp, transform);
                site.Dressing.name = dressing;
                foreach (var c in site.Dressing.GetComponentsInChildren<Collider>()) c.enabled = false;
                PlaceDressing(site);
            }
            if (Economy.Departments.ContainsKey(b.uid))
            {
                site.Pad = new GameObject("Pad " + b.id);
                site.Pad.transform.SetParent(transform, false);
                // no glowing ring any more: you walk into the building itself; the pad is just its doorstep
                PlacePad(site);
            }
        }

        // ---- per frame ----------------------------------------------------------------------------

        private void Update()
        {
            if (_grid == null || !_begun) return;
            var now = Clock;
            Economy.Tick(now);
            if (Economy.TakeClosed() is { } closed)
            {
                if (closed.Done) { Toast($"Order delivered: +{closed.Reward:N0} coins", 3.5f); IslandAudio.Play("cheer", null, 0.7f, 0f, 1f); }
                else Toast($"The post office order for {closed.Count} {closed.Item.Replace('_', ' ')} ran out");
            }
            var spoiled = Economy.TakeSpoiled();
            if (spoiled > 0) Toast(spoiled == 1 ? "Eggs or milk went off in a full house — collect more often" : $"{spoiled} fresh goods went off — collect more often", 3f);
            var storm = Economy.TakeStormHit();
            if (storm >= 0 && Building(storm) is { } struck)
            {
                Toast($"A storm damaged the {Name(struck.id).ToLowerInvariant()} — builders will mend it", 3f);
                Magic.Shake(struck.gameObject, Vector3.forward, 3f);
                IslandAudio.Play("hit", struck.transform.position, 0.8f, 0.1f, 0.5f, 0.6f);
            }
            var rotted = Economy.TakeRotted();
            if (rotted > 0) Toast(rotted == 1 ? "A crop rotted in the field — harvest sooner, or get farm workers" : $"{rotted} crops rotted in the fields — harvest sooner, or get farm workers", 3f);
            if (Economy.TakeLevelUp() && Economy.BestLevel > 1)
            {
                var opens = Economy.BestLevel <= 26;   // every level up to 26 opens a building
                Toast(opens ? $"Island level {Economy.BestLevel}! New in the shop" : $"Island level {Economy.BestLevel}!", 4f);
                IslandAudio.Play("built", null, 0.9f, 0f, 1f);
            }
            if (Time.time >= _nextBloom) { _nextBloom = Time.time + 10f; Bloom(now); }
            if (Time.time >= _nextWorkStep) { _nextWorkStep = Time.time + 8f; WorkOrders(now); }
            RefreshVisuals(now);
            DoWork(now);
            RefreshBack();
            Footsteps();
            PlayerToolTick();
            if (Time.unscaledTime >= _nextSave)
            {
                _nextSave = Time.unscaledTime + 5f;
                Save();
            }
        }

        private void DoWork(double now)
        {
            _workSite = -1;
            _work = WorkKind.None;
            if (_player == null || _player.Riding) { _highlight?.Clear(); return; }   // work is done on foot
            var at = _player.transform.position;

            // walking into a building site or a damaged building with the materials it needs: hand them over
            // (first, so a ruined farmhouse still takes the wood for its own repair)
            foreach (var kv in _sites)
            {
                if (!Economy.SiteWants(kv.Key) || !Near(kv.Value.Building, at, 0.4f)) continue;
                _workSite = kv.Key;
                _work = WorkKind.Build;
                break;
            }

            // walking into a department (or onto its pad) hands the load over
            foreach (var kv in _sites)
            {
                if (_work != WorkKind.None) break;
                if (kv.Value.Pad == null) continue;
                var p = kv.Value.Pad.transform.position;
                if (Flat(at - p).magnitude > padReach && !Near(kv.Value.Building, at, 0.2f)) continue;
                _workSite = kv.Key;
                _work = WorkKind.Unload;
                break;
            }
            // leaving a store ends the pause on unloading there
            if (_workSite != _keepBasketAt) _keepBasketAt = -1;
            if (_workSite != _lastStoreAt) _lastStoreAt = _workSite;
            // walking into a machine: take what it made, give it what it uses
            if (_work == WorkKind.None)
                foreach (var kv in _sites)
                {
                    if (!Economy.Machines.ContainsKey(kv.Key) || !Economy.IsBuilt(kv.Key) || !Near(kv.Value.Building, at, 0.3f)) continue;
                    _workSite = kv.Key;
                    _work = WorkKind.Machine;
                    break;
                }
            // walking into the post office with goods for its order: hand them over
            if (_work == WorkKind.None && Economy.Orders.Any(o => Economy.Basket.Stacks.Any(st => st.Item == o.Item)))
                foreach (var kv in _sites)
                {
                    if (kv.Value.Building.id != "post_office" || !Economy.Working(kv.Key) || !Near(kv.Value.Building, at, 0.3f)) continue;
                    _workSite = kv.Key;
                    _work = WorkKind.Post;
                    break;
                }
            // walking into a tree, bush or rock: chop it
            NatureProp? prop = null;
            if (_work == WorkKind.None && Nature != null && _playerSpeed < 2.5f)   // stop at a tree to chop it; running past doesn't
            {
                var fwd = _player.transform.forward;
                foreach (var probe in new[] { at, at + fwd * 0.9f })
                {
                    var cell = _grid!.Data.CellOf(probe);
                    var owner = _grid.OwnerAt(cell.x, cell.y);
                    if (owner < 0 && Nature.Find(-owner - 1) is { } standing && standing.kind != "pond") { prop = standing; break; }
                }
                if (prop != null) _work = WorkKind.Chop;
            }
            if (prop != _chopping) { _chopping = prop; _chops = 0; }
            if (_work == WorkKind.None)
            {
                foreach (var kv in _sites)
                {
                    if (!Near(kv.Value.Building, at, workReach) || !Economy.IsBuilt(kv.Key)) continue;
                    if (Economy.Plots.TryGetValue(kv.Key, out var plot))
                    {
                        var state = plot.State(now);
                        if (state == PlotState.Ripe) { _workSite = kv.Key; _work = WorkKind.Harvest; break; }
                        if (state == PlotState.Empty) { _workSite = kv.Key; _work = WorkKind.Plant; break; }
                    }
                    else if (Economy.Animals.TryGetValue(kv.Key, out var house) && Economy.Working(kv.Key))
                    {
                        if (house.StockAt(now) > 0) { _workSite = kv.Key; _work = WorkKind.Collect; break; }
                        // nothing to collect: top up the trough from the basket if there's grain on your back
                        if (house.Producer.Eats && house.Feed < GameplayRules.TroughSize && Economy.Basket.Stacks.Any(st => GameplayRules.FeedValue(st.Item) > 0))
                        { _workSite = kv.Key; _work = WorkKind.Feed; break; }
                    }
                }
            }
            // the building you're in or working at glows
            if (_highlight != null)
            {
                if (_work == WorkKind.None) _highlight.Clear();
                else if (_work == WorkKind.Chop) _highlight.Show(_chopping!.gameObject, new Color(1f, 0.86f, 0.15f));
                else _highlight.Show(_sites[_workSite].Building.gameObject,
                    Economy.IsRuined(_workSite) || (_work == WorkKind.Unload && Economy.Departments.TryGetValue(_workSite, out var hd) && hd.Starving) ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.86f, 0.15f));
            }
            if (_work == WorkKind.None || _work == WorkKind.Plant) _streak = 0;
            if (_work == WorkKind.None || _work == WorkKind.Plant || Time.time < _nextWork) return;

            if (_work == WorkKind.Chop)
            {
                _nextWork = Time.time + 0.55f;
                Chop(_chopping!);
                return;
            }
            var site = _sites[_workSite];
            switch (_work)
            {
                case WorkKind.Machine:
                {
                    _nextWork = Time.time + collectEvery * 0.6f;
                    var m = Economy.Machines[_workSite];
                    var r = Economy.CollectMachineStep(_workSite, now);
                    PlayerTool("Wrench");
                    if (r == GatherResult.Took)
                    {
                        Fly(m.Recipe.Output, Centre(site.Building) + Vector3.up * 1.5f, BackPoint());
                        IslandAudio.Play("pop", null, 0.55f, 0.03f, 0.05f, 1f + Mathf.Min(_streak++, 12) * 0.035f);
                        break;
                    }
                    if (r == GatherResult.BasketFull) { Toast("Basket full — walk into the farmhouse to store it"); IslandAudio.Play("error", null, 0.6f, 0f, 1.5f); }
                    var fed = Economy.FeedMachineStep(_workSite, now);
                    if (fed != null)
                    {
                        Fly(fed, BackPoint(), Centre(site.Building) + Vector3.up * 1.5f);
                        IslandAudio.Play("drop", null, 0.55f, 0.05f, 0.05f, 1f + Mathf.Min(_streak++, 12) * 0.03f);
                    }
                    break;
                }
                case WorkKind.Unload:
                {
                    _nextWork = Time.time + unloadEvery;
                    if (_keepBasketAt == _workSite) break;   // you just took things out: don't put them straight back
                    var r = Economy.UnloadStep(_workSite);
                    if (r.Moved)
                    {
                        Fly(r.Item!, BackPoint(), site.Building.transform.position + Vector3.up * 2f);
                        var pitch = 1f + Mathf.Min(_streak++, 12) * 0.03f;
                        switch (r.Outcome)
                        {
                            case UnloadOutcome.Sold: IslandAudio.Play("coin", null, 0.7f, 0.02f, 0.05f, pitch); break;
                            case UnloadOutcome.Energy: IslandAudio.Play("eat", null, 0.55f, 0.08f, 0.12f, pitch); break;
                            default: IslandAudio.Play("drop", null, 0.6f, 0.05f, 0.05f, pitch); break;
                        }
                        if (Economy.Basket.IsEmpty) IslandAudio.Play("built", null, 0.5f);
                    }
                    else if (r.Outcome == UnloadOutcome.StorageFull) { Toast("The farmhouse is full — sell at a market, upgrade it, or build grain silos"); IslandAudio.Play("error", null, 0.6f, 0f, 1.5f); }
                    break;
                }
                case WorkKind.Harvest:
                {
                    _nextWork = Time.time + harvestEvery;
                    var crop = Economy.Plots[_workSite].Crop ?? "";
                    var r = Economy.HarvestStep(_workSite, now);
                    if (r == GatherResult.Took)
                    {
                        Fly(crop, Centre(site.Building) + Vector3.up * 0.6f, BackPoint());
                        PlayerTool(crop is "wheat" or "maize" ? "Rake" : crop is "pumpkin" or "beans" or "cabbage" ? "Shovel" : "GardenShears");
                        Motion()?.Play("Dig", 0.6f);
                        IslandAudio.Play("pop", null, 0.55f, 0.03f, 0.05f, 1f + Mathf.Min(_streak++, 12) * 0.035f);
                    }
                    else if (r == GatherResult.BasketFull) { Toast("Basket full — walk into the farmhouse to store it"); IslandAudio.Play("error", null, 0.6f, 0f, 1.5f); }
                    break;
                }
                case WorkKind.Build:
                {
                    _nextWork = Time.time + unloadEvery;
                    var given = Economy.GiveToSite(_workSite);
                    if (given == null) break;
                    Fly(given, BackPoint(), Centre(site.Building) + Vector3.up * 1.2f);
                    IslandAudio.Play("drop", null, 0.6f, 0.05f, 0.05f, 1f + Mathf.Min(_streak++, 12) * 0.03f);
                    var started = Economy.Sites.FirstOrDefault(c => c.Uid == _workSite);
                    if (started != null && started.Supplied)
                    {
                        IslandAudio.Play("built", null, 0.5f);
                        Toast(started.Repair ? $"Everything's here — builders will repair the {Name(site.Building.id).ToLowerInvariant()}"
                                             : $"Everything's here — builders will put up the {Name(site.Building.id).ToLowerInvariant()}", 2.6f);
                    }
                    break;
                }
                case WorkKind.Post:
                {
                    _nextWork = Time.time + unloadEvery;
                    var r = Economy.PostStep(_workSite);
                    if (!r.Moved) break;
                    Fly(r.Item!, BackPoint(), site.Building.transform.position + Vector3.up * 2f);
                    IslandAudio.Play(r.Outcome == UnloadOutcome.Sold ? "coin" : "drop", null, 0.65f, 0.03f, 0.05f, 1f + Mathf.Min(_streak++, 12) * 0.03f);
                    break;
                }
                case WorkKind.Feed:
                {
                    _nextWork = Time.time + collectEvery;
                    var fed = Economy.FeedAnimalsStep(_workSite, now);
                    if (fed != null)
                    {
                        PlayerTool("Bucket");
                        Fly(fed, BackPoint(), Centre(site.Building) + Vector3.up * 0.8f);
                        IslandAudio.Play("eat", null, 0.45f, 0.08f, 0.12f);
                    }
                    break;
                }
                case WorkKind.Collect:
                {
                    _nextWork = Time.time + collectEvery;
                    var item = Economy.Animals[_workSite].Producer.Item;
                    var r = Economy.CollectStep(_workSite, now);
                    if (r == GatherResult.Took)
                    {
                        Fly(item, Centre(site.Building) + Vector3.up * 1f, BackPoint());
                        PlayerTool(item == "milk" ? "Bucket" : null);
                        Motion()?.Play("Lift", 0.6f);
                        IslandAudio.Play("pop", null, 0.55f, 0.03f, 0.05f, 1f + Mathf.Min(_streak++, 12) * 0.035f);
                        if (item == "egg") IslandAudio.Play("chicken", null, 0.35f, 0.1f, 4f);
                    }
                    else if (r == GatherResult.BasketFull) { Toast("Basket full — walk into the farmhouse to store it"); IslandAudio.Play("error", null, 0.6f, 0f, 1.5f); }
                    break;
                }
            }
        }

        private void RefreshVisuals(double now)
        {
            SeasonalDisplays(now);
            foreach (var kv in _sites)
            {
                var site = kv.Value;
                // the machine beside a factory works when it does, and shows goods when there are some
                if (site.Dressing != null)
                {
                    site.Dressing.SetActive(Economy.Working(kv.Key));
                    if (Economy.Machines.TryGetValue(kv.Key, out var mach))
                    {
                        var st = mach.Busy ? VisualStage.Working : VisualStage.Idle;
                        if (st != site.DressingShown) { site.DressingShown = st; Stage(site.Dressing, kv.Key, site.Dressing.name, st, mach.Output > 0); }
                    }
                    else if (site.DressingShown != VisualStage.Idle) { site.DressingShown = VisualStage.Idle; Stage(site.Dressing, kv.Key, site.Dressing.name, VisualStage.Idle, false); }
                }
                if (site.Ring != null && Economy.Departments.TryGetValue(kv.Key, out var dept))
                {
                    var cost = Economy.UpgradeCost(kv.Key);
                    var fill = cost is int c && c > 0 ? Mathf.Clamp01(Economy.UpgradeFunds(kv.Key) / (float)c) : 1f;
                    var active = _work == WorkKind.Unload && _workSite == kv.Key && !Economy.Basket.IsEmpty;
                    site.Ring.Show(fill, active, dept.Starving);
                }
                if (Economy.Plots.TryGetValue(kv.Key, out var plot)) ShowCrop(site, plot, now);
                ShowRuin(kv.Key, site.Building);
            }
        }

        /// <summary>A ruin sinks a little, leans, and smokes; repaired, it springs back up.</summary>
        private void ShowRuin(int uid, PlacedBuilding b)
        {
            var ruined = Economy.IsRuined(uid);
            if (_ruinShown.TryGetValue(uid, out var was) && was == ruined) return;
            _ruinShown[uid] = ruined;
            var smoke = b.transform.Find("Ruin smoke");
            if (ruined)
            {
                b.transform.position += Vector3.down * 0.5f;
                b.transform.rotation *= Quaternion.Euler(3f, 0f, -4f);
                if (smoke == null) Smoke(b.transform);
                foreach (var r in b.GetComponentsInChildren<Renderer>())
                    if (r is not ParticleSystemRenderer) foreach (var m in r.materials) if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * 0.45f);
            }
            else if (was)
            {
                b.transform.position += Vector3.up * 0.5f;
                b.transform.rotation *= Quaternion.Inverse(Quaternion.Euler(3f, 0f, -4f));
                if (smoke != null) Destroy(smoke.gameObject);
                foreach (var r in b.GetComponentsInChildren<Renderer>())
                    if (r is not ParticleSystemRenderer) foreach (var m in r.materials) if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") / 0.45f);
                Magic.Pop(b.gameObject);
            }
        }

        private static void Smoke(Transform parent)
        {
            var go = new GameObject("Ruin smoke");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.up * 2f;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.25f, 0.24f, 0.23f, 0.55f), new Color(0.45f, 0.43f, 0.4f, 0.4f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 5f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1.2f;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = Magic.SmokeMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void ShowCrop(Site site, Plot plot, double now)
        {
            var crop = plot.Crop ?? "";
            if (crop != site.CropShown)
            {
                if (site.Crop != null) Destroy(site.Crop);
                site.Crop = null;
                site.CropShown = crop;
                site.StageShown = VisualStage.None;
                var prefab = crop.Length > 0 && worldVisuals != null ? worldVisuals.Prefab("crop_" + crop) : null;
                if (prefab != null)
                {
                    site.Crop = Instantiate(prefab, Centre(site.Building), site.Building.transform.rotation, transform);
                    foreach (var c in site.Crop.GetComponentsInChildren<Collider>()) c.enabled = false;
                }
            }
            if (site.Crop == null) return;
            var progress = plot.Progress(now);
            var stage = plot.State(now) == PlotState.Ripe ? VisualStage.Ready
                : progress < 0.15 ? VisualStage.Seeded : progress < 0.4 ? VisualStage.Sprout : progress < 0.7 ? VisualStage.Young : VisualStage.Grown;
            if (stage == site.StageShown) return;
            site.StageShown = stage;
            var p = site.Crop.transform.position;
            var visual = new WorldVisual("island/" + site.Building.uid, "p" + site.Building.uid, "crop_" + crop, stage,
                stage == VisualStage.Ready ? VisualFlags.Harvestable : VisualFlags.None, new GroundPoint(p.x, p.z), (int)(progress * 100));
            foreach (var r in site.Crop.GetComponents<IVisualStateReceiver>()) r.OnVisualState(visual, false);
        }

        // ---- the basket on the farmer's back ------------------------------------------------------

        private void RefreshBack()
        {
            if (_backpack == null) return;
            var key = string.Join(",", Economy.Basket.Stacks.Select(s => s.Item + s.Count));
            if (key == _backShown) return;
            _backShown = key;
            // the real goods, each fitted to a 20 cm block, packed two by two in neat layers on the farmer's back
            // (long things like logs lie across the back), at most three layers shown however full the basket is
            var boxes = new List<string>();
            foreach (var s in Economy.Basket.Stacks) for (var i = 0; i < s.Count; i++) boxes.Add(s.Item);
            for (var c = _backpack.childCount - 1; c >= 0; c--) Destroy(_backpack.GetChild(c).gameObject);
            _backBoxes.Clear();
            var items = Farmverse.Presentation.World.ItemCatalog.Load();
            const float cell = 0.17f;
            var shown = Mathf.Min(boxes.Count, 12);
            for (var i = 0; i < shown; i++)
            {
                // spread the kinds through the pile, so a mixed basket shows each
                var go = MakeGood(boxes[(int)((long)i * boxes.Count / shown)], items);
                go.transform.SetParent(_backpack, false);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localPosition = Vector3.zero;
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds; foreach (var rr in rs) b.Encapsulate(rr.bounds);
                    var size = _backpack.InverseTransformVector(b.size);
                    size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                    // long along z (into the back): turn it to lie across
                    if (size.z > size.x * 1.3f) { go.transform.localRotation = Quaternion.Euler(0, 90f, 0); size = new Vector3(size.z, size.y, size.x); }
                    var fit = cell / Mathf.Max(0.01f, Mathf.Max(size.x * 0.5f, Mathf.Max(size.y, size.z)));   // across, two cells wide at most
                    go.transform.localScale *= fit;
                }
                int layer = i / 4, slot = i % 4;
                go.transform.localPosition = new Vector3((slot % 2 - 0.5f) * cell, layer * cell * 0.95f, -(slot / 2) * cell * 0.9f);
                go.transform.localRotation = Quaternion.Euler(0, (i % 3 - 1) * 6f, 0) * go.transform.localRotation;   // not too tidy
                foreach (var rr in go.GetComponentsInChildren<Renderer>()) rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var r = go.GetComponentInChildren<Renderer>();
                if (r != null) _backBoxes.Add(r);
            }
        }

        /// <summary>A good as its pack model (a plain coloured crate if it has none).</summary>
        private static GameObject MakeGood(string item, Farmverse.Presentation.World.ItemCatalog? items)
        {
            var prefab = items?.Good(item);
            if (prefab != null)
            {
                var g = Instantiate(prefab);
                foreach (var c in g.GetComponentsInChildren<Collider>()) c.enabled = false;
                return g;
            }
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(cube.GetComponent<Collider>());
            cube.transform.localScale = Vector3.one * 0.24f;
            cube.GetComponent<Renderer>().material.color = ColourOf(item);
            return cube;
        }

        // ---- chopping -----------------------------------------------------------------------------

        /// <summary>Hits a tree, bush or rock takes, and what it gives.</summary>
        /// <summary>A farm worker finished chopping a prop: it goes (and may grow back), its wood or stone goes in <paramref name="into"/>.</summary>
        public void TakeProp(NatureProp p, List<string> into)
        {
            var (_, item, count) = ChopYield(p.kind);
            for (var i = 0; i < count; i++) into.Add(item);
            Economy.Count("chopped");
            if (_chopping == p) { _chopping = null; _chops = 0; }
            PropCleared?.Invoke(p);
        }

        /// <summary>What chopping this kind of prop gives.</summary>
        public static string YieldOf(string kind) => ChopYield(kind).item;

        private static (int hits, string item, int count) ChopYield(string kind) => kind switch
        {
            "rock" => (6, "stone", 4),
            "bush" => (2, "wood", 2),
            "flowers" => (1, "flowers", 3),
            "mushroom" => (2, "mushroom", 4),
            "apple" or "cherry" => (4, "wood", 3),
            _ => (5, "wood", 4),
        };

        private void Chop(NatureProp p)
        {
            var (hits, item, count) = ChopYield(p.kind);
            // the right tool in hand: a pickaxe for rock, shears for bushes and flowers, the axe for trees
            PlayerTool(p.kind == "rock" ? "Pickaxe" : p.kind is "bush" or "flowers" or "mushroom" ? "GardenShears" : "Axe");
            _chops++;
            Motion()?.Play("Chop", 0.6f);
            IslandAudio.Play(p.kind == "rock" ? "hit" : "chop", p.transform.position, 0.8f, 0.08f, 0.1f);
            // it shakes with each blow: a tree rocks away from the axe, a rock or bush jolts
            if (_player != null) Magic.Shake(p.gameObject, p.transform.position - _player.transform.position, p.kind is "rock" ? 0f : p.kind == "bush" ? 7f : 4f);
            Magic.Burst(p.transform.position + Vector3.up * 1.2f, p.kind == "rock" ? new Color(0.75f, 0.72f, 0.68f) : new Color(0.55f, 0.8f, 0.35f), 6, 0.6f);
            if (_chops < hits) return;
            var room = Economy.Basket.Free;
            if (room < count)
            {
                _chops = hits - 1;   // ready to fall as soon as there's room
                Toast("Basket full — walk into the farmhouse to store it first");
                IslandAudio.Play("error", null, 0.6f, 0f, 1.5f);
                return;
            }
            Economy.Basket.Add(item, count);
            Economy.Count("chopped");
            for (var i = 0; i < count; i++) Fly(item, p.transform.position + Vector3.up * (1f + i * 0.2f), BackPoint());
            IslandAudio.Play("loot", null, 0.6f);
            _chopping = null;
            _chops = 0;
            _highlight?.Clear();
            PropCleared?.Invoke(p);
        }

        // ---- the player's tool ------------------------------------------------------------------------

        private GameObject? _playerTool;
        private string? _playerToolId;
        private float _playerToolUntil;

        /// <summary>Put this pack tool in the farmer's hand for the job (null: empty hands); it goes when the work stops.</summary>
        private void PlayerTool(string? id)
        {
            _playerToolUntil = Time.time + 1.4f;
            if (id == _playerToolId && _playerTool != null) return;
            if (_playerTool != null) Destroy(_playerTool);
            _playerTool = null;
            _playerToolId = id;
            if (id == null || _player == null) return;
            var prefab = ItemCatalog.Load()?.Tool(id);
            if (prefab != null) _playerTool = HandTool.Hold(_player.gameObject, prefab);
            HandTool.Reach(_player.gameObject, null, _playerTool, id);
        }

        private void PlayerToolTick()
        {
            if (_playerTool == null) return;
            if (Time.time > _playerToolUntil || _player == null || _player.Riding)
            {
                Destroy(_playerTool);
                _playerTool = null;
                _playerToolId = null;
                if (_player != null) HandTool.Reach(_player.gameObject, null, null, "");
            }
        }

        // ---- sound --------------------------------------------------------------------------------

        /// <summary>Footsteps while walking, quicker ones running, hooves on the horse; the sea always, softly.</summary>
        private void Footsteps()
        {
            if (_seaSound == null) { _seaSound = IslandAudio.Loop("sea_loop", false); if (_seaSound != null) _seaSound.Target = 0.22f; }
            if (_player == null) return;
            _walkSound ??= IslandAudio.Loop("walk_loop", false);
            _runSound ??= IslandAudio.Loop("run_loop", false);
            _gallopSound ??= IslandAudio.Loop("gallop_loop", false);
            var at = _player.transform.position;
            var v = new Vector3(at.x - _lastPlayerAt.x, 0, at.z - _lastPlayerAt.z).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            _lastPlayerAt = at;
            _playerSpeed = Mathf.Lerp(_playerSpeed, v, 1f - Mathf.Exp(-10f * Time.deltaTime));
            var riding = _player.Riding;
            var moving = _playerSpeed > 0.4f && _playerSpeed < 40f;   // not a teleport
            if (_walkSound != null) _walkSound.Target = !riding && moving && _playerSpeed < 6f ? 0.55f : 0f;
            if (_runSound != null) _runSound.Target = !riding && moving && _playerSpeed >= 6f ? 0.6f : 0f;
            if (_gallopSound != null)
            {
                _gallopSound.Target = riding && moving ? 0.75f : 0f;
                _gallopSound.Pitch = Mathf.Clamp(_playerSpeed / 10f, 0.8f, 1.25f);
            }
        }

        // ---- for the island's people (IslandLife) ------------------------------------------------

        public PlacedBuilding? Building(int uid) => _sites.TryGetValue(uid, out var s) && s.Building != null ? s.Building : null;
        public IEnumerable<int> Buildings => _sites.Keys;

        /// <summary>Where to stand to hand goods to a building: its pad, or the middle of its front side.</summary>
        public Vector3 DoorOf(int uid)
        {
            if (!_sites.TryGetValue(uid, out var site)) return Vector3.zero;
            if (site.Pad != null) return site.Pad.transform.position;
            var b = site.Building;
            var size = Size(b);
            var front = b.transform.rotation * Vector3.back;
            var reach = Mathf.Abs(front.x) * size.x * 0.5f + Mathf.Abs(front.z) * size.y * 0.5f + 0.9f;
            var at = Centre(b) + front * reach;
            at.y = GroundHeight.At(at.x, at.z, at.y);
            return at;
        }

        public Vector3 CentreOf(PlacedBuilding b) => Centre(b);
        public Vector2 SizeOf(PlacedBuilding b) => Size(b);

        /// <summary>A good flying between two points (workers loading and unloading use it too).</summary>
        public void FlyGood(string item, Vector3 from, Vector3 to) => Fly(item, from, to);

        public void Say(string text, float seconds = 1.8f) => Toast(text, seconds);

        public static Color GoodColour(string item) => ColourOf(item);

        private static Color ColourOf(string item) => item switch
        {
            "wheat" => new Color(0.95f, 0.8f, 0.35f),
            "maize" => new Color(1f, 0.85f, 0.15f),
            "pumpkin" => new Color(0.95f, 0.55f, 0.15f),
            "tomato" => new Color(0.9f, 0.2f, 0.15f),
            "cabbage" => new Color(0.55f, 0.75f, 0.4f),
            "beans" => new Color(0.4f, 0.7f, 0.3f),
            "sunflower" => new Color(1f, 0.75f, 0.1f),
            "egg" => new Color(0.97f, 0.94f, 0.85f),
            "milk" => new Color(0.9f, 0.95f, 1f),
            "meat" => new Color(0.75f, 0.3f, 0.3f),
            "wood" => new Color(0.55f, 0.38f, 0.22f),
            "flour" => new Color(0.98f, 0.97f, 0.92f),
            "croissant" => new Color(0.82f, 0.55f, 0.25f),
            "yogurt" => new Color(0.95f, 0.95f, 0.9f),
            "oil" => new Color(0.95f, 0.85f, 0.2f),
            "juice" => new Color(0.95f, 0.35f, 0.25f),
            _ => new Color(0.8f, 0.8f, 0.8f),
        };

        private Vector3 BackPoint() => _backpack != null ? _backpack.position + Vector3.up * 0.3f : (_player != null ? _player.transform.position + Vector3.up * 1.5f : Vector3.zero);

        /// <summary>A unit of a good flying in an arc between two points (what you gather jumps onto your back, what you hand over jumps into the building).</summary>
        private void Fly(string item, Vector3 from, Vector3 to)
        {
            var go = MakeGood(item, Farmverse.Presentation.World.ItemCatalog.Load());
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<Flight>().Go(from, to, 0.35f);
        }

        // ---- placing pads -------------------------------------------------------------------------

        private void PlacePad(Site site)
        {
            var b = site.Building;
            var centre = Centre(b);
            var size = Size(b);
            // the door side faces the camera when a building isn't turned (its local back, world −z)
            var front = b.transform.rotation * Vector3.back;
            var reach = Mathf.Abs(front.x) * size.x * 0.5f + Mathf.Abs(front.z) * size.y * 0.5f + 1.5f;
            var at = centre + front * reach;
            at.y = GroundHeight.At(at.x, at.z, at.y) + 0.04f;
            site.Pad!.transform.position = at;
        }

        private Vector3 Centre(PlacedBuilding b)
        {
            var size = Size(b);
            var corner = _grid != null ? _grid.Data.CellCorner(b.cells.x, b.cells.y) : b.transform.position;
            var c = corner + new Vector3(size.x * 0.5f, 0f, size.y * 0.5f);
            c.y = GroundHeight.At(c.x, c.z, b.transform.position.y);
            return c;
        }

        private Vector2 Size(PlacedBuilding b)
        {
            var cell = _grid != null ? _grid.Data.cellSize : 2f;
            return new Vector2(b.cells.width * cell, b.cells.height * cell);
        }

        private bool Near(PlacedBuilding b, Vector3 at, float reach)
        {
            var c = Centre(b);
            var half = Size(b) * 0.5f + new Vector2(reach, reach);
            return Mathf.Abs(at.x - c.x) <= half.x && Mathf.Abs(at.z - c.z) <= half.y;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private CharacterMotion? Motion() => _player != null && _player.TryGetComponent<CharacterMotion>(out var m) ? m : null;

        private static float PlayerHeight(PlayerAvatar p) =>
            p.TryGetComponent<CharacterController>(out var cc) ? cc.height * p.transform.lossyScale.y : 1.7f;

        private readonly List<(double at, string text)> _log = new List<(double, string)>();

        /// <summary>Recent happenings, newest first (the island's diary).</summary>
        public IReadOnlyList<(double at, string text)> Log => _log;

        private readonly Dictionary<string, float> _said = new Dictionary<string, float>();
        private const float RepeatAfter = 45f;

        private void Toast(string text, float seconds = 1.8f)
        {
            // everything said is also written down (not the same line twice in a row)
            if (_log.Count == 0 || _log[0].text != text)
            {
                _log.Insert(0, (Clock, text));
                if (_log.Count > 60) _log.RemoveAt(_log.Count - 1);
            }
            if (_toast == text && Time.unscaledTime < _toastUntil) return;
            // the same warning again (a full farmhouse while you stand at the door) waits a good while before it's said again
            if (_said.TryGetValue(text, out var last) && Time.unscaledTime - last < RepeatAfter) return;
            _said[text] = Time.unscaledTime;
            // an important message isn't pushed off the screen at once by small talk
            if (Time.unscaledTime < _toastUntil - 2f && seconds <= 1.8f) return;
            _toast = text;
            _toastUntil = Time.unscaledTime + seconds;
        }

        // ---- save ---------------------------------------------------------------------------------

        public void Save()
        {
            if (_grid == null || !_begun) return;
            PlayerPrefs.SetString(saveKey, JsonUtility.ToJson(Economy.ToSave()));
            PlayerPrefs.Save();
        }

        private EconomySave? ReadSave()
        {
            if (!PlayerPrefs.HasKey(saveKey)) return null;
            try { return JsonUtility.FromJson<EconomySave>(PlayerPrefs.GetString(saveKey)); }
            catch { return null; }
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            Save();
            // cloud save: the island as it is now goes up when the game is put away
            if (_grid != null && _begun) IslandShadow.Offer(CurrentState());
        }

        private void OnApplicationQuit() => Save();

        // ---- screen -------------------------------------------------------------------------------



        private static string Pretty(string item) => item.Replace('_', ' ');

        private static string MissingText(Construction site) => string.Join(", ", site.Missing.Select(m => $"{m.Value} {Pretty(m.Key)}"));

        private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static string Name(string buildingId) => buildingId switch
        {
            "farmhouse" => "Farmhouse",
            "army_house" => "Army house",
            "workers_house" => "Workers' house",
            "builders_hut" => "Builders' hut",
            "market_stall" => "Market",
            _ => Cap(buildingId.Replace('_', ' ')),
        };

        private static string PeopleWord(DepartmentKind kind) => kind switch
        {
            DepartmentKind.ArmyHouse => "warriors",
            DepartmentKind.WorkersHouse => "farm workers",
            DepartmentKind.BuildersHut => "builders",
            _ => "people",
        };
    }

    /// <summary>The glowing ring at a department's door: a soft disc, a rim, and an arc showing progress to the next level.</summary>
    public sealed class PadRing : MonoBehaviour
    {
        private const int Segments = 48;
        private Mesh? _arc;
        private MeshRenderer? _disc;
        private MeshRenderer? _rim;
        private MeshRenderer? _fill;
        private Color _colour;
        private float _shownFill = -1f;

        public void Build(Material material, Color colour)
        {
            _colour = colour;
            _disc = Part("Disc", Disc(1.45f), material);
            _rim = Part("Rim", Ring(1.25f, 1.55f, 0f, 1f), material);
            _arc = new Mesh { name = "pad_arc" };
            _fill = Part("Fill", _arc, material);
            _rim.transform.localPosition = Vector3.up * 0.006f;
            _fill.transform.localPosition = Vector3.up * 0.012f;
        }

        public void Show(float fill, bool active, bool starving)
        {
            if (_disc == null || _rim == null || _fill == null || _arc == null) return;
            var pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (active ? 9f : 2.4f));
            var c = starving ? Color.Lerp(_colour, Color.red, 0.6f + 0.4f * pulse) : _colour;
            _disc.material.color = new Color(c.r, c.g, c.b, active ? 0.75f + 0.25f * pulse : 0.45f + 0.2f * pulse);
            _rim.material.color = new Color(c.r, c.g, c.b, active ? 1f : 0.75f);
            _fill.material.color = new Color(1f, 1f, 0.85f, 0.95f);
            if (Mathf.Abs(fill - _shownFill) < 0.004f) return;
            _shownFill = fill;
            var ring = Ring(0.95f, 1.2f, 0f, Mathf.Max(0.001f, fill));
            _arc.Clear();
            _arc.vertices = ring.vertices;
            _arc.triangles = ring.triangles;
            _arc.colors = ring.colors;
            _arc.RecalculateBounds();
        }

        private MeshRenderer Part(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.material = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private static Mesh Disc(float radius)
        {
            var v = new List<Vector3> { Vector3.zero };
            var c = new List<Color> { Color.white };
            var t = new List<int>();
            for (var i = 0; i <= Segments; i++)
            {
                var a = i / (float)Segments * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius);
                c.Add(new Color(1, 1, 1, 0.35f));   // fades toward the rim
                if (i > 0) t.AddRange(new[] { 0, i + 1, i });
            }
            var m = new Mesh { name = "pad_disc" };
            m.SetVertices(v);
            m.SetColors(c);
            m.SetTriangles(t, 0);
            return m;
        }

        private static Mesh Ring(float inner, float outer, float from, float to)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            var c = new List<Color>();
            var steps = Mathf.Max(2, Mathf.CeilToInt(Segments * (to - from)));
            for (var i = 0; i <= steps; i++)
            {
                // clockwise from the top (as seen from above, facing north)
                var a = Mathf.PI / 2f - (from + (to - from) * i / steps) * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * inner);
                v.Add(d * outer);
                c.Add(Color.white);
                c.Add(Color.white);
                if (i > 0)
                {
                    var b = (i - 1) * 2;
                    t.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
                }
            }
            var m = new Mesh { name = "pad_ring" };
            m.SetVertices(v);
            m.SetColors(c);
            m.SetTriangles(t, 0);
            return m;
        }
    }

    /// <summary>Moves a little crate in an arc and removes it on arrival.</summary>
    public sealed class Flight : MonoBehaviour
    {
        private Vector3 _from, _to, _size = Vector3.one;
        private float _t, _duration = 0.35f;

        public void Go(Vector3 from, Vector3 to, float seconds)
        {
            _from = from;
            _to = to;
            _duration = Mathf.Max(0.05f, seconds);
            transform.position = from;
            _size = transform.localScale;
        }

        private void Update()
        {
            _t += Time.deltaTime / _duration;
            var k = Mathf.Clamp01(_t);
            var p = Vector3.Lerp(_from, _to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.2f;
            transform.position = p;
            transform.localScale = _size * Mathf.Lerp(1f, 0.6f, k);
            transform.Rotate(0f, 540f * Time.deltaTime, 0f, Space.World);
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
