// 11-17 “弹药不穿楼 / 无人机不穿楼”的纯逻辑体检（非播放模式跑，和 sim.cs 一样只建逻辑城市）。参数 /tmp/monster_game/arg.txt：关号 [头高，默认 1.6] [第 ③ 项跑多少秒，默认 60]
// ① LineBlocked 和“每 3mm 取一个点硬查”对账；② 在高楼背后朝玩家开一炮，看炮弹停在楼上还是穿过去；
// ③ 满星放着不管跑 60 秒：各类开火 / 打在楼上 / 打中玩家的次数，无人机和其它飞行单位钻进楼里的帧数，有没有一直被挡着、一枪没开的追兵
var a = System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim().Split(' ');
int li = int.Parse(a[0]); float headY = a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 1.6f;
const float K = 0.7f;
System.Func<Vector2, bool> inside = p => Mathf.Abs(p.x) < 1.3f && p.y > -1.3f && p.y < 1.3f;
var head = new Vector3(0, headY, 0);
Monster.Game.Enemies.Scale = K;
var lv = Monster.Game.Level.All[li];
System.Func<int, Monster.Game.MonsterGame> make = seed =>
{
    var zones = Monster.CityLayout.FitRoom(inside, Vector2.zero, headY, false, 0);
    if (lv.sides <= 0) zones.RemoveAll(z => z.kind == Monster.ZoneKind.Cabinet && z.furniture); else Monster.CityLayout.AddSides(zones, inside, Vector3.zero, lv.sides);
    var design = new System.Collections.Generic.List<Monster.Zone>();
    foreach (var z in zones) design.Add(new Monster.Zone(z.id, z.kind, z.origin, z.u, z.v, z.w / K, z.h / K, z.harbor) { furniture = z.furniture });
    var c = Monster.CityLayout.Build(design, lv.Spec, 7 + li * 17);
    foreach (var z in c.zones) { z.w *= K; z.h *= K; z.cell *= K; }
    foreach (var b in c.buildings) { b.a *= K; b.b *= K; b.fw *= K; b.fd *= K; b.lotW *= K; b.lotD *= K; b.modH *= K; }
    return new Monster.Game.MonsterGame(lv, c, seed) { S = K, sealMax = 99999 };
};
var gi = new Monster.Game.GameInput { hasHead = true, headPos = head, headFwd = Vector3.forward, hasGaze = true, gaze = new Ray(head, Vector3.forward), hands = new System.Collections.Generic.List<Monster.Game.HandIn>() };
var sb = new System.Text.StringBuilder();

// ① 对账
var g = make(4242); var city = g.city; g.spawnCd = 1e9f; g.Tick(0.011f, gi); g.DrainEvents();
sb.Append("第" + li + "区 头高 " + headY + "：城区 " + city.zones.Count + " 片，楼 " + city.buildings.Count + " 栋\n");
var rnd = new System.Random(7); int nSeg = 0, nBlocked = 0, mism = 0;
for (int s = 0; s < 220; s++)
{
    var z = city.zones[rnd.Next(city.zones.Count)];
    var from = z.Point((float)rnd.NextDouble() * z.w, (float)rnd.NextDouble() * z.h, 0.012f);
    Monster.Building skip = null; foreach (var b in city.buildings) if (b.h > 0 && Monster.Game.MonsterGame.InsideBuilding(b, from, 0.002f)) { skip = b; break; }
    bool brute = false; int steps = Mathf.CeilToInt(Vector3.Distance(from, head) / 0.003f);
    for (int k = 0; k <= steps && !brute; k++)
    {
        var p = Vector3.Lerp(from, head, k / (float)steps);
        foreach (var b in city.buildings) if (b.h > 0 && b != skip && Monster.Game.MonsterGame.InsideBuilding(b, p, 0)) { brute = true; break; }
        if (!brute) foreach (var zz in city.zones)
        {
            if (zz.kind == Monster.ZoneKind.Floor) continue;
            var d = p - zz.origin; float m = zz.furniture ? 0.09f : 0.045f, aa = Vector3.Dot(d, zz.u), bb = Vector3.Dot(d, zz.v);
            if (aa > -m && aa < zz.w + m && bb > -m && bb < zz.h + m && d.y < -0.004f && d.y > -3) { brute = true; break; }
        }
    }
    bool fast = g.LineBlocked(from, head);
    nSeg++; if (fast) nBlocked++; if (fast != brute) mism++;
}
sb.Append("① 视线对账：" + nSeg + " 条，被挡 " + nBlocked + " 条，和逐点硬查不一致 " + mism + " 条\n");

// ② 高楼背后开炮
int tested = 0, stop = 0, reach = 0, chips = 0, falls = 0;
foreach (var b in city.buildings.ToArray())
{
    if (tested >= 40) break;
    if (b.kind != Monster.BuildingKind.House || b.h < 6) continue;
    var toHead = head - b.Base; toHead.y = 0; toHead.Normalize();
    var from = b.Base - toHead * (b.Foot * 0.5f + 0.03f) + Vector3.up * 0.012f;
    if (g.AnyBuildingAt(from, 0.003f) || !g.LineBlocked(from, head)) continue;
    tested++;
    var p = new Monster.Game.Projectile { kind = "shell", pos = from, vel = (head - from).normalized * 0.75f, dmg = 7, from = -1 };
    g.projectiles.Add(p);
    for (int t = 0; t < 500 && g.projectiles.Contains(p); t++)
    {
        g.Tick(1 / 90f, gi); g.hp = 100;
        foreach (var ev in g.DrainEvents()) { if (ev.type == "chip") chips++; if (ev.type == "collapse" && ev.cause == "shot") falls++; }
    }
    if (Vector3.Distance(p.pos, head) < 0.4f) reach++; else stop++;
}
sb.Append("② 高楼背后开炮 " + tested + " 发：停在楼上 " + stop + "，穿过去打到玩家 " + reach + "；崩碎块 " + chips + " 次，打塌 / 打断 " + falls + " 次；怒气 " + g.rage.ToString("F1") + "（应为 0）\n");

// ③ 满星放 60 秒
var g2 = make(99); g2.Tick(0.016f, gi); if (li > 0) g2.AddHeat(300);   // 玩法关（0）按它自己的节奏出兵
var cnt = new System.Collections.Generic.Dictionary<string, int>();
System.Action<string> inc = k => { cnt.TryGetValue(k, out int v); cnt[k] = v + 1; };
var born = new System.Collections.Generic.Dictionary<int, float>(); var fires = new System.Collections.Generic.Dictionary<int, int>(); var types = new System.Collections.Generic.Dictionary<int, string>();
int droneT = 0, droneIn = 0, flyT = 0, flyIn = 0, droneN = 0;
int T3 = a.Length > 2 ? int.Parse(a[2]) * 60 : 3600;
for (int t = 0; t < T3; t++)
{
    g2.Tick(1 / 60f, gi); g2.hp = 100; g2.result = null;
    foreach (var ev in g2.DrainEvents())
    {
        if (ev.type == "fire") { inc("开火 " + ev.kind); fires.TryGetValue(ev.id, out int f); fires[ev.id] = f + 1; }
        else if (ev.type == "ricochet") inc("子弹打在楼 / 桌上");
        else if (ev.type == "blast" && ev.cause == "shot") inc("炮弹炸在楼 / 桌上");
        else if (ev.type == "blast" && ev.cause == "missile") inc("导弹爆炸（楼上或飞过头）");
        else if (ev.type == "collapse" && ev.cause == "shot") inc("楼被炮火打塌");
        else if (ev.type == "chip") inc("楼被崩出碎块");
        else if (ev.type == "hurt") inc("玩家挨打 " + ev.cause);
    }
    foreach (var e in g2.enemies)
    {
        if (!born.ContainsKey(e.id)) { born[e.id] = g2.time; types[e.id] = e.type; if (e.type == "drone") droneN++; }
        if (e.dead || e.state != Monster.Game.EState.Move || !Monster.Game.Enemies.Types[e.type].fly) continue;
        bool inB = g2.AnyBuildingAt(e.pos, 0);
        if (e.type == "drone") { droneT++; if (inB) droneIn++; } else { flyT++; if (inB) flyIn++; }
    }
}
foreach (var kv in cnt) sb.Append("   " + kv.Key + "：" + kv.Value + "\n");
sb.Append("③ 无人机 " + droneN + " 架，飞行 " + droneT + " 帧里在楼里 " + droneIn + " 帧；其它飞行单位 " + flyT + " 帧里在楼里 " + flyIn + " 帧；星级 " + g2.stars + "\n");
int silent = 0; var sl = new System.Text.StringBuilder();
foreach (var kv in born)
{
    string ty = types[kv.Key]; if (ty != "police" && ty != "tank" && ty != "boat" && ty != "heli" && ty != "rocket" && ty != "sam") continue;
    Monster.Game.Enemy en = null; foreach (var e in g2.enemies) if (e.id == kv.Key) en = e;
    if (en == null || en.dead || g2.time - kv.Value < 25) continue;
    fires.TryGetValue(kv.Key, out int f); if (f == 0) { silent++; sl.Append(" " + ty + "#" + kv.Key + "(" + (g2.time - kv.Value).ToString("F0") + "s," + en.zone?.kind + ")"); }
}
sb.Append("   活了 25 秒以上却一枪没开的：" + silent + sl + "\n");
// ④ 无人机：主城区放 6 辆装甲车，跑 40 秒，看无人机有没有钻进楼里、最后到没到玩家面前
var g3 = make(5); g3.Tick(0.016f, gi); g3.spawnCd = 1e9f;
for (int n = 0; n < 6; n++) Monster.Game.Enemies.Spawn(g3, "apc", g3.city.zones[0]);
int d3 = 0, d3in = 0, d3n = 0, d3hit = 0; var seen = new System.Collections.Generic.HashSet<int>(); float maxLife = 0; var bornD = new System.Collections.Generic.Dictionary<int, float>();
for (int t = 0; t < 2400; t++)
{
    g3.Tick(1 / 60f, gi); g3.hp = 100; g3.result = null;
    foreach (var ev in g3.DrainEvents()) if (ev.type == "hurt" && ev.cause == "drone") d3hit++;
    foreach (var e in g3.enemies)
    {
        if (e.type != "drone") continue;
        if (seen.Add(e.id)) { d3n++; bornD[e.id] = g3.time; }
        if (e.dead || e.state != Monster.Game.EState.Move) continue;
        d3++; if (g3.AnyBuildingAt(e.pos, 0)) d3in++; maxLife = Mathf.Max(maxLife, g3.time - bornD[e.id]);
    }
}
sb.Append("④ 无人机 " + d3n + " 架：飞行 " + d3 + " 帧里在楼里 " + d3in + " 帧；撞到玩家 " + d3hit + " 架；飞得最久的一架 " + maxLife.ToString("F1") + " 秒\n");
result.Log(sb.ToString());
