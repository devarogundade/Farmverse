#nullable enable
using System.Runtime.InteropServices;
using UnityEngine;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// Where the game sends the player outside itself (the site's legal pages, the market, support), and
    /// the two things the browser page does for it: copy text to the clipboard and reload the game.
    /// </summary>
    public static class GameLinks
    {
        public const string SupportEmail = "support@farmverse.world";

        /// <summary>The landing site (the page's <c>siteUrl</c>: localhost in a dev build).</summary>
        public static string Site => PageValue("siteUrl", "https://farmverse.world");
        public static string Terms => Site + "/#/terms";
        public static string Privacy => Site + "/#/privacy";
        /// <summary>The marketplace (the page's <c>marketplaceUrl</c>).</summary>
        public static string Market => PageValue("marketplaceUrl", "https://marketplace.farmverse.world");

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void FvCopyText(string text);
        [DllImport("__Internal")] private static extern void FvReload();
        [DllImport("__Internal")] private static extern void FvOpenTab(string url, string label);
        [DllImport("__Internal")] private static extern string FvConfig(string key);
#endif

        /// <summary>Opens a link in a new tab (in the browser through the page, which offers the link itself if a popup is blocked).</summary>
        public static void Open(string url, string label = "")
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            FvOpenTab(url, label);
#else
            UnityEngine.Application.OpenURL(url);
#endif
        }

        private static string PageValue(string key, string fallback)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var value = FvConfig(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value.TrimEnd('/');
#else
            return fallback;
#endif
        }

        /// <summary>A support email with the player's id and version filled in.</summary>
        public static void EmailSupport(string playerId)
        {
            var subject = System.Uri.EscapeDataString("Farmverse support");
            var body = System.Uri.EscapeDataString($"\n\n—\nPlayer: {playerId}\nVersion: {UnityEngine.Application.version} ({UnityEngine.Application.platform})");
            UnityEngine.Application.OpenURL($"mailto:{SupportEmail}?subject={subject}&body={body}");
        }

        /// <summary>Copies text to the clipboard (in the browser through the page; the Editor's own clipboard otherwise).</summary>
        public static void Copy(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            FvCopyText(text);
#else
            GUIUtility.systemCopyBuffer = text;
#endif
        }

        /// <summary>Loads the game page again (the newest build); does nothing outside the browser.</summary>
        public static void Reload()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            FvReload();
#endif
        }
    }
}
