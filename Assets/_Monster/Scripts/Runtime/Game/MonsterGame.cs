// 玩法主循环（纯逻辑，不碰渲染）：每帧 game.Tick(dt, 输入)。和原型 src/logic/game.js 一致：
// 倒塌连锁、着火蔓延、护盾供电、通缉星、追兵、弹药、抓 / 扔 / 拔楼、原子吐息、眼睛读城。
// 表现层读状态（city / enemies / projectiles / flying / held）和 DrainEvents() 取出的一次性事件。
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Monster.Game
{
    public class Projectile { public string kind; public Vector3 pos, vel, target; public float dmg, age; public int from; public bool passed, warned, init; public string heldBy; public Building skip; }   // heldBy：被哪只手捏住（导弹 / 炮弹可以捏住扔回去）
    public class Flying { public string kind, etype; public Vector3 pos, vel, spin; public float age; public int refId = -1; public bool thrown; public Slab chunk; public object prop; }
    public class Slab { public int mods, from; public float modH, foot, fw, fd; public Style style; public int seed; }
    public class Held { public bool wrap; public float span0, grabY = -1; public string kind; public Enemy enemy; public Slab chunk; public string etype; public Vector3 pos; public bool hasPos; public Projectile proj; public object prop; }

    public class HandIn { public string side; public bool present, pinch, grip, open = true; public Vector3 pos, vel, palm, palmN, wrist, fwd; public Vector3[] pts; public int nPts; public float pinchD = 9; }   // pinchD：拇指尖到食指尖的距离（快捏上了 = 正在去抓，不当成拍）   // palmN：手心朝外的法线   // pts：接触用的点（掌心 + 五个指尖 + 食指中节）
    public class GameInput { public bool hasHead; public Vector3 headPos, headFwd; public bool hasGaze, eyeGaze = true; public Ray gaze; public List<HandIn> hands; }   // eyeGaze=false：眼动拿不到，gaze 是头部朝向

    public class MonsterGame
    {
        public readonly Level level; public readonly CityLayout city; public readonly Rng rng;
        public float hp = Cfg.HpMax, rage, breathing; readonly Dictionary<string, float> breathCd = new();
        public int stars; public float heat, idle, seal, sealMax;
        public readonly List<Enemy> enemies = new(); public readonly List<Projectile> projectiles = new(); public readonly List<Flying> flying = new();
        public readonly Dictionary<string, Held> held = new();
        public float spawnCd = 3; int mechN; float mechAt = -99, noRageUntil;
        public int chain, maxChain, destroyed, kills; public float chainT, time;
        public string result;   // null / win / seal / hp
        // 11-13 用户：胜利不能一下子就结束、要有过程；封印塔必须全没了才算赢（不能手里还拿着最后一座 / 还剩半座）。
        // 所有塔都倒了 → 最后一座的残骸不在手里、也不在空中飞 → 再等 WinSettle 秒（倒塌动画放完）→ winT 从 0 开始计：
        // 冲击波、广播“全员撤离”、弹药熄火、追兵一辆辆撤走（见 BeginVictory 和表现层的 seal_break）→ WinShow 秒后才真正结束、出结算
        public float winT = -1; float winSettle, pieceT; bool pieceTold, nukeOff; int lastTowerId = -1;
        public bool Winning => winT >= 0;
        readonly Dictionary<int, float> scanAcc = new();
        readonly List<(float t, Action fn)> pending = new();
        readonly List<Ev> events = new();
        readonly Dictionary<string, bool> prevPinch = new();
        public Vector3 headPos = new(0, 1.6f, 0.3f), headFwd = Vector3.forward;
        public Ray? gaze; public Vector3? breathEnd;
        // 能量波（11-10 用户：原来的吐息从眼前喷出去会晕 → 双手张开、掌根并拢往前推，光束从两手之间发出、跟着手走）
        public float charge; public bool waveWant; public Ray? beam; public float nukeT = -1; bool rageTold; public Vector3 waveMid, waveDir; float beamLost; bool waveAssist; Vector3 waveHd = Vector3.forward; Quaternion waveRot = Quaternion.identity;
        public readonly bool rain;
        public Tutorial tut;
        public List<HandIn> lastHands;   // 这一帧的手（玩法模式的软提示要看手靠近了什么）
        public readonly int totalMods;
        static readonly Vector3 G = new(0, Cfg.Gravity, 0);

        public MonsterGame(Level level, CityLayout city, int seed = 7)
        {
            this.level = level; this.city = city;
            rng = new Rng((uint)(seed * 31 + 5));
            stars = level.stars; heat = Cfg.HeatStars[stars];
            sealMax = level.seal; rain = level.time == "rain";
            foreach (var t in city.towers) { t.shielded = true; t.scanUntil = -1; }
            foreach (var s in city.stations) { s.online = !s.backup; s.armor = Mathf.Max(1, level.armor); s.restart = -1; }   // 备用电站开局不供电
            totalMods = city.buildings.Sum(b => b.maxH);
            if (level.tutorial) { tut = new Tutorial(this); tut.Begin(); }
        }

        public void Emit(Ev e) { events.Add(e); tut?.OnEvent(e); }
        public void Later(float t, Action fn) => pending.Add((t, fn));
        public List<Ev> DrainEvents() { var e = new List<Ev>(events); events.Clear(); return e; }

        public void Tick(float dt, GameInput input)
        {
            if (result != null) return;
            dt = Mathf.Min(dt, 0.05f);
            time += dt;
            if (input != null && input.hasHead) { headPos = input.headPos; headFwd = input.headFwd; }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i]; p.t -= dt; pending[i] = p;
                if (p.t <= 0) { pending.RemoveAt(i); p.fn(); }
            }
            // 11-12 用户：城市上空那圈封印光环看不懂 —— 倒计时改成“核打击”：到点后集团发射一枚核弹，落地一击致命。
            // 倒计时走完 → nukeT 从 0 开始计（表现层画核弹从天上落下来 + 警报）→ Cfg.NukeFall 秒后落地，游戏结束
            bool towersDown = city.towers.Count > 0; foreach (var tw0 in city.towers) if (tw0.h > 0) { towersDown = false; break; }
            if (tut == null && !towersDown)
            {
                seal += dt;
                if (seal >= sealMax && nukeT < 0) { nukeT = 0; Emit(new Ev { type = "nuke_launch" }); }
                if (nukeT >= 0) { nukeT += dt; if (nukeT >= Cfg.NukeFall) { hp = 0; Emit(new Ev { type = "nuke_hit" }); End("seal"); return; } }
            }
            // 塔全倒了：倒计时停住；核弹已经在路上的话当场失去引导（表现层：熄火、在空中解体）
            if (towersDown && nukeT >= 0 && !nukeOff) { nukeOff = true; nukeT = -1; Emit(new Ev { type = "nuke_abort" }); }
            if (chainT > 0) { chainT -= dt; if (chainT <= 0) chain = 0; }
            // 通缉：躲风头（8 秒不拆降一星，更高星级的追兵撤退）
            idle += dt;
            if (tut == null && idle >= Cfg.WantedDecayT && stars > level.stars)
            {
                stars--; heat = Cfg.HeatStars[stars]; idle = 0;
                Emit(new Ev { type = "star_down", stars = stars });
                foreach (var e in enemies)
                    if (!e.dead && Enemies.Types[e.type].stars > stars && Enemies.Types[e.type].stars < 6 && e.type != "mech" && e.state == EState.Move)
                    { e.dead = true; e.state = EState.Dead; Emit(new Ev { type = "leave", id = e.id }); }
            }
            Perf.Sample("电"); TickPower(dt); Perf.SampleEnd();
            Perf.Sample("火"); TickFire(dt); Perf.SampleEnd();
            TickSpawn(dt);
            Perf.Sample("追兵"); for (int ei = 0, en = enemies.Count; ei < en && ei < enemies.Count; ei++) { var e = enemies[ei]; if (!e.dead) Enemies.Tick(this, e, dt); } Perf.SampleEnd();
            Perf.Sample("弹"); TickProjectiles(dt); Perf.SampleEnd();
            Perf.Sample("飞"); TickFlying(dt); Perf.SampleEnd();
            lastHands = input?.hands;
            Perf.Sample("手"); if (input?.hands != null) TickHands(dt, input.hands, input.hasGaze ? input.gaze : (Ray?)null); Perf.SampleEnd();
            Perf.Sample("眼"); if (input != null && input.hasGaze) { TickScan(dt, input.gaze, input.eyeGaze); gaze = input.gaze; } Perf.SampleEnd();
            if (breathing > 0) TickBreath(dt, beam ?? gaze);
            foreach (var b in city.buildings) if (b.hitCd > 0) b.hitCd -= dt;
            enemies.RemoveAll(e => e.dead && e.state != EState.Held);
            tut?.Update(dt);
            if (winT >= 0) { winT += dt; if (winT >= Cfg.WinShow) End("win"); }
            else if (towersDown && (tut == null || tut.allowWin))
            {
                bool Piece(Slab c) => c != null && city.byId.TryGetValue(c.from, out var fb) && fb.kind == BuildingKind.Tower;
                bool piece = held.Values.Any(h => h.kind == "chunk" && Piece(h.chunk)) || flying.Any(f => f.kind == "chunk" && Piece(f.chunk));
                if (piece)
                {
                    // 最后一座塔还攥在手里 / 还在空中：不算。拿着超过 1.2 秒提醒一次“扔掉它”
                    winSettle = 0; pieceT += dt;
                    if (pieceT > 1.2f && !pieceTold && held.Values.Any(h => h.kind == "chunk" && Piece(h.chunk))) { pieceTold = true; Emit(new Ev { type = "tower_piece" }); }
                }
                else { winSettle += dt; if (winSettle >= Cfg.WinSettle) BeginVictory(); }
            }
            else winSettle = 0;
        }

        /// <summary>胜利过程开始（封印解除）：之后玩家不再受伤、不再出兵；弹药熄火，追兵依次撤走；Cfg.WinShow 秒后 End("win")</summary>
        void BeginVictory()
        {
            winT = 0; maxChain = Mathf.Max(maxChain, chain);
            var last = city.byId.TryGetValue(lastTowerId, out var lt) ? lt : city.towers[0];
            Emit(new Ev { type = "seal_break", id = last.id, pos = last.Base });
            foreach (var p in projectiles) Emit(new Ev { type = "fizzle", pos = p.pos });
            projectiles.Clear();
            int i = 0;
            foreach (var e in enemies)
            {
                if (e.dead || e.state != EState.Move) continue;
                var ee = e;
                Later(0.9f + 0.28f * i++, () => { if (!ee.dead && ee.state == EState.Move) { ee.dead = true; ee.state = EState.Dead; Emit(new Ev { type = "leave", id = ee.id, cause = "win" }); } });
            }
        }

        // ═══════════ 飞行单位的净空（11-13 用户：旁边有柜子时飞机不要穿进柜子）═══════════
        List<(Zone z, float top, float m)> obstacles;
        List<(Zone z, float top, float m)> Obstacles()
        {
            if (obstacles == null)
            {
                obstacles = new();
                foreach (var z in city.zones)
                {
                    float h = 0; foreach (var b in city.buildings) if (b.zone == z) h = Mathf.Max(h, b.TopH(b.maxH));
                    obstacles.Add((z, z.origin.y + h, z.furniture ? 0.09f : 0.045f));   // 虚拟家具比城区每边多出 8cm 的底座和木框
                }
            }
            return obstacles;
        }
        /// <summary>
        /// 飞行单位在 p 处不能低于的高度（世界 y）；附近没有城区 / 家具时是一个很低的数。r = 机身半径；skip = 不算的城区（无人机从这片城区的街上起飞）。
        /// 每片城区（连同下面的桌子 / 柜子）当成一个从地面到“最高楼顶”的方块：正上方要高过 楼顶 + 机身 + 3cm；离开边缘后要求的高度按每 1cm 降 2.5cm 往下放
        /// </summary>
        public float Clearance(Vector3 p, float r, Zone skip = null)
        {
            float need = -99;
            foreach (var (z, top, m) in Obstacles())
            {
                if (z == skip) continue;
                var d = p - z.origin; float a = Vector3.Dot(d, z.u), b = Vector3.Dot(d, z.v);
                float da = Mathf.Max(0, Mathf.Max(-a - m, a - z.w - m)), db = Mathf.Max(0, Mathf.Max(-b - m, b - z.h - m));
                need = Mathf.Max(need, top + r + 0.03f - Mathf.Sqrt(da * da + db * db) * 2.5f);
            }
            return need;
        }

        public void End(string r)
        {
            if (result != null) return;
            result = r; maxChain = Mathf.Max(maxChain, chain);
            Emit(new Ev { type = r == "win" ? "win" : "lose", cause = r });
        }

        // ═══════════ 建筑 ═══════════
        public static float HeightOf(Building b) => b.Height;   // 真实楼高（按模块累加，见 Building.tops）
        public static Vector3 TopOf(Building b) => b.Base + Vector3.up * HeightOf(b);
        public float S = 1;   // 城市比例（CityBuilder.scale）
        readonly List<Building> nbBuf = new();
        /// <summary>b 周围 range 内还立着的楼。返回的是一个复用的缓冲区：用完再调下一次（不能嵌套着用、不能存起来）</summary>
        public List<Building> Neighbors(Building b, float range = -1)
        {
            if (range < 0) range = Cfg.FireRange * S;
            nbBuf.Clear();
            foreach (var o in city.buildings) if (o != b && o.zone == b.zone && o.h > 0 && Vector3.Distance(o.Base, b.Base) < range) nbBuf.Add(o);
            return nbBuf;
        }

        public bool IsShielded(Building t)
        {
            if (t.kind != BuildingKind.Tower || t.shieldBroken || t.h <= 0) return false;
            foreach (var id in t.powerIds) if (city.byId.TryGetValue(id, out var ps) && ps.online) return true;
            return t.backupId >= 0 && city.byId.TryGetValue(t.backupId, out var bk) && bk.online;
        }

        /// <summary>从第 k 层往上整段倒下（dir 为平面内方向；null = 原地砸碎）</summary>
        public bool ToppleAt(Building b, int k, Vector3? dir, string cause = "smash")
        {
            if (b == null || b.h <= 0) return false;
            if (b.kind == BuildingKind.Tower && IsShielded(b)) { Emit(new Ev { type = "shield_hit", id = b.id, cause = cause }); return false; }
            // 电站护甲（见 Level.armor）：还有不止 1 格时，这一下只打掉护甲，电站不倒
            if (b.kind == BuildingKind.Station && b.armor > 1)
            {
                int hit = cause is "smash" or "fire" or "uproot" or "blast" or "breath" or "quake" ? 1 : cause == "thrown" ? 2 : 99;   // 楼倒下来压到（chain）、机甲撞：直接毁；能量波每 0.35 秒打掉一格（11-14：原来一照就毁）
                if (hit < b.armor)
                {
                    b.armor -= hit; b.hitCd = 0.45f; idle = 0; AddHeat(Cfg.HeatMod);
                    Emit(new Ev { type = "armor_hit", id = b.id, n = b.armor, pos = TopOf(b), cause = cause });
                    return false;
                }
            }
            if (b.kind == BuildingKind.Station) b.armor = 0;
            k = Mathf.Clamp(k, 0, b.h - 1);
            int from = b.h, removed = from - k;
            b.h = k;
            if (b.h == 0) b.fire = 0;
            destroyed += removed;
            if (cause != "shot") { chain += 1; chainT = 1.2f; maxChain = Mathf.Max(maxChain, chain); Gain("mod", removed); }   // 军队自己的炮火打塌的楼（shot）：不算玩家的连击，不涨怒气和通缉
            Emit(new Ev { type = "collapse", id = b.id, from = from, to = k, dir = dir, cause = cause, chain = chain });
            if (b.kind == BuildingKind.Station && b.h == 0) StationDown(b);
            if (b.h == 0) MaybeOrb(b, cause);
            if (b.kind == BuildingKind.Tower && b.h == 0)
            {
                seal = Mathf.Max(0, seal - sealMax * Cfg.SealRebate); lastTowerId = b.id;
                Emit(new Ev { type = "tower_down", id = b.id });
                Heal(Cfg.HealTower);
                int left = city.towers.Count(t => t.h > 0);
                if (tut == null && left > 0 && stars < level.maxStars)
                {
                    AddHeat(Mathf.Max(0, Cfg.HeatStars[stars + 1] - heat));
                    spawnCd = 0;
                    Emit(new Ev { type = "counter", stars = stars });
                }
                Blast(b.Base, 0.12f, 0, "tower");
            }
            // 倒下的那一截砸到顺着方向的邻楼 → 连锁
            if (dir.HasValue && removed > 0)
            {
                var d = dir.Value;
                float L = b.TopH(from) - b.TopH(k);
                foreach (var n in Neighbors(b, L + b.Foot + 0.06f).ToList())
                {
                    var rel = n.Base - b.Base;
                    float along = Vector3.Dot(rel, d);
                    float perp = (rel - d * along).magnitude;
                    if (along <= 0.01f || along > L + n.Foot / 2 + b.Foot / 2 || perp > (n.Foot + b.Foot) / 2) continue;
                    float hitC = b.TopH(k) + Mathf.Sqrt(Mathf.Max(0, L * L - along * along)) * 0.6f;
                    int k2 = Mathf.Max(0, Mathf.Min(n.h - 1, Mathf.Min(n.StoreyAt(hitC), n.h - Mathf.CeilToInt(removed / 3f))));
                    var nn = n;
                    Later(Cfg.ChainDelay * (1 + along / 0.1f), () => ToppleAt(nn, k2, d, "chain"));
                }
                // 11-17 用户：一切交互都要符合常理 —— 楼倒下来以前只会砸到别的楼，倒在路上的军车从瓦砾里照常开出来。
                // 现在倒下去的那一条（宽 = 楼宽、长 = 倒下那截的高度）上的地面载具被砸毁（机甲挨一下、出破绽）；等楼真倒到那儿才算（那时车已经开走的不算）
                if (removed >= 2)
                    for (int ei = 0; ei < enemies.Count; ei++)
                    {
                        var e = enemies[ei];
                        if (e.dead || e.state != EState.Move || Enemies.Types[e.type].fly || e.zone != b.zone) continue;
                        bool Under(Enemy x) { var rel = x.pos - b.Base; rel.y = 0; float al = Vector3.Dot(rel, d); return al > b.Foot * 0.4f && al < L + Enemies.R(x) && (rel - d * al).magnitude < b.Foot * 0.5f + Enemies.R(x); }
                        if (!Under(e)) continue;
                        var ee = e; float al0 = Vector3.Dot(e.pos - b.Base, d);
                        Later(Cfg.ChainDelay * (1 + al0 / 0.1f) + 0.25f, () =>
                        {
                            if (ee.dead || ee.state != EState.Move || !Under(ee)) return;
                            if (ee.type != "mech") Enemies.Kill(this, ee, "crush");
                            else if (ee.dropT <= 0 && !ee.air && ee.hop <= 0) { ee.guard = 0; ee.stagger = Mathf.Max(ee.stagger, 2.5f); HitEnemy(ee, d * 0.3f, "thrown"); }
                        });
                    }
            }
            return true;
        }

        /// <summary>手（或别的东西）以速度 vel 撞到建筑的 p 点</summary>
        public bool SmashBuilding(Building b, Vector3 p, Vector3 vel)
        {
            if (b == null || b.h <= 0 || b.hitCd > 0) return false;
            b.hitCd = 0.12f;
            float c = p.y - b.Base.y;
            int k = b.StoreyAt(Mathf.Max(0, c));
            float speed = vel.magnitude, down = -vel.y;
            // 竖直往下拍：砸碎手所在那层及以上；横着打：上半截朝手的方向倒下
            if (down > speed * 0.7f) return ToppleAt(b, Mathf.Max(0, Mathf.Min(k, b.h - 1 - Mathf.FloorToInt(Mathf.Max(0, speed - 0.5f) * 2.2f))), null, "smash");   // 越用力拍掉越多层
            var flat = new Vector3(vel.x, 0, vel.z);
            return ToppleAt(b, k, flat.magnitude > 1e-4f ? flat.normalized : (Vector3?)null, "smash");
        }

        /// <summary>扔出去的东西撞楼（11-11 用户：扔一辆警车，那么大的楼直接塌了，很假）：按撞击能量算伤害层数（车 ≈ 1~3 层，整栋楼块按层数算重量）。
        /// 撞在楼顶附近（伤害够得到楼顶）：从撞击处往上塌；撞在高楼中间：只崩出碎块（不改楼高），楼累计伤害，
        /// 累计到楼高的 45% 才从撞击处往上整截倒（像结构被打断）；矮楼（≤3 层）照常被砸塌</summary>
        void ImpactBuilding(Building b, Flying f)
        {
            if (b.kind == BuildingKind.Tower && IsShielded(b)) { Emit(new Ev { type = "shield_hit", id = b.id, pos = f.pos }); return; }
            float mass = f.kind == "chunk" && f.chunk != null ? Mathf.Max(1, f.chunk.mods) * 0.8f : f.kind == "shell" ? 0.6f : 1f;
            float v = f.vel.magnitude, energy = mass * v * v;
            int dmg = Mathf.Clamp(Mathf.RoundToInt(energy * 0.45f), 1, 14);
            int k = b.StoreyAt(Mathf.Max(0, f.pos.y - b.Base.y));
            var flat = new Vector3(f.vel.x, 0, f.vel.z); Vector3? dir = flat.sqrMagnitude > 1e-6f ? flat.normalized : null;
            b.dmg += dmg;
            if (b.h <= 3 || k >= b.h - dmg || b.dmg >= Mathf.Max(3, b.maxH * 0.45f))
            {
                b.dmg = 0;
                ToppleAt(b, Mathf.Clamp(k, 0, b.h - 1), b.h - k >= 2 ? dir : null, "thrown");
            }
            else
            {
                Gain("mod", 1);
                Emit(new Ev { type = "chip", id = b.id, pos = f.pos, dir = dir, n = dmg });
            }
        }

        public void Ignite(Building b)
        {
            if (b == null || b.h <= 0 || b.fire > 0 || (b.kind == BuildingKind.Tower && IsShielded(b))) return;
            if (b.kind == BuildingKind.Station) return;   // 电站不着火（不然电站一炸点着邻楼，重启后又被邻楼的火烧掉，玩家看不懂它是怎么没的）
            b.fire = 0.001f; b.burn = 0;
            Emit(new Ev { type = "ignite", id = b.id });
        }

        /// <summary>爆炸：点燃、震塌顶层、波及载具和玩家</summary>
        public void Blast(Vector3 p, float r, float dmg, string cause, int spare = -1)
        {
            float rp = r; r *= S;   // 波及楼和地面单位的半径跟城市比例走；伤到玩家的半径不变
            Emit(new Ev { type = "blast", pos = p, r = r, cause = cause });
            foreach (var b in city.buildings)
            {
                if (b.h <= 0) continue;
                float d = Vector3.Distance(p, b.Base) - b.Foot / 2, dTop = Vector3.Distance(p, TopOf(b));
                if (Mathf.Min(d, dTop) > r) continue;
                if (rng.Chance(0.75f)) Ignite(b);
                if (dTop < r * 0.7f && b.kind != BuildingKind.Tower) ToppleAt(b, b.h - 1, null, "blast");
            }
            for (int ei = 0, en = enemies.Count; ei < en && ei < enemies.Count; ei++) if (enemies[ei] is Enemy e)
            {
                if (e.dead || e.state != EState.Move || e.type == "mech" || e.id == spare) continue;
                if (Vector3.Distance(e.pos, p) < r + Enemies.R(e)) Enemies.Kill(this, e, "blast");
            }
            if (dmg > 0 && Vector3.Distance(p, headPos) < rp + Cfg.HeadR) Hurt(dmg, p, cause);
        }

        void StationDown(Building s)
        {
            // 自动重启：它的塔还立着的话，restart 秒后原地重建
            s.restart = level.restart > 0 && city.byId.TryGetValue(s.towerId, out var tw0) && tw0.h > 0 ? level.restart : -1;
            if (!s.online && s.backup) { Emit(new Ev { type = "station_off", id = s.id }); return; }
            s.online = false; s.repair = 0;
            Emit(new Ev { type = "station_off", id = s.id, tower = s.towerId });
            Blast(s.Base, 0.08f, 0, "station");
            if (level.repair && !s.backup) Later(2.5f, () => { if (!s.online && result == null) Enemies.Spawn(this, "repair", s.zone, s.id); });
        }

        /// <summary>教学兜底：把一栋拆掉的楼重新长出来（玩家提前把这一步要用的楼拆光了，不能卡关）</summary>
        public void Regrow(Building b)
        {
            b.h = b.maxH; b.fire = 0; b.burn = 0; b.hitCd = 0.6f;
            Emit(new Ev { type = "regrow", id = b.id, pos = b.Base });
        }

        public void RepairStation(Building s, Enemy e)
        {
            s.h = 1; s.online = true; s.repair = 0; s.fire = 0; s.armor = Mathf.Max(1, level.armor); s.restart = -1;
            e.dead = true; e.state = EState.Dead;
            Emit(new Ev { type = "station_on", id = s.id, tower = s.towerId, cause = "repair", car = e.id });
        }

        void TickPower(float dt)
        {
            // 电站自动重启
            foreach (var s in city.stations)
            {
                if (s.h > 0 || s.restart < 0) continue;
                if (!city.byId.TryGetValue(s.towerId, out var tw) || tw.h <= 0) { s.restart = -1; continue; }   // 塔没了：不用再供电
                if (s.restart > 2.5f && s.restart - dt <= 2.5f) Emit(new Ev { type = "station_prep", id = s.id, pos = s.Base });   // 还剩 2.5 秒：先把废墟清掉、搭脚手架
                s.restart -= dt;
                if (s.restart > 0) continue;
                s.restart = -1; s.h = 1; s.online = !s.backup; s.repair = 0; s.fire = 0; s.burn = 0; s.armor = Mathf.Max(1, level.armor); s.hitCd = 0.8f;
                Emit(new Ev { type = "station_on", id = s.id, tower = s.towerId, cause = "restart", pos = s.Base });
            }
            foreach (var t in city.towers)
            {
                bool was = t.shielded;
                t.shielded = IsShielded(t);
                if (was && !t.shielded && t.h > 0) Emit(new Ev { type = "shield_down", id = t.id });
                if (!was && t.shielded) Emit(new Ev { type = "shield_up", id = t.id });
                if (t.backupId < 0 || !city.byId.TryGetValue(t.backupId, out var bk) || bk.h <= 0 || bk.online || t.h <= 0) continue;
                bool mainsDown = true; foreach (var id in t.powerIds) if (city.byId[id].online) { mainsDown = false; break; }
                bk.countdown = mainsDown ? bk.countdown + dt : 0;
                if (bk.countdown >= Cfg.BackupDelay) { bk.online = true; Emit(new Ev { type = "station_on", id = bk.id, tower = t.id, cause = "backup" }); }
            }
        }

        void TickFire(float dt)
        {
            float slow = rain ? 0.6f : 1;
            foreach (var b in city.buildings)
            {
                if (b.h <= 0 || b.fire <= 0) continue;
                b.fire += dt; b.burn += dt * slow;
                if (b.burn >= Cfg.FireWeakenT) { b.burn = 0; ToppleAt(b, b.h - 1, null, "fire"); }
                if (b.fire > Cfg.FireSpreadT)
                    { var nb = Neighbors(b); for (int ni = 0; ni < nb.Count; ni++) if (nb[ni].fire <= 0 && rng.Chance(Cfg.FireSpreadRate * slow * dt)) Ignite(nb[ni]); }
            }
        }

        // ═══════════ 收益 / 通缉 / 受伤 ═══════════
        public void Gain(string kind, int n = 1)
        {
            // 11-14 用户：大招释放期间不涨怒气（原来光束扫倒的楼照样涨怒气，一发放完马上又满，可以连着放）
            // （放完以后再留 1.5 秒：被光束轰倒的楼还在倒、还在砸后面的楼，这些连锁也不算）
            float mult = breathing > 0 || time < noRageUntil ? 0 : chain >= 3 ? Cfg.RageChainMult : 1;
            if (kind == "mod") { rage += Cfg.RagePerMod * n * mult; AddHeat(Cfg.HeatMod * n); }
            else { rage += Cfg.RagePerVehicle * mult; AddHeat(Cfg.HeatVehicle); kills++; Heal(Cfg.HealVehicle); }
            rage = Mathf.Min(Cfg.RageMax, rage);
            if (rage >= Cfg.RageMax && !rageTold) { rageTold = true; Emit(new Ev { type = "rage_full" }); } else if (rage < Cfg.RageMax) rageTold = false;
            idle = 0;
        }

        public void Heal(float n)
        {
            if (result != null || hp >= Cfg.HpMax) return;
            hp = Mathf.Min(Cfg.HpMax, hp + n);
            Emit(new Ev { type = "heal", n = n });
        }

        public void AddHeat(float h)
        {
            if (winT >= 0) return;   // 胜利过程中不再涨通缉
            heat += h;
            int s = stars, cap = level.maxStars;
            while (s < cap && heat >= Cfg.HeatStars[s + 1]) s++;
            if (s > stars) { stars = s; Emit(new Ev { type = "star_up", stars = s }); spawnCd = Mathf.Min(spawnCd, 0.8f); }
        }

        public void Hurt(float dmg, Vector3? from, string cause)
        {
            if (result != null || winT >= 0) return;
            // 教学关：伤害打四折，体力不会低于 30（能感觉到疼，但不会输）
            if (tut != null) { dmg *= 0.4f; hp = Mathf.Max(30, hp - dmg); Emit(new Ev { type = "hurt", dmg = dmg, pos = from, cause = cause }); return; }
            dmg *= level.dmg;
            hp = Mathf.Max(0, hp - dmg);
            Emit(new Ev { type = "hurt", dmg = dmg, pos = from, cause = cause });
            if (hp <= 0) End("hp");
        }

        public bool HitsPlayer(Vector3 p, float extra = 0)
        {
            if (Vector3.Distance(p, headPos) < Cfg.HeadR + extra) return true;
            var top = headPos - new Vector3(0, 0.2f, 0); var bot = headPos - new Vector3(0, 0.2f + Cfg.TorsoLen, 0);
            return DistToSeg(p, top, bot) < Cfg.TorsoR + extra;
        }

        // ═══════════ 追兵 ═══════════
        void TickSpawn(float dt)
        {
            if (mechN < level.mechs && stars >= Mathf.Min(5, level.mechStars + mechN) && time - mechAt > 12 && winT < 0) { mechN++; mechAt = time; Enemies.Spawn(this, "mech"); }
            if (tut != null || winT >= 0) return;
            spawnCd -= dt;
            if (spawnCd > 0 || stars <= 0) return;
            spawnCd = Enemies.SpawnInterval(stars);
            int alive = enemies.Count(e => !e.dead && e.type != "repair" && e.type != "drone" && e.type != "mech");
            if (alive >= Enemies.MaxAlive(stars)) return;
            var type = Weighted(Enemies.PoolFor(stars));
            if (type == "sam" && enemies.Count(e => !e.dead && e.type == "sam") >= 2) type = "tank";
            if (type == "boat" && !city.zones.Any(z => z.harbor)) type = "police";
            Enemies.Spawn(this, type);
        }

        string Weighted((string, float)[] opts)
        {
            float sum = opts.Sum(o => o.Item2), r = rng.Next() * sum;
            foreach (var (k, w) in opts) { r -= w; if (r <= 0) return k; }
            return opts[^1].Item1;
        }

        /// <summary>机甲被击毁：先失控、跪下（表现层演 MechDie 秒），大爆炸放在最后</summary>
        public void KillMech(Enemy e, string cause)
        {
            var at = e.pos; Enemies.Kill(this, e, cause);
            Later(Enemies.MechDie, () => Blast(at + Vector3.up * 0.04f, 0.14f, 0, "mech"));
        }
        /// <summary>拍 / 撞追兵；返回是否打中</summary>
        public bool HitEnemy(Enemy e, Vector3 vel, string cause)
        {
            if (e == null || e.dead || e.state != EState.Move) return false;
            var T = Enemies.Types[e.type];
            if (e.type == "mech")
            {
                if (e.dropT > 0 || e.air || e.hop > 0) return false;
                idle = 0;
                var flat = new Vector3(vel.x, 0, vel.z);
                if (e.guard > 0 && cause != "thrown") { e.knock = flat * 0.6f; Emit(new Ev { type = "mech_block", id = e.id }); return false; }
                e.hp -= cause == "thrown" ? 1 : 2;
                if (cause == "thrown") { e.stagger = Mathf.Max(e.stagger, 2.5f); Emit(new Ev { type = "mech_stagger", id = e.id }); }
                Emit(new Ev { type = "mech_hurt", id = e.id, hp = e.hp });
                if (e.hp <= 0) KillMech(e, cause);
                return true;
            }
            e.hp -= 1;
            if (e.hp > 0) { Emit(new Ev { type = "enemy_hurt", id = e.id }); return true; }
            if (T.fly && cause == "swat")
            {
                // 被拍飞的飞机会砸进城里
                Enemies.Kill(this, e, cause);
                flying.Add(new Flying { kind = "wreck", etype = e.type, pos = e.pos, vel = vel * 0.8f, spin = new Vector3(rng.Range(-9, 9), rng.Range(-9, 9), rng.Range(-9, 9)), refId = e.id });
                return true;
            }
            Enemies.Kill(this, e, cause);
            return true;
        }

        // ═══════════ 抓 / 扔 / 拔楼 ═══════════
        public bool GrabEnemy(string holder, Enemy e)
        {
            if (held.ContainsKey(holder) || e.dead || e.state != EState.Move) return false;
            if (e.type == "mech") { if (e.dropT > 0 || e.landT < Enemies.MechRise || e.air || e.hop > 0) return false; e.heldT = 0; e.charging = false; e.hopPending = false; }   // 机甲：落地站稳以后才抓得住（见 Enemies.TickMechHeld）
            e.state = EState.Held;
            held[holder] = new Held { kind = "enemy", enemy = e, etype = e.type };
            Emit(new Ev { type = "grab", id = e.id });
            return true;
        }

        public bool Uproot(string holder, Building b)
        {
            if (held.ContainsKey(holder)) return false;
            if (b.h <= 0 || (b.kind == BuildingKind.Tower && IsShielded(b))) { if (b.kind == BuildingKind.Tower) Emit(new Ev { type = "shield_hit", id = b.id }); return false; }
            var chunk = new Slab { mods = b.h, modH = b.h > 0 ? b.TopH(b.h) / b.h : b.modH, foot = b.Foot, fw = b.fw, fd = b.fd, style = b.style, seed = b.seed, from = b.id };
            ToppleAt(b, 0, null, "uproot");
            held[holder] = new Held { kind = "chunk", chunk = chunk };
            Emit(new Ev { type = "uproot", id = b.id, mods = chunk.mods });
            return true;
        }

        // ═══════════ 回血球（11-12 用户：要有回血的办法 —— 拆楼会掉出生命球，抓一下回血）═══════════
        // 普通楼被彻底拆平时有机会掉一个（血越少越容易掉；血低于四成、场上没有球时必掉一个），在废墟上方浮 10 秒；
        // 手捏 / 握住它，或者手掌直接碰到，就回 15 点血。场上最多 2 个，两次掉落至少隔 5 秒
        public class Orb { public int id; public Vector3 pos; public float age, life = 10, heal = 15; }
        public readonly List<Orb> orbs = new(); float orbCd; int orbSeq; bool tutOrb;
        void MaybeOrb(Building b, string cause)
        {
            // 封印塔倒下本身就回血，不掉球；电站被毁也会掉（11-12 模拟：只靠拆普通楼回血的话，越拆通缉越高、追兵越多，反而更糟）
            if (b.kind == BuildingKind.Tower || cause == "uproot") return;
            MaybeOrbAt(b.Base + Vector3.up * 0.075f, 0.1f);
        }
        /// <summary>在 pos 掉一个回血球：平时按概率（baseP + 0.4 × 缺血比例）；血低于四成、场上没有球时必掉</summary>
        public void MaybeOrbAt(Vector3 pos, float baseP)
        {
            if (result != null || orbs.Count >= 2 || orbCd > time) return;
            float k = 1 - hp / Cfg.HpMax;
            bool must = hp < Cfg.HpMax * 0.4f && orbs.Count == 0;
            if (tut != null && !tutOrb && hp < Cfg.HpMax - 4) { must = true; tutOrb = true; }   // 玩法关：第一次掉血以后，下一栋拆平的楼必掉一个（教回血）
            if (!must && rng.Next() > baseP + 0.4f * k) return;
            orbCd = time + 5;
            var o = new Orb { id = ++orbSeq, pos = pos };
            orbs.Add(o); Emit(new Ev { type = "orb", id = o.id, pos = o.pos });
        }
        void TickOrbs(float dt, List<HandIn> live)
        {
            for (int i = orbs.Count - 1; i >= 0; i--)
            {
                var o = orbs[i]; o.age += dt;
                if (o.age > o.life) { orbs.RemoveAt(i); Emit(new Ev { type = "orb_gone", id = o.id, pos = o.pos }); continue; }
                if (o.age < 0.35f) continue;   // 刚冒出来：先让玩家看见
                foreach (var h in live)
                {
                    bool closed = h.pinch || h.grip;
                    if (!(closed && Vector3.Distance(h.pos, o.pos) < 0.075f) && Vector3.Distance(h.palm, o.pos) > 0.045f && !NearHand(h, o.pos, 0.03f)) continue;
                    float before = hp; Heal(o.heal);
                    orbs.RemoveAt(i); Emit(new Ev { type = "orb_take", id = o.id, pos = o.pos, n = hp - before });
                    break;
                }
            }
        }

        readonly Dictionary<string, float> pinchStart = new();
        /// <summary>捏住飞来的导弹 / 炮弹（算挡下了一发），松手时顺着手甩出去</summary>
        public bool GrabProjectile(string holder, Vector3 at)
        {
            if (held.ContainsKey(holder)) return false;
            var p = projectiles.FirstOrDefault(q => q.heldBy == null && q.kind != "bullet" && Vector3.Distance(q.pos, at) < Cfg.GrabR * 1.2f);
            if (p == null) return false;
            p.heldBy = holder; p.pos = at;
            held[holder] = new Held { kind = "missile", proj = p, pos = at, hasPos = true };
            Emit(new Ev { type = "parry", pos = at, kind = p.kind });
            Emit(new Ev { type = "grab", id = -1, pos = at, kind = "missile" });
            return true;
        }

        public void HoldAt(string holder, Vector3 pos)
        {
            if (!held.TryGetValue(holder, out var h)) return;
            h.pos = pos; h.hasPos = true;
            if (h.kind == "enemy") h.enemy.pos = pos;
            if (h.kind == "missile") h.proj.pos = pos;
        }

        /// <summary>松手：快 = 扔（眼睛看哪就往哪扔），慢 = 放下</summary>
        public void Release(string holder, Vector3 vel, Vector3? aimPoint)
        {
            if (!held.TryGetValue(holder, out var h)) return;
            held.Remove(holder);
            if (holder == "both") { wrapBlock["left"] = wrapBlock["right"] = time + 0.8f; hugBlock = time + 0.8f; } else wrapBlock[holder] = time + 0.8f;
            var pos = h.hasPos ? h.pos : h.kind == "enemy" ? h.enemy.pos : headPos;
            var v = vel;
            bool thrown = vel.magnitude > Cfg.ThrowMin;
            if (thrown && aimPoint.HasValue) v = Ballistic(pos, aimPoint.Value, vel.magnitude);
            if (h.kind == "missile") { projectiles.Remove(h.proj); flying.Add(new Flying { kind = "shell", pos = pos, vel = v * 1.2f, thrown = true }); Emit(new Ev { type = "throw", id = -1 }); return; }
            if (h.kind == "enemy" && h.enemy.type == "mech")
            {
                // 机甲不会被一下扔死：它自己飞出去 / 掉下去，落地掉血（见 Enemies.TickMech 的 air 段）
                var m = h.enemy; m.state = EState.Move; m.air = true; m.airThrown = thrown; m.airHit = false; m.heldT = 0; m.pos = pos;
                m.vel = thrown ? v : new Vector3(vel.x, Mathf.Min(0, vel.y), vel.z) * 0.5f;
                Emit(new Ev { type = thrown ? "throw" : "drop", id = m.id });
                return;
            }
            if (h.kind == "enemy") { h.enemy.state = EState.Dead; h.enemy.dead = true; Emit(new Ev { type = thrown ? "throw" : "drop", id = h.enemy.id }); }
            if (h.kind == "prop") Emit(new Ev { type = thrown ? "throw" : "drop", id = -1 });
            flying.Add(new Flying { kind = h.kind == "chunk" ? "chunk" : "vehicle", etype = h.etype, chunk = h.chunk, prop = h.prop, pos = pos, vel = v, spin = new Vector3(rng.Range(-6, 6), rng.Range(-6, 6), rng.Range(-6, 6)), refId = h.kind == "enemy" ? h.enemy.id : -1, thrown = thrown });
        }

        void TickFlying(float dt)
        {
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                var f = flying[i];
                f.age += dt; f.vel += G * dt;
                var prev = f.pos; f.pos += f.vel * dt;
                float r = f.kind == "chunk" ? 0.04f : 0.025f;
                Building hb = null; Enemy he = null; bool ground = false;
                foreach (var b in city.buildings) { if (b.h <= 0 || (f.chunk != null && b.id == f.chunk.from)) continue; if (InsideBuilding(b, f.pos, 0.01f)) { hb = b; break; } }
                if (hb == null) foreach (var e in enemies) { if (e.dead || e.state != EState.Move || e.id == f.refId) continue; if (Vector3.Distance(e.pos, f.pos) < Enemies.R(e) + r) { he = e; break; } }
                if (hb == null && he == null)
                    foreach (var z in city.zones)
                    {
                        float d0 = prev.y - z.origin.y, d1 = f.pos.y - z.origin.y;
                        if (d0 >= 0 && d1 < 0) { var dd = f.pos - z.origin; float a = Vector3.Dot(dd, z.u), bb = Vector3.Dot(dd, z.v); if (a >= 0 && a <= z.w && bb >= 0 && bb <= z.h) { ground = true; break; } }
                    }
                if (hb == null && he == null && !ground && f.pos.y < 0) ground = true;
                if (hb == null && he == null && !ground) { if (f.age > 5) flying.RemoveAt(i); continue; }
                flying.RemoveAt(i);
                float power = f.kind == "chunk" ? 1.6f : 1;
                if (hb != null) ImpactBuilding(hb, f);
                if (he != null) HitEnemy(he, f.vel, "thrown");
                if (f.thrown || f.kind != "vehicle" || f.vel.magnitude > 1) Blast(f.pos, (f.kind == "shell" ? 0.06f : Cfg.BlastVehicle) * power, 0, "impact");
                Emit(new Ev { type = "impact", pos = f.pos, kind = f.kind, n = power });
            }
        }

        // ═══════════ 吐息 ═══════════
        public bool CanBreath => rage >= Cfg.RageMax && breathing <= 0;

        /// <summary>能量波手势（11-11 用户：光波要从两只手中间发出去，位置要对）：
        /// 两只手都张开、没在捏，掌心离得近，伸在身前（离头 22cm 以上）。蓄力 0.45 秒发射；怒气不满只聚到三成就散。
        /// 起点 = 两掌心的中点，沿发射方向往前 4cm（刚好在手前面，不被手挡住）。
        /// 方向只看手，不再从头连过来 —— 原来掌心相对时方向 = “头 → 两手中间”，光束正好和视线重合，看上去像从脸上喷出去的。
        /// 眼睛辅助瞄准：视线落点在手的朝向 40° 以内（已经瞄上的放宽到 55°）时，光束从手里射向眼睛看的地方，手抖不影响准头。
        /// 发射以后只要两只手还在一起（掌心 27cm 以内）就一直跟手，不再要求手指张开 —— 原来发射中手指一蜷 / 追踪一抖就断，光束留在原地和手脱节</summary>
        void TickWave(float dt, List<HandIn> live, Vector3? aim)
        {
            HandIn L = Side(live, "left"), R = Side(live, "right");
            bool firing = breathing > 0, pose = false;
            if (L != null && R != null)
            {
                float gap = Vector3.Distance(L.palm, R.palm);
                // 起手收紧（真机：抱拳、两手靠近楼时误触发）：两手都张开、没捏没握、不在楼群里、手基本不动
                bool shape = firing ? gap < Cfg.WaveGap * 1.8f
                    : (!L.pinch && !R.pinch && !L.grip && !R.grip && L.open && R.open && gap < Cfg.WaveGap && held.Count == 0
                       && L.vel.magnitude < 0.6f && R.vel.magnitude < 0.6f && !AnyBuildingAt((L.palm + R.palm) * 0.5f, 0.05f));
                var mid = (L.palm + R.palm) * 0.5f;
                var hd = WaveHandDir(L, R, mid);
                // 朝前（不朝自己）、手伸在身前
                if (shape && Vector3.Distance(mid, headPos) > Cfg.WaveReach && Vector3.Dot(hd, mid - headPos) > 0)
                {
                    pose = true;
                    var dir = hd; bool assist = false;
                    // 11-12 用户：大招要能跟着手的移动扫 —— 发射以后方向只看手（原来视线一直在辅助瞄准：眼睛不动，手怎么转光束都不动）。
                    // 做法：发射那一刻记下“手的朝向 → 实际光束方向”的偏差（蓄力时眼睛帮忙对准的那一点），之后手转多少、光束就跟着转多少
                    // 11-12 用户真机：光束位置跟手，但方向总是朝下 —— 因为蓄力时“眼睛辅助瞄准”把方向拉到视线落点（玩家低头看着城市），
                    // 发射后又把这个朝下的偏差一直带着。现在不要任何视线辅助：光束只沿两只手指的方向出去，手往哪转就往哪扫
                    if (Cfg.WaveAssist > 0 && !firing && aim.HasValue)
                    {
                        var to = aim.Value - mid;
                        if (to.sqrMagnitude > 0.01f && Vector3.Angle(to, hd) < (waveAssist ? Cfg.WaveAssist + 15 : Cfg.WaveAssist)) { dir = to.normalized; assist = true; }
                    }
                    waveAssist = assist; waveHd = hd;
                    waveMid = waveDir.sqrMagnitude > 0.5f ? Vector3.Lerp(waveMid, mid, 1 - Mathf.Exp(-dt * 30)) : mid;   // 位置紧跟手，只滤掉追踪抖动
                    waveDir = Vector3.Slerp(waveDir.sqrMagnitude > 0.5f ? waveDir : dir, dir, 1 - Mathf.Exp(-dt * 12)).normalized;   // 方向平滑一点，手抖不晃光束
                }
            }
            if (firing)
            {
                if (pose) { beam = new Ray(waveMid + waveDir * Cfg.WaveMuzzle, waveDir); beamLost = 0; }
                else if ((beamLost += dt) > 0.35f) breathing = Mathf.Min(breathing, 0.001f);   // 手分开了：光束停
                charge = 0; waveWant = false;
                return;
            }
            if (!pose)
            {
                charge = Mathf.Max(0, charge - dt * 3); waveWant = false;
                if (charge <= 0) { waveDir = Vector3.zero; waveAssist = false; }   // 下次摆姿势从新的方向开始，不从上次的方向慢慢转过来
                return;
            }
            if (!CanBreath)
            {
                // 怒气不满：能量球只聚到三成就散（给反馈，告诉玩家这个手势是对的）
                charge = Mathf.Min(charge + dt / Cfg.WaveCharge, 0.35f); waveWant = true;
                return;
            }
            waveWant = false;
            charge += dt / Cfg.WaveCharge;
            if (charge >= 1) { beam = new Ray(waveMid + waveDir * Cfg.WaveMuzzle, waveDir); beamLost = 0; StartBreath(); charge = 0; }
        }

        /// <summary>两只手合起来“指”的方向：掌心同向（推掌、上下开合）→ 两个掌心法线的合方向；
        /// 掌心相对（捧着球，法线互相抵消）→ 两只手手指的合方向；中间连续过渡，不会在某个角度突然跳</summary>
        Vector3 WaveHandDir(HandIn L, HandIn R, Vector3 mid)
        {
            var nSum = L.palmN + R.palmN; float a = nSum.magnitude * 0.5f;   // 0 = 掌心正相对，1 = 掌心同向
            var fSum = L.fwd + R.fwd;
            var f = fSum.sqrMagnitude > 0.04f ? fSum.normalized : (mid - headPos).normalized;   // 两手手指正好反着 / 模拟输入没给手指方向：退回“头 → 手”
            float w = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.35f, 0.8f, a));
            var d = (a > 1e-3f ? nSum / (2 * a) : Vector3.zero) * w + f * (1 - w);
            return d.sqrMagnitude > 1e-4f ? d.normalized : f;
        }
        public bool StartBreath()
        {
            if (!CanBreath) return false;
            rage = 0; breathing = Cfg.BreathT; breathCd.Clear();
            AddHeat(Cfg.HeatBreath); idle = 0;
            Emit(new Ev { type = "breath_start" });
            return true;
        }

        void TickBreath(float dt, Ray? g)
        {
            breathing -= dt;
            if (breathing <= 0) { noRageUntil = time + 1.5f; Emit(new Ev { type = "breath_end" }); breathEnd = null; beam = null; return; }
            if (!g.HasValue) return;
            var gz = g.Value;
            keyBuf.Clear(); keyBuf.AddRange(breathCd.Keys); foreach (var k in keyBuf) breathCd[k] -= dt;
            var hit = Raycast(gz, Cfg.BreathRange, false);
            var end = hit.HasValue ? hit.Value.point : gz.GetPoint(Cfg.BreathRange);
            breathEnd = end;
            for (int ei = 0, en = enemies.Count; ei < en && ei < enemies.Count; ei++) if (enemies[ei] is Enemy e)
            {
                if (e.dead || e.state != EState.Move) continue;
                if (DistToSeg(e.pos, gz.origin, end) < Enemies.R(e) + 0.045f)
                {
                    if (e.type == "mech") { if (e.dropT <= 0 && breathCd.GetValueOrDefault("e" + e.id) <= 0) { breathCd["e" + e.id] = 0.5f; e.guard = 0; e.stagger = Mathf.Max(e.stagger, 1); HitEnemy(e, Vector3.zero, "breath"); } }
                    else Enemies.Kill(this, e, "breath");
                }
            }
            if (!hit.HasValue || hit.Value.b == null) return;
            var b = hit.Value.b;
            // 11-13 用户：大招应该直接把楼射倒，而不是一层一层往下掉 —— 普通楼：光束打中哪栋，哪栋就从地基起整栋被轰倒（命中点炸开、顺着光束方向倒、
            // 倒下的那一截会砸到后面的楼 = 连锁、旁边的楼被点着），光束接着往后打。
            // 11-14 用户：整体太简单，一发大招扫一圈就赢了 —— 真机日志：第 7～9 区实际只打了 35～50 秒。原因是光束能直接打碎封印塔的护盾（绕过“先断电”），
            // 电站也是一照就毁。现在：
            //   · 带护盾的塔：光束被护盾挡住（护盾亮一下、溅火花），打不动 —— 必须先断电；
            //   · 断了电的塔：每 0.35 秒削掉三分之一，照满约 1 秒才整座倒；
            //   · 电站：每 0.35 秒打掉一格护甲（见 ToppleAt），不再一照就毁。
            var key = "b" + b.id;
            if (breathCd.GetValueOrDefault(key) > 0) return;
            breathCd[key] = Cfg.BreathTick;
            var flat = Vector3.ProjectOnPlane(gz.direction, Vector3.up);
            Vector3? dir = flat.sqrMagnitude > 1e-4f ? flat.normalized : (Vector3?)null;
            if (b.kind == BuildingKind.Tower)
            {
                if (IsShielded(b)) { Emit(new Ev { type = "shield_hit", id = b.id, pos = hit.Value.point, cause = "breath" }); return; }
                Emit(new Ev { type = "beam_hit", id = b.id, pos = hit.Value.point, dir = dir });
                int cut = Mathf.Max(1, Mathf.CeilToInt(b.maxH / 3f));
                ToppleAt(b, Mathf.Max(0, b.h - cut), b.h - cut <= 0 ? dir : null, "breath");
                return;
            }
            if (b.kind == BuildingKind.Station) { Emit(new Ev { type = "beam_hit", id = b.id, pos = hit.Value.point, dir = dir }); ToppleAt(b, 0, null, "breath"); return; }
            Emit(new Ev { type = "beam_hit", id = b.id, pos = hit.Value.point, dir = dir });
            { var nb = Neighbors(b, 0.1f * S); for (int ni = 0; ni < nb.Count; ni++) Ignite(nb[ni]); }
            ToppleAt(b, 0, dir, "breath");
        }

        // ═══════════ 眼睛：读城（盯住塔 = 显示它连的电站）═══════════
        // 容差按角度算（眼动 4°，退回头部朝向时 7°，最少 4.5cm）；视线短暂滑开不清零，按两倍速度回退
        void TickScan(float dt, Ray g, bool eye = true)
        {
            foreach (var t in city.towers)
            {
                if (t.h <= 0) continue;
                // 11-10：连线平时不显示，盯住塔 —— 或者盯住它的任意一座电站 —— 才显示
                bool Looking(Building x)
                {
                    var top = TopOf(x) + Vector3.up * 0.02f;
                    float along = Vector3.Dot(x.Base - g.origin, g.direction);
                    float d = along > 0 ? SegRayDist(x.Base, top, g) : 9;
                    return d < Mathf.Max(0.045f, along * Mathf.Tan((eye ? 4f : 7f) * Mathf.Deg2Rad));
                }
                bool looking = Looking(t);
                if (!looking) foreach (var sid in t.powerIds) if (city.byId.TryGetValue(sid, out var st) && st.h > 0 && Looking(st)) { looking = true; break; }
                scanAcc[t.id] = Mathf.Max(0, scanAcc.GetValueOrDefault(t.id) + (looking ? dt : -dt * 2));
                if (looking && scanAcc[t.id] >= Cfg.ScanTime && t.scanUntil < time + 0.5f)
                {
                    bool first = t.scanUntil < time;
                    t.scanUntil = time + Cfg.ScanShow;
                    if (first) Emit(new Ev { type = "scan", id = t.id });
                }
            }
        }

        // ═══════════ 头显：手 ═══════════
        readonly HashSet<int> liftIntent = new();
        readonly List<HandIn> liveBuf = new(); readonly List<string> keyBuf = new();
        public bool AnyBuildingAt(Vector3 p, float pad) { foreach (var b in city.buildings) if (b.h > 0 && InsideBuilding(b, p, pad)) return true; return false; }
        static HandIn Side(List<HandIn> l, string side) { foreach (var h in l) if (h.side == side) return h; return null; }
        void TickHands(float dt, List<HandIn> hands, Ray? g)
        {
            var aim = g.HasValue ? AimPoint(g.Value) : null;
            liveBuf.Clear(); foreach (var h0 in hands) if (h0 != null && h0.present) liveBuf.Add(h0);
            var live = liveBuf;
            // 两只手都在同一栋楼 4cm 范围内 = 想拔起这栋楼（这期间它不会被拍 / 压）
            liftIntent.Clear();
            if (live.Count == 2 && !held.ContainsKey("both"))
                foreach (var b in city.buildings)
                    if (b.h > 0 && InsideBuilding(b, live[0].pos, 0.04f) && InsideBuilding(b, live[1].pos, 0.04f)) liftIntent.Add(b.id);
            TickOrbs(dt, live);
            TickWrap(dt, live);
            TickCombo(dt, live);
            foreach (var h in live)
            {
                var id = h.side;
                bool wasPinch = prevPinch.GetValueOrDefault(id);
                prevPinch[id] = h.pinch || h.grip;
                bool holding = held.ContainsKey(id) || held.ContainsKey("both");
                if (holding)
                {
                    if (held.ContainsKey("both"))
                    {
                        var other = live.FirstOrDefault(o => o.side != id);
                        var hb = held["both"];
                        var mid = other != null ? ((hb.wrap ? h.palm : h.pos) + (hb.wrap ? other.palm : other.pos)) * 0.5f : h.pos;
                        HoldAt("both", mid);
                        // 双手夹着（环握）：两只手掌分开超过抓的时候 6cm 才算松手；捏 / 握着的：任一只手松开就算
                        if (hb.wrap && other != null) hb.span0 = Mathf.Min(hb.span0, Vector3.Distance(h.palm, other.palm));
                        bool let = hb.wrap ? other == null || Vector3.Distance(h.palm, other.palm) > hb.span0 + 0.06f
                                           : !(h.pinch || h.grip) || (other != null && !(other.pinch || other.grip));
                        if (let) Release("both", other != null ? (h.vel + other.vel) * 0.5f : h.vel, aim);
                    }
                    else
                    {
                        var hd = held[id];
                        HoldAt(id, hd.wrap ? WrapMid(h) : h.pos);
                        // 环握着的楼：手张开（拇指离四指比抓的时候多 3.5cm）才松手 —— 楼比拳头宽，不能要求握成拳
                        // （松手看“比握得最紧的时候张开了多少”：抓的时候手张得大、手小的人，按绝对张开量会松不开）
                        if (hd.wrap) hd.span0 = Mathf.Min(hd.span0, WrapSpan(h));
                        bool keep = h.pinch || h.grip || (hd.wrap && WrapSpan(h) < hd.span0 + 0.028f);
                        if (!keep) Release(id, h.vel, aim);
                    }
                    continue;
                }
                // 11-11：捏或握都算“合手”；合手那一刻起 0.3 秒内按优先级尝试抓（真机：常常先合手、再靠过去）：
                // 飞来的导弹 → 追兵载具 → 路上的民用车 / 树（表现层摘下来）→ 双手拔楼 → 单手从侧面握楼（整栋拿起来）
                bool closed = h.pinch || h.grip;
                if (closed && !wasPinch) pinchStart[id] = time;
                if (closed && time - pinchStart.GetValueOrDefault(id, -9) < 0.3f && TryGrab(h, live)) continue;
                if (h.grip && !h.pinch && h.vel.magnitude < Cfg.SmashV) continue;   // 握着拳慢慢移动不压楼；握拳快速砸下去照样算拍
                float speed = h.vel.magnitude;
                if (speed > Cfg.SwatV) Swat(h);
                Smash(h, speed, dt);
                CrushSmall(h, speed);
            }
            TickWave(dt, live, aim);
        }

        // ═══════════ 双手招式（11-13 用户：举一反三，怪兽该有的攻击手势）═══════════
        //  合掌拍：两只张开的手快速合到一起，夹在中间的飞行单位（直升机 / 无人机 / 战斗机 / 轰炸机）当场拍扁 —— 像拍蚊子，轰炸机也是一下；
        //  双手砸地：两只手（拳、掌都行）几乎同时往下砸到桌面 → 以两手中间为圆心一圈震波：圈内的地面载具挨一下、普通楼震掉最上面一层、
        //           树和车被震扁，机甲被震得后退（撞到楼就出破绽）。3 秒冷却。电站和封印塔不受影响（它们各有各的规则）。
        float clapCd, quakeCd; readonly Dictionary<string, (float t, Vector3 p, Zone z)> slams = new();
        void TickCombo(float dt, List<HandIn> live)
        {
            clapCd -= dt; quakeCd -= dt;
            if (live.Count < 2 || held.Count > 0 || winT >= 0) return;
            HandIn L = live[0], R = live[1];
            // 合掌拍
            var across = R.palm - L.palm; float gap = across.magnitude;
            if (clapCd <= 0 && gap < 0.2f && gap > 1e-3f && !L.grip && !R.grip && !L.pinch && !R.pinch && Vector3.Dot(L.vel - R.vel, across / gap) > 0.9f)
            {
                int n = 0;
                for (int ei = 0, en = enemies.Count; ei < en && ei < enemies.Count; ei++) if (enemies[ei] is Enemy e)
                {
                    if (e.dead || e.state != EState.Move || !Enemies.Types[e.type].fly) continue;
                    if (DistToSeg(e.pos, L.palm, R.palm) < Enemies.R(e) + 0.05f) { Enemies.Kill(this, e, "clap"); n++; }
                }
                if (n > 0) { clapCd = 0.4f; idle = 0; Emit(new Ev { type = "clap", pos = (L.palm + R.palm) * 0.5f, n = n }); }
            }
            // 双手砸地
            foreach (var h in live)
            {
                float sp = h.vel.magnitude;
                if (h.vel.y > -0.8f || -h.vel.y < sp * 0.7f || h.pinch && !h.grip) continue;
                foreach (var z in city.zones)
                {
                    var d = h.palm - z.origin; float a = Vector3.Dot(d, z.u), b = Vector3.Dot(d, z.v);
                    if (a < -0.08f || a > z.w + 0.08f || b < -0.08f || b > z.h + 0.08f || d.y < -0.03f || d.y > 0.07f) continue;
                    slams[h.side] = (time, new Vector3(h.palm.x, z.origin.y, h.palm.z), z);
                    break;
                }
            }
            if (quakeCd <= 0 && slams.TryGetValue(L.side, out var sl) && slams.TryGetValue(R.side, out var sr) && sl.z == sr.z
                && time - sl.t < 0.18f && time - sr.t < 0.18f && Vector3.Distance(sl.p, sr.p) < 0.55f)
            {
                slams.Clear(); quakeCd = 3f;
                Quake((sl.p + sr.p) * 0.5f, sl.z, 0.24f * S + 0.04f);
            }
        }
        void Quake(Vector3 c, Zone z, float r)
        {
            idle = 0;
            Emit(new Ev { type = "quake", pos = c, r = r });
            for (int ei = 0, en = enemies.Count; ei < en && ei < enemies.Count; ei++) if (enemies[ei] is Enemy e)
            {
                if (e.dead || e.state != EState.Move || Enemies.Types[e.type].fly || e.zone != z) continue;
                var d = e.pos - c; d.y = 0; if (d.magnitude > r + Enemies.R(e)) continue;
                HitEnemy(e, (d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward) * 0.7f, "quake");
            }
            foreach (var b in city.buildings)
            {
                if (b.zone != z || b.h <= 0 || b.kind != BuildingKind.House) continue;
                var d = b.Base - c; d.y = 0; float dist = d.magnitude; if (dist > r) continue;
                var bb = b; var dir = dist > 1e-4f ? d / dist : (Vector3?)null;
                Later(dist / r * 0.25f, () => { if (bb.h > 0) ToppleAt(bb, bb.h - 1, dir, "quake"); });   // 震波往外走：近的先掉、远的后掉
            }
            CrushProp?.Invoke(c, r);
        }

        /// <summary>表现层提供：在 pos 附近 r 内摘一辆民用车，返回它的句柄（没有就 null）</summary>
        public Func<Vector3, float, object> TakeProp;
        /// <summary>表现层提供：手拍 / 压到 pos，半径 r 内的树、车被拍扁，返回拍掉几样（11-11 用户：树这类小东西不能拿，但能压）</summary>
        public Func<Vector3, float, int> CrushProp;
        readonly Dictionary<string, float> crushCd = new();
        void CrushSmall(HandIn h, float speed)
        {
            if (CrushProp == null || ((h.pinch || h.pinchD < 0.06f) && !IsFist(h)) || h.vel.y > -0.25f || speed < Cfg.SmashV) return;   // 往下拍 / 往下压才算，伸手去捏不算
            if (crushCd.GetValueOrDefault(h.side) > time) return;
            // 手要贴近所在城区的地面（树、车都只有两三厘米高）
            foreach (var z in city.zones)
            {
                var d = h.palm - z.origin; float a = Vector3.Dot(d, z.u), b = Vector3.Dot(d, z.v);
                if (a < -0.02f || a > z.w + 0.02f || b < -0.02f || b > z.h + 0.02f || d.y < -0.02f || d.y > 0.06f) continue;
                if (CrushProp(h.palm, 0.045f) > 0) { crushCd[h.side] = time + 0.12f; idle = 0; }
                return;
            }
        }

        bool TryGrab(HandIn h, List<HandIn> live)
        {
            var id = h.side;
            if (held.ContainsKey(id)) return false;
            if (GrabProjectile(id, h.pos)) return true;
            var e = NearestEnemy(h.pos, Cfg.GrabR);
            if (e != null && GrabEnemy(id, e)) { HoldAt(id, h.pos); return true; }
            var prop = TakeProp?.Invoke(h.pos, Cfg.GrabR);
            if (prop != null) { held[id] = new Held { kind = "prop", prop = prop, pos = h.pos, hasPos = true, etype = "civ" }; Emit(new Ev { type = "grab", id = -1, pos = h.pos, kind = "prop" }); return true; }
            // 双手拔楼：两只手都合上、都在同一栋楼旁边
            var o = live.FirstOrDefault(x => x.side != id && (x.pinch || x.grip) && !held.ContainsKey(x.side));
            if (o != null)
            {
                var b2 = city.buildings.FirstOrDefault(x => CanLift(x) && InsideBuilding(x, h.pos, 0.04f) && InsideBuilding(x, o.pos, 0.04f));
                if (b2 != null && Uproot("both", b2)) { var mp = (h.pos + o.pos) * 0.5f; held["both"].grabY = Mathf.Clamp(mp.y - b2.Base.y, 0, b2.TopH(b2.maxH)); HoldAt("both", mp); return true; }
            }
            // 单手从侧面握住一栋楼（11-11 用户）：手在楼侧面 3.5cm 内、低于楼顶 → 整栋拿起来（选水平距离最近的那栋）
            Building best = null; float bd = 9;
            foreach (var b in city.buildings)
            {
                if (b.h <= 0 || b.kind == BuildingKind.Station || h.pos.y > TopOf(b).y - 0.004f || h.pos.y < b.Base.y - 0.01f || !InsideBuilding(b, h.pos, 0.035f)) continue;
                var d = h.pos - b.Base; d.y = 0;
                if (d.magnitude < bd) { bd = d.magnitude; best = b; }
            }
            if (best != null && Uproot(id, best)) { held[id].grabY = Mathf.Clamp(h.pos.y - best.Base.y, 0, best.TopH(best.maxH)); HoldAt(id, h.pos); return true; }
            return false;
        }

        // ═══════════ 环握（11-11 用户：为什么拿不起整栋楼）═══════════
        // 原来拿楼要“捏”（拇指食指相距 4cm 内）或“握”（四指蜷成拳），可楼有 5～9cm 宽：手环着楼身时手指是半张开的，两个条件都满足不了。
        // 环握 = 拇指在楼的一侧、四指在另一侧（楼夹在虎口里），往里一收（或贴着楼停 0.2 秒）→ 整栋拿起来；
        // 楼太宽一只手环不住：两只手掌一左一右夹住，停 0.25 秒 → 双手抱起来。环握期间这栋楼不会被这只手拍 / 压坏。
        static Vector3 WrapMid(HandIn h) => h.nPts >= 4 ? (h.pts[1] + (h.pts[2] + h.pts[3]) * 0.5f) * 0.5f : h.pos;
        static float WrapSpan(HandIn h) => h.nPts >= 4 ? Vector3.Distance(h.pts[1], (h.pts[2] + h.pts[3]) * 0.5f) : 9;
        readonly Dictionary<string, (int id, float t, float span0)> wrapState = new();
        readonly Dictionary<string, float> wrapBlock = new();   // 刚松手的那只手 0.8 秒内不再环握（不然张开的手还在楼群里，马上又握起旁边一栋）
        (int id, float t) hugState = (-1, 0); float hugBlock;
        static float DistToSegXZ(Vector3 c, Vector3 a, Vector3 b)
        {
            var ab = new Vector2(b.x - a.x, b.z - a.z); var ac = new Vector2(c.x - a.x, c.z - a.z);
            float k = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(ac, ab) / ab.sqrMagnitude) : 0;
            return (ac - ab * k).magnitude;
        }
        // 11-17：电站的护盾罩碎了以后（护甲只剩最后一格）也能像别的楼一样拿起来；罩着护盾的拿不动
        bool CanLift(Building b) => b.h > 0 && !(b.kind == BuildingKind.Station && b.armor > 1) && !(b.kind == BuildingKind.Tower && IsShielded(b));
        void TickWrap(float dt, List<HandIn> live)
        {
            // 单手环握
            foreach (var h in live)
            {
                var id = h.side;
                if (held.ContainsKey(id) || held.ContainsKey("both") || h.nPts < 4 || h.pinch || time < wrapBlock.GetValueOrDefault(id)) { wrapState.Remove(id); continue; }
                Vector3 thumb = h.pts[1], fing = (h.pts[2] + h.pts[3]) * 0.5f; float span = Vector3.Distance(thumb, fing);
                var mid = (thumb + fing) * 0.5f;
                Building best = null; float bd = 9;
                foreach (var b in city.buildings)
                {
                    if (!CanLift(b)) continue;
                    float foot = Mathf.Min(b.fw, b.fd);
                    if (mid.y < b.Base.y - 0.01f || mid.y > TopOf(b).y + 0.015f) continue;
                    if (span > b.Foot + 0.075f || span < foot * 0.45f) continue;                      // 手张得和楼差不多宽（不是张着手路过，也不是已经捏拢）
                    float d = DistToSegXZ(b.Base, thumb, fing);
                    if (d > foot * 0.5f) continue;                                                     // 拇指 → 四指的连线穿过楼身
                    var c = b.Base; c.y = mid.y;
                    if (Vector3.Distance(thumb, c) < foot * 0.3f || Vector3.Distance(fing, c) < foot * 0.3f) continue;   // 指尖插在楼正中间：那是在戳楼，不是环握
                    if (d < bd) { bd = d; best = b; }
                }
                if (best == null) { wrapState.Remove(id); continue; }
                liftIntent.Add(best.id);
                var st = wrapState.TryGetValue(id, out var s0) && s0.id == best.id ? s0 : (best.id, 0f, span);
                st = (st.Item1, st.Item2 + dt, Mathf.Max(st.Item3, span));
                wrapState[id] = st;
                bool squeeze = st.Item3 - span > 0.012f || span < best.Foot + 0.02f;                 // 往里收了 1.2cm，或手指已经贴到楼身
                // 收手 = 马上拿起；没收手、只是环着不动：要几乎静止停满 0.5 秒（手在楼群里路过 / 伸过去拍别的楼不会误拿）
                float sp = h.vel.magnitude;
                if (((squeeze && st.Item2 > 0.08f && sp < 0.6f) || (st.Item2 > 0.5f && sp < 0.2f)) && Uproot(id, best))
                {
                    var hd = held[id]; hd.wrap = true; hd.span0 = Mathf.Max(span, 0.03f); hd.grabY = Mathf.Clamp(mid.y - best.Base.y, 0, best.TopH(best.maxH));
                    HoldAt(id, mid); wrapState.Remove(id);
                }
            }
            // 双手抱：两只手掌一左一右夹住同一栋楼
            HandIn L = Side(live, "left"), R = Side(live, "right");
            if (L == null || R == null || held.Count > 0 || time < hugBlock) { hugState = (-1, 0); return; }
            float gap = Vector3.Distance(L.palm, R.palm); var pm = (L.palm + R.palm) * 0.5f;
            Building hug = null;
            foreach (var b in city.buildings)
            {
                if (!CanLift(b) || pm.y < b.Base.y - 0.01f || pm.y > TopOf(b).y + 0.02f) continue;
                if (gap > b.Foot + 0.13f || gap < Mathf.Min(b.fw, b.fd) * 0.6f) continue;
                if (DistToSegXZ(b.Base, L.palm, R.palm) > Mathf.Min(b.fw, b.fd) * 0.5f) continue;
                var c = b.Base; c.y = pm.y;
                if (Vector3.Dot(L.palm - c, R.palm - c) > 0) continue;                                // 两只手要在楼的两侧
                hug = b; break;
            }
            if (hug == null) { hugState = (-1, 0); return; }
            liftIntent.Add(hug.id);
            hugState = hugState.id == hug.id ? (hug.id, hugState.t + dt) : (hug.id, 0f);
            if (hugState.t > 0.25f && L.vel.magnitude < 0.5f && R.vel.magnitude < 0.5f && Uproot("both", hug))
            {
                var hd = held["both"]; hd.wrap = true; hd.span0 = gap; hd.grabY = Mathf.Clamp(pm.y - hug.Base.y, 0, hug.TopH(hug.maxH));
                HoldAt("both", pm); hugState = (-1, 0);
            }
        }

        public void Swat(HandIn h)
        {
            for (int ei = 0, en = enemies.Count; ei < en && ei < enemies.Count; ei++) if (enemies[ei] is Enemy e)
            {
                if (e.dead || e.state != EState.Move || !Enemies.Types[e.type].fly) continue;
                if (NearHand(h, e.pos, Enemies.R(e) + 0.04f)) HitEnemy(e, h.vel, "swat");
            }
            // 炮弹 / 导弹：整只手（掌心 + 指尖）8cm 内就算拍到（站着原地玩，拍开比躲更舒服）
            for (int i = projectiles.Count - 1; i >= 0; i--) if (NearHand(h, projectiles[i].pos, projectiles[i].kind == "bullet" ? 0.05f : 0.08f)) Parry(i, h.vel);
        }
        static bool NearHand(HandIn h, Vector3 p, float r)
        {
            if (Vector3.Distance(p, h.pos) < r) return true;
            for (int i = 0; i < h.nPts; i++) if (Vector3.Distance(p, h.pts[i]) < r) return true;
            return false;
        }

        /// <summary>
        /// 手碰楼（11-08 真机：只看掌心 + 0.9m/s 时几乎拍不到，楼又小）：
        /// 用整只手的接触点（掌心 + 五个指尖 + 食指中节）；手速 ≥ SmashV 时碰到就拍；
        /// 慢慢按进楼顶 0.25 秒也会压塌最上面一层（不想用力拍也能玩）。
        /// </summary>
        readonly Dictionary<int, float> pressT = new();
        /// <summary>
        /// 握着拳挥过来（11-13 用户：握拳砸下去应该和往下拍一样）。原来握拳完全打不动东西：握拳时拇指和食指尖本来就挨着，
        /// 被“手呈捏的姿势 = 正要去拿，不算拍”这条规则挡掉了。拳头握好 0.3 秒以上（刚合手的那 0.3 秒留给“抓”）、手速够快 = 出拳，不看拇指食指的距离。
        /// 方向规则和拍一样：往下 = 砸扁，横着 = 整截朝出拳方向倒（直拳、摆拳、反手、手刀都算）
        /// </summary>
        bool IsFist(HandIn h) => h.grip && time - pinchStart.GetValueOrDefault(h.side, -9) > 0.3f && h.vel.magnitude > Cfg.SmashV;
        public void Smash(HandIn h, float speed = 9, float dt = 0)
        {
            bool fist = IsFist(h);
            int n = h.pts != null && h.nPts > 0 ? h.nPts : 0;
            Vector3 Pt(int i) => n > 0 ? h.pts[i] : h.pos;
            int count = Mathf.Max(1, n);
            foreach (var b in city.buildings)
            {
                if (b.h <= 0 || b.hitCd > 0) continue;
                int hit = -1;
                for (int i = 0; i < count && hit < 0; i++) if (InsideBuilding(b, Pt(i), 0.006f)) hit = i;
                if (b.kind == BuildingKind.Tower && IsShielded(b))
                {
                    if (hit >= 0 && b.bounceCd < time && speed > 0.3f)
                    {
                        b.bounceCd = time + 0.6f;
                        Hurt(Cfg.ShieldBounceDmg, Pt(hit), "shield");
                        Emit(new Ev { type = "shield_hit", id = b.id, pos = Pt(hit) });
                    }
                    continue;
                }
                if (hit < 0) { pressT.Remove(b.id); continue; }
                // 11-10 用户：想拿起楼时常被误拍碎 —— 手呈捏的姿势（拇指食指靠近 / 已捏上）不算拍也不算压；
                // 两只手都伸到这栋楼旁边（想拔起来）也不算
                if (((h.pinch || h.pinchD < 0.075f) && !fist) || liftIntent.Contains(b.id)) { pressT.Remove(b.id); continue; }
                if (speed > Cfg.SmashV) { pressT.Remove(b.id); if (SmashBuilding(b, Pt(hit), h.vel)) Debug.Log($"[怪兽][拍] 楼 {b.id} 速度 {speed:0.00}m/s 点 {hit}"); continue; }
                // 慢压
                float t = pressT.GetValueOrDefault(b.id) + dt;
                pressT[b.id] = t;
                if (t > 0.25f) { pressT.Remove(b.id); if (ToppleAt(b, b.h - 1, null, "smash")) Debug.Log($"[怪兽][压] 楼 {b.id}"); b.hitCd = 0.35f; }
            }
            for (int ei = 0, en = enemies.Count; ei < en && ei < enemies.Count; ei++) if (enemies[ei] is Enemy e)
            {
                if (e.dead || e.state != EState.Move || Enemies.Types[e.type].fly || speed < Cfg.SmashV) continue;
                // 车只算“往下拍”：手往下有速度、手也不是正要捏的样子（11-09 真机：伸手去捏车，车先被碰歪了）
                if (h.vel.y > -0.3f || ((h.pinchD < 0.075f || h.pinch) && !fist)) continue;
                for (int i = 0; i < count; i++)
                    if (Vector3.Distance(e.pos, Pt(i)) < Enemies.R(e) + 0.015f)
                    {
                        if (e.hitCd > time) break;
                        e.hitCd = time + 0.25f;
                        HitEnemy(e, h.vel, "smash");
                        break;
                    }
            }
        }

        /// <summary>拍开飞来的炮弹 / 导弹：它会顺着手的方向飞出去，砸到哪炸到哪</summary>
        public void Parry(int i, Vector3 vel)
        {
            var p = projectiles[i];
            projectiles.RemoveAt(i);
            Emit(new Ev { type = "parry", pos = p.pos, kind = p.kind });
            if (p.kind == "bullet") return;
            flying.Add(new Flying { kind = "shell", pos = p.pos, vel = vel * 1.2f, thrown = true });
        }

        void TickProjectiles(float dt)
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                if (p.heldBy != null) continue;   // 捏在手里：位置跟手（HoldAt），不飞、不计时
                p.age += dt;
                if (p.kind == "missile")
                {
                    // 冲向锁定时的位置（不追踪当前的头），所以预警时侧身就能躲开
                    var toT = p.target - p.pos;
                    if (!p.passed && toT.magnitude > 0.03f) p.vel = (p.vel.normalized * 0.9f + toT.normalized * 0.1f).normalized * Mathf.Min(1.4f, p.vel.magnitude + dt * 1.6f);
                    else p.passed = true;
                }
                if (p.kind == "bomb") p.vel += G * dt * 0.6f;
                var prev = p.pos;
                p.pos += p.vel * dt;
                if (HitsPlayer(p.pos))
                {
                    projectiles.RemoveAt(i);
                    if (p.kind == "missile" || p.kind == "bomb") Blast(p.pos, 0.06f, 0, p.kind);
                    Hurt(p.dmg, p.vel.sqrMagnitude > 1e-6f ? p.pos - p.vel.normalized * 0.6f : p.pos, p.kind);   // 来向：弹道反方向（受伤提示指向打你的地方）
                    continue;
                }
                if (p.kind == "bomb")
                {
                    var b = city.buildings.FirstOrDefault(x => x.h > 0 && InsideBuilding(x, p.pos, 0));
                    bool onZone = b == null && city.zones.Any(z => OnZone(z, p.pos));
                    if (b != null || onZone || p.pos.y < 0) { projectiles.RemoveAt(i); Blast(p.pos, 0.08f, 0, "bomb"); continue; }
                }
                else if (ShotStopped(p, prev)) { projectiles.RemoveAt(i); continue; }
                if (p.age > (p.kind == "missile" ? 3.5f : 2.5f))
                {
                    projectiles.RemoveAt(i);
                    if (p.kind == "missile") Blast(p.pos, 0.05f, 0, "missile");
                }
            }
        }

        // ═══════════ 弹药碰到楼 / 桌柜（11-17 用户：所有事物、所有交互都要合乎常理）═══════════
        // 以前子弹、炮弹、导弹只认玩家：楼后面的坦克隔着整栋楼照样打中人，地板河道里的船隔着桌子照样打中人。
        // 现在每一步走过的那一小段先和楼、桌柜求交，碰到就停在那儿：
        //   子弹：溅火花；
        //   炮弹 / 机甲的等离子弹：炸在墙上 —— 矮楼（≤3 层）或打在楼顶附近直接塌，高楼崩碎块并累计，累计到楼高四成五从弹着点折断（和扔东西砸楼同一套规矩，见 ImpactBuilding）；
        //   导弹：爆炸（点着楼、掀掉楼顶；不炸死发射它的那辆车）。
        // 电站和封印塔是军方自己的设施：只挡弹、不受损（不然军队会替玩家把电站打掉）。出膛时炮口所在的那栋楼不算（贴着墙开炮，炮口会伸进墙里）。
        // 追兵开火前用 LineBlocked 先看打不打得到（见 Enemies.Tick）：看不见玩家就不开火，所以平时打在楼上的主要是等急了的坦克、机甲和飞过头的导弹
        bool ShotStopped(Projectile p, Vector3 prev)
        {
            if (!p.init) { p.init = true; p.skip = BuildingAround(prev); }
            var step = p.pos - prev;
            if (!SegHit(prev, step, p.skip, true, out var hb, out float t)) return false;
            var at = prev + step * t;
            var flat = new Vector3(p.vel.x, 0, p.vel.z); Vector3? dir = flat.sqrMagnitude > 1e-6f ? flat.normalized : null;
            if (p.kind == "missile") { Blast(at, 0.05f, 0, "missile", p.from); return true; }
            if (p.kind == "bullet") { Emit(new Ev { type = "ricochet", pos = at, dir = dir }); return true; }
            Emit(new Ev { type = "blast", pos = at, r = 0.035f, cause = "shot" });
            if (hb == null) return true;
            if (hb.kind == BuildingKind.Tower && IsShielded(hb)) { Emit(new Ev { type = "shield_hit", id = hb.id, pos = at, cause = "shot" }); return true; }
            if (hb.kind != BuildingKind.House) return true;
            int dmg = p.kind == "fist" ? 3 : 1;
            int k = hb.StoreyAt(Mathf.Max(0, at.y - hb.Base.y));
            hb.dmg += dmg;
            if (hb.h <= 3 || k >= hb.h - dmg || hb.dmg >= Mathf.Max(3, hb.maxH * 0.45f)) { hb.dmg = 0; ToppleAt(hb, Mathf.Clamp(k, 0, hb.h - 1), hb.h - k >= 2 ? dir : null, "shot"); }
            else Emit(new Ev { type = "chip", id = hb.id, pos = at, dir = dir, n = dmg });
            return true;
        }

        Building BuildingAround(Vector3 p)
        {
            foreach (var b in city.buildings)
            {
                if (b.h <= 0) continue;
                var d = p - b.Base; Body(b, out float hw, out float v0, out float v1); float bv = Vector3.Dot(d, b.zone.v);
                if (Mathf.Abs(Vector3.Dot(d, b.zone.u)) < hw + 0.002f && bv > v0 - 0.002f && bv < v1 + 0.002f && d.y > -0.002f && d.y < HeightOf(b) + 0.002f) return b;
            }
            return null;
        }
        /// <summary>楼体模型的真实占地（相对 Base：u 方向 ±hw，v 方向 v0～v1）。楼是等比缩放的，常比分给它的 fw × fd 窄或浅（多出来的是院子），弹药要打在看得见的墙上</summary>
        static void Body(Building b, out float hw, out float v0, out float v1)
        {
            if (b.bodyD > 0) { hw = b.bodyW / 2; v0 = b.bodyOff - b.bodyD / 2; v1 = b.bodyOff + b.bodyD / 2; }
            else { hw = b.fw / 2; v0 = -b.fd / 2; v1 = b.fd / 2; }
        }

        /// <summary>从 from 到 to 的直线上有没有楼 / 桌柜挡着（追兵开火前先看一眼：打不到就不开火）</summary>
        public bool LineBlocked(Vector3 from, Vector3 to) => SegHit(from, to - from, BuildingAround(from), false, out _, out _);

        /// <summary>线段 a → a + step 最先碰到的楼（hb）或桌柜（hb = null）；t = 碰到的位置（0～1）。near = 这一段很短（弹药的一步），先按距离粗筛</summary>
        bool SegHit(Vector3 a, Vector3 step, Building skip, bool near, out Building hb, out float t)
        {
            hb = null; t = 2; bool hit = false;
            float len = near ? step.magnitude : 0;
            foreach (var b in city.buildings)
            {
                if (b.h <= 0 || b == skip) continue;
                if (near) { float dx = b.Base.x - a.x, dz = b.Base.z - a.z, reach = len + b.fw + b.fd; if (dx * dx + dz * dz > reach * reach) continue; }
                Body(b, out float hw, out float v0, out float v1);
                float tb = SegBox(a, step, b.Base, b.zone.u, b.zone.v, -hw, hw, v0, v1, 0, HeightOf(b));
                if (tb >= 0 && tb < t) { t = tb; hb = b; hit = true; }
            }
            // 桌子 / 柜子（连同上面的沙盘底座）：从台面往下整块都算实心；地板上的城区没有
            foreach (var (z, top, m) in Obstacles())
            {
                if (z.kind == ZoneKind.Floor) continue;
                float tz = SegBox(a, step, z.origin, z.u, z.v, -m, z.w + m, -m, z.h + m, -3, -0.004f);
                if (tz >= 0 && tz < t) { t = tz; hb = null; hit = true; }
            }
            return hit;
        }

        /// <summary>线段进一个盒子的位置（0～1，没碰到 = -1）。盒子：原点 o，水平两轴 u / v 上的范围 + 竖直范围（都相对 o）。不分配内存</summary>
        static float SegBox(Vector3 a, Vector3 d, Vector3 o, Vector3 u, Vector3 v, float u0, float u1, float v0, float v1, float y0, float y1)
        {
            var l = a - o; float t0 = 0, t1 = 1;
            return Slab(Vector3.Dot(l, u), Vector3.Dot(d, u), u0, u1, ref t0, ref t1) && Slab(Vector3.Dot(l, v), Vector3.Dot(d, v), v0, v1, ref t0, ref t1) && Slab(l.y, d.y, y0, y1, ref t0, ref t1) ? t0 : -1;
        }
        static bool Slab(float o, float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (Mathf.Abs(d) < 1e-9f) return o >= lo && o <= hi;
            float ta = (lo - o) / d, tb = (hi - o) / d;
            if (ta > tb) (ta, tb) = (tb, ta);
            t0 = Mathf.Max(t0, ta); t1 = Mathf.Min(t1, tb);
            return t0 <= t1;
        }

        // ═══════════ 查询 ═══════════
        public Enemy NearestEnemy(Vector3 p, float r)
        {
            Enemy best = null; float bd = r;
            foreach (var e in enemies)
            {
                if (e.dead || e.state != EState.Move) continue;
                if (e.type == "mech" && (e.dropT > 0 || e.landT < Enemies.MechRise || e.air || e.hop > 0)) continue;
                float d = Vector3.Distance(e.pos, p) - Enemies.R(e);
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        public struct Hit { public float t; public Vector3 point; public Building b; public Enemy e; public Zone zone; }

        /// <summary>射线拾取：最近的楼 / 追兵 / 城区地面</summary>
        public Hit? Raycast(Ray r, float maxT = 4, bool withEnemies = true)
        {
            Hit? best = null;
            void Consider(float t, Building b, Enemy e, Zone z) { if (t >= 0 && t < maxT && (!best.HasValue || t < best.Value.t)) best = new Hit { t = t, point = r.GetPoint(t), b = b, e = e, zone = z }; }
            foreach (var b in city.buildings) if (b.h > 0) Consider(RayBuilding(b, r), b, null, null);
            if (withEnemies) foreach (var e in enemies) if (!e.dead && e.state == EState.Move) Consider(RaySphere(r, e.pos, Enemies.R(e) + 0.012f), null, e, null);
            foreach (var z in city.zones)
            {
                if (Mathf.Abs(r.direction.y) < 1e-6f) continue;
                float t = (z.origin.y - r.origin.y) / r.direction.y;
                if (t < 0) continue;
                var d = r.GetPoint(t) - z.origin;
                float a = Vector3.Dot(d, z.u), bb = Vector3.Dot(d, z.v);
                if (a >= 0 && a <= z.w && bb >= 0 && bb <= z.h) Consider(t, null, null, z);
            }
            return best;
        }

        public Vector3? AimPoint(Ray g) { var h = Raycast(g, 4); return h.HasValue ? h.Value.point : (Vector3?)null; }

        /// <summary>结算星级</summary>
        public int Rating()
        {
            if (result != "win") return 0;
            int s = 1;
            if (time < sealMax * 0.6f) s++;
            if (destroyed / (float)totalMods > 0.45f || maxChain >= 8) s++;
            return s;
        }

        // ───────── 几何工具 ─────────
        public static bool InsideBuilding(Building b, Vector3 p, float pad = 0)
        {
            var d = p - b.Base;
            float c = d.y;
            return Mathf.Abs(Vector3.Dot(d, b.zone.u)) < b.fw / 2 + pad && Mathf.Abs(Vector3.Dot(d, b.zone.v)) < b.fd / 2 + pad && c >= -pad && c <= HeightOf(b) + pad;
        }

        static bool OnZone(Zone z, Vector3 p)
        {
            var d = p - z.origin;
            if (d.y > 0.002f || d.y < -0.03f) return false;
            float a = Vector3.Dot(d, z.u), b = Vector3.Dot(d, z.v);
            return a >= 0 && a <= z.w && b >= 0 && b <= z.h;
        }

        public static float RayBuilding(Building b, Ray r)
        {
            var lo = r.origin - b.Base;
            float[] O = { Vector3.Dot(lo, b.zone.u), Vector3.Dot(lo, b.zone.v), lo.y };
            float[] D = { Vector3.Dot(r.direction, b.zone.u), Vector3.Dot(r.direction, b.zone.v), r.direction.y };
            float[] mn = { -b.fw / 2, -b.fd / 2, 0 }, mx = { b.fw / 2, b.fd / 2, HeightOf(b) };
            float t0 = float.NegativeInfinity, t1 = float.PositiveInfinity;
            for (int k = 0; k < 3; k++)
            {
                if (Mathf.Abs(D[k]) < 1e-9f) { if (O[k] < mn[k] || O[k] > mx[k]) return -1; continue; }
                float a = (mn[k] - O[k]) / D[k], c = (mx[k] - O[k]) / D[k];
                if (a > c) (a, c) = (c, a);
                t0 = Mathf.Max(t0, a); t1 = Mathf.Min(t1, c);
                if (t0 > t1) return -1;
            }
            return t0 >= 0 ? t0 : (t1 >= 0 ? 0 : -1);
        }

        static float RaySphere(Ray r, Vector3 c, float rad)
        {
            var oc = r.origin - c;
            float b = Vector3.Dot(oc, r.direction), q = Vector3.Dot(oc, oc) - rad * rad, h = b * b - q;
            if (h < 0) return -1;
            float t = -b - Mathf.Sqrt(h);
            return t >= 0 ? t : -1;
        }

        public static float DistToSeg(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(Vector3.Dot(ab, ab), 1e-9f));
            return Vector3.Distance(p, a + ab * t);
        }

        static float SegRayDist(Vector3 a, Vector3 b, Ray r)
        {
            float best = float.MaxValue;
            for (int i = 0; i <= 8; i++)
            {
                var p = Vector3.Lerp(a, b, i / 8f);
                float t = Mathf.Max(0, Vector3.Dot(p - r.origin, r.direction));
                best = Mathf.Min(best, Vector3.Distance(p, r.GetPoint(t)));
            }
            return best;
        }

        /// <summary>从 p 扔到 target 的抛物线初速；手越快飞得越平</summary>
        public static Vector3 Ballistic(Vector3 p, Vector3 target, float handSpeed)
        {
            var d = target - p;
            float flat = new Vector2(d.x, d.z).magnitude;
            float speed = Mathf.Max(Cfg.ThrowSpeed, handSpeed * 1.4f);
            float T = Mathf.Clamp(new Vector2(flat, d.y).magnitude / speed, 0.12f, 0.9f);
            return new Vector3(d.x / T, d.y / T - 0.5f * Cfg.Gravity * T, d.z / T);
        }
    }
}
