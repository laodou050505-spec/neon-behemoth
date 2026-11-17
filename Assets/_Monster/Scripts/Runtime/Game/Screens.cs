// 标题 / 关前简报 / 结算（全是场景实物：霓虹招牌 + 霓虹按键牌 + 黄色旁白卡），以及局内 HUD（楼顶通缉警灯牌、城市上空的封印光环）。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Monster.Game
{
    /// <summary>进度存档：解锁到第几关、每关最好星级</summary>
    public static class Progress
    {
        public static int Unlocked { get => PlayerPrefs.GetInt("monster.unlocked", 0); set { PlayerPrefs.SetInt("monster.unlocked", Mathf.Max(Unlocked, value)); PlayerPrefs.Save(); } }
        public static int Stars(int lv) => PlayerPrefs.GetInt("monster.stars." + lv, 0);
        public static void SetStars(int lv, int s) { if (s > Stars(lv)) { PlayerPrefs.SetInt("monster.stars." + lv, s); PlayerPrefs.Save(); } }
        public static bool EndingSeen { get => PlayerPrefs.GetInt("monster.ending", 0) == 1; set { PlayerPrefs.SetInt("monster.ending", value ? 1 : 0); PlayerPrefs.Save(); } }
        /// <summary>
        /// 存档版本迁移（启动时调一次）。11-14：关卡从 6 个区加到 9 个区，6、7、8 是新插进来的，原来的第 6 区（决战）挪到了第 9 区 ——
        /// 老存档里“第 6 区的星”其实是旧决战的：清掉；已经解锁到 7（= 旧版全通）的收回到 6，从新的第 6 区接着打
        /// </summary>
        public static void Migrate()
        {
            if (PlayerPrefs.GetInt("monster.saveVer", 1) >= 2) return;
            if (PlayerPrefs.GetInt("monster.unlocked", 0) > 6) PlayerPrefs.SetInt("monster.unlocked", 6);
            PlayerPrefs.DeleteKey("monster.stars.6");
            PlayerPrefs.SetInt("monster.saveVer", 2); PlayerPrefs.Save();
        }
    }

    /// <summary>设置（标题“设置”页；存 PlayerPrefs）。启动时 Apply() 一次，音量立刻生效</summary>
    public static class GameSettings
    {
        static readonly float[] MusicVols = { 0f, 0.25f, 0.45f, 0.7f }, SfxVols = { 0f, 0.6f, 1f };
        public static int Music { get => PlayerPrefs.GetInt("monster.musicLv", 2); set { PlayerPrefs.SetInt("monster.musicLv", value); PlayerPrefs.Save(); Apply(); } }
        public static int Sfx { get => PlayerPrefs.GetInt("monster.sfxLv", 2); set { PlayerPrefs.SetInt("monster.sfxLv", value); PlayerPrefs.Save(); Apply(); } }
        /// <summary>城市高度：-1 低 / 0 标准 / 1 高（桌面上下各 6cm）</summary>
        public static int CityHeight { get => PlayerPrefs.GetInt("monster.cityH", 0); set { PlayerPrefs.SetInt("monster.cityH", value); PlayerPrefs.Save(); } }
        public static float CityHeightOffset => CityHeight * 0.06f;
        public static bool Intro { get => PlayerPrefs.GetInt("monster.intro", 1) == 1; set { PlayerPrefs.SetInt("monster.intro", value ? 1 : 0); PlayerPrefs.Save(); } }
        public static void Apply() { MonsterAudio.MusicVol = MusicVols[Mathf.Clamp(Music, 0, 3)]; MonsterAudio.SfxVol = SfxVols[Mathf.Clamp(Sfx, 0, 2)]; }
        public static void ResetProgress() { foreach (var k in new[] { "monster.unlocked", "monster.ending" }) PlayerPrefs.DeleteKey(k); for (int i = 0; i < 16; i++) PlayerPrefs.DeleteKey("monster.stars." + i); PlayerPrefs.Save(); }
        public static string MusicLabel => new[] { "音乐：关", "音乐：低", "音乐：中", "音乐：高" }[Mathf.Clamp(Music, 0, 3)];
        public static string SfxLabel => new[] { "音效：关", "音效：中", "音效：高" }[Mathf.Clamp(Sfx, 0, 2)];
        public static string HeightLabel => new[] { "城市高度：低", "城市高度：标准", "城市高度：高" }[Mathf.Clamp(CityHeight + 1, 0, 2)];
        public static string IntroLabel => Intro ? "开场漫画：播放" : "开场漫画：跳过";
    }

    static class Shapes
    {
        /// <summary>五角星（xy 平面，外半径 1）</summary>
        public static Mesh Star()
        {
            var v = new List<Vector3> { Vector3.zero };
            for (int i = 0; i < 10; i++) { float a = Mathf.PI / 2 + i * Mathf.PI / 5, r = i % 2 == 0 ? 1 : 0.45f; v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0)); }
            var t = new List<int>(); for (int i = 1; i <= 10; i++) { t.Add(0); t.Add(i % 10 + 1); t.Add(i); }
            var m = new Mesh { name = "star" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
    }

    // ═══════════ 局内 HUD：楼顶通缉警灯牌 + 城市上空的封印光环 ═══════════
    /// <summary>
    /// 局内牌子的实体外壳（11-09 用户：牌子做好看点）：和标题招牌同一套做法 —— 1.2cm 厚的深色铁皮背盒、钢框、铆钉。
    /// 牌面（本地 −Z 朝玩家）宽 w、高 h，底边在本地 y=y0；外壳都在牌面后面 / 四周，不挡字。
    /// </summary>
    public static class HudFrame
    {
        public static void Build(Transform p, float w, float h, float y0)
        {
            var steel = CityMaterials.Solid("#565c74"); var dark = CityMaterials.Solid("#262836"); var rivet = CityMaterials.Solid("#9aa0b8");
            const float F = 0.006f;
            Part(p, CityMeshes.Box(), dark, new Vector3(0, y0 - 0.008f, 0.0075f), new Vector3(w + 0.024f, h + 0.016f, 0.012f));
            Part(p, CityMeshes.Box(), steel, new Vector3(0, y0 + h + 0.002f, -0.0005f), new Vector3(w + 0.024f, F, F + 0.004f));
            Part(p, CityMeshes.Box(), steel, new Vector3(0, y0 - 0.008f, -0.0005f), new Vector3(w + 0.024f, F, F + 0.004f));
            foreach (var sx in new[] { -1, 1 }) Part(p, CityMeshes.Box(), steel, new Vector3(sx * (w / 2 + 0.009f), y0 - 0.008f, -0.0005f), new Vector3(F, h + 0.016f, F + 0.004f));
            for (float x = -w / 2; x <= w / 2 + 1e-4f; x += 0.04f)
                foreach (var y in new[] { y0 + h + 0.005f, y0 - 0.005f }) Part(p, CityMeshes.Sphere(8, 5), rivet, new Vector3(x, y - 0.0015f, -0.0035f), Vector3.one * 0.003f);
        }
        static void Part(Transform p, Mesh m, Material mat, Vector3 pos, Vector3 size)
        {
            var go = new GameObject("Frame"); go.transform.SetParent(p, false); go.transform.localPosition = pos; go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    public class CityHud : MonoBehaviour
    {
        public MonsterGame game; public GameView view; public Camera cam;
        public void Init(MonsterGame g, GameView v, Camera c) { game = g; view = v; cam = c; }
        Transform wanted; readonly List<Material> beacons = new(); TextMeshPro wantedLabel; HpBar hpBar;
        int lastStars = -1; float flash;
        const int N = 40;
        void Start()
        {
            if (!cam) cam = Camera.main;
            var z = game.city.zones.Find(q => q.kind == ZoneKind.Table) ?? game.city.zones[0];
            // 通缉警灯牌：一排 5 个警灯。站着玩（11-08）：不再立在桌子近边（要低头很多），
            // 架在桌子远边的左角外、高过桌上最高的楼（和教学牌一样像记分牌），看城市时余光就能扫到
            wanted = new GameObject("WantedSign").transform;
            float tall = 0;
            foreach (var b in game.city.buildings) if (b.zone == z) tall = Mathf.Max(tall, MonsterGame.TopOf(b).y - z.origin.y);
            float lift = Mathf.Clamp(tall + 0.02f, 0.1f, 0.34f);
            // 教学关：警灯并到教学牌顶框上（两块牌挤在窄桌上会互相穿插）；正式关：单独一块，居中立在远边
            float ca = Mathf.Clamp(Vector3.Dot(cam.transform.position - z.origin, z.u), 0.15f, z.w - 0.15f);
            wanted.position = z.Point(ca, CityEdge.Back(view.builder, z) - 0.03f, lift);
            if (game.tut != null) wanted.gameObject.SetActive(false);
            var flat = wanted.position - cam.transform.position; flat.y = 0;
            float sc = Mathf.Clamp(flat.magnitude / 0.8f, 1f, 1.5f);
            wanted.localScale = Vector3.one * sc;
            // 11-12 用户：通缉牌的质感要做好，像真的广告牌（原来是一块发光的平板）→ Billboard.Build：
            // 铁皮背盒 + 角钢边框 + 铆钉 + 印刷海报牌面 + 顶上三盏鹅颈射灯 + 底下检修走道 + 两根带斜撑的立柱。
            // 牌面 38×16cm：左边通缉令照片（胶带贴着），右边“通缉”+ 五盏星灯 + 体力条（11-11 用户：看不到血量在哪）。
            // 边距：内容离边框 2cm 以上；星灯常亮不闪，只在升星那 1.2 秒红蓝交替（好看但不抢眼）
            const float W = 0.38f, H = 0.16f, Y0 = 0.02f, Pad = 0.022f;
            Billboard.Build(wanted, W, H, Y0, lift / sc + Y0);
            float posterR = -W / 2 + Pad;
            var mug = NeonKit.Tex("mugshot");
            if (mug)
            {
                var pm = NeonKit.Mat(Color.white, 3153); pm.mainTexture = mug;
                float ph = H - 0.05f, pw = ph * mug.width / Mathf.Max(1f, mug.height); posterR = -W / 2 + Pad + 0.004f + pw;
                var poster = NeonKit.Shape(wanted, "Poster", NeonKit.Quad(), new Vector3(-W / 2 + Pad + 0.004f + pw / 2, Y0 + H / 2, -0.0015f), pm);
                poster.localScale = new Vector3(pw, ph, 1); poster.localRotation = Quaternion.Euler(0, 0, -2.5f);
                var tape = NeonKit.Tex("tape_b");
                if (tape) { var tm = NeonKit.Mat(Color.white, 3154); tm.mainTexture = tape; var tq = NeonKit.Shape(wanted, "Tape", NeonKit.Quad(), new Vector3(-W / 2 + Pad + 0.004f + pw / 2, Y0 + H / 2 + ph / 2 - 0.001f, -0.002f), tm); tq.localScale = new Vector3(0.032f, 0.013f, 1); tq.localRotation = Quaternion.Euler(0, 0, 6); }
            }
            // 右半区：三行（标题 / 星灯 / 体力条），整组左右居中
            float rx0 = posterR + 0.02f, rx1 = W / 2 - Pad, gx = (rx0 + rx1) / 2;
            wantedLabel = NeonKit.Text(wanted, new Vector3(gx, Y0 + H - 0.042f, -0.0015f), 30, new Color(1f, 0.36f, 0.72f), 3153);
            NeonKit.SetText(wantedLabel, "通缉", 3153);
            wantedLabel.font = Resources.Load<TMP_FontAsset>(Loc.En ? "Fonts/ComicEN SDF" : "Fonts/GraffitiZH SDF"); wantedLabel.fontMaterial.renderQueue = 3153;
            wantedLabel.outlineWidth = 0.22f; wantedLabel.outlineColor = new Color32(20, 12, 30, 255);
            wantedLabel.transform.localRotation = Quaternion.Euler(0, 0, 3);
            for (int i = 0; i < 5; i++) beacons.Add(Billboard.Lamp(wanted, new Vector3(gx + (i - 2) * 0.031f, Y0 + H * 0.47f, -0.0015f), 0.02f, 3152));
            hpBar = new HpBar(wanted, new Vector3(rx0, Y0 + 0.036f, -0.0015f), rx1 - rx0, 0.013f, 3152);
            rageBar = new HpBar(wanted, new Vector3(rx0, Y0 + 0.02f, -0.0015f), rx1 - rx0, 0.008f, 3152, true);
            // 核打击倒计时（11-12：原来城市上空那圈“封印光环”看不懂 → 去掉；倒计时做成牌子下面挂的一块红色数码屏）：
            // “核打击 02:30”，最后 30 秒字变大、慢闪；到点后显示“核弹来袭！”，核弹从天上落下来，一击致命
            NeonKit.Shape(wanted, "NukeBox", NeonKit.RoundRect(0.17f, 0.03f, 0.006f), new Vector3(0, Y0 - 0.04f, -0.031f), NeonKit.Mat(new Color(0.05f, 0.03f, 0.06f), 3151));
            nukeText = NeonKit.Text(wanted, new Vector3(0, Y0 - 0.04f, -0.0325f), 17, new Color(1f, 0.25f, 0.3f), 3153);
            nukeText.font = Resources.Load<TMP_FontAsset>(Loc.En ? "Fonts/ComicEN SDF" : "Fonts/GraffitiZH SDF"); nukeText.fontMaterial.renderQueue = 3153;
        }
        TextMeshPro nukeText; HpBar rageBar; int lastSec = -99;
        void OnDestroy() { if (wanted) Destroy(wanted.gameObject); }
        void LateUpdate()
        {
            if (game == null || !wanted) return;
            bool show = !view.paused && game.result == null && game.tut == null;
            wanted.gameObject.SetActive(show);
            if (!show) return;
            // 警灯牌转向玩家
            var to = cam.transform.position - wanted.position; to.y = 0;
            if (to.sqrMagnitude > 1e-4f) wanted.rotation = Quaternion.LookRotation(-to.normalized, Vector3.up);
            hpBar?.Set(game.hp / Cfg.HpMax, Mathf.Max(view.hurtFlash, view.HeartBeat * 0.8f));   // 濒死：生命条跟着心跳一亮一亮
            if (game.stars != lastStars) { flash = lastStars >= 0 && game.stars > lastStars ? 1.2f : 0; lastStars = game.stars; }
            flash = Mathf.Max(0, flash - Time.deltaTime);
            // 胜利过程：通缉星从右往左一颗颗熄灭（每 0.5 秒一颗）
            int shownStars = game.Winning ? Mathf.Max(0, game.stars - Mathf.FloorToInt(Mathf.Max(0, game.winT - 0.8f) / 0.5f) - (game.winT > 0.8f ? 1 : 0)) : game.stars;
            for (int i = 0; i < 5; i++)
            {
                bool lit = i < shownStars;
                bool blue = ((int)(Time.time * 6) + i) % 2 == 0;
                // 平时常亮不闪（不抢眼）；只在刚升星的 1.2 秒里红蓝交替
                var c = lit ? (flash > 0 ? (blue ? new Color(0.3f, 0.6f, 1.8f) : new Color(1.8f, 0.3f, 0.3f)) : new Color(1.25f, 0.85f, 0.25f)) : new Color(0.25f, 0.22f, 0.3f);
                beacons[i].color = c;
            }
            rageBar?.Set(game.rage / Cfg.RageMax, 0);
            if (nukeText)
            {
                float left = Mathf.Max(0, game.sealMax - game.seal); int sec = game.Winning ? -7 : Mathf.CeilToInt(left);
                if (sec != lastSec)
                {
                    lastSec = sec;
                    nukeText.text = game.Winning ? Loc.T("封印解除") : game.nukeT >= 0 ? Loc.T("核弹来袭！") : Loc.T("核打击 ") + (sec / 60) + ":" + (sec % 60).ToString("00");
                    var pv = nukeText.GetPreferredValues(nukeText.text, 9999, 0); nukeText.rectTransform.sizeDelta = new Vector2(pv.x + 1, pv.y + 1);
                }
                bool hurry = left < 30 && !game.Winning;
                if (game.Winning) nukeText.color = new Color(0.45f, 1f, 0.95f);
                else nukeText.color = hurry ? Color.Lerp(new Color(1f, 0.2f, 0.25f), new Color(1f, 0.9f, 0.3f), 0.5f + 0.5f * Mathf.Sin(Time.time * 6f)) : new Color(1f, 0.3f, 0.35f);
                nukeText.transform.localScale = Vector3.one * 0.01f * (hurry ? 1.15f : 1f);
            }
        }
    }
}
