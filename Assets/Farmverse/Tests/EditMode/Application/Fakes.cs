#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Lists;
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;

namespace Farmverse.Tests.Application
{
    /// <summary>Records calls and answers with what the test scripts, in order.</summary>
    internal sealed class FakeTransport : ICallableTransport
    {
        public readonly List<(string Name, Dictionary<string, object> Payload)> Calls = new List<(string, Dictionary<string, object>)>();
        private readonly Queue<Func<string, Dictionary<string, object>, object?>> _answers = new Queue<Func<string, Dictionary<string, object>, object?>>();
        public long ServerTimeMs = 1_800_000_000_000L;

        /// <summary>Answer the next call with a success envelope around <paramref name="data"/>.</summary>
        public void Ok(Func<Dictionary<string, object>, object> data) =>
            _answers.Enqueue((_, payload) => Envelope(true, data(payload)));

        public void Refuse(string code, Dictionary<string, string>? parameters = null) =>
            _answers.Enqueue((_, __) => new Dictionary<string, object>
            {
                ["ok"] = false,
                ["error"] = new Dictionary<string, object> { ["code"] = code, ["params"] = ToObjects(parameters ?? new Dictionary<string, string>()) },
                ["serverTimeMs"] = ServerTimeMs,
                ["requestId"] = "r",
            });

        public void Fail(TransportFailure failure) =>
            _answers.Enqueue((_, __) => throw new TransportException(failure, "scripted"));

        public void Raw(object? raw) => _answers.Enqueue((_, __) => raw);

        public Task<object?> Invoke(string name, object payload, TimeSpan timeout, CancellationToken cancellation)
        {
            var map = (Dictionary<string, object>)payload;
            Calls.Add((name, map));
            if (_answers.Count == 0) throw new InvalidOperationException("no scripted answer for " + name);
            return Task.FromResult(_answers.Dequeue()(name, map));
        }

        public Dictionary<string, object> Envelope(bool ok, object data) => new Dictionary<string, object>
        {
            ["ok"] = ok, ["data"] = data, ["serverTimeMs"] = ServerTimeMs, ["requestId"] = "r",
        };

        private static Dictionary<string, object> ToObjects(Dictionary<string, string> map)
        {
            var copy = new Dictionary<string, object>();
            foreach (var pair in map) copy[pair.Key] = pair.Value;
            return copy;
        }
    }

    internal sealed class MemoryLocalStore : ILocalStore
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public string? Read(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void Write(string key, string value) => Values[key] = value;
        public void Delete(string key) => Values.Remove(key);
    }

    internal sealed class FakeConnectivity : IConnectivity
    {
        public readonly ReactiveProperty<bool> Online = new ReactiveProperty<bool>(true);
        public IReadOnlyReactiveProperty<bool> IsOnline => Online;
    }

    internal sealed class NullCrash : ICrashReporter
    {
        public readonly List<string> Breadcrumbs = new List<string>();
        public void SetUser(string uid) { }
        public void SetKey(string key, string value) { }
        public void Breadcrumb(string message) => Breadcrumbs.Add(message);
        public void RecordNonFatal(string name, string message, IReadOnlyDictionary<string, string>? fields = null) { }
    }

    /// <summary>An in-memory Firestore: documents by path, listeners fire synchronously on Put.</summary>
    internal sealed class FakeDocumentStore : IDocumentStore
    {
        private readonly SortedDictionary<string, Dictionary<string, object>> _docs = new SortedDictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        private readonly List<(string Path, Action<DocumentSnapshot> OnNext)> _listeners = new List<(string, Action<DocumentSnapshot>)>();
        private readonly List<(QuerySpec Query, Action<QuerySnapshot> OnNext)> _queryListeners = new List<(QuerySpec, Action<QuerySnapshot>)>();
        public readonly List<QuerySpec> Queries = new List<QuerySpec>();
        public readonly List<(string Path, IReadOnlyDictionary<string, object> Fields)> Updates = new List<(string, IReadOnlyDictionary<string, object>)>();

        public void Put(string path, IWireObject doc) => Put(path, doc.ToWire());

        public void Put(string path, Dictionary<string, object> data)
        {
            _docs[path] = data;
            foreach (var l in _listeners.ToArray()) if (l.Path == path) l.OnNext(Snapshot(path));
            foreach (var q in _queryListeners.ToArray()) if (path.StartsWith(q.Query.Collection + "/")) q.OnNext(QueryNow(q.Query));
        }

        public IDisposable Listen(string path, Action<DocumentSnapshot> onNext, Action<AppError> onError)
        {
            var entry = (path, onNext);
            _listeners.Add(entry);
            onNext(Snapshot(path));
            return Farmverse.Core.Reactive.Disposable.Create(() => _listeners.Remove(entry));
        }

        public Task<Result<DocumentSnapshot>> Get(string path, Freshness freshness, CancellationToken cancellation) =>
            Task.FromResult(Result<DocumentSnapshot>.Ok(Snapshot(path)));

        public Task<Result<QuerySnapshot>> Query(QuerySpec query, Freshness freshness, CancellationToken cancellation)
        {
            Queries.Add(query);
            return Task.FromResult(Result<QuerySnapshot>.Ok(QueryNow(query)));
        }

        public IDisposable ListenQuery(QuerySpec query, Action<QuerySnapshot> onNext, Action<AppError> onError)
        {
            var entry = (query, onNext);
            _queryListeners.Add(entry);
            onNext(QueryNow(query));
            return Farmverse.Core.Reactive.Disposable.Create(() => _queryListeners.Remove(entry));
        }

        public Task<Result> Update(string path, IReadOnlyDictionary<string, object> fields)
        {
            Updates.Add((path, fields));
            return Task.FromResult(Result.Ok());
        }

        private DocumentSnapshot Snapshot(string path) =>
            _docs.TryGetValue(path, out var data) ? new DocumentSnapshot(path, true, data, false, false) : new DocumentSnapshot(path, false, null, false, false);

        private QuerySnapshot QueryNow(QuerySpec query)
        {
            var depth = query.Collection.Split('/').Length + 1;
            var docs = new List<DocumentSnapshot>();
            foreach (var pair in _docs)
            {
                if (pair.Key.StartsWith(query.Collection + "/") && pair.Key.Split('/').Length == depth) docs.Add(Snapshot(pair.Key));
            }
            return new QuerySnapshot(docs, false);
        }
    }
}
