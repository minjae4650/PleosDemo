"""Buildings beside the road on their real footprints: modelled near the road (shop fronts, recessed windows, signs, parapet),
boxes with a window texture farther away."""
import math

from shapely.geometry import Point, Polygon
from shapely.geometry.polygon import orient

from .geo import proj
from .mesh import Face, flat

FLOOR = 3.3

WALLS = [(0.80, 0.78, 0.74), (0.70, 0.69, 0.66), (0.60, 0.60, 0.61), (0.76, 0.70, 0.60), (0.48, 0.49, 0.52), (0.84, 0.84, 0.82), (0.44, 0.40, 0.36), (0.62, 0.50, 0.42),
         (0.30, 0.31, 0.33), (0.72, 0.74, 0.76), (0.55, 0.36, 0.30)]
# Every footprint a district builds on (building, block, busan_kit.towers.glass_tower) is noted here; tools/build_district.py
# writes them out, and districts/infill.py builds only where none stands.
PLACED = []
# Lines the driver drives (tools/build_district.py sets them for the landmark map): a far block nearer than MODEL_WITHIN m to
# one is modelled (windows, slab edges, parapet) instead of a box with a window picture, which read as a white box from the road.
DRIVEN = None
MODEL_WITHIN = 50.0
FABRIC = [(0.70, 0.12, 0.12), (0.12, 0.25, 0.45), (0.12, 0.38, 0.25), (0.85, 0.82, 0.74), (0.85, 0.50, 0.12), (0.16, 0.16, 0.17)]


def sign_uv(i):
    c, r = i % 4, i // 4
    return ((c / 4, 1 - (r + 1) / 8), ((c + 1) / 4, 1 - (r + 1) / 8), ((c + 1) / 4, 1 - r / 8), (c / 4, 1 - r / 8))


def window(M, F, s0, s1, y0, y1, depth, rng, frame_col, mull=1.4):
    """A recessed window: reveals, glass, frame strips."""
    F.sides(M['wall'], s0, s1, y0, y1, 0.0, -depth, F.col, inward=True)
    g = rng.choice([(1, 1, 1), (0.8, 0.9, 1.0), (1.1, 1.05, 0.95), (0.7, 0.75, 0.8), (1.2, 1.1, 0.9)])
    F.q(M['glass'], s0, s1, y0, y1, -depth, g)
    d, w = -depth + 0.025, 0.055
    for a, b, c, e in ((s0, s1, y0, y0 + w), (s0, s1, y1 - w, y1), (s0, s0 + w, y0, y1), (s1 - w, s1, y0, y1)):
        F.q(M['frame'], a, b, c, e, d, frame_col)
    n = max(1, int(round((s1 - s0) / mull)))
    for k in range(1, n):
        x = s0 + (s1 - s0) * k / n
        F.q(M['frame'], x - w / 2, x + w / 2, y0, y1, d, frame_col)


def storefront(M, F, s0, s1, G, rng, base_col, frame_col):
    pier, depth, head = 0.24, 0.14, 2.85
    for a, b in ((s0, s0 + pier), (s1 - pier, s1)):
        F.q(M['base'], a, b, 0.0, G, 0.0, base_col)
    a, b = s0 + pier, s1 - pier
    F.q(M['base'], a, b, head, head + 0.2, 0.0, base_col)
    F.q(M['base'], a, b, G - 0.14, G, 0.0, base_col)
    i = rng.randrange(32)
    F.box(M['frame'], a + 0.05, b - 0.05, head + 0.2, G - 0.14, 0.0, 0.16, (0.12, 0.12, 0.13), sign_uv(i), M['signs'])
    F.sides(M['base'], a, b, 0.0, head, 0.0, -depth, base_col, inward=True)
    door = None
    if b - a > 2.6:
        k = rng.choice([0.0, 1.0, 0.5])
        door = a + (b - a - 1.0) * k
    g = rng.choice([(1, 1, 1), (1.2, 1.1, 0.9), (0.9, 1.0, 1.1), (1.25, 1.15, 0.95), (0.75, 0.8, 0.85)])
    w, d = 0.06, -depth + 0.03
    spans = [(a, b)] if door is None else [(a, door), (door + 1.0, b)]
    for u0, u1 in spans:
        if u1 - u0 < 0.25:
            continue
        F.q(M['stone'], u0, u1, 0.0, 0.32, -depth + 0.02, (0.55, 0.55, 0.56))
        F.q(M['glass'], u0, u1, 0.32, head, -depth, g)
        for x0, x1, y0, y1 in ((u0, u1, 0.32, 0.32 + w), (u0, u1, head - w, head), (u0, u0 + w, 0.32, head), (u1 - w, u1, 0.32, head), (u0, u1, 2.3, 2.3 + w)):
            F.q(M['frame'], x0, x1, y0, y1, d, frame_col)
        n = max(1, int(round((u1 - u0) / 1.6)))
        for k in range(1, n):
            x = u0 + (u1 - u0) * k / n
            F.q(M['frame'], x - w / 2, x + w / 2, 0.32, head, d, frame_col)
    if door is not None:
        F.q(M['glass'], door, door + 1.0, 0.0, head, -depth, g)
        for x0, x1, y0, y1 in ((door, door + 1.0, 2.25, 2.25 + w), (door, door + w, 0.0, head), (door + 1.0 - w, door + 1.0, 0.0, head), (door, door + 1.0, 0.0, 0.1)):
            F.q(M['frame'], x0, x1, y0, y1, d, frame_col)
        F.box(M['frame'], door + 0.80, door + 0.84, 0.85, 1.45, d, d + 0.06, (0.75, 0.75, 0.76))
    if rng.random() < 0.3:      # a fabric awning with a valance
        col = rng.choice(FABRIC)
        p = [F.P(a, head + 0.15, 0.16), F.P(b, head + 0.15, 0.16), F.P(b, head - 0.3, 1.25), F.P(a, head - 0.3, 1.25)]
        M['fabric'].quad2(p[0], p[1], p[2], p[3], (F.nx * 0.4, 1.0, F.nz * 0.4), col=col)
        F.q(M['fabric'], a, b, head - 0.5, head - 0.3, 1.25, col, both=True)


def building(kit, poly, storeys, near_front):
    """A building beside the road, modelled: shop fronts toward the road, recessed windows, signs, parapet."""
    rng, centre, BASE = kit.rng, kit.centre, kit.base
    PLACED.append(poly)
    poly = orient(poly.simplify(0.5), 1.0)
    floors = max(1, int(round(storeys)))
    G = 4.2 if floors > 1 else 3.8
    H = G + (floors - 1) * FLOOR
    col = rng.choice(WALLS)
    k = rng.choice([0.55, 0.7, 0.85, 1.0])       # one factor for the three channels (per channel it made pastel pink and lilac bases)
    base_col = tuple(c * k for c in col)
    frame_col = rng.choice([(0.10, 0.10, 0.11), (0.16, 0.16, 0.17), (0.80, 0.80, 0.80), (0.30, 0.22, 0.16)])
    style = 'commercial' if floors <= 10 else rng.choice(['tower', 'balcony'])
    # the finish of this building's walls: painted render, stone/tile panels, or (low buildings) brick
    finish = rng.choice(['wall', 'wall_tile', 'wall_tile', 'wall_brick'] if floors <= 6 else ['wall', 'wall_tile', 'wall_tile'])
    if finish == 'wall_brick':
        col, base_col = rng.choice([(0.95, 0.92, 0.9), (0.8, 0.74, 0.7), (1.0, 0.98, 0.95)]), (0.5, 0.5, 0.5)
    M = dict(kit.M, wall=kit.M[finish], base=kit.M['wall_tile'])
    c = list(poly.exterior.coords)
    for (ax, az), (bx, bz) in zip(c, c[1:]):
        if math.hypot(bx - ax, bz - az) < 0.3:
            continue
        F = Face(ax, az, bx, bz, BASE)
        F.col = col
        mid = Point((ax + bx) / 2, (az + bz) / 2)
        if F.L < 2.2:
            F.q(M['wall'], 0.0, F.L, 0.0, H, 0.0, col)
            continue
        on = centre.interpolate(centre.project(mid))
        toward = ((on.x - mid.x) * F.nx + (on.y - mid.y) * F.nz) / max(0.01, mid.distance(on))
        front = near_front and toward > 0.45 and mid.distance(on) < 26.0
        margin = 0.35
        nb = max(1, int(round((F.L - 2 * margin) / 5.2)))
        bw = (F.L - 2 * margin) / nb
        for a, b in ((0.0, margin), (F.L - margin, F.L)):
            F.q(M['wall'], a, b, 0.0, H, 0.0, col)
        for k in range(nb):
            s0, s1 = margin + k * bw, margin + (k + 1) * bw
            if front:
                storefront(M, F, s0, s1, G, rng, base_col, frame_col)
            else:
                F.q(M['base'], s0, s1, 0.0, G, 0.0, base_col)
            for f in range(1, floors):
                y = G + (f - 1) * FLOOR
                if style == 'commercial':
                    p = min(0.5, bw * 0.18)
                    F.q(M['wall'], s0, s0 + p, y, y + FLOOR, 0.0, col)
                    F.q(M['wall'], s1 - p, s1, y, y + FLOOR, 0.0, col)
                    F.q(M['wall'], s0 + p, s1 - p, y, y + 0.95, 0.0, col)
                    F.q(M['wall'], s0 + p, s1 - p, y + 2.7, y + FLOOR, 0.0, col)
                    window(M, F, s0 + p, s1 - p, y + 0.95, y + 2.7, 0.17, rng, frame_col)
                    if front and f <= 3 and rng.random() < 0.4:
                        F.box(M['frame'], s0 + p + 0.1, s1 - p - 0.1, y + 0.08, y + 0.86, 0.0, 0.1, (0.12, 0.12, 0.13), sign_uv(rng.randrange(32)), M['signs'])
                        signed = True
                    else:
                        signed = False
                    if front and f in (1, 2) and rng.random() < 0.22:      # a sign board standing out from the wall, in a steel frame
                        i = rng.randrange(8)
                        uv = ((i / 8, 0), ((i + 1) / 8, 0), ((i + 1) / 8, 1), (i / 8, 1))
                        for e, out, uvs in ((-0.045, (-F.ux, 0.0, -F.uz), uv), (0.045, (F.ux, 0.0, F.uz), (uv[1], uv[0], uv[3], uv[2]))):   # readable from both sides
                            sx = s0 + 0.12 + e
                            M['signs_v'].quad(F.P(sx, y + 0.2, 0.1), F.P(sx, y + 0.2, 0.78), F.P(sx, y + 3.0, 0.78), F.P(sx, y + 3.0, 0.1), out, uvs)
                        F.box(M['frame'], s0 + 0.085, s0 + 0.155, y + 0.2, y + 3.0, 0.1, 0.785, (0.2, 0.2, 0.21))
                        for yy in (y + 0.5, y + 2.7):
                            F.box(M['frame'], s0 + 0.09, s0 + 0.15, yy, yy + 0.05, 0.0, 0.1, (0.2, 0.2, 0.21))
                    if not signed and rng.random() < (0.3 if front else 0.45):      # an air conditioner's outdoor unit on brackets under the window
                        u0 = s0 + p + rng.uniform(0.1, max(0.11, s1 - s0 - 2 * p - 1.0))
                        F.box(M['frame'], u0, u0 + 0.82, y + 0.22, y + 0.80, 0.06, 0.38, (0.80, 0.80, 0.78), ((0, 0), (1, 0), (1, 1), (0, 1)), M['ac'])
                        F.box(M['frame'], u0 + 0.05, u0 + 0.77, y + 0.17, y + 0.22, 0.0, 0.4, (0.3, 0.3, 0.31))
                else:
                    F.q(M['wall'], s0, s1, y, y + 0.85, 0.0, col)
                    F.q(M['wall'], s0, s1, y + FLOOR - 0.28, y + FLOOR, 0.0, col)
                    g = rng.choice([(0.86, 1.0, 1.08), (0.92, 1.03, 1.1), (0.8, 0.92, 1.0)])
                    F.q(M['glass'], s0, s1, y + 0.85, y + FLOOR - 0.28, -0.05, g)
                    n = max(1, int(round(bw / 1.3)))
                    for j in range(n + 1):
                        x = s0 + bw * j / n
                        F.q(M['frame'], max(s0, x - 0.035), min(s1, x + 0.035), y + 0.85, y + FLOOR - 0.28, 0.0, frame_col)
                    if style == 'balcony' and toward > 0.2:
                        F.box(M['wall'], s0, s1, y - 0.14, y + 0.03, 0.0, 1.15, (0.92, 0.92, 0.90))
                        F.q(M['glass'], s0, s1, y + 0.03, y + 1.05, 1.12, (1.0, 1.08, 1.1), both=True)
        F.q(M['wall'], 0.0, F.L, H, H + 0.9, 0.0, col)
        F.q(M['wall'], 0.0, F.L, H, H + 0.9, -0.2, col, both=True)
        if style == 'commercial':                      # a slab edge at every floor: a shadow line across the facade
            band = tuple(v * 0.86 for v in col)
            for f in range(1, floors + 1):
                F.box(M['wall'], 0.0, F.L, G + (f - 1) * FLOOR - 0.16, G + (f - 1) * FLOOR, 0.0, 0.07, band)
            if rng.random() < 0.7:                     # a rainwater pipe down one corner
                F.box(M['painted'], 0.12, 0.22, 0.0, H, 0.0, 0.1, rng.choice([(0.62, 0.62, 0.62), (0.75, 0.74, 0.70), (0.35, 0.35, 0.36)]))
        if front and rng.random() < 0.55:              # a sandwich board on the sidewalk in front of the shops
            s, dist, i = rng.uniform(1.0, max(1.1, F.L - 1.0)), rng.uniform(1.0, 1.8), rng.randrange(8)
            uv = ((i % 4 / 4, 1 - (i // 4 + 1) / 2), ((i % 4 + 1) / 4, 1 - (i // 4 + 1) / 2), ((i % 4 + 1) / 4, 1 - i // 4 / 2), (i % 4 / 4, 1 - i // 4 / 2))
            for e in (-1.0, 1.0):
                M['boards'].quad2(F.P(s + e * 0.2, 0.0, dist - 0.25 * e), F.P(s + e * 0.2, 0.0, dist + 0.25 * e), F.P(s, 0.9, dist + 0.25 * e), F.P(s, 0.9, dist - 0.25 * e),
                                  (F.ux * e, 0.3, F.uz * e), (uv[1], uv[0], uv[3], uv[2]))   # (u swapped: it read mirrored from outside)
    M['roof'].polygon(poly, flat(BASE + H), (0.75, 0.75, 0.74))


def block(kit, p, storeys, y0):
    """A far building: walls with the window tile from 6 m below `y0` (into the ground) to its roof, and the roof."""
    if DRIVEN is not None and abs(y0 - (kit.base - 0.1)) < 0.5 and p.distance(DRIVEN) < MODEL_WITHIN:
        near = min(DRIVEN.geoms, key=p.distance)
        building(kit.along(near), p, storeys, False)
        return
    PLACED.append(p)
    top = y0 + storeys * FLOOR
    shade = kit.rng.uniform(0.7, 1.0)
    col = (shade, shade * kit.rng.uniform(0.95, 1.0), shade * kit.rng.uniform(0.9, 1.0))
    if DRIVEN is not None:          # (the landmark map) the far town in the colours of its walls, not all white
        base = WALLS[int(kit.rng.random() * len(WALLS))]
        col = tuple(min(1.0, c * 1.15 * shade) for c in base)
    kit.M['windows'].walls(p.simplify(1.0), y0 - 6.0, top, col, 13.2)       # one tile = 4 windows by 4 storeys
    kit.M['roof'].polygon(p, flat(top), (0.7, 0.7, 0.69))


def footprints(kit, elements, road_main, alleys, land_side, sea_side, ground_y, model_within, front_within):
    """Every OpenStreetMap building of `elements` not on the road or a side street: town-side ones within `model_within` m of the
    road centre modelled (shop fronts if within `front_within` m), the rest boxes standing on `ground_y(x, z)` (sea-side ones
    only if large and not in the sea). Storeys from building:levels, else from the footprint area. Returns (modelled, boxes)."""
    modelled = boxes = 0
    for e in elements:
        if len(e.get('geometry', [])) < 3:
            continue
        p = Polygon([proj(q['lat'], q['lon']) for q in e['geometry']]).buffer(0)
        if p.is_empty or p.geom_type != 'Polygon' or p.intersects(road_main) or p.intersects(alleys.buffer(-0.5)):
            continue
        town = land_side.contains(p.centroid)
        if not town and (p.area < 60.0 or sea_side.contains(p.centroid)):
            continue
        lv = e.get('tags', {}).get('building:levels')
        try:
            storeys = float(lv) if lv else None
        except ValueError:
            storeys = None
        if storeys is None:
            storeys = 3 if p.area < 150 else 5 if p.area < 400 else 7
        d = p.distance(kit.centre)
        if town and d < model_within and p.area > 12.0:
            building(kit, p, storeys, d < front_within)
            modelled += 1
        else:
            block(kit, p, storeys, max(kit.base - 0.1, ground_y(p.centroid.x, p.centroid.y) - 5.0))
            boxes += 1
    return modelled, boxes
