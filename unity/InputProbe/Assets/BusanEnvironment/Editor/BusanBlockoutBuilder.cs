using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // Review blockout: all coordinates and elevations are design values, not survey data.
    public static class BusanBlockoutBuilder
    {
        const string Root = "Assets/BusanEnvironment/Generated";
        static Material road, provisional, land, sea, building, bridge;
        static readonly Dictionary<string, Vector3> points = new Dictionary<string, Vector3>();
        static readonly List<string> routeRecords = new List<string>();
        static Transform roads;

        [MenuItem("Busan/Build whole-city review blockout")]
        public static void Build()
        {
            Directory.CreateDirectory(Root);
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            road = Mat("Road", new Color(.14f,.19f,.24f));
            provisional = Mat("ProvisionalConnection", new Color(.9f,.48f,.14f));
            land = Mat("Land", new Color(.34f,.43f,.34f));
            sea = Mat("Sea", new Color(.06f,.25f,.34f));
            building = Mat("UrbanMass", new Color(.65f,.70f,.72f));
            bridge = Mat("Bridge", new Color(.77f,.78f,.73f));
            points.Clear(); routeRecords.Clear();
            roads = new GameObject("Roads - provisional geometry; verify junctions").transform;
            Box("Sea", new Vector3(0,-12,0), new Vector3(21000,10,19000), sea);
            Polygon("Mainland - schematic coast", new [] {
                V(-8500,-6500),V(-6100,-5800),V(-5000,-4000),V(-3200,-3000),
                V(-2300,-3400),V(-1900,-2100),V(-900,-1200),V(300,-500),
                V(1400,-600),V(2200,700),V(2700,1800),V(2800,2700),
                V(3500,3300),V(4100,3500),V(4400,4400),V(5200,4300),
                V(5900,3600),V(7400,4000),V(8500,4800),V(8500,8000),V(-8500,8000)
            }, land);
            Polygon("Yeongdo - separate island", new [] {
                V(-900,-1950),V(300,-1600),V(1100,-2300),V(1700,-3900),
                V(500,-4600),V(-800,-3900),V(-1400,-2800)
            }, land);
            Point("Haeundae",6800,4400,2); Point("MarineCity",5500,4050,2);
            Point("GwanganEast",4900,4800,8); Point("Gwangalli",3550,3550,2);
            Point("Namcheon",2700,2650,8); Point("Daeyeon",2100,1750,8);
            Point("Dongmyeong",2200,700,8); Point("Gamman",1600,-300,35);
            Point("YeongdoUpper",100,-1800,65); Point("YeongdoWest",-1050,-2450,32);
            Point("Songdo",-2350,-2700,2); Point("Gamcheon",-3700,-3600,8);
            Point("Dadaepo",-6100,-5300,2); Point("Seomyeon",0,3500,6);
            Point("Jeonpo",650,3500,10); Point("Beomnaegol",0,2500,6);
            Point("BusanStation",-1050,-100,4); Point("OldCity",-1500,-1450,4);
            Point("Hwangnyeong",1650,3600,220);
            Link("Haeundae-MarineCity",false,26,"Haeundae","MarineCity");
            Link("MarineCity-east approach",true,24,"MarineCity","GwanganEast");
            Route("Gwangan Bridge upper review deck",38,false, new [] {
                P("GwanganEast"),V(4950,3950,55),V(4500,2850,65),V(3650,2250,65),P("Namcheon")});
            Route("Gwangan Bridge lower review deck",38,false, new [] {
                P("GwanganEast")+Vector3.down*6,V(4950,3950,49),V(4500,2850,59),V(3650,2250,59),P("Namcheon")+Vector3.down*6});
            Link("Coastal city route",true,24,"GwanganEast","Gwangalli","Namcheon","Daeyeon","Dongmyeong");
            Route("Sinseondae corridor - tunnel alignment pending",26,true,new [] {P("Dongmyeong"),V(2100,200,-2),V(1800,-150,5),P("Gamman")});
            Link("Busan Harbor Bridge",false,30,"Gamman","YeongdoUpper");
            Link("Yeongdo through viaduct",false,22,"YeongdoUpper","YeongdoWest");
            Link("Namhang Bridge",false,25,"YeongdoWest","Songdo");
            Link("Western coastal city route",true,24,"Songdo","Gamcheon","Dadaepo");
            Link("Downtown arterial",true,28,"Seomyeon","Beomnaegol","BusanStation","OldCity","Songdo");
            Link("Daeyeon-downtown",true,24,"Daeyeon","Beomnaegol");
            Link("Jeonpo arterial",true,22,"Seomyeon","Jeonpo","Daeyeon");
            Route("Hwangnyeong mountain access - alignment pending",9,true,new [] {P("Jeonpo"),V(1000,3800,45),V(1300,4200,100),V(1900,4050,155),P("Hwangnyeong")});
            // Loop joins the Gamman-bound side only. Exit ramps remain separate.
            var loop = new List<Vector3>();
            for(int i=0;i<=80;i++) { float t=i/80f; float a=(180+360*t)*Mathf.Deg2Rad;
                loop.Add(new Vector3(550+270*Mathf.Cos(a),5+60*t,-2150+270*Mathf.Sin(a))); }
            loop.Add(Vector3.Lerp(P("YeongdoUpper"),P("Gamman"),.1f));
            Route("Yeongdo inbound loop - entry and merge provisional",8,true,loop.ToArray());
            Route("Yeongdo separate surface exit",8,true,new [] {P("YeongdoUpper")+new Vector3(-25,0,25),V(-180,-1900,40),V(-400,-2150,8)});
            Route("Songdo representative alley - topology only",7,true,new [] {P("Songdo"),V(-2500,-2500,8),V(-2650,-2300,14),V(-2200,-2150,15)});
            Route("Songdo alley arterial return",18,true,new [] {V(-2200,-2150,15),P("OldCity")});
            Route("Jeonpo representative alley loop",7,true,new [] {P("Jeonpo"),V(650,3700,10),V(300,3700,8),P("Seomyeon")});
            Towers("Gwangan",V(4500,2850,65),V(3650,2250,65),100);
            Towers("Harbor",Vector3.Lerp(P("Gamman"),P("YeongdoUpper"),.3f),Vector3.Lerp(P("Gamman"),P("YeongdoUpper"),.7f),130);
            foreach(var pair in points) { Label(pair.Key,pair.Value+Vector3.up*170); UrbanMass(pair.Key,pair.Value); }
            Box("Hwangnyeong lookout review pad",P("Hwangnyeong")+Vector3.down,new Vector3(50,2,40),road);
            Hill("Hwangnyeong ridge",new Vector3(1650,-10,3600),new Vector3(1200,430,950));
            Hill("Yeongdo ridge",new Vector3(300,-15,-3300),new Vector3(1100,400,1450));
            Hill("Western ridge",new Vector3(-4000,-10,-2300),new Vector3(1400,300,1000));
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type=LightType.Directional; sun.intensity=1.2f; sun.transform.rotation=Quaternion.Euler(48,-35,0);
            RenderSettings.ambientLight=new Color(.65f,.69f,.73f);
            var cam = new GameObject("Whole city review camera").AddComponent<Camera>();
            cam.transform.position=new Vector3(1000,14500,-12000); cam.transform.LookAt(new Vector3(0,0,1000));
            cam.orthographic=true;cam.orthographicSize=8500;cam.farClipPlane=50000;cam.backgroundColor=new Color(.06f,.16f,.22f);cam.clearFlags=CameraClearFlags.SolidColor;
            var scenePath=Root+"/BusanWholeCityReview.unity";
            EditorSceneManager.SaveScene(scene,scenePath);
            AssetDatabase.SaveAssets();
            string repo=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
            string outDir=Path.Combine(repo,"docs/concepts"); Directory.CreateDirectory(outDir);
            var rt=new RenderTexture(2000,1500,24); cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
            var image=new Texture2D(2000,1500,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,2000,1500),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(outDir,"busan-whole-city-blockout-v1.png"),image.EncodeToPNG());
            cam.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);
            File.WriteAllLines(Path.Combine(outDir,"busan-blockout-routes.txt"),routeRecords);
            Debug.Log("BUSAN_BLOCKOUT_OK: "+scenePath+"; routes="+routeRecords.Count);
        }
        static Vector3 V(float x,float z,float y=0) { return new Vector3(x,y,z); }
        static void Point(string id,float x,float z,float y) {points[id]=V(x,z,y);}
        static Vector3 P(string id) {return points[id];}
        static Material Mat(string name,Color color) {string p=Root+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(p);if(m==null){m=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(m,p);}m.color=color;return m;}
        static void Box(string name,Vector3 pos,Vector3 size,Material mat) {var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.position=pos;o.transform.localScale=size;o.GetComponent<Renderer>().sharedMaterial=mat;}
        static void Hill(string name,Vector3 pos,Vector3 size) {var o=GameObject.CreatePrimitive(PrimitiveType.Sphere);o.name=name+" - approximate mass";o.transform.position=pos;o.transform.localScale=size;o.GetComponent<Renderer>().sharedMaterial=land;UnityEngine.Object.DestroyImmediate(o.GetComponent<Collider>());}
        static void Label(string name,Vector3 pos) {var o=new GameObject(name+" label");o.transform.position=pos;o.transform.rotation=Quaternion.Euler(65,0,0);var t=o.AddComponent<TextMesh>();t.text=name;t.fontSize=64;t.characterSize=20;t.anchor=TextAnchor.MiddleCenter;t.color=Color.white;}
        static void Link(string name,bool uncertain,float width,params string[] ids) {var p=new Vector3[ids.Length];for(int i=0;i<ids.Length;i++)p[i]=P(ids[i]);Route(name,width,uncertain,p);}
        static void Route(string name,float width,bool uncertain,Vector3[] p)
        {
            routeRecords.Add(name+" | "+(uncertain?"provisional connection":"documented facility; geometry provisional")+" | design width="+width);
            // Flat strips at shared endpoints. Junction turning geometry remains a later pass.
            var verts=new Vector3[p.Length*2]; var tris=new int[(p.Length-1)*6];
            for(int i=0;i<p.Length;i++) {var tangent=p[Math.Min(i+1,p.Length-1)]-p[Math.Max(i-1,0)];var side=Vector3.Cross(Vector3.up,tangent).normalized*width*.5f;verts[2*i]=p[i]-side;verts[2*i+1]=p[i]+side;
                if(i<p.Length-1){int j=i*6,k=i*2;tris[j]=k;tris[j+1]=k+2;tris[j+2]=k+1;tris[j+3]=k+1;tris[j+4]=k+2;tris[j+5]=k+3;}}
            MeshObject(name,verts,tris,uncertain?provisional:road,roads,true);
        }
        static void Polygon(string name,Vector3[] p,Material mat)
        {
            // Ear clipping handles the concave coast instead of a fan across sea bays.
            var ids=new List<int>();for(int i=0;i<p.Length;i++)ids.Add(i);float area=0;for(int i=0;i<p.Length;i++){var a=p[i];var b=p[(i+1)%p.Length];area+=a.x*b.z-b.x*a.z;}if(area<0)ids.Reverse();
            var ts=new List<int>();int guard=0;
            while(ids.Count>2 && guard++<p.Length*p.Length){bool found=false;for(int i=0;i<ids.Count;i++){int a=ids[(i+ids.Count-1)%ids.Count],b=ids[i],c=ids[(i+1)%ids.Count];if(Cross(p[a],p[b],p[c])<=0)continue;bool inside=false;foreach(int k in ids){if(k==a||k==b||k==c)continue;if(Cross(p[a],p[b],p[k])>=0&&Cross(p[b],p[c],p[k])>=0&&Cross(p[c],p[a],p[k])>=0){inside=true;break;}}if(inside)continue;ts.Add(a);ts.Add(c);ts.Add(b);ids.RemoveAt(i);found=true;break;}if(!found)throw new Exception("Invalid coast polygon: "+name);}
            MeshObject(name,p,ts.ToArray(),mat,null,true);
        }
        static float Cross(Vector3 a,Vector3 b,Vector3 c){return (b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x);}
        static void MeshObject(string name,Vector3[] vertices,int[] triangles,Material mat,Transform parent,bool collision){var mesh=new Mesh();mesh.name=name;mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();string path=Root+"/mesh-"+name.Replace("/","-")+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved==null){AssetDatabase.CreateAsset(mesh,path);}else{EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);mesh=saved;}var o=new GameObject(name);o.transform.parent=parent;o.AddComponent<MeshFilter>().sharedMesh=mesh;o.AddComponent<MeshRenderer>().sharedMaterial=mat;if(collision)o.AddComponent<MeshCollider>().sharedMesh=mesh;}
        static void UrbanMass(string id,Vector3 center){if(id=="Hwangnyeong"||id=="YeongdoUpper"||id=="Gamman")return;int hash=0;foreach(char c in id)hash=hash*31+c;var r=new System.Random(hash);for(int i=0;i<14;i++){float a=(float)r.NextDouble()*Mathf.PI*2,dist=100+r.Next(220),h=20+r.Next(id=="MarineCity"||id=="Haeundae"?200:70);Box(id+" mass "+i,center+new Vector3(Mathf.Cos(a)*dist,h/2,Mathf.Sin(a)*dist),new Vector3(35+r.Next(35),h,35+r.Next(35)),building);}}
        static void Towers(string name,Vector3 a,Vector3 b,float height){foreach(var p in new[]{a,b}){Box(name+" left tower",p+new Vector3(-25,height/2,0),new Vector3(9,height,9),bridge);Box(name+" right tower",p+new Vector3(25,height/2,0),new Vector3(9,height,9),bridge);Box(name+" cross beam",p+Vector3.up*(height*.8f),new Vector3(60,8,9),bridge);}}
    }
}
