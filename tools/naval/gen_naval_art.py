#!/usr/bin/env python3
"""
REBOUND RUMBLE - naval art generator.
Draws every ship / crew / rock / barrel / crate / harbour / icon sprite used by the sea battles as pixel art,
straight from code (no external art needed).  Ships are drawn top-down facing EAST (+x), local +y = left side.

usage:  python3 gen_naval_art.py <Assets/Resources/Sprites dir> [preview.png]
All images are drawn at their native low resolution; the game imports them at 16 pixels per unit with point filtering.
"""
import os, sys, math, random
from PIL import Image, ImageDraw

OUT = sys.argv[1] if len(sys.argv) > 1 else "."
PREVIEW = sys.argv[2] if len(sys.argv) > 2 else None
os.makedirs(OUT, exist_ok=True)


def C(h, a=255):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def mul(c, k):
    return (max(0, min(255, int(c[0] * k))), max(0, min(255, int(c[1] * k))), max(0, min(255, int(c[2] * k))), c[3])


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(4))


def smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


class Img:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        self.px = self.im.load()
        self.cx, self.cy = w / 2.0, h / 2.0

    # local ship coords (x forward, y left) <-> pixels
    def P(self, x, y):
        return int(math.floor(self.cx + x)), int(math.floor(self.cy - y))

    def put(self, x, y, c):
        """put in local coords (blends over)"""
        i, j = self.P(x, y)
        self.set(i, j, c)

    def set(self, i, j, c):
        if 0 <= i < self.w and 0 <= j < self.h:
            if c[3] == 255 or self.px[i, j][3] == 0:
                self.px[i, j] = c
            else:
                o = self.px[i, j]
                a = c[3] / 255.0
                self.px[i, j] = (int(o[0] * (1 - a) + c[0] * a), int(o[1] * (1 - a) + c[1] * a), int(o[2] * (1 - a) + c[2] * a), max(o[3], c[3]))

    def get(self, i, j):
        if 0 <= i < self.w and 0 <= j < self.h:
            return self.px[i, j]
        return (0, 0, 0, 0)

    def rect(self, x0, y0, x1, y1, c):
        for x in range(int(math.floor(x0)), int(math.ceil(x1))):
            for y in range(int(math.floor(y0)), int(math.ceil(y1))):
                self.put(x, y, c)

    def disc(self, x, y, r, c):
        for dx in range(-int(r) - 1, int(r) + 2):
            for dy in range(-int(r) - 1, int(r) + 2):
                if dx * dx + dy * dy <= r * r:
                    self.put(x + dx, y + dy, c)

    def line(self, x0, y0, x1, y1, c):
        n = int(max(abs(x1 - x0), abs(y1 - y0)) * 2) + 1
        for k in range(n + 1):
            t = k / float(n)
            self.put(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, c)

    def outline(self, col, min_alpha=200):
        """1px outline around everything opaque"""
        src = self.im.copy().load()
        for j in range(self.h):
            for i in range(self.w):
                if self.px[i, j][3] == 0:
                    for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        ni, nj = i + di, j + dj
                        if 0 <= ni < self.w and 0 <= nj < self.h and src[ni, nj][3] >= min_alpha:
                            self.px[i, j] = col
                            break

    def save(self, name):
        path = os.path.join(OUT, name + ".png")
        self.im.save(path)
        return self.im


# ----------------------------------------------------------------------------------------------
#  hulls
# ----------------------------------------------------------------------------------------------
def profile(t, blunt, bowexp, bow_start=0.45, flat_bow=False):
    if t < 0 or t > 1:
        return 0.0
    stern = blunt + (1 - blunt) * smooth(0.0, 0.30, t)
    if flat_bow:
        bow = 1 - 0.55 * smooth(0.78, 1.0, t) ** 1.5
    else:
        bow = 1 - max(0.0, (t - bow_start) / (1 - bow_start)) ** bowexp
    return max(0.0, min(stern, bow))


def draw_hull(canvas_w, canvas_h, L, W, wood, deck, rail, trim, blunt=0.7, bowexp=1.7, ports=(), castle=0.0,
              flat_bow=False, seed=1, bow_start=0.45, forecastle=0.0, gunports=True):
    img = Img(canvas_w, canvas_h)
    rnd = random.Random(seed)
    plank_tone = [0.93 + 0.07 * rnd.random() for _ in range(400)]
    dark = mul(rail, 0.38)
    for j in range(canvas_h):
        for i in range(canvas_w):
            x = i + 0.5 - img.cx
            y = img.cy - (j + 0.5)
            t = (x + L / 2.0) / L
            hw = profile(t, blunt, bowexp, bow_start, flat_bow) * W / 2.0
            ay = abs(y)
            if hw < 0.6 or ay > hw:
                continue
            edge = hw - ay
            col = None
            if edge < 1.15:
                col = dark
            elif edge < 2.7:
                col = mul(rail, 1.12 if y > 0 else 0.86)           # rail, lit from the upper-left
                if int(x + L) % 6 == 0:
                    col = mul(col, 0.82)
            else:
                pi = int((x + L / 2.0) // 4)
                col = mul(deck, plank_tone[pi % 400] * (1.0 if y > -1 else 0.93))
                if int(math.floor(x + L / 2.0)) % 4 == 0:
                    col = mul(col, 0.8)                           # seams between planks
                if edge < 3.5:
                    col = mix(col, trim, 0.55)                    # painted trim line just inside the rail
            img.set(i, j, col)
    # bowsprit
    for k in range(0, 9):
        img.put(L / 2.0 - 3 + k, 0, dark if k % 2 == 0 else mul(wood, 0.6))
    img.put(L / 2.0 + 5, 0, mul(trim, 1.1))
    # stern castle (raised, covered deck)
    if castle > 0:
        x0 = -L / 2.0 + 2.5
        x1 = x0 + L * castle
        for x in range(int(x0), int(x1)):
            t = (x + L / 2.0) / L
            hw = profile(t, blunt, bowexp, bow_start, flat_bow) * W / 2.0 - 3.2
            if hw < 1:
                continue
            for y in range(-int(hw), int(hw) + 1):
                e = hw - abs(y)
                c = mul(wood, 0.62) if e < 1.0 or x < x0 + 1 or x >= x1 - 1 else mul(wood, 0.82 + 0.08 * ((x + y) % 3 == 0))
                img.put(x, y, c)
        # cabin windows on the stern
        for wy in (-W * 0.18, W * 0.18):
            img.rect(x0 + 0.5, wy - 1, x0 + 2, wy + 1, C("#f4d36a"))
    if forecastle > 0:
        x1 = L / 2.0 - 4
        x0 = x1 - L * forecastle
        for x in range(int(x0), int(x1)):
            t = (x + L / 2.0) / L
            hw = profile(t, blunt, bowexp, bow_start, flat_bow) * W / 2.0 - 3.0
            if hw < 1:
                continue
            for y in range(-int(hw), int(hw) + 1):
                e = hw - abs(y)
                c = mul(wood, 0.6) if e < 1.0 or x < x0 + 1 else mul(wood, 0.86)
                img.put(x, y, c)
    # gun ports: dark muzzles poking out of both rails
    if gunports:
        for px in ports:
            t = (px + L / 2.0) / L
            hw = profile(t, blunt, bowexp, bow_start, flat_bow) * W / 2.0
            for sgn in (1, -1):
                img.rect(px - 1.5, sgn * (hw - 1.2) - (0 if sgn > 0 else 1.5), px + 1.5, sgn * (hw + 1.0) + (1.5 if sgn > 0 else 0), C("#26262c"))
                img.put(px + 1, sgn * (hw + 0.5) - (0 if sgn > 0 else 1), C("#9a8a5a"))
    img.outline(C("#140d0a"))
    return img


def sail_poly(img, mx, span, thick, depth, base, edge, shadow=True, ribs=True):
    """a sail seen from above: a curved strip across the beam, billowing toward the bow"""
    pts = []
    for y in range(-int(span), int(span) + 1):
        k = 1 - (y / float(span)) ** 2
        bulge = depth * k
        pts.append((y, mx - thick / 2.0 + bulge, mx + thick / 2.0 + bulge))
    if shadow:
        for (y, a, b) in pts:
            for x in range(int(a) - 2, int(b) - 1):
                img.put(x, y - 3, C("#000000", 78))
    for (y, a, b) in pts:
        for x in range(int(math.floor(a)), int(math.ceil(b))):
            u = (x - a) / max(1.0, (b - a))
            shade = 0.82 + 0.28 * (1 - abs(u - 0.4)) - 0.16 * abs(y) / float(span)
            c = mul(base, shade)
            if x == int(math.floor(a)) or abs(y) >= span - 0.5:
                c = edge
            img.put(x, y, c)
        if ribs and y % 5 == 0:
            img.put(int(a) + 1, y, mul(base, 0.7))


def mast(img, mx, big=False):
    img.disc(mx, 0, 2.6 if big else 2.1, C("#3b2616"))
    img.disc(mx - 0.4, 0.4, 1.4 if big else 1.0, C("#7a5230"))
    img.put(mx - 1, 1, C("#b98a52"))
    if big:
        for a in range(0, 360, 20):
            img.put(mx + 4.2 * math.cos(math.radians(a)), 4.2 * math.sin(math.radians(a)), C("#5a3a20"))


def flag(img, x, y, col, length=7, skull=False):
    img.line(x, y, x, y, C("#2a1a10"))
    for k in range(length):
        h = max(1, int(round((length - k) / 3.0)))
        for dy in range(-h + 1, h):
            img.put(x - 1 - k, y + dy + (1 if k % 4 > 1 else 0), col)
    if skull:
        img.put(x - 3, y, C("#f0f0f0"))


def draw_rig(canvas_w, canvas_h, L, W, masts, sail, edge, flag_col, skull=False, rope=True, flag_pos=None, depth=2.5, thick=4):
    img = Img(canvas_w, canvas_h)
    if rope:
        mxs = [m[0] for m in masts]
        for mx in mxs:
            img.line(mx, 0, L / 2.0 + 4, 0, C("#d8d0b8", 120))
            img.line(mx, 0, -L / 2.0 + 3, 0, C("#d8d0b8", 120))
    for k, (mx, span) in enumerate(masts):
        sail_poly(img, mx, span, thick, depth, sail, edge)
    for k, (mx, span) in enumerate(masts):
        mast(img, mx, big=(k == 0))
    fx = flag_pos if flag_pos is not None else -L / 2.0 + 2
    flag(img, fx, 0, flag_col, 8, skull)
    img.outline(C("#0e0a08", 255), min_alpha=250) if False else None
    return img


# ----------------------------------------------------------------------------------------------
#  ships  (name, hull params, rig params)
# ----------------------------------------------------------------------------------------------
SHIPS = {}


def make_ship(name, L, W, canvas_w, canvas_h, hull_kw, masts, sail, edge, flag_col, rig_kw=None, skull=False, flag_pos=None):
    rig_kw = rig_kw or {}
    hull = draw_hull(canvas_w, canvas_h, L, W, **hull_kw)
    rig = draw_rig(canvas_w, canvas_h, L, W, masts, sail, edge, flag_col, skull=skull, flag_pos=flag_pos, **rig_kw)
    hull.save("ship_%s_hull" % name)
    rig.save("ship_%s_rig" % name)
    SHIPS[name] = (hull, rig)


# the player's ring-ship (cream sails, warm wood, gold trim)
make_ship("player", 100, 40, 124, 60,
          dict(wood=C("#7b4a26"), deck=C("#b9803f"), rail=C("#8a5630"), trim=C("#e8b84a"), blunt=0.72, bowexp=1.75,
               ports=(-6, 8), castle=0.17, forecastle=0.14, seed=3),
          [(18, 17), (-22, 18)], C("#efe3c4"), C("#7d5a3a"), C("#ffb02e"), flag_pos=-44)

# raider sloop: small, fast, black sail
make_ship("sloop", 46, 18, 64, 34,
          dict(wood=C("#33271f"), deck=C("#6b4a31"), rail=C("#4a3426"), trim=C("#8a2a22"), blunt=0.55, bowexp=1.3, ports=(), castle=0.0, seed=5, gunports=False),
          [(4, 9)], C("#2c2c36"), C("#5a1e1e"), C("#a22b2b"), rig_kw=dict(depth=2.0, thick=3), skull=True, flag_pos=-21)

# gunboat: stout, bow gun
make_ship("gunboat", 60, 26, 80, 44,
          dict(wood=C("#4a3b2a"), deck=C("#8d6b45"), rail=C("#5d4630"), trim=C("#9aa0aa"), blunt=0.66, bowexp=1.6, ports=(-8, 4), castle=0.16, seed=7),
          [(6, 11)], C("#4b4f5c"), C("#262a33"), C("#556a8c"), skull=False, flag_pos=-28)

# frigate: long broadsider
make_ship("frigate", 84, 34, 104, 52,
          dict(wood=C("#3e1f1c"), deck=C("#84553a"), rail=C("#5a2a26"), trim=C("#d6a53a"), blunt=0.7, bowexp=1.7, ports=(-20, -6, 8, 22), castle=0.15, forecastle=0.1, seed=9),
          [(14, 14), (-14, 15)], C("#2f2630"), C("#7a1f1f"), C("#a02424"), skull=True, flag_pos=-38)

# mortar barge: wide, flat, blunt, no sails
make_ship("mortar", 56, 36, 76, 52,
          dict(wood=C("#3a3f33"), deck=C("#6e6a50"), rail=C("#454a3a"), trim=C("#c9a040"), blunt=0.8, bowexp=1.4, ports=(), flat_bow=True, seed=11, gunports=False),
          [], C("#2c2c36"), C("#5a1e1e"), C("#8a6a2a"), rig_kw=dict(rope=False), flag_pos=-26)

# the DREAD GALLEON (boss)
make_ship("galleon", 160, 60, 188, 82,
          dict(wood=C("#231519"), deck=C("#5e3a2c"), rail=C("#3a1d24"), trim=C("#e0b13a"), blunt=0.74, bowexp=1.9, ports=(-46, -30, -14, 2, 18, 34), castle=0.2, forecastle=0.13, seed=13),
          [(46, 24), (6, 28), (-36, 24)], C("#1e1a26"), C("#c9a23a"), C("#c9a23a"), skull=True, flag_pos=-74, rig_kw=dict(depth=3.2, thick=5))


# mortar tub on the barge (drawn on top of the hull layer so it can rotate separately later - kept static here)
def mortar_tub():
    img = Img(24, 24)
    img.disc(0, 0, 9, C("#1a1a20"))
    img.disc(0, 0, 8, C("#3a3a44"))
    img.disc(0, 0, 6, C("#14141a"))
    img.disc(-0.5, 0.5, 4.2, C("#07070a"))
    for a in range(0, 360, 30):
        img.put(8.3 * math.cos(math.radians(a)), 8.3 * math.sin(math.radians(a)), C("#a58a4a"))
    img.save("mortar_tub")
    return img


mortar_tub()


# ----------------------------------------------------------------------------------------------
#  crew, guns, shots
# ----------------------------------------------------------------------------------------------
def make_crew(name, shirt, hat):
    img = Img(14, 14)
    img.disc(0, -0.5, 4.2, mul(shirt, 0.75))                 # shoulders
    img.disc(0, 0, 3.6, shirt)
    img.rect(-1.5, 1.5, 1.5, 3.2, mul(shirt, 0.9))
    img.disc(0.2, 0.4, 2.9, C("#e2b48a"))                    # head
    for dx in range(-3, 4):                                  # bandana / hat covers the back of the head
        for dy in range(-3, 4):
            if dx * dx + dy * dy <= 8.4 and dx <= 0.2:
                img.put(dx, dy + 0.4, hat)
    img.put(1.6, 1.4, C("#1a1a1a"))
    img.put(1.6, -0.4, C("#1a1a1a"))
    img.disc(-1.0, 3.9, 1.1, C("#e2b48a"))                   # hands
    img.disc(-1.0, -3.2, 1.1, C("#e2b48a"))
    img.outline(C("#12100e"))
    img.save(name)


make_crew("crew_a", C("#c43c3c"), C("#f2f2f2"))
make_crew("crew_b", C("#3c6ac4"), C("#2a2a2a"))
make_crew("crew_c", C("#3f9a58"), C("#b8322c"))
make_crew("crew_d", C("#c9a23a"), C("#222a44"))


def swivel():
    img = Img(22, 10)
    img.cx = 6.0
    for x in range(-2, 15):
        w = 2.0 if x < 10 else 2.6
        for y in range(-int(w), int(w) + 1):
            img.put(x, y, C("#4a4a54") if abs(y) < w else C("#2b2b33"))
    img.rect(13, -3, 15, 3, C("#8a7a48"))
    img.rect(13, -2, 14, 2, C("#c0aa66"))
    img.disc(0, 0, 3.0, C("#6a5a30"))
    img.disc(-0.3, 0.3, 1.8, C("#a8924c"))
    img.outline(C("#121014"))
    img.save("swivel_gun")


swivel()


def ball(name, core, rim, size=9, hot=False):
    img = Img(size, size)
    r = size / 2.0 - 0.8
    img.disc(0, 0, r, rim)
    img.disc(0, 0, r - 0.9, core)
    img.put(-1, 1, mul(core, 2.3))
    if hot:
        img.put(1, -1, C("#ffcf6a"))
    img.save(name)


ball("cannonball", C("#2c2c34"), C("#14141a"))
ball("cannonball_hot", C("#3a2a2a"), C("#8a2a1a"), hot=True)


def shell():
    img = Img(13, 13)
    img.disc(0, -0.5, 4.6, C("#101015"))
    img.disc(0, -0.5, 3.6, C("#2c2c36"))
    img.put(-1.5, 1.2, C("#6a6a7a"))
    img.line(1, 3.5, 3, 5, C("#a07a3a"))
    img.put(3.6, 5.4, C("#ffd24a"))
    img.put(4.4, 5.9, C("#ff7a2a"))
    img.save("mortar_shell")


shell()


def crate():
    img = Img(20, 20)
    img.rect(-8, -8, 8, 8, C("#9a6a38"))
    for k in (-6, -2, 2, 6):
        img.rect(-8, k, 8, k + 1, C("#7a5028"))
    img.rect(-8, -8, 8, -7, C("#5a3a1c"))
    img.rect(-8, 7, 8, 8, C("#5a3a1c"))
    img.rect(-8, -8, -7, 8, C("#5a3a1c"))
    img.rect(7, -8, 8, 8, C("#5a3a1c"))
    for (a, b) in ((-8, 8), (8, 8), (-8, -8), (8, -8)):
        pass
    img.line(-7, -7, 7, 7, C("#6a4220"))
    img.line(-7, 7, 7, -7, C("#6a4220"))
    img.rect(-1.5, -1.5, 1.5, 1.5, C("#e0b13a"))                   # gold clasp
    img.outline(C("#140d0a"))
    img.save("crate")


crate()


def barrel():
    img = Img(18, 18)
    img.disc(0, 0, 8, C("#16100c"))
    img.disc(0, 0, 7, C("#a8302a"))
    img.disc(0, 0, 5.2, C("#c4443a"))
    for r in (6.6, 3.6):
        for a in range(0, 360, 6):
            img.put(r * math.cos(math.radians(a)), r * math.sin(math.radians(a)), C("#3a3a42"))
    img.line(-2, -2, 2, 2, C("#ffd24a"))
    img.line(-2, 2, 2, -2, C("#ffd24a"))
    img.disc(0, 0, 1.0, C("#1a1a1a"))
    img.put(-3, 3, C("#ff9a8a"))
    img.save("barrel")


barrel()


def rock(name, seed, w, h):
    rnd = random.Random(seed)
    img = Img(w + 18, h + 18)
    ph = [rnd.random() * 6.28 for _ in range(4)]
    am = [0.12, 0.09, 0.06, 0.04]
    for j in range(img.h):
        for i in range(img.w):
            x = (i + 0.5 - img.cx) / (w / 2.0)
            y = (img.cy - (j + 0.5)) / (h / 2.0)
            a = math.atan2(y, x)
            r = math.sqrt(x * x + y * y)
            lim = 1.0 + sum(am[k] * math.sin((k + 2) * a + ph[k]) for k in range(4))
            foam = lim + 0.22
            if r <= lim:
                lit = 0.62 + 0.4 * (-x * 0.5 + y * 0.6) / 1.2
                col = mul(C("#7a7a82"), max(0.5, min(1.25, lit)))
                if r > lim - 0.12:
                    col = mul(C("#3a3a42"), 1.0)
                elif ((i * 7 + j * 13) % 11) == 0:
                    col = mul(col, 0.8)
                # moss on the shady side
                if y < -0.15 and ((i + j * 3) % 5) < 2 and r < lim - 0.18:
                    col = mix(col, C("#4c7a3c"), 0.7)
                img.set(i, j, col)
            elif r <= foam:
                t = (r - lim) / 0.22
                img.set(i, j, (225, 240, 250, int(150 * (1 - t) * (0.6 + 0.4 * math.sin(a * 9 + ph[0])))))
    img.save(name)


rock("rock_a", 21, 54, 40)
rock("rock_b", 22, 40, 34)
rock("rock_c", 23, 28, 24)


def harbor():
    """a little lagoon with a sandy beach rim; the ship is drawn on top, so it works wherever the dock ends up"""
    w, h = 200, 120
    img = Img(w, h)
    rnd = random.Random(31)
    ph = [rnd.random() * 6.28 for _ in range(3)]
    for j in range(h):
        for i in range(w):
            x = (i + 0.5 - w / 2.0) / (w / 2.0)
            y = (h / 2.0 - (j + 0.5)) / (h / 2.0)
            a = math.atan2(y, x)
            r = math.sqrt(x * x + y * y) / (1.0 + 0.04 * math.sin(3 * a + ph[0]) + 0.03 * math.sin(5 * a + ph[1]))
            if r > 1.0:
                continue
            if r > 0.9:
                col = C("#d8c48a") if (i + j) % 4 else C("#c8b27a")
                col = (col[0], col[1], col[2], int(255 * smooth(1.0, 0.93, r)))
            elif r > 0.84:
                col = C("#a9e0e6")
            else:
                d = r / 0.84
                col = mix(C("#2f8fb8"), C("#0e4a7a"), d)
                if (i * 3 + j * 5) % 23 == 0 and r < 0.8:
                    col = mix(col, C("#e8f6ff"), 0.45)
            img.set(i, j, col)
    img.save("harbor")


harbor()


def pier():
    img = Img(70, 18)
    for x in range(-33, 33):
        for y in range(-6, 6):
            col = C("#8a5a30") if (x // 4) % 2 == 0 else C("#9a6a3a")
            if y == -6 or y == 5:
                col = C("#5a381c")
            if x % 4 == 0:
                col = mul(col, 0.8)
            img.put(x, y, col)
    for px in (-31, -12, 8, 28):
        for sgn in (-1, 1):
            img.disc(px, sgn * 6.5, 1.8, C("#3b2616"))
            img.put(px - 0.5, sgn * 6.5 + 0.5, C("#7a5230"))
    img.outline(C("#140d0a"))
    img.save("pier")


pier()


# ----------------------------------------------------------------------------------------------
#  HUD icons (16x16, drawn big with point filtering)
# ----------------------------------------------------------------------------------------------
def icon(name, painter):
    img = Img(16, 16)
    painter(img)
    img.outline(C("#0e0a14"))
    img.save(name)


def p_dash(img):
    for k in range(3):
        for t in range(-4, 1):
            img.put(-4 + k * 3 + t, t, C("#7ae0ff"))
            img.put(-4 + k * 3 + t, -t, C("#7ae0ff"))
    for x in range(-5, 5):
        img.put(x, 0, C("#bff4ff"))


def p_fire(img):
    img.disc(-2, -2, 3.4, C("#2c2c36"))
    img.put(-3, -1, C("#8a8a9a"))
    for a in range(0, 360, 45):
        img.line(2.5 * math.cos(math.radians(a)) + 3, 2.5 * math.sin(math.radians(a)) + 3, 5 * math.cos(math.radians(a)) + 3, 5 * math.sin(math.radians(a)) + 3, C("#ffb02e"))
    img.disc(3, 3, 1.6, C("#fff0a0"))


def p_blast(img):
    for a in range(0, 360, 30):
        r = 6.8 if (a // 30) % 2 == 0 else 4.2
        img.line(0, 0, r * math.cos(math.radians(a)), r * math.sin(math.radians(a)), C("#ff6a2a"))
    img.disc(0, 0, 3.4, C("#ffd24a"))
    img.disc(0, 0, 1.8, C("#ffffff"))


def p_crew(img):
    img.disc(0, -2.5, 4.5, C("#c43c3c"))
    img.disc(0, 2, 3.0, C("#e2b48a"))
    for dx in range(-3, 4):
        img.put(dx, 3.6, C("#f2f2f2"))


def p_hull(img):
    for x in range(-7, 7):
        hw = max(0.0, 4.5 * (1 - (max(0, x - 1) / 6.0) ** 1.6))
        for y in range(-int(hw), int(hw) + 1):
            img.put(x, y, C("#b9803f"))
    img.line(-1, -6, -1, 6, C("#efe3c4"))
    img.line(-2, 5, 4, 0, C("#efe3c4"))


def p_anchor(img):
    img.line(0, 6, 0, -5, C("#c8d2e0"))
    img.line(-3, 3, 3, 3, C("#c8d2e0"))
    img.disc(0, 6.5, 1.3, C("#c8d2e0"))
    for a in range(200, 341, 10):
        img.put(5 * math.cos(math.radians(a)), -1 + 5 * math.sin(math.radians(a)), C("#c8d2e0"))


def p_wave(img):
    for k in range(3):
        for x in range(-7, 7):
            img.put(x, 3 - k * 3 + 1.4 * math.sin(x * 0.8 + k), C("#8ad0ff"))


icon("ico_dash", p_dash)
icon("ico_fire", p_fire)
icon("ico_blast", p_blast)
icon("ico_crew", p_crew)
icon("ico_hull", p_hull)
icon("ico_anchor", p_anchor)
icon("ico_wave", p_wave)

# ----------------------------------------------------------------------------------------------
#  preview sheet
# ----------------------------------------------------------------------------------------------
if PREVIEW:
    sheet = Image.new("RGBA", (980, 760), (14, 56, 100, 255))
    y = 10
    x = 10
    for name in ("player", "frigate", "gunboat", "sloop", "mortar", "galleon"):
        hull, rig = SHIPS[name]
        comp = Image.alpha_composite(hull.im, rig.im)
        comp = comp.resize((comp.width * 2, comp.height * 2), Image.NEAREST)
        if x + comp.width > 960:
            x = 10
            y += 190
        sheet.alpha_composite(comp, (x, y))
        x += comp.width + 12
    y += 200
    x = 10
    for name in ("crew_a", "crew_b", "crew_c", "crew_d", "swivel_gun", "cannonball", "cannonball_hot", "mortar_shell", "crate", "barrel", "mortar_tub",
                 "rock_a", "rock_b", "rock_c", "ico_dash", "ico_fire", "ico_blast", "ico_crew", "ico_hull", "ico_anchor", "ico_wave"):
        im = Image.open(os.path.join(OUT, name + ".png"))
        im = im.resize((im.width * 4, im.height * 4), Image.NEAREST)
        if x + im.width > 960:
            x = 10
            y += 120
        sheet.alpha_composite(im, (x, y))
        x += im.width + 12
    y += 130
    hb = Image.open(os.path.join(OUT, "harbor.png"))
    pr = Image.open(os.path.join(OUT, "pier.png"))
    sheet.alpha_composite(hb, (10, y))
    sheet.alpha_composite(pr.resize((pr.width * 2, pr.height * 2), Image.NEAREST), (230, y + 20))
    pl = Image.alpha_composite(SHIPS["player"][0].im, SHIPS["player"][1].im)
    sheet.alpha_composite(pl, (400, y))
    sheet.convert("RGB").save(PREVIEW)
    print("preview ->", PREVIEW)

print("sprites written to", OUT)
