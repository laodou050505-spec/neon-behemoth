"""《霓虹巨兽》· 巨兽之手 v3（11-10 用户：手做工粗糙、很丑、没有怪兽的力量感 → 重做成一只完整的、带骨骼的怪兽手）。
    Blender -b -P Tools/blender_hand.py -- <输出目录>
绑定姿势 = HandPoseLib 的张开手（ArtSource/Hand/bind_right.json，Unity 坐标：腕在原点、手背 +Y、手指 +Z、右手拇指在 −X）。
骨骼 26 根，和 PICO / OpenXR 关节一一对应：J{k} 的头 = 关节 k，尾 = 下一节关节；Arm = 前臂（尾在腕）。骨骼互不父子，运行时每根直接按真手关节摆。
造型（霓虹动漫 + 美式卡通涂鸦，粗壮有力量）：
  · 主体：元球按骨骼生成圆润整体（手掌 + 前臂一组，每根手指单独一组，手指之间不粘连），指节鼓包；
  · 硬质件：指节骨甲（近节 + 中节）、掌指关节骨刺、手背骨盾（上面三道霓虹爪痕）、前臂三块骨甲（= 体力，掉一格裂一块）+ 背鳍骨刺、弯曲黑爪、爪根霓虹环、前臂两侧霓虹脉络；
  · 顶点色 = 底色；UV0 = 绑定姿势俯视坐标（着色器画鳞片）；UV1 = (点亮顺序 / 骨甲序号, 区域)：0 皮 0.25 骨甲 0.5 体力骨甲 0.75 爪 1 霓虹。
左手 = 右手镜像（单独导出，面的绕序翻回来）。"""
import bpy, bmesh, json, math, sys
from mathutils.bvhtree import BVHTree
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

HERE = Path(__file__).resolve().parent
OUT = Path(sys.argv[sys.argv.index('--') + 1]) if '--' in sys.argv else HERE.parent / 'ArtSource' / 'Hand' / 'out'
OUT.mkdir(parents=True, exist_ok=True)
BIND = json.load(open(HERE.parent / 'ArtSource' / 'Hand' / 'bind_right.json'))

# ───────── 配色（线性空间）─────────
SKIN = Vector((0.045, 0.085, 0.20))      # 手背：深靛青
PALM = Vector((0.55, 0.42, 0.30))        # 掌心：暖沙色
ARMOR = Vector((0.80, 0.74, 0.62))       # 骨甲：象牙
ARMOR_D = Vector((0.36, 0.30, 0.42))     # 骨甲深色边
CLAW = Vector((0.025, 0.02, 0.035))      # 爪：墨黑
NEON = Vector((0.55, 1.0, 1.0))          # 霓虹（着色器里按怒气发光）

FINGERS = [[2, 3, 4, 5]] + [[6 + 5 * f + k for k in range(5)] for f in range(4)]   # 拇指 [掌骨根..指尖]；四指 [掌骨根, 掌指, 中, 远, 尖]


def clear():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes): bpy.data.meshes.remove(m)
    for m in list(bpy.data.metaballs): bpy.data.metaballs.remove(m)
    for a in list(bpy.data.armatures): bpy.data.armatures.remove(a)


class Geo:
    """自己攒的网格：顶点 / 面 / 每顶点颜色、UV0、UV1、骨骼权重、面是否平滑"""
    def __init__(s): s.V, s.F, s.C, s.U0, s.U1, s.U2, s.A, s.W, s.S = [], [], [], [], [], [], [], [], []

    def add(s, verts, faces, color, region, order=0.0, weights=None, smooth=False, colors=None, u1=None, u0=None, u2=None, alpha=None):
        o = len(s.V)
        for i, v in enumerate(verts):
            s.V.append(Vector(v))
            s.C.append(colors[i] if colors else color)
            s.U0.append(u0[i] if u0 else (v[0], v[1]))
            s.U1.append(u1[i] if u1 else (order, region))
            s.U2.append(u2[i] if u2 else (v[0], v[1]))
            s.A.append(alpha[i] if alpha else 1.0)
            s.W.append(weights(Vector(v)) if callable(weights) else weights)
        for f in faces:
            s.F.append([o + i for i in f]); s.S.append(smooth)


def build(mirror):
    mx = -1.0 if mirror else 1.0
    J = [Vector((p[0] * mx, p[2], p[1])) for p in BIND['j']]   # Unity (x, y, z) → Blender (x, z, y)：同一个物理方向
    R = BIND['r']
    UP = Vector((0, 0, 1)); FWD = Vector((0, 1, 0))
    wrist = J[1]
    ARM0 = wrist - FWD * 0.2 - UP * 0.004   # 前臂根（肘方向）
    tail = {}
    for ch in FINGERS:
        for a, b in zip(ch, ch[1:]): tail[a] = J[b]
        d = (J[ch[-1]] - J[ch[-2]]).normalized(); tail[ch[-1]] = J[ch[-1]] + d * 0.014
    tail[1] = J[0]
    BONES = {'J1': (wrist, J[0])}
    for k, t in tail.items(): BONES[f'J{k}'] = (J[k], t)

    def seg_dist(p, a, b):
        ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-9, ab.dot(ab))))
        return (p - (a + ab * t)).length

    def skin_weights(cands):
        def fn(p):
            ds = sorted(((seg_dist(p, *BONES[c]), c) for c in cands))[:3]
            ws = [(1.0 / (d + 0.003) ** 4, c) for d, c in ds]
            tot = sum(w for w, _ in ws)
            return {c: w / tot for w, c in ws if w / tot > 0.02}
        return fn

    def rigid(bone): return {bone: 1.0}

    G = Geo()

    # ───────── 元球主体 ─────────
    # 先量一下元球的实际表面半径和设定半径的比例（threshold / stiffness 决定），后面按想要的表面半径反推
    def metaball(name, elems, res=0.0021):
        mb = bpy.data.metaballs.new(name); mb.resolution = res; mb.render_resolution = res; mb.threshold = 0.6
        ob = bpy.data.objects.new(name, mb); bpy.context.collection.objects.link(ob)
        for kind, a, b, r in elems:
            if kind == 'cap':
                e = mb.elements.new(type='CAPSULE'); d = b - a
                e.co = (a + b) / 2; e.radius = r / KR; e.size_x = d.length / 2
                e.rotation = Vector((1, 0, 0)).rotation_difference(d.normalized())
            else:
                e = mb.elements.new(type='BALL'); e.co = a; e.radius = r / KR
            e.stiffness = 2.0
        bpy.context.view_layer.update()
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
        bpy.data.objects.remove(ob, do_unlink=True)
        return me

    def decimated(me, ratio):
        ob = bpy.data.objects.new('tmp', me); bpy.context.collection.objects.link(ob)
        m = ob.modifiers.new('d', 'DECIMATE'); m.ratio = ratio
        bpy.context.view_layer.update()
        me2 = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
        bpy.data.objects.remove(ob, do_unlink=True)
        return me2

    def add_body(me, cands, ratio, paint):
        """主体网格。骨甲 / 霓虹沟的轮廓交给着色器逐像素算（11-10：顶点色画的边糊、发毛）——
        这里只写连续参数进 UV（三角形内线性插值是准的，所以边缘利落），几何上只留一点平滑隆起，轮廓有厚度"""
        me = decimated(me, ratio)
        verts = [v.co.copy() for v in me.vertices]
        normals = [v.normal.copy() for v in me.vertices]
        weights = cands if callable(cands) else skin_weights(cands)
        W0 = [weights(v) for v in verts]
        cols = []; U0 = []; U1 = []; U2 = []; AL = []
        for i, (p, n) in enumerate(zip(verts, normals)):
            t = max(0.0, min(1.0, (n.z + 0.35) / 0.45)); t = t * t * (3 - 2 * t)
            cols.append(PALM.lerp(SKIN, t))
            disp, u0, u1, u2, al = paint(p, n)
            verts[i] = p + n * disp
            U0.append(u0); U1.append(u1); U2.append(u2); AL.append(al)
        faces = [list(p.vertices) for p in me.polygons]
        G.add(verts, faces, None, 0.0, 0.0, None, True, cols, U1, U0, U2, AL)
        G.W[-len(verts):] = W0
        return len(faces)

    def frame(d, up=UP):
        """沿 d 的局部坐标系：x 侧、y 前（d）、z 上（尽量朝手背）"""
        y = d.normalized(); x = y.cross(up).normalized(); z = x.cross(y).normalized()
        return x, y, z

    def ss(e0, e1, x):
        t = max(0.0, min(1.0, (x - e0) / (e1 - e0))); return t * t * (3 - 2 * t)

    def seg_t(p, a, b):
        ab = b - a; return (p - a).dot(ab) / max(1e-9, ab.dot(ab))

    # ── 手指：UV0 = (节号 + 节内位置 0..1（近 / 中 / 远 = 0 / 1 / 2 开头）, 朝背程度)，UV1 = (0, 0.1 手指)，UV2 = (沿手指长度 m, 绕手指一圈的角度)（鳞片）──
    def finger_paint(ch, fi):
        segs = list(zip(ch[1:], ch[2:]))
        up0 = UP if fi else (UP + Vector((-0.6 * mx, 0, 0))).normalized()
        bands = [(0.16, 0.86), (0.18, 0.84), (0.1, 0.62)]
        def fn(p, n):
            best = None; acc = 0.0
            for si, (a, b) in enumerate(segs):
                t = seg_t(p, J[a], J[b])
                d = seg_dist(p, J[a], J[b])
                if best is None or d < best[0]: best = (d, si, t, (J[b] - J[a]).normalized(), acc + max(0, min(1, t)) * (J[b] - J[a]).length)
                acc += (J[b] - J[a]).length
            _, si, t, d, along = best
            x, y, z = frame(d, up0)
            back = n.dot(z)
            lo, hi = bands[si]
            m = ss(lo, lo + 0.08, t) * (1 - ss(hi - 0.08, hi, t)) * ss(0.3, 0.65, back)
            ang = math.atan2(n.dot(x), n.dot(z))
            return 0.0012 * m, (si + max(0.0, min(0.999, t)), back), (0.0, 0.1), (along, ang * 0.012), 1.0
        return fn

    # ── 手掌 / 前臂：UV0 = (离两片甲壳中线的距离差 gap, 沿掌骨位置 t)，UV1 = (前臂：沿前臂距离 yy；手背：1 + 沟号, 0 手掌组)，
    #    UV2 = 俯视坐标（鳞片），颜色 alpha = 朝上程度 ──
    METAS = [6, 11, 16, 21]
    def palm_paint(p, n):
        up = n.dot(UP)
        yy = (p - ARM0).dot(FWD)
        q = Vector((p.x, p.y, 0)); ds = []
        for k, b in enumerate(METAS):
            a2 = Vector((J[b].x, J[b].y, 0)); b2 = Vector((J[b + 1].x, J[b + 1].y, 0))
            ds.append((seg_dist(q, a2, b2), k, seg_t(q, a2, b2)))
        ds.sort()
        (d1, k1, t1), (d2, k2, _) = ds[0], ds[1]
        gap = d2 - d1
        disp = 0.0
        if True:
            # 连续的手背坐标（11-10：按“最近那根掌骨”算会在换掌骨处跳变 → 甲壳边缘成阶梯）：
            # 纵向 lngc：手腕 0 → 掌指关节连线 1；横向 lat：小指那根掌骨 0 → 食指那根 1（左右手都一样）
            mcp_y = sum(J[b + 1].y for b in METAS) / 4
            lngc = (p.y - wrist.y) / (mcp_y - wrist.y)
            x_l = (J[21].x + J[22].x) / 2; x_i = (J[6].x + J[7].x) / 2
            lat = (p.x - x_l) / (x_i - x_l)
            lng = ss(0.12, 0.25, lngc) * (1 - ss(0.86, 0.96, lngc))
            disp = 0.0018 * lng * ss(0.25, 0.6, up) * ss(0.001, 0.004, gap) * ss(-0.32, -0.18, lat) * (1 - ss(1.18, 1.32, lat))
            gap = max(gap, 0.0)
            return disp, (gap, lngc), (1.0 + max(-0.5, min(1.5, lat)), 0.0), (p.x, p.y), max(-1.0, min(1.0, up)) * 0.5 + 0.5
        return disp, (gap, t1), u1, (p.x, p.y), max(-1.0, min(1.0, up)) * 0.5 + 0.5

    def chain_weights(ch, fi):
        """手指沿指链的权重：每节骨头在自己那一节里是 1，靠近关节的 30% 和相邻那节线性渐变（关节正中各一半）。
        近节的根部和掌骨（拇指是腕）渐变，指尖之外跟远节"""
        segs = list(zip(ch[:-1], ch[1:]))   # (掌骨根→掌指), (掌指→中), (中→远), (远→尖)；拇指少一节
        def fn(p):
            best = None
            for si, (a, b) in enumerate(segs):
                d = seg_dist(p, J[a], J[b]); t = seg_t(p, J[a], J[b])
                if best is None or d < best[0]: best = (d, si, t)
            _, si, t = best
            w = {}
            def add(k, v):
                if 0 <= k < len(segs) and v > 0: w[f'J{segs[k][0]}'] = w.get(f'J{segs[k][0]}', 0) + v
            B = 0.3
            if t < B and si > 0: m = 0.5 + 0.5 * t / B; add(si, m); add(si - 1, 1 - m)
            elif t > 1 - B and si < len(segs) - 1: m = 0.5 + 0.5 * (1 - t) / B; add(si, m); add(si + 1, 1 - m)
            else: add(si, 1.0)
            return w
        return fn

    body_tris = 0
    # 手掌 + 前臂：四根掌骨胶囊连成厚掌板，拇指根的大鱼际，手腕，前臂（往肘方向变粗，肌肉块）
    palm = []
    for b in (6, 11, 16, 21): palm.append(('cap', J[b], J[b + 1], 0.019))
    palm.append(('cap', J[2], J[3], 0.019))                        # 大鱼际
    palm.append(('cap', J[2], J[11], 0.017))
    palm.append(('ball', (J[0] + J[11]) / 2 + UP * 0.002, None, 0.024))
    palm.append(('cap', wrist + FWD * 0.004, J[21] + FWD * 0.01, 0.017))
    palm.append(('cap', wrist - FWD * 0.004, wrist + FWD * 0.014, 0.024))      # 手腕（11-10 用户：不要手臂，只要手；收短，末端藏进护腕）
    body_tris += add_body(metaball('MB_Palm', palm, 0.0016), ['J1', 'J2', 'J6', 'J11', 'J16', 'J21'], 0.5, palm_paint)
    # 每根手指单独一组（手指之间不粘连）：指骨胶囊 + 指节鼓包
    for fi, ch in enumerate(FINGERS):
        k = 1.42 if fi else 1.5   # 11-11 用户：手指再粗一点
        el = []
        segs = list(zip(ch[1:] if fi else ch, (ch[2:] if fi else ch[1:])))
        taper = [1.12, 1.0, 0.9] if fi else [1.15, 1.02, 0.92]   # 根粗尖细（11-10：原来一节节像香肠）
        for si, (a, b) in enumerate(segs): el.append(('cap', J[a], J[b], R[a] * k * taper[min(si, 2)]))
        for si, (a, _) in enumerate(segs): el.append(('ball', J[a], None, R[a] * k * taper[min(si, 2)] * 1.05))   # 指节只微微鼓起
        el.append(('ball', J[ch[-1]] - (J[ch[-1]] - J[ch[-2]]).normalized() * 0.003, None, R[ch[-1]] * k * 0.95))
        body_tris += add_body(metaball(f'MB_F{fi}', el, 0.0014), chain_weights(ch, fi), 0.5, finger_paint(ch, fi))

    # ───────── 硬质件：全部贴着主体的真实表面摆（元球表面比半径估算的鼓，按半径摆会埋进肉里）─────────
    bvh = BVHTree.FromPolygons([tuple(v) for v in G.V], G.F)

    def surf(axis_pt, rd, fallback, maxr=0.08):
        """从骨头中心往外找最外层的表面：沿射线连续求交，记下每次“穿出”（法线和射线同向）；
        穿出后 2mm 内又“穿入”= 重叠的另一块肉（指根和手掌的元球叠在一起），继续往外；遇到真正的空气间隙（下一根手指）就停"""
        rd = rd.normalized(); o = axis_pt.copy(); travelled = 0.0; best = None
        while travelled < maxr:
            hit, nrm, _, dist = bvh.ray_cast(o, rd, maxr - travelled)
            if hit is None: break
            travelled += dist
            if nrm.dot(rd) > 0: best = travelled                     # 穿出
            elif best is not None and travelled - best > 0.002: break   # 穿入另一块（中间有空气）
            o = hit + rd * 0.0002; travelled += 0.0002
        return best if best is not None else fallback

    def frame(d, up=UP):
        """沿 d 的局部坐标系：x 侧、y 前（d）、z 上（尽量朝手背）"""
        y = d.normalized(); x = y.cross(up).normalized(); z = x.cross(y).normalized()
        return x, y, z

    def shell(a, b, r, arc, th, color, region, order, bone, up=UP, taper=1.0, ns=16, nl=7, maxr=0.08):
        """包在一段骨头背面的弧形骨甲：内外两层 + 封边（闭合实体）"""
        x, y, z = frame(b - a, up); L = (b - a).length
        V = []; F = []
        for layer in (0, 1):
            for i in range(nl + 1):
                u = i / nl
                for j in range(ns + 1):
                    ang = math.radians(-arc / 2 + arc * j / ns)
                    rd = z * math.cos(ang) + x * math.sin(ang); c = a + y * (L * u)
                    rs = surf(c, rd, r, maxr)
                    # 边缘一圈略薄（倒角感），中间厚
                    edge = min(j, ns - j, i * 2, (nl - i) * 2) / max(1, min(ns, nl * 2) / 2)
                    V.append(c + rd * (rs - 0.0016 + (th * (0.55 + 0.45 * min(1, edge)) if layer else 0)))
        n = (nl + 1) * (ns + 1)
        idx = lambda l, i, j: l * n + i * (ns + 1) + j
        for i in range(nl):
            for j in range(ns):
                F.append([idx(1, i, j), idx(1, i + 1, j), idx(1, i + 1, j + 1), idx(1, i, j + 1)])   # 外
                F.append([idx(0, i, j), idx(0, i, j + 1), idx(0, i + 1, j + 1), idx(0, i + 1, j)])   # 内
        for i in range(nl):
            for j in (0, ns):
                q = [idx(0, i, j), idx(0, i + 1, j), idx(1, i + 1, j), idx(1, i, j)]
                F.append(q if j == ns else q[::-1])
        for i in (0, nl):
            for j in range(ns):
                q = [idx(0, i, j), idx(0, i, j + 1), idx(1, i, j + 1), idx(1, i, j)]
                F.append(q if i == 0 else q[::-1])
        G.add(V, F, color, region, order, rigid(bone), False)

    def cone(base, d, r, h, color, region, order, bone, seg=6, bend=None):
        """尖刺 / 爪：底面圆心 base，沿 d 长 h；bend 不为空时沿 bend 方向弯（爪）"""
        x, y, z = frame(d, bend if bend is not None else UP)
        rings = 5 if bend is not None else 1
        V = []; F = []
        for i in range(rings):
            u = i / rings; rad = r * (1 - u) ** 0.8
            curve = -(u * u) * h * 0.55 if bend is not None else 0
            c = base + y * (h * u) + z * curve
            for j in range(seg):
                ang = 2 * math.pi * j / seg
                V.append(c + (x * math.cos(ang) + z * math.sin(ang)) * rad)
        tip = base + y * h + z * (-0.55 * h if bend is not None else 0)
        V.append(tip); V.append(base)
        for i in range(rings - 1):
            for j in range(seg):
                a0 = i * seg + j; a1 = i * seg + (j + 1) % seg
                F.append([a0, a1, a1 + seg, a0 + seg])
        t = len(V) - 2; bc = len(V) - 1; last = (rings - 1) * seg
        for j in range(seg):
            F.append([last + j, last + (j + 1) % seg, t])
            F.append([(j + 1) % seg, j, bc])
        G.add(V, F, color, region, order, rigid(bone), False)

    def tube(pts, r, color, region, order, bone, seg=6, weights=None):
        V = []; F = []
        for i, p in enumerate(pts):
            d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            x, _, z = frame(d)
            for j in range(seg):
                ang = 2 * math.pi * j / seg
                V.append(p + (x * math.cos(ang) + z * math.sin(ang)) * r)
        for i in range(len(pts) - 1):
            for j in range(seg):
                a0 = i * seg + j; a1 = i * seg + (j + 1) % seg
                F.append([a0, a1, a1 + seg, a0 + seg])
        V.append(pts[0]); V.append(pts[-1]); c0 = len(V) - 2; c1 = len(V) - 1; last = (len(pts) - 1) * seg
        for j in range(seg):
            F.append([(j + 1) % seg, j, c0]); F.append([last + j, last + (j + 1) % seg, c1])
        G.add(V, F, color, region, order, weights or rigid(bone), True)

    def ring(c, d, r, th, color, region, order, bone, seg=12):
        x, y, z = frame(d)
        pts = [c + (x * math.cos(2 * math.pi * j / seg) + z * math.sin(2 * math.pi * j / seg)) * r for j in range(seg)]   # 首尾不重复（闭合环）
        V = []; F = []; s2 = 6
        for i, p in enumerate(pts):
            rd = (p - c).normalized()
            for k in range(s2):
                a = 2 * math.pi * k / s2
                V.append(p + (rd * math.cos(a) + y * math.sin(a)) * th)
        for i in range(seg):
            i2 = (i + 1) % seg
            for k in range(s2):
                a0 = i * s2 + k; a1 = i * s2 + (k + 1) % s2; b0 = i2 * s2 + k; b1 = i2 * s2 + (k + 1) % s2
                F.append([a0, b0, b1, a1])
        G.add(V, F, color, region, order, rigid(bone), True)

    # 掌指关节骨刺（四指），往手腕方向斜
    # 11-11 用户：手背四根骨刺要长在手背上、不跟着手指动 → 挪到掌指关节后面 1.8cm（手背甲壳前端），绑手掌（J1）
    for f in range(4):
        m = J[7 + 5 * f] - FWD * 0.018; r = 0.02
        rs = surf(m, UP, r)
        cone(m + UP * (rs - 0.0015), (UP * 1.0 - FWD * 0.6).normalized(), 0.0058, 0.016 - f * 0.0015, ARMOR, 0.25, 0, 'J1', 6)
    # 手腕口：一圈骨甲护腕 + 一道霓虹环收口（不像被截断）
    wc = wrist - FWD * 0.008
    wr = max(surf(wc + FWD * 0.008, d2, 0.024) for d2 in (UP, -UP, Vector((1, 0, 0)), Vector((-1, 0, 0))))
    ring(wc, FWD, wr + 0.0016, 0.0046, ARMOR, 0.25, 0, 'J1', 24)                 # 粗护腕
    ring(wc + FWD * 0.0085, FWD, wr + 0.0004, 0.0012, NEON, 1.0, 0.02, 'J1', 24)  # 护腕前沿一圈霓虹
    # 端盖：深色骨甲微拱圆盖，把手腕截面整个盖住（从后面看不到肉的圆头）
    cone(wc - FWD * 0.002, -FWD, wr + 0.0035, 0.007, ARMOR_D * 0.5, 0.25, 0, 'J1', 24)
    # 爪（弯曲黑爪，从指尖背面伸出）+ 爪根霓虹环（怒气最后亮到这里：小指 → 拇指）
    for fi, ch in enumerate(FINGERS):
        tip = J[ch[-1]]; d = (J[ch[-1]] - J[ch[-2]]).normalized()
        r = R[ch[-1]] * (1.5 if fi == 0 else 1.42)
        up = UP if fi else (UP + Vector((-0.6 * mx, 0, 0))).normalized()
        x, y, z = frame(d, up)
        # 爪：从指尖背面（贴表面）长出来，粗、长、往下弯
        cp = tip - d * 0.007
        base = cp + z * (surf(cp, z, r) - 0.0025)
        cone(base, (d + z * 0.25).normalized(), r * 0.72, 0.027 if fi else 0.025, CLAW, 0.75, 0, f'J{ch[-1]}', 8, bend=z)
        rc = tip - d * 0.011
        rr = max(surf(rc, z, r), surf(rc, x, r), surf(rc, -x, r), surf(rc, -z, r)) + 0.0006
        ring(rc, d, rr, 0.0012, NEON, 1.0, 0.68 + (4 - fi) * 0.08 if fi else 1.0, f'J{ch[-2]}')

    # ───────── 组装网格 + 骨骼 ─────────
    me = bpy.data.meshes.new('MonsterHand')
    me.from_pydata([tuple(v) for v in G.V], [], G.F)
    me.update()
    # 每个闭合部件统一朝外（11-10：镜像左手时整只翻面 → 左手发黑；程序尖锥的绕序也不统一）
    bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:]); bm.to_mesh(me); bm.free(); me.update()
    for p, sm in zip(me.polygons, G.S): p.use_smooth = sm
    col = me.color_attributes.new('Col', 'FLOAT_COLOR', 'POINT')
    for i, c in enumerate(G.C): col.data[i].color = (c[0], c[1], c[2], G.A[i])
    uv0 = me.uv_layers.new(name='UV0'); uv1 = me.uv_layers.new(name='UV1'); uv2 = me.uv_layers.new(name='UV2')
    for p in me.polygons:
        for li in p.loop_indices:
            vi = me.loops[li].vertex_index
            uv0.data[li].uv = G.U0[vi]; uv1.data[li].uv = G.U1[vi]; uv2.data[li].uv = G.U2[vi]
    ob = bpy.data.objects.new('MonsterHand_' + ('L' if mirror else 'R'), me); bpy.context.collection.objects.link(ob)

    arm = bpy.data.armatures.new('Rig'); rig = bpy.data.objects.new('Rig_' + ('L' if mirror else 'R'), arm)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig; rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    for name, (h, t) in BONES.items():
        eb = arm.edit_bones.new(name); eb.head = h; eb.tail = t; eb.use_deform = True
    bpy.ops.object.mode_set(mode='OBJECT')
    for name in BONES: ob.vertex_groups.new(name=name)
    for vi, w in enumerate(G.W):
        for bn, ww in w.items(): ob.vertex_groups[bn].add([vi], ww, 'REPLACE')
    ob.parent = rig
    mod = ob.modifiers.new('Armature', 'ARMATURE'); mod.object = rig

    # 体检：开放边 / 非流形（每个部件都应是闭合实体）
    bm = bmesh.new(); bm.from_mesh(me)
    open_edges = sum(1 for e in bm.edges if not e.is_manifold)
    bm.free()
    rep = {'verts': len(me.vertices), 'tris': sum(len(p.vertices) - 2 for p in me.polygons), 'body_faces': body_tris, 'non_manifold_edges': open_edges}

    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); rig.select_set(True); bpy.context.view_layer.objects.active = rig
    fp = OUT / f"monster_hand_{'L' if mirror else 'R'}.fbx"
    bpy.ops.export_scene.fbx(filepath=str(fp), use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                             use_armature_deform_only=True, axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_UNITS',
                             mesh_smooth_type='FACE', colors_type='LINEAR', bake_anim=False, path_mode='STRIP')
    if not mirror: preview(ob, rig)
    return rep


def preview(ob, rig):
    """工作台渲染 4 张预览（俯视手背 / 掌心 / 侧面 / 3/4），看造型"""
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading; sh.light = 'STUDIO'; sh.color_type = 'VERTEX'; sh.show_cavity = True; sh.cavity_type = 'WORLD'; sh.show_object_outline = True
    sh.background_type = 'VIEWPORT'; sh.background_color = (0.12, 0.12, 0.14)
    sc.render.resolution_x = 900; sc.render.resolution_y = 700; sc.render.film_transparent = False
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); bpy.context.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = 50
    tgt = Vector((0, 0.03, 0))
    for name, pos in (('top', (0.0, 0.03, 0.55)), ('palm', (0.0, 0.03, -0.55)), ('side', (-0.5, 0.05, 0.08)), ('q34', (-0.32, 0.32, 0.3))):
        cam.location = Vector(pos); d = tgt - cam.location
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = str(OUT / f'preview_{name}.png')
        bpy.ops.render.render(write_still=True)


KR = 1.0
clear()
# 量元球比例：一个设定半径 0.01 的球，实际表面半径是多少
mb = bpy.data.metaballs.new('cal'); mb.resolution = 0.0005; mb.render_resolution = 0.0005; mb.threshold = 0.6
e = mb.elements.new(type='BALL'); e.co = (0, 0, 0); e.radius = 0.01; e.stiffness = 2.0
o = bpy.data.objects.new('cal', mb); bpy.context.collection.objects.link(o); bpy.context.view_layer.update()
cm = bpy.data.meshes.new_from_object(o.evaluated_get(bpy.context.evaluated_depsgraph_get()))
KR = max(v.co.length for v in cm.vertices) / 0.01
bpy.data.objects.remove(o, do_unlink=True)
print('元球表面/设定半径比', round(KR, 3), flush=True)

report = {}
for mirror in (False, True):
    clear()
    report['L' if mirror else 'R'] = build(mirror)
print('报告', json.dumps(report, ensure_ascii=False), flush=True)
