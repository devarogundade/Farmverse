#nullable enable
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// Scenery that belongs to some chapters of the island only (docs: Island by Chapter): shown from
    /// <see cref="fromChapter"/> on, and up to <see cref="untilChapter"/> (the chapter 1 jetty goes when
    /// its bay fills with land). <see cref="IslandChapters"/> switches it as the island grows.
    /// </summary>
    public sealed class IslandChapterTag : MonoBehaviour
    {
        public int fromChapter = 1;
        public int untilChapter = 99;

        public bool ShownIn(int chapter) => chapter >= fromChapter && chapter <= untilChapter;
    }
}
