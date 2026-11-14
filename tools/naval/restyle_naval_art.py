"""Run AFTER gen_naval_art.py: pulls the naval sprites into the same muted palette as the Cainos tiles (olive grass, grey stone)."""
import sys, os, colorsys
from PIL import Image
import numpy as np
D = sys.argv[1]
GRADE = ['ship_', 'pier', 'rock_', 'barrel', 'crate', 'crew_', 'swivel_gun', 'mortar_tub', 'harbor']
for fn in sorted(os.listdir(D)):
    if not fn.endswith('.png') or not any(fn.startswith(g) for g in GRADE): continue
    p = os.path.join(D, fn)
    im = Image.open(p).convert('RGBA'); a = np.array(im).astype(np.float32) / 255.0
    rgb = a[..., :3]; al = a[..., 3]
    mx = rgb.max(-1); mn = rgb.min(-1)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    lum = (0.3 * rgb[..., 0] + 0.59 * rgb[..., 1] + 0.11 * rgb[..., 2])[..., None]
    if fn == 'harbor.png':
        # sandy rim (warm, bright) -> grey stone like the Cainos stone tiles
        warm = (rgb[..., 0] > rgb[..., 2] + 0.12) & (al > 0.05)
        stone = np.clip(lum * 0.78 + 0.02, 0, 1) * np.array([1.0, 1.0, 1.04])
        rgb = np.where(warm[..., None], stone, rgb)
    k = 0.62                                              # keep ~60% of the saturation
    rgb = lum + (rgb - lum) * k
    rgb = rgb * 0.94 + np.array([0.012, 0.016, 0.0])      # a touch darker, slightly olive
    a[..., :3] = np.clip(rgb, 0, 1)
    Image.fromarray((a * 255 + 0.5).astype(np.uint8), 'RGBA').save(p)
    print('graded', fn)
