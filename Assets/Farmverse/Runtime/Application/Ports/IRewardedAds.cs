#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace Farmverse.Application.Ports
{
    /// <summary>How a rewarded ad ended.</summary>
    public enum AdOutcome
    {
        /// <summary>Watched to the end: the reward was earned.</summary>
        Earned,
        /// <summary>Closed before the end (no reward).</summary>
        Skipped,
        /// <summary>No ad could be shown (none loaded, no network, not supported here).</summary>
        Unavailable,
    }

    /// <summary>
    /// Rewarded video ads behind a port (AdMob in the game; a fake in tests). <c>userId</c> and
    /// <c>customData</c> travel to the ad network's server-side verification callback, so the backend can
    /// tell which player watched which ad.
    /// </summary>
    public interface IRewardedAds
    {
        /// <summary>True when an ad is loaded and can be shown right away.</summary>
        bool Ready { get; }

        /// <summary>Start loading the next ad (no-op while one is loaded or loading).</summary>
        void Preload();

        Task<AdOutcome> Show(string userId, string customData, CancellationToken cancellation = default);

        /// <summary>The player must be offered a way to change their ad consent (Settings ▸ Privacy).</summary>
        bool PrivacyOptionsRequired { get; }

        /// <summary>Show the ad consent choices again.</summary>
        void ShowPrivacyOptions();
    }
}
