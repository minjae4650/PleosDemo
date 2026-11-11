using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Parkside.Jeju.Editor
{
 public static partial class JejuDriveBuilder
 {
    internal sealed class Geo
    {
        public readonly List<Vector3> v=new List<Vector3>();public readonly List<Vector2> uv=new List<Vector2>();public readonly List<int> t=new List<int>();public readonly List<Color> c=new List<Color>();
        public void Vertex(Vector3 p,Vector2 u,Color colour){v.Add(p);uv.Add(u);c.Add(colour);}
        public void Face(Vector3 a,Vector3 b,Vector3 d,Vector3 e,Color colour)
        {int n=v.Count;Vertex(a,new Vector2(0,0),colour);Vertex(b,new Vector2(1,0),colour);Vertex(d,new Vector2(0,1),colour);Vertex(e,new Vector2(1,1),colour);Quad(t,n,n+1,n+2,n+3);}
        public void Box(Vector3 p,Vector3 size,Quaternion q,Color colour)
        {
            var x=q*Vector3.right*size.x*.5f;var y=q*Vector3.up*size.y*.5f;var z=q*Vector3.forward*size.z*.5f;
            Face(p-x-y-z,p+x-y-z,p-x+y-z,p+x+y-z,colour);Face(p+x-y+z,p-x-y+z,p+x+y+z,p-x+y+z,colour);
            Face(p-x-y+z,p-x-y-z,p-x+y+z,p-x+y-z,colour);Face(p+x-y-z,p+x-y+z,p+x+y-z,p+x+y+z,colour);
            Face(p-x+y-z,p+x+y-z,p-x+y+z,p+x+y+z,colour);Face(p-x-y+z,p+x-y+z,p-x-y-z,p+x-y-z,colour);
        }
        public void Rod(Vector3 a,Vector3 b,float radius,Color colour,int sides=7)
        {
            Vector3 f=(b-a).normalized,r=Vector3.Cross(Mathf.Abs(f.y)>.9f?Vector3.forward:Vector3.up,f).normalized,u=Vector3.Cross(f,r);
            for(int k=0;k<sides;k++){float aa=k*Mathf.PI*2/sides,bb=(k+1)*Mathf.PI*2/sides;Vector3 ra=(r*Mathf.Cos(aa)+u*Mathf.Sin(aa))*radius,rb=(r*Mathf.Cos(bb)+u*Mathf.Sin(bb))*radius;Face(a+rb,a+ra,b+rb,b+ra,colour);}
        }
        public void Rock(Vector3 p,Vector3 scale,float seed,Color colour)
        {
            int n=v.Count;const int rings=5,sides=8;
            for(int j=0;j<=rings;j++)for(int k=0;k<=sides;k++)
            {
                float a=k*Mathf.PI*2/sides,b=j*Mathf.PI/rings;
                float rough=.83f+.22f*Mathf.PerlinNoise(seed+k*.7f,j*.9f+seed);
                Vertex(p+Vector3.Scale(new Vector3(Mathf.Sin(b)*Mathf.Cos(a),Mathf.Cos(b),Mathf.Sin(b)*Mathf.Sin(a))*rough,scale),new Vector2(k*1f/sides,j*1f/rings),colour);
                if(j<rings&&k<sides){int x=n+j*(sides+1)+k;Quad(t,x,x+sides+1,x+1,x+sides+2);}
            }
        }
        public void Card(Vector3 p,float width,float height,float angle,Color colour)
        {
            Vector3 r=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*width*.5f;
            Face(p+r,p-r,p+r+Vector3.up*height,p-r+Vector3.up*height,colour);
        }
        public GameObject Save(string name,Material mat,Transform parent,bool shadow=false,bool collider=false,string assetPath=null)
        {
            if(v.Count==0)return null;
            var local=v.Select(p=>p-parent.position).ToList();
            var go=MeshObject(name,local,t,uv,mat,parent,c,collider,assetPath);
            go.GetComponent<Renderer>().shadowCastingMode=shadow?ShadowCastingMode.On:ShadowCastingMode.Off;
            return go;
        }
    }

 }
}
