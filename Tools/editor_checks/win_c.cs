var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; Time.timeScale = 0.04f; v.paused = false;
Camera.main.transform.SetPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.Euler(35, 0, 0));
foreach (var t in g.city.towers) t.shieldBroken = true;
foreach (var t in g.city.towers) if (t.h > 0) g.ToppleAt(t, 0, null, "breath");
result.Log("toppled; enemies=" + g.enemies.Count);
