var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); if (v == null || v.game == null) { result.Log("no game; playing=" + EditorApplication.isPlaying); return; }
var g = v.game; var sb = new System.Text.StringBuilder(); var seen = new System.Collections.Generic.HashSet<Monster.Style>();
foreach (var b in g.city.buildings)
{
    if (b.kind != Monster.BuildingKind.House || b.h <= 0 || !seen.Add(b.style)) continue;
    var bv = v.builder.View(b.id); if (!bv || bv.storeys == null) continue;
    sb.Append("\n" + b.style + " id=" + b.id + " h=" + b.h + " modH=" + (b.modH * 1000).ToString("F1") + " logicTop=" + ((Monster.Game.MonsterGame.TopOf(b).y - b.Base.y) * 1000).ToString("F0") + " | ");
    for (int i = 0; i < bv.storeys.Length; i++)
    {
        var s = bv.storeys[i]; if (!s) continue; float top = float.MinValue, bot = float.MaxValue;
        foreach (var r in s.GetComponentsInChildren<MeshFilter>(true)) { var mr = r.GetComponent<MeshRenderer>(); if (!mr) continue; var bb = mr.bounds; if (bb.size.sqrMagnitude < 1e-12f) { var lb = r.sharedMesh.bounds; var c = r.transform.TransformPoint(lb.center); bb = new Bounds(c, Vector3.Scale(lb.size, r.transform.lossyScale)); } top = Mathf.Max(top, bb.max.y); bot = Mathf.Min(bot, bb.min.y); }
        if (i < 2 || i >= bv.storeys.Length - 2) sb.Append("[" + i + "] " + ((bot - b.Base.y) * 1000).ToString("F0") + "~" + ((top - b.Base.y) * 1000).ToString("F0") + "  "); else if (i == 2) sb.Append("... ");
    }
    sb.Append(" crown=" + (bv.crown ? ((bv.crown.position.y - b.Base.y) * 1000).ToString("F0") : "-"));
}
result.Log(sb.ToString());
