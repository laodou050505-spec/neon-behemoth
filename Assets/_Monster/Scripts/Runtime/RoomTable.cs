// 真实房间里的桌子（11-09 用户：我面前明明有桌子，为什么识别不了 —— 之前根本没开场景识别）。
// 读 PICO 系统“空间扫描”里标成 Table 的三维框（PXR_MixedReality 场景识别，做法照 TwilightKeys/TKRoomTable，真机验证过），
// 只存锚点的原始数据；要用时再按当时的追踪空间（XR Origin，选关后会重新对齐朝向）换算成世界坐标下的桌面。
// 玩家没在系统里扫描过房间 / 没标桌子 / 没给权限时 Found = false，城市退回虚拟桌。
using System.Collections.Generic;
using ByteDance.PICO.XR;
using UnityEngine;

namespace Monster
{
    public class RoomTable : MonoBehaviour
    {
        public struct Table
        {
            public Vector3 center;        // 桌面中心（世界坐标，y = 桌面高度）
            public Vector3 axisA, axisB;  // 桌面两根水平轴（单位向量）
            public float halfA, halfB;    // 两个方向的半宽
        }
        struct Raw { public Vector3 anchorPos, offset, extent; public Quaternion anchorRot; }

        readonly List<Raw> raws = new();
        public static RoomTable Instance { get; private set; }
        /// <summary>每次读完桌子 +1（标题页据此刷新“认没认出桌子”的提示）</summary>
        public int Version { get; private set; }
        public bool Scanning { get; private set; }
        public int Count => raws.Count;
        bool providerOn;
        public bool Done { get; private set; }
        public bool Found => raws.Count > 0;
        public string Status { get; private set; } = "未开始";

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>11-10 用户：开局先提一下桌子，并能直接跳到系统的“房间扫描”——
        /// 拉起 PICO 系统的空间扫描应用（StartSceneCaptureAsync），玩家扫完 / 标好桌子回到游戏后重新读一遍</summary>
        public async void Scan()
        {
            if (Scanning) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            Scanning = true; Status = "正在打开系统房间扫描";
            try
            {
                if (!providerOn) { var r0 = await PXR_MixedReality.StartSenseDataProvider(PxrSenseDataProviderType.SceneCapture); providerOn = r0 == PxrResult.SUCCESS; }
                var r = await PXR_MixedReality.StartSceneCaptureAsync();
                Debug.Log("[怪兽][桌子] 系统房间扫描返回 " + r);
            }
            catch (System.Exception e) { Debug.Log("[怪兽][桌子] 打开房间扫描异常 " + e.Message); }
            Scanning = false;
            Load();
#else
            Debug.Log("[怪兽][桌子] 编辑器里没有系统房间扫描");
            Version++;
#endif
        }

        // 从系统扫描（或别的应用）切回来：重新读一遍桌子
        void OnApplicationFocus(bool focus)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (focus && Done && !Scanning) Load();
#endif
        }

        void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Load();
#else
            Done = true; Status = "编辑器里不读";
#endif
        }

        bool loading;
        async void Load()
        {
            if (loading) return; loading = true;
            try
            {
                if (!providerOn)
                {
                    var r0 = await PXR_MixedReality.StartSenseDataProvider(PxrSenseDataProviderType.SceneCapture);
                    if (r0 != PxrResult.SUCCESS) { Status = "场景识别启动失败 " + r0; return; }
                    providerOn = true;
                }
                var r = await PXR_MixedReality.QuerySceneAnchorAsync(new[] { PxrSemanticLabel.Table });
                if (r.result != PxrResult.SUCCESS) { Status = "查询桌子失败 " + r.result; return; }
                raws.Clear();
                foreach (var h in r.anchorHandleList)
                {
                    if (PXR_MixedReality.GetSceneBox3DData(h, out var pos, out _, out var ext) != PxrResult.SUCCESS) continue;
                    if (PXR_MixedReality.LocateAnchor(h, out var ap, out var ar) != PxrResult.SUCCESS) continue;
                    raws.Add(new Raw { anchorPos = ap, anchorRot = ar, offset = pos, extent = ext });
                }
                Status = $"系统扫描里有 {raws.Count} 张桌子";
            }
            catch (System.Exception e) { Status = "场景识别异常 " + e.Message; }
            finally { loading = false; Done = true; Version++; Debug.Log("[怪兽][桌子] " + Status); }
        }

        /// <summary>玩家前方（±70°）、2m 以内、桌面高 0.45～1.25m、不小于 0.4×0.3m 的桌子里离玩家最近的一张</summary>
        public bool TryFront(Transform space, Vector3 head, Vector3 forward, out Table t)
        {
            t = default; float best = float.MaxValue; bool ok = false;
            forward.y = 0; forward.Normalize();
            foreach (var raw in raws)
            {
                var tb = ToWorld(space, raw);
                var d = tb.center - head; d.y = 0;
                if (d.magnitude > 2f || (d.sqrMagnitude > 1e-4f && Vector3.Angle(forward, d) > 70f)) continue;
                if (tb.center.y < 0.45f || tb.center.y > 1.25f) continue;
                if (Mathf.Max(tb.halfA, tb.halfB) < 0.3f || Mathf.Min(tb.halfA, tb.halfB) < 0.225f) continue;   // 太窄（<0.45m 深）摆不出像样的城市：退回虚拟桌
                if (d.magnitude < best) { best = d.magnitude; t = tb; ok = true; }
            }
            return ok;
        }

        static Table ToWorld(Transform sp, Raw r)
        {
            Vector3 W(Vector3 p) => sp ? sp.TransformPoint(p) : p;
            Vector3 D(Vector3 d) => sp ? sp.TransformDirection(d) : d;
            var c = r.anchorPos + r.anchorRot * r.offset;
            var ax = new[] { r.anchorRot * Vector3.right, r.anchorRot * Vector3.up, r.anchorRot * Vector3.forward };
            var half = new[] { r.extent.x * 0.5f, r.extent.y * 0.5f, r.extent.z * 0.5f };
            // 竖直轴 = 和世界上方向最接近的那根；另外两根是桌面的水平轴
            int up = 0; float bestUp = -1;
            for (int i = 0; i < 3; i++) { float dd = Mathf.Abs(Vector3.Dot(D(ax[i]).normalized, Vector3.up)); if (dd > bestUp) { bestUp = dd; up = i; } }
            int a = (up + 1) % 3, b = (up + 2) % 3;
            float topY = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                var p = c + ax[0] * half[0] * ((k & 1) == 0 ? -1 : 1) + ax[1] * half[1] * ((k & 2) == 0 ? -1 : 1) + ax[2] * half[2] * ((k & 4) == 0 ? -1 : 1);
                topY = Mathf.Max(topY, W(p).y);
            }
            var wc = W(c);
            static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.right; }
            return new Table { center = new Vector3(wc.x, topY, wc.z), axisA = Flat(D(ax[a])), axisB = Flat(D(ax[b])), halfA = half[a], halfB = half[b] };
        }

        /// <summary>
        /// 真桌子 → 城区：v 指向玩家（桌子离玩家最近的那条边是城市“前排”），u = v × 上；
        /// 四周各留 8cm 给城市边界（路沿 + 步行道 + 路灯），城区最大 1.6×0.8m（太大够不着），靠玩家那条边对齐
        /// </summary>
        public static Zone ToZone(Table t, Vector3 head)
        {
            const float Edge = 0.04f;   // 11-11：有城市地垫后四周只留 4cm（路沿 + 步行道），原来 8cm 让窄桌上的城市只剩一条
            var toP = head - t.center; toP.y = 0;
            bool aIsDepth = Mathf.Abs(Vector3.Dot(toP, t.axisA)) * t.halfB > Mathf.Abs(Vector3.Dot(toP, t.axisB)) * t.halfA;   // 玩家站在哪一侧
            var v = aIsDepth ? t.axisA * Mathf.Sign(Vector3.Dot(toP, t.axisA)) : t.axisB * Mathf.Sign(Vector3.Dot(toP, t.axisB));
            var u = Vector3.Cross(v, Vector3.up).normalized;
            float halfW = aIsDepth ? t.halfB : t.halfA, halfD = aIsDepth ? t.halfA : t.halfB;
            float w = Mathf.Min(1.6f, halfW * 2 - Edge * 2), h = Mathf.Min(head.y - t.center.y < 0.62f ? 0.6f : 0.8f, halfD * 2 - Edge * 2);   // 坐着（头离桌面不到 62cm）：城区最深 0.6m
            // 宽度方向居中对着玩家（玩家投影到桌边的位置，夹在桌子范围内）；深度方向贴着近边
            float pa = Mathf.Clamp(Vector3.Dot(head - t.center, u), -halfW + Edge + w / 2, halfW - Edge - w / 2);
            var nearMid = t.center + v * (halfD - Edge) + u * pa;   // 城区近边中点
            var origin = nearMid - u * (w / 2) - v * h;              // 远边、u 最小的角
            origin.y = t.center.y + 0.004f;   // 城市坐在 4mm 的城市地垫上（CityBuilder.BuildCityMat）
            return new Zone("table", ZoneKind.Table, origin, u, v, w, h) { furniture = false };
        }
    }
}
