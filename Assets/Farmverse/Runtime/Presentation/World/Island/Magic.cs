#nullable enable
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// The island's little bits of magic: things never just pop in or out. A finished building
    /// springs up with a burst of sparkles, a sold building or a cleared tree shrinks away in a puff,
    /// people arriving or leaving shimmer. All code-made (no assets), cheap, and self-cleaning.
    /// </summary>
    public static class Magic
    {
        private static Material? _sparkMaterial;

        /// <summary>Grow something in from nothing with a little overshoot.</summary>
        public static void Appear(GameObject go, float seconds = 0.45f, Color? colour = null)
        {
            var t = go.GetComponent<MagicTween>() ?? go.AddComponent<MagicTween>();
            t.Begin(MagicTween.Kind.Appear, seconds, false);
            IslandAudio.Play("whoosh", go.transform.position, 0.35f, 0.15f, 0.25f, 1.25f);
            Burst(go.transform.position + Vector3.up * 0.5f, colour ?? new Color(1f, 0.92f, 0.55f), 26, Size(go));
        }

        /// <summary>Spring a finished building: a quick squash and stretch, sparkles all round.</summary>
        public static void Pop(GameObject go, Color? colour = null)
        {
            var t = go.GetComponent<MagicTween>() ?? go.AddComponent<MagicTween>();
            t.Begin(MagicTween.Kind.Pop, 0.55f, false);
            IslandAudio.Play("built", go.transform.position, 0.9f, 0f, 0.3f);
            Burst(go.transform.position + Vector3.up * 1f, colour ?? new Color(1f, 0.85f, 0.4f), 60, Size(go));
        }

        /// <summary>Shrink away in a puff; then destroy it, or just hide it (a cleared tree can come back).</summary>
        public static void Vanish(GameObject go, bool destroy = true, Color? colour = null)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            var t = go.GetComponent<MagicTween>() ?? go.AddComponent<MagicTween>();
            t.Begin(MagicTween.Kind.Vanish, 0.4f, destroy);
            IslandAudio.Play("whoosh", go.transform.position, 0.45f, 0.15f, 0.25f, 0.9f);
            Burst(go.transform.position + Vector3.up * 0.6f, colour ?? new Color(0.75f, 0.85f, 1f), 30, Size(go));
        }

        private static float Size(GameObject go)
        {
            var found = false;
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!found) b = r.bounds; else b.Encapsulate(r.bounds);
                found = true;
            }
            return found ? Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.5f, 0.4f, 8f) : 1f;
        }

        /// <summary>A one-off spray of soft sparkles at a point, spread over <paramref name="radius"/> metres.</summary>
        public static void Burst(Vector3 at, Color colour, int count, float radius = 1f)
        {
            var go = new GameObject("Sparkles");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.3f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
            main.startColor = new ParticleSystem.MinMaxGradient(colour, Color.white);
            main.gravityModifier = -0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = radius;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var shrink = ps.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.1f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = SparkMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        /// <summary>
        /// A blow: a tree rocks away from <paramref name="from"/> (a quick lean that swings back and
        /// settles, <paramref name="degrees"/> at most), a rock just jolts. It always comes back to rest.
        /// </summary>
        public static void Shake(GameObject go, Vector3 from, float degrees)
        {
            var s = go.GetComponent<MagicShake>() ?? go.AddComponent<MagicShake>();
            s.Hit(from, degrees);
        }

        private static Material? _smokeMaterial;

        /// <summary>A soft round puff, alpha-blended (smoke, dust).</summary>
        public static Material SmokeMaterial()
        {
            if (_smokeMaterial != null) return _smokeMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            _smokeMaterial = new Material(shader);
            var tex = SparkMaterial().mainTexture;
            _smokeMaterial.mainTexture = tex;
            if (_smokeMaterial.HasProperty("_BaseMap")) _smokeMaterial.SetTexture("_BaseMap", tex);
            if (_smokeMaterial.HasProperty("_Surface")) _smokeMaterial.SetFloat("_Surface", 1f);
            _smokeMaterial.SetOverrideTag("RenderType", "Transparent");
            _smokeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _smokeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _smokeMaterial.SetInt("_ZWrite", 0);
            _smokeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _smokeMaterial.renderQueue = 3050;
            return _smokeMaterial;
        }

        private static Material SparkMaterial()
        {
            if (_sparkMaterial != null) return _sparkMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            _sparkMaterial = new Material(shader);
            // a soft round dot
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var d = Vector2.Distance(new Vector2(x, y), new Vector2((n - 1) / 2f, (n - 1) / 2f)) / (n / 2f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            _sparkMaterial.mainTexture = tex;
            if (_sparkMaterial.HasProperty("_BaseMap")) _sparkMaterial.SetTexture("_BaseMap", tex);
            // additive-ish glow where the shader allows it
            if (_sparkMaterial.HasProperty("_Surface")) _sparkMaterial.SetFloat("_Surface", 1f);
            if (_sparkMaterial.HasProperty("_Blend")) _sparkMaterial.SetFloat("_Blend", 2f);
            _sparkMaterial.SetOverrideTag("RenderType", "Transparent");
            _sparkMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _sparkMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _sparkMaterial.SetInt("_ZWrite", 0);
            _sparkMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _sparkMaterial.renderQueue = 3100;
            return _sparkMaterial;
        }
    }

    /// <summary>Rocks or jolts its object for a moment after a blow, then puts it back exactly as it was.</summary>
    public sealed class MagicShake : MonoBehaviour
    {
        private Quaternion _rot;
        private Vector3 _pos;
        private Vector3 _axis;
        private float _degrees;
        private float _t = -1f;

        public void Hit(Vector3 from, float degrees)
        {
            if (_t < 0f) { _rot = transform.localRotation; _pos = transform.localPosition; }
            var dir = new Vector3(from.x, 0, from.z);
            _axis = dir.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, dir.normalized) : Vector3.right;
            _degrees = degrees;
            _t = 0f;
        }

        private void Update()
        {
            if (_t < 0f) return;
            _t += Time.deltaTime;
            const float length = 0.55f;
            if (_t >= length) { Restore(); return; }
            var fade = 1f - _t / length;
            if (_degrees > 0f)
            {
                // lean away, swing back past upright, settle
                var a = Mathf.Sin(_t * 26f) * _degrees * fade * fade;
                transform.localRotation = Quaternion.AngleAxis(a, transform.parent != null ? transform.parent.InverseTransformDirection(_axis) : _axis) * _rot;
            }
            else
            {
                var j = Random.insideUnitSphere * 0.07f * fade;
                transform.localPosition = _pos + new Vector3(j.x, Mathf.Abs(j.y) * 0.5f, j.z);
            }
        }

        private void Restore()
        {
            if (_t < 0f) return;
            transform.localRotation = _rot;
            transform.localPosition = _pos;
            _t = -1f;
        }

        private void OnDisable() => Restore();
    }

    /// <summary>Scales its object in, out, or through a pop, from the scale it had when it started.</summary>
    public sealed class MagicTween : MonoBehaviour
    {
        public enum Kind { Appear, Pop, Vanish }

        private Kind _kind;
        private float _t;
        private float _seconds;
        private bool _destroy;
        private Vector3 _scale;
        private bool _running;

        public void Begin(Kind kind, float seconds, bool destroy)
        {
            if (!_running) _scale = transform.localScale;
            _kind = kind;
            _seconds = Mathf.Max(0.05f, seconds);
            _destroy = destroy;
            _t = 0f;
            _running = true;
            Apply(0f);
        }

        private void Update()
        {
            if (!_running) return;
            _t += Time.deltaTime / _seconds;
            if (_t >= 1f)
            {
                _running = false;
                if (_kind == Kind.Vanish)
                {
                    transform.localScale = _scale;   // so a hidden tree comes back full size
                    if (_destroy) Destroy(gameObject); else gameObject.SetActive(false);
                }
                else transform.localScale = _scale;
                Destroy(this);
                return;
            }
            Apply(_t);
        }

        private void Apply(float t)
        {
            switch (_kind)
            {
                case Kind.Appear:
                {
                    // ease out with a small overshoot
                    var k = 1f + 2.2f * Mathf.Pow(t - 1f, 3) + 1.2f * Mathf.Pow(t - 1f, 2);
                    transform.localScale = _scale * Mathf.Max(0.01f, k);
                    break;
                }
                case Kind.Pop:
                {
                    var s = Mathf.Sin(t * Mathf.PI);
                    transform.localScale = new Vector3(_scale.x * (1f + 0.12f * s), _scale.y * (1f + 0.22f * s * (1f - t)), _scale.z * (1f + 0.12f * s));
                    break;
                }
                case Kind.Vanish:
                    transform.localScale = _scale * Mathf.Max(0.01f, 1f - t * t);
                    break;
            }
        }
    }
}
