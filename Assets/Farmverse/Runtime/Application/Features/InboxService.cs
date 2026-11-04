#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Documents;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;

namespace Farmverse.Application.Features
{
    /// <summary>
    /// The inbox: the player's notifications (<c>players/{uid}/notifications</c>, newest 50, live). A message
    /// with <c>routeArgs.rewardId</c> carries a reward collected through <see cref="RewardsService"/>; read
    /// marks are a direct write (rules allow only readAt); deleting goes through <c>inbox-delete</c>.
    /// </summary>
    public sealed class InboxService : IDisposable
    {
        public const int PageSize = 50;

        private readonly IDocumentStore _documents;
        private readonly ServerCalls _server;
        private readonly Func<long> _nowMs;
        private readonly ReactiveProperty<int> _version = new ReactiveProperty<int>(0);
        private IDisposable? _watch;
        private string? _uid;

        public InboxService(IDocumentStore documents, ServerCalls server, Func<long> nowMs)
        {
            _documents = documents;
            _server = server;
            _nowMs = nowMs;
        }

        public IReadOnlyReactiveProperty<int> Version => _version;

        public bool Loaded { get; private set; }

        /// <summary>Newest first.</summary>
        public IReadOnlyList<(string id, NotificationDoc doc)> Messages { get; private set; } = Array.Empty<(string, NotificationDoc)>();

        public void Attach(string uid)
        {
            _uid = uid;
            _watch?.Dispose();
            var query = new QuerySpec(Paths.Notifications(uid), Array.Empty<QueryFilter>(), new[] { new QueryOrder("createdAt", true) }, PageSize);
            _watch = _documents.ListenQuery(query, OnMessages, _ => { });
        }

        public Task<Result> MarkRead(string id)
        {
            if (_uid == null) return Task.FromResult(Result.Fail(ErrorCode.Unauthenticated));
            return _documents.Update(Paths.Notification(_uid, id), new Dictionary<string, object> { ["readAt"] = _nowMs() });
        }

        public Task<Result<InboxDeleteResponse>> Delete(IReadOnlyList<string> ids, CancellationToken cancellation = default) =>
            _server.InboxDelete(ids, cancellation);

        public void Dispose() => _watch?.Dispose();

        private void OnMessages(QuerySnapshot snapshot)
        {
            var rows = new List<(string, NotificationDoc)>();
            foreach (var d in snapshot.Documents)
            {
                if (!d.Exists || d.Data == null) continue;
                try { rows.Add((d.Id, NotificationDoc.FromWire(new Dictionary<string, object>(d.Data)))); }
                catch (WireFormatException) { }
            }
            Messages = rows;
            Loaded = true;
            _version.Value = _version.Value + 1;
        }
    }
}
