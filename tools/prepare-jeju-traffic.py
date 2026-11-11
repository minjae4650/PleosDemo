"""Directed OSM road graph, limited to roads actually authored in the Jeju scene.

Coordinates and metadata are ODbL derivatives. Routes never join by proximity:
connectivity comes only from a shared OSM node. Unsupported via-way restrictions
are reported and conservatively exclude their from-way from NPC spawning/routing.
"""
import importlib.util
import json
import math
from pathlib import Path
import sys
import numpy as np

SPEC=importlib.util.spec_from_file_location('base',Path(__file__).with_name('prepare-jeju-drive.py'))
base=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(base)
sys.path.insert(0,str(base.ROOT/'build/jeju-drive/pythondeps'))
from scipy.spatial import cKDTree


def build(merged,data):
    nodes={e['id']:e for e in merged.values() if e['type']=='node'}
    polylines=[np.array([[p['x'],p['y'],p['z']] for p in data['points']])]
    widths=[np.array(data['widths'])]
    for branch in data['branches']:
        polylines.append(np.array([[p['x'],p['y'],p['z']] for p in branch['points']]))
        widths.append(np.array(branch['widths']))
    flat=np.concatenate(polylines);tree=cKDTree(flat[:,[0,2]])
    offsets=np.cumsum([0]+[len(p) for p in polylines])
    def project(raw):
        _,indices=tree.query(raw,k=4)
        best=None
        for index in indices:
            ri=min(len(polylines)-1,int(np.searchsorted(offsets,index,side='right')-1))
            local=int(index-offsets[ri]);road=polylines[ri]
            for i in range(max(0,local-1),min(len(road)-1,local+1)):
                a,b=road[i],road[i+1];v=b[[0,2]]-a[[0,2]]
                t=np.clip(np.dot(raw-a[[0,2]],v)/max(.001,np.dot(v,v)),0,1)
                p=a+(b-a)*t;d=np.linalg.norm(p[[0,2]]-raw)
                if best is None or d<best[0]:best=(d,p,float(widths[ri][i]))
        return best
    def vector(p):return dict(x=round(float(p[0]),3),y=round(float(p[1]),3),z=round(float(p[2]),3))
    restricted=[];blocked_ways=set()
    for rel in merged.values():
        tags=rel.get('tags',{})
        if rel['type']!='relation' or tags.get('type')!='restriction':continue
        if any(v in tags.get('except','').split(';') for v in ('motorcar','motor_vehicle')):continue
        roles={m['role']:m for m in rel.get('members',[])}
        if not all(k in roles for k in ('from','via','to')):continue
        if roles['via']['type']=='node':
            restricted.append((roles['from']['ref'],roles['via']['ref'],roles['to']['ref'],tags.get('restriction:motorcar',tags.get('restriction',''))))
        else:blocked_ways.add(roles['from']['ref'])
    segments=[];adj={};way_for={};positions={};metadata={}
    allowed=('primary','secondary','tertiary','unclassified','residential','primary_link','secondary_link','tertiary_link')
    excluded=[]
    for way in merged.values():
        tags=way.get('tags',{})
        if way['type']!='way' or tags.get('highway') not in allowed or way['id'] in blocked_ways:continue
        if way['id'] not in data.get('sourceWayIds',[]):continue
        access=next((tags[k] for k in ('motorcar','motor_vehicle','vehicle','access') if k in tags),'yes')
        if access in ('no','private','destination'):continue
        for a,b in zip(way.get('nodes',[]),way.get('nodes',[])[1:]):
            if a not in nodes or b not in nodes:continue
            pa,pb=[np.array(base.xy(nodes[n]['lat'],nodes[n]['lon'])) for n in (a,b)]
            length=np.linalg.norm(pb-pa)
            if length<.1:continue
            probes=np.linspace(pa,pb,max(3,int(length/4)+1));d,_=tree.query(probes)
            if np.max(d)>5:continue
            # Exact centreline projection supplies the same smoothed road surface
            # as the rendered scene, while the topology remains the OSM topology.
            for n,p in ((a,pa),(b,pb)):
                if n not in positions:
                    result=project(p);positions[n]=result[1].copy()
                    if result[0]<2:positions[n][[0,2]]=p
            segments.append((a,b,way['id']))
            adj.setdefault(a,set()).add(b);adj.setdefault(b,set()).add(a)
            way_for[a,b]=way_for[b,a]=way['id'];metadata[way['id']]=tags
    # Split at topology, metadata changes and restriction nodes, never every 5m.
    restriction_nodes={r[1] for r in restricted}
    split={n for n,near in adj.items() if len(near)!=2 or n in restriction_nodes or len({way_for[n,b] for b in near})>1}
    seen=set();chains=[]
    for a,b,_ in segments:
        if tuple(sorted((a,b))) in seen:continue
        # Walk back to an actual boundary for chains whose first segment is internal.
        start=a;previous=b;visited={b}
        while start not in split:
            options=adj[start]-{previous}
            if not options:break
            nxt=next(iter(options))
            if nxt in visited:break
            visited.add(start);previous,start=start,nxt
        chain=[start];previous=None
        while True:
            options=[v for v in adj[chain[-1]] if v!=previous and tuple(sorted((chain[-1],v))) not in seen]
            if not options:break
            nxt=options[0];seen.add(tuple(sorted((chain[-1],nxt))))
            previous=chain[-1];chain.append(nxt)
            if nxt in split or nxt==start:break
        if len(chain)>1:chains.append(chain)
    endpoints=sorted({n for c in chains for n in (c[0],c[-1])})
    node_index={n:i for i,n in enumerate(endpoints)}
    network_nodes=[dict(osmId=n,point=vector(positions[n]),roundabout=any(metadata[way_for[n,b]].get('junction')=='roundabout' for b in adj[n]),signal=-1) for n in endpoints]
    edges=[]
    for chain in chains:
        wid=way_for[chain[0],chain[1]];tags=metadata[wid]
        osm_order=next(w['nodes'] for w in merged.values() if w['type']=='way' and w['id']==wid)
        order={(a,b) for a,b in zip(osm_order,osm_order[1:])}
        normal=(chain[0],chain[1]) in order
        oneway=tags.get('oneway','yes' if tags.get('junction')=='roundabout' else 'no')
        for direction in (1,-1):
            is_forward=normal==(direction==1)
            if oneway in ('yes','1','true') and not is_forward or oneway=='-1' and is_forward:continue
            ids=chain if direction==1 else chain[::-1]
            # Densify the authored centreline between actual OSM nodes. Intermediate
            # raw points are projected, so bends follow road geometry rather than chords.
            raw=np.array([base.xy(nodes[n]['lat'],nodes[n]['lon']) for n in ids])
            ds=np.r_[0,np.cumsum(np.linalg.norm(np.diff(raw,axis=0),axis=1))]
            samples=np.unique(np.r_[np.arange(0,ds[-1],3.),ds,ds[-1]])
            probes=np.column_stack((np.interp(samples,ds,raw[:,0]),np.interp(samples,ds,raw[:,1])))
            projected=[project(p) for p in probes];p=np.array([v[1] for v in projected]);w=float(np.median([v[2] for v in projected]))
            p[0]=positions[ids[0]];p[-1]=positions[ids[-1]]
            keep=np.r_[True,np.linalg.norm(np.diff(p,axis=0),axis=1)>.02];p=p[keep]
            if len(p)<2:continue
            distances=np.r_[0,np.cumsum(np.linalg.norm(np.diff(p,axis=0),axis=1))]
            if distances[-1]<2:continue
            dense=np.arange(0,distances[-1],1.)
            centre_probes=np.column_stack((np.interp(dense,distances,p[:,0]),np.interp(dense,distances,p[:,2])))
            coverage,_=tree.query(centre_probes)
            if np.max(coverage)>4:
                excluded.append(dict(osmWayId=wid,fromNode=ids[0],toNode=ids[-1],reason='unrendered_chord',maxDistance=float(np.max(coverage))))
                continue
            try:speed=float(tags.get('maxspeed','50').split()[0])/3.6
            except ValueError:speed=50/3.6
            lane_key='lanes:forward' if is_forward else 'lanes:backward'
            try:lanes=int(tags.get(lane_key,str(max(1,int(tags.get('lanes','2'))//(1 if oneway in ('yes','1','-1') else 2)))))
            except ValueError:lanes=1
            turn=tags.get('turn:lanes:forward' if is_forward else 'turn:lanes:backward',tags.get('turn:lanes',''))
            edges.append(dict(fromNode=node_index[ids[0]],toNode=node_index[ids[-1]],osmWayId=wid,name=tags.get('name',tags.get('ref','제주 마을길')),
                              osmNodes=ids,
                              points=[vector(v) for v in p],distances=np.round(distances,3).tolist(),width=w,lanes=max(1,lanes),
                              oneWay=oneway in ('yes','1','true','-1'),speed=min(16.67,max(5.56,speed)),turnLanes=turn,allowedNext=[]))
            edges[-1]['roundabout']=tags.get('junction')=='roundabout'
    for i,e in enumerate(edges):
        for j,n in enumerate(edges):
            if e['toNode']!=n['fromNode']:continue
            if e['osmNodes']==n['osmNodes'][::-1]:continue
            valid=True
            only=set()
            for source,via,dest,rule in restricted:
                if source!=e['osmWayId'] or endpoints[e['toNode']]!=via:continue
                if rule=='no_u_turn':
                    ep=e['points'];np_=n['points']
                    incoming=np.array([ep[-1]['x']-ep[-2]['x'],ep[-1]['z']-ep[-2]['z']])
                    outgoing=np.array([np_[1]['x']-np_[0]['x'],np_[1]['z']-np_[0]['z']])
                    reverse=np.dot(incoming,outgoing)<-.85*np.linalg.norm(incoming)*np.linalg.norm(outgoing)
                    if dest==n['osmWayId'] and reverse:valid=False
                elif rule.startswith('no_') and dest==n['osmWayId']:valid=False
                if rule.startswith('only_'):only.add(dest)
            if only and n['osmWayId'] not in only:valid=False
            if valid:e['allowedNext'].append(j)
    signals=data['signals']
    for si,s in enumerate(signals):
        ni=node_index.get(s['osmNodeId'])
        if ni is None:continue
        network_nodes[ni]['signal']=si
        s['centre']=network_nodes[ni]['point']
        arms=[]
        for e in edges:
            incoming=e['toNode']==ni;outgoing=e['fromNode']==ni
            if not (incoming or outgoing):continue
            p=np.array([[v['x'],v['y'],v['z']] for v in e['points']]);ds=np.array(e['distances'])
            target=max(0,ds[-1]-25) if incoming else min(25,ds[-1])
            near=np.array([np.interp(target,ds,p[:,axis]) for axis in range(3)])
            direction=near-positions[s['osmNodeId']];direction[1]=0;direction/=max(.01,np.linalg.norm(direction))
            if any(np.dot(direction,np.array([a['outward']['x'],0,a['outward']['z']]))>.9 for a in arms):
                if incoming:
                    match=max(arms,key=lambda a:np.dot(direction,np.array([a['outward']['x'],0,a['outward']['z']])))
                    match['incoming']=True
                continue
            arms.append(dict(outward=vector(direction),width=e['width'],incoming=incoming,lanes=e['lanes'],oneWay=e['oneWay']))
        s['arms']=arms
    output=dict(attribution='© OpenStreetMap contributors; ODbL 1.0',nodes=network_nodes,edges=edges,
                restrictions=[dict(fromWay=a,viaNode=b,toWay=c,rule=d) for a,b,c,d in restricted],excludedViaWayFromWays=sorted(blocked_ways),excludedUnrenderedEdges=excluded)
    (base.ASSETS/'Data/JejuTrafficNetwork.json').write_text(json.dumps(output,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    (base.ASSETS/'Data/JejuRoadData.json').write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    length=sum(e['distances'][-1] for e in edges if e['oneWay'])+sum(e['distances'][-1] for e in edges if not e['oneWay'])/2
    report=dict(nodes=len(network_nodes),directedEdges=len(edges),uniqueRoadKm=round(length/1000,3),oneWayEdges=sum(e['oneWay'] for e in edges),
                restrictions=len(restricted),excludedViaWayFromWays=len(blocked_ways),junctions=sum(len(v)>2 for v in adj.values()),
                controlledArms=[dict(osmId=s['osmNodeId'],arms=len(s.get('arms',[]))) for s in signals])
    target=base.ROOT/'build/jeju-drive/traffic-network-report.json';target.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2),flush=True)


if __name__=='__main__':
    spec=importlib.util.spec_from_file_location('expansion',Path(__file__).with_name('prepare-jeju-expansion.py'))
    expansion=importlib.util.module_from_spec(spec);spec.loader.exec_module(expansion)
    merged={(e['type'],e['id']):e for e in base.fetch_osm()['elements']}
    for e in expansion.context():merged[e['type'],e['id']]=e
    build(merged,json.loads((base.ASSETS/'Data/JejuRoadData.json').read_text(encoding='utf-8')))
