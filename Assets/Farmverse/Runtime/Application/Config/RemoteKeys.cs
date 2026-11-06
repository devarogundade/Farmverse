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

        public const string MinClientVersion = "min_client_version";
        public const string MinClientVersionDefault = "0.0.0";

        /// <summary>Rollout phase 1: mirror the island to the server engine on every open and compare the runs.</summary>
        public const string IslandShadow = "island_shadow";
        public const bool IslandShadowDefault = true;
    }
}
