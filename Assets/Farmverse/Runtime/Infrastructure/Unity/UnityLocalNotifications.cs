#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using UnityEngine;
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
using Unity.Notifications;
#endif

namespace Farmverse.Infrastructure.Unity
{
    /// <summary>
    /// Local reminders through Unity Mobile Notifications (com.unity.mobile.notifications, unified API).
    /// One Android channel, "Island". In the Editor and on desktop it only logs what it would book.
    /// </summary>
    public sealed class UnityLocalNotifications : ILocalNotifications
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        private QueryLastRespondedNotificationOp? _opened;
        private bool _taken;

        public UnityLocalNotifications()
        {
            var args = NotificationCenterArgs.Default;
            args.AndroidChannelId = "island";
            args.AndroidChannelName = "Island";
            args.AndroidChannelDescription = "Crops, workshops and gifts on your island";
            NotificationCenter.Initialize(args);
            _opened = NotificationCenter.QueryLastRespondedNotification();
        }

        public async Task<bool> RequestPermission()
        {
            var request = NotificationCenter.RequestPermission();
            while (request.Status == NotificationsPermissionStatus.RequestPending) await Task.Yield();
            return request.Status == NotificationsPermissionStatus.Granted;
        }

        public void Replace(IReadOnlyList<LocalNudge> nudges)
        {
            CancelAll();
            foreach (var n in nudges)
            {
                var at = DateTimeOffset.FromUnixTimeMilliseconds(n.AtMs).LocalDateTime;
                if (at <= DateTime.Now) continue;
                NotificationCenter.ScheduleNotification(new Notification { Title = n.Title, Text = n.Body, Data = n.Route }, new NotificationDateTimeSchedule(at));
            }
        }

        public void CancelAll()
        {
            NotificationCenter.CancelAllScheduledNotifications();
            NotificationCenter.CancelAllDeliveredNotifications();
        }

        public string? TakeOpenedRoute()
        {
            if (_taken || _opened == null || _opened.State == QueryLastRespondedNotificationState.Pending) return null;
            _taken = true;
            return _opened.State == QueryLastRespondedNotificationState.HaveRespondedNotification && !string.IsNullOrEmpty(_opened.Notification.Data)
                ? _opened.Notification.Data
                : null;
        }
#else
        public Task<bool> RequestPermission() => Task.FromResult(true);

        public void Replace(IReadOnlyList<LocalNudge> nudges)
        {
            foreach (var n in nudges)
                Debug.Log($"[LocalNotifications] would show at {DateTimeOffset.FromUnixTimeMilliseconds(n.AtMs).LocalDateTime:HH:mm} ({n.Topic}): {n.Title} — {n.Body} → {n.Route}");
        }

        public void CancelAll() { }

        public string? TakeOpenedRoute() => null;
#endif
    }
}
