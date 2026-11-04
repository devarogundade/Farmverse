#nullable enable
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>The lighthouse beam: sweeps round, bright when the sun is low or the weather is dark.</summary>
    [RequireComponent(typeof(Light))]
    public sealed class IslandLighthouse : MonoBehaviour
    {
        [SerializeField] private float degreesPerSecond = 40f;
        [SerializeField] private float nightIntensity = 7f;

        private Light? _light;

        private void Awake() => _light = GetComponent<Light>();

        private void Update()
        {
            transform.localRotation = Quaternion.Euler(8f, Time.time * degreesPerSecond, 0f);
            if (_light == null) return;
            var sun = RenderSettings.sun;
            float day = sun != null && sun.enabled ? Mathf.Clamp01(sun.intensity / 1.1f) : 1f;
            _light.intensity = Mathf.Lerp(nightIntensity, 0.4f, day);
        }
    }
}
