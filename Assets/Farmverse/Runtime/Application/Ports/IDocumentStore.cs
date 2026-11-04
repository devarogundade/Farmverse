#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Core.Lists;
using Farmverse.Core.Results;

namespace Farmverse.Application.Ports
{
    /// <summary>One document as Firestore delivered it, data in wire form (maps, lists, long, double, string, bool).</summary>
    public sealed class DocumentSnapshot
    {
        public DocumentSnapshot(string path, bool exists, IReadOnlyDictionary<string, object>? data, bool fromCache, bool hasPendingWrites)
        {
            Path = path;
            Exists = exists;
            Data = data;
            FromCache = fromCache;
            HasPendingWrites = hasPendingWrites;
        }

        public string Path { get; }
        public string Id => Path.Substring(Path.LastIndexOf('/') + 1);
        public bool Exists { get; }
        public IReadOnlyDictionary<string, object>? Data { get; }
        public bool FromCache { get; }
        public bool HasPendingWrites { get; }
    }

    public sealed class QuerySnapshot
    {
        public QuerySnapshot(IReadOnlyList<DocumentSnapshot> documents, bool fromCache)
        {
            Documents = documents;
            FromCache = fromCache;
        }

        public IReadOnlyList<DocumentSnapshot> Documents { get; }
        public bool FromCache { get; }
    }

    public enum FilterOp
    {
        Equal = 1,
        Less = 2,
        LessOrEqual = 3,
        Greater = 4,
        GreaterOrEqual = 5,
        In = 6,
    }

    public readonly struct QueryFilter
    {
        public QueryFilter(string field, FilterOp op, object value)
        {
            Field = field;
            Op = op;
            Value = value;
        }

        public string Field { get; }
        public FilterOp Op { get; }
        public object Value { get; }
    }

    public readonly struct QueryOrder
    {
        public QueryOrder(string field, bool descending)
        {
            Field = field;
            Descending = descending;
        }

        public string Field { get; }
        public bool Descending { get; }
    }

    /// <summary>
    /// A single-collection query in index-backed shape (06 §4): filters, orders, a
    /// page size and an optional cursor (the order values of the last document seen,
    /// then its id — a stable keyset cursor).
    /// </summary>
    public sealed class QuerySpec
    {
        public QuerySpec(string collection, IReadOnlyList<QueryFilter> filters, IReadOnlyList<QueryOrder> orders, int limit, IReadOnlyList<object>? startAfter = null)
        {
            if (limit < 1 || limit > 50) throw new ArgumentOutOfRangeException(nameof(limit), "security rules cap pages at 50");
            Collection = collection;
            Filters = filters;
            Orders = orders;
            Limit = limit;
            StartAfter = startAfter;
        }

        public string Collection { get; }
        public IReadOnlyList<QueryFilter> Filters { get; }
        public IReadOnlyList<QueryOrder> Orders { get; }
        public int Limit { get; }
        public IReadOnlyList<object>? StartAfter { get; }

        public QuerySpec After(IReadOnlyList<object> cursor) => new QuerySpec(Collection, Filters, Orders, Limit, cursor);
    }

    /// <summary>
    /// Firestore reads behind a port (02 §5): listeners for documents and queries,
    /// one-shot reads honouring <see cref="Freshness"/>, and the two direct writes
    /// the rules allow (11 §1.4).
    /// </summary>
    public interface IDocumentStore
    {
        IDisposable Listen(string path, Action<DocumentSnapshot> onNext, Action<AppError> onError);

        Task<Result<DocumentSnapshot>> Get(string path, Freshness freshness, CancellationToken cancellation);

        Task<Result<QuerySnapshot>> Query(QuerySpec query, Freshness freshness, CancellationToken cancellation);

        IDisposable ListenQuery(QuerySpec query, Action<QuerySnapshot> onNext, Action<AppError> onError);

        /// <summary>A direct field update (own settings, notification readAt). Queued offline by the SDK.</summary>
        Task<Result> Update(string path, IReadOnlyDictionary<string, object> fields);
    }
}
