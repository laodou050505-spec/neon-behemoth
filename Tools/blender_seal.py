"""《我是怪兽》 · 封印塔 + 电站（Blender 程序化，真实尺寸米；色板 UV，Unity 按塔的颜色换发光格）
    Blender -b -P Tools/blender_seal.py -- <输出目录>
封印塔 = 底座（八角基台 + 四面台阶 + 四根斜撑扶壁）+ 塔身段（八角芯筒 + 每面一条符文发光槽 + 四角外骨架肋 + 上下箍环 + 螺栓），
         堆 N 段、越往上越细（Unity 里按层缩放，一段一个物体，拆的时候一段段掉）+ 塔冠（平台 + 三根爪臂托着发光晶核 + 两道光环）。
电站 = 水泥场地 + 围栏 + 两台变压器（散热片、瓷瓶套管）+ 门型构架挂绝缘子串 + 能量线圈柱（发光环）+ 控制室 + 警示条 + 电缆沟。
色板格：0 深金属 1 钢 2 水泥 3 浅金属 4 发光（塔的颜色）5 警示黄 6 黑 7 白瓷 8 铜 9 深玻璃 10 绿漆"""
import bpy, bmesh, sys, json, math, pathlib
from mathutils import Vector, Matrix

OUT = pathlib.Path(sys.argv[sys.argv.index('--') + 1])
OUT.mkdir(parents=True, exist_ok=True)
exec(open(pathlib.Path(__file__).with_name('blender_mesh_checks.py'), encoding='utf-8').read())
DARK, STEEL, CONC, LIGHT, GLOW, HAZ, BLACK, CERAMIC, COPPER, GLASS, GREEN = range(11)


exec(open(pathlib.Path(__file__).with_name('blender_part.py'), encoding='utf-8').read())   # Part / octa


def on_faces(m, r, fn):
    """在八角形每个面的中心建局部坐标（x 沿面、y 朝外），调 fn(m)"""
    for k in range(8):
        ang = math.pi / 8 + k * math.pi / 4 + math.pi / 8   # 面中心的方位角
        c = Vector((r * math.cos(math.pi / 8) * math.cos(ang), r * math.cos(math.pi / 8) * math.sin(ang), 0))
        rot = Matrix.Rotation(ang - math.pi / 2, 4, 'Z')
        m.mat = Matrix.Translation(c) @ rot
        fn(m, k)
    m.mat = Matrix.Identity(4)


SEG_H = 6.6      # 一段塔身高（米）= Unity 里 modH / K
R = 4.2          # 芯筒外接圆半径


def tower_seg():
    m = Part()
    m.prism(octa(R), 0.45, SEG_H - 0.6, DARK)
    m.prism(octa(R + 0.35), 0.0, 0.45, STEEL)                # 下箍
    m.prism(octa(R + 0.45), SEG_H - 0.6, SEG_H, STEEL)       # 上箍
    def face_detail(m, k):
        # 符文发光槽（凸出 6cm）+ 两侧黑色槽边；每隔一面加两颗铆钉板
        m.box(-0.28, -0.02, 0.9, 0.28, 0.06, SEG_H - 1.0, GLOW)
        m.box(-0.5, -0.02, 0.75, -0.28, 0.1, SEG_H - 0.85, BLACK)
        m.box(0.28, -0.02, 0.75, 0.5, 0.1, SEG_H - 0.85, BLACK)
        if k % 2 == 0:
            for z in (1.6, 3.2, 4.6):
                m.box(-1.25, -0.02, z, -0.85, 0.08, z + 0.4, LIGHT)
                m.box(0.85, -0.02, z, 1.25, 0.08, z + 0.4, LIGHT)
    on_faces(m, R, face_detail)
    # 四角外骨架肋（在八角形的 4 个角上，竖向通高，凸出 0.55）
    for k in range(0, 8, 2):
        a = math.pi / 8 + k * math.pi / 4
        cx, cy = (R + 0.25) * math.cos(a), (R + 0.25) * math.sin(a)
        m.mat = Matrix.Translation((cx, cy, 0)) @ Matrix.Rotation(a, 4, 'Z')
        m.box(-0.35, -0.35, 0.45, 0.45, 0.35, SEG_H - 0.6, STEEL)
        m.box(0.45, -0.18, 1.0, 0.6, 0.18, SEG_H - 1.2, HAZ)
        m.mat = Matrix.Identity(4)
    return m.finish('tower_seg')


def tower_base():
    m = Part()
    m.prism(octa(6.4), 0.0, 0.5, CONC)                       # 基台下层
    m.prism(octa(5.8), 0.5, 1.4, CONC, octa(5.6))            # 基台上层（微收）
    m.prism(octa(R + 0.3), 1.4, SEG_H - 0.6, DARK)           # 芯筒
    m.prism(octa(R + 0.6), SEG_H - 0.6, SEG_H, STEEL)
    m.prism(octa(5.0), 1.4, 1.9, STEEL)                      # 芯筒脚下的钢环
    on_faces(m, R + 0.3, lambda m, k: (m.box(-0.28, -0.02, 2.2, 0.28, 0.06, SEG_H - 1.0, GLOW) if k % 2 else m.box(-0.95, -0.02, 1.9, 0.95, 0.12, 4.0, BLACK)))
    on_faces(m, 5.6, lambda m, k: [m.box(-1.3, 0.0 + s * 0.4, 0.0, 1.3, 0.4 + s * 0.4, 1.4 - s * 0.33, CONC) for s in range(4)] if k % 2 == 0 else None)
    # 四根斜撑扶壁（角上，从基台斜着顶到芯筒半腰）
    for k in range(1, 8, 2):
        a = math.pi / 8 + k * math.pi / 4
        d = Vector((math.cos(a), math.sin(a), 0)); t = Vector((-d.y, d.x, 0))
        p0, p1 = d * 5.5, d * (R + 0.35)
        def P(base, z, s): return tuple(base + t * s + Vector((0, 0, z)))
        m.hexa([P(p0, 1.4, -0.35), P(p0 + d * 0.0, 1.4, 0.35), P(p1, 1.4, 0.35), P(p1, 1.4, -0.35),
                                        P(p0, 1.9, -0.35), P(p0, 1.9, 0.35), P(p1, 4.6, 0.35), P(p1, 4.6, -0.35)], STEEL)   # 前端竖直一小段，侧面是凸四边形
    return m.finish('tower_base')


def tower_crown():
    m = Part()
    m.prism(octa(R + 0.5), 0.0, 0.7, STEEL)
    m.prism(octa(R - 0.4), 0.7, 1.3, DARK, octa(R - 1.0))
    # 三根爪臂：每根三节，往中心收拢
    for k in range(3):
        a = k * 2 * math.pi / 3 + math.pi / 6
        d = Vector((math.cos(a), math.sin(a), 0)); t = Vector((-d.y, d.x, 0))
        pts = [(3.2, 1.3), (3.6, 3.5), (2.6, 5.6), (1.2, 6.6)]
        for (r0, z0), (r1, z1) in zip(pts[:-1], pts[1:]):
            w0, w1 = 0.45, 0.32
            def P(r, z, s, h): return tuple(d * (r + h) + t * s + Vector((0, 0, z)))
            m.hexa([P(r0, z0, -w0, -0.3), P(r0, z0, w0, -0.3), P(r0, z0, w0, 0.3), P(r0, z0, -w0, 0.3),
                                            P(r1, z1, -w1, -0.25), P(r1, z1, w1, -0.25), P(r1, z1, w1, 0.25), P(r1, z1, -w1, 0.25)], DARK)
        m.mat = Matrix.Translation(d * 3.75 + Vector((0, 0, 3.4))) @ Matrix.Rotation(a, 4, 'Z')
        m.box(-0.1, -0.2, -0.6, 0.12, 0.2, 0.6, GLOW)
        m.mat = Matrix.Identity(4)
    # 晶核：上下两个八棱锥（发光）
    core = octa(1.4)
    m.prism(octa(0.05), 1.3, 3.9, GLOW, core)
    m.prism(core, 3.9, 6.4, GLOW, octa(0.05))
    # 两道光环（12 段弧块拼成）
    for zr, rr in ((3.3, 2.6), (4.6, 2.1)):
        for i in range(12):
            a0, a1 = i * 2 * math.pi / 12, (i + 1) * 2 * math.pi / 12   # 段与段首尾相接成一整圈
            def Q(a, r, z): return (r * math.cos(a), r * math.sin(a), z)
            m.hexa([Q(a0, rr - 0.12, zr), Q(a1, rr - 0.12, zr), Q(a1, rr + 0.12, zr), Q(a0, rr + 0.12, zr),
                                            Q(a0, rr - 0.12, zr + 0.18), Q(a1, rr - 0.12, zr + 0.18), Q(a1, rr + 0.12, zr + 0.18), Q(a0, rr + 0.12, zr + 0.18)], GLOW)
    # 光环的三根辐条：从晶核穿到光环外沿（和爪臂同方向），光环不再悬空
    for zr, rr in ((3.3, 2.6), (4.6, 2.1)):
        for k in range(3):
            a = k * 2 * math.pi / 3 + math.pi / 6
            m.mat = Matrix.Translation((0, 0, zr)) @ Matrix.Rotation(a, 4, 'Z')
            m.box(0.3, -0.08, 0.02, rr + 0.1, 0.08, 0.16, DARK)
            m.mat = Matrix.Identity(4)
    return m.finish('tower_crown')


def station():
    """16 × 12 米；正面 -y"""
    m = Part()
    W, D = 16.0, 12.0
    m.box(-W / 2, -D / 2, 0, W / 2, D / 2, 0.3, CONC)
    for i in range(16):                                      # 场地边警示条
        x0 = -W / 2 + i * W / 16
        m.box(x0, -D / 2 - 0.02, 0.0, x0 + W / 16, -D / 2 + 0.3, 0.32, HAZ if i % 2 == 0 else BLACK)
    # 围栏：立柱 + 两道横杆（正面中间留门）
    for side in range(4):
        L = W if side % 2 == 0 else D
        n = int(L / 2)
        for i in range(n + 1):
            s = -L / 2 + i * L / n
            if side == 0 and abs(s) < 1.6: continue
            x, y = [(s, -D / 2 + 0.5), (W / 2 - 0.5, s), (s, D / 2 - 0.5), (-W / 2 + 0.5, s)][side]
            m.box(x - 0.06, y - 0.06, 0.3, x + 0.06, y + 0.06, 2.3, STEEL)
        for z in (1.2, 2.2):
            if side == 0:
                for a0, a1 in ((-W / 2 + 0.5, -1.6), (1.6, W / 2 - 0.5)):
                    m.box(a0, -D / 2 + 0.47, z, a1, -D / 2 + 0.53, z + 0.06, STEEL)
            elif side == 2: m.box(-W / 2 + 0.5, D / 2 - 0.53, z, W / 2 - 0.5, D / 2 - 0.47, z + 0.06, STEEL)
            elif side == 1: m.box(W / 2 - 0.53, -D / 2 + 0.56, z, W / 2 - 0.47, D / 2 - 0.56, z + 0.06, STEEL)
            else: m.box(-W / 2 + 0.47, -D / 2 + 0.56, z, -W / 2 + 0.53, D / 2 - 0.56, z + 0.06, STEEL)
    # 两台变压器：箱体 + 两侧散热片 + 顶上三支瓷瓶套管（发光帽）
    for cx in (-4.2, -0.2):
        m.box(cx - 1.5, -1.0, 0.3, cx + 1.5, 1.0, 2.7, GREEN)
        m.box(cx - 1.6, -1.1, 2.7, cx + 1.6, 1.1, 2.9, STEEL)
        for i in range(6):
            x = cx - 1.25 + i * 0.5
            m.box(x - 0.06, -1.6, 0.6, x + 0.06, -1.0, 2.4, STEEL)
            m.box(x - 0.06, 1.0, 0.6, x + 0.06, 1.6, 2.4, STEEL)
        for i in (-1, 0, 1):
            for k in range(3):
                m.cyl(cx + i * 0.9, 0, 2.9 + k * 0.42, 3.25 + k * 0.42, 0.26 - k * 0.03, 8, CERAMIC)
                if k < 2: m.cyl(cx + i * 0.9, 0, 3.25 + k * 0.42, 3.32 + k * 0.42, 0.14, 8, STEEL)
            m.cyl(cx + i * 0.9, 0, 4.09, 4.34, 0.18, 8, GLOW)   # 紧贴最上一节瓷瓶
    # 门型构架（两榀）+ 横梁上挂的绝缘子串
    for y in (-3.2, 3.2):
        for x in (-6.8, 2.6):
            m.box(x - 0.18, y - 0.18, 0.3, x + 0.18, y + 0.18, 6.2, STEEL)
        m.box(-7.0, y - 0.15, 6.2, 2.8, y + 0.15, 6.5, STEEL)
        for x in (-5.0, -2.2, 0.6):
            for k in range(4):   # 绝缘子串：四节首尾相接，最上一节顶到横梁底（6.2）
                m.cyl(x, y, 6.2 - (k + 1) * 0.3, 6.2 - k * 0.3, 0.18 if k % 2 == 0 else 0.13, 8, CERAMIC)
            m.box(x - 0.05, y - 0.05, 4.4, x + 0.05, y + 0.05, 5.0, COPPER)
    # 能量线圈柱（发光环，和塔同色）
    cx, cy = 5.3, 2.0
    m.cyl(cx, cy, 0.3, 0.9, 1.8, 10, CONC)
    m.cyl(cx, cy, 0.9, 5.6, 1.1, 10, DARK)
    for z in (1.6, 2.8, 4.0):
        m.cyl(cx, cy, z, z + 0.35, 1.32, 10, GLOW)
    m.cyl(cx, cy, 5.6, 6.0, 1.35, 10, STEEL)
    m.cyl(cx, cy, 6.0, 7.6, 0.25, 8, COPPER, 0.08)
    m.cyl(cx, cy, 7.4, 7.9, 0.55, 8, GLOW)
    # 控制室：墙 + 门 + 窗带 + 屋顶 + 空调
    m.box(3.0, -5.3, 0.3, 7.6, -1.6, 3.3, CONC)
    m.box(3.0 - 0.15, -5.45, 3.3, 7.6 + 0.15, -1.45, 3.55, STEEL)
    m.box(3.6, -5.33, 0.3, 4.6, -5.3, 2.4, BLACK)
    m.box(5.0, -5.33, 1.5, 7.2, -5.3, 2.5, GLASS)
    m.box(5.6, -3.0, 3.55, 6.8, -2.0, 4.3, STEEL)
    # 电缆沟（正面朝塔的方向拉出去）
    m.box(-1.0, -D / 2 + 0.6, 0.3, 1.0, -1.2, 0.42, BLACK)
    return m.finish('station')


bpy.ops.wm.read_factory_settings(use_empty=True)
report = {}
for fn in (tower_seg, tower_base, tower_crown, station):
    ob, rep = fn()
    report[ob.name] = rep
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=str(OUT / f'{ob.name}.fbx'), use_selection=True, path_mode='STRIP', axis_forward='-Z', axis_up='Y',
                                                                                                    apply_scale_options='FBX_SCALE_UNITS', mesh_smooth_type='FACE', bake_space_transform=True)
print('报告', json.dumps(report, ensure_ascii=False), flush=True)
