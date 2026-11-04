#nullable enable
using System;
using Farmverse.Application.Features;
using Farmverse.Contracts;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// Gems and prices as the screens see them. The balance is the server's (<see cref="WalletService"/>,
    /// set by GameRoot); prices come from the catalogue (<see cref="CatalogueService"/>), the same one the
    /// server charges by. Coins live in the island's economy (<c>GameUi.Economy</c>).
    /// Without GameRoot (the Island scene opened on its own) there are no gems and the built-in prices.
    /// </summary>
    public static class Wallet
    {
        private static WalletService? _service;
        private static CatalogueDoc? _builtin;

        public static event Action? Changed;

        public static WalletService? Service
        {
            get => _service;
            set
            {
                if (_service != null) _service.Changed -= Raise;
                _service = value;
                if (_service != null) _service.Changed += Raise;
                Raise();
            }
        }

        public static CatalogueService? Catalogue { get; set; }

        private static ShopService? _shop;

        /// <summary>Raised when boosts, the gift timer or the chests change (the shop screens rebuild).</summary>
        public static event Action? ShopChanged;

        /// <summary>Boosts, the free gift, the starter pack window and the daily chests (server-owned).</summary>
        public static ShopService? Shop
        {
            get => _shop;
            set
            {
                if (_shop != null) _shop.Changed -= RaiseShop;
                _shop = value;
                if (_shop != null) _shop.Changed += RaiseShop;
                RaiseShop();
            }
        }

        private static void RaiseShop() => ShopChanged?.Invoke();

        /// <summary>The catalogue in use (the live one, else the one built in).</summary>
        public static CatalogueDoc Prices => Catalogue?.Current ?? (_builtin ??= CatalogueService.Builtin());

        public static int Gems => _service?.Gems ?? 0;

        /// <summary>True when gems can be spent right now (signed in).</summary>
        public static bool CanSpend => _service is { Online: true };

        /// <summary>The gem price of a catalogue offer (0 when it isn't sold).</summary>
        public static int Price(string offerId) => Prices.GemOffers.TryGetValue(offerId, out var o) ? o.Gems : 0;

        public static GemOffer? Offer(string offerId) => Prices.GemOffers.TryGetValue(offerId, out var o) ? o : null;

        /// <summary>Gems earned in the game; the server confirms them (capped per source per day).</summary>
        public static void Earn(GemSource source, int n)
        {
            if (n > 0) _service?.Earn(source, n);
        }

        private static void Raise() => Changed?.Invoke();
    }
}
