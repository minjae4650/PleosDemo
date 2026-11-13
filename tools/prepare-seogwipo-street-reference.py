"""Prepare road-view study geometry from OSM and Overture open building data.

Reads the existing Jeju road data, cached OSM extracts and a bounded Overture
GeoJSON download. Shapely is required. Panorama imagery is visual reference.
"""
import hashlib
import json
import math
import xml.etree.ElementTree as ET
from pathlib import Path

from shapely import constrained_delaunay_triangles, get_parts, make_valid
from shapely.geometry import Polygon, LineString, shape
from shapely.ops import transform

ROOT = Path(__file__).resolve().parents[1]
CACHE = ROOT / "build/jeju-drive"
DATA = ROOT / "unity/InputProbe/Assets/_Parkside/JejuDrive/Data"
LAT_METRES = 111195.08
LON_METRES = LAT_METRES * math.cos(math.radians(33.29))
REFERENCE_ROADS = (
    ("대청로", {1152587937, 391471593, 1152587936}, 13.0, 4, 33.251, 33.254),
    ("신중로", {278166752, 278166755, 1153088907}, 7.6, 2, 33.253, 33.257),
    ("월드컵로", {29484856, 1361438485, 1102344669}, 12.8, 4, 33.24435, 33.24963),
)
VIEWS = (
    ("대청로 상가·공동주택", 33.25253,126.50341),
    ("대청로 동쪽 교차로", 33.25248,126.50468),
    ("신중로 제2청사", 33.25506,126.50998),
    ("월드컵로 경기장 입구", 33.2466,126.50705),
    ("경기장 상업시설 주차장", 33.24858,126.5080),
)


def project(lon, lat, z=None):
    return ((lon-126.46)*LON_METRES, (lat-33.29)*LAT_METRES)


def point(x, z, y=0):
    return dict(x=round(x,3),y=round(y,3),z=round(z,3))


def xz(p):
    return p["x"], p["z"]


def new_buildings(reference, city, road_line):
    existing=[Polygon([xz(p) for p in b["points"]]) for b in city if b["urban"]]
    buildings=[]
    for feature in reference["features"]:
        props=feature["properties"]
        polygon=transform(project,shape(feature["geometry"]))
        if polygon.geom_type!="Polygon" or not polygon.is_valid or polygon.area<22 or polygon.distance(road_line)>155:
            continue
        if any(polygon.intersection(b).area > polygon.area*.27 for b in existing if b.is_valid and polygon.intersects(b)):
            continue
        boundary=polygon.simplify(.18,preserve_topology=True)
        c=polygon.centroid
        lon, lat=c.x/LON_METRES+126.46,c.y/LAT_METRES+33.29
        width=polygon.bounds[2]-polygon.bounds[0]
        tower=width>=24 and 126.5004<lon<126.5043 and 33.25155<lat<33.25299
        front=polygon.distance(road_line)<42
        style="roadview_apartment" if tower else "roadview_shop" if front else "roadview_villa"
        key=int.from_bytes(hashlib.blake2b(str(feature["id"]).encode(),digest_size=6).digest(),"big")
        levels=int(props.get("num_floors") or (12+key%4 if tower else 2+key%3 if front else 2+key%2))
        buildings.append(dict(osmId=key,gersId=feature["id"],points=[point(x,z) for x,z in boundary.exterior.coords],
                              height=props.get("height") or 0,levels=levels,kind=style,urban=True))
    return buildings


def parking():
    nodes, selected = {}, {}
    for file in sorted((CACHE/"source").glob("context-*.osm")):
        for e in ET.parse(file).getroot():
            if e.tag=="node":
                nodes[e.get("id")]=project(float(e.get("lon")),float(e.get("lat")))
            elif e.tag=="way" and e.get("id") in ("287438788","373863728"):
                selected[e.get("id")]=[n.get("ref") for n in e.findall("nd")]
    result=[]
    for osm_id, refs in selected.items():
        poly=make_valid(Polygon([nodes[i] for i in refs]))
        if poly.geom_type!="Polygon":
            continue
        mesh=[point(x,z) for tri in get_parts(constrained_delaunay_triangles(poly)) for x,z in list(tri.exterior.coords)[:3]]
        result.append(dict(osmId=int(osm_id),points=[point(x,z) for x,z in poly.exterior.coords],triangles=mesh))
    return result


def main():
    road_data=json.loads((DATA/"JejuRoadData.json").read_text(encoding="utf-8"))
    traffic=json.loads((DATA/"JejuTrafficNetwork.json").read_text(encoding="utf-8"))
    source=CACHE/"daechung-overture.geojson"
    state=json.loads(source.with_suffix(".geojson.state").read_text(encoding="utf-8"))
    footprints=json.loads(source.read_text(encoding="utf-8"))
    streets=[]
    for name, osm_ids, width, lanes, low, high in REFERENCE_ROADS:
        seen=set()
        for edge in traffic["edges"]:
            if edge["osmWayId"] not in osm_ids:
                continue
            pair=tuple(sorted((edge["fromNode"],edge["toNode"])))+(edge["osmWayId"],)
            if pair in seen and not edge["oneWay"]:
                continue
            seen.add(pair)
            pts=[p for p in edge["points"] if low<p["z"]/LAT_METRES+33.29<high]
            if len(pts)<2:
                continue
            streets.append(dict(name=name,osmId=edge["osmWayId"],width=width,lanes=lanes,
                                oneWay=edge["oneWay"],points=pts))
    district_line=LineString([project(126.49984,33.25267),project(126.50476,33.25248)])
    buildings=new_buildings(footprints,road_data["buildings"],district_line)
    data=dict(attribution="© OpenStreetMap contributors, Overture Maps Foundation · ODbL",
              release=state["last_release"],sourceHash=hashlib.sha256(source.read_bytes()).hexdigest(),
              views=[dict(name=n,url=f"https://map.kakao.com/link/roadview/{lat},{lon}",capture="2026-03")
                     for n,lat,lon in VIEWS],
              streets=streets,buildings=buildings,parking=parking())
    (DATA/"SeogwipoStreetReference.json").write_text(json.dumps(data,ensure_ascii=False,separators=(",",":")),encoding="utf-8")
    print(f"{len(streets)} road strips; {len(buildings)} new footprints; {len(data['parking'])} parking plots")


if __name__=="__main__":
    main()
