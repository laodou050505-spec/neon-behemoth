// 难度模拟（11-12）：纯逻辑、不建模型。机器人按“断电 → 拆塔”的最优路线打，三档水平只差在出手间隔和挡弹成功率。
// 城市按真机的做法生成（设计尺寸 = 真实 ÷ 0.7，生成后整体乘 0.7），房间 = 2.6m 见方的安全区 + 虚拟桌。
// 参数走 /tmp/monster_game/sim.txt：“每格跑几局”
// sim.txt：第一个数 = 每格跑几局；后面每关一组 “伤害系数:护甲:重启秒:回血量:核打击时限”（后两项可省；整组省略就用代码里的值；写 “-” = 这一关不跑）
var cfg = System.IO.File.ReadAllText("/tmp/monster_game/sim.txt").Trim().Split(' ');
int N = int.Parse(cfg[0]); float orbHeal = 15;
float F(string x) => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture);
const float K = 0.7f;
System.Func<Vector2, bool> inside = p => Mathf.Abs(p.x) < 1.3f && p.y > -1.3f && p.y < 1.3f;
var head = new Vector3(0, 1.6f, 0);
var skills = new (string name, float act, float pb, float ps, float pm)[] { ("新手", 3.0f, 0.2f, 0.3f, 0.45f), ("一般", 2.0f, 0.45f, 0.55f, 0.7f), ("熟练", 1.2f, 0.7f, 0.8f, 0.9f) };
var sb = new System.Text.StringBuilder();
Monster.Game.Enemies.Scale = K;
for (int li = 1; li < Monster.Game.Level.All.Length; li++)
{
    var lv = Monster.Game.Level.All[li];
    if (cfg.Length > li && cfg[li] == "-") continue;   // “-” = 这一关不跑
    if (cfg.Length > li) { var q = cfg[li].Split(':'); lv.dmg = F(q[0]); lv.armor = int.Parse(q[1]); lv.restart = F(q[2]); if (q.Length > 3) orbHeal = F(q[3]); if (q.Length > 4) lv.seal = F(q[4]); }   // 第 5 项 = 核打击时限
    sb.Append("\n第" + li + "区 伤×" + lv.dmg + " 甲" + lv.armor + " 重启" + lv.restart + "s 核" + lv.seal + "s | ");
    foreach (var sk in skills)
    {
        int win = 0, loseHp = 0, loseSeal = 0, orbsMade = 0; float tSum = 0, hpSum = 0; int towersN = 0, stationsN = 0;
        for (int run = 0; run < N; run++)
        {
            var zones = Monster.CityLayout.FitRoom(inside, Vector2.zero, 1.6f, false, 0);
            if (lv.sides <= 0) zones.RemoveAll(z => z.kind == Monster.ZoneKind.Cabinet && z.furniture); else Monster.CityLayout.AddSides(zones, inside, Vector3.zero, lv.sides);
            var design = new System.Collections.Generic.List<Monster.Zone>();
            foreach (var z in zones) design.Add(new Monster.Zone(z.id, z.kind, z.origin, z.u, z.v, z.w / K, z.h / K, z.harbor) { furniture = z.furniture });
            var city = Monster.CityLayout.Build(design, lv.Spec, 7 + li * 17);
            foreach (var z in city.zones) { z.w *= K; z.h *= K; z.cell *= K; }
            foreach (var b in city.buildings) { b.a *= K; b.b *= K; b.fw *= K; b.fd *= K; b.lotW *= K; b.lotD *= K; b.modH *= K; }
            var g = new Monster.Game.MonsterGame(lv, city, 100 + run * 13 + li) { S = K };
            towersN = city.towers.Count; stationsN = city.stations.Count;
            var rnd = new System.Random(run * 31 + li * 7 + (int)(sk.act * 10));
            float next = 2.5f; Monster.Zone lastZone = city.zones[0];
            var gi = new Monster.Game.GameInput { hasHead = true, headPos = head, headFwd = Vector3.forward, hasGaze = true, gaze = new Ray(head, Vector3.forward), hands = new System.Collections.Generic.List<Monster.Game.HandIn>() };
            for (int tick = 0; tick < 6000 && g.result == null; tick++)
            {
                Monster.Building tw = null; foreach (var t in city.towers) if (t.h > 0) { tw = t; break; }
                if (tw != null) gi.gaze = new Ray(head, (Monster.Game.MonsterGame.TopOf(tw) - head).normalized);
                g.Tick(0.05f, gi);
                g.DrainEvents();
                // 挡弹：飞到 0.45m 内时掷一次骰子
                for (int i = g.projectiles.Count - 1; i >= 0; i--)
                {
                    var p = g.projectiles[i];
                    if (p.warned || Vector3.Distance(p.pos, head) > 0.45f) continue;
                    p.warned = true;
                    float pr = p.kind == "bullet" ? sk.pb : p.kind == "missile" ? sk.pm : sk.ps;
                    if (rnd.NextDouble() < pr) g.Parry(i, -p.vel);
                }
                if (g.result != null || g.time < next || tw == null) continue;
                float cost = sk.act; Monster.Zone zoneNow = lastZone;
                Monster.Building house = null; foreach (var hb in city.buildings) if (hb.kind == Monster.BuildingKind.House && hb.h > 0 && hb.h <= 5 && hb.zone == lastZone) { house = hb; break; }
                Monster.Game.Enemy rep = null, shooter = null;
                foreach (var e in g.enemies) { if (e.dead || e.state != Monster.Game.EState.Move) continue; if (e.type == "repair") rep = e; else if (e.type != "mech" && shooter == null) shooter = e; }
                if (g.hp < 75 && g.orbs.Count > 0 && g.orbs[0].age > 0.4f) { var o = g.orbs[0]; g.orbs.RemoveAt(0); g.Heal(orbHeal); cost *= 0.8f; orbsMade++; }
                else if (rep != null) Monster.Game.Enemies.Kill(g, rep, "smash");
                else if (shooter != null && g.enemies.Count >= 2 && rnd.NextDouble() < 0.35) Monster.Game.Enemies.Kill(g, shooter, "smash");
                else if (g.IsShielded(tw))
                {
                    Monster.Building st = null;
                    foreach (var s in city.stations) if (s.towerId == tw.id && s.h > 0 && s.online) { st = s; break; }
                    if (st != null) { st.hitCd = 0; g.ToppleAt(st, 0, null, "smash"); zoneNow = st.zone; }
                }
                else { tw.hitCd = 0; g.ToppleAt(tw, Mathf.Max(0, tw.h - 2), null, "smash"); zoneNow = tw.zone; }
                if (zoneNow != lastZone) { cost += 1.5f; lastZone = zoneNow; }
                next = g.time + cost * (0.8f + (float)rnd.NextDouble() * 0.4f);
            }
            if (g.result == "win") { win++; tSum += g.time; hpSum += g.hp; } else if (g.result == "hp") loseHp++; else loseSeal++;
        }
        sb.Append(sk.name + " 胜" + (win * 100 / N) + "%" + (win > 0 ? " 用时" + (tSum / win).ToString("F0") + "s 剩血" + (hpSum / win).ToString("F0") : "") + " (血尽" + loseHp + "/超时" + loseSeal + ") 吃球" + (orbsMade / (float)N).ToString("F1") + " 塔" + towersN + "站" + stationsN + " | ");
    }
}
result.Log(sb.ToString());
