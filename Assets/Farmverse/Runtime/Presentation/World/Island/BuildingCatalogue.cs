#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// Everything a player can own on the island: the prefab, its price in coins and its footprint
    /// in grid cells. Starter entries are given free on a new island; the rest are bought in play.
    /// Filled by Farmverse / World / Build Island from Content/Tripo/Prefabs, then tuned by hand.
    /// </summary>
    [CreateAssetMenu(menuName = "Farmverse/Building Catalogue")]
    public sealed class BuildingCatalogue : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id = "";
            public string displayName = "";
            public GameObject? prefab;
            /// <summary>The same building's classic look, if there is one (a player can restyle to it).</summary>
            public GameObject? altPrefab;
            public int price = 100;
            public Vector2Int footprint = new Vector2Int(3, 3);   // cells, before turning
            public bool starter;
            public string category = "Farm";
        }

        public List<Entry> entries = new List<Entry>();

        public Entry? Find(string id) => entries.Find(e => e.id == id);
    }
}
