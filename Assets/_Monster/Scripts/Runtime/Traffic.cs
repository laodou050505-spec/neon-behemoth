// 车流：每条车道是一条直线（分区坐标里沿 a 或沿 b），车只直行。
// 红绿灯每个路口一套相位（11-08 用户：车动得太规律、每条街都一样 → 不同步）：u 向绿 6s → 黄 1s → 全红 2s → v 向绿 6s → 黄 1s → 全红 2s，
// 但全城分 6 个相位组，每个路口按位置随机落在一组，组与组之间错开，所以一条街上前后几个路口不同时变灯。
// 每辆车有自己的“性格”：巡航速度（六成到一成半快）、起步快慢、跟车距离、看见黄灯冲不冲；偶尔临时靠边停一会儿再走。
// 每条车道疏密不同；车开到车道尽头淡出后，隔一段随机时间才从起点重新进场。
// 全红 2s 足够最长的公交清空路口，横竖车流不会穿插（每个路口自己的相位里也是一样的保证）。
using System.Collections.Generic;
using UnityEngine;

namespace Monster
{
    public class Traffic : MonoBehaviour
    {
        /// <summary>民用车保留比例（0～1）：GameView 按通缉星级设（0 星 1.0，每升一星少两成，五星只剩零星几辆）</summary>
        public static float Keep = 1;
        /// <summary>
        /// 11-16 用户：战斗开始以后不应该再有车开来开去（楼都没了车还在跑，很违和），车停在路上不动就行。
        /// Halt = true：车陆续刹停（11-17 用户：应该是广播说巨兽出现以后才逐渐停下来 —— 上一版开局两秒全城的车一秒内一起定住）：
        /// 每辆车有自己的反应时间（0～5.5 秒，随机），到点才开始刹、一两秒停稳；还没轮到的照常开，开到前车后面自然排队停下。
        /// 之后就停在原地；开出去的不再进场；船也靠岸不动。
        /// 停着的车照样能捏起来扔、能被拍扁。由 GameView 在开局广播响起时打开，场景重新加载时复位
        /// </summary>
        public static bool Halt;
        public class Car
        {
            public Transform t; public float s, v, len;
            public float cruise = 1, accel = 0.05f, gap = 1, wait;   // 巡航速度（× Vmax）、加速度、跟车距离（× Gap）、在起点外排队等进场的剩余秒数
            public bool bold;                                         // 黄灯会冲过去
            public float stopAt = -1, stopT;                          // 临时靠边：停在哪（车道位置）、停多久
            public float rank = -1; public bool hidden;                                       // 开出去了，等着重新进场
        }
        public class Lane
        {
            public Vector3 p0, dir; public float s0, s1; public bool alongU;
            public List<float> stops = new();                         // 停止线（车头位置，升序）
            public List<(float s, int g)> stopList = new();           // 停止线 + 该路口的相位组
            public float density = 1;
            public List<Car> cars = new();
        }

        public List<Lane> lanes = new();
        public Material sigU, sigV;          // 船道没有灯（保留兼容旧字段）
        public Material[] sigUs, sigVs;      // 每个相位组一对灯色材质
        public float time = 2f;
        public float vmax = Vmax;            // 船用更慢的速度
        public Vector3 pivot; public float scale = 1;   // 城市整体缩小后：车道数据仍是设计尺寸，摆车时以城区原点为支点缩放
        public const float Period = 18f, Vmax = 0.06f, Gap = 0.012f;
        public const int Groups = 6;
        static readonly Color Green = new(0.3f, 1.7f, 0.6f), Yellow = new(1.8f, 1.3f, 0.2f), Red = new(1.9f, 0.22f, 0.2f);
        readonly System.Random rnd = new(7);

        public static float GroupOffset(int g) => g * Period / Groups + (g % 2) * 1.3f;
        /// <summary>路口 (a, b) 属于哪个相位组：按位置哈希（同一路口所有车道、所有灯一致）</summary>
        public static int GroupOf(Zone z, float a, float b)
        {
            // 对齐到马路中线（车道坐标偏了半个车道，不对齐的话同一路口的横竖车道会分到不同组 → 路口里撞车）
            float C = CityConst.Cell;
            a = (Mathf.Floor(a / C) + 0.5f) * C; b = (Mathf.Floor(b / C) + 0.5f) * C;
            int h = Mathf.RoundToInt(a * 1000) * 73856093 ^ Mathf.RoundToInt(b * 1000) * 19349663 ^ z.id.GetHashCode();
            return (int)((uint)h % Groups);
        }
        static float Phase(bool alongU, float t) => Mathf.Repeat(t, Period) - (alongU ? 0f : 9f);
        public static bool Go(bool alongU, float t) { float d = Phase(alongU, t); return d >= 0 && d < 6f; }
        static bool Yellowish(bool alongU, float t) { float d = Phase(alongU, t); return d >= 6f && d < 7f; }
        public static Color Light(bool alongU, float t)
        {
            float d = Phase(alongU, t);
            return d >= 0 && d < 6f ? Green : d >= 6f && d < 7f ? Yellow : Red;
        }

        /// <summary>给一辆车随机“性格”（大车更慢更稳）</summary>
        public static void Personalize(Car c, Rng rng, bool big)
        {
            c.cruise = big ? rng.Range(0.5f, 0.75f) : rng.Chance(0.15f) ? rng.Range(1.1f, 1.45f) : rng.Range(0.65f, 1.05f);
            c.accel = big ? rng.Range(0.025f, 0.04f) : rng.Range(0.04f, 0.09f);
            c.gap = rng.Range(0.8f, 1.8f);
            c.bold = !big && rng.Chance(0.3f);
        }

        // 运行时用 GPU 实例化画车（同一种车型 + 颜色一次画完），自己的渲染器关掉；编辑器里截图仍走普通渲染器
        readonly Dictionary<Mesh, List<Matrix4x4>> batches = new();
        readonly Dictionary<Mesh, Material> batchMat = new();
        bool instanced;

        void Start()
        {
            if (!Application.isPlaying) return;
            foreach (var L in lanes) foreach (var c in L.cars)
            {
                var mr = c.t.GetComponent<MeshRenderer>(); var mf = c.t.GetComponent<MeshFilter>();
                if (!mr || !mf || !mr.sharedMaterial || !mr.sharedMaterial.enableInstancing) continue;
                batchMat[mf.sharedMesh] = mr.sharedMaterial; mr.enabled = false;
            }
            instanced = batchMat.Count > 0;
        }

        void Update()
        {
            Step(Time.deltaTime);
            if (!instanced) return;
            foreach (var l in batches.Values) l.Clear();
            foreach (var L in lanes) foreach (var c in L.cars)
            {
                if (c.hidden || c.t.localScale.x < 0.01f) continue;
                var mesh = c.t.GetComponent<MeshFilter>().sharedMesh;
                if (!batchMat.ContainsKey(mesh)) continue;
                if (!batches.TryGetValue(mesh, out var list)) batches[mesh] = list = new List<Matrix4x4>();
                list.Add(c.t.localToWorldMatrix);
            }
            foreach (var kv in batches)
            {
                if (kv.Value.Count == 0) continue;
                var rp = new RenderParams(batchMat[kv.Key]) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = true, layer = gameObject.layer };
                Graphics.RenderMeshInstanced(rp, kv.Key, 0, kv.Value);
            }
        }

        /// <summary>11-11 用户：要能捏起路上的车 —— 在 p 附近 r 内摘下一辆（这辆车离开车流，过半分钟换一辆新的进场），返回它的网格 / 材质 / 世界矩阵</summary>
        public bool TryTake(Vector3 p, float r, out Mesh mesh, out Material mat, out Matrix4x4 m)
        {
            mesh = null; mat = null; m = default; Car best = null; float bd = float.MaxValue;
            foreach (var L in lanes) foreach (var c in L.cars)
            {
                if (c.hidden || c.t.localScale.x < 0.5f) continue;
                float d = Vector3.Distance(c.t.position, p) - c.len * scale * 0.35f;
                if (d < r && d < bd) { bd = d; best = c; }
            }
            if (best == null) return false;
            var mf = best.t.GetComponent<MeshFilter>(); var mr = best.t.GetComponent<MeshRenderer>();
            if (!mf || !mr) return false;
            mesh = mf.sharedMesh; mat = mr.sharedMaterial; m = best.t.localToWorldMatrix;
            best.hidden = true; best.wait = R(25f, 40f); best.t.localScale = Vector3.zero;
            return true;
        }
        /// <summary>普通轿车的世界长度（追兵里的警车 / 皮卡等按它统一大小）</summary>
        public float TypicalLen()
        {
            var l = new List<float>(); foreach (var L in lanes) foreach (var c in L.cars) if (c.len > 0) l.Add(c.len);
            if (l.Count == 0) return 0; l.Sort(); return l[l.Count / 2] * scale;
        }

        float R(float lo, float hi) => lo + (float)rnd.NextDouble() * (hi - lo);

        float haltT; const float HaltSpread = 5.5f;
        public void Step(float dt)
        {
            time += dt;
            if (Halt) { haltT += dt; if (haltT > HaltSpread + 3.5f) { if (sigUs != null && Time.frameCount % 6 == 0) for (int g = 0; g < sigUs.Length; g++) { float tg = time + GroupOffset(g); sigUs[g].SetColor("_Color", Light(true, tg)); sigVs[g].SetColor("_Color", Light(false, tg)); } return; } } else haltT = 0;   // 都停稳了：只让红绿灯接着变
            if (sigUs != null) for (int g = 0; g < sigUs.Length; g++) { float tg = time + GroupOffset(g); sigUs[g].SetColor("_Color", Light(true, tg)); sigVs[g].SetColor("_Color", Light(false, tg)); }
            foreach (var L in lanes)
            {
                L.cars.Sort((x, y) => y.s.CompareTo(x.s));
                Car front = null;
                for (int i = 0; i < L.cars.Count; i++)
                {
                    var c = L.cars[i];
                    if (c.hidden)
                    {
                        // 在起点外排队：时间到、起点空出来才进场
                        c.wait -= dt;
                        float tail = float.MaxValue; foreach (var o in L.cars) if (o != c && !o.hidden) tail = Mathf.Min(tail, o.s - o.len * 0.5f);
                        // 11-13 用户：星级越高，普通车越少（城里戒严了）—— 每辆车有个固定的序号，序号超过 Keep 的开出去以后就不再回来；星级降了再陆续回来
                        if (c.rank < 0) c.rank = R(0f, 1f);
                        if (!Halt && c.wait <= 0 && c.rank <= Keep && tail - L.s0 > c.len + Gap * 3) { c.hidden = false; c.s = L.s0; c.v = c.cruise * vmax * 0.6f; c.stopAt = -1; }
                        else { c.t.localScale = Vector3.zero; continue; }
                    }
                    float limit = float.MaxValue;
                    if (front != null) limit = front.s - (front.len + c.len) * 0.5f - Gap * c.gap;
                    // 红灯（和不敢冲的黄灯）：停在下一个停止线前
                    foreach (var (st, g) in L.stopList)
                    {
                        float stop = st - c.len * 0.5f;
                        if (c.s > stop + 0.002f) continue;
                        float tg = time + GroupOffset(g);
                        bool go = Go(L.alongU, tg);
                        if (!go && c.bold && Yellowish(L.alongU, tg) && stop - c.s < 0.03f) go = true;   // 冲黄灯：离线很近才冲
                        if (!go) limit = Mathf.Min(limit, stop);
                        break;
                    }
                    // 偶尔临时靠边停一会儿（不在路口里）
                    if (c.stopAt < 0 && R(0, 1) < dt * 0.012f)
                    {
                        float at = c.s + R(0.06f, 0.2f);
                        bool nearX = false; foreach (var st in L.stops) if (Mathf.Abs(at - st) < 0.14f) nearX = true;
                        if (!nearX && at < L.s1 - 0.08f) { c.stopAt = at; c.stopT = R(1.5f, 5f); }
                    }
                    if (c.stopAt >= 0)
                    {
                        if (c.s >= c.stopAt - 0.002f && c.v < 0.002f) { c.stopT -= dt; if (c.stopT <= 0) c.stopAt = -1; }
                        if (c.stopAt >= 0) limit = Mathf.Min(limit, c.stopAt);
                    }
                    float want = c.cruise * vmax * (0.92f + 0.08f * Mathf.Sin(time * 0.7f + c.len * 97));   // 巡航时速度轻微起伏
                    float target = Mathf.Clamp((limit - c.s) * 3f, 0, want);
                    if (c.rank < 0) c.rank = R(0f, 1f);
                    if (Halt && haltT > c.rank * HaltSpread) { c.v = Mathf.MoveTowards(c.v, 0, vmax * (0.42f + 0.25f * c.gap) * dt); c.stopAt = -1; }   // 轮到它了：1.4～2.2 秒刹停
                    else
                    c.v = target < c.v ? target : Mathf.MoveTowards(c.v, target, c.accel * dt);
                    c.s = Mathf.Min(c.s + c.v * dt, Mathf.Max(c.s, limit));
                    if (c.s > L.s1)
                    {
                        // 开出去了：隔一段随机时间再进场（冷清的车道等得更久）
                        c.hidden = true; c.wait = R(0.5f, 6f) / Mathf.Max(0.2f, L.density); c.t.localScale = Vector3.zero;
                        continue;
                    }
                    Place(L, c);
                    front = c;
                }
            }
        }

        void Place(Lane L, Car c)
        {
            float k = Mathf.Clamp01((c.s - L.s0) / 0.04f) * Mathf.Clamp01((L.s1 - c.s) / 0.04f);
            c.t.SetPositionAndRotation(pivot + (L.p0 + L.dir * c.s - pivot) * scale, Quaternion.LookRotation(L.dir, Vector3.up));
            c.t.localScale = Vector3.one * k;
        }
    }
}
