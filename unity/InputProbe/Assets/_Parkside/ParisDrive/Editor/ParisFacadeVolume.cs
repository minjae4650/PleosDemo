using UnityEngine;
using Geo=Parkside.Jeju.Editor.JejuDriveBuilder.Geo;
using Finish=Parkside.Streets.Editor.StreetFinishGeometry;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        readonly struct Elevation
        {
            public readonly Vector3 Origin, Along, Outward;
            public readonly Quaternion Rotation;
            public readonly float Length;

            public Elevation(Vector3 a,Vector3 b)
            {
                Origin=a;
                Length=Vector3.Distance(a,b);
                Along=(b-a)/Length;
                Outward=Vector3.Cross(Vector3.up,Along);
                Rotation=Quaternion.LookRotation(Outward);
            }

            public Vector3 At(float x,float y,float depth=0) =>
                Origin+Along*x+Vector3.up*y+Outward*depth;

            public void Panel(Geo mesh,float left,float bottom,float right,float top,float depth,Color tint)
            {
                if(right-left<.001f||top-bottom<.001f) return;
                Finish.Rect(mesh,At(left,bottom,depth),Along*(right-left),Vector3.up*(top-bottom),
                    Rect.MinMaxRect(left,bottom,right,top),tint);
            }

            public void Box(Geo mesh,float x,float y,float depth,float width,float height,float thickness,Color tint) =>
                mesh.Box(At(x,y,depth),new Vector3(width,height,thickness),Rotation,tint);
        }

        readonly struct Opening
        {
            public readonly float Left,Right,Bottom,Top,Recess;
            public float Width=>Right-Left;
            public float Mid=>(Left+Right)*.5f;
            public Opening(float left,float right,float bottom,float top,float recess)
            {
                Left=left; Right=right; Bottom=bottom; Top=top; Recess=recess;
            }
        }

        static int BuildResidentialStoreys(Block block,Elevation wall,float groundHeight,float eave,int floors,
            int bays,Color stone,int style,bool close)
        {
            if(floors<2) return 0;
            var masonry=block.masonry;
            var trim=block.trim;
            float band=(eave-groundHeight)/(floors-1);
            float bay=wall.Length/bays;
            var dressed=Color.Lerp(stone,new Color(.95f,.90f,.80f),.27f);
            var joinery=ParisJoinery[style%ParisJoinery.Length];
            int windows=0;

            for(int floor=1;floor<floors;floor++)
            {
                float baseHeight=groundHeight+band*(floor-1);
                float head=baseHeight+band;
                bool balcony=band>1.8f&&floors>=4&&
                    (floor==1&&style%5!=4||floor==floors-1&&style%4==0);
                float sillHeight=baseHeight+band*(balcony?.095f:.22f);
                float windowHead=baseHeight+band*(floor==floors-1?.77f:.84f);
                wall.Panel(masonry,0,baseHeight,wall.Length,sillHeight,0,stone);
                wall.Panel(masonry,0,windowHead,wall.Length,head,0,stone);
                float previous=0;

                for(int j=0;j<bays;j++)
                {
                    float mid=(j+.5f)*bay;
                    float half=bay*(style%3==0?.254f:.283f);
                    float recess=close?.30f:.22f;
                    var opening=new Opening(mid-half,mid+half,sillHeight,windowHead,recess);
                    wall.Panel(masonry,previous,sillHeight,opening.Left,windowHead,0,stone);
                    previous=opening.Right;
                    Reveal(masonry,wall,opening,stone);
                    BuildWindow(block,wall,opening,dressed,joinery,style+j*7+floor*11,close,balcony);
                    if(balcony&&(style%4!=2||j%2==0))
                        AddBalcony(block,wall,opening,baseHeight,dressed,close);
                    windows++;
                }
                wall.Panel(masonry,previous,sillHeight,wall.Length,windowHead,0,stone);
                if(close&&floor==1&&wall.Length>5&&style%3==0)
                    wall.Box(trim,wall.Length*.5f,head-.055f,.071f,wall.Length,.10f,.20f,dressed);
            }
            AddCornice(trim,wall,eave,dressed,close);
            return windows;
        }

        static void Reveal(Geo mesh,Elevation wall,Opening opening,Color stone,bool cap=true)
        {
            float l=opening.Left,r=opening.Right,b=opening.Bottom,t=opening.Top,d=-opening.Recess;
            mesh.Face(wall.At(l,b),wall.At(l,b,d),wall.At(l,t),wall.At(l,t,d),stone*.78f);
            mesh.Face(wall.At(r,b,d),wall.At(r,b),wall.At(r,t,d),wall.At(r,t),stone*.83f);
            mesh.Face(wall.At(l,b),wall.At(r,b),wall.At(l,b,d),wall.At(r,b,d),stone*.90f);
            if(cap) mesh.Face(wall.At(l,t,d),wall.At(r,t,d),wall.At(l,t),wall.At(r,t),stone*.74f);
        }

        static void BuildWindow(Block block,Elevation wall,Opening o,Color stone,Color paint,int variant,
            bool close,bool balcony)
        {
            float d=-o.Recess+.018f;
            float span=o.Top-o.Bottom;
            float inset=.052f;
            var glass=Color.Lerp(new Color(.16f,.22f,.25f),new Color(.43f,.51f,.54f),(variant%11)/10f);
            wall.Panel(block.panes,o.Left+inset,o.Bottom+inset,o.Right-inset,o.Top-inset,d,glass);
            var frame=Color.Lerp(paint,new Color(.79f,.77f,.68f),variant%4==0?.63f:.24f);
            if(close)
            {
                wall.Panel(block.ornament,o.Left,o.Bottom,o.Left+inset,o.Top,d+.022f,frame);
                wall.Panel(block.ornament,o.Right-inset,o.Bottom,o.Right,o.Top,d+.022f,frame);
                wall.Panel(block.ornament,o.Left,o.Top-inset,o.Right,o.Top,d+.022f,frame);
                wall.Panel(block.ornament,o.Left,o.Bottom,o.Right,o.Bottom+inset,d+.022f,frame);
            }
            else wall.Panel(block.panes,o.Left,o.Bottom,o.Right,o.Top,d-.004f,frame*.58f);
            wall.Panel(block.ornament,o.Mid-.025f,o.Bottom+inset,o.Mid+.025f,o.Top-inset,d+.025f,frame);

            if(close)
            {
                float transom=o.Bottom+span*.66f;
                wall.Panel(block.ornament,o.Left+inset,transom-.020f,o.Right-inset,transom+.020f,d+.024f,frame);
                AddWindowSurround(block.trim,wall,o,stone,variant);
                if(variant%5<2) AddShutters(block.ornament,wall,o,paint);
                if(variant%5==4&&!balcony)
                    wall.Box(block.trim,o.Mid,o.Top+.11f,.092f,.22f,.23f,.16f,stone);
            }

            if(balcony) return;
            if(close)
                wall.Box(block.trim,o.Mid,o.Bottom-.045f,.050f,o.Width+.20f,.095f,.215f,stone*.92f);
            else
                block.trim.Face(wall.At(o.Left-.08f,o.Bottom-.02f,.14f),wall.At(o.Right+.08f,o.Bottom-.02f,.14f),
                    wall.At(o.Left-.08f,o.Bottom-.02f),wall.At(o.Right+.08f,o.Bottom-.02f),stone*.93f);
        }

        static void AddWindowSurround(Geo mesh,Elevation wall,Opening o,Color stone,int variant)
        {
            float thick=variant%3==0?.145f:.105f;
            float height=o.Top-o.Bottom;
            wall.Box(mesh,o.Left-thick*.5f,o.Bottom+height*.5f,.030f,thick,height,.105f,stone);
            wall.Box(mesh,o.Right+thick*.5f,o.Bottom+height*.5f,.030f,thick,height,.105f,stone);
            wall.Box(mesh,o.Mid,o.Top+.043f,.049f,o.Width+thick*2,.086f,.143f,stone);
        }

        static void AddShutters(Geo joinery,Elevation wall,Opening o,Color paint)
        {
            float width=Mathf.Min(o.Width*.33f,.50f);
            float height=(o.Top-o.Bottom)*.90f;
            float mid=(o.Top+o.Bottom)*.5f;
            for(int side=-1;side<=1;side+=2)
            {
                float x=o.Mid+side*(o.Width*.5f+width*.56f+.095f);
                wall.Box(joinery,x,mid,.077f,width,height,.073f,paint);
                for(int slat=1;slat<8;slat++)
                {
                    float y=mid+height*(slat/8f-.5f);
                    wall.Panel(joinery,x-width*.43f,y,x+width*.43f,y+.014f,.115f,paint*.68f);
                }
            }
        }

        static void AddBalcony(Block block,Elevation wall,Opening o,float floor,Color stone,bool close)
        {
            float width=o.Width+(close?.52f:.38f);
            float depth=close?.86f:.57f;
            float baseHeight=o.Bottom-.083f;
            float railFront=depth-.042f;
            float left=o.Mid-width*.5f,right=o.Mid+width*.5f;
            wall.Box(block.trim,o.Mid,baseHeight,depth*.49f,width,.17f,depth+.06f,stone*.95f);
            wall.Box(block.trim,o.Mid,baseHeight-.093f,depth*.47f,width-.075f,.07f,depth+.023f,stone*.87f);
            var iron=new Color(.13f,.17f,.17f);
            float low=baseHeight+.106f;
            float high=low+.86f;
            wall.Box(block.ornament,o.Mid,high,railFront,width,.045f,.058f,iron);
            wall.Box(block.ornament,o.Mid,low+.055f,railFront,width,.035f,.041f,iron);
            int bars=close?8:4;
            for(int i=0;i<=bars;i++)
            {
                float x=Mathf.Lerp(left+.032f,right-.032f,i/(float)bars);
                if(close) block.ornament.Rod(wall.At(x,low,railFront),wall.At(x,high,railFront),.014f,iron,4);
                else wall.Panel(block.ornament,x-.015f,low,x+.015f,high,railFront,iron);
            }
            for(int side=-1;side<=1;side+=2)
            {
                float x=o.Mid+side*(width*.5f-.027f);
                block.ornament.Rod(wall.At(x,high,.04f),wall.At(x,high,railFront),.021f,iron,4);
                if(!close) continue;
                block.ornament.Rod(wall.At(x,low,.29f),wall.At(x,high,.29f),.016f,iron,4);
                block.trim.Rod(wall.At(x-side*.12f,baseHeight-.055f,depth*.77f),
                    wall.At(x-side*.12f,Mathf.Min(floor-.22f,baseHeight-.32f),.045f),.056f,stone*.90f,5);
            }
        }

        static void AddCornice(Geo mesh,Elevation wall,float eave,Color stone,bool close)
        {
            wall.Box(mesh,wall.Length*.5f,eave-.12f,.09f,wall.Length,.19f,.31f,stone);
            if(!close) return;
            wall.Box(mesh,wall.Length*.5f,eave-.235f,.047f,wall.Length,.071f,.18f,stone*.91f);
            wall.Box(mesh,wall.Length*.5f,eave+.006f,.11f,wall.Length,.079f,.37f,stone);
            int brackets=Mathf.FloorToInt(wall.Length/.53f);
            for(int j=0;j<brackets;j++)
                wall.Box(mesh,(j+.5f)*wall.Length/brackets,eave-.25f,.106f,.118f,.105f,.153f,stone);
        }
    }
}
