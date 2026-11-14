using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    public static partial class BusanSliceBuilder
    {
        // Gwangan Bridge. Real main span is 500 m between towers with 200 m side spans; the deck
        // is 24 m wide. Tower height above the deck (50 m) and the viaduct pier layout are design
        // values, not survey data.
        const float TowerAboveDeck = 50f;

        static float CourseYAt(float s)
        {
            int lo = 0, hi = ccum.Length - 1;
            while (lo + 1 < hi) { int mid = (lo + hi) / 2; if (ccum[mid] <= s) lo = mid; else hi = mid; }
            float t = Mathf.InverseLerp(ccum[lo], ccum[hi], s);
            return Mathf.Lerp(cp[lo].y, cp[hi].y, t);
        }

        static Vector3 CoursePosAt(float s)
        {
            int lo = 0, hi = ccum.Length - 1;
            while (lo + 1 < hi) { int mid = (lo + hi) / 2; if (ccum[mid] <= s) lo = mid; else hi = mid; }
            return Vector3.Lerp(cp[lo], cp[hi], Mathf.InverseLerp(ccum[lo], ccum[hi], s));
        }

        static void Prism(MeshBuilder b, Vector3 center, float yBottom, float yTop, float radius, int sides)
        {
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(center.x + Mathf.Cos(a0) * radius, 0f, center.z + Mathf.Sin(a0) * radius);
                var p1 = new Vector3(center.x + Mathf.Cos(a1) * radius, 0f, center.z + Mathf.Sin(a1) * radius);
                var n = ((p0 + p1) * 0.5f - new Vector3(center.x, 0f, center.z)).normalized;
                b.Quad(new Vector3(p0.x, yBottom, p0.z), new Vector3(p1.x, yBottom, p1.z), new Vector3(p1.x, yTop, p1.z), new Vector3(p0.x, yTop, p0.z), n,
                    new Vector2((float)k / sides * 4f, 0f), new Vector2((float)(k + 1) / sides * 4f, 0f), new Vector2((float)(k + 1) / sides * 4f, (yTop - yBottom) / 4f), new Vector2((float)k / sides * 4f, (yTop - yBottom) / 4f));
            }
        }

        // Frustum with rectangular sections; width across the bridge, depth along it.
        static void Frustum(MeshBuilder b, Vector3 center, float yBottom, float w0, float d0, float yTop, float w1, float d1, Vector3 along, Vector3 across)
        {
            Vector3 C(float y, float w, float d, float sw, float sd) => new Vector3(center.x, y, center.z) + across * (sw * w * 0.5f) + along * (sd * d * 0.5f);
            var corners = new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) };
            for (int k = 0; k < 4; k++)
            {
                var (sw0, sd0) = corners[k]; var (sw1, sd1) = corners[(k + 1) % 4];
                var n = (across * (sw0 + sw1) + along * (sd0 + sd1)).normalized;
                b.Quad(C(yBottom, w0, d0, sw0, sd0), C(yBottom, w0, d0, sw1, sd1), C(yTop, w1, d1, sw1, sd1), C(yTop, w1, d1, sw0, sd0), n);
            }
            b.Quad(C(yTop, w1, d1, -1, -1), C(yTop, w1, d1, 1, -1), C(yTop, w1, d1, 1, 1), C(yTop, w1, d1, -1, 1), Vector3.up);
        }

        static void CableLine(MeshBuilder b, List<Vector3> pts, float width)
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var t = pts[i + 1] - pts[i];
                var h = Right(t) * (width * 0.5f);
                var v = Vector3.up * (width * 0.5f);
                b.QuadBoth(pts[i] - h, pts[i] + h, pts[i + 1] + h, pts[i + 1] - h, Vector3.up);
                b.QuadBoth(pts[i] - v, pts[i] + v, pts[i + 1] + v, pts[i + 1] - v, h.normalized);
            }
        }

        static void BuildBridge()
        {
            BuildPiers();
            BuildSuspensionSpan();
        }

        static void BuildPiers()
        {
            var cap = sink.Single("Piers", "bridge");
            float next = 0f;
            int count = 0;
            for (int i = 1; i < cp.Length - 1; i++)
            {
                bool deck = ckind[i] == Kbridge || (ckind[i] == Kapproach && celev[i]);
                if (!deck || InSpan(i) || ccum[i] < next) continue;
                next = ccum[i] + (ckind[i] == Kbridge ? 85f : 65f);
                var pos = cp[i];
                float yUnder = pos.y - 2.2f;
                float floor = GroundClass(pos.x, pos.z) == 1 ? -3.5f : GroundH(pos.x, pos.z) - 0.4f;
                var along = new Vector3(-cr[i].z, 0f, cr[i].x);
                if (yUnder - floor < 3f) continue;
                if (ckind[i] == Kbridge)
                {
                    float lat = chw[i] * 0.5f + 0.3f;
                    foreach (var s in new[] { -1f, 1f })
                        Prism(cap, pos + cr[i] * (s * lat), floor, yUnder - 1.8f, 1.55f, 10);
                    cap.Box(new Vector3(pos.x, yUnder - 0.9f, pos.z), new Vector3(lat * 2f + 3.6f, 1.8f, 2.8f), Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg);
                }
                else
                {
                    Prism(cap, pos, floor, yUnder - 1.3f, 1.2f, 10);
                    cap.Box(new Vector3(pos.x, yUnder - 0.65f, pos.z), new Vector3(5.4f, 1.3f, 2.2f), Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg);
                }
                count++;
            }
            Log($"BRIDGE piers={count}");
        }

        static void BuildSuspensionSpan()
        {
            var t0 = route.towers[0]; var t1 = route.towers[1];
            var p0 = new Vector3(t0.x, t0.y, t0.z); var p1 = new Vector3(t1.x, t1.y, t1.z);
            var dir = p1 - p0; dir.y = 0f;
            float span = dir.magnitude; dir.Normalize();
            var side = Right(dir);
            float deck = (t0.y + t1.y) * 0.5f;
            float topY = deck + TowerAboveDeck;
            float legLat = 13.4f;
            var steel = sink.Single("Towers", "bridge");
            var concrete = sink.Single("TowerBase", "bridge");

            foreach (var p in new[] { p0, p1 })
            {
                foreach (var s in new[] { -1f, 1f })
                    Frustum(steel, p + side * (s * legLat), -6f, 6.2f * 0.8f, 8.4f, topY, 3.0f, 5.2f, dir, side);
                foreach (var by in new[] { 29f, 46f })
                    steel.Box(new Vector3(p.x, deck + by, p.z), new Vector3(2f * legLat - 3.2f, 3.6f, 4.2f), Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
                // pier block from the sea floor up to the lower deck
                concrete.Box(new Vector3(p.x, (-6f + deck - 7.8f) * 0.5f, p.z), new Vector3(2f * legLat + 6.4f, deck - 7.8f + 6f, 10f), Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
            }

            // main cables, side-span cables and hangers (both sides)
            var cables = sink.Single("Cables", "cable");
            var outriggers = sink.Single("Outriggers", "bridge");
            float anchorA = -200f, anchorB = span + 212f;
            Func<float, float> cableY = u =>
            {
                if (u < 0f) { float w = u / anchorA; return Mathf.Lerp(topY, deck + 3f, w) - 3f * 4f * w * (1f - w); }
                if (u > span) { float w = (u - span) / (anchorB - span); return Mathf.Lerp(topY, deck + 3f, w) - 3f * 4f * w * (1f - w); }
                float t = u / span;
                return topY - 4f * (topY - (deck + 4f)) * t * (1f - t);
            };
            foreach (var s in new[] { -1f, 1f })
            {
                var pts = new List<Vector3>();
                for (float u = anchorA; u <= anchorB + 0.01f; u += (u < 0f || u > span) ? 10f : 8f)
                    pts.Add(p0 + dir * u + side * (s * legLat) + Vector3.up * (cableY(u) - p0.y));
                // make sure the tower tops are sampled exactly
                CableLine(cables, pts, 0.7f);
                for (float u = anchorA + 6f; u < anchorB - 4f; u += 12.5f)
                {
                    float y = cableY(u);
                    float deckY = CourseYAt(Mathf.Clamp(t0.s + u, 0f, ccum[ccum.Length - 1]));
                    var top = p0 + dir * u + side * (s * legLat); top.y = y;
                    var bottom = top; bottom.y = deckY - 1.6f;
                    if (top.y - bottom.y < 2f) continue;
                    var h = side * 0.09f; var f = dir * 0.09f;
                    cables.QuadBoth(bottom - h, bottom + h, top + h, top - h, dir);
                    cables.QuadBoth(bottom - f, bottom + f, top + f, top - f, side);
                    outriggers.Box(new Vector3(top.x, deckY - 1.6f, top.z) - side * (s * 1.3f), new Vector3(2.6f, 0.4f, 0.45f), Mathf.Atan2(side.x, side.z) * Mathf.Rad2Deg);
                }
            }

            // double-deck truss band under the roadway: X cut-outs between upper and lower deck
            for (int i = 0; i < cp.Length - 1; i++)
            {
                if (!InSpan(i) || !InSpan(i + 1)) continue;
                float lat0 = csh[i] - 0.4f, lat1 = csh[i + 1] - 0.4f;
                float u0 = ccum[i] / 12.5f, u1 = ccum[i + 1] / 12.5f;
                foreach (var s in new[] { -1f, 1f })
                {
                    var a = cp[i] + cr[i] * (s * lat0); var b = cp[i + 1] + cr[i + 1] * (s * lat1);
                    var tb = sink.B("Truss", "truss", a);
                    tb.QuadBothUV(a + Vector3.down * 2.2f, b + Vector3.down * 2.2f, b + Vector3.down * 7.0f, a + Vector3.down * 7.0f, cr[i] * s,
                        new Vector2(u0, 1f), new Vector2(u1, 1f), new Vector2(u1, 0f), new Vector2(u0, 0f));
                }
                var l0 = cp[i] - cr[i] * lat0 + Vector3.down * 7.0f; var r0 = cp[i] + cr[i] * lat0 + Vector3.down * 7.0f;
                var l1 = cp[i + 1] - cr[i + 1] * lat1 + Vector3.down * 7.0f; var r1 = cp[i + 1] + cr[i + 1] * lat1 + Vector3.down * 7.0f;
                var lb = sink.B("LowerDeck", "bridge", l0);
                lb.Quad(l0, r0, r1, l1, Vector3.up);
                lb.Quad(l0 + Vector3.down * 0.6f, r0 + Vector3.down * 0.6f, r1 + Vector3.down * 0.6f, l1 + Vector3.down * 0.6f, Vector3.down);
            }
            var lowerEdge = new float[cp.Length];
            for (int i = 0; i < cp.Length; i++) lowerEdge[i] = csh[i] - 0.4f;
            foreach (var s in new[] { -1f, 1f })
                Sweep(sink.Single("LowerDeck", "bridge"), cp, cr, lowerEdge, s, new[] { new Vector2(0f, -7.0f), new Vector2(0f, -7.6f) }, i => InSpan(i), 3f);
            Log($"BRIDGE span={span:0}m towerSpacing={Vector3.Distance(p0, p1):0}m deckY={deck:0.0}");
        }

        // ---------- parked cars: beach road (building side), residential streets and alleys (road edge), never in a lane ----------

        const int ParkedMax = 70;

        static Vector3 PolylinePoint(List<float> p, float s, out Vector3 tangent)
        {
            int cnt = p.Count / 3;
            float acc = 0f;
            for (int i = 0; i < cnt - 1; i++)
            {
                var a = new Vector3(p[i * 3], p[i * 3 + 1], p[i * 3 + 2]); var b = new Vector3(p[i * 3 + 3], p[i * 3 + 4], p[i * 3 + 5]);
                float seg = Vector3.Distance(a, b);
                if (acc + seg >= s || i == cnt - 2)
                {
                    float t = seg > 1e-4f ? Mathf.Clamp01((s - acc) / seg) : 0f;
                    tangent = (b - a); tangent.y = 0f; tangent.Normalize();
                    return Vector3.Lerp(a, b, t);
                }
                acc += seg;
            }
            tangent = Vector3.forward;
            return Vector3.zero;
        }

        static void BuildParkedCars(Transform root)
        {
            var parent = new GameObject("Parked cars").transform;
            parent.SetParent(root, false);
            if (bakedCars.Count == 0) return;
            var models = bakedCars.Select(c => c.GetComponent<CarModel>()).ToArray();
            var r = new System.Random(1212);
            var placedPerModel = new int[bakedCars.Count];
            long tris = 0;
            int beach = 0, residential = 0, alley = 0;
            var batches = new HashSet<(int model, Material paint)>();
            Func<int> placedTotal = () => beach + residential + alley;
            
            Func<int, bool> available = i => models[i].maxCount > 3 || placedPerModel[i] < Mathf.Max(1, models[i].maxCount - 1);
            // heavy models are drawn less often: weight / (triangles / 25k), kept within 0.25..1.3; Porter and box truck at most 1 and 2
            Func<int, float> parkedWeight = i => models[i].weight * Mathf.Clamp(25000f / Mathf.Max(1, models[i].triangles), 0.25f, 1.3f);
            Physics.SyncTransforms();
            int settled = 0, shifted = 0, rejected = 0;
            Func<Vector3, Vector3, int, bool> park = (pos, forward, forceModel) =>
            {
                // with the SUMO network the roads are narrower than the old ribbons the spots were measured on: the car is moved
                // sideways (up to 3 m) until all four wheels stand on the carriageway, and set on its surface; otherwise it is not placed
                if (UseSumo)
                {
                    forward.y = 0f; forward.Normalize();
                    var side = Right(forward);
                    bool ok = false;
                    foreach (float s in new[] { 0f, 0.5f, -0.5f, 1f, -1f, 1.5f, -1.5f, 2f, -2f, 2.5f, -2.5f, 3f, -3f })
                    {
                        float lo = float.MaxValue, hi = float.MinValue; bool all = true;
                        foreach (var (a, b) in new[] { (0.9f, 2.2f), (-0.9f, 2.2f), (0.9f, -2.2f), (-0.9f, -2.2f) })
                        {
                            var c = pos + side * (s + a) + forward * b;
                            if (!Physics.Raycast(c + Vector3.up * 2.5f, Vector3.down, out var hit, 6f, 1 << RoadLayer)) { all = false; break; }
                            lo = Mathf.Min(lo, hit.point.y); hi = Mathf.Max(hi, hit.point.y);
                        }
                        if (!all || hi - lo > 0.35f) continue;
                        pos = pos + side * s; pos.y = (lo + hi) * 0.5f;
                        if (s != 0f) shifted++;
                        ok = true; break;
                    }
                    if (!ok) { rejected++; return false; }
                    settled++;
                }
                float total = 0f;
                for (int i = 0; i < models.Length; i++) if (available(i)) total += parkedWeight(i);
                float pick = (float)r.NextDouble() * total;
                int index = 0;
                for (int i = 0; i < models.Length; i++)
                {
                    if (!available(i)) continue;
                    pick -= parkedWeight(i);
                    if (pick <= 0f) { index = i; break; }
                }
                if (forceModel >= 0) index = forceModel;
                placedPerModel[index]++;
                var paint = models[index].fixedPaint ? paintMaterials[0] : PickPaint(r, models[index].lightPaintOnly);
                forward.y = 0f;
                var car = (GameObject)PrefabUtility.InstantiatePrefab(bakedCars[index], parent);
                car.transform.SetPositionAndRotation(pos + Vector3.up * (UseSumo ? 0f : 0.02f), Quaternion.LookRotation(forward) * Quaternion.Euler(0f, ((float)r.NextDouble() - 0.5f) * 4f, 0f));
                car.GetComponent<CarModel>().SetPaint(paint);
                if (models[index].modelName == "BoxTruck" || models[index].modelName == "PorterII")
                    Log($"PARKEDTRUCK {models[index].modelName} at {pos.x:0.0} {pos.y:0.00} {pos.z:0.0} fwd {forward.x:0.000} {forward.z:0.000}");
                tris += models[index].triangles;
                batches.Add((index, paint));
                return true;
            };

            // beach road: building side of the course
            float next = 10f;
            for (int i = 2; i < cp.Length - 2 && beach < 22; i++)
            {
                if (ckind[i] != Kbeach || celev[i] || ccum[i] < next) continue;
                float s = SeaSide(i, 1f) ? -1f : 1f;
                var pos = cp[i] + cr[i] * (s * (csh[i] - 1.15f));
                if (NearMouth(pos, 5f)) continue;
                next = ccum[i] + 18f + (float)r.NextDouble() * 55f;
                int run = 1 + r.Next(3);
                for (int k = 0; k < run && beach < 22; k++)
                {
                    int j = Mathf.Min(i + k, cp.Length - 2);
                    var q = cp[j] + cr[j] * (s * (csh[j] - 1.15f));
                    var tangent = cp[j + 1] - cp[j]; tangent.y = 0f;
                    if (park(q + tangent.normalized * (k * 5.2f), s > 0f ? tangent : -tangent, -1)) beach++;
                }
            }

            // keep clear of junctions, mouths, closures and road ends (15 m)
            var avoid = new List<Vector3>();
            foreach (var j in net.junctions) avoid.Add(new Vector3(j.x, j.y, j.z));
            foreach (var m in mouths) avoid.Add(m.pos);
            if (net.closures != null) foreach (var c in net.closures) avoid.Add(new Vector3(c.x, c.y, c.z));
            foreach (var road in net.roads)
            {
                int cnt = road.p.Count / 3;
                avoid.Add(new Vector3(road.p[0], road.p[1], road.p[2]));
                avoid.Add(new Vector3(road.p[(cnt - 1) * 3], road.p[(cnt - 1) * 3 + 1], road.p[(cnt - 1) * 3 + 2]));
            }
            var avoidGrid = new PointGrid(avoid.ToArray(), 30f);

            var order = Enumerable.Range(0, net.roads.Count).OrderBy(i => (uint)(i * 2654435761u) % 1000003u).ToList();
            // Guaranteed Porter II (CarModel.minCount, parked side): one in an alley and one on a residential street, within 150 m of the course
            // so a driver passes them; placed before the random pass, which keeps 10 m away from them.
            var reserved = new List<Vector3>();
            int porterIndex = Array.FindIndex(models, m => m.minCount > 0);
            if (porterIndex >= 0)
                foreach (bool wantAlley in new[] { true, false })
                {
                    bool done = false;
                    foreach (int ri in order)
                    {
                        var road = net.roads[ri];
                        if (wantAlley ? (road.cls != 6 || road.w < 4.6f) : !(road.cls == 3 || road.cls == 4)) continue;
                        int cnt = road.p.Count / 3;
                        float len = 0f;
                        for (int i = 0; i < cnt - 1; i++) len += Vector3.Distance(new Vector3(road.p[i * 3], 0f, road.p[i * 3 + 2]), new Vector3(road.p[i * 3 + 3], 0f, road.p[i * 3 + 5]));
                        if (len < 32f) continue;
                        for (float sd = 15f; sd < len - 15f && !done; sd += 10f)
                        {
                            var pos0 = PolylinePoint(road.p, sd, out var tan);
                            int ci = courseGrid.Nearest(pos0, 150f, out var dCourse);
                            if (ci < 0 || dCourse < chw[ci] + 9f || dCourse > 150f || avoidGrid.Any(pos0, 15f)) continue;
                            var rt = Right(tan);
                            float side = 1f;
                            var at = pos0 + rt * (side * (road.w * 0.5f - 1.0f));
                            if (!park(at, tan, porterIndex)) continue;   // no carriageway under all four wheels here: try further along
                            reserved.Add(pos0);
                            if (wantAlley) alley++; else residential++;
                            Log($"PARKED PorterII {(wantAlley ? "alley" : "residential")} at {at.ToString("0.0")} road={ri} cls={road.cls} width={road.w:0.0} course distance={dCourse:0}m");
                            done = true;
                        }
                        if (done) break;
                    }
                }
            foreach (int ri in order)
            {
                var road = net.roads[ri];
                bool isAlley = road.cls == 6;
                if (!(road.cls == 3 || road.cls == 4 || isAlley)) continue;
                if (isAlley && road.w < 4.6f) continue;
                if (placedTotal() >= ParkedMax) break;
                if (isAlley ? alley >= 28 : residential >= 20) continue;
                int cnt = road.p.Count / 3;
                float len = 0f;
                for (int i = 0; i < cnt - 1; i++) len += Vector3.Distance(new Vector3(road.p[i * 3], 0f, road.p[i * 3 + 2]), new Vector3(road.p[i * 3 + 3], 0f, road.p[i * 3 + 5]));
                if (len < 32f) continue;
                float sd = 15f + (float)r.NextDouble() * 10f;
                int onRoad = 0;
                while (sd < len - 15f && onRoad < 2 && placedTotal() < ParkedMax)
                {
                    var pos0 = PolylinePoint(road.p, sd, out var tan);
                    sd += 22f + (float)r.NextDouble() * 30f;
                    int ci = courseGrid.Nearest(pos0, 540f, out var dCourse);
                    if (ci < 0 || dCourse < chw[ci] + 9f) continue;
                    if (avoidGrid.Any(pos0, 15f)) continue;
                    if (reserved.Any(q => (q - pos0).sqrMagnitude < 100f)) continue;
                    var rt = Right(tan);
                    float side = r.NextDouble() < 0.5 ? 1f : -1f;
                    if (!park(pos0 + rt * (side * (road.w * 0.5f - 1.0f)), side > 0f ? tan : -tan, -1)) continue;
                    if (isAlley) alley++; else residential++;
                    onRoad++;
                }
            }
            if (UseSumo) Log($"PARKEDSURFACE settledOnCarriageway={settled} movedSideways={shifted} notPlaced(no carriageway under all wheels)={rejected}");
            Log($"PARKED beach={beach} residential={residential} alleys={alley} total={placedTotal()} (cap {ParkedMax}) triangles={tris} renderers={placedTotal()} instancedBatches={batches.Count} models=" +
                string.Join(",", Enumerable.Range(0, models.Length).Select(i => models[i].modelName + ":" + placedPerModel[i])) + $" PorterII>={Mathf.Max(1, models.Where(m => m.minCount > 0).Select(m => m.minCount).DefaultIfEmpty(0).Max())} check={(porterIndex >= 0 && placedPerModel[porterIndex] >= models[porterIndex].minCount ? "ok" : "FAIL")}");
        }
    }
}
