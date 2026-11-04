#nullable enable
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// The island's building grid, baked by the island builder (Farmverse / World / Build Island).
    /// One cell per <see cref="cellSize"/> metres. The island grows by chapter (docs: Island by Chapter):
    /// <see cref="heights"/> holds the ground per chapter and <see cref="buildable"/> a bit per chapter
    /// (bit c-1: buildable land in chapter c). A cell that is buildable in one chapter is buildable, at the
    /// same height, in every chapter after. <see cref="Chapter"/> is the chapter the island has grown to;
    /// every query answers for it. Row-major, x fastest.
    /// </summary>
    [CreateAssetMenu(menuName = "Farmverse/Island Data")]
    public sealed class IslandData : ScriptableObject
    {
        public Vector2 origin;          // world x,z of cell (0,0)'s corner
        public float cellSize = 2f;
        public int width;
        public int depth;
        public float seaLevel;
        public int chapters = 1;
        public int layoutVersion;       // changes when the land is redesigned (old saves forget cleared props)
        public float previousLayoutScale = 1f;   // how far the last redesign shrank the land (a save one layout behind moves its buildings in by this)
        public byte[] buildable = new byte[0];
        public float[] height = new float[0];           // the last chapter's ground (all land)
        public float[] heightCh1 = new float[0];        // chapter 1's ground
        public float[] heightCh2 = new float[0];        // chapter 2's ground
        public byte[] firstChapter = new byte[0];       // first chapter the cell is buildable in (0 = never)
        public Vector3[] railPath = new Vector3[0];     // the railway line, a point every metre, from the buffer stop out to the map's edge
        public float stationIndex;                      // metre along the line where the engine stops

        [System.NonSerialized] private int _chapter;

        /// <summary>The chapter the island has grown to (1 until told).</summary>
        public int Chapter
        {
            get => Mathf.Clamp(_chapter <= 0 ? 1 : _chapter, 1, Mathf.Max(1, chapters));
            set => _chapter = value;
        }

        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < width && z < depth;

        public bool IsBuildable(int x, int z) => IsBuildableIn(x, z, Chapter);

        public bool IsBuildableIn(int x, int z, int chapter)
        {
            if (!InBounds(x, z)) return false;
            var b = buildable[z * width + x];
            if (chapters <= 1) return b != 0;
            return (b & (1 << (Mathf.Clamp(chapter, 1, 8) - 1))) != 0;
        }

        public float HeightAt(int x, int z) => HeightIn(x, z, Chapter);

        public float HeightIn(int x, int z, int chapter)
        {
            if (!InBounds(x, z)) return seaLevel;
            int i = z * width + x;
            if (chapters > 1 && chapter <= 1 && heightCh1.Length == height.Length) return heightCh1[i];
            if (chapters > 2 && chapter == 2 && heightCh2.Length == height.Length) return heightCh2[i];
            return height[i];
        }

        /// <summary>The first chapter a cell is buildable land in (0 if never).</summary>
        public int FirstChapterOf(int x, int z) =>
            !InBounds(x, z) ? 0 : firstChapter.Length == height.Length ? firstChapter[z * width + x] : (buildable[z * width + x] != 0 ? 1 : 0);

        public Vector2Int CellOf(Vector3 world) =>
            new Vector2Int(Mathf.FloorToInt((world.x - origin.x) / cellSize), Mathf.FloorToInt((world.z - origin.y) / cellSize));

        public Vector3 CellCorner(int x, int z) => new Vector3(origin.x + x * cellSize, 0, origin.y + z * cellSize);
    }
}
