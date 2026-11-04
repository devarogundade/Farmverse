#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// Pictures of the island's own models for the game's UI: every building in the shop ("b:barn"),
    /// the people you talk to ("p:worker", "p:grandma"…) and the animals ("a:sheep"). Rendered from the
    /// prefabs by Farmverse / World / Build Island, so the shop always shows the model you'll place.
    /// </summary>
    [CreateAssetMenu(menuName = "Farmverse/Island Pictures")]
    public sealed class IslandPictures : ScriptableObject
    {
        [Serializable]
        public sealed class Picture
        {
            public string key = "";
            public Texture2D? texture;
        }

        public List<Picture> pictures = new List<Picture>();

        private Dictionary<string, Texture2D>? _byKey;

        public Texture2D? Find(string key)
        {
            // rebuilt when the list changed under it (the asset re-rendered while the editor kept this object)
            if (_byKey == null || _byKey.Count != pictures.Count)
            {
                _byKey = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
                foreach (var p in pictures) if (p.texture != null) _byKey[p.key] = p.texture;
            }
            return _byKey.TryGetValue(key, out var t) ? t : null;
        }

        public Texture2D? Building(string id) => Find("b:" + id);
        public Texture2D? Person(string who) => Find("p:" + who);

        private void OnValidate() => _byKey = null;
    }
}
