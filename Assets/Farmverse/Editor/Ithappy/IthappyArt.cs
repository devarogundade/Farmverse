#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Farmverse.Presentation.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Farmverse.Editor
{
    /// <summary>
    /// Farmverse / Art / Build ITHappy Art: every building, crop, nature prop and decoration the game
    /// uses, built from the ITHappy Farm pack (Assets/ithappy/Farm/Prefabs) into Content/Art.
    ///  - Buildings: one prefab per catalogue id, the pack model (plus a few props) scaled to fit the
    ///    building's footprint in grid cells, a box collider for tapping, static flags on the body.
    ///    "id__style.prefab" is a restyle (another pack model of the same building).
    ///  - Crops: the plant model in a grid over the field at five sizes (Seeded → Ready), under a
    ///    StagedVisual so the economy shows growth.
    ///  - Props: decorations, fences, paths and the field, scaled to a size in metres.
    ///  - Nature: the trees, bushes, rocks, grass and mushrooms the island is scattered with.
    /// Re-running updates everything in place. The models stay nested pack prefabs, so a pack update
    /// flows through.
    /// </summary>
    public static class IthappyArt
    {
        public const string Art = "Assets/Farmverse/Content/Art";
        public const string Buildings = Art + "/Buildings";
        public const string Nature = Art + "/Nature";
        public const string Props = Art + "/Props";
        public const string Crops = Art + "/Crops";
        const string FarmPrefabs = "Assets/ithappy/Farm/Prefabs/";
        const string FoodPrefabs = "Assets/ithappy/Food_Free/Prefabs/";
        const string XmasPrefabs = "Assets/ithappy/Christmas_Pack_FREE/Prefabs/";
        public const float Cell = 2f;
        const string VisualCatalogPath = "Assets/Farmverse/Content/World/WorldVisualCatalog.asset";

        [MenuItem("Farmverse/Art/Build ITHappy Art", priority = 1)]
        public static void BuildAll()
        {
            Folder(Buildings); Folder(Nature); Folder(Props); Folder(Crops);
            int n = 0;
            n += BuildBuildings();
            n += BuildProps();
            n += BuildCrops();
            n += BuildNature();
            n += BuildWeapons();
            n += BuildVehicles();
            VisualCatalog();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ITHappy] {n} prefabs built under {Art}");
        }

        // ------------------------------------------------------------------ buildings

        /// <summary>An extra pack prop stood on a building: where on the main model's footprint (−1..1 of its half size,
        /// y as a fraction of its height) and how tall in metres.</summary>
        public struct Part
        {
            public string pack; public float ax, ay, az, yaw, height;
            public Part(string pack, float ax, float az, float height, float yaw = 0f, float ay = 0f)
            { this.pack = pack; this.ax = ax; this.az = az; this.height = height; this.yaw = yaw; this.ay = ay; }
        }

        public sealed class Building
        {
            public string id = ""; public int fx = 3, fz = 3; public string main = ""; public float yaw;
            public Part[] extras = Array.Empty<Part>(); public string[] styles = Array.Empty<string>();
            public string? rotor;   // an extra whose pack name is this spins (the windmill's sails)
            public bool metric;     // extras' heights are final metres (for a main model far from real scale, like a mountain)
            public float fill = 0.9f;
        }

        static Building B(string id, int fx, int fz, string main, float yaw = 180f, Part[]? extras = null, string[]? styles = null, string? rotor = null, float fill = 0.9f, bool metric = false)
            => new Building { id = id, fx = fx, fz = fz, main = main, yaw = yaw, extras = extras ?? Array.Empty<Part>(), styles = styles ?? Array.Empty<string>(), rotor = rotor, fill = fill, metric = metric };

        /// <summary>Every building the island sells, and the pack models it is made of (plan: Island buildings).</summary>
        public static readonly Building[] Catalogue =
        {
            B("farmhouse", 4, 4, "house_007", extras: new[] { new Part("barrel_002", 1.05f, -0.75f, 0.8f), new Part("barrel_008", 1.05f, -0.45f, 0.8f), new Part("cart_006", -1.1f, -0.7f, 0.8f, 60f), new Part("dray_002", -1.1f, 0.6f, 1.2f, 90f) }),
            B("barn", 4, 4, "hangar_002", extras: new[] { new Part("hay_001", 1.12f, -0.6f, 1.4f), new Part("hay_001", 1.12f, -0.15f, 1.4f, 90f), new Part("hay_002", 1.15f, 0.35f, 0.8f), new Part("tractor_001", -1.25f, 0.2f, 2.4f, 90f) }, styles: new[] { "hangar_015", "hangar_006" }),
            B("chicken_coop", 3, 3, "coop_001", extras: new[] { new Part("coop_002", 0.25f, 0f, 2.2f, 180f), new Part("case_001", -1.1f, 0.5f, 0.5f), new Part("bag_001", -1.1f, 0.05f, 0.55f) }),
            B("pigsty", 3, 3, "coop_001", extras: new[] { new Part("hangar_020", -0.35f, -0.2f, 2.3f, 180f), new Part("barrel_001", 0.55f, 0.45f, 0.8f) }),
            B("stable", 4, 3, "hangar_005", extras: new[] { new Part("hay_002", 1.12f, -0.5f, 0.8f), new Part("hay_001", 1.12f, 0.1f, 1.3f), new Part("dray_001", -1.2f, 0.2f, 1.3f, 90f) }, styles: new[] { "hangar_016" }),
            B("grain_silos", 2, 2, "tower_002", extras: new[] { new Part("hangar_012", 1.3f, 0.3f, 7f), new Part("bag_002", -1.2f, -0.6f, 0.6f), new Part("bag_003", -1.2f, -0.2f, 0.6f) }, styles: new[] { "tower_003" }),
            B("windmill", 3, 3, "tower_001", extras: new[] { new Part("bag_002", 0.9f, -0.95f, 0.6f), new Part("bag_002", 0.6f, -1.0f, 0.6f, 40f), new Part("dray_005", -1.1f, -0.6f, 1.2f, 60f) }, rotor: "tower_020"),
            B("greenhouse", 3, 3, "greenhouse_004", extras: new[] { new Part("cart_005", 1.15f, -0.6f, 0.8f, 30f), new Part("bucket_001", 1.15f, -0.1f, 0.45f) }, styles: new[] { "greenhouse_002", "greenhouse_003" }),
            B("potting_shed", 2, 2, "tower_011", extras: new[] { new Part("bag_001", 1.15f, -0.5f, 0.55f), new Part("bucket_003", 1.15f, 0.1f, 0.5f) }),
            B("packing_shed", 3, 3, "hangar_013", extras: new[] { new Part("case_001", -0.5f, 0.3f, 0.6f), new Part("case_003", 0.5f, 0.3f, 0.6f), new Part("tractor_002", 1.3f, 0.2f, 2.4f, 90f), new Part("trailer_003", -1.3f, 0.2f, 1.8f, 90f) }, styles: new[] { "hangar_017" }),
            B("cider_press", 3, 3, "hangar_011", extras: new[] { new Part("barrel_004", -1.1f, 0.5f, 1.0f), new Part("barrel_005", -1.1f, -0.3f, 1.0f) }),
            B("dairy_factory", 4, 3, "hangar_007", extras: new[] { new Part("bucket_001", -0.7f, 1.15f, 0.5f), new Part("barrel_009", 0.7f, 1.15f, 1.0f) }),
            B("bakery_factory", 3, 3, "house_001", extras: new[] { new Part("house_025", 0.55f, 0f, 1.6f, 0f, 0.92f) }),
            B("sugar_mill", 4, 3, "hangar_029"),
            B("feed_mill", 3, 3, "tower_010", extras: new[] { new Part("bag_001", 0.9f, 0.7f, 0.7f), new Part("bag_002", 1.1f, 0.2f, 0.7f), new Part("bag_003", 0.8f, -0.25f, 0.7f) }),
            B("lumber_yard", 4, 3, "hangar_010", extras: new[] { new Part("firewood_007", -1.0f, 0.7f, 1.6f), new Part("firewood_013", 1.0f, 0.7f, 0.9f), new Part("dray_004", 1.15f, -0.4f, 1.2f, 90f), new Part("firewood_010", -1.15f, -0.3f, 1.0f, 90f) }),
            B("fishing_shed", 2, 2, "house_031", extras: new[] { new Part("bucket_001", 1.0f, 1.0f, 0.5f) }),
            // the pack mountains are landscape-sized, so the quarry is one shrunk to a mound with real-sized gear in front
            B("mine_entrance", 3, 3, "mountain_001", metric: true, extras: new[] { new Part("cart_004", 0.9f, -1.1f, 0.9f, 150f), new Part("stone_fence_006", -0.9f, -1.05f, 0.45f), new Part("stone_fence_002", 0.15f, -1.2f, 0.6f, 20f), new Part("tower_006", -0.55f, 0.25f, 2.8f) }),
            B("market_stall", 2, 2, "stall_002", extras: new[] { new Part("prop.021", 1.1f, -0.4f, 0.5f), new Part("prop.029", 1.15f, 0.2f, 0.45f, 20f) }),
            B("grocer_shop", 2, 2, "stall_001", extras: new[] { new Part("table_001", 0f, 0.3f, 0.9f) }),
            B("bakery_shop", 3, 2, "house_027"),
            B("fish_market", 2, 2, "stall_001", extras: new[] { new Part("case_002", 0f, 0.3f, 0.5f) }),
            B("workers_house", 3, 3, "house_009", extras: new[] { new Part("cart_002", 1.1f, -0.6f, 0.8f, 40f), new Part("lawn_mower_002", 1.12f, 0.1f, 0.8f, 90f), new Part("barrel_006", -1.1f, -0.5f, 0.8f) }),
            B("army_house", 3, 3, "house_011", extras: new[] { new Part("barrel_010", 1.1f, -0.6f, 0.9f), new Part("barrel_012", 1.1f, -0.25f, 0.9f), new Part("prop.020", -1.1f, -0.5f, 0.7f) }),
            B("builders_hut", 2, 2, "house_028", extras: new[] { new Part("firewood_009", 1.0f, 0.8f, 0.6f), new Part("cart_001", -1.0f, 0.9f, 0.9f, -40f) }),
            B("police_station", 3, 3, "house_008"),
            B("fire_station", 3, 3, "hangar_004", extras: new[] { new Part("truck_006", 1.35f, 0.3f, 2.4f, 90f) }),
            B("clinic", 3, 3, "house_010"),
            B("school", 4, 2, "house_023"),
            B("library", 3, 3, "house_013"),
            B("chapel", 2, 2, "house_024"),
            B("community_hall", 4, 3, "house_005"),
            B("post_office", 2, 2, "house_017", extras: new[] { new Part("truck_005", 1.4f, 0.2f, 2.3f, 90f) }),
            B("bank", 3, 3, "house_018"),
            B("town_hall", 4, 4, "house_006"),
            B("facility_well", 2, 2, "tower_004", styles: new[] { "tower_008" }),
            B("facility_warehouse", 4, 3, "hangar_008", extras: new[] { new Part("prop.020", 1.1f, -0.7f, 0.7f), new Part("prop.021", 1.1f, -0.4f, 0.6f), new Part("prop.023", 1.12f, -0.1f, 0.7f), new Part("trailer_003", -1.25f, 0.2f, 1.8f, 90f) }),
        };

        static int BuildBuildings()
        {
            int n = 0;
            foreach (var b in Catalogue)
            {
                if (BuildBuilding(b, b.main, $"{Buildings}/{b.id}.prefab")) n++;
                for (int i = 0; i < b.styles.Length; i++)
                    if (BuildBuilding(b, b.styles[i], $"{Buildings}/{b.id}__{i + 1}.prefab", restyle: true)) n++;
            }
            return n;
        }

        static bool BuildBuilding(Building b, string mainPack, string path, bool restyle = false)
        {
            var main = Pack(mainPack);
            if (main == null) { Debug.LogWarning($"[ITHappy] {b.id}: no pack prefab {mainPack}"); return false; }
            var root = new GameObject(b.id);
            try
            {
                var model = new GameObject("Model");
                model.transform.SetParent(root.transform, false);
                var body = Inst(main, model.transform);
                body.name = "Body";
                body.transform.localRotation = Quaternion.Euler(0, b.yaw, 0);
                var mb = WorldBounds(body);
                if (b.rotor != null)
                    foreach (var t in body.GetComponentsInChildren<Transform>(true))
                        if (t.name == b.rotor) { t.name = b.rotor + "_rotor"; break; }
                // with metric extras, heights are what they'll be once the main model is fitted to its footprint
                float pre = Mathf.Min(b.fx * Cell * b.fill / Mathf.Max(mb.size.x, 0.01f), b.fz * Cell * b.fill / Mathf.Max(mb.size.z, 0.01f));
                if (!restyle)
                    foreach (var p in b.extras)
                    {
                        var pf = Pack(p.pack);
                        if (pf == null) { Debug.LogWarning($"[ITHappy] {b.id}: no pack prop {p.pack}"); continue; }
                        var go = Inst(pf, model.transform);
                        go.name = p.pack == b.rotor ? p.pack + "_rotor" : p.pack;
                        FitHeight(go, b.metric ? p.height / pre : p.height);
                        go.transform.localRotation = Quaternion.Euler(0, p.yaw, 0);
                        var pb = WorldBounds(go);
                        var at = new Vector3(mb.center.x + p.ax * mb.extents.x, mb.min.y + p.ay * mb.size.y, mb.center.z + p.az * mb.extents.z);
                        // stand the prop on that point (its own base at the point), centred on it
                        go.transform.position += at - new Vector3(pb.center.x, pb.min.y, pb.center.z);
                    }
                // the whole thing fits its footprint, stands on the ground, centred
                var all = WorldBounds(model);
                float s = Mathf.Min(b.fx * Cell * b.fill / Mathf.Max(all.size.x, 0.01f), b.fz * Cell * b.fill / Mathf.Max(all.size.z, 0.01f));
                model.transform.localScale = Vector3.one * s;
                all = WorldBounds(model);
                model.transform.localPosition -= new Vector3(all.center.x, all.min.y, all.center.z);
                all = WorldBounds(model);
                var box = root.AddComponent<BoxCollider>();
                box.center = all.center; box.size = all.size;
                foreach (var r in root.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
                Transform? rotor = null;
                foreach (var t in root.GetComponentsInChildren<Transform>()) if (t.name.EndsWith("_rotor")) rotor = t;
                foreach (var t in root.GetComponentsInChildren<Transform>())
                    if (rotor == null || !t.IsChildOf(rotor)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                if (rotor != null) { var sp = root.AddComponent<Spinner>(); Set(sp, "localAxis", Vector3.forward); Set(sp, "speed", -20f); }
                root.AddComponent<StagedVisual>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return true;
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ------------------------------------------------------------------ props (decor, fences, paths, field)

        /// <summary>A decoration or piece: the pack model scaled so its widest side is <c>size</c> metres (or, with
        /// <c>flat</c>, stretched to size × size on the ground and <c>thick</c> high).</summary>
        public sealed class Prop
        {
            public string id = ""; public string pack = ""; public float size = 1f; public bool flat; public float thick = 0.1f;
            public bool byHeight; public float yaw; public string? pack2; public bool xmas; public bool food; public Color? tint;
        }
        static Prop Pr(string id, string pack, float size, bool flat = false, float thick = 0.1f, bool byHeight = false, float yaw = 180f, string? pack2 = null, bool xmas = false, bool food = false, Color? tint = null)
            => new Prop { id = id, pack = pack, size = size, flat = flat, thick = thick, byHeight = byHeight, yaw = yaw, pack2 = pack2, xmas = xmas, food = food, tint = tint };

        public static readonly Prop[] PropList =
        {
            // the pack's ground pieces are all sand-coloured: tinted copies of their material make soil, dirt and stone
            Pr("field", "ground_011", 3 * Cell * 0.98f, flat: true, thick: 0.12f, tint: new Color(0.80f, 0.56f, 0.40f)),
            Pr("path_dirt", "ground_008", Cell, flat: true, thick: 0.06f, tint: new Color(0.92f, 0.77f, 0.55f)),
            Pr("path_stone", "road_001", Cell, flat: true, thick: 0.06f, tint: new Color(0.70f, 0.70f, 0.72f)),
            Pr("fence_wood", "fence_001", Cell),
            Pr("fence_white", "fence_008", Cell),
            Pr("pen", "coop_001", 3 * Cell * 0.95f),
            Pr("decor_bench", "table_002", 2.6f),
            Pr("decor_flower_bed", "house_026", 1.8f),
            Pr("decor_lamp_post", "Garland_002", 3.4f, byHeight: true, xmas: true),
            Pr("decor_mailbox", "prop.020", 1.0f, pack2: "prop.029"),
            Pr("decor_hay_bales", "hay_001", 1.8f, pack2: "hay_002"),
            Pr("decor_fountain", "tower_015", 2.4f),
            Pr("decor_beehive", "apiary_002", 1.3f, byHeight: true),
            Pr("decor_default", "lawn_mower_001", 1.5f),
            Pr("sign_default", "pointer_002", 2.6f, byHeight: true),
            Pr("seasonal_default", "Christmass_tree_outdoor_001", 3.2f, byHeight: true, xmas: true),
            // the depot the order trucks stop at (was the railway station)
            Pr("depot", "hangar_019", 12f),
        };

        static int BuildProps()
        {
            int n = 0;
            foreach (var p in PropList)
            {
                var pf = p.xmas ? Xmas(p.pack) : p.food ? FoodPack(p.pack) : Pack(p.pack);
                if (pf == null) { Debug.LogWarning($"[ITHappy] {p.id}: no pack prefab {p.pack}"); continue; }
                var root = new GameObject(p.id);
                try
                {
                    var model = new GameObject("Model");
                    model.transform.SetParent(root.transform, false);
                    var go = Inst(pf, model.transform);
                    go.name = "Body";
                    go.transform.localRotation = Quaternion.Euler(0, p.yaw, 0);
                    if (p.pack2 != null && Pack(p.pack2) is { } pf2)
                    {
                        var b1 = WorldBounds(go);
                        var go2 = Inst(pf2, model.transform);
                        go2.name = "Body 2";
                        go2.transform.localRotation = Quaternion.Euler(0, 35f, 0);
                        var b2 = WorldBounds(go2);
                        go2.transform.position += new Vector3(b1.max.x + b2.extents.x * 0.6f - b2.center.x, b1.min.y - b2.min.y, b1.center.z - b2.center.z);
                    }
                    var b = WorldBounds(model);
                    if (p.flat)
                        model.transform.localScale = new Vector3(p.size / Mathf.Max(b.size.x, 0.01f), p.thick / Mathf.Max(b.size.y, 0.01f), p.size / Mathf.Max(b.size.z, 0.01f));
                    else if (p.byHeight) model.transform.localScale = Vector3.one * (p.size / Mathf.Max(b.size.y, 0.01f));
                    else model.transform.localScale = Vector3.one * (p.size / Mathf.Max(Mathf.Max(b.size.x, b.size.z), 0.01f));
                    b = WorldBounds(model);
                    model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                    b = WorldBounds(model);
                    var box = root.AddComponent<BoxCollider>();
                    // flat pieces still need some depth to be hit at a low camera angle
                    var size = b.size; size.y = Mathf.Max(size.y, 0.3f);
                    box.center = new Vector3(b.center.x, size.y / 2f, b.center.z); box.size = size;
                    foreach (var r in root.GetComponentsInChildren<Renderer>())
                    {
                        r.shadowCastingMode = p.flat ? ShadowCastingMode.Off : ShadowCastingMode.On;
                        r.receiveShadows = true;
                        // ground slabs take a plain palette colour (the packs' own renders: flat sand, soil and grey road), other props a tinted copy of their material
                        if (p.tint is { } tint) r.sharedMaterials = r.sharedMaterials.Select(m => p.flat ? FlatMaterial(p.id + "_flat", tint, 0.12f) : Tinted(m, p.id, tint)).ToArray();
                    }
                    foreach (var t in root.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                    root.AddComponent<StagedVisual>();
                    PrefabUtility.SaveAsPrefabAsset(root, $"{Props}/{p.id}.prefab");
                    n++;
                }
                finally { Object.DestroyImmediate(root); }
            }
            return n;
        }

        public const string Materials = Art + "/Materials";

        /// <summary>A copy of a pack material with its base colour multiplied by <paramref name="tint"/>, saved once per prop.</summary>
        /// <summary>A plain URP Lit colour material under Content/Art/Materials (ground pieces, ridges, room walls).</summary>
        public static Material FlatMaterial(string id, Color colour, float smooth)
        {
            Folder(Materials);
            var path = $"{Materials}/{id}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", colour);
            mat.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material Tinted(Material source, string id, Color tint)
        {
            Folder(Materials);
            var path = $"{Materials}/{id}_{source.name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(source); AssetDatabase.CreateAsset(mat, path); }
            else mat.CopyPropertiesFromMaterial(source);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.GetColor("_BaseColor") * tint);
            else mat.color *= tint;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Logs the size of the pack prefabs named in Claude outputs/run/measure.txt (one per line; "xmas:"/"food:" prefixes).</summary>
        public static void MeasurePacks()
        {
            var file = Path.Combine(Directory.GetCurrentDirectory(), "Claude outputs", "run", "measure.txt");
            if (!File.Exists(file)) { Debug.LogWarning("[ITHappy] no measure.txt"); return; }
            foreach (var raw in File.ReadAllLines(file))
            {
                var name = raw.Trim(); if (name.Length == 0) continue;
                var pf = name.StartsWith("xmas:") ? Xmas(name.Substring(5)) : name.StartsWith("food:") ? FoodPack(name.Substring(5)) : name.StartsWith("weapon:") ? ZombieWeapon(name.Substring(7)) : name.StartsWith("furn:") ? Furn(name.Substring(5)) : Pack(name);
                if (pf == null) { Debug.Log($"[Measure] {name}: not found"); continue; }
                var root = new GameObject("measure");
                try { var b = WorldBounds(Inst(pf, root.transform)); Debug.Log($"[Measure] {name}: size {b.size.x:F2} x {b.size.y:F2} x {b.size.z:F2}, centre {b.center.x:F2},{b.center.y:F2},{b.center.z:F2}, min y {b.min.y:F2}"); }
                finally { Object.DestroyImmediate(root); }
            }
        }

        // ------------------------------------------------------------------ crops

        /// <summary>A crop: the growing plant, the ripe plant, how tall it ends up, and how many across a field.</summary>
        public sealed class CropArt { public string id = ""; public string grown = ""; public string ready = ""; public float height = 1f; public int across = 4; }
        static CropArt Cr(string id, string grown, string ready, float height, int across = 4) => new CropArt { id = id, grown = grown, ready = ready, height = height, across = across };

        public static readonly CropArt[] CropList =
        {
            Cr("wheat", "plant_010", "plant_011", 0.9f, 5),
            Cr("maize", "plant_001", "plant_001", 1.9f, 4),
            Cr("beans", "plant_009", "plant_009", 1.4f, 4),
            Cr("tomato", "plant_006", "plant_006", 1.4f, 4),
            Cr("sunflower", "plant_008", "plant_007", 1.5f, 4),
            Cr("cabbage", "plant_005", "plant_004", 0.45f, 4),
            Cr("pumpkin", "plant_003", "plant_002", 0.9f, 2),
        };

        static readonly (VisualStage stage, float size)[] Stages =
        {
            (VisualStage.Seeded, 0.18f), (VisualStage.Sprout, 0.38f), (VisualStage.Young, 0.62f), (VisualStage.Grown, 0.88f), (VisualStage.Ready, 1f),
        };

        static int BuildCrops()
        {
            int n = 0;
            foreach (var c in CropList)
            {
                var grown = Pack(c.grown); var ready = Pack(c.ready);
                if (grown == null || ready == null) { Debug.LogWarning($"[ITHappy] crop {c.id}: missing {c.grown}/{c.ready}"); continue; }
                var root = new GameObject("crop_" + c.id);
                try
                {
                    var model = new GameObject("Model");
                    model.transform.SetParent(root.transform, false);
                    float span = 3 * Cell * 0.8f;                       // the planted part of a 3×3 field
                    float pitch = c.across > 1 ? span / (c.across - 1) : 0f;
                    var rng = new System.Random(c.id.GetHashCode());
                    // ridge rows under the plants, the way the packs' own fields are drawn (raised earth rows along the field)
                    var ridges = new GameObject("Ridges");
                    ridges.transform.SetParent(model.transform, false);
                    var ridgeMat = FlatMaterial("field_ridge", new Color(0.70f, 0.47f, 0.33f), 0.1f);
                    for (int j = 0; j < c.across; j++)
                    {
                        var r = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Object.DestroyImmediate(r.GetComponent<Collider>());
                        r.name = "Ridge";
                        r.transform.SetParent(ridges.transform, false);
                        r.transform.localPosition = new Vector3(0, 0.09f, -span / 2 + j * pitch);
                        r.transform.localScale = new Vector3(span + 0.7f, 0.2f, Mathf.Min(0.62f, pitch * 0.55f));
                        r.GetComponent<Renderer>().sharedMaterial = ridgeMat;
                    }
                    foreach (var (stage, size) in Stages)
                    {
                        var st = new GameObject(stage.ToString());
                        st.transform.SetParent(model.transform, false);
                        var pf = stage == VisualStage.Ready ? ready : grown;
                        for (int j = 0; j < c.across; j++)
                            for (int i = 0; i < c.across; i++)
                            {
                                var go = Inst(pf, st.transform);
                                go.name = "Plant";
                                FitHeight(go, c.height * size);
                                var b = WorldBounds(go);
                                float x = -span / 2 + i * pitch + ((float)rng.NextDouble() - 0.5f) * pitch * 0.25f;
                                float z = -span / 2 + j * pitch;   // on its ridge
                                go.transform.localRotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                                b = WorldBounds(go);
                                go.transform.position += new Vector3(x - b.center.x, 0.17f - b.min.y, z - b.center.z);
                            }
                        st.SetActive(stage == VisualStage.Grown);
                    }
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
                    root.AddComponent<StagedVisual>();
                    var box = root.AddComponent<BoxCollider>();
                    box.center = new Vector3(0, c.height / 2f, 0); box.size = new Vector3(span + 0.4f, c.height, span + 0.4f);
                    PrefabUtility.SaveAsPrefabAsset(root, $"{Crops}/crop_{c.id}.prefab");
                    n++;
                }
                finally { Object.DestroyImmediate(root); }
            }
            return n;
        }

        // ------------------------------------------------------------------ nature

        /// <summary>A nature prop: the pack model and its size in metres (height for trees, width for the rest).</summary>
        public static readonly (string id, string pack, float size, bool byHeight, bool sway)[] NatureList =
        {
            ("tree_leafy_1", "tree_001", 5.5f, true, true), ("tree_leafy_2", "tree_002", 7.0f, true, true), ("tree_leafy_3", "tree_003", 7.5f, true, true),
            ("tree_leafy_4", "tree_004", 6.5f, true, true), ("tree_leafy_5", "tree_005", 8.0f, true, true), ("tree_leafy_6", "tree_006", 6.5f, true, true),
            ("tree_leafy_7", "tree_007", 8.0f, true, true), ("tree_leafy_8", "tree_008", 8.5f, true, true),
            ("tree_fruit_1", "tree_009", 6.0f, true, true), ("tree_fruit_2", "tree_010", 6.5f, true, true), ("tree_fruit_3", "tree_011", 5.0f, true, true),
            ("tree_fir_1", "fir_tree_001", 9.0f, true, true), ("tree_fir_2", "fir_tree_002", 7.0f, true, true), ("tree_fir_3", "fir_tree_003", 5.0f, true, true),
            ("tree_fir_4", "fir_tree_004", 4.0f, true, true), ("tree_fir_5", "fir_tree_005", 5.0f, true, true), ("tree_fir_6", "fir_tree_006", 5.0f, true, true),
            ("tree_fir_7", "fir_tree_007", 5.0f, true, true), ("tree_cypress", "fir_tree_008", 7.0f, true, true),
            ("tree_fir_teal_1", "fir_tree_009", 9.0f, true, true), ("tree_fir_teal_2", "fir_tree_010", 7.0f, true, true), ("tree_fir_teal_3", "fir_tree_011", 5.0f, true, true),
            ("bush_1", "bush_001", 2.4f, false, true), ("bush_2", "bush_002", 2.2f, false, true), ("bush_berry", "bush_003", 1.6f, false, true), ("bush_3", "bush_004", 2.2f, false, true),
            ("rock_1", "stone_fence_001", 4.0f, false, false), ("rock_2", "stone_fence_002", 2.2f, false, false), ("rock_3", "stone_fence_003", 1.9f, false, false),
            ("rock_4", "stone_fence_004", 2.1f, false, false), ("rock_5", "stone_fence_005", 2.3f, false, false), ("rock_6", "stone_fence_006", 1.3f, false, false),
            ("rock_7", "stone_fence_007", 1.2f, false, false),
            ("grass_1", "grass_002", 1.1f, false, true), ("grass_2", "grass_004", 1.2f, false, true), ("grass_3", "grass_006", 0.9f, false, true), ("grass_4", "grass_008", 1.4f, false, true),
            ("stump", "dry_wood_007", 1.6f, false, false), ("dead_tree_1", "dry_wood_002", 5.0f, true, false), ("dead_tree_2", "dry_wood_004", 4.5f, true, false),
            ("pond", "water_001", 7f, false, false),
        };

        static int BuildNature()
        {
            int n = 0;
            foreach (var (id, pack, size, byHeight, sway) in NatureList)
                if (BuildNatureProp(id, Pack(pack), size, byHeight, sway)) n++;
            // mushrooms from the Food pack, three in a cluster
            var m = new[] { FoodPack("Mushroom_002"), FoodPack("Mushroom_004"), FoodPack("Mushroom_006") }.Where(x => x != null).ToArray();
            if (m.Length > 0 && BuildCluster("mushrooms", m!, 0.45f, 0.5f)) n++;
            return n;
        }

        static bool BuildNatureProp(string id, GameObject? pf, float size, bool byHeight, bool sway)
        {
            if (pf == null) { Debug.LogWarning($"[ITHappy] nature {id}: missing pack prefab"); return false; }
            var root = new GameObject(id);
            try
            {
                var go = Inst(pf, root.transform);
                go.name = "Model";
                var b = WorldBounds(go);
                float s = byHeight ? size / Mathf.Max(b.size.y, 0.01f) : size / Mathf.Max(Mathf.Max(b.size.x, b.size.z), 0.01f);
                go.transform.localScale = Vector3.one * s;
                b = WorldBounds(go);
                go.transform.localPosition -= new Vector3(b.center.x, id == "pond" ? b.center.y : b.min.y, b.center.z);
                b = WorldBounds(go);
                var box = root.AddComponent<BoxCollider>();
                box.center = b.center; box.size = b.size;
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    r.shadowCastingMode = id.StartsWith("grass") || id == "pond" ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    r.receiveShadows = true;
                }
                PrefabUtility.SaveAsPrefabAsset(root, $"{Nature}/{id}.prefab");
                return true;
            }
            finally { Object.DestroyImmediate(root); }
        }

        static bool BuildCluster(string id, GameObject[] pfs, float height, float spread)
        {
            var root = new GameObject(id);
            try
            {
                var model = new GameObject("Model");
                model.transform.SetParent(root.transform, false);
                var rng = new System.Random(3);
                for (int i = 0; i < 4; i++)
                {
                    var go = Inst(pfs[i % pfs.Length], model.transform);
                    FitHeight(go, height * (0.7f + (float)rng.NextDouble() * 0.5f));
                    go.transform.localRotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                    var b = WorldBounds(go);
                    float a = i * Mathf.PI * 0.5f + (float)rng.NextDouble();
                    go.transform.position += new Vector3(Mathf.Cos(a) * spread - b.center.x, -b.min.y, Mathf.Sin(a) * spread - b.center.z);
                }
                var all = WorldBounds(model);
                var box = root.AddComponent<BoxCollider>();
                box.center = all.center; box.size = all.size;
                PrefabUtility.SaveAsPrefabAsset(root, $"{Nature}/{id}.prefab");
                return true;
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ------------------------------------------------------------------ the visual catalogue (crops by id)

        static void VisualCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WorldVisualCatalog>(VisualCatalogPath);
            if (catalog == null)
            {
                Folder(Path.GetDirectoryName(VisualCatalogPath)!.Replace('\\', '/'));
                catalog = ScriptableObject.CreateInstance<WorldVisualCatalog>();
                AssetDatabase.CreateAsset(catalog, VisualCatalogPath);
            }
            catalog.ClearEntries();
            foreach (var c in CropList)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{Crops}/crop_{c.id}.prefab");
                if (pf != null) catalog.SetEntry("crop_" + c.id, pf);
            }
            var field = AssetDatabase.LoadAssetAtPath<GameObject>($"{Props}/field.prefab");
            if (field != null) catalog.SetEntry("field", field);
            catalog.SetDefaults(Cell, 11, Array.Empty<GameObject>(), Array.Empty<GameObject>());
            EditorUtility.SetDirty(catalog);
        }

        // ------------------------------------------------------------------ pack sheet

        /// <summary>Every prefab of the packs rendered the way the UI pictures are, into Claude outputs/stage/packsheet,
        /// to choose models from.</summary>
        public static void RenderPackSheet()
        {
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Claude outputs", "stage", "packsheet"));
            Directory.CreateDirectory(dir);
            var rig = IslandPictureMaker.Rig();
            int n = 0;
            try
            {
                foreach (var (tag, folder) in new[] { ("farm", "Assets/ithappy/Farm/Prefabs"), ("food", "Assets/ithappy/Food_Free/Prefabs"), ("furn", "Assets/ithappy/Cute_Furniture_Free/Prefabs"), ("xmas", "Assets/ithappy/Christmas_Pack_FREE/Prefabs"), ("zomb", "Assets/ithappy/Zombies_Mini/Prefabs/Weapons") })
                    foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                    {
                        var path = AssetDatabase.GUIDToAssetPath(g);
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (pf == null) continue;
                        var file = Path.Combine(dir, tag + "_" + Path.GetFileNameWithoutExtension(path) + ".png");
                        if (File.Exists(file)) continue;
                        var png = IslandPictureMaker.Render(rig.cam, pf, false);
                        if (png != null) { File.WriteAllBytes(file, png); n++; }
                    }
            }
            finally { Object.DestroyImmediate(rig.root); }
            Debug.Log($"[ITHappy] pack sheet: {n} pictures in {dir}");
        }

        // ------------------------------------------------------------------ helpers

        public static GameObject? Pack(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(FarmPrefabs + name + ".prefab")
                                                      ?? AssetDatabase.LoadAssetAtPath<GameObject>(FarmPrefabs + "Animals/" + name + ".prefab");
        public static GameObject? FoodPack(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(FoodPrefabs + name + ".prefab");
        public static GameObject? Xmas(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(XmasPrefabs + name + ".prefab");
        public const string FurniturePrefabs = "Assets/ithappy/Cute_Furniture_Free/Prefabs";
        /// <summary>A Cute Furniture prefab by name (they sit in room sub-folders).</summary>
        public static GameObject? Furn(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Prefab", new[] { FurniturePrefabs }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }
        public const string ZombieWeapons = "Assets/ithappy/Zombies_Mini/Prefabs/Weapons/";
        public static GameObject? ZombieWeapon(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(ZombieWeapons + name + ".prefab");
        public const string Weapons = Art + "/Weapons";
        public const string Vehicles = Art + "/Vehicles";

        /// <summary>The Farm pack's vehicles as clean prefabs: order trucks, vans, visitors' cars, campers and caravans.</summary>
        static int BuildVehicles()
        {
            Folder(Vehicles);
            int n = 0;
            var names = new List<string> { "truck_005", "truck_007", "bus_001", "bus_002", "bus_003", "bus_004", "bus_005", "bus_006", "trailer_004", "trailer_005", "truck_006" };
            for (var i = 1; i <= 23; i++) names.Add($"car_{i:000}");
            foreach (var name in names)
            {
                var pf = Pack(name);
                if (pf == null) { Debug.LogWarning($"[ITHappy] no vehicle prefab {name}"); continue; }
                var root = new GameObject(name);
                try
                {
                    var go = Inst(pf, root.transform);
                    go.name = "Model";
                    foreach (var r in root.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
                    PrefabUtility.SaveAsPrefabAsset(root, $"{Vehicles}/{name}.prefab");
                    n++;
                }
                finally { Object.DestroyImmediate(root); }
            }
            return n;
        }

        /// <summary>The Zombies Mini hand-held weapons and tools as clean prefabs (held by warriors, labourers, builders).</summary>
        static int BuildWeapons()
        {
            Folder(Weapons);
            int n = 0;
            foreach (var name in new[] { "Bat", "Baton", "Crowbar", "Pipe", "PipeWrench", "RoadSign", "Weapon_005", "Hoe", "Rake", "Shovel", "GardenShears", "Pickaxe", "Saw", "Wrench", "FryingPan" })
            {
                var pf = ZombieWeapon(name);
                if (pf == null) { Debug.LogWarning($"[ITHappy] no weapon prefab {name}"); continue; }
                var root = new GameObject(name);
                try
                {
                    var go = Inst(pf, root.transform);
                    go.name = "Model";
                    foreach (var r in root.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
                    PrefabUtility.SaveAsPrefabAsset(root, $"{Weapons}/{name}.prefab");
                    n++;
                }
                finally { Object.DestroyImmediate(root); }
            }
            return n;
        }

        /// <summary>A pack prefab instance under <paramref name="parent"/>, with the pack's scripts that don't load here
        /// (file and class names differ, so Unity sees a missing script) and its own behaviours removed: the game
        /// moves and animates everything itself.</summary>
        public static GameObject Inst(GameObject prefab, Transform parent)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            StripScripts(go);
            return go;
        }

        public static void StripScripts(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            // the pack's own behaviours, dependents first (a RequireComponent can't be removed before what needs it)
            for (int pass = 0; pass < 6; pass++)
            {
                var pack = go.GetComponentsInChildren<MonoBehaviour>(true)
                    .Where(mb => mb != null && (mb.GetType().Namespace ?? "").StartsWith("ithappy")).ToList();
                if (pack.Count == 0) break;
                var removed = 0;
                foreach (var mb in pack)
                {
                    var type = mb.GetType();
                    bool needed = mb.gameObject.GetComponents<MonoBehaviour>().Any(o => o != null && o != mb
                        && o.GetType().GetCustomAttributes(typeof(RequireComponent), true).Cast<RequireComponent>()
                            .Any(r => r.m_Type0 == type || r.m_Type1 == type || r.m_Type2 == type));
                    if (needed) continue;
                    Object.DestroyImmediate(mb);
                    removed++;
                }
                if (removed == 0) break;
            }
        }

        /// <summary>World-space bounds of everything drawn under <paramref name="go"/>.</summary>
        public static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        /// <summary>Scale an instance so it stands <paramref name="height"/> metres tall.</summary>
        public static void FitHeight(GameObject go, float height)
        {
            var b = WorldBounds(go);
            if (b.size.y > 0.001f) go.transform.localScale = go.transform.localScale * (height / b.size.y);
        }

        public static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static void Set(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[ITHappy] no field {field} on {target.GetType().Name}"); return; }
            switch (value)
            {
                case Vector3 v: p.vector3Value = v; break;
                case float f: p.floatValue = f; break;
                case string s: p.stringValue = s; break;
                case bool bo: p.boolValue = bo; break;
                case Object o: p.objectReferenceValue = o; break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
