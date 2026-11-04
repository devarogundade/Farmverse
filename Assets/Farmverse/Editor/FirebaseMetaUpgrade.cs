#nullable enable
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// Re-saves the Firebase plugin import settings (.meta files) in the current
    /// format. Old Firebase packages ship PluginImporter version 1, which Unity 6.6
    /// still reads but warns about on every compile. Run once after importing or
    /// updating a Firebase package: Farmverse ▸ Tools ▸ Upgrade Firebase plugin settings.
    /// </summary>
    public static class FirebaseMetaUpgrade
    {
        private static readonly string[] Roots =
        {
            "Assets/Firebase/Plugins",
            "Assets/Plugins/iOS/Firebase",
            "Assets/Plugins/tvOS/Firebase",
        };

        [MenuItem("Farmverse/Tools/Upgrade Firebase plugin settings")]
        public static void Run()
        {
            var paths = new List<string>();
            foreach (var root in Roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var meta in Directory.GetFiles(root, "*.meta", SearchOption.AllDirectories))
                {
                    var text = File.ReadAllText(meta);
                    if (!text.Contains("PluginImporter:")) continue;
                    paths.Add(meta.Substring(0, meta.Length - ".meta".Length).Replace('\\', '/'));
                }
            }

            if (paths.Count == 0)
            {
                Debug.Log("Farmverse: no Firebase plugin settings found to upgrade.");
                return;
            }

            AssetDatabase.ForceReserializeAssets(paths, ForceReserializeAssetsOptions.ReserializeMetadata);
            AssetDatabase.Refresh();
            Debug.Log($"Farmverse: re-saved {paths.Count} Firebase plugin settings in the current format.");
        }
    }
}
