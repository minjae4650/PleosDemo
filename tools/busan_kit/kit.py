"""The context every part draws with."""
import copy
import random

from .mesh import Mesh


class Kit:
    """One mesh per material (`M`, in the order of `mats`), the district's one random stream (`rng`), the road and paving heights,
    and the road the street parts line: its centre line, length `LEN` and half width `half` (kerb line to centre line).

    Every part appends to the shared meshes and draws from the shared stream, so the order of the calls is part of the output."""

    def __init__(self, mats, seed, road_y, kerb, half, centre=None):
        self.mats = mats
        self.rng = random.Random(seed)
        self.M = {k: Mesh(v['kind'] != 'ground') for k, v in mats.items()}
        self.road_y, self.kerb, self.half = road_y, kerb, half
        self.base = road_y + kerb          # paving level
        self.centre, self.LEN = None, 0.0
        if centre is not None:
            self.centre, self.LEN = centre, centre.length

    def along(self, centre, half=None):
        """The same meshes and random stream, lining another road."""
        k = copy.copy(self)
        k.centre, k.LEN = centre, centre.length
        if half is not None:
            k.half = half
        return k

    def pose(self, s, off=0.0):
        """Point at distance s along the road, `off` metres to the right of travel (toward the sea); and the travel direction."""
        s = min(max(s, 0.0), self.LEN - 1.0)
        p, q = self.centre.interpolate(s), self.centre.interpolate(s + 1.0)
        ux, uz = q.x - p.x, q.y - p.y
        return p.x + uz * off, p.y - ux * off, ux, uz
