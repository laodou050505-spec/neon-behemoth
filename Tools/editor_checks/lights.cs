// 现场调“万家灯火”：参数 = 太阳倍率 环境倍率 窗灯倍率（相对当前代码里的值） 截图名
var a = System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim().Split(' ');
float fs = float.Parse(a[0]), fa = float.Parse(a[1]), fw = float.Parse(a[2]);
var v = Object.FindAnyObjectByType<Monster.Game.GameView>();
Light sun = RenderSettings.sun; if (!sun) foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) sun = l;
string f = "/tmp/monster_game/lights0.txt";
if (!System.IO.File.Exists(f) || a.Length > 4) System.IO.File.WriteAllText(f, sun.intensity + " " + RenderSettings.ambientSkyColor.r + " " + RenderSettings.ambientSkyColor.g + " " + RenderSettings.ambientSkyColor.b + " " + RenderSettings.ambientEquatorColor.r + " " + RenderSettings.ambientEquatorColor.g + " " + RenderSettings.ambientEquatorColor.b + " " + RenderSettings.ambientGroundColor.r + " " + RenderSettings.ambientGroundColor.g + " " + RenderSettings.ambientGroundColor.b);
var b = System.IO.File.ReadAllText(f).Split(' '); float F(int i) => float.Parse(b[i]);
sun.intensity = F(0) * fs;
var cool = new Color(0.8f, 0.9f, 1.15f);
RenderSettings.ambientSkyColor = new Color(F(1), F(2), F(3)) * fa * (fa < 0.99f ? cool : Color.white); RenderSettings.ambientEquatorColor = new Color(F(4), F(5), F(6)) * fa * (fa < 0.99f ? cool : Color.white); RenderSettings.ambientGroundColor = new Color(F(7), F(8), F(9)) * fa;
int n = 0; var seen = new System.Collections.Generic.HashSet<Material>();
foreach (var r in v.builder.root.GetComponentsInChildren<MeshRenderer>(true)) foreach (var m in r.sharedMaterials) if (m && m.name.StartsWith("module_") && seen.Add(m)) { m.SetFloat("_NightGlow", 1.25f * fw); n++; }
Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/" + a[3] + ".png", new Vector3(0, 1.6f, 0), new Vector3(30, 0, 0), 80, 1280, 960);
Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/" + a[3] + "_near.png", new Vector3(-0.25f, 1.22f, 0.25f), new Vector3(22, 25, 0), 60, 1280, 960);
result.Log(a[3] + " sun=" + sun.intensity.ToString("F2") + " amb=" + RenderSettings.ambientSkyColor + " mats=" + n + " sunColor=" + sun.color);
