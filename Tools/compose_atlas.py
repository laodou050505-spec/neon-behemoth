"""《我是怪兽》 · 拼一种楼的贴图图集（2048，区域划分和 Tools/blender_buildings.py 的 ATLAS 一致）：
    python3 Tools/compose_atlas.py <楼型> <输出目录>
有 nano banana 生成的材质（ArtSource/Textures/<名>/albedo.png）就用它平铺进对应区域；没有的区域先用程序生成的写实占位
（带噪点、污渍、雨痕），之后正式贴图到了直接重跑本脚本替换，模型和 UV 不用动。
输出：albedo.png（RGB）、mask.png（URP Lit 用：R 金属、G AO、A 光滑度）、emission.png（窗灯 / 霓虹）。"""
import sys, pathlib
import numpy as np
from PIL import Image, ImageFilter

KIND, OUT = sys.argv[1], pathlib.Path(sys.argv[2])
OUT.mkdir(parents=True, exist_ok=True)
ROOT = pathlib.Path(__file__).resolve().parents[1]
TEX = ROOT / 'ArtSource' / 'Textures'
N = 2048
rng = np.random.default_rng(7)

ATLAS = {
    'wall': (0.00, 0.50, 0.50, 1.00), 'glass': (0.50, 0.50, 1.00, 1.00),
    'frame': (0.00, 0.40, 0.25, 0.50), 'slab': (0.25, 0.40, 0.50, 0.50),
    'roof': (0.50, 0.25, 0.75, 0.50), 'rail': (0.75, 0.25, 1.00, 0.50),
    'base': (0.00, 0.00, 0.50, 0.40), 'metal': (0.50, 0.00, 0.75, 0.25),
    'neon': (0.75, 0.00, 1.00, 0.125), 'awning': (0.75, 0.125, 1.00, 0.25),
}
# 每种楼各区域的底色、金属度、光滑度、用哪张照片材质（有的话）
LOOK = {
    'glass_tower': {
        'wall': ('#6e9fd6', 0.0, 0.25, None), 'glass': ('#ffffff', 0.0, 0.92, None),
        'frame': ('#3f6f9e', 0.0, 0.55, None), 'slab': ('#cfe0f0', 0.0, 0.3, None),
        'roof': ('#8a8680', 0.0, 0.15, None), 'rail': ('#2c4650', 0.0, 0.92, None),
        'base': ('#7894b8', 0.0, 0.35, None), 'metal': ('#4d5f70', 0.0, 0.45, None),
        'neon': ('#ff5fb4', 0.0, 0.6, None), 'awning': ('#e0e0e0', 0.0, 0.2, None),
    },
    'deco_hotel': {
        'wall': ('#f0b8b0', 0.0, 0.2, None), 'glass': ('#ffffff', 0.0, 0.9, None),
        'frame': ('#f4efe6', 0.0, 0.3, None), 'slab': ('#f6f1e8', 0.0, 0.25, None),
        'roof': ('#958c82', 0.0, 0.15, None), 'rail': ('#d9d4cc', 0.0, 0.5, None),
        'base': ('#e9c8b8', 0.0, 0.3, None), 'metal': ('#4f5358', 0.0, 0.4, None),
        'neon': ('#3ef0ff', 0.0, 0.6, None), 'awning': ('#3fb4a8', 0.0, 0.2, None),
    },
    'office': {
        'wall': ('#efe2c8', 0.0, 0.2, None), 'glass': ('#ffffff', 0.0, 0.9, None),
        'frame': ('#f6f3ec', 0.0, 0.3, None), 'slab': ('#d9c9aa', 0.0, 0.25, None),
        'roof': ('#8f8a84', 0.0, 0.15, None), 'rail': ('#34505a', 0.0, 0.8, None),
        'base': ('#c2ab8e', 0.0, 0.3, None), 'metal': ('#4a4f55', 0.0, 0.4, None),
        'neon': ('#ffb23a', 0.0, 0.6, None), 'awning': ('#e07a3f', 0.0, 0.2, None),
    },
    'apartment': {
        'wall': ('#7fd3b4', 0.0, 0.2, None), 'glass': ('#ffffff', 0.0, 0.9, None),
        'frame': ('#fbf8f2', 0.0, 0.3, None), 'slab': ('#f4efe4', 0.0, 0.25, None),
        'roof': ('#9a948c', 0.0, 0.15, None), 'rail': ('#e6efec', 0.0, 0.5, None),
        'base': ('#f4b48c', 0.0, 0.3, None), 'metal': ('#5c646e', 0.0, 0.4, None),
        'neon': ('#ff5fb4', 0.0, 0.6, None), 'awning': ('#ff8a5c', 0.0, 0.2, None),
    },
    'garage': {
        'wall': ('#cdc9c0', 0.0, 0.2, None), 'glass': ('#ffffff', 0.0, 0.9, None),
        'frame': ('#f08a2c', 0.0, 0.3, None), 'slab': ('#b6b2aa', 0.0, 0.25, None),
        'roof': ('#5e5d63', 0.0, 0.15, None), 'rail': ('#2d3a44', 0.0, 0.8, None),
        'base': ('#d9d5cc', 0.0, 0.3, None), 'metal': ('#26262c', 0.0, 0.4, None),
        'neon': ('#3ef0ff', 0.0, 0.6, None), 'awning': ('#e8463c', 0.0, 0.2, None),
    },
    'shop': {
        'wall': ('#f6d77c', 0.0, 0.2, None), 'glass': ('#ffffff', 0.0, 0.9, None),
        'frame': ('#fbf8f0', 0.0, 0.3, None), 'slab': ('#f7efdc', 0.0, 0.25, None),
        'roof': ('#958c82', 0.0, 0.15, None), 'rail': ('#30444c', 0.0, 0.8, None),
        'base': ('#df9f78', 0.0, 0.3, None), 'metal': ('#3e4248', 0.0, 0.4, None),
        'neon': ('#ff5fb4', 0.0, 0.6, None), 'awning': ('#e8463c', 0.0, 0.2, None),
    },
    'villa': {
        'wall': ('#f6eee0', 0.0, 0.2, None), 'glass': ('#ffffff', 0.0, 0.9, None),
        'frame': ('#f9f5ec', 0.0, 0.3, None), 'slab': ('#ede3d2', 0.0, 0.25, None),
        'roof': ('#c8603a', 0.0, 0.15, None), 'rail': ('#f6eee0', 0.0, 0.5, None),
        'base': ('#d6bf9e', 0.0, 0.3, None), 'metal': ('#5a4c42', 0.0, 0.4, None),
        'neon': ('#ffcf86', 0.0, 0.6, None), 'awning': ('#5fa58a', 0.0, 0.2, None),
    },
    'warehouse': {
        'wall': ('#b9604a', 0.0, 0.2, None), 'glass': ('#ffffff', 0.0, 0.9, None),
        'frame': ('#58626e', 0.0, 0.3, None), 'slab': ('#cfcac0', 0.0, 0.25, None),
        'roof': ('#94a0a6', 0.0, 0.15, None), 'rail': ('#2d3a44', 0.0, 0.8, None),
        'base': ('#bdb8ae', 0.0, 0.3, None), 'metal': ('#8c979d', 0.0, 0.4, None),
        'neon': ('#ffcf5a', 0.0, 0.6, None), 'awning': ('#e8b23a', 0.0, 0.2, None),
    },
    'gas_station': {
        'wall': ('#f2f1ec', 0.0, 0.2, None), 'glass': ('#ffffff', 0.0, 0.9, None),
        'frame': ('#e2463c', 0.0, 0.3, None), 'slab': ('#fbfbf8', 0.0, 0.25, None),
        'roof': ('#9a9a98', 0.0, 0.15, None), 'rail': ('#30444c', 0.0, 0.8, None),
        'base': ('#f6f6f2', 0.0, 0.3, None), 'metal': ('#3a3e44', 0.0, 0.4, None),
        'neon': ('#3ef0ff', 0.0, 0.6, None), 'awning': ('#e2463c', 0.0, 0.2, None),
    },
    'condo': {
        'wall': ('#f4f1ea', 0.0, 0.2, None), 'glass': ('#ffffff', 0.0, 0.9, None),
        'frame': ('#dfe6ea', 0.0, 0.3, None), 'slab': ('#fbfbf8', 0.0, 0.25, None),
        'roof': ('#9a9a98', 0.0, 0.15, None), 'rail': ('#8fc3d6', 0.0, 0.8, None),
        'base': ('#e8e4dc', 0.0, 0.3, None), 'metal': ('#4a5058', 0.0, 0.4, None),
        'neon': ('#3ef0ff', 0.0, 0.6, None), 'awning': ('#3fb4a8', 0.0, 0.2, None),
    },
    'brick': {
        'wall': ('#a5523e', 0.0, 0.2, None), 'glass': ('#ffffff', 0.0, 0.9, None),
        'frame': ('#efe6d8', 0.0, 0.3, None), 'slab': ('#d8cdb8', 0.0, 0.25, None),
        'roof': ('#6f6a66', 0.0, 0.15, None), 'rail': ('#2c2c30', 0.0, 0.8, None),
        'base': ('#8f8578', 0.0, 0.3, None), 'metal': ('#2b2b30', 0.0, 0.4, None),
        'neon': ('#ff5fb4', 0.0, 0.6, None), 'awning': ('#8a5a3a', 0.0, 0.2, None),
    },
    'modern': {
        'wall': ('#2f3440', 0.0, 0.2, None), 'glass': ('#ffffff', 0.0, 0.9, None),
        'frame': ('#c9ccd2', 0.0, 0.3, None), 'slab': ('#dcd8cf', 0.0, 0.25, None),
        'roof': ('#8f8b86', 0.0, 0.15, None), 'rail': ('#2d3a44', 0.0, 0.8, None),
        'base': ('#bdb8ae', 0.0, 0.3, None), 'metal': ('#33363c', 0.0, 0.4, None),
        'neon': ('#ff4fa8', 0.0, 0.6, None), 'awning': ('#e0e0e0', 0.0, 0.2, None),
    },
    'spire': {
        'wall': ('#dac9a6', 0.0, 0.2, None), 'glass': ('#ffffff', 0.0, 0.9, None),
        'frame': ('#f1e6cc', 0.0, 0.3, None), 'slab': ('#c9b48e', 0.0, 0.25, None),
        'roof': ('#7d776f', 0.0, 0.15, None), 'rail': ('#2d3a44', 0.0, 0.8, None),
        'base': ('#b9a07a', 0.0, 0.3, None), 'metal': ('#3b3f46', 0.0, 0.4, None),
        'neon': ('#ffcf5a', 0.0, 0.6, None), 'awning': ('#c44d4d', 0.0, 0.2, None),
    },
    'mixed': {
        'wall': ('#f2c06b', 0.0, 0.2, None), 'glass': ('#ffffff', 0.0, 0.9, None),
        'frame': ('#fbf6ec', 0.0, 0.3, None), 'slab': ('#efe5d2', 0.0, 0.25, None),
        'roof': ('#958c82', 0.0, 0.15, None), 'rail': ('#30444c', 0.0, 0.8, None),
        'base': ('#d98b6a', 0.0, 0.3, None), 'metal': ('#3e4248', 0.0, 0.4, None),
        'neon': ('#ff5fb4', 0.0, 0.6, None), 'awning': ('#2fa58f', 0.0, 0.2, None),
    },
}[KIND]

# 配色变体：python3 Tools/compose_atlas.py <楼型> <输出目录> <变体号>，变体 ≥1 只覆盖下面几个区域的颜色，
# 只输出 albedo_v<n>.png（窗灯 / 遮罩和变体 0 共用）。Unity 按楼的种子挑变体，同一种楼不再一个颜色。
VARIANTS = {
    'glass_tower': [{'wall': '#5fb6b0', 'frame': '#2f7a7a', 'base': '#6aa9a3'}, {'wall': '#4a5a78', 'frame': '#c9a66b', 'base': '#5d6880'}],
    'deco_hotel': [{'wall': '#a8e0cf', 'base': '#c9ecdf', 'awning': '#f08a6a'}, {'wall': '#f6e3a1', 'base': '#f4d6a8', 'awning': '#3f8fd6'}],
    'office': [{'wall': '#c9d3dc', 'base': '#9fb0bf', 'slab': '#b8c4cf'}, {'wall': '#e8c2a4', 'base': '#c99474', 'slab': '#d9b090'}],
    'apartment': [{'wall': '#f4a9a0', 'base': '#f7d6a6'}, {'wall': '#b9b2e6', 'base': '#f2c3d4'}],
    'garage': [{'frame': '#3a8fd6'}, {'frame': '#d6c13a'}],
    'shop': [{'wall': '#9fd8e8', 'base': '#7fb0c8'}, {'wall': '#f2a7c3', 'base': '#d98aa8'}],
    'warehouse': [{'wall': '#5f7f9a'}, {'wall': '#8a9a62'}],
    'condo': [{'wall': '#e9d8c4', 'rail': '#7fd0c0'}, {'wall': '#cfe3ee', 'rail': '#a0b8e8', 'slab': '#eef4f8'}],
    'brick': [{'wall': '#7b4a3a', 'frame': '#e6dcc8'}, {'wall': '#c58a5a', 'frame': '#f4efe4', 'awning': '#2f6b4f'}],
    'modern': [{'wall': '#1f2a3a', 'slab': '#4a4c52', 'frame': '#8a8d94'}, {'wall': '#3a3240', 'slab': '#ece6da'}],
    'spire': [{'wall': '#9fb3c4', 'frame': '#e4ecf2', 'slab': '#7f95a8'}, {'wall': '#c9a3a0', 'frame': '#f4e4dc', 'slab': '#a87e7a'}],
    'mixed': [{'wall': '#8ec9b8', 'base': '#e48a72'}, {'wall': '#e48a72', 'base': '#f2c06b', 'awning': '#3f6fd6'}],
}
VAR = int(sys.argv[3]) if len(sys.argv) > 3 else 0
if VAR:
    for k_, c_ in VARIANTS[KIND][VAR - 1].items():
        LOOK[k_] = (c_,) + tuple(LOOK[k_][1:])



# 玻璃 8 种窗格：(明暗, 百叶拉下比例 0~1, 色调)。夜里亮灯的格子见 GLASS_LIT
GLASS_CELLS = [
    (0.70, 0.0, (1.00, 1.00, 1.00)), (0.60, 0.35, (1.00, 1.00, 1.00)),
    (0.78, 0.0, (1.05, 1.00, 0.95)), (0.55, 0.6, (0.95, 1.00, 1.05)),
    (0.66, 0.15, (1.00, 1.00, 1.00)), (0.74, 0.0, (0.95, 1.02, 1.05)),
    (0.58, 0.45, (1.00, 0.98, 0.95)), (0.70, 0.0, (1.00, 1.00, 1.00)),
]
# 8 格全都画成亮灯的样子（亮度、色温各不同：暖黄、偏白、偏橙、电视蓝光），亮不亮由 Unity 着色器按每扇窗的随机号决定
GLASS_LIT = {0: 0.95, 1: 0.7, 2: 1.0, 3: 0.6, 4: 0.85, 5: 0.75, 6: 0.9, 7: 0.65}
GLASS_TINT = {0: '#ffcf86', 1: '#ffe2b0', 2: '#ffc070', 3: '#a9c8ff', 4: '#ffd89a', 5: '#fff0d0', 6: '#ffb860', 7: '#ffd0a0'}


def hexrgb(h):
    h = h.lstrip('#'); return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32) / 255


def noise(w, h, scale, octaves=4):
    """多倍频值噪声（0~1）"""
    acc = np.zeros((h, w), np.float32); amp = 1.0; tot = 0
    for o in range(octaves):
        s = max(2, int(scale * 2 ** o))
        small = rng.random((s, s)).astype(np.float32)
        big = np.asarray(Image.fromarray((small * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)).astype(np.float32) / 255
        acc += big * amp; tot += amp; amp *= 0.5
    return acc / tot


def photo(name, w, h):
    """把照片材质（已做成可平铺）平铺到 w×h；没有返回 None"""
    p = TEX / name / 'albedo.png'
    if not p.exists():
        return None
    t = np.asarray(Image.open(p).convert('RGB')).astype(np.float32) / 255
    reps_y, reps_x = int(np.ceil(h / t.shape[0])), int(np.ceil(w / t.shape[1]))
    return np.tile(t, (reps_y, reps_x, 1))[:h, :w]


def streaks(w, h, k=0.25):
    """雨水流痕：从上往下的竖向暗纹，越往下越淡"""
    cols = noise(w, 1, w / 24, 2)[0]
    mask = (cols > 0.62).astype(np.float32) * (cols - 0.62) / 0.38
    fall = np.linspace(1.0, 0.25, h, dtype=np.float32)[:, None]
    m = mask[None, :] * fall * noise(w, h, 6, 3)
    return 1 - np.round(np.clip(m * k * 3, 0, k) / (k / 2)) * (k / 2)   # 两阶，像画上去的流痕


def blotch(w, h):
    """手绘斑驳：低频噪声量化成 3 阶色块（0.9 / 0.97 / 1.0），边缘干净，三渲二里不会糊"""
    n = noise(w, h, 7, 3)
    return np.select([n < 0.38, n < 0.5], [0.9, 0.96], 1.0).astype(np.float32)


def cracks(w, h, count=14):
    """细裂缝：随机折线，画成 2px 深色线"""
    img = Image.new('L', (w, h), 255)
    from PIL import ImageDraw
    d = ImageDraw.Draw(img)
    for _ in range(count):
        x, y = rng.random() * w, rng.random() * h
        pts = [(x, y)]
        ang = rng.random() * 6.28
        for _ in range(5):
            ang += (rng.random() - 0.5) * 1.2
            ln = (0.02 + rng.random() * 0.04) * w
            x, y = x + np.cos(ang) * ln, y + np.sin(ang) * ln
            pts.append((x, y))
        d.line(pts, fill=150, width=2)
    return np.asarray(img).astype(np.float32) / 255


GLASS_DARK, GLASS_MID, GLASS_LIGHT = hexrgb('#34495c'), hexrgb('#5b7d93'), hexrgb('#9cc0cf')


def painted_glass(w, h):
    """手绘玻璃 4×2 格：上浅下深 3 阶色带 + 两道斜向白色高光笔触 + 部分格子拉了百叶/窗帘。
    格子顺序和 GLASS_CELLS / blender_buildings.py 一致"""
    cw, ch = w // 4, h // 2
    out = np.zeros((h, w, 3), np.float32)
    yy, xx = np.mgrid[0:ch, 0:cw].astype(np.float32)
    for k, (dark, blind, tint) in enumerate(GLASS_CELLS):
        t = yy / ch
        cell = np.where((t < 0.33)[..., None], GLASS_LIGHT, np.where((t < 0.7)[..., None], GLASS_MID, GLASS_DARK))
        cell = cell * (0.85 + 0.25 * dark) * np.array(tint, np.float32)
        # 斜向高光：一宽一窄两道平行笔触
        off = (k * 0.17) % 0.6
        diag = (xx / cw + yy / ch * 0.7) - off
        stroke = ((diag > 0.35) & (diag < 0.47)) | ((diag > 0.55) & (diag < 0.59))
        cell = np.where(stroke[..., None], cell * 0.4 + 0.6 * hexrgb('#e8f4f6'), cell)
        if blind:   # 百叶：上部米色横条（两阶）
            cut = int(ch * blind)
            stripe = ((yy[:cut] // max(3, ch // 28)) % 2)[..., None]
            cell[:cut] = np.where(stripe > 0, hexrgb('#d9cdb5'), hexrgb('#b8ab92'))
        if k == 5:  # 一格窗帘：竖向暖色布褶
            fold = ((xx // max(4, cw // 12)) % 2)[..., None]
            cell = np.where((xx < cw * 0.4)[..., None], np.where(fold > 0, hexrgb('#c98f74'), hexrgb('#b07a62')), cell)
        cu, cv = k % 4, k // 4
        r0 = h - (cv + 1) * ch
        out[r0:r0 + ch, cu * cw:(cu + 1) * cw] = cell
    return np.clip(out, 0, 1)


# 楼型专属纹样：压型钢板竖肋 / 卷帘门横肋 / 陶瓦 / 停车位线
PATTERN = {
    'warehouse': {'wall': 'vrib', 'metal': 'hrib', 'roof': 'vrib'},
    'villa': {'roof': 'tiles'},
    'garage': {'roof': 'parking'},
    'brick': {'wall': 'brick', 'base': 'brick'},
    
}.get(KIND, {})


def apply_pattern(name, img):
    kind = PATTERN.get(name)
    if not kind:
        return img
    h, w = img.shape[:2]
    if kind == 'vrib':
        col = (np.arange(w) % max(6, w // 32)) / max(6, w // 32)
        k = np.select([col < 0.18, col < 0.5], [0.78, 1.06], 0.94).astype(np.float32)
        return img * k[None, :, None]
    if kind == 'hrib':
        row = (np.arange(h) % max(6, h // 24)) / max(6, h // 24)
        k = np.select([row < 0.2, row < 0.55], [0.72, 1.08], 0.95).astype(np.float32)
        return img * k[:, None, None]
    if kind == 'tiles':
        rh = max(8, h // 14)
        yy, xx = np.mgrid[0:h, 0:w]
        r = yy // rh
        cw = max(10, w // 16)
        xs = (xx + (r % 2) * cw // 2) % cw
        t = (yy % rh) / rh
        k = np.where(t > 0.82, 0.62, 1.0 - 0.18 * t) * np.where(xs < 2, 0.8, 1.0)
        return img * k[..., None].astype(np.float32)
    if kind == 'brick':
        # 砖墙：一开间约 22 皮砖，错缝；砖缝浅色，每块砖明暗略有差（两阶，像画的）
        rh = max(6, h // 22); bw_ = rh * 3
        yy, xx = np.mgrid[0:h, 0:w]
        r = yy // rh
        xs = (xx + (r % 2) * bw_ // 2) % bw_
        mortar = ((yy % rh) < max(1, rh // 6)) | (xs < max(1, rh // 6))
        cell = (r * 7 + (xx + (r % 2) * bw_ // 2) // bw_ * 13) % 5
        k = np.where(cell == 0, 0.86, np.where(cell == 3, 1.06, 1.0))
        out = img * k[..., None].astype(np.float32)
        out[mortar] = np.minimum(out[mortar] * 0.5 + 0.42, 1)
        return out
    if kind == 'parking':
        out = img.copy()
        lw = max(3, w // 160)
        for x in np.linspace(0, w, 9)[:-1].astype(int):
            out[:, x:x + lw] = hexrgb('#e9e4d6')
        out[h // 2 - lw:h // 2 + lw] = hexrgb('#e9e4d6')
        return out
    return img


def region_pixels(name, w, h):
    col, metal, smooth, ph = LOOK[name]
    base = hexrgb(col)
    img = np.ones((h, w, 3), np.float32) * base
    if name == 'glass' and ph and (TEX / ph / 'albedo.png').exists():
        # 玻璃照片：压低对比（天空 / 室内的上下分界缩小后会变成条纹），整体压暗——
        # 真实玻璃本身很暗，亮度主要来自 Unity 里的天空反射和高光
        # 分成 4×2 共 8 格，每格一整扇窗（和 blender_buildings.py 里每扇窗挑一格对应）
        cw, ch = w // 4, h // 2
        src = Image.open(TEX / ph / 'albedo.png').convert('RGB')
        out = np.zeros((h, w, 3), np.float32)
        for k, (dark, blind, tint) in enumerate(GLASS_CELLS):
            g = np.asarray(src.resize((cw, ch), Image.LANCZOS)).astype(np.float32) / 255
            if k % 2: g = g[:, ::-1]                                  # 左右翻转，打散重复
            mean = g.reshape(-1, 3).mean(0)
            g = (mean + (g - mean) * 0.45) * dark * np.array(tint, np.float32)
            if blind:                                                 # 拉下的百叶：上半截横条纹
                rows = np.arange(ch)[:, None]
                stripe = ((rows // max(2, ch // 40)) % 2).astype(np.float32)
                cut = int(ch * blind)
                g[:cut] = g[:cut] * 0.55 + (0.42 + 0.08 * stripe[:cut, :, None])
            cu, cv = k % 4, k // 4
            # 图片行从上往下，UV 的 v 从下往上：第 cv 行在图里是倒数第 cv+1 行
            r0 = h - (cv + 1) * ch
            out[r0:r0 + ch, cu * cw:(cu + 1) * cw] = g
        return np.clip(out, 0, 1), metal, smooth
    if name == 'glass':
        return painted_glass(w, h), metal, smooth
    used_photo = False
    if ph:
        t = photo(ph, w, h)
        if t is not None:
            lum = t @ np.array([0.299, 0.587, 0.114], np.float32)
            # 照片的明暗细节 × 本楼底色；对比度拉高，坑点、划痕、污渍才看得出来
            img = base * np.clip(0.3 + 0.75 * lum[..., None] / max(lum.mean(), 1e-3), 0, 1.25)
            used_photo = True
    if used_photo:
        img *= (0.96 + 0.08 * noise(w, h, 3, 2))[..., None]        # 只留很淡的大块色差，不再糊成云
    else:
        img *= blotch(w, h)[..., None]                              # 手绘色块斑驳（色阶化，不是噪点）
    if name in ('wall', 'base', 'slab'):
        img *= streaks(w, h)[..., None]
        # 底部发黑（溅水、灰尘）
        img *= np.linspace(1.0, 0.82, h, dtype=np.float32)[::-1][:, None, None] ** 0.6
    if name == 'glass':
        # 玻璃：上亮下暗的天空反光 + 斜向高光 + 室内暗影块
        y = np.linspace(1, 0, h, dtype=np.float32)[:, None]
        sky = np.clip(0.55 + 0.6 * y, 0, 1.2)
        xx, yy = np.meshgrid(np.arange(w), np.arange(h))
        band = np.exp(-(((xx + yy * 0.6) % (w * 0.9)) - w * 0.3) ** 2 / (2 * (w * 0.05) ** 2)).astype(np.float32)
        img = img * sky[..., None] + band[..., None] * 0.18
        inner = noise(w, h, 3, 2)
        img *= (0.8 + 0.25 * inner)[..., None]
    if name == 'rail':
        # 阳台栏板：上浅下深两阶玻璃色 + 竖向栏杆 + 顶部扶手亮线
        yy = np.arange(h, dtype=np.float32)[:, None] / h
        img = np.where((yy < 0.5)[..., None], base * 1.6, base * 1.15) * np.ones((h, w, 3), np.float32)
        bars = ((np.arange(w) % max(6, w // 24)) < max(2, w // 160)).astype(np.float32)
        img = img * (1 - 0.45 * bars)[None, :, None]
        img[: max(3, h // 14)] = hexrgb('#c9d3d4')
    if name == 'awning':
        stripes = ((np.arange(w) // max(1, w // 10)) % 2).astype(np.float32)
        img = img * (0.75 + 0.25 * stripes)[None, :, None] + (1 - stripes)[None, :, None] * 0.25
    if name == 'roof':
        # 楼顶：沥青补丁（大块深色）+ 几道裂缝线
        patch = noise(w, h, 5, 2)
        img *= np.where(patch > 0.62, 0.78, 1.0)[..., None]
        img *= cracks(w, h)[..., None]
    img = apply_pattern(name, img)
    return np.clip(img, 0, 1), metal, smooth


albedo = np.zeros((N, N, 3), np.float32)
mask = np.zeros((N, N, 4), np.float32); mask[..., 1] = 1
emis = np.zeros((N, N, 3), np.float32)
nrm = np.ones((N, N, 3), np.float32) * np.array([0.5, 0.5, 1.0], np.float32)   # 法线图集：平面 = (0.5, 0.5, 1)
for name, (u0, v0, u1, v1) in ATLAS.items():
    x0, x1 = int(u0 * N), int(u1 * N)
    # UV 的 v 从下往上，图片行从上往下
    y0, y1 = int((1 - v1) * N), int((1 - v0) * N)
    px, metal, smooth = region_pixels(name, x1 - x0, y1 - y0)
    albedo[y0:y1, x0:x1] = px
    mask[y0:y1, x0:x1, 0] = metal
    mask[y0:y1, x0:x1, 3] = smooth * (0.85 + 0.3 * noise(x1 - x0, y1 - y0, 10, 2))
    # 有照片材质的区域把它的法线平铺进来（玻璃除外：玻璃要平整才反光干净）
    ph = LOOK[name][3]
    if ph and name != 'glass' and (TEX / ph / 'normal.png').exists():
        t = np.asarray(Image.open(TEX / ph / 'normal.png').convert('RGB')).astype(np.float32) / 255
        ry, rx = int(np.ceil((y1 - y0) / t.shape[0])), int(np.ceil((x1 - x0) / t.shape[1]))
        nrm[y0:y1, x0:x1] = np.tile(t, (ry, rx, 1))[:y1 - y0, :x1 - x0]
    if name == 'neon':
        emis[y0:y1, x0:x1] = hexrgb(LOOK['neon'][0])
    if name == 'glass':
        # 夜里亮灯：按格子整扇窗亮（暖光，中间亮、四周稍暗，像室内灯照到玻璃上）
        w, h = x1 - x0, y1 - y0
        cw, ch = w // 4, h // 2
        yy, xx = np.mgrid[0:ch, 0:cw].astype(np.float32)
        glow_ = 1 - 0.35 * (((xx / cw - 0.5) * 2) ** 2 + ((yy / ch - 0.45) * 2) ** 2) / 2
        for k, lv in GLASS_LIT.items():
            cu, cv = k % 4, k // 4
            r0 = y0 + h - (cv + 1) * ch
            emis[r0:r0 + ch, x0 + cu * cw:x0 + (cu + 1) * cw] = glow_[..., None] * hexrgb(GLASS_TINT[k]) * lv

if VAR:
    Image.fromarray((albedo * 255 + 0.5).astype(np.uint8)).save(OUT / f'albedo_v{VAR}.png')
    print('配色变体', KIND, VAR, flush=True)
    sys.exit(0)
Image.fromarray((albedo * 255 + 0.5).astype(np.uint8)).save(OUT / 'albedo.png')
Image.fromarray((np.clip(mask, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT / 'mask.png')
Image.fromarray((emis * 255 + 0.5).astype(np.uint8)).save(OUT / 'emission.png')
Image.fromarray((nrm * 255 + 0.5).astype(np.uint8)).save(OUT / 'normal.png')
print('图集', KIND, '→', OUT, flush=True)
