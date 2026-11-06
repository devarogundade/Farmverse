#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// Keeps the web download small: WebGL import overrides for every texture, sound and model the build uses
    /// (the scenes' dependencies and everything in a Resources folder). Runs before each web build, so it also
    /// covers the store packs, which aren't in git. Farmverse ▸ Web ▸ Shrink Web Assets runs it alone.
    /// </summary>
    public static class FarmverseWebSize
    {
        private const string Web = "WebGL";

        [MenuItem("Farmverse/Web/Shrink Web Assets", priority = 39)]
        public static void Apply()
        {
            var roots = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path)
                .Concat(AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") && p.Contains("/Resources/") && !AssetDatabase.IsValidFolder(p)))
                .ToArray();
            var used = AssetDatabase.GetDependencies(roots, true).Where(p => p.StartsWith("Assets/")).Distinct().ToList();

            int textures = 0, sounds = 0, models = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in used)
                {
                    switch (AssetImporter.GetAtPath(path))
                    {
                        case TextureImporter t when Texture(t, path): textures++; break;
                        case AudioImporter a when Sound(a, path): sounds++; break;
                        case ModelImporter m when Model(m): models++; break;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // the rest of the download: no Unity splash (its logo is 2.7 MB), no unused mesh channels, smaller code
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.stripUnusedMeshComponents = true;
            PlayerSettings.SetIl2CppCodeGeneration(UnityEditor.Build.NamedBuildTarget.WebGL, UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize);
            AssetDatabase.SaveAssets();
            Debug.Log($"Farmverse: web sizes set for {used.Count} assets ({textures} textures, {sounds} sounds, {models} models changed).");
        }

        /// <summary>
        /// World and pack textures (skies included): compressed and crunched, at most 1024 px. The game UI's pictures
        /// stay sharp (GameUiTextureImport keeps them uncompressed), except its big full-screen backgrounds without
        /// transparency, which are compressed (not crunched) at full size. True when changed.
        /// </summary>
        private static bool Texture(TextureImporter t, string path)
        {
            if (t.isReadable) return false; // read on the CPU at runtime: leave its format alone
            var screenArt = path.Contains("/UI/") || t.textureType is TextureImporterType.Sprite or TextureImporterType.GUI;
            if (screenArt)
            {
                t.GetSourceTextureWidthAndHeight(out var w, out var h);
                if (w * h < 1_500_000 || t.DoesSourceTextureHaveAlpha()) return false;
                // block compression needs sides in 4s, or the texture silently stays uncompressed
                if (w % 4 != 0 || h % 4 != 0) PadToFour(path, w, h);
            }
            var current = t.GetPlatformTextureSettings(Web);
            var want = new TextureImporterPlatformSettings
            {
                name = Web,
                overridden = true,
                maxTextureSize = Mathf.Min(screenArt ? 2048 : 1024, t.maxTextureSize),
                resizeAlgorithm = current.resizeAlgorithm,
                format = TextureImporterFormat.Automatic,
                textureCompression = TextureImporterCompression.Compressed, // DXT1/DXT5 (crunchable); HQ would be BC7
                crunchedCompression = !screenArt,
                compressionQuality = screenArt ? 100 : 50,
            };
            if (current.overridden && current.maxTextureSize == want.maxTextureSize && current.format == want.format &&
                current.textureCompression == want.textureCompression && current.crunchedCompression == want.crunchedCompression &&
                current.compressionQuality == want.compressionQuality) return false;
            t.SetPlatformTextureSettings(want);
            t.SaveAndReimport();
            return true;
        }

        /// <summary>
        /// Grows a picture to the next multiple of 4 on each side by repeating its edge pixels (2048×1143 becomes
        /// 2048×1144). The full-screen backgrounds are drawn cropped to cover, so the extra row never shows.
        /// </summary>
        private static void PadToFour(string path, int w, int h)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGB24, false);
            src.LoadImage(File.ReadAllBytes(path)); // decodes at the file's own size
            int pw = (w + 3) / 4 * 4, ph = (h + 3) / 4 * 4;
            var from = src.GetPixels32();
            var to = new Color32[pw * ph];
            for (var y = 0; y < ph; y++)
                for (var x = 0; x < pw; x++)
                    to[y * pw + x] = from[Mathf.Min(y, h - 1) * w + Mathf.Min(x, w - 1)];
            var dst = new Texture2D(pw, ph, TextureFormat.RGB24, false);
            dst.SetPixels32(to);
            File.WriteAllBytes(path, dst.EncodeToPNG());
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(dst);
            AssetDatabase.ImportAsset(path);
            Debug.Log($"Farmverse: {path} padded {w}×{h} → {pw}×{ph} so it can be compressed.");
        }

        /// <summary>Music at a lower bit rate (the browser decodes it; format and load type stay); effects as they are. True when changed.</summary>
        private static bool Sound(AudioImporter a, string path)
        {
            if (!path.Contains("/Music/")) return false;
            var current = a.GetOverrideSampleSettings(Web);
            var want = current;
            want.quality = 0.3f;
            if (a.ContainsSampleSettingsOverride(Web) && Mathf.Approximately(current.quality, want.quality)) return false;
            a.SetOverrideSampleSettings(Web, want);
            a.SaveAndReimport();
            return true;
        }

        /// <summary>Mesh compression (medium) on models that have none. True when changed.</summary>
        private static bool Model(ModelImporter m)
        {
            if (m.meshCompression != ModelImporterMeshCompression.Off) return false;
            m.meshCompression = ModelImporterMeshCompression.Medium;
            m.SaveAndReimport();
            return true;
        }
    }
}
