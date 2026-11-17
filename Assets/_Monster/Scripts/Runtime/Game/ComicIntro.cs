// 开场漫画（11-08 用户：漫画形式、6 格、捏一下出一格、对白气泡讲清楚要做什么；格子要不规则、能拼在一起、错落；字不要塞满、不要挡画）。
// 一页漫画浮在玩家面前约 1m（略低于视线）。整页被几条斜线切成 6 个不规则四边形，格与格之间是等宽的斜缝，外缘高低错落。
// 每格：画按“铺满裁切”贴进多边形（每格有自己的取景焦点）+ 白边 + 墨线 + 投影；出场时从自己中心弹出来。
// 对白只说一句短话，小气泡放在画面空处，尾巴指向说话的小潮。中英由 Loc 决定。
// 捏一下（编辑器：左键 / 空格）：还在打字就先显示完整，否则出下一格；6 格看完再捏一下，各格依次飞进城里，然后开始游戏。
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Monster.Game
{
    public class ComicIntro : MonoBehaviour
    {
        public Camera cam; public MonsterInput input;
        public System.Action onDone;
        public Transform Root => page;
        public Vector3 flyTo;   // 退场时各格飞去的位置（城市中心）

        /// <summary>一格：贴图、取景焦点（-0.5~0.5，画面往哪边偏）、对白（中文原文）、气泡中心和尾巴尖（格子外接框内的比例坐标，-0.5~0.5，y 向上）</summary>
        public class Spec { public string tex, line; public Vector2 focus, bubble, tail; }
        public static readonly Spec[] Panels =
        {
            new() { tex = "intro_1", focus = new(0.04f, 0), line = "醒醒，深潮！", bubble = new(-0.22f, 0.26f), tail = new(-0.06f, 0.02f) },
            new() { tex = "intro_2", focus = new(0, 0.04f), line = "他们偷走了\n你的光！", bubble = new(-0.14f, -0.33f), tail = new(-0.3f, -0.3f) },
            new() { tex = "intro_3", focus = new(0, 0), line = "封印裂了——\n醒过来！", bubble = new(-0.2f, 0.32f), tail = new(0.22f, -0.2f) },
            new() { tex = "intro_4", focus = new(-0.1f, -0.08f), line = "这座霓虹城，\n靠你的光点亮。", bubble = new(-0.17f, 0.14f), tail = new(-0.33f, -0.22f) },
            new() { tex = "intro_5", focus = new(-0.1f, -0.09f), line = "封印塔还在\n吸你的力量……", bubble = new(-0.05f, 0.33f), tail = new(-0.3f, -0.2f) },
            new() { tex = "intro_6", focus = new(0, 0.03f), line = "风暴只给一夜——\n把光拿回来！", bubble = new(0.1f, 0.3f), tail = new(0.33f, 0.12f) },
        };

        // ───────── 版式：整页 1.2 × 0.66（页面本地单位，整体再缩放），斜线切格 ─────────
        const float Gutter = 0.016f;
        /// <summary>直线：经过 a、b 两点</summary>
        struct Line { public Vector2 a, b; public Line(float ax, float ay, float bx, float by) { a = new(ax, ay); b = new(bx, by); } }
        static Vector2 Cross(Line p, Line q)
        {
            Vector2 r = p.b - p.a, s = q.b - q.a; float d = r.x * s.y - r.y * s.x;
            float t = ((q.a.x - p.a.x) * s.y - (q.a.y - p.a.y) * s.x) / d;
            return p.a + r * t;
        }
        /// <summary>把直线朝 toward 那一侧平移 d（用来让出格缝）</summary>
        static Line Shift(Line l, float d, Vector2 toward)
        {
            var dir = (l.b - l.a).normalized; var n = new Vector2(-dir.y, dir.x);
            if (Vector2.Dot(toward - l.a, n) < 0) n = -n;
            return new Line { a = l.a + n * d, b = l.b + n * d };
        }
        /// <summary>6 格的四个角（左上、右上、右下、左下）。外缘每格高低、斜度各不相同（错落）；内部格缝是斜的</summary>
        static Vector2[][] Layout()
        {
            var row = new Line(-0.6f, 0.045f, 0.6f, -0.075f);            // 上下两排的分界（斜）
            Line d1 = new(0.0f, 0.4f, -0.1f, -0.1f), d2 = new(0.24f, 0.4f, 0.33f, -0.1f);   // 上排竖缝
            Line d3 = new(-0.13f, 0.1f, -0.03f, -0.45f), d4 = new(0.19f, 0.1f, 0.12f, -0.45f);  // 下排竖缝（和上排错开）
            Line[] top = { new(-0.6f, 0.30f, 0f, 0.35f), new(-0.1f, 0.37f, 0.3f, 0.33f), new(0.2f, 0.31f, 0.6f, 0.36f) };
            Line[] bot = { new(-0.6f, -0.32f, 0f, -0.36f), new(-0.1f, -0.38f, 0.3f, -0.33f), new(0.2f, -0.31f, 0.6f, -0.355f) };
            Line L1 = new(-0.6f, 0.4f, -0.59f, -0.1f), L2 = new(-0.585f, 0.1f, -0.605f, -0.45f);
            Line R1 = new(0.6f, 0.4f, 0.59f, -0.1f), R2 = new(0.592f, 0.1f, 0.612f, -0.45f);
            float g = Gutter / 2;
            var up = new Vector2(0, 1); var dn = new Vector2(0, -1);
            Line rowU = Shift(row, g, new Vector2(0, 1)), rowD = Shift(row, g, new Vector2(0, -1));
            Vector2[] Q(Line t, Line r, Line b, Line l) => new[] { Cross(t, l), Cross(t, r), Cross(b, r), Cross(b, l) };
            return new[]
            {
                Q(top[0], Shift(d1, g, new Vector2(-1, 0)), rowU, L1),
                Q(top[1], Shift(d2, g, new Vector2(-1, 0.2f)), rowU, Shift(d1, g, new Vector2(1, 0.2f))),
                Q(top[2], R1, rowU, Shift(d2, g, new Vector2(1, 0.2f))),
                Q(rowD, Shift(d3, g, new Vector2(-1, -0.2f)), bot[0], L2),
                Q(rowD, Shift(d4, g, new Vector2(-1, -0.2f)), bot[1], Shift(d3, g, new Vector2(1, -0.2f))),
                Q(rowD, R2, bot[2], Shift(d4, g, new Vector2(1, -0.2f))),
            };
        }

        class Live { public int q; public Spec s; public Vector2[] poly; public Vector2 center; public Rect box; public Transform root, bubble; public TextMeshPro text; public float t = -1; public Vector3 basePos; public Material[] mats; }
        readonly List<Live> live = new();
        Transform page, hintRoot; int shown; float hintT; TextMeshPro hint;
        Material cardMat; TMP_FontAsset fontZh, fontEn;
        bool prevPinch, leaving; float leaveT;
        static readonly Color Ink = new(0.08f, 0.06f, 0.12f), Paper = new(0.97f, 0.95f, 0.9f);

        void Start()
        {
            if (!cam) cam = Camera.main;
            cardMat = new Material(Shader.Find("Monster/ComicCard")) { name = "comic" };
            artMat = new Material(Shader.Find("Monster/ComicNeon")) { name = "comicArt" };   // 11-11：画面加霓虹美漫处理（网点 / 错版 / 霓虹偏色），内容不变
            fontZh = Resources.Load<TMP_FontAsset>("Fonts/ComicZH SDF");
            fontEn = Resources.Load<TMP_FontAsset>("Fonts/ComicEN SDF");
            var h = cam.transform;
            var fwd = Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            page = new GameObject("ComicIntro").transform;
            pageFwd = fwd; PlacePage(1);
            page.localScale = Vector3.one * PageScale;
            var polys = Layout();
            for (int i = 0; i < Panels.Length; i++) live.Add(Build(Panels[i], polys[i], i));
            BuildGraffiti(polys);
            BuildHint();
            BuildStand();
            Loc.Changed += Relabel;
            shown = -1; hintT = 0; RelabelHint();   // 先显示“请站起来玩”，捏一下才开始漫画
        }

        // ───────── 位置：正对眼睛，转头超过 30° 才慢慢绕到面前（不死贴着头，看格子时可以转眼） ─────────
        // 站着看：页面中心比视线低 11cm（约 7°，最舒服的看书角度），页面朝眼睛微微仰起
        // 11-11：整页原来宽 1.38m（0.62m 处横向占 96°，比头显视野还宽，要转头扫着看）→ 缩到约 0.83m（横向约 67°，一眼看全），对白字单独放大 1.2 倍
        const float Dist = 0.62f, Drop = 0.08f, PageScale = 0.6f;   // 11-10 用户：漫画离得太远 → 0.92 拉到 0.62m（页面同时缩到 1.0，视角只比原来大约 15%，一眼看全）
        Vector3 pageFwd; bool following;
        void PlacePage(float k)
        {
            var h = cam.transform;
            var f = Vector3.ProjectOnPlane(h.forward, Vector3.up); if (f.sqrMagnitude < 1e-4f) f = pageFwd; f.Normalize();
            float ang = Vector3.Angle(pageFwd, f);
            if (ang > 30) following = true;
            if (following) { pageFwd = Vector3.Slerp(pageFwd, f, k).normalized; if (ang < 3) following = false; }
            var target = h.position + pageFwd * Dist + Vector3.down * Drop;
            page.position = Vector3.Lerp(page.position == Vector3.zero ? target : page.position, target, k);
            page.rotation = Quaternion.LookRotation(pageFwd * Dist + Vector3.down * Drop * 0.6f, Vector3.up);
        }

        // ───────── 开场第一张：站着玩的提醒（黄色旁白卡，在页面正中） ─────────
        Transform stand; TextMeshPro standText;
        void BuildStand()
        {
            stand = new GameObject("Stand").transform; stand.SetParent(page, false);
            stand.localPosition = new Vector3(0, 0.02f, -0.012f);
            stand.localScale = Vector3.one * 1.3f;
            stand.localRotation = Quaternion.Euler(0, 0, -1.5f);
            var box = new[] { new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, -0.5f), new Vector2(-0.5f, -0.5f) };
            Poly(stand, "Shadow", box, new Vector2(0.006f, -0.008f), 0.002f, new Color(0, 0, 0, 0.35f), null, 3195, null);
            Poly(stand, "Ink", box, Vector2.zero, 0.001f, Ink, null, 3196, null);
            Poly(stand, "Box", box, Vector2.zero, 0f, new Color(1f, 0.86f, 0.3f), null, 3197, null);
            standText = Text(stand, 3198);
            RelabelStand();
        }
        void RelabelStand()
        {
            if (!standText) return;
            standText.font = Loc.En ? fontEn : fontZh; standText.fontSize = Loc.En ? 30 : 32; standText.fontMaterial.renderQueue = 3198;
            standText.lineSpacing = Loc.En ? -10 : 4;
            standText.text = Loc.T("站着、坐着都能玩\n面前留出一块空地");
            if (Loc.En) standText.text = standText.text.ToUpperInvariant();
            var pref = standText.GetPreferredValues(standText.text, 9999, 0);
            standText.rectTransform.sizeDelta = new Vector2(pref.x + 1, pref.y + 1);
            float w = pref.x * 0.01f + 0.06f, h = pref.y * 0.01f + 0.05f;
            foreach (var n in new[] { "Shadow", "Box" }) stand.Find(n).localScale = new Vector3(w, h, 1);
            stand.Find("Ink").localScale = new Vector3(w + 0.006f, h + 0.006f, 1);
        }

        void OnDestroy() { Loc.Changed -= Relabel; if (page) Destroy(page.gameObject); }

        // ───────── 搭建 ─────────
        Live Build(Spec s, Vector2[] poly, int index)
        {
            var L = new Live { s = s, q = 3100 + index * 10 };
            var c = Vector2.zero; foreach (var p in poly) c += p; c /= poly.Length;
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (var p in poly) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y); }
            L.center = c; L.box = Rect.MinMaxRect(x0, y0, x1, y1);
            L.poly = new Vector2[poly.Length]; for (int i = 0; i < poly.Length; i++) L.poly[i] = poly[i] - c;   // 以格子中心为原点（弹出动画绕中心）
            L.root = new GameObject("Panel_" + s.tex).transform; L.root.SetParent(page, false);
            L.basePos = new Vector3(c.x, c.y, 0); L.root.localPosition = L.basePos;
            var tex = Resources.Load<Texture2D>("Comic/" + s.tex);
            var mats = new List<Material>
            {
                Poly(L.root, "Shadow", Grow(L.poly, 0.006f), new Vector2(0.006f, -0.008f), 0.003f, new Color(0, 0, 0, 0.35f), null, L.q, null),
                Poly(L.root, "Paper", Grow(L.poly, 0.006f), Vector2.zero, 0.002f, NeonEdge[index % NeonEdge.Length], null, L.q + 1, null),   // 11-11：格子外圈白纸边 → 霓虹色边
                Poly(L.root, "Frame", Grow(L.poly, 0.0022f), Vector2.zero, 0.001f, Ink, null, L.q + 2, null),
                Poly(L.root, "Art", L.poly, Vector2.zero, 0f, Color.white, tex, L.q + 3, CoverUV(L, tex, s.focus), artMat),
            };
            L.mats = mats.ToArray();
            L.bubble = new GameObject("Bubble").transform; L.bubble.SetParent(L.root, false);
            L.text = Text(L.bubble, L.q + 6);
            new GameObject("Shape").transform.SetParent(L.bubble, false);
            Relabel(L);
            L.root.gameObject.SetActive(false);
            return L;
        }

        static readonly Color[] NeonEdge = { new(1f, 0.32f, 0.72f), new(0.3f, 0.92f, 1f), new(1f, 0.86f, 0.22f), new(0.62f, 0.4f, 1f), new(1f, 0.32f, 0.72f), new(0.3f, 0.92f, 1f) };
        Material artMat;

        /// <summary>页面外缘的涂鸦（11-11）：两三团喷漆、几张贴纸（星 / 闪电 / 爪印 / 感叹号），压在页面后面和四角，不挡画面</summary>
        void BuildGraffiti(Vector2[][] polys)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (var pl in polys) foreach (var p in pl) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y); }
            var root = new GameObject("Graffiti").transform; root.SetParent(page, false);
            void Q(string tex, Vector2 at, float size, float rot, float z, int q, Color c)
            {
                var t = NeonKit.Tex(tex); if (!t) return;
                var m = new Material(cardMat) { renderQueue = q, color = c }; m.mainTexture = t;
                var go = new GameObject(tex); go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(at.x, at.y, z); go.transform.localRotation = Quaternion.Euler(0, 0, rot);
                go.transform.localScale = new Vector3(size, size * t.height / Mathf.Max(1, t.width), 1);
                go.AddComponent<MeshFilter>().sharedMesh = NeonKit.Quad();
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // 喷漆在页面后面（比所有格子先画），露出来的是页面外缘一圈
            Q("spray_pink", new Vector2(x0 + 0.03f, y1 - 0.02f), 0.24f, -12, 0.006f, 3090, new Color(1, 1, 1, 0.85f));
            Q("spray_pink", new Vector2(x1 - 0.02f, y0 + 0.03f), 0.2f, 160, 0.006f, 3090, new Color(0.55f, 1f, 1f, 0.75f));
            // 贴纸在最上层，只贴在页面四角外侧
            Q("doodle_star", new Vector2(x1 + 0.012f, y1 + 0.008f), 0.05f, 14, -0.004f, 3190, Color.white);
            Q("doodle_bolt", new Vector2(x0 - 0.014f, y0 + 0.04f), 0.045f, -18, -0.004f, 3190, Color.white);
            Q("claw_print", new Vector2(x1 + 0.018f, y0 - 0.006f), 0.07f, 24, -0.004f, 3190, new Color(1, 1, 1, 0.9f));
            Q("doodle_bang", new Vector2(x0 - 0.01f, y1 + 0.004f), 0.05f, -10, -0.004f, 3190, Color.white);
        }

        /// <summary>画“铺满裁切”进格子的外接框：短边对齐，长边居中裁掉，再按焦点平移</summary>
        static System.Func<Vector2, Vector2> CoverUV(Live L, Texture2D tex, Vector2 focus)
        {
            float ar = tex ? tex.width / (float)tex.height : 1, bw = L.box.width, bh = L.box.height;
            float sx, sy;   // 外接框占画面的比例
            if (bw / bh > ar) { sx = 1; sy = (bw / ar) > 0 ? bh / (bw / ar) : 1; } else { sy = 1; sx = bw / (bh * ar); }
            // 11-11：取景窗口不许超出图片。原图四周自带一圈黑框线，焦点偏移把窗口推到图外时，边上那条黑线被拉成一片黑（第 4 格左边和上下露黑边）
            // → 四周各裁掉 2%（去掉自带框线），焦点偏移限制在剩下的余量里
            const float Crop = 0.02f;
            sx *= 1 - 2 * Crop; sy *= 1 - 2 * Crop;
            float fx = Mathf.Clamp(focus.x, -(1 - 2 * Crop - sx) / 2, (1 - 2 * Crop - sx) / 2), fy = Mathf.Clamp(focus.y, -(1 - 2 * Crop - sy) / 2, (1 - 2 * Crop - sy) / 2);
            var bc = L.box.center - L.center;
            return p => new Vector2(0.5f + fx + (p.x - bc.x) / bw * sx, 0.5f + fy + (p.y - bc.y) / bh * sy);
        }

        /// <summary>凸多边形往外扩 d：每条边沿外法线平移后与相邻边求交</summary>
        static Vector2[] Grow(Vector2[] p, float d)
        {
            int n = p.Length; var lines = new Line[n];
            for (int i = 0; i < n; i++) { var a = p[i]; var b = p[(i + 1) % n]; var dir = (b - a).normalized; var nm = new Vector2(dir.y, -dir.x); if (Vector2.Dot(nm, (a + b) * 0.5f) < 0) nm = -nm; lines[i] = new Line { a = a + nm * d, b = b + nm * d }; }
            var r = new Vector2[n];
            for (int i = 0; i < n; i++) r[i] = Cross(lines[(i + n - 1) % n], lines[i]);
            return r;
        }

        Material Poly(Transform parent, string name, Vector2[] poly, Vector2 off, float z, Color c, Texture tex, int queue, System.Func<Vector2, Vector2> uv, Material baseMat = null)
        {
            var v = new List<Vector3>(); var u = new List<Vector2>(); var t = new List<int>();
            foreach (var p in poly) { v.Add(new Vector3(p.x + off.x, p.y + off.y, z)); u.Add(uv != null ? uv(p) : Vector2.zero); }
            for (int i = 1; i + 1 < poly.Length; i++) { t.Add(0); t.Add(i + 1); t.Add(i); }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetUVs(0, u); m.SetTriangles(t, 0); m.RecalculateBounds();
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mat = new Material(baseMat ? baseMat : cardMat) { renderQueue = queue, color = c }; if (tex) mat.mainTexture = tex;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return mat;
        }

        TextMeshPro Text(Transform parent, int queue)
        {
            var go = new GameObject("Text"); go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            t.transform.localPosition = new Vector3(0, 0, -0.004f);
            t.transform.localScale = Vector3.one * 0.01f;   // 文字框按厘米算
            t.alignment = TextAlignmentOptions.Center; t.color = Ink; t.enableWordWrapping = false;
            t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            t.fontMaterial.renderQueue = queue;
            return t;
        }

        void Relabel() { foreach (var L in live) Relabel(L); RelabelHint(); RelabelStand(); }

        /// <summary>换语言：换字体、重排文字，按文字实际大小重做气泡（气泡只比字大一圈）</summary>
        void Relabel(Live L)
        {
            var t = L.text;
            t.font = Loc.En ? fontEn : fontZh; t.fontMaterial.renderQueue = L.q + 6;
            string line = L.s.line;
            t.text = Loc.En ? Loc.T(line).ToUpperInvariant() : line;
            t.fontSize = Loc.En ? 23 : 25; t.lineSpacing = Loc.En ? -18 : -6;
            var pref = t.GetPreferredValues(t.text, 9999, 0);
            t.rectTransform.sizeDelta = new Vector2(pref.x + 1, pref.y + 1);
            // 11-11 用户：注意排版边距 —— 气泡四周各留约一个半字宽的一半（原来左右 1.5cm、上下 1.1cm，气泡是圆角的，字的四个角几乎顶着边线）
            // 气泡整体留在格子里、离格边至少 1.4cm（原来第 3 格的气泡压过左边框、第 5 格的气泡和格子一样宽）；格子太窄放不下就把字缩小
            const float PadW = 0.052f, PadH = 0.04f, EdgeGap = 0.014f;
            float maxW = L.box.width - 2 * EdgeGap - PadW;
            if (pref.x * 0.01f > maxW) { t.fontSize *= maxW / (pref.x * 0.01f); pref = t.GetPreferredValues(t.text, 9999, 0); t.rectTransform.sizeDelta = new Vector2(pref.x + 1, pref.y + 1); }
            float bw = pref.x * 0.01f + PadW, bh = pref.y * 0.01f + PadH;
            var off = new Vector2(L.s.bubble.x * L.box.width, L.s.bubble.y * L.box.height);
            float lx = Mathf.Max(0, L.box.width / 2 - bw / 2 - EdgeGap), ly = Mathf.Max(0, L.box.height / 2 - bh / 2 - EdgeGap);
            off = new Vector2(Mathf.Clamp(off.x, -lx, lx), Mathf.Clamp(off.y, -ly, ly));
            var center = L.box.center - L.center + off;
            L.bubble.localPosition = new Vector3(center.x, center.y, -0.006f);
            var tail = L.box.center - L.center + new Vector2(L.s.tail.x * L.box.width, L.s.tail.y * L.box.height) - center;
            var shape = L.bubble.Find("Shape");
            foreach (Transform c in shape) Destroy(c.gameObject);
            BubbleMesh(shape, "Ink", bw / 2 + 0.0025f, bh / 2 + 0.0025f, tail, 0.003f, 0.001f, Ink, L.q + 4);
            BubbleMesh(shape, "Fill", bw / 2, bh / 2, tail, 0f, 0f, Color.white, L.q + 5);
        }

        /// <summary>对白气泡：圆润的超椭圆 + 指向说话者的尖尾巴（尾巴不长，只伸到说话者方向的一小段）</summary>
        void BubbleMesh(Transform parent, string name, float a, float b, Vector2 tail, float grow, float z, Color c, int queue)
        {
            const int N = 48; const float P = 2.6f;
            float ta = Mathf.Atan2(tail.y, tail.x);
            float rim = Mathf.Abs(Mathf.Cos(ta)) * a + Mathf.Abs(Mathf.Sin(ta)) * b;
            float tailLen = Mathf.Clamp(tail.magnitude * 0.7f, rim + 0.012f, rim + 0.035f);
            var pts = new List<Vector3>();
            for (int i = 0; i < N; i++)
            {
                float ang = ta + Mathf.PI * 2 * i / N;
                if (i == 0) { var apex = new Vector2(Mathf.Cos(ta), Mathf.Sin(ta)) * (tailLen + grow * 2); pts.Add(new Vector3(apex.x, apex.y, z)); continue; }
                float d = Mathf.DeltaAngle(ta * Mathf.Rad2Deg, ang * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                if (Mathf.Abs(d) < 0.22f) continue;   // 尾巴根部
                float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                pts.Add(new Vector3(a * Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), 2 / P), b * Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 2 / P), z));
            }
            var v = new List<Vector3> { new(0, 0, z) }; v.AddRange(pts);
            var tri = new List<int>();
            for (int i = 0; i < pts.Count; i++) { int i0 = 1 + i, i1 = 1 + (i + 1) % pts.Count; tri.Add(0); tri.Add(i1); tri.Add(i0); }
            var m = new Mesh { name = "bubble" }; m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateBounds();
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = new Material(cardMat) { color = c, renderQueue = queue }; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // 旁白框（黄底墨边）：捏一下继续 / 开始
        void BuildHint()
        {
            hintRoot = new GameObject("Hint").transform; hintRoot.SetParent(page, false);
            hintRoot.localPosition = new Vector3(0.44f, -0.4f, -0.012f);
            hintRoot.localRotation = Quaternion.Euler(0, 0, 1.5f);
            var box = new[] { new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, -0.5f), new Vector2(-0.5f, -0.5f) };
            Poly(hintRoot, "Ink", box, Vector2.zero, 0.001f, Ink, null, 3198, null);
            Poly(hintRoot, "Box", box, Vector2.zero, 0f, new Color(1f, 0.86f, 0.3f), null, 3199, null);
            hint = Text(hintRoot, 3200);
            RelabelHint();
        }
        void RelabelHint()
        {
            if (!hint) return;
            hint.font = Loc.En ? fontEn : fontZh; hint.fontSize = Loc.En ? 22 : 24; hint.fontMaterial.renderQueue = 3200;
            hint.text = (shown >= Panels.Length - 1 || shown < 0 ? Loc.T("捏一下开始") : Loc.T("捏一下继续")) + "  »";
            hintRoot.localPosition = shown < 0 ? new Vector3(0, -0.1f, -0.014f) : new Vector3(0.44f, -0.4f, -0.012f);
            hint.color = Ink;
            var pref = hint.GetPreferredValues(hint.text, 9999, 0);
            hint.rectTransform.sizeDelta = new Vector2(pref.x + 1, pref.y + 1);
            float w = pref.x * 0.01f + 0.03f, h = pref.y * 0.01f + 0.018f;
            hintRoot.Find("Ink").localScale = new Vector3(w + 0.005f, h + 0.005f, 1);
            hintRoot.Find("Box").localScale = new Vector3(w, h, 1);
        }

        // ───────── 流程 ─────────
        void Show(int i)
        {
            shown = i;
            var L = live[i]; L.t = 0; L.root.gameObject.SetActive(true);
            MonsterAudio.Play("ui_page");
            hintT = 0; RelabelHint();
        }

        readonly Pointer click = new();
        bool PinchDown()
        {
            bool down = false;
            // 和纸面界面用同一套“明确的捏合”（见 Pointer.Update）：手放松时两指晃来晃去不会把漫画翻过去
            click.input = input; click.cam = cam; click.Update(); down = click.down;
            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame) down = true;
            return down;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!leaving) PlacePage(1 - Mathf.Exp(-dt * 3));
            foreach (var L in live) if (L.t >= 0) Animate(L, L.t += dt);
            if (leaving) { Leave(dt); return; }
            hintT += dt;
            bool ready = shown < 0 ? hintT > 0.8f : !Typing(live[shown]) && live[shown].t > 0.6f;
            if (hintRoot) { hintRoot.gameObject.SetActive(ready); hintRoot.localScale = Vector3.one * (1 + 0.04f * Mathf.Sin(hintT * 5)); }
            if (PinchDown()) Advance();
        }

        /// <summary>捏一下：还在打字就先显示完整，否则出下一格；全部看完就退场</summary>
        public void Advance()
        {
            if (leaving) return;
            if (shown < 0) { if (hintT < 0.5f) return; stand.gameObject.SetActive(false); Show(0); return; }   // 站立提醒 → 第 1 格
            var cur = live[shown];
            if (Typing(cur)) { cur.t = 99; return; }
            if (shown + 1 < live.Count) Show(shown + 1);
            else { leaving = true; leaveT = 0; if (hintRoot) hintRoot.gameObject.SetActive(false); }
        }
        /// <summary>截图验收用：直接显示到第 n 格（全部动画走完）</summary>
        public void ShowAll(int n) { if (stand) stand.gameObject.SetActive(false); for (int i = 0; i <= n && i < live.Count; i++) { Show(i); live[i].t = 99; } }

        bool Typing(Live L) => L.text.maxVisibleCharacters < L.text.text.Length;

        static float Back(float x) { x = Mathf.Clamp01(x); const float c = 1.9f; return 1 + (c + 1) * Mathf.Pow(x - 1, 3) + c * Mathf.Pow(x - 1, 2); }

        void Animate(Live L, float t)
        {
            float e = Back(t / 0.38f);
            L.root.localScale = Vector3.one * Mathf.LerpUnclamped(0.55f, 1f, e);
            L.root.localRotation = Quaternion.Euler(0, 0, (1 - Mathf.Clamp01(e)) * 7);
            L.root.localPosition = L.basePos + new Vector3(0, 0, -0.03f * (1 - Mathf.Clamp01(t / 0.25f)));
            float a = Mathf.Clamp01(t / 0.15f);
            for (int i = 0; i < L.mats.Length; i++) { var c = L.mats[i].color; c.a = (i == 0 ? 0.35f : 1f) * a; L.mats[i].color = c; }
            float bt = t - 0.28f;
            L.bubble.localScale = Vector3.one * (bt <= 0 ? 0.001f : Mathf.LerpUnclamped(0.3f, 1f, Back(bt / 0.25f)));
            L.text.maxVisibleCharacters = bt <= 0.15f ? 0 : Mathf.Min(L.text.text.Length, Mathf.FloorToInt((bt - 0.15f) * (Loc.En ? 40 : 18)));
        }

        /// <summary>退场：一格一格缩小、飞向城市中心，最后一格到了就开始游戏</summary>
        // 退场（11-09 用户：各格飞进城市的收法很怪、不舒服）：原地淡出 —— 每格轻轻往下沉 2cm、缩到 94%、变透明，
        // 格与格之间只差 0.04 秒；没有大幅位移，视野里不会有大块画面掠过
        void Leave(float dt)
        {
            leaveT += dt;
            bool all = true;
            for (int i = 0; i < live.Count; i++)
            {
                var L = live[i];
                float k = Mathf.Clamp01((leaveT - i * 0.04f) / 0.45f);
                if (k < 1) all = false;
                float e = k * k * (3 - 2 * k);
                L.root.localPosition = L.basePos + new Vector3(0, -0.02f * e, 0.01f * e);
                L.root.localScale = Vector3.one * Mathf.Lerp(1, 0.94f, e);
                for (int m = 0; m < L.mats.Length; m++) { var c = L.mats[m].color; c.a = (m == 0 ? 0.35f : 1f) * (1 - e); L.mats[m].color = c; }
                if (L.text) L.text.alpha = 1 - e;
                if (L.bubble)
                {
                    L.bubble.localScale = Vector3.one * Mathf.Lerp(1, 0.9f, e);
                    foreach (var r in L.bubble.GetComponentsInChildren<MeshRenderer>())
                        if (!r.GetComponent<TMPro.TMP_Text>() && r.sharedMaterial && r.sharedMaterial.HasProperty("_Color")) { var c = r.sharedMaterial.color; c.a = Mathf.Min(c.a, 1 - e); r.sharedMaterial.color = c; }
                }
            }
            if (!all) return;
            MonsterAudio.Music("music_game"); MonsterAudio.Ambience(true);
            onDone?.Invoke();
            Destroy(this);
        }
    }
}
