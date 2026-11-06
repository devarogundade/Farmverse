#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Farmverse.Application.Documents;
using Farmverse.Application.Ports;
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
    /// (No push on the web build: the in-game inbox is the only channel.)
    /// </summary>
    public sealed class NotificationService : IDisposable
    {
        private readonly IDocumentStore _documents;
        private readonly IClock _clock;
        private readonly ReactiveProperty<int> _unread = new ReactiveProperty<int>(0);
        private IDisposable? _watch;
        private string? _uid;

        public NotificationService(IDocumentStore documents, IClock clock)
        {
            _documents = documents;
            _clock = clock;
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

        /// <summary>The language server copy (push, mail) is written in.</summary>
        public Task<Result> SetLocale(string locale) =>
            _uid == null ? Task.FromResult(Result.Fail(ErrorCode.Unauthenticated))
                : _documents.Update(Paths.Settings(_uid), new Dictionary<string, object> { ["locale"] = locale });

        public void Dispose() => _watch?.Dispose();
    }
}
