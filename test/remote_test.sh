#!/usr/bin/env bash
# remote_test.sh -- the remote input (app/Remote.cs) against the pretend game, in build/remotetest only (no real game, no
# real mouse, no real pose files). PASS = both answers and the pretend game's echo are as expected. 2026-11-23.
cd "$(dirname "$0")/.."; T=build/remotetest; rm -rf $T; mkdir -p $T
(python test/fake_game.py $T 40 > $T/fake.out 2>&1 &); sleep 2
powershell -NoProfile -Command "\$p = Start-Process -FilePath '$(cygpath -w "$PWD/dist/HandPoser.exe")' -ArgumentList '--game','\"$(cygpath -w "$PWD/$T/FakeGame")\"','--data','\"$(cygpath -w "$PWD/$T/appdata")\"' -PassThru; \$p.Id" > $T/pid.txt
sleep 6; D=$T/FakeGame/reframework/data/hand_poser; ok=1
echo '{"seq": 1, "set": {"l_hand_index_0": [1, 2, -60], "l_hand_thumb_1": [0, 33, 0]}}' > $D/remote.json; sleep 3
grep -q '"seq": 1, "ok": true, "set": 2' $D/remote_done.json || { echo "FAIL first answer: $(cat $D/remote_done.json)"; ok=0; }
echo '{"seq": 2, "set": {"l_hand_index_0.z": -45, "nojoint": [0,0,0], "l_hand_ring_0.y": 5}}' > $D/remote.json; sleep 3
grep -q '"seq": 2, "ok": true, "set": 1' $D/remote_done.json || { echo "FAIL second answer: $(cat $D/remote_done.json)"; ok=0; }
grep -q 'no joint nojoint' $D/remote_done.json || { echo "FAIL the unknown joint was not named"; ok=0; }
grep -q 'l_hand_index_0=\[1, 2, -45\]' $T/fake.out || { echo "FAIL the game did not get the one-axis change"; ok=0; }
powershell -NoProfile -Command "\$p = Get-Process -Id $(tr -d '\r' < $T/pid.txt); \$p.CloseMainWindow() | Out-Null" >/dev/null
[ $ok = 1 ] && echo "ALL PASS (4 checks)"
