#nullable enable
using UnityEngine;

namespace Farmverse.Presentation.World
{
    /// <summary>
    /// Animates a rigged person from how they actually move: standing still plays Idle (with the odd
    /// look around), moving plays Walk, moving fast plays Run, with the walk sped up or slowed down
    /// to match the ground speed so feet don't slide. Whatever moves the object (the player's stick,
    /// a villager's route) needs to know nothing about animation. One-off actions (Wave, Cheer, Dig…)
    /// are played with <see cref="Play"/> and return to Idle on their own.
    /// The states are the ones Farmverse / Art / Build Characters puts in Villager.controller.
    /// </summary>
    public sealed class CharacterMotion : MonoBehaviour
    {
        [SerializeField] private float walkFrom = 0.3f;
        [SerializeField] private float runFrom = 4.8f;
        /// <summary>The speed (m/s) from which Run plays instead of Walk (animals set their own: a cow runs slower than a person).</summary>
        public float RunFrom { get => runFrom; set => runFrom = Mathf.Max(walkFrom + 0.1f, value); }
        [SerializeField, Tooltip("Ground speed the walk cycle was made for, m/s.")] private float walkCycleSpeed = 2.4f;
        [SerializeField, Tooltip("Ground speed the run cycle was made for, m/s.")] private float runCycleSpeed = 6f;

        private Animator? _anim;
        private Vector3 _last;
        private float _speed;
        private string _state = string.Empty;
        private float _still;
        private float _fidgetAt;
        private float _actionUntil;
        private string? _held;

        private void Awake()
        {
            _anim = GetComponentInChildren<Animator>();
            if (_anim != null && _anim.runtimeAnimatorController == null)
                Debug.LogWarning($"[CharacterMotion] {name} ({(transform.parent != null ? transform.parent.name : "root")}) has an animator without a controller", this);
            _last = transform.position;
            _fidgetAt = Random.Range(6f, 12f);
        }

        /// <summary>The body changed (a costume put on or taken off): drive the animator that's showing now.</summary>
        public void Rebind()
        {
            _anim = GetComponentInChildren<Animator>();
            _state = string.Empty;
            _held = null;
            _actionUntil = 0f;
        }

        /// <summary>Whether this character's controller has <paramref name="state"/> (a horse has no Wave).</summary>
        public bool Has(string state) => _anim != null && _anim.runtimeAnimatorController != null && _anim.HasState(0, Animator.StringToHash(state));

        /// <summary>Stay in a state (sitting, working, lying beaten) until <see cref="Release"/> or until they walk off.</summary>
        public void Hold(string state)
        {
            if (_anim == null || !Has(state) || _held == state) return;
            _held = state;
            _anim.speed = 1f;
            _anim.CrossFadeInFixedTime(state, 0.2f);
            _state = state;
            _actionUntil = float.MaxValue;
        }

        /// <summary>Freeze in one frame of a state (the rider in the saddle: no sitting clip exists, so a crouch frame stands in).</summary>
        public void Pose(string state, float at)
        {
            if (_anim == null || !Has(state)) return;
            _held = state;
            _state = state;
            _actionUntil = float.MaxValue;
            _anim.speed = 1f;
            _anim.Play(state, 0, at);
            _anim.Update(0f);
            _anim.speed = 0f;
        }

        public void Release()
        {
            if (_anim != null) _anim.speed = 1f;
            if (_held == null) return;
            _held = null;
            _actionUntil = 0f;
        }

        /// <summary>Walking with something in the arms (or pushing a barrow): the carry walk instead of the plain one.</summary>
        public bool Carrying { get; set; }

        /// <summary>Move with this state instead of Walk/Run (a duck swims, a pigeon flies).</summary>
        public string? WalkAs { get; set; }
        /// <summary>Stand with this state instead of Idle (a duck bobbing on the water).</summary>
        public string? IdleAs { get; set; }

        private int _dances = -1;

        /// <summary>One of the packs' dances, picked at random (Dance1..DanceN in the controller).</summary>
        public void Dance(float seconds)
        {
            if (_dances < 0) { _dances = 0; while (Has("Dance" + (_dances + 1))) _dances++; }
            if (_dances == 0) { Play("Cheer", seconds); return; }
            Play("Dance" + Random.Range(1, _dances + 1), seconds);
        }

        private static readonly string[] Fidgets = { "LookAround", "LookAround2", "LookAround3", "Relax", "Rare", "Gesture", "Stretch", "Eat" };

        public void Play(string state, float seconds = 2.5f)
        {
            if (_anim == null || !Has(state)) return;
            _anim.speed = 1f;
            _anim.CrossFadeInFixedTime(state, 0.15f);
            _state = state;
            _actionUntil = Time.time + seconds;
        }

        private void LateUpdate()
        {
            if (_anim == null) return;
            var p = transform.position;
            var v = (p - _last) / Mathf.Max(Time.deltaTime, 1e-4f);
            v.y = 0;
            _last = p;
            _speed = Mathf.Lerp(_speed, v.magnitude, 1f - Mathf.Exp(-12f * Time.deltaTime));

            var want = _speed > runFrom ? "Run" : _speed > walkFrom ? "Walk" : "Idle";
            if (want != "Idle" && WalkAs != null && Has(WalkAs)) want = WalkAs;
            else if (want == "Idle" && IdleAs != null && Has(IdleAs) && Time.time >= _actionUntil) want = IdleAs;
            else if (want != "Idle")
            {
                // the way they move against the way they face: forward, back, or sideways (strafe) clips
                var local = transform.InverseTransformDirection(v);
                string dir = Mathf.Abs(local.x) > Mathf.Abs(local.z) * 1.2f ? (local.x > 0 ? "Right" : "Left") : local.z < -0.1f ? "Back" : "";
                if (dir != "" && Has(want + dir)) want += dir;
                else if (dir == "" && want == "Walk" && Carrying && Has("Carry")) want = "Carry";
            }
            if (want == "Idle" || want == IdleAs)
            {
                if (Time.time < _actionUntil) return;   // a wave or a look around finishes first
                _still += Time.deltaTime;
                if (_still > _fidgetAt)
                {
                    _still = 0f;
                    _fidgetAt = Random.Range(8f, 16f);
                    // any of the idle extras this character has (people: three look-arounds, breathing, the rare idle; animals: gesture, stretch, a nibble)
                    string pick = "LookAround";
                    for (var tries = 0; tries < 6; tries++) { var f = Fidgets[Random.Range(0, Fidgets.Length)]; if (Has(f)) { pick = f; break; } }
                    Play(pick, 3f);
                    return;
                }
            }
            else
            {
                _still = 0f;
                _actionUntil = 0f;
                _held = null;
            }
            if (want != _state)
            {
                _anim.CrossFadeInFixedTime(want, want == "Idle" ? 0.25f : 0.15f);
                _state = want;
            }
            _anim.speed = want.StartsWith("Walk") || want == "Carry" ? Mathf.Clamp(_speed / walkCycleSpeed, 0.6f, 1.6f)
                        : want.StartsWith("Run") ? Mathf.Clamp(_speed / runCycleSpeed, 0.7f, 1.4f) : 1f;
        }
    }
}
