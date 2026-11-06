#nullable enable
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Farmverse.Application.Features;
using Farmverse.Domain.Gameplay;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Farmverse.Presentation.World.Island
{
    /// <summary>
    /// Cloud save: every mirror also stores the device's own island files on the server, and a phone whose
    /// island was never saved to this player's cloud (new phone, reinstall, switched account) takes the
    /// cloud's farm when it has gone further. Rollout phase 1, shadow mode: the game still owns the island, but every time it opens it also sends
    /// the island as it was saved to the server engine (island-sync, mirror) and runs the same span itself
    /// with <see cref="IslandSim"/>. The two must come out identical; a difference means the server would
    /// not run the island the way the game does, and phase 2 (server authority) must wait.
    /// </summary>
    public static class IslandShadow
    {
        /// <summary>How one comparison went.</summary>
        public sealed class Check
        {
            public bool Match;
            /// <summary>True when the server clamped the device clock (a week or more away, or a clock ahead), so no comparison was made.</summary>
            public bool Skipped;
            public double AwaySeconds;
            public long RoundTripMs;
            public string Detail = "";
            public override string ToString() =>
                Skipped ? $"skipped ({Detail})" : (Match ? "match" : "MISMATCH " + Detail) + $", {AwaySeconds:0}s away, {RoundTripMs} ms";
        }

        private static ServerCalls? _server;
        private static string? _uid;
        private static Func<Task<CloudCopy?>>? _readCloud;
        private static IslandState? _pending;
        private static bool _running;

        /// <summary>The latest comparison (null until one has run).</summary>
        public static Check? Last { get; private set; }

        public static event Action<Check>? Checked;

        /// <summary>True once after the island was brought back from the cloud (the game says so, then clears it).</summary>
        public static bool JustRestored { get; set; }

        /// <summary>PlayerPrefs keys of the device's island save files (the sandbox and the economy register theirs).</summary>
        private static readonly System.Collections.Generic.List<string> DeviceKeys = new System.Collections.Generic.List<string>();

        /// <summary>The player whose cloud copy this device's island was last saved to.</summary>
        private const string OwnerPref = "fv_island_owner";

        /// <summary>The server's copy for cloud save: the device files saved with it.</summary>
        public sealed class CloudCopy
        {
            public string? DeviceSave;

            /// <summary>Island XP in the saved files (the economy file's), 0 when there is none.</summary>
            public long Xp => DeviceXp(DeviceSave);
        }

        /// <summary>The island XP inside a device snapshot (its economy save).</summary>
        public static long DeviceXp(string? deviceSave)
        {
            if (string.IsNullOrEmpty(deviceSave)) return 0;
            try
            {
                var files = JsonUtility.FromJson<DeviceFiles>(deviceSave);
                long best = 0;
                foreach (var f in files?.files ?? new System.Collections.Generic.List<DeviceFile>())
                    if (f.key.Contains("economy"))
                        best = Math.Max(best, JsonUtility.FromJson<EconomySave>(f.value)?.xp ?? 0);
                return best;
            }
            catch
            {
                return 0;
            }
        }

        [Serializable] private sealed class DeviceFile { public string key = ""; public string value = ""; }
        [Serializable] private sealed class DeviceFiles { public System.Collections.Generic.List<DeviceFile> files = new System.Collections.Generic.List<DeviceFile>(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _server = null;
            _uid = null;
            _readCloud = null;
            _pending = null;
            _running = false;
            JustRestored = false;
            DeviceKeys.Clear();
            Last = null;
        }

        /// <summary>A save file of the island (PlayerPrefs key) that cloud save carries.</summary>
        public static void RegisterDeviceKey(string key)
        {
            if (!DeviceKeys.Contains(key)) DeviceKeys.Add(key);
        }

        /// <summary>
        /// Called by the composition root once the session is signed in and ready (null turns it off).
        /// <paramref name="readCloud"/> reads the server's copy (state/island) for restoring on a new phone.
        /// </summary>
        public static void Attach(ServerCalls? server, string? uid = null, Func<Task<CloudCopy?>>? readCloud = null)
        {
            _server = server;
            _uid = uid;
            _readCloud = readCloud;
            Flush();
        }

        /// <summary>The island as the game loaded it from its save, before catching up (from the economy host), or as it is now.</summary>
        public static void Offer(IslandState loaded)
        {
            _pending = loaded;
            Flush();
        }

        private static void Flush()
        {
            if (_server == null || _pending == null || _running) return;
            var state = _pending;
            _pending = null;
            _ = Run(_server, state);
        }

        /// <summary>The device's island save files as one JSON (what cloud save stores).</summary>
        private static string DeviceSnapshot()
        {
            var files = new DeviceFiles();
            foreach (var key in DeviceKeys)
                if (PlayerPrefs.HasKey(key)) files.files.Add(new DeviceFile { key = key, value = PlayerPrefs.GetString(key) });
            return JsonUtility.ToJson(files);
        }

        /// <summary>
        /// Should the cloud copy replace this device's island? Only when this device's island was never saved
        /// to this player's cloud (a new phone, a reinstall, a switched account) and the cloud one has gone further.
        /// </summary>
        public static bool ShouldRestore(string? owner, string uid, long localXp, CloudCopy? cloud) =>
            owner != uid && cloud != null && !string.IsNullOrEmpty(cloud.DeviceSave) && cloud.Xp > localXp;

        private static bool Restore(string deviceSave)
        {
            DeviceFiles? files;
            try { files = JsonUtility.FromJson<DeviceFiles>(deviceSave); }
            catch { return false; }
            if (files == null || files.files.Count == 0) return false;
            foreach (var f in files.files)
                if (!string.IsNullOrEmpty(f.key) && f.key.StartsWith("fv_", StringComparison.Ordinal)) PlayerPrefs.SetString(f.key, f.value);
            PlayerPrefs.Save();
            return true;
        }

        private static async Task Run(ServerCalls server, IslandState before)
        {
            _running = true;
            try
            {
                var uid = _uid;
                // cloud save, the restore half: a phone that never saved to this player's cloud takes the cloud's farm
                if (uid != null && _readCloud != null && PlayerPrefs.GetString(OwnerPref, "") != uid)
                {
                    var cloud = await _readCloud();
                    if (ShouldRestore(PlayerPrefs.GetString(OwnerPref, ""), uid, before.economy?.xp ?? 0, cloud) && Restore(cloud!.DeviceSave!))
                    {
                        PlayerPrefs.SetString(OwnerPref, uid);
                        PlayerPrefs.Save();
                        Debug.Log("[IslandCloud] restored the island from the cloud");
                        JustRestored = true;
                        _pending = null;
                        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                        return;
                    }
                }

                // the save half: mirror the island and keep the device's files beside it
                var json = JsonUtility.ToJson(before);
                var watch = Stopwatch.StartNew();
                var result = await server.IslandSync(json, mirror: true, device: DeviceSnapshot());
                watch.Stop();
                if (!result.IsOk)
                {
                    Debug.LogWarning("[IslandShadow] island-sync failed: " + result.Error);
                    return;
                }
                if (uid != null)
                {
                    PlayerPrefs.SetString(OwnerPref, uid);
                    PlayerPrefs.Save();
                }
                // the server changed the island itself (a redeem, a mint refund): take its island, or the
                // next upload would undo the change (docs 13 §7.4)
                if (result.Value.ServerChanged == true)
                {
                    _pending = null;
                    IslandServerState.Load(result.Value.State);
                    return;
                }
                var check = Compare(before, JsonUtility.FromJson<IslandState>(result.Value.State));
                check.RoundTripMs = watch.ElapsedMilliseconds;
                Last = check;
                if (check.Match || check.Skipped) Debug.Log("[IslandShadow] " + check);
                else Debug.LogWarning("[IslandShadow] " + check);
                Checked?.Invoke(check);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[IslandShadow] " + e.Message);
            }
            finally
            {
                _running = false;
                Flush();   // an offer that came in meanwhile
            }
        }

        /// <summary>Run the same span here and compare the economy saves field by field (pure; tested).</summary>
        public static Check Compare(IslandState before, IslandState server)
        {
            var from = before.economy?.now ?? before.simAt;
            var check = new Check { AwaySeconds = server.simAt - from };
            if (before.economy == null || from <= 0 || server.economy == null)
            {
                check.Skipped = true;
                check.Detail = "no save";
                return check;
            }
            if (from > server.simAt || server.simAt - from > 7 * 24 * 3600 + 1)
            {
                check.Skipped = true;
                check.Detail = "device clock clamped";
                return check;
            }
            var local = IslandSim.Advance(before, server.simAt).State;
            var a = JsonUtility.ToJson(local.economy);
            var b = JsonUtility.ToJson(server.economy);
            check.Match = a == b;
            if (!check.Match) check.Detail = FirstDifference(a, b);
            return check;
        }

        private static string FirstDifference(string a, string b)
        {
            var n = Math.Min(a.Length, b.Length);
            var i = 0;
            while (i < n && a[i] == b[i]) i++;
            var start = Math.Max(0, i - 40);
            string Cut(string s) => s.Substring(start, Math.Min(s.Length - start, 100));
            return $"at {i}: game …{Cut(a)}… server …{Cut(b)}…";
        }
    }
}
