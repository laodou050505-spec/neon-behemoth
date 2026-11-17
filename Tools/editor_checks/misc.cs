// 其它常理项的验证。参数：station（把一座电站打到只剩一格护甲，再让右手环握它）/ crush（在一栋楼旁边的路上放一辆坦克和一辆警车，把楼朝它们推倒）/ state
var a = System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim().Split(' ');
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; g.sealMax = 99999; v.paused = false; Time.timeScale = 1f;
if (a[0] == "station")
{
    Monster.Building st = null; foreach (var s in g.city.stations) if (s.h > 0 && s.zone == g.city.zones[0]) { st = s; break; }
    st.armor = 1; bool ok = g.Uproot("right", st);
    System.IO.File.WriteAllText("/tmp/monster_game/misc_id.txt", st.id.ToString());
    if (ok) g.HoldAt("right", st.Base + Vector3.up * 0.2f);
    result.Log("电站 #" + st.id + " 护甲 1 → 拿起来 ok=" + ok + " h=" + st.h + " online=" + st.online + " restart=" + st.restart.ToString("F0") + " 手里 " + g.held.Count); return;
}
if (a[0] == "crush")
{
    // 找一对“高楼 + 正好在它倒下范围里的地面追兵”（追兵在自己的路上，不挪它），把楼朝它推倒
    for (int n = 0; n < 8; n++) Monster.Game.Enemies.Spawn(g, n % 2 == 0 ? "tank" : "police", g.city.zones[0]);
    foreach (var b in g.city.buildings)
    {
        if (b.kind != Monster.BuildingKind.House || b.h < 8 || b.h != b.maxH) continue;
        float L = b.TopH(b.h);
        foreach (var d in new[] { b.zone.u, -b.zone.u, b.zone.v, -b.zone.v })
            foreach (var e in g.enemies)
            {
                if (e.dead || e.zone != b.zone || Monster.Game.Enemies.Types[e.type].fly || e.type == "mech") continue;
                var rel = e.pos - b.Base; rel.y = 0; float al = Vector3.Dot(rel, d);
                if (al < b.Foot * 0.5f + 0.01f || al > L * 0.7f || (rel - d * al).magnitude > b.Foot * 0.3f) continue;
                System.IO.File.WriteAllText("/tmp/monster_game/misc_id.txt", e.id.ToString());
                g.ToppleAt(b, 0, d, "smash");
                result.Log("推倒 " + b.style + " " + b.maxH + " 层（高 " + (L * 100).ToString("F0") + "cm），倒向 " + e.type + " #" + e.id + "（离楼脚 " + (al * 100).ToString("F0") + "cm）"); return;
            }
    }
    result.Log("这一刻没有追兵正好在高楼旁边，再试一次"); return;
}
var sb = new System.Text.StringBuilder();
foreach (var q in System.IO.File.ReadAllText("/tmp/monster_game/misc_id.txt").Trim().Split(' '))
{
    int id = int.Parse(q); Monster.Game.Enemy e = null; foreach (var x in g.enemies) if (x.id == id) e = x;
    sb.Append(" #" + id + (e == null ? " 已不在（被砸毁）" : " " + e.type + " dead=" + e.dead + " hp=" + e.hp));
}
result.Log("state:" + sb + " | 手里 " + g.held.Count + " kills=" + g.kills);
