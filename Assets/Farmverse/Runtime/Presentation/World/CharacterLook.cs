#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World
{
    /// <summary>
    /// Dresses a person from <see cref="CharacterParts"/>. Every character of one role (the player, the
    /// army, labourers, builders, townsfolk) wears the look the player chose for that role; the choice is
    /// kept on the device and every dressed character of that role changes the moment it is saved.
    /// A look is "Slot=Mesh;Slot=Mesh" (an empty mesh = nothing in that slot).
    /// </summary>
    public sealed class CharacterLook : MonoBehaviour
    {
        /// <summary>Roles the player can dress, in the order the Customize screen shows them.</summary>
        public static readonly string[] Roles = { "player", "army", "labourer", "builder", "townsfolk" };

        [SerializeField] private string role = "player";
        [SerializeField] private CharacterParts? parts;
        [SerializeField, Tooltip("What this role wears until the player changes it.")] private string defaultLook = "";
        [SerializeField, Tooltip("Townsfolk keep their own look instead of the role's.")] private bool ownLook;

        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>();
        private static readonly List<CharacterLook> Live = new List<CharacterLook>();
        public static event Action<string>? Changed;

        public string Role => role;
        public CharacterParts? Parts => parts;

        private void Awake()
        {
            if (!string.IsNullOrEmpty(defaultLook) && !Defaults.ContainsKey(role)) Defaults[role] = defaultLook;
            Apply(ownLook ? defaultLook : LookOf(role));
        }

        private void OnEnable() => Live.Add(this);
        private void OnDisable() => Live.Remove(this);

        /// <summary>The look saved for a role (or its default).</summary>
        public static string LookOf(string role)
        {
            try
            {
                var saved = PlayerPrefs.GetString("fv_look_" + role, "");
                if (!string.IsNullOrEmpty(saved)) return saved;
            }
            catch { /* prefs unavailable: use the default */ }
            return Defaults.TryGetValue(role, out var d) ? d : "";
        }

        /// <summary>Keep a role's new look and dress everyone of that role in it.</summary>
        public static void Save(string role, string look)
        {
            PlayerPrefs.SetString("fv_look_" + role, look);
            PlayerPrefs.Save();
            foreach (var c in Live.ToArray()) if (c.role == role && !c.ownLook) c.Apply(look);
            Changed?.Invoke(role);
        }

        public static Dictionary<string, string> Parse(string look)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in (look ?? "").Split(';'))
            {
                var eq = part.IndexOf('=');
                if (eq > 0) d[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
            return d;
        }

        public static string Write(Dictionary<string, string> look)
        {
            var parts = new List<string>();
            foreach (var kv in look) parts.Add(kv.Key + "=" + kv.Value);
            return string.Join(";", parts);
        }

        /// <summary>The slot that holds a whole costume instead of parts (an id from CharacterCatalog.costumes; empty: own clothes).</summary>
        public const string CostumeSlot = "Costume";

        private GameObject? _base;      // the body built from parts (hidden while a costume is worn)
        private GameObject? _costume;
        private string _costumeId = "";

        /// <summary>The costume worn now ("" for none).</summary>
        public string CostumeId => _costumeId;

        /// <summary>Put on a look: each slot named in it gets that mesh (or nothing); slots not named stay as they are.</summary>
        public void Apply(string look)
        {
            var want = Parse(look);
            want.TryGetValue(CostumeSlot, out var costume);
            Wear(costume ?? "");
            if (parts == null) return;
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var slot = SlotOf(smr.name);
                if (slot == null || !want.TryGetValue(slot.name, out var meshName)) continue;
                var mesh = parts.MeshOf(slot.name, meshName);
                smr.sharedMesh = mesh;
                smr.enabled = mesh != null;
                if (mesh != null)
                {
                    smr.localBounds = mesh.bounds;
                    if (parts.material != null) smr.sharedMaterial = parts.material;
                }
            }
        }

        /// <summary>
        /// Swap the body for a costume (a whole person from the packs: its own meshes and skeleton), driven by this
        /// person's animator controller; "" puts the own body back.
        /// </summary>
        private void Wear(string id)
        {
            if (id == _costumeId) return;
            if (_base == null)
            {
                var anim = GetComponentInChildren<Animator>(true);
                _base = anim != null ? (anim.transform.parent == transform || anim.transform == transform ? anim.gameObject : anim.transform.parent.gameObject) : null;
                if (_base == gameObject) _base = null;   // the animator sits on the root: no swapping
            }
            if (_costume != null) Destroy(_costume);
            _costume = null;
            _costumeId = "";
            var entry = string.IsNullOrEmpty(id) ? null : CharacterCatalog.Load()?.CostumeById(id);
            var model = entry?.prefab != null ? entry.prefab.transform.Find("Model") : null;
            if (_base == null || model == null)
            {
                if (_base != null) _base.SetActive(true);
                Rebind();
                return;
            }
            var baseAnim = _base.GetComponentInChildren<Animator>(true);
            _costume = Instantiate(model.gameObject, transform);
            _costume.name = "Costume";
            _costume.transform.localPosition = model.localPosition;
            _costume.transform.localRotation = model.localRotation;
            _costume.transform.localScale = model.localScale;
            foreach (var t in _costume.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
            foreach (var c in _costume.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            var anim2 = _costume.GetComponentInChildren<Animator>(true);
            if (anim2 != null && baseAnim != null && baseAnim.runtimeAnimatorController != null)
            {
                anim2.runtimeAnimatorController = baseAnim.runtimeAnimatorController;
                anim2.cullingMode = baseAnim.cullingMode;
            }
            _base.SetActive(false);
            _costumeId = id;
            Rebind();
        }

        private void Rebind()
        {
            var motion = GetComponent<CharacterMotion>();
            if (motion != null) motion.Rebind();
        }

        private CharacterParts.Slot? SlotOf(string child)
        {
            if (parts == null) return null;
            CharacterParts.Slot? best = null;
            foreach (var s in parts.slots)
                if (child.StartsWith(s.name, StringComparison.Ordinal) && (best == null || s.name.Length > best.name.Length)) best = s;
            return best;
        }
    }
}
