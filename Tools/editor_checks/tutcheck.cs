var m = System.IO.File.ReadAllText("/tmp/monster_game/tc.txt").Trim();
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; var s = g.city.stations[0]; var tw = g.city.byId[s.towerId];
if (m == "scan") { tw.scanUntil = g.time + 6; g.Emit(new Monster.Game.Ev { type = "scan", id = tw.id }); }
if (m == "hit") { s.hitCd = 0; g.ToppleAt(s, 0, null, "smash"); }
if (m == "hurt") { g.headPos = new Vector3(0, 1.6f, 0); g.Hurt(30, new Vector3(0.3f, 1.3f, 0.6f), "test"); Monster.Building b = null; foreach (var x in g.city.buildings) if (x.kind == Monster.BuildingKind.House && x.h > 0 && x.h <= 6 && x.zone == g.city.zones[0]) { b = x; break; } b.hitCd = 0; g.ToppleAt(b, 0, null, "smash"); }
result.Log(m + ": hint=[" + g.tut.Hint + "] station armor=" + s.armor + " h=" + s.h + " restart=" + s.restart.ToString("F0") + " orbs=" + g.orbs.Count + " hp=" + g.hp + " chain=" + g.tut.Chain);
