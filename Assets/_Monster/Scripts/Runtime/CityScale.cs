// 城市整体缩小（11-08 用户：城市有点小 → 楼缩小一些、放更多楼）。
// 城市按“设计尺寸”（真实尺寸 ÷ k）生成，所有楼、道具、车流的造型和相互关系（穿插 / 悬空检查都是在设计尺寸下做的）保持不变；
// 生成完把每个城区以城区原点（桌面角点）为支点整体缩小 k 倍，再把逻辑数据（城区宽深、楼位置 / 占地 / 层高）同步乘 k，玩法和画面对得上。
// 虚拟桌柜要按真实尺寸：缩放之后再生成，挂在城市根节点下（不跟城区一起缩）。
using UnityEngine;

namespace Monster
{
    public partial class CityBuilder
    {
        /// <summary>城市比例：1 = 原始设计；0.7 = 楼缩到七成、同样面积放约两倍的楼</summary>
        public float scale = 1;
        bool shrunk;

        void Shrink(float k)
        {
            scale = k;
            if (Mathf.Approximately(k, 1)) return;
            foreach (var z in city.zones)
            {
                var zr = root.Find(z.id); if (!zr) continue;
                // 城区根节点挪到城区原点（子物体世界位置不变），再整体缩放：缩放支点就是桌面角点，桌面高度不变
                var kids = new System.Collections.Generic.List<(Transform t, Vector3 p, Quaternion r)>();
                foreach (Transform c in zr) kids.Add((c, c.position, c.rotation));
                zr.position = z.origin;
                foreach (var (t, p, r) in kids) t.SetPositionAndRotation(p, r);
                zr.localScale = Vector3.one * k;
                foreach (var tr in zr.GetComponentsInChildren<Traffic>(true)) { tr.pivot = z.origin; tr.scale = k; }
                z.w *= k; z.h *= k; z.cell *= k;
            }
            foreach (var b in city.buildings) { b.a *= k; b.b *= k; b.fw *= k; b.fd *= k; b.lotW *= k; b.lotD *= k; b.bodyW *= k; b.bodyD *= k; b.bodyOff *= k; b.modH *= k; if (b.tops != null) for (int i = 0; i < b.tops.Length; i++) b.tops[i] *= k; }
            foreach (var o in city.opens) { o.a *= k; o.b *= k; o.w *= k; o.d *= k; }
            foreach (var v in views.Values) if (v) v.modH *= k;
            shrunk = true;
        }

        /// <summary>桌柜挂哪里：没缩放时在城区下；缩放后挂城市根节点（按真实尺寸，不跟城区缩）</summary>
        Transform FurnitureRoot(Zone z)
        {
            if (!shrunk) return ZoneRoot(z, "Furniture");
            var n = "Furniture_" + z.id; var t = root.Find(n);
            if (!t) { t = new GameObject(n).transform; t.SetParent(root, false); }
            return t;
        }
    
        /// <summary>
        /// 出场动画：城市从桌面往上“长”出来。s = 0 压扁在桌面上，1 = 正常。
        /// 城区根节点（地面、道具、车流、零散物体）按城区原点压扁；合批块是世界坐标网格，各自以所在城区的桌面高度为基准压扁。
        /// </summary>
        public void SetGrow(float s)
        {
            s = Mathf.Max(0.001f, s);
            foreach (var z in city.zones) { var zr = root.Find(z.id); if (zr) zr.localScale = new Vector3(scale, scale * s, scale); }
            foreach (var c in chunks)
                foreach (var mf in new[] { c.mf, c.mf1 })
                {
                    if (!mf) continue;
                    mf.transform.localScale = new Vector3(1, s, 1);
                    mf.transform.localPosition = new Vector3(0, c.baseY * (1 - s), 0);
                }
            foreach (var t in root.GetComponentsInChildren<Traffic>(true)) t.enabled = s >= 0.999f;
        }
    }
}
