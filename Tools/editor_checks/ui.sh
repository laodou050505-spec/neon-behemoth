#!/bin/bash
# ui.sh 名字 [方法名]
( ~/.local/bin/py3 ~/.unity/monster-tools/cs.py "$(echo "$*" > /tmp/monster_game/ui.txt; cat /tmp/monster_game/uishot.cs)" 2>&1 | grep "\[Log\]\|rror\|ERROR" ) >/tmp/monster_game/cs_out.txt 2>&1 & p=$!; ( sleep 100; kill $p 2>/dev/null ) >/dev/null 2>&1 & w=$!; wait $p 2>/dev/null; pkill -P $w 2>/dev/null; kill $w 2>/dev/null; cat /tmp/monster_game/cs_out.txt
