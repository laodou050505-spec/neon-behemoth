#!/bin/bash
cs(){ ~/.local/bin/py3 ~/.unity/monster-tools/cs.py "$1" 2>&1 | grep "\[Log\]"; }
/tmp/monster_game/restart.sh Monster.Game.TitleScreen >/dev/null 2>&1
cs 'Monster.Game.GameSettings.Intro = true; Object.FindAnyObjectByType<Monster.Game.TitleScreen>().chosen = 0; result.Log("go");'
for i in $(seq 1 40); do cs 'result.Log("ci="+(Object.FindAnyObjectByType<Monster.Game.ComicIntro>()!=null));' | grep -q "ci=True" && break; sleep 1; done
sleep 1
cs "Object.FindAnyObjectByType<Monster.Game.ComicIntro>().ShowAll(${1:-6}); result.Log(\"shown\");"
sleep 2
cs "var cam = Camera.main.transform; Monster.EditorTools.MonsterPreview.Shot(\"/tmp/monster_game/shots/neon/comic.png\", cam.position, cam.eulerAngles, ${2:-75}, 1280, 800); result.Log(\"shot\");"
