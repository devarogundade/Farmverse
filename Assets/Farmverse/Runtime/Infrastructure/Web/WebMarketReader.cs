#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Results;

namespace Farmverse.Infrastructure.Web
{
    /// <summary>The market's public read API (`/v1/market`, no sign-in) for the game (docs 15 §3.1).</summary>
    public sealed class WebMarketReader : IMarketReader
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
        private readonly WebApi _api;
        private readonly Func<string> _rpcUrl;

        /// <param name="rpcUrl">The NEAR RPC node (from the server's config); balances are view calls on it.</param>
        public WebMarketReader(WebApi api, Func<string> rpcUrl)
        {
            _api = api;
            _rpcUrl = rpcUrl;
        }

        public async Task<Result<(string farm, string near)>> Balances(string account, string farmContract, CancellationToken cancellation)
        {
            var rpc = _rpcUrl();
            if (string.IsNullOrEmpty(rpc)) return Result<(string, string)>.Fail(ErrorCode.Unsupported);
            var farm = "0";
            if (!string.IsNullOrEmpty(farmContract))
            {
                var args = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"account_id\":\"" + account + "\"}"));
                var r = await Rpc(rpc, new Dictionary<string, object>
                {
                    ["request_type"] = "call_function", ["finality"] = "final", ["account_id"] = farmContract, ["method_name"] = "ft_balance_of", ["args_base64"] = args,
                }, cancellation);
                if (r is Dictionary<string, object> res && res.TryGetValue("result", out var bytes) && bytes is List<object> list)
                {
                    var raw = new byte[list.Count];
                    for (var i = 0; i < list.Count; i++) raw[i] = (byte)Convert.ToInt32(list[i]);
                    farm = System.Text.Encoding.UTF8.GetString(raw).Trim('"');
                }
            }
            var near = "0";
            var a = await Rpc(rpc, new Dictionary<string, object> { ["request_type"] = "view_account", ["finality"] = "final", ["account_id"] = account }, cancellation);
            if (a is Dictionary<string, object> acc && acc.TryGetValue("amount", out var amt) && amt is string s) near = s;
            return Result<(string, string)>.Ok((farm, near));
        }

        private async Task<object?> Rpc(string url, Dictionary<string, object> queryParams, CancellationToken cancellation)
        {
            var body = WireJson.Serialize(new Dictionary<string, object> { ["jsonrpc"] = "2.0", ["id"] = "fv", ["method"] = "query", ["params"] = queryParams });
            try
            {
                var res = await _api.SendAbsolute("POST", url, body, Timeout, cancellation);
                if (!res.Ok) return null;
                return WireJson.Parse(res.Body) is Dictionary<string, object> m && m.TryGetValue("result", out var r) ? r : null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public async Task<Result<IReadOnlyList<OwnedNft>>> Owned(string account, CancellationToken cancellation)
        {
            var r = await Get("/v1/market/owners/" + Uri.EscapeDataString(account) + "/tokens?all=1&limit=60", cancellation);
            if (!r.IsOk) return Result<IReadOnlyList<OwnedNft>>.Fail(r.Error);
            var list = new List<OwnedNft>();
            if (r.Value is Dictionary<string, object> page && page.TryGetValue("items", out var items) && items is List<object> rows)
            {
                foreach (var row in rows)
                {
                    if (row is not Dictionary<string, object> t) continue;
                    var meta = t.TryGetValue("metadata", out var m) && m is Dictionary<string, object> md ? md : new Dictionary<string, object>();
                    var props = meta.TryGetValue("props", out var pr) && pr is Dictionary<string, object> pd ? pd : new Dictionary<string, object>();
                    list.Add(new OwnedNft
                    {
                        TokenId = Str(t, "token_id"),
                        Title = Str(t, "title"),
                        Kind = Str(t, "kind"),
                        ItemId = Str(meta, "item_id"),
                        Quantity = Long(meta, "quantity", 1),
                        Price = t.TryGetValue("price", out var p) && p is string ps ? ps : null,
                        Props = props,
                    });
                }
            }
            return Result<IReadOnlyList<OwnedNft>>.Ok(list);
        }

        public async Task<Result<IReadOnlyList<MarketDrop>>> Drops(CancellationToken cancellation)
        {
            var r = await Get("/v1/market/drops", cancellation);
            if (!r.IsOk) return Result<IReadOnlyList<MarketDrop>>.Fail(r.Error);
            var list = new List<MarketDrop>();
            if (r.Value is List<object> rows)
            {
                foreach (var row in rows)
                {
                    if (row is not Dictionary<string, object> d) continue;
                    list.Add(new MarketDrop
                    {
                        Id = Str(d, "id"),
                        Kind = Str(d, "kind"),
                        ItemId = Str(d, "item_id"),
                        Title = Str(d, "title"),
                        Price = Str(d, "price"),
                        Cap = d.TryGetValue("cap", out var c) && c != null ? Long(d, "cap", 0) : (long?)null,
                        Sold = Long(d, "sold", 0),
                        PerWallet = d.TryGetValue("per_wallet", out var w) && w != null ? (int)Long(d, "per_wallet", 0) : (int?)null,
                        StartsAt = Long(d, "starts_at", 0),
                        EndsAt = d.TryGetValue("ends_at", out var e) && e != null ? Long(d, "ends_at", 0) : (long?)null,
                        Paused = d.TryGetValue("paused", out var pa) && pa is bool b && b,
                    });
                }
            }
            return Result<IReadOnlyList<MarketDrop>>.Ok(list);
        }

        public async Task<Result<IReadOnlyList<ItemOffer>>> OffersForItem(string itemId, CancellationToken cancellation)
        {
            var r = await Get("/v1/market/offers?item=" + Uri.EscapeDataString(itemId), cancellation);
            if (!r.IsOk) return Result<IReadOnlyList<ItemOffer>>.Fail(r.Error);
            var list = new List<ItemOffer>();
            if (r.Value is List<object> rows)
            {
                foreach (var row in rows)
                {
                    if (row is not Dictionary<string, object> o) continue;
                    list.Add(new ItemOffer
                    {
                        Id = Str(o, "id"),
                        From = Str(o, "from"),
                        ItemId = Str(o, "item_id"),
                        MinQuantity = Long(o, "min_quantity", 1),
                        Amount = Str(o, "amount"),
                        ExpiresAt = Long(o, "expires_at", 0),
                    });
                }
            }
            return Result<IReadOnlyList<ItemOffer>>.Ok(list);
        }

        private async Task<Result<object?>> Get(string path, CancellationToken cancellation)
        {
            WebResponse res;
            try
            {
                res = await _api.Send("GET", path, null, null, Timeout, cancellation);
            }
            catch (OperationCanceledException)
            {
                return Result<object?>.Fail(ErrorCode.Cancelled);
            }
            if (!res.Reached) return Result<object?>.Fail(ErrorCode.NetworkRequired);
            if (!res.Ok) return Result<object?>.Fail(ErrorCode.Internal);
            return Result<object?>.Ok(WireJson.Parse(res.Body));
        }

        private static string Str(Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) && v is string s ? s : "";

        private static long Long(Dictionary<string, object> d, string key, long fallback) =>
            d.TryGetValue(key, out var v) ? v switch { long l => l, int i => i, double x => (long)x, string s when long.TryParse(s, out var n) => n, _ => fallback } : fallback;
    }
}
