var bc = Object.FindAnyObjectByType<Monster.Game.BriefCard>(); var cam = Camera.main.transform;
float y = bc.Root.position.y; float half = 0.5f * 0.66f * 0.5f;
result.Log("头高 " + cam.position.y + " 桌面 " + Monster.MonsterMR.TableY.ToString("F3") + " 纸中心 y=" + y.ToString("F3") + " 纸下沿≈" + (y - 0.03f * 0.66f - half).ToString("F3") + "（旧算法中心=" + (cam.position.y - 0.25f).ToString("F3") + "）");
