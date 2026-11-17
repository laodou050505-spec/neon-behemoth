"""《我是怪兽》 · 程序化楼模块（在 Blender 里运行）：每种楼 = 底座 + 标准层（2~3 种变体）+ 楼顶，真实尺寸（米）建模。
    Blender -b -P Tools/blender_buildings.py -- <楼型> <输出目录>      楼型：glass_tower | deco_hotel
- 结构是真几何：窗洞内凹、窗框、楼板外沿、阳台板 + 栏杆、檐口、线脚；玻璃是实心面（贴图 + 反光表现）。
- 每个模块一块网格、一个材质（整种楼一张图集），UV 落在图集里对应区域：墙 / 玻璃 / 窗框 / 楼板 / 屋顶 / 栏杆……
                图集区域写进 atlas.json，Tools/compose_atlas.py 按它拼贴图（污渍按窗位叠加）。
- 每个模块都过封闭检查（开放边 = 0、非流形边 = 0），不合格直接报错退出。
- 导出 FBX（轴转换烘进顶点：Unity 里 y 向上，正面朝 -Z → Unity +Z 背面……统一：模块正面朝 Unity -Z）+ 体检图。
单位：米（真实建筑尺寸）；Unity 里按城市比例统一缩放。"""
import bpy, bmesh, sys, json, math, pathlib
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
KIND, OUT = argv[0], pathlib.Path(argv[1])
OUT.mkdir(parents=True, exist_ok=True)

report = {}

# ───────── 图集区域（u0, v0, u1, v1，0~1）─────────
# 2048 图集：上半是立面（墙、玻璃、窗框），下半是小件（楼板边、屋顶、栏杆、金属、霓虹）
ATLAS = {
    'wall':     (0.00, 0.50, 0.50, 1.00),   # 墙面（贴图里画好一个开间的墙 + 污渍）
    'glass':    (0.50, 0.50, 1.00, 1.00),   # 玻璃（反光天空 + 室内暗影）
    'frame':    (0.00, 0.40, 0.25, 0.50),   # 窗框 / 竖梃（铝 / 白钢）
    'slab':     (0.25, 0.40, 0.50, 0.50),   # 楼板外沿 / 檐口 / 线脚
    'roof':     (0.50, 0.25, 0.75, 0.50),   # 屋面（防水层 / 碎石）
    'rail':     (0.75, 0.25, 1.00, 0.50),   # 栏杆玻璃 / 金属
    'base':     (0.00, 0.00, 0.50, 0.40),   # 底层石材 / 大堂立面
    'metal':    (0.50, 0.00, 0.75, 0.25),   # 深色金属（门框、雨棚骨架）
    'neon':     (0.75, 0.00, 1.00, 0.125),  # 霓虹 / 招牌（自发光）
    'awning':   (0.75, 0.125, 1.00, 0.25),  # 遮阳棚条纹布
}
# 每种区域在真实世界里一张贴图覆盖多少米（u 方向, v 方向）：墙 / 玻璃按“一个开间 × 一层”对齐
SPAN = {'awning': (2.0, 2.0), 'frame': (1.0, 1.0), 'slab': (4.0, 0.5), 'roof': (8.0, 8.0), 'rail': (3.0, 1.0), 'base': (8.0, 5.0), 'metal': (2.0, 2.0), 'neon': (1.0, 1.0)}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


exec(open(pathlib.Path(__file__).with_name('blender_mesh_checks.py'), encoding='utf-8').read())   # 共面 / 法线检查


LOD1 = {}


def make_lod1(bm, name):
    """远处用的简化版（LOD1）：删掉“细小的独立构件”（最薄一边 < 25cm 且体积 < 1.5m³：竖梃、窗台、线脚、栏杆、铆钉……），
    删完会悬空的（比如灯杆删了、灯头还在）就把支撑它的那根补回来；墙体 / 玻璃 / 楼板 / 女儿墙 / 大块设备保留"""
    b2 = bm.copy()
    kill = lod_kill_safe(b2, lambda d, vol: d[0] < 0.25 and vol < 1.5)
    bmesh.ops.delete(b2, geom=list(set(kill)), context='FACES')
    fl = floating_parts(b2)
    if fl[0]:
        print('  LOD1 悬空', name, fl, flush=True)
    me = bpy.data.meshes.new(name + '_lod1')
    b2.to_mesh(me); b2.free()
    ob = bpy.data.objects.new(name + '_lod1', me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


class Mesh:
    """收集四边面，按区域写 UV；最后合并重复顶点、检查封闭"""
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new('UVMap')
        # 第二套 UV（Unity 里是 uv2）：每扇窗一个 0~1 的随机号，着色器拿它 + 这一层的位置决定夜里亮不亮；
        # 不是玻璃的面写 2（霓虹这类永远亮，其余面本来就没有自发光）
        self.lit = self.bm.loops.layers.uv.new('Lit')
        # 焊接分组：同一组的顶点才合并（墙体一组、每个长方体各一组）。
        # 不同组的实体可以贴在一起，但不焊死，否则共面接触会产生非流形边。
        self.groups = {0: []}
        self.cur = 0
        self.next_group = 1

    def new_group(self):
        g = self.next_group; self.next_group += 1
        self.groups[g] = []
        return g

    def quad(self, pts, region, uv=None, lit=None):
        p = [Vector(x) for x in pts]
        # 零面积的面（窗洞贴着墙边时会出现）直接跳过
        if ((p[1] - p[0]).cross(p[2] - p[0]).length + (p[2] - p[0]).cross(p[3] - p[0]).length) < 1e-8:
            return None
        vs = [self.bm.verts.new(x) for x in pts]
        self.groups[self.cur].extend(vs)
        f = self.bm.faces.new(vs)
        if uv is None:
            uv = self.auto_uv(pts, region)
        c = sum(p, Vector()) / 4
        r = lit if lit is not None else ((math.sin(c.x * 12.9898 + c.y * 78.233 + c.z * 37.719) * 43758.5453) % 1.0 if region == 'glass' else 2.0)
        for loop, t in zip(f.loops, uv):
            loop[self.uv].uv = t
            loop[self.lit].uv = (r, 0.0)
        return f

    def auto_uv(self, pts, region):
        """按面的朝向投影到平面，用真实尺寸 / SPAN 换算成区域内坐标（超出部分夹在区域内，区域贴图本身可平铺）"""
        u0, v0, u1, v1 = ATLAS[region]
        p = [Vector(x) for x in pts]
        n = (p[1] - p[0]).cross(p[2] - p[0]).normalized()
        if abs(n.z) > 0.7:
            ax, ay = Vector((1, 0, 0)), Vector((0, 1, 0))
        else:
            ax = Vector((-n.y, n.x, 0)).normalized(); ay = Vector((0, 0, 1))
        sx, sy = SPAN.get(region, (1.0, 1.0))
        out = []
        for q in p:
            a, b = q.dot(ax) / sx, q.dot(ay) / sy
            a -= math.floor(min(x.dot(ax) / sx for x in p)); b -= math.floor(min(x.dot(ay) / sy for x in p))
            out.append((u0 + (u1 - u0) * min(max(a, 0), 1), v0 + (v1 - v0) * min(max(b, 0), 1)))
        return out

    def box(self, x0, y0, z0, x1, y1, z1, region, top=None, bottom=None, sides=None):
        """封闭长方体；各面可指定不同区域"""
        c = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
        self.hexa(c, region, top, bottom, sides)

    def hexa(self, c, region, top=None, bottom=None, sides=None):
        """任意六面体（斜遮阳棚、斜屋面用）：c = 底面 4 角 + 顶面 4 角，顺序同 box"""
        faces = [((0, 3, 2, 1), bottom or region), ((4, 5, 6, 7), top or region),
                        ((0, 1, 5, 4), sides or region), ((1, 2, 6, 5), sides or region),
                        ((2, 3, 7, 6), sides or region), ((3, 0, 4, 7), sides or region)]
        prev = self.cur
        self.cur = self.new_group()
        for idx, reg in faces:
            self.quad([c[i] for i in idx], reg)
        self.cur = prev

    def finish(self, max_faces=None):
        # 1. 各组分别焊接
        for g, vs in self.groups.items():
            vs = [v for v in vs if v.is_valid]
            if vs:
                bmesh.ops.remove_doubles(self.bm, verts=vs, dist=1e-5)
        # 2. 墙体上下端按实际轮廓补面（轮廓上有窗洞分割点，四边形盖子对不上），再三角化
        open_e = [e for e in self.bm.edges if len(e.link_faces) == 1]
        if open_e:
            filled = bmesh.ops.holes_fill(self.bm, edges=open_e, sides=0)['faces']
            if filled:
                tri = bmesh.ops.triangulate(self.bm, faces=filled, quad_method='BEAUTY', ngon_method='BEAUTY')['faces']
                for f in tri:
                    uv = self.auto_uv([tuple(l.vert.co) for l in f.loops], 'roof')
                    for l, t in zip(f.loops, uv):
                        l[self.uv].uv = t
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        # 共面重叠（Unity 里深度冲突闪烁）：只处理看得见的（前方是空气）；把较小的那块沿法线往外推 4mm（顶点一起动，网格仍封闭）
        _n, _h, pairs = coplanar_overlaps(self.bm)
        moved = set()
        for fa, fb in visible_pairs(self.bm, pairs):
            f = fa if fa.calc_area() <= fb.calc_area() else fb
            nrm = f.normal.normalized()
            for v in f.verts:
                if v.index not in moved:
                    v.co += nrm * 0.004; moved.add(v.index)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        _n, _h, pairs = coplanar_overlaps(self.bm)
        vis = visible_pairs(self.bm, pairs)
        self.coplanar = (len(vis), [tuple(round(c, 2) for c in fa.calc_center_median()) for fa, fb in vis[:8]])
        self.flipped = flipped_faces(self.bm)
        self.floating = floating_parts(self.bm)
        if self.floating[0]: print('  悬空', self.name, self.floating, flush=True)
        if self.coplanar[0] and '--detail' in sys.argv:
            for fa, fb in visible_pairs(self.bm, coplanar_overlaps(self.bm)[2])[:6]:
                bb = lambda f: [tuple(round(min(v.co[i] for v in f.verts), 2) for i in range(3)), tuple(round(max(v.co[i] for v in f.verts), 2) for i in range(3))]
                def reg(f):
                    u = sum(l[self.uv].uv[0] for l in f.loops) / len(f.loops); v = sum(l[self.uv].uv[1] for l in f.loops) / len(f.loops)
                    return next((k for k, (u0, v0, u1, v1) in ATLAS.items() if u0 - 1e-4 <= u <= u1 + 1e-4 and v0 - 1e-4 <= v <= v1 + 1e-4), '?')
                print('    对', tuple(round(c, 2) for c in fa.normal), reg(fa), bb(fa), '|', reg(fb), bb(fb), flush=True)
        me = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(me)
        LOD1[self.name] = make_lod1(self.bm, self.name)
        nb = sum(1 for e in self.bm.edges if len(e.link_faces) == 1)
        nm = sum(1 for e in self.bm.edges if len(e.link_faces) > 2)
        for e in [e for e in self.bm.edges if len(e.link_faces) != 2][:6]:
            print('  问题边', self.name, [tuple(round(c, 2) for c in v.co) for v in e.verts], len(e.link_faces), flush=True)
        self.bm.free()
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        tris = sum(len(p.vertices) - 2 for p in me.polygons)
        report[self.name] = {'tris': tris, 'open_edges': nb, 'nonmanifold': nm, 'coplanar': self.coplanar[0], 'flipped': self.flipped[0], 'floating': self.floating[0]}
        if self.flipped[0]:
            print('  法线反了', self.name, self.flipped[0], self.flipped[1], flush=True)
        if self.coplanar[0]:
            print('  共面重叠', self.name, self.coplanar[0], self.coplanar[1], flush=True)
        if max_faces and tris > max_faces:
            raise SystemExit(f'{self.name} 面数超标 {tris} > {max_faces}')
        return ob


# 墙体每个开间：实心墙 + 内凹窗洞（窗洞四壁 = 墙区域，窗玻璃 = 玻璃区域，凹进 depth），
# 用“墙面切成 3×3 格、中间格内凹”的方式保持封闭
def clamp_win(win, h):
    """窗洞不贴楼板：上下各留 2cm（否则窗台面和楼板面重合会闪）；窗太矮就当实墙"""
    if win is None:
        return None
    wl, wb, ww, wh = win
    b0, b1 = max(wb, 0.02), min(wb + wh, h - 0.02)
    if b1 - b0 < 0.05 or ww < 0.05:
        return None
    return (wl, b0, ww, b1 - b0)


def facade_bay(m, origin, right, up, outward, w, h, win, depth, region_wall='wall', bay_uv=None, levels=(), inner='glass'):
    """竖直墙面上的一个开间：墙面 + 窗洞四壁 + 内凹玻璃（或卷帘门等 inner 区域）。
    levels：整圈墙统一的分行高度——每个开间的竖边都在这些高度上有顶点，相邻开间、转角两侧才能严丝合缝地焊上
    （否则会出现 T 形接缝，补洞时冒出歪面、法线反掉）。win 为 None 时是实墙。"""
    win = clamp_win(win, h)
    P = lambda x, y, d=0.0: tuple(origin + right * x + up * y - outward * d)
    u0, v0, u1, v1 = ATLAS[region_wall]
    def wuv(x, y): return (u0 + (u1 - u0) * x / w, v0 + (v1 - v0) * y / h)
    ys = sorted({0.0, h} | {round(l, 5) for l in levels if 1e-4 < l < h - 1e-4} | ({round(win[1], 5), round(win[1] + win[3], 5)} if win else set()))
    if win is None:
        for ya, yb in zip(ys[:-1], ys[1:]):
            m.quad([P(0, ya), P(w, ya), P(w, yb), P(0, yb)], region_wall, [wuv(0, ya), wuv(w, ya), wuv(w, yb), wuv(0, yb)])
        return
    wl, wb, ww, wh = win
    top = wb + wh
    xs = [0, wl, wl + ww, w]
    rows = list(zip(ys[:-1], ys[1:]))
    inwin = lambda ya, yb: ya >= wb - 1e-6 and yb <= top + 1e-6
    for ya, yb in rows:
        for i in range(3):
            if i == 1 and inwin(ya, yb):
                continue
            q = [P(xs[i], ya), P(xs[i + 1], ya), P(xs[i + 1], yb), P(xs[i], yb)]
            m.quad(q, region_wall, [wuv(xs[i], ya), wuv(xs[i + 1], ya), wuv(xs[i + 1], yb), wuv(xs[i], yb)])
    # 窗洞四壁：窗台（a→b）、右侧（b→c，逐行往上）、窗楣（c→d）、左侧（d→a，逐行往下）
    xl, xr = wl, wl + ww
    m.quad([P(xl, wb), P(xl, wb, depth), P(xr, wb, depth), P(xr, wb)], 'frame')
    m.quad([P(xr, top), P(xr, top, depth), P(xl, top, depth), P(xl, top)], 'frame')
    for ya, yb in rows:
        if not inwin(ya, yb):
            continue
        m.quad([P(xr, ya), P(xr, ya, depth), P(xr, yb, depth), P(xr, yb)], 'frame')
        m.quad([P(xl, yb), P(xl, yb, depth), P(xl, ya, depth), P(xl, ya)], 'frame')
    # 玻璃（内凹）：8 种窗格里按位置挑一格；按行切开时 v 坐标插值，整扇窗同一个夜里亮灯随机号
    c0 = Vector(P(xl + ww / 2, wb + wh / 2, depth))
    r = (math.sin(c0.x * 12.9898 + c0.y * 78.233 + c0.z * 37.719) * 43758.5453) % 1.0
    if inner == 'glass':
        G0, G1, G2, G3 = ATLAS['glass']
        cell = int(abs(origin.x * 7.31 + origin.y * 3.17 + w * 13.7) * 10) % 8
        cu, cv = cell % 4, cell // 4
        cw, ch = (G2 - G0) / 4, (G3 - G1) / 2
        ins = 0.04
        g0, g2 = G0 + cw * (cu + ins), G0 + cw * (cu + 1 - ins)
        g1, g3 = G1 + ch * (cv + ins), G1 + ch * (cv + 1 - ins)
        gv = lambda y: g1 + (g3 - g1) * (y - wb) / wh
    for ya, yb in rows:
        if not inwin(ya, yb):
            continue
        q = [P(xl, ya, depth), P(xl, yb, depth), P(xr, yb, depth), P(xr, ya, depth)]
        if inner == 'glass':
            m.quad(q, 'glass', [(g0, gv(ya)), (g0, gv(yb)), (g2, gv(yb)), (g2, gv(ya))], lit=r)
        else:
            m.quad(q, inner)


def corner_safe(win, i, n, bw, depth):
    """转角开间：窗洞离转角至少 (凹进深度 + 5cm)，否则相邻两面墙的窗洞凹槽会在转角处互相切穿（法线反、出洞）"""
    if win is None:
        return None
    wl, wb, ww, wh = win
    m = depth + 0.05
    x0, x1 = wl, wl + ww
    if i == 0: x0 = max(x0, m)
    if i == n - 1: x1 = min(x1, bw - m)
    if x1 - x0 < 0.1:
        return None
    return (x0, wb, x1 - x0, wh)


def ring_levels(winfs, h):
    """整圈墙所有窗洞的上下沿（夹过 2cm 之后的），做为每个开间统一的分行高度"""
    out = set()
    for w in winfs:
        w = clamp_win(w, h)
        if w:
            out.add(round(w[1], 5)); out.add(round(w[1] + w[3], 5))
    return sorted(out)


def ring_wall(m, W, D, z0, h, bays_x, bays_y, win, depth):
    """四面墙，每面若干开间（逆时针），墙顶 / 墙底不封（由楼板封）；整圈统一分行，转角处也能焊上"""
    corners = [Vector((-W / 2, -D / 2, z0)), Vector((W / 2, -D / 2, z0)), Vector((W / 2, D / 2, z0)), Vector((-W / 2, D / 2, z0))]
    sides = []
    for k in range(4):
        p0, p1 = corners[k], corners[(k + 1) % 4]
        n = bays_x if k % 2 == 0 else bays_y
        bw = (p1 - p0).length / n
        sides.append((p0, (p1 - p0).normalized(), n, bw))
    lv = ring_levels([corner_safe(win(bw, h), i, n, bw, depth) for (_, _, n, bw) in sides for i in range(n)], h)
    for p0, right, n, bw in sides:
        outward = Vector((right.y, -right.x, 0))
        for i in range(n):
            facade_bay(m, p0 + right * (i * bw), right, Vector((0, 0, 1)), outward, bw, h, corner_safe(win(bw, h), i, n, bw, depth), depth, levels=lv)


def _sides(W, D, z0):
    """四面墙的 (起点, 沿墙方向, 外法线, 长度)，逆时针"""
    cs = [Vector((-W / 2, -D / 2, z0)), Vector((W / 2, -D / 2, z0)), Vector((W / 2, D / 2, z0)), Vector((-W / 2, D / 2, z0))]
    for k in range(4):
        p0, p1 = cs[k], cs[(k + 1) % 4]
        right = (p1 - p0).normalized()
        yield k, p0, right, Vector((right.y, -right.x, 0)), (p1 - p0).length


def _slab_on_wall(m, base, right, out, x0, y0, x1, y1, d0, d1, region):
    """贴在墙面 / 玻璃面上的小方条：墙内坐标 x（沿墙）、y（高度）、d（向外）"""
    q = lambda x, y, d: tuple(base + right * x + Vector((0, 0, y)) + out * d)
    m.hexa([q(x0, y0, d0), q(x1, y0, d0), q(x1, y0, d1), q(x0, y0, d1),
            q(x0, y1, d0), q(x1, y1, d0), q(x1, y1, d1), q(x0, y1, d1)], region)


def mullions(m, W, D, z0, h, bays, depth, cols=3, rows=2, bar=0.07, proud=0.06, win=None, rows_at=None):
    """幕墙格栅：每个开间的玻璃再分 cols 列 × rows 行，竖梃 / 横档是凸出玻璃面的方条；横档在竖梃处断开，互不穿插。
    win：开间里的玻璃范围（同 ring_wall 的窗函数），不给就是幕墙默认；rows_at：横档所在高度（玻璃高度的比例）"""
    for k, p0, right, out, L in _sides(W, D, z0):
        bw = L / bays
        for i in range(bays):
            base = p0 + right * (i * bw) - out * depth          # 玻璃面
            if win:
                wl, wb, ww, wh = win(bw, h); gx0, gx1, gy0, gy1 = wl, wl + ww, wb, wb + wh
            else:
                gx0, gx1, gy0, gy1 = 0.12, bw - 0.12, 0.25, h - 0.25
            xs = [gx0 + (gx1 - gx0) * c / cols for c in range(cols + 1)]
            for x in xs[1:-1]:
                _slab_on_wall(m, base, right, out, x - bar / 2, gy0, x + bar / 2, gy1, 0, proud, 'frame')
            ys = rows_at if rows_at is not None else [r / rows for r in range(1, rows)]
            for f in ys:
                y = gy0 + (gy1 - gy0) * f
                for a, b in zip(xs[:-1], xs[1:]):
                    a2 = a + (bar / 2 if a > gx0 else 0); b2 = b - (bar / 2 if b < gx1 else 0)
                    _slab_on_wall(m, base, right, out, a2, y - bar / 2, b2, y + bar / 2, 0, proud, 'frame')


def frames(m, W, D, z0, h, bays_x, bays_y, win, wdt, proud, full=True, lintel=True):
    """窗套：每个窗洞外一圈宽 wdt、凸出 proud 的方框（窗台 / 窗楣通长，左右两条夹在中间，不重叠）"""
    for k, p0, right, out, L in _sides(W, D, z0):
        n = bays_x if k % 2 == 0 else bays_y
        bw = L / n
        for i in range(n):
            base = p0 + right * (i * bw)
            wl, wb, ww, wh = win(bw, h)
            x0, x1, y0, y1 = wl - wdt, wl + ww + wdt, wb - wdt, wb + wh + wdt
            if x0 < 0.02 or x1 > bw - 0.02 or y0 < 0.02 or y1 > h - 0.02:
                continue
            # full：四边完整窗套；否则省面数——窗台每面都有，窗楣只在正面（k=0，玩家看得最多的一面）
            bars = [(x0, y0, x1, wb)]
            if full or (lintel and k == 0):
                bars.append((x0, wb + wh, x1, y1))
            if full:
                bars += [(x0, wb, wl, wb + wh), (wl + ww, wb, x1, wb + wh)]
            for a in bars:
                _slab_on_wall(m, base, right, out, *a, 0, proud, 'frame')


def prism(m, ring, z0, z1, region):
    """按逆时针多边形挤出封闭棱柱：侧面四边形 + 顶底扇形三角"""
    prev = m.cur; m.cur = m.new_group()
    n = len(ring)
    for i in range(n):
        (x0, y0), (x1, y1) = ring[i], ring[(i + 1) % n]
        m.quad([(x0, y0, z0), (x1, y1, z0), (x1, y1, z1), (x0, y0, z1)], region)
    for i in range(1, n - 1):
        for zz, order in ((z1, (0, i, i + 1)), (z0, (0, i + 1, i))):
            pts = [(ring[j][0], ring[j][1], zz) for j in order]
            vs = [m.bm.verts.new(p) for p in pts]
            m.groups[m.cur].extend(vs)
            f = m.bm.faces.new(vs)
            for l, t in zip(f.loops, m.auto_uv(pts + [pts[-1]], region)[:3]):
                l[m.uv].uv = t
    m.cur = prev


def slab(m, W, D, z, t, over, chamfer=0.0):
    """楼板：比墙面伸出 over，厚 t；chamfer > 0 时四角切 45° 倒角（八边形棱柱）"""
    if chamfer <= 0:
        m.box(-W / 2 - over, -D / 2 - over, z, W / 2 + over, D / 2 + over, z + t, 'slab', top='slab', bottom='slab')
        return
    X, Y, c = W / 2 + over, D / 2 + over, chamfer
    prism(m, [(-X + c, -Y), (X - c, -Y), (X, -Y + c), (X, Y - c), (X - c, Y), (-X + c, Y), (-X, Y - c), (-X, -Y + c)], z, z + t, 'slab')


def cap(m, W, D, z):
    """墙体顶 / 底的封口面（把 ring_wall 的上下边缘接成封闭体）"""
    m.quad([(-W / 2, -D / 2, z), (W / 2, -D / 2, z), (W / 2, D / 2, z), (-W / 2, D / 2, z)], 'roof')


# ═════════════ 玻璃摩天楼：28×28m，层高 3.6m ═════════════
def glass_tower():
    W = D = 28.0; H = 3.6; T = 0.35; OVER = 0.6
    # 幕墙：下部 0.9m 是窗下墙（实墙带，灰白石材），上面是玻璃；开间之间留 0.5m 宽的结构柱
    win = lambda bw, h: (0.25, 0.9, bw - 0.5, h - 1.15)
    mods = {}

    # 标准层 A：普通幕墙层（6 开间 × 4 面）
    for var, balcony in (('storey_a', False), ('storey_b', True)):
        m = Mesh(var)
        ring_wall(m, W, D, T, H - T, 4, 4, win, 0.22)
        # 竖梃 3 根 + 上亮子横档（玻璃高度 78% 处）：分隔出开启扇
        mullions(m, W, D, T, H - T, 4, 0.22, cols=2, rows=1, win=win, rows_at=[0.78], bar=0.07, proud=0.05)
        # 每开间之间的结构柱（凸出墙面 0.18m 的竖向线条，从楼板到楼板）+ 四个转角柱
        for k, p0, right, out, L in _sides(W, D, T):
            bw = L / 4
            for i in range(5):
                x = i * bw
                x0, x1 = (x, x + 0.35) if i == 0 else ((x - 0.35, x) if i == 4 else (x - 0.2, x + 0.2))
                _slab_on_wall(m, p0, right, out, x0, 0, x1, H - T, 0, 0.18, 'slab')
        # 楼板（层底，伸出墙面，转角倒角）
        slab(m, W, D, 0, T, OVER, chamfer=0.9)
        if balcony:
            # 转角阳台：正反两面的两端各一个，阳台板接在楼板外沿上（不穿插），前 + 两侧三面玻璃栏板
            RH, RT, BD = 1.05, 0.05, 1.6
            for sx in (-1, 1):
                for sy in (-1, 1):
                    xa, xb = sorted((sx * (W / 2 - 6.0), sx * (W / 2 - 0.5)))
                    def Y(a, b): return sorted((sy * a, sy * b))
                    y0, y1 = Y(D / 2 + OVER, D / 2 + BD)
                    m.box(xa, y0, 0, xb, y1, T, 'slab')                         # 阳台板（和楼板同厚，接在外沿上）
                    y0, y1 = Y(D / 2 + BD - RT, D / 2 + BD)
                    m.box(xa, y0, T, xb, y1, T + RH, 'rail')                     # 前栏板
                    y0, y1 = Y(D / 2, D / 2 + BD - RT)
                    m.box(xa, y0, T, xa + RT, y1, T + RH, 'rail')                # 两侧栏板（从墙面到前栏板）
                    m.box(xb - RT, y0, T, xb, y1, T + RH, 'rail')
        mods[var] = m.finish(1600)

    # 底座：两层通高大堂（6m）+ 石材柱 + 雨棚
    m = Mesh('base')
    HB = 6.0
    # 石材实体（四面），大堂玻璃内凹
    ring_wall(m, W, D, 0, HB, 4, 4, lambda bw, h: (0.9, 0.0, bw - 1.8, h - 1.2), 1.2)
    # 大堂玻璃上的竖梃 + 中间一道横档（门头高度）
    mullions(m, W, D, 0, HB, 4, 1.2, cols=3, rows=1, win=lambda bw, h: (0.9, 0.0, bw - 1.8, h - 1.2), rows_at=[0.55], bar=0.12, proud=0.08)
    # 石材分缝：底座上沿一圈凸出的压顶线脚 + 离地 0.6m 的勒脚
    for k, p0, right, out, L in _sides(W, D, 0):
        _slab_on_wall(m, p0, right, out, 0, HB - 0.5, L, HB, 0, 0.25, 'base')
        _slab_on_wall(m, p0, right, out, 0, 0, L, 0.6, 0, 0.12, 'base')
    # 正门雨棚（正面 = -Y）：钢板 + 两根圆柱 + 雨棚前沿的发光招牌条
    m.box(-6, -D / 2 - 3.2, 4.2, 6, -D / 2, 4.5, 'metal')
    m.box(-5.8, -D / 2 - 3.25, 4.25, 5.8, -D / 2 - 3.15, 4.45, 'neon')
    for x in (-5.6, 5.6):
        m.cyl_z(x, -D / 2 - 2.95, 0, 4.2, 0.18, 10, 'metal') if hasattr(m, 'cyl_z') else m.box(x - 0.15, -D / 2 - 3.1, 0, x + 0.15, -D / 2 - 2.8, 4.2, 'metal')
    # 入口台阶两级
    m.box(-4.5, -D / 2 - 1.6, 0, 4.5, -D / 2, 0.15, 'base')
    m.box(-4.0, -D / 2 - 0.9, 0.15, 4.0, -D / 2, 0.3, 'base')
    mods['base'] = m.finish(2200)

    # 楼顶：设备层（退台）+ 女儿墙 + 霓虹边
    m = Mesh('crown')
    slab(m, W, D, 0, T, OVER, chamfer=0.9)
    # 女儿墙（实心环：用四块长方体，互不重叠）
    PH, PT = 1.2, 0.35
    m.box(-W / 2, -D / 2, T, W / 2, -D / 2 + PT, T + PH, 'slab')
    m.box(-W / 2, D / 2 - PT, T, W / 2, D / 2, T + PH, 'slab')
    m.box(-W / 2, -D / 2 + PT, T, -W / 2 + PT, D / 2 - PT, T + PH, 'slab')
    m.box(W / 2 - PT, -D / 2 + PT, T, W / 2, D / 2 - PT, T + PH, 'slab')
    # 屋面
    m.box(-W / 2 + PT, -D / 2 + PT, T, W / 2 - PT, D / 2 - PT, T + 0.05, 'roof')
    # 女儿墙顶的压顶（比墙宽、略伸出）
    for x0, y0, x1, y1 in ((-W / 2 - 0.1, -D / 2 - 0.1, W / 2 + 0.1, -D / 2 + PT + 0.1), (-W / 2 - 0.1, D / 2 - PT - 0.1, W / 2 + 0.1, D / 2 + 0.1)):
        m.box(x0, y0, T + PH, x1, y1, T + PH + 0.12, 'base')
    for x0, x1 in ((-W / 2 - 0.1, -W / 2 + PT + 0.1), (W / 2 - PT - 0.1, W / 2 + 0.1)):
        m.box(x0, -D / 2 + PT + 0.1, T + PH, x1, D / 2 - PT - 0.1, T + PH + 0.12, 'base')
    # 设备层：退进 5m，墙面是金属百叶（每面 9 条凸出的横向百叶板），四角 0.6m 的方柱
    E0, E1, EI = T + 0.05, T + 4.8, 5.0
    ex, ey = W / 2 - EI, D / 2 - EI
    m.box(-ex, -ey, E0, ex, ey, E1, 'metal', top='roof')
    for k in range(9):
        z = E0 + 0.5 + k * 0.45
        for y0, y1 in ((-ey - 0.12, -ey), (ey, ey + 0.12)):
            m.box(-ex + 0.6, y0, z, ex - 0.6, y1, z + 0.12, 'frame')
        for x0, x1 in ((-ex - 0.12, -ex), (ex, ex + 0.12)):
            m.box(x0, -ey + 0.6, z, x1, ey - 0.6, z + 0.12, 'frame')
    for sx in (-1, 1):
        for sy in (-1, 1):
            m.box(sx * ex - 0.35 if sx > 0 else -ex - 0.25, sy * ey - 0.35 if sy > 0 else -ey - 0.25,
                                E0, sx * ex + 0.25 if sx > 0 else -ex + 0.35, sy * ey + 0.25 if sy > 0 else -ey + 0.35, E1 + 0.1, 'base')
    # 设备层顶：一圈霓虹边 + 压顶
    m.box(-ex - 0.15, -ey - 0.15, E1, ex + 0.15, ey + 0.15, E1 + 0.25, 'neon', top='roof')
    # 电梯机房（带检修门）+ 天线杆（三节，越往上越细）+ 航空障碍灯
    m.box(-W / 2 + 7, 2, E1 + 0.25, -W / 2 + 13, 8, E1 + 3.4, 'base', top='roof')
    m.box(-W / 2 + 9, 1.9, E1 + 0.25, -W / 2 + 10.4, 2.0, E1 + 2.4, 'frame')
    for z0, z1, r in ((E1 + 0.25, E1 + 4.5, 0.3), (E1 + 4.5, E1 + 8.0, 0.2), (E1 + 8.0, E1 + 11.0, 0.12)):
        m.box(3.25 - r, -2.75 - r, z0, 3.25 + r, -2.75 + r, z1, 'metal')
    m.box(3.1, -2.9, E1 + 11.0, 3.4, -2.6, E1 + 11.4, 'neon')
    # 女儿墙角上的航空障碍灯
    for sx in (-1, 1):
        for sy in (-1, 1):
            m.box(sx * (W / 2 - 0.3) - 0.15, sy * (D / 2 - 0.3) - 0.15, T + PH + 0.12, sx * (W / 2 - 0.3) + 0.15, sy * (D / 2 - 0.3) + 0.15, T + PH + 0.45, 'neon')
    mods['crown'] = m.finish(1600)
    return mods


# ═════════════ 海滨酒店（装饰艺术风）：22×16m，层高 3.3m ═════════════
def deco_hotel():
    W, D = 22.0, 16.0; H = 3.3; T = 0.25
    win = lambda bw, h: (bw * 0.18, 0.75, bw * 0.64, 1.75)
    mods = {}
    win_b = lambda bw, h: (bw * 0.3, 0.75, bw * 0.4, 1.75)
    for var, w_fn, nx in (('storey_a', win, 6), ('storey_b', win_b, 8)):
        eyebrow = True
        m = Mesh(var)
        ring_wall(m, W, D, 0, H, nx, 4, w_fn, 0.22)
        if eyebrow:
            # 窗眉：每面一条伸出的薄檐（装饰艺术风标志），正面中间留出竖向招牌位
            E0, E1, EO = H - 0.75, H - 0.62, 0.55
            for y0, y1, x0, x1 in ((-D / 2 - EO, -D / 2, -W / 2, -0.6), (-D / 2 - EO, -D / 2, 0.6, W / 2),
                                    (D / 2, D / 2 + EO, -W / 2, W / 2)):
                m.box(x0, y0, E0, x1, y1, E1, 'slab')
            for x0, x1 in ((-W / 2 - EO, -W / 2), (W / 2, W / 2 + EO)):
                m.box(x0, -D / 2 - EO, E0, x1, D / 2 + EO, E1, 'slab')            # 两端伸到转角，和正面窗眉接上
        # 窗套：每个窗洞外一圈凸出 0.08 的白框
        frames(m, W, D, 0, H, nx, 4, w_fn, 0.11, 0.08, full=False, lintel=(var == 'storey_a'))
        # 正面中间竖向招牌鳍（每层一段，堆起来连成整条）：鳍体 + 两侧凹槽线 + 霓虹管
        m.box(-0.6, -D / 2 - 1.4, 0, 0.6, -D / 2, H, 'slab')
        for x in (-0.75, 0.6):
            m.box(x, -D / 2 - 1.3, 0, x + 0.15, -D / 2 - 1.15, H, 'neon')
        m.box(-0.08, -D / 2 - 1.5, 0, 0.08, -D / 2 - 1.4, H, 'frame')        # 鳍前沿的白色立线
        # 四个转角：两道凸出的竖向装饰条（从楼板到楼板，层层相接成通高线条）
        for sx in (-1, 1):
            for sy in (-1, 1):
                cx, cy = sx * W / 2, sy * D / 2
                xa, xb = sorted((cx - sx * 0.45, cx - sx * 0.2)); ya, yb = sorted((cy, cy + sy * 0.12))
                m.box(xa, ya, 0, xb, yb, H, 'frame')                 # 贴在正 / 背面上、凸出 0.12
                xa, xb = sorted((cx, cx + sx * 0.12)); ya, yb = sorted((cy - sy * 0.45, cy - sy * 0.2))
                m.box(xa, ya, 0, xb, yb, H, 'frame')                 # 贴在侧面上、凸出 0.12
        # 窗下的装饰横线（每层一条，凸出 0.06，连成水平线脚）
        for k, p0, right, out, L in _sides(W, D, 0):
            _slab_on_wall(m, p0, right, out, 0.5, 0.45, L - 0.5, 0.55, 0, 0.06, 'frame')
        mods[var] = m.finish(1600)

    # 底座：带遮阳棚的露台 + 入口
    m = Mesh('base')
    HB = 4.2
    ring_wall(m, W, D, 0, HB, 5, 4, lambda bw, h: (0.4, 0.0, bw - 0.8, h - 0.9), 0.5)
    m.box(-W / 2 - 0.3, -D / 2 - 0.4, HB - 0.35, W / 2 + 0.3, D / 2 + 0.4, HB, 'slab')
    # 两段遮阳棚（斜面，用薄长方体）
    for x0, x1 in ((-W / 2 + 0.8, -2.5), (2.5, W / 2 - 0.8)):
        # 靠墙高、外沿低的斜棚，厚 0.08
        y0, y1, zh, zl, t = -D / 2, -D / 2 - 2.2, 3.5, 2.75, 0.08
        m.hexa([(x0, y1, zl), (x1, y1, zl), (x1, y0, zh), (x0, y0, zh),
                (x0, y1, zl + t), (x1, y1, zl + t), (x1, y0, zh + t), (x0, y0, zh + t)], 'awning')
    # 入口台阶两级 + 鳍底座
    m.box(-2.4, -D / 2 - 1.4, 0, 2.4, -D / 2, 0.15, 'slab')
    m.box(-2.0, -D / 2 - 0.8, 0.15, 2.0, -D / 2, 0.3, 'slab')
    m.box(-0.6, -D / 2 - 1.4, 0.3, 0.6, -D / 2, HB, 'slab')
    # 店面之间的方柱（凸出墙面 0.25）+ 柱头
    for k, p0, right, out, L in _sides(W, D, 0):
        n = 5 if k % 2 == 0 else 4
        bw = L / n
        for i in range(n + 1):
            x = i * bw
            x0, x1 = (x, x + 0.4) if i == 0 else ((x - 0.4, x) if i == n else (x - 0.25, x + 0.25))
            _slab_on_wall(m, p0, right, out, x0, 0, x1, HB - 0.35, 0, 0.25, 'frame')
            _slab_on_wall(m, p0, right, out, x0 - 0.08, HB - 0.65, x1 + 0.08, HB - 0.35, 0, 0.33, 'frame')
        # 压顶下的齿形装饰：每 0.8m 一个凸出小方块
        for j in range(int(L / 0.8)):
            x = 0.4 + j * 0.8
            if x + 0.3 > L - 0.3: break
            _slab_on_wall(m, p0, right, out, x, HB - 0.55, x + 0.3, HB - 0.33, 0.1, 0.26, 'frame')   # 挂在压顶板底下（压顶伸出 0.3）
    mods['base'] = m.finish(2400)

    # 楼顶：阶梯式女儿墙（正面中间三级升起）
    m = Mesh('crown')
    PH, PT = 1.0, 0.3
    m.box(-W / 2, -D / 2, 0, W / 2, -D / 2 + PT, PH, 'slab')
    m.box(-W / 2, D / 2 - PT, 0, W / 2, D / 2, PH, 'slab')
    m.box(-W / 2, -D / 2 + PT, 0, -W / 2 + PT, D / 2 - PT, PH, 'slab')
    m.box(W / 2 - PT, -D / 2 + PT, 0, W / 2, D / 2 - PT, PH, 'slab')
    m.box(-W / 2 + PT, -D / 2 + PT, 0, W / 2 - PT, D / 2 - PT, 0.05, 'roof')
    # 女儿墙：压顶 + 外侧一道装饰带（凸出、白色）
    for x0, y0, x1, y1 in ((-W / 2 - 0.1, -D / 2 - 0.1, W / 2 + 0.1, -D / 2 + PT + 0.1), (-W / 2 - 0.1, D / 2 - PT - 0.1, W / 2 + 0.1, D / 2 + 0.1)):
        m.box(x0, y0, PH, x1, y1, PH + 0.12, 'frame')
    for x0, x1 in ((-W / 2 - 0.1, -W / 2 + PT + 0.1), (W / 2 - PT - 0.1, W / 2 + 0.1)):
        m.box(x0, -D / 2 + PT + 0.1, PH, x1, D / 2 - PT - 0.1, PH + 0.12, 'frame')
    for k, p0, right, out, L in _sides(W, D, 0):
        _slab_on_wall(m, p0, right, out, 0, 0.3, L, 0.5, 0, 0.08, 'frame')
    # 正面中间的阶梯塔（招牌鳍顶端）：三级，每级顶上一圈白色线脚，正面两道竖向凹槽（用凸出的白条夹出来）
    steps = ((1.7, 0.0, 2.6), (1.2, 2.6, 4.8), (0.75, 4.8, 7.2))
    for k, (hw, z0, z1) in enumerate(steps):
        yf, yb = -D / 2 - 1.4, -D / 2 + 2.2 - k * 0.6
        m.box(-hw, yf, z0, hw, yb, z1, 'wall', top='slab')
        m.box(-hw - 0.12, yf - 0.12, z1 - 0.2, hw + 0.12, yb + 0.12, z1, 'frame')          # 线脚
        for x in (-hw * 0.5, hw * 0.5):
            m.box(x - 0.1, yf - 0.08, z0 + 0.2, x + 0.1, yf, z1 - 0.3, 'frame')            # 竖向凸条
    for x in (-0.75, 0.6):
        m.box(x, -D / 2 - 1.3 - 0.12, 0, x + 0.15, -D / 2 - 1.15 - 0.12, 6.6, 'neon')
    m.box(-0.12, -D / 2 - 1.2, 7.2, 0.12, -D / 2 - 0.96, 9.0, 'frame')                     # 顶上旗杆
    # 屋顶楼梯间：墙 + 檐口 + 门 + 小窗
    m.box(4, 1, 0.05, 8, 5, 3.0, 'wall', top='roof')
    m.box(3.85, 0.85, 3.0, 8.15, 5.15, 3.18, 'frame')
    m.box(5.3, 0.95, 0.05, 6.5, 1.0, 2.2, 'frame')
    m.box(6.9, 0.95, 1.6, 7.5, 1.0, 2.3, 'glass') if False else None
    mods['crown'] = m.finish(1600)
    return mods


exec(open(pathlib.Path(__file__).with_name('blender_buildings_more.py'), encoding='utf-8').read())   # 其余 7 种楼
reset()
mods = {'glass_tower': glass_tower, 'deco_hotel': deco_hotel, **MORE_KINDS}[KIND]()

# 导出：每个模块一个 FBX；体检图（所有模块并排 + 线框）
# wall：墙体占地（米，不含楼板外沿 / 雨棚 / 台阶），Unity 按它把整栋楼缩放到地块大小
meta = {'kind': KIND, 'atlas': ATLAS, 'parts': {}, 'wall': {'glass_tower': [28.0, 28.0], 'deco_hotel': [22.0, 16.0], **MORE_WALL}[KIND],
        'mids': MORE_MIDS.get(KIND, {'offset': 2, 'min': 1})}
for name, ob in list(mods.items()) + [(n + '_lod1', o) for n, o in LOD1.items()]:
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=str(OUT / f'{name}.fbx'), use_selection=True, path_mode='STRIP',
                                    axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_UNITS',
                                    mesh_smooth_type='FACE', bake_space_transform=True)
    bb = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    meta['parts'][name] = {'height': max(v.z for v in bb) - min(v.z for v in bb),
                                                'size': [max(v.x for v in bb) - min(v.x for v in bb), max(v.y for v in bb) - min(v.y for v in bb)],
                                                **report.get(name, {})}
bad = {k: v for k, v in report.items() if v['open_edges'] or v['nonmanifold'] or v.get('coplanar') or v.get('flipped') or v.get('floating')}   # 破面 / 闪烁 / 法线反都算不合格
meta['ok'] = not bad
(OUT / 'meta.json').write_text(json.dumps(meta, indent=2, ensure_ascii=False))
print('报告', json.dumps(report, ensure_ascii=False), flush=True)
if bad:
    print('不合格（开放边 / 非流形边 / 共面重叠会闪 / 法线反）', bad, flush=True)
