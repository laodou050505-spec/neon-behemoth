// 环握拿楼验收：装一个模拟右手，每帧从 /tmp/monster_game/wrap.txt 读“楼id 张开余量(米) 抬高(米) 往玩家挪(米)”：
// 拇指在楼的一侧、四指在另一侧（相距 = 楼宽 + 余量），手在楼身三分之一高处
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game;
Monster.Building pick = null;
foreach (var x in g.city.buildings) if (x.kind == Monster.BuildingKind.House && x.h == x.maxH && x.h >= 6 && x.zone.kind == Monster.ZoneKind.Table && x.fire <= 0 && x.Foot < 0.075f && (pick == null || Vector3.Distance(x.Base, new Vector3(0, x.Base.y, 0.55f)) < Vector3.Distance(pick.Base, new Vector3(0, pick.Base.y, 0.55f)))) pick = x;
System.IO.File.WriteAllText("/tmp/monster_game/wrap.txt", pick.id + " 0.06 0 0");
var head = new Vector3(0, 1.6f, 0); Vector3 last = Vector3.zero; bool has = false;
var hv = Object.FindAnyObjectByType<Monster.Game.HandView>();
v.simInput = () =>
{
    var a = System.IO.File.ReadAllText("/tmp/monster_game/wrap.txt").Trim().Split(' ');
    var b = g.city.byId[int.Parse(a[0])]; float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
    float span = Mathf.Min(b.fw, b.fd) + F(1); var u = b.zone.u; var vv = b.zone.v;
    var c = b.Base + Vector3.up * (b.TopH(b.maxH) * 0.35f + F(2)) + vv * F(3);
    var thumb = c - u * span * 0.5f; var idx = c + u * span * 0.5f + Vector3.up * 0.008f; var mid = c + u * span * 0.5f - Vector3.up * 0.008f;
    var palm = c + vv * (b.fd * 0.5f + 0.04f);
    var vel = has ? (palm - last) / Mathf.Max(1e-3f, Time.deltaTime) : Vector3.zero; last = palm; has = true;
    var h = new Monster.Game.HandIn { side = "right", present = true, open = true, pinch = false, grip = false, pinchD = Vector3.Distance(thumb, idx), pos = palm, palm = palm, palmN = -vv, wrist = palm + vv * 0.08f, fwd = -vv, vel = Vector3.ClampMagnitude(vel, 2f), pts = new[] { palm, thumb, idx, mid, mid - Vector3.up * 0.016f, mid - Vector3.up * 0.03f, idx + vv * 0.03f }, nPts = 7 };
    return new Monster.Game.GameInput { hasHead = true, headPos = head, headFwd = Vector3.forward, hasGaze = true, gaze = new Ray(head, (b.Base - head).normalized), hands = new System.Collections.Generic.List<Monster.Game.HandIn> { h } };
};
result.Log("wrap sim on building " + pick.id + " " + pick.style + " h=" + pick.h + " foot=" + pick.fw.ToString("F3") + "x" + pick.fd.ToString("F3"));
