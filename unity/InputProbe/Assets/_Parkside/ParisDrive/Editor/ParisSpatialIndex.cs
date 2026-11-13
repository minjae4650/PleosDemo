using System.Collections.Generic;
using UnityEngine;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        sealed class ParisStreetLookup
        {
            internal readonly struct Segment
            {
                internal readonly Road road;
                internal readonly Vector3 a,b;
                internal Segment(Road road,Vector3 a,Vector3 b){this.road=road;this.a=a;this.b=b;}
            }

            const float CellSize=38;
            readonly Dictionary<Vector2Int,List<Segment>> cells=new Dictionary<Vector2Int,List<Segment>>();
            readonly Dictionary<Vector2Int,List<Rect>> buildingCells=new Dictionary<Vector2Int,List<Rect>>();

            internal ParisStreetLookup(Source source)
            {
                foreach(var r in source.roads)
                    for(int i=0;i<r.points.Length-1;i++)
                    {
                        Vector3 a=r.points[i],b=r.points[i+1];
                        if((a-b).sqrMagnitude<.04f) continue;
                        var lo=Cell(Vector3.Min(a,b));var hi=Cell(Vector3.Max(a,b));
                        for(int x=lo.x;x<=hi.x;x++)
                            for(int z=lo.y;z<=hi.y;z++)
                            {
                                var key=new Vector2Int(x,z);
                                if(!cells.TryGetValue(key,out var list)) cells[key]=list=new List<Segment>();
                                list.Add(new Segment(r,a,b));
                            }
                    }
                foreach(var building in source.buildings)
                {
                    if(building.points.Length==0) continue;
                    var min=building.points[0];
                    var max=min;
                    foreach(var p in building.points)
                    {
                        min=Vector3.Min(min,p);
                        max=Vector3.Max(max,p);
                    }
                    var bounds=Rect.MinMaxRect(min.x,min.z,max.x,max.z);
                    var lo=Cell(min);
                    var hi=Cell(max);
                    for(int x=lo.x;x<=hi.x;x++)
                        for(int z=lo.y;z<=hi.y;z++)
                        {
                            var key=new Vector2Int(x,z);
                            if(!buildingCells.TryGetValue(key,out var list)) buildingCells[key]=list=new List<Rect>();
                            list.Add(bounds);
                        }
                }
            }

            static Vector2Int Cell(Vector3 p)=>new Vector2Int(Mathf.FloorToInt(p.x/CellSize),Mathf.FloorToInt(p.z/CellSize));

            internal bool InBuilding(Vector3 p,float clearance)
            {
                var offset=Vector3.one*clearance;
                var lo=Cell(p-offset);
                var hi=Cell(p+offset);
                for(int x=lo.x;x<=hi.x;x++)
                    for(int z=lo.y;z<=hi.y;z++)
                    {
                        if(!buildingCells.TryGetValue(new Vector2Int(x,z),out var list)) continue;
                        foreach(var b in list)
                            if(p.x>=b.xMin-clearance&&p.x<=b.xMax+clearance&&
                                p.z>=b.yMin-clearance&&p.z<=b.yMax+clearance) return true;
                    }
                return false;
            }

            internal bool Nearest(Vector3 position,float radius,out Vector3 p,out Vector3 heading,out Road road,string streetName=null,Vector3 facing=default)
            {
                p=position;heading=Vector3.forward;road=null;
                var centre=Cell(position);
                int extent=Mathf.CeilToInt(radius/CellSize);
                float best=radius*radius;
                for(int x=-extent;x<=extent;x++)
                    for(int z=-extent;z<=extent;z++)
                    {
                        if(!cells.TryGetValue(centre+new Vector2Int(x,z),out var segments)) continue;
                        foreach(var s in segments)
                        {
                            if(streetName!=null&&s.road.name!=streetName) continue;
                            Vector3 delta=s.b-s.a;
                            Vector3 off=position-s.a;off.y=0;
                            float t=Mathf.Clamp01(Vector3.Dot(off,delta)/delta.sqrMagnitude);
                            Vector3 point=s.a+delta*t;
                            float sq=(point-position).sqrMagnitude;
                            if(sq>=best) continue;
                            if(facing!=Vector3.zero&&(s.road.bridge||sq<Mathf.Pow(s.road.width*.5f+.24f,2)||
                                Vector3.Dot(point-position,facing)<Mathf.Sqrt(sq)*.54f)) continue;
                            best=sq;p=point;heading=delta.normalized;road=s.road;
                        }
                    }
                return road!=null;
            }
        }

    }
}
