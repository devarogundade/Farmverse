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
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;

namespace Farmverse.Infrastructure.Web
{
    /// <summary>
    /// Documents from the game server (docs 13 §5.1): one-shot reads and queries over HTTP, listeners
    /// over the live channel, and the two direct writes (settings, notification readAt).
    /// </summary>
    public sealed class WebDocumentStore : IDocumentStore
    {
        private readonly WebApi _api;
        private readonly WebSession _session;
        private readonly LiveChannel _live;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);

        public WebDocumentStore(WebApi api, WebSession session, LiveChannel live)
        {
            _api = api;
            _session = session;
            _live = live;
        }

        public IDisposable Listen(string path, Action<DocumentSnapshot> onNext, Action<AppError> onError) =>
            _live.Subscribe(new Dictionary<string, object> { ["type"] = "sub", ["path"] = path }, m => onNext(DocOf(m)), onError);

        public IDisposable ListenQuery(QuerySpec query, Action<QuerySnapshot> onNext, Action<AppError> onError) =>
            _live.Subscribe(new Dictionary<string, object> { ["type"] = "subq", ["query"] = Wire(query) }, m => onNext(QueryOf(m)), onError);

        public async Task<Result<DocumentSnapshot>> Get(string path, Freshness freshness, CancellationToken cancellation)
        {
            var res = await Call("GET", "/v1/doc?path=" + Uri.EscapeDataString(path), null, cancellation);
            return res.IsOk ? Result<DocumentSnapshot>.Ok(DocOf(res.Value)) : Result<DocumentSnapshot>.Fail(res.Error);
        }

        public async Task<Result<QuerySnapshot>> Query(QuerySpec query, Freshness freshness, CancellationToken cancellation)
        {
            var res = await Call("POST", "/v1/query", WireJson.Serialize(Wire(query)), cancellation);
            return res.IsOk ? Result<QuerySnapshot>.Ok(QueryOf(res.Value)) : Result<QuerySnapshot>.Fail(res.Error);
        }

        public async Task<Result> Update(string path, IReadOnlyDictionary<string, object> fields)
        {
            var body = new Dictionary<string, object> { ["path"] = path, ["fields"] = fields.ToDictionary(p => p.Key, p => p.Value) };
            var res = await Call("PATCH", "/v1/doc", WireJson.Serialize(body), CancellationToken.None);
            return res.IsOk ? Result.Ok() : Result.Fail(res.Error);
        }

        private async Task<Result<Dictionary<string, object>>> Call(string method, string path, string? json, CancellationToken cancellation)
        {
            WebResponse res;
            try
            {
                var token = await _session.AccessToken(cancellation);
                res = await _api.Send(method, path, json, token, Timeout, cancellation);
            }
            catch (OperationCanceledException) { return Result<Dictionary<string, object>>.Fail(ErrorCode.Cancelled); }
            if (!res.Reached) return Result<Dictionary<string, object>>.Fail(ErrorCode.NetworkRequired);
            var body = WireJson.Parse(res.Body) as Dictionary<string, object>;
            if (res.Ok && body != null) return Result<Dictionary<string, object>>.Ok(body);
            return Result<Dictionary<string, object>>.Fail(ErrorOf(body, res.Status));
        }

        internal static AppError ErrorOf(Dictionary<string, object>? body, long status)
        {
            if (body != null && body.TryGetValue("error", out var e) && e is Dictionary<string, object> err && err.TryGetValue("code", out var c) && c is string code)
                return AppError.Of(ErrorCodeWire.FromWire(code));
            return AppError.Of(status switch { 401 => ErrorCode.Unauthenticated, 403 => ErrorCode.PermissionDenied, 404 => ErrorCode.NotFound, 429 => ErrorCode.RateLimited, _ => ErrorCode.Internal });
        }

        private static Dictionary<string, object> Wire(QuerySpec q)
        {
            var d = new Dictionary<string, object>
            {
                ["collection"] = q.Collection,
                ["filters"] = q.Filters.Select(f => (object)new Dictionary<string, object> { ["field"] = f.Field, ["op"] = Op(f.Op), ["value"] = f.Value }).ToList(),
                ["orders"] = q.Orders.Select(o => (object)new Dictionary<string, object> { ["field"] = o.Field, ["direction"] = o.Descending ? "desc" : "asc" }).ToList(),
                ["limit"] = (long)q.Limit,
            };
            if (q.StartAfter != null) d["startAfter"] = q.StartAfter.ToList();
            return d;
        }

        private static string Op(FilterOp op) => op switch
        {
            FilterOp.Equal => "==",
            FilterOp.Less => "<",
            FilterOp.LessOrEqual => "<=",
            FilterOp.Greater => ">",
            FilterOp.GreaterOrEqual => ">=",
            FilterOp.In => "in",
            _ => "==",
        };

        private static DocumentSnapshot DocOf(Dictionary<string, object> m)
        {
            var path = m.TryGetValue("path", out var p) ? p as string ?? string.Empty : string.Empty;
            var exists = m.TryGetValue("exists", out var e) && e is bool b && b;
            var data = m.TryGetValue("data", out var d) ? d as Dictionary<string, object> : null;
            return new DocumentSnapshot(path, exists && data != null, exists ? data : null, fromCache: false, hasPendingWrites: false);
        }

        private static QuerySnapshot QueryOf(Dictionary<string, object> m)
        {
            var docs = m.TryGetValue("docs", out var d) && d is List<object> list ? list : new List<object>();
            var snaps = docs.OfType<Dictionary<string, object>>()
                .Select(r => new DocumentSnapshot(r.TryGetValue("path", out var p) ? p as string ?? string.Empty : string.Empty, true,
                    r.TryGetValue("data", out var data) ? data as Dictionary<string, object> : null, false, false))
                .ToList();
            return new QuerySnapshot(snaps, fromCache: false);
        }
    }

    /// <summary>
    /// The server's live channel (<c>/v1/live</c>): one WebSocket for every listener. Authenticates
    /// first, then (re)subscribes everything; reconnects with backoff and resubscribes, so listeners
    /// survive a dropped connection. Pushes for a document carry a revision; stale ones are dropped.
    /// </summary>
    public sealed class LiveChannel
    {
        private sealed class Sub
        {
            public Dictionary<string, object> Request = new Dictionary<string, object>();
            public Action<Dictionary<string, object>> OnNext = _ => { };
            public Action<AppError> OnError = _ => { };
            public long Rev = -1;
        }

        private readonly Func<ITextSocket> _newSocket;
        private readonly string _url;
        private readonly WebSession _session;
        private readonly ILog _log;
        private readonly Dictionary<string, Sub> _subs = new Dictionary<string, Sub>();
        private ITextSocket? _socket;
        private bool _ready;
        private bool _connecting;
        private int _failures;
        private int _next;

        public LiveChannel(Func<ITextSocket> newSocket, string url, WebSession session, ILog log)
        {
            _newSocket = newSocket;
            _url = url;
            _session = session;
            _log = log.For("Live");
            _session.Changed += _ => Reconnect();
        }

        public IDisposable Subscribe(Dictionary<string, object> request, Action<Dictionary<string, object>> onNext, Action<AppError> onError)
        {
            var id = "s" + (++_next);
            var sub = new Sub { Request = request, OnNext = onNext, OnError = onError };
            _subs[id] = sub;
            if (_ready) Send(id, sub);
            else Connect();
            return Disposable.Create(() =>
            {
                if (!_subs.Remove(id)) return;
                if (_ready) _socket?.Send(WireJson.Serialize(new Dictionary<string, object> { ["type"] = "unsub", ["id"] = id }));
                if (_subs.Count == 0) Disconnect();
            });
        }

        private void Send(string id, Sub sub)
        {
            var message = new Dictionary<string, object>(sub.Request) { ["id"] = id };
            _socket?.Send(WireJson.Serialize(message));
        }

        private async void Connect()
        {
            if (_socket != null || _connecting || _subs.Count == 0) return;
            _connecting = true;
            try
            {
                var token = await _session.AccessToken(CancellationToken.None);
                if (token == null) { Retry(); return; }
                var socket = _newSocket();
                _socket = socket;
                socket.Opened += () => socket.Send(WireJson.Serialize(new Dictionary<string, object> { ["type"] = "auth", ["token"] = token }));
                socket.Message += OnMessage;
                socket.Closed += reason =>
                {
                    if (_socket != socket) return;
                    _socket = null;
                    _ready = false;
                    _log.Info("live channel closed", new Dictionary<string, object?> { ["reason"] = reason });
                    Retry();
                };
                socket.Connect(_url);
            }
            finally
            {
                _connecting = false;
            }
        }

        private async void Retry()
        {
            if (_subs.Count == 0) return;
            _failures++;
            var delayMs = Math.Min(30_000, 500 * (1 << Math.Min(6, _failures)));
            await Task.Delay(delayMs);
            Connect();
        }

        private void Reconnect()
        {
            Disconnect();
            foreach (var s in _subs.Values) s.Rev = -1;
            Connect();
        }

        private void Disconnect()
        {
            var s = _socket;
            _socket = null;
            _ready = false;
            s?.Close();
        }

        private void OnMessage(string text)
        {
            if (!(WireJson.Parse(text) is Dictionary<string, object> m)) return;
            var type = m.TryGetValue("type", out var t) ? t as string : null;
            var id = m.TryGetValue("id", out var i) ? i as string : null;
            switch (type)
            {
                case "ready":
                    _ready = true;
                    _failures = 0;
                    foreach (var kv in _subs) Send(kv.Key, kv.Value);
                    break;
                case "doc":
                    if (id != null && _subs.TryGetValue(id, out var ds))
                    {
                        var rev = m.TryGetValue("rev", out var r) && r is long l ? l : 0;
                        if (rev != 0 && rev < ds.Rev) return; // an older push overtaken by a newer one
                        ds.Rev = rev;
                        ds.OnNext(m);
                    }
                    break;
                case "query":
                    if (id != null && _subs.TryGetValue(id, out var qs)) qs.OnNext(m);
                    break;
                case "error":
                    var code = m.TryGetValue("code", out var c) ? c as string ?? "INTERNAL" : "INTERNAL";
                    if (id != null && _subs.TryGetValue(id, out var es)) es.OnError(AppError.Of(ErrorCodeWire.FromWire(code)));
                    else if (code == "UNAUTHENTICATED") _ = _session.Acquire(true, CancellationToken.None).ContinueWith(_ => Reconnect(), TaskScheduler.FromCurrentSynchronizationContext());
                    break;
            }
        }
    }
}
