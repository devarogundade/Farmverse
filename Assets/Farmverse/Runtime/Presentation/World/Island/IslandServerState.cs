#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// When the server changed the island itself (a mint took goods or a building, a redeem or refund
    /// gave some back; docs 13 §7.4), the device must take the server's island in place of its own,
    /// or its next upload would undo the change. Writes the server's island into the device saves and
    /// reloads the island scene, the same way a cloud restore does.
    /// </summary>
    public static class IslandServerState
    {
        public const string EconomyKey = "fv_economy_v1";
        public const string HeldKey = "fv_held_v1";

        [Serializable] private sealed class HeldList { public List<IslandState.Held> items = new List<IslandState.Held>(); }

        /// <summary>True from writing the server's island until the reloaded scene starts: nothing may save over it.</summary>
        public static bool Applying { get; private set; }

        /// <summary>Raised just before the reload (UI can show "Updating your farm…").</summary>
        public static event Action? Loading;

        /// <summary>Buildings the player owns but hasn't placed (redeemed from NFTs), as the server last sent them.</summary>
        public static IReadOnlyList<IslandState.Held> Held
        {
            get
            {
                if (!PlayerPrefs.HasKey(HeldKey)) return Array.Empty<IslandState.Held>();
                try { return JsonUtility.FromJson<HeldList>(PlayerPrefs.GetString(HeldKey))?.items ?? new List<IslandState.Held>(); }
                catch { return Array.Empty<IslandState.Held>(); }
            }
        }

        /// <summary>A held building was placed: it leaves the held list (the next upload tells the server).</summary>
        public static void TakeHeld(IslandState.Held placed)
        {
            var items = new List<IslandState.Held>(Held);
            var i = items.FindIndex(h => h.id == placed.id && h.level == placed.level && h.price == placed.price);
            if (i < 0) return;
            items.RemoveAt(i);
            PlayerPrefs.SetString(HeldKey, JsonUtility.ToJson(new HeldList { items = items }));
            PlayerPrefs.Save();
        }

        /// <summary>Takes <paramref name="server"/>'s island (an <see cref="IslandState"/> JSON) and reloads the scene.</summary>
        public static void Load(string serverStateJson)
        {
            var server = JsonUtility.FromJson<IslandState>(serverStateJson);
            if (server == null) return;
            Applying = true;
            if (server.economy != null) PlayerPrefs.SetString(EconomyKey, JsonUtility.ToJson(server.economy));
            IslandSandbox.KeepOnly(new HashSet<int>(server.buildings.Select(b => b.uid)));
            PlayerPrefs.SetString(HeldKey, JsonUtility.ToJson(new HeldList { items = server.held ?? new List<IslandState.Held>() }));
            PlayerPrefs.Save();
            Debug.Log($"[IslandServerState] loaded the server's island ({server.buildings.Count} buildings, {server.held?.Count ?? 0} held)");
            Loading?.Invoke();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>The reloaded island has started from the server's state: saving is safe again.</summary>
        internal static void Started() => Applying = false;
    }
}
