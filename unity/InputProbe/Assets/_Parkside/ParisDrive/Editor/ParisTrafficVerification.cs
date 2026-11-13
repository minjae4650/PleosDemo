using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Parkside.Jeju;
using Parkside.Jeju.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        const string TrafficPlayKey="Parkside.Paris.TrafficPlayCheck";

        public static void VerifyTrafficPlay()
        {
            EditorSceneManager.OpenScene(ScenePath);
            SessionState.SetBool(TrafficPlayKey,true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void RegisterTrafficPlayCheck()
        {
            EditorApplication.playModeStateChanged+=state=>
            {
                if(!SessionState.GetBool(TrafficPlayKey,false)||state!=PlayModeStateChange.EnteredPlayMode)return;
                SessionState.EraseBool(TrafficPlayKey);
                EditorApplication.delayCall+=CheckParisTraffic;
            };
        }

        static void CheckParisTraffic()
        {
            var report=new List<string>();
            try
            {
                var car=Object.FindObjectOfType<JejuDriveController>();
                if(car==null||car.traffic==null||car.ambient==null)
                    throw new InvalidOperationException("Saved Paris scene is missing traffic components.");
                car.enabled=false;
                car.ambient.Prepare();
                report.Add("PlayMode=PASS");
                report.Add("DirectedLanes="+car.ambient.Network.edges.Length);
                report.Add("AllowedConnections="+car.ambient.Network.edges.Sum(e=>e.allowedNext.Length));
                report.Add("SignalGroups="+car.traffic.junctions.Length);
                report.Add("SignalApproaches="+car.traffic.approaches.Length);
                CheckParisRedQueue(car,report);
                CheckParisNeighbourhoods(car,report);
                CheckParisTrafficGuide(car,report);
                File.WriteAllLines(Path.Combine(Output,"paris-traffic-play.txt"),report);
                EditorApplication.Exit(0);
            }
            catch(Exception error)
            {
                File.WriteAllLines(Path.Combine(Output,"paris-traffic-play.txt"),report.Append(error.ToString()));
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        static void CheckParisRedQueue(JejuDriveController car,List<string> report)
        {
            var traffic=car.ambient;
            var network=traffic.Network;
            var system=car.traffic;
            traffic.spawning=false;
            traffic.ResetPopulation();
            var champs=car.route.points[car.route.stations[6].index];
            var edge=network.edges.Where(e=>e.signalApproach>0&&e.Length>43&&
                e.allowedNext.Any(n=>network.edges[n].Length>15))
                .OrderBy(e=>(network.nodes[e.toNode].point-champs).sqrMagnitude).First();
            int edgeIndex=Array.IndexOf(network.edges,edge);
            var approach=system.approaches[edge.signalApproach-1];
            var junction=system.junctions[approach.junction];
            edge.At(edge.Length,out var heading);
            int exitIndex=edge.allowedNext.Where(n=>network.edges[n].Length>15)
                .OrderByDescending(n=>Vector3.Angle(heading,HeadingAtStart(network.edges[n]))).First();
            car.transform.position=approach.line+Vector3.Cross(Vector3.up,heading)*175;
            for(int i=0;i<2;i++)
            {
                var vehicle=traffic.vehicles[i];
                vehicle.path.Clear();
                vehicle.path.Add(edgeIndex);
                vehicle.path.Add(exitIndex);
                traffic.Activate(vehicle,network.edges[exitIndex].toNode,edge.Length-19-13*i);
            }
            system.SetClock((approach.cross?5:45)-junction.phaseOffset);
            for(int i=0;i<600;i++)traffic.Simulate(.05f);
            var first=traffic.vehicles[0];
            var second=traffic.vehicles[1];
            float lineGap=edge.Length-approach.stopBeforeEdgeEnd-2.5f-first.distance;
            if(first.inTurn||second.inTurn||first.leg!=0||second.leg!=0||first.speed>.2f||second.speed>.2f||
                lineGap<0||lineGap>1.5f||first.distance-second.distance<4.8f)
                throw new InvalidOperationException("Paris red signal failed to stop a two-car queue. Gap="+lineGap);
            float signalDistance=system.StopDistance(first.position,heading,out var aspect);
            if(aspect!=JejuSignalAspect.Red||signalDistance<2.4f||signalDistance>5f)
                throw new InvalidOperationException("Player signal does not match the NPC stop line.");
            report.Add("RedStopAndQueue=PASS");
            report.Add("QueueStreet="+edge.name);
            CaptureParisQueue(car,edge,heading,"paris-traffic-red.png");
            system.SetClock((approach.cross?45:5)-junction.phaseOffset);
            int completed=traffic.CompletedTurns;
            for(int i=0;i<80;i++)traffic.Simulate(.05f);
            CaptureParisQueue(car,edge,heading,"paris-traffic-green.png");
            for(int i=0;i<720;i++)traffic.Simulate(.05f);
            if(traffic.CompletedTurns-completed<2)
                throw new InvalidOperationException("Paris queue did not clear when the light turned green.");
            report.Add("GreenRestartAndTurns=PASS");
        }

        static Vector3 HeadingAtStart(JejuTrafficEdge edge)
        {
            edge.At(0,out var heading);
            return heading;
        }

        static void CaptureParisQueue(JejuDriveController car,JejuTrafficEdge edge,Vector3 heading,string filename)
        {
            car.cameraMode=1;
            car.UpdateCamera(true);
            var eye=edge.At(edge.Length-19,out _);
            var right=Vector3.Cross(Vector3.up,heading);
            car.view.transform.position=eye+Vector3.up*2.1f-right*1.6f;
            car.view.transform.LookAt(edge.At(edge.Length,out _)+Vector3.up*1.6f+right*.8f);
            car.view.fieldOfView=62;
            JejuDriveBuilder.Capture(car.view,Path.Combine(Output,filename));
        }

        static void CheckParisNeighbourhoods(JejuDriveController car,List<string> report)
        {
            var traffic=car.ambient;
            car.guidedDrive=false;
            traffic.spawning=true;
            for(int station=0;station<car.route.stations.Length;station++)
            {
                car.JumpToStation(station);
                for(int frame=0;frame<480;frame++)car.Simulate(0,0,false,.05f);
                int destinations=traffic.vehicles.Where(v=>v.active).Select(v=>v.destination).Distinct().Count();
                if(traffic.ActiveCount<8||destinations<4||!traffic.vehicles.Any(v=>v.active&&v.travelled>25))
                    throw new InvalidOperationException("Paris traffic did not populate station "+(station+1));
                report.Add($"Station{station+1}={traffic.ActiveCount} vehicles, {destinations} destinations");
                if(station!=6)continue;
                car.cameraMode=0;
                car.UpdateCamera(true);
                JejuDriveBuilder.Capture(car.view,Path.Combine(Output,"paris-champs-traffic.png"));
            }
            int before=traffic.ActiveCount;
            int recycled=traffic.Recycled;
            traffic.spawning=false;
            car.transform.position+=Vector3.right*2000;
            traffic.Simulate(.05f);
            if(traffic.ActiveCount!=0||traffic.Recycled-recycled!=before)
                throw new InvalidOperationException("Paris vehicles were not returned to the pool.");
            report.Add("DistanceRecycling=PASS");
        }

        static void CheckParisTrafficGuide(JejuDriveController car,List<string> report)
        {
            car.ambient.spawning=true;
            int start=car.route.stations[0].index;
            car.route.stations[0].index=0;
            car.JumpToStation(0);
            car.route.stations[0].index=start;
            car.guidedDrive=true;
            float last=car.Progress;
            int idle=0;
            for(int step=0;step<75000&&car.Progress<car.route.Length-42;step++)
            {
                car.Simulate(0,0,false,.05f);
                if(car.Progress>last+1){last=car.Progress;idle=0;}
                if(++idle>3200)
                {
                    var traffic=car.ambient;
                    float stop=car.traffic.StopDistance(car.transform.position,car.transform.forward,out var aspect);
                    report.Add($"Stall={car.Progress:F1}; Position={car.transform.position}; Forward={car.transform.forward}; Signal={aspect}/{stop:F2}; GuideGap={traffic.GuidedClearance(car.route,car.Progress):F2}; Circle={traffic.GuidedCircleStop:F2}");
                    report.AddRange(traffic.vehicles.Where(v=>v.active&&Vector3.Distance(v.position,car.transform.position)<50)
                        .Select(v=>$"NPC={v.body.name}; Position={v.position}; Forward={v.heading}; Edge={v.path[v.leg]}; Next={(v.leg+1<v.path.Count?v.path[v.leg+1]:-1)}; Turn={v.inTurn}; Reserve={v.reservedNode}; Speed={v.speed:F2}; Waiting={v.waiting:F1}; Distance={v.distance:F2}"));
                    throw new InvalidOperationException("Paris traffic stalled the guide.");
                }
            }
            if(car.Progress<car.route.Length-42)
                throw new InvalidOperationException($"Paris guide only reached {car.Progress:F1} m.");
            report.Add($"GuidedDriveWithTraffic=PASS ({car.route.Length:F1} m)");
        }
    }
}
