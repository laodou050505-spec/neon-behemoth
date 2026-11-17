// 楼顶自动铺设备（11-07）：楼盖完以后，在每栋楼楼顶用射线扫出“平且空”的地方，按楼型摆空调外机 / 风管 / 太阳能板 / 天窗 /
// 屋顶花园 / 卫星锅 / 通风管 / 小设备间 / 水箱 / 天线 / 遮阳伞座椅……
// 每栋楼的楼顶杂物合成一个网格、一个材质（16 色色板），挂在楼顶那一层下面，倒塌时一起掉。
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Monster
{
    public partial class CityBuilder
    {
        // 色板格：0 水泥 1 金属 2 深灰 3 白 4 太阳能蓝 5 天窗玻璃（夜里亮） 6 叶 7 深叶 8 木 9 红 10 青 11 黄 12 浅框 13 土 14 锈 15 暖灯（夜里亮）
        static readonly string[] RoofPal = { "#c9c3b8", "#8f959c", "#34363c", "#eeeae2", "#2f4f7a", "#8fc6d8", "#4f9d55", "#3b7f45", "#8a6a48", "#c8463c", "#3fb4a8", "#f2c53a", "#d8dde2", "#5a4636", "#9a5a3a", "#ffd9a0" };
        Material roofMat;
        Material RoofMat()
        {
            if (roofMat) return roofMat;
            var pal = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "roof_palette" };
            var emi = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "roof_palette_emis" };
            for (int i = 0; i < 16; i++) { pal.SetPixel(i, 0, CityMaterials.Hex(RoofPal[i])); emi.SetPixel(i, 0, Color.black); }
            emi.SetPixel(5, 0, CityMaterials.Hex("#ffd28a")); emi.SetPixel(15, 0, CityMaterials.Hex("#ffd9a0"));
            pal.Apply(); emi.Apply();
            roofMat = new Material(CityMaterials.Toon) { name = "roof_clutter", enableInstancing = true };
            roofMat.SetTexture("_BaseMap", pal); roofMat.SetTexture("_EmissionMap", emi);
            roofMat.SetColor("_EmissionColor", new Color(1.6f, 1.6f, 1.6f));
            roofMat.SetFloat("_NightGlow", night ? 1 : 0);
            roofMat.SetFloat("_OutlineWidth", 0.0016f);
            roofMat.SetShaderPassEnabled("SRPDefaultUnlit", false);   // 楼顶小设备不画描边（省顶点，俯视时色块已经够清楚）
            return roofMat;
        }

        // 往一个网格里堆小件（顶点已经变换到楼顶那一层的本地坐标；尺寸单位是真实米，m 里含缩放）
        class Pile
        {
            public readonly List<Vector3> v = new(), n = new(); public readonly List<Vector2> uv = new(); public readonly List<int> t = new();
            public Matrix4x4 m;
            void Add(Mesh src, Vector3 c, Vector3 size, int slot, Quaternion rot)
            {
                var sv = src.vertices; var sn = src.normals; var st = src.triangles;
                int o = v.Count;
                var p = new Vector2((slot + 0.5f) / 16f, 0.5f);
                for (int i = 0; i < sv.Length; i++)
                {
                    v.Add(m.MultiplyPoint3x4(c + rot * Vector3.Scale(sv[i], size)));
                    n.Add(m.MultiplyVector(rot * new Vector3(sn[i].x / size.x, sn[i].y / size.y, sn[i].z / size.z)).normalized);
                    uv.Add(p);
                }
                foreach (var i in st) t.Add(o + i);
            }
            public void Box(Vector3 c, Vector3 size, int slot, Quaternion? r = null) => Add(CityMeshes.Box(), c, size, slot, r ?? Quaternion.identity);
            public void Cyl(Vector3 c, float rad, float h, int slot, int seg = 10, Quaternion? r = null) => Add(CityMeshes.Cylinder(seg), c, new Vector3(rad * 2, h, rad * 2), slot, r ?? Quaternion.identity);
            public void Ball(Vector3 c, Vector3 size, int slot) => Add(CityMeshes.Sphere(), c, size, slot, Quaternion.identity);
        }

        // ───────── 各种楼顶小件（原点在脚下中心，x 宽 z 深，单位米）─────────
        delegate void PropFn(Pile p, Rng r);
        static readonly Dictionary<string, (float w, float d, PropFn fn)> RoofKit = new()
        {
            ["ac"] = (1.3f, 1.1f, (p, r) =>
            {
                p.Box(new Vector3(0, 0, 0), new Vector3(1.1f, 0.12f, 0.9f), 2);
                p.Box(new Vector3(0, 0.12f, 0), new Vector3(1.05f, 0.85f, 0.85f), 1);
                p.Cyl(new Vector3(0, 0.97f, 0), 0.34f, 0.05f, 2, 8);
                for (int i = 0; i < 4; i++) p.Box(new Vector3(-0.3f + i * 0.2f, 0.3f, -0.43f), new Vector3(0.05f, 0.5f, 0.02f), 2);
            }),
            ["acbank"] = (3.9f, 1.4f, (p, r) =>
            {
                p.Box(Vector3.zero, new Vector3(3.8f, 0.18f, 1.3f), 2);
                for (int k = -1; k <= 1; k++)
                {
                    p.Box(new Vector3(k * 1.22f, 0.18f, 0), new Vector3(1.1f, 1.0f, 1.1f), 1);
                    p.Cyl(new Vector3(k * 1.22f, 1.18f, 0), 0.42f, 0.06f, 2, 8);
                }
                p.Box(new Vector3(0, 0.5f, 0.62f), new Vector3(3.6f, 0.22f, 0.22f), 12);
            }),
            ["duct"] = (5.2f, 0.9f, (p, r) =>
            {
                for (int k = -2; k <= 2; k++) p.Box(new Vector3(k * 1.1f, 0, 0), new Vector3(0.12f, 0.45f, 0.6f), 2);
                p.Box(new Vector3(0, 0.45f, 0), new Vector3(5.0f, 0.55f, 0.55f), 12);
                p.Box(new Vector3(2.35f, 0, 0), new Vector3(0.6f, 1.0f, 0.6f), 12);
                p.Box(new Vector3(-2.3f, 1.0f, 0), new Vector3(0.7f, 0.25f, 0.7f), 1);
            }),
            ["solar"] = (4.6f, 3.4f, (p, r) =>
            {
                var tilt = Quaternion.Euler(-22, 0, 0);
                for (int row = 0; row < 3; row++)
                {
                    float z = -1.1f + row * 1.1f;
                    p.Box(new Vector3(-2.0f, 0, z), new Vector3(0.08f, 0.55f, 0.08f), 2); p.Box(new Vector3(2.0f, 0, z), new Vector3(0.08f, 0.55f, 0.08f), 2);
                    p.Box(new Vector3(0, 0.35f, z), new Vector3(4.4f, 0.06f, 0.95f), 12, tilt);
                    for (int c = -3; c <= 3; c++) p.Box(new Vector3(c * 0.6f, 0.4f, z), new Vector3(0.55f, 0.03f, 0.85f), 4, tilt);
                }
            }),
            ["skylight"] = (2.4f, 2.4f, (p, r) =>
            {
                p.Box(Vector3.zero, new Vector3(2.2f, 0.35f, 2.2f), 0);
                p.Box(new Vector3(0, 0.35f, 0), new Vector3(1.9f, 0.18f, 1.9f), 5);
                p.Box(new Vector3(0, 0.53f, 0), new Vector3(0.08f, 0.04f, 1.9f), 12);
                p.Box(new Vector3(0, 0.53f, 0), new Vector3(1.9f, 0.04f, 0.08f), 12);
            }),
            ["garden"] = (3.4f, 2.4f, (p, r) =>
            {
                p.Box(Vector3.zero, new Vector3(3.2f, 0.5f, 2.2f), 8);
                p.Box(new Vector3(0, 0.5f, 0), new Vector3(3.0f, 0.04f, 2.0f), 13);
                for (int i = 0; i < 5; i++) { float s = r.Range(0.6f, 1.0f); p.Ball(new Vector3(r.Range(-1.1f, 1.1f), 0.5f, r.Range(-0.6f, 0.6f)), new Vector3(s, s * 0.8f, s), r.Chance(0.5f) ? 6 : 7); }
                p.Cyl(new Vector3(1.0f, 0.5f, 0.4f), 0.08f, 1.0f, 8, 6);
                p.Ball(new Vector3(1.0f, 1.3f, 0.4f), new Vector3(1.2f, 1.0f, 1.2f), 6);
            }),
            ["vent"] = (0.7f, 0.7f, (p, r) =>
            {
                p.Cyl(Vector3.zero, 0.16f, 1.1f, 1, 8);
                p.Cyl(new Vector3(0, 1.1f, 0), 0.28f, 0.06f, 2, 8);
                p.Cyl(new Vector3(0, 1.22f, 0), 0.2f, 0.12f, 1, 8);
            }),
            ["dish"] = (1.5f, 1.5f, (p, r) =>
            {
                p.Box(Vector3.zero, new Vector3(0.8f, 0.1f, 0.8f), 2);
                p.Cyl(new Vector3(0, 0.1f, 0), 0.06f, 0.9f, 2, 6);
                var q = Quaternion.Euler(-55, 0, 0);
                p.Cyl(new Vector3(0, 0.9f, 0.05f), 0.6f, 0.08f, 3, 8, q);
                p.Box(new Vector3(0, 1.05f, 0.35f), new Vector3(0.08f, 0.08f, 0.5f), 2, q);
            }),
            ["shed"] = (2.8f, 2.3f, (p, r) =>
            {
                p.Box(Vector3.zero, new Vector3(2.6f, 2.2f, 2.1f), 3);
                p.Box(new Vector3(0, 2.2f, 0), new Vector3(2.8f, 0.15f, 2.3f), 2);
                p.Box(new Vector3(-0.5f, 0, -1.06f), new Vector3(0.9f, 1.9f, 0.04f), 14);
                p.Box(new Vector3(0.7f, 1.7f, -1.08f), new Vector3(0.3f, 0.15f, 0.06f), 15);
                p.Box(new Vector3(0.7f, 0.8f, -1.06f), new Vector3(0.7f, 0.5f, 0.04f), 1);
            }),
            ["tank"] = (2.2f, 2.2f, (p, r) =>
            {
                p.Box(Vector3.zero, new Vector3(2.0f, 0.3f, 2.0f), 2);
                p.Cyl(new Vector3(0, 0.3f, 0), 0.9f, 1.6f, r.Chance(0.5f) ? 3 : 10, 14);
                p.Cyl(new Vector3(0, 1.9f, 0), 0.94f, 0.08f, 1, 8);
                p.Box(new Vector3(0.95f, 0.3f, 0), new Vector3(0.06f, 1.6f, 0.4f), 1);
            }),
            ["antenna"] = (0.8f, 0.8f, (p, r) =>
            {
                p.Box(Vector3.zero, new Vector3(0.6f, 0.15f, 0.6f), 2);
                p.Box(new Vector3(0, 0.15f, 0), new Vector3(0.08f, 4.0f, 0.08f), 1);
                for (int i = 0; i < 3; i++) p.Box(new Vector3(0, 1.5f + i * 0.9f, 0), new Vector3(1.0f - i * 0.25f, 0.05f, 0.05f), 1);
                p.Box(new Vector3(0, 4.15f, 0), new Vector3(0.14f, 0.14f, 0.14f), 9);
            }),
            ["lounge"] = (3.4f, 2.6f, (p, r) =>
            {
                p.Box(Vector3.zero, new Vector3(3.2f, 0.06f, 2.4f), 8);
                p.Cyl(new Vector3(0, 0.06f, 0), 0.05f, 2.1f, 3, 6);
                p.Cyl(new Vector3(0, 2.1f, 0), 1.2f, 0.1f, r.Chance(0.5f) ? 9 : 11, 12);
                p.Cyl(new Vector3(0, 0.06f, 0), 0.45f, 0.7f, 3, 8);
                for (int i = 0; i < 4; i++) { var a = i * Mathf.PI / 2 + 0.4f; p.Box(new Vector3(Mathf.Cos(a) * 0.9f, 0.06f, Mathf.Sin(a) * 0.9f), new Vector3(0.45f, 0.45f, 0.45f), 3); }
                p.Box(new Vector3(-1.2f, 0.06f, 0.9f), new Vector3(0.6f, 0.3f, 1.9f), 12);
                p.Box(new Vector3(1.4f, 0.06f, -1.0f), new Vector3(0.06f, 1.8f, 0.06f), 2);
                p.Box(new Vector3(1.4f, 1.86f, -1.0f), new Vector3(0.2f, 0.12f, 0.2f), 15);
            }),
        };

        static (string kind, int w)[] RoofMix(Style s) => s switch
        {
            Style.Glass or Style.Office or Style.Modern or Style.Spire => new[] { ("acbank", 4), ("ac", 3), ("duct", 2), ("solar", 2), ("skylight", 2), ("vent", 1), ("antenna", 1), ("shed", 1), ("dish", 1) },
            Style.Brick => new[] { ("ac", 4), ("vent", 2), ("dish", 2), ("duct", 1), ("garden", 1), ("shed", 1), ("antenna", 1) },
            Style.Apt or Style.Condo or Style.Deco or Style.Mixed or Style.Villa => new[] { ("garden", 3), ("lounge", 2), ("ac", 3), ("dish", 2), ("vent", 1), ("tank", 1), ("solar", 1), ("skylight", 1) },
            Style.Shop or Style.Warehouse => new[] { ("ac", 3), ("vent", 1), ("skylight", 2), ("solar", 2), ("acbank", 1) },
            _ => null,
        };

        // ───────── 扫楼顶：射线找“平且空”的格子，再按楼型挑小件往里塞 ─────────
        void RoofClutter() { var it = RoofClutterSteps(); while (it.MoveNext()) { } }
        /// <summary>分步版：每栋楼之间看一下耗时，超过 8ms 就让出一帧（分帧生成时用）</summary>
        System.Collections.IEnumerator RoofClutterSteps()
        {
            var mat = RoofMat();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var b in city.buildings)
            {
                if (sw.ElapsedMilliseconds > 8) { yield return null; sw.Restart(); }
                if (b.kind != BuildingKind.House || !views.TryGetValue(b.id, out var view) || !view || view.storeys == null || view.storeys.Length == 0) continue;
                var mix = RoofMix(b.style);
                if (mix == null) continue;
                var crown = view.storeys[^1];
                var body = crown.Find("Body");
                if (!body) continue;
                float s = body.lossyScale.y;   // 世界单位 / 米
                var tmp = new List<MeshCollider>();
                foreach (var mf in view.GetComponentsInChildren<MeshFilter>())
                {
                    if (!mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; tmp.Add(mc);
                }
                Physics.SyncTransforms();
                var bb = body.GetComponent<Renderer>().bounds;
                // 格子在楼顶层的本地坐标系里排（和楼的朝向对齐），步长 0.5 米
                float step = 0.5f;
                Vector3 lo = crown.InverseTransformPoint(bb.min), hi = crown.InverseTransformPoint(bb.max);
                float x0 = Mathf.Min(lo.x, hi.x), x1 = Mathf.Max(lo.x, hi.x), z0 = Mathf.Min(lo.z, hi.z), z1 = Mathf.Max(lo.z, hi.z);
                int nx = Mathf.FloorToInt((x1 - x0) / (step * s)), nz = Mathf.FloorToInt((z1 - z0) / (step * s));
                if (nx < 3 || nz < 3) { foreach (var c in tmp) Object.DestroyImmediate(c); continue; }
                var hgt = new float[nx, nz]; var ok = new bool[nx, nz];
                float top = bb.max.y + 0.02f;
                var mine = new HashSet<Collider>(tmp);
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < nz; j++)
                    {
                        var lp = new Vector3(x0 + (i + 0.5f) * step * s, 0, z0 + (j + 0.5f) * step * s);
                        var wp = crown.TransformPoint(lp); wp.y = top;
                        var hits = Physics.RaycastAll(wp, Vector3.down, top - bb.min.y + 0.01f).Where(h => mine.Contains(h.collider)).OrderBy(h => h.distance).ToArray();
                        if (hits.Length == 0) continue;
                        var h0 = hits[0];
                        // 只要打在楼体本身、朝上的面（打到招牌 / 已有设备的不算空地）
                        if (h0.collider.transform != body || h0.normal.y < 0.97f) continue;
                        var tc = h0.textureCoord;
                        if (tc.x < 0.5f || tc.x > 0.75f || tc.y < 0.25f || tc.y > 0.5f) continue;   // 只认屋面区域（泳池、霓虹、天窗不能放东西）
                        hgt[i, j] = h0.point.y; ok[i, j] = true;
                    }
                // 临时碰撞体留到放完设备再删：放之前要按设备实际占地细查一遍（0.5 米一格的采样会漏掉夹在中间的薄墙 / 假立面）
                // 只用面积最大的那一层屋面（设备间、电梯机房的顶上不再堆东西）
                var levels = new Dictionary<int, int>();
                for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) if (ok[i, j]) { int key = Mathf.RoundToInt(hgt[i, j] / (0.05f * s)); levels[key] = levels.TryGetValue(key, out var c0) ? c0 + 1 : 1; }
                if (levels.Count == 0) { foreach (var c in tmp) Object.DestroyImmediate(c); continue; }
                int main = levels.OrderByDescending(kv => kv.Value).First().Key;
                for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) if (ok[i, j] && Mathf.Abs(Mathf.RoundToInt(hgt[i, j] / (0.05f * s)) - main) > 1) ok[i, j] = false;
                // 每种小件的格子占地；同一块平地（高度差 < 3cm）上全部空着才放
                var used = new bool[nx, nz];
                var rng = new Rng((uint)b.seed * 7 + 13);
                var pile = new Pile { m = crown.worldToLocalMatrix };
                int total = mix.Sum(x => x.w);
                int placed = 0, tries = nx * nz;
                float budget = nx * nz * 0.3f, usedCells = 0;
                for (int k = 0; k < tries && usedCells < budget; k++)
                {
                    float pick = rng.Next() * total; string kind = mix[0].kind;
                    foreach (var (kk, w) in mix) { pick -= w; if (pick <= 0) { kind = kk; break; } }
                    var (pw, pd, fn) = RoofKit[kind];
                    bool rot = rng.Chance(0.5f);
                    int cw = Mathf.CeilToInt((rot ? pd : pw) / step) + 1, cd = Mathf.CeilToInt((rot ? pw : pd) / step) + 1;   // 外加一格留缝
                    // 11-13 真机：第 5 区生成到一半停住 —— 屋面只比设备窄一格时（nx - cw == -1），rng.Int(0, -1) 返回的是 0 而不是负数，
                    // 下面按 i0 + cw 取格子就越界、整个城市生成中断。放不下就直接换下一件
                    if (cw > nx || cd > nz) continue;
                    int i0 = rng.Int(0, nx - cw), j0 = rng.Int(0, nz - cd);
                    if (i0 < 0 || j0 < 0 || i0 + cw > nx || j0 + cd > nz) continue;
                    bool fit = true; float h = 0;
                    for (int i = i0; i < i0 + cw && fit; i++)
                        for (int j = j0; j < j0 + cd && fit; j++)
                        {
                            if (!ok[i, j] || used[i, j]) { fit = false; break; }
                            if (i == i0 && j == j0) h = hgt[i, j];
                            else if (Mathf.Abs(hgt[i, j] - h) > 0.03f * s) fit = false;
                        }
                    if (!fit) continue;
                    // 细查：设备实际占地内 4×4 个点往下打，第一下都要打在同一高度的屋面上（不插进墙、不压到别的东西）
                    {
                        var cc = new Vector3(x0 + (i0 + cw * 0.5f) * step * s, 0, z0 + (j0 + cd * 0.5f) * step * s);
                        float hw = (rot ? pd : pw) * 0.5f * s, hd = (rot ? pw : pd) * 0.5f * s;
                        for (int gi = 0; gi < 16 && fit; gi++)
                        {
                            var lp = cc + new Vector3(Mathf.Lerp(-hw, hw, (gi % 4) / 3f), 0, Mathf.Lerp(-hd, hd, (gi / 4) / 3f));
                            var wp = crown.TransformPoint(lp); wp.y = top;
                            var hs = Physics.RaycastAll(wp, Vector3.down, top - bb.min.y + 0.01f).Where(x => mine.Contains(x.collider)).OrderBy(x => x.distance).ToArray();
                            if (hs.Length == 0 || hs[0].collider.transform != body || Mathf.Abs(hs[0].point.y - h) > 0.02f * s) fit = false;
                        }
                    }
                    if (!fit) continue;
                    for (int i = i0; i < i0 + cw; i++) for (int j = j0; j < j0 + cd; j++) used[i, j] = true;
                    usedCells += cw * cd;
                    var c = new Vector3(x0 + (i0 + cw * 0.5f) * step * s, 0, z0 + (j0 + cd * 0.5f) * step * s);
                    var wc = crown.TransformPoint(c); wc.y = h + 0.0004f;   // 抬 0.4mm，不和屋面同高（会闪）
                    // 小件坐标（米）→ 世界：以 wc 为原点、楼的朝向、比例 s
                    var q = crown.rotation * Quaternion.Euler(0, (rot ? 90 : 0) + (rng.Chance(0.5f) ? 180 : 0), 0);
                    pile.m = crown.worldToLocalMatrix * Matrix4x4.TRS(wc, q, Vector3.one * s);
                    if (AuditMode) pile = new Pile { m = pile.m };   // 体检模式：每件单独成一个物体
                    fn(pile, rng);
                    placed++;
                    if (AuditMode) EmitPile(pile, crown, mat, "RoofItem_" + kind);
                }
                foreach (var c in tmp) Object.DestroyImmediate(c);
                if (placed == 0 || AuditMode) continue;
                var mesh = new Mesh { name = $"roof_{b.id}", indexFormat = pile.v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.SetVertices(pile.v); mesh.SetNormals(pile.n); mesh.SetUVs(0, pile.uv); mesh.SetTriangles(pile.t, 0);
                CityMeshes.BakeSmoothNormals(mesh); mesh.RecalculateBounds();
                var go = new GameObject("RoofClutter"); go.transform.SetParent(crown, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        void EmitPile(Pile pile, Transform crown, Material mat, string name)
        {
            if (pile.v.Count == 0) return;
            var mesh = new Mesh { name = name };
            mesh.SetVertices(pile.v); mesh.SetNormals(pile.n); mesh.SetUVs(0, pile.uv); mesh.SetTriangles(pile.t, 0); mesh.RecalculateBounds();
            var go = new GameObject(name); go.transform.SetParent(crown, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
