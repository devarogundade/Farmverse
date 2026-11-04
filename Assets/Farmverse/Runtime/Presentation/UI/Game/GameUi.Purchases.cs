#nullable enable
using System;
using System.Linq;
using Farmverse.Application.Features;
using Farmverse.Contracts;
using Farmverse.Core.Results;
using Farmverse.Presentation.World.Island;

namespace Farmverse.Presentation.UI.Game
{
    // Real-money purchases for every screen: the store sells, the server checks and pays (PurchaseService).
    public sealed partial class GameUi
    {
        private static PurchaseService? _purchases;

        /// <summary>Set by GameRoot; null in the Island scene opened on its own.</summary>
        public static PurchaseService? Purchases
        {
            get => _purchases;
            set
            {
                _purchases = value;
                if (value != null) value.Finished += (id, result) => Instance?.ShopIapFinished(id, result);
            }
        }

        /// <summary>The live screen (purchases finish while any screen is up).</summary>
        private static GameUi? Instance;

        private void ShopIapBuy(string productId)
        {
            var store = Purchases;
            if (store == null || !store.Online)
            {
                Say("Connect to the internet to buy");
                return;
            }
            if (!store.StoreReady)
            {
                Say("The store isn't ready yet — try again in a moment");
                return;
            }
            if (store.Busy)
            {
                Say("A purchase is already on its way");
                return;
            }
            if (store.Buy(productId)) Open("wait");
        }

        private void ShopIapRestore()
        {
            var store = Purchases;
            if (store == null) return;
            Open("wait");
            store.Restore(ok =>
            {
                if (TopPopup == "wait") Back();
                Say(ok ? "Purchases restored" : "Couldn't restore purchases — try again");
            });
        }

        private void ShopIapFinished(string productId, Result<IapVerifyResponse> result)
        {
            if (TopPopup == "wait") Back();
            if (result.IsFailure)
            {
                switch (result.Error.Code)
                {
                    case ErrorCode.Cancelled: break;
                    case ErrorCode.IapInvalid: Say("The store didn't confirm this purchase"); break;
                    default: Say("Your purchase will arrive as soon as you're back online"); break;
                }
                MarkDirty();
                return;
            }
            var got = result.Value;
            if (got.Pending)
            {
                Say("Payment pending — your items arrive once it goes through");
                return;
            }
            if (got.Fresh && got.Coins > 0) Economy?.AddCoins((int)got.Coins);
            var title = Wallet.Prices.Iap.TryGetValue(productId, out var p) ? p.Title : productId;
            IslandAudio.Play("cheer");
            if (got.Fresh && (got.Coins > 0 || got.Gems > 0 || got.Boosts.Count > 0))
                Open("reward", CpPrize.Reward(got.Coins, got.Gems, got.Boosts));
            else Say(title + " — thank you!");
            BoostSync();
            MarkDirty();
        }
    }
}
