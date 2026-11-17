// 固定文本（不触发重编译）：输出目录和编号从 /tmp/monster_game/wshot.txt 读（"目录 编号 [close]"）
var a = System.IO.File.ReadAllText("/tmp/monster_game/wshot.txt").Trim().Split(' ');
string dir = "/tmp/monster_game/shots/" + a[0] + "/"; string n = a[1]; bool close = a.Length > 2;
System.IO.Directory.CreateDirectory(dir);
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game;
var w = System.IO.File.ReadAllText("/tmp/monster_game/wave_target.txt").Trim().Split(' ');
float F(int i) => float.Parse(w[i], System.Globalization.CultureInfo.InvariantCulture);
var head = new Vector3(F(0), F(1), F(2)); var m = new Vector3(F(3), F(4), F(5)); var t = new Vector3(F(6), F(7), F(8));
var look = Vector3.Lerp(m, t, 0.45f);
Monster.EditorTools.MonsterPreview.Shot(dir + "pov_" + n + ".png", head, Quaternion.LookRotation(look - head).eulerAngles, 78, 1280, 900);
var sp = m + new Vector3(0.75f, 0.25f, -0.15f); var sl = Vector3.Lerp(m, t, 0.5f);
Monster.EditorTools.MonsterPreview.Shot(dir + "side_" + n + ".png", sp, Quaternion.LookRotation(sl - sp).eulerAngles, 55, 1280, 800);
if (close)
{
    var o = g.beam.HasValue ? g.beam.Value.origin : g.waveMid + g.waveDir * 0.04f; var e = g.breathEnd.HasValue ? g.breathEnd.Value : t;
    var d = (e - o).normalized; var rt = Vector3.Cross(Vector3.up, d).normalized;
    var p1 = o + rt * 0.26f + Vector3.up * 0.1f - d * 0.04f; Monster.EditorTools.MonsterPreview.Shot(dir + "muzzle_" + n + ".png", p1, Quaternion.LookRotation(o + d * 0.08f - p1).eulerAngles, 50, 1280, 800);
    var mm = Vector3.Lerp(o, e, 0.55f); var p2 = mm + rt * 0.26f + Vector3.up * 0.12f; Monster.EditorTools.MonsterPreview.Shot(dir + "mid_" + n + ".png", p2, Quaternion.LookRotation(mm - p2).eulerAngles, 50, 1280, 800);
    var p3 = e + rt * 0.22f + Vector3.up * 0.2f - d * 0.22f; Monster.EditorTools.MonsterPreview.Shot(dir + "hit_" + n + ".png", p3, Quaternion.LookRotation(e - p3).eulerAngles, 50, 1280, 800);
}
result.Log("shot " + n + " charge=" + g.charge.ToString("F2") + " breathing=" + g.breathing.ToString("F3"));
