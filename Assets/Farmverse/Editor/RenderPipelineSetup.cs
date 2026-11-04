#nullable enable
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Farmverse.Editor
{
    /// <summary>
    /// The game renders with URP: every world material uses URP shaders, so without a
    /// pipeline asset they draw magenta. This creates the project's one URP asset
    /// (Content/Rendering/FarmverseURP.asset with its Universal Renderer) and assigns it
    /// as the default pipeline and to every quality level. Called by Set Up Boot Scene;
    /// safe to run again. URP is reached by reflection, as in WorldArtSetup, so the
    /// editor assembly needs no extra reference.
    /// </summary>
    public static class RenderPipelineSetup
    {
        private const string Folder = "Assets/Farmverse/Content/Rendering";
        private const string AssetPath = Folder + "/FarmverseURP.asset";
        private const string UrpAssembly = "Unity.RenderPipelines.Universal.Runtime";

        [MenuItem("Farmverse/Setup/Assign Render Pipeline (URP)", priority = 30)]
        public static void Run()
        {
            if (Ensure()) Debug.Log("Farmverse: URP is the render pipeline (" + AssetPath + ").");
        }

        /// <summary>True when URP is assigned everywhere afterwards.</summary>
        public static bool Ensure()
        {
            var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetPath) ?? Create();
            if (asset == null) return false;

            if (GraphicsSettings.defaultRenderPipeline != asset) GraphicsSettings.defaultRenderPipeline = asset;
            var current = QualitySettings.GetQualityLevel();
            for (var level = 0; level < QualitySettings.names.Length; level++)
            {
                var levelAsset = QualitySettings.GetRenderPipelineAssetAt(level);
                if (levelAsset == asset) continue;
                QualitySettings.SetQualityLevel(level, applyExpensiveChanges: false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, applyExpensiveChanges: false);
            AssetDatabase.SaveAssets();
            return true;
        }

        private static RenderPipelineAsset? Create()
        {
            var assetType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, " + UrpAssembly);
            var dataType = Type.GetType("UnityEngine.Rendering.Universal.ScriptableRendererData, " + UrpAssembly);
            if (assetType == null || dataType == null) return Fail("the Universal RP package is not installed");

            // the URP menu's own path: a Universal Renderer saved beside the asset, then the asset around it
            var makeRenderer = assetType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "CreateRendererAsset" && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(string));
            var create = assetType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public, null, new[] { dataType }, null);
            if (makeRenderer == null || create == null) return Fail("this URP version has no CreateRendererAsset/Create");

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Farmverse/Content", "Rendering");
            var args = makeRenderer.GetParameters().Select((p, i) =>
                i == 0 ? AssetPath
                : p.ParameterType.IsEnum ? Enum.Parse(p.ParameterType, "UniversalRenderer")
                : p.ParameterType == typeof(bool) ? (object)true
                : p.HasDefaultValue ? p.DefaultValue
                : null).ToArray();
            var renderer = makeRenderer.Invoke(null, args) as ScriptableObject;
            if (renderer == null) return Fail("the Universal Renderer could not be created");
            if (!AssetDatabase.Contains(renderer)) AssetDatabase.CreateAsset(renderer, Folder + "/FarmverseURP_Renderer.asset");

            var asset = create.Invoke(null, new object[] { renderer }) as RenderPipelineAsset;
            if (asset == null) return Fail("the URP asset could not be created");
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static RenderPipelineAsset? Fail(string why)
        {
            Debug.LogError("Farmverse: could not set up URP (" + why + "). Create one with Assets > Create > Rendering > URP Asset (with Universal Renderer) and assign it in Project Settings > Graphics and Quality.");
            return null;
        }
    }
}
