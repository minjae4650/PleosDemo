using System;
using UnityEngine;
using Geo = Parkside.Jeju.Editor.JejuDriveBuilder.Geo;

namespace Parkside.Streets.Editor
{
    internal static class StreetFinishGeometry
    {
        internal static void Rect(Geo g,Vector3 lowerLeft,Vector3 right,Vector3 up,Rect uv,Color colour)
        {
            int n=g.v.Count;
            g.Vertex(lowerLeft,new Vector2(uv.xMin,uv.yMin),colour);
            g.Vertex(lowerLeft+right,new Vector2(uv.xMax,uv.yMin),colour);
            g.Vertex(lowerLeft+up,new Vector2(uv.xMin,uv.yMax),colour);
            g.Vertex(lowerLeft+right+up,new Vector2(uv.xMax,uv.yMax),colour);
            g.t.Add(n);g.t.Add(n+2);g.t.Add(n+1);
            g.t.Add(n+1);g.t.Add(n+2);g.t.Add(n+3);
        }

        internal static void Letter(Transform parent,Font font,string value,Vector3 p,Vector3 outward,
            float maxWidth,float height,Color colour)
        {
            var go=new GameObject("Lettering · "+value);
            go.transform.SetParent(parent,false);
            go.transform.SetPositionAndRotation(p,Quaternion.LookRotation(-outward));
            var text=go.AddComponent<TextMesh>();
            text.text=value;text.font=font;text.fontSize=72;text.fontStyle=FontStyle.Bold;
            text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;
            text.characterSize=1;text.color=colour;
            var renderer=go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=font.material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            var bounds=renderer.localBounds.size;
            float scale=Mathf.Min(maxWidth/Mathf.Max(.03f,bounds.x),height/Mathf.Max(.03f,bounds.y));
            if(bounds.x<.01f||bounds.y<.01f) scale=Mathf.Min(height*.15f,maxWidth/Mathf.Max(3,value.Length*5));
            go.transform.localScale=Vector3.one*scale;
        }

        internal static bool RoadPoint(Vector3 query,out Vector3 point)
        {
            if(Physics.Raycast(query+Vector3.up*2,Vector3.down,out var hit,4,~(1<<2),QueryTriggerInteraction.Ignore)&&
                hit.collider.name=="Asphalt")
            {
                point=hit.point;
                return true;
            }
            point=query;
            return false;
        }

        internal static bool RoadPatch(Geo mesh,Vector3 p,Vector3 forward,float width,float length)
        {
            var right=Vector3.Cross(Vector3.up,forward).normalized*width*.5f;
            forward=forward.normalized*length*.5f;
            if(!RoadPoint(p-forward-right,out var a)||!RoadPoint(p-forward+right,out var b)||
                !RoadPoint(p+forward-right,out var c)||!RoadPoint(p+forward+right,out var d)) return false;
            mesh.Face(a+Vector3.up*.026f,b+Vector3.up*.026f,c+Vector3.up*.026f,d+Vector3.up*.026f,Color.white);
            return true;
        }

        internal static void Cover(Geo mesh,Vector3 p,float radius,bool drain)
        {
            var iron=new Color(.22f,.22f,.20f);
            var rim=new Color(.34f,.34f,.32f);
            if(drain)
            {
                mesh.Box(p+Vector3.up*.009f,new Vector3(.38f,.018f,.61f),Quaternion.identity,iron);
                for(int i=-4;i<=4;i++)
                    mesh.Box(p+new Vector3(0,.022f,i*.062f),new Vector3(.31f,.009f,.014f),Quaternion.identity,rim);
                return;
            }
            int centre=mesh.v.Count;
            mesh.Vertex(p+Vector3.up*.018f,new Vector2(.5f,.5f),iron);
            const int sides=24;
            for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2/sides;
                var x=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                mesh.Vertex(p+x*radius+Vector3.up*.018f,Vector2.zero,iron);
                mesh.t.Add(centre);mesh.t.Add(centre+1+(i+1)%sides);mesh.t.Add(centre+1+i);
            }
            for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;
                var x=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                var y=new Vector3(Mathf.Cos(b),0,Mathf.Sin(b));
                mesh.Rod(p+x*(radius*.93f)+Vector3.up*.019f,p+y*(radius*.93f)+Vector3.up*.019f,.009f,rim,4);
            }
            for(int x=-2;x<=2;x++)
                for(int z=-2;z<=2;z++)
                    mesh.Box(p+new Vector3(x*radius*.28f,.022f,z*radius*.28f),new Vector3(.028f,.008f,.028f),Quaternion.identity,rim);
        }

        internal static void Awning(Geo g,Vector3 centre,Vector3 along,Vector3 outward,float width,float projection,Color fabric)
        {
            const int stripes=12;
            for(int i=0;i<stripes;i++)
            {
                float w=width/stripes;
                var a=centre+along*(w*(i-stripes*.5f));
                var b=a+along*w;
                var drop=outward*projection-Vector3.up*.25f;
                Color tint=i%4==0?Color.Lerp(fabric,new Color(.87f,.80f,.65f),.56f):fabric;
                g.Face(a,b,a+drop,b+drop,tint);
                g.Face(b,a,b+drop,a+drop,tint*.87f);
                g.Face(b+drop,a+drop,b+drop-Vector3.up*.12f,a+drop-Vector3.up*.12f,tint*.9f);
            }
        }

        internal static void PhotoCamera(Camera camera,Shader shader,float exposure)
        {
            if(shader==null||!shader.isSupported) return;
            var aa=camera.GetComponent<ParkingLotEnvironment.CloudPilot.MapCameraFXAA>();
            if(aa!=null) aa.enabled=false;
            var finish=camera.GetComponent<ParkingLotEnvironment.ParkingLotPhotoFinish>();
            if(finish==null) finish=camera.gameObject.AddComponent<ParkingLotEnvironment.ParkingLotPhotoFinish>();
            finish.shader=shader;
            finish.exposure=exposure;
        }
    }
}
