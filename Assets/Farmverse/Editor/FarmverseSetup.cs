#nullable enable
using System.IO;
using System.Linq;
using Farmverse.Bootstrap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Editor
{
    /// <summary>
    /// One click from an empty project to a playable boot scene: environment
    /// configs, panel settings, the world prefabs and their visual catalog
    /// (<see cref="WorldArtSetup"/>), and the boot scene: one GameRoot that opens the
    /// Island scene. Safe to run again: existing assets are reused, the scene is rebuilt.
    /// </summary>
    public static class FarmverseSetup
    {
        private const string Root = "Assets/Farmverse/Content";
        private const string ScenePath = "Assets/Farmverse/Scenes/Boot.unity";
        public const string IslandScenePath = "Assets/Farmverse/Scenes/Island.unity";

        [MenuItem("Farmverse/Set Up Boot Scene", priority = 0)]
        public static void SetUp()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Folder("Assets/Farmverse/Scenes");
            Folder(Root + "/Config/Environments");
            Folder(Root + "/UI");
            RenderPipelineSetup.Ensure(); // URP first: the world's materials are URP materials

            var dev = Environment("dev", "http://localhost:8787");
            Environment("staging", "https://api.staging.farmverse.world");
            Environment("prod", "https://api.farmverse.world");
            Panel();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("GameRoot").AddComponent<GameRoot>();
            Set(root, "environment", dev);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuild(ScenePath);
            // the game itself: GameRoot opens it
            if (File.Exists(IslandScenePath)) AddSceneToBuild(IslandScenePath);
            else Debug.LogWarning("Farmverse setup: " + IslandScenePath + " is missing; run Farmverse ▸ World ▸ Build Island.");
            Verify();
            AssetDatabase.SaveAssets();
            Debug.Log("Farmverse boot scene ready at " + ScenePath + ". Press Play with the game server running (backend/server: npm run dev, FARMVERSE_ENV=dev).");
        }

        /// <summary>Fail loudly in the editor rather than with a null at Play.</summary>
        private static void Verify()
        {
            var root = Object.FindAnyObjectByType<GameRoot>();
            var so = new SerializedObject(root);
            foreach (var field in new[] { "environment" })
                if (so.FindProperty(field)?.objectReferenceValue == null)
                    Debug.LogError("Farmverse setup: GameRoot." + field + " is not assigned in " + ScenePath + ".", root);
        }

        private static EnvironmentConfig Environment(string name, string apiBaseUrl)
        {
            var path = Root + "/Config/Environments/Environment." + name + ".asset";
            var config = AssetDatabase.LoadAssetAtPath<EnvironmentConfig>(path);
            if (config == null && File.Exists(path))
            {
                // the file is there but did not load as an EnvironmentConfig (stale import after a compile error): reimport, never overwrite
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                config = AssetDatabase.LoadAssetAtPath<EnvironmentConfig>(path);
                if (config == null) throw new System.InvalidOperationException(path + " exists but is not an EnvironmentConfig; fix or delete it and run the setup again.");
            }
            if (config != null) return config;
            config = ScriptableObject.CreateInstance<EnvironmentConfig>();
            var so = new SerializedObject(config);
            so.FindProperty("environmentName").stringValue = name;
            so.FindProperty("apiBaseUrl").stringValue = apiBaseUrl;
            so.FindProperty("minimumLog").enumValueIndex = name == "prod" ? 2 : 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(config, path);
            return config;
        }

        private static PanelSettings Panel()
        {
            var path = Root + "/UI/PanelSettings.asset";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (panel != null)
            {
                // keep older projects on the design's scaling too
                panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panel.referenceResolution = new Vector2Int(390, 844);
                panel.screenMatchMode = PanelScreenMatchMode.Expand;
                EditorUtility.SetDirty(panel);
                return panel;
            }
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(390, 844); // the design canvas is 390 x 844 dp
            // Expand: the 390 x 844 design box always fits; tall phones gain height, iPads and tablets gain width
            panel.screenMatchMode = PanelScreenMatchMode.Expand;
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Root + "/UI/FarmverseTheme.tss");
            if (theme != null) panel.themeStyleSheet = theme;
            else Debug.LogWarning("Farmverse: " + Root + "/UI/FarmverseTheme.tss is missing; assign a theme to " + path + ".");
            AssetDatabase.CreateAsset(panel, path);
            return panel;
        }

        private static void Set(Object target, string field, Object? value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"Farmverse setup: {target.GetType().Name} has no serialized field '{field}'.");
                return;
            }
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Adds a scene to the build after the boot scene (kept if it is already there).</summary>
        public static void AddSceneToBuild(string path)
        {
            if (EditorBuildSettings.scenes.Any(s => s.path == path)) return;
            var scenes = EditorBuildSettings.scenes.Where(s => File.Exists(s.path)).ToList();
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void AddToBuild(string path)
        {
            // keep other existing scenes, drop entries whose file is gone (the legacy scenes)
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path && File.Exists(s.path)).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
