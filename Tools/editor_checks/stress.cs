int lvi = int.Parse(System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim());
var lv = Monster.Game.Level.All[lvi];
var sb = new System.Text.StringBuilder();
var sizes = new[] { new Vector2(1.60f, 0.60f), new Vector2(1.2f, 0.6f), new Vector2(1.0f, 0.5f), new Vector2(1.4f, 0.7f), new Vector2(0.8f, 0.6f) };
System.Func<Vector2, bool> inside = p => Mathf.Abs(p.x) < 1.46f && Mathf.Abs(p.y) < 1.46f;
int bad = 0;
for (int si = 0; si <= sizes.Length + 1; si++)
{
    var zones = new System.Collections.Generic.List<Monster.Zone>();
    var head = new Vector3(0, si == sizes.Length + 1 ? 1.2f : 1.6f, 0);
    if (si < sizes.Length) { var t = new Monster.RoomTable.Table { center = new Vector3(0, 0.86f, 0.25f + sizes[si].y / 2 + 0.04f), axisA = Vector3.right, axisB = Vector3.forward, halfA = sizes[si].x / 2 + 0.04f, halfB = sizes[si].y / 2 + 0.04f }; zones.Add(Monster.RoomTable.ToZone(t, head)); }
    else zones = Monster.CityLayout.FitRoom(inside, Vector2.zero, head.y, false, 0);
    if (lv.sides <= 0) zones.RemoveAll(z => z.kind == Monster.ZoneKind.Cabinet && z.furniture); else Monster.CityLayout.AddSides(zones, inside, Vector3.zero, lv.sides);
    float area = 0; foreach (var z in zones) area += z.w * z.h; float k = area < 0.6f ? 0.5f : 0.7f;
    var design = new System.Collections.Generic.List<Monster.Zone>();
    foreach (var z in zones) design.Add(new Monster.Zone(z.id, z.kind, z.origin, z.u, z.v, z.w / k, z.h / k, z.harbor) { furniture = z.furniture });
    try
    {
        var city = Monster.CityLayout.Build(design, lv.Spec, 7 + lvi * 17);
        var b = Monster.CityBuilder.Build(city, true, null, k);
        sb.Append(" [" + si + "] 楼" + city.buildings.Count + " 区" + zones.Count + " 塔" + city.towers.Count);
        Object.DestroyImmediate(b.root.gameObject);
    }
    catch (System.Exception e) { bad++; sb.Append(" [" + si + "] 出错 " + e.GetType().Name + " " + e.Message + " @ " + e.StackTrace.Split('\n')[0]); }
}
result.Log("level " + lvi + " bad=" + bad + sb);
