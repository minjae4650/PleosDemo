using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Parkside.Jeju;
using Parkside.Jeju.Editor;
using UnityEngine;
using Object=UnityEngine.Object;
using Finish=Parkside.Streets.Editor.StreetFinishGeometry;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        static void CaptureParisStreetFinish(JejuDriveController car,Source source,ParisStreetLookup streets)
        {
            var buildings=GameObject.Find(BuildingDistrict).transform;
            var signs=buildings.GetComponentsInChildren<TextMesh>()
                .Where(t=>t.text.Length>5&&t.text!="MERCURE"&&t.text!="PULLMAN")
                .OrderBy(t=>(t.transform.position-source.guide[0]).sqrMagnitude);
            var neighbourhoods=new List<Vector3>();
            foreach(var shop in signs)
            {
                var target=shop.transform.position;
                var basePoint=new Vector3(target.x,0,target.z);
                if(neighbourhoods.Any(p=>(p-basePoint).sqrMagnitude<360*360)) continue;
                if(!streets.Nearest(basePoint,26,out var street,out var tangent,out _,facing:-shop.transform.forward)||
                    Vector3.Distance(basePoint,street)<6) continue;
                var cameraPoint=street+tangent*5;
                if(!Finish.RoadPoint(cameraPoint,out var onRoad)) continue;
                car.view.transform.position=onRoad+Vector3.up*1.74f;
                car.view.transform.LookAt(target-Vector3.up*.55f);
                car.view.fieldOfView=61;
                if(neighbourhoods.Count==0)
                    JejuDriveBuilder.Capture(car.view,Path.Combine(Output,"paris-shopfront-finish.png"));
                JejuDriveBuilder.Capture(car.view,Path.Combine(Output,$"paris-corridor-{neighbourhoods.Count+1}.png"));
                neighbourhoods.Add(basePoint);
                if(neighbourhoods.Count==4) break;
            }
            if(neighbourhoods.Count<3)
                throw new InvalidOperationException("Expected signed facades in at least three separate Paris districts.");
        }

        static void CaptureBuildingDepth(JejuDriveController car,Source source,ParisStreetLookup streets)
        {
            var district=GameObject.Find(BuildingDistrict);
            var meshNames=district.GetComponentsInChildren<MeshFilter>()
                .Where(filter=>filter.sharedMesh!=null&&filter.sharedMesh.vertexCount>0)
                .Select(filter=>filter.name).ToArray();
            foreach(string name in new[]{"Masonry_with_openings","Recessed_upper_window_panes",
                "Transparent_shop_glazing","Shop_floors_walls_and_fittings"})
                if(meshNames.Count(value=>value==name)<12)
                    throw new InvalidOperationException("Paris architecture layer missing from saved blocks: "+name);

            var signs=district.GetComponentsInChildren<TextMesh>().Where(t=>t.text.Length>5).ToArray();
            car.cameraMode=1;
            car.UpdateCamera(true);
            CaptureFacadeView(car,source.guide[source.stations[6].index],streets,signs,
                "paris-building-depth.png",lookUp:2.7f);
            CaptureFacadeView(car,source.guide[source.stations[8].index],streets,signs,
                "paris-shop-depth.png",lookUp:.15f);
            car.cameraMode=0;
            car.JumpToStation(0);
        }

        static void CaptureFacadeView(JejuDriveController car,Vector3 anchor,ParisStreetLookup streets,
            TextMesh[] signs,string filename,float lookUp)
        {
            foreach(var sign in signs.OrderBy(t=>(t.transform.position-anchor).sqrMagnitude))
            {
                var target=sign.transform.position;
                var footprint=new Vector3(target.x,0,target.z);
                var outward=-sign.transform.forward;
                if(!streets.Nearest(footprint,30,out var street,out var heading,out _,facing:outward)) continue;
                float distance=Vector3.Distance(footprint,street);
                if(distance<6.7f||distance>25f) continue;
                var eye=street+heading*5.5f;
                if(!Finish.RoadPoint(eye,out var ground)) continue;
                var shift=outward*-Mathf.Max(0,distance-10f);
                if(Vector3.Dot(ground+shift-footprint,outward)<4.5f) continue;
                car.view.transform.position=ground+shift+Vector3.up*1.82f;
                car.view.transform.LookAt(target+Vector3.up*lookUp);
                car.view.fieldOfView=57;
                JejuDriveBuilder.Capture(car.view,Path.Combine(Output,filename));
                return;
            }
            throw new InvalidOperationException("No camera could frame the Paris facade near "+anchor);
        }

        static int VerifyGuideSurface(Source source, JejuDriveRoute route)
        {
            int checks=0;
            for(int i=1;i<source.guide.Length-1;i+=3)
            {
                var point=source.guide[i];
                var right=route.Right(i);
                float edge=Mathf.Min(1.5f,source.guideWidths[i]*.30f);
                foreach(float offset in new[]{-edge,0,edge})
                {
                    RequireAsphalt(point+right*offset);
                    checks++;
                }
            }
            return checks;
        }

        static int VerifyBranchSurface(Source source)
        {
            int checks=0;
            foreach(var road in source.roads)
                for(int i=0;i<road.points.Length-1;i++)
                {
                    var a=road.points[i];
                    var b=road.points[i+1];
                    int samples=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/42));
                    for(int j=0;j<samples;j++)
                    {
                        RequireAsphalt(Vector3.Lerp(a,b,(j+.5f)/samples));
                        checks++;
                    }
                }
            return checks;
        }

        static void RequireAsphalt(Vector3 p)
        {
            if(!Finish.RoadPoint(p,out _))
                throw new InvalidOperationException("Missing Paris asphalt at "+p.ToString("F2"));
        }

        static void CaptureExpansionStreetViews(JejuDriveController car, Source source)
        {
            var arc=LandmarkByKey(source,"arc").centre;
            var obelisk=LandmarkByKey(source,"obelisk").centre;
            var dome=LandmarkByKey(source,"invalides").centre;
            CaptureApproach(car,source,source.stations[5].index-20,arc+Vector3.up*22,
                "paris-arc-de-triomphe.png",57);
            CaptureApproach(car,source,source.stations[6].index,arc+Vector3.up*17,
                "paris-champs-elysees.png",58);
            var concordeView=obelisk+new Vector3(-33,0,-66);
            var vaubanView=dome+new Vector3(10,0,-150);
            CaptureApproach(car,source,NearestGuideView(source,concordeView,"Place de la Concorde","Pont de la Concorde"),
                obelisk+new Vector3(1,10,13),"paris-concorde.png",70);
            CaptureApproach(car,source,NearestGuideView(source,vaubanView,"Place Vauban","Avenue de Tourville"),
                dome+Vector3.up*37,"paris-invalides.png",59);
        }

        static int NearestGuideView(Source source,Vector3 eye,string firstStreet,string secondStreet)
        {
            return Enumerable.Range(1,source.guide.Length-2)
                .Where(i=>source.guideNames[i]==firstStreet||source.guideNames[i]==secondStreet)
                .OrderBy(i=>(source.guide[i]-eye).sqrMagnitude).First();
        }

        static void CaptureApproach(JejuDriveController car, Source source, int index, Vector3 focus,
            string filename, float fieldOfView)
        {
            index=Mathf.Clamp(index,1,source.guide.Length-2);
            var forward=(source.guide[index+1]-source.guide[index-1]).normalized;
            var right=Vector3.Cross(Vector3.up,forward);
            var p=source.guide[index]+right*JejuDriveRoute.LaneOffset(source.guideWidths[index]);
            if(!Finish.RoadPoint(p,out var ground))
                throw new InvalidOperationException("Capture point is outside the road: "+filename);
            car.transform.SetPositionAndRotation(ground,Quaternion.LookRotation(forward));
            car.cameraMode=0;
            car.UpdateCamera(true);
            car.view.transform.LookAt(focus);
            car.view.fieldOfView=fieldOfView;
            JejuDriveBuilder.Capture(car.view,Path.Combine(Output,filename));
        }

        static void Verify(Source s,ParisStreetLookup streets)
        {
            var car=Object.FindObjectOfType<JejuDriveController>();
            var letters=Object.FindObjectsOfType<TextMesh>();
            foreach(var label in new[]{"RUE JEAN REY","PULLMAN","MERCURE","ÉMILE ANTHOINE"})
                if(!letters.Any(t=>t.text==label)) throw new InvalidOperationException("Missing Paris lettering: "+label);
            Physics.SyncTransforms();
            int checks=VerifyGuideSurface(s,car.route);
            int roadChecks=VerifyBranchSurface(s);
            for(int i=0;i<car.route.stations.Length;i++)
            {
                car.JumpToStation(i);
                JejuDriveBuilder.Capture(car.view,Path.Combine(Output,$"paris-station-{i+1}.png"));
            }
            car.JumpToStation(0);
            car.view.transform.position=car.transform.position-car.transform.forward*12+Vector3.up*4.2f;
            car.view.transform.LookAt(new Vector3(-42,95,-43));
            car.view.fieldOfView=64;
            JejuDriveBuilder.Capture(car.view,Path.Combine(Output,"paris-eiffel-street.png"));
            car.view.transform.position=new Vector3(-460,230,480);
            car.view.transform.LookAt(new Vector3(40,74,-190));
            car.view.fieldOfView=58;
            JejuDriveBuilder.Capture(car.view,Path.Combine(Output,"paris-overview.png"));
            car.JumpToStation(0);
            CaptureParisStreetFinish(car,s,streets);
            CaptureBuildingDepth(car,s,streets);
            CaptureExpansionStreetViews(car,s);
            car.JumpToStation(0);
            var start=car.transform.position;
            for(int i=0;i<90;i++) car.Simulate(1,0,false,.02f);
            if (Vector3.Distance(start,car.transform.position)<2f) throw new InvalidOperationException("Paris manual drive did not move");
            car.guidedDrive=true;
            car.route.stations[0].index=0;
            car.JumpToStation(0);
            int steps=0;
            int stepLimit=Mathf.CeilToInt(car.route.Length/(4f*.05f));
            while(car.Progress<car.route.Length-42 && steps++<stepLimit) car.Simulate(0,0,false,.05f);
            if(car.Progress<car.route.Length-42) throw new InvalidOperationException("Paris guide did not finish");
            File.WriteAllText(Path.Combine(Output,"verification.txt"),
                $"Scene={ScenePath}\nSavedAndReopened=PASS\nFacadeLayers=PASS\nRoadSamples={checks}\nBranchSamples={roadChecks}\nManualDrive=PASS\nGuidedDrive=PASS\nGuideMetres={car.route.Length:F1}\nRoadWays={s.roads.Length}\nBuildings={s.buildings.Length}\nStations={s.stations.Length}");
        }
    }
}
