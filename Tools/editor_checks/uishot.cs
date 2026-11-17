// 固定文本：/tmp/monster_game/ui.txt = "名字 [方法名]"。带方法名 = 对当前纸面界面反射调用该方法（切页）；不带 = 从头的位置正对界面截图
var a = System.IO.File.ReadAllText("/tmp/monster_game/ui.txt").Trim().Split(' ');
var cam = Camera.main.transform; cam.SetPositionAndRotation(new Vector3(0, 1.6f, 0), cam.rotation);
Transform root = null; object scr = null;
foreach (var ns in Object.FindObjectsByType<Monster.Game.NoticeScreen>()) if (ns.Root && ns.enabled) { root = ns.Root; scr = ns; }
if (a[0] == "sign") { var go = GameObject.Find(a.Length > 1 ? a[1] : "TutorialSign"); root = go ? go.transform : null; scr = null; a = new[] { a.Length > 1 ? a[1] : "TutorialSign" }; }
if (root == null) { result.Log("no screen for " + a[0]); return; }
if (a.Length > 1 && scr != null)
{
    var ts = scr as Monster.Game.TitleScreen;
    if (ts == null) { result.Log("not title"); return; }
    if (a[1] == "ShowLevels") ts.ShowLevels(); else if (a[1] == "ShowSettings") ts.ShowSettings(); else ts.ShowMain();
    result.Log("invoked " + a[1]); return;
}
Bounds b = new Bounds(root.position, Vector3.zero); bool f = true;
foreach (var r in root.GetComponentsInChildren<Renderer>()) { if (!r.enabled || !r.gameObject.activeInHierarchy || r.name == "Pole" || r.name == "Post") continue; if (f) { b = r.bounds; f = false; } else b.Encapsulate(r.bounds); }
// 正对界面（沿界面法线退后），刚好把整块装进画面
var n = -root.forward; if (Vector3.Dot(n, cam.position - b.center) < 0) n = -n;
float ext = Mathf.Max(b.size.x, b.size.y * 1.6f, b.size.z) * 0.5f; float dist = ext / Mathf.Tan(20 * Mathf.Deg2Rad) * 1.12f;
var p = b.center + n * dist;
Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/ui/" + a[0] + ".png", p, Quaternion.LookRotation(b.center - p, Vector3.up).eulerAngles, 40, 1600, 1000);
Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/ui/" + a[0] + "_pov.png", cam.position, Quaternion.LookRotation(b.center - cam.position).eulerAngles, 70, 1280, 900);
result.Log("ui shot " + a[0] + " size " + b.size.ToString("F2") + " dist " + Vector3.Distance(cam.position, b.center).ToString("F2"));
