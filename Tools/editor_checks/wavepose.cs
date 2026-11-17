// 能量波验收：用程序手势摆出大招姿势（POSE = cup 捧球 / push 推掌 / kame 上下开合），巨兽之手和光波一起显示。
// 手的输入按 MonsterInput.ReadHand 同样的几何公式从 26 个关节算（掌心、手心法线、手指方向），所以能验证真机那条路。
foreach (var pm in Object.FindObjectsByType<Monster.Game.PalmMenu>(FindObjectsSortMode.None)) pm.enabled = false;
var view = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = view.game; view.paused = false;
g.rage = RAGE;
var cam = Camera.main.transform; cam.SetPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.Euler(35, 0, 0));   // 编辑器的键盘模拟会把相机挪走，每次复位
var head = cam.position;
// 目标：离城区中心最近的一栋还立着的楼
Monster.Building tb = null; float bd = 1e9f; var zc = Vector3.zero; int zn = 0;
foreach (var b in g.city.buildings) if (b.zone.kind == Monster.ZoneKind.Table) { zc += b.Base; zn++; }
zc /= Mathf.Max(1, zn);
foreach (var b in g.city.buildings) if (b.zone.kind == Monster.ZoneKind.Table && b.h >= 3) { float dd = Vector3.Distance(b.Base, zc + new Vector3(TX, 0, TZ)); if (dd < bd) { bd = dd; tb = b; } }
var target = tb.Base + Vector3.up * 0.05f;
var M = head + new Vector3(MX, MY, 0.33f);
var A = (target - M).normalized; var Rt = Vector3.Cross(Vector3.up, A).normalized; var Uo = Vector3.Cross(A, Rt).normalized;
string pose = "POSE";
Vector3 wr, wl; Quaternion qr, ql;
if (pose == "cup") { qr = Quaternion.LookRotation(A, Rt); ql = Quaternion.LookRotation(A, -Rt); wr = M + Rt * 0.055f - A * 0.06f; wl = M - Rt * 0.055f - A * 0.06f; }
else if (pose == "push") { qr = Quaternion.LookRotation(Uo, -A); ql = qr; wr = M + Rt * 0.05f - Uo * 0.06f; wl = M - Rt * 0.05f - Uo * 0.06f; }
else { var fu = (Uo * 0.85f + A * 0.25f).normalized; var fd = (-Uo * 0.85f + A * 0.25f).normalized; qr = Quaternion.LookRotation(fu, Vector3.Cross(fu, Rt)); ql = Quaternion.LookRotation(fd, Vector3.Cross(Rt, fd)); wr = M + Uo * 0.012f - A * 0.01f; wl = M - Uo * 0.012f - A * 0.01f; }
var (jr, rr) = Monster.Game.HandPoseLib.Make(Monster.Game.HandPoseLib.Pose.Open, false, wr, qr);
var (jl, rl) = Monster.Game.HandPoseLib.Make(Monster.Game.HandPoseLib.Pose.Open, true, wl, ql);
var hv = Object.FindAnyObjectByType<Monster.Game.HandView>();
hv.debugR = jr; hv.debugRadR = rr; hv.debugL = jl; hv.debugRadL = rl;
Monster.Game.HandIn H(string side, Vector3[] J)
{
    var wrist = J[1]; var palm = (wrist + J[7] + J[12] + J[22]) * 0.25f;
    var cr = Vector3.Cross(J[7] - wrist, J[22] - wrist).normalized;
    return new Monster.Game.HandIn { side = side, present = true, open = true, pinch = false, grip = false, pinchD = Vector3.Distance(J[5], J[10]), pos = palm, palm = palm, palmN = side == "left" ? cr : -cr, wrist = wrist, fwd = (J[12] - wrist).normalized, vel = Vector3.zero, pts = new[] { palm, J[5], J[10], J[15], J[20], J[25], J[8] }, nPts = 7 };
}
var hl = H("left", jl); var hr = H("right", jr);
var hands = new System.Collections.Generic.List<Monster.Game.HandIn> { hl, hr };
var gz = new Ray(head, (GAZE - head).normalized);
view.simInput = () => new Monster.Game.GameInput { hasHead = true, headPos = head, headFwd = cam.forward, hasGaze = true, gaze = gz, hands = hands };
System.IO.File.WriteAllText("/tmp/monster_game/wave_target.txt", $"{head.x} {head.y} {head.z} {M.x} {M.y} {M.z} {target.x} {target.y} {target.z}");
result.Log($"pose={pose} mid={(hl.palm + hr.palm) * 0.5f} gap={Vector3.Distance(hl.palm, hr.palm):F3} nL={hl.palmN:F2} nR={hr.palmN:F2} fL={hl.fwd:F2} A={A:F2} target={target} b={tb.id}");
