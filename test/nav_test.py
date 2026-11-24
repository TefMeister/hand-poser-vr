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


def post_click(h):
    lp = (8 << 16) | 8
    user32.PostMessageW(h, WM_LBUTTONDOWN, 1, lp)
    time.sleep(0.05)
    user32.PostMessageW(h, WM_LBUTTONUP, 0, lp)
    time.sleep(0.4)


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
    check("the window grew past its starting height for the long spot rows", r.bottom > 1180, str(r.bottom))
    clear = [h for h, t in children(hwnd) if t == "CLEAR ALL TICKS"]
    if clear:
        left_col = user32.GetParent(user32.GetParent(clear[0]))
        style = user32.GetWindowLongW(left_col, -16)
        check("the left column has no scroll bar", not (style & 0x00200000))
    grab("1-layout.png")

    kids = children(hwnd)
    head = [h for h, t in kids if t == "LEFT INDEX"]
    check("a LEFT INDEX box", bool(head), ", ".join(sorted(set(t for _, t in kids if t.startswith("LEFT")))))
    if head:
        post_click(head[0])
    before = len(commands())
    pulse(wv, LSTICK_Y, -1.0)   # the first push: the part's first slider (BEND ALL)
    pulse(wv, LSTICK_Y, -1.0)   # down: the first joint row
    pulse(wv, LSTICK_X, 1.0)    # right: its second slider
    grab("2-picked.png")
    pulse(wv, STICK_X, 1.0)     # nothing ticked: the right stick steps the selected slider
    time.sleep(0.5)
    new = commands()[before:]
    check("the right stick stepped the selected slider (no tick)", any("hold=15" in l for l in new), "%d new commands" % len(new))
    pulse(wv, BUTTONS, 1, as_int=True)   # A: tick it
    pulse(wv, LSTICK_X, -1.0)            # left: the first slider of that row
    pulse(wv, BUTTONS, 1, as_int=True)   # A: tick that one too
    grab("3-two-ticked.png")
    before = len(commands())
    pulse(wv, STICK_X, 1.0)              # two ticked: both step
    time.sleep(0.5)
    new = commands()[before:]
    check("with two ticked, the right stick steps (a command went out)", len(new) > 0)
    user32.PostMessageW(hwnd, 0x10, 0, 0)   # WM_CLOSE
    time.sleep(2)
    fake.terminate()
    print("ALL PASS" if all(results) else "SOME FAILED", flush=True)


if __name__ == "__main__":
    main()
