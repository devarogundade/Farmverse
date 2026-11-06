#nullable enable
namespace Farmverse.Application.Documents
{
    /// <summary>Document paths the client reads (04 §1), mirroring backend/game/src/infra/paths.ts.</summary>
    public static class Paths
    {
        public static string Player(string uid) => "players/" + uid;
        /// <summary>The player's own settings the server reads (SettingsDoc: push opt-in, quiet hours, locale).</summary>
        public static string Settings(string uid) => "players/" + uid + "/private/settings";
        public static string Friends(string uid) => "players/" + uid + "/friends";
        public static string Rewards(string uid) => "players/" + uid + "/rewards";
        public const string News = "news";
        /// <summary>The player's gems (WalletDoc).</summary>
        public static string Wallet(string uid) => "players/" + uid + "/state/wallet";
        /// <summary>Boosts held and running (InventoryDoc).</summary>
        public static string Inventory(string uid) => "players/" + uid + "/state/inventory";
        /// <summary>The shop's timers and daily chests (ShopDoc).</summary>
        public static string Shop(string uid) => "players/" + uid + "/state/shop";
        /// <summary>Achievements collected (GoalsDoc).</summary>
        public static string Goals(string uid) => "players/" + uid + "/state/goals";
        /// <summary>The login ladder and today's wheel spins (DailyDoc).</summary>
        public static string Daily(string uid) => "players/" + uid + "/state/daily";
        /// <summary>The server's copy of the island (cloud save: state, deviceSave).</summary>
        public static string Island(string uid) => "players/" + uid + "/state/island";
        /// <summary>Lasting purchases and unlocks (PurchasesDoc).</summary>
        public static string Purchases(string uid) => "players/" + uid + "/state/purchases";
        /// <summary>This season's Farm Pass (PassDoc).</summary>
        public static string Pass(string uid) => "players/" + uid + "/state/pass";
        /// <summary>Coupon codes the server issued to the player (CouponDoc).</summary>
        public static string Coupons(string uid) => "players/" + uid + "/coupons";
        /// <summary>A week's leaderboard rows (LeaderboardEntryDoc).</summary>
        public static string Board(string weekId) => "leaderboards/" + weekId + "/players";
        public static string BoardEntry(string weekId, string uid) => Board(weekId) + "/" + uid;
        /// <summary>The live tuning data (CatalogueDoc).</summary>
        public const string Catalogue = "config/catalogue";
        public static string Notifications(string uid) => "players/" + uid + "/notifications";
        public static string Notification(string uid, string id) => Notifications(uid) + "/" + id;
    }
}
