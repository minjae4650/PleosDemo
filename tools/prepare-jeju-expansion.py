"""Enrich the Jeju scene with connected coastal roads and bounded OSM context.

All geographic derivatives remain ODbL. Existing scene data is retained in build/
before replacement. No source environment assets or other scenes are modified.
"""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import importlib.util
import json
import math
import heapq
import sys
import hashlib
import xml.etree.ElementTree as ET
import numpy as np
from PIL import Image, ImageDraw

SPEC = importlib.util.spec_from_file_location('jeju_base', Path(__file__).with_name('prepare-jeju-drive.py'))
base = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(base)
ROOT, ASSETS, CACHE = base.ROOT, base.ASSETS, base.CACHE
OUT = ROOT / 'build/jeju-drive/expansion'



# Real road anchors in the Gangjeong–Beophwan–Seoho development district.
# The same snapped nodes connect the corridors to one another and to the coast.
CITY_CORRIDORS = (
    ('월드컵로·서호남로', [(33.2370,126.5020),(33.2457,126.5069),(33.24891,126.50761),(33.25035,126.51619),(33.24987,126.52333)]),
    ('서귀포시청 제2청사·서호동', [(33.24891,126.50761),(33.2522,126.508),(33.25485,126.51008),(33.25756,126.51182),(33.25281,126.51575),(33.25035,126.51619)]),
    ('강정·신중로', [(33.2527,126.4992),(33.25446,126.50169),(33.25610,126.50545),(33.25485,126.51008)]),
    ('강정·서호 주거지', [(33.2527,126.4992),(33.25141,126.50122),(33.25177,126.50305),(33.25105,126.50501),(33.2522,126.508)]),
)
STADIUM_WAY = 175919449
CITY_HALL_WAY = 281765558


def urban_area(lat, lon):
    return 33.242 < lat < 33.263 and 126.498 < lon < 126.526


def building_outline(item, nodes, road_tree):
    tags = item['tags']
    stadium = item['id'] == STADIUM_WAY
    kind = 'stadium' if stadium else tags.get('building')
    ids = item.get('nodes', [])
    if not kind or kind == 'no' or len(ids) < 4 or ids[0] != ids[-1] or any(n not in nodes for n in ids):
        return None
    coords = np.array([base.xy(nodes[n]['lat'],nodes[n]['lon']) for n in ids])
    centre = coords[:-1].mean(axis=0)
    urban = urban_area(centre[1]/base.METRES_LAT+base.ORIGIN[0], centre[0]/base.METRES_LON+base.ORIGIN[1])
    distance = road_tree.query(centre)[0]
    if distance > (315 if urban else 150) and not stadium:
        return None

    def positive_number(tag):
        try:
            value = float(tags.get(tag, '0').strip().removesuffix('m').strip())
            return value if math.isfinite(value) and value > 0 else 0
        except ValueError:
            return 0

    return dict(osmId=item['id'], points=[dict(x=float(x),y=0,z=float(z)) for x,z in coords],
                height=positive_number('height'), levels=int(positive_number('building:levels')),
                name=tags.get('name', ''), kind=kind, urban=bool(urban),
                landmark='stadium' if stadium else 'city_hall' if item['id']==CITY_HALL_WAY else '')

def context():
    boxes = [(126.37+c*.035,33.23+r*.022,126.405+c*.035,33.252+r*.022)
             for r in range(2) for c in range(3)]
    boxes += [(126.37+c*.035,33.23+r*.022,126.405+c*.035,33.252+r*.022)
              for r in range(2) for c in range(3,5)]
    def fetch(item):
        i, box = item
        path = CACHE / f'context-{i}.osm'
        raw = base.download('https://api.openstreetmap.org/api/0.6/map?bbox='+','.join(f'{v:.6f}' for v in box),path)
        print(f'Context {i}: {len(raw):,} bytes',flush=True)
        return ET.fromstring(raw)
    elements = {}
    with ThreadPoolExecutor(max_workers=2) as pool:
        for tree in pool.map(fetch,enumerate(boxes)):
            for item in tree:
                if item.tag not in ('node','way','relation'):
                    continue
                obj = dict(type=item.tag,id=int(item.attrib['id']),tags={t.attrib['k']:t.attrib['v'] for t in item.findall('tag')})
                if item.tag=='node':obj.update(lat=float(item.attrib['lat']),lon=float(item.attrib['lon']))
                elif item.tag=='way':obj['nodes']=[int(n.attrib['ref']) for n in item.findall('nd')]
                else:obj['members']=[dict(type=m.attrib['type'],ref=int(m.attrib['ref']),role=m.attrib['role']) for m in item.findall('member')]
                elements[obj['type'],obj['id']]=obj
    return list(elements.values())


def main():
    def without_backtracks(ids):
        result=[]
        for node in ids:
            if len(result)>1 and node==result[-2]:result.pop()
            else:result.append(node)
        return result
    OUT.mkdir(parents=True,exist_ok=True)
    source=base.fetch_osm()
    merged={(e['type'],e['id']):e for e in source['elements']}
    for e in context():merged[e['type'],e['id']]=e
    nodes={e['id']:e for e in merged.values() if e['type']=='node'}
    roads=[e for e in merged.values() if e['type']=='way' and e.get('tags',{}).get('highway') in
           ('primary','secondary','tertiary','unclassified','residential','primary_link','secondary_link','tertiary_link')
           and e.get('tags',{}).get('access') not in ('private','no') and e.get('tags',{}).get('motor_vehicle')!='no'
           and all(n in nodes for n in e.get('nodes',[]))]
    graph={}; edges={}
    neighbours={}
    for way in roads:
        for a,b in zip(way['nodes'],way['nodes'][1:]):
            neighbours.setdefault(a,set()).add(b);neighbours.setdefault(b,set()).add(a)
            cost=math.dist(base.xy(nodes[a]['lat'],nodes[a]['lon']),base.xy(nodes[b]['lat'],nodes[b]['lon']))
            tags=way.get('tags',{})
            for src,dst,enabled in ((a,b,tags.get('oneway')!='-1'),(b,a,tags.get('oneway') not in ('yes','1') and tags.get('junction')!='roundabout')):
                if enabled:graph.setdefault(src,[]).append((dst,cost));edges[src,dst]=way
    def nearest(lat,lon):
        p=base.xy(lat,lon)
        return min(graph,key=lambda n:math.dist(base.xy(nodes[n]['lat'],nodes[n]['lon']),p))
    def path(a,b):
        q=[(0,a)]; dist={a:0}; prev={}
        while q:
            cost,n=heapq.heappop(q)
            if cost!=dist[n]:continue
            if n==b:
                result=[b]
                while result[-1]!=a:result.append(prev[result[-1]])
                return result[::-1]
            for dest,d in graph.get(n,[]):
                if cost+d<dist.get(dest,float('inf')):
                    dist[dest]=cost+d;prev[dest]=n;heapq.heappush(q,(cost+d,dest))
        raise RuntimeError(f'No drivable route between OSM nodes {a} and {b}')
    old=json.loads((ASSETS/'Data/JejuRoadData.json').read_text(encoding='utf-8'))
    backup=OUT/'base-road-data.json'
    baseline=ASSETS/'Data/OriginalCoastalRoute.json'
    if baseline.exists():old=json.loads(baseline.read_text(encoding='utf-8'))
    elif backup.exists():old=json.loads(backup.read_text(encoding='utf-8'))
    else:backup.write_text(json.dumps(old,ensure_ascii=False),encoding='utf-8')
    # A continuous western coastal approach joins the existing Jungmun station.
    targets=[(33.2404,126.3739),(33.2348,126.3902),(33.2458,126.4000),(33.2444,126.4136)]
    join=next(i for i,name in enumerate(old['roadNames']) if '1100' in name)
    join_point=old['points'][join]
    targets[-1]=(join_point['z']/base.METRES_LAT+base.ORIGIN[0],join_point['x']/base.METRES_LON+base.ORIGIN[1])
    stops=[nearest(*p) for p in targets]
    route=[stops[0]]
    for a,b in zip(stops,stops[1:]):route.extend(path(a,b)[1:])
    # Sightseeing targets can snap to a short cul-de-sac. Keep those in the free
    # road network, not as an instantaneous 180-degree turn in guided driving.
    route=without_backtracks(route)
    dem=base.Elevation()
    def sample_route(ids):
        plan=np.array([base.xy(nodes[n]['lat'],nodes[n]['lon']) for n in ids])
        ds=np.r_[0,np.cumsum(np.linalg.norm(np.diff(plan,axis=0),axis=1))]
        regular=np.unique(np.r_[np.arange(0,ds[-1],5.),ds[-1]])
        x,z=np.interp(regular,ds,plan[:,0]),np.interp(regular,ds,plan[:,1])
        # Preserve the OSM plan shape at small junctions and roundabouts. The
        # former 8.5m sigma could cut across a short connecting street entirely.
        plan_kernel=np.exp(-np.arange(-2,3)**2/(2*.45**2));plan_kernel/=plan_kernel.sum()
        x=np.convolve(np.pad(x,(2,2),mode='edge'),plan_kernel,mode='valid')
        z=np.convolve(np.pad(z,(2,2),mode='edge'),plan_kernel,mode='valid')
        k=np.exp(-np.arange(-5,6)**2/(2*1.7**2));k/=k.sum()
        y=dem.sample(x,z)+.18
        y=np.convolve(np.pad(y,(5,5),mode='edge'),k,mode='valid')
        points=np.stack((x,y,z),axis=1)
        widths=[];names=[];oneways=[]
        for d in regular:
            i=min(len(ids)-2,max(0,int(np.searchsorted(ds,d,side='right')-1)))
            way=edges[ids[i],ids[i+1]]; tags=way.get('tags',{})
            try:lanes=int(tags.get('lanes','2'))
            except ValueError:lanes=2
            widths.append(float(max(7,min(14,lanes*3.2+1.2))))
            names.append(tags.get('name','해안 연결도로'))
            oneways.append(tags.get('oneway') in ('yes','1','-1','true') or tags.get('junction')=='roundabout')
        return points,widths,names,oneways
    extension,widths,names,extension_oneways=sample_route(route)
    old_points=np.array([[p['x'],p['y'],p['z']] for p in old['points']])
    used_ways=set(old.get('osmWayIds',[]))
    used_ways.update(edges[a,b]['id'] for a,b in zip(route,route[1:]))
    # Preserve the existing eastern coast as a fully drivable branch rather than discarding it.
    coast_stops=[nearest(p[2]/base.METRES_LAT+base.ORIGIN[0],p[0]/base.METRES_LON+base.ORIGIN[1]) for p in old_points[:join+1:180]]+[stops[-1]]
    coast_ids=[coast_stops[0]]
    for a,b in zip(coast_stops,coast_stops[1:]):
        if a!=b:coast_ids.extend(path(a,b)[1:])
    coast_ids=without_backtracks(coast_ids)
    old_coast,coast_widths,_,coast_oneways=sample_route(coast_ids)
    used_ways.update(edges[a,b]['id'] for a,b in zip(coast_ids,coast_ids[1:]))
    # Re-route the mountain section on the directed source graph as well: an
    # older undirected sightseeing path could take the short, wrong-way arc of
    # a roundabout. Traffic and the guide must share the legal direction.
    last=old_points[-1]
    mountain_ids=path(stops[-1],nearest(last[2]/base.METRES_LAT+base.ORIGIN[0],last[0]/base.METRES_LON+base.ORIGIN[1]))
    mountain,mountain_widths,mountain_names,mountain_oneways=sample_route(mountain_ids)
    used_ways.update(edges[a,b]['id'] for a,b in zip(mountain_ids,mountain_ids[1:]))
    extension[-1]=mountain[0]
    points=np.concatenate((extension[:-1],mountain))
    all_widths=widths[:-1]+mountain_widths
    all_names=names[:-1]+mountain_names
    all_oneways=extension_oneways[:-1]+mountain_oneways
    stations=[]
    for name,subtitle,lat,lon in [('예래 해안','논짓물 · 현무암 돌담과 바닷길',33.2348,126.3902),
                                  ('중문 마을','신호 교차로 · 감귤밭과 마을길',33.2548,126.423),
                                  ('한라산 숲길','1100도로 · 수목과 하층 식생',33.303,126.453),
                                  ('1100고지','한라산 서쪽 고원',33.3545,126.461)]:
        x,z=base.xy(lat,lon);i=int(np.argmin((points[:,0]-x)**2+(points[:,2]-z)**2))
        stations.append(dict(name=name,subtitle=subtitle,index=i,lookAt=dict(x=6400,y=1900,z=7900)))
    # Additional loops connect existing junctions to nearby real public streets.
    branch_specs=[dict(name='대포·중문 해안',points=old_coast,widths=coast_widths,oneWays=coast_oneways)]
    corridors = (
        ('중문 시가지 순환', [(33.2444,126.4136),(33.2517,126.4138),(33.2587,126.4322),(33.2595,126.4455)]),
        ('예래 마을 연결', [(33.2458,126.4000),(33.2602,126.4024),(33.2587,126.4322)]),
        ('대포·월평·강정 해안 순환', [(33.2444,126.446),(33.2384,126.4668),(33.2329,126.4827),(33.2370,126.5020),(33.2527,126.4992),(33.2587,126.4322)]),
    )
    for name, targets in corridors + CITY_CORRIDORS:
        ids=[nearest(*p) for p in targets]
        sequence=[ids[0]]
        for a,b in zip(ids,ids[1:]):
            sequence.extend(path(a,b)[1:])
        sequence=without_backtracks(sequence)
        used_ways.update(edges[a,b]['id'] for a,b in zip(sequence,sequence[1:]))
        p,w,_,ow=sample_route(sequence)
        branch_specs.append(dict(name=name,points=p,widths=w,oneWays=ow))
    all_samples=np.concatenate([points]+[b['points'] for b in branch_specs])
    # Keep the return carriageways and complete nearby roundabouts. A sightseeing
    # itinerary alone often renders only one of two legal directions around a
    # divided junction; ambient cars need the real return connection as well.
    try:
        from scipy.spatial import cKDTree
    except ModuleNotFoundError:
        sys.path.insert(0,str(ROOT/'build/jeju-drive/pythondeps'))
        from scipy.spatial import cKDTree
    rendered_tree=cKDTree(all_samples[:,[0,2]])
    for way in roads:
        tags=way.get('tags',{});ids=way['nodes']
        raw=np.array([base.xy(nodes[n]['lat'],nodes[n]['lon']) for n in ids])
        dist,_=rendered_tree.query(raw)
        length=float(np.sum(np.linalg.norm(np.diff(raw,axis=0),axis=1)))
        directed=tags.get('oneway') in ('yes','1','-1') or tags.get('junction')=='roundabout'
        if ids[0]==ids[-1] and length<30:continue
        if max(dist)<3 or max(dist)>90 or max(dist[0],dist[-1])>12 or length<5:continue
        if not directed and (length>350 or max(dist)>45):continue
        if tags.get('oneway')=='-1':ids=ids[::-1]
        if any((a,b) not in edges for a,b in zip(ids,ids[1:])):continue
        p,w,_,ow=sample_route(ids)
        branch_specs.append(dict(name=('회전교차로 연결' if tags.get('junction')=='roundabout' else '실제 교차로·반대편 연결')+' · '+tags.get('name','마을길'),points=p,widths=w,oneWays=ow))
        used_ways.add(way['id'])
    all_samples=np.concatenate([points]+[b['points'] for b in branch_specs])
    def reconcile(p):
        # Coincident intersection endpoints are levelled against the main route locally.
        for i in range(len(p)):
            near=np.argmin((points[:,0]-p[i,0])**2+(points[:,2]-p[i,2])**2)
            d=math.hypot(points[near,0]-p[i,0],points[near,2]-p[i,2])
            if d<25:p[i,1]=min(p[i,1],points[near,1]+max(0,d-5)*.03)
        return p
    for b in branch_specs:b['points']=reconcile(b['points'])
    # Carry actual signal/node metadata and visible building outlines from bounded map extracts.
    signals=[]; buildings=[]; coast=[]
    building_tree=cKDTree(all_samples[:,[0,2]])
    for item in merged.values():
        tags=item.get('tags',{})
        if item['type']=='node' and tags.get('highway')=='traffic_signals':
            x,z=base.xy(item['lat'],item['lon'])
            i=int(np.argmin((points[:,0]-x)**2+(points[:,2]-z)**2))
            d=math.hypot(points[i,0]-x,points[i,2]-z)
            if d<25 and (not signals or min(abs(s['distance']-sum_distance(points)[i]) for s in signals)>180):
                signals.append(dict(name=tags.get('name','신호 교차로'),index=i,distance=float(sum_distance(points)[i]),osmNodeId=item['id']))
        if item['type']=='way':
            building=building_outline(item,nodes,building_tree)
            if building:
                buildings.append(building)
        if item['type']=='way' and tags.get('natural')=='coastline' and all(n in nodes for n in item.get('nodes',[])):
            coast.append(dict(points=[dict(x=base.xy(nodes[n]['lat'],nodes[n]['lon'])[0],y=0,z=base.xy(nodes[n]['lat'],nodes[n]['lon'])[1]) for n in item['nodes']]))
    # OSM signal tagging is incomplete here. Use actual public-road junction nodes
    # for explicitly labelled simulated controls; do not imply surveyed lamp locations.
    cumulative=sum_distance(points)
    candidates=[]
    roundabout_nodes={n for w in roads if w.get('tags',{}).get('junction')=='roundabout' for n in w['nodes']}
    for node,adjacent in neighbours.items():
        if len(adjacent)<3 or node in roundabout_nodes:continue
        x,z=base.xy(nodes[node]['lat'],nodes[node]['lon'])
        i=int(np.argmin((points[:,0]-x)**2+(points[:,2]-z)**2))
        d=math.hypot(points[i,0]-x,points[i,2]-z)
        if d<12 and 5<points[i,1]<220 and cumulative[i]>400:
            candidates.append((d,i,node))
    signal_nodes=[]
    for d,i,node in sorted(candidates):
        if len(signals)>=7:break
        if signals and min(abs(s['distance']-cumulative[i]) for s in signals)<420:continue
        signals.append(dict(name=nodes[node].get('tags',{}).get('name','마을 교차로'),index=i,distance=float(cumulative[i]),osmNodeId=node,
                            placement='simulated_control_at_real_osm_junction'))
        signal_nodes.append(node)
    for node, item in nodes.items():
        if not urban_area(item['lat'],item['lon']) or item['tags'].get('highway')!='traffic_signals':
            continue
        if len(neighbours.get(node,())) < 3:
            continue
        x,z=base.xy(item['lat'],item['lon'])
        if building_tree.query((x,z))[0] > 5:
            continue
        i=int(np.argmin((points[:,0]-x)**2+(points[:,2]-z)**2))
        signals.append(dict(name=item['tags'].get('name','서귀포 신시가지 교차로'),index=i,distance=float(cumulative[i]),
                            osmNodeId=node,placement='osm_traffic_signals'))
        signal_nodes.append(node)
    # Show and drive the real short side-street approaches at each controlled junction.
    for node in signal_nodes:
        for neighbour in neighbours[node]:
            ids=[node,neighbour]
            length=0
            while length<130:
                a,b=ids[-2:]
                length+=math.dist(base.xy(nodes[a]['lat'],nodes[a]['lon']),base.xy(nodes[b]['lat'],nodes[b]['lon']))
                choices=neighbours.get(b,set())-{a}
                if len(choices)!=1:break
                nxt=next(iter(choices))
                if nxt in ids:break
                ids.append(nxt)
            if len(ids)<2:continue
            # sample_route uses metadata for both directions on these local visual approaches.
            for a,b in zip(ids,ids[1:]):
                if (a,b) not in edges:edges[a,b]=edges[b,a]
            p,w,_,ow=sample_route(ids)
            mid=p[len(p)//2]
            if ((all_samples[:,0]-mid[0])**2+(all_samples[:,2]-mid[2])**2).min()<9**2:continue
            branch_specs.append(dict(name='교차로 진입도로',points=reconcile(p),widths=w,oneWays=ow))
            used_ways.update(edges[a,b]['id'] for a,b in zip(ids,ids[1:]))
    # One continuous, spatial height field serves every road. Repeated roads must
    # not receive different heights from route-direction-specific smoothing.
    from scipy.ndimage import gaussian_filter
    old['terrainMin']['x']=-10500.
    old['terrainSize']['x']=20500.
    n=old['terrainResolution'];minimum=old['terrainMin'];size=old['terrainSize']
    tx,tz=np.meshgrid(np.linspace(minimum['x'],minimum['x']+size['x'],n),np.linspace(minimum['y'],minimum['y']+size['y'],n))
    raw_terrain=dem.sample(tx,tz)
    repair_spec=importlib.util.spec_from_file_location('coastal_dem',Path(__file__).with_name('repair-jeju-coastal-dem.py'))
    repair=importlib.util.module_from_spec(repair_spec);repair_spec.loader.exec_module(repair)
    raw_terrain,_=repair.offshore_repairs(raw_terrain,minimum,size,np.concatenate([points]+[b['points'] for b in branch_specs]),coast)
    field=gaussian_filter(raw_terrain,.9)
    def field_at(x,z):
        fx=np.clip((np.asarray(x)-minimum['x'])/size['x']*(n-1),0,n-1.001)
        fz=np.clip((np.asarray(z)-minimum['y'])/size['y']*(n-1),0,n-1.001)
        ix,iz=fx.astype(int),fz.astype(int);ax,az=fx-ix,fz-iz
        return (field[iz,ix]*(1-ax)+field[iz,ix+1]*ax)*(1-az)+(field[iz+1,ix]*(1-ax)+field[iz+1,ix+1]*ax)*az+.18
    junction_coordinates=np.array([base.xy(nodes[node]['lat'],nodes[node]['lon']) for node,adjacent in neighbours.items() if len(adjacent)>=3])
    junction_tree=cKDTree(junction_coordinates)
    junction_y=field_at(junction_coordinates[:,0],junction_coordinates[:,1])
    for p in [points]+[b['points'] for b in branch_specs]:
        y=field_at(p[:,0],p[:,2]);distance,idx=junction_tree.query(p[:,[0,2]])
        blend=np.clip((distance-10)/20,0,1);blend=blend*blend*(3-2*blend)
        p[:,1]=junction_y[idx]*(1-blend)+y*blend
    # Project the joint road graph onto a comfortable grade bound, including
    # coincident samples shared by different roads. DEM buildings and cliff-edge
    # artefacts must not pitch the driving camera or create steps at joins.
    collections=[points]+[b['points'] for b in branch_specs]
    flat=np.concatenate(collections)
    pairs=[]; limits=[];offset=0
    for p in collections:
        for i in range(len(p)-1):
            pairs.append((offset+i,offset+i+1));limits.append(max(.02,math.hypot(p[i+1,0]-p[i,0],p[i+1,2]-p[i,2])*.16))
        offset+=len(p)
    shared=cKDTree(flat[:,[0,2]]).query_pairs(12,output_type='ndarray')
    for a,b in shared:
        if abs(int(a)-int(b))>3:
            horizontal=math.hypot(flat[a,0]-flat[b,0],flat[a,2]-flat[b,2])
            pairs.append((int(a),int(b)));limits.append(.06+horizontal*.008)
    pairs=np.asarray(pairs);limits=np.asarray(limits);a,b=pairs[:,0],pairs[:,1]
    degree=np.bincount(np.r_[a,b],minlength=len(flat));height=flat[:,1].copy()
    for iteration in range(8000):
        delta=height[b]-height[a]
        fix=np.sign(delta)*np.maximum(np.abs(delta)-limits,0)*.95
        adjustment=np.zeros_like(height)
        np.add.at(adjustment,a,fix);np.add.at(adjustment,b,-fix)
        height+=adjustment/np.maximum(1,degree)
    offset=0
    for p in collections:p[:,1]=height[offset:offset+len(p)];offset+=len(p)
    flat=np.concatenate([points]+[b['points'] for b in branch_specs])
    tree=cKDTree(flat[:,[0,2]])
    def clearance(x,z,y):
        d,i=tree.query(np.stack((x,z),axis=-1))
        return np.where(d<13,np.minimum(y,flat[i,1]-.65),y)
    offsets=list(range(-90,91,5))
    def corridor(p):
        tangent=np.gradient(p,axis=0);right=np.stack((tangent[:,2],-tangent[:,0]),axis=1)
        right/=np.maximum(.01,np.linalg.norm(right,axis=1)[:,None])
        result=[]
        for offset in offsets:
            x,z=p[:,0]+right[:,0]*offset,p[:,2]+right[:,1]*offset
            y=dem.sample(x,z); blend=np.clip((abs(offset)-8)/27,0,1)
            y=p[:,1]*(1-blend)+y*blend-.18
            y=clearance(x,z,y)
            result.append(dict(values=y.tolist()))
        return result
    terrain=raw_terrain.copy()
    terrain.astype('<f4').tofile(ASSETS/'Data/TerrainOriginal.bytes')
    dist,idx=tree.query(np.stack((tx,tz),axis=-1))
    terrain=np.where(dist<60,np.minimum(terrain,flat[idx,1]-3-(1-dist/60)*9),terrain)
    terrain[terrain<1]=-3
    terrain.astype('<f4').tofile(ASSETS/'Data/TerrainHeights.bytes')
    def vector(p):return dict(x=round(float(p[0]),3),y=round(float(p[1]),3),z=round(float(p[2]),3))
    distances=sum_distance(points)
    for s in signals:s['distance']=float(distances[s['index']])
    if buildings:
        centres=np.array([[sum(p['x'] for p in b['points'])/len(b['points']),sum(p['z'] for p in b['points'])/len(b['points'])] for b in buildings])
        views=[]
        for i in range(stations[0]['index']+100,len(points)-2,10):
            if points[i,1]>190:continue
            forward=points[i+1,[0,2]]-points[i,[0,2]];forward/=max(.001,np.linalg.norm(forward))
            delta=centres-points[i,[0,2]];near=np.linalg.norm(delta,axis=1)<160
            score=np.sum(near & (delta@forward>0))+.25*np.sum(near)
            views.append((score,i))
        if views:stations[1]['index']=max(views)[1]
    old.pop('osmWayIds',None)  # Original per-sample IDs belong to OriginalCoastalRoute.json.
    old.update(title='Jeju expanded coastal and mountain drive',sourceDate='2026-11-06',originLat=base.ORIGIN[0],originLon=base.ORIGIN[1],scale=1,
               points=[vector(p) for p in points],distances=np.round(distances,3).tolist(),widths=all_widths,roadNames=all_names,oneWays=all_oneways,
               stations=stations,corridorOffsets=offsets,corridorHeights=corridor(points),
               branches=[dict(name=b['name'],points=[vector(p) for p in b['points']],widths=b['widths'],oneWays=b['oneWays'],corridorHeights=corridor(b['points'])) for b in branch_specs],
               signals=sorted(signals,key=lambda s:s['distance']),buildings=buildings,coastlines=coast,sourceWayIds=sorted(used_ways))
    (ASSETS/'Data/JejuRoadData.json').write_text(json.dumps(old,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    im=base.terrain_map(dem,(minimum['x'],minimum['y']),(size['x'],size['y']),points,stations)
    draw=ImageDraw.Draw(im)
    def pixel(p):return ((p[0]-minimum['x'])/size['x']*1024,(1-(p[2]-minimum['y'])/size['y'])*1024)
    city_names = {name for name, _ in CITY_CORRIDORS}
    for b in branch_specs:
        if b['name'] not in city_names:
            draw.line([pixel(p) for p in b['points']],fill=(112,179,198),width=3)
    for b in branch_specs:
        if b['name'] in city_names:
            draw.line([pixel(p) for p in b['points']],fill=(242,187,100),width=4)
    im.save(ASSETS/'Data/RouteMap.png')
    report=dict(mainKm=round(float(distances[-1]/1000),3),maxMainGrade=float(np.max(np.abs(np.diff(points[:,1]))/np.maximum(.01,np.linalg.norm(np.diff(points[:,[0,2]],axis=0),axis=1)))),
                branches=[dict(name=b['name'],km=round(float(sum_distance(b['points'])[-1]/1000),3)) for b in branch_specs],
                signals=len(signals),buildings=len(buildings),urbanBuildings=sum(b['urban'] for b in buildings),coastlines=len(coast),stations=stations)
    (OUT/'data-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(report,ensure_ascii=False,indent=2),flush=True)
    export_context_sources()
    spec=importlib.util.spec_from_file_location('jeju_traffic',Path(__file__).with_name('prepare-jeju-traffic.py'))
    traffic=importlib.util.module_from_spec(spec);spec.loader.exec_module(traffic)
    traffic.build(merged,old)


def export_context_sources():
    """Retain source footprints, road metadata and node coordinates for ODbL review."""
    elements={}
    for path in sorted(CACHE.glob('context-*.osm')):
        for item in ET.parse(path).getroot():
            if item.tag not in ('node','way'):continue
            obj=dict(type=item.tag,id=int(item.attrib['id']),tags={t.attrib['k']:t.attrib['v'] for t in item.findall('tag')})
            if item.tag=='node':obj.update(lat=float(item.attrib['lat']),lon=float(item.attrib['lon']))
            else:obj['nodes']=[int(n.attrib['ref']) for n in item.findall('nd')]
            elements[obj['type'],obj['id']]=obj
    nodes={e['id']:e for e in elements.values() if e['type']=='node'}
    features=[]
    for e in elements.values():
        tags=e.get('tags',{})
        if e['type']!='way' or not (tags.get('building') or e['id']==STADIUM_WAY or tags.get('highway') or tags.get('natural')=='coastline'):continue
        if not all(n in nodes for n in e['nodes']):continue
        coords=[[nodes[n]['lon'],nodes[n]['lat']] for n in e['nodes']]
        polygon=bool(tags.get('building') or e['id']==STADIUM_WAY) and coords[0]==coords[-1]
        features.append(dict(type='Feature',properties=dict(osmId=e['id'],**tags),geometry=dict(type='Polygon' if polygon else 'LineString',coordinates=[coords] if polygon else coords)))
    content=dict(type='FeatureCollection',attribution='© OpenStreetMap contributors; Open Database License 1.0',features=features)
    (ASSETS/'Data/ContextGeography.geojson').write_text(json.dumps(content,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    manifest=dict(source='OpenStreetMap API 0.6 bounded map extracts',license='ODbL 1.0',accessDate='2026-11-06',
                  files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(CACHE.glob('context-*.osm'))})
    (ASSETS/'Data/ContextSources.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')


def sum_distance(p):return np.r_[0,np.cumsum(np.linalg.norm(np.diff(p,axis=0),axis=1))]


if __name__=='__main__':main()
