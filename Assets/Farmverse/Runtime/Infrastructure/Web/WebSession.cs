#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;

namespace Farmverse.Infrastructure.Web
{
    /// <summary>A signed-in session: the short-lived access token and who it belongs to.</summary>
    public sealed class SessionTokens
    {
        public string AccessToken = string.Empty;
        /// <summary>Epoch ms.</summary>
        public long AccessExpiresAt;
        /// <summary>The game uid (opaque).</summary>
        public string Uid = string.Empty;
        /// <summary>The NEAR account.</summary>
        public string AccountId = string.Empty;
        /// <summary>Only when this client owns the refresh (the editor's dev sign-in).</summary>
        public string? RefreshToken;

        public static SessionTokens? FromJson(string json)
        {
            if (!(WireJson.Parse(json) is Dictionary<string, object> m)) return null;
            string S(string k) => m.TryGetValue(k, out var v) ? v as string ?? string.Empty : string.Empty;
            var t = new SessionTokens
            {
                AccessToken = S("accessToken"),
                Uid = S("uid"),
                AccountId = S("accountId"),
                RefreshToken = m.TryGetValue("refreshToken", out var r) ? r as string : null,
                AccessExpiresAt = m.TryGetValue("accessExpiresAt", out var e) && e is long l ? l : 0,
            };
            return t.AccessToken.Length > 0 && t.Uid.Length > 0 ? t : null;
        }
    }

    /// <summary>Who can produce session tokens: the page's wallet sign-in (WebGL) or the dev sign-in (editor).</summary>
    public interface ITokenSource
    {
        /// <summary>A session whose access token is valid for a while (signing in or refreshing as needed).</summary>
        Task<SessionTokens> Acquire(SessionTokens? current, bool forceRefresh, CancellationToken cancellation);
        Task SignOut(SessionTokens? current);
    }

    /// <summary>
    /// The current session for every web adapter: hands out a fresh access token (refreshing a minute
    /// before expiry) and remembers who is signed in.
    /// </summary>
    public sealed class WebSession
    {
        private static readonly TimeSpan Margin = TimeSpan.FromSeconds(60);
        private readonly ITokenSource _source;
        private readonly Func<long> _nowMs;
        private SessionTokens? _current;
        private Task<SessionTokens>? _inFlight;

        public WebSession(ITokenSource source, Func<long> nowMs)
        {
            _source = source;
            _nowMs = nowMs;
        }

        public SessionTokens? Current => _current;

        /// <summary>Raised when the signed-in account changes (sign-in, sign-out).</summary>
        public event Action<SessionTokens?>? Changed;

        /// <summary>A valid access token, or null when signing in isn't possible right now.</summary>
        public async Task<string?> AccessToken(CancellationToken cancellation)
        {
            var c = _current;
            if (c != null && c.AccessExpiresAt - _nowMs() > Margin.TotalMilliseconds) return c.AccessToken;
            try { return (await Acquire(false, cancellation)).AccessToken; }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
        }

        /// <summary>
        /// A new access token after the server refused the one we had (expired early, a clock off, the
        /// server's key changed): the session is refreshed even though it looks valid here.
        /// </summary>
        public async Task<string?> RefreshedToken(CancellationToken cancellation)
        {
            try { return (await Acquire(true, cancellation)).AccessToken; }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
        }

        /// <summary>Signs in (or refreshes). Concurrent callers share one attempt.</summary>
        public Task<SessionTokens> Acquire(bool forceRefresh, CancellationToken cancellation)
        {
            if (_inFlight != null && !forceRefresh) return _inFlight;
            var attempt = Run(forceRefresh, cancellation);
            _inFlight = attempt;
            return attempt;
        }

        private async Task<SessionTokens> Run(bool forceRefresh, CancellationToken cancellation)
        {
            try
            {
                var before = _current;
                var next = await _source.Acquire(before, forceRefresh, cancellation);
                _current = next;
                if (before?.Uid != next.Uid) Changed?.Invoke(next);
                return next;
            }
            finally
            {
                _inFlight = null;
            }
        }

        public async Task SignOut()
        {
            var before = _current;
            _current = null;
            await _source.SignOut(before);
            if (before != null) Changed?.Invoke(null);
        }
    }

    /// <summary>WebGL: the page signs in with the wallet and keeps the refresh token; the game asks it.</summary>
    public sealed class PageTokenSource : ITokenSource
    {
        public async Task<SessionTokens> Acquire(SessionTokens? current, bool forceRefresh, CancellationToken cancellation)
        {
            var json = await PageBridge.Instance.RequestSession(forceRefresh || current != null);
            cancellation.ThrowIfCancellationRequested();
            return SessionTokens.FromJson(json) ?? throw new PageException("the page returned no session");
        }

        public Task SignOut(SessionTokens? current)
        {
            PageBridge.Instance.SignOut();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Editor and desktop: the server's dev sign-in (only enabled with FARMVERSE_ENV=dev), then
    /// refresh-token rotation. The refresh token is kept in the local store between runs.
    /// </summary>
    public sealed class DevTokenSource : ITokenSource
    {
        private const string RefreshKey = "fv_dev_refresh";
        private readonly WebApi _api;
        private readonly string _account;
        private readonly ILocalStore _local;

        public DevTokenSource(WebApi api, string account, ILocalStore local)
        {
            _api = api;
            _account = account;
            _local = local;
        }

        public async Task<SessionTokens> Acquire(SessionTokens? current, bool forceRefresh, CancellationToken cancellation)
        {
            var refresh = current?.RefreshToken ?? _local.Read(RefreshKey);
            if (!string.IsNullOrEmpty(refresh))
            {
                var r = await _api.Send("POST", "/v1/auth/refresh", WireJson.Serialize(new Dictionary<string, object> { ["refreshToken"] = refresh! }), null, TimeSpan.FromSeconds(10), cancellation);
                var refreshed = r.Ok ? SessionTokens.FromJson(r.Body) : null;
                if (refreshed != null && refreshed.AccountId == _account) return Keep(refreshed);
            }
            var res = await _api.Send("POST", "/v1/auth/dev", WireJson.Serialize(new Dictionary<string, object> { ["accountId"] = _account }), null, TimeSpan.FromSeconds(10), cancellation);
            if (!res.Reached) throw new WebUnavailableException(res.Error ?? "server unreachable");
            return Keep(SessionTokens.FromJson(res.Body) ?? throw new InvalidOperationException($"dev sign-in refused ({res.Status}): is the server running with FARMVERSE_ENV=dev?"));
        }

        public async Task SignOut(SessionTokens? current)
        {
            var refresh = current?.RefreshToken ?? _local.Read(RefreshKey);
            _local.Delete(RefreshKey);
            if (!string.IsNullOrEmpty(refresh))
                await _api.Send("POST", "/v1/auth/logout", WireJson.Serialize(new Dictionary<string, object> { ["refreshToken"] = refresh! }), null, TimeSpan.FromSeconds(10), CancellationToken.None);
        }

        private SessionTokens Keep(SessionTokens t)
        {
            if (!string.IsNullOrEmpty(t.RefreshToken)) _local.Write(RefreshKey, t.RefreshToken!);
            return t;
        }
    }

    /// <summary>The server can't be reached (offline, DNS, server down).</summary>
    public sealed class WebUnavailableException : Exception
    {
        public WebUnavailableException(string message) : base(message) { }
    }
}
