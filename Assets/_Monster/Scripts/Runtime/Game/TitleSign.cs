// 标题：立在真实房间地板上的一块 3D 霓虹广告牌（MR，不出现游戏场景）。
// 结构：两根立柱落地 + 斜撑 + 底座钢板；牌子是 7cm 厚的铁皮灯箱，四周钢框 + 铆钉，下面一条白色桁架；
// 灯箱字离底板 4.5cm，中间 6 层深色切片当字的侧面（有厚度、转头有视差）；底板上一层加色光晕；
// 顶上一排跑马灯泡 + 三盏鹅颈射灯。入场：从上方砸到地上（落地闷响、轻弹两下），然后灯管从左到右一个字一个字通电。
// 贴图：Resources/Title/sign_panel|letters|glow_<zh|en>（由标题图拆出来，见 Tools/split_title_sign.py）。
using System.Collections.Generic;
using UnityEngine;

namespace Monster.Game
{
    public class TitleSign : MonoBehaviour
    {
        public Camera cam; public float floorY;
        public float lift;   // 招牌整体再抬高多少（标题菜单纸因为桌子被抬高时跟着抬，免得纸压住招牌下沿；搭之前设好）
        public bool Landed => t > DropT;
        public float Lit => Mathf.Clamp01((t - LightT) / LightDur);   // 0..1：灯管通电进度

        // 安全区（原地模式实测半径 0.95m）：招牌最远的底座钢板离头水平 0.73 + 0.096 + 0.07 ≈ 0.90m，整块都在圈内
        const float PW = 0.74f, PH = PW / 2.212f, Dist = 0.73f, DropT = 0.55f, LightT = 0.85f, LightDur = 1.5f;
        Transform root, body; float t = -1, cy;
        Material panelM, lettersM, sliceM, glowM, glowFrontM;
        readonly List<Material> sliceMats = new();
        readonly List<Renderer> bulbs = new(); MaterialPropertyBlock mpb; float nextBuzz;
        bool built;

        void Update()
        {
            if (!cam) cam = Camera.main;
            if (!built)
            {
                // 头显：等头部追踪起来（高度正常）再摆，不然会摆在地上
                if (!cam || cam.transform.position.y - floorY < 0.9f) return;
                built = true; Build(); t = 0;
            }
            float dt = Time.deltaTime; t += dt;
            // ── 入场：从 1.1m 高处落下（越落越快），落地后轻弹两下 ──
            float y;
            if (t < DropT) { float k = t / DropT; y = 1.1f * (1 - k * k); }
            else
            {
                float k = t - DropT;
                y = -0.018f * Mathf.Sin(k * 26) * Mathf.Exp(-k * 7);
                if (!thud) { thud = true; MonsterAudio.Play("sfx_land", body.position, 0.7f); }
            }
            body.localPosition = new Vector3(0, y, 0);
            body.localRotation = Quaternion.Euler(0, 0, t > DropT ? Mathf.Sin((t - DropT) * 19) * Mathf.Exp(-(t - DropT) * 6) * 1.2f : 0);
            // ── 通电：从左到右扫过去，扫的时候接触不良地闪 ──
            float lit = Lit, reveal = lit >= 1 ? 2 : lit * 1.08f - (Mathf.PerlinNoise(t * 9, 1.3f) > 0.6f ? 0.12f : 0);
            if (lit > 0 && nextBuzz == 0) { nextBuzz = 1; MonsterAudio.Play("sfx_neon", body.position, 0.6f); }
            // 通电后：偶尔整块闪一下（霓虹接触不良）
            float flick = lit >= 1 && Mathf.PerlinNoise(t * 2.6f, 7.1f) > 0.84f ? 0.7f : 1f;
            lettersM.SetFloat("_Reveal", reveal); lettersM.color = new Color(flick, flick, flick, 1);
            glowM.SetFloat("_Reveal", reveal); glowM.color = Color.white * 0.85f * flick;
            glowFrontM.SetFloat("_Reveal", reveal); glowFrontM.color = Color.white * 0.28f * flick;
            // 铭牌不发光：没通电时很暗，招牌亮起来以后被带亮（跟着接触不良一起轻微闪）
            if (plateFaceM) { float pk = Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0, 1, lit)) * Mathf.Lerp(1, flick, 0.35f); plateFaceM.color = new Color(pk, pk, pk, 1); }
            // 跑马灯：每三颗一组往右跑；通电前全灭
            mpb ??= new MaterialPropertyBlock();
            int step = Mathf.FloorToInt(t * 7);
            for (int i = 0; i < bulbs.Count; i++)
            {
                bool on = lit > (float)i / bulbs.Count && (lit < 1 || (i + step) % 3 != 0);
                mpb.SetColor("_Color", on ? new Color(1.6f, 1.25f, 0.7f) : new Color(0.28f, 0.22f, 0.16f));
                bulbs[i].SetPropertyBlock(mpb);
            }
        }
        bool thud;

        public void SetLanguage(bool en)
        {
            if (!built) return;
            string s = en ? "en" : "zh";
            panelM.mainTexture = Resources.Load<Texture2D>("Title/sign_panel_" + s);
            var lt = Resources.Load<Texture2D>("Title/sign_letters_" + s);
            lettersM.mainTexture = lt; foreach (var m in sliceMats) m.mainTexture = lt;
            var gt = Resources.Load<Texture2D>("Title/sign_glow_" + s);
            glowM.mainTexture = gt; glowFrontM.mainTexture = gt;
            if (plateFaceM) plateFaceM.mainTexture = Resources.Load<Texture2D>("Brand/plate_" + s);
        }

        void OnDestroy() { if (root) Destroy(root.gameObject); }

        // ═══════════ 搭模型 ═══════════
        void Build()
        {
            var h = cam.transform;
            var fwd = Vector3.ProjectOnPlane(h.forward, Vector3.up); if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
            root = new GameObject("TitleSign").transform;
            root.position = new Vector3(h.position.x, floorY, h.position.z) + fwd * Dist;
            root.rotation = Quaternion.LookRotation(fwd, Vector3.up);   // 本地 −Z 朝玩家
            body = new GameObject("Body").transform; body.SetParent(root, false);
            cy = h.position.y - floorY + 0.21f + lift;   // 牌子中心比眼睛高 21cm：抬眼看招牌，低头看菜单（11-17 从 19 抬到 21：桁架上的出品铭牌最下面一行字原来被菜单纸上沿压住）
            float top = cy + PH / 2, bot = cy - PH / 2;

            var steel = CityMaterials.Solid("#565c74"); var dark = CityMaterials.Solid("#262836"); var rivet = CityMaterials.Solid("#9aa0b8");
            var white = CityMaterials.Solid("#d4d6e2"); var post = CityMaterials.Solid("#3a3f52");
            var unlit = Shader.Find("Monster/ComicCard"); var cut = Shader.Find("Monster/SignCut"); var glow = Shader.Find("Monster/SignGlow");

            // 灯箱铁皮盒（前面在 z=0）
            Box(body, dark, new Vector3(0, bot - 0.02f, 0.035f), new Vector3(PW + 0.06f, PH + 0.04f, 0.07f));
            // 底板（字被抹掉的深色灯箱面）
            panelM = new Material(unlit) { name = "signPanel", renderQueue = 2450 };
            Quad(body, panelM, new Vector3(0, cy, -0.0015f), new Vector2(PW, PH));
            // 底板光晕（字后面那圈灯光，打在底板上）
            glowM = new Material(glow) { name = "signGlow", renderQueue = 3000 };
            Quad(body, glowM, new Vector3(0, cy, -0.003f), new Vector2(PW, PH));
            // 字的侧面：6 层切片，越靠前越亮一点（像灯箱字的金属侧边受了一点光）
            for (int i = 0; i < 6; i++)
            {
                float k = i / 5f;
                var m = new Material(cut) { name = "signSlice" + i };
                m.color = Color.Lerp(new Color(0.1f, 0.07f, 0.14f), new Color(0.24f, 0.16f, 0.3f), k);
                m.SetFloat("_Cutoff", 0.5f);
                sliceMats.Add(m);
                Quad(body, m, new Vector3(0, cy, -0.008f - k * 0.034f), new Vector2(PW, PH));
            }
            // 灯箱字正面
            lettersM = new Material(cut) { name = "signLetters" }; lettersM.SetFloat("_Cutoff", 0.5f); lettersM.SetFloat("_Dark", 0.18f);
            Quad(body, lettersM, new Vector3(0, cy, -0.045f), new Vector2(PW, PH));
            // 灯管表面的一点泛光（叠在字前面）
            glowFrontM = new Material(glow) { name = "signGlowFront", renderQueue = 3001 };
            Quad(body, glowFrontM, new Vector3(0, cy, -0.047f), new Vector2(PW, PH));

            // 钢框 + 铆钉
            const float F = 0.032f;
            Box(body, steel, new Vector3(0, top, -0.012f), new Vector3(PW + 2 * F, F, 0.05f));
            Box(body, steel, new Vector3(0, bot - F, -0.012f), new Vector3(PW + 2 * F, F, 0.05f));
            foreach (var sx in new[] { -1, 1 }) Box(body, steel, new Vector3(sx * (PW + F) / 2, bot - F, -0.012f), new Vector3(F, PH + 2 * F, 0.05f));
            for (float yy = bot; yy <= top; yy += 0.06f)
                foreach (var sx in new[] { -1, 1 }) Sphere(body, rivet, new Vector3(sx * (PW + F) / 2, yy, -0.038f), 0.008f);

            // 下面一条白色三角桁架（上弦是钢框下边）
            float tb = bot - F - 0.07f;
            Box(body, white, new Vector3(0, tb, -0.012f), new Vector3(PW + 2 * F, 0.012f, 0.014f));
            int nTr = 16; float span = PW + F;
            for (int i = 0; i < nTr; i++)
            {
                float x0 = -span / 2 + span * i / nTr, x1 = -span / 2 + span * (i + 1) / nTr;
                var a = new Vector3(x0, i % 2 == 0 ? tb + 0.012f : bot - F, -0.012f);
                var b = new Vector3(x1, i % 2 == 0 ? bot - F : tb + 0.012f, -0.012f);
                Strut(body, white, a, b, 0.009f);
            }

            BuildPlate((tb + bot - F) / 2);

            // 两根立柱落地（在灯箱背后），一道横撑 + 一道斜撑，地上压钢板
            float px = PW * 0.32f, pz = 0.096f;   // 立柱贴着灯箱背面（背面 z=0.07，柱半径 2.2cm）
            foreach (var sx in new[] { -1, 1 })
            {
                Cyl(body, post, new Vector3(sx * px, 0, pz), 0.044f, top - 0.02f);
                Box(body, dark, new Vector3(sx * px, 0, pz), new Vector3(0.14f, 0.012f, 0.14f));
                // 立柱到灯箱背面的托架
                Box(body, steel, new Vector3(sx * px, cy - 0.015f, (0.07f + pz) / 2), new Vector3(0.05f, 0.03f, pz - 0.07f + 0.004f));
            }
            Box(body, post, new Vector3(0, bot * 0.42f, pz), new Vector3(px * 2, 0.022f, 0.022f));
            Strut(body, post, new Vector3(-px, 0.08f, pz), new Vector3(px, bot * 0.42f, pz), 0.016f);

            // 跑马灯泡：镶在上、下框的正面（站着从下往上看，放框顶上会被框挡住）。上排从左往右、下排从右往左，绕一圈
            var bulbM = new Material(unlit) { name = "bulb", renderQueue = 2460 };
            var row = new List<Renderer>();
            for (float x = -PW / 2 + 0.01f; x <= PW / 2 - 0.005f; x += 0.044f)
            {
                var sock = Cyl(body, dark, new Vector3(x, top + F / 2, -0.037f), 0.02f, 0.006f); sock.localRotation = Quaternion.Euler(-90, 0, 0);
                bulbs.Add(Sphere(body, bulbM, new Vector3(x, top + F / 2, -0.046f), 0.02f).GetComponent<Renderer>());
                sock = Cyl(body, dark, new Vector3(x, bot - F / 2, -0.037f), 0.02f, 0.006f); sock.localRotation = Quaternion.Euler(-90, 0, 0);
                row.Add(Sphere(body, bulbM, new Vector3(x, bot - F / 2, -0.046f), 0.02f).GetComponent<Renderer>());
            }
            row.Reverse(); bulbs.AddRange(row);
            // 三盏鹅颈射灯：从框顶伸出来，灯头朝下照着字
            foreach (var x in new[] { -0.27f, 0f, 0.27f })
            {
                var p0 = new Vector3(x, top + F, 0.0f); var p1 = new Vector3(x, top + F + 0.07f, -0.06f); var p2 = new Vector3(x, top + F + 0.05f, -0.13f);
                Strut(body, post, p0, p1, 0.008f); Strut(body, post, p1, p2, 0.008f);
                var head = Box(body, dark, p2 + new Vector3(0, -0.02f, 0), new Vector3(0.04f, 0.03f, 0.05f));
                head.localRotation = Quaternion.Euler(-35, 0, 0);
                // 灯口一圈暖光
                var lens = new Material(unlit) { name = "lamp", color = new Color(1.4f, 1.2f, 0.85f), renderQueue = 2460 };
                var q = Quad(body, lens, p2 + new Vector3(0, -0.024f, -0.012f), new Vector2(0.032f, 0.032f));
                q.localRotation = Quaternion.Euler(55, 0, 0);
            }

            foreach (var r in root.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
            SetLanguage(Loc.En);
            lettersM.SetFloat("_Reveal", -1); glowM.SetFloat("_Reveal", -1); glowFrontM.SetFloat("_Reveal", -1);
            body.localPosition = Vector3.up * 1.1f;
        }

        // ═══════════ 出品铭牌（11-17 用户：标题处加“Tripo XR 出品”，中英都要；位置定在招牌钢架上）═══════════
        // 像真广告牌下面钉的制造商铭牌：一块深色搪瓷铁牌，挂在游戏名正下方那条白桁架的正中，钢边、四角铆钉、背后两根挂条搭在桁架上下弦上。
        // 牌面是一张做好的图（Resources/Brand/plate_zh|en，由 Tools/brand/render.sh 合成）：公司标志用官方原图，不改色、不涂鸦、不拉伸、四周留白；
        // 官方标志只有 TRIPO（没有“TRIPO XR”这个标志）；标志下面另起一行字：中文“TRIPO XR 出品”，英文“TRIPO XR DEVELOPERS”（用户定的），随语言切换。
        // 不发光：招牌没通电时牌面很暗，通电以后被灯箱带亮（Lit），比游戏名小得多、暗一档，不抢标题。没有牌面图就整块不建
        public const float PlH = 0.056f, PlW = PlH * 1600 / 496, PlZ = -0.024f;   // 牌面高 / 宽（跟贴图同比例）/ 牌面所在的 z（桁架前面 1cm）
        Transform plate; Material plateFaceM;
        void BuildPlate(float y)
        {
            if (!Resources.Load<Texture2D>("Brand/plate_zh")) return;
            plate = new GameObject("MakerPlate").transform; plate.SetParent(body, false); plate.localPosition = new Vector3(0, y, 0);
            var enamel = CityMaterials.Solid("#1b1c26"); var edge = CityMaterials.Solid("#565c74"); var bolt = CityMaterials.Solid("#9aa0b8");
            const float E = 0.004f;   // 钢边宽
            Box(plate, enamel, new Vector3(0, -PlH / 2, PlZ + 0.003f), new Vector3(PlW, PlH, 0.006f));   // 牌身（Box 的原点在底面中心）
            Box(plate, edge, new Vector3(0, PlH / 2, PlZ + 0.002f), new Vector3(PlW + 2 * E, E, 0.008f));
            Box(plate, edge, new Vector3(0, -PlH / 2 - E, PlZ + 0.002f), new Vector3(PlW + 2 * E, E, 0.008f));
            foreach (var sx in new[] { -1, 1 })
            {
                Box(plate, edge, new Vector3(sx * (PlW + E) / 2, -PlH / 2, PlZ + 0.002f), new Vector3(E, PlH, 0.008f));
                foreach (var sy in new[] { -1, 1 }) Sphere(plate, bolt, new Vector3(sx * (PlW / 2 - 0.006f), sy * (PlH / 2 - 0.006f), PlZ - 0.001f), 0.0045f);
                // 背后的挂条：左右各一根竖扁钢，上端搭在钢框下边、下端搭在桁架下弦上，牌子就挂在这两根上（夹在牌子和桁架之间，不和牌面共面）
                Box(plate, edge, new Vector3(sx * PlW * 0.3f, -0.041f, -0.020f), new Vector3(0.010f, 0.082f, 0.005f));
            }
            // 牌面：不受光的贴图，贴在牌身前 0.4mm（牌身是不透明的，牌面在它之后画，不会闪）
            plateFaceM = new Material(Shader.Find("Monster/ComicCard")) { name = "makerPlate", renderQueue = 2470, color = new Color(0.25f, 0.25f, 0.25f, 1) };
            Quad(plate, plateFaceM, new Vector3(0, 0, PlZ - 0.0004f), new Vector2(PlW, PlH));
        }

        static Transform Box(Transform p, Material m, Vector3 pos, Vector3 size) => Part(p, CityMeshes.Box(), m, pos, size, "Box");
        static Transform Sphere(Transform p, Material m, Vector3 center, float d) => Part(p, CityMeshes.Sphere(10, 6), m, center - Vector3.up * d / 2, Vector3.one * d, "Bolt");
        static Transform Cyl(Transform p, Material m, Vector3 pos, float d, float h) => Part(p, CityMeshes.Cylinder(14), m, pos, new Vector3(d, h, d), "Post");
        static Transform Strut(Transform p, Material m, Vector3 a, Vector3 b, float th)
        {
            var t = Part(p, CityMeshes.Box(), m, a, new Vector3(th, (b - a).magnitude, th), "Strut");
            t.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            return t;
        }
        static Transform Part(Transform p, Mesh mesh, Material m, Vector3 pos, Vector3 scale, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(p, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }
        static Transform Quad(Transform p, Material m, Vector3 center, Vector2 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(p, false);
            go.transform.localPosition = center; go.transform.localScale = new Vector3(size.x, size.y, 1);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }
    }
}
