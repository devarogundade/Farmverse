#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// All the nature props on the island (children of this object). On start the ones the player
    /// has already cleared are removed and the rest take their cells on the grid.
    /// Tripo trees sway on the GPU (Farmverse/TreeSway); any older blockout tree is rocked here
    /// instead, and only near the camera.
    /// </summary>
    public sealed class IslandNature : MonoBehaviour
    {
        [SerializeField] private float swayDegrees = 1.6f;
        [SerializeField] private float swayRadius = 140f;

        private readonly Dictionary<int, NatureProp> _props = new Dictionary<int, NatureProp>();   // standing ones
        private readonly List<NatureProp> _all = new List<NatureProp>();
        private readonly List<(Transform t, Quaternion rest, float phase)> _trees = new List<(Transform, Quaternion, float)>();
        private Camera? _cam;

        public IEnumerable<NatureProp> Props => _props.Values;

        private void Awake()
        {
            foreach (var p in GetComponentsInChildren<NatureProp>())
            {
                _props[p.index] = p;
                _all.Add(p);
                if (p.kind is not ("rock" or "bush" or "pond" or "flowers" or "mushroom") && !p.shaderSway)
                    _trees.Add((p.transform, p.transform.localRotation, p.index * 0.37f));
            }
        }

        private readonly Dictionary<int, NatureProp> _later = new Dictionary<int, NatureProp>();   // on land the island hasn't grown yet
        private readonly HashSet<int> _cleared = new HashSet<int>();
        private IslandGrid? _grid;
        private int _chapter = 1;

        /// <summary>
        /// Take the props' cells on the grid, leaving out the cleared ones and those on land of later chapters
        /// (hidden until <see cref="ShowChapter"/>).
        /// </summary>
        public void Setup(IslandGrid grid, IEnumerable<int> cleared, int chapter = 1)
        {
            _grid = grid;
            _chapter = chapter;
            foreach (var i in cleared)
            {
                _cleared.Add(i);
                if (_props.TryGetValue(i, out var p)) { _props.Remove(i); p.gameObject.SetActive(false); }
            }
            foreach (var p in _props.Values.Where(p => p.chapter > chapter).ToList())
            {
                _props.Remove(p.index);
                _later[p.index] = p;
                p.gameObject.SetActive(false);
            }
            foreach (var p in _props.Values) grid.Occupy(p.cells, -(p.index + 1));
        }

        /// <summary>The island grew: the props on the new land take their places (popping up if <paramref name="animate"/>).</summary>
        public void ShowChapter(int chapter, bool animate)
        {
            if (chapter <= _chapter && _later.Count == 0) { _chapter = Mathf.Max(_chapter, chapter); return; }
            _chapter = Mathf.Max(_chapter, chapter);
            if (_grid == null) return;
            var n = 0;
            foreach (var p in _later.Values.Where(p => p.chapter <= _chapter).OrderBy(p => p.index).ToList())
            {
                _later.Remove(p.index);
                if (_cleared.Contains(p.index)) continue;
                if (_grid.Cells(p.cells).Any(c => _grid.OwnerAt(c.x, c.y) != 0)) continue;   // something stands there already
                _props[p.index] = p;
                _grid.Occupy(p.cells, -(p.index + 1));
                p.gameObject.SetActive(true);
                if (animate && n++ < 400) Magic.Appear(p.gameObject, 0.6f + (n % 7) * 0.08f, new Color(0.6f, 1f, 0.5f));
            }
        }

        /// <summary>The prop with this index if it's still standing.</summary>
        public NatureProp? Find(int index) => _props.TryGetValue(index, out var p) ? p : null;

        /// <summary>Any prop by index, standing or not.</summary>
        public NatureProp? Any(int index) => _all.FirstOrDefault(p => p.index == index);

        /// <summary>A cleared prop grows back (a sapling springs up, a rock turns up).</summary>
        public void Regrow(NatureProp p, IslandGrid grid)
        {
            if (_props.ContainsKey(p.index) || p.chapter > _chapter) return;
            _cleared.Remove(p.index);
            p.gameObject.SetActive(true);
            _props[p.index] = p;
            grid.Occupy(p.cells, -(p.index + 1));
            Magic.Appear(p.gameObject, 1.2f, new Color(0.6f, 1f, 0.5f));
        }

        /// <summary>Gone without a show (it stood where something was already built).</summary>
        public void Hide(NatureProp p)
        {
            _cleared.Add(p.index);
            _props.Remove(p.index);
            p.gameObject.SetActive(false);
        }

        public void Clear(NatureProp p, IslandGrid grid)
        {
            _cleared.Add(p.index);
            grid.Release(p.cells);
            _props.Remove(p.index);
            Magic.Vanish(p.gameObject, false, new Color(0.7f, 1f, 0.6f));   // shrinks away in a puff of leaves
        }

        /// <summary>Hide the props of chapters after this one again (the island shown smaller, for testing).</summary>
        public void SinkAbove(int chapter, IslandGrid grid)
        {
            _chapter = chapter;
            foreach (var p in _all.Where(p => p.chapter > chapter))
            {
                if (_props.Remove(p.index)) grid.Release(p.cells);
                _later[p.index] = p;
                p.gameObject.SetActive(false);
            }
        }

        /// <summary>Brings every cleared prop back (a fresh island).</summary>
        public void RestoreAll(IslandGrid grid, int chapter = 1)
        {
            _cleared.Clear();
            _chapter = chapter;
            // land of later chapters sinks again: its props wait for it
            foreach (var p in _all.Where(p => p.chapter > chapter))
            {
                if (_props.Remove(p.index)) grid.Release(p.cells);
                _later[p.index] = p;
                p.gameObject.SetActive(false);
            }
            foreach (var p in _all)
            {
                if (_props.ContainsKey(p.index) || _later.ContainsKey(p.index)) continue;
                p.gameObject.SetActive(true);
                _props[p.index] = p;
                grid.Occupy(p.cells, -(p.index + 1));
            }
        }

        private void Update()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            float t = Time.time;
            var c = _cam.transform.position;
            float r2 = swayRadius * swayRadius;
            for (int i = 0; i < _trees.Count; i++)
            {
                var (tr, rest, ph) = _trees[i];
                if (!tr.gameObject.activeSelf) continue;
                var p = tr.position;
                float dx = p.x - c.x, dz = p.z - c.z;
                if (dx * dx + dz * dz > r2) continue;
                // two slow sines per tree, phase by position so a gust rolls across the woods
                float g = Mathf.Sin(t * 1.1f + p.x * 0.05f + ph) + 0.4f * Mathf.Sin(t * 2.3f + p.z * 0.07f);
                tr.localRotation = rest * Quaternion.Euler(g * swayDegrees, 0, g * swayDegrees * 0.6f);
            }
        }
    }
}
