// 新规则验证（参数：beam_tower / beam_station / beam_open / tap / read）
string mode = System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim();
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; Time.timeScale = 1f;
var ov = Object.FindAnyObjectByType<Monster.Game.HurtOverlay>(); if (ov) { ov.enabled = false; var q = GameObject.Find("HurtOverlay"); if (q) q.SetActive(false); }
var head = new Vector3(0, 1.6f, 0); var tw = g.city.towers[0]; Monster.Building st = null; foreach (var s in g.city.stations) if (s.towerId == tw.id && s.h > 0) { st = s; break; }
string S() => "塔 " + tw.h + "/" + tw.maxH + " 护盾=" + g.IsShielded(tw) + " 破盾=" + tw.shieldBroken + " | 电站 h=" + (st != null ? st.h : -1) + " 甲=" + (st != null ? st.armor : -1) + " | 怒气=" + g.rage.ToString("F0") + " 大招剩 " + g.breathing.ToString("F1") + " 暂停=" + v.paused + " seal=" + g.seal.ToString("F0");
if (mode == "read") { result.Log(S()); return; }
var gi = new Monster.Game.GameInput { hasHead = true, headPos = head, headFwd = Vector3.forward, hasGaze = true, hands = new System.Collections.Generic.List<Monster.Game.HandIn>() };
// 光束从目标旁边 12cm 处打过去（中间没有别的楼挡着）；不给手的数据（给了空列表的话 0.35 秒后会被判成“手分开了”而停掉）
void Aim(Vector3 to) { var from = to + (new Vector3(head.x, to.y, head.z) - to).normalized * 0.12f + Vector3.up * 0.06f; gi.hands = null; gi.gaze = new Ray(from, (to - from).normalized); v.simInput = () => gi; g.rage = 0; g.breathing = 1.25f; g.beam = null; v.paused = false; }
if (mode == "beam_tower") { Aim(tw.Base + Vector3.up * (tw.Height * 0.7f)); }
if (mode == "beam_station") { Aim(st.Base + Vector3.up * 0.01f); }
if (mode == "beam_open") { foreach (var s in g.city.stations) if (s.towerId == tw.id) { s.h = 0; s.online = false; s.restart = 999; } Aim(tw.Base + Vector3.up * (tw.Height * 0.5f)); }
if (mode == "tap")
{
    gi.hands = new System.Collections.Generic.List<Monster.Game.HandIn>();
    var pb = Object.FindAnyObjectByType<Monster.Game.PauseButton>(); var top = pb.Pos; v.paused = false; g.breathing = 0;
    var h = new Monster.Game.HandIn { side = "right", present = true, pts = new Vector3[12], nPts = 7, pinchD = 0.11f, open = true, fwd = Vector3.down, palmN = Vector3.back };
    h.palm = h.pos = top + new Vector3(0, 0.09f, -0.03f); h.wrist = h.palm + new Vector3(0, 0.06f, -0.04f); for (int i = 0; i < 12; i++) h.pts[i] = h.palm; h.pts[2] = top + Vector3.up * 0.006f;
    gi.hands.Add(h); gi.gaze = new Ray(head, Vector3.forward); v.simInput = () => gi;
}
result.Log(mode + " 前：" + S());
