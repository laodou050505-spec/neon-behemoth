"""《我是怪兽》 · 量楼层：统计朝上 / 朝下的水平面在各高度的面积（楼板、阳台板会形成尖峰），用来决定在哪里切层。
    Blender -b -P Tools/floor_profile.py -- <模型.glb> [分箱数]
输出：总高、每个尖峰的高度（相对 0~1）和面积，以及估计的标准层高度。"""
import bpy, sys, bmesh
argv = sys.argv[sys.argv.index('--') + 1:]
bins = int(argv[1]) if len(argv) > 1 else 200
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=argv[0])
area = [0.0] * bins
zs = []
objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for o in objs:
    for p in o.data.polygons:
        zs.append((o.matrix_world @ p.center).z)
z0, z1 = min(zs), max(zs)
H = z1 - z0
for o in objs:
    mw = o.matrix_world
    for p in o.data.polygons:
        n = (mw.to_3x3() @ p.normal).normalized()
        if abs(n.z) < 0.9:
            continue
        z = ((mw @ p.center).z - z0) / H
        area[min(bins - 1, int(z * bins))] += p.area
mx = max(area)
peaks = [i for i in range(1, bins - 1) if area[i] > mx * 0.08 and area[i] >= area[i - 1] and area[i] >= area[i + 1]]
print(f'总高 {H:.4f}（模型单位）', flush=True)
for i in peaks:
    print(f'  峰 z={i / bins:.3f}  面积 {area[i] / mx:.2f}', flush=True)
gaps = [(peaks[k + 1] - peaks[k]) / bins for k in range(len(peaks) - 1)]
if gaps:
    gaps_sorted = sorted(gaps)
    print('间距中位数（标准层高 / 总高）', round(gaps_sorted[len(gaps_sorted) // 2], 4), flush=True)
