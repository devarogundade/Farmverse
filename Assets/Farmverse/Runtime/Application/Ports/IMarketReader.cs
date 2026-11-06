#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Core.Results;

namespace Farmverse.Application.Ports
{
    /// <summary>An NFT the player holds, as the market's read API shows it (docs 15 §3.1).</summary>
    public sealed class OwnedNft
    {
        public string TokenId = "";
        public string Title = "";
        /// <summary>The contract's kind: good, boost, building, land, tool, pack, worker.</summary>
        public string Kind = "";
        public string ItemId = "";
        public long Quantity = 1;
        /// <summary>Listed for this many yocto-FARM, or null when not for sale.</summary>
        public string? Price;
        /// <summary>Small traits (level, edition, specialty …) for the detail panel.</summary>
        public IReadOnlyDictionary<string, object> Props = new Dictionary<string, object>();
    }

    /// <summary>A studio drop for sale (docs 15 §5): the Farm Pass, gem packs, land deeds …</summary>
    public sealed class MarketDrop
    {
        public string Id = "";
        public string Kind = "";
        public string ItemId = "";
        public string Title = "";
        /// <summary>yocto-FARM</summary>
        public string Price = "0";
        public long? Cap;
        public long Sold;
        public int? PerWallet;
        public long StartsAt;
        public long? EndsAt;
        public bool Paused;

        public long? Left => Cap.HasValue ? System.Math.Max(0, Cap.Value - Sold) : (long?)null;
    }

    /// <summary>An offer on any token of an item (a merchant's or a player's buy order, docs 15 §12.2, §13.1).</summary>
    public sealed class ItemOffer
    {
        public string Id = "";
        public string From = "";
        public string ItemId = "";
        public long MinQuantity;
        /// <summary>yocto-FARM</summary>
        public string Amount = "0";
        public long ExpiresAt;
    }

    /// <summary>The marketplace's public read API (`/v1/market`), for the game.</summary>
    public interface IMarketReader
    {
        Task<Result<IReadOnlyList<OwnedNft>>> Owned(string account, CancellationToken cancellation);
        Task<Result<IReadOnlyList<MarketDrop>>> Drops(CancellationToken cancellation);
        Task<Result<IReadOnlyList<ItemOffer>>> OffersForItem(string itemId, CancellationToken cancellation);

        /// <summary>FARM (yocto-FARM) and NEAR (yoctoNEAR) the account holds; "0" when unknown.</summary>
        Task<Result<(string farm, string near)>> Balances(string account, string farmContract, CancellationToken cancellation);
    }
}
