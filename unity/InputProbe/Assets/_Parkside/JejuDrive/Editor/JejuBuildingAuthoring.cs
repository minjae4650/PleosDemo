using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;
using UnityEngine;

namespace Parkside.Jeju.Editor
{
    public static partial class JejuDriveBuilder
    {
        const string CityRoot = "Seogwipo_Beophwan_Seoho";
        const string RoadsideBuildingRoot = "Jeju_Coastal_and_Village_Buildings";
        const string CityMeshes = Folder+"/Generated/CityMeshes";
        const string CityStationName = "서귀포 신시가지";
        sealed class BuildingBatch
        {
            public readonly Geo roofs = new Geo();
            public readonly Geo glass = new Geo();
            public readonly Geo details = new Geo();
            public readonly Geo foundation = new Geo();
            public readonly Geo paving = new Geo();
            public readonly Geo streetWalls = new Geo();
            public readonly Geo tileBands = new Geo();
            public readonly Geo fabric = new Geo();
            public readonly Geo photo = new Geo();
            public readonly Geo brick = new Geo();
            public readonly Geo granite = new Geo();
            public readonly Geo basalt = new Geo();
            public readonly Geo villaWindows = new Geo();
            public readonly Geo apartmentWindows = new Geo();
        }

        static Vector3 GeoPosition(double lat, double lon)
        {
            const double metresLat = 111195.08;
            double metresLon = metresLat * Math.Cos(33.29 * Math.PI / 180);
            return new Vector3((float)((lon-126.46)*metresLon), 0, (float)((lat-33.29)*metresLat));
        }

        static bool InSeogwipo(Vector3 p)
        {
            Vector3 min = GeoPosition(33.242,126.498), max = GeoPosition(33.263,126.526);
            return p.x > min.x && p.x < max.x && p.z > min.z && p.z < max.z;
        }

        static Material CityMaterial(string name, Color colour, float smooth = .1f, Texture texture = null)
        {
            var mat = Material(name, colour, smooth, 0, "PARKSIDE/Jeju/CitySurface");
            if (texture != null) mat.mainTexture = texture;
            UnityEditor.EditorUtility.SetDirty(mat);
            return mat;
        }

        static void MakeActualBuildings(Transform parent, bool urbanOnly=false)
        {
            if (data.buildings == null) return;
            Directory.CreateDirectory(CityMeshes);
            var city = Group(CityRoot, parent);
            var villages = urbanOnly ? null : Group(RoadsideBuildingRoot, parent);
            var reference = ReadStreetReference();
            var batches = new Dictionary<Transform,BuildingBatch>();
            var cityBlocks = new Dictionary<Vector2Int,Transform>();
            var villageBlocks = new Dictionary<Vector2Int,Transform>();
            var brick=CityMaterial("Facade_red_brick",Color.white,.13f,
                Load<Texture2D>(Environment+"/KoreanAssets/red_brick_diff_2k.jpg"));
            var granite=CityMaterial("Facade_granite_and_concrete",Color.white,.20f,
                Load<Texture2D>(Environment+"/KoreanAssets/concrete_wall_009_diff_2k.jpg"));
            var basalt=CityMaterial("Facade_dark_volcanic_stone",Color.white,.11f,
                Load<Texture2D>(Environment+"/KoreanAssets/aerial_grass_rock_diff_2k.jpg"));
            var villaWindows=CityMaterial("Villa_window_glass_and_curtains",Color.white,.35f,
                Load<Texture2D>(Environment+"/KoreanAssets/villa_facade_gray.png"));
            var apartmentWindows=CityMaterial("Apartment_window_glass_and_curtains",Color.white,.38f,
                Load<Texture2D>(Environment+"/KoreanAssets/apartment_facade.png"));
            var roof = CityMaterial("Building_Roof", new Color(.64f,.69f,.66f), .08f);
            var glass = CityMaterial("Building_Glass", new Color(.44f,.59f,.66f), .64f);
            var trim = CityMaterial("Building_Trim", Color.white);
            var forecourt = CityMaterial("City_Forecourt",Color.white,.06f,
                Load<Texture2D>(Environment+"/KoreanAssets/concrete_pavement_diff_2k.jpg"));
            var streetWalls=CityMaterial("Street_mineral_render",Color.white,.14f,plaster.mainTexture);
            var tileBands=CityMaterial("Street_tile_cladding",Color.white,.21f,
                Load<Texture2D>(Environment+"/KoreanAssets/red_brick_diff_2k.jpg"));
            var fabric=CityMaterial("Street_canvas",Color.white,.08f);
            var photo=CityMaterial("Korean_storefront_and_window_photo",Color.white,.21f,
                Load<Texture2D>(Environment+"/KoreanAssets/shop_facade.png"));
            int citySigns=0, villageSigns=0;
            int urbanCount=0, villageCount=0, setbacks=0;
            foreach (var building in data.buildings.Concat(reference.buildings))
            {
                if (urbanOnly && !building.urban) continue;
                var outline = Outline(building.points);
                if (outline.Length < 3) continue;
                if (building.urban && OverlapsStreet(outline,reference))
                {
                    setbacks++;
                    continue;
                }
                var centre = outline.Aggregate(Vector3.zero,(sum,p)=>sum+p)/outline.Length;
                float baseY = outline.Max(p=>DetailHeight(p.x,p.z)) + .12f;
                if (baseY < 1) continue;
                centre.y = baseY;
                if (building.landmark == "stadium")
                {
                    MakeWorldCupStadium(city, outline, centre, trim, glass);
                    buildingCount++;
                    urbanCount++;
                    continue;
                }

                Transform group;
                if (building.landmark == "city_hall")
                    group = SceneryGroup("Seogwipo_City_Hall_Annex", city, centre, 2000);
                else
                {
                    const int blockSize=110;
                    var block=new Vector2Int(Mathf.FloorToInt(centre.x/blockSize),Mathf.FloorToInt(centre.z/blockSize));
                    var districtBlocks=building.urban?cityBlocks:villageBlocks;
                    if(!districtBlocks.TryGetValue(block,out group))
                    {
                        string prefix=building.urban?"City_block_":"Village_block_";
                        group=SceneryGroup(prefix+block.x+"_"+block.y,building.urban?city:villages,
                            new Vector3((block.x+.5f)*blockSize,baseY,(block.y+.5f)*blockSize),1550);
                        districtBlocks.Add(block,group);
                    }
                }

                if (!batches.TryGetValue(group, out var batch))
                {
                    batch = new BuildingBatch();
                    batches.Add(group, batch);
                }
                bool signed=BuildStreetFacade(building,outline,centre,batch,group);
                buildingCount++;
                if(building.urban){ urbanCount++; if(signed) citySigns++; }
                else { villageCount++; if(signed) villageSigns++; }
            }

            foreach (var pair in batches)
            {
                var group = pair.Key;
                var b = pair.Value;
                SaveBuildingLayer(b.brick,"Brick_facades",brick,group,true);
                SaveBuildingLayer(b.granite,"Granite_and_concrete_facades",granite,group,true);
                SaveBuildingLayer(b.basalt,"Basalt_cladding",basalt,group,true);
                SaveBuildingLayer(b.villaWindows,"Villa_windows",villaWindows,group);
                SaveBuildingLayer(b.apartmentWindows,"Apartment_windows",apartmentWindows,group);
                SaveBuildingLayer(b.roofs,"Roofs_and_parapets",roof,group,true);
                SaveBuildingLayer(b.glass,"Windows_and_storefronts",glass,group);
                SaveBuildingLayer(b.details,"Balconies_and_trim",trim,group);
                SaveBuildingLayer(b.foundation,"Basalt_building_plinths",wallStone,group,true);
                SaveBuildingLayer(b.paving,"Street_frontage_paving",forecourt,group);
                SaveBuildingLayer(b.streetWalls,"Recessed_facades",streetWalls,group,true);
                SaveBuildingLayer(b.tileBands,"Brick_tile_and_stone_bands",tileBands,group,true);
                SaveBuildingLayer(b.fabric,"Shop_awnings",fabric,group);
                SaveBuildingLayer(b.photo,"Storefront_photography_and_window_interiors",photo,group);
            }
            Directory.CreateDirectory(Path.Combine(Output,"seogwipo"));
            File.WriteAllText(Path.Combine(Output,"seogwipo","buildings-report.txt"),
                "UrbanBuildings="+urbanCount+"\nRoadsideBuildings="+villageCount+
                "\nUrbanSignedBuildings="+citySigns+"\nRoadsideSignedBuildings="+villageSigns+
                "\nExcludedForRoadClearance="+setbacks);
        }

        static void SaveBuildingLayer(Geo mesh, string name, Material material, Transform group, bool shadow=false)
        {
            string path=CityMeshes+"/"+group.name+"_"+name+".asset";
            mesh.Save(name,material,group,shadow,assetPath:path);
        }

        static bool OverlapsStreet(Vector3[] outline,StreetReference reference)
        {
            for (int i=0;i<outline.Length;i++)
            {
                Vector3 a=outline[i],b=outline[(i+1)%outline.Length];
                int samples=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/1.5f));
                for (int j=0;j<samples;j++)
                {
                    var p=Vector3.Lerp(a,b,j/(float)samples);
                    var road=ClosestRoad(p.x,p.z,1);
                    if (road.road!=null && road.d<road.road.w[road.i]*.5f+.18f || InReferenceRoad(p,reference,.18f)) return true;
                }
            }
            return false;
        }

        // Unity's upward winding is clockwise in XZ. Use a consistent CCW
        // outline for facade normals and reverse each roof triangle.
        internal static Vector3[] Outline(Vector3[] source)
        {
            if (source == null || source.Length < 3) return Array.Empty<Vector3>();
            var points = new List<Vector3>();
            foreach (var p in source)
                if (points.Count == 0 || (p-points[points.Count-1]).sqrMagnitude > .0025f)
                    points.Add(p);
            if (points.Count > 1 && (points[0]-points[points.Count-1]).sqrMagnitude <= .0025f)
                points.RemoveAt(points.Count-1);
            if (points.Count < 3) return Array.Empty<Vector3>();
            float area = 0;
            for (int i=1;i<points.Count-1;i++) area += CrossXZ(points[i]-points[0],points[i+1]-points[0]);
            if (Mathf.Abs(area)<.05f) return Array.Empty<Vector3>();
            if (area<0) points.Reverse();
            return points.ToArray();
        }

        static float CrossXZ(Vector3 a,Vector3 b) => a.x*b.z-a.z*b.x;

        internal static void RoofPolygon(Geo mesh, Vector3[] outline, float y, Color colour)
        {
            int start = mesh.v.Count;
            var polygon = new List<int>();
            for (int i=0;i<outline.Length;i++)
            {
                var p = outline[i]; p.y = y;
                mesh.Vertex(p,new Vector2(p.x*.09f,p.z*.09f),colour);
                polygon.Add(i);
            }
            // Ear clipping keeps roofs inside concave source footprints.
            while (polygon.Count > 2)
            {
                bool clipped = false;
                for (int i=0;i<polygon.Count;i++)
                {
                    int a=polygon[(i+polygon.Count-1)%polygon.Count], b=polygon[i], c=polygon[(i+1)%polygon.Count];
                    float area=CrossXZ(outline[b]-outline[a],outline[c]-outline[a]);
                    if (area < -.0001f) continue;
                    if (area > .0001f)
                    {
                        bool contains=false;
                        foreach (int v in polygon)
                        {
                            if (v==a||v==b||v==c) continue;
                            var p=outline[v];
                            if (CrossXZ(outline[b]-outline[a],p-outline[a])>=0 &&
                                CrossXZ(outline[c]-outline[b],p-outline[b])>=0 &&
                                CrossXZ(outline[a]-outline[c],p-outline[c])>=0)
                            { contains=true; break; }
                        }
                        if (contains) continue;
                        mesh.t.Add(start+a); mesh.t.Add(start+c); mesh.t.Add(start+b);
                    }
                    polygon.RemoveAt(i);
                    clipped=true;
                    break;
                }
                if (!clipped) break;
            }
        }

        static void BuildForecourt(Geo paving,Vector3 middle,Vector3 tangent,Vector3 outward,float length,bool publicFront)
        {
            var road=ClosestRoad(middle.x,middle.z,1);
            if (road.road==null || Mathf.Abs(Vector3.Dot(road.right,outward))<.68f) return;
            float depth=Vector3.Dot(road.centre-middle,outward)-road.road.w[road.i]*.5f-2.2f;
            if (depth<1.5f || depth>(publicFront?45:28)) return;
            float span=Mathf.Min(length-.3f,publicFront?37:21);
            var colour=publicFront?new Color(.75f,.72f,.64f):new Color(.63f,.64f,.61f);
            int across=Mathf.CeilToInt(span/2.5f), along=Mathf.CeilToInt(depth/2.5f);
            for (int z=0;z<along;z++)
                for (int x=0;x<across;x++)
                {
                    Vector3 a=middle+tangent*(span*(x/(float)across-.5f))+outward*(depth*z/along);
                    Vector3 b=a+tangent*(span/across),c=a+outward*(depth/along),d=b+outward*(depth/along);
                    if (!SetForecourtHeight(ref a) || !SetForecourtHeight(ref b) ||
                        !SetForecourtHeight(ref c) || !SetForecourtHeight(ref d)) continue;
                    int n=paving.v.Count;
                    foreach (var p in new[]{a,b,c,d}) paving.Vertex(p,new Vector2(p.x,p.z)*.45f,colour);
                    Quad(paving.t,n,n+2,n+1,n+3);
                }
        }

        static bool SetForecourtHeight(ref Vector3 p)
        {
            var road=ClosestRoad(p.x,p.z,1);
            if (road.road!=null && road.d<road.road.w[road.i]*.5f+1.9f) return false;
            p.y=DetailHeight(p.x,p.z)+.09f;
            return true;
        }

        static int StreetFrontage(Vector3[] outline)
        {
            int bestEdge=-1;
            float best=float.PositiveInfinity;
            for (int i=0;i<outline.Length;i++)
            {
                Vector3 a=outline[i],b=outline[(i+1)%outline.Length];
                Vector3 mid=(a+b)*.5f;
                if ((b-a).sqrMagnitude<25) continue;
                var road=ClosestRoad(mid.x,mid.z);
                Vector3 outward=Vector3.Cross(Vector3.up,b-a);
                if (road.road==null || Vector3.Dot(road.centre-mid,outward)<=0 || road.d>=best) continue;
                best=road.d;
                bestEdge=i;
            }
            return bestEdge;
        }

        static void MakeWorldCupStadium(Transform parent,Vector3[] outline,Vector3 centre,Material trim,Material glass)
        {
            var group=SceneryGroup("Jeju_World_Cup_Stadium",parent,centre,3300);
            var concreteRing=new Geo();
            var concourse=new Geo();
            var canopy=new Geo();
            var steel=new Geo();
            var grass=new Geo();
            var whiteSail=new Color(.93f,.96f,.97f);
            var stone=new Color(.64f,.69f,.68f);
            var pale=new Color(.80f,.87f,.87f);
            float deck=centre.y+9f;
            float bowl=centre.y+2;
            for (int i=0;i<outline.Length;i++)
            {
                var a=outline[i];var b=outline[(i+1)%outline.Length];
                a.y=b.y=deck;
                var bottomA=a;var bottomB=b;
                bottomA.y=DetailHeight(a.x,a.z)-.2f;bottomB.y=DetailHeight(b.x,b.z)-.2f;
                concreteRing.Face(bottomA,bottomB,a,b,stone);
                var innerA=Vector3.Lerp(a,centre,.38f);var innerB=Vector3.Lerp(b,centre,.38f);
                innerA.y=innerB.y=bowl;
                concreteRing.Face(a,b,innerA,innerB,pale);
                concourse.Face(a+Vector3.up*.3f,b+Vector3.up*.3f,a+Vector3.up*3.4f,b+Vector3.up*3.4f,Color.white);
                steel.Rod(a+Vector3.up*3.5f,b+Vector3.up*3.5f,.24f,pale,6);
                var f=(b-a).normalized;
                int count=Mathf.CeilToInt(Vector3.Distance(a,b)/8);
                for (int k=0;k<count;k++)
                {
                    var p=Vector3.Lerp(a,b,(k+.5f)/count);
                    steel.Box(p+Vector3.up*1.8f,new Vector3(.53f,3.8f,.72f),Quaternion.LookRotation(f),pale);
                }
                // West-side tensile roof, shaped as a single arc above the bowl.
                if (Mathf.Max(a.x,b.x)<centre.x+2)
                {
                    var rearA=StadiumRoofPoint(a,centre,0);
                    var rearB=StadiumRoofPoint(b,centre,0);
                    var crestA=StadiumRoofPoint(a,centre,.48f);
                    var crestB=StadiumRoofPoint(b,centre,.48f);
                    var frontA=StadiumRoofPoint(a,centre,1);
                    var frontB=StadiumRoofPoint(b,centre,1);
                    canopy.Face(rearA,rearB,crestA,crestB,whiteSail);
                    canopy.Face(crestA,crestB,frontA,frontB,whiteSail);
                    canopy.Face(crestA,crestB,rearA,rearB,whiteSail);
                    canopy.Face(frontA,frontB,crestA,crestB,whiteSail);
                    steel.Rod(rearA,rearB,.30f,pale);
                    steel.Rod(frontA,frontB,.24f,pale);
                    if (i%3==0)
                    {
                        var mast=a+(a-centre).normalized*1.4f+Vector3.up*18;
                        steel.Rod(a,mast,.42f,pale);
                        steel.Rod(mast,crestA,.055f,pale,5);
                        steel.Rod(mast,frontA,.055f,pale,5);
                    }
                }
            }
            var pitch=outline.Select(p=>Vector3.Lerp(p,centre,.40f)).ToArray();
            RoofPolygon(grass,pitch,bowl,new Color(.24f,.47f,.27f));
            SaveBuildingLayer(concreteRing,"Stadium_bowl_and_concourse",trim,group,true);
            SaveBuildingLayer(concourse,"Stadium_concourse_glazing",glass,group);
            SaveBuildingLayer(canopy,"West_tensile_sail",trim,group,true);
            SaveBuildingLayer(steel,"Stadium_masts_and_cables",trim,group,true);
            SaveBuildingLayer(grass,"Bowl_pitch",trim,group);
            var west=outline.OrderBy(p=>p.x).First();
            west.y=deck+5;
            Label("제주월드컵경기장",west+Vector3.left*1.1f,.14f,group,Quaternion.LookRotation(Vector3.right));
        }

        static Vector3 StadiumRoofPoint(Vector3 edge,Vector3 centre,float t)
        {
            var p=Vector3.Lerp(edge,centre,.36f*t);
            float span=Mathf.Max(0,1-Mathf.Abs(edge.z-centre.z)/105f);
            p.y=edge.y+6+span*4+Mathf.Sin(t*Mathf.PI)*4;
            return p;
        }

        public static void RefreshUrbanDistrictAndVerify()
        {
            try
            {
                scene=EditorSceneManager.OpenScene(ScenePath);
                root=scene.GetRootGameObjects().Single(g=>g.name=="Jeju_CoastalDrive").transform;
                var car=Object.FindObjectOfType<JejuDriveController>();
                var landscape=root.Find("Jeju_Landscape_And_Roadside");
                var previous=landscape.Find(CityRoot);
                if (previous==null || car.route.stations.Length<10)
                    throw new InvalidOperationException("Build the connected Seogwipo roads before refreshing its district.");
                PrepareCoastalLandmarkData();
                plaster=Load<Material>(Folder+"/Generated/Materials/WarmCoastalPlaster.mat");
                wallStone=Load<Material>(Folder+"/Generated/Materials/VolcanicDryStone.mat");
                Object.DestroyImmediate(previous.gameObject);
                chunks.Clear();
                var existing=root.Find(StreetReferenceRoot);
                if(existing!=null) Object.DestroyImmediate(existing.gameObject);
                AssetDatabase.StartAssetEditing();
                try
                {
                    WarpStreetBase(root);
                    BuildStreetReference(root,car);
                    MakeActualBuildings(landscape,true);
                    RefreshUrbanSignalHeads(car);
                    BuildSeogwipoStreetFinish(root,car);
                }
                finally { AssetDatabase.StopAssetEditing(); }
                AssignGradedTraffic(car);
                car.sceneryChunks=car.sceneryChunks.Where(t=>t!=null).Concat(chunks).ToArray();
                var stations=car.route.stations.Take(9).ToList();
                AttachUrbanStation(car,stations);
                car.route.stations=stations.ToArray();
                SealRoadbedSeams();
                car.JumpToStation(0);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the Seogwipo district.");
                ValidateCoastalLandmarks();
                VerifyStreetReference(car);
                CaptureSeogwipoStreetFinish(car);
                Verify();
                CaptureUrbanViews();
                JejuDriveVerification.BeginPlayCheck();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                File.WriteAllText(Path.Combine(Output,"seogwipo-error.txt"),e.ToString());
                EditorApplication.Exit(1);
            }
        }

        static void CaptureUrbanViews()
        {
            var car = UnityEngine.Object.FindObjectOfType<JejuDriveController>();
            int station = Array.FindIndex(car.route.stations, s=>s.name==CityStationName);
            if (station < 0) return;
            var spawn=car.route.stations[station].position;
            Physics.SyncTransforms();
            if (!Physics.Raycast(spawn+Vector3.up*3,Vector3.down,out var road,6) ||
                road.collider.name!="Asphalt" || Mathf.Abs(road.point.y-spawn.y)>.35f)
                throw new InvalidOperationException("The Seogwipo start is outside the drivable road.");
            string folder=Path.Combine(Output,"seogwipo");
            Directory.CreateDirectory(folder);
            car.cameraMode=0;
            car.JumpToStation(station);
            Capture(car.view,Path.Combine(folder,"seogwipo-drive.png"));
            var city=car.route.stations[station];
            var target=city.lookAt;
            target.y=city.position.y;
            car.view.transform.position=target+new Vector3(-430,220,-220);
            car.view.transform.LookAt(target+new Vector3(30,5,255));
            car.view.fieldOfView=64;
            Capture(car.view,Path.Combine(folder,"seogwipo-overview.png"));
            car.view.transform.position=target+new Vector3(-250,125,-90);
            car.view.transform.LookAt(target+Vector3.up*16);
            car.view.fieldOfView=56;
            Capture(car.view,Path.Combine(folder,"world-cup-stadium.png"));
            car.JumpToStation(0);
        }

        static void AttachUrbanStation(JejuDriveController car, List<JejuStation> stations)
        {
            var stadium=data.buildings?.FirstOrDefault(b=>b.landmark=="stadium");
            if (stadium==null) return;
            var target=stadium.points.Aggregate(Vector3.zero,(sum,p)=>sum+p)/stadium.points.Length;
            var gate=GeoPosition(33.25255,126.50335);
            var entry=ClosestRoad(gate.x,gate.z);
            if (entry.road==null) return;
            var forward=entry.forward;
            if (forward.x<0) forward=-forward;
            var p=GradedRoad(entry.centre)+Vector3.Cross(Vector3.up,forward).normalized*JejuDriveRoute.LaneOffset(entry.road.w[entry.i]);
            if (Physics.Raycast(p+Vector3.up*2,Vector3.down,out var hit,4) && hit.collider.name=="Asphalt")
                p.y=hit.point.y;
            stations.Add(new JejuStation
            {
                name=CityStationName,subtitle="대청로 · 상가와 공동주택",branchSpawn=true,
                index=car.route.Nearest(p,out _,out _),position=p,forward=forward,lookAt=target
            });
        }
    }
}
