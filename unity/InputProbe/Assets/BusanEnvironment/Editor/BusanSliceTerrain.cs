using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    public static partial class BusanSliceBuilder
    {
        // Land use as WEIGHTS for SliceGround.shader: r grass (park), g forest, b beach, a sea floor; all zero = urban.
        static readonly Color[] GroundColors =
        {
            new Color(0f, 0f, 0f, 0f),   // urban
            new Color(0f, 0f, 0f, 1f),   // sea floor
            new Color(0f, 0f, 1f, 0f),   // beach
            new Color(1f, 0f, 0f, 0f),   // park / grass
            new Color(0f, 1f, 0f, 0f),   // forest
        };

        static void BuildGround()
        {
            var g = ground;
            const int block = 24;
            int tris = 0;
            for (int r0 = 0; r0 < g.rows - 1; r0 += block)
                for (int c0 = 0; c0 < g.cols - 1; c0 += block)
                {
                    int c1 = Mathf.Min(c0 + block, g.cols - 1), r1 = Mathf.Min(r0 + block, g.rows - 1);
                    var b = sink.B("Ground", "ground", new Vector3(g.x0 + c0 * g.step, 0f, g.z0 + r0 * g.step), true);
                    int w = c1 - c0 + 1;
                    int baseIndex = b.V.Count;
                    for (int r = r0; r <= r1; r++)
                        for (int c = c0; c <= c1; c++)
                        {
                            float x = g.x0 + c * g.step, z = g.z0 + r * g.step;
                            float h = g.h[r * g.cols + c];
                            float hl = g.h[r * g.cols + Mathf.Max(c - 1, 0)], hr = g.h[r * g.cols + Mathf.Min(c + 1, g.cols - 1)];
                            float hd = g.h[Mathf.Max(r - 1, 0) * g.cols + c], hu = g.h[Mathf.Min(r + 1, g.rows - 1) * g.cols + c];
                            b.N.Add(new Vector3(hl - hr, 2f * g.step, hd - hu).normalized);
                            b.Vertex(new Vector3(x, h, z), new Vector2(x, z) / 20f, GroundColors[Mathf.Clamp(g.c[r * g.cols + c], 0, 4)]);
                        }
                    for (int r = r0; r < r1; r++)
                        for (int c = c0; c < c1; c++)
                        {
                            bool allSea = true;
                            foreach (var (rr, cc) in new[] { (r, c), (r, c + 1), (r + 1, c), (r + 1, c + 1) })
                                if (g.c[rr * g.cols + cc] != 1) { allSea = false; break; }
                            if (allSea) continue;
                            int v00 = baseIndex + (r - r0) * w + (c - c0), v10 = v00 + 1, v01 = v00 + w, v11 = v01 + 1;
                            b.Index(v00, v01, v10); b.Index(v10, v01, v11);
                            tris += 2;
                        }
                }

            // sea: one big plane, flat at sea level
            float minX = g.x0 - 7000f, maxX = g.x0 + g.cols * g.step + 7000f, minZ = g.z0 - 7000f, maxZ = g.z0 + g.rows * g.step + 7000f;
            var sea = sink.Single("Sea", "sea");
            var a0 = new Vector3(minX, 0f, minZ); var a1 = new Vector3(maxX, 0f, minZ); var a2 = new Vector3(maxX, 0f, maxZ); var a3 = new Vector3(minX, 0f, maxZ);
            sea.Quad(a0, a1, a2, a3, Vector3.up, new Vector2(minX, minZ) / 400f, new Vector2(maxX, minZ) / 400f, new Vector2(maxX, maxZ) / 400f, new Vector2(minX, maxZ) / 400f);
            sink.CastShadows["Sea"] = false;

            BuildFarTerrain();
            Log($"GROUND cells={tris / 2}");
        }

        // Coarse DEM patch for the ridge silhouettes; sits 1.5 m under the fine ground where they overlap.
        static void BuildFarTerrain()
        {
            var g = ground;
            int cols = terrain.columns, rows = terrain.rows;
            float x0 = terrain.vertices[0].x, z0 = terrain.vertices[0].z;
            float dx = terrain.vertices[1].x - x0, dz = terrain.vertices[cols].z - z0;
            float fx0 = g.x0, fx1 = g.x0 + (g.cols - 1) * g.step, fz0 = g.z0, fz1 = g.z0 + (g.rows - 1) * g.step;
            const float reach = 4500f;
            int c0 = Mathf.Clamp(Mathf.FloorToInt((fx0 - reach - x0) / dx), 0, cols - 2), c1 = Mathf.Clamp(Mathf.CeilToInt((fx1 + reach - x0) / dx), c0 + 1, cols - 1);
            int r0 = Mathf.Clamp(Mathf.FloorToInt((fz0 - reach - z0) / dz), 0, rows - 2), r1 = Mathf.Clamp(Mathf.CeilToInt((fz1 + reach - z0) / dz), r0 + 1, rows - 1);
            var b = sink.Single("FarTerrain", "ground", true);
            int w = c1 - c0 + 1;
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    var v = terrain.vertices[r * cols + c];
                    float dem = v.y;
                    float y = dem <= 0f ? Mathf.Min(dem, -1.2f) : dem + 2.75f - 1.5f;
                    var col = dem <= 0f ? GroundColors[1] : dem > 14f ? GroundColors[4] : GroundColors[0];
                    b.Vertex(new Vector3(v.x, y, v.z), new Vector2(v.x, v.z) / 20f, col);
                }
            for (int r = r0; r < r1; r++)
                for (int c = c0; c < c1; c++)
                {
                    var p = terrain.vertices[r * cols + c];
                    float cx = p.x + dx * 0.5f, cz = p.z + dz * 0.5f;
                    if (cx > fx0 && cx < fx1 && cz > fz0 && cz < fz1) continue; // hidden under the fine ground
                    float h0 = terrain.vertices[r * cols + c].y, h1 = terrain.vertices[r * cols + c + 1].y, h2 = terrain.vertices[(r + 1) * cols + c].y, h3 = terrain.vertices[(r + 1) * cols + c + 1].y;
                    if (h0 <= 0f && h1 <= 0f && h2 <= 0f && h3 <= 0f) continue;
                    int v00 = (r - r0) * w + (c - c0), v10 = v00 + 1, v01 = v00 + w, v11 = v01 + 1;
                    b.Index(v00, v01, v10); b.Index(v10, v01, v11);
                }
        }
    }
}
