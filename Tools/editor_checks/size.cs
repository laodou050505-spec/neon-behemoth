// 量车的大小：路上各种民用车的长度分布（世界尺寸，厘米），和各种地面追兵模型的实际长度
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; g.sealMax = 99999;
var lens = new System.Collections.Generic.Dictionary<string, (int n, float len)>();
foreach (var tr in v.builder.root.GetComponentsInChildren<Monster.Traffic>(true))
{
    if (tr.vmax < Monster.Traffic.Vmax * 0.9f) continue;
    foreach (var L in tr.lanes) foreach (var c in L.cars) { var mf = c.t.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue; var b = mf.sharedMesh.bounds; float len = Mathf.Max(b.size.x, b.size.z) * tr.scale * 1f; string k = mf.sharedMesh.name; lens.TryGetValue(k, out var o); lens[k] = (o.n + 1, c.len * tr.scale); }
}
var sb = new System.Text.StringBuilder("民用车："); foreach (var kv in lens) sb.Append(kv.Key + "×" + kv.Value.n + " " + (kv.Value.len * 100).ToString("F2") + "cm  ");
sb.Append("\n追兵：");
foreach (var ty in new[] { "police", "repair", "apc", "tank", "rocket" })
{
    var e = Monster.Game.Enemies.Spawn(g, ty, g.city.zones[0]); if (e == null) continue;
    System.IO.File.AppendAllText("/tmp/monster_game/size_ids.txt", e.id + " " + ty + "\n");
}
result.Log(sb.ToString());
