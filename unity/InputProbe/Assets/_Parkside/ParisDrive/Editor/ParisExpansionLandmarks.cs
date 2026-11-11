using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Geo=Parkside.Jeju.Editor.JejuDriveBuilder.Geo;
using Finish=Parkside.Streets.Editor.StreetFinishGeometry;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        static readonly Color CarvedLimestone = new Color(.83f,.79f,.71f);
        static readonly Color PaleStone = new Color(.94f,.89f,.79f);
        static readonly Color PatinatedBronze = new Color(.21f,.34f,.29f);
        static readonly Color Gilding = new Color(.91f,.65f,.20f);

        static void BuildExpansionLandmarks(Source source, Transform root)
        {
            var group = Group("Paris_Landmarks", root);
            var limestone = Surface("Landmark_limestone", Color.white,
                "KoreanAssets/concrete_wall_009_diff_2k.jpg", .20f);
            var metal = Surface("Landmark_metal", Color.white, smooth:.35f);
            metal.SetFloat("_Metallic", .25f);
            EditorUtility.SetDirty(metal);
            var arc = LandmarkByKey(source, "arc");
            var invalides = LandmarkByKey(source, "invalides");
            MakeArc(group, arc.centre, limestone, metal);
            MakeInvalides(group, invalides.centre, limestone, metal);
            MakeConcorde(group, LandmarkByKey(source,"obelisk").centre,
                LandmarkByKey(source,"fontaine_nord").centre, LandmarkByKey(source,"fontaine_sud").centre,
                limestone, metal);
        }

        static Landmark LandmarkByKey(Source source, string key)
        {
            var landmark = source.landmarks.FirstOrDefault(item => item.key == key);
            if (landmark == null) throw new InvalidOperationException("Missing OSM landmark: "+key);
            return landmark;
        }

        static void MakeArc(Transform parent, Vector3 centre, Material limestone, Material metal)
        {
            var group = Group("Arc_de_Triomphe", parent);
            var stone = new Geo();
            var relief = new Geo();
            var bronze = new Geo();
            var q = Quaternion.LookRotation(new Vector3(.932f,0,-.363f));
            Vector3 P(float x,float y,float z) => centre + q*new Vector3(x,y,z);
            const float width = 44.82f, depth = 22.21f, opening = 14.62f;
            float foot = (width-opening)*.5f;
            float footCentre = (width+opening)*.25f;
            var pavers = new Geo();
            pavers.Box(P(0,-.10f,0),new Vector3(width+4,.24f,depth+4),q,new Color(.73f,.71f,.66f));
            Save(pavers,"Paved_parvis",Surface("Arc_parvis",Color.white,"KoreanAssets/concrete_pavement_diff_2k.jpg"),
                group);
            for (int side=-1;side<=1;side+=2)
            {
                var cross = q*Quaternion.Euler(0,90,0);
                StoneArch(stone,P(side*footCentre,.02f,0),cross,depth,foot,8.44f,14.46f,22.11f);
                for (int face=-1;face<=1;face+=2)
                {
                    float z=face*(depth*.5f+.18f);
                    stone.Box(P(side*footCentre,1.1f,z),new Vector3(foot+.18f,2.18f,.72f),q,CarvedLimestone);
                    stone.Box(P(side*footCentre,4.16f,z),new Vector3(11.18f,2.4f,.64f),q,PaleStone);
                    stone.Box(P(side*footCentre,11.15f,z),new Vector3(10.25f,11.2f,.54f),q,CarvedLimestone*.92f);
                    for (int figure=-2;figure<=2;figure++)
                    {
                        float x=side*footCentre+figure*1.57f;
                        float height=3.4f+(2-Mathf.Abs(figure))*1.38f;
                        var basePoint=P(x,6.1f+Mathf.Abs(figure)*.47f,z+face*.31f);
                        relief.Rock(basePoint+Vector3.up*(height*.45f),new Vector3(1.0f,height*.44f,.55f),
                            21+figure*4+side*3,CarvedLimestone);
                        relief.Rock(basePoint+Vector3.up*(height*.94f),new Vector3(.48f,.55f,.38f),
                            46+figure,PaleStone);
                        relief.Rod(basePoint+Vector3.up*(height*.58f),
                            basePoint+q*(Vector3.right*(figure<=0?-.83f:.84f))+Vector3.up*(height*.77f),
                            .23f,CarvedLimestone,6);
                    }
                    stone.Box(P(side*footCentre,17.18f,z),new Vector3(11.2f,.36f,.81f),q,PaleStone);
                    stone.Box(P(side*footCentre,22.6f,z),new Vector3(8.7f,4.1f,.34f),q,CarvedLimestone);
                    for(int row=0;row<3;row++)
                        stone.Box(P(side*footCentre,21.38f+row*.83f,z+face*.19f),
                            new Vector3(7.5f,.08f,.04f),q,CarvedLimestone*.73f);
                }
            }

            // A 29.42 m central opening and the crossing 18.68 m side openings.
            StoneArch(stone,centre,q,width,depth,opening,22.11f,34.2f,buildPiers:false);
            stone.Box(P(0,35.06f,0),new Vector3(width+.83f,1.72f,depth+.85f),q,PaleStone);
            stone.Box(P(0,38.29f,0),new Vector3(width+.35f,4.74f,depth+.35f),q,CarvedLimestone);
            stone.Box(P(0,40.86f,0),new Vector3(width+1.1f,.59f,depth+1.20f),q,PaleStone);
            stone.Box(P(0,41.71f,0),new Vector3(width+2.12f,1.24f,depth+2.10f),q,CarvedLimestone);
            stone.Box(P(0,45.7f,0),new Vector3(width+.80f,6.72f,depth+.78f),q,CarvedLimestone);
            stone.Box(P(0,49.29f,0),new Vector3(width+1.19f,.50f,depth+1.18f),q,PaleStone);
            for(int side=-1;side<=1;side+=2)
            {
                float z=side*(depth*.5f+.42f);
                for(int i=-12;i<=12;i++)
                {
                    float x=i*1.69f;
                    stone.Box(P(x,38.1f,z),new Vector3(1.34f,2.64f,.20f),q,CarvedLimestone*.92f);
                    relief.Rock(P(x,38.36f,z+side*.19f),new Vector3(.35f,1.02f,.18f),i+18,PaleStone);
                }
                for(int i=-5;i<=5;i++)
                {
                    float x=i*3.75f;
                    stone.Box(P(x,46.51f,z),new Vector3(2.85f,2.04f,.17f),q,PaleStone*.88f);
                    stone.Box(P(x,46.51f,z+side*.10f),new Vector3(2.58f,1.76f,.038f),q,CarvedLimestone);
                }
            }
            bronze.Box(P(0,.23f,9.8f),new Vector3(2.2f,.28f,1.32f),q,new Color(.24f,.25f,.23f));
            bronze.Rock(P(0,.46f,9.8f),new Vector3(.23f,.28f,.20f),5,new Color(.96f,.56f,.18f));
            Save(stone,"Carved_stone_arches_and_attic",limestone,group,true);
            Save(relief,"Sculpted_relief_panels",limestone,group,true);
            Save(bronze,"Memorial_bronze",metal,group);
        }

        static void StoneArch(Geo stone,Vector3 at,Quaternion q,float width,float depth,float opening,
            float spring,float top,bool buildPiers=true)
        {
            float radius=opening*.5f;
            Vector3 P(float x,float y,float z) => at+q*new Vector3(x,y,z);
            for(int side=-1;side<=1;side+=2)
            {
                float baseY=buildPiers?0:spring;
                stone.Box(P(side*(width+opening)*.25f,(baseY+top)*.5f,0),
                    new Vector3((width-opening)*.5f,top-baseY,depth),q,CarvedLimestone);
            }
            const int segments=28;
            for(int i=0;i<segments;i++)
            {
                float theta=Mathf.PI-i*Mathf.PI/segments;
                float next=Mathf.PI-(i+1)*Mathf.PI/segments;
                float x0=Mathf.Cos(theta)*radius,x1=Mathf.Cos(next)*radius;
                float y0=spring+Mathf.Sin(theta)*radius,y1=spring+Mathf.Sin(next)*radius;
                float z=depth*.5f;
                var a=P(x0,y0,z);var b=P(x1,y1,z);
                var c=P(x0,y0,-z);var d=P(x1,y1,-z);
                stone.Face(b,a,P(x1,top,z),P(x0,top,z),CarvedLimestone);
                stone.Face(c,d,P(x0,top,-z),P(x1,top,-z),CarvedLimestone*.96f);
                stone.Face(c,a,d,b,CarvedLimestone*.79f);
                stone.Face(P(x0,top,z),P(x1,top,z),P(x0,top,-z),P(x1,top,-z),CarvedLimestone);
                for(int side=-1;side<=1;side+=2)
                {
                    float edge=side*(z+.026f);
                    var inner0=P(x0,y0,edge);
                    var inner1=P(x1,y1,edge);
                    var outer0=P(Mathf.Cos(theta)*(radius+.51f),spring+Mathf.Sin(theta)*(radius+.51f),edge);
                    var outer1=P(Mathf.Cos(next)*(radius+.51f),spring+Mathf.Sin(next)*(radius+.51f),edge);
                    var tint=i%3==0?PaleStone:CarvedLimestone;
                    if(side>0) stone.Face(inner1,inner0,outer1,outer0,tint);
                    else stone.Face(inner0,inner1,outer0,outer1,tint);
                }
            }
        }

        static void MakeInvalides(Transform parent,Vector3 centre,Material limestone,Material metal)
        {
            var group=Group("Dome_des_Invalides",parent);
            var body=new Geo();var roof=new Geo();var gilded=new Geo();
            var q=Quaternion.LookRotation(new Vector3(.19f,0,-.98f));
            Vector3 P(float x,float y,float z)=>centre+q*new Vector3(x,y,z);
            body.Box(P(0,1.3f,0),new Vector3(38,2.6f,39),q,CarvedLimestone);
            body.Box(P(0,11.7f,0),new Vector3(35.5f,20.8f,36.4f),q,CarvedLimestone);
            body.Box(P(0,27.2f,0),new Vector3(26.0f,11.3f,28.4f),q,PaleStone);
            body.Box(P(0,22.9f,0),new Vector3(37,.92f,37.8f),q,PaleStone);
            body.Box(P(0,33.15f,0),new Vector3(28.2f,1.17f,30.4f),q,PaleStone);
            var slate=new Color(.25f,.30f,.31f);
            roof.Box(P(0,24.2f,0),new Vector3(35.2f,1.50f,35.7f),q,slate);
            for(int face=0;face<4;face++)
            {
                var turn=q*Quaternion.Euler(0,face*90,0);
                var front=turn*Vector3.forward;
                var side=turn*Vector3.right;
                var entrance=centre+front*18.8f;
                body.Box(entrance+Vector3.up*9.4f,new Vector3(9.5f,18.3f,1.10f),turn,CarvedLimestone*.88f);
                roof.Box(entrance+front*.61f+Vector3.up*4.77f,new Vector3(3.85f,9.0f,.15f),turn,
                    new Color(.20f,.23f,.20f));
                for(int i=-2;i<=2;i++)
                {
                    var b=entrance+side*i*3.37f+front*.53f+Vector3.up*1.6f;
                    body.Rod(b,b+Vector3.up*16.18f,.51f,PaleStone,16);
                    body.Box(b+Vector3.up*15.98f,new Vector3(1.48f,.62f,1.44f),turn,PaleStone);
                    body.Box(b+Vector3.up*.43f,new Vector3(1.43f,.83f,1.46f),turn,CarvedLimestone);
                }
                body.Face(entrance-side*9.5f+Vector3.up*18.4f,entrance+side*9.5f+Vector3.up*18.4f,
                    entrance+Vector3.up*23.4f,entrance+Vector3.up*23.4f,PaleStone);
            }
            Lathe(body,centre,new[]{(34f,14.4f),(35f,14.4f),(36f,13.2f),(49f,13.2f),(50f,14f),(51f,14f)},
                PaleStone);
            for(int i=0;i<16;i++)
            {
                float angle=i*Mathf.PI*2/16;
                var direction=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
                var column=centre+direction*13.15f+Vector3.up*36.3f;
                body.Rod(column,column+Vector3.up*12.1f,.42f,CarvedLimestone,12);
                var pane=centre+Quaternion.Euler(0,11.25f,0)*direction*13.13f+Vector3.up*43.4f;
                roof.Box(pane,new Vector3(1.58f,6.2f,.08f),Quaternion.LookRotation(pane-centre-Vector3.up*43.4f),slate);
            }
            var dome=new[]{(51f,13.6f),(54f,14.02f),(59f,13.7f),(65f,12.55f),(71f,10.3f),
                (77f,7.25f),(82f,4.85f),(85f,3.33f),(86f,2.95f)};
            Lathe(roof,centre,dome,new Color(.31f,.30f,.23f));
            for(int rib=0;rib<24;rib++)
            {
                float angle=rib*Mathf.PI*2/24;
                var dir=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                for(int i=1;i<dome.Length;i++)
                {
                    var (y0,r0)=dome[i-1];var (y1,r1)=dome[i];
                    gilded.Rod(centre+dir*(r0+.045f)+Vector3.up*y0,centre+dir*(r1+.045f)+Vector3.up*y1,
                        .20f,Gilding,6);
                }
            }
            Lathe(gilded,centre,new[]{(85.7f,3.6f),(86.8f,3.6f),(87.5f,2.44f),
                (93.2f,2.44f),(94f,3.25f),(95.1f,2.86f),(98.5f,1.48f),(101f,.28f)},Gilding);
            gilded.Rod(centre+Vector3.up*99.4f,centre+Vector3.up*107,.115f,Gilding,8);
            gilded.Rod(P(-1.6f,104.2f,0),P(1.6f,104.2f,0),.097f,Gilding,6);
            Save(body,"Limestone_portico_and_drum",limestone,group,true);
            Save(roof,"Slate_and_dome",metal,group,true);
            Save(gilded,"Gilded_ribs_and_lantern",metal,group);
        }

        static void MakeConcorde(Transform parent,Vector3 obelisk,Vector3 north,Vector3 south,
            Material limestone,Material metal)
        {
            var group=Group("Place_de_la_Concorde",parent);
            var stone=new Geo();var bronze=new Geo();var basins=new Geo();
            var granite=new Color(.64f,.47f,.40f);
            var q=Quaternion.Euler(0,16,0);
            stone.Box(obelisk+Vector3.up*.18f,new Vector3(8.8f,.37f,8.8f),q,CarvedLimestone);
            stone.Box(obelisk+Vector3.up*1.28f,new Vector3(5.8f,2.1f,5.8f),q,CarvedLimestone);
            stone.Box(obelisk+Vector3.up*3.40f,new Vector3(3.84f,2.85f,3.84f),q,granite*.78f);
            for(int side=0;side<4;side++)
            {
                var face=q*Quaternion.Euler(0,side*90,0);
                var r=face*Vector3.right;var f=face*Vector3.forward;
                Vector3 P(float x,float y,float z)=>obelisk+r*x+Vector3.up*y+f*z;
                stone.Face(P(.925f,4.79f,.925f),P(-.925f,4.79f,.925f),
                    P(.59f,25.25f,.59f),P(-.59f,25.25f,.59f),granite);
                bronze.Face(P(.59f,25.25f,.59f),P(-.59f,25.25f,.59f),
                    P(0,26.8f,0),P(0,26.8f,0),Gilding);
                for(int i=0;i<17;i++)
                {
                    float y=5.7f+i*1.08f;
                    float z=Mathf.Lerp(.925f,.59f,(y-4.79f)/20.46f)+.014f;
                    var a=P(-.22f+(i%3)*.065f,y,z);
                    bronze.Rod(a,a+r*.24f,.026f,granite*.72f,4);
                    bronze.Rod(a+r*.14f,a+r*.14f+Vector3.up*.33f,.023f,granite*.72f,4);
                }
            }
            MakeFountain(stone,bronze,basins,north);
            MakeFountain(stone,bronze,basins,south);
            AddConcordeLamps(stone,bronze,obelisk,(north-south).normalized);
            Save(stone,"Obelisk_granite_and_fountain_stone",limestone,group,true);
            Save(bronze,"Fountain_bronze_and_gilded_tip",metal,group,true);
            Save(basins,"Fountain_water",Surface("Concorde_water",new Color(.36f,.53f,.52f),smooth:.75f),group);
        }

        static void AddConcordeLamps(Geo stone,Geo bronze,Vector3 obelisk,Vector3 axis)
        {
            var side=Vector3.Cross(Vector3.up,axis);
            var rotation=Quaternion.LookRotation(axis);
            var iron=new Color(.21f,.26f,.23f);
            for(int end=-1;end<=1;end+=2)
                for(int flank=-1;flank<=1;flank+=2)
                {
                    var p=obelisk+axis*(end*26)+side*(flank*12.3f);
                    if(Finish.RoadPoint(p,out _)) continue;
                    stone.Box(p+Vector3.up*.27f,new Vector3(1.12f,.58f,1.12f),rotation,CarvedLimestone);
                    bronze.Rod(p+Vector3.up*.54f,p+Vector3.up*1.55f,.24f,iron,12);
                    bronze.Rod(p+Vector3.up*1.55f,p+Vector3.up*5.04f,.115f,iron,10);
                    bronze.Rod(p+Vector3.up*1.56f,p+Vector3.up*1.68f,.176f,Gilding,10);
                    for(int lamp=-1;lamp<=1;lamp++)
                    {
                        var head=p+Vector3.up*(lamp==0?5.8f:5.43f)+side*(lamp*.84f);
                        bronze.Rod(p+Vector3.up*4.66f,head-Vector3.up*.31f,.061f,iron,7);
                        bronze.Box(head,new Vector3(.35f,.42f,.35f),rotation,new Color(.88f,.81f,.60f));
                        bronze.Rock(head+Vector3.up*.25f,new Vector3(.28f,.15f,.28f),5,iron);
                    }
                }
        }

        static void MakeFountain(Geo stone,Geo bronze,Geo basins,Vector3 at)
        {
            Lathe(stone,at,new[]{(.03f,8.72f),(.21f,8.72f),(.25f,8.39f),(.83f,8.39f),
                (.93f,8.12f),(.93f,7.82f),(.50f,7.82f)},CarvedLimestone);
            Lathe(basins,at,new[]{(.55f,7.80f),(.55f,0f)},Color.white);
            Lathe(bronze,at,new[]{(.51f,3.54f),(1.65f,2.8f),(3.05f,1.43f),(4.18f,1.11f),
                (4.63f,2.25f),(4.80f,2.61f),(5.04f,2.61f),(5.04f,1.12f),(6.14f,.46f),(6.70f,.80f),
                (6.85f,1.04f),(6.99f,.63f)},PatinatedBronze);
            for(int i=0;i<8;i++)
            {
                float a=i*Mathf.PI/4;
                var arm=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));
                var figure=at+arm*4.75f;
                bronze.Rock(figure+Vector3.up*.83f,new Vector3(1.2f,.49f,.79f),i,PatinatedBronze);
                bronze.Rock(figure+Vector3.up*1.37f-arm*.19f,new Vector3(.49f,.83f,.43f),i+8,PatinatedBronze);
                bronze.Rock(figure+Vector3.up*2.03f-arm*.16f,new Vector3(.30f,.34f,.31f),i+20,Gilding*.78f);
            }
        }

        static void Lathe(Geo mesh,Vector3 centre,(float y,float r)[] profile,Color tint)
        {
            const int sides=48;
            for(int row=1;row<profile.Length;row++)
            {
                var (low,inner)=profile[row-1];
                var (high,outer)=profile[row];
                for(int i=0;i<sides;i++)
                {
                    float a=Mathf.PI*2*i/sides,b=Mathf.PI*2*(i+1)/sides;
                    var dirA=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                    var dirB=new Vector3(Mathf.Cos(b),0,Mathf.Sin(b));
                    mesh.Face(centre+dirA*inner+Vector3.up*low,centre+dirB*inner+Vector3.up*low,
                        centre+dirA*outer+Vector3.up*high,centre+dirB*outer+Vector3.up*high,tint);
                }
            }
        }
    }
}
