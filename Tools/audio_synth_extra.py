"""《霓虹巨兽》补充音效（11-12）：全部程序合成（numpy），原创、无任何采样和版权素材。
    python3 Tools/audio_synth_extra.py <输出目录>
sfx_incoming 来袭呼啸（3D 播放，听得出方向）/ sfx_shot 开火 / sfx_heal 回血 / sfx_clang 打在护甲上 / sfx_down 巨兽倒下 / sfx_build 施工 / sfx_win 封印解除（胜利）"""
import sys, wave, pathlib
import numpy as np
SR = 44100
OUT = pathlib.Path(sys.argv[1]); OUT.mkdir(parents=True, exist_ok=True)
rng = np.random.default_rng(11)
def t_(d): return np.arange(int(SR * d)) / SR
def env(n, a=0.005, r=0.2):
    e = np.ones(n); na = max(1, int(SR * a)); nr = max(1, int(SR * r))
    e[:na] = np.linspace(0, 1, na); e[-nr:] *= np.linspace(1, 0, nr); return e
def lpf(x, cut):
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / SR); X[f > cut] = 0; return np.fft.irfft(X, len(x))
def hpf(x, cut):
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / SR); X[f < cut] = 0; return np.fft.irfft(X, len(x))
def noise(n): return rng.standard_normal(n)
def save(name, x, gain=0.8):
    x = np.asarray(x, float); x = x / (np.max(np.abs(x)) or 1) * gain
    with wave.open(str(OUT / f'{name}.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes((x * 32767).astype(np.int16).tobytes())
    print(name, round(len(x) / SR, 2), 's')
def incoming():       # 来袭：音调上滑的呼啸（越来越近）+ 一层风噪
    d = 0.5; n = int(SR * d); t = t_(d)
    f = 500 + 1500 * (t / d) ** 2
    tone = np.sin(2 * np.pi * np.cumsum(f) / SR) * 0.5
    wind = lpf(hpf(noise(n), 900), 5000) * 0.5
    return (tone + wind) * (t / d) ** 1.5 * env(n, 0.01, 0.04)
def shot():           # 开火：短促的“砰” + 一点金属尾音
    d = 0.22; n = int(SR * d); t = t_(d)
    pop = np.sin(2 * np.pi * (220 * np.exp(-t * 30) + 70) * t) * np.exp(-t * 28)
    crack = hpf(noise(n), 2000) * np.exp(-t * 60) * 0.7
    return pop + crack
def heal():           # 回血：三个上行的清亮音 + 一点闪
    d = 0.6; n = int(SR * d); t = t_(d); out = np.zeros(n)
    for i, fr in enumerate([659.3, 880.0, 1318.5]):
        i0 = int(i * 0.09 * SR); tt = t[:n - i0]
        out[i0:] += (np.sin(2 * np.pi * fr * tt) + 0.3 * np.sin(2 * np.pi * fr * 2 * tt)) * np.exp(-tt * 7) * 0.5
    return out * env(n, 0.003, 0.1)
def clang():          # 打在护甲 / 护盾上：金属“铛” + 短电流
    d = 0.5; n = int(SR * d); t = t_(d)
    ring = sum(np.sin(2 * np.pi * fr * t) * np.exp(-t * dc) * a for fr, dc, a in [(820, 9, 0.6), (1370, 12, 0.4), (2210, 16, 0.3), (3160, 22, 0.2)])
    hit = lpf(noise(n), 3500) * np.exp(-t * 70) * 0.8
    zap = np.sign(np.sin(2 * np.pi * 95 * t)) * hpf(noise(n), 2500) * np.exp(-t * 14) * 0.15
    return ring + hit + zap
def down():           # 巨兽倒下：下沉的低吼 + 闷重的落地
    d = 2.0; n = int(SR * d); t = t_(d)
    f = 150 * np.exp(-t * 0.9) + 38
    growl = np.tanh(np.sin(2 * np.pi * np.cumsum(f) / SR) * 2.2) * np.exp(-t * 1.1)
    rumble = lpf(noise(n), 180) * np.exp(-np.maximum(0, t - 0.5) * 2.5) * (t > 0.5) * 1.6
    return (growl * 0.7 + rumble) * env(n, 0.02, 0.5)
def build():          # 施工：三下敲击 + 棘轮声（0.9 秒）
    d = 0.9; n = int(SR * d); t = t_(d); out = np.zeros(n)
    for s in [0.02, 0.3, 0.55]:
        i0 = int(s * SR); m = min(n - i0, int(0.12 * SR)); tt = np.arange(m) / SR
        out[i0:i0 + m] += (np.sin(2 * np.pi * 1300 * tt) * 0.4 + lpf(noise(m), 4000) * 0.6) * np.exp(-tt * 45)
    ratchet = hpf(noise(n), 1800) * (np.sin(2 * np.pi * 34 * t) > 0.7) * 0.25 * env(n, 0.05, 0.2)
    return out + ratchet
def win():            # 胜利（封印解除，11-13）：先是一声往下沉的“断电”（0.7 秒），然后四个上行的清亮和弦音，最后一个拖长带余韵（共 2.6 秒）
    d = 2.6; n = int(SR * d); t = t_(d); out = np.zeros(n)
    m = int(0.7 * SR); tt = t[:m]
    f = 420 * np.exp(-tt * 3.2) + 45
    out[:m] += np.tanh(np.sin(2 * np.pi * np.cumsum(f) / SR) * 1.8) * np.exp(-tt * 2.2) * 0.7 + lpf(noise(m), 600) * np.exp(-tt * 6) * 0.5
    for i, (st, fr) in enumerate([(0.62, 523.25), (0.82, 659.25), (1.02, 783.99), (1.26, 1046.5)]):
        i0 = int(st * SR); tt = t[:n - i0]; dec = 5.5 if i < 3 else 1.6
        tone = np.sin(2 * np.pi * fr * tt) + 0.35 * np.sin(2 * np.pi * fr * 2 * tt) + 0.2 * np.sin(2 * np.pi * fr * 1.5 * tt)
        out[i0:] += tone * np.exp(-tt * dec) * (0.42 if i < 3 else 0.55) * np.minimum(1, tt / 0.004)
    i0 = int(1.26 * SR); tt = t[:n - i0]
    out[i0:] += lpf(hpf(noise(n - i0), 3000), 9000) * np.exp(-tt * 3) * 0.12        # 最后一个音上的一点“闪”
    return out * env(n, 0.004, 0.5)
# ───────── 机甲（11-13）：脚步 / 落地 / 蓄力 / 开炮 / 失控 ─────────
def mech_step():      # 一步：沉重的金属落脚（低频“咚” + 一点金属和液压的嘶声），0.35 秒
    d = 0.35; n = int(SR * d); t = t_(d)
    thud = np.sin(2 * np.pi * (95 * np.exp(-t * 16) + 42) * t) * np.exp(-t * 15)
    metal = lpf(hpf(noise(n), 1500), 5000) * np.exp(-t * 42) * 0.3
    hiss = hpf(noise(n), 4000) * np.exp(-np.maximum(0, t - 0.08) * 22) * (t > 0.08) * 0.1
    return thud + metal + hiss
def mech_land():      # 空降落地：一声很沉的撞击 + 滚动的低频余震 + 金属嘎吱，1.5 秒
    d = 1.5; n = int(SR * d); t = t_(d)
    boom = np.tanh(np.sin(2 * np.pi * (70 * np.exp(-t * 6) + 30) * t) * 2.5) * np.exp(-t * 3.2)
    rumble = lpf(noise(n), 140) * np.exp(-t * 2.4) * 1.5
    crack = lpf(hpf(noise(n), 700), 4500) * np.exp(-t * 20) * 0.6
    creak = np.sin(2 * np.pi * (310 - 90 * t) * t) * np.exp(-np.maximum(0, t - 0.35) * 5) * (t > 0.35) * 0.12
    return (boom + rumble + crack + creak) * env(n, 0.002, 0.4)
def mech_charge():    # 蓄力：音调和颤动都越来越高的电流声，最后收住，1.1 秒
    d = 1.1; n = int(SR * d); t = t_(d); x = t / d
    f = 160 + 900 * x ** 2
    tone = np.sin(2 * np.pi * np.cumsum(f) / SR) + 0.4 * np.sin(2 * np.pi * np.cumsum(f * 2.01) / SR)
    trem = 0.7 + 0.3 * np.sin(2 * np.pi * np.cumsum(9 + 22 * x) / SR)
    air = hpf(noise(n), 3500) * x * 0.15
    return (tone * trem * 0.5 + air) * x ** 1.3 * env(n, 0.02, 0.04)
def mech_fire():      # 开炮：等离子炮 —— 一声下滑的“啾——轰”，带低频冲击，0.7 秒
    d = 0.7; n = int(SR * d); t = t_(d)
    zap = np.sin(2 * np.pi * np.cumsum(1300 * np.exp(-t * 7) + 90) / SR) * np.exp(-t * 5)
    punch = np.tanh(np.sin(2 * np.pi * (85 * np.exp(-t * 10) + 40) * t) * 2) * np.exp(-t * 7)
    crack = hpf(noise(n), 2500) * np.exp(-t * 45) * 0.5
    tail = lpf(noise(n), 900) * np.exp(-t * 6) * 0.3
    return (zap * 0.6 + punch + crack + tail) * env(n, 0.002, 0.2)
def mech_down():      # 失控：音调往下掉的电机声（断断续续）+ 金属呻吟，1.6 秒
    d = 1.6; n = int(SR * d); t = t_(d); x = t / d
    f = 520 * np.exp(-t * 1.6) + 50
    motor = np.tanh(np.sin(2 * np.pi * np.cumsum(f) / SR) * 1.6) * (0.6 + 0.4 * np.sign(np.sin(2 * np.pi * np.cumsum(7 - 4 * x) / SR)))
    groan = np.sin(2 * np.pi * np.cumsum(140 - 60 * x + 12 * np.sin(2 * np.pi * 3 * t)) / SR) * 0.35
    spark = hpf(noise(n), 3000) * (rng.random(n) > 0.9993).astype(float)
    spark = np.convolve(spark, np.exp(-np.arange(int(0.03 * SR)) / (0.006 * SR)), 'same') * 0.6
    return (motor * 0.55 + groan + spark) * env(n, 0.01, 0.5) * (1 - 0.5 * x)
# ───────── 结局（11-14）：全城断电 / 潮水 ─────────
def blackout():       # 全城断电：一声往下沉的嗡鸣（变压器停转），上面叠五下越来越远的继电器“咔哒”，3.6 秒
    d = 3.6; n = int(SR * d); t = t_(d); x = t / d
    f = 118 * np.exp(-t * 0.9) + 28
    hum = (np.sin(2 * np.pi * np.cumsum(f) / SR) + 0.5 * np.sin(2 * np.pi * np.cumsum(f * 2) / SR) + 0.25 * np.sin(2 * np.pi * np.cumsum(f * 3) / SR)) * (1 - x) ** 1.4
    out = hum * 0.6 + lpf(noise(n), 300) * (1 - x) ** 2 * 0.25
    for i, st in enumerate([0.05, 0.55, 1.15, 1.85, 2.6]):
        i0 = int(st * SR); m = int(0.09 * SR); tt = np.arange(m) / SR
        out[i0:i0 + m] += (np.sin(2 * np.pi * 900 * tt) * 0.3 + lpf(hpf(noise(m), 800), 5000)) * np.exp(-tt * 70) * (0.8 - i * 0.12)
    return out * env(n, 0.01, 0.8)
def tide():           # 潮水：三道慢慢涌上来又退下去的浪（滤过的噪声，高频随浪头变亮），10 秒，首尾淡入淡出
    d = 10.0; n = int(SR * d); t = t_(d)
    base = lpf(noise(n), 1400); bright = lpf(hpf(noise(n), 1400), 6000)
    sw = np.zeros(n)
    for c0, wd in [(1.8, 1.5), (5.0, 1.7), (8.2, 1.4)]:
        sw += np.exp(-((t - c0) / wd) ** 2 * 2.2)
    return (base * (0.25 + 0.75 * sw) + bright * sw ** 2 * 0.35) * env(n, 1.2, 1.8)
def heart():          # 濒死的心跳（11-16）：“咚—咚”两下低沉的闷响（第二下轻一点），0.55 秒
    d = 0.55; n = int(SR * d); t = t_(d); out = np.zeros(n)
    for st, amp in [(0.0, 1.0), (0.2, 0.62)]:
        i0 = int(st * SR); tt = t[:n - i0]
        out[i0:] += np.sin(2 * np.pi * (58 * np.exp(-tt * 9) + 38) * tt) * np.exp(-tt * 16) * amp + lpf(noise(n - i0), 160) * np.exp(-tt * 30) * 0.35 * amp
    return out * env(n, 0.004, 0.12)
save('sfx_incoming', incoming(), 0.7); save('sfx_shot', shot(), 0.6); save('sfx_heal', heal(), 0.6)
save('sfx_clang', clang(), 0.7); save('sfx_down', down(), 0.85); save('sfx_build', build(), 0.5)
save('sfx_win', win(), 0.7)
save('sfx_mech_step', mech_step(), 0.6); save('sfx_mech_land', mech_land(), 0.9); save('sfx_mech_charge', mech_charge(), 0.6)
save('sfx_mech_fire', mech_fire(), 0.85); save('sfx_mech_down', mech_down(), 0.7)
save('sfx_blackout', blackout(), 0.75); save('amb_tide', tide(), 0.5)
save('sfx_heart', heart(), 0.9)
