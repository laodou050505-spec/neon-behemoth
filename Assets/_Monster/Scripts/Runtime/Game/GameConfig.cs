// 玩法常量（单位：米 / 秒），和 Three.js 原型 src/logic/config.js 一致
using UnityEngine;

namespace Monster.Game
{
    public static class Cfg
    {
        // 手
        public const float SmashV = 0.45f;       // 手速超过这个碰到楼 = 拍（11-08 真机：0.9 太难触发）
        public const float SwatV = 0.6f;         // 手速超过这个碰到飞行物 = 拍掉
        public const float GrabR = 0.075f;        // 捏合时离载具多近能抓住
        public const float ThrowMin = 0.5f;      // 松手速度超过这个算扔（否则是放下）
        public const float ThrowSpeed = 2.2f;    // 扔出去的初速（眼睛辅助瞄准）
        public const float BreathMouth = 0.24f;
        public const float WaveMuzzle = 0.04f, WaveAssist = 0f, NukeFall = 6.5f;   // 能量波：起点在两掌心中点前面多远 / 眼睛辅助瞄准的夹角（度）
        public const float WinSettle = 1.0f, WinShow = 5.4f;   // 胜利：最后一座塔倒下后等 1 秒（倒塌放完）才开始胜利过程；过程 5.4 秒（冲击波 → 广播 → 追兵撤离），然后出结算
        public const float WaveGap = 0.15f, WaveReach = 0.22f, WaveCharge = 0.7f;   // 能量波：两掌心多近算并拢 / 手离头多远 / 蓄力时间  // 双手离头多近算“嘴前张开”
        // 建筑
        public const float ChainDelay = 0.18f;   // 倒下的楼砸到邻楼的延迟
        public const float FireSpreadT = 2.4f;   // 着火多久开始蔓延
        public const float FireSpreadRate = 0.35f;
        public const float FireWeakenT = 4.5f;   // 烧多久塌掉一层
        public const float FireRange = 0.13f;    // 相邻判定距离
        // 封印塔 / 电站
        public const float ScanTime = 0.9f, ScanShow = 6f, BackupDelay = 10f, RepairTime = 4f, ShieldBounceDmg = 3f;
        // 玩家
        public const float HpMax = 100, HealVehicle = 3, HealTower = 15, RageMax = 100, RagePerMod = 2.4f, RagePerVehicle = 6, RageChainMult = 1.6f;   // 11-14：怒气攒得慢一点（原来 3 / 7 / 2）
        public const float SealRebate = 0.05f;   // 每拆一座塔，核打击倒计时退回时限的 5%（原来 8%：塔多的关等于白送半分钟）
        public const float BreathT = 4.5f, BreathRange = 3.2f, BreathTick = 0.35f;   // 11-12 用户：大招别太短（1.8 → 4.5 秒），而且要能跟着手扫
        public const float HeadR = 0.12f, TorsoR = 0.15f, TorsoLen = 0.5f;
        // 通缉
        public static readonly float[] HeatStars = { 0, 6, 24, 52, 90, 140 };
        public const float HeatMod = 1, HeatVehicle = 2, HeatBreath = 18, WantedDecayT = 8;
        // 物理
        public const float Gravity = -2.6f, BlastVehicle = 0.07f;
    }

    /// <summary>关卡：一套城市，每关只换塔数、供电、开局星级、时段、特殊规则</summary>
    public class Level
    {
        public int id; public string name, time = "dusk", tip;
        public bool tutorial, near, repair, backup;
        public int towers = 2, power = 1, stars, maxStars = 5;
        public float seal = 120;
        // 11-11 用户：一上来把几座电站全拍掉，游戏就没难度了 —— 电站加两条规则：
        //  ① 护甲：armor 格。徒手拍 / 压一下掉 1 格；扔车、扔楼砸中掉 2 格；楼倒下来压到、能量波、弹回去的导弹直接摧毁。
        //  ② 重启：电站被毁后 restart 秒自动重建（它的塔还在的话），塔的护盾跟着回来 —— 不能先把电站扫光再慢慢拆塔，
        //     必须“断电 → 趁护盾没回来拆塔”，一座一座来；越往后重启越快、一座塔的电站越多、电站离塔越远。
        public int armor = 2; public float restart = 30;
        /// <summary>这一关受到的伤害乘几（11-12 难度模拟：原来所有失败都是血量耗尽、没有一局是封印超时，新手第 2 区就过不去 → 每关单独定）</summary>
        // 11-12 难度模拟（Tools/editor_checks/sim.cs：机器人按“断电 → 拆塔”打，新手 / 一般 / 熟练三档只差出手间隔和挡弹率）后的定值：
        //   伤害系数 0.55 → 0.22～0.27（越往后敌人越多，单发伤害要压下来）；重启 30～36 秒（太紧的话慢的玩家会陷入“刚拆完电站又重启”的死循环）；
        //   封印时限 150 → 240 秒（原来没有一局是超时输的，血的问题解决后慢的玩家开始超时）；第 4 区每塔两座电站，护甲降回 2。
        public float dmg = 1;
        /// <summary>有真桌子 / 空间够时，在玩家左右再加几件虚拟家具（0 = 只有正面一张桌；1 = 左边加柜子；2 = 左柜 + 右边小桌）</summary>
        public int sides = 0;
        /// <summary>机甲：通缉到 mechStars 星时空降第一台；mechs = 一共几台（第二台再高一星、至少隔 12 秒）</summary>
        public int mechStars = 5, mechs = 1;
        public static readonly Level[] All =
        {
            new Level { id = 0, name = "巨兽觉醒", tutorial = true, maxStars = 2, towers = 2, power = 1, seal = 9999, stars = 0, time = "dusk", near = true, armor = 2, restart = 45, tip = "跟着提示一步一步来" },   // 玩法关也有护盾和重启（教这两条规则），重启给得很宽
            new Level { id = 1, name = "黄昏港湾", maxStars = 3, towers = 2, power = 1, seal = 65, stars = 1, time = "dusk", armor = 2, restart = 30, dmg = 0.8f, sides = 0, tip = "电站要拍两下才碎；30 秒后会重启，趁护盾没回来拆塔" },
            new Level { id = 2, name = "霓虹市区", maxStars = 3, towers = 3, power = 1, seal = 75, stars = 0, time = "dusk", armor = 2, restart = 32, dmg = 0.5f, sides = 2, tip = "握拳砸、双手砸桌面、合掌拍飞机都行；左右两边各多了一片城区" },
            new Level { id = 3, name = "入夜大桥", maxStars = 3, towers = 3, power = 1, seal = 80, stars = 1, time = "night", repair = true, armor = 2, restart = 30, dmg = 0.48f, sides = 2, tip = "黄色维修车会去修电站：先拍掉它" },
            // 11-14 用户通关后：关卡偏少、后面的关可以更难、核打击的时间卡得更紧 —— 从 6 个区加到 9 个区（6、7、8 是新的，原来的决战挪到第 9 区），
            // 第 4 区起核打击时限收紧（原来 170 / 170 / 195）。新区各有一条新规则：6 加固电站（四格护甲、重启更快）、7 开局就是三星（坦克和导弹车已经在街上）、
            // 8 机甲四星就空降、9 两台机甲。数值先按 editor_checks/sim.cs 的模拟定（结果见项目记忆），最后看真机
            new Level { id = 4, name = "码头暴雨", maxStars = 4, towers = 4, power = 2, seal = 95, stars = 1, time = "rain", repair = true, armor = 2, restart = 36, dmg = 0.36f, sides = 2, tip = "一座塔两座电站，要一起断电；扔车砸电站更快" },
            new Level { id = 5, name = "空袭警报", maxStars = 4, towers = 4, power = 1, seal = 92, stars = 2, time = "night", repair = true, backup = true, armor = 3, restart = 34, dmg = 0.4f, sides = 2, tip = "虚线连着备用电站：主站停 10 秒它就顶上" },
            new Level { id = 6, name = "熔炉工业区", maxStars = 4, towers = 4, power = 1, seal = 88, stars = 2, time = "dusk", repair = true, armor = 4, restart = 26, dmg = 0.46f, sides = 2, tip = "电站加厚到四格，26 秒就重启：扔车扔楼去砸" },
            new Level { id = 7, name = "铁幕防线", maxStars = 4, towers = 4, power = 2, seal = 95, stars = 3, time = "rain", repair = true, armor = 2, restart = 32, dmg = 0.42f, sides = 2, tip = "坦克和导弹车开局就在街上，通缉不会低于三星" },
            new Level { id = 8, name = "总部前庭", maxStars = 5, towers = 4, power = 1, seal = 100, stars = 2, time = "night", repair = true, backup = true, armor = 3, restart = 28, dmg = 0.38f, sides = 2, mechStars = 4, tip = "四星时机甲空降：把它推到楼上，撞出破绽" },
            new Level { id = 9, name = "决战之夜", maxStars = 5, towers = 5, power = 1, seal = 115, stars = 2, time = "rain", repair = true, backup = true, armor = 3, restart = 28, dmg = 0.38f, sides = 2, mechStars = 4, mechs = 2, tip = "两台机甲守着最后五座塔，拆完力量就都回来了" },
        };
        public LevelSpec Spec => new LevelSpec { towers = towers, power = power, backup = backup, near = near };
    }

    /// <summary>一次性事件（表现层 / 教学 / 音效读）</summary>
    public class Ev
    {
        public string type, cause, kind, etype;
        public int id = -1, from, to, chain, stars, tower = -1, car = -1, hp, mods, i;
        public Vector3? dir, pos; public float r, dmg, n;
        public override string ToString() => $"{type}#{id} {cause}{kind}{etype}";
    }
}
