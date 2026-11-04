#nullable enable
using System;
using Farmverse.Contracts;
using Farmverse.Presentation.World.Island;

namespace Farmverse.Presentation.UI.Game
{
    // Rewarded ads for every screen: watch → the server pays the placement's prize (ads-claim; AdsService).
    public sealed partial class GameUi
    {
        private bool _adBusy;

        /// <summary>True while an ad is showing or its prize is being collected.</summary>
        public bool AdBusy => _adBusy;

        /// <summary>
        /// Watch a rewarded ad for a placement (catalogue <c>ads</c>). When it pays, its coins go to the island
        /// (gems and boosts are already on the server) and <paramref name="paid"/> runs; otherwise
        /// <paramref name="failed"/> runs after the player is told why.
        /// </summary>
        private async void WatchAd(string placement, Action<AdsClaimResponse>? paid = null, Action? failed = null)
        {
            var ads = Ads;
            if (_adBusy) return;
            if (ads == null || !ads.Online)
            {
                Say("Connect to the internet to watch ads");
                failed?.Invoke();
                return;
            }
            _adBusy = true;
            MarkDirty();
            var result = await ads.Watch(placement);
            _adBusy = false;
            if (result.IsFailure)
            {
                Say(result.Error.Code switch
                {
                    ErrorCode.Cancelled => "Watch the ad to the end to get the reward",
                    ErrorCode.Unsupported => "No ad right now — try again in a moment",
                    ErrorCode.AdLimit => "That's all the ads for today — come back tomorrow",
                    ErrorCode.AdNotVerified => "Your reward is on its way — try again in a minute",
                    _ => "Couldn't collect the reward — check your connection",
                });
                MarkDirty();
                failed?.Invoke();
                return;
            }
            var got = result.Value;
            if (got.Fresh && got.Coins > 0) Economy?.AddCoins((int)got.Coins);
            IslandAudio.Play("coin");
            paid?.Invoke(got);
            MarkDirty();
        }

        /// <summary>The catalogue's prize for a placement ("25 coins").</summary>
        private static string AdPrizeText(string placement)
        {
            if (!Wallet.Prices.Ads.TryGetValue(placement, out var a)) return "";
            var b = a.Prize;
            if (b.Coins > 0) return PK.Num(b.Coins) + " coins";
            if (b.Gems > 0) return PK.Num(b.Gems) + " gems";
            foreach (var kv in b.Boosts) return kv.Value + " " + (System.Linq.Enumerable.FirstOrDefault(ShopBoosts, x => x.id == kv.Key).name ?? kv.Key);
            return "";
        }
    }
}
