using System;
using UnityEngine;

namespace Parkside.Jeju
{
    [Serializable] public sealed class JejuRoadBranch
    {
        public string name;
        public Vector3[] points;
        public float[] widths;
    }
    public struct JejuRoadProjection
    {
        public Vector3 point, forward, right;
        public float width, fraction;
        public string name;
        public int mainIndex;
    }
    [Serializable] public sealed class JejuStation
    {
        public string name;
        public string subtitle;
        public int index;
        public Vector3 lookAt;
        public bool branchSpawn;
        public Vector3 position,forward;
    }

    /// <summary>Metre-scale route, derived from OSM and sampled against a real DEM.</summary>
    public sealed class JejuDriveRoute : MonoBehaviour
    {
        public Vector3[] points;
        public float[] distances;
        public float[] widths;
        public JejuStation[] stations;
        public Texture2D map;
        public Vector2 mapMin;
        public Vector2 mapMax;
        public Font font;
        public string[] roadNames;
        public JejuRoadBranch[] branches;
        public float Length => distances == null || distances.Length == 0 ? 0 : distances[distances.Length - 1];

        public int Nearest(Vector3 position, out Vector3 centre, out float fraction)
        {
            float best = float.PositiveInfinity;
            int index = 0;
            fraction = 0;
            centre = points[0];
            // A 20 km route contains only a few thousand segments; no scene-wide physics query is needed.
            for (int i = 0; i < points.Length - 1; i++)
            {
                float d = ProjectSegment(position, points[i], points[i+1], out var projected, out float t);
                if (d >= best) continue;
                best = d; centre = projected; fraction = t; index = i;
            }
            return index;
        }

        public Vector3 AtDistance(float distance, out Vector3 forward, out float width)
        {
            distance = Mathf.Clamp(distance, 0, Length - .01f);
            int index = Array.BinarySearch(distances, distance);
            if (index < 0) index = Mathf.Max(0, ~index - 1);
            index = Mathf.Min(index, points.Length - 2);
            float t = Mathf.InverseLerp(distances[index], distances[index + 1], distance);
            Vector3 a=(points[index+1]-points[Mathf.Max(0,index-1)]).normalized;
            Vector3 b=(points[Mathf.Min(points.Length-1,index+2)]-points[index]).normalized;
            forward=Vector3.Slerp(a,b,t).normalized;
            width = Mathf.Lerp(widths[index],widths[index+1],t);
            return Vector3.Lerp(points[index], points[index + 1], t);
        }

        public Vector3 Right(int index)
        {
            Vector3 forward = points[Mathf.Min(points.Length - 1, index + 1)] - points[Mathf.Max(0, index - 1)];
            return Vector3.Cross(Vector3.up, forward).normalized;
        }

        public JejuRoadProjection ProjectDrivable(Vector3 position)
        {
            int main=Nearest(position,out Vector3 centre,out float fraction);
            Vector3 delta=points[main+1]-points[main];
            var result=new JejuRoadProjection{point=centre,forward=delta.normalized,right=Right(main),width=widths[main],fraction=fraction,mainIndex=main,name=roadNames!=null&&main<roadNames.Length?roadNames[main]:"한라산·해안 연결"};
            float best=Vector2.SqrMagnitude(new Vector2(position.x-centre.x,position.z-centre.z));
            if(branches==null)return result;
            foreach(var branch in branches)
            {
                for(int i=0;i<branch.points.Length-1;i++)
                {
                    Vector3 edge=branch.points[i+1]-branch.points[i];
                    float d=ProjectSegment(position,branch.points[i],branch.points[i+1],out var p,out _);
                    if(d>=best)continue;
                    best=d;result.point=p;result.forward=edge.normalized;result.right=Vector3.Cross(Vector3.up,edge).normalized;
                    result.width=branch.widths[Mathf.Min(i,branch.widths.Length-1)];result.name=branch.name;
                }
            }
            return result;
        }
        public static float LaneOffset(float width) => Mathf.Min(1.8f,width*.23f);

        static float ProjectSegment(Vector3 position, Vector3 a, Vector3 b, out Vector3 point, out float fraction)
        {
            Vector3 delta=b-a;
            Vector2 flat=new Vector2(delta.x,delta.z);
            Vector2 offset=new Vector2(position.x-a.x,position.z-a.z);
            fraction=Mathf.Clamp01(Vector2.Dot(offset,flat)/Mathf.Max(.001f,flat.sqrMagnitude));
            point=a+delta*fraction;
            return new Vector2(position.x-point.x,position.z-point.z).sqrMagnitude;
        }
    }
}
