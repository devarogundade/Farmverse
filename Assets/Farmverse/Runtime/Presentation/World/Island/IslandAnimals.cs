#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// The island's animals, out and about. Every working animal house keeps its animals around it:
    /// chickens round the coop, cows by the barn, sheep by the shed, pigs by the sty, and the horse by
    /// the stable while nobody's riding it. They amble to a spot near home, stop to peck or graze,
    /// amble on; now and then one goes inside and comes back out, and when the house has made
    /// something (an egg, milk, wool) one goes in to lay or be milked. Hungry animals hang about the
    /// door waiting to be fed. Raiders close by send them running. A ruined house's animals are gone.
    /// The horse you get off walks itself home to its stable.
    /// </summary>
    public sealed class IslandAnimals : MonoBehaviour
    {
        [SerializeField] private GameObject? chickenPrefab;
        [SerializeField] private GameObject? cowPrefab;
        [SerializeField] private GameObject? sheepPrefab;
        [SerializeField] private GameObject? pigPrefab;
        [SerializeField] private GameObject? horsePrefab;
        [SerializeField, Tooltip("Every animal prefab (Content/Art/Animals), by name: their variants and the farm's dogs, cats, ducks and pigeons.")] private GameObject[] animalPrefabs = new GameObject[0];
        [SerializeField, Tooltip("Ponds the ducks swim on: x, water height, z, radius.")] private Vector4[] ponds = new Vector4[0];
        [SerializeField] private float chickenScale = 1.05f;
        [SerializeField] private float cowScale = 1.05f;
        [SerializeField] private float sheepScale = 1.5f;
        [SerializeField] private float pigScale = 1.5f;
        [SerializeField, Tooltip("Turn (degrees) that makes the cow model face where it walks.")] private float cowYaw = 90f;
        [SerializeField, Tooltip("Turn (degrees) that makes the chicken model face where it walks.")] private float chickenYaw = 180f;

        private enum State { Wander, Graze, GoingIn, Inside, Home, Flee, Gone }

        private sealed class Kind
        {
            public string Name = "";
            public int Count;
            public float Roam;      // how far from the door they wander (m)
            public float Speed;
            public bool Hops;       // chickens hop as they go
            public string? Call;    // a sound now and then
            public string[] Names = new string[0];   // prefab variants (Content/Art/Animals)
            public string? Follow;  // chicks keep close to a hen
            public bool Flies;      // pigeons take off when someone comes close
            public bool Swims;      // ducks on the pond
            public bool Lies;       // lies down for a rest now and then (cows, pigs, dogs, cats)
        }

        private sealed class Beast
        {
            public int Home;
            public Kind Kind = null!;
            public Transform Root = null!;
            public Transform Model = null!;
            public CharacterMotion? Motion;
            public Quaternion Facing;
            public float Yaw;
            public float Scale = 1f;
            public State State;
            public Vector3 Goal;
            public float Wait;
            public float Seed;
            public float Grow;      // 0 → 1 popping out of the door, 1 → 0 going in
            public bool Moving;
            public float Speed;
            public float Foot;      // lowest point of the full-size model relative to the root
            public float Heading;   // each animal's own favourite side of the house, drifting over time
            public float Range;     // and how far it likes to go (some homebodies, some wanderers)
            public string Act = ""; // what it does while standing (Eat, Lying, Gesture…)
            public float Air;       // a pigeon's height above the ground
            public bool Flying;
            public Beast? Leader;   // the hen a chick follows
            public Vector4 Pond;    // a duck's pond
        }

        private static Kind K(string name, int count, float roam, float speed, params string[] names) => new Kind { Name = name, Count = count, Roam = roam, Speed = speed, Names = names };

        /// <summary>Who lives at each building (every animal in the Farm pack has a home).</summary>
        private static readonly Dictionary<string, Kind[]> Homes = new Dictionary<string, Kind[]>
        {
            ["chicken_coop"] = new[]
            {
                new Kind { Name = "chicken", Count = 4, Roam = 5f, Speed = 0.9f, Hops = true, Call = "chicken", Names = new[] { "chicken", "chicken_2", "chicken_3" } },
                new Kind { Name = "chick", Count = 3, Roam = 4f, Speed = 1.1f, Hops = true, Follow = "chicken", Names = new[] { "chick" } },
            },
            ["barn"] = new[]
            {
                new Kind { Name = "cow", Count = 2, Roam = 8f, Speed = 0.65f, Lies = true, Names = new[] { "cow", "cow_2" } },
                new Kind { Name = "sheep", Count = 2, Roam = 7f, Speed = 0.7f, Names = new[] { "sheep", "sheep_2", "sheep_3" } },
                new Kind { Name = "goat", Count = 1, Roam = 7f, Speed = 0.8f, Names = new[] { "goat", "goat_2" } },
            },
            ["pigsty"] = new[] { new Kind { Name = "pig", Count = 3, Roam = 5f, Speed = 0.8f, Lies = true, Names = new[] { "pig", "pig_2", "pig_3" } } },
            ["stable"] = new[]
            {
                new Kind { Name = "horse", Count = 1, Roam = 9f, Speed = 1.3f, Call = "neigh", Names = new[] { "horse" } },
                new Kind { Name = "donkey", Count = 1, Roam = 8f, Speed = 1f, Names = new[] { "donkey", "donkey_2" } },
                new Kind { Name = "pony", Count = 1, Roam = 8f, Speed = 1.1f, Names = new[] { "horse_2", "horse_3", "horse_4", "horse_5" } },
            },
            ["farmhouse"] = new[]
            {
                new Kind { Name = "dog", Count = 1, Roam = 12f, Speed = 1.4f, Lies = true, Names = new[] { "dog", "dog_2", "dog_3", "dog_4", "dog_5", "dog_6" } },
                new Kind { Name = "cat", Count = 1, Roam = 9f, Speed = 1.1f, Lies = true, Names = new[] { "cat", "cat_2", "cat_3", "cat_4" } },
                new Kind { Name = "kitty", Count = 2, Roam = 6f, Speed = 1f, Lies = true, Names = new[] { "kitty", "kitty_2", "kitty_3", "kitty_4" } },
            },
            ["workers_house"] = new[] { new Kind { Name = "dog", Count = 1, Roam = 10f, Speed = 1.4f, Lies = true, Names = new[] { "dog_2", "dog_4", "dog_6" } } },
            ["market_stall"] = new[] { new Kind { Name = "pigeon", Count = 3, Roam = 6f, Speed = 0.9f, Flies = true, Names = new[] { "pigeon", "pigeon_2", "pigeon_3" } } },
            ["town_hall"] = new[] { new Kind { Name = "pigeon", Count = 4, Roam = 8f, Speed = 0.9f, Flies = true, Names = new[] { "pigeon", "pigeon_2", "pigeon_3" } } },
            ["community_hall"] = new[] { new Kind { Name = "pigeon", Count = 3, Roam = 7f, Speed = 0.9f, Flies = true, Names = new[] { "pigeon", "pigeon_2", "pigeon_3" } } },
            ["bakery_shop"] = new[] { new Kind { Name = "pigeon", Count = 2, Roam = 5f, Speed = 0.9f, Flies = true, Names = new[] { "pigeon_2", "pigeon_3" } } },
        };

        private static readonly Kind Duck = new Kind { Name = "duck", Count = 4, Roam = 5f, Speed = 0.6f, Swims = true, Call = null, Names = new[] { "duck" } };
        private static Kind StableKind => Homes["stable"][0];

        private IslandEconomyHost? _host;
        private readonly List<Beast> _beasts = new List<Beast>();

        /// <summary>The animals, shown right now (hidden while visiting another island).</summary>
        public IEnumerable<GameObject> ShownBodies
        {
            get
            {
                foreach (var b in _beasts)
                    if (b.Root != null && b.Root.gameObject.activeSelf) yield return b.Root.gameObject;
            }
        }
        private readonly Dictionary<int, int> _madeSeen = new Dictionary<int, int>();
        private readonly Dictionary<int, float> _nextCall = new Dictionary<int, float>();
        private float _nextSync;

        /// <summary>Whether the farmer is on the horse (then the stable's horse is with them, not at home).</summary>
        public System.Func<bool>? Riding { get; set; }

        public void Begin(IslandEconomyHost host) => _host = host;

        /// <summary>Everyone back in (a fresh island).</summary>
        public void Clear()
        {
            foreach (var b in _beasts) if (b.Root != null) Destroy(b.Root.gameObject);
            _beasts.Clear();
            _madeSeen.Clear();
        }

        /// <summary>The horse came to the farmer: the one by the stable goes (it's the one being ridden).</summary>
        public void HorseTaken()
        {
            foreach (var b in _beasts.Where(b => b.Kind.Name == "horse" && b.State != State.Gone).ToList())
            {
                b.State = State.Gone;
                Magic.Vanish(b.Root.gameObject, true, new Color(1f, 0.9f, 0.6f));
            }
        }

        /// <summary>The farmer got off: this horse walks itself home to the nearest stable. False if there's no stable to go to.</summary>
        public bool AdoptHorse(Transform horse)
        {
            if (_host == null) return false;
            var eco = _host.Economy;
            var at = horse.position;
            var stable = _host.Buildings.Where(u => _host.Building(u)?.id == "stable" && eco.Working(u))
                .OrderBy(u => Flat(_host.DoorOf(u) - at).sqrMagnitude).DefaultIfEmpty(-1).First();
            if (stable < 0) return false;
            horse.SetParent(transform, true);
            var b = new Beast
            {
                Home = stable, Kind = StableKind, Root = horse, Model = horse, Motion = horse.GetComponent<CharacterMotion>(),
                Facing = horse.rotation, Seed = Random.value * 10f, Grow = 1f, State = State.Home, Scale = 1f,
            };
            b.Speed = b.Kind.Speed * 1.4f;
            if (b.Motion != null) b.Motion.RunFrom = b.Kind.Speed * 1.9f;
            b.Foot = GroundHeight.FootOffset(horse.gameObject);
            b.Goal = Around(stable, b.Kind.Roam * 0.5f);
            _beasts.Add(b);
            return true;
        }

        private void Update()
        {
            if (_host == null || _host.Grid == null) return;
            if (Time.time >= _nextSync)
            {
                _nextSync = Time.time + 1f;
                Sync();
            }
            var dt = Time.deltaTime;
            var raiders = _host.Life != null ? _host.Life.RaiderPositions.ToList() : new List<Vector3>();
            foreach (var b in _beasts)
            {
                if (b.Root == null || b.State == State.Gone) continue;
                Think(b, dt, raiders);
                Move(b, dt);
            }
            _beasts.RemoveAll(b => b.Root == null || b.State == State.Gone);
            Calls();
        }

        // ---- who should be out ---------------------------------------------------------------

        private void Sync()
        {
            var host = _host!;
            var eco = host.Economy;
            var now = IslandEconomyHost.Clock;
            var riding = Riding?.Invoke() ?? false;
            var homes = new HashSet<int>();
            foreach (var uid in host.Buildings)
            {
                var pb = host.Building(uid);
                if (pb == null || !Homes.TryGetValue(pb.id, out var kinds) || !eco.Working(uid)) continue;
                homes.Add(uid);
                foreach (var kind in kinds)
                {
                    var want = kind == StableKind ? (riding ? 0 : 1) : kind.Count;
                    var have = _beasts.Where(b => b.Home == uid && b.Kind == kind && b.State != State.Gone).ToList();
                    for (var i = have.Count; i < want; i++) Spawn(uid, kind);
                    if (have.Count > want && kind != StableKind)
                        foreach (var b in have.Skip(want)) Leave(b);
                }
                // something new was made: one of them goes in to lay it (or be milked, or sheared)
                if (eco.Animals.TryGetValue(uid, out var house))
                {
                    var made = house.StockAt(now);
                    if (_madeSeen.TryGetValue(uid, out var seen) && made > seen)
                    {
                        var who = _beasts.Where(b => b.Home == uid && b.Kind == kinds[0] && b.State is State.Wander or State.Graze).OrderBy(_ => Random.value).FirstOrDefault();
                        if (who != null) GoIn(who);
                    }
                    _madeSeen[uid] = made;
                }
            }
            // ducks on the ponds (they belong to no house)
            for (var p = 0; p < ponds.Length; p++)
            {
                if (_beasts.Count(b => b.Kind == Duck && b.Home == -100 - p) < Duck.Count) Spawn(-100 - p, Duck);
                homes.Add(-100 - p);
            }
            // a sold or ruined house: its animals run off
            foreach (var b in _beasts.Where(b => !homes.Contains(b.Home) && b.State != State.Gone && b.State != State.Flee).ToList())
            {
                b.State = State.Flee;
                b.Goal = b.Root.position + Flat(b.Root.position - host.DoorOf(b.Home)).normalized * 14f;
                b.Wait = 5f;
            }
        }

        private void Spawn(int home, Kind kind)
        {
            var pick = kind.Names.Length > 0 ? kind.Names[Random.Range(0, kind.Names.Length)] : kind.Name;
            var prefab = animalPrefabs.FirstOrDefault(x => x != null && x.name == pick) ?? kind.Name switch
            {
                "chicken" => chickenPrefab, "cow" => cowPrefab, "sheep" => sheepPrefab, "pig" => pigPrefab, "horse" => horsePrefab, _ => null,
            };
            if (prefab == null) return;
            var pond = home <= -100 && -100 - home < ponds.Length ? ponds[-100 - home] : Vector4.zero;
            var door = home <= -100 ? new Vector3(pond.x, pond.y, pond.z) : _host!.DoorOf(home);
            var at = door + Jitter(home <= -100 ? pond.w * 0.5f : 1.2f);
            at.y = home <= -100 ? pond.y : GroundHeight.At(at.x, at.z, door.y);
            var root = new GameObject(kind.Name).transform;
            root.SetParent(transform, false);
            root.position = at;
            var model = Instantiate(prefab, root);
            model.name = "Model";
            // the old world's pen behaviour isn't wanted here: this class moves them
            foreach (var c in model.GetComponentsInChildren<MonoBehaviour>())
                if (c is Wander || c is StagedVisual) Destroy(c);
            foreach (var c in model.GetComponentsInChildren<Collider>()) c.enabled = false;
            var scale = kind.Name switch { "chicken" => chickenScale, "cow" => cowScale, "sheep" => sheepScale, "pig" => pigScale, _ => 1f };
            var b = new Beast
            {
                Home = home, Kind = kind, Root = root, Model = model.transform, Motion = model.GetComponent<CharacterMotion>(),
                Yaw = kind.Name switch { "cow" => cowYaw, "chicken" => chickenYaw, _ => 0f },
                Scale = scale, Seed = Random.value * 10f, Grow = 0f, State = State.Wander, Wait = Random.Range(0.5f, 3f),
                Facing = Quaternion.Euler(0, Random.Range(0f, 360f), 0),
            };
            b.Speed = kind.Speed * Random.Range(0.85f, 1.15f);
            if (b.Motion != null) b.Motion.RunFrom = kind.Speed * 1.9f;   // its own gallop: past a brisk walk for its size
            b.Pond = pond;
            if (kind.Follow != null) b.Leader = _beasts.Where(x => x.Home == home && x.Kind.Name == kind.Follow && x.State != State.Gone).OrderBy(_ => Random.value).FirstOrDefault();
            if (b.Motion != null && kind.Swims) { b.Motion.WalkAs = "Swim"; b.Motion.IdleAs = "Swim_Idle"; }
            // stand it on its feet: measure the model at full size, then start it tiny and grow it in
            b.Model.localScale = Vector3.one * scale;
            b.Model.localRotation = Quaternion.Euler(0, b.Yaw, 0);
            b.Foot = GroundHeight.FootOffset(model) + (model.transform.position.y - root.position.y);
            b.Heading = Random.Range(0f, Mathf.PI * 2f);
            b.Range = Random.Range(0.45f, 1.25f);
            b.Model.localScale = Vector3.one * 0.01f;
            _beasts.Add(b);
        }

        private void Leave(Beast b)
        {
            GoIn(b);
            b.Wait = float.MaxValue;   // and stays in
        }

        private void GoIn(Beast b)
        {
            b.State = State.GoingIn;
            b.Goal = _host!.DoorOf(b.Home) + Jitter(0.4f);
        }

        // ---- behaviour ---------------------------------------------------------------------------

        private void Think(Beast b, float dt, List<Vector3> raiders)
        {
            var host = _host!;
            var eco = host.Economy;
            if (b.Kind.Swims) { Paddle(b, dt); return; }
            if (b.Kind.Flies && Pigeon(b, dt)) return;
            var door = host.DoorOf(b.Home);
            // raiders close by: run from them (unless already tucked inside)
            if (b.State is State.Wander or State.Graze or State.Home && raiders.Count > 0)
            {
                var near = raiders.OrderBy(r => Flat(r - b.Root.position).sqrMagnitude).First();
                if (Flat(near - b.Root.position).magnitude < 7f)
                {
                    var away = Flat(b.Root.position - near).normalized;
                    b.Goal = b.Root.position + away * 6f + Jitter(1.5f);
                    b.Wait = 0f;
                    b.State = State.Wander;
                    b.Speed = b.Kind.Speed * 3f;
                    if (b.Kind.Call != null && Random.value < 0.02f) IslandAudio.Play(b.Kind.Call, b.Root.position, 0.6f, 0.15f, 0.8f, 1.2f);
                    return;
                }
            }
            switch (b.State)
            {
                case State.Flee:
                    b.Wait -= dt;
                    if (b.Wait <= 0f || Arrived(b)) { b.State = State.Gone; Magic.Vanish(b.Root.gameObject); }
                    return;
                case State.GoingIn:
                    if (!Arrived(b)) return;
                    b.Grow = Mathf.MoveTowards(b.Grow, 0f, dt * 3f);
                    if (b.Grow > 0f) return;
                    if (b.Wait == float.MaxValue) { b.State = State.Gone; Destroy(b.Root.gameObject); return; }   // one too many: stays in
                    b.State = State.Inside;
                    if (b.Wait < float.MaxValue) b.Wait = Random.Range(4f, 12f);
                    return;
                case State.Inside:
                    b.Wait -= dt;
                    if (b.Wait > 0f) return;
                    b.Root.position = Ground(door + Jitter(0.6f));
                    b.State = State.Wander;
                    b.Wait = 0f;
                    return;
                case State.Home:
                    // the horse walking back to its stable after a ride
                    if (!Arrived(b)) return;
                    b.State = State.Graze;
                    b.Speed = b.Kind.Speed;
                    b.Wait = Random.Range(3f, 6f);
                    return;
            }
            if (b.Grow < 1f) b.Grow = Mathf.MoveTowards(b.Grow, 1f, dt * 2.5f);
            // hungry animals wait by the door to be fed
            var hungry = eco.Animals.TryGetValue(b.Home, out var house) && house.Hungry(IslandEconomyHost.Clock)
                      || eco.Departments.TryGetValue(b.Home, out var stable) && stable.Starving;
            if (b.State == State.Wander && !Arrived(b)) return;
            if (b.State == State.Wander)
            {
                b.State = State.Graze;
                PickAct(b);
                b.Speed = b.Kind.Speed * Random.Range(0.85f, 1.15f);
                b.Wait = hungry ? Random.Range(3f, 8f) : Random.Range(1f, 9f) * (Random.value < 0.2f ? 2f : 1f);
                return;
            }
            b.Wait -= dt;
            if (b.Wait > 0f) return;
            // off again: mostly somewhere nearby, sometimes a peek inside
            if (!hungry && Random.value < 0.08f && b.Kind.Name != "horse") { GoIn(b); return; }
            b.State = State.Wander;
            // each keeps to its own patch, which drifts, so they spread out instead of bunching up
            b.Heading += Random.Range(-1.1f, 1.1f);
            if (Random.value < 0.15f) b.Range = Random.Range(0.45f, 1.25f);
            b.Goal = hungry ? Spot(b, door, 3.5f) : Spot(b, door, b.Kind.Roam);
            if (b.Kind.Follow != null)
            {
                if (b.Leader == null || b.Leader.State == State.Gone || b.Leader.Root == null)
                    b.Leader = _beasts.Where(x => x.Home == b.Home && x.Kind.Name == b.Kind.Follow && x.State != State.Gone).OrderBy(_ => Random.value).FirstOrDefault();
                if (b.Leader != null) { b.Goal = Ground(b.Leader.Root.position + Jitter(0.9f)); b.Wait = 0f; }
            }
            // chickens now and then make a dash for it
            if (b.Kind.Hops && Random.value < 0.25f) b.Speed = b.Kind.Speed * 2.4f;
            // the lively ones gallop or scamper about now and then (their Run clip), further than a stroll
            else if (b.Kind.Name is "dog" or "horse" or "pony" or "donkey" or "goat" or "cat" or "kitty" or "sheep" or "pig" or "cow" && Random.value < 0.14f && b.Motion != null && b.Motion.Has("Run"))
            {
                b.Speed = b.Kind.Speed * 3.2f;
                if (!hungry) b.Goal = Spot(b, door, b.Kind.Roam * 1.4f);
            }
        }

        private void Move(Beast b, float dt)
        {
            var root = b.Root;
            var pos = root.position;
            b.Moving = false;
            if (b.State is State.Wander or State.GoingIn or State.Home or State.Flee)
            {
                var to = Flat(b.Goal - pos);
                var step = b.Speed * dt;
                if (to.magnitude > 0.08f)
                {
                    pos += to.normalized * Mathf.Min(step, to.magnitude);
                    b.Facing = Quaternion.Slerp(b.Facing, Quaternion.LookRotation(to.normalized, Vector3.up), 1f - Mathf.Exp(-6f * dt));
                    b.Moving = true;
                }
                else b.Goal = pos;
            }
            if (b.Kind.Swims) pos.y = b.Pond.y - b.Foot * 0.55f * Mathf.Max(0.01f, Ease(b.Grow));
            else pos.y = GroundHeight.At(pos.x, pos.z, pos.y) - GroundHeight.Sink - b.Foot * Mathf.Max(0.01f, Ease(b.Grow)) + b.Air;
            root.position = pos;
            if (b.Motion != null)
            {
                // a rigged animal animates itself from how it moves; standing, it does its act (eating, lying down…)
                root.rotation = b.Facing;
                if (!b.Moving && b.Act != "" && b.State == State.Graze) b.Motion.Hold(b.Act);
                b.Model.localScale = Vector3.one * b.Scale * Mathf.Max(0.01f, Ease(b.Grow));
                return;
            }
            // the rest: a hop or a bob as they go, a waddle, a dip of the head to peck or graze
            var t = Time.time;
            var lift = b.Moving
                ? (b.Kind.Hops ? Mathf.Abs(Mathf.Sin(t * 12f + b.Seed)) * 0.08f : Mathf.Abs(Mathf.Sin(t * 7f + b.Seed)) * 0.04f)
                : 0f;
            var roll = b.Moving ? Mathf.Sin(t * (b.Kind.Hops ? 12f : 7f) + b.Seed) * (b.Kind.Hops ? 8f : 4f) : 0f;
            var peck = !b.Moving && b.State == State.Graze
                ? Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * (b.Kind.Hops ? 2.6f : 1.1f) + b.Seed * 3f)), 6f) * (b.Kind.Hops ? 28f : 10f)
                : 0f;
            root.rotation = b.Facing;
            b.Model.localPosition = new Vector3(0, lift, 0);
            b.Model.localRotation = Quaternion.Euler(peck, b.Yaw, roll);
            b.Model.localScale = Vector3.one * b.Scale * Mathf.Max(0.01f, Ease(b.Grow));
        }

        /// <summary>The odd cluck or neigh from round a house (not every animal at once).</summary>
        private void Calls()
        {
            foreach (var g in _beasts.Where(b => b.Kind.Call != null && b.State != State.Inside && b.State != State.Gone).GroupBy(b => b.Home))
            {
                if (!_nextCall.TryGetValue(g.Key, out var at)) { _nextCall[g.Key] = Time.time + Random.Range(3f, 10f); continue; }
                if (Time.time < at) continue;
                var who = g.ElementAt(Random.Range(0, g.Count()));
                var horse = who.Kind.Name == "horse";
                _nextCall[g.Key] = Time.time + (horse ? Random.Range(25f, 50f) : Random.Range(6f, 14f));
                IslandAudio.Play(who.Kind.Call!, who.Root.position, horse ? 0.45f : 0.35f, 0.12f, 0.5f);
            }
        }

        /// <summary>What an animal does while it stands about: eat, lie down, a gesture or a stretch (every clip the kind has).</summary>
        private static void PickAct(Beast b)
        {
            b.Act = "";
            var m = b.Motion;
            if (m == null) return;
            var r = Random.value;
            if (r < 0.5f && m.Has("Eat")) b.Act = "Eat";
            else if (r < 0.72f && b.Kind.Lies && m.Has("Lying")) { b.Act = "Lying"; b.Wait += 6f; }
            else if (r < 0.72f && b.Kind.Lies && m.Has("Sit")) { b.Act = "Sit"; b.Wait += 4f; }
            else if (r < 0.85f && m.Has("Gesture")) b.Act = "Gesture";
            else if (r < 0.92f && m.Has("Stretch")) b.Act = "Stretch";
        }

        /// <summary>A duck paddling about its pond: swimming, then bobbing still for a while.</summary>
        private void Paddle(Beast b, float dt)
        {
            if (b.Grow < 1f) b.Grow = Mathf.MoveTowards(b.Grow, 1f, dt * 2.5f);
            if (b.State == State.Wander && !Arrived(b)) return;
            if (b.State == State.Wander) { b.State = State.Graze; b.Wait = Random.Range(2f, 7f); return; }
            b.Wait -= dt;
            if (b.Wait > 0f) return;
            b.State = State.Wander;
            var c = Random.insideUnitCircle * b.Pond.w * 0.65f;
            b.Goal = new Vector3(b.Pond.x + c.x, b.Pond.y, b.Pond.z + c.y);
        }

        /// <summary>A pigeon takes off when anyone comes close, flies a little way and lands again. True while it's busy with that.</summary>
        private bool Pigeon(Beast b, float dt)
        {
            var host = _host!;
            if (!b.Flying)
            {
                var near = host.Player != null && Flat(host.Player.transform.position - b.Root.position).magnitude < 4.5f;
                if (!near || b.Grow < 1f) return false;
                b.Flying = true;
                b.State = State.Wander;
                var away = Flat(b.Root.position - host.Player!.transform.position).normalized;
                if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                b.Goal = Ground(b.Root.position + Quaternion.Euler(0, Random.Range(-40f, 40f), 0) * away * Random.Range(9f, 15f));
                b.Speed = 4.5f;
                if (b.Motion != null) b.Motion.WalkAs = "Fly";
                if (b.Motion != null) b.Motion.IdleAs = "Fly";
                return true;
            }
            // up while flying, down to land
            var left = Flat(b.Goal - b.Root.position).magnitude;
            var targetAir = left > 2f ? 2.6f : 0f;
            b.Air = Mathf.MoveTowards(b.Air, targetAir, dt * 3f);
            if (left <= 0.1f && b.Air <= 0.01f)
            {
                b.Flying = false;
                b.Speed = b.Kind.Speed;
                b.State = State.Graze;
                b.Wait = Random.Range(2f, 5f);
                if (b.Motion != null) { b.Motion.WalkAs = null; b.Motion.IdleAs = null; b.Motion.Play("Jump", 0.6f); }
            }
            return true;
        }

        // ---- helpers -----------------------------------------------------------------------------

        /// <summary>A dry spot within reach of a house's door.</summary>
        private Vector3 Around(int home, float roam)
        {
            var host = _host!;
            var door = host.DoorOf(home);
            for (var i = 0; i < 8; i++)
            {
                var p = door + Jitter(roam);
                var cell = host.Grid!.Data.CellOf(p);
                if (!host.Grid.Dry(cell.x, cell.y)) continue;
                return Ground(p);
            }
            return door;
        }

        /// <summary>Somewhere on this animal's side of its house, a dry spot.</summary>
        private Vector3 Spot(Beast b, Vector3 door, float roam)
        {
            var host = _host!;
            for (var i = 0; i < 6; i++)
            {
                var a = b.Heading + Random.Range(-0.6f, 0.6f);
                var r = roam * b.Range * Random.Range(0.5f, 1f);
                var p = door + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                var cell = host.Grid!.Data.CellOf(p);
                if (host.Grid.Dry(cell.x, cell.y)) return Ground(p);
                b.Heading += 1.3f;   // water that way: try another side
            }
            return door;
        }

        private static Vector3 Ground(Vector3 p) { p.y = GroundHeight.At(p.x, p.z, p.y); return p; }
        private static bool Arrived(Beast b) => Flat(b.Goal - b.Root.position).magnitude <= 0.1f;
        private static float Ease(float x) => x * x * (3f - 2f * x);
        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
        private static Vector3 Jitter(float r) { var c = Random.insideUnitCircle * r; return new Vector3(c.x, 0, c.y); }
    }
}
