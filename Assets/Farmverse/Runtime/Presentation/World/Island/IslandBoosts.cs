#nullable enable
using System;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// Timed boosts the island reads while it plays (the game sets <see cref="Running"/> from the server's
    /// inventory): Worker Rush makes workers and builders move twice as fast, a Raid Shield keeps raiders away.
    /// Fertilizer, extra builders and basket slots live in the economy itself (saved with the island).
    /// </summary>
    public static class IslandBoosts
    {
        /// <summary>True while the boost (shield, fert, rush) runs.</summary>
        public static Func<string, bool> Running { get; set; } = _ => false;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Running = _ => false;
    }
}
