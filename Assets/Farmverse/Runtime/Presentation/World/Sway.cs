#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World
{
    /// <summary>Crops and trees move in the wind: a slow lean that differs per plant, so a field ripples.</summary>
    public sealed class Sway : MonoBehaviour
    {
        [SerializeField] private float degrees = 4f;
        [SerializeField] private float speed = 0.6f;

        private Quaternion _rest;
        private float _phase;

        public bool ReduceMotion { get; set; }

        private Transform _model = null!;

        private void Awake()
        {
            _model = StagedVisual.ModelOf(transform);
            _rest = _model.localRotation;
        }

        private void Update()
        {
            if (ReduceMotion) return;
            if (_phase == 0f)
            {
                var p = transform.position;
                _phase = 1f + Mathf.Abs(p.x * 0.37f + p.z * 0.23f);
            }
            var t = Time.time * speed + _phase;
            var lean = (Mathf.PerlinNoise(t, _phase) - 0.5f) * 2f * degrees;
            var side = (Mathf.PerlinNoise(_phase, t) - 0.5f) * 2f * degrees * 0.5f;
            _model.localRotation = _rest * Quaternion.Euler(lean, 0, side);
        }
    }
}
