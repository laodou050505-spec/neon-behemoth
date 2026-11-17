"""《我是怪兽》 · 楼顶附件共用图集（1024，区域和 Tools/blender_props.py 的 ATLAS 一致）：
    python3 Tools/compose_props.py <输出目录>
金属 / 灰泥区用混凝土、灰泥照片的细节，锈蚀钢、铜管、保温管程序生成（带锈斑、污渍、划痕）。
输出 albedo.png、mask.png（R 金属 / G AO / A 光滑度）、normal.png。"""
import sys, pathlib
import numpy as np
from PIL import Image

OUT = pathlib.Path(sys.argv[1]); OUT.mkdir(parents=True, exist_ok=True)
TEX = pathlib.Path(__file__).resolve().parents[1] / 'ArtSource' / 'Textures'
N = 1024
rng = np.random.default_rng(11)
ATLAS = {
    'metal': (0.00, 0.50, 0.50, 1.00), 'dark': (0.50, 0.50, 1.00, 1.00),
    'rust': (0.00, 0.00, 0.50, 0.50), 'copper': (0.50, 0.25, 0.75, 0.50),
    'rubber': (0.75, 0.25, 1.00, 0.50), 'stucco': (0.50, 0.00, 1.00, 0.25),
}
# 区域：底色、金属度、光滑度、照片（细节来源）、锈斑强度
LOOK = {
    'metal': ('#b9bcbd', 0.55, 0.45, 'concrete_clean', 0.35),
    'dark': ('#3a3d42', 0.6, 0.4, 'concrete_clean', 0.1),
    'rust': ('#7d7a74', 0.5, 0.3, 'concrete', 0.8),
    'copper': ('#b26a3f', 0.9, 0.6, None, 0.2),
    'rubber': ('#2a2b2d', 0.0, 0.25, None, 0.0),
    'stucco': ('#d8cfc4', 0.0, 0.15, 'stucco', 0.15),
}


def hexrgb(h):
    h = h.lstrip('#'); return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32) / 255


def noise(w, h, scale, octaves=4):
    acc = np.zeros((h, w), np.float32); amp, tot = 1.0, 0
    for o in range(octaves):
        s = max(2, int(scale * 2 ** o))
        small = rng.random((s, s)).astype(np.float32)
        acc += np.asarray(Image.fromarray((small * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)).astype(np.float32) / 255 * amp
        tot += amp; amp *= 0.5
    return acc / tot


def tile(name, kind, w, h):
    p = TEX / name / f'{kind}.png'
    if not p.exists():
        return None
    t = np.asarray(Image.open(p).convert('RGB')).astype(np.float32) / 255
    return np.tile(t, (int(np.ceil(h / t.shape[0])), int(np.ceil(w / t.shape[1])), 1))[:h, :w]


albedo = np.zeros((N, N, 3), np.float32)
mask = np.zeros((N, N, 4), np.float32); mask[..., 1] = 1
nrm = np.ones((N, N, 3), np.float32) * np.array([0.5, 0.5, 1.0], np.float32)
for name, (u0, v0, u1, v1) in ATLAS.items():
    x0, x1 = int(u0 * N), int(u1 * N); y0, y1 = int((1 - v1) * N), int((1 - v0) * N)
    w, h = x1 - x0, y1 - y0
    col, metal, smooth, ph, rust = LOOK[name]
    base = hexrgb(col)
    img = np.ones((h, w, 3), np.float32) * base
    t = tile(ph, 'albedo', w, h) if ph else None
    if t is not None:
        lum = t @ np.array([0.299, 0.587, 0.114], np.float32)
        img = base * np.clip(0.35 + 0.7 * lum[..., None] / max(lum.mean(), 1e-3), 0, 1.25)
        n = tile(ph, 'normal', w, h)
        if n is not None: nrm[y0:y1, x0:x1] = n
    img *= (0.9 + 0.2 * noise(w, h, 24, 3))[..., None]
    # 锈斑：低频斑块 + 往下淌的锈痕
    if rust > 0:
        spots = np.clip((noise(w, h, 6, 4) - 0.55) * 4, 0, 1) * rust
        cols = noise(w, 1, w / 20, 2)[0]
        drip = np.clip((cols - 0.6) * 3, 0, 1)[None, :] * np.linspace(0.2, 1, h, dtype=np.float32)[:, None] * rust * 0.6
        r = np.clip(spots + drip, 0, 1)[..., None]
        img = img * (1 - r) + hexrgb('#8a4a28') * (0.7 + 0.3 * noise(w, h, 40, 2))[..., None] * r
        metal_map = metal * (1 - r[..., 0] * 0.8)
        smooth_map = smooth * (1 - r[..., 0] * 0.7)
    else:
        metal_map = np.full((h, w), metal, np.float32); smooth_map = np.full((h, w), smooth, np.float32)
    if name == 'copper':   # 铜绿
        g = np.clip((noise(w, h, 8, 3) - 0.6) * 3, 0, 1)[..., None]
        img = img * (1 - g) + hexrgb('#5f9a84') * g
    albedo[y0:y1, x0:x1] = np.clip(img, 0, 1)
    mask[y0:y1, x0:x1, 0] = metal_map
    mask[y0:y1, x0:x1, 3] = smooth_map * (0.85 + 0.3 * noise(w, h, 12, 2))

Image.fromarray((albedo * 255 + 0.5).astype(np.uint8)).save(OUT / 'albedo.png')
Image.fromarray((np.clip(mask, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT / 'mask.png')
Image.fromarray((nrm * 255 + 0.5).astype(np.uint8)).save(OUT / 'normal.png')
print('附件图集 →', OUT, flush=True)
