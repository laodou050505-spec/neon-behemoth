// 楼顶塌落验收：/tmp/monster_game/tt.txt = "pick 楼型" | "hit 层数" | "shot 名" | "ts 倍率"
var a = System.IO.File.ReadAllText("/tmp/monster_game/tt.txt").Trim().Split(' ');
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game;
int id = System.IO.File.Exists("/tmp/monster_game/tt_id.txt") ? int.Parse(System.IO.File.ReadAllText("/tmp/monster_game/tt_id.txt").Trim()) : -1;
if (a[0] == "pick") { Monster.Building pb = null; foreach (var x in g.city.buildings) if (x.kind == Monster.BuildingKind.House && x.h == x.maxH && x.style.ToString() == a[1] && x.zone.kind == Monster.ZoneKind.Table && x.fire <= 0 && (pb == null || x.h > pb.h)) pb = x; if (pb == null) { result.Log("no " + a[1]); return; } id = pb.id; System.IO.File.WriteAllText("/tmp/monster_game/tt_id.txt", id.ToString()); }
var b = g.city.byId[id];
if (a[0] == "ts") Time.timeScale = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
if (a[0] == "hit") { b.hitCd = 0; g.ToppleAt(b, b.h - int.Parse(a[1]), null, "smash"); }
if (a[0] == "shot")
{
    // 机位：从玩家那一侧的斜上方看楼顶（俯 40°），再加一张贴着楼顶高度的水平侧视（从玩家一侧，沿 −v 看过去），都离楼 0.3m 以上
    var top = b.Base + Vector3.up * b.TopH(b.h); var zv = b.zone.v; var zu = b.zone.u;
    var p1 = top + zv * 0.2f + zu * 0.08f + Vector3.up * 0.2f;
    Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/top/" + a[1] + "_hi.png", p1, Quaternion.LookRotation(top + Vector3.up * 0.01f - p1).eulerAngles, 30, 1280, 900);
    var p2 = top + zv * 0.45f + Vector3.up * 0.03f;
    Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/top/" + a[1] + "_side.png", p2, Quaternion.LookRotation(top + Vector3.up * 0.005f - p2).eulerAngles, 16, 1280, 900);
}
result.Log(a[0] + ": b=" + b.id + " " + b.style + " h=" + b.h + "/" + b.maxH + " realTop=" + (b.TopH(b.h) * 1000).ToString("F0") + "mm avgTop=" + (b.h * b.modH * 1000).ToString("F0") + "mm fire=" + b.fire.ToString("F1"));
