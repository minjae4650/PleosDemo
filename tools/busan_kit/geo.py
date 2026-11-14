"""Where the source data lives, and map coordinates made from OpenStreetMap ways.

Coordinates are the drive map's world coordinates (x east, z north, metres); heights are metres above the sea plane."""
import json
import math
from pathlib import Path

import numpy as np
from shapely.geometry import LineString, Point
from shapely.ops import substring

ROOT = Path(__file__).resolve().parents[2]
CONCEPTS = ROOT / 'docs/concepts'
DATA = ROOT / 'unity/InputProbe/Assets/BusanEnvironment/Editor/Data'


def proj(lat, lon):
    return ((lon - 129.06) * 111320.0 * math.cos(math.radians(35.12)), (lat - 35.12) * 110574.0)


def osm(name):
    """The elements of one OpenStreetMap extract in docs/concepts (ODbL)."""
    return json.loads((CONCEPTS / name).read_text(encoding='utf-8'))['elements']


def way_line(way):
    return LineString([proj(p['lat'], p['lon']) for p in way['geometry']])


def road_centre(way, a, b, margin, step=4.0):
    """The way from the point nearest `a` to the point nearest `b`, `margin` metres more at both ends (as far as the way goes),
    resampled every `step` metres. Returns the line and the distance along it of the point nearest `a`."""
    full = way_line(way)
    a, b = full.project(Point(a)), full.project(Point(b))
    lo, hi = max(0.0, a - margin), min(full.length, b + margin)
    centre = substring(full, lo, hi)
    centre = LineString([centre.interpolate(s).coords[0] for s in np.arange(0.0, centre.length, step)] + [centre.coords[-1]])
    return centre, a - lo
