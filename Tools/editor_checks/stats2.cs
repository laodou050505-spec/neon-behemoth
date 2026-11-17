var cam = Camera.main; var planes = GeometryUtility.CalculateFrustumPlanes(cam);
var by = new System.Collections.Generic.Dictionary<string, int>();
foreach (var r in Object.FindObjectsByType<MeshRenderer>())
{
    if (!r.enabled || !r.gameObject.activeInHierarchy || !GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
    bool known = false; for (var q = r.transform; q != null; q = q.parent) if (q.name == "Batched" || q.name == "Enemies" || q.name == "GameFx" || q.GetComponent<Monster.BuildingView>() || q.name.StartsWith("Edge_") || q.name == "Props" || q.name.StartsWith("Furniture")) known = true;
    if (known) continue;
    var t = r.transform; string path = (t.parent && t.parent.parent ? t.parent.parent.name.TrimEnd('0','1','2','3','4','5','6','7','8','9','_') + "/" : "") + (t.parent ? t.parent.name.TrimEnd('0','1','2','3','4','5','6','7','8','9','_') + "/" : "") + r.name.TrimEnd('0','1','2','3','4','5','6','7','8','9','_') + "(" + (r.sharedMaterial ? r.sharedMaterial.shader.name.Replace("Monster/", "") : "?") + (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off ? "+影" : "") + ")";
    by.TryGetValue(path, out var n); by[path] = n + 1;
}
var keys = new System.Collections.Generic.List<string>(by.Keys); keys.Sort((a, b) => by[b].CompareTo(by[a])); var sb = new System.Text.StringBuilder();
foreach (var k in keys.GetRange(0, Mathf.Min(16, keys.Count))) sb.Append("\n" + by[k] + "  " + k);
result.Log("其它：" + sb);
