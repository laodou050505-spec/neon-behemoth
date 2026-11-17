// 暂停钮（11-12 用户：手心朝自己、抱拳两种手势都容易误触 / 难识别 → 不用手势，做成城市近角的一个实物按钮）：
// 主城区近边外、玩家正前方的桌沿上，一块横着的霓虹小控制台（深色底座 + 粉色按键 + 白色“Ⅱ”），位置由城市边界预留（见 Start）。
// 捏它（拇指食指在 6cm 内合上），或者手掌按上去（掌心在 4.5cm 内、往下有速度）→ 暂停并打开菜单。手靠近 10cm 时按钮变亮、微微鼓起。
using UnityEngine;

namespace Monster.Game
{
    public class PauseButton : MonoBehaviour
    {
        public MonsterGame game; public GameView view; public PalmMenu menu; public Zone zone;
        Transform root, cap; Material capM; float cd, near, hold, tap, capY; Vector3 topL;
        public Vector3 Pos => root ? root.TransformPoint(topL) : Vector3.zero;

        void Start()
        {
            root = new GameObject("PauseButton").transform;
            // 11-13 用户：暂停钮的位置不要和模型穿插 —— 上一版 5.6cm 的圆钮直接摆在城区近边上，压着路沿、步行道、路灯和串灯。
            // 现在城市边界生成时就在近边上留出一段（CityBuilder.pausePad：那一段没有串灯、没有立牌，离两头路灯至少 9cm），
            // 按钮做成一块横着的小控制台：深色底座骑在路沿上（比路沿高，路沿从底座里穿过去），整块在城区边线之外、不伸出桌沿 / 地垫
            var pad = view && view.builder != null ? view.builder.pausePad : null;
            bool fur = zone.furniture;
            float D = fur ? 0.052f : 0.035f, WX = fur ? 0.078f : 0.07f, H0 = CityBuilder.PadTop;
            if (pad.HasValue) root.position = pad.Value;
            else
            {
                // 兜底（近边是水面等没留位置的情况）：城区近边外、正对玩家
                var hd = Camera.main ? Camera.main.transform.position : zone.Center;
                float a0 = Mathf.Clamp(Vector3.Dot(hd - zone.origin, zone.u), 0.06f, zone.w - 0.06f);
                root.position = zone.Point(a0, zone.h + 0.036f, 0);
            }
            root.rotation = Quaternion.LookRotation(-zone.v, Vector3.up);   // 本地 −z 朝玩家
            float zc = -(0.0005f + D * 0.5f);
            var dark = CityMaterials.Solid("#231f30"); var rim = CityMaterials.Solid("#6a6f88");
            Part(CityMeshes.Box(), dark, new Vector3(0, 0, zc), new Vector3(WX, H0, D));
            Part(CityMeshes.Box(), rim, new Vector3(0, H0, zc), new Vector3(WX - 0.008f, 0.002f, D - 0.007f));
            capM = new Material(CityMaterials.Glow("#ff4fb8", 1.2f)) { name = "pauseCap" };
            capY = H0 + 0.002f;
            cap = Part(CityMeshes.Box(), capM, new Vector3(0, capY, zc), new Vector3(WX - 0.016f, 0.007f, D - 0.013f));
            var white = CityMaterials.Glow("#ffffff", 1.6f);
            foreach (float sx in new[] { -1f, 1f }) Part(CityMeshes.Box(), white, new Vector3(sx * 0.0055f, capY + 0.007f, zc), new Vector3(0.005f, 0.0012f, (D - 0.013f) * 0.6f)).SetParent(cap, true);
            topL = new Vector3(0, capY + 0.008f, zc);
        }
        Transform Part(Mesh m, Material mat, Vector3 pos, Vector3 size)
        {
            var go = new GameObject("p"); go.transform.SetParent(root, false); go.transform.localPosition = pos; go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
        void OnDestroy() { if (root) Destroy(root.gameObject); }

        void Update()
        {
            if (!root || game == null || view == null) return;
            bool live = !view.paused && game.result == null && MonsterMR.CityShown;
            if (root.gameObject.activeSelf != MonsterMR.CityShown) root.gameObject.SetActive(MonsterMR.CityShown);
            cd -= Time.deltaTime;
            float best = 9; bool press = false, holding = false, tapping = false; var top = root.TransformPoint(topL);
            if (live && game.lastHands != null)
                foreach (var h in game.lastHands)
                {
                    if (h == null || !h.present) continue;
                    float d = Mathf.Min(Vector3.Distance(h.pos, top), Vector3.Distance(h.palm, top));
                    best = Mathf.Min(best, d);
                    // 就在手边，拍楼时手经常从上面过：捏住它才立刻触发；用手掌按的话要按住 0.35 秒（按钮会慢慢沉下去、变亮）
                    if (h.pinch && Vector3.Distance(h.pos, top) < 0.055f) press = true;
                    if (Vector3.Distance(h.palm, top) < 0.045f && h.vel.magnitude < 0.25f) holding = true;
                    // 11-14 用户：手指点一下就行 —— 食指尖从上面点到按钮（指尖在按键上方 2.5cm 内、水平 3.2cm 内，而且指尖比掌心低 = 手指是朝下点的），
                    // 停住约 0.06 秒就触发。伸手进城里拍楼时手是平着从上面过去的（指尖不比掌心低）、速度也快，不会误触
                    if (h.nPts > 2 && !h.grip)
                    {
                        var tip = h.pts[2]; var dd = tip - top; float flat = new Vector2(dd.x, dd.z).magnitude;
                        if (flat < 0.032f && dd.y > -0.012f && dd.y < 0.025f && tip.y < h.palm.y - 0.012f && h.vel.magnitude < 0.7f) { tapping = true; best = Mathf.Min(best, 0.02f); }
                    }
                }
            hold = holding ? hold + Time.deltaTime : 0; if (hold >= 0.35f) press = true;
            tap = tapping ? tap + Time.deltaTime : 0; if (tap >= 0.06f) press = true;
            near = Mathf.MoveTowards(near, best < 0.1f ? 1 : 0, Time.deltaTime * 6);
            capM.SetColor("_Color", Color.Lerp(new Color(1f, 0.31f, 0.72f) * 1.2f, new Color(1f, 0.6f, 0.85f) * 2.4f, near));
            cap.localPosition = new Vector3(cap.localPosition.x, capY + 0.0015f * near - 0.0045f * Mathf.Max(Mathf.Clamp01(hold / 0.35f), Mathf.Clamp01(tap / 0.06f)), cap.localPosition.z);
            if (press && cd <= 0 && game.held.Count == 0) { cd = 1.2f; hold = 0; tap = 0; MonsterAudio.Play("ui_press", top, 0.9f); menu?.OpenFromButton(); }
        }
    }
}
