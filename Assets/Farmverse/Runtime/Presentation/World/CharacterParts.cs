#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World
{
    /// <summary>
    /// Every part a person can wear (Creative Characters): the meshes for each slot of the shared rig.
    /// Built by Farmverse / Art / Build ITHappy Art from Assets/ithappy/Creative_Characters_FREE.
    /// A slot's child on the base model is the one whose name starts with the slot's name.
    /// </summary>
    [CreateAssetMenu(menuName = "Farmverse/Character Parts")]
    public sealed class CharacterParts : ScriptableObject
    {
        [Serializable]
        public sealed class Slot
        {
            public string name = "";
            /// <summary>Shown to players ("Hat", "Top"…).</summary>
            public string label = "";
            /// <summary>The slot may be left empty (no hat, no glasses).</summary>
            public bool canBeEmpty = true;
            public List<Mesh> options = new List<Mesh>();
        }

        public List<Slot> slots = new List<Slot>();
        /// <summary>The pack's one palette material, used by every part.</summary>
        public Material? material;

        public Slot? Find(string slot) => slots.Find(s => s.name == slot);

        public Mesh? MeshOf(string slot, string mesh)
        {
            var s = Find(slot);
            if (s == null || string.IsNullOrEmpty(mesh)) return null;
            foreach (var m in s.options) if (m != null && m.name == mesh) return m;
            return null;
        }
    }
}
