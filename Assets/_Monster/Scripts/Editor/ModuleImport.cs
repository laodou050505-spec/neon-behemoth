// 楼模块导入设置：Resources/Modules/ 下的 FBX 读写开启、不导入材质 / 动画、法线按导入（P2 的硬边）；
// 导入后把平滑法线烘进 UV3（描边沿它外扩，硬边处不会裂开）。贴图 2K、开 mipmap、高质量压缩。
using UnityEditor;
using UnityEngine;

namespace Monster.EditorTools
{
    public class ModuleImport : AssetPostprocessor
    {
        bool IsModule => assetPath.Replace('\\', '/').Contains("/Resources/Modules/");

        void OnPreprocessModel()
        {
            if (!IsModule) return;
            var m = (ModelImporter)assetImporter;
            m.isReadable = true;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            m.importAnimation = false;
            m.animationType = ModelImporterAnimationType.None;
            m.importCameras = false; m.importLights = false;
            m.importNormals = ModelImporterNormals.Import;
            m.meshCompression = ModelImporterMeshCompression.Off;
            m.globalScale = 1; m.useFileScale = true;
            m.bakeAxisConversion = true;
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!IsModule) return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh) CityMeshes.BakeSmoothNormals(mf.sharedMesh);
        }

        void OnPreprocessTexture()
        {
            if (!IsModule) return;
            var t = (TextureImporter)assetImporter;
            var file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            // normal = 法线贴图；mask（金属 / AO / 光滑度）是线性数据，不能做 sRGB 转换
            if (file == "normal") t.textureType = TextureImporterType.NormalMap;
            t.sRGBTexture = file != "mask" && file != "normal";
            t.maxTextureSize = 2048;
            t.mipmapEnabled = true;
            t.textureCompression = TextureImporterCompression.CompressedHQ;
            t.anisoLevel = 4;
        }
    }
}
