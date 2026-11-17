// 城市场景生成：按 CityLayout 在每个城区画马路（透明地面 + 接影子，楼直接长在真实桌面上）、楼（每层一个子物体，可拆）、塔、电站、港口。
// 运行时也用它（头显里扫描房间后现场生成），所以放在 Runtime。
// 层级：City / <城区> / Ground, Props, Buildings / B<id> / Storey_<k>, Crown
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Monster
{
    public class BuildingView : MonoBehaviour
    {
        public int id;
        public Transform[] storeys;   // 从下往上，每层一个
        public Transform crown;       // 楼顶（拆到哪一层就跟到哪一层上面）
        public float modH;
    }

    public partial class CityBuilder
    {
        public bool night;
        /// <summary>体检模式：不合并网格（每个小件保留成独立物体），给场景级悬空 / 穿插检查用</summary>
        public static bool AuditMode;
        public Transform root;
        public CityLayout city;
        readonly Dictionary<int, BuildingView> views = new();

        static readonly Color Ink = new(0.13f, 0.08f, 0.18f);

        public static CityBuilder Build(CityLayout city, bool night, Transform parent = null, float scale = 1)
        {
            var b = new CityBuilder { city = city, night = night };
            b.root = new GameObject("City").transform;
            if (parent) b.root.SetParent(parent, false);
            foreach (var bd in city.buildings) b.BuildBuilding(bd);
            b.BuildStreets();   // 楼盖完再摆路边的东西（要避开楼的实际轮廓）
            b.Shrink(scale);    // 整体缩小（城区按设计尺寸生成，再以桌面角点为支点缩放）
            foreach (var z in city.zones) b.BuildFurniture(z);   // 虚拟桌柜按真实尺寸，最后生成
            b.BuildEdges();     // 城市边界：路沿 / 海堤 + 步行道 + 路灯串灯（真实尺寸）
            if (!AuditMode) b.BatchCity();      // 静态合批（性能）：楼按 1.1 米格子 × 材质合网格，拆楼前调 Detach
            return b;
        }

        /// <summary>
        /// 分帧生成（11-09 用户：开始游戏转漫画时卡一下、头晕）：每帧最多占 budget 秒，超了就让出一帧，
        /// 画面一直在刷新（漫画 / 简报此时已经在眼前）。步骤和 Build 完全一样。
        /// </summary>
        public static System.Collections.IEnumerator BuildAsync(CityLayout city, bool night, float scale, System.Action<CityBuilder> done, float budget = 0.008f)
        {
            var b = new CityBuilder { city = city, night = night };
            b.root = new GameObject("City").transform;
            var sw = System.Diagnostics.Stopwatch.StartNew(); var total = System.Diagnostics.Stopwatch.StartNew(); int frames0 = Time.frameCount;
            int Frames() => Time.frameCount - frames0;
            var phase = new System.Text.StringBuilder();
            foreach (var bd in city.buildings)
            {
                b.BuildBuilding(bd);
                if (sw.Elapsed.TotalSeconds > budget) { yield return null; sw.Restart(); }
            }
            var ps = System.Diagnostics.Stopwatch.StartNew(); long worst = 0; string worstAt = "";
            yield return null;
            // 分步执行：记下单帧最长的一步（真机看日志确认生成期间不卡）
            System.Collections.IEnumerator Run(System.Collections.IEnumerator steps, string name)
            {
                // 11-09 真机：原来每一小步都让出一帧（头显一帧 11ms，几千小步 → 城市生成 69.7 秒）。
                // 改成一帧里连着走多步，累计用满 budget 才让出（时间片调度）；单步本身超长的照样记下来
                var frame = System.Diagnostics.Stopwatch.StartNew();
                for (int idx = 0; ; idx++)
                {
                    // 11-13 真机：某一步抛异常（屋顶设备越界）→ 整个生成协程死掉，玩家一直停在“城市生成中…”。
                    // 每一步单独接异常：出错只丢掉这一组里剩下的步骤（记进日志），后面的照常做，城市一定能生成完
                    ps.Restart(); bool more;
                    try { more = steps.MoveNext(); }
                    catch (System.Exception ex) { Debug.LogError($"[怪兽][生成] {name}#{idx} 出错，跳过这一组剩下的步骤：{ex}"); phase.Append($"{name}#{idx} 出错 "); yield break; }
                    long ms = ps.ElapsedMilliseconds;
                    if (ms > worst) { worst = ms; worstAt = name + "#" + idx; }
                    if (ms > 60) phase.Append($"{name}#{idx} {ms}ms ");
                    if (!more) yield break;
                    if (frame.Elapsed.TotalSeconds > budget) { yield return null; frame.Restart(); }
                }
            }
            var st = Run(b.BuildStreetsSteps(), "街道"); while (st.MoveNext()) yield return null;
            ps.Restart(); b.Shrink(scale); foreach (var z in city.zones) b.BuildFurniture(z); phase.Append($"缩放+家具 {ps.ElapsedMilliseconds}ms ");
            yield return null;
            ps.Restart(); b.BuildEdges(); phase.Append($"边界 {ps.ElapsedMilliseconds}ms ");
            yield return null;
            if (!AuditMode) { var bt = Run(b.BatchCitySteps(), "合批"); while (bt.MoveNext()) yield return null; }
            Debug.Log($"[怪兽][生成] 分步最长一步 {worst}ms（{worstAt}）；{phase}总耗时 {total.Elapsed.TotalSeconds:0.0}s / {Frames()} 帧");
            done(b);
        }

        /// <summary>出场动画期间：合批网格先藏起来、显示每栋楼原来的一层层（这样每栋楼能单独升起来）；结束后切回合批</summary>
        public void SetRevealMode(bool on)
        {
            var bp = root.Find("Batched"); if (bp) bp.gameObject.SetActive(!on);
            foreach (var c in chunks) foreach (var p in c.parts) if (p.r) p.r.enabled = on;
        }
        // 11-11 用户：刚进游戏时虚拟手卡一下 —— 真机日志：城市出场的第一帧 85～90ms（平时 11ms）。
        // 原因之一：出场那一帧把所有楼的上千个零件同时打开。改成分批：出场开始只开不属于任何楼的静态件，
        // 每栋楼轮到它升起时才开它自己的零件；出场结束换回合批网格，零件分几帧关掉。
        public void BeginReveal()
        {
            var bp = root.Find("Batched"); if (bp) bp.gameObject.SetActive(false);
            foreach (var c in chunks) foreach (var p in c.parts) if (p.r) p.r.enabled = p.owner == null;
        }
        public void RevealShow(BuildingView v)
        {
            if (!v || !chunkOf.TryGetValue(v, out var list)) return;
            foreach (var c in list) foreach (var p in c.parts) if (p.owner == v && p.r) p.r.enabled = true;
        }
        public System.Collections.IEnumerator EndReveal()
        {
            var bp = root.Find("Batched"); if (bp) bp.gameObject.SetActive(true);
            yield return null;
            int n = 0;
            foreach (var c in chunks)
                foreach (var p in c.parts)
                {
                    if (!p.r || !p.r.enabled) continue;
                    p.r.enabled = false;
                    if (++n % 350 == 0) yield return null;
                }
        }

        Transform ZoneRoot(Zone z, string child)
        {
            var zr = root.Find(z.id) ?? new GameObject(z.id).transform;
            if (zr.parent != root) zr.SetParent(root, false);
            var c = zr.Find(child);
            if (!c) { c = new GameObject(child).transform; c.SetParent(zr, false); }
            return c;
        }

        // 一个带材质的子物体（单位网格 → 缩放到 size，底面中心放在 local pos）
        static Transform Part(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 size, string name = "p", Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return go.transform;
        }

        // ───────── 城区地面 ─────────
        // 地面不再是整块板：只画马路（边缘渐隐）、楼底人行道、港口水面，其余透明露出真实桌面 / 地板；
        // 网格比城区每边多出 GM，用来接住边缘楼投到桌面上的影子（Monster/CityGround）。
        const float GM = 0.12f;

        // 路网贴图（640 像素 / 米）：马路 / 车道线 / 斑马线 / 楼底人行道 / 水面；四周留 GM 的透明边。
        // a 通道：马路按离城区边缘的距离渐隐，人行道和水面不透明，其余 0（真实桌面）。
        Texture2D GroundTexture(Zone z, out float aMax, out float bMax)
        {
            int px = 64; float ppm = px / CityConst.Cell;
            int mg = Mathf.RoundToInt(GM * ppm);
            int cols = z.Cols, rows = z.Rows;
            int W = cols * px + mg * 2, H = rows * px + mg * 2;
            aMax = W / ppm - GM; bMax = H / ppm - GM;
            var p = new Color[W * H];
            void R(int x, int y, int w, int h, Color c)
            {
                x += mg; y += mg;
                for (int j = Mathf.Max(0, y); j < Mathf.Min(H, y + h); j++)
                    for (int i = Mathf.Max(0, x); i < Mathf.Min(W, x + w); i++) p[j * W + i] = c;
            }
            System.Func<string, Color> H_ = CityMaterials.Hex;
            var asphalt = H_("#3a3a42"); var line = H_("#f5d76e"); var zebra = H_("#eeeeee");
            for (int gx = 0; gx < cols; gx++) for (int gy = 0; gy < rows; gy++)
            {
                if (z.IsWater(gy) || !CityLayout.IsRoad(gx, gy)) continue;
                int x = gx * px, y = gy * px;   // 贴图 v 从下往上 = 分区 b 从小到大
                R(x, y, px, px, asphalt);
                bool hor = gy % CityConst.RoadEvery == 0, ver = gx % CityConst.RoadEvery == 0;
                if (hor && !ver) for (int k = 0; k < 3; k++) R(x + k * 22 + 4, y + px / 2 - 2, 12, 4, line);
                if (ver && !hor) for (int k = 0; k < 3; k++) R(x + px / 2 - 2, y + k * 22 + 4, 4, 12, line);
                if (hor && ver) for (int k = 0; k < 6; k++) { R(x + 5 + k * 9, y + 3, 5, 8, zebra); R(x + 5 + k * 9, y + px - 11, 5, 8, zebra); }
            }
            // 街区人行道：每个街区四周一圈（宽 CityConst.Walk），浅色地砖 + 外沿深色路缘石；和马路一起在边缘淡出
            var walkC = H_("#cfc5b5"); var curbC = H_("#8f8578");
            int wk = Mathf.RoundToInt(CityConst.Walk * ppm);
            foreach (var blk in CityLayout.Blocks(z))
            {
                int bx0 = blk.gx * px, by0 = blk.gy * px, bw0 = blk.wc * px, bh0 = blk.hc * px;
                if (z.harbor && blk.gy + blk.hc > z.LandRows) continue;
                R(bx0, by0, bw0, bh0, curbC);
                R(bx0 + 2, by0 + 2, bw0 - 4, bh0 - 4, walkC);
                for (int k = bx0 + 9; k < bx0 + bw0 - 4; k += 9) { R(k, by0 + 2, 1, wk - 2, Color.Lerp(walkC, curbC, 0.3f)); R(k, by0 + bh0 - wk, 1, wk - 2, Color.Lerp(walkC, curbC, 0.3f)); }
                // 街区内部先铺一层地坪（地块之间的缝、院子没覆盖到的地方都不再露空），地块 / 院子再画在上面
                R(bx0 + wk, by0 + wk, bw0 - wk * 2, bh0 - wk * 2, H_("#bdb3a2"));
            }
            // 不盖楼的地块：地面直接画出来（草地 / 停车场 / 广场地砖 / 球场 / 工地黄土）
            foreach (var o in city.opens) if (o.zone == z) PaintOpen(o, R, ppm);
            PaintYards(z, R, ppm);
            // 11-09 用户：边缘一圈半透明的马路残影 + 草地上孤零零的红绿灯很假 → 马路不再在边缘淡出，
            // 最外一圈就是完整的环路，外面接路沿 / 步行道（CityEdge）
            // 楼底人行道：按楼的实际占地（含雨棚）每边多 3mm，浅色地砖 + 深色路缘；不渐隐
            var walk = H_("#d4cabb"); var curb = H_("#9a8f80");
            foreach (var rr in buildingRects[z])
            {
                float m2 = 0.003f;
                int x0 = Mathf.RoundToInt((rr.xMin - m2) * ppm), x1 = Mathf.RoundToInt((rr.xMax + m2) * ppm);
                int y0 = Mathf.RoundToInt((rr.yMin - m2) * ppm), y1 = Mathf.RoundToInt((rr.yMax + m2) * ppm);
                R(x0, y0, x1 - x0, y1 - y0, curb);
                R(x0 + 2, y0 + 2, x1 - x0 - 4, y1 - y0 - 4, walk);
            }
            // 港口：水面单独一块网格（CityStreets.BuildHarbor，动态河面着色器），这里只画码头地坪
            if (z.harbor) R(0, z.LandRows * px - 10, cols * px, 10, H_("#a89f92"));
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "ground_" + z.id, anisoLevel = 8, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(p); tex.Apply(true);
            return tex;
        }

        // 离城区边缘的距离（米）：街道小品只放在马路没怎么淡出的地方
        static float EdgeDist(Zone z, float a, float b) => Mathf.Min(Mathf.Min(a, z.w - a), Mathf.Min(b, z.h - b));

        // 空地块的地面图案（像素坐标 = 分区坐标 × ppm；R 是 GroundTexture 里的画矩形函数）
        void PaintOpen(OpenLot o, System.Action<int, int, int, int, Color> R, float ppm)
        {
            System.Func<string, Color> H_ = CityMaterials.Hex;
            int x0 = Mathf.RoundToInt((o.a - o.w / 2) * ppm), y0 = Mathf.RoundToInt((o.b - o.d / 2) * ppm);
            int w = Mathf.RoundToInt(o.w * ppm), h = Mathf.RoundToInt(o.d * ppm);
            var rng = new Rng((uint)o.seed + 3);
            switch (o.kind)
            {
                case "park":
                    R(x0, y0, w, h, H_("#6fae5a"));
                    for (int i = 0; i < 14; i++) { int bw = rng.Int(6, 18), bh = rng.Int(6, 18); R(x0 + rng.Int(0, w - bw), y0 + rng.Int(0, h - bh), bw, bh, H_("#62a052")); }
                    R(x0 + w / 2 - 4, y0, 8, h, H_("#d9cdb2")); R(x0, y0 + h / 2 - 4, w, 8, H_("#d9cdb2"));
                    break;
                case "green":
                    R(x0, y0, w, h, H_("#6fa95a"));
                    for (int i = 0; i < 20; i++) { int bw = rng.Int(5, 16), bh = rng.Int(5, 16); R(x0 + rng.Int(0, Mathf.Max(0, w - bw)), y0 + rng.Int(0, Mathf.Max(0, h - bh)), bw, bh, i % 3 == 0 ? H_("#5e9a4c") : H_("#7cb466")); }
                    R(x0 + rng.Int(w / 4, w / 2), y0, 5, h, H_("#d9cdb2"));
                    break;
                case "plaza":
                    R(x0, y0, w, h, H_("#d8cdb8"));
                    for (int x = 0; x < w; x += 10) R(x0 + x, y0, 1, h, H_("#c4b8a2"));
                    for (int y = 0; y < h; y += 10) R(x0, y0 + y, w, 1, H_("#c4b8a2"));
                    R(x0 + w / 4, y0 + h / 4, w / 2, h / 2, H_("#c9a98a"));
                    break;
                case "court":
                    R(x0, y0, w, h, H_("#3f7a5c"));
                    R(x0 + 5, y0 + 5, w - 10, h - 10, H_("#d9773f"));
                    var ln = H_("#f4f0e6");
                    R(x0 + 5, y0 + 5, w - 10, 1, ln); R(x0 + 5, y0 + h - 6, w - 10, 1, ln); R(x0 + 5, y0 + 5, 1, h - 10, ln); R(x0 + w - 6, y0 + 5, 1, h - 10, ln);
                    R(x0 + 5, y0 + h / 2, w - 10, 1, ln);
                    R(x0 + w / 2 - 8, y0 + 5, 16, 18, H_("#2f6fb0")); R(x0 + w / 2 - 8, y0 + h - 23, 16, 18, H_("#2f6fb0"));
                    break;
                case "parking":
                    R(x0, y0, w, h, H_("#4a4a52"));
                    {
                        float sw = 2.7f * K * ppm, sd = 5.2f * K * ppm; var stallC = H_("#efeae0");
                        foreach (var (sa, sb, up) in ParkingStalls(o))
                        {
                            int cx = Mathf.RoundToInt(sa * ppm), cy = Mathf.RoundToInt(sb * ppm);
                            R(cx - Mathf.RoundToInt(sw / 2), cy - Mathf.RoundToInt(sd / 2), 1, Mathf.RoundToInt(sd), stallC);
                            R(cx + Mathf.RoundToInt(sw / 2), cy - Mathf.RoundToInt(sd / 2), 1, Mathf.RoundToInt(sd), stallC);
                            R(cx - Mathf.RoundToInt(sw / 2), up ? cy + Mathf.RoundToInt(sd / 2) : cy - Mathf.RoundToInt(sd / 2), Mathf.RoundToInt(sw) + 1, 1, stallC);
                        }
                    }
                    break;
                case "construction":
                    R(x0, y0, w, h, H_("#a88a62"));
                    for (int i = 0; i < 18; i++) { int bw = rng.Int(5, 16), bh = rng.Int(4, 12); R(x0 + rng.Int(0, w - bw), y0 + rng.Int(0, h - bh), bw, bh, i % 2 == 0 ? H_("#97794f") : H_("#b49870")); }
                    break;
            }
        }

        // ───────── 楼 ─────────
        void BuildBuilding(Building b)
        {
            var parent = ZoneRoot(b.zone, "Buildings");
            var go = new GameObject($"B{b.id}_{b.style}");
            go.transform.SetParent(parent, false);
            go.transform.position = b.Base;
            go.transform.rotation = Quaternion.LookRotation(b.zone.v, Vector3.up);
            var view = go.AddComponent<BuildingView>();
            view.id = b.id; view.modH = b.modH;
            var rng = new Rng((uint)b.seed + 11);
            if (b.kind == BuildingKind.Tower) BuildTower(b, view);
            else if (b.kind == BuildingKind.Station) BuildStation(b, view);
            else BuildHouse(b, view, rng);
            views[b.id] = view;
        }

        // 正式模块（Blender 程序化，真实尺寸米）：底座 + N 层标准层（A/B 交替）+ 楼顶。
        // 整栋按占地统一缩放（x / z 拉到地块大小，y 用 x、z 的平均比例，保持建筑比例不变形）；每层一个子物体，倒塌时按层拆。
        bool BuildFromModules(Building b, BuildingView view)
        {
            var set = ModuleSet.Get(b.style);
            if (set == null) return false;
            var mat = ModuleMaterial(b.style, set, (b.seed >> 4) % Mathf.Max(1, set.albedos.Count));
            // 等比缩放（不拉伸窗户和招牌）：按地块较紧的一边定比例，另一边多出来的地做院子（CityStreets.ComputeYards）
            float sx = Mathf.Min(b.fw / set.size.x, b.fd / set.size.y), sz = sx, sy = sx;
            float zOff = Mathf.Max(0, b.fd - set.size.y * sz) * 0.5f;   // 变浅了就贴着临街一侧（本地 +Z），空地留在楼后
            // 底座、楼顶各算一“层”（倒塌也是一层一层拆）：标准层数 = 总层数 - 2，至少 1
            int mids = Mathf.Max(set.minMids, b.maxH - set.midOffset);
            var storeys = new List<Transform>(); var storeyMeshes = new List<Mesh>(); var tops = new List<float> { 0 };
            float y = 0;
            Transform Add(Mesh mesh, float h, string name)
            {
                storeyMeshes.Add(mesh);
                var s = new GameObject(name).transform;
                s.SetParent(view.transform, false);
                s.localPosition = new Vector3(0, y, zOff);
                // 模块正面（入口 / 招牌）在 FBX 里朝 -Z，楼的本地 +Z 朝街道（玩家一侧），所以转 180°
                Part(s, mesh, mat, Vector3.zero, new Vector3(sx, sy, sz), "Body", Quaternion.Euler(0, 180, 0));
                storeys.Add(s);
                y += h * sy; tops.Add(y);
                return s;
            }
            Add(set.baseMesh, set.baseH, "Storey_0");
            for (int i = 0; i < mids; i++) Add(i % 2 == 0 ? set.storeyA : set.storeyB, set.storeyH, $"Storey_{i + 1}");
            var crown = Add(set.crownMesh, set.crownH, $"Storey_{mids + 1}");
            AddRoofProps(b, crown, sx, sy, sz);
            AddSigns(b, set, storeys, sx, sy, sz, y);
            AddGraffiti(b, storeys, storeyMeshes, sx);
            view.storeys = storeys.ToArray();
            // 楼高由模块决定：同步回逻辑层（层数 = 实际子物体数，每层平均高）
            b.maxH = b.h = storeys.Count;
            b.modH = y / storeys.Count; b.tops = tops.ToArray();   // 每层真实高度（倒塌 / 压扁 / 瓦砾落点都按它，不按平均层高）
            b.bodyW = set.size.x * sx; b.bodyD = set.size.y * sz; b.bodyOff = zOff;   // 模型真实占地：炮弹要炸在看得见的墙上，不是炸在分给它的那块地的边上
            view.modH = b.modH;
            return true;
        }

        // ───────── 楼顶附件（Tools/blender_props.py，真实尺寸米）─────────
        // 坐标用 Blender 楼顶模块里的米：(bx, by) 平面位置、bz 楼顶表面高度；k 是附件放大倍数（真实楼顶设备比 1 米大）。
        // 附件挂在楼顶那一层下面，倒塌时一起掉；不投影子（小件，省性能）。
        struct RoofProp { public string name; public float bx, by, bz, k; public RoofProp(string n, float x, float y, float z, float s) { name = n; bx = x; by = y; bz = z; k = s; } }
        static readonly Dictionary<Style, RoofProp[]> RoofProps = new()
        {
            // 玻璃楼：女儿墙内、设备层外的一圈空地（|x| 或 |y| 在 9~13.6 米之间），设备层顶面 5.4 米处再放两台
            [Style.Glass] = new[]
            {
                new RoofProp("ac_unit", -11.3f, -11.3f, 0.4f, 2.4f), new RoofProp("ac_unit", -11.3f, -7.6f, 0.4f, 2.4f),
                new RoofProp("ac_unit", -7.6f, -11.3f, 0.4f, 2.4f), new RoofProp("water_tank", 11.2f, 11.2f, 0.4f, 1.6f),
                new RoofProp("dish", 11.6f, -11.6f, 0.4f, 1.6f), new RoofProp("vent", -11.6f, 11.6f, 0.4f, 1.5f),
                new RoofProp("vent", 0f, 11.4f, 0.4f, 1.5f),
                new RoofProp("ac_unit", -3.5f, -5.5f, 5.4f, 2.0f), new RoofProp("ac_unit", 1.2f, -6.2f, 5.4f, 2.0f),
            },
            // 海滨酒店：正面中间是阶梯塔（y < -5.8），右后方是楼梯间（x 4~8，y 1~5），其余是空地
            [Style.Brick] = new[] { new RoofProp("ac_unit", -1f, -3f, 0.4f, 1.5f), new RoofProp("vent", 1.2f, -4.2f, 0.4f, 1.2f) },
            [Style.Mixed] = new[] { new RoofProp("ac_unit", 0f, -1f, 0.05f, 1.5f), new RoofProp("ac_unit", 2.4f, -1f, 0.05f, 1.5f), new RoofProp("dish", -7f, -6f, 0.05f, 1.2f) },
            [Style.Condo] = new[] { new RoofProp("vent", 6f, -6f, 0.27f, 1.3f), new RoofProp("ac_unit", 6f, -3.5f, 0.27f, 1.5f) },
            [Style.Deco] = new[]
            {
                new RoofProp("ac_unit", -8.0f, 4.5f, 0.05f, 1.6f), new RoofProp("ac_unit", -5.6f, 4.5f, 0.05f, 1.6f),
                new RoofProp("ac_unit", -8.0f, 1.6f, 0.05f, 1.6f), new RoofProp("water_tank", -7.0f, -3.6f, 0.05f, 1.3f),
                new RoofProp("vent", 8.6f, -3.8f, 0.05f, 1.2f), new RoofProp("dish", 1.2f, 5.6f, 0.05f, 1.3f),
            },
        };

        readonly Dictionary<string, Mesh> propMeshes = new();
        Material propMat;
        Mesh PropMesh(string n)
        {
            if (propMeshes.TryGetValue(n, out var m) && m) return m;
            var go = Resources.Load<GameObject>($"Modules/props/{n}");
            m = go ? go.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
            propMeshes[n] = m;
            return m;
        }
        Material PropMaterial()
        {
            if (propMat) return propMat;
            propMat = new Material(CityMaterials.Toon) { name = "props", enableInstancing = true };
            propMat.SetTexture("_BaseMap", Resources.Load<Texture2D>("Modules/props/albedo"));
            return propMat;
        }

        void AddRoofProps(Building b, Transform crown, float sx, float sy, float sz)
        {
            if (!RoofProps.TryGetValue(b.style, out var list)) return;
            var mat = PropMaterial();
            var rng = new Rng((uint)b.seed + 71);
            foreach (var p in list)
            {
                var mesh = PropMesh(p.name);
                if (!mesh) continue;
                // 楼顶模块：Blender (x, y, z) → 楼的本地 (-x·sx, z·sy, -y·sz)（FBX 轴转换 + 转 180°）
                var pos = new Vector3(-p.bx * sx, p.bz * sy, -p.by * sz);
                var rot = Quaternion.Euler(0, 90 * rng.Int(0, 3), 0);
                var t = Part(crown, mesh, mat, pos, Vector3.one * (p.k * sy), "Prop_" + p.name, rot);
                t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // ───────── 霓虹招牌（Tripo banana 生成：ArtSource/Signs 源图 → Resources/Signs/<名>.png + <名>_emis.png 自发光遮罩）─────────
        // 坐标用 Blender 楼模块里的米：bx / by 平面位置（Blender 前端 -y = 楼本地 +Z 临街面），bz 离地高度。
        // face：Front 面朝本地 +Z，Right 面朝本地 +X。招牌按楼层切段挂在各层下面，倒塌时跟着那一层一起掉。
        // roof = false 的招牌不会伸进楼顶层（层数少的楼自动截短）。
        readonly struct SignBox
        {
            public readonly string n, face; public readonly float bx, by, bz, w, h, t; public readonly bool roof;
            public SignBox(string n, float bx, float by, float bz, float w, float h, float t, string face, bool roof = false)
            { this.n = n; this.bx = bx; this.by = by; this.bz = bz; this.w = w; this.h = h; this.t = t; this.face = face; this.roof = roof; }
        }
        static readonly Dictionary<Style, SignBox[]> Signs = new()
        {
            [Style.Glass] = new[]
            {
                // NEXUS 竖招牌：正面右侧，离开阳台外沿（y = -15.6）挂在支架上
                new SignBox("nexus_blade", -9.0f, -15.9f, 7.0f, 2.2f, 30f, 0.3f, "Front"),
                // NEON BAY 楼顶横幅：女儿墙前沿内侧，立在楼顶层
                new SignBox("neonbay", 0f, -8.8f, 5.4f, 15f, 3.4f, 0.35f, "Front", roof: true),
            },
            // 写字楼：楼顶广告牌支架前面（支架立柱前沿 y = -6.3）挂 WAVE COLA 印刷广告
            [Style.Office] = new[] { new SignBox("wavecola", 0f, -6.42f, 3.0f, 11f, 4.66f, 0.18f, "Front", roof: true) },
            // 停车楼：正面右角挂 PARKING 竖招牌（楼板外沿 y = -10.1）
            [Style.Garage] = new[] { new SignBox("parking_blade", -11.2f, -10.35f, 1.0f, 2.0f, 30f, 0.25f, "Front") },
            // 商铺：楼顶假立面（正面女儿墙升起那段）前挂 SEASIDE MART 店招
            [Style.Shop] = new[] { new SignBox("seaside", 0f, -6.62f, 0.12f, 8.4f, 2.1f, 0.1f, "Front", roof: true) },
            // 仓库：正面中间两个开间上方刷的公司名（钢柱凸出到 y = -9.2）
            [Style.Warehouse] = new[] { new SignBox("bayside", 0f, -9.28f, 2.65f, 11f, 2.75f, 0.05f, "Front") },
            // 加油站：路边价格立牌正面（立牌前沿 y = -7.6）
            // 底商住宅：店招带上 SEASIDE MART，楼顶广告牌支架前挂 WAVE COLA；老砖楼：店招带左段
            [Style.Mixed] = new[] { new SignBox("seaside", 0f, -8.36f, 3.5f, 3.2f, 0.8f, 0.06f, "Front"), new SignBox("wavecola", 0f, -5.12f, 2.95f, 9f, 3.82f, 0.2f, "Front", roof: true) },
            [Style.Brick] = new[] { new SignBox("seaside", 4.2f, -7.36f, 3.2f, 2.6f, 0.65f, 0.06f, "Front") },
            [Style.Gas] = new[] { new SignBox("sunfuel", 9.0f, -7.66f, 0.5f, 1.05f, 4.0f, 0.06f, "Front") },
            [Style.Deco] = new[]
            {
                // HOTEL 竖招牌：贴在正面招牌鳍前沿（y = -9.5）
                new SignBox("hotel_blade", 0f, -9.66f, 4.6f, 1.7f, 30f, 0.3f, "Front"),
                // BAR 方招牌：底座右侧墙（Blender -x = 本地 +X），靠近街角
                new SignBox("bar", -11.15f, -3.5f, 1.0f, 2.6f, 2.6f, 0.25f, "Right"),
            },
        };

        readonly Dictionary<string, Material> signMats = new();
        Material SignMaterial(string n)
        {
            if (signMats.TryGetValue(n, out var m) && m) return m;
            m = new Material(CityMaterials.Toon) { name = "sign_" + n, enableInstancing = true };
            m.SetTexture("_BaseMap", Resources.Load<Texture2D>($"Signs/{n}"));
            m.SetTexture("_EmissionMap", Resources.Load<Texture2D>($"Signs/{n}_emis"));
            m.SetColor("_EmissionColor", new Color(3.2f, 3.2f, 3.2f));
            m.SetFloat("_NightGlow", night ? 1.4f : 0.18f);   // 白天也留一点灯管的亮
            m.SetFloat("_OutlineWidth", 0);
            signMats[n] = m;
            return m;
        }

        void AddSigns(Building b, ModuleSet set, List<Transform> storeys, float sx, float sy, float sz, float topY)
        {
            if (!Signs.TryGetValue(b.style, out var list)) return;
            var boardMat = CityMaterials.Solid("#2a2238");
            int n = storeys.Count;
            float crownY = storeys[n - 1].localPosition.y;
            foreach (var sg in list)
            {
                // 招牌在楼本地的竖向范围（楼顶招牌以楼顶层地面为基准）
                float y0 = (sg.roof ? crownY : 0) + sg.bz * sy;
                float y1 = y0 + sg.h * sy;
                if (!sg.roof) y1 = Mathf.Min(y1, crownY - 0.15f * sy);
                if (y1 - y0 < 0.5f * sy) continue;
                bool front = sg.face == "Front";
                float W = sg.w * (front ? sx : sz), T = sg.t * (front ? sz : sx);
                var mat = SignMaterial(sg.n);
                // 招牌图绝不拉伸：按贴图宽高比放进可用的框里（楼的 sx/sy 不等、楼层少被截短，都会让框的比例变）。
                // 竖招牌贴着框的上端、楼顶招牌坐在楼顶、其余居中；铁皮底板和图一样大
                var tex = mat.GetTexture("_BaseMap");
                if (tex)
                {
                    float asp = tex.width / (float)tex.height, H = y1 - y0;
                    if (W / H > asp) W = H * asp;
                    else
                    {
                        float nh = W / asp;
                        if (sg.h > sg.w * 3) y0 = y1 - nh;
                        else if (sg.roof) y1 = y0 + nh;
                        else { float mid = (y0 + y1) * 0.5f; y0 = mid - nh * 0.5f; y1 = mid + nh * 0.5f; }
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    float sb = storeys[i].localPosition.y, st = i + 1 < n ? storeys[i + 1].localPosition.y : topY;
                    float a = Mathf.Max(y0, sb), c = Mathf.Min(y1, st);
                    if (c - a < 0.001f) continue;
                    var host = storeys[i];
                    var basePos = new Vector3(-sg.bx * sx, a - sb, -sg.by * sz);
                    // 底板（深色铁皮盒子）+ 正面贴图面片（按这一段在整块招牌里的比例取 UV，拼起来是一整张图）
                    var board = Part(host, CityMeshes.Box(), boardMat, basePos, front ? new Vector3(W, c - a, T) : new Vector3(T, c - a, W), "SignBoard_" + sg.n);
                    board.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var off = front ? new Vector3(0, 0, T * 0.5f + 0.01f * sz) : new Vector3(T * 0.5f + 0.01f * sx, 0, 0);
                    float v0 = (a - y0) / (y1 - y0), v1 = (c - y0) / (y1 - y0);
                    var face = Part(host, CityMeshes.SignQuad(v0, v1), mat, basePos + off, new Vector3(W, c - a, 1), "SignFace_" + sg.n,
                        front ? Quaternion.identity : Quaternion.Euler(0, 90, 0));
                    face.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        // ───────── 楼上的涂鸦（11-08 用户：涂鸦可以贴在任何地方，比如贴在楼上）─────────
        // 约三成普通楼：楼顶刷一块大涂鸦（站着俯视城市最显眼）、朝玩家那一侧的侧墙贴海报 / 喷漆。
        // 每张都挂在某一层下面（倒塌时跟着那层掉）。贴之前朝那层的网格打 5 条射线（中心 + 四角），
        // 五点离墙面的深度差不超过贴纸高的 3% 才贴 —— 只贴在平整的墙 / 屋面上，不悬空、不插进阳台和设备。
        static readonly string[] WallArt = { "poster_1", "poster_2", "poster_3", "poster_4", "doodle_jelly", "doodle_claw", "doodle_crown", "doodle_bolt" };
        static readonly string[] RoofArt = { "claw_print", "doodle_claw", "doodle_jelly", "doodle_crown", "doodle_star", "doodle_heart_x", "doodle_eye" };
        readonly Dictionary<string, Material> grafMats = new();
        Material GrafMat(string tex)
        {
            if (grafMats.TryGetValue(tex, out var m) && m) return m;
            m = new Material(Shader.Find("Monster/ComicCard")) { name = "graf_" + tex, renderQueue = 2990 };
            m.mainTexture = Resources.Load<Texture2D>("UI/" + tex);
            m.color = night ? new Color(0.6f, 0.58f, 0.68f, 0.95f) : new Color(0.88f, 0.85f, 0.82f, 0.95f);   // 不受光的“印刷品”：按昼夜压亮度
            return grafMats[tex] = m;
        }

        void AddGraffiti(Building b, List<Transform> storeys, List<Mesh> meshes, float s)
        {
            if (b.kind != BuildingKind.House || storeys.Count < 3) return;
            var rng = new Rng((uint)b.seed * 7 + 3);
            if (rng.Next() > 0.4f) return;
            var cam = Camera.main; var eye = cam ? cam.transform.position : Vector3.up * 1.6f;
            bool wallSign = Signs.TryGetValue(b.style, out var sl) && System.Array.Exists(sl, g => g.face == "Right");
            int done = 0;
            // 侧墙：朝玩家那一侧（±X 里朝向玩家的那面），一张跨 2~3 层的大海报 / 喷漆（单层太小，站着看不见）
            if (rng.Next() < 0.75f)
            {
                var root = storeys[0];
                float side = Vector3.Dot(root.TransformDirection(Vector3.right), eye - root.position) > 0 ? 1 : -1;
                int span = Mathf.Min(4, storeys.Count - 1);   // 从底商层（街面）往上最多跨 4 层，不进楼顶层
                if (!(wallSign && side > 0) && span >= 2)
                {
                    string tex = WallArt[(int)(rng.Next() * WallArt.Length) % WallArt.Length];
                    for (int k = 0; k < 12 && done == 0; k++)
                    {
                        int sp = Mathf.Max(2, span - k / 4);   // 贴不上就换位置、再缩短一层
                        int i0 = Mathf.Clamp((int)(rng.Next() * (storeys.Count - sp)), 0, storeys.Count - 1 - sp);
                        float zc = (rng.Next() - 0.5f) * meshes[i0].bounds.size.z * s * 0.5f;
                        if (WallDecal(storeys, meshes, i0, sp, s, tex, side, zc)) done++;
                    }
                }
            }
            // 楼顶：屋面靠后半边（前沿常有招牌板和女儿墙），平铺一大块
            if (rng.Next() < 0.7f)
            {
                int i = storeys.Count - 1; var mb = meshes[i].bounds;
                string tex = RoofArt[(int)(rng.Next() * RoofArt.Length) % RoofArt.Length];
                float h = Mathf.Min(mb.size.x, mb.size.z) * s * 0.46f;
                for (int k = 0; k < 8; k++)
                {
                    var c = new Vector3((rng.Next() - 0.5f) * mb.size.x * s * 0.4f, 0, -(0.1f + rng.Next() * 0.25f) * mb.size.z * s);
                    if (Decal(storeys[i], meshes[i], s, tex, Vector3.up, c, h, rng.Next() * 360)) break;
                }
            }
        }

        // 侧墙大海报：竖向跨 span 层，按层切片挂在各层下面（UV 拼成一整张，倒塌时各片跟着自己那层掉）。
        // 每一片都朝所在那层的网格打 5 条射线，所有点深度差不超过海报高 2% 才贴；海报离墙面 1.2%（不闪不穿）
        bool WallDecal(List<Transform> st, List<Mesh> ms, int i0, int span, float s, string tex, float side, float zc)
        {
            var t = Resources.Load<Texture2D>("UI/" + tex); if (!t) return false;
            int i1 = i0 + span - 1;
            float y0 = st[i0].localPosition.y, y1 = st[i1].localPosition.y + ms[i1].bounds.max.y * s;
            float H = (y1 - y0) * 0.86f, w = H * t.width / t.height, yb = y0 + (y1 - y0 - H) * 0.5f, yt = yb + H;
            var n = new Vector3(side, 0, 0); float L = 60 * s, best = float.MinValue, worst = float.MaxValue;
            for (int j = i0; j <= i1; j++)
            {
                float sy = st[j].localPosition.y, a = Mathf.Max(yb, sy), c = Mathf.Min(yt, sy + ms[j].bounds.max.y * s);
                if (c - a < 1e-5f) continue;
                // 3 列 × 4 行网格采样（只打 5 个点会漏掉窗口空调这种小凸起）
                for (int gi = 0; gi < 12; gi++)
                {
                    float u = (gi % 3 - 1) * 0.47f, v = 0.04f + (gi / 3) * 0.307f;
                    var p = new Vector3(0, Mathf.Lerp(a, c, v) - sy, zc + u * w);
                    if (!RayMesh(ms[j], s, p + n * L, -n, out float d)) return false;
                    best = Mathf.Max(best, L - d); worst = Mathf.Min(worst, L - d);
                }
                // 这一层上别的东西（招牌 / 空调 / 别的涂鸦）不能落在海报范围里
                float inv = 1 / Mathf.Max(1e-5f, st[j].lossyScale.x);
                foreach (var cr in st[j].GetComponentsInChildren<Renderer>())
                {
                    if (cr.name == "Body") continue;
                    var bc = st[j].InverseTransformPoint(cr.bounds.center); var ext = cr.bounds.extents * inv;
                    if (bc.x * side < -ext.magnitude) continue;
                    if (Mathf.Abs(bc.z - zc) < w * 0.5f + Mathf.Max(ext.z, ext.x) && bc.y + ext.y > a - sy && bc.y - ext.y < c - sy) return false;
                }
            }
            if (best - worst > H * 0.03f) return false;
            float depth = best + H * 0.012f;
            var mat = GrafMat(tex);
            for (int j = i0; j <= i1; j++)
            {
                float sy = st[j].localPosition.y, a = Mathf.Max(yb, sy), c = Mathf.Min(yt, sy + ms[j].bounds.max.y * s);
                if (c - a < 1e-5f) continue;
                var go = new GameObject("Graffiti_" + tex); go.transform.SetParent(st[j], false);
                go.transform.localPosition = new Vector3(side * depth, a - sy, zc);
                go.transform.localRotation = Quaternion.LookRotation(n, Vector3.up); go.transform.localScale = new Vector3(w, c - a, 1);
                go.AddComponent<MeshFilter>().sharedMesh = CityMeshes.SignQuad((a - yb) / H, (c - yb) / H);
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
            return true;
        }

        // 贴一张：n = 贴的面朝外的方向（层本地），c = 贴纸中心的大概位置（沿 n 方向的分量由射线求），h = 贴纸高
        bool Decal(Transform storey, Mesh mesh, float s, string tex, Vector3 n, Vector3 c, float h, float spin)
        {
            var t = Resources.Load<Texture2D>("UI/" + tex); if (!t) return false;
            float w = h * t.width / t.height;
            var rot = Quaternion.LookRotation(n, Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up) * Quaternion.Euler(0, 0, spin);
            var right = rot * Vector3.right; var up = rot * Vector3.up;
            float L = 60 * s, best = float.MinValue, worst = float.MaxValue;
            for (int gi = 0; gi < 9; gi++)   // 3×3 网格采样
            {
                float a = (gi % 3 - 1) * 0.45f, bb = (gi / 3 - 1) * 0.45f;
                var p = c + right * (a * w) + up * (bb * h);
                p -= n * Vector3.Dot(p, n);                     // 先放回过原点、垂直于 n 的平面上
                if (!RayMesh(mesh, s, p + n * L, -n, out float d)) return false;
                float depth = L - d;                            // 沿 n 离原点多远（越大越靠外）
                best = Mathf.Max(best, depth); worst = Mathf.Min(worst, depth);
            }
            if (best - worst > h * 0.03f) return false;
            var center = c - n * Vector3.Dot(c, n) + n * (best + h * 0.03f);
            // 不和这一层上已经有的东西（楼顶设备、招牌、别的涂鸦）叠在一起：按包围盒在贴面平面上的投影判断
            float rad = Mathf.Max(w, h) * 0.5f, inv = 1 / Mathf.Max(1e-5f, storey.lossyScale.x);
            foreach (var cr in storey.GetComponentsInChildren<Renderer>())
            {
                if (cr.name == "Body") continue;
                var bc = storey.InverseTransformPoint(cr.bounds.center) - center; var ext = cr.bounds.extents * inv;
                var flat = bc - n * Vector3.Dot(bc, n);
                if (Vector3.Dot(bc, n) < -ext.magnitude) continue;   // 在贴面后面（墙里 / 屋面下）的不算
                if (flat.magnitude < rad + Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z))) return false;
            }
            var go = new GameObject("Graffiti_" + tex); go.transform.SetParent(storey, false);
            go.transform.localPosition = center - up * (h * 0.5f);   // SignQuad 原点在底边中点
            go.transform.localRotation = rot; go.transform.localScale = new Vector3(w, h, 1);
            go.AddComponent<MeshFilter>().sharedMesh = CityMeshes.SignQuad(0, 1);
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = GrafMat(tex);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return true;
        }

        // 射线 × 一层的网格（层本地坐标；网格在层下面转了 180° 再按 s 缩放），返回最近的命中距离
        static readonly Dictionary<(Mesh, float), (Vector3[] v, int[] t)> rayCache = new();
        static bool RayMesh(Mesh mesh, float s, Vector3 o, Vector3 d, out float dist)
        {
            dist = float.MaxValue;
            if (!rayCache.TryGetValue((mesh, s), out var g))
            {
                var src = mesh.vertices; var v = new Vector3[src.Length];
                for (int i = 0; i < v.Length; i++) v[i] = new Vector3(-src[i].x * s, src[i].y * s, -src[i].z * s);
                g = (v, mesh.triangles); if (rayCache.Count > 64) rayCache.Clear(); rayCache[(mesh, s)] = g;
            }
            var V = g.v; var T = g.t;
            for (int i = 0; i < T.Length; i += 3)
            {
                Vector3 a = V[T[i]], e1 = V[T[i + 1]] - a, e2 = V[T[i + 2]] - a;
                var pv = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, pv);
                if (Mathf.Abs(det) < 1e-9f) continue;
                float inv = 1 / det; var tv = o - a;
                float u = Vector3.Dot(tv, pv) * inv; if (u < 0 || u > 1) continue;
                var qv = Vector3.Cross(tv, e1); float vv = Vector3.Dot(d, qv) * inv; if (vv < 0 || u + vv > 1) continue;
                float tt = Vector3.Dot(e2, qv) * inv;
                if (tt > 0 && tt < dist) dist = tt;
            }
            return dist < float.MaxValue;
        }

        // 三渲二材质（Monster/CityToon）：手绘图集 × 三阶色带 + 平滑法线描边；窗灯 / 霓虹走自发光遮罩，夜里才亮
        readonly Dictionary<(Style, int), Material> moduleMats = new();
        Material ModuleMaterial(Style s, ModuleSet set, int variant)
        {
            if (moduleMats.TryGetValue((s, variant), out var m) && m) return m;
            m = new Material(CityMaterials.Toon) { name = $"module_{s}_{variant}", enableInstancing = true };
            m.SetTexture("_BaseMap", set.albedos.Count > variant ? set.albedos[variant] : set.albedo);
            if (set.emission) m.SetTexture("_EmissionMap", set.emission);
            m.SetFloat("_NightGlow", night ? 2.25f : 0);   // 11-14：万家灯火 —— 亮着的窗从四成加到六成、亮度 ×2.25（环境和楼体受光同时压暗，见 DuskSky）
            m.SetFloat("_RandomLit", 1);
            m.SetFloat("_LitFrac", 0.6f);
            moduleMats[(s, variant)] = m;
            return m;
        }

        void BuildHouse(Building b, BuildingView view, Rng rng)
        {
            if (BuildFromModules(b, view)) return;
            var pal = CityMaterials.Palette[b.style];
            string col = pal[b.seed % pal.Length];
            string acc = CityMaterials.Neon[(b.seed >> 3) % CityMaterials.Neon.Length];
            int variant = (b.seed >> 5) % 3;
            var face = CityMaterials.Facade(b.style, col, acc, variant, night);
            var roof = CityMaterials.Solid(CityMaterials.Roof[b.style]);
            float tile = CityMaterials.Tile[b.style];
            int ru = Mathf.Max(1, Mathf.RoundToInt(b.fw / tile)), rv = Mathf.Max(1, Mathf.RoundToInt(b.fd / tile));
            var storeys = new List<Transform>();
            if (b.style == Style.Gas) { storeys.Add(GasStation(view.transform, b, face, acc)); view.storeys = storeys.ToArray(); return; }
            for (int i = 0; i < b.maxH; i++)
            {
                var s = new GameObject($"Storey_{i}").transform;
                s.SetParent(view.transform, false);
                s.localPosition = new Vector3(0, i * b.modH, 0);
                // 层主体（立面材质）+ 顶 / 底同材质。楼板线让每层看得出来
                Part(s, CityMeshes.Box(ru, rv), face, Vector3.zero, new Vector3(b.fw, b.modH, b.fd), "Body");
                if (b.style == Style.Office || b.style == Style.Apt || b.style == Style.Deco)
                    Part(s, CityMeshes.Box(), CityMaterials.Solid(b.style == Style.Deco ? "#ffffff" : "#8a7f78"), new Vector3(0, b.modH * 0.94f, 0), new Vector3(b.fw * 1.03f, b.modH * 0.06f, b.fd * 1.03f), "Slab");
                if (i == 0 && (b.style == Style.Shop || (b.style == Style.Deco && b.seed % 2 == 0)))
                    Part(s, CityMeshes.Box(), CityMaterials.Solid(b.style == Style.Shop ? acc : CityMaterials.Neon[b.seed % 6]), new Vector3(0, b.modH * 0.62f, b.fd * 0.56f), new Vector3(b.fw, b.modH * 0.07f, b.fd * 0.16f), "Awning", Quaternion.Euler(14, 0, 0));
                storeys.Add(s);
            }
            view.storeys = storeys.ToArray();
            var crown = new GameObject("Crown").transform;
            crown.SetParent(view.transform, false);
            crown.localPosition = new Vector3(0, b.maxH * b.modH, 0);
            view.crown = crown;
            BuildCrown(b, crown, face, roof, col, acc, ru, rv);
        }

        void BuildCrown(Building b, Transform t, Material face, Material roof, string col, string acc, int ru, int rv)
        {
            float W = b.fw, D = b.fd, M = b.modH;
            int v = b.seed % 3;
            float neonK = night ? 1.8f : 1.2f;
            switch (b.style)
            {
                case Style.Glass:
                    {
                        float k, y;
                        if (v == 0)
                        {
                            Part(t, CityMeshes.Box(Mathf.Max(1, ru * 3 / 4), Mathf.Max(1, rv * 3 / 4), 1.6f), face, Vector3.zero, new Vector3(W * 0.72f, M * 1.6f, D * 0.72f), "Setback");
                            Part(t, CityMeshes.Cylinder(8), CityMaterials.Solid("#d8dde3"), new Vector3(0, M * 1.6f, 0), new Vector3(0.003f, M * 3.2f, 0.003f), "Spire");
                            Part(t, CityMeshes.Box(), CityMaterials.Glow("#ff3344", 2), new Vector3(0, M * 4.8f, 0), new Vector3(0.004f, 0.004f, 0.004f), "Beacon");
                            k = 0.72f; y = M * 1.6f;
                        }
                        else if (v == 1)
                        {
                            Part(t, CityMeshes.Box(), CityMaterials.Solid("#46505c"), Vector3.zero, new Vector3(W * 1.02f, M * 0.35f, D * 1.02f), "Parapet");
                            Part(t, CityMeshes.Cylinder(20), CityMaterials.Solid("#3a4049"), new Vector3(0, M * 0.35f, 0), new Vector3(W * 0.62f, M * 0.12f, D * 0.62f), "Helipad");
                            Part(t, CityMeshes.Box(), CityMaterials.Solid("#f0d24a"), new Vector3(0, M * 0.47f, 0), new Vector3(W * 0.25f, 0.0008f, D * 0.06f), "H");
                            k = 1.02f; y = M * 0.36f;
                        }
                        else
                        {
                            Part(t, CityMeshes.Box(Mathf.Max(1, ru * 4 / 5), Mathf.Max(1, rv * 4 / 5)), face, Vector3.zero, new Vector3(W * 0.8f, M, D * 0.8f), "Step1");
                            Part(t, CityMeshes.Box(Mathf.Max(1, ru / 2), Mathf.Max(1, rv / 2)), face, new Vector3(0, M, 0), new Vector3(W * 0.55f, M, D * 0.55f), "Step2");
                            k = 0.55f; y = M * 2;
                        }
                        // 顶部霓虹轮廓线
                        var neon = CityMaterials.Glow(CityMaterials.Neon[b.seed % 6], neonK);
                        float th = 0.0016f;
                        Part(t, CityMeshes.Box(), neon, new Vector3(0, y, D * k / 2), new Vector3(W * k, th * 2, th), "NeonN");
                        Part(t, CityMeshes.Box(), neon, new Vector3(0, y, -D * k / 2), new Vector3(W * k, th * 2, th), "NeonS");
                        Part(t, CityMeshes.Box(), neon, new Vector3(W * k / 2, y, 0), new Vector3(th, th * 2, D * k), "NeonE");
                        Part(t, CityMeshes.Box(), neon, new Vector3(-W * k / 2, y, 0), new Vector3(th, th * 2, D * k), "NeonW");
                        break;
                    }
                case Style.Office:
                    Part(t, CityMeshes.Box(), CityMaterials.Solid("#8a847c"), Vector3.zero, new Vector3(W * 1.03f, M * 0.25f, D * 1.03f), "Parapet");
                    Part(t, CityMeshes.Box(), CityMaterials.Solid("#b8b4ac"), new Vector3(W * 0.22f, M * 0.25f, -D * 0.15f), new Vector3(W * 0.3f, M * 0.7f, D * 0.35f), "Machine");
                    Part(t, CityMeshes.Cylinder(10), CityMaterials.Solid("#9aa6b0"), new Vector3(-W * 0.25f, M * 0.25f, D * 0.2f), new Vector3(W * 0.22f, M * 1.0f, W * 0.22f), "Tank");
                    break;
                case Style.Deco:
                    {
                        // 装饰艺术风：阶梯女儿墙 + 竖向霓虹招牌
                        var white = CityMaterials.Solid("#ffffff");
                        Part(t, CityMeshes.Box(), white, Vector3.zero, new Vector3(W * 1.03f, M * 0.3f, D * 1.03f), "Parapet");
                        Part(t, CityMeshes.Box(), CityMaterials.Solid(col), new Vector3(0, M * 0.3f, 0), new Vector3(W * 0.62f, M * 1.1f, D * 0.62f), "Mid");
                        Part(t, CityMeshes.Box(), white, new Vector3(0, M * 1.4f, 0), new Vector3(W * 0.66f, M * 0.2f, D * 0.66f), "Cap");
                        Part(t, CityMeshes.Box(), CityMaterials.Solid(col), new Vector3(0, M * 1.6f, 0), new Vector3(W * 0.32f, M * 0.9f, D * 0.32f), "Top");
                        float fh = M * 3.2f;
                        Part(t, CityMeshes.Box(), CityMaterials.Solid("#262030"), new Vector3(0, -fh * 0.55f, D * 0.56f), new Vector3(W * 0.12f, fh, D * 0.16f), "Fin");
                        Part(t, CityMeshes.Box(), CityMaterials.Glow(acc, neonK * 1.2f), new Vector3(0, -fh * 0.5f, D * 0.65f), new Vector3(W * 0.07f, fh * 0.88f, 0.0012f), "FinNeon");
                        break;
                    }
                case Style.Apt:
                    Part(t, CityMeshes.Box(), CityMaterials.Solid("#8d8174"), Vector3.zero, new Vector3(W * 1.03f, M * 0.22f, D * 1.03f), "Parapet");
                    Part(t, CityMeshes.Box(), CityMaterials.Solid(col), new Vector3(-W * 0.25f, M * 0.22f, D * 0.2f), new Vector3(W * 0.32f, M * 0.7f, D * 0.3f), "Stair");
                    if (b.seed % 3 == 1) Part(t, CityMeshes.Box(), CityMaterials.Solid("#5e7aa3"), new Vector3(W * 0.15f, M * 0.22f, -D * 0.2f), new Vector3(W * 0.45f, M * 0.06f, D * 0.3f), "Solar");
                    break;
                case Style.Garage:
                    {
                        Part(t, CityMeshes.Box(), CityMaterials.Solid("#9a958c"), Vector3.zero, new Vector3(W * 1.02f, M * 0.3f, D * 1.02f), "Parapet");
                        var cars = new[] { "#d9534f", "#f4f1ea", "#2f7bbf", "#f0ad4e" };
                        for (int i = 0; i < 3; i++) Part(t, CityMeshes.Box(), CityMaterials.Solid(cars[(b.seed + i) % 4]), new Vector3(-W * 0.3f + i * W * 0.3f, M * 0.3f, (i % 2 - 0.5f) * D * 0.4f), new Vector3(W * 0.16f, M * 0.5f, D * 0.28f), "Car");
                        break;
                    }
                case Style.Shop:
                    Part(t, CityMeshes.Box(), CityMaterials.Solid("#2a2430"), new Vector3(0, 0, D * 0.35f), new Vector3(W * 0.75f, M * 0.9f, D * 0.06f), "SignBoard");
                    Part(t, CityMeshes.Box(), CityMaterials.Glow(acc, neonK), new Vector3(0, M * 0.5f, D * 0.39f), new Vector3(W * 0.6f, M * 0.18f, 0.0012f), "Sign1");
                    Part(t, CityMeshes.Box(), CityMaterials.Glow(acc, neonK), new Vector3(-W * 0.08f, M * 0.2f, D * 0.39f), new Vector3(W * 0.42f, M * 0.14f, 0.0012f), "Sign2");
                    break;
                case Style.Villa:
                    Part(t, CityMeshes.Pyramid(), CityMaterials.Solid("#c8603a"), Vector3.zero, new Vector3(W * 1.12f, M * 1.3f, D * 1.12f), "Roof", Quaternion.Euler(0, 45, 0) * Quaternion.identity);
                    break;
                case Style.Warehouse:
                    Part(t, CityMeshes.Cylinder(14), CityMaterials.Solid(CityMaterials.Palette[Style.Warehouse][b.seed % 4]), new Vector3(-W * 0.51f, 0, 0), new Vector3(D * 1.02f, W * 1.02f, M * 0.9f), "Arch", Quaternion.Euler(0, 90, -90));
                    break;
            }
        }

        Transform GasStation(Transform parent, Building b, Material face, string acc)
        {
            var s = new GameObject("Storey_0").transform; s.SetParent(parent, false);
            float W = b.fw, D = b.fd, M = b.modH;
            Part(s, CityMeshes.Box(2, 1), face, new Vector3(0, 0, D * 0.27f), new Vector3(W * 0.5f, M, D * 0.45f), "Shop");
            var pole = CityMaterials.Solid("#d8d8d4");
            foreach (var x in new[] { -0.38f, 0.38f }) foreach (var z in new[] { -0.12f, -0.42f })
                Part(s, CityMeshes.Box(), pole, new Vector3(W * x, 0, D * z), new Vector3(W * 0.05f, M * 1.3f, W * 0.05f), "Pole");
            Part(s, CityMeshes.Box(), CityMaterials.Solid("#f4f4f0"), new Vector3(0, M * 1.3f, -D * 0.27f), new Vector3(W, M * 0.18f, D * 0.55f), "Canopy");
            Part(s, CityMeshes.Box(), CityMaterials.Glow(acc, night ? 1.8f : 1.2f), new Vector3(0, M * 1.28f, -D * 0.27f), new Vector3(W * 1.01f, M * 0.06f, D * 0.56f), "CanopyNeon");
            foreach (var x in new[] { -0.2f, 0.2f }) Part(s, CityMeshes.Box(), CityMaterials.Solid(acc), new Vector3(W * x, 0, -D * 0.27f), new Vector3(W * 0.1f, M * 0.5f, W * 0.08f), "Pump");
            return s;
        }

        // 封印塔：深紫方尖碑，四角发光符文条，顶上光环 + 光球；外面一层同色护盾
        // ───────── 封印塔 / 电站（Tools/blender_seal.py：色板 UV，格 4 = 塔的颜色、夜里白天都发光）─────────
        static readonly string[] SealPal = { "#2c2a33", "#6f7480", "#c9c3b6", "#b9bec6", "#ffffff", "#f3c623", "#1b1b20", "#e8e2d6", "#b06a3a", "#2b3a4c", "#3f6a52" };
        readonly Dictionary<Color, Material> sealMats = new();
        Material SealMat(Color team)
        {
            if (sealMats.TryGetValue(team, out var m) && m) return m;
            var pal = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "seal_palette" };
            var emi = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "seal_palette_emis" };
            for (int i = 0; i < 16; i++) { pal.SetPixel(i, 0, i < SealPal.Length ? CityMaterials.Hex(SealPal[i]) : Color.gray); emi.SetPixel(i, 0, Color.black); }
            pal.SetPixel(4, 0, team); emi.SetPixel(4, 0, team);
            pal.Apply(); emi.Apply();
            m = new Material(CityMaterials.Toon) { name = "seal_" + ColorUtility.ToHtmlStringRGB(team), enableInstancing = true };
            m.SetTexture("_BaseMap", pal); m.SetTexture("_EmissionMap", emi);
            m.SetColor("_EmissionColor", new Color(2.2f, 2.2f, 2.2f));
            m.SetFloat("_NightGlow", 1);   // 符文槽白天也亮（是玩法目标，要一眼看到）
            sealMats[team] = m;
            return m;
        }
        static Mesh SealMesh(string n)
        {
            var go = Resources.Load<GameObject>($"Modules/seal/{n}");
            return go ? go.GetComponentInChildren<MeshFilter>().sharedMesh : null;
        }

        // 封印塔：第 0 层 = 底座，往上每层一段塔身（越往上越细），最上面塔冠；每段一个物体，拆的时候一段段掉
        void BuildTower(Building b, BuildingView view)
        {
            var mat = SealMat(b.color);
            Mesh baseM = SealMesh("tower_base"), segM = SealMesh("tower_seg"), crownM = SealMesh("tower_crown");
            float realW = 12.8f, segH = 6.6f;                    // 模型真实尺寸（米）：底座宽、一段高
            float s = b.fw / realW;                               // 世界 / 米（宽度对上地块）
            float sy = b.modH / segH;                             // 高度对上逻辑层高
            var storeys = new List<Transform>();
            for (int i = 0; i < b.maxH; i++)
            {
                var st = new GameObject($"Storey_{i}").transform; st.SetParent(view.transform, false);
                st.localPosition = new Vector3(0, i * b.modH, 0);
                float k = i == 0 ? 1 : 1.15f - i * 0.06f;        // 塔身段比底座窄，越往上越细
                Part(st, i == 0 ? baseM : segM, mat, Vector3.zero, new Vector3(s * k, sy, s * k), "Body");
                storeys.Add(st);
            }
            view.storeys = storeys.ToArray();
            var crown = new GameObject("Crown").transform; crown.SetParent(view.transform, false);
            crown.localPosition = new Vector3(0, b.maxH * b.modH, 0);
            float kc = 1.15f - b.maxH * 0.06f;
            Part(crown, crownM, mat, Vector3.zero, new Vector3(s * kc, s * kc, s * kc), "Body");
            view.crown = crown;
            // 护盾：半透明椭球（MR 里也能看见）
            var shield = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(shield.GetComponent<Collider>());
            shield.name = "Shield"; shield.transform.SetParent(view.transform, false);
            float H = b.Height;
            shield.transform.localPosition = new Vector3(0, H * 0.5f, 0);
            // 11-11：罩子原来直径 = 塔宽 × 2.5（约 15cm，比整个街区还大，所以塔必须独占一个街区）；收到刚好罩住塔身（× 1.46），旁边的地块才能盖楼
            shield.transform.localScale = new Vector3(b.Foot * 1.46f, H * 1.2f, b.Foot * 1.46f);
            var sm = new Material(Shader.Find("Monster/SealShield")) { name = "shield" };
            sm.SetColor("_Color", b.color * 1.6f);
            sm.SetFloat("_GroundY", view.transform.position.y);
            var shr = shield.GetComponent<MeshRenderer>(); shr.sharedMaterial = sm;
            shr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; shr.receiveShadows = false;
        }

        // 电站：一整块（一层），模型 16 × 12 米，等比缩到地块；正面（控制室、电缆沟）朝街
        void BuildStation(Building b, BuildingView view)
        {
            var st = new GameObject("Storey_0").transform; st.SetParent(view.transform, false);
            float s = Mathf.Min(b.fw / 16f, b.fd / 12f);
            Part(st, SealMesh("station"), SealMat(b.color), Vector3.zero, Vector3.one * s, "Body", Quaternion.Euler(0, 180, 0));
            view.storeys = new[] { st };
        }

        // 远景：桌面城区后方两排楼群剪影（不可拆，让城市看起来比桌子大）
        public BuildingView View(int id) => views.TryGetValue(id, out var v) ? v : null;
    }
}
