using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BusanEnvironment.Editor
{
    public static class BusanTerrainBuilder
    {
        const string Root="Assets/BusanEnvironment/TerrainReview";
        public static void RebuildReferences(){BusanGeographyBuilder.Build();Build();}
        [Serializable] public class Data {public int columns,rows;public Vector3[] vertices;public Guide[] guides;}
        [Serializable] public class Guide {public string id;public bool coast;public float displayWidth;public Vector3[] points;}
        [MenuItem("Busan/Build terrain context review")]
        public static void Build()
        {
            var d=JsonUtility.FromJson<Data>(File.ReadAllText("Assets/BusanEnvironment/Editor/Data/BusanTerrain.json"));
            if(d.vertices.Length!=d.columns*d.rows)throw new Exception("Terrain grid mismatch");
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Directory.CreateDirectory(Root);
            var triangles=new List<int>();
            for(int r=0;r<d.rows-1;r++)for(int c=0;c<d.columns-1;c++){int a=r*d.columns+c,b=a+d.columns;triangles.Add(a);triangles.Add(b);triangles.Add(a+1);triangles.Add(a+1);triangles.Add(b);triangles.Add(b+1);}
            MeshObject("Regional DEM - not road surface",d.vertices,triangles.ToArray(),Mat("Terrain",new Color(.35f,.44f,.32f),false));
            var sea=GameObject.CreatePrimitive(PrimitiveType.Cube);sea.name="Sea display - 0.1m offset avoids coplanar artifacts";sea.transform.position=new Vector3(0,-4.9f,0);sea.transform.localScale=new Vector3(40000,10,40000);sea.GetComponent<Renderer>().sharedMaterial=Mat("Sea",new Color(.06f,.22f,.30f),true);UnityEngine.Object.DestroyImmediate(sea.GetComponent<Collider>());
            Guides(d,false,Mat("RoadGuides",new Color(.85f,.82f,.65f),true));Guides(d,true,Mat("CoastGuides",new Color(.3f,.75f,.85f),true));
            var bridgeFile="Assets/BusanEnvironment/Editor/Data/BusanBridgeDraft.json";
            bool hasDraft=File.Exists(bridgeFile);
            if(hasDraft)DraftBridges(JsonUtility.FromJson<Data>(File.ReadAllText(bridgeFile)));
            var sun=new GameObject("Terrain light").AddComponent<Light>();sun.type=LightType.Directional;sun.transform.rotation=Quaternion.Euler(32,-40,0);sun.intensity=1.25f;RenderSettings.ambientLight=new Color(.55f,.57f,.6f);
            var cam=new GameObject("Terrain review camera").AddComponent<Camera>();cam.transform.position=new Vector3(3000,14000,-17000);cam.transform.LookAt(new Vector3(0,0,500));cam.orthographic=true;cam.orthographicSize=11500;cam.farClipPlane=60000;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.06f,.17f,.23f);
            Label("BUSAN / DEM terrain context",new Vector3(-10500,800,10000),cam,95);
            Label(hasDraft?"Orange bridges: assumed heights / Ground DEM 1x / Tunnels omitted":"Ground guides only / Bridges and tunnels omitted / Heights 1x",new Vector3(-10500,800,8700),cam,42);
            Label("Mapzen Terrain Tiles (USGS / NOAA) + OpenStreetMap contributors (ODbL)",new Vector3(-10500,500,-11000),cam,35);
            EditorSceneManager.SaveScene(scene,Root+"/BusanTerrainContext.unity");AssetDatabase.SaveAssets();
            var rt=new RenderTexture(2200,1800,24);cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
            var image=new Texture2D(2200,1800,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,2200,1800),0,0);image.Apply();
            File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../../docs/concepts/busan-terrain-context.png")),image.EncodeToPNG());
            if(hasDraft){
                cam.transform.position=new Vector3(600,650,-2800);cam.transform.LookAt(new Vector3(100,35,-2050));cam.orthographicSize=500;cam.Render();
                image.ReadPixels(new Rect(0,0,2200,1800),0,0);image.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../../docs/concepts/busan-yeongdo-ramp-draft.png")),image.EncodeToPNG());
            }
            cam.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);
            Debug.Log("BUSAN_TERRAIN_OK vertices="+d.vertices.Length+"; groundGuides="+d.guides.Length);
        }
        static Material Mat(string name,Color color,bool unlit){string path=Root+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null){m=new Material(Shader.Find(unlit?"Unlit/Color":"Standard"));AssetDatabase.CreateAsset(m,path);}m.color=color;if(!unlit)m.SetFloat("_Glossiness",0);return m;}
        static void MeshObject(string name,Vector3[] vertices,int[] triangles,Material mat){var mesh=new Mesh{indexFormat=IndexFormat.UInt32,name=name};mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();string path=Root+"/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old==null)AssetDatabase.CreateAsset(mesh,path);else{EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);mesh=old;}var go=new GameObject(name);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;}
        static void Guides(Data d,bool coast,Material mat){var v=new List<Vector3>();var t=new List<int>();foreach(var g in d.guides)if(g.coast==coast)for(int i=1;i<g.points.Length;i++){var a=g.points[i-1];var b=g.points[i];var side=Vector3.Cross(Vector3.up,b-a).normalized*(coast?7:5);int n=v.Count;v.Add(a-side);v.Add(b-side);v.Add(a+side);v.Add(b+side);t.Add(n);t.Add(n+1);t.Add(n+2);t.Add(n+2);t.Add(n+1);t.Add(n+3);}MeshObject(coast?"Coast reference":"Surface road guides - no colliders",v.ToArray(),t.ToArray(),mat);}
        static void DraftBridges(Data d){var v=new List<Vector3>();var t=new List<int>();foreach(var g in d.guides)for(int i=1;i<g.points.Length;i++){var a=g.points[i-1];var b=g.points[i];var side=Vector3.Cross(Vector3.up,b-a).normalized*g.displayWidth*.5f;int n=v.Count;v.Add(a-side);v.Add(b-side);v.Add(a+side);v.Add(b+side);t.Add(n);t.Add(n+1);t.Add(n+2);t.Add(n+2);t.Add(n+1);t.Add(n+3);}MeshObject("Bridge height DRAFT - unverified no colliders",v.ToArray(),t.ToArray(),Mat("BridgeDraft",new Color(1,.48f,.12f),true));}
        static void Label(string text,Vector3 pos,Camera camera,float size){var o=new GameObject(text);o.transform.position=pos;o.transform.rotation=camera.transform.rotation;var t=o.AddComponent<TextMesh>();t.text=text;t.fontSize=64;t.characterSize=size;t.color=Color.white;}
    }
}
