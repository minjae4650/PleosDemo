using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusanEnvironment
{
    /// <summary>
    /// Moving cars for the landmark map. Each car follows one baked line (tools/network.py, LandmarkTraffic.json) at that line's
    /// speed: a closed line goes round for ever, an open one starts again at its beginning (both ends of the open line are
    /// inside a tunnel mouth, out of sight). No physics and no signals. A car slows for whatever is in front of it in its own
    /// lane, another car on any line or the player, and stands still rather than drive through it.
    /// The lines keep out of the lanes the routes use, so the player meets these cars beside them and coming the other way.
    /// </summary>
    public sealed class LoopTraffic : MonoBehaviour
    {
        [Serializable]
        public sealed class Line
        {
            public string name;
            public bool closed;
            public int cars;
            public Vector3[] points;
            public float[] speed;
            // room beside the line, in lanes, at each point (the bridge's other lanes; null where a line has none): each car of
            // the line keeps a lane of its own there, as far as the room allows (tools/network.py traffic)
            public float[] wideL, wideR;
        }

        [SerializeField] private Line[] lines = new Line[0];
        [SerializeField] private GameObject[] vehiclePrefabs = new GameObject[0];
        [SerializeField] private Material[] paintMaterials = new Material[0];
        [SerializeField] private Transform player;
        [SerializeField] private int seed = 1115;
        [SerializeField] private float standGap = 7.5f;       // nose to the middle of what is in front, standing
        [SerializeField] private float laneHalf = 1.5f;
        [SerializeField] private Vector4[] zebras = new Vector4[0];   // x, z, road direction angle (rad), half width: SignalLights stops
        [SerializeField] private float crossHalf = 3.2f;      // for a car of another line (where two lines cross or run together)
        [SerializeField] private float streetY = 4f;          // the height of the streets (and their zebra crossings)

        public void SetZebras(Vector4[] crossings) { zebras = crossings ?? new Vector4[0]; }

        public void Configure(Line[] newLines, GameObject[] prefabs, Material[] paints, Transform playerCar)
        {
            lines = newLines; vehiclePrefabs = prefabs; paintMaterials = paints; player = playerCar;
        }

        private sealed class Car
        {
            public Transform T;
            public CarModel Model;
            public int Line;
            public float S, Speed, Factor, HeldFor, IgnoreUntil, Lane;
            public Vector3 Pos, Forward;
        }

        private readonly List<Car> cars = new List<Car>();
        private float[][] cum;
        private float[] total;
        private Vector3 playerPrev, playerVelocity;
        private int heldFrames, passThroughFrames;
        private float minGap = float.MaxValue;
        private string minGapInfo = "";
        private string nearPlayerInfo = "";
        private string nearList = "";
        private readonly HashSet<string> nearSeen = new HashSet<string>();
        public string NearList => nearList;

        public int CarCount => cars.Count;
        public string StatsLine() => $"loopTraffic[cars={cars.Count} carsHighNow={cars.Count(c => c.Pos.y > 12f)} lines={lines.Length} carFramesHeld={heldFrames} minCentreGap={(minGap > 1e6f ? -1f : minGap):0.0}m framesWithin2mOfPlayer={passThroughFrames}{nearPlayerInfo} crossings={crossings.Count} signalStops={(signalStops == null ? 0 : signalStops.Sum(x => x.Count))} carFramesAtRed={redWaits} carFramesKeepingAhead={keepAheadFrames} heldMost={string.Join(";", HeldWhere().OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + ":" + kv.Value))} heldHeads={string.Join(";", HeldWhere().Where(kv => !kv.Key.EndsWith("follow")).OrderByDescending(kv => kv.Value).Take(10).Select(kv => kv.Key + ":" + kv.Value))} minGapWhere={minGapInfo}]";

        private void Start()
        {
            if (lines == null || lines.Length == 0 || vehiclePrefabs == null || vehiclePrefabs.Length == 0) { enabled = false; return; }
            var random = new System.Random(seed);
            cum = new float[lines.Length][];
            total = new float[lines.Length];
            var used = new Dictionary<GameObject, int>();
            for (int l = 0; l < lines.Length; l++)
            {
                var p = lines[l].points;
                int n = p.Length;
                cum[l] = new float[n + 1];
                for (int i = 1; i < n; i++) cum[l][i] = cum[l][i - 1] + Vector3.Distance(p[i - 1], p[i]);
                cum[l][n] = cum[l][n - 1] + (lines[l].closed ? Vector3.Distance(p[n - 1], p[0]) : 0f);
                total[l] = cum[l][n];
                int count = Mathf.Max(0, lines[l].cars);
                for (int k = 0; k < count; k++)
                {
                    var prefab = Pick(random, used);
                    var go = Instantiate(prefab, transform);
                    go.name = lines[l].name + "-" + k + "-" + prefab.name;
                    var car = new Car { T = go.transform, Model = go.GetComponent<CarModel>(), Line = l, Factor = 0.86f + 0.2f * (float)random.NextDouble() };
                    car.S = total[l] * (k + 0.15f + 0.7f * (float)random.NextDouble()) / count;
                    car.Lane = LanePattern[k % LanePattern.Length];
                    if (car.Model != null && !car.Model.fixedPaint && paintMaterials.Length > 0)
                    {
                        int paint = random.Next(paintMaterials.Length);
                        if (car.Model.lightPaintOnly)
                            for (int tries = 0; tries < 12 && !IsLight(paintMaterials[paint]); tries++) paint = random.Next(paintMaterials.Length);
                        car.Model.SetPaint(paintMaterials[paint]);
                    }
                    if (car.Model != null) car.Model.SetBraking(false);
                    car.Speed = SpeedAt(l, car.S) * car.Factor;
                    Place(car);
                    cars.Add(car);
                }
            }
            if (player != null) playerPrev = player.position;
            FindCrossings();
            FindSignalStops();
        }

        // where each line runs over a zebra crossing: a stop line 6 m before it (SignalLights is not green: cars wait there)
        private List<float>[] signalStops;
        private int redWaits;
        private int keepAheadFrames;
        // frames a car stood still, by line, 50 m stretch and what held it (a number: no string made every frame)
        private readonly Dictionary<long, int> heldCounts = new Dictionary<long, int>();
        private static readonly string[] WhyNames = { "free", "follow", "block", "red", "cross", "player" };
        private const int WhyCar = 10;                // + the other car's line
        private Dictionary<string, int> HeldWhere()
        {
            var d = new Dictionary<string, int>();
            foreach (var kv in heldCounts)
            {
                int line = (int)(kv.Key >> 32), bucket = (int)((kv.Key >> 12) & 0xFFFFF), why = (int)(kv.Key & 0xFFF);
                string key = lines[line].name + "@" + (bucket * 50).ToString("0") + (why >= WhyCar ? "car:" + lines[why - WhyCar].name : WhyNames[why]);
                d[key] = d.TryGetValue(key, out var n) ? n + kv.Value : kv.Value;
            }
            return d;
        }
        // nothing further than this (along the line, or apart on the ground) can hold a car: the longest look ahead is
        // standGap + 1.6 s at MaxKeepAhead, about 51 m
        private const float FarEnough = 60f;
        private const float MaxKeepAhead = 27f;      // m/s (about 97 km/h): no faster to stay ahead of the player
        private const float LaneWidth = 3.25f;
        // the lane each car keeps where the line has room beside it, in lanes from the line (-1 = the lane by the median); fewer
        // in the line's own lane, the player's
        private static readonly float[] LanePattern = { -1f, 1f, 0f, 2f, -1f, 1f, 2f };

        // metres to the right of the line for a car keeping `lane` at `s`
        private float Lateral(int l, float lane, float s)
        {
            var wl = lines[l].wideL; var wr = lines[l].wideR;
            if (wl == null || wr == null || wl.Length == 0 || lane == 0f) return 0f;
            if (lines[l].closed) { s %= total[l]; if (s < 0f) s += total[l]; } else s = Mathf.Clamp(s, 0f, total[l]);
            int i = Mathf.Min(Segment(l, s), wl.Length - 1), j = (i + 1) % wl.Length;
            float len = Mathf.Max(1e-3f, cum[l][i + 1] - cum[l][i]);
            float t = Mathf.Clamp01((s - cum[l][i]) / len);
            float left = Mathf.Lerp(wl[i], wl[j], t), right = Mathf.Lerp(wr[i], wr[j], t);
            return Mathf.Clamp(lane, -left, right) * LaneWidth;
        }

        // two cars of one line: 2 = in the same lane now, 1 = their lanes join within 30 m ahead of the car in front (the one
        // behind lets it in), 0 = side by side in lanes of their own
        private int SameLane(Car car, Car other)
        {
            if (car.Lane == other.Lane || lines[car.Line].wideL == null || lines[car.Line].wideL.Length == 0) return 2;
            if (Mathf.Abs(Lateral(car.Line, car.Lane, car.S) - Lateral(car.Line, other.Lane, other.S)) < 2.4f) return 2;
            float ahead = other.S + 30f;
            return Mathf.Abs(Lateral(car.Line, car.Lane, ahead) - Lateral(car.Line, other.Lane, ahead)) < 2.4f ? 1 : 0;
        }

        private void FindSignalStops()
        {
            signalStops = new List<float>[lines.Length];
            for (int l = 0; l < lines.Length; l++)
            {
                signalStops[l] = new List<float>();
                var p = lines[l].points;
                foreach (var zb in zebras)
                {
                    var u = new Vector2(Mathf.Cos(zb.z), Mathf.Sin(zb.z));
                    float best = float.MaxValue; int at = -1;
                    for (int i = 0; i < p.Length; i++)
                    {
                        if (Mathf.Abs(p[i].y - streetY) > 1.5f) continue;     // the line passes under (an underpass) or over the crossing
                        var d = new Vector2(p[i].x - zb.x, p[i].z - zb.y);
                        float along = Mathf.Abs(Vector2.Dot(d, u)), side = Mathf.Abs(d.x * u.y - d.y * u.x);
                        if (side < zb.w + 0.5f && along < 4f && along < best) { best = along; at = i; }
                    }
                    if (at >= 0) signalStops[l].Add(cum[l][at] - 6f);
                }
            }
        }

        // the fastest this car may go for a signal that is not green: it stops at the stop line, unless on yellow it is too
        // close to stop
        private float SignalCap(Car car)
        {
            var phase = SignalLights.Phase(Time.time);
            if (phase == SignalLights.State.Green || signalStops == null) return float.MaxValue;
            float cap = float.MaxValue;
            foreach (var stop in signalStops[car.Line])
            {
                float ds = stop - car.S;
                if (lines[car.Line].closed && ds < -total[car.Line] * 0.5f) ds += total[car.Line];
                if (ds < -0.5f || ds > 14f + car.Speed * 2.2f) continue;
                if (phase == SignalLights.State.Yellow && ds < car.Speed * 1.1f) continue;
                cap = Mathf.Min(cap, Mathf.Max(0f, ds) * 0.7f);
            }
            return cap;
        }

        // Where an open line runs through a closed one (the fork, the junction): a stretch of the open line with a stop line
        // before it. Cars of the open line wait there while a car of a closed line, or the player, is near the stretch.
        private struct Crossing { public int Line; public float From, To; public Vector3 Centre; public float Reach; }
        private readonly List<Crossing> crossings = new List<Crossing>();

        private void FindCrossings()
        {
            for (int l = 0; l < lines.Length; l++)
            {
                if (lines[l].closed) continue;
                var p = lines[l].points;
                int start = -1;
                for (int i = 0; i <= p.Length; i++)
                {
                    bool near = false;
                    if (i < p.Length)
                        for (int m = 0; m < lines.Length && !near; m++)
                        {
                            if (!lines[m].closed) continue;
                            var qs = lines[m].points;
                            var dp = p[Mathf.Min(i + 1, p.Length - 1)] - p[Mathf.Max(i - 1, 0)]; dp.y = 0f; dp.Normalize();
                            for (int j = 0; j < qs.Length; j++)
                            {
                                var q = qs[j];
                                if (Mathf.Abs(q.y - p[i].y) >= 3f || (q.x - p[i].x) * (q.x - p[i].x) + (q.z - p[i].z) * (q.z - p[i].z) >= 3.6f * 3.6f) continue;
                                // lanes side by side (the oncoming lane next to a route's lane, both ways) are not a crossing
                                var dq = qs[(j + 1) % qs.Length] - qs[(j - 1 + qs.Length) % qs.Length]; dq.y = 0f; dq.Normalize();
                                if (Mathf.Abs(Vector3.Dot(dp, dq)) < 0.9f) { near = true; break; }
                            }
                        }
                    if (near && start < 0) start = i;
                    if (!near && start >= 0)
                    {
                        var centre = (p[start] + p[i - 1]) * 0.5f;
                        crossings.Add(new Crossing { Line = l, From = cum[l][start], To = cum[l][i - 1], Centre = centre, Reach = Vector3.Distance(p[start], p[i - 1]) * 0.5f + 33f });
                        start = -1;
                    }
                }
            }
        }

        // the fastest a car of an open line may go for the crossing in front of it
        private float CrossingCap(Car car, bool hasPlayer, Vector3 playerPos)
        {
            float cap = float.MaxValue;
            foreach (var c in crossings)
            {
                if (c.Line != car.Line) continue;
                float stop = c.From - 5f;
                if (car.S > stop + 1f || car.S < stop - 14f - car.Speed * 2f) continue;
                // the player: near the crossing, or on the way to it and there within 9 s (at 80 km/h the 33 m of before was a
                // second and a half: a car that had just set off across the ramp's lanes was still in them when the player
                // arrived). Up to 12 m higher counts: the player coming down the ramp is still well above the junction.
                bool busy = false;
                if (hasPlayer && playerPos.y - c.Centre.y > -3f && playerPos.y - c.Centre.y < 12f)
                {
                    Vector3 toCentre = c.Centre - playerPos; toCentre.y = 0f;
                    float away = toCentre.magnitude, halfLength = c.Reach - 33f;
                    Vector3 flatVelocity = playerVelocity; flatVelocity.y = 0f;
                    float closing = away > 0.1f ? Vector3.Dot(flatVelocity, toCentre) / away : 0f;
                    busy = away < c.Reach || (closing > 1f && away < halfLength + 15f + closing * 9f);
                }
                if (!busy)
                    foreach (var other in cars)
                    {
                        if (!lines[other.Line].closed) continue;
                        // only a car that is in the crossing or will reach it within a few seconds (one that has gone through
                        // does not count: with three lines of cars through each crossing it was never free)
                        Vector3 to = c.Centre - other.Pos; to.y = 0f;
                        float dist = to.magnitude;
                        if (dist > c.Reach) continue;
                        float ahead = Vector3.Dot(to, other.Forward);
                        float half = c.Reach - 33f;
                        if (other.Speed < 0.5f && dist >= half + 4f) continue;     // standing (at the red light before the fork): no hurry
                        if (dist < half + 4f || (ahead > 0f && ahead < half + 6f + other.Speed * 3.5f)) { busy = true; break; }
                    }
                if (busy) cap = Mathf.Min(cap, Mathf.Max(0f, stop - car.S) * 0.7f);
            }
            return cap;
        }

        private static bool IsLight(Material m) => m != null && (m.name.Contains("white") || m.name.Contains("silver"));

        // by weight, with each model's own limit (two Porters, a few trucks)
        private GameObject Pick(System.Random random, Dictionary<GameObject, int> used)
        {
            for (int tries = 0; tries < 24; tries++)
            {
                float sum = 0f;
                foreach (var p in vehiclePrefabs) sum += Weight(p);
                float at = (float)random.NextDouble() * sum;
                foreach (var p in vehiclePrefabs)
                {
                    at -= Weight(p);
                    if (at > 0f) continue;
                    var model = p.GetComponent<CarModel>();
                    used.TryGetValue(p, out int have);
                    int limit = model != null ? Mathf.Max(1, model.maxCount) * 2 : 99;
                    if (have >= limit) break;
                    used[p] = have + 1;
                    return p;
                }
            }
            return vehiclePrefabs[0];
        }

        private static float Weight(GameObject prefab)
        {
            var m = prefab.GetComponent<CarModel>();
            return m != null ? Mathf.Max(0.01f, m.weight) : 1f;
        }

        private int Segment(int l, float s)
        {
            var c = cum[l];
            int lo = 0, hi = c.Length - 2;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (c[mid] <= s) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        private float SpeedAt(int l, float s)
        {
            var v = lines[l].speed;
            return v[Mathf.Min(Segment(l, s), v.Length - 1)];
        }

        private void Place(Car car)
        {
            var p = lines[car.Line].points;
            int n = p.Length;
            int i = Segment(car.Line, car.S);
            Vector3 a = p[Mathf.Min(i, n - 1)], b = p[(i + 1) % n];
            if (i >= n - 1 && !lines[car.Line].closed) { a = p[n - 2]; b = p[n - 1]; i = n - 2; }
            float len = Mathf.Max(1e-3f, cum[car.Line][i + 1] - cum[car.Line][i]);
            float t = Mathf.Clamp01((car.S - cum[car.Line][i]) / len);
            car.Pos = Vector3.Lerp(a, b, t);
            // the heading eases from one piece of the line into the next (a car does not snap round at a point)
            Vector3 dir = (b - a).normalized;
            Vector3 next = (p[(i + 2) % n] - b).normalized;
            if (i + 2 >= n && !lines[car.Line].closed) next = dir;
            Vector3 prev = (a - p[(i - 1 + n) % n]).normalized;
            if (i == 0 && !lines[car.Line].closed) prev = dir;
            car.Forward = Vector3.Slerp(Vector3.Slerp(prev, dir, 0.5f + t * 0.5f), Vector3.Slerp(dir, next, t * 0.5f), t).normalized;
            if (car.Lane != 0f && lines[car.Line].wideL != null && lines[car.Line].wideL.Length > 0)
            {
                // in a lane of its own beside the line: moved across, and turned a little while it moves from one lane to the next
                float side = Lateral(car.Line, car.Lane, car.S);
                float slope = (Lateral(car.Line, car.Lane, car.S + 2f) - Lateral(car.Line, car.Lane, car.S - 2f)) / 4f;
                Vector3 right = new Vector3(car.Forward.z, 0f, -car.Forward.x).normalized;
                car.Pos += right * side;
                car.Forward = (car.Forward + right * slope).normalized;
            }
            car.T.SetPositionAndRotation(car.Pos, Quaternion.LookRotation(car.Forward, Vector3.up));
        }

        private static readonly Unity.Profiling.ProfilerMarker UpdateMarker = new Unity.Profiling.ProfilerMarker("LoopTraffic.Update");

        private void Update()
        {
            using (UpdateMarker.Auto()) Step();
        }

        private void Step()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            bool hasPlayer = player != null;
            Vector3 playerPos = hasPlayer ? player.position : Vector3.zero;
            if (hasPlayer)
            {
                playerVelocity = (playerPos - playerPrev) / dt;
                playerPrev = playerPos;
            }
            float now = Time.time;
            foreach (var car in cars)
            {
                float target = SpeedAt(car.Line, car.S) * car.Factor;
                bool keepAhead = false;
                if (hasPlayer)
                {
                    // a player coming up behind in this lane: speed up to stay ahead (to a limit; faster still, the player drives
                    // through, as nothing collides yet)
                    Vector3 pf = playerVelocity; pf.y = 0f;
                    float pv = pf.magnitude;
                    Vector3 d = car.Pos - playerPos;
                    if (pv > 2f && Mathf.Abs(d.y) < 3f)
                    {
                        pf /= pv; d.y = 0f;
                        float along = Vector3.Dot(d, pf), side = Mathf.Abs(d.x * pf.z - d.z * pf.x);
                        if (along > 0f && along < 35f && side < 2.2f)
                        {
                            float want = Mathf.Min(pv * 1.08f + (35f - along) * 0.12f, MaxKeepAhead);
                            if (want > target) { target = want; keepAhead = true; keepAheadFrames++; }
                        }
                    }
                }
                float limit = target;
                bool heldByCar = false;
                int why = 0;
                float aheadDs = float.MaxValue;
                foreach (var other in cars)
                {
                    if (other == car) continue;
                    if (other.Line == car.Line)
                    {
                        // the car ahead on the same line, by distance along the line: never ignored (a queue at a stop line
                        // lasts longer than the give-way below, and the follower drove into the car in front)
                        float ds = other.S - car.S;
                        if (lines[car.Line].closed && ds < 0f) ds += total[car.Line];
                        if (ds < 0f || ds > FarEnough) continue;  // behind, or too far ahead to matter
                        int same = SameLane(car, other);
                        if (same == 0) continue;               // in the next lane over (the bridge): passes or is passed
                        if (same == 2 && ds >= 0f && ds < aheadDs && !(ds == 0f && other.GetHashCode() > car.GetHashCode())) aheadDs = ds;
                        float cap = FollowCap(car, ds, other.Speed);
                        if (cap < limit) { limit = cap; why = 1; }
                        continue;
                    }
                    float gx = other.Pos.x - car.Pos.x, gz = other.Pos.z - car.Pos.z;
                    if (gx * gx + gz * gz > FarEnough * FarEnough) continue;
                    // one of them under the road (the oncoming line's underpasses), or on the bridge above: nothing to each other
                    if (Mathf.Abs(other.Pos.y - car.Pos.y) > 3f) continue;
                    // a car already in a crossing goes on through it: the cars of the other lines wait for it (each waiting for
                    // the other locked the fork)
                    else if (!lines[car.Line].closed && lines[other.Line].closed && InCrossing(car)) { }
                    // the through traffic (closed lines) does not give way to the oncoming line where it crosses; the oncoming cars
                    // wait for a gap at their stop line instead (giving way both ways had the bridge-bound cars queue at the fork)
                    else if (lines[car.Line].closed && !lines[other.Line].closed)
                    {
                        if (Blocking(car, other.Pos) && !Blocking(other, car.Pos)) { limit = 0f; why = 2; }
                    }
                    else if (now >= car.IgnoreUntil)
                    {
                        // a car of another line going the same way is in this car's lane only if nearly in line with it (lanes are
                        // 3.25 m apart, and the crossing width held cars side by side in the next lane); one that crosses is wider
                        float half = Vector3.Dot(car.Forward, other.Forward) > 0.94f ? laneHalf : crossHalf;
                        float cap = CapFor(car, other.Pos, other.Forward * other.Speed, half);
                        // where two lines merge side by side (both routes into Seomyeon), the later line gives way to a car level with it
                        if (car.Line > other.Line && AlongsideClose(car, other.Pos)) cap = Mathf.Min(cap, other.Speed * 0.6f);
                        if (cap < limit) { limit = cap; heldByCar = true; why = WhyCar + other.Line; }
                    }
                    // (two cars each right in front of the other, as a crossing locks up: the one of the lower line goes first)
                    else if (Blocking(car, other.Pos) && !(car.Line < other.Line && Blocking(other, car.Pos)))
                    { limit = 0f; why = 2; }             // giving up a give-way never means driving into the car right in front
                }
                float red = SignalCap(car);
                if (red < limit) { limit = red; heldByCar = false; why = 3; if (red < 0.2f) redWaits++; }
                if (!lines[car.Line].closed)
                {
                    float wait = CrossingCap(car, hasPlayer, playerPos);
                    if (wait < limit) { limit = wait; heldByCar = false; why = 4; }
                }
                if (hasPlayer)
                {
                    float cap = CapFor(car, playerPos, playerVelocity, laneHalf + 0.4f);
                    if (cap < limit) { limit = cap; heldByCar = false; why = 5; }
                    if ((playerPos - car.Pos).sqrMagnitude < 4f)
                    {
                        passThroughFrames++;
                        if (nearPlayerInfo.Length == 0) nearPlayerInfo = $" firstNear={car.T.name} s={car.S:0} at ({car.Pos.x:0},{car.Pos.z:0}) speed={car.Speed:0.0} t={now:0.0}";
                        // every car that came this near, once each, with the angle between its heading and the player's (a car
                        // of the player's own lane is near 0, one that crosses or comes the other way is not)
                        if (nearSeen.Add(car.T.name) && nearSeen.Count <= 14)
                        {
                            Vector3 pv2 = playerVelocity; pv2.y = 0f;
                            nearList += $" near:{car.T.name}@({car.Pos.x:0},{car.Pos.z:0})angle={(pv2.sqrMagnitude > 0.01f ? Vector3.Angle(pv2, car.Forward) : -1f):0}";
                        }
                    }
                }
                float before = car.Speed;
                car.Speed = limit < car.Speed ? Mathf.Max(limit, car.Speed - 7f * dt) : Mathf.Min(limit, car.Speed + (keepAhead ? 4f : 2.2f) * dt);
                if (car.Model != null) car.Model.SetBraking(car.Speed < before - 0.5f * dt || car.Speed < 0.3f);
                if (car.Speed < 0.2f) { heldFrames++; car.HeldFor += dt; } else car.HeldFor = 0f;
                if (car.Speed < 0.2f)
                {
                    long key = ((long)car.Line << 32) | ((long)Mathf.RoundToInt(car.S / 50f) << 12) | (long)why;
                    heldCounts[key] = heldCounts.TryGetValue(key, out var n) ? n + 1 : 1;
                }
                // two cars each waiting for the other (lines that cross): after a while one of them goes
                if (car.HeldFor > 5f && heldByCar) { car.IgnoreUntil = now + 3f; car.HeldFor = 0f; }
                car.S += car.Speed * dt;
                if (aheadDs < float.MaxValue)              // never onto or past the car in front on the same line
                    car.S = Mathf.Min(car.S, car.S - car.Speed * dt + Mathf.Max(0f, aheadDs - standGap * 0.6f));
                if (car.S >= total[car.Line])
                {
                    if (lines[car.Line].closed) car.S -= total[car.Line];
                    else if (StartFree(car)) { car.S = 0f; car.Speed = SpeedAt(car.Line, 0f) * car.Factor; }
                    else { car.S = total[car.Line]; car.Speed = 0f; }      // waits at the end, inside the tunnel mouth, until the start is clear
                }
                Place(car);
            }
            for (int i = 0; i < cars.Count; i++)
                for (int j = i + 1; j < cars.Count; j++)
                {
                    float d2 = (cars[i].Pos - cars[j].Pos).sqrMagnitude;
                    if (d2 < minGap * minGap) { minGap = Mathf.Sqrt(d2); minGapInfo = $"{cars[i].T.name} s={cars[i].S:0} and {cars[j].T.name} s={cars[j].S:0} at ({cars[i].Pos.x:0},{cars[i].Pos.z:0})"; }
                }
        }

        private bool StartFree(Car car)
        {
            foreach (var other in cars)
                if (other != car && other.Line == car.Line && other.S < 22f) return false;
            return true;
        }

        private bool InCrossing(Car car)
        {
            foreach (var c in crossings)
                if (c.Line == car.Line && car.S > c.From - 5.5f && car.S < c.To + 4f) return true;
            return false;
        }

        private bool AlongsideClose(Car car, Vector3 pos)
        {
            Vector3 d = pos - car.Pos; d.y = 0f;
            Vector3 f = car.Forward; f.y = 0f; f.Normalize();
            float along = Vector3.Dot(d, f);
            return along > -0.5f && along <= 0.5f && Mathf.Abs(d.x * f.z - d.z * f.x) < 2.0f;     // level with it, not behind
        }

        // something standing right in front of this car's nose (within 4.5 m ahead, 1.8 m to either side)
        private bool Blocking(Car car, Vector3 pos)
        {
            Vector3 d = pos - car.Pos; d.y = 0f;
            Vector3 f = car.Forward; f.y = 0f; f.Normalize();
            float along = Vector3.Dot(d, f);
            return along > 0.5f && along < 4.5f && Mathf.Abs(d.x * f.z - d.z * f.x) < 1.8f;
        }

        // the fastest this car may go behind a car `ds` metres ahead along the same line, moving at `theirs`
        private float FollowCap(Car car, float ds, float theirs)
        {
            float reach = standGap + car.Speed * 1.6f;
            if (ds < 0f || ds > reach) return float.MaxValue;
            if (ds <= standGap) return 0f;
            return Mathf.Max(0f, theirs) + (ds - standGap) * 0.7f;
        }

        // the fastest this car may go for something at `pos` moving with `velocity`: no limit unless it is in front, in this lane
        private float CapFor(Car car, Vector3 pos, Vector3 velocity, float half)
        {
            Vector3 d = pos - car.Pos;
            if (Mathf.Abs(d.y) > 3f) return float.MaxValue;
            d.y = 0f;
            Vector3 f = car.Forward; f.y = 0f; f.Normalize();
            float along = Vector3.Dot(d, f);
            float reach = standGap + car.Speed * 1.6f;
            if (along <= 0.5f || along > reach) return float.MaxValue;
            float side = Mathf.Abs(d.x * f.z - d.z * f.x);
            if (side > half) return float.MaxValue;
            if (along <= standGap) return 0f;
            float theirs = Mathf.Max(0f, Vector3.Dot(velocity, f));
            return theirs + (along - standGap) * 0.7f;
        }
    }
}
