"""Fetch real OSM roads / Terrarium DEM and prepare the isolated Jeju drive scene.

Requires Python 3, numpy and Pillow. Downloads are cached outside Unity Assets.
Only this tool's JejuDrive files are written; existing environment assets are read-only.
"""
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.parse import urlencode
import argparse
import json
import math
import time
import xml.etree.ElementTree as ET
from concurrent.futures import ThreadPoolExecutor
import heapq
import hashlib
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
CACHE = ROOT / 'build/jeju-drive/source'
ASSETS = ROOT / 'unity/InputProbe/Assets/_Parkside/JejuDrive'
BBOX = (33.22, 126.37, 33.39, 126.56)
ORIGIN = (33.29, 126.46)
METRES_LAT = 111195.0802
METRES_LON = METRES_LAT * math.cos(math.radians(ORIGIN[0]))


def download(url, target, data=None):
    if target.exists():
        return target.read_bytes()
    target.parent.mkdir(parents=True, exist_ok=True)
    request = Request(url, data=data, headers={'User-Agent': 'Parkside-JejuDrive-Prototype/1.0'})
    with urlopen(request, timeout=90) as response:
        result = response.read()
    target.write_bytes(result)
    return result


def fetch_osm():
    cache = CACHE / 'jeju-osm.json'
    if cache.exists():
        return json.loads(cache.read_text(encoding='utf-8'))
    # Small read-only map extracts also work when public Overpass instances are busy.
    boxes = [(126.39 + col * .045, 33.23 + row * .035,
              126.435 + col * .045, 33.265 + row * .035)
             for row in range(4) for col in range(2)]
    def fetch_box(item):
        i, box = item
        path = CACHE / f'osm-tile-{i}.xml'
        raw = download('https://api.openstreetmap.org/api/0.6/map?bbox=' + ','.join(f'{v:.6f}' for v in box), path)
        print(f'OSM tile {i}: {len(raw):,} bytes', flush=True)
        return ET.fromstring(raw)
    elements = {}
    with ThreadPoolExecutor(max_workers=2) as pool:
        for tree in pool.map(fetch_box, enumerate(boxes)):
            for element in tree:
                if element.tag not in ('node', 'way'):
                    continue
                e = {'type': element.tag, 'id': int(element.attrib['id'])}
                e['tags'] = {t.attrib['k']: t.attrib['v'] for t in element.findall('tag')}
                if element.tag == 'node':
                    e.update(lat=float(element.attrib['lat']), lon=float(element.attrib['lon']))
                else:
                    e['nodes'] = [int(n.attrib['ref']) for n in element.findall('nd')]
                elements[(e['type'], e['id'])] = e
    data = {'source': 'OpenStreetMap API 0.6 bounded extracts', 'elements': list(elements.values())}
    cache.parent.mkdir(parents=True, exist_ok=True)
    cache.write_text(json.dumps(data, ensure_ascii=False), encoding='utf-8')
    return data


def fetch_overpass():
    cache = CACHE / 'jeju-osm.json'
    box = ','.join(map(str, BBOX))
    query = f'''[out:json][timeout:150];(
      way[highway~"^(primary|secondary|tertiary|unclassified|residential|service|primary_link|secondary_link|tertiary_link)$"]({box});
      way[natural=coastline]({box});
      node[natural=peak]({box});
    );out body;>;out skel qt;'''
    for endpoint in ('https://overpass.private.coffee/api/interpreter', 'https://overpass-api.de/api/interpreter', 'https://overpass.kumi.systems/api/interpreter'):
        try:
            raw = download(endpoint + '?' + urlencode({'data': query}), cache)
            data = json.loads(raw)
            if not data.get('elements') or data.get('remark'):
                cache.unlink(missing_ok=True)
                raise RuntimeError(data.get('remark', 'Empty OSM response'))
            return data
        except Exception as error:
            print(f'OSM endpoint: {error}', flush=True)
    raise RuntimeError('Unable to download the OSM extract')


def xy(lat, lon):
    return ((lon - ORIGIN[1]) * METRES_LON, (lat - ORIGIN[0]) * METRES_LAT)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--fetch-only', action='store_true')
    args = parser.parse_args()
    data = fetch_osm()
    nodes = {e['id']: e for e in data['elements'] if e['type'] == 'node'}
    ways = [e for e in data['elements'] if e['type'] == 'way' and 'highway' in e.get('tags', {})]
    print(f'Fetched {len(nodes)} nodes, {len(ways)} roads.', flush=True)
    summary = {}
    for way in ways:
        name = way.get('tags', {}).get('name', '(unnamed)')
        summary[name] = summary.get(name, 0) + 1
    print(json.dumps(sorted(summary.items(), key=lambda item: -item[1])[:45], ensure_ascii=False), flush=True)
    if args.fetch_only:
        return
    prepare(data, nodes, ways)


def prepare(data, nodes, ways):
    ASSETS.mkdir(parents=True, exist_ok=True)
    (ASSETS / 'Data').mkdir(exist_ok=True)
    graph, edge_ways = {}, {}
    allowed = {'primary', 'secondary', 'tertiary', 'unclassified', 'residential', 'service', 'primary_link', 'secondary_link', 'tertiary_link'}
    usable = []
    for way in ways:
        tags = way.get('tags', {})
        if tags.get('highway') not in allowed or tags.get('access') in ('private', 'no') or tags.get('motor_vehicle') == 'no':
            continue
        if any(n not in nodes for n in way['nodes']):
            continue
        usable.append(way)
        for a, b in zip(way['nodes'], way['nodes'][1:]):
            pa, pb = xy(nodes[a]['lat'], nodes[a]['lon']), xy(nodes[b]['lat'], nodes[b]['lon'])
            distance = math.dist(pa, pb)
            penalty = 1.8 if tags.get('highway') == 'service' else 1
            forward, reverse = tags.get('oneway') != '-1', tags.get('oneway') not in ('yes', '1') and tags.get('junction') != 'roundabout'
            for src, dst, enabled in ((a, b, forward), (b, a, reverse)):
                if enabled:
                    graph.setdefault(src, []).append((dst, distance * penalty))
                    edge_ways[src, dst] = way
    # Start on the public coastal road near Daepo, follow Jungmun's coast,
    # then join 1100-ro to the actual 1100 Highland rest area.
    targets = [(33.2397, 126.4439), (33.2444, 126.4136), (33.2595, 126.4455), (33.3576, 126.4626)]
    def nearest(lat, lon, name=None):
        candidates = set(graph)
        if name:
            candidates = {n for w in usable if name in w.get('tags', {}).get('name', '') for n in w['nodes'] if n in graph}
        px, pz = xy(lat, lon)
        return min(candidates, key=lambda n: math.dist(xy(nodes[n]['lat'], nodes[n]['lon']), (px, pz)))
    stops = [nearest(*t, '1100' if i == 3 else None) for i, t in enumerate(targets)]
    def path(start, end):
        queue = [(0., start)]; distances = {start: 0.}; previous = {}
        while queue:
            d, n = heapq.heappop(queue)
            if d != distances[n]:
                continue
            if n == end:
                result = [end]
                while result[-1] != start:
                    result.append(previous[result[-1]])
                return result[::-1]
            for dest, cost in graph.get(n, []):
                nd = d + cost
                if nd < distances.get(dest, float('inf')):
                    distances[dest] = nd; previous[dest] = n; heapq.heappush(queue, (nd, dest))
        raise RuntimeError(f'No connected public driving route between {start} and {end}')
    node_route = [stops[0]]
    for a, b in zip(stops, stops[1:]):
        node_route.extend(path(a, b)[1:])
    if len(set(node_route)) < len(node_route) * .90:
        print('Route revisits a road; inspect for intended coastal return.', flush=True)
    plan = [(xy(nodes[n]['lat'], nodes[n]['lon'])) for n in node_route]
    lengths = np.r_[0, np.cumsum(np.linalg.norm(np.diff(plan, axis=0), axis=1))]
    regular = np.arange(0, lengths[-1], 5.)
    px = np.interp(regular, lengths, np.array(plan)[:, 0])
    pz = np.interp(regular, lengths, np.array(plan)[:, 1])
    # Round the sharp polyline corners at intersections into driveable turn radii.
    # This changes a few metres locally while retaining the real road alignment.
    fair = np.exp(-np.arange(-7,8)**2/(2*2.4**2)); fair /= fair.sum()
    px = np.convolve(np.pad(px,(7,7),mode='edge'),fair,mode='valid')
    pz = np.convolve(np.pad(pz,(7,7),mode='edge'),fair,mode='valid')
    sampler = Elevation()
    heights = sampler.sample(px, pz)
    kernel = np.exp(-np.arange(-15, 16)**2 / (2 * 5**2)); kernel /= kernel.sum()
    heights = np.convolve(np.pad(heights, (15, 15), mode='edge'), kernel, mode='valid') + .18
    # Reconcile DEM smoothing where the route meets itself at a real junction.
    # Project onto the non-adjacent segment so a grade does not bias the comparison.
    buckets = {}
    for i, (x, z) in enumerate(zip(px, pz)):
        buckets.setdefault((int(x//12), int(z//12)), []).append(i)
    alignments = []
    for i, (x, z) in enumerate(zip(px, pz)):
        bx,bz = int(x//12),int(z//12)
        choices = []
        for ox in (-1,0,1):
            for oz in (-1,0,1):
                for j in buckets.get((bx+ox,bz+oz),[]):
                    if abs(i-j)<40 or j>=len(px)-1:
                        continue
                    dx,dz = px[j+1]-px[j],pz[j+1]-pz[j]
                    t = float(np.clip(((x-px[j])*dx+(z-pz[j])*dz)/max(.001,dx*dx+dz*dz),0,1))
                    distance = math.hypot(x-px[j]-dx*t,z-pz[j]-dz*t)
                    if distance<9 and abs(heights[i]-(heights[j]*(1-t)+heights[j+1]*t))<3:
                        choices.append((distance,j,t))
        if choices:
            distance,j,t = min(choices)
            alignments.append((i,j,t,distance))
    for _ in range(8):
        correction = np.zeros_like(heights)
        for i,j,t,distance in alignments:
            correction[i] = ((heights[j]*(1-t)+heights[j+1]*t)-heights[i])*.5*(1-distance/12)
        smooth = np.array([.04,.12,.20,.28,.20,.12,.04])
        heights += np.convolve(np.pad(correction,(3,3),mode='edge'),smooth,mode='valid')
    widths, road_names, osm_ids = [], [], []
    for d in regular:
        i = min(len(node_route)-2, max(0, int(np.searchsorted(lengths, d, side='right')-1)))
        way = edge_ways[node_route[i], node_route[i+1]]
        tags = way.get('tags', {})
        try:
            lanes = int(tags.get('lanes', '2'))
        except ValueError:
            lanes = 2
        width = min(14, max(6.8, lanes * 3.2 + 1.2))
        widths.append(width)
        road_names.append(tags.get('name', '연결도로'))
        osm_ids.append(way['id'])
    points = np.stack((px, heights, pz), axis=1)
    distances = np.r_[0, np.cumsum(np.linalg.norm(np.diff(points, axis=0), axis=1))]
    station_targets = [(33.2397, 126.4439), (33.2444, 126.4136), (33.303, 126.453), (33.3545, 126.461)]
    station_names = [('대포 해안', '중문 해안도로 · 바다에서 출발'), ('중문 관광단지', '해안에서 마을로'), ('한라산 숲길', '1100도로 · 고도를 높이는 숲길'), ('1100고지', '한라산 서쪽 고원 · 종점 부근')]
    stations = []
    for (lat, lon), (name, subtitle) in zip(station_targets, station_names):
        x, z = xy(lat, lon)
        index = int(np.argmin((px-x)**2 + (pz-z)**2))
        stations.append(dict(name=name, subtitle=subtitle, index=index, lookAt=dict(x=6400, y=1900, z=7900)))
    # Leave a real length of road behind the initial chase camera.
    stations[0]['index'] = 100
    # DEM envelope includes the summit as background, but only the route corridor gets objects.
    terrain_min = (-7000., -8000.)
    terrain_size = (17000., 19000.)
    resolution = 1025
    tx, tz = np.meshgrid(np.linspace(terrain_min[0], terrain_min[0]+terrain_size[0], resolution),
                         np.linspace(terrain_min[1], terrain_min[1]+terrain_size[1], resolution))
    terrain = sampler.sample(tx, tz)
    # Lower coarse DEM directly underneath roads. The dense shoulder mesh covers this cut.
    cellx, cellz = terrain_size[0]/(resolution-1), terrain_size[1]/(resolution-1)
    for x, y, z in points:
        ix, iz = round((x-terrain_min[0])/cellx), round((z-terrain_min[1])/cellz)
        for oz in range(-4, 5):
            for ox in range(-4, 5):
                gx, gz = ix+ox, iz+oz
                if 0 <= gx < resolution and 0 <= gz < resolution:
                    d = math.hypot(tx[gz,gx]-x, tz[gz,gx]-z)
                    if d < 55:
                        terrain[gz,gx] = min(terrain[gz,gx], y - 2 - (1-d/55) * 8)
    # Sea cells become a shallow negative seabed so water is visible at zero metres.
    terrain[terrain < 1] = -3
    terrain.astype('<f4').tofile(ASSETS / 'Data/TerrainHeights.bytes')
    preview = terrain_map(sampler, terrain_min, terrain_size, points, stations)
    preview.save(ASSETS / 'Data/RouteMap.png')
    # Dense ground under/along both shoulders: separate from the coarse distant DEM.
    tangents = np.gradient(points, axis=0)
    right = np.stack((tangents[:,2], -tangents[:,0]), axis=1)
    right /= np.linalg.norm(right, axis=1)[:,None]
    offsets = list(range(-65,66,5))
    ground_buckets = {}
    for i,(x,z) in enumerate(zip(px,pz)):
        ground_buckets.setdefault((int(x//32),int(z//32)),[]).append(i)
    def clear_other_roads(x,z,y):
        bx,bz=int(x//32),int(z//32)
        best=14*14;chosen=None
        for ox in (-1,0,1):
            for oz in (-1,0,1):
                for j in ground_buckets.get((bx+ox,bz+oz),[]):
                    if j>=len(px)-1:
                        continue
                    dx,dz=px[j+1]-px[j],pz[j+1]-pz[j]
                    t=max(0,min(1,((x-px[j])*dx+(z-pz[j])*dz)/max(.001,dx*dx+dz*dz)))
                    distance=(x-px[j]-dx*t)**2+(z-pz[j]-dz*t)**2
                    if distance<best:
                        best=distance;chosen=(j,t)
        if chosen is not None:
            j,t=chosen
            if best<(widths[j]*.5+6)**2:
                y=min(y,heights[j]*(1-t)+heights[j+1]*t-.6)
        return y
    corridor = []
    for offset in offsets:
        cx, cz = px + right[:,0]*offset, pz + right[:,1]*offset
        cy = sampler.sample(cx, cz)
        blend = np.clip((abs(offset)-7)/24,0,1)
        cy = heights * (1-blend) + cy * blend - .12
        cy = np.array([clear_other_roads(float(x),float(z),float(y)) for x,z,y in zip(cx,cz,cy)])
        corridor.append(cy.tolist())
    road_data = dict(title='Jeju / Jungmun coast to 1100 Highland', sourceDate='2026-11-06',
                     originLat=ORIGIN[0], originLon=ORIGIN[1], scale=1,
                     points=[dict(x=round(x,3), y=round(y,3), z=round(z,3)) for x,y,z in points],
                     distances=np.round(distances,3).tolist(), widths=widths, roadNames=road_names,
                     osmWayIds=osm_ids, stations=stations,
                     terrainMin=dict(x=terrain_min[0], y=terrain_min[1]),
                     terrainSize=dict(x=terrain_size[0], y=terrain_size[1]), terrainResolution=resolution,
                     corridorOffsets=offsets, corridorHeights=[dict(values=row) for row in corridor])
    (ASSETS / 'Data/JejuRoadData.json').write_text(json.dumps(road_data,ensure_ascii=False,separators=(',',':')), encoding='utf-8')
    derived = dict(type='FeatureCollection', attribution='© OpenStreetMap contributors; ODbL 1.0',
                   features=[dict(type='Feature', properties=dict(osmWayId=w['id'], **w.get('tags',{})),
                                  geometry=dict(type='LineString', coordinates=[[nodes[n]['lon'],nodes[n]['lat']] for n in w['nodes']]))
                             for w in usable if w['id'] in set(osm_ids)])
    (ASSETS / 'Data/SourceRoads.geojson').write_text(json.dumps(derived,ensure_ascii=False), encoding='utf-8')
    report = dict(routeLengthKm=round(distances[-1]/1000,3), samples=len(points),
                  minElevation=round(float(heights.min()),2), maxElevation=round(float(heights.max()),2),
                  maxRoadGrade=float(np.max(np.abs(np.diff(heights))/5)),
                  junctionSamplesReconciled=len(alignments),
                  summitDEM=float(sampler.sample(np.array([xy(33.3617,126.5292)[0]]),np.array([xy(33.3617,126.5292)[1]]))[0]),
                  roadNames=sorted(set(road_names)), stations=stations,
                  osmBaseTimestamp=data.get('osm3s',{}).get('timestamp_osm_base'),
                  sourceHashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest()
                                for p in sorted(CACHE.iterdir()) if p.name=='jeju-osm.json' or p.name.startswith(('osm-tile-','dem-'))})
    (ROOT/'build/jeju-drive/data-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2), encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k != 'sourceHashes'}, ensure_ascii=False,indent=2), flush=True)


class Elevation:
    zoom = 12
    def __init__(self):
        self.tiles = {}

    def sample(self, x, z):
        lat, lon = np.asarray(z)/METRES_LAT+ORIGIN[0], np.asarray(x)/METRES_LON+ORIGIN[1]
        n = 2**self.zoom
        u = (lon+180)/360*n
        v = (1-np.arcsinh(np.tan(np.deg2rad(lat)))/np.pi)/2*n
        # Pixel-centre convention; gather the four neighbours across tile edges.
        fx, fy = u*256-.5, v*256-.5
        ix, iy = np.floor(fx).astype(int), np.floor(fy).astype(int)
        def gather(gx, gy):
            result = np.empty(gx.shape, dtype=float)
            tiles = np.stack((gx//256,gy//256),axis=-1).reshape(-1,2)
            for tx,ty in np.unique(tiles,axis=0):
                key = (int(tx),int(ty))
                if key not in self.tiles:
                    name = CACHE/f'dem-{self.zoom}-{tx}-{ty}.png'
                    download(f'https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{self.zoom}/{tx}/{ty}.png',name)
                    rgb = np.asarray(Image.open(name)).astype(float)
                    self.tiles[key] = rgb[:,:,0]*256+rgb[:,:,1]+rgb[:,:,2]/256-32768
                    print(f'DEM {key}', flush=True)
                mask = (gx//256 == tx)&(gy//256 == ty)
                result[mask] = self.tiles[key][gy[mask]%256,gx[mask]%256]
            return result
        ax,ay = fx-ix,fy-iy
        return (gather(ix,iy)*(1-ax)+gather(ix+1,iy)*ax)*(1-ay)+(gather(ix,iy+1)*(1-ax)+gather(ix+1,iy+1)*ax)*ay


def terrain_map(sampler, minimum, size, points, stations):
    n=1024
    mx,mz=np.meshgrid(np.linspace(minimum[0],minimum[0]+size[0],n),np.linspace(minimum[1]+size[1],minimum[1],n))
    h=sampler.sample(mx,mz)
    dx,dz=np.gradient(h)
    shade=np.clip(.85+dx*.012-dz*.012,.55,1.15)
    rgb=np.stack((47+h*.028,75+h*.031,63+h*.025),axis=-1)*shade[:,:,None]
    rgb[h<1]=[22,58,71]
    im=Image.fromarray(np.clip(rgb,0,255).astype('uint8'))
    draw=ImageDraw.Draw(im)
    def pixel(p):
        return ((p[0]-minimum[0])/size[0]*n,(1-(p[2]-minimum[1])/size[1])*n)
    line=[pixel(p) for p in points]
    draw.line(line,fill=(11,31,35),width=12)
    draw.line(line,fill=(174,227,189),width=5)
    for i,station in enumerate(stations):
        x,y=line[station['index']]
        draw.ellipse((x-11,y-11,x+11,y+11),fill=(240,208,127))
    font=ImageFont.truetype(str(ROOT/'unity/InputProbe/Assets/_Parkside/UI/Fonts/AstaSans-Medium.ttf'),24)
    x,z=xy(33.3617,126.5292); sx,sy=pixel((x,0,z))
    draw.polygon([(sx,sy-10),(sx-10,sy+8),(sx+10,sy+8)],fill=(221,228,210))
    draw.text((sx+16,sy-13),'한라산',fill=(233,238,224),font=font)
    draw.text((n-46,15),'N',fill=(223,233,224),font=font)
    return im


if __name__ == '__main__':
    main()
