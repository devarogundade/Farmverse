#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// Which island cells are free. A building occupies a rectangle of cells (its footprint, turned
    /// with the building); it may be placed where every cell is buildable land and not taken.
    /// Buildings keep one free cell of breathing room in front only if their prefab asks for it,
    /// otherwise they may stand side by side.
    /// </summary>
    public sealed class IslandGrid : MonoBehaviour
    {
        [SerializeField] private IslandData? data;

        private int[] _owner = new int[0];   // 0 = free, otherwise placement id

        public IslandData Data => data!;

        private void Awake()
        {
            if (data != null && _owner.Length != data.width * data.depth) _owner = new int[data.width * data.depth];
        }

        public bool CanPlace(RectInt cells)
        {
            var d = Data;
            for (int z = cells.yMin; z < cells.yMax; z++)
                for (int x = cells.xMin; x < cells.xMax; x++)
                {
                    if (!d.IsBuildable(x, z)) return false;
                    if (_owner[z * d.width + x] != 0) return false;
                }
            return true;
        }

        public void Occupy(RectInt cells, int id) => Fill(cells, id);
        public void Release(RectInt cells) => Fill(cells, 0);

        private void Fill(RectInt cells, int id)
        {
            var d = Data;
            for (int z = cells.yMin; z < cells.yMax; z++)
                for (int x = cells.xMin; x < cells.xMax; x++)
                    if (d.InBounds(x, z)) _owner[z * d.width + x] = id;
        }

        /// <summary>Lowest ground under the cells, settled a touch, so no corner of a building floats on a slope.</summary>
        public float GroundHeight(RectInt cells)
        {
            var d = Data;
            float h = float.MaxValue;
            for (int z = cells.yMin; z < cells.yMax; z++)
                for (int x = cells.xMin; x < cells.xMax; x++)
                    h = Mathf.Min(h, d.HeightAt(x, z));
            return h == float.MaxValue ? d.seaLevel : h - Farmverse.Presentation.World.GroundHeight.Sink;
        }

        /// <summary>The footprint rectangle whose centre is nearest the given world point.</summary>
        public RectInt FootprintAt(Vector3 world, Vector2Int size)
        {
            var d = Data;
            int x0 = Mathf.RoundToInt((world.x - d.origin.x) / d.cellSize - size.x * 0.5f);
            int z0 = Mathf.RoundToInt((world.z - d.origin.y) / d.cellSize - size.y * 0.5f);
            return new RectInt(x0, z0, size.x, size.y);
        }

        public Vector3 CentreOf(RectInt cells)
        {
            var d = Data;
            return new Vector3(d.origin.x + (cells.xMin + cells.width * 0.5f) * d.cellSize,
                               GroundHeight(cells),
                               d.origin.y + (cells.yMin + cells.height * 0.5f) * d.cellSize);
        }

        /// <summary>Nearest free spot for a footprint, searching outwards from a point (for the starter pack).</summary>
        public bool FindFree(Vector3 near, Vector2Int size, out RectInt cells, int maxRings = 80)
        {
            var start = FootprintAt(near, size);
            for (int r = 0; r <= maxRings; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        var c = new RectInt(start.xMin + dx, start.yMin + dz, size.x, size.y);
                        // leave a one-cell gap around starter buildings so paths fit between them
                        var padded = new RectInt(c.xMin - 1, c.yMin - 1, c.width + 2, c.height + 2);
                        if (CanPlace(padded)) { cells = c; return true; }
                    }
            }
            cells = default;
            return false;
        }

        /// <summary>Who stands on a cell: 0 free, a placement id (building), or a negative id (tree, rock).</summary>
        public int OwnerAt(int x, int z) => Data.InBounds(x, z) && _owner.Length > 0 ? _owner[z * Data.width + x] : 0;

        /// <summary>Dry land someone could walk on (beaches and slopes too, not the sea).</summary>
        public bool Dry(int x, int z) => Data.InBounds(x, z) && Data.HeightAt(x, z) > Data.seaLevel + 0.35f;

        public IEnumerable<Vector2Int> Cells(RectInt r)
        {
            for (int z = r.yMin; z < r.yMax; z++)
                for (int x = r.xMin; x < r.xMax; x++)
                    yield return new Vector2Int(x, z);
        }
    }
}
