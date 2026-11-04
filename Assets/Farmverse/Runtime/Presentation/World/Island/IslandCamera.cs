#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// The island camera: looks down at a fixed angle around a pivot on the ground.
    /// Drag (one finger / left mouse) pans, pinch or wheel zooms, WASD/arrows pan, Q/E turn.
    /// With a player avatar the drag and the keys walk the farmer instead and the camera follows them.
    /// The pivot stays over the island. Panning can be switched off while a building is dragged.
    /// </summary>
    public sealed class IslandCamera : MonoBehaviour
    {
        [SerializeField] private float pitch = 50f;
        [SerializeField] private float yaw = 45f;
        [SerializeField] private float distance = 70f;
        [SerializeField] private float minDistance = 18f;
        [SerializeField] private float maxDistance = 320f;
        [SerializeField] private float islandRadius = 300f;
        [SerializeField] private float keyPanSpeed = 60f;

        public Vector3 Pivot = Vector3.zero;
        public bool PanEnabled { get; set; } = true;
        /// <summary>False when one-finger drags drive the player (the thumb-stick) instead of panning.</summary>
        public bool DragPans { get; set; } = true;
        /// <summary>False when WASD/arrows walk the player instead of panning.</summary>
        public bool KeysPan { get; set; } = true;
        public bool PointerOverUi { get; set; }

        private Vector2 _lastPointer;
        private bool _dragging;
        private float _lastPinch;

        private void OnEnable() => EnhancedTouchSupport.Enable();

        private void LateUpdate()
        {
            var cam = GetComponent<Camera>();
            var beforeTouch = Pivot;
            HandleTouch(cam);
            if (Pivot != beforeTouch) Follow = null;
            var before = Pivot;
            HandleMouseAndKeys(cam);
            if (Pivot != before) Follow = null;
            if (Follow != null) Pivot = Vector3.Lerp(Pivot, new Vector3(Follow.position.x, 0, Follow.position.z), 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            Pivot = Vector3.ClampMagnitude(new Vector3(Pivot.x, 0, Pivot.z), islandRadius);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            var rot = Quaternion.Euler(pitch, yaw, 0);
            _boost = Mathf.Lerp(_boost, ZoomBoost, 1f - Mathf.Exp(-2.2f * Time.unscaledDeltaTime));
            transform.SetPositionAndRotation(Pivot - rot * Vector3.forward * distance * _boost, rot);
        }

        private Vector3 GroundDelta(Camera cam, Vector2 from, Vector2 to)
        {
            var plane = new Plane(Vector3.up, Vector3.zero);
            var a = cam.ScreenPointToRay(from); var b = cam.ScreenPointToRay(to);
            if (plane.Raycast(a, out var ea) && plane.Raycast(b, out var eb)) return a.GetPoint(ea) - b.GetPoint(eb);
            return Vector3.zero;
        }

        private void HandleTouch(Camera cam)
        {
            var touches = Touch.activeTouches;
            if (touches.Count == 2)
            {
                float d = Vector2.Distance(touches[0].screenPosition, touches[1].screenPosition);
                if (_lastPinch > 0) distance *= _lastPinch / Mathf.Max(d, 1f);
                _lastPinch = d;
                return;
            }
            _lastPinch = 0;
            if (touches.Count == 1 && PanEnabled && DragPans && !PointerOverUi)
            {
                var t = touches[0];
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Moved)
                    Pivot += GroundDelta(cam, t.screenPosition, t.screenPosition - t.delta) * -1f;
            }
        }

        private void HandleMouseAndKeys(Camera cam)
        {
            var mouse = Mouse.current;
            if (mouse != null && Touch.activeTouches.Count == 0)
            {
                var pos = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame) { _dragging = PanEnabled && DragPans && !PointerOverUi; _lastPointer = pos; }
                if (mouse.leftButton.wasReleasedThisFrame) _dragging = false;
                if (_dragging && mouse.leftButton.isPressed)
                {
                    Pivot += GroundDelta(cam, _lastPointer, pos);
                    _lastPointer = pos;
                }
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f && !PointerOverUi)
                {
                    // one notch is 120 on some platforms and 1 on others; several notches can land in one frame
                    float notches = Mathf.Clamp(Mathf.Abs(wheel) >= 60f ? wheel / 120f : wheel, -10f, 10f);
                    distance *= Mathf.Pow(1.14f, -notches);
                }
            }
            var kb = Keyboard.current;
            if (kb == null) return;
            var fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            var move = Vector3.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move += fwd;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move -= fwd;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move += right;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move -= right;
            if (KeysPan) Pivot += move * keyPanSpeed * (distance / 70f) * Time.unscaledDeltaTime;
            if (kb.qKey.isPressed) yaw += 60f * Time.unscaledDeltaTime;
            if (kb.eKey.isPressed) yaw -= 60f * Time.unscaledDeltaTime;
            if (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed) distance *= 1f - 1.2f * Time.unscaledDeltaTime;
            if (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed) distance *= 1f + 1.2f * Time.unscaledDeltaTime;
        }

        public void FocusOn(Vector3 p) => Pivot = new Vector3(p.x, 0, p.z);

        /// <summary>How far from the middle the view may wander (grows with the island's chapter).</summary>
        public float Radius { get => islandRadius; set => islandRadius = Mathf.Max(20f, value); }

        /// <summary>How far the camera sits from its pivot (clamped to the zoom range).</summary>
        public float Distance { get => distance; set => distance = Mathf.Clamp(value, minDistance, maxDistance); }

        /// <summary>Pull the view back by this factor (1 = the player's own zoom), eased in and out: wider while riding.</summary>
        public float ZoomBoost { get; set; } = 1f;
        private float _boost = 1f;

        /// <summary>Keeps the view on a moving thing (the train) until the player pans.</summary>
        public Transform? Follow { get; set; }
    }
}
