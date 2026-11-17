"""《我是怪兽》 · 把 nano banana 生成的材质照片做成可平铺贴图 + 法线 + 粗糙度（不依赖 Blender，python3 -I 运行）：
    python3 -I Tools/make_tileable.py <source.png> <输出目录> [边长=1024] [凹凸强度=2.0]
1. 去掉低频光照不均（大半径高斯模糊相除），让整张亮度均匀，平铺不会出现明暗格子。
2. 偏移半张 + 十字羽化混合，消掉接缝（输出前后对比接缝差）。
3. 从亮度算高度 → Sobel 求法线（OpenGL 约定，Unity 导入时设为 Normal map），亮度反推粗糙度（脏的地方更糙）。
输出：albedo.png、normal.png、mask.png（R=金属 0，G=AO 1，A=光滑度，供 URP Lit 的 Metallic 贴图）。"""
import sys, pathlib
import numpy as np
from PIL import Image, ImageFilter

src = pathlib.Path(sys.argv[1]); out = pathlib.Path(sys.argv[2]); out.mkdir(parents=True, exist_ok=True)
N = int(sys.argv[3]) if len(sys.argv) > 3 else 1024
bump = float(sys.argv[4]) if len(sys.argv) > 4 else 2.0

img = Image.open(src).convert('RGB').resize((N, N), Image.LANCZOS)
a = np.asarray(img).astype(np.float32) / 255


def seam(x):
    return float(np.abs(x[:, 0] - x[:, -1]).mean() + np.abs(x[0] - x[-1]).mean()) / 2


print('原图接缝差', round(seam(a) * 255, 2), '图内相邻差', round(float(np.abs(a[:, 1:] - a[:, :-1]).mean()) * 255, 2))

# 1. 去低频光照
blur = np.asarray(img.filter(ImageFilter.GaussianBlur(N / 6))).astype(np.float32) / 255
mean = a.reshape(-1, 3).mean(0)
a = np.clip(a / np.maximum(blur, 1e-3) * mean, 0, 1)

# 2. 偏移半张，十字区域羽化混合原图
sh = np.roll(np.roll(a, N // 2, 0), N // 2, 1)
yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
# 越靠近图中心十字（偏移后的接缝位置）越多用原图
d = np.minimum(np.abs(xx - N / 2), np.abs(yy - N / 2)) / (N * 0.12)
w = np.clip(1 - d, 0, 1) ** 1.5
w = w[..., None]
t = sh * (1 - w) + a * w
print('平铺后接缝差', round(seam(t) * 255, 2))
Image.fromarray((t * 255 + 0.5).astype(np.uint8)).save(out / 'albedo.png')

# 3. 法线：亮度 → 高度（缝、坑、污渍偏暗 = 凹）
lum = t @ np.array([0.299, 0.587, 0.114], np.float32)
h = np.asarray(Image.fromarray((lum * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2))).astype(np.float32) / 255
gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * bump
gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * bump
nrm = np.stack([-gx, gy, np.ones_like(h)], -1)
nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
Image.fromarray(((nrm * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)).save(out / 'normal.png')

# 粗糙度：整体偏糙，越暗（越脏）越糙；URP 用光滑度 = 1 - 粗糙度，放在 A 通道
rough = np.clip(0.72 + (0.5 - lum) * 0.4, 0.45, 0.95)
mask = np.zeros((N, N, 4), np.float32)
mask[..., 1] = 1
mask[..., 3] = 1 - rough
Image.fromarray((mask * 255 + 0.5).astype(np.uint8), 'RGBA').save(out / 'mask.png')
print('输出', out, N, 'x', N)
