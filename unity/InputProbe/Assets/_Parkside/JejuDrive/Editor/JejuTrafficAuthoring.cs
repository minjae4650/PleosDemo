using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Parkside.Jeju.Editor
{
 public static partial class JejuDriveBuilder
 {
    static JejuTrafficNetwork authoredNetwork;
    static GameObject[] trafficPrefabs;
    public static void VerifyTrafficPlay()
    {
        var current=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(current.path!=ScenePath)EditorSceneManager.OpenScene(ScenePath);
        JejuDriveVerification.BeginPlayCheck();
    }
    public static void RefreshTrafficAndVerify()
    {
        try
        {
            scene=EditorSceneManager.OpenScene(ScenePath);
            data=JsonUtility.FromJson<Source>(File.ReadAllText(Folder+"/Data/JejuRoadData.json"));PrepareRoadLookup();
            SealRoadbedSeams();
            foreach(var filter in Object.FindObjectsOfType<MeshFilter>())
            {
                if(filter.name!="Stop_lines_zebras_and_lane_arrows")continue;
                var mesh=filter.sharedMesh;var normals=mesh.normals;
                for(int i=0;i<normals.Length;i++)if(normals[i].sqrMagnitude<.5f)normals[i]=Vector3.up;
                mesh.normals=normals;EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
            Verify();JejuDriveVerification.BeginPlayCheck();
        }
        catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"traffic-refresh-error.txt"),e.ToString());EditorApplication.Exit(1);}
    }
    static void SealRoadbedSeams()
    {
        var temporary=new Dictionary<Collider,MeshFilter>();var affected=new HashSet<MeshFilter>();
        foreach(var f in Object.FindObjectsOfType<MeshFilter>())if(f.name=="Dense_Roadside_Ground")
        {var c=f.gameObject.AddComponent<MeshCollider>();c.sharedMesh=f.sharedMesh;temporary.Add(c,f);}
        Physics.SyncTransforms();
        foreach(var road in detailRoads)for(int i=0;i<road.p.Length-1;i++)
        {
            Vector3 r=Vector3.Cross(Vector3.up,road.p[i+1]-road.p[i]).normalized;
            for(int side=-1;side<=1;side++)
            {
                var p=road.p[i]+r*side*1.5f;
                if(Physics.Raycast(p+Vector3.up*3,Vector3.down,out var hit,6)&&temporary.TryGetValue(hit.collider,out var f))affected.Add(f);
            }
        }
        foreach(var c in temporary.Keys)Object.DestroyImmediate(c);
        foreach(var f in affected)
        {
            var mesh=f.sharedMesh;var vertices=mesh.vertices.ToList();var triangles=mesh.triangles.ToList();var uv=mesh.uv.ToList();var colours=mesh.colors.ToList();
            ClipRoadBed(ref vertices,ref triangles,ref uv,ref colours,f.transform.parent);
            mesh.Clear();mesh.indexFormat=vertices.Count>65534?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,uv);mesh.SetColors(colours);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.UploadMeshData(false);EditorUtility.SetDirty(mesh);
        }
        Debug.Log("Reclipped roadbed seam meshes: "+affected.Count);
    }
    static void PrepareTrafficAssets()
    {
        authoredNetwork=JsonUtility.FromJson<JejuTrafficNetwork>(File.ReadAllText(Folder+"/Data/JejuTrafficNetwork.json"));authoredNetwork.Prepare();
        signalCentres=authoredNetwork.nodes.Where(n=>n.degree>=3||n.signal>=0).Select(n=>n.point).ToArray();
        var names=new[]{"hyundai-tucson-lod1","hyundai-i30-lod1","hyundai-azera-lod1"};trafficPrefabs=new GameObject[names.Length];
        var red=Material("TrafficBrakeLamp",new Color(.5f,.015f,.005f),.4f);red.EnableKeyword("_EMISSION");red.SetColor("_EmissionColor",new Color(.3f,.005f,.002f));
        var amber=Material("TrafficIndicator",new Color(1,.4f,.01f));amber.EnableKeyword("_EMISSION");amber.SetColor("_EmissionColor",new Color(2.5f,.7f,.005f));
        for(int i=0;i<names.Length;i++)
        {
            var wrapper=new GameObject("JejuTraffic_"+names[i]);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(Environment+"/RealVehicles/"+names[i]+"/"+names[i]+".prefab"));
            model.transform.SetParent(wrapper.transform,false);model.transform.localRotation=Quaternion.Euler(0,180,0);
            var b=Bounds(model);float scale=4.6f/Mathf.Max(1,b.size.z);model.transform.localScale*=scale;b=Bounds(model);
            model.transform.localPosition-=new Vector3(b.center.x,b.min.y,b.center.z);
            foreach(var c in model.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
            foreach(var t in model.GetComponentsInChildren<Transform>()){t.gameObject.isStatic=false;t.gameObject.layer=2;}
            wrapper.layer=2;
            var collider=wrapper.AddComponent<BoxCollider>();collider.center=new Vector3(0,.8f,0);collider.size=new Vector3(1.9f,1.6f,4.6f);collider.isTrigger=true;
            for(int side=-1;side<=1;side+=2)
            {
                TrafficLamp(wrapper.transform,"NPC_BrakeLamp",new Vector3(side*.69f,.77f,-2.28f),red);
                var left=TrafficLamp(wrapper.transform,"NPC_Turn_"+(side<0?"Left":"Right"),new Vector3(side*.79f,.7f,-2.3f),amber);left.gameObject.SetActive(false);
                var front=TrafficLamp(wrapper.transform,"NPC_Turn_"+(side<0?"Left":"Right"),new Vector3(side*.79f,.7f,2.3f),amber);front.gameObject.SetActive(false);
            }
            trafficPrefabs[i]=PrefabUtility.SaveAsPrefabAsset(wrapper,Folder+"/Generated/Prefabs/JejuTraffic_"+names[i]+".prefab");Object.DestroyImmediate(wrapper);
        }
    }
    static Transform TrafficLamp(Transform parent,string name,Vector3 position,Material material)
    {
        var lamp=GameObject.CreatePrimitive(PrimitiveType.Cube);lamp.name=name;lamp.transform.SetParent(parent,false);lamp.layer=2;
        lamp.transform.localPosition=position;lamp.transform.localScale=new Vector3(.22f,.075f,.035f);Object.DestroyImmediate(lamp.GetComponent<Collider>());
        lamp.GetComponent<Renderer>().sharedMaterial=material;return lamp.transform;
    }
    static void MakeAmbientTraffic(JejuDriveController controller,Transform parent)
    {
        var go=Group("Ambient_OSM_Traffic",parent);var traffic=go.gameObject.AddComponent<JejuAmbientTraffic>();
        traffic.networkSource=Load<TextAsset>(Folder+"/Data/JejuTrafficNetwork.json");traffic.vehiclePrefabs=trafficPrefabs;
        traffic.signals=controller.traffic;traffic.player=controller;controller.ambient=traffic;
        var branch=data.branches.FirstOrDefault(b=>b.name.Contains("강정"));
        if(branch!=null)
        {
            int i=0;float best=float.PositiveInfinity;
            for(int k=1;k<branch.points.Length-2;k++)
            {
                float d=(new Vector2(branch.points[k].x-2100,branch.points[k].z+5900)).sqrMagnitude;
                if(d<best){best=d;i=k;}
            }
            Vector3 f=(branch.points[i+1]-branch.points[i]).normalized;
            var s=new JejuStation{name="월평·강정 해안",subtitle="확장된 동쪽 해안 순환도로",branchSpawn=true,
                position=branch.points[i]+Vector3.Cross(Vector3.up,f)*Mathf.Min(1.8f,branch.widths[i]*.23f),forward=f,index=0};
            controller.route.stations=controller.route.stations.Concat(new[]{s}).ToArray();
        }
    }
    // A road apron uses the real incoming/outgoing arm tangents, including skewed
    // T junctions. It closes the small gaps left by independently smoothed ribbons.
    static void MakeJunctionAprons()
    {
        var parent=Group("Actual_OSM_Junction_Connections",root);
        for(int ni=0;ni<authoredNetwork.nodes.Length;ni++)
        {
            var node=authoredNetwork.nodes[ni];if(node.degree<2)continue;
            var candidates=new List<Vector3>();
            foreach(var e in authoredNetwork.edges)
            {
                if(e.fromNode!=ni&&e.toNode!=ni)continue;
                bool outgoing=e.fromNode==ni;float d=outgoing?Mathf.Min(9,e.distances.Last()*.3f):Mathf.Max(0,e.distances.Last()-9);
                Vector3 p=JejuTrafficNetwork.Sample(e.points,e.distances,d,out var f);Vector3 r=Vector3.Cross(Vector3.up,f).normalized*e.width*.5f;
                candidates.Add(p+r);candidates.Add(p-r);
            }
            var ordered=candidates.OrderBy(p=>p.x).ThenBy(p=>p.z).ToList();var hull=new List<Vector3>();
            foreach(var p in ordered){while(hull.Count>=2&&Cross2(hull[hull.Count-2],hull[hull.Count-1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
            int lower=hull.Count;
            for(int i=ordered.Count-2;i>=0;i--){var p=ordered[i];while(hull.Count>lower&&Cross2(hull[hull.Count-2],hull[hull.Count-1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
            if(hull.Count<4)continue;hull.RemoveAt(hull.Count-1);
            var g=new Geo();Vector3 centre=node.point;centre.y+=.022f;
            for(int i=0;i<hull.Count;i++)
            {
                var a=hull[i];var b=hull[(i+1)%hull.Count];a.y=ClosestRoad(a.x,a.z,2).centre.y+.022f;b.y=ClosestRoad(b.x,b.z,2).centre.y+.022f;
                int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Max(Vector3.Distance(centre,a),Vector3.Distance(a,b),Vector3.Distance(b,centre))/1.5f));
                for(int row=0;row<steps;row++)for(int col=0;col<steps-row;col++)
                {
                    Vector3 p=centre+(a-centre)*(row/(float)steps)+(b-centre)*(col/(float)steps);
                    Vector3 u=p+(a-centre)/steps,v=p+(b-centre)/steps;
                    ApronTriangle(g,p,u,v);
                    if(col<steps-row-1)ApronTriangle(g,u,u+(b-centre)/steps,v);
                }
            }
            g.Save("Asphalt",asphalt,Group("OSM_Junction_"+node.osmId,parent),false,true);
        }
    }
    static float Cross2(Vector3 a,Vector3 b,Vector3 c)=>(b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x);
    static void ApronTriangle(Geo g,Vector3 a,Vector3 b,Vector3 c)
    {
        int n=g.v.Count;
        foreach(var p0 in new[]{a,b,c}){var p=p0;p.y=ClosestRoad(p.x,p.z,2).centre.y+.022f;g.Vertex(p,new Vector2(p.x,p.z)/8,Color.white);}
        g.t.AddRange(new[]{n,n+2,n+1});
    }
 }
}
