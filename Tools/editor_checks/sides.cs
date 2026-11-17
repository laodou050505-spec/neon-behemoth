// 两侧城区放不放得下：真桌子 1.6×0.6（离人 25cm）和虚拟桌两种，安全区 = 半径 1.46m 的圆，圆心相对玩家偏移 −0.7～0.7m（49 种），统计左 / 右各放下了多少次
var sb = new System.Text.StringBuilder();
foreach (bool real in new[] { true, false })
{
    int n = 0, left = 0, right = 0, both = 0, late = 0;
    for (int ix = -3; ix <= 3; ix++) for (int iz = -3; iz <= 3; iz++)
    {
        var c = new Vector2(ix * 0.233f, iz * 0.233f); if (c.magnitude > 1.0f) continue;
        var inside = Monster.CityLayout.Circle(c, 1.46f); var head = new Vector3(0, 1.25f, 0);
        System.Collections.Generic.List<Monster.Zone> zones;
        if (real) { var t = new Monster.RoomTable.Table { center = new Vector3(0, 0.85f, 0.25f + 0.34f), axisA = Vector3.right, axisB = Vector3.forward, halfA = 0.84f, halfB = 0.34f }; zones = new System.Collections.Generic.List<Monster.Zone> { Monster.RoomTable.ToZone(t, head) }; }
        else zones = Monster.CityLayout.FitRoom(inside, Vector2.zero, 1.6f, true, 0);
        string log = Monster.CityLayout.AddSides(zones, inside, Vector3.zero, 2);
        bool L = zones.Exists(z => z.id == "cabinet" || (z.kind == Monster.ZoneKind.Cabinet)), R = zones.Exists(z => z.id == "side");
        n++; if (L) left++; if (R) right++; if (L && R) both++; if (log.Contains("轮")) late++;
    }
    sb.Append("\n" + (real ? "真桌子" : "虚拟桌") + "：" + n + " 种站位，左边有 " + left + "，右边有 " + right + "，两边都有 " + both + "（其中靠放宽 / 不看安全区才放下的 " + late + "）");
}
result.Log(sb.ToString());
