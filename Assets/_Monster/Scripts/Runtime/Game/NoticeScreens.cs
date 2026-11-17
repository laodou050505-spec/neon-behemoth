// 标题 / 关前简报 / 结算 —— 告示纸版（见 Notice.cs 顶部说明）。全部只是悬在真实房间里的纸片，不出现游戏场景。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Monster.Game
{
    /// <summary>三个界面共用：纸片摆位（正对眼睛、转头才跟）、弹出动画、指针</summary>
    public abstract class NoticeScreen : MonoBehaviour
    {
        public Camera cam; public MonsterInput input;
        public Transform Root => page;
        protected Transform page; protected Follow follow; protected readonly Pointer ptr = new(); public readonly NoticeMenu menu = new();
        protected readonly List<Sheet> sheets = new(); protected readonly List<Shout> shouts = new(); protected readonly List<Glitch> glitches = new();
        protected float t; float shake;
        // 站着玩：纸摆在胸前、伸手就能点到的地方（离眼 0.55m、低于视线约 25°，纸面朝眼睛仰起来像讲台），
        // 纸按 size 缩小（视角大小和以前 1.1m 远时差不多）。看着 + 捏一下、手指直接点，两种都行
        public const float NearD = 0.55f, NearDrop = 0.25f, TitleDrop = 0.29f;
        /// <summary>
        /// 11-17 用户：战斗开始前的这块面板再抬高一些，现在还是要低着头看 —— 关前简报、结算、结局公告的纸从“视线往下 24°”抬到“往下 9°”
        /// （纸中心比眼睛低 9cm，上沿到视线以上）。标题页的纸不动：它上面挂着霓虹招牌，再抬会顶到招牌
        /// </summary>
        public const float EyeDrop = 0.09f;
        protected float size = 1, delay;   // delay：晚一点再弹出来（标题等招牌落地通电）
        Transform cursor;
        protected Sheet NewSheet(string name, int q) { var s = new Sheet(page, name, q); sheets.Add(s); return s; }
        protected void Setup(string name, float d, float drop, float size = 1)
        {
            if (!cam) cam = Camera.main;
            this.size = size;
            ptr.input = input; ptr.cam = cam;
            page = new GameObject(name).transform;
            follow = new Follow { t = page, cam = cam, d = d, drop = drop, minY = MinY() }; follow.Place(1);
            menu.page = page;
            page.localScale = Vector3.one * 0.001f;
            Loc.Changed += Relabel;
            if (delay <= 0) MonsterAudio.Play("ui_page");
            // 头向瞄准时的小光标（眼动正常时不画：眼睛看哪儿就是哪儿，画了反而晃）
            var cs = new Sheet(page, "Cursor", 3400);
            cursor = cs.Img("doodle_circle", Vector2.zero, 0.022f, 0, new Color(0.4f, 1f, 0.95f, 0.95f));
            cursor.localPosition = new Vector3(0, 0, -0.03f);
            cursor.gameObject.SetActive(false);
        }
        /// <summary>
        /// 纸中心的最低高度（纸约 0.66 × size 高）：城市还在桌上时（结算）下沿抬到最高楼顶上面 3cm；
        /// 11-13 用户：进下一关时界面太低 —— 纸是按“视线往下 25cm”摆的，坐着玩时这个位置已经在桌面以下了。
        /// 只要知道面前桌面的高度（MonsterMR.TableY：真桌子扫描到就有；重开关卡沿用上一关的），纸的下沿就至少比桌面高 6cm。每帧算，桌子后识别到也能跟上
        /// </summary>
        protected float MinY()
        {
            if (MonsterMR.CityTopY > 0 && MonsterMR.CityShown) return MonsterMR.CityTopY + 0.33f * size + 0.03f;
            if (MonsterMR.TableY > 0) return MonsterMR.TableY + 0.33f * size + 0.06f;
            return -1;
        }
        protected virtual void Relabel() { foreach (var s in sheets) s.Relabel(); }
        protected virtual void OnDestroy() { Loc.Changed -= Relabel; if (page) Destroy(page.gameObject); }
        protected void Shake() => shake = 1;
        protected bool Shown => t > delay;
        protected virtual void Update()
        {
            if (follow == null || !page) return;   // 还没 Setup / 页面已随界面销毁
            float dt = Time.deltaTime;
            if (!Shown && t + dt > delay && delay > 0) MonsterAudio.Play("ui_page");
            t += dt;
            follow.minY = MinY();
            follow.Place(1 - Mathf.Exp(-dt * 3));
            // 弹出：从小、带点旋转甩进来
            float x = Mathf.Clamp01((t - delay) / 0.45f); const float c = 1.7f;
            float e = Mathf.Max(0.001f, 1 + (c + 1) * Mathf.Pow(x - 1, 3) + c * Mathf.Pow(x - 1, 2));
            shake = Mathf.Max(0, shake - dt * 4);
            page.localScale = Vector3.one * e * size;
            page.rotation = page.rotation * Quaternion.Euler(0, 0, (1 - Mathf.Clamp01(e)) * 12 + Mathf.Sin(t * 60) * shake * 1.5f);
            foreach (var s in shouts) s.Tick(dt);
            foreach (var g in glitches) g.Tick(dt);
            ptr.Update();
            if (t - delay > 0.5f) menu.Tick(dt, ptr);
            // 光标：头向瞄准、射线打在纸上时才显示
            bool cur = false;
            if (ptr.headGaze && t - delay > 0.5f && new Plane(-page.forward, page.position).Raycast(ptr.ray, out var hit))
            {
                var lp = page.InverseTransformPoint(ptr.ray.GetPoint(hit));
                if (Mathf.Abs(lp.x) < 0.45f && Mathf.Abs(lp.y) < 0.45f) { cur = true; cursor.localPosition = new Vector3(lp.x, lp.y, cursor.localPosition.z); }
            }
            if (cursor.gameObject.activeSelf != cur) cursor.gameObject.SetActive(cur);
        }
    }

    // ═══════════ 标题 ═══════════
    public class TitleScreen : NoticeScreen
    {
        public int chosen = -1;
        TitleSign sign; readonly List<NoticeItem> mainItems = new(), levelItems = new(), setItems = new();
        readonly List<(NoticeItem it, Func<string> label)> dyn = new();   // 设置页上文字会变的选项
        readonly List<GameObject> levelDeco = new(), mainDeco = new(), rightDeco = new(); bool resetArmed; Shout titleShout;
        void Start()
        {
            // 标题招牌：立在房间地板上的 3D 霓虹广告牌（砸下来 → 通电），纸质菜单等它亮到一半再弹到胸前
            MonsterMR.Mark("标题.开始");
            sign = new GameObject("TitleSignCtl").AddComponent<TitleSign>(); sign.cam = cam ? cam : Camera.main; sign.floorY = transform.position.y;
            delay = 99;
            // 11-09 用户：菜单纸太窄太小、按钮太少（至少要能退出）→ 纸加宽成近方形、整体放大，左列 5 个选项，右列涂鸦 + 手写提示
            // 11-17 用户：标题的菜单纸往下挪一点，别挡住标题上的字 —— 纸中心从“眼睛往下 25cm”降到 29cm。
            // 另外识别到真桌子时纸会被抬高（下沿不插进桌面，见 MinY），坐着玩时一抬就压到招牌下沿和出品铭牌：
            // 纸被抬高多少，招牌就跟着整体抬高多少（sign.lift，招牌搭起来之前算好；立柱照样落地）
            Setup("TitleScreen", 0.6f, TitleDrop, 0.68f);
            { float eye = (cam ? cam : Camera.main).transform.position.y, want = eye - TitleDrop, my = MinY(); sign.lift = my > 0 ? Mathf.Max(0, Mathf.Min(my, eye + 0.1f) - want) * 1.2f : 0; }   // ×1.2：招牌比纸远（0.73m 对 0.6m），抬同样的视角要多抬一点
            GameSettings.Apply();
            MonsterMR.Mark("标题.Setup");
            MonsterAudio.Music("music_title"); MonsterAudio.Ambience(false);
            MonsterMR.Mark("标题.音乐");
            // ── 告示纸（底）──
            var paper = NewSheet("Paper", 3100);
            var pc = new Vector2(0, -0.03f); const float PW = 0.64f, PH = 0.66f, PR = 1.6f;
            paper.Img("paper_notice", pc, PH, PR, null, PW);
            // 11-11 用户：这块面板左边的字离纸边太近 —— 所有左对齐的字从左版心线往里起排（见 Sheet.PaperX），顺着纸歪的角度走；
            // 页眉放在黑条正中，小标题放在粉线和上版心线之间（原来压在版心线上）
            float XL(float y) => Sheet.PaperX(pc, PW, PR, Sheet.TextL, y);
            Vector2 P(float y) => new Vector2(XL(y), y);
            float barY = Sheet.PaperY(pc, PH, PR, Sheet.BarMid, XL(0.23f)), subY = Sheet.PaperY(pc, PH, PR, 0.178f, XL(0.18f));
            paper.Fit(paper.Text("霓虹动力集团 · 公告", Ink.Face.Corp, P(barY), 0.026f, Ink.Paper, 1.6f), 0.33f);   // 右边还有编号：超宽就缩（英文页眉更长）
            paper.Text("NO.0730 · 霓湾市", Ink.Face.Corp, new Vector2(0.12f, 0.24f), 0.013f, new Color(0.7f, 0.7f, 0.72f), 1.6f);
            paper.Fit(paper.Text("关于海湾异常生物的紧急通告", Ink.Face.Corp, P(subY), 0.015f, Ink.Gray, 1.6f), 0.23f);
            paper.Rect(new Vector2(XL(subY) + 0.15f, subY + 0.004f), new Vector2(0.075f, 0.019f), Ink.Black, 1.6f);   // 涂黑
            paper.Text("是我！", Ink.Face.Note, new Vector2(0.0f, 0.211f), 0.046f, Ink.Cyan, -10);
            paper.Img("doodle_arrow", new Vector2(-0.035f, 0.197f), 0.024f, -150);
            // 11-17 用户：菜单纸最下面也印一行“Tripo XR 出品”（英文 Tripo XR Developers），前面是公司标志。
            // 当成印刷品的页脚：放在纸底部两条横线的正中，顺着纸的角度；标志用深色版（纸是浅色的）、原样不涂鸦，字用公文字
            // 11-17 用户（先说居中，后来定）：和上面菜单文字的开头左对齐 —— 标志的左边缘对齐“继续 / 玩法 / …”那一列的起点（左版心线往里的那条字线，顺着纸的角度）
            footMark = paper.Img("brand_mark_dark", Vector2.zero, FootMarkH, PR);
            footText = paper.Fit(paper.Text("Tripo XR 出品", Ink.Face.Corp, Vector2.zero, 0.0215f, Ink.Black, PR), 0.19f);
            footY = fx => Sheet.PaperY(pc, PH, PR, FootF, fx); footCx = XL(pc.y + PH / 2 - FootF * PH);
            LayoutFoot();
            // ── 选项（公文字，看着 / 手指靠近时喷漆带刷出来）──
            // 11-10 用户：封面上要有“玩法”按钮（玩中学）；“开始游戏”直接进第一夜（第 0 关 = 玩法）
            // 从没玩过（玩法也没过）的新玩家点“开始游戏”先进玩法，不然第一夜不知道怎么玩
            int next = Progress.Unlocked <= 0 ? 0 : Mathf.Clamp(Progress.Unlocked, 1, Level.All.Length - 1);
            const float H = 0.044f;
            mainItems.Add(new NoticeItem(paper, next <= 1 ? "开始游戏" : "继续", P(0.105f), H, 0.22f, () => Choose(next), 1.6f));
            mainItems.Add(new NoticeItem(paper, "玩法", P(0.04f), H, 0.16f, () => Choose(0), 1.6f));
            mainItems.Add(new NoticeItem(paper, "选关", P(-0.025f), H, 0.16f, ShowLevels, 1.6f));
            // 11-10 用户：开局先提一下桌子，并能直接跳到系统的房间扫描（扫完回来自动重读，城市摆到真桌上）
            var scan = new NoticeItem(paper, RoomScanLabel(), P(-0.09f), H * 0.8f, 0.24f, () => { RoomTable.Instance?.Scan(); Relabel(); }, 1.6f);
            dyn.Add((scan, RoomScanLabel)); mainItems.Add(scan);
            // 11-09 用户：外面别堆一排按钮 → 音乐 / 语言等收进“设置”
            mainItems.Add(new NoticeItem(paper, "设置", P(-0.155f), H * 0.9f, 0.16f, ShowSettings, 1.6f));
            mainItems.Add(new NoticeItem(paper, "退出游戏", P(-0.22f), H * 0.8f, 0.2f, Quit, 1.6f));
            menu.items.AddRange(mainItems);
            // 设置页（同一张纸上替换选项）：点一下切到下一档
            float sy = 0.11f; const float SH = 0.03f, SS = 0.047f;
            void Opt(Func<string> label, Action act) { var it = new NoticeItem(paper, label(), P(sy), SH, 0.24f, () => { act(); Relabel(); }, 1.6f); dyn.Add((it, label)); setItems.Add(it); menu.items.Add(it); sy -= SS; }
            Opt(() => GameSettings.MusicLabel, () => GameSettings.Music = (GameSettings.Music + 1) % 4);
            Opt(() => GameSettings.SfxLabel, () => { GameSettings.Sfx = (GameSettings.Sfx + 1) % 3; MonsterAudio.Play("ui_press"); });
            Opt(() => Loc.En ? "Language: English" : "语言：中文", () => Loc.Toggle());
            Opt(() => GameSettings.HeightLabel, () => GameSettings.CityHeight = (GameSettings.CityHeight + 2) % 3 - 1);
            Opt(RoomScanLabel, () => RoomTable.Instance?.Scan());   // 11-10 用户：设置里也要能跳到系统房间扫描
            Opt(() => GameSettings.IntroLabel, () => GameSettings.Intro = !GameSettings.Intro);
            Opt(() => resetArmed ? "再点一次确认清除" : "清除进度", () => { if (resetArmed) { GameSettings.ResetProgress(); resetArmed = false; Shake(); } else resetArmed = true; });
            var sback = new NoticeItem(paper, "返回", P(sy - 0.01f), SH, 0.1f, () => { resetArmed = false; ShowMain(); }, 1.6f);
            setItems.Add(sback); menu.items.Add(sback);
            // 关卡列表：同一张纸上替换选项（没解锁的被涂黑）。
            // 11-14：加到 9 个区后一列排不下 —— 排两列：左列 玩法、1～4 区，右列 5～9 区；行距 5cm（比原来的 4.2cm 松）。
            // 右列压着右半边的涂鸦（爪印、“拆！”），选关页显示时把它们藏掉（见 Show）
            for (int i = 0; i < Level.All.Length; i++)
            {
                int li = i; int col = i < 5 ? 0 : 1, row = i < 5 ? i : i - 5; float y = 0.1f - row * 0.05f, x = XL(y) + col * 0.225f;
                var it = new NoticeItem(paper, i == 0 ? "玩法" : $"第 {i} 区", new Vector2(x, y), 0.03f, 0.11f, () => Choose(li), 1.6f);
                it.enabled = i <= Mathf.Max(1, Progress.Unlocked);
                levelItems.Add(it); menu.items.Add(it);
                // 11-14 用户：没解锁的关不要用实心黑块盖住，用涂鸦 —— 字还在（灰色），上面一团乱线划掉，每行的角度和长短不一样
                if (!it.enabled) levelDeco.Add(paper.Img("doodle_scribble", new Vector2(x + 0.05f, y + 0.001f), 0.03f + (i % 3) * 0.003f, 1.6f + (i % 2 == 0 ? 3 : -4), new Color(1, 1, 1, 0.95f), 0.125f + (i % 2) * 0.012f).gameObject);
                for (int st = 0; st < Progress.Stars(i); st++) levelDeco.Add(paper.Img("doodle_star", new Vector2(x + 0.122f + st * 0.023f, y), 0.021f, st * 12 - 10).gameObject);
            }
            var back = new NoticeItem(paper, "返回", P(-0.19f), 0.03f, 0.1f, ShowMain, 1.6f);
            levelItems.Add(back); menu.items.Add(back);
            MonsterMR.Mark("标题.纸和选项");
            // ── 纸顶上两条胶带 ──
            var deco = NewSheet("Deco", 3200);
            deco.Img("tape_a", new Vector2(-0.3f, 0.3f), 0.038f, 35);
            deco.Img("tape_c", new Vector2(0.28f, 0.3f), 0.038f, -30);
            // ── 涂鸦层（右半边：爪印、大字、手写提示；左边留给选项）──
            var graf = NewSheet("Graffiti", 3300);
            rightDeco.Add(graf.Img("claw_print", new Vector2(0.17f, 0.0f), 0.17f, 22, new Color(1, 1, 1, 0.85f)).gameObject);
            graf.Img("doodle_scribble", new Vector2(XL(subY) + 0.105f, subY), 0.045f, 2, new Color(1, 1, 1, 0.9f), 0.24f);   // 青色乱线划掉公文标题
            rightDeco.Add(graf.Img("doodle_waves", new Vector2(0.3f, -0.1f), 0.06f, -8).gameObject);
            graf.Img("doodle_x", new Vector2(0.22f, -0.33f), 0.07f, 8);   // 条码上打叉
            graf.Img("doodle_eye", new Vector2(-0.37f, 0.14f), 0.05f, -12);
            graf.Img("doodle_bang", new Vector2(0.37f, 0.16f), 0.09f, 14);
            titleShout = new Shout(graf, "拆！", new Vector2(0.21f, 0.13f), 0.17f, Ink.Pink, 11, 0.24f); shouts.Add(titleShout);
            mainDeco.Add(graf.Img("doodle_circle", new Vector2(XL(0.105f) + 0.08f, 0.105f), 0.075f, 0, new Color(1, 1, 1, 0.55f), 0.3f).gameObject);
            graf.Img("doodle_jelly", new Vector2(0.34f, -0.26f), 0.11f, 12);
            // 桌子状态（第一行，没认出时用粉色提醒）；其余三行手写提示往右挪，给左边五个选项让位
            // 右列手写提示：左边的菜单往里挪了 5cm，提示跟着收窄，夹在菜单和右版心线之间（宽 25cm，超了自动缩小），不顶到纸的右边
            const float NoteX = 0.105f, NoteW = 0.25f;
            roomNote = graf.Fit(graf.Text(RoomNote(), Ink.Face.Note, new Vector2(NoteX, -0.13f), 0.023f, Ink.Pink, -2, TextAlignmentOptions.Center), NoteW);
            mainDeco.Add(roomNote.gameObject);
            mainDeco.Add(graf.Fit(graf.Text("伸手点，或看着捏一下", Ink.Face.Note, new Vector2(NoteX, -0.175f), 0.024f, Ink.Cyan, 2, TextAlignmentOptions.Center), NoteW).gameObject);
            mainDeco.Add(graf.Fit(graf.Text("站着坐着都能玩，面前留块空地", Ink.Face.Note, new Vector2(NoteX, -0.215f), 0.024f, Ink.Cyan, 3, TextAlignmentOptions.Center), NoteW).gameObject);
            mainDeco.Add(graf.Fit(graf.Text("捏城市近角的暂停钮：暂停", Ink.Face.Note, new Vector2(NoteX, -0.255f), 0.022f, Ink.Cyan, 2, TextAlignmentOptions.Center), NoteW).gameObject);
            graf.Img("doodle_bolt", new Vector2(0.36f, 0.27f), 0.07f, 14);
            graf.Img("doodle_tally", new Vector2(-0.37f, -0.1f), 0.05f, -8);
            graf.Img("tape_b", new Vector2(0.2f, -0.355f), 0.035f, -12);
            glitches.Add(new Glitch(graf, new Vector2(-0.06f, -0.05f), new Vector2(0.5f, 0.6f)));
            MonsterMR.Mark("标题.涂鸦层");
            ShowMain(); Relabel();
            MonsterMR.Mark("标题.排字");
        }
        static void Set(NoticeItem it, bool on) { it.text.gameObject.SetActive(on); it.spray.gameObject.SetActive(on); it.arrow.gameObject.SetActive(on); it.claw.gameObject.SetActive(on); }
        void Show(List<NoticeItem> on, bool levelsDeco, bool mainNotes)
        {
            foreach (var l in new[] { mainItems, levelItems, setItems }) foreach (var i in l) Set(i, l == on);
            foreach (var d in levelDeco) d.SetActive(levelsDeco); foreach (var d in mainDeco) d.SetActive(mainNotes);
            foreach (var d in rightDeco) d.SetActive(!levelsDeco); titleShout?.SetActive(!levelsDeco);   // 选关页：右半边让给第二列
        }
        public void ShowMain() => Show(mainItems, false, true);   // 公开：编辑器截图验收要切页
        public void ShowLevels() { Show(levelItems, true, false); Shake(); }
        public void ShowSettings() { Show(setItems, false, false); Shake(); }
        void Choose(int lv) { if (chosen < 0) { chosen = lv; Shake(); } }

        // ── 真实桌子提示 ──
        TextMeshPro roomNote; int roomVer = -1;
        static bool TableFound => RoomTable.Instance && RoomTable.Instance.Found;
        static string RoomScanLabel() => TableFound ? "重新扫描房间" : "扫描房间";
        static string RoomNote()
        {
            var rt = RoomTable.Instance;
            if (rt && rt.Scanning) return "扫完房间回来就好";
            if (rt && !rt.Done) return "正在找你的桌子";
            return TableFound ? "认出你的桌子了，城市摆在上面" : "没认出你的桌子，先扫描房间吧";
        }
        void UpdateRoomNote()
        {
            var rt = RoomTable.Instance; int v = rt ? rt.Version * 4 + (rt.Scanning ? 1 : 0) + (rt.Done ? 2 : 0) : -2;
            if (v == roomVer || roomNote == null) return;
            roomVer = v;
            foreach (var s in sheets) foreach (var l in s.labels) if (l.t == roomNote) l.zh = RoomNote();
            roomNote.color = TableFound ? Ink.Cyan : Ink.Pink;
            Relabel();
        }

        void Quit()
        {
            Shake();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        protected override void Relabel()
        {
            foreach (var (it, label) in dyn) foreach (var s in sheets) foreach (var l in s.labels) if (l.t == it.text) l.zh = label();
            base.Relabel();
            if (sign) sign.SetLanguage(Loc.En);
        }
        protected override void OnDestroy() { base.OnDestroy(); if (sign) Destroy(sign.gameObject); }
        // 页脚“标志 + Tripo XR 出品”：footCx = 这一组的左边缘（和菜单文字左对齐）；标志在前、文字跟在后面
        const float FootMarkH = 0.033f, FootGap = 0.009f, FootF = 0.897f;   // FootF：页脚在纸高的什么位置 —— 纸上底部两条横线（84.4% 和 95.0%，从贴图量的）的正中（11-17 用户）
        Transform footMark; TextMeshPro footText; Func<float, float> footY; float footCx; string footKey;
        void LayoutFoot()
        {
            if (!footText || !footMark) return;
            string key = footText.text + footText.fontSize; if (key == footKey) return; footKey = key;
            float tw = footText.GetPreferredValues(footText.text, 9999, 0).x * 0.01f, mw = footMark.localScale.x;
            float left = footCx;
            float mx = left + mw / 2, tx = left + mw + FootGap;
            footMark.localPosition = new Vector3(mx, footY(mx) + 0.001f, footMark.localPosition.z);
            footText.transform.localPosition = new Vector3(tx, footY(tx + tw / 2), footText.transform.localPosition.z);
        }
        protected override void Update()
        {
            // 招牌通电到 60% 时纸才弹出来（先看招牌，再看菜单）
            if (delay > 90 && sign && sign.Lit > 0.6f) delay = t + 0.01f;
            base.Update();
            LayoutFoot();
            UpdateRoomNote();
        }
    }

    // ═══════════ 关前简报 ═══════════
    public class BriefCard : NoticeScreen
    {
        public int level; public Action onDone; public bool ready = true;   // ready=false：城市还在后台生成，先不让开始
        TextMeshPro hint; bool wasReady = true;
        void Start()
        {
            Setup("BriefCard", NearD, EyeDrop, 0.66f);
            var card = StoryText.Briefs[Mathf.Clamp(level, 0, StoryText.Briefs.Length - 1)];
            var paper = NewSheet("Paper", 3100);
            // 11-11 用户：纸面左边的字离纸边太近 —— 同标题菜单，按纸上印的版心排：字从左版心线往里起，页眉放在黑条正中（原来压在粉线上），
            // 正文在右版心线之前折行（原来折行宽度 30cm，比版心宽 5cm，一直排到纸的右边）
            var pc = new Vector2(0, -0.03f); const float PH = 0.5f, PW = PH * 1175f / 1600f, PR = -2f;
            paper.Img("paper_notice", pc, PH, PR, null, PW);
            float XL(float y) => Sheet.PaperX(pc, PW, PR, Sheet.TextL + 0.008f, y);
            float barY = Sheet.PaperY(pc, PH, PR, Sheet.BarMid, XL(0.17f));
            paper.Text("霓虹动力集团 · 夜间通告", Ink.Face.Corp, new Vector2(XL(barY), barY), 0.016f, Ink.Paper, -2);
            paper.Fit(paper.Text(card.title, Ink.Face.Corp, new Vector2(XL(0.095f), 0.095f), 0.028f, Ink.Black, -2), (Sheet.RuleR - Sheet.TextL - 0.02f) * PW);
            // 正文和下面的手写提示按“实际排出来几行”往下接（英文比中文多一两行，原来固定位置会叠在一起）
            float colW = (Sheet.RuleR - Sheet.TextL - 0.02f) * PW;
            var bodyT = paper.Text(card.text, Ink.Face.Corp, new Vector2(XL(0.022f), 0.022f), 0.017f, Ink.Gray, -2, TextAlignmentOptions.Left, colW);
            float bodyH = bodyT.GetPreferredValues(bodyT.text, colW * 100, 0).y * 0.01f, bodyTop = 0.058f, bodyBot = bodyTop - bodyH;
            bodyT.transform.localPosition = new Vector3(XL(bodyTop - bodyH / 2), bodyTop - bodyH / 2, bodyT.transform.localPosition.z);
            // 这一关的要点（11-11：电站护甲 / 重启等新规则要让玩家开打前知道）：小潮在公文上的手写批注，一两行
            var tipText = Level.All[Mathf.Clamp(level, 0, Level.All.Length - 1)].tip;
            var graf = NewSheet("Graffiti", 3200);
            shouts.Add(new Shout(graf, "上！", new Vector2(0.12f, -0.135f), 0.085f, Ink.Pink, -12, 0.11f));
            if (!string.IsNullOrEmpty(tipText))
            {
                float tipSize = Loc.En ? 0.016f : 0.021f, tipW = (Sheet.RuleR - Sheet.TextL - 0.03f) * PW;
                var tipT = graf.Text(tipText, Ink.Face.Note, new Vector2(XL(-0.05f) + 0.004f, -0.05f), tipSize, Ink.Pink, -3, TextAlignmentOptions.Left, tipW);
                float tipH = tipT.GetPreferredValues(tipT.text, tipW * 100, 0).y * 0.01f, tipC = bodyBot - 0.012f - tipH / 2;
                tipT.transform.localPosition = new Vector3(XL(tipC) + 0.004f, tipC, tipT.transform.localPosition.z);
            }
            graf.Img("doodle_jelly2", new Vector2(-0.105f, -0.148f), 0.058f, -8);
            // 提示这行字原来正好压在下版心线和钢印上、右端伸到条码外面：往上挪到版心里，限宽 21cm
            hint = graf.Fit(graf.Text("点一下纸，或捏一下开始", Ink.Face.Note, new Vector2(-0.085f, -0.178f), 0.022f, Ink.Cyan, 4), 0.21f);
            wasReady = !ready;   // 第一帧按当前状态刷一次提示文字
            graf.Img("doodle_arrow", new Vector2(0.1f, -0.21f), 0.025f, 4);
            graf.Img("tape_a", new Vector2(-0.16f, 0.23f), 0.03f, 25);
            glitches.Add(new Glitch(graf, new Vector2(0, -0.03f), new Vector2(0.36f, 0.5f), 4));
            Relabel();
        }
        protected override void Update()
        {
            base.Update();
            hint.gameObject.SetActive(t > 0.8f);
            if (ready != wasReady) { wasReady = ready; foreach (var sh in sheets) foreach (var l in sh.labels) if (l.t == hint) { l.zh = ready ? "点一下纸，或捏一下开始" : "城市生成中…"; l.Apply(); } }
            if (t > 0.8f && ready && (ptr.down || menu.Poked)) { MonsterAudio.Play("ui_press"); MonsterAudio.Music("music_game"); MonsterAudio.Ambience(true); onDone?.Invoke(); Destroy(this); }
        }
    }

    // ═══════════ 结算：被拍了爪印的通缉令 ═══════════
    public class ResultScreen : NoticeScreen
    {
        public MonsterGame game; public int level; public Action<int> onLevel; public Action onTitle;
        readonly List<(Transform t, Vector3 s)> pops = new();
        void Start()
        {
            Setup("ResultScreen", NearD, EyeDrop, 0.58f);
            MonsterAudio.Music("music_title"); MonsterAudio.Ambience(false);
            bool win = game.result == "win"; int rating = game.Rating();
            if (win) { Progress.Unlocked = level + 1; Progress.SetStars(level, rating); }
            Card card = level == 0 ? new Card("学会了！", "玩法都会了，去真的城市吧。") : win ? new Card("拆光了！", "封印塔一座不剩。") : StoryText.Lose(game.result);
            var paper = NewSheet("Paper", 3100);
            // 11-11 用户：纸面左边的字离纸边太近 —— 同标题菜单，按纸上印的版心排：“通缉令”、照片、选项都从左版心线往里起
            var pc = new Vector2(-0.02f, -0.02f); const float PH = 0.64f, PW = PH * 1175f / 1600f, PR = -1.5f;
            paper.Img("paper_notice", pc, PH, PR, null, PW);
            float XL(float y) => Sheet.PaperX(pc, PW, PR, Sheet.TextL + 0.004f, y);
            float barY = Sheet.PaperY(pc, PH, PR, Sheet.BarMid, XL(0.235f));
            paper.Text("通缉令", Ink.Face.Corp, new Vector2(XL(barY), barY), 0.045f, Ink.Paper, -1.5f);
            // 副标题接在“通缉令 / WANTED”实际宽度之后，再限宽（英文更长）
            float wantedW = 0; foreach (var l in paper.labels) if (l.zh == "通缉令") { l.Apply(); wantedW = l.t.GetPreferredValues(l.t.text, 9999, 0).x * 0.01f; }
            float subX = XL(barY) + wantedW + 0.014f;
            paper.Fit(paper.Text("霓虹动力集团 · 安全部", Ink.Face.Corp, new Vector2(subX, barY - 0.012f), 0.013f, new Color(0.75f, 0.75f, 0.78f), -1.5f), Mathf.Max(0.05f, Sheet.PaperX(pc, PW, PR, 0.93f, barY) - subX));
            const float MugH = 0.205f, MugW = MugH * 725f / 900f;
            float mugX = XL(0.03f) + MugW / 2;   // 照片左沿对齐版心
            paper.Img("mugshot", new Vector2(mugX, 0.03f), MugH, -3);
            const float ColX = 0.012f;   // 右列（代号、战绩、星）：照片右沿再留 2cm
            paper.Img("tape_a", new Vector2(mugX - 0.08f, 0.14f), 0.026f, 30); paper.Img("tape_c", new Vector2(mugX + 0.09f, 0.14f), 0.026f, -25);
            paper.Text("代号：深潮", Ink.Face.Corp, new Vector2(ColX, 0.1f), 0.018f, Ink.Black, -1.5f);
            paper.Text(Loc.F("拆毁楼层：{0}", game.destroyed), Ink.Face.Corp, new Vector2(ColX, 0.065f), 0.014f, Ink.Gray, -1.5f);
            paper.Text(Loc.F("最长连锁：{0}", game.maxChain), Ink.Face.Corp, new Vector2(ColX, 0.04f), 0.014f, Ink.Gray, -1.5f);
            paper.Text(Loc.F("击毁载具：{0}", game.kills), Ink.Face.Corp, new Vector2(ColX, 0.015f), 0.014f, Ink.Gray, -1.5f);
            paper.Rect(new Vector2(ColX + 0.058f, -0.01f), new Vector2(0.116f, 0.016f), Ink.Black, -2);   // 涂黑的“悬赏金额”
            paper.Img("doodle_circle", new Vector2(ColX + 0.095f, 0.067f), 0.04f, 0, null, 0.07f);
            // 星：手画黄色星星，拿到的弹出来，没拿到的是灰色铅笔线
            for (int i = 0; i < 3; i++)
            {
                bool on = win && i < rating;
                var st = paper.Img("doodle_star", new Vector2(ColX + 0.028f + i * 0.052f, -0.06f), 0.05f, i * 9 - 9, on ? Color.white : new Color(0.35f, 0.35f, 0.38f, 0.5f));
                pops.Add((st, st.localScale)); st.localScale = Vector3.zero;
            }
            // 选项
            var row = new List<(string, Action)>();
            // 11-10 用户：玩法（教学）结束后还有“再来一次”，玩家会误点 —— 玩法只给“开始第一夜 / 回到标题”
            if (level == 0) row.Add(("开始第一区", () => onLevel?.Invoke(1)));
            else
            {
                if (win && level + 1 < Level.All.Length) row.Add(("下一区", () => onLevel?.Invoke(level + 1)));
                row.Add(("再来一次", () => onLevel?.Invoke(level)));
            }
            row.Add(("回到标题", () => onTitle?.Invoke()));
            for (int i = 0; i < row.Count; i++) menu.items.Add(new NoticeItem(paper, row[i].Item1, new Vector2(XL(-0.13f - i * 0.05f) + 0.004f, -0.13f - i * 0.05f), 0.032f, 0.15f, row[i].Item2, -1.5f));
            // 涂鸦层：爪印拍在照片上、大字喊一句、手写一句剧情
            var graf = NewSheet("Graffiti", 3300);
            if (win) { var cp = graf.Img("claw_print", new Vector2(mugX - 0.005f, -0.025f), 0.09f, -32, new Color(1, 1, 1, 0.92f)); pops.Add((cp, cp.localScale)); cp.localScale = Vector3.zero; }
            else graf.Img("doodle_x", new Vector2(mugX, 0.03f), 0.16f, 5, new Color(1, 1, 1, 0.85f));
            shouts.Add(new Shout(graf, card.title, new Vector2(0.15f, 0.262f), 0.07f, win ? Ink.Pink : Ink.Cyan, -9, 0.2f));   // 抬到页眉黑条上沿之外，不压页眉里的字
            graf.Text(card.text, Ink.Face.Note, new Vector2(ColX + 0.008f, -0.15f), 0.02f, win ? Ink.Pink : Ink.Cyan, 2, TextAlignmentOptions.Left, 0.168f);   // 折行宽度收到右版心线以内
            graf.Img("doodle_jelly", new Vector2(0.24f, -0.27f), 0.09f, 10);
            if (win && rating == 3) graf.Img("doodle_crown", new Vector2(mugX, 0.17f), 0.06f, -10);
            glitches.Add(new Glitch(graf, new Vector2(-0.02f, -0.02f), new Vector2(0.46f, 0.62f)));
            Relabel();
        }
        protected override void Update()
        {
            base.Update();
            const float c = 1.7f;
            for (int i = 0; i < pops.Count; i++)
            {
                float k = Mathf.Clamp01((t - 0.55f - i * 0.22f) / 0.28f);
                pops[i].t.localScale = pops[i].s * Mathf.Max(0, 1 + (c + 1) * Mathf.Pow(k - 1, 3) + c * Mathf.Pow(k - 1, 2));
            }
        }
    }
}
