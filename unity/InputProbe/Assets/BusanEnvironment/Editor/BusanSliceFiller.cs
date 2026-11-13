using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // Filler blocks: urban ground that has neither a road nor an OSM building (mostly beyond the 520 m OSM cache, where the map
    // read as an empty concrete field) gets estimated buildings on a jittered 34 m grid, turned to the nearest road. They are
    // design values like the other infill: not real buildings, and never on a road, a sidewalk, the course or another building.
    public static partial class BusanSliceBuilder
    {
        static void AddFillerBuildings()
        {
            if (!UseSumo) return;
            var rnd = new System.Random(3417);
            var existing = new BuildingIndex();
            foreach (var d in bld.buildings)
            {
                int n = d.p.Count / 2;
                if (n < 3) continue;
                var ring = new Vector2[n];
                for (int i = 0; i < n; i++) ring[i] = new Vector2(d.p[2 * i], d.p[2 * i + 1]);
                existing.Add(ring);
            }
            // road directions from the generated network and the course
            var dirPts = new List<Vector3>(); var dirs = new List<Vector3>();
            foreach (var road in net.roads)
            {
                int cnt = road.p.Count / 3;
                for (int i = 0; i < cnt - 1; i++)
                {
                    var a = new Vector3(road.p[i * 3], 0f, road.p[i * 3 + 2]); var b = new Vector3(road.p[i * 3 + 3], 0f, road.p[i * 3 + 5]);
                    dirPts.Add((a + b) * 0.5f); dirs.Add((b - a).normalized);
                }
            }
            for (int i = 0; i < cp.Length - 1; i++) { dirPts.Add(Flat(cp[i] + cp[i + 1]) * 0.5f); dirs.Add(Flat(cp[i + 1] - cp[i]).normalized); }
            var dirGrid = new PointGrid(dirPts.ToArray(), 60f);

            var g = ground;
            float x0 = g.x0, z0 = g.z0, x1 = g.x0 + (g.cols - 1) * g.step, z1 = g.z0 + (g.rows - 1) * g.step;
            const float cell = 34f;
            int placed = 0, tall = 0;
            var placedRings = new BuildingIndex();
            AddStreetRows(existing, placedRings);
            for (float z = z0 + 20f; z < z1 - 20f; z += cell)
                for (float x = x0 + 20f; x < x1 - 20f; x += cell)
                {
                    var c = new Vector3(x + ((float)rnd.NextDouble() - 0.5f) * 10f, 0f, z + ((float)rnd.NextDouble() - 0.5f) * 10f);
                    if (GroundClass(c.x, c.z) != 0) continue;
                    courseGrid.Nearest(c, 40f, out var dCourse);
                    if (dCourse < 40f) continue;
                    int di = dirGrid.Nearest(c, 400f, out float dRoad);
                    if (di < 0) continue;
                    var u = dirs[di]; var v = Right(u);
                    float w = 12f + (float)rnd.NextDouble() * 12f, dep = 10f + (float)rnd.NextDouble() * 9f;
                    // keep 2.5 m from every carriageway and sidewalk (Korean blocks stand at the street; the frontage paving
                    // between them is not a road): sample the footprint grown by 2.5 m, every 2 m or closer
                    bool clear = true;
                    const float clearance = 2.5f;
                    int nx = Mathf.CeilToInt((w + 2f * clearance) / 2f), nz = Mathf.CeilToInt((dep + 2f * clearance) / 2f);
                    for (int sx = 0; sx <= nx && clear; sx++)
                        for (int sz = 0; sz <= nz && clear; sz++)
                        {
                            var q = c + u * ((sx / (float)nx - 0.5f) * (w + 2f * clearance)) + v * ((sz / (float)nz - 0.5f) * (dep + 2f * clearance));
                            if (sumoPavedArea.Contains(q.x, q.z) || InSumoJunction(q, 2f) || GroundClass(q.x, q.z) != 0) clear = false;
                            else if (existing.Near(q.x, q.z, 3f) || placedRings.Near(q.x, q.z, 3f)) clear = false;
                        }
                    if (!clear) continue;
                    var corners = new[] { c - u * (w / 2) - v * (dep / 2), c + u * (w / 2) - v * (dep / 2), c + u * (w / 2) + v * (dep / 2), c - u * (w / 2) + v * (dep / 2) };
                    float gmin = float.MaxValue, gmax = float.MinValue;
                    for (int k = 0; k < 4; k++)
                        for (float t = 0f; t < 1f; t += 0.25f)
                        {
                            var q = Vector3.Lerp(corners[k], corners[(k + 1) % 4], t);
                            float gh = GroundH(q.x, q.z); gmin = Mathf.Min(gmin, gh); gmax = Mathf.Max(gmax, gh);
                        }
                    if (gmax - gmin > 6f) continue;   // too steep for a block: leave the hillside open
                    // backdrop: towers in clusters further out, villas and small shops elsewhere
                    float towerChance = dRoad > 25f ? 0.35f : 0.15f;
                    string style; float h;
                    double pick = rnd.NextDouble();
                    if (pick < towerChance) { style = "apartment"; h = 36f + (float)rnd.NextDouble() * 40f; tall++; }
                    else if (pick < towerChance + 0.15) { style = "shop"; h = 10f + (float)rnd.NextDouble() * 6f; }
                    else { style = new[] { "villa_brick", "villa_ivory", "villa_gray", "brick", "plaster" }[rnd.Next(5)]; h = 9f + (float)rnd.NextDouble() * 9f; }
                    var p = new List<float>();
                    foreach (var q in corners) { p.Add(q.x); p.Add(q.z); }
                    bld.buildings.Add(new BuildingData { h = h, b = gmin - 0.5f, de = 0f, real = 0, e = 0, infill = 0, st = style, p = p });
                    placedRings.Add(corners.Select(q => new Vector2(q.x, q.z)).ToArray());
                    placed++;
                }
            Log($"FILLER placed={placed} towers={tall} (estimated blocks on empty urban ground, 40 m+ from the course, 2.5 m+ from roads)");
        }

        // Back streets in 광안동/민락동 are lined wall to wall with 3-5 storey villas (붉은 벽돌, 흰 도장, 1층 필로티 주차), 0.5-1.5 m
        // behind the asphalt edge (Kakao roadview, 2025-2026). Where OSM has no building there, a row of estimated villas is placed
        // along residential streets, living streets and alleys within 320 m of the course (what the drive can see), never on paving,
        // a junction, a park, the sea or within 1 m of another building. Design values, like the other infill.
        static void AddStreetRows(BuildingIndex existing, BuildingIndex placedRings)
        {
            var rnd = new System.Random(5521);
            string[] villaStyles = { "villa_brick", "villa_ivory", "villa_gray", "villa_brick", "plaster", "brick" };
            int rows = 0, pilotis = 0, tried = 0;
            const int cap = 4000;
            const float reach = 700f;   // the OSM cache reaches 520 m from the course; beyond, the grid filler takes over
            foreach (var road in net.roads)
            {
                if (rows >= cap) break;
                if (road.L != 0 || !(road.cls == 3 || road.cls == 4 || (road.cls == 6 && road.w >= 4.5f))) continue;
                int cnt = road.p.Count / 3;
                if (cnt < 2) continue;
                var pts = new Vector3[cnt];
                for (int i = 0; i < cnt; i++) pts[i] = new Vector3(road.p[i * 3], road.p[i * 3 + 1], road.p[i * 3 + 2]);
                var cum = new float[cnt];
                for (int i = 1; i < cnt; i++) cum[i] = cum[i - 1] + Vector3.Distance(Flat(pts[i - 1]), Flat(pts[i]));
                float total = cum[cnt - 1];
                if (total < 14f) continue;
                float hw = road.w * 0.5f;
                foreach (float side in new[] { 1f, -1f })
                {
                    float s = 2f + (float)rnd.NextDouble() * 3f;
                    while (s < total - 6f && rows < cap)
                    {
                        float W = 8f + (float)rnd.NextDouble() * 7f, D = 9f + (float)rnd.NextDouble() * 5f;
                        float sm = Mathf.Min(s + W * 0.5f, total - 0.01f);
                        int i = 0;
                        while (i < cnt - 2 && cum[i + 1] < sm) i++;
                        float t = (sm - cum[i]) / Mathf.Max(cum[i + 1] - cum[i], 1e-4f);
                        var on = Vector3.Lerp(pts[i], pts[i + 1], t);
                        var u = Flat(pts[i + 1] - pts[i]).normalized;
                        if (u.sqrMagnitude < 0.5f) { s += 3f; continue; }   // repeated point in the polyline
                        var v = Right(u) * side;   // from the street toward the lot
                        float setback = hw + 0.6f + (float)rnd.NextDouble() * 0.9f;
                        var front = Flat(on) + v * setback;
                        var c = front + v * (D * 0.5f);
                        tried++;
                        courseGrid.Nearest(c, reach + 10f, out var dCourse);
                        if (dCourse > reach || dCourse < 18f) { s += 6f; continue; }
                        bool clear = GroundClass(c.x, c.z) == 0;
                        int nx = Mathf.CeilToInt(W / 2f), nz = Mathf.CeilToInt(D / 2f);
                        for (int sx = 0; sx <= nx && clear; sx++)
                            for (int sz = 0; sz <= nz && clear; sz++)
                            {
                                var q = front + u * ((sx / (float)nx - 0.5f) * (W + 0.6f)) + v * (sz / (float)nz * (D + 0.3f) - 0.15f);
                                if (sumoPavedArea.Contains(q.x, q.z) || InSumoJunction(q, 1f) || GroundClass(q.x, q.z) != 0) clear = false;
                                else if (existing.Near(q.x, q.z, 1f) || placedRings.Near(q.x, q.z, 0.6f)) clear = false;
                            }
                        if (!clear) { s += 3f; continue; }
                        // front-left, front-right, back-right, back-left; edge 0 faces the street
                        var corners = new[] { front - u * (W / 2), front + u * (W / 2), front + u * (W / 2) + v * D, front - u * (W / 2) + v * D };
                        float gmin = float.MaxValue, gmax = float.MinValue;
                        for (int k = 0; k < 4; k++)
                            for (float tt = 0f; tt < 1f; tt += 0.25f)
                            {
                                var q = Vector3.Lerp(corners[k], corners[(k + 1) % 4], tt);
                                float gh = GroundH(q.x, q.z); gmin = Mathf.Min(gmin, gh); gmax = Mathf.Max(gmax, gh);
                            }
                        if (gmax - gmin > 4.5f) { s += 3f; continue; }
                        int floors = 3 + rnd.Next(3);
                        bool openGround = rnd.NextDouble() < 0.45;   // 1층 필로티 주차장
                        string style = openGround ? "villa_pilotis" : villaStyles[rnd.Next(villaStyles.Length)];
                        float h = floors * 2.9f + (openGround ? 2.8f : 0f) + 0.6f;
                        var p = new List<float>();
                        foreach (var q in corners) { p.Add(q.x); p.Add(q.z); }
                        bld.buildings.Add(new BuildingData { h = h, b = Mathf.Min(gmin, on.y) - 0.5f, de = 0f, real = 0, e = 0, infill = 0, st = style, p = p });
                        placedRings.Add(corners.Select(q => new Vector2(q.x, q.z)).ToArray());
                        rows++;
                        if (openGround) pilotis++;
                        s += W + 0.8f + (float)rnd.NextDouble() * 1.4f;
                    }
                }
            }
            Log($"STREETROWS villas={rows} pilotis={pilotis} tried={tried} (estimated villas lining back streets within {reach:0} m of the course)");
        }
    }
}
