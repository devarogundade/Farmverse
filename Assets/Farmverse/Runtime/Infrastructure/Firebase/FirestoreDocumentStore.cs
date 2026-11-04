#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Lists;
using Farmverse.Core.Logging;
using Farmverse.Core.Results;
using Firebase.Firestore;
using AppQuerySnapshot = Farmverse.Application.Ports.QuerySnapshot;
using AppDocumentSnapshot = Farmverse.Application.Ports.DocumentSnapshot;
using FsQuery = Firebase.Firestore.Query;

namespace Farmverse.Infrastructure.Firebase
{
    /// <summary>
    /// <see cref="IDocumentStore"/> on the Firestore Unity SDK. Listener callbacks are
    /// marshalled to the main thread; snapshot metadata (from cache, pending writes)
    /// travels with every snapshot so the UI can show freshness (11 §1.3).
    /// </summary>
    public sealed class FirestoreDocumentStore : IDocumentStore
    {
        private readonly FirebaseFirestore _db;
        private readonly IMainThread _main;
        private readonly ILog _log;

        public FirestoreDocumentStore(FirebaseFirestore db, IMainThread main, ILog log)
        {
            _db = db;
            _main = main;
            _log = log.For("Firestore");
        }

        public IDisposable Listen(string path, Action<AppDocumentSnapshot> onNext, Action<AppError> onError)
        {
            var registration = _db.Document(path).Listen(MetadataChanges.Include, snapshot => _main.Post(() => onNext(Convert(path, snapshot))));
            Watch(registration, path, onError);
            return new Registration(registration);
        }

        public async Task<Result<AppDocumentSnapshot>> Get(string path, Freshness freshness, CancellationToken cancellation)
        {
            try
            {
                var snapshot = await _db.Document(path).GetSnapshotAsync(SourceOf(freshness));
                return Result<AppDocumentSnapshot>.Ok(Convert(path, snapshot));
            }
            catch (Exception error)
            {
                return Result<AppDocumentSnapshot>.Fail(Map(error));
            }
        }

        public async Task<Result<AppQuerySnapshot>> Query(QuerySpec query, Freshness freshness, CancellationToken cancellation)
        {
            try
            {
                var snapshot = await Build(query).GetSnapshotAsync(SourceOf(freshness));
                return Result<AppQuerySnapshot>.Ok(Convert(snapshot));
            }
            catch (Exception error)
            {
                return Result<AppQuerySnapshot>.Fail(Map(error));
            }
        }

        public IDisposable ListenQuery(QuerySpec query, Action<AppQuerySnapshot> onNext, Action<AppError> onError)
        {
            var registration = Build(query).Listen(MetadataChanges.Include, snapshot => _main.Post(() => onNext(Convert(snapshot))));
            Watch(registration, query.Collection, onError);
            return new Registration(registration);
        }

        public async Task<Result> Update(string path, IReadOnlyDictionary<string, object> fields)
        {
            try
            {
                await _db.Document(path).UpdateAsync(fields.ToDictionary(p => p.Key, p => p.Value));
                return Result.Ok();
            }
            catch (Exception error)
            {
                return Result.Fail(Map(error));
            }
        }

        /// <summary>The spec as a Firestore query; document id ordering is appended so keyset cursors are total.</summary>
        private FsQuery Build(QuerySpec spec)
        {
            FsQuery query = _db.Collection(spec.Collection);
            foreach (var filter in spec.Filters)
            {
                switch (filter.Op)
                {
                    case FilterOp.Equal: query = query.WhereEqualTo(filter.Field, filter.Value); break;
                    case FilterOp.Less: query = query.WhereLessThan(filter.Field, filter.Value); break;
                    case FilterOp.LessOrEqual: query = query.WhereLessThanOrEqualTo(filter.Field, filter.Value); break;
                    case FilterOp.Greater: query = query.WhereGreaterThan(filter.Field, filter.Value); break;
                    case FilterOp.GreaterOrEqual: query = query.WhereGreaterThanOrEqualTo(filter.Field, filter.Value); break;
                    case FilterOp.In: query = query.WhereIn(filter.Field, ((IEnumerable<object>)filter.Value).ToList()); break;
                }
            }
            var descending = false;
            foreach (var order in spec.Orders)
            {
                query = order.Descending ? query.OrderByDescending(order.Field) : query.OrderBy(order.Field);
                descending = order.Descending;
            }
            query = descending ? query.OrderByDescending(FieldPath.DocumentId) : query.OrderBy(FieldPath.DocumentId);
            if (spec.StartAfter != null) query = query.StartAfter(spec.StartAfter.ToArray());
            return query.Limit(spec.Limit);
        }

        private void Watch(ListenerRegistration registration, string what, Action<AppError> onError)
        {
            registration.ListenerTask.ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    var error = Map(task.Exception.GetBaseException());
                    _log.Warn("listener on " + what + " stopped", null, task.Exception);
                    _main.Post(() => onError(error));
                }
            }, TaskScheduler.Default);
        }

        private static Source SourceOf(Freshness freshness)
        {
            switch (freshness)
            {
                case Freshness.Server: return Source.Server;
                case Freshness.Cache: return Source.Cache;
                default: return Source.Default;
            }
        }

        private static AppDocumentSnapshot Convert(string path, global::Firebase.Firestore.DocumentSnapshot snapshot) =>
            new AppDocumentSnapshot(
                path,
                snapshot.Exists,
                snapshot.Exists ? Values.ToWireMap(snapshot.ToDictionary(ServerTimestampBehavior.Estimate)) : null,
                snapshot.Metadata.IsFromCache,
                snapshot.Metadata.HasPendingWrites);

        private static AppQuerySnapshot Convert(global::Firebase.Firestore.QuerySnapshot snapshot) =>
            new AppQuerySnapshot(snapshot.Documents.Select(d => Convert(d.Reference.Path, d)).ToList(), snapshot.Metadata.IsFromCache);

        private static AppError Map(Exception error)
        {
            if (error is FirestoreException firestore)
            {
                switch (firestore.ErrorCode)
                {
                    case FirestoreError.Unavailable:
                    case FirestoreError.DeadlineExceeded:
                        return AppError.FromException(ErrorCode.NetworkRequired, error);
                    case FirestoreError.PermissionDenied: return AppError.FromException(ErrorCode.PermissionDenied, error);
                    case FirestoreError.Unauthenticated: return AppError.FromException(ErrorCode.Unauthenticated, error);
                    case FirestoreError.NotFound: return AppError.FromException(ErrorCode.NotFound, error);
                }
            }
            return AppError.FromException(ErrorCode.Internal, error);
        }

        private sealed class Registration : IDisposable
        {
            private ListenerRegistration? _registration;
            public Registration(ListenerRegistration registration) => _registration = registration;
            public void Dispose()
            {
                _registration?.Stop();
                _registration = null;
            }
        }
    }
}
