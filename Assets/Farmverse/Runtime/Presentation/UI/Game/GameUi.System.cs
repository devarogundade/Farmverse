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

        /// <summary>The admin's maintenance message (remote config), shown on the maintenance popup; null for the usual text.</summary>
        public static string? MaintenanceMessage { get; set; }

        /// <summary>The inbox's read marks (set by GameRoot).</summary>
        public static Farmverse.Application.Features.NotificationService? Notifications { get; set; }

        /// <summary>Start the session again (set by GameRoot).</summary>
        public static Action? RetrySession { get; set; }

        private string? _noticeShown;

        /// <summary>A short message from outside the screens (gems paid for levels…), shown once over any screen.</summary>
        public static string? PendingSay { get; set; }

        private void RouteTick()
        {
            if (PendingSay is { } say && BaseScreen != "loading")
            {
                PendingSay = null;
                Say(say, 3f);
            }
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
