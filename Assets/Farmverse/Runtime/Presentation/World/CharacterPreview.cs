#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World
{
    /// <summary>
    /// A little stage for the Customize screen: one dressed character, its own camera, rendered into a
    /// texture the UI shows; and thumbnails of single parts for the wardrobe grid. The stage stands far below
    /// the island on its own layer, which the main camera never draws; its own camera is enabled (rendering
    /// into the texture), so Unity culls and skins the model for it.
    /// Made on demand; goes away a moment after the screen stops asking for it (<see cref="Touch"/>).
    /// </summary>
    public sealed class CharacterPreview : MonoBehaviour
    {
        private const int Layer = 31;
        private static CharacterPreview? _live;
        private static readonly Dictionary<string, Texture2D> Thumbs = new Dictionary<string, Texture2D>();

        private Camera? _cam;
        private RenderTexture? _rt;
        private Transform? _stage;
        private GameObject? _model;
        private CharacterLook? _look;
        private GameObject? _prefab;
        private float _yaw = -20f;
        private float _lastTouch;
        private int _settle;   // frames to wait after a mesh swap before drawing (a swapped skinned mesh can't be drawn the same frame)
        private GameObject? _thumbRoot;
        private MeshFilter? _thumbPart, _thumbBody;

        /// <summary>The stage (made if needed). Call <see cref="Touch"/> every frame the screen is up.</summary>
        public static CharacterPreview Get()
        {
            if (_live != null) return _live;
            var go = new GameObject("Character preview") { hideFlags = HideFlags.DontSave };
            _live = go.AddComponent<CharacterPreview>();
            _live.Build();
            return _live;
        }

        /// <summary>What the stage draws, for the UI (a render texture).</summary>
        public Texture? Texture => _rt;

        /// <summary>The look the shown model wears now.</summary>
        public string Look { get; private set; } = "";

        public void Touch() => _lastTouch = Time.unscaledTime;

        private void Build()
        {
            _lastTouch = Time.unscaledTime;
            Ride();
            // the Studio scene holds the stage's lights and turntable (plan: Unity scenes); loaded on top once
            if (UnityEngine.Application.CanStreamedLevelBeLoaded("Studio") && !UnityEngine.SceneManagement.SceneManager.GetSceneByName("Studio").isLoaded)
                UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Studio", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            _stage = new GameObject("Stage").transform;
            _stage.SetParent(transform, false);

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0, 0, 0, 0);
            _cam.cullingMask = 1 << Layer;
            _cam.fieldOfView = 22f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 60f;
            _cam.allowHDR = false;
            _cam.depth = -50;
            _cam.enabled = true;    // a live camera into the texture: it culls the stage, so the skinned model is skinned
            _rt = new RenderTexture(640, 900, 24, RenderTextureFormat.ARGB32) { name = "character_preview", antiAliasing = 4 };
            _rt.Create();
            _cam.targetTexture = _rt;
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        }

        /// <summary>The stage stands far below the island; the main camera never draws its layer.</summary>
        private void Ride()
        {
            transform.SetPositionAndRotation(new Vector3(0f, -1500f, 0f), Quaternion.identity);
            foreach (var c in Camera.allCameras)
                if (c != _cam && (c.cullingMask & (1 << Layer)) != 0) c.cullingMask &= ~(1 << Layer);
        }

        /// <summary>Put a role's model on the stage wearing <paramref name="look"/>.</summary>
        public void Show(GameObject prefab, string look, RuntimeAnimatorController? controller = null)
        {
            if (_stage == null) return;
            if (_model == null || _prefab != prefab)
            {
                // clear the stage completely (whatever is there), hiding at once since Destroy waits for the frame's end
                for (var i = _stage.childCount - 1; i >= 0; i--)
                {
                    var old = _stage.GetChild(i).gameObject;
                    old.SetActive(false);
                    Destroy(old);
                }
                _model = null;
                _prefab = prefab;
                _model = Instantiate(prefab, _stage);
                _model.transform.localPosition = Vector3.zero;
                foreach (var m in _model.GetComponentsInChildren<MonoBehaviour>(true))
                    if (m is not CharacterLook && m is not CharacterMotion) m.enabled = false;
                foreach (var t in _model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                foreach (var a in _model.GetComponentsInChildren<Animator>(true))
                {
                    // the prefab's nested-prefab override of the controller doesn't survive Instantiate here
                    if (a.runtimeAnimatorController == null && controller != null) a.runtimeAnimatorController = controller;
                    a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
                foreach (var r in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
                _look = _model.GetComponent<CharacterLook>();
            }
            Look = look;
            _look?.Apply(look);
            Animate();
            _settle = 1;
            Frame(ModelBounds(_model), 0.12f, true);
        }

        /// <summary>Change what the shown model wears.</summary>
        public void Dress(string look)
        {
            Look = look;
            _look?.Apply(look);
            Animate();
            _settle = 1;
        }

        /// <summary>Whatever body is showing (a costume swaps it) animates and skins off screen too.</summary>
        private void Animate()
        {
            if (_model == null) return;
            foreach (var a in _model.GetComponentsInChildren<Animator>(true)) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var r in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
        }

        /// <summary>Turn the model by <paramref name="degrees"/> (dragging the picture).</summary>
        public void Spin(float degrees) => _yaw += degrees;

        private void Update()
        {
            if (Time.unscaledTime - _lastTouch > 1.5f) { Destroy(gameObject); return; }
            Ride();
            // the rig's skeleton faces +Z; its raw meshes face -Z (see Thumb)
            if (_model != null) _model.transform.localRotation = Quaternion.Euler(0f, 180f + _yaw, 0f);
        }

        private void LateUpdate()
        {
            if (_cam == null || _rt == null || _model == null) return;
            // the camera draws itself into the texture; skip a frame after a mesh swap
            _cam.enabled = _settle <= 0;
            if (_settle > 0) _settle--;
        }

        private void OnDestroy()
        {
            if (_live == this) _live = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }

        /// <summary>
        /// A picture of one part for the wardrobe grid: the part's mesh in its bind pose with the body behind it
        /// for context, framed on the part. Plain mesh renderers (a skinned mesh can't be swapped and drawn in the
        /// same frame). Cached for the session; the same for every role since they share the rig.
        /// </summary>
        public Texture2D? Thumb(CharacterParts parts, string slot, string mesh, int size = 160)
        {
            var key = slot + "=" + mesh;
            if (Thumbs.TryGetValue(key, out var t) && t != null) return t;
            if (_cam == null || _rt == null || _stage == null) return null;
            var m = parts.MeshOf(slot, mesh);
            if (m == null) return null;

            if (_thumbRoot == null)
            {
                _thumbRoot = new GameObject("Thumb") { layer = Layer };
                _thumbRoot.transform.SetParent(transform, false);
                _thumbRoot.transform.localPosition = new Vector3(20f, 0f, 0f);
                MeshFilter Part(string name)
                {
                    var go = new GameObject(name) { layer = Layer };
                    go.transform.SetParent(_thumbRoot.transform, false);
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = parts.material;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    return go.AddComponent<MeshFilter>();
                }
                _thumbPart = Part("Part");
                _thumbBody = Part("Body");
                var body = parts.Find("Body");
                if (body != null && body.options.Count > 0) _thumbBody.sharedMesh = body.options[0];
            }
            _thumbRoot.SetActive(true);
            _thumbPart!.sharedMesh = m;
            _thumbBody!.gameObject.SetActive(slot != "Body");
            _thumbRoot.transform.localRotation = Quaternion.Euler(0f, slot is "Shoes" or "Pants" ? 30f : 15f, 0f);

            var wasPos = _cam.transform.localPosition; var wasRot = _cam.transform.localRotation;
            var wasActive = RenderTexture.active;
            try
            {
                Frame(_thumbPart.GetComponent<Renderer>().bounds, 0.35f, false);
                var small = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
                _cam.targetTexture = small;
                _cam.Render();
                _cam.targetTexture = _rt;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "thumb_" + key };
                RenderTexture.active = small;
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply(false, true);
                RenderTexture.ReleaseTemporary(small);
                Thumbs[key] = tex;
                return tex;
            }
            finally
            {
                RenderTexture.active = wasActive;
                _thumbRoot.SetActive(false);
                _cam.transform.localPosition = wasPos; _cam.transform.localRotation = wasRot;
                _cam.aspect = (float)_rt.width / _rt.height;
            }
        }

        private static Bounds ModelBounds(GameObject go)
        {
            var b = new Bounds(go.transform.position + Vector3.up, Vector3.one * 0.1f);
            var any = false;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        /// <summary>Put the camera so <paramref name="b"/> fills the picture, from this stage's own front (its -Z), a little above eye level.</summary>
        private void Frame(Bounds b, float pad, bool tall)
        {
            if (_cam == null) return;
            var aspect = tall ? (float)_rt!.width / _rt.height : 1f;
            var half = Mathf.Max(b.extents.y, b.extents.x / aspect, b.extents.z / aspect) * (1f + pad);
            var dist = half / Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) + b.extents.z;
            var dir = transform.rotation * (Quaternion.Euler(tall ? 8f : 12f, 0f, 0f) * Vector3.back);
            _cam.transform.position = b.center + dir * dist;
            _cam.transform.LookAt(b.center, Vector3.up);
            _cam.aspect = aspect;
        }
    }
}
