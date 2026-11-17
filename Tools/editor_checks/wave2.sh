#!/bin/bash
# 能量波逐帧验收（第二版，时间可控）：wave2.sh 姿势 输出目录 [怒气=100] [视线=target|away] [MX=0] [TX=0] [TZ=0]；环境变量 MY
# 做法：摆姿势 → 慢放推进 → 到了要拍的时刻把时间冻结（timeScale=0，特效照画）→ 拍 → 继续。
# 检查 / 截图命令文本固定、参数走文件，不会每次重编译（重编译的十几秒里游戏会继续跑，时间就对不上）
cs(){ ( ~/.local/bin/py3 ~/.unity/monster-tools/cs.py "$1" 2>&1 | grep "\[Log\]\|rror" ) & p=$!; ( sleep 120; kill $p 2>/dev/null ) >/dev/null 2>&1 & w=$!; wait $p 2>/dev/null; pkill -P $w 2>/dev/null; kill $w 2>/dev/null; }
step(){ echo "$1" > /tmp/monster_game/wstep.txt; cs "$(cat /tmp/monster_game/wstep.cs)"; }
shot(){ echo "$DIR $1 $2" > /tmp/monster_game/wshot.txt; cs "$(cat /tmp/monster_game/wshot.cs)"; }
val(){ echo "$1" | sed -n "s/.*$2=\([-0-9.]*\).*/\1/p"; }
POSE=$1; DIR=$2; RAGE=${3:-100}; GAZE=${4:-target}; MX=${5:-0}; TX=${6:-0}; TZ=${7:-0}
G="target"; [ "$GAZE" = "away" ] && G="(head + new Vector3(-1.5f, -0.2f, 1f))"
step 0 >/dev/null   # 先冻结
[ -z "$NOPOSE" ] && cs 'var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); v.simInput = null; v.game.breathing = 0; v.game.charge = 0; v.game.beam = null; result.Log("reset");' >/dev/null
[ -z "$NOPOSE" ] && cs "$(sed -e "s/POSE/$POSE/g; s/RAGE/${RAGE}f/; s/MX/${MX}f/; s/MY/${MY:--0.27}f/; s/TX/${TX}f/; s/TZ/${TZ}f/; s/GAZE/$G/" /tmp/monster_game/wavepose.cs)"
ge(){ awk -v a="$1" -v b="$2" 'BEGIN{exit !(a+0 >= b+0)}'; }
r=$(step 0); echo "$r" | cut -c1-160
if ! echo "$r" | grep -q "rage=100"; then echo "怒气不满，姿势脚本没生效，退出"; exit 1; fi
# 蓄力：拍 0.6 / 0.9
for c in ${CH:-0.60 0.90}; do
  for i in $(seq 1 60); do r=$(step 0.05); ch=$(val "$r" charge); br=$(val "$r" breathing); if ge "$ch" "$c" || ge "$br" 0.01; then break; fi; done
  step 0 >/dev/null; shot "c$c" close | cut -c1-80
done
# 发射：从 1.8 秒倒数，拍 刚发射 / 发射 0.3 秒 / 中段 / 收尾
for b in ${BR:-1.77 1.50 0.90 0.12}; do
  for i in $(seq 1 150); do r=$(step 0.03); br=$(val "$r" breathing); ch=$(val "$r" charge); if ge "$br" 0.0001 && ge "$b" "$br"; then break; fi; if [ $i -gt 25 ] && ! ge "$br" 0.0001; then break; fi; done
  step 0 | cut -c1-200; shot "f$b" close | cut -c1-80
done
[ -z "$KEEP" ] && step 1 >/dev/null
echo finished
