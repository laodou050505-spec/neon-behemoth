// 全屏反馈层（见 Shaders/HurtVignette）：受伤泼漆边框、低血量压暗、威胁指示弧、倒下时的闭眼、核爆白光。
// 11-12 用户：泛红 / 压暗没感觉、不知道敌人在哪、死的时候不要“巨兽倒下”四个字，要眼睛慢慢合上的无力感。
// 渲染顺序在场景特效之后、界面纸 / 记分牌之前：牌子和结算纸不会被盖住。不做频闪：呼吸都在 1.2Hz 以下。
using System.Collections.Generic;
using UnityEngine;

namespace Monster.Game
{
    public class HurtOverlay : MonoBehaviour
    {
        public MonsterGame game; public Camera cam;
        Material mat; Transform quad; float hit, angle, dead = -1, kick, white;
        readonly List<(Vector3 pos, float s, int kind)> threats = new();
        readonly Vector4[] tArr = new Vector4[6];

        void Start()
        {
            if (!cam) cam = Camera.main;
            var sh = Shader.Find("Monster/HurtVignette"); if (!sh) { enabled = false; return; }
            mat = new Material(sh) { name = "hurtVignette", renderQueue = 3050 };
            var tex = Resources.Load<Texture2D>("UI/hurt_frame"); if (tex) { tex.wrapMode = TextureWrapMode.Clamp; mat.SetTexture("_Frame", tex); }
            var m = new Mesh { name = "fullscreen" };
            m.SetVertices(new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1000);
            var go = new GameObject("HurtOverlay"); quad = go.transform; quad.SetParent(cam.transform, false); quad.localPosition = new Vector3(0, 0, 1);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }
        void OnDestroy() { if (quad) Destroy(quad.gameObject); }

        float AngleOf(Vector3 world)
        {
            var l = cam.transform.InverseTransformPoint(world);
            // 在身后：按左右放到下半圈（“后面”）
            return l.z > 0.05f ? Mathf.Atan2(l.y, l.x) : Mathf.Atan2(-0.7f, l.x >= 0 ? 1 : -1);
        }
        /// <summary>挨打：from = 伤害来的位置（世界坐标）</summary>
        public void Hit(Vector3 from, float dmg)
        {
            if (!cam) return;
            angle = AngleOf(from);
            hit = 1; kick = Mathf.Max(kick, Mathf.Clamp01(0.5f + dmg / 30f));
        }
        /// <summary>每帧由 GameView 给：这一刻的威胁（位置、强度 0～1、0 黄 = 瞄准 / 开火，1 红 = 弹药来袭）</summary>
        public void SetThreats(List<(Vector3 pos, float s, int kind)> list) { threats.Clear(); threats.AddRange(list); }
        public void Die() { if (dead < 0) dead = 0; }
        public void Flash() { white = 1.9f; flashed = true; }
        /// <summary>柔和地亮一下（结局里力量回到身上的那一刻）：a = 最亮时的白光强度，约一秒退掉</summary>
        public void Pulse(float a) { white = Mathf.Max(white, a); }
        bool flashed;
        /// <summary>警报（核弹来袭）：四周红框按给定强度显示，每帧调</summary>
        public void Alarm(float amt) { alarm = amt; }
        float alarm, level;
        // ───────── 濒死（11-16 用户：血量状态的反馈还差点意思，具体是濒死状态）─────────
        // 原来血再少也只是红框比平时深一点、慢慢呼吸，和“还剩一半血”没有质的区别。现在血量 ≤ 25% 进入濒死：
        //   · 心跳：每 0.95 → 0.72 秒一次“咚—咚”（血越少越快，最快每秒 1.4 次），背景音乐同时压低一半，心跳听得清；
        //   · 画面跟着心跳一缩一放：每一下红框往里压、四周更暗，再松开（平滑的“咚—咚”两拍，不是闪）；
        //   · 视野收窄：暗角常驻加深（像隧道），红框压到最里面；
        //   · 进入的那一下：眼前一句“濒死！找回血球”，牌子和手背上的生命条变红（原有）；回到 30% 以上才解除（留一点迟滞，不在 25% 上下反复进出）。
        public bool NearDeath { get; private set; }
        public float Beat { get; private set; }     // 这一刻心跳的强度 0～1（记分牌的生命条跟着跳）
        float beatPh; public System.Action onNearDeath;

        void LateUpdate()
        {
            if (!mat || game == null) return;
            float dt = Time.unscaledDeltaTime;
            float hp = Mathf.Clamp01(game.hp / Cfg.HpMax);
            hit = Mathf.Max(0, hit - dt * 1.0f); kick = Mathf.Max(0, kick - dt * 0.75f); white = Mathf.Max(0, white - dt * 0.7f);
            // 11-13 用户：低血量的画面不是“被打了才亮一下”，而是按剩余血量一直在、逐渐加深 ——
            //   九成血以下开始出现一点点红框，血越少红框往里压得越深、四周越暗（连续变化，平滑跟随，不跳变）；三成以下再加一点慢呼吸（1.2Hz）。
            //   挨打本身只在来袭的那一侧额外重一下（指方向用，0.9 秒退掉），整圈只轻微加一点。
            float miss = 1 - hp;
            float want = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.1f, 0.92f, miss));
            level = Mathf.MoveTowards(level, want, dt * 0.8f);          // 掉血 / 回血后在一秒多里平滑过渡到新的深浅
            float breathe = hp <= 0.3f ? 0.9f + 0.1f * Mathf.Sin(Time.unscaledTime * 7.5f) : 1;
            float low = level * 0.62f * breathe;
            float dark = level * 0.82f;
            float flat = Mathf.InverseLerp(0.6f, 1f, level) * 0.14f, lid = 0;
            // 濒死
            bool nd = dead < 0 && game.result == null && !game.Winning && game.tut == null && (NearDeath ? hp <= 0.3f : hp <= 0.25f) && hp > 0;
            if (nd && !NearDeath) { beatPh = 0.95f; onNearDeath?.Invoke(); }
            NearDeath = nd; Beat = 0;
            if (nd)
            {
                float period = Mathf.Lerp(0.72f, 0.95f, Mathf.InverseLerp(0.05f, 0.25f, hp));
                float prev = beatPh; beatPh += dt / period;
                if (beatPh >= 1) { beatPh -= 1; MonsterAudio.Play("sfx_heart", null, Mathf.Lerp(0.95f, 0.7f, Mathf.InverseLerp(0.05f, 0.25f, hp))); }
                float a1 = (beatPh - 0.06f) / 0.07f, a2 = (beatPh - 0.3f) / 0.08f;
                Beat = Mathf.Exp(-a1 * a1) + 0.6f * Mathf.Exp(-a2 * a2);
                low = Mathf.Max(low, 0.6f) + 0.2f * Beat;
                dark = Mathf.Max(dark, 0.84f) + 0.1f * Beat;
                flat = Mathf.Max(flat, 0.12f) + 0.05f * Beat;
            }
            MonsterAudio.Strain = nd;
            float frame = Mathf.Max(low + kick * 0.14f, alarm); alarm = 0;
            if (dead >= 0)
            {
                // 倒下：眼皮沉下来 —— 合到一半、撑开一点、又沉下去、再撑一下、彻底合上（约 2.6 秒）；结算纸出来后慢慢睁开一条能看清纸的缝
                // 核爆：先是一片白光（约 1.2 秒才开始退），白光退下去的同时眼睛才开始合 —— 不然眼皮先合上，白光就看不到了
                if (!(flashed && white > 1.0f)) dead += dt;
                // 11-13 用户：最后眼睛就是一条缝（不再重新睁开）—— 眨两下、越来越沉，最后剩一条缝停住；结算纸画在眼皮上面，照样看得清
                var keys = new (float t, float v)[] { (0, 0), (0.55f, 0.55f), (0.95f, 0.3f), (1.5f, 0.8f), (1.85f, 0.58f), (2.6f, 0.9f), (3.1f, 0.86f), (3.9f, 0.88f) };
                lid = keys[^1].v;
                for (int i = 0; i < keys.Length - 1; i++)
                    if (dead < keys[i + 1].t) { float k = Mathf.InverseLerp(keys[i].t, keys[i + 1].t, dead); k = k * k * (3 - 2 * k); lid = Mathf.Lerp(keys[i].v, keys[i + 1].v, k); break; }
                float fadeIn = Mathf.Clamp01(dead / 1.2f);
                dark = Mathf.Lerp(dark, 0.9f, fadeIn); flat = Mathf.Lerp(flat, 0.4f, fadeIn); frame *= 1 - Mathf.Clamp01((dead - 0.6f) / 1.0f);
                if (flashed) frame = 0;
                for (int i = 0; i < 6; i++) tArr[i] = Vector4.zero;
            }
            else
            {
                threats.Sort((x, y) => y.s.CompareTo(x.s));
                for (int i = 0; i < 6; i++)
                {
                    if (i >= threats.Count) { tArr[i] = Vector4.zero; continue; }
                    // 玩家已经正看着它（离视线中心 8° 以内）就不画弧：弧是用来指“视线之外”的方向的，正中间的东西方位角没有意义
                    var l = cam.transform.InverseTransformPoint(threats[i].pos);
                    float off = l.z > 0.05f ? new Vector2(l.x, l.y).magnitude / l.z : 9;
                    tArr[i] = new Vector4(AngleOf(threats[i].pos), threats[i].s * Mathf.InverseLerp(0.24f, 0.4f, off), threats[i].kind, 0);   // 图标在 15° 一圈：目标已经在这圈以内就不画
                }
            }
            mat.SetFloat("_Dark", dark); mat.SetFloat("_Flat", flat); mat.SetFloat("_FrameAmt", frame); mat.SetFloat("_Hit", hit); mat.SetFloat("_Angle", angle);
            mat.SetFloat("_Lid", lid); mat.SetFloat("_White", Mathf.Clamp01(white)); mat.SetVectorArray("_Threat", tArr);
            var ct = cam.transform; mat.SetVector("_HeadR", ct.right); mat.SetVector("_HeadU", ct.up); mat.SetVector("_HeadF", ct.forward); mat.SetVector("_HeadP", ct.position);
            // 效果画在离头多远的球面上：头到主城区中心的距离（0.5～1.0m）—— 玩家的眼睛正对焦在城市上，图标放在同一个距离才不重影
            var zc = game.city.zones[0].Center; mat.SetFloat("_Depth", Mathf.Clamp(Vector3.Distance(ct.position, zc), 0.5f, 1.0f));
            bool on = dark > 0.005f || flat > 0.005f || frame > 0.005f || hit > 0.005f || lid > 0.001f || white > 0.005f || (dead < 0 && threats.Count > 0);
            if (quad.gameObject.activeSelf != on) quad.gameObject.SetActive(on);
        }
    }
}
