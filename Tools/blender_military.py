"""《霓虹巨兽》· 追兵（霓虹动力集团的无人军团，没有人）：Blender 程序化，真实尺寸米。
    Blender -b -P Tools/blender_military.py -- <输出目录>
每个单位导出一个 FBX，里面是几个独立物体（部件），物体原点就是转动中心，Unity 里按名字找部件做动作：
  body 车身 / 机身；turret 炮塔（绕竖轴转）；barrel 炮管（绕横轴俯仰、后坐）；launcher 发射架（抬起）；radar 雷达（转）；
  rotor / tail_rotor / rotor0..3 旋翼（转）；beacon 警灯（闪）；arm 维修吊臂；leg_l / leg_r / arm_l / arm_r / shield 机甲。
车身复用 blender_vehicles.py 的 build()（民用车同一套造型）。色板 UV 16 格：
  0 车身主色（Unity 按单位换）  1 集团深蓝  2 洋红条纹  3 枪灰  4 深灰  5 琥珀警灯（自发光）  6 浅灰  7 橄榄  8 黄黑警示
  9 玻璃  10 轮胎 / 履带  11 前灯（自发光）  12 红警灯（自发光）  13 蓝警灯（自发光）  14 装饰深色  15 镀铬
朝向：车头朝 Blender −Y（导出后 Unity 里转 180° 朝 +Z）。"""
import bpy, bmesh, sys, json, math, pathlib
from mathutils import Vector, Matrix

OUT = pathlib.Path(sys.argv[sys.argv.index('--') + 1]); OUT.mkdir(parents=True, exist_ok=True)
HERE = pathlib.Path(__file__).parent
exec(open(HERE / 'blender_mesh_checks.py', encoding='utf-8').read())
# 民用车的造型函数（只取定义部分，不跑它的导出）
src = open(HERE / 'blender_vehicles.py', encoding='utf-8').read()
exec(src[:src.index("bpy.ops.wm.read_factory_settings")])
VPart = Part
exec(open(HERE / 'blender_part.py', encoding='utf-8').read())   # 通用 Part（box / prism / cyl / slab，带 self.mat 变换）
GPart = Part

BODY, NAVY, MAG, GUN, DARK, AMBER, LGRAY, OLIVE, HAZ = 0, 1, 2, 3, 4, 5, 6, 7, 8
GLASS, TYRE, HEAD, RED, BLUE, TRIM, CHROME = 9, 10, 11, 12, 13, 14, 15

REPORT = {}
bpy.ops.wm.read_factory_settings(use_empty=True)
LODS = []


def part(name):
    p = GPart(); p.name = name; return p


def done(unit, parts):
    """parts: [(Part, 原点 Vector)]；部件几何在 Part 里是“相对原点”的坐标"""
    obs = []
    for p, origin in parts:
        ob, rep = p.finish(f'{unit}__{p.name}')
        ob.name = p.name; ob.data.name = f'{unit}_{p.name}'
        ob.location = origin
        REPORT[f'{unit}.{p.name}'] = rep
        obs.append(ob)
    bpy.ops.object.select_all(action='DESELECT')
    for ob in obs: ob.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    bpy.ops.export_scene.fbx(filepath=str(OUT / f'{unit}.fbx'), use_selection=True, path_mode='STRIP', axis_forward='-Z', axis_up='Y',
                             apply_scale_options='FBX_SCALE_UNITS', mesh_smooth_type='FACE', bake_space_transform=True)
    for ob in obs: bpy.data.objects.remove(ob)


def car_body(kind):
    """民用车车身当底盘（返回 bmesh 已建好的 Part 对象，名字 body）"""
    global Part
    P = dict(CARS[kind])
    Part = VPart   # build() 里用的是民用车那版 Part
    try: ob, rep = build(kind, P)
    finally: Part = GPart
    # 删掉 build 顺手建的 LOD
    for o in list(LODS): bpy.data.objects.remove(o)
    LODS.clear()
    return ob, rep, P


def stripe(p, x0, x1, y0, y1, z0, z1, pal=MAG):
    p.box(x0, y0, z0, x1, y1, z1, pal)


# ───────── 无人警车：白车身 + 深蓝下半 + 洋红条 + 红蓝警灯条 ─────────
def police():
    ob, rep, P = car_body('sedan'); ob.name = 'body'; REPORT['police.body'] = rep
    W, L = P['W'], P['L']
    deco = part('deco')   # 车门上的深蓝下半 + 洋红细条（贴在车身外 6mm）
    for sx in (-1, 1):
        x = sx * (W / 2 - 0.03)
        deco.box(x, -L / 2 + 0.5, P['clear'] + 0.25, x + sx * 0.008, L / 2 - 0.5, P['belt'] - 0.18, NAVY)
        deco.box(x + sx * 0.008, -L / 2 + 0.6, P['belt'] - 0.17, x + sx * 0.012, L / 2 - 0.6, P['belt'] - 0.12, MAG)
    beacon = part('beacon')
    beacon.box(-0.55, -0.12, 0, 0.55, 0.12, 0.05, TRIM)
    beacon.box(-0.52, -0.1, 0.05, -0.04, 0.1, 0.14, RED)
    beacon.box(0.04, -0.1, 0.05, 0.52, 0.1, 0.14, BLUE)
    beacon.box(-0.04, -0.1, 0.05, 0.04, 0.1, 0.12, TRIM)
    yroof = -L / 2 + P['hood'] + P['ws'] + P['roof'] * 0.45
    # 11-12 用户：警车会开火，但车上没有任何武器 —— 车顶后部加一座遥控武器站：转台（turret，绕竖轴转）+ 双管机枪（barrel，俯仰）
    turret = part('turret')
    turret.cyl(0, 0, 0, 0.1, 0.3, 10, DARK)                      # 转台座圈
    turret.box(-0.3, -0.26, 0.1, 0.3, 0.3, 0.36, GUN)            # 枪塔外壳
    turret.box(-0.36, -0.1, 0.14, -0.3, 0.22, 0.32, MAG)         # 两侧洋红识别条
    turret.box(0.3, -0.1, 0.14, 0.36, 0.22, 0.32, MAG)
    turret.box(-0.12, 0.02, 0.36, 0.12, 0.24, 0.46, DARK)        # 顶上的瞄准镜盒
    turret.box(-0.08, -0.02, 0.38, 0.08, 0.02, 0.44, HEAD)       # 镜头（自发光）
    barrel = part('barrel')
    barrel.box(-0.2, -0.12, -0.07, 0.2, 0.1, 0.07, DARK)         # 枪架
    barrel.mat = Matrix.Rotation(math.pi / 2, 4, 'X')
    for sx in (-1, 1):
        barrel.cyl(sx * 0.11, 0, 0.1, 0.95, 0.045, 8, GUN)       # 双管
        barrel.cyl(sx * 0.11, 0, 0.82, 0.98, 0.062, 8, DARK)     # 枪口制退器
    barrel.mat = Matrix.Identity(4)
    yt = yroof + min(0.62, P['roof'] * 0.3)
    obs = [ob]
    for p, o in ((deco, Vector((0, 0, 0))), (beacon, Vector((0, yroof, P['H']))), (turret, Vector((0, yt, P['H']))), (barrel, Vector((0, yt - 0.26, P['H'] + 0.23)))):
        o2, r2 = p.finish('police__' + p.name); o2.name = p.name; o2.location = o; obs.append(o2); REPORT['police.' + p.name] = r2
    export('police', obs)


def export(unit, obs):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in obs: ob.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    bpy.ops.export_scene.fbx(filepath=str(OUT / f'{unit}.fbx'), use_selection=True, path_mode='STRIP', axis_forward='-Z', axis_up='Y',
                             apply_scale_options='FBX_SCALE_UNITS', mesh_smooth_type='FACE', bake_space_transform=True)
    for ob in obs: bpy.data.objects.remove(ob)


# ───────── 维修车：黄色面包车 + 琥珀警灯 + 车顶折叠吊臂 ─────────
def repair():
    ob, rep, P = car_body('van'); ob.name = 'body'; REPORT['repair.body'] = rep
    W, L, H = P['W'], P['L'], P['H']
    deco = part('deco')
    for sx in (-1, 1):
        x = sx * (W / 2 - 0.03)
        for k in range(6):   # 黄黑斜纹贴条
            y = -L / 2 + 0.9 + k * 0.5
            deco.box(x, y, P['clear'] + 0.3, x + sx * 0.008, y + 0.25, P['clear'] + 0.5, HAZ if k % 2 == 0 else TRIM)
    beacon = part('beacon')
    beacon.box(-0.3, -0.1, 0, 0.3, 0.1, 0.04, TRIM)
    for sx in (-1, 1): beacon.cyl(sx * 0.18, 0, 0.04, 0.16, 0.08, 10, AMBER)
    arm = part('arm')   # 吊臂：底座转台 + 两节臂 + 吊钩（折叠在车顶，原点在转台）
    arm.cyl(0, 0, 0, 0.12, 0.28, 12, DARK)
    arm.box(-0.09, -0.09, 0.12, 0.09, 1.7, 0.28, HAZ)
    arm.box(-0.07, 1.6, 0.06, 0.07, 1.78, 0.3, DARK)
    arm.box(-0.02, 1.66, -0.25, 0.02, 1.72, 0.06, CHROME)
    arm.box(-0.08, 1.62, -0.33, 0.08, 1.76, -0.25, DARK)
    obs = [ob]
    for p, o in ((deco, Vector((0, 0, 0))), (beacon, Vector((0, -L / 2 + 1.0, H))), (arm, Vector((0, -L / 2 + 1.6, H)))):
        o2, r2 = p.finish('repair__' + p.name); o2.name = p.name; o2.location = o; obs.append(o2); REPORT['repair.' + p.name] = r2
    export('repair', obs)


def wheels(p, xs, ys, r, w, z, pal=TYRE, hub=GUN):
    for x in xs:
        for y in ys:
            p.mat = Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.pi / 2, 4, 'Y')
            p.cyl(0, 0, -w / 2, w / 2, r, 12, pal)
            p.cyl(0, 0, w / 2 if x > 0 else -w / 2 - 0.02, w / 2 + 0.02 if x > 0 else -w / 2, r * 0.55, 8, hub)
    p.mat = Matrix.Identity(4)


# ───────── 装甲运兵车：八轮、斜面车体、小炮塔 ─────────
def apc():
    L, W = 7.2, 2.7
    body = part('body')
    # 侧面轮廓 (y, z)：从车头顶部开始逆时针（从 +x 看），跟民用车车身同一个方向约定
    prof = [(-L / 2, 0.9), (-L / 2 + 0.7, 0.5), (L / 2 - 0.5, 0.5), (L / 2, 0.7), (L / 2, 1.6), (L / 2 - 0.3, 1.95), (-L / 2 + 1.1, 1.85)]
    VPart.prism_x(body, prof, -W / 2, W / 2, BODY)
    for sx in (-1, 1):
        body.box(sx * W / 2, -L / 2 + 1.2, 1.2, sx * (W / 2 + 0.01), L / 2 - 0.5, 1.32, MAG)       # 洋红条
        body.box(sx * W / 2, -L / 2 + 1.3, 0.62, sx * (W / 2 + 0.06), L / 2 - 0.6, 0.72, DARK)    # 侧挡泥板
    sl = lambda y: 0.9 + (y + L / 2) * 0.95 / 1.1   # 车头上斜面高度
    body.slab((-L / 2 + 0.25, sl(-L / 2 + 0.25) - 0.01), (-L / 2 + 0.95, sl(-L / 2 + 0.95) - 0.01), 1.6, 0.03, GLASS, 0.1)   # 传感器窗（贴在车头斜面上）
    for sx in (-1, 1): body.box(sx * 0.95, -L / 2 + 0.28, 0.64, sx * 0.7, -L / 2 + 0.42, 0.78, HEAD)   # 车头下斜面上
    for y in (-2.4, -0.9, 0.9, 2.4):
        body.box(-W / 2 + 0.2, y - 0.08, 0.55, W / 2 - 0.2, y + 0.08, 0.8, DARK)   # 车轴
    wheels(body, (-W / 2 - 0.12 + 0.2, W / 2 + 0.12 - 0.2), (-2.4, -0.9, 0.9, 2.4), 0.55, 0.42, 0.55)
    turret = part('turret')
    turret.cyl(0, 0, 0, 0.45, 0.85, 10, BODY, 0.65)
    turret.box(-0.25, -1.0, 0.1, 0.25, -0.6, 0.35, DARK)
    barrel = part('barrel')
    barrel.mat = Matrix.Rotation(math.pi / 2, 4, 'X')
    barrel.cyl(0, 0, 0, 1.6, 0.07, 8, GUN); barrel.cyl(0, 0, 1.45, 1.65, 0.1, 8, DARK)
    barrel.mat = Matrix.Identity(4)
    parts = [(body, Vector((0, 0, 0))), (turret, Vector((0, 0.4, 1.9))), (barrel, Vector((0, 0.0, 2.15)))]
    finish_export('apc', parts)


def finish_export(unit, parts):
    obs = []
    for p, o in parts:
        ob, rep = p.finish(f'{unit}__{p.name}'); ob.name = p.name; ob.location = o; obs.append(ob); REPORT[f'{unit}.{p.name}'] = rep
    export(unit, obs)


# ───────── 坦克：履带 + 负重轮 + 楔形车体 + 大炮塔 + 长炮管 ─────────
def tank():
    L, W = 7.6, 3.5
    body = part('body')
    body.box(-W / 2 + 0.75, -L / 2 + 0.3, 0.55, W / 2 - 0.75, L / 2 - 0.3, 1.75, BODY)   # 车体
    body.slab((-L / 2 + 0.3, 1.0), (-L / 2 + 1.6, 1.78), W - 1.6, 0.05, BODY)              # 首上倾斜装甲
    for sx in (-1, 1):
        xo = sx * (W / 2 - 0.4)
        body.box(xo - 0.38, -L / 2, 0.15, xo + 0.38, L / 2, 0.42, TYRE)                     # 履带下段
        body.box(xo - 0.38, -L / 2 + 0.35, 0.95, xo + 0.38, L / 2 - 0.2, 1.1, TYRE)         # 履带上段
        body.box(xo - 0.4, -L / 2 + 0.1, 1.1, xo + 0.4, L / 2, 1.2, BODY)                   # 挡泥板
        body.box(xo + sx * 0.38, -L / 2 + 0.6, 0.55, xo + sx * 0.45, L / 2 - 0.4, 1.14, NAVY) # 侧裙板（顶到挡泥板）
        body.box(xo + sx * 0.45, -L / 2 + 0.8, 0.75, xo + sx * 0.46, L / 2 - 0.6, 0.85, MAG)
        body.mat = Matrix.Identity(4)
        for k in range(6):
            y = -L / 2 + 0.9 + k * 1.15
            body.mat = Matrix.Translation((xo, y, 0.5)) @ Matrix.Rotation(math.pi / 2, 4, 'Y')
            body.cyl(0, 0, -0.32, 0.32, 0.36, 10, GUN)
        body.mat = Matrix.Identity(4)
        body.box(xo - 0.3, -L / 2 + 0.6, 0.42, xo + 0.3, L / 2 - 0.6, 0.95, DARK)           # 履带内侧（连起轮子和上段）
    for sx in (-1, 1): body.box(sx * 1.0, -L / 2 + 0.25, 1.4, sx * 0.75, -L / 2 + 0.35, 1.55, HEAD)
    turret = part('turret')
    turret.prism([(-1.4, -1.6), (1.4, -1.6), (1.6, 0.4), (1.2, 1.5), (-1.2, 1.5), (-1.6, 0.4)][::-1], 0, 0.75, BODY,
                 [(-1.2, -1.3), (1.2, -1.3), (1.4, 0.4), (1.05, 1.35), (-1.05, 1.35), (-1.4, 0.4)][::-1])
    turret.box(-1.25, -0.5, 0.75, -0.6, 0.6, 1.05, DARK)        # 传感器舱
    turret.box(0.5, 0.2, 0.75, 1.0, 0.9, 0.95, DARK)
    turret.box(-0.1, -1.1, 0.74, 0.1, 1.1, 0.78, MAG)
    turret.box(-0.5, -1.62, 0.15, 0.5, -1.2, 0.6, DARK)         # 炮盾
    barrel = part('barrel')
    barrel.mat = Matrix.Rotation(math.pi / 2, 4, 'X')
    barrel.cyl(0, 0, 0, 4.2, 0.13, 10, GUN, 0.11)
    barrel.cyl(0, 0, 1.6, 2.2, 0.2, 10, DARK)                   # 抽烟装置
    barrel.cyl(0, 0, 4.0, 4.4, 0.16, 10, DARK)                  # 炮口
    barrel.mat = Matrix.Identity(4)
    finish_export('tank', [(body, Vector((0, 0, 0))), (turret, Vector((0, 0.2, 1.75))), (barrel, Vector((0, -1.3, 2.15)))])


# ───────── 导弹车 / 防空导弹：卡车底盘（皮卡车身）+ 发射箱 / 雷达 ─────────
def rocket(unit='rocket'):
    ob, rep, P = car_body('pickup'); ob.name = 'body'; REPORT[unit + '.body'] = rep
    W, L = P['W'], P['L']
    cab = -L / 2 + P['hood'] + P['ws'] + P['roof'] + 0.1
    deco = part('deco')
    for sx in (-1, 1): deco.box(sx * (W / 2 - 0.03), -L / 2 + 0.4, P['belt'] - 0.2, sx * (W / 2 - 0.022), cab - 0.2, P['belt'] - 0.12, MAG)
    launcher = part('launcher')   # 原点在后端铰链；平放时箱子朝前（−y），抬起绕 x 轴转
    launcher.box(-0.85, -2.3, 0.0, 0.85, 0.0, 0.75, BODY)
    for ix in range(3):
        for iz in range(2):
            x = -0.55 + ix * 0.55; z = 0.2 + iz * 0.35
            launcher.mat = Matrix.Translation((x, -2.3, z)) @ Matrix.Rotation(math.pi / 2, 4, 'X')
            launcher.cyl(0, 0, -0.02, 0.0, 0.14, 8, DARK)      # 发射管口
            launcher.cyl(0, 0, -0.03, -0.02, 0.07, 8, RED)     # 管里的导弹头
    launcher.mat = Matrix.Identity(4)
    launcher.box(-0.86, -1.4, 0.3, 0.86, -1.3, 0.45, MAG)
    extra = []
    if unit == 'sam':
        radar = part('radar')   # 转台 + 竖杆 + 抛物面（八边形碟）
        radar.cyl(0, 0, 0, 0.25, 0.3, 8, DARK)
        radar.box(-0.06, -0.06, 0.25, 0.06, 0.06, 1.1, GUN)
        radar.mat = Matrix.Translation((0, -0.05, 1.25)) @ Matrix.Rotation(math.pi / 2, 4, 'X')
        radar.cyl(0, 0, 0, 0.1, 0.65, 8, LGRAY, 0.45)
        radar.mat = Matrix.Identity(4)
        extra = [(radar, Vector((0, cab + 0.4, P['belt'])))]
    obs = [ob]
    for p, o in [(deco, Vector((0, 0, 0))), (launcher, Vector((0, L / 2 - 0.1, P['belt'] + 0.05)))] + extra:
        o2, r2 = p.finish(unit + '__' + p.name); o2.name = p.name; o2.location = o; obs.append(o2); REPORT[unit + '.' + p.name] = r2
    export(unit, obs)


# ───────── 巡逻艇：船体 + 驾驶舱 + 前甲板机枪 ─────────
def boat():
    L, W = 9.0, 2.8
    body = part('body')
    hull = [(-W / 2, L / 2), (W / 2, L / 2), (W / 2, -L / 2 + 2.2), (0, -L / 2), (-W / 2, -L / 2 + 2.2)]
    body.prism(hull, 0, 1.2, BODY, [(x * 0.92, y * 0.96) for (x, y) in hull])
    body.prism([(x * 0.94, y * 0.97) for (x, y) in hull], -0.35, 0, NAVY, [(x * 1.0, y * 1.0) for (x, y) in hull])   # 吃水线下深蓝
    body.box(-W / 2 + 0.3, -0.4, 1.2, W / 2 - 0.3, 2.6, 2.4, LGRAY)                   # 驾驶舱
    body.box(-W / 2 + 0.35, -0.42, 1.8, W / 2 - 0.35, -0.38, 2.25, GLASS)
    for sx in (-1, 1): body.box(sx * (W / 2 - 0.3), 0.0, 1.85, sx * (W / 2 - 0.27), 2.3, 2.2, GLASS)
    body.box(-0.08, 1.0, 2.4, 0.08, 1.16, 3.6, GUN)                                   # 桅杆
    body.box(-0.3, 1.0, 3.3, 0.3, 1.16, 3.4, GUN)
    body.box(-0.25, 1.02, 3.6, 0.25, 1.14, 3.75, BLUE)
    for sx in (-1, 1): body.box(sx * (W / 2 - 0.15), -L / 2 + 2.5, 0.7, sx * (W / 2 - 0.04), L / 2 - 0.3, 0.85, MAG)
    turret = part('turret')
    turret.cyl(0, 0, 0, 0.35, 0.4, 8, DARK)
    turret.box(-0.25, -0.2, 0.35, 0.25, 0.3, 0.7, GUN)
    barrel = part('barrel')
    barrel.mat = Matrix.Rotation(math.pi / 2, 4, 'X'); barrel.cyl(0, 0, 0, 1.3, 0.06, 8, GUN); barrel.mat = Matrix.Identity(4)
    finish_export('boat', [(body, Vector((0, 0, 0))), (turret, Vector((0, -L / 2 + 2.6, 1.2))), (barrel, Vector((0, -L / 2 + 2.45, 1.75)))])


# ───────── 武装直升机：流线机身 + 短翼挂架 + 尾梁 + 起落橇 + 主旋翼 + 尾桨 ─────────
def heli():
    body = part('body')
    body.prism([(-0.9, -2.4), (0.9, -2.4), (1.1, 0.0), (0.8, 2.2), (-0.8, 2.2), (-1.1, 0.0)][::-1], 0.5, 2.3, BODY,
               [(-0.6, -1.8), (0.6, -1.8), (0.8, 0.0), (0.6, 2.0), (-0.6, 2.0), (-0.8, 0.0)][::-1])
    body.slab((-2.4, 1.2), (-1.0, 2.25), 1.3, 0.05, GLASS)
    body.prism([(-0.35, 2.0), (0.35, 2.0), (0.15, 7.6), (-0.15, 7.6)][::-1], 1.3, 2.1, BODY, [(-0.3, 2.0), (0.3, 2.0), (0.12, 7.6), (-0.12, 7.6)][::-1])   # 尾梁
    body.box(-0.08, 7.0, 1.6, 0.08, 7.8, 3.2, BODY)          # 垂尾
    body.box(-1.0, 6.6, 1.65, 1.0, 7.2, 1.75, BODY)          # 平尾
    body.box(-0.5, -0.3, 2.3, 0.5, 1.2, 2.75, DARK)          # 发动机舱
    body.box(-0.1, 0.3, 2.75, 0.1, 0.5, 2.95, GUN)           # 旋翼轴
    for sx in (-1, 1):
        body.box(sx * 0.9, -0.3, 1.2, sx * 2.0, 0.5, 1.3, BODY)             # 短翼
        body.mat = Matrix.Translation((sx * 1.75, -0.2, 1.0)) @ Matrix.Rotation(math.pi / 2, 4, 'X')
        body.cyl(0, 0, -0.8, 0.8, 0.16, 8, GUN)                             # 火箭巢
        body.cyl(0, 0, -0.85, -0.8, 0.1, 8, RED)
        body.mat = Matrix.Identity(4)
        body.box(sx * 1.75 - 0.03, -0.1, 1.15, sx * 1.75 + 0.03, 0.1, 1.2, GUN)   # 挂架连接
        body.box(sx * 0.9, -1.6, 0.05, sx * 0.98, 1.6, 0.13, GUN)            # 起落橇
        for y in (-0.9, 0.9): body.box(sx * 0.92, y - 0.05, 0.13, sx * 0.84, y + 0.05, 0.6, GUN)
        body.box(sx * 1.08, -1.0, 1.2, sx * 1.1, 1.2, 1.35, MAG)
    body.box(-0.25, -2.45, 0.65, 0.25, -2.35, 0.95, HEAD)
    rotor = part('rotor')
    rotor.cyl(0, 0, 0, 0.18, 0.22, 8, DARK)
    for k in range(4):
        rotor.mat = Matrix.Rotation(k * math.pi / 2, 4, 'Z')
        rotor.box(0.15, -0.14, 0.04, 5.4, 0.14, 0.11, GUN)
    rotor.mat = Matrix.Identity(4)
    tail = part('tail_rotor')
    tail.mat = Matrix.Rotation(math.pi / 2, 4, 'Y'); tail.cyl(0, 0, -0.06, 0.06, 0.1, 6, DARK); tail.mat = Matrix.Identity(4)
    for k in range(2):
        tail.mat = Matrix.Rotation(k * math.pi / 2, 4, 'X')
        tail.box(-0.04, -0.06, -0.9, 0.04, 0.06, 0.9, GUN)
    tail.mat = Matrix.Identity(4)
    finish_export('heli', [(body, Vector((0, 0, 0))), (rotor, Vector((0, 0.4, 2.95))), (tail, Vector((0.18, 7.4, 2.6)))])


# ───────── 自爆无人机：四旋翼，机腹挂一个红色弹头 ─────────
def drone():
    body = part('body')
    body.prism([(-0.25, -0.35), (0.25, -0.35), (0.35, 0.0), (0.25, 0.35), (-0.25, 0.35), (-0.35, 0.0)][::-1], 0, 0.22, BODY)
    body.cyl(0, 0, -0.25, 0.0, 0.16, 8, RED, 0.1)
    body.box(-0.1, -0.37, 0.06, 0.1, -0.33, 0.15, HEAD)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        body.mat = Matrix.Rotation(a, 4, 'Z')
        body.box(0.2, -0.04, 0.08, 0.7, 0.04, 0.16, DARK)
        body.cyl(0.72, 0, 0.0, 0.2, 0.07, 8, GUN)
    body.mat = Matrix.Identity(4)
    parts = [(body, Vector((0, 0, 0)))]
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        r = part(f'rotor{k}')
        r.box(-0.32, -0.04, 0, 0.32, 0.04, 0.025, LGRAY)
        parts.append((r, Vector((0.72 * math.cos(a), 0.72 * math.sin(a), 0.2))))
    finish_export('drone', parts)


# ───────── 战斗机：细长机身 + 三角翼 + 双垂尾 + 座舱（无人：深色传感器罩）─────────
def jet():
    body = part('body')
    body.prism([(-0.6, -5.0), (0.6, -5.0), (0.9, 4.5), (-0.9, 4.5)][::-1], 0.6, 1.6, BODY, [(-0.4, -4.6), (0.4, -4.6), (0.7, 4.5), (-0.7, 4.5)][::-1])
    body.prism([(-0.6, -5.0), (0.6, -5.0), (0.0, -7.0)][::-1], 0.75, 1.45, BODY, [(-0.4, -4.6), (0.4, -4.6), (0.0, -6.8)][::-1])   # 机头
    body.slab((-4.8, 1.55), (-2.5, 2.05), 0.7, 0.05, GLASS)
    body.box(-0.35, -2.5, 1.6, 0.35, 0.0, 1.95, BODY)
    for sx in (-1, 1):
        body.prism([(sx * 0.8, -1.0), (sx * 0.85, 3.5), (sx * 4.6, 3.5), (sx * 4.6, 2.6)][::(1 if sx > 0 else -1)], 0.95, 1.1, BODY)   # 主翼
        body.box(sx * 4.45, 2.6, 1.1, sx * 4.6, 3.4, 1.18, MAG)
        body.prism([(sx * 0.7, 3.2), (sx * 0.75, 4.6), (sx * 2.2, 4.6), (sx * 2.2, 4.1)][::(1 if sx > 0 else -1)], 0.95, 1.08, BODY)   # 平尾
        body.box(sx * 0.55 - 0.05, 3.0, 1.6, sx * 0.55 + 0.05, 4.4, 3.2, BODY)       # 垂尾
        body.box(sx * 0.55 - 0.06, 3.6, 2.6, sx * 0.55 + 0.06, 4.3, 2.75, MAG)
        body.mat = Matrix.Translation((sx * 0.4, 4.5, 1.1)) @ Matrix.Rotation(math.pi / 2, 4, 'X')
        body.cyl(0, 0, 0, 0.3, 0.32, 8, DARK); body.cyl(0, 0, 0.3, 0.36, 0.22, 8, HEAD)   # 尾喷口
        body.mat = Matrix.Identity(4)
    finish_export('jet', [(body, Vector((0, 0, 0)))])


# ───────── 轰炸机：飞翼 ─────────
def bomber():
    body = part('body')
    wing = [(0.0, -4.5), (11.0, 3.0), (10.0, 4.0), (5.0, 2.5), (3.0, 3.8), (0.0, 2.6), (-3.0, 3.8), (-5.0, 2.5), (-10.0, 4.0), (-11.0, 3.0)]
    body.prism(wing[::-1], 0.8, 1.3, BODY, [(x * 0.96, y * 0.96) for (x, y) in wing][::-1])
    body.prism([(-1.4, -4.0), (1.4, -4.0), (1.8, 2.0), (-1.8, 2.0)][::-1], 1.3, 2.0, BODY, [(-1.0, -3.5), (1.0, -3.5), (1.4, 1.8), (-1.4, 1.8)][::-1])
    body.slab((-3.6, 1.9), (-2.0, 2.15), 1.6, 0.04, GLASS)
    for sx in (-1, 1):
        body.box(sx * 2.4, -0.5, 1.3, sx * 3.4, 1.8, 1.7, DARK)                          # 发动机进气
        body.box(sx * 2.5, 1.8, 1.35, sx * 3.3, 1.9, 1.62, HEAD)
        body.box(sx * 9.6, 2.9, 1.3, sx * 10.5, 3.6, 1.36, MAG)
    body.box(-1.0, -1.5, 0.75, 1.0, 1.0, 0.8, DARK)                                       # 弹舱门
    finish_export('bomber', [(body, Vector((0, 0, 0)))])


# ───────── 机甲（最终 BOSS，11-13 重做：用户“最后的机器人效果太差，做精致一些”）─────────
# 原来是四十来个方块、腿和胳膊各一整根、身宽比马路还宽。现在：
#   · 16 个可动部件，原点都在关节上：pelvis 胯 / torso 上身（腰可以转、可以前倾）/ head 头（转向玩家）/ eye 目镜、core 胸口反应堆（单独的发光件，
#     Unity 里可以单独调亮度）/ leg_l·r 大腿 → shin_l·r 小腿 → foot_l·r 脚（膝、踝能弯）/ arm_l·r 上臂 → fore_l 左前臂（拳）、fore_r 右前臂（加农炮）/
#     shield 塔盾（挂在左拳上）、shield_glow 盾缘的能量边（单独的发光件）；
#   · 集团涂装：钢蓝主体 + 白色装甲板（胸、肩、膝、大腿、盾面）+ 洋红识别条 + 黄黑警示（脚尖、膝、盾底）；
#   · 细节：胸口反应堆的环形座、背包 + 两个推进器喷口 + 散热格栅 + 排气管、右肩六联装导弹巢、天线、头顶洋红冠、耳侧传感器、
#     大腿后的液压杆、小腿后的散热块、脚跟支撑、炮管散热环 + 炮口制退器、盾上的观察窗和徽记；
#   · 比例改瘦高（宽 4.2 × 高 8.0）：Unity 里按马路宽度定大小，走在路中间不蹭两边的楼。
# 朝向：正面朝 −Y。左 = −X。
def mech():
    I = Matrix.Identity(4)
    def xcyl(p, x0, x1, y, z, r, seg, pal, r1=None):          # 沿 X 轴的圆柱
        p.mat = Matrix.Translation((0, y, z)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'); p.cyl(0, 0, x0, x1, r, seg, pal, r1); p.mat = I
    def ycyl(p, y0, y1, x, z, r, seg, pal, r1=None):          # 沿 Y 轴的圆柱（y0 < y1）
        p.mat = Matrix.Translation((x, 0, z)) @ Matrix.Rotation(-math.pi / 2, 4, 'X'); p.cyl(0, 0, y0, y1, r, seg, pal, r1); p.mat = I
    def hexr(w0, w1, yf, ym, yb, k=1.0):                       # 六边形截面：前宽 w0、最宽 w1（在 ym 处）、后宽 w0×k
        return [(-w0, yf), (w0, yf), (w1, ym), (w0 * k, yb), (-w0 * k, yb), (-w1, ym)]

    HIP, KNEE, ANK, WAIST, NECK, SHO, ELB = 3.75, 2.2, 0.55, 4.3, 6.95, 6.15, 4.75
    LX, SX = 0.8, 1.6                                          # 髋、肩离中线
    parts = []

    # 胯
    pelvis = part('pelvis')
    pelvis.box(-0.55, -0.5, -0.35, 0.55, 0.5, 0.38, GUN)
    xcyl(pelvis, -1.22, 1.22, 0, 0, 0.36, 10, DARK)                         # 髋轴
    pelvis.box(-0.4, -0.7, -0.6, 0.4, -0.45, 0.25, BODY)                    # 前护裆
    pelvis.box(-0.12, -0.74, -0.5, 0.12, -0.62, 0.15, MAG)
    pelvis.box(-0.5, 0.45, -0.52, 0.5, 0.72, 0.2, NAVY)                     # 后裙
    for sx in (-1, 1): pelvis.box(sx * 1.12, -0.42, -0.62, sx * 1.3, 0.42, 0.34, LGRAY)   # 髋侧装甲
    pelvis.cyl(0, 0, 0.3, 0.62, 0.5, 10, DARK)                              # 腰环
    parts.append((pelvis, Vector((0, 0, HIP))))

    # 上身（原点在腰）
    torso = part('torso')
    torso.cyl(0, 0, -0.12, 0.62, 0.6, 8, GUN)                               # 腹
    torso.cyl(0, 0, 0.14, 0.3, 0.67, 8, DARK)
    torso.prism(hexr(0.95, 1.15, -0.7, 0.2, 0.85, 0.85), 0.5, 2.3, BODY, hexr(1.35, 1.55, -0.95, 0.25, 1.0, 0.78))   # 胸（上宽下窄）
    for sx in (-1, 1):
        torso.box(sx * 0.2, -1.16, 1.5, sx * 1.3, -0.84, 2.2, LGRAY)        # 胸甲（左右两块，中间露出反应堆）
        torso.box(sx * 0.35, -1.2, 1.95, sx * 1.15, -1.1, 2.08, MAG)
        torso.box(sx * 0.3, -1.0, 0.75, sx * 0.9, -0.7, 1.3, NAVY)          # 腹甲
        xcyl(torso, sx * 1.3 if sx > 0 else sx * 1.72, sx * 1.72 if sx > 0 else sx * 1.3, 0.05, 1.85, 0.42, 10, GUN)   # 肩座
        torso.cyl(sx * 0.5, 1.15, 0.2, 0.82, 0.3, 8, GUN, 0.2)              # 推进器喷口（上细下粗）
        torso.cyl(sx * 0.5, 1.15, 0.12, 0.3, 0.2, 8, AMBER)
        torso.box(sx * 0.82, 0.95, 1.0, sx * 0.92, 1.3, 1.9, MAG)           # 背包侧面识别条
        torso.cyl(sx * 0.6, 1.1, 2.1, 2.75, 0.14, 6, GUN)                   # 排气管
        torso.cyl(sx * 0.6, 1.1, 2.68, 2.86, 0.18, 6, DARK)
    ycyl(torso, -1.14, -0.6, 0, 1.3, 0.44, 10, DARK)                        # 反应堆座（环）
    torso.box(-0.72, -0.62, 2.22, 0.72, 0.62, 2.5, NAVY)                    # 领
    torso.cyl(0, 0, 2.4, 2.7, 0.34, 8, DARK)                                # 颈
    torso.box(-0.85, 0.78, 0.7, 0.85, 1.45, 2.2, NAVY)                      # 背包
    for k in range(3): torso.box(-0.6, 1.42, 1.0 + k * 0.36, 0.6, 1.52, 1.2 + k * 0.36, DARK)   # 散热格栅
    torso.box(0.5, 0.15, 2.3, 1.38, 1.0, 2.92, NAVY)                        # 右肩导弹巢
    for i in range(3):
        for j in range(2): torso.box(0.6 + i * 0.25, 0.08, 2.4 + j * 0.25, 0.78 + i * 0.25, 0.3, 2.58 + j * 0.25, DARK)
    torso.box(0.52, 0.5, 2.9, 1.36, 0.62, 2.97, HAZ)
    torso.cyl(-0.95, 0.85, 2.15, 3.7, 0.045, 5, CHROME)                     # 天线
    torso.box(-1.01, 0.79, 3.6, -0.89, 0.91, 3.78, RED)
    parts.append((torso, Vector((0, 0, WAIST))))
    core = part('core'); ycyl(core, -1.2, -1.02, 0, 0, 0.3, 10, AMBER)      # 反应堆（发光）
    parts.append((core, Vector((0, 0, WAIST + 1.3))))

    # 头（原点在颈）
    head = part('head')
    head.cyl(0, 0, -0.2, 0.2, 0.25, 8, DARK)
    head.prism(hexr(0.45, 0.55, -0.55, 0.1, 0.55, 0.9), 0.1, 0.75, NAVY, hexr(0.35, 0.45, -0.45, 0.1, 0.45, 0.85))
    head.box(-0.43, -0.67, 0.27, 0.43, -0.48, 0.57, DARK)                   # 目镜框
    head.box(-0.06, -0.52, 0.7, 0.06, 0.5, 0.98, MAG)                       # 头冠
    head.box(-0.3, -0.63, 0.04, 0.3, -0.4, 0.3, LGRAY)                      # 下颌
    for sx in (-1, 1): xcyl(head, sx * 0.48 if sx > 0 else sx * 0.7, sx * 0.7 if sx > 0 else sx * 0.48, 0, 0.4, 0.16, 8, GUN)   # 耳侧传感器
    parts.append((head, Vector((0, 0, NECK))))
    eye = part('eye'); eye.box(-0.37, -0.71, 0.34, 0.37, -0.64, 0.5, RED)
    parts.append((eye, Vector((0, 0, NECK))))

    for side, sx in (('l', -1), ('r', 1)):
        # 大腿（原点在髋）
        leg = part('leg_' + side)
        xcyl(leg, -0.36, 0.36, 0, 0, 0.42, 10, DARK)
        leg.box(-0.34, -0.4, -1.45, 0.34, 0.4, -0.1, BODY)
        leg.box(-0.38, -0.54, -1.25, 0.38, -0.36, -0.25, LGRAY)             # 前装甲
        leg.box(-0.1, -0.58, -1.1, 0.1, -0.5, -0.4, MAG)
        leg.box(min(sx * 0.3, sx * 0.45), -0.3, -1.2, max(sx * 0.3, sx * 0.45), 0.3, -0.3, NAVY)   # 外侧装甲
        leg.cyl(0, 0.45, -1.4, -0.25, 0.09, 6, CHROME)                      # 液压杆
        xcyl(leg, -0.4, 0.4, 0, -1.55, 0.3, 10, GUN)                        # 膝轴
        parts.append((leg, Vector((sx * LX, 0, HIP))))
        # 小腿（原点在膝）
        shin = part('shin_' + side)
        shin.box(-0.37, -0.64, -0.3, 0.37, -0.22, 0.36, LGRAY)              # 护膝
        shin.box(-0.3, -0.69, -0.06, 0.3, -0.6, 0.1, HAZ)
        r0 = [(-0.42, -0.45), (0.42, -0.45), (0.42, 0.55), (-0.42, 0.55)]; r1 = [(-0.3, -0.35), (0.3, -0.35), (0.3, 0.35), (-0.3, 0.35)]
        shin.prism(r0, -1.5, 0.1, BODY, r1)                                 # 小腿（下粗上细）
        shin.box(-0.3, -0.6, -1.35, 0.3, -0.3, -0.42, NAVY)                 # 胫甲
        shin.box(-0.06, -0.65, -1.25, 0.06, -0.55, -0.52, MAG)
        shin.box(-0.25, 0.4, -1.3, 0.25, 0.7, -0.6, DARK)                   # 小腿后的散热块
        xcyl(shin, -0.46, 0.46, 0.05, -1.65, 0.26, 10, DARK)                # 踝轴
        parts.append((shin, Vector((sx * LX, 0, KNEE))))
        # 脚（原点在踝）
        foot = part('foot_' + side)
        foot.prism(hexr(0.5, 0.6, -1.15, -0.2, 0.75), -0.55, -0.3, DARK)    # 脚底
        foot.prism(hexr(0.42, 0.5, -1.0, -0.2, 0.6), -0.33, 0.02, BODY, hexr(0.3, 0.36, -0.5, -0.1, 0.4))
        foot.box(-0.44, -1.2, -0.5, 0.44, -0.86, -0.12, HAZ)                # 脚尖
        foot.box(-0.2, 0.6, -0.5, 0.2, 0.96, -0.2, GUN)                     # 脚跟支撑
        foot.cyl(0, 0.02, -0.1, 0.22, 0.3, 8, GUN)
        parts.append((foot, Vector((sx * LX, 0, ANK))))
        # 上臂（原点在肩）
        arm = part('arm_' + side)
        xcyl(arm, -0.3, 0.3, 0, 0, 0.4, 10, DARK)
        px0, px1 = sorted((sx * -0.4, sx * 0.5))
        arm.box(px0, -0.62, 0.0, px1, 0.62, 0.55, LGRAY)                    # 肩甲
        arm.box(px0 + 0.1, -0.5, 0.5, px1 - 0.1, 0.5, 0.72, NAVY)
        arm.box(min(sx * 0.44, sx * 0.56), -0.5, 0.1, max(sx * 0.44, sx * 0.56), 0.5, 0.26, MAG)
        arm.box(-0.26, -0.28, -1.25, 0.26, 0.28, -0.1, BODY)
        xcyl(arm, -0.34, 0.34, 0, -1.4, 0.27, 10, GUN)                      # 肘轴
        parts.append((arm, Vector((sx * SX, 0.05, SHO))))

    # 右前臂：加农炮（原点在肘，炮口朝下 = 胳膊垂着时）
    fr = part('fore_r')
    fr.box(-0.32, -0.34, -1.3, 0.32, 0.34, -0.1, NAVY)
    fr.box(0.28, -0.3, -1.2, 0.44, 0.3, -0.3, LGRAY)
    fr.cyl(0, 0, -2.3, -1.2, 0.2, 10, GUN)                                  # 炮管
    for k in range(3): fr.cyl(0, 0, -1.62 - k * 0.2, -1.52 - k * 0.2, 0.27, 10, DARK)   # 散热环
    fr.cyl(0, 0, -2.46, -2.2, 0.28, 10, DARK)                               # 炮口制退器
    fr.cyl(0, 0, -2.5, -2.4, 0.15, 8, AMBER)
    fr.box(-0.2, 0.3, -0.95, 0.2, 0.62, -0.3, DARK)                         # 弹鼓
    fr.box(-0.22, 0.56, -0.7, 0.22, 0.66, -0.55, HAZ)
    parts.append((fr, Vector((SX, 0.05, ELB))))
    # 左前臂：拳
    fl = part('fore_l')
    fl.box(-0.28, -0.3, -1.3, 0.28, 0.3, -0.1, BODY)
    fl.box(-0.42, -0.28, -1.15, -0.26, 0.28, -0.3, LGRAY)
    fl.box(-0.3, -0.33, -1.72, 0.3, 0.33, -1.25, DARK)                      # 拳
    fl.box(-0.32, -0.37, -1.6, 0.32, -0.3, -1.38, GUN)
    parts.append((fl, Vector((-SX, 0.05, ELB))))

    # 塔盾（原点在握把：盾背面正中；盾面朝 −Y）
    sh = part('shield')
    OUT_ = [(-0.85, -1.9), (0.85, -1.9), (1.0, -1.2), (1.0, 1.3), (0.6, 1.9), (-0.6, 1.9), (-1.0, 1.3), (-1.0, -1.2)]
    def scaled(k): return [(x * k, z * k) for x, z in OUT_]
    sh.mat = Matrix.Rotation(math.pi / 2, 4, 'X')                           # 本地 (x, y, z) → (x, −z, y)：轮廓画在 x–z 面里，沿 −Y 加厚
    sh.prism(OUT_, 0.15, 0.4, NAVY)                                         # 盾体
    sh.prism(scaled(0.86), 0.36, 0.5, LGRAY)                                # 盾面装甲
    sh.prism([(0, -0.55), (0.38, 0), (0, 0.55), (-0.38, 0)], 0.46, 0.57, MAG)   # 徽记
    sh.box(-0.7, -1.58, 0.45, 0.7, -1.3, 0.55, HAZ)                         # 盾底警示带
    sh.box(-0.42, 1.0, 0.45, 0.42, 1.22, 0.56, GLASS)                       # 观察窗
    for sx in (-1, 1): sh.box(sx * 0.62, -1.0, 0.45, sx * 0.74, 0.8, 0.54, NAVY)   # 两道竖筋
    sh.mat = I
    sh.box(-0.16, -0.2, -0.32, 0.16, 0.22, 0.32, DARK)                      # 握把座
    sh.box(-0.7, -0.19, -0.07, 0.7, -0.08, 0.07, GUN)                       # 背面横梁
    sh.box(-0.07, -0.19, -1.5, 0.07, -0.08, 1.5, GUN)
    SHP = Vector((-SX, -0.45, ELB - 1.48))
    parts.append((sh, SHP))
    glow = part('shield_glow')
    glow.mat = Matrix.Rotation(math.pi / 2, 4, 'X'); glow.prism(scaled(1.07), 0.24, 0.33, BLUE); glow.mat = I   # 盾缘能量边（比盾体大一圈、夹在中间）
    parts.append((glow, SHP))
    finish_export('mech', parts)


# ───────── 核弹（11-12 用户：核弹做得太简单 —— 原来是运行时几个方块拼的）─────────
# 一枚 9 米长的集团战略导弹，弹头朝 −Y：钟形喷口（里面是发光的燃烧室）→ 收腰尾段 → 深蓝尾段 + 洋红识别环 → 白色弹身（蒙皮环缝、两条走线槽）
# → 黄黑警示带 → 收细的前段 → 三段红色弹头 + 镀铬引信；四片后掠尾翼、四片小鸭翼、两枚捆绑助推器（各带红色整流罩、喷口、绑带）。
def nuke():
    body = part('body')
    body.mat = Matrix.Rotation(math.pi / 2, 4, 'X')      # 本地 +z = 弹头方向
    R = 0.8
    def rect(x0, x1, hw, ang):                            # 径向的薄矩形（x 沿半径、宽 2·hw），绕弹轴转 ang 度
        c, s_ = math.cos(math.radians(ang)), math.sin(math.radians(ang))
        return [(x * c - y * s_, x * s_ + y * c) for x, y in ((x0, -hw), (x1, -hw), (x1, hw), (x0, hw))]
    # 主体（相邻两段互相插进去 5～10cm，不留共面的端面）
    body.cyl(0, 0, -4.5, -3.8, 0.62, 12, GUN, 0.4)        # 钟形喷口
    body.cyl(0, 0, -4.58, -4.42, 0.5, 12, AMBER)          # 燃烧室（自发光）
    body.cyl(0, 0, -3.9, -3.3, 0.5, 12, DARK, R)          # 收腰
    body.cyl(0, 0, -3.35, -1.0, R, 12, NAVY)              # 尾段
    body.cyl(0, 0, -1.08, -0.86, R + 0.035, 12, MAG)      # 洋红识别环
    body.cyl(0, 0, -0.9, 2.0, R, 12, LGRAY)               # 弹身
    for zc in (-0.2, 0.62, 1.4): body.cyl(0, 0, zc, zc + 0.07, R + 0.018, 12, DARK)   # 蒙皮环缝
    body.cyl(0, 0, 1.95, 2.6, R + 0.02, 12, HAZ)          # 警示带（黄）
    for k in range(6): body.prism(rect(R - 0.02, R + 0.05, 0.15, k * 60 + 15), 2.02, 2.53, DARK)   # 警示带上的黑条
    body.cyl(0, 0, 2.55, 3.2, R, 12, LGRAY, 0.68)         # 前段收细
    body.cyl(0, 0, 3.15, 4.0, 0.68, 12, RED, 0.36)        # 弹头
    body.cyl(0, 0, 3.95, 4.4, 0.36, 12, RED, 0.12)
    body.cyl(0, 0, 4.35, 4.62, 0.12, 8, CHROME, 0.03)     # 引信
    # 尾翼 ×4（后掠梯形）+ 翼尖洋红条；鸭翼 ×4（错开 45°）
    for k in range(4):
        ang = k * 90
        body.prism(rect(0.68, 2.05, 0.05, ang), -3.7, -1.5, NAVY, rect(0.68, 0.95, 0.05, ang))
        body.prism(rect(1.9, 2.12, 0.07, ang), -3.74, -3.3, MAG, rect(1.72, 1.94, 0.07, ang))
        body.prism(rect(0.6, 1.2, 0.04, ang + 45), 2.7, 3.3, GUN, rect(0.6, 0.72, 0.04, ang + 45))
    # 走线槽 ×2
    for ang in (45, 225): body.prism(rect(0.74, 0.9, 0.07, ang), -3.2, 1.9, GUN)
    # 捆绑助推器 ×2（各带整流罩、喷口、燃烧室、两道绑带）
    for ang in (135, 315):
        cx, cy = 1.05 * math.cos(math.radians(ang)), 1.05 * math.sin(math.radians(ang))
        body.cyl(cx, cy, -3.6, 0.2, 0.3, 10, LGRAY)
        body.cyl(cx, cy, 0.15, 0.85, 0.3, 10, RED, 0.06)
        body.cyl(cx, cy, -1.6, -1.45, 0.32, 10, MAG)
        body.cyl(cx, cy, -4.0, -3.55, 0.3, 10, GUN, 0.2)
        body.cyl(cx, cy, -4.06, -3.94, 0.22, 10, AMBER)
        for zc in (-2.9, -0.5): body.prism(rect(0.6, 1.1, 0.14, ang), zc, zc + 0.16, DARK)
    body.mat = Matrix.Identity(4)
    finish_export('nuke', [(body, Vector((0, 0, 0)))])


UNITS = {'police': police, 'repair': repair, 'apc': apc, 'tank': tank, 'rocket': rocket, 'sam': lambda: rocket('sam'), 'boat': boat, 'heli': heli,
         'drone': drone, 'jet': jet, 'bomber': bomber, 'mech': mech, 'nuke': nuke}
ONLY = sys.argv[sys.argv.index('--') + 2:]          # 可选：只生成这几个单位（-- <输出目录> mech nuke）
for name, f in UNITS.items():
    if not ONLY or name in ONLY: f()
bad = {k: v for k, v in REPORT.items() if v['open_edges'] or v['nonmanifold'] or v['coplanar'] or v['flipped']}
print('报告', json.dumps(REPORT, ensure_ascii=False), flush=True)
print('不合格', json.dumps(bad, ensure_ascii=False), flush=True)
