using System.Collections.Generic;
using UnityEngine;
using Geo=Parkside.Jeju.Editor.JejuDriveBuilder.Geo;
using Finish=Parkside.Streets.Editor.StreetFinishGeometry;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        enum PortalUse { Window, Entry, Shop }

        readonly struct Portal
        {
            public readonly Opening Opening;
            public readonly PortalUse Use;
            public readonly int Number;
            public Portal(Opening opening,PortalUse use,int number)
            {
                Opening=opening; Use=use; Number=number;
            }
        }

        static int BuildGroundStorey(Block block,Building building,Elevation wall,float height,int bays,
            int style,Color stone,bool frontage,bool commercial,Road road,ParisStreetLookup streets,Font font,bool close)
        {
            if(height<1.65f)
            {
                wall.Panel(block.masonry,0,0,wall.Length,height,0,stone);
                return 0;
            }

            var portals=new List<Portal>(bays+1);
            float openingTop=height-Mathf.Min(.50f,height*.14f);
            var groundTone=Color.Lerp(stone,new Color(.68f,.65f,.61f),.27f);
            var trim=Color.Lerp(stone,new Color(.92f,.88f,.81f),.24f);
            int shops=0;
            if(commercial&&height>=2.25f)
            {
                float entrySpace=wall.Length>=8?2.43f:.23f;
                float available=Mathf.Min(wall.Length-entrySpace-.27f,30);
                int capacity=Mathf.Clamp(Mathf.FloorToInt(available/3.25f),1,7);
                int mapped=building.retailUse?.Length??0;
                shops=mapped>0?Mathf.Min(mapped,capacity):1+(int)(building.osmId%capacity);
                float shopWidth=Mathf.Min(5.55f,available/shops);
                float first=entrySpace+(available-shopWidth*shops)*.25f;
                if(entrySpace>2)
                    portals.Add(new Portal(new Opening(.36f,1.94f,.075f,openingTop,.56f),PortalUse.Entry,0));
                for(int i=0;i<shops;i++)
                {
                    float left=first+shopWidth*i+.12f;
                    portals.Add(new Portal(new Opening(left,left+shopWidth-.24f,.08f,openingTop-.28f,.36f),
                        PortalUse.Shop,i));
                }
            }
            else
            {
                float bay=wall.Length/bays;
                for(int j=0;j<bays;j++)
                {
                    float mid=(j+.5f)*bay;
                    bool entry=frontage&&j==bays/2&&wall.Length>=3.5f;
                    float span=entry?Mathf.Min(1.70f,bay*.73f):bay*.58f;
                    float low=entry?.06f:height*.28f;
                    float top=entry?openingTop:height*.79f;
                    portals.Add(new Portal(new Opening(mid-span*.5f,mid+span*.5f,low,top,entry?.50f:.24f),
                        entry?PortalUse.Entry:PortalUse.Window,j));
                }
            }

            float cursor=0;
            foreach(var portal in portals)
            {
                var opening=portal.Opening;
                wall.Panel(block.masonry,cursor,0,opening.Left,height,0,groundTone);
                wall.Panel(block.masonry,opening.Left,0,opening.Right,opening.Bottom,0,groundTone);
                cursor=opening.Right;
                switch(portal.Use)
                {
                    case PortalUse.Entry:
                        AddEntry(block,wall,opening,height,trim,building,font,style);
                        break;
                    case PortalUse.Shop:
                        wall.Panel(block.masonry,opening.Left,opening.Top,opening.Right,height,0,groundTone);
                        Reveal(block.masonry,wall,opening,trim);
                        AddShop(block,wall,opening,building,portal.Number,road,streets,font,close);
                        break;
                    default:
                        wall.Panel(block.masonry,opening.Left,opening.Top,opening.Right,height,0,groundTone);
                        Reveal(block.masonry,wall,opening,trim);
                        BuildWindow(block,wall,opening,trim,ParisJoinery[style%ParisJoinery.Length],
                            style+portal.Number*3,close,false);
                        break;
                }
            }
            wall.Panel(block.masonry,cursor,0,wall.Length,height,0,groundTone);
            wall.Box(block.trim,wall.Length*.5f,height-.048f,.089f,wall.Length,.125f,.29f,trim);
            return shops;
        }

        static void AddEntry(Block block,Elevation wall,Opening o,float storeyTop,Color stone,
            Building building,Font font,int style)
        {
            float shoulder=o.Top-o.Width*.29f;
            var lower=new Opening(o.Left,o.Right,o.Bottom,shoulder,o.Recess);
            Reveal(block.masonry,wall,lower,stone,cap:false);
            var wood=ParisJoinery[style%ParisJoinery.Length];
            float d=-o.Recess+.026f;
            wall.Panel(block.ornament,o.Left+.035f,o.Bottom+.025f,o.Right-.035f,shoulder,d,wood);
            wall.Box(block.trim,o.Mid,o.Bottom+.004f,-.146f,o.Width+.08f,.075f,.39f,stone*.86f);
            for(int side=-1;side<=1;side+=2)
            {
                float x=o.Mid+side*o.Width*.238f;
                wall.Box(block.ornament,x,o.Bottom+(shoulder-o.Bottom)*.31f,d+.031f,o.Width*.39f,
                    (shoulder-o.Bottom)*.39f,.048f,wood*.80f);
                wall.Box(block.ornament,x,o.Bottom+(shoulder-o.Bottom)*.73f,d+.026f,o.Width*.38f,
                    (shoulder-o.Bottom)*.32f,.038f,wood*.86f);
                wall.Box(block.ornament,o.Mid+side*.083f,o.Bottom+.95f,d+.075f,.031f,.11f,.065f,Brass);
            }

            const int pieces=8;
            float radius=o.Width*.5f;
            for(int i=0;i<pieces;i++)
            {
                float a=Mathf.PI*(1-i/(float)pieces),b=Mathf.PI*(1-(i+1f)/pieces);
                float x0=o.Mid+Mathf.Cos(a)*radius,x1=o.Mid+Mathf.Cos(b)*radius;
                float y0=shoulder+Mathf.Sin(a)*(o.Top-shoulder);
                float y1=shoulder+Mathf.Sin(b)*(o.Top-shoulder);
                block.masonry.Face(wall.At(x0,y0),wall.At(x1,y1),wall.At(x0,storeyTop),
                    wall.At(x1,storeyTop),stone);
                block.masonry.Face(wall.At(x0,y0,-o.Recess),wall.At(x1,y1,-o.Recess),
                    wall.At(x0,y0),wall.At(x1,y1),stone*.80f);
                block.panes.Face(wall.At(x0,shoulder,d),wall.At(x1,shoulder,d),
                    wall.At(x0,y0,d),wall.At(x1,y1,d),new Color(.30f,.35f,.32f));
                block.trim.Face(wall.At(x0,y0,.075f),wall.At(x1,y1,.075f),
                    wall.At(x0+(x0-o.Mid)*.15f,y0+.12f,.075f),
                    wall.At(x1+(x1-o.Mid)*.15f,y1+.12f,.075f),stone);
            }

            if(!string.IsNullOrWhiteSpace(building.number)&&building.number.Length<=6)
            {
                float x=o.Right+.16f,y=shoulder-.14f;
                wall.Box(block.ornament,x,y,.032f,.23f,.15f,.046f,EnamelBlue);
                Finish.Letter(block.group,font,building.number,wall.At(x,y,.06f),wall.Outward,.21f,.10f,Cream);
            }
        }

        static void AddShop(Block block,Elevation wall,Opening o,Building building,int shop,Road frontRoad,
            ParisStreetLookup streets,Font font,bool close)
        {
            int mapped=building.retailUse?.Length??0;
            var tagged=shop<mapped?building.retailUse[shop]:null;
            long seed=building.osmId+shop;
            int style=ParisShopStyle(tagged,seed);
            var paint=Color.Lerp(ShopPaint[style],ParisJoinery[(int)(seed%ParisJoinery.Length)],.22f);
            float sill=o.Bottom+.38f;
            float d=-o.Recess+.078f;
            float rightDoor=o.Right-.13f,leftDoor=rightDoor-Mathf.Min(.94f,o.Width*.31f);
            bool shutter=style==3&&seed%4==0;

            AddShopRoom(block,wall,o,style,seed,close,shutter);
            wall.Box(block.ornament,(o.Left+leftDoor)*.5f,o.Bottom+.18f,d+.014f,leftDoor-o.Left,.36f,.12f,paint);
            wall.Box(block.ornament,o.Mid,o.Top-.059f,d+.011f,o.Width,.12f,.107f,paint);
            foreach(float x in new[]{o.Left+.043f,leftDoor,rightDoor,o.Right-.043f})
                wall.Box(block.ornament,x,(o.Top+o.Bottom)*.5f,d+.034f,.087f,o.Top-o.Bottom,.13f,paint);
            float mullion=(o.Left+leftDoor)*.5f;
            wall.Box(block.ornament,mullion,(o.Top+sill)*.5f,d+.031f,.060f,o.Top-sill,.104f,paint);
            wall.Box(block.ornament,(leftDoor+rightDoor)*.5f,sill-.027f,d+.031f,
                rightDoor-leftDoor,.076f,.092f,paint);
            wall.Box(block.ornament,rightDoor-.17f,o.Bottom+.95f,d+.092f,.029f,.15f,.055f,Brass);
            if(!shutter)
            {
                wall.Panel(block.shopGlass,o.Left+.088f,sill,leftDoor-.043f,o.Top-.116f,d,Color.white);
                wall.Panel(block.shopGlass,leftDoor+.043f,o.Bottom+.11f,rightDoor-.043f,o.Top-.116f,d,Color.white);
            }
            else
            {
                var slate=new Color(.34f,.38f,.41f);
                wall.Panel(block.ornament,o.Left+.05f,sill,o.Right-.05f,o.Top-.115f,d+.035f,slate);
                for(float y=sill+.065f;y<o.Top-.122f;y+=.087f)
                    wall.Box(block.ornament,o.Mid,y,d+.044f,o.Width-.14f,.016f,.031f,slate*.76f);
            }

            float fascia=o.Top+.141f;
            wall.Box(block.ornament,o.Mid,fascia,.083f,o.Width+.11f,.298f,.204f,paint);
            wall.Box(block.ornament,o.Mid,fascia+.157f,.101f,o.Width+.20f,.045f,.25f,paint*.77f);
            for(int side=-1;side<=1;side+=2)
                wall.Box(block.ornament,o.Mid+side*(o.Width*.5f+.052f),(o.Top+o.Bottom)*.5f,.038f,
                    .13f,o.Top-o.Bottom,.17f,paint);
            string label=string.IsNullOrWhiteSpace(tagged?.name)
                ?ShopWords[style][(int)((building.osmId/7+shop)%ShopWords[style].Length)]
                :tagged.name.ToUpperInvariant();
            Finish.Letter(block.group,font,label,wall.At(o.Mid,fascia,.195f),wall.Outward,o.Width-.18f,.192f,Cream);

            var centre=wall.At(o.Mid,0);
            if(!streets.Nearest(centre,40,out var roadAt,out _,out var road,frontRoad.name,wall.Outward)) return;
            float clearance=Vector3.Distance(roadAt,centre)-road.width*.5f;
            if(clearance>1.82f&&(style==1||style==0&&seed%3==0))
                Finish.Awning(block.ornament,wall.At(o.Mid,o.Top-.085f,.11f),wall.Along,wall.Outward,o.Width*.99f,
                    Mathf.Min(1.35f,clearance-.62f),paint);
            if(style==1&&clearance>3.10f&&shop==0&&seed%2==0)
                ParisCafeTable(block.ornament,wall.At(o.Mid+.35f,0,1.45f));
        }

        static void AddShopRoom(Block block,Elevation wall,Opening o,int style,long seed,bool close,bool shutter)
        {
            if(shutter) return;
            float depth=close?2.32f:1.18f;
            var brick=new Color(.70f,.52f,.36f);
            var plaster=Color.Lerp(new Color(.81f,.70f,.53f),brick,style*.12f);
            float front=-o.Recess,back=front-depth;
            var room=block.interiors;
            wall.Panel(room,o.Left,o.Bottom,o.Right,o.Top,back,plaster);
            room.Face(wall.At(o.Left,o.Bottom,front),wall.At(o.Right,o.Bottom,front),
                wall.At(o.Left,o.Bottom,back),wall.At(o.Right,o.Bottom,back),new Color(.55f,.44f,.33f));
            room.Face(wall.At(o.Left,o.Bottom,front),wall.At(o.Left,o.Bottom,back),
                wall.At(o.Left,o.Top,front),wall.At(o.Left,o.Top,back),plaster*.81f);
            room.Face(wall.At(o.Right,o.Bottom,back),wall.At(o.Right,o.Bottom,front),
                wall.At(o.Right,o.Top,back),wall.At(o.Right,o.Top,front),plaster*.89f);
            var wood=new Color(.43f,.28f,.17f);
            float counterWidth=o.Width*.61f;
            float x=o.Left+o.Width*.37f;
            wall.Box(room,x,o.Bottom+.46f,back+.43f,counterWidth,.91f,.54f,wood);
            wall.Box(room,x,o.Bottom+.95f,back+.43f,counterWidth+.07f,.076f,.60f,new Color(.76f,.65f,.49f));
            if(!close) return;

            wall.Box(room,x,o.Bottom+1.45f,back+.12f,counterWidth,.058f,.22f,wood);
            wall.Box(room,x,o.Bottom+1.83f,back+.12f,counterWidth,.058f,.22f,wood);
            var display=style==2?new Color(.29f,.52f,.28f):new Color(.77f,.51f,.23f);
            for(int row=0;row<2;row++)
                for(int i=0;i<5;i++)
                {
                    float sample=x+counterWidth*(i*.183f-.37f);
                    var p=wall.At(sample,o.Bottom+1.54f+row*.38f,back+.168f);
                    room.Rock(p,new Vector3(.12f,style==2?.15f:.10f,.10f),i+seed%11,display);
                }
            float lightHeight=o.Top-.22f;
            room.Rod(wall.At(o.Mid,o.Top,back+.65f),wall.At(o.Mid,lightHeight,back+.65f),.022f,wood,5);
            wall.Box(room,o.Mid,lightHeight,back+.65f,.24f,.08f,.22f,new Color(.92f,.78f,.49f));
        }
    }
}
