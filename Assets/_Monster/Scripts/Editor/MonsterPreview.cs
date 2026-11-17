// 编辑器预览：生成一间简单的房间（地板、墙、窗、桌、柜）+ 灯光 + 城市，保存为 Scenes/Preview.unity；截图对比渲染图。
//   MonsterPreview.Build("dusk");  MonsterPreview.Shot("/tmp/a.png", pos, euler, fov)
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Monster.EditorTools
{
    public static class MonsterPreview
    {
        public const string ScenePath = "Assets/_Monster/Scenes/Preview.unity";

        public struct TimeLook
        {
            public Color sky, ground, sun, wall, floor, bg; public float sunI, amb; public Vector3 sunEuler; public bool night;
        }
        public static TimeLook Look(string time) => time switch
        {
            "night" => new TimeLook { sky = C("#6f62c8"), ground = C("#2a2050"), sun = C("#a9b6ff"), sunI = 0.75f, amb = 0.9f, sunEuler = new Vector3(55, -150, 0), wall = C("#2e2850"), floor = C("#3a2f3a"), bg = C("#120e26"), night = true },
            _ => new TimeLook { sky = C("#ffd6e2"), ground = C("#8a6a92"), sun = C("#ffd9a8"), sunI = 1.5f, amb = 1.1f, sunEuler = new Vector3(32, -145, 0), wall = C("#d79a9a"), floor = C("#9a6a50"), bg = C("#3a2a4a"), night = false },
        };
        static Color C(string h) => CityMaterials.Hex(h);

        [MenuItem("Monster/预览场景（黄昏）")] public static void BuildDusk() => Build("dusk");
        [MenuItem("Monster/预览场景（夜晚）")] public static void BuildNight() => Build("night");

        public static bool LargeRoom;
        public static void Build(string time = "dusk", int seed = 7, int towers = 3)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // 换场景后旧的程序网格 / 材质已被卸载，缓存要清掉重建
            CityMeshes.ClearCache(); CityMaterials.ClearCache();
            var L = Look(time);
            // 光
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = L.sun; sun.intensity = L.sunI; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(L.sunEuler);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = L.sky * L.amb; RenderSettings.ambientEquatorColor = Color.Lerp(L.sky, L.ground, 0.5f) * L.amb; RenderSettings.ambientGroundColor = L.ground * L.amb;
            // 程序天空只用来给玻璃 / 金属提供反射（相机背景仍是纯色）：黄昏粉橙、夜里深蓝紫
            var sky = SkyMaterial(L, sun);
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
            DynamicGI.UpdateEnvironment();
            // 房间（只在编辑器预览里有；头显里是真实房间透视）
            var room = new GameObject("Room_PreviewOnly").transform;
            // 和头显一致：默认按原地模式（半径 0.95m 的圆，楼 0.5 倍）；LargeRoom = 能走动的 2.6m 见方房间（楼 0.7 倍）
            float k = LargeRoom ? 0.7f : 0.5f;
            var inside = LargeRoom ? (System.Func<Vector2, bool>)(p => Mathf.Abs(p.x) < 1.24f && p.y > -1.24f && p.y < 1.36f) : CityLayout.Circle(Vector2.zero, 0.95f);
            var zones = CityLayout.FitRoom(inside, Vector2.zero, 1.62f, !LargeRoom).ConvertAll(z => new Zone(z.id, z.kind, z.origin, z.u, z.v, z.w / k, z.h / k, z.harbor) { furniture = z.furniture });
            Room(room, zones, L);
            // 城市
            var city = CityLayout.Build(zones, new LevelSpec { towers = towers, power = 1 }, seed);
            var b = CityBuilder.Build(city, L.night, null, k);
            // 相机（玩家站在桌前）
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = L.bg; cam.nearClipPlane = 0.03f; cam.fieldOfView = 70;
            camGo.AddComponent<UniversalAdditionalCameraData>();
            camGo.transform.SetPositionAndRotation(new Vector3(0, 1.62f, 0.05f), Quaternion.Euler(42, 0, 0));
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"预览场景：楼 {city.buildings.Count} 塔 {city.towers.Count} 站 {city.stations.Count}");
        }

        static void Room(Transform room, System.Collections.Generic.List<Zone> zones, TimeLook L)
        {
            Material M(string hex) => CityMaterials.Solid(hex);
            var wood = M("#7a5640"); var leg = M("#3a2a26");
            Transform Box(string n, Material m, Vector3 pos, Vector3 size)
            {
                var go = new GameObject(n); go.transform.SetParent(room, false);
                go.transform.position = pos; go.transform.localScale = size;
                go.AddComponent<MeshFilter>().sharedMesh = CityMeshes.Box();
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
                mr.receiveShadows = false;   // 假房间代替真实桌面 / 地板：影子由城区地面层（CityGround）画，和真机一致
                return go.transform;
            }
            var floorM = new Material(CityMaterials.Toon); floorM.SetColor("_BaseColor", L.floor); floorM.SetFloat("_OutlineWidth", 0);
            Box("Floor", floorM, new Vector3(0, -0.02f, 1.5f), new Vector3(7, 0.02f, 6));
            var wallM = new Material(CityMaterials.Toon); wallM.SetColor("_BaseColor", L.wall); wallM.SetFloat("_OutlineWidth", 0);
            Box("WallBack", wallM, new Vector3(0, 0, 3.4f), new Vector3(7, 2.7f, 0.05f));
            Box("WallLeft", wallM, new Vector3(-3.0f, 0, 1.5f), new Vector3(0.05f, 2.7f, 6));
            // 窗（后墙）：亮的窗玻璃
            var glass = CityMaterials.Glow(L.night ? "#3a3f8a" : "#ffd1a8", L.night ? 0.9f : 1.15f);
            Box("Window", glass, new Vector3(-0.2f, 1.0f, 3.36f), new Vector3(2.4f, 1.3f, 0.02f));
            var frame = M("#e8e0d8");
            Box("WinFrameV", frame, new Vector3(-0.2f, 1.0f, 3.35f), new Vector3(0.04f, 1.3f, 0.02f));
            Box("WinFrameH", frame, new Vector3(-0.2f, 1.62f, 3.35f), new Vector3(2.4f, 0.04f, 0.02f));
            foreach (var z in zones)
            {
                if (z.furniture) continue;   // 虚拟家具由 CityBuilder 生成（和头显里一致）
                var c = z.Center; float F = CityConst.Fringe;
                if (z.kind == ZoneKind.Table)
                {
                    Box("TableTop", wood, new Vector3(c.x, c.y - 0.045f, c.z), new Vector3(z.w + F * 2 + 0.04f, 0.045f, z.h + F * 2 + 0.04f));
                    foreach (var dx in new[] { -1, 1 }) foreach (var dz in new[] { -1, 1 })
                        Box("Leg", leg, new Vector3(c.x + dx * (z.w / 2 + F - 0.02f), 0, c.z + dz * (z.h / 2 + F - 0.02f)), new Vector3(0.05f, c.y - 0.045f, 0.05f));
                }
                if (z.kind == ZoneKind.Cabinet)
                    Box("Cabinet", M("#5a4a5e"), new Vector3(c.x, 0, c.z), new Vector3(z.w + F * 2 + 0.03f, c.y - 0.01f, z.h + F * 2 + 0.03f));
            }
        }

        // 样板楼展示：只放正式模块做的楼（玻璃楼 + 海滨酒店各两栋，高矮不同），一块路面，烘一次反射探针。
        // 用来单独检查模型和材质，不被旁边还没换的程序方块楼干扰。
        [MenuItem("Monster/样板楼展示（黄昏）")] public static void ShowcaseDusk() => Showcase("dusk");
        public static void Showcase(string time = "dusk", string lineup = "a")
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CityMeshes.ClearCache(); CityMaterials.ClearCache(); ModuleSet.ClearCache();
            var L = Look(time);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = L.sun; sun.intensity = L.sunI; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(L.sunEuler);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = L.sky * L.amb; RenderSettings.ambientEquatorColor = Color.Lerp(L.sky, L.ground, 0.5f) * L.amb; RenderSettings.ambientGroundColor = L.ground * L.amb;
            var sky = SkyMaterial(L, sun);
            RenderSettings.skybox = sky; RenderSettings.sun = sun;

            // 一个 3×1 街区的小地块：路面 + 人行道（真实尺寸的 1:60 城市比例，和游戏里一致）
            // lineup a：玻璃楼 + 海滨酒店；b：其余 7 种楼一字排开（写字楼 → 加油站）
            bool B = lineup == "b";
            var zone = B ? new Zone("show", ZoneKind.Table, new Vector3(-0.66f, 0.76f, 1.2f), Vector3.right, Vector3.back, 1.32f, 0.4f)
                  : new Zone("show", ZoneKind.Table, new Vector3(-0.45f, 0.76f, 1.2f), Vector3.right, Vector3.back, 0.9f, 0.4f);
            var city = new CityLayout { zones = new System.Collections.Generic.List<Zone> { zone } };
            int id = 1;
            void Add(Style s, float a, float b, int floors, float fw, float fd)
            {
                var info = CityLayout.Styles[s];
                var bd = new Building { id = id++, zone = zone, a = a, b = b, kind = BuildingKind.House, style = s, h = floors, maxH = floors, modH = info.modH, fw = fw, fd = fd, seed = id * 97 };
                city.buildings.Add(bd); city.byId[bd.id] = bd;
            }
            if (B)
            {
                Add(Style.Office, 0.09f, 0.2f, 8, 0.15f, 0.13f);
                Add(Style.Apt, 0.25f, 0.2f, 6, 0.09f, 0.16f);
                Add(Style.Garage, 0.41f, 0.2f, 5, 0.15f, 0.12f);
                Add(Style.Shop, 0.565f, 0.24f, 1, 0.08f, 0.075f);
                Add(Style.Villa, 0.69f, 0.24f, 2, 0.075f, 0.065f);
                Add(Style.Warehouse, 0.88f, 0.22f, 1, 0.16f, 0.11f);
                Add(Style.Gas, 1.12f, 0.22f, 1, 0.13f, 0.11f);
            }
            else
            {
                Add(Style.Glass, 0.12f, 0.2f, 12, 0.15f, 0.15f);
                Add(Style.Glass, 0.33f, 0.2f, 8, 0.14f, 0.14f);
                Add(Style.Deco, 0.56f, 0.22f, 6, 0.17f, 0.12f);
                Add(Style.Deco, 0.78f, 0.22f, 4, 0.15f, 0.11f);
            }
            CityBuilder.Build(city, L.night);
            // 桌面（地面现在是透明的，只有马路，楼直接立在桌上）
            var top = new GameObject("TableTop_PreviewOnly");
            top.transform.position = zone.Center + Vector3.down * 0.045f;
            top.transform.localScale = new Vector3(zone.w + 0.3f, 0.045f, zone.h + 0.3f);
            top.AddComponent<MeshFilter>().sharedMesh = CityMeshes.Box();
            var topR = top.AddComponent<MeshRenderer>(); topR.sharedMaterial = CityMaterials.Solid("#7a5640"); topR.receiveShadows = false;

            // 反射探针：烘焙成立方体贴图，玻璃和金属才有天空反光
            var probe = new GameObject("Reflection").AddComponent<ReflectionProbe>();
            probe.transform.position = zone.Center + Vector3.up * 0.3f;
            probe.size = new Vector3(4, 3, 4);
            probe.mode = ReflectionProbeMode.Custom;
            probe.resolution = 256;
            Directory.CreateDirectory("Assets/_Monster/Scenes");
            Lightmapping.BakeReflectionProbe(probe, "Assets/_Monster/Scenes/ShowcaseProbe.exr");
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>("Assets/_Monster/Scenes/ShowcaseProbe.exr");

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox; cam.nearClipPlane = 0.02f; cam.fieldOfView = 50;
            camGo.AddComponent<UniversalAdditionalCameraData>();
            camGo.transform.SetPositionAndRotation(new Vector3(0, 1.15f, 0.25f), Quaternion.Euler(22, 0, 0));
            EditorSceneManager.SaveScene(scene, "Assets/_Monster/Scenes/Showcase.unity");
        }

        // 渐变天空（黄昏：紫 → 粉橙地平线；夜晚：深蓝 → 紫红地平线），太阳光晕跟着主光方向
        static Material SkyMaterial(TimeLook L, Light sun)
        {
            var m = new Material(Shader.Find("Monster/GradientSky")) { name = "Sky_" + (L.night ? "night" : "dusk") };
            m.SetColor("_Top", L.night ? C("#0c0b2a") : C("#4a3a8a"));
            m.SetColor("_Horizon", L.night ? C("#5a2f6e") : C("#ffa08a"));
            m.SetColor("_Ground", L.night ? C("#0a0814") : C("#3a2a34"));
            m.SetColor("_SunColor", L.night ? C("#6f6aa8") : C("#ffd09a"));
            m.SetVector("_SunDir", -sun.transform.forward);
            m.SetFloat("_SunSize", L.night ? 24 : 8);
            m.SetFloat("_Exposure", L.night ? 0.8f : 1.1f);
            return m;
        }

        // 临时相机截图（不开后期，和真机 MR 一致）
        public static void Shot(string path, Vector3 pos, Vector3 euler, float fov = 70, int w = 1600, int h = 900)
        {
            var go = new GameObject("__CaptureCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main) cam.CopyFrom(main);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler));
            cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 60;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
        }
    }
}
