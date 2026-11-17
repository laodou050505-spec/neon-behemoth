// 画了多少：把相机能看到的东西按“渲染器个数 × 材质通道数”估一下绘制次数，并统计三角形
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; var cam = Camera.main; var planes = GeometryUtility.CalculateFrustumPlanes(cam);
int rend = 0, draws = 0, shadow = 0, detached = 0; long tris = 0; var mats = new System.Collections.Generic.HashSet<Material>();
var byGroup = new System.Collections.Generic.Dictionary<string, (int n, long t)>();
foreach (var r in Object.FindObjectsByType<MeshRenderer>())
{
    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
    var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
    if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
    rend++; var m = r.sharedMaterial; if (m) mats.Add(m);
    bool outline = m && m.shader && m.shader.name == "Monster/CityToon" && m.GetShaderPassEnabled("SRPDefaultUnlit") && m.GetFloat("_OutlineWidth") > 0;
    int d = 1 + (outline ? 1 : 0); if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) { d++; shadow++; }
    draws += d; long t = mf.sharedMesh.triangles.Length / 3 * (outline ? 2 : 1); tris += t;
    string grp = "其它"; for (var q = r.transform; q != null; q = q.parent) { if (q.name == "Batched") { grp = "合批城市"; break; } if (q.name == "Enemies") { grp = "追兵"; break; } if (q.name == "GameFx") { grp = "特效残骸"; break; } if (q.GetComponent<Monster.BuildingView>()) { grp = "拆开的楼"; break; } if (q.name.StartsWith("Edge_") || q.name == "Props" || q.name.StartsWith("Furniture")) { grp = "环境"; break; } }
    byGroup.TryGetValue(grp, out var b0); byGroup[grp] = (b0.n + d, b0.t + t);
}
var sb = new System.Text.StringBuilder(); foreach (var kv in byGroup) sb.Append(" | " + kv.Key + " " + kv.Value.n + " 次 " + kv.Value.t / 1000 + "k 面");
result.Log("可见渲染器 " + rend + " 估计绘制 " + draws + " 次（投影 " + shadow + "）材质 " + mats.Count + " 种 三角形 " + tris / 1000 + "k" + sb + " || 追兵 " + g.enemies.Count + " stars=" + g.stars + " time=" + g.time.ToString("F0"));
