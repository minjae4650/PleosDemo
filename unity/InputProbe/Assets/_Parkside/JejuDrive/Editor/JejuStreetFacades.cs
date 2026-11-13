using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Finish=Parkside.Streets.Editor.StreetFinishGeometry;

namespace Parkside.Jeju.Editor
{
    public static partial class JejuDriveBuilder
    {
        static readonly Color[] StoreColours =
        {
            new Color(.19f,.49f,.44f), new Color(.65f,.22f,.19f), new Color(.23f,.33f,.49f),
            new Color(.56f,.39f,.19f), new Color(.32f,.34f,.30f), new Color(.34f,.17f,.28f),
            new Color(.21f,.34f,.28f), new Color(.63f,.60f,.51f)
        };
        static readonly string[] LocalShopNames =
            { "대청 식자재마트", "서호약국", "제주 베이커리", "동백커피", "강정 공인중개사", "한끼 돈카츠", "우리 세탁", "귤빛 식당",
              "중문철물", "바다앞 식당", "예래마을 슈퍼", "제주감귤 직판장", "마을카페", "해안수산", "제주 작은책방", "바당국수" };
        static readonly Color ShadowGlass=new Color(.40f,.56f,.64f);
        static readonly Color DarkFrame=new Color(.22f,.25f,.26f);
        static readonly Color WarmAluminium=new Color(.72f,.72f,.67f);

        static bool BuildStreetFacade(BuildingSource building,Vector3[] outline,Vector3 centre,
            BuildingBatch batch,Transform group)
        {
            int style=(int)(building.osmId%11);
            string kind=building.kind;
            bool civic=building.landmark=="city_hall";
            bool apartment=kind=="roadview_apartment"||kind=="apartments";
            bool commercial=kind=="roadview_shop"||kind=="commercial"||kind=="retail"||kind=="hotel";
            bool utility=kind=="greenhouse"||kind=="roof";
            bool institution=kind=="public"||kind=="school"||kind=="church";
            int estimate=civic?5:apartment?6+style%5:commercial?2+style%3:utility?1:1+style%3;
            float height=building.height>0?Mathf.Clamp(building.height,2.3f,90):
                (building.levels>0?building.levels:estimate)*(commercial?3.16f:2.95f);
            int levels=building.levels>0?building.levels:building.height>0?Mathf.RoundToInt(height/3):estimate;
            levels=Mathf.Clamp(levels,1,Mathf.Max(1,Mathf.FloorToInt(height/2.35f)));
            float levelHeight=height/levels;
            int street=StreetFrontage(outline);
            var material=style%5==0&&!apartment&&!civic?batch.brick:style%5==1&&!civic?batch.granite:batch.streetWalls;
            Color wallTint=Color.Lerp(new Color(.76f,.81f,.79f),new Color(.96f,.88f,.75f),style/10f);
            if(material==batch.brick) wallTint=Color.Lerp(new Color(.76f,.71f,.66f),new Color(.96f,.88f,.82f),style/10f);
            if(!building.urban&&style%4==0&&!commercial&&!apartment&&!utility) material=batch.basalt;
            if(material==batch.basalt) wallTint=new Color(.43f,.44f,.41f);
            Color roof=utility?new Color(.63f,.73f,.71f):Color.Lerp(new Color(.34f,.46f,.45f),
                new Color(.58f,.47f,.36f),style/10f);
            var accent=Color.Lerp(wallTint,new Color(.38f,.41f,.39f),.43f);
            bool signed=false;
            for(int side=0;side<outline.Length;side++)
            {
                var a=outline[side];var b=outline[(side+1)%outline.Length];
                a.y=b.y=centre.y;
                var edge=b-a;float span=edge.magnitude;
                if(span<.13f) continue;
                var tangent=edge/span;
                var outward=Vector3.Cross(Vector3.up,tangent);
                var frame=Quaternion.LookRotation(outward);
                var middle=(a+b)*.5f;
                var road=ClosestRoad(middle.x,middle.z);
                float clearance=road.road==null?0:road.d-road.road.w[road.i]*.5f;
                bool facesRoad=road.d<65&&Vector3.Dot(road.centre-middle,outward)>.38f*road.d;
                bool frontage=side==street&&facesRoad;
                bool closeToObservedStreet=gradeReference!=null&&TryStreet(middle,gradeReference,44,out _,out _,out _);
                bool mixedUse=apartment&&building.urban&&span>10&&(closeToObservedStreet||road.d<30&&style%3!=1);
                bool shopfront=frontage&&span>=3.7f&&(commercial||civic||mixedUse);
                bool curtain=civic&&(frontage||side==(street+1)%outline.Length);

                var lowerA=a;var lowerB=b;
                lowerA.y=DetailHeight(a.x,a.z)-.13f;lowerB.y=DetailHeight(b.x,b.z)-.13f;
                batch.basalt.Face(lowerA,lowerB,a,b,new Color(.52f,.52f,.48f));
                if(frontage&&span>=5&&(commercial||civic||institution))
                    BuildForecourt(batch.paving,middle,tangent,outward,span,true);
                if(span<1.8f||utility)
                {
                    WallPanel(material,a,tangent,0,span,0,height,utility?roof:wallTint);
                    continue;
                }

                float baySpacing=civic?2.45f:apartment?3.15f:commercial?3.5f:2.95f;
                int bays=Mathf.Max(1,Mathf.RoundToInt(span/baySpacing));
                float bay=span/bays;
                for(int floor=0;floor<levels;floor++)
                    for(int j=0;j<bays;j++)
                    {
                        var anchor=a+tangent*(j*bay)+Vector3.up*(floor*levelHeight);
                        int variation=style*19+j*3+floor*5+side;
                        Color tint=apartment&&(j+style)%5==0?accent:wallTint;
                        if(floor==0&&shopfront)
                        {
                            Color sign=StoreColours[(j+style)%StoreColours.Length];
                            float projection=Mathf.Clamp(clearance-.7f,0,1.25f);
                            Storefront(batch,material,anchor,tangent,outward,frame,bay,levelHeight,sign,civic,variation,projection);
                            bool label=civic?j==bays/2:bay>=1.8f&&(j%2==0||bays<=3);
                            if(label)
                            {
                                string name=civic?"서귀포시청 제2청사":JejuShopLabel(building,style,j,bays);
                                var signAt=anchor+tangent*bay*.5f+Vector3.up*(levelHeight-.39f);
                                Finish.Letter(group,font,name,signAt+outward*.188f,outward,bay*.90f,.37f,new Color(1,.96f,.87f));
                                if(!civic&&clearance>1.25f&&j%4==0)
                                    JejuBladeSign(batch,group,signAt,tangent,outward,frame,bay,sign,name);
                                signed=true;
                            }
                        }
                        else if(frontage&&floor==0&&j==bays/2&&bay>=1.75f)
                        {
                            JejuEntry(batch,material,group,building,anchor,tangent,outward,frame,bay,levelHeight,tint);
                        }
                        else
                        {
                            WindowBay(batch,material,anchor,tangent,outward,frame,bay,levelHeight,tint,
                                apartment,curtain,variation,facesRoad||road.d<100);
                            if(facesRoad&&!apartment&&!civic&&floor>0&&bay>2.4f&&variation%8==0)
                                AirConditioner(batch,anchor+tangent*(bay*.84f)+Vector3.up*.64f,outward,frame);
                        }
                    }
                batch.details.Box(middle+Vector3.up*(height+.065f),new Vector3(span,.16f,.26f),frame,wallTint*.91f);
                if(!shopfront)
                    WallPanel(batch.basalt,a+outward*.011f,tangent,0,span,0,.24f,new Color(.55f,.55f,.52f));
                if(facesRoad&&span>5&&!civic)
                {
                    var drain=a+tangent*.18f+outward*.055f;
                    batch.details.Rod(drain+Vector3.up*.25f,drain+Vector3.up*(height-.08f),.033f,DarkFrame,5);
                }
            }
            RoofPolygon(batch.roofs,outline,centre.y+height,roof);
            if(apartment||civic)
                batch.streetWalls.Box(centre+Vector3.up*(height+.8f),new Vector3(civic?6:2.4f,1.6f,3),Quaternion.identity,accent);
            return signed;
        }

        static string JejuShopLabel(BuildingSource building,int style,int bay,int bays)
        {
            if(bay==bays/2&& !string.IsNullOrWhiteSpace(building.name)&&building.name.Length<=20)
                return building.name;
            int first=building.urban?0:8;
            return LocalShopNames[first+(style+bay/2)%8];
        }

        static void JejuBladeSign(BuildingBatch batch,Transform group,Vector3 signAt,Vector3 tangent,
            Vector3 outward,Quaternion rotation,float width,Color paint,string title)
        {
            var blade=signAt+tangent*width*.40f+outward*.37f-Vector3.up*.36f;
            batch.details.Box(blade,new Vector3(.068f,.48f,.53f),rotation,paint);
            batch.details.Rod(blade-outward*.39f,blade-outward*.17f,.033f,DarkFrame,5);
            string word=title.Split(' ')[0];
            Finish.Letter(group,font,word,blade+tangent*.045f,tangent,.44f,.18f,Color.white);
            Finish.Letter(group,font,word,blade-tangent*.045f,-tangent,.44f,.18f,Color.white);
        }

        static void JejuEntry(BuildingBatch batch,Geo material,Transform group,BuildingSource building,Vector3 origin,
            Vector3 tangent,Vector3 outside,Quaternion rotation,float width,float height,Color wall)
        {
            float doorWidth=Mathf.Min(width*.66f,1.48f),doorHeight=Mathf.Min(height-.24f,2.16f);
            float edge=(width-doorWidth)*.5f;
            WallPanel(material,origin,tangent,0,edge,0,height,wall);
            WallPanel(material,origin,tangent,width-edge,width,0,height,wall);
            WallPanel(material,origin,tangent,edge,width-edge,doorHeight,height,wall);
            var middle=origin+tangent*width*.5f;
            var door=middle+Vector3.up*doorHeight*.5f-outside*.075f;
            batch.details.Box(door,new Vector3(doorWidth,doorHeight,.14f),rotation,DarkFrame);
            batch.glass.Box(door+outside*.082f+Vector3.up*.22f,new Vector3(doorWidth*.82f,doorHeight*.62f,.018f),
                rotation,new Color(.45f,.52f,.52f));
            batch.details.Box(door+outside*.098f+tangent*doorWidth*.30f,new Vector3(.035f,.27f,.07f),rotation,WarmAluminium);
            batch.granite.Box(middle+Vector3.up*.055f+outside*.10f,new Vector3(doorWidth+.18f,.11f,.38f),rotation,wall*.75f);
            var plaque=middle+tangent*doorWidth*.69f+outside*.021f+Vector3.up*1.56f;
            bool publicBuilding=building.kind=="school"||building.kind=="public"||building.kind=="church";
            if(publicBuilding&&!string.IsNullOrWhiteSpace(building.name)&&building.name.Length<=20&&height-doorHeight>.21f)
            {
                var nameplate=middle+outside*.08f+Vector3.up*(doorHeight+.12f);
                batch.details.Box(nameplate,new Vector3(doorWidth+.48f,.23f,.075f),rotation,DarkFrame);
                Finish.Letter(group,font,building.name,nameplate+outside*.047f,outside,doorWidth+.29f,.14f,Color.white);
            }
            else batch.details.Box(plaque,new Vector3(.18f,.14f,.043f),rotation,new Color(.15f,.29f,.43f));
        }

        static void WallPanel(Geo geo,Vector3 origin,Vector3 tangent,float x0,float x1,float y0,float y1,Color colour)
        {
            if(x1<=x0||y1<=y0) return;
            var a=origin+tangent*x0+Vector3.up*y0;var b=origin+tangent*x1+Vector3.up*y0;
            var c=origin+tangent*x0+Vector3.up*y1;var d=origin+tangent*x1+Vector3.up*y1;
            int n=geo.v.Count;
            geo.Vertex(a,new Vector2(x0,y0)*.55f,colour);geo.Vertex(b,new Vector2(x1,y0)*.55f,colour);
            geo.Vertex(c,new Vector2(x0,y1)*.55f,colour);geo.Vertex(d,new Vector2(x1,y1)*.55f,colour);
            Quad(geo.t,n,n+1,n+2,n+3);
        }

        static void WindowBay(BuildingBatch m,Geo wallMesh,Vector3 p,Vector3 along,Vector3 outwards,Quaternion q,
            float width,float height,Color wall,bool apartment,bool curtain,int variant,bool photo)
        {
            float border=width*(curtain?.035f:apartment?.13f:.20f);
            float sill=height*(curtain?.13f:apartment?.22f:.28f),head=height*(curtain?.92f:.81f);
            float glassWidth=width-border*2;
            WallPanel(wallMesh,p,along,0,width,0,sill,wall);
            WallPanel(wallMesh,p,along,0,width,head,height,wall);
            WallPanel(wallMesh,p,along,0,border,sill,head,wall);
            WallPanel(wallMesh,p,along,width-border,width,sill,head,wall);
            Vector3 c=p+along*(width*.5f)+Vector3.up*((sill+head)*.5f);
            float glassHeight=head-sill;
            Color tint=curtain?new Color(.42f,.90f,.85f):
                Color.Lerp(ShadowGlass,new Color(.92f,.96f,.91f),(variant%7)/8f);
            if(photo&&!curtain)
            {
                Rect crop=apartment
                    ?new Rect(.043f+(variant%6)*.158f,.078f+(variant%12)*.050f,.122f,.034f)
                    :new Rect(variant%2==0?.099f:.650f,.590f+(variant%3-1)*.243f,.230f,.105f);
                Finish.Rect(apartment?m.apartmentWindows:m.villaWindows,
                    c-along*glassWidth*.5f-Vector3.up*glassHeight*.5f-outwards*.116f,
                    along*glassWidth,Vector3.up*glassHeight,crop,
                    Color.Lerp(Color.white,new Color(.75f,.79f,.78f),variant%4/5f));
            }
            else m.glass.Box(c-outwards*.115f,new Vector3(glassWidth,glassHeight,.032f),q,tint);
            Color frame=curtain?new Color(.33f,.51f,.52f):variant%5==0?WarmAluminium:DarkFrame;
            m.details.Box(c-outwards*.063f,new Vector3(.065f,glassHeight+.02f,.10f),q,frame);
            for(int sign=-1;sign<=1;sign+=2)
            {
                m.details.Box(c+along*(sign*(glassWidth*.5f-.014f))-outwards*.035f,
                    new Vector3(.085f,glassHeight,.17f),q,frame);
                m.details.Box(c+Vector3.up*(sign*glassHeight*.5f)-outwards*.025f,
                    new Vector3(glassWidth+.04f,.085f,.18f),q,frame);
            }
            if(apartment&&variant%3!=0)
            {
                var edge=c+outwards*.39f-Vector3.up*(glassHeight*.5f+.08f);
                m.details.Box(edge,new Vector3(glassWidth+.21f,.15f,.79f),q,wall*.91f);
                m.glass.Box(edge+outwards*.38f+Vector3.up*.38f,
                    new Vector3(glassWidth+.14f,.62f,.045f),q,Color.Lerp(new Color(.42f,.51f,.53f),new Color(.71f,.73f,.68f),variant%5/5f));
                m.details.Box(edge+outwards*.40f+Vector3.up*.72f,new Vector3(glassWidth+.16f,.055f,.055f),q,frame);
            }
        }

        static void Storefront(BuildingBatch m,Geo wallMesh,Vector3 p,Vector3 along,Vector3 outside,Quaternion q,
            float width,float height,Color sign,bool civic,int index,float projection)
        {
            var centre=p+along*(width*.5f);
            // Close the wall behind the glazing and the narrow fascia-to-slab joint.
            var backing=new Color(.67f,.67f,.63f);
            WallPanel(wallMesh,p-outside*.17f,along,0,width,0,height,backing);
            WallPanel(wallMesh,p,along,0,width,height-.139f,height,backing);
            var glass=centre+Vector3.up*(height*.41f)-outside*.08f;
            if(!civic)
            {
                float u=(index%5)*.20f+.015f;
                Finish.Rect(m.photo,p+along*.065f+Vector3.up*.05f-outside*.085f,along*(width-.13f),
                    Vector3.up*(height*.82f),new Rect(u,.016f,.168f,.308f),Color.white);
            }
            else m.glass.Box(glass,new Vector3(width-.14f,height*.81f,.06f),q,civic?new Color(.42f,.88f,.87f):new Color(.62f,.76f,.78f));
            for(int i=-1;i<=1;i++)
                m.details.Box(glass+along*(i*width/3)+outside*.057f,new Vector3(.072f,height*.80f,.13f),q,DarkFrame);
            m.tileBands.Box(centre+Vector3.up*.20f,new Vector3(width,.39f,.14f),q,new Color(.39f,.41f,.42f));
            m.details.Box(centre+Vector3.up*(height-.40f)+outside*.08f,
                new Vector3(width-.045f,.54f,.16f),q,civic?new Color(.23f,.38f,.41f):sign);
            if(civic) return;
            if(index%3!=0&&projection>.5f)
                Finish.Awning(m.fabric,centre+Vector3.up*(height-.66f)+outside*.06f,along,outside,width*.91f,projection,sign);
            var handle=centre+along*(width*.31f)+Vector3.up*1.05f+outside*.032f;
            m.details.Box(handle,new Vector3(.046f,.38f,.071f),q,new Color(.55f,.57f,.57f));
        }

        static void CaptureJejuFacadeCoverage(JejuDriveController car)
        {
            string folder=Path.Combine(Output,"seogwipo");
            var village=scene.GetRootGameObjects().Single(g=>g.name=="Jeju_CoastalDrive")
                .transform.Find("Jeju_Landscape_And_Roadside/"+RoadsideBuildingRoot);
            var signs=village.GetComponentsInChildren<TextMesh>(true);
            var photographed=new System.Collections.Generic.List<Vector3>();
            var start=car.route.stations[0];
            try
            {
                foreach(var sign in signs.Where(t=>t.text.Length>=4))
                {
                    var at=sign.transform.position;
                    if(photographed.Any(p=>XZDistanceSquared(p,at)<550*550)) continue;
                    var road=ClosestRoad(at.x,at.z);
                    if(road.road==null||road.d<6||road.d>29||
                        Vector3.Dot(-sign.transform.forward,road.centre-at)<3) continue;
                    if(!Finish.RoadPoint(road.centre+road.forward*8,out var eye)&&
                        !Finish.RoadPoint(road.centre,out eye)) continue;
                    car.route.stations[0]=new JejuStation
                    {
                        branchSpawn=true, position=eye, forward=road.forward, index=start.index, name=road.road.name
                    };
                    car.cameraMode=1;
                    car.JumpToStation(0);
                    car.view.transform.position=eye+Vector3.up*1.85f;
                    car.view.transform.LookAt(at-Vector3.up*.65f);
                    car.view.fieldOfView=61;
                    Capture(car.view,Path.Combine(folder,$"jeju-corridor-{photographed.Count+1}.png"));
                    photographed.Add(at);
                    if(photographed.Count==3) break;
                }
            }
            finally
            {
                car.route.stations[0]=start;
                car.cameraMode=0;
                car.JumpToStation(0);
            }
            File.WriteAllText(Path.Combine(folder,"corridor-coverage.txt"),
                $"RoadsideBlocks={village.childCount}\nRoadsideLettering={signs.Length}\nSeparateStreetViews={photographed.Count}");
            if(photographed.Count<2) throw new InvalidOperationException("Expected signed facades across at least two Jeju roadside districts.");
        }

        static void AirConditioner(BuildingBatch m,Vector3 p,Vector3 outward,Quaternion q)
        {
            var c=p+outward*.20f;
            m.details.Box(c,new Vector3(.67f,.45f,.38f),q,new Color(.75f,.76f,.74f));
            for(int n=0;n<4;n++)
                m.details.Box(c+outward*.20f+Vector3.up*(n*.062f-.10f),
                    new Vector3(.42f,.022f,.028f),q,DarkFrame);
        }
    }
}
