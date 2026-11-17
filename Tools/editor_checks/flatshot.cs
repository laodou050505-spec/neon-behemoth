// 手拍一辆停着的车（压扁），从近处截图
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; g.sealMax = 99999;
var ov = GameObject.Find("HurtOverlay"); if (ov) ov.SetActive(false);
Vector3 best = Vector3.zero; bool found = false; Vector3 other = Vector3.zero; float bd = 9;
var all = new System.Collections.Generic.List<Vector3>();
foreach (var tr in v.builder.root.GetComponentsInChildren<Monster.Traffic>(true)) { if (tr.vmax < Monster.Traffic.Vmax * 0.9f) continue; foreach (var L in tr.lanes) foreach (var c in L.cars) if (!c.hidden && c.t.localScale.x > 0.5f) all.Add(c.t.position); }
// 找两辆挨得近的车：压扁一辆、留一辆做对比
for (int i = 0; i < all.Count; i++) for (int j = 0; j < all.Count; j++) { if (i == j) continue; float d = Vector3.Distance(all[i], all[j]); if (d > 0.035f && d < bd) { bd = d; best = all[i]; other = all[j]; found = true; } }
if (!found) { result.Log("没有合适的车"); return; }
int n = v.FlattenCars(best, 0.012f, 1);
var mid = (best + other) * 0.5f; var pos = mid + new Vector3(0.07f, 0.06f, -0.09f);
System.IO.File.WriteAllText("/tmp/monster_game/flatpos.txt", mid.x + " " + mid.y + " " + mid.z);
result.Log("压扁 " + n + " 辆，旁边 " + (bd * 100).ToString("F1") + "cm 处有一辆完好的");
