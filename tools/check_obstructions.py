"""Anything standing in the way: geometry of a district between 0.3 and 2.6 m above the road within reach of a line that is driven.

    python tools/check_obstructions.py [district ...]

The lines are the lane lines of both routes (LandmarkNetwork.json) and the lines of the moving cars (LandmarkTraffic.json). A
vertex within `REACH` metres of one of them, at that height above it, is reported, grouped by material and by where it is along
the line. Cars are prefabs, not geometry, and are not looked at here."""
import importlib
import json
import sys

import numpy as np
from scipy.spatial import cKDTree

from busan_kit.geo import DATA
from busan_kit.materials import TEX_DIR

REACH = 1.5
SKIP = ('tree', 'asphalt', 'paint_white', 'paint_yellow', 'paint_red', 'tunnel_floor', 'tunnel_paint', 'sea')


def lines():
    net = json.loads((DATA / 'LandmarkNetwork.json').read_text(encoding='utf-8'))
    out = []
    for k, route in enumerate(net['routes']):
        p = np.array(route['path'], dtype=float)
        xz = p[:, [0, 2]]
        t = np.gradient(np.vstack([xz[-1:], xz, xz[:1]]), axis=0)[1:-1]
        t /= np.linalg.norm(t, axis=1)[:, None]
        line = xz + np.stack([t[:, 1], -t[:, 0]], axis=1) * np.array(route['lane'])[:, None]
        s = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(line, axis=0), axis=1))])
        out.append(('route %d' % k, line, p[:, 1], s))
    traffic = DATA / 'LandmarkTraffic.json'
    if traffic.exists():
        for path in json.loads(traffic.read_text(encoding='utf-8'))['paths']:
            line = np.stack([path['x'], path['z']], axis=1)
            s = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(line, axis=0), axis=1))])
            out.append(('traffic ' + path['name'], line, np.array(path['y']), s))
    dense = []
    for name, line, y, s in out:                         # a point every metre
        at = np.arange(0.0, s[-1], 1.0)
        dense.append((name, np.stack([np.interp(at, s, line[:, 0]), np.interp(at, s, line[:, 1])], axis=1), np.interp(at, s, y), at))
    return dense


def main():
    names = sys.argv[1:] or ['ring', 'gwangalli', 'bridge', 'haeundae', 'seomyeon', 'infill']
    L = lines()
    total = 0
    for name in names:
        district = importlib.import_module('districts.' + name)
        try:
            kit, scene = district.build(TEX_DIR, legacy=False)
        except TypeError:
            kit, scene = district.build(TEX_DIR)
        for mat, mesh in kit.M.items():
            if mat in SKIP or not mesh.v:
                continue
            v = np.array(mesh.v, dtype=float).reshape(-1, 3)
            tree = cKDTree(v[:, [0, 2]])
            for lname, pts, y, at in L:
                hits = {}
                for i, near in enumerate(tree.query_ball_point(pts, REACH)):
                    if not near:
                        continue
                    h = v[near, 1] - y[i]
                    n = int(((h > 0.3) & (h < 2.6)).sum())
                    if n:
                        hits[int(at[i] // 10) * 10] = hits.get(int(at[i] // 10) * 10, 0) + n
                if hits:
                    total += 1
                    keys = sorted(hits)
                    runs, start, prev = [], keys[0], keys[0]
                    for k in keys[1:] + [None]:
                        if k is None or k - prev > 10:
                            runs.append('%d-%d' % (start, prev + 10))
                            start = k
                        prev = k
                    print('%-10s %-14s %-16s s = %s' % (name, mat, lname, ', '.join(runs[:14]) + (' ...' if len(runs) > 14 else '')))
    print('obstructed line/material pairs: %d' % total)


if __name__ == '__main__':
    main()
