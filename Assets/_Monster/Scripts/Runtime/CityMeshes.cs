// 城市用的程序化网格：带平滑法线（写进 UV3，描边用）的方块、侧面 UV 按开间重复。
// 第一版场景全部用它拼；正式模块（Blender）导入后同样在 UV3 烘平滑法线。
using System.Collections.Generic;
using UnityEngine;

namespace Monster
{
    public static class CityMeshes
    {
        static readonly Dictionary<string, Mesh> cache = new();
        // 换场景时 Unity 会卸载没人引用的网格，缓存里的引用就失效了：每次生成城市前清一次
        public static void ClearCache() => cache.Clear();

        // 单位方块（-0.5..0.5，底面在 y=0），侧面 u 方向重复 ru / rv 次，顶面 / 底面 0..1
        public static Mesh Box(int ru = 1, int rv = 1, float vRepeat = 1)
        {
            string key = $"box{ru}x{rv}x{vRepeat}";
            if (cache.TryGetValue(key, out var m) && m) return m;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            void Face(Vector3 n, Vector3 right, Vector3 up, float uRep, float vRep)
            {
                int i = verts.Count;
                Vector3 c = n * 0.5f + Vector3.up * 0.5f;
                verts.Add(c - right * 0.5f - up * 0.5f); verts.Add(c + right * 0.5f - up * 0.5f);
                verts.Add(c + right * 0.5f + up * 0.5f); verts.Add(c - right * 0.5f + up * 0.5f);
                for (int k = 0; k < 4; k++) norms.Add(n);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(uRep, 0)); uvs.Add(new Vector2(uRep, vRep)); uvs.Add(new Vector2(0, vRep));
                tris.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            Face(Vector3.forward, Vector3.left, Vector3.up, ru, vRepeat);
            Face(Vector3.back, Vector3.right, Vector3.up, ru, vRepeat);
            Face(Vector3.right, Vector3.forward, Vector3.up, rv, vRepeat);
            Face(Vector3.left, Vector3.back, Vector3.up, rv, vRepeat);
            Face(Vector3.up, Vector3.right, Vector3.forward, 1, 1);
            Face(Vector3.down, Vector3.right, Vector3.back, 1, 1);
            // 顶面 / 底面中心在 y=1 / y=0：上面 Face 用 c = n*0.5 + up*0.5，正好
            m = new Mesh { name = key };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            BakeSmoothNormals(m);
            m.RecalculateBounds();
            cache[key] = m;
            return m;
        }

        // 招牌面片：x -0.5..0.5、y 0..1，面朝 +Z；v 取整张招牌图里 v0..v1 那一段（竖招牌按楼层切段时拼成整图）
        public static Mesh SignQuad(float v0, float v1)
        {
            string key = $"sign{v0:F3}_{v1:F3}";
            if (cache.TryGetValue(key, out var m) && m) return m;
            m = new Mesh { name = key };
            m.SetVertices(new List<Vector3> { new(0.5f, 0, 0), new(-0.5f, 0, 0), new(-0.5f, 1, 0), new(0.5f, 1, 0) });
            m.SetNormals(new List<Vector3> { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward });
            // 面朝 +Z 时观察者看到的左边是 +X：u=0 放在 +X 侧，图不会左右翻
            m.SetUVs(0, new List<Vector2> { new(0, v0), new(1, v0), new(1, v1), new(0, v1) });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);   // 从 +Z 看是顺时针（Unity 正面）
            m.RecalculateBounds();
            cache[key] = m;
            return m;
        }

        // 圆柱（底面在 y=0，高 1，半径 0.5），seg 段
        public static Mesh Cylinder(int seg = 16, float topScale = 1)
        {
            string key = $"cyl{seg}x{topScale}";
            if (cache.TryGetValue(key, out var m) && m) return m;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts.Add(d * 0.5f); verts.Add(d * 0.5f * topScale + Vector3.up);
                norms.Add(d); norms.Add(d);
                uvs.Add(new Vector2(i / (float)seg, 0)); uvs.Add(new Vector2(i / (float)seg, 1));
            }
            for (int i = 0; i < seg; i++) { int a = i * 2; tris.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 }); }
            foreach (var (y, n, s) in new[] { (0f, Vector3.down, 1f), (1f, Vector3.up, topScale) })
            {
                int c = verts.Count; verts.Add(new Vector3(0, y, 0)); norms.Add(n); uvs.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2;
                    verts.Add(new Vector3(Mathf.Cos(a) * 0.5f * s, y, Mathf.Sin(a) * 0.5f * s)); norms.Add(n); uvs.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                }
                for (int i = 0; i < seg; i++) { if (y > 0) tris.AddRange(new[] { c, c + 2 + i, c + 1 + i }); else tris.AddRange(new[] { c, c + 1 + i, c + 2 + i }); }
            }
            m = new Mesh { name = key };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            BakeSmoothNormals(m); m.RecalculateBounds();
            cache[key] = m;
            return m;
        }

        // 四棱锥屋顶（底 1×1 在 y=0，尖在 y=1）
        public static Mesh Pyramid()
        {
            if (cache.TryGetValue("pyr", out var m) && m) return m;
            var top = new Vector3(0, 1, 0);
            var c = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
            for (int i = 0; i < 4; i++)
            {
                int k = verts.Count; verts.Add(c[i]); verts.Add(top); verts.Add(c[(i + 1) % 4]);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0.5f, 1)); uvs.Add(new Vector2(1, 0));
                tris.AddRange(new[] { k, k + 1, k + 2 });
            }
            int b = verts.Count; verts.AddRange(c); uvs.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            m = new Mesh { name = "pyr" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0); m.RecalculateNormals();
            BakeSmoothNormals(m); m.RecalculateBounds();
            cache["pyr"] = m;
            return m;
        }

        // 把同一位置的顶点法线平均，写进 UV3：描边沿它外扩，方块的角不会裂开
        // 低多边形球（底在 y=0，高 1，半径 0.5）：树冠、椰子
        public static Mesh Sphere(int seg = 6, int rings = 4)
        {
            string key = $"sph{seg}x{rings}";
            if (cache.TryGetValue(key, out var m) && m) return m;
            var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
            for (int r = 0; r <= rings; r++)
            {
                float th = Mathf.PI * r / rings;
                for (int s = 0; s <= seg; s++)
                {
                    float ph = 2 * Mathf.PI * s / seg;
                    verts.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph) * 0.5f, 0.5f + Mathf.Cos(th) * 0.5f, Mathf.Sin(th) * Mathf.Sin(ph) * 0.5f));
                    uvs.Add(new Vector2((float)s / seg, 1 - (float)r / rings));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    tris.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            m = new Mesh { name = key };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            BakeSmoothNormals(m);
            m.RecalculateBounds();
            cache[key] = m;
            return m;
        }

        public static void BakeSmoothNormals(Mesh m)
        {
            var v = m.vertices; var n = m.normals;
            var acc = new Dictionary<Vector3Int, Vector3>();
            Vector3Int K(Vector3 p) => new(Mathf.RoundToInt(p.x * 10000), Mathf.RoundToInt(p.y * 10000), Mathf.RoundToInt(p.z * 10000));
            for (int i = 0; i < v.Length; i++) { var k = K(v[i]); acc[k] = (acc.TryGetValue(k, out var a) ? a : Vector3.zero) + n[i]; }
            var sm = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) sm[i] = acc[K(v[i])].normalized;
            m.SetUVs(3, sm);
        }
    }
}
