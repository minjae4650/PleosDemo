using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Parkside.Jeju.Editor
{
    /// <summary>Explicit authoring only; all writes stay under JejuDrive. No shared assets are modified.</summary>
    public static partial class JejuDriveBuilder
    {
        public const string Folder = "Assets/_Parkside/JejuDrive";
        public const string ScenePath = "Assets/_Parkside/Scenes/JejuCoastalDrive.unity";
        public static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build/jeju-drive"));
        const string Environment = "Assets/ParkingLotEnvironment";
        [Serializable] public class Row { public float[] values; }
        [Serializable] public class BranchSource { public string name;public Vector3[] points;public float[] widths;public bool[] oneWays;public Row[] corridorHeights; }
        [Serializable] public class ArmSource {public Vector3 outward;public float width;public bool incoming,oneWay;public int lanes;}
        [Serializable] public class SignalSource { public string name;public int index;public float distance;public long osmNodeId;public string placement;public Vector3 centre;public ArmSource[] arms; }
        [Serializable] public class BuildingSource { public long osmId;public Vector3[] points;public float height;public int levels;public string name,kind,landmark;public bool urban; }
        [Serializable] public class CoastSource { public Vector3[] points; }
        [Serializable] public class Source
        {
            public Vector3[] points;
            public float[] distances, widths, corridorOffsets;
            public string[] roadNames;
            public bool[] oneWays;
            public JejuStation[] stations;
            public Vector2 terrainMin, terrainSize;
            public int terrainResolution;
            public Row[] corridorHeights;
            public BranchSource[] branches;
            public SignalSource[] signals;
            public BuildingSource[] buildings;
            public CoastSource[] coastlines;
        }
        static Source data;
        static float[,] terrain;
        static Scene scene;
        static Transform root;
        static Material asphalt, white, yellow, metal, basalt, ground, ocean, concrete;
        static Font font;
        static readonly List<Transform> chunks = new List<Transform>();
        static System.Random random;
        static int meshNumber;
        static bool refiningDraft;

        [MenuItem("PARKSIDE/Jeju/Open coastal drive")]
        public static void Open() { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath); }

        [MenuItem("PARKSIDE/Jeju/Create initial coastal drive")]
        public static void CreateMenu()
        {
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Scene already exists. Open it for editing; initial authoring never overwrites it.");
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Build();
        }

        public static void BuildAndVerify()
        {
            try
            {
                Directory.CreateDirectory(Output);
                Build();
                Verify();
                JejuDriveVerification.BeginPlayCheck();
            }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText(Path.Combine(Output, "error.txt"), e.ToString()); EditorApplication.Exit(1); }
        }

        // Explicit in-place refinement of this generator's draft. Existing asset GUIDs are retained.
        public static void RefineDraftAndVerify()
        {
            try
            {
                var existing=EditorSceneManager.OpenScene(ScenePath);
                if(!existing.GetRootGameObjects().Any(g=>g.name=="Jeju_CoastalDrive") ||
                   Object.FindObjectOfType<JejuDriveRoute>()==null)
                    throw new InvalidOperationException("The saved scene is not the Jeju generator's draft.");
                refiningDraft=true;
                Build();ValidateCoastalLandmarks();Verify();CaptureUrbanViews();JejuDriveVerification.BeginPlayCheck();
            }
            catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"error.txt"),e.ToString());EditorApplication.Exit(1);}
            finally{refiningDraft=false;}
        }

        public static void VerifySavedAndPlay()
        {
            try
            {
                scene=EditorSceneManager.OpenScene(ScenePath);
                var car=Object.FindObjectOfType<JejuDriveController>();
                foreach(var t in car.visual.GetComponentsInChildren<Transform>())t.gameObject.isStatic=false;
                EditorSceneManager.SaveScene(scene);
                Verify();
                JejuDriveVerification.BeginPlayCheck();
            }
            catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"error.txt"),e.ToString());EditorApplication.Exit(1);}
        }

        public static void RefreshStationViewsAndVerify()
        {
            try
            {
                scene=EditorSceneManager.OpenScene(ScenePath);
                var source=JsonUtility.FromJson<Source>(File.ReadAllText(Folder+"/Data/JejuRoadData.json"));
                var route=Object.FindObjectOfType<JejuDriveRoute>();
                route.stations=source.stations.Concat(route.stations.Where(s=>s.branchSpawn)).ToArray();
                Object.FindObjectOfType<JejuDriveController>().JumpToStation(0);
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
                Verify();JejuDriveVerification.BeginPlayCheck();
            }
            catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"error.txt"),e.ToString());EditorApplication.Exit(1);}
        }

        public static void InspectSaved()
        {
            try
            {
                scene=EditorSceneManager.OpenScene(ScenePath);
                var controller=Object.FindObjectOfType<JejuDriveController>();
                var stats=new List<string>();
                foreach(var r in Object.FindObjectsOfType<Renderer>().OrderByDescending(r=>r.bounds.size.sqrMagnitude).Take(85))
                    stats.Add(r.name+" parent="+r.transform.parent.name+" bounds="+r.bounds+" active="+r.gameObject.activeInHierarchy);
                foreach(string key in new[]{"tree-small-real","tree-small-far"})
                {
                    var p=Load<GameObject>(Environment+"/RealVehicles/"+key+"/"+key+".prefab");
                    stats.Add(key+": "+string.Join(",",p.GetComponentsInChildren<MeshFilter>().Select(f=>f.name+"="+f.sharedMesh.triangles.Length/3)));
                }
                File.WriteAllLines(Path.Combine(Output,"inspection.txt"),stats);
                controller.view.cullingMask=0;Capture(controller.view,Path.Combine(Output,"sky-only.png"));
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }

        public static void Build()
        {
            if (File.Exists(ScenePath) && !refiningDraft) throw new InvalidOperationException("Refusing to overwrite authored Jeju Scene: " + ScenePath);
            Directory.CreateDirectory(Folder + "/Generated/Meshes");
            Directory.CreateDirectory(Folder + "/Generated/Materials");
            AssetDatabase.Refresh();
            data = JsonUtility.FromJson<Source>(File.ReadAllText(Folder + "/Data/JejuRoadData.json"));
            byte[] bytes = File.ReadAllBytes(Folder + "/Data/TerrainHeights.bytes");
            terrain = new float[data.terrainResolution, data.terrainResolution];
            Buffer.BlockCopy(bytes, 0, terrain, 0, bytes.Length);
            random = new System.Random(11002026); meshNumber = 0; chunks.Clear();
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = Group("Jeju_CoastalDrive");
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Parkside/UI/Fonts/AstaSans-Medium.ttf");
            PrepareMaterials();
            PrepareStreetGrade();
            PrepareJejuDetails();
            PrepareTrafficAssets();
            AssetDatabase.StartAssetEditing();
            try
            {
                MakeTerrain();MakeRoad();MakeBranchRoads();MakeJunctionAprons();WarpStreetBase(root);BuildStreetReference(root);MakeJejuScenery();MakeSignalJunctions();MakeLighting();MakeVehicle();BuildSeogwipoStreetFinish(root,Object.FindObjectOfType<JejuDriveController>());
            }
            finally{AssetDatabase.StopAssetEditing();}
            AssignGradedTraffic(Object.FindObjectOfType<JejuDriveController>());
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save Jeju Scene");
            ApplyCoastalLandmarks(false);
            scene = EditorSceneManager.OpenScene(ScenePath);
            File.WriteAllText(Path.Combine(Output, "build-complete.txt"), "Scene=" + ScenePath + "\nRouteMetres=" + data.distances.Last() + "\nSceneryChunks=" + chunks.Count);
            Debug.Log("Jeju drive scene authored: " + ScenePath);
        }

        static void PrepareMaterials()
        {
            asphalt = Material("Asphalt", new Color(.61f,.63f,.63f));
            asphalt.mainTexture = Load<Texture2D>(Environment + "/RealismAssets/clean_asphalt_diff_2k.jpg");
            asphalt.SetTexture("_BumpMap", Load<Texture2D>(Environment + "/RealismAssets/clean_asphalt_nor_gl_2k.jpg"));
            asphalt.EnableKeyword("_NORMALMAP"); asphalt.SetFloat("_BumpScale",.25f);
            white = Material("RoadWhite", new Color(.85f,.84f,.75f));
            yellow = Material("CentreYellow", new Color(.92f,.61f,.12f));
            metal = Material("Guardrail", new Color(.48f,.53f,.53f), .44f, .6f);
            basalt = Material("JejuBasalt", new Color(.18f,.20f,.19f));
            basalt.mainTexture = Load<Texture2D>(Environment + "/KoreanAssets/aerial_grass_rock_diff_2k.jpg");
            concrete = Material("Concrete", new Color(.59f,.59f,.53f));
            ground = Material("Landscape", Color.white,0,0,"PARKSIDE/Jeju/Landscape");
            ground.mainTexture = Load<Texture2D>(Environment + "/KoreanAssets/aerial_grass_rock_diff_2k.jpg");
            ground.SetTexture("_RockTex", asphalt.mainTexture);
            ground.SetTexture("_CanopyTex",Load<Texture2D>(Environment+"/KoreanAssets/forest_canopy.png"));
            ground.SetTexture("_SoilTex",Load<Texture2D>(Environment+"/RealismAssets/brown_mud_leaves_01_diff_1k.jpg"));
            ocean = Material("Ocean", new Color(.035f,.26f,.33f),0,0,"PARKSIDE/Jeju/Ocean");
            foreach (var m in new[] { asphalt,white,yellow,metal,basalt,concrete,ground,ocean }) EditorUtility.SetDirty(m);
        }

        static Material Material(string name, Color colour, float smooth=0, float metallic=0, string shader="Standard")
        {
            var m = new Material(Shader.Find(shader)) { name = name, enableInstancing = true };
            if (m.HasProperty("_Color")) m.color = colour;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness",smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic",metallic);
            string path=Folder + "/Generated/Materials/" + name + ".mat";
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(existing!=null){EditorUtility.CopySerialized(m,existing);Object.DestroyImmediate(m);m=existing;EditorUtility.SetDirty(m);}
            else AssetDatabase.CreateAsset(m,path);
            return m;
        }
        static T Load<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException("Missing source asset: " + path);
            return asset;
        }

        static Transform Group(string name, Transform parent=null, Vector3 position=default)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = position; return go.transform;
        }
        static GameObject MeshObject(string name, List<Vector3> vertices, List<int> indices, List<Vector2> uv,
            Material material, Transform parent, List<Color> colours=null, bool collider=false, string assetPath=null)
        {
            if(name=="Dense_Roadside_Ground"&&uv!=null&&colours!=null)ClipRoadBed(ref vertices,ref indices,ref uv,ref colours,parent);
            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65534 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetTriangles(indices,0);
            if (uv != null) mesh.SetUVs(0,uv);
            if (colours != null) mesh.SetColors(colours);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            string path=assetPath ?? Folder + "/Generated/Meshes/" + (meshNumber++).ToString("D4") + "_" + name + ".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing!=null)
            {
                existing.Clear();existing.indexFormat=mesh.indexFormat;existing.SetVertices(vertices);existing.SetTriangles(indices,0);
                if(uv!=null)existing.SetUVs(0,uv);if(colours!=null)existing.SetColors(colours);
                existing.RecalculateNormals();existing.RecalculateBounds();existing.UploadMeshData(false);
                Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh);
            }
            else AssetDatabase.CreateAsset(mesh,path);
            var obj = new GameObject(name); obj.transform.SetParent(parent,false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            if (collider && indices.Count>0) obj.AddComponent<MeshCollider>().sharedMesh = mesh;
            return obj;
        }
        static void Quad(List<int> tris, int a, int b, int c, int d)
        {
            tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d);
        }
        static Vector3 Right(int i)
        {
            Vector3 delta = data.points[Mathf.Min(i+1,data.points.Length-1)] - data.points[Mathf.Max(0,i-1)];
            return Vector3.Cross(Vector3.up, delta).normalized;
        }
        static Color Biome(Vector3 p)
        {
            float noise = Mathf.PerlinNoise(p.x * .0009f+20,p.z * .0009f+10);
            Color low = Color.Lerp(new Color(.27f,.32f,.16f),new Color(.42f,.43f,.23f),noise);
            Color forest = Color.Lerp(new Color(.16f,.23f,.13f),new Color(.27f,.31f,.16f),noise);
            Color upland = Color.Lerp(new Color(.36f,.36f,.21f),new Color(.44f,.43f,.28f),noise);
            Color result=Color.Lerp(Color.Lerp(low,forest,Mathf.InverseLerp(120,350,p.y)),upland,Mathf.InverseLerp(1250,1750,p.y));
            result.a=Mathf.InverseLerp(100,260,p.y)*(1-Mathf.InverseLerp(1400,1750,p.y));
            return result;
        }
        static float Height(float x,float z)
        {
            float fx = Mathf.Clamp01((x-data.terrainMin.x)/data.terrainSize.x)*(data.terrainResolution-1);
            float fz = Mathf.Clamp01((z-data.terrainMin.y)/data.terrainSize.y)*(data.terrainResolution-1);
            int ix = Mathf.Min((int)fx,data.terrainResolution-2), iz = Mathf.Min((int)fz,data.terrainResolution-2);
            return Mathf.Lerp(Mathf.Lerp(terrain[iz,ix],terrain[iz,ix+1],fx-ix),Mathf.Lerp(terrain[iz+1,ix],terrain[iz+1,ix+1],fx-ix),fz-iz);
        }

        static void MakeTerrain()
        {
            var parent = Group("Real_DEM_Hallasan_Backdrop",root);
            // 128-sample patches keep each mesh below 16-bit index limits.
            int size = data.terrainResolution-1, step=128;
            for (int sz=0;sz<size;sz+=step) for (int sx=0;sx<size;sx+=step)
            {
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>(); var t = new List<int>();
                for (int z=0;z<=step;z++) for (int x=0;x<=step;x++)
                {
                    var p = new Vector3(data.terrainMin.x+(sx+x)*data.terrainSize.x/size,terrain[sz+z,sx+x],data.terrainMin.y+(sz+z)*data.terrainSize.y/size);
                    v.Add(p); uv.Add(new Vector2(p.x*.04f,p.z*.04f)); c.Add(Biome(p));
                    if (x<step && z<step) { int a=z*(step+1)+x; Quad(t,a,a+1,a+step+1,a+step+2); }
                }
                MeshObject("DEM_"+sx+"_"+sz,v,t,uv,ground,parent,c);
            }
            var sea = Group("Sea",root);
            MeshObject("South_Jeju_Ocean",new List<Vector3>{new Vector3(-35000,-.25f,-35000),new Vector3(35000,-.25f,-35000),new Vector3(-35000,-.25f,35000),new Vector3(35000,-.25f,35000)},
                new List<int>{0,2,1,1,2,3},null,ocean,sea);
        }

        static void MakeRoad()
        {
            var parent = Group("Actual_OSM_Expanded_Roads",root);
            for (int start=0;start<data.points.Length-1;start+=50)
            {
                int end = Mathf.Min(start+50,data.points.Length-1);
                var chunk = Group("Road_"+(start*5).ToString("D5")+"m",parent);
                Strip("Asphalt",start,end,i=>-data.widths[i]*.5f,i=>data.widths[i]*.5f,0,asphalt,chunk,true);
                Strip("White_left",start,end,i=>-data.widths[i]*.5f+.20f,i=>-data.widths[i]*.5f+.34f,.018f,white,chunk);
                Strip("White_right",start,end,i=>data.widths[i]*.5f-.34f,i=>data.widths[i]*.5f-.20f,.018f,white,chunk);
                Strip("Yellow_left",start,end,i=>-.18f,i=>-.06f,.022f,yellow,chunk);
                Strip("Yellow_right",start,end,i=>.06f,i=>.18f,.022f,yellow,chunk);
                Strip("Dashed_oneway",start,end,i=>-.07f,i=>.07f,.025f,white,chunk);
                if(data.widths[(start+end)/2]>11)
                {
                    Strip("Dashed_left",start,end,i=>-data.widths[i]*.25f-.07f,i=>-data.widths[i]*.25f+.07f,.028f,white,chunk);
                    Strip("Dashed_right",start,end,i=>data.widths[i]*.25f-.07f,i=>data.widths[i]*.25f+.07f,.028f,white,chunk);
                }
                var v=new List<Vector3>(); var uv=new List<Vector2>(); var colours=new List<Color>(); var t=new List<int>();
                int columns=data.corridorOffsets.Length;
                for (int i=start;i<=end;i++)
                {
                    Vector3 right=Right(i);
                    for(int j=0;j<columns;j++)
                    {
                        Vector3 p=data.points[i]+right*data.corridorOffsets[j]; p.y=data.corridorHeights[j].values[i];
                        v.Add(p);uv.Add(new Vector2(p.x*.06f,p.z*.06f));colours.Add(Biome(p));
                        if(i<end && j<columns-1){int a=(i-start)*columns+j;Quad(t,a,a+1,a+columns,a+columns+1);}
                    }
                }
                MeshObject("Dense_Roadside_Ground",v,t,uv,ground,chunk,colours);
            }
        }
        static void Strip(string name,int start,int end,Func<int,float> left,Func<int,float> right,float lift,Material m,Transform parent,bool collider=false)
        {
            var v=new List<Vector3>(); var uv=new List<Vector2>(); var t=new List<int>();
            int columns=collider?5:2;
            for(int i=start;i<=end;i++)
            {
                Vector3 r=Right(i), p=data.points[i]+Vector3.up*lift;
                float[] offsets=collider?new[]{left(i),-.25f,0,.25f,right(i)}:new[]{left(i),right(i)};
                for(int j=0;j<columns;j++)
                {
                    v.Add(p+r*offsets[j]);uv.Add(new Vector2(offsets[j]/4,data.distances[i]/4));
                    bool marking=!collider;
                    bool visible=(!marking || !JunctionGap(data.points[i]))&&!DuplicateBranchSegment(i,marking);
                    bool oneWay=data.oneWays!=null&&i<data.oneWays.Length&&data.oneWays[i];
                    if(name.StartsWith("Yellow"))visible&=!oneWay;
                    if(name=="Dashed_oneway")visible&=oneWay&&data.widths[i]>7.3f;
                    if(name.StartsWith("Dashed"))visible&=Mathf.Repeat(data.distances[i],15)<5;
                    if(i<end && j<columns-1 && visible){int a=(i-start)*columns+j;Quad(t,a,a+1,a+columns,a+columns+1);}
                }
            }
            MeshObject(name,v,t,uv,m,parent,null,collider);
        }

        static float Range(float lo,float hi) => Mathf.Lerp(lo,hi,(float)random.NextDouble());
        static Bounds Bounds(GameObject obj)
        {
            var rs=obj.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;
            foreach(var r in rs)b.Encapsulate(r.bounds);return b;
        }

        static void Label(string text,Vector3 p,float size,Transform parent,Quaternion rotation)
        {
            var go=new GameObject(text);go.transform.SetParent(parent,false);go.transform.SetPositionAndRotation(p,rotation);
            var label=go.AddComponent<TextMesh>();label.text=text;label.font=font;label.fontSize=64;label.characterSize=size;label.anchor=TextAnchor.MiddleCenter;label.color=new Color(.9f,.91f,.85f);
            go.GetComponent<MeshRenderer>().sharedMaterial=font.material;
        }
        static void MakeLighting()
        {
            var parent=Group("Lighting",root);
            var sun=Group("Jeju_Afternoon_Sun",parent).gameObject.AddComponent<Light>();sun.type=LightType.Directional;
            sun.transform.rotation=Quaternion.Euler(39,-38,0);sun.color=new Color(1,.94f,.83f);sun.intensity=1.15f;
            sun.shadows=LightShadows.Soft;sun.shadowBias=.04f;sun.shadowNormalBias=.15f;
            RenderSettings.sun=sun;RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.48f,.62f,.70f);RenderSettings.ambientEquatorColor=new Color(.35f,.40f,.38f);RenderSettings.ambientGroundColor=new Color(.17f,.20f,.16f);
            RenderSettings.skybox=Load<Material>(Environment+"/Materials/Parking_sky.mat");
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=3500;RenderSettings.fogEndDistance=24000;
            RenderSettings.fogColor=new Color(.63f,.74f,.78f);
            RenderSettings.defaultReflectionMode=DefaultReflectionMode.Skybox;RenderSettings.reflectionIntensity=.65f;
            Lightmapping.giWorkflowMode=Lightmapping.GIWorkflowMode.OnDemand;
        }
        static void MakeVehicle()
        {
            var systems=Group("Drive_Runtime",root);
            var route=systems.gameObject.AddComponent<JejuDriveRoute>();route.points=data.points;route.distances=data.distances;route.widths=data.widths;route.stations=data.stations;route.font=font;
            route.roadNames=data.roadNames;
            route.branches=data.branches?.Select(b=>new JejuRoadBranch{name=b.name,points=b.points,widths=b.widths}).ToArray();
            route.map=Load<Texture2D>(Folder+"/Data/RouteMap.png");route.mapMin=data.terrainMin;route.mapMax=data.terrainMin+data.terrainSize;
            var player=Group("Player_Tucson",systems).gameObject;
            var model=(GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(Environment+"/RealVehicles/hyundai-tucson/hyundai-tucson.prefab"),scene);
            model.name="Existing_Tucson_Model";model.transform.SetParent(player.transform,false);
            model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.Euler(0,180,0);
            var b=Bounds(model);model.transform.localPosition-=new Vector3(b.center.x,b.min.y,b.center.z);
            foreach(var t in model.GetComponentsInChildren<Transform>())t.gameObject.isStatic=false;
            foreach(var c in model.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
            var controller=player.AddComponent<JejuDriveController>();controller.route=route;controller.visual=model.transform;controller.sceneryChunks=chunks.ToArray();
            controller.traffic=Object.FindObjectOfType<JejuTrafficSystem>();
            MakeAmbientTraffic(controller,systems);
            var camera=Group("Jeju_Drive_Camera",systems).gameObject.AddComponent<Camera>();camera.tag="MainCamera";camera.nearClipPlane=.15f;camera.farClipPlane=28000;
            camera.allowHDR=true;camera.allowMSAA=true;camera.clearFlags=CameraClearFlags.Skybox;
            camera.gameObject.AddComponent<AudioListener>();
            var aa=camera.gameObject.AddComponent<ParkingLotEnvironment.CloudPilot.MapCameraFXAA>();
            aa.edgeShader=Load<Shader>(Environment+"/CloudPilot/Canopy/MapCameraFXAA.shader");
            controller.view=camera;ApplyStreetWidths(route,ReadStreetReference());controller.JumpToStation(0);
        }

        public static void Verify()
        {
            Directory.CreateDirectory(Output);
            var route=Object.FindObjectOfType<JejuDriveRoute>();
            var controller=Object.FindObjectOfType<JejuDriveController>();
            if(route==null || controller==null || route.points.Length<1000 || route.Length<15000)throw new InvalidOperationException("Incomplete route setup");
            int missing=0,shaderErrors=0;long triangles=0;
            foreach(var r in Resources.FindObjectsOfTypeAll<Renderer>().Where(r=>r.gameObject.scene==scene))
                foreach(var m in r.sharedMaterials)
                    if(m==null)missing++;else if(m.shader==null || ShaderUtil.ShaderHasError(m.shader))shaderErrors++;
            foreach(var filter in Object.FindObjectsOfType<MeshFilter>())
                if(filter.sharedMesh!=null)triangles+=filter.sharedMesh.triangles.Length/3;
            if(missing>0 || shaderErrors>0)throw new InvalidOperationException("Missing materials="+missing+", shader errors="+shaderErrors);
            Physics.SyncTransforms();
            // The same downward query must see the visible ground, not just the road collider.
            // These temporary terrain colliders are removed before saving/rendering.
            var temporaryGround=new List<MeshCollider>();
            foreach(var filter in Object.FindObjectsOfType<MeshFilter>())
                if(filter.name=="Dense_Roadside_Ground" || filter.name.StartsWith("DEM_"))
                {
                    var collider=filter.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=filter.sharedMesh;temporaryGround.Add(collider);
                }
            Physics.SyncTransforms();
            int roadHits=0,misses=0;
            var discontinuities=new List<string>();
            for(int i=0;i<route.points.Length-1;i+=3)
            {
                foreach(float lateral in new[]{-1.75f,0,1.75f})
                {
                    Vector3 p=route.points[i]+route.Right(i)*lateral;
                    if(Physics.Raycast(p+Vector3.up*5,Vector3.down,out RaycastHit hit,10) && hit.collider.name=="Asphalt" && Mathf.Abs(hit.point.y-p.y)<.35f)roadHits++;
                    else {misses++;discontinuities.Add("index="+i+" lateral="+lateral+" p="+p.ToString("F4")+" hit="+hit.point.ToString("F4")+" collider="+(hit.collider==null?"none":hit.collider.name+"/"+hit.collider.transform.parent.name));}
                }
            }
            if(route.branches!=null)foreach(var branch in route.branches)
            {
                for(int i=1;i<branch.points.Length-1;i+=5)foreach(float lateral in new[]{-1.5f,0,1.5f})
                {
                    Vector3 right=Vector3.Cross(Vector3.up,branch.points[i+1]-branch.points[i]).normalized;
                    Vector3 p=branch.points[i]+right*lateral;
                    if(Physics.Raycast(p+Vector3.up*5,Vector3.down,out var hit,10)&&hit.collider.name=="Asphalt"&&Mathf.Abs(hit.point.y-p.y)<.35f)roadHits++;
                    else{misses++;discontinuities.Add("branch="+branch.name+" index="+i+" lateral="+lateral+" expected="+p.ToString("F3")+" hit="+hit.point.ToString("F3")+" collider="+(hit.collider==null?"none":hit.collider.name));}
                }
            }
            File.WriteAllLines(Path.Combine(Output,"road-discontinuities.txt"),discontinuities);
            foreach(var c in temporaryGround)Object.DestroyImmediate(c);
            CaptureViews(controller);
            if(misses>0)throw new InvalidOperationException("Road collider discontinuities="+misses);
            File.WriteAllText(Path.Combine(Output,"scene-report.txt"),"Scene="+ScenePath+"\nRouteMetres="+route.Length+"\nRouteSamples="+route.points.Length+"\nRoadColliderSamples="+roadHits+"\nMissingMaterials="+missing+"\nShaderErrors="+shaderErrors+"\nActiveTriangles="+triangles);
        }
        public static void CaptureViews(JejuDriveController controller)
        {
            if(controller.traffic!=null)controller.traffic.SetClock(0);
            for(int i=0;i<4;i++)
            {
                controller.JumpToStation(i);controller.cameraMode=0;controller.UpdateCamera(true);
                Capture(controller.view,Path.Combine(Output,"view-"+(i+1)+".png"));
            }
            controller.JumpToStation(3);
            controller.view.transform.position=controller.transform.position+new Vector3(-50,30,-60);
            controller.view.transform.LookAt(new Vector3(6400,1700,7900));controller.view.fieldOfView=62;
            Capture(controller.view,Path.Combine(Output,"hallasan.png"));
            controller.JumpToStation(0);Vector3 coast=controller.transform.position;
            controller.view.transform.position=coast+new Vector3(50,24,70);controller.view.transform.LookAt(coast+new Vector3(-60,0,-170));controller.view.fieldOfView=60;
            Capture(controller.view,Path.Combine(Output,"coastal-overview.png"));
            if(controller.traffic!=null&&controller.traffic.junctions.Length>0)
            {
                var junction=controller.traffic.junctions[0];int original=controller.route.stations[1].index;
                int index=Array.BinarySearch(controller.route.distances,junction.distance-45);controller.route.stations[1].index=Mathf.Max(0,index<0?~index:index);
                controller.JumpToStation(1);controller.cameraMode=0;controller.UpdateCamera(true);
                controller.traffic.SetClock(42-junction.phaseOffset);Capture(controller.view,Path.Combine(Output,"traffic-red.png"));
                controller.traffic.SetClock(5-junction.phaseOffset);Capture(controller.view,Path.Combine(Output,"traffic-green.png"));
                controller.traffic.SetClock(0);controller.route.stations[1].index=original;
            }
            controller.JumpToStation(0);controller.cameraMode=0;controller.UpdateCamera(true);
        }
        public static void Capture(Camera camera,string path)
        {
            var target=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32){antiAliasing=2};
            var old=camera.targetTexture;var active=RenderTexture.active;
            try
            {
                camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());Object.DestroyImmediate(image);
            }
            finally{camera.targetTexture=old;RenderTexture.active=active;target.Release();Object.DestroyImmediate(target);}
        }
    }
}
