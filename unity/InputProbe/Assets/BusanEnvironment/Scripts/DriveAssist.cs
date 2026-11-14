using System;
using Parkside.Core;
using Parkside.Vehicle;
using UnityEngine;

namespace BusanEnvironment
{
    /// <summary>
    /// One course of a map with several (plan.md 2.3): a line of points in driving order, the player's lane (metres to the right
    /// of the line) and the auto-drive speed (m/s) at each point. A closed route joins its last point to its first and its
    /// distance wraps. Routes that share road carry the same points there.
    /// </summary>
    [Serializable]
    public sealed class CourseRoute
    {
        public string name;
        public bool closed = true;
        public Vector3[] path;
        public float[] laneOffsets;
        public float[] autoSpeeds;

        [NonSerialized] internal float[] cumulative;   // distance (3D, as the single course measures) at each point
        [NonSerialized] internal float total;          // the whole line; closed: including the step from the last point to the first
        [NonSerialized] internal Vector3[] right;      // flat right of travel at each point (central differences), as the lane is paved
        [NonSerialized] internal Vector3[] heading;    // direction of travel at each point (central differences, with the grade)

        internal bool Valid => path != null && path.Length >= 2;

        internal void Prepare()
        {
            int n = path.Length;
            cumulative = new float[n];
            for (int i = 1; i < n; i++) cumulative[i] = cumulative[i - 1] + Vector3.Distance(path[i - 1], path[i]);
            total = cumulative[n - 1] + (closed ? Vector3.Distance(path[n - 1], path[0]) : 0f);
            right = new Vector3[n];
            heading = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var prev = closed ? path[(i - 1 + n) % n] : path[Mathf.Max(i - 1, 0)];
                var next = closed ? path[(i + 1) % n] : path[Mathf.Min(i + 1, n - 1)];
                var d = next - prev;
                heading[i] = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
                var flat = new Vector3(d.x, 0f, d.z);
                flat = flat.sqrMagnitude > 1e-8f ? flat.normalized : Vector3.forward;
                right[i] = new Vector3(flat.z, 0f, -flat.x);
            }
        }

        /// <summary>s on the line: wrapped on a closed route, clamped on an open one.</summary>
        internal float Wrap(float s) => closed ? Mathf.Repeat(s, total) : Mathf.Clamp(s, 0f, total);

        int Count => cumulative.Length;
        int Next(int i) => closed ? (i + 1) % Count : Mathf.Min(i + 1, Count - 1);
        float SegmentLength(int i) => i < Count - 1 ? cumulative[i + 1] - cumulative[i] : total - cumulative[Count - 1];
        internal float LaneAt(int i) => laneOffsets != null && laneOffsets.Length == path.Length ? laneOffsets[i] : 1.9f;

        // the segment s lies on and the fraction along it
        internal void Locate(float s, out int lo, out int hi, out float t)
        {
            s = Wrap(s);
            int n = Count;
            if (closed && s >= cumulative[n - 1])
            {
                lo = n - 1; hi = 0;
                float last = total - cumulative[n - 1];
                t = last > 0.0001f ? (s - cumulative[n - 1]) / last : 0f;
                return;
            }
            lo = 0; hi = n - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) / 2;
                if (cumulative[mid] <= s) lo = mid; else hi = mid;
            }
            float segment = cumulative[hi] - cumulative[lo];
            t = segment > 0.0001f ? (s - cumulative[lo]) / segment : 0f;
        }

        internal Vector3 PointAt(float s)
        {
            Locate(s, out int lo, out int hi, out float t);
            return Vector3.Lerp(path[lo], path[hi], t);
        }

        /// <summary>The lane line at s (each point moved its lane to its right, straight between), `lateral` metres further right,
        /// and the direction of travel there (eased between the points, so a bend turns the car smoothly).</summary>
        internal void LanePose(float s, float lateral, out Vector3 point, out Vector3 forward)
        {
            Locate(s, out int lo, out int hi, out float t);
            point = Vector3.Lerp(path[lo] + right[lo] * (LaneAt(lo) + lateral), path[hi] + right[hi] * (LaneAt(hi) + lateral), t);
            forward = Vector3.Lerp(heading[lo], heading[hi], t);
            forward = forward.sqrMagnitude > 1e-8f ? forward.normalized : heading[lo];
        }

        internal Vector3 RightAt(float s)
        {
            Locate(s, out int lo, out int hi, out float t);
            var r = Vector3.Lerp(right[lo], right[hi], t);
            return r.sqrMagnitude > 1e-8f ? r.normalized : right[lo];
        }

        /// <summary>Auto-drive speed at s (the value of the point before it, as the single course does), else `fallback`.</summary>
        internal float SpeedAt(float s, float fallback)
        {
            if (autoSpeeds == null || autoSpeeds.Length != Count) return fallback;
            Locate(s, out int lo, out _, out _);
            return autoSpeeds[lo];
        }

        /// <summary>The nearest point of the lane line (or of the centre line), with height weighted as the single course's search
        /// does (dx² + dz² + 4 dy², so standing under a viaduct picks the road, not the deck): its s and that weighted distance.</summary>
        internal void Nearest(Vector3 position, bool laneLine, out float s, out float distance)
        {
            var q = new Vector3(position.x, position.y * 2f, position.z);
            float bestScore = float.MaxValue; s = 0f;
            int segments = closed ? Count : Count - 1;
            var a = Point(0, laneLine);
            for (int i = 0; i < segments; i++)
            {
                int j = Next(i);
                var b = Point(j, laneLine);
                var ab = b - a;
                float l2 = ab.sqrMagnitude;
                float u = l2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(q - a, ab) / l2) : 0f;
                float score = (a + ab * u - q).sqrMagnitude;
                if (score < bestScore) { bestScore = score; s = cumulative[i] + SegmentLength(i) * u; }
                a = b;
            }
            if (closed && s >= total) s -= total;
            distance = Mathf.Sqrt(bestScore);
        }

        // point i of the lane line or the centre line, with its height doubled (the search's metric)
        Vector3 Point(int i, bool laneLine)
        {
            var p = laneLine ? path[i] + right[i] * LaneAt(i) : path[i];
            return new Vector3(p.x, p.y * 2f, p.z);
        }
    }

    /// <summary>
    /// Makes the drive demo usable without Unity knowledge: starts driving on Play,
    /// offers an auto-drive along the course, jump points, R to return to the course
    /// and a short on-screen guide. Runs before VehicleCamera so the camera sees the
    /// auto-driven pose.
    /// With `routes` (the landmark map) it follows several closed routes with a fork: the route under the car is the active one,
    /// auto-drive takes the preferred route (F) at the fork and goes round and round, R returns to the nearest lane of any route.
    /// Without `routes` (BusanDrive) the single open course in `path` is followed exactly as before.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class DriveAssist : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private VehicleController vehicle;
        [SerializeField] private Parkside.Cameras.VehicleCamera vehicleCamera;
        [SerializeField] private Vector3[] path;
        [SerializeField] private float[] laneOffsets;
        [SerializeField] private Bounds driveBounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 1e5f));
        [SerializeField] private float autoSpeed = 16f;
        [SerializeField] private float[] autoSpeeds;
        [SerializeField] private float rideHeight = 0f;
        [SerializeField] private string[] jumpNames;
        [SerializeField] private float[] jumpDistances;
        [SerializeField] private Behaviour[] postEffects;
        [SerializeField] private RouteTraffic traffic;
        [SerializeField] private float shadowDistance = 300f;
        // Several courses (plan.md 2.3). Empty: the single course above, unchanged (BusanDrive).
        [SerializeField] private CourseRoute[] routes;
        [SerializeField] private int preferredRoute;     // the route auto-drive takes at a fork; F changes it
        [SerializeField] private int[] jumpRoutes;       // with routes: the route of each jump point

        private float[] cumulative;
        private float total;
        private float autoS;
        private float currentAutoSpeed;
        private bool autoDriving;
        private bool started;
        private bool wasRunning, resumeWhenReady;
        private bool viewSet;
        private bool showGuide;
        private GUIStyle guideStyle;
        private ShadowQuality savedShadows;
        private float savedShadowDistance;
        private bool shadowsApplied;
        private int savedAntiAliasing, savedCascades;
        private ShadowResolution savedShadowResolution;
        private ShadowProjection savedShadowProjection;

        // ---- several routes
        private const float LeaveDistance = 2.5f;   // the active route is left when its lane line is further than this from the car
        private const float JoinDistance = 1.0f;    // ... and another route's lane line is within this
        private const float JoinMargin = 3.0f;      // ... or nearer by this much (another lane of a branch that is clearly the other route)
        private const float PartingAhead = 50f;     // ... where, this far on along the active route,
        private const float PartingGap = 3.0f;      // ... the other route's lane line is further than this (the routes part, not merge)
        private const float RouteCheckEvery = 0.2f;
        private const float BlendLength = 60f;      // auto-drive moves over to a new route's lane over this distance
        private const float SharedTolerance = 0.3f; // centre lines this close are the same road
        private bool multi;
        private int active, autoRoute;
        private float nextRouteCheck;
        private float blendFrom, blendDone = BlendLength;

        public bool AutoDriving => autoDriving;

        private void Awake()
        {
            if (routes != null && routes.Length > 0)
            {
                bool ok = true;
                foreach (var r in routes) ok &= r != null && r.Valid;
                if (ok)
                {
                    foreach (var r in routes) r.Prepare();
                    multi = true;
                    preferredRoute = Mathf.Clamp(preferredRoute, 0, routes.Length - 1);
                    active = autoRoute = preferredRoute;
                }
                else Debug.LogWarning("DriveAssist: a route has fewer than two points; the single course is used instead");
            }
            if (!multi && path != null && path.Length >= 2)
            {
                cumulative = new float[path.Length];
                for (int i = 1; i < path.Length; i++)
                    cumulative[i] = cumulative[i - 1] + Vector3.Distance(path[i - 1], path[i]);
                total = cumulative[cumulative.Length - 1];
            }
#if UNITY_ANDROID || UNITY_IOS
            // Mobile: no post-processing, and the quality level's shadow setting stays as authored.
            if (postEffects != null)
                foreach (var effect in postEffects)
                    if (effect != null) effect.enabled = false;
#else
            // Quality level ships with shadows off (parking scenes); the global value is restored
            // in OnDestroy so it cannot leak into other scenes.
            savedShadows = QualitySettings.shadows;
            savedShadowDistance = QualitySettings.shadowDistance;
            savedAntiAliasing = QualitySettings.antiAliasing;
            savedCascades = QualitySettings.shadowCascades;
            savedShadowResolution = QualitySettings.shadowResolution;
            savedShadowProjection = QualitySettings.shadowProjection;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = shadowDistance;
            // desktop only: 4x MSAA and four cascades (restored in OnDestroy, so the parking scenes keep their quality level)
            QualitySettings.antiAliasing = 4;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            shadowsApplied = true;
#endif
        }

        private void OnDestroy()
        {
            if (!shadowsApplied) return;
            QualitySettings.shadows = savedShadows;
            QualitySettings.shadowDistance = savedShadowDistance;
            QualitySettings.antiAliasing = savedAntiAliasing;
            QualitySettings.shadowCascades = savedCascades;
            QualitySettings.shadowResolution = savedShadowResolution;
            QualitySettings.shadowProjection = savedShadowProjection;
        }

        private void Update()
        {
            if (gameManager == null || vehicle == null) return;
            if (!started && gameManager.InputReady)
            {
                gameManager.StartPractice();
                started = gameManager.IsRunning;
            }
            // GameManager pauses when the window loses focus or the input drops out. Neither is the driver's choice, so driving
            // resumes once both are back; a pause from the HUD button happens with focus and valid input and stays paused.
            if (wasRunning && !gameManager.IsRunning && !gameManager.InputReady) resumeWhenReady = true;
            if (gameManager.IsRunning) resumeWhenReady = false;
            else if (resumeWhenReady && gameManager.InputReady && Application.isFocused) gameManager.StartPractice();
            wasRunning = gameManager.IsRunning;
            if (!viewSet && vehicleCamera != null) { vehicleCamera.SetView(2); viewSet = true; } // driver view is the default
            UpdateSpeedCap();
            if (!autoDriving && gameManager.IsRunning) GuardEdge(Time.deltaTime);
            if (UnityEngine.Input.GetKeyDown(KeyCode.T)) { if (autoDriving) EndAutoDrive(); else BeginAutoDrive(); }
            if (UnityEngine.Input.GetKeyDown(KeyCode.V) && vehicleCamera != null) vehicleCamera.CycleView();
            if (UnityEngine.Input.GetKeyDown(KeyCode.G)) showGuide = !showGuide;
            if (UnityEngine.Input.GetKeyDown(KeyCode.R)) ReturnToCourse();
            if (multi && UnityEngine.Input.GetKeyDown(KeyCode.F)) SetPreferredRoute((preferredRoute + 1) % routes.Length);
            if (jumpDistances != null)
                for (int i = 0; i < jumpDistances.Length && i < 9; i++)
                    if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1 + i)) { if (multi) JumpTo(JumpRoute(i), jumpDistances[i]); else JumpTo(jumpDistances[i]); }
            if (multi && Time.time >= nextRouteCheck) { nextRouteCheck = Time.time + RouteCheckEvery; CheckRoute(); }
            // Runs before GameManager.Update, so leaving the area returns to the course instead of
            // GameManager pausing and resetting the practice to the spawn point.
            if (!autoDriving && !driveBounds.Contains(vehicle.transform.position)) ReturnToCourse();
        }

        // The window can lose focus between two Updates, and an unfocused player may not run Update at all: wasRunning is the state before it.
        private void OnApplicationFocus(bool focused) { if (!focused && wasRunning) resumeWhenReady = true; }
        private void OnApplicationPause(bool paused) { if (paused && wasRunning) resumeWhenReady = true; }

        private void LateUpdate()
        {
            if (multi) { if (autoDriving) AutoDriveRoutes(); return; }
            if (!autoDriving || cumulative == null) return;
            float dt = Time.deltaTime;
            float bend = Vector3.Angle(TangentAt(autoS), TangentAt(autoS + 40f));
            float limit = autoSpeed;
            if (autoSpeeds != null && autoSpeeds.Length == cumulative.Length)
            {
                int lo = 0, hi = cumulative.Length - 1;
                while (lo + 1 < hi) { int mid = (lo + hi) / 2; if (cumulative[mid] <= autoS) lo = mid; else hi = mid; }
                limit = autoSpeeds[lo];
            }
            float wanted = Mathf.Lerp(limit, 8f, Mathf.Clamp01(bend / 35f));
            // auto-drive skips VehicleController.Simulate, so the car-ahead limit is applied to the wanted speed here
            wanted = Mathf.Min(wanted, AheadLimit(true));
            currentAutoSpeed = Mathf.MoveTowards(currentAutoSpeed, wanted, dt * (wanted < currentAutoSpeed ? 9f : 4f));
            autoS += currentAutoSpeed * dt;
            if (autoS >= total - 1f) { EndAutoDrive(); return; }
            Place(autoS);
        }

        // ---- the road's edge. The car is held on the road only by where the driving surface ends: VehicleController stops it
        // dead when neither the way ahead nor a heading up to 50 degrees aside has surface under the leading axle, and a stopped
        // car cannot steer, so the player stood stuck against every lane that ends (the span's right lanes at the down ramp, a
        // kerb met square on). Here the car is turned along the edge before that happens: when the surface ends a little ahead,
        // toward the side that still has surface, faster the wider it has to turn; and for a second and a half after it was
        // stopped there, so it comes free on its own. A dead end (no surface to either side) is left alone.
        private int edgeMask = -1;
        private float lastMoving = -10f;
        private static readonly RaycastHit[] EdgeHits = new RaycastHit[8];

        private bool SurfaceAt(Vector3 at, float y)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(at.x, y + 2f, at.z), Vector3.down, EdgeHits, 4.2f, edgeMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (Mathf.Abs(EdgeHits[i].point.y - y) <= 1.2f) return true;
            return false;
        }

        private void GuardEdge(float dt)
        {
            if (edgeMask == -1)
            {
                var field = typeof(VehicleController).GetField("groundMask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                edgeMask = field != null ? ((LayerMask)field.GetValue(vehicle)).value : ~0;
                if (edgeMask == -1) edgeMask = ~(1 << 2);
            }
            var t = vehicle.transform;
            float speed = Mathf.Abs(vehicle.SpeedMps);
            if (speed > 0.5f) lastMoving = Time.time;
            else if (Time.time - lastMoving > 1.5f) return;
            Vector3 travel = t.forward; travel.y = 0f;
            if (travel.sqrMagnitude < 1e-6f) return;
            travel = travel.normalized * (vehicle.IsReversing ? -1f : 1f);
            Vector3 pos = t.position;
            if (!SurfaceAt(pos, pos.y)) return;                       // not on a surface at all (a jump point being placed)
            // right at the edge (where the leading axle will be after this frame): the heading is put along the edge at once,
            // as a guard rail would, however hard the driver steers into it (at speed the steering turns the car faster than
            // any gentle correction)
            float near = 1.35f + 0.3f + speed * Mathf.Min(dt, 0.1f) * 1.5f;
            if (!SurfaceAt(pos + travel * near, pos.y))
            {
                for (float angle = 4f; angle <= 92f; angle += 4f)
                {
                    bool right = SurfaceAt(pos + Quaternion.Euler(0f, angle, 0f) * travel * near, pos.y);
                    bool left = SurfaceAt(pos + Quaternion.Euler(0f, -angle, 0f) * travel * near, pos.y);
                    if (!right && !left) continue;
                    if (right && left) right = lastEdgeSide >= 0;      // open both ways: the side it was turned to last
                    float turn = Mathf.Min(angle + 1f, 720f * dt);
                    t.Rotate(0f, right ? turn : -turn, 0f, Space.World);
                    lastEdgeSide = right ? 1 : -1;
                    edgeTurns++;
                    return;
                }
                return;                                              // a dead end
            }
            // further ahead: a gentle turn, more the wider it has to be
            float reach = 1.35f + (speed > 0.5f ? 0.8f + speed * 0.3f : 0.4f);
            if (SurfaceAt(pos + travel * reach, pos.y)) return;
            for (int k = 1; k <= 6; k++)
            {
                float angle = 15f * k;
                bool right = SurfaceAt(pos + Quaternion.Euler(0f, angle, 0f) * travel * reach, pos.y);
                bool left = SurfaceAt(pos + Quaternion.Euler(0f, -angle, 0f) * travel * reach, pos.y);
                if (right == left) { if (right) return; continue; }  // open both ways: nothing to choose
                float rate = Mathf.Lerp(30f, 100f, k / 6f) * Mathf.Clamp(0.6f + speed * 0.08f, 0.6f, 2.2f);
                t.Rotate(0f, (right ? rate : -rate) * dt, 0f, Space.World);
                lastEdgeSide = right ? 1 : -1;
                edgeTurns++;
                return;
            }
        }

        private int lastEdgeSide;
        /// <summary>One step of the edge guard, for a test that drives the controller itself (the game calls it from Update).</summary>
        public void EdgeStep(float dt) { if (!autoDriving) GuardEdge(dt); }

        private int edgeTurns;
        /// <summary>Frames in which the car was turned along the road's edge (test helper).</summary>
        public int EdgeTurns => edgeTurns;

        // Manual driving: never run into the car ahead (same heading, within 2.5 m height, same lane, moving forward).
        private const float CarLength = 4.6f;
        private float blockedTime, bypassUntil;

        // Speed limit from the car ahead (same heading, within 2.5 m height, same lane). Bumper gap under 4 m means stop.
        // Never a deadlock: steering out of the lane releases it, and after 4 s of being held at a standstill
        // the limit is ignored for 3 s so the player can pass a car that is not going to move.
        private float AheadLimit(bool automatic)
        {
            float limit = float.PositiveInfinity;
            bool queued = false;
            if (traffic != null && Time.time >= bypassUntil
                && traffic.TryGetAhead(vehicle.transform.position, vehicle.transform.forward, 45f, out var gap, out var speed, out queued))
            {
                float bumper = gap - CarLength;
                limit = bumper < 4f ? 0f : Mathf.Max(2f, bumper < 12f ? speed * 0.6f : speed + (bumper - 12f) * 0.4f);
            }
            float own = automatic ? currentAutoSpeed : vehicle.SpeedMps;
            // a car waiting at a red light moves on green: do not drive through it (the bypass is for a car that will not move);
            // only after longer than any signal cycle (70 s) is a queue treated as stuck
            blockedTime = limit <= 0f && own < 0.5f ? blockedTime + Time.deltaTime : 0f;
            if (blockedTime > (queued ? 70f : 4f)) { bypassUntil = Time.time + 3f; blockedTime = 0f; }
            return limit;
        }

        // Manual driving: never run into the car ahead.
        private void UpdateSpeedCap()
        {
            if (autoDriving || traffic == null || vehicle.IsReversing) { vehicle.SpeedCap = float.PositiveInfinity; return; }
            vehicle.SpeedCap = AheadLimit(false);
        }

        /// <summary>Arc length of the course point nearest to a position (test and tooling helper). With routes: on the active route.</summary>
        public float DistanceNearest(Vector3 position)
        {
            if (multi) return RouteDistanceNearest(active, position);
            return cumulative == null ? 0f : NearestDistance(position);
        }

        /// <summary>Course length in metres (test helper). With routes: of the active route.</summary>
        public float CourseLength => multi ? routes[active].total : cumulative == null ? 0f : total;

        /// <summary>Point in the player's lane at arc length s (test helper for the steering bot). With routes: on the active route.</summary>
        public Vector3 LanePointAt(float s)
        {
            if (multi) return RouteLanePointAt(active, s);
            if (cumulative == null) return Vector3.zero;
            Sample(Mathf.Clamp(s, 0f, total - 1f), out var position, out var tangent, out var lane);
            var right = Vector3.Cross(Vector3.up, new Vector3(tangent.x, 0f, tangent.z)).normalized;
            return position + right * lane;
        }

        // ---- several routes: what tools and the demo capture read

        /// <summary>Number of routes (1 for the single course, 0 without one).</summary>
        public int RouteCount => multi ? routes.Length : cumulative != null ? 1 : 0;
        /// <summary>The route under the car (0 for the single course).</summary>
        public int ActiveRoute => multi ? active : 0;
        /// <summary>The route auto-drive takes at a fork.</summary>
        public int PreferredRoute => multi ? preferredRoute : 0;
        /// <summary>The route auto-drive is on, and its distance along it.</summary>
        public int AutoRoute => multi ? autoRoute : 0;
        public float AutoDistance => autoS;
        public bool ShowGuide { get => showGuide; set => showGuide = value; }

        public string RouteName(int index) => multi && index >= 0 && index < routes.Length ? routes[index].name : "";
        public bool RouteClosed(int index) => multi && index >= 0 && index < routes.Length && routes[index].closed;
        public float RouteLength(int index) => multi ? (index >= 0 && index < routes.Length ? routes[index].total : 0f) : CourseLength;

        /// <summary>Distance along route `index` of its lane point nearest to `position` (height weighted).</summary>
        public float RouteDistanceNearest(int index, Vector3 position)
        {
            if (!multi) return DistanceNearest(position);
            if (index < 0 || index >= routes.Length) return 0f;
            routes[index].Nearest(position, true, out float s, out _);
            return s;
        }

        /// <summary>How far `position` is from route `index`'s lane line (dx² + dz² + 4 dy² under the root).</summary>
        public float RouteLaneDistance(int index, Vector3 position)
        {
            if (!multi)
            {
                if (cumulative == null) return float.PositiveInfinity;
                var p = LanePointAt(NearestDistance(position)) - position;
                return Mathf.Sqrt(p.x * p.x + p.z * p.z + 4f * p.y * p.y);
            }
            if (index < 0 || index >= routes.Length) return float.PositiveInfinity;
            routes[index].Nearest(position, true, out _, out float d);
            return d;
        }

        /// <summary>The lane point of route `index` at s (wrapped on a closed route), `lateral` metres further right.</summary>
        public Vector3 RouteLanePointAt(int index, float s, float lateral = 0f)
        {
            if (!multi)
            {
                var p = LanePointAt(s);
                if (lateral == 0f || cumulative == null) return p;
                var t = LanePointAt(s + 1f) - LanePointAt(s - 1f); t.y = 0f;
                return p + Vector3.Cross(Vector3.up, t).normalized * lateral;
            }
            if (index < 0 || index >= routes.Length) return Vector3.zero;
            var r = routes[index];
            r.LanePose(r.closed ? s : Mathf.Clamp(s, 0f, r.total - 1f), lateral, out var point, out _);
            return point;
        }

        public int JumpCount => jumpDistances != null ? jumpDistances.Length : 0;
        public string JumpName(int i) => jumpNames != null && i >= 0 && i < jumpNames.Length ? jumpNames[i] : "";
        public float JumpDistance(int i) => jumpDistances != null && i >= 0 && i < jumpDistances.Length ? jumpDistances[i] : 0f;
        public int JumpRoute(int i) => multi && jumpRoutes != null && i >= 0 && i < jumpRoutes.Length ? Mathf.Clamp(jumpRoutes[i], 0, routes.Length - 1) : ActiveRoute;

        /// <summary>The route auto-drive takes at the next fork (it moves over where the two share the road).</summary>
        public void SetPreferredRoute(int index)
        {
            if (!multi) return;
            preferredRoute = Mathf.Clamp(index, 0, routes.Length - 1);
            if (autoDriving) TryJoinPreferred();
        }

        // Leaves auto-drive without a hard stop: the car keeps the speed it had.
        private void EndAutoDrive()
        {
            autoDriving = false;
            vehicle.SetSpeed(currentAutoSpeed);
        }

        private void RecoverToForward()
        {
            if (vehicle.IsReversing) vehicle.ToggleDirection();
            vehicle.ResetSteering();
        }

        public void BeginAutoDrive()
        {
            if (multi)
            {
                RecoverToForward();
                var position = vehicle.transform.position;
                // the route under the car (the nearest lane line; the active route keeps a tie), even right after a teleport
                float bestD = float.MaxValue;
                foreach (int k in RouteOrder())
                {
                    routes[k].Nearest(position, true, out float s, out float d);
                    if (d < bestD - 0.01f) { bestD = d; autoS = s; autoRoute = k; }
                }
                active = autoRoute;
                // from where the car is across the lane (a small way off: eased into the lane), not a jump sideways
                routes[autoRoute].LanePose(autoS, 0f, out var lanePoint, out _);
                float across = Vector3.Dot(position - lanePoint, routes[autoRoute].RightAt(autoS));
                StartBlend(Mathf.Abs(across) <= 4f ? across : 0f);
                currentAutoSpeed = Mathf.Max(vehicle.SpeedMps, 4f);
                autoDriving = true;
                TryJoinPreferred();
                return;
            }
            if (cumulative == null) return;
            RecoverToForward(); // auto-drive hands its speed back through SetSpeed, which would otherwise be a reverse speed
            autoS = NearestDistance(vehicle.transform.position);
            currentAutoSpeed = Mathf.Max(vehicle.SpeedMps, 4f);
            autoDriving = true;
        }

        public void JumpTo(float distance)
        {
            if (multi) { JumpTo(active, distance); return; }
            if (cumulative == null) return;
            autoS = Mathf.Clamp(distance, 0f, total - 1f);
            RecoverToForward();
            vehicle.Stop();
            Place(autoS);
        }

        /// <summary>Puts the car on route `index` at `distance` (wrapped on a closed route), standing.</summary>
        public void JumpTo(int index, float distance)
        {
            if (!multi) { JumpTo(distance); return; }
            if (vehicle == null) return;
            index = Mathf.Clamp(index, 0, routes.Length - 1);
            var r = routes[index];
            active = autoRoute = index;
            autoS = r.closed ? r.Wrap(distance) : Mathf.Clamp(distance, 0f, r.total - 1f);
            StartBlend(0f);
            RecoverToForward();
            vehicle.Stop();
            PlaceOn(index, autoS, 0f);
        }

        /// <summary>Escape hatch for dead ends and wrong turns: put the car on the nearest course point.</summary>
        public void ReturnToCourse()
        {
            if (multi)
            {
                if (vehicle == null) return;
                var position = vehicle.transform.position;
                // the nearest lane point of every route; on shared road the active route, then the preferred one, keep it
                int best = -1; float bestD = float.MaxValue, bestS = 0f;
                foreach (int k in RouteOrder())
                {
                    routes[k].Nearest(position, true, out float s, out float d);
                    if (d < bestD - 0.01f) { bestD = d; bestS = s; best = k; }
                }
                active = autoRoute = best;
                autoS = bestS;
                StartBlend(0f);
                RecoverToForward();
                vehicle.Stop();
                PlaceOn(best, autoS, 0f);
                if (gameManager != null && !gameManager.IsRunning && gameManager.InputReady) gameManager.StartPractice();
                return;
            }
            if (cumulative == null || vehicle == null) return;
            autoS = Mathf.Clamp(NearestDistance(vehicle.transform.position), 0f, total - 1f);
            RecoverToForward();
            vehicle.Stop();
            Place(autoS);
            if (gameManager != null && !gameManager.IsRunning && gameManager.InputReady) gameManager.StartPractice();
        }

        private void Place(float s)
        {
            Sample(s, out var position, out var tangent, out var lane);
            var right = Vector3.Cross(Vector3.up, new Vector3(tangent.x, 0f, tangent.z)).normalized;
            vehicle.transform.SetPositionAndRotation(
                position + right * lane + Vector3.up * rideHeight,
                Quaternion.LookRotation(tangent));
        }

        private Vector3 TangentAt(float s)
        {
            Sample(s, out _, out var tangent, out _);
            return tangent;
        }

        private float NearestDistance(Vector3 position)
        {
            int best = 0;
            float bestScore = float.MaxValue;
            for (int i = 0; i < path.Length; i++)
            {
                var d = path[i] - position;
                // Height counts so that standing under the viaduct picks the road, not the deck above.
                var score = d.x * d.x + d.z * d.z + d.y * d.y * 4f;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return cumulative[best];
        }

        private void Sample(float s, out Vector3 position, out Vector3 tangent, out float lane)
        {
            s = Mathf.Clamp(s, 0f, total);
            int lo = 0, hi = cumulative.Length - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) / 2;
                if (cumulative[mid] <= s) lo = mid; else hi = mid;
            }
            float segment = cumulative[hi] - cumulative[lo];
            float t = segment > 0.0001f ? (s - cumulative[lo]) / segment : 0f;
            position = Vector3.Lerp(path[lo], path[hi], t);
            tangent = (path[hi] - path[lo]).normalized;
            lane = laneOffsets != null && laneOffsets.Length == path.Length ? Mathf.Lerp(laneOffsets[lo], laneOffsets[hi], t) : 1.9f;
        }

        // ---- several routes: auto-drive, the route under the car, the fork

        private void AutoDriveRoutes()
        {
            var r = routes[autoRoute];
            float dt = Time.deltaTime;
            r.LanePose(autoS, 0f, out _, out var here);
            r.LanePose(autoS + 40f, 0f, out _, out var ahead);
            float bend = Vector3.Angle(here, ahead);
            float wanted = Mathf.Lerp(r.SpeedAt(autoS, autoSpeed), 8f, Mathf.Clamp01(bend / 35f));
            // auto-drive skips VehicleController.Simulate, so the car-ahead limit is applied to the wanted speed here
            wanted = Mathf.Min(wanted, AheadLimit(true));
            currentAutoSpeed = Mathf.MoveTowards(currentAutoSpeed, wanted, dt * (wanted < currentAutoSpeed ? 9f : 4f));
            float step = currentAutoSpeed * dt;
            autoS += step;
            if (!r.closed && autoS >= r.total - 1f) { EndAutoDrive(); return; }
            if (r.closed && autoS >= r.total) autoS -= r.total;   // round and round
            blendDone += step;
            PlaceOn(autoRoute, autoS, BlendOffset());
        }

        private void PlaceOn(int index, float s, float lateral)
        {
            routes[index].LanePose(s, lateral, out var point, out var forward);
            vehicle.transform.SetPositionAndRotation(point + Vector3.up * rideHeight, Quaternion.LookRotation(forward));
        }

        // sideways offset (m, to the right) left from a change of lane or route, eased out over BlendLength of travel
        private void StartBlend(float offset) { blendFrom = offset; blendDone = offset == 0f ? BlendLength : 0f; }
        private float BlendOffset() => blendDone >= BlendLength ? 0f : blendFrom * (1f - Mathf.SmoothStep(0f, 1f, blendDone / BlendLength));

        // the active route first, then the preferred one, then the rest: who keeps a tie on shared road
        private System.Collections.Generic.IEnumerable<int> RouteOrder()
        {
            yield return active;
            if (preferredRoute != active) yield return preferredRoute;
            for (int k = 0; k < routes.Length; k++) if (k != active && k != preferredRoute) yield return k;
        }

        // About five times a second. Auto-drive: the active route is the one driven, and it moves over to the preferred route where
        // the two share the road. Otherwise: the active route is left when the car is more than 2.5 m from its lane line and another
        // route's lane line is within 1 m (or 3 m nearer: another lane of the other branch), but only where the two routes part
        // (50 m on along the active route the other's lane line is more than 3 m away): at a merge, where the other route's lane
        // comes across the car's path, and on shared road the active route stays. The distance is then measured on the new route.
        private void CheckRoute()
        {
            if (autoDriving)
            {
                active = autoRoute;
                if (autoRoute != preferredRoute) TryJoinPreferred();
                return;
            }
            var position = vehicle.transform.position;
            routes[active].Nearest(position, true, out float ownS, out float own);
            if (own <= LeaveDistance) return;
            int best = -1; float bestD = float.MaxValue;
            for (int k = 0; k < routes.Length; k++)
            {
                if (k == active) continue;
                float d = RouteLaneDistance(k, position);
                if (d < bestD) { bestD = d; best = k; }
            }
            if (best < 0 || (bestD > JoinDistance && bestD + JoinMargin > own)) return;
            routes[active].LanePose(ownS + PartingAhead, 0f, out var ahead, out _);
            if (RouteLaneDistance(best, ahead) > PartingGap) active = best;
        }

        // Auto-drive changes to the preferred route only where both centre lines are the same road here and BlendLength further on,
        // so the change of lane is finished before a fork; past that point it waits for the next shared road.
        private bool TryJoinPreferred()
        {
            if (!autoDriving || autoRoute == preferredRoute) return false;
            var from = routes[autoRoute];
            var to = routes[preferredRoute];
            var centre = from.PointAt(autoS);
            to.Nearest(centre, false, out float s, out float d);
            if (d > SharedTolerance) return false;
            if ((from.PointAt(autoS + BlendLength + 10f) - to.PointAt(s + BlendLength + 10f)).magnitude > SharedTolerance) return false;
            from.LanePose(autoS, BlendOffset(), out var placed, out _);
            to.LanePose(s, 0f, out var lane, out _);
            float across = Vector3.Dot(placed - lane, to.RightAt(s));
            autoRoute = active = preferredRoute;
            autoS = s;
            StartBlend(across);
            return true;
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        private void OnGUI()
        {
            if (!showGuide) return;
            if (guideStyle == null)
                guideStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = Mathf.Max(13, Screen.height / 46),
                    wordWrap = false,
                    padding = new RectOffset(12, 12, 8, 8),
                };
            var text = "W 가속  Space 제동  ← → 조향  V 시점  R 코스 복귀\nT 자동주행" + (autoDriving ? " (켜짐)" : "") + "  G 안내 숨기기";
            if (multi) text += "\n지금 경로: " + routes[active].name + "    F 갈림길 선택: " + routes[preferredRoute].name;
            if (jumpNames != null)
                for (int i = 0; i < jumpNames.Length; i++)
                    text += (i % 3 == 0 ? "\n" : "   ") + (i + 1) + " " + jumpNames[i];
            var content = new GUIContent(text);
            var size = guideStyle.CalcSize(content);
            GUI.Box(new Rect(12f, Screen.height - size.y - 12f, size.x, size.y), content, guideStyle);
        }
#endif
    }
}
