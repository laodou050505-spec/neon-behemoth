var mr = Object.FindAnyObjectByType<Monster.MonsterMR>(); var cam = Camera.main;
var o = Object.FindAnyObjectByType<Monster.Game.HurtOverlay>(); if (o) o.SetThreats(new System.Collections.Generic.List<(Vector3 pos, float s, int kind)>());
cam.transform.SetPositionAndRotation(new Vector3(0, 1.25f, -0.35f), Quaternion.Euler(10, 0, 0));
Monster.MonsterMR.CityShown = false;
foreach (var old in Object.FindObjectsByType<Monster.Game.BriefCard>()) Object.Destroy(old);
var bc = mr.gameObject.AddComponent<Monster.Game.BriefCard>(); bc.cam = cam; bc.input = Object.FindAnyObjectByType<Monster.Game.MonsterInput>(); bc.level = 5; bc.ready = false;
result.Log("made TableY=" + Monster.MonsterMR.TableY);
