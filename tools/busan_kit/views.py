"""Camera views for the builder's pictures."""
import math


def look(kit, name, s, turn, dist=40.0, y=1.2, off=None, fov=62):
    """The driver's eye `y` above the road at s, `off` right of the centre (default: the middle of the right-hand lane), looking
    `dist` ahead and turned `turn` degrees (> 0: to the right). 62 degrees is the game camera's field of view."""
    if off is None:
        off = kit.half / 2
    x, z, ux, uz = kit.pose(s, off)
    c, k = math.cos(math.radians(turn)), math.sin(math.radians(turn))
    rx, rz = ux * c + uz * k, uz * c - ux * k         # turn > 0: to the right
    return {'name': name, 'px': x, 'py': kit.road_y + y, 'pz': z, 'tx': x + rx * dist, 'ty': kit.road_y + y - 0.25, 'tz': z + rz * dist, 'fov': fov}


def above(kit, name, s, off, back, ahead, side, height, fov):
    """From `height` m up, `back` m behind the point at (s, off), looking at the sea plane `ahead` m beyond it and `side` m to the right."""
    x, z, ux, uz = kit.pose(s, off)
    return {'name': name, 'px': x - ux * back, 'py': height, 'pz': z - uz * back, 'tx': x + ux * ahead + uz * side, 'ty': 0.0, 'tz': z + uz * ahead - ux * side, 'fov': fov}
