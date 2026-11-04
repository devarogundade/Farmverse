#nullable enable
using System.Collections.Generic;
using Farmverse.Domain.Gameplay;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// What the season and the weather look and sound like on the island (cosmetic only: the rules are the
    /// economy's). Rain and storms bring rain streaks, a darker sky and, in a storm, lightning and thunder;
    /// winter snows (harder when the weather turns wet); autumn drops leaves and spring blossom petals; a
    /// drought warms the light. Every season tints the sunlight a little.
    ///
    /// Cheap on purpose: one small particle system per effect (at most ~450 particles on screen, unlit,
    /// no collisions, no lights, no noise), all riding a volume just in front of the camera that scales with
    /// the zoom so it looks the same near and far. Textures and sounds are made in code (no assets). Low-memory
    /// phones get half the particles; <see cref="Amount"/> can turn it all down.
    /// </summary>
    public sealed class IslandWeatherFx : MonoBehaviour
    {
        /// <summary>0..1: how much weather to show (1 = full; low-memory phones start at 0.5; 0 = none).</summary>
        public static float Amount { get; set; } = SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 3000 ? 0.5f : 1f;

        /// <summary>
        /// Show one effect whatever the season and weather (the Editor's Farmverse ▸ Weather Preview menu):
        /// "rain", "storm", "snow", "blizzard", "leaves", "petals", "drought"; null follows the island.
        /// </summary>
        public static string? Preview { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRun()
        {
            Amount = SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 3000 ? 0.5f : 1f;
            Preview = null;
        }

        private const float FadeSeconds = 3f;

        private IslandEconomyHost? _host;
        private IslandCamera? _rig;
        private Transform? _volume;
        private Fx? _rain, _snow, _leaves, _petals;
        private readonly List<Fx> _all = new List<Fx>();

        private Light? _sun;
        private Color _sunColour = Color.white;
        private float _sunIntensity = 1f;
        private Color _ambient;
        private Color _fogColour;
        private float _fogDensity;
        private Camera? _cam;
        private Color _tint = Color.white;
        private float _dim = 1f;
        private bool _storm;
        private float _nextFlash, _flash;

        private AudioSource? _rainSound;
        private AudioClip? _thunder;
        private float _rainVolume;

        private float _nextLook;

        /// <summary>One effect: its particle system, the emission rate at full strength, and where it is fading to.</summary>
        private sealed class Fx
        {
            public ParticleSystem System = null!;
            public float FullRate;
            public float Target, Now;
        }

        public void Begin(IslandEconomyHost host)
        {
            _host = host;
            if (_volume != null) return;
            _rig = FindAnyObjectByType<IslandCamera>();
            _volume = new GameObject("Weather").transform;
            _volume.SetParent(transform, false);

            var dot = Dot();
            var leaf = Leaf();
            _rain = Add(Rain(dot), 380f);
            _snow = Add(Snow(dot), 42f);
            _leaves = Add(Falling("Leaves", leaf, new Color(1f, 0.55f, 0.15f), new Color(0.85f, 0.25f, 0.1f)), 7f);
            _petals = Add(Falling("Petals", leaf, new Color(1f, 0.78f, 0.88f), new Color(1f, 0.93f, 0.96f)), 6f);

            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && (_sun == null || l.intensity > _sun.intensity)) _sun = l;
            if (_sun != null)
            {
                _sunColour = _sun.color;
                _sunIntensity = _sun.intensity;
            }
            _ambient = RenderSettings.ambientLight;
            _fogColour = RenderSettings.fogColor;
            _fogDensity = RenderSettings.fogDensity;
            _cam = Camera.main;

            _rainSound = gameObject.AddComponent<AudioSource>();
            _rainSound.clip = RainLoop();
            _rainSound.loop = true;
            _rainSound.playOnAwake = false;
            _rainSound.spatialBlend = 0f;
            _rainSound.volume = 0f;
            _thunder = Thunder();
        }

        private Fx Add(ParticleSystem ps, float rate)
        {
            ps.transform.SetParent(_volume, false);
            var fx = new Fx { System = ps, FullRate = rate };
            _all.Add(fx);
            return fx;
        }

        private void OnDestroy()
        {
            // the scene's light goes back to how it was
            if (_sun != null)
            {
                _sun.color = _sunColour;
                _sun.intensity = _sunIntensity;
            }
            RenderSettings.ambientLight = _ambient;
        }

        private void Update()
        {
            if (_host == null || _volume == null || _host.Economy == null) return;
            var now = IslandEconomyHost.Clock;
            if (Time.unscaledTime >= _nextLook)
            {
                _nextLook = Time.unscaledTime + 1f;
                Look(now);
            }

            Follow();
            var step = Time.unscaledDeltaTime / FadeSeconds;
            foreach (var fx in _all)
            {
                fx.Now = Mathf.MoveTowards(fx.Now, fx.Target * Amount, step);
                var em = fx.System.emission;
                em.rateOverTime = fx.FullRate * fx.Now;
                var on = fx.Now > 0.001f || fx.System.particleCount > 0;
                if (on && !fx.System.isPlaying) fx.System.Play();
                else if (!on && fx.System.isPlaying) fx.System.Stop();
            }
            Lighting(step);
            Sound(step);
            // the season's look on the ground and the trees (IslandGround and TreeSway read these)
            _winter = Mathf.MoveTowards(_winter, _winterGoal, step * 0.5f);
            _autumn = Mathf.MoveTowards(_autumn, _autumnGoal, step * 0.5f);
            Shader.SetGlobalFloat(WinterId, _winter);
            Shader.SetGlobalFloat(AutumnId, _autumn);
        }

        private static readonly int WinterId = Shader.PropertyToID("_FvWinter"), AutumnId = Shader.PropertyToID("_FvAutumn");
        private float _winter, _autumn, _winterGoal, _autumnGoal;

        private void OnDisable()
        {
            Shader.SetGlobalFloat(WinterId, 0f);
            Shader.SetGlobalFloat(AutumnId, 0f);
        }

        /// <summary>Pick the effects for the season and the weather now (checked once a second).</summary>
        private void Look(double now)
        {
            var eco = _host!.Economy;
            foreach (var fx in _all) fx.Target = 0;
            _storm = false;
            _dim = 1f;
            if (!eco.SeasonsOn && Preview == null)
            {
                _winterGoal = _autumnGoal = 0f;
                _tint = Color.white;
                return;
            }
            var season = eco.SeasonNow(now).Name;
            var weather = GameplayRules.WeatherAt(now);
            switch (Preview)
            {
                case "rain": season = "Spring"; weather = GameplayRules.WeatherKind.Rain; break;
                case "storm": season = "Autumn"; weather = GameplayRules.WeatherKind.Storm; break;
                case "snow": season = "Winter"; weather = GameplayRules.WeatherKind.Clear; break;
                case "blizzard": season = "Winter"; weather = GameplayRules.WeatherKind.Storm; break;
                case "leaves": season = "Autumn"; weather = GameplayRules.WeatherKind.Clear; break;
                case "petals": season = "Spring"; weather = GameplayRules.WeatherKind.Clear; break;
                case "drought": season = "Summer"; weather = GameplayRules.WeatherKind.Drought; break;
            }
            var wet = weather == GameplayRules.WeatherKind.Rain || weather == GameplayRules.WeatherKind.Storm;
            var heavy = weather == GameplayRules.WeatherKind.Storm ? 1.6f : 1f;
            if (season == "Winter")
            {
                // winter rain falls as snow; a dry winter day still has a light flurry
                _snow!.Target = wet ? Mathf.Min(1f, 0.7f * heavy) : 0.35f;
            }
            else if (wet)
            {
                _rain!.Target = Mathf.Min(1f, 0.65f * heavy);
            }
            else if (season == "Autumn") _leaves!.Target = 1f;
            else if (season == "Spring") _petals!.Target = 1f;

            if (wet) _dim = weather == GameplayRules.WeatherKind.Storm ? 0.55f : 0.75f;
            _storm = weather == GameplayRules.WeatherKind.Storm && season != "Winter";
            _winterGoal = season == "Winter" ? 1f : 0f;
            _autumnGoal = season == "Autumn" ? 1f : 0f;
            _tint = season switch
            {
                "Spring" => new Color(1f, 0.98f, 0.97f),
                "Autumn" => new Color(1.06f, 0.93f, 0.82f),
                "Winter" => new Color(0.88f, 0.94f, 1.06f),
                _ => Color.white,
            };
            if (weather == GameplayRules.WeatherKind.Drought) _tint *= new Color(1.08f, 1.0f, 0.86f);
        }

        /// <summary>The volume sits between the camera and what it looks at, and grows with the zoom.</summary>
        private void Follow()
        {
            var cam = Camera.main;
            if (cam == null || _volume == null) return;
            var pivot = _rig != null ? _rig.Pivot : cam.transform.position + cam.transform.forward * 40f;
            var distance = _rig != null ? _rig.Distance : 40f;
            var scale = Mathf.Clamp(distance / 40f, 0.45f, 7f);
            _volume.position = Vector3.Lerp(pivot, cam.transform.position, 0.35f);
            _volume.rotation = Quaternion.identity;
            _volume.localScale = new Vector3(scale, scale, scale);
        }

        private void Lighting(float step)
        {
            if (_sun == null) return;
            // a storm's lightning: a quick double flash, now and then
            if (_storm && Amount > 0f)
            {
                if (_nextFlash <= 0f) _nextFlash = Time.unscaledTime + Random.Range(5f, 12f);
                if (Time.unscaledTime >= _nextFlash)
                {
                    _nextFlash = Time.unscaledTime + Random.Range(7f, 16f);
                    _flash = 1f;
                    if (_thunder != null) AudioSource.PlayClipAtPoint(_thunder, Camera.main != null ? Camera.main.transform.position : Vector3.zero, 0.6f * IslandAudio.SoundVolume);
                }
            }
            else _nextFlash = 0f;
            _flash = Mathf.MoveTowards(_flash, 0f, Time.unscaledDeltaTime * 5f);
            var flicker = _flash > 0f ? _flash * (Mathf.Sin(Time.unscaledTime * 60f) > 0 ? 1.4f : 0.6f) : 0f;

            // a zombie night: the sun goes down to a cold glimmer and fog rolls in over the island
            var night = IslandLife.Night;
            var nightTint = Color.Lerp(Color.white, new Color(0.55f, 0.62f, 0.9f), night);
            var targetColour = _sunColour * _tint * nightTint;
            var targetIntensity = _sunIntensity * _dim * Mathf.Lerp(1f, 0.38f, night) + flicker * _sunIntensity;
            _sun.color = Color.Lerp(_sun.color, targetColour, step * 3f);
            _sun.intensity = _flash > 0f ? targetIntensity : Mathf.Lerp(_sun.intensity, targetIntensity, step * 3f);
            RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, _ambient * _tint * nightTint * Mathf.Lerp(1f, _dim, 0.6f) * Mathf.Lerp(1f, 0.5f, night), step * 3f);
            var fogColour = Color.Lerp(_fogColour, new Color(0.42f, 0.45f, 0.56f), night);
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, fogColour, step * 3f);
            RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, _fogDensity * Mathf.Lerp(1f, 2.4f, night), step * 3f);
            if (_cam != null) _cam.backgroundColor = RenderSettings.fogColor;
        }

        private void Sound(float step)
        {
            if (_rainSound == null) return;
            _rainVolume = Mathf.MoveTowards(_rainVolume, (_rain?.Now ?? 0f) * 0.32f, step);
            _rainSound.volume = _rainVolume * IslandAudio.SoundVolume;
            if (_rainVolume > 0.001f && !_rainSound.isPlaying) _rainSound.Play();
            else if (_rainVolume <= 0.001f && _rainSound.isPlaying) _rainSound.Stop();
        }

        // ---- the particle systems (sizes are for a camera 40 m away; the volume scales with the zoom) -----

        private static ParticleSystem Base(string name, Texture2D texture, int max, float lifetime)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.startLifetime = lifetime;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;   // rides with the camera
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;       // the whole effect scales with the zoom
            main.startSpeed = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(44f, 1f, 44f);
            shape.position = new Vector3(0f, 16f, 0f);
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = Material(texture);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = -10f;
            return ps;
        }

        private static ParticleSystem Rain(Texture2D dot)
        {
            var ps = Base("Rain", dot, 420, 1.1f);
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.12f);
            main.startColor = new Color(0.82f, 0.9f, 1f, 0.7f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            vel.y = new ParticleSystem.MinMaxCurve(-30f, -26f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.045f;
            r.lengthScale = 1f;
            return ps;
        }

        private static ParticleSystem Snow(Texture2D dot)
        {
            var ps = Base("Snow", dot, 460, 11f);
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
            main.startColor = new Color(1f, 1f, 1f, 0.9f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.7f, 0.9f);
            vel.y = new ParticleSystem.MinMaxCurve(-3.2f, -2.2f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            Fade(ps);
            return ps;
        }

        private static ParticleSystem Falling(string name, Texture2D leaf, Color a, Color b)
        {
            var ps = Base(name, leaf, 70, 9f);
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            vel.y = new ParticleSystem.MinMaxCurve(-2.4f, -1.4f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
            Fade(ps);
            return ps;
        }

        /// <summary>Fade in and out over a particle's life (nothing pops into view).</summary>
        private static void Fade(ParticleSystem ps)
        {
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            colour.color = g;
        }

        private static Material Material(Texture2D texture)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(shader) { name = "Weather (" + texture.name + ")" };
            m.mainTexture = texture;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            // transparent, alpha-blended, no depth write
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        // ---- textures and sounds made here (no assets to ship) ----------------------------------------

        /// <summary>A soft round dot (snowflakes, raindrops).</summary>
        private static Texture2D Dot()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "dot", wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var dx = (x + 0.5f) / n * 2f - 1f;
                var dy = (y + 0.5f) / n * 2f - 1f;
                var a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
            }
            t.Apply(false, true);
            return t;
        }

        /// <summary>A pointed oval with a lighter middle vein (a leaf or a petal, tinted by the particle).</summary>
        private static Texture2D Leaf()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "leaf", wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var u = (x + 0.5f) / n * 2f - 1f;
                var v = (y + 0.5f) / n * 2f - 1f;
                var half = 0.55f * (1f - v * v);   // narrows to points at both ends
                var inside = Mathf.Clamp01((half - Mathf.Abs(u)) * 10f);
                var vein = Mathf.Abs(u) < 0.06f && Mathf.Abs(v) < 0.8f ? 1.25f : 1f;
                t.SetPixel(x, y, new Color(vein, vein, vein, inside));
            }
            t.Apply(false, true);
            return t;
        }

        /// <summary>Two seconds of soft, darkened noise that loops without a seam (steady rain).</summary>
        private static AudioClip RainLoop()
        {
            const int rate = 22050;
            var n = rate * 2;
            var data = new float[n];
            var random = new System.Random(7);
            float low = 0f, lower = 0f;
            for (var i = 0; i < n; i++)
            {
                var white = (float)(random.NextDouble() * 2.0 - 1.0);
                low += 0.35f * (white - low);
                lower += 0.06f * (white - lower);
                data[i] = (low - lower) * 0.9f + (random.NextDouble() < 0.0015 ? white * 0.6f : 0f);   // hiss plus the odd drop
            }
            // cross-fade the ends so the loop has no click
            var fade = rate / 10;
            for (var i = 0; i < fade; i++)
            {
                var w = i / (float)fade;
                data[i] = data[i] * w + data[n - fade + i] * (1f - w);
            }
            var clip = AudioClip.Create("rain_loop", n - fade, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A low rumble that swells and dies away (thunder).</summary>
        private static AudioClip Thunder()
        {
            const int rate = 22050;
            var n = (int)(rate * 3.2f);
            var data = new float[n];
            var random = new System.Random(11);
            float low = 0f;
            for (var i = 0; i < n; i++)
            {
                var white = (float)(random.NextDouble() * 2.0 - 1.0);
                low += 0.02f * (white - low);
                var tt = i / (float)rate;
                var envelope = Mathf.Clamp01(tt / 0.08f) * Mathf.Exp(-tt * 1.3f) * (1f + 0.5f * Mathf.Sin(tt * 9f));
                data[i] = low * 9f * envelope;
            }
            var clip = AudioClip.Create("thunder", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
