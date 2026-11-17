"""《霓虹巨兽》广播台词生成：BreezeBlue 文字转语音 → 喇叭 / 广播音效处理 → Resources/Audio/vo_<语言>_<键>.wav
密钥：放在 ~/.unity/monster-tools/breeze_key.txt（只有本机用户可读，不在工程目录里、不会被打进包），或者环境变量 BREEZE_API_KEY。
用法：
    py3 Tools/voice_gen.py --list                         列出账号里的音色（不花额度）
    py3 Tools/voice_gen.py --zh <中文音色ID> --en <英文音色ID> [--only start,leave1] [--dry]
  --dry 只列出要生成哪些、共多少字符，不发请求。已经生成过的原始 MP3（ArtSource/Voice/raw）不会重复请求；每条只请求一次，失败不自动重试（按字符计费）。
喇叭处理（纯 numpy）：带通 350～3400Hz（小喇叭的频响）→ 轻微过载（软削波）→ 两道回声（空旷城市里的广播）→ 开头一声“嘀”。"""
import sys, os, json, subprocess, pathlib, wave, urllib.request, argparse
import numpy as np
ROOT = pathlib.Path(__file__).resolve().parent.parent
RAW = ROOT / 'ArtSource' / 'Voice' / 'raw'; OUT = ROOT / 'Assets/_Monster' / 'Resources' / 'Audio'
ap = argparse.ArgumentParser(); ap.add_argument('--zh'); ap.add_argument('--en'); ap.add_argument('--only', default=''); ap.add_argument('--dry', action='store_true'); ap.add_argument('--list', action='store_true'); ap.add_argument('--instructions-zh', default='防卫指挥官通过城市广播和扩音器朝巨兽喊话：声音洪亮、严厉、急促，有压迫感，每句斩钉截铁，像真实的应急广播和警方喊话，不温柔，不是聊天的语气'); ap.add_argument('--instructions-en', default='A defense commander shouting at a giant creature through a city loudspeaker: loud, stern, urgent and commanding, clipped radio-procedure delivery like a real emergency broadcast, never gentle or conversational'); ap.add_argument('--guidance', type=float, default=2.0)
a = ap.parse_args()
# Blender 自带的 Python 没有证书库：用 macOS 系统的 CA 证书校验（不关校验，密钥只发给验过证书的服务器）
import ssl
CTX = ssl.create_default_context(cafile='/etc/ssl/cert.pem') if os.path.exists('/etc/ssl/cert.pem') else ssl.create_default_context()
def api_key():
    k = os.environ.get('BREEZE_API_KEY', '').strip()
    f = pathlib.Path.home() / '.unity' / 'monster-tools' / 'breeze_key.txt'
    if not k and f.exists(): k = f.read_text(encoding='utf-8').strip()
    return k
if a.list:
    key = api_key()
    if not key: sys.exit('还没有填密钥：~/.unity/monster-tools/breeze_key.txt')
    try:
        data = json.loads(urllib.request.urlopen(urllib.request.Request('https://api.breeze.blue/v1/voices', headers={'xi-api-key': key}), timeout=60, context=CTX).read())
    except Exception as e:
        sys.exit(f'列音色失败：{type(e).__name__} {getattr(e, "code", "")}')
    voices = data.get('voices', data if isinstance(data, list) else [])
    print(f'共 {len(voices)} 个音色')
    for v in voices:
        lab = v.get('labels') or {}
        print(' ', v.get('voice_id'), '|', v.get('name'), '|', v.get('category', ''), '|', ' '.join(f'{k}={lab[k]}' for k in lab), '|', (v.get('description') or '')[:60].replace('\n', ' '))
    sys.exit(0)
lines = {k: v for k, v in json.load(open(ROOT / 'Tools' / 'voice_lines.json', encoding='utf-8')).items() if not k.startswith('_')}
only = set(x for x in a.only.split(',') if x)
jobs = [(k, lang, v[lang]) for k, v in lines.items() for lang in ('zh', 'en') if (not only or k in only) and getattr(a, lang)]
todo = [(k, lang, t) for k, lang, t in jobs if not (RAW / f'{lang}_{k}.mp3').exists()]
print(f'台词 {len(lines)} 条；本次涉及 {len(jobs)} 句，其中要新请求 {len(todo)} 句，共 {sum(len(t) for _, _, t in todo)} 字符')
if a.dry: sys.exit(0)
key = api_key()
if todo and not key: sys.exit('还没有填密钥：~/.unity/monster-tools/breeze_key.txt')
RAW.mkdir(parents=True, exist_ok=True)
for k, lang, text in todo:
    body = {'text': text, 'language_code': lang, 'model_id': 'breeze-tts-2'}
    ins = a.instructions_zh if lang == 'zh' else a.instructions_en
    if ins: body.update(instructions=ins, voice_settings={'guidance_scale': a.guidance})
    req = urllib.request.Request(f'https://api.breeze.blue/v1/text-to-speech/{getattr(a, lang)}?output_format=mp3_24000_64', data=json.dumps(body).encode(), headers={'xi-api-key': key, 'Content-Type': 'application/json'}, method='POST')
    try:
        buf = urllib.request.urlopen(req, timeout=120, context=CTX).read()
    except Exception as e:
        print(f'  × {lang}_{k} 请求失败（不重试）：{type(e).__name__} {getattr(e, "code", "")}'); continue
    if len(buf) < 500: print(f'  × {lang}_{k} 返回的不是有效音频'); continue
    (RAW / f'{lang}_{k}.mp3').write_bytes(buf); print(f'  √ {lang}_{k}  {len(buf) // 1024}KB')

def band(x, sr, lo, hi):
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / sr)
    g = 1 / np.sqrt(1 + (lo / np.maximum(f, 1)) ** 6) / np.sqrt(1 + (f / hi) ** 6)
    return np.fft.irfft(X * g, len(x))
def pa(x, sr):
    x = band(x, sr, 350, 3400); x = x / (np.max(np.abs(x)) or 1)
    x = np.tanh(x * 2.6) / np.tanh(2.6)                                  # 轻微过载
    tail = int(sr * 0.5); y = np.concatenate([x, np.zeros(tail)])
    for d, g in ((0.11, 0.32), (0.23, 0.18), (0.41, 0.08)):               # 回声
        n = int(sr * d); y[n:] += np.concatenate([x, np.zeros(tail)])[:len(y) - n] * g
    t = np.arange(int(sr * 0.16)) / sr                                    # 开头“嘀”
    beep = np.sin(2 * np.pi * 1180 * t) * np.minimum(1, t / 0.005) * np.minimum(1, (0.16 - t) / 0.03) * 0.5
    y = np.concatenate([beep, np.zeros(int(sr * 0.09)), y]); y = band(y, sr, 300, 3600)
    return y / (np.max(np.abs(y)) or 1) * 0.85
n = 0
for mp3 in sorted(RAW.glob('*.mp3')):
    tmp = RAW / (mp3.stem + '.tmp.wav')
    subprocess.run(['afconvert', '-f', 'WAVE', '-d', 'LEI16@24000', '-c', '1', str(mp3), str(tmp)], check=True)
    with wave.open(str(tmp)) as w: sr = w.getframerate(); x = np.frombuffer(w.readframes(w.getnframes()), np.int16).astype(float) / 32768
    tmp.unlink(); y = pa(x, sr)
    with wave.open(str(OUT / f'vo_{mp3.stem}.wav'), 'wb') as w: w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr); w.writeframes((y * 32767).astype(np.int16).tobytes())
    n += 1
print(f'已处理成喇叭音效 {n} 条 → {OUT}')
