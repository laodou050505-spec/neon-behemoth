// 巨兽之手 v3（11-10 用户：原来用圆管 + 圆球每帧搭的手粗糙、难看、没有力量感 → 换成一只完整的带骨骼的怪兽手；
// 第二轮：不要手臂只要手；手指弯曲像被挤压、指甲跑到指腹 → 骨头朝向改成沿指链逐节传递）。
// 模型：Resources/Hand/monster_hand_R|L（Tools/blender_hand.py），着色器 Monster/MonsterHand。
// 驱动：骨骼 J{k} 和 PICO / OpenXR 的 26 个关节一一对应，每帧把每根骨头摆到真手关节上：
//   位置 = 关节；朝前 = 指向下一节关节；“朝上（指背）”= 从掌骨开始沿指链逐节传递（每节只在上一节的基础上转“弯曲”那一下）。
//   原来每节都直接用手背法线当“朝上”，手指弯到快 90° 时骨头方向和它几乎平行 → 朝向算不准、骨头突然拧转，指甲翻到指腹、指节像被挤扁。
// 绑定姿势下每根骨头的朝向偏差启动时按同样的算法算一次（和导入时坐标轴怎么转无关）。
// 着色器参数：_Rage（点亮手腕环 → 手背沟 → 爪环）、_Hp（手背四条甲片 = 四格体力，掉一格烧黑一条）、_Hurt（受伤闪红）。
using UnityEngine;

namespace Monster.Game
{
    public class HandView : MonoBehaviour
    {
        public MonsterInput input; public MonsterGame game;
        /// <summary>编辑器 / 截图：直接给关节（世界坐标）</summary>
        public Vector3[] debugL, debugR; public float[] debugRadL, debugRadR;
        public float debugRage = 0.4f, debugHp = 1;

        class Rig
        {
            public GameObject go; public SkinnedMeshRenderer smr;
            public readonly Transform[] bone = new Transform[26];
            public readonly Quaternion[] off = new Quaternion[26];
            public bool right;
        }
        readonly Rig[] rigs = new Rig[2];   // 0 = 左手, 1 = 右手
        Material mat; MaterialPropertyBlock mpb;
        float lastHp = -1, hurt;
        bool ready;
        readonly Quaternion[] frame = new Quaternion[26]; readonly bool[] has = new bool[26];
        readonly Vector3[] Q = new Vector3[26]; const float FingerStretch = 1.12f;

        // 指链：拇指 掌骨根→近→远→尖；四指 掌骨根→掌指→中→远→尖
        static readonly int[][] Chains =
        {
            new[] { 2, 3, 4, 5 },
            new[] { 6, 7, 8, 9, 10 }, new[] { 11, 12, 13, 14, 15 }, new[] { 16, 17, 18, 19, 20 }, new[] { 21, 22, 23, 24, 25 },
        };

        void Start() => Init();

        void Init()
        {
            if (ready) return;
            mat = new Material(Shader.Find("Monster/MonsterHand")) { name = "monsterHand" };
            mpb = new MaterialPropertyBlock();
            foreach (var n in new[] { "L", "R" })
            {
                var pf = Resources.Load<GameObject>("Hand/monster_hand_" + n);
                if (!pf) { Debug.LogWarning("[怪兽] 没找到巨兽之手模型 Hand/monster_hand_" + n); continue; }
                var r = Bind(Instantiate(pf));
                if (r != null) rigs[r.right ? 1 : 0] = r;   // 按模型实际的左右
            }
            ready = true;
        }

        Rig Bind(GameObject go)
        {
            go.name = "MonsterHand"; go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var r = new Rig { go = go, smr = go.GetComponentInChildren<SkinnedMeshRenderer>() };
            if (!r.smr) { Destroy(go); return null; }
            r.smr.sharedMaterial = mat; r.smr.updateWhenOffscreen = true;
            r.smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.smr.receiveShadows = false;
            foreach (var b in r.smr.bones)
                if (b && b.name.Length > 1 && b.name[0] == 'J' && int.TryParse(b.name.Substring(1), out var k) && k >= 0 && k < 26) r.bone[k] = b;
            var P = new Vector3[26];
            for (int k = 0; k < 26; k++) if (r.bone[k]) P[k] = r.bone[k].position;
            if (!r.bone[1] || !r.bone[7] || !r.bone[22] || !r.bone[12]) { Destroy(go); return null; }
            // 模型是哪只手：绑定姿势里手背朝上（+Y），右手的“小指 − 食指 × 中指 − 腕”指向掌心
            r.right = Vector3.Dot(Vector3.Cross(P[22] - P[7], P[12] - P[1]), Vector3.up) < 0;
            Frames(P, r.right);
            for (int k = 0; k < 26; k++) r.off[k] = r.bone[k] && has[k] ? Quaternion.Inverse(frame[k]) * r.bone[k].rotation : Quaternion.identity;
            go.SetActive(false);
            return r;
        }

        static Vector3 Back(Vector3[] P, bool right)
        {
            var c = Vector3.Cross(P[22] - P[7], P[12] - P[1]);
            return (right ? -c : c).normalized;
        }

        /// <summary>算每根骨头的朝向：腕 = 指向中指掌指关节、手背朝上；每条指链从掌骨开始用手背当“朝上”，往下每节按“上一节方向 → 这一节方向”的最小旋转传递</summary>
        void Frames(Vector3[] P, bool right)
        {
            for (int k = 0; k < 26; k++) has[k] = false;
            var back = Back(P, right);
            var d1 = P[12] - P[1];
            if (d1.sqrMagnitude > 1e-10f) { frame[1] = Quaternion.LookRotation(d1.normalized, back); has[1] = true; }
            foreach (var ch in Chains)
            {
                Vector3 prev = Vector3.zero, up = back;
                for (int i = 0; i < ch.Length; i++)
                {
                    int k = ch[i];
                    var d = i < ch.Length - 1 ? P[ch[i + 1]] - P[k] : P[k] - P[ch[i - 1]];
                    if (d.sqrMagnitude < 1e-10f) { if (i > 0 && has[ch[i - 1]]) { frame[k] = frame[ch[i - 1]]; has[k] = true; } continue; }
                    d.Normalize();
                    if (i == 0) up = Vector3.ProjectOnPlane(back, d);
                    else up = Quaternion.FromToRotation(prev, d) * up;
                    up = Vector3.ProjectOnPlane(up, d);
                    if (up.sqrMagnitude < 1e-8f) up = Vector3.ProjectOnPlane(back, d);
                    frame[k] = Quaternion.LookRotation(d, up.normalized); has[k] = true;
                    prev = d;
                }
            }
        }

        /// <summary>加载期间不画手（11-12 用户：手一出来就定住 —— 加载卡顿时手跟不上真手）</summary>
        public bool hidden;
        void LateUpdate()
        {
            if (!ready) Init();
            if (hidden) { foreach (var r0 in rigs) if (r0 != null && r0.go.activeSelf) r0.go.SetActive(false); return; }
            float rage = game != null ? Mathf.Clamp01(game.rage / Cfg.RageMax) : debugRage;
            float hp = game != null ? game.hp / Cfg.HpMax : debugHp;
            if (game != null && lastHp >= 0 && hp < lastHp - 0.001f) hurt = 1;
            lastHp = hp; hurt = Mathf.Max(0, hurt - Time.deltaTime * 4);
            Vector3[] jl = debugL, jr = debugR;
            if (input && input.Active) { jl = input.left.present ? input.jointsL : null; jr = input.right.present ? input.jointsR : null; }
            Pose(rigs[0], jl, rage, hp);
            Pose(rigs[1], jr, rage, hp);
        }

        void Pose(Rig r, Vector3[] P, float rage, float hp)
        {
            if (r == null) return;
            bool on = P != null && P.Length >= 26;
            if (r.go.activeSelf != on) r.go.SetActive(on);
            if (!on) return;
            // 11-11 用户：手指再长一点 —— 每根手指从掌指关节（拇指从近节根）往外按 1.12 倍拉长（只拉显示，抓取判定仍按真手）
            for (int k = 0; k < 26; k++) Q[k] = P[k];
            foreach (var ch in Chains)
            {
                int root = ch[1];
                for (int i = 2; i < ch.Length; i++) Q[ch[i]] = P[root] + (P[ch[i]] - P[root]) * FingerStretch;
            }
            Frames(Q, r.right);
            for (int k = 0; k < 26; k++)
            {
                var b = r.bone[k]; if (!b || !has[k]) continue;
                b.SetPositionAndRotation(Q[k], frame[k] * r.off[k]);
            }
            r.smr.GetPropertyBlock(mpb);
            mpb.SetFloat("_Rage", rage); mpb.SetFloat("_Hp", hp); mpb.SetFloat("_Hurt", hurt);
            r.smr.SetPropertyBlock(mpb);
        }

        /// <summary>兼容旧的截图验收接口：蒙皮网格本身就是普通物体，截图相机能直接拍到</summary>
        public Transform Snapshot() { if (!ready) Init(); LateUpdate(); return transform; }
    }
}
