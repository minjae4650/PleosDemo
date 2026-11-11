"""Prepare the Paris driving scene from public OpenStreetMap extracts.

Requires Shapely >= 2.1 and Pillow. Raw map extracts live in build/paris-drive.
"""
import argparse
import hashlib
import heapq
import json
import math
from collections import defaultdict
from pathlib import Path
from urllib.request import Request, urlopen
import xml.etree.ElementTree as ET

from PIL import Image, ImageDraw
from shapely import STRtree, box, constrained_delaunay_triangles, get_parts, make_valid, union_all
from shapely.geometry import LineString, Point, Polygon
from shapely.ops import polygonize

ROOT = Path(__file__).resolve().parents[1]
CACHE = ROOT / "build/paris-drive"
ASSETS = ROOT / "unity/InputProbe/Assets/_Parkside/ParisDrive/Data"
ORIGIN = (48.85826, 2.29451)
LAT_METRES = 111195.08
LON_METRES = LAT_METRES * math.cos(math.radians(ORIGIN[0]))
AREA = union_all([
    box(-1050, -1070, 1100, 810),
    box(-660, 510, 1460, 1950),
    box(740, -660, 2330, 1430),
])
API = "https://api.openstreetmap.org/api/0.6/"
SOURCES = {
    **{f"osm-{i}.osm": "map?bbox=" + bbox for i, bbox in enumerate((
        "2.279,48.847,2.290,48.856", "2.290,48.847,2.300,48.856", "2.300,48.847,2.311,48.856",
        "2.279,48.856,2.290,48.866", "2.290,48.856,2.300,48.866", "2.300,48.856,2.311,48.866",
    ))},
    **{f"extension-{name}.osm": "map?bbox=" + bbox for name, bbox in {
        "north-west": "2.279,48.866,2.290,48.876",
        "north-centre": "2.290,48.866,2.301,48.876",
        "north-east": "2.301,48.866,2.312,48.876",
        "invalides": "2.310,48.848,2.321,48.857",
        "grand-palais": "2.310,48.857,2.321,48.866",
        "champs-east": "2.310,48.866,2.321,48.874",
        "concorde": "2.320,48.854,2.331,48.864",
        "tuileries-west": "2.320,48.864,2.331,48.873",
    }.items()},
    "seine.osm": "relation/2191006/full",
}
DRIVABLE = {"primary", "secondary", "tertiary", "unclassified", "residential",
            "primary_link", "secondary_link", "tertiary_link", "living_street"}
ANCHORS = (
    (48.86187, 2.28974, "Avenue de New York"),
    (48.85532, 2.28749, "Pont de Bir Hakeim"),
    (48.85417, 2.29245, "Avenue de Suffren"),
    (48.85590, 2.30000, "Avenue de la Bourdonnais"),
    (48.85388, 2.31110, "Avenue de Tourville"),
    (48.85316, 2.31231, "Place Vauban"),
    (48.85660, 2.31675, "Boulevard des Invalides"),
    (48.86282, 2.31979, "Pont de la Concorde"),
    (48.86566, 2.32044, "Place de la Concorde"),
    (48.86754, 2.31299, "Avenue des Champs-Élysées"),
    (48.87148, 2.30214, "Avenue des Champs-Élysées"),
    (48.87365, 2.29626, "Place Charles de Gaulle"),
    (48.86839, 2.29098, "Avenue Kléber"),
    (48.86483, 2.29957, "Avenue du Président Wilson"),
    (48.86322, 2.30188, "Pont de l'Alma"),
    (48.86187, 2.28974, "Avenue de New York"),
)
STATIONS = (
    (48.85558, 2.29245, "Tour Eiffel · Rue Jean Rey"),
    (48.86187, 2.28974, "Trocadéro · Avenue de New York"),
    (48.85532, 2.28749, "Pont de Bir-Hakeim"),
    (48.85374, 2.30105, "École Militaire"),
    (48.86362, 2.30118, "Pont de l’Alma"),
    (48.87365, 2.29626, "Arc de Triomphe"),
    (48.87148, 2.30214, "Avenue des Champs-Élysées"),
    (48.86566, 2.32044, "Place de la Concorde"),
    (48.85316, 2.31231, "Les Invalides · Place Vauban"),
)
LANDMARK_WAYS = {
    "arc": 226413508, "invalides": 112452790, "obelisk": 72937686,
    "fontaine_nord": 72937684, "fontaine_sud": 72937685,
}
BLOCKED_ACCESS = {"no", "private", "bus", "destination", "permit", "customers"}



def xy(lat, lon):
    return (round((lon - ORIGIN[1]) * LON_METRES, 3), round((lat - ORIGIN[0]) * LAT_METRES, 3))


def vector(p, y=0):
    return dict(x=round(p[0], 3), y=y, z=round(p[1], 3))


def tags(element):
    return {tag.get("k"): tag.get("v") for tag in element.findall("tag")}


def source_map():
    CACHE.mkdir(parents=True, exist_ok=True)
    elements, provenance = {}, {}
    for name, endpoint in SOURCES.items():
        path = CACHE / name
        if not path.exists():
            request = Request(API + endpoint, headers={"User-Agent": "PleosDemo Paris scene authoring"})
            with urlopen(request, timeout=45) as response:
                path.write_bytes(response.read())
        source = path.read_bytes()
        for item in ET.fromstring(source):
            if item.tag in ("node", "way", "relation"):
                key = item.tag, int(item.get("id"))
                previous = elements.get(key)
                if previous is None or int(item.get("version", "0")) >= int(previous.get("version", "0")):
                    elements[key] = item
        provenance[name] = dict(url=API + endpoint, sha256=hashlib.sha256(source).hexdigest())
    return elements, provenance


SHOP_AMENITIES = {"cafe", "restaurant", "fast_food", "bar", "pub", "pharmacy", "ice_cream"}

def populate_building_uses(buildings, elements):
    """Associate mapped shop and amenity nodes with the saved building outlines."""
    footprints = [Polygon([(p["x"], p["z"]) for p in b["points"]]) for b in buildings]
    index = STRtree(footprints)
    for b in buildings:
        way = elements.get(("way", b["osmId"]))
        attrs = tags(way) if way is not None else {}
        b.update(kind=attrs.get("building", ""), number=attrs.get("addr:housenumber", ""), retailUse=[])
    for (kind, _), element in elements.items():
        if kind != "node":
            continue
        attrs = tags(element)
        use = attrs.get("shop", "")
        if not use and attrs.get("amenity") in SHOP_AMENITIES:
            use = attrs["amenity"]
        if not use or use in {"no", "vacant", "yes"}:
            continue
        at = Point(xy(float(element.get("lat")), float(element.get("lon"))))
        hits = index.query(at, predicate="intersects")
        if len(hits) == 0:
            continue
        nearest = min(map(int, hits), key=lambda i: footprints[i].area)
        shops = buildings[nearest]["retailUse"]
        name = " ".join(attrs.get("name", "").split())[:42]
        record = dict(kind=use, name=name)
        if record not in shops and len(shops) < 8:
            shops.append(record)


def number(text, default):
    try:
        value = float(str(text).removesuffix("m").strip())
    except ValueError:
        return default
    return value if math.isfinite(value) and value > 0 else default


def polygons(geometry):
    if geometry.is_empty:
        return
    if geometry.geom_type == "Polygon":
        yield geometry
    elif hasattr(geometry, "geoms"):
        for part in geometry.geoms:
            yield from polygons(part)


def triangles(geometry, y=0):
    result = []
    for polygon in polygons(geometry):
        for face in get_parts(constrained_delaunay_triangles(polygon)):
            points = list(face.exterior.coords)[:3]
            result.extend(vector(p, y) for p in points)
    return result


def route_between(graph, restrictions, start, finish, incoming):
    initial = (start, incoming)
    queue = [(0, initial)]
    cost, previous = {initial: 0}, {}
    while queue:
        distance, state = heapq.heappop(queue)
        if distance != cost[state]:
            continue
        node, way = state
        if node == finish:
            path = [state]
            while path[-1] != initial:
                path.append(previous[path[-1]])
            return list(reversed(path))
        forbidden, only = restrictions.get((node, way), (set(), set()))
        for to, segment, metres in graph[node]:
            if segment in forbidden or only and segment not in only:
                continue
            key = (to, segment)
            if distance + metres < cost.get(key, math.inf):
                cost[key], previous[key] = distance + metres, state
                heapq.heappush(queue, (cost[key], key))
    raise ValueError(f"No public connection from OSM node {start} to {finish}")


def project_elements(elements):
    nodes = {i: xy(float(e.get("lat")), float(e.get("lon")))
             for (kind, i), e in elements.items() if kind == "node"}
    ways = {i: ([int(n.get("ref")) for n in e.findall("nd")], tags(e))
            for (kind, i), e in elements.items() if kind == "way"}
    return nodes, ways


def open_to_motor_traffic(attrs):
    access = attrs.get("motorcar", attrs.get("motor_vehicle", attrs.get("vehicle", attrs.get("access", ""))))
    return not BLOCKED_ACCESS.intersection(access.split(";"))


def build_road_network(nodes, ways):
    graph, roads = defaultdict(list), {}
    stone_ways = set()
    for ident, (ids, t) in ways.items():
        highway, name = t.get("highway"), t.get("name", "")
        if (highway not in DRIVABLE or "Pont d'Iéna" in name or t.get("tunnel") not in (None, "no")
                or not open_to_motor_traffic(t)
                or len(ids) < 2 or any(i not in nodes for i in ids)):
            continue
        coords = [nodes[i] for i in ids]
        line = LineString(coords)
        if line.length < .1 or not line.intersects(AREA):
            continue
        oneway = t.get("oneway") in ("yes", "true", "1", "-1") or t.get("junction") == "roundabout"
        if t.get("oneway") == "-1":
            ids = ids[::-1]
            coords.reverse()
        lanes = min(6, int(number(t.get("lanes"), 1 if oneway else 2)))
        width = min(24, max(4, number(t.get("width"), lanes * 3 + .8)))
        if t.get("surface") in ("sett","cobblestone","paving_stones"):
            stone_ways.add(ident)
        roads[ident] = dict(name=name or "Rue de Paris", lanes=lanes, width=width,
                            oneWay=oneway, bridge=t.get("bridge") not in (None, "no"),
                            points=[vector(p) for p in coords], nodes=ids, osmId=ident, roadClass=highway)
        for a, b in zip(ids, ids[1:]):
            length = math.dist(nodes[a], nodes[b])
            if length < .1:
                continue
            graph[a].append((b, ident, length))
            if not oneway:
                graph[b].append((a, ident, length))

    return roads, graph, stone_ways


def turn_restrictions(elements, roads):
    restrictions = {}
    for (kind, _), e in elements.items():
        if kind != "relation":
            continue
        t = tags(e)
        if t.get("type") != "restriction" or not t.get("restriction"):
            continue
        members = {(m.get("role"), m.get("type")): int(m.get("ref")) for m in e.findall("member")}
        via, src, dst = (members.get(k) for k in (("via", "node"), ("from", "way"), ("to", "way")))
        if via is None or src not in roads or dst not in roads:
            continue
        no, only = restrictions.setdefault((via, src), (set(), set()))
        (only if t["restriction"].startswith("only_") else no).add(dst)

    return restrictions


def build_guide(nodes, roads, graph, restrictions):
    stops = []
    for lat, lon, name in ANCHORS:
        expected = xy(lat, lon)
        candidates = {node for road in roads.values() if road["name"] == name
                      for node in road["nodes"] if node in graph}
        if not candidates:
            raise ValueError(f"Guide anchor is missing from public roads: {name}")
        node = min(candidates, key=lambda n: math.dist(nodes[n], expected))
        if math.dist(nodes[node], expected) > 220:
            raise ValueError(f"Guide anchor too far from {name}: {nodes[node]}")
        stops.append(node)
    path = [(stops[0], 0)]
    for stop in stops[1:]:
        leg = route_between(graph, restrictions, path[-1][0], stop, path[-1][1])
        path.extend(leg[1:])
    guide, guide_names, guide_widths = [], [], []
    for (a, _), (b, way) in zip(path, path[1:]):
        road = roads[way]
        count = max(1, math.ceil(math.dist(nodes[a], nodes[b]) / 3))
        for i in range(count):
            f = i / count
            p = tuple(x + (y-x)*f for x, y in zip(nodes[a], nodes[b]))
            guide.append(vector(p))
            guide_names.append(road["name"])
            guide_widths.append(road["width"])
    guide.append(vector(nodes[path[-1][0]]))
    guide_names.append(guide_names[-1])
    guide_widths.append(guide_widths[-1])

    first = nearest_sample(guide, xy(*STATIONS[0][:2]))
    guide, guide_names, guide_widths = [v[first:-1] + v[:first+1]
                                         for v in (guide, guide_names, guide_widths)]
    stations = [dict(index=nearest_sample(guide, xy(lat, lon)), name=name)
                for lat, lon, name in STATIONS]
    return guide, guide_names, guide_widths, stations, stops[0]


def nearest_sample(guide, at):
    return min(range(len(guide)-1),
               key=lambda i: (guide[i]["x"]-at[0])**2 + (guide[i]["z"]-at[1])**2)


def connected_road_ids(graph, start):
    reached, roads, pending = {start}, set(), [start]
    while pending:
        node = pending.pop()
        for dest, way, _ in graph[node]:
            roads.add(way)
            if dest not in reached:
                reached.add(dest)
                pending.append(dest)
    return roads


def build(output=ASSETS, route_only=False):
    elements, provenance = source_map()
    nodes, ways = project_elements(elements)
    roads, graph, stone_ways = build_road_network(nodes, ways)
    guide, guide_names, guide_widths, stations, start = build_guide(
        nodes, roads, graph, turn_restrictions(elements, roads))
    route_metres = sum(math.dist((a["x"], a["z"]), (b["x"], b["z"])) for a, b in zip(guide, guide[1:]))
    print(f"Guide: {route_metres/1000:.3f} km; {len(guide)} samples; stations={stations}", flush=True)
    print("Guide streets:", ", ".join(dict.fromkeys(guide_names)), flush=True)
    if route_only:
        return
    output.mkdir(parents=True, exist_ok=True)
    connected = connected_road_ids(graph, start)
    roads = {ident: road for ident, road in roads.items() if ident in connected}
    lines = {i: LineString([nodes[n] for n in r["nodes"]]) for i, r in roads.items()}
    road_buffers = [lines[i].buffer(r["width"]*.5, quad_segs=3) for i, r in roads.items()]
    surface = union_all(road_buffers)
    road_overlap = STRtree(road_buffers)
    stone_surface=union_all([lines[i].buffer(roads[i]["width"]*.5,quad_segs=3)
                             for i in stone_ways if i in lines])
    # At the boundary, all selected connected ways remain drivable to their last authored point.
    area = box(*union_all([AREA, surface]).bounds)
    kerb_area = union_all([lines[i].buffer(r["width"]*.5 + (2.6 if r["lanes"]>=2 else 1.6), quad_segs=3)
                           for i, r in roads.items()])
    river_relation = elements["relation", 2191006]
    river_edges = [LineString([nodes[n] for n in ways[int(m.get("ref"))][0]])
                   for m in river_relation.findall("member") if m.get("role") == "outer" and m.get("type") == "way"]
    islands = [LineString([nodes[n] for n in ways[int(m.get("ref"))][0]])
               for m in river_relation.findall("member") if m.get("role") == "inner" and m.get("type") == "way"]
    river = union_all(list(polygonize(river_edges))).difference(union_all(list(polygonize(islands))))
    river = river.intersection(area).simplify(.35, preserve_topology=True)
    if river.area < 50000:
        raise ValueError("Seine polygon did not join")
    land = area.difference(river)
    landmarks = []
    for key, way in LANDMARK_WAYS.items():
        ids, _ = ways[way]
        footprint = Polygon([nodes[n] for n in ids])
        landmarks.append(dict(key=key, centre=vector(footprint.centroid.coords[0]),
                              outline=[vector(p) for p in footprint.exterior.coords]))
    buildings = []
    building_region = AREA.union(surface.buffer(70)).intersection(area)
    for ident, (ids, t) in ways.items():
        if (not t.get("building") or ident in LANDMARK_WAYS.values() or ident == 5013364
                or len(ids) < 4 or ids[0] != ids[-1] or any(n not in nodes for n in ids)):
            continue
        polygon = Polygon([nodes[n] for n in ids])
        if not polygon.is_valid or polygon.area < 22 or not building_region.covers(polygon.centroid):
            continue
        overlapping = road_overlap.query(polygon, predicate="intersects")
        if len(overlapping) and union_all([road_buffers[i].intersection(polygon)
                                           for i in overlapping]).area > min(4, polygon.area*.12):
            continue
        levels = min(14, int(number(t.get("building:levels"), 4 + ident % 3)))
        buildings.append(dict(osmId=ident, name=t.get("name", ""), levels=levels,
                              height=min(56, number(t.get("height"), 3.15*levels)),
                              points=[vector(p) for p in polygon.simplify(.12, preserve_topology=True).exterior.coords]))
    populate_building_uses(buildings, elements)
    park_ids = (4208595, 469214756, 1024371295)
    parks = []
    for ident in park_ids:
        ids, _ = ways[ident]
        if ids[0] == ids[-1]:
            parks.append(make_valid(Polygon([nodes[n] for n in ids])))
    for ids, tag in ways.values():
        if tag.get("leisure") not in ("park", "garden") and tag.get("landuse") not in ("grass", "recreation_ground"):
            continue
        if len(ids)<4 or ids[0]!=ids[-1] or any(i not in nodes for i in ids):
            continue
        polygon=Polygon([nodes[n] for n in ids])
        if polygon.is_valid and polygon.area > 30 and polygon.intersects(AREA):
            parks.append(polygon)
    parks = union_all(parks).difference(kerb_area).intersection(land)
    bridge = make_valid(Polygon([nodes[n] for n in ways[184263889][0]]))
    decks=[]
    bridge_ways = {
        183602168: "Bir-Hakeim", 183589443: "Alma", 183618971: "Invalides",
        183620685: "Alexandre III", 176396895: "Concorde",
    }
    for ident, name in bridge_ways.items():
        deck=make_valid(Polygon([nodes[n] for n in ways[ident][0]]))
        outline=max(polygons(deck),key=lambda p:p.area).exterior.coords
        decks.append(dict(name=name, points=[vector(p) for p in outline], mesh=triangles(deck,-.065)))
    banks = [dict(points=[vector(p) for p in part.exterior.coords]) for part in polygons(river)]
    junctions = defaultdict(set)
    signals = []
    for road in roads.values():
        ids = road["nodes"]
        for a, b in zip(ids, ids[1:]):
            junctions[a].add(b)
            junctions[b].add(a)
    for n, neighbors in junctions.items():
        if len(neighbors) < 3 or not AREA.covers(Point(nodes[n])):
            continue
        if tags(elements["node", n]).get("highway") != "traffic_signals":
            continue
        signals.append(dict(point=vector(nodes[n]), arms=[vector(nodes[x]) for x in sorted(neighbors)]))
    tree_exclusion = surface.buffer(1)
    trees = [vector(nodes[n]) for (kind, n), e in elements.items() if kind == "node"
             and tags(e).get("natural") == "tree" and AREA.covers(Point(nodes[n]))
             and not tree_exclusion.covers(Point(nodes[n]))]
    tree_grid = {}
    for p in trees:
        tree_grid.setdefault((int(p["x"]//9), int(p["z"]//9)), p)
    for r in roads.values():
        del r["nodes"]
    data = dict(
        attribution="© OpenStreetMap contributors · ODbL 1.0", sources=list(provenance.values()),
        min=vector(area.bounds[:2]), max=vector(area.bounds[2:]), roads=list(roads.values()),
        guide=guide, guideNames=guide_names, guideWidths=guide_widths, stations=stations,
        roadSurface=triangles(surface), stoneRoads=triangles(stone_surface,.008), sidewalks=triangles(kerb_area.difference(surface), -.04),
        land=triangles(land, -.21), water=triangles(river, -3.55), banks=banks,
        parks=triangles(parks, -.18), pedestrianBridge=triangles(bridge, -.025),
        bridgeOutline=[vector(p) for p in max(polygons(bridge), key=lambda p:p.area).exterior.coords],
        buildings=buildings, signals=signals, trees=list(tree_grid.values()), bridgeDecks=decks, landmarks=landmarks,
    )
    (output/"ParisRoadData.json").write_text(json.dumps(data, ensure_ascii=False, separators=(",",":")), encoding="utf-8")
    minx, minz, maxx, maxz = area.bounds
    def pixel(p):
        return ((p[0]-minx)/(maxx-minx)*767, (maxz-p[1])/(maxz-minz)*767)
    im = Image.new("RGB", (768,768), "#e7dfcb")
    draw = ImageDraw.Draw(im)
    for p in polygons(river):
        draw.polygon([pixel(q) for q in p.exterior.coords], fill="#82b9bb")
        for ring in p.interiors:
            draw.polygon([pixel(q) for q in ring.coords], fill="#e7dfcb")
    for p in polygons(parks):
        draw.polygon([pixel(q) for q in p.exterior.coords], fill="#9ea77b")
    for line in lines.values():
        if isinstance(line, LineString):
            draw.line([pixel(p) for p in line.coords], fill="#b4a78f", width=2)
    draw.line([pixel((p["x"],p["z"])) for p in guide], fill="#ba613c", width=4)
    x,z = pixel((0,0))
    draw.ellipse((x-8,z-8,x+8,z+8), fill="#294145")
    im.save(output/"ParisMap.png")
    print(f"{len(roads)} connected road ways; {sum(l.length for l in lines.values())/1000:.2f} road km; "
          f"{len(buildings)} buildings; {len(tree_grid)} tree placements", flush=True)


def build_traffic(output):
    """Use the same OSM way versions and geometry as the saved scene."""
    scene = json.loads((output / "ParisRoadData.json").read_text(encoding="utf-8"))
    elements, _ = source_map()
    coordinates, ways = project_elements(elements)
    roads, _, _ = build_road_network(coordinates, ways)
    for road in scene["roads"]:
        original = roads.get(road["osmId"])
        if (original is None or original["points"] != road["points"]
                or original["oneWay"] != road["oneWay"]):
            raise ValueError(f"Saved road {road['osmId']} does not match the OSM extracts")
    roads = {road["osmId"]: roads[road["osmId"]] for road in scene["roads"]}
    restrictions = turn_restrictions(elements, roads)
    uses, neighbours = defaultdict(int), defaultdict(set)
    for road in roads.values():
        ids = road["nodes"]
        for node in ids:
            uses[node] += 1
        for a, b in zip(ids, ids[1:]):
            neighbours[a].add(b)
            neighbours[b].add(a)
    mapped = {(p["point"]["x"], p["point"]["z"]) for p in scene["signals"]}
    intersections = {i for i in uses if len(neighbours[i]) >= 3 and coordinates[i] in mapped}
    directional = {}
    for node in uses:
        attrs = tags(elements["node", node])
        direction = attrs.get("traffic_signals:direction")
        if (attrs.get("highway") == "traffic_signals" and direction in ("forward", "backward")
                and all(math.dist(coordinates[node], point) >= 24 for point in mapped)):
            directional[node] = direction
    signals = intersections | directional.keys()
    nodes, node_index, edges, departing = [], {}, [], defaultdict(list)

    def index(node, circle):
        if node not in node_index:
            node_index[node] = len(nodes)
            nodes.append(dict(osmId=node, point=vector(coordinates[node]), signal=-1, roundabout=circle))
        else:
            nodes[node_index[node]]["roundabout"] |= circle
        return node_index[node]

    for way, road in roads.items():
        ids = road["nodes"]
        attrs = ways[way][1]
        circle = road["oneWay"] and attrs.get("junction") in ("roundabout", "circular")
        cuts = [0] + [i for i in range(1, len(ids)-1) if uses[ids[i]] > 1 or ids[i] in signals] + [len(ids)-1]
        for first, last in zip(cuts, cuts[1:]):
            segment = ids[first:last+1]
            for reverse in ([False] if road["oneWay"] else [False, True]):
                direction = segment[::-1] if reverse else segment
                points, distances = [coordinates[direction[0]]], [0.0]
                for node in direction[1:]:
                    point = coordinates[node]
                    length = math.dist(points[-1], point)
                    if length > .02:
                        points.append(point)
                        distances.append(distances[-1] + length)
                if distances[-1] < .25:
                    continue
                start, finish = index(direction[0], circle), index(direction[-1], circle)
                travel = "backward" if reverse != (attrs.get("oneway") == "-1") else "forward"
                key = "maxspeed:" + travel
                has_signal = direction[-1] in intersections or directional.get(direction[-1]) == travel
                speed = min(50, max(10, number(attrs.get(key, attrs.get("maxspeed")), 30)))
                departing[start].append(len(edges))
                edges.append(dict(fromNode=start, toNode=finish, osmWayId=way, name=road["name"],
                                  lanes=road["lanes"], oneWay=road["oneWay"], roundabout=circle,
                                  mappedSignal=has_signal,
                                  points=[vector(p) for p in points], distances=distances,
                                  width=road["width"], speed=speed / 3.6))

    for edge in edges:
        node = edge["toNode"]
        no, only = restrictions.get((nodes[node]["osmId"], edge["osmWayId"]), (set(), set()))
        candidates = departing[node]
        edge["allowedNext"] = [i for i in candidates if edges[i]["fromNode"] != edges[i]["toNode"]
                               and edges[i]["toNode"] != edge["fromNode"]
                               and edges[i]["osmWayId"] not in no
                               and (not only or edges[i]["osmWayId"] in only)]
    (output / "ParisTrafficSource.json").write_text(json.dumps(dict(nodes=nodes, edges=edges),
        ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"{len(nodes)} junction nodes, {len(edges)} directed edges, {len(signals)} mapped signals")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ASSETS)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--route-only", action="store_true")
    mode.add_argument("--traffic-only", action="store_true")
    args = parser.parse_args()
    if not args.traffic_only:
        build(args.output, args.route_only)
    if not args.route_only:
        build_traffic(args.output)
