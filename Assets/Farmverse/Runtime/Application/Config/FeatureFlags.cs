#nullable enable
using System;
using Farmverse.Application.Ports;

namespace Farmverse.Application.Config
{
    /// <summary>
    /// The Remote Config switches the client acts on (11 §3), as reactive values.
    /// Read again after the session's fetch and on every real-time update, so a
    /// switch flipped in the console reaches running games without a restart.
    /// </summary>
    public sealed class FeatureFlags : IDisposable
    {
        private readonly IRemoteConfig _config;

        public FeatureFlags(IRemoteConfig config)
        {
            _config = config;
            _config.Updated += Refresh;
            Refresh();
        }

        /// <summary>Rollout phase 1: mirror the island to the server engine and compare (<c>island_shadow</c>).</summary>
        public bool IslandShadow { get; private set; } = RemoteKeys.IslandShadowDefault;

        public void Refresh()
        {
            IslandShadow = _config.GetBool(RemoteKeys.IslandShadow, RemoteKeys.IslandShadowDefault);
        }

        public void Dispose() => _config.Updated -= Refresh;
    }
}
