using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Parkside.Jeju.Editor
{
    public static class JejuTrafficVerification
    {
        public static void Check(JejuDriveController car)
        {
            var traffic=car.ambient;if(traffic==null)throw new InvalidOperationException("Ambient traffic missing");traffic.Prepare();
            Debug.Log("Jeju NPC verification: lane surfaces");
            var network=traffic.Network;var lines=new List<string>();var failures=new List<string>();int checks=0,connections=0;
            Physics.SyncTransforms();
            for(int i=0;i<network.edges.Length;i++)
            {
                var e=network.edges[i];
                for(float d=0;d<e.Length;d+=8)
                {
                    Vector3 p=e.At(d,out var f);CheckLaneSurface(p,f,"edge="+i+" distance="+d,failures);checks+=3;
                }
                foreach(int next in e.allowedNext)
                {
                    var path=JejuAmbientTraffic.Connection(e,network.edges[next]);
                    for(int k=0;k<path.Length;k++)
                    {
                        var f=(path[Mathf.Min(k+1,path.Length-1)]-path[Mathf.Max(0,k-1)]).normalized;
                        CheckLaneSurface(path[k],f,"turn="+i+"->"+next,failures);checks+=3;
                    }
                    connections++;
                }
            }
            File.WriteAllLines(Path.Combine(JejuDriveBuilder.Output,"traffic-surface-failures.txt"),failures);
            if(failures.Count>0)throw new InvalidOperationException("NPC lane/turn leaves road: "+failures.Count+" samples. "+failures[0]);
            lines.Add("LaneAndTurnSurfaceSamples="+checks);lines.Add("AllowedTurnConnections="+connections);
            for(int s=0;s<car.route.stations.Length;s++)
            {
                Debug.Log("Jeju NPC verification: station "+s);
                car.JumpToStation(s);car.guidedDrive=false;traffic.spawning=true;
                for(int i=0;i<600;i++){car.traffic.Advance(.05f);traffic.Simulate(.05f);CheckSeparation(traffic);}
                int destinations=traffic.vehicles.Where(v=>v.active).Select(v=>v.destination).Distinct().Count();
                if(traffic.ActiveCount<3||destinations<2)throw new InvalidOperationException("Insufficient independent traffic at station "+s+": "+traffic.ActiveCount+" / "+destinations);
                if(!traffic.vehicles.Any(v=>v.active&&v.travelled>30))throw new InvalidOperationException("Traffic did not move at station "+s);
                lines.Add("Station"+s+"Population="+traffic.ActiveCount+";Destinations="+destinations);
                car.cameraMode=0;car.UpdateCamera(true);
                JejuDriveBuilder.Capture(car.view,Path.Combine(JejuDriveBuilder.Output,"npc-station-"+(s+1)+".png"));
            }
            traffic.spawning=false;int active=traffic.ActiveCount,recycled=traffic.Recycled;
            Debug.Log("Jeju NPC verification: recycling and controlled red/green queue");
            car.transform.position+=Vector3.right*3000;traffic.Simulate(.05f);
            if(traffic.ActiveCount!=0||traffic.Recycled-recycled!=active)throw new InvalidOperationException("Distant traffic was not recycled");
            lines.Add("DistanceRecycling=PASS");
            traffic.ResetPopulation();
            int chosen=-1;
            for(int i=0;i<network.edges.Length;i++)
                if(network.nodes[network.edges[i].toNode].signal>=0&&network.edges[i].Length>100&&network.edges[i].allowedNext.Length>0){chosen=i;break;}
            if(chosen<0)throw new InvalidOperationException("No signal approach for traffic fixture");
            var edge=network.edges[chosen];int nextEdge=edge.allowedNext[0];var node=network.nodes[edge.toNode];var signal=car.traffic.junctions[node.signal];
            edge.At(edge.Length,out var heading);bool main=Mathf.Abs(Vector3.Dot(heading,signal.forward))>.65f;
            car.transform.position=node.point+Vector3.right*250;
            for(int i=0;i<2;i++)
            {
                var v=traffic.vehicles[i];v.path.Clear();v.path.Add(chosen);v.path.Add(nextEdge);
                traffic.Activate(v,network.edges[nextEdge].toNode,edge.Length-65-i*23);
            }
            for(int i=0;i<1000;i++)
            {
                car.traffic.SetClock((main?42:5)-signal.phaseOffset);traffic.Simulate(.05f);CheckSeparation(traffic);
            }
            var first=traffic.vehicles[0];var second=traffic.vehicles[1];
            if(first.inTurn||first.leg!=0||first.speed>.15f||second.speed>.15f)throw new InvalidOperationException("NPC red-light queue failed");
            float stop=edge.Length-first.distance+edge.trimEnd;
            if(stop<18||stop>24)throw new InvalidOperationException("NPC stop-line clearance invalid: "+stop);
            lines.Add("NpcRedStopAndQueue=PASS");
            int completed=traffic.CompletedTurns;
            for(int i=0;i<1100;i++)
            {
                car.traffic.SetClock((main?5:42)-signal.phaseOffset);traffic.Simulate(.05f);CheckSeparation(traffic);
            }
            if(traffic.CompletedTurns-completed<2)throw new InvalidOperationException("NPC queue failed to clear green intersection");
            lines.Add("NpcGreenRestartAndTurning=PASS");
            // A stationary player in the same lane must be treated as a leading vehicle.
            traffic.ResetPopulation();var blocked=traffic.vehicles[0];blocked.path.Clear();blocked.path.Add(chosen);blocked.path.Add(nextEdge);
            traffic.Activate(blocked,network.edges[nextEdge].toNode,edge.Length-95);
            car.transform.position=edge.At(edge.Length-65,out heading);car.transform.rotation=Quaternion.LookRotation(heading);
            for(int i=0;i<700;i++){car.traffic.SetClock((main?5:42)-signal.phaseOffset);traffic.Simulate(.05f);}
            if(blocked.speed>.15f||Vector3.Distance(blocked.position,car.transform.position)<5)throw new InvalidOperationException("NPC failed to stop behind player");
            if(car.ambient.PlayerClearance(car.transform.position,-heading)>35)throw new InvalidOperationException("Player obstacle clearance failed");
            lines.Add("PlayerAndNpcObstacleStops=PASS");lines.Add("PoolCapacity="+traffic.vehicles.Count);
            // Entering traffic yields even when it has an earlier pool index.
            traffic.ResetPopulation();bool roundaboutChecked=false;
            for(int ni=0;ni<network.nodes.Length&&!roundaboutChecked;ni++)
            {
                if(!network.nodes[ni].roundabout||network.nodes[ni].signal>=0)continue;
                int entry=Array.FindIndex(network.edges,e=>e.toNode==ni&&!e.roundabout&&e.Length>30&&e.allowedNext.Length>0);
                int circulating=Array.FindIndex(network.edges,e=>e.toNode==ni&&e.roundabout&&e.Length>30&&e.allowedNext.Length>0);
                if(entry<0||circulating<0)continue;
                car.transform.position=network.nodes[ni].point+Vector3.right*250;
                for(int i=0;i<2;i++)
                {
                    int edgeIndex=i==0?entry:circulating;var v=traffic.vehicles[i];v.path.Clear();v.path.Add(edgeIndex);v.path.Add(network.edges[edgeIndex].allowedNext[0]);
                    traffic.Activate(v,network.edges[v.path[1]].toNode,network.edges[edgeIndex].Length-14);v.speed=4;
                }
                for(int i=0;i<300;i++)
                {
                    traffic.Simulate(.05f);CheckSeparation(traffic);
                    if(traffic.vehicles[0].inTurn)throw new InvalidOperationException("Roundabout entry took priority over circulating traffic");
                    if(traffic.vehicles[1].inTurn){roundaboutChecked=true;break;}
                }
                if(!roundaboutChecked)throw new InvalidOperationException("Circulating traffic failed to clear entry");
            }
            if(!roundaboutChecked)throw new InvalidOperationException("No complete roundabout entry fixture");
            lines.Add("CirculatingRoundaboutPriority=PASS");
            lines.Add("TotalSpawned="+traffic.Spawned);lines.Add("CompletedTurns="+traffic.CompletedTurns);
            using(var hash=System.Security.Cryptography.SHA256.Create())lines.Add("NetworkSHA256="+BitConverter.ToString(hash.ComputeHash(traffic.networkSource.bytes)).Replace("-","").ToLowerInvariant());
            File.WriteAllLines(Path.Combine(JejuDriveBuilder.Output,"traffic-play-report.txt"),lines);
            traffic.ResetPopulation();traffic.spawning=true;car.traffic.SetClock(0);car.JumpToStation(0);
            Debug.Log("Jeju NPC verification passed");
        }
        static void CheckSurface(Vector3 p,string label,List<string> failures)
        {
            if(!Physics.Raycast(p+Vector3.up*3,Vector3.down,out var hit,6,~(1<<2),QueryTriggerInteraction.Ignore)||hit.collider.name!="Asphalt")
                failures.Add(label+" at="+p.ToString("F3")+" hit="+(hit.collider==null?"none":hit.collider.name));
        }
        static void CheckLaneSurface(Vector3 p,Vector3 f,string label,List<string> failures)
        {
            var right=Vector3.Cross(Vector3.up,f).normalized*.8f;
            CheckSurface(p,label,failures);CheckSurface(p+right,label+" right",failures);CheckSurface(p-right,label+" left",failures);
        }
        static void CheckSeparation(JejuAmbientTraffic traffic)
        {
            var cars=traffic.vehicles;
            for(int i=0;i<cars.Count;i++)for(int j=i+1;j<cars.Count;j++)
            {
                var a=cars[i];var b=cars[j];if(!a.active||!b.active)continue;
                var delta=b.position-a.position;delta.y=0;if(delta.sqrMagnitude>30)continue;
                Vector3 ar=Vector3.Cross(Vector3.up,a.heading).normalized,br=Vector3.Cross(Vector3.up,b.heading).normalized;
                bool separate=false;
                foreach(var axis in new[]{a.heading,b.heading,ar,br})
                {
                    float extent=2.2f*(Mathf.Abs(Vector3.Dot(axis,a.heading))+Mathf.Abs(Vector3.Dot(axis,b.heading)))+.83f*(Mathf.Abs(Vector3.Dot(axis,ar))+Mathf.Abs(Vector3.Dot(axis,br)));
                    if(Mathf.Abs(Vector3.Dot(delta,axis))>=extent){separate=true;break;}
                }
                if(!separate)throw new InvalidOperationException("Overlapping NPCs "+i+" / "+j+" positions "+a.position+" / "+b.position+" legs "+a.path[a.leg]+" / "+b.path[b.leg]);
            }
        }
    }
}
