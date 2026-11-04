#nullable enable
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// A fixed thumb-stick at the bottom middle of the screen. It never moves: press on its ring and
    /// drag, and the knob follows the finger up to the rim (pushed gently = walk, to the rim = run).
    /// Only a press on the stick walks the farmer; a drag anywhere else is the camera's, so players
    /// can pan around the island, and the camera picks the farmer up again as soon as they walk.
    /// A press that never moves is a tap (<see cref="WasTap"/>); a second finger (pinch) lets go.
    /// </summary>
    [DefaultExecutionOrder(-50)]   // before WorldInput, so it knows who owns a drag on the frame it starts
    public sealed class VirtualJoystick : MonoBehaviour
    {
        [SerializeField, Tooltip("Rim radius in UI units (720 = the short side of the screen).")] private float radius = 74f;
        [SerializeField, Tooltip("How far a press must move before it becomes a drag, in UI units.")] private float deadZone = 12f;
        [SerializeField, Tooltip("Where the stick sits on a phone, as fractions of the screen (x across, y up from the bottom).")] private Vector2 anchor = new Vector2(0.5f, 0.2f);
        [SerializeField, Tooltip("On a large screen (tablet, desktop) the stick sits in a corner instead, this far in from the side and up from the bottom (pixels at 720 short side).")] private Vector2 cornerInset = new Vector2(150f, 150f);
        [SerializeField, Tooltip("Short screen side, in pixels, from which a screen counts as large.")] private float largeScreen = 900f;

        /// <summary>Where the player wants the stick on a large screen: "right" (default) or "left". Saved on the device.</summary>
        public static string Side
        {
            get => PlayerPrefs.GetString("fv_stick_side", "right");
            set { PlayerPrefs.SetString("fv_stick_side", value == "left" ? "left" : "right"); PlayerPrefs.Save(); }
        }
        [SerializeField, Tooltip("A press this many rims from the centre still grabs the stick (a little forgiving for thumbs).")] private float grabReach = 1.35f;

        /// <summary>The stick the farmer walks with (the game's UI draws it and reads it).</summary>
        public static VirtualJoystick? Current { get; private set; }

        /// <summary>
        /// Set by the game's UI (GameUi), which draws the stick where the design puts it: its centre on screen
        /// (pixels, y up). Null: the stick's own place. While the UI draws it, the stick's own drawing stays off.
        /// </summary>
        public static Func<Vector2>? CentreOverride { get; set; }

        /// <summary>The stick's rim radius on screen in pixels when the game's UI places it (0: its own size).</summary>
        public static float RadiusOverride { get; set; }

        /// <summary>Stick deflection, length 0..1, x right and y up on screen.</summary>
        public Vector2 Value { get; private set; }
        /// <summary>The finger is dragging the stick.</summary>
        public bool Engaged { get; private set; }
        /// <summary>A pointer went down and came up this frame without dragging.</summary>
        public bool WasTap { get; private set; }
        public Vector2 TapPosition { get; private set; }
        /// <summary>False while something else owns the pointer (placing a building).</summary>
        public bool Active { get; set; } = true;
        /// <summary>Screen points the stick must not start on (buttons, panels).</summary>
        public Func<Vector2, bool>? Blocked { get; set; }
        /// <summary>The stick has the pointer (pressed on it, maybe not dragging yet): the camera must not pan.</summary>
        public bool Holding => _held;
        /// <summary>While riding somewhere the stick turns into a red stop button.</summary>
        public bool StopMode { get; set; }
        /// <summary>The stop button was pressed this frame.</summary>
        public bool StopPressed { get; private set; }

        /// <summary>The stick's centre on screen (pixels, y up).</summary>
        public Vector2 Centre
        {
            get
            {
                if (CentreOverride != null) return CentreOverride();
                if (Mathf.Min(Screen.width, Screen.height) < largeScreen) return new Vector2(Screen.width * anchor.x, Screen.height * anchor.y);
                var inset = cornerInset * Scale;
                return new Vector2(Side == "left" ? inset.x : Screen.width - inset.x, inset.y);
            }
        }

        /// <summary>A press here grabs the stick: on its ring, and not on a button or panel.</summary>
        public bool InZone(Vector2 screen) =>
            Vector2.Distance(screen, Centre) <= Rim * grabReach && !(Blocked?.Invoke(screen) ?? false);

        private bool _held;
        private int _finger = -1;
        private bool _mouse;
        private Vector2 _origin;
        private Vector2 _pos;

        /// <summary>The rim radius on screen, pixels.</summary>
        private float Rim => RadiusOverride > 0 ? RadiusOverride : radius * Scale;

        private static float Scale => Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 720f);

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            Current = this;
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
        }

        private void Update()
        {
            WasTap = false;
            StopPressed = false;
            if (!Active)
            {
                Release(false);
                return;
            }
            var touches = Touch.activeTouches;
            if (touches.Count >= 2)
            {
                Release(false);   // pinch: the camera's
                return;
            }
            if (touches.Count == 1 || _finger >= 0)
            {
                if (_finger < 0)
                {
                    var t = touches[0];
                    if (t.phase == UnityEngine.InputSystem.TouchPhase.Began && InZone(t.screenPosition)) Press(t.finger.index, t.screenPosition, false);
                }
                else
                {
                    var found = false;
                    foreach (var t in touches)
                    {
                        if (t.finger.index != _finger) continue;
                        found = true;
                        _pos = t.screenPosition;
                        if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled) Release(true);
                    }
                    if (!found) Release(true);
                }
            }
            else
            {
                var m = Mouse.current;
                if (m != null)
                {
                    var p = m.position.ReadValue();
                    if (!_held && m.leftButton.wasPressedThisFrame && InZone(p)) Press(-1, p, true);
                    else if (_held && _mouse)
                    {
                        _pos = p;
                        if (!m.leftButton.isPressed || m.leftButton.wasReleasedThisFrame) Release(true);
                    }
                }
            }
            if (_held)
            {
                var d = _pos - _origin;
                var r = Rim;
                if (!Engaged && d.magnitude > deadZone * Scale) Engaged = true;
                if (Engaged)
                {
                    if (d.magnitude > r) d = d.normalized * r;   // the stick stays put; the knob stops at the rim
                    Value = d / r;
                }
            }
        }

        // a press can't outlive the app losing focus (the release would never arrive)
        private void OnApplicationFocus(bool focused)
        {
            if (!focused) Release(false);
        }

        private void Press(int finger, Vector2 at, bool mouse)
        {
            if (StopMode)
            {
                StopPressed = true;   // one press is enough; the finger isn't held
                return;
            }
            _held = true;
            _finger = finger;
            _mouse = mouse;
            _origin = Centre;
            _pos = at;
            Engaged = false;
        }

        private void Release(bool mayTap)
        {
            if (_held && !Engaged && mayTap)
            {
                WasTap = true;
                TapPosition = _pos;
            }
            _held = false;
            Engaged = false;
            _finger = -1;
            _mouse = false;
            Value = Vector2.zero;
        }

        // ---- drawing ------------------------------------------------------------------------------

    }
}
