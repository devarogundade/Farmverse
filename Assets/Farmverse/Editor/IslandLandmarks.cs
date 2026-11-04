using System.Collections.Generic;
using Farmverse.Presentation.World.Island;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Farmverse.Editor
{
    /// <summary>
    /// The island's landmarks by chapter (docs: Island by Chapter), built from simple shapes and the
    /// world textures until their models are made: the chapter 1 jetty and rowing boat in the bay,
    /// lily pads and reeds on the pond and lake, the cove's pier and fishing boats and stepping stones
    /// over the river (chapter 2), the mountain lake, the waterfall, the harbour's breakwater and
    /// lighthouse, the town square with its fountain and the lookout bench (chapter 3).
    /// Each is tagged with the chapters it shows in.
    /// </summary>
    internal static class IslandLandmarks
    {
        const string Tex = "Assets/Farmverse/Content/Textures/World/";
        const float Sea = IslandShape.Sea;

        /// <summary>Builds them all under a "Landmarks" object; returns the renderers that use the sea's water material.</summary>
        public static List<MeshRenderer> Build(string gen, string matDir, Material waterMat)
        {
            var root = new GameObject("Landmarks");
            var wood = IslandPaths.LitMaterial($"{matDir}/Bridge_Wood.mat", "wood_planks", new Color(0.78f, 0.62f, 0.46f), 0f, 0.2f);
            var stone = IslandPaths.LitMaterial($"{matDir}/Rock_Breakwater.mat", "rock_cliff", new Color(0.86f, 0.84f, 0.8f), 0f, 0.1f);
            var paving = IslandPaths.LitMaterial($"{matDir}/Town_Paving.mat", "paving_stone", new Color(1f, 0.97f, 0.92f), 0f, 0.15f);
            var lily = Plain($"{matDir}/Lily.mat", new Color(0.36f, 0.66f, 0.28f), 0.25f);
            var reed = Plain($"{matDir}/Reed.mat", new Color(0.52f, 0.62f, 0.3f), 0.1f);
            var white = Plain($"{matDir}/Paint_White.mat", new Color(0.95f, 0.94f, 0.9f), 0.35f);
            var red = Plain($"{matDir}/Paint_Red.mat", new Color(0.82f, 0.2f, 0.16f), 0.35f);
            var blue = Plain($"{matDir}/Paint_Blue.mat", new Color(0.2f, 0.46f, 0.78f), 0.35f);
            var lamp = Plain($"{matDir}/Lamp_Glow.mat", new Color(1f, 0.9f, 0.55f), 0.6f);
            lamp.EnableKeyword("_EMISSION"); lamp.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.45f) * 2.2f);
            var waterRenderers = new List<MeshRenderer>();
            int n = 0;
            string P(string what) => $"{gen}/lm_{what}_{n++}.asset";

            // ---- chapter 1: the jetty and rowing boat in the bay (the bay fills with land in chapter 2) ----
            var bay = Shore(1, -90f, 0.5f);
            var dirOut = new Vector3(0, 0, -1);
            var jetty = new GameObject("Jetty");
            jetty.transform.SetParent(root.transform, false);
            Pier(jetty, bay - dirOut * 5f, dirOut, 20f, 1.25f, 1.2f, wood, P("jetty"));
            Boat(jetty, bay + dirOut * 14f + new Vector3(2.6f, 0, 0), Quaternion.Euler(0, 8f, 0), 3.4f, wood, red, P("rowboat"));
            Tag(jetty, 1, 1);

            // the island's sign by the farmyard
            var sign = AssetDatabase.LoadAssetAtPath<GameObject>(IthappyArt.Props + "/sign_default.prefab");
            if (sign != null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(sign, root.transform);
                go.name = "Island sign";
                go.transform.SetPositionAndRotation(new Vector3(9f, IslandBuilder.Height(1, 9f, -17f), -17f), Quaternion.Euler(0, 180f, 0));
                Tag(go, 1);
            }

            // ---- the pond (all chapters) and the lake (chapter 2): lily pads and reeds ----
            Pads(root, IslandShape.Pond, IslandShape.PondR - 2.5f, 7, 1, lily, reed, P("pond"), 11);
            Pads(root, IslandShape.Lake, 16f * IslandShape.K, 14, 2, lily, reed, P("lake"), 23);
            var willow = AssetDatabase.LoadAssetAtPath<GameObject>(IthappyArt.Nature + "/tree_leafy_6.prefab");
            if (willow != null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(willow, root.transform);
                go.name = "Islet tree";
                var at = IslandShape.Islet;
                go.transform.SetPositionAndRotation(new Vector3(at.x, IslandBuilder.Height(2, at.x, at.y) - 0.15f, at.y), Quaternion.Euler(0, 40f, 0));
                go.transform.localScale = Vector3.one * 1.3f;
                Tag(go, 2);
            }

            // ---- chapter 2: the cove's pier and fishing boats, stepping stones across the river ----
            var cove = Shore(2, 226f, 0.5f);
            var coveOut = (new Vector3(cove.x, 0, cove.z) - Vector3.zero).normalized;
            var pier = new GameObject("Cove pier");
            pier.transform.SetParent(root.transform, false);
            Pier(pier, cove - coveOut * 6f, coveOut, 26f, 1.6f, 1.25f, wood, P("pier"));
            var side = new Vector3(coveOut.z, 0, -coveOut.x);
            Boat(pier, cove + coveOut * 12f + side * 3.4f, Quaternion.LookRotation(coveOut), 6.5f, white, blue, P("fishboat"));
            Boat(pier, cove + coveOut * 19f - side * 3.6f, Quaternion.LookRotation(-coveOut) * Quaternion.Euler(0, 10f, 0), 5.8f, white, red, P("fishboat"));
            Tag(pier, 2);
            SteppingStones(root, new Vector2(-139f, -44f) * IslandShape.K, stone, P("stones"));

            // ---- chapter 3: the mountain lake, the waterfall, the harbour, the town square, the lookout ----
            var ml = IslandShape.MountainLake;
            var lakeGo = Disc(root, "Mountain lake", new Vector3(ml.x, IslandShape.MountainLakeLevel, ml.y), IslandShape.MountainLakeR - 0.5f, 40, MountainLakeWater(gen, matDir, waterMat), P("mlake"));
            lakeGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Tag(lakeGo, 3);
            Waterfall(root, gen, matDir, P("falls"));

            var harbour = IslandPaths.Harbour();
            Breakwater(root, harbour, stone, P("breakwater"));
            Lighthouse(root, harbour, white, red, lamp, P("lighthouse"));

            var sq = IslandShape.Plateau;
            var square = Disc(root, "Town square", new Vector3(sq.x, IslandBuilder.Height(3, sq.x, sq.y) + 0.04f, sq.y), 12.5f, 48, paving, P("square"));
            square.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Tag(square, 3);
            var fountain = AssetDatabase.LoadAssetAtPath<GameObject>(IthappyArt.Props + "/decor_fountain.prefab");
            if (fountain != null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(fountain, root.transform);
                go.name = "Town fountain";
                go.transform.position = new Vector3(sq.x, IslandBuilder.Height(3, sq.x, sq.y), sq.y);
                go.transform.localScale *= 1.6f;
                Tag(go, 3);
            }
            var bench = AssetDatabase.LoadAssetAtPath<GameObject>(IthappyArt.Props + "/decor_bench.prefab");
            if (bench != null)
            {
                var at = new Vector2(-41.5f, 253f) * IslandShape.K;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(bench, root.transform);
                go.name = "Lookout bench";
                go.transform.SetPositionAndRotation(new Vector3(at.x, IslandBuilder.Height(3, at.x, at.y), at.y), Quaternion.Euler(0, -90f, 0));
                Tag(go, 3);
            }
            // ---- chapter 2: the caravan park by the causeway (campers parked for good, picnic tables, a campfire) ----
            if (IslandPaths.HasCamp) CaravanPark(root);

            // ---- the events' dressing (December's Winter Festival, October's Zombie Night): root objects the
            //      island builder moves into their own scenes, loaded on top of the island by IslandFestival ----
            Festival(root);
            Halloween();

            return waterRenderers;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The sea's water for the high lake: its own depth (the sea's mask is for sea level) and hardly any swell.</summary>
        static Material MountainLakeWater(string gen, string matDir, Material sea)
        {
            var path = $"{gen}/mountain_lake_mask.png";
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = new Color(0.42f, 0.6f, 0.42f, 1f);
            t.SetPixels(px); t.Apply();
            System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = false; imp.mipmapEnabled = false; imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            var mpath = $"{matDir}/Water_MountainLake.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mpath);
            if (m == null) { m = new Material(sea); AssetDatabase.CreateAsset(m, mpath); }
            else m.CopyPropertiesFromMaterial(sea);
            m.SetTexture("_Mask", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            if (m.HasProperty("_WaveAmp")) m.SetFloat("_WaveAmp", m.GetFloat("_WaveAmp") * 0.12f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Two rows of pitches on the lot beside the causeway: the Farm pack's caravans and camper vans parked nose
        /// out, a picnic table and chairs at each, a campfire, toilets and a sign at the gate.
        /// </summary>
        static void CaravanPark(GameObject root)
        {
            var park = new GameObject("Caravan park");
            park.transform.SetParent(root.transform, false);
            var c = IslandPaths.CampPos;
            var rot = Quaternion.Euler(0, IslandPaths.CampYaw, 0);
            park.transform.SetPositionAndRotation(c, rot);
            void Put(GameObject? prefab, float x, float z, float yaw, Transform? parent = null)
            {
                if (prefab == null) return;
                var go = IthappyArt.Inst(prefab, parent ?? park.transform);
                go.transform.localPosition = new Vector3(x, 0, z);
                go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                // stand on the ground there
                var w = go.transform.position;
                w.y = IslandBuilder.Height(2, w.x, w.z);
                go.transform.position = w;
            }
            GameObject? V(string n) => AssetDatabase.LoadAssetAtPath<GameObject>(IthappyArt.Vehicles + "/" + n + ".prefab");
            var campers = new[] { V("trailer_004"), V("bus_001"), V("trailer_005"), V("bus_002"), V("bus_004"), V("trailer_004") };
            // local frame: +Z looks at the road; pitches 4 m apart along x, the back row behind the front row
            for (int i = 0; i < 3; i++)
            {
                float x = -IslandPaths.CampHalfX + 4f + i * 8f;
                Put(campers[i], x, 3f, 0f);
                Put(IthappyArt.Pack("table_001"), x + 2.6f, 4.6f, 90f);
                Put(IthappyArt.Pack("chair_001"), x + 2.6f, 5.8f, 180f);
                Put(campers[i + 3], x + 1.5f, -4.5f, 180f);
            }
            Put(IthappyArt.Pack("firewood_002"), 2f, -0.6f, 30f);
            Put(IthappyArt.Pack("table_002"), -2f, -0.8f, 0f);
            Put(IthappyArt.Pack("toilet_001"), IslandPaths.CampHalfX - 1.5f, -5.5f, -90f);
            Put(IthappyArt.Pack("toilet_002"), IslandPaths.CampHalfX - 1.5f, -3.8f, -90f);
            Put(IthappyArt.Pack("pointer_002"), IslandPaths.CampHalfX - 0.5f, IslandPaths.CampHalfZ - 0.5f, 0f);
            Put(IthappyArt.Pack("barrel_001"), -IslandPaths.CampHalfX + 0.8f, -6.5f, 0f);
            foreach (var col in park.GetComponentsInChildren<Collider>()) col.enabled = false;
            Tag(park, 2);
        }

        /// <summary>Built by <see cref="Festival"/> for the island builder to hand to IslandFestival.</summary>
        public static GameObject? FestivalDecor;
        /// <summary>Built by <see cref="Halloween"/>: October's Zombie Night dressing.</summary>
        public static GameObject? HalloweenDecor;

        /// <summary>
        /// October: pumpkin piles and hay bales on the crossroads' corners, dead trees, the Zombies pack's road
        /// sign, and the Farm pack's low fog drifting over the fields.
        /// </summary>
        static void Halloween()
        {
            var decor = new GameObject("Zombie Night");
            HalloweenDecor = decor;
            GameObject? A(string path) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Farmverse/Content/Art/" + path + ".prefab");
            var pumpkin = A("Goods/good_pumpkin");
            var hay = A("Props/decor_hay_bales");
            var dead = new[] { A("Nature/dead_tree_1"), A("Nature/dead_tree_2") };
            var sign = A("Weapons/RoadSign");
            var fog = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ithappy/Farm/VFX/FX_Prefabs/FX_Fog.prefab");
            void Put(GameObject? prefab, Vector2 at, float yaw, int chapter, float scale = 1f)
            {
                if (prefab == null) return;
                var go = IthappyArt.Inst(prefab, decor.transform);
                go.transform.SetPositionAndRotation(new Vector3(at.x, IslandBuilder.Height(chapter, at.x, at.y), at.y), Quaternion.Euler(0, yaw, 0));
                go.transform.localScale *= scale;
                foreach (var col in go.GetComponentsInChildren<Collider>()) col.enabled = false;
                Tag(go, chapter);
            }
            int k = 0;
            foreach (var gx in IslandPaths.GridX)
                foreach (var gz in IslandPaths.GridZ)
                {
                    int ch = IslandShape.FirstChapter(gx, gz, 6f);
                    if (ch > 3 || IslandShape.IsMountain(gx, gz) || IslandBuilder.Height(3, gx, gz) < IslandShape.Sea + 1f) continue;
                    if (IslandPaths.RoadDist(gx, gz) > 1.5f && IslandPaths.TrackDist(gx, gz) > 1.5f) continue;
                    float hx = (System.Array.IndexOf(IslandPaths.MainX, gx) >= 0 ? IslandPaths.RoadHalf : IslandPaths.TrackHalf) + 1.1f;
                    float hz = (System.Array.IndexOf(IslandPaths.MainZ, gz) >= 0 ? IslandPaths.RoadHalf : IslandPaths.TrackHalf) + 1.1f;
                    var c = new Vector2(gx, gz);
                    // the corner the winter tree doesn't use, so both can stand at once in the editor
                    var corner = new Vector2((k & 1) == 0 ? -hx : hx, (k & 2) == 0 ? -hz : hz);
                    for (int i = 0; i < 3; i++) Put(pumpkin, c + corner + new Vector2(Mathf.Cos(i * 2.1f), Mathf.Sin(i * 2.1f)) * 0.5f, i * 70f, ch, 1.6f);
                    if (k % 2 == 0) Put(hay, c - corner, k * 23f, ch);
                    if (k % 3 == 1) Put(dead[k % 2], c + new Vector2(corner.x, -corner.y) * 1.2f, k * 41f, ch);
                    if (k % 4 == 2) Put(sign, c + new Vector2(-corner.x, corner.y), 180f + k * 10f, ch, 2.5f);
                    if (k % 2 == 1) Put(fog, c + corner * 3f, 0f, ch);
                    k++;
                }
            decor.SetActive(true);
        }
        public static Animator? FestivalSanta;

        /// <summary>
        /// The Christmas pack's outdoor trees, snowmen and presents on the corners of the farm's crossroads (the
        /// corners no building may take), the big tree with Santa at the main crossing and another on the town
        /// square. Hidden until December.
        /// </summary>
        static void Festival(GameObject root)
        {
            const string X = "Assets/ithappy/Christmas_Pack_FREE/Prefabs/";
            GameObject? Xm(string n) => AssetDatabase.LoadAssetAtPath<GameObject>(X + n + ".prefab");
            var decor = new GameObject("Winter Festival");   // a root: it goes to the Event_WinterFestival scene
            FestivalDecor = decor;
            FestivalSanta = null;
            GameObject? Put(GameObject? prefab, Vector2 at, float yaw, int chapter)
            {
                if (prefab == null) return null;
                var go = IthappyArt.Inst(prefab, decor.transform);
                go.transform.SetPositionAndRotation(new Vector3(at.x, IslandBuilder.Height(chapter, at.x, at.y), at.y), Quaternion.Euler(0, yaw, 0));
                foreach (var col in go.GetComponentsInChildren<Collider>()) col.enabled = false;
                Tag(go, chapter);
                return go;
            }
            var bigTree = Xm("Christmass_tree_outdoor_002_toys") ?? Xm("Christmass_tree_outdoor_002");
            var tree = Xm("Christmass_tree_outdoor_001_toys") ?? Xm("Christmass_tree_outdoor_001");
            var snowman = Xm("Snowman_001");
            var presents = new[] { Xm("Present_001"), Xm("Present_002"), Xm("Present_003"), Xm("Present_004"), Xm("Present_005"), Xm("Present_006") };
            int k = 0;
            float mx = IslandPaths.MainX[0], mz = IslandPaths.MainZ[0];
            foreach (var gx in IslandPaths.GridX)
                foreach (var gz in IslandPaths.GridZ)
                {
                    int ch = IslandShape.FirstChapter(gx, gz, 6f);
                    if (ch > 3 || IslandShape.IsMountain(gx, gz) || IslandBuilder.Height(3, gx, gz) < IslandShape.Sea + 1f) continue;
                    if (IslandPaths.RoadDist(gx, gz) > 1.5f && IslandPaths.TrackDist(gx, gz) > 1.5f) continue;   // no crossing here
                    float hx = (System.Array.IndexOf(IslandPaths.MainX, gx) >= 0 ? IslandPaths.RoadHalf : IslandPaths.TrackHalf) + 1.1f;
                    float hz = (System.Array.IndexOf(IslandPaths.MainZ, gz) >= 0 ? IslandPaths.RoadHalf : IslandPaths.TrackHalf) + 1.1f;
                    bool main = Mathf.Approximately(gx, mx) && Mathf.Approximately(gz, mz);
                    if (main)
                    {
                        Put(bigTree, new Vector2(gx + hx + 0.6f, gz + hz + 0.6f), 20f, ch);
                        for (int i = 0; i < presents.Length; i++)
                            Put(presents[i], new Vector2(gx + hx + 0.6f, gz + hz + 0.6f) + new Vector2(Mathf.Cos(i * 1.05f), Mathf.Sin(i * 1.05f)) * 1.5f, i * 47f, ch);
                        var santaPrefab = Xm("Santa");
                        var santa = Put(santaPrefab, new Vector2(gx - hx, gz + hz), 135f, ch);
                        if (santa != null)
                        {
                            santa.name = "Santa";
                            var anim = santa.GetComponentInChildren<Animator>() ?? santa.AddComponent<Animator>();
                            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(IthappyCharacters.VillagerController);
                            anim.applyRootMotion = false;
                            FestivalSanta = anim;
                        }
                        Put(snowman, new Vector2(gx + hx, gz - hz), -30f, ch);
                        Put(snowman, new Vector2(gx - hx, gz - hz), 40f, ch);
                        continue;
                    }
                    // the other crossings: a tree or a snowman on one corner, turn about
                    var corner = new Vector2((k & 1) == 0 ? hx : -hx, (k & 2) == 0 ? hz : -hz);
                    Put((k % 3 == 0) ? tree : snowman, new Vector2(gx, gz) + corner, k * 37f, ch);
                    k++;
                }
            // the town square's tree (chapter 3)
            var sq = IslandShape.Plateau;
            Put(bigTree, sq + new Vector2(7f, 4f), 0f, 3);
        }

        static void Tag(GameObject go, int from, int until = 99)
        {
            var t = go.GetComponent<IslandChapterTag>() ?? go.AddComponent<IslandChapterTag>();
            t.fromChapter = from; t.untilChapter = until;
        }

        static Material Plain(string path, Color c, float smooth)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The first point from the middle outwards, at this angle, where chapter <paramref name="c"/>'s ground drops to this height.</summary>
        static Vector3 Shore(int c, float deg, float h)
        {
            float a = deg * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            float r = 20f;
            while (r < 310f && IslandBuilder.Height(c, dir.x * r, dir.y * r) > Sea + h) r += 0.5f;
            return new Vector3(dir.x * r, Sea, dir.y * r);
        }

        static GameObject Mesh(GameObject parent, string name, Mesh m, Material mat, string path, bool shadows = true)
        {
            AssetDatabase.CreateAsset(m, path);
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A plank pier on piles from the shore out over the water; walkable (it has a collider).</summary>
        static void Pier(GameObject parent, Vector3 from, Vector3 dir, float length, float halfWidth, float deck, Material wood, string path)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            dir.y = 0; dir = dir.normalized;
            var side = new Vector3(dir.z, 0, -dir.x);
            var mid = from + dir * (length / 2f);
            IslandPaths.Box(v, uv, tri, new Vector3(mid.x, deck, mid.z), side * halfWidth, Vector3.up * 0.09f, dir * (length / 2f));
            for (float d = 1f; d <= length; d += 3f)
            {
                var p = from + dir * d;
                foreach (float s in new[] { -1f, 1f })
                {
                    var q = p + side * s * (halfWidth - 0.15f);
                    IslandPaths.Box(v, uv, tri, new Vector3(q.x, (deck - 3f) / 2f, q.z), side * 0.13f, Vector3.up * ((deck + 3f) / 2f), dir * 0.13f);
                    IslandPaths.Box(v, uv, tri, new Vector3(q.x, deck + 0.45f, q.z), side * 0.07f, Vector3.up * 0.4f, dir * 0.07f);   // post
                }
            }
            foreach (float s in new[] { -1f, 1f })
            {
                var q = mid + side * s * (halfWidth - 0.1f);
                IslandPaths.Box(v, uv, tri, new Vector3(q.x, deck + 0.85f, q.z), side * 0.05f, Vector3.up * 0.05f, dir * (length / 2f));   // rail
            }
            var go = Mesh(parent, "Pier", IslandPaths.Finish(v, uv, tri, "pier"), wood, path);
            // walkable: a flat deck the ground test trusts, running from the beach to the end
            var start = from - dir * 3f; start.y = deck - 0.6f;
            var span = new List<Vector3> { start, new Vector3(from.x, deck, from.z) };
            for (float d = 2f; d <= length; d += 2f) { var q = from + dir * d; span.Add(new Vector3(q.x, deck, q.z)); }
            IslandPaths.WalkDeck(go, span, halfWidth - 0.15f, 0.09f, path.Replace(".asset", "_walk.asset"));
        }

        /// <summary>A little boat from boxes: a tapered hull, a painted strake, thwarts; it bobs on the water.</summary>
        static void Boat(GameObject parent, Vector3 at, Quaternion turn, float length, Material hull, Material trim, string path)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            float w = length * 0.2f, h = length * 0.1f;
            // hull: a loft of rectangles narrowing to the bow and stern
            int rings = 9; int b0 = v.Count;
            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)(rings - 1), z = (t - 0.5f) * length;
                float k = Mathf.Sin(Mathf.PI * Mathf.Lerp(0.06f, 0.94f, t));
                float half = w * Mathf.Pow(k, 0.7f), keel = -h * k, gun = h * (0.6f + 0.5f * Mathf.Abs(t - 0.5f) * 2f);
                v.Add(new Vector3(-half, gun, z)); v.Add(new Vector3(-half * 0.6f, keel, z)); v.Add(new Vector3(half * 0.6f, keel, z)); v.Add(new Vector3(half, gun, z));
                for (int q = 0; q < 4; q++) uv.Add(new Vector2(q / 3f, t * 3f));
                if (i == 0) continue;
                int r0 = b0 + (i - 1) * 4, r1 = b0 + i * 4;
                for (int q = 0; q < 3; q++)
                {
                    tri.Add(r0 + q); tri.Add(r1 + q); tri.Add(r0 + q + 1);
                    tri.Add(r0 + q + 1); tri.Add(r1 + q); tri.Add(r1 + q + 1);
                }
            }
            for (int i = 1; i < rings - 1; i += 2)
            {
                float t = i / (float)(rings - 1), z = (t - 0.5f) * length;
                IslandPaths.Box(v, uv, tri, new Vector3(0, h * 0.35f, z), new Vector3(w * 0.8f * Mathf.Sin(Mathf.PI * t), 0, 0), Vector3.up * 0.04f, new Vector3(0, 0, 0.12f));
            }
            var root = new GameObject("Boat");
            root.transform.SetParent(parent.transform, false);
            root.transform.SetPositionAndRotation(at + Vector3.up * 0.1f, turn);
            var m = IslandPaths.Finish(v, uv, tri, "boat");
            AssetDatabase.CreateAsset(m, path);
            var go = new GameObject("Hull");
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = hull;
            // the painted strake: a thin band along the gunwale
            var sv = new List<Vector3>(); var suv = new List<Vector2>(); var stri = new List<int>();
            for (int s = -1; s <= 1; s += 2)
                IslandPaths.Box(sv, suv, stri, new Vector3(s * w * 0.98f, h * 0.75f, 0), new Vector3(0.03f, 0, 0), Vector3.up * h * 0.12f, new Vector3(0, 0, length * 0.36f));
            var sm = IslandPaths.Finish(sv, suv, stri, "strake");
            AssetDatabase.CreateAsset(sm, path.Replace(".asset", "_trim.asset"));
            var st = new GameObject("Trim");
            st.transform.SetParent(root.transform, false);
            st.AddComponent<MeshFilter>().sharedMesh = sm;
            st.AddComponent<MeshRenderer>().sharedMaterial = trim;
            root.AddComponent<IslandBob>();
        }

        /// <summary>Flat lily pads on the water and reeds round the edge.</summary>
        static void Pads(GameObject parent, Vector2 centre, float radius, int count, int chapter, Material lily, Material reed, string path, int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i < count; i++)
            {
                float a = R() * Mathf.PI * 2f, d = Mathf.Sqrt(R()) * radius;
                var c = new Vector3(centre.x + Mathf.Cos(a) * d, Sea + 0.04f, centre.y + Mathf.Sin(a) * d);
                float s = 0.45f + R() * 0.5f;
                int b = v.Count; v.Add(c); uv.Add(new Vector2(0.5f, 0.5f));
                const int seg = 10;
                for (int k = 0; k <= seg; k++)
                {
                    float t = 0.35f + k / (float)seg * (Mathf.PI * 2f - 0.7f) + a;      // a notch cut out, like a real pad
                    v.Add(c + new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t)) * s); uv.Add(new Vector2(0.5f + Mathf.Cos(t) * 0.5f, 0.5f + Mathf.Sin(t) * 0.5f));
                    if (k > 0) { tri.Add(b); tri.Add(b + k + 1); tri.Add(b + k); }
                }
            }
            var pads = Mesh(parent, $"Lily pads ({chapter})", IslandPaths.Finish(v, uv, tri, "lily pads"), lily, path, false);
            Tag(pads, chapter);
            // reeds: thin blades in clumps where the ground meets the water
            var rv = new List<Vector3>(); var ruv = new List<Vector2>(); var rtri = new List<int>();
            for (int i = 0; i < count * 5; i++)
            {
                float a = R() * Mathf.PI * 2f;
                Vector3 at = default; bool found = false;
                for (float d = radius; d < radius * 3f + 20f; d += 0.5f)
                {
                    float x = centre.x + Mathf.Cos(a) * d, z = centre.y + Mathf.Sin(a) * d;
                    float h = IslandBuilder.Height(chapter, x, z);
                    if (h > Sea - 0.1f) { at = new Vector3(x, h, z); found = true; break; }
                }
                if (!found || at.y > Sea + 0.6f) continue;
                for (int k = 0; k < 5; k++)
                {
                    var p = at + new Vector3((R() - 0.5f) * 0.8f, -0.1f, (R() - 0.5f) * 0.8f);
                    float hh = 0.7f + R() * 0.7f;
                    var lean = new Vector3((R() - 0.5f) * 0.25f, 0, (R() - 0.5f) * 0.25f);
                    IslandPaths.Box(rv, ruv, rtri, p + Vector3.up * hh / 2f + lean * 0.5f, new Vector3(0.025f, 0, 0), Vector3.up * hh / 2f, new Vector3(0, 0, 0.025f));
                }
            }
            if (rv.Count > 0) Tag(Mesh(parent, $"Reeds ({chapter})", IslandPaths.Finish(rv, ruv, rtri, "reeds"), reed, path.Replace(".asset", "_reeds.asset"), false), chapter);
        }

        static void SteppingStones(GameObject parent, Vector2 at, Material stone, string path)
        {
            // across the river where it is narrow: flat stones a stride apart
            IslandShape.RiverDist(at.x, at.y, out var along);
            int i = Mathf.Clamp(Mathf.FloorToInt(along * (IslandShape.River.Length - 1)), 0, IslandShape.River.Length - 2);
            var flow = (IslandShape.River[i + 1] - IslandShape.River[i]).normalized;
            var across = new Vector3(-flow.y, 0, flow.x);
            float half = IslandShape.RiverHalf(along) + 2f;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            var rng = new System.Random(5);
            for (float d = -half; d <= half; d += 1.4f)
            {
                var c = new Vector3(at.x, Sea + 0.18f, at.y) + across * d + new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 0.4f;
                IslandPaths.Box(v, uv, tri, c, across * 0.45f, Vector3.up * 0.35f, new Vector3(flow.x, 0, flow.y) * 0.4f);
            }
            Tag(Mesh(parent, "Stepping stones", IslandPaths.Finish(v, uv, tri, "stepping stones"), stone, path), 2);
        }

        static GameObject Disc(GameObject parent, string name, Vector3 centre, float radius, int seg, Material mat, string path)
        {
            var v = new List<Vector3> { centre }; var uv = new List<Vector2> { new Vector2(centre.x, centre.z) / 4f }; var tri = new List<int>();
            for (int k = 0; k <= seg; k++)
            {
                float t = k / (float)seg * Mathf.PI * 2f;
                var p = centre + new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t)) * radius;
                v.Add(p); uv.Add(new Vector2(p.x, p.z) / 4f);
                if (k > 0) { tri.Add(0); tri.Add(k + 1); tri.Add(k); }
            }
            return Mesh(parent, name, IslandPaths.Finish(v, uv, tri, name), mat, path);
        }

        /// <summary>The falls: a sheet of falling water over the cliff (a scrolling texture), spray and mist at its foot.</summary>
        static void Waterfall(GameObject parent, string gen, string matDir, string path)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{matDir}/Waterfall.mat");
            var shader = Shader.Find("Farmverse/Waterfall");
            if (mat == null) { mat = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(mat, $"{matDir}/Waterfall.mat"); }
            if (shader != null) mat.shader = shader;
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "water_river_albedo.png"));
            mat.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "foam_shore_albedo.png"));
            EditorUtility.SetDirty(mat);

            float x0 = IslandShape.MountainLake.x;
            // follow the cliff face down its middle, a little out from the rock
            var line = new List<Vector3>();
            for (float z = IslandShape.CliffTop + 0.6f; z >= IslandShape.CliffFoot - 2.5f; z -= 0.25f)
            {
                float h = IslandBuilder.Height(3, x0, z);
                line.Add(new Vector3(x0, Mathf.Max(h, Sea - 0.2f), z));
            }
            var pts = new List<Vector3>();
            float total = 0;
            for (int i = 0; i < line.Count; i++)
            {
                var t = (i < line.Count - 1 ? line[i + 1] - line[i] : line[i] - line[i - 1]).normalized;
                var outward = new Vector3(0, t.z, -t.y).normalized;     // perpendicular to the face, away from the rock
                if (outward.z > 0) outward = -outward;
                pts.Add(line[i] + outward * 0.7f + Vector3.up * (i == 0 ? 0.25f : 0f));
            }
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>(); var col = new List<Color>();
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) total += Vector3.Distance(pts[i], pts[i - 1]);
                float t = i / (float)(pts.Count - 1);
                float half = Mathf.Lerp(3.4f, 4.4f, t);
                for (int k = 0; k <= 6; k++)
                {
                    float u = k / 6f;
                    var p = pts[i] + Vector3.right * Mathf.Lerp(-half, half, u);
                    v.Add(p); uv.Add(new Vector2(u * 1.6f, total / 6f));
                    float edge = Mathf.Sin(u * Mathf.PI);
                    col.Add(new Color(1, 1, 1, Mathf.Clamp01(edge * 1.4f) * Mathf.Lerp(0.95f, 0.7f, t)));
                }
                if (i == 0) continue;
                int r0 = (i - 1) * 7, r1 = i * 7;
                for (int k = 0; k < 6; k++) { tri.Add(r0 + k); tri.Add(r0 + k + 1); tri.Add(r1 + k); tri.Add(r0 + k + 1); tri.Add(r1 + k + 1); tri.Add(r1 + k); }
            }
            var m = new Mesh { name = "waterfall" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            var go = new GameObject("Waterfall");
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            Tag(go, 3);

            // spray where it lands, and a drifting mist above the plunge pool
            var foot = pts[pts.Count - 1];
            foreach (var (name, rate, size, speed, life, alpha) in new[] { ("Spray", 26f, 1.4f, 4.5f, 1.4f, 0.75f), ("Mist", 6f, 5.5f, 1.2f, 4.5f, 0.35f) })
            {
                var fx = new GameObject(name);
                fx.transform.SetParent(go.transform, false);
                fx.transform.position = new Vector3(x0, Sea + 0.3f, foot.z - 1f);
                var ps = fx.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
                main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
                main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
                main.startColor = new Color(1f, 1f, 1f, alpha);
                main.gravityModifier = name == "Spray" ? 0.35f : -0.02f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 200;
                var em = ps.emission; em.rateOverTime = rate;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(7f, 0.5f, 2f);
                sh.rotation = new Vector3(-70f, 0, 0);
                var colt = ps.colorOverLifetime; colt.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                          new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1) });
                colt.color = g;
                var sz = ps.sizeOverLifetime; sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 1.6f)));
                var r = fx.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Farmverse/Content/Island/Smoke.mat");
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        /// <summary>A breakwater of big stones curving out from the shore west of the harbour.</summary>
        static void Breakwater(GameObject parent, Vector2 harbour, Material stone, string path)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            var rng = new System.Random(9);
            float R() => (float)rng.NextDouble();
            var start = new Vector2(harbour.x - 30f, harbour.y + 4f);
            for (float t = 0; t <= 1f; t += 0.025f)
            {
                // out to sea, then bending east across the harbour mouth
                var p = start + new Vector2(Mathf.Sin(t * Mathf.PI * 0.5f) * 34f, -Mathf.Sin(t * Mathf.PI * 0.9f) * 30f - t * 8f);
                var c = new Vector3(p.x, -0.4f + R() * 0.3f, p.y);
                float s = 1.4f + R() * 1.1f;
                var turn = Quaternion.Euler(R() * 20f - 10f, R() * 360f, R() * 20f - 10f);
                IslandPaths.Box(v, uv, tri, c + Vector3.up * s * 0.5f, turn * Vector3.right * s, turn * Vector3.up * (s * 0.9f), turn * Vector3.forward * (s * 0.8f));
            }
            Tag(Mesh(parent, "Breakwater", IslandPaths.Finish(v, uv, tri, "breakwater"), stone, path), 3);
        }

        /// <summary>A white lighthouse with red bands on the point east of the harbour; its lamp turns at night.</summary>
        static void Lighthouse(GameObject parent, Vector2 harbour, Material white, Material red, Material lamp, string path)
        {
            // stand it on the shore a little east of the harbour
            float x = harbour.x + 36f, z = harbour.y;
            while (z < 0 && IslandShape.Inland(3, x, z) < 5f) z += 0.5f;
            float y = IslandBuilder.Height(3, x, z);
            var root = new GameObject("Lighthouse");
            root.transform.SetParent(parent.transform, false);
            root.transform.position = new Vector3(x, y - 0.2f, z);
            const float H = 13f;
            Mesh Tower(float y0, float y1, float r0, float r1, int seg)
            {
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
                for (int k = 0; k <= seg; k++)
                {
                    float t = k / (float)seg * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t));
                    v.Add(d * r0 + Vector3.up * y0); v.Add(d * r1 + Vector3.up * y1);
                    uv.Add(new Vector2(k / (float)seg, 0)); uv.Add(new Vector2(k / (float)seg, 1));
                    if (k == 0) continue;
                    int a = (k - 1) * 2, b = k * 2;
                    tri.Add(a); tri.Add(a + 1); tri.Add(b); tri.Add(b); tri.Add(a + 1); tri.Add(b + 1);
                }
                // a lid
                int c0 = v.Count; v.Add(Vector3.up * y1); uv.Add(Vector2.zero);
                for (int k = 0; k <= seg; k++) { float t = k / (float)seg * Mathf.PI * 2f; v.Add(new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t)) * r1 + Vector3.up * y1); uv.Add(Vector2.zero); if (k > 0) { tri.Add(c0); tri.Add(c0 + k + 1); tri.Add(c0 + k); } }
                return IslandPaths.Finish(v, uv, tri, "tower");
            }
            float R(float yy) => Mathf.Lerp(2.3f, 1.5f, yy / H);
            int part = 0;
            void Part(string name, Mesh m, Material mat)
            {
                AssetDatabase.CreateAsset(m, path.Replace(".asset", $"_{part++}.asset"));
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            // white tower with two red bands
            float[] cuts = { 0f, 4f, 5.6f, 8.6f, 10.2f, H };
            for (int i = 0; i < cuts.Length - 1; i++) Part(i % 2 == 0 ? "Tower" : "Band", Tower(cuts[i], cuts[i + 1], R(cuts[i]), R(cuts[i + 1]), 20), i % 2 == 0 ? white : red);
            Part("Gallery", Tower(H, H + 0.25f, 2.1f, 2.1f, 20), red);
            Part("Lantern", Tower(H + 0.25f, H + 2.0f, 1.05f, 1.05f, 12), lamp);
            Part("Roof", Tower(H + 2.0f, H + 3.2f, 1.35f, 0.15f, 12), red);
            // the beam: a spot light that sweeps round, on when it gets dark
            var beam = new GameObject("Beam");
            beam.transform.SetParent(root.transform, false);
            beam.transform.localPosition = Vector3.up * (H + 1.1f);
            var light = beam.AddComponent<Light>();
            light.type = LightType.Spot; light.range = 140f; light.spotAngle = 22f; light.intensity = 0f;
            light.color = new Color(1f, 0.92f, 0.7f); light.shadows = LightShadows.None;
            beam.AddComponent<IslandLighthouse>();
            Tag(root, 3);
        }
    }
}
