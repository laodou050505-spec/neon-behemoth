// 参数：动作 [时间倍率]。动作：land 马上落地 / charge 马上蓄力 / block 手拍在盾上 / thrown 被扔来的东西砸中（出破绽）/ breath 被大招打 / kill 打到只剩 1 格再砸一下 / respawn 重新空降
var a = System.IO.File.ReadAllText("/tmp/monster_game/arg.txt").Trim().Split(' ');
var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); var g = v.game; g.level.dmg = 0; v.paused = false;
var ov = Object.FindAnyObjectByType<Monster.Game.HurtOverlay>(); if (ov) { ov.enabled = false; var q = GameObject.Find("HurtOverlay"); if (q) q.SetActive(false); }
Monster.Game.Enemy m = null; foreach (var e in g.enemies) if (e.type == "mech" && !e.dead) m = e;
if (a[0] == "respawn" || m == null) { foreach (var e in g.enemies) if (e.type == "mech") { e.dead = true; e.state = Monster.Game.EState.Dead; } m = Monster.Game.Enemies.Spawn(g, "mech"); }
if (a[0] == "land") m.dropT = Mathf.Min(m.dropT, 0.06f);
if (a[0] == "charge") { m.stagger = 0; m.cd = Monster.Game.Enemies.MechCharge + 0.02f; }
if (a[0] == "block") g.HitEnemy(m, new Vector3(0.3f, 0, 0.2f), "swat");
if (a[0] == "thrown") g.HitEnemy(m, new Vector3(0.5f, 0, 0), "thrown");
if (a[0] == "breath") { m.guard = 0; m.stagger = 1; g.HitEnemy(m, Vector3.zero, "breath"); }
if (a[0] == "kill") { m.hp = 1; g.HitEnemy(m, new Vector3(0.5f, 0, 0), "thrown"); }
Time.timeScale = a.Length > 1 ? float.Parse(a[1]) : 0.05f;
result.Log(a[0] + " hp=" + m.hp + " dropT=" + m.dropT.ToString("F2") + " cd=" + m.cd.ToString("F2") + " stagger=" + m.stagger.ToString("F1") + " dead=" + m.dead);
