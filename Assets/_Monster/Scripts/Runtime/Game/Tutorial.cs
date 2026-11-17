// “玩法”（玩中学，11-10 用户：教学别太长、别一步一步来，让玩家自己探索，做软交互，但玩法要讲清楚）。
// 不计时、不会死（伤害打四折、体力最低 30），所有招式一开始就能用、顺序随意：
//   · 招式清单（6 项）：做到哪项勾哪项（拍楼 / 横推 / 抓车扔 / 拔楼 / 挡导弹 / 能量波）；
//   · 主线目标链常驻：盯住封印塔 → 拍掉同色电站 → 拆塔（当前这一环亮）；
//   · 软提示：只在相关时机冒出来（手靠近车、导弹飞来、怒气满、刚看过塔……），10 秒没动静才用箭头轻轻指一个没试过的；
//   · 追兵慢慢登场：开局一辆警车 → 拍几栋楼后来直升机 → 再来一辆导弹车；怒气中途自动充满，让玩家试能量波。
// 塔全拆掉就结束（结算只有“开始第一夜 / 回到标题”）。
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Monster.Game
{
    public class TutCtx { public int retry, miss; }

    /// <summary>兼容旧接口：玩法进行中 Step 非空</summary>
    public class TutStep { public int goal; }

    public class Tutorial
    {
        public class Move { public string key, name, how; public bool done; public float doneT = -9; }

        readonly MonsterGame g;
        public readonly List<Move> moves = new()
        {
            new Move { key = "smash", name = "拍楼", how = "手掌往下拍楼" },
            new Move { key = "topple", name = "横推", how = "横着扫过高楼上半截，让它倒向别的楼" },
            new Move { key = "throw", name = "扔车", how = "捏住路上的车，朝楼甩手松开" },
            new Move { key = "uproot", name = "拔楼", how = "一只手环住楼身，往里一收，整栋拿起来" },
            new Move { key = "parry", name = "挡导弹", how = "导弹飞来：拍开，或捏住扔回去" },
            new Move { key = "wave", name = "能量波", how = "怒气满了：双手张开、掌根并拢往前推" },
        };
        public bool allowWin = true, early; public float t, passT; public int i;
        public TutCtx c = new();
        public int Version { get; private set; }   // 牌面要重写时 +1
        public string Hint { get; private set; } = "";
        public Vector3? Focus { get; private set; }
        public Enemy car, heli, rocket;
        bool scanned, heliSent, rocketSent, rageGiven; float idle, carWait = 3f, lastAct;
        bool orbTold, pauseTold; float pauseT;   // 11-12：回血球、抱拳暂停各提示一次
        int smashes;
        static readonly TutStep Dummy = new();

        public Tutorial(MonsterGame g) { this.g = g; }
        public void Begin() { Version++; }

        public TutStep Step => g.result == null ? Dummy : null;
        public int Count => moves.Count;
        public int DoneCount => moves.Count(m => m.done);
        public string Title => Loc.T("玩法");
        public string Text => Hint;
        public string Sub => "";

        // ── 主线：盯塔 → 拍电站 → 拆塔 ──
        Building TargetTower => g.city.towers.Where(x => x.h > 0).OrderBy(x => g.IsShielded(x) ? 1 : 0).ThenBy(x => Vector3.Distance(x.Base, g.headPos)).FirstOrDefault();
        /// <summary>0 = 盯塔，1 = 拍电站，2 = 拆塔，3 = 全拆完</summary>
        public int Chain
        {
            get
            {
                var tw = TargetTower; if (tw == null) return 3;
                if (!g.IsShielded(tw)) return 2;
                return scanned ? 1 : 0;
            }
        }

        void Tick(string key)
        {
            var m = moves.FirstOrDefault(x => x.key == key);
            if (m == null || m.done) return;
            m.done = true; m.doneT = t; passT = 1.2f; Version++;
            g.Emit(new Ev { type = "tut_pass", i = moves.IndexOf(m) });
        }

        public void OnEvent(Ev ev)
        {
            if (ev.type == "tut_pass" || ev.type == "tut_step") return;
            switch (ev.type)
            {
                case "collapse":
                    lastAct = t;
                    if (ev.cause == "smash" && !ev.dir.HasValue) { smashes++; Tick("smash"); }
                    if (ev.dir.HasValue && ev.from - ev.to >= 2 && ev.cause != "uproot") Tick("topple");
                    if (ev.cause == "smash" && ev.dir.HasValue) smashes++;
                    break;
                case "throw": lastAct = t; Tick("throw"); break;
                case "uproot": lastAct = t; Tick("uproot"); break;
                case "parry": if (ev.kind == "missile") { lastAct = t; Tick("parry"); } break;
                case "grab": lastAct = t; if (ev.kind == "missile") Tick("parry"); break;
                case "breath_start": lastAct = t; Tick("wave"); break;
                case "scan": lastAct = t; if (!scanned) { scanned = true; Version++; } break;
                case "orb_take": orbTold = true; Version++; break;
                case "armor_hit": case "station_on": lastAct = t; Version++; break;
                case "station_off": case "shield_down": case "tower_down": lastAct = t; Version++; break;
            }
        }

        static bool Alive(Enemy e) => e != null && !e.dead && (e.state == EState.Move || e.state == EState.Held);
        Zone Table => g.city.zones.FirstOrDefault(z => z.kind == ZoneKind.Table) ?? g.city.zones[0];

        public void Update(float dt)
        {
            if (g.result != null) return;
            t += dt; passT = Mathf.Max(0, passT - dt);
            var hands = g.lastHands;
            if (hands != null && hands.Any(h => h != null && h.present && h.vel.magnitude > 0.25f)) idle = 0; else idle += dt;

            // ── 追兵慢慢登场 ──
            if (!Alive(car) && !g.held.Values.Any(h => h.kind == "enemy"))
            {
                carWait -= dt;
                if (carWait <= 0 && (!moves[2].done || t < 60)) { car = Enemies.Spawn(g, "police", Table); if (car != null) car.cd = 1e9f; carWait = moves[2].done ? 9 : 3; }
            }
            if (!heliSent && (smashes >= 3 || t > 40)) { heliSent = true; if (g.stars < 1) g.AddHeat(Mathf.Max(0, 12 - g.heat)); heli = Enemies.Spawn(g, "heli"); }
            if (!rocketSent && heliSent && (t > 65 || (heli != null && heli.dead && t > 30)))
            {
                rocketSent = true; rocket = Enemies.Spawn(g, "rocket", Table);
                if (rocket != null) { rocket.parked = true; rocket.cd = 1.5f; }
            }
            // 导弹车被拆了、挡导弹还没学会：过一会儿再来一辆
            if (rocketSent && !moves[4].done && !Alive(rocket)) { rocket = Enemies.Spawn(g, "rocket", Table); if (rocket != null) { rocket.parked = true; rocket.cd = 2.5f; } }
            // 怒气：玩了一阵自动充满一次；能量波没学会、用完了就再给
            if (!moves[5].done && g.breathing <= 0 && g.rage < Cfg.RageMax && (t > 50 || DoneCount >= 3) && (!rageGiven || g.rage <= 1)) { rageGiven = true; g.rage = Cfg.RageMax; Version++; }
            // 楼不够拍 / 推 / 拔：挑最近的废墟重新长出来（不锁楼）
            if (Tall(g, 3) < 2) Ensure(g, 2, 3);

            // ── 软提示：只说眼下最相关的一句 ──
            string hint; Vector3? focus = null;
            var missile = g.projectiles.FirstOrDefault(p => p.kind == "missile" && p.heldBy == null);
            var nearCar = hands == null ? null : g.enemies.FirstOrDefault(e => Alive(e) && e.state == EState.Move && !Enemies.Types[e.type].fly && hands.Any(h => h != null && h.present && Vector3.Distance(h.pos, e.pos) < 0.12f));
            var tw = TargetTower;
            if (missile != null && !moves[4].done) { hint = "导弹来了：拍开，或捏住它扔回去"; }
            else if (rocket != null && Alive(rocket) && rocket.lockT > 0 && !moves[4].done) { hint = "导弹车要发射了：准备拍开导弹"; focus = rocket.pos + Vector3.up * 0.1f; }
            else if (g.orbs.Count > 0 && !orbTold) { hint = "绿色的生命球：捏住它（或用手碰一下）就能回血"; focus = g.orbs[0].pos + Vector3.up * 0.07f; }
            else if (nearCar != null && !moves[2].done) hint = "捏住这辆车，朝楼甩出去";
            else if (g.rage >= Cfg.RageMax && !moves[5].done) hint = "怒气满了：双手张开、掌根并拢，往前推出能量波";
            else if (g.waveWant) hint = "手势对了！怒气还没满：多拍楼、扔车攒怒气";
            else if (passT > 0) hint = "\u0001" + Loc.T("学会了：") + Loc.T(moves.Where(m => m.done).OrderBy(m => m.doneT).Last().name) + (Loc.En ? "!" : "！");
            else
            {
                switch (Chain)
                {
                    case 0: hint = "盯住封印塔看一秒，看它连着哪座电站"; if (idle > 6) focus = Above(tw, 0.12f); break;
                    case 1:
                        {
                            // 11-12：电站的护盾、重启在玩法关里就教（原来只在第一夜的简报纸上写了一行）
                            var st = g.city.stations.FirstOrDefault(s => s.towerId == tw.id && s.online && s.h > 0);
                            hint = st != null && st.armor > 1 ? "电站有护盾：先拍碎护盾，再拍一下才会毁" : "顺着亮起的线，拍掉同色的发电站";
                            if (idle > 6) focus = Above(st, 0.11f); break;
                        }
                    case 2:
                        hint = g.city.stations.Any(s => s.towerId == tw.id && s.h <= 0 && s.restart > 0) ? "护盾没了！电站过一会儿会重启：趁现在把塔拆掉" : "护盾没了：把封印塔拆掉！";
                        focus = Above(tw, 0.12f); break;
                    default: hint = "城市拆光了！"; break;
                }
                // 抱拳暂停：玩了半分钟、学会两招以后提示一次（显示 7 秒）
                if (!pauseTold && t > 30 && DoneCount >= 2 && Chain != 2) { pauseTold = true; pauseT = 7; }
                if (pauseT > 0) { pauseT -= dt; hint = "想暂停：捏一下城市近角的暂停钮"; focus = null; }
                // 很久没动：轻轻指一个还没试过的招式
                if (idle > 10 && focus == null)
                {
                    var next = moves.FirstOrDefault(m => !m.done);
                    if (next != null)
                    {
                        hint = next.how;
                        if (next.key == "throw" && Alive(car)) focus = car.pos + Vector3.up * 0.06f;
                        else if (next.key is "smash" or "topple" or "uproot") focus = Above(NearestHouse(g, 3));
                    }
                }
            }
            hint = hint.StartsWith("\u0001") ? hint.Substring(1) : Loc.T(hint);   // \u0001 开头 = 已经拼好的译文
            if (hint != Hint) { Hint = hint; Version++; }
            Focus = focus;
        }

        // ───────── 工具 ─────────
        static Building NearestHouse(MonsterGame g, int minH = 1)
        {
            Building best = null; float bd = float.MaxValue;
            foreach (var b in g.city.buildings)
            {
                if (b.kind != BuildingKind.House || b.h < minH || b.zone.kind != ZoneKind.Table || b.style == Style.Gas) continue;
                float d = Vector3.Distance(b.Base, g.headPos);
                if (d < bd) { bd = d; best = b; }
            }
            return best;
        }
        static int Tall(MonsterGame g, int minH) => g.city.buildings.Count(b => b.kind == BuildingKind.House && b.zone.kind == ZoneKind.Table && b.style != Style.Gas && b.h >= minH);
        static void Ensure(MonsterGame g, int need, int minH)
        {
            int have = Tall(g, minH);
            if (have >= need) return;
            foreach (var b in g.city.buildings.Where(b => b.kind == BuildingKind.House && b.zone.kind == ZoneKind.Table && b.style != Style.Gas && b.h < minH && b.maxH >= minH && !g.held.Values.Any(h => h.chunk != null && h.chunk.from == b.id))
                                                .OrderBy(b => Vector3.Distance(b.Base, g.headPos)).Take(need - have).ToList())
                g.Regrow(b);
        }
        static Vector3? Above(Building b, float k = 0.05f) => b == null ? null : MonsterGame.TopOf(b) + Vector3.up * k;
    }
}
