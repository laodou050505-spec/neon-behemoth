// 局内菜单（11-08 全流程体检补的）：之前一局开始后没法暂停 / 重来 / 回标题，切到系统菜单时封印倒计时还在走。
// 打开：抱拳（一只手握拳、另一只手包住它，或两拳相碰）在胸前停 0.6 秒（拳头上方先画一圈进度，误触看得见，手一分开就取消）；
// 应用失焦（按了系统键 / 摘头显）也会自动打开。菜单是一小张告示纸，打开时定在手心上方不动（方便另一只手去点），
// 游戏暂停；“继续 / 重来 / 回到标题”可以伸手点，也可以看着捏一下。
using UnityEngine;

namespace Monster.Game
{
    public class PalmMenu : MonoBehaviour
    {
        public MonsterInput input; public GameView view; public MonsterGame game; public Camera cam; public int level;
        const float Hold = 0.6f;
        float hold; bool open, needReset; Transform page, ring; Sheet ringSheet; NoticeMenu menu; readonly Pointer ptr = new(); float openT;

        void Start()
        {
            if (!cam) cam = Camera.main;
            ptr.input = input; ptr.cam = cam;
            // 进度圈（手心上）
            ring = new GameObject("PalmRing").transform;
            ringSheet = new Sheet(ring, "Ring", 3420);
            ringSheet.Img("doodle_circle", Vector2.zero, 1f, 0, new Color(0.4f, 1f, 0.95f, 0.9f));
            ringSheet.Text("暂停", Ink.Face.Note, new Vector2(-0.32f, -0.72f), 0.42f, Ink.Cyan, 0);
            ring.gameObject.SetActive(false);
            Loc.Changed += Relabel;
        }
        void OnDestroy() { Loc.Changed -= Relabel; if (ring) Destroy(ring.gameObject); if (page) Destroy(page.gameObject); }
        Sheet paperS, grafS;
        void Relabel() { ringSheet?.Relabel(); paperS?.Relabel(); grafS?.Relabel(); }

        // 按了系统键 / 摘下头显：自动暂停并打开菜单
        void OnApplicationFocus(bool focus) { if (!focus && CanOpen()) Open(); }

        bool CanOpen() => !open && view && !view.paused && game != null && game.result == null;

        void Update()
        {
            if (!cam) return;
            var head = cam.transform.position;
            if (open) { Tick(); return; }
            // 11-12：手势（手心朝自己 / 抱拳）都不可靠 —— 不再用手势打开，改由城市近角的实物暂停钮（PauseButton）打开
            if (ring.gameObject.activeSelf) ring.gameObject.SetActive(false);
        }
        public void OpenFromButton() { if (CanOpen()) Open(); }

        void Open() => Open(cam.transform.position + Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized * 0.45f + Vector3.down * 0.22f);
        void Open(Vector3 near)
        {
            open = true; openT = 0; hold = 0; view.paused = true;
            MonsterAudio.Play("ui_page");
            var head = cam.transform.position;
            // 定在手心上方、离眼 0.42m 左右、低于视线（胸前）：另一只手伸过来正好点到
            var p = near + Vector3.up * 0.1f; var dir = p - head; float d = dir.magnitude;
            p = head + dir / Mathf.Max(1e-3f, d) * Mathf.Clamp(d, 0.36f, 0.5f);
            p.y = Mathf.Min(p.y, head.y - 0.08f);
            if (MonsterMR.CityTopY > 0) p.y = Mathf.Max(p.y, Mathf.Min(MonsterMR.CityTopY + 0.13f, head.y + 0.05f));   // 菜单纸（约 0.23m 高）下沿在楼顶之上
            page = new GameObject("PalmMenu").transform;
            page.position = p;
            page.rotation = Quaternion.LookRotation(p - head, Vector3.up);
            page.localScale = Vector3.one * 0.001f;
            var paper = new Sheet(page, "Paper", 3430);
            paper.Img("paper_notice", new Vector2(0, -0.02f), 0.42f, -2);
            paper.Text("暂停", Ink.Face.Corp, new Vector2(-0.11f, 0.152f), 0.024f, Ink.Paper, -2);
            menu = new NoticeMenu { page = page };
            menu.items.Add(new NoticeItem(paper, "继续", new Vector2(-0.1f, 0.05f), 0.034f, 0.12f, Close, -2));
            menu.items.Add(new NoticeItem(paper, "重来", new Vector2(-0.1f, -0.025f), 0.034f, 0.12f, () => MonsterMR.Restart(level), -2));
            menu.items.Add(new NoticeItem(paper, "回到标题", new Vector2(-0.1f, -0.1f), 0.034f, 0.16f, () => MonsterMR.Restart(-1), -2));
            var graf = new Sheet(page, "Graffiti", 3440);
            graf.Img("doodle_jelly2", new Vector2(0.11f, -0.15f), 0.07f, 10);
            paperS = paper; grafS = graf; paper.Relabel(); graf.Relabel();
            MonsterAudio.Ambience(false);
        }

        void Tick()
        {
            float dt = Time.deltaTime; openT += dt;
            float x = Mathf.Clamp01(openT / 0.3f); const float c = 1.7f;
            float e = Mathf.Max(0.001f, 1 + (c + 1) * Mathf.Pow(x - 1, 3) + c * Mathf.Pow(x - 1, 2));
            page.localScale = Vector3.one * 0.55f * e;
            ptr.Update();
            if (openT > 0.35f) menu.Tick(dt, ptr);
        }

        void Close()
        {
            open = false; needReset = true; view.paused = false;
            MonsterAudio.Ambience(true);
            if (page) Destroy(page.gameObject);
        }
    }
}
