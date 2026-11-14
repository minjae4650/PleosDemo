using System.Collections.Generic;
using UnityEngine;

namespace BusanEnvironment
{
    /// <summary>
    /// Ambient traffic for the drive slice. A pool of cars follows baked polylines (no physics, no signals):
    /// course cars use the course path only (oncoming cars never on one-way stretches, the bridge is one
    /// direction), corridor cars follow arterial roads in their way direction so the opposite carriageway is
    /// not empty. Course cars live in a window around the player (450 m ahead, 150 m behind) and are
    /// re-placed at the far edge of it, so they never pop up in view.
    /// </summary>
    public sealed class RouteTraffic : MonoBehaviour
    {
        [SerializeField] private TrafficData data;
        [SerializeField] private GameObject[] vehiclePrefabs;
        [SerializeField] private Transform player;
        [SerializeField] private int poolSize = 34;
        [SerializeField] private int corridorCars = 14;
        [SerializeField] private int minInWindow = 8;
        [SerializeField] private float aheadWindow = 480f; // cars are placed at 450-480 m, i.e. beyond view
        [SerializeField] private float behindWindow = 150f;
        [SerializeField] private float vehicleYOffset = 0f;
        [SerializeField] private int triangleBudget = 1100000;   // two Porter II (100k each) fit without pushing the rest to the cheapest models
        [SerializeField] private Material[] paintMaterials;   // seven body colours (white 30, black 20, grey 20, silver 15, navy 7, wine 5, blue 3 percent)
        [SerializeField] private float[] paintWeights;
        [SerializeField] private TrafficSignals signals;       // optional: cars stop at their stop line on red
        [SerializeField] private LayerMask roadMask = 1 << 30; // road surface colliders (BusanSliceBuilder.RoadLayer)

        private readonly Dictionary<long, SignalStop[]> stopsByPath = new Dictionary<long, SignalStop[]>();
        private int redStops;
        private static long PathKey(int path, int dir) => ((long)path << 2) ^ (dir > 0 ? 1 : 2);

        private enum Mode { Course, Corridor }

        private sealed class Car
        {
            public Transform T;
            public Mode Mode;
            public int Path;
            public float S;
            public int Dir = 1;
            public int Slot;
            public float Cruise;
            public float Speed;
            public float Wait;
            public float IgnorePlayerUntil;
            public bool Active;
            public float StoppedTime;
            public bool Frozen;
            public bool Queued;   // standing at a red light, or behind a car that is (it will move on green)
            public float LaneNow; public bool LaneSet;   // drawn lateral offset, easing toward Lane(car) (no sideways jumps)
            public CarModel Model;
            public float PrevSpeed;
            public Vector3 SamplePos;
            public bool HasSample;
        }

        private readonly List<Car> cars = new List<Car>();
        private float[] courseCum;
        private float courseTotal;
        private float[] courseCurve;
        private float[][] corridorCum;
        private float[][] corridorCurve;
        private readonly List<(int path, float s)> corridorSpots = new List<(int, float)>();
        private float playerS;
        private bool deficit;
        private float nextPlayerScan, nextSample;
        private System.Random random;

        // measurements for the bridge report
        private int relocations, corridorRelocations, stoppedRelocations, samples;
        private float minSameLaneBumperGap = float.MaxValue;
        private string minGapInfo = "";
        private double aheadSum, windowSum, activeSum;
        private int tailFirstMoves, wrongSideSamples;   // moved against the car's own +Z (the nose); on the left of its travel on a two-way part of the course
        private string tailFirstInfo = "";

        public string StatsLine()
        {
            double n = Mathf.Max(1, samples);
            int courseActive = 0, courseTotalCars = 0, behind = 0, farAhead = 0, lampsOn = 0;
            foreach (var c in cars)
            {
                if (c.Active && c.Model != null && c.Model.brakeLamps != null && c.Model.brakeLamps.activeSelf) lampsOn++;
                if (c.Mode != Mode.Course) continue;
                courseTotalCars++;
                if (!c.Active) continue;
                courseActive++;
                if (c.S - playerS < -behindWindow) behind++;
                if (c.S - playerS > aheadWindow) farAhead++;
            }
            return $"fleet[{fleetSummary}] tailFirstMoves={tailFirstMoves} {tailFirstInfo} wrongSideSamples={wrongSideSamples} signalStops={(data.stops != null ? data.stops.Length : 0)} carFramesHeldAtRed={redStops} brakeLampsOn={lampsOn}/{cars.Count} minSameLaneBumperGap={(minSameLaneBumperGap > 1e6f ? -1f : minSameLaneBumperGap):0.0}m ({minGapInfo}) stoppedRelocations={stoppedRelocations} courseCars={courseActive}/{courseTotalCars} outsideBehind={behind} outsideAhead={farAhead} playerS={playerS:0} relocations={relocations} corridorRelocations={corridorRelocations} avgCarsAhead150={aheadSum / n:0.0} avgCourseCarsInWindow={windowSum / n:0.0} avgActive={activeSum / n:0.0} samples={samples}";
        }

        /// <summary>Player lane centre, metres to the right of the centreline.</summary>
        public static float PlayerLane(float width, bool oneWay)
        {
            if (!oneWay) return Mathf.Min(width * 0.25f, 2f);
            int lanes = OneWayLanes(width);
            float spacing = width / lanes;
            return lanes >= 2 ? spacing * 0.5f : 0f;
        }

        // Wide one-way carriageways (the bridge) are marked as 4.5 m lanes; ramps are a single lane.
        private static int OneWayLanes(float width) => width >= 14f ? Mathf.Max(2, Mathf.RoundToInt(width / 4.5f)) : 1;

        private static float CourseLane(float width, bool oneWay, int slot, int dir)
        {
            if (oneWay)
            {
                int lanes = OneWayLanes(width);
                int index = lanes >= 3 ? new[] { 0, 2, 3 }[slot % 3] : (lanes == 2 ? 1 : 0);
                index = Mathf.Min(index, lanes - 1);
                return ((lanes - 1) * 0.5f - index) * (width / lanes);
            }
            float inner = Mathf.Min(width * 0.25f, 2f);
            float outer = width >= 13f ? 5.5f : inner;   // the car eases into a changed lane (Car.LaneNow), it never jumps
            return (slot % 2 == 0 ? inner : outer) * dir;
        }

        private void Start()
        {
            if (data == null || data.course == null || data.course.Length < 2 || vehiclePrefabs == null || vehiclePrefabs.Length == 0)
            {
                enabled = false;
                return;
            }
            random = new System.Random(5115);
            courseCum = Cumulative(data.course);
            courseTotal = courseCum[courseCum.Length - 1];
            courseCurve = CurveSpeeds(data.course, courseCum);
            int k = data.corridors != null ? data.corridors.Length : 0;
            corridorCum = new float[k][];
            corridorCurve = new float[k][];
            for (int c = 0; c < k; c++)
            {
                corridorCum[c] = Cumulative(data.corridors[c].points);
                corridorCurve[c] = CurveSpeeds(data.corridors[c].points, corridorCum[c]);
                for (float s = 10f; s < corridorCum[c][corridorCum[c].Length - 1] - 10f; s += 20f) corridorSpots.Add((c, s));
            }
            if (k == 0) corridorCars = 0;

            if (data.stops != null)
                foreach (var g in System.Linq.Enumerable.GroupBy(data.stops, st => PathKey(st.path, st.dir)))
                    stopsByPath[g.Key] = System.Linq.Enumerable.ToArray(g);

            playerS = player != null ? NearestCourseS(player.position) : 0f;
            var models = new CarModel[vehiclePrefabs.Length];
            for (int p = 0; p < models.Length; p++) models[p] = vehiclePrefabs[p].GetComponent<CarModel>();
            var uses = new int[vehiclePrefabs.Length];
            long usedTriangles = 0;
            int usedRenderers = 0;
            // Guaranteed models (CarModel.minCount): fixed Course-mode slots, counted as used before the random draw, so the draw can never
            // exceed maxCount and the result does not depend on the seed.
            var forced = new int[poolSize];
            for (int i = 0; i < poolSize; i++) forced[i] = -1;
            int courseSlots = Mathf.Max(1, poolSize - corridorCars), forcedNo = 0;
            for (int p = 0; p < models.Length; p++)
            {
                if (models[p] == null) continue;
                for (int j = 0; j < models[p].minCount && forcedNo < courseSlots; j++)
                {
                    int slot = Mathf.Min(courseSlots - 1, 5 + forcedNo * 8);
                    while (forced[slot] >= 0) slot = (slot + 1) % courseSlots;
                    forced[slot] = p; forcedNo++;
                    uses[p]++; usedTriangles += Tris(models, p);
                }
            }
            for (int i = 0; i < poolSize; i++)
            {
                int pick;
                if (forced[i] >= 0) pick = forced[i];
                else
                {
                    pick = PickModel(models, uses);
                    int cheapest = Cheapest(models, uses);
                    uses[pick]++;   // the rest of the pool is costed as if this pick is made
                    long reserve = CheapestFill(models, uses, poolSize - i - 1);
                    uses[pick]--;
                    if (usedTriangles + Tris(models, pick) + reserve > triangleBudget) pick = cheapest;
                    uses[pick]++; usedTriangles += Tris(models, pick);
                }
                var prefab = vehiclePrefabs[pick];
                var go = Instantiate(prefab, transform);
                go.name = prefab.name + " (traffic)";
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                usedRenderers += go.GetComponentsInChildren<Renderer>(true).Length;
                var model = go.GetComponent<CarModel>();
                if (model != null && !model.fixedPaint) model.SetPaint(PickPaint(model.lightPaintOnly));
                var car = new Car { T = go.transform, Mode = i < poolSize - corridorCars ? Mode.Course : Mode.Corridor, Slot = i, Model = model };
                cars.Add(car);
                go.SetActive(false);
            }
            var counts = new System.Text.StringBuilder();
            for (int p = 0; p < models.Length; p++) if (uses[p] > 0) counts.Append(models[p] != null ? models[p].modelName : vehiclePrefabs[p].name).Append(':').Append(uses[p]).Append(' ');
            fleetSummary = $"pool={poolSize} triangles={usedTriangles} renderers={usedRenderers} models={counts}";
            Debug.Log("BUSAN_TRAFFIC " + fleetSummary);
            // initial spread: course cars anywhere in the window except right around the player
            foreach (var car in cars)
            {
                if (car.Mode == Mode.Course) PlaceCourse(car, true);
                else PlaceCorridor(car);
            }
        }

        // Cheapest possible cost of `count` more cars: cheapest models first, each only up to its maxCount.
        private static long CheapestFill(CarModel[] models, int[] uses, int count)
        {
            var order = new List<int>();
            for (int i = 0; i < models.Length; i++) order.Add(i);
            order.Sort((x, y) => Tris(models, x).CompareTo(Tris(models, y)));
            long cost = 0;
            foreach (int i in order)
            {
                int cap = models[i] != null ? Mathf.Max(0, models[i].maxCount - uses[i]) : count;
                int take = Mathf.Min(cap, count);
                cost += (long)take * Tris(models, i); count -= take;
            }
            if (count > 0) cost += (long)count * Tris(models, order[order.Count - 1]);
            return cost;
        }

        private static int Tris(CarModel[] models, int index) => models[index] != null ? models[index].triangles : 30000;

        // Weighted by CarModel.weight, a model stops being offered at its maxCount (Porter 2, the rest 8).
        private int PickModel(CarModel[] models, int[] uses)
        {
            float total = 0f;
            for (int i = 0; i < models.Length; i++) if (models[i] != null && uses[i] < models[i].maxCount) total += models[i].weight;
            if (total <= 0f) return random.Next(models.Length);
            float pick = (float)random.NextDouble() * total;
            for (int i = 0; i < models.Length; i++)
            {
                if (models[i] == null || uses[i] >= models[i].maxCount) continue;
                pick -= models[i].weight;
                if (pick <= 0f) return i;
            }
            return 0;
        }

        private static int Cheapest(CarModel[] models, int[] uses)
        {
            int best = -1;
            for (int i = 0; i < models.Length; i++)
            {
                if (models[i] == null || uses[i] >= models[i].maxCount) continue;
                if (best < 0 || models[i].triangles < models[best].triangles) best = i;
            }
            if (best >= 0) return best;
            best = 0;
            for (int i = 1; i < models.Length; i++) if (Tris(models, i) < Tris(models, best)) best = i;
            return best;
        }

        // lightOnly: white (index 0) and silver (index 3) only; the order is the paint palette order in BusanSliceVehicles.
        private Material PickPaint(bool lightOnly = false)
        {
            if (paintMaterials == null || paintMaterials.Length == 0) return null;
            bool Allowed(int i) => !lightOnly || i == 0 || i == 3;
            float total = 0f;
            for (int i = 0; i < paintMaterials.Length; i++) if (Allowed(i)) total += paintWeights != null && i < paintWeights.Length ? paintWeights[i] : 1f;
            float pick = (float)random.NextDouble() * total;
            for (int i = 0; i < paintMaterials.Length; i++)
            {
                if (!Allowed(i)) continue;
                pick -= paintWeights != null && i < paintWeights.Length ? paintWeights[i] : 1f;
                if (pick <= 0f) return paintMaterials[i];
            }
            return paintMaterials[0];
        }

        private string fleetSummary = "";
        public string FleetSummary => fleetSummary;

        private static float[] Cumulative(Vector3[] p)
        {
            var c = new float[p.Length];
            for (int i = 1; i < p.Length; i++) c[i] = c[i - 1] + Vector3.Distance(p[i - 1], p[i]);
            return c;
        }

        // Comfortable lateral acceleration 3.5 m/s^2 -> v = sqrt(a / curvature), measured over about 30 m.
        private static float[] CurveSpeeds(Vector3[] p, float[] cum)
        {
            var v = new float[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                int a = i, b = i;
                while (a > 0 && cum[i] - cum[a] < 15f) a--;
                while (b < p.Length - 1 && cum[b] - cum[i] < 15f) b++;
                var t0 = p[i] - p[a]; var t1 = p[b] - p[i];
                t0.y = 0f; t1.y = 0f;
                float len = Mathf.Max(cum[b] - cum[a], 1f);
                float angle = (t0.sqrMagnitude < 1e-4f || t1.sqrMagnitude < 1e-4f) ? 0f : Vector3.Angle(t0, t1) * Mathf.Deg2Rad;
                float curvature = angle / (len * 0.5f);
                v[i] = curvature < 1e-3f ? 99f : Mathf.Clamp(Mathf.Sqrt(3.5f / curvature), 4f, 99f);
            }
            return v;
        }

        // ---------- course helpers ----------

        private static int IndexAt(float[] cum, float s)
        {
            s = Mathf.Clamp(s, 0f, cum[cum.Length - 1]);
            int lo = 0, hi = cum.Length - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) / 2;
                if (cum[mid] <= s) lo = mid; else hi = mid;
            }
            return lo;
        }

        private bool OneWayAt(float s) => data.courseOneWay != null && data.courseOneWay.Length == data.course.Length && data.courseOneWay[IndexAt(courseCum, s)];
        private float WidthAt(float s) => data.courseWidth != null && data.courseWidth.Length == data.course.Length ? data.courseWidth[IndexAt(courseCum, s)] : 12f;

        private float NearestCourseS(Vector3 position)
        {
            int best = 0; float bestScore = float.MaxValue;
            for (int i = 0; i < data.course.Length; i++)
            {
                var d = data.course[i] - position;
                // height counts, so standing under the viaduct picks the road rather than the deck above
                float score = d.x * d.x + d.z * d.z + d.y * d.y * 4f;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return courseCum[best];
        }

        private bool ClearOfCars(Car self, Mode mode, int path, float s, float gap)
        {
            foreach (var o in cars)
            {
                if (o == self || !o.Active || o.Mode != mode || o.Path != path) continue;
                if (Mathf.Abs(o.S - s) < gap) return false;
            }
            return true;
        }

        private void PlaceCourse(Car car, bool initial)
        {
            for (int attempt = 0; attempt < 24; attempt++)
            {
                // normal refill: 450-480 m ahead (out of view). Deficit refill: 60-150 m behind (outside the view frustum).
                float s = initial
                    ? playerS - 100f + (float)random.NextDouble() * (aheadWindow + 100f)
                    : deficit ? playerS - 60f - (float)random.NextDouble() * (behindWindow - 60f)
                              : playerS + 450f + (float)random.NextDouble() * (aheadWindow - 450f);
                if (s < 10f || s > courseTotal - 20f) continue; // outside the course: draw again, never pop up inside the view
                if (initial && Mathf.Abs(s - playerS) < 45f) continue;
                bool oneWay = OneWayAt(s);
                int dir = oneWay || random.NextDouble() < 0.55 ? 1 : -1;
                if (!ClearOfCars(car, Mode.Course, 0, s, 14f)) continue;
                car.Path = 0; car.S = s; car.Dir = dir;
                // mostly stay out of the player's lane so the player is not boxed in
                // (the lap bot spent 134 of 240 s queued behind cars in its lane at 30 percent)
                // (even slots share the player's inner lane: forward cars mostly take the odd, outer slots)
                car.Slot = dir > 0 ? (random.NextDouble() < 0.15 ? 0 : (random.NextDouble() < 0.5 ? 1 : 3)) : 1 + random.Next(3);
                float bonus = data.courseSpeed != null && data.courseSpeed.Length == data.course.Length ? Mathf.Max(0f, data.courseSpeed[IndexAt(courseCum, s)] - 16f) : 0f;
                car.Cruise = (dir > 0 ? 13f : 11f) + (float)random.NextDouble() * 4f + bonus;
                car.Speed = car.Cruise;
                car.Wait = 0f; car.IgnorePlayerUntil = 0f;
                car.Active = true;
                car.T.gameObject.SetActive(true);
                car.HasSample = false;   // a re-placement is a jump, not driving
                if (!initial) relocations++;
                Apply(car);
                return;
            }
            car.Active = false;
            car.T.gameObject.SetActive(false);
        }

        private void PlaceCorridor(Car car)
        {
            car.Active = false;
            car.T.gameObject.SetActive(false);
            if (corridorSpots.Count == 0 || player == null) return;
            int start = random.Next(corridorSpots.Count);
            for (int n = 0; n < corridorSpots.Count; n++)
            {
                var (path, s) = corridorSpots[(start + n) % corridorSpots.Count];
                var pos = CorridorPos(path, s, out _);
                float dist = Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(player.position.x, 0f, player.position.z));
                var toSpot = new Vector3(pos.x - player.position.x, 0f, pos.z - player.position.z);
                bool inFront = Vector3.Dot(toSpot.normalized, new Vector3(player.forward.x, 0f, player.forward.z).normalized) > 0.2f;
                if (dist < 250f || dist > 600f || (inFront && dist < 450f)) continue; // beyond view, or behind/beside the camera
                if (!ClearOfCars(car, Mode.Corridor, path, s, 30f)) continue;
                car.Path = path; car.S = s; car.Dir = 1;
                car.Cruise = 10f + (float)random.NextDouble() * 4f;
                car.Speed = car.Cruise;
                car.Wait = 0f; car.IgnorePlayerUntil = 0f;
                car.Active = true;
                car.T.gameObject.SetActive(true);
                car.HasSample = false;   // a re-placement is a jump, not driving
                corridorRelocations++;
                Apply(car);
                return;
            }
        }

        private Vector3 CorridorPos(int path, float s, out Vector3 tangent)
        {
            var pts = data.corridors[path].points;
            var cum = corridorCum[path];
            int lo = IndexAt(cum, s);
            int hi = Mathf.Min(lo + 1, pts.Length - 1);
            float seg = cum[hi] - cum[lo];
            float t = seg > 1e-4f ? Mathf.Clamp01((s - cum[lo]) / seg) : 0f;
            tangent = (pts[hi] - pts[lo]).normalized;
            return Vector3.Lerp(pts[lo], pts[hi], t);
        }

        // ---------- simulation ----------

        /// <summary>Nearest ambient car in front of a position (same heading, similar height, same lane).</summary>
        public bool TryGetAhead(Vector3 position, Vector3 forward, float range, out float gap, out float speed)
            => TryGetAhead(position, forward, range, out gap, out speed, out _);

        /// <summary>As above; `queued` says the car ahead waits at a red light (or behind one that does) and will move on green.</summary>
        public bool TryGetAhead(Vector3 position, Vector3 forward, float range, out float gap, out float speed, out bool queued)
        {
            gap = float.MaxValue;
            speed = 0f;
            queued = false;
            forward.y = 0f;
            forward.Normalize();
            foreach (var car in cars)
            {
                if (!car.Active) continue;
                var to = car.T.position - position;
                if (Mathf.Abs(to.y) > 2.5f) continue;
                var f = car.T.forward; f.y = 0f; f.Normalize();
                if (Vector3.Dot(f, forward) < 0.7f) continue;
                var flat = new Vector3(to.x, 0f, to.z);
                float ahead = Vector3.Dot(flat, forward);
                if (ahead <= 0f || ahead > range || ahead >= gap) continue;
                if (Mathf.Abs(Vector3.Dot(flat, Vector3.Cross(Vector3.up, forward))) > 2.6f) continue;
                gap = ahead;
                speed = car.Speed;
                queued = car.Queued && !car.Frozen;
            }
            return gap < float.MaxValue;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float now = Time.time;
            if (player != null && now >= nextPlayerScan)
            {
                nextPlayerScan = now + 0.25f;
                float previous = playerS;
                playerS = NearestCourseS(player.position);
                if (Mathf.Abs(playerS - previous) > 200f) RespreadAfterJump();
                Maintain();
            }
            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                if (!car.Active || car.Frozen) continue;
                float[] curve = car.Mode == Mode.Course ? courseCurve : corridorCurve[car.Path];
                float[] cum = car.Mode == Mode.Course ? courseCum : corridorCum[car.Path];
                float target = Mathf.Min(car.Cruise, curve[IndexAt(cum, car.S + car.Dir * 10f)]);

                // keep distance from the car ahead on the same path, direction and lane slot
                bool queuedBehind = false;
                foreach (var other in cars)
                {
                    if (other == car || !other.Active || other.Mode != car.Mode || other.Path != car.Path || other.Dir != car.Dir) continue;
                    if (Mathf.Abs(Lane(other) - Lane(car)) > 1.5f) continue; // different lane: not "ahead"
                    float gap = (other.S - car.S) * car.Dir;
                    if (gap <= 0f) continue;
                    if (gap < 14f && other.Queued) queuedBehind = true;
                    if (now > 3f && gap - 4.6f < minSameLaneBumperGap) { minSameLaneBumperGap = gap - 4.6f; minGapInfo = $"{car.Mode} path{car.Path} dir{car.Dir} s={car.S:0} v={car.Speed:0.0}/{other.Speed:0.0} lane={Lane(car):0.0}/{Lane(other):0.0} slot={car.Slot}/{other.Slot} t={now:0}"; }
                    if (gap < 7.5f) car.Speed = Mathf.Min(car.Speed, other.Speed); // never close in on a car this near
                    // brake early enough for a 6 m/s^2 stop behind the car ahead (7 m centre gap = ~2.4 m bumper gap)
                    target = Mathf.Min(target, other.Speed + Mathf.Sqrt(Mathf.Max(0f, 12f * (gap - 7f))));
                    if (gap < 10.6f) target = Mathf.Min(target, other.Speed * Mathf.Clamp01((gap - 4.6f) / 6f)); // open the gap back up to >= 4 m
                }

                // brake for the player just ahead in the same lane; give way for a few seconds, never deadlock
                bool blockedByPlayer = false;
                if (player != null && now > car.IgnorePlayerUntil)
                {
                    GetPose(car, out var pos, out var tangent);
                    var travel = tangent * car.Dir;
                    var toPlayer = player.position - pos;
                    if (Mathf.Abs(toPlayer.y) < 3f)
                    {
                        float ahead = Vector3.Dot(toPlayer, travel);
                        float lateral = Vector3.Dot(toPlayer, Vector3.Cross(Vector3.up, tangent));
                        if (ahead > 0f && ahead < 22f && Mathf.Abs(lateral - Lane(car)) < 2.6f)
                        {
                            blockedByPlayer = true;
                            target = ahead < 10f ? 0f : Mathf.Min(target, 4f);
                        }
                    }
                }
                // signal: stop with the front bumper at the stop line on red, and on yellow unless already close to it
                bool heldByRed = false;
                if (signals != null && signals.Ready && stopsByPath.TryGetValue(PathKey(car.Mode == Mode.Course ? -1 : car.Path, car.Dir), out var stops))
                {
                    float best = float.MaxValue; int bestStop = -1;
                    for (int k = 0; k < stops.Length; k++)
                    {
                        float g = (stops[k].s - car.S) * car.Dir - 4.2f;   // the line is painted 1.2-1.65 m before the lane end; half a car (2.3 m) behind it
                        if (g > -1.5f && g < 70f && g < best) { best = g; bestStop = k; }
                    }
                    if (bestStop >= 0)
                    {
                        int state = signals.HeadState(stops[bestStop].head, stops[bestStop].group);
                        if (state == TrafficSignals.Red || (state == TrafficSignals.Yellow && best > 12f))
                        {
                            heldByRed = true;
                            float gap = Mathf.Max(0f, best);
                            target = Mathf.Min(target, gap < 0.5f ? 0f : Mathf.Sqrt(2f * 3.0f * gap));
                        }
                    }
                }
                if (heldByRed && car.Speed < 0.3f) redStops++;
                car.Queued = heldByRed || queuedBehind;

                car.Wait = blockedByPlayer && car.Speed < 0.3f ? car.Wait + dt : 0f;
                if (car.Wait > 3f) { car.IgnorePlayerUntil = now + 6f; car.Wait = 0f; }

                car.PrevSpeed = car.Speed;
                car.Speed = Mathf.MoveTowards(car.Speed, target, dt * (target < car.Speed ? 9f : 3f));
                if (car.Model != null) car.Model.SetBraking(car.Speed < car.PrevSpeed - 0.01f || (car.Speed < 0.3f && (blockedByPlayer || heldByRed)));
                // waiting at a red light is normal: such a car is not recycled as a stuck one
                car.StoppedTime = car.Speed < 0.3f && !heldByRed ? car.StoppedTime + dt : 0f;
                // a car that has stood still for a while is recycled once the player cannot see it, so no queue lasts forever
                if (car.StoppedTime > 6f && player != null)
                {
                    var away = car.T.position - player.position; away.y = 0f;
                    if (away.magnitude > 120f || (away.magnitude > 25f && Vector3.Dot(away, player.forward) < 0f))
                    {
                        stoppedRelocations++;
                        car.StoppedTime = 0f;
                        if (car.Mode == Mode.Course) PlaceCourse(car, false); else PlaceCorridor(car);
                        continue;
                    }
                }
                float prevS = car.S;
                car.S += car.Speed * car.Dir * dt;
                // hard floor: never closer than a 4 m bumper gap to the same-lane car ahead (covers re-placements and lane changes).
                // The car is held where it was, never pushed back: a push of several metres read as a car reversing.
                foreach (var other in cars)
                {
                    if (other == car || !other.Active || other.Mode != car.Mode || other.Path != car.Path || other.Dir != car.Dir) continue;
                    if (Mathf.Abs(Lane(other) - Lane(car)) > 1.5f) continue;
                    float ahead = (other.S - car.S) * car.Dir;
                    if (ahead > 0f && ahead < 8.6f)
                    {
                        float floorS = other.S - car.Dir * 8.6f;
                        if ((floorS - prevS) * car.Dir < 0f) floorS = prevS;
                        car.S = floorS;
                        car.Speed = Mathf.Min(car.Speed, other.Speed);
                    }
                }

                float total = cum[cum.Length - 1];
                if (car.Mode == Mode.Corridor && car.S >= total - 1f)
                {
                    bool playerNear = player != null && Vector3.Distance(player.position, car.T.position) < 80f;
                    if (playerNear) { car.S = total - 1f; car.Speed = 0f; }
                    else { PlaceCorridor(car); continue; }
                }
                else if (car.Mode == Mode.Course && (car.S < 0f || car.S > total || (car.Dir < 0 && OneWayAt(car.S))))
                {
                    PlaceCourse(car, false);
                    continue;
                }
                Apply(car);
            }

            if (now >= nextSample && player != null)
            {
                nextSample = now + 0.5f;
                int ahead = 0, window = 0, active = 0;
                foreach (var car in cars)
                {
                    if (!car.Active) continue;
                    active++;
                    var to = car.T.position - player.position; to.y = 0f;
                    if (to.magnitude < 150f && Vector3.Dot(to, player.forward) > 0f) ahead++;
                    if (car.Mode == Mode.Course && car.S - playerS >= -behindWindow && car.S - playerS <= aheadWindow) window++;
                    var moved = car.T.position - car.SamplePos;
                    // a re-placed car jumps much further than 20 m in one sample, which says nothing about its direction
                    if (car.HasSample && moved.sqrMagnitude > 0.04f && moved.sqrMagnitude < 400f && Vector3.Dot(moved, car.T.forward) < 0f)
                    {
                        tailFirstMoves++;
                        if (tailFirstMoves <= 8) tailFirstInfo += $"[t={now:0.0} {car.Mode} path{car.Path} dir{car.Dir} s={car.S:0} v={car.Speed:0.0} moved={moved.magnitude:0.0}m along={Vector3.Dot(moved, car.T.forward):0.0} side={Vector3.Dot(moved, car.T.right):0.0} frozen={car.Frozen}]";
                    }
                    car.SamplePos = car.T.position; car.HasSample = true;
                    if (car.Mode == Mode.Course && !OneWayAt(car.S))
                    {
                        GetPose(car, out var centre, out _);
                        if (Vector3.Dot(car.T.position - centre, Vector3.Cross(Vector3.up, car.T.forward)) < 0f) wrongSideSamples++;
                    }
                }
                samples++; aheadSum += ahead; windowSum += window; activeSum += active;
            }
        }

        // A jump or respawn moves the player far along the course: spread the course cars around the new position
        // at once instead of letting them trickle in from the far edge.
        private void RespreadAfterJump()
        {
            foreach (var car in cars)
                if (car.Mode == Mode.Course && !car.Frozen) PlaceCourse(car, true);
        }

        /// <summary>Test hook: puts one car, standing still, in the player's lane `distance` metres ahead.</summary>
        public Transform TestPlaceStoppedAhead(Vector3 position, Vector3 forward, float distance)
        {
            foreach (var other in cars)
            {
                if (!other.Active || Vector3.Distance(other.T.position, position) > 200f) continue;
                other.Active = false;                     // clear the stretch so the test measures only the stopped car
                other.T.gameObject.SetActive(false);
            }
            if (player != null) playerS = NearestCourseS(player.position); // no re-spread for this teleport
            var car = cars[0];
            car.Frozen = true; car.Active = true; car.Speed = 0f; car.Cruise = 0f; car.HasSample = false;
            car.T.gameObject.SetActive(true);
            forward.y = 0f; forward.Normalize();
            car.T.SetPositionAndRotation(position + forward * distance, Quaternion.LookRotation(forward));
            return car.T;
        }

        // Keeps the window populated and recycles cars that left it (placement is at its far edge).
        private void Maintain()
        {
            int inWindow = 0;
            Car spare = null;
            int moved = 0;
            foreach (var car in cars)
            {
                if (car.Mode != Mode.Course || car.Frozen) continue;
                bool inside = car.Active && car.S - playerS >= -behindWindow && car.S - playerS <= aheadWindow;
                if (inside) { inWindow++; continue; }
                if (moved < (inWindow < minInWindow ? 6 : 2)) { deficit = inWindow < minInWindow; PlaceCourse(car, false); deficit = false; moved++; if (car.Active) inWindow++; }
                else if (spare == null) spare = car;
            }
            if (inWindow < minInWindow && spare != null) { deficit = true; PlaceCourse(spare, false); deficit = false; }
            foreach (var car in cars)
            {
                if (car.Mode != Mode.Corridor) continue;
                if (!car.Active) { PlaceCorridor(car); continue; }
                if (Vector3.Distance(car.T.position, player.position) > 650f) PlaceCorridor(car);
            }
        }

        private float Lane(Car car)
        {
            if (car.Mode == Mode.Corridor) return data.corridors[car.Path].lane;
            return CourseLane(WidthAt(car.S), OneWayAt(car.S), car.Slot, car.Dir);
        }

        private void GetPose(Car car, out Vector3 position, out Vector3 tangent)
        {
            if (car.Mode == Mode.Corridor)
            {
                position = CorridorPos(car.Path, car.S, out tangent);
                return;
            }
            position = CoursePoint(car.S);
            // heading over +-6 m, not the segment's: at a sharp vertex the lane offset turned in one frame and a car jumped ~5 m
            var ahead = CoursePoint(car.S + 6f) - CoursePoint(car.S - 6f);
            tangent = ahead.sqrMagnitude > 1e-6f ? ahead.normalized : Vector3.forward;
        }

        private Vector3 CoursePoint(float s)
        {
            s = Mathf.Clamp(s, 0f, courseCum[courseCum.Length - 1]);
            int lo = IndexAt(courseCum, s);
            int hi = Mathf.Min(lo + 1, data.course.Length - 1);
            float seg = courseCum[hi] - courseCum[lo];
            float t = seg > 1e-4f ? Mathf.Clamp01((s - courseCum[lo]) / seg) : 0f;
            return Vector3.Lerp(data.course[lo], data.course[hi], t);
        }

        private void Apply(Car car)
        {
            GetPose(car, out var pos, out var tangent);
            var right = Vector3.Cross(Vector3.up, new Vector3(tangent.x, 0f, tangent.z)).normalized;
            var forward = tangent * car.Dir;
            if (forward.sqrMagnitude < 0.0001f) return;
            float lane = Lane(car);
            // a re-placed car snaps to its lane; a driving one changes lane at 1.6 m/s (the outer lane steps where the road widens)
            car.LaneNow = !car.LaneSet || !car.HasSample ? lane : Mathf.MoveTowards(car.LaneNow, lane, Time.deltaTime * 1.6f);
            car.LaneSet = true;
            var p = pos + right * car.LaneNow;
            // the paths carry the designed road height; the car sits on the surface actually built under it (no hovering)
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 3.5f, roadMask, QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.point.y - p.y) < 1.5f)
                p.y = hit.point.y;
            car.T.SetPositionAndRotation(p + Vector3.up * vehicleYOffset, Quaternion.LookRotation(forward));
        }
    }
}
