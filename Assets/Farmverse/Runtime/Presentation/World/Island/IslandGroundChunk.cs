#nullable enable
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// One 80 m square of the island's ground, baked once per chapter (the same mesh where a chapter
    /// changes nothing there). <see cref="IslandChapters"/> swaps the mesh, and blends between two while
    /// new land rises from the sea.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public sealed class IslandGroundChunk : MonoBehaviour
    {
        public Mesh[] chapters = new Mesh[0];   // index 0 = chapter 1

        public Mesh? For(int chapter) => chapters.Length == 0 ? null : chapters[Mathf.Clamp(chapter, 1, chapters.Length) - 1];
    }
}
