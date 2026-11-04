using System.Collections.Generic;
using System.IO;
using System.Linq;
using Farmverse.Presentation.World.Island;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Farmverse.Editor
{
    /// <summary>
    /// The game's scenes (plan: Unity scenes), in build order:
    ///  0 Boot (Farmverse ▸ Setup), 1 Island (Build Island), 2 Farmhouse (the five rooms, loaded on top for
    ///  "Go inside"), 3 Studio (the character stage's light rig and turntable, loaded on top for Customize),
    ///  4 Event_ZombieNight (October dressing) and 5 Event_WinterFestival (December dressing), both made by the
    ///  island build from the landmarks' event decor and loaded on top by IslandFestival.
    /// The packs' demo scenes stay in Assets/ithappy and never go in the build.
    /// </summary>
    public static class IslandScenes
    {
        const string Dir = "Assets/Farmverse/Scenes/";
        public const string Boot = Dir + "Boot.unity", Island = Dir + "Island.unity", Farmhouse = Dir + "Farmhouse.unity",
            Studio = Dir + "Studio.unity", ZombieNight = Dir + "Event_ZombieNight.unity", WinterFestival = Dir + "Event_WinterFestival.unity";

        [MenuItem("Farmverse/World/Build Scenes (Farmhouse, Studio, build list)")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Scenes] stop Play mode first"); return; }
            BuildFarmhouse();
            BuildStudio();
            BuildList();
        }

        /// <summary>Called by the island build before it saves Island: the event decor goes to its own scenes.</summary>
        public static bool SplitEvents()
        {
            var ok = Split(IslandLandmarks.HalloweenDecor, ZombieNight);
            ok &= Split(IslandLandmarks.FestivalDecor, WinterFestival);
            return ok;
        }

        static bool Split(GameObject? root, string path)
        {
            if (root == null) return false;
            var rootName = root.name;
            var active = SceneManager.GetActiveScene();
            root.transform.SetParent(null, true);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.SetActive(true);
            var saved = EditorSceneManager.SaveScene(scene, path);
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(active);
            Debug.Log($"[Scenes] {Path.GetFileNameWithoutExtension(path)}: {rootName} saved ({saved})");
            return saved;
        }

        /// <summary>The five rooms in a row 40 m apart, far from the island, as IslandSandbox expects them.</summary>
        public static void BuildFarmhouse()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var prev = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("Farmhouse rooms");
            root.transform.position = new Vector3(4000f, 300f, 4000f);
            int i = 0;
            foreach (var name in new[] { "living", "kitchen", "bedroom", "bathroom", "study" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{IthappyRooms.Rooms}/room_{name}.prefab");
                if (prefab == null) continue;
                var room = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                room.transform.localPosition = new Vector3(i * 40f, 0, 0);
                room.transform.localRotation = Quaternion.identity;
                foreach (var c in room.GetComponentsInChildren<Collider>()) c.enabled = false;
                i++;
            }
            root.SetActive(false);   // shown when you go inside
            EditorSceneManager.SaveScene(scene, Farmhouse);
            SceneManager.SetActiveScene(prev);
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"[Scenes] Farmhouse: {i} rooms");
        }

        /// <summary>The character stage's lights and turntable, on the stage's layer, where CharacterPreview stands.</summary>
        public static void BuildStudio()
        {
            const int layer = 31;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var prev = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("Studio") { layer = layer };
            root.transform.position = new Vector3(0f, -1500f, 0f);
            Light L(string n, Vector3 at, Color c, float intensity, float range)
            {
                var go = new GameObject(n) { layer = layer };
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = at;
                var l = go.AddComponent<Light>();
                l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
                return l;
            }
            L("Key", new Vector3(1.6f, 2.6f, 2.2f), new Color(1f, 0.93f, 0.82f), 3.2f, 9f);
            L("Fill", new Vector3(-2.2f, 1.4f, 1.6f), new Color(0.75f, 0.85f, 1f), 1.4f, 8f);
            L("Rim", new Vector3(0f, 2.4f, -2.4f), new Color(1f, 0.95f, 0.9f), 2.0f, 7f);
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Turntable";
            disc.layer = layer;
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(root.transform, false);
            disc.transform.localPosition = new Vector3(0f, -0.04f, 0f);
            disc.transform.localScale = new Vector3(1.4f, 0.04f, 1.4f);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.36f, 0.58f, 0.86f));
            mat.SetFloat("_Smoothness", 0.35f);
            Directory.CreateDirectory("Assets/Farmverse/Content/Rendering");
            AssetDatabase.CreateAsset(mat, "Assets/Farmverse/Content/Rendering/Studio_Turntable.mat");
            disc.GetComponent<MeshRenderer>().sharedMaterial = mat;
            EditorSceneManager.SaveScene(scene, Studio);
            SceneManager.SetActiveScene(prev);
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log("[Scenes] Studio saved");
        }

        /// <summary>Boot 0, Island 1, Farmhouse 2, Studio 3, Event_ZombieNight 4, Event_WinterFestival 5.</summary>
        public static void BuildList()
        {
            var list = new List<EditorBuildSettingsScene>();
            foreach (var p in new[] { Boot, Island, Farmhouse, Studio, ZombieNight, WinterFestival })
                if (File.Exists(p)) list.Add(new EditorBuildSettingsScene(p, true));
                else Debug.LogWarning("[Scenes] missing " + p);
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[Scenes] build list: " + string.Join(", ", list.Select(s => Path.GetFileNameWithoutExtension(s.path))));
        }
    }
}
