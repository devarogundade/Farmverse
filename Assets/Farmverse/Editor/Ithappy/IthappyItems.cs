#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Farmverse.Presentation.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Farmverse.Editor
{
    /// <summary>
    /// Farmverse / Art / Build ITHappy Items: the tools people hold, the goods they carry and the barrows,
    /// mowers and carts they push, all cut from the bought packs and normalised so the game can put them in a
    /// fist or in front of a worker without per-model tuning. Writes Content/Art/{Tools,Goods,Carts} and
    /// Resources/ItemCatalog.asset (<see cref="ItemCatalog"/>).
    /// </summary>
    public static class IthappyItems
    {
        public const string Tools = IthappyArt.Art + "/Tools";
        public const string Goods = IthappyArt.Art + "/Goods";
        public const string Carts = IthappyArt.Art + "/Carts";
        const string CatalogPath = "Assets/Farmverse/Content/Resources/ItemCatalog.asset";

        /// <summary>Every Zombies Mini tool and weapon.</summary>
        static readonly string[] PackTools = { "Bat", "Baton", "Crowbar", "FryingPan", "GardenShears", "Hoe", "Pickaxe", "Pipe", "PipeWrench", "Rake", "RoadSign", "Saw", "Shovel", "Weapon_005", "Wrench" };

        /// <summary>A pack model for every good: (good id, pack, model, carried size in metres along its longest side).</summary>
        static readonly (string id, string pack, string model, float size)[] GoodModels =
        {
            ("wheat", "farm", "plant_011", 0.55f), ("maize", "farm", "plant_001", 0.6f), ("pumpkin", "farm", "plant_002", 0.5f),
            ("tomato", "farm", "plant_003", 0.45f), ("cabbage", "farm", "plant_004", 0.42f), ("beans", "farm", "plant_009", 0.55f),
            ("sunflower", "farm", "plant_008", 0.6f), ("flowers", "farm", "plant_007", 0.5f),
            ("egg", "food", "Egg_002", 0.2f), ("milk", "food", "Yogurt_002", 0.34f), ("meat", "food", "Sasuage_003", 0.32f),
            ("fish", "food", "Fish_004", 0.42f), ("mushroom", "food", "Mushroom_002", 0.28f), ("croissant", "food", "Croissant_001", 0.3f),
            ("yogurt", "food", "Coffee_005", 0.24f), ("juice", "food", "Bottle_002", 0.32f), ("soda", "food", "Soda_002", 0.3f),
            ("wood", "farm", "firewood_005", 0.6f), ("stone", "farm", "stone_fence_006", 0.45f), ("planks", "farm", "firewood_011", 0.75f),
            ("flour", "farm", "bag_002", 0.42f), ("feed", "farm", "bag_001", 0.42f), ("oil", "farm", "barrel_007", 0.4f),
            ("veg_box", "farm", "case_003", 0.5f), ("crate", "farm", "prop.020", 0.38f),
        };

        static readonly string[] Barrows = { "cart_001", "cart_002", "cart_003", "cart_004", "cart_005", "cart_006" };
        static readonly string[] Mowers = { "lawn_mower_001", "lawn_mower_002", "lawn_mower_003" };
        static readonly string[] Drays = { "dray_002", "dray_003", "dray_004", "dray_005" };

        [MenuItem("Farmverse/Art/Build ITHappy Items", priority = 3)]
        public static void BuildAll()
        {
            IthappyArt.Folder(Tools); IthappyArt.Folder(Goods); IthappyArt.Folder(Carts);
            IthappyArt.Folder("Assets/Farmverse/Content/Resources");
            var cat = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<ItemCatalog>(); AssetDatabase.CreateAsset(cat, CatalogPath); }
            cat.tools.Clear(); cat.goods.Clear(); cat.barrows.Clear(); cat.mowers.Clear(); cat.carts.Clear();

            foreach (var name in PackTools)
            {
                var pf = IthappyArt.ZombieWeapon(name);
                if (pf == null) { Debug.LogWarning("[Items] no tool " + name); continue; }
                var made = Tool(name, root => IthappyArt.Inst(pf, root), bucket: false);
                if (made != null) cat.tools.Add(new ItemCatalog.Entry { id = name, prefab = made });
            }
            if (Axe() is { } axe) cat.tools.Add(new ItemCatalog.Entry { id = "Axe", prefab = axe });
            if (IthappyArt.Pack("bucket_001") is { } bucket && Tool("Bucket", root => IthappyArt.Inst(bucket, root), bucket: true) is { } b)
                cat.tools.Add(new ItemCatalog.Entry { id = "Bucket", prefab = b });

            foreach (var (id, pack, model, size) in GoodModels)
            {
                var pf = pack == "food" ? IthappyArt.FoodPack(model) : IthappyArt.Pack(model);
                if (pf == null) { Debug.LogWarning($"[Items] no model {pack}:{model} for {id}"); continue; }
                var made = Good(id, pf, size);
                if (made == null) continue;
                if (id == "crate") cat.fallbackGood = made;
                else cat.goods.Add(new ItemCatalog.Entry { id = id, prefab = made });
            }

            foreach (var n in Barrows) if (Pushed(n, 1.45f) is { } p) cat.barrows.Add(p);
            foreach (var n in Mowers) if (Pushed(n, 1.1f) is { } p) cat.mowers.Add(p);
            foreach (var n in Drays) if (Pushed(n, 2.1f) is { } p) cat.carts.Add(p);

            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Items] {cat.tools.Count} tools, {cat.goods.Count} goods, {cat.barrows.Count} barrows, {cat.mowers.Count} mowers, {cat.carts.Count} carts");
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>Every vertex drawn under <paramref name="root"/>, in root space (works on meshes that aren't readable).</summary>
        static List<Vector3> Points(GameObject root)
        {
            var pts = new List<Vector3>();
            var toRoot = root.transform.worldToLocalMatrix;
            void Take(Mesh? mesh, Transform t)
            {
                if (mesh == null) return;
                using var data = Mesh.AcquireReadOnlyMeshData(mesh);
                var md = data[0];
                var v = new Unity.Collections.NativeArray<Vector3>(md.vertexCount, Unity.Collections.Allocator.Temp);
                md.GetVertices(v);
                var m = toRoot * t.localToWorldMatrix;
                foreach (var p in v) pts.Add(m.MultiplyPoint3x4(p));
                v.Dispose();
            }
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>()) Take(mf.sharedMesh, mf.transform);
            foreach (var sm in root.GetComponentsInChildren<SkinnedMeshRenderer>()) Take(sm.sharedMesh, sm.transform);
            return pts;
        }

        static Bounds BoundsOf(List<Vector3> pts)
        {
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            return b;
        }

        /// <summary>
        /// A held tool: the model under a root whose origin is the grip, +Y up the handle to the head, +Z the way the
        /// head sticks out. The handle is the thin end of the longest axis; the grip is a little way up it.
        /// A bucket hangs from its handle: grip at the top, the bail along +Y, the bucket below (+Z down).
        /// </summary>
        static GameObject? Tool(string name, Func<Transform, GameObject> make, bool bucket)
        {
            var root = new GameObject(name);
            try
            {
                var model = make(root.transform);
                model.name = "Model";
                foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                var pts = Points(model);
                if (pts.Count == 0) { Debug.LogWarning("[Items] no mesh in " + name); return null; }
                var b = BoundsOf(pts);
                Vector3 grip, axis, side;
                if (bucket)
                {
                    grip = new Vector3(b.center.x, b.max.y - b.size.y * 0.04f, b.center.z);
                    axis = b.size.x >= b.size.z ? Vector3.right : Vector3.forward;
                    side = Vector3.down;
                }
                else
                {
                    int L = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : b.size.y >= b.size.z ? 1 : 2;
                    var dir = Vector3.zero; dir[L] = 1f;
                    float lo = b.min[L], len = b.size[L];
                    // cross-section spread in the first and last fifth: the head is the wide end
                    float Spread(float from, float to)
                    {
                        var sel = pts.Where(p => p[L] >= lo + from * len && p[L] <= lo + to * len).ToList();
                        if (sel.Count == 0) return 0f;
                        var sb = BoundsOf(sel);
                        var e = sb.size; e[L] = 0f;
                        return e.magnitude;
                    }
                    var low = Spread(0f, 0.2f); var high = Spread(0.8f, 1f);
                    var headHigh = high >= low;
                    if (name is "Saw" or "Weapon_005") headHigh = !headHigh;   // the blade is thinner than the grip
                    axis = headHigh ? dir : -dir;
                    var handleEnd = headHigh ? lo : lo + len;
                    // the grip: 15% up from the handle's end, on the handle's own centre line
                    var gAt = handleEnd + (headHigh ? 1f : -1f) * len * 0.15f;
                    var near = pts.Where(p => Mathf.Abs(p[L] - gAt) < len * 0.05f).ToList();
                    grip = near.Count > 0 ? BoundsOf(near).center : b.center;
                    grip[L] = gAt;
                    // the head's lean off the handle line
                    var head = pts.Where(p => headHigh ? p[L] > lo + len * 0.8f : p[L] < lo + len * 0.2f).ToList();
                    var hc = head.Count > 0 ? head.Aggregate(Vector3.zero, (a, p) => a + p) / head.Count : b.center;
                    side = Vector3.ProjectOnPlane(hc - grip, axis);
                    if (side.magnitude < len * 0.04f)
                    {
                        // a straight tool (bat, pipe): its next-widest axis
                        var e = b.size; e[L] = -1f;
                        int S = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
                        side = Vector3.zero; side[S] = 1f;
                    }
                    side.Normalize();
                }
                // turn the model so axis → +Y and side → +Z, grip at the origin
                var frame = Quaternion.LookRotation(side, axis);
                var inv = Quaternion.Inverse(frame);
                model.transform.localRotation = inv * model.transform.localRotation;
                model.transform.localPosition = inv * (model.transform.localPosition - grip);
                foreach (var r in root.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
                return PrefabUtility.SaveAsPrefabAsset(root, $"{Tools}/{name}.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>The axe out of the Farm pack's chopping block (firewood_013): the parts standing above the stump.</summary>
        static GameObject? Axe()
        {
            var pf = IthappyArt.Pack("firewood_013");
            if (pf == null) return null;
            var mf = pf.GetComponentInChildren<MeshFilter>();
            var mr = pf.GetComponentInChildren<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null || mr == null) return null;
            var src = mf.sharedMesh;
            using var data = Mesh.AcquireReadOnlyMeshData(src);
            var md = data[0];
            int n = md.vertexCount;
            var pos = new Unity.Collections.NativeArray<Vector3>(n, Unity.Collections.Allocator.Temp); md.GetVertices(pos);
            var nrm = new Unity.Collections.NativeArray<Vector3>(n, Unity.Collections.Allocator.Temp); md.GetNormals(nrm);
            var uvs = new Unity.Collections.NativeArray<Vector2>(n, Unity.Collections.Allocator.Temp);
            var hasUv = md.HasVertexAttribute(VertexAttribute.TexCoord0);
            if (hasUv) md.GetUVs(0, uvs);
            var tris = new List<int>();
            for (int sm = 0; sm < md.subMeshCount; sm++)
            {
                var d = md.GetSubMesh(sm);
                var idx = new Unity.Collections.NativeArray<int>(d.indexCount, Unity.Collections.Allocator.Temp);
                md.GetIndices(idx, sm);
                tris.AddRange(idx);
                idx.Dispose();
            }
            // pieces: triangles joined through vertices at the same place
            var parent = Enumerable.Range(0, n).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b2) { a = Find(a); b2 = Find(b2); if (a != b2) parent[a] = b2; }
            var byPos = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < n; i++)
            {
                var k = Vector3Int.RoundToInt(pos[i] * 2000f);
                if (byPos.TryGetValue(k, out var j)) Union(i, j); else byPos[k] = i;
            }
            for (int t = 0; t < tris.Count; t += 3) { Union(tris[t], tris[t + 1]); Union(tris[t], tris[t + 2]); }
            var pieces = new Dictionary<int, Bounds>();
            for (int i = 0; i < n; i++)
            {
                var r = Find(i);
                if (pieces.TryGetValue(r, out var pb)) { pb.Encapsulate(pos[i]); pieces[r] = pb; } else pieces[r] = new Bounds(pos[i], Vector3.zero);
            }
            // the stump is the biggest piece; the axe is whatever reaches above its top
            var stump = pieces.OrderByDescending(kv => kv.Value.size.x * kv.Value.size.y * kv.Value.size.z).First();
            var top = stump.Value.max.y;
            Debug.Log("[Items] firewood_013 pieces: " + string.Join(" | ", pieces.Values.Select(pb => $"c{pb.center:F2} s{pb.size:F2}")) + $" scale {mf.transform.lossyScale} rot {mf.transform.rotation.eulerAngles}");
            var keep = new HashSet<int>(pieces.Where(kv => kv.Key != stump.Key && kv.Value.max.y > top + stump.Value.size.y * 0.15f).Select(kv => kv.Key));
            if (keep.Count == 0) { Debug.LogWarning("[Items] no axe found on firewood_013"); pos.Dispose(); nrm.Dispose(); uvs.Dispose(); return null; }
            var map = new Dictionary<int, int>();
            var v = new List<Vector3>(); var nn = new List<Vector3>(); var uu = new List<Vector2>(); var tt = new List<int>();
            for (int t = 0; t < tris.Count; t += 3)
            {
                if (!keep.Contains(Find(tris[t]))) continue;
                for (int k = 0; k < 3; k++)
                {
                    var i = tris[t + k];
                    if (!map.TryGetValue(i, out var ni)) { ni = v.Count; map[i] = ni; v.Add(pos[i]); nn.Add(nrm.Length > 0 ? nrm[i] : Vector3.up); uu.Add(hasUv ? uvs[i] : Vector2.zero); }
                    tt.Add(ni);
                }
            }
            pos.Dispose(); nrm.Dispose(); uvs.Dispose();
            var mesh = new Mesh { name = "Axe" };
            mesh.SetVertices(v); mesh.SetNormals(nn); mesh.SetUVs(0, uu); mesh.SetTriangles(tt, 0);
            mesh.RecalculateBounds();
            var meshPath = $"{Tools}/Axe_mesh.asset";
            if (AssetDatabase.LoadMainAssetAtPath(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
            var mat = mr.sharedMaterial;
            return Tool("Axe", root =>
            {
                var go = new GameObject("Model");
                go.transform.SetParent(root, false);
                // the same scale the pack prefab gives its mesh
                go.transform.localScale = mf.transform.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                return go;
            }, bucket: false) is { } axe ? EmbedMesh(axe, mesh) : null;
        }

        /// <summary>Keep a made mesh inside its prefab (a separate mesh asset came back empty on load).</summary>
        static GameObject EmbedMesh(GameObject prefab, Mesh mesh)
        {
            var path = AssetDatabase.GetAssetPath(prefab);
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path)) if (o is Mesh) Object.DestroyImmediate(o, true);
            AssetDatabase.AddObjectToAsset(mesh, path);
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var mf in contents.GetComponentsInChildren<MeshFilter>()) mf.sharedMesh = mesh;
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>A carried good: the model scaled so its longest side is <paramref name="size"/>, base at the origin.</summary>
        static GameObject? Good(string id, GameObject pf, float size)
        {
            var root = new GameObject("good_" + id);
            try
            {
                var model = IthappyArt.Inst(pf, root.transform);
                model.name = "Model";
                foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                var b = IthappyArt.WorldBounds(model);
                var longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (longest > 1e-4f) model.transform.localScale *= size / longest;
                b = IthappyArt.WorldBounds(model);
                model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                foreach (var r in root.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
                return PrefabUtility.SaveAsPrefabAsset(root, $"{Goods}/good_{id}.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>
        /// A pushed or pulled thing, <paramref name="length"/> metres long: origin on the ground at the handles' end,
        /// +Z towards the wheels. The handle end is the end of the long axis whose lowest point is highest (handles
        /// stick up; the wheel end touches the ground).
        /// </summary>
        static GameObject? Pushed(string name, float length)
        {
            var pf = IthappyArt.Pack(name);
            if (pf == null) { Debug.LogWarning("[Items] no " + name); return null; }
            var root = new GameObject(name);
            try
            {
                var model = IthappyArt.Inst(pf, root.transform);
                model.name = "Model";
                foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                var pts = Points(model);
                var b = BoundsOf(pts);
                int L = b.size.x >= b.size.z ? 0 : 2;
                float lo = b.min[L], len = b.size[L];
                float MinY(float from, float to) { var sel = pts.Where(p => p[L] >= lo + from * len && p[L] <= lo + to * len).ToList(); return sel.Count == 0 ? 0f : sel.Min(p => p.y); }
                var handlesHigh = MinY(0.88f, 1f) > MinY(0f, 0.12f);   // handles at the high end of the axis
                var dir = Vector3.zero; dir[L] = handlesHigh ? -1f : 1f;   // towards the wheels
                var handleEnd = new Vector3(b.center.x, b.min.y, b.center.z); handleEnd[L] = handlesHigh ? lo + len : lo;
                var frame = Quaternion.LookRotation(dir, Vector3.up);
                var inv = Quaternion.Inverse(frame);
                model.transform.localRotation = inv * model.transform.localRotation;
                model.transform.localPosition = inv * (model.transform.localPosition - handleEnd);
                var wb = IthappyArt.WorldBounds(model);
                var s = length / Mathf.Max(wb.size.z, 0.01f);
                model.transform.localScale *= s;
                model.transform.localPosition *= s;
                foreach (var r in root.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
                // where the hands go: the two handle ends (the top of the last fifth, at its outer sides)
                var q = Points(root);   // in the prefab's own space (origin between the handles, +Z to the wheel)
                var qb = BoundsOf(q);
                var rear = q.Where(p => p.z <= qb.min.z + qb.size.z * 0.2f).ToList();
                if (rear.Count > 0)
                {
                    var top = rear.Max(p => p.y);
                    var high = rear.Where(p => p.y >= top - 0.12f).ToList();
                    float zg = high.Min(p => p.z) + 0.04f;
                    float xl = high.Min(p => p.x), xr = high.Max(p => p.x);
                    if (xr - xl < 0.2f) { xl = -0.22f; xr = 0.22f; }   // a single handle bar: hands either side of the middle
                    foreach (var (gn, gx) in new[] { ("GripL", xl), ("GripR", xr) })
                    {
                        var g = new GameObject(gn);
                        g.transform.SetParent(root.transform, false);
                        g.transform.localPosition = new Vector3(gx, top, zg);
                    }
                }
                return PrefabUtility.SaveAsPrefabAsset(root, $"{Carts}/{name}.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
