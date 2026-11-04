#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Farmverse.Presentation.World;
using Farmverse.Presentation.World.Island;
using UnityEditor;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// Pictures of the island's own models for the game's UI: each building in the catalogue (the shop's
    /// cards, the building panel), and the people you talk to (portraits); goods use the UI kit's painted icons. Each model is rendered off in
    /// a corner of the scene on its own layer, against black and then white, which gives a clean
    /// transparent edge. Saved as PNGs under Content/UI/Art/Island/Pictures and listed in IslandPictures.
    /// Build Island makes the missing ones; Farmverse / World / Render Island Pictures redoes them all.
    /// </summary>
    public static class IslandPictureMaker
    {
        public const string Dir = "Assets/Farmverse/Content/UI/Art/Island";
        public const string PicturesDir = Dir + "/Pictures";
        public const string AssetPath = Dir + "/IslandPictures.asset";
        private const int Layer = 31;
        private const int Size = 256;

        /// <summary>Who you can talk to, and the model their portrait comes from.</summary>
        private static readonly (string who, string[] prefabs)[] People =
        {
            ("farmer", new[] { "char_farmer" }),
            ("worker", new[] { "char_farm_worker" }),
            ("builder", new[] { "char_builder", "char_farm_worker" }),
            ("warrior", new[] { "char_warrior" }),
            ("guard", new[] { "char_warrior" }),
            ("baker", new[] { "char_baker" }),
            ("villager", new[] { "villager_a" }),
            ("zombie", new[] { "char_zombie_1" }),
            ("zombie_footballer", new[] { "char_zombie_footballer" }),
            ("zombie_builder", new[] { "char_zombie_builder" }),
            ("zombie_clerk", new[] { "char_zombie_clerk" }),
            ("zombie_policeman", new[] { "char_zombie_policeman" }),
            ("zombie_mime", new[] { "char_zombie_mime" }),
            ("zombie_tourist", new[] { "char_zombie_tourist" }),
            ("zombie_mayor", new[] { "char_zombie_clerk" }),
            ("cured_footballer", new[] { "char_cured_footballer" }),
            ("cured_builder", new[] { "char_cured_builder" }),
            ("cured_clerk", new[] { "char_cured_clerk" }),
            ("cured_policeman", new[] { "char_cured_policeman" }),
            ("cured_mime", new[] { "char_cured_mime" }),
            ("cured_tourist", new[] { "char_cured_tourist" }),
        };

        /// <summary>The boosts sold in the shop and the pack model that pictures each (Isl_x_&lt;id&gt;).</summary>
        static readonly (string id, string path)[] Boosts =
        {
            ("shield", "Assets/Farmverse/Content/Art/Weapons/RoadSign.prefab"),            // the zombies' stop sign: they stay away
            ("fert", "Assets/ithappy/Farm/Prefabs/bag_001.prefab"),                        // a sack of fertilizer
            ("rush", "Assets/Farmverse/Content/Art/Carts/cart_001.prefab"),                // the workers' wheelbarrow
            ("instant", "Assets/Farmverse/Content/Art/Tools/Saw.prefab"),                  // the builders' saw
            ("basket", "Assets/Farmverse/Content/Art/Goods/good_crate.prefab"),            // a bigger crate
            ("nightshift", "Assets/ithappy/Christmas_Pack_FREE/Prefabs/Clock_001.prefab"), // the clock: working through the night
            ("lunch", "Assets/Farmverse/Content/Art/Goods/good_veg_box.prefab"),           // a box of food
        };

        [MenuItem("Farmverse/World/Render Island Pictures")]
        public static void RenderAll()
        {
            var cat = AssetDatabase.LoadAssetAtPath<BuildingCatalogue>("Assets/Farmverse/Content/Island/BuildingCatalogue.asset");
            if (cat == null) { Debug.LogError("[Pictures] no building catalogue yet: run Farmverse / World / Build Island"); return; }
            Make(cat, redo: true);
        }

        /// <summary>Render what's missing (or everything with <paramref name="redo"/>) and return the list.</summary>
        public static IslandPictures Make(BuildingCatalogue catalogue, bool redo = false)
        {
            Folder(PicturesDir);
            var pics = AssetDatabase.LoadAssetAtPath<IslandPictures>(AssetPath);
            if (pics == null) { pics = ScriptableObject.CreateInstance<IslandPictures>(); AssetDatabase.CreateAsset(pics, AssetPath); }

            var jobs = new List<(string key, GameObject prefab, bool portrait)>();
            foreach (var e in catalogue.entries)
                if (e.prefab != null) jobs.Add(("b:" + e.id, e.prefab, false));
            foreach (var (who, names) in People)
            {
                var prefab = names.Select(FindPerson).FirstOrDefault(p => p != null);
                if (prefab != null) jobs.Add(("p:" + who, prefab, true));
            }
            // the costumes sold in Customize (portraits like the people's)
            foreach (var (cid, _, _, prefabName) in IthappyCharacters.CostumeList)
            {
                var pf = FindPerson(prefabName);
                if (pf != null) jobs.Add(("p:costume_" + cid, pf, true));
            }
            // the farmhouse's furniture and every good, from the packs
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { IthappyRooms.Furniture }))
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (pf != null) jobs.Add(("f:" + pf.name, pf, false));
            }
            var items = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/Farmverse/Content/Resources/ItemCatalog.asset");
            if (items != null) foreach (var e in items.goods) if (e.prefab != null) jobs.Add(("g:" + e.id, e.prefab, false));
            // the shop's boosts, each shown by a pack model of what it is
            foreach (var (id, path) in Boosts)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (pf != null) jobs.Add(("x:" + id, pf, false));
                else Debug.LogWarning("[Pictures] no model for boost " + id + ": " + path);
            }
            if (redo) Purge();
            var made = new List<string>();
            var rig = Rig();
            try
            {
                foreach (var (key, prefab, portrait) in jobs)
                {
                    var path = PathOf(key);
                    if (prefab == null || (!redo && File.Exists(path))) continue;
                    if (Render(rig.cam, prefab, portrait, FacesAway(key) ? 180f : 0f) is { } png)
                    {
                        File.WriteAllBytes(path, png);
                        made.Add(path);
                    }
                }
            }
            finally { Object.DestroyImmediate(rig.root); }
            AssetDatabase.Refresh();
            foreach (var path in made) Import(path);

            pics.pictures.Clear();
            foreach (var (key, _, _) in jobs)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(PathOf(key));
                if (tex != null) pics.pictures.Add(new IslandPictures.Picture { key = key, texture = tex });
            }
            EditorUtility.SetDirty(pics);
            AssetDatabase.SaveAssets();
            // the UI loads its pictures by name from Resources/GameUi/Art (Isl_b_barn, Isl_p_farmer, Isl_f_pouf, Isl_g_egg):
            // copy every render there so the cards show the pack models, not older pictures under the same names
            const string UiArt = "Assets/Farmverse/Content/UI/Game/Resources/GameUi/Art";
            var copied = 0;
            foreach (var (key, _, _) in jobs)
            {
                var src = PathOf(key);
                if (!File.Exists(src)) continue;
                var dst = $"{UiArt}/Isl_{key.Replace(':', '_')}.png";
                if (File.Exists(dst) && File.ReadAllBytes(dst).Length == new FileInfo(src).Length && !redo) continue;
                File.Copy(src, dst, true);
                copied++;
            }
            AssetDatabase.Refresh();
            Debug.Log($"[Pictures] {copied} copied to {UiArt}");
            Debug.Log($"[Pictures] {made.Count} rendered, {pics.pictures.Count} in {AssetPath}");
            return pics;
        }

        private static string PathOf(string key) => $"{PicturesDir}/{key.Replace(':', '_')}.png";

        private static GameObject? FindPerson(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(IthappyCharacters.People + "/" + name + ".prefab");

        internal static (GameObject root, Camera cam) Rig()
        {
            var root = new GameObject("Picture rig") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = new Vector3(0, -3000, 0);
            var cam = new GameObject("Picture camera").AddComponent<Camera>();
            cam.transform.SetParent(root.transform, false);
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.fieldOfView = 26f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 500f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.enabled = false;
            // a key light from the front-left and a soft fill, so the pictures read like the painted icons
            var key = new GameObject("Key").AddComponent<Light>();
            key.transform.SetParent(root.transform, false);
            key.type = LightType.Directional;
            key.intensity = 1.25f;
            key.color = new Color(1f, 0.96f, 0.88f);
            key.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            key.cullingMask = 1 << Layer;
            key.shadows = LightShadows.None;
            return (root, cam);
        }

        /// <summary>Things hung on a wall face the room with their back to the camera's side: turn them round.</summary>
        static bool FacesAway(string key) => key.Contains("picture") || key.Contains("clock") || key == "x:nightshift";

        internal static byte[]? Render(Camera cam, GameObject prefab, bool portrait, float yaw = 0f)
        {
            var go = PrefabUtility.IsPartOfPrefabAsset(prefab) ? (GameObject)PrefabUtility.InstantiatePrefab(prefab) : Object.Instantiate(prefab);
            go.SetActive(true);
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                go.transform.position = cam.transform.parent.position;
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                // people stand in their idle pose, not the bind pose
                if (portrait && go.GetComponentInChildren<Animator>() is { } anim && anim.runtimeAnimatorController != null)
                {
                    anim.Rebind();
                    anim.Update(0f);
                    anim.Update(0.5f);
                }
                var renderers = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToList();
                if (renderers.Count == 0) return null;
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);

                Vector3 centre;
                float radius;
                Vector3 dir;
                var animator = go.GetComponentInChildren<Animator>();
                var head = portrait && animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
                var hips = head != null ? animator!.GetBoneTransform(HumanBodyBones.Hips) : null;
                if (head != null && hips != null)
                {
                    // head and shoulders from the skeleton (a hat, a weapon or a crate doesn't throw the framing off),
                    // seen from the way the person faces
                    var torso = Mathf.Max(0.2f, Vector3.Distance(head.position, hips.position));
                    var headSize = Mathf.Clamp(b.max.y - head.position.y, torso * 0.45f, torso * 1.2f);
                    centre = head.position + Vector3.up * headSize * 0.22f;
                    radius = headSize * 0.72f;
                    var facing = animator!.transform.forward;
                    facing.y = 0f;
                    if (facing.sqrMagnitude < 0.01f) facing = Vector3.forward;
                    dir = Quaternion.AngleAxis(18f, Vector3.up) * Quaternion.AngleAxis(-6f, Vector3.Cross(facing.normalized, Vector3.up)) * facing.normalized;
                }
                else if (portrait)
                {
                    // head and shoulders, nearly face on
                    var h = b.size.y;
                    centre = new Vector3(b.center.x, b.min.y + h * 0.74f, b.center.z);
                    radius = Mathf.Max(h * 0.27f, Mathf.Min(b.extents.x, b.extents.z) * 0.6f);
                    dir = Quaternion.Euler(-6f, 198f, 0f) * Vector3.forward;   // the old world's villagers face −z
                }
                else
                {
                    // three-quarter view from a little above, like the island's camera
                    centre = b.center;
                    radius = b.extents.magnitude;
                    dir = Quaternion.Euler(-26f, 145f, 0f) * Vector3.forward;   // buildings face −z: seen from the front-left
                }
                var dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * (portrait ? 1.0f : 0.92f);
                cam.transform.position = centre + dir * dist;
                cam.transform.LookAt(centre);

                var black = Shoot(cam, Color.black);
                var white = Shoot(cam, Color.white);
                var outPx = new Color32[black.Length];
                for (var i = 0; i < black.Length; i++)
                {
                    var k = black[i];
                    var w = white[i];
                    // what changes with the background is how see-through it is
                    var a = 1f - ((w.r - k.r) + (w.g - k.g) + (w.b - k.b)) / (3f * 255f);
                    a = Mathf.Clamp01(a);
                    if (a < 0.01f) { outPx[i] = new Color32(0, 0, 0, 0); continue; }
                    outPx[i] = new Color32(
                        (byte)Mathf.Clamp(k.r / a, 0, 255),
                        (byte)Mathf.Clamp(k.g / a, 0, 255),
                        (byte)Mathf.Clamp(k.b / a, 0, 255),
                        (byte)Mathf.RoundToInt(a * 255f));
                }
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.SetPixels32(outPx);
                tex.Apply();
                var png = tex.EncodeToPNG();
                Object.DestroyImmediate(tex);
                return png;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static Color32[] Shoot(Camera cam, Color background)
        {
            var rt = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
            cam.backgroundColor = background;
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }

        /// <summary>
        /// Every picture goes before a full render: the old renders (older models, things the packs have no model for)
        /// leave the project, and only what the packs draw comes back.
        /// </summary>
        private static void Purge()
        {
            var old = new List<string>();
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { PicturesDir })) old.Add(AssetDatabase.GUIDToAssetPath(g));
            foreach (var g in AssetDatabase.FindAssets("Isl_ t:Texture2D", new[] { UiArtDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileName(p).StartsWith("Isl_")) old.Add(p);
            }
            var failed = new List<string>();
            AssetDatabase.MoveAssetsToTrash(old.ToArray(), failed);   // to the recycle bin
            Debug.Log($"[Pictures] purged {old.Count - failed.Count} old pictures" + (failed.Count > 0 ? $" ({failed.Count} could not go)" : ""));
        }

        private const string UiArtDir = "Assets/Farmverse/Content/UI/Game/Resources/GameUi/Art";

        private static void Import(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter imp) return;
            imp.textureType = TextureImporterType.Default;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.maxTextureSize = Size;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
        }

        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
