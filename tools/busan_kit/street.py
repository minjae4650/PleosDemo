"""The street, built along the kit's road: carriageway, gutter, kerbs, paint, crossings, signals, lamps, bollards, planters,
benches, shelters, road signs, grates, repairs, cabinets, and the cars standing in it.

Offsets are metres to the right of travel (`Kit.pose`): + is the sea side of the Gwangalli road, - the town side. A positive
single-sided buffer of the centre line lies on the left (town side), a negative one on the right."""
import math

import numpy as np
from shapely import affinity
from shapely.geometry import LineString, Point, Polygon
from shapely.ops import substring, unary_union

from .mesh import flat, lines, polys, vdisc

VEHICLES = 'Assets/ParkingLotEnvironment/CloudPilot/ParkingContext20261105/ReusableVehicles/'   # the project's own Hyundai models

ARROW = Polygon([(-2.5, -0.15), (0.6, -0.15), (0.6, -0.5), (2.5, 0.0), (0.6, 0.5), (0.6, 0.15), (-2.5, 0.15)])
DIAMOND = Polygon([(-2.5, 0.0), (0.0, 0.75), (2.5, 0.0), (0.0, -0.75)])
DIAMOND = DIAMOND.difference(DIAMOND.buffer(-0.2, join_style='mitre'))


# ------------------------------------------------------------------ carriageway

def carriageway(kit, road_main, alleys=None):
    """Asphalt between the gutters, the concrete gutter strips inside the kerbs, the side streets' asphalt. Returns the asphalt area."""
    inner = kit.centre.buffer(kit.half - 0.4, cap_style='flat')
    kit.M['asphalt'].polygon(inner, flat(kit.road_y))
    kit.M['gutter'].polygon(road_main.difference(inner), flat(kit.road_y + 0.004))
    if alleys is not None:
        kit.M['asphalt_alley'].polygon(alleys, flat(kit.road_y))
    return inner


def kerbs(kit, gaps=None):
    """Kerb stones, 1 m long with open joints, town side first; none on the town side whose centre lies in `gaps` (side-street mouths)."""
    for off in (kit.half + 0.1, -(kit.half + 0.1)):
        for s in np.arange(0.5, kit.LEN, 1.0):
            x, z, ux, uz = kit.pose(s, -off)
            if off > 0 and gaps is not None and gaps.contains(Point(x, z)):
                continue
            shade = kit.rng.uniform(0.70, 0.84)
            kit.M['stone'].obox(x, z, ux, uz, 0.492, 0.1, kit.road_y - 0.05, kit.base + 0.006, (shade, shade, shade * 0.99), 1.0)


def lane_lines(kit):
    """The yellow centre line and the two edge lines."""
    paint = [kit.centre.buffer(0.075, cap_style='flat'), kit.centre.offset_curve(kit.half - 0.55).buffer(0.06, cap_style='flat'),
             kit.centre.offset_curve(-(kit.half - 0.55)).buffer(0.06, cap_style='flat')]
    kit.M['paint_yellow'].polygon(unary_union(paint), flat(kit.road_y + 0.008))


def arrow(kit, s, off, turn=0.0):
    """A lane arrow pointing along the road (turn = pi: against it)."""
    x, z, ux, uz = kit.pose(s, off)
    kit.M['paint_white'].polygon(affinity.translate(affinity.rotate(ARROW, math.atan2(uz, ux) + turn, origin=(0, 0), use_radians=True), x, z), flat(kit.road_y + 0.008))


def manhole(kit, s, off):
    x, z, _, _ = kit.pose(s, off)
    kit.M['iron'].polygon(Point(x, z).buffer(0.34, quad_segs=5), flat(kit.road_y + 0.006), (1, 1, 1), 0.3)


def side_street_mouths(kit, alleys):
    """Crossings at the mouths of the side streets (town side): red surfacing, white bars, a stop line."""
    half = kit.half
    band = kit.centre.buffer(half + 4.8, single_sided=True).difference(kit.centre.buffer(half + 0.8, single_sided=True))
    for mouth in polys(alleys.intersection(band)):
        if mouth.area < 6.0:
            continue
        kit.M['paint_red'].polygon(mouth, flat(kit.road_y + 0.006))
        s = kit.centre.project(mouth.centroid)
        x, z, ux, uz = kit.pose(s, 0.0)
        bars = []
        for k in np.arange(-6.0, 6.01, 0.9):
            cx, cz = x + ux * k, z + uz * k
            bars.append(LineString([(cx - uz * (half + 1.1), cz + ux * (half + 1.1)), (cx - uz * (half + 4.5), cz + ux * (half + 4.5))]).buffer(0.22, cap_style='flat'))
        kit.M['paint_white'].polygon(unary_union(bars).intersection(mouth.buffer(-0.25)), flat(kit.road_y + 0.011))
        stop = kit.centre.buffer(half + 6.0, single_sided=True).difference(kit.centre.buffer(half + 5.55, single_sided=True)).intersection(alleys).intersection(mouth.buffer(3.0))
        kit.M['paint_white'].polygon(stop, flat(kit.road_y + 0.008))


def patch(kit, s, off, ln, wd, inner):
    """An asphalt repair across a lane, in newer, darker asphalt, kept inside the asphalt area `inner`."""
    seg = substring(kit.centre, s, s + ln).offset_curve(-off)
    if not seg.is_empty:
        kit.M['asphalt_patch'].polygon(seg.buffer(wd / 2, cap_style='flat').intersection(inner), flat(kit.road_y + 0.003))


def trench(kit, s0, s1, off, half_w):
    """A long trench repair line."""
    line = substring(kit.centre, s0, s1).offset_curve(-off)
    kit.M['asphalt_patch'].polygon(line.buffer(half_w, cap_style='flat'), flat(kit.road_y + 0.003))


# Every zebra crossing drawn: its middle, the road's direction there and half the road's width (tools/build_district.py writes
# them out; the moving cars stop before them while the signals are red).
ZEBRAS = []


def zebra(kit, s):
    """A zebra crossing over the road from s to s + 4.6: red surfacing, white bars, a stop line before it for each direction."""
    half = kit.half
    x, z, ux, uz = kit.pose(s + 2.3, 0.0)
    ZEBRAS.append((round(x, 2), round(z, 2), round(ux, 4), round(uz, 4), round(half, 2)))
    zone = substring(kit.centre, s, s + 4.6)
    kit.M['paint_red'].polygon(zone.buffer(half - 0.45, cap_style='flat'), flat(kit.road_y + 0.006))
    bars = [zone.offset_curve(o).buffer(0.22, cap_style='flat') for o in np.arange(-(half - 0.85), half - 0.84, 0.88)]
    kit.M['paint_white'].polygon(unary_union([b for b in bars if not b.is_empty]), flat(kit.road_y + 0.012))
    kit.M['paint_white'].polygon(substring(kit.centre, s - 2.6, s - 2.15).buffer(-(half - 0.5), single_sided=True), flat(kit.road_y + 0.009))
    kit.M['paint_white'].polygon(substring(kit.centre, s + 6.75, s + 7.2).buffer(half - 0.5, single_sided=True), flat(kit.road_y + 0.009))


def diamond(kit, s, off):
    """The diamond painted on the lane ahead of a crossing."""
    x, z, ux, uz = kit.pose(s, off)
    kit.M['paint_white'].polygon(affinity.translate(affinity.rotate(DIAMOND, math.atan2(uz, ux), origin=(0, 0), use_radians=True), x, z), flat(kit.road_y + 0.008))


def grate(kit, s, off):
    """A gutter grate."""
    x, z, ux, uz = kit.pose(s, off)
    c = [(x + ux * a + uz * b, kit.road_y + 0.009, z + uz * a - ux * b) for a, b in ((-0.3, -0.17), (0.3, -0.17), (0.3, 0.17), (-0.3, 0.17))]
    kit.M['grate'].quad(c[0], c[1], c[2], c[3], (0, 1, 0), ((0, 0), (0, 1), (1, 1), (1, 0)))


# ------------------------------------------------------------------ either side of it

def town_side(kit, sidewalk, yard):
    """The block-paved sidewalk with its kerb face, a tactile strip, and the yards in front of the buildings."""
    kit.M['sidewalk'].polygon(sidewalk, flat(kit.base))
    kit.M['stone'].walls(sidewalk, kit.road_y - 0.02, kit.base, (0.9, 0.9, 0.89), 1.0)
    tact = kit.centre.buffer(kit.half + 1.55, single_sided=True).difference(kit.centre.buffer(kit.half + 1.25, single_sided=True)).intersection(sidewalk)
    kit.M['tactile'].polygon(tact, flat(kit.base + 0.006))
    kit.M['yard'].polygon(yard, flat(kit.base - 0.02))
    kit.M['stone'].walls(yard, kit.road_y - 0.02, kit.base - 0.02, (0.8, 0.8, 0.79), 1.0)


def promenade(kit, area, step1, step2):
    """The sea-side promenade: a granite border, red paving with grey bands, its edge walls, two granite steps down to the sand."""
    base = kit.base
    border = area.intersection(kit.centre.buffer(-(kit.half + 1.1), single_sided=True)).union(area.difference(area.buffer(-0.5)))
    kit.M['granite'].polygon(border, flat(base))
    # a grey band across the red paving every 14 m, as paving patterns go
    bands = unary_union([LineString([kit.pose(s, kit.half)[:2], kit.pose(s, kit.half + 45.0)[:2]]).buffer(0.55, cap_style='flat') for s in np.arange(7.0, kit.LEN, 14.0)]).intersection(area).difference(border)
    kit.M['promenade2'].polygon(bands, flat(base))
    kit.M['promenade'].polygon(area.difference(border).difference(bands), flat(base))
    kit.M['stone'].walls(area, base - 0.5, base, (0.95, 0.95, 0.93), 1.0)
    kit.M['granite'].polygon(step1, flat(base - 0.15))
    kit.M['stone'].walls(step1, base - 0.5, base - 0.15, (0.95, 0.95, 0.93), 1.0)
    kit.M['granite'].polygon(step2, flat(base - 0.30))
    kit.M['stone'].walls(step2, base - 0.5, base - 0.30, (0.95, 0.95, 0.93), 1.0)


def promenade_width(kit, sand_all, s):
    """From the kerb to the first sand, square across the road (45 m when no sand is that near)."""
    x, z, ux, uz = kit.pose(s, kit.half)
    hit = LineString([(x, z), (x + uz * 45.0, z - ux * 45.0)]).intersection(sand_all)
    ds = [math.dist((x, z), c) for g in lines(hit) for c in g.coords]
    return min(ds) if ds else 45.0


# ------------------------------------------------------------------ furniture

def bollard(kit, x, z):
    shade = kit.rng.uniform(0.74, 0.84)                    # a rounded stone bollard, turned in one piece
    B = kit.base
    kit.M['stone'].lathe(x, z, [(0.145, B), (0.135, B + 0.05), (0.135, B + 0.31), (0.124, B + 0.36), (0.092, B + 0.40),
                                (0.045, B + 0.42), (0.0, B + 0.423)], 12, (shade, shade, shade * 0.98))


def lamp(kit, s, off, toward, banner=None):
    """A street lamp; `toward` = +1 reaches to the right of travel, -1 to the left (over the road)."""
    M, BASE = kit.M, kit.base
    x, z, ux, uz = kit.pose(s, off)
    ax, az = (uz * toward, -ux * toward)                # the arm reaches over the road
    M['metal'].frustum(x, z, 0.11, 0.07, BASE, BASE + 8.6, 10, cap=False)
    M['metal'].frustum(x, z, 0.16, 0.12, BASE, BASE + 0.9, 10)
    pts = [(x, BASE + 8.5, z), (x + ax * 0.9, BASE + 9.0, z + az * 0.9), (x + ax * 2.3, BASE + 9.15, z + az * 2.3)]
    for (x0, y0, z0), (x1, y1, z1) in zip(pts, pts[1:]):
        M['metal'].quad2((x0, y0 - 0.05, z0), (x1, y1 - 0.05, z1), (x1, y1 + 0.05, z1), (x0, y0 + 0.05, z0), (ux, 0, uz))
        M['metal'].quad2((x0 - ux * 0.05, y0, z0 - uz * 0.05), (x1 - ux * 0.05, y1, z1 - uz * 0.05), (x1 + ux * 0.05, y1, z1 + uz * 0.05), (x0 + ux * 0.05, y0, z0 + uz * 0.05), (0, 1, 0))
    M['metal'].obox(x + ax * 2.6, z + az * 2.6, ax, az, 0.42, 0.14, BASE + 9.07, BASE + 9.17, (0.9, 0.9, 0.9), bottom=True)
    if banner:                                         # a pair of banners on brackets, square to the road (no lettering: no real names)
        for side in (1.0, -1.0):
            def at(b, y):
                return (x + ax * side * b, BASE + y, z + az * side * b)
            light = tuple(min(1.0, c * 0.5 + 0.5) for c in banner)
            for y0, y1, col in ((3.3, 3.52, banner), (3.52, 3.60, light), (3.60, 3.66, banner), (3.66, 3.80, light), (3.80, 4.72, banner), (4.72, 5.1, (0.93, 0.93, 0.90))):
                M['fabric'].quad2(at(0.13, y0), at(0.75, y0), at(0.75, y1), at(0.13, y1), (ux, 0.0, uz), col=col)
            for y in (3.28, 5.12):
                M['metal'].quad2(at(0.0, y - 0.015), at(0.78, y - 0.015), at(0.78, y + 0.015), at(0.0, y + 0.015), (ux, 0.0, uz))


def planter_shape(heading):
    """The outline of a planter, 10.4 x 3.6 m, its long axis at `heading` (the road's)."""
    return affinity.rotate(affinity.scale(Point(0, 0).buffer(1.0, quad_segs=8), 5.2, 1.8), heading, origin=(0, 0), use_radians=True)


def planter(kit, s, off, ell, within):
    """A planter with a stone rim and grass (no trees yet), three flag poles in the bed. Not placed unless `within` holds the rim;
    returns whether it was."""
    M, BASE = kit.M, kit.base
    x, z, _, _ = kit.pose(s, off)
    rim, bed = affinity.translate(ell, x, z), affinity.translate(affinity.scale(ell, 0.9, 0.78), x, z)
    if not within.contains(rim):
        return False
    M['granite'].polygon(rim.difference(bed), flat(BASE + 0.42))
    M['stone'].walls(rim, BASE, BASE + 0.42, (0.9, 0.9, 0.88), 1.0)
    M['grass'].polygon(bed, flat(BASE + 0.33))
    _, _, ux, uz = kit.pose(s, 0.0)
    for k, col in zip((-2.4, 0.0, 2.4), kit.rng.sample([(0.85, 0.15, 0.15), (0.1, 0.3, 0.7), (0.95, 0.95, 0.95), (0.95, 0.75, 0.1), (0.1, 0.5, 0.35)], 3)):   # flag poles in the bed
        px, pz = x + ux * k, z + uz * k
        M['painted'].frustum(px, pz, 0.05, 0.035, BASE + 0.33, BASE + 7.6, 8, (0.9, 0.9, 0.9))
        M['fabric'].quad2((px, BASE + 6.6, pz), (px + ux * 1.3, BASE + 6.55, pz + uz * 1.3), (px + ux * 1.3, BASE + 7.4, pz + uz * 1.3), (px, BASE + 7.45, pz), (uz, 0.0, -ux), col=col)
    return True


def bench(kit, x, z, ux, uz):
    """A timber bench along (ux, uz), facing right of it (the sea), its back on the left."""
    M, BASE = kit.M, kit.base
    for k in range(3):                                 # seat slats, then two back slats on the road side
        M['wood'].obox(x + uz * (k - 1) * 0.15, z - ux * (k - 1) * 0.15, ux, uz, 0.9, 0.065, BASE + 0.40, BASE + 0.44, uvscale=1.0, bottom=True)
    for k in range(2):
        M['wood'].obox(x - uz * 0.27, z + ux * 0.27, ux, uz, 0.9, 0.02, BASE + 0.52 + k * 0.17, BASE + 0.65 + k * 0.17, uvscale=1.0, bottom=True)
    for e in (-0.75, 0.75):
        M['metal'].obox(x + ux * e - uz * 0.03, z + uz * e + ux * 0.03, uz, -ux, 0.27, 0.025, BASE, BASE + 0.40)
        M['metal'].obox(x + ux * e - uz * 0.27, z + uz * e + ux * 0.27, uz, -ux, 0.025, 0.025, BASE + 0.40, BASE + 0.84)


def shelter(kit, x, z, ux, uz):
    """A timber shelter with a bench under it."""
    M, BASE = kit.M, kit.base
    for a in (-2.3, 2.3):
        for b in (-1.3, 1.3):
            M['wood'].obox(x + ux * a + uz * b, z + uz * a - ux * b, ux, uz, 0.07, 0.07, BASE, BASE + 2.6, (0.75, 0.7, 0.65), 1.0)
    for b in (-1.3, 1.3):
        M['wood'].obox(x + uz * b, z - ux * b, ux, uz, 2.7, 0.06, BASE + 2.6, BASE + 2.76, (0.75, 0.7, 0.65), 1.0, bottom=True)
    for a in np.arange(-2.5, 2.51, 0.45):
        M['wood'].obox(x + ux * a, z + uz * a, uz, -ux, 1.65, 0.035, BASE + 2.76, BASE + 2.84, (0.8, 0.75, 0.7), 1.0, bottom=True)
    bench(kit, x, z, ux, uz)


def road_sign(kit, s, off, cell):
    """A round sign for the driver on a pole, facing back along the road; `cell`: 0 keep right, 1 30, 2 no parking, 3 straight on."""
    BASE = kit.base
    x, z, ux, uz = kit.pose(s, off)
    kit.M['painted'].frustum(x, z, 0.035, 0.035, BASE, BASE + 2.95, 8, (0.72, 0.72, 0.72))
    vdisc(kit.M['roadsigns'], x - ux * 0.045, BASE + 2.6, z - uz * 0.045, 0.3, -ux, -uz, cell)
    vdisc(kit.M['painted'], x - ux * 0.04, BASE + 2.6, z - uz * 0.04, 0.3, ux, uz, None, (0.6, 0.6, 0.6))


# what comes with a crossing: a signal on a mast arm beyond it for each approach, a pedestrian head at each kerb, and the
# diamond painted on the lane ahead of it (`diamond` above)
def signal(kit, s, side):
    """side +1: for the lane going up the road (the sea side); -1: for the oncoming lane."""
    M, BASE, HALF = kit.M, kit.base, kit.half
    x, z, ux, uz = kit.pose(s, side * (HALF + (1.05 if side > 0 else 0.8)))
    ax, az = -uz * side, ux * side                     # across the road, from the pole toward its lane (the driver's left)
    fx, fz = -ux * side, -uz * side                    # the head faces the traffic it is for
    M['metal'].frustum(x, z, 0.10, 0.08, BASE, BASE + 5.9, 10, cap=False)
    M['metal'].frustum(x, z, 0.15, 0.11, BASE, BASE + 0.7, 10)
    y, reach = BASE + 5.6, (1.05 if side > 0 else 0.8) + HALF / 2 + 0.75
    x1, z1 = x + ax * reach, z + az * reach
    M['metal'].quad2((x, y - 0.06, z), (x1, y - 0.04, z1), (x1, y + 0.04, z1), (x, y + 0.06, z), (ux, 0.0, uz))
    M['metal'].quad2((x - ux * 0.05, y, z - uz * 0.05), (x1 - ux * 0.04, y, z1 - uz * 0.04), (x1 + ux * 0.04, y, z1 + uz * 0.04), (x + ux * 0.05, y, z + uz * 0.05), (0, 1, 0))
    hx, hz = x + ax * (reach - 0.75), z + az * (reach - 0.75)                      # the head hangs over the middle of the lane
    M['painted'].obox(hx, hz, ax, az, 0.66, 0.11, y - 0.52, y - 0.08, (0.07, 0.07, 0.08), 1.0, bottom=True)
    for e, mat in ((0.42, 'sig_red'), (0.0, 'sig_yellow'), (-0.42, 'sig_green')):      # red on the driver's left (SignalLights.cs lights them)
        vdisc(M[mat], hx + ax * e + fx * 0.116, y - 0.30, hz + az * e + fz * 0.116, 0.125, fx, fz, None, (1, 1, 1))


def walk_signal(kit, s, side):
    M, BASE = kit.M, kit.base
    off = kit.half + (1.05 if side > 0 else 0.8)
    x, z, ux, uz = kit.pose(s, side * off)
    fx, fz = -uz * side, ux * side                     # faces across the road
    M['metal'].frustum(x, z, 0.06, 0.05, BASE, BASE + 2.95, 8)
    cx, cz = x + fx * 0.16, z + fz * 0.16
    M['painted'].obox(cx, cz, ux, uz, 0.17, 0.10, BASE + 2.15, BASE + 2.85, (0.07, 0.07, 0.08), 1.0, bottom=True)
    vdisc(M['walk_red'], cx + fx * 0.106, BASE + 2.67, cz + fz * 0.106, 0.11, fx, fz, None, (1, 1, 1))
    vdisc(M['walk_green'], cx + fx * 0.106, BASE + 2.34, cz + fz * 0.106, 0.11, fx, fz, None, (1, 1, 1))


def cabinet(kit, x, z, ux, uz):
    """A street cabinet (traffic control, telecom) on a stone plinth."""
    BASE = kit.base
    kit.M['painted'].obox(x, z, ux, uz, 0.36, 0.22, BASE, BASE + 1.3, kit.rng.choice([(0.42, 0.47, 0.44), (0.6, 0.6, 0.58), (0.35, 0.38, 0.42)]), 1.0)
    kit.M['stone'].obox(x, z, ux, uz, 0.42, 0.28, BASE, BASE + 0.08, (0.8, 0.8, 0.8), 1.0)


def car(kit, s, off, back, name):
    """A car standing in the road: a prefab of VEHICLES, facing along the road or (back) against it."""
    x, z, ux, uz = kit.pose(s, off)
    return {'prefab': VEHICLES + name + '.prefab', 'x': round(x, 2), 'y': kit.road_y, 'z': round(z, 2), 'yaw': round(math.degrees(math.atan2(ux, uz)) + (180.0 if back else 0.0), 1)}
