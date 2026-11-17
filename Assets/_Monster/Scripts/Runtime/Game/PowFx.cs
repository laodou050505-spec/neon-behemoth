// 漫画拟声大字（11-11 用户：整体要霓虹动漫 + 美式卡通涂鸦的感觉，特效也是）：
// 楼被拍扁 / 砸地、爆炸、拍开导弹、拆塔时，在事件上方弹出“轰！”“咔嚓！”“嘭！”（英文 WHAM! / CRASH! / BOOM!），
// 黄字粗黑边、背后一个粉 / 青色爆炸星（带黑描边），弹出 → 停一下 → 缩小上飘消失。
// 舒适：离头不到 35cm 不出（不糊在脸上），同屏最多 3 个，两次之间至少 0.35 秒；字的大小按距离放大（远处也看得清）。
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Monster.Game
{
    public class PowFx : MonoBehaviour
    {
        class P { public Transform root; public float age, tilt, extra, mul = 1; public Vector3 pos; public bool text; }
        readonly List<P> live = new();
        float cd;
        Mesh star, starInk;

        static readonly Dictionary<string, (string[] zh, string[] en, Color burst)> Words = new()
        {
            ["smash"] = (new[] { "轰！", "砰！", "嘭！" }, new[] { "WHAM!", "POW!", "BAM!" }, new Color(1f, 0.36f, 0.72f)),
            ["crash"] = (new[] { "咔嚓！", "哗啦！" }, new[] { "CRASH!", "KRAK!" }, new Color(0.35f, 0.95f, 1f)),
            ["boom"] = (new[] { "嘭！", "轰隆！" }, new[] { "BOOM!", "KABOOM!" }, new Color(1f, 0.55f, 0.15f)),
            ["smack"] = (new[] { "啪！" }, new[] { "SMACK!", "WHACK!" }, new Color(0.35f, 0.95f, 1f)),
            ["tower"] = (new[] { "破！" }, new[] { "BREAK!" }, new Color(1f, 0.36f, 0.72f)),
            ["uproot"] = (new[] { "拔！", "起！" }, new[] { "YOINK!", "HEAVE!" }, new Color(0.35f, 0.95f, 1f)),
            ["clang"] = (new[] { "铛！", "哐！" }, new[] { "CLANG!", "CLONK!" }, new Color(1f, 0.86f, 0.22f)),
            ["zap"] = (new[] { "滋——", "通电！" }, new[] { "BZZT!", "ZAP!" }, new Color(0.35f, 0.95f, 1f)),
        };

        public void Show(string kind, Vector3 at)
        {
            if (cd > 0 || live.Count >= 3 || !Words.TryGetValue(kind, out var w)) return;
            var cam = Camera.main; if (!cam) return;
            float d = Vector3.Distance(cam.transform.position, at);
            if (d < 0.35f) return;
            cd = 0.35f;
            star ??= Star(12, 1f, 0.6f); starInk ??= Star(12, 1.12f, 0.7f);
            var root = new GameObject("Pow").transform; root.SetParent(transform, false); root.position = at;
            var ink = NeonKit.Shape(root, "Ink", starInk, new Vector3(0, 0, 0.002f), NeonKit.Mat(NeonKit.Ink, 3300));
            var bs = NeonKit.Shape(root, "Burst", star, new Vector3(0, 0, 0.001f), NeonKit.Mat(w.burst, 3301));
            foreach (var t0 in new[] { ink, bs }) t0.localScale = new Vector3(0.056f, 0.04f, 1);
            var t = NeonKit.Text(root, Vector3.zero, 26, new Color(1f, 0.9f, 0.18f), 3302);
            var list = Loc.En ? w.en : w.zh;
            t.text = list[Random.Range(0, list.Length)];
            t.font = Resources.Load<TMP_FontAsset>(Loc.En ? "Fonts/ComicEN SDF" : "Fonts/GraffitiZH SDF");
            t.fontMaterial.renderQueue = 3302;
            t.outlineWidth = 0.32f; t.outlineColor = new Color32(20, 12, 30, 255);
            t.fontStyle = Loc.En ? FontStyles.Normal : FontStyles.Bold;
            root.localScale = Vector3.zero;
            live.Add(new P { root = root, pos = at, tilt = Random.Range(-12f, 12f) });
        }

        /// <summary>只弹一行字（没有爆炸星）：伤害数字。不受“同屏 3 个 / 间隔”的限制（挨打一定要看得到），但同时最多 2 个</summary>
        int textLive;
        public void ShowText(string text, Vector3 at, Color color, float extra = 0, float size = 22, float scale = 1, bool force = false)
        {
            var cam = Camera.main; if (!cam) return;
            if (Vector3.Distance(cam.transform.position, at) < 0.3f) return;
            int n = 0; foreach (var p0 in live) if (p0.text) n++;
            if (n >= 2 && !force) return;   // force：重要提示（封印解除等）不受“最多两条”的限制
            var root = new GameObject("PowText").transform; root.SetParent(transform, false); root.position = at;
            var t = NeonKit.Text(root, Vector3.zero, size, color, 3302);
            bool ascii = true; foreach (char ch in text) if (ch > 127) { ascii = false; break; }
            t.text = text; t.font = Resources.Load<TMP_FontAsset>(ascii ? "Fonts/ComicEN SDF" : "Fonts/GraffitiZH SDF"); t.fontMaterial.renderQueue = 3302;
            t.outlineWidth = 0.3f; t.outlineColor = new Color32(20, 12, 30, 255);
            root.localScale = Vector3.zero;
            live.Add(new P { root = root, pos = at, tilt = scale > 1 ? 0 : Random.Range(-8f, 8f), text = true, extra = extra, mul = scale });
        }

        void Update()
        {
            cd -= Time.deltaTime;
            var cam = Camera.main; if (!cam) return;
            var head = cam.transform.position;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var p = live[i]; p.age += Time.deltaTime;
                if (!p.root) { live.RemoveAt(i); continue; }
                const float Pop = 0.14f; float Hold = 0.55f + p.extra, End = 0.8f + p.extra;
                if (p.age >= End) { Destroy(p.root.gameObject); live.RemoveAt(i); continue; }
                float s = p.age < Pop ? Back(p.age / Pop) : p.age < Hold ? 1 : 1 - (p.age - Hold) / (End - Hold);
                float d = Vector3.Distance(head, p.pos);
                p.root.position = p.pos + Vector3.up * (0.015f * Mathf.Max(0, p.age - Hold) / (End - Hold) * d);
                p.root.rotation = Quaternion.LookRotation(p.root.position - head, Vector3.up) * Quaternion.Euler(0, 0, p.tilt);
                p.root.localScale = Vector3.one * Mathf.Max(0.0001f, s) * Mathf.Clamp(d / 0.8f, 0.8f, 2.2f) * p.mul;
            }
        }

        static float Back(float x) { x = Mathf.Clamp01(x); const float c = 2.2f; return 1 + (c + 1) * Mathf.Pow(x - 1, 3) + c * Mathf.Pow(x - 1, 2); }

        /// <summary>爆炸星（xy 平面、面朝 −Z，和 NeonKit.RoundRect 一样）：n 个尖、外半径 r1、内半径 r2</summary>
        static Mesh Star(int n, float r1, float r2)
        {
            var v = new List<Vector3> { Vector3.zero }; var t = new List<int>();
            for (int i = 0; i < n * 2; i++)
            {
                float a = i * Mathf.PI / n + Mathf.PI / 2, r = (i % 2 == 0 ? r1 : r2) * (1 + 0.12f * Mathf.Sin(i * 2.3f));
                v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0));
            }
            for (int i = 0; i < n * 2; i++) { t.Add(0); t.Add(1 + (i + 1) % (n * 2)); t.Add(1 + i); }
            var m = new Mesh { name = "pow_star" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
