r"""family_icons.py -- draws the icons of the helper apps on one shared tile (Tefa 2026-10-25: "please make the app
background unified on all 4 apps ... as they are sort of all a part of this helper program family"): a dark rounded square
with a blue edge, and each app's own picture on it.

    python tools\family_icons.py <out folder>     writes hand_poser.ico, grab_zones.ico, window_view.ico, hand_freeze.ico
                                                   and a preview sheet family.png (256, 64, 48, 32, 16 pixels)

The canonical copy lives in hand-poser-vr; the icons are copied into each app's app\ folder.
"""
import math, os, sys
from PIL import Image, ImageDraw

S = 1024                     # drawn at 4x and shrunk, for smooth edges
K = S / 256
TILE = (36, 38, 50, 255)     # the shared tile
EDGE = (70, 120, 200, 255)   # its blue edge
TILE_BOX, TILE_R, EDGE_W = (6, 6, 250, 250), 44, 9
SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]


def R(*v):
    return [x * K for x in v]


def tile():
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(im).rounded_rectangle(R(*TILE_BOX), radius=TILE_R * K, fill=TILE)
    return im


def edge(im):
    ImageDraw.Draw(im).rounded_rectangle(R(*TILE_BOX), radius=TILE_R * K, outline=EDGE, width=int(EDGE_W * K))
    return im


def inside_mask():
    m = Image.new("L", (S, S), 0)
    ImageDraw.Draw(m).rounded_rectangle(R(*TILE_BOX), radius=TILE_R * K, fill=255)
    return m


def hand_poser():
    im = tile(); d = ImageDraw.Draw(im)
    SKIN, DOT = (236, 200, 166, 255), EDGE

    def pill(p0, p1, w):
        d.line(R(*p0, *p1), fill=SKIN, width=int(w * K))
        for p in (p0, p1):
            d.ellipse(R(p[0] - w / 2, p[1] - w / 2, p[0] + w / 2, p[1] + w / 2), fill=SKIN)
    pill((98, 192), (50, 134), 22)                                     # the thumb, out and up to the left
    d.rounded_rectangle(R(86, 136, 186, 218), radius=24 * K, fill=SKIN)  # the palm
    W, GAP = 17, 10                                                    # wide gaps: the fingers stay apart at desktop size
    xs = [86 + i * (W + GAP) for i in range(4)]
    for x, top in zip(xs, [74, 46, 50, 72]):
        d.rounded_rectangle(R(x, top, x + W, 154), radius=8.5 * K, fill=SKIN)
    for x in xs[1:]:
        d.rectangle(R(x - GAP, 94, x, 150), fill=TILE)                 # the slots run down into the palm
    d.polygon(R(86, 136, 86, 170, 74, 158), fill=TILE)                 # the gap between the thumb and the palm
    for x in xs:
        c = x + W / 2
        d.ellipse(R(c - 6, 96, c + 6, 108), fill=DOT)                  # joint dots, no bar joining them
    d.ellipse(R(58, 144, 72, 158), fill=DOT)
    return edge(im)


def grab_zones():
    im = tile(); d = ImageDraw.Draw(im)
    d.ellipse(R(34, 34, 222, 222), outline=(90, 200, 120, 255), width=int(16 * K))
    d.ellipse(R(80, 80, 176, 176), outline=(230, 190, 90, 255), width=int(12 * K))
    d.ellipse(R(116, 116, 140, 140), fill=(235, 235, 240, 255))
    return edge(im)


def hand_freeze():
    im = tile(); d = ImageDraw.Draw(im)
    ICE, c, w = (196, 232, 255, 255), (128, 128), 16
    for a in range(0, 360, 60):
        r = math.radians(a)
        ex, ey = c[0] + 86 * math.cos(r), c[1] + 86 * math.sin(r)
        d.line(R(c[0], c[1], ex, ey), fill=ICE, width=int(w * K))
        for side in (-1, 1):                                            # the two little branches near each tip
            bx, by = c[0] + 54 * math.cos(r), c[1] + 54 * math.sin(r)
            rr = r + side * math.radians(45)
            d.line(R(bx, by, bx + 30 * math.cos(rr), by + 30 * math.sin(rr)), fill=ICE, width=int(12 * K))
    d.ellipse(R(108, 108, 148, 148), fill=ICE)
    return edge(im)


def window_view():
    scene = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(scene)
    WALL, FR = TILE, (240, 236, 226, 255)
    d.rectangle(R(0, 0, 256, 256), fill=WALL)
    for y in range(int(30 * K), int(178 * K)):                          # the sky, lighter towards the hills
        t = (y - 30 * K) / (148 * K)
        d.line([(40 * K, y), (216 * K, y)], fill=(int(110 + 40 * t), int(185 + 30 * t), int(245 - 5 * t), 255))
    sx, sy = 162, 72
    for a in range(0, 360, 30):                                          # the sun and its rays
        r = math.radians(a)
        d.line(R(sx + 24 * math.cos(r), sy + 24 * math.sin(r), sx + 36 * math.cos(r), sy + 36 * math.sin(r)), fill=(255, 214, 90, 255), width=int(5 * K))
    d.ellipse(R(sx - 18, sy - 18, sx + 18, sy + 18), fill=(255, 226, 110, 255))
    hills = Image.new("RGBA", (S, S), (0, 0, 0, 0)); hd = ImageDraw.Draw(hills)
    hd.ellipse(R(20, 128, 170, 230), fill=(120, 190, 110, 255))
    hd.ellipse(R(110, 140, 260, 240), fill=(86, 164, 90, 255))
    view = Image.new("L", (S, S), 0); ImageDraw.Draw(view).rectangle(R(40, 30, 216, 178), fill=255)
    scene.paste(hills, (0, 0), Image.composite(hills, Image.new("RGBA", (S, S)), view).split()[3])   # the hills only in the glass
    d.rectangle(R(36, 26, 220, 34), fill=FR); d.rectangle(R(36, 26, 44, 180), fill=FR); d.rectangle(R(212, 26, 220, 180), fill=FR)
    d.rectangle(R(124, 26, 132, 180), fill=FR); d.rectangle(R(36, 100, 220, 107), fill=FR)
    d.rounded_rectangle(R(22, 172, 234, 190), radius=4 * K, fill=(206, 170, 120, 255))   # the sill
    d.rectangle(R(22, 186, 234, 192), fill=(160, 124, 82, 255))

    def pot(cx, w, h, leaves):
        d.polygon(R(cx - w / 2, 172 - h, cx + w / 2, 172 - h, cx + w / 2 - 5, 172, cx - w / 2 + 5, 172), fill=(204, 104, 62, 255))
        d.rectangle(R(cx - w / 2 - 3, 172 - h - 6, cx + w / 2 + 3, 172 - h), fill=(226, 124, 78, 255))
        top = 172 - h - 6
        for i, (dx, dy, rw, rh) in enumerate(leaves):
            d.ellipse(R(cx + dx - rw, top + dy - rh, cx + dx + rw, top + dy + rh), fill=(64, 168, 80, 255) if i % 2 else (92, 196, 96, 255))
    pot(66, 30, 26, [(-10, -10, 10, 16), (10, -12, 10, 16), (0, -24, 9, 16)])
    pot(128, 24, 20, [(-8, -6, 8, 10), (8, -6, 8, 10), (0, -14, 8, 11)])
    pot(190, 30, 28, [(-12, -14, 8, 20), (0, -26, 8, 22), (12, -14, 8, 20)])
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    im.paste(scene, (0, 0), inside_mask())                               # everything stays inside the tile
    return edge(im)


def gun_poser():
    # Tefa 2026-10-28: "a machine gun in the middle and arrows going around it"
    im = tile(); d = ImageDraw.Draw(im)
    ARROW, GUN, DARK = (90, 200, 120, 255), (214, 218, 226, 255), (150, 156, 170, 255)
    c, r, w = 128, 98, 13
    for a0, a1 in ((205, 335), (25, 155)):                               # two arrows, chasing each other clockwise
        d.arc(R(c - r - w / 2, c - r - w / 2, c + r + w / 2, c + r + w / 2), a0, a1, fill=ARROW, width=int(w * K))
        a = math.radians(a1)
        ex, ey = c + r * math.cos(a), c + r * math.sin(a)
        tx, ty, nx, ny = -math.sin(a), math.cos(a), math.cos(a), math.sin(a)
        d.polygon(R(ex + tx * 22, ey + ty * 22, ex + nx * 17, ey + ny * 17, ex - nx * 17, ey - ny * 17), fill=ARROW)
    d.polygon(R(52, 114, 86, 110, 86, 138, 56, 150), fill=GUN)           # the stock
    d.rounded_rectangle(R(84, 106, 154, 136), radius=4 * K, fill=GUN)    # the body
    d.rectangle(R(152, 111, 192, 128), fill=DARK)                         # the barrel shroud
    d.rectangle(R(190, 115, 210, 122), fill=GUN)                          # the barrel
    d.rectangle(R(198, 104, 203, 115), fill=GUN)                          # the front sight
    d.rectangle(R(98, 94, 140, 100), fill=GUN)                            # the carry handle
    d.rectangle(R(100, 94, 106, 108), fill=GUN); d.rectangle(R(132, 94, 138, 108), fill=GUN)
    d.polygon(R(126, 134, 142, 134, 154, 170, 138, 176), fill=DARK)      # the magazine, curved forward
    d.polygon(R(94, 134, 110, 134, 104, 164, 88, 162), fill=GUN)         # the grip
    return edge(im)


def main(out):
    os.makedirs(out, exist_ok=True)
    made = {"hand_poser": hand_poser(), "grab_zones": grab_zones(), "window_view": window_view(), "hand_freeze": hand_freeze(),
            "gun_poser": gun_poser()}
    sheet = Image.new("RGBA", (len(made) * 266, 256 + 74), (30, 30, 30, 255))
    for i, (name, im) in enumerate(made.items()):
        small = im.resize((256, 256), Image.LANCZOS)
        small.save(os.path.join(out, name + ".ico"), sizes=SIZES)
        sheet.alpha_composite(small, (i * 266, 0))
        x = i * 266
        for s in (64, 48, 32, 16):
            sheet.alpha_composite(im.resize((s, s), Image.LANCZOS), (x, 262)); x += s + 6
    sheet.save(os.path.join(out, "family.png"))
    print("wrote", ", ".join(n + ".ico" for n in made), "and family.png into", out)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
