r"""sync_kit.py -- one copy of the helper windows' shared parts (2026-11-25, Tefa: "if possible, unify the look of all the window view
apps so it is easier for you to build that navigation into all the apps around the same control layout").

The kit (hand-poser-vr/kit/: the look, the controller buttons, the left stick's walking, the one undo, the little JSON reader) is
edited ONLY here, then copied into the other helpers' own kit/ folder, each a clone beside this one:

    python tools\sync_kit.py           copy the kit into every helper found beside hand-poser-vr
    python tools\sync_kit.py --check   change nothing; exit 1 when a copy differs or an old copy of a kit file is still in the
                                       app's own folders (each build.bat runs this first, when hand-poser-vr is beside it)

Files are written with Windows line ends, so a copy never differs by those alone."""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
KIT = os.path.join(REPO, "kit")
BESIDE = os.path.dirname(REPO)
HELPERS = ["grip-zones-vr", "holster-zones-vr", "gun-poser-vr", "animation-timing-vr", "hand-freeze-vr"]   # Hand Freeze joined 2026-11-25
OLD_PLACES = ["app", "shared"]   # where the apps kept their own copies before the kit


def text(path):
    with open(path, "rb") as f:
        return f.read().replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")


def kit_files():
    return sorted(n for n in os.listdir(KIT) if n.endswith((".cs", ".md")))


def main():
    check = "--check" in sys.argv[1:]
    names = kit_files()
    bad = []
    targets = [REPO] + [os.path.join(BESIDE, h) for h in HELPERS if os.path.isdir(os.path.join(BESIDE, h))]
    for name in names:   # the kit's own files keep Windows line ends too
        p = os.path.join(KIT, name)
        want = text(p)
        with open(p, "rb") as f:
            if f.read() != want:
                if check: bad.append("kit/" + name + " (line ends)")
                else:
                    with open(p, "wb") as g: g.write(want)
    for t in targets:
        app = os.path.basename(t)
        for name in names:
            want = text(os.path.join(KIT, name))
            dst = os.path.join(t, "kit", name)
            if t != REPO:
                have = open(dst, "rb").read() if os.path.exists(dst) else None
                if have != want:
                    if check: bad.append(app + "/kit/" + name + (" missing" if have is None else " differs"))
                    else:
                        os.makedirs(os.path.dirname(dst), exist_ok=True)
                        with open(dst, "wb") as f: f.write(want)
                        print("copied  " + app + "/kit/" + name)
            for place in OLD_PLACES:
                old = os.path.join(t, place, name)
                if name.endswith(".cs") and os.path.exists(old):
                    bad.append(app + "/" + place + "/" + name + " (an old copy: delete it, the kit has it)")
        if t != REPO:   # a kit file this kit no longer has
            for name in (os.listdir(os.path.join(t, "kit")) if os.path.isdir(os.path.join(t, "kit")) else []):
                if name not in names: bad.append(app + "/kit/" + name + " (not in the kit any more)")
    missing = [h for h in HELPERS if not os.path.isdir(os.path.join(BESIDE, h))]
    for line in bad: print("KIT  " + line)
    if missing: print("not beside hand-poser-vr (skipped): " + ", ".join(missing))
    if bad:
        print("KIT OUT OF STEP" if check else "KIT COPIED, BUT SEE ABOVE")
        sys.exit(1)
    print("KIT IN STEP: " + ", ".join(os.path.basename(t) for t in targets))


if __name__ == "__main__":
    main()
