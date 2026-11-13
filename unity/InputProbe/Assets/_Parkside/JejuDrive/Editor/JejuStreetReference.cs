using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Parkside.Jeju.Editor
{
    public static partial class JejuDriveBuilder
    {
        const string StreetReferenceRoot="Seogwipo_Roadview_Streets";
        const string StreetMeshes=Folder+"/Generated/StreetReference";
        const float RoadLift=.17f;
        static readonly Color PaintWhite=new Color(.90f,.91f,.87f);
        static readonly Color PaintYellow=new Color(.98f,.70f,.12f);

        [Serializable] sealed class StreetReference
        {
            public StreetSample[] streets=Array.Empty<StreetSample>();
            public BuildingSource[] buildings=Array.Empty<BuildingSource>();
            public ParkingSample[] parking=Array.Empty<ParkingSample>();
        }

        [Serializable] sealed class StreetSample
        {
            public string name;
            public long osmId;
            public float width;
            public bool oneWay;
            public int lanes;
            public Vector3[] points;
            [NonSerialized] public float[] distances;
            [NonSerialized] public Bounds bounds;
            [NonSerialized] public bool connectsStart,connectsEnd;
        }

        [Serializable] sealed class ParkingSample
        {
            public long osmId;
            public Vector3[] points,triangles;
        }

        static StreetReference ReadStreetReference()
        {
            string path=Folder+"/Data/SeogwipoStreetReference.json";
            if(!File.Exists(path)) return new StreetReference();
            var reference=JsonUtility.FromJson<StreetReference>(File.ReadAllText(path));
            foreach(var street in reference.streets)
            {
                street.distances=JejuTrafficNetwork.Distances(street.points);
                var bounds=new Bounds(street.points[0],Vector3.zero);
                foreach(var p in street.points) bounds.Encapsulate(p);
                street.bounds=bounds;
                var ends=reference.streets.Where(s=>s!=street&&s.name==street.name&&s.oneWay==street.oneWay)
                    .SelectMany(s=>new[]{s.points[0],s.points[s.points.Length-1]}).ToArray();
                street.connectsStart=ends.Any(p=>XZDistanceSquared(p,street.points[0])<9);
                street.connectsEnd=ends.Any(p=>XZDistanceSquared(p,street.points[street.points.Length-1])<9);
            }
            return reference;
        }

        static float XZDistanceSquared(Vector3 a,Vector3 b)
        {
            float x=a.x-b.x,z=a.z-b.z;
            return x*x+z*z;
        }

        static float StreetWidth(StreetSample street,float metres)
        {
            float start=street.connectsStart?1:Mathf.Clamp01(metres/19);
            float end=street.connectsEnd?1:Mathf.Clamp01((street.distances.Last()-metres)/19);
            return Mathf.Lerp(7.6f,street.width,Mathf.Min(start,end));
        }

        static bool TryStreet(Vector3 p,StreetReference reference,float radius,out StreetSample match,out float metres,out float distance)
        {
            match=null;metres=0;distance=radius*radius;
            foreach(var street in reference.streets)
            {
                if(p.x<street.bounds.min.x-radius||p.x>street.bounds.max.x+radius||
                    p.z<street.bounds.min.z-radius||p.z>street.bounds.max.z+radius) continue;
                for(int i=0;i<street.points.Length-1;i++)
                {
                    var a=street.points[i];
                    var delta=street.points[i+1]-a;delta.y=0;
                    var offset=p-a;offset.y=0;
                    float u=Mathf.Clamp01(Vector3.Dot(delta,offset)/Mathf.Max(.0001f,delta.sqrMagnitude));
                    float sq=XZDistanceSquared(p,a+delta*u);
                    if(sq>=distance) continue;
                    distance=sq;match=street;
                    metres=Mathf.Lerp(street.distances[i],street.distances[i+1],u);
                }
            }
            return match!=null;
        }

        static bool InReferenceRoad(Vector3 p,StreetReference reference,float margin)
        {
            if(!TryStreet(p,reference,8+margin,out var street,out float metres,out float distance)) return false;
            float radius=StreetWidth(street,metres)*.5f+margin;
            return distance<radius*radius;
        }

        static void ApplyStreetWidths(JejuDriveRoute route,StreetReference reference)
        {
            if (route.branches==null || data.branches==null) return;
            for (int branchIndex=0;branchIndex<route.branches.Length;branchIndex++)
            {
                var branch=route.branches[branchIndex];
                var source=data.branches[branchIndex];
                branch.points=source.points.Select(GradedRoad).ToArray();
                branch.widths=(float[])source.widths.Clone();
                for(int i=0;i<branch.points.Length;i++)
                    if(TryStreet(branch.points[i],reference,1.15f,out var street,out float metres,out _))
                        branch.widths[i]=Mathf.Max(branch.widths[i],StreetWidth(street,metres));
            }
        }

        static void BuildStreetReference(Transform rootObject,JejuDriveController car=null)
        {
            var reference=ReadStreetReference();
            if(reference.streets.Length==0) return;
            Directory.CreateDirectory(StreetMeshes);
            var group=Group(StreetReferenceRoot,rootObject);
            var road=Load<Material>(Folder+"/Generated/Materials/Asphalt.mat");
            var curb=CityMaterial("Roadview_concrete_curbs",new Color(.69f,.69f,.65f),.06f);
            var blocks=CityMaterial("Roadview_red_pavers",new Color(.78f,.59f,.49f),.08f,
                Load<Texture2D>(Environment+"/KoreanAssets/concrete_pavement_diff_2k.jpg"));
            var paint=CityMaterial("Roadview_worn_paint",Color.white,.03f);
            var widthMeshes=new Geo();
            var edges=new Geo();
            var sidewalks=new Geo();
            var stripes=new Geo();

            foreach(var street in reference.streets)
            {
                float end=street.distances.Last();
                for(float d=0;d<end-.02f;d+=1.5f)
                {
                    float next=Mathf.Min(end,d+1.5f);
                    var a=StreetPoint(street,d,out var ra);
                    var b=StreetPoint(street,next,out var rb);
                    float wa=StreetWidth(street,d)*.5f,wb=StreetWidth(street,next)*.5f;
                    // Use the same vertex stations on all adjoining asphalt quads.
                    FlatFace(widthMeshes,a-ra*wa,a+ra*wa,b-rb*wb,b+rb*wb,Color.white,.24f);
                    if(NearStreetJunction((a+b)*.5f,10)) continue;
                    for(int side=-1;side<=1;side+=2)
                    {
                        if(street.oneWay&&side<0) continue;
                        var innerA=a+ra*wa*side;var innerB=b+rb*wb*side;
                        var outerA=innerA+ra*(side*2.25f);var outerB=innerB+rb*(side*2.25f);
                        var at=ClosestRoad(outerA.x,outerA.z,1);
                        if(at.road!=null&&at.d<at.road.w[at.i]*.5f+.15f) continue;
                        var kerbA=innerA+Vector3.up*.12f;var kerbB=innerB+Vector3.up*.12f;
                        if(side>0)
                        {
                            edges.Face(innerA,innerB,kerbA,kerbB,Color.white);
                            FlatFace(sidewalks,kerbA,outerA+Vector3.up*.12f,kerbB,outerB+Vector3.up*.12f,Color.white,.50f);
                        }
                        else
                        {
                            edges.Face(innerB,innerA,kerbB,kerbA,Color.white);
                            FlatFace(sidewalks,outerA+Vector3.up*.12f,kerbA,outerB+Vector3.up*.12f,kerbB,Color.white,.50f);
                        }
                    }
                }
                PaintReferenceStreet(street,stripes);
            }

            SaveStreet(widthMeshes,"Asphalt",road,group,true);
            SaveStreet(edges,"Raised_concrete_kerbs",curb,group);
            SaveStreet(sidewalks,"Red_and_grey_sidewalk_pavers",blocks,group);
            Physics.SyncTransforms();
            PaintObservedCrossing(stripes,reference,33.25249,126.50464);
            PaintObservedCrossing(stripes,reference,33.24635,126.50712);
            SaveStreet(stripes,"Centre_lines_lane_dividers_and_crossings",paint,group);
            MakeReferenceParking(reference,group,road,paint);
            MakeReferenceStreetTrees(reference,group);
            if(car!=null) ApplyStreetWidths(car.route,reference);
        }

        static void SaveStreet(Geo geometry,string name,Material material,Transform group,bool collider=false)
        {
            geometry.Save(name,material,group,collider:collider,assetPath:StreetMeshes+"/"+name+".asset");
        }

        static Vector3 StreetPoint(StreetSample street,float d,out Vector3 right)
        {
            var p=JejuTrafficNetwork.Sample(street.points,street.distances,d,out var tangent);
            var a=JejuTrafficNetwork.Sample(street.points,street.distances,Mathf.Max(0,d-2.5f),out _);
            var b=JejuTrafficNetwork.Sample(street.points,street.distances,Mathf.Min(street.distances.Last(),d+2.5f),out _);
            right=Vector3.Cross(Vector3.up,(b-a).sqrMagnitude>.0001f?b-a:tangent).normalized;
            p=GradedRoad(p);
            p.y+=RoadLift;
            return p;
        }

        static bool NearStreetJunction(Vector3 p,float radius)
        {
            float sq=radius*radius;
            return data.signals!=null&&data.signals.Any(s=>s.arms!=null&&XZDistanceSquared(s.centre,p)<sq);
        }

        static void PaintReferenceStreet(StreetSample street,Geo paint)
        {
            float end=street.distances.Last();
            for(float d=.5f;d<end-.02f;d+=.75f)
            {
                float next=Mathf.Min(end,d+.75f);
                var a=StreetPoint(street,d,out var ra)+Vector3.up*.024f;
                var b=StreetPoint(street,next,out var rb)+Vector3.up*.024f;
                var midpoint=(a+b)*.5f;
                if(NearStreetJunction(midpoint,20)||
                    XZDistanceSquared(midpoint,GeoPosition(33.25249,126.50464))<100||
                    XZDistanceSquared(midpoint,GeoPosition(33.24635,126.50712))<100) continue;
                float wa=StreetWidth(street,d),wb=StreetWidth(street,next);
                if(street.oneWay)
                {
                    if(Mathf.Repeat(d,9)<3.4f) Mark(paint,a,b,ra,rb,0,0,.13f,PaintWhite);
                }
                else
                {
                    Mark(paint,a,b,ra,rb,-.12f,-.12f,.12f,PaintYellow);
                    Mark(paint,a,b,ra,rb,.12f,.12f,.12f,PaintYellow);
                    if(wa>10&&wb>10&&Mathf.Repeat(d,10)<3.8f)
                    {
                        Mark(paint,a,b,ra,rb,-wa*.25f,-wb*.25f,.13f,PaintWhite);
                        Mark(paint,a,b,ra,rb,wa*.25f,wb*.25f,.13f,PaintWhite);
                    }
                }
                if(!street.oneWay)
                    for(int side=-1;side<=1;side+=2)
                        Mark(paint,a,b,ra,rb,side*(wa*.5f-.14f),side*(wb*.5f-.14f),.10f,PaintYellow*.88f);
            }
        }

        static void Mark(Geo mesh,Vector3 a,Vector3 b,Vector3 ra,Vector3 rb,float x,float y,float width,Color tint)
        {
            mesh.Face(a+ra*(x-width*.5f),a+ra*(x+width*.5f),b+rb*(y-width*.5f),b+rb*(y+width*.5f),tint);
        }

        static void FlatFace(Geo mesh,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color tint,float textureScale)
        {
            int n=mesh.v.Count;
            foreach(var p in new[]{a,b,c,d}) mesh.Vertex(p,new Vector2(p.x,p.z)*textureScale,tint);
            Quad(mesh.t,n,n+1,n+2,n+3);
        }

        static void PaintObservedCrossing(Geo paint,StreetReference reference,double latitude,double longitude)
        {
            Vector3 origin=GeoPosition(latitude,longitude);
            if(!TryStreet(origin,reference,17,out var street,out float metres,out _)) return;
            float start=Mathf.Clamp(metres-1.7f,1,street.distances.Last()-5);
            var a=StreetPoint(street,start,out var ra)+Vector3.up*.030f;
            var b=StreetPoint(street,start+3.4f,out var rb)+Vector3.up*.030f;
            float width=Mathf.Min(StreetWidth(street,start),StreetWidth(street,start+3.4f));
            for(float x=-width*.5f+.48f;x<width*.5f-.47f;x+=.90f) Mark(paint,a,b,ra,rb,x,x,.43f,PaintWhite);
        }

        static void MakeReferenceParking(StreetReference reference,Transform group,Material road,Material paint)
        {
            var asphaltMesh=new Geo();
            var marking=new Geo();
            var lamps=new Geo();
            foreach(var plot in reference.parking)
            {
                for(int i=0;i<plot.triangles.Length;i+=3)
                {
                    Vector3 a=ParkingHeight(plot.triangles[i]),b=ParkingHeight(plot.triangles[i+1]),c=ParkingHeight(plot.triangles[i+2]);
                    if(Vector3.Cross(b-a,c-a).y<0) (b,c)=(c,b);
                    int n=asphaltMesh.v.Count;
                    foreach(var p in new[]{a,b,c}) asphaltMesh.Vertex(p,new Vector2(p.x,p.z)*.18f,Color.white);
                    asphaltMesh.t.Add(n);asphaltMesh.t.Add(n+1);asphaltMesh.t.Add(n+2);
                }
                var bounds=new Bounds(plot.points[0],Vector3.zero);
                foreach(var p in plot.points) bounds.Encapsulate(p);
                for(float z=bounds.min.z+5;z<bounds.max.z-5;z+=17)
                    for(float x=bounds.min.x+3;x<bounds.max.x-3;x+=2.65f)
                    {
                        var a=new Vector3(x,0,z);var b=a+Vector3.forward*4.85f;
                        if(!InsidePlot(a,plot.points)||!InsidePlot(b,plot.points)||InReferenceRoad(a,reference,1)) continue;
                        var p=ParkingHeight(a)+Vector3.up*.033f;var q=ParkingHeight(b)+Vector3.up*.033f;
                        Mark(marking,p,q,Vector3.right,Vector3.right,0,0,.11f,PaintWhite);
                    }
                var at=bounds.center;
                if(InsidePlot(at,plot.points))
                {
                    at=ParkingHeight(at);
                    var head=at+Vector3.up*7;
                    lamps.Rod(at,head,.10f,new Color(.30f,.31f,.30f),8);
                    lamps.Box(head,new Vector3(2.0f,.16f,.40f),Quaternion.identity,new Color(.53f,.55f,.52f));
                }
            }
            SaveStreet(asphaltMesh,"Stadium_parking_asphalt",road,group);
            SaveStreet(marking,"Stadium_parking_bays",paint,group);
            SaveStreet(lamps,"Stadium_parking_lamps",CityMaterial("Roadview_iron",Color.white),group);
        }

        static void MakeReferenceStreetTrees(StreetReference reference,Transform group)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Generated/Prefabs/JejuBroadleaf.prefab");
            if(prefab==null) return;
            foreach(var street in reference.streets.Where(s=>s.name=="대청로"||s.name=="신중로"))
                for(float d=13;d<street.distances.Last()-8;d+=23)
                {
                    var p=StreetPoint(street,d,out var right);
                    if(NearStreetJunction(p,18)) continue;
                    for(int side=-1;side<=1;side+=2)
                    {
                        if(street.oneWay&&side<0) continue;
                        var at=p+right*(side*(StreetWidth(street,d)*.5f+1.53f));
                        if(InReferenceRoad(at,reference,.45f)) continue;
                        var tree=(GameObject)PrefabUtility.InstantiatePrefab(prefab,group);
                        tree.name="Pruned_street_tree";
                        tree.transform.SetPositionAndRotation(at+Vector3.up*.12f,Quaternion.Euler(0,d*19,0));
                        tree.transform.localScale*=.58f;
                    }
                }
        }

        static Vector3 ParkingHeight(Vector3 p)
        {
            p.y=DetailHeight(p.x,p.z)+.14f;
            return p;
        }

        static bool InsidePlot(Vector3 p,Vector3[] polygon)
        {
            bool inside=false;
            for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
            {
                var a=polygon[i];var b=polygon[j];
                if((a.z>p.z)!=(b.z>p.z) && p.x<(b.x-a.x)*(p.z-a.z)/(b.z-a.z)+a.x) inside=!inside;
            }
            return inside;
        }

        static void VerifyStreetReference(JejuDriveController car)
        {
            var reference=ReadStreetReference();
            var ground=new List<MeshCollider>();
            foreach(string name in new[]{"Real_DEM_Hallasan_Backdrop","Connected_Coastal_And_Village_Roads"})
                foreach(var filter in root.Find(name).GetComponentsInChildren<MeshFilter>())
                    if((filter.name.StartsWith("DEM_")||filter.name=="Dense_Roadside_Ground")&&GradingBounds(filter.GetComponent<Renderer>().bounds))
                    {
                        var c=filter.gameObject.AddComponent<MeshCollider>();
                        c.sharedMesh=filter.sharedMesh;
                        ground.Add(c);
                    }
            Physics.SyncTransforms();
            int samples=0;
            var issues=new List<string>();
            foreach(var street in reference.streets)
                for(float d=3;d<street.distances.Last()-3;d+=5)
                {
                    var p=StreetPoint(street,d,out var right);
                    foreach(float offset in new[]{-.34f,0,.34f})
                    {
                        var at=p+right*(offset*StreetWidth(street,d));
                        if(!Physics.Raycast(at+Vector3.up*2,Vector3.down,out var hit,4)||
                            hit.collider.name!="Asphalt"||Mathf.Abs(hit.point.y-at.y)>.34f)
                            issues.Add(street.name+" "+d+" "+at+" hit="+(hit.collider!=null?hit.collider.name:"none"));
                        samples++;
                    }
                }
            foreach(var c in ground) UnityEngine.Object.DestroyImmediate(c);
            var dir=Path.Combine(Output,"seogwipo");
            File.WriteAllLines(Path.Combine(dir,"roadview-road-failures.txt"),issues);
            if(issues.Count>0) throw new InvalidOperationException("Seogwipo road-view asphalt coverage failed: "+issues[0]);
            car.JumpToStation(9);
            car.cameraMode=0;car.UpdateCamera(true);
            Capture(car.view,Path.Combine(dir,"roadview-daechung.png"));
            CaptureReferenceView(car,reference,33.25506,126.50998,"roadview-sinjung.png",Vector3.left);
            CaptureReferenceView(car,reference,33.24661,126.50706,"roadview-worldcup.png",Vector3.back);
            car.JumpToStation(0);
            File.WriteAllText(Path.Combine(dir,"roadview-verification.txt"),
                "RoadSamples="+samples+"\nSourceStrips="+reference.streets.Length+"\nOvertureFootprints="+reference.buildings.Length+
                "\nParkingPlots="+reference.parking.Length+"\nCoverage=PASS");
        }

        static void CaptureReferenceView(JejuDriveController car,StreetReference reference,double latitude,double longitude,string name,Vector3 heading)
        {
            var position=GeoPosition(latitude,longitude);
            if(!TryStreet(position,reference,35,out var street,out float metres,out _)) return;
            var p=StreetPoint(street,metres,out var right);
            var forward=Vector3.Cross(right,Vector3.up).normalized;
            if(Vector3.Dot(forward,heading)<0) {forward=-forward;right=-right;}
            car.transform.SetPositionAndRotation(p+right*JejuDriveRoute.LaneOffset(StreetWidth(street,metres)),Quaternion.LookRotation(forward));
            car.UpdateCamera(true);
            Capture(car.view,Path.Combine(Output,"seogwipo",name));
        }
    }
}
