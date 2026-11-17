// 告示纸界面（11-08 用户：UI 太规整、AI 味重，参考给的涂鸦式界面“更疯一点”）。
// 设定：每块界面都是“霓虹动力集团”冷冰冰的官方告示纸，被巨兽和小潮用喷漆、马克笔涂改了 —— 界面本身就在讲“你在反抗集团”。
//   公文层：米白复印纸（撕边、折角、条码、印章）、黑色抬头条、思源黑体粗的正经排版；
//   涂鸦层：粉 / 青 / 黄马克笔手写大字（站酷庆科黄油体 / Permanent Marker）、龙藏体批注、贴纸涂鸦、爪痕、遮蔽胶带、黑色涂黑条；
//   选中：一条斜贴的粉色喷漆带从左往右刷出来，字变白；按下：纸一抖、爪痕划过。
//   故障感：偶尔几条粉 / 青色的横向切片在纸边闪一下（错位的复印机条纹），大字偶尔分出青 / 粉两层错位。
// 全部是 MR 里悬在眼前的纸片（不出现任何游戏场景）。看着 + 捏一下操作（Pointer）。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Monster.Game
{
    public static class Ink
    {
        public static readonly Color Pink = new(1f, 0.18f, 0.54f), Cyan = new(0.18f, 0.9f, 1f), Yellow = new(1f, 0.82f, 0.23f), Black = new(0.07f, 0.06f, 0.08f), Paper = new(0.95f, 0.94f, 0.9f), Gray = new(0.42f, 0.42f, 0.45f);
        public enum Face { Corp, Graffiti, Note }
        public static TMP_FontAsset Font(Face f) => Resources.Load<TMP_FontAsset>(f switch
        {
            Face.Corp => "Fonts/Corp SDF",
            Face.Graffiti => Loc.En ? "Fonts/MarkerEN SDF" : "Fonts/GraffitiZH SDF",
            _ => Loc.En ? "Fonts/MarkerEN SDF" : "Fonts/NoteZH SDF",
        });
    }

    /// <summary>一张告示纸 / 一组贴纸的搭建器：所有元素都是带透明的面片，按顺序叠（renderQueue 递增），本地 xy 平面、面朝 −Z</summary>
    public class Sheet
    {
        public readonly Transform root; int q;
        static Material baseMat;
        readonly List<(Material m, float a)> fades = new();
        public Sheet(Transform parent, string name, int queue)
        {
            root = new GameObject(name).transform; root.SetParent(parent, false); q = queue;
            if (!baseMat) baseMat = new Material(Shader.Find("Monster/ComicCard")) { name = "notice" };
        }
        float z;   // 越后加的越靠前一点（防止同面闪烁），配合 renderQueue
        /// <summary>贴一张图：pos = 中心，h = 高（宽按图比例），rot = 绕 z 的角度</summary>
        public Transform Img(string tex, Vector2 pos, float h, float rot = 0, Color? tint = null, float w = -1)
        {
            var t = Resources.Load<Texture2D>("UI/" + tex) ?? Resources.Load<Texture2D>(tex);
            float aspect = t ? t.width / (float)t.height : 1;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.name = tex; go.transform.SetParent(root, false);
            z -= 0.0004f;
            go.transform.localPosition = new Vector3(pos.x, pos.y, z); go.transform.localRotation = Quaternion.Euler(0, 0, rot);
            go.transform.localScale = new Vector3(w > 0 ? w : h * aspect, h, 1);
            var m = new Material(baseMat) { mainTexture = t, color = tint ?? Color.white, renderQueue = q++ };
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            fades.Add((m, m.color.a));
            return go.transform;
        }
        /// <summary>纯色块（黑色涂黑条 / 抬头条 / 故障切片）</summary>
        public Transform Rect(Vector2 pos, Vector2 size, Color c, float rot = 0)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.name = "rect"; go.transform.SetParent(root, false);
            z -= 0.0004f;
            go.transform.localPosition = new Vector3(pos.x, pos.y, z); go.transform.localRotation = Quaternion.Euler(0, 0, rot); go.transform.localScale = new Vector3(size.x, size.y, 1);
            var m = new Material(baseMat) { color = c, renderQueue = q++ };
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fades.Add((m, c.a));
            return go.transform;
        }
        /// <summary>文字：pivot 左中（align Left）或中心；size = 字高大约多少米</summary>
        public TextMeshPro Text(string zh, Ink.Face face, Vector2 pos, float size, Color c, float rot = 0, TextAlignmentOptions align = TextAlignmentOptions.Left, float maxW = 0)
        {
            var go = new GameObject("T"); go.transform.SetParent(root, false);
            z -= 0.0004f;
            go.transform.localPosition = new Vector3(pos.x, pos.y, z); go.transform.localRotation = Quaternion.Euler(0, 0, rot); go.transform.localScale = Vector3.one * 0.01f;
            var t = go.AddComponent<TextMeshPro>(); t.alignment = align; t.enableWordWrapping = maxW > 0; t.color = c; t.fontSize = size * 900;   // TMP 字号 ≈ 行高（毫米）：缩放 0.01 时字号 21 ≈ 2cm 行高
            t.rectTransform.pivot = align == TextAlignmentOptions.Left || align == TextAlignmentOptions.TopLeft ? new Vector2(0, 0.5f) : align == TextAlignmentOptions.Right ? new Vector2(1, 0.5f) : new Vector2(0.5f, 0.5f);
            t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var L = new Label { t = t, zh = zh, face = face, maxW = maxW, q = q++ };
            labels.Add(L); L.Apply();
            return t;
        }
        public class Label
        {
            public TextMeshPro t; public string zh; public Ink.Face face; public float maxW; public int q;
            public float fitW, baseSize;   // fitW > 0：单行文字宽度超过它（米）就把字缩小（换语言 / 换内容后不会顶出版心）
            public void Apply()
            {
                t.font = Ink.Font(face); t.fontMaterial.renderQueue = q;
                string s = Loc.T(zh); t.text = Loc.En && face != Ink.Face.Corp ? s.ToUpperInvariant() : s;
                if (fitW > 0)
                {
                    if (baseSize <= 0) baseSize = t.fontSize;
                    t.fontSize = baseSize;
                    float pw = t.GetPreferredValues(t.text, 9999, 0).x * 0.01f;
                    if (pw > fitW) t.fontSize = baseSize * fitW / pw;
                }
                float w = maxW > 0 ? maxW * 100 : 9999;
                var p = t.GetPreferredValues(t.text, w, 0);
                t.rectTransform.sizeDelta = new Vector2(maxW > 0 ? Mathf.Min(w, p.x) + 1 : p.x + 1, p.y + 1);
            }
        }
        public readonly List<Label> labels = new();
        /// <summary>给一行字设宽度上限（米）：超了自动缩小字号</summary>
        public TextMeshPro Fit(TextMeshPro t, float w) { foreach (var l in labels) if (l.t == t) { l.fitW = w; l.Apply(); } return t; }

        // ───────── 告示纸的版心（11-11 用户：纸面左边的字离纸边太近）─────────
        // 纸的贴图（paper_notice）上印着版心线，量出来占图宽的比例：纸边 2% / 98.8%，页眉黑条 6.5%–93.5%，左右版心线 13.8% / 86.3%；
        // 占图高（从上往下）：黑条 4.6%–14.9%，粉线到 15.9%，上下版心线 19.7% / 84.4%。
        // 原来的字是凭感觉摆的：页眉比黑条左端还靠左，菜单在左版心线外面。现在统一用下面的换算，从版心线往里排。
        public const float RuleL = 0.138f, RuleR = 0.863f, RuleT = 0.197f, RuleB = 0.844f, BarMid = 0.0975f, TextL = 0.160f;
        /// <summary>纸上占图宽 fx 的那条竖线，在页面高度 y 处的 x（纸是歪着贴的，竖线跟着歪；c / w / rot = 纸的中心、宽、转角）</summary>
        public static float PaperX(Vector2 c, float w, float rot, float fx, float y)
        {
            float dx = (fx - 0.5f) * w, r = rot * Mathf.Deg2Rad, sn = Mathf.Sin(r), cs = Mathf.Cos(r);
            float k = (y - c.y - dx * sn) / cs;
            return c.x + dx * cs - k * sn;
        }
        /// <summary>纸上占图高 fy（从上往下）的那条横线，在页面 x 处的 y</summary>
        public static float PaperY(Vector2 c, float h, float rot, float fy, float x)
        {
            float dy = (0.5f - fy) * h, r = rot * Mathf.Deg2Rad, sn = Mathf.Sin(r), cs = Mathf.Cos(r);
            float k = (x - c.x + dy * sn) / cs;
            return c.y + dy * cs + k * sn;
        }
        public void Relabel() { foreach (var l in labels) l.Apply(); }
        public void SetAlpha(float a) { foreach (var (m, a0) in fades) { var c = m.color; c.a = a0 * a; m.color = c; } foreach (var l in labels) { var c = l.t.color; c.a = a; l.t.color = c; } }
    }

    /// <summary>告示纸上的一行可选项：平时是黑色公文字；看着它 → 粉色喷漆带从左刷到右、字变白、左边冒出一个涂鸦箭头；捏一下 → 爪痕划过</summary>
    public class NoticeItem
    {
        public string zh; public Action onPress; public bool enabled = true;
        public Transform spray, arrow, claw, redact; public TextMeshPro text; Vector2 pos; float w, h, hover, press, rot;
        public NoticeItem(Sheet s, string zh, Vector2 pos, float h, float w, Action onPress, float rot = 0)
        {
            this.zh = zh; this.pos = pos; this.w = w; this.h = h; this.onPress = onPress; this.rot = rot;
            // 11-11 用户：注意排版边距 —— 喷漆圈原来从字左边 5cm 处开始，字离纸边只有 2.5cm，喷漆有一半喷到纸外面悬在空中；改成从字左边 2.2cm 开始
            spray = s.Img("spray_pink", pos + new Vector2(w * 0.5f - 0.006f, 0), h * 1.9f, rot - 2.5f, null, w + 0.032f);
            text = s.Text(zh, Ink.Face.Corp, pos, h, Ink.Black, rot);
            arrow = s.Img("doodle_arrow", pos + new Vector2(-0.045f, 0.002f), h * 0.6f, rot + 8);
            claw = s.Img("doodle_claw", pos + new Vector2(w * 0.55f, 0), h * 2.2f, rot - 20);
            spray.localScale = new Vector3(0.0001f, spray.localScale.y, 1); arrow.localScale = Vector3.zero; claw.localScale = Vector3.zero;
            sprayW = w + 0.032f; arrowS = arrow.localScale; clawS = new Vector3(h * 2.2f, h * 2.2f, 1);
            arrowS = new Vector3(h * 0.6f * 1.9f, h * 0.6f, 1);
        }
        float sprayW; Vector3 arrowS, clawS;
        public bool Hit(Ray r, Transform page, out float t)
        {
            t = 0; var pl = new Plane(-page.forward, page.position);
            if (!pl.Raycast(r, out t)) return false;
            return Contains(page.InverseTransformPoint(r.GetPoint(t)));
        }
        /// <summary>纸面本地坐标的点在不在这一行的可点范围里（上下留一点，相邻两行不重叠）</summary>
        public bool Contains(Vector2 p)
        {
            var d = p - (pos + new Vector2(w * 0.5f - 0.02f, 0));
            return Mathf.Abs(d.x) < w * 0.5f + 0.04f && Mathf.Abs(d.y) < h * 0.95f;
        }
        public void Tick(float dt, bool hot)
        {
            if (hot && enabled && !wasHot) MonsterAudio.Play("ui_hover");
            wasHot = hot && enabled;
            hover = Mathf.MoveTowards(hover, hot && enabled ? 1 : 0, dt * (hot ? 7 : 5));
            press = Mathf.Max(0, press - dt * 2.5f);
            float e = 1 - (1 - hover) * (1 - hover);
            // 喷漆带从左往右刷出来（缩放 x，同时把中心往右挪，左端固定）
            var sp = spray.localPosition; float sw = Mathf.Max(0.0001f, sprayW * e);
            spray.localScale = new Vector3(sw, spray.localScale.y, 1);
            spray.localPosition = new Vector3(pos.x - 0.022f + sw * 0.5f, sp.y, sp.z);
            text.color = enabled ? Color.Lerp(Ink.Black, Color.white, e) : new Color(0.55f, 0.55f, 0.58f);
            text.transform.localPosition = new Vector3(pos.x + 0.012f * e, pos.y, text.transform.localPosition.z);
            arrow.localScale = arrowS * e;
            float c = press > 0 ? Mathf.Min(1, (1 - press) * 4) : 0;
            claw.localScale = clawS * c;
        }
        bool wasHot;
        public void Press() { press = 1; MonsterAudio.Play("ui_press"); onPress?.Invoke(); }
    }

    /// <summary>一组可选项 + 看着 / 捏一下</summary>
    public class NoticeMenu
    {
        public readonly List<NoticeItem> items = new(); public Transform page;
        public NoticeItem Hot { get; private set; }
        public int debugHot = -1;   // 截图验收：强制悬停第几项
        // 手指直接点：指尖在纸面前 4cm 内、对着某一行 → 悬停（优先于视线）；从 2cm 外戳进 0.8cm 内 → 按下。
        // 按完要把手指收回到 2cm 外才能再点（防连点）
        readonly bool[] armed = { true, true };
        public bool Poked { get; private set; }   // 这一帧手指点到了纸面（不管有没有点中选项）
        public void Tick(float dt, Pointer p)
        {
            Hot = null; float best = float.MaxValue; Poked = false;
            if (debugHot >= 0 && debugHot < items.Count) { Hot = items[debugHot]; foreach (var it in items) it.Tick(dt, it == Hot); return; }
            NoticeItem poke = null; bool pokeDown = false;
            float s = Mathf.Max(1e-4f, page.lossyScale.z);
            for (int i = 0; i < p.tips.Count && i < 2; i++)
            {
                var lp = page.InverseTransformPoint(p.tips[i]);
                float depth = -lp.z * s;   // 纸面朝玩家的一侧是本地 −Z
                if (depth > 0.02f) armed[i] = true;
                if (depth > 0.04f || depth < -0.05f || Mathf.Abs(lp.x) > 0.5f || Mathf.Abs(lp.y) > 0.5f) continue;
                NoticeItem on = null;
                foreach (var it in items) if (it.text.gameObject.activeInHierarchy && it.enabled && it.Contains(lp)) { on = it; break; }
                if (on != null) poke = on;
                if (armed[i] && depth < 0.008f) { armed[i] = false; Poked = true; if (on != null) pokeDown = true; }
            }
            if (poke != null) Hot = poke;
            else foreach (var it in items) if (it.text.gameObject.activeInHierarchy && it.enabled && it.Hit(p.ray, page, out var t) && t < best) { best = t; Hot = it; }
            foreach (var it in items) it.Tick(dt, it == Hot);
            // 用眼睛选的：视线要在这一项上停满 0.15 秒，捏才算数（眼睛扫过去的那一下不算）；手指直接点的不受这个限制
            hotT = Hot != null && Hot == lastHot ? hotT + dt : 0; lastHot = Hot;
            if (Hot != null && (pokeDown || (p.down && hotT >= 0.15f))) Hot.Press();
        }
        NoticeItem lastHot; float hotT;
    }

    /// <summary>故障切片：偶尔在纸边闪几条粉 / 青 / 黄的横向错位条（复印机条纹）</summary>
    public class Glitch
    {
        readonly List<Transform> bars = new(); float next, t; readonly Vector2 area; readonly System.Random rnd = new(11);
        public Glitch(Sheet s, Vector2 center, Vector2 size, int n = 6)
        {
            area = size;
            for (int i = 0; i < n; i++)
            {
                var c = i % 3 == 0 ? Ink.Pink : i % 3 == 1 ? Ink.Cyan : Ink.Yellow; c.a = 0.85f;
                var b = s.Rect(center, new Vector2(0.06f, 0.006f), c); b.gameObject.SetActive(false); bars.Add(b);
            }
            this.center = center;
        }
        readonly Vector2 center;
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        public void Tick(float dt)
        {
            t += dt;
            if (t < next) { if (t > next - R(0.5f, 3f) + 0.12f) foreach (var b in bars) b.gameObject.SetActive(false); return; }
            next = t + R(1.2f, 3.5f);
            int k = (int)R(2, bars.Count);
            for (int i = 0; i < bars.Count; i++)
            {
                var b = bars[i]; bool on = i < k; b.gameObject.SetActive(on); if (!on) continue;
                float y = center.y + R(-0.5f, 0.5f) * area.y, x = center.x + (R(0, 1) < 0.5f ? -1 : 1) * area.x * 0.5f + R(-0.04f, 0.04f);
                b.localPosition = new Vector3(x, y, b.localPosition.z); b.localScale = new Vector3(R(0.03f, 0.16f), R(0.003f, 0.012f), 1);
            }
        }
    }

    /// <summary>大号涂鸦字：同一句话叠青 / 粉两层错位，偶尔抖一下（RGB 分离的故障感）</summary>
    public class Shout
    {
        readonly TextMeshPro main, c1, c2; float t, jitter; readonly Vector3 p0;
        /// <summary>maxW > 0：整行宽度超过它（米）就缩小字号（英文“WRECK!”比中文“拆！”宽三倍，原来压住了左边的菜单）</summary>
        public Shout(Sheet s, string zh, Vector2 pos, float size, Color color, float rot, float maxW = 0)
        {
            c1 = s.Text(zh, Ink.Face.Graffiti, pos, size, new Color(Ink.Cyan.r, Ink.Cyan.g, Ink.Cyan.b, 0.8f), rot, TextAlignmentOptions.Center);
            c2 = s.Text(zh, Ink.Face.Graffiti, pos, size, new Color(Ink.Yellow.r, Ink.Yellow.g, Ink.Yellow.b, 0.7f), rot, TextAlignmentOptions.Center);
            main = s.Text(zh, Ink.Face.Graffiti, pos, size, color, rot, TextAlignmentOptions.Center);
            if (maxW > 0) foreach (var t0 in new[] { c1, c2, main }) s.Fit(t0, maxW);
            p0 = main.transform.localPosition;
        }
        public void SetActive(bool on) { foreach (var x in new[] { c1, c2, main }) x.gameObject.SetActive(on); }
        public void Tick(float dt)
        {
            t += dt;
            if (UnityEngine.Random.value < dt * 0.6f) jitter = 0.18f;
            jitter = Mathf.Max(0, jitter - dt);
            float k = jitter > 0 ? 1 : 0.25f;
            c1.transform.localPosition = p0 + new Vector3(-0.004f, 0.002f, 0.0002f) * k;
            c2.transform.localPosition = p0 + new Vector3(0.004f, -0.002f, 0.0003f) * k;
            main.transform.localPosition = p0 + (jitter > 0 ? new Vector3(UnityEngine.Random.Range(-0.003f, 0.003f), 0, 0) : Vector3.zero);
        }
    }
}
