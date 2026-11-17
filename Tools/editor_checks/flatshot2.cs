var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); v.game.hp = 100; v.paused = true;
var o = Object.FindAnyObjectByType<Monster.Game.HurtOverlay>(); if (o) o.enabled = false; var ov = GameObject.Find("HurtOverlay"); if (ov) ov.SetActive(false);
var q = System.IO.File.ReadAllText("/tmp/monster_game/flatpos.txt").Trim().Split(' '); var mid = new Vector3(float.Parse(q[0]), float.Parse(q[1]), float.Parse(q[2]));
var pos = mid + new Vector3(0.012f, 0.13f, -0.035f);
Monster.EditorTools.MonsterPreview.Shot("/tmp/monster_game/shots/flat_car.png", pos, Quaternion.LookRotation(mid - pos).eulerAngles, 38, 1100, 800);
// 压扁的那辆：量一下实际高度
float h = 0, h0 = 0; foreach (Transform t in v.builder.root.Find("GameFx")) if (t.name == "FlatCar") { var r = t.GetComponent<MeshRenderer>(); h = r.bounds.size.y; h0 = t.GetComponent<MeshFilter>().sharedMesh.bounds.size.y * 0.7f; }
result.Log("shot；压扁后车高 " + (h * 1000).ToString("F1") + "mm（原来约 " + (h0 * 1000).ToString("F1") + "mm）");
