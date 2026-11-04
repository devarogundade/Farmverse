#nullable enable
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>A moored boat rocking gently on the swell.</summary>
    public sealed class IslandBob : MonoBehaviour
    {
        [SerializeField] private float lift = 0.08f;
        [SerializeField] private float roll = 2.2f;

        private Vector3 _rest;
        private Quaternion _turn;
        private float _phase;

        private void Start()
        {
            _rest = transform.localPosition;
            _turn = transform.localRotation;
            _phase = (transform.position.x * 0.37f + transform.position.z * 0.21f) % 6.28f;
        }

        private void Update()
        {
            float t = Time.time * 0.9f + _phase;
            transform.localPosition = _rest + Vector3.up * (Mathf.Sin(t) * lift);
            transform.localRotation = _turn * Quaternion.Euler(Mathf.Sin(t * 0.8f + 1f) * roll * 0.5f, 0, Mathf.Sin(t * 1.1f) * roll);
        }
    }
}
