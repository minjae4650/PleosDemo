using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Parkside.Jeju.Editor
{
    public static partial class JejuDriveBuilder
    {
        const string GradeFolder=StreetMeshes+"/GradedBase";
        const float GradeWest=3819,GradeEast=4167;
        static StreetReference gradeReference;
        static Vector3[] daechungGrade=Array.Empty<Vector3>();

        static void PrepareStreetGrade()
        {
            gradeReference=ReadStreetReference();
            daechungGrade=gradeReference.streets.Where(s=>s.name=="대청로")
                .SelectMany(s=>s.points).OrderBy(p=>p.x).ToArray();
        }

        static float SmoothWeight(float x)
        {
            x=Mathf.Clamp01(x);
            return x*x*(3-2*x);
        }

        static bool DaechungShelf(Vector3 p,out float proposed,out float delta,out float weight)
        {
            proposed=delta=weight=0;
            if(daechungGrade.Length<2||p.x<GradeWest||p.x>GradeEast||p.z< -4290||p.z> -4035) return false;
            int i=1;
            while(i<daechungGrade.Length-1&&daechungGrade[i].x<p.x) i++;
            var a=daechungGrade[i-1];var b=daechungGrade[i];
            var original=Vector3.Lerp(a,b,Mathf.InverseLerp(a.x,b.x,p.x));
            proposed=Mathf.Lerp(95.05f,102.55f,Mathf.InverseLerp(GradeWest,GradeEast,p.x));
            float edge=SmoothWeight(Mathf.Min(p.x-GradeWest,GradeEast-p.x)/21);
            float side=Mathf.Abs(p.z-original.z);
            weight=edge*SmoothWeight((112-side)/53);
            delta=(proposed-original.y)*weight;
            return weight>0;
        }

        static Vector3 GradedRoad(Vector3 p)
        {
            if(DaechungShelf(p,out _,out float delta,out _)) p.y+=delta;
            return p;
        }

        static float GradedGround(Vector3 p,bool coarse=false)
        {
            if(gradeReference==null) return p.y;
            float height=p.y;
            bool onShelf=DaechungShelf(p,out float proposed,out float delta,out float weight);
            if(onShelf) height=Mathf.Lerp(p.y+delta,proposed-.23f,weight);
            if(TryStreet(p,gradeReference,31,out var street,out float metres,out float distance))
            {
                float margin=Mathf.Sqrt(distance)-StreetWidth(street,metres)*.5f;
                float blend=SmoothWeight(((coarse?25:18)-margin)/11);
                float pavement=StreetPoint(street,metres,out _).y;
                height=Mathf.Lerp(height,Mathf.Min(height,pavement-(coarse?2.35f:.44f)),blend);
            }
            if(onShelf)
            {
                // Side streets slope away from the observed corridor. Leave
                // room beneath their asphalt on both the detailed and DEM meshes.
                var closest=ClosestRoad(p.x,p.z,1);
                if(closest.road!=null)
                {
                    float margin=closest.d-closest.road.w[closest.i]*.5f;
                    float blend=SmoothWeight(((coarse?31:13)-margin)/(coarse?12:6));
                    float pavement=GradedRoad(closest.centre).y;
                    height=Mathf.Lerp(height,Mathf.Min(height,pavement-(coarse?2.65f:.42f)),blend);
                }
            }
            return height;
        }

        static bool GradingBounds(Bounds bounds)
        {
            if(gradeReference==null) return false;
            foreach(var street in gradeReference.streets)
                if(bounds.max.x>=street.bounds.min.x-115 && bounds.min.x<=street.bounds.max.x+115 &&
                   bounds.max.z>=street.bounds.min.z-115 && bounds.min.z<=street.bounds.max.z+115) return true;
            return false;
        }

        static void WarpStreetBase(Transform sceneRoot)
        {
            if(gradeReference.streets.Length==0) return;
            Directory.CreateDirectory(GradeFolder);
            foreach(string name in new[]{"Connected_Coastal_And_Village_Roads","Actual_OSM_Junction_Connections","Real_DEM_Hallasan_Backdrop"})
            {
                var parent=sceneRoot.Find(name);
                if(parent==null) continue;
                foreach(var filter in parent.GetComponentsInChildren<MeshFilter>(true))
                    if(GradingBounds(filter.GetComponent<Renderer>().bounds))
                        GradeMesh(filter,filter.name=="Dense_Roadside_Ground"||filter.name.StartsWith("DEM_",StringComparison.Ordinal));
            }
        }

        static void GradeMesh(MeshFilter filter,bool groundMesh)
        {
            var original=filter.sharedMesh;
            var path=AssetDatabase.GetAssetPath(original);
            if(path.StartsWith(GradeFolder+"/",StringComparison.Ordinal))
                path=AssetDatabase.GUIDToAssetPath(Path.GetFileNameWithoutExtension(path));
            if(string.IsNullOrEmpty(path)) return;
            original=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(original==null) return;
            bool coarse=filter.name.StartsWith("DEM_",StringComparison.Ordinal);
            var vertices=original.vertices;
            bool changed=false;
            for(int i=0;i<vertices.Length;i++)
            {
                var p=filter.transform.TransformPoint(vertices[i]);
                float y=groundMesh?GradedGround(p,coarse):GradedRoad(p).y;
                if(Mathf.Abs(y-p.y)<.003f) continue;
                p.y=y;
                vertices[i]=filter.transform.InverseTransformPoint(p);
                changed=true;
            }
            var collider=filter.GetComponent<MeshCollider>();
            if(!changed)
            {
                filter.sharedMesh=original;
                if(collider!=null) collider.sharedMesh=original;
                return;
            }
            string destination=GradeFolder+"/"+AssetDatabase.AssetPathToGUID(path)+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(destination);
            if(mesh==null)
            {
                mesh=Object.Instantiate(original);
                mesh.name=Path.GetFileNameWithoutExtension(destination);
                AssetDatabase.CreateAsset(mesh,destination);
            }
            mesh.vertices=vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            EditorUtility.SetDirty(mesh);
            filter.sharedMesh=mesh;
            if(collider!=null)
            {
                collider.sharedMesh=null;
                collider.sharedMesh=mesh;
            }
        }

        static void AssignGradedTraffic(JejuDriveController car)
        {
            if(car.ambient==null||gradeReference==null||gradeReference.streets.Length==0) return;
            var network=JsonUtility.FromJson<JejuTrafficNetwork>(File.ReadAllText(Folder+"/Data/JejuTrafficNetwork.json"));
            foreach(var node in network.nodes) node.point=GradedRoad(node.point);
            foreach(var e in network.edges)
            {
                e.points=e.points.Select(GradedRoad).ToArray();
                e.distances=JejuTrafficNetwork.Distances(e.points);
            }
            string path=StreetMeshes+"/StreetTraffic.json";
            File.WriteAllText(path,JsonUtility.ToJson(network));
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            car.ambient.networkSource=Load<TextAsset>(path);
            car.mapCredit="© OpenStreetMap contributors · Overture Maps Foundation";
        }
    }
}
