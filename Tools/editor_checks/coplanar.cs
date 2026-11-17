// 重合面体检：找“朝向相同、在同一个平面上（间距 < TH）、而且互相重叠”的三角形对 —— 头显里这种地方两个面抢着显示 = 闪。
// 只查环境（地面、路沿、步行道、家具、街道小件），楼本身（BuildingView 下面的）不查。按“物体(材质) × 物体(材质)”汇总重叠面积
float TH = float.Parse(System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim(), System.Globalization.CultureInfo.InvariantCulture);
var gv = Object.FindAnyObjectByType<Monster.Game.GameView>(); var root = gv.builder.root;
var A = new System.Collections.Generic.List<Vector3>(); var B = new System.Collections.Generic.List<Vector3>(); var C = new System.Collections.Generic.List<Vector3>();
var N = new System.Collections.Generic.List<Vector3>(); var D = new System.Collections.Generic.List<float>(); var O = new System.Collections.Generic.List<int>();
var names = new System.Collections.Generic.List<string>();
foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
{
    if (!r.enabled || r.GetComponentInParent<Monster.BuildingView>()) continue;
    var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
    bool skip = false; for (var q = r.transform; q != null && q != root; q = q.parent) if (q.name == "GameFx" || q.name.StartsWith("Enemy")) skip = true;
    if (skip || r.name.EndsWith("_lod1")) continue;   // 远景替身和本体由 LODGroup 二选一，不会同时画
    string nm = (r.transform.parent ? r.transform.parent.name + "/" : "") + r.name + "(" + (r.sharedMaterial ? r.sharedMaterial.name : "?") + ")";
    int oi = names.Count; names.Add(nm);
    var m = mf.sharedMesh; var vs = m.vertices; var l2w = r.transform.localToWorldMatrix;
    for (int sm = 0; sm < m.subMeshCount; sm++)
    {
        var ts = m.GetTriangles(sm);
        for (int i = 0; i + 2 < ts.Length; i += 3)
        {
            var a = l2w.MultiplyPoint3x4(vs[ts[i]]); var b = l2w.MultiplyPoint3x4(vs[ts[i + 1]]); var c = l2w.MultiplyPoint3x4(vs[ts[i + 2]]);
            var cr = Vector3.Cross(b - a, c - a); float ar = cr.magnitude * 0.5f; if (ar < 2e-6f) continue;
            var n = cr / (ar * 2); if (n.y < -0.7f) continue;   // 朝下的面（压在桌面上）看不到
            A.Add(a); B.Add(b); C.Add(c); N.Add(n); D.Add(Vector3.Dot(n, a)); O.Add(oi);
        }
    }
}
// 分桶：法线量化 + 平面距离量化（查相邻桶）
var buckets = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<int>>();
long Key(Vector3 n, int dq) { long kx = Mathf.RoundToInt(n.x * 40) + 50, ky = Mathf.RoundToInt(n.y * 40) + 50, kz = Mathf.RoundToInt(n.z * 40) + 50; return ((kx * 128 + ky) * 128 + kz) * 200003L + (dq + 100000); }
float Q = Mathf.Max(TH, 0.0002f);
for (int i = 0; i < A.Count; i++) { long k = Key(N[i], Mathf.RoundToInt(D[i] / Q)); if (!buckets.TryGetValue(k, out var l)) buckets[k] = l = new System.Collections.Generic.List<int>(); l.Add(i); }
// 两个三角形在平面里的重叠面积（Sutherland–Hodgman 裁剪）
float Overlap(int i, int j)
{
    var n = N[i]; var ux = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized; var uy = Vector3.Cross(n, ux);
    Vector2 P(Vector3 p) => new Vector2(Vector3.Dot(p, ux), Vector3.Dot(p, uy));
    var poly = new System.Collections.Generic.List<Vector2> { P(A[i]), P(B[i]), P(C[i]) };
    var cl = new[] { P(A[j]), P(B[j]), P(C[j]) };
    float sgn = (cl[1].x - cl[0].x) * (cl[2].y - cl[0].y) - (cl[1].y - cl[0].y) * (cl[2].x - cl[0].x) > 0 ? 1 : -1;
    for (int e = 0; e < 3 && poly.Count > 0; e++)
    {
        var p0 = cl[e]; var p1 = cl[(e + 1) % 3]; var outp = new System.Collections.Generic.List<Vector2>();
        float Side(Vector2 p) => sgn * ((p1.x - p0.x) * (p.y - p0.y) - (p1.y - p0.y) * (p.x - p0.x));
        for (int q = 0; q < poly.Count; q++)
        {
            var cur = poly[q]; var nxt = poly[(q + 1) % poly.Count]; float sc = Side(cur), sn = Side(nxt);
            if (sc >= 0) outp.Add(cur);
            if ((sc >= 0) != (sn >= 0)) outp.Add(Vector2.Lerp(cur, nxt, sc / (sc - sn)));
        }
        poly = outp;
    }
    float area = 0; for (int q = 0; q < poly.Count; q++) { var u = poly[q]; var w = poly[(q + 1) % poly.Count]; area += u.x * w.y - w.x * u.y; }
    return Mathf.Abs(area) * 0.5f;
}
var hits = new System.Collections.Generic.Dictionary<string, (int n, float area, float gap, Vector3 at, Vector3 nn)>();
long tests = 0;
foreach (var kv in buckets)
{
    var l = kv.Value;
    for (int dd = 0; dd <= 1; dd++)
    {
        System.Collections.Generic.List<int> l2 = l;
        if (dd == 1 && !buckets.TryGetValue(kv.Key + 1, out l2)) continue;
        for (int x = 0; x < l.Count; x++)
        {
            int i = l[x]; var mn = Vector3.Min(A[i], Vector3.Min(B[i], C[i])); var mx = Vector3.Max(A[i], Vector3.Max(B[i], C[i]));
            for (int y = dd == 0 ? x + 1 : 0; y < l2.Count; y++)
            {
                int j = l2[y];
                var mn2 = Vector3.Min(A[j], Vector3.Min(B[j], C[j])); var mx2 = Vector3.Max(A[j], Vector3.Max(B[j], C[j]));
                if (mn2.x > mx.x || mx2.x < mn.x || mn2.z > mx.z || mx2.z < mn.z || mn2.y > mx.y + 0.001f || mx2.y < mn.y - 0.001f) continue;
                if (Vector3.Dot(N[i], N[j]) < 0.9995f) continue;
                float gap = Mathf.Abs(Vector3.Dot(N[i], A[j]) - D[i]); if (gap >= TH) continue;
                tests++;
                float ov = Overlap(i, j); if (ov < 3e-6f) continue;
                string ka = names[O[i]], kb = names[O[j]]; string key = string.CompareOrdinal(ka, kb) <= 0 ? ka + "  ×  " + kb : kb + "  ×  " + ka;
                hits.TryGetValue(key, out var h); hits[key] = (h.n + 1, h.area + ov, h.n == 0 ? gap : Mathf.Min(h.gap, gap), (A[i] + B[i] + C[i]) / 3, N[i]);
            }
        }
    }
}
var sb = new System.Text.StringBuilder(); var keys = new System.Collections.Generic.List<string>(hits.Keys); keys.Sort((x, y) => hits[y].area.CompareTo(hits[x].area));
float tot = 0; foreach (var k in keys) tot += hits[k].area;
float bt = 0; int bn = 0; foreach (var k in keys) if (k.StartsWith("Batched/Chunk_module")) { bt += hits[k].area; bn++; }
keys.RemoveAll(k => k.StartsWith("Batched/Chunk_module") && System.IO.File.Exists("/tmp/monster_game/nobuild.flag"));
sb.Append("\n楼体内部 " + bn + " 种 " + (bt * 1e4f).ToString("F1") + "cm²");
foreach (var k in keys.GetRange(0, Mathf.Min(45, keys.Count))) sb.Append("\n" + (hits[k].area * 1e4f).ToString("F2") + "cm² " + hits[k].n + "对 间距" + (hits[k].gap * 1000).ToString("F3") + "mm 法线" + hits[k].nn.ToString("F2") + " @" + hits[k].at.ToString("F3") + "  " + k);
result.Log("TH=" + (TH * 1000) + "mm 三角形 " + A.Count + " 物体 " + names.Count + " 精测 " + tests + "；重合的名字对 " + keys.Count + " 种，共 " + (tot * 1e4f).ToString("F1") + "cm²" + sb);
