#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Results;

namespace Farmverse.Application.Features
{
    /// <summary>
    /// Rewarded ads end to end: show one (with this player's id and a fresh key for AdMob's server-side
    /// verification), then ask the server for the placement's prize (<c>ads-claim</c>). Live backends pay
    /// once AdMob's callback for that key has arrived, which can take a few seconds after the ad closes,
    /// so the claim is retried briefly. Gems and boosts land on the server; coins are the game's to add.
    /// </summary>
    public sealed class AdsService
    {
        private static readonly TimeSpan[] VerifyWaits =
            { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5) };

        private readonly IRewardedAds _ads;
        private readonly ServerCalls _server;
        private string? _uid;

        private readonly Func<bool> _noAds;

        /// <param name="noAds">True when the player bought No Ads: rewards pay without showing an ad (the server agrees).</param>
        public AdsService(IRewardedAds ads, ServerCalls server, Func<bool>? noAds = null)
        {
            _ads = ads;
            _server = server;
            _noAds = noAds ?? (() => false);
        }

        /// <summary>Signed in: ads can pay.</summary>
        public bool Online => _uid != null;

        /// <summary>An ad is loaded and ready.</summary>
        public bool Ready => _ads.Ready;

        /// <summary>The player must be able to change their ad consent (Settings ▸ Privacy).</summary>
        public bool PrivacyOptionsRequired => _ads.PrivacyOptionsRequired;

        public void ShowPrivacyOptions() => _ads.ShowPrivacyOptions();

        public void Attach(string uid)
        {
            _uid = uid;
            _ads.Preload();
        }

        /// <summary>
        /// Watch an ad for a placement. Ok = the prize was paid (Fresh) or had been already; a failure says why
        /// (<see cref="ErrorCode.Cancelled"/> when the ad was skipped, <see cref="ErrorCode.Unsupported"/> when none could be shown).
        /// </summary>
        public Task<Result<AdsClaimResponse>> Watch(string placement, CancellationToken cancellation = default) =>
            WatchThen(key => _server.AdsClaim(placement, key, cancellation), cancellation);

        /// <summary>
        /// Watch an ad, then make the server call it pays for under the ad's key (e.g. <c>wheel-spin</c>: one spin
        /// per ad). The server accepts the key once AdMob's verification for it has arrived, so the call is retried
        /// briefly while it says <see cref="ErrorCode.AdNotVerified"/>; the same key never pays twice.
        /// </summary>
        public async Task<Result<T>> WatchThen<T>(Func<string, Task<Result<T>>> pay, CancellationToken cancellation = default)
        {
            if (_uid == null) return Result<T>.Fail(ErrorCode.NetworkRequired);
            var key = Guid.NewGuid().ToString("N");
            if (!_noAds())
            {
                var outcome = await _ads.Show(_uid, key, cancellation);
                _ads.Preload();
                if (outcome == AdOutcome.Skipped) return Result<T>.Fail(ErrorCode.Cancelled);
                if (outcome == AdOutcome.Unavailable) return Result<T>.Fail(ErrorCode.Unsupported);
            }
            var result = await pay(key);
            foreach (var wait in VerifyWaits)
            {
                if (!result.IsFailure || (result.Error.Code != ErrorCode.AdNotVerified && result.Error.Code != ErrorCode.Timeout && result.Error.Code != ErrorCode.NetworkRequired)) break;
                await Task.Delay(wait, cancellation);
                result = await pay(key);
            }
            return result;
        }
    }
}
