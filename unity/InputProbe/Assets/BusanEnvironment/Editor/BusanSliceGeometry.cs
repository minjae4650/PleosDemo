using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    public static partial class BusanSliceBuilder
    {
        const float ChunkSize = 500f;

        sealed class MeshBuilder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<int> T = new List<int>();
            public bool UseColors;
            public Color Tint = Color.white;
            public float NormalUp;   // > 0 bends every normal towards the sky (kerb faces, foliage): small shaded faces keep their colour

            int Add(Vector3 p, Vector2 uv, Color c)
            {
                V.Add(p); U.Add(uv);
                if (UseColors) C.Add(c);
                return V.Count - 1;
            }

            // Emits the quad so that its front face looks toward `expected`; the winding is
            // derived from the geometry instead of assumed, which keeps every caller honest.
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 expected, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                var n = Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a);
                int i = Add(a, ua, Tint); Add(b, ub, Tint); Add(c, uc, Tint); Add(d, ud, Tint);
                if (Vector3.Dot(n, expected) >= 0f) { T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3); }
                else { T.Add(i); T.Add(i + 2); T.Add(i + 1); T.Add(i); T.Add(i + 3); T.Add(i + 2); }
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 expected)
                => Quad(a, b, c, d, expected, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));

            public void QuadUp(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uvScale)
                => Quad(a, b, c, d, Vector3.up, new Vector2(a.x, a.z) / uvScale, new Vector2(b.x, b.z) / uvScale, new Vector2(c.x, c.z) / uvScale, new Vector2(d.x, d.z) / uvScale);

            public void QuadBoth(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 expected)
            {
                Quad(a, b, c, d, expected);
                Quad(a, b, c, d, -expected);
            }

            public void QuadBothUV(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 expected, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                Quad(a, b, c, d, expected, ua, ub, uc, ud);
                Quad(a, b, c, d, -expected, ua, ub, uc, ud);
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 expected, Vector2 ua, Vector2 ub, Vector2 uc)
            {
                int i = Add(a, ua, Tint); Add(b, ub, Tint); Add(c, uc, Tint);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), expected) >= 0f) { T.Add(i); T.Add(i + 1); T.Add(i + 2); }
                else { T.Add(i); T.Add(i + 2); T.Add(i + 1); }
            }

            public int Vertex(Vector3 p, Vector2 uv, Color c) => Add(p, uv, c);
            public void Index(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }

            // Box with outward faces. Axes: x/z horizontal, y up; yaw rotates around Y.
            public void Box(Vector3 center, Vector3 size, float yawDeg = 0f, float uvScale = 2f)
            {
                var rot = Quaternion.Euler(0f, yawDeg, 0f);
                var e = size * 0.5f;
                Vector3 P(float sx, float sy, float sz) => center + rot * new Vector3(sx * e.x, sy * e.y, sz * e.z);
                var faces = new[]
                {
                    (P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Vector3.up),
                    (P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), P(-1, -1, -1), Vector3.down),
                    (P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), P(1, -1, 1), rot * Vector3.forward),
                    (P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), P(-1, -1, -1), rot * Vector3.back),
                    (P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), P(1, -1, -1), rot * Vector3.right),
                    (P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), rot * Vector3.left),
                };
                foreach (var (a, b, c, d, n) in faces)
                    Quad(a, b, c, d, n, new Vector2(a.x + a.z, a.y) / uvScale, new Vector2(b.x + b.z, b.y) / uvScale, new Vector2(c.x + c.z, c.y) / uvScale, new Vector2(d.x + d.z, d.y) / uvScale);
            }

            public Mesh Bake(string name)
            {
                var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(V); m.SetUVs(0, U);
                if (UseColors) m.SetColors(C);
                m.SetTriangles(T, 0);
                if (N.Count == V.Count && N.Count > 0) m.SetNormals(N); else m.RecalculateNormals();
                if (NormalUp > 0f)
                {
                    var ns = m.normals;
                    for (int i = 0; i < ns.Length; i++) ns[i] = (ns[i] + Vector3.up * NormalUp).normalized;
                    m.normals = ns;
                }
                m.RecalculateBounds();
                return m;
            }
        }

        // Output container: meshes are grouped by category, 500 m chunk and material. Each
        // category becomes ONE asset file with the chunks as sub-assets, so a rebuild deletes
        // the file and no stale chunk survives.
        sealed class Sink
        {
            readonly Dictionary<string, MeshBuilder> map = new Dictionary<string, MeshBuilder>();
            public readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
            public readonly HashSet<string> ColliderCats = new HashSet<string>();
            public readonly Dictionary<string, bool> CastShadows = new Dictionary<string, bool>();

            public MeshBuilder B(string cat, string mat, Vector3 at, bool colors = false)
            {
                var key = cat + "|" + Mathf.FloorToInt(at.x / ChunkSize) + "|" + Mathf.FloorToInt(at.z / ChunkSize) + "|" + mat;
                if (!map.TryGetValue(key, out var b)) { b = new MeshBuilder { UseColors = colors }; map[key] = b; }
                return b;
            }

            public MeshBuilder Single(string cat, string mat, bool colors = false)
            {
                var key = cat + "|s|s|" + mat;
                if (!map.TryGetValue(key, out var b)) { b = new MeshBuilder { UseColors = colors }; map[key] = b; }
                return b;
            }

            public int ChunkCount => map.Count(kv => kv.Value.T.Count > 0);
            public long Triangles => map.Values.Sum(b => (long)b.T.Count / 3);

            public int Flush(Transform root, string folder, Action<GameObject, string> configure)
            {
                int created = 0;
                var byCat = map.Where(kv => kv.Value.T.Count > 0).GroupBy(kv => kv.Key.Split('|')[0]).OrderBy(g => g.Key);
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var cat in byCat)
                    {
                        var path = folder + "/" + cat.Key + ".asset";
                        AssetDatabase.DeleteAsset(path);
                        var parent = new GameObject(cat.Key).transform;
                        parent.SetParent(root, false);
                        Mesh main = null;
                        var pending = new List<(Mesh mesh, string mat, string name)>();
                        foreach (var kv in cat.OrderBy(k => k.Key, StringComparer.Ordinal))
                        {
                            var parts = kv.Key.Split('|');
                            var mesh = kv.Value.Bake(cat.Key + "-" + parts[1] + "_" + parts[2] + "-" + parts[3]);
                            pending.Add((mesh, parts[3], mesh.name));
                        }
                        foreach (var item in pending)
                        {
                            if (main == null) { AssetDatabase.CreateAsset(item.mesh, path); main = item.mesh; }
                            else AssetDatabase.AddObjectToAsset(item.mesh, path);
                        }
                        foreach (var item in pending)
                        {
                            var go = new GameObject(item.name);
                            go.transform.SetParent(parent, false);
                            go.AddComponent<MeshFilter>().sharedMesh = item.mesh;
                            var mr = go.AddComponent<MeshRenderer>();
                            mr.sharedMaterial = Mats[item.mat];
                            if (CastShadows.TryGetValue(cat.Key, out var cast) && !cast) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                            configure?.Invoke(go, cat.Key);
                            if (ColliderCats.Contains(cat.Key))
                            {
                                go.layer = RoadLayer;
                                go.AddComponent<MeshCollider>().sharedMesh = item.mesh;
                            }
                            created++;
                        }
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }
                return created;
            }
        }

        const int RoadLayer = 30;

        static Vector3 Right(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-8f) return Vector3.right;
            return Vector3.Cross(Vector3.up, forward).normalized;
        }

        // Per-vertex right vectors for a polyline (horizontal, averaged over neighbours).
        static Vector3[] Rights(Vector3[] p)
        {
            var r = new Vector3[p.Length];
            for (int i = 0; i < p.Length; i++)
                r[i] = Right(p[Mathf.Min(i + 1, p.Length - 1)] - p[Mathf.Max(i - 1, 0)]);
            return r;
        }

        // Sweeps a closed or open profile along a polyline. Profile points are (outward, up)
        // relative to an edge at lateral `edge[i] * side`; `side` +1 is the right-hand edge.
        // Quads face the left-hand side of the profile direction (outward for clockwise profiles).
        static void Sweep(MeshBuilder b, Vector3[] p, Vector3[] r, float[] edge, float side, Vector2[] profile,
            Func<int, bool> keep = null, float uvScale = 3f, float along0 = 0f, float[] scale = null)
        {
            float dist = along0;
            for (int i = 0; i < p.Length - 1; i++)
            {
                float seg = Vector3.Distance(p[i], p[i + 1]);
                if (keep != null && (!keep(i) || !keep(i + 1))) { dist += seg; continue; }
                float vAcc = 0f;
                for (int k = 0; k < profile.Length - 1; k++)
                {
                    var pa = profile[k]; var pb = profile[k + 1];
                    float len = Vector2.Distance(pa, pb);
                    Vector3 Pos(int idx, Vector2 q) => p[idx] + r[idx] * (side * (edge[idx] + q.x * (scale != null ? scale[idx] : 1f))) + Vector3.up * q.y;
                    var a0 = Pos(i, pa); var b0 = Pos(i, pb); var b1 = Pos(i + 1, pb); var a1 = Pos(i + 1, pa);
                    var dir = pb - pa;
                    // left of the profile direction in (right, up) space, mirrored for the left edge
                    var expected = r[i] * (side * -dir.y) + Vector3.up * dir.x;
                    b.Quad(a0, b0, b1, a1, expected,
                        new Vector2(dist / uvScale, vAcc / uvScale), new Vector2(dist / uvScale, (vAcc + len) / uvScale),
                        new Vector2((dist + seg) / uvScale, (vAcc + len) / uvScale), new Vector2((dist + seg) / uvScale, vAcc / uvScale));
                    vAcc += len;
                }
                dist += seg;
            }
        }

        // ---------- ground sampling (shared by builders and the sweep) ----------

        static float GroundH(float x, float z)
        {
            var g = ground;
            float gx = (x - g.x0) / g.step, gz = (z - g.z0) / g.step;
            int c = Mathf.Clamp(Mathf.FloorToInt(gx), 0, g.cols - 2), r = Mathf.Clamp(Mathf.FloorToInt(gz), 0, g.rows - 2);
            float tx = Mathf.Clamp01(gx - c), tz = Mathf.Clamp01(gz - r);
            float h00 = g.h[r * g.cols + c], h10 = g.h[r * g.cols + c + 1], h01 = g.h[(r + 1) * g.cols + c], h11 = g.h[(r + 1) * g.cols + c + 1];
            return (h00 * (1 - tx) + h10 * tx) * (1 - tz) + (h01 * (1 - tx) + h11 * tx) * tz;
        }

        static int GroundClass(float x, float z)
        {
            var g = ground;
            int c = Mathf.Clamp(Mathf.RoundToInt((x - g.x0) / g.step), 0, g.cols - 1);
            int r = Mathf.Clamp(Mathf.RoundToInt((z - g.z0) / g.step), 0, g.rows - 1);
            return g.c[r * g.cols + c];
        }

        // ---------- uniform grid for nearest-course queries ----------

        sealed class PointGrid
        {
            readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            readonly Vector3[] pts;
            readonly float cell;
            public PointGrid(Vector3[] points, float cellSize)
            {
                pts = points; cell = cellSize;
                for (int i = 0; i < points.Length; i++)
                {
                    long k = Key(points[i].x, points[i].z);
                    if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>();
                    l.Add(i);
                }
            }
            long Key(float x, float z) => ((long)Mathf.FloorToInt(x / cell) << 32) ^ (uint)Mathf.FloorToInt(z / cell);
            public int Nearest(Vector3 p, float maxDist, out float dist)
            {
                int best = -1; float bd = maxDist * maxDist;
                int rr = Mathf.CeilToInt(maxDist / cell);
                int cx = Mathf.FloorToInt(p.x / cell), cz = Mathf.FloorToInt(p.z / cell);
                for (int dx = -rr; dx <= rr; dx++)
                    for (int dz = -rr; dz <= rr; dz++)
                    {
                        long k = ((long)(cx + dx) << 32) ^ (uint)(cz + dz);
                        if (!cells.TryGetValue(k, out var l)) continue;
                        foreach (int i in l)
                        {
                            float ddx = pts[i].x - p.x, ddz = pts[i].z - p.z;
                            float d = ddx * ddx + ddz * ddz;
                            if (d < bd) { bd = d; best = i; }
                        }
                    }
                dist = best < 0 ? float.MaxValue : Mathf.Sqrt(bd);
                return best;
            }
            public bool Any(Vector3 p, float radius) => Nearest(p, radius, out _) >= 0;
            /// <summary>Indices of every point within `radius` (horizontal distance).</summary>
            public void Within(Vector3 p, float radius, List<int> result)
            {
                result.Clear();
                float rr = radius * radius;
                int span = Mathf.CeilToInt(radius / cell);
                int cx = Mathf.FloorToInt(p.x / cell), cz = Mathf.FloorToInt(p.z / cell);
                for (int dx = -span; dx <= span; dx++)
                    for (int dz = -span; dz <= span; dz++)
                    {
                        long k = ((long)(cx + dx) << 32) ^ (uint)(cz + dz);
                        if (!cells.TryGetValue(k, out var l)) continue;
                        foreach (int i in l)
                        {
                            float ddx = pts[i].x - p.x, ddz = pts[i].z - p.z;
                            if (ddx * ddx + ddz * ddz <= rr) result.Add(i);
                        }
                    }
            }
        }
    }
}
