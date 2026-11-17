// 追兵表现层：每个追兵一个模型（Tools/blender_military.py，Resources/Modules/military/<类型>.fbx，部件按名字找）。
// 模型按碰撞半径定大小（看到多大就能拍中多大）；朝向 = 逻辑层的 fwd / up。部件动作：
//   rotor / rotor0..3 / tail_rotor 旋翼转；turret 炮塔转向 aim；barrel 跟着炮塔、开炮后坐；launcher 抬起；radar 转；
//   beacon 警灯红蓝 / 琥珀交替闪；机甲两腿交替迈步、举盾（guard）、被打晕（stagger）时摇晃。
// 被抓：跟着手；被扔：整台翻滚着飞（跟 game.flying 里 refId 一致的那个）；落地 / 被打掉：交给 GameView 变成翻滚的碎片。
using System.Collections.Generic;
using UnityEngine;

namespace Monster.Game
{
    public class EnemyView : MonoBehaviour
    {
        public MonsterGame game; public GameView view; public float cityScale = 1;
        // 11-12：体型拉开（原来 1.0～1.4，远看都像警车）：警车 = 轿车，皮卡底盘 1.3，装甲车 1.5，坦克 1.8
        static readonly Dictionary<string, float> CarFactor = new() { ["police"] = 1.0f, ["repair"] = 1.1f, ["rocket"] = 1.3f, ["sam"] = 1.3f, ["apc"] = 1.5f, ["tank"] = 1.8f };
        static readonly Dictionary<string, string> CivBody = new() { ["police"] = "sedan", ["repair"] = "van", ["rocket"] = "pickup", ["sam"] = "pickup" };
        /// <summary>路上某一款私家车（sedan / van / pickup）的世界长度；没有这一款就 0</summary>
        float CivBodyLen(string kind)
        {
            foreach (var t in Object.FindObjectsByType<Monster.Traffic>(FindObjectsSortMode.None))
            {
                if (t.vmax < Monster.Traffic.Vmax * 0.9f) continue;
                foreach (var L in t.lanes) foreach (var c in L.cars) { var mf = c.t ? c.t.GetComponent<MeshFilter>() : null; if (mf && mf.sharedMesh && mf.sharedMesh.name.Contains(kind) && c.len > 0) return c.len * t.scale; }
            }
            return 0;
        }
        float civLen = -1;
        float CivLen()
        {
            if (civLen >= 0) return civLen;
            civLen = 0;
            foreach (var t in Object.FindObjectsByType<Monster.Traffic>(FindObjectsSortMode.None)) { if (t.vmax < Monster.Traffic.Vmax * 0.9f) continue; float l = t.TypicalLen(); if (l > 0) { civLen = l; break; } }
            return civLen;
        }
        class V { public Transform root, body, turret, barrel, launcher, radar, beacon, legL, legR, armL, armR, shield, torso; public readonly List<(Transform t, Vector3 axis, float speed)> spin = new(); public Vector3 barrelPos, legLp; public Quaternion turretRot, barrelRot, launcherRot, legLr, legRr, armLr, armRr, shieldRot; public float walk; public Enemy e; public Vector3 lastPos; public bool flying; public MaterialPropertyBlock mpb; public Renderer beaconR; public Transform launchYaw; public Vector3 muzzleL, muzzleAx; public MechRig mech; }
        readonly Dictionary<int, V> views = new();
        readonly Dictionary<string, GameObject> prefabs = new();
        readonly Dictionary<string, Material> mats = new();
        readonly Dictionary<string, float> sizeOf = new();
        static readonly Dictionary<string, string> Livery = new()
        {
            ["police"] = "#f2f2ee", ["repair"] = "#f2c53a", ["apc"] = "#3b4a5c", ["tank"] = "#34404f", ["rocket"] = "#3b4a5c", ["sam"] = "#3b4a5c",
            ["boat"] = "#e9ecef", ["heli"] = "#2a3446", ["drone"] = "#22252c", ["jet"] = "#5a6472", ["bomber"] = "#2d323b", ["mech"] = "#4e6c98",   // 机甲：钢蓝（原来 #2c3a52 太暗，夜里一团黑）
        };

        Material Mat(string type)
        {
            if (mats.TryGetValue(type, out var m)) return m;
            var pal = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "enemy_pal_" + type };
            var emi = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            string[] cols = { Livery[type], "#232c48", "#ff2d8a", "#4a4f57", "#26282d", "#ffb020", "#c3c8cf", "#5b6340", "#f2c53a", "#1d2a3a", "#16161a", "#fff3c8", "#ff2a3a", "#2a7bff", "#2a2a30", "#c9ccd2" };
            for (int i = 0; i < 16; i++) { pal.SetPixel(i, 0, CityMaterials.Hex(cols[i])); emi.SetPixel(i, 0, Color.black); }
            foreach (int i in new[] { 5, 11, 12, 13 }) emi.SetPixel(i, 0, CityMaterials.Hex(cols[i]));
            emi.SetPixel(2, 0, CityMaterials.Hex("#ff2d8a") * 0.35f);   // 洋红条纹微微发光（霓虹集团的涂装）
            pal.Apply(); emi.Apply();
            m = new Material(CityMaterials.Toon) { name = "enemy_" + type };
            m.SetTexture("_BaseMap", pal); m.SetTexture("_EmissionMap", emi);
            m.SetColor("_EmissionColor", new Color(2.2f, 2.2f, 2.2f)); m.SetFloat("_NightGlow", 1);
            m.SetFloat("_OutlineWidth", 0.0011f);
            if (type == "mech")
            {
                // 机甲的发光件多几样：胸口反应堆（琥珀）、目镜（红）、盾缘能量边（蓝）都很亮；洋红识别条、黄黑警示带微微亮，夜里也看得清涂装
                emi.SetPixel(5, 0, CityMaterials.Hex("#ffb020") * 1.1f); emi.SetPixel(12, 0, CityMaterials.Hex("#ff2a3a")); emi.SetPixel(13, 0, CityMaterials.Hex("#40c8ff"));
                emi.SetPixel(2, 0, CityMaterials.Hex("#ff2d8a") * 0.5f); emi.SetPixel(8, 0, CityMaterials.Hex("#f2c53a") * 0.22f); emi.SetPixel(9, 0, CityMaterials.Hex("#40c8ff") * 0.3f);
                emi.Apply();
                m.SetFloat("_OutlineWidth", 0.0013f);
            }
            return mats[type] = m;
        }

        V Make(Enemy e)
        {
            if (!prefabs.TryGetValue(e.type, out var pf)) prefabs[e.type] = pf = Resources.Load<GameObject>("Modules/military/" + e.type);
            if (!pf) return null;
            var v = new V { e = e, mpb = new MaterialPropertyBlock() };
            v.root = new GameObject("Enemy_" + e.type + "_" + e.id).transform; v.root.SetParent(transform, false);
            var inst = Instantiate(pf, v.root, false).transform;
            inst.localRotation = Quaternion.Euler(0, 180, 0);   // 模型车头朝 −Z（FBX），转成 +Z
            var mat = Mat(e.type);
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>()) { r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }
            Transform F(string n) { foreach (var t in inst.GetComponentsInChildren<Transform>()) if (t.name == n) return t; return null; }
            v.body = F("body") ?? F("torso"); v.turret = F("turret"); v.barrel = F("barrel"); v.launcher = F("launcher"); v.radar = F("radar"); v.beacon = F("beacon");
            v.legL = F("leg_l"); v.legR = F("leg_r"); v.armL = F("arm_l"); v.armR = F("arm_r"); v.shield = F("shield"); v.torso = F("torso");
            if (e.type == "mech") v.mech = MechRig.Build(v.root, inst);   // 机甲有自己的骨架和动作（MechRig），下面这些通用的部件逻辑不走
            if (v.mech != null) { v.legL = null; v.shield = null; v.armL = null; }
            if (v.barrel && v.turret) v.barrel.SetParent(v.turret, true);     // 炮管跟炮塔一起转
            if (v.shield && v.armL) v.shield.SetParent(v.armL, true);
            if (v.turret) v.turretRot = v.turret.localRotation; if (v.barrel) { v.barrelPos = v.barrel.localPosition; v.barrelRot = v.barrel.localRotation; }
            if (v.launcher)
            {
                v.launcherRot = v.launcher.localRotation;
                // 11-10：发射箱下面加一个转台（运行时建的空节点，放在箱子平躺时的中心下面），锁定时先转向目标再抬起
                var mf = v.launcher.GetComponent<MeshFilter>();
                var lb = mf && mf.sharedMesh ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                var yaw = new GameObject("launchYaw").transform;
                yaw.SetParent(v.launcher.parent, false);
                var c = v.launcher.parent.InverseTransformPoint(v.launcher.TransformPoint(lb.center));
                yaw.localPosition = new Vector3(c.x, v.launcher.localPosition.y, c.z); yaw.localRotation = Quaternion.identity;
                v.launcher.SetParent(yaw, true);
                v.launchYaw = yaw;
                // 管口：箱子最长那根轴上离铰链远的那一端的面中心
                var sz = lb.size; int ax = sz.x >= sz.y && sz.x >= sz.z ? 0 : sz.y >= sz.z ? 1 : 2;
                var axis = Vector3.zero; axis[ax] = Mathf.Sign(lb.center[ax]);
                v.muzzleAx = axis; v.muzzleL = lb.center + Vector3.Scale(axis, sz) * 0.5f;
            }
            if (v.legL) { v.legLr = v.legL.localRotation; v.legRr = v.legR.localRotation; v.armLr = v.armL.localRotation; v.armRr = v.armR.localRotation; }
            if (v.beacon) v.beaconR = v.beacon.GetComponent<Renderer>();
            foreach (var n in new[] { "rotor", "rotor0", "rotor1", "rotor2", "rotor3" }) { var t = F(n); if (t) v.spin.Add((t, Vector3.up, n == "rotor" ? 1400 : 2200)); }
            var tr = F("tail_rotor"); if (tr) v.spin.Add((tr, Vector3.right, 2400));
            if (v.radar) v.spin.Add((v.radar, Vector3.up, 90));
            // 大小：模型水平尺寸 ≈ 碰撞直径 × 1.15
            if (!sizeOf.TryGetValue(e.type, out var k))
            {
                var b = new Bounds(inst.position, Vector3.zero); bool first = true;
                foreach (var r in inst.GetComponentsInChildren<MeshRenderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                float ext = Mathf.Max(b.size.x, b.size.z);
                sizeOf[e.type] = k = ext > 1e-4f ? Enemies.R(e) * 2.3f / ext : 0.0075f;
                // 11-11 用户：车要统一大小（警车比路上的车大很多）—— 地面车辆按路上普通轿车的长度定：警车 = 轿车，
                // 维修车 / 导弹车（皮卡底盘）稍长，装甲车 / 防空车 / 坦克再大一点（军用的本来就更大），船和飞的不变
                float carLen = CivLen();
                if (carLen > 0 && ext > 1e-4f && CarFactor.TryGetValue(e.type, out var cf)) sizeOf[e.type] = k = carLen * cf / ext;
                // 11-17 用户：武装车辆可以大，但警车这类的和私家车一样正常大小就行。实测警车车身 4.07cm、私家轿车 3.99cm（本来就差不多），
                // 偏大的是皮卡底盘的导弹车 / 防空车（5.30cm，同款民用皮卡 4.58cm）和面包车底盘的维修车。
                // 规则改成：凡是用民用车底盘的（警车 = 轿车、维修车 = 面包车、导弹车 / 防空车 = 皮卡），车身和路上同款私家车一样长；装甲车、坦克、机甲照旧
                if (CivBody.TryGetValue(e.type, out var civKind) && v.body && v.body.GetComponent<Renderer>())
                {
                    float civ = CivBodyLen(civKind); var bb = v.body.GetComponent<Renderer>().bounds; float bodyLen = Mathf.Max(bb.size.x, bb.size.z);
                    if (civ > 0 && bodyLen > 1e-4f) sizeOf[e.type] = k = civ / bodyLen;
                }
                // 机甲按“马路有多宽”定大小（11-13：原来 12.8cm 宽，比马路还宽，走起来半个身子在楼里）：肩宽 = 两排楼之间空隙的八成，站直约 9cm 高，比多数楼矮一头
                if (e.type == "mech" && e.zone != null && ext > 1e-4f)
                {
                    float gap = 0.05f * cityScale;
                    foreach (var bd in game.city.buildings) { if (bd.zone != e.zone || bd.kind != BuildingKind.House) continue; gap = e.zone.cell * 3 - Mathf.Max(bd.fw, bd.fd); break; }
                    sizeOf[e.type] = k = Mathf.Clamp(gap * 0.8f, 0.03f, 0.075f) / ext;
                }
            }
            v.root.localScale = Vector3.one * k;
            v.lastPos = e.pos;
            // 持续声：直升机旋翼、警车警笛（3D，听得出方向；音量小，不吵）
            string loop = e.type == "heli" ? "loop_rotor" : e.type == "police" ? "loop_siren" : null;
            if (loop != null)
            {
                var a = v.root.gameObject.AddComponent<AudioSource>();
                a.clip = Resources.Load<AudioClip>("Audio/" + loop); a.loop = true; a.spatialBlend = 1; a.rolloffMode = AudioRolloffMode.Logarithmic;
                a.minDistance = 0.3f; a.maxDistance = 5; a.dopplerLevel = 0; a.volume = e.type == "heli" ? 0.5f : 0.18f; a.pitch = Random.Range(0.95f, 1.05f);
                a.time = Random.Range(0, a.clip ? a.clip.length : 0); a.Play();
            }
            return v;
        }

        // 11-13：撤离（降星时的撤退、胜利后的全员撤离）以前和“被打掉”走同一条路，整台变成翻滚的残骸，像是凭空炸了。
        // 现在撤离有自己的过程：飞的调头、加速飞远、越飞越小；地上的原地停住，头顶落下一道青色光柱，被吊起来收走，地上留一圈光环
        readonly HashSet<int> leaving = new();
        class Retreat { public V v; public float t; public Vector3 dir, p0, s0; public bool fly; }
        readonly List<Retreat> retreats = new();
        void Start() { if (view) { view.onLeave = id => leaving.Add(id); view.onEnemyEvent = ev => { if (views.TryGetValue(ev.id, out var mv) && mv.mech != null) mv.mech.OnEvent(ev, mv.e, view); }; } }
        readonly List<MechRig> dyingMechs = new(); readonly HashSet<int> seenBuf = new(); readonly List<int> goneBuf = new();
        void UpdateRetreats(float dt)
        {
            for (int i = retreats.Count - 1; i >= 0; i--)
            {
                var R = retreats[i]; R.t += dt; var tr = R.v.root;
                if (!tr) { retreats.RemoveAt(i); continue; }
                foreach (var (sp, ax, w) in R.v.spin) if (sp) sp.Rotate(ax, w * dt, Space.Self);
                float life = R.fly ? 1.6f : 1.15f;
                if (R.fly)
                {
                    var vel = (R.dir + Vector3.up * 0.45f).normalized * (0.25f + 1.7f * R.t);
                    tr.position += vel * dt;
                    tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.LookRotation(new Vector3(vel.x, vel.y * 0.3f, vel.z).normalized, Vector3.up), 1 - Mathf.Exp(-dt * 5));
                    tr.localScale = R.s0 * Mathf.Clamp01((life - R.t) / 0.45f);
                }
                else
                {
                    // 0～0.35s 光柱落下；0.35～1.15s 吊起来、缩小
                    float k = Mathf.Clamp01((R.t - 0.35f) / (life - 0.35f)); k = k * k;
                    tr.position = R.p0 + Vector3.up * (0.14f * k);
                    tr.localScale = R.s0 * (1 - k);
                    float grow = Mathf.Clamp01(R.t / 0.3f), fade = Mathf.Clamp01((life - R.t) / 0.25f);
                    view.FxStreak(R.p0 + Vector3.up * (0.2f - 0.1f * grow), 0.012f, 0.2f * grow, new Color(0.45f, 1f, 0.95f, 0.55f * fade));
                    view.FxRing(R.p0 + Vector3.up * 0.003f, 0.018f + 0.01f * Mathf.Sin(R.t * 9), new Color(0.45f, 1f, 0.95f, 0.8f * fade));
                }
                if (R.t >= life) { Destroy(tr.gameObject); retreats.RemoveAt(i); }
            }
        }

        void LateUpdate()
        {
            if (game == null) return;
            Perf.Begin("追兵画面");
            float dt = Time.deltaTime, t = Time.time;
            if (view && !view.paused) UpdateRetreats(dt);
            bool fx = view && !view.paused;
            for (int i = dyingMechs.Count - 1; i >= 0; i--) if (dyingMechs[i].UpdateDeath(view, fx ? dt : 0, t, fx)) dyingMechs.RemoveAt(i);
            var seen = seenBuf; seen.Clear();
            foreach (var e in game.enemies)
            {
                if (e.dead && e.state != EState.Held) continue;
                seen.Add(e.id);
                if (!views.TryGetValue(e.id, out var v)) { v = Make(e); if (v == null) continue; views[e.id] = v; }
                // 地面军车开到停着的车跟前：重型的碾扁，轻型的顶飞（见 GameView.RamCars）
                if (fx && e.state == EState.Move && Enemies.Types.TryGetValue(e.type, out var et) && et.ground && !et.fixedPos && !(e.type == "mech" && (e.dropT > 0 || e.air || e.hop > 0)))
                {
                    float er = Enemies.R(e); var ef = e.fwd.sqrMagnitude > 1e-6f ? e.fwd : Vector3.forward;
                    view.RamCars(e.pos + ef * (er * 0.45f), er * 0.6f, ef, e.type is "tank" or "apc" or "rocket" or "mech");
                }
                if (v.mech != null) { v.mech.Pose(e, game, view, fx ? dt : 0, t, fx); v.lastPos = e.pos; continue; }
                Pose(v, e, dt, t);
            }
            // 被扔出去的：跟着 flying 里同一个 refId 翻滚着飞
            foreach (var f in game.flying)
            {
                if (f.refId < 0 || !views.TryGetValue(f.refId, out var v)) continue;
                seen.Add(f.refId); v.flying = true;
                v.root.position = f.pos;
                v.root.rotation = Quaternion.Euler(f.spin * (dt * Mathf.Rad2Deg)) * v.root.rotation;
                foreach (var (tr, ax, sp) in v.spin) tr.Rotate(ax, sp * dt, Space.Self);
            }
            // 没了的：交给 GameView 变成碎片（整台翻滚着落地，几秒后缩没）
            var gone = goneBuf; gone.Clear();
            foreach (var kv in views) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone)
            {
                var v = views[id]; views.Remove(id);
                if (v.mech != null && !leaving.Contains(id)) { v.mech.BeginDeath(view); dyingMechs.Add(v.mech); continue; }   // 机甲被击毁：先演失控、跪下，最后才炸开
                if (leaving.Remove(id))
                {
                    bool fly = Enemies.Types.TryGetValue(v.e.type, out var lt) && lt.fly;
                    var away = v.root.position - game.headPos; away.y = 0; away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward;
                    retreats.Add(new Retreat { v = v, fly = fly, dir = away, p0 = v.root.position, s0 = v.root.localScale });
                    var au = v.root.GetComponent<AudioSource>(); if (au && !fly) au.Stop();
                    continue;
                }
                var vel = (v.root.position - v.lastPos) / Mathf.Max(dt, 1e-3f);
                view.AddWreck(v.root, vel * 0.5f + Vector3.up * 0.15f);
            }
            Perf.End();
        }

        void Pose(V v, Enemy e, float dt, float t)
        {
            var p = e.pos;
            float spd = (p - v.lastPos).magnitude / Mathf.Max(dt, 1e-4f);
            v.lastPos = p;
            v.root.position = p;
            var fwd = e.fwd.sqrMagnitude > 1e-6f ? e.fwd : Vector3.forward; var up = e.up.sqrMagnitude > 1e-6f ? e.up : Vector3.up;
            if (e.state == EState.Held) { v.root.rotation = Quaternion.Slerp(v.root.rotation, Quaternion.LookRotation(fwd, up) * Quaternion.Euler(Mathf.Sin(t * 9) * 10, 0, Mathf.Sin(t * 7) * 12), 1 - Mathf.Exp(-dt * 10)); }
            else v.root.rotation = Quaternion.LookRotation(fwd, up);
            if (e.stagger > 0) v.root.rotation *= Quaternion.Euler(Mathf.Sin(t * 25) * 6, 0, Mathf.Sin(t * 19) * 8);
            foreach (var (tr, ax, sp) in v.spin) tr.Rotate(ax, sp * dt, Space.Self);
            // 炮塔转向目标（只绕炮塔自己的竖轴），炮管后坐
            if (v.turret && e.aim.HasValue)
            {
                var parent = v.turret.parent;
                var local = parent.InverseTransformDirection(e.aim.Value - v.turret.position); local.y = 0;
                if (local.sqrMagnitude > 1e-8f)
                {
                    var want = Quaternion.LookRotation(-local.normalized, Vector3.up);   // 模型前方是本地 −Z
                    v.turret.localRotation = Quaternion.RotateTowards(v.turret.localRotation, want, 120 * dt);
                }
            }
            if (v.barrel) v.barrel.localPosition = v.barrelPos + new Vector3(0, 0, e.recoil * 0.35f);   // 往后坐（本地 +Z 是车尾）
            if (v.launcher)
            {
                // 转台：锁定时转向目标（模型前方是本地 −Z），没锁定时慢慢转回正前
                if (v.launchYaw)
                {
                    var want = Quaternion.identity;
                    if (e.launcher > 0.02f && e.aim.HasValue)
                    {
                        var local = v.launchYaw.parent.InverseTransformDirection(e.aim.Value - v.launchYaw.position); local.y = 0;
                        if (local.sqrMagnitude > 1e-8f) want = Quaternion.LookRotation(-local.normalized, Vector3.up);
                    }
                    v.launchYaw.localRotation = Quaternion.RotateTowards(v.launchYaw.localRotation, want, 90 * dt);
                }
                // 抬起：绕车尾铰链往上 55°（11-10 前符号反了，箱子往下插进车斗和地面）
                v.launcher.localRotation = v.launcherRot * Quaternion.Euler(Mathf.Clamp01(e.launcher) * 55, 0, 0);
                e.muzzle = v.launcher.TransformPoint(v.muzzleL);
                e.muzzleDir = v.launcher.TransformDirection(v.muzzleAx).normalized;
            }
            // 警灯
            if (v.beaconR)
            {
                bool on = Mathf.Repeat(t * 5 + e.id * 0.37f, 1) < 0.5f;
                v.mpb.SetColor("_EmissionColor", on ? new Color(3.2f, 3.2f, 3.2f) : new Color(0.4f, 0.4f, 0.4f));
                v.beaconR.SetPropertyBlock(v.mpb);
            }
            // 机甲：迈步、举盾
            if (v.legL)
            {
                v.walk += spd / Mathf.Max(0.001f, Enemies.R(e)) * dt * 1.6f;
                float s = Mathf.Sin(v.walk) * (spd > 0.002f ? 22 : 0);
                v.legL.localRotation = v.legLr * Quaternion.Euler(s, 0, 0); v.legR.localRotation = v.legRr * Quaternion.Euler(-s, 0, 0);
                v.armR.localRotation = v.armRr * Quaternion.Euler(-s * 0.6f - (e.recoil > 0 ? 25 * e.recoil : 0), 0, 0);
                v.armL.localRotation = v.armLr * Quaternion.Euler(e.guard > 0 ? -70 : s * 0.6f, 0, e.guard > 0 ? 15 : 0);
            }
        }
    }
}
