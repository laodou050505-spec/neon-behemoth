// 程序手部姿势（编辑器截图验收 / 电脑试玩时显示巨兽之手用）：按真实手的骨长和关节位置生成 26 个关节（PICO / OpenXR 编号）。
// 本地坐标：腕在原点，手背朝 +Y，手指指向 +Z；右手拇指在 −X（手背朝上看时拇指在左边），左手镜像。
using UnityEngine;

namespace Monster.Game
{
    public static class HandPoseLib
    {
        public enum Pose { Open, Pinch, Fist, Slap, Relaxed }
        // 掌指关节（近节指骨根）、指骨长度（近 / 中 / 远）
        static readonly Vector3[] McpPos = { new(-0.024f, 0, 0.085f), new(-0.006f, 0.002f, 0.09f), new(0.012f, 0.001f, 0.086f), new(0.028f, -0.002f, 0.075f) };
        static readonly Vector3[] CmcPos = { new(-0.016f, 0, 0.022f), new(-0.004f, 0.001f, 0.024f), new(0.008f, 0.001f, 0.022f), new(0.019f, 0, 0.018f) };
        static readonly float[][] Seg = { new[] { 0.043f, 0.025f, 0.022f }, new[] { 0.047f, 0.029f, 0.024f }, new[] { 0.044f, 0.028f, 0.023f }, new[] { 0.034f, 0.021f, 0.02f } };
        static readonly float[] Spread = { -7, -1.5f, 4, 10 };

        /// <summary>生成 26 个关节（世界坐标）和半径</summary>
        public static (Vector3[] j, float[] r) Make(Pose pose, bool left, Vector3 wristPos, Quaternion handRot)
        {
            var j = new Vector3[26]; var r = new float[26];
            float mx = left ? -1 : 1;
            Vector3 W(Vector3 p) => wristPos + handRot * new Vector3(p.x * mx, p.y, p.z);
            float[] curl = pose switch
            {
                Pose.Open => new[] { 4f, 4, 3 },
                Pose.Relaxed => new[] { 18f, 22, 14 },
                Pose.Pinch => new[] { 14f, 18, 12 },
                Pose.Fist => new[] { 82f, 95, 60 },
                Pose.Slap => new[] { 2f, 2, 2 },
                _ => new[] { 5f, 5, 5 },
            };
            float spreadK = pose == Pose.Open || pose == Pose.Slap ? 1.4f : pose == Pose.Fist ? 0.3f : 0.9f;
            j[1] = W(Vector3.zero); r[1] = 0.021f;
            j[0] = W(new Vector3(0.002f, 0, 0.05f)); r[0] = 0.02f;
            for (int f = 0; f < 4; f++)
            {
                int b = 6 + f * 5;
                j[b] = W(CmcPos[f]); r[b] = 0.01f;
                var p = McpPos[f];
                j[b + 1] = W(p); r[b + 1] = 0.0098f - f * 0.0006f;
                var dir = Quaternion.Euler(0, Spread[f] * spreadK, 0) * Vector3.forward;
                var axis = Vector3.Cross(Vector3.up, dir).normalized;   // 往掌心弯 = 绕 +X 侧轴转（手背 +Y 朝下压）
                float[] c = curl;
                if (pose == Pose.Pinch && f == 0) c = new[] { 38f, 42f, 28f };   // 食指去碰拇指
                float ang = 0;
                for (int s = 0; s < 3; s++)
                {
                    ang += c[s];
                    var d = Quaternion.AngleAxis(ang, axis) * dir;
                    p += d * Seg[f][s];
                    j[b + 2 + s] = W(p); r[b + 2 + s] = (0.0088f - f * 0.0006f) - s * 0.0009f;
                }
            }
            // 拇指：根在掌缘偏掌心，朝前外伸；捏合时指尖去碰食指尖，握拳时压在其余手指外面
            var t0 = new Vector3(-0.019f, -0.008f, 0.024f);
            j[2] = W(t0); r[2] = 0.0115f;
            Vector3 tdir = new Vector3(-0.62f, -0.15f, 0.77f).normalized;
            float[] ts = { 0.042f, 0.032f, 0.027f };
            if (pose == Pose.Pinch)
            {
                // 两段式：先朝食指尖方向伸，再让指尖落在食指尖（左右手在本地坐标里都按右手算）
                var target = Local(j[10], wristPos, handRot, mx) + new Vector3(-0.004f, -0.006f, 0);
                var p1 = t0 + tdir * ts[0];
                var p2 = Vector3.Lerp(p1, target, 0.55f) + new Vector3(-0.01f, -0.006f, 0);
                j[3] = W(p1); j[4] = W(p2); j[5] = W(target);
            }
            else if (pose == Pose.Fist)
            {
                var mid = Local(j[8], wristPos, handRot, mx);
                var p1 = t0 + new Vector3(-0.006f, -0.018f, 0.032f);
                var p2 = Vector3.Lerp(p1, mid, 0.5f) + new Vector3(0, -0.012f, 0);
                var p3 = mid + new Vector3(0.012f, -0.016f, -0.004f);
                j[3] = W(p1); j[4] = W(p2); j[5] = W(p3);
            }
            else
            {
                float tc = pose == Pose.Relaxed ? 18 : 6;
                var axis = Vector3.Cross(tdir, Vector3.down).normalized;
                var p = t0; float ang = 0;
                for (int s = 0; s < 3; s++)
                {
                    var d = Quaternion.AngleAxis(ang, axis) * tdir;
                    p += d * ts[s]; ang += tc;
                    j[3 + s] = W(p);
                }
            }
            r[3] = 0.0102f; r[4] = 0.0094f; r[5] = 0.0085f;
            return (j, r);
        }

        static Vector3 Local(Vector3 w, Vector3 wristPos, Quaternion rot, float mx) { var p = Quaternion.Inverse(rot) * (w - wristPos); return new Vector3(p.x * mx, p.y, p.z); }
    }
}
