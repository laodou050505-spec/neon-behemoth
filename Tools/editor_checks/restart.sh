#!/bin/bash
# 可靠地：停止播放（循环确认）→ 编译 → 播放 → 等到某个组件出现（$1 = 类型名，例如 Monster.Game.TitleScreen）
cs() { ~/.local/bin/py3 ~/.unity/monster-tools/cs.py "$1" 2>&1 | grep "\[Log\]"; }
for i in $(seq 1 15); do ~/.local/bin/py3 ~/.unity/monster-tools/cs.py 'EditorApplication.isPlaying = false; result.Log("x");' >/dev/null 2>&1; sleep 4; cs 'result.Log("p="+EditorApplication.isPlaying);' | grep -q "p=False" && break; done
/tmp/monster_game/compile.sh
for i in $(seq 1 5); do cs 'EditorApplication.isPlaying = true; result.Log("go");' >/dev/null; sleep 5; cs 'result.Log("p="+EditorApplication.isPlaying);' | grep -q "p=True" && break; done
for i in $(seq 1 30); do sleep 3; cs "var o = Object.FindAnyObjectByType<$1>(); result.Log(\"has=\"+(o!=null));" | grep -q "has=True" && break; done
sleep 2; echo ready
