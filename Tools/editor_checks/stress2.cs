// 后期压测：五星、拆 30 栋楼（一半带方向 = 连锁）、点着 14 栋，核打击时限拉长。之后看编辑器日志里的 [怪兽][性能]
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; g.sealMax = 99999; v.paused = false; Time.timeScale = 1f;
var ov = Object.FindAnyObjectByType<Monster.Game.HurtOverlay>();
Camera.main.transform.SetPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.Euler(35, 0, 0));
g.AddHeat(200);
int n = 0, f = 0; var rnd = new System.Random(5);
foreach (var b in g.city.buildings)
{
    if (b.kind != Monster.BuildingKind.House || b.h <= 0) continue;
    if (n < 30 && rnd.NextDouble() < 0.55) { var d = Quaternion.Euler(0, rnd.Next(360), 0) * Vector3.forward; g.ToppleAt(b, rnd.Next(2) == 0 ? 0 : b.h / 2, rnd.Next(2) == 0 ? d : (Vector3?)null, "smash"); n++; }
    else if (f < 14) { g.Ignite(b); f++; }
}
g.hp = 22;
result.Log("stress: stars=" + g.stars + " toppled=" + n + " ignited=" + f + " buildings=" + g.city.buildings.Count);
