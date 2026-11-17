"""《我是怪兽》 · 楼顶 / 立面附件（在 Blender 里运行），真实尺寸（米），每件封闭网格、单独导出：
    Blender -b -P Tools/blender_props.py -- <输出目录>
- ac_unit      空调外机：机身（倒角）+ 顶部风扇圈 + 格栅辐条 + 两侧百叶 + 钢支架 + 铜管
- water_tank   水箱：圆柱罐体 + 穹顶盖 + 四条腿 + 斜撑 + 爬梯
- vent         通风管：方形风管 + 弯头 + 防雨帽
- dish         卫星天线：碟面 + 支架 + 底座
- stair_hut    楼梯间小屋：墙体 + 门 + 小檐
每件 UV 落在共用的“附件图集”区域里（金属 / 锈 / 深色 / 铜 / 灰泥），Tools/compose_props.py 拼贴图。
每件都过封闭检查，面数写进 meta.json。"""
import bpy, bmesh, sys, json, math, pathlib
from mathutils import Vector

OUT = pathlib.Path(sys.argv[sys.argv.index('--') + 1])
OUT.mkdir(parents=True, exist_ok=True)

# 附件图集（512×512 够用，附件在游戏里只有几厘米）
ATLAS = {
    'metal':  (0.00, 0.50, 0.50, 1.00),   # 浅灰钣金（带锈斑）
    'dark':   (0.50, 0.50, 1.00, 1.00),   # 深色金属 / 格栅 / 风扇
    'rust':   (0.00, 0.00, 0.50, 0.50),   # 锈蚀钢（支架、爬梯）
    'copper': (0.50, 0.25, 0.75, 0.50),   # 铜管
    'rubber': (0.75, 0.25, 1.00, 0.50),   # 黑色保温管
    'stucco': (0.50, 0.00, 1.00, 0.25),   # 灰泥墙（楼梯间）
}
report = {}


class M:
    def __init__(self, name):
        self.name = name; self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new('UVMap')
        self.groups = []; self.cur = []

    def _uv(self, f, region, scale):
        u0, v0, u1, v1 = ATLAS[region]
        n = f.normal
        ax = Vector((1, 0, 0)) if abs(n.z) > 0.7 else Vector((-n.y, n.x, 0)).normalized()
        ay = Vector((0, 1, 0)) if abs(n.z) > 0.7 else Vector((0, 0, 1))
        for l in f.loops:
            a = (l.vert.co.dot(ax) / scale) % 1.0; b = (l.vert.co.dot(ay) / scale) % 1.0
            l[self.uv].uv = (u0 + (u1 - u0) * a, v0 + (v1 - v0) * b)

    def _face(self, pts, region, scale=0.6):
        vs = [self.bm.verts.new(p) for p in pts]
        self.cur.extend(vs)
        f = self.bm.faces.new(vs); f.normal_update()
        self._uv(f, region, scale)
        return f

    def begin(self):
        self.cur = []; self.groups.append(self.cur)

    def box(self, x0, y0, z0, x1, y1, z1, region, scale=0.6):
        self.begin()
        c = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
        for idx in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
            self._face([c[i] for i in idx], region, scale)

    def cyl(self, cx, cy, z0, z1, r, seg, region, r_top=None, scale=0.6):
        """竖直圆柱 / 圆台（封闭）"""
        self.begin()
        rt = r if r_top is None else r_top
        lo = [(cx + r * math.cos(2 * math.pi * i / seg), cy + r * math.sin(2 * math.pi * i / seg), z0) for i in range(seg)]
        hi = [(cx + rt * math.cos(2 * math.pi * i / seg), cy + rt * math.sin(2 * math.pi * i / seg), z1) for i in range(seg)]
        for i in range(seg):
            j = (i + 1) % seg
            self._face([lo[i], lo[j], hi[j], hi[i]], region, scale)
        self._face(hi, region, scale)
        self._face(lo[::-1], region, scale)

    def tube(self, pts, r, seg, region, scale=0.6):
        """沿折线的封闭管子（两端封口），转角处用平分面相接"""
        self.begin()
        P = [Vector(p) for p in pts]
        rings = []
        for k, p in enumerate(P):
            d = (P[min(k + 1, len(P) - 1)] - P[max(k - 1, 0)]).normalized()
            up = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
            a = d.cross(up).normalized(); b = d.cross(a).normalized()
            rings.append([p + (a * math.cos(2 * math.pi * i / seg) + b * math.sin(2 * math.pi * i / seg)) * r for i in range(seg)])
        for k in range(len(rings) - 1):
            for i in range(seg):
                j = (i + 1) % seg
                self._face([rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]], region, scale)
        self._face(rings[0][::-1], region, scale)
        self._face(rings[-1], region, scale)

    def finish(self):
        for g in self.groups:
            vs = [v for v in g if v.is_valid]
            if vs: bmesh.ops.remove_doubles(self.bm, verts=vs, dist=1e-5)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        nb = sum(1 for e in self.bm.edges if len(e.link_faces) == 1)
        nm = sum(1 for e in self.bm.edges if len(e.link_faces) > 2)
        me = bpy.data.meshes.new(self.name); self.bm.to_mesh(me); self.bm.free()
        ob = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(ob)
        tris = sum(len(p.vertices) - 2 for p in me.polygons)
        report[self.name] = {'tris': tris, 'open_edges': nb, 'nonmanifold': nm}
        return ob


def ac_unit():
    """1.0 × 1.0 × 0.9 m 商用空调外机，坐在 0.25 m 高的钢架上"""
    m = M('ac_unit')
    S, H0, H1 = 0.5, 0.25, 1.05
    # 钢架：四根腿 + 上下两圈横梁（各自封闭小方条，互相不焊）
    for sx in (-1, 1):
        for sy in (-1, 1):
            m.box(sx * S - 0.03 if sx > 0 else -S, sy * S - 0.03 if sy > 0 else -S, 0, sx * S if sx > 0 else -S + 0.03, sy * S if sy > 0 else -S + 0.03, H0, 'rust')
    m.box(-S, -S, H0 - 0.04, S, -S + 0.03, H0, 'rust'); m.box(-S, S - 0.03, H0 - 0.04, S, S, H0, 'rust')
    m.box(-S, -S + 0.03, H0 - 0.04, -S + 0.03, S - 0.03, H0, 'rust'); m.box(S - 0.03, -S + 0.03, H0 - 0.04, S, S - 0.03, H0, 'rust')
    # 机身（下部机箱 + 上盖略大一圈）
    m.box(-0.47, -0.47, H0, 0.47, 0.47, H1 - 0.1, 'metal')
    m.box(-0.49, -0.49, H1 - 0.1, 0.49, 0.49, H1, 'metal')
    # 两侧百叶：每面 7 条凸出的薄板
    for k in range(7):
        z = H0 + 0.1 + k * 0.08
        m.box(-0.40, -0.50, z, 0.05, -0.47, z + 0.035, 'dark')     # 正面左半
        m.box(-0.50, -0.40, z, -0.47, 0.30, z + 0.035, 'dark')     # 左侧
    # 顶部风扇：深色风扇圈（圆台）+ 中心电机罩 + 4 条格栅辐条
    m.cyl(0.0, 0.0, H1, H1 + 0.06, 0.38, 20, 'dark')
    m.cyl(0.0, 0.0, H1 + 0.06, H1 + 0.1, 0.1, 12, 'dark')
    for a in range(4):
        ang = a * math.pi / 4
        dx, dy = math.cos(ang) * 0.36, math.sin(ang) * 0.36
        m.tube([(-dx, -dy, H1 + 0.07), (dx, dy, H1 + 0.07)], 0.008, 4, 'dark')
    # 铜管 + 黑色保温管（从右侧伸出）
    m.tube([(0.47, 0.25, H0 + 0.2), (0.62, 0.25, H0 + 0.2), (0.62, 0.25, H0 - 0.05)], 0.025, 6, 'rubber')
    m.tube([(0.47, 0.12, H0 + 0.12), (0.58, 0.12, H0 + 0.12), (0.58, 0.12, H0 - 0.05)], 0.015, 6, 'copper')
    return m.finish()


def water_tank():
    """直径 1.6 m、高 1.8 m 的镀锌水箱，0.6 m 高的四腿支架 + 爬梯"""
    m = M('water_tank')
    R, L, H = 0.8, 0.6, 1.8
    for a in range(4):
        ang = math.pi / 4 + a * math.pi / 2
        x, y = math.cos(ang) * R * 0.75, math.sin(ang) * R * 0.75
        m.box(x - 0.05, y - 0.05, 0, x + 0.05, y + 0.05, L, 'rust')
    # 两条交叉斜撑（细管）
    m.tube([(-R * 0.53, -R * 0.53, 0.05), (R * 0.53, -R * 0.53, L - 0.05)], 0.02, 4, 'rust')
    m.tube([(-R * 0.53, R * 0.53, 0.05), (R * 0.53, R * 0.53, L - 0.05)], 0.02, 4, 'rust')
    m.cyl(0, 0, L, L + 0.06, R * 0.95, 16, 'rust')              # 底板
    m.cyl(0, 0, L + 0.06, L + H, R, 16, 'metal')                # 罐体
    m.cyl(0, 0, L + H, L + H + 0.25, R, 16, 'metal', r_top=0.25)  # 穹顶盖
    m.cyl(0, 0, L + H + 0.25, L + H + 0.35, 0.18, 10, 'dark')   # 检修口
    # 罐体加强箍
    for z in (L + 0.5, L + 1.2):
        m.cyl(0, 0, z, z + 0.05, R + 0.02, 16, 'rust')
    # 爬梯：两根立杆 + 6 根横档
    for x in (-0.18, 0.18):
        m.box(x - 0.02, -R - 0.15, 0, x + 0.02, -R - 0.11, L + H + 0.2, 'rust')
    for k in range(6):
        z = 0.3 + k * 0.35
        m.box(-0.16, -R - 0.15, z, 0.16, -R - 0.12, z + 0.03, 'rust')
    # 出水管 + 阀门
    m.tube([(R - 0.05, 0, L + 0.15), (R + 0.25, 0, L + 0.15), (R + 0.25, 0, 0.05)], 0.05, 8, 'rust')
    m.cyl(R + 0.25, 0, 0.3, 0.42, 0.09, 8, 'dark')
    return m.finish()


def vent():
    """屋顶排风管：0.5 m 方风管竖起 1.2 m，顶上弯头朝下 + 防雨帽"""
    m = M('vent')
    m.box(-0.3, -0.3, 0, 0.3, 0.3, 0.12, 'metal')               # 基座
    m.box(-0.25, -0.25, 0.12, 0.25, 0.25, 1.2, 'metal')         # 风管
    m.box(-0.3, -0.3, 1.2, 0.3, 0.3, 1.26, 'dark')              # 法兰
    m.box(-0.25, -0.25, 1.26, 0.25, 0.75, 1.7, 'metal')         # 弯头（朝 +Y 伸出）
    m.box(-0.27, 0.55, 1.1, 0.27, 0.8, 1.72, 'dark')            # 防雨百叶口
    return m.finish()


def dish():
    """卫星天线：碟面（浅圆台）+ 馈源杆 + 底座"""
    m = M('dish')
    m.box(-0.35, -0.35, 0, 0.35, 0.35, 0.08, 'dark')            # 配重底座
    m.cyl(0, 0, 0.08, 0.9, 0.05, 8, 'rust')                     # 立杆
    # 碟面：倾斜的浅圆台，用一组点手工旋转
    me = M('dish_tmp')
    obj = m
    ang = math.radians(35)
    c = Vector((0, 0.05, 1.05))
    seg, R, D = 20, 0.55, 0.12
    rot = lambda v: Vector((v.x, v.y * math.cos(ang) - v.z * math.sin(ang), v.y * math.sin(ang) + v.z * math.cos(ang))) + c
    m.begin()
    rim = [rot(Vector((R * math.cos(2 * math.pi * i / seg), R * math.sin(2 * math.pi * i / seg), 0))) for i in range(seg)]
    back = [rot(Vector((R * 0.35 * math.cos(2 * math.pi * i / seg), R * 0.35 * math.sin(2 * math.pi * i / seg), -D))) for i in range(seg)]
    for i in range(seg):
        j = (i + 1) % seg
        m._face([rim[i], rim[j], back[j], back[i]], 'metal')
    m._face(rim[::-1], 'metal'); m._face(back, 'metal')
    me.bm.free()
    # 馈源臂
    m.tube([tuple(rot(Vector((0, -R * 0.9, 0)))), tuple(rot(Vector((0, 0, 0.45))))], 0.015, 4, 'dark')
    m.cyl(*tuple(rot(Vector((0, 0, 0.45))))[:2], tuple(rot(Vector((0, 0, 0.45))))[2] - 0.04, tuple(rot(Vector((0, 0, 0.45))))[2] + 0.04, 0.05, 8, 'dark')
    return m.finish()


def stair_hut():
    """楼梯间小屋：3 × 2.5 × 2.8 m，正面一扇金属门 + 门上小檐"""
    m = M('stair_hut')
    m.box(-1.5, -1.25, 0, 1.5, 1.25, 2.8, 'stucco', scale=2.0)
    m.box(-1.6, -1.35, 2.8, 1.6, 1.35, 2.95, 'metal')            # 檐口压顶
    m.box(-0.5, -1.3, 0.0, 0.5, -1.25, 2.1, 'dark')              # 门（贴在墙面外 5cm）
    m.box(-0.7, -1.75, 2.2, 0.7, -1.25, 2.3, 'metal')            # 门上小檐
    m.box(0.9, -1.3, 1.6, 1.3, -1.25, 2.2, 'dark')               # 小窗
    return m.finish()


bpy.ops.wm.read_factory_settings(use_empty=True)
objs = [ac_unit(), water_tank(), vent(), dish(), stair_hut()]
meta = {'atlas': ATLAS, 'parts': {}}
for ob in objs:
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=str(OUT / f'{ob.name}.fbx'), use_selection=True, path_mode='STRIP',
                             axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_UNITS',
                             mesh_smooth_type='FACE', bake_space_transform=True)
    bb = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    meta['parts'][ob.name] = {**report[ob.name], 'size': [round(max(v.x for v in bb) - min(v.x for v in bb), 3),
                                                         round(max(v.y for v in bb) - min(v.y for v in bb), 3),
                                                         round(max(v.z for v in bb) - min(v.z for v in bb), 3)]}
(OUT / 'meta.json').write_text(json.dumps(meta, indent=2, ensure_ascii=False))
bad = {k: v for k, v in report.items() if v['open_edges'] or v['nonmanifold']}
print('报告', json.dumps(report, ensure_ascii=False), flush=True)
if bad: print('不合格', bad, flush=True)
