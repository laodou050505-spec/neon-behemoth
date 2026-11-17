"""《我是怪兽》 · 色板 UV 的程序化零件工具（塔 / 船 / 吊车共用，Blender 里 exec 载入；依赖 blender_mesh_checks.py）"""

class Part:
    def __init__(self):
        self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new('UVMap'); self.groups = []; self.mat = Matrix.Identity(4)

    def face(self, pts, pal, g):
        vs = [self.bm.verts.new(self.mat @ Vector(p)) for p in pts]
        g.extend(vs)
        f = self.bm.faces.new(vs)
        for l in f.loops: l[self.uv].uv = ((pal + 0.5) / 16.0, 0.5)

    def hexa(self, c, pal):
        g = []
        for idx in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
            self.face([c[i] for i in idx], pal, g)
        self.groups.append(g)

    def box(self, x0, y0, z0, x1, y1, z1, pal):
        x0, x1 = sorted((x0, x1)); y0, y1 = sorted((y0, y1)); z0, z1 = sorted((z0, z1))
        self.hexa([(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], pal)

    def prism(self, ring, z0, z1, pal, ring1=None):
        """xy 多边形（逆时针）从 z0 挤到 z1；ring1 给定时顶面换成另一个同点数多边形（锥台）"""
        ring1 = ring1 or ring
        g = []; n = len(ring)
        for i in range(n):
            a, b = ring[i], ring[(i + 1) % n]; a1, b1 = ring1[i], ring1[(i + 1) % n]
            self.face([(a[0], a[1], z0), (b[0], b[1], z0), (b1[0], b1[1], z1), (a1[0], a1[1], z1)], pal, g)
        self.face([(p[0], p[1], z0) for p in ring[::-1]], pal, g)
        self.face([(p[0], p[1], z1) for p in ring1], pal, g)
        self.groups.append(g)

    def cyl(self, cx, cy, z0, z1, r, seg, pal, r1=None):
        ring = [(cx + r * math.cos(2 * math.pi * (k + 0.5) / seg), cy + r * math.sin(2 * math.pi * (k + 0.5) / seg)) for k in range(seg)]
        ring1 = None if r1 is None else [(cx + r1 * math.cos(2 * math.pi * (k + 0.5) / seg), cy + r1 * math.sin(2 * math.pi * (k + 0.5) / seg)) for k in range(seg)]
        self.prism(ring, z0, z1, pal, ring1)

    def slab(self, a, b, width, thick, pal, shrink=0.06):
        """斜面薄板（风挡）：a、b 是 yz 平面上的两端，往外凸 thick"""
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

    def finish(self, name):
        for g in self.groups:
            bmesh.ops.remove_doubles(self.bm, verts=[v for v in g if v.is_valid], dist=1e-5)
        # 不共面的四边形（棱台侧面上下截面形状不同时会扭）先拆成三角形，否则法线方向不确定
        def warped(f):
            if len(f.verts) != 4: return False
            n = f.normal; c = f.calc_center_median()
            return max(abs((v.co - c).dot(n)) for v in f.verts) > 0.001
        ng = [f for f in self.bm.faces if len(f.verts) > 4 or warped(f)]
        if ng: bmesh.ops.triangulate(self.bm, faces=ng, quad_method='BEAUTY', ngon_method='EAR_CLIP')
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        fix_coplanar(self.bm)
        rep = {'tris': sum(len(f.verts) - 2 for f in self.bm.faces),
                                    'open_edges': sum(1 for e in self.bm.edges if len(e.link_faces) == 1),
                                    'nonmanifold': sum(1 for e in self.bm.edges if len(e.link_faces) > 2),
                                    'coplanar': visible_coplanar_count(self.bm), 'flipped': flipped_faces(self.bm)[0], 'floating': floating_parts(self.bm)[0]}
        if rep['floating']: print('  悬空', name, floating_parts(self.bm)[1], flush=True)
        if rep['flipped']: print('  法线反了', name, flipped_faces(self.bm)[1], flush=True)
        me = bpy.data.meshes.new(name); self.bm.to_mesh(me); self.bm.free()
        ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
        return ob, rep


def octa(r, rot=math.pi / 8):
    return [(r * math.cos(rot + k * math.pi / 4), r * math.sin(rot + k * math.pi / 4)) for k in range(8)]
