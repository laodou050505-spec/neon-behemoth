# 量纸张贴图上的版心：可见纸边、页眉黑条、粉线、竖向 / 横向版心线（都换算成占整张图的比例）
import struct, zlib, sys
d = open(sys.argv[1], 'rb').read()
w, h = struct.unpack('>II', d[16:24])
pos = 8; idat = b''
while pos < len(d):
    ln, typ = struct.unpack('>I4s', d[pos:pos + 8])
    if typ == b'IDAT': idat += d[pos + 8:pos + 8 + ln]
    pos += 12 + ln
raw = zlib.decompress(idat); bpp = 4; stride = w * bpp
rows = []; prev = bytearray(stride); i = 0
for y in range(h):
    f = raw[i]; cur = bytearray(raw[i + 1:i + 1 + stride]); i += 1 + stride
    if f == 1:
        for x in range(bpp, stride): cur[x] = (cur[x] + cur[x - bpp]) & 255
    elif f == 2:
        for x in range(stride): cur[x] = (cur[x] + prev[x]) & 255
    elif f == 3:
        for x in range(stride): cur[x] = (cur[x] + (((cur[x - bpp] if x >= bpp else 0) + prev[x]) >> 1)) & 255
    elif f == 4:
        for x in range(stride):
            a = cur[x - bpp] if x >= bpp else 0; b = prev[x]; c = prev[x - bpp] if x >= bpp else 0
            p = a + b - c; pa = abs(p - a); pb = abs(p - b); pc = abs(p - c)
            cur[x] = (cur[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
    rows.append(cur); prev = cur
def px(x, y): o = x * 4; r = rows[y]; return r[o], r[o + 1], r[o + 2], r[o + 3]
def lum(x, y): r, g, b, a = px(x, y); return (r * 3 + g * 6 + b) / 10
# 可见纸边（alpha）
for fy in (0.2, 0.5, 0.8):
    y = int(h * fy); xs = [x for x in range(w) if px(x, y)[3] > 128]
    print(f'row {fy}: paper x {xs[0] / w:.3f} .. {xs[-1] / w:.3f}')
ys = [y for y in range(h) if px(w // 2, y)[3] > 128]; print(f'paper y {ys[0] / h:.3f} .. {ys[-1] / h:.3f} (from top)')
# 页眉黑条：中间一列上连续的深色段
col = [lum(w // 2, y) for y in range(h)]
dark = [y for y in range(h) if col[y] < 70 and px(w // 2, y)[3] > 128]
print(f'header bar y {dark[0] / h:.3f} .. {dark[-1] / h:.3f}')
ym = (dark[0] + dark[-1]) // 2
xs = [x for x in range(w) if lum(x, ym) < 70 and px(x, ym)[3] > 128]; print(f'header bar x {xs[0] / w:.3f} .. {xs[-1] / w:.3f}')
# 粉线
pink = [y for y in range(h) if px(w // 2, y)[0] > 200 and px(w // 2, y)[1] < 120 and px(w // 2, y)[2] > 90]
if pink: print(f'pink line y {pink[0] / h:.3f} .. {pink[-1] / h:.3f}')
# 竖向版心线：正文区（页眉以下）每一列的平均亮度，比左右邻居明显暗的细线
y0, y1 = int(h * 0.35), int(h * 0.75)
cm = [sum(lum(x, y) for y in range(y0, y1, 8)) / len(range(y0, y1, 8)) for x in range(w)]
vl = [x for x in range(6, w - 6) if cm[x] < cm[x - 5] - 12 and cm[x] < cm[x + 5] - 12 and px(x, (y0 + y1) // 2)[3] > 128]
grp = []
for x in vl:
    if grp and x - grp[-1][-1] <= 3: grp[-1].append(x)
    else: grp.append([x])
print('vertical rules x:', [round(sum(g) / len(g) / w, 3) for g in grp])
x0, x1 = int(w * 0.3), int(w * 0.7)
rm = [sum(lum(x, y) for x in range(x0, x1, 8)) / len(range(x0, x1, 8)) for y in range(h)]
hl = [y for y in range(6, h - 6) if rm[y] < rm[y - 5] - 12 and rm[y] < rm[y + 5] - 12]
grp = []
for y in hl:
    if grp and y - grp[-1][-1] <= 3: grp[-1].append(y)
    else: grp.append([y])
print('horizontal rules y:', [round(sum(g) / len(g) / h, 3) for g in grp])
