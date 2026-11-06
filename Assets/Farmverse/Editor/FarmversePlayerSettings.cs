#nullable enable
using UnityEditor;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// Farmverse is laid out portrait first. Enforced on every editor load so a stray
    /// Player Settings click cannot ship landscape: auto-rotation between the two
    /// portrait orientations only (used by browsers that honour orientation). The
    /// phone-only settings (iOS full screen, Android resizable activity) went with the
    /// move to the web build.
    /// </summary>
    [InitializeOnLoad]
    public static class FarmversePlayerSettings
    {
        static FarmversePlayerSettings() => EditorApplication.delayCall += Apply;

        /// <summary>The UI kit's clover (not in git: the icon stays unset in a clone without the kit).</summary>
        private const string CloverIcon = "Assets/Farmverse/Content/UI/Game/Resources/GameUi/Art/Icon_Clover.png";

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

            // the project's icon: the clover (also the sites' favicon and the FARM token's icon)
            var clover = AssetDatabase.LoadAssetAtPath<Texture2D>(CloverIcon);
            var icons = PlayerSettings.GetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, IconKind.Any);
            if (clover != null && (icons.Length == 0 || icons[0] != clover))
            {
                PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { clover }, IconKind.Any);
                changed = true;
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[Farmverse] Player settings set: portrait only, clover icon.");
            }
        }
    }
}
