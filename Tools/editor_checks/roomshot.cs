foreach (var pm in Object.FindObjectsByType<Monster.Game.PalmMenu>()) pm.enabled = false;
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; v.paused = false; Time.timeScale = 0f;
var head = new Vector3(0, 1.6f, 0); Camera.main.transform.SetPositionAndRotation(head, Quaternion.Euler(35, 0, 0));
System.Action<string, Vector3, Vector3, float, Vector3> S = (n, p, t, f, up) => Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/l4/" + n + ".png", p, Quaternion.LookRotation(t - p, up).eulerAngles, f, 1600, 1000);
S("room_top", new Vector3(0, 3.2f, 0.25f), new Vector3(0, 0, 0.25f), 50, Vector3.forward);
S("room_back", new Vector3(0, 2.3f, -1.5f), new Vector3(0, 0.9f, 0.4f), 60, Vector3.up);
string zs = "";
foreach (var z in g.city.zones) { zs += z.id + ":" + z.kind + " " + z.w.ToString("F2") + "x" + z.h.ToString("F2") + " y=" + z.origin.y.ToString("F2") + " c=" + z.Center.ToString("F2") + " | "; S("pov_" + z.id, head, z.Center + Vector3.up * 0.05f, 75, Vector3.up); }
int onT = 0; foreach (var t in g.city.towers) onT += g.city.zones.IndexOf(t.zone) == 0 ? 1 : 0;
result.Log(zs + " towers on main=" + onT + "/" + g.city.towers.Count + " hp=" + g.hp + " armor=" + g.city.stations[0].armor + " enemies=" + g.enemies.Count);
