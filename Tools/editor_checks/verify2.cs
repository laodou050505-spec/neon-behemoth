// 时间冻结下一次做完再拍：① 受伤数字 ② 拍扁主桌上的一辆车
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; var z = g.city.zones[0]; Time.timeScale = 0f;
var head = new Vector3(0, 1.6f, 0); Camera.main.transform.SetPositionAndRotation(head, Quaternion.Euler(35, 0, 0));
g.headPos = head;
var src = new Vector3(0.25f, 1.25f, 0.6f); g.Hurt(8, src, "test");
foreach (var ev in g.DrainEvents()) v.Handle(ev);
string pows = ""; foreach (var t in Object.FindObjectsByType<TMPro.TextMeshPro>()) if (t.transform.parent && t.transform.parent.name == "PowText") { t.transform.parent.localScale = Vector3.one * 1.2f; t.transform.parent.rotation = Quaternion.LookRotation(t.transform.parent.position - head); pows += t.text + "@" + t.transform.parent.position.ToString("F2") + " d=" + Vector3.Distance(head, t.transform.parent.position).ToString("F2") + " "; }
Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/l4/hurt2.png", head, Quaternion.LookRotation(src - head).eulerAngles, 75, 1280, 900);
Monster.Traffic tr = null; foreach (var x in Object.FindObjectsByType<Monster.Traffic>()) if (x.transform.IsChildOf(v.builder.root.Find(z.id))) tr = x;
Monster.Traffic.Car best = null; foreach (var L in tr.lanes) foreach (var c in L.cars) if (!c.hidden && c.t.localScale.x > 0.5f && (best == null || Vector3.Distance(c.t.position, z.Center) < Vector3.Distance(best.t.position, z.Center))) best = c;
var target = best.t.position; float len = best.len * tr.scale;
var p = target + new Vector3(0.04f, 0.14f, -0.16f);
Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/l4/car_before.png", p, Quaternion.LookRotation(target - p).eulerAngles, 40, 1280, 900);
int n = g.CrushProp(target + Vector3.up * 0.01f, 0.045f);
Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/l4/car_after.png", p, Quaternion.LookRotation(target - p).eulerAngles, 40, 1280, 900);
result.Log("hurt hp=" + g.hp + " pow=[" + pows + "] | car len=" + (len * 100).ToString("F1") + "cm crushed=" + n);
