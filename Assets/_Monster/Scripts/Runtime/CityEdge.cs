// 城市边界（11-08 用户定：B+C 结合）：沿每个城区的外轮廓一圈薄混凝土路沿（陆地边）/ 海堤（港口水面三边），
// 外侧一条窄步行道（浅色铺地 + 砖缝），步行道上隔一段一盏小路灯，路灯之间挂一串暖色串灯（悬垂的弧线 + 小灯泡）。
// 真家具上：路沿就贴着城区边（城区本身离家具边留了 Fringe）；虚拟桌柜：底座顶面整块变成步行道，不再是草地。
// 按真实尺寸在城区缩放之后生成（挂在城市根节点下），所以楼缩小也不会把路沿变细。整圈合成一个网格（每个城区 3 次绘制）。
using System.Collections.Generic;
using UnityEngine;

namespace Monster
{
    public partial class CityBuilder
    {
        const float CurbW = 0.008f, CurbH = 0.0045f, WalkW = 0.026f;
        /// <summary>玩家正前方落在主城区 u 方向上的位置（真实尺寸；MonsterMR 在生成前写，-1 = 不留暂停钮的位置）</summary>
        public static float PauseA = -1;
        /// <summary>暂停钮底座的位置（主城区近边线上、留出来的那一段正中，世界坐标）；BuildEdge 写，PauseButton 读</summary>
        public Vector3? pausePad;
        public const float PadTop = 0.0082f;   // 暂停钮底座顶面高度（比路沿勾缝 7.35mm 高，路沿从底座里穿过去）

        void BuildEdges()
        {
            foreach (var z in city.zones) BuildEdge(z);
        }

        void BuildEdge(Zone z)
        {
            var root = new GameObject("Edge_" + z.id).transform; root.SetParent(this.root, false);
            var stone = PM("#e6e1d6", 0.0014f); var stoneDark = PM("#5d5a55", 0.0012f); var walk = WalkTex();
            var walkMat = new Material(CityMaterials.Toon) { name = "promenade", enableInstancing = true };
            walkMat.SetTexture("_BaseMap", walk); walkMat.SetFloat("_OutlineWidth", 0);
            float y0 = z.origin.y;
            // 四条边：(起点 a,b) → (终点 a,b)、向外的方向（城区坐标），以及这条边是不是港口水面那一侧
            float W = z.w, H = z.h;
            // 港口城区：陆地部分（b < WaterB）四周是路沿 + 步行道；水面那一段（b ≥ WaterB）左右两边没有路沿——
            // 水面本身两端渐隐进真实地面（CityWater），岸线就是陆地近边，那条做成海堤（不铺步行道）
            float L = z.harbor ? z.WaterB : H;
            var edges = new List<(Vector2 a, Vector2 b, Vector2 outDir, bool sea)>
            {
                (new(0, 0), new(W, 0), new(0, -1), false),                    // 远边（v = 0）
                (new(W, 0), new(W, L), new(1, 0), false),                     // 右（陆地段）
                (new(W, L), new(0, L), new(0, 1), z.harbor),                  // 近边：港口时是岸线海堤
                (new(0, L), new(0, 0), new(-1, 0), false),                    // 左（陆地段）
            };
            // 地板上的河道城区整片都是水（LandRows ≤ 0，WaterB 是负的）：只有靠桌那一侧（远边）有河岸。
            // 以前左右两条“陆地段”的长度是负的，在两头各画出一截反向的短桩、和远边的步行道叠在一起（重合面，会闪）→ 这种城区只留远边一条，两头敞开；
            // 步行道一直铺到码头墙（原来只有 2.6cm 宽，和码头墙之间露出一条水）
            bool allWater = z.harbor && L <= 0.001f;
            if (allWater) { edges[1] = (edges[1].a, edges[1].b, edges[1].outDir, true); edges[3] = (edges[3].a, edges[3].b, edges[3].outDir, true); }
            var parts = new List<CombineInstance>(); var partsDark = new List<CombineInstance>(); var partsWalk = new List<CombineInstance>();
            Matrix4x4 M(Vector2 ab, float y, Vector3 size, Vector2 along)
            {
                var p = z.Point(ab.x, ab.y) ; p.y = y0 + y;
                var dir = (z.u * along.x + z.v * along.y).normalized;
                return Matrix4x4.TRS(p, Quaternion.LookRotation(dir, Vector3.up), size);
            }
            var box = CityMeshes.Box();   // 原点在底面
            // 11-13 用户真机：四周的路沿在闪、地面拐角处贴图在闪 —— 原来每条边的路沿 / 勾缝 / 步行道都往两头各伸出一截，
            // 四个拐角上两条边的盒子叠在一起（顶面同高、步行道的砖纹方向还不一样）；勾缝的外侧面又和路沿的外侧面完全重合。同一位置两个面 = 闪。
            // 现在一圈按“首尾相接”拼：每条边的条带从自己起点的外拐角开始（盖住拐角那一块），到下一条边条带的内侧面为止，谁也不压谁；
            // 勾缝比路沿往外多凸 0.3mm、往上高 0.6mm，没有任何一个面和路沿共面。
            // i0 / i1：条带内 / 外侧离城区边线的距离；y1：顶面高度
            void Strip(List<CombineInstance> list, int k, float i0, float i1, float y1, float openInset = 0)
            {
                var (a, b, o, sea) = edges[k];
                bool openStart = edges[(k + 3) % 4].sea, openEnd = edges[(k + 1) % 4].sea;
                var along = b - a; float len = along.magnitude; var d = along / len;
                float s0 = openStart ? -openInset : i1, s1 = openEnd ? -openInset : i0;   // 敞开的一头：勾缝比路沿短一点点，端面不共面
                var mid = a + d * ((len + s1 - s0) * 0.5f) + o * ((i0 + i1) * 0.5f);
                list.Add(new CombineInstance { mesh = box, transform = M(mid, 0, new Vector3(i1 - i0, y1, len + s0 + s1), d) });
            }
            const float CurbTop = 0.00675f, JointTop = 0.00735f, WalkTop = 0.0033f;   // 顶面高度和以前一样（贴地各层的间距是按这几个高度排的）
            for (int k = 0; k < edges.Count; k++)
            {
                if (edges[k].sea) continue;   // 港口岸线已有码头边石（CityStreets.BuildHarbor），不重复
                Strip(parts, k, 0, CurbW, CurbTop);
                Strip(partsDark, k, CurbW - 0.0021f, CurbW + 0.0003f, JointTop, 0.0004f);   // 路沿外侧一道深色勾缝
                // 步行道（虚拟家具上铺满底座一直到木框，真家具上 2.6cm 宽）
                float ww = allWater ? Mathf.Max(WalkW, -L - CurbW - 0.0019f) : z.furniture ? CityConst.Fringe + 0.04f - CurbW : WalkW;
                Strip(partsWalk, k, CurbW + 0.0003f, CurbW + ww, WalkTop);
            }
            Part(root, Combine(parts), stone, Vector3.zero, Vector3.one, "Curb");
            Part(root, Combine(partsDark), stoneDark, Vector3.zero, Vector3.one, "CurbJoint");
            if (partsWalk.Count > 0) Part(root, Combine(partsWalk), walkMat, Vector3.zero, Vector3.one, "Promenade").GetComponent<MeshRenderer>().receiveShadows = true;
            // 路灯 + 串灯：沿陆地边每 18cm 一盏，相邻两盏之间一串弧线小灯
            var lamps = new List<Vector3>();
            foreach (var (a, b, o, sea) in edges)
            {
                if (sea) continue;
                var along = b - a; float len = along.magnitude; var d = along / len;
                int n = Mathf.Max(1, Mathf.FloorToInt(len / 0.18f));
                var row = new List<Vector3>();
                for (int i = 0; i <= n; i++)
                {
                    var ab = a + d * (len * i / n) + o * (CurbW + WalkW * 0.55f);
                    var p = z.Point(ab.x, ab.y); p.y = y0 + 0.0012f;
                    row.Add(p);
                }
                lamps.AddRange(row);
                // 涂鸦立牌（11-08 用户：城里要有和标题一套的涂鸦立牌，但不用到处都是）：
                // 近边 1 块（柜顶也只这 1 块）、左右边各 1 块（地板港口只放远边），放在一段灯距正中，那一段不挂串灯
                int want = z.kind == ZoneKind.Cabinet ? (o.y > 0.5f ? 1 : 0) : z.harbor ? (o.y < -0.5f ? 1 : 0) : (o.y < -0.5f ? 0 : 1);
                var skip = new HashSet<int>();
                // 11-13 用户：暂停钮不要和模型穿插 —— 主城区近边上，离玩家正前方最近的那一段灯距留给暂停钮：
                // 这一段不挂串灯、不立牌子，按钮底座落在这段正中（离两头的路灯各半段灯距，至少 9cm）
                int padSeg = -1;
                if (z == city.zones[0] && o.y > 0.5f && PauseA >= 0)
                {
                    padSeg = Mathf.Clamp(Mathf.FloorToInt((a.x - Mathf.Clamp(PauseA, 0, W)) / (len / n)), 0, n - 1);
                    skip.Add(padSeg);
                    var pm = a + d * (len * (padSeg + 0.5f) / n);
                    var pw = z.Point(pm.x, pm.y); pw.y = y0;
                    pausePad = pw;
                }
                if (want > 0 && row.Count >= 3)
                {
                    int gi = Mathf.Clamp(Mathf.RoundToInt((row.Count - 2) * (0.3f + 0.4f * Mathf.Repeat(z.w * 7.31f + o.x * 3.7f + o.y * 1.9f, 1))), 0, row.Count - 2);
                    if (gi == padSeg) gi = gi + 1 <= row.Count - 2 ? gi + 1 : gi - 1;   // 立牌让开暂停钮那一段
                    if (gi < 0) { for (int i = 0; i + 1 < row.Count; i++) if (!skip.Contains(i)) StringLights(root, row[i] + Vector3.up * 0.034f, row[i + 1] + Vector3.up * 0.034f); continue; }
                    skip.Add(gi);
                    var mid = (row[gi] + row[gi + 1]) * 0.5f - (z.u * o.x + z.v * o.y) * 0.004f;
                    Standee(root, mid, z.u * o.x + z.v * o.y, posterN++);
                    // 旁边一块小的“禁止巨兽”告示牌（11-09 用户：立牌旁边再来一块小的放别的信息）；不碰路灯杆
                    var segDir = (row[gi + 1] - row[gi]); float seg = segDir.magnitude; segDir /= Mathf.Max(1e-4f, seg);
                    float off = Mathf.Min(0.031f, seg * 0.5f - 0.011f);   // 大牌半宽 1.6cm + 小牌半宽 1.1cm + 缝
                    if (off > 0.029f) NoMonsterSign(root, mid + segDir * off, z.u * o.x + z.v * o.y, posterN);
                }
                for (int i = 0; i + 1 < row.Count; i++) if (!skip.Contains(i)) StringLights(root, row[i] + Vector3.up * 0.034f, row[i + 1] + Vector3.up * 0.034f);
            }
            var steel = PM("#3d3f48", 0.0012f);
            var bulb = GlowM("#f3ead2", "#ffd28a", 1.0f, 2.4f);
            var lampParts = new List<CombineInstance>(); var bulbParts = new List<CombineInstance>();
            var cyl = CityMeshes.Cylinder(6);
            foreach (var p in lamps)
            {
                lampParts.Add(new CombineInstance { mesh = cyl, transform = Matrix4x4.TRS(p, Quaternion.identity, new Vector3(0.0012f, 0.036f, 0.0012f)) });
                lampParts.Add(new CombineInstance { mesh = cyl, transform = Matrix4x4.TRS(p, Quaternion.identity, new Vector3(0.0026f, 0.003f, 0.0026f)) });
                bulbParts.Add(new CombineInstance { mesh = CityMeshes.Sphere(6, 4), transform = Matrix4x4.TRS(p + Vector3.up * 0.0372f, Quaternion.identity, Vector3.one * 0.0042f) });
            }
            FlushStringLights(root);
            Part(root, Combine(lampParts), steel, Vector3.zero, Vector3.one, "PromenadeLamps");
            Part(root, Combine(bulbParts), bulb, Vector3.zero, Vector3.one, "PromenadeBulbs").GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>两盏路灯之间一串悬垂的暖光小灯（线 + 灯泡），线是一串细段</summary>
        readonly List<CombineInstance> sWire = new(); readonly List<CombineInstance>[] sBulbs = { new(), new(), new() };
        void StringLights(Transform root, Vector3 a, Vector3 b)
        {
            const int N = 9;
            var wire = sWire; var bulbs = sBulbs[stringN++ % 3];
            var box = CityMeshes.Box(); var sph = CityMeshes.Sphere(5, 3);
            Vector3 P(float t) => Vector3.Lerp(a, b, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * 0.012f);
            for (int i = 0; i < N; i++)
            {
                var p0 = P(i / (float)N); var p1 = P((i + 1) / (float)N);
                var d = p1 - p0;
                wire.Add(new CombineInstance { mesh = box, transform = Matrix4x4.TRS((p0 + p1) * 0.5f, Quaternion.LookRotation(d.normalized, Vector3.up), new Vector3(0.0008f, 0.0008f, d.magnitude + 0.0004f)) });
                if (i > 0) bulbs.Add(new CombineInstance { mesh = sph, transform = Matrix4x4.TRS(p0 + Vector3.down * 0.002f, Quaternion.identity, Vector3.one * 0.0034f) });
            }
        }
        /// <summary>一个城区的串灯全部攒完后合成：线一个网格、三种颜色的灯泡各一个网格</summary>
        void FlushStringLights(Transform root)
        {
            if (stringWire == null) { stringWire = PM("#2a2a30", 0); stringBulb = new Material[] { GlowM("#ffe2a0", "#ffcf70", 1, 2.6f), GlowM("#ff9ad0", "#ff6fb0", 1, 2.4f), GlowM("#9ff2ff", "#6fe0ff", 1, 2.4f) }; }
            if (sWire.Count > 0) Part(root, Combine(sWire), stringWire, Vector3.zero, Vector3.one, "StringWire").GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < 3; i++)
                if (sBulbs[i].Count > 0) Part(root, Combine(sBulbs[i]), stringBulb[i], Vector3.zero, Vector3.one, "StringBulbs").GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sWire.Clear(); foreach (var l in sBulbs) l.Clear();
        }
        Material stringWire; Material[] stringBulb; int stringN, posterN;

        /// <summary>
        /// 涂鸦立牌：一块被涂鸦过的霓虹集团告示，钉在一块薄铁板上，两根细腿立在步行道上，稍微歪一点；
        /// 正面朝城外（朝玩家走过来的方向）。板子背面是深灰铁皮，板边一圈黑色包边。按真实尺寸：板高 5cm。
        /// </summary>
        void Standee(Transform root, Vector3 foot, Vector3 outward, int idx)
        {
            var t = new GameObject("GraffitiStandee").transform; t.SetParent(root, false);
            t.position = foot;
            t.rotation = Quaternion.LookRotation(-outward, Vector3.up) * Quaternion.Euler(-6, (idx % 2 == 0 ? 7 : -9), (idx % 3 - 1) * 2.5f);
            const float W = 0.032f, H = 0.052f, legH = 0.008f, T = 0.0014f;
            var steel = PM("#3d3f48", 0.0012f); var edgeM = PM("#16161a", 0.0008f);
            foreach (var sx in new[] { -1, 1 })
                Part(t, CityMeshes.Box(), steel, new Vector3(sx * W * 0.32f, 0, 0), new Vector3(0.0016f, legH + 0.002f, 0.0016f), "Leg");   // CityMeshes.Box 原点在底面
            Part(t, CityMeshes.Box(), edgeM, new Vector3(0, legH - 0.001f, 0.0004f), new Vector3(W + 0.002f, H + 0.002f, T), "Board");
            // 海报：用不受光的面片（和界面一样的“印刷品”），贴在板正面（朝 −Z = 朝外）
            var tex = Resources.Load<Texture2D>("UI/poster_" + (idx % 4 + 1));
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = "Poster"; q.transform.SetParent(t, false);
            q.transform.localPosition = new Vector3(0, legH + H * 0.5f, -T * 0.5f - 0.0003f);
            q.transform.localRotation = Quaternion.identity;
            q.transform.localScale = new Vector3(W, H, 1);
            var m = new Material(Shader.Find("Monster/ComicCard")) { name = "poster", mainTexture = tex, color = night ? new Color(0.62f, 0.6f, 0.7f) : new Color(0.9f, 0.86f, 0.84f) };   // 撕边透明，不受光：按昼夜压一点亮度
            var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // 板子上方歪着贴一条遮蔽胶带
            var tape = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(tape.GetComponent<Collider>());
            tape.name = "Tape"; tape.transform.SetParent(t, false);
            tape.transform.localPosition = new Vector3(W * 0.25f, legH + H - 0.002f, -T * 0.5f - 0.0006f);
            tape.transform.localRotation = Quaternion.Euler(0, 0, -18); tape.transform.localScale = new Vector3(0.0134f, 0.005f, 1);
            var tm = new Material(Shader.Find("Monster/ComicCard")) { name = "tape" }; tm.mainTexture = Resources.Load<Texture2D>("UI/tape_b");
            var tr = tape.GetComponent<MeshRenderer>(); tr.sharedMaterial = tm; tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// “禁止巨兽”告示牌：白色搪瓷牌、红色禁止圈 + 斜杠压在一个爪印上，下面一行“禁止巨兽 / NO MONSTERS”；
        /// 一根细腿立在步行道上，比涂鸦立牌小（板高 3.4cm），歪向另一边。被谁贴了一条胶带。
        /// </summary>
        void NoMonsterSign(Transform root, Vector3 foot, Vector3 outward, int idx)
        {
            var t = new GameObject("NoMonsterSign").transform; t.SetParent(root, false);
            t.position = foot;
            t.rotation = Quaternion.LookRotation(-outward, Vector3.up) * Quaternion.Euler(-4, idx % 2 == 0 ? -8 : 10, (idx % 2 == 0 ? 3 : -2.5f));
            const float W = 0.022f, H = 0.034f, legH = 0.012f, T = 0.0012f;
            var steel = PM("#3d3f48", 0.0012f);
            Part(t, CityMeshes.Box(), steel, new Vector3(0, 0, 0.001f), new Vector3(0.0016f, legH + 0.004f, 0.0016f), "Leg");
            Part(t, CityMeshes.Box(), PM("#d9d6cf", 0.0008f), new Vector3(0, legH, 0.0004f), new Vector3(W + 0.0016f, H + 0.0016f, T), "Plate");
            var shader = Shader.Find("Monster/ComicCard");
            var tint = night ? new Color(0.66f, 0.64f, 0.72f) : new Color(0.95f, 0.93f, 0.9f);
            Quad(t, "Claw", Resources.Load<Texture2D>("UI/doodle_claw"), new Vector3(0, legH + H * 0.62f, -T * 0.5f - 0.0002f), new Vector2(W * 0.62f, W * 0.62f), tint * new Color(0.25f, 0.25f, 0.3f, 1), shader);
            Quad(t, "Ban", NoTex(), new Vector3(0, legH + H * 0.62f, -T * 0.5f - 0.0004f), new Vector2(W * 0.86f, W * 0.86f), tint, shader);
            var font = Resources.Load<TMPro.TMP_FontAsset>(Monster.Game.Loc.En ? "Fonts/Corp SDF" : "Fonts/Corp SDF");
            var go = new GameObject("Text"); go.transform.SetParent(t, false);
            go.transform.localPosition = new Vector3(0, legH + H * 0.17f, -T * 0.5f - 0.0004f); go.transform.localScale = Vector3.one * 0.001f;
            var tx = go.AddComponent<TMPro.TextMeshPro>(); tx.font = font; tx.alignment = TMPro.TextAlignmentOptions.Center; tx.enableWordWrapping = false;
            tx.text = Monster.Game.Loc.En ? "NO MONSTERS" : "禁止巨兽"; tx.fontSize = Monster.Game.Loc.En ? 22 : 30; tx.color = new Color(0.75f, 0.1f, 0.12f) * tint;
            tx.rectTransform.sizeDelta = new Vector2(W * 1000, 10);
            tx.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Quad(t, "Tape", Resources.Load<Texture2D>("UI/tape_c"), new Vector3(W * 0.32f, legH + H - 0.002f, -T * 0.5f - 0.0006f), new Vector2(0.011f, 0.0037f), tint, shader, 24);
        }
        void Quad(Transform p, string n, Texture2D tex, Vector3 pos, Vector2 size, Color c, Shader sh, float rot = 0)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = n; q.transform.SetParent(p, false); q.transform.localPosition = pos; q.transform.localRotation = Quaternion.Euler(0, 0, rot);
            q.transform.localScale = new Vector3(size.x, size.y, 1);
            var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = new Material(sh) { name = n, mainTexture = tex, color = c }; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        // 红色禁止圈 + 左上到右下的斜杠（程序画，128²，抗锯齿）
        static Texture2D noTex;
        static Texture2D NoTex()
        {
            if (noTex) return noTex;
            const int S = 128; var px = new Color32[S * S]; var red = new Color(0.86f, 0.12f, 0.14f);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2 - 1, v = (y + 0.5f) / S * 2 - 1, r = Mathf.Sqrt(u * u + v * v);
                float ring = Mathf.Clamp01((0.11f - Mathf.Abs(r - 0.84f)) * S * 0.5f);
                float slash = r < 0.84f ? Mathf.Clamp01((0.1f - Mathf.Abs((u + v) * 0.7071f)) * S * 0.5f) : 0;
                float a = Mathf.Max(ring, slash);
                px[y * S + x] = new Color(red.r, red.g, red.b, a);
            }
            noTex = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "no_monster", wrapMode = TextureWrapMode.Clamp };
            noTex.SetPixels32(px); noTex.Apply(true);
            return noTex;
        }

        static Mesh Combine(List<CombineInstance> l)
        {
            var m = new Mesh { name = "edge", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.CombineMeshes(l.ToArray(), true, true); m.RecalculateBounds();
            return m;
        }

        /// <summary>步行道铺地：浅暖灰方砖，砖缝 + 一点斑驳</summary>
        static Texture2D walkTex;
        static Texture2D WalkTex()
        {
            if (walkTex) return walkTex;
            const int S = 128; var px = new Color32[S * S];
            var a = new Color(0.56f, 0.58f, 0.63f); var b = new Color(0.5f, 0.52f, 0.57f); var j = new Color(0.32f, 0.33f, 0.37f);   // 冷灰花岗岩（和城里暖色地块拉开）
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                bool joint = x % 32 < 2 || (y + (x / 32 % 2) * 16) % 32 < 2;
                float n = Mathf.PerlinNoise(x * 0.09f, y * 0.09f);
                px[y * S + x] = joint ? j : Color.Lerp(a, b, n * (((x / 32) + (y / 32)) % 2 == 0 ? 1 : 0.6f));
            }
            walkTex = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "promenade", wrapMode = TextureWrapMode.Repeat };
            walkTex.SetPixels32(px); walkTex.Apply(true);
            return walkTex;
        }
    }
}
