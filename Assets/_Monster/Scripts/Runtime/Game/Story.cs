// 剧情（原型 src/logic/story.js）：开场 4 张卡 → 每关开局一张简报 → 最后一关打完一张结局。
// 世界观（自创，不用任何真实城市 / 公司名）：你是“深潮”，海湾最古老的守护者；霓虹动力集团用封印塔把你钉在海底，
// 抽走你的力量点亮霓湾市。今夜风暴震裂了封印，市民已撤离，街上只剩集团的无人军团（城里没有人）。
namespace Monster.Game
{
    public class Card { public string title, text; public bool shake; public Card(string title, string text, bool shake = false) { this.title = title; this.text = text; this.shake = shake; } }

    public static class StoryText
    {
        public static readonly Card[] Intro =
        {
            new("三十年前", "你是深潮，海湾最古老的守护者。你在海底沉睡，潮水随你的呼吸涨落。"),
            new("霓虹的代价", "霓虹动力集团在你沉睡的海湾上建起了霓湾市。他们立起封印塔，把你钉在海底，抽走你的力量，点亮整座城。"),
            new("今夜", "一场风暴震裂了封印，你醒了。全城警报响起，市民已经撤离，街上只剩集团的无人军团。", true),
            new("夺回力量", "拆掉封印塔，夺回被抽走的力量。动作要快：他们正在加固封印，封印走满，你会被永远钉回海底。"),
        };
        // 每关开局简报（下标对应 Level.All）
        public static readonly Card[] Briefs =
        {
            new("巨兽觉醒", "封印裂开了一道缝。先活动一下爪子，想起来自己是怎么拆城的。"),
            new("第 1 区 · 黄昏港湾", "你从港湾爬上岸。离海最近的两座封印塔，先拔掉它们。"),
            new("第 2 区 · 霓虹市区", "市中心的霓虹全靠你的力量亮着。三座塔分散在城里各处，走起来。"),
            new("第 3 区 · 入夜大桥", "集团派出了维修车，想把你拆掉的电站修回去。别让它们得逞。"),
            new("第 4 区 · 码头暴雨", "暴雨里，集团给每座塔接上了两座电站。下雨时火也烧得慢。"),
            new("第 5 区 · 空袭警报", "集团动用了战斗机和备用电站。主站停机 10 秒，备用的就会顶上。"),
            new("第 6 区 · 熔炉工业区", "给封印塔供电的工业区。这里的电站加了厚甲，坏了也修得飞快。"),
            new("第 7 区 · 铁幕防线", "集团在总部外围拉起了防线。坦克和导弹车已经在街上等你了。"),
            new("第 8 区 · 总部前庭", "总部就在眼前。集团放出了它们的王牌：一台持盾的机甲。"),
            new("第 9 区 · 决战之夜", "集团总部的五座主封印塔，两台机甲守着。拆掉它们，三十年的力量就全部回到你身上。"),
        };
        public static readonly Card[] Ending =
        {
            // 结局的四句旁白（见 Ending.cs：熄灯 → 力量回来 → 涨潮 → 天亮）
            new("", "最后一座塔倒下，霓虹一盏一盏熄灭。"),
            new("", "被抽走三十年的力量，回到你身上。"),
            new("", "潮水漫上来，又随着你的呼吸涨落。"),
            new("", "天亮了。这一次，城市要学会自己发光。"),
        };
        public static Card Lose(string cause) => cause == "hp"
            ? new Card("没撑住", "眼皮越来越沉……无人军团重新拉起了封印。")
            : new Card("核打击", "集团按下了按钮。白光吞没了霓湾，也吞没了你。");
    }

    /// <summary>一段剧情：一组卡片，一张一张打字显示（每秒 26 字）；点一下先显示完整，再点下一张</summary>
    public class Story
    {
        public readonly Card[] cards; public int i; public float t; public bool done;
        readonly System.Action onDone;
        public Story(Card[] cards, System.Action onDone = null) { this.cards = cards; this.onDone = onDone; }
        public Card Current => cards[UnityEngine.Mathf.Min(i, cards.Length - 1)];
        public int Shown => UnityEngine.Mathf.Min(Current.text.Length, UnityEngine.Mathf.FloorToInt(t * 26));
        public bool Typing => Shown < Current.text.Length;
        public void Update(float dt) => t += dt;
        public void Next()
        {
            if (done) return;
            if (Typing) { t = 99; return; }
            i++; t = 0;
            if (i >= cards.Length) Finish();
        }
        public void Skip() { if (!done) Finish(); }
        void Finish() { done = true; onDone?.Invoke(); }
    }
}
