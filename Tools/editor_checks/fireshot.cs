// 固定文本：编号从 /tmp/monster_game/fshot.txt 读。第一次调用点火，之后每次冻结时间拍近景 + 站立视角
var n = System.IO.File.ReadAllText("/tmp/monster_game/fshot.txt").Trim();
foreach (var pm in Object.FindObjectsByType<Monster.Game.PalmMenu>(FindObjectsSortMode.None)) pm.enabled = false;
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; v.paused = false;
Monster.Building f = null;
foreach (var b in g.city.buildings) if (b.fire > 0 && b.h > 0 && b.zone.kind == Monster.ZoneKind.Table && (f == null || b.h > f.h)) f = b;
if (f == null) { foreach (var b in g.city.buildings) if (b.zone.kind == Monster.ZoneKind.Table && b.kind == Monster.BuildingKind.House && b.h >= 5 && (f == null || Mathf.Abs(b.h - 7) < Mathf.Abs(f.h - 7))) f = b; f.fire = 0.5f; Time.timeScale = 0.25f; result.Log("ignite " + f.id + " h=" + f.h); return; }
Time.timeScale = 0f;
var top = Monster.Game.MonsterGame.TopOf(f);
Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/fire/near_" + n + ".png", top + new Vector3(0.1f, 0.1f, -0.2f), Quaternion.LookRotation(top + Vector3.up * 0.03f - (top + new Vector3(0.1f, 0.1f, -0.2f))).eulerAngles, 40, 1280, 800);
if (n == "1") Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/fire/stand_" + n + ".png", new Vector3(0, 1.6f, 0), Quaternion.LookRotation(top - new Vector3(0, 1.6f, 0)).eulerAngles, 45, 1280, 800);
Time.timeScale = 0.25f;
result.Log("fire shot " + n + " b=" + f.id + " fire=" + f.fire.ToString("F1") + " h=" + f.h);
