#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Logging;
using Farmverse.Core.Results;
using Farmverse.Infrastructure.Web;

namespace Farmverse.Bootstrap
{
    /// <summary>
    /// The session's platform step on the web (docs 13 §5): check the game server answers, then bind
    /// the web adapters behind the deferred ports. Idempotent, so a retry after a failed boot runs again.
    /// </summary>
    internal sealed class WebComposition : IPlatformBootstrap
    {
        private readonly WebApi _api;
        private readonly WebSession _session;
        private readonly LiveChannel _live;
        private readonly ILocalStore _local;
        private readonly ILog _log;
        private readonly DeferredDocumentStore _documents;
        private readonly DeferredAuth _auth;
        private readonly DeferredRemoteConfig _remoteConfig;
        private bool _bound;

        public WebComposition(WebApi api, WebSession session, LiveChannel live, ILocalStore local, ILog log,
            DeferredDocumentStore documents, DeferredAuth auth, DeferredRemoteConfig remoteConfig)
        {
            _api = api;
            _session = session;
            _live = live;
            _local = local;
            _log = log.For("Platform");
            _documents = documents;
            _auth = auth;
            _remoteConfig = remoteConfig;
        }

        public async Task<Result> Initialize(CancellationToken cancellation)
        {
            var health = await _api.Send("GET", "/healthz", null, null, TimeSpan.FromSeconds(8), cancellation);
            if (!health.Ok)
            {
                _log.Warn("game server not reachable", new System.Collections.Generic.Dictionary<string, object?> { ["url"] = _api.Url("/healthz"), ["status"] = health.Status, ["error"] = health.Error });
                return Result.Fail(health.Reached ? ErrorCode.Maintenance : ErrorCode.NetworkRequired);
            }
            if (_bound) return Result.Ok();
            _documents.Bind(new WebDocumentStore(_api, _session, _live));
            _auth.Bind(new WebAuthService(_session));
            _remoteConfig.Bind(new WebRemoteConfig(_api, _local));
            _bound = true;
            return Result.Ok();
        }
    }
}
