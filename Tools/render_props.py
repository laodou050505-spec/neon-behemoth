"""《我是怪兽》 · 附件白模检查图（在 Blender 里运行）：把目录里所有附件 FBX 排成一排，3/4 视角 Workbench 渲染（带 cavity 和描边）。
    Blender -b -P Tools/render_props.py -- <附件目录> <输出.png>"""
import bpy, sys, pathlib
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
folder, out = pathlib.Path(argv[0]), pathlib.Path(argv[1])
out.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)

x = 0.0
objs = []
for f in sorted(folder.glob('*.fbx')):
    bpy.ops.import_scene.fbx(filepath=str(f))
    ob = [o for o in bpy.context.selected_objects if o.type == 'MESH'][0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bb = [Vector(c) for c in ob.bound_box]
    w = max(v.x for v in bb) - min(v.x for v in bb)
    ob.location.x = x - min(v.x for v in bb)
    ob.location.z = -min(v.z for v in bb)
    x += w + 0.6
    objs.append(ob)

sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'; sh.color_type = 'SINGLE'; sh.single_color = (0.82, 0.82, 0.8)
sh.show_cavity = True; sh.cavity_type = 'BOTH'
sh.show_object_outline = True; sh.show_shadows = True
sc.render.resolution_x, sc.render.resolution_y = 2000, 800
sc.world = bpy.data.worlds.new('w'); sc.world.color = (0.32, 0.33, 0.36)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
sc.collection.objects.link(cam); sc.camera = cam
# 正交相机，按整排宽度取景（透视相机离得近时两边的附件会被裁掉）
cx = (x - 0.6) / 2
cam.data.type = 'ORTHO'
cam.data.ortho_scale = x * 1.08
d = Vector((0.55, -1.0, 0.6)).normalized()
cam.location = Vector((cx, 0, 1.2)) + d * 30
cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
sc.render.filepath = str(out)
bpy.ops.render.render(write_still=True)
print('完成', out, [o.name for o in objs], flush=True)
