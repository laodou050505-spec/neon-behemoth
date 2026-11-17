// 场景实物界面（11-07 用户：UI 忌 AI 味，做成场景里的实物）：霓虹按键牌、黄色旁白卡、标题招牌、结算招牌。
// 操作：看着它（眼动，没有眼动就是头的朝向；编辑器是鼠标）+ 任意一只手捏一下。看着的那块会稍微放大、灯管变亮，旁边有细细的选中描边。
// 所有牌子都是“立在空中的实物”：深色铁皮底 + 墨线边 + 霓虹字，放在眼前约 1m、比视线略低，一直正对玩家。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Monster.Game
{
    public static class NeonKit
    {
        public static readonly Color Ink = new(0.08f, 0.06f, 0.12f), Board = new(0.13f, 0.11f, 0.22f), Pink = new(1f, 0.36f, 0.72f), Cyan = new(0.35f, 0.95f, 1f), Gold = new(1f, 0.86f, 0.3f), Paper = new(0.97f, 0.95f, 0.9f);
        static Material baseMat;
        public static Material Mat(Color c, int q)
        {
            if (!baseMat) baseMat = new Material(Shader.Find("Monster/ComicCard")) { name = "ui" };
            return new Material(baseMat) { color = c, renderQueue = q };
        }
        public static TMP_FontAsset Font => Resources.Load<TMP_FontAsset>(Loc.En ? "Fonts/ComicEN SDF" : "Fonts/ComicZH SDF");

        /// <summary>圆角矩形（中心在原点，xy 平面，面朝 −Z）</summary>
        public static Mesh RoundRect(float w, float h, float r, int seg = 6)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            var v = new List<Vector3> { Vector3.zero };
            var corners = new[] { new Vector2(w / 2 - r, h / 2 - r), new Vector2(-w / 2 + r, h / 2 - r), new Vector2(-w / 2 + r, -h / 2 + r), new Vector2(w / 2 - r, -h / 2 + r) };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = (c * 90 + 90f * i / seg) * Mathf.Deg2Rad;
                    v.Add(new Vector3(corners[c].x + Mathf.Cos(a) * r, corners[c].y + Mathf.Sin(a) * r, 0));
                }
            var t = new List<int>(); int n = v.Count - 1;
            for (int i = 1; i <= n; i++) { int j = i % n + 1; t.Add(0); t.Add(j); t.Add(i); }
            var m = new Mesh { name = "rrect" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }

        public static Transform Shape(Transform parent, string name, Mesh mesh, Vector3 pos, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        public static TextMeshPro Text(Transform parent, Vector3 pos, float size, Color c, int q, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject("Text"); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = Vector3.one * 0.01f;
            var t = go.AddComponent<TextMeshPro>(); t.alignment = align; t.enableWordWrapping = false; t.color = c; t.fontSize = size;
            t.font = Font; t.fontMaterial.renderQueue = q;
            t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return t;
        }
        // ───────── 霓虹涂鸦面板（11-11 用户：整体要霓虹动漫 + 美式卡通涂鸦）─────────
        // 墨黑描边 + 深紫黑底板 + 两圈霓虹灯管边框（外粉内青，真的发光管）+ 一角喷漆底纹 + 两三张涂鸦贴纸 + 一条胶带。
        // 面板中心在 (0, y0 + h/2)，xy 平面、面朝 −Z（和 RoundRect 一样）。q = 底板的渲染顺序（字要比它大）
        public static Transform GraffitiPanel(Transform p, float w, float h, float y0, int q, int seed = 0, float glow = 1f, bool clean = false)
        {
            var root = new GameObject("GraffitiPanel").transform; root.SetParent(p, false);
            float cy = y0 + h / 2;
            Shape(root, "Ink", RoundRect(w + 0.012f, h + 0.012f, 0.014f), new Vector3(0, cy, 0.002f), Mat(Ink, q - 2));
            Shape(root, "Board", RoundRect(w, h, 0.011f), new Vector3(0, cy, 0), Mat(new Color(0.07f, 0.05f, 0.13f), q - 1));
            // 喷漆底纹（粉色喷漆贴在左上，半透明，像被人喷过一道）
            var spray = Tex("spray_pink");
            if (spray && !clean) { var sm = Mat(new Color(1, 1, 1, 0.22f), q); sm.mainTexture = spray; var sq = Shape(root, "Spray", Quad(), new Vector3(w * 0.28f, cy - h * 0.18f, -0.0005f), sm); sq.localScale = new Vector3(w * 0.32f, h * 0.42f, 1); sq.localRotation = Quaternion.Euler(0, 0, -6); }
            // 两圈霓虹灯管：外粉内青（发光材质 + 一圈深色管座，像真的灯管）
            float tk = clean ? 1.6f : 1f;   // 大招牌灯管加粗
            Tube(root, RectPath(w - 0.006f * tk, h - 0.006f * tk, 0.009f * tk, cy), 0.0016f * tk, CityMaterials.Glow("#ff4fb8", 3.2f * glow), -0.003f);
            Tube(root, RectPath(w - 0.02f * tk, h - 0.02f * tk, 0.006f * tk, cy), 0.0008f * tk, CityMaterials.Glow("#4ff3ff", 2.2f * glow), -0.0022f);
            // 贴纸：右下一颗星、左下一道闪电；左上一条胶带
            var rnd = new System.Random(seed);
            void Sticker(string tex, Vector3 pos, float size, float rot)
            {
                var t = Tex(tex); if (!t) return;
                var m = Mat(Color.white, q + 1); m.mainTexture = t;
                var sq = Shape(root, tex, Quad(), pos, m); sq.localScale = new Vector3(size, size * t.height / Mathf.Max(1, t.width), 1); sq.localRotation = Quaternion.Euler(0, 0, rot);
            }
            if (clean) return root;   // 11-11 用户：面板别小家子气 —— 大招牌不贴零碎贴纸 / 胶带
            Sticker("doodle_star", new Vector3(w / 2 - 0.012f, y0 + 0.012f, -0.003f), 0.026f, (float)rnd.NextDouble() * 30 - 15);
            Sticker("doodle_bolt", new Vector3(-w / 2 + 0.01f, y0 + 0.014f, -0.003f), 0.022f, -20);
            Sticker("tape_a", new Vector3(-w / 2 + 0.012f, y0 + h - 0.004f, -0.004f), 0.034f, 38);
            return root;
        }
        static readonly Dictionary<string, Texture2D> texCache = new();
        public static Texture2D Tex(string name) { if (!texCache.TryGetValue(name, out var t) || !t) texCache[name] = t = Resources.Load<Texture2D>("UI/" + name); return t; }
        static Mesh quad;
        public static Mesh Quad()
        {
            if (quad) return quad;
            quad = new Mesh { name = "uiquad" };
            quad.SetVertices(new List<Vector3> { new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0.5f, 0.5f, 0), new(-0.5f, 0.5f, 0) });
            quad.SetUVs(0, new List<Vector2> { new(0, 0), new(1, 0), new(1, 1), new(0, 1) });
            quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0); quad.RecalculateBounds();
            return quad;
        }
        /// <summary>圆角矩形的一圈路径点（闭合，首尾不重复）</summary>
        public static List<Vector3> RectPath(float w, float h, float r, float cy, int seg = 5)
        {
            var pts = new List<Vector3>();
            var corners = new[] { new Vector2(w / 2 - r, h / 2 - r), new Vector2(-w / 2 + r, h / 2 - r), new Vector2(-w / 2 + r, -h / 2 + r), new Vector2(w / 2 - r, -h / 2 + r) };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = (c * 90 + 90f * i / seg) * Mathf.Deg2Rad;
                    pts.Add(new Vector3(corners[c].x + Mathf.Cos(a) * r, cy + corners[c].y + Mathf.Sin(a) * r, 0));
                }
            return pts;
        }
        /// <summary>沿闭合路径的一根灯管（6 边形截面），z = 离板面的距离（负 = 朝玩家）</summary>
        public static Transform Tube(Transform p, List<Vector3> pts, float r, Material m, float z)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>(); const int S = 6; int N = pts.Count;
            for (int i = 0; i < N; i++)
            {
                var d = (pts[(i + 1) % N] - pts[(i - 1 + N) % N]).normalized; var side = Vector3.Cross(d, Vector3.forward).normalized;
                for (int j = 0; j < S; j++)
                {
                    float a = j * Mathf.PI * 2 / S; var o = side * Mathf.Cos(a) + Vector3.back * Mathf.Sin(a);
                    v.Add(pts[i] + new Vector3(0, 0, z) + o * r); n.Add(o);
                }
            }
            for (int i = 0; i < N; i++) for (int j = 0; j < S; j++)
                {
                    int a = i * S + j, b = ((i + 1) % N) * S + j, c = ((i + 1) % N) * S + (j + 1) % S, d2 = i * S + (j + 1) % S;
                    t.AddRange(new[] { a, b, c, a, c, d2 });
                }
            var mesh = new Mesh { name = "neontube" }; mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
            return Shape(p, "Tube", mesh, Vector3.zero, m);
        }

        public static void SetText(TextMeshPro t, string zh, int q)
        {
            t.font = Font; t.fontMaterial.renderQueue = q;
            string s = Loc.T(zh); t.text = Loc.En ? s.ToUpperInvariant() : s;
            var p = t.GetPreferredValues(t.text, 9999, 0); t.rectTransform.sizeDelta = new Vector2(p.x + 1, p.y + 1);
        }
    }

    /// <summary>一块霓虹按键牌：铁皮底 + 霓虹字 + 选中描边；看着 + 捏一下触发 onPress</summary>
    public class NeonButton
    {
        public Transform root; public string label; public Action onPress; public bool enabled = true;
        TextMeshPro text; Material board, edge, glowEdge; Color color; float w, h, hover, press; int q;
        public NeonButton(Transform parent, string label, Vector3 pos, float w, float h, Color color, int q, Action onPress)
        {
            this.label = label; this.w = w; this.h = h; this.color = color; this.q = q; this.onPress = onPress;
            root = new GameObject("Btn_" + label).transform; root.SetParent(parent, false); root.localPosition = pos;
            glowEdge = NeonKit.Mat(color * 1.4f, q);
            NeonKit.Shape(root, "Glow", NeonKit.RoundRect(w + 0.012f, h + 0.012f, 0.014f), new Vector3(0, 0, 0.002f), glowEdge);
            edge = NeonKit.Mat(NeonKit.Ink, q + 1);
            NeonKit.Shape(root, "Edge", NeonKit.RoundRect(w + 0.006f, h + 0.006f, 0.011f), new Vector3(0, 0, 0.001f), edge);
            board = NeonKit.Mat(NeonKit.Board, q + 2);
            NeonKit.Shape(root, "Board", NeonKit.RoundRect(w, h, 0.008f), Vector3.zero, board);
            text = NeonKit.Text(root, new Vector3(0, 0, -0.002f), 30, color, q + 3);
            Relabel();
        }
        public void Relabel()
        {
            text.fontSize = 30; NeonKit.SetText(text, label, q + 3);
            // 字太长就缩小，保证在牌子里
            float max = (w - 0.03f) * 100, pw = text.GetPreferredValues(text.text, 9999, 0).x;
            text.fontSize = pw > max ? 30 * max / pw : 30;
            NeonKit.SetText(text, label, q + 3);
        }
        public bool Hit(Ray r, out float t)
        {
            t = 0;
            var pl = new Plane(-root.forward, root.position);
            if (!pl.Raycast(r, out t)) return false;
            var p = root.InverseTransformPoint(r.GetPoint(t));
            return Mathf.Abs(p.x) < w / 2 + 0.01f && Mathf.Abs(p.y) < h / 2 + 0.01f;
        }
        public void Tick(float dt, bool hot)
        {
            hover = Mathf.MoveTowards(hover, hot && enabled ? 1 : 0, dt * 8);
            press = Mathf.Max(0, press - dt * 5);
            root.localScale = Vector3.one * (1 + 0.07f * hover - 0.06f * press);
            var c = enabled ? color : color * 0.35f; c.a = 1;
            text.color = Color.Lerp(Color.Lerp(c, Color.white, 0.15f), Color.Lerp(c, Color.white, 0.5f), hover);
            var g = color; g.a = hover; glowEdge.color = g;
        }
        public void Press() { press = 1; onPress?.Invoke(); }
    }

    /// <summary>“看着 + 捏一下”的输入：每帧给出瞄准射线和是否刚捏下（头显：眼动 / 头向 + 任一手捏合；编辑器：鼠标射线 + 左键）</summary>
    public class Pointer
    {
        public MonsterInput input; public Camera cam; bool prev;
        readonly float[] openT = new float[2], closeT = new float[2]; readonly bool[] armed = new bool[2];
        public Ray ray; public bool down;
        public bool headGaze;   // 眼动拿不到、退回头部朝向：界面要画一个小光标
        public readonly List<Vector3> tips = new();   // 两只手的食指尖（手指直接点纸面用）
        public void Update()
        {
            down = false; tips.Clear(); headGaze = false;
            if (input && input.Active)
            {
                var gi = input.Read();
                ray = input.Gaze; headGaze = !input.EyeTracked;
                // 11-17 用户真机：标题页有时没点也没捏，只是眼睛看着就选中了。原因：这里用的是玩法那套很松的“捏”（拇指食指 4cm 内就算，松到 5.5cm 才放开，
                // 为的是好抓东西）—— 手放松垂着的时候两指本来就在 3～5cm 之间晃，一会儿算捏、一会儿不算，每跳一次就当成点了一下，点在眼睛正看着的那一项上。
                // 界面的“点一下”改成要一个明确的捏合动作：手先张开（两指离开 4cm 以上、保持 0.12 秒）→ 0.8 秒内捏到 2cm 以内，才算一次；
                // 手刚被追踪到、一直半捏着、慢慢合拢的都不算
                for (int i = 0; i < 2; i++)
                {
                    var h = i == 0 ? input.left : input.right;
                    if (!h.present) { openT[i] = 0; armed[i] = false; continue; }
                    if (h.pinchD > 0.04f) { openT[i] += Time.deltaTime; closeT[i] = 0; if (openT[i] > 0.12f) armed[i] = true; }
                    else { openT[i] = 0; closeT[i] += Time.deltaTime; if (closeT[i] > 0.8f) armed[i] = false; }
                    if (armed[i] && h.pinchD < 0.02f) { armed[i] = false; down = true; }
                    if (h.nPts > 2) tips.Add(h.pts[2]);
                }
                return;
            }
            var ms = Mouse.current;
            ray = ms != null ? cam.ScreenPointToRay(ms.position.ReadValue()) : new Ray(cam.transform.position, cam.transform.forward);
            down = ms != null && ms.leftButton.wasPressedThisFrame;
        }
    }

    /// <summary>一组按键：处理悬停 / 按下（只有看着的那块响应）</summary>
    public class ButtonGroup
    {
        public readonly List<NeonButton> list = new();
        public NeonButton Hot { get; private set; }
        public void Tick(float dt, Pointer p)
        {
            Hot = null; float best = float.MaxValue;
            foreach (var b in list) if (b.root.gameObject.activeInHierarchy && b.enabled && b.Hit(p.ray, out var t) && t < best) { best = t; Hot = b; }
            foreach (var b in list) b.Tick(dt, b == Hot);
            if (p.down && Hot != null) Hot.Press();
        }
        public void Relabel() { foreach (var b in list) b.Relabel(); }
    }

    /// <summary>摆在玩家眼前：距离 d、比视线低 drop，正对玩家；转头超过 30° 才慢慢跟过来</summary>
    public class Follow
    {
        public Transform t; public Camera cam; public float d = 1.05f, drop = 0.08f, minY = -1; Vector3 fwd; bool following;   // minY：中心最低高度（不插进城市）
        public void Place(float k)
        {
            var h = cam.transform;
            var f = Vector3.ProjectOnPlane(h.forward, Vector3.up); if (f.sqrMagnitude < 1e-4f) f = fwd == Vector3.zero ? Vector3.forward : fwd; f.Normalize();
            if (fwd == Vector3.zero) { fwd = f; k = 1; }
            float ang = Vector3.Angle(fwd, f);
            if (ang > 30) following = true;
            if (following) { fwd = Vector3.Slerp(fwd, f, k).normalized; if (ang < 3) following = false; }
            var target = h.position + fwd * d + Vector3.down * drop;
            if (minY > 0) target.y = Mathf.Max(target.y, Mathf.Min(minY, h.position.y + 0.1f));
            t.position = k >= 1 ? target : Vector3.Lerp(t.position, target, k);
            t.rotation = Quaternion.LookRotation(t.position - h.position - Vector3.up * (t.position.y - h.position.y) * 0.6f, Vector3.up);
        }
    }
}
