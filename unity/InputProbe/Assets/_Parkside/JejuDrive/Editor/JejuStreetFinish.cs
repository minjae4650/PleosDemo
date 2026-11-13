using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Finish=Parkside.Streets.Editor.StreetFinishGeometry;

namespace Parkside.Jeju.Editor
{
    public static partial class JejuDriveBuilder
    {
        const string StreetFinishRoot="Seogwipo_Furniture_and_Wear";
        static readonly Color Galvanized=new Color(.54f,.57f,.58f);
        static readonly Color MarineBlue=new Color(.047f,.27f,.46f);
        static readonly Color ReflectiveIvory=new Color(.92f,.95f,.91f);

        static void BuildSeogwipoStreetFinish(Transform sceneRoot,JejuDriveController car)
        {
            var reference=ReadStreetReference();
            if(reference.streets.Length==0) return;
            var prior=sceneRoot.Find(StreetFinishRoot);
            if(prior!=null) UnityEngine.Object.DestroyImmediate(prior.gameObject);
            var group=Group(StreetFinishRoot,sceneRoot);
            var metalwork=new Geo();
            var letteringBoards=new Geo();
            var roadWear=new Geo();
            var covers=new Geo();
            var glazing=new Geo();
            var curbDetails=new Geo();
            var occupied=new List<Vector3>();
            Physics.SyncTransforms();

            foreach(var street in reference.streets)
            {
                float length=street.distances.Last();
                for(float d=13;d<length-7;d+=19)
                {
                    var p=StreetPoint(street,d,out var right);
                    var forward=Vector3.Cross(right,Vector3.up).normalized;
                    float width=StreetWidth(street,d);
                    if(NearStreetJunction(p,12)) continue;
                    float side=Mathf.Sin(d+street.osmId%23)>0?1:-1;
                    float lateral=width*.5f*(street.oneWay?.38f:side*.53f);
                    Finish.RoadPatch(roadWear,p+right*lateral,forward,1.2f+Mathf.Repeat(d,1.6f),2.7f+Mathf.Repeat(d,4.5f));
                    if(Finish.RoadPoint(p-right*lateral*.63f,out var cover))
                        Finish.Cover(covers,cover,.34f,false);
                    var drain=p+right*(side*(width*.5f-.26f));
                    if(Finish.RoadPoint(drain,out var drainAt)) Finish.Cover(covers,drainAt,.28f,true);
                }
                if(street.name=="대청로"||street.name=="월드컵로"||street.name=="신중로")
                    BuildCurbCaps(street,curbDetails);
                if(street.name=="대청로")
                    BuildUtilityLine(street,reference,metalwork,occupied);
            }

            PlaceJejuBoard(reference,group,letteringBoards,metalwork,occupied,33.25255,126.50381,
                "대청로","Daecheong-ro",2.15f,Vector3.right);
            PlaceJejuBoard(reference,group,letteringBoards,metalwork,occupied,33.25499,126.50957,
                "서귀포시청 제2청사","Seogwipo City Hall",3.45f,Vector3.left);
            PlaceJejuBoard(reference,group,letteringBoards,metalwork,occupied,33.24714,126.50681,
                "제주월드컵경기장","Jeju World Cup Stadium",3.85f,Vector3.back);
            PlaceJejuBoard(reference,group,letteringBoards,metalwork,occupied,33.24884,126.50742,
                "서귀포버스터미널","Seogwipo Bus Terminal",3.30f,Vector3.back);
            PlaceJejuShelter(reference,group,letteringBoards,metalwork,glazing,occupied,33.25256,126.50396,"대청로");
            PlaceJejuShelter(reference,group,letteringBoards,metalwork,glazing,occupied,33.25492,126.51002,"서귀포시청 제2청사");

            var repair=Material("City_road_repair",Color.white,shader:"ParkingLot/RoadRepair");
            repair.mainTexture=Load<Texture2D>(Environment+"/KoreanAssets/asphalt_repair_decal.png");
            repair.SetFloat("_Opacity",.25f);
            UnityEditor.EditorUtility.SetDirty(repair);
            SaveStreet(roadWear,"Asphalt_patches_and_scuffs",repair,group);
            SaveStreet(covers,"Service_covers_and_drains",CityMaterial("Cast_iron_street_fittings",Color.white,.29f),group);
            SaveStreet(metalwork,"Utility_poles_wires_and_shelters",CityMaterial("Painted_and_galvanized_metal",Color.white,.31f),group);
            SaveStreet(letteringBoards,"Road_names_direction_signs_and_panels",CityMaterial("Enamel_street_sign_panels",Color.white,.38f),group);
            var glass=Material("Tinted_shelter_glazing",new Color(.43f,.57f,.56f,.26f),
                shader:"ParkingLot/Realism20261104/Glass");
            glass.SetFloat("_Glossiness",.77f);
            UnityEditor.EditorUtility.SetDirty(glass);
            SaveStreet(glazing,"Shelter_glazing",glass,group);
            SaveStreet(curbDetails,"Jointed_concrete_kerb_caps",CityMaterial("Curb_caps",Color.white,.11f,
                Load<Texture2D>(Environment+"/KoreanAssets/concrete_pavement_diff_2k.jpg")),group);
            if(car!=null) Finish.PhotoCamera(car.view,Load<Shader>(Environment+"/PhotoFinish.shader"),1.25f);
            var letters=group.GetComponentsInChildren<TextMesh>();
            if(!letters.Any(t=>t.text=="대청로")||!letters.Any(t=>t.text=="BUS")||curbDetails.v.Count<80)
                throw new InvalidOperationException("Seogwipo street detail coverage is incomplete.");
            string directory=Path.Combine(Output,"seogwipo");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"street-finish.txt"),"StreetFurnitureLocations="+occupied.Count+
                "\nAsphaltPatchQuads="+roadWear.v.Count/4+"\nLettering="+letters.Length+
                "\nCurbCapQuads="+curbDetails.v.Count/4);
        }

        static void BuildCurbCaps(StreetSample street,Geo mesh)
        {
            float end=street.distances.Last();
            for(float d=0;d<end-.65f;d+=1.5f)
            {
                float finish=Mathf.Min(end,d+1.465f);
                var a=StreetPoint(street,d+.015f,out var ra);
                var b=StreetPoint(street,finish,out var rb);
                if(NearStreetJunction((a+b)*.5f,10)) continue;
                float wa=StreetWidth(street,d)*.5f,wb=StreetWidth(street,finish)*.5f;
                for(int side=-1;side<=1;side+=2)
                {
                    if(street.oneWay&&side<0) continue;
                    var aa=a+ra*(wa*side);var bb=b+rb*(wb*side);
                    var at=ClosestRoad(aa.x+ra.x*(side*2.25f),aa.z+ra.z*(side*2.25f),1);
                    if(at.road!=null&&at.d<at.road.w[at.i]*.5f+.15f) continue;
                    if(Finish.RoadPoint((aa+bb)*.5f+(ra+rb)*(side*.14f),out _)) continue;
                    var inner=Vector3.up*.133f;
                    var oa=aa+ra*(side*.24f)+inner;
                    var ob=bb+rb*(side*.24f)+inner;
                    var tone=new Color(.74f,.75f,.72f)*(.91f+.09f*Mathf.Sin(d*2.4f));
                    if(side>0) FlatFace(mesh,aa+inner,oa,bb+inner,ob,tone,.68f);
                    else FlatFace(mesh,oa,aa+inner,ob,bb+inner,tone,.68f);
                }
            }
        }

        static bool SidewalkPosition(StreetReference reference,double lat,double lon,Vector3 heading,
            float offset,out Vector3 p,out Vector3 forward)
        {
            p=GeoPosition(lat,lon);forward=heading;
            if(!TryStreet(p,reference,31,out var street,out float d,out _)) return false;
            p=StreetPoint(street,d,out var right);
            forward=Vector3.Cross(right,Vector3.up).normalized;
            if(Vector3.Dot(forward,heading)<0){forward=-forward;right=-right;}
            p+=right*(StreetWidth(street,d)*.5f+offset);
            p.y+=.12f;
            return !InReferenceRoad(p,reference,.4f)&&!Finish.RoadPoint(p,out _);
        }

        static void PlaceJejuBoard(StreetReference reference,Transform parent,Geo boards,Geo poles,List<Vector3> occupied,
            double lat,double lon,string name,string latin,float width,Vector3 heading)
        {
            if(!SidewalkPosition(reference,lat,lon,heading,1.45f,out var p,out var f)) return;
            Vector3 right=Vector3.Cross(Vector3.up,f).normalized;
            Vector3 centre=p+Vector3.up*3.13f+right*(width*.28f);
            var q=Quaternion.LookRotation(f);
            poles.Rod(p,p+Vector3.up*3.71f,.069f,Galvanized,9);
            poles.Rod(p+Vector3.up*2.76f,centre-Vector3.up*.27f+f*.08f,.039f,Galvanized,7);
            boards.Box(centre,new Vector3(width,.81f,.094f),q,ReflectiveIvory);
            boards.Box(centre-f*.053f,new Vector3(width-.085f,.72f,.018f),q,MarineBlue);
            Finish.Letter(parent,font,name,centre-f*.072f+Vector3.up*.13f,-f,width-.19f,.30f,Color.white);
            Finish.Letter(parent,font,latin,centre-f*.073f-Vector3.up*.183f,-f,width-.22f,.14f,ReflectiveIvory);
            occupied.Add(p);
        }

        static void PlaceJejuShelter(StreetReference reference,Transform parent,Geo boards,Geo frame,Geo glass,List<Vector3> occupied,
            double lat,double lon,string name)
        {
            if(!SidewalkPosition(reference,lat,lon,Vector3.right,1.41f,out var p,out var f)) return;
            if(occupied.Any(v=>XZDistanceSquared(v,p)<9)) return;
            var r=Vector3.Cross(Vector3.up,f);
            var q=Quaternion.LookRotation(f);
            var dark=new Color(.22f,.29f,.31f);
            frame.Box(p+Vector3.up*2.63f,new Vector3(1.38f,.12f,3.9f),q,dark);
            for(int side=-1;side<=1;side+=2)
            {
                var a=p+r*.54f+f*side*1.72f;
                frame.Rod(a,a+Vector3.up*2.60f,.048f,Galvanized);
                frame.Rod(a-f*side*.10f,a+Vector3.up*2.60f-f*side*.10f,.038f,dark);
            }
            glass.Box(p+r*.55f+Vector3.up*1.42f,new Vector3(.036f,1.90f,3.30f),q,new Color(.42f,.59f,.58f));
            frame.Box(p+r*.25f+Vector3.up*.51f,new Vector3(.40f,.10f,2.21f),q,new Color(.42f,.29f,.19f));
            boards.Box(p+r*.54f+Vector3.up*2.44f,new Vector3(.07f,.29f,3.53f),q,MarineBlue);
            Finish.Letter(parent,font,name,p+r*.484f+Vector3.up*2.44f,-r,3.27f,.22f,Color.white);
            var stop=p-f*2.22f;
            frame.Rod(stop,stop+Vector3.up*2.58f,.045f,Galvanized);
            boards.Box(stop+Vector3.up*2.30f,new Vector3(.46f,.62f,.075f),q,MarineBlue);
            Finish.Letter(parent,font,"BUS",stop+Vector3.up*2.32f-f*.049f,-f,.36f,.21f,Color.white);
            occupied.Add(p);
        }

        static void BuildUtilityLine(StreetSample street,StreetReference reference,Geo mesh,List<Vector3> occupied)
        {
            Vector3 previous=Vector3.zero;
            for(float d=17;d<street.distances.Last()-9;d+=28)
            {
                var p=StreetPoint(street,d,out var r);
                var q=p-r*(StreetWidth(street,d)*.5f+1.69f);
                q.y+=.12f;
                if(InReferenceRoad(q,reference,.42f)||Finish.RoadPoint(q,out _)||NearStreetJunction(p,16)) continue;
                var head=q+Vector3.up*7.95f;
                mesh.Rod(q,head,.12f,new Color(.47f,.49f,.46f),8);
                mesh.Rod(head-r*.62f,head+r*.62f,.041f,Galvanized,6);
                mesh.Box(q+Vector3.up*4.05f+r*.13f,new Vector3(.35f,.69f,.36f),Quaternion.identity,new Color(.51f,.54f,.52f));
                for(int wire=-1;wire<=1;wire++)
                {
                    var socket=head+r*(wire*.41f);
                    mesh.Rod(socket,socket+Vector3.up*.12f,.048f,ReflectiveIvory,6);
                    if(previous==Vector3.zero) continue;
                    var from=previous+r*(wire*.41f);
                    var a=from;
                    for(int n=1;n<=10;n++)
                    {
                        float t=n/10f;
                        var b=Vector3.Lerp(from,socket,t)-Vector3.up*(Mathf.Sin(t*Mathf.PI)*.48f);
                        mesh.Rod(a,b,.019f,new Color(.13f,.15f,.15f),4);
                        a=b;
                    }
                }
                previous=head;
                occupied.Add(q);
            }
        }

        public static void RefreshSeogwipoFacadeFinishBatch() => RefreshFacadeFinish(false);

        public static void RefreshJejuFacadeFinishBatch() => RefreshFacadeFinish(true);

        static void RefreshFacadeFinish(bool includeCoastalAndVillageBuildings)
        {
            try
            {
                scene=EditorSceneManager.OpenScene(ScenePath);
                var landscape=scene.GetRootGameObjects().Single(g=>g.name=="Jeju_CoastalDrive")
                    .transform.Find("Jeju_Landscape_And_Roadside");
                var city=landscape.Find(CityRoot);
                var car=UnityEngine.Object.FindObjectOfType<JejuDriveController>();
                if(city==null||car==null||car.route.stations.Length<10)
                    throw new InvalidOperationException("Build the Seogwipo district before updating its facades.");
                PrepareCoastalLandmarkData();
                plaster=Load<Material>(Folder+"/Generated/Materials/WarmCoastalPlaster.mat");
                wallStone=Load<Material>(Folder+"/Generated/Materials/VolcanicDryStone.mat");
                UnityEngine.Object.DestroyImmediate(city.gameObject);
                if(includeCoastalAndVillageBuildings)
                {
                    var oldGroups=landscape.Cast<Transform>().Where(t=>t.name==RoadsideBuildingRoot||
                        t.name.StartsWith("OSM_house_",StringComparison.Ordinal)).ToArray();
                    foreach(var group in oldGroups) UnityEngine.Object.DestroyImmediate(group.gameObject);
                }
                chunks.Clear();
                AssetDatabase.StartAssetEditing();
                try { MakeActualBuildings(landscape,!includeCoastalAndVillageBuildings); }
                finally { AssetDatabase.StopAssetEditing(); }
                car.sceneryChunks=car.sceneryChunks.Where(chunk=>chunk!=null).Concat(chunks).ToArray();
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save Seogwipo facades.");
                scene=EditorSceneManager.OpenScene(ScenePath);
                car=UnityEngine.Object.FindObjectOfType<JejuDriveController>();
                Verify();
                CaptureSeogwipoStreetFinish(car);
                if(includeCoastalAndVillageBuildings) CaptureJejuFacadeCoverage(car);
                File.WriteAllText(Path.Combine(Output,"seogwipo","facade-refresh.txt"),
                    "SavedAndReopened=PASS\nRoadAndMaterialVerification=PASS");
                EditorApplication.Exit(0);
            }
            catch(Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        static void CaptureSeogwipoStreetFinish(JejuDriveController car)
        {
            car.JumpToStation(9);
            car.cameraMode=0;car.UpdateCamera(true);
            string directory=Path.Combine(Output,"seogwipo");
            Capture(car.view,Path.Combine(directory,"seogwipo-street-finish.png"));
            car.cameraMode=1;car.UpdateCamera(true);
            Capture(car.view,Path.Combine(directory,"seogwipo-driver-finish.png"));
            CaptureSeogwipoShopfront(car,directory);
            car.cameraMode=0;car.JumpToStation(0);
        }

        static void CaptureSeogwipoShopfront(JejuDriveController car,string directory)
        {
            var reference=ReadStreetReference();
            var start=car.transform.position;
            var signs=UnityEngine.Object.FindObjectsOfType<TextMesh>()
                .Where(t=>LocalShopNames.Contains(t.text))
                .OrderBy(t=>XZDistanceSquared(t.transform.position,start));
            foreach(var sign in signs)
            {
                var target=sign.transform.position;
                if(!TryStreet(target,reference,31,out var street,out float d,out float distance)||distance>18*18) continue;
                var p=StreetPoint(street,Mathf.Min(d+6,street.distances.Last()),out var right);
                var facing=Vector3.Dot(-sign.transform.forward,p-target);
                if(facing<2) continue;
                var candidate=p+right*(Mathf.Sign(Vector3.Dot(right,target-p))*StreetWidth(street,d)*.24f);
                if(!Finish.RoadPoint(candidate,out var road)) continue;
                car.view.transform.position=road+Vector3.up*1.73f;
                car.view.transform.LookAt(target-Vector3.up*.85f);
                car.view.fieldOfView=52;
                Capture(car.view,Path.Combine(directory,"seogwipo-shopfront-finish.png"));
                return;
            }
        }
    }
}
