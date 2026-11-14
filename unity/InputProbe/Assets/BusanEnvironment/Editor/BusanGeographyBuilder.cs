using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BusanEnvironment.Editor
{
    public static class BusanGeographyBuilder
    {
        const string Output="Assets/BusanEnvironment/GeographyReview";
        [Serializable] public class Dataset {public double originLongitude,originLatitude;public string projection,attribution,limitations;public Feature[] features;}
        [Serializable] public class Feature {public string osmId,name,category,layerTag,onewayTag,lanesTag;public Vector3[] points;}

        [MenuItem("Busan/Build actual-coordinate reference scene")]
        public static void Build()
        {
            var data=JsonUtility.FromJson<Dataset>(File.ReadAllText("Assets/BusanEnvironment/Editor/Data/BusanGeography.json"));
            if(data.features==null||data.features.Length==0)throw new Exception("Missing geography input");
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Directory.CreateDirectory(Output);
            var root=new GameObject("Actual-coordinate reference - FLAT, NOT DRIVABLE");
            var ids=new HashSet<string>();
            foreach(var f in data.features){if(!ids.Add(f.osmId))throw new Exception("Duplicate OSM ID "+f.osmId);if(f.points.Length<2)throw new Exception("Short geometry "+f.osmId);}
            // All heights are zero. Groups are flat visual reference marks, not road surfaces.
            BuildGroup(data,"road",new Color(.7f,.78f,.82f),14,root.transform);
            BuildGroup(data,"bridge",new Color(1,.74f,.3f),22,root.transform);
            BuildGroup(data,"tunnel",new Color(.67f,.5f,1),20,root.transform);
            BuildGroup(data,"coast",new Color(.3f,.8f,.85f),18,root.transform);
            Label("BUSAN / Actual-coordinate reference",new Vector3(-9000,5,9000),100);
            Label("FLAT PLAN / Heights and lane connections not verified",new Vector3(-9000,5,8550),55);
            Label("OpenStreetMap contributors / ODbL 1.0 / openstreetmap.org/copyright",new Vector3(-9000,5,-10500),45);
            string[] names={"해운대해변로","광안대로","중앙대로","황령산로","충무대로","다대로","남항대교"};
            string[] labels={"Haeundae","Gwangan bridge corridor","Central arterial","Hwangnyeong access","Songdo arterial","Dadaepo arterial","Namhang Bridge"};
            for(int n=0;n<names.Length;n++){Feature best=null;foreach(var f in data.features)if(f.name==names[n]&&(best==null||f.points.Length>best.points.Length))best=f;if(best!=null)Label(labels[n],best.points[best.points.Length/2]+new Vector3(80,6,80),45);}
            var camera=new GameObject("Geographic top view").AddComponent<Camera>();
            camera.transform.position=new Vector3(0,24000,0);camera.transform.rotation=Quaternion.Euler(90,0,0);
            camera.orthographic=true;camera.orthographicSize=11500;camera.farClipPlane=50000;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.12f,.16f);
            EditorSceneManager.SaveScene(scene,Output+"/BusanActualCoordinateReview.unity");AssetDatabase.SaveAssets();
            var rt=new RenderTexture(2200,2000,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            var image=new Texture2D(2200,2000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,2200,2000),0,0);image.Apply();
            string repo=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
            File.WriteAllBytes(Path.Combine(repo,"docs/concepts/busan-unity-coordinate-review.png"),image.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);
            Debug.Log("BUSAN_GEOGRAPHY_OK ways="+ids.Count+"; heights=unverified; scene="+Output+"/BusanActualCoordinateReview.unity");
        }
        static void BuildGroup(Dataset data,string category,Color color,float displayWidth,Transform parent)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            foreach(var f in data.features)if(f.category==category)for(int i=1;i<f.points.Length;i++){
                var a=f.points[i-1];var b=f.points[i];if((b-a).sqrMagnitude<.001f)continue;
                var side=Vector3.Cross(Vector3.up,b-a).normalized*displayWidth*.5f;int start=vertices.Count;
                vertices.Add(a-side);vertices.Add(b-side);vertices.Add(a+side);vertices.Add(b+side);
                triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);triangles.Add(start+2);triangles.Add(start+1);triangles.Add(start+3);
            }
            var mesh=new Mesh{indexFormat=IndexFormat.UInt32,name=category+" reference"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();
            string mp=Output+"/"+category+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(mp);
            if(existing==null)AssetDatabase.CreateAsset(mesh,mp);else{EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}
            string matPath=Output+"/"+category+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if(mat==null){mat=new Material(Shader.Find("Unlit/Color"));AssetDatabase.CreateAsset(mat,matPath);}mat.color=color;
            var go=new GameObject(category+" / display strokes only");go.transform.parent=parent;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;
        }
        static void Label(string text,Vector3 position,float size){var o=new GameObject(text);o.transform.position=position;o.transform.rotation=Quaternion.Euler(90,0,0);var t=o.AddComponent<TextMesh>();t.text=text;t.fontSize=64;t.characterSize=size;t.color=Color.white;t.anchor=TextAnchor.MiddleLeft;}
    }
}
