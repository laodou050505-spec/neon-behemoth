var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; v.paused = false; Time.timeScale = 0.15f;
Camera.main.transform.SetPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.Euler(35, 0, 0));
var o = GameObject.Find("HurtOverlay"); if (o) o.SetActive(false);
Monster.Game.Enemy m = null; foreach (var e in g.enemies) if (e.type == "mech" && !e.dead) m = e;
if (m == null) m = Monster.Game.Enemies.Spawn(g, "mech");
result.Log("mech id=" + m.id + " target=" + m.lockTarget.ToString("F3") + " dropT=" + m.dropT + " cell=" + m.zone.cell);
