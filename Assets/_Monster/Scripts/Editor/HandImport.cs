// 巨兽之手模型导入设置（Resources/Hand/，Tools/blender_hand.py 生成）：保留蒙皮和 26+1 根骨骼（Generic、不建 Avatar），
// 不导入材质 / 动画，法线按文件（主体平滑、骨刺和爪是硬边），网格不压缩（骨甲轮廓靠 UV 里的连续参数，压缩会让边缘抖）。
using UnityEditor;

namespace Monster.EditorTools
{
    public class HandImport : AssetPostprocessor
    {
        bool IsHand => assetPath.Replace('\\', '/').Contains("/Resources/Hand/");

        void OnPreprocessModel()
        {
            if (!IsHand) return;
            var m = (ModelImporter)assetImporter;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            m.importAnimation = false;
            m.animationType = ModelImporterAnimationType.Generic;
            m.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            m.optimizeGameObjects = false;
            m.importBlendShapes = false;
            m.importCameras = false; m.importLights = false;
            m.importNormals = ModelImporterNormals.Import;
            m.meshCompression = ModelImporterMeshCompression.Off;
            m.globalScale = 1; m.useFileScale = true;
            m.isReadable = false;
        }
    }
}
