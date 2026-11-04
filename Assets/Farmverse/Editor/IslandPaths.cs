using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// The island's routes by chapter, laid out before the ground is built (docs: Island by Chapter):
    ///  - chapter 1: dirt tracks from the farmyard to the shore, and the railway: a single line from the
    ///    station out across the sea on a trestle to the edge of the map, where the freight train comes from;
    ///  - chapter 2: the cobbled village road west over the river (a stone bridge) and down to the cove,
    ///    tracks to the quarry and orchard hills, a footbridge below the lake;
    ///  - chapter 3: roads to the town plateau and the harbour, a path up to the mountain lake.
    /// A route belongs to the chapter it opens in and stays in every chapter after. Each is stamped into a
    /// distance field per chapter (distance to the route and the route's own smoothed height, one value per
    /// 2 m ground vertex): the builder levels the ground under the routes of a chapter, paints their dirt,
    /// keeps their cells off the building grid and trees off them. Where a route crosses water it does not
    /// level the ground: it runs on a bridge (or the railway on its trestle).
    /// Pure layout (no editor calls), so it can be checked off-line; the meshes are in IslandPaths.Objects.
    /// </summary>
    internal static partial class IslandPaths
    {
        const float Size = 640f;
        const float Step = 2f;                      // field spacing = ground vertex spacing
        static int N => Mathf.RoundToInt(Size / Step) + 1;
        const int Chapters = IslandShape.Chapters;

        public const float RoadHalf = 3f;           // cobbles: 6 m wide
        public const float TrackHalf = 1.6f;
        public const float RailHalf = 1.8f;         // ballast foot
        public const float Gauge = 1.5f;
        /// <summary>Ground lower than this (sea, lake, river) is crossed on a bridge, not filled in.</summary>
        public const float WetBelow = IslandShape.Sea + 0.8f;

        public enum Kind { Road, Track, Rail }

        public sealed class Route
        {
            public string name = "";
            public Kind kind;
            public int chapter;
            public List<Vector3> pts = new List<Vector3>();
        }

        /// <summary>A stretch of a route over water: a bridge (roads, tracks) or the railway trestle.</summary>
        public sealed class Bridge
        {
            public Route route = null!;
            public int from, to;          // point indices, inclusive, along route.pts
            public float deck;            // deck height (m)
            public float half;            // half width of the deck
        }

        public static readonly List<Route> Routes = new List<Route>();
        public static readonly List<Bridge> Bridges = new List<Bridge>();
        public static Route RailRoute => Routes.Find(r => r.kind == Kind.Rail)!;
        public static List<Vector3> Rail => RailRoute.pts;

        // per chapter (index c - 1): distance and height fields
        static float[][] _roadD = null!, _roadH = null!, _dirtD = null!, _dirtH = null!, _railD = null!, _railH = null!;
        static bool _ready;

        // the railway station: beside the line, facing it, joined to the farmyard by a dirt track
        public static Vector3 StationPos;
        public static float StationYaw;          // degrees; the station's front (+Z) faces the line
        public static int StationIndex;          // metre along the line where the engine stops
        public const float StationRadius = 10f;
        const float StationSetback = 8.5f;       // rail centre to station centre

        // ------------------------------------------------------------------ layout

        public static void Prepare()
        {
            _ready = false;
            int n = N;
            float[][] F(float v) { var a = new float[Chapters][]; for (int c = 0; c < Chapters; c++) a[c] = Filled(n * n, v); return a; }
            _roadD = F(999f); _roadH = F(0f); _dirtD = F(999f); _dirtH = F(0f); _railD = F(999f); _railH = F(0f);
            Routes.Clear();
            Bridges.Clear();

            // ---- the causeway: the road off the island, straight east along the farm's main street ----
            var rail = Add("Causeway", Kind.Rail, 1, new[] { new Vector2(CausewayFrom, CausewayZ), new Vector2(340f * IslandShape.K, CausewayZ) }, 1.75f, 18);
            PlaceStation(rail);

            // ---- the planned layout: straight roads at right angles, blocks of different sizes between them ----
            Grid();
            // the harbour lane (chapter 3) and the hiking path up the mountain stay as they were meant: one straight lane, one path
            var hb = Harbour();
            Add("Harbour lane", Kind.Track, 3, new[] { new Vector2(hb.x, GridZ[1]), hb + new Vector2(0f, 10f) }, 1.7f, 10);
            Add("Mountain path", Kind.Track, 3, new[] { new Vector2(10f * IslandShape.K, GridZ[8]), new Vector2(-4f, 196f) * IslandShape.K, new Vector2(-22f, 214f) * IslandShape.K, new Vector2(-36f, 236f) * IslandShape.K, new Vector2(-40f, 252f) * IslandShape.K }, 1.7f, 6);

            foreach (var r in Routes)
            {
                float radius = r.kind == Kind.Road ? 10f : r.kind == Kind.Rail ? 9f : 7f;
                for (int c = r.chapter; c <= Chapters; c++)
                {
                    var (D, H) = r.kind == Kind.Road ? (_roadD[c - 1], _roadH[c - 1]) : r.kind == Kind.Rail ? (_railD[c - 1], _railH[c - 1]) : (_dirtD[c - 1], _dirtH[c - 1]);
                    Stamp(r.pts, D, H, radius);
                }
                FindBridges(r);
            }
            _ready = true;
            PlaceCamp();
        }

        // ------------------------------------------------------------------ the caravan park

        /// <summary>The caravan park (valley chapter): a flat lot beside the causeway where visitors' campers stay.</summary>
        public static Vector3 CampPos { get; private set; }
        public static float CampYaw { get; private set; }
        public static bool HasCamp { get; private set; }
        public const float CampHalfX = 12f, CampHalfZ = 8f;

        /// <summary>Inside the caravan park's lot (plus a margin).</summary>
        public static bool InCamp(float x, float z, float margin = 0f) =>
            HasCamp && Mathf.Abs(x - CampPos.x) < CampHalfX + margin && Mathf.Abs(z - CampPos.z) < CampHalfZ + margin;

        static void PlaceCamp()
        {
            HasCamp = false;
            var why = new Dictionary<string, int>();
            void No(string r) => why[r] = why.TryGetValue(r, out var n) ? n + 1 : 1;
            // beside the causeway (both sides), then anywhere in the valley's new land near it: the first flat lot that is
            // new land in chapter 2, off every road and lane
            for (float off = 0f; off <= 40f; off += 4f)
                foreach (var side in new[] { 1f, -1f })
                    for (float x = GridX[5]; x < GridX[9]; x += 1f)
                    {
                        float z = CausewayZ + side * (RailHalf + CampHalfZ + 3f + off);
                        if (IslandShape.FirstChapter(x, z, 4f) != 2) { No("chapter"); continue; }
                        float lo = float.MaxValue, hi = float.MinValue; string? bad = null;
                        for (float dx = -CampHalfX; dx <= CampHalfX && bad == null; dx += 2f)
                            for (float dz = -CampHalfZ; dz <= CampHalfZ && bad == null; dz += 2f)
                            {
                                float px = x + dx, pz = z + dz;
                                if (IslandShape.Inland(2, px, pz) < 4f) bad = "coast2";
                                else if (IslandShape.Inland(1, px, pz) > 4f) bad = "chapter1";
                                else if (RoadDist(px, pz) < RoadHalf + 1f || RailDist(px, pz) < RailHalf + 1f || TrackDist(px, pz) < TrackHalf + 0.8f) bad = "route";
                                else if (StationDist(px, pz) < StationRadius + 2f) bad = "station";
                                else
                                {
                                    float h = IslandShape.Base(2, px, pz);
                                    lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                                }
                            }
                        if (bad != null) { No(bad); continue; }
                        if (lo < IslandShape.Sea + 0.8f) { No("low"); continue; }
                        if (hi - lo > 3.5f) { No("steep"); continue; }   // levelled under the lot
                        CampPos = new Vector3(x, Mathf.Max(lo, IslandShape.Meadow - 0.2f) * 0.5f + hi * 0.5f, z);
                        CampYaw = side > 0 ? 180f : 0f;      // the campers' doors look at the road
                        HasCamp = true;
                        Debug.Log($"[Paths] caravan park at {CampPos}");
                        return;
                    }
            Debug.LogWarning("[Paths] no flat lot by the causeway for the caravan park: " + string.Join(", ", why.Select(kv => $"{kv.Key} {kv.Value}")));
        }

        // the planned grid: lines of constant x (running north–south) and constant z (east–west); uneven spacing
        // gives blocks of different sizes. The main streets are cobbled-width roads, the rest farm lanes.
        // design metres, shrunk by IslandShape.K
        public static readonly float[] GridX = Scaled(-230f, -178f, -128f, -78f, -24f, 62f, 110f, 156f, 200f, 248f);
        public static readonly float[] GridZ = Scaled(-236f, -184f, -132f, -80f, -26f, 34f, 86f, 140f, 192f, 240f);
        internal static readonly float[] MainX = Scaled(-24f, 156f);
        internal static readonly float[] MainZ = Scaled(-26f, 140f);
        static float[] Scaled(params float[] v) => System.Array.ConvertAll(v, a => a * IslandShape.K);
        public const float CausewayZ = 34f * IslandShape.K, CausewayFrom = -24f * IslandShape.K;
        const float LandMargin = 7f;      // keep the roads this far in from the beach
        const float MaxBridge = 24f;      // a road may cross this much water on a bridge (the river)

        /// <summary>Every grid line, cut into pieces between crossings, each over land (or a short bridge), each in the chapter its land arrives.</summary>
        static void Grid()
        {
            foreach (var x in GridX) Line(true, x);
            foreach (var z in GridZ) Line(false, z);
        }

        static void Line(bool northSouth, float c)
        {
            const float From = -320f * IslandShape.K, To = 320f * IslandShape.K;
            Vector2 P(float t) => northSouth ? new Vector2(c, t) : new Vector2(t, c);
            int n = (int)(To - From) + 1;
            var dry = new bool[n]; var wet = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var p = P(From + i);
                var inside = IslandShape.Inland(Chapters, p.x, p.y) > LandMargin && !IslandShape.IsMountain(p.x, p.y)
                             && Vector2.Distance(p, Flat(StationPos)) > StationRadius + 1f;
                var water = IslandShape.Base(Chapters, p.x, p.y) < WetBelow;
                dry[i] = inside && !water;
                wet[i] = inside && water;
            }
            // short water crossings between dry ground become bridges
            var ok = (bool[])dry.Clone();
            for (int i = 0; i < n; i++)
            {
                if (!wet[i] || (i > 0 && wet[i - 1])) continue;
                int j = i; while (j < n && wet[j]) j++;
                if (i > 0 && dry[i - 1] && j < n && dry[j] && j - i <= MaxBridge) for (int k = i; k < j; k++) ok[k] = true;
                i = j - 1;
            }
            // cut at every crossing line so each piece can join its own chapter
            var cuts = new List<float>(northSouth ? GridZ : GridX);
            if (!northSouth) cuts.Add(CausewayFrom);
            if (northSouth) cuts.Add(CausewayZ);
            cuts.Sort();
            var main = northSouth ? System.Array.IndexOf(MainX, c) >= 0 : System.Array.IndexOf(MainZ, c) >= 0;
            int run = -1;
            for (int i = 0; i <= n; i++)
            {
                var good = i < n && ok[i];
                if (good && run < 0) run = i;
                if (good || run < 0) continue;
                // a run [run, i-1]: pieces between cuts
                float a = From + run, b = From + i - 1;
                var stops = new List<float> { a };
                foreach (var k in cuts) if (k > a + 1f && k < b - 1f) stops.Add(k);
                stops.Add(b);
                for (int s = 0; s + 1 < stops.Count; s++)
                {
                    float s0 = stops[s], s1 = stops[s + 1];
                    if (s1 - s0 < 12f) continue;
                    // the causeway is this street east of the farm
                    if (!northSouth && Mathf.Approximately(c, CausewayZ) && s0 >= CausewayFrom - 0.5f) continue;
                    int ch = 1;
                    for (float t = s0; t <= s1; t += 2f) { var q = P(t); if (IslandShape.Base(Chapters, q.x, q.y) >= WetBelow) ch = Mathf.Max(ch, IslandShape.FirstChapter(q.x, q.y, LandMargin)); }
                    if (ch > Chapters) continue;
                    Add($"{(main ? "Street" : "Lane")} {(northSouth ? "x" : "z")}{c:0} {s0:0}..{s1:0}", main ? Kind.Road : Kind.Track, ch, new[] { P(s0), P(s1) }, 1.7f, main ? 12 : 10);
                }
                run = -1;
            }
        }

        /// <summary>The harbour's spot: chapter 3's south shore, below the farm.</summary>
        public static Vector2 Harbour()
        {
            float r = 300f;
            while (r > 0f && IslandShape.Inland(3, 6f, -r) < 14f) r -= 1f;
            return new Vector2(6f, -r);
        }

        static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        static Route Add(string name, Kind kind, int chapter, Vector2[] ctrl, float minH, int window)
        {
            var r = new Route { name = name, kind = kind, chapter = chapter, pts = WithHeights(Resample(Spline(ctrl), false), minH, window, false) };
            Routes.Add(r);
            return r;
        }

        /// <summary>A winding track from near the farmyard out to just above chapter <paramref name="c"/>'s beach.</summary>
        static Vector2[] Radial(float deg, float from, int c)
        {
            float a = deg * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var side = new Vector2(-dir.y, dir.x);
            float len = 0f;
            while (len < 300f && IslandShape.Inland(c, dir.x * len, dir.y * len) > 12f) len += 1f;
            var pts = new List<Vector2>();
            for (float d = from; d < len; d += 12f)
            {
                float f = d / len;
                pts.Add(dir * d + side * (Mathf.Sin(f * Mathf.PI * 2.1f + a * 3f) * 6f * Mathf.Sin(f * Mathf.PI)));
            }
            pts.Add(dir * len);
            return pts.ToArray();
        }

        static void PlaceStation(Route rail)
        {
            // the station stands by the line about 30 m in from the buffer, on the farm's side
            int best = Mathf.Min(Mathf.RoundToInt((GridX[5] + GridX[6]) / 2f - CausewayFrom), rail.pts.Count - 2);   // on the main street, between the grid's 6th and 7th crossings
            var p = rail.pts[best];
            var side = Side(rail.pts, best);
            var flat = new Vector3(p.x, 0, p.z);
            if (Vector3.Dot(side, -flat) < 0) side = -side;
            StationIndex = best;
            StationPos = p + side * StationSetback;
            StationYaw = Mathf.Atan2(-side.x, -side.z) * Mathf.Rad2Deg;      // front looks back at the line
        }

        static void FindBridges(Route r)
        {
            int start = -1;
            for (int i = 0; i <= r.pts.Count; i++)
            {
                bool wet = i < r.pts.Count && IslandShape.Base(r.kind == Kind.Rail ? 1 : r.chapter, r.pts[i].x, r.pts[i].z) < WetBelow;
                if (wet && start < 0) start = i;
                if (!wet && start >= 0)
                {
                    int from = Mathf.Max(0, start - 3), to = Mathf.Min(r.pts.Count - 1, i + 2);
                    float deck = 0f;
                    for (int k = from; k <= to; k++) deck = Mathf.Max(deck, r.pts[k].y);
                    Bridges.Add(new Bridge
                    {
                        route = r, from = from, to = to, deck = deck + 0.25f,
                        half = r.kind is Kind.Road or Kind.Rail ? RoadHalf + 0.6f : TrackHalf + 0.5f,
                    });
                    start = -1;
                }
            }
        }

        static float[] Filled(int n, float v) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }

        /// <summary>A smooth curve through the control points (Catmull-Rom), a point every half metre or so.</summary>
        static List<Vector2> Spline(Vector2[] c)
        {
            var o = new List<Vector2>();
            if (c.Length < 3) { o.AddRange(c); return o; }
            for (int i = 0; i < c.Length - 1; i++)
            {
                var p0 = c[Mathf.Max(i - 1, 0)]; var p1 = c[i]; var p2 = c[i + 1]; var p3 = c[Mathf.Min(i + 2, c.Length - 1)];
                int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 0.5f));
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            o.Add(c[c.Length - 1]);
            return o;
        }

        static List<Vector2> Resample(List<Vector2> ctrl, bool closed)
        {
            var o = new List<Vector2>();
            int cnt = closed ? ctrl.Count : ctrl.Count - 1;
            float dist = 0f, next = 0f;
            for (int i = 0; i < cnt; i++)
            {
                var a = ctrl[i]; var b = ctrl[(i + 1) % ctrl.Count];
                float L = Vector2.Distance(a, b);
                if (L < 1e-4f) continue;
                while (next <= dist + L) { o.Add(Vector2.Lerp(a, b, (next - dist) / L)); next += 1f; }
                dist += L;
            }
            if (!closed) o.Add(ctrl[ctrl.Count - 1]);
            return o;
        }

        /// <summary>Gives each point a height: the final landscape (never below <paramref name="minH"/>), averaged along the route so it runs smooth.</summary>
        static List<Vector3> WithHeights(List<Vector2> pts, float minH, int window, bool closed)
        {
            int n = pts.Count;
            var raw = new float[n];
            for (int i = 0; i < n; i++) raw[i] = Mathf.Max(IslandShape.Base(3, pts[i].x, pts[i].y), minH);
            var o = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                float s = 0; int c = 0;
                for (int k = -window; k <= window; k++)
                {
                    int j = i + k;
                    if (closed) j = (j % n + n) % n; else if (j < 0 || j >= n) continue;
                    s += raw[j]; c++;
                }
                o.Add(new Vector3(pts[i].x, s / c, pts[i].y));
            }
            return o;
        }

        static void Stamp(List<Vector3> pts, float[] D, float[] H, float radius)
        {
            int n = N;
            float o = -Size / 2;
            foreach (var p in pts)
            {
                int i0 = Mathf.Max(0, Mathf.FloorToInt((p.x - radius - o) / Step)), i1 = Mathf.Min(n - 1, Mathf.CeilToInt((p.x + radius - o) / Step));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((p.z - radius - o) / Step)), j1 = Mathf.Min(n - 1, Mathf.CeilToInt((p.z + radius - o) / Step));
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        float dx = o + i * Step - p.x, dz = o + j * Step - p.z;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        int k = j * n + i;
                        if (d < D[k]) { D[k] = d; H[k] = p.y; }
                    }
            }
        }

        static float Bilerp(float[] f, float x, float z)
        {
            int n = N;
            float fx = (x + Size / 2) / Step, fz = (z + Size / 2) / Step;
            int i = Mathf.Clamp(Mathf.FloorToInt(fx), 0, n - 2), j = Mathf.Clamp(Mathf.FloorToInt(fz), 0, n - 2);
            float tx = Mathf.Clamp01(fx - i), tz = Mathf.Clamp01(fz - j);
            float a = Mathf.Lerp(f[j * n + i], f[j * n + i + 1], tx);
            float b = Mathf.Lerp(f[(j + 1) * n + i], f[(j + 1) * n + i + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        // ------------------------------------------------------------------ queries (chapter 3 = every route)

        public static float RoadDist(float x, float z, int c = Chapters) => _ready ? Bilerp(_roadD[c - 1], x, z) : 999f;
        public static float RailDist(float x, float z, int c = Chapters) => _ready ? Bilerp(_railD[c - 1], x, z) : 999f;
        public static float TrackDist(float x, float z, int c = Chapters) => _ready ? Bilerp(_dirtD[c - 1], x, z) : 999f;

        /// <summary>
        /// Chapter <paramref name="c"/>'s ground with its routes levelled in (cuts and gentle embankments).
        /// Over water (h below <see cref="WetBelow"/>) nothing is filled: the route crosses on a bridge.
        /// </summary>
        public static float Level(int c, float x, float z, float h)
        {
            if (!_ready) return h;
            float dry = Smooth(IslandShape.Sea - 0.4f, WetBelow, h);
            if (dry <= 0f) return h;
            int k = c - 1;
            float dr = Bilerp(_roadD[k], x, z);
            if (dr < 10f) h = Mathf.Lerp(h, Bilerp(_roadH[k], x, z), (1f - Smooth(RoadHalf + 0.6f, RoadHalf + 7f, dr)) * dry);
            float dl = Bilerp(_railD[k], x, z);
            if (dl < 9f) h = Mathf.Lerp(h, Bilerp(_railH[k], x, z), (1f - Smooth(RailHalf + 0.6f, RailHalf + 6f, dl)) * dry);
            float ds = StationDist(x, z);
            if (ds < StationRadius + 6f) h = Mathf.Lerp(h, StationPos.y, 1f - Smooth(StationRadius, StationRadius + 6f, ds));
            if (c >= 2 && InCamp(x, z, 5f))
            {
                float ex = Mathf.Max(0f, Mathf.Abs(x - CampPos.x) - CampHalfX), ez = Mathf.Max(0f, Mathf.Abs(z - CampPos.z) - CampHalfZ);
                h = Mathf.Lerp(h, CampPos.y, (1f - Smooth(0f, 5f, Mathf.Max(ex, ez))) * dry);
            }
            float dt = Bilerp(_dirtD[k], x, z);
            if (dt < 7f) h = Mathf.Lerp(h, Bilerp(_dirtH[k], x, z), (1f - Smooth(1.5f, 6f, dt)) * 0.8f * dry);
            return h;
        }

        /// <summary>Weight of bare earth from chapter <paramref name="c"/>'s routes: dirt tracks and the shoulders of roads and the railway.</summary>
        public static float DirtWeight(int c, float x, float z)
        {
            if (!_ready) return 0f;
            int k = c - 1;
            float jag = (IslandShape.Noise(x * 0.35f + 7f, z * 0.35f + 3f) - 0.5f) * 1.4f;   // ragged edges
            float track = 1f - Smooth(TrackHalf - 0.2f, TrackHalf + 2.0f, Bilerp(_dirtD[k], x, z) + jag * 0.5f);   // narrow sand tracks, like the packs' farms
            float road = 1f - Smooth(RoadHalf + 0.2f, RoadHalf + 2.2f, Bilerp(_roadD[k], x, z) + jag * 0.5f);
            float rail = 1f - Smooth(RailHalf + 0.2f, RailHalf + 2.2f, Bilerp(_railD[k], x, z) + jag * 0.5f);
            return Mathf.Max(track, Mathf.Max(road * 0.9f, rail * 0.85f));
        }

        /// <summary>True where the cell would sit on any route of any chapter (kept free from the start, so a later road never runs under a building).</summary>
        public static bool Blocks(float x, float z) =>
            RoadDist(x, z) < RoadHalf + 1.2f || RailDist(x, z) < RailHalf + 1.5f || TrackDist(x, z) < TrackHalf + 0.8f || StationDist(x, z) < StationRadius || InCamp(x, z, 1f);

        public static float StationDist(float x, float z) => Vector2.Distance(new Vector2(x, z), new Vector2(StationPos.x, StationPos.z));

        /// <summary>The deck height if (x, z) is on a bridge of chapter <paramref name="c"/> or earlier (walkable), else null.</summary>
        public static float? BridgeAt(int c, float x, float z)
        {
            foreach (var b in Bridges)
            {
                if (b.route.chapter > c || b.route.kind == Kind.Rail) continue;
                for (int i = b.from; i <= b.to; i++)
                {
                    var p = b.route.pts[i];
                    float dx = p.x - x, dz = p.z - z;
                    if (dx * dx + dz * dz < (b.half + 0.3f) * (b.half + 0.3f)) return b.deck;
                }
            }
            return null;
        }

        static float Smooth(float a, float b, float v) { float t = Mathf.Clamp01((v - a) / (b - a)); return t * t * (3f - 2f * t); }

        static Vector3 Side(List<Vector3> p, int i)
        {
            var a = p[Mathf.Max(i - 1, 0)]; var b = p[Mathf.Min(i + 1, p.Count - 1)];
            var t = b - a; t.y = 0; t = t.normalized;
            return new Vector3(t.z, 0, -t.x);
        }
    }
}
