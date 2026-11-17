var sb = new System.Text.StringBuilder();
foreach (var line in System.IO.File.ReadAllLines("/tmp/monster_game/size_ids.txt"))
{
    var q = line.Split(' '); var go = GameObject.Find("Enemy_" + q[1] + "_" + q[0]); if (!go) continue;
    Bounds b = default; bool any = false; var rot = go.transform.rotation; go.transform.rotation = Quaternion.identity;
    foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
    go.transform.rotation = rot;
    sb.Append(q[1] + " 长 " + (Mathf.Max(b.size.x, b.size.z) * 100).ToString("F2") + " 宽 " + (Mathf.Min(b.size.x, b.size.z) * 100).ToString("F2") + " 高 " + (b.size.y * 100).ToString("F2") + "cm  ");
}
result.Log(sb.ToString());
