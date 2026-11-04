#nullable enable
using UnityEngine;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// Where the game sends the player outside itself: the site's legal pages, support, and the store
    /// listing (to update or rate). Fill <see cref="AppStoreId"/> once the App Store listing exists.
    /// </summary>
    public static class GameLinks
    {
        public const string Site = "https://farmverse.world";
        public const string Terms = Site + "/terms";
        public const string Privacy = Site + "/privacy";
        public const string Support = Site + "/support";
        public const string SupportEmail = "support@farmverse.world";
        public const string AndroidPackage = "world.farmverse.mobile";
        /// <summary>The numeric App Store id (App Store Connect ▸ App Information ▸ Apple ID); empty until listed.</summary>
        public const string AppStoreId = "";

        /// <summary>The game's store page on this platform.</summary>
        public static string StorePage
        {
            get
            {
#if UNITY_IOS
                return AppStoreId.Length > 0 ? "itms-apps://apps.apple.com/app/id" + AppStoreId : Site;
#else
                return "https://play.google.com/store/apps/details?id=" + AndroidPackage;
#endif
            }
        }

        public static void Open(string url) => UnityEngine.Application.OpenURL(url);

        /// <summary>A support email with the player's id and version filled in.</summary>
        public static void EmailSupport(string playerId)
        {
            var subject = System.Uri.EscapeDataString("Farmverse support");
            var body = System.Uri.EscapeDataString($"\n\n—\nPlayer: {playerId}\nVersion: {UnityEngine.Application.version} ({UnityEngine.Application.platform})");
            UnityEngine.Application.OpenURL($"mailto:{SupportEmail}?subject={subject}&body={body}");
        }

        /// <summary>Ask for a rating: the system's own review sheet on iOS, the store page elsewhere.</summary>
        public static void Rate()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (UnityEngine.iOS.Device.RequestStoreReview()) return;
#endif
            UnityEngine.Application.OpenURL(StorePage);
        }
    }
}
