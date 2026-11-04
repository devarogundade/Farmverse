#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// A vehicle on a line: today the order truck on the causeway (docs: Island by Chapter): the road runs from
    /// the farm, past the depot and out over the sea to the edge of the map. When the economy's truck is due
    /// (<see cref="Present"/>) it drives in from the horizon, pulls up at the depot and waits there while its
    /// crates are loaded; when it leaves it turns round (a train would back out) and is gone until the next.
    /// The first car leads coming in; any others follow at fixed gaps along the same line.
    /// </summary>
    public sealed class IslandTrain : MonoBehaviour
    {
        [SerializeField] private IslandData? data;
        [SerializeField] private List<Transform> cars = new List<Transform>();   // engine first
        [SerializeField] private List<float> carLengths = new List<float>();     // metres, same order
        [SerializeField] private float gap = 0.6f;
        [SerializeField] private float cruiseSpeed = 11f;     // m/s
        [SerializeField] private float accel = 1.8f;
        [SerializeField] private float stationIndex = 0f;     // metre along the line where the engine stops
        [SerializeField] private float railTop = 0.5f;        // height of the rail heads above the path
        [SerializeField] private ParticleSystem? smoke;
        [SerializeField, Tooltip("A road vehicle turns round to leave instead of backing out (a train backs out).")] private bool turnsAround;

        private enum State { Away, Arriving, AtStation, Leaving }

        private State _state = State.Away;
        private float _s;          // the engine's position along the line (m from the buffer stop)
        private float _v;          // speed towards the buffer (negative while backing out)
        private float _length;
        private float _trainLength;
        private Vector3[] _p = new Vector3[0];

        public Transform? Engine => cars.Count > 0 ? cars[0] : null;

        /// <summary>The economy has a train at the station (set every frame by the island).</summary>
        public bool Present { get; set; }

        /// <summary>Moving (for its sound).</summary>
        public bool Moving => _state is State.Arriving or State.Leaving && Mathf.Abs(_v) > 0.3f;

        /// <summary>On the island's part of the line, for the camera to follow.</summary>
        public bool Visible => _state != State.Away;

        private void Start()
        {
            _p = data != null ? data.railPath : new Vector3[0];
            if (data != null && data.stationIndex > 0) stationIndex = data.stationIndex;
            _length = _p.Length;   // one point per metre
            _trainLength = 0;
            for (int i = 0; i < cars.Count; i++) _trainLength += (i < carLengths.Count ? carLengths[i] : 6f) + gap;
            _s = OffMap;
            Show(false);
        }

        /// <summary>Where the train waits out of sight: beyond the end of the line, cars and all.</summary>
        private float OffMap => _length + _trainLength + 10f;

        private void Update()
        {
            if (_p.Length < 4 || cars.Count == 0) return;
            float dt = Time.deltaTime;
            float stop = Mathf.Clamp(stationIndex, 2f, _length - 2f);
            switch (_state)
            {
                case State.Away:
                    if (!Present) break;
                    _state = State.Arriving;
                    _s = OffMap;
                    _v = cruiseSpeed;
                    Show(true);
                    break;
                case State.Arriving:
                {
                    // brake smoothly so the engine stops at the station
                    float toStop = _s - stop;
                    float target = Mathf.Min(cruiseSpeed, Mathf.Sqrt(2f * accel * Mathf.Max(toStop, 0f)) + 0.25f);
                    _v = Mathf.MoveTowards(_v, target, accel * 2f * dt);
                    _s -= _v * dt;
                    if (_s <= stop + 0.05f) { _s = stop; _v = 0f; _state = State.AtStation; }
                    if (!Present && _state == State.Arriving && _v < 2f) _state = State.Leaving;
                    break;
                }
                case State.AtStation:
                    if (!Present) _state = State.Leaving;
                    break;
                case State.Leaving:
                    // back out the way it came, gathering speed, until it's gone over the horizon
                    _v = Mathf.MoveTowards(_v, -cruiseSpeed, accel * dt);
                    _s -= _v * dt;
                    if (_s >= OffMap) { _s = OffMap; _v = 0f; _state = State.Away; Show(false); }
                    else if (Present && _v > -2f) _state = State.Arriving;
                    break;
            }
            if (_state == State.Away) return;

            float s = _s;
            for (int i = 0; i < cars.Count; i++)
            {
                float half = (i < carLengths.Count ? carLengths[i] : 6f) * 0.5f;
                if (i > 0) s += half;
                // a car rests on two bogies: front (towards the buffer) and back points on the track
                var front = At(s - half * 0.7f);
                var back = At(s + half * 0.7f);
                var c = cars[i];
                if (c == null) continue;
                var mid = (front + back) * 0.5f + Vector3.up * railTop;
                var dir = front - back;
                if (turnsAround && _state == State.Leaving) dir = -dir;
                if (dir.sqrMagnitude > 1e-4f) c.SetPositionAndRotation(mid, Quaternion.LookRotation(dir.normalized, Vector3.up));
                s += half + gap;
            }
            if (smoke != null)
            {
                var em = smoke.emission;
                float speed = Mathf.Abs(_v);
                em.rateOverTime = (_state == State.AtStation ? 1.5f : 3f) + speed * 1.2f + (speed < cruiseSpeed * 0.9f && _state != State.AtStation ? 6f : 0f);
            }
        }

        private void Show(bool on)
        {
            foreach (var c in cars) if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        /// <summary>A point on the line; past its end the track carries straight on out to sea.</summary>
        private Vector3 At(float s)
        {
            if (s <= 0f) return _p[0];
            if (s >= _length - 1f)
            {
                var last = _p[_p.Length - 1];
                var dir = (last - _p[_p.Length - 2]).normalized;
                return last + dir * (s - (_length - 1f));
            }
            int i = Mathf.FloorToInt(s);
            return Vector3.Lerp(_p[i], _p[i + 1], s - i);
        }
    }
}
