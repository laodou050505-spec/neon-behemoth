// 参数：cars（车速统计）/ beam（大招横扫一排楼）/ near（血降到 18%）/ shot 名字 俯角 [x y z]（灰背景截图）
var a = System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim().Split(' ');
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; g.sealMax = 99999;
foreach (var c in Camera.allCameras) { c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.62f, 0.58f, 0.52f, 1); }
if (a[0] == "cars")
{
    int n = 0, moving = 0; float vmax = 0;
    foreach (var tr in Object.FindObjectsByType<Monster.Traffic>()) foreach (var L in tr.lanes) foreach (var c in L.cars) { if (c.hidden) continue; n++; if (c.v > 0.0005f) moving++; vmax = Mathf.Max(vmax, c.v); }
    result.Log("车 " + n + " 辆在路上，其中还在动的 " + moving + "，最快 " + vmax.ToString("F4") + " m/s；Halt=" + Monster.Traffic.Halt + " time=" + g.time.ToString("F1"));
}
if (a[0] == "beam")
{
    // 光束贴着桌面上方横扫主城区中间一排：从左到右
    var z = g.city.zones[0]; float t0 = Time.time; var gi = new Monster.Game.GameInput { hasHead = true, headPos = new Vector3(0, 1.6f, 0), headFwd = Vector3.forward, hasGaze = true };
    v.simInput = () => { float k = Mathf.Clamp01((Time.time - t0) / 3.5f); var from = new Vector3(0, 1.25f, 0.1f); var to = z.Point(Mathf.Lerp(0.1f, z.w - 0.1f, k), z.h * 0.5f) + Vector3.up * 0.03f; gi.gaze = new Ray(from, (to - from).normalized); return gi; };
    g.rage = 0; g.breathing = 3.6f; g.beam = null; v.paused = false;
    int standing = 0; foreach (var b in g.city.buildings) if (b.h > 0 && b.kind == Monster.BuildingKind.House) standing++;
    result.Log("beam sweep start, standing houses=" + standing);
}
if (a[0] == "near") { v.simInput = null; g.hp = 18; v.paused = false; result.Log("hp=18"); }
if (a[0] == "state") { var ov = Object.FindAnyObjectByType<Monster.Game.HurtOverlay>(); int standing = 0; foreach (var b in g.city.buildings) if (b.h > 0 && b.kind == Monster.BuildingKind.House) standing++; result.Log("hp=" + g.hp.ToString("F0") + " nearDeath=" + ov.NearDeath + " beat=" + ov.Beat.ToString("F2") + " houses=" + standing + " rage=" + g.rage.ToString("F0") + " result=" + g.result); }
if (a[0] == "shot")
{
    var pos = a.Length > 5 ? new Vector3(float.Parse(a[3]), float.Parse(a[4]), float.Parse(a[5])) : new Vector3(0, 1.6f, 0);
    Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/" + a[1] + ".png", pos, new Vector3(float.Parse(a[2]), a.Length > 6 ? float.Parse(a[6]) : 0, 0), 80, 1280, 960);
    result.Log("shot " + a[1]);
}
