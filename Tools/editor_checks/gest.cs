// 手势验证：给玩法层喂模拟的手。参数：fist（右手握拳砸 A 楼、左手张掌拍 B 楼）/ quake（双拳砸地）/ clap（合掌拍直升机）/ beam（大招照一栋楼）/ read（读结果）
string mode = System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim();
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; v.paused = false; Time.timeScale = 1f;
var ov = Object.FindAnyObjectByType<Monster.Game.HurtOverlay>(); if (ov) { ov.enabled = false; var q = GameObject.Find("HurtOverlay"); if (q) q.SetActive(false); }
var z = g.city.zones[0];
var houses = new System.Collections.Generic.List<Monster.Building>(); foreach (var b in g.city.buildings) if (b.zone == z && b.kind == Monster.BuildingKind.House && b.h >= 5 && b.h == b.maxH) houses.Add(b);
string F(Monster.Building b) => "#" + b.id + " " + b.h + "/" + b.maxH;
if (mode == "read") { var sb = new System.Text.StringBuilder(); foreach (var line in System.IO.File.ReadAllLines("/tmp/monster_game/gest_ids.txt")) { var b = g.city.byId[int.Parse(line)]; sb.Append(" " + F(b)); } int fl = 0, gr = 0; foreach (var e in g.enemies) if (!e.dead) { if (Monster.Game.Enemies.Types[e.type].fly) fl++; else gr++; } result.Log("楼" + sb + " | 飞行 " + fl + " 地面 " + gr + " breathing=" + g.breathing.ToString("F1") + " time=" + g.time.ToString("F1")); return; }
float t0 = Time.time; var head = new Vector3(0, 1.6f, 0);
Monster.Game.HandIn Hand(string side) { var h = new Monster.Game.HandIn { side = side, present = true, pts = new Vector3[12], nPts = 7, palmN = Vector3.down, fwd = Vector3.forward }; return h; }
void Put(Monster.Game.HandIn h, Vector3 p, Vector3 vel, bool fist) { h.pos = h.palm = p; h.wrist = p - Vector3.forward * 0.08f; h.vel = vel; h.grip = fist; h.pinch = fist; h.pinchD = fist ? 0.03f : 0.11f; h.open = !fist; h.nPts = fist ? 12 : 7; for (int i = 0; i < 12; i++) h.pts[i] = p + new Vector3(((i % 3) - 1) * 0.02f, 0, ((i / 3) - 1.5f) * 0.015f); }
var L = Hand("left"); var R = Hand("right"); var gi = new Monster.Game.GameInput { hasHead = true, headPos = head, headFwd = Vector3.forward, hands = new System.Collections.Generic.List<Monster.Game.HandIn> { L, R } };
var ids = new System.Collections.Generic.List<int>();
if (mode == "fist")
{
    var A = houses[0]; var B = houses[houses.Count - 1]; ids.Add(A.id); ids.Add(B.id);
    Vector3 ta = Monster.Game.MonsterGame.TopOf(A), tb = Monster.Game.MonsterGame.TopOf(B);
    v.simInput = () => { float t = Time.time - t0; float d = Mathf.Max(0, t - 0.7f) * 1.0f; bool mv = t > 0.7f && d < 0.33f; var vel = mv ? Vector3.down : Vector3.zero;
        Put(R, ta + Vector3.up * (0.25f - Mathf.Min(d, 0.33f)), vel, true); Put(L, tb + Vector3.up * (0.25f - Mathf.Min(d, 0.33f)), vel, false); L.present = R.present = t < 1.3f; return gi; };
    result.Log("fist：右拳砸 " + F(A) + "，左掌拍 " + F(B));
}
else if (mode == "quake")
{
    // 找主城区里一个路口（不压着楼）
    var c = z.Point(z.cell * 2.5f, z.cell * 2.5f); float r = 0.24f * g.S + 0.04f;
    foreach (var b in g.city.buildings) if (b.zone == z && b.h > 0 && b.kind == Monster.BuildingKind.House && Vector3.Distance(b.Base, c) < r) ids.Add(b.id);
    for (int i = 0; i < 3; i++) { var e = Monster.Game.Enemies.Spawn(g, i == 0 ? "tank" : "police", z); if (e != null) { e.pos = c + new Vector3(0.05f * (i - 1), 0.004f, 0.06f); } }
    v.simInput = () => { float t = Time.time - t0; float d = Mathf.Max(0, t - 0.7f) * 1.3f; bool mv = t > 0.7f && d < 0.28f; var vel = mv ? Vector3.down * 1.3f : Vector3.zero;
        Put(L, c + new Vector3(-0.1f, 0.3f - Mathf.Min(d, 0.28f), 0), vel, true); Put(R, c + new Vector3(0.1f, 0.3f - Mathf.Min(d, 0.28f), 0), vel, true); L.present = R.present = t < 1.2f; return gi; };
    result.Log("quake：双拳砸 " + c.ToString("F2") + " 半径 " + r.ToString("F2") + "，圈内普通楼 " + ids.Count + " 栋");
}
else if (mode == "clap")
{
    var c = head + new Vector3(0, -0.25f, 0.45f);
    var e1 = Monster.Game.Enemies.Spawn(g, "bomber"); e1.pos = c; e1.fwd = Vector3.right; e1.cd = 99;
    v.simInput = () => { float t = Time.time - t0; float half = Mathf.Max(0.02f, 0.3f - Mathf.Max(0, t - 0.5f) * 0.7f); bool mv = t > 0.5f && half > 0.021f; e1.pos = c;
        Put(L, c + Vector3.left * half, mv ? Vector3.right * 0.7f : Vector3.zero, false); Put(R, c + Vector3.right * half, mv ? Vector3.left * 0.7f : Vector3.zero, false); L.present = R.present = t < 1.3f; return gi; };
    result.Log("clap：两掌之间放了一架轰炸机（2 格血）");
}
else if (mode == "beam")
{
    var A = houses[houses.Count / 2]; ids.Add(A.id);
    foreach (var b in g.city.buildings) if (b != A && b.zone == z && b.h > 0 && Vector3.Distance(b.Base, A.Base) < 0.12f) ids.Add(b.id);
    var from = new Vector3(A.Base.x * 0.3f, 1.25f, 0.25f); var to = A.Base + Vector3.up * (A.Height * 0.6f);
    gi.hands.Clear(); gi.hasGaze = true; gi.gaze = new Ray(from, (to - from).normalized);
    v.simInput = () => gi; g.breathing = 1.2f; g.beam = null;
    System.IO.File.WriteAllText("/tmp/monster_game/mechpos.txt", A.Base.x.ToString("F3") + " " + A.Base.y.ToString("F3") + " " + A.Base.z.ToString("F3"));
    Time.timeScale = 0.12f;
    result.Log("beam：照 " + F(A) + "，旁边 " + (ids.Count - 1) + " 栋");
}
var lines = new System.Collections.Generic.List<string>(); foreach (var i in ids) lines.Add(i.ToString()); System.IO.File.WriteAllLines("/tmp/monster_game/gest_ids.txt", lines);
