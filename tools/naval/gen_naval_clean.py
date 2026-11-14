#!/usr/bin/env python3
"""
Clean (non-pixel) ship art for the sea battles.

Draws every ship as smooth, anti-aliased, softly shaded vector-style sprites at 64 pixels per world unit
(4x the old pixel sprites, same world size), in the muted palette of the land tiles.

usage:  python3 gen_naval_clean.py <Assets/Resources/Sprites dir> [preview.png]
Run AFTER gen_naval_art.py (it overwrites ship_*_hull.png / ship_*_rig.png).
"""
import sys, os, math, random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1] if len(sys.argv) > 1 else "."
PREVIEW = sys.argv[2] if len(sys.argv) > 2 else None
SS = 2            # extra supersampling on top of the 4x sprite size


def hexc(h, a=255):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(4))


def shade(c, k):
    return (max(0, min(255, int(c[0] * k))), max(0, min(255, int(c[1] * k))), max(0, min(255, int(c[2] * k))), c[3])


class Canvas:
    """Draws in sprite pixel coordinates (4x size); internally supersampled."""

    def __init__(self, w, h):
        self.w, self.h = w, h
        self.img = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img, "RGBA")

    def _c(self, fn):
        layer = Image.new("RGBA", self.img.size, (0, 0, 0, 0))
        fn(ImageDraw.Draw(layer))
        self.img.alpha_composite(layer)

    def poly(self, pts, fill, outline=None, width=1):
        p = [(x * SS, y * SS) for x, y in pts]
        def f(d):
            if fill is not None: d.polygon(p, fill=fill)
            if outline is not None:
                d.line(p + [p[0]], fill=outline, width=max(1, int(width * SS)), joint="curve")
        self._c(f)

    def line(self, pts, fill, width):
        p = [(x * SS, y * SS) for x, y in pts]
        self._c(lambda d: d.line(p, fill=fill, width=max(1, int(width * SS)), joint="curve"))

    def ellipse(self, cx, cy, rx, ry, fill, outline=None, width=1):
        self._c(lambda d: d.ellipse([(cx - rx) * SS, (cy - ry) * SS, (cx + rx) * SS, (cy + ry) * SS], fill=fill,
                                    outline=outline, width=max(1, int(width * SS))))

    def rrect(self, x0, y0, x1, y1, r, fill, outline=None, width=1):
        self._c(lambda d: d.rounded_rectangle([x0 * SS, y0 * SS, x1 * SS, y1 * SS], radius=r * SS, fill=fill,
                                              outline=outline, width=max(1, int(width * SS))))

    def mask_of(self, fn):
        m = Image.new("L", self.img.size, 0)
        fn(ImageDraw.Draw(m))
        return m

    def blur_layer(self, layer, radius):
        return layer.filter(ImageFilter.GaussianBlur(radius * SS))

    def finish(self):
        im = self.img.resize((self.w, self.h), Image.LANCZOS)
        a = np.array(im).astype(np.float32)
        # bleed colour into the transparent border so bilinear filtering never shows a dark / white halo
        alpha = a[..., 3] > 0
        rgb = a[..., :3].copy()
        for _ in range(6):
            have = alpha.astype(np.float32)
            acc = np.zeros_like(rgb); cnt = np.zeros(alpha.shape, np.float32)
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    if dx == 0 and dy == 0: continue
                    acc += np.roll(np.roll(rgb * have[..., None], dy, 0), dx, 1)
                    cnt += np.roll(np.roll(have, dy, 0), dx, 1)
            fill = (~alpha) & (cnt > 0)
            rgb[fill] = acc[fill] / cnt[fill][:, None]
            alpha = alpha | fill
        a[..., :3] = rgb
        return Image.fromarray(np.clip(a + 0.5, 0, 255).astype(np.uint8), "RGBA")


# ---------------------------------------------------------------------------------------------------------------------
#  hull geometry
# ---------------------------------------------------------------------------------------------------------------------
def hull_pts(W, H, kind, scale_len=1.0, scale_w=1.0, inset=0.0, n=64):
    """outline polygon of a hull (bow = +x). inset 0..1 shrinks it towards the centre line."""
    cx, cy = W / 2.0, H / 2.0
    L = W * 0.46 * scale_len          # half length
    B = H * 0.43 * scale_w            # half beam
    bow_p, stern_p = kind["bow"], kind["stern"]
    top, bot = [], []
    for i in range(n + 1):
        u = -1.0 + 2.0 * i / n            # -1 stern .. +1 bow
        if u >= 0:
            w = (1.0 - u ** bow_p) ** kind["bowf"]
        else:
            w = (1.0 - (-u) ** stern_p) ** kind["sternf"]
        w = max(0.0, w)
        x = cx + u * L * (1.0 - inset * 0.35)
        hw = B * w * (1.0 - inset)
        top.append((x, cy - hw))
        bot.append((x, cy + hw))
    return top + bot[::-1]


def deck_clip(c, pts_fn):
    """mask of an inset polygon"""
    return c.mask_of(lambda d: d.polygon([(x * SS, y * SS) for x, y in pts_fn()], fill=255))


def paste_masked(c, layer, mask):
    c.img.paste(layer, (0, 0), mask)


def make_hull(name, W, H, k):
    c = Canvas(W, H)
    cx, cy = W / 2.0, H / 2.0
    pal = k["pal"]
    # soft contact shadow ring (water is dark underneath)
    sh = Image.new("RGBA", c.img.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).polygon([(x * SS + 2 * SS, y * SS + 3 * SS) for x, y in hull_pts(W, H, k, 1.02, 1.04)], fill=(0, 12, 22, 110))
    c.img.alpha_composite(c.blur_layer(sh, W * 0.012))

    # outline + hull side gradient (edge dark -> centre light)
    outline = shade(pal["hull"], 0.34)
    c.poly(hull_pts(W, H, k, 1.0, 1.0), outline)
    steps = 14
    for i in range(steps):
        t = i / (steps - 1.0)
        col = mix(shade(pal["hull"], 0.78), shade(pal["hull"], 1.12), t)
        c.poly(hull_pts(W, H, k, 0.985, 0.985, inset=0.012 + 0.07 * t), col)

    # planking: curved strakes following the hull
    rnd = random.Random(k["seed"])
    for j in range(7):
        ins = 0.02 + 0.115 * j / 6.0
        pts = hull_pts(W, H, k, 0.985, 0.985, inset=ins)
        half = len(pts) // 2
        c.line(pts[:half], shade(pal["hull"], 0.66 + 0.05 * rnd.random()), H * 0.004)
        c.line(pts[half:][::-1], shade(pal["hull"], 0.66 + 0.05 * rnd.random()), H * 0.004)

    # rail (lighter strip at the edge of the deck)
    rail_out = hull_pts(W, H, k, 0.975, 0.975, inset=0.115)
    rail_in = hull_pts(W, H, k, 0.975, 0.975, inset=0.19)
    c.poly(rail_out, pal["rail"], shade(pal["rail"], 0.55), H * 0.006)
    c.poly(rail_in, shade(pal["rail"], 0.8))
    if pal.get("trim"):
        tr = hull_pts(W, H, k, 0.98, 0.98, inset=0.095)
        c.line(tr[:len(tr) // 2], pal["trim"], H * 0.012)
        c.line(tr[len(tr) // 2:][::-1], pal["trim"], H * 0.012)

    # deck with planks along the length
    deck_pts = hull_pts(W, H, k, 0.97, 0.97, inset=0.20)
    dm = c.mask_of(lambda d: d.polygon([(x * SS, y * SS) for x, y in deck_pts], fill=255))
    deck = Image.new("RGBA", c.img.size, pal["deck"])
    dd = ImageDraw.Draw(deck, "RGBA")
    plank_h = H * 0.034
    y = cy - H * 0.45
    idx = 0
    while y < cy + H * 0.45:
        col = shade(pal["deck"], 0.93 + 0.1 * ((idx * 7) % 5) / 5.0)
        dd.rectangle([0, y * SS, W * SS, (y + plank_h) * SS], fill=col)
        dd.line([(0, y * SS), (W * SS, y * SS)], fill=shade(pal["deck"], 0.62), width=max(1, int(H * 0.0035 * SS)))
        # staggered butt joints
        xx = (idx * 53) % 90 + 20
        while xx < W:
            dd.line([(xx * SS, y * SS), (xx * SS, (y + plank_h) * SS)], fill=shade(pal["deck"], 0.66), width=max(1, int(H * 0.003 * SS)))
            xx += 90 + (idx * 17) % 40
        y += plank_h
        idx += 1
    # lighting: lighter on the upper (north) half, darker on the lower half
    grad = Image.new("RGBA", c.img.size, (0, 0, 0, 0))
    gd = ImageDraw.Draw(grad, "RGBA")
    for yy in range(0, c.img.size[1], 2):
        t = yy / float(c.img.size[1])
        a = int(46 * (t - 0.5) * 2) if t > 0.5 else 0
        b = int(34 * (0.5 - t) * 2) if t < 0.5 else 0
        gd.line([(0, yy), (c.img.size[0], yy)], fill=(0, 0, 0, a) if a else (255, 255, 255, b), width=2)
    deck.alpha_composite(grad)
    paste_masked(c, deck, dm)
    c.poly(deck_pts, None, shade(pal["deck"], 0.45), H * 0.007)

    k["details"](c, W, H, k, pal)

    # cannon ports + side rubbing strake
    for side in (-1, 1):
        for i in range(k["ports"]):
            u = -0.38 + 0.78 * (i + 0.5) / max(1, k["ports"])
            x = cx + u * W * 0.46
            wv = (1 - abs(u) ** 2.3) ** 0.7 if u > 0 else (1 - abs(u) ** 3.2) ** 0.8
            y = cy + side * (H * 0.43 * wv * 0.80)
            c.rrect(x - W * 0.013, y - H * 0.022, x + W * 0.013, y + H * 0.022, 3, shade(pal["hull"], 0.25))
            c.rrect(x - W * 0.010, y - H * 0.014, x + W * 0.010, y + H * 0.014, 2, hexc("#14100e"))
    # bowsprit
    bx = cx + W * 0.46
    c.poly([(bx - 6, cy - H * 0.018), (W * 0.992, cy - H * 0.006), (W * 0.992, cy + H * 0.006), (bx - 6, cy + H * 0.018)],
           shade(pal["rail"], 0.9), shade(pal["rail"], 0.45), 2)
    return c.finish()


# ---------------------------------------------------------------------------------------------------------------------
#  per-kind deck details
# ---------------------------------------------------------------------------------------------------------------------
def mast_base(c, x, y, r, pal):
    c.ellipse(x, y, r * 1.35, r * 1.35, shade(pal["deck"], 0.55))
    c.ellipse(x, y, r, r, shade(pal["rail"], 0.9), shade(pal["rail"], 0.4), 2)


def hatch(c, x, y, w, h, pal):
    c.rrect(x - w / 2, y - h / 2, x + w / 2, y + h / 2, 3, shade(pal["deck"], 0.78), shade(pal["deck"], 0.4), 2)
    for i in range(1, 4):
        xx = x - w / 2 + w * i / 4
        c.line([(xx, y - h / 2 + 3), (xx, y + h / 2 - 3)], shade(pal["deck"], 0.5), 1.5)


def cabin(c, x, y, w, h, pal, roof=None):
    roof = roof or shade(pal["rail"], 1.0)
    c.rrect(x - w / 2 + 3, y - h / 2 + 4, x + w / 2 + 3, y + h / 2 + 4, 6, (0, 0, 0, 70))
    c.rrect(x - w / 2, y - h / 2, x + w / 2, y + h / 2, 6, shade(roof, 0.8), shade(roof, 0.4), 2.5)
    c.rrect(x - w / 2 + 6, y - h / 2 + 6, x + w / 2 - 6, y + h / 2 - 6, 4, roof)
    for i in range(1, 6):
        xx = x - w / 2 + w * i / 6
        c.line([(xx, y - h / 2 + 7), (xx, y + h / 2 - 7)], shade(roof, 0.72), 1.5)


def coil(c, x, y, r, pal):
    for i in range(3):
        c.ellipse(x, y, r - i * r * 0.28, r - i * r * 0.28, None, hexc("#c8b68a"), 2)


def det_player(c, W, H, k, pal):
    cx, cy = W / 2.0, H / 2.0
    cabin(c, cx - W * 0.33, cy, W * 0.16, H * 0.46, pal, shade(pal["deck"], 1.04))
    c.ellipse(cx - W * 0.33, cy, 6, 6, hexc("#e8d9a0"))        # lantern
    hatch(c, cx + W * 0.05, cy, W * 0.07, H * 0.22, pal)
    hatch(c, cx + W * 0.20, cy, W * 0.05, H * 0.16, pal)
    mast_base(c, cx - W * 0.10, cy, H * 0.045, pal)
    mast_base(c, cx + W * 0.28, cy, H * 0.04, pal)
    coil(c, cx + W * 0.38, cy - H * 0.12, 11, pal)
    c.ellipse(cx + W * 0.40, cy + H * 0.13, 14, 14, hexc("#3b3b40"), hexc("#18181c"), 2)   # anchor winch


def det_sloop(c, W, H, k, pal):
    cx, cy = W / 2.0, H / 2.0
    mast_base(c, cx, cy, H * 0.06, pal)
    hatch(c, cx - W * 0.20, cy, W * 0.10, H * 0.26, pal)
    c.rrect(cx - W * 0.40, cy - H * 0.10, cx - W * 0.30, cy + H * 0.10, 4, shade(pal["rail"], 0.8), shade(pal["rail"], 0.4), 2)
    coil(c, cx + W * 0.27, cy + H * 0.08, 8, pal)


def det_gunboat(c, W, H, k, pal):
    cx, cy = W / 2.0, H / 2.0
    mast_base(c, cx - W * 0.06, cy, H * 0.055, pal)
    c.ellipse(cx + W * 0.28, cy, H * 0.17, H * 0.17, shade(pal["rail"], 0.7), shade(pal["rail"], 0.35), 3)   # bow gun mount
    c.ellipse(cx + W * 0.28, cy, H * 0.10, H * 0.10, hexc("#2c2c31"), hexc("#111114"), 2)
    hatch(c, cx - W * 0.24, cy, W * 0.10, H * 0.24, pal)
    cabin(c, cx - W * 0.38, cy, W * 0.10, H * 0.28, pal, shade(pal["deck"], 0.95))


def det_frigate(c, W, H, k, pal):
    cx, cy = W / 2.0, H / 2.0
    cabin(c, cx - W * 0.34, cy, W * 0.15, H * 0.44, pal, shade(pal["deck"], 1.0))
    for mx in (-0.05, 0.22):
        mast_base(c, cx + W * mx, cy, H * 0.05, pal)
    hatch(c, cx + W * 0.08, cy, W * 0.06, H * 0.20, pal)
    hatch(c, cx - W * 0.16, cy, W * 0.07, H * 0.22, pal)
    coil(c, cx + W * 0.36, cy + H * 0.13, 10, pal)


def det_mortar(c, W, H, k, pal):
    cx, cy = W / 2.0, H / 2.0
    c.ellipse(cx + W * 0.04, cy, H * 0.30, H * 0.30, shade(pal["deck"], 0.55), shade(pal["deck"], 0.3), 3)   # tub well
    c.ellipse(cx + W * 0.04, cy, H * 0.25, H * 0.25, hexc("#2b2b30"))
    hatch(c, cx - W * 0.30, cy, W * 0.09, H * 0.22, pal)
    c.rrect(cx - W * 0.44, cy - H * 0.12, cx - W * 0.36, cy + H * 0.12, 4, shade(pal["rail"], 0.8), shade(pal["rail"], 0.4), 2)
    for i in range(4):
        c.ellipse(cx + W * 0.30, cy - H * 0.18 + i * H * 0.12, 6, 6, hexc("#4b4b50"), hexc("#18181c"), 1.5)   # shell stack
    mast_base(c, cx - W * 0.12, cy - H * 0.2, H * 0.035, pal)


def det_galleon(c, W, H, k, pal):
    cx, cy = W / 2.0, H / 2.0
    # stern castle
    c.rrect(cx - W * 0.44, cy - H * 0.34, cx - W * 0.22, cy + H * 0.34, 10, shade(pal["rail"], 0.7), shade(pal["rail"], 0.35), 3)
    c.rrect(cx - W * 0.435, cy - H * 0.325, cx - W * 0.225, cy + H * 0.325, 8, pal["trim"] if pal.get("trim") else pal["rail"])
    c.rrect(cx - W * 0.425, cy - H * 0.31, cx - W * 0.235, cy + H * 0.31, 6, shade(pal["deck"], 0.95), shade(pal["deck"], 0.4), 2)
    for i in range(7):
        xx = cx - W * 0.42 + i * W * 0.027
        c.line([(xx, cy - H * 0.29), (xx, cy + H * 0.29)], shade(pal["deck"], 0.7), 2)
    for ly in (-0.26, 0.26):
        c.ellipse(cx - W * 0.43, cy + H * ly, 16, 16, hexc("#ffd98a", 90))
        c.ellipse(cx - W * 0.43, cy + H * ly, 7, 7, hexc("#ffe9b0"))
    # fore castle
    c.rrect(cx + W * 0.24, cy - H * 0.26, cx + W * 0.36, cy + H * 0.26, 8, shade(pal["deck"], 0.9), shade(pal["rail"], 0.45), 3)
    for mx in (-0.12, 0.05, 0.19):
        mast_base(c, cx + W * mx, cy, H * 0.05, pal)
    hatch(c, cx - W * 0.03, cy - H * 0.14, W * 0.05, H * 0.14, pal)
    hatch(c, cx - W * 0.03, cy + H * 0.14, W * 0.05, H * 0.14, pal)


# ---------------------------------------------------------------------------------------------------------------------
#  rigging (masts, sails, flags) - drawn on its own sprite so it can sway above the hull
# ---------------------------------------------------------------------------------------------------------------------
def sail(c, x, half_span, thick, col, edge, billow=1.0, emblem=None, stripe=None, torn=False):
    """top-down sail across the beam at mast x, bulging toward the bow (+x)"""
    cy = c.h / 2.0
    n = 28
    fore, aft = [], []
    for i in range(n + 1):
        t = -1 + 2.0 * i / n
        bulge = (1 - t * t) * thick * billow
        fore.append((x + bulge, cy + t * half_span))
        aft.append((x - thick * 0.10 * (1 - t * t), cy + t * half_span))
    pts = fore + aft[::-1]
    # shadow onto the deck
    shl = Image.new("RGBA", c.img.size, (0, 0, 0, 0))
    ImageDraw.Draw(shl).polygon([(px * SS + 6 * SS, py * SS + 9 * SS) for px, py in pts], fill=(0, 0, 0, 78))
    c.img.alpha_composite(c.blur_layer(shl, 4))
    c.poly(pts, shade(col, 0.88), shade(edge, 0.55), 3)
    # cloth with soft vertical shading
    m = c.mask_of(lambda d: d.polygon([(px * SS, py * SS) for px, py in pts], fill=255))
    cloth = Image.new("RGBA", c.img.size, col)
    ov = Image.new("RGBA", c.img.size, (0, 0, 0, 0))
    cd = ImageDraw.Draw(ov, "RGBA")
    for i in range(0, 40):
        t = i / 39.0
        xx = (x - thick * 0.2 + (thick * 1.25) * t) * SS
        a = int(60 * abs(t - 0.45) * 2)
        cd.line([(xx, 0), (xx, c.img.size[1])], fill=(0, 0, 0, a), width=max(1, int(thick * 0.04 * SS)))
    for i in range(1, 6):                      # panel seams
        yy = (cy - half_span + 2 * half_span * i / 6.0) * SS
        cd.line([(0, yy), (c.img.size[0], yy)], fill=(0, 0, 0, 34), width=max(1, int(2 * SS)))
    if stripe:
        cd.rectangle([0, (cy - half_span * 0.18) * SS, c.img.size[0], (cy + half_span * 0.18) * SS], fill=stripe)
    cloth.alpha_composite(ov)
    c.img.paste(cloth, (0, 0), m)
    if emblem == "ring":
        c.ellipse(x + thick * 0.45, cy, half_span * 0.34, half_span * 0.34, None, hexc("#e6b830"), half_span * 0.07)
    elif emblem == "skull":
        r = half_span * 0.2
        ex = x + thick * 0.45
        c.ellipse(ex, cy - r * 0.15, r, r * 0.95, hexc("#e8e4d6"))
        c.rrect(ex - r * 0.55, cy + r * 0.6, ex + r * 0.55, cy + r * 1.2, 3, hexc("#e8e4d6"))
        c.ellipse(ex - r * 0.38, cy - r * 0.2, r * 0.26, r * 0.3, hexc("#18181c"))
        c.ellipse(ex + r * 0.38, cy - r * 0.2, r * 0.26, r * 0.3, hexc("#18181c"))
    if torn:
        for tt in (-0.55, 0.1, 0.62):
            c.poly([(x + thick * 0.2, cy + tt * half_span), (x + thick * 1.1, cy + tt * half_span + 6), (x + thick * 0.2, cy + tt * half_span + 12)],
                   (0, 0, 0, 0), None)
    # yard arm
    c.line([(x - thick * 0.05, cy - half_span - 5), (x - thick * 0.05, cy + half_span + 5)], hexc("#5b4128"), 5)


def mast(c, x, r, flag_col=None, flag_len=60):
    cy = c.h / 2.0
    c.ellipse(x + 3, cy + 4, r * 1.2, r * 1.2, (0, 0, 0, 70))
    c.ellipse(x, cy, r * 1.15, r * 1.15, hexc("#2f2218"))
    c.ellipse(x, cy, r, r, hexc("#8a6a44"), hexc("#3b2a1a"), 2)
    c.ellipse(x - r * 0.25, cy - r * 0.25, r * 0.35, r * 0.35, hexc("#b79868"))
    if flag_col:
        c.poly([(x, cy - r * 0.6), (x - flag_len, cy - r * 1.9), (x - flag_len * 0.85, cy - r * 0.6)], flag_col, shade(flag_col, 0.5), 2)


def make_rig(name, W, H, k):
    c = Canvas(W, H)
    cx, cy = W / 2.0, H / 2.0
    pal = k["pal"]
    for (mx, span, thick, emblem) in k["sails"]:
        sail(c, cx + W * mx, H * span, W * thick, pal["sail"], pal["sail_edge"], k.get("billow", 1.0),
             emblem=emblem, stripe=pal.get("sail_stripe"), torn=k.get("torn", False))
    for (mx, r) in k["masts"]:
        mast(c, cx + W * mx, H * r, pal.get("flag") if mx == k["masts"][0][0] else None, W * 0.12)
    for (lx, ly) in k.get("lanterns", []):
        c.ellipse(cx + W * lx, cy + H * ly, 14, 14, hexc("#ffd98a", 80))
        c.ellipse(cx + W * lx, cy + H * ly, 6, 6, hexc("#fff0c0"))
    return c.finish()


# ---------------------------------------------------------------------------------------------------------------------
#  the six ships (sizes = old pixel sprite size x4, so world size is unchanged)
# ---------------------------------------------------------------------------------------------------------------------
SHIPS = {
    "player": dict(size=(496, 240), seed=1, bow=2.1, bowf=0.72, stern=3.4, sternf=0.62, ports=3, details=det_player,
                   pal=dict(hull=hexc("#7b5434"), rail=hexc("#a47a4e"), deck=hexc("#b58f5e"), trim=hexc("#3f7399"),
                            sail=hexc("#e8e1cb"), sail_edge=hexc("#a69f88"), sail_stripe=None, flag=hexc("#3f7399")),
                   masts=[(-0.10, 0.045), (0.28, 0.04)], sails=[(-0.07, 0.40, 0.085, "ring"), (0.31, 0.33, 0.07, None)],
                   lanterns=[(-0.33, 0.0)]),
    "sloop": dict(size=(256, 136), seed=2, bow=2.0, bowf=0.8, stern=3.0, sternf=0.6, ports=1, details=det_sloop,
                  pal=dict(hull=hexc("#4a3a30"), rail=hexc("#6c5240"), deck=hexc("#8c6c4a"), trim=hexc("#8c3328"),
                           sail=hexc("#5b4f4a"), sail_edge=hexc("#2f2724"), sail_stripe=None, flag=hexc("#2a2a30")),
                  masts=[(0.0, 0.07)], sails=[(0.03, 0.40, 0.11, "skull")], torn=True),
    "gunboat": dict(size=(320, 176), seed=3, bow=2.2, bowf=0.78, stern=3.2, sternf=0.6, ports=2, details=det_gunboat,
                    pal=dict(hull=hexc("#4f5b52"), rail=hexc("#74806f"), deck=hexc("#8d8b72"), trim=hexc("#b5a14e"),
                             sail=hexc("#c9bb92"), sail_edge=hexc("#8a7f5e"), sail_stripe=None, flag=hexc("#8c3328")),
                    masts=[(-0.06, 0.06)], sails=[(-0.03, 0.38, 0.09, None)]),
    "frigate": dict(size=(416, 208), seed=4, bow=2.1, bowf=0.74, stern=3.3, sternf=0.62, ports=4, details=det_frigate,
                    pal=dict(hull=hexc("#59392a"), rail=hexc("#8a5c3a"), deck=hexc("#9c7a52"), trim=hexc("#a8402f"),
                             sail=hexc("#d3c8aa"), sail_edge=hexc("#8f8467"), sail_stripe=hexc("#a8402f", 150), flag=hexc("#a8402f")),
                    masts=[(-0.05, 0.05), (0.22, 0.045)], sails=[(-0.02, 0.40, 0.085, None), (0.25, 0.33, 0.07, None)]),
    "mortar": dict(size=(304, 208), seed=5, bow=3.4, bowf=0.5, stern=3.6, sternf=0.5, ports=0, details=det_mortar,
                   pal=dict(hull=hexc("#484e55"), rail=hexc("#6b727a"), deck=hexc("#767066"), trim=None,
                            sail=hexc("#b9b09a"), sail_edge=hexc("#7f7866"), sail_stripe=None, flag=hexc("#8c3328")),
                   masts=[(-0.12, 0.035)], sails=[(-0.10, 0.18, 0.05, None)], billow=0.6, lanterns=[(-0.12, -0.2)]),
    "galleon": dict(size=(752, 328), seed=6, bow=2.0, bowf=0.7, stern=3.0, sternf=0.55, ports=6, details=det_galleon,
                    pal=dict(hull=hexc("#2f2521"), rail=hexc("#4d3a2e"), deck=hexc("#715840"), trim=hexc("#c3a246"),
                             sail=hexc("#2e2e36"), sail_edge=hexc("#8c2a24"), sail_stripe=hexc("#8c2a24", 150), flag=hexc("#8c2a24")),
                    masts=[(-0.12, 0.04), (0.05, 0.045), (0.19, 0.04)],
                    sails=[(-0.09, 0.44, 0.07, None), (0.08, 0.46, 0.08, "skull"), (0.22, 0.38, 0.06, None)],
                    lanterns=[(-0.43, -0.26), (-0.43, 0.26)]),
}

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    made = {}
    for nm, k in SHIPS.items():
        W, H = k["size"]
        hull = make_hull(nm, W, H, k)
        rig = make_rig(nm, W, H, k)
        hull.save(os.path.join(OUT, "ship_%s_hull.png" % nm))
        rig.save(os.path.join(OUT, "ship_%s_rig.png" % nm))
        made[nm] = (hull, rig)
        print("ship", nm, W, H)
    if PREVIEW:
        sheet = Image.new("RGBA", (1700, 900), (28, 88, 120, 255))
        x, y = 20, 20
        for nm, (h, r) in made.items():
            comp = h.copy(); comp.alpha_composite(r)
            if x + comp.width > 1680: x = 20; y += 340
            sheet.alpha_composite(comp, (x, y)); x += comp.width + 30
        sheet.save(PREVIEW)
        print("preview ->", PREVIEW)
