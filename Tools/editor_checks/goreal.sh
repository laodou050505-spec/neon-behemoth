#!/bin/bash
# goreal.sh 关号：模拟“真实桌面”进关（MonsterMR.DebugRealTable）
cs(){ ( ~/.local/bin/py3 ~/.unity/monster-tools/cs.py "$1" 2>&1 | grep "\[Log\]\|rror\|ERROR" ) >/tmp/monster_game/cs_out.txt 2>&1 & p=$!; ( sleep 100; kill $p 2>/dev/null ) >/dev/null 2>&1 & w=$!; wait $p 2>/dev/null; pkill -P $w 2>/dev/null; kill $w 2>/dev/null; cat /tmp/monster_game/cs_out.txt; }
( /tmp/monster_game/restart.sh Monster.Game.TitleScreen >/dev/null 2>&1 ) </dev/null & p=$!; ( sleep 300; kill $p 2>/dev/null ) >/dev/null 2>&1 & w=$!; wait $p 2>/dev/null; pkill -P $w 2>/dev/null; kill $w 2>/dev/null; sleep 3
cs "Monster.MonsterMR.DebugRealTable = ${REAL:-true}; Object.FindAnyObjectByType<Monster.Game.TitleScreen>().chosen = $1; result.Log(\"chosen $1 real=${REAL:-true}\");"
for i in $(seq 1 40); do sleep 2; cs 'var b = Object.FindAnyObjectByType<Monster.Game.BriefCard>(); result.Log("ready=" + (b ? b.ready.ToString() : "-"));' | grep -q "ready=True" && break; done
cs 'var b = Object.FindAnyObjectByType<Monster.Game.BriefCard>(); if (b && b.onDone != null) { b.onDone(); Object.Destroy(b); } result.Log("go");' >/dev/null; sleep 8
cs 'foreach (var pm in Object.FindObjectsByType<Monster.Game.PalmMenu>()) pm.enabled = false; var v = Object.FindAnyObjectByType<Monster.Game.GameView>(); if (v) v.paused = false; Camera.main.transform.SetPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.Euler(35, 0, 0)); result.Log("unpaused view=" + (v != null));'
