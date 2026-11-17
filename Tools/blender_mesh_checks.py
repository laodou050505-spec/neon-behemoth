"""《我是怪兽》 · 网格体检（楼 / 车 / 塔共用，在 Blender 里 exec 载入）：共面重叠（会闪）、看得见的共面、法线反"""

def coplanar_overlaps(bm, limit=8):
    """找同一平面、同一朝向、且有面积重叠的面（Unity 里会深度冲突闪烁）。返回 (数量, 前几处位置)"""
    from collections import defaultdict
    bm.faces.ensure_lookup_table()
    buckets = defaultdict(list)
    for f in bm.faces:
        if f.calc_area() < 1e-6:
            continue
        n = f.normal.normalized()
        d = n.dot(f.verts[0].co)
        buckets[(round(n.x, 3), round(n.y, 3), round(n.z, 3), round(d, 3))].append(f)
    def proj(f, ax):
        return [(v.co[ax[0]], v.co[ax[1]]) for v in f.verts]
    def inside(pt, poly):
        x, y = pt; c = False
        for i in range(len(poly)):
            (x1, y1), (x2, y2) = poly[i], poly[i - 1]
            if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1 + 1e-12) + x1:
                c = not c
        return c
    def samples(poly):
        cx = sum(p[0] for p in poly) / len(poly); cy = sum(p[1] for p in poly) / len(poly)
        pts = [(cx, cy)]
        for (x, y) in poly:
            pts.append((x + (cx - x) * 0.25, y + (cy - y) * 0.25))
        return pts
    hits = []; pairs = []
    for key, fs in buckets.items():
        if len(fs) < 2:
            continue
        n = abs(key[0]), abs(key[1]), abs(key[2])
        drop = n.index(max(n)); ax = [i for i in range(3) if i != drop]
        P = [proj(f, ax) for f in fs]
        B = [(min(p[0] for p in q), max(p[0] for p in q), min(p[1] for p in q), max(p[1] for p in q)) for q in P]
        for i in range(len(fs)):
            for j in range(i + 1, len(fs)):
                a, b = B[i], B[j]
                if min(a[1], b[1]) - max(a[0], b[0]) < 1e-3 or min(a[3], b[3]) - max(a[2], b[2]) < 1e-3:
                    continue
                if any(inside(s, P[j]) for s in samples(P[i])) or any(inside(s, P[i]) for s in samples(P[j])):
                    hits.append(tuple(round(c, 2) for c in fs[i].calc_center_median()))
                    pairs.append((fs[i], fs[j]))
    return len(hits), hits[:limit], pairs


def visible_pairs(bm, pairs):
    """只留真看得见的共面重叠：面前方 1cm 处如果落在任何一个封闭部件里面（逐部件射线奇偶判定），就是被挡住了，不会闪；
    模型最底下朝下的面贴着地面，也看不见"""
    from mathutils.bvhtree import BVHTree
    from mathutils import Vector
    bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
    # 按边连通分出各个封闭部件
    seen, comps = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, comp = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop(); comp.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index); stack.append(h)
        comps.append(comp)
    trees = []
    for comp in comps:
        vs = {}
        polys = []
        for f in comp:
            polys.append([vs.setdefault(v.index, len(vs)) for v in f.verts])
        co = [None] * len(vs)
        for vi, k in vs.items():
            co[k] = bm.verts[vi].co.copy()
        lo = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
        hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
        trees.append((lo, hi, BVHTree.FromPolygons(co, polys)))
    zmin = min(v.co.z for v in bm.verts)
    dirs = [Vector((0.137, 0.271, 0.953)).normalized(), Vector((-0.61, 0.33, -0.72)).normalized(), Vector((0.8, -0.55, 0.2)).normalized()]
    def inside_one(p, tree):
        votes = 0
        for d in dirs:
            n, o = 0, p.copy()
            for _ in range(64):
                hit = tree.ray_cast(o, d)
                if hit[0] is None:
                    break
                n += 1; o = hit[0] + d * 1e-5
            votes += n % 2
        return votes >= 2
    def inside(p):
        for lo, hi, t in trees:
            if lo.x - 1e-4 <= p.x <= hi.x + 1e-4 and lo.y - 1e-4 <= p.y <= hi.y + 1e-4 and lo.z - 1e-4 <= p.z <= hi.z + 1e-4:
                if inside_one(p, t):
                    return True
        return False
    out = []
    for fa, fb in pairs:
        n = fa.normal.normalized()
        c = fa.calc_center_median() if fa.calc_area() <= fb.calc_area() else fb.calc_center_median()
        if n.z < -0.9 and c.z < zmin + 0.05:
            continue
        if not inside(c + n * 0.01):
            out.append((fa, fb))
    return out


def flipped_faces(bm, limit=6):
    """法线朝里的面（Unity 背面剔除后会变成看穿的洞）：面中心往法线反方向 3mm 应该在自己所属的封闭部件里面"""
    from mathutils.bvhtree import BVHTree
    from mathutils import Vector
    bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
    comp_of, comps, seen = {}, [], set()
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, comp = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop(); comp.append(g); comp_of[g.index] = len(comps)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index); stack.append(h)
        comps.append(comp)
    trees = []
    for comp in comps:
        vs, polys = {}, []
        for f in comp:
            polys.append([vs.setdefault(v.index, len(vs)) for v in f.verts])
        co = [None] * len(vs)
        for vi, k in vs.items():
            co[k] = bm.verts[vi].co.copy()
        trees.append(BVHTree.FromPolygons(co, polys))
    dirs = [Vector((0.137, 0.271, 0.953)).normalized(), Vector((-0.61, 0.33, -0.72)).normalized(), Vector((0.8, -0.55, 0.2)).normalized()]
    def inside(p, tree):
        votes = 0
        for d in dirs:
            n, o = 0, p.copy()
            for _ in range(64):
                hit = tree.ray_cast(o, d)
                if hit[0] is None:
                    break
                n += 1; o = hit[0] + d * 1e-5
            votes += n % 2
        return votes >= 2
    bad = []
    for f in bm.faces:
        if f.calc_area() < 1e-3:
            continue
        vs_ = [v.co for v in f.verts]
        c = (vs_[0] + vs_[1] + vs_[2]) / 3   # 第一个三角形的重心（细长四边形的顶点平均点可能落在面外）
        if not inside(c - f.normal.normalized() * 0.003, trees[comp_of[f.index]]):
            bad.append(tuple(round(x, 2) for x in c))
    return len(bad), bad[:limit]


def fix_coplanar(bm):
    """把看得见的共面重叠里较小的那块沿法线外推 4mm（顶点一起动，网格仍封闭）"""
    _n, _h, pairs = coplanar_overlaps(bm)
    moved = set()
    bm.verts.index_update()
    for fa, fb in visible_pairs(bm, pairs):
        f = fa if fa.calc_area() <= fb.calc_area() else fb
        nrm = f.normal.normalized()
        for v in f.verts:
            if v.index not in moved:
                v.co += nrm * 0.004; moved.add(v.index)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


def visible_coplanar_count(bm):
    return len(visible_pairs(bm, coplanar_overlaps(bm)[2]))


def support_graph(bm, touch=0.012):
    """零件（按边连通的封闭块）+ 相连关系 + 落地零件。返回 (comps, info, adj, ground)"""
    from mathutils.bvhtree import BVHTree
    from mathutils import Vector
    bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
    seen, comps = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, comp = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop(); comp.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index); stack.append(h)
        comps.append(comp)
    info = []
    for comp in comps:
        vs, polys = {}, []
        for f in comp:
            polys.append([vs.setdefault(v.index, len(vs)) for v in f.verts])
        co = [None] * len(vs)
        for vi, k in vs.items():
            co[k] = bm.verts[vi].co.copy()
        lo = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
        hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
        # 采样点：顶点 + 每个面的中心 + 大面上的 3×3 点
        sp = list(co)
        for f in comp:
            pts = [v.co for v in f.verts]
            c = sum(pts, Vector()) / len(pts); sp.append(c)
            if f.calc_area() > 0.3:
                for p in pts:
                    sp.append(c + (p - c) * 0.33); sp.append(c + (p - c) * 0.75)
        info.append(dict(lo=lo, hi=hi, tree=BVHTree.FromPolygons(co, polys), sp=sp))
    n = len(info)
    zmin = min(i['lo'].z for i in info)
    adj = [dict() for _ in range(n)]   # adj[a][b] = (嵌入?, 竖直面接触?, [接触点])
    for a in range(n):
        A = info[a]
        for b in range(a + 1, n):
            B = info[b]
            if any(A['lo'][k] > B['hi'][k] + touch or B['lo'][k] > A['hi'][k] + touch for k in range(3)):
                continue
            emb = len(A['tree'].overlap(B['tree'])) > 0
            pa, pb, va, vb = [], [], False, False
            for (S, T, out, side) in ((A, B, pa, 'a'), (B, A, pb, 'b')):
                for p in S['sp']:
                    hit = T['tree'].find_nearest(p, touch)
                    if hit[0] is not None:
                        out.append(hit[0])
                        if abs(hit[1].z) < 0.5:
                            if side == 'a': va = True
                            else: vb = True
            if emb or pa or pb:
                adj[a][b] = (emb, va, pa + pb)   # a 贴在 b 的竖直面上 → va
                adj[b][a] = (emb, vb, pa + pb)
    ground = [i for i in range(n) if info[i]['lo'].z < zmin + 0.02]
    return comps, info, adj, ground


def floating_parts(bm, touch=0.012, limit=8):
    """悬空 / 不稳检查（按物理）：
    - 零件之间“相连”= 表面相交（嵌在一起）或采样点离对方表面 < touch；
    - 从落地的零件（最低点 = 模型最低点）出发沿相连关系走不到的零件 → 悬空；
    - 走得到、但只靠上下面接触（没有嵌入、也没有贴在别人的竖直面上）的零件：重心（包围盒中心）的水平投影
      要落在接触点围成的凸包里，否则 → 半悬空（比如货柜一半伸出船尾、雨棚只搭了一边）"""
    comps, info, adj, ground = support_graph(bm, touch)
    n = len(info)
    ok = set(ground); stack = list(ground)
    while stack:
        i = stack.pop()
        for j in adj[i]:
            if j not in ok:
                ok.add(j); stack.append(j)
    def hull_contains(pts, q, margin):
        pts = sorted(set((round(p.x, 3), round(p.y, 3)) for p in pts))
        if not pts:
            return False
        def cross(o, a, b): return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
        lower, upper = [], []
        for p in pts:
            while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0: lower.pop()
            lower.append(p)
        for p in reversed(pts):
            while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0: upper.pop()
            upper.append(p)
        h = lower[:-1] + upper[:-1]
        if len(pts) < 3 or len(h) < 3:
            # 接触点退化成一个点 / 一条线（零件立在一根细柱正上方）：重心离这条线足够近就算稳
            def segd(a, b):
                ax, ay = b[0] - a[0], b[1] - a[1]; L2 = ax * ax + ay * ay
                t = 0 if L2 == 0 else max(0, min(1, ((q[0] - a[0]) * ax + (q[1] - a[1]) * ay) / L2))
                return ((q[0] - a[0] - ax * t) ** 2 + (q[1] - a[1] - ay * t) ** 2) ** 0.5
            return segd(pts[0], pts[-1]) <= margin
        for k in range(len(h)):
            a, b = h[k], h[(k + 1) % len(h)]
            L = ((b[0] - a[0]) ** 2 + (b[1] - a[1]) ** 2) ** 0.5
            if L > 0 and cross(a, b, q) / L < -margin:
                return False
        return True
    bad = []
    for i in range(n):
        I = info[i]
        c = (I['lo'] + I['hi']) / 2
        tag = tuple(round(x, 2) for x in c)
        if i not in ok:
            bad.append(('悬空', tag)); continue
        if i in ground:
            continue
        if any(e or v for (e, v, _) in adj[i].values()):
            continue
        pts = [p for (_, _, ps) in adj[i].values() for p in ps]
        size = max(I['hi'].x - I['lo'].x, I['hi'].y - I['lo'].y)
        if not hull_contains(pts, (c.x, c.y), size * 0.03 + 0.01):
            bad.append(('半悬空（重心不在支撑范围内）', tag))
    return len(bad), bad[:limit]



def lod_kill_safe(bm, kill_pred, touch=0.012):
    """远处简化版要删的零件：kill_pred(零件包围盒尺寸 d 升序, 体积) → True 就删；
    删完以后还留着的零件如果因此悬空（比如灯头的灯杆被删了），沿最短的支撑路径把删掉的零件补回来。返回要删的面"""
    comps, info, adj, ground = support_graph(bm, touch)
    n = len(comps)
    kill = set()
    for i, I in enumerate(info):
        d = sorted(I['hi'][k] - I['lo'][k] for k in range(3))
        if kill_pred(d, d[0] * d[1] * d[2]):
            kill.add(i)
    for _ in range(8):
        alive = [i for i in range(n) if i not in kill]
        ok = set(i for i in ground if i not in kill); stack = list(ok)
        while stack:
            i = stack.pop()
            for j in adj[i]:
                if j not in ok and j not in kill:
                    ok.add(j); stack.append(j)
        orphans = [i for i in alive if i not in ok]
        if not orphans:
            break
        # 从地面出发（可以走被删的零件）的最短路径，把路上被删的零件补回来
        prev = {g: None for g in ground}; q = list(ground)
        while q:
            i = q.pop(0)
            for j in adj[i]:
                if j not in prev:
                    prev[j] = i; q.append(j)
        for o in orphans:
            j = prev.get(o)
            while j is not None:
                kill.discard(j); j = prev.get(j)
    return [f for i in kill for f in comps[i]]
