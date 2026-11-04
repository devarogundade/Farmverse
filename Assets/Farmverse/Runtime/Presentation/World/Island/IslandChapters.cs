#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// The island grows with its chapters (docs: Island by Chapter): small in chapter 1, a river valley
    /// with a lake, hills and a cove in chapter 2, mountains, a waterfall, a town and a harbour in
    /// chapter 3. Everything is baked once (ground per chapter, water masks per chapter, scenery tagged
    /// with its chapters); this switches the island to a chapter at once (<see cref="Apply"/>) or plays
    /// the growth: the new land rises out of the sea, its trees and landmarks pop up, and the camera
    /// tours the new places (<see cref="Grow"/>). Nothing the player built moves: only land around the
    /// old shore changes.
    /// </summary>
    public sealed class IslandChapters : MonoBehaviour
    {
        [SerializeField] private IslandData? data;
        [SerializeField] private IslandCamera? islandCamera;
        [SerializeField] private GameObject? groundRoot;
        [SerializeField] private Texture2D[] waterMasks = new Texture2D[0];
        [SerializeField] private MeshRenderer[] waterRenderers = new MeshRenderer[0];
        [SerializeField] private MeshRenderer[] mistRenderers = new MeshRenderer[0];
        [SerializeField] private IslandNature? nature;
        [SerializeField] private int[] tourChapter = new int[0];
        [SerializeField] private string[] tourName = new string[0];
        [SerializeField] private Vector3[] tourAt = new Vector3[0];
        [SerializeField] private float[] tourDistance = new float[0];
        [SerializeField, Tooltip("How far the camera may wander from the middle, per chapter.")] private float[] cameraRadius = { 135f, 215f, 300f };
        [SerializeField] private float riseSeconds = 5f;

        private IslandGroundChunk[] _chunks = new IslandGroundChunk[0];
        private IslandChapterTag[] _tags = new IslandChapterTag[0];
        private Material[] _waterMats = new Material[0];
        private Material[] _mistMats = new Material[0];
        private bool _skip;

        /// <summary>The chapter the island shows now.</summary>
        public int Chapter { get; private set; } = 1;
        public int Chapters => data != null ? Mathf.Max(1, data.chapters) : 1;
        /// <summary>True while the growth is playing.</summary>
        public bool Growing { get => _growing; private set { _growing = value; AnyGrowing = value; } }
        private bool _growing;

        /// <summary>The island is growing (the villagers hold their chatter, nothing else asks for the screen).</summary>
        public static bool AnyGrowing { get; private set; }
        /// <summary>The place the tour is showing ("The lake"), or null.</summary>
        public string? TourLabel { get; private set; }

        public IslandData? Data => data;

        /// <summary>The island's chapters (for scenes loaded on top, like the events', to show only what this chapter has).</summary>
        public static IslandChapters? Instance { get; private set; }

        /// <summary>Take in the chapter-tagged scenery of a scene loaded later (an event's), shown as the island stands now.</summary>
        public void Adopt(IEnumerable<IslandChapterTag> tags)
        {
            var more = tags.Where(t => t != null).ToArray();
            _tags = _tags.Where(t => t != null).Concat(more).ToArray();
            foreach (var t in more) t.gameObject.SetActive(t.ShownIn(Chapter));
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Awake()
        {
            Instance = this;
            _chunks = groundRoot != null ? groundRoot.GetComponentsInChildren<IslandGroundChunk>(true) : new IslandGroundChunk[0];
            _tags = FindObjectsByType<IslandChapterTag>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            // own copies, so switching masks never writes into the material assets
            _waterMats = waterRenderers.Where(r => r != null).Select(r => r.material).Distinct().ToArray();
            _mistMats = mistRenderers.Where(r => r != null).Select(r => r.material).ToArray();
            Apply(1);
        }

        /// <summary>The smallest chapter whose land holds all these cells (for old islands and visits).</summary>
        public int ChapterHolding(IEnumerable<RectInt> cells)
        {
            if (data == null) return 1;
            var need = 1;
            foreach (var r in cells)
                for (int z = r.yMin; z < r.yMax; z++)
                    for (int x = r.xMin; x < r.xMax; x++)
                    {
                        var c = data.FirstChapterOf(x, z);
                        need = Mathf.Max(need, c == 0 ? Chapters : c);
                    }
            return Mathf.Min(need, Chapters);
        }

        /// <summary>Show the island as it is in this chapter, at once.</summary>
        public void Apply(int chapter, bool withNature = true)
        {
            chapter = Mathf.Clamp(chapter, 1, Chapters);
            Chapter = chapter;
            if (data != null) data.Chapter = chapter;
            foreach (var c in _chunks) SetGround(c, c.For(chapter));
            SetMask(chapter);
            foreach (var t in _tags) if (t != null) t.gameObject.SetActive(t.ShownIn(chapter));
            if (withNature && nature != null) nature.ShowChapter(chapter, false);
            if (islandCamera != null) islandCamera.Radius = cameraRadius[Mathf.Clamp(chapter - 1, 0, cameraRadius.Length - 1)];
        }

        private static void SetGround(IslandGroundChunk chunk, Mesh? mesh)
        {
            if (mesh == null) return;
            var mf = chunk.GetComponent<MeshFilter>();
            if (mf.sharedMesh != mesh) mf.sharedMesh = mesh;
            if (chunk.TryGetComponent<MeshCollider>(out var col) && col.sharedMesh != mesh) col.sharedMesh = mesh;
        }

        private void SetMask(int chapter)
        {
            if (waterMasks.Length == 0) return;
            var mask = waterMasks[Mathf.Clamp(chapter - 1, 0, waterMasks.Length - 1)];
            if (mask == null) return;
            foreach (var m in _waterMats) if (m != null && m.HasProperty("_Mask")) m.SetTexture("_Mask", mask);
            foreach (var m in _mistMats) if (m != null && m.HasProperty("_Mask")) m.SetTexture("_Mask", mask);
        }

        /// <summary>Jump to the end of the growth (the player tapped to skip).</summary>
        public void Skip() => _skip = true;

        /// <summary>
        /// Play the growth to <paramref name="to"/>: the camera pulls up, the new land rises from the sea,
        /// its scenery appears, the camera tours the new places, then comes back. <paramref name="done"/>
        /// runs at the end (also when skipped).
        /// </summary>
        public void Grow(int to, Action? done = null)
        {
            to = Mathf.Clamp(to, 1, Chapters);
            if (Growing || to <= Chapter) { done?.Invoke(); return; }
            StartCoroutine(GrowRoutine(to, done));
        }

        private IEnumerator GrowRoutine(int to, Action? done)
        {
            Growing = true;
            _skip = false;
            var cam = islandCamera;
            var follow = cam != null ? cam.Follow : null;
            var pivot = cam != null ? cam.Pivot : Vector3.zero;
            var dist = cam != null ? cam.Distance : 70f;
            bool panWas = cam != null && cam.PanEnabled;
            if (cam != null) { cam.Follow = null; cam.PanEnabled = false; cam.Radius = cameraRadius[Mathf.Clamp(to - 1, 0, cameraRadius.Length - 1)]; }

            int from = Chapter;
            for (int step = from + 1; step <= to && !_skip; step++)
            {
                // 1. pull up and out over the middle
                yield return CameraTo(Vector3.zero, cameraRadius[Mathf.Clamp(step - 1, 0, cameraRadius.Length - 1)], 1.4f);
                if (_skip) break;
                // the jetty and the like go as their bay fills
                foreach (var t in _tags)
                    if (t != null && t.gameObject.activeSelf && !t.ShownIn(step)) Magic.Vanish(t.gameObject, false);
                IslandAudio.Play("magic");
                // 2. the land rises
                var blends = _chunks.Select(c => (chunk: c, a: c.For(step - 1), b: c.For(step))).Where(x => x.a != null && x.b != null && x.a != x.b).ToList();
                var work = blends.Select(x => Blend.Make(x.a!, x.b!)).ToList();
                for (int i = 0; i < blends.Count; i++) blends[i].chunk.GetComponent<MeshFilter>().sharedMesh = work[i].Mesh;
                Sparkles(step);
                float t0 = Time.unscaledTime;
                bool masked = false;
                while (!_skip)
                {
                    float k = Mathf.Clamp01((Time.unscaledTime - t0) / riseSeconds);
                    float e = k * k * (3f - 2f * k);
                    foreach (var w in work) w.Set(e);
                    if (!masked && k > 0.55f) { SetMask(step); masked = true; }
                    if (k >= 1f) break;
                    yield return null;
                }
                foreach (var w in work) w.Dispose();
                Apply(step);
                // 3. its trees, rocks and landmarks pop up
                foreach (var t in _tags)
                    if (t != null && t.fromChapter == step && t.gameObject.activeSelf) Magic.Appear(t.gameObject, 0.8f, new Color(0.7f, 0.95f, 1f));
                if (nature != null) nature.ShowChapter(step, true);
                IslandAudio.Play("cheer");
                if (_skip) break;
                yield return Wait(1.2f);
                // 4. the tour
                for (int i = 0; i < tourAt.Length && !_skip; i++)
                {
                    if (i >= tourChapter.Length || tourChapter[i] != step) continue;
                    TourLabel = i < tourName.Length ? tourName[i] : null;
                    yield return CameraTo(tourAt[i], i < tourDistance.Length && tourDistance[i] > 0 ? tourDistance[i] : 130f, 2f);
                    yield return Wait(2.6f);
                }
                TourLabel = null;
            }
            // skipped: land straight on the end
            if (Chapter != to) Apply(to);
            if (nature != null) nature.ShowChapter(to, false);
            TourLabel = null;
            // 5. back to the farmer
            if (!_skip) yield return CameraTo(follow != null ? follow.position : pivot, dist, 1.4f);
            if (cam != null)
            {
                cam.Pivot = new Vector3((follow != null ? follow.position : pivot).x, 0, (follow != null ? follow.position : pivot).z);
                cam.Distance = dist;
                cam.Follow = follow;
                cam.PanEnabled = panWas;
            }
            Growing = false;
            done?.Invoke();
        }

        private IEnumerator Wait(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end && !_skip) yield return null;
        }

        private IEnumerator CameraTo(Vector3 at, float distance, float seconds)
        {
            var cam = islandCamera;
            if (cam == null) yield break;
            var p0 = cam.Pivot; var d0 = cam.Distance;
            var p1 = new Vector3(at.x, 0, at.z);
            float t0 = Time.unscaledTime;
            while (!_skip)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - t0) / seconds);
                float e = k * k * (3f - 2f * k);
                cam.Pivot = Vector3.Lerp(p0, p1, e);
                cam.Distance = Mathf.Lerp(d0, distance, e);
                if (k >= 1f) break;
                yield return null;
            }
        }

        /// <summary>A ring of sparkles along the new shore while it rises.</summary>
        private void Sparkles(int chapter)
        {
            if (data == null) return;
            for (int i = 0; i < 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Vector3? shore = null;
                for (float r = 30f; r < 315f; r += 2f)
                {
                    var c = data.CellOf(dir * r);
                    if (!data.InBounds(c.x, c.y)) break;
                    if (data.HeightIn(c.x, c.y, chapter) < data.seaLevel + 0.4f) { shore = dir * r; break; }
                }
                if (shore is { } s) Magic.Burst(s + Vector3.up * 1.2f, new Color(0.75f, 0.95f, 1f), 14, 3f);
            }
        }

        /// <summary>A ground mesh between two chapters: positions, normals and colours eased from one to the other.</summary>
        private sealed class Blend
        {
            public Mesh Mesh = null!;
            private Vector3[] _va = null!, _vb = null!, _na = null!, _nb = null!, _v = null!, _n = null!;
            private Color[] _ca = null!, _cb = null!, _c = null!;
            private Vector2[] _ua = null!, _ub = null!, _u = null!;

            public static Blend Make(Mesh a, Mesh b)
            {
                var x = new Blend { Mesh = Instantiate(a) };
                x._va = a.vertices; x._vb = b.vertices; x._na = a.normals; x._nb = b.normals;
                x._ca = a.colors; x._cb = b.colors; x._ua = a.uv; x._ub = b.uv;
                x._v = new Vector3[x._va.Length]; x._n = new Vector3[x._va.Length]; x._c = new Color[x._ca.Length]; x._u = new Vector2[x._ua.Length];
                x.Mesh.MarkDynamic();
                return x;
            }

            public void Set(float t)
            {
                for (int i = 0; i < _v.Length; i++) { _v[i] = Vector3.LerpUnclamped(_va[i], _vb[i], t); _n[i] = Vector3.LerpUnclamped(_na[i], _nb[i], t); }
                for (int i = 0; i < _c.Length; i++) _c[i] = Color.LerpUnclamped(_ca[i], _cb[i], t);
                for (int i = 0; i < _u.Length; i++) _u[i] = Vector2.LerpUnclamped(_ua[i], _ub[i], t);
                Mesh.vertices = _v; Mesh.normals = _n;
                if (_c.Length == _v.Length) Mesh.colors = _c;
                if (_u.Length == _v.Length) Mesh.uv = _u;
                Mesh.RecalculateBounds();
            }

            public void Dispose() => Destroy(Mesh);
        }

        /// <summary>The places a chapter's growth tours, in order (for the chapter card).</summary>
        public IEnumerable<string> TourNames(int chapter)
        {
            for (int i = 0; i < tourChapter.Length && i < tourName.Length; i++)
                if (tourChapter[i] == chapter) yield return tourName[i];
        }

        /// <summary>Tour stops, baked by the island builder.</summary>
        public void SetTour(IEnumerable<(int chapter, string name, Vector3 at, float distance)> stops)
        {
            var list = stops.ToList();
            tourChapter = list.Select(s => s.chapter).ToArray();
            tourName = list.Select(s => s.name).ToArray();
            tourAt = list.Select(s => s.at).ToArray();
            tourDistance = list.Select(s => s.distance).ToArray();
        }
    }
}
