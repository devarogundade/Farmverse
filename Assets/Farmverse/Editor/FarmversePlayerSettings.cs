#nullable enable
using UnityEditor;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// Farmverse is portrait only, on phones, iPads and Android tablets. Enforced on
    /// every editor load so a stray Player Settings click cannot ship landscape:
    ///
    ///   * auto-rotation between the two portrait orientations only (an iPad can be
    ///     held either way up; phones simply stay upright);
    ///   * iPad "Requires full screen", which Apple needs for a portrait-only iPad
    ///     app (it opts out of Split View and Slide Over);
    ///   * Android: resizable activity off, so tablets and foldables keep the
    ///     portrait layout instead of a free-form window.
    /// </summary>
    [InitializeOnLoad]
    public static class FarmversePlayerSettings
    {
        static FarmversePlayerSettings() => EditorApplication.delayCall += Apply;

        [MenuItem("Farmverse/Apply Portrait Settings", priority = 30)]
        public static void Apply()
        {
            var changed = false;
            void Set<T>(T current, T wanted, System.Action<T> apply)
            {
                if (Equals(current, wanted)) return;
                apply(wanted);
                changed = true;
            }

            Set(PlayerSettings.defaultInterfaceOrientation, UIOrientation.AutoRotation, v => PlayerSettings.defaultInterfaceOrientation = v);
            Set(PlayerSettings.allowedAutorotateToPortrait, true, v => PlayerSettings.allowedAutorotateToPortrait = v);
            Set(PlayerSettings.allowedAutorotateToPortraitUpsideDown, true, v => PlayerSettings.allowedAutorotateToPortraitUpsideDown = v);
            Set(PlayerSettings.allowedAutorotateToLandscapeLeft, false, v => PlayerSettings.allowedAutorotateToLandscapeLeft = v);
            Set(PlayerSettings.allowedAutorotateToLandscapeRight, false, v => PlayerSettings.allowedAutorotateToLandscapeRight = v);
            Set(PlayerSettings.iOS.requiresFullScreen, true, v => PlayerSettings.iOS.requiresFullScreen = v);
            Set(PlayerSettings.Android.resizeableActivity, false, v => PlayerSettings.Android.resizeableActivity = v);

            if (changed)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[Farmverse] Player settings set to portrait only (phones, iPad full screen, tablets).");
            }
        }
    }
}
