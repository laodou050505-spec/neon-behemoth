// 城市布局（从 Three.js 原型 src/logic/city.js 移植）：城区 → 街区 → 地块 → 楼型 → 层数。
// 城区来自房间扫描：桌面 = 市中心 + 海滨大道，柜顶 = 山顶别墅区，地板空地 = 一整片街区（港口只在靠玩家一侧）。
// 坐标：Unity 世界坐标，米。每个城区是一块水平矩形：origin 角点，u / v 两条边的方向，w / h 边长。
using System.Collections.Generic;
using UnityEngine;

namespace Monster
{
    public static class CityConst
    {
        public const float Cell = 0.1f;      // 每格 10cm：两格一个街区，第三格是路
        public const int RoadEvery = 3;
        public const float Walk = 0.012f;    // 人行道宽
        public const float Fringe = 0.025f;  // 城区离家具边缘留的一小圈（楼直接长在桌面上，不再铺郊野带）
        public const int WaterRows = 4;      // 港口水面占几行
    }

    public enum ZoneKind { Table, Cabinet, Floor }
    public enum Style { Glass, Office, Deco, Apt, Garage, Shop, Gas, Villa, Warehouse, Tower, Station, Condo, Brick, Modern, Spire, Mixed }
    public enum BuildingKind { House, Tower, Station }

    [System.Serializable]
    public class Zone
    {
        public string id;
        public ZoneKind kind;
        public Vector3 origin, u, v;
        public float w, h;
        public bool harbor;   // 靠 v 正方向（玩家一侧）的几行是水面
        public bool furniture;   // 这块家具是游戏生成的（房间里没扫到真家具）：要搭沙盘底座 + 虚拟桌 / 柜
        public float cell = CityConst.Cell;   // 每格边长（城市整体缩小后跟着缩）

        public Zone(string id, ZoneKind kind, Vector3 origin, Vector3 u, Vector3 v, float w, float h, bool harbor = false)
        { this.id = id; this.kind = kind; this.origin = origin; this.u = u.normalized; this.v = v.normalized; this.w = w; this.h = h; this.harbor = harbor; }

        public Vector3 Point(float a, float b, float c = 0) => origin + u * a + v * b + Vector3.up * c;
        public Vector3 Center => Point(w / 2, h / 2);
        public int Cols => Mathf.Max(1, Mathf.FloorToInt(w / cell + 1e-4f));
        public int Rows => Mathf.Max(1, Mathf.FloorToInt(h / cell + 1e-4f));
        public int LandRows => harbor ? Rows - CityConst.WaterRows : Rows;
        public bool IsWater(int gy) => harbor && gy >= LandRows;
        public float WaterB => LandRows * cell;
        public Quaternion Rotation => Quaternion.LookRotation(v, Vector3.up);
    }

    public struct StyleInfo
    {
        public float modH; public int hMin, hMax; public float fill; public string label;
        public StyleInfo(float m, int a, int b, float f, string l) { modH = m; hMin = a; hMax = b; fill = f; label = l; }
    }

    public class Building
    {
        public int id; public Zone zone; public float a, b;
        public BuildingKind kind; public Style style;
        public int h, maxH; public float modH, fw, fd; public int seed;
        public float lotW, lotD;   // 整块地块尺寸（楼等比缩放后剩下的地做院子）
        public float bodyW, bodyD, bodyOff;   // 模型真实占地（等比缩放后可能比 fw × fd 窄 / 浅）和它沿 zone.v 的偏移（变浅的楼贴临街一侧）；0 = 没填，按 fw / fd。弹药打墙按它算（11-17）
        public float dmg;          // 被扔的东西砸中后累计的伤害（层数当量，11-11）
        public Color color;                 // 塔 / 电站的配对颜色
        public int towerId = -1;            // 电站属于哪座塔
        public bool backup;                 // 备用电站
        public List<int> powerIds = new();  // 塔连着的电站
        public int backupId = -1;
        // ── 玩法状态（MonsterGame 读写）──
        public float fire, burn, hitCd, scanUntil = -1, repair, countdown, bounceCd;   // 着火时长 / 烧到塌一层的计时 / 受击冷却 / 被盯住显示到何时 / 维修进度 / 备用电站倒计时 / 护盾弹手冷却
        public bool shieldBroken, shielded, online = true;
        public int armor;            // 电站护甲（剩几格；0 = 再挨一下就毁）
        public float restart = -1;   // 电站被毁后的重启倒计时（秒；<0 = 不会重启）
        public Vector3 Base => zone.Point(a, b);
        // 11-11 用户：楼顶一层被拍掉后，瓦砾悬在半空、没落在剩下的楼上 —— 原因：模块真实高度不一样（底座高、标准层矮、楼顶带设备最高），
        // 逻辑层却按“平均层高 × 层数”算剩余楼高（写字楼拍掉顶层差 4cm，尖顶摩天楼差 12cm）。
        // tops[n] = 剩 n 层时的真实楼高（CityBuilder 按模块累加写进来）；自建方块楼 / 塔每层等高，不填
        public float[] tops;
        public float TopH(int n) => tops != null && n >= 0 && n < tops.Length ? tops[n] : n * modH;
        /// <summary>离楼底 y 高的位置在第几层（0 起；超出楼顶算顶层）</summary>
        public int StoreyAt(float y)
        {
            if (h <= 0) return 0;
            if (tops == null) return Mathf.Clamp(Mathf.FloorToInt(y / Mathf.Max(1e-5f, modH)), 0, h - 1);
            for (int i = 1; i <= h && i < tops.Length; i++) if (y < tops[i]) return i - 1;
            return h - 1;
        }
        public float Height => TopH(h) + (kind == BuildingKind.Tower && h > 0 ? 0.04f : 0);
        public float Foot => Mathf.Max(fw, fd);
    }

    // 不盖楼的地块：分区坐标中心 (a, b)、尺寸 (w, d)；kind = park / parking / plaza / court / construction
    public class OpenLot { public Zone zone; public float a, b, w, d; public string kind; public int seed; }

    public class LevelSpec
    {
        public int towers = 2, power = 1; public bool backup, near;
        public static LevelSpec Tutorial => new() { towers = 2, power = 1, near = true };
    }

    public class CityLayout
    {
        public static readonly Dictionary<Style, StyleInfo> Styles = new()
        {
            [Style.Glass] = new(0.03f, 11, 17, 0.78f, "玻璃摩天楼"),
            [Style.Office] = new(0.03f, 7, 12, 0.88f, "写字楼"),
            [Style.Deco] = new(0.03f, 6, 10, 0.9f, "海滨酒店"),
            [Style.Apt] = new(0.028f, 6, 10, 0.88f, "公寓"),
            [Style.Garage] = new(0.024f, 4, 6, 0.92f, "停车楼"),
            [Style.Shop] = new(0.034f, 1, 1, 0.92f, "商铺"),
            [Style.Gas] = new(0.03f, 1, 1, 0.5f, "加油站"),
            [Style.Villa] = new(0.03f, 1, 2, 0.72f, "别墅"),
            [Style.Warehouse] = new(0.04f, 1, 2, 0.92f, "仓库"),
            [Style.Condo] = new(0.03f, 13, 19, 0.8f, "滨海公寓塔楼"),
            [Style.Brick] = new(0.03f, 5, 8, 0.92f, "老砖楼"),
            [Style.Modern] = new(0.03f, 9, 15, 0.84f, "现代格栅写字楼"),
            [Style.Spire] = new(0.03f, 12, 17, 0.78f, "装饰艺术摩天楼"),
            [Style.Mixed] = new(0.03f, 6, 10, 0.9f, "底商住宅楼"),
        };
        public static readonly string[] TowerColors = { "#c44dff", "#ff4f8b", "#3ec5ff", "#ffb23a", "#5cff9a" };
        static readonly ZoneKind[] TowerOrder = { ZoneKind.Table, ZoneKind.Cabinet, ZoneKind.Floor, ZoneKind.Table, ZoneKind.Floor, ZoneKind.Cabinet };

        public List<Zone> zones;
        public List<Building> buildings = new();
        public List<OpenLot> opens = new();   // 不盖楼的地块（纯装饰，不参与玩法）
        public List<Building> towers = new();
        public List<Building> stations = new();
        public Dictionary<int, Building> byId = new();

        public static bool IsRoad(int gx, int gy) => gx % CityConst.RoadEvery == 0 || gy % CityConst.RoadEvery == 0;

        public class Block { public Zone zone; public int gx, gy, wc, hc; public float a, b; }

        public static List<Block> Blocks(Zone z)
        {
            var list = new List<Block>();
            int cols = z.Cols, land = z.LandRows;
            for (int gy = 1; gy < land; gy += CityConst.RoadEvery)
                for (int gx = 1; gx < cols; gx += CityConst.RoadEvery)
                {
                    int wc = Mathf.Min(2, cols - gx), hc = Mathf.Min(2, land - gy);
                    if (wc <= 0 || hc <= 0) continue;
                    list.Add(new Block { zone = z, gx = gx, gy = gy, wc = wc, hc = hc, a = (gx + wc / 2f) * CityConst.Cell, b = (gy + hc / 2f) * CityConst.Cell });
                }
            return list;
        }

        // 所有路口（分区坐标），追兵沿路网走
        public static (List<float> a, List<float> b) Intersections(Zone z)
        {
            var a = new List<float>(); var b = new List<float>();
            for (int gx = 0; gx < z.Cols; gx += CityConst.RoadEvery) a.Add((gx + 0.5f) * z.cell);
            for (int gy = 0; gy < z.Rows; gy += CityConst.RoadEvery) b.Add((gy + 0.5f) * z.cell);
            return (a, b);
        }

        struct Lot { public float da, db, w, d; }

        static List<Lot> Lots(Block blk, string plan)
        {
            float C = CityConst.Cell, Wk = CityConst.Walk;
            float W = blk.wc * C - Wk * 2, H = blk.hc * C - Wk * 2;
            float Half(int n) => n == 2 ? C - Wk - 0.004f : n * C - Wk * 2;
            float[] Offs(int n) => n == 2 ? new[] { -C / 2, C / 2 } : new[] { 0f };
            var r = new List<Lot>();
            if (plan == "one") r.Add(new Lot { w = W, d = H });
            else if (plan == "twoU") foreach (var da in Offs(blk.wc)) r.Add(new Lot { da = da, w = Half(blk.wc), d = H });
            else if (plan == "twoV") foreach (var db in Offs(blk.hc)) r.Add(new Lot { db = db, w = W, d = Half(blk.hc) });
            else foreach (var da in Offs(blk.wc)) foreach (var db in Offs(blk.hc)) r.Add(new Lot { da = da, db = db, w = Half(blk.wc), d = Half(blk.hc) });
            return r;
        }

        // 每个街区怎么分、盖什么：返回 (分法, 楼型…)，楼型按地块轮流取
        static (string plan, Style[] styles) PlanBlock(Zone z, Block blk, Rng rng, ref bool gas)
        {
            // 都市布局：中心是摩天楼 / 现代写字楼 / 公寓塔楼，往外是写字楼、底商住宅、砖楼、公寓，最外圈才有零星商铺、停车楼、加油站。
            // 别墅只在柜顶偶尔出现；同一个街区尽量混不同楼型，避免一排长一样。
            (int w, string p, Style[] s)[] opts;
            float cx = Mathf.Abs(blk.a / z.w - 0.5f) * 2, cy = Mathf.Abs(blk.b / z.h - 0.5f) * 2;
            float rad = Mathf.Max(cx, cy);   // 0 = 正中，1 = 边缘
            bool front = blk.gy + blk.hc >= z.LandRows - 1;   // 靠玩家（或靠水）的一排
            var G = Style.Glass; var O = Style.Office; var D = Style.Deco; var A = Style.Apt; var P = Style.Garage; var S = Style.Shop;
            var V = Style.Villa; var W = Style.Warehouse; var C = Style.Condo; var B = Style.Brick; var M = Style.Modern; var X = Style.Spire; var Q = Style.Mixed;
            if (z.kind == ZoneKind.Cabinet)
                opts = new[] { (3, "twoU", new[] { B, Q }), (2, "four", new[] { B, A, Q, S }), (2, "one", new[] { Q }), (1, "twoU", new[] { V, B }), (1, "one", new[] { D }) };
            else if (z.harbor && front)
                opts = new[] { (4, "twoU", new[] { W, W }), (2, "one", new[] { W }), (1, "twoU", new[] { W, P }) };
            else if (front && z.kind == ZoneKind.Table)
                opts = new[] { (3, "twoU", new[] { D, C }), (2, "one", new[] { C }), (2, "twoU", new[] { Q, D }), (1, "twoU", new[] { B, Q }) };
            else if (rad < 0.45f)
                opts = new[] { (4, "one", new[] { G }), (3, "one", new[] { X }), (3, "one", new[] { M }), (2, "one", new[] { C }), (2, "twoU", new[] { G, M }), (1, "twoV", new[] { C, O }) };
            else if (rad < 0.75f)
                opts = new[] { (3, "one", new[] { O }), (3, "twoU", new[] { Q, B }), (2, "one", new[] { M }), (2, "twoU", new[] { C, A }), (2, "twoV", new[] { O, Q }), (2, "four", new[] { B, Q, A, B }), (1, "one", new[] { G }) };
            else
            {
                if (!gas && rng.Chance(0.25f)) { gas = true; return ("one", new[] { Style.Gas }); }
                opts = new[] { (3, "four", new[] { B, Q, S, A }), (3, "twoU", new[] { Q, B }), (2, "twoU", new[] { A, B }), (1, "one", new[] { P }), (1, "four", new[] { S, B, A, Q }), (1, "one", new[] { O }) };
            }
            int sum = 0; foreach (var o in opts) sum += o.w;
            float r = rng.Next() * sum;
            foreach (var o in opts) { r -= o.w; if (r <= 0) return (o.p, o.s); }
            return (opts[^1].p, opts[^1].s);
        }

        int nextId = 1;
        Building Make(Zone z, float a, float b, BuildingKind kind, Style style, int h, float modH, float fw, float fd, Rng rng)
        {
            var bd = new Building { id = nextId++, zone = z, a = a, b = b, kind = kind, style = style, h = h, maxH = h, modH = modH, fw = fw, fd = fd, seed = rng.Int(0, 99999) };
            buildings.Add(bd); byId[bd.id] = bd;
            return bd;
        }

        public static CityLayout Build(List<Zone> zones, LevelSpec level, int seed = 7)
        {
            var c = new CityLayout { zones = zones };
            var rng = new Rng((uint)seed);
            var free = new Dictionary<Zone, List<Block>>();
            foreach (var z in zones) free[z] = rng.Shuffle(Blocks(z));
            var ground = zones.FindAll(z => free[z].Count > 0);
            bool HasFree(Zone z) => free[z].Count > 0;
            Zone PickZone(ZoneKind k) => ground.Find(z => z.kind == k && HasFree(z)) ?? ground.Find(HasFree);
            bool IsFront(Zone z, Block b) => b.gy + b.hc >= z.LandRows - 1;
            Block Take(Zone z, System.Predicate<Block> prefer)
            {
                var l = free[z]; if (l.Count == 0) return null;
                int i = prefer != null ? l.FindIndex(prefer) : 0; if (i < 0) i = 0;
                var b = l[i]; l.RemoveAt(i); return b;
            }

            var partial = new List<(Zone z, Block blk, List<Lot> rest)>();   // 塔 / 电站所在街区里剩下的地块
            // 空间小的时候（安全区小）塔 + 电站最多占全部街区的四成，剩下的留给普通楼：塔数自动减少
            int blocks = 0; foreach (var z in zones) blocks += free[z].Count;
            int per = 1 + level.power + (level.backup ? 1 : 0);
            int towers = Mathf.Clamp(Mathf.Min(level.towers, Mathf.FloorToInt(blocks * 0.4f / per)), 1, level.towers);
            if (towers < level.towers) Debug.Log($"[怪兽] 空间较小（{blocks} 个街区）：封印塔 {level.towers} → {towers}");
            for (int i = 0; i < towers; i++)
            {
                var z = PickZone(TowerOrder[i % TowerOrder.Length]);
                var blk = z != null ? Take(z, z.kind == ZoneKind.Table ? b => !IsFront(z, b) : null) : null;
                if (blk == null) break;
                // 11-11 用户：塔 / 电站的地基范围太大、周围太空 —— 原来各占一整个街区（20×20cm，塔才 8.5cm 宽，四周全是空广场）。
                // 现在塔只占街区四块地里的一块（靠玩家那排、左右随机），其余三块照常盖矮楼（见下面 partial）
                var tl = Lots(blk, "four"); int ti = 0;
                for (int k = 1; k < tl.Count; k++) if (tl[k].db > tl[ti].db + 1e-4f || (Mathf.Abs(tl[k].db - tl[ti].db) < 1e-4f && rng.Chance(0.5f))) ti = k;
                var tlot = tl[ti]; tl.RemoveAt(ti); partial.Add((z, blk, tl));
                float tfw = Mathf.Min(0.078f, Mathf.Min(tlot.w, tlot.d) - 0.006f);
                var t = c.Make(z, blk.a + tlot.da, blk.b + tlot.db, BuildingKind.Tower, Style.Tower, 6, 0.05f, tfw, tfw, rng);
                t.lotW = tlot.w; t.lotD = tlot.d;
                ColorUtility.TryParseHtmlString(TowerColors[i % TowerColors.Length], out t.color);
                c.towers.Add(t);
            }
            Building AddStation(Building tower, bool backup)
            {
                Zone z;
                var table = ground.Find(x => x.kind == ZoneKind.Table && HasFree(x));
                if (level.near && table != null) z = table;
                else
                {
                    var others = ground.FindAll(x => x != tower.zone && HasFree(x));
                    z = others.Count > 0 && (rng.Chance(0.7f) || !HasFree(tower.zone)) ? rng.Pick(others) : (HasFree(tower.zone) ? tower.zone : ground.Find(HasFree));
                }
                var blk = z != null ? Take(z, level.near && z.kind == ZoneKind.Table ? b => IsFront(z, b) : null) : null;
                if (blk == null) return null;
                // 电站：2×2 的街区只占靠玩家的半边（两格宽一格深），另外半边两块地盖矮楼；本来就只有一格深 / 宽的街区占整块。
                // 电站等比缩到地块里（原来固定 12×9cm，最前排一格深的街区只有 7.6cm 深，电站和广场都铺到了人行道和马路上）
                Lot sl; var rest = new List<Lot>();
                if (blk.wc >= 2 && blk.hc >= 2)
                {
                    var halves = Lots(blk, "twoV"); int hi = halves[0].db > halves[1].db ? 0 : 1;
                    sl = halves[hi];
                    foreach (var q in Lots(blk, "four")) if (Mathf.Sign(q.db) != Mathf.Sign(sl.db)) rest.Add(q);
                }
                else sl = Lots(blk, "one")[0];
                partial.Add((z, blk, rest));
                float sfw = Mathf.Min(0.11f, sl.w - 0.02f, (sl.d - 0.008f) * 16f / 12f), sfd = sfw * 12f / 16f;
                var s = c.Make(z, blk.a + sl.da, blk.b + sl.db, BuildingKind.Station, Style.Station, 1, 0.036f, sfw, sfd, rng);
                s.lotW = sl.w; s.lotD = sl.d;
                s.towerId = tower.id; s.color = tower.color; s.backup = backup;
                c.stations.Add(s);
                return s;
            }
            foreach (var t in c.towers)
            {
                for (int k = 0; k < level.power; k++) { var s = AddStation(t, false); if (s != null) t.powerIds.Add(s.id); }
                if (level.backup) { var s = AddStation(t, true); if (s != null) t.backupId = s.id; }
            }

            // 塔 / 电站街区里剩下的地块：盖小体量的矮楼（不挡塔、不压护盾罩）
            foreach (var (pz, pblk, rest) in partial)
                foreach (var lot in rest)
                {
                    var pool = pz.kind == ZoneKind.Cabinet ? new[] { Style.Brick, Style.Mixed } : new[] { Style.Brick, Style.Mixed, Style.Apt, Style.Shop };
                    var st = pool[rng.Int(0, pool.Length - 1)]; var PS = Styles[st];
                    int ph = Mathf.Min(rng.Int(PS.hMin, PS.hMax), Mathf.Max(PS.hMin, 4));
                    float pf = PS.fill * rng.Range(0.88f, 0.96f);
                    var pb = c.Make(pz, pblk.a + lot.da, pblk.b + lot.db, BuildingKind.House, st, ph, PS.modH, lot.w * pf, lot.d * pf, rng);
                    pb.lotW = lot.w; pb.lotD = lot.d;
                }

            foreach (var z in zones)
            {
                bool gas = false;
                foreach (var blk in free[z])
                {
                    var (plan, styles) = PlanBlock(z, blk, rng, ref gas);
                    var lots = Lots(blk, plan);
                    for (int k = 0; k < lots.Count; k++)
                    {
                        var lot = lots[k];
                        // 边缘渐稀：离城区边缘 D 以内，越靠外越容易空地（露出真实桌面），最外圈只剩两成左右；港口靠水那排码头不稀
                        float la = blk.a + lot.da, lb = blk.b + lot.db;
                        float edge = Mathf.Min(Mathf.Min(la, z.w - la), Mathf.Min(lb, z.h - lb));
                        float D = Mathf.Min(0.26f, 0.22f * Mathf.Min(z.w, z.h)) * (Mathf.Min(z.w, z.h) < 1.0f ? 0.35f : 1f);   // 小城区（原地模式）边缘只稀薄一点点，不然楼太少
                        float e = Mathf.Clamp01(edge / D);
                        bool dock = z.harbor && IsFront(z, blk);
                        // 11-09：边缘不再“渐稀”成空地（用户：边缘一排红绿灯、空着不合理，应该摆楼）——边缘照样盖楼，只是更矮（见下面 ek）
                        // 不是每块地都盖楼：一部分做成停车场 / 街心公园 / 广场 / 球场 / 工地（中心少、往外多；柜顶和码头不做）
                        float rad0 = Mathf.Max(Mathf.Abs(la / z.w - 0.5f), Mathf.Abs(lb / z.h - 0.5f)) * 2;
                        if (!dock && z.kind != ZoneKind.Cabinet && rng.Next() < Mathf.Lerp(0.07f, 0.2f, rad0) * (Mathf.Min(z.w, z.h) < 1.0f ? 0.3f : 1f))
                        {
                            bool big = lot.w > 0.12f && lot.d > 0.12f;
                            string[] kinds = big ? new[] { "park", "parking", "construction", "plaza", "park", "parking" } : new[] { "parking", "park", "plaza", "court", "parking" };
                            c.opens.Add(new OpenLot { zone = z, a = la, b = lb, w = lot.w - 0.004f, d = lot.d - 0.004f, kind = kinds[rng.Int(0, kinds.Length - 1)], seed = rng.Int(0, 99999) });
                            continue;
                        }
                        var style = styles[k % styles.Length];
                        var S = Styles[style];
                        int h = rng.Int(S.hMin, S.hMax);
                        // 摩天楼越靠中心越高，形成天际线
                        if (style == Style.Glass || style == Style.Spire || style == Style.Modern || style == Style.Condo)
                        {
                            float cc = 1 - Mathf.Min(1, Mathf.Max(Mathf.Abs(blk.a / z.w - 0.5f), Mathf.Abs(blk.b / z.h - 0.5f)) * 2.0f);
                            h = Mathf.RoundToInt(S.hMin + (S.hMax - S.hMin) * (0.35f + 0.65f * cc) * rng.Range(0.85f, 1.05f));
                        }
                        // 边缘过渡：离城区边缘越近越矮，天际线从中间往外落下来
                        float ek = Mathf.Lerp(0.3f, 1f, e);
                        if (ek < 1) h = Mathf.Max(S.hMin, Mathf.RoundToInt(h * ek));
                        float f = S.fill * rng.Range(0.9f, 1f);
                        float fd = lot.d * (style == Style.Glass || style == Style.Spire ? S.fill * rng.Range(0.85f, 1f) : f);
                        // 码头那排：靠水留出 6cm 的码头作业带（岸桥、集装箱堆场在这），楼往里收
                        float lotD = lot.d, lotB = blk.b + lot.db;
                        if (dock) { float cut = Mathf.Min(0.06f, lotD * 0.45f); lotD -= cut; lotB -= cut * 0.5f; fd = Mathf.Min(fd, lotD * f); }
                        var hb = c.Make(z, blk.a + lot.da, lotB, BuildingKind.House, style, h, S.modH, lot.w * f, fd, rng);
                        hb.lotW = lot.w; hb.lotD = lotD;
                    }
                }
            }
            return c;
        }

        /// <summary>
        /// 按系统安全区排布（11-08 用户第三轮：场景必须在安全区内；站着玩要舒服）。
        /// inside(p) 判断水平面上的点是否在安全区里（已留余量）；player 是玩家站的位置，玩家面朝 +Z。
        ///   · 主桌在正前方，近边离玩家 0.30m；在“家具外轮廓四角都在安全区内、最远处伸手够得到”的前提下尽量大；
        ///   · 空间够才加：左边一个高柜（比桌高 0.25m）；桌子侧面的真实地板上铺一条长河道（11-09 用户要求河放地上、长一点）；
        ///   · 桌面高度 = 头高 − 0.62m（0.85～1.05m）：站着看城市约低头 40°，不用一直弯腰。
        /// </summary>
        public static List<Zone> FitRoom(System.Func<Vector2, bool> inside, Vector2 player, float headY, bool stationary, float heightOffset = 0)
        {
            // 11-13 用户：不强制站着玩，站着坐着都行 —— 头低于 1.32m 当作坐着：虚拟桌按书桌高度（头下方 0.5m，0.6～0.78m），
            // 桌子更浅（最深 0.6m）、离身体更近（0.24m），坐着不用起身就够得到最远一排
            bool seated = headY < 1.32f;
            float topY = (seated ? Mathf.Clamp(headY - 0.5f, 0.6f, 0.78f) : Mathf.Clamp(headY - 0.62f, 0.85f, 1.05f)) + heightOffset;   // heightOffset：设置里的“城市高度”
            float reach = seated ? 0.86f : stationary ? 0.88f : 1.3f;      // 最远处离玩家（坐着 / 站着不走 / 能走动）
            const float Over = 0.08f;                      // 家具（底座 + 木框 + 桌面）比城区每边多出来的
            bool Fits(float x0, float z0, float x1, float z1)
            {
                x0 -= Over; z0 -= Over; x1 += Over; z1 += Over;
                return inside(new Vector2(x0, z0)) && inside(new Vector2(x1, z0)) && inside(new Vector2(x1, z1)) && inside(new Vector2(x0, z1));
            }
            static float Snap(float v) => Mathf.Floor(v / 0.05f + 1e-4f) * 0.05f;
            float near = player.y + (seated ? 0.24f : 0.30f);
            // 柜（左）贴着主桌放；主桌尺寸、柜和地板河一起算，取总分最高的组合
            float cx = player.x;   // 主桌中心 x（为了给地板河让地方，主桌可以略往左挪）
            (float x0, float z0, float w, float d)? Side(float mainW, bool left, float w0, float w1, float d)
            {
                for (float w = w0; w >= w1 - 1e-4f; w -= 0.05f)
                {
                    float x0 = left ? cx - mainW / 2 - Over * 2 - 0.08f - w : cx + mainW / 2 + Over * 2 + 0.08f;
                    float z0 = near + (left ? 0.05f : 0);
                    if (z0 + d <= player.y + reach + 0.05f && Fits(x0, z0, x0 + w, z0 + d)) return (x0, z0, Snap(w), Snap(d));
                }
                return null;
            }
            // 地板上的河：返回 (靠桌一侧的 x, 起点 z, 终点 z, 在哪一侧 +1 右 / -1 左, 长度)
            (float xin, float zs, float ze, int side, float len)? River(float mainW, bool leftFree)
            {
                // 河道四周 CityEdge 还有一圈步行道 + 路沿（约 Over），连这一圈一起算：不压桌子、不出安全区
                const float RW = 0.25f, Gap = 0.05f, EIn = 0.09f, EOut = 0.07f, EEnd = 0.13f;   // 河岸外轮廓（实测：靠桌侧路 + 步行道、外侧海堤、两端）
                (float, float, float, int, float)? bestR = null;
                foreach (int side in leftFree ? new[] { 1, -1 } : new[] { 1 })
                {
                    float xin = cx + side * (mainW / 2 + Over + Gap + EIn), xout = xin + side * RW;
                    float ein = xin - side * EIn, eout = xout + side * EOut;   // 含河岸的外轮廓
                    float lo = player.y - 0.7f, hi = player.y + reach + 0.1f;
                    // 两端各自往里收，直到外轮廓四个角都在安全区里
                    bool In(float z) => inside(new Vector2(ein, z)) && inside(new Vector2(eout, z));
                    float zs = -9, ze = -9;
                    for (float z = player.y + 0.4f; z >= lo; z -= 0.02f) { if (!In(z)) break; zs = z; }
                    for (float z = player.y + 0.4f; z <= hi; z += 0.02f) { if (!In(z)) break; ze = z; }
                    if (zs < -8 || ze < -8) continue;
                    zs += EEnd; ze -= EEnd;   // 两端河岸
                    float len = Mathf.Min(1.4f, ze - zs);
                    if (len < 0.45f) continue;
                    zs = Mathf.Max(zs, ze - len);
                    if (!bestR.HasValue || len > bestR.Value.Item5 + 0.05f) bestR = (xin, zs, ze, side, len);
                }
                return bestR;
            }
            float bw = 0, bd = 0, best = 0;
            (float x0, float z0, float w, float d)? bc = null;
            float bx = player.x;
            foreach (float dx in new[] { 0f })   // 11-09 用户：城市要正对玩家 —— 不再为了给河让地方把桌子挪偏
            for (float w = 1.6f; w >= 0.6f - 1e-4f; w -= 0.05f)
                for (float d = seated ? 0.6f : 0.8f; d >= 0.3f - 1e-4f; d -= 0.05f)
                {
                    cx = player.x + dx;
                    if (near + d > player.y + reach || !Fits(cx - w / 2, near, cx + w / 2, near + d)) continue;
                    var c = Side(w, true, 0.55f, 0.35f, 0.35f);
                    var r = River(w, !c.HasValue);
                    // 河的分量：长 0.6m 的河约等于 0.1㎡ 桌面；挪桌子略扣分（正对玩家最舒服）
                    float score = w * d + (c.HasValue ? c.Value.w * c.Value.d * 0.8f : 0) + (r.HasValue ? r.Value.len * 0.17f : 0) - Mathf.Abs(dx) * 0.15f;
                    if (score > best) { best = score; bw = w; bd = d; bc = c; bx = cx; }
                }
            cx = bx;
            if (bw <= 0) { bw = 0.6f; bd = 0.3f; Debug.LogWarning("[怪兽] 安全区太小，按最小桌面摆放"); }
            bw = Snap(bw); bd = Snap(bd);
            // Zone 的 origin 是 (x 最小, 高度, z 最大) 那个角：u 向右、v 向玩家（−Z）
            // 主桌不做港口：水面会在靠玩家一侧，楼被挤到远处够不着（11-08 试过，否掉）。没有河时巡逻艇自动换成警车
            var zones = new List<Zone> { new("table", ZoneKind.Table, new Vector3(cx - bw / 2, topY, near + bd), Vector3.right, Vector3.back, bw, bd) { furniture = true } };
            if (bc.HasValue) { var c = bc.Value; zones.Add(new Zone("cabinet", ZoneKind.Cabinet, new Vector3(c.x0, topY + 0.25f, c.z0 + c.d), Vector3.right, Vector3.back, c.w, c.d) { furniture = true }); }
            // 河（11-09 用户：河放到地上、长一点；场景不能和虚拟桌子重叠）：直接铺在真实地板上的一条长河道，
            // 在桌子侧面（离桌子含底座再空 7cm，碰不到桌腿），沿安全区能放多长放多长（最长 1.4m）；靠桌子一侧一条沿河路，外侧是水
            var rv = River(bw, !bc.HasValue);
            if (rv.HasValue)
            {
                var (xin, zs, ze, side, len) = rv.Value;
                const float RW = 0.25f;
                var zr = side > 0
                    ? new Zone("floor", ZoneKind.Floor, new Vector3(xin, 0.002f, zs), Vector3.forward, Vector3.right, len, RW, true)
                    : new Zone("floor", ZoneKind.Floor, new Vector3(xin, 0.002f, ze), Vector3.back, Vector3.left, len, RW, true);
                zones.Add(zr);
            }
            Debug.Log($"[怪兽] 布局：主桌 {bw:0.00}×{bd:0.00}m 高 {topY:0.00}m，柜 {(zones.Exists(z => z.kind == ZoneKind.Cabinet) ? "有" : "无")}，地板河 {(zones.Exists(z => z.harbor) ? zones.Find(z => z.harbor).w.ToString("0.00") + "m" : "无")}");
            return zones;
        }

        /// <summary>
        /// 11-11 用户：每一关城市都一样；检测到有桌子的话，后面的关可以在玩家左右两边再加虚拟的桌子 / 柜子。
        /// sides ≥ 1：玩家左手边加一个柜子（比桌面高 20cm）；sides ≥ 2：右手边再加一张小桌（比桌面低 4cm）。
        /// 位置：紧挨着玩家的左 / 右侧（离玩家 30～40cm，略靠后，转身 90° 正好面对它、伸手够得到），
        /// 不和主桌 / 已有的柜子 / 地板河重叠，四个角都在安全区里；放不下就不放（返回里说明）。真桌子和虚拟桌都适用。
        /// </summary>
        public static string AddSides(List<Zone> zones, System.Func<Vector2, bool> inside, Vector3 player, int sides)
        {
            if (sides <= 0 || zones.Count == 0) return "";
            var main = zones[0]; Vector3 r = main.u, f = -main.v; player.y = 0;
            const float Over = 0.08f;
            (float r0, float r1, float f0, float f1) Box(Zone z)
            {
                float a0 = 1e9f, a1 = -1e9f, b0 = 1e9f, b1 = -1e9f;
                foreach (var c in new[] { z.Point(0, 0), z.Point(z.w, 0), z.Point(0, z.h), z.Point(z.w, z.h) })
                {
                    var d = c - player; float a = Vector3.Dot(d, r), b = Vector3.Dot(d, f);
                    a0 = Mathf.Min(a0, a); a1 = Mathf.Max(a1, a); b0 = Mathf.Min(b0, b); b1 = Mathf.Max(b1, b);
                }
                return (a0, a1, b0, b1);
            }
            Zone Try(bool left, ZoneKind kind, string id, float y, int pass = 0)
            {
                // 所有放得下的摆法里挑分最高的：面积大、中心尽量在玩家正侧面略靠后（fc ≈ −0.12；太靠后要转身超过 90°）
                Zone best = null; float bs = -1;
                // 11-12 用户：左右两边离人有点近 → 离玩家 46cm（原来 34cm；放不下再退到 40cm）。最远处 86cm，站着伸手还够得到
                // 11-17 用户：有时两边都有城区、有时只有一边（之前两边都有，才有被包围的感觉）—— 原来一边放不下（出了安全区，或者和桌子挨得太近）就直接不放。
                // 现在分三轮找：pass 0 原来的标准摆法；pass 1 放宽（可以更近、更窄、更短、更靠后）；pass 2 不再要求在安全区里（虚拟家具，玩家不用走过去，只是伸手够）。
                // 调用方一轮一轮试，保证两边一定都有
                var gaps = pass == 0 ? new[] { 0.46f, 0.4f } : new[] { 0.46f, 0.4f, 0.34f };
                var ws = pass == 0 ? new[] { 0.4f, 0.35f, 0.3f } : new[] { 0.4f, 0.35f, 0.3f, 0.25f };
                var ds = pass == 0 ? new[] { 0.7f, 0.6f, 0.5f, 0.4f } : new[] { 0.7f, 0.6f, 0.5f, 0.4f, 0.32f };
                var fcs = pass == 0 ? new[] { -0.05f, -0.12f, -0.2f, -0.3f } : new[] { 0.05f, -0.05f, -0.12f, -0.2f, -0.3f, -0.4f, -0.5f };
                foreach (float gap in gaps)
                    foreach (float w in ws)
                        foreach (float d in ds)
                            foreach (float fc in fcs)
                            {
                                float ra = left ? -(gap + w) : gap, rb = left ? -gap : gap + w, fa = fc - d / 2, fb = fc + d / 2;
                                float score = w * d - Mathf.Abs(fc + 0.12f) * 0.5f - (gap < 0.44f ? 0.03f : 0);
                                if (score <= bs) continue;
                                bool ok = true;
                                foreach (var (cr, cf) in new[] { (ra - Over, fa - Over), (rb + Over, fa - Over), (ra - Over, fb + Over), (rb + Over, fb + Over) })
                                { var p = player + r * cr + f * cf; if (pass < 2 && !inside(new Vector2(p.x, p.z))) { ok = false; break; } }
                                if (!ok) continue;
                                foreach (var z in zones)
                                {
                                    var b = Box(z); float M = (z.furniture ? 0.2f : 0.13f) * (pass == 2 ? 0.75f : 1);   // 两件家具的台面外沿（虚拟家具每边多 8cm）+ 中间留一条缝（最后一轮缝收窄）
                                    if (ra < b.r1 + M && rb > b.r0 - M && fa < b.f1 + M && fb > b.f0 - M) { ok = false; break; }
                                }
                                if (!ok) continue;
                                // v 指向玩家；u = v × 上；origin = 远离玩家那条边上 u 最小的角
                                var v = left ? r : -r; var u = Vector3.Cross(v, Vector3.up).normalized;
                                var far = player + r * (left ? ra : rb);
                                float ua = Vector3.Dot(f * fa, u), ub = Vector3.Dot(f * fb, u);
                                var origin = far + u * Mathf.Min(ua, ub); origin.y = y;
                                best = new Zone(id, kind, origin, u, v, d, w) { furniture = true }; bs = score;
                            }
                return best;
            }
            var log = new List<string>();
            if (!zones.Exists(z => z.kind == ZoneKind.Cabinet))
            {
                Zone c = null; int pc = 0; for (; pc < 3 && c == null; pc++) c = Try(true, ZoneKind.Cabinet, "cabinet", main.origin.y + 0.2f, pc);
                if (c == null && zones.RemoveAll(z => z.harbor && z.kind == ZoneKind.Floor) > 0) { for (pc = 0; pc < 3 && c == null; pc++) c = Try(true, ZoneKind.Cabinet, "cabinet", main.origin.y + 0.2f, pc); if (c != null) log.Add("去掉地板河给左边的柜子让位"); }
                if (c != null) { zones.Add(c); log.Add($"左手边加柜子 {c.w:0.00}×{c.h:0.00}m" + (pc > 1 ? $"（第 {pc} 轮才放下）" : "")); } else log.Add("左手边放不下柜子");
            }
            if (sides >= 2)
            {
                var t = Try(false, ZoneKind.Table, "side", main.origin.y - 0.04f);
                if (t == null && zones.RemoveAll(z => z.harbor && z.kind == ZoneKind.Floor) > 0) { t = Try(false, ZoneKind.Table, "side", main.origin.y - 0.04f); if (t != null) log.Add("去掉地板河给右边的小桌让位"); }
                for (int pt = 1; pt < 3 && t == null; pt++) { t = Try(false, ZoneKind.Table, "side", main.origin.y - 0.04f, pt); if (t != null) log.Add($"右手边第 {pt + 1} 轮才放下"); }
                if (t != null) { zones.Add(t); log.Add($"右手边加小桌 {t.w:0.00}×{t.h:0.00}m"); } else log.Add("右手边放不下小桌");
            }
            return string.Join("，", log);
        }

        /// <summary>安全区：圆（原地模式）</summary>
        public static System.Func<Vector2, bool> Circle(Vector2 c, float r, float margin = 0.04f) => p => (p - c).magnitude <= r - margin;
        /// <summary>安全区：任意凸多边形 / 矩形（逆时针或顺时针都行）</summary>
        public static System.Func<Vector2, bool> Polygon(List<Vector2> poly, float margin = 0.06f) => p =>
        {
            int n = poly.Count; float sign = 0;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % n]; var e = b - a;
                float cr = e.x * (p.y - a.y) - e.y * (p.x - a.x);
                float dist = cr / Mathf.Max(e.magnitude, 1e-5f);
                if (sign == 0) sign = Mathf.Sign(cr);
                if (dist * sign < margin) return false;
            }
            return true;
        };

        // 桌面预览用的默认房间：玩家站在原点面朝 +Z；v 指向玩家（城区的“前排”靠玩家）
        public static List<Zone> DefaultRoom() => new()
        {
            new Zone("table", ZoneKind.Table, new Vector3(-1.1f, 0.76f, 1.5f), Vector3.right, Vector3.back, 2.2f, 1.2f) { furniture = true },
            new Zone("cabinet", ZoneKind.Cabinet, new Vector3(-2.35f, 1.0f, 1.45f), Vector3.right, Vector3.back, 0.8f, 0.6f) { furniture = true },
            new Zone("floor", ZoneKind.Floor, new Vector3(1.25f, 0.0f, 2.4f), Vector3.right, Vector3.back, 2.2f, 2.2f, true),
        };
    }

    // 可复现的随机数（mulberry32，与原型一致）
    public class Rng
    {
        uint a;
        public Rng(uint seed) { a = seed; }
        public float Next()
        {
            unchecked
            {
                a += 0x6d2b79f5u;
                uint t = a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (float)((t ^ (t >> 14)) / 4294967296.0);
            }
        }
        public float Range(float lo, float hi) => lo + (hi - lo) * Next();
        public int Int(int lo, int hi) => lo + Mathf.FloorToInt(Next() * (hi - lo + 1));
        public bool Chance(float p) => Next() < p;
        public T Pick<T>(List<T> l) => l[Mathf.Min(l.Count - 1, Mathf.FloorToInt(Next() * l.Count))];
        public List<T> Shuffle<T>(List<T> l)
        {
            for (int i = l.Count - 1; i > 0; i--) { int j = Mathf.FloorToInt(Next() * (i + 1)); (l[i], l[j]) = (l[j], l[i]); }
            return l;
        }
    }
}
