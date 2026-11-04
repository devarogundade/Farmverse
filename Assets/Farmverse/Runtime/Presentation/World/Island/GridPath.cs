#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// Walking routes for the island's people: A* over the building grid's cells (dry land, not
    /// through buildings, trees or rocks, except the ones a caller lets them cross, such as fields),
    /// then pulled straight wherever the way between two points is clear, so people walk in long
    /// natural lines and turn only at corners. Cheap enough to ask for a route whenever someone
    /// picks a new job.
    /// </summary>
    public static class GridPath
    {
        private static int[] _seen = new int[0];      // search stamp per cell
        private static float[] _cost = new float[0];
        private static int[] _from = new int[0];
        private static int _stamp;
        private static readonly List<(float f, int cell)> Heap = new List<(float, int)>();

        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>
        /// A route from <paramref name="from"/> to <paramref name="to"/> as world points (the last one is
        /// <paramref name="to"/> itself). Cells owned by something <paramref name="mayCross"/> accepts can be
        /// walked through; the start and end cells always can. False (and a straight line) when there is no way.
        /// </summary>
        public static bool Find(IslandGrid grid, Vector3 from, Vector3 to, Func<int, bool>? mayCross, List<Vector3> into, int maxNodes = 60000)
        {
            into.Clear();
            var d = grid.Data;
            var a = d.CellOf(from);
            var b = d.CellOf(to);
            if (!d.InBounds(a.x, a.y) || !d.InBounds(b.x, b.y) || a == b) { into.Add(to); return a == b; }
            var n = d.width * d.depth;
            if (_seen.Length != n) { _seen = new int[n]; _cost = new float[n]; _from = new int[n]; _stamp = 0; }
            _stamp++;
            if (_stamp == int.MaxValue) { Array.Clear(_seen, 0, n); _stamp = 1; }
            var start = a.y * d.width + a.x;
            var goal = b.y * d.width + b.x;
            bool Open(int x, int z)
            {
                if (!grid.Dry(x, z)) return false;
                var c = z * d.width + x;
                if (c == start || c == goal) return true;
                var owner = grid.OwnerAt(x, z);
                return owner == 0 || (mayCross != null && mayCross(owner));
            }
            Heap.Clear();
            _seen[start] = _stamp; _cost[start] = 0; _from[start] = -1;
            Push(H(a.x, a.y, b), start);
            var expanded = 0;
            var found = false;
            while (Heap.Count > 0 && expanded++ < maxNodes)
            {
                var cur = Pop();
                if (cur == goal) { found = true; break; }
                int cx = cur % d.width, cz = cur / d.width;
                for (var i = 0; i < 8; i++)
                {
                    int nx = cx + Dx[i], nz = cz + Dz[i];
                    if (!d.InBounds(nx, nz) || !Open(nx, nz)) continue;
                    if (i >= 4 && (!Open(cx + Dx[i], cz) || !Open(cx, cz + Dz[i]))) continue;   // no squeezing past corners
                    var nc = nz * d.width + nx;
                    var g = _cost[cur] + (i >= 4 ? 1.4142f : 1f);
                    if (_seen[nc] == _stamp && g >= _cost[nc]) continue;
                    _seen[nc] = _stamp; _cost[nc] = g; _from[nc] = cur;
                    Push(g + H(nx, nz, b), nc);
                }
            }
            if (!found) { into.Add(to); return false; }
            // cells back to front, then pulled straight
            var cells = new List<int>();
            for (var c = goal; c >= 0; c = _from[c]) cells.Add(c);
            cells.Reverse();
            var anchor = 0;
            while (anchor < cells.Count - 1)
            {
                var far = anchor + 1;
                for (var j = cells.Count - 1; j > anchor + 1; j--)
                    if (Clear(cells[anchor], cells[j], d.width, Open)) { far = j; break; }
                if (far == cells.Count - 1) break;
                into.Add(Centre(d, cells[far]));
                anchor = far;
            }
            into.Add(to);
            return true;
        }

        private static Vector3 Centre(IslandData d, int cell) =>
            d.CellCorner(cell % d.width, cell / d.width) + new Vector3(d.cellSize * 0.5f, 0, d.cellSize * 0.5f);

        /// <summary>Every cell a straight walk between two cell centres touches is open.</summary>
        private static bool Clear(int a, int b, int width, Func<int, int, bool> open)
        {
            float ax = a % width + 0.5f, az = a / width + 0.5f, bx = b % width + 0.5f, bz = b / width + 0.5f;
            var steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(bx - ax), Mathf.Abs(bz - az)) * 3f);
            for (var i = 1; i < steps; i++)
            {
                var t = i / (float)steps;
                float x = ax + (bx - ax) * t, z = az + (bz - az) * t;
                // a body is about a cell wide: check the cells either side of the line too
                if (!open(Mathf.FloorToInt(x), Mathf.FloorToInt(z))) return false;
                if (!open(Mathf.FloorToInt(x + 0.3f), Mathf.FloorToInt(z + 0.3f)) || !open(Mathf.FloorToInt(x - 0.3f), Mathf.FloorToInt(z - 0.3f))) return false;
            }
            return true;
        }

        private static float H(int x, int z, Vector2Int b)
        {
            int dx = Mathf.Abs(x - b.x), dz = Mathf.Abs(z - b.y);
            return dx + dz + (1.4142f - 2f) * Mathf.Min(dx, dz);
        }

        private static void Push(float f, int cell)
        {
            Heap.Add((f, cell));
            var i = Heap.Count - 1;
            while (i > 0)
            {
                var p = (i - 1) / 2;
                if (Heap[p].f <= Heap[i].f) break;
                (Heap[p], Heap[i]) = (Heap[i], Heap[p]);
                i = p;
            }
        }

        private static int Pop()
        {
            var top = Heap[0].cell;
            var last = Heap[Heap.Count - 1];
            Heap.RemoveAt(Heap.Count - 1);
            if (Heap.Count == 0) return top;
            Heap[0] = last;
            var i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < Heap.Count && Heap[l].f < Heap[m].f) m = l;
                if (r < Heap.Count && Heap[r].f < Heap[m].f) m = r;
                if (m == i) break;
                (Heap[m], Heap[i]) = (Heap[i], Heap[m]);
                i = m;
            }
            return top;
        }
    }
}
