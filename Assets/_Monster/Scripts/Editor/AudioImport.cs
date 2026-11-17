// 音频导入：音乐流式 Vorbis（省内存），音效 / 循环解压到内存 ADPCM 单声道（零延迟）
using UnityEditor;
using UnityEngine;

namespace Monster.EditorTools
{
    public class AudioImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            var p = assetPath.Replace((char)92, (char)47);
            if (!p.Contains("/_Monster/Resources/Audio/")) return;
            var ai = (AudioImporter)assetImporter;
            bool music = p.Contains("/music_");
            ai.forceToMono = !music;
            var s = new AudioImporterSampleSettings
            {
                loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad,
                compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM,
                quality = music ? 0.6f : 1f,
                sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate,
            };
            ai.defaultSampleSettings = s;
            ai.SetOverrideSampleSettings("Android", s);
        }
    }
}
