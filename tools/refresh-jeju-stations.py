"""Refresh the village review spawn and overview map without rebuilding geometry."""
import importlib.util
import json
from pathlib import Path
import numpy as np
from PIL import ImageDraw

spec=importlib.util.spec_from_file_location('base',Path(__file__).with_name('prepare-jeju-drive.py'))
base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
path=base.ASSETS/'Data/JejuRoadData.json';data=json.loads(path.read_text(encoding='utf-8'))
points=np.array([[p['x'],p['y'],p['z']] for p in data['points']])
centres=np.array([[sum(p['x'] for p in b['points'])/len(b['points']),sum(p['z'] for p in b['points'])/len(b['points'])] for b in data['buildings']])
views=[]
for i in range(data['stations'][0]['index']+100,len(points)-2,10):
    if points[i,1]>190:continue
    forward=points[i+1,[0,2]]-points[i,[0,2]];forward/=max(.001,np.linalg.norm(forward))
    delta=centres-points[i,[0,2]];near=np.linalg.norm(delta,axis=1)<160
    score=np.sum(near & (delta@forward>0))+.25*np.sum(near)
    views.append((score,i))
score,index=max(views);data['stations'][1]['index']=index
path.write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
minimum=data['terrainMin'];size=data['terrainSize']
im=base.terrain_map(base.Elevation(),(minimum['x'],minimum['y']),(size['x'],size['y']),points,data['stations'])
draw=ImageDraw.Draw(im)
for branch in data['branches']:
    line=[((p['x']-minimum['x'])/size['x']*1024,(1-(p['z']-minimum['y'])/size['y'])*1024) for p in branch['points']]
    draw.line(line,fill=(112,179,198),width=3)
im.save(base.ASSETS/'Data/RouteMap.png')
print(f'Village review station: {index}, view score: {score}',flush=True)
