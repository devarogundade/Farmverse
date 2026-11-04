#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// The farmer the player walks around the island: the thumb-stick (or WASD/arrows) moves them
    /// relative to the camera, pushed gently = walk, pushed to the rim = run. They walk through
    /// buildings, trees and rocks (standing in a building is how they use it) and stop at the water's edge.
    /// <see cref="CharacterMotion"/> on the same object plays Idle/Walk/Run from the speed.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerAvatar : MonoBehaviour
    {
        [SerializeField] private float walkSpeed = 3.8f;
        [SerializeField] private float runSpeed = 8f;
        [SerializeField, Tooltip("Stick deflection at which walking becomes running.")] private float runAt = 0.85f;
        [SerializeField, Tooltip("Ground below this height is water: the farmer stops at the shore.")] private float shoreHeight = 0.35f;
        [SerializeField] private float turnSpeed = 12f;

        public VirtualJoystick? Joystick { get; set; }
        public Transform? View { get; set; }
        /// <summary>Off while a building is being placed: the stick and keys belong to the placer.</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>
        /// Size the collider to the body actually on screen (the prefab's scale sets the character's
        /// height), so a taller farmer still fits through gaps their size and steps up kerbs.
        /// CharacterController values are in local units, hence the division by the scale.
        /// </summary>
        public static void FitController(CharacterController cc)
        {
            var t = cc.transform;
            var height = 1.7f;
            var found = false;
            var bounds = new Bounds(t.position, Vector3.zero);
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!found) bounds = r.bounds; else bounds.Encapsulate(r.bounds);
                found = true;
            }
            if (found && bounds.size.y > 0.3f) height = bounds.size.y;
            var s = Mathf.Max(0.01f, t.lossyScale.y);
            cc.height = height * 0.95f / s;
            cc.radius = Mathf.Clamp(height * 0.2f, 0.3f, 0.6f) / s;
            cc.center = new Vector3(0, height * 0.5f / s, 0);
            cc.stepOffset = Mathf.Min(0.5f, height * 0.25f) / s;
            cc.slopeLimit = 50f;
        }

        /// <summary>On horseback: <see cref="HorseRide"/> moves the farmer, not this.</summary>
        public bool Riding { get; set; }

        /// <summary>The player is steering the farmer this frame (stick or keys).</summary>
        public bool Steering { get; private set; }

        private CharacterController _cc = null!;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cc.enabled = false;   // kept for its size (fitted to the body); it doesn't collide
        }

        private void Update()
        {
            if (Riding) return;
            var input = InputEnabled && Joystick != null ? Joystick.Value : Vector2.zero;
            var kb = Keyboard.current;
            if (InputEnabled && kb != null)
            {
                var k = Vector2.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) k.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) k.y -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) k.x += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) k.x -= 1;
                if (k != Vector2.zero) input = k.normalized * (kb.leftShiftKey.isPressed ? 1f : 0.7f);
            }
            var view = View != null ? View : Camera.main != null ? Camera.main.transform : null;
            var fwd = view != null ? Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            var amount = Mathf.Clamp01(input.magnitude);
            Steering = amount > 0.05f;
            var dir = amount > 0.05f ? (right * input.x + fwd * input.y).normalized : Vector3.zero;
            var speed = amount < 0.05f ? 0f : amount >= runAt ? runSpeed : Mathf.Lerp(walkSpeed * 0.55f, walkSpeed, amount / runAt);
            var move = dir * speed;
            if (move != Vector3.zero && Water(transform.position + dir * 0.9f)) move = Vector3.zero;

            // the farmer walks through everything (buildings, trees, crops): standing in a building is
            // how they hand goods over; only the sea stops them. Feet stay on the terrain.
            var pos = transform.position + move * Time.deltaTime;
            pos.y = GroundHeight.At(pos.x, pos.z, pos.y);
            transform.position = pos;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir, Vector3.up), 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
        }

        /// <summary>True when the island ground at this spot is under the sea (or there is no ground).</summary>
        private bool Water(Vector3 at)
        {
            return !GroundHeight.TryAt(at.x, at.z, out var y) || y < shoreHeight;
        }

        /// <summary>Stand the farmer on the ground at a spot (spawning, resetting).</summary>
        public void PlaceAt(Vector3 p)
        {
            if (GroundHeight.TryAt(p.x, p.z, out var y)) p.y = y;
            transform.position = p;
        }
    }
}
