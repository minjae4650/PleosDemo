using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    public static partial class BusanSliceBuilder
    {
        // Ray sweep over the road collider layer (layer 30): every 5 m along the course and every
        // side street, centre and both near-edge lanes. Reports holes (no surface within 0.6 m of the
        // intended height) and steps (jumps between consecutive centre hits beyond the allowed grade).
        static int SweepColliders(out string text)
        {
            Physics.SyncTransforms();
            int mask = 1 << RoadLayer;
            var sb = new StringBuilder();
            var examples = new List<string>();
            int courseSamples = 0, courseHoles = 0, courseSteps = 0, courseOverlaps = 0, netOverlaps = 0;
            float worstStep = 0f;
            float total = ccum[ccum.Length - 1];
            float prevY = float.NaN;
            int idx = 0;
            for (float s = 0f; s <= total; s += 5f)
            {
                while (idx < cp.Length - 2 && ccum[idx + 1] < s) idx++;
                var pos = CoursePosAt(s);
                foreach (var lat in new[] { 0f, chw[idx] - 1f, -(chw[idx] - 1f) })
                {
                    var origin = pos + cr[idx] * lat + Vector3.up * 1.2f;
                    courseSamples++;
                    bool cgot = Physics.Raycast(origin, Vector3.down, out var hit, 3f, mask);
                    if (cgot && hit.point.y - pos.y > 0.6f && hit.point.y - pos.y < 2.5f)
                    {
                        courseOverlaps++; // another road ribbon lies just above: drivable, not a gap
                        if (examples.Count < 8) examples.Add($"overlap@s={s:0} ({pos.x:0},{pos.z:0}) lat={lat:0.0} roadY={pos.y:0.00} hit={hit.point.y:0.00}");
                    }
                    else if (!cgot || Mathf.Abs(hit.point.y - pos.y) > 0.6f)
                    {
                        courseHoles++;
                        if (examples.Count < 8) examples.Add($"hole@s={s:0} ({pos.x:0},{pos.z:0}) lat={lat:0.0} hit={(cgot ? hit.point.y.ToString("0.00") : "none")} roadY={pos.y:0.00}");
                    }
                    else if (lat == 0f)
                    {
                        if (!float.IsNaN(prevY))
                        {
                            float step = Mathf.Abs(hit.point.y - prevY) - 0.07f * 5f - 0.1f;
                            if (step > 0.25f)
                            {
                                courseSteps++;
                                worstStep = Mathf.Max(worstStep, step);
                                if (examples.Count < 8) examples.Add($"step@s={s:0} ({pos.x:0},{pos.z:0}) {step:0.00}m");
                            }
                        }
                        prevY = hit.point.y;
                    }
                }
            }
            int netSamples = 0, netHoles = 0, netCenterHoles = 0;
            foreach (var road in net.roads)
            {
                int cnt = road.p.Count / 3;
                float hw = road.w * 0.5f;
                for (int i = 1; i < cnt - 1; i++)
                {
                    var a = new Vector3(road.p[(i - 1) * 3], road.p[(i - 1) * 3 + 1], road.p[(i - 1) * 3 + 2]);
                    var b = new Vector3(road.p[i * 3], road.p[i * 3 + 1], road.p[i * 3 + 2]);
                    var c = new Vector3(road.p[(i + 1) * 3], road.p[(i + 1) * 3 + 1], road.p[(i + 1) * 3 + 2]);
                    var r = Right(c - a);
                    foreach (var lat in new[] { 0f, hw - 0.6f, -(hw - 0.6f) })
                    {
                        netSamples++;
                        bool got = Physics.Raycast(b + r * lat + Vector3.up * 1.2f, Vector3.down, out var hit, 3f, mask);
                        if (got && hit.point.y - b.y > 0.6f && hit.point.y - b.y < 2.5f) netOverlaps++;
                        else if (!got || Mathf.Abs(hit.point.y - b.y) > 0.6f)
                        {
                            netHoles++;
                            if (lat == 0f) netCenterHoles++;
                            if (examples.Count < 14 || lat == 0f) examples.Add($"netHole way={road.way} ({b.x:0},{b.z:0}) lat={lat:0.0} roadY={b.y:0.00} hit={(got ? hit.point.y.ToString("0.00") : "none")} w={road.w}");
                        }
                    }
                }
            }
            sb.Append($"SWEEP course samples={courseSamples} holes={courseHoles} overlaps={courseOverlaps} steps={courseSteps} worstStep={worstStep:0.00}m; network samples={netSamples} holes={netHoles} centreHoles={netCenterHoles} overlaps={netOverlaps}");
            foreach (var e in examples) sb.Append("\n  ").Append(e);
            var reachExamples = new List<string>();
            ReachCheck(sb, reachExamples);
            if (UseSumo) sb.Append("\n").Append(SumoLaneSweep());
            foreach (var e in reachExamples) sb.Append("\n  ").Append(e);
            text = sb.ToString();
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/BusanBridge/sweep.txt"));
            if (Directory.Exists(Path.GetDirectoryName(path))) File.WriteAllText(path, text);
            return courseHoles + courseSteps + netHoles;
        }

        // Reachability: every road must be drivable end to end, and reachable from the course through drivable roads.
        // A road is drivable when, every metre along it, the centre and both lanes at +-0.8 m have a collider surface within
        // VehicleController's SurfaceTolerance (1.2 m) of the previous centre height. Roads connect like tools/check-busan-network.py:
        // an end within half a width plus 1 m of another road's vertex. Attached roads start on the course edge.
        static int ReachCheck(StringBuilder sb, List<string> examples)
        {
            const float tolerance = 1.2f;
            int mask = 1 << RoadLayer;
            int n = net.roads.Count;
            var pts = new Vector3[n][];
            var all = new List<Vector3>(); var owner = new List<int>();
            for (int i = 0; i < n; i++)
            {
                var r = net.roads[i];
                int cnt = r.p.Count / 3;
                pts[i] = new Vector3[cnt];
                for (int k = 0; k < cnt; k++)
                {
                    pts[i][k] = new Vector3(r.p[k * 3], r.p[k * 3 + 1], r.p[k * 3 + 2]);
                    all.Add(pts[i][k]); owner.Add(i);
                }
            }
            var grid = new PointGrid(all.ToArray(), 30f);
            var adjacency = new List<int>[n];
            for (int i = 0; i < n; i++) adjacency[i] = new List<int>();
            var near = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float radius = Mathf.Max(net.roads[i].w * 0.5f + 1f, 1.5f);
                foreach (var end in new[] { pts[i][0], pts[i][pts[i].Length - 1] })
                {
                    grid.Within(end, radius, near);
                    foreach (int v in near)
                    {
                        int j = owner[v];
                        if (j == i || Mathf.Abs(all[v].y - end.y) > 3f) continue;
                        if (!adjacency[i].Contains(j)) adjacency[i].Add(j);
                        if (!adjacency[j].Contains(i)) adjacency[j].Add(i);
                    }
                }
            }
            var attached = new bool[n];
            if (net.mouths != null)
                for (int i = 0; i < n; i++)
                    foreach (var m in net.mouths)
                    {
                        var at = new Vector3(m.x, m.y, m.z);
                        if ((pts[i][0] - at).sqrMagnitude < 0.36f || (pts[i][pts[i].Length - 1] - at).sqrMagnitude < 0.36f) { attached[i] = true; break; }
                    }

            var hits = new RaycastHit[16];
            string lastHits = "";
            bool Surface(float x, float z, float refY, out float height)
            {
                int count = Physics.RaycastNonAlloc(new Vector3(x, refY + 4f, z), Vector3.down, hits, 14f, mask, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue; height = 0f;
                for (int h = 0; h < count; h++)
                {
                    float gap = Mathf.Abs(hits[h].point.y - refY);
                    if (gap < best) { best = gap; height = hits[h].point.y; }
                }
                lastHits = count == 0 ? "none" : string.Join("/", Enumerable.Range(0, count).Select(h => hits[h].point.y.ToString("0.0") + "@" + hits[h].collider.transform.parent.name + ":" + hits[h].collider.name));
                return count > 0 && best <= tolerance;
            }

            var clean = new bool[n];
            var why = new string[n];
            for (int i = 0; i < n; i++)
            {
                var p = pts[i];
                float prevY = p[0].y;
                clean[i] = true;
                for (int k = 0; k < p.Length - 1 && clean[i]; k++)
                {
                    var d = p[k + 1] - p[k]; d.y = 0f;
                    float len = d.magnitude;
                    if (len < 1e-4f) continue;
                    var dir = d / len; var right = new Vector3(dir.z, 0f, -dir.x);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(len));
                    for (int st = (k == 0 ? 0 : 1); st <= steps; st++)
                    {
                        float t = (float)st / steps;
                        var q = Vector3.Lerp(p[k], p[k + 1], t);
                        // The car steers onto the surface nearest its own height, so the road is checked at its designed height; the hit
                        // heights must also be continuous from one metre to the next.
                        if (!Surface(q.x, q.z, q.y, out float cy)) { clean[i] = false; why[i] = $"gap at ({q.x:0},{q.z:0}) lat=0 designedY={q.y:0.0} hits={lastHits} vertex={k}/{p.Length}"; break; }
                        if (Mathf.Abs(cy - prevY) > tolerance) { clean[i] = false; why[i] = $"step {cy - prevY:0.0} m at ({q.x:0},{q.z:0}) vertex={k}/{p.Length}"; break; }
                        foreach (float lat in new[] { 0.8f, -0.8f })
                        {
                            var s2 = q + right * lat;
                            if (!Surface(s2.x, s2.z, q.y, out _)) { clean[i] = false; why[i] = $"gap at ({s2.x:0},{s2.z:0}) lat={lat:0.0} designedY={q.y:0.0} hits={lastHits} vertex={k}/{p.Length}"; break; }
                        }
                        if (!clean[i]) break;
                        prevY = cy;
                    }
                }
            }
            var reached = new bool[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++) if (attached[i] && clean[i]) { reached[i] = true; queue.Enqueue(i); }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                foreach (int j in adjacency[i])
                    if (!reached[j] && clean[j]) { reached[j] = true; queue.Enqueue(j); }
            }
            int a = reached.Count(x => x);
            int blocked = n - a;
            sb.Append($"\nREACH roads={a}/{n} blocked={blocked} unclean={clean.Count(x => !x)} attached={attached.Count(x => x)}");
            int shown = 0;
            for (int i = 0; i < n && shown < 12; i++)
            {
                if (reached[i]) continue;
                examples.Add($"unreached way={net.roads[i].way} cls={net.roads[i].cls} w={net.roads[i].w} {(clean[i] ? "clean but cut off" : why[i])}");
                shown++;
            }
            return blocked;
        }

        public static string SweepCommand()
        {
            LoadData();
            PrepareCourse();
            SweepColliders(out var text);
            var lines = text.Split('\n');
            return lines[0] + " | " + lines.First(l => l.StartsWith("REACH")) + (lines.Any(l => l.StartsWith("SUMOLANES")) ? " | " + lines.First(l => l.StartsWith("SUMOLANES")) : "");
        }
    }
}
