// 场景级体检：代码摆放的东西（路灯、树、车、楼顶设备、集装箱、长椅……）底下必须有东西托着。
// 用体检模式生成城市（不合并网格），给每个渲染器临时加网格碰撞体，从每个“独立物体”的底面往下打射线。
//   CityAudit.Floating() → 返回悬空清单（名字、位置、离下面的距离，单位：真实米）
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Monster.EditorTools
{
    public static class CityAudit
    {
        // 这些是“一组”的父物体：按整组的包围盒检查（组里的零件互相挂着，比如路灯的灯臂）
        static readonly string[] Groups = { "Lamp", "Tree", "Palm", "BusShelter", "Signal", "Crane", "TowerCrane", "STSCrane", "Fountain", "Gazebo" };

        public static int LastUnits, LastPairs;
        public static List<string> Floating(float tolReal = 0.25f)
        {
            LastUnits = 0; LastPairs = 0;
            CityBuilder.AuditMode = true;
            try { MonsterPreview.Build("dusk"); }
            finally { CityBuilder.AuditMode = false; }
            var city = GameObject.Find("City");
            var added = new List<MeshCollider>();
            foreach (var mf in city.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh || mf.GetComponent<Collider>()) continue;
                if (mf.GetComponentInParent<Traffic>()) continue;   // 车流 / 船在动，不参与
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; added.Add(mc);
            }
            foreach (var r in GameObject.Find("Room_PreviewOnly")?.GetComponentsInChildren<MeshFilter>() ?? new MeshFilter[0])
                if (!r.GetComponent<Collider>()) added.Add(r.gameObject.AddComponent<MeshCollider>());
            Physics.SyncTransforms();
            const float K = 0.0075f;
            float tol = tolReal * K;
            var report = new List<string>();
            var checkedRoots = new HashSet<Transform>();
            IEnumerable<Transform> Units()
            {
                foreach (var t in city.GetComponentsInChildren<Transform>())
                {
                    if (t.GetComponentInParent<Traffic>()) continue;
                    var p = t.parent; if (!p) continue;
                    string n = t.name;
                    bool inProps = t.GetComponentsInParent<Transform>().Any(a => a.name == "Props" || a.name == "Harbor" || a.name == "Furniture");
                    if (Groups.Contains(n)) { yield return t; continue; }
                    if (Groups.Contains(p.name)) continue;   // 组里的零件跟着组走
                    if (n == "River" || n == "QuayWall") continue;
                    if (n.StartsWith("Prop_") || n.StartsWith("RoofItem_") || n == "ParkedCar" || n == "YardCar" || n == "Container") { yield return t; continue; }
                    if (inProps && t.GetComponent<MeshFilter>() && !p.name.StartsWith("Open_") && p.name != "Props" && p.name != "Harbor") continue;
                    if (inProps && t.GetComponent<MeshFilter>()) yield return t;
                }
            }
            foreach (var u in Units())
            {
                var rs = u.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                LastUnits++;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                var mine = new HashSet<Collider>(u.GetComponentsInChildren<Collider>());
                // 底面 5 个点往下打（从底面上方 1mm 出发）
                int supported = 0;
                float best = float.MaxValue;
                foreach (var (fx, fz) in new[] { (0.5f, 0.5f), (0.25f, 0.25f), (0.75f, 0.25f), (0.25f, 0.75f), (0.75f, 0.75f) })
                {
                    var o = new Vector3(Mathf.Lerp(b.min.x, b.max.x, fx), b.min.y + 0.001f, Mathf.Lerp(b.min.z, b.max.z, fz));
                    var hits = Physics.RaycastAll(o, Vector3.down, 0.2f).Where(h => !mine.Contains(h.collider)).OrderBy(h => h.distance).ToArray();
                    float d = hits.Length > 0 ? hits[0].distance - 0.001f : float.MaxValue;
                    best = Mathf.Min(best, d);
                    if (d <= tol) supported++;
                }
                // 侧面挂靠（招牌、墙上的东西）：包围盒稍微扩一点，和别的碰撞体重叠就算挂着
                bool lateral = false;
                if (supported == 0)
                {
                    var ov = Physics.OverlapBox(b.center, b.extents + Vector3.one * tol, Quaternion.identity).Where(c => !mine.Contains(c)).ToArray();
                    lateral = ov.Any(c => c.bounds.max.y > b.min.y + b.size.y * 0.3f);
                }
                if (supported == 0 && !lateral)
                    report.Add($"{u.name} @ {u.position:F3} 离下面 {(best == float.MaxValue ? "∞" : (best / K).ToString("F2") + " 米")}  [{string.Join("/", u.GetComponentsInParent<Transform>().Take(4).Select(t => t.name))}]");
            }
            // 穿插：每个独立物体和别的物体（楼、其他小件）的穿透深度 > 0.3 真实米就报（地面 / 沙盘顶面 / 房间不算）
            static bool IsGround(Collider c) { var n = c.name; return n == "RoadsAndShadow" || n == "Terrain" || n == "PlinthStrata" || n == "River" || c.transform.root.name.StartsWith("Room"); }
            foreach (var u in Units())
            {
                if (u.name == "STSCrane" || u.name == "TowerCrane") continue;   // 镂空的大构架，凸包检查不成立（腿的位置在生成时已避让）
                var mine = u.GetComponentsInChildren<MeshCollider>();
                foreach (var mc in mine) { mc.convex = true; }
                Physics.SyncTransforms();
                var own = new HashSet<Collider>(mine);
                var rs = u.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                foreach (var other in Physics.OverlapBox(b.center, b.extents, Quaternion.identity))
                {
                    if (own.Contains(other) || IsGround(other)) continue;
                    LastPairs++;
                    foreach (var mc in mine)
                    {
                        if (Physics.ComputePenetration(mc, mc.transform.position, mc.transform.rotation, other, other.transform.position, other.transform.rotation, out _, out float depth) && depth > 0.3f * K)
                        {
                            report.Add($"穿插 {u.name}/{mc.name} × {other.transform.parent?.name}/{other.name} 深 {(depth / K):F2} 米 @ {u.position:F3}");
                            goto nextUnit;
                        }
                    }
                }
                nextUnit:
                foreach (var mc in mine) mc.convex = false;
            }
            foreach (var c in added) Object.DestroyImmediate(c);
            return report;
        }
    }
}
