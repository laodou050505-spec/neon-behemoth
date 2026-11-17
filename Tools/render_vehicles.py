"""《我是怪兽》 · 车辆体检图：把 ArtSource/Vehicles 下的车排成一排，按色板上色，3/4 视角 + 侧视渲染
    Blender -b -P Tools/render_vehicles.py -- <车目录> <输出.png>"""
import bpy, sys, pathlib, math
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
folder, out = pathlib.Path(argv[0]), pathlib.Path(argv[1])
bpy.ops.wm.read_factory_settings(use_empty=True)
PAL = ['#d2333a'] * 9 + ['#2b3a4c', '#18181c', '#f4eccc', '#c02a2a', '#fff2a0', '#2a2a30', '#c9ccd2']
def hx(h): h = h.lstrip('#'); return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)) + (1,)
mats = []
for i, c in enumerate(PAL):
    m = bpy.data.materials.new(f'p{i}'); m.diffuse_color = hx(c); mats.append(m)
obs = []
for f in sorted(folder.glob('*.fbx')):
    bpy.ops.import_scene.fbx(filepath=str(f))
    ob = [o for o in bpy.context.selected_objects if o.type == 'MESH'][0]
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    me = ob.data
    for m in mats: me.materials.append(m)
    uv = me.uv_layers.active.data
    for p in me.polygons:
        p.material_index = min(15, int(uv[p.loop_start].uv[0] * 16))
    obs.append(ob)
# 车长在哪个轴（导出时 -Y 前 → 导入后可能变成 z）：取最长的那个
def layout(along):
    x = 0
    for ob in obs:
        d = ob.dimensions
        ln = max(d.x, d.y)
        if along == 'x':   # 并排（车长沿 y），看 3/4
            ob.location = (x + d.x / 2, 0, 0); x += d.x + 1.2
        else:              # 首尾相接（车长沿 y），侧视
            ob.location = (0, x + d.y / 2, 0); x += d.y + 1.2
    return x
x = layout('x')
cx = x / 2
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'MATERIAL'
sc.display.shading.show_cavity = True; sc.display.shading.show_object_outline = True
sc.render.resolution_x, sc.render.resolution_y = 1800, 900
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = x * 0.6
imgs = []
cam.location = (cx + 9, -16, 12); cam.rotation_euler = (math.radians(58), 0, math.radians(28))
p = str(out.with_name(out.stem + '_iso.png')); sc.render.filepath = p; bpy.ops.render.render(write_still=True); imgs.append(p)
x = layout('y'); cx = x / 2; cam.data.ortho_scale = x * 0.55
cam.location = (30, cx, 1.2); cam.rotation_euler = (math.radians(90), 0, math.radians(90))
p = str(out.with_name(out.stem + '_side.png')); sc.render.filepath = p; bpy.ops.render.render(write_still=True); imgs.append(p)
print('渲染', imgs)
