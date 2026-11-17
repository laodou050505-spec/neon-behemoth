// 结局验收：把所有塔拆掉 → 等胜利过程 → 结局开始。参数：时间倍率
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; v.paused = false;
Time.timeScale = float.Parse(System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim());
Camera.main.transform.SetPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.Euler(35, 0, 0));
foreach (var t in g.city.towers) t.shieldBroken = true;
foreach (var t in g.city.towers) if (t.h > 0) g.ToppleAt(t, 0, null, "breath");
result.Log("toppled level=" + System.Array.IndexOf(Monster.Game.Level.All, g.level) + " towers=" + g.city.towers.Count + " hp=" + g.hp);
