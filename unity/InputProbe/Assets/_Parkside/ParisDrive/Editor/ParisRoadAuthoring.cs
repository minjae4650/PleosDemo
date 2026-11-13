using UnityEditor;
using UnityEngine;
using System.Linq;
using Geo=Parkside.Jeju.Editor.JejuDriveBuilder.Geo;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        static void Triangle(Geo g,Vector3 a,Vector3 b,Vector3 c,Color tint,float scale)
        {
            if (Vector3.Cross(b-a,c-a).y<0) (b,c)=(c,b);
            int i=g.v.Count;
            foreach (var p in new[]{a,b,c}) g.Vertex(p,new Vector2(p.x,p.z)*scale,tint);
            g.t.Add(i);g.t.Add(i+1);g.t.Add(i+2);
        }

        static Geo SurfaceMesh(Vector3[] positions,float scale,Color colour)
        {
            var g=new Geo();
            for(int i=0;i<positions.Length;i+=3) Triangle(g,positions[i],positions[i+1],positions[i+2],colour,scale);
            return g;
        }

        static void BuildLandscape(Source s,Transform root)
        {
            var landscape=Group("Seine_and_Parks",root);
            var paving=Surface("StonePaving",new Color(.78f,.75f,.69f),"KoreanAssets/concrete_pavement_diff_2k.jpg");
            var grass=Surface("ParkGrass",new Color(.60f,.69f,.46f),"KoreanAssets/grass_ground_diff_2k.jpg");
            var water=Surface("SeineWater",new Color(.17f,.32f,.33f),smooth:.56f,shader:"PARKSIDE/Paris/Seine");
            var land=Surface("CityGround",new Color(.62f,.61f,.57f),"KoreanAssets/concrete_pavement_diff_2k.jpg");
            Save(SurfaceMesh(s.land,.14f,Color.white),"Paris_land",land,landscape);
            var river=Save(SurfaceMesh(s.water,.04f,Color.white),"Seine",water,landscape);
            var riverMesh=river.GetComponent<MeshFilter>().sharedMesh;
            riverMesh.RecalculateTangents();
            EditorUtility.SetDirty(riverMesh);
            Save(SurfaceMesh(s.parks,.20f,Color.white),"Paris_gardens_and_lawns",grass,landscape);
            Save(SurfaceMesh(s.pedestrianBridge,.2f,Color.white),"Pont_d_Iena_pedestrian_deck",paving,landscape);
            var wall=new Geo();
            var rail=new Geo();
            foreach (var bank in s.banks)
                for (int i=0;i<bank.points.Length-1;i++)
                {
                    var a=bank.points[i]; var b=bank.points[i+1];
                    if ((a-b).sqrMagnitude<.01f) continue;
                    wall.Face(a-Vector3.up*4,b-Vector3.up*4,a-Vector3.up*.20f,b-Vector3.up*.20f,Limestone);
                    wall.Face(b-Vector3.up*4,a-Vector3.up*4,b-Vector3.up*.20f,a-Vector3.up*.20f,Limestone);
                }
            for(int i=0;i<s.bridgeOutline.Length-1;i++)
            {
                var a=s.bridgeOutline[i];var b=s.bridgeOutline[i+1];
                float length=Vector3.Distance(a,b);
                if(length<.25f) continue;
                wall.Face(a-Vector3.up*2.6f,b-Vector3.up*2.6f,a,b,Limestone);
                wall.Face(b-Vector3.up*2.6f,a-Vector3.up*2.6f,b,a,Limestone);
                if(length<18) continue;
                rail.Box((a+b)*.5f+Vector3.up*.57f,new Vector3(.39f,1.14f,length),Quaternion.LookRotation(b-a),Limestone);
            }
            Save(wall,"Quay_revetments",paving,landscape);
            Save(rail,"Iena_stone_parapets",paving,landscape);
            BuildBridgeDecks(s,paving,landscape);
            var paths=new Geo();
            Vector3 axis=new Vector3(.62f,0,-.7846f).normalized,right=Vector3.Cross(Vector3.up,axis);
            for(int side=-1;side<=1;side+=2)
            {
                var a=axis*78+right*(side*23);var b=axis*505+right*(side*23);
                a.y=b.y=-.145f;
                RoadLine(paths,a,b,2.1f,new Color(.96f,.85f,.69f));
            }
            Save(paths,"Champ_de_Mars_promenoirs",paving,landscape);
        }

        static void BuildBridgeDecks(Source s,Material paving,Transform parent)
        {
            var group=Group("Bridge_stone_decks_and_parapets",parent);
            var edges=new Geo();
            var rail=new Geo();
            foreach(var deck in s.bridgeDecks)
            {
                Save(SurfaceMesh(deck.mesh,.36f,Color.white),"Bridge_deck_"+deck.name,paving,group);
                var endA=deck.points.OrderBy(p=>p.z).First();
                var endB=deck.points.OrderByDescending(p=>p.z).First();
                var heading=(endB-endA).normalized;
                if(deck.name=="Concorde")
                {
                    var middle=(endA+endB)*.5f;
                    float span=Vector3.Distance(endA,endB);
                    var rotation=Quaternion.LookRotation(heading);
                    for(int pier=1;pier<=4;pier++)
                        edges.Box(middle+heading*(span*(pier/5f-.5f))-Vector3.up*2.57f,
                            new Vector3(16.4f,4.3f,2.9f),rotation,CarvedLimestone);
                }
                for(int i=0;i<deck.points.Length-1;i++)
                {
                    var a=deck.points[i];var b=deck.points[i+1];
                    float length=Vector3.Distance(a,b);
                    if(length<.12f) continue;
                    Vector3 f=(b-a)/length;
                    edges.Face(a-Vector3.up*.95f,b-Vector3.up*.95f,a,b,Limestone);
                    edges.Face(b-Vector3.up*.95f,a-Vector3.up*.95f,b,a,Limestone);
                    if(Mathf.Abs(Vector3.Dot(f,heading))<.66f) continue;
                    var q=Quaternion.LookRotation(f);
                    edges.Box((a+b)*.5f+Vector3.up*.15f,new Vector3(.31f,.33f,length),q,Limestone);
                    if(deck.name=="Alma"||deck.name=="Concorde")
                    {
                        edges.Box((a+b)*.5f+Vector3.up*.49f,new Vector3(.22f,.54f,length),q,Limestone);
                        continue;
                    }
                    var iron=new Color(.27f,.35f,.33f);
                    rail.Box((a+b)*.5f+Vector3.up*.98f,new Vector3(.075f,.072f,length),q,iron);
                    for(float d=.2f;d<length;d+=.42f)
                    {
                        var p=a+f*d+Vector3.up*.28f;
                        rail.Rod(p,p+Vector3.up*.69f,.018f,iron,5);
                    }
                }
            }
            Save(edges,"Decks_and_stone_fascia",paving,group);
            Save(rail,"Wrought_iron_bridge_parapets",Surface("BridgeRail",Color.white),group);
        }

        static void BuildRoads(Source s,Transform root)
        {
            var roads=Group("OSM_Streets_and_Bridges",root);
            var asphalt=Surface("Asphalt",new Color(.58f,.60f,.61f),"RealismAssets/clean_asphalt_diff_2k.jpg");
            var pavement=Surface("Sidewalk",new Color(.80f,.77f,.71f),"KoreanAssets/concrete_pavement_diff_2k.jpg");
            var paint=Surface("WhitePaint",Color.white);
            Save(SurfaceMesh(s.roadSurface,.24f,Color.white),"Asphalt",asphalt,roads,collider:true);
            Save(SurfaceMesh(s.sidewalks,.42f,Color.white),"Paris_sidewalks",pavement,roads);
            var stone=StoneSetts();
            Save(SurfaceMesh(s.stoneRoads,.70f,Color.white),"Mapped_stone_road_surface",stone,roads);
            var lines=new Geo();
            foreach (var road in s.roads)
            {
                float cumulative=0;
                for(int i=0;i<road.points.Length-1;i++)
                {
                    var a=road.points[i];var b=road.points[i+1];float metres=Vector3.Distance(a,b);
                    if(metres<.3f) continue;
                    var forward=(b-a)/metres;
                    var right=Vector3.Cross(Vector3.up,forward);
                    if (road.lanes>1)
                        for(int lane=1;lane<road.lanes;lane++)
                        {
                            float lateral=road.width*(lane/(float)road.lanes-.5f);
                            for(float d=10-Mathf.Repeat(cumulative,10);d+3<metres;d+=10)
                            {
                                var p=a+forward*d+right*lateral+Vector3.up*.02f;
                                RoadLine(lines,p,p+forward*3,.12f,White);
                            }
                        }
                    cumulative+=metres;
                }
            }
            foreach(var signal in s.signals)
            {
                foreach(var arm in signal.arms)
                {
                    var f=(arm-signal.point).normalized;
                    var r=Vector3.Cross(Vector3.up,f);
                    var centre=signal.point+f*4.3f+Vector3.up*.023f;
                    for(int stripe=-3;stripe<=3;stripe++)
                        RoadLine(lines,centre+r*stripe*.57f-f*1.1f,centre+r*stripe*.57f+f*1.1f,.37f,White);
                }
            }
            Save(lines,"White_lane_guides_and_zebras",paint,roads);
        }

        static Material StoneSetts() => Surface("Stone_setts",new Color(.385f,.408f,.415f),
            "KoreanAssets/concrete_pavement_diff_2k.jpg",.16f);

        static void RoadLine(Geo mesh,Vector3 a,Vector3 b,float width,Color tint)
        {
            var right=Vector3.Cross(Vector3.up,(b-a).normalized)*(width*.5f);
            mesh.Face(a-right,a+right,b-right,b+right,tint);
        }

    }
}
