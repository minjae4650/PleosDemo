using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Geo = Parkside.Jeju.Editor.JejuDriveBuilder.Geo;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        static void BuildEiffelTower(Transform root)
        {
            var group=Group("Tour_Eiffel_330m",root);
            var steel=new Geo();
            var decks=new Geo();
            var stone=new Geo();
            var rotation=Quaternion.Euler(0,-38,0);
            Vector3 P(float x,float y,float z) => rotation*new Vector3(x,y,z);
            Vector3 Leg(int sx,int sz,float y)
            {
                float f=y/115;
                float spread=17+36*Mathf.Pow(1-f,1.28f);
                return P(sx*spread,y,sz*spread);
            }
            foreach(int sx in new[]{-1,1})
                foreach(int sz in new[]{-1,1})
                {
                    stone.Box(Leg(sx,sz,0)+Vector3.up*.68f,new Vector3(17,1.36f,17),rotation,Limestone);
                    for(int i=0;i<17;i++)
                    {
                        float y0=i*115f/17,y1=(i+1)*115f/17;
                        float half0=Mathf.Lerp(7.1f,2.55f,y0/115),half1=Mathf.Lerp(7.1f,2.55f,y1/115);
                        BracedBay(steel,Leg(sx,sz,y0),Leg(sx,sz,y1),half0,half1,rotation,
                            Mathf.Lerp(.61f,.30f,y0/115));
                    }
                }
            for(int i=0;i<27;i++)
            {
                float y0=115+i*(161f/27), y1=115+(i+1)*(161f/27);
                float half0=4.6f+12.4f*Mathf.Pow(1-(y0-115)/161,2.12f);
                float half1=4.6f+12.4f*Mathf.Pow(1-(y1-115)/161,2.12f);
                BracedBay(steel,P(0,y0,0),P(0,y1,0),half0,half1,rotation,Mathf.Lerp(.42f,.18f,(y0-115)/161));
            }
            foreach(int side in new[]{-1,1})
            {
                for(int i=0;i<26;i++)
                {
                    float t0=i/26f,t1=(i+1)/26f;
                    float x0=Mathf.Lerp(-48,48,t0),x1=Mathf.Lerp(-48,48,t1);
                    float h0=10+44*Mathf.Sin(t0*Mathf.PI),h1=10+44*Mathf.Sin(t1*Mathf.PI);
                    float z0=side*(32+16*Mathf.Abs(2*t0-1)),z1=side*(32+16*Mathf.Abs(2*t1-1));
                    steel.Rod(P(x0,h0,z0),P(x1,h1,z1),.87f,Iron,6);
                    steel.Rod(P(z0,h0,x0),P(z1,h1,x1),.87f,Iron,6);
                    if(i%2==0)
                    {
                        steel.Rod(P(x0,h0,z0),P(x0,56,side*34),.23f,Iron,5);
                        steel.Rod(P(z0,h0,x0),P(side*34,56,x0),.23f,Iron,5);
                    }
                }
            }
            TowerPlatform(decks,57.6f,72,rotation);
            TowerPlatform(decks,115.7f,39,rotation);
            TowerPlatform(decks,276,12.7f,rotation);
            decks.Box(new Vector3(0,289,0),new Vector3(7.6f,17,7.6f),rotation,Iron);
            steel.Rod(new Vector3(0,296,0),new Vector3(0,330,0),.39f,Iron,8);
            for(int i=0;i<7;i++)
            {
                float y=298+i*4.5f;
                steel.Rod(P(-1.65f,y,0),P(1.65f,y,0),.11f,Iron,5);
                steel.Rod(P(0,y,-1.65f),P(0,y,1.65f),.11f,Iron,5);
            }
            var bronze=Surface("Eiffel_brown_iron",Color.white,smooth:.25f);
            Save(steel,"Riveted_iron_lattice",bronze,group,true);
            Save(decks,"Observation_levels",bronze,group,true);
            Save(stone,"Stone_foundations",Surface("Tower_footings",Color.white),group,true);
        }

        static void BracedBay(Geo steel,Vector3 low,Vector3 high,float half0,float half1,Quaternion q,float beam)
        {
            Vector3[] corners={new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(1,0,1),new Vector3(-1,0,1)};
            for(int i=0;i<corners.Length;i++)
            {
                int next=(i+1)%corners.Length;
                Vector3 a=low+q*corners[i]*half0,b=low+q*corners[next]*half0;
                Vector3 c=high+q*corners[i]*half1,d=high+q*corners[next]*half1;
                steel.Rod(a,c,beam,Iron,6);
                steel.Rod(a,b,beam*.68f,Iron,5);
                steel.Rod(a,d,beam*.52f,Iron,5);
                steel.Rod(b,c,beam*.52f,Iron,5);
            }
        }

        static void TowerPlatform(Geo mesh,float y,float width,Quaternion q)
        {
            var level=Vector3.up*y;
            mesh.Box(level,new Vector3(width,2.45f,width),q,Iron);
            var band=new Color(.43f,.32f,.23f);
            mesh.Box(level+Vector3.up*1.37f,new Vector3(width+.9f,.32f,width+.9f),q,band);
            for(int side=-1;side<=1;side+=2)
                for(float i=-width*.47f;i<width*.48f;i+=1.55f)
                {
                    var a=level+q*new Vector3(i,1.48f,side*(width*.5f-.13f));
                    var b=level+q*new Vector3(side*(width*.5f-.13f),1.48f,i);
                    mesh.Rod(a,a+Vector3.up*1.37f,.11f,Iron,5);
                    mesh.Rod(b,b+Vector3.up*1.37f,.11f,Iron,5);
                }
        }

        static void BuildBirHakeim(Source s,Transform root)
        {
            var positions=s.roads.Where(r=>r.bridge&&r.name=="Pont de Bir Hakeim").SelectMany(r=>r.points).ToArray();
            if(positions.Length<2) return;
            var group=Group("Bir_Hakeim_Viaduct",root);
            var steel=new Geo();
            var piers=new Geo();
            var centre=positions.Aggregate(Vector3.zero,(a,p)=>a+p)/positions.Length;
            var a=positions.OrderBy(p=>p.z).First();
            var b=positions.OrderByDescending(p=>p.z).First();
            var forward=(b-a).normalized;
            float low=positions.Min(p=>Vector3.Dot(p-centre,forward)),high=positions.Max(p=>Vector3.Dot(p-centre,forward));
            var q=Quaternion.LookRotation(forward);
            var colour=new Color(.32f,.40f,.36f);
            steel.Box(centre+forward*((low+high)*.5f)+Vector3.up*7.4f,new Vector3(8.7f,.85f,high-low),q,colour);
            var right=Vector3.Cross(Vector3.up,forward);
            for(float d=low+1;d<high-1;d+=7.5f)
            {
                var p=centre+forward*d;
                steel.Rod(p,p+Vector3.up*7.5f,.23f,colour,9);
                steel.Rod(p+Vector3.up*5.1f,p+Vector3.up*7.0f+right*2.4f,.14f,colour,7);
                steel.Rod(p+Vector3.up*5.1f,p+Vector3.up*7.0f-right*2.4f,.14f,colour,7);
                steel.Box(p+Vector3.up*7,new Vector3(8,.18f,.32f),q,colour);
            }
            for(float d=low+18;d<high-12;d+=38)
                piers.Box(centre+forward*d-Vector3.up*2.2f,new Vector3(13.8f,4.2f,3.1f),q,Limestone);
            Save(steel,"Metro_ironwork",Surface("Metro_steel",Color.white),group,true);
            Save(piers,"River_piers",Surface("Bridge_limestone",Color.white,"KoreanAssets/concrete_wall_009_diff_2k.jpg"),group,true);
        }

        static void BuildStreetTrees(Source s,Transform root)
        {
            var group=Group("Plane_Trees",root);
            var leaves=new Geo();
            var trunks=new Geo();
            var cells=new HashSet<Vector2Int>();
            var scanned=Asset<GameObject>(Env+"/RealVehicles/tree-small-far/tree-small-far.prefab");
            int scanCount=0;
            var route=s.guide.Where((_,i)=>i%16==0).ToArray();
            var photoAreas=s.stations.Select(station=>s.guide[station.index]).ToArray();
            var closeTrees=new HashSet<Vector3>(photoAreas.SelectMany(at=>s.trees
                .Where(p=>(p-at).sqrMagnitude<175*175)
                .OrderBy(p=>(p-at).sqrMagnitude).Take(19)));
            foreach(var p in s.trees.OrderBy(p=>closeTrees.Contains(p)?-1:route.Min(v=>(v-p).sqrMagnitude)))
            {
                var cell=new Vector2Int(Mathf.FloorToInt(p.x/18),Mathf.FloorToInt(p.z/18));
                if(!cells.Add(cell)) continue;
                float n=Mathf.PerlinNoise(p.x*.013f,p.z*.013f);
                float h=7+n*4;
                if (scanCount<155)
                {
                    var instance=(GameObject)PrefabUtility.InstantiatePrefab(scanned,group);
                    instance.transform.SetPositionAndRotation(p,Quaternion.Euler(0,n*340,0));
                    var renderers=instance.GetComponentsInChildren<Renderer>();
                    var bounds=renderers[0].bounds;
                    foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    float scale=h/Mathf.Max(.1f,bounds.size.y);
                    instance.transform.localScale*=scale;
                    instance.transform.position+=(p-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z))*scale;
                    scanCount++;
                    continue;
                }
                var dark=new Color(.29f,.37f,.17f);
                var light=new Color(.51f,.60f,.29f);
                trunks.Rod(p,p+Vector3.up*(h*.84f),.19f,new Color(.39f,.34f,.25f),6);
                leaves.Rock(p+Vector3.up*(h*.83f),new Vector3(3.35f,2.8f,3.3f),n*102,Color.Lerp(dark,light,n));
                leaves.Rock(p+new Vector3(1.8f,h*.67f,.9f),new Vector3(2.5f,2.1f,2.6f),n*79,Color.Lerp(dark,light,.48f));
                leaves.Rock(p+new Vector3(-1.4f,h*.72f,-1.1f),new Vector3(2.6f,2.25f,2.5f),n*18,Color.Lerp(dark,light,.39f));
            }
            Save(trunks,"Trunks",Surface("Plane_tree_bark",Color.white),group,true);
            Save(leaves,"Tree_crowns",Surface("Plane_tree_leaves",Color.white,"KoreanAssets/grass_ground_diff_2k.jpg"),group,true);
        }
    }
}
