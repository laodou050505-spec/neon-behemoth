// 追兵：全部是无人军用载具，没有人类。每种都是多部件的（坦克 = 底盘 + 炮塔 + 炮管……），逻辑层给出 aim / launcher 等状态，表现层据此转动部件。
// 地面单位沿城区里的道路网行驶；飞行单位在玩家头部周围飞。和原型 src/logic/enemies.js 一致。
using System.Collections.Generic;
using UnityEngine;

namespace Monster.Game
{
    public class EType
    {
        public int stars, hp; public float speed, r; public bool ground, fly, fixedPos, boss; public string onlyZone, label;
    }

    public enum EState { Move, Held, Dead }

    public class Enemy
    {
        public int id, hp, maxHp; public string type; public float t;
        public Vector3 pos, vel, fwd = Vector3.right, up = Vector3.up;
        public Vector3? aim; public float launcher, cd;
        public Vector3? muzzle; public Vector3 muzzleDir;   // 表现层每帧写：发射管口的世界位置 / 朝向（导弹从这里出）
        public EState state = EState.Move; public bool dead; public float stagger, guard = 1;
        public bool parked;   // 停在原地不开（教学：导弹车直接就位、很快发射）
        // 道路
        public Zone zone; public List<float> netA, netB; public int i, j, ni, nj, pi = -1, pj = -1; public float prog, segLen, a = -9, b = -9;
        // 飞行
        public float orbitA, orbitR, orbitH; public int passes; public string phase; public Vector3 diveTarget;
        public int burst; public float burstCd, recoil, lockT, hatch, dropT, hitCd, blockT;
        public Vector3 lockTarget, knock;
        public int station = -1; public bool offroad, working;
        public Zone home;   // 无人机：从哪片城区的街上起飞（算净空高度时不算这片）
        // 机甲：landT 落地后过了多久（−1 = 还没落地）；charging 正在蓄力（站定、抬炮、炮口聚能），charge 0～1
        public float landT = -1, charge; public bool charging, fallTold;
        // 机甲被抓起来 / 扔出去（11-17）：heldT 被攥了多久；air 正在空中（被扔 / 被松手 / 挣脱后掉下去）；airThrown 是被扔的；airHit 路上撞过楼；
        // hopPending 摔在地上、缓过来以后要喷气跳回自己的路上；hop 0～1 跳回去的进度
        public float heldT, hop; public bool air, airThrown, airHit, hopPending; public Vector3 hopFrom;
    }

    public static class Enemies
    {
        public const float VScale = 1.6f;
        public static readonly Dictionary<string, EType> Types = new()
        {
            ["police"] = new EType { stars = 1, hp = 1, speed = 0.12f, r = 0.035f, ground = true, label = "无人警车" },
            ["boat"] = new EType { stars = 1, hp = 1, speed = 0.08f, r = 0.038f, ground = true, onlyZone = "harbor", label = "巡逻艇" },
            ["heli"] = new EType { stars = 2, hp = 1, speed = 0.35f, r = 0.048f, fly = true, label = "武装直升机" },
            ["apc"] = new EType { stars = 2, hp = 2, speed = 0.09f, r = 0.042f, ground = true, label = "装甲运兵车" },
            ["drone"] = new EType { stars = 99, hp = 1, speed = 0.32f, r = 0.019f, fly = true, label = "自爆无人机" },
            ["tank"] = new EType { stars = 3, hp = 2, speed = 0.06f, r = 0.048f, ground = true, label = "坦克" },
            ["rocket"] = new EType { stars = 3, hp = 1, speed = 0.07f, r = 0.048f, ground = true, label = "导弹车" },
            ["jet"] = new EType { stars = 4, hp = 1, speed = 1.1f, r = 0.048f, fly = true, label = "战斗机" },
            ["bomber"] = new EType { stars = 4, hp = 2, speed = 0.32f, r = 0.072f, fly = true, label = "轰炸机" },
            ["sam"] = new EType { stars = 4, hp = 2, speed = 0, r = 0.048f, ground = true, fixedPos = true, onlyZone = "cabinet", label = "防空导弹" },
            ["mech"] = new EType { stars = 5, hp = 10, speed = 0.05f, r = 0.08f, ground = true, onlyZone = "table", boss = true, label = "机甲" },
            ["repair"] = new EType { stars = 99, hp = 1, speed = 0.1f, r = 0.024f, ground = true, label = "维修车" },
        };

        static readonly Dictionary<int, (string, float)[]> Pool = new()
        {
            // 11-12 用户：还是按星级出兵，但地面上别全是警车 —— 警车权重降下来，装甲车 1 星就出、坦克 2 星就出
            [1] = new[] { ("police", 3f), ("boat", 2f), ("apc", 2f) },
            [2] = new[] { ("police", 1.5f), ("boat", 1f), ("heli", 4f), ("apc", 3f), ("tank", 1.5f) },
            [3] = new[] { ("police", 0.5f), ("heli", 3f), ("apc", 2f), ("tank", 4f), ("rocket", 3f) },
            [4] = new[] { ("heli", 2f), ("apc", 1f), ("tank", 3f), ("rocket", 3f), ("jet", 4f), ("bomber", 2f), ("sam", 1f) },
            [5] = new[] { ("heli", 2f), ("tank", 3f), ("rocket", 3f), ("jet", 4f), ("bomber", 2f), ("sam", 1f) },
        };
        public static (string, float)[] PoolFor(int stars) => Pool[Mathf.Clamp(stars, 1, 5)];
        // 11-17 用户：不卡了以后难度又偏低 —— 追兵来得快一成半、同时在场的上限 +1
        public static int MaxAlive(int stars) => stars <= 0 ? 0 : 3 + stars * 2;
        public static float SpawnInterval(int stars) => Mathf.Max(1.2f, 3.6f - stars * 0.48f);
        static int eid = 1;

        static bool ZoneIs(Zone z, string kind) => kind == "harbor" ? z.harbor : kind == "table" ? z.kind == ZoneKind.Table : kind == "cabinet" ? z.kind == ZoneKind.Cabinet : kind == "floor" ? z.kind == ZoneKind.Floor : false;

        // ───────── 道路网 ─────────
        static Vector3 NodePos(Enemy e, int i, int j, float hover = 0) => e.zone.Point(e.netA[i], e.netB[j], hover);

        // 船只在港口水面的航道上，车辆只在陆地道路上（水面那几行的路口不走）
        static bool Allowed(Enemy e, int j)
        {
            if (!e.zone.harbor) return e.type != "boat";
            bool wet = e.netB[j] >= e.zone.WaterB - 1e-6f;
            return e.type == "boat" ? wet : !wet;
        }

        static void PlaceOnRoad(Enemy e, Zone z, Rng rng)
        {
            var (na, nb) = CityLayout.Intersections(z);
            if (e.type == "boat")
            {
                // 船：水面上的两条航道（沿岸）
                float wb = z.WaterB, wd = z.h - wb;
                nb = new List<float> { wb + wd * 0.35f, wb + wd * 0.65f };
            }
            e.zone = z; e.netA = na; e.netB = nb;
            var okJ = new List<int>(); for (int j = 0; j < nb.Count; j++) if (Allowed(e, j)) okJ.Add(j);
            if (okJ.Count == 0) okJ.Add(0);
            int edge = rng.Int(0, 3), ni = na.Count;
            if (edge == 0) { e.i = 0; e.j = rng.Pick(okJ); }
            else if (edge == 1) { e.i = ni - 1; e.j = rng.Pick(okJ); }
            else if (edge == 2) { e.i = rng.Int(0, ni - 1); e.j = okJ[0]; }
            else { e.i = rng.Int(0, ni - 1); e.j = okJ[^1]; }
            e.ni = e.i; e.nj = e.j; e.prog = 1;
            e.pos = NodePos(e, e.i, e.j, 0.004f);
            e.up = Vector3.up; e.fwd = z.u;
        }

        static (int, int) NextNode(Enemy e, Rng rng, Vector2? goal)
        {
            var opts = new List<(int, int)>();
            int ni = e.netA.Count, nj = e.netB.Count;
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int i = e.i + di, j = e.j + dj;
                if (i < 0 || j < 0 || i >= ni || j >= nj || !Allowed(e, j)) continue;
                opts.Add((i, j));
            }
            if (opts.Count == 0) return (e.i, e.j);
            var noBack = opts.FindAll(o => !(o.Item1 == e.pi && o.Item2 == e.pj));
            var list = noBack.Count > 0 ? noBack : opts;
            if (goal.HasValue && rng.Chance(0.75f))
            {
                var best = list[0]; float bd = float.MaxValue;
                foreach (var o in list) { float d = Mathf.Abs(e.netA[o.Item1] - goal.Value.x) + Mathf.Abs(e.netB[o.Item2] - goal.Value.y); if (d < bd) { bd = d; best = o; } }
                return best;
            }
            return rng.Pick(list);
        }

        /// <summary>城市比例（CityBuilder.scale）：地面单位的车速、车道偏移、碰撞半径跟着城市缩小</summary>
        public static float Scale = 1;
        public static float R(Enemy e) { var T = Types[e.type]; return T.r * (T.fly ? 1 : Scale); }

        static void DriveRoad(Enemy e, float dt, float speed, Rng rng, Vector2? goal)
        {
            speed *= Scale;
            if (e.prog >= 1)
            {
                e.pi = e.i; e.pj = e.j; e.i = e.ni; e.j = e.nj;
                var (ni, nj) = NextNode(e, rng, goal);
                e.ni = ni; e.nj = nj; e.prog = 0;
                e.segLen = Mathf.Max(0.001f, Mathf.Abs(e.netA[ni] - e.netA[e.i]) + Mathf.Abs(e.netB[nj] - e.netB[e.j]));
            }
            if (e.ni == e.i && e.nj == e.j) { e.prog = 1; return; }
            e.prog = Mathf.Min(1, e.prog + speed * dt / e.segLen);
            e.a = Mathf.Lerp(e.netA[e.i], e.netA[e.ni], e.prog);
            e.b = Mathf.Lerp(e.netB[e.j], e.netB[e.nj], e.prog);
            // 靠右行驶（和车流同一侧），不压中线
            var dirAB = new Vector2(e.netA[e.ni] - e.netA[e.i], e.netB[e.nj] - e.netB[e.j]).normalized;
            var off = new Vector2(dirAB.y, -dirAB.x) * (e.type == "mech" ? 0 : 0.012f * Scale);   // 机甲走在路正中（它几乎和路一样宽，靠边走会蹭到楼）
            var p = e.zone.Point(e.a + off.x, e.b + off.y, 0.0004f);
            var d = p - e.pos;
            if (d.sqrMagnitude > 1e-10f) e.fwd = d.normalized;
            e.pos = p;
        }

        static Vector2 HeadGoal(Zone z, Vector3 head)
        {
            var d = head - z.origin;
            return new Vector2(Mathf.Clamp(Vector3.Dot(d, z.u), 0, z.w), Mathf.Clamp(Vector3.Dot(d, z.v), 0, z.h));
        }

        // ───────── 生成 ─────────
        public static Enemy Spawn(MonsterGame game, string type, Zone zone = null, int station = -1)
        {
            var T = Types[type];
            var rng = game.rng;
            var head = game.headPos;
            var e = new Enemy { id = eid++, type = type, hp = T.hp, maxHp = T.hp, station = station, cd = 1 + rng.Next() * 1.5f };
            if (T.ground)
            {
                var zones = new List<Zone>(game.city.zones);
                zones.RemoveAll(z => z.kind == ZoneKind.Cabinet && type != "sam" && type != "repair");   // 柜顶太小，只放防空导弹 / 维修车
                // 11-13 用户：水里怎么会有坦克 —— 地板上的河道城区几乎全是水，路网的节点落在水里：除了船，地面单位一律不进有水的城区；船只进有水的城区
                if (type != "boat") { zones.RemoveAll(z => z.harbor); if (zone != null && zone.harbor) zone = null; }
                if (zone != null && (T.onlyZone == null || ZoneIs(zone, T.onlyZone))) zones = new List<Zone> { zone };   // 指定城区也要守“船只在港口”这类限制
                else if (T.onlyZone != null) zones = zones.FindAll(z => ZoneIs(z, T.onlyZone)) is var f && f.Count > 0 ? f : game.city.zones.FindAll(z => ZoneIs(z, T.onlyZone));
                else if (zones.Count > 1 && rng.Chance(0.55f)) zones = zones.FindAll(z => z.kind == ZoneKind.Table) is var t2 && t2.Count > 0 ? t2 : zones;
                if (zones.Count == 0) return null;
                var zz = type == "mech" ? zones[0] : rng.Pick(zones);
                PlaceOnRoad(e, zz, rng);
                if (T.fixedPos) { e.i = e.ni = rng.Int(0, e.netA.Count - 1); e.j = e.nj = rng.Int(0, e.netB.Count - 1); e.prog = 1; e.pos = NodePos(e, e.i, e.j, 0.004f); }
                if (type == "mech")
                {
                    // 11-13：空降要有过程 —— 先预警（落点上一个靶标，MechWarn 秒），再从高处减速降下来（MechFall 秒），落地蹲一下站起来（MechRise 秒）才开打
                    e.lockTarget = NodePos(e, e.i, e.j, 0); e.dropT = MechWarn + MechFall; e.pos = e.lockTarget + Vector3.up * MechHigh;
                    game.Emit(new Ev { type = "mech_warn", id = e.id, pos = e.lockTarget });
                }
            }
            else
            {
                // 飞行单位从玩家身后远处飞来
                float ang = rng.Range(0, Mathf.PI * 2);
                e.pos = head + new Vector3(Mathf.Cos(ang) * 1.6f, rng.Range(0.1f, 0.5f), Mathf.Sin(ang) * 1.6f);
                e.orbitA = ang; e.orbitR = rng.Range(0.5f, 0.75f); e.orbitH = rng.Range(-0.15f, 0.12f);
                if (type == "bomber")
                {
                    var dir = new Vector3(-Mathf.Cos(ang), 0, -Mathf.Sin(ang)).normalized;
                    e.fwd = dir;
                    e.pos = head - dir * 1.8f + new Vector3(0, 0.42f, 0);
                }
                if (type == "drone") e.cd = 0;
            }
            game.enemies.Add(e);
            game.Emit(new Ev { type = "spawn", id = e.id, etype = type });
            return e;
        }

        // ───────── 每帧行为 ─────────
        public static void Tick(MonsterGame game, Enemy e, float dt)
        {
            var T = Types[e.type];
            var head = game.headPos;
            var rng = game.rng;
            e.t += dt; e.cd -= dt;
            if (e.type == "mech" && e.state == EState.Held) { TickMechHeld(game, e, dt); return; }
            if (e.state != EState.Move) return;
            if (game.Winning) { e.cd = Mathf.Max(e.cd, 9); e.burst = 0; e.charging = false; if (e.lockT > 0) { e.lockT = 0; e.cd = 9; } if (e.phase == "dive") e.phase = "leave"; }   // 胜利过程：全体停火（不再开火、取消导弹锁定、俯冲的拉起），等着撤离
            var aim = (head - e.pos).normalized; var p0 = e.pos;
            switch (e.type)
            {
                case "police":
                case "boat":
                    DriveRoad(e, dt, T.speed, rng, HeadGoal(e.zone, head));
                    e.aim = aim;
                    // 11-17 合常理：子弹不穿楼（见 MonsterGame.ShotStopped）—— 被楼 / 桌子挡着看不见玩家就先不开火，过一会儿再看
                    if (e.cd <= 0) { if (game.LineBlocked(Muzzle(e), head)) e.cd = HoldFire; else { e.cd = rng.Range(2.2f, 3.2f); Fire(game, e, "bullet", 1, 1); } }
                    break;
                case "apc":
                    DriveRoad(e, dt, T.speed, rng, HeadGoal(e.zone, head));
                    if (e.cd <= 0)
                    {
                        e.cd = 6.5f; e.hatch = 1;
                        for (int k = 0; k < 2; k++) { var d = Spawn(game, "drone"); if (d != null) { d.pos = e.pos + e.up * (0.03f + k * 0.01f); d.home = e.zone; } }
                    }
                    e.hatch = Mathf.Max(0, e.hatch - dt);
                    break;
                case "tank":
                    DriveRoad(e, dt, T.speed, rng, HeadGoal(e.zone, head));
                    e.aim = aim;
                    // 坦克：看不见玩家也先等；连着被挡 TankPatience 秒就不等了，直接轰挡路的楼（炮弹炸在楼上，见 MonsterGame.ShotStopped）
                    if (e.cd <= 0 && game.LineBlocked(Muzzle(e), head)) { e.blockT += HoldFire; if (e.blockT < TankPatience) e.cd = HoldFire; }
                    if (e.cd <= 0) { e.blockT = 0; e.cd = rng.Range(3, 3.8f); Fire(game, e, "shell", 7, 1); e.recoil = 1; }
                    e.recoil = Mathf.Max(0, e.recoil - dt * 3);
                    break;
                case "rocket":
                case "sam":
                    if (!T.fixedPos && (!e.parked || e.blockT > 0) && !(e.lockT > 0)) DriveRoad(e, dt, T.speed, rng, HeadGoal(e.zone, head));
                    e.aim = aim;
                    // 导弹：升架 → 红色预警线 → 发射；预警时锁定的是当时头的位置，躲开就打不中
                    // 11-17 合常理：导弹不穿楼。被楼 / 桌子挡着看不见玩家就先不锁定、接着往前开 —— 教学里停好的那辆（parked）也一样，开到看得见玩家再停；
                    // 柜顶上固定的防空车挪不了，照常发射（导弹打在挡路的楼上会炸，不伤发射车）
                    if (e.cd <= 0 && !(e.lockT > 0) && !T.fixedPos) { bool blocked = game.LineBlocked(e.muzzle ?? Muzzle(e), head); e.blockT = blocked ? 1 : 0; if (blocked) e.cd = HoldFire; }
                    if (e.cd <= 0 && !(e.lockT > 0)) { e.lockT = e.parked ? 1.6f : 3; e.lockTarget = head; game.Emit(new Ev { type = "missile_lock", id = e.id }); }
                    if (e.lockT > 0)
                    {
                        e.lockT -= dt;
                        e.launcher = Mathf.Min(1, e.launcher + dt * 1.5f);
                        if (e.lockT <= 0) { FireMissile(game, e); e.cd = T.fixedPos ? 7 : rng.Range(5.5f, 7); }
                    }
                    else e.launcher = Mathf.Max(0, e.launcher - dt);
                    break;
                case "heli":
                    {
                        e.orbitA += dt * 0.35f;
                        var gf = game.headFwd;
                        float bas = Mathf.Atan2(gf.z, gf.x);
                        float ang = bas + Mathf.Sin(e.orbitA) * 1.0f;
                        var target = head + new Vector3(Mathf.Cos(ang) * e.orbitR, e.orbitH, Mathf.Sin(ang) * e.orbitR);
                        FlyToward(e, target, dt, T.speed, false);
                        e.aim = aim;
                        if (e.cd <= 0) { if (game.LineBlocked(Muzzle(e), head)) e.cd = HoldFire; else { e.cd = rng.Range(3, 4); e.burst = 4; } }
                        if (e.burst > 0) { e.burstCd -= dt; if (e.burstCd <= 0) { e.burstCd = 0.12f; e.burst--; Fire(game, e, "bullet", 1, 1); } }
                        break;
                    }
                case "drone":
                    FlyToward(e, head, dt, T.speed, true);
                    // 11-17 合常理：无人机从街上的装甲车里起飞，以前笔直朝玩家飞、直接穿过挡在前面的楼。现在前面有楼就先贴着楼往上爬，过了楼顶再朝玩家飞
                    if (game.AnyBuildingAt(e.pos, T.r * 0.6f)) { e.pos = p0 + Vector3.up * (T.speed * dt); e.vel = Vector3.up * T.speed; }
                    if (Vector3.Distance(e.pos, head) < Cfg.HeadR) { game.Hurt(3, e.pos, "drone"); Kill(game, e, "self"); }
                    break;
                case "jet":
                    if (e.phase == null) e.phase = "circle";
                    if (e.phase == "circle")
                    {
                        e.orbitA += dt * 0.9f;
                        var target = head + new Vector3(Mathf.Cos(e.orbitA) * 1.3f, 0.45f, Mathf.Sin(e.orbitA) * 1.3f);
                        FlyToward(e, target, dt, T.speed * 0.7f, false);
                        if (e.cd <= 0) { e.phase = "dive"; e.diveTarget = head; e.cd = 99; game.Emit(new Ev { type = "jet_dive", id = e.id }); }
                    }
                    else if (e.phase == "dive")
                    {
                        var d = e.diveTarget - e.pos;
                        e.fwd = (e.fwd * 0.85f + d.normalized * 0.15f).normalized;
                        e.pos += e.fwd * T.speed * dt;
                        if (Vector3.Distance(e.pos, head) < Cfg.HeadR + 0.02f) { game.Hurt(12, e.pos, "jet"); e.phase = "leave"; }
                        if (Vector3.Dot(e.pos - e.diveTarget, e.fwd) > 0.05f) e.phase = "leave";
                    }
                    else
                    {
                        e.pos += e.fwd * T.speed * dt;
                        e.fwd = (e.fwd + new Vector3(0, dt * 0.8f, 0)).normalized;
                        if (Vector3.Distance(e.pos, head) > 1.5f)
                        {
                            e.passes++;
                            if (e.passes >= 3) { e.dead = true; e.state = EState.Dead; game.Emit(new Ev { type = "leave", id = e.id }); }
                            else { e.phase = "circle"; e.cd = rng.Range(1.5f, 3); e.orbitA = Mathf.Atan2(e.pos.z - head.z, e.pos.x - head.x); }
                        }
                    }
                    break;
                case "bomber":
                    {
                        e.pos += e.fwd * T.speed * dt;
                        float flat = new Vector2(e.pos.x - head.x, e.pos.z - head.z).magnitude;
                        if (flat < 0.35f && e.cd <= 0) { e.cd = 0.18f; game.projectiles.Add(new Projectile { kind = "bomb", pos = e.pos + new Vector3(0, -0.02f, 0), vel = e.fwd * 0.15f, dmg = 8, from = e.id }); }
                        if (e.t > 14) { e.dead = true; e.state = EState.Dead; game.Emit(new Ev { type = "leave", id = e.id }); }
                        break;
                    }
                case "mech": TickMech(game, e, dt); break;
                case "repair": TickRepair(game, e, dt); break;
            }
            if (T.fly && e.state == EState.Move) KeepClear(game, e, p0, dt);
        }

        // 11-13 用户：旁边有柜子时飞机不要穿进柜子 —— 飞行单位以前只管朝玩家头部附近飞，完全不看桌子 / 柜子和上面的楼。
        // 现在每帧查“现在的位置”和“前方 0.5 秒处”的净空高度（MonsterGame.Clearance），比它低就平滑拉高（像贴着地形飞），已经在家具范围里的直接顶上去。
        // 离玩家头部水平 0.3m 以内不管（那里是玩家自己站 / 坐的地方，无人机和俯冲的战斗机要能够到头）
        public const float HoldFire = 0.35f, TankPatience = 2.5f;   // 被挡住时隔多久再看一次；坦克连着被挡多久就直接开炮
        public static bool NoClear;   // 体检用：关掉避让，对比“不避让时会钻进去多少次”
        static void KeepClear(MonsterGame game, Enemy e, Vector3 p0, float dt)
        {
            if (NoClear) return;
            var hd = game.headPos;
            if (new Vector2(e.pos.x - hd.x, e.pos.z - hd.z).sqrMagnitude < 0.09f) return;
            float r = Types[e.type].r;
            var step = e.pos - p0; step.y = 0;
            var ahead = e.pos + step / Mathf.Max(dt, 1e-4f) * 0.5f;
            float here = game.Clearance(e.pos, r, e.home);
            float need = Mathf.Max(here, game.Clearance(ahead, r, e.home)) + 0.02f;
            if (e.pos.y < need) e.pos.y = Mathf.MoveTowards(Mathf.Lerp(e.pos.y, need, 1 - Mathf.Exp(-dt * 8)), need, dt * 0.2f);
            if (e.pos.y < here) e.pos.y = here;
        }

        static void FlyToward(Enemy e, Vector3 target, float dt, float speed, bool direct)
        {
            var d = target - e.pos;
            float l = d.magnitude;
            if (l < 1e-4f) return;
            var want = d.normalized * Mathf.Min(speed, l * 3);
            e.vel = direct ? want : e.vel * 0.92f + want * 0.08f;
            e.pos += e.vel * dt;
            if (e.vel.magnitude > 0.02f) e.fwd = new Vector3(e.vel.x, e.vel.y * 0.3f, e.vel.z).normalized;
        }

        static Vector3 Muzzle(Enemy e) => e.pos + e.up * ((e.type == "tank" ? 0.03f : 0.02f) * VScale);

        // 子弹 / 炮弹：直线飞向头（略散布），可以侧身躲、可以挥手拍掉
        static void Fire(MonsterGame game, Enemy e, string kind, float dmg, int n)
        {
            var from = Muzzle(e);
            var head = game.headPos;
            var rng = game.rng;
            for (int k = 0; k < n; k++)
            {
                float spread = kind == "bullet" ? 0.05f : 0.02f;
                var target = head + new Vector3((rng.Next() - 0.5f) * spread, (rng.Next() - 0.5f) * spread - 0.03f, (rng.Next() - 0.5f) * spread);
                float speed = kind == "bullet" ? 0.95f : 0.75f;   // 11-12 用户：看不到自己是怎么掉血的 —— 子弹 / 炮弹放慢（看得见、来得及拍开），表现层加粗加拖尾
                game.projectiles.Add(new Projectile { kind = kind, pos = from, vel = (target - from).normalized * speed, dmg = dmg, from = e.id });
            }
            game.Emit(new Ev { type = "fire", id = e.id, kind = kind });
        }

        static void FireMissile(MonsterGame game, Enemy e)
        {
            // 11-10：从管口沿发射箱方向射出（先往上冲，再拐向锁定点），不再从车身中间冒出来
            var from = e.muzzle ?? e.pos + e.up * 0.04f;
            var dir = e.muzzle.HasValue && e.muzzleDir.sqrMagnitude > 0.5f ? e.muzzleDir : (e.lockTarget - from).normalized;
            game.projectiles.Add(new Projectile { kind = "missile", pos = from, vel = dir * 0.6f, target = e.lockTarget, dmg = 14, from = e.id });
            game.Emit(new Ev { type = "fire", id = e.id, kind = "missile" });
        }

        // 机甲：空降到市中心，举盾格挡；要用扔过去的载具、或者把它推进楼里撞出破绽，才打得动。
        // 11-13 用户：最后的机器人效果太差 —— 每个动作都补上过程：
        //   空降：预警 → 减速下降 → 落地冲击（蹲）→ 站起来举盾；
        //   开炮：站定 → 抬炮蓄力 MechCharge 秒（炮口聚能，看得见、躲得开）→ 开火（后坐）→ 再迈步；被打出破绽时蓄力中断；
        //   击毁：先失控冒火花、跪下（MechDie 秒，表现层演），最后才是大爆炸（见 MonsterGame.HitEnemy）
        public const float MechWarn = 1.6f, MechFall = 1.8f, MechHigh = 1.15f, MechRise = 1.0f, MechCharge = 1.1f, MechDie = 2.3f, MechHold = 3.2f, MechHop = 1.7f;
        // 11-17 用户：按常理，机甲也应该能捏起来、扔下去掉几格血（一切交互都要符合常理）。原来机甲是唯一抓不动的东西。现在：
        //   · 落地站稳以后随时能抓（捏 / 握住它）。被攥着的时候它手脚乱蹬、不能开炮；攥到 MechHold 秒它挣脱（电一下你的手，掉 4 点血），掉回地上；
        //   · 扔出去：落地 −2 格，路上撞到楼再 −1（楼被撞塌）、摔到桌子外面的地板上再 −1；只是松手让它掉下去：−1 格；
        //   · 摔下去以后瘫 2.6 秒（这时候拍它是打在破绽上），然后喷气跳回自己的路上接着打；血掉光就地爆炸。
        static void TickMechHeld(MonsterGame game, Enemy e, float dt)
        {
            e.heldT += dt; e.charging = false; e.charge = 0; e.stagger = 0; e.guard = 0; e.knock = Vector3.zero; e.recoil = 0;
            e.aim = (game.headPos - e.pos).normalized;
            if (e.heldT < MechHold) return;
            string holder = null; foreach (var kv in game.held) if (kv.Value.enemy == e) holder = kv.Key;
            game.Emit(new Ev { type = "mech_break", id = e.id, pos = e.pos });
            game.Hurt(4, e.pos, "mech");
            if (holder != null) game.Release(holder, Vector3.zero, null);
            else { e.state = EState.Move; e.air = true; e.airThrown = false; e.airHit = false; e.vel = Vector3.zero; e.heldT = 0; }
        }
        static void TickMech(MonsterGame game, Enemy e, float dt)
        {
            var head = game.headPos;
            e.aim = (head - e.pos).normalized;
            if (e.dropT > 0)
            {
                e.dropT -= dt;
                if (!e.fallTold && e.dropT <= MechFall) { e.fallTold = true; game.Emit(new Ev { type = "mech_fall", id = e.id, pos = e.lockTarget }); }
                float k = Mathf.Clamp01(e.dropT / MechFall);                // 1 → 0，越接近地面越慢（推进器减速）
                e.pos = e.lockTarget + Vector3.up * (MechHigh * k * k);
                if (e.dropT <= 0)
                {
                    e.pos = e.lockTarget; e.landT = 0; e.cd = 2.6f;
                    game.Blast(e.pos, 0.12f, 2, "mech"); game.Emit(new Ev { type = "mech_land", id = e.id, pos = e.pos });
                }
                return;
            }
            if (e.air)
            {
                // 在空中：抛物线；撞到楼（只算一次）把楼撞塌、自己减速；落到所在位置的桌面 / 柜面，不在任何城区上方就一直掉到地板
                e.vel += new Vector3(0, Cfg.Gravity, 0) * dt; e.pos += e.vel * dt;
                var c = e.pos + Vector3.up * 0.04f;
                // 飞行 / 下落途中进到一栋楼的范围里：普通楼和没护盾的电站被它整栋撞塌（它落在瓦砾上，不会嵌在楼里）；
                // 封印塔撞不穿 —— 没护盾的从撞到的那层往上塌，机甲被弹到塔外
                foreach (var b in game.city.buildings)
                {
                    if (b.h <= 0 || !MonsterGame.InsideBuilding(b, c, 0.02f)) continue;
                    var flat = new Vector3(e.vel.x, 0, e.vel.z); Vector3? dir = flat.sqrMagnitude > 1e-4f ? flat.normalized : (Vector3?)null;
                    if (b.kind == BuildingKind.Tower)
                    {
                        if (!e.airHit) { if (game.IsShielded(b)) game.Emit(new Ev { type = "shield_hit", id = b.id, pos = c }); else game.ToppleAt(b, b.StoreyAt(Mathf.Max(0, c.y - b.Base.y)), dir, "mech"); }
                        var outv = e.pos - b.Base; outv.y = 0; outv = outv.sqrMagnitude > 1e-6f ? outv.normalized : Vector3.right;
                        var np = b.Base + outv * (b.Foot * 0.5f + 0.05f); e.pos = new Vector3(np.x, e.pos.y, np.z);
                        e.vel = new Vector3(outv.x * 0.15f, Mathf.Min(e.vel.y, 0), outv.z * 0.15f);
                    }
                    else
                    {
                        game.ToppleAt(b, 0, dir, "mech");
                        e.vel = new Vector3(e.vel.x * 0.3f, Mathf.Min(e.vel.y, 0), e.vel.z * 0.3f);
                    }
                    e.airHit = true;
                    break;
                }
                float gy = 0; bool onZone = false;
                foreach (var z in game.city.zones)
                {
                    var d = e.pos - z.origin; float a = Vector3.Dot(d, z.u), b2 = Vector3.Dot(d, z.v);
                    if (a >= -0.03f && a <= z.w + 0.03f && b2 >= -0.03f && b2 <= z.h + 0.03f && z.origin.y >= gy && e.pos.y > z.origin.y - 0.06f) { gy = z.origin.y; onZone = true; }
                }
                if (e.pos.y > gy || e.vel.y > 0) return;
                e.air = false; e.pos = new Vector3(e.pos.x, gy, e.pos.z); e.vel = Vector3.zero;
                int dmg = (e.airThrown ? 2 : 1) + (e.airHit ? 1 : 0) + (onZone ? 0 : 1);
                e.hp -= dmg; e.stagger = 2.6f; e.guard = 0; e.charging = false; e.cd = Mathf.Max(e.cd, 3f); e.hopPending = true;
                game.Emit(new Ev { type = "mech_slam", id = e.id, pos = e.pos, n = dmg, hp = e.hp });
                game.Emit(new Ev { type = "mech_hurt", id = e.id, hp = e.hp });
                if (e.hp <= 0) game.KillMech(e, "thrown");
                return;
            }
            if (e.hop > 0)
            {
                // 喷气跳回自己的路上（一道弧线，1.3 秒），落地蹲一下
                // 先几乎垂直升到比这片城区最高的楼还高，平移过去，再垂直落下（不从楼里穿过去）
                e.hop += dt / MechHop; float k = Mathf.Clamp01(e.hop), sk = k * k * k * (k * (6 * k - 15) + 10);
                float top = 0.12f; foreach (var b in game.city.buildings) if (b.zone == e.zone && b.h > 0) top = Mathf.Max(top, b.TopH(b.h));
                float arc = top + 0.07f + Mathf.Max(0, e.hopFrom.y - e.lockTarget.y);
                float up = 1 - Mathf.Pow(Mathf.Abs(2 * k - 1), 4);
                e.pos = Vector3.Lerp(e.hopFrom, e.lockTarget, sk) + Vector3.up * (up * arc);
                if (k >= 1) { e.hop = 0; e.pos = e.lockTarget; e.landT = MechRise - 0.45f; e.cd = Mathf.Max(e.cd, 2f); game.Emit(new Ev { type = "mech_land", id = e.id, pos = e.pos, n = 0.5f }); }
                return;
            }
            if (e.landT < MechRise) { e.landT += dt; return; }               // 落地：蹲着缓冲 → 站起来
            e.stagger = Mathf.Max(0, e.stagger - dt);
            if (e.hopPending && e.stagger <= 0)
            {
                e.hopPending = false; e.hop = 0.0001f; e.hopFrom = e.pos;
                e.lockTarget = e.a > -8 ? e.zone.Point(e.a, e.b, 0) : NodePos(e, e.i, e.j, 0);
                game.Emit(new Ev { type = "mech_fall", id = e.id, pos = e.lockTarget });
                return;
            }
            e.guard = e.stagger > 0 ? 0 : 1;
            e.recoil = Mathf.Max(0, e.recoil - dt * 1.6f);
            if (e.knock.magnitude > 0.01f)
            {
                e.pos += e.knock * dt;
                e.knock *= Mathf.Pow(0.05f, dt);
                foreach (var b in game.city.buildings)
                {
                    if (b.zone != e.zone || b.h <= 0 || b.kind == BuildingKind.Tower) continue;
                    if (Vector3.Distance(b.Base, e.pos) < b.Foot * 0.5f + 0.03f)
                    {
                        game.ToppleAt(b, 0, e.knock.normalized, "mech");
                        e.stagger = 3; e.knock = Vector3.zero;
                        game.Emit(new Ev { type = "mech_stagger", id = e.id });
                        break;
                    }
                }
            }
            else if (e.stagger <= 0 && !e.charging && !e.hopPending) DriveRoad(e, dt, Types["mech"].speed, game.rng, HeadGoal(e.zone, head));
            if (e.stagger > 0)
            {
                if (e.charging) { e.charging = false; e.cd = Mathf.Max(e.cd, 2.5f); game.Emit(new Ev { type = "mech_fizzle", id = e.id }); }   // 被打出破绽：蓄力中断
            }
            else
            {
                if (!e.charging && e.cd <= MechCharge && !game.Winning) { e.charging = true; game.Emit(new Ev { type = "mech_charge", id = e.id }); }
                if (e.charging && e.cd <= 0)
                {
                    var from = e.muzzle ?? e.pos + e.up * 0.09f;             // 炮口（表现层每帧写）
                    var target = head + new Vector3(0, -0.03f, 0);
                    game.projectiles.Add(new Projectile { kind = "fist", pos = from, vel = (target - from).normalized * 0.55f, dmg = 15, from = e.id });
                    e.cd = 5; e.charging = false; e.recoil = 1;
                    game.Emit(new Ev { type = "fire", id = e.id, kind = "fist", pos = from });
                }
            }
            e.charge = e.charging ? Mathf.Clamp01(1 - e.cd / MechCharge) : 0;
        }

        // 维修车：开到停机的电站，待满 RepairTime 秒修好
        static void TickRepair(MonsterGame game, Enemy e, float dt)
        {
            var st = game.city.byId.TryGetValue(e.station, out var s0) ? s0 : null;
            if (st == null || st.online || st.h > 0 && st.online) { e.dead = true; e.state = EState.Dead; game.Emit(new Ev { type = "leave", id = e.id }); return; }
            var goal = new Vector2(st.a, st.b);
            float dGoal = Mathf.Abs(e.a - st.a) + Mathf.Abs(e.b - st.b);
            if (dGoal > e.zone.cell * 0.9f && !e.offroad) { DriveRoad(e, dt, Types["repair"].speed, game.rng, goal); return; }
            e.offroad = true;
            var target = e.zone.Point(st.a, st.b, 0.0004f);
            var d = target - e.pos;
            if (d.magnitude > 0.045f) { e.fwd = d.normalized; e.pos += e.fwd * Mathf.Min(d.magnitude, Types["repair"].speed * dt); return; }
            e.working = true;
            st.repair += dt;
            if (st.repair >= Cfg.RepairTime) game.RepairStation(st, e);
        }

        public static void Kill(MonsterGame game, Enemy e, string cause)
        {
            // 击毁地面载具也可能掉回血球（落在它原地上方）；飞行的不掉（够不着）
            if (!e.dead && Types.TryGetValue(e.type, out var kt) && !kt.fly && e.type != "repair" && e.type != "boat") game.MaybeOrbAt(new Vector3(e.pos.x, e.zone != null ? e.zone.origin.y + 0.075f * Scale / 0.7f : e.pos.y + 0.05f, e.pos.z), 0.06f);
            if (e.dead) return;
            e.dead = true; e.state = EState.Dead;
            if (e.type == "repair" && game.city.byId.TryGetValue(e.station, out var st)) st.repair = 0;
            game.Emit(new Ev { type = "enemy_dead", id = e.id, etype = e.type, cause = cause, pos = e.pos });
            if (cause != "self") game.Gain("vehicle");
        }
    }
}
