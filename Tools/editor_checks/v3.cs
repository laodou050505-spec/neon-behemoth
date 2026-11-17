// 第七轮验收（固定文本，模式走 /tmp/monster_game/v3.txt）
var a = System.IO.File.ReadAllText("/tmp/monster_game/v3.txt").Trim().Split(' ');
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; var s = g.city.stations[0]; var z = g.city.zones[0];
var head = new Vector3(0, 1.6f, 0);
System.Action<string, Vector3, Vector3, float> S = (n, p, t, f) => Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/v3/" + n + ".png", p, Quaternion.LookRotation(t - p).eulerAngles, f, 1280, 800);
string msg = a[0];
switch (a[0])
{
    case "ts": Time.timeScale = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture); break;
    case "hit": s.hitCd = 0; g.ToppleAt(s, 0, null, "smash"); break;
    case "restart": s.restart = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture); break;
    case "sshot": { var p = s.Base + new Vector3(0.03f, 0.13f, -0.19f); S(a[1], p, s.Base + Vector3.up * 0.015f, 34); msg += " station h=" + s.h + " armor=" + s.armor + " restart=" + s.restart.ToString("F2"); break; }
    case "hurt": g.headPos = head; g.Hurt(float.Parse(a[1]), new Vector3(float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture), 1.3f, 0.6f), "bullet"); msg += " hp=" + g.hp + " result=" + g.result; break;
    case "hp": g.hp = float.Parse(a[1]); break;
    case "pov": Camera.main.transform.SetPositionAndRotation(head, Quaternion.Euler(25, 0, 0)); S(a[1], head, z.Center + Vector3.up * 0.15f, 80); msg += " hp=" + g.hp; break;
    case "orb":
        {
            Monster.Building b = null; foreach (var x in g.city.buildings) if (x.kind == Monster.BuildingKind.House && x.h > 0 && x.h <= 5 && x.zone == z && (b == null || Vector3.Distance(x.Base, z.Center) < Vector3.Distance(b.Base, z.Center))) b = x;
            b.hitCd = 0; g.ToppleAt(b, 0, null, "smash"); msg += " toppled " + b.id + " orbs=" + g.orbs.Count; break;
        }
    case "orbshot": if (g.orbs.Count > 0) { var o = g.orbs[0]; var p = o.pos + new Vector3(0.03f, 0.12f, -0.2f); S(a[1], p, o.pos, 34); } msg += " orbs=" + g.orbs.Count + " hp=" + g.hp; break;
    case "take":
        {
            if (g.orbs.Count == 0) { msg += " no orb"; break; }
            var op = g.orbs[0].pos;
            var h = new Monster.Game.HandIn { side = "right", present = true, open = false, pinch = true, grip = false, pinchD = 0.02f, pos = op, palm = op + Vector3.back * 0.06f, palmN = Vector3.forward, wrist = op + Vector3.back * 0.14f, fwd = Vector3.forward, vel = Vector3.zero, pts = new[] { op, op, op, op, op, op, op }, nPts = 7 };
            v.simInput = () => new Monster.Game.GameInput { hasHead = true, headPos = head, headFwd = Vector3.forward, hasGaze = true, gaze = new Ray(head, (op - head).normalized), hands = new System.Collections.Generic.List<Monster.Game.HandIn> { h } };
            break;
        }
    case "nosim": v.simInput = null; break;
    case "death":
        {
            var d = GameObject.Find("DeathText"); var t = d ? d.GetComponentInChildren<TMPro.TextMeshPro>() : null;
            msg += " deathText=" + (d != null) + (d ? " pos=" + d.transform.position.ToString("F2") + " scale=" + d.transform.localScale.x.ToString("F2") + " text=[" + (t ? t.text : "?") + "] bounds=" + (t ? t.GetComponent<Renderer>().bounds.size.ToString("F2") : "-") + " enabled=" + (t ? t.GetComponent<Renderer>().enabled.ToString() : "-") : "");
            if (d) S(a[1], head, d.transform.position, 80);
            break;
        }
    case "bullets":
        {
            var from = z.Center + new Vector3(-0.3f, 0.25f, 0.1f);
            for (int i = 0; i < 3; i++) { var p0 = Vector3.Lerp(from, head, 0.25f + i * 0.2f) + new Vector3(i * 0.05f, 0, 0); g.projectiles.Add(new Monster.Game.Projectile { kind = i == 1 ? "shell" : "bullet", pos = p0, vel = (head - from).normalized * 0.9f, dmg = 3, from = -1 }); }
            msg += " projectiles=" + g.projectiles.Count; break;
        }
}
result.Log(msg + " | t=" + g.time.ToString("F1") + " hp=" + g.hp + " station armor=" + s.armor + " h=" + s.h);
