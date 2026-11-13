"""Check source direction, turn restrictions and driveable surface alignment."""
import importlib.util
import json
from pathlib import Path
import sys
import numpy as np

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'build/jeju-drive/pythondeps'))
from scipy.spatial import cKDTree
from scipy.sparse import csr_matrix
from scipy.sparse.csgraph import connected_components
spec=importlib.util.spec_from_file_location('expansion',Path(__file__).with_name('prepare-jeju-expansion.py'))
expansion=importlib.util.module_from_spec(spec);spec.loader.exec_module(expansion)
merged={(e['type'],e['id']):e for e in expansion.base.fetch_osm()['elements']}
for e in expansion.context():merged[e['type'],e['id']]=e
folder=expansion.ASSETS/'Data'
network=json.loads((folder/'JejuTrafficNetwork.json').read_text(encoding='utf-8'))
data=json.loads((folder/'JejuRoadData.json').read_text(encoding='utf-8'))
edges=network['edges'];nodes=network['nodes'];checks=0;turns=0;applicable=set();samples=0
roads=[data]+data['branches'];points=np.array([[p['x'],p['z']] for r in roads for p in r['points']]);tree=cKDTree(points)
for i,e in enumerate(edges):
    way=merged['way',e['osmWayId']];tags=way.get('tags',{});ids=way['nodes']
    a,b=nodes[e['fromNode']]['osmId'],nodes[e['toNode']]['osmId']
    assert a in ids and b in ids,(i,'endpoints absent from source way',a,b)
    one=tags.get('oneway','yes' if tags.get('junction')=='roundabout' else 'no')
    source_order=set(zip(ids,ids[1:]));source_path=e.get('osmNodes',[])
    if source_path:
        if one in ('yes','1','true'):assert all((a,b) in source_order for a,b in zip(source_path,source_path[1:])),(i,'one-way violation')
        if one=='-1':assert all((b,a) in source_order for a,b in zip(source_path,source_path[1:])),(i,'reverse one-way violation')
    elif ids[0]!=ids[-1]:
        if one in ('yes','1','true'):assert ids.index(a)<ids.index(b) or a==b,(i,'one-way violation')
        if one=='-1':assert ids.index(a)>ids.index(b) or a==b,(i,'reverse one-way violation')
    checks+=1
    p=np.array([[v['x'],v['z']] for v in e['points']]);dist,_=tree.query(p)
    assert max(dist)<3.0,(i,'path leaves authored centreline',max(dist))
    samples+=len(p)
    for j in e['allowedNext']:
        n=edges[j];assert e['toNode']==n['fromNode'];assert e['osmNodes']!=n['osmNodes'][::-1]
        only=set()
        for r in network['restrictions']:
            if r['fromWay']!=e['osmWayId'] or r['viaNode']!=nodes[e['toNode']]['osmId']:continue
            applicable.add((r['fromWay'],r['viaNode'],r['toWay']))
            if r['rule']=='no_u_turn' and n['osmWayId']==r['toWay']:
                ep=e['points'];np_=n['points'];a=np.array([ep[-1]['x']-ep[-2]['x'],ep[-1]['z']-ep[-2]['z']]);b=np.array([np_[1]['x']-np_[0]['x'],np_[1]['z']-np_[0]['z']])
                assert np.dot(a,b)>=-.85*np.linalg.norm(a)*np.linalg.norm(b),(i,j,r)
            elif r['rule'].startswith('no_') and r['rule']!='no_u_turn':assert n['osmWayId']!=r['toWay'],(i,j,r)
            if r['rule'].startswith('only_'):only.add(r['toWay'])
        if only:assert n['osmWayId'] in only,(i,j,only)
        turns+=1
graph=csr_matrix((np.ones(len(edges)),([e['fromNode'] for e in edges],[e['toNode'] for e in edges])),shape=(len(nodes),len(nodes)))
components,labels=connected_components(graph,connection='strong')
sizes=sorted(np.bincount(labels).tolist(),reverse=True)
report=dict(sourceDirectionChecks=checks,allowedTurnChecks=turns,applicableRestrictionRelations=len(applicable),surfaceCentrelineSamples=samples,
            strongComponents=components,largestStrongComponents=sizes[:8],nearbyRouteEdges=[])
for station in data['stations']:
    p=data['points'][station['index']];eligible=0
    for e in edges:
        if any((v['x']-p['x'])**2+(v['z']-p['z'])**2<420**2 for v in e['points']):eligible+=1
    assert eligible>=2,(station['name'],'no through traffic')
    report['nearbyRouteEdges'].append(dict(station=station['name'],edges=eligible))
target=ROOT/'build/jeju-drive/traffic-data-checks.json';target.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
