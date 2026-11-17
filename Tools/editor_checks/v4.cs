var a = System.IO.File.ReadAllText("/tmp/monster_game/v4.txt").Trim().Split(' ');
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; var z = g.city.zones[0];
var head = new Vector3(0, 1.6f, 0); float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
System.Action<string, Vector3, Vector3, float> S = (n, p, t, f) => Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/v4/" + n + ".png", p, Quaternion.LookRotation(t - p).eulerAngles, f, 1280, 800);
string msg = a[0];
switch (a[0])
{
    case "ts": Time.timeScale = F(1); break;
    case "pov": Camera.main.transform.SetPositionAndRotation(head, Quaternion.Euler(F(2), a.Length > 3 ? F(3) : 0, 0)); S(a[1], head, head + Quaternion.Euler(F(2), a.Length > 3 ? F(3) : 0, 0) * Vector3.forward, 90); break;
    case "hand":
        {
            var hp0 = head + new Vector3(-0.12f, -0.38f, 0.38f);
            var h = new Monster.Game.HandIn { side = "left", present = true, open = true, pinch = false, grip = false, pinchD = 0.1f, pos = hp0, palm = hp0, palmN = Vector3.down, wrist = hp0 - Vector3.forward * 0.08f, fwd = Vector3.forward, vel = Vector3.zero, pts = new[] { hp0, hp0, hp0, hp0, hp0, hp0, hp0 }, nPts = 7 };
            v.simInput = () => new Monster.Game.GameInput { hasHead = true, headPos = head, headFwd = Vector3.forward, hasGaze = true, gaze = new Ray(head, Vector3.forward), hands = new System.Collections.Generic.List<Monster.Game.HandIn> { h } };
            break;
        }
    case "handshot": { var hp0 = head + new Vector3(-0.12f, -0.38f, 0.38f); S(a[1], head, hp0, 34); break; }
    case "set": if (a[1] == "hp") g.hp = F(2); else if (a[1] == "rage") g.rage = F(2); else if (a[1] == "seal") g.seal = g.sealMax - F(2); break;
    case "hurt": g.headPos = head; g.Hurt(F(1), new Vector3(F(2), 1.3f, 0.6f), "bullet"); break;
    case "bullets": { var from = z.Center + new Vector3(F(1), 0.25f, 0.1f); for (int i = 0; i < 2; i++) g.projectiles.Add(new Monster.Game.Projectile { kind = i == 1 ? "shell" : "bullet", pos = Vector3.Lerp(from, head, 0.3f + i * 0.25f), vel = (head - from).normalized * 0.9f, dmg = 3, from = -1 }); break; }
    case "police": { var e = Monster.Game.Enemies.Spawn(g, "police", z); msg += e != null ? " at " + e.pos : " null"; break; }
    case "policeshot": { Monster.Game.Enemy e = null; foreach (var x in g.enemies) if (!x.dead && x.type == a[2]) e = x; if (e != null) { var p = e.pos + new Vector3(0.05f, 0.06f, -0.1f); S(a[1], p, e.pos + Vector3.up * 0.012f, 30); } msg += " found=" + (e != null); break; }
    case "nukeshot": { var n = GameObject.Find("Nuke"); if (n && n.activeInHierarchy) { var np = n.transform.position; var p = np + new Vector3(0.22f, 0.05f, -0.3f); S(a[1], p, np, 40); msg += " nuke at " + np.ToString("F2") + " scale=" + n.transform.lossyScale.x.ToString("F4"); } else msg += " no nuke"; break; }
    case "pause": { var pb = Object.FindAnyObjectByType<Monster.Game.PauseButton>(); if (pb) { var p = pb.Pos + new Vector3(0.06f, 0.14f, -0.16f); S(a[1], p, pb.Pos, 34); } msg += " btn=" + (pb != null); break; }
}
result.Log(msg + " | hp=" + g.hp.ToString("F0") + " rage=" + g.rage + " seal=" + (g.sealMax - g.seal).ToString("F1") + " nukeT=" + g.nukeT.ToString("F2") + " result=" + g.result + " stars=" + g.stars);
