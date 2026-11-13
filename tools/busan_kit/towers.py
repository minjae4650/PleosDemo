"""Glass towers: a curtain-wall texture (panes, mullions, a band at every floor) and a tower drawn with it.

A tall tower is seen from 50 m to a kilometre away, never from arm's length: its street level is a podium built like any shop
building (frontage.building), and everything above is one sheet of glass per face. The glass is a mirror (the reflection
probe gives it the sky and the street), so the pattern only has to say "storeys and panes"."""
import math

import numpy as np
from PIL import Image, ImageDraw
from shapely.geometry import Polygon

from .materials import TEX, surface
from .mesh import flat

TILE = 6.6                                   # metres a tile covers each way: four panes of 1.65 m, two storeys of 3.3 m
MATERIAL = {'curtain': surface(TEX + 'curtain.png', '', (1, 1, 1), 0.93, 0.86, alpha=True)}


def make_tower_textures(tex_dir):
    n = 256
    rng = np.random.default_rng(7)
    img = np.zeros((n, n, 4), dtype=np.uint8)
    for r in range(2):                       # storeys
        for c in range(4):                   # panes
            shade = int(rng.integers(-9, 10))
            x0, x1, y0, y1 = c * n // 4, (c + 1) * n // 4, r * n // 2, (r + 1) * n // 2
            img[y0:y1, x0:x1] = (128 + shade, 141 + shade, 154 + shade, 255)
    pic = Image.fromarray(img, 'RGBA')
    d = ImageDraw.Draw(pic)
    for r in range(2):                       # the band at the floor slab: duller, a little lighter
        y = r * n // 2
        d.rectangle((0, y, n, y + 30), fill=(150, 158, 164, 150))
        d.rectangle((0, y + 30, n, y + 33), fill=(70, 74, 78, 120))
    for c in range(4):                       # mullions
        x = c * n // 4
        d.rectangle((x - 1, 0, x + 2, n), fill=(66, 70, 75, 110))
    pic.save(tex_dir / 'curtain.png')


def rounded(cx, cz, rx, rz, turn=0.0, lobes=0, bulge=0.0, n=40):
    """A smooth footprint: an ellipse, or with `lobes` a rounded triangle (3) or rounded square (4), turned by `turn` radians."""
    pts = []
    for k in range(n):
        t = 2 * math.pi * k / n
        r = 1.0 + (bulge * math.cos(lobes * t) if lobes else 0.0)
        x, z = rx * r * math.cos(t), rz * r * math.sin(t)
        pts.append((cx + x * math.cos(turn) - z * math.sin(turn), cz + x * math.sin(turn) + z * math.cos(turn)))
    return Polygon(pts)


def glass_tower(kit, footprint, y0, top, tint, crown=0.12):
    from . import frontage
    frontage.PLACED.append(footprint)
    """One sheet of glass per face from y0 to `top`; the last `crown` share of the height is set back, with a flat cap."""
    M = kit.M
    y1 = top - (top - y0) * crown
    M['curtain'].walls(footprint, y0, y1, tint, TILE)
    M['roof'].polygon(footprint, flat(y1), (0.62, 0.63, 0.64))
    if crown > 0:
        inner = footprint.buffer(-min(3.5, math.sqrt(footprint.area) * 0.12))
        if not inner.is_empty and inner.geom_type == 'Polygon':
            M['curtain'].walls(inner, y1, top, tuple(c * 0.92 for c in tint), TILE)
            M['roof'].polygon(inner, flat(top), (0.62, 0.63, 0.64))
    band = footprint.buffer(0.35)                                    # a lip where the tower meets its podium
    M['stone'].walls(band, y0 - 0.2, y0 + 0.9, (0.80, 0.80, 0.78), 2.0)
    M['stone'].polygon(band.difference(footprint.buffer(-0.2)), flat(y0 + 0.9), (0.80, 0.80, 0.78), 2.0)
