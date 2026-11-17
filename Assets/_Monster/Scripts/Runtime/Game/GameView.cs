// 玩法表现层：每帧把输入交给 MonsterGame，取出事件做表现，再按逻辑状态更新画面。逻辑层不知道 Unity 物体的存在。
// 倒塌：先把这栋楼从合批网格里拿出来（CityBuilder.Detach），被拆掉的那几层复制一份做“倒下来的那一截”，原物体隐藏；
//   横推且断两层以上 → 以断口为轴整段倒下（越倒越快），落地后每层带着自己的招牌、楼顶设备翻滚着散开；其余 → 原地往下塌碎。
// 碎块 / 烟 / 火 / 爆炸 / 电线光点 / 炮弹全部走 FxBatch 实例化绘制。
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Monster.Game
{
    public class GameView : MonoBehaviour
    {
        public MonsterGame game; public CityBuilder builder; public MonsterInput input;
        public System.Func<GameInput> simInput;   // 编辑器里用鼠标模拟（DeskSim）
        public float shake;
        public bool paused;   // 开场漫画期间：不推进玩法

        FxBatch blobs, cones, boxes, rings; Material fxMat; PowFx pow;
        static readonly Color Ink = new(0.13f, 0.08f, 0.18f);
        static readonly Color Dust = new(0.80f, 0.75f, 0.68f), Smoke = new(0.23f, 0.20f, 0.25f);
        static readonly Color[] FireCol = { new(0.95f, 0.18f, 0.05f), new(1f, 0.5f, 0.05f), new(1f, 0.86f, 0.25f) };   // 楼着火：外橙红 / 中橙 / 芯黄
        static readonly Color[] Flame = { new(1f, 0.35f, 0.12f), new(1f, 0.69f, 0.16f), new(1f, 0.94f, 0.48f) };
        MaterialPropertyBlock mpb;   // 不能在字段初始化里 new（MonoBehaviour 构造阶段不允许），Init 里建

        class Piece
        {
            public Transform t; public Vector3 vel, spin; public float age, floor, life; public Zone zone; public bool rest; public Building b;
            public Renderer[] rs;                                  // 落地按真实包围盒算（只看原点会插进地面）
            public Pile pile;
        }
        class Falling { public Transform pivot; public Quaternion baseRot; public Vector3 axis, dir; public float ang, w, len; public Building b; public int n; public List<Transform> storeys; }
        class Puff { public Vector3 pos, vel; public float age, life, size, grow; public Color col; public float glow; public bool cartoon; }
        class Burst { public Vector3 pos; public float r, age, life; public string kind; public Vector3[] dirs; }
        readonly List<Piece> pieces = new();
        readonly List<Falling> falling = new();
        readonly List<Puff> puffs = new();
        readonly List<Burst> bursts = new();
        readonly Dictionary<int, float> smokeCd = new();
        readonly Dictionary<int, float> burnt = new();
        readonly Dictionary<object, Transform> carried = new();   // 被拔起 / 扔出去的楼
        Transform fxRoot;

        public void Init(MonsterGame g, CityBuilder b)
        {
            game = g; builder = b;
            mpb = new MaterialPropertyBlock();
            var mat = new Material(Shader.Find("Monster/FxPuff")) { enableInstancing = true, name = "fx" }; fxMat = mat;
            blobs = new FxBatch(FxBatch.Blob(), mat); cones = new FxBatch(FxBatch.Cone(), mat); boxes = new FxBatch(FxBatch.Box(), mat); rings = new FxBatch(FxBatch.Ring(), mat);
            fxRoot = new GameObject("GameFx").transform; fxRoot.SetParent(b.root, false);
            pow = gameObject.GetComponent<PowFx>() ?? gameObject.AddComponent<PowFx>();   // 漫画拟声大字
            g.TakeProp = TakeProp;   // 捏起路上的车
            g.CrushProp = CrushProps;   // 拍扁树 / 车
            overlay = gameObject.GetComponent<HurtOverlay>() ?? gameObject.AddComponent<HurtOverlay>(); overlay.game = g; overlay.cam = Camera.main;
            overlay.onNearDeath = () =>
            {
                // 进入濒死的那一下：眼前一句提示 + 一声低沉的警示；场上没有回血球的话当场在面前掉一个（给一条活路）
                pow?.ShowText(Loc.T("濒死！找回血球"), game.headPos + cam0.forward * 0.55f + Vector3.down * 0.06f, new Color(1f, 0.25f, 0.3f), 1.8f, 15, 2.4f, true);
                MonsterAudio.Play("sfx_down", null, 0.5f, 1.4f);
            };
        }

        void Update()
        {
            if (game == null || paused) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            Perf.Begin("输入"); var gi = input && input.Active ? input.Read() : simInput?.Invoke();
            Perf.Begin("逻辑"); game.Tick(dt, gi);
            Perf.Begin("事件"); foreach (var ev in game.DrainEvents()) Handle(ev);
            Perf.Begin("楼"); UpdateBuildings(dt);
            UpdateFalling(dt);
            Perf.Begin("碎块"); UpdatePieces(dt); UpdateFlats(dt);
            UpdateRegrow(dt);
            UpdateCarried(dt);
            Perf.Begin("线和弹"); DrawLines();
            DrawProjectiles();
            Perf.Begin("大招"); DrawBreath();
            TickVoice(dt);
            Traffic.Keep = game.tut != null ? 1 : Mathf.Clamp01(1 - 0.2f * game.stars);   // 星级越高普通车越少（开出去的不再回来）
            // 开战后全城的车陆续停在路上。时机：开场广播 1.5 秒响起，“巨型不明生物已进入市区，全市一级戒备”这句说完（中文约第 8 秒、英文约第 9.5 秒）才开始停
            if (!Traffic.Halt && game.time > (game.tut != null ? 6f : Loc.En ? 9.5f : 8.3f)) Traffic.Halt = true;
            Perf.Begin("电站等"); UpdateVictory(dt); UpdateQuakes(dt); DrawStations(); UpdateJolts(dt); UpdateStationFx(dt); DrawOrbs(); DrawNuke(); DrawHandGauge(gi); UpdateThreats(dt); hurtFlash = Mathf.Max(0, hurtFlash - Time.deltaTime * 2);
            Perf.Begin("烟"); UpdatePuffs(dt);
            UpdateBursts(dt);
            shake = Mathf.Max(0, shake - dt * 2.5f);
            Perf.Begin("提交特效"); blobs.Draw(); cones.Draw(); boxes.Draw(); rings.Draw();
            Perf.End();
            Perf.Count("追兵", game.enemies.Count); Perf.Count("弹", game.projectiles.Count); Perf.Count("瓦砾", rubs.Count); Perf.Count("烟", puffs.Count); Perf.Count("碎块", pieces.Count); Perf.Count("火花", shards.Count);
            int burning = 0; foreach (var b in game.city.buildings) if (b.h > 0 && b.fire > 0) burning++; Perf.Count("着火", burning);
        }

        // ═══════════ 事件 ═══════════
        public void Handle(Ev ev)
        {
            MonsterAudio.OnEvent(ev, game); VoiceEvent(ev);
            switch (ev.type)
            {
                case "collapse": OnCollapse(ev); break;
                case "ignite": { var v = builder.View(ev.id); if (v) { builder.Detach(v); builder.QueueRemerge(v); } break; }
                case "regrow": Regrow(ev.id); break;
                case "station_on":
                    if (ev.cause == "repair") { var v = builder.View(ev.id); if (v) { builder.Unmerge(v); foreach (var s in v.storeys) s.gameObject.SetActive(true); } }
                    else if (ev.cause == "restart") { Regrow(ev.id); buildFx[ev.id] = 0; MonsterAudio.Play("sfx_build", ev.pos, 0.8f); var sb = B(game, ev.id); if (sb != null) pow?.Show("zap", sb.Base + Vector3.up * 0.07f); }
                    break;
                case "armor_hit": OnArmorHit(ev); break;
                case "station_prep": if (ev.pos.HasValue) { MonsterAudio.Play("sfx_build", ev.pos, 0.6f, 0.8f); var pb = B(game, ev.id); clearings.Add((ev.pos.Value, (pb != null ? pb.Foot : 0.07f) * 0.8f, 0)); } break;
                case "lose": overlay?.Die(); if (ev.cause == "hp") MonsterAudio.Play("sfx_down", null, 0.7f); break;
                case "nuke_launch": nukeBeep = 0; nukeDive = false; nukeOut = false; pow?.ShowText(Loc.T("危险！危险！危险！"), game.headPos + cam0.forward * 0.6f + cam0.up * 0.06f, new Color(1f, 0.92f, 0.25f), 2.6f, 26, 3.2f); break;
                case "nuke_hit": if (nukeModel) nukeModel.gameObject.SetActive(false); overlay?.Flash(); MonsterAudio.Play("sfx_blast", null, 1f, 0.55f); break;
                case "rage_full":
                    // 11-12 用户：不知道大招能用了 —— 怒气满的那一刻：眼前弹一句“大招就绪！双掌并拢往前推”（停 2.5 秒）+ 提示音；手背的怒气条和牌子上的怒气条变粉
                    MonsterAudio.Play("sfx_star_up", null, 0.8f, 1.5f);
                    pow?.ShowText(Loc.T("大招就绪！双掌并拢往前推"), game.headPos + cam0.forward * 0.55f + Vector3.down * 0.1f, new Color(1f, 0.45f, 0.8f), 2.5f, 11, 2.6f); break;
                case "orb": if (ev.pos.HasValue) { AddDust(ev.pos.Value - Vector3.up * 0.06f, 0.02f); MonsterAudio.Play("ui_press", ev.pos, 0.7f, 1.6f); } break;
                case "orb_take": OnOrbTake(ev); break;
                case "fire" when ev.kind == "fist": onEnemyEvent?.Invoke(ev); { var me = game.enemies.FirstOrDefault(x => x.id == ev.id); if (me != null) fired.Add((me.pos, 0.9f)); } break;
                case "fire": { var fe = game.enemies.FirstOrDefault(x => x.id == ev.id); if (fe != null) { fired.Add((fe.pos, 0.9f)); flashes.Add((fe.muzzle ?? fe.pos + Vector3.up * 0.03f, 0)); MonsterAudio.Play("sfx_shot", fe.pos, ev.kind == "bullet" ? 0.45f : 0.7f, ev.kind == "bullet" ? 1.3f : ev.kind == "missile" ? 0.6f : 0.85f); } break; }
                case "hurt": OnHurt(ev); break;
                case "blast": AddBlast(ev.pos ?? Vector3.zero, ev.r); break;
                case "chip": OnChip(ev); break;
                case "ricochet": if (ev.pos.HasValue) OnRicochet(ev); break;
                case "parry": if (ev.pos.HasValue) pow?.Show("smack", ev.pos.Value + Vector3.up * 0.03f); break;
                case "tower_down": { var tb = B(game, ev.id); if (tb != null) pow?.Show("tower", tb.Base + Vector3.up * 0.14f); break; }
                case "impact": AddDust(ev.pos ?? Vector3.zero, 0.03f * Mathf.Max(1, ev.n)); break;
                case "shield_hit": case "shield_break":
                    {
                        var sh = Shield(ev.id); if (sh) sh.sharedMaterial.SetFloat("_Hit", 1);
                        // 光束被护盾挡住：命中点溅一圈火花、一个环；每 2.5 秒提醒一次“先断电”（不然玩家以为大招坏了）
                        if (ev.cause == "breath" && ev.pos.HasValue)
                        {
                            Sparks(ev.pos.Value, 8, new Color(0.5f, 0.95f, 1f), 0.3f); rings.Add(ev.pos.Value, Quaternion.identity, Vector3.one * 0.03f, new Color(0.15f, 0.8f, 0.9f, 0.8f), 1);
                            if (Time.time > beamBlockTold) { beamBlockTold = Time.time + 2.5f; MonsterAudio.Play("sfx_shield", ev.pos, 0.8f); pow?.ShowText(Loc.T("护盾挡住了！先断电"), ev.pos.Value + Vector3.up * 0.1f, new Color(0.45f, 1f, 0.95f), 1.2f, 13, 1.6f, true); }
                        }
                        break;
                    }
                case "uproot": OnUproot(ev); break;
                case "seal_break": OnSealBreak(ev); break;
                case "beam_hit":
                    if (ev.pos.HasValue)
                    {
                        // 大招命中：命中点炸开，碎片和火星顺着光束方向喷出去
                        var bp = ev.pos.Value; var bd = ev.dir ?? Vector3.forward;
                        AddBlast(bp, 0.06f); AddDust(bp, 0.04f); shake = Mathf.Max(shake, 0.35f);
                        for (int i = 0; i < 16; i++) shards.Add(new Shard { p = bp, v = bd * Random.Range(0.3f, 0.8f) + Random.insideUnitSphere * 0.25f + Vector3.up * 0.2f, r = Random.rotation, spin = Random.insideUnitSphere * 16, s = new Vector3(0.008f, 0.0016f, 0.004f), life = Random.Range(0.35f, 0.6f), c = i % 3 == 0 ? Color.white : i % 3 == 1 ? new Color(1f, 0.45f, 0.8f) : new Color(0.4f, 0.95f, 1f) });
                    }
                    break;
                case "clap":
                    if (ev.pos.HasValue) { pow?.Show("smack", ev.pos.Value + Vector3.up * 0.05f); Sparks(ev.pos.Value, 14, new Color(1f, 0.85f, 0.3f), 0.35f); AddDust(ev.pos.Value, 0.03f); shake = Mathf.Max(shake, 0.2f); }
                    break;
                case "quake":
                    if (ev.pos.HasValue)
                    {
                        // 双手砸地：两道贴着桌面扩出去的震波 + 一圈尘 + 震屏
                        quakes.Add((ev.pos.Value, ev.r, 0)); shake = Mathf.Max(shake, 0.7f);
                        AddDust(ev.pos.Value + Vector3.up * 0.004f, 0.07f);
                        for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4; AddDust(ev.pos.Value + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * ev.r * 0.6f + Vector3.up * 0.004f, 0.03f); }
                        pow?.Show("boom", ev.pos.Value + Vector3.up * 0.1f);
                    }
                    break;
                case "fizzle": if (ev.pos.HasValue) { AddDust(ev.pos.Value, 0.018f); blobs.Add(ev.pos.Value, 0.02f, new Color(1f, 0.9f, 0.6f, 0.8f), 1); } break;
                case "leave": onLeave?.Invoke(ev.id); break;
                case "mech_warn" when ev.pos.HasValue: pow?.ShowText(Loc.T("机甲空降！"), game.headPos + cam0.forward * 0.6f + cam0.up * 0.07f, new Color(1f, 0.3f, 0.4f), 1.6f, 18, 2.6f, true); MonsterAudio.Play("sfx_star_up", null, 0.8f, 0.7f); break;
                case "mech_fall": case "mech_land": case "mech_charge": case "mech_fizzle": case "mech_block": case "mech_stagger": case "mech_hurt": case "mech_break": case "mech_slam": onEnemyEvent?.Invoke(ev); break;
                case "tower_piece":
                    // 最后一座塔还攥在手里：提醒把它扔掉（塔的残骸不落地，封印不算解除）
                    pow?.ShowText(Loc.T("把塔扔出去！"), game.headPos + cam0.forward * 0.55f + Vector3.down * 0.08f, new Color(0.45f, 1f, 0.95f), 2.6f, 14, 2.6f, true); break;
                case "nuke_abort":
                    // 塔全倒了，核弹失去引导：发动机熄火，弹体翻滚着掉下去摔碎
                    if (nukeModel && nukeModel.gameObject.activeSelf) { AddBlast(nukePos, 0.04f); MonsterAudio.Play("sfx_clang", nukePos, 0.8f, 0.7f); AddWreck(nukeModel, Vector3.up * 0.2f); nukeModel = null; }
                    break;
            }
        }

        // ═══════════ 胜利过程（11-13 用户：胜利不能一下子就结束，要有过程）═══════════
        // 逻辑层确认“塔真的全没了”之后发 seal_break，到真正结束（出结算）之间有 Cfg.WinShow 秒：
        //   0s    最后一座塔的塔基炸开一圈，三道青色冲击波贴着桌面一圈圈扩出去；眼前“封印解除！”；背景乐淡出、一声“断电 + 上扬和弦”；
        //   0.6s  广播：“封印塔全部失守……全员撤离”；每座塔的废墟上，被封住的能量化成光点螺旋着升上天；
        //   0.9s～ 追兵一辆接一辆撤走（飞的掉头飞远，地上的被一道光柱吊走）；牌子上的核打击倒计时换成“封印解除”、通缉星一颗颗熄灭；
        //   5.4s  结束，结算纸弹出来。
        public System.Action<int> onLeave;   // 追兵撤离（EnemyView 接：不变成残骸，而是飞走 / 被吊走）
        float winFx = -1; Vector3 winAt; bool winVo; float beamBlockTold;
        public Vector3 WinAt => winFx >= 0 ? winAt : game.city.zones[0].Center;   // 最后一座塔的位置（结局的熄灯从这里往外扩）
        public System.Func<Vector3, bool> powerOff;                              // 结局：这个位置已经断电了吗（电站的护盾罩跟着碎掉）
        static readonly Color SealCyan = new(0.35f, 1f, 0.95f), SealPink = new(1f, 0.45f, 0.8f);
        void OnSealBreak(Ev ev)
        {
            winFx = 0; winVo = false; winAt = ev.pos ?? game.city.zones[0].Center;
            MonsterAudio.Music(null);
            MonsterAudio.Play("sfx_win", null, 0.9f);
            AddBlast(winAt + Vector3.up * 0.03f, 0.09f); AddDust(winAt + Vector3.up * 0.004f, 0.09f);
            pow?.ShowText(Loc.T("封印解除！"), game.headPos + cam0.forward * 0.6f + cam0.up * 0.05f, SealCyan, 3.2f, 24, 3f, true);
        }
        readonly List<(Vector3 p, float r, float t)> quakes = new();
        void UpdateQuakes(float dt)
        {
            for (int i = quakes.Count - 1; i >= 0; i--)
            {
                var (p, r, t) = quakes[i]; t += dt; if (t > 0.7f) { quakes.RemoveAt(i); continue; }
                quakes[i] = (p, r, t);
                for (int j = 0; j < 2; j++)
                {
                    float x = (t - j * 0.12f) / 0.5f; if (x <= 0 || x >= 1) continue;
                    float rr = Mathf.Lerp(0.03f, r, 1 - (1 - x) * (1 - x));
                    var c = j == 0 ? new Color(0.95f, 0.85f, 0.7f) : new Color(0.85f, 0.2f, 0.55f); c.a = (1 - x) * 0.7f;
                    rings.Add(p + Vector3.up * (0.006f + j * 0.004f), Quaternion.identity, new Vector3(rr, rr * 0.12f, rr), c, 1);
                }
            }
        }
        void UpdateVictory(float dt)
        {
            if (winFx < 0) return;
            winFx += dt; float t = winFx;
            if (!winVo && t > 0.6f) { winVo = true; if (game.tut == null) MonsterAudio.Voice("win", true); }
            // 冲击波：扁平的光带贴着桌面扩出去，越远越淡
            for (int i = 0; i < 3; i++)
            {
                float k = (t - i * 0.35f) / 1.5f; if (k <= 0 || k >= 1) continue;
                float r = Mathf.Lerp(0.03f, 0.7f, 1 - (1 - k) * (1 - k));
                var c = i == 1 ? new Color(0.85f, 0.2f, 0.55f) : new Color(0.1f, 0.7f, 0.75f); c.a = (1 - k) * 0.6f;   // 特效着色器会把自发光提亮 1.6 倍：用深一档的颜色，不然发白
                rings.Add(winAt + Vector3.up * (0.01f + i * 0.004f), Quaternion.identity, new Vector3(r, r * 0.08f, r), c, 1);
            }
            // 废墟上升起的光点（4.4 秒，最后 0.8 秒淡出）
            float fade = Mathf.Clamp01((4.4f - t) / 0.8f) * Mathf.Clamp01(t / 0.4f);
            if (fade > 0)
                foreach (var tw in game.city.towers)
                    for (int j = 0; j < 5; j++)
                    {
                        float ph = Mathf.Repeat(t * 0.4f + j * 0.2f + tw.id * 0.13f, 1);
                        float a = ph * 8 + j * 1.26f + tw.id, rad = 0.012f + 0.03f * ph;
                        var p = tw.Base + new Vector3(Mathf.Cos(a) * rad, 0.01f + ph * 0.42f, Mathf.Sin(a) * rad);
                        var c = j % 2 == 0 ? SealCyan : SealPink; c.a = fade * (1 - ph * ph);
                        blobs.Add(p, 0.013f * (1 - ph * 0.5f), c, 1);
                    }
        }
        /// <summary>细长的一道光（EnemyView：把撤离的地面载具吊走）</summary>
        public void FxStreak(Vector3 p, float w, float h, Color c) => blobs.Add(p, Quaternion.identity, new Vector3(w, h, w), c, 1);
        public void FxRing(Vector3 p, float r, Color c) => rings.Add(p, Quaternion.identity, new Vector3(r, r * 0.3f, r), c, 1);
        // 机甲（MechRig）用的小特效接口
        public void FxRingQ(Vector3 p, Quaternion q, Vector3 s, Color c) => rings.Add(p, q, s, c, 1);
        public void FxBlob(Vector3 p, float s, Color c) => blobs.Add(p, s, c, 1);
        public void FxBox(Vector3 p, Quaternion q, Vector3 s, Color c, float glow) => boxes.Add(p, q, s, c, glow);
        public void FxCone(Vector3 p, Vector3 dir, float w, float len, Color c) => cones.Add(p, YTo(dir), new Vector3(w, len, w), c, 1);
        public void FxSmoke(Vector3 p, Vector3 vel, float size, float life, Color c) => puffs.Add(new Puff { pos = p, vel = vel, size = size, life = life, col = c, cartoon = true });
        public void Sparks(Vector3 p, int n, Color c, float speed)
        {
            for (int i = 0; i < n; i++) shards.Add(new Shard { p = p, v = Random.insideUnitSphere * speed + Vector3.up * speed * 0.4f, r = Random.rotation, spin = Random.insideUnitSphere * 14, s = new Vector3(0.007f, 0.0014f, 0.003f), life = Random.Range(0.3f, 0.5f), c = i % 3 == 0 ? Color.white : c });
        }
        public void Shake(float k) => shake = Mathf.Max(shake, k);
        /// <summary>一段光管（a → b，粗 w）：layer 0 白芯（不透明）、1 青色中层、2 粉色外层 —— 和能量波的光柱是同一套三层材质</summary>
        public void FxTube(Vector3 a, Vector3 b, float w, Color c, int layer)
        {
            if (beamCore == null) return;
            var d = b - a; float len = d.magnitude; if (len < 1e-5f) return;
            (layer == 0 ? beamCore : layer == 1 ? beamMid : beamOuter).Add(a, Quaternion.LookRotation(d / len, Mathf.Abs(d.y) > len * 0.95f ? Vector3.forward : Vector3.up), new Vector3(w, w, len), c, 1);
        }
        public void OverlayPulse(float a) => overlay?.Pulse(a);
        public float HeartBeat => overlay ? overlay.Beat : 0;   // 濒死时的心跳强度（记分牌、手背的生命条跟着跳）
        public bool NearDeath => overlay && overlay.NearDeath;
        public void Pow(string kind, Vector3 at) => pow?.Show(kind, at);
        public void PowText(string text, Vector3 at, Color c, float extra, float size, float scale) => pow?.ShowText(text, at, c, extra, size, scale, true);
        public System.Action<Ev> onEnemyEvent;   // 机甲的事件（空降 / 蓄力 / 格挡 / 破绽 / 挨打）交给 EnemyView

        static Building B(MonsterGame g, int id) => g.city.byId.TryGetValue(id, out var b) ? b : null;

        void OnCollapse(Ev ev)
        {
            var b = B(game, ev.id); var v = builder.View(ev.id);
            if (b == null || !v) return;
            builder.Detach(v); builder.Unmerge(v);   // 还原成一层层的（下面要按层克隆、关掉）；改完再排队重新合（见 CityBuilder.Remerge）
            if (ev.to > 0) builder.QueueRemerge(v);
            int from = Mathf.Min(ev.from, v.storeys.Length), to = Mathf.Clamp(ev.to, 0, from);
            var go = new List<Transform>();
            for (int i = to; i < from; i++)
            {
                var s = v.storeys[i]; if (!s.gameObject.activeSelf) continue;
                go.Add(Clone(s));
                s.gameObject.SetActive(false);
            }
            // 独立楼顶（自建楼 / 塔）：跟着倒下去一份，原来的那份落到剩下的最高层上
            if (v.crown && v.crown.gameObject.activeSelf)
            {
                if (from >= b.maxH || go.Count > 0) go.Add(Clone(v.crown));
                v.crown.localPosition = new Vector3(v.crown.localPosition.x, b.TopH(to), v.crown.localPosition.z);
                v.crown.gameObject.SetActive(to > 0);
            }
            var sh = Shield(b.id);
            if (sh) { float H = MonsterGame.HeightOf(b); sh.transform.localPosition = new Vector3(0, H * 0.5f, 0); sh.transform.localScale = new Vector3(sh.transform.localScale.x, Mathf.Max(0.001f, H * 1.25f), sh.transform.localScale.z); }
            if (ev.cause == "uproot")
            {
                // 11-11：被拔起来的楼不能走“原地拍扁 → 碎掉”（11-10 重写倒塌后，拔起来的楼也被拍扁了，手里是个空壳 —— 用户：整栋楼拿不起来）。
                // 各层原样挂到一个组下面，OnUproot 把它交给手
                var grp = new GameObject("Uprooted").transform; grp.SetParent(fxRoot, false);
                grp.SetPositionAndRotation(v.transform.position, v.transform.rotation);
                foreach (var t in go) t.SetParent(grp, true);
                uprooted[b.id] = grp;
                AddDust(b.Base + Vector3.up * 0.004f, 0.03f + b.Foot * 0.3f);   // 楼脚扬一圈尘
                return;
            }
            var top = b.Base + Vector3.up * (b.TopH(to) + b.modH * 0.5f);
            AddDust(top, 0.035f + Mathf.Min(8, from - to) * 0.01f);
            if (go.Count == 0) return;
            if (ev.dir.HasValue && from - to >= 2)
            {
                // 11-16 用户：大招扫过去以后每栋楼也要留下废墟，不要光秃秃 —— 整栋从地基倒下去的时候，瓦砾全落在倒过去的那一头，原来的地块是空的。
                // 现在连根倒的楼（to == 0，不管是被光束轰倒、被手横着扫倒还是被别的楼砸倒）：最底下一截（约三成，至少一层）留在原地塌成瓦砾堆，上面的才倒出去
                int keep = to == 0 && go.Count >= 2 ? Mathf.Clamp(Mathf.RoundToInt(go.Count * 0.3f), 1, go.Count - 1) : 0;
                if (keep > 0)
                {
                    var crownT = v.crown; var lower = new List<Transform>(); int si = 0;
                    for (int i = 0; i < go.Count && si < keep; i++) { lower.Add(go[i]); si++; }
                    go.RemoveRange(0, lower.Count);
                    Crumble(b, lower, null);
                }
                if (go.Count >= 1) Topple(b, v, go, to + keep, ev.dir.Value, ev.cause == "breath" ? 3.4f : 0.6f);   // 被大招轰倒的：一上来就带着很大的角速度（是被轰倒的，不是慢慢歪倒）
            }
            else Crumble(b, go, ev.dir);
            if (ev.to == 0 && b.kind == BuildingKind.Tower) AddBlast(b.Base + Vector3.up * 0.02f, 0.1f);
        }

        // 教学兜底重建：各层重新显示，整栋从地里升起来（0.9 秒，略回弹），脚下扬尘
        readonly List<(Transform t, float age, Vector3 s0)> regrowing = new();
        void Regrow(int id)
        {
            var b = B(game, id); var v = builder.View(id); if (b == null || !v) return;
            builder.Unmerge(v);
            foreach (var st in v.storeys) st.gameObject.SetActive(true);
            if (v.crown) { v.crown.localPosition = new Vector3(v.crown.localPosition.x, b.TopH(b.maxH), v.crown.localPosition.z); v.crown.gameObject.SetActive(true); }
            var s0 = v.transform.localScale;
            foreach (var r in regrowing) if (r.t == v.transform) s0 = r.s0;   // 正在长的又被要求长一次：用原始缩放
            regrowing.RemoveAll(r => r.t == v.transform);
            v.transform.localScale = new Vector3(s0.x, 0.001f, s0.z);
            regrowing.Add((v.transform, 0, s0));
            AddDust(b.Base + Vector3.up * 0.004f, 0.05f);
        }
        void UpdateRegrow(float dt)
        {
            for (int i = regrowing.Count - 1; i >= 0; i--)
            {
                var (t, age, s0) = regrowing[i]; age += dt;
                float x = Mathf.Clamp01(age / 0.9f); const float c1 = 1.3f;
                float e = 1 + (c1 + 1) * Mathf.Pow(x - 1, 3) + c1 * Mathf.Pow(x - 1, 2);
                if (t) t.localScale = new Vector3(s0.x, Mathf.Max(0.001f, e) * s0.y, s0.z);
                if (x >= 1) { if (t) t.localScale = s0; regrowing.RemoveAt(i); } else regrowing[i] = (t, age, s0);
            }
        }

        Transform Clone(Transform s)
        {
            var c = Instantiate(s.gameObject, s.position, s.rotation, fxRoot).transform;
            c.localScale = s.lossyScale;
            c.gameObject.SetActive(true);
            foreach (var r in c.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = true;
            // 11-13 用户：烧黑的楼拿起来又变回原来的浅色 —— 烧黑是加在原楼零件上的“临时颜色”（MaterialPropertyBlock），复制物体时不会跟着走：逐个抄过去
            var src = s.GetComponentsInChildren<MeshRenderer>(true); var dst = c.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < src.Length && i < dst.Length; i++)
                if (src[i].HasPropertyBlock()) { src[i].GetPropertyBlock(mpb); dst[i].SetPropertyBlock(mpb); }
            return c;
        }

        void Topple(Building b, BuildingView v, List<Transform> parts, int to, Vector3 dir, float w0 = 0.6f)
        {
            var d = new Vector3(dir.x, 0, dir.z).normalized;
            // 绕倒向那一侧的底边转（绕底面中心转的话，楼底另一半会转进地面 / 下面的楼里）
            float edge = Mathf.Abs(Vector3.Dot(d, b.zone.u)) * b.fw * 0.5f + Mathf.Abs(Vector3.Dot(d, b.zone.v)) * b.fd * 0.5f;
            var pivot = new GameObject("Falling").transform; pivot.SetParent(fxRoot, false);
            pivot.position = b.Base + Vector3.up * b.TopH(to) + d * edge;
            pivot.rotation = Quaternion.identity;
            foreach (var p in parts) p.SetParent(pivot, true);
            falling.Add(new Falling { pivot = pivot, baseRot = pivot.rotation, axis = Vector3.Cross(Vector3.up, d), dir = d, w = w0, b = b, n = parts.Count, storeys = parts, len = Mathf.Max(b.modH, b.TopH(to + parts.Count) - b.TopH(to)) });
        }

        void UpdateFalling(float dt)
        {
            for (int i = falling.Count - 1; i >= 0; i--)
            {
                var F = falling[i];
                // 越倒越快（重力矩 ∝ sin）
                F.w += (4 + 14 * Mathf.Sin(F.ang)) * dt;
                F.ang = Mathf.Min(1.5f, F.ang + F.w * dt);
                F.pivot.rotation = Quaternion.AngleAxis(F.ang * Mathf.Rad2Deg, F.axis) * F.baseRot;
                // 楼尖碰到旁边还立着的楼顶 / 地面就在那儿断开散落（不转进邻楼里）
                var tip = F.pivot.position + Quaternion.AngleAxis(F.ang * Mathf.Rad2Deg, F.axis) * Vector3.up * F.len;
                bool hit = F.ang > 0.35f && tip.y < Support(F.b.zone, tip, F.b.zone.origin.y) + 0.002f;
                if (F.ang < 1.5f && !hit) continue;
                // 落地：每一层带着自己的东西翻滚着散开
                // 落地 / 砸到邻楼：整截楼当场碎成一地瓦砾（不再整块弹来弹去、最后缩没）
                foreach (var s in F.storeys) { s.SetParent(fxRoot, true); Shatter(s, F.b, F.dir * 0.12f); }
                {
                    // 倒下去的那一截碎成瓦砾落在这一条上：路面上（没有楼挡着）的车压扁
                    float gy = F.b.zone.origin.y, half = Mathf.Max(F.b.fw, F.b.fd) * 0.5f;
                    var a = F.pivot.position; a.y = gy; var b2 = a + F.dir * (F.len * Mathf.Sin(F.ang)); b2.y = gy;
                    FlattenCarsAlong(a, b2, half, F.b.zone);
                }
                pow?.Show("crash", F.pivot.position + F.dir * F.len * 0.5f + Vector3.up * 0.05f);
                AddDust(F.pivot.position + F.dir * F.len * 0.5f, 0.05f + F.n * 0.008f);
                Destroy(F.pivot.gameObject);
                falling.RemoveAt(i);
            }
        }

        // 往下拍（11-10 用户：楼塌还是一层层的；地基没了上面几层还悬在天上 —— 原来每层各自压扁叠成一摞、再从下往上逐层碎）：
        // 被拍掉的那一截作为一个整体：0.12 秒被拍扁（压到 12%、略外鼓、微歪）→ 停 0.1 秒（看得出“被拍扁了”）→ 一次性整体碎成瓦砾堆 + 尘浪
        class Squash { public Transform pivot; public Building b; public Pile pile; public float t, tilt; public int n; public Vector3 tiltAxis; public Quaternion rot0; }
        readonly List<Squash> squashes = new();
        void Crumble(Building b, List<Transform> parts, Vector3? dir)
        {
            if (parts.Count == 0) return;
            float baseY = b.Base.y + b.TopH(b.h);   // 剩下那截楼的真实楼顶
            var pivot = new GameObject("Squash").transform; pivot.SetParent(fxRoot, false);
            var v = builder.View(b.id);
            pivot.SetPositionAndRotation(new Vector3(b.Base.x, baseY, b.Base.z), v ? v.transform.rotation : Quaternion.identity);
            foreach (var t in parts) t.SetParent(pivot, true);
            var ax = Random.insideUnitSphere; ax.y = 0;
            squashes.Add(new Squash { pivot = pivot, rot0 = pivot.rotation, b = b, pile = PileFor(b, parts.Count, baseY), n = parts.Count, tilt = Random.Range(2f, 4f), tiltAxis = ax.sqrMagnitude > 1e-4f ? ax.normalized : Vector3.right });
        }
        void UpdateSquashes(float dt)
        {
            for (int i = squashes.Count - 1; i >= 0; i--)
            {
                var S = squashes[i];
                if (!S.pivot) { squashes.RemoveAt(i); continue; }
                float t0 = S.t; S.t += dt;
                float e = Mathf.Clamp01(S.t / 0.12f); e *= e;   // 加速压下去（像被一巴掌拍下）
                S.pivot.localScale = new Vector3(1 + 0.12f * e, Mathf.Lerp(1, 0.12f, e), 1 + 0.12f * e);
                S.pivot.rotation = Quaternion.AngleAxis(S.tilt * e, S.tiltAxis) * S.rot0;
                if (t0 < 0.12f && S.t >= 0.12f)
                {
                    if (S.pile != null && S.pile.c.y < S.b.zone.origin.y + 0.004f) FlattenCars(S.pile.c, S.pile.R * 1.05f, 6, false);   // 塌到地面的瓦砾堆盖住的车
                    // 拍到底的一刻：贴地一圈尘浪 + 轻微震屏
                    AddDust(S.pivot.position + Vector3.up * 0.003f, 0.05f + Mathf.Min(10, S.n) * 0.008f);
                    if (S.n >= 2) pow?.Show("smash", S.pivot.position + Vector3.up * 0.06f);
                    shake = Mathf.Max(shake, 0.25f);
                }
                if (S.t >= 0.22f)
                {
                    // 整体一次碎掉（瓦砾数按原来的层数，不按压扁后的体积）
                    int per = Mathf.Clamp(6 + S.n, 8, 14);
                    var kids = new List<Transform>(); foreach (Transform c in S.pivot) kids.Add(c);
                    foreach (var c in kids) { c.SetParent(fxRoot, true); Shatter(c, S.b, Vector3.zero, per, S.pile, false); }
                    Destroy(S.pivot.gameObject);
                    squashes.RemoveAt(i);
                }
            }
        }

        /// <summary>追兵残骸：整台翻滚着飞出去、落回所在城区的地面（飞出边缘就掉到真实地板），过几秒碎成几块残片</summary>
        public void AddWreck(Transform t, Vector3 vel, float life = -1)
        {
            t.SetParent(fxRoot, true);
            Zone z = null; float best = float.MaxValue;
            foreach (var q in game.city.zones) { float d = Mathf.Abs(t.position.y - q.origin.y); if (d < best) { best = d; z = q; } }
            pieces.Add(new Piece { t = t, vel = vel, spin = Random.insideUnitSphere * 8, floor = z != null && t.position.y > z.origin.y - 0.02f ? z.origin.y : 0, zone = z ?? game.city.zones[0], life = life > 0 ? life : 0.45f + Random.value * 0.25f, rs = t.GetComponentsInChildren<Renderer>() });
            AddDust(t.position, 0.03f);
        }

        void AddPiece(Transform t, Building b, Vector3 vel, Vector3 spin) => pieces.Add(NewPiece(t, b, vel, spin));
        Piece NewPiece(Transform t, Building b, Vector3 vel, Vector3 spin) =>
            new Piece { t = t, b = b, vel = vel, spin = spin, floor = b.zone.origin.y, zone = b.zone, life = 3.2f + Random.value, rs = t.GetComponentsInChildren<Renderer>() };

        static Bounds Bounds(Piece P)
        {
            Bounds bb = default; bool any = false;
            if (P.rs != null) foreach (var r in P.rs) { if (!r) continue; if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
            return any ? bb : new Bounds(P.t.position, Vector3.zero);
        }
        /// <summary>某点下方能托住碎块的高度：城区地面（出了城区就是真实地板 0），以及还立着的楼顶</summary>
        float Support(Zone z, Vector3 p, float floor)
        {
            float f = FloorAt(z, p, floor);
            foreach (var b in game.city.buildings)
            {
                if (b.h <= 0 || b.zone != z) continue;
                var d = p - b.Base;
                if (Mathf.Abs(Vector3.Dot(d, z.u)) < b.fw / 2 && Mathf.Abs(Vector3.Dot(d, z.v)) < b.fd / 2) f = Mathf.Max(f, RoofY(b, p));
            }
            return f;
        }

        // 11-13 用户：还有个别瓦砾悬在空中 —— 落在完好的楼上时，原来按“楼的最高点”停（最高点含天线、尖顶、楼顶招牌；尖顶摩天楼的最高点比屋面高十几厘米），
        // 瓦砾就悬在屋面上方。现在按真实屋面算：取最上面那一层模型里“朝上的面积最大的那个高度”当屋面（缓存，每种模型算一次）；
        // 落点在屋面范围以外（退台的外圈）就落到这一层的底（下面一层的顶）。被拍掉一截的楼顶是平的切面，照旧按层高算。
        readonly Dictionary<Mesh, (float y, Rect r)> decks = new();
        public float RoofY(Building b, Vector3 p)
        {
            float cut = b.Base.y + b.TopH(b.h);
            var v = builder.View(b.id);
            if (!v || v.storeys == null || b.h < 1 || b.h > v.storeys.Length) return cut;
            if (b.h < b.maxH && b.kind == BuildingKind.House) return cut;      // 切面：平的
            var st = v.storeys[b.h - 1]; if (!st) return cut;
            var mf = st.GetComponentInChildren<MeshFilter>(true); if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) return cut;
            if (!decks.TryGetValue(mf.sharedMesh, out var dk)) decks[mf.sharedMesh] = dk = Deck(mf.sharedMesh);
            float below = b.Base.y + b.TopH(b.h - 1);
            if (dk.r.width <= 0) return Mathf.Min(cut, below + 0.002f);
            var lp = mf.transform.InverseTransformPoint(p);
            if (lp.x < dk.r.xMin || lp.x > dk.r.xMax || lp.z < dk.r.yMin || lp.z > dk.r.yMax) return below;
            return Mathf.Clamp(mf.transform.TransformPoint(new Vector3(lp.x, dk.y, lp.z)).y, below, cut);
        }
        /// <summary>模型里朝上的面按高度分 40 档累计面积，面积最大的那一档 = 屋面；返回它的高度和水平范围（模型本地坐标）</summary>
        static (float y, Rect r) Deck(Mesh m)
        {
            var V = m.vertices; var T = m.triangles; var bb = m.bounds;
            if (bb.size.y < 1e-6f) return (bb.max.y, Rect.MinMaxRect(bb.min.x, bb.min.z, bb.max.x, bb.max.z));
            const int N = 40; var area = new float[N + 1];
            int Bin(float y) => Mathf.Clamp(Mathf.RoundToInt((y - bb.min.y) / bb.size.y * N), 0, N);
            for (int i = 0; i < T.Length; i += 3)
            {
                Vector3 a = V[T[i]], b = V[T[i + 1]], c = V[T[i + 2]]; var n = Vector3.Cross(b - a, c - a); float ar = n.magnitude * 0.5f;
                if (ar < 1e-10f || n.y / (ar * 2) < 0.9f) continue;
                area[Bin((a.y + b.y + c.y) / 3)] += ar;
            }
            int best = -1; for (int k = 1; k <= N; k++) if (area[k] > 0 && (best < 0 || area[k] > area[best] * 1.0001f)) best = k;   // 第 0 档是模型的底面，不算
            if (best < 0) return (bb.max.y, new Rect(0, 0, 0, 0));
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue, ys = 0; int cnt = 0;
            for (int i = 0; i < T.Length; i += 3)
            {
                Vector3 a = V[T[i]], b = V[T[i + 1]], c = V[T[i + 2]]; var n = Vector3.Cross(b - a, c - a); float ar = n.magnitude * 0.5f;
                if (ar < 1e-10f || n.y / (ar * 2) < 0.9f || Bin((a.y + b.y + c.y) / 3) != best) continue;
                foreach (var q in new[] { a, b, c }) { x0 = Mathf.Min(x0, q.x); x1 = Mathf.Max(x1, q.x); z0 = Mathf.Min(z0, q.z); z1 = Mathf.Max(z1, q.z); ys += q.y; cnt++; }
            }
            return (ys / Mathf.Max(1, cnt), Rect.MinMaxRect(x0, z0, x1, z1));
        }

        static Color BuildingTint(Building b) => b.kind == BuildingKind.Tower ? Color.Lerp(b.color, Color.gray, 0.5f) : b.style switch
        {
            Style.Brick => new Color(0.62f, 0.36f, 0.3f), Style.Glass or Style.Modern or Style.Spire => new Color(0.55f, 0.62f, 0.7f),
            Style.Deco or Style.Condo => new Color(0.86f, 0.8f, 0.7f), Style.Warehouse => new Color(0.6f, 0.58f, 0.52f), _ => new Color(0.75f, 0.72f, 0.68f),
        };

        /// <summary>落点：还在原来的家具面上就落在面上，飞出边缘就掉到真实地板（y=0）</summary>
        static float FloorAt(Zone z, Vector3 p, float floor)
        {
            if (floor <= 0.001f) return 0;
            var d = p - z.origin; float a = Vector3.Dot(d, z.u), bb = Vector3.Dot(d, z.v);
            return a >= 0 && a <= z.w && bb >= 0 && bb <= z.h ? floor : 0;
        }

        void UpdatePieces(float dt)
        {
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                var P = pieces[i]; P.age += dt;
                if (!P.t) { pieces.RemoveAt(i); continue; }
                if (!P.rest)
                {
                    P.vel += Vector3.up * (-2.6f * dt);
                    P.t.position += P.vel * dt;
                    P.t.rotation = Quaternion.Euler(P.spin * (dt * Mathf.Rad2Deg)) * P.t.rotation;
                    float f = FloorAt(P.zone, P.t.position, P.floor);
                    if (f < P.floor) P.floor = f;
                    float sup = Support(P.zone, P.t.position, P.floor);
                    float minY = Bounds(P).min.y;
                    // 按真实包围盒落地：最低点碰到地面 / 楼顶就托住（不插进地里、不穿进别的楼）
                    if (minY < sup + 0.0005f && P.vel.y < 0)
                    {
                        P.t.position += Vector3.up * (sup + 0.0005f - minY);
                        P.vel *= 0.3f; P.spin *= 0.3f;
                        if (P.vel.magnitude < 0.03f) P.rest = true; else P.vel.y = -P.vel.y * 0.25f;
                    }
                }
                // 到时间不再缩小消失：在原地碎成几块瓦砾 + 一小团尘（追兵残骸等）
                if (P.age > P.life) { pieces.RemoveAt(i); Shatter(P.t, P.b, Vector3.zero, P.b == null ? 4 : 0); }
            }
            if (pieces.Count > 120) { for (int i = 0; i < pieces.Count - 120; i++) if (pieces[i].t) Shatter(pieces[i].t, pieces[i].b, Vector3.zero, 3); pieces.RemoveRange(0, pieces.Count - 120); }
            UpdateSquashes(dt);
            UpdateRubble(dt);
        }

        // ═══════════ 瓦砾：楼碎掉以后留在原地的一堆块（11-09 用户：不要缩小消失）═══════════
        // 一块 = 实例化的方块（几百块也只要几次绘制）；落定后平躺在地面 / 楼顶上（只留一点歪斜，不插进地里），
        // 躺在楼顶上的，下面的楼被拆了就重新往下掉。最多留 2400 块（超出时去掉最早的）。
        // 11-09 第二版（逐帧看过：第一版是一色米白小方块平铺一层，像方糖；瓦砾和烟共用透明材质，没有深度、发虚）：
        //   · 瓦砾不透明、写深度（rubMat：同一个 FxPuff，混合方式改属性）；
        //   · 一栋楼碎掉 = 一座“瓦砾堆”（Pile）：中间高、四周低（落定高度按到堆心的距离），底下垫一个低矮土丘（Mound）把缝补上；
        //   · 颜色 = 楼的主色深浅变化 6 成 + 混凝土灰 2.5 成 + 深色窗框 / 玻璃 1.5 成；形状 = 扁石板 + 长梁 + 不规则碎块。
        class Rub { public Matrix4x4 m; public bool hasM; public Vector3 p, v, s, spin; public Quaternion r; public Zone z; public Color c; public float floor, check; public bool rest, chunk; public Pile pile; }
        class Pile { public Vector3 c; public float R, H; }
        class Mound { public Vector3 p; public Vector3 s; public Quaternion r; public Color c; public float age, vy, check; public Zone z; }
        readonly List<Rub> rubs = new();
        readonly List<Mound> mounds = new();
        readonly List<FxBatch> rubBatches = new(), chunkBatches = new();
        FxBatch moundBatch; Material rubMat;
        const int RubMax = 3600, MoundMax = 200;   // 11-16：2400 → 3600（大招扫一圈会倒十几二十栋，早先的废墟被挤掉 = 地块又变光秃秃）

        Material RubMat()
        {
            if (rubMat) return rubMat;
            rubMat = new Material(Shader.Find("Monster/FxPuff")) { enableInstancing = true, name = "rubble", renderQueue = 2450 };
            rubMat.SetFloat("_SrcBlend", 1); rubMat.SetFloat("_DstBlend", 0); rubMat.SetFloat("_ZWrite", 1);
            return rubMat;
        }

        /// <summary>一栋楼被拍扁时的瓦砾堆：以楼脚为中心，半径 ≈ 占地对角线一半 × 1.15，高 ≈ 塌掉层数（最多 5 层）× 层高 × 0.4</summary>
        Pile PileFor(Building b, int storeys, float y)
        {
            float R = Mathf.Max(b.fw, b.fd) * 0.5f + 0.003f;
            float H = Mathf.Clamp(Mathf.Min(5, storeys) * b.modH * 0.4f, 0.004f, R * 0.7f);
            if (y > b.zone.origin.y + 0.002f) { R = Mathf.Max(b.fw, b.fd) * 0.5f; H = Mathf.Min(H, R * 0.4f); }   // 堆在剩下的楼顶上：不超出楼顶
            AddMound(new Vector3(b.Base.x, y, b.Base.z), R, H, BuildingTint(b));
            return new Pile { c = new Vector3(b.Base.x, y, b.Base.z), R = R, H = H };
        }

        void AddMound(Vector3 p, float R, float H, Color tint)
        {
            var c = Color.Lerp(tint, new Color(0.36f, 0.34f, 0.34f), 0.6f) * 0.72f; c.a = 1;   // 比瓦砾暗：只当堆底的阴影和填缝
            mounds.Add(new Mound { p = p, s = new Vector3(R * 1.35f, H * 1.4f, R * 1.35f), r = Quaternion.Euler(0, Random.Range(0, 360f), 0), c = c, z = NearestZone(p), check = 0.3f });
            if (mounds.Count > MoundMax) mounds.RemoveAt(0);
        }

        static Color RubColor(Color tint)
        {
            float u = Random.value; Color c;
            if (u < 0.6f) c = tint * Random.Range(0.72f, 1.08f);
            else if (u < 0.85f) c = new Color(0.56f, 0.55f, 0.54f) * Random.Range(0.8f, 1.05f);
            else c = Random.value < 0.5f ? new Color(0.2f, 0.2f, 0.25f) : new Color(0.32f, 0.42f, 0.52f);
            c.a = 1; return c;
        }

        /// <summary>把一截楼（或残骸）碎成瓦砾：按它的包围盒体积撒块，原物体删掉。pile 为空时按这截的包围盒自己堆一小堆</summary>
        void Shatter(Transform t, Building b, Vector3 push, int n = 0, Pile pile = null, bool dust = true)
        {
            if (!t) return;
            Bounds bb = default; bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>()) { if (!r.enabled) continue; if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
            Destroy(t.gameObject);
            if (!any) return;
            var z = b != null ? b.zone : NearestZone(bb.center);
            var tint = b != null ? BuildingTint(b) : new Color(0.3f, 0.3f, 0.34f);
            float char_ = b != null ? burnt.GetValueOrDefault(b.id) : 0;   // 烧过的楼：瓦砾、土堆也发黑（举一反三：不只是拿起来的那份）
            if (char_ > 0) tint = Color.Lerp(tint, new Color(0.14f, 0.12f, 0.12f), char_ * 0.8f);
            var ext = bb.extents;
            float floor0 = FloorAt(z, bb.center, z != null ? z.origin.y : 0);
            if (pile == null)
            {
                float R = Mathf.Max(ext.x, ext.z) * 0.9f + 0.003f, H = Mathf.Clamp(ext.y * 0.5f, 0.003f, R * 0.6f);
                pile = new Pile { c = new Vector3(bb.center.x, floor0, bb.center.z), R = R, H = H };
                if (b != null) AddMound(pile.c, R * 0.85f, H * 0.8f, tint);
            }
            // 块的大小按这截楼的平面尺寸（约楼宽的 30%，有大有小）；块数按体积，每截 8～14 块
            float unit = Mathf.Clamp(Mathf.Min(ext.x, ext.z) * 0.3f, 0.0025f, 0.016f);
            if (n <= 0) n = Mathf.Clamp(Mathf.RoundToInt(ext.x * ext.y * ext.z * 8f / (unit * unit * unit) * 0.12f), 10, 16);
            for (int k = 0; k < n; k++)
            {
                var p = bb.center + Vector3.Scale(ext, new Vector3(Random.Range(-0.85f, 0.85f), Random.Range(-0.6f, 0.6f), Random.Range(-0.85f, 0.85f)));
                var out_ = p - bb.center; out_.y = 0;
                float sz = unit * Random.Range(0.5f, 1.25f), kind = Random.value;
                // 扁石板（楼板 / 墙板）6 成、长梁 1.5 成、不规则碎块 2.5 成
                Vector3 sc = kind < 0.6f ? new Vector3(sz * Random.Range(1f, 1.6f), sz * Random.Range(0.18f, 0.32f), sz * Random.Range(0.8f, 1.3f))
                           : kind < 0.75f ? new Vector3(sz * 0.22f, sz * 0.22f, sz * Random.Range(1.8f, 2.6f))
                           : Vector3.one * sz * Random.Range(0.7f, 1f);
                rubs.Add(new Rub
                {
                    p = p, s = sc, r = Random.rotation, chunk = kind >= 0.75f, pile = pile,
                    v = push + out_.normalized * Random.Range(0.02f, 0.09f) + Vector3.up * Random.Range(0.0f, 0.08f),
                    spin = Random.insideUnitSphere * 7, z = z, c = Color.Lerp(RubColor(tint), new Color(0.14f, 0.12f, 0.12f), char_ * 0.7f), floor = floor0, check = Random.value * 0.3f,
                });
            }
            if (rubs.Count > RubMax) rubs.RemoveRange(0, rubs.Count - RubMax);
            if (dust) AddDust(new Vector3(bb.center.x, floor0, bb.center.z), Mathf.Clamp(Mathf.Max(ext.x, ext.z) * 1.2f, 0.012f, 0.04f));
        }
        Zone NearestZone(Vector3 p)
        {
            Zone best = game.city.zones[0]; float bd = float.MaxValue;
            foreach (var q in game.city.zones) { float d = Mathf.Abs(p.y - q.origin.y); if (d < bd) { bd = d; best = q; } }
            return best;
        }

        void UpdateRubble(float dt)
        {
            int bi = 0, ci = 0;
            for (int i = 0; i < rubs.Count; i++)
            {
                var r = rubs[i];
                if (r.rest)
                {
                    // 躺在楼顶上的：每 0.3 秒看一下下面还托不托得住
                    r.check -= dt;
                    if (r.check <= 0)
                    {
                        r.check = 0.3f;
                        if (r.floor > (r.z != null ? r.z.origin.y : 0) + 0.002f && Support(r.z, r.p, r.z != null ? r.z.origin.y : 0) < r.floor - 0.002f)
                        {
                            // 11-10 用户：上半截塌了瓦砾堆在剩下的楼顶，下半截再塌，瓦砾还悬在原高度 —— 原因是重新下落时还按旧堆的高度停。
                            // 托不住就掉，并且丢掉旧堆（旧堆的高度已经不存在了），落到新的支撑面上
                            r.rest = false; r.hasM = false; r.v = Vector3.zero; r.pile = null;
                        }
                    }
                }
                else
                {
                    r.v += Vector3.up * (-2.6f * dt); r.p += r.v * dt; r.r = Quaternion.Euler(r.spin * (dt * Mathf.Rad2Deg)) * r.r;
                    float sup = r.z != null ? Support(r.z, r.p, r.z.origin.y) : 0;
                    // 瓦砾堆：离堆心越近落得越高（抛物面），堆出中间高、四周低的形状
                    float pileY = sup;
                    if (r.pile != null)
                    {
                        var d = r.p - r.pile.c; d.y = 0;
                        float q = d.magnitude / r.pile.R;
                        if (q < 1) pileY = Mathf.Max(sup, r.pile.c.y + r.pile.H * (1 - q * q) * Random.Range(0.75f, 1.05f));
                    }
                    if (r.p.y - r.s.y * 0.5f < pileY && r.v.y < 0)
                    {
                        // 落定：大致平躺（石板最多歪 18°，碎块随意），底面贴在堆面 / 地面 / 楼顶上
                        r.floor = sup; r.rest = true;
                        r.r = r.chunk ? Random.rotation : Quaternion.Euler(Random.Range(-18f, 18f), Random.Range(0f, 360f), Random.Range(-18f, 18f));
                        r.p.y = pileY + r.s.y * 0.35f;
                    }
                }
                // 落定的瓦砾不再动：矩阵算一次存起来（原来 2400 块每帧各重算一次，是“碎块”这一段耗时的大头）
                if (r.rest && !r.hasM) { r.m = Matrix4x4.TRS(r.p, r.r, r.s); r.hasM = true; }
                if (r.chunk)
                {
                    if (ci / 1000 >= chunkBatches.Count) chunkBatches.Add(new FxBatch(FxBatch.Blob(), RubMat()));
                    if (r.hasM) chunkBatches[ci / 1000].AddM(r.m, r.c); else chunkBatches[ci / 1000].Add(r.p, r.r, r.s, r.c); ci++;
                }
                else
                {
                    if (bi / 1000 >= rubBatches.Count) rubBatches.Add(new FxBatch(FxBatch.Box(), RubMat()));
                    if (r.hasM) rubBatches[bi / 1000].AddM(r.m, r.c); else rubBatches[bi / 1000].Add(r.p, r.r, r.s, r.c); bi++;
                }
            }
            // 土丘：0.35 秒从地里拱起来（和瓦砾落下同时），之后不动
            if (mounds.Count > 0)
            {
                moundBatch ??= new FxBatch(FxBatch.Blob(), RubMat());
                foreach (var m in mounds)
                {
                    // 土丘同样：下面的楼没了就往下掉（重力），落到新的支撑面
                    m.check -= dt;
                    if (m.check <= 0 || m.vy != 0)
                    {
                        m.check = 0.3f;
                        float sup = m.z != null ? Support(m.z, m.p, m.z.origin.y) : 0;
                        if (m.p.y > sup + 0.002f) { m.vy -= 2.6f * dt; m.p.y = Mathf.Max(sup, m.p.y + m.vy * dt); if (m.p.y <= sup) m.vy = 0; }
                        else m.vy = 0;
                    }
                    m.age += dt; float e = Mathf.Clamp01(m.age / 0.35f); e = 1 - (1 - e) * (1 - e);
                    var sc = new Vector3(m.s.x, m.s.y * e, m.s.z);
                    moundBatch.Add(m.p, m.r, sc, m.c);
                }
                moundBatch.Draw();
            }
            foreach (var b in rubBatches) b.Draw();
            foreach (var b in chunkBatches) b.Draw();
        }

        // ═══════════ 楼：着火、烧黑、护盾 ═══════════
        readonly List<(int, bool)> lineIds = new(); readonly List<object> keyBuf = new();
        bool InPlay(object key, bool prop) { foreach (var h in game.held.Values) if ((prop ? h.prop : (object)h.chunk) == key) return true; foreach (var f in game.flying) if ((prop ? f.prop : (object)f.chunk) == key) return true; return false; }
        void UpdateBuildings(float dt)
        {
            float t = Time.time;
            foreach (var b in game.city.buildings)
            {
                var sh = b.kind == BuildingKind.Tower ? Shield(b.id) : null;
                if (sh)
                {
                    bool on = b.h > 0 && game.IsShielded(b);
                    if (sh.gameObject.activeSelf != on) sh.gameObject.SetActive(on);
                    float hit = sh.sharedMaterial.GetFloat("_Hit");
                    if (hit > 0) sh.sharedMaterial.SetFloat("_Hit", Mathf.Max(0, hit - dt * 3));
                }
                if (b.h <= 0 || b.fire <= 0) continue;
                var top = MonsterGame.TopOf(b);
                float s = Mathf.Min(0.055f, Mathf.Min(b.fw, b.fd) * 0.6f) * Mathf.Min(1, 0.45f + b.fire * 0.25f);
                // 11-11 卡通火（第二版）。第一版是七根笔直对称的粉 / 黄 / 白尖锥，看着像一丛水晶不像火 —— 问题在颜色（粉）和形状（笔直不动）：
                //  · 颜色：外层橙红、中层橙、内芯黄（特效着色器会把发光体中间提亮，所以用深一档的颜色）；
                //  · 形状：每条火舌由两截圆锥拼成（下半截粗短、上半截细而且歪得更多）→ 弯弯的火苗；整体被风带歪，各自左右摆、一跳一跳；
                //  · 火苗尖上不断甩出小火团，往上飘着缩没；火星和深紫卡通烟团照旧。
                var wind = new Vector3(0.14f + 0.06f * Mathf.Sin(t * 1.3f + b.id), 0, 0.05f * Mathf.Sin(t * 0.9f + b.id * 2));
                for (int k = 0; k < 7; k++)
                {
                    float ph = t * 9 + b.id * 1.7f + k * 2.1f, ang = k * 0.9f, r = k == 0 ? 0 : s * (k < 4 ? 0.3f : 0.44f);
                    float jump = 1 + 0.28f * Mathf.Sin(ph) + 0.14f * Mathf.Sin(ph * 2.7f);
                    var basePos = top + new Vector3(Mathf.Cos(ang) * r, 0, Mathf.Sin(ang) * r);
                    float h = s * (k == 0 ? 1.5f : k < 4 ? 1.0f : 0.62f) * jump, w = s * (k == 0 ? 0.66f : 0.4f);
                    var sway = new Vector3(Mathf.Sin(ph * 0.55f + k), 0, Mathf.Cos(ph * 0.43f + k * 1.7f)) * 0.16f;
                    var d1 = (Vector3.up + wind + sway).normalized; var d2 = (Vector3.up + (wind + sway) * 2.6f).normalized;
                    var q1 = YTo(d1); var q2 = YTo(d2);
                    for (int layer = 0; layer < (k < 4 ? 3 : 2); layer++)
                    {
                        float sc = layer == 0 ? 1 : layer == 1 ? 0.7f : 0.4f; var col = FireCol[layer];
                        cones.Add(basePos, q1, new Vector3(w * sc, h * 0.72f * sc, w * sc), col, 1);                                                   // 下半截：粗、短
                        cones.Add(basePos + d1 * h * 0.42f * sc, q2, new Vector3(w * 0.62f * sc, h * 0.62f * sc, w * 0.62f * sc), col, 1);   // 上半截：细、歪得更多
                    }
                }
                for (int k = 0; k < 3; k++)
                {
                    float ph = Mathf.Repeat(t * 1.1f + k * 0.37f + b.id * 0.13f, 1);
                    var fp = top + Vector3.up * s * (1.1f + ph * 1.1f) + wind * s * (1.5f + ph * 2.5f) + new Vector3(Mathf.Sin(k * 2.4f), 0, Mathf.Cos(k * 2.4f)) * s * 0.3f;
                    float sz = s * 0.26f * (1 - ph); var fq = YTo((Vector3.up + wind * 2).normalized);
                    cones.Add(fp, fq, new Vector3(sz, sz * 1.8f, sz), FireCol[1], 1);
                    cones.Add(fp, fq, new Vector3(sz * 0.55f, sz, sz * 0.55f), FireCol[2], 1);
                }
                smokeCd.TryGetValue(b.id, out var cd); cd -= dt;
                if (cd <= 0)
                {
                    cd = Random.Range(0.4f, 0.65f);
                    puffs.Add(new Puff { pos = top + new Vector3(Random.Range(-0.4f, 0.4f) * s, s * 1.2f, Random.Range(-0.4f, 0.4f) * s), vel = new Vector3(0.012f + Random.Range(-0.008f, 0.008f), 0.06f, Random.Range(-0.008f, 0.008f)), size = s * 0.3f, life = 1.5f, col = new Color(0.3f, 0.22f, 0.38f), cartoon = true });
                    // 火星
                    for (int e = 0; e < 3; e++)
                        puffs.Add(new Puff { pos = top + new Vector3(Random.Range(-0.4f, 0.4f) * s, s * 0.6f, Random.Range(-0.4f, 0.4f) * s), vel = new Vector3(Random.Range(-0.03f, 0.04f), Random.Range(0.08f, 0.16f), Random.Range(-0.03f, 0.03f)), size = 0.0022f, grow = -1, life = Random.Range(0.6f, 1.1f), col = new Color(1f, 0.8f, 0.3f), glow = 1 });
                }
                smokeCd[b.id] = cd;
                // 烧黑：逐渐压暗、熄掉窗灯
                float kb = Mathf.Min(1, b.fire / 8);
                burnt.TryGetValue(b.id, out var was);
                if (kb - was >= 0.08f) { burnt[b.id] = kb; Scorch(builder.View(b.id), kb); }
            }
        }

        void Scorch(BuildingView v, float k)
        {
            if (!v) return;
            foreach (var r in v.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.sharedMaterial == null || r.sharedMaterial.shader != CityMaterials.Toon) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", Color.Lerp(Color.white, new Color(0.25f, 0.2f, 0.19f), k * 0.8f));
                mpb.SetFloat("_NightGlow", 0);
                mpb.SetColor("_EmissionColor", Color.Lerp(new Color(1.6f, 1.25f, 0.7f), Color.black, k));
                r.SetPropertyBlock(mpb);
            }
        }

        readonly Dictionary<int, MeshRenderer> shields = new();
        MeshRenderer Shield(int id)
        {
            if (shields.TryGetValue(id, out var r)) return r;
            var v = builder.View(id); var t = v ? v.transform.Find("Shield") : null;
            return shields[id] = t ? t.GetComponent<MeshRenderer>() : null;
        }

        // ═══════════ 塔 → 电站的电线：光点从电站流向塔；盯住塔时变粗变亮 ═══════════
        readonly Dictionary<int, float> lineFade = new();
        void DrawLines()
        {
            float t = Time.time, dt = Time.deltaTime;
            foreach (var tw in game.city.towers)
            {
                if (tw.h <= 0) continue;
                bool focus = tw.scanUntil > game.time;
                // 11-10 用户：塔不是要看了才知道连着哪儿吗，怎么一开始就有 —— 平时不画；盯住塔 / 电站后，
                // 线从塔顶往电站一路“画”出去（0.6 秒），显示 6 秒后淡掉
                float f = lineFade.GetValueOrDefault(tw.id);
                f = Mathf.MoveTowards(f, focus ? 1 : 0, dt * (focus ? 1.7f : 1.2f));
                lineFade[tw.id] = f;
                if (f <= 0.01f) continue;
                var ids = lineIds; ids.Clear(); foreach (var pid in tw.powerIds) ids.Add((pid, false));
                if (tw.backupId >= 0) ids.Add((tw.backupId, true));
                foreach (var (sid, backup) in ids)
                {
                    var st = B(game, sid); if (st == null || st.h <= 0) continue;
                    var A = MonsterGame.TopOf(tw) + Vector3.up * 0.03f; var Bv = MonsterGame.TopOf(st) + Vector3.up * 0.02f;
                    float lift = Mathf.Max(0.1f, Vector3.Distance(A, Bv) * 0.3f);
                    const int N = 40;
                    for (int i = 0; i < N; i++)
                    {
                        if (backup && i % 3 != 0) continue;
                        float u = i / (float)(N - 1);
                        if (focus && u < 1 - f) continue;   // 渐显：从塔（u=1）往电站（u=0）长出去；淡出时整条变细
                        var p = Vector3.Lerp(Bv, A, u); p.y += Mathf.Sin(u * Mathf.PI) * lift;
                        float flow = Mathf.Repeat(u * 5 - t * 1.6f, 1);
                        float bs = 0.01f * (focus ? 1 : f);
                        if (st.online) blobs.Add(p, bs * (flow < 0.25f ? 1.5f : 0.8f), tw.color * (focus ? 1.5f : 1.1f), 1);
                        else blobs.Add(p, bs * 0.6f, new Color(0.33f, 0.33f, 0.4f), 0.3f);
                    }
                }
            }
        }

        // ═══════════ 电站：护甲灯 + 重启倒计时（11-11 新玩法）═══════════
        // 立着的电站：头顶一排护甲灯（还剩几格亮几盏，黄色；刚被打的那盏闪一下白）。
        // 被拆掉、正在重启的电站：废墟上一圈倒计时光环（塔的颜色，一格一格灭；最后 5 秒变红、慢闪）+ 中间一根越来越高的光柱（快好了）。
        readonly Dictionary<int, float> armorFlash = new();
        void DrawStations()
        {
            float t = Time.time; int max = Mathf.Max(1, game.level.armor);
            foreach (var st in game.city.stations)
            {
                var tw = B(game, st.towerId);
                var col = tw != null ? tw.color : Color.white;
                // 护盾罩：护甲还剩不止一格时罩着；最后一层护甲被打掉（或电站直接被毁）那一刻碎掉
                {
                    bool want = st.h > 0 && st.armor > 1 && !(powerOff != null && powerOff(st.Base));
                    var sh = want || stShields.ContainsKey(st.id) ? ShieldOf(st, col) : null;
                    if (sh != null)
                    {
                        if (sh.on && !want) ShatterShield(st, col);
                        if (!sh.on && want) sh.pop = 0;
                        sh.on = want; sh.pop = Mathf.Min(1, sh.pop + Time.deltaTime * 2.5f);
                        if (sh.t.gameObject.activeSelf != want) sh.t.gameObject.SetActive(want);
                        if (want)
                        {
                            float e = 1 - Mathf.Pow(1 - sh.pop, 3);
                            sh.t.localScale = DomeScale(st) * Mathf.Lerp(0.6f, 1, e);
                            sh.m.SetFloat("_Crack", max > 1 ? 1 - (st.armor - 1) / (float)(max - 1) : 0);
                            sh.m.SetFloat("_Hit", Mathf.Clamp01(armorFlash.GetValueOrDefault(st.id)));
                            sh.m.SetFloat("_Fade", e);
                        }
                    }
                }
                // 脚手架：重启前 1.6 秒从地里搭起来，新电站在里面升起（0.9 秒，四周冒施工的烟尘），升完脚手架收走
                {
                    bool building = buildFx.TryGetValue(st.id, out var bage);
                    if (building) { bage += Time.deltaTime; if (bage > 1.3f) buildFx.Remove(st.id); else buildFx[st.id] = bage; }
                    float sk = st.h <= 0 && st.restart >= 0 && st.restart < 1.6f ? 1 - st.restart / 1.6f : building ? (bage < 0.95f ? 1 : Mathf.Clamp01(1 - (bage - 0.95f) / 0.3f)) : 0;
                    if (sk > 0.01f)
                    {
                        var rot = Quaternion.LookRotation(-st.zone.v, Vector3.up); var yel = new Color(0.96f, 0.76f, 0.16f); var dk = new Color(0.2f, 0.18f, 0.24f);
                        float hx = st.fw * 0.5f + 0.005f, hz = st.fd * 0.5f + 0.005f, H = (st.TopH(1) + 0.014f) * sk;
                        foreach (var (px, pz) in new[] { (-hx, -hz), (hx, -hz), (-hx, hz), (hx, hz), (0f, -hz), (0f, hz) })
                            boxes.Add(st.Base + rot * new Vector3(px, H * 0.5f, pz), rot, new Vector3(0.0022f, H, 0.0022f), yel);
                        foreach (float lv in new[] { 0.5f, 1f })
                        {
                            foreach (float pz in new[] { -hz, hz }) boxes.Add(st.Base + rot * new Vector3(0, H * lv, pz), rot, new Vector3(hx * 2, 0.0018f, 0.0018f), lv < 1 ? dk : yel);
                            foreach (float px in new[] { -hx, hx }) boxes.Add(st.Base + rot * new Vector3(px, H * lv, 0), rot, new Vector3(0.0018f, 0.0018f, hz * 2), lv < 1 ? dk : yel);
                        }
                        if (building && bage < 0.95f && Time.frameCount % 5 == 0)
                        {
                            float a = Random.value * Mathf.PI * 2;
                            AddDust(st.Base + rot * new Vector3(Mathf.Cos(a) * hx, 0.004f + Random.value * H * 0.6f, Mathf.Sin(a) * hz), 0.012f);
                        }
                    }
                }
                if (st.h > 0)
                {
                    if (max <= 1) continue;
                    float fl = armorFlash.GetValueOrDefault(st.id); if (fl > 0) armorFlash[st.id] = fl - Time.deltaTime * 2.5f;
                    // 一小块正对玩家的“电池牌”：深色底板 + 一格一格的护甲条（亮 = 还在，暗 = 打掉了）
                    var c0 = MonsterGame.TopOf(st) + Vector3.up * 0.014f; const float cw = 0.0085f, ch = 0.0055f, gap = 0.0105f;
                    var toHead = game.headPos - c0; toHead.y = 0; var face = toHead.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(-toHead.normalized, Vector3.up) : Quaternion.identity;
                    var right = face * Vector3.right;
                    boxes.Add(c0 + face * Vector3.forward * 0.0012f, face, new Vector3(gap * max + 0.004f, ch + 0.004f, 0.0012f), new Color(0.07f, 0.06f, 0.1f), 0);
                    for (int i = 0; i < max; i++)
                    {
                        var p = c0 + right * ((i - (max - 1) * 0.5f) * gap);
                        bool lit = i < st.armor; var dim = new Color(0.22f, 0.2f, 0.28f);
                        var c = lit ? Color.Lerp(new Color(1f, 0.82f, 0.2f), Color.white, Mathf.Clamp01(fl)) : (i == st.armor && fl > 0 ? Color.Lerp(dim, Color.white, Mathf.Clamp01(fl)) : dim);
                        boxes.Add(p, face, new Vector3(cw, ch, 0.002f), c, lit || fl > 0 ? 1 : 0);
                    }
                    continue;
                }
                if (st.restart < 0 || game.level.restart <= 0) continue;
                float k = Mathf.Clamp01(st.restart / game.level.restart);
                bool hurry = st.restart < 5;
                float pulse = hurry ? 0.65f + 0.35f * Mathf.Sin(t * 9) : 1;   // 约 1.4Hz，不是频闪
                const int N = 16; float R = Mathf.Max(st.fw, st.fd) * 0.62f; int litN = Mathf.CeilToInt(k * N);
                var cOn = hurry ? new Color(1f, 0.3f, 0.3f) * pulse : col;
                for (int i = 0; i < N; i++)
                {
                    float a = (i + 0.5f) / N * Mathf.PI * 2;
                    var p = st.Base + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * R + Vector3.up * 0.012f;
                    bool lit = i < litN;
                    boxes.Add(p, Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), new Vector3(0.0035f, 0.0035f, R * 2 * Mathf.PI / N * 0.62f), lit ? cOn : new Color(0.22f, 0.2f, 0.28f, 0.6f), lit ? 1 : 0);
                }
                // 中间的光柱：重启进度（越高越接近通电）
                float h = Mathf.Lerp(0.008f, 0.07f, 1 - k);
                boxes.Add(st.Base + Vector3.up * (0.006f + h * 0.5f), Quaternion.Euler(0, t * 60, 0), new Vector3(0.006f, h, 0.006f), cOn, 1);
            }
        }

        // ── 电站护盾罩（Shaders/StationShield）──
        class StSh { public Transform t; public Material m; public bool on; public float pop = 1; }
        readonly Dictionary<int, StSh> stShields = new();
        class Shard { public Vector3 p, v, spin, s; public Quaternion r; public float age, life; public Color c; }
        readonly List<Shard> shards = new();
        readonly List<(Vector3 c, float R, float t)> clearings = new();
        readonly Dictionary<int, float> buildFx = new();
        readonly List<(Vector3 p, float age)> flashes = new();
        static Mesh domeMesh;
        static Vector3 DomeP(int ring, int seg, int R, int S)
        {
            float th = ring / (float)R * Mathf.PI * 0.5f, a = (seg + (ring % 2) * 0.5f) / S * Mathf.PI * 2;
            return new Vector3(Mathf.Sin(th) * Mathf.Cos(a), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(a));
        }
        /// <summary>低面数半球（半径 1，顶在 +Y）：每个三角面独立顶点，uv = 重心坐标（画边线用），uv2.x = 这一面的随机号（裂纹 / 缺块用）</summary>
        static Mesh Dome()
        {
            if (domeMesh) return domeMesh;
            const int R = 3, S = 9;
            var V = new List<Vector3>(); var N = new List<Vector3>(); var U = new List<Vector2>(); var U2 = new List<Vector2>(); var T = new List<int>();
            var rnd = new System.Random(7);
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(n, a + b + c) < 0) { (b, c) = (c, b); n = -n; }
                float id = (float)rnd.NextDouble(); int o = V.Count;
                V.Add(a); V.Add(b); V.Add(c); N.Add(n); N.Add(n); N.Add(n);
                U.Add(new Vector2(1, 0)); U.Add(new Vector2(0, 1)); U.Add(new Vector2(0, 0));
                for (int k = 0; k < 3; k++) { U2.Add(new Vector2(id, 0)); T.Add(o + k); }
            }
            for (int ring = 0; ring < R; ring++)
                for (int seg = 0; seg < S; seg++)
                {
                    if (ring == 0) { Tri(Vector3.up, DomeP(1, seg, R, S), DomeP(1, seg + 1, R, S)); continue; }
                    Vector3 a = DomeP(ring, seg, R, S), b = DomeP(ring, seg + 1, R, S), c = DomeP(ring + 1, seg, R, S), d = DomeP(ring + 1, seg + 1, R, S);
                    if (ring % 2 == 1) { Tri(a, b, d); Tri(a, d, c); } else { Tri(a, b, c); Tri(b, d, c); }
                }
            domeMesh = new Mesh { name = "station_dome" };
            domeMesh.SetVertices(V); domeMesh.SetNormals(N); domeMesh.SetUVs(0, U); domeMesh.SetUVs(1, U2); domeMesh.SetTriangles(T, 0); domeMesh.RecalculateBounds();
            return domeMesh;
        }
        Vector3 DomeScale(Building st) => new Vector3(st.fw * 0.78f, MonsterGame.HeightOf(st) * 1.25f + 0.012f, st.fd * 0.82f);
        StSh ShieldOf(Building st, Color col)
        {
            if (stShields.TryGetValue(st.id, out var s) && s.t) return s;
            var sh = Shader.Find("Monster/StationShield"); if (!sh) return null;
            var go = new GameObject("StationShield"); go.transform.SetParent(fxRoot, false);
            go.transform.SetPositionAndRotation(st.Base, Quaternion.LookRotation(-st.zone.v, Vector3.up));
            go.AddComponent<MeshFilter>().sharedMesh = Dome();
            var m = new Material(sh) { name = "stationShield" }; m.SetColor("_Color", Color.Lerp(col, Color.white, 0.25f));
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            go.SetActive(false);
            return stShields[st.id] = new StSh { t = go.transform, m = m };
        }
        /// <summary>护盾碎掉：罩子上各处崩出一片片亮色的碎片（往外飞、翻着落下、缩没）+ 一圈扩开的光环 + “咔嚓！”</summary>
        void ShatterShield(Building st, Color col)
        {
            var sc = DomeScale(st); var rot = Quaternion.LookRotation(-st.zone.v, Vector3.up);
            for (int i = 0; i < 26; i++)
            {
                var d = Fib(i, 52); d.y = Mathf.Abs(d.y);
                var p = st.Base + rot * Vector3.Scale(d, sc);
                var outv = (p - st.Base).normalized;
                shards.Add(new Shard
                {
                    p = p, v = outv * Random.Range(0.12f, 0.3f) + Vector3.up * Random.Range(0.05f, 0.2f), r = Random.rotation, spin = Random.insideUnitSphere * 10,
                    s = new Vector3(Random.Range(0.005f, 0.011f), 0.001f, Random.Range(0.004f, 0.008f)), life = Random.Range(0.6f, 0.95f), c = i % 4 == 0 ? Color.white : Color.Lerp(col, Color.white, i % 2 == 0 ? 0.15f : 0.4f),
                });
            }
            bursts.Add(new Burst { pos = st.Base + Vector3.up * sc.y * 0.5f, r = Mathf.Max(sc.x, sc.z) * 0.5f, life = 0.5f, kind = "blast", dirs = new[] { Vector3.right, Vector3.left, Vector3.forward } });
            pow?.Show("crash", st.Base + Vector3.up * (sc.y + 0.04f));
            MonsterAudio.Play("sfx_shield", st.Base, 0.9f, 1.25f);
        }
        void UpdateStationFx(float dt)
        {
            // 碎片
            for (int i = shards.Count - 1; i >= 0; i--)
            {
                var s = shards[i]; s.age += dt;
                if (s.age >= s.life) { shards.RemoveAt(i); continue; }
                s.v += Vector3.down * (1.4f * dt); s.p += s.v * dt; s.r = Quaternion.Euler(s.spin * (dt * Mathf.Rad2Deg)) * s.r;
                float k = s.age / s.life;
                boxes.Add(s.p, s.r, s.s * (k < 0.7f ? 1 : 1 - (k - 0.7f) / 0.3f), s.c, 1);
            }
            // 清场：重启前把这块地上的瓦砾、土堆收走（缩没）+ 一阵阵扬尘，像被清运了
            for (int i = clearings.Count - 1; i >= 0; i--)
            {
                var (c, R, t) = clearings[i]; float t1 = t + dt;
                if (Mathf.FloorToInt(t / 0.14f) != Mathf.FloorToInt(t1 / 0.14f) && t < 0.8f) { var o = Random.insideUnitCircle * R * 0.7f; AddDust(c + new Vector3(o.x, 0.004f, o.y), 0.016f); }
                float shrink = Mathf.Exp(-dt * 7);
                for (int k = rubs.Count - 1; k >= 0; k--)
                {
                    var r = rubs[k]; var d = r.p - c;
                    if (d.x * d.x + d.z * d.z > R * R || Mathf.Abs(d.y) > 0.09f) continue;
                    r.s *= shrink; r.hasM = false; if (r.s.x < 0.0006f) rubs.RemoveAt(k);
                }
                for (int k = mounds.Count - 1; k >= 0; k--)
                {
                    var m = mounds[k]; var d = m.p - c;
                    if (d.x * d.x + d.z * d.z > R * R || Mathf.Abs(d.y) > 0.09f) continue;
                    m.s *= shrink; if (m.s.x < 0.002f) mounds.RemoveAt(k);
                }
                if (t1 > 1.3f) clearings.RemoveAt(i); else clearings[i] = (c, R, t1);
            }
            // 枪口火光（0.12 秒）：白芯 + 六根黄橙尖刺
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                var (p, age) = flashes[i]; age += dt;
                if (age > 0.12f) { flashes.RemoveAt(i); continue; }
                float k = 1 - age / 0.12f;
                blobs.Add(p, 0.014f * k, Color.white, 1);
                for (int j = 0; j < 6; j++) { var d = Fib(j, 6); cones.Add(p + d * 0.004f, YTo(d), new Vector3(0.006f, 0.024f * k, 0.006f), j % 2 == 0 ? new Color(1f, 0.86f, 0.2f) : new Color(1f, 0.5f, 0.12f), 1); }
                flashes[i] = (p, age);
            }
        }

        Transform cam0 => Camera.main ? Camera.main.transform : transform;
        // ═══════════ 广播台词的触发 ═══════════
        //  开局 1.5 秒：警告并威胁核打击；拆楼时：喊话让巨兽撤离（每 22 秒最多一句，三句轮换）；升星 / 塔倒 / 血低各一句；
        //  核打击倒计时 60 / 30 / 10 秒报时；发射、倒下、通关各一句。玩法关不播（那里是教学提示的地方）
        float voLeaveCd = 12; int voLeaveI; bool voStart, voWeak; float voLastLeft = 1e9f, voMiscCd;
        void TickVoice(float dt)
        {
            if (game.tut != null || game.result != null || game.Winning) return;
            voLeaveCd -= dt; voMiscCd -= dt;
            if (!voStart && game.time > 1.5f) { voStart = true; MonsterAudio.Voice("start", true); }
            float left = game.sealMax - game.seal;
            foreach (var (th, key) in new[] { (60f, "nuke60"), (30f, "nuke30"), (10f, "nuke10") })
                if (voLastLeft > th && left <= th && game.time > 13) MonsterAudio.Voice(key, true);   // 时限短的关开局就不到 60 秒：不让报时打断开场广播
            voLastLeft = left;
            if (!voWeak && game.hp < Cfg.HpMax * 0.3f) { voWeak = true; MonsterAudio.Voice("weak"); }
        }
        void VoiceEvent(Ev ev)
        {
            if (game.tut != null) return;
            switch (ev.type)
            {
                case "collapse": if (voLeaveCd <= 0 && ev.cause is "smash" or "thrown" or "chain" && game.time > 10 && MonsterAudio.Voice("leave" + (1 + voLeaveI % 3))) { voLeaveI++; voLeaveCd = 22; } break;
                case "star_up": if (voMiscCd <= 0 && MonsterAudio.Voice("reinforce")) voMiscCd = 15; break;
                case "tower_down": if (game.city.towers.Any(tw => tw.h > 0)) MonsterAudio.Voice("tower", true); voLeaveCd = Mathf.Max(voLeaveCd, 8); break;   // 最后一座倒下不喊“集中火力”：紧接着就是“全员撤离”
                case "nuke_launch": MonsterAudio.Voice("nuke_launch", true); break;
                case "lose": if (ev.cause == "hp") MonsterAudio.Voice("down", true); break;
            }
        }
        // ═══════════ 核打击（11-12 用户：倒计时走完来一发核弹，一击致命）═══════════
        // 11-12 用户：“什么都要有个过程”“核弹要真的打到玩家脸上，想清楚导弹从哪来 —— 可以从桌子后面出现，还要有 危险 危险 危险 的警报”。
        // 全程 6.5 秒（Cfg.NukeFall）：
        //   0～2.2s  警报：眼前红色大字“危险！危险！危险！”、每 0.7 秒一声警报、视野四周红框慢慢脉动、牌子上“核弹来袭！”；
        //   1.2～3.4s 升空：导弹从桌子远边后面（桌面以下）竖直升起来，拖着一根烟柱，升到头顶以上；
        //   3.4～6.5s 俯冲：在最高点转向，朝玩家的脸越飞越快、越来越大，威胁指示弧一直指着它；
        //   命中：白光吞没视野（核爆）→ 眼睛合上 → 结算。
        Vector3 NukeTarget => game.headPos;
        float nukeBeep; Vector3 nukePos;
        void DrawNuke()
        {
            if (game.nukeT < 0 || game.result != null) return;
            float T = game.nukeT, t = Time.time; var z = game.city.zones[0];
            // 警报声 + 红框脉动
            if (T < 3.2f && T >= nukeBeep) { nukeBeep = T + 0.7f; MonsterAudio.Play("sfx_star_up", null, 0.9f, 0.75f); }
            overlay?.Alarm(T < 3.4f ? 0.2f + 0.1f * Mathf.Sin(T * 7f) : 0.18f + 0.3f * Mathf.InverseLerp(3.4f, Cfg.NukeFall, T));
            if (T < 1.2f) return;
            var silo = z.Point(z.w * 0.5f, -0.16f, -0.35f);                         // 桌子远边后面、桌面以下 35cm
            var apex = silo + Vector3.up * Mathf.Max(1.15f, game.headPos.y - silo.y + 0.45f);   // 升到比头高 45cm
            Vector3 pos, dir;
            if (T < 3.4f)
            {
                float k = Mathf.InverseLerp(1.2f, 3.4f, T); k = 1 - (1 - k) * (1 - k);
                pos = Vector3.Lerp(silo, apex, k); dir = Vector3.up;
            }
            else
            {
                float k = Mathf.InverseLerp(3.4f, Cfg.NukeFall, T); k = k * k;      // 越飞越快
                var ctrl = apex + (game.headPos - apex).normalized * 0.25f + Vector3.up * 0.2f;
                Vector3 B(float u) => (1 - u) * (1 - u) * apex + 2 * (1 - u) * u * ctrl + u * u * game.headPos;
                pos = B(k); dir = (B(Mathf.Min(1, k + 0.02f)) - pos); dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : (game.headPos - pos).normalized;
                if (!nukeDive) { nukeDive = true; MonsterAudio.Play("sfx_incoming", pos, 1f, 0.6f); }
            }
            nukePos = pos;
            // 弹体：正式模型（Tools/blender_military.py 的 nuke()：喷口、尾翼、鸭翼、警示带、弹头、两枚捆绑助推器；三渲二材质带描边），全长 26cm，慢慢自转
            if (!nukeModel)
            {
                var pf = Resources.Load<GameObject>("Modules/military/nuke");
                if (pf)
                {
                    nukeModel = Instantiate(pf).transform; nukeModel.name = "Nuke"; nukeModel.SetParent(fxRoot, false);
                    var m = NukeMat();
                    Bounds lb = default; bool any = false;
                    foreach (var r in nukeModel.GetComponentsInChildren<MeshRenderer>()) { r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; if (!any) { lb = r.bounds; any = true; } else lb.Encapsulate(r.bounds); }
                    float len = Mathf.Max(lb.size.x, Mathf.Max(lb.size.y, lb.size.z));
                    nukeModel.localScale = Vector3.one * (len > 1e-4f ? NukeLen / len * nukeModel.localScale.x : 0.028f);
                    // 模型的弹头朝哪根轴：包围盒最长的那根；正负号用“细的一头是弹头”判断不了，按导出约定（弹头朝模型 −Z）定，截图核对过
                    nukeNose = Quaternion.Euler(0, 180, 0);
                }
            }
            var up = Mathf.Abs(dir.y) > 0.95f ? Vector3.forward : Vector3.up;
            var rot = Quaternion.LookRotation(dir, up) * Quaternion.Euler(0, 0, T * 50);
            if (nukeModel) { nukeModel.gameObject.SetActive(true); nukeModel.SetPositionAndRotation(pos, rot * nukeNose); }
            // 尾焰：主喷口三层（橙 → 黄 → 白芯）一长一短地抖 + 喷口外一圈马赫环；两枚助推器各一小股
            var tail = pos - dir * NukeLen * 0.5f; float fl = 1 + 0.18f * Mathf.Sin(t * 37) + 0.1f * Mathf.Sin(t * 61);
            cones.Add(tail, YTo(-dir), new Vector3(0.036f, 0.17f * fl, 0.036f), new Color(1f, 0.42f, 0.08f), 1);
            cones.Add(tail, YTo(-dir), new Vector3(0.026f, 0.12f * fl, 0.026f), new Color(1f, 0.8f, 0.2f), 1);
            cones.Add(tail, YTo(-dir), new Vector3(0.014f, 0.07f * fl, 0.014f), Color.white, 1);
            for (int i = 0; i < 2; i++) rings.Add(tail - dir * (0.035f + i * 0.04f) * fl, YTo(dir), Vector3.one * (0.016f - i * 0.004f), new Color(1f, 0.9f, 0.6f, 0.8f), 1);
            foreach (int sgn in new[] { -1, 1 })
            {
                var side = rot * (Quaternion.Euler(0, 0, 135) * Vector3.right) * (sgn * NukeLen * 0.115f);
                cones.Add(tail + side + dir * NukeLen * 0.05f, YTo(-dir), new Vector3(0.014f, 0.07f * fl, 0.014f), new Color(1f, 0.62f, 0.12f), 1);
            }
            blobs.Add(tail - dir * 0.02f, 0.05f, new Color(1f, 0.5f, 0.12f, 0.45f), 1);
            // 烟：每帧两团带描边的卡通烟，先白后灰，慢慢散开；升空阶段更粗（烟柱立在桌子后面），俯冲阶段细长
            bool rising = T < 3.4f;
            for (int i = 0; i < (rising ? 2 : 1); i++)
                puffs.Add(new Puff { pos = tail - dir * (0.05f + i * 0.03f) + Random.insideUnitSphere * 0.012f, vel = Random.insideUnitSphere * (rising ? 0.035f : 0.02f) - dir * 0.05f, size = rising ? 0.04f : 0.024f, life = rising ? 2.2f : 1.1f, col = Random.value < 0.5f ? new Color(0.97f, 0.95f, 0.92f) : new Color(0.8f, 0.78f, 0.82f), cartoon = true });
            // 刚从桌子后面冒出来的那一下：发射井口一圈扬尘 + 火光
            if (!nukeOut && pos.y > z.origin.y - 0.1f) { nukeOut = true; for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4; AddDust(silo + new Vector3(Mathf.Cos(a) * 0.08f, 0.3f, Mathf.Sin(a) * 0.08f), 0.06f); } AddBlast(silo + Vector3.up * 0.25f, 0.09f); MonsterAudio.Play("sfx_blast", silo, 0.8f, 0.7f); }
        }
        const float NukeLen = 0.26f;
        Transform nukeModel; Quaternion nukeNose = Quaternion.identity; bool nukeOut; Material nukeMat;
        /// <summary>核弹的色板材质（和追兵同一套 16 格色板、三渲二 + 描边）</summary>
        Material NukeMat()
        {
            if (nukeMat) return nukeMat;
            var pal = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "nuke_pal" };
            var emi = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            string[] cols = { "#e9e6de", "#232c48", "#ff2d8a", "#4a4f57", "#26282d", "#ffb020", "#ece9e2", "#5b6340", "#f2c53a", "#1d2a3a", "#16161a", "#fff3c8", "#e8222e", "#2a7bff", "#2a2a30", "#c9ccd2" };
            for (int i = 0; i < 16; i++) { pal.SetPixel(i, 0, CityMaterials.Hex(cols[i])); emi.SetPixel(i, 0, Color.black); }
            emi.SetPixel(5, 0, CityMaterials.Hex("#ffb020")); emi.SetPixel(2, 0, CityMaterials.Hex("#ff2d8a") * 0.5f); emi.SetPixel(12, 0, CityMaterials.Hex("#e8222e") * 0.35f);
            pal.Apply(); emi.Apply();
            nukeMat = new Material(CityMaterials.Toon) { name = "nuke" };
            nukeMat.SetTexture("_BaseMap", pal); nukeMat.SetTexture("_EmissionMap", emi);
            nukeMat.SetColor("_EmissionColor", new Color(2.2f, 2.2f, 2.2f)); nukeMat.SetFloat("_NightGlow", 1); nukeMat.SetFloat("_OutlineWidth", 0.0014f);
            return nukeMat;
        }
        bool nukeDive;

        // ═══════════ 手背上的生命 / 怒气条（11-12 用户：手背那几条甲片看不出是血量；怒气满了也不知道）═══════════
        // 画在左手手背上方 3cm（左手不在就右手），一直朝着眼睛：上面一排 10 格生命（绿 → 黄 → 红，左边一个小十字），下面一排细的怒气（青；满了整排变粉、慢慢呼吸）
        void DrawHandGauge(GameInput gi)
        {
            if (gi?.hands == null || game.result != null) return;
            HandIn h = null; foreach (var x in gi.hands) if (x != null && x.present && (h == null || x.side == "left")) h = x;
            if (h == null) return;
            var c = h.palm - h.palmN * 0.03f + Vector3.up * 0.012f;
            var toHead = game.headPos - c; if (toHead.sqrMagnitude < 0.01f) return;
            var face = Quaternion.LookRotation(-toHead.normalized, Vector3.up); var right = face * Vector3.right; var up = face * Vector3.up;
            float hp = Mathf.Clamp01(game.hp / Cfg.HpMax), rg = Mathf.Clamp01(game.rage / Cfg.RageMax), t = Time.time;
            const float W = 0.066f, seg = W / 10;
            // （特效着色器会把发光体提亮，所以用深一档的颜色，不然发白）
            var hc = hp > 0.5f ? new Color(0.08f, 0.8f, 0.25f) : hp > 0.3f ? new Color(0.95f, 0.62f, 0.05f) : new Color(0.95f, 0.1f, 0.14f) * (0.72f + 0.28f * Mathf.Sin(t * 7.5f));
            hc = Color.Lerp(hc, Color.white, Mathf.Clamp01(hurtFlash));
            boxes.Add(c + face * Vector3.forward * 0.001f, face, new Vector3(W + 0.022f, 0.021f, 0.001f), new Color(0.06f, 0.05f, 0.1f, 0.85f));
            boxes.Add(c - right * (W / 2 + 0.005f) + up * 0.004f, face, new Vector3(0.007f, 0.0024f, 0.0015f), new Color(0.32f, 1f, 0.5f), 1);   // 小十字
            boxes.Add(c - right * (W / 2 + 0.005f) + up * 0.004f, face, new Vector3(0.0024f, 0.007f, 0.0015f), new Color(0.32f, 1f, 0.5f), 1);
            bool full = rg >= 0.999f; var rc = full ? new Color(0.95f, 0.1f, 0.55f) * (0.75f + 0.25f * Mathf.Sin(t * 6)) : new Color(0.05f, 0.55f, 0.95f);
            for (int i = 0; i < 10; i++)
            {
                var p = c + right * ((i - 4.5f) * seg + 0.004f);
                bool on = hp * 10 > i + 0.01f;
                boxes.Add(p + up * 0.004f, face, new Vector3(seg * 0.8f, 0.007f, 0.0015f), on ? hc : new Color(0.2f, 0.18f, 0.26f), on ? 1 : 0);
                bool ron = rg * 10 > i + 0.01f;
                boxes.Add(p - up * 0.0045f, face, new Vector3(seg * 0.8f, 0.0035f, 0.0015f), ron ? rc : new Color(0.16f, 0.15f, 0.22f), ron ? 1 : 0);
            }
        }

        // ═══════════ 威胁指示（11-12 用户给的参考：视野中心外一圈弧形标志，指出敌人 / 来袭方向）═══════════
        readonly List<(Vector3 pos, float t)> fired = new();
        readonly List<(Vector3 pos, float s, int kind)> threatBuf = new();
        void UpdateThreats(float dt)
        {
            threatBuf.Clear();
            for (int i = fired.Count - 1; i >= 0; i--) { var (p, t) = fired[i]; t -= dt; if (t <= 0) fired.RemoveAt(i); else { fired[i] = (p, t); threatBuf.Add((p, Mathf.Clamp01(t / 0.9f) * 0.75f, 0)); } }
            foreach (var e in game.enemies) if (!e.dead && e.lockT > 0) threatBuf.Add((e.pos, 0.7f + 0.3f * Mathf.Sin(Time.time * 6), 0));   // 导弹车锁定中
            if (game.nukeT >= 1.2f && game.result == null) threatBuf.Add((nukePos, 1f, 1));   // 核弹：红弧一直指着它
            foreach (var p in game.projectiles)
            {
                if (p.heldBy != null || Vector3.Dot(p.vel, game.headPos - p.pos) <= 0) continue;
                float d = Vector3.Distance(p.pos, game.headPos);
                if (d < 1.6f) threatBuf.Add((p.pos, Mathf.Clamp01(1.25f - d * 0.6f) * (p.kind == "bullet" ? 0.75f : 1f), 1));
            }
            overlay?.SetThreats(threatBuf);
        }

        // ═══════════ 回血球（11-12）：废墟上方浮着的一颗绿色生命球，白十字，外面一圈慢慢转的环；快消失时缩小 ═══════════
        void DrawOrbs()
        {
            float t = Time.time;
            foreach (var o in game.orbs)
            {
                float pop = Mathf.Clamp01(o.age / 0.3f); pop = 1 + (pop < 1 ? Mathf.Sin(pop * Mathf.PI) * 0.4f - (1 - pop) : 0);
                float left = o.life - o.age; float fade = left < 2.5f ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(left * 3.2f)) : 1;   // 最后 2.5 秒一明一暗（约 1Hz）
                float sc = pop * (left < 0.4f ? left / 0.4f : 1);
                var p = o.pos + Vector3.up * (Mathf.Sin(t * 2.2f + o.id) * 0.006f);
                var green = new Color(0.25f, 1f, 0.5f);
                var halo = green; halo.a = 0.3f * fade; blobs.Add(p, 0.05f * sc, halo, 1);
                var core = green; core.a = fade; blobs.Add(p, 0.03f * sc, core, 1);
                var toHead = game.headPos - p; var face = toHead.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(-toHead.normalized, Vector3.up) : Quaternion.identity;
                var fwd = face * Vector3.back * 0.017f * sc;
                boxes.Add(p + fwd, face, new Vector3(0.018f, 0.006f, 0.002f) * sc, Color.white, 1);
                boxes.Add(p + fwd, face, new Vector3(0.006f, 0.018f, 0.002f) * sc, Color.white, 1);
                rings.Add(p, Quaternion.Euler(70, t * 60, 0), Vector3.one * 0.032f * sc, new Color(0.7f, 1f, 0.8f, 0.8f * fade), 1);
                // 往上冒的小加号火花
                for (int i = 0; i < 3; i++)
                {
                    float ph = Mathf.Repeat(t * 0.8f + i * 0.33f + o.id * 0.17f, 1);
                    var sp = p + new Vector3(Mathf.Sin(i * 2.1f + o.id) * 0.02f, 0.02f + ph * 0.05f, Mathf.Cos(i * 2.1f + o.id) * 0.02f);
                    boxes.Add(sp, face, new Vector3(0.006f, 0.002f, 0.002f) * (1 - ph) * sc, green, 1); boxes.Add(sp, face, new Vector3(0.002f, 0.006f, 0.002f) * (1 - ph) * sc, green, 1);
                }
            }
        }
        void OnOrbTake(Ev ev)
        {
            if (!ev.pos.HasValue) return; var p = ev.pos.Value;
            pow?.ShowText("+" + Mathf.Max(1, Mathf.RoundToInt(ev.n)), p + Vector3.up * 0.03f, new Color(0.3f, 1f, 0.55f));
            for (int i = 0; i < 14; i++) shards.Add(new Shard { p = p, v = Fib(i, 14) * 0.18f + Vector3.up * 0.12f, r = Random.rotation, spin = Random.insideUnitSphere * 9, s = new Vector3(0.007f, 0.007f, 0.0015f), life = 0.55f, c = i % 2 == 0 ? new Color(0.3f, 1f, 0.55f) : Color.white });
            MonsterAudio.Play("sfx_heal", p, 0.9f);
            hurtFlash = 0;
        }

        void OnArmorHit(Ev ev)
        {
            var b = B(game, ev.id); if (b == null) return;
            armorFlash[b.id] = 1;
            var p = ev.pos ?? MonsterGame.TopOf(b);
            MonsterAudio.Play("sfx_clang", p, 0.9f);
            pow?.Show("clang", p + Vector3.up * 0.05f);
            // 火星：十几颗黄白小方块朝四周溅开
            for (int i = 0; i < 12; i++)
                rubs.Add(new Rub
                {
                    p = p + Random.insideUnitSphere * 0.012f, s = Vector3.one * Random.Range(0.002f, 0.0045f), r = Random.rotation, chunk = true,
                    v = new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.08f, 0.26f), Random.Range(-0.2f, 0.2f)), spin = Random.insideUnitSphere * 12,
                    z = b.zone, c = i % 2 == 0 ? new Color(1f, 0.9f, 0.4f) : new Color(1f, 0.6f, 0.2f), floor = b.zone.origin.y, check = Random.value * 0.3f,
                });
            // 电站本体抖一下
            var v = builder.View(b.id); if (v) { builder.Detach(v); jolts.Add((v.transform, v.transform.localPosition, 0)); }
            shake = Mathf.Max(shake, 0.15f);
        }
        readonly List<(Transform t, Vector3 p0, float age)> jolts = new();
        void UpdateJolts(float dt)
        {
            for (int i = jolts.Count - 1; i >= 0; i--)
            {
                var (t, p0, age) = jolts[i]; age += dt;
                if (!t) { jolts.RemoveAt(i); continue; }
                if (age >= 0.3f) { t.localPosition = p0; jolts.RemoveAt(i); continue; }
                float k = 1 - age / 0.3f;
                t.localPosition = p0 + new Vector3(Mathf.Sin(age * 90) * 0.0025f * k, -0.002f * k, Mathf.Cos(age * 70) * 0.002f * k);
                jolts[i] = (t, p0, age);
            }
        }

        // 挨打了（11-11 用户：看不到自己的血量）：原来只有一声音效，画面上什么都没有。
        // 现在：伤害来的方向弹一个红色的“−5”，记分牌上的体力条闪一下（见 HpBar）
        public float hurtFlash; HurtOverlay overlay;
        void OnHurt(Ev ev)
        {
            hurtFlash = 1;
            var head = game.headPos; var from = ev.pos ?? head + Vector3.forward * 0.5f;
            var dir = from - head; if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            var at = head + dir.normalized * Mathf.Clamp(dir.magnitude, 0.42f, 0.6f) + Vector3.down * 0.12f;   // 不贴脸：离头至少 42cm、略低于视线
            pow?.ShowText("-" + Mathf.Max(1, Mathf.RoundToInt(ev.dmg)), at, new Color(1f, 0.25f, 0.3f));
            overlay?.Hit(from, ev.dmg);
            // 碰到带护盾的封印塔被弹开也会掉血：在手碰到的地方打一串电火花 + “滋——”，玩家才知道这一下是怎么掉的
            if (ev.cause == "shield" && ev.pos.HasValue)
            {
                pow?.Show("zap", ev.pos.Value + Vector3.up * 0.04f);
                for (int i = 0; i < 10; i++) shards.Add(new Shard { p = ev.pos.Value, v = Random.insideUnitSphere * 0.25f + Vector3.up * 0.1f, r = Random.rotation, spin = Random.insideUnitSphere * 14, s = new Vector3(0.006f, 0.0012f, 0.003f), life = 0.4f, c = i % 2 == 0 ? Color.white : new Color(0.5f, 0.95f, 1f) });
            }
        }

        // ═══════════ 炮弹 / 导弹 / 炸弹 / 子弹 ═══════════
        void DrawProjectiles()
        {
            foreach (var p in game.projectiles)
            {
                var r = p.vel.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(p.vel) : Quaternion.identity;
                // 来袭的呼啸（11-12）：飞到离头 0.6m 以内、还在靠近时响一声，3D 播放 —— 背后 / 侧面来的也听得出方向
                if (!p.warned && p.heldBy == null && Vector3.Distance(p.pos, game.headPos) < 0.6f && Vector3.Dot(p.vel, game.headPos - p.pos) > 0)
                { p.warned = true; MonsterAudio.Play("sfx_incoming", p.pos, p.kind == "bullet" ? 0.85f : 1f, p.kind == "bullet" ? 1.5f : p.kind == "missile" ? 0.85f : 1.1f); }
                switch (p.kind)
                {
                    case "bullet":
                        {
                            // 11-12 用户：不知道自己是怎么掉血的 —— 原来子弹只有 1.5mm 粗、12mm 长，头显里根本看不见。
                            // 现在：5mm 粗的亮黄弹头 + 橙色光晕 + 9cm 长的曳光尾迹
                            var fw = r * Vector3.forward;
                            blobs.Add(p.pos, r, new Vector3(0.011f, 0.011f, 0.03f), new Color(1f, 0.95f, 0.6f), 1);
                            blobs.Add(p.pos, r, new Vector3(0.02f, 0.02f, 0.036f), new Color(1f, 0.5f, 0.1f, 0.4f), 1);
                            boxes.Add(p.pos - fw * 0.08f, r, new Vector3(0.004f, 0.004f, 0.13f), new Color(1f, 0.8f, 0.3f, 0.55f), 1);
                            break;
                        }
                    case "fist":
                        {
                            // 机甲的加农炮（11-13 重做：原来是一个灰疙瘩）：洋红色的等离子弹 —— 白芯 + 洋红弹体 + 淡光晕 + 两道反着转的青色环 + 一串越来越小的尾迹
                            var fw = r * Vector3.forward; float tt = Time.time;
                            var halo = new Color(0.9f, 0.16f, 0.5f, 0.35f);
                            blobs.Add(p.pos, 0.07f, halo, 1);
                            blobs.Add(p.pos, r, new Vector3(0.036f, 0.036f, 0.05f), new Color(0.9f, 0.16f, 0.5f), 1);
                            blobs.Add(p.pos + fw * 0.004f, 0.02f, new Color(1f, 0.85f, 0.92f), 1);
                            rings.Add(p.pos, r * Quaternion.Euler(90, 0, 0) * Quaternion.Euler(tt * 220, 0, 25), Vector3.one * 0.03f, new Color(0.15f, 0.8f, 0.9f, 0.9f), 1);
                            rings.Add(p.pos - fw * 0.012f, r * Quaternion.Euler(90, 0, 0) * Quaternion.Euler(-tt * 180, 0, -30), Vector3.one * 0.024f, new Color(0.15f, 0.8f, 0.9f, 0.7f), 1);
                            for (int j = 1; j <= 5; j++) { var c = j % 2 == 0 ? new Color(0.9f, 0.16f, 0.5f) : new Color(0.15f, 0.8f, 0.9f); c.a = 0.75f - j * 0.12f; blobs.Add(p.pos - fw * (0.03f * j) + r * new Vector3(Mathf.Sin(tt * 14 + j) * 0.004f, Mathf.Cos(tt * 11 + j) * 0.004f, 0), 0.026f - j * 0.0035f, c, 1); }
                            break;
                        }
                    case "shell": if (Time.frameCount % 3 == 0) puffs.Add(new Puff { pos = p.pos - r * Vector3.forward * 0.02f, vel = Random.insideUnitSphere * 0.008f, size = 0.006f, grow = 2f, life = 0.45f, col = new Color(0.8f, 0.78f, 0.76f) }); blobs.Add(p.pos, r, new Vector3(0.02f, 0.02f, 0.034f), new Color(1f, 0.7f, 0.23f), 1); blobs.Add(p.pos, r, new Vector3(0.03f, 0.03f, 0.04f), new Color(1f, 0.35f, 0.1f, 0.35f), 1); boxes.Add(p.pos - r * Vector3.forward * 0.07f, r, new Vector3(0.006f, 0.006f, 0.11f), new Color(1f, 0.42f, 0.1f, 0.6f), 1); break;
                    case "missile":
                        {
                            // 11-10：头显里看不清 → 加粗加长，卡通配色（白弹身 + 红弹头 + 黑十字尾翼 + 黄火舌），拖一条一团团的卡通白烟
                            var fw = r * Vector3.forward;
                            boxes.Add(p.pos, r, new Vector3(0.008f, 0.008f, 0.04f), new Color(0.97f, 0.96f, 0.92f));
                            boxes.Add(p.pos + fw * 0.006f, r, new Vector3(0.0084f, 0.0084f, 0.006f), new Color(1f, 0.25f, 0.45f));   // 粉色色环
                            cones.Add(p.pos + fw * 0.02f, r * Quaternion.Euler(90, 0, 0), new Vector3(0.008f, 0.012f, 0.008f), new Color(0.95f, 0.18f, 0.22f));
                            boxes.Add(p.pos - fw * 0.016f, r, new Vector3(0.018f, 0.0018f, 0.008f), new Color(0.12f, 0.1f, 0.16f));
                            boxes.Add(p.pos - fw * 0.016f, r, new Vector3(0.0018f, 0.018f, 0.008f), new Color(0.12f, 0.1f, 0.16f));
                            float fl = 0.022f + Mathf.Sin(Time.time * 40 + p.age * 7) * 0.005f;
                            cones.Add(p.pos - fw * (0.02f + fl * 0.5f), r * Quaternion.Euler(-90, 0, 0), new Vector3(0.009f, fl, 0.009f), new Color(1f, 0.85f, 0.2f), 1);
                            if (Time.frameCount % 2 == 0)
                                puffs.Add(new Puff { pos = p.pos - fw * 0.03f, vel = Random.insideUnitSphere * 0.01f, size = 0.007f, grow = 2.2f, life = 0.55f, col = new Color(0.98f, 0.97f, 0.95f) });
                            break;
                        }
                    case "bomb": blobs.Add(p.pos, r, new Vector3(0.012f, 0.012f, 0.02f), new Color(0.19f, 0.2f, 0.23f)); blobs.Add(p.pos - r * Vector3.forward * 0.012f, 0.006f, new Color(1f, 0.3f, 0.3f), 0.5f + 0.5f * Mathf.Sin(Time.time * 12)); break;
                }
            }
            // 被拍回去的炮弹
            foreach (var f in game.flying) if (f.kind == "shell") blobs.Add(f.pos, 0.008f, new Color(1f, 0.7f, 0.23f), 1);
            // 导弹预警（11-10 用户：那条射线看着头晕 —— 原来是车 → 玩家脸前一串每秒闪 8 次的红点 + 眼前一个圈）：
            // 不再往脸上画任何东西。改成车顶上方一个卡通“！”警示牌（黄底黑边，弹出后慢慢上下浮），管口慢慢变红发亮
            foreach (var e in game.enemies)
            {
                if (e.dead || e.lockT <= 0) continue;
                float lockDur = e.parked ? 1.6f : 3f, k = Mathf.Clamp01(1 - e.lockT / lockDur);
                float pop = Mathf.Min(1, k * lockDur / 0.25f); pop = 1 + (pop < 1 ? Mathf.Sin(pop * Mathf.PI) * 0.35f : 0) - (1 - Mathf.Min(1, k * lockDur / 0.25f));
                var top = e.pos + Vector3.up * (0.075f + Mathf.Sin(Time.time * 2.5f) * 0.004f);
                var toCam = game.headPos - top; toCam.y = 0;
                var rq = toCam.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toCam.normalized, Vector3.up) : Quaternion.identity;
                float sc = 0.9f * pop;
                var ink = new Color(0.1f, 0.06f, 0.14f); var yel = new Color(1f, 0.86f, 0.12f);
                boxes.Add(top + Vector3.up * 0.012f * sc, rq, new Vector3(0.011f, 0.026f, 0.003f) * sc, ink);          // 黑描边
                boxes.Add(top + Vector3.up * 0.012f * sc + rq * Vector3.forward * 0.0012f, rq, new Vector3(0.007f, 0.022f, 0.003f) * sc, yel, 0.8f);
                blobs.Add(top - Vector3.up * 0.012f * sc, rq, new Vector3(0.011f, 0.011f, 0.004f) * sc, ink);
                blobs.Add(top - Vector3.up * 0.012f * sc + rq * Vector3.forward * 0.0012f, rq, new Vector3(0.0075f, 0.0075f, 0.004f) * sc, yel, 0.8f);
                if (e.muzzle.HasValue) blobs.Add(e.muzzle.Value, 0.008f + 0.004f * k, new Color(1f, 0.2f, 0.3f), 0.5f + 0.5f * Mathf.Sin(Time.time * 4) * 0.5f + k * 0.5f);
            }
        }

        // ═══════════ 吐息：从嘴前到落点的一串翻滚的能量团 ═══════════
        // ═══════════ 能量波（11-10：原来的吐息从眼前喷出去会晕 → 从两手之间发出，霓虹动漫风）═══════════
        // 蓄力：两手之间一团能量球（白芯 + 青色光晕 + 粉色尖刺慢慢转），怒气不满时只聚到三成、发灰、散掉；
        // 光束：白芯（不透明、最亮）+ 青色中层 + 粉色外层，沿光束往前跑的青 / 粉光环 + 白色速度线，命中处一个黄白卡通爆炸星。
        // 不做频闪：所有脉动都在 3Hz 以下、幅度小。
        FxBatch beamCore, beamMid, beamOuter, beamBits; float beamAge;
        static readonly Color WaveCyan = new(0.35f, 0.95f, 1f), WavePink = new(1f, 0.35f, 0.75f), WaveYellow = new(1f, 0.9f, 0.3f);
        // 光柱用的深一档的青 / 品红：特效着色器会把发光体中间提亮 1.6 倍，用浅色会发白成一团淡蓝雾，霓虹感全没了
        static readonly Color WaveCyanDeep = new(0.06f, 0.6f, 0.95f), WavePinkDeep = new(0.95f, 0.1f, 0.55f);
        /// <summary>把圆锥 / 圆环的轴（本地 +Y）转到方向 d</summary>
        static Quaternion YTo(Vector3 d) => Quaternion.FromToRotation(Vector3.up, d);
        /// <summary>均匀撒在球面上的第 i 个方向（黄金角螺旋）：特效里当“固定的随机方向”用，不用每帧取随机数</summary>
        static Vector3 Fib(int i, int n)
        {
            float y = 1 - 2 * (i + 0.5f) / n, r = Mathf.Sqrt(Mathf.Max(0, 1 - y * y)), a = i * 2.39996f;
            return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }

        // 11-11 用户：大招要从两只手中间发出去、位置要对、光波要更好看。第二版：
        //  蓄力：两手中间的能量球（白芯 + 青色光晕）+ 从四周往球里吸的彩色能量线 + 两道陀螺环 + 一圈向四周散开的爆炸星尖刺；
        //  发射：手里一直留着一个比光束粗的能量球（光束的源头，一眼看得出是从手里出去的）→ 喇叭口收成光束；
        //        白芯 / 青 / 粉三层光柱，两条螺旋光带缠着光束往前钻，一团团能量脉冲往前冲，外圈白色速度线；刚发射时两道冲击环从手里炸开；
        //  命中：正对玩家的爆炸星 + 贴地往外扩的冲击环 + 溅出去的火星 + 拟声大字。
        //  （原来的尖刺公式写错了：最后多乘了一个绕 X 转 90°，所有尖刺都指向同一个方向叠成一坨，不是星形。）
        // 不做频闪：整体明暗脉动都在 3Hz 以下、幅度小；转动的只是细小的光带。
        void DrawBreath()
        {
            float t = Time.time;
            if (beamCore == null)
            {
                // 三层光柱套在一起、包围盒一样，半透明物体之间谁先画是不确定的（有的帧粉色外层盖在青色上面，整根变成淡紫色）
                // → 各用一份材质、指定渲染顺序：外层最先画，中层其次，其余特效（能量球、螺旋光带、爆炸星）在它们之后
                beamCore = new FxBatch(FxBatch.Tube(14), RubMat());
                beamOuter = new FxBatch(FxBatch.Tube(14), new Material(fxMat) { name = "fx_beamOuter", renderQueue = 2998 });
                beamMid = new FxBatch(FxBatch.Tube(14), new Material(fxMat) { name = "fx_beamMid", renderQueue = 2999 });
                beamBits = new FxBatch(FxBatch.Box(), fxMat);
            }
            // ── 蓄力球 ──
            if (game.charge > 0.01f && game.breathing <= 0)
            {
                var dir0 = game.waveDir.sqrMagnitude > 0.5f ? game.waveDir : Vector3.forward;
                var c = game.waveMid + dir0 * Cfg.WaveMuzzle; float k = game.charge;
                bool weak = game.waveWant;
                var rq = Quaternion.LookRotation(dir0);
                float r = 0.018f + 0.05f * k;   // 能量球直径
                var cy = weak ? Color.Lerp(WaveCyan, new Color(0.5f, 0.55f, 0.6f), 0.6f) : WaveCyan;
                blobs.Add(c, r * 0.6f, Color.white, 1);
                var halo = cy; halo.a = 0.5f; blobs.Add(c, r * (1.05f + 0.06f * Mathf.Sin(t * 12)), halo, 1);
                // 从四周往球里吸的能量线（越靠近越短、越亮）
                int nl = weak ? 6 : 14;
                for (int i = 0; i < nl; i++)
                {
                    float ph = Mathf.Repeat(t * 1.5f + i * 0.283f, 1);
                    var dv = Fib(i, nl); float dist = Mathf.Lerp(0.10f, r * 0.45f, ph * ph);
                    var col = weak ? cy : (i % 3 == 0 ? WavePink : i % 3 == 1 ? WaveCyan : WaveYellow); col.a = Mathf.Min(1, ph * 3);
                    beamBits.Add(c + dv * dist, Quaternion.LookRotation(dv), new Vector3(0.0024f, 0.0024f, 0.01f + 0.024f * (1 - ph)), col, 1);
                }
                if (!weak)
                {
                    // 两道陀螺环，绕着发射方向反着转
                    rings.Add(c, Quaternion.AngleAxis(t * 150, dir0) * rq * Quaternion.Euler(65, 0, 0), Vector3.one * r * 0.85f, WavePink, 1);
                    rings.Add(c, Quaternion.AngleAxis(-t * 120, dir0) * rq * Quaternion.Euler(-60, 0, 50), Vector3.one * r, WaveCyan, 1);
                    // 爆炸星尖刺：垂直于发射方向往四周（长短交替），随蓄力变长
                    for (int i = 0; i < 8; i++)
                    {
                        var rad = rq * Quaternion.Euler(0, 0, i * 45 + t * 40) * Vector3.up;
                        float spike = r * (i % 2 == 0 ? 1.1f : 0.7f) * (0.85f + 0.15f * Mathf.Sin(t * 5 + i));
                        cones.Add(c + rad * r * 0.38f, YTo(rad), new Vector3(r * 0.3f, spike, r * 0.3f), i % 2 == 0 ? WaveYellow : WavePink, 1);
                    }
                }
            }
            // ── 光束 ──
            bool on = game.breathing > 0 && game.beam.HasValue && game.breathEnd.HasValue;
            var o = on ? game.beam.Value.origin : Vector3.zero; var e = on ? game.breathEnd.Value : Vector3.zero;
            var d = e - o; float L = d.magnitude;
            if (!on || L < 0.01f) { if (!on) beamAge = 0; beamCore.Draw(); beamMid.Draw(); beamOuter.Draw(); beamBits.Draw(); return; }
            float age0 = beamAge; beamAge += Time.deltaTime;
            var dir = d / L; var rot = Quaternion.LookRotation(dir);
            float grow = Mathf.Clamp01(beamAge / 0.12f), fade = Mathf.Clamp01(game.breathing / 0.25f);
            float len = L * grow, w = (1 + 0.07f * Mathf.Sin(t * 14)) * fade;
            beamCore.Add(o, rot, new Vector3(0.02f * w, 0.02f * w, len), Color.white, 1);
            var mid = WaveCyanDeep; mid.a = 0.9f; beamMid.Add(o, rot, new Vector3(0.034f * w, 0.034f * w, len), mid, 1);
            var outer = WavePinkDeep; outer.a = 0.55f; beamOuter.Add(o, rot, new Vector3(0.052f * w, 0.052f * w, len), outer, 1);
            // 手里的能量球：光束的源头，比光束粗一圈
            float orb = (0.058f + 0.005f * Mathf.Sin(t * 9)) * Mathf.Lerp(0.4f, 1, fade);
            blobs.Add(o, orb * 0.62f, Color.white, 1);
            var oh = WaveCyanDeep; oh.a = 0.6f; blobs.Add(o, orb, oh, 1);
            // 球 → 光束的喇叭口
            var fl = WavePinkDeep; fl.a = 0.5f; cones.Add(o, YTo(dir), new Vector3(orb * 1.15f, 0.13f * grow, orb * 1.15f), fl, 1);
            // 源头的爆炸星（垂直于光束、长短交替、慢慢转）
            for (int i = 0; i < 10; i++)
            {
                var rad = rot * Quaternion.Euler(0, 0, i * 36 - t * 55) * Vector3.up;
                float sl = (i % 2 == 0 ? 0.062f : 0.036f) * fade * (0.9f + 0.1f * Mathf.Sin(t * 6 + i * 2));
                cones.Add(o + rad * orb * 0.36f, YTo(rad), new Vector3(0.02f, sl, 0.02f), i % 2 == 0 ? WaveYellow : WavePink, 1);
            }
            // 刚发射的 0.4 秒：两道冲击环从手里炸开、往前推
            if (beamAge < 0.4f)
            {
                float k = beamAge / 0.4f, ek = 1 - (1 - k) * (1 - k);
                var c1 = WavePink; c1.a = 1 - k; rings.Add(o + dir * (0.02f + 0.05f * ek), YTo(dir), Vector3.one * (0.035f + 0.085f * ek), c1, 1);
                var c2 = Color.white; c2.a = 1 - k; rings.Add(o + dir * (0.03f + 0.14f * ek), YTo(dir), Vector3.one * (0.03f + 0.05f * ek), c2, 1);
            }
            // 两条螺旋光带缠着光束往前钻（黄 / 白；从源头的球里慢慢张开）
            const float Step = 0.0125f, Pitch = 0.15f, Flow = 0.55f;
            float hr = 0.034f * w;
            for (int si = 0; si * Step < len && si < 200; si++)
            {
                float s0 = si * Step, s1 = s0 + Step;
                for (int st = 0; st < 2; st++)
                {
                    float a0 = (s0 - t * Flow) / Pitch * 360f + st * 180f, a1 = a0 + Step / Pitch * 360f;
                    var r0 = rot * Quaternion.Euler(0, 0, a0) * Vector3.up; var r1 = rot * Quaternion.Euler(0, 0, a1) * Vector3.up;
                    var p0 = o + dir * s0 + r0 * hr * Mathf.Clamp01(s0 / 0.08f); var p1 = o + dir * s1 + r1 * hr * Mathf.Clamp01(s1 / 0.08f);
                    beamBits.Add((p0 + p1) * 0.5f, Quaternion.LookRotation(p1 - p0, r0), new Vector3(0.0095f * w, 0.0032f, (p1 - p0).magnitude * 1.1f), st == 0 ? WaveYellow : Color.white, 1);
                }
            }
            // 沿光束往前冲的能量脉冲（拉长的亮团）
            for (float s0 = Mathf.Repeat(t * 1.6f, 0.24f); s0 < len; s0 += 0.24f)
            {
                var pc = WaveCyan; pc.a = 0.42f * Mathf.Clamp01(s0 / 0.1f);
                blobs.Add(o + dir * s0, rot, new Vector3(0.064f * w, 0.064f * w, 0.11f), pc, 1);
            }
            // 外圈速度线
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45 + 20, ph = Mathf.Repeat(t * 2.2f + i * 0.37f, 1);
                var off = rot * Quaternion.Euler(0, 0, ang) * Vector3.up * (0.058f + 0.012f * (i % 3));
                var sc = new Color(1, 1, 1, 0.7f * (1 - ph));
                beamBits.Add(o + off + dir * (ph * len), rot, new Vector3(0.0016f, 0.0016f, 0.07f), sc, 1);
            }
            // 命中处
            if (grow >= 1)
            {
                if (age0 < 0.12f) pow?.Show("boom", e + Vector3.up * 0.1f);   // 光束刚打到：拟声大字
                var toCam = (game.headPos - e).normalized; var face = Quaternion.LookRotation(toCam);
                float pul = 1 + 0.12f * Mathf.Sin(t * 14);
                blobs.Add(e, 0.046f * pul * fade, Color.white, 1);
                var ih = WaveCyanDeep; ih.a = 0.5f; blobs.Add(e, 0.074f * pul * fade, ih, 1);
                // 卡通爆炸星：正对玩家、尖刺朝四周
                for (int i = 0; i < 10; i++)
                {
                    var rad = face * Quaternion.Euler(0, 0, i * 36 + Mathf.Sin(t * 2 + i) * 6) * Vector3.up;
                    float sp = (i % 2 == 0 ? 0.058f : 0.034f) * pul * fade;
                    cones.Add(e + rad * 0.024f, YTo(rad), new Vector3(0.018f, sp, 0.018f), i % 2 == 0 ? WaveYellow : WavePink, 1);
                }
                // 贴着命中处往外扩的冲击环（两道错开）
                for (int i = 0; i < 2; i++)
                {
                    float ph = Mathf.Repeat(t * 2.2f + i * 0.5f, 1);
                    var rc = i == 0 ? WaveCyan : WavePink; rc.a = (1 - ph) * fade;
                    rings.Add(e + Vector3.up * 0.004f, Quaternion.identity, Vector3.one * (0.03f + 0.10f * ph), rc, 1);
                }
                // 溅出去的火星（往上往外抛，再落下）
                for (int i = 0; i < 12; i++)
                {
                    float ph = Mathf.Repeat(t * 1.7f + i * 0.618f, 1);
                    var dv = Fib(i, 24);
                    var sp = e + dv * (0.03f + 0.15f * ph) + Vector3.down * (0.12f * ph * ph);
                    beamBits.Add(sp, Quaternion.Euler(ph * 300 + i * 40, i * 77, 0), Vector3.one * 0.008f * (1 - ph) * fade, i % 2 == 0 ? WaveYellow : Color.white, 1);
                }
            }
            beamCore.Draw(); beamMid.Draw(); beamOuter.Draw(); beamBits.Draw();
        }

        // ═══════════ 被拔起的楼：拿在手里 → 扔出去 → 落地散开 ═══════════
        readonly Dictionary<int, Transform> uprooted = new();
        void OnUproot(Ev ev)
        {
            var held = game.held.Values.FirstOrDefault(h => h.kind == "chunk" && h.chunk != null && h.chunk.from == ev.id);
            if (held == null) return;
            // OnCollapse(uproot) 已经把整栋的各层挂到一个组下面
            if (!uprooted.TryGetValue(ev.id, out var grp) || !grp) return;
            uprooted.Remove(ev.id);
            carried[held.chunk] = grp;
            pow?.Show("uproot", grp.position + Vector3.up * 0.05f);
        }

        // ═══════════ 捏起来的民用车 / 树（11-11）：从场景里摘下来复制一份，跟手 → 飞 → 落地变残骸碎掉 ═══════════
        readonly Dictionary<object, Transform> props = new();
        object TakeProp(Vector3 p, float r)
        {
            foreach (var tr in Traffics)
                if (tr && tr.TryTake(p, r, out var mesh, out var mat, out var m))
                {
                    var go = new GameObject("HeldCar"); go.transform.SetParent(fxRoot, false);
                    go.transform.SetPositionAndRotation(m.GetColumn(3), m.rotation); go.transform.localScale = m.lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var key = new object(); props[key] = go.transform;
                    return key;
                }
            // 11-11 用户：树这类很小的东西不要能拿（太小了捏不住），但能被拍扁 / 压扁（见 CrushProps）
            return null;
        }

        // ───────── 被压扁的车（11-17 用户：楼压到车，车应该被压扁；车停下以后敌方载具会从停着的车里穿过去）─────────
        // 以前路上的车只有被手拍才有反应（拍成残骸翻两下、碎掉），楼倒在车上、瓦砾堆在车上、军车开过去，车都原样立在那儿、互相穿插。现在：
        //   · 压扁 FlattenCars：车在 0.1 秒里被压瘪（高度剩四分之一、摊开一点、歪着），留在原地不消失（最多留 80 辆）。
        //     触发：倒下的楼砸到的那一条、原地塌碎的楼的瓦砾堆范围、扔出去的楼 / 机甲落地的地方、双手砸地的震波、手拍、以及重型军车（坦克、装甲车、导弹车、机甲）碾过；
        //   · 撞开 RamCars：轻型军车（警车、维修车、巡逻艇）撞上停着的车，把它顺着路往前顶飞，翻滚半秒碎掉。
        // 两种都是把车从车流里摘出来（Traffic.TryTake），所以不会再和别的东西穿插。
        class FlatCar { public Transform t; public Vector3 s0, p0; public float age, minY; }
        readonly List<FlatCar> flats = new(); float flatSfx; Traffic[] traffics;
        Traffic[] Traffics => traffics ??= builder.root.GetComponentsInChildren<Traffic>(true);
        Transform CarObject(string name, Mesh mesh, Material mat, Matrix4x4 m)
        {
            var go = new GameObject(name); go.transform.SetParent(fxRoot, false);
            go.transform.SetPositionAndRotation(m.GetColumn(3), m.rotation); go.transform.localScale = m.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
        /// <summary>把 p 周围 r 内的车压扁（最多 max 辆）。loud = 弹拟声字、出声音（成片压扁时只响一次）</summary>
        public int FlattenCars(Vector3 p, float r, int max = 4, bool loud = true)
        {
            int n = 0;
            foreach (var tr in Traffics)
                while (n < max && tr && tr.TryTake(p, r, out var mesh, out var mat, out var m))
                {
                    var t = CarObject("FlatCar", mesh, mat, m); t.rotation *= Quaternion.Euler(Random.Range(-4f, 4f), Random.Range(-9f, 9f), Random.Range(-7f, 7f));
                    flats.Add(new FlatCar { t = t, s0 = t.localScale, p0 = t.position, minY = mesh.bounds.min.y });
                    AddDust(t.position + Vector3.up * 0.003f, 0.016f);
                    n++;
                }
            if (n > 0 && loud && Time.time > flatSfx) { flatSfx = Time.time + 0.3f; pow?.Show("smack", p + Vector3.up * 0.04f); MonsterAudio.Play("sfx_smash", p, 0.5f, 1.5f); }
            while (flats.Count > 80) { if (flats[0].t) Destroy(flats[0].t.gameObject); flats.RemoveAt(0); }
            return n;
        }
        /// <summary>沿一条线（倒下的楼）压扁</summary>
        public int FlattenCarsAlong(Vector3 a, Vector3 b, float r, Zone z)
        {
            int n = 0; float len = Vector3.Distance(a, b); int steps = Mathf.Max(1, Mathf.CeilToInt(len / Mathf.Max(0.01f, r)));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector3.Lerp(a, b, (float)i / steps);
                if (z != null && Support(z, p, z.origin.y) > z.origin.y + 0.004f) continue;   // 这一点上有楼接着：瓦砾落在楼顶，压不到街上的车
                n += FlattenCars(p, r, 3, false);
            }
            return n;
        }
        /// <summary>军车开到 p（车头朝 fwd）：heavy 碾扁，否则把车顺着路顶飞</summary>
        public void RamCars(Vector3 p, float r, Vector3 fwd, bool heavy)
        {
            if (heavy) { FlattenCars(p, r, 2); return; }
            foreach (var tr in Traffics)
                if (tr && tr.TryTake(p, r, out var mesh, out var mat, out var m))
                {
                    var t = CarObject("RammedCar", mesh, mat, m);
                    AddWreck(t, fwd * 0.16f + Vector3.up * 0.2f, 0.5f);
                    if (Time.time > flatSfx) { flatSfx = Time.time + 0.3f; pow?.Show("crash", p + Vector3.up * 0.04f); MonsterAudio.Play("sfx_clang", p, 0.5f, 1.3f); }
                    return;
                }
        }
        void UpdateFlats(float dt)
        {
            for (int i = flats.Count - 1; i >= 0; i--)
            {
                var f = flats[i]; if (!f.t) { flats.RemoveAt(i); continue; }
                if (f.age >= 0.1f) continue;
                // 11-17 用户：压扁的车不是直接压成一张纸，还是有体积的 —— 高度压到原来的四分之一（上一版一成；车顶塌到轮子的高度，约 3mm 厚），摊开一点、歪着（见 FlattenCars 里给的倾角）
                f.age += dt; float k = Mathf.Clamp01(f.age / 0.1f); k *= k; float sy = Mathf.Lerp(1, 0.24f, k);
                f.t.localScale = new Vector3(f.s0.x * (1 + 0.16f * k), f.s0.y * sy, f.s0.z * (1 + 0.08f * k));
                f.t.position = f.p0 + Vector3.up * (f.minY * f.s0.y * (1 - sy));   // 车底留在路面上（模型原点不一定在车底）
            }
        }

        /// <summary>手拍 / 压到的地方：半径 r 内的树被拍扁（碎叶 + 木屑），路上的车被压扁。返回拍掉几样</summary>
        int CrushProps(Vector3 p, float r)
        {
            int n = 0;
            for (int guard = 0; guard < 4; guard++)
            {
                var tree = builder.TakeTree(p, r); if (!tree) break;
                var bb = PropBounds(tree); Destroy(tree.gameObject); n++;
                for (int i = 0; i < 7; i++)
                    rubs.Add(new Rub
                    {
                        p = bb.center + Vector3.Scale(bb.extents, Random.insideUnitSphere), s = Vector3.one * Random.Range(0.003f, 0.006f), r = Random.rotation, chunk = true,
                        v = new Vector3(Random.Range(-0.12f, 0.12f), Random.Range(0.05f, 0.16f), Random.Range(-0.12f, 0.12f)), spin = Random.insideUnitSphere * 9,
                        z = NearestZone(bb.center), c = i < 5 ? new Color(0.3f, 0.62f, 0.32f) : new Color(0.45f, 0.3f, 0.2f), floor = FloorAt(NearestZone(bb.center), bb.center, NearestZone(bb.center)?.origin.y ?? 0), check = Random.value * 0.3f,
                    });
                AddDust(new Vector3(bb.center.x, bb.min.y + 0.002f, bb.center.z), 0.014f);
            }
            n += FlattenCars(p, r, r > 0.08f ? 8 : 1);   // 手拍一辆；双手砸地的震波范围大，一次压一片
            return n;
        }
        void UpdateProps(float dt)
        {
            foreach (var h in game.held.Values)
                if (h.kind == "prop" && h.prop != null && props.TryGetValue(h.prop, out var t) && t && h.hasPos)
                {
                    var b = PropBounds(t);
                    t.position += h.pos - b.center;   // 手捏住它的中间
                    t.rotation = Quaternion.Slerp(t.rotation, t.rotation * Quaternion.Euler(Mathf.Sin(Time.time * 9) * 4, 0, Mathf.Sin(Time.time * 7) * 5), 0.2f);
                }
            foreach (var f in game.flying)
                if (f.prop != null && props.TryGetValue(f.prop, out var t) && t)
                {
                    var b = PropBounds(t); t.position += f.pos - b.center;
                    t.rotation = Quaternion.Euler(f.spin * (dt * Mathf.Rad2Deg)) * t.rotation;
                }
            keyBuf.Clear(); foreach (var k0 in props.Keys) keyBuf.Add(k0);
            foreach (var key in keyBuf)
            {
                if (InPlay(key, true)) continue;
                var t = props[key]; props.Remove(key);
                if (t) AddWreck(t, new Vector3(Random.Range(-0.15f, 0.15f), 0.15f, Random.Range(-0.15f, 0.15f)));   // 落地：翻滚一下变残骸，再碎掉
            }
        }
        static Bounds PropBounds(Transform t)
        {
            Bounds b = new(t.position, Vector3.zero); bool f = true;
            foreach (var r in t.GetComponentsInChildren<Renderer>()) { if (f) { b = r.bounds; f = false; } else b.Encapsulate(r.bounds); }
            return b;
        }

        // 车砸在高楼中间（11-11）：楼不塌，只在撞击点崩出一把碎块（往撞来的反方向飞）+ 扬尘 + “咔！”
        /// <summary>子弹打在楼 / 桌沿上（11-17：弹药不再穿楼）：一小撮黄白火星朝来的方向溅回去 + 一点墙灰 + 很轻的一声“叮”</summary>
        void OnRicochet(Ev ev)
        {
            var p = ev.pos.Value; var back = ev.dir.HasValue ? -ev.dir.Value : Vector3.up;
            for (int i = 0; i < 5; i++)
                shards.Add(new Shard { p = p, v = (back + Random.insideUnitSphere * 0.8f).normalized * Random.Range(0.08f, 0.2f) + Vector3.up * 0.05f, r = Random.rotation, spin = Random.insideUnitSphere * 14, s = new Vector3(0.006f, 0.0013f, 0.0026f), life = Random.Range(0.22f, 0.4f), c = i % 2 == 0 ? Color.white : new Color(1f, 0.85f, 0.35f) });
            blobs.Add(p, 0.012f, new Color(1f, 0.92f, 0.6f, 0.9f), 1);
            MonsterAudio.Play("sfx_clang", p, 0.22f, 1.9f + Random.value * 0.4f);
        }

        void OnChip(Ev ev)
        {
            var b = B(game, ev.id); if (b == null || !ev.pos.HasValue) return;
            var p = ev.pos.Value; var back = ev.dir.HasValue ? -ev.dir.Value : Vector3.up;
            var tint = BuildingTint(b);
            int n = Mathf.Clamp(4 + Mathf.RoundToInt(ev.n) * 2, 5, 14);
            for (int i = 0; i < n; i++)
            {
                float sz = b.modH * Random.Range(0.25f, 0.6f);
                rubs.Add(new Rub
                {
                    p = p + Random.insideUnitSphere * b.modH * 0.4f, s = new Vector3(sz, sz * Random.Range(0.3f, 0.6f), sz * Random.Range(0.6f, 1f)), r = Random.rotation,
                    v = (back + Random.insideUnitSphere * 0.6f).normalized * Random.Range(0.08f, 0.22f) + Vector3.up * 0.06f,
                    spin = Random.insideUnitSphere * 9, z = b.zone, c = RubColor(tint), floor = b.zone.origin.y, check = Random.value * 0.3f,
                });
            }
            AddDust(p, 0.03f);
            pow?.Show("crash", p + Vector3.up * 0.03f);
        }

        void UpdateCarried(float dt)
        {
            UpdateProps(dt);
            foreach (var h in game.held.Values)
                if (h.kind == "chunk" && carried.TryGetValue(h.chunk, out var g) && h.hasPos)
                {
                    // 抓哪儿就提哪儿（原来是楼的正中间对到手上：抓楼脚的话整栋往下沉半截、插进桌子）；跟手略带一点缓冲，不抖
                    float gy = h.grabY >= 0 ? h.grabY : h.chunk.mods * h.chunk.modH * 0.5f;
                    var want = h.pos - Vector3.up * gy;
                    g.position = Vector3.Lerp(g.position, want, 1 - Mathf.Exp(-dt * 30));
                }
            foreach (var f in game.flying)
                if (f.kind == "chunk" && f.chunk != null && carried.TryGetValue(f.chunk, out var g)) { g.position = f.pos; g.rotation = Quaternion.Euler(f.spin * (dt * Mathf.Rad2Deg)) * g.rotation; }
            // 已经落地（不在手里也不在飞）的：散成碎片
            keyBuf.Clear(); foreach (var k0 in carried.Keys) keyBuf.Add(k0);
            foreach (var key in keyBuf)
            {
                var s = key as Slab;
                if (InPlay(key, false)) continue;
                var g = carried[key]; carried.Remove(key);
                var b = B(game, s.from);
                // 11-12 用户：扔出去的楼落地后还整块滚半天，很假 —— 落地（或撞上东西）那一刻整栋当场碎成瓦砾 + 尘 + “哗啦！”
                Bounds gb = PropBounds(g);
                foreach (Transform c in g.Cast<Transform>().ToList()) { c.SetParent(fxRoot, true); Shatter(c, b, new Vector3(Random.Range(-0.08f, 0.08f), 0, Random.Range(-0.08f, 0.08f)), 0, null, false); }
                AddDust(new Vector3(gb.center.x, gb.min.y + 0.004f, gb.center.z), 0.05f); pow?.Show("crash", gb.center + Vector3.up * 0.05f); shake = Mathf.Max(shake, 0.2f);
                { var zz = NearestZone(gb.center); if (zz != null && gb.min.y < zz.origin.y + 0.03f) FlattenCars(new Vector3(gb.center.x, zz.origin.y, gb.center.z), Mathf.Max(gb.extents.x, gb.extents.z), 6, false); }
                Destroy(g.gameObject);
            }
        }

        // ═══════════ 烟、尘、爆炸 ═══════════
        /// <summary>扬尘（11-11 第三版：霓虹动漫 + 美式卡通 —— 带黑描边的奶油 / 淡紫色卡通烟团，贴地一圈往外推，弹出来再缩没；
        /// 不透明、写深度（不发虚），个头小、贴地（不挡楼）），r = 推出去的半径</summary>
        static readonly Color DustC = new(0.96f, 0.9f, 0.8f), DustL = new(0.8f, 0.76f, 0.95f);
        public void AddDust(Vector3 p, float r)
        {
            int n = 9; float a0 = Random.value * Mathf.PI * 2;
            for (int k = 0; k < n; k++)
            {
                float a = a0 + k / (float)n * Mathf.PI * 2 + Random.Range(-0.25f, 0.25f);
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                puffs.Add(new Puff { pos = p + d * r * 0.2f + Vector3.up * r * 0.06f, vel = d * r * Random.Range(1.5f, 2.1f) + Vector3.up * r * 0.3f, size = r * Random.Range(0.17f, 0.25f), life = Random.Range(0.6f, 0.85f), col = (k % 3 == 0 ? DustL : DustC) * Random.Range(0.95f, 1.05f), cartoon = true });
            }
            for (int k = 0; k < 2; k++)
                puffs.Add(new Puff { pos = p + Random.insideUnitSphere * r * 0.15f, vel = Vector3.up * r * Random.Range(0.6f, 1f), size = r * 0.2f, life = 0.9f, col = DustC, cartoon = true });
        }

        public void AddBlast(Vector3 p, float r)
        {
            r = Mathf.Max(0.03f, r);
            var dirs = new Vector3[6];
            for (int k = 0; k < 6; k++) { float a = k / 6f * Mathf.PI * 2; dirs[k] = new Vector3(Mathf.Cos(a), Random.Range(0.6f, 1.2f), Mathf.Sin(a)); }
            bursts.Add(new Burst { pos = p, r = r, life = 0.9f, kind = "blast", dirs = dirs });
            if (r >= 0.05f) pow?.Show("boom", p + Vector3.up * (r + 0.02f));
            float d = Vector3.Distance(p, game.headPos);
            shake = Mathf.Max(shake, Mathf.Min(1, r * 4 / Mathf.Max(0.3f, d)));
        }

        void UpdatePuffs(float dt)
        {
            for (int i = puffs.Count - 1; i >= 0; i--)
            {
                var p = puffs[i]; p.age += dt;
                if (p.age >= p.life) { puffs.RemoveAt(i); continue; }
                p.pos += p.vel * dt; p.vel *= 1 - dt * 1.5f;
                float k = p.age / p.life;
                if (p.cartoon)
                {
                    // 卡通烟团：0.15 秒鼓起来，之后慢慢缩到没（不淡出 —— 卡通烟是“缩没”的）
                    float pop = k < 0.18f ? k / 0.18f : Mathf.Pow(1 - (k - 0.18f) / 0.82f, 0.7f);
                    toonPuffs ??= new FxBatch(FxBatch.Blob(), RubMat());
                    toonPuffs.Add(p.pos, Quaternion.Euler(i * 23, i * 37, 0), Vector3.one * p.size * pop * (1 + 0.7f * k), p.col);
                    continue;
                }
                float s = p.grow >= 0 ? p.size * (1 + p.age * p.grow) : p.size * (1 - k);
                var c = p.col; c.a = p.grow >= 0 ? 0.42f * (1 - k * k) : 1;
                blobs.Add(p.pos, Quaternion.Euler(0, i * 37, 0), Vector3.one * s, c, p.glow);
            }
            if (puffs.Count > 400) puffs.RemoveRange(0, puffs.Count - 400);
            toonPuffs?.Draw();
        }
        FxBatch toonPuffs;

        // 爆炸（11-11 卡通化）：白芯 → 黄色爆炸星（尖刺朝四周、正对玩家）+ 粉色冲击环 → 深紫卡通烟团（带黑描边、缩没）
        void UpdateBursts(float dt)
        {
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                var B = bursts[i]; float age0 = B.age; B.age += dt;
                float k = B.age / B.life;
                if (k >= 1) { bursts.RemoveAt(i); continue; }
                float e = 1 - Mathf.Pow(1 - Mathf.Min(1, k * 3f), 3);
                float fade = 1 - Mathf.Clamp01((k - 0.35f) / 0.4f);
                blobs.Add(B.pos, B.r * 0.75f * e * fade, Color.white, 1);
                var face = Quaternion.LookRotation((game.headPos - B.pos).normalized);
                for (int s2 = 0; s2 < 10; s2++)
                {
                    // 尖刺朝四周（原来多乘了一个绕 X 转 90°，十根尖刺全指向同一个方向）
                    var rad = face * Quaternion.Euler(0, 0, s2 * 36 + B.dirs[0].x * 40) * Vector3.up;
                    float len = B.r * (s2 % 2 == 0 ? 1.5f : 0.95f) * e * fade;
                    cones.Add(B.pos + rad * B.r * 0.35f * e, YTo(rad), new Vector3(B.r * 0.42f, len, B.r * 0.42f), s2 % 2 == 0 ? new Color(1f, 0.86f, 0.18f) : new Color(1f, 0.55f, 0.15f), 1);
                }
                if (k < 0.55f) rings.Add(B.pos, Quaternion.identity, Vector3.one * B.r * (0.6f + k * 2.6f), new Color(1f, 0.36f, 0.72f, 1 - k / 0.55f), 1);
                // 爆炸过了一小会儿：冒出几团深紫卡通烟
                if (age0 < B.life * 0.25f && B.age >= B.life * 0.25f)
                    foreach (var d in B.dirs)
                        puffs.Add(new Puff { pos = B.pos + d * B.r * 0.5f, vel = d * B.r * 0.8f + Vector3.up * B.r * 0.6f, size = B.r * 0.55f, life = 0.9f, col = new Color(0.32f, 0.24f, 0.4f), cartoon = true });
            }
        }
    }
}
