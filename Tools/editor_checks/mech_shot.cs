// 参数（/tmp/monster_game/arg.txt）：名字 水平方位角(度) 距离 高度 视场角
var a = System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim().Split(' ');
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; Monster.Game.Enemy m = null; foreach (var e in g.enemies) if (e.type == "mech") m = e;
bool at = a.Length > 9 && a[9] == "at"; Vector3 c = m != null && !at ? m.pos : new Vector3(float.Parse(a[5]), float.Parse(a[6]), float.Parse(a[7]));
if (m != null && !at) System.IO.File.WriteAllText("/tmp/monster_game/mechpos.txt", m.pos.x.ToString("F3") + " " + m.pos.y.ToString("F3") + " " + m.pos.z.ToString("F3"));
float az = float.Parse(a[1]) * Mathf.Deg2Rad, d = float.Parse(a[2]), h = float.Parse(a[3]);
var p = c + new Vector3(Mathf.Sin(az) * d, h, -Mathf.Cos(az) * d); var t = c + Vector3.up * float.Parse(a[8]);
Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/" + a[0] + ".png", p, Quaternion.LookRotation(t - p).eulerAngles, float.Parse(a[4]), 1100, 900);
result.Log(a[0] + (m != null ? " dropT=" + m.dropT.ToString("F2") + " landT=" + m.landT.ToString("F2") + " cd=" + m.cd.ToString("F2") + " charging=" + m.charging + " hp=" + m.hp + " stagger=" + m.stagger.ToString("F1") + " pos=" + m.pos.ToString("F3") : " (no mech)"));
