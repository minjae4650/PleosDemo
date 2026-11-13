using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // Stage 2 street detail: placement indices (roads, buildings, mouth zones), prop rules, traffic lights, stop lines and
    // crosswalks, utility poles with sagging wires, and tree / shrub clusters on green land and empty parcels.
    public static partial class BusanSliceBuilder
    {
        // ---------- indices ----------

        sealed class SegmentIndex
        {
            readonly List<(float ax, float az, float bx, float bz, float half)> segs = new List<(float, float, float, float, float)>();
            readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            const float Cell = 32f;
            static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
            public void Add(Vector3 a, Vector3 b, float half)
            {
                int i = segs.Count;
                segs.Add((a.x, a.z, b.x, b.z, half));
                float reach = half + 6f;   // covers every query margin used (planting asks for 4.5 m beyond the ribbon)
                int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach) / Cell), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + reach) / Cell);
                int z0 = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - reach) / Cell), z1 = Mathf.FloorToInt((Mathf.Max(a.z, b.z) + reach) / Cell);
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        var k = Key(x, z);
                        if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>();
                        l.Add(i);
                    }
            }
            /// <summary>Smallest (distance to a centre line minus that segment's half width) near p; negative = inside a ribbon. Searches the cell of p only (reach 32 m).</summary>
            public float Clearance(float x, float z)
            {
                float best = float.MaxValue;
                if (!cells.TryGetValue(Key(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell)), out var l)) return best;
                foreach (int i in l)
                {
                    var s = segs[i];
                    float dx = s.bx - s.ax, dz = s.bz - s.az;
                    float len2 = dx * dx + dz * dz;
                    float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(((x - s.ax) * dx + (z - s.az) * dz) / len2);
                    float px = s.ax + dx * t - x, pz = s.az + dz * t - z;
                    float c = Mathf.Sqrt(px * px + pz * pz) - s.half;
                    if (c < best) best = c;
                }
                return best;
            }
        }

        /// <summary>3D road ribbon segments (centre line with its height, half width) for the crown-versus-road test. Unlike SegmentIndex it keeps height and includes the elevated deck.</summary>
        sealed class RibbonIndex
        {
            public readonly List<(Vector3 a, Vector3 b, float half)> Segs = new List<(Vector3, Vector3, float)>();
            readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            const float Cell = 32f, Reach = 14f;   // Reach: larger than any crown radius (largest planted tree about 10 m)
            static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
            public void Add(Vector3 a, Vector3 b, float half)
            {
                int i = Segs.Count;
                Segs.Add((a, b, half));
                float reach = half + Reach;
                for (int x = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach) / Cell); x <= Mathf.FloorToInt((Mathf.Max(a.x, b.x) + reach) / Cell); x++)
                    for (int z = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - reach) / Cell); z <= Mathf.FloorToInt((Mathf.Max(a.z, b.z) + reach) / Cell); z++)
                    {
                        var k = Key(x, z);
                        if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>();
                        l.Add(i);
                    }
            }
            public bool CrownReaches(Vector3 pos, float radius, float yTop)
            {
                if (!cells.TryGetValue(Key(Mathf.FloorToInt(pos.x / Cell), Mathf.FloorToInt(pos.z / Cell)), out var l)) return false;
                foreach (int i in l) if (CrownHitsRibbon(Segs[i].a, Segs[i].b, Segs[i].half, pos, radius, yTop)) return true;
                return false;
            }
        }

        /// <summary>A crown (cylinder: radius, from the base up to yTop) reaches a road surface it does not stand on: the ribbon is within the radius in plan view,
        /// its surface is more than 2 m above the tree's base (a different level) and the crown top gets within 1.5 m (slab depth) below that surface.</summary>
        static bool CrownHitsRibbon(Vector3 a, Vector3 b, float half, Vector3 pos, float radius, float yTop)
        {
            float dx = b.x - a.x, dz = b.z - a.z, len2 = dx * dx + dz * dz;
            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(((pos.x - a.x) * dx + (pos.z - a.z) * dz) / len2);
            float px = a.x + dx * t - pos.x, pz = a.z + dz * t - pos.z;
            if (Mathf.Sqrt(px * px + pz * pz) - half >= radius) return false;
            float ry = Mathf.Lerp(a.y, b.y, t);
            return ry > pos.y + 2f && ry - 1.5f < yTop;
        }

        static RibbonIndex ribbons;
        static readonly List<(string kind, Vector3 pos, float radius, float yTop)> placedCrowns = new List<(string, Vector3, float, float)>();
        static int crownCulled;
        static bool CrownCull = true;   // false only to measure the "before" count

        /// <summary>True when the crown may stand here (and records it); false when it would reach a road it does not stand on.</summary>
        static bool CrownOK(string kind, Vector3 pos, float radius, float yTop)
        {
            if (CrownCull && ribbons.CrownReaches(pos, radius, yTop)) { crownCulled++; if (crownCulled <= 40) Log($"PROPCHECK culled {kind} at {pos.ToString("0.0")} radius={radius:0.0} top={yTop:0.0}"); return false; }
            placedCrowns.Add((kind, pos, radius, yTop));
            return true;
        }

        sealed class BuildingIndex
        {
            readonly List<Vector2[]> rings = new List<Vector2[]>();
            readonly List<Vector4> boxes = new List<Vector4>();
            readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            const float Cell = 32f;
            static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
            public void Add(Vector2[] ring)
            {
                int i = rings.Count;
                rings.Add(ring);
                float x0 = ring.Min(p => p.x), x1 = ring.Max(p => p.x), z0 = ring.Min(p => p.y), z1 = ring.Max(p => p.y);
                boxes.Add(new Vector4(x0, z0, x1, z1));
                for (int x = Mathf.FloorToInt((x0 - 6f) / Cell); x <= Mathf.FloorToInt((x1 + 6f) / Cell); x++)
                    for (int z = Mathf.FloorToInt((z0 - 6f) / Cell); z <= Mathf.FloorToInt((z1 + 6f) / Cell); z++)
                    {
                        var k = Key(x, z);
                        if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>();
                        l.Add(i);
                    }
            }
            /// <summary>True when p is inside a footprint or within `margin` of its walls.</summary>
            public bool Near(float x, float z, float margin)
            {
                if (!cells.TryGetValue(Key(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell)), out var l)) return false;
                foreach (int i in l)
                {
                    var b = boxes[i];
                    if (x < b.x - margin || x > b.z + margin || z < b.y - margin || z > b.w + margin) continue;
                    var r = rings[i];
                    bool inside = false;
                    float m2 = margin * margin;
                    for (int a = 0, c = r.Length - 1; a < r.Length; c = a++)
                    {
                        if (((r[a].y > z) != (r[c].y > z)) && (x < (r[c].x - r[a].x) * (z - r[a].y) / (r[c].y - r[a].y) + r[a].x)) inside = !inside;
                        if (margin > 0f)
                        {
                            float dx = r[c].x - r[a].x, dz = r[c].y - r[a].y, len2 = dx * dx + dz * dz;
                            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(((x - r[a].x) * dx + (z - r[a].y) * dz) / len2);
                            float px = r[a].x + dx * t - x, pz = r[a].y + dz * t - z;
                            if (px * px + pz * pz < m2) return true;
                        }
                    }
                    if (inside) return true;
                }
                return false;
            }
        }

        struct MouthZone { public Vector3 origin, dir; public float half, tMin, tMax; public int kind; }

        static SegmentIndex roadSegments;
        static BuildingIndex buildingIndex;
        static PointGrid roadSampleGrid;
        static readonly List<MouthZone> mouthZones = new List<MouthZone>();
        static readonly List<(string kind, Vector3 pos)> placedProps = new List<(string, Vector3)>();
        static List<(Vector3 pos, Vector3 dir, float hw, int cls)>[] junctionArms; // parallel to net.junctions: arms with the class of the road they belong to

        static void BuildPlacementIndices()
        {
            roadSegments = new SegmentIndex();
            var samples = new List<Vector3>();
            for (int i = 0; i < cp.Length - 1; i++)
            {
                if (celev[i] && ckind[i] != Kapproach) continue;
                roadSegments.Add(cp[i], cp[i + 1], csh[i] + 0.2f);
                samples.Add(cp[i]);
            }
            foreach (var road in net.roads)
            {
                int cnt = road.p.Count / 3;
                bool flared = road.ws != null && road.ws.Count == cnt;
                Vector3 P(int i) => new Vector3(road.p[i * 3], road.p[i * 3 + 1], road.p[i * 3 + 2]);
                for (int i = 0; i < cnt - 1; i++)
                {
                    float h = Mathf.Max(flared ? road.ws[i] : road.w, flared ? road.ws[i + 1] : road.w) * 0.5f;
                    roadSegments.Add(P(i), P(i + 1), h);
                    if (i % 2 == 0) samples.Add(P(i));
                }
                samples.Add(P(cnt - 1));
            }
            roadSampleGrid = new PointGrid(samples.ToArray(), 40f);

            ribbons = new RibbonIndex();
            placedCrowns.Clear(); crownCulled = 0;
            for (int i = 0; i < cp.Length - 1; i++) ribbons.Add(cp[i], cp[i + 1], csh[i] + 0.2f);
            foreach (var road in net.roads)
            {
                int cnt = road.p.Count / 3;
                bool flared = road.ws != null && road.ws.Count == cnt;
                for (int i = 0; i < cnt - 1; i++)
                    ribbons.Add(new Vector3(road.p[i * 3], road.p[i * 3 + 1], road.p[i * 3 + 2]), new Vector3(road.p[i * 3 + 3], road.p[i * 3 + 4], road.p[i * 3 + 5]),
                        Mathf.Max(flared ? road.ws[i] : road.w, flared ? road.ws[i + 1] : road.w) * 0.5f);
            }

            buildingIndex = new BuildingIndex();
            foreach (var d in bld.buildings)
            {
                int n = d.p.Count / 2;
                if (n < 3) continue;
                var ring = new Vector2[n];
                for (int i = 0; i < n; i++) ring[i] = new Vector2(d.p[2 * i], d.p[2 * i + 1]);
                buildingIndex.Add(ring);
            }

            // arms with their road class: roads whose end sits on the junction node
            junctionArms = new List<(Vector3, Vector3, float, int)>[net.junctions.Count];
            var byPos = new Dictionary<long, List<(Vector3 pos, Vector3 dir, float hw, int cls)>>();
            long K(float x, float z) => ((long)Mathf.RoundToInt(x * 0.5f) << 32) ^ (uint)Mathf.RoundToInt(z * 0.5f);
            foreach (var road in net.roads)
            {
                int cnt = road.p.Count / 3;
                if (cnt < 2) continue;
                foreach (int end in new[] { 0, cnt - 1 })
                {
                    var a = new Vector3(road.p[end * 3], road.p[end * 3 + 1], road.p[end * 3 + 2]);
                    int nx = end == 0 ? 1 : cnt - 2;
                    var b = new Vector3(road.p[nx * 3], road.p[nx * 3 + 1], road.p[nx * 3 + 2]);
                    var dir = Flat(b - a).normalized;
                    for (int ox = -1; ox <= 1; ox++)
                        for (int oz = -1; oz <= 1; oz++)
                        {
                            var key = K(a.x + ox * 2f, a.z + oz * 2f);
                            if (!byPos.TryGetValue(key, out var l)) byPos[key] = l = new List<(Vector3, Vector3, float, int)>();
                            l.Add((a, dir, road.w * 0.5f, road.cls));
                        }
                }
            }
            for (int j = 0; j < net.junctions.Count; j++)
            {
                var jn = net.junctions[j];
                var node = new Vector3(jn.x, jn.y, jn.z);
                junctionArms[j] = new List<(Vector3, Vector3, float, int)>();
                if (byPos.TryGetValue(K(jn.x, jn.z), out var l))
                    foreach (var arm in l)
                        if ((arm.pos - node).sqrMagnitude < 1.5f * 1.5f && !junctionArms[j].Any(q => Vector3.Dot(q.dir, arm.dir) > 0.98f)) junctionArms[j].Add((node, arm.dir, arm.hw, arm.cls));
            }

            // mouth zones: the opening of every side street plus the kerb on the far side of the road it opens onto
            mouthZones.Clear();
            if (net.mouths != null)
                foreach (var m in net.mouths)
                {
                    int ci = Mathf.Clamp(m.ci, 0, cp.Length - 1);
                    mouthZones.Add(new MouthZone { origin = new Vector3(m.x, m.y, m.z), dir = new Vector3(m.dx, 0f, m.dz).normalized, half = m.half, tMin = -(2f * csh[ci] + 6f), tMax = 4f, kind = m.cls });
                }
            for (int j = 0; j < net.junctions.Count; j++)
            {
                var arms = junctionArms[j];
                if (arms.Count < 3) continue;
                foreach (var arm in arms)
                    mouthZones.Add(new MouthZone { origin = arm.pos, dir = arm.dir, half = arm.hw, tMin = -12f, tMax = 4f, kind = arm.cls });
            }
            Log($"PLACEMENT segments indexed, buildings={bld.buildings.Count}, mouth zones={mouthZones.Count}");
        }

        static bool InMouthZone(Vector3 p, float extra)
        {
            foreach (var z in mouthZones)
            {
                if (Mathf.Abs(p.y - z.origin.y) > 5f) continue;   // a viaduct passes over the junction below, it does not stand in its mouth
                var rel = Flat(p - z.origin);
                if (rel.sqrMagnitude > 120f * 120f) continue;
                float t = Vector3.Dot(rel, z.dir);
                if (t < z.tMin || t > z.tMax) continue;
                float lat = Mathf.Abs(rel.x * z.dir.z - rel.z * z.dir.x);
                if (lat < z.half + extra) return true;
            }
            return false;
        }

        /// <summary>A prop may stand here: not on or beside another road's pavement, not inside or hard against a building, not in a mouth zone.</summary>
        static bool PropOK(Vector3 p, float roadMargin, float buildingMargin, float mouthExtra = 2f)
        {
            if (roadSegments.Clearance(p.x, p.z) < roadMargin) return false;
            if (UseSumo && (sumoRoadArea.Contains(p.x, p.z) || InSumoJunction(p, 1.0f))) return false;
            if (buildingMargin >= 0f && buildingIndex.Near(p.x, p.z, buildingMargin)) return false;
            return !InMouthZone(p, mouthExtra);
        }

        static void Record(string kind, Vector3 pos) => placedProps.Add((kind, pos));

        static void LogPropViolations()
        {
            // Independent post-check over everything that was placed: any lamp, tree, pole or signal inside a mouth zone (mouth width + 2 m)
            // counts, whichever code path placed it.
            var counts = placedProps.GroupBy(p => p.kind).ToDictionary(g => g.Key, g => g.Count());
            int viol = 0;
            var byKind = new Dictionary<string, int>();
            foreach (var (kind, pos) in placedProps)
                if (InMouthZone(pos, 2f)) { viol++; byKind[kind] = byKind.TryGetValue(kind, out var c) ? c + 1 : 1; if (viol <= 5) Log($"PROPCHECK violation {kind} at {pos.ToString("0.0")}"); }
            // 3D crown check, brute force over every ribbon segment (independent of the grid used while placing): crown radius = half the card width (size / 2),
            // height = the card top above the base; both from the card size in tree_card.png.txt at the tree's own random scale.
            var inter = new Dictionary<string, int>();
            int crownHits = 0;
            foreach (var c in placedCrowns)
                foreach (var s in ribbons.Segs)
                    if (CrownHitsRibbon(s.a, s.b, s.half, c.pos, c.radius, c.yTop)) { crownHits++; inter[c.kind] = inter.TryGetValue(c.kind, out var n) ? n + 1 : 1; if (crownHits <= 5) Log($"PROPCHECK crown reaches road: {c.kind} at {c.pos.ToString("0.0")} radius={c.radius:0.0} top={c.yTop:0.0}"); break; }
            Log($"PROPCHECK3D crowns={placedCrowns.Count} intersectingRoad={crownHits}{(crownHits > 0 ? " (" + string.Join(",", inter.Select(kv => kv.Key + ":" + kv.Value)) + ")" : "")} culled={crownCulled} cull={CrownCull} cardSize={BusanSliceTrees.CardSize:0.00} cardCenterY={BusanSliceTrees.CardCenterY:0.00}");
            Log($"PROPCHECK placed={string.Join(",", counts.Select(kv => kv.Key + ":" + kv.Value))} mouthZoneViolations={viol}{(viol > 0 ? " (" + string.Join(",", byKind.Select(kv => kv.Key + ":" + kv.Value)) + ")" : "")}");
        }

        // ---------- cylinders and wires ----------

        static void Pole(MeshBuilder b, Vector3 basePos, float height, float r0, float r1, int sides)
        {
            Cone(b, basePos, height, r0, r1, sides);
        }

        // thin wire as a triangular prism (a flat ribbon would vanish edge-on when the wire runs along the viewing axis)
        static void WireSpan(MeshBuilder b, Vector3 a, Vector3 c, float sag, float thick)
        {
            const int seg = 5;
            Vector3 PointAt(int k) { float t = k / (float)seg; return Vector3.Lerp(a, c, t) + Vector3.down * (4f * sag * t * (1f - t)); }
            var dir0 = (c - a).normalized;
            var n1 = Vector3.Cross(dir0, Vector3.up); if (n1.sqrMagnitude < 1e-4f) n1 = Vector3.right; n1.Normalize();
            var n2 = Vector3.Cross(dir0, n1).normalized;
            Vector3 Ring(Vector3 p, int s) { float ang = s * 2f * Mathf.PI / 3f; return p + (n1 * Mathf.Cos(ang) + n2 * Mathf.Sin(ang)) * thick; }
            for (int k = 0; k < seg; k++)
            {
                var p0 = PointAt(k); var p1 = PointAt(k + 1);
                for (int s = 0; s < 3; s++)
                {
                    float am = (s + 0.5f) * 2f * Mathf.PI / 3f;
                    b.Quad(Ring(p0, s), Ring(p0, (s + 1) % 3), Ring(p1, (s + 1) % 3), Ring(p1, s), n1 * Mathf.Cos(am) + n2 * Mathf.Sin(am));
                }
            }
        }

        // ---------- utility poles and wires (class 3 and 6 roads) ----------

        static void BuildPolesAndWires()
        {
            var rnd = new System.Random(9137);
            int poles = 0, spans = 0, transformers = 0, skipped = 0;
            long wireTris0 = 0;
            foreach (var road in net.roads)
            {
                if (road.cls != 3 && road.cls != 6) continue;
                int cnt = road.p.Count / 3;
                var pts = new Vector3[cnt];
                for (int i = 0; i < cnt; i++) pts[i] = new Vector3(road.p[i * 3], road.p[i * 3 + 1], road.p[i * 3 + 2]);
                float hw = road.w * 0.5f;
                float height = road.cls == 6 ? 7.6f : 8.6f;
                float side0 = rnd.NextDouble() < 0.5 ? 1f : -1f;
                float next = 6f + (float)rnd.NextDouble() * 12f, acc = 0f;
                Vector3 prevTop = default; Vector3 prevRight = default; bool havePrev = false;
                Vector3[] prevAttach = null;
                for (int i = 0; i < cnt - 1; i++)
                {
                    float seg = Vector3.Distance(pts[i], pts[i + 1]);
                    while (next <= acc + seg)
                    {
                        float t = (next - acc) / seg;
                        var onRoad = Vector3.Lerp(pts[i], pts[i + 1], t);
                        var tan = Flat(pts[i + 1] - pts[i]).normalized;
                        var right = Right(tan);
                        next += 32f + (float)rnd.NextDouble() * 7f;
                        Vector3 placed = default; bool ok = false;
                        foreach (var s in new[] { side0, -side0 })
                        {
                            var pos = onRoad + right * (s * (hw + 0.75f));
                            pos.y = onRoad.y;
                            if (!PropOK(pos, 0.3f, 0.5f, 2.2f)) continue;
                            placed = pos; ok = true; break;
                        }
                        if (!ok) { skipped++; havePrev = false; continue; }
                        // pole
                        var b = sink.B("Poles", "pole", placed);
                        float h = height + (float)rnd.NextDouble() * 0.6f;
                        Pole(b, placed, h, 0.17f, 0.11f, 8);
                        var arm = sink.B("PoleArms", "poleArm", placed);
                        float yaw = Mathf.Atan2(right.x, right.z) * Mathf.Rad2Deg;
                        arm.Box(placed + Vector3.up * (h - 0.35f), new Vector3(1.9f, 0.09f, 0.09f), yaw + 90f);
                        arm.Box(placed + Vector3.up * (h - 0.35f - 1.6f), new Vector3(1.1f, 0.07f, 0.07f), yaw + 90f);
                        if (rnd.NextDouble() < 0.22)
                        {
                            var tb = sink.B("PoleArms", "poleArm", placed);
                            var tpos = placed + right * (0.33f * side0) + Vector3.up * (h - 1.35f);
                            Cone(tb, tpos, 0.62f, 0.2f, 0.2f, 8);
                            tb.Box(tpos + Vector3.up * 0.64f, new Vector3(0.4f, 0.04f, 0.4f));
                            transformers++;
                        }
                        Record("pole", placed);
                        poles++;
                        // wires: three conductors on the cross arm and a bundle of telecom cables lower down (Busan back streets
                        // carry four to eight of them per pole in every roadview)
                        var attach = new[]
                        {
                            placed + Vector3.up * (h - 0.28f) - right * 0.8f, placed + Vector3.up * (h - 0.28f), placed + Vector3.up * (h - 0.28f) + right * 0.8f,
                            placed + Vector3.up * (h - 1.95f) + right * 0.3f, placed + Vector3.up * (h - 2.25f) + right * 0.22f,
                            placed + Vector3.up * (h - 2.5f) + right * 0.34f, placed + Vector3.up * (h - 2.8f) + right * 0.26f
                        };
                        if (havePrev && prevAttach != null && Vector3.Distance(prevTop, placed) < 48f)
                        {
                            float span = Vector3.Distance(prevTop, placed);
                            var wb = sink.B("Wires", "wire", placed);
                            for (int k = 0; k < attach.Length; k++)
                                WireSpan(wb, prevAttach[k], attach[k], 0.012f * span + 0.12f + (k >= 3 ? 0.18f + 0.07f * (k - 3) : 0f), k >= 3 ? 0.026f + 0.006f * (k & 1) : 0.032f);
                            spans++;
                        }
                        prevTop = placed; prevRight = right; prevAttach = attach; havePrev = true;
                    }
                    acc += seg;
                }
            }
            sink.CastShadows["Wires"] = false;
            Log($"POLES poles={poles} spans={spans} transformers={transformers} skippedByRules={skipped}");
        }

        // ---------- traffic lights, stop lines, crosswalks ----------

        static void Crosswalk(Vector3 center, Vector3 travel, Vector3 across, float width)
        {
            // zebra: bars parallel to the travel direction, 0.5 m wide with 0.5 m gaps, 3 m long, across `width`
            travel.y = 0f; travel.Normalize(); across.y = 0f; across.Normalize();
            for (float lat = -width * 0.5f + 0.5f; lat <= width * 0.5f - 0.4f; lat += 1.0f)
            {
                var c0 = center + across * lat;
                var a = c0 - travel * 1.5f - across * 0.25f; var b = c0 - travel * 1.5f + across * 0.25f;
                var c = c0 + travel * 1.5f + across * 0.25f; var d = c0 + travel * 1.5f - across * 0.25f;
                var lift = Vector3.up * 0.03f;
                sink.B("Paint", "white", c0).Quad(a + lift, b + lift, c + lift, d + lift, Vector3.up);
            }
        }

        static void StopLine(Vector3 center, Vector3 travel, Vector3 across, float from, float to)
        {
            travel.y = 0f; travel.Normalize(); across.y = 0f; across.Normalize();
            var lift = Vector3.up * 0.03f;
            var a = center + across * from - travel * 0.2f; var b = center + across * to - travel * 0.2f;
            var c = center + across * to + travel * 0.2f; var d = center + across * from + travel * 0.2f;
            sink.B("Paint", "white", center).Quad(a + lift, b + lift, c + lift, d + lift, Vector3.up);
        }

        // Korean horizontal four-lens signal (red, yellow, left arrow, green) on a mast arm. Green is lit, there is no signal logic.
        static void Signal(Vector3 basePos, Vector3 towardRoad, Vector3 face, float armLength)
        {
            towardRoad = Flat(towardRoad).normalized; face = Flat(face).normalized;
            var pole = sink.B("Signals", "signalPole", basePos);
            Pole(pole, basePos, 6.1f, 0.14f, 0.09f, 8);
            float yawArm = Mathf.Atan2(towardRoad.x, towardRoad.z) * Mathf.Rad2Deg;
            pole.Box(basePos + towardRoad * (armLength * 0.5f) + Vector3.up * 5.75f, new Vector3(0.1f, 0.12f, armLength), yawArm);
            var housingCentre = basePos + towardRoad * (armLength - 1.1f) + Vector3.up * 5.28f;
            float yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
            sink.B("Signals", "signalHousing", basePos).Box(housingCentre, new Vector3(1.62f, 0.44f, 0.3f), yaw);
            sink.B("Signals", "signalHousing", basePos).Box(housingCentre + Vector3.up * 0.28f, new Vector3(0.06f, 0.14f, 0.06f), yaw);
            var sideDir = Right(face);
            string[] lens = { "signalRed", "signalYellow", "signalArrow", "signalGreen" };
            for (int k = 0; k < 4; k++)
            {
                var c = housingCentre + sideDir * ((k - 1.5f) * 0.38f) + face * 0.16f;
                var lb = sink.B("Signals", lens[k], basePos);
                const int n = 8;
                for (int s = 0; s < n; s++)
                {
                    float a0 = s * Mathf.PI * 2f / n, a1 = (s + 1) * Mathf.PI * 2f / n;
                    lb.Tri(c, c + (sideDir * Mathf.Cos(a0) + Vector3.up * Mathf.Sin(a0)) * 0.15f, c + (sideDir * Mathf.Cos(a1) + Vector3.up * Mathf.Sin(a1)) * 0.15f, face,
                        Vector2.zero, Vector2.right, Vector2.up);
                }
            }
            Record("signal", basePos);
        }

        static void BuildSignalsAndCrossings()
        {
            if (UseSumo) { BuildSumoSignals(); BuildCrossingFurniture(); BuildRoadWear(); return; }   // crossings and stop lines come with the SUMO paint
            int signals = 0, crosswalks = 0, stopLines = 0, skipped = 0, courseJunctions = 0, nodeJunctions = 0;
            // course junctions with a class <= 2 street
            if (net.mouths != null)
                foreach (var m in net.mouths)
                {
                    int ci = Mathf.Clamp(m.ci, 1, cp.Length - 2);
                    if (m.cls > 2 || celev[ci] || ckind[ci] == Kbridge || ckind[ci] == Kapproach) continue;
                    var T = Flat(cp[ci + 1] - cp[ci - 1]).normalized;
                    var R = Right(T);
                    var C = cp[ci];
                    float half = chw[ci];
                    float gap = m.half + 3.6f;
                    courseJunctions++;
                    foreach (float sgn in new[] { 1f, -1f })
                    {
                        var at = C + T * (sgn * gap);
                        Crosswalk(new Vector3(at.x, cp[ci].y, at.z), T, R, half * 2f - 0.8f);
                        crosswalks++;
                    }
                    // stop lines: forward direction stops before the crosswalk behind the mouth, oncoming stops after the mouth
                    StopLine(new Vector3(C.x, C.y, C.z) - T * (gap + 2.4f), T, R, 0.25f, half - 0.4f); stopLines++;
                    StopLine(new Vector3(C.x, C.y, C.z) + T * (gap + 2.4f), -T, R, -half + 0.4f, -0.25f); stopLines++;
                    // signals: forward traffic sees the one beyond the junction on its right kerb, oncoming traffic the one behind on the left kerb
                    foreach (float sgn in new[] { 1f, -1f })
                    {
                        float latSide = sgn;                              // +1 = right kerb (forward direction), -1 = left kerb
                        Vector3 pos = default; bool ok = false;
                        for (float extra = 0f; extra <= 6f && !ok; extra += 1.5f)
                        {
                            pos = C + T * (sgn * (m.half + 3.0f + extra)) + R * (latSide * (csh[ci] + 0.9f));
                            pos.y = cp[ci].y;
                            ok = PropOK(pos, 0.2f, 0.6f, 2.2f);
                        }
                        if (!ok) { skipped++; continue; }
                        Signal(pos, -R * latSide, -T * sgn, Mathf.Min(csh[ci] * 0.9f + 1.5f, 9f));
                        signals++;
                    }
                }
            // junction nodes where at least two class <= 2 roads meet
            for (int j = 0; j < net.junctions.Count; j++)
            {
                var arms = junctionArms[j];
                if (arms.Count < 3 || arms.Count(a => a.cls <= 2) < 2) continue;
                var node = new Vector3(net.junctions[j].x, net.junctions[j].y, net.junctions[j].z);
                if (NearCourseElevated(node)) continue;
                nodeJunctions++;
                foreach (var arm in arms)
                {
                    if (arm.cls > 2) continue;
                    var u = arm.dir;                      // away from the node
                    var travel = -u;                      // traffic approaching the node along this arm
                    var R = Right(travel);
                    float stopAt = arm.hw + 2.4f + 1.0f;
                    StopLine(node + u * (stopAt + 2.2f), travel, R, 0.25f, arm.hw - 0.3f); stopLines++;
                    Crosswalk(node + u * (arm.hw + 3.6f), travel, R, arm.hw * 2f - 0.8f); crosswalks++;
                    // signal on the far side of the junction, right of the approach, facing the approaching traffic
                    Vector3 pos = default; bool ok = false;
                    for (float extra = 0f; extra <= 6f && !ok; extra += 1.5f)
                    {
                        pos = node + travel * (9f + extra) + R * (arm.hw + 2.6f);
                        pos.y = node.y;
                        ok = PropOK(pos, 0.2f, 0.6f, 2.2f);
                    }
                    if (!ok) { skipped++; continue; }
                    Signal(pos, -R, -travel, Mathf.Min(arm.hw * 1.5f + 1f, 9f));
                    signals++;
                }
            }
            Log($"SIGNALS courseJunctions={courseJunctions} nodeJunctions={nodeJunctions} signals={signals} crosswalks={crosswalks} stopLines={stopLines} skippedByRules={skipped}");
        }

        static bool NearCourseElevated(Vector3 p)
        {
            int i = courseGrid.Nearest(p, 20f, out _);
            return i >= 0 && celev[i];
        }

        // ---------- tree and shrub clusters on green land and empty parcels ----------

        static float Hash2(float x, float z) { float s = Mathf.Sin(x * 12.9898f + z * 78.233f) * 43758.5453f; return s - Mathf.Floor(s); }
        static float ValueNoise(float x, float z)
        {
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            float fx = x - ix, fz = z - iz;
            fx = fx * fx * (3f - 2f * fx); fz = fz * fz * (3f - 2f * fz);
            return Mathf.Lerp(Mathf.Lerp(Hash2(ix, iz), Hash2(ix + 1, iz), fx), Mathf.Lerp(Hash2(ix, iz + 1), Hash2(ix + 1, iz + 1), fx), fz);
        }

        const int PlantingCap = 5500;

        static void BuildPlanting()
        {
            var rnd = new System.Random(515);
            var g = ground;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in cp) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z); }
            foreach (var road in net.roads)
                for (int k = 0; k < road.p.Count; k += 3) { minX = Mathf.Min(minX, road.p[k]); maxX = Mathf.Max(maxX, road.p[k]); minZ = Mathf.Min(minZ, road.p[k + 2]); maxZ = Mathf.Max(maxZ, road.p[k + 2]); }
            const float spacing = 7.5f, range = 150f;
            int placed = 0, trees = 0, shrubs = 0, onGreen = 0, onParcel = 0;
            var candidates = new List<(float dist, Vector3 pos, bool shrub, int cls)>();
            for (float x = minX - range; x <= maxX + range; x += spacing)
                for (float z = minZ - range; z <= maxZ + range; z += spacing)
                {
                    float jx = x + ((float)rnd.NextDouble() - 0.5f) * spacing * 0.9f, jz = z + ((float)rnd.NextDouble() - 0.5f) * spacing * 0.9f;
                    int cls = GroundClass(jx, jz);
                    if (cls == 1 || cls == 2) continue;                       // sea, beach
                    float cluster = Mathf.Clamp01((ValueNoise(jx / 38f + 3.1f, jz / 38f + 9.4f) - 0.42f) / 0.2f);
                    float chance = cls == 4 ? 0.85f : cls == 3 ? 0.55f * (0.35f + cluster) : 0.34f * cluster;   // forest, park, empty urban parcel
                    if (rnd.NextDouble() > chance) continue;
                    if (!roadSampleGrid.Any(new Vector3(jx, 0f, jz), range)) continue;
                    float h = GroundH(jx, jz);
                    if (h < 0.4f) continue;
                    var pos = new Vector3(jx, h, jz);
                    if (!PropOK(pos, 4.5f, 3.0f, 3f)) continue;               // 4.5 m beyond every road ribbon (sidewalk and kerb included), 3 m from buildings
                    courseGrid.Nearest(pos, 3000f, out float dc);
                    candidates.Add((dc, pos, rnd.NextDouble() < 0.32, cls));
                }
            // over the cap: keep the candidates nearest to the course, so the budget goes where the player drives
            foreach (var cnd in candidates.OrderBy(c => c.dist).Take(PlantingCap))
            {
                if (!(cnd.shrub ? PlantShrub(cnd.pos, rnd) : Tree(cnd.pos, rnd, 0.8f + (float)rnd.NextDouble() * 0.35f))) continue;   // crown would reach a road above it
                Record(cnd.shrub ? "shrub" : "plantedTree", cnd.pos);
                if (cnd.shrub) shrubs++; else trees++;
                if (cnd.cls == 3 || cnd.cls == 4) onGreen++; else onParcel++;
                placed++;
            }
            Log($"PLANTING candidates={candidates.Count} placed={placed} farthestPlantedFromCourse={(candidates.Count > 0 ? candidates.OrderBy(c => c.dist).Take(PlantingCap).Max(c => c.dist) : 0f):0}m trees={trees} shrubs={shrubs} onGreenLand={onGreen} onEmptyParcels={onParcel} cap={PlantingCap}");
        }

        // per-tree value and hue variation (fresh yellow-green to deep blue-green)
        static Color TreeTint(System.Random r)
        {
            float v = 0.82f + (float)r.NextDouble() * 0.30f, h = (float)r.NextDouble() - 0.5f;
            return new Color(v * (1f + 0.16f * h), v, v * (1f - 0.20f * h));
        }

        static bool PlantShrub(Vector3 basePos, System.Random r)
        {
            // low, wide bush: the tree card squashed to a third of the height
            float size = BusanSliceTrees.CardSize * (1.15f + (float)r.NextDouble() * 0.5f);
            float yaw = (float)r.NextDouble() * 180f;
            var tint = TreeTint(r);
            float cy = BusanSliceTrees.CardCenterY * (size / BusanSliceTrees.CardSize);
            if (!CrownOK("shrub", basePos, size * 0.5f, basePos.y + cy * 0.62f + size * 0.5f * 0.62f)) return false;
            var b = sink.B("Trees", "tree", basePos, true);
            b.NormalUp = 2.0f;
            b.Tint = tint;
            for (int k = 0; k < 2; k++)
            {
                var rot = Quaternion.Euler(0f, yaw + 90f * k, 0f);
                var along = rot * Vector3.right * (size * 0.5f);
                var up = Vector3.up * (size * 0.5f * 0.62f);
                var center = basePos + Vector3.up * (cy * 0.62f);
                float u0 = k * 0.5f, u1 = u0 + 0.5f;
                b.QuadBothUV(center - along - up, center + along - up, center + along + up, center - along + up, rot * Vector3.forward,
                    new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f));
            }
            return true;
        }
    }
}
