// 机甲抓 / 扔验证。参数：spawn（放一台并让它马上落地站稳）/ grab / throw / drop / hold（一直攥着等它挣脱）/ state / shot 名字
var a = System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim().Split(' ');
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; g.sealMax = 99999; v.paused = false; Time.timeScale = a.Length > 2 ? float.Parse(a[2]) : 1f;
var ov = Object.FindAnyObjectByType<Monster.Game.HurtOverlay>(); if (ov) { ov.enabled = false; var q = GameObject.Find("HurtOverlay"); if (q) q.SetActive(false); }
Monster.Game.Enemy m = null; foreach (var e in g.enemies) if (e.type == "mech" && !e.dead) m = e;
string S() => m == null ? "没有机甲" : "hp=" + m.hp + " state=" + m.state + " air=" + m.air + " hop=" + m.hop.ToString("F2") + " heldT=" + m.heldT.ToString("F1") + " stagger=" + m.stagger.ToString("F1") + " pos=" + m.pos.ToString("F2") + " 手里 " + g.held.Count + " 玩家血 " + g.hp.ToString("F0");
if (a[0] == "spawn") { foreach (var e in g.enemies) if (e.type == "mech") { e.dead = true; e.state = Monster.Game.EState.Dead; } m = Monster.Game.Enemies.Spawn(g, "mech"); m.dropT = 0.05f; v.simInput = null; }
if (a[0] == "grab" || a[0] == "hold")
{
    bool ok = g.GrabEnemy("right", m); float t0 = Time.time; var p0 = m.pos + Vector3.up * 0.05f; var top = new Vector3(0, 1.25f, 0.4f);
    var h = new Monster.Game.HandIn { side = "right", present = true, pinch = true, grip = false, pts = new Vector3[12], nPts = 7, pinchD = 0.015f, open = false };
    var gi = new Monster.Game.GameInput { hasHead = true, headPos = new Vector3(0, 1.6f, 0), headFwd = Vector3.forward, hasGaze = true, gaze = new Ray(new Vector3(0, 1.6f, 0), Vector3.forward), hands = new System.Collections.Generic.List<Monster.Game.HandIn> { h } };
    System.IO.File.WriteAllText("/tmp/monster_game/mg_release.txt", "0");
    v.simInput = () =>
    {
        float k = Mathf.Clamp01((Time.time - t0) / 0.7f); h.pos = h.palm = Vector3.Lerp(p0, top, k * k * (3 - 2 * k)); for (int i = 0; i < 12; i++) h.pts[i] = h.pos;
        string rl = System.IO.File.ReadAllText("/tmp/monster_game/mg_release.txt").Trim();
        h.vel = Vector3.zero; h.pinch = true; h.pinchD = 0.015f;
        if (rl == "throw") { h.pinch = false; h.pinchD = 0.09f; h.vel = new Vector3(0.3f, -0.6f, 1.6f); }
        if (rl == "drop") { h.pinch = false; h.pinchD = 0.09f; h.vel = Vector3.zero; }
        return gi;
    };
    result.Log("grab ok=" + ok + " | " + S()); return;
}
if (a[0] == "throw" || a[0] == "drop") System.IO.File.WriteAllText("/tmp/monster_game/mg_release.txt", a[0]);
if (a[0] == "shot")
{
    var c = m != null ? m.pos : new Vector3(0, 1, 0.5f); var pos = c + new Vector3(0.22f, 0.16f, -0.26f);
    Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/" + a[1] + ".png", pos, Quaternion.LookRotation(c + Vector3.up * 0.04f - pos).eulerAngles, 45, 1100, 900);
}
result.Log(a[0] + " | " + S());
