#!/bin/bash
# 用法：shot.sh 输出名 px py pz 目标x 目标y 目标z [fov]
~/.local/bin/py3 ~/.unity/monster-tools/cs.py "var p = new Vector3($2f,$3f,$4f); var t = new Vector3($5f,$6f,$7f); var e = Quaternion.LookRotation(t - p).eulerAngles; Monster.EditorTools.MonsterPreview.Shot(\"/tmp/monster_game/shots/$1.png\", p, e, ${8:-50}, 1280, 800); result.Log(\"shot\");" 2>&1 | grep "\[Log\]\|rror"
