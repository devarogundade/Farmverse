#nullable enable
namespace Farmverse.Application.Config
{
    /// <summary>
    /// Remote Config keys and their compiled-in defaults (11 §3). The template in
    /// backend/remoteconfig.template.json must carry the same keys.
    /// </summary>
    public static class RemoteKeys
    {
        public const string Maintenance = "maintenance";
        public const bool MaintenanceDefault = false;

        /// <summary>What the admin says on the maintenance screen (empty: the usual text).</summary>
        public const string MaintenanceMessage = "maintenance_message";

        /// <summary>The items NFT contract; absent when the server has no chain (mint and redeem are then hidden).</summary>
        public const string ItemsContract = "items_contract";

        /// <summary>Hours a new farm waits before it may mint (the admin web sets it).</summary>
        public const string MintMinHours = "mint_min_hours";
        /// <summary>The FARM token and market contracts (drops, offers; docs 15).</summary>
        public const string FarmContract = "farm_contract";
        public const string MarketContract = "market_contract";
        /// <summary>The testnet faucet is open (docs 15 §6.6).</summary>
        public const string Faucet = "faucet";
        public const long MintMinHoursDefault = 24;

        public const string MinClientVersion = "min_client_version";
        public const string MinClientVersionDefault = "0.0.0";

        /// <summary>Rollout phase 1: mirror the island to the server engine on every open and compare the runs.</summary>
        public const string IslandShadow = "island_shadow";
        public const bool IslandShadowDefault = true;
    }
}
