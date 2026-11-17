"""《霓虹巨兽》音效 + 音乐：全部程序合成（numpy），原创、无任何采样和版权素材。
    python3 Tools/audio_synth.py <输出目录>
音乐：自写的合成器流行（synthwave）和弦进行 + 贝斯 + 琶音 + 鼓机，无缝循环。"""
import sys, wave, pathlib, math
import numpy as np

SR = 44100
OUT = pathlib.Path(sys.argv[1]); OUT.mkdir(parents=True, exist_ok=True)
rng = np.random.default_rng(7)

def t_(d): return np.arange(int(SR * d)) / SR
def env(n, a=0.005, r=0.2, d=None):
    t = np.arange(n) / SR; e = np.minimum(1, t / max(a, 1e-4))
    return e * np.exp(-t / max(r, 1e-4)) if d is None else e * np.clip(1 - (t - d) / max(r, 1e-4), 0, 1)
def lp(x, k):   # 一阶低通，k 越小越闷
    y = np.empty_like(x); s = 0.0
    for i in range(len(x)): s += k * (x[i] - s); y[i] = s
    return y
def lpf(x, cut):   # 频域低通（快）
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / SR); X *= 1 / (1 + (f / cut) ** 4); return np.fft.irfft(X, len(x))
def hpf(x, cut):
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / SR); X *= 1 / (1 + (cut / np.maximum(f, 1)) ** 4); return np.fft.irfft(X, len(x))
def noise(n): return rng.standard_normal(n)
def save(name, x, stereo=None, gain=0.9):
    x = np.asarray(x, float)
    if stereo is not None: x = np.stack([x, stereo], 1)
    peak = np.max(np.abs(x)) or 1; x = x / peak * gain
    pcm = (x * 32767).astype(np.int16)
    with wave.open(str(OUT / f'{name}.wav'), 'wb') as w:
        w.setnchannels(2 if x.ndim == 2 else 1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
    print(name, round(len(pcm) / SR, 2), 's')
def loopfade(x, f=0.05):   # 首尾交叉淡化，保证无缝
    n = int(SR * f); a = x[:n].copy(); b = x[-n:].copy(); k = np.linspace(0, 1, n)
    y = x[:-n].copy(); y[:n] = a * k + b * (1 - k); return y

# ───── 音效 ─────
def smash():          # 拍楼：低沉一击 + 碎裂噪声 + 几颗碎块
    n = int(SR * 0.9); t = t_(0.9)
    thud = np.sin(2 * np.pi * (70 * t + 40 * (1 - np.exp(-t * 20)) / 20)) * env(n, 0.002, 0.18)
    crunch = lpf(noise(n), 2600) * env(n, 0.001, 0.12)
    debris = np.zeros(n)
    for k in range(14):
        i = int(rng.uniform(0.04, 0.6) * SR); m = int(SR * 0.03)
        debris[i:i + m] += lpf(noise(m), rng.uniform(1500, 5000)) * env(m, 0.0005, 0.008) * rng.uniform(0.2, 0.6)
    return thud * 1.2 + crunch * 0.7 + debris * 0.5
def collapse():       # 整段倒塌：长的低频隆隆 + 碎块雨
    d = 2.2; n = int(SR * d); t = t_(d)
    rum = lpf(noise(n), 180) * env(n, 0.15, 0.9, d=1.2) * 3
    groan = np.sin(2 * np.pi * (55 - 15 * t) * t) * env(n, 0.3, 0.6, d=0.8) * 0.4
    debris = np.zeros(n)
    for k in range(60):
        i = int(rng.uniform(0.2, 1.9) * SR); m = int(SR * 0.04)
        if i + m < n: debris[i:i + m] += lpf(noise(m), rng.uniform(1000, 6000)) * env(m, 0.0005, 0.01) * rng.uniform(0.1, 0.5)
    return rum + groan + debris * 0.6
def blast():          # 爆炸
    d = 1.6; n = int(SR * d); t = t_(d)
    boom = np.sin(2 * np.pi * (48 * t + 60 * (1 - np.exp(-t * 12)) / 12)) * env(n, 0.002, 0.35)
    roar = lpf(noise(n), 900) * env(n, 0.003, 0.5) * 1.4
    crack = hpf(noise(n), 3000) * env(n, 0.0005, 0.03) * 0.6
    return boom * 1.4 + roar + crack
def pinch():          # 捏住（抓起）：短促的“啪嗒”
    n = int(SR * 0.12); t = t_(0.12)
    return np.sin(2 * np.pi * 1300 * t * (1 - t * 3)) * env(n, 0.0005, 0.02) + lpf(noise(n), 4000) * env(n, 0.0003, 0.006) * 0.5
def throw():          # 甩出：呼啸
    d = 0.5; n = int(SR * d); t = t_(d)
    x = noise(n); y = np.zeros(n)
    for i, cut in enumerate(np.linspace(600, 3000, 10)):
        a, b = i * n // 10, (i + 1) * n // 10; y[a:b] = lpf(x, cut)[a:b]
    return y * np.sin(np.pi * t / d) ** 2
def ui_hover():       # 喷漆“嘶”一下（选中项刷出来）
    d = 0.22; n = int(SR * d)
    return hpf(noise(n), 2500) * env(n, 0.02, 0.06, d=0.12) * 0.6
def ui_press():       # 盖章：闷闷的“咚” + 纸响
    d = 0.3; n = int(SR * d); t = t_(d)
    return np.sin(2 * np.pi * 120 * t) * env(n, 0.001, 0.05) * 1.2 + lpf(noise(n), 3000) * env(n, 0.001, 0.03) * 0.7
def page():           # 纸片弹出：纸张抖动
    d = 0.35; n = int(SR * d); t = t_(d)
    flutter = (np.sin(2 * np.pi * 28 * t) > 0.3).astype(float)
    return hpf(lpf(noise(n), 5000), 700) * flutter * env(n, 0.005, 0.12) * 0.7
def shield():         # 拍到护盾：电流嗡 + 弹开
    d = 0.6; n = int(SR * d); t = t_(d)
    buzz = np.sign(np.sin(2 * np.pi * 110 * t)) * 0.3 + np.sin(2 * np.pi * 880 * t * (1 + 0.3 * t))
    return lpf(buzz, 4000) * env(n, 0.002, 0.15) + hpf(noise(n), 4000) * env(n, 0.001, 0.04) * 0.5
def hurt():           # 受伤：低沉的冲击 + 失真
    d = 0.5; n = int(SR * d); t = t_(d)
    x = np.tanh(3 * np.sin(2 * np.pi * 90 * t * (1 - 0.4 * t))) * env(n, 0.002, 0.15)
    return x + lpf(noise(n), 500) * env(n, 0.002, 0.1)
def breath():         # 原子吐息：咆哮上扬 + 能量嗡鸣（3 秒）
    d = 3.0; n = int(SR * d); t = t_(d)
    roar = lpf(noise(n), 1400) * env(n, 0.2, 0.8, d=2.2) * 1.6
    tone = sum(np.sin(2 * np.pi * f * (1 + 0.15 * t) * t + k) for k, f in enumerate([110, 165, 220, 330])) * 0.18 * env(n, 0.3, 0.8, d=2.2)
    sizzle = hpf(noise(n), 5000) * env(n, 0.3, 0.8, d=2.2) * 0.3
    return roar + tone + sizzle
def tower_down():     # 封印塔倒下：玻璃碎 + 下沉的电子音
    d = 2.4; n = int(SR * d); t = t_(d)
    glass = np.zeros(n)
    for k in range(40):
        i = int(rng.uniform(0, 0.8) * SR); m = int(SR * 0.08)
        f = rng.uniform(2500, 7000); tt = t_(0.08)
        glass[i:i + m] += np.sin(2 * np.pi * f * tt) * env(m, 0.0005, 0.02) * rng.uniform(0.1, 0.4)
    drop = np.sin(2 * np.pi * (600 * np.exp(-t * 1.2)) * t) * env(n, 0.01, 0.9) * 0.6
    c = collapse(); c = np.pad(c, (0, max(0, n - len(c))))[:n]
    return glass + drop + c * 0.6
def star_up():        # 通缉星上升：两声警报 up-chirp
    d = 0.7; n = int(SR * d); t = t_(d)
    f = np.where(t < 0.35, 700 + 900 * (t / 0.35), 700 + 900 * ((t - 0.35) / 0.35))
    return np.sign(np.sin(2 * np.pi * np.cumsum(f) / SR)) * 0.25 * env(n, 0.005, 0.5, d=0.6)
def siren_loop():     # 警笛循环（2 秒）
    d = 2.0; n = int(SR * d); t = t_(d)
    f = 650 + 350 * (0.5 + 0.5 * np.sin(2 * np.pi * t / d))
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) + 0.3 * np.sin(4 * np.pi * np.cumsum(f) / SR)
    return lpf(x, 3000) * 0.5
def rotor_loop():     # 直升机旋翼循环（1 秒，每秒 14 下）
    d = 1.0; n = int(SR * d); t = t_(d)
    chop = (0.5 + 0.5 * np.cos(2 * np.pi * 14 * t)) ** 6
    return lpf(noise(n), 700) * chop * 1.5 + np.sin(2 * np.pi * 42 * t) * 0.2
def city_loop():      # 城市底噪（12 秒）：车流低频 + 偶尔喇叭 + 远处的风
    d = 12.0; n = int(SR * d); t = t_(d)
    hum = lpf(noise(n), 220) * (0.7 + 0.3 * np.sin(2 * np.pi * t / 6))
    wind = lpf(hpf(noise(n), 300), 1200) * 0.15 * (0.5 + 0.5 * np.sin(2 * np.pi * t / 12 + 1))
    horns = np.zeros(n)
    for k in range(3):
        i = int(rng.uniform(1, 10) * SR); m = int(SR * rng.uniform(0.15, 0.4)); f = rng.choice([392, 440, 466])
        horns[i:i + m] += lpf(np.sign(np.sin(2 * np.pi * f * t_(m / SR))), 1500) * env(m, 0.01, 0.1, d=m / SR * 0.7) * 0.08
    return loopfade(hum + wind + horns, 0.3)

# ───── 音乐：原创合成器流行 ─────
def note(m): return 440 * 2 ** ((m - 69) / 12)
def saw(f, t): return 2 * (t * f - np.floor(0.5 + t * f))
def music(name, bpm, chords, bars_per_chord, energy):
    beat = 60 / bpm; bar = beat * 4
    total = bar * bars_per_chord * len(chords)
    n = int(SR * total); t = t_(total)
    L = np.zeros(n); R = np.zeros(n)
    def add(sig, i0, pan=0.0):
        i1 = min(n, i0 + len(sig)); s = sig[:i1 - i0]
        L[i0:i1] += s * (1 - max(0, pan)); R[i0:i1] += s * (1 + min(0, pan))
    for ci, ch in enumerate(chords):
        c0 = ci * bars_per_chord * bar
        # 铺底：慢起的锯齿和弦（略微失谐的两层，左右分开）
        d = bars_per_chord * bar; tt = t_(d)
        pad = sum(saw(note(m), tt) + saw(note(m) * 1.006, tt) for m in ch) / (2 * len(ch))
        pad = lpf(pad, 1600 + 600 * energy) * env(len(tt), 0.4, 0.4, d=d - 0.4) * 0.35
        add(pad, int(c0 * SR), -0.3); add(lpf(pad, 1200), int(c0 * SR), 0.3)
        # 贝斯：八分音符根音
        for b8 in range(int(bars_per_chord * 8)):
            tt = t_(beat / 2); f = note(ch[0] - 12)
            bass = lpf(saw(f, tt), 400 + 300 * energy) * env(len(tt), 0.003, 0.12) * 0.55
            add(bass, int((c0 + b8 * beat / 2) * SR))
        # 琶音：十六分音符，在和弦音上上行
        arp = [ch[0] + 12, ch[1] + 12, ch[2] + 12, ch[1] + 24 if len(ch) < 4 else ch[3] + 12]
        for s16 in range(int(bars_per_chord * 16)):
            if energy < 0.5 and s16 % 2: continue
            tt = t_(beat / 4); m = arp[s16 % 4]
            sq = np.sign(np.sin(2 * np.pi * note(m) * tt)) * 0.5 + np.sin(2 * np.pi * note(m) * 2 * tt) * 0.3
            add(lpf(sq, 3500) * env(len(tt), 0.002, 0.05) * 0.16, int((c0 + s16 * beat / 4) * SR), 0.5 if s16 % 2 else -0.5)
    # 鼓机：底鼓 1、3 拍（高能量四拍），军鼓 2、4 拍带混响尾，踩镲八分
    nbeats = int(total / beat)
    for b in range(nbeats):
        i = int(b * beat * SR)
        if energy > 0.5 or b % 2 == 0:
            tt = t_(0.35); add(np.sin(2 * np.pi * (50 + 90 * np.exp(-tt * 30)) * tt) * env(len(tt), 0.001, 0.12) * 0.9, i)
        if b % 2 == 1:
            tt = t_(0.5); sn = lpf(noise(len(tt)), 6000) * env(len(tt), 0.001, 0.09) + np.sin(2 * np.pi * 190 * tt) * env(len(tt), 0.001, 0.05) * 0.5
            add(sn * 0.45, i, -0.1); add(lpf(noise(len(tt)), 3000) * env(len(tt), 0.01, 0.3) * 0.1, i, 0.2)   # 混响尾
        for h in range(2):
            tt = t_(0.06); add(hpf(noise(len(tt)), 7000) * env(len(tt), 0.0005, 0.015) * (0.18 if h else 0.1) * (0.6 + 0.4 * energy), int((b * beat + h * beat / 2) * SR), 0.4)
    save(name, L, R, 0.85)

save('sfx_smash', smash()); save('sfx_collapse', collapse()); save('sfx_blast', blast())
save('sfx_pinch', pinch(), gain=0.6); save('sfx_throw', throw(), gain=0.7)
save('ui_hover', ui_hover(), gain=0.35); save('ui_press', ui_press(), gain=0.6); save('ui_page', page(), gain=0.5)
save('sfx_shield', shield(), gain=0.7); save('sfx_hurt', hurt(), gain=0.8); save('sfx_breath', breath())
save('sfx_tower_down', tower_down()); save('sfx_star_up', star_up(), gain=0.5)
save('loop_siren', siren_loop(), gain=0.5); save('loop_rotor', rotor_loop(), gain=0.6); save('amb_city', city_loop(), gain=0.5)
# 标题：慢、梦幻（Am - F - C - G，各 2 小节，96 BPM）；关卡：快、紧张（Dm - Bb - F - C，各 1 小节，118 BPM，重复两遍更长）
A, F, C, G, D, Bb = [57, 60, 64], [53, 57, 60, 64], [48, 52, 55, 59], [55, 59, 62], [50, 53, 57], [46, 50, 53, 57]
music('music_title', 96, [A, F, C, G], 2, 0.3)
music('music_game', 118, [D, Bb, F, C, D, Bb, C, A], 1, 1.0)

# ───── 标题招牌（11-08 加）─────
def land():           # 铁皮广告牌砸到地上：低频闷击 + 钢架的金属余振 + 一点碎屑
    d = 1.2; n = int(SR * d); t = t_(d)
    thump = np.sin(2 * np.pi * (52 + 40 * np.exp(-t * 18)) * t) * env(n, 0.002, 0.22)
    ring = sum(np.sin(2 * np.pi * f * t + p) * a for f, p, a in [(317, 0.3, 0.5), (523, 1.1, 0.35), (811, 2.0, 0.25), (1297, 0.7, 0.15)]) * env(n, 0.001, 0.35) * 0.35
    grit = lpf(noise(n), 2500) * env(n, 0.001, 0.06) * 0.6
    return thump + ring + grit
def neon_on():        # 霓虹灯管通电：变压器“咔哒” + 120Hz 嗡鸣 + 接触不良的噼啪，最后稳住
    d = 1.6; n = int(SR * d); t = t_(d)
    click = hpf(noise(n), 1500) * env(n, 0.0005, 0.012) * 0.9
    gate = np.ones(n)
    for s in [0.08, 0.2, 0.27, 0.45, 0.6]:   # 闪的几下
        i0, i1 = int(s * SR), int((s + 0.05) * SR); gate[i0:i1] = 0.15
    hum = (np.sign(np.sin(2 * np.pi * 120 * t)) * 0.25 + np.sin(2 * np.pi * 240 * t) * 0.4 + np.sin(2 * np.pi * 360 * t) * 0.15)
    hum = lpf(hum, 1800) * gate * np.minimum(1, t / 0.03) * np.clip((d - t) / 0.4, 0, 1) * 0.6
    crackle = hpf(noise(n), 3000) * (rng.random(n) > 0.997) * 2.5 * np.clip(1 - t / 0.8, 0, 1)
    return click + hum + crackle
save('sfx_land', land(), gain=0.8); save('sfx_neon', neon_on(), gain=0.55)
