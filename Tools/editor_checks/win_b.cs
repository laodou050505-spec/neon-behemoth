var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; Time.timeScale = 0.12f;
g.Release("left", new Vector3(0.3f, 1.2f, 1.6f), null);
result.Log("released held=" + g.held.Count + " flying=" + g.flying.Count);
