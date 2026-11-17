var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); v.paused = true; Time.timeScale = 1f;
var cam = Camera.main.transform; cam.SetPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.Euler(35, 0, 0));
var o = Object.FindAnyObjectByType<Monster.Game.HurtOverlay>(); o.enabled = true;
var l = new System.Collections.Generic.List<(Vector3 pos, float s, int kind)>();
l.Add((cam.TransformPoint(new Vector3(-1.2f, 0.1f, 0.8f)), 1f, 0));
l.Add((cam.TransformPoint(new Vector3(0.9f, 0.9f, 1.0f)), 1f, 1));
l.Add((cam.TransformPoint(new Vector3(0.3f, -0.2f, -1.0f)), 0.6f, 0));
o.SetThreats(l);
result.Log("threats set hp=" + v.game.hp);
