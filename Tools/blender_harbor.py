"""《我是怪兽》 · 港口：船（游艇 / 拖轮 / 快艇 / 集装箱驳船）+ 岸桥（集装箱吊车）+ 集装箱。真实尺寸米、色板 UV（同车的 16 色板：0 船身色由 Unity 换）
    Blender -b -P Tools/blender_harbor.py -- <输出目录>
船体 = 平面轮廓（尖船头）竖直挤出，上沿比下沿宽一点（外飘），水线以上一段船身色、下面一圈深色；上层建筑一层层退台。"""
import bpy, bmesh, sys, json, math, pathlib
from mathutils import Vector, Matrix

OUT = pathlib.Path(sys.argv[sys.argv.index('--') + 1])
OUT.mkdir(parents=True, exist_ok=True)
exec(open(pathlib.Path(__file__).with_name('blender_mesh_checks.py'), encoding='utf-8').read())
exec(open(pathlib.Path(__file__).with_name('blender_part.py'), encoding='utf-8').read())
# 车色板格：0 车身色（Unity 换） 9 深玻璃 10 黑 11 前灯（夜亮） 12 尾灯（夜亮） 13 暖黄灯（夜亮） 14 深灰 15 浅金属
BODY, GLASS, BLACK, HEAD, TAIL, LAMP, TRIM, CHROME = 0, 9, 10, 11, 12, 13, 14, 15
WHITE = 0   # 游艇船身用车身格，Unity 给白色
WHITEW = 6   # 船楼：车色板 6 = 浅灰白
STEELC = 14  # 钢构
LAMP2 = 13   # 救生艇（亮黄橙）


def hull_plan(L, W, bow=0.3):
    """船平面（逆时针），船头朝 -y"""
    y0, y1 = -L / 2, L / 2
    return [(0, y0), (W * 0.35, y0 + L * bow * 0.35), (W / 2, y0 + L * bow), (W / 2, y1 - 0.3), (W / 2 - 0.3, y1), (-W / 2 + 0.3, y1), (-W / 2, y1 - 0.3), (-W / 2, y0 + L * bow), (-W * 0.35, y0 + L * bow * 0.35)]


def hull(m, L, W, h, pal, flare=1.08):
    plan = hull_plan(L, W)
    S = lambda k: [(x * k, y * k) for (x, y) in plan]   # 等比缩放：侧面四边形保持平面（x、y 不同比例会扭曲出反面）
    m.prism(S(0.93), 0.0, 0.35, BLACK, S(0.96))         # 水线下的深色一圈
    m.prism(S(0.96), 0.35, h, pal, S(1.0))
    m.prism(S(0.985), h, h + 0.08, CHROME)              # 甲板


def yacht():
    m = Part()
    L, W = 14.0, 4.2
    hull(m, L, W, 1.6, WHITE)
    m.prism(hull_plan(L * 0.62, W * 0.82), 1.68, 3.1, WHITE)
    m.box(-W * 0.4, -L * 0.26, 2.0, W * 0.4, L * 0.25, 2.8, GLASS)
    m.prism(hull_plan(L * 0.4, W * 0.7), 3.1, 4.2, WHITE)
    m.box(-W * 0.34, -L * 0.17, 3.35, W * 0.34, L * 0.15, 3.95, GLASS)
    m.box(-W * 0.36, -L * 0.1, 4.2, W * 0.36, L * 0.18, 4.32, CHROME)
    m.box(-0.06, L * 0.05, 4.32, 0.06, L * 0.08, 5.6, CHROME)
    m.box(-0.15, L * 0.05 - 0.05, 5.6, 0.15, L * 0.08 + 0.05, 5.75, LAMP)
    m.box(-W * 0.38, L * 0.3, 1.68, W * 0.38, L * 0.46, 1.9, TRIM)   # 尾部泳台
    return m.finish('yacht')


def tug():
    m = Part()
    L, W = 10.0, 4.0
    hull(m, L, W, 1.8, BODY)
    m.box(-W / 2 - 0.12, -L * 0.3, 0.6, -W / 2 * 0.95, L * 0.45, 1.0, BLACK)   # 两侧防撞胎带（压进船壳）
    m.box(W / 2 * 0.95, -L * 0.3, 0.6, W / 2 + 0.12, L * 0.45, 1.0, BLACK)
    m.box(-1.4, -L * 0.15, 1.88, 1.4, L * 0.2, 3.6, CHROME)
    m.box(-1.6, -L * 0.17, 3.6, 1.6, L * 0.05, 5.0, TRIM)
    m.box(-1.5, -L * 0.18, 4.0, 1.5, L * 0.04, 4.7, GLASS)
    m.box(-1.7, -L * 0.19, 5.0, 1.7, L * 0.07, 5.15, CHROME)
    m.cyl(0, L * 0.14, 3.6, 6.2, 0.45, 8, BLACK)
    m.cyl(0, L * 0.14, 5.4, 5.8, 0.5, 8, TAIL)
    m.box(-0.08, -L * 0.08, 5.15, 0.08, -L * 0.04, 6.4, CHROME)
    m.box(-0.18, -L * 0.07, 6.4, 0.18, -L * 0.05, 6.55, LAMP)
    return m.finish('tug')


def speedboat():
    m = Part()
    L, W = 7.0, 2.4
    hull(m, L, W, 0.95, BODY, 1.05)
    m.slab((-L * 0.12, 0.99), (L * 0.02, 1.7), W * 0.8, 0.05, GLASS, shrink=0.0)   # 风挡底边压进甲板
    m.box(-W * 0.36, L * 0.05, 1.03, W * 0.36, L * 0.25, 1.4, CHROME)
    m.box(-0.5, L * 0.42, 0.4, 0.5, L * 0.5 + 0.35, 1.3, TRIM)   # 舷外机
    return m.finish('speedboat')


CW, CH = 2.44, 2.59   # 集装箱宽、高（米）


def container_box(m, cx, cy, z0, L, pal):
    """一只集装箱（长边沿 y）：箱体 + 两侧竖向波纹板（凸 3.5cm）+ 门端（+y）门框和四根锁杆 + 另一端三道竖肋 + 四角角件。
    所有构件都不超出 [z0, z0 + CH]，上下堆叠时刚好贴合"""
    x0, x1, y0, y1 = cx - CW / 2, cx + CW / 2, cy - L / 2, cy + L / 2
    m.box(x0, y0, z0, x1, y1, z0 + CH, pal)
    n = max(4, int((L - 0.6) / 0.62))
    for i in range(n):
        y = y0 + 0.35 + (L - 0.7) * (i + 0.5) / n
        for sx in (-1, 1):
            xa, xb = (x1, x1 + 0.035) if sx > 0 else (x0 - 0.035, x0)
            m.box(xa, y - 0.12, z0 + 0.12, xb, y + 0.12, z0 + CH - 0.12, pal)
    # 门端：外框（上下横梁 + 两侧立柱，凸 6cm）+ 四根锁杆（镀铬）
    m.box(x0, y1, z0, x1, y1 + 0.06, z0 + 0.14, TRIM)
    m.box(x0, y1, z0 + CH - 0.16, x1, y1 + 0.06, z0 + CH, TRIM)
    m.box(x0, y1, z0 + 0.14, x0 + 0.12, y1 + 0.06, z0 + CH - 0.16, TRIM)
    m.box(x1 - 0.12, y1, z0 + 0.14, x1, y1 + 0.06, z0 + CH - 0.16, TRIM)
    for x in (-0.82, -0.42, 0.42, 0.82):
        m.box(cx + x - 0.025, y1, z0 + 0.3, cx + x + 0.025, y1 + 0.085, z0 + CH - 0.3, CHROME)
    # 另一端：三道竖肋
    for x in (-0.6, 0.0, 0.6):
        m.box(cx + x - 0.1, y0 - 0.035, z0 + 0.12, cx + x + 0.1, y0, z0 + CH - 0.12, pal)
    # 四角角件（门端那头已有门框，只在另一头加）
    for sx in (-1, 1):
        for z in (z0, z0 + CH - 0.16):
            xa, xb = (x1 - 0.16, x1 + 0.02) if sx > 0 else (x0 - 0.02, x0 + 0.16)
            m.box(xa, y0 - 0.02, z, xb, y0 + 0.16, z + 0.16, TRIM)


def container():
    """岸边堆场用的单只 40 尺集装箱"""
    m = Part()
    container_box(m, 0, 0, 0, 12.0, BODY)
    return m.finish('container')


def container_ship():
    """集装箱船（52 × 10 米）：船体 + 船头锚机 / 桅杆 + 四个货舱（舱盖 + 三列集装箱，中间堆得高两边低）+ 舱与舱之间的绑扎桥
    + 船尾船楼（三层、两翼驾驶台带窗、烟囱带色环、救生艇吊在两侧）"""
    m = Part()
    L, W, h = 52.0, 10.0, 3.2
    hull(m, L, W, h, BODY, 1.03)
    deck = h + 0.08
    # 船头：锚机两台 + 桅杆 + 航行灯
    for sx in (-1, 1):
        m.box(sx * 1.3 - 0.5, -L / 2 + 6.0, deck, sx * 1.3 + 0.5, -L / 2 + 7.0, deck + 0.7, TRIM)
        m.cyl(sx * 1.3, -L / 2 + 6.5, deck + 0.7, deck + 0.9, 0.35, 8, CHROME)
    m.cyl(0, -L / 2 + 9.0, deck, deck + 6.0, 0.18, 8, CHROME)
    m.box(-0.2, -L / 2 + 8.8, deck + 6.0, 0.2, -L / 2 + 9.2, deck + 6.3, LAMP)
    # 货舱：四个，每个 = 舱盖 + 3 列 20 尺集装箱
    rng = __import__('random').Random(5)
    cols = [2, 7, 4, 3, 5, 8, 6, 1]
    bay0, pitch, bays = -11.5, 6.6, 4
    for b in range(bays):
        yc = bay0 + pitch * (b + 0.5)
        m.box(-4.1, yc - 3.2, deck, 4.1, yc + 3.2, deck + 0.42, TRIM)                       # 舱盖
        top = deck + 0.42
        for j, xc in enumerate((-2.6, 0.0, 2.6)):
            tiers = rng.choice([2, 3, 3]) if j == 1 else rng.choice([1, 2, 2])
            for t in range(tiers):
                container_box(m, xc, yc, top + t * CH, 6.06, rng.choice(cols))
        # 绑扎桥（舱与舱之间的钢架：两根立柱 + 一道横梁），立在甲板上
        if b < bays - 1:
            yb = yc + pitch / 2
            for x in (-4.3, 4.3):
                m.box(x - 0.1, yb - 0.08, deck, x + 0.1, yb + 0.08, deck + 2.4, STEELC)
            m.box(-4.4, yb - 0.08, deck + 2.4, 4.4, yb + 0.08, deck + 2.6, STEELC)
    # 船楼（y 17 ~ 23.5）
    ys0, ys1 = 17.0, 23.5
    m.box(-4.2, ys0, deck, 4.2, ys1, deck + 2.8, WHITEW)
    m.box(-3.6, ys0 + 0.4, deck + 2.8, 3.6, ys1 - 0.4, deck + 5.4, WHITEW)
    m.box(-5.2, ys0 + 0.2, deck + 5.4, 5.2, ys0 + 3.2, deck + 7.8, WHITEW)                 # 驾驶台（两翼伸出船宽）
    m.box(-5.15, ys0 + 0.17, deck + 6.4, 5.15, ys0 + 0.2, deck + 7.4, GLASS)                # 驾驶台前窗带
    for z in (deck + 1.2, deck + 3.9):                                                     # 舱室窗带
        m.box(-3.0, ys0 - 0.03 + (0.4 if z > deck + 2.8 else 0), z, 3.0, ys0 + (0.4 if z > deck + 2.8 else 0), z + 0.7, GLASS)
    m.box(-5.3, ys0 + 0.1, deck + 7.8, 5.3, ys0 + 3.3, deck + 8.0, TRIM)                    # 驾驶台顶
    m.box(-0.12, ys0 + 1.6, deck + 8.0, 0.12, ys0 + 1.84, deck + 10.5, CHROME)              # 雷达桅
    m.box(-1.0, ys0 + 1.62, deck + 9.6, 1.0, ys0 + 1.82, deck + 9.75, TRIM)                 # 雷达天线
    m.box(-0.2, ys0 + 1.52, deck + 10.5, 0.2, ys0 + 1.92, deck + 10.75, LAMP)
    m.cyl(0, ys1 - 1.8, deck + 5.4, deck + 9.5, 1.1, 10, TRIM)                               # 烟囱
    m.cyl(0, ys1 - 1.8, deck + 8.0, deck + 8.8, 1.12, 10, TAIL)                              # 烟囱色环
    # 救生艇：船楼两侧各一只，挂在两根吊臂上（吊臂贴着船楼侧墙）
    for sx in (-1, 1):
        xw = sx * 4.2
        for yy in (ys0 + 2.0, ys0 + 4.6):   # 吊臂从二层船楼侧墙（±3.6）伸出
            xa_, xb_ = sorted((sx * 3.6, xw + sx * 1.3))
            m.box(xa_, yy - 0.1, deck + 3.6, xb_, yy + 0.1, deck + 3.8, STEELC)
        xa, xb = sorted((xw + sx * 0.1, xw + sx * 1.3))
        m.box(xa, ys0 + 1.7, deck + 2.6, xb, ys0 + 4.9, deck + 3.6, LAMP2)
    return m.finish('container_ship')


def sts_crane():
    """岸桥：四条腿的门架（跨 18m）+ 横梁 + 伸向水面的长吊臂（-y 是海侧）+ 后拉杆 + 驾驶室 + 小车 + 吊具"""
    m = Part()
    SP, H, D = 9.0, 36.0, 8.0     # 门架半跨、横梁高、前后腿间距
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * SP, sy * D / 2
            m.box(x - 0.6, y - 0.6, 0, x + 0.6, y + 0.6, H, BODY)
            m.box(x - 1.0, y - 1.2, 0, x + 1.0, y + 1.2, 1.2, TRIM)        # 行走台车
        m.box(sx * SP - 0.5, -D / 2, 12.0, sx * SP + 0.5, D / 2, 13.0, BODY)
        # 斜撑
        m.hexa([(sx * SP - 0.3, -D / 2 + 0.6, 13.0), (sx * SP + 0.3, -D / 2 + 0.6, 13.0), (sx * SP + 0.3, -D / 2 + 1.4, 13.0), (sx * SP - 0.3, -D / 2 + 1.4, 13.0),
                                        (sx * SP - 0.3, D / 2 - 1.4, H - 1.0), (sx * SP + 0.3, D / 2 - 1.4, H - 1.0), (sx * SP + 0.3, D / 2 - 0.6, H - 1.0), (sx * SP - 0.3, D / 2 - 0.6, H - 1.0)], BODY)
    for sy in (-1, 1):
        m.box(-SP - 0.6, sy * D / 2 - 0.6, H, SP + 0.6, sy * D / 2 + 0.6, H + 1.6, BODY)
    # 吊臂：两根平行主梁，从后面 14m 伸到海侧 40m
    for sx in (-1, 1):
        m.box(sx * 2.0 - 0.5, -40.0, H + 1.6, sx * 2.0 + 0.5, 14.0, H + 3.2, BODY)
    for y in range(-38, 14, 6):
        m.box(-2.0, y - 0.25, H + 2.0, 2.0, y + 0.25, H + 2.6, BODY)
    # 塔头 + 拉杆
    m.box(-1.2, -0.8, H + 3.2, 1.2, 0.8, H + 14.0, BODY)
    for (y1, z1) in ((-36.0, H + 3.2), (12.0, H + 3.2)):
        for sx in (-1, 1):
            d = Vector((0, y1, z1 - (H + 13.0))).normalized()
            a = Vector((sx * 0.9, 0, H + 13.0)); b = Vector((sx * 2.0, y1, z1))
            n = Vector((1, 0, 0)) * 0.15; up = d.cross(Vector((1, 0, 0))).normalized() * 0.15
            c = [a - n - up, a + n - up, a + n + up, a - n + up, b - n - up, b + n - up, b + n + up, b - n + up]
            m.hexa([tuple(v) for v in (c[0], c[1], c[2], c[3], c[4], c[5], c[6], c[7])], CHROME)
    # 驾驶室 + 小车 + 吊具（吊着一个箱子的高度）
    m.box(-1.6, -14.0, H - 1.8, 1.6, -10.0, H + 1.6, TRIM)
    m.box(-1.5, -14.05, H - 1.2, 1.5, -13.98, H + 1.0, GLASS)
    m.box(-2.8, -16.0, H + 3.2, 2.8, -11.0, H + 4.4, TRIM)
    for sx in (-1, 1):
        m.box(sx * 1.0 - 0.04, -13.6, H - 14.0, sx * 1.0 + 0.04, -13.4, H + 3.2, BLACK)
    m.box(-1.5, -19.5, H - 14.8, 1.5, -7.5, H - 14.0, LAMP)
    m.box(-0.25, -38.2, H + 2.6, 0.25, -37.8, H + 3.0, TAIL)   # 吊臂尖警示灯，坐在最前面那根横撑上
    return m.finish('sts_crane')


bpy.ops.wm.read_factory_settings(use_empty=True)
report = {}
for fn in (yacht, tug, speedboat, container_ship, container, sts_crane):
    ob, rep = fn()
    report[ob.name] = rep
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=str(OUT / f'{ob.name}.fbx'), use_selection=True, path_mode='STRIP', axis_forward='-Z', axis_up='Y',
                                                                                                    apply_scale_options='FBX_SCALE_UNITS', mesh_smooth_type='FACE', bake_space_transform=True)
print('报告', json.dumps(report, ensure_ascii=False), flush=True)
