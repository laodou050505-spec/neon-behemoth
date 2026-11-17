var n = System.IO.File.ReadAllText("/tmp/monster_game/wrapn.txt").Trim();
var a = System.IO.File.ReadAllText("/tmp/monster_game/wrap.txt").Trim().Split(' ');
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; var b = g.city.byId[int.Parse(a[0])];
var up = GameObject.Find("Uprooted"); string gs = "none";
Vector3 look = b.Base + Vector3.up * 0.08f;
if (up) { Bounds bb = new Bounds(up.transform.position, Vector3.zero); bool f = true; foreach (var r in up.GetComponentsInChildren<Renderer>()) { if (f) { bb = r.bounds; f = false; } else bb.Encapsulate(r.bounds); } gs = "center=" + bb.center.ToString("F2") + " size=" + bb.size.ToString("F2") + " bottom-above-table=" + ((bb.min.y - b.zone.origin.y) * 100).ToString("F1") + "cm"; look = bb.center; }
var p = look + b.zone.v * 0.5f + b.zone.u * 0.15f + Vector3.up * 0.25f;
Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/wrap/" + n + ".png", p, Quaternion.LookRotation(look - p).eulerAngles, 38, 1280, 900);
string held = ""; foreach (var kv in g.held) held += kv.Key + ":" + kv.Value.kind + (kv.Value.wrap ? "(wrap span0=" + kv.Value.span0.ToString("F3") + ")" : "") + " ";
result.Log(n + ": b.h=" + b.h + "/" + b.maxH + " held=[" + held + "] flying=" + g.flying.Count + " group " + gs);
