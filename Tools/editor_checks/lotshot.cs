Time.timeScale = 0f; var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; var z = g.city.zones.Find(q => q.kind == Monster.ZoneKind.Table); var c = z.Center;
System.Action<string, Vector3, Vector3, float, Vector3> S = (n, p, t, f, up) => Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/lot2/" + n + ".png", p, Quaternion.LookRotation(t - p, up).eulerAngles, f, 1600, 1000);
S("top_new", c + Vector3.up * 1.0f, c, 62, Vector3.forward);
var t0 = g.city.towers[0]; var s0 = g.city.stations[0];
S("tower_new", t0.Base + new Vector3(0.03f, 0.3f, -0.26f), t0.Base + Vector3.up * 0.05f, 50, Vector3.up);
S("station_new", s0.Base + new Vector3(0.0f, 0.2f, -0.2f), s0.Base, 50, Vector3.up);
S("stand_new", new Vector3(0, 1.6f, 0), c + Vector3.up * 0.1f, 75, Vector3.up);
string zs = ""; foreach (var q in g.city.zones) zs += q.id + ":" + q.kind + " " + q.w.ToString("F2") + "x" + q.h.ToString("F2") + " ";
result.Log("zones " + zs + "| towers=" + g.city.towers.Count + " stations=" + g.city.stations.Count + " buildings=" + g.city.buildings.Count + " tower fw=" + t0.fw.ToString("F3") + " lot=" + t0.lotW.ToString("F3") + " station " + s0.fw.ToString("F3") + "x" + s0.fd.ToString("F3") + " lot " + s0.lotW.ToString("F3") + "x" + s0.lotD.ToString("F3") + " armor=" + s0.armor);
