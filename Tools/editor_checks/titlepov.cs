// 标题页：从头的位置看招牌 + 菜单纸。arg2.txt：“截图名 [俯仰角，正 = 往下] [fov] [往前走几米] [头高]”
var a = System.IO.File.ReadAllText("/tmp/monster_game/arg2.txt").Trim().Split(' ');
float P(int i, float d) => a.Length > i ? float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture) : d;
var sign = GameObject.Find("TitleSign"); if (!sign) { result.Log("没有招牌"); return; }
var fwd = sign.transform.forward; var head = sign.transform.position - fwd * 0.73f; head.y = sign.transform.position.y + P(4, 1.6f);
var p = head + fwd * P(3, 0);
var rot = Quaternion.LookRotation(fwd) * Quaternion.Euler(P(1, 0), 0, 0);
foreach (var pm in Object.FindObjectsByType<Monster.Game.PalmMenu>()) pm.enabled = false;
Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/brand/" + a[0] + ".png", p, rot.eulerAngles, P(2, 80), 1600, 1100);
var pl = sign.transform.Find("Body/MakerPlate");
result.Log("shot " + a[0] + " 铭牌=" + (pl ? (pl.position.y - sign.transform.position.y).ToString("F3") + "m 高" : "无") + " 眼高 " + (head.y - sign.transform.position.y).ToString("F2"));
