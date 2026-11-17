#!/bin/bash
# 刷新 Unity 并等编译结束，打印本次新增的 error CS
L=$(wc -l < /tmp/monster-logs/editor.log | tr -d ' ')
~/.local/bin/py3 ~/.unity/monster-tools/cs.py 'AssetDatabase.Refresh(); result.Log("refreshed");' >/dev/null 2>&1
for i in $(seq 1 60); do sleep 4; r=$(~/.local/bin/py3 ~/.unity/monster-tools/cs.py 'result.Log("c="+EditorApplication.isCompiling);' 2>&1 | grep "\[Log\]"); echo "$r" | grep -q "c=False" && break; done
sleep 3
grep -n "error CS" /tmp/monster-logs/editor.log | awk -F: -v L=$L '$1>L' | cut -d: -f2- | sort -u | head -40
echo "done (from line $L)"
