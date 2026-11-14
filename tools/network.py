"""The road network of the compressed Busan map: segments, the two routes, heights, speeds, lanes, regions (plan.md, section 1).

Not true to the map. The Gwangalli beach road stays where the finished district has it (from OpenStreetMap, ODbL); everything
else is laid out around it in a local frame: `a` runs along the Gwangalli road toward the north-east, `b` to the right of that,
toward the sea. The drive starts in Haeundae (east), and at the fork F either climbs onto Gwangan Bridge or turns right along
the coast through Gwangalli; both ways meet again at the junction J on the Seomyeon side, run up the Seomyeon boulevard, turn
right and come back through a tunnel under the hill that fills the inside of the loop. Every turn is a right turn.

A segment's reference line is the centre of a two-way road, or 1.5 m left of the left lane of a one-way ramp, so that the line
runs on without a sideways step where a ramp leaves or joins a boulevard. Lanes lie to the right of it.

Run:  python tools/network.py [--png <file>]   (writes LandmarkNetwork.json and prints the checks; exit code 1 if one fails)"""
import argparse
import json
import math
import sys
from pathlib import Path

import numpy as np
from shapely.geometry import LineString, Point
from shapely.ops import substring

ROOT = Path(__file__).resolve().parents[1]
CONCEPTS = ROOT / 'docs/concepts'
OUT = ROOT / 'unity/InputProbe/Assets/BusanEnvironment/Editor/Data/LandmarkNetwork.json'

ROAD_WAY = 165503746                      # 광안해변로, as in the Gwangalli generator
STRETCH = ((5081.0, 3338.0), (5354.0, 3736.0))
MARGIN = 90.0
ROAD_Y, DECK_Y = 4.0, 30.0
STEP = 4.0
LANE = 3.25

A_H, A_S = 800.0, -300.0                  # the Haeundae and the Seomyeon boulevards run along b at these a
B_BACK = -700.0                           # the return road behind the hill
B_BRIDGE, R_ARC = 520.0, 250.0            # the bridge line out at sea, and the radius of the two ramp curves (at 450 the ramp was 6.2% steep)
A_PW, A_PE = -100.0, 520.0                # tunnel portals on the return road (Seomyeon side, Haeundae side)

O = np.array(STRETCH[0])
EA = np.array(STRETCH[1]) - O
EA = EA / np.linalg.norm(EA)
EB = np.array([EA[1], -EA[0]])            # right of travel along +a: the sea side


def world(ab):
    ab = np.atleast_2d(np.asarray(ab, dtype=float))
    return O + ab[:, :1] * EA + ab[:, 1:2] * EB


def local(xz):
    d = np.atleast_2d(np.asarray(xz, dtype=float)) - O
    return np.stack([d @ EA, d @ EB], axis=1)


def unit(v):
    v = np.asarray(v, dtype=float)
    return v / np.linalg.norm(v)


def right_of(d):
    return np.array([-d[1], d[0]])        # in the local frame: heading +a has +b (the sea) on its right


def resample(pts, step=STEP):
    ls = LineString(pts)
    n = max(1, int(math.ceil(ls.length / step)))
    return np.array([ls.interpolate(ls.length * k / n).coords[0] for k in range(n + 1)])


def arc(centre, r, a0, a1, n_per_deg=1.0):
    n = max(2, int(abs(a1 - a0) * n_per_deg) + 1)
    return [(centre[0] + r * math.cos(math.radians(t)), centre[1] + r * math.sin(math.radians(t))) for t in np.linspace(a0, a1, n)]


def fillet(p0, x, p1, r):
    """p0 -> p1 around the corner x with radius r (a straight, an arc, a straight)."""
    p0, x, p1 = np.asarray(p0, float), np.asarray(x, float), np.asarray(p1, float)
    d0, d1 = unit(x - p0), unit(p1 - x)
    turn = math.acos(max(-1.0, min(1.0, float(d0 @ d1))))
    t = r * math.tan(turn / 2)
    assert np.linalg.norm(x - p0) >= t - 1e-6 and np.linalg.norm(p1 - x) >= t - 1e-6, ('fillet does not fit', p0, x, p1, r)
    t0, t1 = x - d0 * t, x + d1 * t
    side = 1.0 if float(right_of(d0) @ d1) > 0 else -1.0             # +1: a right turn
    c = t0 + right_of(d0) * side * r
    a0 = math.degrees(math.atan2(t0[1] - c[1], t0[0] - c[0]))
    a1 = a0 + side * math.degrees(turn)                             # in the local frame a right turn runs counter-clockwise
    return [tuple(p0)] + arc(c, r, a0, a1) + [tuple(p1)]


def round_corners(pts, radius, min_turn=3.0):
    """The same line with its sharp vertices rounded (the OSM road has a 16 degree vertex that a car at speed cannot follow).
    The line moves by well under a metre; the road that is drawn keeps its own shape."""
    v = [np.array(p) for p in LineString(pts).simplify(0.25).coords]
    out = [tuple(v[0])]
    for i in range(1, len(v) - 1):
        d0, d1 = unit(v[i] - v[i - 1]), unit(v[i + 1] - v[i])
        turn = math.acos(max(-1.0, min(1.0, float(d0 @ d1))))
        if math.degrees(turn) < min_turn:
            out.append(tuple(v[i]))
            continue
        t = min(radius * math.tan(turn / 2), 0.45 * np.linalg.norm(v[i] - v[i - 1]), 0.45 * np.linalg.norm(v[i + 1] - v[i]))
        r = t / math.tan(turn / 2)
        t0 = v[i] - d0 * t
        side = 1.0 if float(right_of(d0) @ d1) > 0 else -1.0
        c = t0 + right_of(d0) * side * r
        a0 = math.degrees(math.atan2(t0[1] - c[1], t0[0] - c[0]))
        out += arc(c, r, a0, a0 + side * math.degrees(turn), n_per_deg=2.0)
    out.append(tuple(v[-1]))
    return resample(out)


def grade_profile(s, y0, y1, lead, tail, vc=75.0):
    """Heights along arc length s: level for `lead`, a constant grade eased in and out over `vc`, level for the last `tail`."""
    L = s[-1]
    s0, s1 = lead, L - tail
    g = (y1 - y0) / (s1 - s0 - vc)

    def grade(t):
        if t <= s0 or t >= s1:
            return 0.0
        if t < s0 + vc:
            return g * (t - s0) / vc
        if t > s1 - vc:
            return g * (s1 - t) / vc
        return g
    y, out = y0, [y0]
    for k in range(1, len(s)):
        m = 8                                                        # integrate the grade in small steps
        for j in range(m):
            t = s[k - 1] + (s[k] - s[k - 1]) * (j + 0.5) / m
            y += grade(t) * (s[k] - s[k - 1]) / m
        out.append(y)
    out = np.array(out)
    out += (y1 - out[-1]) * np.clip((s - s0) / (s1 - s0), 0.0, 1.0)  # the integral lands within millimetres of y1: close the rest
    return out, abs(g)


def gwangalli_centre():
    def proj(lat, lon):
        return ((lon - 129.06) * 111320.0 * math.cos(math.radians(35.12)), (lat - 35.12) * 110574.0)
    streets = json.loads((CONCEPTS / 'busan-slice-streets-osm.json').read_text(encoding='utf-8'))['elements']
    way = next(w for w in streets if w['id'] == ROAD_WAY)
    full = LineString([proj(p['lat'], p['lon']) for p in way['geometry']])
    a, b = full.project(Point(STRETCH[0])), full.project(Point(STRETCH[1]))
    lo, hi = max(0.0, a - MARGIN), min(full.length, b + MARGIN)
    centre = substring(full, lo, hi)
    pts = [centre.interpolate(s).coords[0] for s in np.arange(0.0, centre.length, 4.0)] + [centre.coords[-1]]
    return np.array(pts), a - lo                                     # as the generator has it; and where its 500 m start


class Seg:
    def __init__(self, sid, kind, ab, fwd, back, median, speed, lane_w=LANE, y=None, note=''):
        self.id, self.kind, self.note = sid, kind, note
        self.ab = np.asarray(ab, dtype=float)
        self.fwd, self.back, self.median, self.lane_w = fwd, back, median, lane_w
        d = np.linalg.norm(np.diff(self.ab, axis=0), axis=1)
        self.s = np.concatenate([[0.0], np.cumsum(d)])
        self.y = np.full(len(self.ab), ROAD_Y) if y is None else np.asarray(y, dtype=float)
        self.speed = np.full(len(self.ab), float(speed))
        self.next = []

    @property
    def length(self):
        return float(self.s[-1])

    @property
    def width(self):
        return (self.fwd + self.back) * self.lane_w + self.median

    def lane(self, k):
        """Offset of forward lane k (0 = next to the median) to the right of the reference line."""
        return self.median / 2 + (k + 0.5) * self.lane_w

    def tangent(self, i):
        j0, j1 = max(0, i - 1), min(len(self.ab) - 1, i + 1)
        return unit(self.ab[j1] - self.ab[j0])


def build():
    g_xz, g_start = gwangalli_centre()
    g_ab = round_corners(local(g_xz), 60.0)                          # in +s order: south-west to north-east
    g_ne, g_sw = g_ab[-1], g_ab[0]
    d_ne = unit(g_ab[-6] - g_ab[-1])                                 # the drive enters at the north-east end, against +s
    d_sw = unit(g_ab[0] - g_ab[5])                                   # and leaves at the south-west end

    # where the lines of the Gwangalli road, carried on, meet the two boulevards
    x1 = g_ne + (-d_ne) * ((A_H - g_ne[0]) / (-d_ne[0]))
    x2 = g_sw + d_sw * ((A_S - g_sw[0]) / d_sw[0])
    r1, r2 = 40.0, 30.0
    nose = np.array([A_H, x1[1] - r1 - 20.0])                        # the fork: the right lanes peel off 20 m before the curve
    f0 = nose + np.array([0.0, -80.0])
    h1_0 = nose + np.array([0.0, -500.0])
    pc = np.array([A_S, x2[1] - 70.0])                               # past the junction J: both routes are on the Seomyeon boulevard

    S = {}
    S['H1'] = Seg('H1', 'city', resample([h1_0, f0]), 3, 3, 3.0, 14, note='해운대 대로 (출발)')
    S['F'] = Seg('F', 'junction', resample([f0, nose]), 4, 3, 3.0, 13, note='갈림: 왼쪽 2차로 광안대교, 오른쪽 2차로 광안리')
    # B1 starts as the right two of F's four lanes: its centre line 8.0 m to the right of F's reference line (median 3.0 / 2 +
    # two lanes of 3.25), so the coast route stays in its lane through the fork and never crosses the bridge lanes
    b1_0 = nose + right_of(unit(nose - f0)) * 8.0
    x1b = g_ne + (-d_ne) * ((b1_0[0] - g_ne[0]) / (-d_ne[0]))
    S['B1'] = Seg('B1', 'city', resample(fillet(b1_0, x1b, g_ne, r1)), 2, 2, 0.0, 11, note='우회전, 방파제길')
    S['G'] = Seg('G', 'beach', g_ab[::-1].copy(), 1, 1, 0.0, 12, lane_w=3.5, note='광안리 해변로 (기존 구역, 남서 방향)')
    S['B2'] = Seg('B2', 'city', resample(fillet(g_sw, x2, pc, r2)), 2, 2, 0.0, 12, note='남천 쪽 해변길, J에서 우회전')

    a1 = [tuple(nose), (A_H, B_BRIDGE - R_ARC)] + arc((A_H - R_ARC, B_BRIDGE - R_ARC), R_ARC, 0.0, 90.0)
    S['A1'] = Seg('A1', 'ramp', resample(a1), 2, 0, 3.0, 17, note='상승 램프')
    S['A1'].y, g1 = grade_profile(S['A1'].s, ROAD_Y, DECK_Y, 60.0, 0.0)
    S['A2'] = Seg('A2', 'bridge', resample([(A_H - R_ARC, B_BRIDGE), (A_S + R_ARC, B_BRIDGE)]), 4, 0, 3.0, 22, y=None, note='광안대교 본선')
    S['A2'].y = np.full(len(S['A2'].ab), DECK_Y)
    a3 = arc((A_S + R_ARC, B_BRIDGE - R_ARC), R_ARC, 90.0, 180.0) + [tuple(pc)]
    S['A3'] = Seg('A3', 'ramp', resample(a3), 2, 0, 3.0, 17, note='하강 램프, J 직진')
    tail = float(np.linalg.norm(np.array([A_S, x2[1] + 60.0]) - pc))  # level from 60 m before the junction on
    S['A3'].y, g3 = grade_profile(S['A3'].s, DECK_Y, ROAD_Y, 0.0, tail)

    c1_1 = pc + np.array([0.0, -100.0])
    s_1 = np.array([A_S, B_BACK + 60.0])
    S['C1'] = Seg('C1', 'city', resample([pc, c1_1]), 3, 3, 3.0, 13, note='서면 진입')
    S['S'] = Seg('S', 'city', resample([c1_1, s_1]), 4, 4, 3.0, 11, note='서면')
    t_1 = np.array([A_S + 60.0, B_BACK])
    S['T'] = Seg('T', 'junction', resample(fillet(s_1, (A_S, B_BACK), t_1, 35.0)), 2, 2, 0.0, 8, note='큰 교차로에서 우회전')
    S['R1'] = Seg('R1', 'city', resample([t_1, (A_PW, B_BACK)]), 2, 2, 0.0, 13, note='서면 뒷길')
    S['TN'] = Seg('TN', 'tunnel', resample([(A_PW, B_BACK), (A_PE, B_BACK)]), 2, 0, 0.0, 19, note='터널 (일방 2차로)')
    S['H0'] = Seg('H0', 'city', resample(fillet((A_PE, B_BACK), (A_H, B_BACK), h1_0, 100.0)), 2, 2, 0.0, 13, note='터널 출구에서 해운대 입구까지')

    for a_, b_ in (('H1', 'F'), ('F', 'B1'), ('F', 'A1'), ('B1', 'G'), ('G', 'B2'), ('B2', 'C1'), ('A1', 'A2'), ('A2', 'A3'), ('A3', 'C1'),
                   ('C1', 'S'), ('S', 'T'), ('T', 'R1'), ('R1', 'TN'), ('TN', 'H0'), ('H0', 'H1')):
        S[a_].next.append(b_)
    # slower through the tight corners
    for sid, v in (('B1', 9.0), ('B2', 8.0)):
        seg = S[sid]
        for i in range(1, len(seg.ab) - 1):
            turn = math.degrees(math.acos(max(-1, min(1, float(seg.tangent(i - 1) @ seg.tangent(i + 1))))))
            if turn > 3.0:
                seg.speed[i] = v
    routes = [
        {'name': '광안리 해안', 'segments': ['H1', 'F', 'B1', 'G', 'B2', 'C1', 'S', 'T', 'R1', 'TN', 'H0'],
         'lanes': {'H1': 2, 'F': 2, 'B1': 0, 'G': 0, 'B2': 0, 'C1': 2, 'S': 2, 'T': 1, 'R1': 1, 'TN': 1, 'H0': 1}},
        {'name': '광안대교', 'segments': ['H1', 'F', 'A1', 'A2', 'A3', 'C1', 'S', 'T', 'R1', 'TN', 'H0'],
         'lanes': {'H1': 1, 'F': 1, 'A1': 1, 'A2': 1, 'A3': 1, 'C1': 2, 'S': 2, 'T': 1, 'R1': 1, 'TN': 1, 'H0': 1}},
    ]
    info = {'g_start': g_start, 'x1': x1, 'x2': x2, 'nose': nose, 'pc': pc, 'grades': (g1, g3), 'g_ab': g_ab}
    return S, routes, info


def flatten(S, route):
    """One closed line for a route: points, heights, lane offsets, speeds, and where each segment starts."""
    ab, y, lane, speed, starts = [], [], [], [], {}
    jump, last = {}, None      # where a segment's reference line starts to the side of the one before (a lane that splits off): index -> metres to the right
    for sid in route['segments']:
        seg = S[sid]
        if last is not None and np.linalg.norm(seg.ab[0] - last.ab[-1]) > 0.05:
            jump[len(ab)] = float((seg.ab[0] - last.ab[-1]) @ right_of(last.tangent(len(last.ab) - 1)))
        last = seg
        n = len(seg.ab) - 1                                          # the last point is the next segment's first
        starts[sid] = len(ab)
        ab += list(seg.ab[:n])
        y += list(seg.y[:n])
        lane += [seg.lane(route['lanes'][sid])] * n
        speed += list(seg.speed[:n])
    ab, y, lane, speed = np.array(ab), np.array(y), np.array(lane), np.array(speed)
    d = np.linalg.norm(np.diff(np.vstack([ab, ab[:1]]), axis=0), axis=1)
    n = len(ab)
    # speed: never more than 2 m/s per 10 m away from its neighbour, so a slow corner is braked for and left gradually
    for i in list(range(n)) * 2:
        j = (i + 1) % n
        speed[j] = min(speed[j], speed[i] + 0.2 * d[i])
    for i in list(range(n - 1, -1, -1)) * 2:
        j = (i - 1) % n
        speed[j] = min(speed[j], speed[i] + 0.2 * d[j])
    # lanes: a slew-limited follow of the target (0.06 m sideways per metre), forward and backward, so a change is centred on the joint
    lim = 0.06
    fw = lane.copy()
    for i in list(range(len(fw))) * 2:
        j = (i + 1) % len(fw)
        base = fw[i] - jump.get(j, 0.0)                              # the same place, measured from the next reference line
        fw[j] = base + max(-lim * d[i], min(lim * d[i], lane[j] - base))
    bw = lane.copy()
    for i in list(range(len(bw) - 1, -1, -1)) * 2:
        j = (i - 1) % len(bw)
        base = bw[i] + jump.get(i, 0.0)
        bw[j] = base + max(-lim * d[j], min(lim * d[j], lane[j] - base))
    lane = (fw + bw) / 2
    # the path itself may not step sideways: over the 120 m before such a start it slides across, and the lane offset gives the
    # same amount back, so the line that is driven stays where it was
    was = ab.copy()
    for j, delta in jump.items():
        back, k = STEP, j - 1
        while back < 120.0:
            w = 1.0 - back / 120.0
            w = w * w * (3 - 2 * w)
            ab[k] = was[k] + right_of(unit(was[k] - was[k - 1])) * delta * w
            lane[k] -= delta * w
            back += float(np.linalg.norm(was[k] - was[k - 1]))
            k -= 1
    d = np.linalg.norm(np.diff(np.vstack([ab, ab[:1]]), axis=0), axis=1)
    s = np.concatenate([[0.0], np.cumsum(d)])
    return ab, y, lane, speed, starts, s


def check(S, routes, flat, info):
    problems, notes = [], []
    for r, (ab, y, lane, speed, starts, s) in zip(routes, flat):
        n = len(ab)
        seg_d = np.linalg.norm(np.diff(np.vstack([ab, ab[:1]]), axis=0), axis=1)
        if seg_d.max() > STEP + 0.06 or seg_d.min() < 0.5:
            problems.append('%s: point spacing %.2f-%.2f m' % (r['name'], seg_d.min(), seg_d.max()))
        worst_turn, worst_acc, worst_grade, worst_pitch, worst_lane = 0.0, 0.0, 0.0, 0.0, 0.0
        for i in range(n):
            p0, p1, p2 = ab[i - 1], ab[i], ab[(i + 1) % n]
            t0, t1 = unit(p1 - p0), unit(p2 - p1)
            turn = math.acos(max(-1, min(1, float(t0 @ t1))))
            if turn > math.radians(12):
                problems.append('%s: kink of %.0f degrees at point %d' % (r['name'], math.degrees(turn), i))
            if float(right_of(t0) @ t1) < -math.sin(math.radians(1.5)):
                notes.append('%s: bends left at point %d' % (r['name'], i))
            radius = seg_d[i] / turn if turn > 1e-4 else 1e9
            worst_acc = max(worst_acc, speed[i] ** 2 / radius)
            g0, g1 = (y[i] - y[i - 1]) / seg_d[i - 1], (y[(i + 1) % n] - y[i]) / seg_d[i]
            worst_grade = max(worst_grade, abs(g1))
            worst_pitch = max(worst_pitch, math.degrees(abs(math.atan(g1) - math.atan(g0))) * (speed[i] * 0.02 / seg_d[i]))
            worst_lane = max(worst_lane, abs(lane[(i + 1) % n] - lane[i]) / seg_d[i])
        notes.append('%s: %.0f m, about %.1f min; lateral %.1f m/s2 max, grade %.1f%% max, pitch step %.2f deg per 20 ms max, lane drift %.3f m/m max'
                     % (r['name'], s[-1], sum(seg_d / speed) / 60, worst_acc, worst_grade * 100, worst_pitch, worst_lane))
        if worst_acc > 3.6:
            problems.append('%s: lateral acceleration %.1f m/s2' % (r['name'], worst_acc))
        if worst_grade > 0.056:
            problems.append('%s: grade %.1f%%' % (r['name'], worst_grade * 100))
        if worst_pitch > 2.5:
            problems.append('%s: pitch step %.2f degrees' % (r['name'], worst_pitch))
    # surfaces of different segments close together: level with each other (a junction) or far apart in height; never 0.3-4.5 m
    ids = list(S)
    for i, a_ in enumerate(ids):
        for b_ in ids[i + 1:]:
            sa, sb = S[a_], S[b_]
            if b_ in sa.next or a_ in sb.next:
                joint = True
            else:
                joint = False
            reach = (sa.width + sb.width) / 2 + 1.0
            da = np.linalg.norm(sa.ab[:, None, :] - sb.ab[None, :, :], axis=2)
            close = np.argwhere(da < reach)
            if len(close) == 0:
                continue
            dy = np.abs(sa.y[close[:, 0]] - sb.y[close[:, 1]])
            bad = close[(dy > 0.3) & (dy < 4.5)]
            stacked = close[dy >= 4.5]
            if len(bad):
                k = bad[0]
                problems.append('%s and %s: %d point pairs within %.0f m with 0.3-4.5 m between their heights (first at %s, heights %.1f and %.1f)'
                                % (a_, b_, len(bad), reach, np.round(world(sa.ab[k[0]])[0], 0), sa.y[k[0]], sb.y[k[1]]))
            if len(stacked):
                notes.append('%s over/under %s at %d point pairs (needs separate colliders)' % (a_, b_, len(stacked)))
            if not joint and len(close) and not len(bad):
                notes.append('%s and %s run together or cross on the level at %d point pairs' % (a_, b_, int((dy <= 0.3).sum())))
    return problems, notes


def box_ab(a0, a1, b0, b1):
    return [[round(float(v), 1) for v in world([p])[0]] for p in ((a0, b0), (a1, b0), (a1, b1), (a0, b1))]


def write(S, routes, flat, info):
    def xyz(ab, y):
        w = world(ab)
        return [[round(float(w[i, 0]), 3), round(float(y[i]), 3), round(float(w[i, 1]), 3)] for i in range(len(ab))]
    segments = [{'id': g.id, 'kind': g.kind, 'note': g.note, 'oneWay': g.back == 0, 'fwd': g.fwd, 'back': g.back, 'median': g.median, 'laneWidth': g.lane_w,
                 'width': round(g.width, 2), 'length': round(g.length, 1), 'next': g.next, 'pts': xyz(g.ab, g.y), 'speed': [round(float(v), 2) for v in g.speed]}
                for g in S.values()]
    out_routes, jumps = [], []
    for k, (r, (ab, y, lane, speed, starts, s)) in enumerate(zip(routes, flat)):
        out_routes.append({'name': r['name'], 'closed': True, 'segments': r['segments'], 'length': round(float(s[-1]), 1),
                           'segmentStart': {sid: round(float(s[i]), 1) for sid, i in starts.items()},
                           'path': xyz(ab, y), 'lane': [round(float(v), 3) for v in lane], 'speed': [round(float(v), 2) for v in speed]})
    s0, s1 = flat[0][5], flat[1][5]
    st0, st1 = flat[0][4], flat[1][4]
    jumps = [{'name': '해운대', 'route': 0, 's': 60.0}, {'name': '광안리', 'route': 0, 's': round(float(s0[st0['G']]), 1)},
             {'name': '광안대교', 'route': 1, 's': round(float(s1[st1['A2']]), 1)}, {'name': '서면', 'route': 0, 's': round(float(s0[st0['S']]), 1)},
             {'name': '터널', 'route': 0, 's': round(float(s0[st0['TN']]), 1)}]
    tn = S['TN']
    mid = world([(tn.ab[0] + tn.ab[-1]) / 2])[0]
    towers = [(425.0, B_BRIDGE), (75.0, B_BRIDGE)]
    out = {
        'note': 'Generated by tools/network.py. The Gwangalli road line is from OpenStreetMap contributors (ODbL).',
        'frame': {'origin': [float(O[0]), float(O[1])], 'a': [float(EA[0]), float(EA[1])], 'b': [float(EB[0]), float(EB[1])]},
        'roadY': ROAD_Y, 'deckY': DECK_Y,
        'segments': segments, 'routes': out_routes, 'spawn': {'route': 0, 's': 60.0}, 'jumps': jumps,
        'junctions': [{'id': 'F', 'at': [round(float(v), 1) for v in world([info['nose']])[0]]},
                      {'id': 'J', 'at': [round(float(v), 1) for v in world([(A_S, info['x2'][1])])[0]]},
                      {'id': 'T', 'at': [round(float(v), 1) for v in world([(A_S, B_BACK)])[0]]}],
        'zones': [{'kind': 'tunnel', 'segment': 'TN', 'centre': [round(float(mid[0]), 1), ROAD_Y + 4.0, round(float(mid[1]), 1)],
                   'axis': [float(EA[0]), float(EA[1])], 'half': [round(tn.length / 2, 1), 5.0, 7.0], 'ramp': 30.0}],
        'bridge': {'towers': [[round(float(v), 1) for v in world([t])[0]] for t in towers], 'deckY': DECK_Y,
                   'from': [round(float(v), 1) for v in world([(A_H - R_ARC, B_BRIDGE)])[0]], 'to': [round(float(v), 1) for v in world([(A_S + R_ARC, B_BRIDGE)])[0]]},
        'regions': {'gwangalli': box_ab(float(info['g_ab'][0][0]) - 0.5, float(info['g_ab'][-1][0]) + 0.5, -300, 140), 'haeundae': box_ab(640, 1150, -1000, 220), 'seomyeon': box_ab(-650, -150, -1000, 140),
                    'hill': box_ab(-150, 640, -1000, -300), 'sea': box_ab(-650, 1150, 140, 900)},
        'gwangalli': {'start500': round(float(info['g_start']), 1)},
    }
    OUT.write_text(json.dumps(out, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    return out


def draw(S, routes, flat, info, png):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from matplotlib import font_manager
    font_manager.fontManager.addfont(r'C:\Windows\Fonts\NotoSansKR-Regular.ttf')
    plt.rcParams['font.family'] = font_manager.FontProperties(fname=r'C:\Windows\Fonts\NotoSansKR-Regular.ttf').get_name()
    colour = {'city': '#555555', 'beach': '#c8553d', 'ramp': '#7b5ea7', 'bridge': '#3d6fb4', 'tunnel': '#222222', 'junction': '#d08a1d'}
    fig, (ax, ax2) = plt.subplots(2, 1, figsize=(13, 13), gridspec_kw={'height_ratios': [3.2, 1]})
    ax.fill([-650, 1150, 1150, -650], [60, 60, 900, 900], color='#dcebf5', zorder=0)                      # sea
    hill = plt.Polygon([(-150, -300), (640, -300), (640, -1000), (-150, -1000)], color='#dfe8d6', zorder=0)
    ax.add_patch(hill)
    for g in S.values():
        ax.plot(g.ab[:, 0], g.ab[:, 1], color=colour[g.kind], lw=max(1.5, g.width / 4.0), solid_capstyle='butt', zorder=2)
        m = g.ab[len(g.ab) // 2]
        ax.annotate('%s %.0f m' % (g.id, g.length), m, textcoords='offset points', xytext=(8, 8), fontsize=10, zorder=5)
        k = len(g.ab) // 3
        d = unit(g.ab[k + 1] - g.ab[k])
        ax.annotate('', g.ab[k] + d * 30, g.ab[k], arrowprops=dict(arrowstyle='->', color='white', lw=1.6), zorder=4)
    for name, p in (('F 갈림길', info['nose']), ('J 합류 교차로', (A_S, info['x2'][1])), ('출발', flat[0][0][15]), ('안녕, 광안리', (250, 22)),
                    ('주탑', (425, B_BRIDGE)), ('주탑', (75, B_BRIDGE)), ('터널 입구', (A_PW, B_BACK)), ('터널 출구', (A_PE, B_BACK)), ('산', (250, -520))):
        ax.plot([p[0]], [p[1]], 'o', color='black', ms=4, zorder=6)
        ax.annotate(name, p, textcoords='offset points', xytext=(6, -14), fontsize=11, zorder=6)
    ax.set_aspect('equal')
    ax.set_xlim(-650, 1150)
    ax.set_ylim(650, -1000)                                           # the sea at the bottom, as in plan.md
    ax.set_xlabel('a (m): 광안리 도로 방향, 오른쪽이 북동(해운대)')
    ax.set_ylabel('b (m): 아래가 바다')
    ax.grid(True, color='#cccccc', lw=0.5)
    ax.set_title('축약 부산 도로망: 시계 방향 고리, 모든 회전은 우회전')
    for r, (ab, y, lane, speed, starts, s), c in zip(routes, flat, ('#c8553d', '#3d6fb4')):
        ax2.plot(s[:-1], y, color=c, label='%s (%.0f m)' % (r['name'], s[-1]))
        ax2.plot(s[:-1], speed, color=c, ls=':', lw=1)
        ax2.plot(s[:-1], lane, color=c, ls='--', lw=1)
        for sid, i in starts.items():
            ax2.annotate(sid, (s[i], y[i]), textcoords='offset points', xytext=(0, 6 if c == '#c8553d' else 16), fontsize=8, color=c)
    ax2.set_xlabel('경로를 따라 간 거리 (m). 실선 높이 m, 점선 속도 m/s, 파선 차로 위치 m')
    ax2.legend(loc='upper right')
    ax2.grid(True, color='#cccccc', lw=0.5)
    fig.tight_layout()
    fig.savefig(png, dpi=110)


TRAFFIC_OUT = OUT.with_name('LandmarkTraffic.json')
STUB_OFF, STUB_DEEP = -5.25, 24.0          # the second tunnel mouth (districts/ring.py): its middle, left of the tunnel line; how far in cars start
UNDER_DEPTH, UNDER_RAMP = 4.4, 64.0        # the two underpasses of the oncoming line: floor below the road, length of each ramp


def traffic(S, routes):
    """Lines for the moving cars (LoopTraffic.cs), in a file JsonUtility reads: flat arrays.

    'loop': the bridge route in the lane next to the median, all the way round; the routes use other lanes everywhere.
    'oncoming': the other carriageway of every two-way road, from the second mouth of the tunnel on the Seomyeon side by Seomyeon,
    Gwangalli and Haeundae to the second mouth on the Haeundae side. Cars come out of one mouth and go into the other.
    At the junction J and at the fork F that carriageway has to get across the bridge ramp's lanes: it goes under them. The line
    dips into an underpass before each (a ramp down in its own lane, out of sight under the road, a ramp up in its lane beyond);
    districts/ring.py builds the two ramps and mouths of each from this line's heights. (On the level its cars drove across the
    lanes of a player bound for the bridge or coming down from it.)"""
    # in the lane by the median, except on the ramps and the bridge, where it takes the bridge route's own lane (the cars ahead
    # of a player on the bridge); route 0 never goes there
    loop = {'name': 'loop', 'segments': routes[1]['segments'],
            'lanes': {sid: (routes[1]['lanes'][sid] if sid in ('A1', 'A2', 'A3') else 0) for sid in routes[1]['segments']}}
    ab, y, lane, speed, starts, _ = flatten(S, loop)
    n = len(ab)
    t = np.array([unit(ab[(i + 1) % n] - ab[i - 1]) for i in range(n)])
    line = ab + np.array([right_of(v) for v in t]) * lane[:, None]
    # the bridge's other lanes: room beside the line, in lanes (the line is in lane 1; lane 0 on the ramps and the span, lanes 2
    # and 3 on the span only). Each car keeps a lane of its own (LoopTraffic.cs) as far as the room allows; the room opens and
    # closes at no more than a lane in 80 m, inside the segments that have it, so a car never drives where there is no lane.
    order = loop['segments']
    tl, tr = np.zeros(n), np.zeros(n)
    for k, sid in enumerate(order):
        i0, i1 = starts[sid], (starts[order[k + 1]] if k + 1 < len(order) else n)
        if sid in ('A1', 'A2', 'A3'):
            tl[i0:i1] = 1.0
        if sid == 'A2':
            tr[i0:i1] = 2.0
    # over the bridge the loop's cars go at about 55 km/h (the player at 80 overtakes them lane after lane, and as many cars are
    # on the bridge at once as the slower speed gathers there); eased in and out at 2 m/s per 10 m like the other speeds
    v = speed * 0.92
    v[tl > 0] = np.minimum(v[tl > 0], 15.0)
    dv = np.linalg.norm(np.diff(np.vstack([line, line[:1]]), axis=0), axis=1)
    for i in list(range(n)) * 2:
        j = (i + 1) % n
        v[j] = min(v[j], v[i] + 0.2 * dv[i])
    for i in list(range(n - 1, -1, -1)) * 2:
        j = (i - 1) % n
        v[j] = min(v[j], v[i] + 0.2 * dv[j])
    paths = [('loop', True, line, y, v, 48, (tl, tr))]       # the room is worked out below, clear of the other lines
    # the players' own lanes: each route's line, as the player drives it (LoopTraffic keeps these cars ahead of a player who
    # comes up behind them, up to a limit; nothing collides yet, so a player faster still drives through)
    for k, (name, cars) in enumerate((('route0', 18),)):
        ab_k, y_k, lane_k, speed_k, _, _ = flatten(S, routes[k])
        n_k = len(ab_k)
        t_k = np.array([unit(ab_k[(i + 1) % n_k] - ab_k[i - 1]) for i in range(n_k)])
        paths.append((name, True, ab_k + np.array([right_of(v) for v in t_k]) * lane_k[:, None], y_k, speed_k * 0.9, cars, None))

    lanes = {'R1': 1, 'T': 1, 'S': 0, 'C1': 0, 'B2': 0, 'G': 0, 'B1': 0, 'F': 0, 'H1': 0, 'H0': 1}
    anchors = {}

    def ease_(v):
        v = max(0.0, min(1.0, v))
        return v * v * (3 - 2 * v)

    def turn_span(seg):
        """Distance along the segment where its bend begins and ends (its heading within a degree of the first and the last)."""
        t0, t1 = seg.tangent(0), seg.tangent(len(seg.ab) - 1)
        i0 = next(i for i in range(len(seg.ab)) if float(seg.tangent(i) @ t0) < math.cos(math.radians(1.0)))
        i1 = next(i for i in range(len(seg.ab) - 1, -1, -1) if float(seg.tangent(i) @ t1) < math.cos(math.radians(1.0)))
        return float(seg.s[i0]), float(seg.s[i1])

    def lane_point(seg, s_, lane_):
        i = int(np.argmin(np.abs(seg.s - s_)))
        return seg.ab[i] + right_of(seg.tangent(i)) * -(seg.median / 2 + (lane_ + 0.5) * seg.lane_w)
    tn = S['TN']
    along = unit(tn.ab[-1] - tn.ab[0])
    side = right_of(along)
    pts = [tn.ab[0] + along * STUB_DEEP + side * STUB_OFF, tn.ab[0] + side * STUB_OFF]
    spd = [12.0, 12.0]
    for sid in ('R1', 'T', 'S', 'C1', 'B2', 'G', 'B1', 'F', 'H1', 'H0'):
        seg = S[sid]
        off = -(seg.median / 2 + (lanes[sid] + 0.5) * seg.lane_w)
        offs = np.full(len(seg.ab), off)
        # the two coast-road segments: the outer lane where their underpass ramp is (toward the fork, toward the junction), the
        # lane by the centre line toward Gwangalli, where each narrows to that road's one lane
        if sid in ('B1', 'B2'):
            a0_, a1_ = turn_span(seg)
            anchors[sid] = (a0_, a1_)
            for i in range(len(seg.ab)):
                w = (1.0 - ease_((seg.s[i] - (a1_ + 96.0)) / 22.0)) if sid == 'B1' else ease_((seg.s[i] - (a0_ - 116.0)) / 24.0)
                offs[i] = -(seg.median / 2 + (w + 0.5) * seg.lane_w)
        own = [seg.ab[i] + right_of(seg.tangent(i)) * offs[i] for i in range(len(seg.ab))][::-1]
        if sid == 'G':
            # the Gwangalli district draws its road on the surveyed line, not on the rounded one the routes drive (they part by
            # up to 1.7 m at the bend): the oncoming lane is laid on the road as drawn, or its cars ride the kerb
            raw = local(gwangalli_centre()[0])[::-1]
            own = [raw[i] + right_of(unit(raw[min(i + 1, len(raw) - 1)] - raw[max(i - 1, 0)])) * off for i in range(len(raw))][::-1]
        skip = 3 if sid in ('R1', 'H0') else 0                # room to slide across to the mouth
        own = own[skip:] if sid == 'R1' else (own[:len(own) - skip] if sid == 'H0' else own)
        pts += own
        spd += [float(min(seg.speed) * 0.9)] * len(own)
    pts += [tn.ab[-1] + side * STUB_OFF, tn.ab[-1] - along * STUB_DEEP + side * STUB_OFF]
    spd += [12.0, 12.0]
    pts, spd = np.array(pts), np.array(spd)
    d = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(pts, axis=0), axis=1))])
    keep = np.concatenate([[True], np.diff(d) > 0.05])
    pts, spd, d = pts[keep], spd[keep], d[keep]
    at = np.arange(0.0, d[-1], STEP)
    pts = np.stack([np.interp(at, d, pts[:, 0]), np.interp(at, d, pts[:, 1])], axis=1)
    spd = np.interp(at, d, spd)
    for _ in range(3):                                         # round the steps between one road's lane and the next's
        mid = pts.copy()
        mid[2:-2] = (pts[:-4] + pts[1:-3] + pts[2:-2] + pts[3:-1] + pts[4:]) / 5.0
        pts = mid
        sm = spd.copy()
        sm[2:-2] = (spd[:-4] + spd[1:-3] + spd[2:-2] + spd[3:-1] + spd[4:]) / 5.0
        spd = sm
    # the two underpasses: between a mouth before the ramp's lanes and a mouth beyond them the line is UNDER_DEPTH below the road;
    # a ramp of UNDER_RAMP leads down to the first and up from the second
    dd = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(pts, axis=0), axis=1))])
    depth = np.zeros(len(pts))
    under = []
    # (each mouth is past the zebra crossing that lies across its road, 66 m before J and 56 m before the fork: the ramp begins
    # or ends beyond the crossing, and the crossing lies on the road over the underpass)
    for name, m_in, m_out in (('J', lane_point(S['C1'], 75.0, 0), lane_point(S['B2'], anchors['B2'][0] - 24.0, 1)),
                              ('F', lane_point(S['B1'], anchors['B1'][1] + 24.0, 1), lane_point(S['F'], S['F'].s[-1] - 64.5, 0))):
        i_in = int(np.argmin(np.linalg.norm(pts - m_in, axis=1)))
        i_out = int(np.argmin(np.linalg.norm(pts - m_out, axis=1)))
        assert i_out > i_in + 8, (name, i_in, i_out)
        for i in range(len(pts)):
            if dd[i] <= dd[i_in]:
                v = ease_((dd[i] - (dd[i_in] - UNDER_RAMP)) / UNDER_RAMP)
            elif dd[i] >= dd[i_out]:
                v = 1.0 - ease_((dd[i] - dd[i_out]) / UNDER_RAMP)
            else:
                v = 1.0
            depth[i] = max(depth[i], UNDER_DEPTH * v)
        under.append((name, i_in, i_out, float(dd[i_out] - dd[i_in])))
    depth[depth < 0.01] = 0.0
    print('underpasses: ' + ', '.join('%s %d points, %.0f m under the road' % (n_, o_ - i_ + 1, l_) for n_, i_, o_, l_ in under))
    paths.append(('oncoming', False, pts, ROAD_Y - depth, spd, 26, None))

    # the room beside the loop line: none where a lane of its own would come within 3.5 m of another line's car (the end of the
    # down ramp, where the coast road comes alongside to merge), then opened and closed at no more than a lane in 80 m
    from scipy.spatial import cKDTree
    others = cKDTree(np.vstack([p for name, _, p, _, _, _, _ in paths if name != 'loop']))
    tl, tr = paths[0][6]
    rgt = np.array([right_of(v) for v in t])
    step = np.linalg.norm(np.diff(np.vstack([line, line[:1]]), axis=0), axis=1)
    wide = []
    for tgt, sign in ((tl, -1.0), (tr, 1.0)):
        w = tgt.copy()
        for k in (1, 2):
            near = others.query(line + rgt * (sign * k * LANE))[0] < 3.5
            w[near & (w >= k - 0.5)] = k - 1.0
        for i in list(range(n)) * 2:
            j = (i + 1) % n
            w[j] = min(w[j], w[i] + step[i] / 80.0)
        for i in list(range(n - 1, -1, -1)) * 2:
            j = (i - 1) % n
            w[j] = min(w[j], w[i] + step[j] / 80.0)
        wide.append(w)
    paths[0] = paths[0][:6] + (wide,)

    out = []
    for name, closed, p, yy, v, cars, wide in paths:
        w = world(p)
        out.append({'name': name, 'closed': closed, 'cars': cars, 'x': [round(float(q), 2) for q in w[:, 0]], 'y': [round(float(q), 3) for q in yy],
                    'z': [round(float(q), 2) for q in w[:, 1]], 'speed': [round(float(q), 2) for q in v]})
        if wide is not None:
            out[-1]['wideL'], out[-1]['wideR'] = [round(float(q), 3) for q in wide[0]], [round(float(q), 3) for q in wide[1]]
    TRAFFIC_OUT.write_text(json.dumps({'paths': out}, separators=(',', ':')), encoding='utf-8')
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--png', default='')
    args = ap.parse_args()
    S, routes, info = build()
    flat = [flatten(S, r) for r in routes]
    problems, notes = check(S, routes, flat, info)
    out = write(S, routes, flat, info)
    lines = traffic(S, routes)
    print('traffic: ' + ', '.join('%s %d points, %d cars' % (p['name'], len(p['x']), p['cars']) for p in lines))
    print('segments: ' + ', '.join('%s %.0f' % (g.id, g.length) for g in S.values()))
    print('ramp grades: up %.1f%%, down %.1f%%' % (info['grades'][0] * 100, info['grades'][1] * 100))
    print('Gwangalli ends in the local frame: NE (%.0f, %.1f), SW (%.0f, %.1f); lines meet the boulevards at b = %.1f (F side) and %.1f (J side)'
          % (info['g_ab'][-1][0], info['g_ab'][-1][1], info['g_ab'][0][0], info['g_ab'][0][1], info['x1'][1], info['x2'][1]))
    for n in notes:
        print('note: ' + n)
    for p in problems:
        print('PROBLEM: ' + p)
    print('wrote %s (%.0f KB), routes %s' % (OUT.name, OUT.stat().st_size / 1024, ', '.join('%s %.0f m' % (r['name'], r['length']) for r in out['routes'])))
    if args.png:
        draw(S, routes, flat, info, args.png)
        print('drew ' + args.png)
    sys.exit(1 if problems else 0)


if __name__ == '__main__':
    main()
