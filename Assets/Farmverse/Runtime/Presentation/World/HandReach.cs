#nullable enable
using UnityEngine;

namespace Farmverse.Presentation.World
{
    /// <summary>
    /// Puts a person's hands where a tool needs them, after the animation has posed the body (a two-bone arm
    /// solve in LateUpdate, so it works with every controller without an IK pass):
    ///  - both hands on a wheelbarrow's or mower's handles (arms reaching forward to push), or on a cart's shafts;
    ///  - the left hand further down the handle of a two-handed tool held in the right (axe, pickaxe, shovel…).
    /// Targets are Transforms that move with what they're on; a weight eases the reach in and out.
    /// </summary>
    public sealed class HandReach : MonoBehaviour
    {
        public Transform? Left, Right;
        /// <summary>Local offset along the left target's own up axis (down a tool's handle: negative).</summary>
        public float LeftAlong;

        private Animator? _anim;
        private Transform? _lu, _ll, _lh, _ru, _rl, _rh;
        private float _wl, _wr;

        private void Awake() => Bind();

        private void Bind()
        {
            _anim = GetComponentInChildren<Animator>();
            if (_anim == null || !_anim.isHuman) return;
            _lu = _anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _ll = _anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _lh = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
            _ru = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rl = _anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rh = _anim.GetBoneTransform(HumanBodyBones.RightHand);
        }

        private void LateUpdate()
        {
            if (_anim == null || !_anim.isActiveAndEnabled) { Bind(); if (_anim == null) return; }   // a costume swaps the body
            var step = Time.deltaTime * 6f;
            // a hand only goes where the arm can reach (a tool hanging at the other side waits for the swing to bring it near)
            var lt = Left != null ? Left.position + Left.up * LeftAlong : Vector3.zero;
            var leftOk = Left != null && _lu != null && Reachable(_lu, _ll, _lh, lt, 1.08f);
            var rightOk = Right != null && _ru != null && Reachable(_ru, _rl, _rh, Right.position, 1.15f);
            _wl = Mathf.MoveTowards(_wl, leftOk ? 1f : 0f, step);
            _wr = Mathf.MoveTowards(_wr, rightOk ? 1f : 0f, step);
            if (_wl > 0f && Left != null && _lu != null && _ll != null && _lh != null)
                Solve(_lu, _ll, _lh, Left.position + Left.up * LeftAlong, -transform.right, _wl);
            if (_wr > 0f && Right != null && _ru != null && _rl != null && _rh != null)
                Solve(_ru, _rl, _rh, Right.position, transform.right, _wr);
        }

        private static bool Reachable(Transform? upper, Transform? lower, Transform? hand, Vector3 target, float slack)
        {
            if (upper == null || lower == null || hand == null) return false;
            var arm = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
            return Vector3.Distance(upper.position, target) <= arm * slack;
        }

        /// <summary>The height of a hand above the feet in the pose the person stands in now (for sizing what they push).</summary>
        public static float HandHeight(GameObject who)
        {
            var anim = who.GetComponentInChildren<Animator>();
            var hand = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
            return hand != null ? hand.position.y - who.transform.position.y : 0.9f;
        }

        /// <summary>Two-bone solve: bend the elbow to the right length, then aim the arm at the target; elbows out and down.</summary>
        private void Solve(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 side, float weight)
        {
            var u0 = upper.rotation; var l0 = lower.rotation;
            var a = upper.position; var b = lower.position; var c = hand.position;
            float ab = Vector3.Distance(a, b), bc = Vector3.Distance(b, c);
            if (ab < 1e-4f || bc < 1e-4f) return;
            var at = target - a;
            float d = Mathf.Clamp(at.magnitude, Mathf.Abs(ab - bc) + 1e-3f, ab + bc - 1e-3f);
            // 1. the elbow's angle for that reach (law of cosines)
            float want = Mathf.Acos(Mathf.Clamp((ab * ab + bc * bc - d * d) / (2f * ab * bc), -1f, 1f));
            float now = Vector3.Angle(a - b, c - b) * Mathf.Deg2Rad;
            var hint = (side * 0.6f - transform.up * 0.8f).normalized;   // the elbow points out and down
            var axis = Vector3.Cross(a - b, c - b);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(at, hint);
            axis.Normalize();
            lower.rotation = Quaternion.AngleAxis((want - now) * Mathf.Rad2Deg, axis) * lower.rotation;   // turning about a×c opens the elbow
            // 2. swing the whole arm so the hand lands on the target
            upper.rotation = Quaternion.FromToRotation(hand.position - a, at) * upper.rotation;
            // 3. turn the arm about the shoulder–target line so the elbow sits toward the hint
            var dir = at.normalized;
            var elbow = Vector3.ProjectOnPlane(lower.position - a, dir);
            var toward = Vector3.ProjectOnPlane(hint, dir);
            if (elbow.sqrMagnitude > 1e-6f && toward.sqrMagnitude > 1e-6f)
                upper.rotation = Quaternion.FromToRotation(elbow, toward) * upper.rotation;
            if (weight < 1f)
            {
                upper.rotation = Quaternion.Slerp(u0, upper.rotation, weight);
                lower.rotation = Quaternion.Slerp(l0, lower.rotation, weight);
            }
        }
    }
}
