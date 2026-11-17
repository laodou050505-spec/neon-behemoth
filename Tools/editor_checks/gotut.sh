#!/bin/bash
# 重开播放 → 标题选教学 → 翻完漫画 → 等城市长出来
cs(){ ~/.local/bin/py3 ~/.unity/monster-tools/cs.py "$1" 2>&1 | grep "\[Log\]\|rror"; }
/tmp/monster_game/restart.sh Monster.Game.TitleScreen >/dev/null 2>&1
sleep 3
L=${1:-0}; cs "Object.FindAnyObjectByType<Monster.Game.TitleScreen>().chosen = $L; result.Log(\"chosen\");"
for i in $(seq 1 60); do sleep 2; cs 'result.Log("c="+(Object.FindAnyObjectByType<Monster.Game.ComicIntro>()!=null)+" b="+(Object.FindAnyObjectByType<Monster.Game.BriefCard>()!=null));' | grep -q "=True" && break; done
for i in $(seq 1 14); do cs 'var c = Object.FindAnyObjectByType<Monster.Game.ComicIntro>(); if (c) c.Advance(); var b = Object.FindAnyObjectByType<Monster.Game.BriefCard>(); if (b && b.onDone != null) { b.onDone(); Object.Destroy(b); } result.Log("x");' >/dev/null; sleep 0.8; done
sleep 7; echo ready
