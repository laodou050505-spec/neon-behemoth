// 量“把一栋楼从合批网格里拆出来”要多久：对 8 栋完好的楼各调一次 Detach
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; var sb = new System.Text.StringBuilder(); int n = 0; double total = 0, worst = 0;
foreach (var b in g.city.buildings)
{
    if (b.kind != Monster.BuildingKind.House || b.h != b.maxH || n >= 8) continue;
    var view = v.builder.View(b.id); if (!view) continue;
    double t0 = Time.realtimeSinceStartupAsDouble; v.builder.Detach(view);
    double ms = (Time.realtimeSinceStartupAsDouble - t0) * 1000; if (ms < 0.01) continue;
    total += ms; worst = System.Math.Max(worst, ms); n++; sb.Append(" " + b.style + b.maxH + "层:" + ms.ToString("F1"));
}
result.Log("Detach " + n + " 栋，平均 " + (total / System.Math.Max(1, n)).ToString("F1") + "ms，最慢 " + worst.ToString("F1") + "ms |" + sb);
