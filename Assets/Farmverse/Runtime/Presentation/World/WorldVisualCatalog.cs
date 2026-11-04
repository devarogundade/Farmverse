#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World
{
    /// <summary>
    /// Maps catalogue ids to models (02 §7.2). Art changes are data: a new crop
    /// needs a model named after it under Content/World/Models, which the setup tool
    /// picks up, never code. Each model holds its growth or working stages as child
    /// objects named after <see cref="VisualStage"/> (see <see cref="StagedVisual"/>).
    /// An id with no model is simply not drawn, so a catalogue published before its
    /// art still plays (the object stays tappable through its tile).
    /// </summary>
    [CreateAssetMenu(menuName = "Farmverse/World Visual Catalog", fileName = "WorldVisualCatalog")]
    public sealed class WorldVisualCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Catalogue id (crop_wheat, livestock_chicken, tree_pine, machine_mill, decor_bench, path_dirt, fence_wood), a world id (land_tile, land_sale, land_wild, pen, farmhouse, facility_warehouse) or a prefix ending in * (crop_*).")]
            public string id = string.Empty;
            public GameObject? prefab;
        }

        [SerializeField, Tooltip("Centre-to-corner size of a land tile, in metres.")] private float tileSize = 2.6f;
        [SerializeField, Tooltip("Rings of tiles kept clear of scenery around the farmhouse: the farthest the land can grow (catalogue land.maxRadius) plus the wild rings drawn beyond it.")] private int landRadius = 11;
        [SerializeField, Tooltip("People who walk around the edge of the farm.")] private GameObject[] villagers = Array.Empty<GameObject>();
        [SerializeField, Tooltip("Trees, rocks and bushes scattered around the farm (never where the land can grow).")] private GameObject[] scenery = Array.Empty<GameObject>();
        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<string, Entry>? _index;
        private List<Entry>? _prefixes;

        public float TileSize => tileSize;
        public int LandRadius => landRadius;
        public IReadOnlyList<GameObject> Villagers => villagers;
        public IReadOnlyList<GameObject> Scenery => scenery;

        public GameObject? Prefab(string id)
        {
            _index ??= BuildIndex();
            if (_index.TryGetValue(id, out var entry)) return entry.prefab;
            return ByPrefix(id)?.prefab;
        }

        /// <summary>Add or replace an entry (editor setup tooling).</summary>
        public void SetEntry(string id, GameObject prefab)
        {
            entries.RemoveAll(e => e.id == id);
            entries.Add(new Entry { id = id, prefab = prefab });
            _index = null;
        }

        public void ClearEntries()
        {
            entries.Clear();
            _index = null;
        }

        public void SetDefaults(float size, int radius, GameObject[] people, GameObject[] props)
        {
            tileSize = size;
            landRadius = radius;
            villagers = people;
            scenery = props;
            _index = null;
        }

        private Entry? ByPrefix(string id)
        {
            Entry? best = null;
            foreach (var e in _prefixes!)
            {
                var prefix = e.id.Substring(0, e.id.Length - 1);
                if (id.StartsWith(prefix, StringComparison.Ordinal) && (best == null || prefix.Length > best.id.Length - 1)) best = e;
            }
            return best;
        }

        private Dictionary<string, Entry> BuildIndex()
        {
            var map = new Dictionary<string, Entry>(StringComparer.Ordinal);
            _prefixes = new List<Entry>();
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.id) || e.prefab == null) continue;
                if (e.id.EndsWith("*", StringComparison.Ordinal)) _prefixes.Add(e);
                else map[e.id] = e;
            }
            return map;
        }

        private void OnValidate() => _index = null;
    }
}
