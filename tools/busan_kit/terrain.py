"""The ground that is not street: sand, the sea plane, flat far land from the drive map's ground grid, hills from the terrain grid."""
import json
import math
from pathlib import Path

import numpy as np
import shapely
from shapely.geometry import Point, box

from .mesh import flat


class Grid:
    """The terrain grid (BusanTerrain.json): X, Y (height) and Z as rows x columns."""

    def __init__(self, path):
        T = json.loads(Path(path).read_text())
        self.cols, self.rows = T['columns'], T['rows']
        tc, tr = self.cols, self.rows
        self.X = np.array([v['x'] for v in T['vertices']]).reshape(tr, tc)
        self.Y = np.array([v['y'] for v in T['vertices']]).reshape(tr, tc)
        self.Z = np.array([v['z'] for v in T['vertices']]).reshape(tr, tc)

    def height(self, x, z):
        TX, TY, TZ, tc, tr = self.X, self.Y, self.Z, self.cols, self.rows
        fx, fz = (x - TX[0, 0]) / (TX[0, 1] - TX[0, 0]), (z - TZ[0, 0]) / (TZ[1, 0] - TZ[0, 0])
        c, r = int(min(max(fx, 0), tc - 2)), int(min(max(fz, 0), tr - 2))
        tx, tz = fx - c, fz - r
        return (TY[r, c] * (1 - tx) + TY[r, c + 1] * tx) * (1 - tz) + (TY[r + 1, c] * (1 - tx) + TY[r + 1, c + 1] * tx) * tz


def hills(kit, grid, within, min_top, clear, sink):
    """One quad per terrain cell whose corner lies inside `within` = (x0, z0, x1, z1), that rises to `min_top` m somewhere and
    whose corner is `clear` m or more from the road; sunk `sink` m. Returns the number of cells."""
    TX, TY, TZ = grid.X, grid.Y, grid.Z
    lx, lz, hx, hz = within
    n = 0
    for r in range(grid.rows - 1):
        for c in range(grid.cols - 1):
            x0, z0 = TX[r, c], TZ[r, c]
            if not (lx < x0 < hx and lz < z0 < hz):
                continue
            hs = [TY[r, c], TY[r, c + 1], TY[r + 1, c + 1], TY[r + 1, c]]
            if max(hs) < min_top or Point(x0, z0).distance(kit.centre) < clear:
                continue
            p = [(TX[r, c], hs[0] - sink, TZ[r, c]), (TX[r, c + 1], hs[1] - sink, TZ[r, c + 1]), (TX[r + 1, c + 1], hs[2] - sink, TZ[r + 1, c + 1]), (TX[r + 1, c], hs[3] - sink, TZ[r + 1, c])]
            kit.M['hill'].quad(p[0], p[1], p[2], p[3], (0, 1, 0))
            n += 1
    return n


def ground_land(path):
    """Every cell of the drive map's ground grid (BusanSliceGround.json) that is not sea (class 1), as one shape."""
    G = json.loads(Path(path).read_text(encoding='utf-8'))
    cls = np.array(G['c']).reshape(G['rows'], G['cols'])
    rr, cc = np.nonzero(cls != 1)
    st = G['step']
    return shapely.union_all(shapely.box(G['x0'] + cc * st - st / 2, G['z0'] + rr * st - st / 2, G['x0'] + cc * st + st / 2, G['z0'] + rr * st + st / 2))


def far_land(kit, area):
    """Flat land a little below the paving, with a wall down to the sea plane."""
    kit.M['yard'].polygon(area, flat(kit.base - 0.07))
    kit.M['stone'].walls(area, 0.0, kit.base - 0.07, (0.7, 0.7, 0.69), 3.0)


def beach_sand(kit, sand, water_edge, top):
    """Sand in 8 m cells, `top` high inland, sloping down to 0.15 m over the last 14 m to `water_edge`."""
    def sand_y(arr):
        return np.array([0.15 + (top - 0.15) * min(1.0, Point(x, z).distance(water_edge) / 14.0) for x, z in arr])
    minx, minz, maxx, maxz = sand.bounds
    for gx in np.arange(math.floor(minx / 8) * 8, maxx, 8.0):
        for gz in np.arange(math.floor(minz / 8) * 8, maxz, 8.0):
            kit.M['sand'].polygon(sand.intersection(box(gx, gz, gx + 8, gz + 8)), sand_y)


def sea(kit, area):
    kit.M['sea'].polygon(area, flat(0.0))
