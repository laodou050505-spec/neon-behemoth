// 贴地面片体检：找“顶面高度差不到 0.6mm、水平重叠超过 0.5cm²”的薄片对（头显里会互相抢着显示 = 闪）。按名字对汇总
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var root = v.builder.root;
var list = new System.Collections.Generic.List<(string n, Bounds b, bool on)>();
foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
{
    if (!r.gameObject.activeInHierarchy) continue;
    var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
    var t = r.transform; bool batched = false; for (var q = t; q != null; q = q.parent) if (q.name == "Batched") batched = true;
    if (batched) continue;
    var lb = mf.sharedMesh.bounds; var b = new Bounds(t.TransformPoint(lb.center), Vector3.zero);
    for (int i = 0; i < 8; i++) b.Encapsulate(t.TransformPoint(lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1))));
    if (b.size.y < 0.0035f && b.size.x * b.size.z > 0.0001f) { string n = r.name; if (r.sharedMaterial && (n == "p" || n.Length < 2)) n = r.sharedMaterial.name; list.Add((n + "(" + r.sharedMaterial?.shader?.name?.Replace("Monster/", "") + ")", b, r.enabled)); }
}
var hits = new System.Collections.Generic.Dictionary<string, (int n, float minGap, float y)>();
for (int i = 0; i < list.Count; i++)
    for (int j = i + 1; j < list.Count; j++)
    {
        var A = list[i].b; var B = list[j].b;
        float ox = Mathf.Min(A.max.x, B.max.x) - Mathf.Max(A.min.x, B.min.x), oz = Mathf.Min(A.max.z, B.max.z) - Mathf.Max(A.min.z, B.min.z);
        if (ox <= 0 || oz <= 0 || ox * oz < 0.00005f) continue;
        float gap = Mathf.Abs(A.max.y - B.max.y); if (gap >= 0.0006f) continue;
        string key = string.CompareOrdinal(list[i].n, list[j].n) < 0 ? list[i].n + " × " + list[j].n : list[j].n + " × " + list[i].n;
        hits.TryGetValue(key, out var h); hits[key] = (h.n + 1, h.n == 0 ? gap : Mathf.Min(h.minGap, gap), A.max.y);
    }
var sb = new System.Text.StringBuilder(); var keys = new System.Collections.Generic.List<string>(hits.Keys); keys.Sort((x, y) => hits[y].n.CompareTo(hits[x].n));
foreach (var k in keys.GetRange(0, Mathf.Min(25, keys.Count))) sb.Append("\n" + hits[k].n + " 对  最小高差 " + (hits[k].minGap * 1000).ToString("F2") + "mm  y=" + hits[k].y.ToString("F4") + "  " + k);
result.Log("薄片 " + list.Count + " 个；可疑的名字对 " + hits.Count + " 种" + sb + "\ncam near=" + Camera.main.nearClipPlane + " far=" + Camera.main.farClipPlane);
