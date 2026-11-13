using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Parkside.Jeju.Editor
{
 public static partial class JejuDriveBuilder
 {
    static float PaintedRoadHeight(Vector3 p)
    {
        if(Physics.Raycast(p+Vector3.up*15,Vector3.down,out var hit,30)&&hit.collider.name=="Asphalt")return hit.point.y+.035f;
        return p.y+.035f;
    }
    static void PaintedFace(Geo g,Vector3 a,Vector3 b,Vector3 c,Vector3 d)
    {
        a.y=PaintedRoadHeight(a);b.y=PaintedRoadHeight(b);c.y=PaintedRoadHeight(c);d.y=PaintedRoadHeight(d);
        g.Face(a,b,c,d,Color.white);
    }
    static void PaintRect(Geo g,Vector3 centre,Vector3 forward,float width,float length)
    {
        Vector3 right=Vector3.Cross(Vector3.up,forward).normalized;
        int cols=Mathf.Max(1,Mathf.CeilToInt(width/1.2f)),rows=Mathf.Max(1,Mathf.CeilToInt(length/1.2f));
        for(int z=0;z<rows;z++)for(int x=0;x<cols;x++)
        {
            Vector3 a=centre+right*(width*(x/(float)cols-.5f))+forward*(length*(z/(float)rows-.5f));
            Vector3 b=a+right*width/cols,c=a+forward*length/rows,d=b+forward*length/rows;PaintedFace(g,a,b,c,d);
        }
    }
    static void RoadArrow(Geo g,Vector3 p,Vector3 forward,bool turn)
    {
        forward.y=0;forward.Normalize();Vector3 right=Vector3.Cross(Vector3.up,forward);
        PaintRect(g,p-forward*.45f,forward,.18f,2.1f);
        Vector3 tip=p+forward*1.35f,a=p+forward*.5f-right*.58f,b=p+forward*.5f+right*.58f;
        tip.y=PaintedRoadHeight(tip);a.y=PaintedRoadHeight(a);b.y=PaintedRoadHeight(b);
        int n=g.v.Count;g.Vertex(a,Vector2.zero,Color.white);g.Vertex(b,Vector2.right,Color.white);g.Vertex(tip,Vector2.up,Color.white);g.t.AddRange(new[]{n,n+2,n+1});
        if(turn){PaintRect(g,p-forward*.5f+right*.4f,right,.18f,.8f);PaintRect(g,p-forward*.2f+right*.8f,forward,.18f,.7f);}
    }
    static void MakeSignalJunctions()
    {
        var parent=Group("Signalised_Village_Junctions",root);var traffic=parent.gameObject.AddComponent<JejuTrafficSystem>();var junctions=new List<JejuJunction>();
        if(data.signals==null){traffic.junctions=junctions.ToArray();return;}
        Directory.CreateDirectory(SignalMeshes);
        Physics.SyncTransforms();
        for (int number=0;number<data.signals.Length;number++)
            junctions.Add(AuthorSignal(parent,number));
        traffic.junctions=junctions.ToArray();traffic.SetClock(0);
        ValidateUrbanSignals(traffic);
        File.WriteAllText(Path.Combine(Output,"traffic-report.txt"),"ControlledJunctionNodes="+junctions.Count+"\nPlacement=Actual OSM junction nodes; simulated signals and timing\nCycle=MainGreen30 Amber4 AllRed3 CrossGreen20 Amber4 AllRed3\nStopLines=True\nCrosswalks=True\nLaneArrows=True\nGuidedDrivingObeysSignals=True");
    }

    const string SignalMeshes=Folder+"/Generated/CitySignalMeshes";
    static bool IsUrbanSignal(SignalSource signal)=>signal.placement=="osm_traffic_signals";
    static bool HasIncomingArms(SignalSource signal)=>signal.arms!=null&&signal.arms.Any(a=>a.incoming);

    static JejuJunction AuthorSignal(Transform parent,int number)
    {
        var signal=data.signals[number];
        int i=Mathf.Clamp(signal.index,1,data.points.Length-3);
        Vector3 centre=GradedRoad(signal.arms!=null?signal.centre:data.points[i]);
        Vector3 forward=data.points[i+2]-data.points[i-1];
        float width=data.widths[i],phase=number*9;
        if (IsUrbanSignal(signal) && HasIncomingArms(signal))
        {
            // Paired OSM nodes on opposite carriageways represent one controller.
            int first=Array.FindIndex(data.signals,s=>IsUrbanSignal(s)&&HasIncomingArms(s)&&(s.centre-centre).sqrMagnitude<14*14);
            var control=data.signals[first];
            var main=control.arms.Where(a=>a.incoming).OrderByDescending(a=>a.lanes)
                .ThenByDescending(a=>a.width).First();
            forward=-main.outward;
            width=signal.arms.Max(a=>a.width);
            phase=first*9;
        }
        forward.y=0;
        forward.Normalize();
        var group=Group("Junction_"+number+"_OSM_"+signal.osmNodeId,parent);
        var markings=new Geo();var poles=new Geo();var frames=new Geo();
        var mainRed=new List<Renderer>();var mainAmber=new List<Renderer>();var mainGreen=new List<Renderer>();
        var crossRed=new List<Renderer>();var crossAmber=new List<Renderer>();var crossGreen=new List<Renderer>();
        var arms=signal.arms??new[]{new ArmSource{outward=forward,width=width,incoming=true},new ArmSource{outward=-forward,width=width,incoming=true}};
        foreach(var arm in arms)
        {
            if(!arm.incoming)continue;
            Vector3 approach=-arm.outward;float armWidth=arm.width;
            bool main=Mathf.Abs(Vector3.Dot(approach,forward))>.65f;
            Vector3 stop=centre-approach*16;
            float laneOffset=arm.oneWay?0:armWidth*.25f,paintWidth=arm.oneWay?armWidth:armWidth*.5f;
            PaintRect(markings,stop+Vector3.Cross(Vector3.up,approach)*laneOffset,approach,paintWidth-.3f,.4f);
            Vector3 zebra=centre-approach*12;
            for(float x=-armWidth*.5f+.3f;x<armWidth*.5f-.3f;x+=.9f)PaintRect(markings,zebra+Vector3.Cross(Vector3.up,approach)*x,approach,.45f,3.4f);
            RoadArrow(markings,centre-approach*34+Vector3.Cross(Vector3.up,approach)*Mathf.Min(1.8f,laneOffset),approach,false);
            BuildSignalHead(centre+approach*19-Vector3.Cross(Vector3.up,approach)*(armWidth*.5f+1),approach,armWidth,group,poles,frames,
                main?mainRed:crossRed,main?mainAmber:crossAmber,main?mainGreen:crossGreen);
        }
        string meshPrefix=IsUrbanSignal(signal)?SignalMeshes+"/"+signal.osmNodeId+"_":null;
        SaveSignalLayer(markings,"Stop_lines_zebras_and_lane_arrows",roadPaint,group,false,meshPrefix);
        SaveSignalLayer(poles,"Galvanised_signal_masts",metal,group,true,meshPrefix);
        SaveSignalLayer(frames,"Signal_housings_and_visors",basalt,group,true,meshPrefix);
        return new JejuJunction{name=string.IsNullOrWhiteSpace(signal.name)?"마을 교차로":signal.name,centre=centre,forward=forward,width=width,distance=signal.distance,phaseOffset=phase,
            mainRed=mainRed.ToArray(),mainAmber=mainAmber.ToArray(),mainGreen=mainGreen.ToArray(),crossRed=crossRed.ToArray(),crossAmber=crossAmber.ToArray(),crossGreen=crossGreen.ToArray()};
    }

    static void SaveSignalLayer(Geo mesh,string name,Material mat,Transform group,bool shadow,string prefix)
    {
        mesh.Save(name,mat,group,shadow,assetPath:prefix==null?null:prefix+name+".asset");
    }

    static void RefreshUrbanSignalHeads(JejuDriveController car)
    {
        Directory.CreateDirectory(SignalMeshes);
        roadPaint=Load<Material>(Folder+"/Generated/Materials/WornRoadPaint.mat");
        metal=Load<Material>(Folder+"/Generated/Materials/Guardrail.mat");
        basalt=Load<Material>(Folder+"/Generated/Materials/JejuBasalt.mat");
        lampMaterial=Load<Material>(Folder+"/Generated/Materials/SignalLens.mat");
        var traffic=car.traffic;
        if (traffic.junctions.Length!=data.signals.Length)
            throw new InvalidOperationException("Scene and OSM signal node counts differ.");
        Physics.SyncTransforms();
        for (int i=0;i<data.signals.Length;i++)
        {
            var signal=data.signals[i];
            if (!IsUrbanSignal(signal)) continue;
            var group=traffic.transform.Find("Junction_"+i+"_OSM_"+signal.osmNodeId);
            if (group!=null) Object.DestroyImmediate(group.gameObject);
            traffic.junctions[i]=AuthorSignal(traffic.transform,i);
        }
        traffic.SetClock(0);
        ValidateUrbanSignals(traffic);
    }

    static void ValidateUrbanSignals(JejuTrafficSystem traffic)
    {
        int nodes=0,paired=0;
        for (int i=0;i<data.signals.Length;i++)
        {
            if (!IsUrbanSignal(data.signals[i])) continue;
            nodes++;
            var signal=traffic.junctions[i];
            for (int j=0;j<i;j++)
            {
                if (!IsUrbanSignal(data.signals[j])) continue;
                var other=traffic.junctions[j];
                if ((signal.centre-other.centre).sqrMagnitude>=14*14) continue;
                if (signal.phaseOffset!=other.phaseOffset || Mathf.Abs(Vector3.Dot(signal.forward,other.forward))<.99f)
                    throw new InvalidOperationException("Split city junction has conflicting controllers.");
                paired++;
            }
        }
        Directory.CreateDirectory(Path.Combine(Output,"seogwipo"));
        File.WriteAllText(Path.Combine(Output,"seogwipo","signal-report.txt"),
            "OSMSignalNodes="+nodes+"\nSharedPhasePairs="+paired);
    }

    static void BuildSignalHead(Vector3 poleBase,Vector3 direction,float width,Transform parent,Geo poles,Geo housings,List<Renderer> red,List<Renderer> amber,List<Renderer> green)
    {
        poleBase.y=DetailHeight(poleBase.x,poleBase.z);
        Vector3 right=Vector3.Cross(Vector3.up,direction),mast=poleBase+Vector3.up*5.5f;
        poles.Rod(poleBase,mast,.12f,Color.white,9);poles.Rod(mast,mast+right*(width*.5f+1),.09f,Color.white,7);
        Vector3 head=mast+right*(width*.35f+1)-direction*.12f;
        var q=Quaternion.LookRotation(direction);
        housings.Box(head,new Vector3(1.45f,.48f,.25f),q,Color.white);
        for(int lamp=0;lamp<3;lamp++)
        {
            Vector3 pos=head+right*(lamp*.43f-.43f)-direction*.15f;
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name=lamp==0?"Signal_red":lamp==1?"Signal_amber":"Signal_green";go.transform.SetParent(parent,false);go.transform.SetPositionAndRotation(pos,q);go.transform.localScale=new Vector3(.28f,.28f,.05f);
            Object.DestroyImmediate(go.GetComponent<Collider>());var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=lampMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            if(lamp==0)red.Add(renderer);else if(lamp==1)amber.Add(renderer);else green.Add(renderer);
            housings.Box(pos+Vector3.up*.18f-direction*.06f,new Vector3(.34f,.06f,.23f),q,Color.white);
        }
    }
 }
}
