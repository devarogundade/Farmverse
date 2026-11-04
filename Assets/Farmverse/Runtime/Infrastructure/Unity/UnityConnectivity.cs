#nullable enable
using Farmverse.Application.Ports;
using Farmverse.Core.Reactive;
using UnityEngine;

namespace Farmverse.Infrastructure.Unity
{
    /// <summary>
    /// Online/offline from the OS reachability flag, sampled twice a second. It is a
    /// hint (captive portals report "reachable"); real failures still surface as
    /// NETWORK_REQUIRED from the transport, and the action queue retries.
    /// </summary>
    public sealed class UnityConnectivity : MonoBehaviour, IConnectivity
    {
        private readonly ReactiveProperty<bool> _online = new ReactiveProperty<bool>(true);
        private float _next;

        public IReadOnlyReactiveProperty<bool> IsOnline => _online;

        private void Awake() => Sample();

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            Sample();
        }

        private void Sample() => _online.Value = UnityEngine.Application.internetReachability != NetworkReachability.NotReachable;
    }
}
