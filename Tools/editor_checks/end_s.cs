// 参数：截图名 [把结局时间拨到几秒]
var a = System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim().Split(' ');
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; var en = Object.FindAnyObjectByType<Monster.Game.Ending>();
if (en && a.Length > 1) en.t = float.Parse(a[1]);
var es = Object.FindAnyObjectByType<Monster.Game.EndingScreen>();
if (a[0] != "-") Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/" + a[0] + ".png", new Vector3(0, 1.6f, 0), new Vector3(a.Length > 2 ? float.Parse(a[2]) : 30, 0, 0), 80, 1280, 960);
result.Log(a[0] + " winT=" + g.winT.ToString("F1") + " result=" + g.result + " ending=" + (en ? en.t.ToString("F1") : "-") + " paper=" + (es != null) + " hp=" + g.hp.ToString("F0") + " rage=" + g.rage.ToString("F0") + " blackout=" + Shader.GetGlobalVector("_Blackout").w.ToString("F2"));
