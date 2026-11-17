// 播放模式：在主城区找一栋高楼，从它背后朝玩家打一发（arg.txt：shell / bullet / fist / missile），时间放慢到 arg 第二项（默认 0.015）好截图；state = 看那栋楼和弹药现在的样子
// 弹着点和建议的机位写到 /tmp/monster_game/block_pos.txt（“楼id 弹着点xyz 侧向xyz 朝玩家方向xyz”）
var a = System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim().Split(' ');
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.sealMax = 99999; g.hp = 100; v.paused = false;
foreach (var pm in Object.FindObjectsByType<Monster.Game.PalmMenu>()) pm.enabled = false;
foreach (var ho in Object.FindObjectsByType<Monster.Game.HurtOverlay>()) { ho.enabled = false; foreach (Transform c in ho.transform) c.gameObject.SetActive(false); }
if (a[0] == "state")
{
    var q = System.IO.File.ReadAllText("/tmp/monster_game/block_pos.txt").Split(' '); int id = int.Parse(q[0]);
    var sbd = g.city.byId[id];
    result.Log("楼 #" + id + " " + sbd.style + " h=" + sbd.h + "/" + sbd.maxH + " 累计伤害=" + sbd.dmg + " 着火=" + (sbd.fire > 0) + " | 场上弹药 " + g.projectiles.Count + " 追兵 " + g.enemies.Count + " 怒气 " + g.rage.ToString("F1") + " timeScale=" + Time.timeScale); return;
}
var head = g.headPos; Monster.Building best = null; Vector3 from = default, toHead = default;
foreach (var b in g.city.buildings)
{
    if (b.kind != Monster.BuildingKind.House || b.h < 7 || b.h != b.maxH || b.zone != g.city.zones[0] || b.dmg > 0) continue;
    toHead = head - b.Base; toHead.y = 0; toHead.Normalize();
    var f = b.Base - toHead * (b.Foot * 0.5f + 0.04f) + Vector3.up * 0.012f;
    if (g.AnyBuildingAt(f, 0.004f) || !g.LineBlocked(f, head)) continue;
    best = b; from = f; break;
}
if (best == null) { result.Log("没找到合适的高楼"); return; }
var dir = (head - from).normalized; int n = a[0] == "bullet" ? 4 : 1;
for (int k = 0; k < n; k++)
    g.projectiles.Add(new Monster.Game.Projectile { kind = a[0], pos = from - dir * 0.012f * k + Vector3.Cross(Vector3.up, toHead) * 0.006f * (k - n / 2), vel = dir * (a[0] == "bullet" ? 0.95f : 0.75f), target = head, dmg = 1, from = -1 });
Time.timeScale = a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 0.015f;
var at = from + dir * 0.045f; var side = Vector3.Cross(Vector3.up, toHead);
string F(Vector3 p) => p.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " " + p.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " " + p.z.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
System.IO.File.WriteAllText("/tmp/monster_game/block_pos.txt", best.id + " " + F(at) + " " + F(side) + " " + F(toHead));
result.Log("朝 #" + best.id + " " + best.style + "（" + best.h + " 层）背后打 " + n + " 发 " + a[0] + "，离墙 4cm；时间倍率 " + Time.timeScale);
