#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;
using UnityEngine;

namespace Farmverse.Infrastructure.Web
{
    /// <summary>Sign-in behind <see cref="IAuthService"/>: the NEAR wallet (WebGL page) or the dev account (editor).</summary>
    public sealed class WebAuthService : IAuthService
    {
        private readonly WebSession _session;
        private readonly ReactiveProperty<AuthUser?> _user = new ReactiveProperty<AuthUser?>(null);

        public WebAuthService(WebSession session)
        {
            _session = session;
            _session.Changed += t => _user.Value = t == null ? null : new AuthUser(t.Uid, isAnonymous: false, accountId: t.AccountId);
        }

        public IReadOnlyReactiveProperty<AuthUser?> User => _user;

        public async Task<Result<AuthUser>> SignIn(CancellationToken cancellation)
        {
            try
            {
                var t = await _session.Acquire(false, cancellation);
                var user = new AuthUser(t.Uid, isAnonymous: false, accountId: t.AccountId);
                _user.Value = user;
                return Result<AuthUser>.Ok(user);
            }
            catch (OperationCanceledException) { return Result<AuthUser>.Fail(ErrorCode.Cancelled); }
            catch (WebUnavailableException e) { return Result<AuthUser>.Fail(AppError.FromException(ErrorCode.NetworkRequired, e)); }
            catch (PageException e) { return Result<AuthUser>.Fail(AppError.FromException(e.Rejected ? ErrorCode.Cancelled : ErrorCode.Unauthenticated, e)); }
            catch (Exception e) { return Result<AuthUser>.Fail(AppError.FromException(ErrorCode.Unauthenticated, e)); }
        }

        public async Task SignOut()
        {
            await _session.SignOut();
            _user.Value = null;
        }
    }

    /// <summary>Contract callables over HTTP: <c>POST /v1/call/{name}</c> answers the usual envelope.</summary>
    public sealed class HttpCallableTransport : ICallableTransport
    {
        private readonly WebApi _api;
        private readonly WebSession _session;

        public HttpCallableTransport(WebApi api, WebSession session)
        {
            _api = api;
            _session = session;
        }

        public async Task<object?> Invoke(string name, object payload, TimeSpan timeout, CancellationToken cancellation)
        {
            var token = await _session.AccessToken(cancellation);
            var json = WireJson.Serialize(payload);
            for (var attempt = 0; ; attempt++)
            {
                var res = await _api.Send("POST", "/v1/call/" + name, json, token, timeout, cancellation);
                if (!res.Reached) throw new TransportException(TransportFailure.Unavailable, res.Error ?? "server unreachable");
                object? body = null;
                if (res.Status != 401)
                {
                    try { body = WireJson.Parse(res.Body); }
                    catch (Exception e) { throw new TransportException(TransportFailure.Internal, $"not an envelope ({res.Status})", e); }
                }
                // the server refused the token (it answers UNAUTHENTICATED inside a 200 envelope): refresh once and send again
                if (attempt == 0 && (res.Status == 401 || IsUnauthenticated(body)))
                {
                    token = await _session.RefreshedToken(cancellation);
                    if (token != null) continue;
                }
                if (res.Status == 401) throw new TransportException(TransportFailure.Unauthenticated, "unauthenticated");
                // routed calls answer 200 with an envelope (ok or refusal); anything else carries one too, or is a defect
                if (body is Dictionary<string, object> envelope && envelope.ContainsKey("ok")) return envelope;
                throw new TransportException(TransportFailure.Internal, $"HTTP {res.Status} without an envelope");
            }
        }

        private static bool IsUnauthenticated(object? body) =>
            body is Dictionary<string, object> envelope
            && envelope.TryGetValue("ok", out var ok) && ok is bool b && !b
            && envelope.TryGetValue("error", out var e) && e is Dictionary<string, object> error
            && error.TryGetValue("code", out var code) && code as string == "UNAUTHENTICATED";
    }

    /// <summary>The server's settings (<c>GET /v1/config</c>): flags and the minimum client version.</summary>
    public sealed class WebRemoteConfig : IRemoteConfig
    {
        private readonly WebApi _api;
        private readonly ILocalStore _local;
        private Dictionary<string, object> _values = new Dictionary<string, object>();
        private const string CacheKey = "fv_remote_config";

        public WebRemoteConfig(WebApi api, ILocalStore local)
        {
            _api = api;
            _local = local;
            var cached = local.Read(CacheKey);
            if (cached != null && WireJson.Parse(cached) is Dictionary<string, object> v) _values = v;
        }

        public event Action? Updated;

        public async Task<Result> FetchAndActivate(CancellationToken cancellation)
        {
            var res = await _api.Send("GET", "/v1/config", null, null, TimeSpan.FromSeconds(8), cancellation);
            if (!res.Ok) return Result.Fail(ErrorCode.NetworkRequired);
            if (!(WireJson.Parse(res.Body) is Dictionary<string, object> body)) return Result.Fail(ErrorCode.DataCorrupt);
            var values = body.TryGetValue("values", out var v) && v is Dictionary<string, object> map ? map : new Dictionary<string, object>();
            if (!values.ContainsKey("min_client_version") && body.TryGetValue("minClientVersion", out var min)) values["min_client_version"] = min;
            _values = values;
            _local.Write(CacheKey, WireJson.Serialize(values));
            Updated?.Invoke();
            return Result.Ok();
        }

        public string GetString(string key, string fallback) => _values.TryGetValue(key, out var v) && v is string s ? s : fallback;

        public long GetLong(string key, long fallback) => _values.TryGetValue(key, out var v) ? v switch { long l => l, double d => (long)d, _ => fallback } : fallback;

        public bool GetBool(string key, bool fallback) => _values.TryGetValue(key, out var v) ? v switch { bool b => b, string s => s == "true", _ => fallback } : fallback;
    }

    /// <summary>A stable random id for this install, kept in the local store.</summary>
    public sealed class WebInstallationId : IInstallationId
    {
        private const string Key = "fv_installation_id";
        private readonly ILocalStore _local;

        public WebInstallationId(ILocalStore local) => _local = local;

        public Task<string> Get(CancellationToken cancellation)
        {
            var id = _local.Read(Key);
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
                _local.Write(Key, id);
            }
            return Task.FromResult(id!);
        }
    }

    /// <summary>
    /// The local store on PlayerPrefs (WebGL: files under persistentDataPath aren't reliably flushed
    /// to IndexedDB, PlayerPrefs are).
    /// </summary>
    public sealed class PlayerPrefsLocalStore : ILocalStore
    {
        private const string Prefix = "fv_store_";

        public string? Read(string key) => PlayerPrefs.HasKey(Prefix + key) ? PlayerPrefs.GetString(Prefix + key) : null;

        public void Write(string key, string value)
        {
            PlayerPrefs.SetString(Prefix + key, value);
            PlayerPrefs.Save();
        }

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(Prefix + key);
            PlayerPrefs.Save();
        }
    }
}

namespace Farmverse.Infrastructure.Web
{
    /// <summary>The page's NEAR wallet (WebGL): each call is approved by the player in their wallet.</summary>
    public sealed class PageWallet : IWallet
    {
        public bool Available => true;

        public async Task<string> Call(string contractId, string method, IReadOnlyDictionary<string, object> args, string depositYocto, int gasTgas)
        {
            var request = WireJson.Serialize(new Dictionary<string, object>
            {
                ["contractId"] = contractId,
                ["method"] = method,
                ["args"] = new Dictionary<string, object>(args),
                ["deposit"] = depositYocto,
                ["gasTgas"] = (long)gasTgas,
            });
            try
            {
                var json = await PageBridge.Instance.CallContract(request);
                if (WireJson.Parse(json) is Dictionary<string, object> m && m.TryGetValue("txHash", out var h) && h is string hash) return hash;
                throw new WalletException("the wallet returned no transaction", declined: false);
            }
            catch (PageException e)
            {
                throw new WalletException(e.Message, e.Rejected);
            }
        }
    }

    /// <summary>No wallet (editor, desktop): chain actions are only offered in the web build.</summary>
    public sealed class NoWallet : IWallet
    {
        public bool Available => false;

        public Task<string> Call(string contractId, string method, IReadOnlyDictionary<string, object> args, string depositYocto, int gasTgas) =>
            throw new WalletException("wallets are only available in the web build", declined: false);
    }
}
