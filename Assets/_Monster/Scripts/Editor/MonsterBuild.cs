// 打包到 PICO（头显性能 / 美术验收版）：生成头显主场景 → 切 Android → 打 APK。
//   MonsterBuild.Prepare()：PICO 设置（透视 + 手势）、运行时 Shader.Find 用到的着色器加进“总是打包”、开帧时间统计、生成主场景
//   MonsterBuild.BuildApkLater()：下一帧开始打包（RunCommand 立即返回），结果写到 /tmp/monster_build.txt
using System;
using System.IO;
using System.Linq;
using ByteDance.PICO.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Monster.EditorTools
{
    public static class MonsterBuild
    {
        public const string MainScene = "Assets/_Monster/Scenes/Main.unity";
        const string PicoSettingPath = "Assets/Resources/PXR_ProjectSetting.asset";
        const string ResultFile = "/tmp/monster_build.txt";
        // 运行时用 Shader.Find 找的着色器：不被任何资源引用，必须手动加进“总是打包”
        static readonly string[] RuntimeShaders = { "Monster/CityToon", "Monster/CityGlow", "Monster/CityGlowAdd", "Monster/CityGround", "Monster/CityWater", "Monster/CityFade", "Monster/SealShield", "Monster/FxPuff", "Monster/ComicCard", "Monster/CreatureSkin", "Monster/SignCut", "Monster/SignGlow", "Monster/MonsterHand", "Monster/ComicNeon", "Monster/HurtVignette", "Monster/StationShield", "Monster/DuskSky" };

        public static string Prepare()
        {
            var c = AssetDatabase.LoadAssetAtPath<PXR_ProjectSetting>(PicoSettingPath);
            if (!c)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                c = ScriptableObject.CreateInstance<PXR_ProjectSetting>();
                c.handTrackingSupportType = HandTrackingSupport.ControllersAndHands;
                AssetDatabase.CreateAsset(c, PicoSettingPath);
            }
            c.portalInited = true; c.handTracking = true; c.videoSeeThrough = true;
            EditorUtility.SetDirty(c);
            // 总是打包的着色器
            var gs = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in RuntimeShaders)
            {
                var sh = Shader.Find(name);
                if (!sh) { Debug.LogError("[怪兽] 找不到着色器 " + name); continue; }
                bool has = false;
                for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) has = true;
                if (!has) { arr.InsertArrayElementAtIndex(arr.arraySize); arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh; }
            }
            // 11-09：手、电线、导弹、烟尘都是运行时 new Material + RenderMeshInstanced，打包时没有材质资产开实例化，
            // “剔除未用实例化变体”会把这些变体全剔掉 → 头显里画不出来（编辑器正常）。固定为全部保留（2 = Keep All）
            so.FindProperty("m_InstancingStripping").intValue = 2;
            so.ApplyModifiedProperties();
            PlayerSettings.enableFrameTimingStats = true;   // FrameTimingManager 才有 CPU / GPU 帧时间
            AssetDatabase.SaveAssets();
            BuildMainScene();
            return $"hand={c.handTracking} vst={c.videoSeeThrough} shaders={arr.arraySize}";
        }

        public static void BuildMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // 光照：和编辑器预览的黄昏一致
            var L = MonsterPreview.Look("dusk");
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = L.sun; sun.intensity = L.sunI; sun.shadows = LightShadows.Hard;
            sun.transform.rotation = Quaternion.Euler(L.sunEuler);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = L.sky * L.amb; RenderSettings.ambientEquatorColor = Color.Lerp(L.sky, L.ground, 0.5f) * L.amb; RenderSettings.ambientGroundColor = L.ground * L.amb;
            RenderSettings.skybox = null; RenderSettings.sun = sun;
            // XR：原点在地面，相机由头显驱动
            var origin = new GameObject("XR Origin").transform;
            var camGO = new GameObject("XR Camera") { tag = "MainCamera" };
            camGO.transform.SetParent(origin, false);
            camGO.transform.localPosition = new Vector3(0, 1.6f, 0);
            camGO.transform.localRotation = Quaternion.Euler(35, 0, 0);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 30; cam.fieldOfView = 70;
            var data = camGO.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            camGO.AddComponent<AudioListener>();
            var tpd = camGO.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(new InputAction("HeadPos", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            tpd.rotationInput = new InputActionProperty(new InputAction("HeadRot", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            var pxr = origin.gameObject.AddComponent<PXR_Manager>(); pxr.foveationLevel = FoveationLevel.Med;   // 固定注视点渲染：视野边缘降分辨率。11-17：High → Med（用户两次反馈描边闪；最高档时视野外圈分辨率太低，细线在那里一闪一闪）。真机日志这几轮稳定 90 帧；若 [怪兽][性能] 里“超过 20ms 的帧”变多就退回 High
            var mr = origin.gameObject.AddComponent<MonsterMR>(); mr.cam = cam;
            Directory.CreateDirectory(Path.GetDirectoryName(MainScene));
            EditorSceneManager.SaveScene(scene, MainScene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScene, true) };
        }

        /// <summary>下一帧切平台（第一次）或打包；结果追加到 /tmp/monster_build.txt</summary>
        public static void BuildApkLater() => EditorApplication.delayCall += () =>
        {
            string msg;
            try { msg = BuildApk(); } catch (Exception e) { msg = "Failed 异常 " + e.Message; }
            File.AppendAllText(ResultFile, DateTime.Now.ToString("HH:mm:ss ") + msg + "\n");
        };

        /// <summary>应用图标（11-09 用户选定 v3 霓虹爪痕）：默认图标 + Android 各尺寸 Legacy 图标都用这一张</summary>
        public static void ApplyIcon()
        {
            const string path = "Assets/_Monster/Art/Icon/icon_1024.png";
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti != null && (ti.textureCompression != TextureImporterCompression.Uncompressed || ti.mipmapEnabled))
            { ti.textureCompression = TextureImporterCompression.Uncompressed; ti.mipmapEnabled = false; ti.SaveAndReimport(); }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!tex) { Debug.LogWarning("[怪兽] 没找到图标 " + path); return; }
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { tex });
            var nt = UnityEditor.Build.NamedBuildTarget.Android;
            foreach (var kind in new[] { UnityEditor.Android.AndroidPlatformIconKind.Legacy, UnityEditor.Android.AndroidPlatformIconKind.Round })
            {
                var icons = PlayerSettings.GetPlatformIcons(nt, kind);
                foreach (var ic in icons) ic.SetTextures(tex);
                PlayerSettings.SetPlatformIcons(nt, kind, icons);
            }
        }

        /// <summary>启动画面（11-17 用户：去掉 Made with Unity，换成排版一样的 Made with Tripo —— 左边两行小字 MADE / WITH，右边官方 TRIPO 标志）。
        /// 仍然用 Unity 自带的启动画面（位置、大小、淡入淡出、深色背景都是同一套，所以排版和原来一致），只是关掉 Unity 标志、换成我们这一张
        /// （Art/Brand/made_with_tripo.png，透明底，由 Tools/brand/render.sh 合成）。图还没有的时候什么都不改</summary>
        public static void ApplySplash()
        {
            const string path = "Assets/_Monster/Art/Brand/made_with_tripo.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[怪兽] 没找到启动画面图 " + path + "，启动画面保持原样（Made with Unity）"); return; }
            if (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed)
            { ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.alphaIsTransparency = true; ti.SaveAndReimport(); }
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (!sp) { Debug.LogWarning("[怪兽] 启动画面图导入失败 " + path); return; }
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2f, sp) };
            // 11-17 用户：头显里开头看不到 Made with Tripo —— XR 下 Unity 不画上面这个“标志列表”，只认 Virtual Reality Splash Image 这一张贴图
            // （没填就画 Unity 标志；Unity 标志又被关了，所以什么都没有）。这里把头显专用的那张（不透明深色底、内容居中）填进去
            const string vrPath = "Assets/_Monster/Art/Brand/made_with_tripo_vr.png";
            var vi = AssetImporter.GetAtPath(vrPath) as TextureImporter;
            if (vi == null) { Debug.LogWarning("[怪兽] 没找到头显启动图 " + vrPath); return; }
            if (vi.textureType != TextureImporterType.Default || vi.mipmapEnabled || vi.textureCompression != TextureImporterCompression.Uncompressed || vi.npotScale != TextureImporterNPOTScale.None)
            { vi.textureType = TextureImporterType.Default; vi.mipmapEnabled = false; vi.textureCompression = TextureImporterCompression.Uncompressed; vi.npotScale = TextureImporterNPOTScale.None; vi.wrapMode = TextureWrapMode.Clamp; vi.sRGBTexture = true; vi.SaveAndReimport(); }
            var vt = AssetDatabase.LoadAssetAtPath<Texture2D>(vrPath);
            var all = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (!vt || all == null || all.Length == 0) { Debug.LogWarning("[怪兽] 头显启动图没设上"); return; }
            var so = new SerializedObject(all[0]); var prop = so.FindProperty("m_VirtualRealitySplashScreen");
            if (prop == null) { Debug.LogWarning("[怪兽] 这个版本的 Unity 没有 m_VirtualRealitySplashScreen"); return; }
            if (prop.objectReferenceValue != vt) { prop.objectReferenceValue = vt; so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets(); }
        }

        public static string BuildApk()
        {
            ApplyIcon(); ApplySplash();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android)) return "Failed 切换 Android 平台失败";
                return "Switched 已切到 Android，等编译完成后再执行一次打包";
            }
            Prepare();
            PlayerSettings.productName = "霓虹巨兽";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.monstercity.game");
            PlayerSettings.bundleVersion = "0.1." + DateTime.Now.ToString("MMdd");
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "../Builds"));
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, $"../Builds/MonsterCity_{DateTime.Now:MMdd_HHmm}.apk"));
            var rep = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { MainScene }, locationPathName = path, target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android, options = BuildOptions.None });
            var s = rep.summary;
            return $"{s.result} 用时 {s.totalTime.TotalMinutes:0.0} 分钟 APK {(File.Exists(path) ? new FileInfo(path).Length / 1048576f : 0):0.0} MB 错误 {s.totalErrors} → {path}";
        }
    }
}
