#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// A glowing outline round one building at a time: the one the farmer is standing in (to hand
    /// goods over) or working at. Made from copies of the building's meshes drawn with
    /// Farmverse/Outline, removed as soon as the farmer walks away.
    /// </summary>
    public sealed class Highlight : MonoBehaviour
    {
        private static Material? _material;
        private GameObject? _target;
        private readonly List<GameObject> _shells = new List<GameObject>();

        /// <summary>Outline this building (null clears it).</summary>
        public void Show(GameObject? target, Color colour)
        {
            if (target == _target)
            {
                if (_material != null) _material.color = colour;
                return;
            }
            Clear();
            _target = target;
            if (target == null) return;
            var mat = Material();
            if (mat == null) return;
            mat.color = colour;
            foreach (var mf in target.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() is not { enabled: true }) continue;
                var shell = new GameObject("Outline");
                shell.transform.SetParent(mf.transform, false);
                shell.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = shell.AddComponent<MeshRenderer>();
                var mats = new Material[mf.sharedMesh.subMeshCount];
                for (var i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _shells.Add(shell);
            }
        }

        public void Clear()
        {
            foreach (var s in _shells) if (s != null) Destroy(s);
            _shells.Clear();
            _target = null;
        }

        private static Material? Material()
        {
            if (_material != null) return _material;
            var shader = Shader.Find("Farmverse/Outline");
            if (shader == null) return null;
            _material = new Material(shader) { name = "Outline (runtime)" };
            return _material;
        }
    }
}
