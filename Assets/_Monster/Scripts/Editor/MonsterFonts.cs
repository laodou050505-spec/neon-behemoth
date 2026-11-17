// 生成 TMP 字体资源（Resources/Fonts）：漫画对白中文 = 站酷快乐体，英文 = Bangers（都是 SIL OFL 开源字体，可商用、可打包）。
// 动态图集（缺字运行时补），但先把多语言表里出现的所有字预先烘进图集，真机上不用现生成，避免卡顿。
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Monster.EditorTools
{
    public static class MonsterFonts
    {
        [MenuItem("Monster/生成字体资源")]
        public static void Build()
        {
            Make("Assets/_Monster/Art/Fonts/ZCOOLKuaiLe-Regular.ttf", "Assets/_Monster/Resources/Fonts/ComicZH SDF.asset", 2048);
            Make("Assets/_Monster/Art/Fonts/Bangers-Regular.ttf", "Assets/_Monster/Resources/Fonts/ComicEN SDF.asset", 1024);
            // 告示纸界面（11-08）：集团公文 = 思源黑体粗（中英同一款）；涂鸦大字 = 站酷庆科黄油体 / Permanent Marker；手写批注 = 龙藏体 / Permanent Marker
            Make("Assets/_Monster/Art/Fonts/NotoSansSC-Bold.otf", "Assets/_Monster/Resources/Fonts/Corp SDF.asset", 2048);
            Make("Assets/_Monster/Art/Fonts/ZCOOLQingKeHuangYou-Regular.ttf", "Assets/_Monster/Resources/Fonts/GraffitiZH SDF.asset", 2048);
            Make("Assets/_Monster/Art/Fonts/LongCang-Regular.ttf", "Assets/_Monster/Resources/Fonts/NoteZH SDF.asset", 2048);
            Make("Assets/_Monster/Art/Fonts/PermanentMarker-Regular.ttf", "Assets/_Monster/Resources/Fonts/MarkerEN SDF.asset", 1024);
            var mk = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Monster/Resources/Fonts/MarkerEN SDF.asset");
            var gz = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Monster/Resources/Fonts/GraffitiZH SDF.asset");
            var corp = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Monster/Resources/Fonts/Corp SDF.asset");
            var nz = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Monster/Resources/Fonts/NoteZH SDF.asset");
            mk.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { gz, corp }; EditorUtility.SetDirty(mk);
            gz.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { corp }; EditorUtility.SetDirty(gz);
            nz.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { corp }; EditorUtility.SetDirty(nz);
            // 英文模式里偶尔有汉字（语言开关上的“中文”）：英文字体缺字时退到中文字体
            var en = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Monster/Resources/Fonts/ComicEN SDF.asset");
            var zh = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Monster/Resources/Fonts/ComicZH SDF.asset");
            en.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { zh }; EditorUtility.SetDirty(en);
            AssetDatabase.SaveAssets();
        }

        static void Make(string src, string dst, int atlas)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(src);
            if (!font) { Debug.LogError("[怪兽] 找不到字体 " + src); return; }
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(dst)) AssetDatabase.DeleteAsset(dst);
            var fa = TMP_FontAsset.CreateFontAsset(font, 72, 8, GlyphRenderMode.SDFAA, atlas, atlas, AtlasPopulationMode.Dynamic, true);
            fa.name = System.IO.Path.GetFileNameWithoutExtension(dst);
            AssetDatabase.CreateAsset(fa, dst);
            fa.atlasTextures[0].name = fa.name + " Atlas"; AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = fa.name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa);
            // 预烘：多语言表里所有中英文 + 常用标点数字
            string chars = Monster.Game.Loc.AllText() + "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz，。！？：；、“”‘’（）—…·▶!?.,:;'\"()-+/% ";
            fa.TryAddCharacters(new string(chars.Distinct().ToArray()), out string missing);
            var so = new SerializedObject(fa); var pr = so.FindProperty("m_ClearDynamicDataOnBuild");   // 打包时保留预烘的字（否则真机第一次显示要现生成）
            if (pr != null) { pr.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); } else Debug.LogWarning("[怪兽] 找不到 m_ClearDynamicDataOnBuild");
            EditorUtility.SetDirty(fa);
            Debug.Log($"[怪兽] 字体 {dst}：缺字 {(string.IsNullOrEmpty(missing) ? "无" : missing)}");
        }
    }
}
