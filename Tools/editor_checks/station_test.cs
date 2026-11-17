// 电站玩法验收：step 从 /tmp/monster_game/st.txt 读（hit = 拍一下第一座电站；info = 只看状态；fast = 把重启倒计时调到 3 秒；shot 名 = 拍照）
var a = System.IO.File.ReadAllText("/tmp/monster_game/st.txt").Trim().Split(' ');
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; var s = g.city.stations[0]; var tw = g.city.byId[s.towerId];
if (a[0] == "hit") { s.hitCd = 0; g.ToppleAt(s, 0, null, "smash"); }
if (a[0] == "throw") { s.hitCd = 0; g.ToppleAt(s, 0, null, "thrown"); }
if (a[0] == "fast") s.restart = 3f;
if (a[0] == "ts") Time.timeScale = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
if (a[0] == "shot")
{
    var p = s.Base + new Vector3(0.02f, 0.2f, -0.22f);
    Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/station/" + a[1] + ".png", p, Quaternion.LookRotation(s.Base + Vector3.up * 0.02f - p).eulerAngles, 40, 1280, 800);
}
result.Log(a[0] + ": station h=" + s.h + " armor=" + s.armor + " online=" + s.online + " restart=" + s.restart.ToString("F1") + " | tower h=" + tw.h + " shielded=" + g.IsShielded(tw) + " | hp=" + g.hp + " stars=" + g.stars + " time=" + g.time.ToString("F1"));
