// 拍 block_inj.cs 的弹着点。arg2.txt：“截图名 侧向距离 高度 往后距离 [fov]”（相对弹着点；“往后”= 背着玩家的方向，炮弹来的那一侧）
var a = System.IO.File.ReadAllText("/tmp/monster_game/arg2.txt").Trim().Split(' ');
var q = System.IO.File.ReadAllText("/tmp/monster_game/block_pos.txt").Trim().Split(' ');
float P(string x) => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture);
var at = new Vector3(P(q[1]), P(q[2]), P(q[3])); var side = new Vector3(P(q[4]), P(q[5]), P(q[6])); var toHead = new Vector3(P(q[7]), P(q[8]), P(q[9]));
foreach (var ho in Object.FindObjectsByType<Monster.Game.HurtOverlay>()) { ho.enabled = false; foreach (Transform c in ho.transform) c.gameObject.SetActive(false); }
var p = at + side * P(a[1]) + Vector3.up * P(a[2]) - toHead * P(a[3]);
var e = Quaternion.LookRotation(at + Vector3.up * 0.02f - p).eulerAngles;
Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/b1117/" + a[0] + ".png", p, e, a.Length > 4 ? P(a[4]) : 45, 1280, 800);
var g = Object.FindAnyObjectByType<Monster.Game.GameView>().game; g.hp = 100;
result.Log("shot " + a[0] + " t=" + g.time.ToString("F2") + " 弹药 " + g.projectiles.Count);
