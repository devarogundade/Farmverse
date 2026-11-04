#nullable enable
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>A building standing on the island.</summary>
    public sealed class PlacedBuilding : MonoBehaviour
    {
        public string id = "";
        public int uid;
        public RectInt cells;
        public int turns;      // quarter turns
        public bool alt;       // wearing its classic look (the catalogue's alternative model)
    }
}
