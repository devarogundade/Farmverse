#nullable enable
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// The app id every platform must build with: it has to match the package name in
    /// google-services.json and the bundle id in GoogleService-Info.plist, or Firebase
    /// refuses to start on the device. Applied automatically while the project still
    /// carries Unity's template ids; the menu item applies it on demand.
    /// </summary>
    [InitializeOnLoad]
    public static class FarmverseAppIdentity
    {
        public const string AppId = "world.farmverse.mobile";

        private static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.Standalone };

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
            Debug.Log("Farmverse: application id set to " + AppId + " for Android, iOS and desktop (matches the Firebase config files).");
        }
    }
}
