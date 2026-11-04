#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// Visitors (docs: Vehicles): from the valley chapter on, cars and camper vans drive in over the causeway,
    /// park a while by the depot and drive off again. How many are about at once grows with the island's level.
    /// Purely scenery for now: they keep to the right of the road and never block the order truck's stop.
    /// </summary>
    public sealed class IslandTraffic : MonoBehaviour
    {
        [SerializeField] private IslandData? data;
        [SerializeField] private GameObject[] carPrefabs = new GameObject[0];
        [SerializeField] private float speed = 8f;
        [SerializeField] private float lane = 1.7f;          // metres right of the road's centre
        [SerializeField] private float parkBeyond = 14f;     // metres past the depot stop where they pull in
        [SerializeField, Tooltip("Island level visitors start coming at.")] private int fromLevel = 26;

        private sealed class Car
        {
            public Transform Body = null!;
            public float S, Stop, Wait;
            public bool Leaving;
            public float Length;
        }

        private readonly List<Car> _cars = new List<Car>();
        private Vector3[] _p = new Vector3[0];
        private float _length, _stopAt, _next;
        private IslandEconomyHost? _host;

        private void Start()
        {
            _p = data != null ? data.railPath : new Vector3[0];
            _length = _p.Length;
            _stopAt = data != null ? data.stationIndex : 20f;
            _next = Random.Range(8f, 20f);
            _host = FindAnyObjectByType<IslandEconomyHost>();
        }

        private int Level => _host != null ? _host.Economy.BestLevel : 0;

        /// <summary>Cars about at once: one from the start level, one more every 15 levels, at most four.</summary>
        private int Wanted => Level < fromLevel ? 0 : Mathf.Min(4, 1 + (Level - fromLevel) / 15);

        private void Update()
        {
            if (_p.Length < 4 || carPrefabs.Length == 0) return;
            var dt = Time.deltaTime;
            _next -= dt;
            if (_next <= 0f && _cars.Count < Wanted) { Spawn(); _next = Random.Range(25f, 60f); }
            for (var i = _cars.Count - 1; i >= 0; i--)
            {
                var c = _cars[i];
                if (!c.Leaving)
                {
                    if (c.S > c.Stop) c.S = Mathf.Max(c.Stop, c.S - speed * dt);
                    else
                    {
                        c.Wait -= dt;
                        if (c.Wait <= 0f) c.Leaving = true;
                    }
                }
                else
                {
                    c.S += speed * dt;
                    if (c.S > _length + c.Length + 12f) { Destroy(c.Body.gameObject); _cars.RemoveAt(i); continue; }
                }
                Place(c);
            }
        }

        private void Spawn()
        {
            var pf = carPrefabs[Random.Range(0, carPrefabs.Length)];
            if (pf == null) return;
            var go = Instantiate(pf, transform);
            foreach (var col in go.GetComponentsInChildren<Collider>()) col.enabled = false;
            // fit to a car's length, wheels on the origin
            var b = new Bounds(go.transform.position, Vector3.zero); var any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            var length = Mathf.Clamp(b.size.z, 3.5f, 7f);
            go.transform.localScale *= length / Mathf.Max(b.size.z, 0.01f);
            var c = new Car
            {
                Body = go.transform, Length = length, S = _length + length + 10f,
                Stop = Mathf.Clamp(_stopAt + parkBeyond + _cars.Count * 6f, 2f, _length - 4f),
                Wait = Random.Range(20f, 45f),
            };
            _cars.Add(c);
            Place(c);
        }

        private void Place(Car c)
        {
            var front = At(c.S - c.Length * 0.35f);
            var back = At(c.S + c.Length * 0.35f);
            var dir = front - back;
            if (dir.sqrMagnitude < 1e-4f) return;
            dir.Normalize();
            var side = Vector3.Cross(Vector3.up, dir);   // right of the way in
            var mid = (front + back) * 0.5f + side * lane * (c.Leaving ? -1f : 1f) + Vector3.up * 0.08f;
            c.Body.SetPositionAndRotation(mid, Quaternion.LookRotation(c.Leaving ? -dir : dir, Vector3.up));
        }

        private Vector3 At(float s)
        {
            if (s <= 0f) return _p[0];
            if (s >= _length - 1f)
            {
                var last = _p[_p.Length - 1];
                var d = (last - _p[_p.Length - 2]).normalized;
                return last + d * (s - (_length - 1f));
            }
            var i = Mathf.FloorToInt(s);
            return Vector3.Lerp(_p[i], _p[i + 1], s - i);
        }
    }
}
