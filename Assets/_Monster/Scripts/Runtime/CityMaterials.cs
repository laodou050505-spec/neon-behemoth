// 城市材质：每种楼一张“一个开间”的立面贴图（64×64，侧面按开间数重复）+ 夜里亮窗的自发光贴图。
// 第一版是程序画的（从原型 buildings.js 的 drawFacade 移植），之后换成 Tripo 生成的正式贴图，材质槽位不变。
using System.Collections.Generic;
using UnityEngine;

namespace Monster
{
    public static class CityMaterials
    {
        public const int N = 64;
        public static readonly string[] Neon = { "#ff4fa8", "#3ef0ff", "#ffb43c", "#9b6bff", "#58ff9a", "#ff6b4a" };
        public static readonly Dictionary<Style, string[]> Palette = new()
        {
            [Style.Glass] = new[] { "#7cc3e6", "#86d8cf", "#a9b9f0", "#e3a7c8", "#a6e4f2" },
            [Style.Office] = new[] { "#d9cfc0", "#c7c2bb", "#b9c3cc", "#e0d6c6", "#cbbba6" },
            [Style.Deco] = new[] { "#ffd1dc", "#bff0e0", "#ffe0b8", "#e3d0ff", "#fff3e0", "#c8ecff" },
            [Style.Apt] = new[] { "#f2e4d4", "#e8d3a8", "#f0b9a8", "#d9e4cf", "#e6c6dc", "#d6dbe8" },
            [Style.Shop] = new[] { "#f2e4d4", "#ffd8c2", "#d8efe6", "#efe0f5" },
            [Style.Warehouse] = new[] { "#8fa3b0", "#b07a5a", "#7a9a86", "#a0a0a8" },
            [Style.Villa] = new[] { "#f6f2ea", "#fbe9d9", "#eef4f0" },
            [Style.Garage] = new[] { "#bdb8ae" },
            [Style.Gas] = new[] { "#f4f4f0" },
            [Style.Condo] = new[] { "#f4f1ea" }, [Style.Brick] = new[] { "#a5523e" }, [Style.Modern] = new[] { "#2f3440" },
            [Style.Spire] = new[] { "#dac9a6" }, [Style.Mixed] = new[] { "#f2c06b" },
        };
        // 每种立面一个开间的宽度（米）
        public static readonly Dictionary<Style, float> Tile = new()
        {
            [Style.Glass] = 0.024f, [Style.Office] = 0.03f, [Style.Deco] = 0.03f, [Style.Apt] = 0.032f, [Style.Garage] = 0.04f,
            [Style.Shop] = 0.05f, [Style.Warehouse] = 0.03f, [Style.Villa] = 0.035f, [Style.Gas] = 0.05f,
            [Style.Condo] = 0.03f, [Style.Brick] = 0.03f, [Style.Modern] = 0.03f, [Style.Spire] = 0.03f, [Style.Mixed] = 0.03f,
        };
        public static readonly Dictionary<Style, string> Roof = new()
        {
            [Style.Glass] = "#46505c", [Style.Office] = "#7d776f", [Style.Deco] = "#e9e1d6", [Style.Apt] = "#9a8d80", [Style.Garage] = "#8f8b84",
            [Style.Shop] = "#6f6670", [Style.Warehouse] = "#6a7178", [Style.Villa] = "#c8603a", [Style.Gas] = "#e8e8e4",
            [Style.Condo] = "#9a9a98", [Style.Brick] = "#6f6a66", [Style.Modern] = "#8f8b86", [Style.Spire] = "#7d776f", [Style.Mixed] = "#958c82",
        };

        public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static Color Shade(Color c, float k) => new(c.r * k, c.g * k, c.b * k, 1);

        static Shader toon, glow, fade;
        public static Shader Toon => toon ? toon : (toon = Shader.Find("Monster/CityToon"));
        public static Shader GlowShader => glow ? glow : (glow = Shader.Find("Monster/CityGlow"));
        public static Shader FadeShader => fade ? fade : (fade = Shader.Find("Monster/CityFade"));

        static readonly Dictionary<string, Material> mats = new();
        public static void ClearCache() => mats.Clear();

        // 纯色卡通材质（屋顶、附件、地面边）
        public static Material Solid(string hex, float glowK = 0)
        {
            string key = "s" + hex + glowK;
            if (mats.TryGetValue(key, out var m) && m) return m;
            m = new Material(Toon) { name = key, enableInstancing = true };
            m.SetColor("_BaseColor", Hex(hex));
            m.SetFloat("_Glow", glowK);
            mats[key] = m;
            return m;
        }

        // 霓虹：不受光照
        public static Material Glow(string hex, float k = 1.4f)
        {
            string key = "g" + hex + k;
            if (mats.TryGetValue(key, out var m) && m) return m;
            m = new Material(GlowShader) { name = key, enableInstancing = true };
            m.SetColor("_Color", Hex(hex) * k);
            mats[key] = m;
            return m;
        }

        // 立面材质：楼型 + 主色 + 点缀色 + 变体 + 是否夜晚
        public static Material Facade(Style style, string col, string acc, int variant, bool night)
        {
            string key = $"f{style}{col}{acc}{variant}{night}";
            if (mats.TryGetValue(key, out var m) && m) return m;
            m = new Material(Toon) { name = key, enableInstancing = true };
            m.SetTexture("_BaseMap", Paint(style, Hex(col), Hex(acc), variant, false));
            m.SetTexture("_EmissionMap", Paint(style, Hex(col), Hex(acc), variant, true));
            m.SetFloat("_NightGlow", night ? 1 : 0);
            mats[key] = m;
            return m;
        }

        // ───────── 画一个开间 ─────────
        class Canvas
        {
            public Color[] px = new Color[N * N];
            // 坐标和原型 canvas 一致：y 向下
            public void Rect(int x, int y, int w, int h, Color c)
            {
                for (int j = Mathf.Max(0, y); j < Mathf.Min(N, y + h); j++)
                    for (int i = Mathf.Max(0, x); i < Mathf.Min(N, x + w); i++) px[(N - 1 - j) * N + i] = c;
            }
            public void Fill(Color c) => Rect(0, 0, N, N, c);
        }

        static Texture2D Paint(Style style, Color col, Color acc, int variant, bool lit)
        {
            var g = new Canvas();
            bool On(int i) => (variant * 7 + i * 3) % 5 < 3;
            var black = Color.black;
            if (lit) g.Fill(black);
            var warm = Hex("#ffd88a");
            switch (style)
            {
                case Style.Glass:
                    if (!lit)
                    {
                        // 斜向渐变 + 一道高光反射 + 竖框
                        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                        {
                            float t = (x + y) / (2f * N);
                            var c = Color.Lerp(Shade(col, 1.12f), Shade(col, 0.78f), t);
                            int dx = x - (14 - y * 20 / 64);
                            if (dx >= 0 && dx < 12) c = Color.Lerp(c, Color.white, 0.28f);
                            g.px[(N - 1 - y) * N + x] = c;
                        }
                        var frame = Shade(col, 0.45f);
                        g.Rect(0, 0, 3, 64, frame); g.Rect(31, 0, 2, 64, Shade(col, 0.6f)); g.Rect(0, 55, 64, 9, frame);
                    }
                    else for (int i = 0; i < 2; i++) if (On(i)) g.Rect(4 + i * 31, 6, 26, 46, Hex("#ffe3a0"));
                    break;
                case Style.Office:
                    if (!lit) { g.Fill(col); g.Rect(0, 58, 64, 6, Shade(col, 0.85f)); }
                    for (int i = 0; i < 2; i++)
                    {
                        int x = 7 + i * 30;
                        if (!lit) { g.Rect(x, 12, 21, 38, Hex("#33496b")); g.Rect(x, 12, 21, 4, Hex("#5e7aa3")); g.Rect(x + 9, 12, 2, 38, Shade(col, 0.9f)); }
                        else if (On(i)) g.Rect(x, 12, 21, 38, warm);
                    }
                    break;
                case Style.Deco:
                    if (!lit) { g.Fill(col); g.Rect(0, 10, 64, 6, Color.white); g.Rect(0, 16, 64, 3, Shade(col, 0.75f)); g.Rect(0, 60, 64, 4, acc); }
                    for (int i = 0; i < 3; i++)
                    {
                        int x = 6 + i * 20;
                        if (!lit) { g.Rect(x, 22, 13, 30, Hex("#2f8f9c")); g.Rect(x, 22, 13, 4, Hex("#8fe0e6")); }
                        else if (On(i)) g.Rect(x, 22, 13, 30, Hex("#ffe6a6"));
                    }
                    break;
                case Style.Apt:
                    if (!lit)
                    {
                        g.Fill(col); g.Rect(14, 14, 36, 30, Hex("#3b4d6e")); g.Rect(14, 14, 36, 4, Hex("#7d93b8"));
                        g.Rect(10, 44, 44, 4, Shade(col, 0.55f)); for (int k = 0; k < 6; k++) g.Rect(12 + k * 8, 46, 2, 10, Shade(col, 0.6f)); g.Rect(8, 56, 48, 3, Shade(col, 0.55f));
                    }
                    else if (On(0)) g.Rect(14, 14, 36, 30, Hex("#ffcf7a"));
                    break;
                case Style.Garage:
                    if (!lit) { g.Fill(Hex("#3a3a42")); g.Rect(0, 0, 64, 20, col); g.Rect(0, 58, 64, 6, col); g.Rect(0, 0, 7, 64, Shade(col, 0.9f)); g.Rect(18, 34, 14, 10, Hex("#d9534f")); g.Rect(40, 36, 12, 8, Hex("#2f7bbf")); }
                    else g.Rect(0, 22, 64, 4, Hex("#fff1b8"));
                    break;
                case Style.Shop:
                    if (!lit) { g.Fill(col); g.Rect(0, 0, 64, 15, acc); g.Rect(4, 20, 56, 44, Hex("#2a6a76")); g.Rect(8, 22, 8, 40, Color.Lerp(Hex("#2a6a76"), Color.white, 0.35f)); g.Rect(30, 20, 3, 44, Shade(col, 0.7f)); }
                    else { g.Rect(4, 20, 56, 44, Hex("#ffe9b8")); g.Rect(0, 0, 64, 15, acc); }
                    break;
                case Style.Warehouse:
                    if (!lit) { for (int x = 0; x < 64; x += 8) { g.Rect(x, 0, 4, 64, col); g.Rect(x + 4, 0, 4, 64, Shade(col, 0.82f)); } g.Rect(0, 54, 64, 10, Shade(col, 0.6f)); g.Rect(20, 30, 24, 24, Shade(col, 0.5f)); }
                    break;
                case Style.Villa:
                    if (!lit) { g.Fill(col); g.Rect(20, 16, 24, 26, Hex("#3b5f7c")); g.Rect(12, 16, 7, 26, Hex("#2f7fb8")); g.Rect(45, 16, 7, 26, Hex("#2f7fb8")); g.Rect(0, 58, 64, 6, Shade(col, 0.85f)); }
                    else if (On(1)) g.Rect(20, 16, 24, 26, Hex("#ffcf7a"));
                    break;
                case Style.Gas:
                    if (!lit) { g.Fill(col); g.Rect(0, 0, 64, 12, acc); g.Rect(8, 22, 48, 36, Hex("#2a6a76")); }
                    else g.Rect(8, 22, 48, 36, Hex("#fff3c8"));
                    break;
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4, name = $"{style}{(lit ? "_lit" : "")}" };
            tex.SetPixels(g.px); tex.Apply(true);
            return tex;
        }
    }
}
