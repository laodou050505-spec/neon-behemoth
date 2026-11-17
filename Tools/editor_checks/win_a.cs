var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; v.paused = false; Time.timeScale = 0.25f;
Camera.main.transform.SetPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.Euler(35, 0, 0));
foreach (var t in g.city.towers) t.shieldBroken = true;
Monster.Building last = null; foreach (var t in g.city.towers) if (t.h > 0) last = t;
foreach (var t in g.city.towers) if (t != last && t.h > 0) g.ToppleAt(t, 0, null, "breath");
bool up = last != null && g.Uproot("left", last);
if (up) g.HoldAt("left", last.Base + Vector3.up * 0.25f);
result.Log("towers left=" + System.Linq.Enumerable.Count(g.city.towers, t => t.h > 0) + " uproot=" + up + " held=" + g.held.Count + " winT=" + g.winT + " result=" + g.result);
