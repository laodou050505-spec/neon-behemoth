"""《我是怪兽》 · 把一栋 P2 楼按楼层切成模块（在 Blender 里运行）：
    Blender -b -P Tools/split_storeys.py -- <输入.glb> <输出目录> <底座顶> <标准层底> <标准层顶> <楼顶底> [面数上限]
    四个高度都是相对总高的比例（0~1）：先用 Tools/floor_profile.py 量出楼板位置，切在楼板厚度的中线上。
做法：
  1. 读入贴好图的整栋楼，摆正（底面 z=0，占地中心在原点）。
  2. 底座 = 0~底座顶，标准层 = 标准层底~标准层顶（只取一层，游戏里复制堆叠），楼顶 = 楼顶底~1。
  3. 每块用平面二分切开、把切口补上（封闭，碰撞和描边都正常），保留原贴图 UV。
  4. 每块再精简到面数上限以内，导出 base.glb / storey.glb / crown.glb + meta.json（每块高度，单位：原模型）。
"""
import bpy, bmesh, sys, json, pathlib
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
src, out = pathlib.Path(argv[0]), pathlib.Path(argv[1])
f_base, f_s0, f_s1, f_crown = (float(x) for x in argv[2:6])
face_cap = int(argv[6]) if len(argv) > 6 else 2500
out.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(src))
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
ob = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# 摆正：底面 z=0，占地中心在原点
co = [ob.matrix_world @ v.co for v in ob.data.vertices]
mn = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
mx = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
off = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
for v in ob.data.vertices: v.co -= off
H = mx.z - mn.z
step = (f_s1 - f_s0) * H
print(f'整栋高 {H:.4f}，标准层高 {step:.4f}', flush=True)


def piece(name, z0, z1):
    """复制原模型，切掉 z0 以下和 z1 以上，补切口，下移到 z=0"""
    me = ob.data.copy()
    bm = bmesh.new(); bm.from_mesh(me)
    for z, keep_above in ((z0, True), (z1, False)):
        if (keep_above and z <= 1e-6) or (not keep_above and z >= H - 1e-6):
            continue
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        r = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, z), plane_no=(0, 0, 1),
                                   clear_inner=keep_above, clear_outer=not keep_above)
        cut_edges = [e for e in r['geom_cut'] if isinstance(e, bmesh.types.BMEdge)]
        if cut_edges:
            try:
                bmesh.ops.holes_fill(bm, edges=cut_edges, sides=0)
            except Exception as ex:
                print(name, '补切口失败', ex, flush=True)
    for v in bm.verts: v.co.z -= z0
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    # 精简
    if len(me.polygons) > face_cap:
        m = o.modifiers.new('dec', 'DECIMATE'); m.ratio = face_cap / len(me.polygons)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier='dec')
    chk = bmesh_from(o)   # 先存成变量：直接对临时 bmesh 迭代会在迭代中途被回收导致崩溃
    bd = sum(1 for e in chk.edges if not e.is_manifold)
    chk.free()
    print(f'{name}: 面 {len(o.data.polygons)} 高 {z1 - z0:.4f} 非流形边 {bd}', flush=True)
    return o


def bmesh_from(o):
    bm = bmesh.new(); bm.from_mesh(o.data); return bm


def export(o, path):
    """glb 留着审查；Unity 用 FBX（Y 朝上、-Z 朝前，和 Unity 一致）"""
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.gltf(filepath=str(path), use_selection=True, export_format='GLB')
    bpy.ops.export_scene.fbx(filepath=str(path.with_suffix('.fbx')), use_selection=True, path_mode='STRIP',
                             axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_UNITS', mesh_smooth_type='FACE',
                             bake_space_transform=True)   # 轴转换直接烘进顶点：Unity 里网格 y 就是高度


def save_albedo():
    """整栋楼共用一张贴图：存成 albedo.png，最长边限制到 2048"""
    for img in bpy.data.images:
        if img.size[0] == 0:
            continue
        if max(img.size) > 2048:
            k = 2048 / max(img.size)
            img.scale(int(img.size[0] * k), int(img.size[1] * k))
        img.filepath_raw = str(out / 'albedo.png'); img.file_format = 'PNG'; img.save()
        print('贴图', img.size[0], 'x', img.size[1], flush=True)
        return


parts = {
    'base': (0, f_base * H),
    'storey': (f_s0 * H, f_s1 * H),
    'crown': (f_crown * H, H),
}
# 原楼一共有几层标准层（游戏里按楼高需要堆几层）
n_mid = round((f_crown - f_base) / (f_s1 - f_s0))
meta = {'height': H, 'step': step, 'n_mid': n_mid, 'size_xy': [mx.x - mn.x, mx.y - mn.y], 'parts': {}}
for name, (z0, z1) in parts.items():
    o = piece(name, z0, z1)
    export(o, out / f'{name}.glb')
    meta['parts'][name] = {'height': z1 - z0, 'faces': len(o.data.polygons)}
save_albedo()
(out / 'meta.json').write_text(json.dumps(meta, indent=2, ensure_ascii=False))
print('完成', json.dumps(meta['parts'], ensure_ascii=False), flush=True)
