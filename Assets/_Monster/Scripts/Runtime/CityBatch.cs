// 静态合批（11-07，性能）：城市生成完以后，把不会动的东西按“城区 × 1.1 米格子 × 材质 × 是否投影”合成大网格，
// 原来的渲染器只关掉、物体留着（倒塌逻辑要用每一层的 Transform）。
// 楼被打中要拆的时候调 Detach(view)：把这栋楼从它所在的合批网格里剔掉（重建那几块网格），再打开它自己的渲染器。
// 合批后窗灯的随机种子按“每一层的世界位置”烘进 uv2.y（着色器里 uv2.y > 0 时优先用它），合批前后窗灯一样。
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Monster
{
    public partial class CityBuilder
    {
        public class Chunk
        {
            public Material mat; public MeshFilter mf, mf1; public UnityEngine.Rendering.ShadowCastingMode shadow;
            public bool hasLod;   // 有没有远处简化版（只有楼模块 / 楼顶杂物的块才有）
            public Bounds bounds;
            public float baseY;   // 所在城区的桌面高度（出场动画从这里往上长）
            public readonly List<(MeshRenderer r, int sub, BuildingView owner, float seed)> parts = new();
        }
        public readonly List<Chunk> chunks = new();
        readonly Dictionary<BuildingView, List<Chunk>> chunkOf = new();
        const float ChunkSize = 1.1f;

        static float Seed(Vector3 p) { float s = Mathf.Sin(Vector3.Dot(p, new Vector3(127.1f, 311.7f, 74.7f))) * 43758.5453f; s -= Mathf.Floor(s); return Mathf.Max(s, 0.0001f); }

        public void BatchCity() { var it = BatchCitySteps(); while (it.MoveNext()) { } }
        /// <summary>分步版（BuildAsync 用）：每合完一个块让出一帧，生成期间画面不卡</summary>
        public System.Collections.IEnumerator BatchCitySteps()
        {
            var groups = new Dictionary<(string zone, int cx, int cz, Material m, UnityEngine.Rendering.ShadowCastingMode sh), Chunk>();
            void Add(MeshRenderer r, BuildingView owner, string zone, float seed)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (!r.enabled || !mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) return;
                var mats = r.sharedMaterials;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                {
                    var m = mats[Mathf.Min(s, mats.Length - 1)];
                    if (!m || m.shader != CityMaterials.Toon) continue;   // 透明的（护盾、地面、水）不合
                    var p = r.transform.position;
                    var key = (zone, Mathf.FloorToInt(p.x / ChunkSize), Mathf.FloorToInt(p.z / ChunkSize), m, r.shadowCastingMode);
                    if (!groups.TryGetValue(key, out var c)) groups[key] = c = new Chunk { mat = m, shadow = r.shadowCastingMode, baseY = city.zones.Find(q => q.id == zone)?.origin.y ?? 0 };
                    c.parts.Add((r, s, owner, seed));
                }
            }
            foreach (var kv in views)
            {
                var v = kv.Value; if (!v) continue;
                foreach (Transform storey in v.transform)
                    foreach (var r in storey.GetComponentsInChildren<MeshRenderer>())
                        Add(r, v, v.transform.parent.parent.name, Seed(storey.position));
            }
            foreach (var z in city.zones)
            {
                var zr = root.Find(z.id); if (!zr) continue;
                foreach (var name in new[] { "Harbor", "Furniture" })
                {
                    var t = zr.Find(name) ?? (name == "Furniture" ? root.Find("Furniture_" + z.id) : null); if (!t) continue;
                    foreach (var r in t.GetComponentsInChildren<MeshRenderer>())
                        if (!r.GetComponentInParent<Traffic>()) Add(r, null, z.id, 0);
                }
            }
            var parent = new GameObject("Batched").transform; parent.SetParent(root, false);
            foreach (var c in groups.Values)
            {
                if (c.parts.Count < 2) continue;
                var go = new GameObject("Chunk_" + c.mat.name); go.transform.SetParent(parent, false);
                c.mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = c.mat; mr.shadowCastingMode = c.shadow;
                c.hasLod = c.parts.Any(p => LodMesh(p.r) != p.r.GetComponent<MeshFilter>().sharedMesh);
                if (c.hasLod)
                {
                    var go1 = new GameObject(go.name + "_lod1"); go1.transform.SetParent(parent, false);
                    c.mf1 = go1.AddComponent<MeshFilter>(); var mr1 = go1.AddComponent<MeshRenderer>(); mr1.sharedMaterial = NoOutline(c.mat);   // 远处简化版不画描边（省一遍顶点）
                }
                Rebuild(c);
                foreach (var (r, _, owner, _) in c.parts)
                {
                    r.enabled = false;
                    if (owner) { if (!chunkOf.TryGetValue(owner, out var l)) chunkOf[owner] = l = new List<Chunk>(); if (!l.Contains(c)) l.Add(c); }
                }
                chunks.Add(c);
                yield return null;
            }
            UpdateLod(new Vector3(0, 1.62f, 0.05f));   // 默认站位（编辑器预览）；运行时 CityLod 按头显位置更新
            root.gameObject.AddComponent<CityLod>().builder = this;
        }

        // 远处用哪个网格：楼模块换简化版；楼顶杂物、招牌、楼顶附件远处不画（null）；其余同一个
        static Mesh LodMesh(MeshRenderer r)
        {
            var m = r.GetComponent<MeshFilter>().sharedMesh;
            if (r.name == "RoofClutter" || r.name.StartsWith("Sign") || r.name.StartsWith("Prop_")) return null;
            return ModuleSet.Lod1.TryGetValue(m, out var l) ? l : m;
        }

        void Rebuild(Chunk c)
        {
            c.mf.sharedMesh = Build(c, c.mf.sharedMesh, false);
            c.bounds = c.mf.GetComponent<MeshRenderer>().bounds;
            if (c.hasLod) c.mf1.sharedMesh = Build(c, c.mf1.sharedMesh, true);
            ApplyLod(c, chunkLod1.Contains(c));
        }

        // 11-16 用户真机：后期明显卡顿掉帧。实测：每拆 / 点着一栋楼，都要把它所在的合批块整个重建一遍 —— 每个零件的顶点重新做一遍矩阵变换、法线归一化，
        // 电脑上平均 27ms、最慢 77ms（头显的处理器慢三到五倍 = 顿一下）；后期火势蔓延、追兵乱轰时会连着发生。两处改动：
        //   ① 每个零件变换好的顶点数据只算一次、存起来（Baked），以后重建只是把还留着的零件的数组首尾拼起来；
        //   ② 重建不在拆楼的那一帧做：先记下“这块脏了”，由 CityLod 每帧最多花 2.5ms 去重建（FlushDirty）。重建好之前，这栋楼的零件已经单独显示出来，
        //      和旧的合批网格在同一位置重合一两帧（两份完全一样，看不出来），不会出现楼消失一帧的情况。
        class Baked { public Vector3[] v, n, sm; public Vector2[] uv, lit; public int[] tri; }
        readonly Dictionary<(MeshRenderer r, int sub, bool lod), Baked> baked = new();
        static readonly List<Vector3> tv = new(), tn = new(), tsm = new(); static readonly List<Vector2> tuv = new(), tlit = new(); static readonly List<int> ttri = new();
        Baked Bake(Chunk c, MeshRenderer r, int sub, float seed, bool lod1)
        {
            if (baked.TryGetValue((r, sub, lod1), out var b)) return b;
            var mesh = lod1 ? LodMesh(r) : r.GetComponent<MeshFilter>().sharedMesh;
            if (!mesh) return baked[(r, sub, lod1)] = null;
            var M = c.mf.transform.worldToLocalMatrix * r.transform.localToWorldMatrix; var N = M.inverse.transpose;
            mesh.GetVertices(tv); mesh.GetNormals(tn); mesh.GetUVs(0, tuv); mesh.GetUVs(1, tlit); mesh.GetUVs(3, tsm); mesh.GetTriangles(ttri, sub);
            int nv = tv.Count;
            b = new Baked { v = new Vector3[nv], n = new Vector3[nv], sm = new Vector3[nv], uv = new Vector2[nv], lit = new Vector2[nv], tri = ttri.ToArray() };
            for (int i = 0; i < nv; i++)
            {
                b.v[i] = M.MultiplyPoint3x4(tv[i]);
                b.n[i] = i < tn.Count ? N.MultiplyVector(tn[i]).normalized : Vector3.up;
                b.uv[i] = i < tuv.Count ? tuv[i] : Vector2.zero;
                b.lit[i] = new Vector2(i < tlit.Count ? tlit[i].x : 2f, seed);
                b.sm[i] = i < tsm.Count ? N.MultiplyVector(tsm[i]).normalized : Vector3.zero;
            }
            return baked[(r, sub, lod1)] = b;
        }
        static Vector3[] bv = new Vector3[0], bn = new Vector3[0], bsm = new Vector3[0]; static Vector2[] buv = new Vector2[0], blit = new Vector2[0]; static int[] btri = new int[0];
        Mesh Build(Chunk c, Mesh into, bool lod1)
        {
            int nv = 0, nt = 0;
            foreach (var (r, sub, owner, seed) in c.parts) { var b = Bake(c, r, sub, seed, lod1); if (b == null) continue; nv += b.v.Length; nt += b.tri.Length; }
            if (bv.Length < nv) { int cap = Mathf.NextPowerOfTwo(nv); bv = new Vector3[cap]; bn = new Vector3[cap]; bsm = new Vector3[cap]; buv = new Vector2[cap]; blit = new Vector2[cap]; }
            if (btri.Length < nt) btri = new int[Mathf.NextPowerOfTwo(nt)];
            int o = 0, ot = 0;
            foreach (var (r, sub, owner, seed) in c.parts)
            {
                var b = Bake(c, r, sub, seed, lod1); if (b == null) continue;
                int n = b.v.Length;
                System.Array.Copy(b.v, 0, bv, o, n); System.Array.Copy(b.n, 0, bn, o, n); System.Array.Copy(b.sm, 0, bsm, o, n); System.Array.Copy(b.uv, 0, buv, o, n); System.Array.Copy(b.lit, 0, blit, o, n);
                var t = b.tri; for (int i = 0; i < t.Length; i++) btri[ot + i] = t[i] + o;
                o += n; ot += t.Length;
            }
            var mesh2 = into ? into : new Mesh { name = "chunk_" + c.mat.name + (lod1 ? "_lod1" : "") };
            mesh2.Clear();
            mesh2.indexFormat = nv > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh2.SetVertices(bv, 0, nv); mesh2.SetNormals(bn, 0, nv); mesh2.SetUVs(0, buv, 0, nv); mesh2.SetUVs(1, blit, 0, nv); mesh2.SetUVs(3, bsm, 0, nv); mesh2.SetTriangles(btri, 0, nt, 0, false);
            mesh2.RecalculateBounds();
            return mesh2;
        }

        readonly Dictionary<Material, Material> noOutline = new();
        Material NoOutline(Material m)
        {
            if (noOutline.TryGetValue(m, out var n) && n) return n;
            n = new Material(m) { name = m.name + "_far" }; n.SetShaderPassEnabled("SRPDefaultUnlit", false);
            noOutline[m] = n; return n;
        }

        // ───────── 远近切换：离玩家（相机）1.6 米以外的块用简化版；影子永远用简化版画 ─────────
        readonly HashSet<Chunk> chunkLod1 = new();
        public const float LodDistance = 1.6f;
        void ApplyLod(Chunk c, bool far)
        {
            if (far) chunkLod1.Add(c); else chunkLod1.Remove(c);
            var r0 = c.mf.GetComponent<MeshRenderer>();
            if (!c.hasLod) { r0.enabled = true; return; }
            var r1 = c.mf1.GetComponent<MeshRenderer>();
            r0.enabled = !far; r0.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bool shadow = c.shadow != UnityEngine.Rendering.ShadowCastingMode.Off;
            r1.enabled = far || shadow;
            r1.shadowCastingMode = far ? c.shadow : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        /// <summary>按观察点更新所有块的远近（CityLod 每 0.2 秒调一次；编辑器里生成时按默认站位调一次）</summary>
        public void UpdateLod(Vector3 eye)
        {
            // 迟滞 ±14cm：头在切换距离附近晃时不来回切（带描边 ↔ 不带描边 = 一闪一闪）
            foreach (var c in chunks)
            {
                if (!c.hasLod) continue;
                float d = Mathf.Sqrt(c.bounds.SqrDistance(eye)); bool was = chunkLod1.Contains(c);
                bool far = was ? d > LodDistance - 0.14f : d > LodDistance + 0.14f;
                if (far != was) ApplyLod(c, far);
            }
        }

        // ───────── 拆出来以后还立着的楼：自己再合成一份（11-16）─────────
        // 楼一着火 / 被拍掉一截，就从合批网格里拆出来、一层层单独画：一栋 6 层的楼 = 十来个渲染器 × 3 遍（本体、描边、影子）。
        // 压测（五星、30 栋被拆、14 栋着火）：这类楼占了 1090 次绘制里的 457 次。现在拆出来的楼把自己还在的各层再合成每种材质一个网格（挂在楼下面的 Merged），
        // 原来各层的渲染器关掉（物体还在：倒塌时照旧一层层克隆出去）。楼再被拆一截时先还原（Unmerge）、拆完再排队重合。
        readonly Dictionary<BuildingView, List<MeshRenderer>> mergedSrc = new();
        readonly Queue<BuildingView> remergeQ = new(); readonly HashSet<BuildingView> remergeSet = new();
        public void QueueRemerge(BuildingView v) { if (v && remergeSet.Add(v)) remergeQ.Enqueue(v); }
        /// <summary>还原成一层层单独画（要改这栋楼之前调）</summary>
        public void Unmerge(BuildingView view)
        {
            if (!view || !mergedSrc.TryGetValue(view, out var src)) return;
            foreach (var r in src) if (r) r.enabled = true;
            mergedSrc.Remove(view);
            var m = view.transform.Find("Merged");
            if (m) { foreach (var mf in m.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh) Object.Destroy(mf.sharedMesh); m.gameObject.SetActive(false); Object.Destroy(m.gameObject); }
        }
        static readonly MaterialPropertyBlock mergeMpb = new();
        void Remerge(BuildingView view)
        {
            Unmerge(view);
            if (!view || chunkOf.ContainsKey(view) || view.storeys == null) return;   // 还在合批网格里的不用管
            var groups = new Dictionary<(Material m, UnityEngine.Rendering.ShadowCastingMode sh), List<(MeshRenderer r, float seed)>>();
            int count = 0;
            void Collect(Transform st)
            {
                if (!st || !st.gameObject.activeSelf) return;
                float seed = Seed(st.position);
                foreach (var r in st.GetComponentsInChildren<MeshRenderer>())
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (!r.enabled || !mf || !mf.sharedMesh || !mf.sharedMesh.isReadable || mf.sharedMesh.subMeshCount != 1) continue;
                    var m = r.sharedMaterial; if (!m || m.shader != CityMaterials.Toon) continue;
                    var key = (m, r.shadowCastingMode);
                    if (!groups.TryGetValue(key, out var l)) groups[key] = l = new List<(MeshRenderer, float)>();
                    l.Add((r, seed)); count++;
                }
            }
            foreach (var st in view.storeys) Collect(st);
            if (view.crown && System.Array.IndexOf(view.storeys, view.crown) < 0) Collect(view.crown);
            if (count < 3) return;   // 只剩一两件：不值得合
            var root = new GameObject("Merged").transform; root.SetParent(view.transform, false);
            var w2l = view.transform.worldToLocalMatrix; var src = new List<MeshRenderer>();
            foreach (var kv in groups)
            {
                int nv = 0, nt = 0;
                foreach (var (r, _) in kv.Value) { var me = r.GetComponent<MeshFilter>().sharedMesh; nv += me.vertexCount; nt += (int)me.GetIndexCount(0); }
                var V = new Vector3[nv]; var Nn = new Vector3[nv]; var Sm = new Vector3[nv]; var U = new Vector2[nv]; var L = new Vector2[nv]; var T = new int[nt];
                int o = 0, ot = 0;
                foreach (var (r, seed) in kv.Value)
                {
                    var me = r.GetComponent<MeshFilter>().sharedMesh;
                    var M = w2l * r.transform.localToWorldMatrix; var N = M.inverse.transpose;
                    me.GetVertices(tv); me.GetNormals(tn); me.GetUVs(0, tuv); me.GetUVs(1, tlit); me.GetUVs(3, tsm); me.GetTriangles(ttri, 0);
                    for (int i = 0; i < tv.Count; i++)
                    {
                        V[o + i] = M.MultiplyPoint3x4(tv[i]);
                        Nn[o + i] = i < tn.Count ? N.MultiplyVector(tn[i]).normalized : Vector3.up;
                        U[o + i] = i < tuv.Count ? tuv[i] : Vector2.zero;
                        L[o + i] = new Vector2(i < tlit.Count ? tlit[i].x : 2f, seed);
                        Sm[o + i] = i < tsm.Count ? N.MultiplyVector(tsm[i]).normalized : Vector3.zero;
                    }
                    for (int i = 0; i < ttri.Count; i++) T[ot + i] = ttri[i] + o;
                    o += tv.Count; ot += ttri.Count;
                }
                var mesh = new Mesh { name = "merged_" + kv.Key.m.name, indexFormat = nv > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.SetVertices(V); mesh.SetNormals(Nn); mesh.SetUVs(0, U); mesh.SetUVs(1, L); mesh.SetUVs(3, Sm); mesh.SetTriangles(T, 0, false); mesh.RecalculateBounds();
                var go = new GameObject(kv.Key.m.name); go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = kv.Key.m; mr.shadowCastingMode = kv.Key.sh;
                var first = kv.Value[0].r; if (first.HasPropertyBlock()) { first.GetPropertyBlock(mergeMpb); mr.SetPropertyBlock(mergeMpb); }   // 烧黑的颜色跟过来
                foreach (var (r, _) in kv.Value) { r.enabled = false; src.Add(r); }
            }
            mergedSrc[view] = src;
        }

        readonly HashSet<Chunk> dirty = new(); readonly Queue<Chunk> dirtyQ = new();
        /// <summary>true = Detach 当场重建合批网格（出场动画、编辑器体检用）；平时 false，交给 FlushDirty 分帧做</summary>
        public static bool Immediate;
        /// <summary>重建等着的合批块，最多花 budgetMs 毫秒（至少做一块）。返回还剩几块</summary>
        public int FlushDirty(float budgetMs = 2.5f)
        {
            if (dirtyQ.Count == 0 && remergeQ.Count == 0) return 0;
            double t0 = Time.realtimeSinceStartupAsDouble;
            while (dirtyQ.Count > 0)
            {
                var c = dirtyQ.Dequeue(); dirty.Remove(c);
                if (c.mf) Rebuild(c);
                if ((Time.realtimeSinceStartupAsDouble - t0) * 1000 > budgetMs) break;
            }
            // 合批块都重建完了，再用剩下的时间给拆出来的楼重新合网格（每帧最多一栋）
            if (dirtyQ.Count == 0 && remergeQ.Count > 0 && (Time.realtimeSinceStartupAsDouble - t0) * 1000 < budgetMs)
            {
                var v = remergeQ.Dequeue(); remergeSet.Remove(v);
                if (v) Remerge(v);
            }
            return dirtyQ.Count + remergeQ.Count;
        }
        /// <summary>要拆这栋楼（倒塌 / 着火 / 被抓起）之前调用：把它从合批网格里拿出来，恢复成一层层独立渲染。</summary>
        public void Detach(BuildingView view)
        {
            if (!view || !chunkOf.TryGetValue(view, out var list)) return;
            foreach (var c in list)
            {
                foreach (var p in c.parts) if (p.owner == view) { p.r.enabled = true; baked.Remove((p.r, p.sub, false)); baked.Remove((p.r, p.sub, true)); }
                c.parts.RemoveAll(p => p.owner == view);
                if (Immediate) Rebuild(c); else if (dirty.Add(c)) dirtyQ.Enqueue(c);
            }
            chunkOf.Remove(view);
        }
    }

    /// <summary>运行时按头显（主相机）位置每 0.2 秒更新一次合批块的远近版本</summary>
    public class CityLod : MonoBehaviour
    {
        public CityBuilder builder;
        float next;
        void Update()
        {
            if (builder == null) return;
            builder.FlushDirty();
            if (Time.time < next) return;
            next = Time.time + 0.2f;
            var cam = Camera.main;
            if (cam) builder.UpdateLod(cam.transform.position);
        }
    }
}
