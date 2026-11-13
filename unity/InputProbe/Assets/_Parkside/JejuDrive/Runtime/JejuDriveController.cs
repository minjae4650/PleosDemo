using UnityEngine;

namespace Parkside.Jeju
{
    /// <summary>Scene-local road preview: bicycle steering, road grade, braking and recovery.</summary>
    public sealed partial class JejuDriveController : MonoBehaviour
    {
        public JejuDriveRoute route;
        public Transform visual;
        public Transform[] wheels;
        public Camera view;
        public Transform[] sceneryChunks;
        public JejuTrafficSystem traffic;
        public JejuAmbientTraffic ambient;
        public float topSpeedKph = 70;
        public bool guidedDrive;
        public float SpeedKph => Mathf.Abs(speed) * 3.6f;
        public float Progress { get; private set; }
        public int CurrentSegment { get; private set; }
        public int cameraMode;
        public bool showHud = true;
        public string CurrentRoadName { get; private set; }
        float speed, steering, wheelAngle, cullingTimer;
        bool paused;
        void Start() { JumpToStation(0); UpdateScenery(); UpdateCamera(true); }

        void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.C)) { cameraMode = (cameraMode + 1) % 3; UpdateCamera(true); }
            if (UnityEngine.Input.GetKeyDown(KeyCode.G)) guidedDrive = !guidedDrive;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab)) showHud = !showHud;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) paused = !paused;
            if (UnityEngine.Input.GetKeyDown(KeyCode.R)) Recover();
            for (int i = 0; i < Mathf.Min(9, route.stations.Length); i++)
                if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1 + i)) JumpToStation(i);
            if (route.stations.Length > 9 && UnityEngine.Input.GetKeyDown(KeyCode.Alpha0)) JumpToStation(9);
            float throttle = (UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.UpArrow) ? 1 : 0)
                - (UnityEngine.Input.GetKey(KeyCode.S) || UnityEngine.Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
            float turn = (UnityEngine.Input.GetKey(KeyCode.D) || UnityEngine.Input.GetKey(KeyCode.RightArrow) ? 1 : 0)
                - (UnityEngine.Input.GetKey(KeyCode.A) || UnityEngine.Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            Simulate(throttle, turn, UnityEngine.Input.GetKey(KeyCode.Space), Time.deltaTime);
            cullingTimer -= Time.deltaTime;
            if (cullingTimer <= 0) { UpdateScenery(); cullingTimer = .5f; }
        }

        public void Simulate(float throttle, float turn, bool braking, float dt)
        {
            if (paused || route == null || route.points.Length < 2) return;
            dt = Mathf.Clamp(dt, 0, .05f);
            if(traffic!=null)traffic.Advance(dt);
            if(ambient!=null)ambient.Simulate(dt);
            Vector3 before=transform.position;
            if (guidedDrive) SimulateGuided(dt);
            else SimulateManual(throttle,turn,braking,dt);
            if(traffic!=null)traffic.ObserveMovement(before,transform.position,transform.forward);
            wheelAngle += speed * dt / .34f * Mathf.Rad2Deg;
            if (wheels != null) foreach (var wheel in wheels)
                if (wheel != null) wheel.localRotation = Quaternion.Euler(wheelAngle, wheel.name.Contains("Front") ? steering * 25 : 0, 0);
        }

        void SimulateGuided(float dt)
        {
            float targetSpeed=Progress > route.Length - 40 ? 0 : 11.1f;
            route.AtDistance(Progress+18,out var bendForward,out _);
            float bend=Vector3.Angle(transform.forward,bendForward)*Mathf.Deg2Rad;
            if(bend>.15f)targetSpeed=Mathf.Min(targetSpeed,Mathf.Sqrt(2.5f*18/bend));
            if(traffic!=null)
            {
                float stop=traffic.StopDistance(transform.position,transform.forward,out JejuSignalAspect aspect);
                if(aspect!=JejuSignalAspect.Green)targetSpeed=Mathf.Min(targetSpeed,Mathf.Sqrt(2*3.5f*Mathf.Max(0,stop-2.5f)));
            }
            float carGap=ambient!=null?ambient.GuidedClearance(route,Progress):float.PositiveInfinity;
            if(ambient!=null)targetSpeed=Mathf.Min(targetSpeed,Mathf.Sqrt(2*5*Mathf.Max(0,carGap-1)));
            if(ambient!=null)targetSpeed=Mathf.Min(targetSpeed,Mathf.Sqrt(2*3.5f*Mathf.Max(0,ambient.GuidedCircleStop-1)));
            speed = Mathf.MoveTowards(speed,targetSpeed,dt*(targetSpeed<speed?5:2.4f));
            if(ambient!=null)speed=Mathf.Min(speed,Mathf.Max(0,carGap-.7f)/Mathf.Max(.001f,dt));
            Progress = Mathf.Clamp(Progress + speed * dt, 0, route.Length - 6);
            Vector3 p = route.AtDistance(Progress, out Vector3 forward, out float width);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            p += right * JejuDriveRoute.LaneOffset(width);
            p.y = RoadHeight(p);
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(forward));
            int found = System.Array.BinarySearch(route.distances, Progress);
            CurrentSegment = Mathf.Clamp(found < 0 ? ~found - 1 : found, 0, route.points.Length - 2);
        }

        void SimulateManual(float throttle, float turn, bool braking, float dt)
        {
            float target = throttle > 0 ? topSpeedKph / 3.6f : throttle < 0 ? -4.2f : 0;
            speed = Mathf.MoveTowards(speed, braking ? 0 : target, dt * (braking ? 11 : throttle == 0 ? 1.6f : 3.2f));
            if(ambient!=null)
            {
                float gap=ambient.PlayerClearance(transform.position,transform.forward*Mathf.Sign(speed));
                float safe=Mathf.Sqrt(2*8*Mathf.Max(0,gap-.7f));
                speed=Mathf.Sign(speed)*Mathf.Min(Mathf.Abs(speed),safe,Mathf.Max(0,gap-.5f)/Mathf.Max(.001f,dt));
            }
            steering = Mathf.MoveTowards(steering, turn, dt * 2.5f);
            float angle = Mathf.Lerp(32, 11, Mathf.Clamp01(Mathf.Abs(speed) / 19)) * steering;
            float yaw = speed / 2.7f * Mathf.Tan(angle * Mathf.Deg2Rad) * Mathf.Rad2Deg * dt;
            Vector3 heading = Quaternion.Euler(0, yaw, 0) * transform.forward; heading.y = 0; heading.Normalize();
            Vector3 candidate = transform.position + heading * speed * dt;
            var road=route.ProjectDrivable(candidate);
            Vector3 centre=road.point;float fraction=road.fraction;int segment=road.mainIndex;
            Vector3 right=road.right;
            CurrentRoadName=road.name;
            float lateral = Vector3.Dot(candidate - centre, right);
            float limit = road.width * .5f - .65f;
            if (Mathf.Abs(lateral) > limit)
            {
                candidate = centre + right * Mathf.Clamp(lateral, -limit, limit);
                speed = Mathf.MoveTowards(speed, 0, dt * 14);
            }
            // Keep the chassis on the imported road grade instead of moving on a flat XZ plane.
            candidate.y = centre.y;
            candidate.y = RoadHeight(candidate);
            Vector3 slope=road.forward;
            heading.y = slope.y * Mathf.Sign(Vector3.Dot(heading, slope));
            transform.SetPositionAndRotation(candidate, Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(heading), dt * 9));
            Progress = Mathf.Lerp(route.distances[segment], route.distances[segment + 1], fraction);
            CurrentSegment=route.Nearest(transform.position,out _,out _);
        }

        static float RoadHeight(Vector3 p)
        {
            if(Physics.Raycast(p+Vector3.up*3,Vector3.down,out RaycastHit hit,6,~(1<<2),QueryTriggerInteraction.Ignore)&&hit.collider.name=="Asphalt")return hit.point.y;
            return p.y;
        }

        public void JumpToStation(int index)
        {
            var station=route.stations[Mathf.Clamp(index,0,route.stations.Length-1)];
            int i = Mathf.Clamp(station.index, 0, route.points.Length - 2);
            speed = 0; steering = 0; Progress = route.distances[i]; CurrentSegment = i;
            Vector3 forward = (route.points[i + 1] - route.points[i]).normalized;
            transform.SetPositionAndRotation(route.points[i] + route.Right(i) * JejuDriveRoute.LaneOffset(route.widths[i]), Quaternion.LookRotation(forward));
            if (station.branchSpawn)
            {
                guidedDrive=false;
                transform.SetPositionAndRotation(station.position,Quaternion.LookRotation(station.forward));
                CurrentRoadName=station.name;
            }
            else CurrentRoadName=null;
            if(Application.isPlaying&&ambient!=null)ambient.ResetPopulation();
            UpdateScenery(); UpdateCamera(true);
        }

        public void Recover()
        {
            var road=route.ProjectDrivable(transform.position);int i=road.mainIndex;
            speed = 0; steering = 0; Progress = route.distances[i];
            transform.SetPositionAndRotation(road.point+road.right*Mathf.Min(1.75f,road.width*.23f),Quaternion.LookRotation(road.forward));
            UpdateCamera(true);
        }

    }
}
