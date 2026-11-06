#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Results;

namespace Farmverse.Application.Features
{
    /// <summary>
    /// What the player holds on chain and what the studio sells, for the game's NFT tab and shop (docs 15
    /// §3.1, §5.3). Read from the market API (the indexer); refreshed when a screen asks and after every
    /// chain action. Never used to decide anything that moves value: the server checks the chain itself.
    /// </summary>
    public sealed class NftService
    {
        /// <summary>How long a read stays fresh before a screen's <see cref="RefreshIfStale"/> asks again.</summary>
        public const long StaleMs = 30_000;

        private readonly IMarketReader _reader;
        private readonly Func<string?> _account;
        private readonly Func<long> _nowMs;
        private long _ownedAt, _dropsAt;
        private bool _loadingOwned, _loadingDrops;

        public NftService(IMarketReader reader, Func<string?> account, Func<long> nowMs)
        {
            _reader = reader;
            _account = account;
            _nowMs = nowMs;
        }

        public event Action? Changed;

        public IReadOnlyList<OwnedNft> Owned { get; private set; } = Array.Empty<OwnedNft>();
        public IReadOnlyList<MarketDrop> Drops { get; private set; } = Array.Empty<MarketDrop>();
        /// <summary>The last read failed (offline, server down): screens say so instead of "nothing here".</summary>
        public bool OwnedFailed { get; private set; }
        public bool OwnedLoaded => _ownedAt > 0;

        /// <summary>yocto-FARM and yoctoNEAR the wallet holds (null before the first read).</summary>
        public string? FarmYocto { get; private set; }
        public string? NearYocto { get; private set; }
        /// <summary>The FARM contract (set by the game from the server's config).</summary>
        public Func<string> FarmContract { get; set; } = () => "";
        private long _balancesAt;
        private bool _loadingBalances;

        public async Task RefreshBalances()
        {
            var account = _account();
            if (_loadingBalances || string.IsNullOrEmpty(account)) return;
            _loadingBalances = true;
            try
            {
                var r = await _reader.Balances(account!, FarmContract(), CancellationToken.None);
                if (r.IsOk)
                {
                    FarmYocto = r.Value.farm;
                    NearYocto = r.Value.near;
                }
                _balancesAt = _nowMs();
            }
            finally
            {
                _loadingBalances = false;
                Changed?.Invoke();
            }
        }

        /// <summary>yoctoNEAR as NEAR for display ("0.05").</summary>
        public static string Near(string yocto)
        {
            if (string.IsNullOrEmpty(yocto) || yocto == "0") return "0";
            var s = yocto.TrimStart('0').PadLeft(25, '0');
            var whole = s.Substring(0, s.Length - 24).TrimStart('0');
            var frac = s.Substring(s.Length - 24, 3).TrimEnd('0');
            return (whole.Length == 0 ? "0" : whole) + (frac.Length > 0 ? "." + frac : "");
        }

        /// <summary>The signed-in wallet, or null.</summary>
        public string? Account => _account();

        public void RefreshIfStale()
        {
            var now = _nowMs();
            if (now - _ownedAt > StaleMs) _ = RefreshOwned();
            if (now - _balancesAt > StaleMs) _ = RefreshBalances();
            if (now - _dropsAt > StaleMs) _ = RefreshDrops();
        }

        /// <summary>Reads again now (after a mint, redeem or purchase).</summary>
        public async Task RefreshOwned()
        {
            var account = _account();
            if (_loadingOwned || string.IsNullOrEmpty(account)) return;
            _loadingOwned = true;
            try
            {
                var r = await _reader.Owned(account!, CancellationToken.None);
                OwnedFailed = !r.IsOk;
                if (r.IsOk) Owned = r.Value;
                _ownedAt = _nowMs();
            }
            finally
            {
                _loadingOwned = false;
                Changed?.Invoke();
            }
        }

        public async Task RefreshDrops()
        {
            if (_loadingDrops) return;
            _loadingDrops = true;
            try
            {
                var r = await _reader.Drops(CancellationToken.None);
                if (r.IsOk) Drops = r.Value;
                _dropsAt = _nowMs();
            }
            finally
            {
                _loadingDrops = false;
                Changed?.Invoke();
            }
        }

        public Task<Result<IReadOnlyList<ItemOffer>>> OffersForItem(string itemId) => _reader.OffersForItem(itemId, CancellationToken.None);

        /// <summary>The drop with this id, if the studio sells it.</summary>
        public MarketDrop? Drop(string id)
        {
            foreach (var d in Drops) if (d.Id == id) return d;
            return null;
        }

        /// <summary>A token the player holds of this pack/item (e.g. a Farm Pass to use), or null.</summary>
        public OwnedNft? Holding(string kind, string itemId)
        {
            foreach (var t in Owned) if (t.Kind == kind && t.ItemId == itemId && t.Price == null) return t;
            return null;
        }

        /// <summary>yocto-FARM as whole FARM for display ("12.5").</summary>
        public static string Farm(string yocto)
        {
            if (string.IsNullOrEmpty(yocto) || yocto == "0") return "0";
            var s = yocto.TrimStart('0').PadLeft(19, '0');
            var whole = s.Substring(0, s.Length - 18).TrimStart('0');
            var frac = s.Substring(s.Length - 18, 2).TrimEnd('0');
            return (whole.Length == 0 ? "0" : whole) + (frac.Length > 0 ? "." + frac : "");
        }
    }
}
