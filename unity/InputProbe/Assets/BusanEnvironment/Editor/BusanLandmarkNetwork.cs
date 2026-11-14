using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // The road network of the landmark map, Editor/Data/LandmarkNetwork.json (written by tools/network.py), as the assembled scene
    // (BusanLandmarkScene) and its checks (BusanLandmarkChecks) read it. JsonUtility cannot read the file's nested arrays
    // ([[x, y, z], ...]), so it is parsed with the small reader below.
    public sealed class BusanLandmarkNetwork
    {
        public const string DataPath = "Assets/BusanEnvironment/Editor/Data/LandmarkNetwork.json";

        public sealed class Segment { public string id, kind; public Vector3[] pts; public int fwd, back; public float median, laneWidth; }

        // One route: a line of points (closed: the last point joins the first), the lane the player drives (metres to the right of
        // the line) and the auto-drive speed (m/s) at each point.
        public sealed class Route
        {
            public string name;
            public bool closed;
            public Vector3[] path;
            public float[] lane, speed;
            // distance along the line at each point, measured flat as tools/network.py measures s (spawn, jumps, segmentStart);
            // DriveAssist measures in 3D, which is longer on the ramps (by up to ~1 m over the bridge)
            public float[] cum;
            public float Length;           // the whole line; for a closed route including the step from the last point to the first
            public float declaredLength;   // the json's `length` (the same measure, rounded to 0.1 m), for a check

            // the horizontal direction to the right of travel at each point (central differences, wrapping on a closed route), as
            // tools/districts/ring.py lays the lane line it paves
            public Vector3[] right;

            internal void Prepare()
            {
                int n = path.Length;
                float Flat(Vector3 a, Vector3 b) => new Vector2(b.x - a.x, b.z - a.z).magnitude;
                cum = new float[n];
                for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Flat(path[i - 1], path[i]);
                Length = cum[n - 1] + (closed ? Flat(path[n - 1], path[0]) : 0f);
                right = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    var prev = closed ? path[(i - 1 + n) % n] : path[Mathf.Max(i - 1, 0)];
                    var next = closed ? path[(i + 1) % n] : path[Mathf.Min(i + 1, n - 1)];
                    var t = next - prev; t.y = 0f; t.Normalize();
                    right[i] = new Vector3(t.z, 0f, -t.x);
                }
            }

            // s along the line (wrapped on a closed route, clamped on an open one): the segment's first point and the fraction
            public void Locate(float s, out int lo, out int hi, out float f)
            {
                int n = path.Length;
                if (closed) { s %= Length; if (s < 0f) s += Length; }
                else s = Mathf.Clamp(s, 0f, Length);
                if (closed && s >= cum[n - 1])
                {
                    lo = n - 1; hi = 0;
                    float last = Length - cum[n - 1];
                    f = last > 1e-4f ? (s - cum[n - 1]) / last : 0f;
                    return;
                }
                int a = 0, b = n - 1;
                while (a + 1 < b) { int mid = (a + b) / 2; if (cum[mid] <= s) a = mid; else b = mid; }
                lo = a; hi = b;
                float seg = cum[b] - cum[a];
                f = seg > 1e-4f ? (s - cum[a]) / seg : 0f;
            }

            public Vector3 PointAt(float s) { Locate(s, out int a, out int b, out float f); return Vector3.Lerp(path[a], path[b], f); }
            public float LaneAt(float s) { Locate(s, out int a, out int b, out float f); return Mathf.Lerp(lane[a], lane[b], f); }
            public float SpeedAt(float s) { Locate(s, out int a, out int b, out float f); return Mathf.Lerp(speed[a], speed[b], f); }

            // horizontal direction of travel at s (of the segment s lies on)
            public Vector3 TangentAt(float s)
            {
                Locate(s, out int a, out int b, out _);
                var t = path[b] - path[a]; t.y = 0f;
                return t.sqrMagnitude > 1e-8f ? t.normalized : Vector3.forward;
            }

            // The lane line at s: each point moved `lane` to its right, straight between them; plus `lateral` metres further right.
            public Vector3 LanePointAt(float s, float lateral = 0f)
            {
                Locate(s, out int a, out int b, out float f);
                var pa = path[a] + right[a] * (lane[a] + lateral);
                var pb = path[b] + right[b] * (lane[b] + lateral);
                return Vector3.Lerp(pa, pb, f);
            }
        }

        // A light zone (plan.md 4, the tunnel): a box about `centre`, `axis` (x, z) along its long side, `half` = half extents
        // (along, up, across), eased in over `ramp` metres outside each end.
        public sealed class Zone { public string kind, segment; public Vector3 centre; public Vector2 axis; public Vector3 half; public float ramp; }

        public float roadY;
        public readonly List<Segment> segments = new List<Segment>();
        public readonly List<Route> routes = new List<Route>();
        public int spawnRoute;
        public float spawnS;
        public readonly List<(string name, int route, float s)> jumps = new List<(string, int, float)>();
        public readonly List<Zone> zones = new List<Zone>();

        public static BusanLandmarkNetwork Load(string path = DataPath)
        {
            var root = (Dictionary<string, object>)Json.Parse(File.ReadAllText(path, Encoding.UTF8));
            var net = new BusanLandmarkNetwork { roadY = F(root["roadY"]) };
            foreach (Dictionary<string, object> s in (List<object>)root["segments"])
                net.segments.Add(new Segment
                {
                    id = (string)s["id"], kind = (string)s["kind"], pts = Points(s["pts"]),
                    fwd = s.TryGetValue("fwd", out var fw) ? (int)F(fw) : 1, back = s.TryGetValue("back", out var bk) ? (int)F(bk) : 0,
                    median = s.TryGetValue("median", out var md) ? F(md) : 0f, laneWidth = s.TryGetValue("laneWidth", out var lw) ? F(lw) : 3.25f,
                });
            foreach (Dictionary<string, object> r in (List<object>)root["routes"])
            {
                var route = new Route
                {
                    name = (string)r["name"], closed = r.TryGetValue("closed", out var c) && c is bool b && b, path = Points(r["path"]),
                    lane = ((List<object>)r["lane"]).Select(F).ToArray(), speed = ((List<object>)r["speed"]).Select(F).ToArray(),
                    declaredLength = r.TryGetValue("length", out var len) ? F(len) : -1f,
                };
                if (route.lane.Length != route.path.Length || route.speed.Length != route.path.Length)
                    throw new InvalidDataException($"route {route.name}: {route.path.Length} points, {route.lane.Length} lanes, {route.speed.Length} speeds");
                route.Prepare();
                net.routes.Add(route);
            }
            var spawn = (Dictionary<string, object>)root["spawn"];
            net.spawnRoute = (int)F(spawn["route"]);
            net.spawnS = F(spawn["s"]);
            foreach (Dictionary<string, object> j in (List<object>)root["jumps"])
                net.jumps.Add(((string)j["name"], (int)F(j["route"]), F(j["s"])));
            if (root.TryGetValue("zones", out var zoneList) && zoneList is List<object> zs)
                foreach (Dictionary<string, object> z in zs)
                {
                    var c = (List<object>)z["centre"]; var a = (List<object>)z["axis"]; var h = (List<object>)z["half"];
                    net.zones.Add(new Zone
                    {
                        kind = z.TryGetValue("kind", out var k) ? (string)k : "", segment = z.TryGetValue("segment", out var sg) ? (string)sg : "",
                        centre = new Vector3(F(c[0]), F(c[1]), F(c[2])), axis = new Vector2(F(a[0]), F(a[1])),
                        half = new Vector3(F(h[0]), F(h[1]), F(h[2])), ramp = z.TryGetValue("ramp", out var rp) ? F(rp) : 30f,
                    });
                }
            return net;
        }

        // x and z of every point of every segment (heights left out)
        public void FlatExtent(out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = minZ = float.MaxValue; maxX = maxZ = float.MinValue;
            foreach (var p in segments.SelectMany(s => s.pts).Concat(routes.SelectMany(r => r.path)))
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
        }

        static float F(object o) => Convert.ToSingle(o, CultureInfo.InvariantCulture);

        static Vector3[] Points(object o) =>
            ((List<object>)o).Select(p => { var q = (List<object>)p; return new Vector3(F(q[0]), F(q[1]), F(q[2])); }).ToArray();

        // A minimal JSON reader: objects -> Dictionary<string, object>, arrays -> List<object>, numbers -> double, strings, bools, null.
        static class Json
        {
            public static object Parse(string text)
            {
                int i = 0;
                var value = Value(text, ref i);
                Space(text, ref i);
                if (i != text.Length) throw new FormatException("JSON: text after the value at " + i);
                return value;
            }

            static void Space(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            static void Expect(string s, ref int i, char c)
            {
                Space(s, ref i);
                if (i >= s.Length || s[i] != c) throw new FormatException($"JSON: '{c}' expected at {i}");
                i++;
            }

            static object Value(string s, ref int i)
            {
                Space(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON: value expected at the end");
                char c = s[i];
                if (c == '{')
                {
                    var d = new Dictionary<string, object>();
                    i++; Space(s, ref i);
                    if (s[i] == '}') { i++; return d; }
                    while (true)
                    {
                        Space(s, ref i);
                        var key = Text(s, ref i);
                        Expect(s, ref i, ':');
                        d[key] = Value(s, ref i);
                        Space(s, ref i);
                        if (s[i] == ',') { i++; continue; }
                        Expect(s, ref i, '}');
                        return d;
                    }
                }
                if (c == '[')
                {
                    var list = new List<object>();
                    i++; Space(s, ref i);
                    if (s[i] == ']') { i++; return list; }
                    while (true)
                    {
                        list.Add(Value(s, ref i));
                        Space(s, ref i);
                        if (s[i] == ',') { i++; continue; }
                        Expect(s, ref i, ']');
                        return list;
                    }
                }
                if (c == '"') return Text(s, ref i);
                if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
                if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
                if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (i == start) throw new FormatException($"JSON: unexpected '{c}' at {i}");
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            static string Text(string s, ref int i)
            {
                if (s[i] != '"') throw new FormatException("JSON: string expected at " + i);
                i++;
                var sb = new StringBuilder();
                while (s[i] != '"')
                {
                    char c = s[i++];
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = s[i++];
                    switch (e)
                    {
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;     // \" \\ \/
                    }
                }
                i++;
                return sb.ToString();
            }
        }
    }
}
