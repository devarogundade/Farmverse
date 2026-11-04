#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// The island's sound (clips in Content/Audio, wired by Farmverse / World / Build Island).
    /// One-shots: <see cref="Play"/> by clip name, optionally at a world point. A positioned sound is
    /// as loud as it is close to what the camera is looking at (the middle of the screen on the
    /// ground) and panned left/right with the screen, so a camera high above the island still hears
    /// the farm it's showing. Loops (<see cref="Loop"/>): footsteps, the horse, a building site, the
    /// train, the sea — kept and faded in and out by whoever owns them. A clip played again within a
    /// short gap is skipped, so fast repeats (a harvest step every 0.16 s) don't pile up.
    /// Sound on/off is saved on the device (<see cref="Muted"/>).
    /// </summary>
    public sealed class IslandAudio : MonoBehaviour
    {
        [SerializeField] private AudioClip[] clips = new AudioClip[0];
        [SerializeField, Range(0f, 1f)] private float volume = 0.85f;
        [SerializeField, Tooltip("How far from the middle of the view (m) a sound can still be heard.")] private float hearing = 55f;
        [SerializeField, Tooltip("The background music clip (Content/Audio/Music), looped under everything.")] private string musicClip = "music_sweet_country";
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.35f;

        private static IslandAudio? _instance;
        private readonly Dictionary<string, AudioClip> _byName = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private readonly List<LoopSound> _loops = new List<LoopSound>();
        private Vector3 _focus;
        private LoopSound? _music;

        /// <summary>How loud the music plays (0–1 of its normal level): turned down during a raid so the horn and fighting come through.</summary>
        public static float MusicLevel { get; set; } = 1f;

        /// <summary>The music on or off (the player's setting, remembered on the device); sounds are separate.</summary>
        public static bool MusicOn
        {
            get => PlayerPrefs.GetInt("fv_music_off", 0) == 0;
            set { PlayerPrefs.SetInt("fv_music_off", value ? 0 : 1); PlayerPrefs.Save(); }
        }

        /// <summary>A looping sound someone owns: set <see cref="Target"/> (0 = silent) and where it is.</summary>
        public sealed class LoopSound
        {
            internal AudioSource Source = null!;
            internal float Level;
            public float Target;
            public Transform? Follow;
            public Vector3 At;
            public bool Positional;
            public float Pitch = 1f;

            public void Stop()
            {
                Target = 0f;
                Level = 0f;
                if (Source != null) Source.volume = 0f;
            }
        }

        /// <summary>The sound effects level, 0–1 (the settings slider; remembered on the device).</summary>
        public static float SoundVolume
        {
            get => PlayerPrefs.GetFloat("fv_sound_vol", 1f);
            set { PlayerPrefs.SetFloat("fv_sound_vol", Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        /// <summary>The music level, 0–1 (the settings slider; remembered on the device). 0 is off.</summary>
        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat("fv_music_vol", 1f);
            set { PlayerPrefs.SetFloat("fv_music_vol", Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        public static bool Muted
        {
            get => PlayerPrefs.GetInt("fv_sound_off", 0) == 1;
            set
            {
                PlayerPrefs.SetInt("fv_sound_off", value ? 1 : 0);
                PlayerPrefs.Save();
                AudioListener.volume = value ? 0f : 1f;
            }
        }

        private void Awake()
        {
            _instance = this;
            foreach (var c in clips) if (c != null) _byName[c.name] = c;
            for (var i = 0; i < 14; i++) _pool.Add(NewSource("One-shot " + i));
            AudioListener.volume = Muted ? 0f : 1f;
            // the island scene has no listener of its own (sounds are placed by hand, so one anywhere will do)
            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            // the music starts from the top and fades in
            _music = Loop(musicClip, false, fromStart: true);
            if (_music != null) _music.Target = musicVolume;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private AudioSource NewSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;   // positioned by hand (see the class notes)
            return s;
        }

        /// <summary>Play a clip once (2D, or at a world point). Returns false if it isn't loaded, is too far, or played a moment ago.</summary>
        public static bool Play(string name, Vector3? at = null, float vol = 1f, float pitchJitter = 0.06f, float gap = 0.06f, float pitch = 1f)
        {
            var a = _instance;
            if (a == null || !a._byName.TryGetValue(name, out var clip)) return false;
            if (a._lastPlayed.TryGetValue(name, out var last) && Time.unscaledTime - last < gap) return false;
            var (level, pan) = at is Vector3 p ? a.Place(p) : (1f, 0f);
            if (level <= 0.01f) return false;
            a._lastPlayed[name] = Time.unscaledTime;
            AudioSource? free = null;
            foreach (var s in a._pool) if (!s.isPlaying) { free = s; break; }
            if (free == null) return false;
            free.clip = clip;
            free.volume = Mathf.Clamp01(vol * level * a.volume * SoundVolume);
            free.panStereo = pan;
            free.pitch = pitch * (1f + Random.Range(-pitchJitter, pitchJitter));
            free.loop = false;
            free.Play();
            return true;
        }

        /// <summary>A looping sound to fade in and out (footsteps, a building site, the train). Null if the clip isn't loaded.</summary>
        public static LoopSound? Loop(string name, bool positional, bool fromStart = false)
        {
            var a = _instance;
            if (a == null || !a._byName.TryGetValue(name, out var clip)) return null;
            var s = a.NewSource("Loop " + name);
            s.clip = clip;
            s.loop = true;
            s.volume = 0f;
            if (!fromStart) s.time = Random.Range(0f, clip.length * 0.9f);   // two loops of the same clip don't play in step
            s.Play();
            var loop = new LoopSound { Source = s, Positional = positional };
            a._loops.Add(loop);
            return loop;
        }

        public static void Release(LoopSound? loop)
        {
            if (loop == null || _instance == null) return;
            _instance._loops.Remove(loop);
            if (loop.Source != null) Destroy(loop.Source.gameObject);
        }

        /// <summary>How loud and where (pan) a point sounds from the middle of the view.</summary>
        private (float level, float pan) Place(Vector3 at)
        {
            var d = new Vector2(at.x - _focus.x, at.z - _focus.z).magnitude;
            var level = Mathf.Clamp01(1f - d / hearing);
            level *= level;
            var cam = Camera.main;
            var pan = 0f;
            if (cam != null && d > 0.5f)
            {
                var right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
                pan = Mathf.Clamp(Vector3.Dot((at - _focus).normalized, right), -1f, 1f) * 0.6f;
            }
            return (level, pan);
        }

        private void Update()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
                if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var t)) _focus = ray.GetPoint(t);
            }
            if (_music != null) _music.Target = MusicOn ? musicVolume * MusicLevel * MusicVolume : 0f;
            foreach (var l in _loops)
            {
                if (l.Source == null) continue;
                l.Level = Mathf.MoveTowards(l.Level, l.Target, Time.unscaledDeltaTime * 2.5f);
                var level = l.Level;
                var pan = 0f;
                if (l.Positional)
                {
                    var at = l.Follow != null ? l.Follow.position : l.At;
                    var (k, p) = Place(at);
                    level *= k;
                    pan = p;
                }
                l.Source.volume = Mathf.Clamp01(level * volume * (l == _music ? 1f : SoundVolume));
                l.Source.panStereo = pan;
                l.Source.pitch = l.Pitch;
            }
        }
    }
}
