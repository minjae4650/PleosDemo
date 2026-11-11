using System.Linq;
using Parkside.Jeju.Editor;
using UnityEngine;
using Geo=Parkside.Jeju.Editor.JejuDriveBuilder.Geo;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        static Vector3[] RoofRim(Vector3[] footprint,Vector3 centre,float inset) =>
            footprint.Select(p=>Vector3.Lerp(p,centre,inset)).ToArray();

        static int RoofFace(Block block,Elevation wall,Vector3 breakA,Vector3 breakB,
            Vector3 crownA,Vector3 crownB,float eave,float roofHeight,int bays,Color zinc,int style,bool close)
        {
            Vector3 a=wall.At(0,eave),b=wall.At(wall.Length,eave);
            Vector3 c=breakA+Vector3.up*(eave+roofHeight*.76f);
            Vector3 d=breakB+Vector3.up*(eave+roofHeight*.76f);
            Vector3 topA=crownA+Vector3.up*(eave+roofHeight);
            Vector3 topB=crownB+Vector3.up*(eave+roofHeight);
            block.roofs.Face(a,b,c,d,zinc);
            block.roofs.Face(c,d,topA,topB,zinc*.94f);
            if(!close||roofHeight<1.38f||wall.Length<5.3f) return 0;

            int count=0;
            float bay=wall.Length/bays;
            for(int j=0;j<bays;j++)
            {
                if((j+style)%2!=0) continue;
                float x=(j+.5f)*bay;
                float width=Mathf.Min(1.05f,bay*.53f);
                float floor=eave+roofHeight*.17f;
                float head=floor+Mathf.Min(1.02f,roofHeight*.43f);
                float inset=.14f;
                float sideDepth=Mathf.Max(.32f,Vector3.Dot(wall.Origin-breakA,wall.Outward)*.78f);
                var shell=block.trim;
                var paint=new Color(.60f,.60f,.53f);
                var frame=new Opening(x-width*.5f,x+width*.5f,floor,head,.15f);

                wall.Box(shell,x,floor-.034f,-inset,width+.25f,.088f,.37f,paint);
                wall.Box(shell,x-width*.5f-.047f,(floor+head)*.5f,-inset,.115f,head-floor,.18f,paint);
                wall.Box(shell,x+width*.5f+.047f,(floor+head)*.5f,-inset,.115f,head-floor,.18f,paint);
                wall.Panel(block.panes,frame.Left+.015f,floor,frame.Right-.015f,head,-inset-.074f,
                    new Color(.23f,.31f,.35f));
                wall.Box(block.ornament,x,(floor+head)*.5f,-inset-.060f,.035f,head-floor,.04f,paint*.61f);

                float outer=width*.5f+.12f;
                float peak=head+Mathf.Min(.30f,roofHeight*.12f);
                Vector3 p0=wall.At(x-outer,head,-inset+.08f);
                Vector3 p1=wall.At(x+outer,head,-inset+.08f);
                Vector3 ridge=wall.At(x,peak,-inset+.08f);
                Vector3 backRidge=wall.At(x,peak+.12f,-inset-sideDepth);
                shell.Face(p0,p1,ridge,ridge,paint);
                block.roofs.Face(ridge,p0,backRidge,wall.At(x-outer,head+.14f,-inset-sideDepth),zinc*.88f);
                block.roofs.Face(p1,ridge,wall.At(x+outer,head+.14f,-inset-sideDepth),backRidge,zinc*.88f);
                shell.Face(wall.At(x-outer,floor,-inset),wall.At(x-outer,floor,-inset-sideDepth),
                    p0,wall.At(x-outer,head+.14f,-inset-sideDepth),paint*.88f);
                shell.Face(wall.At(x+outer,floor,-inset-sideDepth),wall.At(x+outer,floor,-inset),
                    wall.At(x+outer,head+.14f,-inset-sideDepth),p1,paint*.92f);
                count++;
            }
            return count;
        }

        static void AddChimneys(Block block,Vector3[] rim,float height,int style)
        {
            if(rim.Length<4||height<8f) return;
            int longest=0;
            for(int i=1;i<rim.Length;i++)
                if((rim[(i+1)%rim.Length]-rim[i]).sqrMagnitude>(rim[(longest+1)%rim.Length]-rim[longest]).sqrMagnitude)
                    longest=i;
            var a=rim[longest];
            var b=rim[(longest+1)%rim.Length];
            if((b-a).sqrMagnitude<27f) return;
            var wall=new Elevation(a,b);
            var brick=Color.Lerp(new Color(.55f,.38f,.31f),new Color(.72f,.65f,.52f),style%4/4f);
            for(int j=0;j<2;j++)
            {
                float x=wall.Length*(j==0?.29f:.76f);
                wall.Box(block.trim,x,height+.47f,-.72f,.81f,1.08f,.57f,brick);
                wall.Box(block.trim,x,height+1.02f,-.72f,.94f,.095f,.70f,brick*.84f);
                for(int pot=-1;pot<=1;pot+=2)
                {
                    var p=wall.At(x+pot*.18f,height+1.065f,-.72f);
                    block.ornament.Rod(p,p+Vector3.up*.24f,.089f,new Color(.52f,.32f,.23f),8);
                }
            }
        }
    }
}
