#nullable enable
using System.Collections.Generic;

namespace Farmverse.Application.Ports
{
    /// <summary>A reminder the phone shows by itself at a set time (no server): crops ready, a gift ready…</summary>
    public sealed class LocalNudge
    {
        public string Title = "";
        public string Body = "";
        /// <summary>The screen a tap opens (GameUi route).</summary>
        public string Route = "";
        /// <summary>The push topic it belongs to (island, raids, rewards, offers): the player's switch for it applies.</summary>
        public string Topic = "island";
        /// <summary>When (epoch ms).</summary>
        public long AtMs;
    }

    /// <summary>The phone's own scheduled notifications.</summary>
    public interface ILocalNotifications
    {
        /// <summary>Ask the OS (Android 13+, iOS) to allow notifications; true when allowed.</summary>
        System.Threading.Tasks.Task<bool> RequestPermission();

        /// <summary>Forget everything booked and book these instead.</summary>
        void Replace(IReadOnlyList<LocalNudge> nudges);

        void CancelAll();

        /// <summary>The route of the reminder the player tapped to open the game, once (null when none).</summary>
        string? TakeOpenedRoute();
    }
}
