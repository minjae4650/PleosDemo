using System.Collections.Generic;
using UnityEngine;

namespace Parkside.Jeju.Editor
{
 public static partial class JejuDriveBuilder
 {
    struct BedVertex
    {
        public Vector3 p;public Vector2 uv;public Color colour;
        public static BedVertex Between(BedVertex a,BedVertex b,float t)=>new BedVertex{p=Vector3.Lerp(a.p,b.p,t),uv=Vector2.Lerp(a.uv,b.uv,t),colour=Color.Lerp(a.colour,b.colour,t)};
    }
    static float Side(Vector3 a,Vector3 b,Vector3 p)=>(b.x-a.x)*(p.z-a.z)-(b.z-a.z)*(p.x-a.x);
    static List<BedVertex> HalfPlane(List<BedVertex> input,Vector3 a,Vector3 b,bool inside)
    {
        var result=new List<BedVertex>();if(input.Count==0)return result;
        var previous=input[input.Count-1];float previousDistance=Side(a,b,previous.p)*(inside?1:-1);bool previousIn=previousDistance>=-1e-5f;
        foreach(var current in input)
        {
            float distance=Side(a,b,current.p)*(inside?1:-1);bool currentIn=distance>=-1e-5f;
            if(currentIn!=previousIn)result.Add(BedVertex.Between(previous,current,previousDistance/(previousDistance-distance)));
            if(currentIn)result.Add(current);
            previous=current;previousDistance=distance;previousIn=currentIn;
        }
        return result;
    }
    static float PolygonArea(List<BedVertex> polygon)
    {
        double area=0;for(int i=0;i<polygon.Count;i++){var a=polygon[i].p;var b=polygon[(i+1)%polygon.Count].p;area+=(double)a.x*b.z-(double)b.x*a.z;}return (float)System.Math.Abs(area*.5);
    }
    static Vector3[] RoadQuad(SegmentRef segment)
    {
        var r=segment.road;int i=segment.i;
        Vector3 a=r.p[i],b=r.p[i+1];
        Vector3 tangent=b-a;tangent.y=0;tangent.Normalize();a-=tangent*.08f;b+=tangent*.08f;
        Vector3 ra=Vector3.Cross(Vector3.up,r.p[Mathf.Min(i+1,r.p.Length-1)]-r.p[Mathf.Max(0,i-1)]).normalized;
        Vector3 rb=Vector3.Cross(Vector3.up,r.p[Mathf.Min(i+2,r.p.Length-1)]-r.p[i]).normalized;
        float wa=r.w[i]*.5f-.025f,wb=r.w[Mathf.Min(i+1,r.w.Length-1)]*.5f-.025f;
        var quad=new[]{a-ra*wa,a+ra*wa,b+rb*wb,b-rb*wb};
        if(Side(quad[0],quad[1],quad[2])<0)System.Array.Reverse(quad);
        return quad;
    }
    static List<Vector3[]> RoadFootprints(SegmentRef s)
    {
        var q=RoadQuad(s);bool convex=true;
        for(int i=0;i<4;i++)if(Side(q[i],q[(i+1)%4],q[(i+2)%4])<-.0001f)convex=false;
        if(convex)return new List<Vector3[]>{q};
        // At a tight bend the outer quad can fold. Subtract the actual strip's
        // triangle union rather than treating a concave/bow-tie quad as convex.
        var r=s.road;int k=s.i;var a=r.p[k];var b=r.p[k+1];
        Vector3 tangent=b-a;tangent.y=0;tangent.Normalize();a-=tangent*.08f;b+=tangent*.08f;
        var ra=Vector3.Cross(Vector3.up,r.p[Mathf.Min(k+1,r.p.Length-1)]-r.p[Mathf.Max(0,k-1)]).normalized;
        var rb=Vector3.Cross(Vector3.up,r.p[Mathf.Min(k+2,r.p.Length-1)]-r.p[k]).normalized;
        float[] wa={-r.w[k]*.5f,-.25f,0,.25f,r.w[k]*.5f},wb={-r.w[k+1]*.5f,-.25f,0,.25f,r.w[k+1]*.5f};
        var result=new List<Vector3[]>();
        for(int j=0;j<4;j++)
        {
            var left=a+ra*wa[j];var right=a+ra*wa[j+1];var nextLeft=b+rb*wb[j];var nextRight=b+rb*wb[j+1];
            foreach(var triangle in new[]{new[]{left,right,nextLeft},new[]{right,nextRight,nextLeft}})
            {if(Side(triangle[0],triangle[1],triangle[2])<0)System.Array.Reverse(triangle);result.Add(triangle);}
        }
        return result;
    }
    // Terrain ribbons may overlap on tight bends. Remove their projected road
    // footprint geometrically, rather than hiding paint or weakening coverage checks.
    static void ClipRoadBed(ref List<Vector3> positions,ref List<int> triangles,ref List<Vector2> uvs,ref List<Color> colours,Transform parent)
    {
        if(triangles.Count==0)return;
        var outputTriangles=new List<int>(triangles.Count);var outPositions=new List<Vector3>(positions);var outUV=new List<Vector2>(uvs);var outColors=new List<Color>(colours);
        var matrix=parent.localToWorldMatrix;var inverse=parent.worldToLocalMatrix;
        for(int triangle=0;triangle<triangles.Count;triangle+=3)
        {
            var original=new List<BedVertex>(3);
            for(int j=0;j<3;j++){int index=triangles[triangle+j];original.Add(new BedVertex{p=matrix.MultiplyPoint3x4(positions[index]),uv=uvs[index],colour=colours[index]});}
            Vector3 minimum=original[0].p,maximum=minimum;
            foreach(var v in original){minimum=Vector3.Min(minimum,v.p);maximum=Vector3.Max(maximum,v.p);}
            int minX=Mathf.FloorToInt((minimum.x-9)/50),maxX=Mathf.FloorToInt((maximum.x+9)/50),minZ=Mathf.FloorToInt((minimum.z-9)/50),maxZ=Mathf.FloorToInt((maximum.z+9)/50);
            var fragments=new List<List<BedVertex>>{original};bool changed=false;
            for(int z=minZ;z<=maxZ&&fragments.Count>0;z++)for(int x=minX;x<=maxX&&fragments.Count>0;x++)
            {
                if(!roadGrid.TryGetValue(new Vector2Int(x,z),out var segments))continue;
                foreach(var segment in segments)
                {
                    Vector3 a=segment.road.p[segment.i],b=segment.road.p[segment.i+1];float radius=segment.road.w[segment.i]*.5f+.1f;
                    if(Mathf.Min(a.x,b.x)-radius>maximum.x||Mathf.Max(a.x,b.x)+radius<minimum.x||Mathf.Min(a.z,b.z)-radius>maximum.z||Mathf.Max(a.z,b.z)+radius<minimum.z)continue;
                    foreach(var quad in RoadFootprints(segment))
                    {
                    var next=new List<List<BedVertex>>();
                    foreach(var polygon in fragments)
                    {
                        var intersection=polygon;for(int edge=0;edge<quad.Length&&intersection.Count>0;edge++)intersection=HalfPlane(intersection,quad[edge],quad[(edge+1)%quad.Length],true);
                        if(intersection.Count<3||PolygonArea(intersection)<.00005f){next.Add(polygon);continue;}
                        changed=true;var remaining=polygon;
                        for(int edge=0;edge<quad.Length&&remaining.Count>0;edge++)
                        {
                            var outside=HalfPlane(remaining,quad[edge],quad[(edge+1)%quad.Length],false);
                            if(outside.Count>=3&&PolygonArea(outside)>.00001f)next.Add(outside);
                            remaining=HalfPlane(remaining,quad[edge],quad[(edge+1)%quad.Length],true);
                        }
                    }
                    fragments=next;if(fragments.Count==0)break;
                    }
                }
            }
            if(!changed){outputTriangles.Add(triangles[triangle]);outputTriangles.Add(triangles[triangle+1]);outputTriangles.Add(triangles[triangle+2]);continue;}
            foreach(var polygon in fragments)
            {
                int first=outPositions.Count;foreach(var v in polygon){outPositions.Add(inverse.MultiplyPoint3x4(v.p));outUV.Add(v.uv);outColors.Add(v.colour);}
                for(int i=1;i<polygon.Count-1;i++){outputTriangles.Add(first);outputTriangles.Add(first+i);outputTriangles.Add(first+i+1);}
            }
        }
        positions=outPositions;triangles=outputTriangles;uvs=outUV;colours=outColors;
    }
 }
}
