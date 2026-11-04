#nullable enable
using System;
using System.Linq;
using Farmverse.Domain.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// The Winter Festival on the island (1 December to 6 January, UTC; plan: Seasons and the December event):
    /// the Christmas pack's trees, snowmen and presents come out (built hidden by the island builder), the sky
    /// turns to the pack's winter skybox, and Santa waves, greets and dances by the big tree. His daily gift is
    /// claimed from the HUD (<see cref="GiftReady"/>, <see cref="ClaimGift"/>). Snow and the white ground come
    /// with winter (IslandWeatherFx), which the festival always is.
    /// The dressing lives in its own scenes (plan: Unity scenes), loaded on top of the island while it's on:
    /// Event_WinterFestival in December and Event_ZombieNight (pumpkins, dead trees, fog) all October.
    /// Without those scenes in the build it falls back to <see cref="decor"/>.
    /// </summary>
    public sealed class IslandFestival : MonoBehaviour
    {
        [SerializeField] private GameObject? decor;
        [SerializeField] private Animator? santa;
        [SerializeField] private Material? festivalSky;

        public const string WinterScene = "Event_WinterFestival", HalloweenScene = "Event_ZombieNight";

        /// <summary>Show the festival whatever the date (Editor preview).</summary>
        public static bool Preview { get; set; }
        /// <summary>Show October's Zombie Night dressing whatever the date (Editor preview).</summary>
        public static bool PreviewHalloween { get; set; }
        /// <summary>True while the festival is shown.</summary>
        public static bool On { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRun() { Preview = false; PreviewHalloween = false; On = false; }

        private const string GiftKey = "fv_santa_gift_day";
        private static readonly string[] Acts = { "Wave", "Greet", "Cheer", "Dance1", "Dance5", "Dance9", "Dance14", "Dance22", "Idle" };

        private Material? _normalSky;
        private float _next, _nextAct;
        private bool _shown, _started, _spooky;

        private void Start()
        {
            _normalSky = RenderSettings.skybox;
            Show(false);
            _started = true;
        }

        private void Update()
        {
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + 2f;
                var now = IslandEconomyHost.Clock;
                var on = Preview || GameplayRules.WinterFestival(now);
                if (on != _shown || !_started) Show(on);
                var spooky = PreviewHalloween || GameplayRules.UtcOf(now).Month == 10;
                if (spooky != _spooky) { _spooky = spooky; Dress(HalloweenScene, spooky); }
            }
            if (_shown && santa != null && santa.isActiveAndEnabled && Time.unscaledTime >= _nextAct)
            {
                _nextAct = Time.unscaledTime + UnityEngine.Random.Range(5f, 9f);
                var act = Acts[UnityEngine.Random.Range(0, Acts.Length)];
                if (santa.HasState(0, Animator.StringToHash(act))) santa.CrossFadeInFixedTime(act, 0.3f);
            }
        }

        private void Show(bool on)
        {
            _shown = on;
            On = on;
            if (!Dress(WinterScene, on) && decor != null) decor.SetActive(on);
            if (festivalSky != null && _normalSky != null)
            {
                RenderSettings.skybox = on ? festivalSky : _normalSky;
                DynamicGI.UpdateEnvironment();
            }
        }

        /// <summary>Load or unload an event's scene on top of the island; false when that scene isn't in the build.</summary>
        private bool Dress(string sceneName, bool on)
        {
            if (!UnityEngine.Application.CanStreamedLevelBeLoaded(sceneName)) return false;
            var scene = SceneManager.GetSceneByName(sceneName);
            if (on && !scene.isLoaded)
            {
                var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                if (op != null) op.completed += _ => Loaded(SceneManager.GetSceneByName(sceneName));
            }
            else if (!on && scene.isLoaded)
            {
                if (sceneName == WinterScene) santa = null;
                SceneManager.UnloadSceneAsync(scene);
            }
            return true;
        }

        private void Loaded(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            var roots = scene.GetRootGameObjects();
            // show only what the island's chapter has (a chapter-3 tree stays away while the island is small)
            IslandChapters.Instance?.Adopt(roots.SelectMany(r => r.GetComponentsInChildren<IslandChapterTag>(true)));
            if (scene.name == WinterScene)
                santa = roots.SelectMany(r => r.GetComponentsInChildren<Animator>(true)).FirstOrDefault(a => a.name == "Santa" || a.transform.parent?.name == "Santa");
        }

        private void OnDisable()
        {
            if (_normalSky != null) RenderSettings.skybox = _normalSky;
            On = false;
        }

        private static string Today => DateTime.UtcNow.ToString("yyyyMMdd");

        /// <summary>Santa has a gift for today.</summary>
        public static bool GiftReady => On && PlayerPrefs.GetString(GiftKey, "") != Today;

        /// <summary>The coins Santa gives today (more each day of the festival).</summary>
        public static int GiftCoins
        {
            get
            {
                var d = DateTime.UtcNow;
                var start = new DateTime(d.Month == 1 ? d.Year - 1 : d.Year, 12, 1, 0, 0, 0, DateTimeKind.Utc);
                return GameplayRules.SantaGift(Math.Max(0, (int)(d - start).TotalDays));
            }
        }

        /// <summary>Take today's gift: the coins to add (0 if already taken).</summary>
        public static int ClaimGift()
        {
            if (!GiftReady) return 0;
            PlayerPrefs.SetString(GiftKey, Today);
            PlayerPrefs.Save();
            return GiftCoins;
        }
    }
}
