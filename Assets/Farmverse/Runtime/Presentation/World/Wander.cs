#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World
{
    /// <summary>
    /// Animals live in their pen: they amble to a nearby spot, stop, peck or graze
    /// (a little bob), and wander again. Animals in a pen waiting to be fed lie down;
    /// ones with eggs or milk ready stay put. The world places the object at the
    /// animal's spot in the pen; this moves the model around it. Walking, they waddle (a side-to-side
    /// roll); stopped, they dip their head now and then to peck or graze (a forward tip).
    /// </summary>
    public sealed class Wander : MonoBehaviour, IVisualStateReceiver
    {
        [SerializeField] private float radius = 0.55f;
        [SerializeField] private float speed = 0.45f;

        private Vector3 _offset;
        private Vector3 _goal;
        private float _wait;
        private bool _hungry;
        private bool _ready;
        private bool _reduceMotion;
        private Transform? _model;
        private float _seed;
        private Quaternion _facing = Quaternion.identity;
        private bool _moving;
        private float _lift;

        private void Awake()
        {
            _model = StagedVisual.ModelOf(transform);
            _facing = _model.localRotation;
            _seed = UnityEngine.Random.value * 10f;
            _wait = UnityEngine.Random.Range(0f, 3f);
        }

        public void OnVisualState(WorldVisual visual, bool reduceMotion)
        {
            _reduceMotion = reduceMotion;
            _hungry = (visual.Flags & VisualFlags.Hungry) != 0;
            _ready = (visual.Flags & VisualFlags.Collectable) != 0;
        }

        private void Update()
        {
            if (_model == null) return;
            // lying down when hungry; a grazing bob otherwise
            var lie = _hungry ? -0.1f : 0f;
            var bob = _reduceMotion || _hungry || _wait <= 0f ? 0f : Mathf.Max(0f, Mathf.Sin(Time.time * 5f + _seed)) * 0.025f;
            _moving = false;
            if (!_reduceMotion && !_hungry && !_ready)
            {
                if (_wait > 0f) _wait -= Time.deltaTime;
                else
                {
                    var to = _goal - _offset;
                    if (to.magnitude < 0.03f)
                    {
                        _wait = UnityEngine.Random.Range(1.5f, 5f);
                        var r = UnityEngine.Random.insideUnitCircle * radius;
                        _goal = new Vector3(r.x, 0, r.y);
                    }
                    else
                    {
                        _offset += to.normalized * Mathf.Min(to.magnitude, speed * Time.deltaTime);
                        var face = Quaternion.LookRotation(to.normalized, Vector3.up);
                        _facing = Quaternion.Slerp(_facing, face, Time.deltaTime * 4f);
                        _moving = true;
                    }
                }
            }
            // the spot stands on the terrain; as the animal ambles off it, follow the slope
            if (_moving || _lift == 0f)
            {
                var at = transform.TransformPoint(_offset);
                var scale = Mathf.Max(0.01f, transform.lossyScale.y);
                _lift = GroundHeight.TryAt(at.x, at.z, out var y) ? Mathf.Clamp(y - transform.position.y - GroundHeight.Sink, -0.6f, 0.6f) / scale : 0.0001f;
            }
            _model.localPosition = _offset + new Vector3(0, lie + bob + _lift, 0);
            // waddle while walking, a peck/graze tip while standing
            var roll = _moving && !_reduceMotion ? Mathf.Sin(Time.time * 11f + _seed) * 7f : 0f;
            var peck = !_moving && !_reduceMotion && !_hungry ? Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Time.time * 1.7f + _seed * 3f)), 6f) * 18f : 0f;
            _model.localRotation = _facing * Quaternion.Euler(peck, 0f, roll);
        }
    }
}
