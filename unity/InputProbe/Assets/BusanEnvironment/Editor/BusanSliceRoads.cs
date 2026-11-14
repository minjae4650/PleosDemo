using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    public static partial class BusanSliceBuilder
    {
        // Kerb face (absolute metres, 4 cm chamfer) and sidewalk top (starts 4 cm behind the kerb line, x scaled by the sidewalk width) are separate
        // meshes: the face is its own light grey material with sky-biased normals (see MeshBuilder.NormalUp).
        static readonly Vector2[] KerbFace = { new Vector2(0f, -0.05f), new Vector2(0f, 0.12f), new Vector2(0.04f, 0.15f) };
        static readonly Vector2[] SidewalkTop = { new Vector2(0f, 0.15f), new Vector2(1f, 0.15f), new Vector2(1f, -0.55f) };   // skirt reaches below the lowered ground, no dark gap
        static readonly Vector2[] TactileStrip = { new Vector2(0.45f, 0.158f), new Vector2(0.75f, 0.158f) };
        const float TactileUv = 0.3f;

        static void KerbSweep(Vector3[] p, Vector3[] r, float[] edge, float side, Func<int, bool> keep, float[] width, bool tactile)
        {
            var kerb = sink.Single("Kerb", "kerb");
            kerb.NormalUp = 0.6f;
            Sweep(kerb, p, r, edge, side, KerbFace, keep, 3f);
            var topEdge = new float[p.Length]; var topWidth = new float[p.Length];
            for (int i = 0; i < p.Length; i++) { topEdge[i] = edge[i] + 0.04f; topWidth[i] = Mathf.Max(0.1f, width[i] - 0.04f); }
            var swb = sink.Single("Sidewalk", "sidewalk");
            Sweep(swb, p, r, topEdge, side, SidewalkTop, keep, 3f, 0f, topWidth);
            // end caps where a run of sidewalk stops (mouths, junction corners): the open end was a bare hollow slab
            for (int i = 0; i < p.Length - 1; i++)
            {
                if (keep != null && (!keep(i) || !keep(i + 1))) continue;
                bool start = i == 0 || (keep != null && (!keep(i - 1)));
                bool end = i + 2 >= p.Length || (keep != null && (!keep(i + 2)));
                if (!start && !end) continue;
                foreach (int at in start && end ? new[] { i, i + 1 } : start ? new[] { i } : new[] { i + 1 })
                {
                    bool isStart = at == i && start;
                    var tan = (p[Mathf.Min(at + 1, p.Length - 1)] - p[Mathf.Max(at - 1, 0)]); tan.y = 0f; tan.Normalize();
                    Vector3 At(float outward, float up) => p[at] + r[at] * (side * (topEdge[at] + outward)) + Vector3.up * up;
                    // dropped kerb: the sidewalk ramps down over 2 m to road level at the kerb line and to the ground at its outer edge,
                    // so a run end has no vertical face (the bare end face was 0.15 m kerb + the ground step, about 0.4 m)
                    var f = isStart ? -tan : tan;
                    var kTop = At(0f, 0.15f); var oTop = At(topWidth[at], 0.15f);
                    var k0 = At(0f, 0f); var o0 = At(topWidth[at], -0.25f);
                    var kT = k0 + f * 2f; var oT = o0 + f * 2f;
                    swb.Quad(kTop, oTop, oT, kT, Vector3.up);
                    swb.Tri(kTop, k0, kT, -r[at] * side, Vector2.zero, Vector2.right, Vector2.up);
                    swb.Tri(oTop, o0, oT, r[at] * side, Vector2.zero, Vector2.right, Vector2.up);
                }
            }
            if (tactile) Sweep(sink.Single("Tactile", "tactile"), p, r, edge, side, TactileStrip, keep, TactileUv);
        }
        static readonly Vector2[] JerseyProfile =
            { new Vector2(-0.55f, 0f), new Vector2(-0.55f, 0.25f), new Vector2(-0.3f, 0.7f), new Vector2(-0.1f, 0.85f), new Vector2(0f, 0.85f), new Vector2(0f, 0f) };
        static readonly Vector2[] RailProfile =
            { new Vector2(-0.2f, 0.85f), new Vector2(-0.2f, 1.25f), new Vector2(-0.05f, 1.25f), new Vector2(-0.05f, 0.85f), new Vector2(-0.2f, 0.85f) };
        static readonly Vector2[] FasciaProfile = { new Vector2(0f, 0f), new Vector2(0f, -1.0f), new Vector2(-1.6f, -2.2f) };
        static readonly Vector2[] FenceRailLow = { new Vector2(-0.02f, 0.56f), new Vector2(-0.02f, 0.605f), new Vector2(0.02f, 0.605f), new Vector2(0.02f, 0.56f), new Vector2(-0.02f, 0.56f) };
        static readonly Vector2[] FenceRailHigh = { new Vector2(-0.02f, 0.86f), new Vector2(-0.02f, 0.905f), new Vector2(0.02f, 0.905f), new Vector2(0.02f, 0.86f), new Vector2(-0.02f, 0.86f) };

        static bool SeaSide(int i, float side)
        {
            var q = cp[i] + cr[i] * (side * (csh[i] + 12f));
            int c = GroundClass(q.x, q.z);
            return c == 1 || c == 2;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        static float[] Fill(int n, float v) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }

        static void BuildCourseSurface()
        {
            int n = cp.Length;
            // Sharp vertices (> 12 deg) use per-segment edge normals and get a corner wedge on the outer side,
            // so a hairpin never folds the ribbon or leaves a hole (a mitred normal does both).
            Func<int, Vector3> segRight = i => Right(cp[i + 1] - cp[i]);
            Func<int, bool> sharp = i => i > 0 && i < n - 1 && Vector3.Angle(Flat(cp[i] - cp[i - 1]), Flat(cp[i + 1] - cp[i])) > 12f;
            // with the SUMO network the ground-level course is part of that network; only the elevated course stays a ribbon
            // (one segment of overlap at each ramp foot, same material and world UV, so the seam does not show)
            Func<int, bool> ribbonSeg = i => !UseSumo || celev[i] || celev[i + 1] || ckind[i] == Kbridge || ckind[i + 1] == Kbridge;
            for (int i = 0; i < n - 1; i++)
            {
                if (!ribbonSeg(i)) continue;
                var r0 = sharp(i) ? segRight(i) : cr[i];
                var r1 = sharp(i + 1) ? segRight(i) : cr[i + 1];
                var a = cp[i] - r0 * csh[i]; var b = cp[i] + r0 * csh[i];
                var c = cp[i + 1] + r1 * csh[i + 1]; var d = cp[i + 1] - r1 * csh[i + 1];
                sink.B("Roads", "road", (a + c) * 0.5f).QuadUp(a, b, c, d, 6f);
                if (!sharp(i)) continue;
                var rp = segRight(i - 1);
                var rn = segRight(i);
                var f0 = Flat(cp[i] - cp[i - 1]); var f1 = Flat(cp[i + 1] - cp[i]);
                float sideSign = f0.x * f1.z - f0.z * f1.x > 0f ? 1f : -1f; // left turn: the outer side is the right side
                var wa = cp[i] + rp * (sideSign * csh[i]); var wb = cp[i] + rn * (sideSign * csh[i]);
                sink.B("Roads", "road", cp[i]).Tri(cp[i], wa, wb, Vector3.up, new Vector2(cp[i].x, cp[i].z) / 6f, new Vector2(wa.x, wa.z) / 6f, new Vector2(wb.x, wb.z) / 6f);
            }

            // sidewalks with kerb faces: city and beach only, never on a viaduct. The course is sub-divided to 3 m so a mouth
            // removes the kerb next to it only (course points are 15 m apart).
            BuildDense(3f, out var dp, out var dr, out var dsh, out var dLo, out var dT);
            foreach (var side in UseSumo ? new float[0] : new[] { 1f, -1f })
            {
                var width = new float[dp.Length];
                bool[] baseKeep = new bool[n];
                for (int i = 0; i < n; i++) baseKeep[i] = (ckind[i] == Kcity || ckind[i] == Kbeach) && !celev[i];
                float W(int i) => ckind[i] == Kbeach ? (SeaSide(i, side) ? 7f : 2.6f) : 3.2f;
                var keepFlags = new bool[dp.Length];
                for (int k = 0; k < dp.Length; k++)
                {
                    int lo = dLo[k], hi = Mathf.Min(lo + 1, n - 1);
                    float t = dT[k];
                    width[k] = Mathf.Lerp(W(lo), W(hi), t);
                    bool kind = t < 1e-4f ? baseKeep[lo] : baseKeep[lo] && baseKeep[hi];
                    keepFlags[k] = kind && !NearMouth(dp[k] + dr[k] * (side * dsh[k]), 3f);
                }
                KerbSweep(dp, dr, dsh, side, k => keepFlags[k], width, true);
            }

            // viaduct furniture
            foreach (var side in new[] { 1f, -1f })
            {
                Func<int, bool> onDeck = i => (ckind[i] == Kbridge || (ckind[i] == Kapproach && celev[i]));
                // The approach comes down from the bridge across the bay at ground level: where water lies 3 m outside its edge it
                // gets the same barrier, rail and fascia as the deck (the carriageway ended in open water with nothing at its edge).
                float sideNow = side;
                Func<int, bool> overWater = i =>
                {
                    if (ckind[i] != Kapproach || celev[i]) return false;
                    var edge = cp[i] + cr[i] * (sideNow * csh[i]);
                    var outside = cp[i] + cr[i] * (sideNow * (csh[i] + 3f));
                    return GroundClass(outside.x, outside.z) == 1 && !NearMouth(edge, 8f);
                };
                Func<int, bool> railed = i => onDeck(i) || overWater(i);
                Sweep(sink.Single("Barrier", "concrete"), cp, cr, csh, side, JerseyProfile, railed, 3f);
                Sweep(sink.Single("BarrierRail", "bridge"), cp, cr, csh, side, RailProfile, railed, 3f);
                Sweep(sink.Single("DeckFascia", "bridge"), cp, cr, csh, side, FasciaProfile, i => railed(i) && !InSpan(i), 3f);
                Sweep(sink.Single("DeckFasciaSteel", "bridge"), cp, cr, csh, side, FasciaProfile, i => onDeck(i) && InSpan(i), 3f);
            }
            for (int i = 0; i < n - 1; i++)
            {
                if (!(ckind[i] == Kbridge || (ckind[i] == Kapproach && celev[i])) || !(ckind[i + 1] == Kbridge || (ckind[i + 1] == Kapproach && celev[i + 1]))) continue;
                float w0 = csh[i] - 1.6f, w1 = csh[i + 1] - 1.6f;
                var a = cp[i] - cr[i] * w0 + Vector3.down * 2.2f; var b = cp[i] + cr[i] * w0 + Vector3.down * 2.2f;
                var c = cp[i + 1] + cr[i + 1] * w1 + Vector3.down * 2.2f; var d = cp[i + 1] - cr[i + 1] * w1 + Vector3.down * 2.2f;
                sink.B("DeckUnder", "bridge", (a + c) * 0.5f).Quad(a, b, c, d, Vector3.down);
            }

            // median fence: black rails and posts on wide city arteries (roadview-observed)
            // (with the SUMO network the fence comes from the export, along the SUMO centre lines: line kind "medianFence")
            Func<int, bool> fence = i => !UseSumo && ckind[i] == Kcity && chw[i] >= 7f && !celev[i] && !NearMouth(cp[i], chw[i] + 2.5f, false) && !InSumoJunction(cp[i], 4f);
            var zero = Fill(n, 0f);
            Sweep(sink.Single("MedianFence", "fence"), cp, cr, zero, 1f, FenceRailLow, fence, 3f);
            Sweep(sink.Single("MedianFence", "fence"), cp, cr, zero, 1f, FenceRailHigh, fence, 3f);
            float carry = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                if (!fence(i) || !fence(i + 1)) { carry = 0f; continue; }
                float seg = Vector3.Distance(cp[i], cp[i + 1]);
                float t = carry;
                while (t < seg)
                {
                    var pos = Vector3.Lerp(cp[i], cp[i + 1], t / seg);
                    sink.B("MedianFence", "fence", pos).Box(pos + Vector3.up * 0.46f, new Vector3(0.055f, 0.92f, 0.055f));
                    t += 4f;
                }
                carry = t - seg;
            }

            BuildCoursePaint();
        }

        const float LineHalf = 0.075f, LineGap = 0.15f;   // 0.15 m lines; double yellow = two 0.15 m lines with a 0.15 m gap (centres +-0.15 m)

        static void PaintLine(string mat, Vector3[] p, Vector3[] r, Func<int, float> offset, float half, float on, float cycle,
            Func<int, bool> keep, Func<Vector3, bool> skip = null, float lift = 0.03f)
        {
            float dist = 0f;
            for (int i = 0; i < p.Length - 1; i++)
            {
                float seg = Vector3.Distance(p[i], p[i + 1]);
                if (keep != null && (!keep(i) || !keep(i + 1))) { dist += seg; continue; }
                int pieces = Mathf.Max(1, Mathf.CeilToInt(seg / 2.5f));
                for (int k = 0; k < pieces; k++)
                {
                    float t0 = (float)k / pieces, t1 = (float)(k + 1) / pieces;
                    if (cycle > 0f && (dist + seg * t0) % cycle > on) continue;
                    Vector3 At(float t) => Vector3.Lerp(p[i], p[i + 1], t) + Vector3.Lerp(r[i], r[i + 1], t).normalized * Mathf.Lerp(offset(i), offset(i + 1), t) + Vector3.up * lift;
                    var q0 = At(t0); var q1 = At(t1);
                    if (skip != null && skip((q0 + q1) * 0.5f)) continue;
                    var s = Right(q1 - q0) * half;
                    sink.B("Paint", mat, q0).Quad(q0 - s, q0 + s, q1 + s, q1 - s, Vector3.up);
                }
                dist += seg;
            }
        }

        // Course points resampled to `step` metres (right vectors and half widths interpolated). lo = index of the course point
        // at or before the sample, t = fraction towards the next one.
        static void BuildDense(float step, out Vector3[] dp, out Vector3[] dr, out float[] dsh, out int[] dLo, out float[] dT)
        {
            var p = new List<Vector3>(); var r = new List<Vector3>(); var h = new List<float>(); var lo = new List<int>(); var tt = new List<float>();
            for (int i = 0; i < cp.Length - 1; i++)
            {
                float seg = Vector3.Distance(cp[i], cp[i + 1]);
                int pieces = Mathf.Max(1, Mathf.CeilToInt(seg / step));
                for (int k = 0; k < pieces; k++)
                {
                    float t = (float)k / pieces;
                    p.Add(Vector3.Lerp(cp[i], cp[i + 1], t));
                    r.Add(Vector3.Lerp(cr[i], cr[i + 1], t).normalized);
                    h.Add(Mathf.Lerp(csh[i], csh[i + 1], t));
                    lo.Add(i); tt.Add(t);
                }
            }
            p.Add(cp[cp.Length - 1]); r.Add(cr[cp.Length - 1]); h.Add(csh[cp.Length - 1]); lo.Add(cp.Length - 1); tt.Add(0f);
            dp = p.ToArray(); dr = r.ToArray(); dsh = h.ToArray(); dLo = lo.ToArray(); dT = tt.ToArray();
        }

        static void BuildCoursePaint()
        {
            int n = cp.Length;
            Func<int, bool> twoWayGround = i => !UseSumo && !coneway[i] && (ckind[i] == Kcity || ckind[i] == Kbeach) && !celev[i];
            Func<int, bool> fenced = i => ckind[i] == Kcity && chw[i] >= 7f;
            // double yellow centre where there is no median fence
            foreach (var off in new[] { -LineGap, LineGap })
                PaintLine("yellow", cp, cr, i => off, LineHalf, 0f, 0f, i => twoWayGround(i) && !fenced(i));
            // white edge lines, broken at side-street mouths
            foreach (var side in new[] { -1f, 1f })
            {
                PaintLine("white", cp, cr, i => side * (chw[i] - 0.45f), LineHalf, 0f, 0f,
                    i => UseSumo ? (celev[i] || ckind[i] == Kbridge) : (!celev[i] || ckind[i] == Kbridge || ckind[i] == Kapproach),
                    pos => NearMouth(pos, 1.5f));
                // dashed lane line on wide two-way roads
                PaintLine("white", cp, cr, i => side * chw[i] * 0.5f, LineHalf, 3f, 9f, i => twoWayGround(i) && chw[i] >= 6.5f, pos => NearMouth(pos, 4f));
            }
            // one-way multi-lane: dashed lane lines
            for (int k = 1; k <= 3; k++)
            {
                int kk = k;
                PaintLine("white", cp, cr, i => (kk - 2) * (chw[i] * 2f / 4f), LineHalf, 4f, 12f, i => coneway[i] && chw[i] >= 7f && (!UseSumo || celev[i] || ckind[i] == Kbridge));
            }
        }

        // ---------- side-street network ----------

        // Course point index when `end` is a generator-marked mouth (a road end on the course edge); -1 for every other end.
        static int MouthIndexAt(Vector3 end)
        {
            foreach (var m in mouths)
            {
                var d = m.pos - end;
                if (d.sqrMagnitude < 0.01f) return Mathf.Clamp(m.ci, 0, cp.Length - 1);
            }
            return -1;
        }

        static void BuildNetwork()
        {
            if (UseSumo) { BuildSumoNetwork(); return; }
            int ribbons = 0;
            var sidewalkW = 2.4f;
            foreach (var road in net.roads)
            {
                int cnt = road.p.Count / 3;
                var p = new Vector3[cnt];
                for (int i = 0; i < cnt; i++) p[i] = new Vector3(road.p[i * 3], road.p[i * 3 + 1], road.p[i * 3 + 2]);
                var r = Rights(p);
                float hw = road.w * 0.5f;
                var L = new Vector3[cnt]; var R = new Vector3[cnt];
                bool flared = road.ws != null && road.ws.Count == cnt;
                for (int i = 0; i < cnt; i++) { float h = flared ? road.ws[i] * 0.5f : hw; L[i] = p[i] - r[i] * h; R[i] = p[i] + r[i] * h; }
                // end caps that touch the course are cut flush with the course edge
                foreach (int end in new[] { 0, cnt - 1 })
                {
                    int ci = MouthIndexAt(p[end]);
                    if (ci < 0) continue;
                    var nrm = Flat(cr[ci]); nrm.Normalize(); if (Vector3.Dot(Flat(p[end] - cp[ci]), nrm) < 0f) nrm = -nrm; // true edge normal of the course, pointing at the side street
                    var u = end == 0 ? p[1] - p[0] : p[cnt - 2] - p[cnt - 1]; u.y = 0f; u.Normalize();
                    float du = Vector3.Dot(u, nrm);
                    if (Mathf.Abs(du) < 0.35f) continue;
                    foreach (var which in new[] { 0, 1 })
                    {
                        var corner = which == 0 ? L[end] : R[end];
                        float t = Vector3.Dot(corner - p[end], nrm) / du;
                        if (t <= 0f) continue; // only extend towards the course; cutting back would leave a hole wherever the course edge is not straight
                        corner -= u * Mathf.Min(t, road.w * 1.5f);
                        if (which == 0) L[end] = corner; else R[end] = corner;
                    }
                }
                for (int i = 0; i < cnt - 1; i++)
                {
                    var mid = (p[i] + p[i + 1]) * 0.5f;
                    sink.B(road.L == 0 ? "Roads" : "Roads" + road.L, road.cls == 6 ? "roadAlley" : "road", mid).QuadUp(L[i], R[i], R[i + 1], L[i + 1], 6f);
                    ribbons++;
                }
                if (road.cls <= 2)
                {
                    Func<int, bool> inner = i => i > 0 && i < cnt - 1;
                    var edge = Fill(cnt, hw);
                    var width = Fill(cnt, sidewalkW);
                    foreach (var side in new[] { 1f, -1f })
                        KerbSweep(p, r, edge, side, inner, width, road.cls <= 1);
                    foreach (var side in new[] { -1f, 1f })
                        PaintLine("white", p, r, i => side * (hw - 0.35f), LineHalf, 0f, 0f, inner);
                    if (road.oneway == 0)
                        foreach (var off in new[] { -LineGap, LineGap })
                            PaintLine("yellow", p, r, i => off, LineHalf, 0f, 0f, inner);
                }
                // zebra crossing and stop line where a side street meets the course
                if (road.cls <= 3)
                    foreach (int end in new[] { 0, cnt - 1 })
                    {
                        int ci = MouthIndexAt(p[end]);
                        if (ci < 0 || ckind[ci] == Kbridge) continue;
                        var u = end == 0 ? p[1] - p[0] : p[cnt - 2] - p[cnt - 1]; u.y = 0f;
                        u.Normalize();
                        var side = Right(u);
                        var origin = p[end] + u * (chw[ci] + 4.5f);
                        origin = new Vector3(origin.x, p[end].y, origin.z);
                        for (float lat = -hw + 0.8f; lat <= hw - 0.7f; lat += 1.1f)
                        {
                            var c0 = origin + side * lat;
                            sink.B("Paint", "white", c0).Quad(c0 - u * 1.4f - side * 0.28f + Vector3.up * 0.07f, c0 - u * 1.4f + side * 0.28f + Vector3.up * 0.07f,
                                c0 + u * 1.4f + side * 0.28f + Vector3.up * 0.07f, c0 + u * 1.4f - side * 0.28f + Vector3.up * 0.07f, Vector3.up);
                        }
                        var stop = origin + u * 3.2f;
                        sink.B("Paint", "white", stop).Quad(stop - side * hw + Vector3.up * 0.07f, stop + side * hw + Vector3.up * 0.07f,
                            stop + side * hw + u * 0.4f + Vector3.up * 0.07f, stop - side * hw + u * 0.4f + Vector3.up * 0.07f, Vector3.up);
                    }
            }
            BuildJunctions();
            Log($"NETWORK ribbons={ribbons} roads={net.roads.Count}");
        }

        // Corner wedges between neighbouring arms: flat at the junction node height, never a disc.
        static void BuildJunctions()
        {
            foreach (var j in net.junctions)
            {
                int m = j.a.Count / 2;
                var arms = Enumerable.Range(0, m).Select(k => (ang: j.a[k * 2], hw: j.a[k * 2 + 1])).OrderBy(a => a.ang).ToArray();
                var node = new Vector3(j.x, j.y, j.z);
                for (int k = 0; k < m; k++)
                {
                    var a = arms[k]; var b = arms[(k + 1) % m];
                    float gap = (b.ang - a.ang + 360f) % 360f;
                    if (m == 2 && gap > 160f && gap < 200f) continue;
                    if (gap >= 175f || gap < 3f) continue;
                    var ua = new Vector3(Mathf.Sin(a.ang * Mathf.Deg2Rad), 0f, Mathf.Cos(a.ang * Mathf.Deg2Rad));
                    var ub = new Vector3(Mathf.Sin(b.ang * Mathf.Deg2Rad), 0f, Mathf.Cos(b.ang * Mathf.Deg2Rad));
                    var A = node + Right(ua) * a.hw;
                    var B = node - Right(ub) * b.hw;
                    float cross = ua.x * ub.z - ua.z * ub.x;
                    var bq = B - A;
                    float t = (bq.x * ub.z - bq.z * ub.x) / (Mathf.Abs(cross) < 1e-4f ? 1e-4f : cross);
                    var sinkB = sink.B("Roads", "road", node);
                    if (t > 0f && t < 22f)
                        sinkB.QuadUp(node, A, A + ua * t, B, 6f);
                    else
                        sinkB.Tri(node, A, B, Vector3.up, new Vector2(node.x, node.z) / 6f, new Vector2(A.x, A.z) / 6f, new Vector2(B.x, B.z) / 6f);
                }
            }
        }

        // ---------- lamps and street trees ----------

        static void BuildStreetProps()
        {
            var r = new System.Random(4242);
            int lamps = 0, trees = 0;
            // course
            float nextLamp = 20f;
            int lampSide = 1;
            for (int i = 1; i < cp.Length - 1; i++)
            {
                if (ccum[i] < nextLamp) continue;
                bool deck = ckind[i] == Kbridge || (ckind[i] == Kapproach && celev[i]);
                if (!deck && ckind[i] == Kapproach) { nextLamp = ccum[i] + 40f; continue; }
                if (deck)
                {
                    nextLamp = ccum[i] + 40f;
                    foreach (var s in new[] { -1f, 1f })
                    {
                        var dp = cp[i] + cr[i] * (s * (csh[i] - 0.4f));
                        if (InMouthZone(dp, 2f)) continue;   // a ramp joining the viaduct
                        Lamp(dp, -cr[i] * s, 9f); Record("lamp", dp); lamps++;
                    }
                }
                else
                {
                    int tryside = -lampSide;
                    var lp = cp[i] + cr[i] * (tryside * (csh[i] + 1.0f));
                    if (!PropOK(lp, 0.3f, 0f, 2f)) continue;   // never in a side-street mouth (alleys included) or on the far kerb opposite one
                    lampSide = tryside;
                    nextLamp = ccum[i] + 35f;
                    Lamp(lp, -cr[i] * lampSide, 8f);
                    Record("lamp", lp);
                    lamps++;
                }
            }
            // street trees along city and beach kerbs
            float nextTree = 6f;
            for (int i = 1; i < cp.Length - 1; i++)
            {
                if (ccum[i] < nextTree) continue;
                if (!(ckind[i] == Kcity || ckind[i] == Kbeach) || celev[i]) { nextTree = ccum[i] + 8f; continue; }
                nextTree = ccum[i] + 9f + (float)r.NextDouble() * 7f;
                foreach (var s in new[] { -1f, 1f })
                {
                    if (ckind[i] == Kbeach && SeaSide(i, s) && r.NextDouble() < 0.55) continue;
                    if (r.NextDouble() < 0.12) continue;
                    var pos = cp[i] + cr[i] * (s * (csh[i] + 1.6f));
                    if (!PropOK(pos, 0.5f, 0f, 2f)) continue;
                    if (!Tree(pos, r, ckind[i] == Kcity ? 1.15f : 1f)) continue;
                    Record("tree", pos);
                    trees++;
                }
            }
            // secondary and tertiary side streets: lamps every 30-40 m on alternating sides; plane trees in grates about every 9-12 m
            // on both sidewalks, 1 m behind the kerb (Korean arterials, roadview 수영로/광안해변로). With SUMO a tree stands only on a
            // real sidewalk (paved, not carriageway), found by stepping out from the kerb.
            foreach (var road in net.roads)
            {
                if (road.cls > 2 && road.cls != 5) continue;
                int cnt = road.p.Count / 3;
                float acc = 14f, step = 0f; int idx = 0;
                float treeAcc = 5f, treeStep = 0f;
                for (int i = 1; i < cnt; i++)
                {
                    var a = new Vector3(road.p[(i - 1) * 3], road.p[(i - 1) * 3 + 1], road.p[(i - 1) * 3 + 2]);
                    var b = new Vector3(road.p[i * 3], road.p[i * 3 + 1], road.p[i * 3 + 2]);
                    float segLen = Vector3.Distance(a, b);
                    if (segLen < 0.01f) continue;
                    var rr = Right(b - a);
                    // trees: walk the segment in metres
                    for (float d = 0f; d < segLen; )
                    {
                        float need = treeAcc - treeStep;
                        if (d + need > segLen) { treeStep += segLen - d; break; }
                        d += need; treeStep = 0f; treeAcc = 9f + (float)r.NextDouble() * 3f;
                        var at = Vector3.Lerp(a, b, d / segLen);
                        if (road.cls == 5) continue;   // ramps and links: no trees
                        foreach (float s in new[] { 1f, -1f })
                        {
                            if (r.NextDouble() < 0.1) continue;   // a missing tree here and there
                            if (!SidewalkSpot(at, rr * s, road.w * 0.5f, out var spot)) continue;
                            if (!PropOK(spot, 0.5f, 0.8f, 2f)) continue;
                            if (!Tree(spot, r, 0.95f)) continue;
                            TreeGrate(spot, rr);
                            Record("tree", spot);
                            trees++;
                        }
                    }
                    step += segLen;
                    if (step < acc || i == cnt - 1) continue;
                    step = 0f; acc = 30f + (float)r.NextDouble() * 10f; idx++;
                    float side = idx % 2 == 0 ? 1f : -1f;
                    Vector3 sideLamp;
                    if (UseSumo) { if (!SidewalkSpot(b, rr * side, road.w * 0.5f, out sideLamp)) continue; sideLamp -= rr * side * 0.5f; }
                    else sideLamp = b + rr * (side * (road.w * 0.5f + 1.0f));
                    if (PropOK(sideLamp, 0.3f, 0f, 2f)) { Lamp(sideLamp, -rr * side, road.cls == 5 ? 9f : 8f); Record("lamp", sideLamp); lamps++; }
                }
            }
            Log($"PROPS lamps={lamps} trees={trees}");
        }

        // A point 1 m behind the kerb on a sidewalk: step outward from the road centre until off the carriageway, then 1 m more,
        // and require sidewalk paving (paved but not carriageway) there. Without SUMO: the design width + 1.8 m.
        static bool SidewalkSpot(Vector3 centre, Vector3 outward, float halfWidth, out Vector3 spot)
        {
            outward.y = 0f; outward.Normalize();
            if (!UseSumo) { spot = centre + outward * (halfWidth + 1.8f); return true; }
            // first reach the carriageway (the centre of a divided road is a median), then leave it at the kerb
            bool seenRoad = false;
            for (float o = 0f; o < halfWidth + 6f; o += 0.25f)
            {
                var q = centre + outward * o;
                if (sumoRoadArea.Contains(q.x, q.z)) { seenRoad = true; continue; }
                if (!seenRoad) continue;
                var t = q + outward * 1.0f;
                if (!sumoRoadArea.Contains(t.x, t.z) && sumoPavedArea.Contains(t.x, t.z)) { spot = new Vector3(t.x, centre.y + 0.15f, t.z); return true; }
                break;
            }
            spot = default;
            return false;
        }

        // 1.2 m square steel tree grate flush with the sidewalk
        static void TreeGrate(Vector3 basePos, Vector3 along)
        {
            along.y = 0f; along.Normalize();
            var across = Right(along);
            var g = sink.B("TreeGrates", "poleArm", basePos);
            float y = basePos.y + 0.012f;
            var c = new Vector3(basePos.x, y, basePos.z);
            g.Quad(c - along * 0.6f - across * 0.6f, c + along * 0.6f - across * 0.6f, c + along * 0.6f + across * 0.6f, c - along * 0.6f + across * 0.6f, Vector3.up);
        }

        static void Lamp(Vector3 basePos, Vector3 towardRoad, float height)
        {
            towardRoad.y = 0f; towardRoad.Normalize();
            float yaw = Mathf.Atan2(towardRoad.x, towardRoad.z) * Mathf.Rad2Deg;
            var b = sink.B("Lamps", "lamp", basePos);
            b.Box(basePos + Vector3.up * (height * 0.5f), new Vector3(0.22f, height, 0.22f), yaw);
            b.Box(basePos + towardRoad * 1.2f + Vector3.up * (height - 0.12f), new Vector3(0.12f, 0.12f, 2.5f), yaw);
            b.Box(basePos + towardRoad * 2.3f + Vector3.up * (height - 0.2f), new Vector3(0.5f, 0.2f, 0.95f), yaw);
        }

        static bool Tree(Vector3 basePos, System.Random r, float scale)
        {
            float size = BusanSliceTrees.CardSize * (2.9f + (float)r.NextDouble() * 0.5f) * scale;
            float yaw = (float)r.NextDouble() * 180f;
            var center = basePos + Vector3.up * (BusanSliceTrees.CardCenterY * (size / BusanSliceTrees.CardSize));
            var tint = TreeTint(r);   // leaf cards seen from the shaded side were near black: light them like a sphere, mostly from above
            if (!CrownOK("tree", basePos, size * 0.5f, center.y + size * 0.5f)) return false;   // crown (card half width, card top) must not reach a road it does not stand on
            var b = sink.B("Trees", "tree", basePos, true);
            b.NormalUp = 2.0f;   // nearly flat-up normals: the card lights the same from every side, no pale / dark card pairs
            b.Tint = tint;
            for (int k = 0; k < 2; k++)
            {
                var along = Quaternion.Euler(0f, yaw + 90f * k, 0f) * Vector3.right * (size * 0.5f);
                var up = Vector3.up * (size * 0.5f);
                float u0 = k * 0.5f, u1 = u0 + 0.5f;
                b.QuadBothUV(center - along - up, center + along - up, center + along + up, center - along + up, Quaternion.Euler(0f, yaw + 90f * k, 0f) * Vector3.forward,
                    new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f));
            }
            return true;
        }

        // ---------- closed road ends: orange-and-white PE barriers and traffic cones ----------

        static void Cone(MeshBuilder b, Vector3 baseCenter, float height, float r0, float r1, int sides)
        {
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                var n = (d0 + d1).normalized;
                b.Quad(baseCenter + d0 * r0, baseCenter + d1 * r0, baseCenter + Vector3.up * height + d1 * r1, baseCenter + Vector3.up * height + d0 * r1, n);
            }
        }

        static void BuildClosures()
        {
            if (net.closures == null || net.closures.Count == 0) return;
            int barriers = 0, cones = 0;
            foreach (var c in net.closures)
            {
                var pos = new Vector3(c.x, c.y, c.z);
                var dir = new Vector3(c.dx, 0f, c.dz).normalized;   // away from the open road
                var side = Right(dir);
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                int count = Mathf.Max(2, Mathf.CeilToInt((c.w - 0.4f) / 1.5f));
                for (int k = 0; k < count; k++)
                {
                    var at = pos - dir * 1.2f + side * ((k - (count - 1) * 0.5f) * 1.5f);
                    sink.B("Closures", "peOrange", at).Box(at + Vector3.up * 0.15f, new Vector3(1.5f, 0.3f, 0.46f), yaw);
                    sink.B("Closures", "peWhite", at).Box(at + Vector3.up * 0.45f, new Vector3(1.5f, 0.3f, 0.38f), yaw);
                    sink.B("Closures", "peOrange", at).Box(at + Vector3.up * 0.75f, new Vector3(1.5f, 0.3f, 0.32f), yaw);
                    barriers++;
                }
                foreach (float back in new[] { 3.5f, 7f, 10.5f })
                    foreach (float lat in new[] { -c.w * 0.25f, c.w * 0.25f })
                    {
                        var at = pos - dir * back + side * lat;
                        var cb = sink.B("Closures", "cone", at);
                        cb.Box(at + Vector3.up * 0.015f, new Vector3(0.4f, 0.03f, 0.4f), yaw);
                        Cone(cb, at + Vector3.up * 0.03f, 0.25f, 0.16f, 0.125f, 8);
                        Cone(sink.B("Closures", "peWhite", at), at + Vector3.up * 0.28f, 0.18f, 0.125f, 0.095f, 8);
                        Cone(cb, at + Vector3.up * 0.46f, 0.26f, 0.095f, 0.03f, 8);
                        cones++;
                    }
            }
            Log($"CLOSURES count={net.closures.Count} barriers={barriers} cones={cones}");
        }
    }
}
