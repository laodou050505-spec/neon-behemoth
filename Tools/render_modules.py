"""《我是怪兽》 · 楼模块体检图（在 Blender 里运行）：把 底座 + N 层标准层（A/B 交替）+ 楼顶 堆成整栋楼，渲染白模。
    Blender -b -P Tools/render_modules.py -- <模块目录> <输出.png> [标准层数=8]
输出一张 2×2 拼图：整栋 3/4 视角、整栋正面、近景（两层标准层 + 阳台 / 窗洞）、线框（看面数分布）。
用 Workbench 引擎（快），开 cavity + 描边，结构问题（缝、错位、穿插、悬空）一眼能看出来。"""
import bpy, sys, pathlib, math
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
folder, out = pathlib.Path(argv[0]), pathlib.Path(argv[1])
n_mid = int(argv[2]) if len(argv) > 2 else 8

bpy.ops.wm.read_factory_settings(use_empty=True)


def load(name):
    bpy.ops.import_scene.fbx(filepath=str(folder / f'{name}.fbx'))
    ob = [o for o in bpy.context.selected_objects if o.type == 'MESH'][0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return ob


def height(ob):
    zs = [(ob.matrix_world @ Vector(c)).z for c in ob.bound_box]
    return max(zs) - min(zs), min(zs)


parts = {n: load(n) for n in ('base', 'storey_a', 'storey_b', 'crown') if (folder / f'{n}.fbx').exists()}
parts.setdefault('storey_b', parts['storey_a'])   # 矮楼只有一种标准层
for ob in parts.values():
    ob.hide_render = True; ob.hide_set(True)

# 堆叠：每块复制一份，按高度往上摞（模块底面在 z=0）
z = 0.0
stack = []
order = ['base'] + [('storey_a', 'storey_b')[i % 2] for i in range(n_mid)] + ['crown']
for name in order:
    src = parts[name]
    ob = src.copy(); ob.data = src.data
    bpy.context.scene.collection.objects.link(ob)
    h, z0 = height(src)
    ob.location.z = z - z0
    ob.hide_render = False
    stack.append(ob)
    # 楼板伸出墙面，所以层高按墙高算：标准层 = 包围盒高；底座、楼顶同理
    z += h if name != 'crown' else 0
total_h = z + height(parts['crown'])[0]

sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'; sh.color_type = 'SINGLE'; sh.single_color = (0.82, 0.82, 0.8)
sh.show_cavity = True; sh.cavity_type = 'BOTH'; sh.cavity_ridge_factor = 1.0; sh.cavity_valley_factor = 1.5
sh.show_object_outline = True; sh.show_shadows = True; sh.shadow_intensity = 0.5
sc.display.shadow_shift = 0.1
sc.render.resolution_x, sc.render.resolution_y = 900, 1100
sc.render.film_transparent = False
sc.world = bpy.data.worlds.new('w'); sc.world.color = (0.32, 0.33, 0.36)

cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
sc.collection.objects.link(cam); sc.camera = cam
size = max(parts['storey_a'].dimensions.x, parts['storey_a'].dimensions.y)


def shot(path, pos, target, lens=50, wire=False):
    cam.location = Vector(pos)
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = lens
    sh.show_xray = False
    for ob in stack:
        ob.display_type = 'WIRE' if wire else 'SOLID'
    if wire:
        sh.show_object_outline = False
        sc.display.render_aa = '8'
    else:
        sh.show_object_outline = True
    sc.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


tmp = out.parent / (out.stem + '_parts')
tmp.mkdir(parents=True, exist_ok=True)
R = max(total_h, size) * 1.35
shot(tmp / 'a.png', (R * 0.75, -R * 0.95, total_h * 0.7), (0, 0, total_h * 0.45), 50)
shot(tmp / 'b.png', (0, -R * 1.25, total_h * 0.5), (0, 0, total_h * 0.5), 50)
hs = height(parts['storey_a'])[0]
zc = height(parts['base'])[0] + hs * 2
shot(tmp / 'c.png', (size * 0.55, -size * 1.0, zc + hs * 0.8), (size * 0.15, 0, zc), 35)
shot(tmp / 'd.png', (R * 0.75, -R * 0.95, total_h * 0.7), (0, 0, total_h * 0.45), 50, wire=True)

# 拼成 2×2
imgs = [bpy.data.images.load(str(tmp / f'{k}.png')) for k in 'abcd']
W, H = imgs[0].size
sheet = bpy.data.images.new('sheet', W * 2, H * 2)
import numpy as np
px = np.zeros((H * 2, W * 2, 4), np.float32)
for i, im in enumerate(imgs):
    a = np.array(im.pixels[:], np.float32).reshape(H, W, 4)
    r, c = 1 - i // 2, i % 2
    px[r * H:(r + 1) * H, c * W:(c + 1) * W] = a
sheet.pixels = px.ravel()
sheet.filepath_raw = str(out); sheet.file_format = 'PNG'; sheet.save()
tris = {n: sum(len(p.vertices) - 2 for p in parts[n].data.polygons) for n in parts}
print('总高', round(total_h, 2), '米；每块三角形', tris, '；整栋', sum(tris[n] for n in order), flush=True)
