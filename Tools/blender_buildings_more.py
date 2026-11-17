"""《我是怪兽》 · 其余 7 种楼的程序化模块（由 Tools/blender_buildings.py 用 exec 载入，共享它的 Mesh / ring_wall 等工具）。
楼型：office 写字楼 | apartment 公寓 | garage 停车楼 | shop 商铺 | villa 别墅 | warehouse 仓库 | gas_station 加油站
约定和样板楼一样：真实尺寸（米），正面 = Blender -Y（临街、玩家一侧），每块模块封闭、单图集。
矮楼（商铺 / 别墅 / 仓库 / 加油站）在 meta.json 里写 mids：层数 1 时只有 底座 + 楼顶，不硬塞标准层。"""
from mathutils import Vector


# ───────── 通用工具（在样板楼工具上补的）─────────

def tri(m, pts, region):
    """三角面（山墙、拱顶端面用），进当前焊接组"""
    vs = [m.bm.verts.new(p) for p in pts]
    m.groups[m.cur].extend(vs)
    f = m.bm.faces.new(vs)
    for l, t in zip(f.loops, m.auto_uv(list(pts) + [pts[-1]], region)[:3]):
        l[m.uv].uv = t


def facade_bay2(m, origin, right, up, outward, w, h, win, depth, inner='glass', levels=()):
    """同 facade_bay，窗洞里那块可以不是玻璃（卷帘门、木门、深色车库口）"""
    facade_bay(m, origin, right, up, outward, w, h, win, depth, levels=levels, inner=inner)


def ring_wall_k(m, W, D, z0, h, bx, by, winf, depth, cx=0.0, cy=0.0, inner=None):
    """四面墙，每个开间单独决定开什么洞：winf(k, i, bw, h) → (左, 下, 宽, 高) 或 None（实墙）；
    inner(k, i) → 洞里的区域（默认玻璃）。k：0 正面 1 右 2 背面 3 左。cx / cy：墙体中心偏移。
    整圈墙统一分行高度（ring_levels），转角两侧的开间也能焊上"""
    cs = [Vector((cx - W / 2, cy - D / 2, z0)), Vector((cx + W / 2, cy - D / 2, z0)),
                            Vector((cx + W / 2, cy + D / 2, z0)), Vector((cx - W / 2, cy + D / 2, z0))]
    up = Vector((0, 0, 1))
    sides = []
    for k in range(4):
        p0, p1 = cs[k], cs[(k + 1) % 4]
        n = bx if k % 2 == 0 else by
        bw = (p1 - p0).length / n
        sides.append((k, p0, (p1 - p0).normalized(), n, bw))
    lv = ring_levels([corner_safe(winf(k, i, bw, h), i, n, bw, depth) for (k, _, _, n, bw) in sides for i in range(n)], h)
    for k, p0, right, n, bw in sides:
        out = Vector((right.y, -right.x, 0))
        for i in range(n):
            facade_bay(m, p0 + right * (i * bw), right, up, out, bw, h, corner_safe(winf(k, i, bw, h), i, n, bw, depth), depth, levels=lv,
                                                    inner=(inner(k, i) if inner else 'glass'))


def sides_c(W, D, z0, cx=0.0, cy=0.0):
    for k, p0, right, out, L in _sides(W, D, z0):
        yield k, p0 + Vector((cx, cy, 0)), right, out, L


def mullions_k(m, W, D, z0, h, bx, by, depth, winf, cols=2, rows_at=(), bar=0.07, proud=0.05, sides=(0, 1, 2, 3), cx=0.0, cy=0.0, skip=None):
    """幕墙格栅（每面开间数可不同，按 winf 的窗位）；skip(k, i) 为真的开间不加"""
    for k, p0, right, out, L in sides_c(W, D, z0, cx, cy):
        if k not in sides:
            continue
        n = bx if k % 2 == 0 else by
        bw = L / n
        for i in range(n):
            w = winf(k, i, bw, h)
            if w is None or (skip and skip(k, i)):
                continue
            wl, wb, ww, wh = w
            base = p0 + right * (i * bw) - out * depth
            gx0, gx1, gy0, gy1 = wl, wl + ww, wb, wb + wh
            xs = [gx0 + (gx1 - gx0) * c / cols for c in range(cols + 1)]
            for x in xs[1:-1]:
                _slab_on_wall(m, base, right, out, x - bar / 2, gy0, x + bar / 2, gy1, 0, proud, 'frame')
            for f in rows_at:
                y = gy0 + (gy1 - gy0) * f
                for a, b in zip(xs[:-1], xs[1:]):
                    a2 = a + (bar / 2 if a > gx0 else 0); b2 = b - (bar / 2 if b < gx1 else 0)
                    _slab_on_wall(m, base, right, out, a2, y - bar / 2, b2, y + bar / 2, 0, proud, 'frame')


def sills_k(m, W, D, z0, h, bx, by, winf, wdt=0.1, proud=0.1, lintel_sides=(0,), sides=(0, 1, 2, 3), cx=0.0, cy=0.0):
    """窗台（每扇窗）+ 窗楣（只在 lintel_sides 这几面）"""
    for k, p0, right, out, L in sides_c(W, D, z0, cx, cy):
        if k not in sides:
            continue
        n = bx if k % 2 == 0 else by
        bw = L / n
        for i in range(n):
            w = winf(k, i, bw, h)
            if w is None:
                continue
            wl, wb, ww, wh = w
            base = p0 + right * (i * bw)
            x0, x1 = max(0.02, wl - wdt), min(bw - 0.02, wl + ww + wdt)
            if wb - wdt > 0.02:
                _slab_on_wall(m, base, right, out, x0, wb - wdt, x1, wb, 0, proud, 'frame')
            if k in lintel_sides and wb + wh + wdt < h - 0.02:
                _slab_on_wall(m, base, right, out, x0, wb + wh, x1, wb + wh + wdt, 0, proud * 0.7, 'frame')


def band(m, W, D, z0, y0, y1, d, region, cx=0.0, cy=0.0, sides=(0, 1, 2, 3)):
    """绕楼一圈的水平线脚（四段，转角处两段错开不重叠：前后通长，左右夹在中间）"""
    for k, p0, right, out, L in sides_c(W, D, z0, cx, cy):
        if k not in sides:
            continue
        if k % 2 == 0:
            _slab_on_wall(m, p0, right, out, -d, y0, L + d, y1, 0, d, region)
        else:
            _slab_on_wall(m, p0, right, out, 0, y0, L, y1, 0, d, region)


def parapet(m, W, D, z, ph, pt, region='slab', cap_region='frame', cx=0.0, cy=0.0, roof=True):
    """女儿墙（四块不重叠的长方体）+ 压顶 + 屋面"""
    x0, x1, y0, y1 = cx - W / 2, cx + W / 2, cy - D / 2, cy + D / 2
    m.box(x0, y0, z, x1, y0 + pt, z + ph, region)
    m.box(x0, y1 - pt, z, x1, y1, z + ph, region)
    m.box(x0, y0 + pt, z, x0 + pt, y1 - pt, z + ph, region)
    m.box(x1 - pt, y0 + pt, z, x1, y1 - pt, z + ph, region)
    if roof:
        m.box(x0 + pt, y0 + pt, z, x1 - pt, y1 - pt, z + 0.05, 'roof')
    if cap_region:
        e = 0.08
        m.box(x0 - e, y0 - e, z + ph, x1 + e, y0 + pt + e, z + ph + 0.1, cap_region)
        m.box(x0 - e, y1 - pt - e, z + ph, x1 + e, y1 + e, z + ph + 0.1, cap_region)
        m.box(x0 - e, y0 + pt + e, z + ph, x0 + pt + e, y1 - pt - e, z + ph + 0.1, cap_region)
        m.box(x1 - pt - e, y0 + pt + e, z + ph, x1 + e, y1 - pt - e, z + ph + 0.1, cap_region)


def gable_block(m, x0, x1, y0, y1, z0, zt, region):
    """山墙体（屋脊沿 x）：底面 + 前后两坡 + 两端三角山墙，封闭"""
    prev = m.cur; m.cur = m.new_group()
    yr = (y0 + y1) / 2
    m.quad([(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)], region)
    m.quad([(x0, y0, z0), (x1, y0, z0), (x1, yr, zt), (x0, yr, zt)], region)
    m.quad([(x1, y1, z0), (x0, y1, z0), (x0, yr, zt), (x1, yr, zt)], region)
    tri(m, [(x0, y1, z0), (x0, y0, z0), (x0, yr, zt)], region)
    tri(m, [(x1, y0, z0), (x1, y1, z0), (x1, yr, zt)], region)
    m.cur = prev


def prism_x(m, ring, x0, x1, region, end_region):
    """yz 平面上的凸多边形沿 x 挤出（拱顶）：侧面四边形 + 两端扇形三角"""
    prev = m.cur; m.cur = m.new_group()
    n = len(ring)
    for i in range(n):
        (ya, za), (yb, zb) = ring[i], ring[(i + 1) % n]
        m.quad([(x0, ya, za), (x0, yb, zb), (x1, yb, zb), (x1, ya, za)], region)
    for i in range(1, n - 1):
        tri(m, [(x1, ring[0][0], ring[0][1]), (x1, ring[i][0], ring[i][1]), (x1, ring[i + 1][0], ring[i + 1][1])], end_region)
        tri(m, [(x0, ring[0][0], ring[0][1]), (x0, ring[i + 1][0], ring[i + 1][1]), (x0, ring[i][0], ring[i][1])], end_region)
    m.cur = prev


def car(m, x, y, z, ang_x=True, body='frame'):
    """停车楼里的车（和街上的车同一种造型语言）：四个轮子 + 车身下半（前后保险杠）+ 前后斜的驾驶舱（四面车窗）+ 车顶，沿 x 或沿 y 停"""
    L, Wc = 4.4, 1.8
    def P(u, v, h):   # 车的本地坐标（u 沿车长，v 沿车宽）→ 模型坐标
        return (x + u, y + v, z + h) if ang_x else (x + v, y + u, z + h)
    def bx(u0, v0, h0, u1, v1, h1, reg):
        a, b = P(u0, v0, h0), P(u1, v1, h1)
        m.box(min(a[0], b[0]), min(a[1], b[1]), a[2], max(a[0], b[0]), max(a[1], b[1]), b[2], reg)
    for su in (-1, 1):
        for sv in (-1, 1):
            bx(su * L * 0.32 - 0.33, sv * Wc * 0.41 - 0.13, 0, su * L * 0.32 + 0.33, sv * Wc * 0.41 + 0.13, 0.62, 'metal')   # 轮子
    bx(-L / 2 + 0.15, -Wc / 2, 0.3, L / 2 - 0.15, Wc / 2, 0.95, body)                       # 车身
    bx(-L / 2, -Wc / 2 + 0.05, 0.32, -L / 2 + 0.15, Wc / 2 - 0.05, 0.6, 'metal')            # 后保险杠
    bx(L / 2 - 0.15, -Wc / 2 + 0.05, 0.32, L / 2, Wc / 2 - 0.05, 0.6, 'metal')              # 前保险杠
    # 驾驶舱：底边长 2.4、顶边长 1.3（前风挡更斜），四面是车窗；车头朝 +u
    u0, u1, t0, t1, vw = -1.3, 1.0, -0.75, 0.45, Wc / 2 - 0.12
    c = [P(u0, -vw, 0.95), P(u1, -vw, 0.95), P(u1, vw, 0.95), P(u0, vw, 0.95),
         P(t0, -vw + 0.06, 1.5), P(t1, -vw + 0.06, 1.5), P(t1, vw - 0.06, 1.5), P(t0, vw - 0.06, 1.5)]
    if not ang_x:   # 沿 y 停时 P 交换了 x/y，底面顶点顺序要反一下保持朝外
        c = [c[0], c[3], c[2], c[1], c[4], c[7], c[6], c[5]]
    m.hexa(c, 'glass')
    bx(t0 - 0.05, -vw + 0.02, 1.5, t1 + 0.05, vw - 0.02, 1.58, body)                        # 车顶


def ac_unit(m, x, y, z, face_y=-1):
    """挂在墙上的分体空调外机（贴在 y 向墙面上）：机身 + 前面的格栅凸条"""
    d = 0.5 * face_y
    m.box(x - 0.45, min(y, y + d), z, x + 0.45, max(y, y + d), z + 0.6, 'metal')


# ═════════════ 写字楼：24×20m，层高 3.8m，水平带窗 + 正面竖向遮阳鳍 ═════════════
def office():
    W, D, H, T = 24.0, 20.0, 3.8, 0.3
    BX, BY = 6, 5
    win = lambda k, i, bw, h: (0.15, 1.05, bw - 0.3, h - 1.35)
    mods = {}
    for var in ('storey_a', 'storey_b'):
        m = Mesh(var)
        ring_wall_k(m, W, D, T, H - T, BX, BY, win, 0.2)
        slab(m, W, D, 0, T, 0.35)
        band(m, W, D, T, 0.15, 0.85, 0.1, 'slab')      # 窗下墙的凸出横带
        if var == 'storey_a':
            mullions_k(m, W, D, T, H - T, BX, BY, 0.2, win, cols=2, bar=0.08, proud=0.05)
            # 正面 / 背面竖向遮阳鳍（每个开间分界一片，伸出 0.7m）
            for k, p0, right, out, L in _sides(W, D, T):
                if k % 2:
                    continue
                bw = L / BX
                for i in range(BX + 1):
                    x = min(max(i * bw, 0.1), L - 0.1)
                    _slab_on_wall(m, p0, right, out, x - 0.08, -T, x + 0.08, H - T, 0.1, 0.75, 'frame')
        else:
            mullions_k(m, W, D, T, H - T, BX, BY, 0.2, win, cols=3, bar=0.07, proud=0.05)
            band(m, W, D, T, H - T - 0.32, H - T - 0.22, 0.8, 'frame') # 窗顶水平遮阳板
            # 两侧墙上的空调外机（不是每个开间都有）
            for k, p0, right, out, L in _sides(W, D, T):
                if k % 2 == 0:
                    continue
                bw = L / BY
                for i in (0, 2, 3):
                    c = p0 + right * (i * bw + bw * 0.5)
                    _slab_on_wall(m, p0, right, out, i * bw + bw * 0.5 - 0.45, 0.15, i * bw + bw * 0.5 + 0.45, 0.75, 0.1, 0.6, 'metal')
        mods[var] = m.finish(1800)

    # 底座：5m 柱廊大堂（玻璃退进 1.6m，开间之间留出方柱）+ 入口挑檐
    m = Mesh('base')
    HB = 5.0
    lobby = lambda k, i, bw, h: (0.35, 0.0, bw - 0.7, h - 0.8)
    ring_wall_k(m, W, D, 0, HB, BX, BY, lobby, 1.6)
    mullions_k(m, W, D, 0, HB, BX, BY, 1.6, lobby, cols=3, rows_at=[0.62], bar=0.1, proud=0.06, sides=(0, 1))
    band(m, W, D, 0, HB - 0.8, HB, 0.3, 'base')
    band(m, W, D, 0, 0, 0.45, 0.12, 'base')
    # 入口挑檐：薄钢板 + 前沿灯带 + 两根细钢柱
    m.box(-4.5, -D / 2 - 3.4, 3.7, 4.5, -D / 2, 3.95, 'metal')
    m.box(-4.4, -D / 2 - 3.45, 3.75, 4.4, -D / 2 - 3.38, 3.9, 'neon')
    for x in (-4.0, 4.0):
        m.box(x - 0.1, -D / 2 - 3.1, 0.3, x + 0.1, -D / 2 - 2.9, 3.7, 'metal')
    m.box(-5, -D / 2 - 3.6, 0, 5, -D / 2, 0.15, 'base')
    m.box(-4.5, -D / 2 - 1.2, 0.15, 4.5, -D / 2, 0.3, 'base')
    for x in (-9.0, 9.0):    # 门口两个花池
        m.box(x - 1.6, -D / 2 - 2.2, 0, x + 1.6, -D / 2 - 1.0, 0.6, 'base')
    mods['base'] = m.finish(2600)

    # 楼顶：女儿墙 + 设备间（百叶）+ 电梯机房 + 楼顶广告牌支架
    m = Mesh('crown')
    slab(m, W, D, 0, T, 0.35)
    parapet(m, W, D, T, 1.1, 0.3)
    m.box(-6, 1, T + 0.05, 6, 7, T + 3.6, 'wall', top='roof') # 设备间
    for kk in range(6):
        z = T + 0.5 + kk * 0.5
        m.box(-5.6, 0.88, z, 5.6, 1.0, z + 0.14, 'frame')      # 正面百叶
    m.box(-6.15, 0.85, T + 3.6, 6.15, 7.15, T + 3.8, 'frame')
    m.box(7, 3, T + 0.05, 10, 7, T + 2.8, 'base', top='roof')         # 电梯机房
    m.box(7.9, 2.9, T + 0.05, 9.1, 3.0, T + 2.1, 'metal')
    # 广告牌支架：两根立柱 + 两道横梁（招牌面由 Unity 挂上）
    for x in (-5.0, 5.0):
        m.box(x - 0.15, -6.3, T + 0.05, x + 0.15, -6.0, T + 7.2, 'metal')
    for z in (T + 3.4, T + 7.0):
        m.box(-5.6, -6.25, z, 5.6, -6.05, z + 0.2, 'metal')
    mods['crown'] = m.finish(1800)
    return mods


# ═════════════ 公寓：16×22m（进深大），层高 3.0m，正面阳台 / 侧面空调机 ═════════════
def apartment():
    W, D, H = 16.0, 22.0, 3.0
    BX, BY = 4, 6
    def win(k, i, bw, h):
        if k == 0:
            return (bw * 0.2, 0.0, bw * 0.6, 2.25)      # 正面：落地推拉门（通阳台）
        return (bw * 0.22, 0.85, bw * 0.56, 1.4)
    win_b = lambda k, i, bw, h: (bw * 0.22, 0.85, bw * 0.56, 1.4)
    mods = {}
    for var in ('storey_a', 'storey_b'):
        m = Mesh(var)
        w = win if var == 'storey_a' else win_b
        ring_wall_k(m, W, D, 0, H, BX, BY, w, 0.15)
        sills_k(m, W, D, 0, H, BX, BY, w, wdt=0.1, proud=0.1, lintel_sides=(0, 2))
        band(m, W, D, 0, 0, 0.22, 0.14, 'slab')   # 层间楼板线
        mullions_k(m, W, D, 0, H, BX, BY, 0.15, w, cols=2, bar=0.06, proud=0.04, sides=(0, 2))
        if var == 'storey_a':
            # 正面每户一个阳台：阳台板 + 前栏板 + 两侧栏板
            bw = W / BX
            for i in range(BX):
                xa, xb = -W / 2 + i * bw + 0.25, -W / 2 + (i + 1) * bw - 0.25
                y0, y1 = -D / 2 - 1.3, -D / 2
                m.box(xa, y0, 0, xb, y1, 0.2, 'slab')
                m.box(xa, y0, 0.2, xb, y0 + 0.06, 1.15, 'rail')
                m.box(xa, y0 + 0.06, 0.2, xa + 0.06, y1, 1.15, 'rail')
                m.box(xb - 0.06, y0 + 0.06, 0.2, xb, y1, 1.15, 'rail')
        else:
            # 正面窗上的条纹小雨棚（斜） + 窗下花箱
            bw = W / BX
            for i in range(BX):
                xa, xb = -W / 2 + i * bw + bw * 0.18, -W / 2 + (i + 1) * bw - bw * 0.18
                y0, y1, zh, zl, t = -D / 2, -D / 2 - 0.9, 2.55, 2.2, 0.06
                m.hexa([(xa, y1, zl), (xb, y1, zl), (xb, y0, zh), (xa, y0, zh),
                        (xa, y1, zl + t), (xb, y1, zl + t), (xb, y0, zh + t), (xa, y0, zh + t)], 'awning')
                m.box(xa + 0.1, -D / 2 - 0.35, 0.55, xb - 0.1, -D / 2 - 0.1, 0.78, 'metal')
        # 两侧窗下的空调外机（错开）
        for k, p0, right, out, L in _sides(W, D, 0):
            if k % 2 == 0:
                continue
            bw = L / BY
            for i in ((1, 4) if var == 'storey_a' else (0, 3, 5)):
                x = i * bw + bw * 0.5
                _slab_on_wall(m, p0, right, out, x - 0.45, 0.2, x + 0.45, 0.78, 0.08, 0.55, 'metal')
        mods[var] = m.finish(1800)

    # 底座：底层门厅（门廊 + 台阶）+ 勒脚
    m = Mesh('base')
    HB = 3.4
    def wb(k, i, bw, h):
        if k == 0 and i in (1, 2):
            return (bw * 0.15, 0.0, bw * 0.7, 2.6)       # 门厅玻璃门
        return (bw * 0.2, 0.8, bw * 0.6, 1.7)
    ring_wall_k(m, W, D, 0, HB, BX, BY, wb, 0.2)
    mullions_k(m, W, D, 0, HB, BX, BY, 0.2, wb, cols=2, rows_at=[0.8], bar=0.07, proud=0.05, sides=(0,))
    sills_k(m, W, D, 0, HB, BX, BY, wb, lintel_sides=())
    band(m, W, D, 0, 0, 0.55, 0.1, 'base')
    band(m, W, D, 0, HB - 0.3, HB, 0.18, 'slab')
    # 门廊：平顶 + 两根方柱 + 台阶
    m.box(-4.4, -D / 2 - 2.4, 2.75, 4.4, -D / 2, 3.0, 'slab')
    for x in (-4.0, 4.0):
        m.box(x - 0.2, -D / 2 - 2.2, 0.3, x + 0.2, -D / 2 - 1.8, 2.75, 'frame')
    m.box(-4.4, -D / 2 - 2.4, 0, 4.4, -D / 2, 0.15, 'base')
    m.box(-3.8, -D / 2 - 1.4, 0.15, 3.8, -D / 2, 0.3, 'base')
    mods['base'] = m.finish(2400)

    # 楼顶：女儿墙 + 正面中间阶梯式山花（装饰艺术）+ 楼梯间 + 水箱座
    m = Mesh('crown')
    parapet(m, W, D, 0, 0.9, 0.25)
    m.box(-3.0, -D / 2, 0, 3.0, -D / 2 + 0.3, 2.0, 'wall', top='frame')
    m.box(-1.8, -D / 2, 2.0, 1.8, -D / 2 + 0.3, 2.7, 'wall', top='frame')
    for z in (0.95, 1.3, 1.65):
        m.box(-2.7, -D / 2 - 0.08, z, 2.7, -D / 2, z + 0.1, 'frame')
    m.box(2, 4, 0.05, 6, 8, 2.8, 'wall', top='roof')
    m.box(1.85, 3.85, 2.8, 6.15, 8.15, 2.95, 'frame')
    m.box(3.4, 3.95, 0.05, 4.6, 4.0, 2.1, 'metal')
    mods['crown'] = m.finish(1600)
    return mods


# ═════════════ 停车楼：26×20m，层高 3.0m，开敞层（矮墙 + 柱 + 退进的暗芯）═════════════
def garage():
    W, D, H, T = 26.0, 20.0, 3.0, 0.3
    BX, BY = 7, 5
    mods = {}

    def deck(m, z, h, cars, gates=False):
        slab(m, W, D, z, T, 0.1)
        # 退进的暗芯（看进去是黑的车库内部）
        m.box(-W / 2 + 3.0, -D / 2 + 3.0, z + T, W / 2 - 3.0, D / 2 - 3.0, z + h, 'metal')   # 车停在芯和外圈柱子之间那条 3 米宽的车道上
        # 外圈柱子（矮墙后面）
        for k, p0, right, out, L in _sides(W, D, z + T):
            n = BX if k % 2 == 0 else BY
            bw = L / n
            for i in range(n):   # 每面从 0 开始，转角柱只算一次
                c = p0 + right * (i * bw) - out * 0.55
                m.box(c.x - 0.28, c.y - 0.28, z + T, c.x + 0.28, c.y + 0.28, z + h, 'wall')
        # 矮墙（防撞墙）+ 一道橙色腰线；gates：正面留出入口两段
        for k, p0, right, out, L in _sides(W, D, z + T):
            spans = [(0, L)]
            if gates and k == 0:
                spans = [(0, 4.0), (9.0, 17.0), (22.0, L)]
            for a, b in spans:
                if k % 2:
                    a, b = max(a, 0.25), min(b, L - 0.25)
                _slab_on_wall(m, p0, right, out, a, 0, b, 1.0, -0.25, 0, 'wall')
                _slab_on_wall(m, p0, right, out, a, 0.55, b, 0.75, 0, 0.04, 'frame')
        for (x, y, ax) in cars:
            car(m, x, y, z + T, ax)

    for var, cars in (('storey_a', [(-9, -8.1, True), (-3.5, -8.1, True), (6.5, -8.1, True), (11.1, -2, False), (-11.1, 3, False), (2, 8.1, True)]),
        ('storey_b', [(-6.2, -8.1, True), (1.5, -8.1, True), (9.5, -8.1, True), (11.1, 4, False), (-11.1, -3, False), (-4, 8.1, True)])):
        m = Mesh(var)
        deck(m, 0, H, cars)
        mods[var] = m.finish(1800)

    # 底层：出入口（栏杆机 + 岗亭）
    m = Mesh('base')
    HB = 3.4
    deck(m, 0, HB, [(-9.5, -8.1, True), (11.1, 1, False)], gates=True)
    for x0 in (-9.0, 4.0):   # 两个出入口：道闸
        m.box(x0, -D / 2 + 0.3, 0.3, x0 + 0.4, -D / 2 + 0.7, 1.4, 'frame')
        m.box(x0 + 0.4, -D / 2 + 0.42, 1.05, x0 + 4.6, -D / 2 + 0.58, 1.2, 'awning')
    m.box(-0.8, -D / 2 + 0.3, 0.3, 0.8, -D / 2 + 1.9, 2.6, 'base', top='slab')     # 岗亭
    m.box(-0.6, -D / 2 + 0.25, 1.2, 0.6, -D / 2 + 0.3, 2.1, 'rail')
    mods['base'] = m.finish(2400)

    # 顶层：露天停车层（地面画车位）+ 灯杆 + 楼梯间（招牌位）
    m = Mesh('crown')
    slab(m, W, D, 0, T, 0.1)
    for k, p0, right, out, L in _sides(W, D, T):
        a, b = (0, L) if k % 2 == 0 else (0.25, L - 0.25)
        _slab_on_wall(m, p0, right, out, a, 0, b, 1.1, -0.25, 0, 'wall')
        _slab_on_wall(m, p0, right, out, a, 0.6, b, 0.8, 0, 0.04, 'frame')
    m.box(-W / 2 + 0.25, -D / 2 + 0.25, T, W / 2 - 0.25, D / 2 - 0.25, T + 0.03, 'roof')
    for x, y in ((-8, -4), (8, -4), (-8, 5), (8, 5)):
        m.box(x - 0.12, y - 0.12, T, x + 0.12, y + 0.12, T + 5.0, 'metal')
        m.box(x - 0.5, y - 0.2, T + 5.0, x + 0.5, y + 0.2, T + 5.25, 'neon')
    m.box(W / 2 - 5.5, D / 2 - 5.5, T, W / 2 - 0.4, D / 2 - 0.4, T + 3.6, 'wall', top='roof')
    m.box(W / 2 - 5.65, D / 2 - 5.65, T + 3.6, W / 2 - 0.25, D / 2 - 0.25, T + 3.8, 'frame')
    m.box(W / 2 - 3.6, D / 2 - 5.6, T, W / 2 - 2.4, D / 2 - 5.5, T + 2.2, 'metal')
    for (x, y, ax) in ((-9, -6.5, True), (-2, -6.5, True), (4, 6.5, True), (-6, 6.5, True), (2.5, -6.5, True)):
        car(m, x, y, T + 0.03, ax)
    mods['crown'] = m.finish(1800)
    return mods


# ═════════════ 商铺：15×13m，一层店面（橱窗 + 雨棚 + 招牌带），楼上可加一层住宅 ═════════════
def shop():
    W, D = 15.0, 13.0
    BX, BY = 3, 3
    mods = {}
    HB = 4.4
    def wb(k, i, bw, h):
        if k == 0:
            return (0.3, 0.45, bw - 0.6, 2.75) if i != 1 else (0.3, 0.0, bw - 0.6, 3.2)
        if k == 2:
            return (bw * 0.35, 0.0, bw * 0.3, 2.2) if i == 2 else None   # 后门
        return (bw * 0.3, 1.1, bw * 0.4, 1.3) if i == 1 else None
    m = Mesh('base')
    ring_wall_k(m, W, D, 0, HB, BX, BY, wb, 0.3, inner=lambda k, i: 'metal' if k == 2 else 'glass')
    mullions_k(m, W, D, 0, HB, BX, BY, 0.3, wb, cols=3, rows_at=[0.78], bar=0.08, proud=0.05, sides=(0,))
    # 店面壁柱 + 柱头
    bw = W / BX
    for i in range(BX + 1):
        x = -W / 2 + i * bw
        x0, x1 = (x, x + 0.35) if i == 0 else ((x - 0.35, x) if i == BX else (x - 0.2, x + 0.2))
        m.box(x0, -D / 2 - 0.2, 0, x1, -D / 2, HB - 1.2, 'frame')
    # 招牌带（Unity 会在上面挂招牌）+ 檐口
    m.box(-W / 2 - 0.1, -D / 2 - 0.3, HB - 1.2, W / 2 + 0.1, -D / 2, HB - 0.25, 'slab')
    band(m, W, D, 0, HB - 0.25, HB, 0.35, 'frame')
    band(m, W, D, 0, 0, 0.45, 0.08, 'base')
    # 条纹雨棚（两段，中间门口断开）
    for xa, xb in ((-W / 2 + 0.4, -bw / 2 - 0.1), (bw / 2 + 0.1, W / 2 - 0.4)):
        y0, y1, zh, zl, t = -D / 2 - 0.3, -D / 2 - 1.9, 3.15, 2.65, 0.07
        m.hexa([(xa, y1, zl), (xb, y1, zl), (xb, y0, zh), (xa, y0, zh),
                (xa, y1, zl + t), (xb, y1, zl + t), (xb, y0, zh + t), (xa, y0, zh + t)], 'awning')
    m.box(-1.6, -D / 2 - 1.1, 0, 1.6, -D / 2, 0.12, 'base')         # 门口踏步
    mods['base'] = m.finish(2400)

    # 楼上一层（住宅）：3 扇窗带窗台窗楣，正面中间小阳台
    m = Mesh('storey_a')
    H = 3.2
    w2 = lambda k, i, bw, h: (bw * 0.25, 0.8, bw * 0.5, 1.6) if (k % 2 == 0 or i == 1) else None
    ring_wall_k(m, W, D, 0, H, BX, BY, w2, 0.15)
    sills_k(m, W, D, 0, H, BX, BY, w2, lintel_sides=(0,))
    band(m, W, D, 0, 0, 0.2, 0.12, 'slab')
    m.box(-2.0, -D / 2 - 1.0, 0, 2.0, -D / 2, 0.18, 'slab')
    m.box(-2.0, -D / 2 - 1.0, 0.18, 2.0, -D / 2 - 0.94, 1.1, 'rail')
    mods['storey_a'] = m.finish(1600)

    # 楼顶：假立面（正面女儿墙中间升高成阶梯）+ 屋面 + 排气管
    m = Mesh('crown')
    parapet(m, W, D, 0, 0.9, 0.25)
    m.box(-4.5, -D / 2, 0, 4.5, -D / 2 + 0.25, 1.8, 'wall', top='frame')
    m.box(-2.2, -D / 2, 1.8, 2.2, -D / 2 + 0.25, 2.5, 'wall', top='frame')
    m.box(-4.6, -D / 2 - 0.08, 1.55, 4.6, -D / 2, 1.68, 'frame')
    m.box(-2.3, -D / 2 - 0.08, 2.25, 2.3, -D / 2, 2.38, 'frame')
    m.box(4.0, 3.0, 0.05, 4.3, 3.3, 1.6, 'metal')
    mods['crown'] = m.finish(1400)
    return mods


# ═════════════ 别墅：14×11m，层高 3.2m，两层 + 陶瓦坡屋顶 ═════════════
def villa():
    W, D = 14.0, 11.0
    BX, BY = 3, 3
    mods = {}
    HB = 3.3
    def wb(k, i, bw, h):
        if k == 0:
            return (bw * 0.3, 0.0, bw * 0.4, 2.4) if i == 1 else (bw * 0.12, 0.25, bw * 0.76, 2.2)
        return (bw * 0.25, 0.9, bw * 0.5, 1.4) if i != 1 or k == 2 else None
    m = Mesh('base')
    ring_wall_k(m, W, D, 0, HB, BX, BY, wb, 0.2, inner=lambda k, i: 'metal' if (k == 0 and i == 1) else 'glass')
    mullions_k(m, W, D, 0, HB, BX, BY, 0.2, wb, cols=3, bar=0.07, proud=0.04, sides=(0,), skip=lambda k, i: i == 1)
    sills_k(m, W, D, 0, HB, BX, BY, wb, lintel_sides=(), sides=(1, 2, 3))
    band(m, W, D, 0, 0, 0.5, 0.08, 'base')
    band(m, W, D, 0, HB - 0.22, HB, 0.12, 'slab')
    # 门廊：两根柱子 + 小平顶 + 台阶；门前矮花墙
    bw = W / BX
    m.box(-bw / 2 - 0.6, -D / 2 - 2.0, 2.7, bw / 2 + 0.6, -D / 2, 2.95, 'slab')
    for x in (-bw / 2 - 0.3, bw / 2 + 0.3):
        m.box(x - 0.17, -D / 2 - 1.85, 0.25, x + 0.17, -D / 2 - 1.5, 2.7, 'frame')
    m.box(-bw / 2 - 0.6, -D / 2 - 2.0, 0, bw / 2 + 0.6, -D / 2, 0.25, 'base')
    for xa, xb in ((-W / 2 - 0.5, -bw / 2 - 1.4), (bw / 2 + 1.4, W / 2 + 0.5)):
        m.box(xa, -D / 2 - 3.2, 0, xb, -D / 2 - 2.9, 0.7, 'base', top='frame')
    mods['base'] = m.finish(2200)

    # 二层：带百叶窗扇的窗 + 正面中间小阳台
    m = Mesh('storey_a')
    H = 3.2
    w2 = lambda k, i, bw, h: (bw * 0.3, 0.8, bw * 0.4, 1.5) if (k != 1 and k != 3) or i != 1 else None
    ring_wall_k(m, W, D, 0, H, BX, BY, w2, 0.15)
    sills_k(m, W, D, 0, H, BX, BY, w2, lintel_sides=(0, 2))
    band(m, W, D, 0, 0, 0.18, 0.1, 'slab')
    for k, p0, right, out, L in _sides(W, D, 0):        # 百叶窗扇（窗两侧各一片）
        n = BX if k % 2 == 0 else BY
        b2 = L / n
        for i in range(n):
            w = w2(k, i, b2, H)
            if not w:
                continue
            wl, wbb, ww, wh = w
            for xa, xb in ((i * b2 + wl - 0.6, i * b2 + wl - 0.08), (i * b2 + wl + ww + 0.08, i * b2 + wl + ww + 0.6)):
                _slab_on_wall(m, p0, right, out, xa, wbb, xb, wbb + wh, 0, 0.06, 'awning')
    m.box(-1.8, -D / 2 - 1.1, 0, 1.8, -D / 2, 0.18, 'slab')
    m.box(-1.8, -D / 2 - 1.1, 0.18, 1.8, -D / 2 - 1.04, 1.05, 'rail')
    m.box(-1.8, -D / 2 - 1.04, 0.18, -1.74, -D / 2, 1.05, 'rail')
    m.box(1.74, -D / 2 - 1.04, 0.18, 1.8, -D / 2, 1.05, 'rail')
    mods['storey_a'] = m.finish(1800)

    # 屋顶：山墙体 + 前后两坡陶瓦（挑檐 0.6m）+ 屋脊 + 烟囱
    m = Mesh('crown')
    O, RISE, TH = 0.6, 3.0, 0.2
    gable_block(m, -W / 2, W / 2, -D / 2, D / 2, 0, RISE, 'wall')
    k = RISE / (D / 2)
    lift = 0.03
    ye, ze = -D / 2 - O, -O * k + lift
    yr, zr = 0.12, RISE + 0.12 * (-k) + lift + 0.12 * 0   # 过屋脊一点
    zr = RISE - 0.12 * k + lift
    for sgn in (-1, 1):
        if sgn < 0:
            A, B, C, Dd = (-W / 2 - O, ye, ze), (W / 2 + O, ye, ze), (W / 2 + O, 0.15, RISE - 0.15 * k + lift), (-W / 2 - O, 0.15, RISE - 0.15 * k + lift)
        else:
            A, B, C, Dd = (-W / 2 - O, -0.15, RISE - 0.15 * k + lift), (W / 2 + O, -0.15, RISE - 0.15 * k + lift), (W / 2 + O, -ye, ze), (-W / 2 - O, -ye, ze)
        up = lambda p: (p[0], p[1], p[2] + TH)
        m.hexa([A, B, C, Dd, up(A), up(B), up(C), up(Dd)], 'roof', sides='frame')
    m.box(-W / 2 - O, -0.25, RISE - 0.05, W / 2 + O, 0.25, RISE + 0.28, 'roof')       # 屋脊
    m.box(3.0, 1.2, 1.2, 4.0, 2.2, RISE + 1.3, 'wall', top='frame')     # 烟囱
    m.box(2.85, 1.05, RISE + 1.3, 4.15, 2.35, RISE + 1.45, 'frame')
    mods['crown'] = m.finish(1600)
    return mods


# ═════════════ 仓库：26×18m，压型钢板墙 + 卷帘门 + 装卸平台 + 拱形屋顶 ═════════════
def warehouse():
    W, D = 26.0, 18.0
    BX, BY = 4, 3
    mods = {}
    HB = 5.5
    def wb(k, i, bw, h):
        if k == 0:
            if i in (0, 3):
                return (bw * 0.15, 1.2, bw * 0.7, 3.6)     # 卷帘门（门槛对齐装卸平台）
            return (bw * 0.3, 1.2, bw * 0.4, 1.3) if i == 1 else (bw * 0.6, 0.0, bw * 0.22, 2.3)
        if k == 2 and i == 1:
            return (bw * 0.3, 0.0, bw * 0.4, 4.2)
        return None
    inner = lambda k, i: 'metal' if ((k == 0 and i in (0, 3)) or k == 2 or (k == 0 and i == 2)) else 'glass'
    m = Mesh('base')
    ring_wall_k(m, W, D, 0, HB, BX, BY, wb, 0.25, inner=inner)
    # 钢柱（每个开间分界一根，凸出 0.2）+ 勒脚混凝土带 + 檐口
    for k, p0, right, out, L in _sides(W, D, 0):
        n = BX if k % 2 == 0 else BY
        b2 = L / n
        for i in range(1, n):
            _slab_on_wall(m, p0, right, out, i * b2 - 0.18, 0, i * b2 + 0.18, HB, 0, 0.2, 'frame')
    band(m, W, D, 0, 0, 1.0, 0.1, 'base')
    band(m, W, D, 0, HB - 0.3, HB, 0.22, 'frame')
    # 装卸平台（两扇卷帘门前）+ 防撞胶块 + 台阶 + 斜雨棚
    for i in (0, 3):
        b2 = W / BX
        xa, xb = -W / 2 + i * b2 + 0.4, -W / 2 + (i + 1) * b2 - 0.4
        m.box(xa, -D / 2 - 3.0, 0, xb, -D / 2, 1.2, 'base')
        for x in (xa + 1.0, xb - 1.0):
            m.box(x - 0.25, -D / 2 - 3.15, 0.3, x + 0.25, -D / 2 - 3.0, 1.0, 'metal')
        y0, y1, zh, zl, t = -D / 2, -D / 2 - 3.4, 5.1, 4.6, 0.12
        m.hexa([(xa, y1, zl), (xb, y1, zl), (xb, y0, zh), (xa, y0, zh),
                (xa, y1, zl + t), (xb, y1, zl + t), (xb, y0, zh + t), (xa, y0, zh + t)], 'metal', top='roof')
        m.box(xa + 0.6, -D / 2 - 0.25, 4.85, xa + 1.2, -D / 2, 5.05, 'neon')        # 装卸灯
    b2 = W / BX
    m.box(-W / 2 + 2 * b2 + b2 * 0.55, -D / 2 - 1.2, 0, -W / 2 + 2 * b2 + b2 * 0.87, -D / 2, 0.3, 'base')   # 办公门台阶
    mods['base'] = m.finish(2400)

    # 高窗带（仓库加高时用）
    m = Mesh('storey_a')
    H = 2.4
    w2 = lambda k, i, bw, h: (0.4, 0.5, bw - 0.8, 1.3)
    ring_wall_k(m, W, D, 0, H, BX, BY, w2, 0.12)
    mullions_k(m, W, D, 0, H, BX, BY, 0.12, w2, cols=4, bar=0.07, proud=0.04)
    for k, p0, right, out, L in _sides(W, D, 0):
        n = BX if k % 2 == 0 else BY
        bb = L / n
        for i in range(1, n):
            _slab_on_wall(m, p0, right, out, i * bb - 0.18, 0, i * bb + 0.18, H, 0, 0.2, 'frame')
    mods['storey_a'] = m.finish(1600)

    # 拱形屋顶（沿 x 通长，12 段）+ 屋脊通风帽 + 檐沟
    m = Mesh('crown')
    O, RISE, SEG = 0.4, 3.6, 12
    hw = D / 2 + O
    ring = [(-hw + 2 * hw * s / SEG, RISE * (1 - (-1 + 2 * s / SEG) ** 2) * 0.999 + 0.25) for s in range(SEG + 1)]
    ring = ring[::-1] + [(-hw, 0.0), (hw, 0.0)]
    ring = [(y, z) for (y, z) in ring]
    # 逆时针（从 +x 看）：底边左→右，再沿拱从右回到左
    ring = [(-hw, 0.0), (hw, 0.0)] + [(hw - 2 * hw * s / SEG, 0.25 + RISE * (1 - (1 - 2 * s / SEG) ** 2)) for s in range(SEG + 1)]
    ring = [ring[0], ring[1]] + ring[2:]
    # 去掉和底边端点重合的点（拱脚高 0.25，不重合）
    prism_x(m, ring, -W / 2 - O, W / 2 + O, 'roof', 'wall')
    for x in (-8, 0, 8):
        m.box(x - 0.5, -0.5, RISE + 0.2, x + 0.5, 0.5, RISE + 1.0, 'metal', top='frame')
    for s in (-1, 1):
        y0, y1 = sorted((s * hw, s * (hw + 0.25)))
        m.box(-W / 2 - O, y0, -0.05, W / 2 + O, y1, 0.3, 'frame')
    mods['crown'] = m.finish(1600)
    return mods


# ═════════════ 加油站：20×16m，罩棚（4 柱）+ 两个加油岛 + 后面的便利店 ═════════════
def gas_station():
    W, D = 20.0, 16.0
    mods = {}
    HC = 5.0
    m = Mesh('base')
    # 便利店（后半，12×5.5m，高 3.6m）：正面整面橱窗 + 中间门
    KW, KD, KH, KCY = 12.0, 5.5, 3.6, 4.6
    def kw(k, i, bw, h):
        if k == 0:
            return (0.25, 0.4, bw - 0.5, 2.5) if i != 1 else (0.25, 0.0, bw - 0.5, 2.9)
        if k == 2 and i == 2:
            return (bw * 0.3, 0.0, bw * 0.4, 2.2)
        return (bw * 0.3, 1.2, bw * 0.4, 1.0) if (k % 2 == 1 and i == 1) else None
    ring_wall_k(m, KW, KD, 0, KH, 3, 2, kw, 0.2, cy=KCY, inner=lambda k, i: 'metal' if k == 2 else 'glass')
    mullions_k(m, KW, KD, 0, KH, 3, 2, 0.2, kw, cols=3, bar=0.07, proud=0.04, sides=(0,), cy=KCY)
    m.box(-KW / 2 - 0.2, KCY - KD / 2 - 0.3, KH, KW / 2 + 0.2, KCY + KD / 2 + 0.2, KH + 0.25, 'slab', top='roof')
    m.box(-KW / 2, KCY - KD / 2 - 0.25, KH - 0.9, KW / 2, KCY - KD / 2, KH, 'slab')      # 店招带
    band(m, KW, KD, 0, 0, 0.4, 0.08, 'base', cy=KCY)
    # 罩棚柱（4 根）+ 柱脚
    for x in (-5.5, 5.5):
        for y in (-5.8, -0.6):
            m.box(x - 0.3, y - 0.3, 0, x + 0.3, y + 0.3, HC, 'frame')
            m.box(x - 0.45, y - 0.45, 0, x + 0.45, y + 0.45, 0.6, 'base')
    # 两个加油岛：路缘 + 每岛两台加油机（机身 + 顶部屏幕）
    for x in (-5.5, 5.5):
        m.box(x - 0.8, -6.6, 0, x + 0.8, 0.2, 0.22, 'base')
        for y in (-4.6, -1.8):
            m.box(x - 0.45, y - 0.3, 0.22, x + 0.45, y + 0.3, 1.9, 'awning' if False else 'slab', top='frame')
            m.box(x - 0.5, y - 0.33, 1.9, x + 0.5, y + 0.33, 2.25, 'frame')
            m.box(x - 0.35, y - 0.34, 1.2, x + 0.35, y - 0.3, 1.7, 'rail')
    # 路边价格牌（立柱式，正面由 Unity 挂招牌）
    m.box(W / 2 - 1.6, -D / 2 + 0.4, 0, W / 2 - 0.4, -D / 2 + 0.8, 4.6, 'slab', top='frame')
    m.box(W / 2 - 1.75, -D / 2 + 0.3, 0, W / 2 - 0.25, -D / 2 + 0.9, 0.5, 'base')
    mods['base'] = m.finish(2400)

    # 柱子加长段（只为满足模块格式，层数 1 的加油站用不到）
    m = Mesh('storey_a')
    for x in (-5.5, 5.5):
        for y in (-5.8, -0.6):
            m.box(x - 0.3, y - 0.3, 0, x + 0.3, y + 0.3, 1.0, 'frame')
    mods['storey_a'] = m.finish(400)

    # 罩棚：厚板 + 侧面霓虹灯带 + 顶面
    m = Mesh('crown')
    x0, x1, y0, y1 = -8.5, 8.5, -7.6, 1.2
    m.box(x0, y0, 0, x1, y1, 0.9, 'base', top='roof', bottom='metal')
    m.box(x0 - 0.06, y0 - 0.06, 0.32, x1 + 0.06, y0, 0.58, 'neon')
    m.box(x0 - 0.06, y0, 0.32, x0, y1 + 0.06, 0.58, 'neon')
    m.box(x1, y0, 0.32, x1 + 0.06, y1 + 0.06, 0.58, 'neon')
    m.box(x0, y1, 0.32, x1, y1 + 0.06, 0.58, 'frame')
    mods['crown'] = m.finish(600)
    return mods


MORE_KINDS = {'office': office, 'apartment': apartment, 'garage': garage, 'shop': shop,
                            'villa': villa, 'warehouse': warehouse, 'gas_station': gas_station}
MORE_WALL = {'office': [24.0, 20.0], 'apartment': [16.0, 22.0], 'garage': [26.0, 20.0], 'shop': [15.0, 13.0],
                    'villa': [14.0, 11.0], 'warehouse': [26.0, 18.0], 'gas_station': [20.0, 16.0]}
# 层数怎么换算成标准层数：mids = max(min, 层数 - offset)。矮楼 offset 1、min 0：1 层 = 底座 + 楼顶
MORE_MIDS = {'shop': {'offset': 1, 'min': 0}, 'villa': {'offset': 1, 'min': 0},
                    'warehouse': {'offset': 1, 'min': 0}, 'gas_station': {'offset': 1, 'min': 0}}


# ═════════════ 都市楼型（11-07 加）：滨海公寓塔楼 / 老砖楼 / 现代格栅写字楼 / 装饰艺术摩天楼 / 底商住宅楼 ═════════════

def octagon(cx, cy, r):
    """逆时针八边形（水塔、圆柱类小件用）"""
    import math
    return [(cx + r * math.cos(math.pi / 8 + k * math.pi / 4), cy + r * math.sin(math.pi / 8 + k * math.pi / 4)) for k in range(8)]


def water_tower(m, x, y, z, r=1.3, h=2.6, leg=1.8):
    """木水塔：四条腿 + 八边形桶身 + 稍大的顶盖 + 顶上小帽"""
    for sx in (-1, 1):
        for sy in (-1, 1):
            m.box(x + sx * r * 0.6 - 0.12, y + sy * r * 0.6 - 0.12, z, x + sx * r * 0.6 + 0.12, y + sy * r * 0.6 + 0.12, z + leg, 'metal')
    m.box(x - r * 0.75, y - r * 0.75, z + leg - 0.15, x + r * 0.75, y + r * 0.75, z + leg, 'metal')
    prism(m, octagon(x, y, r), z + leg, z + leg + h, 'awning')
    prism(m, octagon(x, y, r * 1.12), z + leg + h, z + leg + h + 0.25, 'roof')
    m.box(x - 0.3, y - 0.3, z + leg + h + 0.25, x + 0.3, y + 0.3, z + leg + h + 0.75, 'roof')


def wrap_balcony(m, W, D, OV, T, RH, sides=(0, 1, 2, 3)):
    """一圈阳台板 + 玻璃栏板（四块不重叠）；sides 只给前后（0、2）时两侧只伸出 0.3"""
    ox = OV if 1 in sides else 0.3
    oy = OV if 0 in sides else 0.3
    m.box(-W / 2 - ox, -D / 2 - oy, 0, W / 2 + ox, D / 2 + oy, T, 'slab')
    rt = 0.05
    if 0 in sides:
        m.box(-W / 2 - ox, -D / 2 - oy, T, W / 2 + ox, -D / 2 - oy + rt, T + RH, 'rail')
    if 2 in sides:
        m.box(-W / 2 - ox, D / 2 + oy - rt, T, W / 2 + ox, D / 2 + oy, T + RH, 'rail')
    y0 = -D / 2 - oy + (rt if 0 in sides else 0)
    y1 = D / 2 + oy - (rt if 2 in sides else 0)
    if 1 in sides:
        m.box(W / 2 + ox - rt, y0, T, W / 2 + ox, y1, T + RH, 'rail')
    if 3 in sides:
        m.box(-W / 2 - ox, y0, T, -W / 2 - ox + rt, y1, T + RH, 'rail')


# ───────── 滨海公寓塔楼：18×18m，层高 3.2m，每层一圈通长阳台 + 玻璃栏板，楼顶泳池 ─────────
def condo():
    W = D = 18.0; H = 3.2; T = 0.22; OV = 1.7
    BX = BY = 5
    full = lambda k, i, bw, h: (0.15, 0.0, bw - 0.3, h - 0.3)
    mods = {}
    for var in ('storey_a', 'storey_b'):
        m = Mesh(var)
        ring_wall_k(m, W, D, T, H - T, BX, BY, full, 0.12)
        mullions_k(m, W, D, T, H - T, BX, BY, 0.12, full, cols=2, bar=0.06, proud=0.04)
        if var == 'storey_a':
            wrap_balcony(m, W, D, OV, T, 1.05)
        else:
            # 只有前后有阳台，前后各用 3 道隔墙分成 4 户；两侧是实墙带
            wrap_balcony(m, W, D, OV, T, 1.05, sides=(0, 2))
            for sy in (-1, 1):
                for x in (-W / 4, 0.0, W / 4):
                    y0, y1 = sorted((sy * D / 2, sy * (D / 2 + OV - 0.05)))
                    m.box(x - 0.08, y0, T, x + 0.08, y1, H, 'frame')
        mods[var] = m.finish(2400)

    # 底座：5.5m 高大堂（玻璃退进 1m）+ 正门车道雨棚（四柱 + 灯带）
    m = Mesh('base')
    HB = 5.5
    lobby = lambda k, i, bw, h: (0.3, 0.0, bw - 0.6, h - 0.7)
    ring_wall_k(m, W, D, 0, HB, BX, BY, lobby, 1.0)
    mullions_k(m, W, D, 0, HB, BX, BY, 1.0, lobby, cols=3, rows_at=[0.6], bar=0.09, proud=0.05, sides=(0, 1, 3))
    band(m, W, D, 0, HB - 0.5, HB, 0.25, 'slab')
    band(m, W, D, 0, 0, 0.4, 0.08, 'base')
    m.box(-5.5, -D / 2 - 5.0, 4.3, 5.5, -D / 2, 4.65, 'slab', bottom='metal')
    m.box(-5.4, -D / 2 - 5.06, 4.38, 5.4, -D / 2 - 5.0, 4.58, 'neon')
    for x in (-4.8, 4.8):
        m.box(x - 0.18, -D / 2 - 4.5, 0.15, x + 0.18, -D / 2 - 4.14, 4.3, 'frame')
    m.box(-6, -D / 2 - 5.5, 0, 6, -D / 2, 0.15, 'base')
    mods['base'] = m.finish(3000)

    # 楼顶：女儿墙 + 泳池甲板 + 泳池（夜里发光的水）+ 设备间 + 正面竖向装饰鳍（顶上霓虹）
    m = Mesh('crown')
    m.box(-W / 2 - 0.3, -D / 2 - 0.3, 0, W / 2 + 0.3, D / 2 + 0.3, T, 'slab')
    parapet(m, W, D, T, 1.1, 0.25, region='rail', cap_region='frame')
    m.box(-7, -7.5, T + 0.05, 3, 1.5, T + 0.35, 'base')
    m.box(-6, -6.5, T + 0.35, 2, 0.5, T + 0.42, 'neon')
    m.box(4, 2, T + 0.05, 8.5, 8.4, T + 3.4, 'wall', top='roof')
    m.box(3.85, 1.85, T + 3.4, 8.65, 8.55, T + 3.6, 'frame')
    m.box(-0.6, -D / 2 - 0.5, T, 0.6, -D / 2 + 0.6, T + 9.0, 'wall', top='frame')
    m.box(-0.12, -D / 2 - 0.58, T + 0.5, 0.12, -D / 2 - 0.5, T + 8.6, 'neon')
    mods['crown'] = m.finish(2000)
    return mods


# ───────── 老砖楼：16×14m，层高 3.3m，石材窗台窗楣、正面消防梯、楼顶木水塔 ─────────
def fire_escape(m, W, D, H, bays, BX):
    """正面消防梯：每层一块铁平台 + 栏杆 + 斜梯（梯子从本层平台爬到上一层平台底）"""
    bw = W / BX
    for i in bays:
        xa, xb = -W / 2 + i * bw + 0.1, -W / 2 + (i + 1) * bw - 0.1
        y0, y1 = -D / 2 - 1.1, -D / 2
        m.box(xa, y0, 0, xb, y1, 0.08, 'metal')
        m.box(xa, y0, 0.08, xb, y0 + 0.05, 1.0, 'metal')
        m.box(xa, y0 + 0.05, 0.08, xa + 0.05, y1, 1.0, 'metal')
        m.box(xb - 0.05, y0 + 0.05, 0.08, xb, y1, 1.0, 'metal')
        sa, sb, ya, yb = xa + 0.25, xb - 0.45, -D / 2 - 0.95, -D / 2 - 0.4
        za, zb, t = 0.08, H - 0.06, 0.06
        m.hexa([(sa, ya, za), (sb, ya, zb), (sb, yb, zb), (sa, yb, za),
                                    (sa, ya, za + t), (sb, ya, zb + t), (sb, yb, zb + t), (sa, yb, za + t)], 'metal')


def brick():
    W, D, H = 16.0, 14.0, 3.3
    BX, BY = 5, 4
    win = lambda k, i, bw, h: (bw * 0.26, 0.9, bw * 0.48, 1.75)
    mods = {}
    for var in ('storey_a', 'storey_b'):
        m = Mesh(var)
        ring_wall_k(m, W, D, 0, H, BX, BY, win, 0.22)
        sills_k(m, W, D, 0, H, BX, BY, win, wdt=0.14, proud=0.12, lintel_sides=(0, 1, 2, 3))
        mullions_k(m, W, D, 0, H, BX, BY, 0.22, win, cols=2, rows_at=[0.55], bar=0.06, proud=0.03)
        if var == 'storey_b':
            band(m, W, D, 0, 0.0, 0.18, 0.06, 'frame')        # 层间石材腰线
        fire_escape(m, W, D, H, (1, 3), BX)
        mods[var] = m.finish(2400)

    # 底座：沿街店面（正面整片橱窗 + 中间门洞）+ 店招带 + 两段遮阳棚
    m = Mesh('base')
    HB = 4.2
    def wb(k, i, bw, h):
        if k == 0:
            return (0.25, 0.0 if i == 2 else 0.4, bw - 0.5, 3.0 if i == 2 else 2.6)
        return (bw * 0.26, 1.2, bw * 0.48, 1.6) if k != 2 else None
    ring_wall_k(m, W, D, 0, HB, BX, BY, wb, 0.25)
    mullions_k(m, W, D, 0, HB, BX, BY, 0.25, wb, cols=2, rows_at=[0.8], bar=0.07, proud=0.04, sides=(0,))
    sills_k(m, W, D, 0, HB, BX, BY, wb, lintel_sides=(), sides=(1, 3))
    m.box(-W / 2 - 0.05, -D / 2 - 0.3, 3.2, W / 2 + 0.05, -D / 2, 3.85, 'slab')
    band(m, W, D, 0, 3.85, HB, 0.3, 'frame')
    band(m, W, D, 0, 0, 0.4, 0.06, 'base')
    bw = W / BX
    for i in (0, 1, 3, 4):
        xa, xb = -W / 2 + i * bw + 0.3, -W / 2 + (i + 1) * bw - 0.3
        y0, y1, zh, zl, t = -D / 2 - 0.3, -D / 2 - 1.5, 3.15, 2.65, 0.06
        m.hexa([(xa, y1, zl), (xb, y1, zl), (xb, y0, zh), (xa, y0, zh),
                                        (xa, y1, zl + t), (xb, y1, zl + t), (xb, y0, zh + t), (xa, y0, zh + t)], 'awning')
    mods['base'] = m.finish(3000)

    # 楼顶：大檐口（挑 0.6m）+ 檐下齿形 + 女儿墙 + 木水塔 + 楼梯间
    m = Mesh('crown')
    band(m, W, D, 0, 0.0, 0.35, 0.6, 'slab')
    for k, p0, right, out, L in _sides(W, D, 0):
        for j in range(int((L - 0.6) / 0.7)):
            x = 0.4 + j * 0.7
            _slab_on_wall(m, p0, right, out, x, -0.3, x + 0.25, 0.0, 0.0, 0.3, 'frame')
    parapet(m, W, D, 0.35, 0.9, 0.3, region='wall', cap_region='frame')
    water_tower(m, 3.5, 2.0, 0.4)
    m.box(-6, 2, 0.4, -2.5, 5.5, 3.0, 'wall', top='roof')
    m.box(-6.12, 1.88, 3.0, -2.38, 5.62, 3.15, 'frame')
    mods['crown'] = m.finish(2400)
    return mods


# ───────── 现代格栅写字楼：22×18m，层高 3.8m，深色玻璃外罩混凝土格栅（A/B 层竖向格栅错开半个开间）─────────
def modern():
    W, D, H, T = 22.0, 18.0, 3.8, 0.35
    BX, BY = 6, 5
    glass = lambda k, i, bw, h: (0.1, 0.1, bw - 0.2, h - 0.2)
    mods = {}
    for var in ('storey_a', 'storey_b'):
        m = Mesh(var)
        ring_wall_k(m, W, D, T, H - T, BX, BY, glass, 0.25)
        slab(m, W, D, 0, T, 0.9)
        for k, p0, right, out, L in _sides(W, D, T):
            n = BX if k % 2 == 0 else BY
            bw = L / n
            xs = [i * bw for i in range(n)] if var == 'storey_a' else [i * bw + bw / 2 for i in range(n)]
            for x in xs:
                a, b = (x, x + 0.3) if x == 0 else (x - 0.15, x + 0.15)
                _slab_on_wall(m, p0, right, out, a, 0, b, H - T, 0, 0.9, 'slab')
        mods[var] = m.finish(2400)

    # 底座：7m 通高大堂（玻璃退进 2.2m）+ 壁柱 + 顶上一道和格栅齐平的横带
    m = Mesh('base')
    HB = 7.0
    lobby = lambda k, i, bw, h: (0.4, 0.0, bw - 0.8, h - 0.9)
    ring_wall_k(m, W, D, 0, HB, BX, BY, lobby, 2.2)
    mullions_k(m, W, D, 0, HB, BX, BY, 2.2, lobby, cols=2, rows_at=[0.45], bar=0.1, proud=0.06, sides=(0, 1, 3))
    band(m, W, D, 0, HB - 0.9, HB, 0.9, 'slab')
    for k, p0, right, out, L in _sides(W, D, 0):
        n = BX if k % 2 == 0 else BY
        bw = L / n
        for i in range(n):
            x = i * bw
            a, b = (x, x + 0.6) if i == 0 else (x - 0.3, x + 0.3)
            _slab_on_wall(m, p0, right, out, a, 0, b, HB - 0.9, 0, 0.9, 'slab')
    m.box(-4.0, -D / 2 - 1.2, 0, 4.0, -D / 2 - 0.9, 0.12, 'base')
    mods['base'] = m.finish(3000)

    # 楼顶：退进的设备层（四面 LED 灯带）+ 女儿墙 + 桅杆
    m = Mesh('crown')
    slab(m, W, D, 0, T, 0.9)
    parapet(m, W, D, T, 1.2, 0.3, region='wall', cap_region='slab')
    ex, ey = W / 2 - 3.5, D / 2 - 3.0
    m.box(-ex, -ey, T + 0.05, ex, ey, T + 5.0, 'wall', top='roof')
    m.box(-ex - 0.08, -ey - 0.08, T + 4.3, ex + 0.08, -ey, T + 4.55, 'neon')
    m.box(-ex - 0.2, -ey - 0.2, T + 5.0, ex + 0.2, ey + 0.2, T + 5.3, 'slab')
    for z0, z1, r in ((T + 5.3, T + 10.0, 0.25), (T + 10.0, T + 14.0, 0.14)):
        m.box(4.0 - r, 1.0 - r, z0, 4.0 + r, 1.0 + r, z1, 'metal')
    m.box(3.85, 0.85, T + 14.0, 4.15, 1.15, T + 14.35, 'neon')
    mods['crown'] = m.finish(2000)
    return mods


# ───────── 装饰艺术摩天楼：20×20m，层高 3.6m，竖向壁柱；楼顶三级退台 + 尖塔（地标）─────────
def spire():
    W = D = 20.0; H = 3.6
    BX = BY = 5
    win = lambda k, i, bw, h: (0.45, 0.6, bw - 0.9, h - 1.0)
    mods = {}
    for var in ('storey_a', 'storey_b'):
        m = Mesh(var)
        ring_wall_k(m, W, D, 0, H, BX, BY, win, 0.3)
        for k, p0, right, out, L in _sides(W, D, 0):
            bw = L / BX
            for i in range(BX):
                x = i * bw
                a, b = (x, x + 0.5) if i == 0 else (x - 0.25, x + 0.25)
                _slab_on_wall(m, p0, right, out, a, 0, b, H, 0, 0.4, 'frame')
                if var == 'storey_b':
                    _slab_on_wall(m, p0, right, out, x + 0.6, 0.15, x + bw - 0.6, 0.55, 0, 0.06, 'metal')
        mods[var] = m.finish(2400)

    # 底座：8m 高，深门洞 + 壁柱 + 大檐口 + 正门雨棚（下沿霓虹）
    m = Mesh('base')
    HB = 8.0
    lobby = lambda k, i, bw, h: (0.6, 0.0, bw - 1.2, h - 1.6)
    ring_wall_k(m, W, D, 0, HB, BX, BY, lobby, 0.8)
    mullions_k(m, W, D, 0, HB, BX, BY, 0.8, lobby, cols=3, rows_at=[0.55], bar=0.12, proud=0.06)
    for k, p0, right, out, L in _sides(W, D, 0):
        bw = L / BX
        for i in range(BX):
            x = i * bw
            a, b = (x, x + 0.6) if i == 0 else (x - 0.3, x + 0.3)
            _slab_on_wall(m, p0, right, out, a, 0, b, HB - 0.6, 0, 0.5, 'frame')
    band(m, W, D, 0, HB - 0.6, HB, 0.7, 'slab')
    m.box(-3.0, -D / 2 - 3.0, 5.0, 3.0, -D / 2 - 0.5, 5.4, 'metal', top='slab')
    m.box(-2.9, -D / 2 - 3.06, 5.05, 2.9, -D / 2 - 3.0, 5.3, 'neon')
    m.box(-3.5, -D / 2 - 2.0, 0, 3.5, -D / 2 - 0.5, 0.2, 'base')
    mods['base'] = m.finish(3000)

    # 楼顶：顶板 + 三级退台（每级都有窗，顶上一圈压顶 + 正面霓虹）+ 尖塔 + 航空灯
    m = Mesh('crown')
    m.box(-W / 2 - 0.3, -D / 2 - 0.3, 0, W / 2 + 0.3, D / 2 + 0.3, 0.5, 'slab')
    tw = lambda k, i, bw, h: (0.45, 0.8, bw - 0.9, h - 1.6)
    z = 0.5
    for (s, hh, n) in ((14.0, 7.0, 4), (9.0, 5.5, 3), (5.0, 4.0, 2)):
        ring_wall_k(m, s, s, z, hh, n, n, tw, 0.25)
        z += hh
        m.box(-s / 2 - 0.3, -s / 2 - 0.3, z, s / 2 + 0.3, s / 2 + 0.3, z + 0.45, 'slab')
        m.box(-s / 2 - 0.2, -s / 2 - 0.36, z + 0.08, s / 2 + 0.2, -s / 2 - 0.3, z + 0.32, 'neon')
        z += 0.45
    b0, b1 = 1.6, 0.12
    m.hexa([(-b0, -b0, z), (b0, -b0, z), (b0, b0, z), (-b0, b0, z),
                                    (-b1, -b1, z + 13), (b1, -b1, z + 13), (b1, b1, z + 13), (-b1, b1, z + 13)], 'frame')
    m.box(-0.2, -0.2, z + 13, 0.2, 0.2, z + 13.4, 'neon')
    mods['crown'] = m.finish(2400)
    return mods


# ───────── 底商住宅楼：18×16m，层高 3.1m；底层店面 + 遮阳棚 + 店招带，楼上住宅（阳台 / 凸窗 / 空调外机）─────────
def mixed():
    W, D, H = 18.0, 16.0, 3.1
    BX, BY = 5, 4
    win = lambda k, i, bw, h: (bw * 0.22, 0.85, bw * 0.56, 1.5)
    mods = {}
    bw = W / BX
    # A 层：正面 0 / 2 / 4 开间有阳台，侧面有空调外机
    m = Mesh('storey_a')
    ring_wall_k(m, W, D, 0, H, BX, BY, win, 0.18)
    sills_k(m, W, D, 0, H, BX, BY, win, wdt=0.1, proud=0.1, lintel_sides=(0, 2))
    band(m, W, D, 0, 0, 0.2, 0.1, 'slab')
    for i in (0, 2, 4):
        xa, xb = -W / 2 + i * bw + 0.25, -W / 2 + (i + 1) * bw - 0.25
        y0, y1 = -D / 2 - 1.2, -D / 2 - 0.1
        m.box(xa, y0, 0.2, xb, y1, 0.38, 'slab')
        m.box(xa, y0, 0.38, xb, y0 + 0.06, 1.25, 'rail')
        m.box(xa, y0 + 0.06, 0.38, xa + 0.06, y1, 1.25, 'rail')
        m.box(xb - 0.06, y0 + 0.06, 0.38, xb, y1, 1.25, 'rail')
    for k, p0, right, out, L in _sides(W, D, 0):
        if k % 2 == 0:
            continue
        b2 = L / BY
        for i in (1, 3):
            x = i * b2 + b2 * 0.5
            _slab_on_wall(m, p0, right, out, x - 0.45, 0.25, x + 0.45, 0.8, 0.1, 0.6, 'metal')
    mods['storey_a'] = m.finish(2400)

    # B 层：正面 0 / 2 / 4 开间是凸窗（三面玻璃），1 / 3 开间窗下花箱
    m = Mesh('storey_b')
    ring_wall_k(m, W, D, 0, H, BX, BY, win, 0.18)
    sills_k(m, W, D, 0, H, BX, BY, win, wdt=0.1, proud=0.1, lintel_sides=(2,), sides=(1, 2, 3))
    band(m, W, D, 0, 0, 0.2, 0.1, 'slab')
    for i in (0, 2, 4):
        xa, xb = -W / 2 + i * bw + bw * 0.14, -W / 2 + (i + 1) * bw - bw * 0.14
        m.box(xa, -D / 2 - 0.8, 0.55, xb, -D / 2 + 0.05, 2.65, 'glass', top='slab', bottom='slab')   # 凸窗贴着外墙
    for i in (1, 3):
        xa, xb = -W / 2 + i * bw + bw * 0.22, -W / 2 + (i + 1) * bw - bw * 0.22
        m.box(xa, -D / 2 - 0.45, 0.55, xb, -D / 2 + 0.02, 0.8, 'awning')   # 花箱贴着外墙
    mods['storey_b'] = m.finish(2400)

    # 底座（4.6m 店面层）：正面 + 右侧整片橱窗，正面中间是入口；店招带 + 遮阳棚 + 檐口
    m = Mesh('base')
    HB = 4.6
    def wb(k, i, bw_, h):
        if k == 0:
            return (0.25, 0.0 if i == 2 else 0.4, bw_ - 0.5, 3.3 if i == 2 else 2.9)
        if k == 1:   # 侧面橱窗离转角留够墙，避免和正面窗洞的内壁在转角处碰在一起
            return (0.5, 0.4, bw_ - 1.0, 2.9)
        return (bw_ * 0.3, 0.0, bw_ * 0.4, 2.4) if (k == 2 and i == 1) else None
    ring_wall_k(m, W, D, 0, HB, BX, BY, wb, 0.25, inner=lambda k, i: 'metal' if k == 2 else 'glass')
    mullions_k(m, W, D, 0, HB, BX, BY, 0.25, wb, cols=2, rows_at=[0.8], bar=0.07, proud=0.04, sides=(0, 1))
    m.box(-W / 2 - 0.05, -D / 2 - 0.3, 3.5, W / 2 + 0.05, -D / 2, 4.3, 'slab')
    band(m, W, D, 0, 4.3, HB, 0.3, 'frame')
    band(m, W, D, 0, 0, 0.35, 0.06, 'base')
    for i in (0, 1, 3, 4):
        xa, xb = -W / 2 + i * bw + 0.3, -W / 2 + (i + 1) * bw - 0.3
        y0, y1, zh, zl, t = -D / 2 + 0.02, -D / 2 - 1.7, 3.42, 2.85, 0.06   # 从墙面伸出（店面窗顶以上是实墙）
        m.hexa([(xa, y1, zl), (xb, y1, zl), (xb, y0, zh), (xa, y0, zh),
                                        (xa, y1, zl + t), (xb, y1, zl + t), (xb, y0, zh + t), (xa, y0, zh + t)], 'awning')
    mods['base'] = m.finish(3000)

    # 楼顶：女儿墙 + 广告牌支架 + 木水塔 + 楼梯间
    m = Mesh('crown')
    parapet(m, W, D, 0, 1.0, 0.25)
    water_tower(m, -5.0, 3.0, 0.05, r=1.1, h=2.2, leg=1.5)
    m.box(3, 3, 0.05, 7, 6.8, 2.8, 'wall', top='roof')
    m.box(2.88, 2.88, 2.8, 7.12, 6.92, 2.95, 'frame')
    for x in (-4.5, 4.5):
        m.box(x - 0.14, -5.0, 0.05, x + 0.14, -4.72, 6.4, 'metal')
    for zz in (3.0, 6.1):
        m.box(-5.0, -4.98, zz, 5.0, -4.78, zz + 0.2, 'metal')
    mods['crown'] = m.finish(2000)
    return mods


MORE_KINDS.update({'condo': condo, 'brick': brick, 'modern': modern, 'spire': spire, 'mixed': mixed})
MORE_WALL.update({'condo': [18.0, 18.0], 'brick': [16.0, 14.0], 'modern': [22.0, 18.0], 'spire': [20.0, 20.0], 'mixed': [18.0, 16.0]})
