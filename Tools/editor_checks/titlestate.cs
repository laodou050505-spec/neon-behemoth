// 标题页的几何关系：眼高、纸中心 / 上沿、铭牌下沿、招牌抬高量
var ts = Object.FindAnyObjectByType<Monster.Game.TitleScreen>(); var sg = Object.FindAnyObjectByType<Monster.Game.TitleSign>();
var page = GameObject.Find("TitleScreen"); var pl = GameObject.Find("MakerPlate"); var cam = Camera.main.transform;
float top = -99; foreach (var r in page.GetComponentsInChildren<Renderer>()) if (r.name == "paper_notice") top = r.bounds.max.y;
result.Log("眼高 " + cam.position.y.ToString("F2") + " 桌面 " + Monster.MonsterMR.TableY.ToString("F2") + " | 纸中心 " + page.transform.position.y.ToString("F3") + " 纸上沿 " + top.ToString("F3") + " | 铭牌中心 " + (pl ? pl.transform.position.y.ToString("F3") : "无") + " 铭牌下沿 " + (pl ? (pl.transform.position.y - 0.032f).ToString("F3") : "无") + " | 招牌抬高 " + sg.lift.ToString("F3"));
