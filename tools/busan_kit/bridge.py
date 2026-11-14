"""Bridges. For now only the far silhouette of a suspension bridge, as seen from the Gwangalli beach road."""
import numpy as np
from shapely.geometry import Point


def silhouette(kit, deck, towers, deck_y, deck_depth, deck_w, tower_y):
    """Deck boxes every 30 m along the `deck` line, piers every 60 m outside the suspended spans (200 m side spans), a pair of
    towers with two cross beams at each of `towers`, and the main cables on both deck edges."""
    M = kit.M
    span = sorted(deck.project(Point(t)) for t in towers)
    for s in np.arange(0.0, deck.length - 30.0, 30.0):
        p, q = deck.interpolate(s), deck.interpolate(s + 30.0)
        M['bridge'].obox((p.x + q.x) / 2, (p.y + q.y) / 2, (q.x - p.x) / 30.0, (q.y - p.y) / 30.0, 15.2, deck_w / 2, deck_y - deck_depth, deck_y, (0.9, 0.9, 0.9), 10.0, bottom=True)
    for s in np.arange(0.0, deck.length, 60.0):
        if span[0] - 200.0 < s < span[1] + 200.0:
            continue
        p, q = deck.interpolate(s), deck.interpolate(min(deck.length, s + 1.0))
        M['bridge'].obox(p.x, p.y, q.x - p.x, q.y - p.y, 2.0, 8.0, 0.0, deck_y - deck_depth, (0.85, 0.85, 0.85), 10.0)
    for s in span:
        p, q = deck.interpolate(s), deck.interpolate(s + 1.0)
        ux, uz = q.x - p.x, q.y - p.y
        for side in (-1, 1):
            M['bridge'].obox(p.x - uz * side * 14.0, p.y + ux * side * 14.0, ux, uz, 2.5, 2.0, 0.0, tower_y, (1, 1, 1), 10.0)
        for y in (58.0, 98.0):
            M['bridge'].obox(p.x, p.y, ux, uz, 2.0, 14.0, y, y + 4.0, (1, 1, 1), 10.0)

    def cable(s0, y0, s1, y1, sag):
        """A main cable between two points of the deck line, on both edges of the deck."""
        n = 24
        pts = []
        for k in range(n + 1):
            t = k / n
            p, q = deck.interpolate(s0 + (s1 - s0) * t), deck.interpolate(min(deck.length, s0 + (s1 - s0) * t + 1.0))
            pts.append((p.x, p.y, q.x - p.x, q.y - p.y, y0 + (y1 - y0) * t - sag * 4.0 * t * (1.0 - t)))
        for (xa, za, ux, uz, ya), (xb, zb, _, _, yb) in zip(pts, pts[1:]):
            for side in (-12.0, 12.0):
                ox, oz = -uz * side, ux * side
                M['bridge'].quad2((xa + ox, ya - 0.6, za + oz), (xb + ox, yb - 0.6, zb + oz), (xb + ox, yb + 0.6, zb + oz), (xa + ox, ya + 0.6, za + oz), (-uz, 0, ux), col=(0.95, 0.95, 0.95))
    cable(span[0], tower_y - 2.0, span[1], tower_y - 2.0, tower_y - deck_y - 8.0)
    cable(span[0] - 200.0, deck_y + 1.0, span[0], tower_y - 2.0, 6.0)
    cable(span[1], tower_y - 2.0, span[1] + 200.0, deck_y + 1.0, 6.0)
