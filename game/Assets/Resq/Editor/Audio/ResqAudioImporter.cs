using UnityEditor;
using UnityEngine;

namespace Resq.EditorTools
{
    /// <summary>
    /// Quest import rules for Resources/ResqAudio (tools/audio/synth.py): mono Vorbis everywhere; music and ambience
    /// loops stream from disk so they don't sit decompressed in memory; short effects decompress on load so they
    /// start instantly.
    /// </summary>
    public sealed class ResqAudioImporter : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.Contains("/ResqAudio/")) return;
            var imp = (AudioImporter)assetImporter;
            imp.forceToMono = true;
            imp.loadInBackground = true;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool bed = name.StartsWith("music_") || name.StartsWith("amb_");
            var s = imp.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = bed ? 0.45f : 0.6f;
            s.loadType = bed ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            imp.defaultSampleSettings = s;
        }
    }
}
