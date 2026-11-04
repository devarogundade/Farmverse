#nullable enable
using System;
using UnityEngine;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// What the game's UI (GameUi) tells the island: whether a popup covers it and whether a screen
    /// point is over one of its buttons. A press over the UI never walks the farmer, pans the camera
    /// or selects a building.
    /// </summary>
    public static class IslandHud
    {
        /// <summary>One of GameUi's popups is open over the island.</summary>
        public static Func<bool>? GameCovering { get; set; }

        /// <summary>Is this screen point (pixels, y up) over one of GameUi's buttons?</summary>
        public static Func<Vector2, bool>? OverGame { get; set; }

        public static bool Covered => GameCovering != null && GameCovering();

        public static bool Over(Vector2 screen) => Covered || (OverGame != null && OverGame(screen));
    }
}
