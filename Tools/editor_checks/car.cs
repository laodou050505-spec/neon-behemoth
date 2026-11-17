// 车相关的验证。参数：count / topple（把车最多的那条街旁边的一栋楼朝街上推倒）/ shot 名字（对着上次推倒的位置截图）
var a = System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim().Split(' ');
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; g.sealMax = 99999;
int n = 0, moving = 0, hidden = 0; var cars = new System.Collections.Generic.List<Vector3>();
foreach (var tr in v.builder.root.GetComponentsInChildren<Monster.Traffic>(true)) foreach (var L in tr.lanes) foreach (var c in L.cars) { if (c.hidden) { hidden++; continue; } n++; if (c.v > 0.0005f) moving++; cars.Add(c.t.position); }
int flat = 0, ram = 0; foreach (Transform t in v.builder.root.Find("GameFx")) { if (t.name == "FlatCar") flat++; if (t.name == "RammedCar") ram++; }
int ground = 0; foreach (var e in g.enemies) if (!e.dead && Monster.Game.Enemies.Types[e.type].ground) ground++;
string st = "t=" + g.time.ToString("F1") + " 路上 " + n + " 辆（在动 " + moving + "），已摘走 " + hidden + "；压扁的 " + flat + "，被顶飞的 " + ram + "；地面追兵 " + ground + " Halt=" + Monster.Traffic.Halt;
if (a[0] == "topple")
{
    Monster.Building best = null; Vector3 bestDir = Vector3.forward; int bestN = 0;
    foreach (var b in g.city.buildings)
    {
        if (b.kind != Monster.BuildingKind.House || b.h < 5 || b.h != b.maxH) continue;
        foreach (var d in new[] { b.zone.u, -b.zone.u, b.zone.v, -b.zone.v })
        {
            int k = 0; float len = b.Height;
            foreach (var cp in cars) { var rel = cp - b.Base; rel.y = 0; float along = Vector3.Dot(rel, d); if (along > b.Foot * 0.5f && along < len && (rel - d * along).magnitude < b.Foot * 0.5f) k++; }
            if (k > bestN) { bestN = k; best = b; bestDir = d; }
        }
    }
    if (best == null) { result.Log("没找到会砸到车的楼 | " + st); return; }
    System.IO.File.WriteAllText("/tmp/monster_game/carpos.txt", best.Base.x + " " + best.Base.y + " " + best.Base.z + " " + bestDir.x + " " + bestDir.z + " " + best.Height);
    g.ToppleAt(best, 0, bestDir, "smash");
    result.Log("推倒 " + best.style + " " + best.maxH + " 层，倒向的那条路上有 " + bestN + " 辆车 | " + st);
    return;
}
if (a[0] == "shot")
{
    var q = System.IO.File.ReadAllText("/tmp/monster_game/carpos.txt").Trim().Split(' '); var c0 = new Vector3(float.Parse(q[0]), float.Parse(q[1]), float.Parse(q[2])); var d = new Vector3(float.Parse(q[3]), 0, float.Parse(q[4])); float H = float.Parse(q[5]);
    var mid = c0 + d * (H * 0.5f); var side = Vector3.Cross(Vector3.up, d); var pos = mid + side * 0.2f + Vector3.up * 0.22f - d * 0.05f;
    var ov = GameObject.Find("HurtOverlay"); if (ov) ov.SetActive(false);
    Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/" + a[1] + ".png", pos, Quaternion.LookRotation(mid - pos).eulerAngles, 55, 1280, 900);
}
result.Log(st);
