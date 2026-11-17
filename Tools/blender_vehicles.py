"""《我是怪兽》 · 民用车（Blender 程序化，真实尺寸米）：sedan 轿车 / taxi 出租车 / sports 跑车 / suv / pickup 皮卡 / van 面包车 / bus 公交
    Blender -b -P Tools/blender_vehicles.py -- <输出目录>
- 车身 = 侧面轮廓（车头、引擎盖、前风挡斜面、车顶、后风挡、后备箱，底边带两个轮拱凹口）沿车宽挤出；
- 侧窗（前后两块，中间 B 柱）、前后风挡是贴在车身上的薄玻璃；保险杠、格栅、车灯、车牌、后视镜、门把手各自是小件；
- 轮子 = 12 边轮胎 + 凸出的轮毂盖，嵌在轮拱里；
- UV：每个面指向 16×1 色板的一格（Unity CityStreets.CarMat），车身格 0 由 Unity 换成每辆车的颜色。
- 车头朝 Blender -Y（导出后 Unity +Z）；每辆过封闭 / 共面 / 法线检查。"""
import bpy, bmesh, sys, json, math, pathlib
from mathutils import Vector

OUT = pathlib.Path(sys.argv[sys.argv.index('--') + 1])
OUT.mkdir(parents=True, exist_ok=True)
exec(open(pathlib.Path(__file__).with_name('blender_mesh_checks.py'), encoding='utf-8').read())

BODY, GLASS, TYRE, HEAD, TAIL, TAXI, TRIM, CHROME = 0, 9, 10, 11, 12, 13, 14, 15


class Part:
    def __init__(self):
        self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new('UVMap'); self.groups = []

    def face(self, pts, pal, group):
        vs = [self.bm.verts.new(p) for p in pts]
        group.extend(vs)
        f = self.bm.faces.new(vs)
        for l in f.loops:
            l[self.uv].uv = ((pal + 0.5) / 16.0, 0.5)
        return f

    def box(self, x0, y0, z0, x1, y1, z1, pal):
        x0, x1 = sorted((x0, x1)); y0, y1 = sorted((y0, y1)); z0, z1 = sorted((z0, z1))
        g = []
        c = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
        for idx in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
            self.face([c[i] for i in idx], pal, g)
        self.groups.append(g)

    def prism_x(self, prof, x0, x1, pal):
        """yz 平面上的多边形（可凹）沿 x 挤出：侧面四边形 + 两端 n 边形（finish 里三角化）"""
        g = []
        n = len(prof)
        for i in range(n):
            (ya, za), (yb, zb) = prof[i], prof[(i + 1) % n]
            self.face([(x0, ya, za), (x0, yb, zb), (x1, yb, zb), (x1, ya, za)], pal, g)
        self.face([(x0, y, z) for (y, z) in prof[::-1]], pal, g)
        self.face([(x1, y, z) for (y, z) in prof], pal, g)
        self.groups.append(g)

    def slab(self, a, b, width, thick, pal, shrink=0.06):
        """贴在斜面上的薄板（风挡）：a、b 是斜面上下两端 (y, z)，往外凸 thick"""
        A, B = Vector((0, *a)), Vector((0, *b))
        d = B - A; A, B = A + d * shrink, B - d * shrink
        n = Vector((0, -d.z, d.y)).normalized()
        if n.z < 0: n = -n
        n *= thick
        xa, xb = -width / 2, width / 2
        q = [(xa, A.y, A.z), (xb, A.y, A.z), (xb, B.y, B.z), (xa, B.y, B.z)]
        q2 = [(x, y + n.y, z + n.z) for (x, y, z) in q]
        c = q + q2
        g = []
        for idx in ((0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)):
            self.face([c[i] for i in idx], pal, g)
        self.groups.append(g)

    def cylinder_x(self, cx, cy, cz, r, w, seg, pal):
        g = []
        ring = [(cy + r * math.cos(2 * math.pi * (k + 0.5) / seg), cz + r * math.sin(2 * math.pi * (k + 0.5) / seg)) for k in range(seg)]
        x0, x1 = cx - w / 2, cx + w / 2
        for i in range(seg):
            (ya, za), (yb, zb) = ring[i], ring[(i + 1) % seg]
            self.face([(x0, ya, za), (x0, yb, zb), (x1, yb, zb), (x1, ya, za)], pal, g)
        self.face([(x0, y, z) for (y, z) in ring[::-1]], pal, g)
        self.face([(x1, y, z) for (y, z) in ring], pal, g)
        self.groups.append(g)

    def finish(self, name):
        for g in self.groups:
            bmesh.ops.remove_doubles(self.bm, verts=[v for v in g if v.is_valid], dist=1e-5)
        ng = [f for f in self.bm.faces if len(f.verts) > 4]
        if ng:
            bmesh.ops.triangulate(self.bm, faces=ng, quad_method='BEAUTY', ngon_method='EAR_CLIP')
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        fix_coplanar(self.bm)
        rep = {'tris': sum(len(f.verts) - 2 for f in self.bm.faces),
                                    'open_edges': sum(1 for e in self.bm.edges if len(e.link_faces) == 1),
                                    'nonmanifold': sum(1 for e in self.bm.edges if len(e.link_faces) > 2),
                                    'coplanar': visible_coplanar_count(self.bm), 'flipped': flipped_faces(self.bm)[0], 'floating': floating_parts(self.bm)[0]}
        if rep['floating']: print('  悬空', name, floating_parts(self.bm)[1], flush=True)
        me = bpy.data.meshes.new(name); self.bm.to_mesh(me)
        # 停车场 / 院子里停着的车用的简化版：去掉最薄一边 < 6cm 的小件（车牌、门把手、轮毂盖、车灯、后视镜、行李架）
        b2 = self.bm.copy()
        kill = lod_kill_safe(b2, lambda d, vol: d[0] < 0.06 and d[1] < 0.6)
        bmesh.ops.delete(b2, geom=list(set(kill)), context='FACES')
        if floating_parts(b2)[0]: print('  LOD1 悬空', name, floating_parts(b2), flush=True)
        me2 = bpy.data.meshes.new(name + '_lod1'); b2.to_mesh(me2); b2.free(); self.bm.free()
        ob2 = bpy.data.objects.new(name + '_lod1', me2); bpy.context.scene.collection.objects.link(ob2); LODS.append(ob2)
        ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
        return ob, rep


# 车型（米）：L 长 W 宽 H 高；nose 车头高 belt 腰线（引擎盖 / 后备箱高）tail 车尾高；
# hood 引擎盖长 ws 前风挡水平长 roof 车顶长 rear 后风挡水平长（其余是后备箱）；clear 离地 r 轮半径 base 轴距
CARS = {
    'sedan':  dict(L=4.6, W=1.82, H=1.42, nose=0.72, belt=0.95, tail=0.95, hood=1.15, ws=0.72, roof=1.15, rear=0.6, clear=0.3, r=0.32, base=2.7),
    'taxi':   dict(L=4.6, W=1.82, H=1.42, nose=0.72, belt=0.95, tail=0.95, hood=1.15, ws=0.72, roof=1.15, rear=0.6, clear=0.3, r=0.32, base=2.7),
    'sports': dict(L=4.4, W=1.92, H=1.16, nose=0.56, belt=0.8, tail=0.86, hood=1.35, ws=0.8, roof=0.85, rear=0.8, clear=0.22, r=0.33, base=2.55),
    'suv':    dict(L=4.7, W=1.92, H=1.75, nose=0.92, belt=1.12, tail=1.12, hood=1.0, ws=0.55, roof=2.0, rear=0.28, clear=0.42, r=0.38, base=2.8),
    'pickup': dict(L=5.3, W=1.95, H=1.82, nose=0.95, belt=1.12, tail=1.12, hood=1.15, ws=0.5, roof=1.05, rear=0.12, clear=0.45, r=0.39, base=3.3),
    'van':    dict(L=5.0, W=1.98, H=2.05, nose=0.98, belt=1.08, tail=1.05, hood=0.5, ws=0.6, roof=3.6, rear=0.05, clear=0.33, r=0.34, base=3.2),
    'bus':    dict(L=11.5, W=2.5, H=3.05, nose=1.0, belt=1.0, tail=1.0, hood=0.0, ws=0.2, roof=11.0, rear=0.05, clear=0.32, r=0.48, base=6.2),
}


def arch(yc, r, clear, k=8):
    """轮拱凹口（从后往前走，顺着底边方向 +y → -y 时调用方再反转）：半圆，半径比轮子大 6cm"""
    R = r + 0.06
    return [(yc + R * math.cos(math.pi * i / k), max(clear, r + R * math.sin(math.pi * i / k) * 0.92)) for i in range(k + 1)]


def build(kind, P):
    L, W, H = P['L'], P['W'], P['H']
    y0, y1 = -L / 2, L / 2
    yws = y0 + P['hood']; yr0 = yws + P['ws']; yr1 = yr0 + P['roof']; ydeck = yr1 + P['rear']
    if kind == 'pickup':
        cab = yr1 + 0.1
    m = Part()
    axles = [-P['base'] / 2, P['base'] / 2] if kind != 'bus' else [y0 + 2.6, y1 - 3.0]
    # 侧面轮廓：底边从车头到车尾（中间两个轮拱），再沿车身上沿回到车头（逆时针，从 +x 看）
    bottom = [(y0 + 0.15, P['clear'])]
    for yc in axles:
        pts = arch(yc, P['r'], P['clear'])[::-1]   # 从 yc-R 到 yc+R
        bottom += pts
    end = cab if kind == 'pickup' else y1
    bottom += [(end - 0.12, P['clear'])]
    if kind == 'pickup':
        bottom = [p for p in bottom if p[0] < cab - 0.05] + [(cab, P['clear'])]
        top = [(cab, H), (yr0, H), (yws, P['belt']), (y0 + 0.12, P['belt'] - 0.04), (y0, P['nose'])]
    elif kind == 'bus':
        top = [(y1, P['tail']), (y1, H - 0.12), (y1 - 0.12, H), (y0 + 0.15, H), (y0, H - 0.25), (y0, P['nose'])]
    else:
        top = [(y1, P['tail']), (ydeck + (y1 - ydeck) * 0.85, P['belt']), (ydeck, P['belt']), (yr1, H), (yr0, H), (yws, P['belt']), (y0 + 0.12, P['belt'] - 0.04), (y0, P['nose'])]
    prof = bottom + top
    m.prism_x(prof, -W / 2 + 0.03, W / 2 - 0.03, BODY)
    if kind == 'pickup':
        # 货斗：底板 + 两侧和后挡板（不和驾驶室相交，留 5cm 缝）
        b0 = cab + 0.05
        m.box(-W / 2 + 0.03, b0, P['clear'] + 0.15, W / 2 - 0.03, y1, P['clear'] + 0.4, BODY)   # 货斗底板压到保险杠上
        m.box(-W / 2 + 0.03, b0, P['clear'] + 0.4, -W / 2 + 0.11, y1, P['belt'], BODY)
        m.box(W / 2 - 0.11, b0, P['clear'] + 0.4, W / 2 - 0.03, y1, P['belt'], BODY)
        m.box(-W / 2 + 0.11, y1 - 0.08, P['clear'] + 0.4, W / 2 - 0.11, y1, P['belt'], BODY)
        for yc in axles[1:]:
            m.box(-W / 2 + 0.03, yc - P['r'] - 0.1, P['clear'], W / 2 - 0.03, yc + P['r'] + 0.1, P['clear'] + 0.25, TRIM)
    # ── 玻璃 ──
    gx = W / 2 - 0.03
    if kind == 'bus':
        for sx in (-1, 1):
            m.box(sx * gx, y0 + 1.3, 1.3, sx * (gx + 0.015), y1 - 0.6, H - 0.3, GLASS)
        m.box(-W / 2 + 0.18, y0 - 0.015, 1.1, W / 2 - 0.18, y0, H - 0.3, GLASS)
        m.box(W / 2 - 0.03, y0 + 0.35, 0.4, W / 2 - 0.015, y0 + 1.15, 2.7, GLASS)
        m.box(W / 2 - 0.03, 0.4, 0.4, W / 2 - 0.015, 1.4, 2.7, GLASS)
        m.box(-W / 2 + 0.3, y0 + 0.4, H, W / 2 - 0.3, y1 - 1.5, H + 0.28, TRIM)          # 车顶空调
        m.box(-W / 2 + 0.25, y0 - 0.02, H - 0.42, W / 2 - 0.25, y0 + 0.001, H - 0.24, HEAD)   # 线路牌（夜里亮）
    else:
        zb = P['belt'] + 0.05
        rear_end = (cab - 0.1) if kind == 'pickup' else (ydeck - 0.05 if P['rear'] > 0.1 else yr1 - 0.15)
        front_end = yws + P['ws'] * 0.6       # 前窗前沿离风挡根部留一段，窗是梯形不是三角
        bpil = yr0 + (rear_end - yr0) * 0.42
        for sx in (-1, 1):
            for ya, yb in ((front_end, bpil - 0.05), (bpil + 0.05, rear_end)):
                if yb - ya < 0.25: continue
                # 窗顶跟着风挡斜面走：前块前沿收到斜面上，后块后沿收到后风挡斜面上
                def roofz(y):
                    if y < yr0: return P['belt'] + (H - P['belt']) * (y - yws) / max(P['ws'], 1e-3) - 0.07
                    if y > yr1 and P['rear'] > 0.1: return H - (H - P['belt']) * (y - yr1) / P['rear'] - 0.07
                    return H - 0.07
                za, zbb = roofz(ya), roofz(yb)
                win = [(ya, zb), (yb, zb), (yb, max(zbb, zb + 0.1)), (ya, max(za, zb + 0.1))]
                x_in, x_out = sx * gx, sx * (gx + 0.012)
                m.prism_x(win if sx > 0 else win, min(x_in, x_out), max(x_in, x_out), GLASS)
            # 门把手
            for yh in (bpil - 0.35, bpil + 0.45):
                if yh < rear_end:
                    m.box(sx * gx, yh - 0.08, P['belt'] - 0.08, sx * (gx + 0.018), yh + 0.08, P['belt'] - 0.04, CHROME)
        m.slab((yws, P['belt']), (yr0, H), W - 0.3, 0.012, GLASS)
        if P['rear'] > 0.1 and kind != 'pickup':
            m.slab((yr1, H), (ydeck, P['belt']), W - 0.3, 0.012, GLASS)
        if kind == 'pickup':
            m.box(-W / 2 + 0.2, cab, P['belt'] + 0.1, W / 2 - 0.2, cab + 0.012, H - 0.12, GLASS)
        # 后视镜
        for sx in (-1, 1):
            m.box(sx * gx, yws + 0.12, P['belt'] + 0.08, sx * (W / 2 + 0.17), yws + 0.24, P['belt'] + 0.24, TRIM)
    if kind == 'taxi':
        m.box(-0.35, yr0 + 0.3, H, 0.35, yr0 + 0.62, H + 0.24, TAXI)
    if kind == 'sports':
        m.box(-W / 2 + 0.15, y1 - 0.3, P['tail'], W / 2 - 0.15, y1 - 0.05, P['tail'] + 0.05, TRIM)     # 尾翼板
        for sx in (-1, 1):
            m.box(sx * (W / 2 - 0.35), y1 - 0.25, P['belt'] - 0.02, sx * (W / 2 - 0.3), y1 - 0.2, P['tail'], TRIM)
    if kind == 'suv':
        for sx in (-1, 1):
            m.box(sx * (W / 2 - 0.25), yr0 + 0.1, H, sx * (W / 2 - 0.2), yr1 - 0.1, H + 0.06, CHROME)     # 车顶行李架
    # ── 保险杠 / 格栅 / 车灯 / 车牌 ──
    fb = y0 - 0.06
    m.box(-W / 2, fb, P['clear'] - 0.03, W / 2, y0 + 0.22, P['clear'] + 0.2, TRIM)
    m.box(-W / 2, y1 - 0.22, P['clear'] - 0.03, W / 2, y1 + 0.06, P['clear'] + 0.2, TRIM)
    m.box(-W * 0.28, y0 - 0.035, P['clear'] + 0.28, W * 0.28, y0 + 0.05, P['nose'] - 0.1, TRIM)
    m.box(-0.26, fb - 0.015, P['clear'] + 0.02, 0.26, fb, P['clear'] + 0.15, CHROME)
    m.box(-0.26, y1 + 0.06, P['clear'] + 0.02, 0.26, y1 + 0.075, P['clear'] + 0.15, CHROME)
    zt = (P['belt'] if kind == 'pickup' else P['tail']) - 0.05
    for sx in (-1, 1):
        m.box(sx * (W / 2 - 0.05), y0 - 0.025, P['nose'] - 0.2, sx * (W / 2 - 0.38), y0 + 0.05, P['nose'] - 0.05, HEAD)
        m.box(sx * (W / 2 - 0.05), y1 - 0.05, zt - 0.2, sx * (W / 2 - 0.35), y1 + 0.025, zt, TAIL)
    # ── 轮子：轮胎 + 轮毂盖 ──
    tw = 0.25 if kind != 'bus' else 0.32
    for yc in axles:
        # 车轴：从左轮穿到右轮（藏在车身和轮拱里），车身真正“架”在轮子上
        m.box(-W / 2 + 0.05, yc - 0.06, P['r'] - 0.06, W / 2 - 0.05, yc + 0.06, P['r'] + (P['r'] + 0.06) * 0.92 + 0.06, TRIM)   # 一直顶到轮拱上沿，嵌进车身
        for sx in (-1, 1):
            xc = sx * (W / 2 - tw / 2 + 0.01)
            m.cylinder_x(xc, yc, P['r'], P['r'], tw, 12, TYRE)
            m.cylinder_x(xc + sx * (tw / 2 + 0.008), yc, P['r'], P['r'] * 0.58, 0.016, 10, CHROME)
    return m.finish(kind)


bpy.ops.wm.read_factory_settings(use_empty=True)
report = {}
LODS = []
for kind, P in CARS.items():
    ob, rep = build(kind, P)
    report[kind] = rep
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=str(OUT / f'{kind}.fbx'), use_selection=True, path_mode='STRIP', axis_forward='-Z', axis_up='Y',
                                                                                                    apply_scale_options='FBX_SCALE_UNITS', mesh_smooth_type='FACE', bake_space_transform=True)
print('报告', json.dumps(report, ensure_ascii=False), flush=True)
for ob in LODS:
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=str(OUT / f'{ob.name}.fbx'), use_selection=True, path_mode='STRIP', axis_forward='-Z', axis_up='Y',
                             apply_scale_options='FBX_SCALE_UNITS', mesh_smooth_type='FACE', bake_space_transform=True)
    print('LOD', ob.name, sum(len(p.vertices) - 2 for p in ob.data.polygons), flush=True)
