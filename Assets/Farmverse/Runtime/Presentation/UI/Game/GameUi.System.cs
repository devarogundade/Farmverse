#nullable enable
using System;

namespace Farmverse.Presentation.UI.Game
{
    // What the session tells the player: the server is in maintenance, this version is too old, or the game
    // couldn't connect. GameRoot sets SystemNotice from the session's state; the popup opens over any screen.
    public sealed partial class GameUi
    {
        /// <summary>"maintenance", "update" or "noconnection" while the session is stopped for it; null when fine.</summary>
        public static string? SystemNotice { get; set; }

        /// <summary>Push opt-in and the inbox's read marks (set by GameRoot).</summary>
        public static Farmverse.Application.Features.NotificationService? Notifications { get; set; }

        /// <summary>Start the session again (set by GameRoot).</summary>
        public static Action? RetrySession { get; set; }

        private string? _noticeShown;

        /// <summary>The screen a tapped push notification asks for (set by GameRoot; opened once the game is past loading).</summary>
        public static string? PendingRoute { get; set; }

        /// <summary>A short message from outside the screens (gems paid for levels…), shown once over any screen.</summary>
        public static string? PendingSay { get; set; }

        private void RouteTick()
        {
            if (PendingSay is { } say && BaseScreen != "loading")
            {
                PendingSay = null;
                Say(say, 3f);
            }
            var route = PendingRoute;
            if (route == null || BaseScreen == "loading" || TopPopup != null) return;
            PendingRoute = null;
            var screen = route.Split('.')[0];
            if (screen == "ranks") screen = "leaderboard";   // "ranks.friends" and the like
            // a raid push: the island itself shows the result (it applies the server's raid and opens it)
            if (screen == "raidresult") screen = "play";
            if (screen == "play" && BaseScreen == "play") return;
            Open(Registry.ContainsKey(screen) ? screen : "inbox");
        }

        private void SystemTick()
        {
            var notice = SystemNotice;
            if (notice == _noticeShown) return;
            _noticeShown = notice;
            if (notice == null)
            {
                if (TopPopup is "maintenance" or "update" or "noconnection") Back();
                return;
            }
            if (TopPopup != notice) Open(notice, notice == "update" ? "force" : null);
        }

        private void SystemRetry()
        {
            if (TopPopup is "maintenance" or "update" or "noconnection") Back();
            _noticeShown = null;
            SystemNotice = null;
            RetrySession?.Invoke();
        }
    }
}
