var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; Monster.Game.Enemy m = null; foreach (var e in g.enemies) if (e.type == "mech" && !e.dead) m = e;
if (m == null) { result.Log("no mech"); return; }
var go = GameObject.Find("Enemy_mech_" + m.id); Bounds b = default; bool any = false;
if (go) foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
// 最近的楼：机甲包围盒和楼的占地的水平间距
float best = 9; string who = "";
foreach (var bd in g.city.buildings) { if (bd.h <= 0 || bd.zone != m.zone) continue; var d = bd.Base - m.pos; float da = Mathf.Abs(Vector3.Dot(d, bd.zone.u)) - bd.fw / 2, db = Mathf.Abs(Vector3.Dot(d, bd.zone.v)) - bd.fd / 2; float gap = Mathf.Max(da, db); if (gap < best) { best = gap; who = bd.style + "#" + bd.id + " fw=" + bd.fw.ToString("F3") + " fd=" + bd.fd.ToString("F3"); } }
result.Log("pos=" + m.pos.ToString("F3") + " fwd=" + m.fwd.ToString("F2") + " hp=" + m.hp + " guard=" + m.guard + " stagger=" + m.stagger.ToString("F1") + " cd=" + m.cd.ToString("F1") + " size=" + b.size.ToString("F3") + " 最近楼边到机甲中心 " + (best * 100).ToString("F1") + "cm " + who + " cell=" + m.zone.cell);
