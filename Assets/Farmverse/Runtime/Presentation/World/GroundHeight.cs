#nullable enable
using System.Collections.Generic;
using Farmverse.Presentation.World.Island;
using UnityEngine;

namespace Farmverse.Presentation.World
{
    /// <summary>
    /// The height of the island's terrain, and only the terrain: buildings, fences, trees and other
    /// models never count as ground, so nothing ends up perched on something else. Models are stood
    /// on the lowest point under their whole footprint (not just their centre) and settled in a
    /// touch, so on a slope no corner hangs in the air.
    /// </summary>
    public static class GroundHeight
    {
        /// <summary>How far a model is settled below the lowest ground under it, so no edge shows a gap.</summary>
        public const float Sink = 0.04f;

        private const float Top = 200f;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static readonly Dictionary<Collider, bool> IsGroundCache = new Dictionary<Collider, bool>();

        /// <summary>The terrain height at a point; false off the island (open sea) or before it has loaded.</summary>
        public static bool TryAt(float x, float z, out float y)
        {
            y = 0f;
            var n = Physics.RaycastNonAlloc(new Vector3(x, Top, z), Vector3.down, Hits, Top * 2f, ~0, QueryTriggerInteraction.Ignore);
            var found = false;
            for (var i = 0; i < n; i++)
            {
                var c = Hits[i].collider;
                if (c == null || !IsGround(c)) continue;
                if (!found || Hits[i].point.y > y)
                {
                    y = Hits[i].point.y;
                    found = true;
                }
            }
            return found;
        }

        public static float At(float x, float z, float fallback) => TryAt(x, z, out var y) ? y : fallback;

        /// <summary>
        /// How far the lowest point of a model sits above (+) or below (−) its own origin, in world
        /// metres at its current scale. Subtract it when standing the origin on the ground so the
        /// feet (or hooves, or base) touch it: no floating, no sinking, whatever the model's pivot.
        /// </summary>
        public static float FootOffset(GameObject go)
        {
            var found = false;
            var min = 0f;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                var y = r.bounds.min.y;
                if (!found || y < min) { min = y; found = true; }
            }
            return found ? min - go.transform.position.y : 0f;
        }

        /// <summary>The lowest terrain under a footprint: centre ± half extents (turned by <paramref name="yawDegrees"/>), on a 3×3 grid.</summary>
        public static bool Lowest(Vector3 centre, Vector2 half, float yawDegrees, out float y)
        {
            y = 0f;
            var found = false;
            var turn = Quaternion.Euler(0f, yawDegrees, 0f);
            for (var j = -1; j <= 1; j++)
                for (var i = -1; i <= 1; i++)
                {
                    var o = turn * new Vector3(i * half.x, 0f, j * half.y);
                    if (!TryAt(centre.x + o.x, centre.z + o.z, out var h)) continue;
                    y = found ? Mathf.Min(y, h) : h;
                    found = true;
                }
            return found;
        }

        /// <summary>Stand a model on the terrain at its current x/z and facing.</summary>
        public static void Settle(Transform model, Vector2 half)
        {
            if (!Lowest(model.position, half, model.eulerAngles.y, out var y)) return;
            var p = model.position;
            p.y = y - Sink;
            model.position = p;
        }

        /// <summary>The collider is part of the island's terrain.</summary>
        public static bool IsGround(Collider c)
        {
            if (!IsGroundCache.TryGetValue(c, out var ground))
            {
                ground = c.GetComponentInParent<IslandGround>() != null;
                if (IsGroundCache.Count > 4096) IsGroundCache.Clear();
                IsGroundCache[c] = ground;
            }
            return ground;
        }
    }
}
