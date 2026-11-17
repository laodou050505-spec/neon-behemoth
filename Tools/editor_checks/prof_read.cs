// 汇总最近 N 帧里各个采样的平均每帧 GC 分配（字节）和自身耗时
int last = UnityEditorInternal.ProfilerDriver.lastFrameIndex, first = Mathf.Max(UnityEditorInternal.ProfilerDriver.firstFrameIndex, last - 60);
var gc = new System.Collections.Generic.Dictionary<string, float>(); var ms = new System.Collections.Generic.Dictionary<string, float>(); int n = 0;
var kids = new System.Collections.Generic.List<int>();
for (int f = first; f <= last; f++)
{
    using (var view = UnityEditorInternal.ProfilerDriver.GetHierarchyFrameDataView(f, 0, UnityEditor.Profiling.HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory, false))
    {
        if (view == null || !view.valid) continue; n++;
        var stack = new System.Collections.Generic.Stack<(int id, int depth)>(); stack.Push((view.GetRootItemID(), 0));
        while (stack.Count > 0)
        {
            var (id, depth) = stack.Pop();
            string name = view.GetItemName(id);
            float g = view.GetItemColumnDataAsFloat(id, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory);
            float t = view.GetItemColumnDataAsFloat(id, UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime);
            bool mine = name.Contains("Monster") || name.Contains("GameView") || name.Contains("EnemyView") || name.Contains("Traffic") || name.Contains("HandView") || name.Contains("CityHud") || name.Contains("HurtOverlay") || name.Contains("PowFx") || name.Contains("CityLod") || name.Contains("DuskSky") || name.Contains("PauseButton") || name.Contains("PalmMenu") || name.Contains("DeskSim") || name.Contains("TextMeshPro") || name.Contains("Audio");
            mine = mine || depth >= 5;
            if (mine || depth <= 2) { gc.TryGetValue(name, out var g0); gc[name] = g0 + g; ms.TryGetValue(name, out var t0); ms[name] = t0 + t; }
            if (g < 64 && depth > 2) continue;
            if (depth >= 11) continue;
            kids.Clear(); view.GetItemChildren(id, kids); foreach (var k in kids) stack.Push((k, depth + 1));
        }
    }
}
var names = new System.Collections.Generic.List<string>(gc.Keys); names.Sort((a, b) => gc[b].CompareTo(gc[a]));
var sb = new System.Text.StringBuilder();
foreach (var k in names.GetRange(0, Mathf.Min(34, names.Count))) sb.Append("\n" + (gc[k] / n / 1024f).ToString("F2") + " KB/帧  " + (ms[k] / n).ToString("F2") + " ms  " + k);
result.Log("帧数 " + n + sb);
