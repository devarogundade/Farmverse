#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Documents;
using Farmverse.Application.Ports;
using Farmverse.Application.Remote;
using Farmverse.Contracts;
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;
using Farmverse.Core.Time;

namespace Farmverse.Application.Features
{
    /// <summary>
    /// The inbox badge and read marks (11 §1.4): unread notifications are watched
    /// with one capped query (readAt == 0), and marking one read is a direct
    /// Firestore write the rules allow — instant, and queued offline by the SDK.
    /// Push permission is asked at a moment of value (<see cref="PushPrompt"/>),
    /// then the token is registered against this install; once registered, every
    /// session start registers again so a rotated token is never stale.
    /// </summary>
    public sealed class NotificationService : IDisposable
    {
        private readonly IDocumentStore _documents;
        private readonly CallableClient _client;
        private readonly IPushService _push;
        private readonly IClock _clock;
        private readonly Func<RequestMeta> _meta;
        private readonly IInstallationId _installation;
        private readonly ILocalStore _local;
        private readonly ReactiveProperty<int> _unread = new ReactiveProperty<int>(0);
        private IDisposable? _watch;
        private string? _uid;

        public const string RegisteredKey = "push.registered";

        public NotificationService(IDocumentStore documents, CallableClient client, IPushService push, IClock clock, Func<RequestMeta> meta,
            IInstallationId installation, ILocalStore local)
        {
            _installation = installation;
            _local = local;
            _documents = documents;
            _client = client;
            _push = push;
            _clock = clock;
            _meta = meta;
        }

        /// <summary>Unread count, capped at 50 (shown as "50+").</summary>
        public IReadOnlyReactiveProperty<int> Unread => _unread;

        public void Attach(string uid)
        {
            _uid = uid;
            _watch?.Dispose();
            var query = new QuerySpec(Paths.Notifications(uid), new[] { new QueryFilter("readAt", FilterOp.Equal, 0L) }, new[] { new QueryOrder("createdAt", true) }, 50);
            _watch = _documents.ListenQuery(query, snapshot => _unread.Value = snapshot.Documents.Count, _ => { });
        }

        public Task<Result> MarkRead(string notificationId)
        {
            if (_uid == null) return Task.FromResult(Result.Fail(ErrorCode.Unauthenticated));
            return _documents.Update(Paths.Notification(_uid, notificationId), new Dictionary<string, object> { ["readAt"] = _clock.NowMs });
        }

        /// <summary>True once this install has a registered push token (the player said yes).</summary>
        public bool IsRegistered => _local.Read(RegisteredKey) == "1";

        /// <summary>Ask the OS for push permission and register the token; asking again after a yes is silent.</summary>
        public async Task<Result> EnablePush(CancellationToken cancellation = default)
        {
            var token = await _push.RequestPermissionAndToken(cancellation);
            if (token.IsFailure) return Result.Fail(token.Error);
            var installationId = await _installation.Get(cancellation);
            var result = await _client.Call(Callables.PushRegisterToken, new PushRegisterTokenRequest { Meta = _meta(), InstallationId = installationId, Token = token.Value }, cancellation);
            if (result.IsOk) _local.Write(RegisteredKey, "1");
            return result.AsResult();
        }

        /// <summary>
        /// The Settings switch: on asks the OS for permission, registers this phone and opts in on the server;
        /// off opts out (the server sends nothing while <c>pushEnabled</c> is false).
        /// </summary>
        public async Task<Result> SetPushEnabled(bool on, CancellationToken cancellation = default)
        {
            if (_uid == null) return Result.Fail(ErrorCode.Unauthenticated);
            if (on)
            {
                var enabled = await EnablePush(cancellation);
                if (enabled.IsFailure) return enabled;
            }
            return await _documents.Update(Paths.Settings(_uid), new Dictionary<string, object> { ["pushEnabled"] = on });
        }

        /// <summary>The topic switches (Settings → Notifications): raids, island, rewards, offers → on.</summary>
        public Task<Result> SetPushTopics(IReadOnlyDictionary<string, bool> topics)
        {
            if (_uid == null) return Task.FromResult(Result.Fail(ErrorCode.Unauthenticated));
            var map = new Dictionary<string, object>();
            foreach (var kv in topics) map[kv.Key] = kv.Value;
            return _documents.Update(Paths.Settings(_uid), new Dictionary<string, object> { ["pushTopics"] = map });
        }

        /// <summary>The language server copy (push, mail) is written in.</summary>
        public Task<Result> SetLocale(string locale) =>
            _uid == null ? Task.FromResult(Result.Fail(ErrorCode.Unauthenticated))
                : _documents.Update(Paths.Settings(_uid), new Dictionary<string, object> { ["locale"] = locale });

        /// <summary>Re-register at session start when the player already agreed (tokens rotate).</summary>
        public Task<Result> RefreshRegistration(CancellationToken cancellation = default) =>
            IsRegistered ? EnablePush(cancellation) : Task.FromResult(Result.Ok());

        public void Dispose() => _watch?.Dispose();
    }
}
