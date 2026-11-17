// 虚拟家具（11-07）：玩家房间里扫不到桌子 / 柜子时，由游戏生成——做成“城市沙盘展台”：
//  - 沙盘底座（高 3.5cm）：顶面是草地 + 土路 + 碎石（城区边缘的马路就淡出在草地里），四周切面画地层剖面
//    （路面、土层、管线、地铁隧道、岩层），外沿一圈木框；
//  - 底座下面是胡桃木家具：桌子（厚桌面 + 包边 + 带抽屉的围板 + 收分桌腿）/ 柜子（门板 + 抽屉 + 铜把手 + 踢脚）。
// 有真实家具时（zone.furniture = false）什么都不生成，城市直接长在真桌面上。
using System.Collections.Generic;
using UnityEngine;

namespace Monster
{
    public partial class CityBuilder
    {
        const float Plinth = 0.035f;     // 沙盘底座高
        const float PlinthM = 0.04f;     // 底座比城区每边多出的宽度

        static Texture2D woodTex, strataTex;
        readonly Dictionary<Zone, Texture2D> terrainTex = new();

        static Color32 C(string h) => (Color32)CityMaterials.Hex(h);

        // 胡桃木：纵向木纹（三阶色，像手绘），偶尔一个树结
        static Texture2D WoodTex()
        {
            if (woodTex) return woodTex;
            int N = 256; var px = new Color32[N * N];
            var a = C("#7a4a2e"); var b = C("#6a3f27"); var d = C("#56321f");
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    // 木纹：沿 v 拉长的噪声等值线（不是正弦波，避免锯齿纹）
                    float g = Mathf.Sin((Mathf.PerlinNoise(u * 9f, v * 0.9f) * 9f + Mathf.PerlinNoise(u * 30f, v * 2f) * 0.8f) * Mathf.PI);
                    float knot = Mathf.Clamp01(1 - Vector2.Distance(new Vector2(u, v * 0.4f), new Vector2(0.62f, 0.18f)) * 18);
                    var c = g > 0.6f ? b : (g < -0.85f ? d : a);
                    if (knot > 0.2f) c = Color32.Lerp(c, d, knot);
                    px[y * N + x] = c;
                }
            woodTex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "walnut", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            woodTex.SetPixels32(px); woodTex.Apply(true);
            return woodTex;
        }

        // 地层剖面（横向可平铺）：顶 4% 路面沥青 → 14% 浅土 → 土层（带石子）→ 管线 / 地铁隧道剖面 → 岩层
        static Texture2D StrataTex()
        {
            if (strataTex) return strataTex;
            int W = 512, H = 128; var px = new Color32[W * H];
            var rng = new Rng(19);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float v = 1 - y / (float)H;              // 0 顶 → 1 底（贴图行从下往上，这里翻过来按“从上往下”画）
                    float wob = (Mathf.PerlinNoise(x * 0.03f, 3.1f) - 0.5f) * 0.05f;
                    Color32 c;
                    if (v < 0.05f) c = C("#3a3a42");
                    else if (v < 0.09f) c = C("#8f8578");
                    else if (v < 0.42f + wob) c = (Mathf.PerlinNoise(x * 0.08f, y * 0.08f) > 0.62f) ? C("#7a5a3e") : C("#8a6646");
                    else if (v < 0.47f + wob) c = C("#6b4f38");
                    else if (v < 0.8f + wob * 0.6f) c = (Mathf.PerlinNoise(x * 0.05f + 9, y * 0.05f) > 0.55f) ? C("#7d7468") : C("#8e857a");
                    else c = C("#5f5850");
                    px[y * W + x] = c;
                }
            void Disc(int cx, int cy, int r, Color32 c) { for (int y = cy - r; y <= cy + r; y++) for (int x = cx - r; x <= cx + r; x++) if (x >= 0 && x < W && y >= 0 && y < H && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) px[y * W + x] = c; }
            // 石子
            for (int i = 0; i < 90; i++) { int x = rng.Int(0, W - 1), y = rng.Int(H / 2, H - 14); Disc(x, y, rng.Int(1, 3), C("#a89a88")); }
            for (int i = 0; i < 40; i++) { int x = rng.Int(0, W - 1), y = rng.Int(8, H / 2); Disc(x, y, rng.Int(2, 4), C("#6d655b")); }
            // 管线：三根（水管蓝、燃气黄、电缆黑），圆截面 + 深色描边
            foreach (var (cx, col) in new[] { (60, "#3f7fbf"), (200, "#e2b13a"), (330, "#2a2a30"), (450, "#3f7fbf") })
            { int cy = H - 30 - (cx % 3) * 6; Disc(cx, cy, 7, C("#2a2025")); Disc(cx, cy, 5, C(col)); Disc(cx - 1, cy + 1, 2, C("#ffffff")); }
            // 地铁隧道：拱形截面 + 里面一节亮着窗的车厢
            int tx = 256, ty = 34, tr = 20;
            for (int y = ty - tr; y <= ty + tr; y++) for (int x = tx - tr - 8; x <= tx + tr + 8; x++)
                if (x >= 0 && x < W && y >= 0 && y < H && (y <= ty || (x - tx) * (x - tx) / 1.4f + (y - ty) * (y - ty) <= tr * tr)) px[y * W + x] = C("#c9c3b6");
            for (int y = ty - tr + 3; y <= ty + tr - 3; y++) for (int x = tx - tr - 5; x <= tx + tr + 5; x++)
                if (x >= 0 && x < W && y >= 0 && y < H && (y <= ty || (x - tx) * (x - tx) / 1.4f + (y - ty) * (y - ty) <= (tr - 3) * (tr - 3))) px[y * W + x] = C("#2b2733");
            for (int y = ty - 14; y <= ty + 6; y++) for (int x = tx - 18; x <= tx + 18; x++) px[y * W + x] = C("#d8dde2");
            for (int k = 0; k < 4; k++) for (int y = ty - 4; y <= ty + 3; y++) for (int x = tx - 15 + k * 8; x <= tx - 10 + k * 8; x++) px[y * W + x] = C("#ffd28a");
            for (int x = tx - 22; x <= tx + 22; x++) px[(ty - 16) * W + x] = C("#555055");
            strataTex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "strata", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            strataTex.SetPixels32(px); strataTex.Apply(true);
            return strataTex;
        }

        // 沙盘顶面：草地（两阶色块）+ 沿边一圈土路 + 碎石点
        Texture2D TerrainTex(Zone z)
        {
            if (terrainTex.TryGetValue(z, out var t) && t) return t;
            int W = 512, H = Mathf.Max(64, Mathf.RoundToInt(512 * (z.h + PlinthM * 2) / (z.w + PlinthM * 2)));
            var px = new Color32[W * H];
            var g0 = C("#78ad5c"); var g1 = C("#68a052"); var g2 = C("#86b866"); var dirt = C("#b49a74"); var dirt2 = C("#a38960");
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.035f, y * 0.035f), n2 = Mathf.PerlinNoise(x * 0.11f + 5, y * 0.11f);
                    var c = n > 0.62f ? g1 : (n2 > 0.7f ? g2 : g0);
                    float e = Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y)) / (float)W * (z.w + PlinthM * 2);   // 离外沿的米数
                    float path = Mathf.Abs(e - 0.018f - (Mathf.PerlinNoise(x * 0.02f, y * 0.02f) - 0.5f) * 0.006f);
                    px[y * W + x] = c;
                }
            var rng = new Rng((uint)(z.w * 100));
            for (int i = 0; i < W * H / 900; i++) { int x = rng.Int(1, W - 2), y = rng.Int(1, H - 2); px[y * W + x] = C("#9b9184"); px[y * W + x + 1] = C("#c2b8aa"); }
            t = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "terrain_" + z.id, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            t.SetPixels32(px); t.Apply(true);
            terrainTex[z] = t;
            return t;
        }

        Material Mat(Texture2D tex, string name, float outline = 0.0024f)
        {
            var m = new Material(CityMaterials.Toon) { name = name, enableInstancing = true };
            m.SetTexture("_BaseMap", tex); m.SetFloat("_OutlineWidth", outline);
            return m;
        }

        /// <summary>真实桌子上的城市地垫（11-11 用户：识别到真桌子时也要有城市地面，不然露出桌面很难看）：
        /// 厚 4mm（城市整体抬高 4mm 坐在垫子上，见 RoomTable.ToZone），顶面和虚拟沙盘一样的草地 / 铺地，四周地层剖面，外沿一圈深色细压边</summary>
        void BuildCityMat(Zone z)
        {
            const float T = 0.004f, M = 0.036f;   // 地垫比城区每边多 3.6cm（盖住路沿和步行道）
            var root = FurnitureRoot(z);
            root.SetPositionAndRotation(z.Center, z.Rotation); root.localScale = Vector3.one;
            float W = z.w + M * 2, D = z.h + M * 2;
            var strata = Mat(StrataTex(), "strata", 0);
            Part(root, CityMeshes.Box(Mathf.Max(1, Mathf.RoundToInt(W / 0.25f)), Mathf.Max(1, Mathf.RoundToInt(D / 0.25f))), strata, new Vector3(0, -T, 0), new Vector3(W, T - 0.0036f, D), "MatStrata");
            var terrain = Mat(TerrainTex(z), "terrain", 0);
            Part(root, CityMeshes.Box(), terrain, new Vector3(0, -0.0032f, 0), new Vector3(W - 0.0002f, 0.0004f, D - 0.0002f), "MatTerrain").GetComponent<MeshRenderer>().receiveShadows = true;
            var rim = PM("#1c1626", 0.0012f);
            float fr = 0.004f;
            foreach (var (pos, size) in new[] {
                (new Vector3(0, -T, D / 2 + fr / 2), new Vector3(W + fr * 2, T + 0.0015f, fr)), (new Vector3(0, -T, -D / 2 - fr / 2), new Vector3(W + fr * 2, T + 0.0015f, fr)),
                (new Vector3(W / 2 + fr / 2, -T, 0), new Vector3(fr, T + 0.0015f, D)), (new Vector3(-W / 2 - fr / 2, -T, 0), new Vector3(fr, T + 0.0015f, D)) })
                Part(root, CityMeshes.Box(), rim, pos, size, "MatRim");
        }

        void BuildFurniture(Zone z)
        {
            if (!z.furniture) { BuildCityMat(z); return; }
            var root = FurnitureRoot(z);
            root.SetPositionAndRotation(z.Center, z.Rotation);   // 本地：+z = v（朝玩家），x 沿 u
            root.localScale = Vector3.one;
            float W = z.w + PlinthM * 2, D = z.h + PlinthM * 2;
            var wood = Mat(WoodTex(), "walnut"); var woodDark = PM("#4a2c1c", 0.0022f); var brass = PM("#d6a84a", 0.0012f);
            // ── 沙盘底座 ──
            var strata = Mat(StrataTex(), "strata", 0);
            Part(root, CityMeshes.Box(Mathf.Max(1, Mathf.RoundToInt(W / 0.25f)), Mathf.Max(1, Mathf.RoundToInt(D / 0.25f))), strata, new Vector3(0, -Plinth, 0), new Vector3(W, Plinth - 0.0036f, D), "PlinthStrata");
            var terrain = Mat(TerrainTex(z), "terrain", 0);   // 城里是草地；外圈的灰色步行道由 CityEdge 铺
            // 草地顶面比城区地面低 1.4mm（城区地面网格离地 0.4mm），中间留足深度间隔，远看也不会闪
            Part(root, CityMeshes.Box(), terrain, new Vector3(0, -0.0032f, 0), new Vector3(W - 0.0002f, 0.0004f, D - 0.0002f), "Terrain").GetComponent<MeshRenderer>().receiveShadows = true;
            // 木框：四条压边（上沿略高于草地 3mm）
            float fr = 0.012f;
            foreach (var (pos, size) in new[] {
                (new Vector3(0, -Plinth, D / 2 + fr / 2), new Vector3(W + fr * 2, Plinth + 0.003f, fr)), (new Vector3(0, -Plinth, -D / 2 - fr / 2), new Vector3(W + fr * 2, Plinth + 0.003f, fr)),
                (new Vector3(W / 2 + fr / 2, -Plinth, 0), new Vector3(fr, Plinth + 0.003f, D)), (new Vector3(-W / 2 - fr / 2, -Plinth, 0), new Vector3(fr, Plinth + 0.003f, D)) })
                Part(root, CityMeshes.Box(), woodDark, pos, size, "PlinthFrame");
            // 正面一块铜牌
            Part(root, CityMeshes.Box(), brass, new Vector3(0, -Plinth * 0.78f, D / 2 + fr + 0.0008f), new Vector3(0.14f, Plinth * 0.5f, 0.0015f), "Plaque");
            float top = -Plinth;                    // 家具顶面（本地 y）
            float floorY = -z.origin.y;            // 本地地面高度（房间地面在世界 y=0）
            float OW = W + fr * 2 + 0.05f, OD = D + fr * 2 + 0.05f;   // 每边外沿 0.04 + 0.012 + 0.025 = 0.077m（布局按 0.08 留余量）
            if (z.kind == ZoneKind.Table)
            {
                // 桌面：厚 3.5cm + 下沿收进的一道线脚
                Part(root, CityMeshes.Box(Mathf.Max(1, Mathf.RoundToInt(OW / 0.6f)), 2), wood, new Vector3(0, top - 0.035f, 0), new Vector3(OW, 0.035f, OD), "TableTop");
                Part(root, CityMeshes.Box(), woodDark, new Vector3(0, top - 0.045f, 0), new Vector3(OW - 0.03f, 0.01f, OD - 0.03f), "TopMoulding");
                // 围板 + 抽屉（正面三个，铜把手）
                float apY = top - 0.045f - 0.09f, legIn = 0.05f;
                Part(root, CityMeshes.Box(4, 2), wood, new Vector3(0, apY, 0), new Vector3(OW - legIn * 2 - 0.04f, 0.09f, OD - legIn * 2 - 0.04f), "Apron");
                float dw = (OW - 0.4f) / 3;
                for (int i = 0; i < 3; i++)
                {
                    float x = -OW / 2 + 0.2f + dw * (i + 0.5f);
                    Part(root, CityMeshes.Box(), woodDark, new Vector3(x, apY + 0.012f, (OD - legIn * 2 - 0.04f) / 2 + 0.002f), new Vector3(dw - 0.03f, 0.066f, 0.004f), "Drawer");
                    Part(root, CityMeshes.Box(), brass, new Vector3(x, apY + 0.04f, (OD - legIn * 2 - 0.04f) / 2 + 0.006f), new Vector3(0.05f, 0.01f, 0.008f), "Handle");
                }
                // 四条桌腿：方腿往下收细（三段）+ 铜脚套
                float legTop = apY + 0.09f, legH = legTop - floorY;
                foreach (var sx in new[] { -1, 1 }) foreach (var sz in new[] { -1, 1 })
                {
                    var p = new Vector3(sx * (OW / 2 - legIn), floorY, sz * (OD / 2 - legIn));
                    Part(root, CityMeshes.Box(), wood, p + Vector3.up * (legH * 0.55f), new Vector3(0.06f, legH * 0.45f, 0.06f), "Leg");
                    Part(root, CityMeshes.Box(), wood, p + Vector3.up * 0.04f, new Vector3(0.048f, legH * 0.51f, 0.048f), "LegLower");
                    Part(root, CityMeshes.Box(), brass, p, new Vector3(0.054f, 0.04f, 0.054f), "Foot");
                }
                // 腿间横撑（离地 15cm，H 形）
                foreach (var sx in new[] { -1, 1 })
                    Part(root, CityMeshes.Box(), woodDark, new Vector3(sx * (OW / 2 - legIn), floorY + 0.15f, 0), new Vector3(0.03f, 0.03f, OD - legIn * 2), "Stretcher");
                Part(root, CityMeshes.Box(), woodDark, new Vector3(0, floorY + 0.15f, 0), new Vector3(OW - legIn * 2, 0.03f, 0.03f), "StretcherMid");
            }
            else
            {
                // 柜子：顶板 + 柜身 + 上面两个抽屉 + 下面两扇门（门板有凹框）+ 踢脚
                Part(root, CityMeshes.Box(3, 2), wood, new Vector3(0, top - 0.03f, 0), new Vector3(OW, 0.03f, OD), "CabTop");
                float bodyTop = top - 0.03f, bodyH = bodyTop - floorY - 0.08f;
                var body = Part(root, CityMeshes.Box(3, 2), wood, new Vector3(0, floorY + 0.08f, 0), new Vector3(OW - 0.04f, bodyH, OD - 0.04f), "CabBody");
                Part(root, CityMeshes.Box(), woodDark, new Vector3(0, floorY, 0), new Vector3(OW - 0.07f, 0.08f, OD - 0.07f), "Plinth");
                float fz = (OD - 0.04f) / 2 + 0.002f, cw = (OW - 0.1f) / 2;
                for (int i = 0; i < 2; i++)
                {
                    float x = -OW / 2 + 0.05f + cw * (i + 0.5f);
                    Part(root, CityMeshes.Box(), woodDark, new Vector3(x, bodyTop - 0.16f, fz), new Vector3(cw - 0.02f, 0.13f, 0.006f), "Drawer");
                    Part(root, CityMeshes.Box(), brass, new Vector3(x, bodyTop - 0.1f, fz + 0.005f), new Vector3(0.07f, 0.012f, 0.01f), "Handle");
                    float dh = bodyH - 0.24f;
                    Part(root, CityMeshes.Box(), woodDark, new Vector3(x, floorY + 0.1f, fz), new Vector3(cw - 0.02f, dh, 0.006f), "Door");
                    Part(root, CityMeshes.Box(), wood, new Vector3(x, floorY + 0.14f, fz + 0.003f), new Vector3(cw - 0.09f, dh - 0.08f, 0.006f), "DoorPanel");
                    Part(root, CityMeshes.Box(), brass, new Vector3(x + (i == 0 ? 1 : -1) * (cw / 2 - 0.04f), floorY + 0.1f + dh * 0.55f, fz + 0.008f), new Vector3(0.012f, 0.08f, 0.01f), "Knob");
                }
            }
        }
    }
}
