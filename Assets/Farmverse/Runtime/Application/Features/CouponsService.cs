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
    /// Coupons (partner vouchers): the codes the server issued to the player (<c>players/{uid}/coupons</c>,
    /// live, kept on the device for offline viewing) and asking for a new one (<c>coupons-claim</c>): the
    /// island is mirrored first and the server checks the task on its copy before taking a code from the pool.
    /// </summary>
    public sealed class CouponsService : IDisposable
    {
        public const int PageSize = 50;
        private const string CacheKey = "coupons";

        private readonly IDocumentStore _documents;
        private readonly ServerCalls _server;
        private readonly ILocalStore _local;
        private readonly ReactiveProperty<int> _version = new ReactiveProperty<int>(0);
        private IReadOnlyList<(string id, CouponDoc doc)> _codes = Array.Empty<(string, CouponDoc)>();
        private IDisposable? _watch;

        public CouponsService(IDocumentStore documents, ServerCalls server, ILocalStore local)
        {
            _documents = documents;
            _server = server;
            _local = local;
            LoadCache();
        }

        public IReadOnlyReactiveProperty<int> Version => _version;

        public bool Online { get; private set; }

        /// <summary>Issued codes, newest first; the id is <c>{taskId}_{day|once}</c>.</summary>
        public IReadOnlyList<(string id, CouponDoc doc)> Codes => _codes;

        public void Attach(string uid)
        {
            _watch?.Dispose();
            var query = new QuerySpec(Paths.Coupons(uid), Array.Empty<QueryFilter>(), new[] { new QueryOrder("issuedAt", true) }, PageSize);
            _watch = _documents.ListenQuery(query, OnCodes, _ => { });
            Online = true;
        }

        /// <summary>A code for a finished task (the same one again if it already paid).</summary>
        public async Task<Result<CouponsClaimResponse>> Claim(string taskId, string islandJson, CancellationToken cancellation = default)
        {
            if (!Online) return Result<CouponsClaimResponse>.Fail(ErrorCode.NetworkRequired);
            var mirrored = await _server.IslandSync(islandJson, mirror: true, cancellation);
            if (mirrored.IsFailure) return Result<CouponsClaimResponse>.Fail(mirrored.Error);
            return await _server.CouponsClaim(taskId, cancellation);
        }

        public void Dispose() => _watch?.Dispose();

        private void OnCodes(QuerySnapshot snapshot)
        {
            var rows = new List<(string, CouponDoc)>();
            var wire = new List<object>();
            foreach (var d in snapshot.Documents)
            {
                if (!d.Exists || d.Data == null) continue;
                try
                {
                    var data = new Dictionary<string, object>(d.Data);
                    rows.Add((d.Id, CouponDoc.FromWire(data)));
                    wire.Add(new Dictionary<string, object> { ["id"] = d.Id, ["doc"] = data });
                }
                catch (WireFormatException)
                {
                    // a malformed code is left out
                }
            }
            _codes = rows;
            _local.Write(CacheKey, WireJson.Serialize(wire));
            _version.Value = _version.Value + 1;
        }

        private void LoadCache()
        {
            var json = _local.Read(CacheKey);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                if (!(WireJson.Parse(json!) is List<object> list)) return;
                var rows = new List<(string, CouponDoc)>();
                foreach (var item in list)
                    if (item is Dictionary<string, object> m && m.TryGetValue("id", out var id) && id is string s && m.TryGetValue("doc", out var doc))
                        rows.Add((s, CouponDoc.FromWire(doc)));
                _codes = rows;
            }
            catch (Exception)
            {
                // a broken cache is dropped
            }
        }
    }
}
