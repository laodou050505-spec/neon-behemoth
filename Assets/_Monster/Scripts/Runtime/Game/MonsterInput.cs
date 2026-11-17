// 头显输入：PICO 手部追踪 + 眼动 → MonsterGame 的 GameInput。读法照《将军沙盘》MRInput（真机验证过）：
// 捏合 = 拇指食指尖距离（开 4.0cm / 松 5.5cm）且中无名小指至少一根没握死（握拳、自然蜷着不算捏）；
// 追踪丢 0.15 秒内保持上一帧；眼动拿不到（未校准 / 眨眼）时退回头部朝向。
// 手的位置：捏着时用捏合点（抓东西），否则用掌心（拍楼）；速度是掌心位置的平滑差分（拍 / 扔都看它）。
using System.Collections.Generic;
using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.XR;

namespace Monster.Game
{
    public class MonsterInput : MonoBehaviour
    {
        public Camera cam;
        public Transform origin;
        public float gazeSmoothing = 0.035f;
        const float PinchOn = 0.04f, PinchOff = 0.055f, LostGrace = 0.15f, VelSmooth = 0.04f;

        public bool Active => XRSettings.isDeviceActive && cam;
        public bool EyeTracked { get; private set; }
        public Ray Gaze { get; private set; }
        public readonly HandIn left = new() { side = "left" }, right = new() { side = "right" };
        /// <summary>给表现层画手用：每只手 26 个关节的世界坐标（没追踪到时为 null）</summary>
        public Vector3[] jointsL, jointsR;
        public float[] radL = new float[26], radR = new float[26];   // 关节半径（PICO 给的，巨兽之手按它定粗细）
        public string Diag { get; private set; } = "";

        HandJointLocations locs = new() { jointLocations = new HandJointLocation[(int)HandJoint.JointMax] };
        EyeTrackingData eye = new() { eyeDatas = new PerEyeData[(int)PerEyeUsage.EyeCount] };
        bool eyeStarted, eyeTried;
        readonly bool[] pinch = new bool[2], grip = new bool[2];
        readonly float[] lostT = { -9f, -9f }, tipDist = { 9f, 9f };
        readonly Vector3[] lastPalm = new Vector3[2];
        readonly bool[] hasLast = new bool[2];
        Vector3 gazeDir; float diagT;
        readonly GameInput gi = new() { hands = new List<HandIn>() };

        void OnDisable()
        {
            if (eyeStarted) { var stop = new EyeTrackingStopInfo(); PXR_MotionTracking.StopEyeTracking(ref stop); eyeStarted = false; eyeTried = false; }
        }

        Vector3 World(Vector3 p) => origin ? origin.TransformPoint(p) : p;
        Quaternion WorldRot(Quaternion q) => origin ? origin.rotation * q : q;

        bool ReadHand(HandType type, int side, HandIn h, ref Vector3[] joints)
        {
            if (!PXR_HandTracking.GetJointLocations(type, ref locs) || locs.isActive == 0 || locs.jointLocations == null) return false;
            const ulong need = (ulong)HandLocationStatus.PositionValid;
            bool Ok(HandJoint j) => ((ulong)locs.jointLocations[(int)j].locationStatus & need) != 0;
            if (!(Ok(HandJoint.JointWrist) && Ok(HandJoint.JointThumbTip) && Ok(HandJoint.JointIndexTip))) return false;
            joints ??= new Vector3[(int)HandJoint.JointMax];
            var rad = side == 0 ? radL : radR;
            for (int i = 0; i < joints.Length; i++) { joints[i] = World(locs.jointLocations[i].pose.Position.ToVector3()); rad[i] = locs.jointLocations[i].radius; }
            var J = joints;
            Vector3 P(HandJoint j) => J[(int)j];
            var wrist = P(HandJoint.JointWrist); var thumb = P(HandJoint.JointThumbTip);
            var tips = new[] { P(HandJoint.JointIndexTip), P(HandJoint.JointMiddleTip), P(HandJoint.JointRingTip), P(HandJoint.JointLittleTip) };
            var prox = new[] { P(HandJoint.JointIndexProximal), P(HandJoint.JointMiddleProximal), P(HandJoint.JointRingProximal), P(HandJoint.JointLittleProximal) };
            int ext = 0, curled = 0;
            for (int i = 0; i < 4; i++)
            {
                float r = Vector3.Distance(tips[i], wrist) / Mathf.Max(1e-3f, Vector3.Distance(prox[i], wrist));
                if (r > 1.45f) ext++;
                if (r < 1.22f) curled++;
            }
            float d = Vector3.Distance(thumb, tips[0]);
            tipDist[side] = d; h.pinchD = d;
            // 11-11 用户：捏经常拿不起来 —— 原来还要求“至少一根别的手指伸直”，整只手握过去就判不成。去掉；另加“握”（四指里三根以上蜷起来）
            pinch[side] = pinch[side] ? d < PinchOff : d < PinchOn;
            grip[side] = grip[side] ? curled >= 2 : curled >= 3;
            h.grip = grip[side];
            var palm = (wrist + prox[0] + prox[1] + prox[3]) * 0.25f;
            // 速度：掌心位置差分，再做一阶平滑（单帧追踪抖动不会被当成猛拍）
            float dt = Mathf.Max(1e-3f, Time.deltaTime);
            if (hasLast[side])
            {
                var v = (palm - lastPalm[side]) / dt;
                float k = 1f - Mathf.Exp(-dt / VelSmooth);
                h.vel = Vector3.Lerp(h.vel, v, k);
            }
            else h.vel = Vector3.zero;
            lastPalm[side] = palm; hasLast[side] = true;
            h.present = true; h.pinch = pinch[side]; h.open = ext >= 3;
            // 抓取点：捏 = 拇指食指之间；握 = 四指指尖和掌心的中间（手心里）
            h.pos = pinch[side] ? (thumb + tips[0]) * 0.5f : grip[side] ? (palm + (tips[1] + tips[2]) * 0.5f) * 0.5f : palm;
            // 手心法线：食指根、小指根相对手腕的叉乘（左手手心方向 = cross，右手相反；纯几何，和坐标系手性无关）
            var cr = Vector3.Cross(prox[0] - wrist, prox[3] - wrist).normalized;
            h.palm = palm; h.palmN = side == 0 ? cr : -cr;
            h.wrist = wrist; h.fwd = (P(HandJoint.JointMiddleProximal) - wrist).normalized;   // 手指方向（腕 → 中指根）：能量波掌心相对时按它定方向
            if (h.pts == null || h.pts.Length < 12) h.pts = new Vector3[12];
            h.pts[0] = palm; h.pts[1] = thumb; h.pts[2] = tips[0]; h.pts[3] = tips[1]; h.pts[4] = tips[2]; h.pts[5] = tips[3]; h.pts[6] = P(HandJoint.JointIndexIntermediate);
            // 11-13 用户：握拳砸下去要和往下拍一样 —— 握拳时指尖都缩在手心里，真正先碰到东西的是拳面（四个指根关节）和拳底（小指一侧的掌缘）：
            // 握着拳的时候把这几处也算成接触点（直拳、砸拳、手刀都靠它们）；张开手时不加（免得伸手去拿楼时手背蹭到旁边的楼）
            for (int i = 0; i < 4; i++) h.pts[7 + i] = prox[i];
            h.pts[11] = (prox[3] + wrist) * 0.5f;
            h.nPts = h.grip ? 12 : 7;
            return true;
        }

        bool ReadEye(out Ray ray)
        {
            ray = default;
            if (!eyeTried)
            {
                eyeTried = true;
                var start = new EyeTrackingStartInfo { needCalibration = 0, mode = EyeTrackingMode.PXR_ETM_BOTH };
                eyeStarted = PXR_MotionTracking.StartEyeTracking(ref start) == 0;
            }
            if (!eyeStarted) return false;
            var info = new EyeTrackingDataGetInfo { displayTime = 0, flags = EyeTrackingDataGetFlags.PXR_EYE_POSITION | EyeTrackingDataGetFlags.PXR_EYE_ORIENTATION };
            if (PXR_MotionTracking.GetEyeTrackingData(ref info, ref eye) != 0 || eye.eyeDatas == null) return false;
            var cmb = eye.eyeDatas[(int)PerEyeUsage.Combined];
            if (cmb.isPoseValid == 0) return false;
            if (cmb.isOpennessValid != 0 && cmb.openness < 0.2f) return false;
            var o = new Vector3(cmb.pose.position.x, cmb.pose.position.y, -cmb.pose.position.z);
            var q = new Quaternion(cmb.pose.orientation.x, cmb.pose.orientation.y, -cmb.pose.orientation.z, -cmb.pose.orientation.w);
            ray = new Ray(World(o), (WorldRot(q) * Vector3.forward).normalized);
            return true;
        }

        void Track(HandType type, int side, HandIn h, ref Vector3[] joints, bool handsOn)
        {
            float now = Time.time;
            if (handsOn && ReadHand(type, side, h, ref joints)) { lostT[side] = now; return; }
            if (now - lostT[side] < LostGrace) return;   // 丢一两帧：保持上一帧（不松开捏合）
            h.present = false; h.pinch = false; h.grip = false; pinch[side] = false; grip[side] = false; hasLast[side] = false; h.vel = Vector3.zero; joints = null;
        }

        /// <summary>每帧调用一次，返回给 MonsterGame.Tick 的输入</summary>
        int readFrame = -1;
        public GameInput Read()
        {
            if (readFrame == Time.frameCount) return gi;   // 同一帧多处调用（玩法 / 界面 / 漫画）只读一次，手速才不会被算成 0
            readFrame = Time.frameCount;
            gi.hands.Clear();
            if (!Active) { gi.hasHead = gi.hasGaze = false; return gi; }
            var t = cam.transform;
            bool handsOn = PXR_HandTracking.GetActiveInputDevice() == ActiveInputDevice.HandTrackingActive;
            Track(HandType.HandLeft, 0, left, ref jointsL, handsOn);
            Track(HandType.HandRight, 1, right, ref jointsR, handsOn);
            EyeTracked = ReadEye(out var e);
            var target = EyeTracked ? e : new Ray(t.position, t.forward);
            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, gazeSmoothing));
            gazeDir = gazeDir == Vector3.zero ? target.direction : Vector3.Slerp(gazeDir, target.direction, k);
            Gaze = new Ray(target.origin, gazeDir.normalized);
            gi.hasHead = true; gi.headPos = t.position; gi.headFwd = t.forward;
            gi.hasGaze = true; gi.gaze = Gaze; gi.eyeGaze = EyeTracked;
            gi.hands.Add(left); gi.hands.Add(right);
            if (Time.time - diagT > 5f)
            {
                diagT = Time.time;
                Diag = $"L={(left.present ? (left.pinch ? "捏" : "开") : "无")} {tipDist[0] * 100:0.0}cm R={(right.present ? (right.pinch ? "捏" : "开") : "无")} {tipDist[1] * 100:0.0}cm 眼动={EyeTracked}";
                Debug.Log("[怪兽][输入] " + Diag);
            }
            return gi;
        }
    }
}
