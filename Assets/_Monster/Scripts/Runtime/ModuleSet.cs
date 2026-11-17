// 正式楼模块（Tools/blender_buildings.py 程序化建模，真实尺寸：米）。
// 每种楼一套：base（底座）、storey_a / storey_b（标准层两种变体，交替堆叠）、crown（楼顶），共用一张 2K 图集。
// 放在 Resources/Modules/<楼型>/ 下：4 个 FBX + albedo / mask / emission 三张贴图 + meta.json，运行时也能加载（头显里扫描后现场生成）。
// 没有正式模块的楼型继续用程序化方块。
using System.Collections.Generic;
using UnityEngine;

namespace Monster
{
    public class ModuleSet
    {
        public Mesh baseMesh, storeyA, storeyB, crownMesh;
        public Texture2D albedo, mask, emission, normal;
        // 配色变体：albedo（变体 0）+ albedo_v1、albedo_v2……（Tools/compose_atlas.py 第三个参数），按楼的种子挑
        public List<Texture2D> albedos = new();
        // 每块的高度（米，模型原始尺寸）
        public float baseH, storeyH, crownH;
        // 层数 → 标准层数：mids = max(minMids, 层数 - midOffset)。矮楼（商铺 / 别墅 / 仓库 / 加油站）1 层 = 底座 + 楼顶
        public int midOffset = 2, minMids = 1;
        // 墙体占地（米）：标准层包围盒去掉楼板外沿
        public Vector2 size;

        static readonly Dictionary<Style, ModuleSet> cache = new();
        public static void ClearCache() { cache.Clear(); Lod1.Clear(); }
        // 远处用的简化版（Tools/blender_buildings.py 导出的 <模块>_lod1：去掉竖梃、窗台、栏杆等细小构件）
        public static readonly Dictionary<Mesh, Mesh> Lod1 = new();

        static string Folder(Style s) => s switch
        {
            Style.Glass => "glass_tower",
            Style.Deco => "deco_hotel",
            Style.Office => "office",
            Style.Apt => "apartment",
            Style.Garage => "garage",
            Style.Shop => "shop",
            Style.Villa => "villa",
            Style.Warehouse => "warehouse",
            Style.Gas => "gas_station",
            Style.Condo => "condo",
            Style.Brick => "brick",
            Style.Modern => "modern",
            Style.Spire => "spire",
            Style.Mixed => "mixed",
            _ => null,
        };

        public static ModuleSet Get(Style s)
        {
            if (cache.TryGetValue(s, out var m) && (m == null || m.storeyA)) return m;
            m = Load(s);
            cache[s] = m;
            return m;
        }

        static ModuleSet Load(Style s)
        {
            var f = Folder(s);
            if (f == null || !Resources.Load<TextAsset>($"Modules/{f}/meta")) return null;
            var set = new ModuleSet
            {
                baseMesh = FirstMesh($"Modules/{f}/base"),
                storeyA = FirstMesh($"Modules/{f}/storey_a"),
                storeyB = FirstMesh($"Modules/{f}/storey_b"),
                crownMesh = FirstMesh($"Modules/{f}/crown"),
                albedo = Resources.Load<Texture2D>($"Modules/{f}/albedo"),
                mask = Resources.Load<Texture2D>($"Modules/{f}/mask"),
                emission = Resources.Load<Texture2D>($"Modules/{f}/emission"),
                normal = Resources.Load<Texture2D>($"Modules/{f}/normal"),
            };
            if (!set.baseMesh || !set.storeyA || !set.crownMesh) return null;
            if (!set.storeyB) set.storeyB = set.storeyA;
            foreach (var (m, n) in new[] { (set.baseMesh, "base"), (set.storeyA, "storey_a"), (set.storeyB, "storey_b"), (set.crownMesh, "crown") })
            { var l = FirstMesh($"Modules/{f}/{n}_lod1"); if (m && l) Lod1[m] = l; }
            set.albedos.Add(set.albedo);
            for (int v = 1; v < 8; v++) { var t = Resources.Load<Texture2D>($"Modules/{f}/albedo_v{v}"); if (!t) break; set.albedos.Add(t); }
            var meta = JsonUtility.FromJson<MetaJson>(Resources.Load<TextAsset>($"Modules/{f}/meta").text);
            // 模块底面在 y=0，高度 = 包围盒顶
            if (meta != null && meta.mids != null && meta.mids.offset > 0) { set.midOffset = meta.mids.offset; set.minMids = meta.mids.min; }
            set.baseH = set.baseMesh.bounds.max.y;
            set.storeyH = set.storeyA.bounds.max.y;
            set.crownH = set.crownMesh.bounds.max.y;
            // 占地 = 墙体尺寸（meta.json 的 wall，不含楼板外沿 / 雨棚 / 台阶；没有就退回底座包围盒）
            if (meta != null && meta.wall != null && meta.wall.Length == 2) set.size = new Vector2(meta.wall[0], meta.wall[1]);
            else { var b = set.baseMesh.bounds; set.size = new Vector2(b.size.x, b.size.z); }
            return set;
        }

        [System.Serializable] class MidsJson { public int offset, min; }
        [System.Serializable] class MetaJson { public float[] wall; public MidsJson mids; }

        // FBX 导入后是一个预制体，取里面第一个网格（导入时已烘好平滑法线到 UV3）
        static Mesh FirstMesh(string path)
        {
            var go = Resources.Load<GameObject>(path);
            if (!go) return null;
            var mf = go.GetComponentInChildren<MeshFilter>();
            return mf ? mf.sharedMesh : null;
        }
    }
}
