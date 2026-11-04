#nullable enable
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// A tree, bush or rock that grows on the island from the start. It takes its cells on the
    /// building grid until the player pays to clear it, which frees the land for building.
    /// </summary>
    public sealed class NatureProp : MonoBehaviour
    {
        public int index;               // stable id (scatter order), used by the save
        public string kind = "";        // oak, maple, apple, cherry, birch, pine, palm, round, bush, rock
        public string displayName = "";
        public RectInt cells;
        public int clearCost = 40;
        public bool shaderSway;         // moves in the wind on the GPU (Farmverse/TreeSway), not by transform
        public int chapter = 1;         // the chapter of the island whose land it grows on (hidden before)
    }
}
