// 漫画格贴图导入：不透明 sRGB、开 mipmap（头显里远看不闪）、夹边，安卓用 ASTC 6×6（1280 的图约 0.7MB）
using UnityEditor;
using UnityEngine;

namespace Monster.EditorTools
{
    public class ComicImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var ap = assetPath.Replace((char)92, (char)47); bool title = ap.Contains("/Resources/Title/") || ap.Contains("/Resources/UI/"); if (!ap.Contains("/Resources/Comic/") && !title) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaSource = title ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None; ti.alphaIsTransparency = title;
            ti.npotScale = TextureImporterNPOTScale.None; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.anisoLevel = 4; ti.maxTextureSize = 2048;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Android", overridden = true, maxTextureSize = 2048, format = title ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6 });
        }
    }
}
