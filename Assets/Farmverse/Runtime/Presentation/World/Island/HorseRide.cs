#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// The farmer's horse (gameplay design §8). Never automatic: the Ride button is a toggle.
    /// <list type="bullet">
    /// <item>Ride on: the horse appears and the farmer mounts; the stick steers it and sets its pace,
    /// letting go stops it with the farmer still in the saddle. Ride off: back on foot.</item>
    /// <item>In the saddle, tap a far-off spot and it rides there on its own; pushing the stick takes the reins back.
    /// "Ride to the raid" mounts for that trip only.</item>
    /// </list>
    /// The horse is only a ride (no stable, no feeding yet). Work needs feet on the ground.
    /// </summary>
    [RequireComponent(typeof(PlayerAvatar))]
    public sealed class HorseRide : MonoBehaviour
    {
        [SerializeField] private float rideSpeed = 12f;
        [SerializeField, Tooltip("Where the farmer sits, relative to the horse's feet (m, before the horse's scale).")] private Vector3 seat = new Vector3(0f, 0.85f, -0.1f);
        [SerializeField] private float turnSpeed = 6f;

        public GameObject? HorsePrefab { get; set; }
        public VirtualJoystick? Stick { get; set; }
        public IslandGrid? Grid { get; set; }
        public System.Func<int, bool>? MayCross { get; set; }
        /// <summary>Whether the horse can come now (a fed stable); asked before every mount.</summary>
        public System.Func<bool>? CanRide { get; set; }
        /// <summary>The horse came (the stable pays for the ride).</summary>
        public System.Action? Mounted { get; set; }
        /// <summary>The horse didn't come (no stable, or it's hungry).</summary>
        public System.Action? Refused { get; set; }
        /// <summary>Getting off: someone may take the horse over (it walks home to its stable). False: it vanishes.</summary>
        public System.Func<Transform, bool>? KeepHorse { get; set; }
        /// <summary>Tells the sandbox where the farmer is (the camera follows the farmer, who rides along).</summary>
        public bool Riding => _horse != null;
        public bool HasDestination => _route.Count > 0;

        private PlayerAvatar _avatar = null!;
        private CharacterMotion? _motion;
        private Transform? _horse;
        private readonly List<Vector3> _route = new List<Vector3>();
        private int _next;
        private bool _steering, _stayOn;

        private void Awake()
        {
            _avatar = GetComponent<PlayerAvatar>();
            _motion = GetComponent<CharacterMotion>();
        }

        /// <summary>Ride to a spot; the route keeps to dry land and goes round buildings.</summary>
        public void RideTo(Vector3 where)
        {
            if (HorsePrefab == null || Grid == null) return;
            _stayOn = Riding;   // already in the saddle (the Ride toggle): stay on when we get there
            if (!Riding) Mount();
            if (!Riding) return;
            _steering = false;
            GridPath.Find(Grid, _horse!.position, where, MayCross, _route);
            _next = 0;
        }

        public void Mount()
        {
            if (HorsePrefab == null || Riding) return;
            if (CanRide != null && !CanRide()) { Refused?.Invoke(); return; }
            Mounted?.Invoke();
            _steering = true;   // the stick steers the horse until the Ride button gets you down
            var at = transform.position;
            _horse = Instantiate(HorsePrefab, at, transform.rotation).transform;
            _horse.name = "Horse";
            foreach (var c in _horse.GetComponentsInChildren<Collider>()) c.enabled = false;

            _avatar.Riding = true;
            if (_motion != null) { _motion.Pose("Sit", 0.3f); _motion.enabled = false; }
            // seat the farmer before the horse pops in, so they grow in together (the horse's root is unscaled)
            transform.SetParent(_horse, false);
            transform.localPosition = seat;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            Magic.Appear(_horse.gameObject, 0.35f, new Color(1f, 0.9f, 0.6f));
            IslandAudio.Play("neigh", null, 0.7f, 0.05f, 1f);
        }

        public void Dismount()
        {
            if (_horse == null) return;
            var horse = _horse;
            _horse = null;
            _route.Clear();
            _steering = false;
            if (Stick != null) Stick.StopMode = false;
            transform.SetParent(null, true);
            transform.localScale = Vector3.one;
            transform.rotation = Quaternion.Euler(0, horse.eulerAngles.y, 0);
            var side = horse.position + horse.right * 1.6f;
            _avatar.Riding = false;
            _avatar.PlaceAt(side);
            if (_motion != null) { _motion.enabled = true; _motion.Release(); }
            if (KeepHorse == null || !KeepHorse(horse)) Magic.Vanish(horse.gameObject, true, new Color(1f, 0.9f, 0.6f));
        }

        private void Update()
        {
            var stick = Stick;
            // no riding unless asked: the Ride button (a toggle) puts you on the horse and takes you off
            if (!Riding) return;
            var horse = _horse!;
            if (_motion != null && _motion.enabled) _motion.enabled = false;
            if (HasDestination)
            {
                // the stick takes the reins back: the horse stops riding itself and you steer
                if (stick != null && stick.Engaged && stick.Value.sqrMagnitude > 0.04f) { _route.Clear(); _steering = true; return; }
                var target = _route[_next];
                var flat = new Vector3(target.x - horse.position.x, 0, target.z - horse.position.z);
                var step = rideSpeed * Time.deltaTime;
                if (flat.magnitude <= Mathf.Max(0.3f, step))
                {
                    _next++;
                    if (_next >= _route.Count)
                    {
                        if (_stayOn) { _route.Clear(); _steering = true; if (Stick != null) Stick.StopMode = false; }
                        else Dismount();
                        return;
                    }
                }
                Move(horse, flat.normalized, step);
                return;
            }
            if (_steering)
            {
                if (stick == null || !stick.Engaged) return;   // stick let go: the horse stands, you stay on
                var view = Camera.main != null ? Camera.main.transform : null;
                var fwd = view != null ? Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized : Vector3.forward;
                var right = Vector3.Cross(Vector3.up, fwd);
                var v = stick.Value;
                var dir = (right * v.x + fwd * v.y);
                if (dir.sqrMagnitude < 0.01f) return;
                Move(horse, dir.normalized, rideSpeed * Mathf.Clamp01(v.magnitude) * Time.deltaTime);
                return;
            }
            Dismount();
        }

        private void Move(Transform horse, Vector3 dir, float step)
        {
            var next = horse.position + dir * step;
            // the horse doesn't swim or walk through buildings
            if (Grid != null)
            {
                var cell = Grid.Data.CellOf(next + dir * 1.2f);
                var owner = Grid.OwnerAt(cell.x, cell.y);
                if (!Grid.Dry(cell.x, cell.y) || (owner != 0 && !(MayCross?.Invoke(owner) ?? false)))
                {
                    if (HasDestination) { _next = Mathf.Min(_next + 1, _route.Count - 1); }
                    horse.rotation = Quaternion.Slerp(horse.rotation, Quaternion.LookRotation(dir, Vector3.up), 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
                    return;
                }
            }
            next.y = GroundHeight.At(next.x, next.z, horse.position.y);
            horse.position = next;
            horse.rotation = Quaternion.Slerp(horse.rotation, Quaternion.LookRotation(dir, Vector3.up), 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
        }
    }
}
