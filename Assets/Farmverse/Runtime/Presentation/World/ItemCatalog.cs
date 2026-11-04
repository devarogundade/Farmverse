#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World
{
    /// <summary>
    /// The things people hold and carry, all from the bought packs (built by Farmverse / Art / Build ITHappy
    /// Items into Resources/ItemCatalog):
    ///  - tools: every Zombies Mini tool and weapon, the axe from the Farm pack's chopping block, the bucket.
    ///    Each prefab's origin is where the fist closes on it, its +Y runs up the handle to the head and
    ///    its +Z points the way the head sticks out (see <see cref="HandTool"/>).
    ///  - goods: a pack model for every good (wheat sheaf, egg, milk bottle, log, stone…), base on the ground,
    ///    sized to be carried.
    ///  - barrows (cart_001–006) and lawn mowers, pushed: origin between the handles on the ground, +Z towards
    ///    the wheel; hand carts (dray_002–005), pulled: the same, the worker walks in front.
    /// </summary>
    public sealed class ItemCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id = "";
            public GameObject? prefab;
        }

        public List<Entry> tools = new List<Entry>();
        public List<Entry> goods = new List<Entry>();
        public List<GameObject> barrows = new List<GameObject>();
        public List<GameObject> mowers = new List<GameObject>();
        public List<GameObject> carts = new List<GameObject>();
        public GameObject? fallbackGood;

        private Dictionary<string, GameObject>? _tools, _goods;
        private static ItemCatalog? _loaded;
        private static bool _tried;

        public static ItemCatalog? Load()
        {
            if (_loaded != null || _tried) return _loaded;
            _tried = true;
            _loaded = Resources.Load<ItemCatalog>("ItemCatalog");
            return _loaded;
        }

        public GameObject? Tool(string id)
        {
            _tools ??= Index(tools);
            return _tools.TryGetValue(id, out var p) ? p : null;
        }

        public GameObject? Good(string id)
        {
            _goods ??= Index(goods);
            return _goods.TryGetValue(id, out var p) ? p : fallbackGood;
        }

        private static Dictionary<string, GameObject> Index(List<Entry> list)
        {
            var d = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            foreach (var e in list) if (e.prefab != null && !string.IsNullOrEmpty(e.id)) d[e.id] = e.prefab;
            return d;
        }
    }

    /// <summary>Putting a tool in a person's fist, whatever the rig's bone axes.</summary>
    public static class HandTool
    {
        /// <summary>
        /// Hold <paramref name="prefab"/> in the right (or left) hand. The handle runs along the knuckles (little
        /// finger to index, so the head is on the thumb side), the head sticks out the way the fingers point,
        /// and the grip sits across the palm.
        /// </summary>
        public static GameObject? Hold(GameObject who, GameObject prefab, bool left = false)
        {
            var anim = who.GetComponentInChildren<Animator>();
            if (anim == null || !anim.isHuman) return null;
            var hand = anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var index = anim.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            var little = anim.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            var middle = anim.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            if (hand == null) return null;
            var go = UnityEngine.Object.Instantiate(prefab, hand);
            go.name = "Tool";
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            var finger = middle != null ? (middle.position - hand.position) : hand.TransformDirection(Vector3.up) * 0.08f;
            var knuckles = index != null && little != null ? (index.position - little.position) : hand.TransformDirection(Vector3.forward);
            var fingerDir = finger.normalized;
            var up = Vector3.ProjectOnPlane(knuckles, fingerDir).normalized;
            if (up.sqrMagnitude < 1e-4f) up = Vector3.Cross(fingerDir, Vector3.right).normalized;
            var palm = hand.position + finger * 0.95f;
            go.transform.SetPositionAndRotation(palm, Quaternion.LookRotation(fingerDir, up));
            var s = who.transform.lossyScale.y;
            var parentScale = hand.lossyScale.y;
            go.transform.localScale = Vector3.one * (parentScale > 1e-5f ? s / parentScale : 1f);
            return go;
        }

        /// <summary>Tools held in both hands (the left hand goes down the handle).</summary>
        public static bool TwoHanded(string id) => id is "Axe" or "Pickaxe" or "Shovel" or "Hoe" or "Rake" or "Bat" or "Crowbar";

        /// <summary>
        /// Hands on what the person holds or pushes: a pushed or pulled thing's grips (GripL/GripR, set by the item
        /// build), or the left hand down a two-handed tool's handle; nothing: hands free.
        /// </summary>
        public static void Reach(GameObject who, GameObject? pushed, GameObject? tool, string toolId)
        {
            var reach = who.GetComponent<HandReach>();
            if (reach == null)
            {
                if (pushed == null && tool == null) return;
                reach = who.AddComponent<HandReach>();
            }
            if (pushed != null)
            {
                reach.Left = pushed.transform.Find("GripL");
                reach.Right = pushed.transform.Find("GripR");
                reach.LeftAlong = 0f;
                return;
            }
            reach.Right = null;   // the right hand holds the tool as the animation swings it
            reach.Left = tool != null && TwoHanded(toolId) ? tool.transform : null;
            reach.LeftAlong = -0.34f * who.transform.lossyScale.y;
        }

        /// <summary>A pushed thing (barrow, mower) in front of the person, or a pulled cart behind, as a child of the body.</summary>
        public static GameObject Push(Transform body, GameObject prefab, bool pulled, float reach, float lift = 0f)
        {
            var go = UnityEngine.Object.Instantiate(prefab, body);
            go.name = pulled ? "Cart" : "Barrow";
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            var s = body.lossyScale.y > 1e-5f ? 1f / body.lossyScale.y : 1f;
            go.transform.localScale = Vector3.one * s;
            // the handles come up to the hands: a wheelbarrow is lifted, rocking forward onto its wheel; a mower or a
            // cart, whose handle is fixed, is sized up to the person (the packs make people bigger than their props)
            var tilt = 0f;
            var grip = go.transform.Find("GripL");
            var gripY = grip != null ? grip.localPosition.y : 0f;
            var handY = HandReach.HandHeight(body.gameObject);   // metres: the prop stands at world scale 1
            if (grip != null && lift > 0f)
            {
                lift = Mathf.Clamp(handY - gripY - 0.04f, 0f, 0.7f);
                tilt = Mathf.Asin(Mathf.Clamp(lift / 1.2f, 0f, 0.6f)) * Mathf.Rad2Deg;   // ~1.2 m from handles to wheel
            }
            else if (grip != null && gripY > 0.1f && handY > gripY * 1.08f)
            {
                var up = Mathf.Min(handY * 0.95f / gripY, 1.6f);
                go.transform.localScale *= up;
                lift = 0f;
            }
            else lift = 0f;
            go.transform.localPosition = new Vector3(0f, lift * s, (pulled ? -reach : reach) * s);
            go.transform.localRotation = pulled ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.Euler(tilt, 0f, 0f);
            return go;
        }

        /// <summary>A barrow's handles are lifted to the hands when pushed (any value above 0 asks for it).</summary>
        public static float BarrowLift(Transform body) => 1f;
    }
}
