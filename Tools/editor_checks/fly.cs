// 飞行单位避让体检：在当前关卡里放出直升机 / 战斗机 / 轰炸机 / 无人机各若干，只跑逻辑（不等真实时间），
// 每一步检查它们有没有进到任何一片城区（连同下面的桌子 / 柜子）的方块里：水平在城区外扩范围内、高度低于最高楼顶 + 机身半径
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; v.paused = true;
string mode = System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim();
Monster.Game.Enemies.NoClear = mode == "off";
var tops = new System.Collections.Generic.Dictionary<Monster.Zone, float>();
foreach (var z in g.city.zones) { float h = 0; foreach (var b in g.city.buildings) if (b.zone == z) h = Mathf.Max(h, b.TopH(b.maxH)); tops[z] = z.origin.y + h; }
var sb = new System.Text.StringBuilder();
foreach (var headY in new[] { 1.6f, 1.2f })
{
    g.headPos = new Vector3(0, headY, 0); g.headFwd = Vector3.forward;
    var list = new System.Collections.Generic.List<Monster.Game.Enemy>();
    foreach (var ty in new[] { "heli", "heli", "heli", "jet", "jet", "bomber", "bomber", "drone", "drone", "heli", "jet", "bomber" }) { var e = Monster.Game.Enemies.Spawn(g, ty); if (e != null) list.Add(e); }
    int steps = 0, inside = 0, over = 0; float worst = 9; string worstWho = "";
    for (int s = 0; s < 2400; s++)
    {
        foreach (var e in list)
        {
            if (e.dead) continue;
            var p0 = e.pos;
            Monster.Game.Enemies.Tick(g, e, 0.0111f);
            steps++;
            float r = Monster.Game.Enemies.Types[e.type].r;
            foreach (var z in g.city.zones)
            {
                var d = e.pos - z.origin; float a = Vector3.Dot(d, z.u), b = Vector3.Dot(d, z.v); float m = z.furniture ? 0.08f : 0.03f;
                if (a < -m || a > z.w + m || b < -m || b > z.h + m) continue;
                if (new Vector2(e.pos.x - g.headPos.x, e.pos.z - g.headPos.z).magnitude < 0.3f) continue;
                over++;
                float clear = e.pos.y - (tops[z] + r);
                if (clear < worst) { worst = clear; worstWho = e.type + "@" + z.id; }
                if (clear < 0) inside++;
            }
        }
    }
    foreach (var e in list) { e.dead = true; e.state = Monster.Game.EState.Dead; }
    g.enemies.RemoveAll(e => list.Contains(e)); g.projectiles.Clear();
    sb.Append("\n头高 " + headY + "m：" + steps + " 步，飞到城区 / 家具上方 " + over + " 步，其中钻进方块里 " + inside + " 步；最小余量 " + (worst * 100).ToString("F1") + "cm（" + worstWho + "）");
}
g.DrainEvents(); Monster.Game.Enemies.NoClear = false;
result.Log("飞行避让（避让" + (mode == "off" ? "关" : "开") + "）" + sb);
