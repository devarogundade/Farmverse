#nullable enable
using System.Collections.Generic;
using System.Linq;
using Farmverse.Presentation.World;
using Farmverse.Presentation.World.Island;
using UnityEditor;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// Farmverse ▸ World ▸ Check Grounding: finds models hanging in the air. For every model on the
    /// island (nature props, and in play mode everything the farm spawned) it compares the bottom of
    /// the model with the terrain under its footprint; a model whose base is above the ground
    /// everywhere under it is floating. Run it with the Island scene open or while playing.
    /// </summary>
    public static class GroundingCheck
    {
        private const float Tolerance = 0.06f;

        [MenuItem("Farmverse/World/Check Grounding")]
        public static void Run()
        {
            var roots = new List<Transform>();
            foreach (var p in Object.FindObjectsByType<NatureProp>(FindObjectsInactive.Exclude)) roots.Add(p.transform);
            var floating = new List<(string Name, float Gap)>();
            var checkedCount = 0;
            foreach (var root in roots)
            {
                var renderers = root.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
                if (renderers.Length == 0) continue;
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                // the highest ground under the middle of the footprint: if even that is below the model's base, it floats
                var half = new Vector2(Mathf.Min(b.extents.x, 0.6f), Mathf.Min(b.extents.z, 0.6f));
                var top = float.MinValue;
                for (var j = -1; j <= 1; j++)
                    for (var i = -1; i <= 1; i++)
                        if (GroundHeight.TryAt(b.center.x + i * half.x, b.center.z + j * half.y, out var y)) top = Mathf.Max(top, y);
                if (top == float.MinValue) continue;
                checkedCount++;
                var gap = b.min.y - top;
                if (gap > Tolerance) floating.Add((Path(root), gap));
            }
            floating.Sort((a, c) => c.Gap.CompareTo(a.Gap));
            var report = $"[Grounding] checked {checkedCount} models, {floating.Count} floating (> {Tolerance * 100:0} cm)";
            if (floating.Count > 0) report += ":\n" + string.Join("\n", floating.Take(40).Select(f => $"  {f.Name}  +{f.Gap:0.00} m"));
            if (floating.Count > 0) Debug.LogWarning(report); else Debug.Log(report);
        }

        private static string Path(Transform t) => t.parent == null ? t.name : t.parent.name + "/" + t.name;
    }
}
