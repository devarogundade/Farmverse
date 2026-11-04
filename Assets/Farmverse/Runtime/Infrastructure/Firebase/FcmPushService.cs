#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Results;
using Firebase.Messaging;

namespace Farmverse.Infrastructure.Firebase
{
    /// <summary>
    /// FCM: permission is asked at a moment of value (after the first harvest,
    /// 11 §4), the token goes to <c>push-registerToken</c>, and a tapped
    /// notification opens its route through <see cref="Opened"/>.
    /// Messaging is touched only from <see cref="Start"/>, after the platform step,
    /// so it never creates the Firebase app ahead of App Check.
    /// </summary>
    public sealed class FcmPushService : IPushService, IDisposable
    {
        private readonly IMainThread _main;
        private bool _started;

        public FcmPushService(IMainThread main)
        {
            _main = main;
        }

        public event Action<string, IReadOnlyDictionary<string, string>>? Opened;

        /// <summary>The platform step succeeded: listen for taps (a tap that launched the app is delivered now).</summary>
        public void Start()
        {
            if (_started) return;
            _started = true;
            FirebaseMessaging.RegistrationOnInitEnabled = false;
            FirebaseMessaging.MessageReceived += OnMessage;
        }

        public async Task<Result<string>> RequestPermissionAndToken(CancellationToken cancellation)
        {
            if (!_started) return Result<string>.Fail(AppError.Of(ErrorCode.Internal, ("reason", "platform not ready")));
            try
            {
                await FirebaseMessaging.RequestPermissionAsync();
                FirebaseMessaging.RegistrationOnInitEnabled = true;
                // SDK 14 marks token APIs obsolete in favour of installation-based registration; the
                // backend still sends with FCM registration tokens (HTTP v1), so the token is what we need.
#pragma warning disable CS0618
                var token = await FirebaseMessaging.GetTokenAsync();
#pragma warning restore CS0618
                return string.IsNullOrEmpty(token) ? Result<string>.Fail(AppError.Of(ErrorCode.PermissionDenied)) : Result<string>.Ok(token);
            }
            catch (Exception error)
            {
                return Result<string>.Fail(AppError.FromException(ErrorCode.PermissionDenied, error));
            }
        }

        public void Dispose()
        {
            if (_started) FirebaseMessaging.MessageReceived -= OnMessage;
        }

        /// <summary>Only a tap opens a route; a push arriving while playing just updates the inbox (its listener sees the row).</summary>
        private void OnMessage(object sender, MessageReceivedEventArgs args)
        {
            if (!args.Message.NotificationOpened) return;
            var data = new Dictionary<string, string>();
            if (args.Message.Data != null) foreach (var pair in args.Message.Data) data[pair.Key] = pair.Value;
            if (!data.TryGetValue("route", out var route)) return;
            _main.Post(() => Opened?.Invoke(route, data));
        }
    }
}
