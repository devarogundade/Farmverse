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
        public const string Site = "https://farmverse.world";
        public const string Terms = Site + "/#/terms";
        public const string Privacy = Site + "/#/privacy";
        public const string Market = "https://marketplace.farmverse.world";
        public const string SupportEmail = "support@farmverse.world";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void FvCopyText(string text);
        [DllImport("__Internal")] private static extern void FvReload();
#endif

        public static void Open(string url) => UnityEngine.Application.OpenURL(url);

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
