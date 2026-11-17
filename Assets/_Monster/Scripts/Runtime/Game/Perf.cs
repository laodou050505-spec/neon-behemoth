// 分段计时（11-16 用户：后期明显卡顿掉帧）。真机上没有性能分析器可看，头显日志里原来只有“帧率 / 最慢一帧”，看不出时间花在哪。
// 用法：Perf.Begin("名字") …… 下一个 Begin 或 Perf.End() 结束这一段；MonsterMR 每 5 秒把各段平均每帧多少毫秒、每帧产生多少垃圾内存（会触发垃圾回收 = 卡一下）、
// 以及场上各种东西的数量打进日志（[怪兽][性能]）。开销很小（每段两次取时间戳），正式包里一直开着。
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unity.Profiling;

namespace Monster.Game
{
    public static class Perf
    {
        static readonly Dictionary<string, long> acc = new(); static readonly Dictionary<string, int> counts = new();
        static readonly List<string> keys = new();
        static string cur; static long t0; static ProfilerRecorder gc; static long gcBytes; static int gc0 = -1;
        static bool open;
        /// <summary>编辑器里的性能分析器标记（嵌套用；正式包里不编进去）：Sample(名字) …… SampleEnd()</summary>
        [Conditional("UNITY_EDITOR")] public static void Sample(string name) => UnityEngine.Profiling.Profiler.BeginSample(name);
        [Conditional("UNITY_EDITOR")] public static void SampleEnd() => UnityEngine.Profiling.Profiler.EndSample();
        [Conditional("UNITY_EDITOR")] static void Mark(string name) { if (open) UnityEngine.Profiling.Profiler.EndSample(); open = name != null; if (open) UnityEngine.Profiling.Profiler.BeginSample("段:" + name); }
        public static void Begin(string name) { Mark(name); long now = Stopwatch.GetTimestamp(); if (cur != null) acc[cur] = acc.GetValueOrDefault(cur) + now - t0; else if (!acc.ContainsKey(name)) acc[name] = 0; cur = name; t0 = now; }
        public static void End() { Mark(null); if (cur == null) return; acc[cur] = acc.GetValueOrDefault(cur) + Stopwatch.GetTimestamp() - t0; cur = null; }
        /// <summary>记一个数量（取这段时间里的最大值）</summary>
        public static void Count(string name, int n) { if (!counts.TryGetValue(name, out var c) || n > c) counts[name] = n; }
        /// <summary>每帧调一次：累计这一帧分配的托管内存</summary>
        public static void Frame()
        {
            if (!gc.Valid) gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            if (gc.Valid) gcBytes += gc.LastValue;
        }
        /// <summary>过去 frames 帧的汇总，然后清零</summary>
        public static string Report(int frames)
        {
            End(); frames = System.Math.Max(1, frames);
            var sb = new StringBuilder();
            keys.Clear(); keys.AddRange(acc.Keys); keys.Sort((a, b) => acc[b].CompareTo(acc[a]));
            double total = 0; foreach (var k in keys) total += acc[k];
            sb.Append($"脚本 {total * 1000.0 / Stopwatch.Frequency / frames:0.00}ms（");
            for (int i = 0; i < keys.Count && i < 7; i++) { double ms = acc[keys[i]] * 1000.0 / Stopwatch.Frequency / frames; if (ms < 0.02 && i > 2) break; sb.Append($"{keys[i]} {ms:0.00} "); }
            int g = System.GC.CollectionCount(0);
            sb.Append($"）| 垃圾 {gcBytes / 1024f / frames:0.0}KB/帧 回收 {(gc0 < 0 ? 0 : g - gc0)} 次 |");
            gc0 = g;
            foreach (var kv in counts) sb.Append($" {kv.Key} {kv.Value}");
            foreach (var k in keys) acc[k] = 0;
            counts.Clear(); gcBytes = 0;
            return sb.ToString();
        }
    }
}
