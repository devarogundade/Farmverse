#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World
{
    /// <summary>
    /// Who the player can dress (the Customize screen): every role's model, grouped in tabs (Me, Army,
    /// Labourers, Builders, Townsfolk), and the parts they can wear. Built by Farmverse / Art / Build
    /// ITHappy Characters into Resources/CharacterCatalog so the UI can find it anywhere.
    /// </summary>
    public sealed class CharacterCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Person
        {
            /// <summary>The <see cref="CharacterLook"/> role this dresses (player, army, labourer, builder, folk_baker…).</summary>
            public string role = "";
            public string label = "";
            /// <summary>The tab it sits under (player, army, labourer, builder, townsfolk).</summary>
            public string tab = "";
            public GameObject? prefab;
            public string defaultLook = "";
        }

        /// <summary>
        /// A whole outfit from the packs (Customize ▸ Costume): bought once with gems (catalogue offer look_&lt;id&gt;),
        /// then anyone can wear it. The prefab is a complete person (its own body and animator).
        /// </summary>
        [Serializable]
        public sealed class Costume
        {
            public string id = "";
            public string label = "";
            /// <summary>Which pack it comes from (shown under its name).</summary>
            public string pack = "";
            public GameObject? prefab;
        }

        public List<Costume> costumes = new List<Costume>();

        public Costume? CostumeById(string id) => costumes.Find(c => c.id == id);

        public static readonly (string id, string label)[] Tabs =
        {
            ("player", "Me"), ("army", "Army"), ("labourer", "Labourers"), ("builder", "Builders"), ("townsfolk", "Townsfolk"),
        };

        public CharacterParts? parts;
        /// <summary>The people's animator (Idle, Walk…), for a model shown outside the island.</summary>
        public RuntimeAnimatorController? animator;
        public List<Person> people = new List<Person>();

        public static CharacterCatalog? Load() => Resources.Load<CharacterCatalog>("CharacterCatalog");

        public IEnumerable<Person> In(string tab)
        {
            foreach (var p in people) if (p.tab == tab) yield return p;
        }

        public Person? ByRole(string role) => people.Find(p => p.role == role);
    }
}
