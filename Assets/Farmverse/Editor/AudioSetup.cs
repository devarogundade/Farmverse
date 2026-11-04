#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// Import options for the files under Content/Audio (motion plan, "Sound and music assets"):
    /// effects load into memory, ambience stays compressed, music streams.
    /// </summary>
    public sealed class AudioSetup : AssetPostprocessor
    {
        public const string Root = "Assets/Farmverse/Content/Audio";
        public const string Effects = Root + "/Sfx";
        public const string Music = Root + "/Music";

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root + "/", StringComparison.Ordinal)) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            var music = assetPath.StartsWith(Music + "/", StringComparison.Ordinal);
            var ambience = Path.GetFileName(assetPath).StartsWith("amb_", StringComparison.Ordinal);
            importer.forceToMono = !music;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = music ? 0.6f : 0.7f;
            settings.loadType = music ? AudioClipLoadType.Streaming : ambience ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 44100;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = music;
        }
    }
}
