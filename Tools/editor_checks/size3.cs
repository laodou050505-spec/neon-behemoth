var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var sb = new System.Text.StringBuilder();
var seen = new System.Collections.Generic.HashSet<string>();
foreach (var tr in v.builder.root.GetComponentsInChildren<Monster.Traffic>(true))
    foreach (var L in tr.lanes) foreach (var c in L.cars)
    {
        var mf = c.t.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue; string k = mf.sharedMesh.name.Split('_')[1]; if (!seen.Add(k)) continue;
        var b = mf.sharedMesh.bounds; float s = tr.scale * 1f; var ls = c.t.lossyScale; if (ls.x < 0.01f) { seen.Remove(k); continue; }
        sb.Append(k + " 长 " + (Mathf.Max(b.size.x, b.size.z) * ls.x * 100).ToString("F2") + " 宽 " + (Mathf.Min(b.size.x, b.size.z) * ls.x * 100).ToString("F2") + " 高 " + (b.size.y * ls.x * 100).ToString("F2") + " (scale " + ls.x.ToString("F4") + ")  ");
    }
sb.Append("\n");
foreach (var line in System.IO.File.ReadAllLines("/tmp/monster_game/size_ids.txt"))
{
    var q = line.Split(' '); var go = GameObject.Find("Enemy_" + q[1] + "_" + q[0]); if (!go) continue;
    foreach (var mf in go.GetComponentsInChildren<MeshFilter>()) if (mf.name == "body") { var b = mf.sharedMesh.bounds; var ls = mf.transform.lossyScale; sb.Append(q[1] + " 车身 长 " + (Mathf.Max(b.size.x, b.size.z) * ls.x * 100).ToString("F2") + " 宽 " + (Mathf.Min(b.size.x, b.size.z) * ls.x * 100).ToString("F2") + " 高 " + (b.size.y * ls.x * 100).ToString("F2") + " (scale " + ls.x.ToString("F4") + ")  "); }
}
result.Log(sb.ToString());
