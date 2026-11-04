using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// The island's land by chapter (docs: Farmverse — Island by Chapter). One designed landscape
    /// (<see cref="Shape"/>: the meadow, the pond, hills, the river and lake, the town plateau, the
    /// mountains with their lake and waterfall) is cut out of the sea by three coastlines, one per
    /// chapter, each wholly outside the one before. <see cref="Base"/> is the ground of a chapter:
    /// the landscape inside its coast, sloping to the sea bed outside. Deep inside a coast it is the
    /// landscape itself, so land never changes height when the island grows; new land only rises
    /// around the old shore.
    /// Pure functions (own noise, no Unity calls but Mathf/Vector), so the layout can be checked off-line.
    /// x is east, z is north; the farmyard is at the middle.
    /// </summary>
    internal static class IslandShape
    {
        public const int Chapters = 3;
        public const float Sea = 0f;
        public const float Meadow = 1.8f;           // the farm's ground level
        /// <summary>Bump when the land changes: old saves forget cleared props (their indices meant other trees).</summary>
        public const int LayoutVersion = 3;
        /// <summary>
        /// World size of the designed landscape: every position and coast below is drawn in design metres and
        /// shrunk by this on the way out (heights and road/river widths stay), so the islands stay compact.
        /// Layout 3 brought it from 1 to 0.65; <see cref="PreviousScale"/> lets old saves move their buildings in.
        /// </summary>
        public const float K = 0.65f;
        public const float PreviousScale = 1f;

        // ------------------------------------------------------------------ noise

        static readonly int[] Perm = MakePerm(1337);

        static int[] MakePerm(int seed)
        {
            var p = new int[512];
            var a = new int[256];
            for (int i = 0; i < 256; i++) a[i] = i;
            var rng = new System.Random(seed);
            for (int i = 255; i > 0; i--) { int j = rng.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
            for (int i = 0; i < 512; i++) p[i] = a[i & 255];
            return p;
        }

        static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        static float Grad(int h, float x, float y)
        {
            switch (h & 7)
            {
                case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
                case 4: return x; case 5: return -x; case 6: return y; default: return -y;
            }
        }

        /// <summary>2-D gradient noise in 0..1 (like Mathf.PerlinNoise, but the same everywhere this runs).</summary>
        public static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            xi &= 255; yi &= 255;
            float u = Fade(xf), v = Fade(yf);
            int aa = Perm[Perm[xi] + yi], ab = Perm[Perm[xi] + yi + 1], ba = Perm[Perm[xi + 1] + yi], bb = Perm[Perm[xi + 1] + yi + 1];
            float x1 = Mathf.Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
            float x2 = Mathf.Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
            return Mathf.Clamp01(Mathf.Lerp(x1, x2, v) * 0.7071f + 0.5f);
        }

        public static float Fbm(float x, float z, int oct)
        {
            float s = 0, a = 0.5f, f = 1, norm = 0;
            for (int i = 0; i < oct; i++) { s += a * Noise(x * f, z * f); norm += a; a *= 0.5f; f *= 2.03f; }
            return s / norm;
        }

        static float Smooth(float a, float b, float v) { float t = Mathf.Clamp01((v - a) / (b - a)); return t * t * (3f - 2f * t); }
        static float Lobe(float ang, float centreDeg, float widthRad)
        {
            float d = Mathf.DeltaAngle(ang * Mathf.Rad2Deg, centreDeg) * Mathf.Deg2Rad;
            return Mathf.Exp(-(d * d) / (widthRad * widthRad));
        }

        // ------------------------------------------------------------------ coasts

        /// <summary>Distance from the middle to chapter <paramref name="c"/>'s coast in the direction of (x, z).</summary>
        public static float Edge(int c, float x, float z) => K * EdgeD(c, x / K, z / K);

        static float EdgeD(int c, float x, float z)
        {
            float ang = Mathf.Atan2(z, x);
            float n = Fbm(x * 0.0042f + 11.3f, z * 0.0042f + 7.1f, 4);
            // chapter 1: a small round island with a gentle bay to the south
            float e1 = 108f * (0.9f + 0.2f * n) + 6f * Mathf.Sin(3f * ang + 1.3f) + 4f * Mathf.Sin(5f * ang + 0.4f)
                       - 22f * Lobe(ang, -90f, 0.22f);
            if (c <= 1) return e1;
            // chapter 2: lobes north-east (orchard) and west (beyond the river), a cove to the south-west
            float e2 = 166f * (0.88f + 0.24f * n) + 24f * Lobe(ang, 40f, 0.6f) + 80f * Lobe(ang, 178f, 0.6f) + 9f * Mathf.Sin(3f * ang + 0.7f) + 5f * Mathf.Sin(7f * ang + 2.1f);
            e2 = Mathf.Max(e2, e1 + 48f) - 58f * Lobe(ang, 226f, 0.24f);
            if (c == 2) return e2;
            // chapter 3: the whole map; mountains need the north to reach far out
            float e3 = 236f * (0.9f + 0.2f * n) + 30f * Lobe(ang, 95f, 0.7f) + 34f * Lobe(ang, -8f, 0.5f) + 14f * Mathf.Sin(3f * ang + 1.3f) + 9f * Mathf.Sin(5f * ang + 0.4f);
            e3 = Mathf.Min(Mathf.Max(e3, e2 + 36f + 58f * Lobe(ang, 226f, 0.24f)), 292f);
            return e3;
        }

        /// <summary>Metres inland from chapter <paramref name="c"/>'s coast (negative out at sea).</summary>
        public static float Inland(int c, float x, float z) => Edge(c, x, z) - Mathf.Sqrt(x * x + z * z);

        /// <summary>The first chapter whose coast takes in this point well inland (4 if none).</summary>
        public static int FirstChapter(float x, float z, float margin = 22f)
        {
            for (int c = 1; c <= Chapters; c++) if (Inland(c, x, z) >= margin) return c;
            return Chapters + 1;
        }

        // ------------------------------------------------------------------ the landscape

        // design metres (shrunk by K for the world: see the world copies below)
        static readonly Vector2 PondD = new Vector2(38f, -40f);
        const float PondRD = 11f;
        static readonly Vector2 LakeD = new Vector2(-150f, 22f);
        static readonly Vector2 IsletD = new Vector2(-156f, 34f);
        static readonly Vector2 PlateauD = new Vector2(222f, -18f);
        const float PlateauRD = 36f;
        public const float PlateauH = 4.4f;
        static readonly Vector2 MountainLakeD = new Vector2(-58f, 259f);
        const float MountainLakeRD = 12.5f;
        public const float MountainLakeLevel = 20f;
        const float CliffTopD = 246.5f, CliffFootD = 238.5f;     // z of the waterfall's lip and foot
        static readonly Vector2 PlungePoolD = new Vector2(-58f, 231f);

        // the same places in the world
        public static readonly Vector2 Pond = PondD * K;
        public const float PondR = PondRD * K;
        public static readonly Vector2 Lake = LakeD * K;
        public static readonly Vector2 Islet = IsletD * K;
        public static readonly Vector2 Plateau = PlateauD * K;
        public const float PlateauR = PlateauRD * K;
        public static readonly Vector2 MountainLake = MountainLakeD * K;
        public const float MountainLakeR = MountainLakeRD * K;
        public const float CliffTop = CliffTopD * K, CliffFoot = CliffFootD * K;
        public static readonly Vector2 PlungePool = PlungePoolD * K;

        /// <summary>Where the river runs, from the plunge pool under the waterfall, through the lake, to the cove.</summary>
        static readonly Vector2[] RiverD =
        {
            new Vector2(-58f, 232f), new Vector2(-66f, 205f), new Vector2(-92f, 168f), new Vector2(-122f, 126f),
            new Vector2(-140f, 84f), new Vector2(-146f, 54f), LakeD, new Vector2(-146f, -10f),
            new Vector2(-140f, -40f), new Vector2(-128f, -74f), new Vector2(-120f, -104f), new Vector2(-126f, -150f),
            new Vector2(-136f, -200f), new Vector2(-142f, -260f), new Vector2(-146f, -320f),
        };

        /// <summary>The river in the world.</summary>
        public static readonly Vector2[] River = System.Array.ConvertAll(RiverD, p => p * K);

        public struct Hill { public Vector2 at; public float r, h, rough; public int chapter; public string name; }
        public static readonly Hill[] Hills =
        {
            // chapter 2: quarry hills (north, rocky) and orchard hills (north-east, gentle)
            new Hill { at = new Vector2(8f, 152f), r = 32f, h = 15f, rough = 0.5f, chapter = 2, name = "quarry" },
            new Hill { at = new Vector2(48f, 142f), r = 26f, h = 11f, rough = 0.5f, chapter = 2, name = "quarry" },
            new Hill { at = new Vector2(-30f, 160f), r = 22f, h = 9f, rough = 0.45f, chapter = 2, name = "quarry" },
            new Hill { at = new Vector2(116f, 104f), r = 30f, h = 4.2f, rough = 0.15f, chapter = 2, name = "orchard" },
            new Hill { at = new Vector2(92f, 136f), r = 22f, h = 3.4f, rough = 0.15f, chapter = 2, name = "orchard" },
            // chapter 3: the mountains to the north, three peaks and a shoulder that holds the high lake
            new Hill { at = new Vector2(-128f, 258f), r = 52f, h = 44f, rough = 0.35f, chapter = 3, name = "peak" },
            new Hill { at = new Vector2(10f, 272f), r = 62f, h = 64f, rough = 0.3f, chapter = 3, name = "peak" },
            new Hill { at = new Vector2(104f, 256f), r = 50f, h = 46f, rough = 0.35f, chapter = 3, name = "peak" },
            new Hill { at = new Vector2(-58f, 262f), r = 42f, h = 42f, rough = 0.15f, chapter = 3, name = "shoulder" },
        };

        /// <summary>Distance from p to the river line and how far along it is (0 at the waterfall, 1 at the cove).</summary>
        public static float RiverDist(float x, float z, out float along) => K * RiverDistD(x / K, z / K, out along);

        static float RiverDistD(float x, float z, out float along)
        {
            var p = new Vector2(x, z);
            float best = float.MaxValue, total = 0, at = 0, run = 0;
            for (int i = 0; i < RiverD.Length - 1; i++) total += Vector2.Distance(RiverD[i], RiverD[i + 1]);
            for (int i = 0; i < RiverD.Length - 1; i++)
            {
                var a = RiverD[i]; var b = RiverD[i + 1];
                var ab = b - a; float L = ab.magnitude;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / (L * L));
                float d = Vector2.Distance(p, a + ab * t);
                if (d < best) { best = d; at = run + t * L; }
                run += L;
            }
            along = at / total;
            return best;
        }

        /// <summary>Half the river's width there: narrow under the falls, wide at the mouth.</summary>
        public static float RiverHalf(float along) => Mathf.Lerp(4.2f, 7.5f, along);

        /// <summary>The landscape as it would be with land everywhere (metres; sea level 0).</summary>
        public static float Shape(float x, float z) => ShapeD(x / K, z / K);

        static float ShapeD(float x, float z)
        {
            float h = Meadow;
            // a faint swell in the meadow so it isn't a table (under 0.4 m across a field: still buildable)
            h += (Fbm(x * 0.012f + 40f, z * 0.012f + 9f, 3) - 0.5f) * 0.5f * Smooth(30f, 70f, Mathf.Sqrt(x * x + z * z));

            // hills and mountains
            foreach (var k in Hills)  // design metres
            {
                float d = Vector2.Distance(new Vector2(x, z), k.at);
                if (d > k.r * 1.6f) continue;
                float bump = Mathf.Exp(-2.6f * (d / k.r) * (d / k.r));
                float rough = 1f + k.rough * (Fbm(x * 0.045f + k.at.x, z * 0.045f + k.at.y, 4) - 0.5f) * 2f;
                h = Mathf.Max(h, Meadow + k.h * bump * rough);
            }

            // the town plateau: a raised, flat table with soft ramps (chapter 3, east)
            float dp = Vector2.Distance(new Vector2(x, z), PlateauD) + (Fbm(x * 0.03f, z * 0.03f + 5f, 2) - 0.5f) * 10f;
            h = Mathf.Lerp(h, Mathf.Max(h, PlateauH), 1f - Smooth(PlateauRD, PlateauRD + 16f, dp));

            // the mountain lake on its shelf, the cliff it pours over, and the plunge pool below
            h = Waterfall(x, z, h);

            // the pond by the farm
            float dpo = Vector2.Distance(new Vector2(x, z), PondD) + (Noise(x * 0.25f + 3f, z * 0.25f) - 0.5f) * 3f;
            if (dpo < PondRD + 6f) h = Mathf.Lerp(-0.9f, h, Smooth(PondRD - 3f, PondRD + 5f, dpo));

            // the lake (an oval with a small island) and the river
            var q = new Vector2(x, z) - LakeD;
            var rot = new Vector2(q.x * 0.97f + q.y * 0.24f, -q.x * 0.24f + q.y * 0.97f);
            float dl = Mathf.Sqrt(rot.x * rot.x / (28f * 28f) + rot.y * rot.y / (46f * 46f)) * 24f + (Noise(x * 0.08f, z * 0.08f + 2f) - 0.5f) * 6f;
            if (dl < 34f) h = Mathf.Lerp(-2.2f, h, Smooth(20f, 30f, dl));
            float di = Vector2.Distance(new Vector2(x, z), IsletD);
            if (di < 7f) h = Mathf.Max(h, Mathf.Lerp(1.5f, -2.2f, Smooth(3f, 7f, di)));
            float dr = RiverDistD(x, z, out var along);
            float w = RiverHalf(along) / K + (Noise(x * 0.06f + 8f, z * 0.06f) - 0.5f) * 2.5f;
            if (dr < w + 9f) h = Mathf.Min(h, Mathf.Lerp(-1.4f, h, Smooth(w, w + 8f, dr)));
            return h;
        }

        static float Waterfall(float x, float z, float h)
        {
            float dx = x - MountainLakeD.x;
            if (Mathf.Abs(dx) > 30f || z < PlungePoolD.y - 16f || z > MountainLakeD.y + 24f) return h;
            // the high lake: a basin in the mountain's shoulder, its water at 20 m
            float dml = Vector2.Distance(new Vector2(x, z), MountainLakeD);
            if (dml < MountainLakeRD + 4f) h = Mathf.Lerp(MountainLakeLevel - 1.4f, h, Smooth(MountainLakeRD - 3f, MountainLakeRD + 4f, dml));
            // the channel from the lake to the lip of the falls
            if (z > CliffTopD - 1f && z < MountainLakeD.y && Mathf.Abs(dx) < 7f)
                h = Mathf.Min(h, Mathf.Lerp(MountainLakeLevel - 0.3f, h, Smooth(3.5f, 7f, Mathf.Abs(dx))));
            // the cliff the falls pour over: steep from the lip down to the plunge pool, only in the valley
            if (z <= CliffTopD + 1f)
            {
                float wall = Mathf.Lerp(-1.2f, MountainLakeLevel - 0.3f, Smooth(CliffFootD, CliffTopD, z));
                float valley = 1f - Smooth(6f, 13f, Mathf.Abs(dx));
                h = Mathf.Lerp(h, Mathf.Min(h, wall), valley);
                float dpp = Vector2.Distance(new Vector2(x, z), PlungePoolD);
                if (dpp < 13f) h = Mathf.Min(h, Mathf.Lerp(-1.6f, h, Smooth(7f, 13f, dpp)));
            }
            return h;
        }

        /// <summary>Chapter <paramref name="c"/>'s ground (before routes): the landscape inside its coast, the sea bed outside.</summary>
        public static float Base(int c, float x, float z)
        {
            float t = Inland(c, x, z);
            float land = Smooth(-18f, 22f, t);
            float seabed = Sea - 1.5f - Mathf.Clamp01(-t / 60f) * 12f;
            float s = Shape(x, z);
            // the landscape may dip below the sea (lake, river, pond): outside the coast it never rises above the bed
            return Mathf.Lerp(Mathf.Min(seabed, s), s, land);
        }

        /// <summary>The high, rocky ground of the mountains (no buildings, pines and snow).</summary>
        public static bool IsMountain(float x, float z) => z > 205f * K && Shape(x, z) > Meadow + 6f;

        /// <summary>Named places the camera tours when a chapter's land rises.</summary>
        public static IEnumerable<(string name, Vector3 at, float distance)> Tour(int chapter)
        {
            if (chapter == 2)
            {
                yield return ("The lake", new Vector3(Lake.x, 0, Lake.y), 130f * K);
                yield return ("The river and its bridges", new Vector3(-140f * K, 0, 70f * K), 110f * K);
                yield return ("The cove", new Vector3(-112f * K, 0, -108f * K), 120f * K);
                yield return ("The orchard hills", new Vector3(110f * K, 0, 112f * K), 140f * K);
                yield return ("The quarry hills", new Vector3(14f * K, 0, 145f * K), 150f * K);
            }
            else if (chapter == 3)
            {
                yield return ("The mountains", new Vector3(-14f * K, 0, 236f * K), 260f * K);
                yield return ("The waterfall", new Vector3(PlungePool.x, 0, PlungePool.y + 4f), 110f * K);
                yield return ("The town plateau", new Vector3(Plateau.x, 0, Plateau.y), 140f * K);
                yield return ("The harbour", new Vector3(14f * K, 0, -236f * K), 140f * K);
            }
        }
    }
}
