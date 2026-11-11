using System;
using System.IO;
using System.Linq;
using Parkside.Jeju;
using Parkside.Jeju.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Geo = Parkside.Jeju.Editor.JejuDriveBuilder.Geo;
using Finish = Parkside.Streets.Editor.StreetFinishGeometry;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        public const string ScenePath = "Assets/_Parkside/Scenes/ParisEiffelDrive.unity";
        const string Folder = "Assets/_Parkside/ParisDrive";
        const string Env = "Assets/ParkingLotEnvironment";
        static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath,"../../../build/paris-drive"));
        static readonly Color White = new Color(.92f,.91f,.85f);
        static readonly Color Limestone = new Color(.86f,.79f,.66f);
        static readonly Color Iron = new Color(.34f,.25f,.18f);

        [Serializable] sealed class Source
        {
            public Vector3 min, max;
            public Road[] roads;
            public Vector3[] guide, roadSurface, stoneRoads, sidewalks, land, water, parks, pedestrianBridge, bridgeOutline, trees;
            public string[] guideNames;
            public float[] guideWidths;
            public JejuStation[] stations;
            public Outline[] banks;
            public BridgeDeck[] bridgeDecks;
            public Landmark[] landmarks;
            public Building[] buildings;
            public Signal[] signals;
        }
        [Serializable] sealed class Outline { public Vector3[] points; }
        [Serializable] sealed class Landmark { public string key; public Vector3 centre; public Vector3[] outline; }
        [Serializable] sealed class BridgeDeck { public string name; public Vector3[] points,mesh; }
        [Serializable] sealed class Road
        {
            public long osmId;
            public string name, roadClass;
            public int lanes;
            public float width;
            public bool oneWay, bridge;
            public Vector3[] points;
        }
        [Serializable] sealed class Building
        {
            public long osmId;
            public string name,kind,number;
            public Retail[] retailUse;
            public int levels;
            public float height;
            public Vector3[] points;
        }
        [Serializable] sealed class Retail { public string kind,name; }
        [Serializable] sealed class Signal { public Vector3 point; public Vector3[] arms; }

        [MenuItem("PARKSIDE/Paris/Open Eiffel drive")]
        static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        public static void BuildBatch()
        {
            try { BuildAndVerify(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        internal static void BuildAndVerify()
        {
            Directory.CreateDirectory(Output);
            Directory.CreateDirectory(Folder+"/Generated");
            AssetDatabase.Refresh();
            var source=ReadSource();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=Group("Paris_Eiffel_Drive");
            var streets=new ParisStreetLookup(source);
            AssetDatabase.StartAssetEditing();
            try
            {
                BuildLandscape(source,root);
                BuildRoads(source,root);
                BuildBuildings(source,root,streets);
                BuildParisStreetFinish(source,root,streets);
                BuildEiffelTower(root);
                BuildExpansionLandmarks(source,root);
                BuildBirHakeim(source,root);
                BuildStreetTrees(source,root);
                MakeLighting(root);
                MakeDriver(source,root);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            BuildParisTraffic(root);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene,ScenePath)) throw new IOException("Could not save Paris drive scene");
            EditorSceneManager.OpenScene(ScenePath);
            Verify(source,streets);
        }

        static Source ReadSource()
        {
            var source=JsonUtility.FromJson<Source>(File.ReadAllText(Folder+"/Data/ParisRoadData.json"));
            if(source.guide==null||source.guide.Length<2||source.guide[0]!=source.guide[source.guide.Length-1])
                throw new InvalidDataException("Paris guide must be a closed route.");
            return source;
        }

        public static void RefreshGuideBatch()
        {
            try
            {
                var source=ReadSource();
                var scene=EditorSceneManager.OpenScene(ScenePath);
                var root=scene.GetRootGameObjects().Single(go=>go.name=="Paris_Eiffel_Drive").transform;
                var driver=root.GetComponentInChildren<JejuDriveController>();
                var streetContext=root.Find("Paris_Street_Furniture_and_Enamel");
                var landmarks=root.Find("Paris_Landmarks");
                if(driver==null||streetContext==null||landmarks==null)
                    throw new InvalidOperationException("Build the expanded Paris scene before refreshing its guide.");
                var streets=new ParisStreetLookup(source);
                ConfigureRoute(driver.route,source);
                driver.JumpToStation(0);
                Object.DestroyImmediate(streetContext.gameObject);
                Object.DestroyImmediate(landmarks.gameObject);
                AssetDatabase.StartAssetEditing();
                try
                {
                    StoneSetts();
                    BuildParisStreetFinish(source,root,streets);
                    BuildExpansionLandmarks(source,root);
                }
                finally { AssetDatabase.StopAssetEditing(); }
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))
                    throw new IOException("Could not save the updated Paris guide.");
                EditorSceneManager.OpenScene(ScenePath);
                Verify(source,streets);
                EditorApplication.Exit(0);
            }
            catch(Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        public static void RefreshBuildingsBatch()
        {
            try
            {
                var source=ReadSource();
                var scene=EditorSceneManager.OpenScene(ScenePath);
                var root=scene.GetRootGameObjects().Single(go=>go.name=="Paris_Eiffel_Drive").transform;
                var buildings=root.Find(BuildingDistrict);
                if(buildings==null) throw new InvalidOperationException("Paris building district is missing.");
                Object.DestroyImmediate(buildings.gameObject);
                EditorUtility.UnloadUnusedAssetsImmediate();

                var streets=new ParisStreetLookup(source);
                AssetDatabase.StartAssetEditing();
                try { BuildBuildings(source,root,streets); }
                finally { AssetDatabase.StopAssetEditing(); }
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))
                    throw new IOException("Could not save the updated Paris architecture.");
                EditorSceneManager.OpenScene(ScenePath);
                Verify(source,streets);
                EditorApplication.Exit(0);
            }
            catch(Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        static Transform Group(string name,Transform parent=null)
        {
            var g=new GameObject(name).transform;
            g.SetParent(parent,false);
            return g;
        }

        static T Asset<T>(string path) where T:Object
        {
            var value=AssetDatabase.LoadAssetAtPath<T>(path);
            if (value==null) throw new FileNotFoundException(path);
            return value;
        }

        static Material Surface(string name,Color tint,string texture=null,float smooth=.12f,string shader="PARKSIDE/Jeju/CitySurface")
        {
            string path=Folder+"/Generated/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material==null)
            {
                material=new Material(Shader.Find(shader)){name=name};
                AssetDatabase.CreateAsset(material,path);
            }
            material.shader=Shader.Find(shader);
            if(material.HasProperty("_Color")) material.color=tint;
            if (texture!=null) material.mainTexture=Asset<Texture2D>(Env+"/"+texture);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness",smooth);
            EditorUtility.SetDirty(material);
            return material;
        }

        static GameObject Save(Geo geo,string name,Material material,Transform parent,bool castShadows=false,bool collider=false)
        {
            return geo.Save(name,material,parent,castShadows,collider,Folder+"/Generated/"+parent.name+"_"+name+".asset");
        }

        static void MakeLighting(Transform parent)
        {
            var light=Group("Afternoon_sun",parent).gameObject.AddComponent<Light>();
            light.type=LightType.Directional;
            light.transform.rotation=Quaternion.Euler(43,-37,0);
            light.color=new Color(1,.95f,.86f);
            light.intensity=1.18f;light.shadows=LightShadows.Soft;
            RenderSettings.sun=light;
            RenderSettings.skybox=Asset<Material>(Env+"/Materials/Parking_sky.mat");
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.58f,.65f,.71f);
            RenderSettings.ambientEquatorColor=new Color(.50f,.47f,.42f);
            RenderSettings.ambientGroundColor=new Color(.24f,.24f,.21f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;
            RenderSettings.fogColor=new Color(.72f,.77f,.81f);
            RenderSettings.fogStartDistance=850;RenderSettings.fogEndDistance=3300;
        }

        static void MakeDriver(Source s,Transform parent)
        {
            var root=Group("Paris_Driving",parent);
            var route=root.gameObject.AddComponent<JejuDriveRoute>();
            ConfigureRoute(route,s);
            var vehicle=Group("Player",root);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(Asset<GameObject>(Env+"/RealVehicles/hyundai-tucson/hyundai-tucson.prefab"),vehicle);
            model.transform.localRotation=Quaternion.Euler(0,180,0);
            var renderers=model.GetComponentsInChildren<Renderer>();
            var bounds=renderers[0].bounds;
            foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            model.transform.localPosition-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            foreach(var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic=false;
            foreach(var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var car=vehicle.gameObject.AddComponent<JejuDriveController>();
            car.route=route;car.visual=model.transform;car.topSpeedKph=48;
            car.driveTitle="PARIS  /  센강에서 샹젤리제까지";
            car.mapCaption="Tour Eiffel · Concorde · Arc de Triomphe";
            car.stationHelp="1–9 파리 출발점";
            car.mapCredit="© OpenStreetMap contributors · ODbL";
            car.view=Group("Drive_camera",root).gameObject.AddComponent<Camera>();
            car.view.gameObject.tag="MainCamera";
            car.view.gameObject.AddComponent<AudioListener>();
            car.view.farClipPlane=3500;
            Finish.PhotoCamera(car.view,Asset<Shader>(Env+"/PhotoFinish.shader"),1.32f);
            car.JumpToStation(0);
        }

        static void ConfigureRoute(JejuDriveRoute route, Source s)
        {
            route.points=s.guide;route.widths=s.guideWidths;route.roadNames=s.guideNames;
            route.distances=JejuTrafficNetwork.Distances(route.points);
            route.stations=s.stations;
            route.branches=s.roads.Select(r=>new JejuRoadBranch{name=r.name,points=r.points,widths=new[]{r.width}}).ToArray();
            route.map=Asset<Texture2D>(Folder+"/Data/ParisMap.png");
            route.mapMin=new Vector2(s.min.x,s.min.z);route.mapMax=new Vector2(s.max.x,s.max.z);
            route.font=Asset<Font>("Assets/_Parkside/UI/Fonts/AstaSans-Medium.ttf");
        }
    }
}
