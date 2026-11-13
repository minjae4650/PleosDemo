using System;
using System.Collections.Generic;
using UnityEngine;

namespace Parkside.Jeju
{
    [Serializable] public sealed class JejuTrafficNode
    {
        public long osmId;
        public Vector3 point;
        public bool roundabout;
        public int signal = -1;
        [NonSerialized] public int degree;
    }
    [Serializable] public sealed class JejuTrafficEdge
    {
        public int fromNode, toNode, lanes, signalApproach;
        public long osmWayId;
        public string name, turnLanes;
        public bool oneWay,roundabout,mappedSignal;
        public Vector3[] points;
        public float[] distances;
        public float width, speed;
        public int[] allowedNext;
        [NonSerialized] public Vector3[] lane;
        [NonSerialized] public float[] laneDistances;
        public float trimStart, trimEnd;
        public float Length => laneDistances[laneDistances.Length-1];
        public Vector3 At(float distance, out Vector3 forward) => JejuTrafficNetwork.Sample(lane,laneDistances,distance,out forward);
    }
    [Serializable] public sealed class JejuTrafficNetwork
    {
        public JejuTrafficNode[] nodes;
        public JejuTrafficEdge[] edges;
        public bool preserveTrim, checkTurnFootprints;
        sealed class Search {public int[] previous,arrivals,destinations;}
        [NonSerialized] Dictionary<int,Search> searches;
        [NonSerialized] Queue<int> searchOrder;
        public void Prepare()
        {
            searches=new Dictionary<int,Search>();
            searchOrder=new Queue<int>();
            foreach(var n in nodes)n.degree=0;
            var neighbours=new HashSet<int>[nodes.Length];for(int i=0;i<nodes.Length;i++)neighbours[i]=new HashSet<int>();
            foreach(var e in edges){neighbours[e.fromNode].Add(e.toNode);neighbours[e.toNode].Add(e.fromNode);}
            for(int i=0;i<nodes.Length;i++)nodes[i].degree=neighbours[i].Count;
            foreach(var e in edges)
            {
                float total=e.distances[e.distances.Length-1];
                if(!preserveTrim)
                {
                    e.trimStart=Mathf.Min(total*.2f,nodes[e.fromNode].degree>=3?12:4);
                    e.trimEnd=Mathf.Min(total*.2f,nodes[e.toNode].degree>=3?12:4);
                }
                var p=new List<Vector3>();
                float end=total-e.trimEnd,offset=LaneOffset(e,total);
                for(float d=e.trimStart;d<end;d+=2)p.Add(LanePoint(e,d,offset));
                p.Add(LanePoint(e,end,offset));if(p.Count<2)p.Insert(0,LanePoint(e,e.trimStart,offset));
                e.lane=p.ToArray();e.laneDistances=Distances(e.lane);
            }
        }
        static float LaneOffset(JejuTrafficEdge edge,float length)
        {
            float offset=JejuDriveRoute.LaneOffset(edge.width);
            if (!edge.oneWay) return offset;
            if (edge.lanes<=1) return 0;
            // Circulatory lanes and very short connecting arms follow the
            // inner lane through their tight junction footprint.
            return edge.roundabout||length<30 ? -offset : offset;
        }

        static Vector3 LanePoint(JejuTrafficEdge edge,float distance,float offset)
        {
            Vector3 point=Sample(edge.points,edge.distances,distance,out var forward);
            return point+Vector3.Cross(Vector3.up,forward).normalized*offset;
        }
        public static float[] Distances(Vector3[] points)
        {
            var ds=new float[points.Length];for(int i=1;i<ds.Length;i++)ds[i]=ds[i-1]+Vector3.Distance(points[i-1],points[i]);return ds;
        }
        public static Vector3 Sample(Vector3[] p,float[] ds,float d,out Vector3 forward)
        {
            int index=Array.BinarySearch(ds,d);index=Mathf.Clamp(index<0?~index-1:index,0,p.Length-2);
            float t=Mathf.InverseLerp(ds[index],ds[index+1],d);forward=(p[index+1]-p[index]).normalized;
            return Vector3.Lerp(p[index],p[index+1],t);
        }
        // Dijkstra's state is the incoming edge, so OSM no/only-turn rules survive routing.
        public bool FindPath(int startEdge,int destination,List<int> result)
        {
            result.Clear();var search=SearchFrom(startEdge);int at=search.arrivals[destination];
            if(at<0)return false;
            for(int i=at;i>=0;i=search.previous[i])result.Add(i);result.Reverse();return true;
        }
        public int[] Destinations(int startEdge)=>SearchFrom(startEdge).destinations;
        Search SearchFrom(int startEdge)
        {
            if(searches.TryGetValue(startEdge,out var cached))return cached;
            int count=edges.Length;
            var cost=new float[count];var prev=new int[count];
            var arrivals=new int[nodes.Length];for(int i=0;i<arrivals.Length;i++)arrivals[i]=-1;
            for(int i=0;i<count;i++){cost[i]=float.PositiveInfinity;prev[i]=-1;}cost[startEdge]=0;
            var pending=new SortedSet<(float cost,int edge)>{(0,startEdge)};
            while(pending.Count>0)
            {
                var current=pending.Min;
                pending.Remove(current);
                var (best,at)=current;
                if(arrivals[edges[at].toNode]<0)arrivals[edges[at].toNode]=at;
                foreach(int next in edges[at].allowedNext)
                {
                    float candidate=best+edges[next].Length/Mathf.Max(3,edges[next].speed);
                    if(candidate>=cost[next])continue;
                    pending.Remove((cost[next],next));
                    cost[next]=candidate;prev[next]=at;
                    pending.Add((candidate,next));
                }
            }
            var reachable=new List<int>();for(int i=0;i<arrivals.Length;i++)if(arrivals[i]>=0)reachable.Add(i);
            if(searchOrder.Count>=64)searches.Remove(searchOrder.Dequeue());
            cached=new Search{previous=prev,arrivals=arrivals,destinations=reachable.ToArray()};
            searches.Add(startEdge,cached);searchOrder.Enqueue(startEdge);return cached;
        }
    }
}
