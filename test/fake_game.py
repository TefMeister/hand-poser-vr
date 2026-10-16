r"""fake_game.py -- stands in for the in-game script, so the window can be tried with no game running.

    python test\fake_game.py <folder> [seconds]

Makes <folder>\FakeGame\reframework\{autorun,data\hand_poser}, writes a state.json with a small RE-style skeleton
once a second (like the script), watches command.json, and echoes every held joint's angles back as the "live"
angles, the way the game would once the hold is applied. Prints each new command it sees:
    COMMAND seq=3 hold=3 want_read=3 l_hand_index_0=[30.0, 20.0, 30.0]
Joints that are not held report a fixed "animation" angle, so the window's first numbers visibly come from the game.
"""
import json, os, sys, time

JOINTS = [("root", -1), ("spine_0", 0), ("spine_1", 1), ("l_arm_clavicle", 2), ("l_arm_upperarm", 3),
          ("l_arm_forearm", 4), ("l_arm_wrist", 5)]
for f in ("index", "middle", "ring", "little", "thumb"):
    base = len(JOINTS)
    JOINTS.append(("l_hand_%s_0" % f, 6))
    JOINTS.append(("l_hand_%s_1" % f, base))
    JOINTS.append(("l_hand_%s_2" % f, base + 1))
JOINTS += [("r_arm_clavicle", 2), ("r_arm_upperarm", len(JOINTS)), ("r_arm_forearm", len(JOINTS) + 1), ("r_arm_wrist", len(JOINTS) + 2)]
wrist_r = len(JOINTS) - 1
for f in ("index", "middle", "ring", "little", "thumb"):
    base = len(JOINTS)
    JOINTS.append(("r_hand_%s_0" % f, wrist_r))
    JOINTS.append(("r_hand_%s_1" % f, base))
    JOINTS.append(("r_hand_%s_2" % f, base + 1))

ANIM = {name: [round((i * 7) % 60 - 20.0, 1), round((i * 3) % 40 - 10.0, 1), round((i * 11) % 90 - 45.0, 1)]
        for i, (name, _) in enumerate(JOINTS)}


def main():
    root = sys.argv[1]
    seconds = float(sys.argv[2]) if len(sys.argv) > 2 else 60
    data = os.path.join(root, "FakeGame", "reframework", "data", "hand_poser")
    os.makedirs(os.path.join(root, "FakeGame", "reframework", "autorun"), exist_ok=True)
    os.makedirs(data, exist_ok=True)
    state_file, cmd_file = os.path.join(data, "state.json"), os.path.join(data, "command.json")
    print("GAME FOLDER", os.path.join(root, "FakeGame"))
    last_seq, held, want = -1, {}, []
    end = time.time() + seconds
    next_write = 0
    while time.time() < end:
        try:
            with open(cmd_file, encoding="utf-8") as f:
                cmd = json.load(f)
        except (OSError, ValueError):
            cmd = None
        if cmd and cmd.get("seq") != last_seq:
            last_seq = cmd["seq"]
            held = cmd.get("hold", {})
            want = cmd.get("want_read", [])
            first = sorted(held)[0] if held else None
            print("COMMAND seq=%s hold=%d want_read=%d%s" % (last_seq, len(held), len(want),
                  (" %s=%s" % (first, held[first])) if first else ""), flush=True)
            next_write = 0
        if time.time() >= next_write:
            next_write = time.time() + 1.0
            angles = {}
            for name in set(want) | set(held):
                angles[name] = held.get(name, ANIM.get(name))
            st = {"game": "re3", "character": "pl2000", "connected": True, "seq_seen": last_seq, "frame": int(time.time()),
                  "error": "", "joints": [list(j) for j in JOINTS], "angles": angles}
            tmp = state_file + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(st, f)
            os.replace(tmp, state_file)
        time.sleep(0.05)
    print("DONE last_seq=%s held=%d" % (last_seq, len(held)))


if __name__ == "__main__":
    main()
