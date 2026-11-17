// 局内记分牌的实物结构 + 体力条。
// 11-12 用户：通缉牌的质感要做好，像真的广告牌；11-11 用户：看不到自己的血量在哪。
//  · Billboard.Build：路边广告牌的样子 —— 铁皮背盒、角钢边框、铆钉、牌面海报、顶上三盏鹅颈射灯、底下检修走道和栏杆、两根带斜撑的立柱。
//    结构件用城市同一套三渲二材质（受光、有描边），所以和楼、路灯是一个世界里的东西；牌面内容（字、灯、体力条）由调用方贴在牌面上。
//  · HpBar：一排 10 格的体力条 + “体力”标签 + 数字。绿 → 黄 → 红；挨打时整条闪白并抖一下；低于三成慢慢地红色呼吸（约 1.2Hz，不是频闪）。
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Monster.Game
{
    public static class Billboard
    {
        static Transform Part(Transform p, Mesh mesh, Material m, Vector3 pos, Vector3 scale, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(p, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
        /// <summary>盒子：中心 c、尺寸 s（CityMeshes.Box 的原点在底面中心）</summary>
        static Transform Box(Transform p, Material m, Vector3 c, Vector3 s, string name = "Box") => Part(p, CityMeshes.Box(), m, c - Vector3.up * s.y * 0.5f, s, name);
        /// <summary>从 a 到 b 的方杆</summary>
        static Transform Strut(Transform p, Material m, Vector3 a, Vector3 b, float th, string name = "Strut")
        {
            var t = Part(p, CityMeshes.Box(), m, a, new Vector3(th, (b - a).magnitude, th), name);
            t.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            return t;
        }

        /// <summary>
        /// 牌面在本地 xy 平面、朝 −Z（朝玩家），宽 w 高 h，底边在 y0；立柱从 y0 往下伸 postLen（到桌面）。
        /// 返回牌面海报的材质（没有海报贴图时是深色铁皮底）。
        /// </summary>
        public static Material Build(Transform p, float w, float h, float y0, float postLen)
        {
            var steel = CityMaterials.Solid("#6a6f88"); var dark = CityMaterials.Solid("#2a2838"); var rivet = CityMaterials.Solid("#b4b9d0");
            var grate = CityMaterials.Solid("#3c3f52"); var lampM = CityMaterials.Solid("#23212e");
            float cy = y0 + h / 2;
            const float F = 0.007f;   // 角钢边框宽
            // 背盒（比牌面每边大一圈，厚 1.6cm，在牌面后面）
            Box(p, dark, new Vector3(0, cy, 0.0095f), new Vector3(w + 0.02f, h + 0.02f, 0.016f), "BackBox");
            // 牌面海报
            var tex = NeonKit.Tex("billboard_face");
            var face = NeonKit.Mat(tex ? Color.white : new Color(0.1f, 0.08f, 0.17f), 3150); if (tex) face.mainTexture = tex;
            var fq = NeonKit.Shape(p, "Face", NeonKit.Quad(), new Vector3(0, cy, 0.0005f), face); fq.localScale = new Vector3(w, h, 1);
            // 角钢边框：压住牌面四边，比牌面凸出 5mm
            Box(p, steel, new Vector3(0, y0 + h + F / 2 - 0.002f, -0.002f), new Vector3(w + 0.022f, F, 0.012f), "FrameT");
            Box(p, steel, new Vector3(0, y0 - F / 2 + 0.002f, -0.002f), new Vector3(w + 0.022f, F, 0.012f), "FrameB");
            foreach (int sx in new[] { -1, 1 }) Box(p, steel, new Vector3(sx * (w / 2 + F / 2 - 0.002f), cy, -0.002f), new Vector3(F, h + 0.008f, 0.012f), "FrameS");
            // 铆钉：上下边框每 4.5cm 一颗，左右边框各 3 颗
            int nx = Mathf.Max(2, Mathf.RoundToInt(w / 0.045f));
            for (int i = 0; i <= nx; i++)
                foreach (float y in new[] { y0 + h + F / 2 - 0.002f, y0 - F / 2 + 0.002f })
                    Part(p, CityMeshes.Sphere(8, 5), rivet, new Vector3(-w / 2 + w * i / nx, y - 0.0017f, -0.0085f), Vector3.one * 0.0034f, "Rivet");
            foreach (int sx in new[] { -1, 1 })
                for (int i = 1; i <= 3; i++)
                    Part(p, CityMeshes.Sphere(8, 5), rivet, new Vector3(sx * (w / 2 + F / 2 - 0.002f), y0 + h * i / 4f - 0.0017f, -0.0085f), Vector3.one * 0.0034f, "Rivet");
            // 顶上三盏鹅颈射灯：灯臂从上边框往前上方探出去，灯头朝下照着牌面
            var glow = CityMaterials.Glow("#ffe9b0", 2.6f);
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * w * 0.32f;
                var a = new Vector3(x, y0 + h + F - 0.002f, 0.0f); var b = new Vector3(x, y0 + h + 0.03f, -0.014f); var c = new Vector3(x, y0 + h + 0.026f, -0.04f);
                Strut(p, lampM, a, b, 0.0028f, "LampArm"); Strut(p, lampM, b, c, 0.0028f, "LampArm");
                var head = Box(p, lampM, c + new Vector3(0, -0.004f, -0.004f), new Vector3(0.02f, 0.008f, 0.014f), "LampHead"); head.localRotation = Quaternion.Euler(-28, 0, 0);
                var bulb = Box(p, glow, c + new Vector3(0, -0.0085f, -0.002f), new Vector3(0.015f, 0.0016f, 0.009f), "LampBulb"); bulb.localRotation = Quaternion.Euler(-28, 0, 0);
            }
            // 底下的检修走道：格栅板 + 前栏杆（横杆 + 小立柱）+ 两头的三角托架
            float wy = y0 - F - 0.004f;
            Box(p, grate, new Vector3(0, wy, -0.014f), new Vector3(w + 0.03f, 0.003f, 0.03f), "Catwalk");
            Strut(p, steel, new Vector3(-w / 2 - 0.015f, wy + 0.016f, -0.028f), new Vector3(w / 2 + 0.015f, wy + 0.016f, -0.028f), 0.002f, "Rail");
            int np = Mathf.Max(3, Mathf.RoundToInt(w / 0.07f));
            for (int i = 0; i <= np; i++) { float x = -w / 2 - 0.013f + (w + 0.026f) * i / np; Strut(p, steel, new Vector3(x, wy, -0.028f), new Vector3(x, wy + 0.016f, -0.028f), 0.0018f, "RailPost"); }
            foreach (int sx in new[] { -1, 1 }) Strut(p, steel, new Vector3(sx * w * 0.4f, wy - 0.02f, 0.012f), new Vector3(sx * w * 0.4f, wy, -0.026f), 0.003f, "Bracket");
            // 两根格构立柱（每根两条钢管 + 之字形斜撑），从背盒底到桌面；两柱之间一道横梁
            float top = y0 - 0.006f, bot = y0 - postLen;
            foreach (int sx in new[] { -1, 1 })
            {
                float xc = sx * w * 0.3f; const float G = 0.011f;
                Strut(p, steel, new Vector3(xc - G, bot, 0.012f), new Vector3(xc - G, top, 0.012f), 0.0042f, "Post");
                Strut(p, steel, new Vector3(xc + G, bot, 0.012f), new Vector3(xc + G, top, 0.012f), 0.0042f, "Post");
                int nb = Mathf.Max(2, Mathf.RoundToInt((top - bot) / 0.03f));
                for (int i = 0; i < nb; i++)
                {
                    float ya = Mathf.Lerp(bot, top, i / (float)nb), yb = Mathf.Lerp(bot, top, (i + 1) / (float)nb);
                    Strut(p, steel, new Vector3(xc + (i % 2 == 0 ? -G : G), ya, 0.012f), new Vector3(xc + (i % 2 == 0 ? G : -G), yb, 0.012f), 0.0022f, "Brace");
                }
                Box(p, dark, new Vector3(xc, bot + 0.002f, 0.012f), new Vector3(0.04f, 0.004f, 0.022f), "Foot");
            }
            if (top - bot > 0.06f) Strut(p, steel, new Vector3(-w * 0.3f, Mathf.Lerp(bot, top, 0.55f), 0.012f), new Vector3(w * 0.3f, Mathf.Lerp(bot, top, 0.55f), 0.012f), 0.003f, "Beam");
            return face;
        }

        /// <summary>牌面上的一盏圆灯（深色灯座 + 灯泡），返回灯泡材质（改颜色 = 亮 / 灭）</summary>
        public static Material Lamp(Transform p, Vector3 c, float d, int q)
        {
            NeonKit.Shape(p, "Socket", NeonKit.RoundRect(d * 1.28f, d * 1.28f, d * 0.64f), c + new Vector3(0, 0, 0.0006f), NeonKit.Mat(new Color(0.07f, 0.06f, 0.1f), q));
            var m = NeonKit.Mat(new Color(0.25f, 0.22f, 0.3f), q + 1);
            NeonKit.Shape(p, "Bulb", NeonKit.RoundRect(d, d, d * 0.5f), c, m);
            var hi = NeonKit.Mat(new Color(1, 1, 1, 0.35f), q + 2);
            NeonKit.Shape(p, "Glint", NeonKit.RoundRect(d * 0.3f, d * 0.2f, d * 0.1f), c + new Vector3(-d * 0.18f, d * 0.2f, -0.0004f), hi);
            return m;
        }
    }

    /// <summary>体力条（见文件头）。左端在 pos，总宽 w、格子高 h；每帧调 Set</summary>
    public class HpBar
    {
        readonly Transform root; readonly List<Material> segs = new(); readonly List<Transform> segT = new();
        readonly TextMeshPro label, num; readonly int q; readonly Vector3 pos0; int shown = -999; bool en;
        static readonly Color Off = new(0.2f, 0.18f, 0.26f), Good = new(0.32f, 1f, 0.5f), Mid = new(1f, 0.82f, 0.2f), Bad = new(1f, 0.28f, 0.32f);
        const int N = 10;
        readonly bool rage; readonly string labelZh;
        /// <summary>rage = true：怒气条（青色，满了变粉并慢慢呼吸；标签“怒气”）</summary>
        public HpBar(Transform parent, Vector3 pos, float w, float h, int q, bool rage = false)
        {
            this.q = q; pos0 = pos; this.rage = rage; labelZh = rage ? "怒气" : "生命";
            // 外层固定在牌面上（调用方之后可能整体挪动牌面的子物体），内层只负责挨打时抖
            var outer = new GameObject("HpBar").transform; outer.SetParent(parent, false); outer.localPosition = pos;
            root = new GameObject("Inner").transform; root.SetParent(outer, false);
            float labelW = h * 3.1f, numW = h * 3.2f, gap = h * 0.22f;
            // 字号：TMP 字号 × 0.01 ≈ 行高（米）；字高取格子高的 1.25 倍
            label = NeonKit.Text(root, new Vector3(0, 0, 0), h * 1250, Color.white, q + 2, TextAlignmentOptions.Left);
            label.rectTransform.pivot = new Vector2(0, 0.5f);
            float x0 = labelW, x1 = w - numW, cell = (x1 - x0) / N;
            // 槽：一条深色圆角底
            NeonKit.Shape(root, "Trough", NeonKit.RoundRect(x1 - x0 + gap * 2, h + gap * 2, h * 0.35f), new Vector3((x0 + x1) / 2, 0, 0.0006f), NeonKit.Mat(new Color(0.05f, 0.04f, 0.08f), q));
            for (int i = 0; i < N; i++)
            {
                var m = NeonKit.Mat(Off, q + 1); segs.Add(m);
                segT.Add(NeonKit.Shape(root, "Seg" + i, NeonKit.RoundRect(cell - gap, h, h * 0.22f), new Vector3(x0 + cell * (i + 0.5f), 0, 0), m));
            }
            num = NeonKit.Text(root, new Vector3(w, 0, 0), h * 1250, Color.white, q + 2, TextAlignmentOptions.Right);
            num.rectTransform.pivot = new Vector2(1, 0.5f);
            Relabel();
        }
        void Relabel()
        {
            en = Loc.En;
            NeonKit.SetText(label, labelZh, q + 2);   // 11-12 用户：不叫体力，是生命
            num.font = NeonKit.Font; num.fontMaterial.renderQueue = q + 2;
        }
        /// <summary>hp01 = 0～1；flash = 刚挨打（1 → 0）</summary>
        public void Set(float hp01, float flash)
        {
            if (!root) return;
            if (en != Loc.En) { Relabel(); shown = -999; }
            hp01 = Mathf.Clamp01(hp01);
            int pct = Mathf.CeilToInt(hp01 * 100);
            if (pct != shown) { shown = pct; num.text = pct.ToString(); var pv = num.GetPreferredValues(num.text, 9999, 0); num.rectTransform.sizeDelta = new Vector2(pv.x + 1, pv.y + 1); }
            var baseC = hp01 > 0.5f ? Good : hp01 > 0.3f ? Mid : Bad;
            float breathe = hp01 <= 0.3f ? 0.72f + 0.28f * Mathf.Sin(Time.time * 7.5f) : 1;
            if (rage) { bool full = hp01 >= 0.999f; baseC = full ? new Color(1f, 0.36f, 0.72f) : new Color(0.35f, 0.9f, 1f); breathe = full ? 0.75f + 0.25f * Mathf.Sin(Time.time * 6f) : 1; flash = 0; }
            float lit = hp01 * N;
            for (int i = 0; i < N; i++)
            {
                float k = Mathf.Clamp01(lit - i);   // 最后一格按比例亮
                var c = Color.Lerp(Off, baseC * breathe, k > 0 ? Mathf.Lerp(0.45f, 1, k) : 0);
                segs[i].color = Color.Lerp(c, Color.white, Mathf.Clamp01(flash) * (k > 0 ? 0.85f : 0.25f));
            }
            num.color = rage ? (hp01 >= 0.999f ? new Color(1f, 0.5f, 0.8f) : Color.white) : Color.Lerp(hp01 <= 0.3f ? Bad : Color.white, Color.white, Mathf.Clamp01(flash));
            label.color = num.color;
            // 挨打：整条左右抖一下
            root.localPosition = new Vector3(Mathf.Sin(Time.time * 70) * 0.0022f * Mathf.Clamp01(flash), 0, 0);
        }
    }
}
