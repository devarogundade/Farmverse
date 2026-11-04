using System.Collections.Generic;
using Farmverse.Presentation.World.Island;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Farmverse.Editor
{
    internal static partial class IslandPaths
    {
        // ------------------------------------------------------------------ meshes

        /// <summary>The route meshes: cobbled roads, the railway (ballast, sleepers, rails, trestle piers over water)
        /// and the bridges, each tagged with the chapter it opens in.</summary>
        public static void CreateObjects(string genDir, string matDir)
        {
            var root = new GameObject("Routes");
            var cobble = LitMaterial($"{matDir}/Road_Cobble.mat", null, new Color(0.88f, 0.72f, 0.52f), 0f, 0.12f);   // flat sand roads, like the packs' renders (no cobble photo)
            var gravel = LitMaterial($"{matDir}/Rail_Ballast.mat", "gravel", new Color(0.82f, 0.78f, 0.74f), 0f, 0.1f);
            var wood = LitMaterial($"{matDir}/Rail_Sleeper.mat", "wood_planks", new Color(0.55f, 0.42f, 0.32f), 0f, 0.2f);
            var steel = LitMaterial($"{matDir}/Rail_Steel.mat", null, new Color(0.55f, 0.56f, 0.6f), 0.85f, 0.65f);
            var stone = LitMaterial($"{matDir}/Bridge_Stone.mat", "paving_stone", new Color(0.92f, 0.9f, 0.86f), 0f, 0.12f);
            var plank = LitMaterial($"{matDir}/Bridge_Wood.mat", "wood_planks", new Color(0.78f, 0.62f, 0.46f), 0f, 0.2f);

            int part = 0;
            foreach (var r in Routes)
            {
                if (r.kind == Kind.Road)
                    foreach (var seg in Split(r.pts, 160, false))
                        Tag(Add(root, $"{r.name} {part}", Ribbon(seg, new[] { -RoadHalf, -RoadHalf * 0.5f, 0f, RoadHalf * 0.5f, RoadHalf },
                            new[] { 0.03f, 0.07f, 0.09f, 0.07f, 0.03f }, 5f), cobble, $"{genDir}/road_{part++}.asset", false), r.chapter);
                // the causeway (the old railway line): a cobbled road the order trucks and visitors come in on
                if (r.kind == Kind.Rail)
                    foreach (var seg in Split(r.pts, 160, false))
                        Tag(Add(root, $"{r.name} {part}", Ribbon(seg, new[] { -RoadHalf, -RoadHalf * 0.5f, 0f, RoadHalf * 0.5f, RoadHalf },
                            new[] { 0.03f, 0.07f, 0.09f, 0.07f, 0.03f }, 5f), cobble, $"{genDir}/road_{part++}.asset", false), r.chapter);
            }

            Fences(root, genDir);

            int bi = 0;
            foreach (var b in Bridges)
            {
                {
                    bool road = b.route.kind == Kind.Road;
                    var go = Add(root, $"Bridge {bi} ({b.route.name})", road ? StoneBridge(b) : WoodBridge(b), road ? stone : plank, $"{genDir}/bridge_{bi}.asset", true);
                    WalkDeck(go, Span(b), b.half - 0.2f, road ? 0.05f : 0.08f, $"{genDir}/bridge_{bi}_walk.asset");
                    Tag(go, b.route.chapter);
                }
                bi++;
            }
        }

        /// <summary>
        /// Fences along both sides of every street and lane, like the packs' own farms: white rails on the main
        /// streets and the causeway, wooden rails on the lanes. Gaps at every crossing, a gateway every few panels,
        /// none over water or by the depot. Each side is one combined mesh, shown from the route's chapter.
        /// </summary>
        static void Fences(GameObject root, string genDir)
        {
            var white = IthappyArt.Pack("fence_008");
            var wood = IthappyArt.Pack("fence_024");
            if (white == null || wood == null) { Debug.LogWarning("[IslandPaths] no fence prefabs"); return; }
            var fenceRoot = new GameObject("Fences");
            fenceRoot.transform.SetParent(root.transform, false);
            // the panel's own mesh parts, in its own space, and its length along x
            (List<(Mesh mesh, Matrix4x4 m, Material mat)> parts, float length) Panel(GameObject pf)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                try
                {
                    var list = new List<(Mesh, Matrix4x4, Material)>();
                    var w2l = inst.transform.worldToLocalMatrix;
                    foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                    {
                        var mr = mf.GetComponent<MeshRenderer>();
                        if (mf.sharedMesh == null || mr == null) continue;
                        for (int sm = 0; sm < mf.sharedMesh.subMeshCount; sm++)
                            list.Add((mf.sharedMesh, w2l * mf.transform.localToWorldMatrix, mr.sharedMaterials[Mathf.Min(sm, mr.sharedMaterials.Length - 1)]));
                    }
                    var b = IthappyArt.WorldBounds(inst);
                    // panels run along x; one modelled along z is turned to match
                    if (b.size.z > b.size.x)
                    {
                        var turn = Matrix4x4.Rotate(Quaternion.Euler(0, 90f, 0));
                        for (int i = 0; i < list.Count; i++) list[i] = (list[i].Item1, turn * list[i].Item2, list[i].Item3);
                        return (list, b.size.z);
                    }
                    return (list, b.size.x);
                }
                finally { Object.DestroyImmediate(inst); }
            }
            var whiteP = Panel(white); var woodP = Panel(wood);
            int part = 0;
            foreach (var r in Routes)
            {
                var street = r.kind != Kind.Track;
                var (panel, len) = street ? whiteP : woodP;
                var off = r.kind == Kind.Track ? TrackHalf + 0.7f : RoadHalf + 0.9f;
                foreach (var side in new[] { -1f, 1f })
                {
                    var bySub = new Dictionary<Material, List<CombineInstance>>();
                    float walked = 0f; int count = 0;
                    var pts = r.pts;
                    for (int i = 1; i < pts.Count; i++)
                    {
                        var a = pts[i - 1]; var b = pts[i];
                        var seg = new Vector3(b.x - a.x, 0, b.z - a.z);
                        var segLen = seg.magnitude;
                        if (segLen < 1e-3f) continue;
                        var dir = seg / segLen;
                        var right = new Vector3(dir.z, 0, -dir.x);
                        for (float t = (walked > 0 ? len - walked : 0f); t < segLen; t += len)
                        {
                            var c = Vector3.Lerp(a, b, t / segLen) + dir * (len * 0.5f) + right * side * off;
                            count++;
                            if (count % 9 == 0) continue;   // a gateway now and then
                            if (Near(c, r)) continue;     // keep crossings open
                            if (IslandShape.Base(Chapters, c.x, c.z) < WetBelow) continue;
                            if (Vector2.Distance(new Vector2(c.x, c.z), new Vector2(StationPos.x, StationPos.z)) < StationRadius + 3f) continue;
                            if (Mathf.Abs(c.x) < 10f && Mathf.Abs(c.z) < 10f) continue;
                            if (InCamp(c.x, c.z, 0.5f)) continue;   // the caravan park
                            var y = IslandBuilder.Height(r.chapter, c.x, c.z);
                            var m = Matrix4x4.TRS(new Vector3(c.x, y, c.z), Quaternion.LookRotation(right, Vector3.up), Vector3.one);
                            foreach (var (mesh, local, mat) in panel)
                            {
                                if (!bySub.TryGetValue(mat, out var list)) bySub[mat] = list = new List<CombineInstance>();
                                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sm, transform = m * local });
                            }
                        }
                        walked = (walked + segLen) % len;
                    }
                    if (bySub.Count == 0) continue;
                    var mats = new List<Material>(); var subs = new List<CombineInstance>();
                    foreach (var kv in bySub)
                    {
                        var sub = new Mesh { indexFormat = IndexFormat.UInt32 };
                        sub.CombineMeshes(kv.Value.ToArray(), true, true);
                        subs.Add(new CombineInstance { mesh = sub, transform = Matrix4x4.identity });
                        mats.Add(kv.Key);
                    }
                    var whole = new Mesh { name = $"fence {r.name} {side}", indexFormat = IndexFormat.UInt32 };
                    whole.CombineMeshes(subs.ToArray(), false, false);
                    foreach (var ci in subs) Object.DestroyImmediate(ci.mesh);
                    var go = Add(fenceRoot, $"Fence {r.name} {(side < 0 ? "left" : "right")}", whole, mats[0], $"{genDir}/fence_{part++}.asset", true);
                    go.GetComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
                    Tag(go, r.chapter);
                }
            }
        }

        /// <summary>Close to where another route crosses or ends (a gap in the fence there).</summary>
        static bool Near(Vector3 p, Route self)
        {
            foreach (var o in Routes)
            {
                if (o == self) continue;
                var gap = (o.kind == Kind.Track ? TrackHalf : RoadHalf) + 4.5f;
                for (int i = 0; i < o.pts.Count; i += 2)
                {
                    var q = o.pts[i];
                    var dx = q.x - p.x; var dz = q.z - p.z;
                    if (dx * dx + dz * dz < gap * gap) return true;
                }
            }
            return false;
        }

        internal static GameObject Tag(GameObject go, int chapter)
        {
            var t = go.AddComponent<Farmverse.Presentation.World.Island.IslandChapterTag>();
            t.fromChapter = chapter;
            return go;
        }

        /// <summary>
        /// The surface people walk on: a flat, invisible deck counted as ground (Farmverse's ground test only
        /// trusts <see cref="IslandGround"/> colliders), extended a little onto each bank so stepping on and
        /// off is smooth. Parapets and rails are left out, so nobody ends up standing on a handrail.
        /// </summary>
        internal static void WalkDeck(GameObject parent, List<Vector3> span, float half, float lift, string path)
        {
            var pts = new List<Vector3>(span);
            if (pts.Count >= 2)
            {
                var a = (pts[0] - pts[1]).normalized; var z = (pts[pts.Count - 1] - pts[pts.Count - 2]).normalized;
                pts.Insert(0, pts[0] + a * 1.5f + Vector3.down * 0.35f);
                pts.Add(pts[pts.Count - 1] + z * 1.5f + Vector3.down * 0.35f);
            }
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            AppendRibbon(v, uv, tri, pts, new[] { -half, half }, new[] { lift, lift }, 2f);
            var m = Finish(v, uv, tri, "walk deck");
            AssetDatabase.CreateAsset(m, path);
            var go = new GameObject("Walk deck");
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshCollider>().sharedMesh = m;
            go.AddComponent<IslandGround>();
        }

        static List<Vector3> Span(Bridge b)
        {
            var o = new List<Vector3>();
            for (int i = b.from; i <= b.to; i++) { var p = b.route.pts[i]; o.Add(new Vector3(p.x, b.deck, p.z)); }
            return o;
        }

        /// <summary>Wooden piers under the railway where it runs over the sea (they vanish into the land as it rises).</summary>
        static Mesh Trestle(Bridge b)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            var pts = b.route.pts;
            for (int i = b.from; i <= b.to; i += 6)
            {
                var p = pts[i]; var s = Side(pts, i); var f = Vector3.Cross(s, Vector3.up);
                float top = p.y + 0.05f, bottom = -14f, mid = (top + bottom) / 2f, half = (top - bottom) / 2f;
                foreach (float off in new[] { -1.15f, 1.15f })
                    Box(v, uv, tri, new Vector3(p.x, mid, p.z) + s * off, s * 0.16f, Vector3.up * half, f * 0.16f);
                Box(v, uv, tri, new Vector3(p.x, top - 0.12f, p.z), s * 1.5f, Vector3.up * 0.12f, f * 0.2f);           // cap beam
                Box(v, uv, tri, new Vector3(p.x, -0.6f, p.z), s * 1.25f, Vector3.up * 0.08f, f * 0.1f);               // brace at the waterline
            }
            // a plank deck under the ballast, so the line reads as a bridge from above
            var span = new List<Vector3>();
            for (int i = b.from; i <= b.to; i++) span.Add(pts[i] + Vector3.up * 0.02f);
            AppendRibbon(v, uv, tri, span, new[] { -RailHalf - 0.2f, RailHalf + 0.2f }, new[] { 0f, 0f }, 3f);
            return Finish(v, uv, tri, "trestle");
        }

        /// <summary>A stone road bridge: deck, parapets and a solid arch face down to the water.</summary>
        static Mesh StoneBridge(Bridge b)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            var span = Span(b);
            float h = b.half;
            AppendRibbon(v, uv, tri, span, new[] { -h, h }, new[] { 0.05f, 0.05f }, 4f);                       // deck
            foreach (float side in new[] { -1f, 1f })
            {
                // parapet wall
                AppendRibbon(v, uv, tri, span, new[] { side * h, side * h }, new[] { 0.05f, 0.95f }, 4f, true);
                AppendRibbon(v, uv, tri, span, new[] { side * (h - 0.35f), side * (h - 0.35f) }, new[] { 0.95f, 0.05f }, 4f, true);
                AppendRibbon(v, uv, tri, span, new[] { side * h, side * (h - 0.35f) }, new[] { 0.95f, 0.95f }, 4f);
            }
            // the arch: each side face drops from the deck to the water following a curve
            int n = span.Count;
            foreach (float side in new[] { -1f, 1f })
            {
                int b0 = v.Count;
                for (int i = 0; i < n; i++)
                {
                    float t = n > 1 ? i / (float)(n - 1) : 0.5f;
                    float soffit = Mathf.Lerp(b.deck - 0.5f, -1.6f, Mathf.Pow(Mathf.Abs(t * 2f - 1f), 2.5f));   // underside of the arch
                    var s = Side(span, i);
                    v.Add(span[i] + s * side * h); uv.Add(new Vector2(i / 4f, 0));
                    v.Add(new Vector3(span[i].x, soffit, span[i].z) + s * side * h); uv.Add(new Vector2(i / 4f, (b.deck - soffit) / 4f));
                    if (i == 0) continue;
                    int a0 = b0 + (i - 1) * 2, a1 = b0 + i * 2;
                    if (side > 0) { tri.Add(a0); tri.Add(a1); tri.Add(a0 + 1); tri.Add(a0 + 1); tri.Add(a1); tri.Add(a1 + 1); }
                    else { tri.Add(a0); tri.Add(a0 + 1); tri.Add(a1); tri.Add(a0 + 1); tri.Add(a1 + 1); tri.Add(a1); }
                }
            }
            return Finish(v, uv, tri, "stone bridge");
        }

        /// <summary>A wooden footbridge: plank deck on posts, with a handrail each side.</summary>
        static Mesh WoodBridge(Bridge b)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            var span = Span(b);
            float h = b.half;
            AppendRibbon(v, uv, tri, span, new[] { -h, h }, new[] { 0.08f, 0.08f }, 2f);
            for (int i = 0; i < span.Count; i += 3)
            {
                var p = span[i]; var s = Side(span, i); var f = Vector3.Cross(s, Vector3.up);
                foreach (float side in new[] { -1f, 1f })
                {
                    Box(v, uv, tri, p + s * side * (h - 0.08f) + Vector3.up * 0.55f, s * 0.07f, Vector3.up * 0.5f, f * 0.07f);   // railing post
                    Box(v, uv, tri, new Vector3(p.x, (p.y - 2f) / 2f, p.z) + s * side * (h - 0.25f), s * 0.12f, Vector3.up * ((p.y + 2f) / 2f), f * 0.12f);   // pile
                }
            }
            foreach (float side in new[] { -1f, 1f })
                AppendRibbon(v, uv, tri, span, new[] { side * (h - 0.12f), side * (h - 0.02f) }, new[] { 1.05f, 1.05f }, 2f);   // handrail
            return Finish(v, uv, tri, "footbridge");
        }

        static void AppendRibbon(List<Vector3> v, List<Vector2> uv, List<int> tri, List<Vector3> p, float[] across, float[] lift, float tile, bool vertical = false)
        {
            int w = across.Length, b0 = v.Count;
            float along = 0;
            for (int i = 0; i < p.Count; i++)
            {
                if (i > 0) along += Vector3.Distance(p[i], p[i - 1]);
                var s = Side(p, i);
                for (int k = 0; k < w; k++)
                {
                    v.Add(p[i] + s * across[k] + Vector3.up * lift[k]);
                    uv.Add(new Vector2(vertical ? lift[k] / tile : (across[k] - across[0]) / tile, along / tile));
                }
                if (i == 0) continue;
                int r0 = b0 + (i - 1) * w, r1 = b0 + i * w;
                for (int k = 0; k < w - 1; k++)
                {
                    tri.Add(r0 + k); tri.Add(r1 + k); tri.Add(r0 + k + 1);
                    tri.Add(r0 + k + 1); tri.Add(r1 + k); tri.Add(r1 + k + 1);
                }
            }
        }

        internal static GameObject Add(GameObject root, string name, Mesh m, Material mat, string path, bool shadows)
        {
            AssetDatabase.CreateAsset(m, path);
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        static IEnumerable<List<Vector3>> Split(List<Vector3> pts, int len, bool closed)
        {
            for (int s = 0; s < pts.Count; s += len)
            {
                var seg = new List<Vector3>();
                for (int i = s; i <= s + len; i++)
                {
                    if (i < pts.Count) seg.Add(pts[i]);
                    else if (closed) seg.Add(pts[i % pts.Count]);
                }
                if (seg.Count > 1) yield return seg;
            }
        }

        /// <summary>A strip along the route; offsets across (m) with heights above the route.</summary>
        static Mesh Ribbon(List<Vector3> p, float[] across, float[] lift, float tile)
        {
            int w = across.Length;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            float along = 0;
            for (int i = 0; i < p.Count; i++)
            {
                if (i > 0) along += Vector3.Distance(p[i], p[i - 1]);
                var s = Side(p, i);
                for (int k = 0; k < w; k++)
                {
                    v.Add(p[i] + s * across[k] + Vector3.up * lift[k]);
                    uv.Add(new Vector2((across[k] - across[0]) / tile, along / tile));
                }
                if (i == 0) continue;
                int r0 = (i - 1) * w, r1 = i * w;
                for (int k = 0; k < w - 1; k++)
                {
                    tri.Add(r0 + k); tri.Add(r1 + k); tri.Add(r0 + k + 1);
                    tri.Add(r0 + k + 1); tri.Add(r1 + k); tri.Add(r1 + k + 1);
                }
            }
            var m = new Mesh { name = "route", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        static Mesh Sleepers(List<Vector3> p)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            float acc = 0;
            for (int i = 1; i < p.Count; i++)
            {
                acc += Vector3.Distance(p[i], p[i - 1]);
                while (acc >= 0.7f)
                {
                    acc -= 0.7f;
                    float f = 1f - acc / Mathf.Max(Vector3.Distance(p[i], p[i - 1]), 1e-3f);
                    var c = Vector3.Lerp(p[i - 1], p[i], f) + Vector3.up * 0.26f;
                    var s = Side(p, i); var fwd = Vector3.Cross(s, Vector3.up);
                    Box(v, uv, tri, c + Vector3.up * 0.06f, s * 1.3f, Vector3.up * 0.06f, fwd * 0.13f);
                }
            }
            return Finish(v, uv, tri, "sleepers");
        }

        static Mesh Rails(List<Vector3> p)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            foreach (float off in new[] { -Gauge / 2, Gauge / 2 })
            {
                // extrude a small rectangle (head of the rail) along the route
                var prof = new[] { new Vector2(-0.045f, 0.38f), new Vector2(0.045f, 0.38f), new Vector2(0.045f, 0.50f), new Vector2(-0.045f, 0.50f) };
                int baseIdx = v.Count;
                for (int i = 0; i < p.Count; i++)
                {
                    var s = Side(p, i);
                    foreach (var q in prof) { v.Add(p[i] + s * (off + q.x) + Vector3.up * q.y); uv.Add(new Vector2(q.x, i)); }
                    if (i == 0) continue;
                    int a = baseIdx + (i - 1) * 4, b = baseIdx + i * 4;
                    for (int k = 0; k < 4; k++)
                    {
                        int k1 = (k + 1) % 4;
                        tri.Add(a + k); tri.Add(b + k); tri.Add(a + k1);
                        tri.Add(a + k1); tri.Add(b + k); tri.Add(b + k1);
                    }
                }
            }
            return Finish(v, uv, tri, "rails");
        }

        internal static void Box(List<Vector3> v, List<Vector2> uv, List<int> tri, Vector3 c, Vector3 ex, Vector3 ey, Vector3 ez)
        {
            // six faces, own vertices so the edges stay sharp
            var faces = new[] { (ey, ex, ez), (-ey, ex, -ez), (ex, ey, -ez), (-ex, ey, ez), (ez, ey, ex), (-ez, ey, -ex) };
            foreach (var (n, u, w) in faces)
            {
                int b = v.Count;
                var fc = c + n;
                v.Add(fc - u - w); v.Add(fc - u + w); v.Add(fc + u + w); v.Add(fc + u - w);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 0.25f)); uv.Add(new Vector2(1, 0.25f)); uv.Add(new Vector2(1, 0));
                tri.Add(b); tri.Add(b + 1); tri.Add(b + 2); tri.Add(b); tri.Add(b + 2); tri.Add(b + 3);
            }
        }

        internal static Mesh Finish(List<Vector3> v, List<Vector2> uv, List<int> tri, string name)
        {
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        internal static Material LitMaterial(string path, string tex, Color tint, float metallic, float smooth)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            const string W = "Assets/Farmverse/Content/Textures/World/";
            if (tex != null)
            {
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(W + tex + "_albedo.png"));
                var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(W + tex + "_normal.png");
                if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
            }
            else { m.SetTexture("_BaseMap", null); m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            m.enableInstancing = true;
            if (tex == null || tex == "wood_planks") m.SetFloat("_Cull", 0f);   // thin parts: draw both faces
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
