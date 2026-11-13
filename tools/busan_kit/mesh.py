"""Triangle meshes in Unity's conventions, wall planes for facades, and the small shapely helpers they use."""
import math

import mapbox_earcut
import numpy as np
from shapely.geometry import Point, Polygon
from shapely.geometry.polygon import orient


def polys(g):
    if g.is_empty:
        return []
    if g.geom_type == 'Polygon':
        return [g] if g.area > 0.02 else []
    return [p for q in getattr(g, 'geoms', []) for p in polys(q)]


def lines(g):
    if g.is_empty:
        return []
    if g.geom_type == 'LineString':
        return [g]
    return [l for q in getattr(g, 'geoms', []) for l in lines(q)]


def flat(y):
    return lambda a: np.full(len(a), y)


# Where every round post stands (lamps, signals, gantry legs, bollards): districts/trees.py keeps its trees clear of them.
POSTS = []


class Mesh:
    """Triangles of one material. Ground materials need positions only; surface materials carry UVs (metres) and colours."""

    def __init__(self, uv):
        self.has_uv = uv
        self.v, self.uv, self.c, self.t = [], [], [], []

    def _add(self, pts, uvs, col):
        n = len(self.v) // 3
        for p in pts:
            self.v += [round(p[0], 3), round(p[1], 3), round(p[2], 3)]
        if self.has_uv:
            for u in uvs:
                self.uv += [round(u[0], 4), round(u[1], 4)]
            for _ in pts:
                self.c += [round(col[0], 3), round(col[1], 3), round(col[2], 3)]
        return n

    def quad(self, p0, p1, p2, p3, out, uv=((0, 0), (1, 0), (1, 1), (0, 1)), col=(1, 1, 1)):
        """A quad that faces `out` (Unity front faces: cross(b - a, c - a) points at the viewer)."""
        ax, ay, az = p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]
        bx, by, bz = p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2]
        n = (ay * bz - az * by, az * bx - ax * bz, ax * by - ay * bx)
        pts, uvs = [p0, p1, p2, p3], list(uv)
        if n[0] * out[0] + n[1] * out[1] + n[2] * out[2] < 0:
            pts, uvs = [p0, p3, p2, p1], [uv[0], uv[3], uv[2], uv[1]]
        i = self._add(pts, uvs, col)
        self.t += [i, i + 1, i + 2, i, i + 2, i + 3]

    def quad2(self, p0, p1, p2, p3, out, uv=((0, 0), (1, 0), (1, 1), (0, 1)), col=(1, 1, 1)):
        self.quad(p0, p1, p2, p3, out, uv, col)
        self.quad(p0, p1, p2, p3, (-out[0], -out[1], -out[2]), uv, col)

    def polygon(self, poly, y_of, col=(1, 1, 1), uvscale=3.0):
        for p in polys(poly):
            rings = [list(p.exterior.coords)[:-1]] + [list(h.coords)[:-1] for h in p.interiors]
            pts, ends = [], []
            for r in rings:
                pts += r
                ends.append(len(pts))
            if len(pts) < 3:
                continue
            arr = np.array(pts, dtype=np.float64)
            idx = mapbox_earcut.triangulate_float64(arr, np.array(ends, dtype=np.uint32))
            ys = y_of(arr)
            i0 = self._add([(arr[i][0], float(ys[i]), arr[i][1]) for i in range(len(arr))], [(a[0] / uvscale, a[1] / uvscale) for a in arr], col)
            for k in range(0, len(idx), 3):
                a, b, c = int(idx[k]), int(idx[k + 1]), int(idx[k + 2])
                if (arr[b][0] - arr[a][0]) * (arr[c][1] - arr[a][1]) - (arr[b][1] - arr[a][1]) * (arr[c][0] - arr[a][0]) > 0:
                    b, c = c, b
                self.t += [i0 + a, i0 + b, i0 + c]

    def walls(self, poly, y0, y1, col=(1, 1, 1), uvscale=3.0):
        """Vertical faces around a polygon, facing out of it."""
        for p in polys(poly):
            p = orient(p, 1.0)
            for ring in [p.exterior] + list(p.interiors):
                c = list(ring.coords)
                s = 0.0
                for (ax, az), (bx, bz) in zip(c, c[1:]):
                    L = math.hypot(bx - ax, bz - az)
                    if L < 1e-4:
                        continue
                    out = ((bz - az) / L, 0.0, -(bx - ax) / L)
                    self.quad((ax, y0, az), (bx, y0, bz), (bx, y1, bz), (ax, y1, az), out,
                              ((s / uvscale, y0 / uvscale), ((s + L) / uvscale, y0 / uvscale), ((s + L) / uvscale, y1 / uvscale), (s / uvscale, y1 / uvscale)), col)
                    s += L

    def obox(self, cx, cz, ux, uz, hl, hw, y0, y1, col=(1, 1, 1), uvscale=1.5, top=True, bottom=False):
        """A box with its long axis along (ux, uz)."""
        px, pz = -uz, ux
        c = [(cx + ux * a * hl + px * b * hw, cz + uz * a * hl + pz * b * hw) for a, b in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        self.walls(Polygon(c), y0, y1, col, uvscale)
        if top:
            self.polygon(Polygon(c), flat(y1), col, uvscale)
        if bottom:
            self.quad((c[0][0], y0, c[0][1]), (c[1][0], y0, c[1][1]), (c[2][0], y0, c[2][1]), (c[3][0], y0, c[3][1]), (0, -1, 0), col=col)

    def lathe(self, cx, cz, profile, n=12, col=(1, 1, 1)):
        """A round thing turned about a vertical axis: profile = [(radius, y), ...] from the bottom up. The rings share their
        vertices, so Unity shades it smooth (separate quads showed every facet); repeat a profile point for a hard edge."""
        i = len(self.v) // 3
        pts, uvs = [], []
        for r, y in profile:
            for k in range(n):
                a = 2 * math.pi * k / n
                pts.append((cx + r * math.cos(a), y, cz + r * math.sin(a)))
                uvs.append((k / n * max(r, 0.05) * 6.3, y))
        self._add(pts, uvs, col)
        for j in range(len(profile) - 1):
            if profile[j] == profile[j + 1]:
                continue
            lo, hi = i + j * n, i + (j + 1) * n
            for k in range(n):
                k1 = (k + 1) % n
                self.t += [lo + k, hi + k1, lo + k1, lo + k, hi + k, hi + k1]

    def frustum(self, cx, cz, r0, r1, y0, y1, n=10, col=(1, 1, 1), cap=True):
        POSTS.append((cx, cz))
        self.lathe(cx, cz, [(r0, y0), (r1, y1)], n, col)
        if cap:
            self.polygon(Point(cx, cz).buffer(r1, quad_segs=max(2, n // 4)), flat(y1), col, 1.0)


def vdisc(mesh, cx, cy, cz, r, nx, nz, cell=None, col=(1, 1, 1)):
    """An upright disc that faces (nx, nz); `cell` = which of the four road signs (u range), None for a plain back."""
    rx, rz = -nz, nx                                   # the viewer's right when looking at its face
    pts, uvs = [], []
    for k in range(21):
        a = 2 * math.pi * k / 20
        pts.append((cx + rx * r * math.cos(a), cy + r * math.sin(a), cz + rz * r * math.cos(a)))
        uvs.append((((cell or 0) + 0.5 + 0.49 * math.cos(a)) / 4.0, 0.5 + 0.49 * math.sin(a)))
    mid, muv = (cx, cy, cz), (((cell or 0) + 0.5) / 4.0, 0.5)
    for k in range(20):
        mesh.quad(mid, pts[k], pts[k + 1], mid, (nx, 0.0, nz), (muv, uvs[k], uvs[k + 1], muv), col)


class Face:
    """A wall plane: s runs along it from (ax, az), y up from y0, d out of the wall."""

    def __init__(self, ax, az, bx, bz, y0):
        self.L = math.hypot(bx - ax, bz - az)
        self.ax, self.az, self.y0 = ax, az, y0
        self.ux, self.uz = (bx - ax) / self.L, (bz - az) / self.L
        self.nx, self.nz = self.uz, -self.ux              # outward for a counter-clockwise footprint
        self.out = (self.nx, 0.0, self.nz)

    def P(self, s, y, d=0.0):
        return (self.ax + self.ux * s + self.nx * d, self.y0 + y, self.az + self.uz * s + self.nz * d)

    def q(self, m, s0, s1, y0, y1, d=0.0, col=(1, 1, 1), uv=None, both=False):
        if s1 - s0 < 1e-3 or y1 - y0 < 1e-3:
            return
        uv = uv or ((s0 / 3.0, y0 / 3.0), (s1 / 3.0, y0 / 3.0), (s1 / 3.0, y1 / 3.0), (s0 / 3.0, y1 / 3.0))
        (m.quad2 if both else m.quad)(self.P(s0, y0, d), self.P(s1, y0, d), self.P(s1, y1, d), self.P(s0, y1, d), self.out, uv, col)

    def sides(self, m, s0, s1, y0, y1, d0, d1, col=(1, 1, 1), inward=False):
        """The four faces between depths d0 and d1 around the rectangle: a box's sides (inward=False) or a recess's reveals."""
        k = -1.0 if inward else 1.0
        u, up = (self.ux * k, 0.0, self.uz * k), (0.0, k, 0.0)
        m.quad(self.P(s0, y0, d0), self.P(s0, y0, d1), self.P(s0, y1, d1), self.P(s0, y1, d0), (-u[0], 0, -u[2]), col=col)
        m.quad(self.P(s1, y0, d0), self.P(s1, y0, d1), self.P(s1, y1, d1), self.P(s1, y1, d0), u, col=col)
        m.quad(self.P(s0, y1, d0), self.P(s1, y1, d0), self.P(s1, y1, d1), self.P(s0, y1, d1), up, col=col)
        m.quad(self.P(s0, y0, d0), self.P(s1, y0, d0), self.P(s1, y0, d1), self.P(s0, y0, d1), (0.0, -k, 0.0), col=col)

    def box(self, m, s0, s1, y0, y1, d0, d1, col=(1, 1, 1), uv=None, front=None):
        # the front may go to another material (a sign)
        self.q(front or m, s0, s1, y0, y1, d1, col if front is None else (1, 1, 1), uv)
        self.sides(m, s0, s1, y0, y1, d0, d1, col)
