// 特效批量绘制：烟、尘、火舌、爆炸、碎块、电线光点、炮弹……每帧把实例收集起来，按网格一次 RenderMeshInstanced 画完。
// 网格是程序生成的低面数几何体（多面体的硬边正好是三渲二的块状感），材质 Monster/FxPuff（每实例颜色 + 自发光量）。
using System.Collections.Generic;
using UnityEngine;

namespace Monster.Game
{
    public class FxBatch
    {
        const int Max = 1023;
        readonly Mesh mesh; readonly Material mat;
        readonly List<Matrix4x4> mats = new(Max);
        readonly Vector4[] tint = new Vector4[Max]; readonly float[] glow = new float[Max];
        readonly MaterialPropertyBlock mpb = new();
        int n;
        public FxBatch(Mesh mesh, Material mat) { this.mesh = mesh; this.mat = mat; }

        public void Add(Vector3 p, Quaternion r, Vector3 s, Color c, float g = 0)
        {
            if (n >= Max || s.x <= 1e-5f) return;
            mats.Add(Matrix4x4.TRS(p, r, s)); tint[n] = c; glow[n] = g; n++;
        }
        public void Add(Vector3 p, float s, Color c, float g = 0) => Add(p, Quaternion.identity, Vector3.one * s, c, g);
        /// <summary>直接给矩阵（不动的东西把矩阵存起来，省掉每帧重算）</summary>
        public void AddM(in Matrix4x4 m, Color c, float g = 0) { if (n >= Max) return; mats.Add(m); tint[n] = c; glow[n] = g; n++; }

        public void Draw()
        {
            if (n == 0) return;
            mpb.SetVectorArray("_Tint", tint); mpb.SetFloatArray("_Glow", glow);
            var rp = new RenderParams(mat) { matProps = mpb, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderMeshInstanced(rp, mesh, 0, mats);
            mats.Clear(); n = 0;
        }

        // ───────── 程序网格 ─────────
        /// <summary>二十面体细分 1 次（80 面）：块状的烟团 / 火球</summary>
        public static Mesh Blob()
        {
            float t = (1 + Mathf.Sqrt(5)) / 2;
            var v = new List<Vector3> { new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1) };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized * 0.5f;
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var tris = new List<int>(f);
            var cache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(k, out var m)) return m;
                v.Add(((v[a] + v[b]) * 0.5f).normalized * 0.5f); cache[k] = v.Count - 1; return v.Count - 1;
            }
            var nt = new List<int>();
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            return Flat(v, nt, "fx_blob");
        }

        /// <summary>八棱锥（底在 y=0，尖在 y=1，半径 0.5）：火舌</summary>
        public static Mesh Cone(int seg = 7)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            v.Add(new Vector3(0, 1, 0)); v.Add(Vector3.zero);
            for (int i = 0; i < seg; i++) { float a = i * Mathf.PI * 2 / seg; v.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0, Mathf.Sin(a) * 0.5f)); }
            for (int i = 0; i < seg; i++) { int a = 2 + i, b = 2 + (i + 1) % seg; t.AddRange(new[] { 0, b, a, 1, a, b }); }
            return Flat(v, t, "fx_cone");
        }

        /// <summary>单位立方体（中心在原点）：碎块、子弹</summary>
        public static Mesh Box()
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < 8; i++) v.Add(new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f));
            int[] f = { 0,2,3,1, 4,5,7,6, 0,1,5,4, 2,6,7,3, 0,4,6,2, 1,3,7,5 };
            for (int i = 0; i < f.Length; i += 4) t.AddRange(new[] { f[i], f[i + 1], f[i + 2], f[i], f[i + 2], f[i + 3] });
            return Flat(v, t, "fx_box");
        }

        /// <summary>圆环（xz 平面，半径 1，管粗 0.08）：爆炸冲击环</summary>
        public static Mesh Ring(int seg = 24, int side = 5)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    float a = i * Mathf.PI * 2 / seg, b = j * Mathf.PI * 2 / side;
                    var c = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    v.Add(c * (1 + 0.08f * Mathf.Cos(b)) + Vector3.up * 0.08f * Mathf.Sin(b));
                }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    int a = i * side + j, b = ((i + 1) % seg) * side + j, c = ((i + 1) % seg) * side + (j + 1) % side, d = i * side + (j + 1) % side;
                    t.AddRange(new[] { a, d, c, a, c, b });
                }
            return Flat(v, t, "fx_ring", p => { var q = new Vector3(p.x, 0, p.z); return q.sqrMagnitude > 1e-6f ? q.normalized : Vector3.zero; });
        }

        /// <summary>圆管（沿 +Z 从 0 到 1，半径 0.5，平滑法线，两头开口）：能量波光束</summary>
        public static Mesh Tube(int seg = 16)
        {
            var V = new List<Vector3>(); var N = new List<Vector3>(); var T = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg; var n = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                V.Add(n * 0.5f); N.Add(n); V.Add(n * 0.5f + Vector3.forward); N.Add(n);
            }
            for (int i = 0; i < seg; i++) { int a = i * 2; T.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            var m = new Mesh { name = "fx_tube" }; m.SetVertices(V); m.SetNormals(N); m.SetTriangles(T, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 8);
            return m;
        }

        /// <summary>拆成每面独立顶点（硬边法线），并检查绕序朝外</summary>
        static Mesh Flat(List<Vector3> v, List<int> t, string name, System.Func<Vector3, Vector3> centerOf = null)
        {
            var V = new List<Vector3>(); var N = new List<Vector3>(); var T = new List<int>();
            var center = Vector3.zero; foreach (var p in v) center += p; center /= v.Count;
            for (int i = 0; i < t.Count; i += 3)
                {
                    var a = v[t[i]]; var b = v[t[i + 1]]; var c = v[t[i + 2]];
                    var n = Vector3.Cross(b - a, c - a).normalized;
                    var m0 = (a + b + c) / 3;
                    if (Vector3.Dot(n, m0 - (centerOf != null ? centerOf(m0) : center)) < 0) { (b, c) = (c, b); n = -n; }
                    int o = V.Count; V.Add(a); V.Add(b); V.Add(c); N.Add(n); N.Add(n); N.Add(n); T.Add(o); T.Add(o + 1); T.Add(o + 2);
                }
            var m = new Mesh { name = name }; m.SetVertices(V); m.SetNormals(N); m.SetTriangles(T, 0); m.RecalculateBounds();
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 4);
            return m;
        }
    }
}
