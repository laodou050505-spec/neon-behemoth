// 结局（11-14 用户通关后：“游戏要有个结局，想想怎么做”；第二版：“能量回收的效果不好看”“把结局做好”）。
// 最后一区（第 9 区）的封印塔全倒、胜利过程放完以后，不出普通的结算纸，接这一段（约 27 秒）。全部发生在桌上的城市和真实房间里，不切场景、不放过场图：
//   0.4s  熄灯     —— 以最后一座塔为圆心，全城的窗灯、霓虹、路灯一圈一圈灭掉（着色器全局量 _Blackout），电站的护盾罩跟着碎；
//   4.5s  力量回来 —— 每座塔的废墟里冲起一根光束，越往上越细、顺势弯向玩家，几根光束汇到玩家胸前，聚成一颗越来越大的能量球（两道环绕着它转）
//                    → 能量球一收、扑进玩家胸口：一圈光环炸开、视野柔和地亮一下、两只手上各亮起一圈光环；这一刻生命和怒气回满，巨兽之手亮起来；
//   12s   涨潮     —— 水从桌面漫上来，淹过街道和车，还在烧的楼被浇灭；之后水面随“呼吸”慢慢起伏；
//   17.5s 亮灯     —— 黑着的城里，窗户一格一格自己亮起来（只有窗灯，集团的霓虹不再亮）：城市开始自己发光；
//   20s   天亮     —— 天幕从夜色换成清晨，太阳升出地平线，压暗真实房间的那层天幕变淡：房间亮回来；
//   27.5s 最后一份公告 —— 集团的停业公告（纸），上面手写“欢迎回家，深潮”：回到标题 / 再战最后一区。
// 四句旁白（黄底墨边的旁白卡，和开场漫画同一种）立在城市远边的上空，一句一句打出来。看过一次结局以后，捏一下可以跳到公告。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Monster.Game
{
    public class Ending : MonoBehaviour
    {
        public MonsterGame game; public GameView view; public Camera cam; public MonsterInput input; public int level;
        public Action<int> onLevel; public Action onTitle;
        const float TLights = 0.4f, TPower = 4.5f, TTide = 12f, TRelight = 17.5f, TDawn = 20f, TEnd = 27.5f;
        public float t;   // 公开：编辑器验收时可以直接拨时间
        bool seen, paperShown, wasPinch; Vector3 origin; float reach, blackR; int capI = -2, cue;
        DuskSky sky; Light sun; Color sunC0; float sunI0; Color amb0, amb1, amb2; bool gotLight;
        readonly List<(Transform t, Material m, float y0)> floods = new();
        readonly List<Material> rivers = new();
        readonly List<(Vector3 p, Vector3 n, float t)> bursts = new();
        Vector3 gather, chest; bool gathered;
        Caption cap;
        static readonly Color Cy = new(0.06f, 0.6f, 0.95f), Pk = new(0.95f, 0.1f, 0.55f);   // 和能量波同一套青 / 粉（深一档：特效着色器会提亮）

        void Start()
        {
            if (!cam) cam = Camera.main;
            Progress.Unlocked = level + 1; Progress.SetStars(level, game.Rating());
            seen = Progress.EndingSeen;
            origin = view.WinAt;
            reach = 0.5f; foreach (var b in game.city.buildings) reach = Mathf.Max(reach, Vector3.Distance(b.Base, origin));
            reach += 0.5f;
            view.powerOff = p => Vector3.Distance(p, origin) < blackR;
            sky = GetComponent<DuskSky>();
            sun = RenderSettings.sun; if (!sun) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) sun = l;
            if (sun) { sunC0 = sun.color; sunI0 = sun.intensity; }
            amb0 = RenderSettings.ambientSkyColor; amb1 = RenderSettings.ambientEquatorColor; amb2 = RenderSettings.ambientGroundColor; gotLight = true;
            // 河面（已有的）：天亮时把“夜里”的样子退掉
            foreach (var r in view.builder.root.GetComponentsInChildren<MeshRenderer>(true))
                if (r.sharedMaterial && r.sharedMaterial.shader && r.sharedMaterial.shader.name == "Monster/CityWater") rivers.Add(r.sharedMaterial);
            // 涨潮用的水面：每片有陆地的城区一张（比城区每边大 2.5cm，四边渐隐）
            var wsh = Shader.Find("Monster/CityWater");
            if (wsh)
                foreach (var z in game.city.zones)
                {
                    if (z.harbor) continue;
                    const float M = 0.025f;
                    var m = new Mesh { name = "flood" };
                    var vs = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>(); const int Seg = 12;
                    for (int i = 0; i <= Seg; i++)
                    {
                        float f = (float)i / Seg, a = Mathf.Lerp(-M, z.w + M, f);
                        vs.Add(z.Point(a, -M) - z.origin); vs.Add(z.Point(a, z.h + M) - z.origin);
                        uvs.Add(new Vector2(f, 0)); uvs.Add(new Vector2(f, 1));
                        if (i < Seg) { int k = i * 2; tris.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
                    }
                    m.SetVertices(vs); m.SetUVs(0, uvs); m.SetTriangles(tris, 0); m.RecalculateNormals();
                    if (Vector3.Dot(m.normals[0], Vector3.up) < 0) { for (int i = 0; i < tris.Count; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]); m.SetTriangles(tris, 0); m.RecalculateNormals(); }
                    m.RecalculateBounds();
                    var mat = new Material(wsh) { name = "flood" };
                    mat.SetFloat("_Len", z.w + M * 2); mat.SetFloat("_Wid", z.h + M * 2); mat.SetFloat("_Flood", 1); mat.SetFloat("_Stripes", 0); mat.SetFloat("_Night", 1); mat.SetFloat("_Opacity", 0);
                    var go = new GameObject("Flood_" + z.id); go.transform.SetParent(view.builder.root, false); go.transform.position = z.origin;
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var rr = go.AddComponent<MeshRenderer>(); rr.sharedMaterial = mat; rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    go.SetActive(false);
                    floods.Add((go.transform, mat, z.origin.y));
                }
            // 旁白卡：主城区远边的上空，正对玩家
            {
                var z = game.city.zones[0]; var head = cam.transform.position;
                float a = Mathf.Clamp(Vector3.Dot(head - z.origin, z.u), Mathf.Min(0.25f, z.w * 0.5f), Mathf.Max(z.w - 0.25f, z.w * 0.5f));
                var p = z.Point(a, 0.02f);
                p.y = Mathf.Min(Mathf.Max(MonsterMR.CityTopY, z.origin.y + 0.2f) + 0.075f, head.y + 0.1f);
                cap = new Caption(p, cam);
            }
        }

        void OnDestroy()
        {
            Shader.SetGlobalVector("_Blackout", Vector4.zero); Shader.SetGlobalFloat("_Relight", 0);
            if (view) view.powerOff = null;
            if (gotLight)
            {
                if (sun) { sun.color = sunC0; sun.intensity = sunI0; }
                RenderSettings.ambientSkyColor = amb0; RenderSettings.ambientEquatorColor = amb1; RenderSettings.ambientGroundColor = amb2;
            }
            cap?.Destroy();
        }

        void Cue(int n, Action a) { if (cue < n) { cue = n; a(); } }
        static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }

        void Update()
        {
            if (game == null || !view || cap == null) return;   // 编辑器里播放中改脚本会把这些引用清掉
            float dt = Time.deltaTime; t += dt;
            // 看过一次结局以后：捏一下跳到最后的公告（跳的时候不补放中间的音效）
            bool pinch = false; var gi = input && input.Active ? input.Read() : null;
            if (gi?.hands != null) foreach (var h in gi.hands) if (h != null && h.present && h.pinch) pinch = true;
            if (seen && !paperShown && t > 1.5f && t < TEnd && pinch && !wasPinch) { t = TEnd; cue = 99; game.hp = Cfg.HpMax; game.rage = Cfg.RageMax; foreach (var b in game.city.buildings) b.fire = 0; }
            wasPinch = pinch;
            bool fx = !view.paused;

            // ① 熄灯
            float k1 = Mathf.Clamp01((t - TLights) / 5f);
            blackR = t > TLights ? reach * Mathf.Pow(k1, 1.6f) : 0;   // 先慢后快：面前这张桌子上的灯是一圈一圈慢慢灭的（前 2.5 秒只扩出去 80cm），远处的城区最后一下带过
            Shader.SetGlobalVector("_Blackout", new Vector4(origin.x, origin.y, origin.z, blackR));
            if (t > TLights) Cue(1, () => MonsterAudio.Play("sfx_blackout", origin, 0.9f));

            // ② 力量回来
            float P = t - TPower;
            if (P > 0 && P < 8f && fx) Power(P, gi);
            if (P > 5.6f) { game.hp = Mathf.MoveTowards(game.hp, Cfg.HpMax, dt * 70); game.rage = Mathf.MoveTowards(game.rage, Cfg.RageMax, dt * 70); }
            if (P > 0) Cue(2, () => { MonsterAudio.Music("music_title"); MonsterAudio.Play("sfx_mech_charge", origin, 0.6f, 0.55f); });
            if (P > 1.3f) Cue(3, () => MonsterAudio.Play("sfx_neon", null, 0.5f, 0.8f));
            if (P > 4.9f) Cue(4, () => MonsterAudio.Play("sfx_mech_charge", null, 0.5f, 1.3f));
            if (P > 5.6f) Cue(5, () =>
            {
                // 扑进胸口的那一刻：一圈圈光环朝玩家炸开、视野柔和地亮一下、轻轻震一下
                var n = (cam.transform.position - chest).normalized;
                for (int i = 0; i < 3; i++) bursts.Add((chest + n * 0.05f, n, -i * 0.12f));
                view.OverlayPulse(0.42f); view.Shake(0.22f);
                MonsterAudio.Play("sfx_heal", null, 0.9f, 0.7f); MonsterAudio.Play("sfx_star_up", null, 0.6f, 0.6f);
            });
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                var (bp, bn, bt) = bursts[i]; bt += dt; if (bt > 0.9f) { bursts.RemoveAt(i); continue; }
                bursts[i] = (bp, bn, bt); if (bt <= 0 || !fx) continue;
                float x = bt / 0.9f, r = Mathf.Lerp(0.04f, 0.5f, 1 - (1 - x) * (1 - x)); var c = i % 2 == 0 ? Cy : Pk; c.a = (1 - x) * 0.8f;
                view.FxRingQ(bp, Quaternion.FromToRotation(Vector3.up, bn), new Vector3(r, r * 0.35f, r), c);
            }

            // ③ 涨潮（之后随呼吸起伏：5 秒一个来回）
            float k3 = Mathf.SmoothStep(0, 1, (t - TTide) / 5f), k4 = Mathf.SmoothStep(0, 1, (t - TDawn) / 6.5f);
            if (t > TTide) Cue(6, () => MonsterAudio.Play("amb_tide", null, 0.8f));
            if (t > TTide + 9.5f) Cue(9, () => MonsterAudio.Play("amb_tide", null, 0.6f));
            // 潮水漫过来：还在烧的楼被浇灭，各冒一团白汽
            if (t > TTide + 1.8f) Cue(7, () =>
            {
                foreach (var b in game.city.buildings)
                {
                    if (b.h <= 0 || b.fire <= 0) continue;
                    b.fire = 0; b.burn = 0;
                    var top = MonsterGame.TopOf(b);
                    for (int i = 0; i < 5; i++) view.FxSmoke(top + UnityEngine.Random.insideUnitSphere * 0.015f, new Vector3(UnityEngine.Random.Range(-0.012f, 0.012f), UnityEngine.Random.Range(0.05f, 0.09f), UnityEngine.Random.Range(-0.012f, 0.012f)), 0.03f, 1.3f, new Color(0.93f, 0.95f, 0.97f));
                }
                MonsterAudio.Play("sfx_shield", origin, 0.35f, 0.6f);
            });
            float sc = view.builder != null ? view.builder.scale : 0.7f;
            foreach (var (ft, fm, y0) in floods)
            {
                if (ft.gameObject.activeSelf != k3 > 0) ft.gameObject.SetActive(k3 > 0);
                if (k3 <= 0) continue;
                float y = Mathf.Lerp(0.0002f, 0.0125f * sc / 0.7f, k3) + 0.0016f * Mathf.Sin((t - TTide) * 1.26f) * k3;
                ft.position = new Vector3(ft.position.x, y0 + y, ft.position.z);
                fm.SetFloat("_Opacity", Mathf.Clamp01(k3 * 3) * 0.72f); fm.SetFloat("_Night", 1 - k4);
            }

            // ④ 亮灯：窗户一格一格自己亮起来（5.5 秒亮完）
            Shader.SetGlobalFloat("_Relight", Mathf.Clamp01((t - TRelight) / 5.5f));
            if (t > TRelight) Cue(8, () => MonsterAudio.Play("sfx_star_up", null, 0.3f, 0.45f));

            // ⑤ 天亮
            if (k4 > 0)
            {
                if (sky) sky.dawn = k4;
                if (sun) { sun.color = Color.Lerp(sunC0, new Color(1f, 0.87f, 0.7f), k4); sun.intensity = sunI0 * Mathf.Lerp(1, 2.5f, k4); }   // 对局里楼体受光压得很暗（见 DuskSky）：天亮时拉回到白天的样子
                float ak = Mathf.Lerp(1, 2.1f, k4); var warm = new Color(1.3f, 1.1f, 0.85f);
                RenderSettings.ambientSkyColor = Color.Lerp(amb0, amb0 * ak * warm, k4); RenderSettings.ambientEquatorColor = Color.Lerp(amb1, amb1 * ak * warm, k4); RenderSettings.ambientGroundColor = Color.Lerp(amb2, amb2 * ak, k4);
                foreach (var m in rivers) m.SetFloat("_Night", 1 - k4);
            }

            // 旁白
            int ci = t < 1f ? -1 : t < TPower + 1.0f ? 0 : t < TTide + 0.8f ? 1 : t < TRelight + 0.8f ? 2 : t < TEnd ? 3 : -1;
            if (ci != capI) { capI = ci; cap.Show(ci >= 0 ? StoryText.Ending[ci].text : null); }
            cap.Tick(dt);

            // 最后一份公告
            if (t >= TEnd && !paperShown)
            {
                paperShown = true; Progress.EndingSeen = true;
                var es = gameObject.AddComponent<EndingScreen>(); es.cam = cam; es.input = input; es.level = level; es.onLevel = onLevel; es.onTitle = onTitle;
            }
        }

        /// <summary>
        /// 力量回来（P = 这一段开始后过了几秒）：
        ///   0～2.3s   每座塔的废墟冲起一根光束（粉色外层 + 青色中层 + 白芯），先笔直往上、再弯向玩家胸前的汇聚点，柱脚一圈圈光环往外荡；
        ///   2.0～4.9s 汇聚点上的能量球越长越大：白芯、青色球体、粉色光晕，两道环交叉着绕它转，零星的光点被吸进去；
        ///   4.9～5.6s 光带从塔那一头收走，能量球一缩、扑进玩家胸口（越来越快）；
        ///   5.6～7.6s 两只手上各一圈转动的光环 + 一团光（追踪到手的话）。
        /// </summary>
        void Power(float P, GameInput gi)
        {
            var head = cam.transform.position; var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (!gathered || P < 1.2f) { gathered = true; gather = head + fwd * 0.42f + Vector3.down * 0.2f; chest = head + fwd * 0.04f + Vector3.down * 0.27f; }   // 光带开始流之后汇聚点就定住，不跟着头晃
            float tm = Time.time;
            // 能量球此刻在哪、多大
            float grow = Ease((P - 2.0f) / 2.6f), dive = Mathf.Clamp01((P - 4.9f) / 0.7f); dive *= dive;
            var orb = Vector3.Lerp(gather, chest, dive); float orbR = Mathf.Lerp(0.022f, 0.07f, grow) * (1 - 0.55f * dive);
            int ti = 0;
            foreach (var tw in game.city.towers)
            {
                var p0 = tw.Base + Vector3.up * 0.004f;
                // 11-16 用户：光柱和光带的衔接不好 —— 上一版是“一根笔直的粗光柱 + 柱顶另接一条细光带”，粗细、方向在接头处都断了一下。
                // 现在是同一根光束：从废墟笔直冲起来，越往上越细，顺势弯向玩家胸前的能量球（三次曲线：起点的切线竖直向上，终点从上方落进能量球）；
                // 粉色外层、青色中层、白芯三层从头到尾都在，粗细从柱脚的 3.4cm 连续收到末端的 1.2cm；光束像脉搏一样顺着流向一鼓一鼓。
                // 生长：0～2.3 秒从柱脚一路长到能量球；收尾：4.3 秒起从柱脚那一头收走。
                float gHead = Ease(P / 2.3f), gTail = Ease((P - 4.3f) / 0.7f);
                var side = Vector3.Cross(Vector3.up, (orb - p0).normalized) * (0.07f * Mathf.Sin(ti * 2.4f + 1));
                var c1 = p0 + Vector3.up * (0.34f + 0.03f * (ti % 3));                                  // 竖直往上
                var c2 = orb + Vector3.up * 0.26f + side + (p0 - orb).normalized * 0.1f;               // 从上方、略偏外侧落进能量球
                Vector3 B(float u) { float v = 1 - u; return v * v * v * p0 + 3 * v * v * u * c1 + 3 * v * u * u * c2 + u * u * u * orb; }
                if (gHead > 0 && gTail < 1)
                {
                    const int N = 26;
                    for (int i = Mathf.FloorToInt(gTail * N); i < Mathf.CeilToInt(gHead * N); i++)
                    {
                        float u0 = Mathf.Max(gTail, (float)i / N), u1 = Mathf.Min(gHead, (float)(i + 1) / N); if (u1 <= u0) continue;
                        float um = (u0 + u1) * 0.5f, pulse = 0.8f + 0.35f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(um * 11 - tm * 8 + ti), 3);
                        float w = Mathf.Lerp(0.034f, 0.012f, Mathf.SmoothStep(0, 1, um * 1.25f)) * pulse;   // 柱脚粗、往上连续收细
                        var q0 = B(u0); var q1 = B(u1); q1 += (q1 - q0) * 0.1f;                             // 每节多伸一点，节与节之间不露缝
                        var co = Pk; co.a = 0.5f; view.FxTube(q0, q1, w, co, 2);
                        var cm = Cy; cm.a = 0.88f; view.FxTube(q0, q1, w * 0.6f, cm, 1);
                        view.FxTube(q0, q1, w * 0.22f, Color.white, 0);
                    }
                    if (gHead < 1) { var hp = B(gHead); view.FxBlob(hp, 0.03f, Color.white); var hc = Cy; hc.a = 0.5f; view.FxBlob(hp, 0.055f, hc); }   // 光束的头：一颗亮点带着它往前走
                    // 柱脚：光环一圈圈往外荡，细小的光点绕着光束往上升
                    float foot = 1 - gTail;
                    for (int j = 0; j < 2; j++) { float f = Mathf.Repeat(P * 0.9f + j * 0.5f + ti * 0.2f, 1); var rc = j == 0 ? Cy : Pk; rc.a = (1 - f) * 0.8f * foot; float rr = Mathf.Lerp(0.02f, 0.075f, f); view.FxRingQ(p0 + Vector3.up * 0.004f, Quaternion.identity, new Vector3(rr, rr * 0.2f, rr), rc); }
                    for (int j = 0; j < 4; j++) { float f = Mathf.Repeat(P * 1.1f + j * 0.25f + ti * 0.13f, 1) * Mathf.Min(gHead, 0.4f), a = j * 1.57f + tm * 3 + ti; var bp = B(f); view.FxBlob(bp + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.022f, 0.008f * foot, Color.white); }
                }
                ti++;
            }
            // 能量球
            if (P > 1.9f && P < 5.62f)
            {
                var halo = Pk; halo.a = 0.3f; view.FxBlob(orb, orbR * 2.6f, halo);
                var body = Cy; body.a = 0.8f; view.FxBlob(orb, orbR * 1.5f * (1 + 0.05f * Mathf.Sin(tm * 6)), body);
                view.FxBlob(orb, orbR * 0.8f, Color.white);
                var r1 = Cy; r1.a = 0.9f; var r2 = Pk; r2.a = 0.9f; float rr = orbR * 1.55f;
                view.FxRingQ(orb, Quaternion.Euler(tm * 150, tm * 60, 20), new Vector3(rr, rr, rr), r1);
                view.FxRingQ(orb, Quaternion.Euler(-tm * 120, tm * 40 + 90, 70), new Vector3(rr * 1.15f, rr * 0.8f, rr * 1.15f), r2);
                if (dive <= 0)
                    for (int j = 0; j < 8; j++)
                    {
                        float f = Mathf.Repeat(tm * 0.9f + j / 8f, 1); var d = Quaternion.Euler(j * 47, j * 83 + tm * 20, 0) * Vector3.forward;
                        var c = j % 2 == 0 ? Cy : Color.white; c.a = f; view.FxBlob(orb + d * (orbR * 3.2f * (1 - f) + orbR), 0.006f * (0.5f + f), c);
                    }
            }
            // 两只手上的光环
            float aura = Mathf.Clamp01((P - 5.6f) / 0.3f) * Mathf.Clamp01((7.8f - P) / 0.8f);
            if (aura > 0 && gi?.hands != null)
                foreach (var h in gi.hands)
                {
                    if (h == null || !h.present) continue;
                    var c = Cy; c.a = 0.9f * aura; float rr = 0.055f + 0.006f * Mathf.Sin(tm * 4);
                    view.FxRingQ(h.wrist, Quaternion.FromToRotation(Vector3.up, h.fwd) * Quaternion.Euler(0, tm * 200, 0), new Vector3(rr, rr * 0.6f, rr), c);
                    var g = Pk; g.a = 0.22f * aura; view.FxBlob(h.palm, 0.11f, g);
                }
        }

        /// <summary>旁白卡：黄底墨边（和开场漫画的旁白框同一种），立在世界里、面朝玩家；换句子时缩下去再弹出来，字一个一个打出来</summary>
        class Caption
        {
            readonly Transform root, box, ink, shadow; readonly TextMeshPro text; readonly Camera cam; readonly int q;
            string cur, next; float pop, typed; bool hiding;
            const float W = 0.6f, Size = 0.034f, Pad = 0.022f;   // 立在一米多远的城市远边上：字要比手边的纸大（一行约 17 个字，占视野 28° 左右）
            public Caption(Vector3 pos, Camera cam)
            {
                this.cam = cam;
                root = new GameObject("EndingCaption").transform; root.position = pos; root.localScale = Vector3.one * 0.001f;
                var s = new Sheet(root, "Card", 3250);
                shadow = s.Rect(new Vector2(0.005f, -0.006f), Vector2.one, new Color(0, 0, 0, 0.35f));
                ink = s.Rect(Vector2.zero, Vector2.one, Ink.Black);
                box = s.Rect(Vector2.zero, Vector2.one, new Color(1f, 0.86f, 0.3f));
                var go = new GameObject("T"); go.transform.SetParent(root, false); go.transform.localPosition = new Vector3(0, 0, -0.002f); go.transform.localScale = Vector3.one * 0.01f;
                text = go.AddComponent<TextMeshPro>(); text.alignment = TextAlignmentOptions.Center; text.enableWordWrapping = true; text.color = Ink.Black; text.fontSize = Size * 900;
                text.rectTransform.pivot = new Vector2(0.5f, 0.5f); text.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                q = 3254;
                root.gameObject.SetActive(false);
            }
            public void Show(string zh) { next = zh; hiding = cur != null; if (cur == null && zh != null) Begin(); }
            void Begin()
            {
                cur = next; next = null; hiding = false; if (cur == null) { root.gameObject.SetActive(false); return; }
                string s = Loc.T(cur); if (Loc.En) s = s.ToUpperInvariant();
                text.font = Resources.Load<TMP_FontAsset>(Loc.En ? "Fonts/ComicEN SDF" : "Fonts/ComicZH SDF") ?? Ink.Font(Ink.Face.Corp); text.fontMaterial.renderQueue = q;
                text.text = s; text.rectTransform.sizeDelta = new Vector2(W * 100, 10);
                var pv = text.GetPreferredValues(s, W * 100, 0);
                float w = Mathf.Min(W, pv.x * 0.01f) + Pad * 2.4f, h = pv.y * 0.01f + Pad * 2;
                text.rectTransform.sizeDelta = new Vector2(W * 100, pv.y + 1);
                box.localScale = new Vector3(w, h, 1); ink.localScale = new Vector3(w + 0.005f, h + 0.005f, 1); shadow.localScale = new Vector3(w + 0.005f, h + 0.005f, 1);
                text.maxVisibleCharacters = 0; typed = 0; pop = 0;
                root.gameObject.SetActive(true);
                MonsterAudio.Play("ui_page", root.position, 0.5f);
            }
            public void Tick(float dt)
            {
                if (hiding) { pop -= dt / 0.18f; if (pop <= 0) { cur = null; Begin(); } }
                else if (cur != null) { pop = Mathf.Min(1, pop + dt / 0.3f); typed += dt * 24; text.maxVisibleCharacters = Mathf.FloorToInt(typed); }
                if (!root.gameObject.activeSelf) return;
                float x = Mathf.Clamp01(pop); const float c = 1.6f; float e = hiding ? x : 1 + (c + 1) * Mathf.Pow(x - 1, 3) + c * Mathf.Pow(x - 1, 2);
                root.localScale = Vector3.one * Mathf.Max(0.001f, e);
                var to = root.position - cam.transform.position; to.y *= 0.5f;
                if (to.sqrMagnitude > 1e-4f) root.rotation = Quaternion.LookRotation(to, Vector3.up) * Quaternion.Euler(0, 0, -1.2f);
            }
            public void Destroy() { if (root) UnityEngine.Object.Destroy(root.gameObject); }
        }
    }

    // ═══════════ 最后一份公告：集团的停业公告，被手写上“欢迎回家” ═══════════
    public class EndingScreen : NoticeScreen
    {
        public int level; public Action<int> onLevel; public Action onTitle;
        readonly List<(Transform t, Vector3 s)> pops = new();
        void Start()
        {
            Setup("EndingScreen", NearD, EyeDrop, 0.62f);
            var paper = NewSheet("Paper", 3100);
            // 按纸上印的版心排（同简报 / 结算）：字从左版心线往里起，页眉在黑条正中，正文在右版心线之前折行
            var pc = new Vector2(0, -0.02f); const float PH = 0.6f, PW = PH * 1175f / 1600f, PR = -1.5f;
            paper.Img("paper_notice", pc, PH, PR, null, PW);
            float XL(float y) => Sheet.PaperX(pc, PW, PR, Sheet.TextL + 0.006f, y);
            float colW = (Sheet.RuleR - Sheet.TextL - 0.025f) * PW;
            float barY = Sheet.PaperY(pc, PH, PR, Sheet.BarMid, XL(0.22f));
            paper.Fit(paper.Text("霓虹动力集团 · 最后一份公告", Ink.Face.Corp, new Vector2(XL(barY), barY), 0.018f, Ink.Paper, PR), colW);
            paper.Fit(paper.Text("通缉撤销", Ink.Face.Corp, new Vector2(XL(0.13f), 0.13f), 0.042f, Ink.Black, PR), colW);
            const string Body = "九座城区的封印塔全部失守。本集团即日起停止营业。对代号“深潮”的通缉，就此撤销。";
            var bodyT = paper.Text(Body, Ink.Face.Corp, new Vector2(XL(0.05f), 0.05f), 0.0175f, Ink.Gray, PR, TextAlignmentOptions.Left, colW);
            float bodyH = bodyT.GetPreferredValues(bodyT.text, colW * 100, 0).y * 0.01f, bodyTop = 0.092f, bodyBot = bodyTop - bodyH;
            bodyT.transform.localPosition = new Vector3(XL(bodyTop - bodyH / 2), bodyTop - bodyH / 2, bodyT.transform.localPosition.z);
            int total = 0, max = (Level.All.Length - 1) * 3; for (int i = 1; i < Level.All.Length; i++) total += Progress.Stars(i);
            float starY = bodyBot - 0.03f;
            paper.Text(Loc.F("全部星星：{0} / {1}", total, max), Ink.Face.Corp, new Vector2(XL(starY), starY), 0.016f, Ink.Black, PR);
            // 选项
            float oy = Mathf.Min(starY - 0.055f, -0.1f);
            menu.items.Add(new NoticeItem(paper, "回到标题", new Vector2(XL(oy) + 0.004f, oy), 0.032f, 0.16f, () => onTitle?.Invoke(), PR));
            menu.items.Add(new NoticeItem(paper, "再战最后一区", new Vector2(XL(oy - 0.05f) + 0.004f, oy - 0.05f), 0.032f, 0.2f, () => onLevel?.Invoke(level), PR));
            // 涂鸦层（都放在纸面的空处，不压公文的字）：“自由！”抬到页眉黑条上沿之外；爪印像盖章一样拍在正文和选项之间的右侧空白；
            // 选项下面一行手写“欢迎回家，深潮。”；右下角一只水母
            var graf = NewSheet("Graffiti", 3300);
            shouts.Add(new Shout(graf, "自由！", new Vector2(0.105f, 0.297f), 0.07f, Ink.Pink, -8, 0.19f));
            var cp = graf.Img("claw_print", new Vector2(0.09f, Mathf.Min(starY - 0.035f, -0.03f)), 0.085f, -24, new Color(1, 1, 1, 0.9f)); pops.Add((cp, cp.localScale)); cp.localScale = Vector3.zero;
            graf.Fit(graf.Text("欢迎回家，深潮。", Ink.Face.Note, new Vector2(XL(oy - 0.105f) + 0.075f, oy - 0.105f), 0.036f, Ink.Pink, 3), 0.21f);   // 让开左下角印的圆章
            graf.Img("doodle_jelly", new Vector2(0.2f, -0.255f), 0.075f, 10);
            graf.Img("tape_a", new Vector2(-0.17f, 0.275f), 0.03f, 28); graf.Img("tape_c", new Vector2(0.17f, 0.278f), 0.03f, -24);
            glitches.Add(new Glitch(graf, pc, new Vector2(0.42f, 0.58f), 4));
            Relabel();
        }
        protected override void Update()
        {
            base.Update();
            const float c = 1.7f;
            for (int i = 0; i < pops.Count; i++)
            {
                float k = Mathf.Clamp01((t - 0.6f - i * 0.3f) / 0.28f);
                pops[i].t.localScale = pops[i].s * Mathf.Max(0, 1 + (c + 1) * Mathf.Pow(k - 1, 3) + c * Mathf.Pow(k - 1, 2));
            }
        }
    }
}
