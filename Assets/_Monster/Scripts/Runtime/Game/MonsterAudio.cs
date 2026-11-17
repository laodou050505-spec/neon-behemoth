// 声音（全部 Tools/audio_synth.py 程序合成，原创无版权）：音效池（3D 空间音效，城市里发生的事听得出方向）+ 两首背景乐交叉淡变 + 城市底噪。
// 头显切到后台时暂停。
using System.Collections.Generic;
using UnityEngine;

namespace Monster.Game
{
    public class MonsterAudio : MonoBehaviour
    {
        static MonsterAudio inst;
        readonly Dictionary<string, AudioClip> clips = new();
        readonly List<AudioSource> pool = new();
        AudioSource musicA, musicB, amb; string curMusic; float fade;
        public static float MusicVol = 0.45f, SfxVol = 1f;

        public static MonsterAudio I
        {
            get
            {
                if (inst) return inst;
                inst = new GameObject("MonsterAudio").AddComponent<MonsterAudio>();
                DontDestroyOnLoad(inst.gameObject);
                return inst;
            }
        }

        void Awake()
        {
            AudioSource Src(bool loop) { var s = gameObject.AddComponent<AudioSource>(); s.loop = loop; s.playOnAwake = false; s.spatialBlend = 0; return s; }
            musicA = Src(true); musicB = Src(true); amb = Src(true);
            if (!Camera.main || !Camera.main.GetComponent<AudioListener>()) { var c = Camera.main; if (c && !FindAnyObjectByType<AudioListener>()) c.gameObject.AddComponent<AudioListener>(); }
        }

        AudioClip Clip(string name)
        {
            if (clips.TryGetValue(name, out var c)) return c;
            return clips[name] = Resources.Load<AudioClip>("Audio/" + name);
        }

        /// <summary>播一个音效：pos 给定就是 3D（城市里的事），否则 2D（界面）</summary>
        public static void Play(string name, Vector3? pos = null, float vol = 1, float pitch = 1)
        {
            var me = I; var clip = me.Clip(name); if (!clip) return;
            AudioSource s = null;
            foreach (var p in me.pool) if (!p.isPlaying) { s = p; break; }
            if (s == null)
            {
                if (me.pool.Count >= 24) s = me.pool[0];
                else { var go = new GameObject("sfx"); go.transform.SetParent(me.transform, false); s = go.AddComponent<AudioSource>(); s.playOnAwake = false; s.rolloffMode = AudioRolloffMode.Logarithmic; s.minDistance = 0.4f; s.maxDistance = 6; s.dopplerLevel = 0; me.pool.Add(s); }
            }
            s.transform.position = pos ?? Vector3.zero; s.spatialBlend = pos.HasValue ? 0.85f : 0;
            s.clip = clip; s.volume = vol * SfxVol; s.pitch = pitch * Random.Range(0.94f, 1.06f);
            s.Play();
        }

        // ───────── 广播台词（11-13 用户：加集团广播 / 喇叭的喊话）─────────
        // 文件 Resources/Audio/vo_<zh|en>_<键>.wav（Tools/voice_gen.py 生成，台词表 Tools/voice_lines.json）；文件不存在就什么都不做。
        // 一次只说一句：正在说的时候来了新的，重要的（force）打断，不重要的丢掉；说话时背景乐压到四成。
        AudioSource voice; float duck = 1;
        /// <summary>濒死：背景音乐压低一半（HurtOverlay 每帧设）</summary>
        public static bool Strain;
        public static bool Voice(string key, bool force = false)
        {
            var me = I; var clip = me.Clip("vo_" + (Loc.En ? "en_" : "zh_") + key); if (!clip) return false;
            if (!me.voice) { me.voice = me.gameObject.AddComponent<AudioSource>(); me.voice.playOnAwake = false; me.voice.spatialBlend = 0; }
            if (me.voice.isPlaying && !force) return false;
            me.voice.clip = clip; me.voice.volume = Mathf.Clamp01(SfxVol * 1.1f); me.voice.Play();
            return true;
        }
        public static bool Speaking => I.voice && I.voice.isPlaying;

        /// <summary>换背景乐（交叉淡变 1.5 秒）；null = 淡出</summary>
        public static void Music(string name)
        {
            var me = I; if (me.curMusic == name) return;
            me.curMusic = name;
            (me.musicA, me.musicB) = (me.musicB, me.musicA);   // A 是新的
            me.musicA.clip = name != null ? me.Clip(name) : null; me.musicA.volume = 0;
            if (me.musicA.clip) me.musicA.Play();
            me.fade = 0;
        }
        public static void Ambience(bool on) { var me = I; me.amb.clip = me.Clip("amb_city"); me.amb.volume = on ? 0.35f * SfxVol : 0; if (on && !me.amb.isPlaying) me.amb.Play(); }

        void Update()
        {
            fade = Mathf.Min(1, fade + Time.unscaledDeltaTime / 1.5f);
            duck = Mathf.MoveTowards(duck, voice && voice.isPlaying ? 0.4f : Strain ? 0.5f : 1, Time.unscaledDeltaTime * 2);
            musicA.volume = fade * MusicVol * duck; musicB.volume = (1 - fade) * MusicVol * duck;
            if (fade >= 1 && musicB.isPlaying) musicB.Stop();
        }
        void OnApplicationPause(bool p) { AudioListener.pause = p; }

        /// <summary>城市里发生的事 → 音效（GameView.Handle 调）</summary>
        public static void OnEvent(Ev ev, MonsterGame g)
        {
            Vector3? P(int id) => g.city.byId.TryGetValue(id, out var b) ? MonsterGame.TopOf(b) : (Vector3?)null;
            switch (ev.type)
            {
                case "collapse": Play(ev.from - ev.to >= 3 ? "sfx_collapse" : "sfx_smash", P(ev.id), Mathf.Clamp(0.5f + (ev.from - ev.to) * 0.08f, 0.5f, 1)); break;
                case "blast": Play("sfx_blast", ev.pos, Mathf.Clamp01(0.4f + ev.r * 4)); break;
                case "tower_down": Play("sfx_tower_down", P(ev.id)); break;
                case "shield_hit": case "shield_break": Play("sfx_shield", ev.pos ?? P(ev.id)); break;
                case "hurt": Play("sfx_hurt", null, 0.8f); break;
                case "grab": case "uproot": Play("sfx_pinch", null, 0.7f); break;
                case "throw": Play("sfx_throw", null, 0.8f); break;
                case "breath_start": Play("sfx_breath"); break;
                case "star_up": case "counter": Play("sfx_star_up", null, 0.6f); break;
                case "tut_pass": Play("ui_press", null, 0.8f, 1.3f); break;
                case "beam_hit": Play("sfx_blast", ev.pos, 0.7f, 1.25f); break;
                case "clap": Play("sfx_smash", ev.pos, 0.9f, 1.4f); break;
                case "quake": Play("sfx_mech_land", ev.pos, 0.85f, 1.25f); break;
            }
        }
    }
}
