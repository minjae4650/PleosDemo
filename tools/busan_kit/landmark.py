"""Lettering sculptures: printed words standing upright, extruded.

Letter shapes: Noto Sans KR Bold (OFL), made a little heavier and otherwise left as printed. A printed syllable standing free
has strokes that would hang in the air, so every loose part gets a thin strut straight down (it ends in the part below it, or in
the base). Joining the parts instead was tried: the top of 광 closed up into something like ㅍ."""
import math

import mapbox_earcut
import numpy as np
from matplotlib.font_manager import FontProperties
from matplotlib.textpath import TextPath
from shapely import affinity
from shapely.geometry import Polygon, box
from shapely.geometry.polygon import orient
from shapely.ops import unary_union

from .mesh import polys

FONT = FontProperties(fname='C:/Windows/Fonts/NotoSansKR-Bold.ttf')


def glyph(text):
    """`text` as polygons in font units (size 100): x along the line, y up from the baseline."""
    shape = Polygon()
    for ring in sorted((Polygon(r).buffer(0) for r in TextPath((0, 0), text, size=100, prop=FONT).to_polygons() if len(r) >= 4), key=lambda g: -g.area):
        shape = shape.symmetric_difference(ring)       # even-odd: a ring inside another is a hole
    return shape


def lettering(text, ref, tall, heavy, strut, track):
    """`text` laid out in metres, scaled so that `ref` (the same syllables without spaces or commas) is `tall` high, thickened by
    `heavy` x tall, `track` x tall between syllables, a space 0.28 x tall; loose parts get a `strut` wide leg down to y = 0.
    Returns the shape (x from 0 along the line, y up), the number of legs, and the pieces per syllable after the legs."""
    TALL, HEAVY, STRUT, TRACK = tall, heavy, strut, track
    ref = glyph(ref)
    k = TALL / (ref.bounds[3] - ref.bounds[1])
    foot = ref.bounds[1] * k
    parts, x, legs, loose = [], 0.0, 0, []
    for ch in text:
        if ch == ' ':
            x += 0.28 * TALL
            continue
        g = affinity.scale(glyph(ch), k, k, origin=(0, 0))
        g = affinity.translate(g, x - g.bounds[0], -foot).buffer(HEAVY * TALL, join_style='mitre')
        if ch != ',':
            for piece in polys(g):
                if piece.bounds[1] > 0.15:                 # still in the air: a leg from its lowest part down to the base
                    low = piece.intersection(box(-1e4, piece.bounds[1], 1e4, piece.bounds[1] + 0.12))
                    cx = low.centroid.x
                    g = g.union(box(cx - STRUT / 2, 0.0, cx + STRUT / 2, piece.bounds[1] + 0.12))
                    legs += 1
            loose.append(len(polys(g)))
        parts.append(g.intersection(box(-1e4, -0.5, 1e4, 1e4)))
        x = g.bounds[2] + TRACK * TALL
    word = unary_union(parts).simplify(0.01)
    word = affinity.translate(word, -word.bounds[0], 0.0)
    return word, legs, loose


def stand(mesh, basis, shape, t0, y0, d0, depth, front_col, side_col):
    """An upright shape: t along the word, y up, d toward the reader; its face at d0, extruded `depth` back."""
    ox, oz, rx, rz, nx, nz = basis

    def at(t, y, d):
        return (ox + rx * (t0 + t) + nx * d, y0 + y, oz + rz * (t0 + t) + nz * d)
    for g in polys(shape):
        g = orient(g, 1.0)
        rings = [list(g.exterior.coords)[:-1]] + [list(h.coords)[:-1] for h in g.interiors]
        pts, ends = [], []
        for r in rings:
            pts += r
            ends.append(len(pts))
        arr = np.array(pts, dtype=np.float64)
        idx = mapbox_earcut.triangulate_float64(arr, np.array(ends, dtype=np.uint32))
        i0 = mesh._add([at(q[0], q[1], d0) for q in arr], [(q[0] / 2.0, q[1] / 2.0) for q in arr], front_col)
        for j in range(0, len(idx), 3):
            a, b, c = int(idx[j]), int(idx[j + 1]), int(idx[j + 2])
            pa, pb, pc = at(arr[a][0], arr[a][1], d0), at(arr[b][0], arr[b][1], d0), at(arr[c][0], arr[c][1], d0)
            e1, e2 = (pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2]), (pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2])
            cross = (e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0])
            if cross[0] * nx + cross[2] * nz < 0:      # Unity front faces: cross(b - a, c - a) points at the viewer
                b, c = c, b
            mesh.t += [i0 + a, i0 + b, i0 + c]
        for edge in ([g.exterior] + list(g.interiors) if depth > 0 else []):   # the sides
            c = list(edge.coords)
            for (ax, ay), (bx, by) in zip(c, c[1:]):
                L = math.hypot(bx - ax, by - ay)
                if L < 1e-4:
                    continue
                ox2, oy2 = (by - ay) / L, -(bx - ax) / L                   # out of the shape
                mesh.quad(at(ax, ay, d0 - depth), at(bx, by, d0 - depth), at(bx, by, d0), at(ax, ay, d0), (rx * ox2, oy2, rz * ox2), col=side_col)


def sculpture(kit, word, cx0, cz0, ux, uz, foot_top, depth, col, side_col):
    """`word` (from `lettering`) standing centred on (cx0, cz0), along (ux, uz), read from the left of that direction (the road
    when it stands on the sea side), `depth` deep; a flat mirrored face for the other side; on a stone footing up to `foot_top`.
    The faces go to the self-lit 'landmark' material: the real letters are lit, and they look away from the sun here."""
    total = word.bounds[2]
    front = (cx0 + ux * total / 2, cz0 + uz * total / 2, -ux, -uz, -uz, ux)    # read from the road: origin, the reader's right, the face normal
    back = (cx0 - ux * total / 2, cz0 - uz * total / 2, ux, uz, uz, -ux)       # the same thing seen from the water
    stand(kit.M['landmark'], front, word, 0.0, foot_top - 0.03, depth / 2, depth, col, side_col)
    stand(kit.M['landmark'], back, affinity.translate(affinity.scale(word, -1.0, 1.0, origin=(0, 0)), total, 0.0), 0.0, foot_top - 0.03, depth / 2, 0.0, col, side_col)
    kit.M['stone'].obox(cx0, cz0, ux, uz, total / 2 + 0.6, depth / 2 + 0.45, 0.0, foot_top, (0.72, 0.72, 0.70), 1.5)
