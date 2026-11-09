#nullable enable
using UnityEditor;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// Farmverse plays in landscape only (the game UI is laid out for wide screens; on the web the page shows
    /// phones held upright a "turn your phone" screen and locks landscape where the browser allows). Enforced on
    /// every editor load so a stray Player Settings click cannot ship portrait: auto-rotation between the two
    /// landscape orientations only. Also sets the project's icon and the splash screen (our art, no Unity logo).
    /// </summary>
    [InitializeOnLoad]
    public static class FarmversePlayerSettings
    {
        static FarmversePlayerSettings() => EditorApplication.delayCall += Apply;

        /// <summary>The UI kit's clover (not in git: the icon stays unset in a clone without the kit).</summary>
        private const string CloverIcon = "Assets/Farmverse/Content/UI/Game/Resources/GameUi/Art/Icon_Clover.png";

        /// <summary>The splash: Home_LoadingBg with Home_Logo on top, as the title screen shows them (imported as a sprite).</summary>
        public const string SplashArt = "Assets/Farmverse/Content/Splash/Farmverse_Splash.png";

        /// <summary>Behind the splash where the picture doesn't reach: the sea's teal.</summary>
        private static readonly Color SplashSky = new Color(0.30f, 0.66f, 0.72f, 1f);

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

            // the splash: our title art with the logo, alone (no "Made with Unity" logo, no dimming or blur, no logo sequence)
            var splash = AssetDatabase.LoadAssetAtPath<Sprite>(SplashArt);
            if (splash != null)
            {
                Set(PlayerSettings.SplashScreen.show, true, v => PlayerSettings.SplashScreen.show = v);
                Set(PlayerSettings.SplashScreen.showUnityLogo, false, v => PlayerSettings.SplashScreen.showUnityLogo = v);
                Set(PlayerSettings.SplashScreen.logos.Length, 0, _ => PlayerSettings.SplashScreen.logos = new PlayerSettings.SplashScreenLogo[0]);
                Set(PlayerSettings.SplashScreen.background, splash, v => PlayerSettings.SplashScreen.background = v);
                Set(PlayerSettings.SplashScreen.backgroundPortrait, splash, v => PlayerSettings.SplashScreen.backgroundPortrait = v);
                Set(PlayerSettings.SplashScreen.overlayOpacity, 0f, v => PlayerSettings.SplashScreen.overlayOpacity = v);
                Set(PlayerSettings.SplashScreen.blurBackgroundImage, false, v => PlayerSettings.SplashScreen.blurBackgroundImage = v);   // sharp, as the title screen shows it
                Set(PlayerSettings.SplashScreen.animationMode, PlayerSettings.SplashScreen.AnimationMode.Static, v => PlayerSettings.SplashScreen.animationMode = v);
                Set(PlayerSettings.SplashScreen.backgroundColor, SplashSky, v => PlayerSettings.SplashScreen.backgroundColor = v);
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[Farmverse] Player settings set: landscape only, clover icon, Farmverse splash.");
            }
        }
    }
}
