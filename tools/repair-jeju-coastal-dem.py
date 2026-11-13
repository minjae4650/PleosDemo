"""Remove isolated offshore DEM spikes, retaining the mountain and road elevations."""
from pathlib import Path
import json
import sys
import numpy as np

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'build/jeju-drive/pythondeps'))
from scipy.ndimage import median_filter,binary_dilation
from scipy.spatial import cKDTree


def offshore_repairs(raw,minimum,size,roads,coastlines=None):
    n=raw.shape[0]
    x,z=np.meshgrid(np.linspace(minimum['x'],minimum['x']+size['x'],n),np.linspace(minimum['y'],minimum['y']+size['y'],n))
    median=median_filter(raw,size=9,mode='nearest')
    distance,_=cKDTree(np.asarray(roads)[:,[0,2]]).query(np.stack((x,z),axis=-1))
    offshore=(z<-5500)&(median<8)&(distance>80)
    seeds=(raw>150)&offshore
    mask=binary_dilation(seeds,iterations=4)&offshore&(np.abs(raw-median)>2)
    result=raw.copy();result[mask]=median[mask]
    # The Terrarium surface can be slightly positive offshore. Around the Daepo
    # landmark, use the OSM directed shoreline (water on the right) rather than
    # rendering that low positive surface as a grass-covered sea floor.
    if coastlines:
        reference=np.array([(126.42586-126.46)*111195.08*np.cos(np.deg2rad(33.29)),(33.23834-33.29)*111195.08])
        region=((x-reference[0])**2+(z-reference[1])**2<650**2)&(distance>95)&(raw<80)
        samples=[];starts=[];ends=[]
        for line in coastlines:
            p=np.array([[v['x'],v['z']] for v in line['points']])
            for a,b in zip(p,p[1:]):
                if np.linalg.norm(a-b)<.01:continue
                for t in np.linspace(0,1,max(2,int(np.linalg.norm(b-a)/20)+1)):
                    samples.append(a+(b-a)*t);starts.append(a);ends.append(b)
        probes=np.stack((x[region],z[region]),axis=-1)
        if len(probes) and samples:
            _,ids=cKDTree(np.asarray(samples)).query(probes,k=3)
            a=np.asarray(starts)[ids];b=np.asarray(ends)[ids];v=b-a
            t=np.clip(np.sum((probes[:,None,:]-a)*v,axis=-1)/np.maximum(.001,np.sum(v*v,axis=-1)),0,1)
            nearest=a+v*t[:,:,None];choice=np.argmin(np.sum((nearest-probes[:,None,:])**2,axis=-1),axis=1)
            a=a[np.arange(len(a)),choice];v=v[np.arange(len(v)),choice];delta=probes-a
            water=v[:,0]*delta[:,1]-v[:,1]*delta[:,0]<0
            ocean=np.zeros_like(mask);ocean[region]=water
            mask|=ocean;result[ocean]=-3
    return result,mask


if __name__=='__main__':
    folder=ROOT/'unity/InputProbe/Assets/_Parkside/JejuDrive/Data'
    output=ROOT/'build/jeju-drive/coastal-landmarks';output.mkdir(parents=True,exist_ok=True)
    data=json.loads((folder/'JejuRoadData.json').read_text(encoding='utf-8'))
    raw=np.fromfile(folder/'TerrainOriginal.bytes',dtype='<f4').reshape(data['terrainResolution'],data['terrainResolution'])
    roads=np.array([[p['x'],p['y'],p['z']] for r in [data]+data['branches'] for p in r['points']])
    fixed,mask=offshore_repairs(raw,data['terrainMin'],data['terrainSize'],roads,data['coastlines'])
    for name in ('TerrainOriginal.bytes','TerrainHeights.bytes'):
        backup=output/('before-coastal-'+name)
        if not backup.exists():backup.write_bytes((folder/name).read_bytes())
    carved=np.fromfile(folder/'TerrainHeights.bytes',dtype='<f4').reshape(raw.shape)
    carved[mask]=np.where(fixed[mask]<1,-3,fixed[mask])
    fixed.astype('<f4').tofile(folder/'TerrainOriginal.bytes');carved.astype('<f4').tofile(folder/'TerrainHeights.bytes')
    rows=[dict(row=int(j),column=int(i),before=float(raw[j,i]),after=float(fixed[j,i])) for j,i in np.argwhere(mask)]
    report=dict(date='2026-11-07',method='Isolated offshore >150m peaks, low 9x9 median and >80m road clearance; bounded halo replacement.',repairedVertices=len(rows),samples=rows)
    (folder/'CoastalTerrainRepairs.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('Repaired offshore vertices:',len(rows))
