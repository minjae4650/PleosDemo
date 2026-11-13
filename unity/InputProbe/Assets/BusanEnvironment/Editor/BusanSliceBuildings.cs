using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    public static partial class BusanSliceBuilder
    {
        const int SignCols = 4, SignRows = 8, BladeCols = 8, BladeRows = 2;
        static Color curTint = Color.white;
        static readonly Color[] FacadeTints =
        {
            new Color(1f, 1f, 1f), new Color(1.06f, 0.99f, 0.90f), new Color(0.90f, 0.96f, 1.05f), new Color(0.86f, 0.86f, 0.88f),
            new Color(1.10f, 1.04f, 0.94f), new Color(0.94f, 0.88f, 0.82f), new Color(0.82f, 0.84f, 0.80f),
        };
        static readonly Color[] GlassTints =
        {
            new Color(1f, 1f, 1f), new Color(0.80f, 0.95f, 1.1f), new Color(0.78f, 1.0f, 0.95f), new Color(0.9f, 0.9f, 0.92f), new Color(1.1f, 0.95f, 0.8f),
        };

        static void BuildBuildings()
        {
            var rnd = new System.Random(8181);
            int count = 0, signs = 0, roofFail = 0;
            plinthRaised = 0; plinthTotal = 0f;
            wallEdges = 0; wallGaps = 0;
            int onCarriageway = 0;
            for (int bi = 0; bi < bld.buildings.Count; bi++)
            {
                var d = bld.buildings[bi];
                int n = d.p.Count / 2;
                if (n < 3) continue;
                var ring = new Vector3[n];
                for (int i = 0; i < n; i++) ring[i] = new Vector3(d.p[2 * i], 0f, d.p[2 * i + 1]);
                // an OSM footprint that reaches more than 0.8 m into the SUMO carriageway (a shop wall standing in a lane) is left out
                if (UseSumo && IntrudesCarriageway(ring, 0.8f)) { onCarriageway++; continue; }
                float area = 0f;
                for (int i = 0; i < n; i++) { var a = ring[i]; var b = ring[(i + 1) % n]; area += a.x * b.z - b.x * a.z; }
                int facing = d.e;
                if (area < 0f)
                {
                    Array.Reverse(ring);
                    facing = ((n - 2 - d.e) % n + n) % n;
                }
                // Hillside buildings: the facade (ground floor, shop fronts) starts at the highest ground under the footprint, and the
                // drop on the downhill side is a concrete plinth from the old base. Before, the facade started at the lowest corner and
                // the uphill ground buried up to 9 m of it (1299 of 3066 buildings by more than 1 m).
                float gMax = float.MinValue;   // corners and every 4 m along the walls (a ridge between corners would bury the wall)
                for (int i = 0; i < n; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % n];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 4f));
                    for (int k = 0; k < steps; k++) { var q = Vector3.Lerp(a, b, k / (float)steps); gMax = Mathf.Max(gMax, GroundH(q.x, q.z)); }
                }
                float y0 = d.b, H = d.h;
                float floor = Mathf.Max(y0 + 0.5f, gMax + 0.12f);
                float top = floor + H;
                plinthTotal += floor - (y0 + 0.5f);
                if (floor - (y0 + 0.5f) > 1f) plinthRaised++;
                string style = d.st;
                {
                    var palette = style == "glass" ? GlassTints : FacadeTints;
                    float jitter = 0.93f + (float)rnd.NextDouble() * 0.14f;
                    var tint = palette[rnd.Next(palette.Length)];
                    curTint = new Color(tint.r * jitter, tint.g * jitter, tint.b * jitter);
                }
                if (d.infill != 0)
                {
                    // face the course: the edge whose midpoint is nearest to it
                    float best = float.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        var mid = (ring[i] + ring[(i + 1) % n]) * 0.5f;
                        courseGrid.Nearest(mid, 200f, out var dd);
                        if (dd < best) { best = dd; facing = i; }
                    }
                }

                // 필로티 빌라: the ground floor is an open parking bay on the street side, the villa starts one storey up
                bool pil = style == "villa_pilotis";
                float y1 = pil ? floor + PilotisH : floor;
                string bodyStyle = pil ? PilotisBody[bi % PilotisBody.Length] : style;
                for (int i = 0; i < n; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % n];
                    var e = b - a;
                    if (e.magnitude < 0.05f) continue;
                    var outward = new Vector3(e.z, 0f, -e.x).normalized;
                    if (floor > y0 + 0.05f) Band("plinth", a, b, y0, floor, outward, 0f, e.magnitude / 3f, 0f, (floor - y0) / 3f);
                    if (pil && i != facing) Band("plinth", a, b, floor, y1, outward, 0f, e.magnitude / 3f, 0f, PilotisH / 3f);
                    FacadeWall(a, b, y1, top - y1, bodyStyle, outward);
                    // contact darkening: 3 cm in front of the wall, darkest at the ground under each end of the wall and fading out
                    // 1.5 m above it. (It used to stay darkest from the building base up to the floor: on a hillside that painted a
                    // black band 2-3 m tall over the whole plinth.)
                    {
                        var off = outward * 0.03f;
                        float ga = Mathf.Min(GroundH(a.x, a.z), floor) - 0.05f, gb = Mathf.Min(GroundH(b.x, b.z), floor) - 0.05f;
                        var ao = sink.B("WallAO", "wallAO", (a + b) * 0.5f);
                        ao.Quad(new Vector3(a.x, ga, a.z) + off, new Vector3(b.x, gb, b.z) + off, new Vector3(b.x, gb + 1.5f, b.z) + off, new Vector3(a.x, ga + 1.5f, a.z) + off, outward,
                            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
                    }
                    // parapet
                    var chunk = (a + b) * 0.5f;
                    var aT = new Vector3(a.x, top, a.z); var bT = new Vector3(b.x, top, b.z);
                    var up = Vector3.up * 0.9f;
                    var pb = sink.B("Parapet", "plain", chunk);
                    pb.Quad(aT, bT, bT + up, aT + up, outward, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    pb.Quad(aT, bT, bT + up, aT + up, -outward, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                }
                if (pil) PilotisBay(ring, facing, floor, y1);
                var roofBuilder = sink.B("Roofs", "roof", ring[0]);
                if (!Roof(roofBuilder, ring, top)) roofFail++;

                if (style == "shop")
                {
                    var fm = (ring[facing] + ring[(facing + 1) % n]) * 0.5f;
                    courseGrid.Nearest(fm, 40f, out var dCourse);
                    signs += ShopSigns(ring, facing, n, floor, Mathf.Min(ShopH, top - floor), rnd, dCourse < 30f || d.infill != 0, top - floor < 40f);
                }
                count++;
            }
            sink.CastShadows["WallAO"] = false;
            Log($"BUILDINGS meshed={count} signs={signs} roofFailures={roofFail} plinthOver1m={plinthRaised} meanPlinthRaise={(count > 0 ? plinthTotal / count : 0f):0.00}m leftOutOnCarriageway={onCarriageway}");
            Log($"WALLCHECK edges={wallEdges} gaps={wallGaps} (wall top must equal parapet base on every edge)");
        }

        // Fills the blank sign band of the shop facade (about 3.1-3.8 m above the pavement) with 2.8 m
        // boards, leaving the odd gap, and adds blade signs at the corners of course-side shops.
        static int ShopSigns(Vector3[] ring, int facing, int n, float groundY, float shopHeight, System.Random rnd, bool courseSide, bool lowRise)
        {
            var a = ring[facing]; var b = ring[(facing + 1) % n];
            var e = b - a; float len = e.magnitude;
            if (len < 4f || !lowRise) return 0;
            var dir = e / len;
            var outward = new Vector3(e.z, 0f, -e.x).normalized;
            float yc = groundY + 3.45f * shopHeight / ShopH;
            float h = 0.7f * shopHeight / ShopH, w = 2.8f;
            int placed = 0;
            for (float x = 0.5f; x + w <= len - 0.3f; x += w + 0.4f + (float)rnd.NextDouble() * 0.9f)
            {
                if (rnd.NextDouble() < 0.15) continue;
                int cell = rnd.Next(SignCols * SignRows);
                float u0 = (cell % SignCols) / (float)SignCols, v1 = 1f - (cell / SignCols) / (float)SignRows;
                float u1 = u0 + 1f / SignCols, v0 = v1 - 1f / SignRows;
                var c = a + dir * (x + w * 0.5f) + outward * 0.1f;
                var sb = sink.B("Signs", "signs", c);
                sb.Quad(c - dir * (w * 0.5f) + Vector3.up * (yc - h * 0.5f), c + dir * (w * 0.5f) + Vector3.up * (yc - h * 0.5f),
                    c + dir * (w * 0.5f) + Vector3.up * (yc + h * 0.5f), c - dir * (w * 0.5f) + Vector3.up * (yc + h * 0.5f), outward,
                    new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
                placed++;
            }
            if (courseSide)
                foreach (var t in new[] { 0.4f, len - 0.4f })
                {
                    if (rnd.NextDouble() < 0.25) continue;
                    int cell = rnd.Next(BladeCols * BladeRows);
                    float u0 = (cell % BladeCols) / (float)BladeCols, v1 = 1f - (cell / BladeCols) / (float)BladeRows;
                    float u1 = u0 + 1f / BladeCols, v0 = v1 - 1f / BladeRows;
                    var baseP = a + dir * t + outward * 0.05f + Vector3.up * (groundY + 4.4f + (float)rnd.NextDouble() * 1.2f);
                    var tip = baseP + outward * 0.75f;
                    var bb = sink.B("SignsBlade", "signsv", baseP);
                    // the face looking along -dir reads left-to-right base->tip, the other one tip->base: mirror U there
                    bb.Quad(baseP, tip, tip + Vector3.up * 3f, baseP + Vector3.up * 3f, -dir,
                        new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
                    bb.Quad(baseP, tip, tip + Vector3.up * 3f, baseP + Vector3.up * 3f, dir,
                        new Vector2(u1, v0), new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1));
                    placed++;
                }
            return placed;
        }

        // True when a point of the footprint at least `depth` inside its walls lies on the carriageway (grid every 1 m).
        static bool IntrudesCarriageway(Vector3[] ring, float depth)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var q in ring) { x0 = Mathf.Min(x0, q.x); x1 = Mathf.Max(x1, q.x); z0 = Mathf.Min(z0, q.z); z1 = Mathf.Max(z1, q.z); }
            if ((x1 - x0) * (z1 - z0) > 40000f) return false;   // very large footprints (estates): not checked point by point
            int n = ring.Length;
            for (float x = x0 + 0.5f; x < x1; x += 1f)
                for (float z = z0 + 0.5f; z < z1; z += 1f)
                {
                    if (!sumoRoadArea.Contains(x, z)) continue;
                    bool inside = false; float best = float.MaxValue;
                    for (int a = 0, c = n - 1; a < n; c = a++)
                    {
                        if (((ring[a].z > z) != (ring[c].z > z)) && (x < (ring[c].x - ring[a].x) * (z - ring[a].z) / (ring[c].z - ring[a].z) + ring[a].x)) inside = !inside;
                        float dx = ring[c].x - ring[a].x, dz = ring[c].z - ring[a].z, len2 = dx * dx + dz * dz;
                        float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(((x - ring[a].x) * dx + (z - ring[a].z) * dz) / len2);
                        float px = ring[a].x + dx * t - x, pz = ring[a].z + dz * t - z;
                        best = Mathf.Min(best, px * px + pz * pz);
                    }
                    if (inside && best >= depth * depth) return true;
                }
            return false;
        }

        const float PilotisH = 2.8f;
        static readonly string[] PilotisBody = { "villa_ivory", "villa_brick", "villa_gray", "plaster" };

        // The open ground floor of a pilotis villa: a dark parking bay as deep as half the footprint (at most 5 m) under the
        // street-side edge, with its back and side walls, the slab underside and three square columns along the opening.
        static void PilotisBay(Vector3[] ring, int facing, float y0, float y1)
        {
            int n = ring.Length;
            var a = ring[facing]; var b = ring[(facing + 1) % n];
            var e = b - a; float L = e.magnitude;
            if (L < 3f) return;
            var dir = e / L;
            var outward = new Vector3(e.z, 0f, -e.x).normalized;
            var inward = -outward;
            float maxD = 0f;
            foreach (var q in ring) maxD = Mathf.Max(maxD, Vector3.Dot(q - a, inward));
            float depth = Mathf.Min(5f, maxD * 0.5f);
            if (depth < 1.5f) return;
            Vector3 P(Vector3 q, float y) => new Vector3(q.x, y, q.z);
            var a2 = a + inward * depth; var b2 = b + inward * depth;
            var wb = sink.B("Walls", "plinth", (a + b) * 0.5f, true);
            wb.Tint = new Color(0.42f, 0.42f, 0.43f);
            wb.Quad(P(a2, y0), P(b2, y0), P(b2, y1), P(a2, y1), outward, Vector2.zero, new Vector2(L / 3f, 0f), new Vector2(L / 3f, 1f), new Vector2(0f, 1f));
            wb.Quad(P(a, y0), P(a2, y0), P(a2, y1), P(a, y1), dir, Vector2.zero, new Vector2(depth / 3f, 0f), new Vector2(depth / 3f, 1f), new Vector2(0f, 1f));
            wb.Quad(P(b, y0), P(b2, y0), P(b2, y1), P(b, y1), -dir, Vector2.zero, new Vector2(depth / 3f, 0f), new Vector2(depth / 3f, 1f), new Vector2(0f, 1f));
            var ceil = sink.B("Walls", "plinth", (a + b) * 0.5f, true);
            ceil.Tint = new Color(0.55f, 0.55f, 0.56f);
            ceil.Quad(P(a, y1), P(b, y1), P(b2, y1), P(a2, y1), Vector3.down);
            // the slab of the bay (on a slope the floor sits above the terrain, which would show under the sill)
            ceil.Quad(P(a, y0), P(b, y0), P(b2, y0), P(a2, y0), Vector3.up);
            var col = sink.B("Walls", "plinth", (a + b) * 0.5f, true);
            col.Tint = new Color(0.8f, 0.8f, 0.8f);
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            foreach (float t in new[] { 0.2f, L * 0.5f, L - 0.2f })
                col.Box(P(a + dir * t + inward * 0.2f, (y0 + y1) * 0.5f), new Vector3(0.4f, y1 - y0, 0.4f), yaw);
        }

        static float lastBandTop;
        static int wallEdges, wallGaps, plinthRaised;
        static float plinthTotal;

        static void Band(string mat, Vector3 a, Vector3 b, float yA, float yB, Vector3 outward, float u0, float u1, float v0, float v1)
        {
            lastBandTop = Mathf.Max(lastBandTop, yB);
            var mid = (a + b) * 0.5f;
            var wb = sink.B("Walls", mat, mid, true);
            wb.Tint = curTint;
            wb.Quad(new Vector3(a.x, yA, a.z), new Vector3(b.x, yA, b.z), new Vector3(b.x, yB, b.z), new Vector3(a.x, yB, a.z), outward,
                new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
        }

        // Counts of bays and floors are rounded so windows are never cut; the small stretch that
        // results is spread over the wall.
        static void FacadeWall(Vector3 a, Vector3 b, float y0, float H, string style, Vector3 outward)
        {
            float L = Vector3.Distance(a, b);
            lastBandTop = float.MinValue;
            FacadeWallCore(a, b, y0, H, style, outward, L);
            wallEdges++;
            if (Mathf.Abs(lastBandTop - (y0 + H)) > 0.02f)
            {
                wallGaps++;
                if (wallGaps <= 5) Log($"WALLCHECK gap style={style} at ({a.x:0},{a.z:0}) wallTop={lastBandTop:0.00} expected={y0 + H:0.00}");
            }
        }

        static void FacadeWallCore(Vector3 a, Vector3 b, float y0, float H, string style, Vector3 outward, float L)
        {
            if (L < 3.2f) { Band("plain", a, b, y0, y0 + H, outward, 0f, 1f, 0f, 1f); return; }
            switch (style)
            {
                case "apartment":
                {
                    int bays = Mathf.Max(2, Mathf.RoundToInt(L / AptBayW));
                    float u = bays / 6f;
                    float gh = H > 14f ? 2f * AptFloorH : 0f;
                    if (gh > 0f) Band("apt_g", a, b, y0, y0 + gh, outward, 0f, u, 0f, 1f);
                    int tiles = Mathf.Max(1, Mathf.RoundToInt((H - gh) / (4f * AptFloorH)));
                    Band("apt_m", a, b, y0 + gh, y0 + H, outward, 0f, u, 0f, tiles);
                    break;
                }
                case "glass":
                {
                    int tilesU = Mathf.Max(1, Mathf.RoundToInt(L / 9f));
                    int tilesV = Mathf.Max(1, Mathf.RoundToInt(H / 7.8f));
                    Band("glass", a, b, y0, y0 + H, outward, 0f, tilesU, 0f, tilesV);
                    break;
                }
                case "shop":
                {
                    int bays = Mathf.Max(2, Mathf.RoundToInt(L / ShopBayW));
                    float gh = H - ShopH <= 2.5f ? H : ShopH; // a short remainder would leave an open gap under the parapet
                    Band("shop_g", a, b, y0, y0 + gh, outward, 0f, bays / 5f, 0f, 1f);
                    if (H - gh > 2.5f)
                    {
                        int n = Mathf.Max(1, Mathf.RoundToInt(L / VillaTile));
                        int tiles = Mathf.Max(1, Mathf.RoundToInt((H - gh) / 6f));
                        Band("villa_m", a, b, y0 + gh, y0 + H, outward, 0f, n, 0f, tiles);
                    }
                    break;
                }
                default:
                {
                    int n = Mathf.Max(1, Mathf.RoundToInt(L / VillaTile));
                    int tiles = Mathf.Max(1, Mathf.RoundToInt(H / 12f));
                    var mat = style == "villa_brick" || style == "villa_gray" || style == "villa_ivory" || style == "brick" || style == "plaster" ? style : "plain";
                    Band(mat, a, b, y0, y0 + H, outward, 0f, n, 0f, tiles);
                    break;
                }
            }
        }

        static float Cross2(Vector3 a, Vector3 b, Vector3 c) => (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);

        // Ear clipping for a counter-clockwise (x,z) ring. False when the ring is degenerate.
        static bool Roof(MeshBuilder b, Vector3[] ring, float y)
        {
            var ids = new List<int>();
            for (int i = 0; i < ring.Length; i++) ids.Add(i);
            int guard = 0;
            while (ids.Count > 2 && guard++ < ring.Length * ring.Length + 8)
            {
                bool found = false;
                for (int i = 0; i < ids.Count; i++)
                {
                    int a = ids[(i + ids.Count - 1) % ids.Count], mid = ids[i], c = ids[(i + 1) % ids.Count];
                    if (Cross2(ring[a], ring[mid], ring[c]) <= 1e-4f) continue;
                    bool inside = false;
                    foreach (int k in ids)
                    {
                        if (k == a || k == mid || k == c) continue;
                        if (Cross2(ring[a], ring[mid], ring[k]) >= 0f && Cross2(ring[mid], ring[c], ring[k]) >= 0f && Cross2(ring[c], ring[a], ring[k]) >= 0f) { inside = true; break; }
                    }
                    if (inside) continue;
                    Vector3 P(int idx) => new Vector3(ring[idx].x, y, ring[idx].z);
                    b.Tri(P(a), P(mid), P(c), Vector3.up, new Vector2(ring[a].x, ring[a].z) / 6f, new Vector2(ring[mid].x, ring[mid].z) / 6f, new Vector2(ring[c].x, ring[c].z) / 6f);
                    ids.RemoveAt(i);
                    found = true;
                    break;
                }
                if (!found) return false;
            }
            return true;
        }
    }
}
