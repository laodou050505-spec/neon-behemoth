var a = System.IO.File.ReadAllText("/tmp/monster_game/wstep.txt").Trim();
Time.timeScale = float.Parse(a, System.Globalization.CultureInfo.InvariantCulture);
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game;
result.Log("ts=" + Time.timeScale + " time=" + g.time.ToString("F3") + " charge=" + g.charge.ToString("F2") + " want=" + g.waveWant + " breathing=" + g.breathing.ToString("F3") + " rage=" + g.rage + " beam=" + (g.beam.HasValue ? g.beam.Value.origin.ToString("F3") + " dir " + g.beam.Value.direction.ToString("F2") : "-") + " end=" + (g.breathEnd.HasValue ? g.breathEnd.Value.ToString("F3") : "-"));
