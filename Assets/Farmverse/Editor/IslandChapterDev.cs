using Farmverse.Presentation.World.Island;
using UnityEditor;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// Farmverse / World / Island Chapter: while playing, show the island at a chapter at once, or play
    /// the growth to the next one (the chapter card's show), to check the land, the scenery and the tour.
    /// </summary>
    internal static class IslandChapterDev
    {
        const string Menu = "Farmverse/World/Island Chapter/";

        static IslandSandbox? Sandbox => Application.isPlaying ? Object.FindAnyObjectByType<IslandSandbox>() : null;

        [MenuItem(Menu + "Show Chapter 1")] static void One() => Sandbox?.DevSetChapter(1);
        [MenuItem(Menu + "Show Chapter 2")] static void Two() => Sandbox?.DevSetChapter(2);
        [MenuItem(Menu + "Show Chapter 3")] static void Three() => Sandbox?.DevSetChapter(3);
        [MenuItem(Menu + "Grow To Next Chapter")]
        static void Grow()
        {
            var sb = Sandbox;
            if (sb == null) return;
            sb.GrowIsland(sb.IslandChapter + 1, () => Debug.Log($"[Chapters] grown to chapter {sb.IslandChapter}"));
        }

        [MenuItem(Menu + "Show Chapter Card")]
        static void Card()
        {
            var sb = Sandbox;
            var ui = Object.FindAnyObjectByType<Farmverse.Presentation.UI.Game.GameUi>();
            if (sb == null || ui == null || sb.IslandChapter >= 3) return;
            ui.Open("chapter", sb.IslandChapter + 1);
        }

        [MenuItem(Menu + "Show Chapter Card", true)]
        [MenuItem(Menu + "Show Chapter 1", true)]
        [MenuItem(Menu + "Show Chapter 2", true)]
        [MenuItem(Menu + "Show Chapter 3", true)]
        [MenuItem(Menu + "Grow To Next Chapter", true)]
        static bool Playing() => Application.isPlaying;
    }
}
