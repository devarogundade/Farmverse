#nullable enable
using UnityEditor;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// Farmverse plays in landscape only (the game UI is laid out for wide screens; on the web the page shows
    /// phones held upright a "turn your phone" screen and locks landscape where the browser allows). Enforced on
    /// every editor load so a stray Player Settings click cannot ship portrait: auto-rotation between the two
    /// landscape orientations only. Also sets the project's icon.
    /// </summary>
    [InitializeOnLoad]
    public static class FarmversePlayerSettings
    {
        static FarmversePlayerSettings() => EditorApplication.delayCall += Apply;

        /// <summary>The UI kit's clover (not in git: the icon stays unset in a clone without the kit).</summary>
        private const string CloverIcon = "Assets/Farmverse/Content/UI/Game/Resources/GameUi/Art/Icon_Clover.png";

        [MenuItem("Farmverse/Apply Player Settings", priority = 30)]
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
            Set(PlayerSettings.allowedAutorotateToPortrait, false, v => PlayerSettings.allowedAutorotateToPortrait = v);
            Set(PlayerSettings.allowedAutorotateToPortraitUpsideDown, false, v => PlayerSettings.allowedAutorotateToPortraitUpsideDown = v);
            Set(PlayerSettings.allowedAutorotateToLandscapeLeft, true, v => PlayerSettings.allowedAutorotateToLandscapeLeft = v);
            Set(PlayerSettings.allowedAutorotateToLandscapeRight, true, v => PlayerSettings.allowedAutorotateToLandscapeRight = v);

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
                Debug.Log("[Farmverse] Player settings set: landscape only, clover icon.");
            }
        }
    }
}
