#!/bin/bash
# go.sh 关号：停播放 → 编译 → 播放 → 进关，并列出这期间新增的编译错误和异常
cs(){ ( ~/.local/bin/py3 ~/.unity/monster-tools/cs.py "$1" 2>&1 | grep "\[Log\]\|rror\|ERROR" ) >/tmp/monster_game/cs_out.txt 2>&1 & p=$!; ( sleep 100; kill $p 2>/dev/null ) >/dev/null 2>&1 & w=$!; wait $p 2>/dev/null; pkill -P $w 2>/dev/null; kill $w 2>/dev/null; cat /tmp/monster_game/cs_out.txt; }
L0=$(wc -l < /tmp/monster-logs/editor.log | tr -d " "); cs 'Time.timeScale = 1f; result.Log("ts");' >/dev/null
( /tmp/monster_game/gotut.sh $1 2>&1 | tail -1 ) </dev/null >/tmp/monster_game/go_out.txt 2>&1 & p=$!; ( sleep 420; kill $p 2>/dev/null ) >/dev/null 2>&1 & w=$!; wait $p 2>/dev/null; pkill -P $w 2>/dev/null; kill $w 2>/dev/null; cat /tmp/monster_game/go_out.txt
awk -v L=$L0 'NR>L && /error CS/' /tmp/monster-logs/editor.log | sort -u | cut -c1-230 | head -8
awk -v L=$L0 'NR>L && /Exception/' /tmp/monster-logs/editor.log | grep -v "DllNotFound\|PxrPlatform" | sort | uniq -c | head -5 | cut -c1-220
cs 'foreach (var pm in Object.FindObjectsByType<Monster.Game.PalmMenu>()) pm.enabled = false; var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); if (v) { v.paused = false; } var cam = Camera.main.transform; cam.SetPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.Euler(35, 0, 0)); result.Log("unpaused view=" + (v != null));'
