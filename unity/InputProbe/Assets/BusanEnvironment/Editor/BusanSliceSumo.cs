using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // Road network from SUMO netconvert (tools/build-busan-sumo-net.py, tools/export-busan-sumo-geometry.py): lanes and junction areas
    // with rounded corners, raised sidewalks with kerbs, lane paint, zebra crossings, stop lines and signal heads with their programs.
    // When BusanSumoGeometry.json is present it replaces the ground-level course ribbon, the side-street ribbons, the junction wedges
    // and the old signal and crossing pass; the elevated course (viaducts, bridge) is still built by BuildCourseSurface.
    public static partial class BusanSliceBuilder
    {
        [Serializable] class SumoMesh { public string cat, mat; public int cx, cz; public float[] v; public int[] t; }
        [Serializable] class SumoLine { public string kind; public float[] p; }
        [Serializable] class SumoHead { public float x, y, z, dx, dz, fx, fz, arm, px, py, pz, ux, uz, half; public int prog; public int[] s, l, r; }
        [Serializable] class SumoProgram { public string id; public float offset; public float[] dur; public string[] state; }
        [Serializable] class SumoPoly { public float[] p; }
        [Serializable] class SumoCrossingEnd { public float x, y, z, ux, uz, w; }
        [Serializable] class SumoClosure { public float x, y, z, dx, dz, w; }
        [Serializable] class SumoGeometry { public List<SumoMesh> meshes; public List<SumoLine> lines; public List<SumoHead> heads; public List<SumoProgram> programs; public List<SumoPoly> junctions; public List<SumoLine> lanes; public List<SumoCrossingEnd> crossings; public List<SumoClosure> closures; }

        // Road wear along the lane centre lines: cast-iron manhole covers (0.65 m radius) every 35-75 m and patched asphalt (darker,
        // older surface) every 70-200 m, a little off the wheel tracks. Flat, 1.5 cm above the asphalt, under the paint.
        static void BuildRoadWear()
        {
            if (sumo.lanes == null) return;
            var rnd = new System.Random(4071);
            int covers = 0, patches = 0;
            foreach (var lane in sumo.lanes)
            {
                int n = lane.p.Length / 3;
                if (n < 2) continue;
                float nextCover = 15f + (float)rnd.NextDouble() * 40f, nextPatch = 30f + (float)rnd.NextDouble() * 120f, acc = 0f;
                for (int i = 0; i < n - 1; i++)
                {
                    var a = V(lane.p, i); var b = V(lane.p, i + 1);
                    float len = Vector3.Distance(Flat(a), Flat(b));
                    if (len < 0.01f) continue;
                    var u = Flat(b - a).normalized; var r = Right(u);
                    while (nextCover < acc + len)
                    {
                        var c = Vector3.Lerp(a, b, (nextCover - acc) / len) + r * (((float)rnd.NextDouble() - 0.5f) * 1.2f);
                        nextCover += 35f + (float)rnd.NextDouble() * 40f;
                        if (InSumoJunction(c, 1f) || !sumoRoadArea.Contains(c.x, c.z)) continue;
                        var mb = sink.B("RoadWear", "manhole", c);
                        float y = c.y + 0.015f;
                        const int seg = 14;
                        for (int k = 0; k < seg; k++)
                        {
                            float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
                            var p0 = new Vector3(c.x + Mathf.Cos(a0) * 0.65f, y, c.z + Mathf.Sin(a0) * 0.65f);
                            var p1 = new Vector3(c.x + Mathf.Cos(a1) * 0.65f, y, c.z + Mathf.Sin(a1) * 0.65f);
                            mb.Tri(new Vector3(c.x, y, c.z), p1, p0, Vector3.up, new Vector2(0.5f, 0.5f), new Vector2(0.5f + Mathf.Cos(a1) * 0.5f, 0.5f + Mathf.Sin(a1) * 0.5f), new Vector2(0.5f + Mathf.Cos(a0) * 0.5f, 0.5f + Mathf.Sin(a0) * 0.5f));
                        }
                        covers++;
                    }
                    while (nextPatch < acc + len)
                    {
                        var c = Vector3.Lerp(a, b, (nextPatch - acc) / len) + r * (((float)rnd.NextDouble() - 0.5f) * 1.0f);
                        nextPatch += 70f + (float)rnd.NextDouble() * 130f;
                        float pl = 1.6f + (float)rnd.NextDouble() * 2.6f, pw = 0.9f + (float)rnd.NextDouble() * 1.1f;
                        var q0 = c - u * (pl * 0.5f) - r * (pw * 0.5f); var q1 = c + u * (pl * 0.5f) - r * (pw * 0.5f);
                        var q2 = c + u * (pl * 0.5f) + r * (pw * 0.5f); var q3 = c - u * (pl * 0.5f) + r * (pw * 0.5f);
                        bool ok = true;
                        foreach (var q in new[] { q0, q1, q2, q3 }) if (!sumoRoadArea.Contains(q.x, q.z) || InSumoJunction(q, 0.5f)) { ok = false; break; }
                        if (!ok) continue;
                        float y = c.y + 0.012f;
                        Vector3 Y(Vector3 q) => new Vector3(q.x, y, q.z);
                        sink.B("RoadWear", "roadAlley", c).QuadUp(Y(q0), Y(q1), Y(q2), Y(q3), 6f);
                        patches++;
                    }
                    acc += len;
                }
            }
            sink.CastShadows["RoadWear"] = false;
            Log($"ROADWEAR manholes={covers} patches={patches}");
        }

        // Korean crosswalk ends: a 0.6 m band of yellow warning blocks across the crossing width just behind the kerb, and on most
        // crossings a row of granite bollards (0.85 m, about 1.5 m apart) at the kerb. Never on the carriageway.
        static void BuildCrossingFurniture()
        {
            if (sumo.crossings == null || sumo.crossings.Count == 0) { Log("CROSSINGFURNITURE none (old export)"); return; }
            var rnd = new System.Random(733);
            int strips = 0, bollards = 0, skipped = 0;
            foreach (var c in sumo.crossings)
            {
                var u = new Vector3(c.ux, 0f, c.uz).normalized;   // from the sidewalk into the road
                var r = Right(u);
                var p = new Vector3(c.x, c.y, c.z);
                float half = Mathf.Max(1.0f, c.w * 0.5f - 0.15f);
                // step back until the band is off the carriageway (the crossing end can sit a little inside the road)
                float back = 0.25f;
                bool OnRoadAt(Vector3 q) => sumoRoadArea.Contains(q.x, q.z);
                while (back <= 1.2f && (OnRoadAt(p - u * back) || OnRoadAt(p - u * back + r * half) || OnRoadAt(p - u * back - r * half))) back += 0.15f;
                var t0 = p - u * back; var t1 = p - u * (back + 0.6f);
                // the far edge must still lie on sidewalk paving (a narrow sidewalk would leave it hanging over the ground)
                bool farOk = sumoPavedArea.Contains(t1.x, t1.z) && !OnRoadAt(t1);
                if (back > 1.2f || !farOk) { skipped++; continue; }
                float y = c.y + 0.018f;
                Vector3 Y(Vector3 q) => new Vector3(q.x, y, q.z);
                var tb = sink.B("Tactile", "tactile", p);
                tb.Quad(Y(t0 - r * half), Y(t0 + r * half), Y(t1 + r * half), Y(t1 - r * half), Vector3.up,
                    Vector2.zero, new Vector2(2f * half / 0.3f, 0f), new Vector2(2f * half / 0.3f, 2f), new Vector2(0f, 2f));
                strips++;
                if (rnd.NextDouble() < 0.65)
                {
                    var bb = sink.B("Bollards", "kerb", p);
                    for (float o = -half + 0.3f; o <= half - 0.3f + 0.01f; o += 1.5f)
                    {
                        var q = p - u * (back - 0.1f) + r * o;
                        if (sumoRoadArea.Contains(q.x, q.z)) q = p - u * (back + 0.2f) + r * o;
                        if (sumoRoadArea.Contains(q.x, q.z)) continue;
                        var at = new Vector3(q.x, c.y, q.z);
                        Pole(bb, at, 0.82f, 0.11f, 0.1f, 10);
                        Cone(bb, at + Vector3.up * 0.82f, 0.06f, 0.1f, 0.04f, 10);
                        bollards++;
                    }
                }
            }
            Log($"CROSSINGFURNITURE tactileStrips={strips} bollards={bollards} skipped(no sidewalk behind)={skipped}");
        }

        /// <summary>Collider sweep over the SUMO lane centre lines (every 2 m, the centre and +-1 m): a surface within 0.6 m of the lane height.</summary>
        static string SumoLaneSweep()
        {
            if (sumo == null || sumo.lanes == null || sumo.lanes.Count == 0) return "SUMOLANES none";
            int mask = 1 << RoadLayer, samples = 0, holes = 0;
            var examples = new List<string>();
            foreach (var lane in sumo.lanes)
            {
                int n = lane.p.Length / 3;
                for (int i = 0; i < n - 1; i++)
                {
                    var a = V(lane.p, i); var b = V(lane.p, i + 1);
                    float len = Vector3.Distance(Flat(a), Flat(b));
                    var r = Right(b - a);
                    for (float d = 0.5f; d < len; d += 2f)
                    {
                        var q = Vector3.Lerp(a, b, d / len);
                        foreach (float lat in new[] { 0f, 1f, -1f })
                        {
                            samples++;
                            var o = q + r * lat + Vector3.up * 1.2f;
                            if (!Physics.Raycast(o, Vector3.down, out var hit, 3f, mask) || Mathf.Abs(hit.point.y - q.y) > 0.6f)
                            {
                                holes++;
                                if (examples.Count < 6) examples.Add($"({q.x:0.0},{q.z:0.0}) lat={lat:0} y={q.y:0.00}");
                            }
                        }
                    }
                }
            }
            return $"SUMOLANES samples={samples} holes={holes} {string.Join(" ", examples)}";
        }

        static SumoGeometry sumo;
        static List<(Vector2[] ring, Vector4 box)> sumoJunctions;
        static TriangleIndex sumoRoadArea, sumoPavedArea;   // carriageway only; carriageway and sidewalks

        static bool UseSumo => sumo != null;

        static void LoadSumo()
        {
            var path = DataDir + "BusanSumoGeometry.json";
            sumo = File.Exists(path) ? JsonUtility.FromJson<SumoGeometry>(File.ReadAllText(path)) : null;
            sumoJunctions = new List<(Vector2[], Vector4)>();
            sumoRoadArea = new TriangleIndex();
            if (sumo == null) { Log("SUMO geometry not found: old road network"); return; }
            // roads the export closed off (a back-street junction cut through by a main road on another level): the same barrier
            // and cones as the closures of the old network data
            if (sumo.closures != null && sumo.closures.Count > 0)
            {
                if (net.closures == null) net.closures = new List<ClosureData>();
                foreach (var c in sumo.closures) net.closures.Add(new ClosureData { x = c.x, y = c.y, z = c.z, dx = c.dx, dz = c.dz, w = c.w });
            }
            foreach (var j in sumo.junctions)
            {
                var ring = new Vector2[j.p.Length / 2];
                for (int i = 0; i < ring.Length; i++) ring[i] = new Vector2(j.p[i * 2], j.p[i * 2 + 1]);
                sumoJunctions.Add((ring, new Vector4(ring.Min(q => q.x), ring.Min(q => q.y), ring.Max(q => q.x), ring.Max(q => q.y))));
            }
            sumoPavedArea = new TriangleIndex();
            foreach (var m in sumo.meshes)
                // "RoadsOuter": carriageway of the roads the export closed off. Drawn like a road and kept clear of props and
                // buildings, but it gets no collider (only Roads, Roads1-3 do), so the car cannot drive onto it.
                if (m.cat == "Roads" || m.cat == "RoadsOuter" || m.cat == "Sidewalk")
                    for (int k = 0; k < m.t.Length; k += 3)
                    {
                        var a = V(m.v, m.t[k]); var b = V(m.v, m.t[k + 1]); var c = V(m.v, m.t[k + 2]);
                        if (m.cat == "Roads" || m.cat == "RoadsOuter") sumoRoadArea.Add(a, b, c);
                        sumoPavedArea.Add(a, b, c);
                    }
            Log($"SUMO meshes={sumo.meshes.Count} lines={sumo.lines.Count} heads={sumo.heads.Count} programs={sumo.programs.Count} junctions={sumo.junctions.Count}");
        }

        static Vector3 V(float[] v, int i) => new Vector3(v[i * 3], v[i * 3 + 1], v[i * 3 + 2]);

        /// <summary>True when p (plan) lies inside a SUMO junction polygon grown by `margin`.</summary>
        static bool InSumoJunction(Vector3 p, float margin)
        {
            if (sumoJunctions == null) return false;
            foreach (var (ring, b) in sumoJunctions)
            {
                if (p.x < b.x - margin || p.x > b.z + margin || p.z < b.y - margin || p.z > b.w + margin) continue;
                bool inside = false;
                for (int a = 0, c = ring.Length - 1; a < ring.Length; c = a++)
                {
                    if (((ring[a].y > p.z) != (ring[c].y > p.z)) && (p.x < (ring[c].x - ring[a].x) * (p.z - ring[a].y) / (ring[c].y - ring[a].y) + ring[a].x)) inside = !inside;
                    if (margin > 0f)
                    {
                        float dx = ring[c].x - ring[a].x, dz = ring[c].y - ring[a].y, len2 = dx * dx + dz * dz;
                        float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(((p.x - ring[a].x) * dx + (p.z - ring[a].y) * dz) / len2);
                        float px = ring[a].x + dx * t - p.x, pz = ring[a].y + dz * t - p.z;
                        if (px * px + pz * pz < margin * margin) return true;
                    }
                }
                if (inside) return true;
            }
            return false;
        }

        /// <summary>Plan-view triangle bins of the SUMO road surface: is a point on the carriageway (any level)?</summary>
        sealed class TriangleIndex
        {
            readonly List<(Vector2 a, Vector2 b, Vector2 c)> tris = new List<(Vector2, Vector2, Vector2)>();
            readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            const float Cell = 16f;
            static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            public void Add(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = tris.Count;
                tris.Add((new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z)));
                float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                float z0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), z1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                for (int x = Mathf.FloorToInt(x0 / Cell); x <= Mathf.FloorToInt(x1 / Cell); x++)
                    for (int z = Mathf.FloorToInt(z0 / Cell); z <= Mathf.FloorToInt(z1 / Cell); z++)
                    {
                        var k = Key(x, z);
                        if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>();
                        l.Add(i);
                    }
            }
            public bool Contains(float x, float z)
            {
                if (!cells.TryGetValue(Key(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell)), out var l)) return false;
                var p = new Vector2(x, z);
                foreach (int i in l)
                {
                    var (a, b, c) = tris[i];
                    float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
                    bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                    if (!(neg && pos)) return true;
                }
                return false;
            }
            static float Sign(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        }

        // Outer edges of the paving drop like a retaining wall: the 20 m ground grid can sit a metre or more lower on one side of a
        // hillside street, and a shallow skirt left a dark trench there.
        // Paving edges toward open ground fall away as a short concrete lip and then an embankment (a vertical 1.6 m wall in an open
        // lot read as a causeway); where the ground is higher the slope disappears under it.
        static readonly Vector2[] SidewalkSkirt = { new Vector2(0f, 0.15f), new Vector2(0.05f, -0.05f), new Vector2(2.6f, -1.6f) };
        static readonly Vector2[] RoadSkirt = { new Vector2(0f, 0.0f), new Vector2(0f, -1.2f) };
        static readonly Vector2[] ApronSkirt = { new Vector2(0f, -0.03f), new Vector2(0.05f, -0.2f), new Vector2(2.6f, -1.6f) };
        // A carriageway edge over water (a bridge): a 0.9 m concrete parapet and a 1.6 m fascia below the deck edge. Before, such a
        // road was a slab with nothing at its edge.
        static readonly Vector2[] RoadParapet = { new Vector2(0.05f, 0f), new Vector2(0.05f, 0.9f), new Vector2(0.3f, 0.9f), new Vector2(0.3f, -1.6f) };

        static void BuildSumoNetwork()
        {
            int verts = 0;
            foreach (var m in sumo.meshes)
            {
                var at = new Vector3((m.cx + 0.5f) * ChunkSize, 0f, (m.cz + 0.5f) * ChunkSize);
                var b = sink.B(m.cat, m.mat, at);
                int baseIndex = b.V.Count;
                for (int i = 0; i < m.v.Length / 3; i++) b.Vertex(V(m.v, i), Vector2.zero, Color.white);
                for (int k = 0; k < m.t.Length; k += 3) b.Index(baseIndex + m.t[k], baseIndex + m.t[k + 1], baseIndex + m.t[k + 2]);
                verts += m.v.Length / 3;
            }
            int kerbs = 0, skirts = 0;
            foreach (var line in sumo.lines)
            {
                int n = line.p.Length / 3;
                if (n < 2) continue;
                var p = new Vector3[n];
                for (int i = 0; i < n; i++) p[i] = V(line.p, i);
                var r = Rights(p);
                var zero = Fill(n, 0f);
                var mid = p[n / 2];
                if (line.kind == "kerb")
                {
                    var kerb = sink.B("Kerb", "kerb", mid);
                    kerb.NormalUp = 0.6f;
                    Sweep(kerb, p, r, zero, 1f, KerbFace, null, 3f);
                    kerbs++;
                }
                else if (line.kind == "sidewalkSkirt")
                {
                    Sweep(sink.B("Sidewalk", "sidewalk", mid), p, r, zero, 1f, SidewalkSkirt, null, 3f);
                    skirts++;
                }
                else if (line.kind == "roadSkirt")
                {
                    Sweep(sink.B("RoadEdge", "kerb", mid), p, r, zero, 1f, RoadSkirt, null, 3f);
                    skirts++;
                }
                else if (line.kind == "apronSkirt")
                {
                    // asphalt, not concrete: on level ground the lip of the back-street paving read as a pale kerb across the asphalt
                    Sweep(sink.B("RoadEdge", "road", mid), p, r, zero, 1f, ApronSkirt, null, 3f);
                    skirts++;
                }
                else if (line.kind == "roadParapet")
                {
                    Sweep(sink.B("RoadParapet", "concrete", mid), p, r, zero, 1f, RoadParapet, null, 3f);
                    skirts++;
                }
                else if (line.kind == "medianFence")
                {
                    // centre divider fence of a wide artery: two rails and a post every 4 m
                    Sweep(sink.B("MedianFence", "fence", mid), p, r, zero, 1f, FenceRailLow, null, 3f);
                    Sweep(sink.B("MedianFence", "fence", mid), p, r, zero, 1f, FenceRailHigh, null, 3f);
                    float carry = 0f;
                    for (int i = 0; i < n - 1; i++)
                    {
                        float seg = Vector3.Distance(p[i], p[i + 1]);
                        float t = carry;
                        while (t < seg)
                        {
                            var pos = Vector3.Lerp(p[i], p[i + 1], t / seg);
                            sink.B("MedianFence", "fence", pos).Box(pos + Vector3.up * 0.46f, new Vector3(0.055f, 0.92f, 0.055f));
                            t += 4f;
                        }
                        carry = t - seg;
                    }
                }
            }
            sink.CastShadows["RoadEdge"] = false;
            Log($"SUMO NETWORK vertices={verts} kerbs={kerbs} skirts={skirts}");
            // signals are built by BuildSignalsAndCrossings (after the placement indices), not here
        }

        static readonly HashSet<int> placedHeads = new HashSet<int>();

        // Korean horizontal four-lens head, read from the driver's left: red, yellow, left arrow, green. Lamps are one mesh (SignalLamp shader).
        static void BuildSumoSignals()
        {
            var lamps = sink.Single("SignalLamps", "signalLamps");
            int placed = 0, skipped = 0, moved = 0;
            placedHeads.Clear();
            for (int h = 0; h < sumo.heads.Count; h++)
            {
                var hd = sumo.heads[h];
                var toward = new Vector3(hd.dx, 0f, hd.dz).normalized;
                var face = new Vector3(hd.fx, 0f, hd.fz).normalized;
                // the pole stands beyond the junction on the right; when that spot is a carriageway, step further out (1 m at a time)
                var basePos = new Vector3(hd.x, 0f, hd.z);
                int step = 0;
                while (step <= 4 && sumoRoadArea.Contains(basePos.x, basePos.z)) { basePos -= toward; step++; }
                if (sumoRoadArea.Contains(basePos.x, basePos.z)) { skipped++; continue; }   // no stop is created for a head without a pole
                if (step > 0) moved++;
                basePos.y = Mathf.Max(hd.y + 0.15f, GroundH(basePos.x, basePos.z));
                float arm = Mathf.Clamp(hd.arm + step, 2.5f, 9f);
                var pole = sink.B("Signals", "signalPole", basePos);
                Pole(pole, basePos, 6.3f, 0.15f, 0.10f, 10);
                float yawArm = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
                pole.Box(basePos + toward * (arm * 0.5f) + Vector3.up * 5.85f, new Vector3(0.12f, 0.14f, arm), yawArm);
                var housingCentre = basePos + toward * (arm - 0.9f) + Vector3.up * 5.35f;
                float yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
                var housing = sink.B("Signals", "signalHousing", basePos);
                housing.Box(housingCentre, new Vector3(1.62f, 0.44f, 0.30f), yaw);
                housing.Box(housingCentre + Vector3.up * 0.32f, new Vector3(0.07f, 0.22f, 0.07f), yaw);
                var viewerRight = -Right(face);   // the driver faces -face: their right is -Right(face)
                for (int k = 0; k < 4; k++)
                {
                    var c = housingCentre + viewerRight * ((k - 1.5f) * 0.38f) + face * 0.16f;
                    // visor above each lens
                    housing.Box(c + face * 0.12f + Vector3.up * 0.17f, new Vector3(0.32f, 0.02f, 0.24f), yaw);
                    if (k == 2)
                    {
                        LampDisc(lamps, c, face, viewerRight, 0.15f, h, 4);
                        // left arrow: shaft and head, pointing to the driver's left (-viewerRight)
                        var lift = face * 0.005f;
                        var s0 = c - viewerRight * 0.02f + Vector3.up * 0.025f + lift; var s1 = c + viewerRight * 0.09f + Vector3.up * 0.025f + lift;
                        var s2 = c + viewerRight * 0.09f - Vector3.up * 0.025f + lift; var s3 = c - viewerRight * 0.02f - Vector3.up * 0.025f + lift;
                        LampQuad(lamps, s0, s1, s2, s3, face, h, 2);
                        LampTri(lamps, c - viewerRight * 0.10f + lift, c - viewerRight * 0.02f + Vector3.up * 0.075f + lift, c - viewerRight * 0.02f - Vector3.up * 0.075f + lift, face, h, 2);
                    }
                    else LampDisc(lamps, c, face, viewerRight, 0.15f, h, k == 3 ? 3 : k);
                }
                Record("signal", basePos);
                placedHeads.Add(h);
                placed++;
            }
            Log($"SUMO SIGNALS heads={sumo.heads.Count} placed={placed} movedOffCarriageway={moved} skippedInCarriageway={skipped} programs={sumo.programs.Count}");
        }

        static void LampDisc(MeshBuilder b, Vector3 c, Vector3 face, Vector3 right, float radius, int head, int kind)
        {
            const int n = 10;
            var uv = new Vector2(head, kind);
            for (int s = 0; s < n; s++)
            {
                float a0 = s * Mathf.PI * 2f / n, a1 = (s + 1) * Mathf.PI * 2f / n;
                b.Tri(c, c + (right * Mathf.Cos(a0) + Vector3.up * Mathf.Sin(a0)) * radius, c + (right * Mathf.Cos(a1) + Vector3.up * Mathf.Sin(a1)) * radius, face, uv, uv, uv);
            }
        }

        static void LampQuad(MeshBuilder b, Vector3 a, Vector3 c1, Vector3 c2, Vector3 d, Vector3 face, int head, int kind)
        {
            var uv = new Vector2(head, kind);
            b.Quad(a, c1, c2, d, face, uv, uv, uv, uv);
        }

        static void LampTri(MeshBuilder b, Vector3 a, Vector3 c1, Vector3 c2, Vector3 face, int head, int kind)
        {
            var uv = new Vector2(head, kind);
            b.Tri(a, c1, c2, face, uv, uv, uv);
        }

        const float MaxCycle = 64f, MinGreen = 6f;
        static bool IsGreen(string state) => state.IndexOf('G') >= 0 || state.IndexOf('g') >= 0;

        /// <summary>Signal programs, heads and the stop positions on the course and the corridors for the traffic data.</summary>
        static void FillSignalData(TrafficData data)
        {
            if (!UseSumo) return;
            // SUMO's programs hold a green for up to ~30 s in a 70-90 s cycle; a drive through the city should not wait that long:
            // greens at most 14 s, yellows 3 s, all-red clearances at most 2 s (cycle about 35-45 s)
            data.programs = sumo.programs.Select(p => new SignalProgram
            {
                offset = p.offset,
                states = p.state,
                durations = p.dur.Select((d, k) =>
                {
                    var st = p.state[k];
                    if (st.IndexOf('y') >= 0 || st.IndexOf('Y') >= 0) return Mathf.Min(d, 3f);
                    if (st.IndexOf('G') >= 0 || st.IndexOf('g') >= 0) return Mathf.Min(d, 14f);
                    return Mathf.Min(d, 2f);
                }).ToArray()
            }).ToArray();
            // joined signals can have a dozen phases (198 s even with the caps): shorten the greens (to no less than 6 s) until the
            // whole cycle fits in 64 s, so no approach waits more than about a minute
            foreach (var prog in data.programs)
            {
                float total = prog.durations.Sum();
                if (total <= MaxCycle) continue;
                float reducible = 0f;
                for (int k = 0; k < prog.durations.Length; k++)
                    if (IsGreen(prog.states[k])) reducible += Mathf.Max(0f, prog.durations[k] - MinGreen);
                if (reducible <= 0f) continue;
                float f = Mathf.Clamp01(1f - (total - MaxCycle) / reducible);
                for (int k = 0; k < prog.durations.Length; k++)
                    if (IsGreen(prog.states[k]) && prog.durations[k] > MinGreen) prog.durations[k] = MinGreen + (prog.durations[k] - MinGreen) * f;
            }
            int over = data.programs.Count(p => p.durations.Sum() > MaxCycle + 0.5f);
            if (over > 0) Log($"SIGNALCYCLE warning: {over} programs still above {MaxCycle:0}s (too many phases even at {MinGreen:0}s greens)");
            Log($"SIGNALCYCLE before mean={sumo.programs.Average(p => p.dur.Sum()):0}s max={sumo.programs.Max(p => p.dur.Sum()):0}s after mean={data.programs.Average(p => p.durations.Sum()):0}s max={data.programs.Max(p => p.durations.Sum()):0}s");
            data.heads = sumo.heads.Select(h => new SignalHead { program = h.prog, straight = h.s ?? new int[0], left = h.l ?? new int[0], right = h.r ?? new int[0] }).ToArray();
            var stops = new List<SignalStop>();
            // a path is controlled by a head when it passes the head's stop point (within the approach half width + 2.5 m) heading the same way
            void Scan(int path, Vector3[] pts, bool both)
            {
                var cum = new float[pts.Length];
                for (int i = 1; i < pts.Length; i++) cum[i] = cum[i - 1] + Vector3.Distance(Flat(pts[i - 1]), Flat(pts[i]));
                for (int h = 0; h < sumo.heads.Count; h++)
                {
                    if (!placedHeads.Contains(h)) continue;   // no pole, no lamps: cars do not stop there
                    var hd = sumo.heads[h];
                    var P = new Vector3(hd.px, hd.py, hd.pz);
                    var u = new Vector3(hd.ux, 0f, hd.uz);
                    int best = -1; float bestD = hd.half + 2.5f; float bestS = 0f;
                    for (int i = 0; i < pts.Length - 1; i++)
                    {
                        var a = Flat(pts[i]); var b = Flat(pts[i + 1]); var ab = b - a;
                        float len2 = ab.sqrMagnitude;
                        if (len2 < 1e-4f) continue;
                        float t = Mathf.Clamp01(Vector3.Dot(Flat(P) - a, ab) / len2);
                        var q = a + ab * t;
                        float d = Vector3.Distance(q, Flat(P));
                        if (d < bestD && Mathf.Abs(Mathf.Lerp(pts[i].y, pts[i + 1].y, t) - P.y) < 3f) { bestD = d; best = i; bestS = cum[i] + Mathf.Sqrt(len2) * t; }
                    }
                    if (best < 0) continue;
                    var tan = Flat(pts[best + 1] - pts[best]).normalized;
                    float dot = Vector3.Dot(tan, u);
                    int dir = dot > 0.7f ? 1 : (both && dot < -0.7f ? -1 : 0);
                    if (dir == 0) continue;
                    // movement through the junction: compare the path 30 m beyond the stop with the approach direction
                    float sAfter = bestS + dir * 30f;
                    int j = 0; while (j < pts.Length - 2 && cum[j + 1] < sAfter) j++;
                    var exit = Flat(pts[Mathf.Min(j + 1, pts.Length - 1)] - pts[j]).normalized * dir;
                    float turn = Vector3.SignedAngle(u, exit, Vector3.up);
                    int group = Mathf.Abs(turn) < 35f ? 0 : (turn < 0f ? 1 : 2);
                    stops.Add(new SignalStop { path = path, dir = dir, s = bestS, head = h, group = group });
                }
            }
            Scan(-1, data.course, true);
            for (int c = 0; c < data.corridors.Length; c++) Scan(c, data.corridors[c].points, false);
            data.stops = stops.ToArray();
            Log($"SIGNALSTOPS course={stops.Count(s => s.path < 0)} corridors={stops.Count(s => s.path >= 0)} programs={data.programs.Length} heads={data.heads.Length}");
        }
    }
}
