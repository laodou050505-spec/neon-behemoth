// 街道与路边（11-07）：人行道上的路灯（夜里地面光晕）/ 红绿灯 / 行道树 / 棕榈 / 公交候车亭，
// 不盖楼的地块（停车场 / 街心公园 / 广场 / 球场 / 工地），以及马路上的车流（Traffic.cs）。
// 楼盖完以后才摆：每样东西都先查和楼的实际包围盒（含阳台、雨棚）不重叠，避免穿模。
// 静态小件最后按材质合并成少数几个网格（头显省绘制调用）。
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Monster
{
    public partial class CityBuilder
    {
        // 真实尺寸（米）→ 城市比例：和楼模块的平均缩放一致（20m 宽的楼约 15cm）
        const float K = 0.0075f;
        // 11-11 用户：车太小了，很难拿起来 —— 所有车（路上跑的、停着的）放大到 1.6 倍（轿车长 3.8cm，原来 2.4cm），公交本来就长，只放大一点；
        // 车位、路边停车的间距跟着放大。追兵里的警车 / 皮卡等是按轿车长度定大小的（EnemyView.CivLen），自动跟着变大
        public const float CarS = 1.6f, BusS = 1.15f;
        const float Side = 0.006f;     // 人行道中线离街区边缘的距离（人行道宽 CityConst.Walk）

        readonly Dictionary<Zone, List<Rect>> solids = new();   // 每个城区里楼的实际占地（分区坐标 a, b）

        Vector3 AB(Zone z, Vector3 world) { var d = world - z.origin; return new Vector3(Vector3.Dot(d, z.u), Vector3.Dot(d, z.v), d.y); }

        void CollectSolids()
        {
            foreach (var z in city.zones) { solids[z] = new List<Rect>(); buildingRects[z] = new List<Rect>(); }
            foreach (var b in city.buildings)
            {
                if (!views.TryGetValue(b.id, out var v) || !v) continue;
                var rs = v.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
                foreach (var r in rs)
                {
                    var bb = r.bounds;
                    for (int k = 0; k < 4; k++)
                    {
                        var p = AB(b.zone, new Vector3(k % 2 == 0 ? bb.min.x : bb.max.x, 0, k < 2 ? bb.min.z : bb.max.z));
                        a0 = Mathf.Min(a0, p.x); a1 = Mathf.Max(a1, p.x); b0 = Mathf.Min(b0, p.y); b1 = Mathf.Max(b1, p.y);
                    }
                }
                solids[b.zone].Add(Rect.MinMaxRect(a0, b0, a1, b1));
                buildingRects[b.zone].Add(Rect.MinMaxRect(a0, b0, a1, b1));
            }
            foreach (var o in city.opens) solids[o.zone].Add(new Rect(o.a - o.w / 2, o.b - o.d / 2, o.w, o.d));
        }

        bool Free(Zone z, float a, float b, float ra, float rb)
        {
            var r = Rect.MinMaxRect(a - ra, b - rb, a + ra, b + rb);
            foreach (var s in solids[z]) if (s.Overlaps(r)) return false;
            return true;
        }

        static float EdgeOf(Zone z, float a, float b) => Mathf.Min(Mathf.Min(a, z.w - a), Mathf.Min(b, z.h - b));

        // ───────── 材质 ─────────
        readonly Dictionary<string, Material> propMats = new();
        Material PM(string hex, float outline = 0.0026f)
        {
            string key = hex + outline;
            if (propMats.TryGetValue(key, out var m) && m) return m;
            m = new Material(CityMaterials.Toon) { name = "prop_" + hex, enableInstancing = true };
            m.SetColor("_BaseColor", CityMaterials.Hex(hex));
            m.SetFloat("_OutlineWidth", outline);
            propMats[key] = m;
            return m;
        }
        Material GlowM(string day, string nightHex, float kDay, float kNight) => night ? CityMaterials.Glow(nightHex, kNight) : CityMaterials.Glow(day, kDay);
        Material sigU, sigV, poolMat;
        Material[] sigUs, sigVs;   // 6 个相位组（每个路口一组，互相错开，全城不同时变灯）

        Material Pool()
        {
            if (poolMat) return poolMat;
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "lamp_pool" };
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float d = Mathf.Clamp01(new Vector2(x - 31.5f, y - 31.5f).magnitude / 31.5f);
                float a = Mathf.Pow(1 - d, 2.2f);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            poolMat = new Material(Shader.Find("Monster/CityGlowAdd")) { name = "lamp_pool" };
            poolMat.SetTexture("_BaseMap", tex);
            poolMat.SetColor("_Color", new Color(1f, 0.72f, 0.38f) * 0.55f);
            return poolMat;
        }

        // ───────── 街道总入口：楼盖完之后调用 ─────────
        void BuildStreets() { var it = BuildStreetsSteps(); while (it.MoveNext()) { } }
        /// <summary>分步版（BuildAsync 用）：每个城区的每一步之间让出一帧</summary>
        System.Collections.IEnumerator BuildStreetsSteps()
        {
            {
                // 楼顶设备（射线找空地，先于收集占地，不影响街道）。纯装饰：出错只是少几件屋顶设备，不能连累街道
                var rc = RoofClutterSteps();
                while (true)
                {
                    bool more; try { more = rc.MoveNext(); } catch (System.Exception ex) { Debug.LogError("[怪兽][生成] 楼顶设备出错，跳过：" + ex); more = false; }
                    if (!more) break;
                    yield return null;
                }
            }
            yield return null;
            CollectSolids();
            ComputeYards();
            yield return null;
            sigU = CityMaterials.Glow("#39ff7a", 1.6f); sigU = new Material(sigU) { name = "signal_u" };
            sigV = new Material(CityMaterials.Glow("#ff3030", 1.6f)) { name = "signal_v" };
            sigU.SetColor("_Color", Traffic.Light(true, 2f)); sigV.SetColor("_Color", Traffic.Light(false, 2f));
            sigUs = new Material[Traffic.Groups]; sigVs = new Material[Traffic.Groups];
            for (int g = 0; g < Traffic.Groups; g++)
            {
                sigUs[g] = new Material(sigU) { name = "signal_u" + g }; sigVs[g] = new Material(sigV) { name = "signal_v" + g };
                sigUs[g].SetColor("_Color", Traffic.Light(true, 2f + Traffic.GroupOffset(g))); sigVs[g].SetColor("_Color", Traffic.Light(false, 2f + Traffic.GroupOffset(g)));
            }
            foreach (var z in city.zones)
            {
                BuildGround(z); yield return null;
                var props = ZoneRoot(z, "Props");
                if (z.kind != ZoneKind.Cabinet) Intersections(z, props);
                Sidewalks(z, props); yield return null;
                foreach (var o in city.opens) if (o.zone == z) OpenLotProps(o, props);
                YardProps(z, props); yield return null;
                if (z.harbor) { BuildHarbor(z); yield return null; }
                StackThin(z, props);   // 贴地薄片拉开层间距（防深度闪烁）
                yield return null;
                if (!AuditMode) { var trees = SplitTrees(props, z); CombineStatic(props); CombineTreeCells(trees); }
                yield return null;
                if (z.kind != ZoneKind.Cabinet) BuildTraffic(z);
                yield return null;
            }
        }

        // ───────── 贴地薄片分层（11-08 用户：桌面城市地面不能闪烁、要严谨）─────────
        // 头显深度精度有限，贴地的东西（铺地、车位线、草坪块、泳池水面、路灯光斑……）彼此只差零点几毫米就会互相抢着显示。
        // 规则：薄片（厚 < 4mm 设计尺寸、离地 < 6mm）按原来的上下顺序重新排：每片顶面至少比它下面所有和它水平重叠的薄片高 Step，
        // 并且至少比地面网格（RoadsAndShadow，离地 0.8mm）高 Step。只往上抬、不改大小；抬起的缝隙不到 1.5mm（真实尺寸更小），看不出来。
        const float GroundY = 0.0008f, Step = 0.0024f;   // 11-13 用户：地面还在闪 → 层间距 1.6 → 2.4mm（设计尺寸；缩放后约 1.7mm）
        void StackThin(Zone z, Transform props)
        {
            var list = new List<(Transform t, Bounds b)>();
            foreach (var r in props.GetComponentsInChildren<MeshRenderer>())
            {
                var b = r.bounds; float lo = b.min.y - z.origin.y, hi = b.max.y - z.origin.y;
                if (b.size.y < 0.004f && hi < 0.006f && lo > -0.003f && (b.size.x > 0.004f || b.size.z > 0.004f)) list.Add((r.transform, b));
            }
            list.Sort((a, c) => a.b.max.y.CompareTo(c.b.max.y));
            var placed = new List<(Bounds b, float top)>();
            int moved = 0;
            foreach (var (t, b) in list)
            {
                float need = z.origin.y + GroundY + Step;
                foreach (var (pb, top) in placed)
                    if (pb.min.x < b.max.x && pb.max.x > b.min.x && pb.min.z < b.max.z && pb.max.z > b.min.z) need = Mathf.Max(need, top + Step);
                float lift = Mathf.Max(0, need - b.max.y);
                if (lift > 0) { t.position += Vector3.up * lift; moved++; }
                placed.Add((b, b.max.y + lift));
            }
            if (moved > 0) Debug.Log($"[怪兽] {z.id} 贴地薄片 {list.Count} 片，抬高 {moved} 片");
        }

        // ───────── 院子：楼按等比缩放后，地块里没被楼占的部分（前 / 后 / 左 / 右四条）按楼型布置 ─────────
        // 住宅：草坪 + 花坛 + 灌木 + 树（大块的后院有泳池）；写字楼 / 摩天楼：铺装前庭 + 花池 + 树 + 长椅 + 旗杆；
        // 老城区（砖楼 / 底商）：铺装内院 + 树 + 垃圾箱；商铺 / 仓库 / 停车楼 / 加油站：沥青场地 + 车位 + 停着的车（仓库还有集装箱、托盘）
        public struct Yard { public Zone z; public Rect r; public string kind; public bool front; public Building owner; }
        readonly List<Yard> yards = new();
        readonly Dictionary<Zone, List<Rect>> buildingRects = new();

        static string YardKind(Style s) => s switch
        {
            Style.Apt or Style.Condo or Style.Villa or Style.Deco => "lawn",
            Style.Glass or Style.Office or Style.Modern or Style.Spire => "forecourt",
            Style.Brick or Style.Mixed => "court",
            _ => "lot",
        };

        void ComputeYards()
        {
            yards.Clear();
            foreach (var b in city.buildings)
            {
                if (b.kind != BuildingKind.House)
                {
                    // 11-11 用户：楼旁边的地基范围太大、周围太空 —— 塔 / 电站所在的整块街区做成霓虹广场（铺地 + 塔色光环 + 灯柱 + 花池）
                    // 广场 = 这座塔 / 电站自己的那块地（原来一律按 2×2 整街区算，一格深的街区上广场铺到了人行道和马路上）
                    float pw = b.lotW > 0 ? b.lotW : b.fw + 0.02f, pd = b.lotD > 0 ? b.lotD : b.fd + 0.02f;
                    yards.Add(new Yard { z = b.zone, r = new Rect(b.a - pw / 2, b.b - pd / 2, pw, pd), kind = "plaza", owner = b });
                    continue;
                }
                if (!views.TryGetValue(b.id, out var v) || !v) continue;
                float lw = b.lotW > 0 ? b.lotW : b.fw, ld = b.lotD > 0 ? b.lotD : b.fd;   // 整块地块（不是楼的分配占地），院子铺满不留缝
                var lot = new Rect(b.a - lw / 2, b.b - ld / 2, lw, ld);
                // 楼的占地：只看底座那一层的墙体包围盒（雨棚、台阶算院子里的东西，不挡院子）
                var body = v.storeys != null && v.storeys.Length > 0 ? v.storeys[0].GetComponentInChildren<Renderer>() : null;
                if (!body) continue;
                var bb = body.bounds; float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    var p = AB(b.zone, new Vector3(k % 2 == 0 ? bb.min.x : bb.max.x, 0, k < 2 ? bb.min.z : bb.max.z));
                    a0 = Mathf.Min(a0, p.x); a1 = Mathf.Max(a1, p.x); b0 = Mathf.Min(b0, p.y); b1 = Mathf.Max(b1, p.y);
                }
                a0 = Mathf.Max(a0, lot.xMin); a1 = Mathf.Min(a1, lot.xMax); b0 = Mathf.Max(b0, lot.yMin); b1 = Mathf.Min(b1, lot.yMax);
                string kind = YardKind(b.style);
                const float minW = 0.008f;
                // 前（+b，临街 / 朝玩家）和后整条；左右两条夹在中间
                if (lot.yMax - b1 > minW) yards.Add(new Yard { z = b.zone, r = Rect.MinMaxRect(lot.xMin, b1, lot.xMax, lot.yMax), kind = kind, front = true });
                if (b0 - lot.yMin > minW) yards.Add(new Yard { z = b.zone, r = Rect.MinMaxRect(lot.xMin, lot.yMin, lot.xMax, b0), kind = kind });
                if (a0 - lot.xMin > minW) yards.Add(new Yard { z = b.zone, r = Rect.MinMaxRect(lot.xMin, b0, a0, b1), kind = kind });
                if (lot.xMax - a1 > minW) yards.Add(new Yard { z = b.zone, r = Rect.MinMaxRect(a1, b0, lot.xMax, b1), kind = kind });
            }
        }

        // 院子地面（GroundTexture 里调用；R 是画矩形，像素 = 分区坐标 × ppm）
        void PaintYards(Zone z, System.Action<int, int, int, int, Color> R, float ppm)
        {
            System.Func<string, Color> H_ = CityMaterials.Hex;
            foreach (var y in yards)
            {
                if (y.z != z) continue;
                int x0 = Mathf.RoundToInt(y.r.xMin * ppm), y0 = Mathf.RoundToInt(y.r.yMin * ppm), w = Mathf.RoundToInt(y.r.width * ppm), h = Mathf.RoundToInt(y.r.height * ppm);
                if (w < 2 || h < 2) continue;
                var rng = new Rng((uint)(x0 * 31 + y0 * 17));
                switch (y.kind)
                {
                    case "lawn":
                        R(x0, y0, w, h, H_("#74b05e"));
                        for (int i = 0; i < w * h / 160; i++) { int bw = rng.Int(3, 9), bh = rng.Int(3, 9); R(x0 + rng.Int(0, Mathf.Max(0, w - bw)), y0 + rng.Int(0, Mathf.Max(0, h - bh)), bw, bh, H_("#67a253")); }
                        R(x0, y0, w, 2, H_("#c9bfae")); R(x0, y0 + h - 2, w, 2, H_("#c9bfae"));
                        break;
                    case "plaza":
                        {
                            // 深紫铺地 + 方砖缝 + 以楼为中心两圈塔色方环 + 四边青色导向线
                            R(x0, y0, w, h, H_("#3d3550"));
                            for (int x = 0; x < w; x += 5) R(x0 + x, y0, 1, h, H_("#352e46"));
                            for (int yy = 0; yy < h; yy += 5) R(x0, y0 + yy, w, 1, H_("#352e46"));
                            var tc = Color.Lerp(y.owner.color, Color.white, 0.15f);
                            float cx = (y.owner.a - y.r.xMin) * ppm, cy = (y.owner.b - y.r.yMin) * ppm, rf = Mathf.Max(y.owner.fw, y.owner.fd) * 0.5f * ppm;
                            foreach (var (rr, th) in new[] { (rf + 4f, 1.6f), (rf + 9f, 1.1f) })
                                for (int py = 0; py < h; py++) for (int px = 0; px < w; px++)
                                {
                                    float d = Mathf.Abs(Mathf.Max(Mathf.Abs(px - cx), Mathf.Abs(py - cy)) - rr);
                                    if (d < th) R(x0 + px, y0 + py, 1, 1, tc);
                                }
                            int icx = Mathf.RoundToInt(cx), icy = Mathf.RoundToInt(cy), irf = Mathf.RoundToInt(rf);
                            if (icy - irf > 0) R(x0 + icx - 1, y0, 2, icy - irf, H_("#2fd8e8"));
                            if (h - icy - irf > 0) R(x0 + icx - 1, y0 + icy + irf, 2, h - icy - irf, H_("#2fd8e8"));
                            break;
                        }
                    case "forecourt":
                        R(x0, y0, w, h, H_("#d6ccbb"));
                        for (int x = 0; x < w; x += 6) R(x0 + x, y0, 1, h, H_("#c3b8a5"));
                        for (int yy = 0; yy < h; yy += 6) R(x0, y0 + yy, w, 1, H_("#c3b8a5"));
                        break;
                    case "court":
                        R(x0, y0, w, h, H_("#b9ad9a"));
                        for (int x = 0; x < w; x += 4) R(x0 + x, y0, 1, h, H_("#a99c88"));
                        break;
                    default:
                        R(x0, y0, w, h, H_("#55555c"));
                        float sw = 2.7f * K * CarS * ppm;
                        if (h > w) for (float yy = 2; yy < h - 2; yy += sw) R(x0 + 1, y0 + Mathf.RoundToInt(yy), w - 2, 1, H_("#e9e4d6"));
                        else for (float x = 2; x < w - 2; x += sw) R(x0 + Mathf.RoundToInt(x), y0 + 1, 1, h - 2, H_("#e9e4d6"));
                        break;
                }
            }
        }

        void YardProps(Zone z, Transform t)
        {
            foreach (var y in yards)
            {
                if (y.z != z) continue;
                var rng = new Rng((uint)(y.r.xMin * 10007 + y.r.yMin * 7919));
                bool alongA = y.r.width >= y.r.height;
                float len = alongA ? y.r.width : y.r.height, thick = alongA ? y.r.height : y.r.width;
                Vector2 At(float s, float o) => alongA ? new Vector2(y.r.xMin + s, y.r.yMin + o) : new Vector2(y.r.xMin + o, y.r.yMin + s);
                switch (y.kind)
                {
                    case "plaza":
                        {
                            // 四角：带棕榈的方花池；靠前后两边：塔色霓虹灯柱
                            float m = 0.016f;
                            var corners = new[] { new Vector2(y.r.xMin + m, y.r.yMin + m), new Vector2(y.r.xMax - m, y.r.yMin + m), new Vector2(y.r.xMin + m, y.r.yMax - m), new Vector2(y.r.xMax - m, y.r.yMax - m) };
                            foreach (var c in corners)
                            {
                                if (!Free(z, c.x, c.y, 0.009f, 0.009f)) continue;
                                Part(t, CityMeshes.Box(), PM("#2a2436", 0.0014f), z.Point(c.x, c.y), new Vector3(0.016f, 0.004f, 0.016f), "PlazaPlanter");
                                Part(t, CityMeshes.Box(), PM("#4f9d55"), z.Point(c.x, c.y, 0.004f), new Vector3(0.013f, 0.0008f, 0.013f), "PlazaSoil");
                                Palm(t, z.Point(c.x, c.y, 0.0045f), rng.Range(0.055f, 0.075f), rng);
                            }
                            var tcol = "#" + ColorUtility.ToHtmlStringRGB(y.owner.color);
                            float mx = y.r.center.x;
                            foreach (var c in new[] { new Vector2(mx - y.r.width * 0.3f, y.r.yMin + 0.008f), new Vector2(mx + y.r.width * 0.3f, y.r.yMin + 0.008f), new Vector2(mx - y.r.width * 0.3f, y.r.yMax - 0.008f), new Vector2(mx + y.r.width * 0.3f, y.r.yMax - 0.008f) })
                            {
                                if (!Free(z, c.x, c.y, 0.003f, 0.003f)) continue;
                                Part(t, CityMeshes.Cylinder(6), PM("#1c1826", 0.0012f), z.Point(c.x, c.y), new Vector3(0.0018f, 0.028f, 0.0018f), "NeonPylon");
                                Part(t, CityMeshes.Box(), CityMaterials.Glow(tcol, 2.6f), z.Point(c.x, c.y, 0.012f), new Vector3(0.0024f, 0.016f, 0.0024f), "NeonPylonLit");
                                Part(t, CityMeshes.Sphere(), CityMaterials.Glow(tcol, 3f), z.Point(c.x, c.y, 0.029f), Vector3.one * 0.004f, "NeonPylonCap");
                            }
                            break;
                        }
                    case "lawn":
                        if (thick > 0.028f && len > 0.05f && rng.Chance(0.5f))   // 后院泳池（先放泳池并登记占地，再种灌木树木）
                        {
                            var c = At(len * 0.5f, thick * 0.5f);
                            float pw = Mathf.Min(len * 0.45f, 0.05f), pd = Mathf.Min(thick * 0.5f, 0.022f);
                            if (Free(z, c.x, c.y, (alongA ? pw : pd) * 0.5f, (alongA ? pd : pw) * 0.5f))
                                Pool(t, z, c, alongA ? pw : pd, alongA ? pd : pw);
                        }
                        for (float s = 0.01f; s < len - 0.008f; s += rng.Range(0.014f, 0.024f))
                        {
                            var p = At(s, thick * rng.Range(0.3f, 0.7f));
                            if (!Free(z, p.x, p.y, 0.006f, 0.006f)) continue;
                            if (thick > 0.016f && rng.Chance(0.55f)) Tree(t, z.Point(p.x, p.y), rng);
                            else Bush(t, z.Point(p.x, p.y), rng);
                        }
                        break;
                    case "forecourt":
                        for (float s = 0.012f; s < len - 0.01f; s += 0.026f)
                        {
                            var p = At(s, thick * 0.5f);
                            if (!Free(z, p.x, p.y, 0.007f, 0.007f)) continue;
                            Part(t, CityMeshes.Box(), PM("#cfc6b8", 0.0014f), z.Point(p.x, p.y), new Vector3(0.011f, 0.003f, 0.011f), "Planter");
                            Tree(t, z.Point(p.x, p.y, 0.003f), rng);
                            var bp = At(s + 0.013f, thick * 0.5f);
                            if (s + 0.013f < len - 0.006f && Free(z, bp.x, bp.y, 0.007f, 0.003f))
                                Part(t, CityMeshes.Box(), PM("#8a6a50", 0.0012f), z.Point(bp.x, bp.y, 0.0022f), alongA ? new Vector3(0.01f, 0.0012f, 0.003f) : new Vector3(0.003f, 0.0012f, 0.01f), "Bench");
                        }
                        if (y.front && len > 0.06f)
                            for (int i = -1; i <= 1; i++)
                            {
                                var fp = At(len * 0.5f + i * 0.008f, thick * 0.8f);
                                if (!Free(z, fp.x, fp.y, 0.002f, 0.002f) || !Claim(z.Point(fp.x, fp.y), 0.006f)) continue;
                                Part(t, CityMeshes.Cylinder(6), PM("#d9d9d9", 0.0008f), z.Point(fp.x, fp.y), new Vector3(0.0008f, 0.05f, 0.0008f), "FlagPole");
                                Part(t, CityMeshes.Box(), PM(new[] { "#d2333a", "#2f6fd6", "#f2c53a" }[i + 1], 0.0008f), z.Point(fp.x, fp.y, 0.042f) + z.u * 0.004f, new Vector3(0.008f, 0.005f, 0.0003f), "Flag", Face(z, Vector2.up));
                            }
                        break;
                    case "court":
                        for (float s = 0.012f; s < len - 0.008f; s += rng.Range(0.02f, 0.03f))
                        {
                            var p = At(s, thick * 0.5f);
                            if (!Free(z, p.x, p.y, 0.006f, 0.006f)) continue;
                            if (rng.Chance(0.6f)) Tree(t, z.Point(p.x, p.y), rng);
                            else Part(t, CityMeshes.Box(), PM(rng.Chance(0.5f) ? "#2f6b4f" : "#3d4a6b", 0.0012f), z.Point(p.x, p.y), new Vector3(0.008f, 0.005f, 0.005f), "Dumpster", Face(z, alongA ? Vector2.up : Vector2.right));
                        }
                        break;
                    default:
                        // 车位：院子够深就垂直停，否则顺着停
                        float sw = 2.7f * K * CarS, cl = 4.4f * K * CarS;
                        bool perp = thick > cl + 0.004f;
                        float step = perp ? sw : cl + 0.006f;
                        for (float s = step * 0.5f + 0.002f; s < len - step * 0.5f; s += step)
                        {
                            var p = At(s, thick * 0.5f);
                            if (!rng.Chance(0.45f) || !Free(z, p.x, p.y, 0.009f, 0.009f)) continue;
                            var dir = (alongA ^ perp) ? Vector2.right : Vector2.up;
                            var go = new GameObject("YardCar"); go.transform.SetParent(t, false);
                            go.transform.SetPositionAndRotation(z.Point(p.x, p.y), Face(z, rng.Chance(0.5f) ? dir : -dir));
                            var ck = PickCar(rng, false); go.AddComponent<MeshFilter>().sharedMesh = CarMesh(ck, PickColor(ck, rng), true);
                            go.AddComponent<MeshRenderer>().sharedMaterial = CarMat();
                        }
                        break;
                }
            }
        }

        void Bush(Transform t, Vector3 pos, Rng rng)
        {
            float r = rng.Range(0.006f, 0.01f);
            if (!Claim(pos, r * 0.5f)) return;
            Part(t, CityMeshes.Sphere(), PM(new[] { "#3f8f4e", "#4f9d55", "#5f9a48" }[rng.Int(0, 2)], 0.0014f), pos, new Vector3(r, r * 0.7f, r), "Bush", Quaternion.Euler(0, rng.Range(0, 360), 0));
        }

        void Pool(Transform t, Zone z, Vector2 c, float w, float d)
        {
            var p = z.Point(c.x, c.y);
            // 占地登记（11-08 体检：灌木长进泳池）：沿长边放三个圆盖住池子
            var along = (w >= d ? z.u : z.v); float half = Mathf.Max(w, d) * 0.5f, rr = Mathf.Min(w, d) * 0.5f + 0.003f;
            foreach (var k in new[] { -1f, 0f, 1f }) Claim(p + along * (half - rr) * k, rr, true);
            Part(t, CityMeshes.Box(), PM("#e9e4d8", 0.0012f), p, new Vector3(w + 0.004f, 0.0014f, d + 0.004f), "PoolDeck", Face(z, Vector2.up));
            Part(t, CityMeshes.Box(), GlowM("#5fd0e8", "#3ec8f0", 0.95f, 1.2f), p + Vector3.up * 0.0014f, new Vector3(w, 0.0003f, d), "PoolWater", Face(z, Vector2.up));
            for (int i = 0; i < 2; i++)
                Part(t, CityMeshes.Box(), PM("#f2f2ee", 0.0010f), z.Point(c.x + (i == 0 ? -1 : 1) * w * 0.25f, c.y + d * 0.5f + 0.004f, 0.0014f), new Vector3(0.002f, 0.0008f, 0.0045f), "Lounger", Face(z, Vector2.up));
        }

        // 城区地面：楼盖完、院子算完之后再画（楼底的人行道按楼的实际占地画）
        void BuildGround(Zone z)
        {
            var g = ZoneRoot(z, "Ground");
            var tex = GroundTexture(z, out float aMax, out float bMax);
            var mat = new Material(Shader.Find("Monster/CityGround")) { name = "ground_" + z.id };
            mat.SetTexture("_BaseMap", tex);
            Vector3 P(float a, float b) => z.Point(a, b, 0.0008f);
            var m = new Mesh { name = "ground_" + z.id };
            m.vertices = new[] { P(-GM, -GM), P(aMax, -GM), P(aMax, bMax), P(-GM, bMax) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            bool up = Vector3.Dot(Vector3.Cross(m.vertices[1] - m.vertices[0], m.vertices[3] - m.vertices[0]), Vector3.up) > 0;
            m.triangles = up ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 3, 2, 0, 2, 1 };
            m.RecalculateNormals(); m.RecalculateBounds();
            var go = new GameObject("RoadsAndShadow"); go.transform.SetParent(g, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = true;
        }

        // ───────── 占地登记（防穿插）：树冠、棕榈叶、灌木、路灯、红绿灯、候车亭、旗杆、喷泉、凉亭都登记成水平圆；
        // 新放的植物和已有的圆不重叠、也不伸进楼的实际占地，否则这棵就不种 ─────────
        readonly List<(Vector3 p, float r)> occ = new();
        Zone ZoneAt(Vector3 w)
        {
            foreach (var z in city.zones)
            {
                var ab = AB(z, w);
                if (Mathf.Abs(ab.z) < 0.05f && ab.x > -GM && ab.x < z.w + GM && ab.y > -GM && ab.y < z.h + GM) return z;
            }
            return null;
        }
        bool BuildingFree(Vector3 w, float r)
        {
            var z = ZoneAt(w);
            if (z == null || !buildingRects.TryGetValue(z, out var rects)) return true;
            var ab = AB(z, w);
            var c = Rect.MinMaxRect(ab.x - r, ab.y - r, ab.x + r, ab.y + r);
            foreach (var rr in rects) if (rr.Overlaps(c)) return false;
            return true;
        }
        bool Claim(Vector3 w, float r, bool force = false)
        {
            if (!force)
            {
                if (!BuildingFree(w, r)) return false;
                foreach (var (p, ro) in occ)
                    if (Mathf.Abs(p.y - w.y) < 0.05f && new Vector2(p.x - w.x, p.z - w.z).magnitude < r + ro) return false;
            }
            occ.Add((w, r));
            return true;
        }

        // ───────── 人行道：路灯 + 行道树 / 棕榈 + 候车亭 ─────────
        void Sidewalks(Zone z, Transform t)
        {
            float C = CityConst.Cell;
            var rng = new Rng((uint)(z.w * 733 + z.h * 191) + 5);
            foreach (var blk in CityLayout.Blocks(z))
            {
                float A0 = blk.gx * C, A1 = (blk.gx + blk.wc) * C, B0 = blk.gy * C, B1 = (blk.gy + blk.hc) * C;
                if (z.harbor && B1 > z.WaterB - 0.001f) continue;
                // 四条边：(起点, 终点, 朝路的方向)
                var edges = new (Vector2 p0, Vector2 p1, Vector2 outw)[]
                {
                    (new Vector2(A0, B0 + Side), new Vector2(A1, B0 + Side), Vector2.down),
                    (new Vector2(A0, B1 - Side), new Vector2(A1, B1 - Side), Vector2.up),
                    (new Vector2(A0 + Side, B0), new Vector2(A0 + Side, B1), Vector2.left),
                    (new Vector2(A1 - Side, B0), new Vector2(A1 - Side, B1), Vector2.right),
                };
                foreach (var (p0, p1, outw) in edges)
                {
                    float len = (p1 - p0).magnitude; var along = (p1 - p0) / len;
                    bool shelter = rng.Chance(0.18f);
                    for (float s = 0.03f; s < len - 0.025f; s += 0.033f)
                    {
                        var ab = p0 + along * s;
                        float e = EdgeOf(z, ab.x, ab.y);
                        if (e < 0.08f || rng.Next() > Mathf.Clamp01((e - 0.08f) / 0.08f)) continue;
                        int slot = Mathf.RoundToInt(s / 0.033f);
                        if (shelter && slot == 2 && Free(z, ab.x, ab.y, along.x != 0 ? 0.016f : 0.007f, along.x != 0 ? 0.007f : 0.016f))
                        { BusShelter(t, z, ab, along, outw); continue; }
                        if (slot % 2 == 0) { if (Free(z, ab.x, ab.y, 0.003f, 0.003f)) StreetLamp(t, z, ab, outw); }
                        else if (Free(z, ab.x, ab.y, 0.008f, 0.008f))
                        {
                            if (z.kind == ZoneKind.Floor || rng.Chance(0.45f)) Palm(t, z.Point(ab.x, ab.y), rng.Range(0.07f, 0.095f), rng);
                            else Tree(t, z.Point(ab.x, ab.y), rng);
                        }
                    }
                }
            }
        }

        Quaternion Face(Zone z, Vector2 dirAB) => Quaternion.LookRotation(z.u * dirAB.x + z.v * dirAB.y, Vector3.up);

        void StreetLamp(Transform t, Zone z, Vector2 ab, Vector2 toRoad)
        {
            var p = new GameObject("Lamp").transform; p.SetParent(t, false);
            p.SetPositionAndRotation(z.Point(ab.x, ab.y), Face(z, toRoad));
            Claim(p.position, 0.004f, true); Claim(p.position + p.forward * 0.0135f, 0.004f, true);
            var steel = PM("#3d3f48", 0.0018f);
            Part(p, CityMeshes.Cylinder(6), steel, Vector3.zero, new Vector3(0.0022f, 0.0045f, 0.0022f), "Foot");
            Part(p, CityMeshes.Cylinder(6), steel, Vector3.zero, new Vector3(0.0013f, 0.056f, 0.0013f), "Pole");
            Part(p, CityMeshes.Box(), steel, new Vector3(0, 0.0545f, 0.007f), new Vector3(0.0012f, 0.0012f, 0.014f), "Arm");
            Part(p, CityMeshes.Box(), steel, new Vector3(0, 0.0532f, 0.0135f), new Vector3(0.0034f, 0.0018f, 0.0055f), "Head");
            Part(p, CityMeshes.Box(), GlowM("#f3ead2", "#ffd28a", 1.0f, 2.4f), new Vector3(0, 0.0526f, 0.0135f), new Vector3(0.0028f, 0.0007f, 0.0046f), "Bulb");
            if (night)
            {
                var pool = Part(p, CityMeshes.Box(), Pool(), new Vector3(0, 0.0004f, 0.0135f), new Vector3(0.06f, 0.0002f, 0.06f), "Pool");
                pool.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        void Tree(Transform t, Vector3 pos, Rng rng)
        {
            float h = rng.Range(0.024f, 0.034f), r = rng.Range(0.013f, 0.018f);
            if (!Claim(pos, r * 0.62f)) return;   // 树冠半径约 0.6r（球网格半径 0.5 × 缩放 r，再加上偏出去的副冠）
            var p = new GameObject("Tree").transform; p.SetParent(t, false); p.position = pos;
            Part(p, CityMeshes.Cylinder(6), PM("#6b4a34", 0.0018f), Vector3.zero, new Vector3(0.0018f, h * 0.55f, 0.0018f), "Trunk");
            var leaf = PM(new[] { "#3f8f4e", "#4f9d55", "#5aa65a", "#3b7f52" }[rng.Int(0, 3)]);
            Part(p, CityMeshes.Sphere(), leaf, new Vector3(0, h * 0.4f, 0), new Vector3(r, r * 0.85f, r), "Crown", Quaternion.Euler(0, rng.Range(0, 360), 0));
            Part(p, CityMeshes.Sphere(), leaf, new Vector3(r * 0.25f, h * 0.4f + r * 0.45f, 0), new Vector3(r * 0.7f, r * 0.6f, r * 0.7f), "Crown2");
            Part(p, CityMeshes.Box(), PM("#6a5a48", 0.0012f), new Vector3(0, 0, 0), new Vector3(0.007f, 0.0013f, 0.007f), "Pit");   // 高出地面 0.5mm，不和地面同高（会闪）
        }

        // 棕榈：微弯的两节树干 + 7 片下垂的叶子（每片一块，前端压低）+ 椰子
        void Palm(Transform t, Vector3 pos, float h, Rng rng)
        {
            var yawP = Quaternion.Euler(0, rng.Range(0, 360), 0);
            float lean = rng.Range(3f, 9f);
            Vector3 topW = Vector3.zero;
            for (int i = 0; i < 2; i++) topW += Quaternion.Euler(0, 0, lean * (i + 1) * 0.8f) * Vector3.up * (h / 2);
            var crownAt = pos + yawP * new Vector3(topW.x, 0, topW.z);
            if (!BuildingFree(pos, 0.004f) || !BuildingFree(crownAt, 0.023f)) return;
            foreach (var (pp, ro) in occ) if (Mathf.Abs(pp.y - pos.y) < 0.05f && (new Vector2(pp.x - crownAt.x, pp.z - crownAt.z).magnitude < 0.023f + ro || new Vector2(pp.x - pos.x, pp.z - pos.z).magnitude < 0.004f + ro)) return;
            Claim(crownAt, 0.023f, true); Claim(pos, 0.004f, true);
            var p = new GameObject("Palm").transform; p.SetParent(t, false); p.position = pos;
            p.rotation = yawP;
            var bark = PM("#8a6a48", 0.0016f);
            Vector3 top = Vector3.zero;
            for (int i = 0; i < 2; i++)
            {
                var q = Quaternion.Euler(0, 0, lean * (i + 1) * 0.8f);
                float seg = h / 2, w = 0.0034f - i * 0.0008f;
                Part(p, CityMeshes.Cylinder(6, 0.8f), bark, top, new Vector3(w, seg * 1.04f, w), "Trunk", q);
                top += q * Vector3.up * seg;
            }
            var leafA = PM("#3f9a5a", 0.0016f); var leafB = PM("#2f7f4a", 0.0016f);
            for (int i = 0; i < 7; i++)
            {
                float yaw = i * 360f / 7 + rng.Range(-12f, 12f);
                var q = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(8 + rng.Range(-8f, 10f), 0, 0);
                Part(p, CityMeshes.Box(), i % 2 == 0 ? leafA : leafB, top + q * new Vector3(0, -0.002f, 0.01f), new Vector3(0.0055f, 0.0009f, 0.024f), "Frond", q);
            }
            Part(p, CityMeshes.Box(), PM("#6b5232", 0.0012f), top + Vector3.down * 0.003f, Vector3.one * 0.003f, "Nuts");
        }

        void BusShelter(Transform t, Zone z, Vector2 ab, Vector2 along, Vector2 toRoad)
        {
            var p = new GameObject("BusShelter").transform; p.SetParent(t, false);
            p.SetPositionAndRotation(z.Point(ab.x, ab.y), Face(z, toRoad));
            Claim(p.position, 0.017f, true);
            var steel = PM("#3d3f48", 0.0018f);
            Part(p, CityMeshes.Box(), PM("#e9e6df", 0.0018f), new Vector3(0, 0.018f, 0.001f), new Vector3(0.03f, 0.0016f, 0.012f), "Roof");
            Part(p, CityMeshes.Box(), PM("#8fc3d6", 0.0012f), new Vector3(0, 0.002f, -0.0045f), new Vector3(0.028f, 0.016f, 0.0008f), "Back");
            foreach (var x in new[] { -0.0145f, 0.0145f }) Part(p, CityMeshes.Box(), steel, new Vector3(x, 0, -0.0045f), new Vector3(0.0012f, 0.018f, 0.0012f), "Post");
            Part(p, CityMeshes.Box(), GlowM("#f2d7e6", "#ff6fb8", 1.0f, 1.8f), new Vector3(0.0145f, 0.003f, 0.0005f), new Vector3(0.0008f, 0.012f, 0.008f), "Ad");
            Part(p, CityMeshes.Box(), PM("#b07a50", 0.0012f), new Vector3(-0.002f, 0.0035f, -0.002f), new Vector3(0.02f, 0.0012f, 0.003f), "Bench");
        }

        // ───────── 路口：四角红绿灯（悬臂伸到路面上方，灯色跟 Traffic 的相位走）+ 行人信号 ─────────
        void Intersections(Zone z, Transform t)
        {
            float C = CityConst.Cell, H = 0.05f + Side;
            for (int gx = 0; gx < z.Cols; gx += CityConst.RoadEvery)
                for (int gy = 0; gy < z.Rows; gy += CityConst.RoadEvery)
                {
                    float a = (gx + 0.5f) * C, b = (gy + 0.5f) * C;
                    if (!LaneOK(z, a, true) || !LaneOK(z, b, false)) continue;
                    // (角落符号, 悬臂方向)：两根悬臂沿 a 伸（看 v 向车流），两根沿 b 伸（看 u 向车流）
                    // 只在城里的十字路口放红绿灯，而且只放对角两根（11-09 用户：别到处都是红绿灯）；路口轮流换对角
                    var corners = ((gx + gy) / CityConst.RoadEvery) % 2 == 0
                        ? new (int sa, int sb, bool armAlongA)[] { (-1, -1, true), (1, 1, false) }    // 对角两根：一根管 v 向车流、一根管 u 向
                        : new (int sa, int sb, bool armAlongA)[] { (1, -1, false), (-1, 1, true) };
                    foreach (var (sa, sb, armA) in corners)
                    {
                        float pa = a + sa * H, pb = b + sb * H;
                        if (!Free(z, pa, pb, 0.002f, 0.002f)) continue;
                        var dir = armA ? new Vector2(-sa, 0) : new Vector2(0, -sb);
                        int grp = Traffic.GroupOf(z, a, b); SignalPole(t, z, new Vector2(pa, pb), dir, armA ? sigVs[grp] : sigUs[grp]);
                    }
                }
        }

        void SignalPole(Transform t, Zone z, Vector2 ab, Vector2 armDir, Material sig)
        {
            var p = new GameObject("Signal").transform; p.SetParent(t, false);
            p.SetPositionAndRotation(z.Point(ab.x, ab.y), Face(z, armDir));
            Claim(p.position, 0.004f, true);
            var steel = PM("#2f3138", 0.0016f);
            Part(p, CityMeshes.Cylinder(6), steel, Vector3.zero, new Vector3(0.0016f, 0.045f, 0.0016f), "Pole");
            Part(p, CityMeshes.Box(), steel, new Vector3(0, 0.0435f, 0.017f), new Vector3(0.0011f, 0.0011f, 0.034f), "Arm");
            foreach (var zz in new[] { 0.022f, 0.032f })
            {
                Part(p, CityMeshes.Box(), PM("#1d1e22", 0.0012f), new Vector3(0, 0.0352f, zz), new Vector3(0.0032f, 0.0082f, 0.0032f), "Head");
                Part(p, CityMeshes.Box(), sig, new Vector3(0, 0.0372f, zz), new Vector3(0.0036f, 0.0024f, 0.0024f), "Lamp");
            }
            Part(p, CityMeshes.Box(), PM("#e8e3d6", 0.0012f), new Vector3(0, 0.03f, 0.0012f), new Vector3(0.0055f, 0.0018f, 0.0006f), "StreetSign");
        }

        bool LaneOK(Zone z, float c, bool alongA)
        {
            // alongA = true：c 是一条“沿 b 方向的路”的 a 坐标；要求离城区边缘够远（马路还没淡出）
            float lim = alongA ? z.w : (z.harbor ? z.WaterB - 0.15f : z.h);
            return c > 0.1f && c < lim - 0.1f;
        }

        // ───────── 不盖楼的地块 ─────────
        void OpenLotProps(OpenLot o, Transform t)
        {
            var z = o.zone; var rng = new Rng((uint)o.seed);
            float a0 = o.a - o.w / 2, b0 = o.b - o.d / 2;
            Vector3 P(float fa, float fb, float h = 0) => z.Point(a0 + fa * o.w, b0 + fb * o.d, h);
            var g = new GameObject("Open_" + o.kind).transform; g.SetParent(t, false);
            switch (o.kind)
            {
                case "park":
                    for (int i = 0; i < 4 + o.w * 30; i++)
                    {
                        float fa = rng.Range(0.12f, 0.88f), fb = rng.Range(0.12f, 0.88f);
                        if (Mathf.Abs(fa - 0.5f) < 0.08f || Mathf.Abs(fb - 0.5f) < 0.08f) continue;   // 十字小路上不种
                        if (rng.Chance(0.3f)) Palm(g, P(fa, fb), rng.Range(0.06f, 0.085f), rng); else Tree(g, P(fa, fb), rng);
                    }
                    Fountain(g, P(0.5f, 0.5f), Mathf.Min(o.w, o.d) * 0.16f);
                    foreach (var (fa, fb) in new[] { (0.5f, 0.3f), (0.3f, 0.5f) })
                        Part(g, CityMeshes.Box(), PM("#b07a50", 0.0012f), P(fa, fb, 0.0025f), new Vector3(0.012f, 0.0012f, 0.003f), "Bench", Face(z, fb == 0.5f ? Vector2.right : Vector2.up));
                    break;
                case "green":
                    // 小绿地：偶尔一个小亭子（先放），再种成丛的树和灌木（避开亭子）
                    if (o.w > 0.1f && rng.Chance(0.5f) && Claim(P(0.5f, 0.5f), 0.012f))
                    {
                        var gz = new GameObject("Gazebo").transform; gz.SetParent(g, false);
                        Part(gz, CityMeshes.Cylinder(8), PM("#e9e4d8", 0.0014f), P(0.5f, 0.5f), new Vector3(0.014f, 0.0015f, 0.014f), "GazeboBase");
                        foreach (var (da, db) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                            Part(gz, CityMeshes.Box(), PM("#f4f1ea", 0.0010f), P(0.5f, 0.5f, 0.0015f) + (z.u * da + z.v * db) * 0.0045f, new Vector3(0.0012f, 0.011f, 0.0012f), "GazeboPost");
                        Part(gz, CityMeshes.Pyramid(), PM("#b8503a", 0.0014f), P(0.5f, 0.5f, 0.0125f), new Vector3(0.017f, 0.007f, 0.017f), "GazeboRoof", Quaternion.Euler(0, 45, 0));
                    }
                    for (int i = 0; i < 6 + o.w * o.d * 1500; i++)
                    {
                        float fa = rng.Range(0.1f, 0.9f), fb = rng.Range(0.1f, 0.9f);
                        if (rng.Chance(0.55f)) Tree(g, P(fa, fb), rng); else if (rng.Chance(0.3f)) Palm(g, P(fa, fb), rng.Range(0.05f, 0.075f), rng); else Bush(g, P(fa, fb), rng);
                    }
                    break;
                case "plaza":
                    Fountain(g, P(0.5f, 0.5f), Mathf.Min(o.w, o.d) * 0.22f);
                    foreach (var (fa, fb) in new[] { (0.18f, 0.18f), (0.82f, 0.18f), (0.18f, 0.82f), (0.82f, 0.82f) })
                    {
                        Part(g, CityMeshes.Box(), PM("#cfc6b8", 0.0014f), P(fa, fb), new Vector3(0.012f, 0.004f, 0.012f), "Planter");
                        Tree(g, P(fa, fb, 0.004f), rng);
                    }
                    Claim(P(0.5f, 0.12f), 0.005f, true);
                    Part(g, CityMeshes.Box(), PM("#b9b2a6", 0.0016f), P(0.5f, 0.12f), new Vector3(0.006f, 0.022f, 0.006f), "Statue");
                    break;
                case "court":
                    foreach (var fb in new[] { 0.12f, 0.88f })
                    {
                        var hoop = P(0.5f, fb);
                        Part(g, CityMeshes.Box(), PM("#3d3f48", 0.0012f), hoop, new Vector3(0.0012f, 0.024f, 0.0012f), "HoopPole");
                        Part(g, CityMeshes.Box(), PM("#f4f4f2", 0.0012f), hoop + Vector3.up * 0.02f + z.v * (fb < 0.5f ? 0.002f : -0.002f), new Vector3(0.012f, 0.008f, 0.0008f), "Board", Face(z, Vector2.up));
                    }
                    Fence(g, z, a0, b0, o.w, o.d, PM("#4a6a58", 0.0010f), 0.012f);
                    break;
                case "parking":
                    ParkingLot(g, o, rng);
                    break;
                case "construction":
                    Construction(g, o, rng);
                    break;
            }
        }

        void Fountain(Transform g0, Vector3 pos, float r)
        {
            Claim(pos, r, true);
            var g = new GameObject("Fountain").transform; g.SetParent(g0, false);
            Part(g, CityMeshes.Cylinder(16), PM("#d9d2c4", 0.0016f), pos, new Vector3(r * 2, 0.004f, r * 2), "Basin");
            Part(g, CityMeshes.Cylinder(16), GlowM("#6fd6e8", "#3ec8f0", 0.9f, 1.3f), pos + Vector3.up * 0.0049f,   // 比池壁顶高 0.7mm（11-08 防闪）
                 new Vector3(r * 1.7f, 0.0004f, r * 1.7f), "Water");
            Part(g, CityMeshes.Cylinder(8), PM("#d9d2c4", 0.0014f), pos, new Vector3(r * 0.4f, 0.012f, r * 0.4f), "Column");
            Part(g, CityMeshes.Cylinder(8, 0.2f), GlowM("#cfeff6", "#9fe8ff", 1.0f, 1.6f), pos + Vector3.up * 0.012f, new Vector3(r * 0.3f, 0.01f, r * 0.3f), "Jet");
        }

        void Fence(Transform g, Zone z, float a0, float b0, float w, float d, Material m, float h)
        {
            float th = 0.0008f;
            Part(g, CityMeshes.Box(), m, z.Point(a0 + w / 2, b0), new Vector3(w, h, th), "Fence", Face(z, Vector2.up));
            Part(g, CityMeshes.Box(), m, z.Point(a0 + w / 2, b0 + d), new Vector3(w, h, th), "Fence", Face(z, Vector2.up));
            Part(g, CityMeshes.Box(), m, z.Point(a0, b0 + d / 2), new Vector3(d - th * 2, h, th), "Fence", Face(z, Vector2.right));
            Part(g, CityMeshes.Box(), m, z.Point(a0 + w, b0 + d / 2), new Vector3(d - th * 2, h, th), "Fence", Face(z, Vector2.right));
        }

        // 停车场布局（地面画线和摆车共用）：沿 a 方向一排排车位；地块够深就“背靠背双排 + 通道”，不够就单排。
        // 返回每个车位：(中心 a, 中心 b, 车头朝向 +b 还是 -b)
        static List<(float a, float b, bool up)> ParkingStalls(OpenLot o)
        {
            var list = new List<(float, float, bool)>();
            float sw = 2.7f * K * CarS, sd = 5.2f * K * CarS, aisle = 4.5f * K * CarS, m = 0.005f;
            float a0 = o.a - o.w / 2 + m, b0 = o.b - o.d / 2 + m, wUse = o.w - m * 2, dUse = o.d - m * 2;
            int cols = Mathf.FloorToInt(wUse / sw);
            float ax = a0 + (wUse - cols * sw) * 0.5f;
            float b = b0;
            while (true)
            {
                bool dbl = b + sd * 2 <= b0 + dUse;
                if (!dbl && b + sd > b0 + dUse) break;
                for (int i = 0; i < cols; i++)
                {
                    list.Add((ax + sw * (i + 0.5f), b + sd * 0.5f, false));
                    if (dbl) list.Add((ax + sw * (i + 0.5f), b + sd * 1.5f, true));
                }
                b += (dbl ? sd * 2 : sd) + aisle;
                if (b >= b0 + dUse) break;
            }
            return list;
        }

        void ParkingLot(Transform g, OpenLot o, Rng rng)
        {
            var z = o.zone;
            foreach (var (a, b, up) in ParkingStalls(o))
            {
                if (!rng.Chance(0.5f)) continue;
                var go = new GameObject("ParkedCar"); go.transform.SetParent(g, false);
                go.transform.SetPositionAndRotation(z.Point(a, b), Face(z, up ? Vector2.up : Vector2.down));
                var ck = PickCar(rng, false); go.AddComponent<MeshFilter>().sharedMesh = CarMesh(ck, PickColor(ck, rng), true);
                go.AddComponent<MeshRenderer>().sharedMaterial = CarMat();
            }
            float a0 = o.a - o.w / 2, b0 = o.b - o.d / 2;
            foreach (var (fa, fb) in new[] { (0.004f, 0.004f), (o.w - 0.004f, o.d - 0.004f) })
            {
                var lp = z.Point(a0 + fa, b0 + fb);
                Part(g, CityMeshes.Cylinder(6), PM("#3d3f48", 0.0016f), lp, new Vector3(0.0016f, 0.07f, 0.0016f), "LotPole");
                Part(g, CityMeshes.Box(), GlowM("#f3ead2", "#fff0c8", 1.0f, 2.2f), lp + Vector3.up * 0.07f, new Vector3(0.008f, 0.0014f, 0.004f), "LotLight");
            }
        }

        // 工地：围挡 + 半盖的混凝土框架（柱 + 几层楼板，最上一层只有柱子）+ 塔吊 + 工地板房 + 砂堆
        void Construction(Transform g, OpenLot o, Rng rng)
        {
            var z = o.zone;
            float a0 = o.a - o.w / 2, b0 = o.b - o.d / 2;
            Fence(g, z, a0, b0, o.w, o.d, PM("#2f6fb0", 0.0012f), 0.014f);
            var con = PM("#b9b4aa", 0.0018f); var rebar = PM("#7a5a48", 0.0012f);
            float fw = o.w * 0.55f, fd = o.d * 0.5f, fa = a0 + o.w * 0.4f, fb = b0 + o.d * 0.45f, sh = 3.4f * K;
            int floors = rng.Int(3, 5);
            for (int f = 0; f < floors; f++)
            {
                float y = f * sh;
                for (int i = 0; i <= 3; i++) for (int j = 0; j <= 2; j++)
                    Part(g, CityMeshes.Box(), f == floors - 1 ? rebar : con, z.Point(fa - fw / 2 + fw * i / 3f, fb - fd / 2 + fd * j / 2f, y), new Vector3(0.0035f, f == floors - 1 ? sh : sh - 0.0015f, 0.0035f), "Column");
                if (f < floors - 1) Part(g, CityMeshes.Box(), con, z.Point(fa, fb, y + sh - 0.0015f), new Vector3(fw + 0.004f, 0.0015f, fd + 0.004f), "Slab", Face(z, Vector2.up));
            }
            // 塔吊：塔身（黄色，带横撑的方截面用实心细柱代替）+ 回转平台 + 起重臂 + 平衡臂 + 配重 + 驾驶室 + 吊钩
            var yel = PM("#f2b632", 0.0016f);
            var cp = z.Point(a0 + o.w * 0.82f, b0 + o.d * 0.22f);
            float mh = 0.2f + rng.Range(0f, 0.04f);
            var crane = new GameObject("TowerCrane").transform; crane.SetParent(g, false);
            crane.SetPositionAndRotation(cp, Face(z, new Vector2(-1, 0.6f).normalized));
            Part(crane, CityMeshes.Box(), PM("#8f8a80", 0.0014f), Vector3.zero, new Vector3(0.012f, 0.003f, 0.012f), "Base");
            Part(crane, CityMeshes.Box(), yel, Vector3.zero, new Vector3(0.006f, mh, 0.006f), "Mast");
            for (float y = 0.012f; y < mh - 0.01f; y += 0.012f)
                Part(crane, CityMeshes.Box(), yel, new Vector3(0, y, 0), new Vector3(0.0072f, 0.0012f, 0.0072f), "MastRing");
            Part(crane, CityMeshes.Box(), yel, new Vector3(0, mh, 0.045f), new Vector3(0.004f, 0.004f, 0.11f), "Jib");
            Part(crane, CityMeshes.Box(), yel, new Vector3(0, mh + 0.0003f, -0.017f), new Vector3(0.0034f, 0.0034f, 0.034f), "CounterJib");
            Part(crane, CityMeshes.Box(), PM("#6b6b6b", 0.0014f), new Vector3(0, mh - 0.005f, -0.03f), new Vector3(0.008f, 0.006f, 0.008f), "Weight");
            Part(crane, CityMeshes.Box(), PM("#e8e3d6", 0.0014f), new Vector3(0.004f, mh - 0.006f, 0.004f), new Vector3(0.004f, 0.005f, 0.005f), "Cab");
            Part(crane, CityMeshes.Box(), yel, new Vector3(0, mh + 0.004f, 0), new Vector3(0.003f, 0.012f, 0.003f), "Apex");
            Part(crane, CityMeshes.Box(), PM("#2a2a2e", 0.0008f), new Vector3(0, mh - 0.05f, 0.07f), new Vector3(0.0006f, 0.05f, 0.0006f), "Cable");
            Part(crane, CityMeshes.Box(), PM("#c8463c", 0.0012f), new Vector3(0, mh - 0.055f, 0.07f), new Vector3(0.0035f, 0.003f, 0.0035f), "Hook");
            Part(crane, CityMeshes.Box(), GlowM("#ff8080", "#ff3030", 1.2f, 2.2f), new Vector3(0, mh + 0.016f, 0), Vector3.one * 0.0025f, "Beacon");
            Part(g, CityMeshes.Box(), PM("#e9e6df", 0.0014f), z.Point(a0 + o.w * 0.2f, b0 + o.d * 0.85f), new Vector3(0.022f, 0.008f, 0.009f), "SiteOffice", Face(z, Vector2.up));
            Part(g, CityMeshes.Pyramid(), PM("#c9a46a", 0.0014f), z.Point(a0 + o.w * 0.78f, b0 + o.d * 0.78f), new Vector3(0.016f, 0.007f, 0.014f), "Sand");
        }

        // ───────── 港口：河面（Monster/CityWater）+ 码头岸壁 + 岸桥 + 集装箱堆场 + 来往的船 ─────────
        // 模型：Tools/blender_harbor.py（Resources/Modules/harbor，真实尺寸米、车色板 UV；格 0 = 船身色，Unity 换）
        Mesh HarborMesh(string n, int color = -1)
        {
            var key = ("harbor_" + n, color);
            if (carMeshes.TryGetValue(key, out var cm) && cm) return cm;
            var go = Resources.Load<GameObject>($"Modules/harbor/{n}");
            var src = go ? go.GetComponentInChildren<MeshFilter>().sharedMesh : null;
            if (!src) return null;
            cm = Object.Instantiate(src); cm.name = $"{n}_{color}";
            var rot = Quaternion.Euler(0, 180, 0);
            var v = cm.vertices; var nn = cm.normals; var uv = cm.uv;
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = rot * v[i] * K; nn[i] = rot * nn[i];
                if (color >= 0 && uv[i].x < 1f / 16f) uv[i].x = (color + 0.5f) / 16f;
            }
            cm.vertices = v; cm.normals = nn; cm.uv = uv;
            CityMeshes.BakeSmoothNormals(cm); cm.RecalculateBounds();
            carMeshes[key] = cm;
            return cm;
        }

        Transform Placed(Transform parent, Mesh mesh, Vector3 pos, Quaternion rot, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = CarMat();
            return go.transform;
        }

        const float Waterline = 0.0028f;   // 水面高（城市比例 ≈ 真实 0.37 米）：船底那一圈深色船壳在水下，船像真的吃水

        void BuildHarbor(Zone z)
        {
            var t = ZoneRoot(z, "Harbor");
            float wb = z.WaterB, wid = z.h - wb + 0.12f;          // 河面伸出城区 12cm（再渐隐）
            float a0 = -0.25f, a1 = z.w + 0.25f;                   // 两头各伸出 25cm
            // 河面
            var mat = new Material(Shader.Find("Monster/CityWater")) { name = "river" };
            mat.SetFloat("_Night", night ? 1 : 0); mat.SetFloat("_Len", a1 - a0); mat.SetFloat("_Wid", wid);
            var m = new Mesh { name = "river" };
            int seg = 24;
            var vs = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float f = (float)i / seg, a = Mathf.Lerp(a0, a1, f);
                vs.Add(z.Point(a, wb, Waterline)); vs.Add(z.Point(a, wb + wid, Waterline));
                uvs.Add(new Vector2(f, 0)); uvs.Add(new Vector2(f, 1));
                if (i < seg) { int k = i * 2; tris.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
            }
            m.SetVertices(vs); m.SetUVs(0, uvs); m.SetTriangles(tris, 0); m.RecalculateNormals();
            if (Vector3.Dot(m.normals[0], Vector3.up) < 0) { for (int i = 0; i < tris.Count; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]); m.SetTriangles(tris, 0); m.RecalculateNormals(); }
            m.RecalculateBounds();
            var rv = new GameObject("River"); rv.transform.SetParent(t, false);
            rv.AddComponent<MeshFilter>().sharedMesh = m;
            var rr = rv.AddComponent<MeshRenderer>(); rr.sharedMaterial = mat; rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // 码头岸壁：沿岸一道石砌岸沿（顶面浅色、立面深色）+ 系缆桩 + 轮胎护舷
            var quay = PM("#b9b0a2", 0.0016f); var quayDark = PM("#6b625a", 0.0012f);
            Part(t, CityMeshes.Box(), quay, z.Point(z.w / 2, wb - 0.004f, 0), new Vector3(z.w + 0.06f, 0.006f, 0.008f), "QuayEdge", Face(z, Vector2.up));
            Part(t, CityMeshes.Box(), quayDark, z.Point(z.w / 2, wb + 0.0008f, -0.002f), new Vector3(z.w + 0.06f, 0.0075f, 0.0016f), "QuayWall", Face(z, Vector2.up));
            // 岸桥（三台）：门架腿（沿岸 ±5cm、垂直岸线 2.4cm）要落在码头作业带上、不压楼；不行就沿岸挪一挪
            var crane = HarborMesh("sts_crane", 2);
            var craneA = new List<float>();
            const float craneB = 0.038f;   // 岸桥中心离岸线（腿离岸边石沿留出 1cm）
            // 窄河道（地板上的河只有一行沿河路）：岸桥后伸臂会伸出城区压到旁边的桌子，不放
            for (int k = 0; k < 3 && wb >= 0.16f; k++)
            {
                float a0c = 0.3f + k * (z.w - 0.6f) / 2f;
                foreach (var d in new[] { 0f, 0.05f, -0.05f, 0.1f, -0.1f })
                {
                    float ca = a0c + d;
                    var footprint = Rect.MinMaxRect(ca - 0.056f, wb - craneB - 0.026f, ca + 0.056f, wb - craneB + 0.026f);
                    if (buildingRects[z].Any(r => r.Overlaps(footprint)) || craneA.Any(x => Mathf.Abs(x - ca) < 0.2f)) continue;
                    craneA.Add(ca);
                    Placed(t, crane, z.Point(ca, wb - craneB), Face(z, Vector2.down), "STSCrane").localScale = Vector3.one * 0.75f;
                    break;
                }
            }
            for (float a = 0.04f; a < z.w - 0.02f; a += 0.06f)
            {
                Part(t, CityMeshes.Cylinder(8), PM("#1d1d22", 0.0008f), z.Point(a + 0.03f, wb + 0.0012f, 0.0012f), new Vector3(0.004f, 0.0015f, 0.004f), "Fender", Quaternion.LookRotation(Vector3.up, z.v));
                if (craneA.Any(ca => Mathf.Abs(Mathf.Abs(ca - a) - 0.05f) < 0.01f)) continue;   // 系缆桩不放在岸桥腿底下
                Part(t, CityMeshes.Cylinder(6), PM("#2a2a30", 0.0010f), z.Point(a, wb - 0.0055f, 0.006f), new Vector3(0.0028f, 0.003f, 0.0028f), "Bollard");
            }
            var box = new Dictionary<int, Mesh>();
            var rng = new Rng((uint)(z.w * 1000) + 77);
            int[] cc = { 2, 7, 3, 4, 5, 8, 6, 0, 1 };
            for (float a = 0.1f; a < z.w - 0.1f && wb >= 0.16f; a += 0.1f)   // 窄河道（只有一行沿河路）没有堆场，不放集装箱
            {
                if (craneA.Any(ca => Mathf.Abs(ca - a) < 0.11f)) continue;   // 岸桥门架底下留空（腿离中心 5cm + 长箱半长 4.5cm + 1.5cm 余量）
                if (!Free(z, a, wb - 0.03f, 0.045f, 0.02f)) continue;
                int stack = rng.Int(1, 3);
                for (int r = 0; r < 2; r++)
                    for (int h = 0; h < stack; h++)
                    {
                        if (rng.Chance(0.15f)) break;   // 这一层不放，上面也不再堆（不能悬空）
                        int col = cc[rng.Int(0, cc.Length - 1)];
                        if (!box.TryGetValue(col, out var bm)) box[col] = bm = HarborMesh("container", col);
                        Placed(t, bm, z.Point(a, wb - 0.018f - r * 0.021f, h * 2.59f * K), Face(z, Vector2.right), "Container");   // 箱高 2.59 米，上下层刚好贴合
                    }
            }
            // 船：沿河来回走（游艇、拖轮、快艇、驳船），复用 Traffic 的车道逻辑，慢速
            var boats = new GameObject("Boats").transform; boats.SetParent(t, false);
            var tr = boats.gameObject.AddComponent<Traffic>();
            tr.vmax = 0.018f;
            foreach (var (b, dir) in new[] { (wb + wid * 0.32f, 1), (wb + wid * 0.6f, -1) })
            {
                var L = new Traffic.Lane { alongU = true, p0 = z.Point(dir > 0 ? a0 : a1, b, 0.0006f), dir = z.u * dir, s0 = 0.05f, s1 = (a1 - a0) - 0.05f };
                float s = L.s0 + rng.Range(0.05f, 0.4f);
                while (s < L.s1 - 0.1f)
                {
                    string kind = new[] { "yacht", "tug", "speedboat", "container_ship", "speedboat", "yacht", "container_ship" }[rng.Int(0, 6)];
                    int col = kind == "yacht" ? 0 : kind == "tug" ? 2 : kind == "container_ship" ? (rng.Chance(0.5f) ? 7 : 2) : new[] { 0, 2, 3, 7, 5 }[rng.Int(0, 4)];
                    var bm = HarborMesh(kind, col);
                    var go = Placed(boats, bm, Vector3.zero, Quaternion.identity, "Boat");
                    go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    L.cars.Add(new Traffic.Car { t = go, s = s, len = bm.bounds.size.z, v = tr.vmax });
                    s += bm.bounds.size.z + rng.Range(0.25f, 0.6f);
                }
                tr.lanes.Add(L);
            }
            tr.Step(0);
        }

        // ───────── 车 ─────────
        // 所有车共用一个材质：颜色从 16×1 的色板贴图取（每个部件的 UV 指向色板上一个像素），车灯在色板的自发光图里，夜里亮。
        // 一辆车 = 一个网格 + 一个材质 = 一次绘制，几百辆车也扛得住。
        static readonly string[] CarColors = { "#f4f4f2", "#1e1f24", "#d2333a", "#2fb5a8", "#f2c53a", "#ff6fb0", "#a9b3bd", "#2f6fd6", "#7a3fbf" };
        const int PGlass = 9, PTyre = 10, PHead = 11, PTail = 12, PTaxi = 13, PTrim = 14, PChrome = 15;
                Material carMat;

        Material CarMat()
        {
            if (carMat) return carMat;
            var pal = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "car_palette" };
            var emi = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "car_palette_emis" };
            for (int i = 0; i < 16; i++) { pal.SetPixel(i, 0, Color.gray); emi.SetPixel(i, 0, Color.black); }
            for (int i = 0; i < CarColors.Length; i++) pal.SetPixel(i, 0, CityMaterials.Hex(CarColors[i]));
            pal.SetPixel(PGlass, 0, CityMaterials.Hex("#2b3a4c")); pal.SetPixel(PTyre, 0, CityMaterials.Hex("#18181c"));
            pal.SetPixel(PHead, 0, CityMaterials.Hex("#e8e4d0")); pal.SetPixel(PTail, 0, CityMaterials.Hex("#8a2a2a"));
            pal.SetPixel(PTaxi, 0, CityMaterials.Hex("#fff6c0")); pal.SetPixel(PTrim, 0, CityMaterials.Hex("#2a2a30")); pal.SetPixel(PChrome, 0, CityMaterials.Hex("#c9ccd2"));
            emi.SetPixel(PHead, 0, CityMaterials.Hex("#fff4c8")); emi.SetPixel(PTail, 0, CityMaterials.Hex("#ff2a2a")); emi.SetPixel(PTaxi, 0, CityMaterials.Hex("#fff2a0"));
            pal.Apply(); emi.Apply();
            carMat = new Material(CityMaterials.Toon) { name = "cars", enableInstancing = true };
            carMat.SetTexture("_BaseMap", pal); carMat.SetTexture("_EmissionMap", emi);
            carMat.SetColor("_EmissionColor", new Color(2.4f, 2.4f, 2.4f));
            carMat.SetFloat("_NightGlow", night ? 1 : 0);
            carMat.SetFloat("_OutlineWidth", 0.0012f);
            return carMat;
        }

        // 车型网格：Tools/blender_vehicles.py 建的 FBX（真实尺寸米，Resources/Modules/vehicles/），读进来缩放到城市比例、转 180°（FBX 车头朝 -Z → +Z），
        // 再把车身那一格的 UV（色板格 0）换成这辆车的颜色格。每种 (车型, 颜色) 缓存一份。
        public static readonly string[] CarKinds = { "sedan", "taxi", "sports", "suv", "pickup", "van", "bus" };
        readonly Dictionary<(string, int), Mesh> carMeshes = new();   // 车 / 船 / 岸桥 / 集装箱都缓存在这

        static string PickCar(Rng rng, bool bus)
        {
            float r = rng.Next();
            if (bus && r < 0.06f) return "bus";
            if (r < 0.36f) return "sedan";
            if (r < 0.48f) return "taxi";
            if (r < 0.58f) return "sports";
            if (r < 0.78f) return "suv";
            if (r < 0.88f) return "pickup";
            return "van";
        }

        static int PickColor(string kind, Rng rng) => kind == "taxi" ? 4 : kind == "bus" ? (rng.Chance(0.5f) ? 4 : 3) : rng.Int(0, CarColors.Length - 1);

        Mesh CarMesh(string kind, int color, bool parked = false)
        {
            var key = (parked ? kind + "_lod1" : kind, color);
            if (carMeshes.TryGetValue(key, out var cm) && cm) return cm;
            var go = Resources.Load<GameObject>($"Modules/vehicles/{key.Item1}") ?? Resources.Load<GameObject>($"Modules/vehicles/{kind}");
            var src = go ? go.GetComponentInChildren<MeshFilter>().sharedMesh : null;
            if (!src) return null;
            cm = Object.Instantiate(src); cm.name = $"car_{kind}_{color}";
            var rot = Quaternion.Euler(0, 180, 0);
            float ks = K * (System.Array.IndexOf(CarKinds, kind) < 0 ? 1 : kind == "bus" ? BusS : CarS);   // 船 / 岸桥 / 集装箱也走这里，不放大
            var v = cm.vertices; var n = cm.normals; var uv = cm.uv;
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = rot * v[i] * ks; n[i] = rot * n[i];
                if (uv[i].x < 1f / 16f) uv[i].x = (color + 0.5f) / 16f;
            }
            cm.vertices = v; cm.normals = n; cm.uv = uv;
            CityMeshes.BakeSmoothNormals(cm);
            cm.RecalculateBounds();
            carMeshes[key] = cm;
            return cm;
        }

        float CarLen(string kind) { var m = CarMesh(kind, 0); return m ? m.bounds.size.z : 4.5f * K * CarS; }

        // ───────── 树（11-11 用户：要能捏起树）：从静态小件合并里分出来，按 0.4m 区块单独合并；
        // 每棵树登记它的部件（网格 / 材质 / 相对区块的矩阵），捏起一棵时只重建它所在的那一块 ─────────
        class TreeRec { public Transform cell; public Vector3 pos; public float r; public bool taken; public readonly List<(Mesh mesh, Material mat, Matrix4x4 m)> parts = new(); }
        readonly List<TreeRec> treeRecs = new();
        const float TreeCell = 0.4f;
        List<Transform> SplitTrees(Transform props, Zone z)
        {
            var cells = new Dictionary<(int, int), Transform>();
            var treeRoot = ZoneRoot(z, "Trees");
            foreach (var t in props.GetComponentsInChildren<Transform>(true).Where(x => x.name == "Tree" || x.name == "Palm").ToList())
            {
                var key = (Mathf.FloorToInt(t.position.x / TreeCell), Mathf.FloorToInt(t.position.z / TreeCell));
                if (!cells.TryGetValue(key, out var cell)) { cell = new GameObject($"TreeCell_{key.Item1}_{key.Item2}").transform; cell.SetParent(treeRoot, false); cells[key] = cell; }
                var rec = new TreeRec { cell = cell, pos = cell.InverseTransformPoint(t.position) };   // 区块本地坐标（城市整体缩放后照样对）
                Bounds bb = default; bool first = true;
                foreach (var f in t.GetComponentsInChildren<MeshFilter>())
                {
                    var r = f.GetComponent<MeshRenderer>(); if (!r || !f.sharedMesh) continue;
                    rec.parts.Add((f.sharedMesh, r.sharedMaterial, cell.worldToLocalMatrix * f.transform.localToWorldMatrix));
                    if (first) { bb = r.bounds; first = false; } else bb.Encapsulate(r.bounds);
                }
                rec.r = (first ? 0.01f : Mathf.Max(bb.extents.x, bb.extents.z)) / Mathf.Max(1e-4f, cell.lossyScale.x);
                treeRecs.Add(rec);
                t.SetParent(cell, true);
            }
            return cells.Values.ToList();
        }
        static void CombineTreeCells(List<Transform> cells) { foreach (var c in cells) CombineStatic(c); }

        /// <summary>在 p 附近 r 内摘一棵树：返回一棵独立的树（普通物体，可以拿在手里），并重建它原来所在的区块</summary>
        public Transform TakeTree(Vector3 p, float r)
        {
            TreeRec best = null; float bd = float.MaxValue;
            foreach (var t in treeRecs)
            {
                if (t.taken || !t.cell) continue;
                float d = Vector3.Distance(t.cell.TransformPoint(t.pos), p) - t.r * t.cell.lossyScale.x;
                if (d < r && d < bd) { bd = d; best = t; }
            }
            if (best == null) return null;
            best.taken = true;
            var go = new GameObject("HeldTree").transform;
            foreach (var (mesh, mat, m) in best.parts)
            {
                var wm = best.cell.localToWorldMatrix * m;
                var c = new GameObject("part"); c.transform.SetParent(go, false);
                c.transform.SetPositionAndRotation(wm.GetColumn(3), wm.rotation); c.transform.localScale = wm.lossyScale;
                c.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = c.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // 重建这一块：清掉合并网格，把剩下的树按登记的部件重新摆出来再合并
            foreach (Transform c in best.cell.Cast<Transform>().ToList()) Object.Destroy(c.gameObject);
            foreach (var t in treeRecs)
            {
                if (t.taken || t.cell != best.cell) continue;
                foreach (var (mesh, mat, m) in t.parts)
                {
                    var c = new GameObject("part"); c.transform.SetParent(best.cell, false);
                    c.transform.localPosition = m.GetColumn(3); c.transform.localRotation = m.rotation; c.transform.localScale = m.lossyScale;
                    c.AddComponent<MeshFilter>().sharedMesh = mesh; c.AddComponent<MeshRenderer>().sharedMaterial = mat;
                }
            }
            CombineStatic(best.cell);
            return go;
        }

        // ───────── 静态小件合并：按材质合成一个网格（路灯、树、红绿灯杆、停车场的车……）─────────
        static void CombineStatic(Transform root)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>();
            if (filters.Length == 0) return;
            var subs = new List<(Material mat, CombineInstance ci)>();
            foreach (var f in filters)
            {
                var r = f.GetComponent<MeshRenderer>();
                for (int s = 0; s < f.sharedMesh.subMeshCount; s++)
                    subs.Add((r.sharedMaterials[Mathf.Min(s, r.sharedMaterials.Length - 1)], new CombineInstance { mesh = f.sharedMesh, subMeshIndex = s, transform = root.worldToLocalMatrix * f.transform.localToWorldMatrix }));
            }
            var kill = filters.Select(f => f.gameObject).ToList();
            foreach (var g in subs.GroupBy(x => x.mat))
            {
                var mesh = new Mesh { name = "props_" + g.Key.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(g.Select(x => x.ci).ToArray(), true, true);
                CityMeshes.BakeSmoothNormals(mesh);
                mesh.RecalculateBounds();
                var go = new GameObject("Merged_" + g.Key.name); go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = g.Key;
                // 只让高的东西（树、路灯、吊塔）投影；矮小件（长椅、树坑、花池）不投影、也不画描边（省一遍顶点）
                bool tall = mesh.bounds.size.y > 0.02f;
                if (g.Key.shader.name != "Monster/CityToon" || !tall) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (g.Key.shader.name == "Monster/CityToon" && mesh.bounds.size.y < 0.008f) { var m2 = new Material(g.Key) { name = g.Key.name + "_noOutline" }; m2.SetShaderPassEnabled("SRPDefaultUnlit", false); mr.sharedMaterial = m2; }
            }
            foreach (var go in kill) if (go) { if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go); }
            // 清掉空的父物体
            foreach (Transform c in root.Cast<Transform>().ToList()) if (!c.name.StartsWith("Merged_") && c.GetComponentsInChildren<MeshFilter>().Length == 0) { if (Application.isPlaying) Object.Destroy(c.gameObject); else Object.DestroyImmediate(c.gameObject); }
        }

        // ───────── 车流 ─────────
        void BuildTraffic(Zone z)
        {
            float C = CityConst.Cell, LO = 0.025f;
            var root = ZoneRoot(z, "Traffic");
            var tr = root.gameObject.AddComponent<Traffic>();
            tr.sigUs = sigUs; tr.sigVs = sigVs;
            var rng = new Rng((uint)(z.w * 311 + z.h * 977) + 9);
            var crossA = new List<float>(); var crossB = new List<float>();
            for (int gx = 0; gx < z.Cols; gx += CityConst.RoadEvery) { float a = (gx + 0.5f) * C; if (LaneOK(z, a, true)) crossA.Add(a); }
            for (int gy = 0; gy < z.Rows; gy += CityConst.RoadEvery) { float b = (gy + 0.5f) * C; if (LaneOK(z, b, false)) crossB.Add(b); }
            float bMax = z.harbor ? z.WaterB - 0.09f : z.h;
            // 沿 a 走的路（每条 b = 常数）：+a 方向走 b - LO，-a 方向走 b + LO
            foreach (var b in crossB)
                foreach (var dir in new[] { 1, -1 })
                    tr.lanes.Add(MakeLane(z, true, b + (dir > 0 ? -LO : LO), dir, z.w, crossA));
            foreach (var a in crossA)
                foreach (var dir in new[] { 1, -1 })
                    tr.lanes.Add(MakeLane(z, false, a + (dir > 0 ? LO : -LO), dir, bMax, crossB));
            foreach (var L in tr.lanes)
            {
                // 每条车道疏密不一样：有的车多、有的冷清、少数几乎空着
                float density = rng.Chance(0.15f) ? 0.15f : rng.Range(0.35f, 1.1f);
                L.density = density;
                float s = L.s0 + rng.Range(0.02f, 0.3f);
                while (s < L.s1 - 0.03f)
                {
                    var kind = PickCar(rng, true);
                    float len = CarLen(kind);
                    // 开场不把车放在路口里（横竖车流会叠在一起）：车身和任何路口有重叠就往后挪
                    bool inX = L.stops.Any(st => s + len * 0.5f > st && s - len * 0.5f < st + 0.11f);
                    if (inX) { s += 0.02f; continue; }
                    var go = new GameObject("Car"); go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = CarMesh(kind, PickColor(kind, rng));
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = CarMat();
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var car = new Traffic.Car { t = go.transform, s = s, len = len };
                    Traffic.Personalize(car, rng, kind == "bus" || kind == "van");
                    car.v = car.cruise * Traffic.Vmax;
                    L.cars.Add(car);
                    s += len + rng.Range(0.06f, 0.5f) / density;
                }
            }
            tr.Step(0);
        }

        Traffic.Lane MakeLane(Zone z, bool alongA, float c, int dir, float len, List<float> crosses)
        {
            var L = new Traffic.Lane { alongU = alongA };
            var axis = alongA ? z.u : z.v;
            var start = alongA ? z.Point(dir > 0 ? 0 : len, c) : z.Point(c, dir > 0 ? 0 : len);
            L.p0 = start + Vector3.up * 0.0003f; L.dir = axis * dir;
            L.s0 = 0.06f; L.s1 = len - 0.06f;
            foreach (var x in crosses)
            {
                float sc = dir > 0 ? x : len - x;
                L.stopList.Add((sc - 0.05f - 0.005f, Traffic.GroupOf(z, alongA ? x : c, alongA ? c : x)));   // 停止线 + 这个路口的相位组
            }
            L.stopList.Sort((p, q) => p.s.CompareTo(q.s));
            foreach (var st in L.stopList) L.stops.Add(st.s);
            return L;
        }
    }
}
