using System;
using System.Collections.Generic;
using UnityEngine;

namespace Parkside.Jeju
{
    /// <summary>Bounded deterministic traffic on the OSM directed graph. Stepped once by the player simulation.</summary>
    public sealed partial class JejuAmbientTraffic : MonoBehaviour
    {
        [Serializable] public sealed class Vehicle
        {
            public Transform body;
            [NonSerialized] public List<int> path=new List<int>();
            [NonSerialized] public int leg, destination, reservedNode=-1, reservedCircle=-1;
            [NonSerialized] public float distance, speed, factor, travelled, waiting;
            [NonSerialized] public Vector3[] turn;
            [NonSerialized] public float[] turnDistances;
            [NonSerialized] public bool active, inTurn, clearingCircle;
            [NonSerialized] public Vector3 position, heading;
            [NonSerialized] public Renderer[] brakeLamps;
            [NonSerialized] public Transform[] indicators;
        }
        public TextAsset networkSource;
        public GameObject[] vehiclePrefabs;
        public JejuTrafficSystem signals;
        public JejuDriveController player;
        public int population=24;
        public float spawnMin=70,spawnMax=420,despawnDistance=650;
        public float destinationMaxDistance;
        public bool spawning=true;
        public JejuTrafficNetwork Network {get;private set;}
        public readonly List<Vehicle> vehicles=new List<Vehicle>();
        public int Spawned {get;private set;}
        public int Arrivals {get;private set;}
        public int CompletedTurns {get;private set;}
        public int Recycled {get;private set;}
        public int ActiveCount {get {int n=0;foreach(var v in vehicles)if(v.active)n++;return n;}}
        public int RouteChoices {get;private set;}
        System.Random random;
        int[] reservations;
        int[] circleGroup,circleReservations;
        bool[] wideCircles;
        int playerCircle=-1;
        Vector3 playerCircleCentre;
        float playerCircleRadius,guideCircleGap=float.PositiveInfinity;
        readonly List<JejuTrafficEdge> circleEntries=new List<JejuTrafficEdge>();
        public float GuidedCircleStop=>guideCircleGap;
        float spawnTimer,clock;
        bool prepared;
        struct SpawnSite {public int edge;public float distance;}
        readonly List<SpawnSite> spawnSites=new List<SpawnSite>();
        readonly Dictionary<int,Vector3[]> turnPaths=new Dictionary<int,Vector3[]>();
        float siteTimer;
        Vector3 siteOrigin;
        MaterialPropertyBlock lampBlock;

        public void Prepare()
        {
            if(prepared)return;
            Network=JsonUtility.FromJson<JejuTrafficNetwork>(networkSource.text);Network.Prepare();
            random=new System.Random(6112026);reservations=new int[Network.nodes.Length];
            for(int i=0;i<reservations.Length;i++)reservations[i]=-1;
            circleGroup=new int[Network.nodes.Length];circleReservations=new int[Network.nodes.Length];
            for(int i=0;i<circleGroup.Length;i++){circleGroup[i]=i;circleReservations[i]=-1;}
            foreach(var e in Network.edges)if(e.roundabout)
            {
                int a=CircleGroup(e.fromNode),b=CircleGroup(e.toNode);circleGroup[b]=a;
            }
            wideCircles=new bool[Network.nodes.Length];
            if(Network.checkTurnFootprints)
            {
                foreach(var e in Network.edges)if(e.roundabout&&e.width>=11f)
                    wideCircles[CircleGroup(e.fromNode)]=true;
                foreach(var e in Network.edges)if(e.roundabout&&e.width<11f)
                    wideCircles[CircleGroup(e.fromNode)]=false;
            }
            foreach(var e in Network.edges)if(!e.roundabout&&Array.Exists(e.allowedNext,i=>Network.edges[i].roundabout))circleEntries.Add(e);
            for(int i=0;i<population;i++)
            {
                var go=Instantiate(vehiclePrefabs[i%vehiclePrefabs.Length],transform);go.name="Traffic_"+i;go.SetActive(false);
                var v=new Vehicle{body=go.transform,factor=.72f+(float)random.NextDouble()*.22f};
                var brake=new List<Renderer>();var indicators=new List<Transform>();
                foreach(var r in go.GetComponentsInChildren<Renderer>(true))if(r.name=="NPC_BrakeLamp")brake.Add(r);
                foreach(var t in go.GetComponentsInChildren<Transform>(true))if(t.name.StartsWith("NPC_Turn_"))indicators.Add(t);
                v.brakeLamps=brake.ToArray();v.indicators=indicators.ToArray();vehicles.Add(v);
            }
            lampBlock=new MaterialPropertyBlock();prepared=true;
        }
        public void ResetPopulation()
        {
            Prepare();if(playerCircle>=0){circleReservations[playerCircle]=-1;playerCircle=-1;}foreach(var v in vehicles)Retire(v);spawnTimer=0;siteTimer=0;spawnSites.Clear();
        }
        public void Simulate(float dt)
        {
            Prepare();clock+=dt;spawnTimer-=dt;
            UpdatePlayerCircle();
            siteTimer-=dt;
            if(spawning&&(siteTimer<=0||Horizontal(player.transform.position-siteOrigin)>80))RefreshSpawnSites();
            if(spawning&&spawnTimer<=0){for(int i=0;i<3;i++)TrySpawn();spawnTimer=.5f;}
            for(int i=0;i<vehicles.Count;i++)
            {
                var v=vehicles[i];if(!v.active)continue;
                if(Horizontal(v.position-player.transform.position)>despawnDistance){Retire(v);Recycled++;continue;}
                Step(v,i,dt);
            }
        }
        public bool TrySpawn()
        {
            Vehicle v=vehicles.Find(a=>!a.active);if(v==null)return false;
            if(spawnSites.Count==0)RefreshSpawnSites();if(spawnSites.Count==0)return false;
            for(int attempt=0;attempt<60;attempt++)
            {
                var site=spawnSites[random.Next(spawnSites.Count)];int e=site.edge;var edge=Network.edges[e];
                if(edge.Length<28||edge.roundabout)continue;
                float d=Mathf.Clamp(site.distance+((float)random.NextDouble()*2-1)*spawnMax,5,edge.Length-5);
                var p=edge.At(d,out var heading);float radius=Horizontal(p-player.transform.position);
                if(radius<spawnMin||radius>spawnMax)continue;
                Vector3 toward=p-player.transform.position;toward.y=0;
                if(radius<170&&Vector3.Dot(toward.normalized,player.transform.forward)>.45f)continue;
                bool clear=true;foreach(var other in vehicles)if(other.active&&Horizontal(other.position-p)<16){clear=false;break;}
                if(!clear)continue;
                int destination=-1;
                var destinations=Network.Destinations(e);
                for(int choice=0;choice<(destinationMaxDistance>0?24:12);choice++)
                {
                    int n=choice==0&&edge.allowedNext.Length==0?edge.toNode:destinations[random.Next(destinations.Length)];
                    float distance=Horizontal(Network.nodes[n].point-p);
                    if(distance<250||destinationMaxDistance>0&&distance>destinationMaxDistance)continue;
                    if(Network.FindPath(e,n,v.path)&&v.path.Count>0){destination=n;break;}
                }
                if(destination<0)continue;
                Activate(v,destination,d);Spawned++;RouteChoices++;return true;
            }
            return false;
        }
        void RefreshSpawnSites()
        {
            spawnSites.Clear();siteOrigin=player.transform.position;siteTimer=2;
            for(int e=0;e<Network.edges.Length;e++)
            {
                var edge=Network.edges[e];if(edge.Length<28||edge.roundabout)continue;
                int best=0;float nearest=float.PositiveInfinity;
                for(int i=0;i<edge.lane.Length;i+=6)
                {
                    float d=(new Vector2(edge.lane[i].x-siteOrigin.x,edge.lane[i].z-siteOrigin.z)).sqrMagnitude;
                    if(d<nearest){nearest=d;best=i;}
                }
                if(nearest<(spawnMax+15)*(spawnMax+15))spawnSites.Add(new SpawnSite{edge=e,distance=edge.laneDistances[best]});
            }
        }
        public void Activate(Vehicle v,int destination,float distance)
        {
            v.leg=0;v.distance=distance;v.destination=destination;v.speed=0;v.travelled=0;v.waiting=0;v.inTurn=false;v.clearingCircle=false;v.reservedNode=-1;v.reservedCircle=-1;
            var edge=Network.edges[v.path[0]];
            if(edge.roundabout)
            {
                v.reservedCircle=ExclusiveCircle(edge.fromNode);
                if(v.reservedCircle>=0)circleReservations[v.reservedCircle]=vehicles.IndexOf(v);
            }
            v.active=true;v.body.gameObject.SetActive(true);Place(v);
        }
        int CircleGroup(int node){while(circleGroup[node]!=node)node=circleGroup[node];return node;}
        // Wide circulatory roads use local junction and following gaps. A
        // reservation for the whole circle would hold every distant entrance.
        int ExclusiveCircle(int node)
        {
            int group=CircleGroup(node);
            return wideCircles[group]?-1:group;
        }
        void UpdatePlayerCircle()
        {
            guideCircleGap=float.PositiveInfinity;
            if(playerCircle>=0&&Horizontal(player.transform.position-playerCircleCentre)>playerCircleRadius+22)
            {circleReservations[playerCircle]=-1;playerCircle=-1;}
            if(!player.guidedDrive||playerCircle>=0)return;
            var heading=player.transform.forward;heading.y=0;heading.Normalize();
            foreach(var edge in circleEntries)
            {
                edge.At(edge.Length,out var incoming);if(Vector3.Dot(heading,incoming)<.75f)continue;
                var delta=Network.nodes[edge.toNode].point-player.transform.position;delta.y=0;
                float along=Vector3.Dot(delta,heading),side=Mathf.Abs(Vector3.Dot(delta,Vector3.Cross(Vector3.up,heading)));
                if(along<8||along>55||side>edge.width*.6f+2)continue;
                int group=ExclusiveCircle(edge.toNode);
                if(group<0)continue;
                if(circleReservations[group]>=0){guideCircleGap=Mathf.Min(guideCircleGap,along-21);continue;}
                if(along>25)continue;
                if(PlayerClearance(player.transform.position,heading)<along-8)continue;
                playerCircle=group;circleReservations[group]=vehicles.Count;playerCircleCentre=Vector3.zero;int count=0;
                for(int i=0;i<Network.nodes.Length;i++)if(Network.nodes[i].roundabout&&CircleGroup(i)==group){playerCircleCentre+=Network.nodes[i].point;count++;}
                playerCircleCentre/=Mathf.Max(1,count);playerCircleRadius=0;
                for(int i=0;i<Network.nodes.Length;i++)if(Network.nodes[i].roundabout&&CircleGroup(i)==group)playerCircleRadius=Mathf.Max(playerCircleRadius,Horizontal(Network.nodes[i].point-playerCircleCentre));
                break;
            }
        }
        void ReleaseCircle(Vehicle v){if(v.reservedCircle>=0){circleReservations[v.reservedCircle]=-1;v.reservedCircle=-1;}v.clearingCircle=false;}
        void Retire(Vehicle v)
        {
            if(v.reservedNode>=0){reservations[v.reservedNode]=-1;v.reservedNode=-1;}
            ReleaseCircle(v);
            v.active=false;v.speed=0;if(v.body!=null)v.body.gameObject.SetActive(false);
        }
        void Step(Vehicle v,int id,float dt)
        {
            var edge=Network.edges[v.path[v.leg]];
            if(v.clearingCircle&&!v.inTurn&&v.distance>7)ReleaseCircle(v);
            float target=Mathf.Min(13.89f,edge.speed*v.factor),gap=float.PositiveInfinity;
            float remain=(v.inTurn?v.turnDistances[v.turnDistances.Length-1]:edge.Length)-v.distance;
            if(v.inTurn)target=Mathf.Min(target,6);
            else if(v.leg+1<v.path.Count)
            {
                var next=Network.edges[v.path[v.leg+1]];edge.At(edge.Length,out var from);next.At(0,out var to);
                float angle=Vector3.Angle(from,to);
                if(angle>20)target=Mathf.Min(target,Mathf.Sqrt(2*2.2f*Mathf.Max(0,remain-5)+Mathf.Pow(angle>65?4.5f:7,2)));
            }
            foreach(var other in vehicles)
            {
                if(!other.active||other==v)continue;
                // Exact path distance catches queues around bends. Spatial checks also
                // protect merges and the player's unscripted movements.
                if(!v.inTurn&&!other.inTurn&&v.path[v.leg]==other.path[other.leg]&&other.distance>v.distance)
                    gap=Mathf.Min(gap,other.distance-v.distance-5.1f);
                gap=Mathf.Min(gap,VehicleGap(v,other.position,other.heading));
            }
            gap=Mathf.Min(gap,VehicleGap(v,player.transform.position,player.transform.forward));
            if(!v.inTurn)
            {
                int node=edge.toNode;var junction=Network.nodes[node];
                if(edge.signalApproach>0)
                {
                    var approach=signals.approaches[edge.signalApproach-1];
                    var aspect=signals.ApproachAspect(approach);
                    float toLine=remain-approach.stopBeforeEdgeEnd-2.5f;
                    bool canStop=toLine>v.speed*v.speed/11+.8f||v.speed<.6f;
                    if(toLine>-.5f&&(aspect==JejuSignalAspect.Red||aspect==JejuSignalAspect.Amber&&canStop))
                        gap=Mathf.Min(gap,toLine);
                }
                else if(junction.signal>=0&&junction.signal<signals.junctions.Length)
                {
                    var signal=signals.junctions[junction.signal];
                    var aspect=Mathf.Abs(Vector3.Dot(v.heading,signal.forward))>.65f?signals.MainAspect(signal):signals.CrossAspect(signal);
                    float toLine=remain+edge.trimEnd-16-2.5f;
                    // A vehicle already beyond its stop line clears the intersection.
                    if(aspect!=JejuSignalAspect.Green&&toLine>-.5f)gap=Mathf.Min(gap,toLine);
                }
                if(v.leg+1<v.path.Count&&(junction.degree>=3||(!edge.roundabout&&Network.edges[v.path[v.leg+1]].roundabout)))
                {
                    bool clear=reservations[node]<0||reservations[node]==id;
                    if(v.reservedNode<0&&!edge.roundabout&&Horizontal(player.transform.position-junction.point)<10)clear=false;
                    // Give entering traffic a safe gap on the receiving lane (including roundabouts).
                    var next=Network.edges[v.path[v.leg+1]];
                    int circle=next.roundabout&&!edge.roundabout?ExclusiveCircle(node):-1;
                    if(circle>=0&&circleReservations[circle]>=0&&circleReservations[circle]!=id)clear=false;
                    foreach(var other in vehicles)
                    {
                        if(!other.active||other==v)continue;
                        var approach=Network.edges[other.path[other.leg]];
                        if(!other.inTurn&&other.path[other.leg]==v.path[v.leg+1]&&other.distance<10)clear=false;
                        if(!edge.roundabout&&junction.roundabout&&approach.roundabout&&approach.toNode==node&&approach.Length-other.distance<Mathf.Max(18,other.speed*2.8f))clear=false;
                    }
                    float holdingGap=Network.checkTurnFootprints?Mathf.Max(2.8f,8f-edge.trimEnd):2.8f;
                    if(!clear)gap=Mathf.Min(gap,remain-holdingGap);
                    else if(remain<7&&v.reservedNode<0&&gap>remain-1)
                    {
                        reservations[node]=id;v.reservedNode=node;
                        if(circle>=0){circleReservations[circle]=id;v.reservedCircle=circle;}
                    }
                }
                if(v.leg==v.path.Count-1&&remain<2.5f){Arrivals++;Retire(v);return;}
            }
            if(!float.IsPositiveInfinity(gap))
            {
                target=Mathf.Min(target,Mathf.Sqrt(2*4*Mathf.Max(0,gap-.8f)));
                if(gap<20)target=Mathf.Min(target,Mathf.Max(0,(gap-.8f)/1.25f));
            }
            float previous=v.speed;v.speed=Mathf.MoveTowards(v.speed,target,dt*(target<v.speed?5.5f:1.8f));
            float step=v.speed*dt;if(!float.IsPositiveInfinity(gap))step=Mathf.Min(step,Mathf.Max(0,gap-.65f));
            v.distance+=step;v.travelled+=step;v.waiting=v.speed<.2f?v.waiting+dt:0;
            if(v.distance>= (v.inTurn?v.turnDistances[v.turnDistances.Length-1]:edge.Length)-.02f)
            {
                if(v.inTurn)
                {
                    v.inTurn=false;v.leg++;v.distance=0;CompletedTurns++;
                    if(edge.roundabout&&!Network.edges[v.path[v.leg]].roundabout)v.clearingCircle=true;
                    if(v.reservedNode>=0){reservations[v.reservedNode]=-1;v.reservedNode=-1;}
                }
                else if(v.leg+1<v.path.Count)BeginTurn(v,edge,Network.edges[v.path[v.leg+1]]);
                else{Arrivals++;Retire(v);return;}
            }
            Place(v);
            bool braking=target<previous-.1f||v.speed<.2f;
            lampBlock.Clear();lampBlock.SetColor("_EmissionColor",braking?new Color(3,.015f,.005f):new Color(.35f,.005f,.002f));
            foreach(var r in v.brakeLamps)r.SetPropertyBlock(lampBlock);
            float turnSign=0;
            if(v.leg+1<v.path.Count&&(v.inTurn||remain<45))
            {
                edge.At(edge.Length,out var a);Network.edges[v.path[v.leg+1]].At(0,out var b);
                if(Vector3.Angle(a,b)>25)turnSign=Vector3.SignedAngle(a,b,Vector3.up);
            }
            foreach(var lamp in v.indicators)lamp.gameObject.SetActive(turnSign!=0&&Mathf.Repeat(clock,.9f)<.45f&&(turnSign>0==lamp.name.EndsWith("Right")));
        }
        static float ObstacleGap(Vector3 p,Vector3 heading,Vector3 obstacle,Vector3 obstacleHeading)
        {
            var delta=obstacle-p;delta.y=0;heading.y=0;heading.Normalize();
            var right=Vector3.Cross(Vector3.up,heading);obstacleHeading.y=0;obstacleHeading.Normalize();
            float parallel=Mathf.Abs(Vector3.Dot(heading,obstacleHeading)),cross=Mathf.Abs(Vector3.Dot(right,obstacleHeading));
            float along=Vector3.Dot(delta,heading),side=Mathf.Abs(Vector3.Dot(delta,right));
            float halfWidth=.95f+.95f*parallel+2.3f*cross+.1f,front=2.4f+2.3f*parallel+.95f*cross+.4f;
            return along>0&&side<halfWidth?along-front:float.PositiveInfinity;
        }
        void BeginTurn(Vehicle v,JejuTrafficEdge edge,JejuTrafficEdge next)
        {
            int key=v.path[v.leg]*Network.edges.Length+v.path[v.leg+1];
            if(!turnPaths.TryGetValue(key,out var path))
            {
                path=Connection(edge,next,Network.checkTurnFootprints);
                turnPaths.Add(key,path);
            }
            v.turn=path;v.turnDistances=JejuTrafficNetwork.Distances(v.turn);v.distance=0;v.inTurn=true;
        }
        public static Vector3[] Connection(JejuTrafficEdge edge,JejuTrafficEdge next,bool compact=false)
        {
            Vector3 a=edge.At(edge.Length,out var f),b=next.At(0,out var g);
            float distance=Vector3.Distance(a,b);Vector3[] first=null;
            // Different skewed or narrow arms need different tangent lengths.
            // Select a curve whose centre and tyre tracks fit the authored pavement.
            foreach(float factor in compact?new[]{.35f,.48f,.65f,.82f,.2f,1f}:new[]{.48f,.65f,.82f,.35f,.2f,1f})
            {
                float handle=Mathf.Min(22,distance*factor);
                var path=Bezier(a,a+f*handle,b-g*handle,b);
                if(first==null)first=path;
                if(FitsRoad(path))return path;
            }
            Vector3 centre=edge.points[edge.points.Length-1],tangent=(b-a).normalized;
            float firstHandle=Vector3.Distance(a,centre)*.48f,lastHandle=Vector3.Distance(centre,b)*.48f;
            var left=Bezier(a,a+f*firstHandle,centre-tangent*firstHandle,centre);
            var right=Bezier(centre,centre+tangent*lastHandle,b-g*lastHandle,b);
            var joined=new Vector3[left.Length+right.Length-1];Array.Copy(left,joined,left.Length);Array.Copy(right,1,joined,left.Length,right.Length-1);
            return FitsRoad(joined)?joined:first;
        }
        static Vector3[] Bezier(Vector3 a,Vector3 c,Vector3 d,Vector3 b)
        {
            var p=new Vector3[Mathf.Max(8,Mathf.CeilToInt(Vector3.Distance(a,b)))];
            for(int i=0;i<p.Length;i++)
            {
                float t=i/(float)(p.Length-1),s=1-t;
                p[i]=s*s*s*a+3*s*s*t*c+3*s*t*t*d+t*t*t*b;
                p[i].y=RoadHeight(p[i]);
            }
            return p;
        }
        public static bool FitsRoad(Vector3[] p)
        {
            for(int i=0;i<p.Length;i++)
            {
                Vector3 f=(p[Mathf.Min(i+1,p.Length-1)]-p[Mathf.Max(0,i-1)]).normalized;
                var r=Vector3.Cross(Vector3.up,f).normalized*.8f;
                if(!TryRoadHeight(p[i],out _)||!TryRoadHeight(p[i]+r,out _)||!TryRoadHeight(p[i]-r,out _))return false;
            }
            return true;
        }
        void Place(Vehicle v)
        {
            var e=Network.edges[v.path[v.leg]];
            Vector3 p=v.inTurn?JejuTrafficNetwork.Sample(v.turn,v.turnDistances,v.distance,out v.heading):e.At(v.distance,out v.heading);
            p.y=RoadHeight(p);v.position=p;
            v.body.SetPositionAndRotation(p,Quaternion.LookRotation(v.heading));
        }
        static readonly RaycastHit[] roadHits=new RaycastHit[16];
        static float RoadHeight(Vector3 p)
        {
            return TryRoadHeight(p,out float y)?y:p.y;
        }
        static bool TryRoadHeight(Vector3 p,out float y)
        {
            int n=Physics.RaycastNonAlloc(p+Vector3.up*4,Vector3.down,roadHits,8,~(1<<2),QueryTriggerInteraction.Ignore);
            y=p.y;float best=float.PositiveInfinity;
            for(int i=0;i<n;i++)if(roadHits[i].collider.name=="Asphalt"&&roadHits[i].distance<best){best=roadHits[i].distance;y=roadHits[i].point.y;}
            return !float.IsPositiveInfinity(best);
        }
        public float PlayerClearance(Vector3 position,Vector3 heading)
        {
            float gap=float.PositiveInfinity;foreach(var v in vehicles)if(v.active)gap=Mathf.Min(gap,ObstacleGap(position,heading,v.position,v.heading));return gap;
        }
        static float Horizontal(Vector3 v)=>new Vector2(v.x,v.z).magnitude;
    }
}
