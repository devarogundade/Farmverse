using Farmverse.Presentation.UI.Game;
using Farmverse.Presentation.World.Island;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Editor
{
    /// <summary>
    /// The new game UI's setup: its pictures import sharp (no compression, no mipmaps, clamped), and
    /// "Farmverse ▸ UI ▸ Install Game UI" puts <see cref="GameUi"/> on the island scene's "Island UI"
    /// (where the old IslandUi was) without rebuilding the island.
    /// </summary>
    public sealed class GameUiTextureImport : AssetPostprocessor
    {
        private const string Folder = "Assets/Farmverse/Content/UI/Game/";

        /// <summary>Raised whenever the import rules change, so Unity re-imports the pictures with them.</summary>
        public override uint GetVersion() => 2;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.alphaIsTransparency = true;
            ti.sRGBTexture = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 2048;
        }

        /// <summary>
        /// The pack's soft glows (…Glow, Popup_Light) fade through very low alpha. In the project's linear colour
        /// space that faint tail blends far brighter than in the design (sRGB), which shows as a hard grey disc.
        /// Their alpha is reshaped (a^2.2) on import so they fade like the design.
        /// </summary>
        private void OnPostprocessTexture(Texture2D texture)
        {
            if (!assetPath.StartsWith(Folder) || PlayerSettings.colorSpace != ColorSpace.Linear) return;
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (!(name.Contains("Glow") || name == "Popup_Light")) return;
            var px = texture.GetPixels32();
            for (var i = 0; i < px.Length; i++)
                px[i].a = (byte)Mathf.RoundToInt(Mathf.Pow(px[i].a / 255f, 2.2f) * 255f);
            texture.SetPixels32(px);
            texture.Apply(false);
        }
    }

    public static class GameUiSetup
    {
        private const string Scene = "Assets/Farmverse/Scenes/Island.unity";
        private const string Panel = "Assets/Farmverse/Content/UI/PanelSettings.asset";

        [MenuItem("Farmverse/UI/Install Game UI")]
        public static void Install()
        {
            AssetDatabase.ImportAsset("Assets/Farmverse/Content/UI/Game", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

            GameObject go = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "Island UI") go = root;
            if (go == null) go = new GameObject("Island UI");

            var missing = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);   // the archived IslandUi
            var doc = go.GetComponent<UIDocument>();
            if (doc == null) doc = go.AddComponent<UIDocument>();
            if (doc.panelSettings == null) doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(Panel);
            doc.sortingOrder = 10;

            var ui = go.GetComponent<GameUi>();
            if (ui == null) ui = go.AddComponent<GameUi>();
            var so = new SerializedObject(ui);
            so.FindProperty("sandbox").objectReferenceValue = Object.FindAnyObjectByType<IslandSandbox>();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[GameUiSetup] Game UI installed on '{go.name}' in {Scene} (removed {missing} missing script(s)). Press Play: the gear at the top right opens Settings.");
        }
    }
}
