#nullable enable
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// The people of the island (gameplay design §5–§7), on top of <see cref="IslandEconomy"/>:
    /// <list type="bullet">
    /// <item>Farm workers from workers' houses harvest ripe fields, collect from coops and sheds, sow
    /// fields again, and carry it all to the farmhouse; they also run supply orders from the farmhouse
    /// to hungry departments. They only take work within reach of their house, so where you build a
    /// workers' house matters. A hungry house's workers stop and sit at home.</item>
    /// <item>Builders (one with the farmhouse, more from builders' huts) go to every new building and
    /// work at it while it rises out of the ground inside a ring of light, then it springs up.</item>
    /// <item>Warriors from army houses stand guard near their house and run at raiders that land within
    /// their reach — spread army houses round the island to cover every shore. Fallen warriors are lost
    /// (the house takes new ones once it's fed).</item>
    /// <item>Raiders land on a shore every few minutes, walk to the richest building near them and steal
    /// (stored goods, eggs, crops, energy), then run back to the sea. Beaten raiders drop what they took.</item>
    /// </list>
    /// A raid raises an alert with a way to look at it or ride there.
    /// </summary>
    public sealed partial class IslandLife : MonoBehaviour
    {
        [SerializeField] private GameObject? workerPrefab;
        [SerializeField] private GameObject? warriorPrefab;
        [SerializeField] private GameObject[] builderPrefabs = new GameObject[0];
        [SerializeField] private GameObject[] raiderPrefabs = new GameObject[0];
        [SerializeField, Tooltip("The named zombies (prefab names carry the boss id: footballer, builder, clerk, policeman, mime, tourist).")] private GameObject[] bossPrefabs = new GameObject[0];
        [SerializeField, Tooltip("Weapons warriors hold, by name (Bat, Crowbar, Pipe, PipeWrench, Baton, RoadSign, Weapon_005).")] private GameObject[] weaponPrefabs = new GameObject[0];
        [SerializeField, Tooltip("Cured bosses: the boss's clothes with human skin (char_cured_<boss>).")] private GameObject[] curedPrefabs = new GameObject[0];
        [SerializeField, Tooltip("Townsfolk who move in as the town grows (grandma, the kid, the baker, villagers).")] private GameObject[] villagerPrefabs = new GameObject[0];
        [SerializeField, Tooltip("Seconds into a session before the day's raid (if it hasn't happened today).")] private float firstRaidAfter = 150f;
        [SerializeField, Tooltip("Up to this many more seconds, at random.")] private float raidAfterSpread = 150f;
        [SerializeField, Tooltip("How far from their house (m) farm workers will go for work.")] private float workerReach = 45f;
        [SerializeField, Tooltip("How far from their house (m) warriors will run to meet raiders.")] private float guardReach = 55f;

        private enum Role { Worker, Builder, Warrior, Raider, Villager }
        private enum Job { None, Wander, Harvest, Collect, Plant, Deliver, Fetch, Supply, Build, Rest, Fight, Raid, Flee, Leave, Down, Hide, Tired, Feed, Chop }

        private sealed class Agent
        {
            public Role Role;
            public int Home;
            /// <summary>A Farmhand's token (docs 15 §7): they work like the house's people, dressed in their own outfit.</summary>
            public string? Crew;
            public string Kind = "";
            public Transform Body = null!;
            public CharacterMotion? Motion;
            public readonly List<Vector3> Route = new List<Vector3>();
            public int Next;
            public float Speed;
            public Job Job;
            public int Target = -1;      // building uid
            public int Target2 = -1;     // second leg (a supply order's department)
            public float Timer;          // work left at the spot, or time to the next think
            public float Repath;
            public Vector3 Spot;
            public readonly List<string> Load = new List<string>();
            public Transform? Crates;
            public string CratesShown = "";
            public float Hp, MaxHp, Damage, Swing;
            public float Foot;           // the model's lowest point relative to its origin (stood on the ground)
            public Agent? Foe;
            public int Thefts;
            public int Kills;
            public Vector3 Shore;
            public bool Gone;
            public bool Started;
            public float LeaveFarmerUntil;   // a raider who just knocked the farmer down goes about its business for a while
            public int Loads;            // loads carried since the last rest
            public string Boss = "";     // a named zombie (GameplayRules.Bosses id), else empty
            // what they hold or push (IslandLife.Gear)
            public GameObject? Tool;
            public string ToolShown = "";
            public GameObject? Pushing;
            public string PushShown = "";
            public float ToolClock;      // builders change tools now and then
            public bool Mowing;          // a townsperson out with a lawn mower
        }

        private sealed class Loot
        {
            public GameObject Pile = null!;
            public readonly List<string> Items = new List<string>();
        }

        private IslandEconomyHost? _host;
        private readonly List<Agent> _agents = new List<Agent>();
        private readonly Dictionary<int, Agent> _claims = new Dictionary<int, Agent>();
        private readonly Dictionary<int, Vector3> _siteScale = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, PadRing> _siteRings = new Dictionary<int, PadRing>();
        private readonly Dictionary<int, IslandAudio.LoopSound> _siteSounds = new Dictionary<int, IslandAudio.LoopSound>();
        private readonly List<Loot> _loot = new List<Loot>();
        private readonly HashSet<int> _repairs = new HashSet<int>();
        private readonly List<Vector3> _path = new List<Vector3>();
        private float _nextSync;
        private float _playTime;
        private float _nextRaid;
        private Vector3 _raidAt;
        private string _raidFrom = "";
        private int _raidStolen;
        private bool _raidOn;
        private readonly List<string> _bossesTonight = new List<string>();
        private readonly List<string> _bossesCame = new List<string>();
        private int _cured;
        private Material? _ringMaterial;

        /// <summary>0 by day, 1 deep in a zombie night (fog and darkness follow it; the weather reads it).</summary>
        public static float Night { get; private set; }

        /// <summary>The bosses that came in the last raid (names), and how many zombies were cured in it.</summary>
        public string LastRaidBosses { get; private set; } = "";
        public int LastRaidCured { get; private set; }
        private bool _restWarned;

        private IslandEconomy Eco => _host!.Economy;

        /// <summary>Raised when the player asks to ride to the raid (the sandbox owns the horse).</summary>
        public System.Action<Vector3>? RideRequested { get; set; }
        /// <summary>Raised when the player asks to look at the raid.</summary>
        public System.Action<Vector3>? LookRequested { get; set; }

        public bool RaidUnderway => _raidOn;

        /// <summary>How many raids have ended this session (the game's UI shows the result when it goes up).</summary>
        public int RaidsEnded { get; private set; }

        /// <summary>The last raid: how many came, goods stolen, people killed, buildings ruined, coins taken; won = nothing lost.</summary>
        public (int size, int stolen, int killed, int ruined, int coins, bool won) LastRaidOutcome { get; private set; }
        /// <summary>Where the raiders on the island are (animals run from them).</summary>
        public IEnumerable<Vector3> RaiderPositions => _agents.Where(a => a.Role == Role.Raider && a.Job != Job.Down && !a.Gone && a.Body != null).Select(a => a.Body.position);
        /// <summary>Seconds until the next raid is sighted (0 while one is on).</summary>
        /// <summary>The people on the island, shown right now (hidden while visiting another island).</summary>
        public IEnumerable<GameObject> ShownBodies => _agents.Where(a => a.Body != null && a.Body.gameObject.activeSelf).Select(a => a.Body.gameObject);

        public float NextRaidIn => _raidOn || RaidSchedule.DoneToday ? 0f : Mathf.Max(0f, _nextRaid - _playTime);

        /// <summary>The last raid shown happened while the player was away (the server ran it).</summary>
        public bool LastRaidAway { get; private set; }

        /// <summary>A raid asked for by hand (testing): doesn't count as the day's raid.</summary>
        private bool _forcedRaid;

        public void Begin(IslandEconomyHost host)
        {
            _host = host;
            // the day's raid comes a few minutes into the session (a fresh island keeps its longer grace period)
            _nextRaid = Mathf.Max(firstRaidAfter + Random.Range(0f, raidAfterSpread), _nextRaid - _playTime);
            _playTime = 0f;
            _raidOn = false;
            _raidLanded = false;
        }

        /// <summary>Everyone leaves (a fresh island).</summary>
        public void Clear()
        {
            foreach (var a in _agents) if (a.Body != null) Destroy(a.Body.gameObject);
            _agents.Clear();
            _claims.Clear();
            foreach (var r in _siteRings.Values) if (r != null) Destroy(r.gameObject);
            _siteRings.Clear();
            _siteScale.Clear();
            foreach (var l in _siteSounds.Values) IslandAudio.Release(l);
            _siteSounds.Clear();
            foreach (var l in _loot) if (l.Pile != null) Destroy(l.Pile);
            _loot.Clear();
            _raidOn = false;
            _raidLanded = false;
            _nextRaid = _playTime + (float)GameplayRules.GraceSeconds;
        }

        /// <summary>A building was just bought: it starts low in the ground inside a ring of light.</summary>
        public void OnSiteStarted(PlacedBuilding b)
        {
            Magic.Burst(_host!.CentreOf(b) + Vector3.up * 0.3f, new Color(0.75f, 0.55f, 1f), 40, Mathf.Max(_host.SizeOf(b).x, _host.SizeOf(b).y) * 0.5f);
        }

        /// <summary>Start a raid now (for testing from the sandbox panel).</summary>
        public void RaidNow()
        {
            _nextRaid = _playTime;
            _forcedRaid = true;
            Debug.Log($"[Raid] asked for a raid now (raiders known: {raiderPrefabs.Length}, underway: {_raidOn})");
        }

        // ---- every frame --------------------------------------------------------------------------

        private void Update()
        {
            if (_host == null || _host.Grid == null) return;
            var dt = Time.deltaTime;
            _playTime += dt;
            var now = IslandEconomyHost.Clock;
            if (Time.time >= _nextSync)
            {
                _nextSync = Time.time + 1f;
                SyncPeople();
            }
            ShowSites();
            ApplyAwayRaid(now);
            RaidClock(now);
            IslandAudio.MusicLevel = _raidOn ? 0.35f : 1f;
            Night = Mathf.MoveTowards(Night, _raidOn ? 1f : 0f, Time.deltaTime / (_raidOn ? 6f : 10f));
            for (var i = 0; i < _agents.Count; i++)
            {
                var a = _agents[i];
                if (a.Gone || a.Body == null) continue;
                if (a == _talking) { TalkStep(a); ShowLoad(a); Gear(a, dt); continue; }   // stopped for a chat with the farmer
                // Worker Rush: the island's workers and builders get through their jobs twice as fast (they walk 2× in Walk)
                var work = dt * (IslandBoosts.Running("rush") ? 2f : 1f);
                switch (a.Role)
                {
                    case Role.Worker: Worker(a, now, work); break;
                    case Role.Builder: Builder(a, work); break;
                    case Role.Warrior: Warrior(a, dt); break;
                    case Role.Raider: Raider(a, now, dt); break;
                    case Role.Villager: Villager(a, dt); break;
                }
                Walk(a, dt);
                ShowLoad(a);
                Gear(a, dt);
                if (a.Role != Role.Raider && a.Role != Role.Warrior) HandTool.Reach(a.Body.gameObject, a.Pushing, a.Tool, a.ToolShown);
                KeepApart(a, dt);
            }
            _agents.RemoveAll(a => a.Gone || a.Body == null);
            PickUpLoot();
            TalkCheck();
            FarmerFights(dt);
            if (_raidOn && _raidLanded && !_agents.Any(a => a.Role == Role.Raider && a.Job != Job.Down && !a.Gone)) EndRaid();
        }

        // ---- who should be out and about ----------------------------------------------------------

        private void SyncPeople()
        {
            var eco = Eco;
            // farm workers and warriors: one per person present in their house
            Sync(Role.Worker, eco.Workforce(DepartmentKind.WorkersHouse).ToList(), workerPrefab != null ? new[] { workerPrefab } : new GameObject[0]);
            SyncCrew(eco);
            Sync(Role.Warrior, eco.Workforce(DepartmentKind.ArmyHouse).Concat(eco.Workforce(DepartmentKind.Watch)).ToList(), warriorPrefab != null ? new[] { warriorPrefab } : new GameObject[0]);
            // builders: one from the farmhouse, the rest from fed builders' huts
            var homes = new List<(int, int)>();
            var farmhouse = eco.Departments.Values.FirstOrDefault(d => d.Levels.Kind == DepartmentKind.Farmhouse && eco.IsBuilt(d.Uid));
            // the farmhouse's own builder; if raiders killed them, a new one comes after a while
            if (farmhouse != null && _playTime >= _farmhouseBuilderBackAt) homes.Add((farmhouse.Uid, GameplayRules.FarmhouseBuilders));
            foreach (var d in eco.Departments.Values)
                if (d.Levels.Kind == DepartmentKind.BuildersHut && eco.IsBuilt(d.Uid) && !d.Starving && d.Present > 0) homes.Add((d.Uid, d.Present));
            Sync(Role.Builder, homes, builderPrefabs);
            // townsfolk: one moves in with each working town building or shop (up to eight)
            var town = _host!.Buildings.Where(u => _host.Building(u) is { } b && TownIds.Contains(b.id) && eco.Working(u)).Take(8).Select(u => (u, 1)).ToList();
            Sync(Role.Villager, town, villagerPrefabs);
        }

        private static readonly HashSet<string> TownIds = new HashSet<string>
        {
            "market_stall", "grocer_shop", "bakery_shop", "fish_market", "school", "clinic", "library", "chapel",
            "community_hall", "post_office", "bank", "town_hall", "fire_station", "police_station",
        };

        /// <summary>Townsfolk stroll between the island's buildings, stop to look about, and duck indoors when raiders come.</summary>
        private void Villager(Agent a, float dt)
        {
            if (a.Job == Job.Leave) { if (Arrived(a)) Vanish(a); return; }
            if (a.Job == Job.Down) { DownTimer(a, dt); return; }
            if (Hide(a, dt)) return;
            if (!Arrived(a)) return;
            a.Timer -= dt;
            if (a.Timer > 0) return;
            var places = _host!.Buildings.ToList();
            if (places.Count == 0) return;
            var to = places[Random.Range(0, places.Count)];
            a.Job = Job.Wander;
            a.Timer = Random.Range(4f, 12f);
            // some go out to mow the lawns; the rest stroll between buildings, looking about or waving
            a.Mowing = !a.Kind.Contains("baker") && Random.value < 0.3f;
            if (!a.Mowing && Random.value < 0.35f) a.Motion?.Play(Random.value < 0.4f ? "LookAround2" : Random.value < 0.5f ? "Wave" : "Talk", 2.5f);
            GoTo(a, _host.DoorOf(to) + Jitter(a.Mowing ? 7f : 2.5f));
        }

        private void Sync(Role role, List<(int house, int people)> homes, GameObject[] prefabs)
        {
            if (prefabs.Length == 0) return;
            foreach (var (house, people) in homes)
            {
                var have = _agents.Count(a => a.Role == role && a.Crew == null && a.Home == house && a.Job != Job.Leave && a.Job != Job.Down);
                for (var i = have; i < people; i++) Spawn(role, house, prefabs[(house + i) % prefabs.Length]);
                if (have > people)
                    foreach (var a in _agents.Where(a => a.Role == role && a.Crew == null && a.Home == house && a.Job != Job.Leave && a.Job != Job.Down).Take(have - people).ToList())
                        Leave(a);
            }
            // people whose house is gone (sold) leave too
            foreach (var a in _agents.Where(a => a.Role == role && a.Crew == null && a.Job != Job.Leave && a.Job != Job.Down && !homes.Any(h => h.house == a.Home)).ToList())
                Leave(a);
        }

        /// <summary>
        /// Farmhands with a bed walk the island as workers in their own outfits (docs 15 §7.3); one sold or with no
        /// bed walks off. They live in the first workers' house.
        /// </summary>
        private void SyncCrew(IslandEconomy eco)
        {
            if (workerPrefab == null) return;
            var house = eco.Departments.Values.Where(d => d.Levels.Kind == DepartmentKind.WorkersHouse && eco.Working(d.Uid)).OrderBy(d => d.Uid).FirstOrDefault();
            var working = house == null ? new List<Farmhand>() : eco.WorkingCrew.ToList();
            foreach (var f in working)
            {
                if (_agents.Any(a => a.Crew == f.token && a.Job != Job.Leave && a.Job != Job.Down)) continue;
                var a = Spawn(Role.Worker, house!.Uid, workerPrefab);
                a.Crew = f.token;
                a.Speed *= 1f + 0.06f * (f.speed - 1) + 0.03f * (f.level - 1);
                if (!string.IsNullOrEmpty(f.look) && a.Body.GetComponentInChildren<CharacterLook>(true) is { } dress) dress.ApplyOwn(f.look);
            }
            foreach (var a in _agents.Where(a => a.Crew != null && a.Job != Job.Leave && a.Job != Job.Down && working.All(f => f.token != a.Crew)).ToList())
                Leave(a);
        }

        private Agent Spawn(Role role, int home, GameObject prefab, Vector3? where = null)
        {
            var at = (where ?? _host!.DoorOf(home)) + new Vector3(Random.Range(-1.2f, 1.2f), 0, Random.Range(-1.2f, 1.2f));
            at.y = GroundHeight.At(at.x, at.z, at.y);
            var go = Instantiate(prefab, at, Quaternion.Euler(0, Random.Range(0f, 360f), 0), transform);
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            var a = new Agent
            {
                Role = role, Home = home, Body = go.transform, Motion = go.GetComponent<CharacterMotion>(), Kind = prefab.name,
                Speed = role switch { Role.Warrior => 4.6f, Role.Raider => 3.2f, Role.Builder => 3.4f, Role.Villager => 2.1f, _ => 3.2f } * Random.Range(0.92f, 1.08f),
                MaxHp = role == Role.Warrior ? 100f : 60f, Damage = role == Role.Warrior ? 24f : 6f,
            };
            a.Hp = a.MaxHp;
            a.Foot = GroundHeight.FootOffset(go);
            go.transform.position = new Vector3(at.x, at.y - a.Foot, at.z);
            if (role == Role.Builder && !prefab.name.Contains("builder")) HardHat(go);
            if (role == Role.Warrior) Arm(go);
            _agents.Add(a);
            Magic.Appear(go, 0.4f, role == Role.Raider ? new Color(1f, 0.4f, 0.4f) : (Color?)null);
            return a;
        }

        /// <summary>
        /// Nobody stands inside the farmer: anyone closer than a step is eased back out (sideways if they're walking
        /// past, straight back when they've stopped to talk), so two people never share one spot.
        /// </summary>
        private void KeepApart(Agent a, float dt)
        {
            var player = _host != null ? _host.Player : null;
            if (player == null || player.Riding || a.Job == Job.Down) return;
            var away = Flat(a.Body.position - player.transform.position);
            var d = away.magnitude;
            const float room = 1.35f;
            if (d >= room) return;
            if (d < 0.05f) away = player.transform.right; else away /= d;
            var push = Mathf.Min(room - d, 4f * dt);
            var p = a.Body.position + away * push;
            p.y = GroundHeight.At(p.x, p.z, p.y);
            a.Body.position = p;
        }

        /// <summary>A zombie's weapon in its right hand (the pack tool, gripped like the workers' tools).</summary>
        private static void ZombieWeapon(Agent a, string id)
        {
            var prefab = ItemCatalog.Load()?.Tool(id);
            if (prefab == null) return;
            a.Tool = HandTool.Hold(a.Body.gameObject, prefab);
            a.ToolShown = id;
        }

        /// <summary>A weapon in a warrior's right hand, by island level (the army's weapon upgrades).</summary>
        private void Arm(GameObject who)
        {
            if (weaponPrefabs.Length == 0 || _host == null) return;
            var names = GameplayRules.ArmyWeapons(Eco.BestLevel);
            if (names.Count == 0) return;
            var prefab = weaponPrefabs.FirstOrDefault(w => w != null && w.name == names[Random.Range(0, names.Count)]);
            if (prefab == null) return;
            var anim = who.GetComponentInChildren<Animator>();
            var hand = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (hand == null) return;
            var w = Instantiate(prefab, hand);
            w.name = "Weapon";
            var (pos, rot) = GameplayRules.WeaponGrip(prefab.name);
            w.transform.localPosition = new Vector3(pos.x, pos.y, pos.z);
            w.transform.localRotation = Quaternion.Euler(rot.x, rot.y, rot.z);
            foreach (var c in w.GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        private static Material? _hatMaterial;

        /// <summary>A yellow hard hat on the head, so a builder reads as a builder whatever model wears it.</summary>
        private static void HardHat(GameObject who)
        {
            var anim = who.GetComponentInChildren<Animator>();
            var head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            // sit it on the crown: the top of the model (chibi heads are big), following the head bone
            var top = who.transform.position.y + 1.8f;
            var found = false;
            foreach (var r in who.GetComponentsInChildren<Renderer>()) { top = found ? Mathf.Max(top, r.bounds.max.y) : r.bounds.max.y; found = true; }
            var height = Mathf.Max(0.5f, top - who.transform.position.y);
            var hat = new GameObject("Hard hat").transform;
            hat.SetParent(head != null ? head : who.transform, false);
            var centre = head != null ? head.position : who.transform.position;
            hat.position = new Vector3(centre.x, top - height * 0.04f, centre.z);
            hat.rotation = who.transform.rotation;
            // world-size parts whatever the bone's scale
            var s = hat.parent.lossyScale.y > 1e-4f ? 1f / hat.parent.lossyScale.y : 1f;
            var k = height;   // chibi proportions: the head is about a third of the height wide
            if (_hatMaterial == null)
            {
                _hatMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                var yellow = new Color(1f, 0.78f, 0.1f);
                if (_hatMaterial.HasProperty("_BaseColor")) _hatMaterial.SetColor("_BaseColor", yellow);
                _hatMaterial.color = yellow;
                if (_hatMaterial.HasProperty("_Smoothness")) _hatMaterial.SetFloat("_Smoothness", 0.55f);
            }
            void Part(PrimitiveType shape, Vector3 at, Vector3 size)
            {
                var p = GameObject.CreatePrimitive(shape);
                Destroy(p.GetComponent<Collider>());
                p.transform.SetParent(hat, false);
                p.transform.localPosition = at * s * k;
                p.transform.localScale = size * s * k;
                p.GetComponent<Renderer>().sharedMaterial = _hatMaterial;
            }
            Part(PrimitiveType.Sphere, new Vector3(0, -0.01f, 0), new Vector3(0.3f, 0.17f, 0.32f));      // the dome
            Part(PrimitiveType.Cylinder, new Vector3(0, -0.045f, 0.015f), new Vector3(0.36f, 0.006f, 0.4f)); // the brim
            Part(PrimitiveType.Cube, new Vector3(0, 0.07f, 0), new Vector3(0.03f, 0.03f, 0.27f));        // the ridge
        }

        private void Leave(Agent a)
        {
            Unclaim(a);
            a.Job = Job.Leave;
            GoTo(a, _host!.DoorOf(a.Home));
        }

        // ---- farm workers -------------------------------------------------------------------------

        private void Worker(Agent a, double now, float dt)
        {
            var eco = Eco;
            if (a.Job == Job.Leave) { if (Arrived(a)) Vanish(a); return; }
            var home = eco.Departments.TryGetValue(a.Home, out var h) ? h : null;
            if (home == null) { Leave(a); return; }
            if (a.Job == Job.Down) { DownTimer(a, dt); return; }
            if (Hide(a, dt)) return;

            // a hungry house's workers down tools and sit at home
            if (home.Starving && a.Job != Job.Rest && a.Load.Count == 0)
            {
                Unclaim(a);
                a.Job = Job.Rest;
                GoTo(a, _host!.DoorOf(a.Home) + Jitter(1.5f));
                if (!_restWarned) { _restWarned = true; _host.Say("Farm workers are hungry — feed the workers' house"); }
            }
            switch (a.Job)
            {
                case Job.Rest:
                    if (Arrived(a)) a.Motion?.Hold("Relax");
                    if (!home.Starving) { a.Motion?.Release(); a.Job = Job.None; _restWarned = false; }
                    return;
                case Job.Tired:
                    // worn out after a long run of loads: a bite to eat at home (or a few sit-ups) before going on
                    if (!Arrived(a)) return;
                    a.Motion?.Hold(a.Home % 3 == 0 ? "Situps" : "Eat");
                    a.Timer -= dt;
                    if (a.Timer > 0) return;
                    a.Motion?.Release();
                    a.Loads = 0;
                    a.Job = Job.None;
                    return;
                case Job.Chop:
                {
                    // felling a tree or breaking a rock for the farmhouse's wood and stone
                    var prop = _host!.Nature?.Find(a.Target2);
                    if (prop == null) { Unclaim(a); a.Motion?.Release(); a.Job = Job.None; break; }
                    if (!Arrived(a)) break;
                    Face(a, prop.transform.position);
                    if (!a.Started) { a.Started = true; a.Motion?.Hold(WorkAnim(WantTool(a))); }
                    a.Swing -= dt;
                    if (a.Swing <= 0f)
                    {
                        a.Swing = 0.9f;
                        Magic.Shake(prop.gameObject, prop.transform.position - a.Body.position, prop.kind == "rock" ? 0f : prop.kind == "bush" ? 7f : 4f);
                        IslandAudio.Play(prop.kind == "rock" ? "hit" : "chop", prop.transform.position, 0.45f, 0.1f, 0.1f);
                    }
                    a.Timer -= dt;
                    if (a.Timer > 0f) return;
                    a.Motion?.Release();
                    Unclaim(a);
                    _host.TakeProp(prop, a.Load);
                    a.Job = Job.None;
                    if (a.Load.Count > 0) DeliverLoad(a);
                    return;
                }
                case Job.Feed:
                    if (!Valid(a.Target) || !Valid(a.Target2)) { Unclaim(a); a.Job = Job.None; break; }
                    if (!Arrived(a)) break;
                    eco.LoadFeed(a.Target, a.Target2, eco.WorkerCarry, a.Load);
                    foreach (var item in a.Load) _host!.FlyGood(item, _host.CentreOf(_host.Building(a.Target)!) + Vector3.up * 2f, a.Body.position + Vector3.up * 1.6f);
                    a.Motion?.Play("Lift", 1f);
                    if (a.Load.Count == 0) { Unclaim(a); a.Job = Job.None; break; }
                    a.Job = Job.Supply;
                    GoTo(a, _host!.DoorOf(a.Target2));
                    return;
                case Job.Wander:
                    if (Arrived(a)) { a.Timer -= dt; if (a.Timer <= 0) a.Job = Job.None; }
                    break;
                case Job.Harvest:
                case Job.Collect:
                case Job.Plant:
                    if (!Valid(a.Target)) { Unclaim(a); a.Job = Job.None; break; }
                    if (!Arrived(a)) break;
                    Face(a, _host!.CentreOf(_host.Building(a.Target)!));
                    if (!a.Started) { a.Started = true; a.Motion?.Hold(WorkAnim(WantTool(a))); }
                    if (a.Timer > 0) { a.Timer -= dt; return; }
                    a.Motion?.Release();
                    DoFarmWork(a, now);
                    return;
                case Job.Deliver:
                    if (!Valid(a.Target) || !eco.Working(a.Target)) { a.Job = Job.None; break; }
                    if (!Arrived(a)) break;
                    Unload(a);
                    return;
                case Job.Fetch:
                    if (!Valid(a.Target) || !Valid(a.Target2)) { Unclaim(a); a.Job = Job.None; break; }
                    if (!Arrived(a)) break;
                    if (eco.Machines.ContainsKey(a.Target2)) eco.LoadInputs(a.Target, a.Target2, Eco.WorkerCarry, a.Load);
                    else eco.LoadSupplies(a.Target, a.Target2, Eco.WorkerCarry, a.Load);
                    foreach (var item in a.Load) _host!.FlyGood(item, _host.CentreOf(_host.Building(a.Target)!) + Vector3.up * 2f, a.Body.position + Vector3.up * 1.6f);
                    a.Motion?.Play("Lift", 1f);
                    if (a.Load.Count == 0) { Unclaim(a); a.Job = Job.None; break; }
                    a.Job = Job.Supply;
                    GoTo(a, _host!.DoorOf(a.Target2));
                    return;
                case Job.Supply:
                    if (!Valid(a.Target2)) { a.Load.Clear(); Unclaim(a); a.Job = Job.None; break; }
                    if (!Arrived(a)) break;
                    foreach (var item in a.Load) _host!.FlyGood(item, a.Body.position + Vector3.up * 1.6f, _host.CentreOf(_host.Building(a.Target2)!) + Vector3.up * 2f);
                    if (eco.Machines.ContainsKey(a.Target2)) eco.FillMachine(a.Target2, a.Load, now);
                    else if (eco.Animals.ContainsKey(a.Target2)) eco.FillTrough(a.Target2, a.Load, now);
                    else eco.Deliver(a.Target2, a.Load);
                    a.Loads++;
                    eco.SpendEnergy(a.Home, GameplayRules.WorkEnergyPerLoad);
                    IslandAudio.Play("drop", a.Body.position, 0.5f, 0.1f, 0.1f);
                    a.Load.Clear();
                    Unclaim(a);
                    a.Job = Job.None;
                    return;
            }
            if (a.Job != Job.None && a.Job != Job.Wander) return;
            a.Repath -= dt;
            if (a.Repath > 0 && a.Job == Job.Wander) return;
            a.Repath = 1.5f;
            if (a.Load.Count > 0) { DeliverLoad(a); return; }
            if (a.Loads >= GameplayRules.WorkerLoadsBeforeRest)
            {
                a.Job = Job.Tired;
                a.Timer = (float)GameplayRules.WorkerRestSeconds;
                GoTo(a, _host!.DoorOf(a.Home) + Jitter(1.5f));
                return;
            }
            if (!PickFarmJob(a, now) && a.Job == Job.None)
            {
                a.Job = Job.Wander;
                a.Timer = Random.Range(2f, 5f);
                GoTo(a, _host!.DoorOf(a.Home) + Jitter(6f));
            }
        }

        private bool PickFarmJob(Agent a, double now)
        {
            var eco = Eco;
            var host = _host!;
            var homeAt = host.DoorOf(a.Home);
            var best = -1;
            var bestJob = Job.None;
            var bestD = float.MaxValue;
            void Consider(int uid, Job job, Vector3 at)
            {
                if (_claims.ContainsKey(uid)) return;
                if (Flat(at - homeAt).magnitude > workerReach) return;
                var d = Flat(at - a.Body.position).magnitude;
                if (d < bestD) { bestD = d; best = uid; bestJob = job; }
            }
            foreach (var kv in eco.Plots)
            {
                if (!eco.Working(kv.Key) || host.Building(kv.Key) == null) continue;
                var state = kv.Value.State(now);
                var at = host.CentreOf(host.Building(kv.Key)!);
                if (state == PlotState.Ripe && eco.WorkOn(kv.Key, WorkJob.Harvest)) Consider(kv.Key, Job.Harvest, at);
                else if (state == PlotState.Empty && eco.WorkOn(kv.Key, WorkJob.Replant) && eco.Coins >= (GameplayRules.CropOf(eco.SowCrop(kv.Key))?.SeedCost ?? int.MaxValue))
                    Consider(kv.Key, Job.Plant, at);
            }
            foreach (var kv in eco.Animals)
                if (eco.Working(kv.Key) && eco.WorkOn(kv.Key, WorkJob.Collect) && host.Building(kv.Key) != null && (kv.Value.StockAt(now) >= 2 || kv.Value.Side?.StockAt(now) >= 2)) Consider(kv.Key, Job.Collect, host.DoorOf(kv.Key));
            // supply orders: fetch from a farmhouse that has stock
            var farmhouse = eco.Departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && eco.Working(d.Uid) && d.Stored > 0)
                .OrderBy(d => Flat(host.DoorOf(d.Uid) - a.Body.position).magnitude).FirstOrDefault();
            if (farmhouse != null)
                foreach (var order in eco.SupplyOrders)
                {
                    var key = -1000000 - order.Key;
                    if (_claims.ContainsKey(key) || host.Building(order.Key) == null) continue;
                    var d = Flat(host.DoorOf(farmhouse.Uid) - a.Body.position).magnitude * 0.5f;   // orders come first
                    if (d < bestD && Flat(host.DoorOf(farmhouse.Uid) - homeAt).magnitude <= workerReach * 1.5f)
                    {
                        bestD = d; best = key; bestJob = Job.Fetch;
                        a.Target2 = order.Key;
                        a.Spot = host.DoorOf(farmhouse.Uid);
                        a.Target = farmhouse.Uid;
                    }
                }
            // animal houses running low on feed: carry feed (or grain) to their trough
            foreach (var kv in eco.Animals)
            {
                if (!kv.Value.Producer.Eats || !eco.Working(kv.Key) || !eco.WorkOn(kv.Key, WorkJob.Feed) || host.Building(kv.Key) == null) continue;
                if (kv.Value.FeedLeft(now) >= 10) continue;
                var key = -3000000 - kv.Key;
                if (_claims.ContainsKey(key)) continue;
                var store = eco.Departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && eco.Working(d.Uid)
                        && d.Stock.Keys.Any(i => GameplayRules.FeedValue(i) > 0))
                    .OrderBy(d => Flat(host.DoorOf(d.Uid) - a.Body.position).magnitude).FirstOrDefault();
                if (store == null) continue;
                var dist = Flat(host.DoorOf(store.Uid) - a.Body.position).magnitude * 0.6f;   // hungry animals come first
                if (dist < bestD && Flat(host.DoorOf(kv.Key) - homeAt).magnitude <= workerReach)
                {
                    bestD = dist; best = key; bestJob = Job.Feed;
                    a.Target2 = kv.Key;
                    a.Spot = host.DoorOf(store.Uid);
                    a.Target = store.Uid;
                }
            }
            // wood and stone running short (or a site waiting on them): fell a tree or break a rock nearby
            var nature = host.Nature;
            if (nature != null)
            {
                int Have(string item) => eco.Departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && eco.Working(d.Uid)).Sum(d => d.Stock.TryGetValue(item, out var n) ? n : 0);
                int Missing(string item) => eco.Sites.Sum(c => c.Missing.TryGetValue(item, out var n) ? n : 0);
                var need = Have("wood") < 20 + Missing("wood") ? "wood" : Have("stone") < 10 + Missing("stone") ? "stone" : null;
                if (need != null)
                {
                    var prop = nature.Props.Where(p => p != null && p.gameObject.activeInHierarchy && IslandEconomyHost.YieldOf(p.kind) == need && !_claims.ContainsKey(-4000000 - p.index)
                                                     && Flat(p.transform.position - homeAt).magnitude <= workerReach * 1.6f)
                        .OrderBy(p => Flat(p.transform.position - a.Body.position).sqrMagnitude).FirstOrDefault();
                    if (prop != null)
                    {
                        var d = Flat(prop.transform.position - a.Body.position).magnitude * (Missing(need) > 0 ? 0.6f : 1.3f);   // a waiting site comes first
                        if (d < bestD)
                        {
                            bestD = d; best = -4000000 - prop.index; bestJob = Job.Chop;
                            a.Target2 = prop.index;
                            a.Spot = prop.transform.position;
                        }
                    }
                }
            }
            // machines: carry away what they made, and keep their hoppers topped up from the farmhouse
            foreach (var kv in eco.Machines)
            {
                if (!eco.Working(kv.Key) || host.Building(kv.Key) == null) continue;
                var m = kv.Value;
                if (m.Output >= 2 && eco.WorkOn(kv.Key, WorkJob.Collect)) Consider(kv.Key, Job.Collect, host.DoorOf(kv.Key));
                var key = -2000000 - kv.Key;
                if (_claims.ContainsKey(key) || eco.HopperRoom(kv.Key) < 2 || !eco.WorkOn(kv.Key, WorkJob.Refill)) continue;
                // the nearest farmhouse that has something this machine uses
                var store = eco.Departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && eco.Working(d.Uid)
                        && m.Recipe.Inputs.Any(i => d.Stock.ContainsKey(i.item) && m.HopperOf(i.item) < GameplayRules.HopperSize))
                    .OrderBy(d => Flat(host.DoorOf(d.Uid) - a.Body.position).magnitude).FirstOrDefault();
                if (store == null) continue;
                var dist = Flat(host.DoorOf(store.Uid) - a.Body.position).magnitude * (m.Busy ? 1.2f : 0.7f);   // idle machines first
                if (dist < bestD && Flat(host.DoorOf(kv.Key) - homeAt).magnitude <= workerReach)
                {
                    bestD = dist; best = key; bestJob = Job.Fetch;
                    a.Target2 = kv.Key;
                    a.Spot = host.DoorOf(store.Uid);
                    a.Target = store.Uid;
                }
            }
            if (best == -1) return false;
            _claims[best] = a;
            a.Job = bestJob;
            if (bestJob == Job.Chop)
            {
                // stand just off the trunk, on the near side
                var off = Flat(a.Body.position - a.Spot);
                GoTo(a, a.Spot + (off.sqrMagnitude > 0.01f ? off.normalized : Vector3.forward) * 1.4f);
                a.Timer = 6f;
                a.Swing = 0.4f;
                a.Started = false;
                return true;
            }
            if (bestJob == Job.Fetch || bestJob == Job.Feed)
            {
                GoTo(a, a.Spot);
                return true;
            }
            a.Target = best;
            a.Started = false;
            a.Timer = bestJob == Job.Harvest ? 2.4f : bestJob == Job.Plant ? 1.6f : 2f;
            var b = host.Building(best)!;
            // fields: walk into the crop; animal houses: to the door
            GoTo(a, bestJob == Job.Collect ? host.DoorOf(best) + Jitter(0.8f) : host.CentreOf(b) + Jitter(host.SizeOf(b).x * 0.3f));
            return true;
        }

        private void DoFarmWork(Agent a, double now)
        {
            var eco = Eco;
            var host = _host!;
            var b = host.Building(a.Target)!;
            switch (a.Job)
            {
                case Job.Harvest:
                    eco.TakeHarvest(a.Target, now, Eco.WorkerCarry, a.Load);
                    // sow it again straight away if it's bare and there's seed money
                    if (eco.Plots[a.Target].State(now) == PlotState.Empty && eco.WorkOn(a.Target, WorkJob.Replant)) eco.Replant(a.Target, now);
                    break;
                case Job.Collect:
                    if (eco.Machines.ContainsKey(a.Target)) eco.TakeOutput(a.Target, now, Eco.WorkerCarry, a.Load);
                    else eco.TakeProduce(a.Target, now, Eco.WorkerCarry, a.Load);
                    break;
                case Job.Plant:
                    eco.Replant(a.Target, now);
                    break;
            }
            foreach (var item in a.Load) host.FlyGood(item, host.CentreOf(b) + Vector3.up * 0.6f, a.Body.position + Vector3.up * 1.6f);
            if (a.Load.Count > 0) IslandAudio.Play("pop", a.Body.position, 0.45f, 0.1f, 0.1f);
            Unclaim(a);
            a.Job = Job.None;
            a.Repath = 0f;
            if (a.Load.Count > 0) DeliverLoad(a);
        }

        private void DeliverLoad(Agent a)
        {
            var eco = Eco;
            var house = eco.Departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && eco.Working(d.Uid) && d.Stored < d.StorageCapacity)
                .OrderBy(d => Flat(_host!.DoorOf(d.Uid) - a.Body.position).magnitude).FirstOrDefault();
            if (house == null)
            {
                _host!.Say("The farmhouse is full — workers are waiting with their loads");
                a.Job = Job.Wander;
                a.Timer = 5f;
                return;
            }
            a.Job = Job.Deliver;
            a.Target = house.Uid;
            GoTo(a, _host!.DoorOf(house.Uid) + Jitter(0.8f));
        }

        private void Unload(Agent a)
        {
            var eco = Eco;
            var host = _host!;
            var to = host.CentreOf(host.Building(a.Target)!) + Vector3.up * 2f;
            var kept = new List<string>();
            foreach (var item in a.Load)
            {
                if (eco.Store(a.Target, item, 1) == 1) host.FlyGood(item, a.Body.position + Vector3.up * 1.6f, to);
                else kept.Add(item);
            }
            a.Load.Clear();
            a.Load.AddRange(kept);
            eco.SpendEnergy(a.Home, GameplayRules.WorkEnergyPerLoad);
            a.Loads++;
            a.Motion?.Play("Lift", 0.8f);
            IslandAudio.Play("drop", a.Body.position, 0.5f, 0.1f, 0.1f);
            a.Job = Job.None;
            a.Repath = kept.Count > 0 ? 4f : 0f;
        }

        // ---- builders -----------------------------------------------------------------------------

        private void Builder(Agent a, float dt)
        {
            var eco = Eco;
            if (a.Job == Job.Leave) { if (Arrived(a)) Vanish(a); return; }
            if (a.Job == Job.Down) { DownTimer(a, dt); return; }
            if (Hide(a, dt)) return;
            if (a.Job == Job.Build)
            {
                if (!eco.Crews.ContainsKey(a.Target) || !eco.Sites.Any(s => s.Uid == a.Target) || !Valid(a.Target)) { a.Job = Job.None; a.Motion?.Release(); Unclaim(a); }
                else if (Arrived(a))
                {
                    Face(a, _host!.CentreOf(_host.Building(a.Target)!));
                    a.Motion?.Hold(WorkAnim(WantTool(a)));
                    return;
                }
                else return;
            }
            // join a site that has builders booked but not enough here
            foreach (var kv in eco.Crews)
            {
                var here = _agents.Count(x => x.Role == Role.Builder && x.Job == Job.Build && x.Target == kv.Key);
                if (here >= kv.Value || _host!.Building(kv.Key) == null) continue;
                var b = _host.Building(kv.Key)!;
                var size = _host.SizeOf(b);
                var angle = (here * 2.1f + kv.Key) % (Mathf.PI * 2f);
                var at = _host.CentreOf(b) + new Vector3(Mathf.Cos(angle) * (size.x * 0.5f + 0.9f), 0, Mathf.Sin(angle) * (size.y * 0.5f + 0.9f));
                a.Job = Job.Build;
                a.Target = kv.Key;
                a.Timer = 0f;
                GoTo(a, at);
                return;
            }
            if (a.Job == Job.Wander && !Arrived(a)) return;
            a.Timer -= dt;
            if (a.Timer > 0) return;
            a.Job = Job.Wander;
            a.Timer = Random.Range(3f, 7f);
            GoTo(a, _host!.DoorOf(a.Home) + Jitter(5f));
        }

        /// <summary>Buildings going up rise out of the ground inside a ring of light; finished ones spring up.</summary>
        private void ShowSites()
        {
            var eco = Eco;
            var host = _host!;
            foreach (var site in eco.Sites)
            {
                var b = host.Building(site.Uid);
                if (b == null) continue;
                var p = (float)site.Progress;
                // new buildings rise out of the ground; repairs keep their shape (the host shows the damage)
                if (site.Repair) _repairs.Add(site.Uid);
                else
                {
                    if (!_siteScale.ContainsKey(site.Uid)) _siteScale[site.Uid] = b.transform.localScale;
                    var full = _siteScale[site.Uid];
                    b.transform.localScale = new Vector3(full.x, full.y * Mathf.Lerp(0.06f, 1f, p), full.z);
                }
                if (!_siteRings.TryGetValue(site.Uid, out var ring) || ring == null)
                {
                    var go = new GameObject("Site ring");
                    go.transform.SetParent(transform, false);
                    ring = go.AddComponent<PadRing>();
                    ring.Build(RingMaterial(), new Color(0.75f, 0.55f, 1f));
                    _siteRings[site.Uid] = ring;
                }
                var size = host.SizeOf(b);
                var c = host.CentreOf(b);
                ring.transform.position = new Vector3(c.x, GroundHeight.At(c.x, c.z, c.y) + 0.05f, c.z);
                ring.transform.localScale = Vector3.one * (Mathf.Max(size.x, size.y) * 0.5f + 0.8f) / 1.5f;
                ring.Show(p, eco.Crews.ContainsKey(site.Uid), false);
                // hammering and sawing while builders are at it
                if (!_siteSounds.TryGetValue(site.Uid, out var hum) || hum == null)
                {
                    hum = IslandAudio.Loop("build_loop", true)!;
                    if (hum != null) _siteSounds[site.Uid] = hum;
                }
                if (hum != null) { hum.At = c; hum.Target = eco.Crews.ContainsKey(site.Uid) ? 0.6f : 0f; }
            }
            foreach (var uid in eco.TakeFinished())
            {
                var b = host.Building(uid);
                if (_siteRings.TryGetValue(uid, out var ring) && ring != null) Destroy(ring.gameObject);
                _siteRings.Remove(uid);
                if (_siteSounds.TryGetValue(uid, out var doneHum)) { IslandAudio.Release(doneHum); _siteSounds.Remove(uid); }
                if (b != null)
                {
                    if (_siteScale.TryGetValue(uid, out var full)) b.transform.localScale = full;
                    Magic.Pop(b.gameObject);
                    host.Say(_repairs.Remove(uid) ? $"{Pretty(b.id)} is repaired" : $"{Pretty(b.id)} is ready!");
                }
                _siteScale.Remove(uid);
            }
            // a site that was sold or moved away
            foreach (var uid in _siteRings.Keys.Where(u => !eco.Sites.Any(s => s.Uid == u)).ToList())
            {
                if (_siteRings[uid] != null) Destroy(_siteRings[uid].gameObject);
                _siteRings.Remove(uid);
                if (_siteSounds.TryGetValue(uid, out var goneHum)) { IslandAudio.Release(goneHum); _siteSounds.Remove(uid); }
                _siteScale.Remove(uid);
            }
        }

        // ---- warriors -----------------------------------------------------------------------------

        private void Warrior(Agent a, float dt)
        {
            var eco = Eco;
            if (a.Job == Job.Leave) { if (Arrived(a)) Vanish(a); return; }
            if (a.Job == Job.Down) { DownTimer(a, dt); return; }
            var house = eco.Departments.TryGetValue(a.Home, out var d) ? d : null;
            if (house == null) { Leave(a); return; }
            var homeAt = _host!.DoorOf(a.Home);
            var reach = house.Levels.Kind == DepartmentKind.Watch ? guardReach * 0.75f : guardReach;
            // hungry warriors are weak (half the hit); iron for every warrior makes them hit harder
            a.Damage = 24f * (house.Starving ? 0.5f : 1f) * (house.Armed ? 1.3f : 1f);

            if (a.Foe != null && (a.Foe.Job == Job.Down || a.Foe.Gone || a.Foe.Job == Job.Flee && Flat(a.Foe.Body.position - homeAt).magnitude > reach * 1.3f)) a.Foe = null;
            if (a.Foe == null)
            {
                a.Repath -= dt;
                if (a.Repath <= 0)
                {
                    a.Repath = 0.5f;
                    a.Foe = _agents.Where(r => r.Role == Role.Raider && r.Job != Job.Down && !r.Gone && Flat(r.Body.position - homeAt).magnitude <= reach)
                        .OrderBy(r => _agents.Count(w => w.Role == Role.Warrior && w.Foe == r))
                        .ThenBy(r => Flat(r.Body.position - a.Body.position).magnitude).FirstOrDefault();
                    if (a.Foe != null) { a.Job = Job.Fight; a.Repath = 0f; }
                }
            }
            if (a.Foe != null)
            {
                Fight(a, a.Foe, dt, eco);
                return;
            }
            // on guard: heal near the house (faster with a clinic), stroll about
            if (Flat(a.Body.position - homeAt).magnitude < 10f) a.Hp = Mathf.Min(a.MaxHp, a.Hp + (eco.Has("clinic") ? 12f : 6f) * dt);
            if (a.Job == Job.Fight) { a.Job = Job.None; GoTo(a, homeAt + Jitter(4f)); }
            if (!Arrived(a)) return;
            a.Timer -= dt;
            if (a.Timer > 0) return;
            a.Job = Job.Wander;
            a.Timer = Random.Range(3f, 8f);
            GoTo(a, homeAt + Jitter(6f));
        }

        private void Fight(Agent a, Agent foe, float dt, IslandEconomy eco)
        {
            var gap = Flat(foe.Body.position - a.Body.position).magnitude;
            if (gap > 1.7f)
            {
                a.Repath -= dt;
                if (a.Repath <= 0 || a.Route.Count == 0)
                {
                    a.Repath = gap > 12f ? 1.2f : 0.3f;
                    if (gap > 12f) GoTo(a, foe.Body.position);
                    else { a.Route.Clear(); a.Route.Add(foe.Body.position); a.Next = 0; }
                }
                return;
            }
            a.Route.Clear();
            Face(a, foe.Body.position);
            a.Swing -= dt;
            if (a.Swing > 0) return;
            a.Swing = a.Role == Role.Warrior ? 1.1f : 1.4f;
            a.Motion?.Play(a.Role == Role.Warrior ? "Slash" : (Random.value < 0.5f ? "Punch" : "Slash"), 0.9f);
            IslandAudio.Play("swing", a.Body.position, 0.7f, 0.12f, 0.05f);
            if (a.Role == Role.Warrior) eco.SpendEnergy(a.Home, GameplayRules.FightEnergy);
            Hit(foe, a, a.Damage);
        }

        private float _farmerSwing;
        private float _farmhouseBuilderBackAt;

        /// <summary>The farmer isn't helpless: standing next to a raider, they swing at it (weaker than a warrior, but enough for a lone thief).</summary>
        private void FarmerFights(float dt)
        {
            var player = _host!.Player;
            _farmerSwing -= dt;
            if (player == null || player.Riding || !_raidLanded || _farmerSwing > 0) return;
            var at = player.transform.position;
            var foe = _agents.Where(r => r.Role == Role.Raider && r.Job != Job.Down && !r.Gone && Flat(r.Body.position - at).magnitude < 2.4f)
                .OrderBy(r => Flat(r.Body.position - at).magnitude).FirstOrDefault();
            if (foe == null) return;
            _farmerSwing = 0.9f;
            var d = Flat(foe.Body.position - at);
            if (d.sqrMagnitude > 0.01f) player.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            if (player.TryGetComponent<CharacterMotion>(out var m)) m.Play("Punch", 0.6f);
            IslandAudio.Play("swing", at, 0.7f, 0.12f, 0.05f, 1.1f);
            Hit(foe, null, 18f);
        }

        private void Hit(Agent victim, Agent? by, float damage)
        {
            if (victim.Job == Job.Down || victim.Gone) return;
            victim.Hp -= damage;
            IslandAudio.Play("hit", victim.Body.position, 0.75f, 0.1f, 0.05f, victim.Role == Role.Raider ? 0.9f : 1.1f);
            if (by != null && victim.Foe == null && (victim.Role == Role.Raider || victim.Role == Role.Warrior)) victim.Foe = by;   // fight back
            if (victim.Hp > 0)
            {
                victim.Motion?.Play("Hit", 0.4f);
                if (victim.Role == Role.Raider && victim.Hp < victim.MaxHp * 0.25f && victim.Job != Job.Flee) Flee(victim);
                return;
            }
            victim.Job = Job.Down;
            victim.Timer = 2.6f;
            victim.Route.Clear();
            victim.Foe = null;
            Unclaim(victim);
            victim.Motion?.Hold(victim.Role == Role.Raider ? "Defeat" : "Fall");
            switch (victim.Role)
            {
                case Role.Warrior:
                    Eco.LosePerson(victim.Home);
                    _killed++;
                    _host!.Say(Eco.Departments.TryGetValue(victim.Home, out var w) && w.Levels.Kind == DepartmentKind.Watch ? "A guard has fallen" : "A warrior has fallen");
                    IslandAudio.Play("fail", null, 0.7f, 0f, 1f);
                    break;
                case Role.Worker:
                case Role.Builder:
                    // whoever they lived with is one short now (the farmhouse's builder is replaced after a while)
                    if (Eco.Departments.TryGetValue(victim.Home, out var lived) && lived.Levels.Kind == DepartmentKind.Farmhouse)
                        _farmhouseBuilderBackAt = _playTime + 120f;
                    else Eco.LosePerson(victim.Home);
                    _killed++;
                    _host!.Say(victim.Role == Role.Worker ? "The zombies got a farm worker!" : "The zombies got a builder!");
                    IslandAudio.Play("fail", null, 0.7f, 0f, 1f);
                    // what they were carrying falls where they fell: pick it up before it's lost
                    if (victim.Load.Count > 0) { DropLoot(victim.Body.position, new List<string>(victim.Load), null); victim.Load.Clear(); }
                    // a hunter who's had two goes home pleased with itself
                    if (by != null && by.Role == Role.Raider && ++by.Kills >= 2 && by.Kind == nameof(RaiderKind.Hunter)) Flee(by);
                    break;
                default:
                    if (victim.Load.Count > 0)
                    {
                        DropLoot(victim.Body.position, victim.Load);
                        victim.Load.Clear();
                    }
                    if (victim.Boss.Length > 0) BossDown(victim);
                    break;
            }
        }

        /// <summary>A beaten boss: from the cure level it turns back into a townsperson who stays; before that it just slinks off.</summary>
        private void BossDown(Agent boss)
        {
            var name = GameplayRules.BossName(boss.Boss);
            if (Eco.BestLevel < GameplayRules.CureLevel)
            {
                _host!.Say($"{Cap(name)} is down — zombies never stay down for long", 3f);
                return;
            }
            Eco.Count("cured_" + boss.Boss);
            Eco.Count("zombies_cured");
            _cured++;
            _host!.Say($"{Cap(name)} is cured! A new townsperson joins the island", 4f);
            IslandAudio.Play("levelup", boss.Body.position, 0.8f, 0f, 1f);
            // the boss in its own clothes, its skin back to a person's (else any townsperson)
            var cured = curedPrefabs.FirstOrDefault(p => p != null && p.name.Contains(boss.Boss == "mayor" ? "clerk" : boss.Boss));
            if (cured != null || villagerPrefabs.Length > 0)
            {
                var who = Spawn(Role.Villager, -1, cured ?? villagerPrefabs[Random.Range(0, villagerPrefabs.Length)], boss.Body.position + Jitter(1f));
                who.Job = Job.Wander;
                Magic.Appear(who.Body.gameObject, 0.8f, new Color(0.6f, 1f, 0.7f));
            }
        }

        private static string Cap(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;

        private void DownTimer(Agent a, float dt)
        {
            a.Timer -= dt;
            if (a.Timer <= 0) Vanish(a);
        }

        /// <summary>Workers and builders run home and keep their heads down while raiders are near.</summary>
        private bool Hide(Agent a, float dt)
        {
            if (!_raidLanded) return false;
            var danger = _agents.Any(r => r.Role == Role.Raider && r.Job != Job.Down && !r.Gone && Flat(r.Body.position - a.Body.position).magnitude < 10f);
            if (a.Job == Job.Hide)
            {
                var stillNear = _agents.Any(r => r.Role == Role.Raider && r.Job != Job.Down && !r.Gone && Flat(r.Body.position - _host!.DoorOf(a.Home)).magnitude < 18f);
                if (!danger && !stillNear && Arrived(a)) { a.Job = Job.None; a.Speed /= 1.5f; a.Motion?.Release(); return false; }
                if (Arrived(a)) a.Motion?.Hold("Shake");   // cowering at home, shaking
                return true;
            }
            if (!danger) return false;
            Unclaim(a);
            a.Motion?.Release();
            a.Job = Job.Hide;
            a.Speed *= 1.5f;
            GoTo(a, _host!.DoorOf(a.Home) + Jitter(1f));
            return true;
        }

        // ---- raids --------------------------------------------------------------------------------

        private enum RaiderKind { Wrecker, Hunter, Thief }

        private bool _raidLanded;
        private float _landAt;
        private float _raidEndsAt;
        private int _killed;
        private int _ruined;
        private double _threatAtRaid;
        private int _raidSize;
        private int _coinsTaken;
        private bool _satisfied;

        /// <summary>
        /// What the raid has got so far: goods carried off, people killed, buildings wrecked, coins taken.
        /// Raiders come for a haul, not a war: once it's big enough for a raid this size they head back
        /// to their boats with it.
        /// </summary>
        private int Spoils => _agents.Where(a => a.Role == Role.Raider && !a.Gone).Sum(a => a.Load.Count) + _raidStolen
                              + _killed * 3 + _ruined * 8 + _coinsTaken / 20;
        // a raid takes at most a tenth of what's in store
        private int SpoilsWanted => Mathf.Max(3, Mathf.Min(6 + 5 * _raidSize, _storedAtRaid / 10));
        private int _storedAtRaid;

        /// <summary>
        /// One raid a day (<see cref="RaidSchedule"/>): a few minutes into the session if today's hasn't
        /// happened, or as soon as its planned time comes while the player is here; warned of first.
        /// </summary>
        private void RaidClock(double now)
        {
            var eco = Eco;
            if (!_raidOn)
            {
                var due = RaidSchedule.DueNow && _playTime > 30f;
                if (_playTime < _nextRaid && !due) return;
                if (!_forcedRaid && RaidSchedule.DoneToday) { _nextRaid = _playTime + 60f; return; }
                // a Raid Shield keeps them away for the day
                if (!_forcedRaid && IslandBoosts.Running("shield")) { RaidSchedule.Finish("shield", 0); return; }
                if (raiderPrefabs.Length == 0 || _host!.Buildings.Count() < GameplayRules.RaidMinBuildings || Eco.BestLevel < GameplayRules.RaidStartLevel) { _nextRaid = _playTime + 60f; return; }
                Warn();
                return;
            }
            if (!_raidLanded && _playTime >= _landAt) Land();
            if (_raidLanded && !_satisfied && Spoils >= SpoilsWanted)
            {
                _satisfied = true;
                var running = _agents.Where(r => r.Role == Role.Raider && r.Job != Job.Down && r.Job != Job.Flee && !r.Gone).ToList();
                foreach (var r in running) Flee(r);
                if (running.Count > 0)
                {
                    _host!.Say("The zombies got what they came for — they're shambling back into the fog!", 3f);
                    IslandAudio.Play("raid_horn", null, 0.6f, 0f, 1f, 1.2f);
                }
            }
            // raiders don't stay all day: after a while they head back to their boats
            if (_raidLanded && _playTime >= _raidEndsAt)
                foreach (var r in _agents.Where(r => r.Role == Role.Raider && r.Job != Job.Down && r.Job != Job.Flee).ToList()) Flee(r);
        }

        private void Warn()
        {
            var host = _host!;
            var d = host.Grid!.Data;
            var centre = new Vector3(d.origin.x + d.width * d.cellSize * 0.5f, 0, d.origin.y + d.depth * d.cellSize * 0.5f);
            var farm = host.Buildings.Select(u => host.Building(u)).Where(b => b != null).Select(b => b!.transform.position).ToList();
            if (farm.Count > 0) centre = new Vector3(farm.Average(p => p.x), 0, farm.Average(p => p.z));
            var bearing = Random.Range(0f, Mathf.PI * 2f);
            var dir = new Vector3(Mathf.Cos(bearing), 0, Mathf.Sin(bearing));
            var shore = centre;
            for (var r = d.width * d.cellSize; r > 0; r -= d.cellSize)
            {
                var p = centre + dir * r;
                var cell = d.CellOf(p);
                if (host.Grid.Dry(cell.x, cell.y)) { shore = p - dir * 2f; break; }
            }
            shore.y = GroundHeight.At(shore.x, shore.z, 0);
            _raidOn = true;
            _raidLanded = false;
            _raidAt = shore;
            _raidFrom = Compass(shore - centre);
            _raidStolen = 0;
            _killed = 0;
            _ruined = 0;
            _coinsTaken = 0;
            _satisfied = false;
            _threatAtRaid = Eco.Threat;
            _storedAtRaid = Eco.Departments.Values.Where(d => d.Levels.Kind is DepartmentKind.Farmhouse or DepartmentKind.Silo).Sum(d => d.Stored);
            _bossesTonight.Clear();
            _bossesTonight.AddRange(GameplayRules.BossesTonight(Eco.BestLevel, Eco.Stat("raids_seen"), id => Eco.Stat("cured_" + id) > 0));
            _bossesCame.Clear();
            _cured = 0;
            if (_bossesTonight.Contains("mime"))
            {
                // the Mime comes without a sound: no horn, almost no warning
                _landAt = _playTime + 3f;
                host.Say("Something is moving in the fog…", 3f);
            }
            else
            {
                _landAt = _playTime + (float)Eco.RaidWarning;
                host.Say(_bossesTonight.Count > 0
                    ? $"Zombies sighted to the {_raidFrom} — {string.Join(" and ", _bossesTonight.Select(GameplayRules.BossName))} leads them!"
                    : $"Zombies sighted to the {_raidFrom}!", 3f);
                IslandAudio.Play("raid_horn", null, 1f, 0f, 1f);
            }
            Debug.Log($"[Raid] sighted to the {_raidFrom}; threat {_threatAtRaid:0.0}, defence {Eco.Defence:0.0}, {GameplayRules.RaidSize(Eco.BestLevel, _threatAtRaid, Eco.Stat("raids_seen"))} raiders in {Eco.RaidWarning}s");
        }

        private void Land()
        {
            _raidLanded = true;
            _raidEndsAt = _playTime + 150f;
            var count = GameplayRules.RaidSize(Eco.BestLevel, _threatAtRaid, Eco.Stat("raids_seen"));
            if (_bossesTonight.Contains("policeman")) count++;   // he rouses one more
            Eco.Count("raids_seen");
            var strength = (float)GameplayRules.RaiderStrength(Eco.BestLevel);
            for (var i = 0; i < count; i++)
            {
                var prefab = raiderPrefabs[Random.Range(0, raiderPrefabs.Length)];
                var a = Spawn(Role.Raider, -1, prefab, _raidAt + Jitter(3f));
                a.Shore = _raidAt;
                // the four common zombies: two thieves, a wrecker and a hunter
                var kind = prefab.name.EndsWith("3") ? RaiderKind.Wrecker : prefab.name.EndsWith("4") ? RaiderKind.Hunter : RaiderKind.Thief;
                SetKind(a, kind, strength);
                // wreckers come armed: a crowbar, a pipe or a bat from the Zombies pack
                if (kind == RaiderKind.Wrecker) ZombieWeapon(a, new[] { "Crowbar", "Pipe", "Bat" }[Random.Range(0, 3)]);
            }
            foreach (var id in _bossesTonight)
            {
                var prefab = bossPrefabs.FirstOrDefault(p => p != null && p.name.ToLowerInvariant().Contains(id == "mayor" ? "clerk" : id));
                if (prefab == null) continue;
                var a = Spawn(Role.Raider, -1, prefab, _raidAt + Jitter(2f));
                a.Shore = _raidAt;
                a.Boss = id;
                var (hp, dmg, spd, habit) = GameplayRules.BossTraits(id);
                SetKind(a, habit == "wrecker" ? RaiderKind.Wrecker : habit == "hunter" ? RaiderKind.Hunter : RaiderKind.Thief, strength);
                a.MaxHp *= (float)hp; a.Hp = a.MaxHp; a.Damage *= (float)dmg; a.Speed *= (float)spd;
                if (id == "mayor") a.Body.localScale *= 1.3f;
                // bosses carry what they had in life: the policeman's baton, the builder's pipe wrench, the mayor's road sign
                var w = id switch { "policeman" => "Baton", "builder" => "PipeWrench", "mayor" => "RoadSign", "clerk" => "Weapon_005", _ => null };
                if (w != null) ZombieWeapon(a, w);
                _bossesCame.Add(id);
                count++;
            }
            _raidSize = count;
            _host!.Say($"{count} zombie{(count == 1 ? "" : "s")} shamble in from the {_raidFrom}!", 3f);
            if (!_bossesTonight.Contains("mime")) IslandAudio.Play("raid_horn", null, 0.8f, 0f, 0f, 0.8f);
        }

        private void SetKind(Agent a, RaiderKind kind, float strength)
        {
            a.Kind = kind.ToString();
            (a.MaxHp, a.Damage, a.Speed) = kind switch
            {
                RaiderKind.Wrecker => (150f, 12f, 2.6f),
                RaiderKind.Hunter => (120f, 16f, 3.2f),
                _ => (80f, 10f, 4.2f),
            };
            a.MaxHp *= strength;
            a.Damage *= strength;
            a.Hp = a.MaxHp;
            a.Job = Job.None;
        }

        private void EndRaid()
        {
            _raidOn = false;
            _raidLanded = false;
            _nextRaid = _playTime + 60f;
            var losses = new List<string>();
            if (_raidStolen > 0) losses.Add($"{_raidStolen} goods stolen");
            if (_killed > 0) losses.Add($"{_killed} people killed");
            if (_ruined > 0) losses.Add($"{_ruined} building{(_ruined == 1 ? "" : "s")} ruined");
            if (_coinsTaken > 0) losses.Add($"{_coinsTaken} coins taken");
            if (losses.Count == 0) Eco.Count("raids_won");
            LastRaidOutcome = (_raidSize, _raidStolen, _killed, _ruined, _coinsTaken, losses.Count == 0);
            LastRaidAway = false;
            RaidsEnded++;
            if (!_forcedRaid) RaidSchedule.Finish(losses.Count == 0 ? "won" : "lost", _raidSize);
            _forcedRaid = false;
            LastRaidBosses = string.Join(", ", _bossesCame.Select(GameplayRules.BossName));
            LastRaidCured = _cured;
            var summary = losses.Count == 0 ? "The zombies are driven off!" : (_satisfied ? "The zombies got away — " : "The night is over — ") + string.Join(", ", losses);
            _host!.Say(summary, 4f);
            _lastRaid = losses.Count == 0 ? $"We drove off {_raidSize} zombie{(_raidSize == 1 ? "" : "s")} and lost nothing." : $"{_raidSize} zombies came. They got: {string.Join(", ", losses)}.";
            IslandAudio.Play(losses.Count > 0 ? "fail" : "cheer", null, 0.8f, 0f, 1f);
            // a night beaten: the army and the labourers dance
            if (losses.Count == 0)
                foreach (var a in _agents.Where(x => (x.Role == Role.Warrior || x.Role == Role.Worker) && x.Job != Job.Down && !x.Gone && x.Motion != null))
                {
                    var r = Random.value;
                    if (r < 0.6f) a.Motion!.Dance(Random.Range(3f, 5f));
                    else if (r < 0.75f) a.Motion!.Play(Random.value < 0.5f ? "Flip" : "RunFlip", 2f);
                    else if (r < 0.9f) a.Motion!.Play("JumpStart", 0.4f);
                    else a.Motion!.Play("Cheer", 2.5f);
                }
            Debug.Log($"[Raid] over: {string.Join(", ", losses)}");
        }

        /// <summary>The server ran today's raid while the player was away: the island takes the same losses, and the game shows them.</summary>
        private void ApplyAwayRaid(double now)
        {
            var o = RaidSchedule.Pending;
            if (o == null || _raidOn) return;
            Eco.ApplyRaid(o, now);
            LastRaidOutcome = (o.size, o.StolenTotal, 0, o.ruined.Count, o.coins, o.won);
            LastRaidAway = true;
            _lastRaid = o.won ? $"While you were away we drove off {o.size} zombie{(o.size == 1 ? "" : "s")}." : $"While you were away {o.size} zombies came.";
            RaidsEnded++;
            RaidSchedule.MarkApplied();
            Debug.Log($"[Raid] applied the server's raid: {o.size} raiders, won {o.won}, {o.StolenTotal} goods, {o.coins} coins, {o.ruined.Count} ruined");
        }

        private void Raider(Agent a, double now, float dt)
        {
            var eco = Eco;
            if (a.Job == Job.Down) { DownTimer(a, dt); return; }
            if (a.Foe != null && (a.Foe.Job == Job.Down || a.Foe.Gone || a.Foe.Job == Job.Hide && Arrived(a.Foe))) a.Foe = null;
            if (a.Job == Job.Flee)
            {
                if (Arrived(a))
                {
                    _raidStolen += a.Load.Count;
                    a.Load.Clear();
                    Vanish(a);
                }
                return;
            }
            // someone fighting them gets fought back
            if (a.Foe != null && Flat(a.Foe.Body.position - a.Body.position).magnitude < 6f)
            {
                Fight(a, a.Foe, dt, eco);
                return;
            }
            var kind = a.Kind == nameof(RaiderKind.Wrecker) ? RaiderKind.Wrecker : a.Kind == nameof(RaiderKind.Hunter) ? RaiderKind.Hunter : RaiderKind.Thief;
            // people in reach get hit; hunters go looking for them
            var preyReach = kind == RaiderKind.Hunter ? 25f : 3f;
            // people who made it indoors (hiding at home) are safe
            var prey = _agents.Where(p => (p.Role == Role.Worker || p.Role == Role.Builder || p.Role == Role.Warrior) && p.Job != Job.Down && !p.Gone
                    && !(p.Job == Job.Hide && Arrived(p))
                    && Flat(p.Body.position - a.Body.position).magnitude < preyReach)
                .OrderBy(p => Flat(p.Body.position - a.Body.position).magnitude).FirstOrDefault();
            if (prey != null)
            {
                a.Foe = prey;
                Fight(a, prey, dt, eco);
                return;
            }
            // the farmer gets knocked down and spills part of the basket
            var player = _host!.Player;
            if (player != null && !player.Riding && _playTime >= a.LeaveFarmerUntil)
            {
                var gap = Flat(player.transform.position - a.Body.position).magnitude;
                if (gap < (kind == RaiderKind.Hunter ? 25f : 2.5f))
                {
                    if (gap > 1.8f) { a.Route.Clear(); a.Route.Add(player.transform.position); a.Next = 0; return; }
                    Face(a, player.transform.position);
                    a.Swing -= dt;
                    if (a.Swing <= 0)
                    {
                        a.Swing = 2.2f;
                        a.Motion?.Play("Punch", 0.9f);
                        KnockFarmer(player);
                        a.LeaveFarmerUntil = _playTime + 8f;
                    }
                    return;
                }
            }
            if (a.Job == Job.Raid)
            {
                if (!Valid(a.Target) || eco.IsRuined(a.Target)) { a.Job = Job.None; a.Repath = 0f; return; }
                if (!Arrived(a)) return;
                Face(a, _host.CentreOf(_host.Building(a.Target)!));
                a.Timer -= dt;
                if (a.Timer > 0) return;
                a.Timer = kind == RaiderKind.Thief ? 2f : 1.6f;
                a.Motion?.Play("Punch", 1f);
                IslandAudio.Play("hit", a.Body.position, 0.55f, 0.15f, 0.15f, 0.8f);
                if (kind == RaiderKind.Thief) { Steal(a, now); if (a.Thefts >= 6) Flee(a); }
                else Wreck(a, now, kind == RaiderKind.Wrecker ? 2f : 1f);
                return;
            }
            a.Repath -= dt;
            if (a.Repath > 0) return;
            a.Repath = 2f;
            var target = PickTarget(a, now, kind);
            if (target < 0) { Flee(a); return; }
            a.Job = Job.Raid;
            a.Target = target;
            a.Timer = 1f;
            var b = _host.Building(target)!;
            GoTo(a, eco.Plots.ContainsKey(target) ? _host.CentreOf(b) + Jitter(1.5f) : _host.DoorOf(target) + Jitter(1.2f));
        }

        /// <summary>Wreckers go for the nearest standing building; thieves for whatever has the most to take.</summary>
        private int PickTarget(Agent a, double now, RaiderKind kind)
        {
            var eco = Eco;
            var host = _host!;
            var best = -1;
            var bestScore = float.MinValue;
            foreach (var uid in host.Buildings)
            {
                var b = host.Building(uid);
                if (b == null || !eco.IsBuilt(uid) || eco.IsRuined(uid)) continue;
                float worth;
                if (kind == RaiderKind.Thief)
                {
                    if (eco.Departments.TryGetValue(uid, out var d)) worth = d.Levels.Kind is DepartmentKind.Farmhouse or DepartmentKind.Silo ? d.Stored * 2f : 0f;
                    else if (eco.Animals.TryGetValue(uid, out var house)) worth = (house.StockAt(now) + (house.Side?.StockAt(now) ?? 0)) * 6f;
                    else if (eco.Machines.TryGetValue(uid, out var m)) worth = m.Output * 8f;
                    else worth = 0f;
                    if (worth <= 0) continue;
                }
                else worth = 10f;
                var score = worth - Flat(host.CentreOf(b) - a.Body.position).magnitude * 0.6f;
                if (score > bestScore) { bestScore = score; best = uid; }
            }
            return best;
        }

        private void Wreck(Agent a, double now, float factor)
        {
            var eco = Eco;
            var host = _host!;
            var b = host.Building(a.Target)!;
            Magic.Burst(host.CentreOf(b) + Vector3.up * 1.2f, new Color(0.55f, 0.45f, 0.35f), 8, 1f);
            if (eco.Damage(a.Target, a.Damage * factor, now))
            {
                _ruined++;
                var lost = eco.LastRuinLosses.Count > 0 ? " — lost " + string.Join(", ", eco.LastRuinLosses.Take(3)) : "";
                host.Say($"The {Pretty(b.id)} was ruined{lost}!");
                IslandAudio.Play("fail", null, 0.8f, 0f, 0.5f);
                a.Job = Job.None;
                a.Repath = 0f;
                if (Random.value < 0.5f) Flee(a);   // one wrecked building can be enough
            }
            else if (Random.value < 0.15f) host.Say($"Zombies are smashing the {Pretty(b.id)}!");
        }

        private void Steal(Agent a, double now)
        {
            var eco = Eco;
            var host = _host!;
            var b = host.Building(a.Target)!;
            var took = new List<string>();
            if (eco.Departments.TryGetValue(a.Target, out var d))
            {
                eco.Steal(a.Target, 2, took);
                // the Clerk comes back for the coins every time
                if (d.Levels.Kind == DepartmentKind.Farmhouse && (a.Thefts == 0 || a.Boss == "clerk"))
                {
                    var coins = eco.StealCoins();
                    _coinsTaken += coins;
                    if (coins > 0) { host.Say(a.Boss == "clerk" ? $"The Clerk took {coins} coins! (a bank keeps coins safe)" : $"Zombies took {coins} coins! (a bank keeps coins safe)"); IslandAudio.Play("coin", null, 0.6f, 0f, 0.5f, 0.7f); }
                }
            }
            else if (eco.Machines.ContainsKey(a.Target)) eco.TakeOutput(a.Target, now, 2, took, credit: false);
            else eco.TakeProduce(a.Target, now, 2, took, credit: false);
            foreach (var item in took) host.FlyGood(item, host.CentreOf(b) + Vector3.up * 1.5f, a.Body.position + Vector3.up * 1.6f);
            a.Load.AddRange(took);
            a.Thefts++;
            if (took.Count > 0) host.Say(a.Boss.Length > 0 ? $"{Cap(GameplayRules.BossName(a.Boss))} is stealing from the {Pretty(b.id)}!" : $"Zombies are stealing from the {Pretty(b.id)}!");
            else { a.Job = Job.None; a.Repath = 0f; }
        }

        /// <summary>A raider's blow knocks the farmer down: a quarter of the basket spills and is lost.</summary>
        private void KnockFarmer(PlayerAvatar player)
        {
            var basket = Eco.Basket;
            var lose = (basket.Count + 3) / 4;
            for (var i = 0; i < lose; i++) basket.TakeOne();
            if (player.TryGetComponent<CharacterMotion>(out var m)) m.Play("Hit", 0.6f);
            IslandAudio.Play("hit", player.transform.position, 0.9f, 0.05f, 0.3f, 1.2f);
            _host!.Say(lose > 0 ? $"A zombie knocked you down — {lose} goods spilled!" : "A zombie knocked you down!");
        }

        private void Flee(Agent a)
        {
            if (a.Job == Job.Flee) return;
            a.Job = Job.Flee;
            a.Foe = null;
            a.Speed *= 1.6f;
            GoTo(a, a.Shore);
        }

        // ---- loot ---------------------------------------------------------------------------------

        private void DropLoot(Vector3 at, List<string> items, string? say = "Raider beaten — pick up what they dropped")
        {
            var pile = new GameObject("Loot");
            pile.transform.position = new Vector3(at.x, GroundHeight.At(at.x, at.z, at.y), at.z);
            for (var i = 0; i < Mathf.Min(items.Count, 6); i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(cube.GetComponent<Collider>());
                cube.transform.SetParent(pile.transform, false);
                cube.transform.localScale = Vector3.one * 0.32f;
                cube.transform.localPosition = new Vector3((i % 3 - 1) * 0.34f, 0.16f + (i / 3) * 0.33f, 0);
                cube.GetComponent<Renderer>().material.color = IslandEconomyHost.GoodColour(items[i]);
            }
            var loot = new Loot { Pile = pile };
            loot.Items.AddRange(items);
            _loot.Add(loot);
            Magic.Appear(pile, 0.3f);
            if (say != null) _host!.Say(say);
        }

        private void PickUpLoot()
        {
            var player = _host!.Player;
            if (player == null || player.Riding) return;
            foreach (var l in _loot.ToList())
            {
                if (l.Pile == null) { _loot.Remove(l); continue; }
                if (Flat(l.Pile.transform.position - player.transform.position).magnitude > 1.8f) continue;
                var left = new List<string>();
                foreach (var item in l.Items)
                {
                    if (Eco.Basket.Add(item, 1) == 1)
                    {
                        _host.FlyGood(item, l.Pile.transform.position + Vector3.up * 0.4f, player.transform.position + Vector3.up * 1.8f);
                        IslandAudio.Play("loot", null, 0.7f, 0.02f, 0.06f);
                    }
                    else left.Add(item);
                }
                l.Items.Clear();
                l.Items.AddRange(left);
                if (left.Count == 0) { Magic.Vanish(l.Pile); _loot.Remove(l); }
                else _host.Say("Basket full — store some at the farmhouse");
            }
        }

        // ---- walking ------------------------------------------------------------------------------

        private void GoTo(Agent a, Vector3 to)
        {
            // people walk through buildings and trees like the farmer does; routes only keep them out of the sea
            GridPath.Find(_host!.Grid!, a.Body.position, to, owner => true, _path);
            a.Route.Clear();
            a.Route.AddRange(_path);
            a.Next = 0;
        }

        private static bool Arrived(Agent a) => a.Route.Count == 0;

        private void Walk(Agent a, float dt)
        {
            if (a.Route.Count == 0 || a.Job == Job.Down) return;
            var pos = a.Body.position;
            var target = a.Route[a.Next];
            var flat = new Vector3(target.x - pos.x, 0, target.z - pos.z);
            // Worker Rush: the island's own people go twice as fast
            var step = a.Speed * dt * (a.Role != Role.Raider && IslandBoosts.Running("rush") ? 2f : 1f);
            if (flat.magnitude <= Mathf.Max(0.12f, step))
            {
                pos.x = target.x;
                pos.z = target.z;
                a.Next++;
                if (a.Next >= a.Route.Count) a.Route.Clear();
            }
            else pos += flat.normalized * step;
            pos.y = GroundHeight.At(pos.x, pos.z, pos.y) - a.Foot;
            a.Body.position = pos;
            // a warrior squaring up to a zombie keeps facing it (stepping sideways or back); everyone else faces the way they go
            var look = a.Role == Role.Warrior && a.Foe != null && !a.Foe.Gone ? Flat(a.Foe.Body.position - pos) : flat;
            if (look.sqrMagnitude > 0.0004f)
                a.Body.rotation = Quaternion.Slerp(a.Body.rotation, Quaternion.LookRotation(look.normalized, Vector3.up), 1f - Mathf.Exp(-10f * dt));
        }

        private static void Face(Agent a, Vector3 at)
        {
            var d = Flat(at - a.Body.position);
            if (d.sqrMagnitude > 0.01f) a.Body.rotation = Quaternion.Slerp(a.Body.rotation, Quaternion.LookRotation(d.normalized, Vector3.up), 0.2f);
        }

        private void Vanish(Agent a)
        {
            Unclaim(a);
            a.Gone = true;
            if (a.Body != null) Magic.Vanish(a.Body.gameObject, true, a.Role == Role.Raider ? new Color(1f, 0.45f, 0.45f) : (Color?)null);
        }

        private void Unclaim(Agent a)
        {
            foreach (var k in _claims.Where(kv => kv.Value == a).Select(kv => kv.Key).ToList()) _claims.Remove(k);
        }

        private bool Valid(int uid) => uid >= 0 && _host!.Building(uid) != null;

        /// <summary>Little crates on a worker's or raider's back, coloured by what they carry.</summary>
        /// <summary>
        /// What they carry, as the real goods: one or two in their arms (the carry walk), a bigger load piled in a
        /// wheelbarrow they push (workers) or a hand cart they pull (builders). Falls back to plain crates.
        /// </summary>
        private void ShowLoad(Agent a)
        {
            var key = string.Join(",", a.Load) + (a.Job == Job.Supply ? "@" + a.Target2 : "");
            if (key == a.CratesShown) return;
            a.CratesShown = key;
            if (a.Crates != null) Destroy(a.Crates.gameObject);
            a.Crates = null;
            if (a.Motion != null) a.Motion.Carrying = a.Load.Count > 0;
            var items = ItemCatalog.Load();
            // building supplies go in a hand cart, the harvest in a wheelbarrow
            var forSite = a.Role == Role.Builder || (a.Job == Job.Supply && Eco.Sites.Any(x => x.Uid == a.Target2));
            var big = items != null && (forSite ? a.Load.Count >= 2 && items.carts.Count > 0 : a.Load.Count >= 3 && items.barrows.Count > 0);
            // the barrow or cart comes out with a load and goes away when it's empty
            var push = big ? (forSite ? "cart" : "barrow") : "";
            if (push != a.PushShown)
            {
                if (a.Pushing != null) Destroy(a.Pushing);
                a.Pushing = null;
                a.PushShown = push;
                if (push != "" && items != null)
                {
                    var list = push == "cart" ? items.carts : items.barrows;
                    a.Pushing = HandTool.Push(a.Body, list[(a.Home * 7 + a.Kind.Length + (int)(a.Speed * 100f)) % list.Count], push == "cart", push == "cart" ? 0.45f : 0.35f, push == "cart" ? 0f : HandTool.BarrowLift(a.Body));
                }
            }
            if (a.Load.Count == 0) return;
            var s = a.Body.lossyScale.y > 0.01f ? 1f / a.Body.lossyScale.y : 1f;
            var height = 2f;
            foreach (var r in a.Body.GetComponentsInChildren<Renderer>()) { height = Mathf.Max(0.5f, r.bounds.max.y - a.Body.position.y); break; }
            var pile = new GameObject("Load").transform;
            a.Crates = pile;
            if (a.Pushing != null)
            {
                // piled in the barrow's tub (or the cart's bed), a little above its rim
                var b = new Bounds(a.Pushing.transform.position, Vector3.zero);
                foreach (var r in a.Pushing.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                pile.SetParent(a.Pushing.transform, true);
                pile.position = new Vector3(b.center.x, b.max.y - b.size.y * 0.25f, b.center.z) + a.Body.forward * (a.PushShown == "cart" ? 0f : 0.15f);
                pile.rotation = a.Body.rotation;
            }
            else
            {
                // held against the chest in both arms
                pile.SetParent(a.Body, false);
                pile.localPosition = new Vector3(0, height * 0.42f * s, 0.32f * s);
            }
            var n = a.Load.Count;
            for (var i = 0; i < n; i++)
            {
                var prefab = items?.Good(a.Load[i]);
                GameObject go;
                if (prefab != null) go = Instantiate(prefab, pile);
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(pile, false);
                    go.transform.localScale = Vector3.one * 0.22f;
                    go.GetComponent<Renderer>().material.color = IslandEconomyHost.GoodColour(a.Load[i]);
                }
                foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
                go.transform.localScale *= a.Pushing != null ? 1f : 0.85f;
                // a little heap: two side by side, then the next layer
                var col = i % 2; var row = i / 2;
                go.transform.localPosition = new Vector3((col - 0.5f) * 0.26f * (n > 1 ? 1f : 0f), row * 0.2f, (row % 2) * 0.06f);
                go.transform.localRotation = Quaternion.Euler(0, (i * 47) % 360, 0);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // ---- tools in hand ----------------------------------------------------------------------------

        private static readonly string[] BuilderTools = { "Saw", "Shovel", "Crowbar", "Ladder", "Wrench", "PipeWrench", "Pickaxe", "Hoe" };

        /// <summary>The tool a job calls for (empty: hands free).</summary>
        private string WantTool(Agent a)
        {
            if (a.Load.Count > 0 && a.Role != Role.Warrior) return "";
            var eco = Eco;
            switch (a.Role)
            {
                case Role.Worker:
                    switch (a.Job)
                    {
                        case Job.Chop:
                        {
                            var prop = _host!.Nature?.Find(a.Target2);
                            return prop == null ? "Axe" : prop.kind == "rock" ? "Pickaxe" : prop.kind == "bush" ? "GardenShears" : "Axe";
                        }
                        case Job.Plant: return "Hoe";
                        case Job.Harvest:
                        {
                            var crop = eco.Plots.TryGetValue(a.Target, out var plot) ? plot.Crop ?? "" : "";
                            return crop is "wheat" or "maize" ? "Rake" : crop is "pumpkin" or "beans" ? "Shovel" : "GardenShears";
                        }
                        case Job.Collect: return eco.Machines.ContainsKey(a.Target) ? "Wrench" : "Bucket";
                        default: return "";
                    }
                case Role.Builder:
                    if (a.Job != Job.Build) return "";
                    var repair = eco.Sites.Any(x => x.Uid == a.Target && x.Repair);
                    if (repair) return a.Home % 2 == 0 ? "Wrench" : "PipeWrench";
                    return BuilderTools[(int)(a.ToolClock / 7f + a.Home) % BuilderTools.Length];
                case Role.Villager:
                    return a.Kind.Contains("baker") ? "FryingPan" : "";
                default:
                    return "";
            }
        }

        /// <summary>The swing that goes with a tool.</summary>
        private static string WorkAnim(string tool) => tool switch
        {
            "Axe" or "Pickaxe" or "Hoe" or "Shovel" => "Chop",
            "Rake" => "Dig",
            "GardenShears" or "Saw" => "Swing",
            "Bucket" => "Lift",
            "Wrench" or "PipeWrench" or "Crowbar" => "Machine",
            "Ladder" => "Climb",   // up the scaffold (no tool in hand)
            _ => "Lift",
        };

        /// <summary>Keep the right tool in their hand (and the mower in front of a townsperson out mowing).</summary>
        private void Gear(Agent a, float dt)
        {
            if (a.Motion != null)
            {
                var wet = GroundHeight.At(a.Body.position.x, a.Body.position.z, a.Body.position.y) < -0.35f;
                a.Motion.WalkAs = wet ? "Swim"
                    : a.Role == Role.Raider && a.Hp < a.MaxHp * 0.3f ? "Crawl"
                    : a.Job is Job.Hide or Job.Flee ? "Flee"
                    : a.Load.Count > 0 || a.Pushing != null ? null      // carrying or pushing: the carry walk
                    : a.Job is Job.Fetch or Job.Feed ? "Hurry"          // off to fetch something, briskly
                    : null;
            }
            if (a.Role == Role.Raider || a.Role == Role.Warrior) return;   // zombies' and warriors' weapons are set when they arrive
            a.ToolClock += dt;
            var want = a.Job is Job.Down or Job.Leave or Job.Hide or Job.Flee ? "" : WantTool(a);
            if (want != a.ToolShown)
            {
                var swap = a.Started && want != "" && a.ToolShown != "";
                if (a.Tool != null) Destroy(a.Tool);
                a.Tool = null;
                a.ToolShown = want;
                var prefab = want != "" ? ItemCatalog.Load()?.Tool(want) : null;
                if (prefab != null) a.Tool = HandTool.Hold(a.Body.gameObject, prefab);
                // a builder changing tools at the site changes how they work
                if (swap && a.Role == Role.Builder) { a.Motion?.Release(); a.Motion?.Hold(WorkAnim(want)); }
            }
            if (a.Role != Role.Villager) return;
            var mow = a.Mowing && a.Job == Job.Wander && a.Route.Count > 0 ? "mower" : "";
            if (mow == a.PushShown) return;
            if (a.Pushing != null) Destroy(a.Pushing);
            a.Pushing = null;
            a.PushShown = mow;
            var items = ItemCatalog.Load();
            if (mow != "" && items != null && items.mowers.Count > 0)
                a.Pushing = HandTool.Push(a.Body, items.mowers[(a.Home * 3 + (int)(a.Speed * 100f)) % items.mowers.Count], false, 0.3f);
            if (a.Motion != null) a.Motion.Carrying = a.Pushing != null || a.Load.Count > 0;
        }

        private Material RingMaterial()
        {
            if (_ringMaterial != null) return _ringMaterial;
            _ringMaterial = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit")) { renderQueue = 3000 };
            return _ringMaterial;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
        private static Vector3 Jitter(float r) { var c = Random.insideUnitCircle * r; return new Vector3(c.x, 0, c.y); }

        private static string Compass(Vector3 d)
        {
            var a = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            var names = new[] { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return names[(Mathf.RoundToInt(a / 45f) % 8 + 8) % 8];
        }

        private static string Pretty(string id) => id switch
        {
            "farmhouse" => "farmhouse",
            "army_house" => "army house",
            "workers_house" => "workers' house",
            "builders_hut" => "builders' hut",
            _ => id.Replace('_', ' '),
        };

        // ---- screen: alert, arrows, health bars ---------------------------------------------------


    }
}
