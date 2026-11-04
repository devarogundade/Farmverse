#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

namespace Farmverse.Infrastructure.Unity
{
    /// <summary>
    /// AdMob rewarded ads (Google Mobile Ads for Unity). One ad is kept loaded; showing it passes the player's
    /// id and the game's key to AdMob's server-side verification. In the Unity Editor the plugin shows its
    /// placeholder ad and fires the same events, so the whole flow can be played without a device. Use Google's
    /// test ad unit ids until the app is live (EnvironmentConfig).
    /// </summary>
    public sealed class AdMobRewardedAds : IRewardedAds
    {
        private readonly string _unitId;
        private bool _initialized, _initializing, _loading;
        private RewardedAd? _ad;

        public AdMobRewardedAds(string unitId)
        {
            _unitId = unitId;
        }

        public bool Ready => _ad != null && _ad.CanShowAd();

        /// <summary>The player must be able to change their ad consent from the game (the EEA/UK).</summary>
        public bool PrivacyOptionsRequired =>
            ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required;

        public void ShowPrivacyOptions() =>
            ConsentForm.ShowPrivacyOptionsForm(error => { if (error != null) Debug.LogWarning("[Ads] privacy options: " + error.Message); });

        public void Preload()
        {
            if (string.IsNullOrEmpty(_unitId)) return;
            if (!_initialized)
            {
                if (_initializing) return;
                _initializing = true;
                MobileAds.RaiseAdEventsOnUnityMainThread = true;
#if UNITY_EDITOR
                // the Editor's consent placeholder never answers: no consent step there
                MobileAds.Initialize(_ =>
                {
                    _initialized = true;
                    _initializing = false;
                    Preload();
                });
                return;
#endif
#pragma warning disable CS0162
                // consent first (Google's UMP: the GDPR form in the EEA/UK, and on iOS the tracking prompt
                // when the AdMob console has the IDFA message on); ads are requested only when allowed
                ConsentInformation.Update(new ConsentRequestParameters(), updateError =>
                {
                    if (updateError != null) Debug.LogWarning("[Ads] consent info: " + updateError.Message);
                    ConsentForm.LoadAndShowConsentFormIfRequired(formError =>
                    {
                        if (formError != null) Debug.LogWarning("[Ads] consent form: " + formError.Message);
                        if (!ConsentInformation.CanRequestAds())
                        {
                            _initializing = false;
                            return;
                        }
                        MobileAds.Initialize(_ =>
                        {
                            _initialized = true;
                            _initializing = false;
                            Preload();
                        });
                    });
                });
                return;
#pragma warning restore CS0162
            }
            if (_loading || Ready) return;
            _loading = true;
            RewardedAd.Load(_unitId, new AdRequest(), (ad, error) =>
            {
                _loading = false;
                if (error != null || ad == null)
                {
                    Debug.LogWarning("[Ads] rewarded ad not loaded: " + (error?.GetMessage() ?? "no ad"));
                    return;
                }
                _ad?.Destroy();
                _ad = ad;
            });
        }

        public async Task<AdOutcome> Show(string userId, string customData, CancellationToken cancellation = default)
        {
            if (!Ready)
            {
                Preload();
                // give a slow first load a few seconds
                for (var i = 0; i < 10 && !Ready && !cancellation.IsCancellationRequested; i++) await Task.Delay(500, cancellation);
                if (!Ready) return AdOutcome.Unavailable;
            }

            var ad = _ad!;
            _ad = null;
            EnsureEditorEventSystem();
            var done = new TaskCompletionSource<AdOutcome>();
            var earned = false;
            ad.SetServerSideVerificationOptions(new ServerSideVerificationOptions { UserId = userId, CustomData = customData });
            ad.OnAdFullScreenContentClosed += () => done.TrySetResult(earned ? AdOutcome.Earned : AdOutcome.Skipped);
            ad.OnAdFullScreenContentFailed += error =>
            {
                Debug.LogWarning("[Ads] rewarded ad failed to show: " + error.GetMessage());
                done.TrySetResult(AdOutcome.Unavailable);
            };
            ad.Show(_ => earned = true);
            using (cancellation.Register(() => done.TrySetResult(AdOutcome.Skipped)))
            {
                var outcome = await done.Task;
                ad.Destroy();
                return outcome;
            }
        }

        /// <summary>
        /// The Editor's placeholder ads are uGUI canvases: their buttons need an EventSystem, which the game
        /// (UI Toolkit only) doesn't have. One is added for them, with the Input System's UI module.
        /// </summary>
        private static void EnsureEditorEventSystem()
        {
#if UNITY_EDITOR
            if (UnityEngine.EventSystems.EventSystem.current != null) return;
            var go = new GameObject("EventSystem (ads)");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            Object.DontDestroyOnLoad(go);
#endif
        }
    }
}
