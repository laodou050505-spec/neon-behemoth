// 头显入口（性能 / 美术验收版，玩法接入之前）：
//   · 开 PICO 视频透视（相机清屏全透明，真实房间就是背景），追踪原点设为地面
//   · 头显追踪起来后，把玩家此刻的位置 / 朝向对齐成原点、面朝 +Z，再在玩家面前生成城市
//     （还没接房间扫描：用默认布局——面前一张虚拟沙盘桌、左边一个柜子、右边地上一片港口城区）
//   · 性能读数：每 5 秒往 logcat 打一行（帧率、CPU / GPU 帧时间），视野左下角也显示一行小字
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.XR;
using ByteDance.PICO.XR;

namespace Monster
{
    public class MonsterMR : MonoBehaviour
    {
        public Camera cam;
        public bool night;
        [System.NonSerialized] public bool showStats = false;   // 不序列化：场景里存过的 true 不会再把它打开   // 11-08 用户：不要显示帧率（只写日志）
        [Tooltip("开局关卡（Level.All 下标，0 = 教学）；-1 = 只看城市不接玩法")] public int level = 0;
        [Tooltip("城市比例：楼缩小、同样面积放更多楼（11-08 用户：城市有点小）")] public float cityScale = 0.7f;
        public Monster.Game.MonsterGame game;
        [Tooltip("开局先显示标题招牌（选关 / 语言）")] public bool showTitle = true;
        /// <summary>重开场景时直接进这一关（−1 = 回标题）</summary>
        public static int PendingLevel = -1;
        public static float CityTopY = -1;   // 城市最高楼顶（世界 y），没有城市时 -1
        public static float TableY = -1;      // 主城区所在桌面的高度（世界 y）；重开关卡时沿用上一次的（同一个房间），界面纸不能摆到桌面以下
        public static bool CityShown;          // 城市已经长出来（简报 / 漫画时城市还藏着，界面不用避让）
        Monster.Game.MonsterInput inp; CityBuilder builderRef; int curLevel; bool resultShown;
        [Tooltip("开局先放漫画（讲剧情 + 教操作）")] public bool intro = true;
        bool aligned, floorSet, built; float retry, waitAlign;
        TextMesh stats;

        void Start()
        {
            ApplyPassthrough();
            if (showStats) MakeStats();
            StartCoroutine(BuildWhenReady());
        }

        void OnApplicationPause(bool paused) { if (!paused) ApplyPassthrough(); }

        void ApplyPassthrough()
        {
            if (Application.isEditor || !XRSettings.isDeviceActive) return;
            ByteDance.PICO.XR.PXR_Manager.EnableVideoSeeThrough = true;
            if (cam) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0); }
            // 11-13 用户：城市地面不要闪 —— 深度精度和 近裁剪面 成正比：0.02 → 0.06（眼前 6cm 内本来就没有要画的东西），远裁剪面 30 → 12，贴地各层之间的深度余量大三倍
            if (cam) { cam.nearClipPlane = 0.06f; cam.farClipPlane = 12f; }
        }

        /// <summary>把 XR Origin 旋转 / 平移，使玩家此刻的头在原点正上方、面朝 +Z（地面高度不变）</summary>
        public void Align()
        {
            var origin = transform;
            var lp = origin.InverseTransformPoint(cam.transform.position);
            var lf = Quaternion.Inverse(origin.rotation) * cam.transform.forward; lf.y = 0;
            if (lf.sqrMagnitude < 1e-4f) return;
            float lyaw = Mathf.Atan2(lf.x, lf.z) * Mathf.Rad2Deg;
            // 有矩形安全区：朝向吸附到矩形最接近玩家面朝的那条边，城市和安全区平行
            try
            {
                var dim = ByteDance.PICO.XR.PXR_Boundary.GetDimensions(ByteDance.PICO.XR.BoundaryType.PlayArea);
                var pts = dim.x > .1f && dim.z > .1f ? ByteDance.PICO.XR.PXR_Boundary.GetGeometry(ByteDance.PICO.XR.BoundaryType.PlayArea) : null;
                if (pts != null && pts.Length >= 3) { var e = pts[1] - pts[0]; float ry = Mathf.Atan2(e.x, e.z) * Mathf.Rad2Deg; lyaw = ry + Mathf.Round((lyaw - ry) / 90f) * 90f; }
            }
            catch (System.Exception) { }
            var rot = Quaternion.Euler(0, -lyaw, 0);
            origin.rotation = rot;
            var p = new Vector3(0, lp.y, 0) - rot * lp;
            origin.position = new Vector3(p.x, 0, p.z);
            aligned = true;
        }

        /// <summary>
        /// 系统安全区（11-08 用户：场景范围必须在安全区内）。返回“点是否在安全区内”的判断、玩家站的位置、是否原地模式。
        /// 有矩形安全区（PlayArea）就用矩形；只有外轮廓（原地模式）就用外轮廓多边形本身；都读不到（编辑器）按以玩家为中心 2.6m 见方。
        /// </summary>
        (System.Func<Vector2, bool> inside, Vector2 player, bool stationary) SafeArea()
        {
            var head = cam ? cam.transform.position : new Vector3(0, 1.6f, 0);
            var player = new Vector2(head.x, head.z);
            if (XRSettings.isDeviceActive)
                try
                {
                    var dim = PXR_Boundary.GetDimensions(BoundaryType.PlayArea);
                    var pts = dim.x > .1f && dim.z > .1f ? PXR_Boundary.GetGeometry(BoundaryType.PlayArea) : null;
                    bool rect = pts != null && pts.Length >= 3;
                    if (!rect) pts = PXR_Boundary.GetGeometry(BoundaryType.OuterBoundary);
                    if (pts != null && pts.Length >= 3)
                    {
                        var poly = new System.Collections.Generic.List<Vector2>();
                        foreach (var p in pts) { var w = transform.TransformPoint(p); poly.Add(new Vector2(w.x, w.z)); }
                        // 原地模式的外轮廓是圆：用内切圆（比多边形保守），圆心取轮廓中心
                        if (!rect)
                        {
                            var c = Vector2.zero; foreach (var q in poly) c += q; c /= poly.Count;
                            float r = float.MaxValue; foreach (var q in poly) r = Mathf.Min(r, (q - c).magnitude);
                            Debug.Log($"[怪兽] 安全区：原地模式，半径 {r:0.00}m");
                            return (CityLayout.Circle(c, r), player, true);
                        }
                        Debug.Log($"[怪兽] 安全区：矩形 {dim.x:0.00}×{dim.z:0.00}m");
                        return (CityLayout.Polygon(poly), player, false);
                    }
                }
                catch (System.Exception e) { Debug.LogWarning("[怪兽] 读不到安全区：" + e.Message); }
            Debug.Log("[怪兽] 没有安全区数据，按 2.6m 见方摆放");
            return (p => Mathf.Abs(p.x - player.x) < 1.3f && p.y - player.y > -1.3f && p.y - player.y < 1.3f, player, false);
        }

        IEnumerator BuildWhenReady()
        {
            CityTopY = -1; CityShown = false;   // 静态量：上一局的城市高度不能带到标题
            Traffic.Halt = false;
            Monster.Game.Progress.Migrate();
            Shader.SetGlobalVector("_Blackout", Vector4.zero); Shader.SetGlobalFloat("_Relight", 0);   // 全局量跨场景保留：上一局结局里熄的灯要点回来
            Monster.Game.GameSettings.Apply();   // 音量设置（“再来一次”直接进关不经过标题，也要生效）
            // 等头显追踪起来（最多 3 秒），对齐后再生成，城市才会出现在玩家面前
            while (!aligned && waitAlign < 3f && XRSettings.isDeviceActive) { waitAlign += Time.unscaledDeltaTime; yield return null; }
            yield return null;
            var t0 = Time.realtimeSinceStartup;
            Mark("对齐完成");
            CityMeshes.ClearCache(); CityMaterials.ClearCache(); ModuleSet.ClearCache();
            // 头显输入（标题、漫画、玩法共用一个）
            inp = gameObject.AddComponent<Monster.Game.MonsterInput>(); inp.cam = cam; inp.origin = transform;
            Mark("输入");
            roomTable = gameObject.AddComponent<RoomTable>();   // 后台读系统空间扫描里的桌子（选关时就读好了）
            // 标题招牌：选关 / 语言；从结算“再来一次 / 下一夜”回来的直接进关
            int lvIdx = PendingLevel >= 0 ? PendingLevel : level;
            if (PendingLevel < 0 && showTitle && level >= 0)
            {
                var ts = gameObject.AddComponent<Monster.Game.TitleScreen>(); ts.cam = cam; ts.input = inp;
                // 11-12 用户：一开始手还是会定住几秒。真机日志：启动时有一帧 5.6 秒 —— 标题界面第一次出现时字体、招牌贴图、音乐都在那几帧里加载。
                // 巨兽之手等这些重活干完（标题建好再过几帧）才创建：加载期间画面上没有手，就不会出现“手出来了却不跟着动”。
                // 每个阶段的耗时打进日志（[怪兽][启动]），下次真机能看到到底是哪一步慢
                for (int f = 0; f < 6; f++) { yield return null; Mark("标题第 " + (f + 1) + " 帧"); }
                if (!handView) { handView = gameObject.AddComponent<Monster.Game.HandView>(); handView.input = inp; handView.debugRage = 0; }
                yield return null; Mark("巨兽之手");
                while (ts.chosen < 0)
                {
                    // 标题界面期间桌子一识别到就记下桌面高度（界面纸不能低于桌面，见 NoticeScreen.MinY）
                    if (TableY < 0 && roomTable && roomTable.Done && roomTable.Found && cam && roomTable.TryFront(transform, cam.transform.position, cam.transform.forward, out var rt0)) TableY = rt0.center.y;
                    yield return null;
                }
                handView.hidden = true;   // 11-12 用户：选关后要加载城市，加载彻底结束（城市出场完）之前不画手
                lvIdx = ts.chosen; Destroy(ts);
                yield return null;
            }
            PendingLevel = -1; level = lvIdx;
            var lv = level >= 0 ? Monster.Game.Level.All[Mathf.Clamp(level, 0, Monster.Game.Level.All.Length - 1)] : null;
            int lvi = lv != null ? System.Array.IndexOf(Monster.Game.Level.All, lv) : -1;
            // ① 先出画面（11-09 用户：开始游戏转漫画时卡一下、头晕）：简报卡 / 漫画马上出来，不等城市
            introDone = false; Monster.Game.BriefCard bc = null; Monster.Game.ComicIntro ci = null;
            if (lv != null && !lv.tutorial) { bc = gameObject.AddComponent<Monster.Game.BriefCard>(); bc.cam = cam; bc.input = inp; bc.level = lvi; bc.ready = false; bc.onDone = () => introDone = true; }
            else if (lv != null && intro && Monster.Game.GameSettings.Intro) { ci = gameObject.AddComponent<Monster.Game.ComicIntro>(); ci.cam = cam; ci.input = inp; ci.onDone = () => introDone = true; }
            else introDone = true;
            yield return null;
            // 11-09 用户：城市要在正前方 —— 启动时对齐过一次朝向，但玩家在标题界面可能已经转身了：选完关、生成城市前按当前面朝方向再对齐一次
            Align();
            yield return null;
            var (inside, player, stationary) = SafeArea();
            // 真桌子优先（11-09 用户：面前有桌子却识别不了）：系统空间扫描里正前方有桌子 → 城市直接摆在真桌面上；没有 → 虚拟桌
            for (float w8 = 0; roomTable && !roomTable.Done && w8 < 3f; w8 += Time.unscaledDeltaTime) yield return null;
            System.Collections.Generic.List<Zone> zones;
            var headNow = cam ? cam.transform.position : new Vector3(0, 1.6f, 0);
            if (roomTable && roomTable.Found && roomTable.TryFront(transform, headNow, cam ? cam.transform.forward : Vector3.forward, out var realTable))
            {
                zones = new System.Collections.Generic.List<Zone> { RoomTable.ToZone(realTable, headNow) };
                Debug.Log($"[怪兽] 布局：用真实桌子 {zones[0].w:0.00}×{zones[0].h:0.00}m 高 {zones[0].origin.y:0.00}m（{roomTable.Status}）");
            }
            else
            {
                if (roomTable) Debug.Log("[怪兽] 没用真实桌子：" + roomTable.Status);
                zones = CityLayout.FitRoom(inside, player, cam ? cam.transform.position.y : 1.6f, stationary, Monster.Game.GameSettings.CityHeightOffset);
                if (DebugRealTable) foreach (var dz in zones) if (dz.kind == ZoneKind.Table) { dz.furniture = false; dz.origin += Vector3.up * 0.004f; }   // 编辑器验收：模拟“真实桌面 + 城市地垫”
            }
            // 关卡越往后场地越大：玩家左右两边加虚拟柜子 / 小桌（真桌子、虚拟桌都适用；放不下就不加）
            if (lv != null)
            {
                if (lv.sides <= 0) zones.RemoveAll(z => z.kind == ZoneKind.Cabinet && z.furniture);   // 第一夜只有面前这一张桌（和地板河）
                else { string sideLog = CityLayout.AddSides(zones, inside, new Vector3(player.x, 0, player.y), lv.sides); if (sideLog.Length > 0) Debug.Log("[怪兽] 布局：" + sideLog); }
            }
            // 暂停钮的位置：玩家正前方落在主城区近边上的哪一段（城市边界生成时在那一段留出位置，不挂串灯、不立牌子）
            CityBuilder.PauseA = lv != null && zones.Count > 0 ? Vector3.Dot(headNow - zones[0].origin, zones[0].u) : -1;
            TableY = zones.Count > 0 ? zones[0].origin.y : -1;
            // 小场地楼再缩小一点（同样面积放更多楼）
            float area = 0; foreach (var z in zones) area += z.w * z.h;
            if (area < 0.6f) cityScale = Mathf.Min(cityScale, 0.5f);
            // 按设计尺寸（真实尺寸 ÷ 比例）生成，生成完整体缩回真实尺寸：楼更小、街区更多
            float k = Mathf.Clamp(cityScale, 0.4f, 1f);
            var design = new System.Collections.Generic.List<Zone>();
            foreach (var z in zones) design.Add(new Zone(z.id, z.kind, z.origin, z.u, z.v, z.w / k, z.h / k, z.harbor) { furniture = z.furniture });
            // 11-11 用户：每一关城市都一样 —— 每关用不同的随机种子（楼型、塔和电站的位置都不同；同一关每次进去一样，方便背板）
            var city = CityLayout.Build(design, lv != null ? lv.Spec : new LevelSpec { towers = 3, power = 1 }, 7 + Mathf.Max(0, lvi) * 17);
            yield return null;
            // ② 分帧生成城市；生成期间相机只画界面层（漫画 / 简报），看不到楼一块块冒出来
            int mask = cam ? cam.cullingMask : ~0;
            CityBuilder builder = null;
            var gen = CityBuilder.BuildAsync(city, night || lv != null, k, b => builder = b);   // 11-12：黄昏将入夜，所有关都点亮窗灯和霓虹
            while (gen.MoveNext())
            {
                if (cam) cam.cullingMask = 1 << UILayer;
                if (ci) SetLayer(ci.Root, UILayer); if (bc) SetLayer(bc.Root, UILayer);
                yield return gen.Current;
            }
            // 记下城市最高楼顶（世界 y）：结算纸 / 暂停菜单要摆在它上面，不能插进楼里
            CityTopY = -1;
            foreach (var bv in builder.root.GetComponentsInChildren<BuildingView>(true))
                foreach (var r in bv.GetComponentsInChildren<Renderer>(true)) CityTopY = Mathf.Max(CityTopY, r.bounds.max.y);
            Debug.Log($"[怪兽] 城市生成完：{Time.realtimeSinceStartup - t0:0.0} 秒，楼 {city.buildings.Count} 栋");
            if (lv != null) StartGame(lv, city, builder);   // 城市保持激活（教学牌 / 警灯牌要按城市包围盒摆位），只是相机还看不到
            yield return null;
            {
                // 预热期间相机仍然只画界面层
                var pw = Prewarm(builder);
                while (pw.MoveNext())
                {
                    if (cam) cam.cullingMask = 1 << UILayer;
                    if (ci) SetLayer(ci.Root, UILayer); if (bc) SetLayer(bc.Root, UILayer);
                    yield return pw.Current;
                }
            }
            if (bc) bc.ready = true;
            built = true;
            // ③ 漫画 / 简报看完 → 城市出场（这之前相机一直只画界面层）
            while (!introDone)
            {
                if (cam) cam.cullingMask = 1 << UILayer;
                if (ci) SetLayer(ci.Root, UILayer); if (bc) SetLayer(bc.Root, UILayer);
                yield return null;
            }
            if (cam) cam.cullingMask = mask;
            if (lv != null) yield return Reveal(builder, viewRef, city);
        }
        const int UILayer = 5;
        static float markT = -1;
        /// <summary>启动分段计时：打印离上一个标记过了多少毫秒</summary>
        public static void Mark(string what)
        {
            float now = Time.realtimeSinceStartup;
            Debug.Log($"[怪兽][启动] {what}：+{(markT < 0 ? 0 : (now - markT) * 1000):0}ms（启动后 {now:0.0}s，第 {Time.frameCount} 帧）");
            markT = now;
        }
        bool introDone; Monster.Game.GameView viewRef; RoomTable roomTable; Monster.Game.HandView handView;

        /// <summary>
        /// 预热（11-11 用户：刚进游戏时虚拟手卡一下）。真机日志：城市出场的第一帧 85～90ms —— 整座城的网格、贴图、着色器都是那一帧第一次送进显卡。
        /// 趁玩家还在看漫画 / 简报（相机只画界面层，城市看不见）先把这些做掉，而且切成小份，每帧只做一点：
        ///  ① 网格：运行时生成的网格（合批大网格、程序楼、路面）逐个上传显存，一帧最多用 4ms；
        ///  ② 着色器：每种材质在眼前画一个 0.1 毫米的小方块（界面层，肉眼看不到），一帧 5 个 —— 显卡就会把这种材质要用的着色器程序编译好；
        ///     实例化绘制（车流、烟尘、碎块）是另一套程序，每种着色器再各画一次实例化的。
        /// </summary>
        IEnumerator Prewarm(CityBuilder builder)
        {
            float t0 = Time.realtimeSinceStartup; int frames = 0; float worst = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var seen = new System.Collections.Generic.HashSet<Mesh>();
            foreach (var mf in builder.root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (!m || !seen.Add(m)) continue;
                try { m.UploadMeshData(false); } catch (System.Exception) { }
                if (sw.ElapsedMilliseconds >= 4) { worst = Mathf.Max(worst, sw.ElapsedMilliseconds); frames++; yield return null; sw.Restart(); }
            }
            var mats = new System.Collections.Generic.List<Material>(); var matSet = new System.Collections.Generic.HashSet<Material>();
            foreach (var r in builder.root.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m && matSet.Add(m)) mats.Add(m);
            // 实例化版本：每种着色器一份
            var shaders = new System.Collections.Generic.HashSet<Shader>(); var inst = new System.Collections.Generic.List<Material>();
            foreach (var m in mats) if (m.shader && shaders.Add(m.shader)) inst.Add(new Material(m) { enableInstancing = true });
            var fx = Shader.Find("Monster/FxPuff"); if (fx && shaders.Add(fx)) inst.Add(new Material(fx) { enableInstancing = true });
            var box = CityMeshes.Box(); var one = new System.Collections.Generic.List<Matrix4x4> { Matrix4x4.identity };
            Matrix4x4 Tiny(int k) { var c = cam ? cam.transform : transform; return Matrix4x4.TRS(c.position + c.forward * 0.9f + c.right * (k * 0.002f), Quaternion.identity, Vector3.one * 0.0001f); }
            for (int i = 0; i < mats.Count; i += 5)
            {
                for (int k = 0; k < 5 && i + k < mats.Count; k++)
                    Graphics.DrawMesh(box, Tiny(k), mats[i + k], UILayer, null, 0, null, UnityEngine.Rendering.ShadowCastingMode.On, true);
                frames++; yield return null;
            }
            foreach (var m in inst)
            {
                one[0] = Tiny(0);
                Graphics.RenderMeshInstanced(new RenderParams(m) { layer = UILayer, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off }, box, 0, one);
                frames++; yield return null;
            }
            yield return null;
            foreach (var m in inst) Destroy(m);
            Debug.Log($"[怪兽][预热] 网格 {seen.Count} 个、材质 {mats.Count} 种、着色器 {shaders.Count} 种；{frames} 帧 / {Time.realtimeSinceStartup - t0:0.00} 秒，上传最长一步 {worst:0}ms");
        }
        public static bool DebugRealTable;   // 编辑器验收用
        static void SetLayer(Transform t, int layer) { if (!t) return; t.gameObject.layer = layer; foreach (Transform c in t) SetLayer(c, layer); }

        /// <summary>接玩法：逻辑层 + 表现层；头显用手眼输入，编辑器用鼠标试玩</summary>
        void StartGame(Monster.Game.Level lv, CityLayout city, CityBuilder builder)
        {
            game = new Monster.Game.MonsterGame(lv, city, 7) { S = builder.scale };   // 必须在 CityBuilder.Build 之后：楼高由模块决定，生成时同步回逻辑层
            Monster.Game.Enemies.Scale = builder.scale;
            var view = gameObject.AddComponent<Monster.Game.GameView>();
            view.Init(game, builder);
            view.input = inp; builderRef = builder; curLevel = System.Array.IndexOf(Monster.Game.Level.All, lv);
            if (!XRSettings.isDeviceActive)
            {
                var sim = gameObject.AddComponent<Monster.Game.DeskSim>();
                sim.game = game; sim.cam = cam;
                view.simInput = sim.Read;
            }
            var ev = new GameObject("Enemies").AddComponent<Monster.Game.EnemyView>(); ev.game = game; ev.view = view; ev.cityScale = builder.scale;
            if (!handView) { handView = gameObject.AddComponent<Monster.Game.HandView>(); handView.input = inp; handView.hidden = true; }
            handView.game = game;   // 巨兽之手（按真手 26 个关节实时搭；启动时就建好了，这里只接上怒气 / 体力）
            if (lv.tutorial) { var sg = gameObject.AddComponent<Monster.Game.TutorialSign>(); sg.game = game; sg.view = view; sg.cam = cam; }
            // （11-13：站着坐着都能玩，不再弹“请站起来”的提醒）
            {
                // 黄昏天幕：落日在主城区的远侧（玩家面朝的方向）偏左、仰角约 7°
                var mz = city.zones[0]; var away = -mz.v; var sky = gameObject.AddComponent<Monster.Game.DuskSky>();
                sky.cam = cam; sky.time = lv.time; sky.sunDir = (away * 1f - mz.u * 0.35f + Vector3.up * (lv.time == "dusk" ? -0.02f : 0.13f)).normalized;
            }
            gameObject.AddComponent<Monster.Game.CityHud>().Init(game, view, cam);
            {
                var pm = gameObject.AddComponent<Monster.Game.PalmMenu>(); pm.input = inp; pm.view = view; pm.game = game; pm.cam = cam; pm.level = curLevel;
                var pb = gameObject.AddComponent<Monster.Game.PauseButton>(); pb.game = game; pb.view = view; pb.menu = pm; pb.zone = city.zones[0];   // 城市近角的实物暂停钮
            }   // 左手心菜单：暂停 / 重来 / 回标题
            view.paused = true;   // 城市出场后（Reveal 结束）才开始
            viewRef = view;
            Debug.Log($"[怪兽] 开局：{lv.name}，塔 {city.towers.Count} 座，电站 {city.stations.Count} 座");
        }

        /// <summary>
        /// 城市出场（11-09 用户：出现方式不对 —— 以前整片城区连地面带树一起竖着拉长）：
        /// 地面、马路、桌子直接在，桌沿扬一圈尘；楼从远到近一波一波从地里升起来（每栋 0.5 秒、略回弹，脚下扬尘），封印塔最后；
        /// 期间合批网格先藏、每栋楼单独动；车流等楼都立起来再开。最后停 1.5 秒让玩家看城，然后开始玩。
        /// </summary>
        IEnumerator Reveal(CityBuilder builder, Monster.Game.GameView view, CityLayout city)
        {
            builder.root.gameObject.SetActive(true); CityShown = true;
            builder.BeginReveal();   // 每栋楼轮到它升起时才打开它的零件（不在第一帧一次全开）
            float worstFrame = 0; int frameN = 0, worstAt = 0, shownThis = 0, worstShown = 0;
            foreach (var tr in builder.root.GetComponentsInChildren<Traffic>(true)) tr.enabled = false;
            var head = cam ? cam.transform.position : Vector3.up * 1.6f;
            var fwd = cam ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            var items = new System.Collections.Generic.List<(Transform t, Vector3 s0, float delay, Vector3 foot, bool dust)>();
            var views = new System.Collections.Generic.List<BuildingView>();
            float dMin = float.MaxValue, dMax = float.MinValue;
            foreach (var b in city.buildings) { float d = Vector3.Dot(b.Base - head, fwd); dMin = Mathf.Min(dMin, d); dMax = Mathf.Max(dMax, d); }
            int n = 0;
            foreach (var b in city.buildings)
            {
                var v = builder.View(b.id); if (!v) continue;
                float far = Mathf.InverseLerp(dMax, dMin, Vector3.Dot(b.Base - head, fwd));   // 远的先长
                float delay = b.kind == BuildingKind.Tower ? 1.25f + (n % 3) * 0.1f : far * 0.9f + (b.seed % 7) * 0.02f;
                items.Add((v.transform, v.transform.localScale, delay, b.Base, n++ % 2 == 0 || b.kind == BuildingKind.Tower)); views.Add(v);
                v.transform.localScale = new Vector3(v.transform.localScale.x, 0.001f, v.transform.localScale.z);
            }
            foreach (var z in city.zones)   // 桌沿一圈尘
                for (int i = 0; i < 6; i++) { view.AddDust(z.Point(z.w * (i + 0.5f) / 6, z.h + 0.01f, 0.004f), 0.03f); view.AddDust(z.Point(z.w * (i + 0.5f) / 6, -0.01f, 0.004f), 0.03f); }
            Monster.Game.MonsterAudio.Play("sfx_land", null, 0.45f, 0.8f);
            var started = new bool[items.Count]; int rumble = 0, lastShown = 0;
            for (float t = 0; ; t += Time.deltaTime)
            {
                bool all = true;
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i]; float x = (t - it.delay) / 0.5f;
                    if (x < 1) all = false;
                    if (x <= 0 || !it.t) continue;
                    if (!started[i]) { started[i] = true; builder.RevealShow(views[i]); shownThis++; if (it.dust) view.AddDust(it.foot + Vector3.up * 0.004f, 0.025f); if (rumble++ % 6 == 0) Monster.Game.MonsterAudio.Play("sfx_collapse", it.foot, 0.18f, 1.3f); }
                    x = Mathf.Clamp01(x); const float c1 = 1.3f; float e = 1 + (c1 + 1) * Mathf.Pow(x - 1, 3) + c1 * Mathf.Pow(x - 1, 2);
                    it.t.localScale = new Vector3(it.s0.x, Mathf.Max(0.001f, e) * it.s0.y, it.s0.z);
                }
                if (all) break;
                yield return null;
                frameN++;
                if (Time.unscaledDeltaTime > worstFrame) { worstFrame = Time.unscaledDeltaTime; worstAt = frameN; worstShown = lastShown; }
                lastShown = shownThis; shownThis = 0;
            }
            foreach (var it in items) if (it.t) it.t.localScale = it.s0;
            for (int i = 0; i < views.Count; i++) if (!started[i]) builder.RevealShow(views[i]);
            Debug.Log($"[怪兽][出场] {frameN} 帧，最慢一帧 {worstFrame * 1000:0}ms（第 {worstAt} 帧，那一帧升起 {worstShown} 栋楼）");
            yield return builder.EndReveal();
            foreach (var tr in builder.root.GetComponentsInChildren<Traffic>(true)) tr.enabled = true;
            yield return new WaitForSeconds(1.5f);
            view.paused = false;
            if (handView) handView.hidden = false;   // 加载和出场全部结束：手出现
        }

        float resultT;
        void CheckResult()
        {
            if (game == null || game.result == null || resultShown) return;
            resultT += Time.deltaTime;
            if (resultT < (game.result == "win" ? 0.5f : 3.0f)) return;   // 赢了：胜利过程（Cfg.WinShow 秒）已经在 result 之前放完，这里只再等半秒   // 输了：先闭眼（约 2.6 秒），再出结算纸   // 先让最后一座塔倒完、爆炸放完；倒下时先让画面黑下来、看到“巨兽倒下”
            resultShown = true;
            if (game.result == "win" && curLevel == Monster.Game.Level.All.Length - 1)
            {
                // 最后一区打赢：不出普通的结算纸，接结局（熄灯 → 力量回来 → 涨潮 → 天亮 → 最后一份公告）
                var en = gameObject.AddComponent<Monster.Game.Ending>();
                en.cam = cam; en.input = inp; en.game = game; en.view = viewRef; en.level = curLevel;
                en.onLevel = l => { PendingLevel = l; Reload(); }; en.onTitle = () => { PendingLevel = -1; Reload(); };
                return;
            }
            var rs = gameObject.AddComponent<Monster.Game.ResultScreen>();
            rs.cam = cam; rs.input = inp; rs.game = game; rs.level = curLevel;
            rs.onLevel = l => { PendingLevel = l; Reload(); };
            rs.onTitle = () => { PendingLevel = -1; Reload(); };
        }
        /// <summary>局内菜单用：重开某一关（-1 = 回标题）</summary>
        public static void Restart(int level) { PendingLevel = level; Reload(); }
        public int CurrentLevel => curLevel;
        static void Reload() => UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);

        void Update()
        {
            CheckResult();
            if (XRSettings.isDeviceActive && cam)
            {
                if (!aligned && cam.transform.localPosition.sqrMagnitude > 0.01f) Align();
                if (!floorSet)
                {
                    retry -= Time.unscaledDeltaTime;
                    if (retry <= 0)
                    {
                        retry = 1;
                        var subs = new System.Collections.Generic.List<XRInputSubsystem>();
                        SubsystemManager.GetSubsystems(subs);
                        foreach (var s in subs)
                            if ((s.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) != 0)
                                floorSet |= s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                    }
                }
            }
            Measure();
        }

        // ───────── 性能读数 ─────────
        int frames, slow; float acc, worst, cpuAcc, gpuAcc; int timed;
        readonly FrameTiming[] ft = new FrameTiming[1];
        void Measure()
        {
            float dt = Time.unscaledDeltaTime;
            frames++; acc += dt; worst = Mathf.Max(worst, dt); Monster.Game.Perf.Frame(); if (dt > 0.02f) slow++;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0) { cpuAcc += (float)ft[0].cpuFrameTime; gpuAcc += (float)ft[0].gpuFrameTime; timed++; }
            if (acc < 5f) return;
            float fps = frames / acc;
            string line = $"FPS {fps:0} | 最慢一帧 {worst * 1000:0}ms 超过 20ms 的 {slow} 帧 | CPU {(timed > 0 ? cpuAcc / timed : 0):0.0}ms | GPU {(timed > 0 ? gpuAcc / timed : 0):0.0}ms" + (built ? "" : " | 生成中");
            if (game != null) line += " | " + Monster.Game.Perf.Report(frames);   // 各段脚本耗时、垃圾内存、场上数量（见 Perf.cs）
            slow = 0;
            Debug.Log("[怪兽][性能] " + line);
            if (stats) stats.text = line;
            frames = 0; acc = 0; worst = 0; cpuAcc = 0; gpuAcc = 0; timed = 0;
        }

        void MakeStats()
        {
            var go = new GameObject("Stats");
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(-0.16f, -0.13f, 0.6f);
            stats = go.AddComponent<TextMesh>();
            stats.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            stats.GetComponent<MeshRenderer>().sharedMaterial = stats.font.material;
            stats.fontSize = 48; stats.characterSize = 0.0022f; stats.anchor = TextAnchor.LowerLeft;
            stats.color = new Color(1, 1, 1, 0.85f);
            stats.text = "测量中…";
        }
    }
}
