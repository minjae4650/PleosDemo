using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Parkside.Jeju;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Finish=Parkside.Streets.Editor.StreetFinishGeometry;
using Geo=Parkside.Jeju.Editor.JejuDriveBuilder.Geo;
using Object=UnityEngine.Object;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        const string TrafficRoot="Paris_Traffic_and_Signals";
        const string TrafficNetworkPath=Folder+"/Generated/ParisTrafficNetwork.json";

        public static void RefreshTrafficBatch()
        {
            try
            {
                Directory.CreateDirectory(Output);
                var scene=EditorSceneManager.OpenScene(ScenePath);
                var root=scene.GetRootGameObjects().Single(go=>go.name=="Paris_Eiffel_Drive").transform;
                var previous=root.Find(TrafficRoot);
                if(previous!=null)Object.DestroyImmediate(previous.gameObject);
                BuildParisTraffic(root);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))
                    throw new IOException("Could not save Paris traffic.");
                VerifyTrafficPlay();
            }
            catch(Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        static void BuildParisTraffic(Transform root)
        {
            var player=root.GetComponentInChildren<JejuDriveController>();
            var raw=Asset<TextAsset>(Folder+"/Data/ParisTrafficSource.json");
            var network=JsonUtility.FromJson<JejuTrafficNetwork>(raw.text);
            Physics.SyncTransforms();
            FitTrafficToRoad(network);
            var parent=Group(TrafficRoot,root);
            var signals=parent.gameObject.AddComponent<JejuTrafficSystem>();
            MakeParisSignals(network,signals,parent);
            File.WriteAllText(TrafficNetworkPath,JsonUtility.ToJson(network));
            AssetDatabase.ImportAsset(TrafficNetworkPath,ImportAssetOptions.ForceSynchronousImport);

            var ambient=Group("Recycled_neighbourhood_vehicles",parent).gameObject.AddComponent<JejuAmbientTraffic>();
            ambient.networkSource=Asset<TextAsset>(TrafficNetworkPath);
            ambient.vehiclePrefabs=new[]{"tucson","i30","azera"}.Select(name=>Asset<GameObject>(
                "Assets/_Parkside/JejuDrive/Generated/Prefabs/JejuTraffic_hyundai-"+name+"-lod1.prefab")).ToArray();
            ambient.signals=signals;
            ambient.player=player;
            ambient.population=28;
            ambient.spawnMin=58;
            ambient.spawnMax=330;
            ambient.despawnDistance=495;
            ambient.destinationMaxDistance=1250;
            player.traffic=signals;
            player.ambient=ambient;
            Debug.Log($"Paris traffic authored: {network.edges.Length} directed lanes, {signals.junctions.Length} signal groups, {signals.approaches.Length} approaches.");
        }

        static void FitTrafficToRoad(JejuTrafficNetwork network)
        {
            network.Prepare();
            network.preserveTrim=true;
            network.checkTurnFootprints=true;
            var original=network.edges;
            var index=Enumerable.Repeat(-1,original.Length).ToArray();
            var valid=new List<JejuTrafficEdge>();
            for(int i=0;i<original.Length;i++)
            {
                var edge=original[i];
                if(edge.Length<.45f||!JejuAmbientTraffic.FitsRoad(edge.lane))continue;
                index[i]=valid.Count;
                valid.Add(edge);
            }
            network.edges=valid.ToArray();
            foreach(var edge in network.edges)
                edge.allowedNext=edge.allowedNext.Where(i=>index[i]>=0&&
                    JejuAmbientTraffic.FitsRoad(JejuAmbientTraffic.Connection(edge,original[i],compact:true)))
                    .Select(i=>index[i]).ToArray();
            if(network.edges.Length<1000)throw new InvalidDataException("Too few Paris lanes fit the scene road.");
        }

        static void MakeParisSignals(JejuTrafficNetwork network,JejuTrafficSystem system,Transform parent)
        {
            var lights=Surface("Paris_signal_lens",Color.black,smooth:.35f,shader:"Standard");
            lights.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(lights);
            var poles=new Geo();
            var housings=new Geo();
            var markings=new Geo();
            var junctions=new List<JejuJunction>();
            var approaches=new List<JejuSignalApproach>();
            var incoming=network.edges.Where(e=>e.mappedSignal&&!e.roundabout&&
                e.Length>7f&&e.allowedNext.Length>0).ToLookup(e=>e.toNode);
            var nearby=network.edges.Where(e=>e.distances.Last()<30&&incoming.Contains(e.fromNode)&&incoming.Contains(e.toNode))
                .SelectMany(e=>new[]{(node:e.fromNode,other:e.toNode),(node:e.toNode,other:e.fromNode)})
                .ToLookup(link=>link.node,link=>link.other);
            var seen=new HashSet<int>();
            foreach(var entrance in incoming)
            {
                if(!seen.Add(entrance.Key))continue;
                var cluster=new List<int>{entrance.Key};
                for(int i=0;i<cluster.Count;i++)
                    foreach(int other in nearby[cluster[i]])
                        if(seen.Add(other))cluster.Add(other);
                var arms=cluster.SelectMany(node=>incoming[node]).OrderByDescending(e=>e.width).ToArray();
                var main=arms[0];
                main.At(main.Length,out var axis);
                var group=Group("OSM_"+network.nodes[entrance.Key].osmId,parent);
                var lamps=Enumerable.Range(0,6).Select(_=>new List<Renderer>()).ToArray();
                int count=approaches.Count;
                foreach(var edge in arms)
                {
                    const float inset=1.2f;
                    var lanePoint=edge.At(edge.Length-inset,out var forward);
                    var right=Vector3.Cross(Vector3.up,forward);
                    float width=edge.oneWay?edge.width:edge.width*.5f;
                    float offset=Vector3.Dot(lanePoint-JejuTrafficNetwork.Sample(edge.points,edge.distances,
                        edge.distances.Last()-edge.trimEnd-inset,out _),right);
                    var line=lanePoint+right*((edge.oneWay?0:width*.5f)-offset);
                    if(!TryPlaceSignal(line,forward,right,width,out var pole))continue;
                    if(!PaintStopLine(markings,line,forward,right,width))continue;
                    bool cross=Mathf.Abs(Vector3.Dot(forward,axis))<.65f;
                    AddParisSignalHead(group,pole,forward,lights,poles,housings,lamps,cross?3:0);
                    approaches.Add(new JejuSignalApproach{junction=junctions.Count,cross=cross,line=line,
                        forward=forward,width=width,stopBeforeEdgeEnd=inset});
                    edge.signalApproach=approaches.Count;
                }
                if(approaches.Count==count)
                {
                    Object.DestroyImmediate(group.gameObject);
                    continue;
                }
                junctions.Add(new JejuJunction{name=main.name,centre=network.nodes[entrance.Key].point,forward=axis,
                    width=main.width,phaseOffset=(junctions.Count*13)%64,
                    mainRed=lamps[0].ToArray(),mainAmber=lamps[1].ToArray(),mainGreen=lamps[2].ToArray(),
                    crossRed=lamps[3].ToArray(),crossAmber=lamps[4].ToArray(),crossGreen=lamps[5].ToArray()});
            }
            if(junctions.Count<20)throw new InvalidDataException("Too few mapped Paris intersections could be fitted.");
            system.junctions=junctions.ToArray();
            system.approaches=approaches.ToArray();
            system.RefreshLights();
            Save(poles,"Roadside_signal_posts",Surface("Paris_signal_post",Color.white),parent,true);
            Save(housings,"Vertical_signal_housings",Surface("Paris_signal_housing",Color.white),parent,true);
            Save(markings,"Traffic_stop_lines",Surface("Paris_traffic_paint",Color.white),parent);
        }

        static bool TryPlaceSignal(Vector3 line,Vector3 forward,Vector3 right,float width,out Vector3 pole)
        {
            foreach(float shift in new[]{1f,-.7f,-2f})
                for(float side=width*.5f+.35f;side<=width*.5f+3.4f;side+=.35f)
                {
                    pole=line+forward*shift+right*side;
                    if(!Finish.RoadPoint(pole,out _)&&!Finish.RoadPoint(pole-right*.18f,out _))
                    {
                        pole.y=-.04f;
                        return true;
                    }
                }
            pole=default;
            return false;
        }

        static bool PaintStopLine(Geo paint,Vector3 line,Vector3 forward,Vector3 right,float width)
        {
            bool paved=false;
            int tiles=Mathf.Max(2,Mathf.CeilToInt(width));
            float tile=(width-.42f)/tiles;
            for(int i=0;i<tiles;i++)
            {
                var p=line+right*((i+.5f-tiles*.5f)*tile);
                paved|=Finish.RoadPatch(paint,p,forward,tile,.28f);
            }
            return paved;
        }

        static void AddParisSignalHead(Transform parent,Vector3 pole,Vector3 forward,Material material,
            Geo posts,Geo housings,List<Renderer>[] lamps,int phase)
        {
            var green=new Color(.21f,.29f,.25f);
            var dark=new Color(.095f,.106f,.107f);
            var rotation=Quaternion.LookRotation(forward);
            var head=pole+Vector3.up*2.98f-forward*.13f;
            posts.Rod(pole,pole+Vector3.up*3.29f,.063f,green,10);
            posts.Rod(pole,pole+Vector3.up*.26f,.093f,green*.8f,10);
            housings.Box(head,new Vector3(.38f,.96f,.22f),rotation,dark);
            for(int lamp=0;lamp<3;lamp++)
            {
                var p=head+Vector3.up*(.29f-lamp*.29f)-forward*.126f;
                var lens=GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lens.name=lamp==0?"Red":lamp==1?"Amber":"Green";
                lens.transform.SetParent(parent,false);
                lens.transform.SetPositionAndRotation(p,rotation);
                lens.transform.localScale=new Vector3(.235f,.235f,.065f);
                Object.DestroyImmediate(lens.GetComponent<Collider>());
                var renderer=lens.GetComponent<Renderer>();
                renderer.sharedMaterial=material;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                lamps[phase+lamp].Add(renderer);
                housings.Box(p+Vector3.up*.135f-forward*.066f,new Vector3(.27f,.045f,.23f),rotation,dark);
            }
        }
    }
}
