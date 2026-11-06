#nullable enable
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// The app id the web and desktop builds use: it has to match the app in the
    /// Firebase config files, or Firebase refuses to start. Applied automatically while the project still
    /// carries Unity's template ids; the menu item applies it on demand.
    /// </summary>
    [InitializeOnLoad]
    public static class FarmverseAppIdentity
    {
        public const string AppId = "world.farmverse.mobile";

        private static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.WebGL, NamedBuildTarget.Standalone };

        static FarmverseAppIdentity() => EditorApplication.delayCall += () =>
        {
            foreach (var target in Targets)
                if (PlayerSettings.GetApplicationIdentifier(target).Contains("unity.template")) { Apply(); return; }
        };

        [MenuItem("Farmverse/Setup/Apply App Identity", priority = 31)]
        public static void Apply()
        {
            foreach (var target in Targets) PlayerSettings.SetApplicationIdentifier(target, AppId);
            AssetDatabase.SaveAssets();
            Debug.Log("Farmverse: application id set to " + AppId + " for the web and desktop builds (matches the Firebase config files).");
        }
    }
}
