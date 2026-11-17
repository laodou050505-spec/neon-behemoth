// 机甲（最终 BOSS）的骨架、动作和特效。11-13 用户：“最后的机器人效果太差，做精致一些”。
// 原来：一整根的腿前后摆、左臂举着一块板、没有任何特效，空降是凭空掉下来、被打 / 格挡 / 出破绽 / 被击毁画面上都没有反应。
// 现在（模型见 Tools/blender_military.py 的 mech()，16 个部件，原点都在关节上）：
//   · 骨架：胯 → 大腿 → 小腿 → 脚（膝、踝能弯）；上身（腰能转、能前倾）→ 头（盯着玩家）、两条上臂 → 前臂；塔盾挂在左拳上、自己保持竖直；
//   · 走路：按走过的距离迈步（脚不打滑），抬腿屈膝、脚掌放平、落脚时胯微微一沉，每一步脚下一小圈尘 + 一声闷响；
//   · 空降：落点先出现一个收拢的靶标（预警）→ 背上和脚底喷着火减速降下来 → 落地深蹲、冲击波 + 尘浪 → 站起来把盾举到身前；
//   · 开炮：站定、上身转向玩家、抬炮，炮口聚能（光点往里收、光球变大）→ 开火（炮口火光、烟、后坐）；
//   · 格挡：盾缘的能量边一亮、盾面一圈波纹、火花、“铛！”；破绽：盾垂下来、上身前倾、头上转着三颗星、关节冒火花；
//   · 挨打：全身白一下 + 火花，头顶的生命格掉一格；血少了背上冒烟；
//   · 击毁：失控冒火、一处处小爆炸、眼睛熄灭、跪下，胸口反应堆越来越亮 → 大爆炸，各部件分头飞出去摔成碎片。
// 不做频闪：所有明暗变化都在 2Hz 上下，白一下只有 0.2 秒一次。
using System.Collections.Generic;
using UnityEngine;

namespace Monster.Game
{
    public class MechRig
    {
        const float Thigh = 1.55f, Shin = 1.65f, Stride = 2.4f;   // 模型单位（和 blender_military.py 一致）
        public Transform root, inst, rig;
        Transform pelvis, torso, head, shield;
        readonly Transform[] leg = new Transform[2], shin = new Transform[2], foot = new Transform[2], arm = new Transform[2], fore = new Transform[2];
        readonly Dictionary<Transform, Quaternion> rest = new();
        Renderer eyeR, coreR, glowR; Renderer[] all; MaterialPropertyBlock mpb;
        float lu;                       // 一个模型单位在 inst 本地坐标里的长度
        Vector3 shieldRestPos, shieldGuardPos, muzzleL, muzzleAx; Quaternion shieldRelFore, shieldRelInst; float inSign = 1;
        // 姿态
        float phase, crouch, lean, guard, aimK, torsoYaw, headYaw, headPitch, moveW, armSh = 6, armEl = 10, armFix, recoilK;
        float hurtT, blockT, fireT, hpPop, sparkCd, smokeCd, beepCd, dustCd, dying = -1; int lastStep, hpSeen = -1, pops; bool yawInit, hasLast, falling0; Vector3 lastPos;
        readonly List<(Vector3 p, float t)> waves = new();
        readonly List<(Vector3 p, Vector3 n, float t)> ripples = new();
        static readonly Color Mag = new(0.9f, 0.16f, 0.5f), Cyan = new(0.15f, 0.8f, 0.9f), Hot = new(1f, 0.55f, 0.14f), Yel = new(1f, 0.82f, 0.2f);

        float U => lu * inst.lossyScale.x;   // 一个模型单位在世界里多长

        public static MechRig Build(Transform root, Transform inst)
        {
            var m = new MechRig { root = root, inst = inst, mpb = new MaterialPropertyBlock() };
            Transform F(string n) { foreach (var t in inst.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }
            m.pelvis = F("pelvis"); m.torso = F("torso"); m.head = F("head"); m.shield = F("shield");
            var eye = F("eye"); var core = F("core"); var glow = F("shield_glow");
            string[] S = { "l", "r" };
            for (int i = 0; i < 2; i++) { m.leg[i] = F("leg_" + S[i]); m.shin[i] = F("shin_" + S[i]); m.foot[i] = F("foot_" + S[i]); m.arm[i] = F("arm_" + S[i]); m.fore[i] = F("fore_" + S[i]); }
            if (!m.pelvis || !m.torso || !m.head || !m.shield || !eye || !core || !glow) return null;
            for (int i = 0; i < 2; i++) if (!m.leg[i] || !m.shin[i] || !m.foot[i] || !m.arm[i] || !m.fore[i]) return null;
            m.lu = (inst.InverseTransformPoint(m.leg[0].position) - inst.InverseTransformPoint(m.shin[0].position)).magnitude / Thigh;
            m.inSign = Mathf.Sign(inst.InverseTransformPoint(m.arm[0].position).x);
            m.rig = new GameObject("rig").transform; m.rig.SetParent(inst, false);
            m.pelvis.SetParent(m.rig, true); m.torso.SetParent(m.rig, true);
            m.head.SetParent(m.torso, true); eye.SetParent(m.head, true); core.SetParent(m.torso, true);
            for (int i = 0; i < 2; i++)
            {
                m.leg[i].SetParent(m.rig, true); m.shin[i].SetParent(m.leg[i], true); m.foot[i].SetParent(m.shin[i], true);
                m.arm[i].SetParent(m.torso, true); m.fore[i].SetParent(m.arm[i], true);
            }
            m.shield.SetParent(m.fore[0], true); glow.SetParent(m.shield, true);
            foreach (var t in new[] { m.torso, m.head, m.leg[0], m.leg[1], m.shin[0], m.shin[1], m.foot[0], m.foot[1], m.arm[0], m.arm[1], m.fore[0], m.fore[1] }) m.rest[t] = t.localRotation;
            m.shieldRestPos = m.shield.localPosition; m.shieldRelFore = m.shield.localRotation; m.shieldRelInst = Quaternion.Inverse(inst.rotation) * m.shield.rotation;
            Tip(m.fore[1], out m.muzzleL, out m.muzzleAx); Tip(m.fore[0], out var fist, out _); m.shieldGuardPos = fist * 0.985f;
            m.eyeR = eye.GetComponent<Renderer>(); m.coreR = core.GetComponent<Renderer>(); m.glowR = glow.GetComponent<Renderer>();
            m.all = inst.GetComponentsInChildren<Renderer>(true);
            return m;
        }
        /// <summary>零件最长那根轴上、离关节远的那一端的中心（炮口 / 拳头尖），零件自己的坐标</summary>
        static void Tip(Transform t, out Vector3 tip, out Vector3 axis)
        {
            var mf = t.GetComponent<MeshFilter>(); var lb = mf && mf.sharedMesh ? mf.sharedMesh.bounds : new Bounds(Vector3.down, Vector3.one);
            var sz = lb.size; int ax = sz.x >= sz.y && sz.x >= sz.z ? 0 : sz.y >= sz.z ? 1 : 2;
            axis = Vector3.zero; axis[ax] = Mathf.Sign(lb.center[ax]);
            tip = axis * (Mathf.Abs(lb.center[ax]) + sz[ax] * 0.5f);
        }
        /// <summary>关节 j 上的一个点：off 是模型单位、模型朝向（x 右、y 上、z 后）</summary>
        Vector3 At(Transform j, float x, float y, float z) => j.position + j.rotation * (new Vector3(x, y, z) * U);
        Vector3 Joint(int k) => k switch { 0 => At(torso, 0, 1.4f, -0.6f), 1 => arm[0].position, 2 => arm[1].position, 3 => shin[0].position, 4 => shin[1].position, 5 => head.position, _ => At(torso, 0, 1.2f, 1.1f) };

        // ═══════════ 每帧：状态 → 姿态 → 特效 ═══════════
        public void Pose(Enemy e, MonsterGame game, GameView view, float dt, float t, bool fx)
        {
            if (e.state == EState.Held || e.air) { Loose(e, game, view, dt, t, fx); return; }
            wasAir = false;
            float u = U;
            bool warn = e.dropT > Enemies.MechFall;
            if (rig.gameObject.activeSelf == warn) rig.gameObject.SetActive(!warn);
            if (fx && e.dropT > 0) Marker(view, e.lockTarget, e.dropT, dt, t);
            if (warn) { root.position = e.lockTarget + Vector3.up * Enemies.MechHigh; return; }
            bool falling = e.dropT > 0 || e.hop > 0, landing = !falling && e.landT < Enemies.MechRise, stag = e.stagger > 0 && !falling && !landing;
            // 位置、朝向：走路时朝行进方向；站定（落地、蓄力、破绽）时慢慢转向玩家
            root.position = e.pos;
            var toHead = Vector3.ProjectOnPlane(game.headPos - e.pos, Vector3.up);
            var mv = Vector3.ProjectOnPlane(e.fwd, Vector3.up);
            var face = (falling || landing || e.charging || mv.sqrMagnitude < 1e-4f) && toHead.sqrMagnitude > 1e-4f ? toHead.normalized : mv.sqrMagnitude > 1e-4f ? mv.normalized : root.forward;
            var want = Quaternion.LookRotation(face, Vector3.up);
            root.rotation = !yawInit || falling ? want : Quaternion.RotateTowards(root.rotation, want, 150 * dt); yawInit = true;
            float dist = hasLast ? Vector3.ProjectOnPlane(e.pos - lastPos, Vector3.up).magnitude : 0; lastPos = e.pos; hasLast = true;
            bool walking = !falling && !landing && !stag && !e.charging && dist > 1e-6f && e.knock.magnitude < 0.01f;
            moveW = Mathf.MoveTowards(moveW, walking ? 1 : 0, dt * 4);
            if (walking) phase += dist / (Stride * u) * Mathf.PI;
            // 目标姿态
            float cT, leanT, gT, aT;
            if (falling) { cT = 0.3f; leanT = -8; gT = 0.25f; aT = 0.1f; }
            else if (landing)
            {
                float x = e.landT;
                cT = x < 0.1f ? Mathf.Lerp(0.3f, 1, x / 0.1f) : x < 0.4f ? 1 : 1 - Mathf.SmoothStep(0, 1, (x - 0.4f) / 0.55f);
                leanT = 22 * cT; gT = Mathf.Lerp(0.25f, 1, Mathf.SmoothStep(0, 1, (x - 0.5f) / 0.45f)); aT = 0.15f;
                crouch = cT; lean = leanT; guard = gT;
            }
            else if (stag) { cT = 0.28f; leanT = 20 + 4 * Mathf.Sin(t * 2.3f); gT = 0; aT = 0; }
            else { cT = 0.05f; leanT = e.charging ? -3 : 2; gT = 1; aT = e.charging ? 1 : Mathf.Max(0.22f, e.recoil); }
            float k = 1 - Mathf.Exp(-dt * 9);
            crouch = Mathf.Lerp(crouch, cT, k); lean = Mathf.Lerp(lean, leanT - hurtT * 9, k); guard = Mathf.Lerp(guard, gT, 1 - Mathf.Exp(-dt * 6)); aimK = Mathf.Lerp(aimK, aT, 1 - Mathf.Exp(-dt * (e.charging ? 7 : 4)));
            recoilK = e.recoil;
            // 上身、头、炮臂要对着哪儿：都在“胯”的坐标里算（正面 = −Z）
            var aim = (game.headPos - torso.position).normalized;
            var dl = inst.InverseTransformDirection(aim);
            float yawT = falling || stag ? 0 : Mathf.Clamp(-Mathf.Atan2(dl.x, -dl.z) * Mathf.Rad2Deg, -55, 55);
            torsoYaw = Mathf.LerpAngle(torsoYaw, yawT, 1 - Mathf.Exp(-dt * 5));
            var dT = Quaternion.Inverse(Quaternion.Euler(0, torsoYaw, 0)) * dl;     // 转完上身以后还差多少
            float resid = -Mathf.Atan2(dT.x, -dT.z) * Mathf.Rad2Deg;
            float hyT = stag ? 12 * Mathf.Sin(t * 1.7f) : Mathf.Clamp(resid, -65, 65), hpT = stag ? -22 : falling ? -15 : Mathf.Clamp(Mathf.Atan2(dT.y, new Vector2(dT.x, dT.z).magnitude) * Mathf.Rad2Deg, -20, 42);
            headYaw = Mathf.LerpAngle(headYaw, hyT, 1 - Mathf.Exp(-dt * 6)); headPitch = Mathf.Lerp(headPitch, hpT, 1 - Mathf.Exp(-dt * 6));
            float total = Mathf.Clamp(Mathf.Atan2(-dT.z, -dT.y) * Mathf.Rad2Deg, 15, 150);    // 炮口从垂着（0°）抬到平举（90°）再往上
            armSh = Mathf.Lerp(6, total * 0.5f, aimK); armEl = Mathf.Lerp(10, total * 0.5f, aimK); armFix = aimK * Mathf.Clamp(resid, -35, 35);
            Solve(t, falling, stag);
            e.muzzle = fore[1].TransformPoint(muzzleL); e.muzzleDir = fore[1].TransformDirection(muzzleAx).normalized;
            // 发光：目镜、反应堆、盾缘；挨打全身白一下
            hurtT = Mathf.Max(0, hurtT - dt * 5); blockT = Mathf.Max(0, blockT - dt * 3.5f); fireT = Mathf.Max(0, fireT - dt * 6); hpPop = Mathf.Max(0, hpPop - dt * 3);
            Light(stag ? 0.55f + 0.3f * Mathf.Sin(t * 5) : 2.4f + 1.6f * e.charge,
                  stag ? 3.2f + 1.2f * Mathf.Sin(t * 4) : 1.9f + 0.4f * Mathf.Sin(t * 2.2f) + 3.5f * e.charge,
                  guard * (2 + 0.4f * Mathf.Sin(t * 3)) + blockT * 5, hurtT * 1.3f);
            if (!fx) return;
            // ── 特效 ──
            if (falling)
            {
                float thr = e.hop > 0 ? 0.85f : 0.55f + 0.45f * (1 - Mathf.Clamp01(e.dropT / Enemies.MechFall));       // 越接近地面喷得越猛（减速）
                float fl = 1 + 0.15f * Mathf.Sin(t * 31) + 0.1f * Mathf.Sin(t * 47);
                foreach (int sx in new[] { -1, 1 })
                {
                    var p = At(torso, sx * 0.5f, 0.15f, 1.15f);
                    view.FxCone(p, Vector3.down, u * 0.75f, u * 4.6f * thr * fl, Hot); view.FxCone(p, Vector3.down, u * 0.5f, u * 3.0f * thr * fl, new Color(1f, 0.88f, 0.4f)); view.FxCone(p, Vector3.down, u * 0.26f, u * 1.6f * thr * fl, Color.white);
                    view.FxCone(foot[sx < 0 ? 0 : 1].position - Vector3.up * u * 0.5f, Vector3.down, u * 0.55f, u * 2.6f * thr * fl, Hot); view.FxCone(foot[sx < 0 ? 0 : 1].position - Vector3.up * u * 0.5f, Vector3.down, u * 0.32f, u * 1.5f * thr * fl, new Color(1f, 0.88f, 0.4f));
                }
                smokeCd -= dt; if (smokeCd <= 0) { smokeCd = 0.05f; view.FxSmoke(At(torso, Random.Range(-0.5f, 0.5f), -0.6f, 1.15f) - Vector3.up * u * 2, Random.insideUnitSphere * 0.02f + Vector3.down * 0.05f, u * 0.5f, 0.7f, new Color(0.95f, 0.93f, 0.9f)); }
                dustCd -= dt; if (dustCd <= 0 && e.pos.y - e.lockTarget.y < 0.25f) { dustCd = 0.14f; view.AddDust(e.lockTarget + Vector3.up * 0.004f, 0.035f); }   // 快落地：喷流把地上的灰吹开
                falling0 = true;
            }
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                var (p, wt) = waves[i]; wt += dt; if (wt > 0.9f) { waves.RemoveAt(i); continue; }
                waves[i] = (p, wt);
                for (int j = 0; j < 2; j++)
                {
                    float x = (wt - j * 0.14f) / 0.7f; if (x <= 0 || x >= 1) continue;
                    float r = Mathf.Lerp(0.03f, 0.22f, 1 - (1 - x) * (1 - x)); var c = j == 0 ? new Color(0.6f, 0.52f, 0.42f) : new Color(0.6f, 0.08f, 0.32f); c.a = (1 - x) * 0.55f;   // 颜色用深一档（特效着色器会提亮），环也收小：不糊一大片
                    view.FxRingQ(p + Vector3.up * (0.006f + j * 0.004f), Quaternion.identity, new Vector3(r, r * 0.07f, r), c);
                }
            }
            if (falling) return;
            // 迈步：脚下一小圈尘 + 闷响
            int step = Mathf.FloorToInt(phase / Mathf.PI + 0.5f);
            if (step != lastStep)
            {
                lastStep = step;
                if (moveW > 0.5f) { var fp = foot[step % 2 == 0 ? 1 : 0].position; fp.y = e.pos.y + 0.003f; view.AddDust(fp, 0.013f); MonsterAudio.Play("sfx_mech_step", fp, 0.5f, Random.Range(0.92f, 1.06f)); view.Shake(0.05f); }
            }
            // 蓄力：炮口聚能
            if (e.charging && e.muzzle.HasValue)
            {
                var m = e.muzzle.Value + e.muzzleDir * (u * 0.35f); float c = e.charge;
                var halo = Mag; halo.a = 0.45f; view.FxBlob(m, u * (0.5f + 1.3f * c), halo); view.FxBlob(m, u * (0.2f + 0.6f * c), new Color(1f, 0.8f, 0.9f));
                var q = Quaternion.FromToRotation(Vector3.up, e.muzzleDir);
                var rc = Cyan; rc.a = 0.5f + 0.4f * c; float rr = u * (1.7f - 1.1f * c); view.FxRingQ(m, q, new Vector3(rr, rr, rr), rc);
                for (int j = 0; j < 6; j++)
                {
                    float f = Mathf.Repeat(t * 1.7f + j / 6f, 1), a = j * 1.047f + t * 2;
                    var off = q * new Vector3(Mathf.Cos(a), 0.4f * Mathf.Sin(a * 1.3f), Mathf.Sin(a)) * (u * 2.4f * (1 - f));
                    var mc = j % 2 == 0 ? Mag : Cyan; mc.a = f; view.FxBlob(m + off, u * 0.22f * (0.4f + f), mc);
                }
            }
            if (fireT > 0 && e.muzzle.HasValue)
            {
                var m = e.muzzle.Value; var q = Quaternion.FromToRotation(Vector3.up, e.muzzleDir); float kf = fireT;
                for (int j = 0; j < 7; j++) { var d = q * new Vector3(Mathf.Cos(j * 0.9f) * 0.75f, 1, Mathf.Sin(j * 0.9f) * 0.75f).normalized; view.FxCone(m, d, u * 0.4f, u * 2.2f * kf, j % 2 == 0 ? new Color(1f, 0.85f, 0.4f) : Mag); }
                view.FxBlob(m, u * 1.5f * kf, new Color(1f, 0.9f, 0.8f));
            }
            for (int i = ripples.Count - 1; i >= 0; i--)
            {
                var (p, n, rt) = ripples[i]; rt += dt; if (rt > 0.4f) { ripples.RemoveAt(i); continue; }
                ripples[i] = (p, n, rt); float x = rt / 0.4f; var c = Cyan; c.a = 1 - x; float r = u * (0.5f + 2.2f * x);
                view.FxRingQ(p, Quaternion.FromToRotation(Vector3.up, n), new Vector3(r, r * 0.5f, r), c);
            }
            // 破绽：头上转着三颗星、关节冒火花
            if (stag)
            {
                var hc = head.position + Vector3.up * (u * 0.9f);
                for (int j = 0; j < 3; j++) { float a = t * 3 + j * 2.094f; var p = hc + new Vector3(Mathf.Cos(a), 0.15f * Mathf.Sin(a * 2), Mathf.Sin(a)) * (u * 1.05f); view.FxBlob(p, u * 0.3f, Yel); view.FxBlob(p, u * 0.14f, Color.white); }
                sparkCd -= dt; if (sparkCd <= 0) { sparkCd = 0.28f; view.Sparks(Joint(Random.Range(0, 7)), 4, Random.value < 0.5f ? Yel : Color.white, 0.16f); }
            }
            // 血少了：背上冒烟（≤5 格淡烟，≤2 格黑烟 + 火苗）
            if (e.hp <= 5)
            {
                smokeCd -= dt;
                if (smokeCd <= 0) { smokeCd = e.hp <= 2 ? 0.1f : 0.26f; view.FxSmoke(At(torso, Random.Range(-0.6f, 0.6f), 2.3f, 1.0f), new Vector3(Random.Range(-0.01f, 0.01f), 0.06f, Random.Range(-0.01f, 0.01f)), u * (e.hp <= 2 ? 0.75f : 0.5f), 0.9f, e.hp <= 2 ? new Color(0.22f, 0.2f, 0.25f) : new Color(0.6f, 0.58f, 0.62f)); }
                if (e.hp <= 2) { float fl = 0.8f + 0.2f * Mathf.Sin(t * 9); view.FxCone(At(torso, 0.5f, 2.2f, 1.0f), Vector3.up, u * 0.5f, u * 1.2f * fl, Hot); view.FxCone(At(torso, 0.5f, 2.2f, 1.0f), Vector3.up, u * 0.3f, u * 0.7f * fl, new Color(1f, 0.9f, 0.4f)); }
            }
            HpBar(e, view, u);
        }

        // ───────── 被攥在手里 / 被扔出去在空中（11-17：机甲也能捏起来、扔下去）─────────
        // 攥着：手捏在它腰上，它整个身子跟着手晃，两腿乱蹬、炮臂乱挥、头左右甩；攥得越久胸口反应堆越亮、“嘀嘀”越急 —— 满 3.2 秒它挣脱。
        // 扔出去：绕着身体中心翻滚着飞；只是松手：直着掉下去。
        bool wasAir; Vector3 tumbleAxis = Vector3.right; float spinAcc; Quaternion looseRot = Quaternion.identity;
        void Loose(Enemy e, MonsterGame game, GameView view, float dt, float t, bool fx)
        {
            if (!rig.gameObject.activeSelf) rig.gameObject.SetActive(true);
            float u = U; bool held = e.state == EState.Held; var c = Vector3.up * (u * 4.4f);   // 脚底到腰
            if (held)
            {
                var toHead = Vector3.ProjectOnPlane(game.headPos - e.pos, Vector3.up);
                var face = toHead.sqrMagnitude > 1e-4f ? toHead.normalized : root.forward;
                var want = Quaternion.LookRotation(face, Vector3.up) * Quaternion.Euler(Mathf.Sin(t * 8.3f) * 13, Mathf.Sin(t * 5.1f) * 18, Mathf.Sin(t * 6.7f) * 15);
                root.rotation = Quaternion.Slerp(root.rotation, want, 1 - Mathf.Exp(-dt * 14));
                root.position = e.pos - root.rotation * c;
                wasAir = false;
            }
            else
            {
                if (!wasAir) { wasAir = true; var fl = new Vector3(e.vel.x, 0, e.vel.z); tumbleAxis = fl.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, fl.normalized) : root.right; looseRot = root.rotation; spinAcc = 0; }
                spinAcc += dt * (e.airThrown ? 540 : 60);
                root.rotation = Quaternion.AngleAxis(spinAcc, tumbleAxis) * looseRot;
                root.position = e.pos + c - root.rotation * c;
            }
            lastPos = e.pos; hasLast = true; yawInit = false;
            float k = 1 - Mathf.Exp(-dt * 9);
            phase += dt * (held ? 12f : 7f); moveW = 1; crouch = Mathf.Lerp(crouch, 0.22f, k); lean = Mathf.Lerp(lean, 0, k); guard = Mathf.Lerp(guard, 0.15f, k); aimK = 0.5f; recoilK = 0;
            torsoYaw = 14 * Mathf.Sin(t * 5.3f); headYaw = 28 * Mathf.Sin(t * 6.1f); headPitch = 8;
            armSh = 45 + 35 * Mathf.Sin(t * 9.2f); armEl = 30 + 25 * Mathf.Sin(t * 7.3f + 1); armFix = 0;
            Solve(t, true, false);
            e.muzzle = fore[1].TransformPoint(muzzleL); e.muzzleDir = fore[1].TransformDirection(muzzleAx).normalized;
            hurtT = Mathf.Max(0, hurtT - dt * 5); blockT = 0; fireT = 0; hpPop = Mathf.Max(0, hpPop - dt * 3);
            float strain = held ? Mathf.Clamp01(e.heldT / Enemies.MechHold) : 0; strain *= strain;
            Light(2.4f, 2f + 5f * strain, 0.3f, hurtT * 1.3f);
            if (!fx) return;
            if (held)
            {
                sparkCd -= dt; if (sparkCd <= 0) { sparkCd = Mathf.Lerp(0.5f, 0.14f, strain); view.Sparks(Joint(Random.Range(0, 7)), 3, Random.value < 0.5f ? Yel : Color.white, 0.18f); }
                if (strain > 0.3f) { var cp = coreR.bounds.center; var halo = Hot; halo.a = 0.5f * strain; view.FxBlob(cp, u * (0.8f + 2.2f * strain), halo); }
                beepCd -= dt; if (e.heldT > 1.4f && beepCd <= 0) { beepCd = Mathf.Lerp(0.5f, 0.16f, strain); MonsterAudio.Play("sfx_star_up", root.position, 0.4f, 1.7f); }
            }
            HpBar(e, view, u);
        }

        /// <summary>把姿态参数落到各个关节上。模型正面 = 本地 −Z；Euler(+x) = 垂着的肢体往前摆 / 头往上抬</summary>
        void Solve(float t, bool falling, bool stag)
        {
            float hip = 0; var th = new float[2]; var kn = new float[2];
            for (int i = 0; i < 2; i++)
            {
                float ph = phase + i * Mathf.PI, sw = Mathf.Sin(ph), lift = Mathf.Max(0, Mathf.Cos(ph));
                th[i] = crouch * 50 + moveW * 22 * sw + (falling ? (i == 0 ? 16 : -4) : 0);
                kn[i] = crouch * 95 + moveW * (6 + 34 * lift) + (falling ? 24 : 0);
                hip = Mathf.Max(hip, Thigh * Mathf.Cos(th[i] * Mathf.Deg2Rad) + Shin * Mathf.Cos((th[i] - kn[i]) * Mathf.Deg2Rad));
            }
            rig.localPosition = new Vector3(0, falling ? 0 : -(Thigh + Shin - hip) * lu, 0);     // 支撑腿（伸得更直的那条）决定胯有多高：脚始终踩在地上
            for (int i = 0; i < 2; i++)
            {
                leg[i].localRotation = rest[leg[i]] * Quaternion.Euler(th[i], 0, 0);
                shin[i].localRotation = rest[shin[i]] * Quaternion.Euler(-kn[i], 0, 0);
                foot[i].localRotation = rest[foot[i]] * Quaternion.Euler(-(th[i] - kn[i]) + (falling ? 20 : 0), 0, 0);   // 脚掌放平（下降时脚尖朝下）
            }
            torso.localRotation = rest[torso] * Quaternion.Euler(-lean, torsoYaw + moveW * 5 * Mathf.Sin(phase), moveW * 2.5f * Mathf.Cos(phase));
            head.localRotation = rest[head] * Quaternion.Euler(headPitch, headYaw, 0);
            // 右臂：加农炮
            arm[1].localRotation = rest[arm[1]] * Quaternion.Euler(0, armFix, 0) * Quaternion.Euler(armSh - recoilK * 14 + moveW * (1 - aimK) * 8 * Mathf.Sin(phase), 0, 0);
            fore[1].localRotation = rest[fore[1]] * Quaternion.Euler(armEl - recoilK * 12, 0, 0);
            // 左臂：举盾（肩往前抬、往里收，肘弯到前臂差不多平举；盾自己保持竖直、盾面朝上身的正面）
            float g = Mathf.SmoothStep(0, 1, guard);
            arm[0].localRotation = rest[arm[0]] * Quaternion.Euler(0, inSign * 26 * g, 0) * Quaternion.Euler(Mathf.Lerp(4, 24, g) - blockT * 12 + (stag ? 6 * Mathf.Sin(t * 2) : 0), 0, 0);
            fore[0].localRotation = rest[fore[0]] * Quaternion.Euler(Mathf.Lerp(8, 76, g) - blockT * 10, 0, 0);
            shield.localPosition = Vector3.Lerp(shieldRestPos, shieldGuardPos, g);
            var upright = inst.rotation * Quaternion.Euler(0, torsoYaw, 0) * Quaternion.Euler(6, 0, 0) * shieldRelInst;
            var hang = fore[0].rotation * shieldRelFore * Quaternion.Euler(0, 0, 10);
            shield.rotation = Quaternion.Slerp(hang, upright, g);
        }

        void Light(float eye, float core, float glow, float flash)
        {
            foreach (var r in all)
            {
                if (!r) continue;
                float em = r == eyeR ? eye : r == coreR ? core : r == glowR ? glow : 2.2f;
                mpb.SetFloat("_Glow", flash); mpb.SetColor("_EmissionColor", new Color(em, em, em)); r.SetPropertyBlock(mpb);
            }
        }

        // 落点靶标：外圈一次次往里收拢（“这里要落东西”），内圈常亮，四个刻度，一根淡淡的光柱指向天上；越接近落地收得越快
        void Marker(GameView view, Vector3 p, float left, float dt, float t)
        {
            float total = Enemies.MechWarn + Enemies.MechFall, x = 1 - left / total; p += Vector3.up * 0.005f;
            var c = new Color(0.72f, 0.04f, 0.2f, 0.9f); const float R = 0.05f;   // 特效着色器会提亮 1.6 倍：用深一档的红
            view.FxRingQ(p, Quaternion.identity, new Vector3(R, R * 0.15f, R), c);
            float f = Mathf.Repeat(t * Mathf.Lerp(1.1f, 2.0f, x), 1); var c2 = c; c2.a = f * 0.8f; float r2 = Mathf.Lerp(0.13f, R, f);
            view.FxRingQ(p, Quaternion.identity, new Vector3(r2, r2 * 0.04f, r2 * 0.6f + R * 0.4f), c2);
            for (int j = 0; j < 4; j++) { var d = Quaternion.Euler(0, j * 90 + 45, 0) * Vector3.forward; view.FxBox(p + d * (R + 0.02f), Quaternion.LookRotation(d), new Vector3(0.006f, 0.002f, 0.022f), c, 1); }
            var beam = c; beam.a = 0.16f; view.FxStreak(p + Vector3.up * 0.45f, 0.012f, 0.9f, beam);
            beepCd -= dt; if (beepCd <= 0) { beepCd = Mathf.Lerp(0.6f, 0.3f, x); MonsterAudio.Play("sfx_star_up", p, 0.5f, 0.6f); }
        }

        // 头顶的生命格（8 格，面朝玩家）：掉血时那一格弹一下
        void HpBar(Enemy e, GameView view, float u)
        {
            var cam = Camera.main; if (!cam) return;
            if (hpSeen >= 0 && e.hp < hpSeen) hpPop = 1; hpSeen = e.hp;
            int n = Mathf.Max(1, e.maxHp);
            var c = head.position + Vector3.up * (u * (1.75f + 0.2f * hpPop));
            var rot = Quaternion.LookRotation(c - cam.transform.position, Vector3.up);
            float w = u * 0.3f, h = u * 0.2f, gap = u * 0.08f;   // 8 格一共约 3.5cm 宽，和机甲的肩差不多
            view.FxBox(c + rot * new Vector3(0, 0, u * 0.06f), rot, new Vector3(n * (w + gap) + gap, h + gap * 2, u * 0.04f), new Color(0.07f, 0.05f, 0.1f), 0);
            for (int j = 0; j < n; j++)
            {
                bool lit = j < e.hp; float pop = j == e.hp ? hpPop : 0;
                var col = lit ? (e.hp <= 2 ? new Color(0.95f, 0.25f, 0.15f) : Mag) : Color.Lerp(new Color(0.2f, 0.17f, 0.25f), Color.white, pop);
                view.FxBox(c + rot * new Vector3((j - (n - 1) * 0.5f) * (w + gap), 0, 0), rot, new Vector3(w, h, u * 0.06f) * (1 + 0.6f * pop), col, lit || pop > 0 ? 1 : 0);
            }
        }

        // ═══════════ 事件 ═══════════
        public void OnEvent(Ev ev, Enemy e, GameView view)
        {
            float u = U;
            switch (ev.type)
            {
                case "mech_fall": MonsterAudio.Play("sfx_incoming", root.position, 0.9f, 0.45f); break;
                case "mech_land" when ev.n > 0 && ev.n < 1:
                    {
                        // 喷气跳回路上的落地：比空降小一号
                        var lp = ev.pos ?? root.position; waves.Add((lp, 0)); view.AddDust(lp + Vector3.up * 0.004f, 0.05f); view.Shake(0.3f);
                        MonsterAudio.Play("sfx_mech_step", lp, 0.9f, 0.8f); view.FlattenCars(lp, 0.04f, 4, false);
                        break;
                    }
                case "mech_break":
                    view.Sparks(coreR.bounds.center, 18, new Color(0.5f, 0.95f, 1f), 0.4f); view.Shake(0.25f);
                    MonsterAudio.Play("sfx_shield", root.position, 0.9f, 1.2f); view.PowText(Loc.T("挣脱！"), head.position + Vector3.up * (u * 3.1f), new Color(0.45f, 1f, 0.95f), 0.9f, 14, 1.4f);
                    break;
                case "mech_slam":
                    {
                        // 被扔 / 掉到地上：尘浪、冲击环、压扁落点上的车、头顶弹出掉了几格
                        var sp = ev.pos ?? root.position; waves.Add((sp, 0)); view.AddDust(sp + Vector3.up * 0.004f, 0.08f); view.Shake(0.7f); view.FlattenCars(sp, 0.05f, 6, false);
                        MonsterAudio.Play("sfx_mech_land", sp, 0.9f, 1.15f); view.Pow("crash", sp + Vector3.up * (u * 9));
                        view.PowText("-" + Mathf.RoundToInt(ev.n), sp + Vector3.up * (u * 11.5f), Hot, 0.7f, 16, 1.4f);
                        break;
                    }
                case "mech_land":
                    waves.Add((ev.pos ?? root.position, 0)); view.AddDust((ev.pos ?? root.position) + Vector3.up * 0.004f, 0.1f); view.Shake(0.9f);
                    view.FlattenCars(ev.pos ?? root.position, 0.05f, 6, false);   // 落点上的车压扁
                    MonsterAudio.Play("sfx_mech_land", root.position, 1f); view.Pow("boom", root.position + Vector3.up * (u * 9));
                    break;
                case "mech_charge": MonsterAudio.Play("sfx_mech_charge", root.position, 0.8f); break;
                case "fire":
                    fireT = 1; view.Shake(0.28f); MonsterAudio.Play("sfx_mech_fire", root.position, 1f);
                    if (e != null && e.muzzle.HasValue) for (int i = 0; i < 5; i++) view.FxSmoke(e.muzzle.Value + Random.insideUnitSphere * u * 0.3f, e.muzzleDir * 0.05f + Random.insideUnitSphere * 0.03f, u * 0.6f, 0.7f, new Color(0.93f, 0.9f, 0.95f));
                    break;
                case "mech_block":
                    {
                        blockT = 1; var n = shield.rotation * Quaternion.Inverse(shieldRelInst) * Vector3.back; var p = shield.position + n * (u * 0.6f);
                        ripples.Add((p, n, 0)); view.Sparks(p, 14, Random.value < 0.5f ? new Color(0.5f, 0.95f, 1f) : Color.white, 0.3f);
                        MonsterAudio.Play("sfx_clang", p, 0.9f, 0.8f); view.Pow("clang", p + Vector3.up * (u * 4.5f) + n * (u * 2)); view.Shake(0.12f);
                        break;
                    }
                case "mech_stagger" when e == null || e.hp > 0:
                    view.PowText(Loc.T("破绽！"), head.position + Vector3.up * (u * 3.1f), Yel, 1.0f, 14, 1.4f);
                    for (int k = 0; k < 4; k++) view.Sparks(Joint(k), 6, Yel, 0.25f);
                    MonsterAudio.Play("sfx_mech_down", root.position, 0.6f, 1.5f);
                    break;
                case "mech_fizzle": if (e != null && e.muzzle.HasValue) { view.Sparks(e.muzzle.Value, 8, Mag, 0.2f); view.FxSmoke(e.muzzle.Value, Vector3.up * 0.04f, u * 0.5f, 0.6f, new Color(0.6f, 0.58f, 0.62f)); } break;
                case "mech_hurt":
                    hurtT = 1; view.Sparks(At(torso, 0, 1.3f, -0.8f), 12, Hot, 0.3f); view.Shake(0.14f); MonsterAudio.Play("sfx_clang", root.position, 0.7f, 0.6f);
                    break;
            }
        }

        // ═══════════ 击毁（逻辑层已经把它移出追兵列表；Enemies.MechDie 秒后逻辑层放最后的大爆炸）═══════════
        public void BeginDeath(GameView view)
        {
            dying = 0; pops = 0; if (!rig.gameObject.activeSelf) rig.gameObject.SetActive(true);
            MonsterAudio.Play("sfx_mech_down", root.position, 1f);
            view.PowText(Loc.T("击毁！"), head.position + Vector3.up * (U * 3.1f), Hot, 1.2f, 16, 1.5f);
        }
        /// <summary>返回 true = 演完了（各部件已经交给 GameView 当残骸）</summary>
        public bool UpdateDeath(GameView view, float dt, float t, bool fx)
        {
            if (dying < 0) return true;
            dying += dt; float u = U, D = Enemies.MechDie, x = dying;
            // 姿态：失控抖几下 → 膝盖发软跪下去、上身前倾、盾和炮都垂下、头低下
            float k = 1 - Mathf.Exp(-dt * 6);
            moveW = Mathf.MoveTowards(moveW, 0, dt * 4);
            crouch = Mathf.Lerp(crouch, Mathf.SmoothStep(0, 0.85f, (x - 0.35f) / 1.1f), k); lean = Mathf.Lerp(lean, 8 + 28 * Mathf.SmoothStep(0, 1, (x - 0.4f) / 1.2f) + 3 * Mathf.Sin(t * 5), k);
            guard = Mathf.Lerp(guard, 0, k); aimK = Mathf.Lerp(aimK, 0, k); armSh = Mathf.Lerp(armSh, 4, k); armEl = Mathf.Lerp(armEl, 14, k); armFix = Mathf.Lerp(armFix, 0, k);
            headPitch = Mathf.Lerp(headPitch, -28, k); headYaw = Mathf.Lerp(headYaw, 10 * Mathf.Sin(t * 2.4f), k); torsoYaw = Mathf.Lerp(torsoYaw, 6 * Mathf.Sin(t * 3.1f) * Mathf.Clamp01(1.4f - x), k); recoilK = 0;
            Solve(t, false, true);
            hurtT = Mathf.Max(0, hurtT - dt * 5);
            float over = Mathf.Clamp01((x - (D - 1.0f)) / 1.0f);                       // 最后 1 秒：反应堆过载，越来越亮（平滑上升，不闪）
            Light(Mathf.Max(0, 2.4f * (1 - x / 1.1f)), 2 + 8 * over * over, 0, hurtT * 1.3f + over * over * 0.9f);
            if (fx)
            {
                // 一处处小爆炸（0.25 / 0.8 / 1.3 / 1.75 秒）
                float[] at = { 0.25f, 0.8f, 1.3f, 1.75f };
                while (pops < at.Length && x >= at[pops])
                {
                    var p = Joint(new[] { 2, 3, 1, 6 }[pops]); pops++; hurtT = 1;
                    view.AddBlast(p, 0.03f); view.Sparks(p, 12, Hot, 0.35f); MonsterAudio.Play("sfx_blast", p, 0.5f, 1.5f);
                }
                sparkCd -= dt; if (sparkCd <= 0) { sparkCd = 0.16f; view.Sparks(Joint(Random.Range(0, 7)), 3, Random.value < 0.5f ? Yel : Color.white, 0.2f); }
                smokeCd -= dt; if (smokeCd <= 0) { smokeCd = 0.08f; view.FxSmoke(At(torso, Random.Range(-0.7f, 0.7f), 2.0f, 0.6f), new Vector3(Random.Range(-0.015f, 0.015f), 0.07f, Random.Range(-0.015f, 0.015f)), u * 0.8f, 1.0f, new Color(0.2f, 0.18f, 0.23f)); }
                var cp = coreR.bounds.center; var halo = Hot; halo.a = 0.5f * over; view.FxBlob(cp, u * (0.6f + 2.6f * over), halo); if (over > 0) view.FxBlob(cp, u * 1.1f * over, new Color(1f, 0.95f, 0.8f));
            }
            if (x < D) return false;
            // 大爆炸：各部件分头飞出去，落地摔成碎片
            var center = torso.position;
            view.Shake(1f); view.Sparks(center, 30, Hot, 0.6f); view.Sparks(center, 16, Color.white, 0.45f); view.AddBlast(center, 0.07f);
            foreach (var part in new[] { head, arm[0], arm[1], leg[0], leg[1], torso, pelvis })
            {
                var dir = part.position - center + Random.insideUnitSphere * (u * 0.5f); dir.y = Mathf.Abs(dir.y) * 0.5f;
                view.AddWreck(part, dir.normalized * Random.Range(0.25f, 0.5f) + Vector3.up * Random.Range(0.35f, 0.65f), Random.Range(1.1f, 1.9f));
            }
            Object.Destroy(root.gameObject);
            dying = -1;
            return true;
        }
    }
}
