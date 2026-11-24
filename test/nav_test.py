r"""nav_test.py -- the left stick picks a slider, A ticks it, the right stick steps it (NudgeNav.cs, 2026-11-24), against the pretend
game, with a pretend Window View block (the controllers), in build\navtest only: no real game, no real mouse (clicks are posted to
the window), no real pose files (--data). Also photographs the window, so the layout can be looked at.

    python test\nav_test.py            -> PASS / FAIL lines, build\navtest\*.png

Needs Window View closed (its block has the same name)."""
import ctypes, json, mmap, os, shutil, struct, subprocess, sys, time
from ctypes import wintypes

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
T = os.path.join(ROOT, "build", "navtest")
user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32
kernel32.GetTickCount64.restype = ctypes.c_uint64
WM_LBUTTONDOWN, WM_LBUTTONUP = 0x201, 0x202
CTRL_MODE, STICK_Y, BUTTONS, LSTICK_Y, STICK_X, LSTICK_X = 536, 580, 584, 624, 636, 640
results = []


def check(name, ok, info=""):
    results.append(ok)
    print(("PASS " if ok else "FAIL ") + name + (" (" + info + ")" if info and not ok else ""), flush=True)


def put_f(view, off, v): view[off:off + 4] = struct.pack("<f", v)
def put_i(view, off, v): view[off:off + 4] = struct.pack("<i", v)


def pulse(view, off, value, hold=0.12, as_int=False):
    (put_i if as_int else put_f)(view, off, value)
    time.sleep(hold)
    (put_i if as_int else put_f)(view, off, 0)
    time.sleep(0.25)


def children(hwnd):
    out = []
    proc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def cb(h, _):
        n = user32.GetWindowTextLengthW(h)
        buf = ctypes.create_unicode_buffer(n + 1)
        user32.GetWindowTextW(h, buf, n + 1)
        out.append((h, buf.value))
        return True
    user32.EnumChildWindows(hwnd, proc(cb), 0)
    return out


def post_click(h, middle=False):
    lp = (8 << 16) | 8
    down, up, mk = (0x207, 0x208, 0x10) if middle else (WM_LBUTTONDOWN, WM_LBUTTONUP, 1)
    user32.PostMessageW(h, down, mk, lp)
    time.sleep(0.05)
    user32.PostMessageW(h, up, 0, lp)
    time.sleep(0.4)


def autosave_joints():
    path = os.path.join(T, "appdata", "re3", "autosave.json")
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f).get("joints", {})
    except (OSError, ValueError):
        return {}


def grab(name):
    subprocess.run([sys.executable, os.path.join(HERE, "grab_window.py"), "Hand Poser", os.path.join(T, name)], check=False)


def commands():
    try:
        with open(os.path.join(T, "fake.out"), encoding="utf-8") as f:
            return [l for l in f if l.startswith("COMMAND")]
    except OSError:
        return []


def main():
    if user32.FindWindowW(None, "Hand Poser"):
        sys.exit("close the real Hand Poser first")
    shutil.rmtree(T, ignore_errors=True)
    os.makedirs(T)
    wv = mmap.mmap(-1, 644, tagname=r"Local\FlatToVR_WindowView_v2")
    put_i(wv, CTRL_MODE, 1)
    env = dict(os.environ, FAKE_GUN="wp1000_Inventory", FAKE_SPOT="")
    fake = subprocess.Popen([sys.executable, os.path.join(HERE, "fake_game.py"), T, "60"], env=env,
                            stdout=open(os.path.join(T, "fake.out"), "w"), stderr=subprocess.STDOUT)
    time.sleep(2)
    game = os.path.join(T, "FakeGame")
    data = os.path.join(game, "reframework", "data", "hand_poser")
    # a gun with many spots, as the real Village guns have: the spot rows wrap and the left column needs more than the window's
    # starting height; the window has to grow so nothing scrolls (2026-11-24)
    left = ["slide", "magazine", "handle", "drum", "insert point", "slide from the left", "hammer", "slide at the back",
            "slide from the left at the back", "magazine in hand", "two magazines in hand", "shell port", "land 1", "land 2"]
    with open(os.path.join(data, "spot_numbers.json"), "w") as f:
        json.dump({"wp1000_Inventory": {"L": left, "R": ["right hand on the cover", "right hand on the bolt", "right hand over the hammer",
                   "right hand on the bolt at the back", "right hand, thumb on the latch", "right hand, the latch flicked open"]}}, f)
    fingers = ["l_hand_%s_%d" % (f, i) for f in ("index", "middle", "ring", "little", "thumb") for i in range(3)]
    poser = subprocess.Popen([os.path.join(ROOT, "dist", "HandPoser.exe"), "--game", game, "--data", os.path.join(T, "appdata")])
    time.sleep(6)
    data = os.path.join(game, "reframework", "data", "hand_poser")
    with open(os.path.join(data, "remote.json"), "w") as f:
        json.dump({"seq": 1, "set": {n: [0, 0, 0] for n in fingers}}, f)
    time.sleep(3)
    hwnd = user32.FindWindowW(None, "Hand Poser")
    check("the window is up", bool(hwnd))
    r = wintypes.RECT(); user32.GetClientRect(hwnd, ctypes.byref(r))
    print("client size", r.right, "x", r.bottom)
    # 2026-11-25: the numbered spot buttons moved to Hand Freeze's SPOTS tab; this window keeps only the headline
    check("no spot buttons in this window any more", not [h for h, t in children(hwnd) if t == "FOLLOW THE GAME"])
    clear = [h for h, t in children(hwnd) if t == "CLEAR ALL TICKS"]
    if clear:
        left_col = user32.GetParent(user32.GetParent(clear[0]))
        style = user32.GetWindowLongW(left_col, -16)
        check("the left column has no scroll bar", not (style & 0x00200000))
    grab("1-layout.png")

    kids = children(hwnd)
    head = [h for h, t in kids if t == "LEFT INDEX"]
    check("a LEFT INDEX box", bool(head), ", ".join(sorted(set(t for _, t in kids if t.startswith("LEFT")))))
    check("no BEND ALL bar any more", not any(t == "BEND ALL" for _, t in kids))
    if head:
        post_click(head[0])
    before = len(commands())
    pulse(wv, LSTICK_Y, -1.0)   # the first push: the part's first slider (the knuckle's first)
    pulse(wv, LSTICK_X, 1.0)    # right: its second slider
    grab("2-picked.png")
    pulse(wv, STICK_X, 1.0)     # nothing ticked: the right stick steps the selected slider
    time.sleep(0.5)
    new = commands()[before:]
    check("the right stick stepped the selected slider (no tick)", any("hold=15" in l for l in new), "%d new commands" % len(new))
    stepped = commands()[-1] if commands() else ""
    pulse(wv, BUTTONS, 8, as_int=True)   # X: undo
    time.sleep(0.5)
    last = commands()[-1] if commands() else ""
    check("X undoes the last slider: the index knuckle back to 0", ("l_hand_index_0=[0, 0, 0]" in last or "l_hand_index_0=[0.0, 0.0, 0.0]" in last) and last != stepped,
          stepped.strip() + " -> " + last.strip())
    pulse(wv, BUTTONS, 2, as_int=True)   # Y: redo
    time.sleep(0.5)
    again = commands()[-1] if commands() else ""
    check("Y redoes it", stepped.split("l_hand_index_0=")[-1].strip() == again.split("l_hand_index_0=")[-1].strip(),
          again.strip())
    pulse(wv, BUTTONS, 8, as_int=True)   # X: undo again, to start the rest from 0
    pulse(wv, BUTTONS, 1, as_int=True)   # A: tick it
    pulse(wv, LSTICK_X, -1.0)            # left: the first slider of that row
    pulse(wv, BUTTONS, 1, as_int=True)   # A: tick that one too
    grab("3-two-ticked.png")
    before = len(commands())
    pulse(wv, STICK_X, 1.0)              # two ticked: both step
    time.sleep(0.5)
    new = commands()[before:]
    check("with two ticked, the right stick steps (a command went out)", len(new) > 0)
    pulse(wv, BUTTONS, 4, as_int=True)   # B: the ticks of this section off
    before = len(commands())
    put_f(wv, 576, 1.0)                  # the right grip held (a window carried in Window View) ...
    time.sleep(0.2)
    pulse(wv, STICK_X, 1.0)              # ... the right stick does nothing
    put_f(wv, 576, 0.0)
    time.sleep(0.6)
    check("a grip held: the right stick moves no slider", len(commands()) == before, "%d commands" % (len(commands()) - before))

    # LT (Window View 1.14 sends the middle button): the thumb and the middle finger picked as sections; the left stick then
    # walks the thumb's three rows and goes on to the middle finger, past the index finger
    kids = children(hwnd)
    for name in ("LEFT THUMB", "LEFT MIDDLE"):
        h = [x for x, t in kids if t == name]
        if h:
            post_click(h[0], middle=True)
    grab("4-two-sections.png")
    time.sleep(2.5)
    was = autosave_joints()              # the index finger was moved above on purpose: compare with now
    for _ in range(3):
        pulse(wv, LSTICK_Y, -1.0)        # down, down, down: thumb base -> middle -> tip -> the middle finger's knuckle
    pulse(wv, STICK_X, 1.0)
    time.sleep(2.5)                      # the autosave once the stick is let go
    joints = autosave_joints()
    moved = sorted(k for k, v in joints.items() if any(abs(x - y) > 0.01 for x, y in zip(v, was.get(k, [0, 0, 0]))))
    check("down past the thumb lands on the MIDDLE finger, not the index", moved == ["l_hand_middle_0"], ", ".join(moved))
    grab("5-on-the-middle-finger.png")

    # 2026-11-25: the left stick reaches buttons too: LT on the ticks row, LS right to the window's undo, A presses it
    kids = children(hwnd)
    for name in ("LEFT THUMB", "LEFT MIDDLE"):
        h = [x for x, t in kids if t == name]
        if h:
            post_click(h[0], middle=True)   # LT: both finger sections unticked again
    clear = [h for h, t in kids if t == "CLEAR ALL TICKS"]
    if clear:
        post_click(clear[0], middle=True)   # LT: the ticks row ticked, its first stop (CLEAR ALL TICKS) selected
    pulse(wv, LSTICK_X, 1.0)                # LS right: the undo button
    pulse(wv, BUTTONS, 1, as_int=True)      # A: pressed
    time.sleep(2.5)
    back = autosave_joints().get("l_hand_middle_0")
    grab("6-undo-button.png")
    check("LS reaches the undo button and A presses it (the middle finger back)", bool(clear) and back is not None and all(abs(x - y) < 0.01 for x, y in zip(back, was.get("l_hand_middle_0", [0, 0, 0]))), str(back))
    user32.PostMessageW(hwnd, 0x10, 0, 0)   # WM_CLOSE
    time.sleep(2)
    fake.terminate()
    print("ALL PASS" if all(results) else "SOME FAILED", flush=True)


if __name__ == "__main__":
    main()
