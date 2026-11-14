using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // Checks of the assembled landmark scene that need no play mode. Bridge command: landmarks sweep.
    public static class BusanLandmarkChecks
    {
        const float Tolerance = 1.2f;            // VehicleController.SurfaceTolerance: the car takes the surface nearest its height within this
        const float StepLimit = 0.05f;           // a jump in the surface between neighbouring samples, beyond the road's own rise
        const float NearGap = 0.3f, FarGap = 4.5f;   // another driving surface this far above or below is a conflict
        static readonly float[] Laterals = { -0.8f, 0f, 0.8f };
        const int Examples = 3;

        // Opens the assembled scene and, along every route of LandmarkNetwork.json, casts rays down on the road layer every metre on
        // the lane line and 0.8 m to its left and right. Per route:
        //   missing   samples with no road-layer surface within 1.2 m of the route's height there (the car would stop or slide)
        //   steps     neighbouring samples whose surfaces differ by more than 0.05 m: along a line, after taking out the route's own
        //             rise between them; across, between the left, centre and right sample at the same s (the roads have no camber)
        //   conflicts another road-layer surface 0.3 to 4.5 m above or below the one found (plan 1.5)
        // with counts and the first three places of each. The full text also goes to Temp/BusanBridge/landmark-sweep.txt.
        // `ignore`: collider names whose hits are left out (a check of the check: the scene itself is not touched).
        public static string Sweep(ICollection<string> ignore = null)
        {
            if (!File.Exists(Path.GetFullPath(BusanLandmarkScene.ScenePath))) return "ERR no " + BusanLandmarkScene.ScenePath + " (run landmarks build first)";
            if (EditorSceneManager.GetActiveScene().path != BusanLandmarkScene.ScenePath)
            {
                for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                {
                    var open = EditorSceneManager.GetSceneAt(i);
                    if (open.isDirty) return "REFUSED unsaved changes in " + (string.IsNullOrEmpty(open.path) ? "an untitled scene" : open.path);
                }
                EditorSceneManager.OpenScene(BusanLandmarkScene.ScenePath, OpenSceneMode.Single);
            }
            Physics.SyncTransforms();
            int mask = 1 << BusanDistrictBuilder.RoadLayer;
            var colliders = Object.FindObjectsOfType<MeshCollider>().Where(c => c.enabled && c.gameObject.activeInHierarchy && c.gameObject.layer == BusanDistrictBuilder.RoadLayer).ToArray();
            var net = BusanLandmarkNetwork.Load();
            var sb = new StringBuilder();
            string Owner(Transform t) => t.root != t ? t.root.name + "/" : "";
            if (ignore != null && ignore.Count > 0) sb.Append($"IGNORING hits on [{string.Join(",", ignore)}] ");
            sb.Append($"scene={BusanLandmarkScene.ScenePath} roadColliders={colliders.Length} [{string.Join(" ", colliders.Select(c => Owner(c.transform) + c.name + ":" + (c.sharedMesh != null ? c.sharedMesh.triangles.Length / 3 : 0) + "tris"))}]");
            int faults = 0;
            var hits = new RaycastHit[32];

            // every road-layer hit between two heights at (x, z), one per collider (RaycastAll gives each collider's first)
            int Band(float x, float z, float top, float bottom)
            {
                if (top <= bottom) return 0;
                int n = Physics.RaycastNonAlloc(new Vector3(x, top, z), Vector3.down, hits, top - bottom, mask, QueryTriggerInteraction.Ignore);
                if (ignore == null || ignore.Count == 0) return n;
                int kept = 0;
                for (int h = 0; h < n; h++) if (!ignore.Contains(hits[h].collider.name)) hits[kept++] = hits[h];
                return kept;
            }
            string Describe(int count) => count == 0 ? "none" : string.Join("/", Enumerable.Range(0, count).Select(h => hits[h].point.y.ToString("0.00") + "@" + hits[h].collider.name));

            for (int k = 0; k < net.routes.Count; k++)
            {
                var route = net.routes[k];
                int samples = 0, missing = 0, steps = 0, conflicts = 0;
                var missingAt = new List<string>(); var stepAt = new List<string>(); var conflictAt = new List<string>();
                var prevRel = new float[Laterals.Length];
                var firstRel = new float[Laterals.Length];
                for (int j = 0; j < Laterals.Length; j++) prevRel[j] = firstRel[j] = float.NaN;
                int count = Mathf.FloorToInt(route.Length);
                for (int si = 0; si <= count; si++)
                {
                    float s = si;
                    if (route.closed && s >= route.Length) break;
                    float y = route.PointAt(s).y;
                    var rel = new float[Laterals.Length];
                    for (int j = 0; j < Laterals.Length; j++)
                    {
                        float lat = Laterals[j];
                        var p = route.LanePointAt(s, lat);
                        samples++;
                        // the surface the car would take: of the hits within the tolerance band, the one nearest the route's height
                        int n = Band(p.x, p.z, y + Tolerance, y - Tolerance);
                        int best = -1; float bestGap = float.MaxValue;
                        for (int h = 0; h < n; h++)
                        {
                            float gap = Mathf.Abs(hits[h].point.y - y);
                            if (gap < bestGap) { bestGap = gap; best = h; }
                        }
                        if (best < 0)
                        {
                            missing++;
                            rel[j] = float.NaN;
                            int all = Band(p.x, p.z, y + 40f, y - 40f);
                            if (missingAt.Count < Examples) missingAt.Add($"s={s:0} lat={lat:+0.0;-0.0;0.0} ({p.x:0.0},{p.z:0.0}) routeY={y:0.00} hitsWithin40m={Describe(all)}");
                            prevRel[j] = float.NaN;
                            continue;
                        }
                        float surface = hits[best].point.y;
                        string on = hits[best].collider.name;
                        rel[j] = surface - y;
                        if (si == 0) firstRel[j] = rel[j];
                        if (!float.IsNaN(prevRel[j]) && Mathf.Abs(rel[j] - prevRel[j]) > StepLimit)
                        {
                            steps++;
                            if (stepAt.Count < Examples) stepAt.Add($"along s={s - 1:0}-{s:0} lat={lat:+0.0;-0.0;0.0} ({p.x:0.0},{p.z:0.0}) {rel[j] - prevRel[j]:+0.000;-0.000} m beyond the route's rise, now {surface:0.00} on {on}");
                        }
                        prevRel[j] = rel[j];
                        // another surface 0.3-4.5 m above or below the one taken
                        int above = Band(p.x, p.z, surface + FarGap, surface + NearGap);
                        string aboveText = above > 0 ? Describe(above) : null;
                        int below = Band(p.x, p.z, surface - NearGap, surface - FarGap);
                        string belowText = below > 0 ? Describe(below) : null;
                        if (above > 0 || below > 0)
                        {
                            conflicts++;
                            if (conflictAt.Count < Examples)
                                conflictAt.Add($"s={s:0} lat={lat:+0.0;-0.0;0.0} ({p.x:0.0},{p.z:0.0}) on {surface:0.00}@{on}" + (aboveText != null ? " above " + aboveText : "") + (belowText != null ? " below " + belowText : ""));
                        }
                    }
                    for (int j = 1; j < Laterals.Length; j++)
                        if (!float.IsNaN(rel[j]) && !float.IsNaN(rel[j - 1]) && Mathf.Abs(rel[j] - rel[j - 1]) > StepLimit)
                        {
                            steps++;
                            if (stepAt.Count < Examples)
                                stepAt.Add($"across s={s:0} lat {Laterals[j - 1]:+0.0;-0.0;0.0}..{Laterals[j]:+0.0;-0.0;0.0} {rel[j] - rel[j - 1]:+0.000;-0.000} m");
                        }
                }
                // a closed route: the last sample and the first are neighbours too
                if (route.closed)
                    for (int j = 0; j < Laterals.Length; j++)
                        if (!float.IsNaN(prevRel[j]) && !float.IsNaN(firstRel[j]) && Mathf.Abs(firstRel[j] - prevRel[j]) > StepLimit)
                        {
                            steps++;
                            if (stepAt.Count < Examples) stepAt.Add($"along the closing join lat={Laterals[j]:+0.0;-0.0;0.0} {firstRel[j] - prevRel[j]:+0.000;-0.000} m");
                        }
                faults += missing + steps + conflicts;
                sb.Append($"\nroute {k} '{route.name}' length={route.Length:0.0} samples={samples} (every 1 m, lane line and +-0.8 m) missing={missing} steps={steps} conflicts={conflicts}");
                foreach (var e in missingAt) sb.Append("\n  missing ").Append(e);
                foreach (var e in stepAt) sb.Append("\n  step ").Append(e);
                foreach (var e in conflictAt) sb.Append("\n  conflict ").Append(e);
            }
            sb.Append($"\nfaults={faults}");
            var text = sb.ToString();
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/BusanBridge"));
            if (Directory.Exists(dir)) File.WriteAllText(Path.Combine(dir, "landmark-sweep.txt"), text);
            return text;
        }

        // landmarks lanes: the middle of every lane of every segment, both directions, every metre: is there a driving surface
        // within 1.2 m of the road's height? Per lane the metres without one, as stretches (distance along the segment).
        // (The sweep above follows the two routes' own lane lines only; a player drives any lane.)
        public static string Lanes()
        {
            var refused = OpenAssembled();
            if (refused != null) return refused;
            Physics.SyncTransforms();
            int mask = 1 << BusanDistrictBuilder.RoadLayer;
            var net = BusanLandmarkNetwork.Load();
            var hits = new RaycastHit[16];
            var sb = new StringBuilder();
            int lanes = 0, samples = 0, missing = 0;
            foreach (var seg in net.segments)
            {
                var p = seg.pts; int n = p.Length;
                var offsets = new List<(string name, float off)>();
                for (int k = 0; k < seg.fwd; k++) offsets.Add(("f" + k, seg.median / 2f + (k + 0.5f) * seg.laneWidth));
                for (int k = 0; k < seg.back; k++) offsets.Add(("b" + k, -(seg.median / 2f + (k + 0.5f) * seg.laneWidth)));
                var right = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    var t = p[Mathf.Min(i + 1, n - 1)] - p[Mathf.Max(i - 1, 0)]; t.y = 0f; t.Normalize();
                    right[i] = new Vector3(t.z, 0f, -t.x);
                }
                foreach (var (name, off) in offsets)
                {
                    lanes++;
                    int miss = 0; float s = 0f, runStart = -1f; var runs = new List<string>();
                    for (int i = 0; i < n - 1; i++)
                    {
                        float len = new Vector2(p[i + 1].x - p[i].x, p[i + 1].z - p[i].z).magnitude;
                        int parts = Mathf.Max(1, Mathf.RoundToInt(len));
                        for (int q = 0; q < parts; q++)
                        {
                            float f = (float)q / parts;
                            var at = Vector3.Lerp(p[i], p[i + 1], f) + Vector3.Lerp(right[i], right[i + 1], f) * off;
                            samples++;
                            int count = Physics.RaycastNonAlloc(new Vector3(at.x, at.y + 1.2f, at.z), Vector3.down, hits, 2.4f, mask, QueryTriggerInteraction.Ignore);
                            float here = s + len * f;
                            if (count == 0) { miss++; missing++; if (runStart < 0f) runStart = here; }
                            else if (runStart >= 0f) { runs.Add($"{runStart:0}-{here:0}"); runStart = -1f; }
                        }
                        s += len;
                    }
                    if (runStart >= 0f) runs.Add($"{runStart:0}-{s:0}");
                    string more = runs.Count > 8 ? " ..." : "";
                    if (miss > 0) sb.Append($"\n  {seg.id} {name} (offset {off:+0.0;-0.0}) length={s:0} missing={miss} m at [{string.Join(" ", runs.Take(8))}{more}]");
                }
            }
            return $"lanes={lanes} samples={samples} missing={missing}" + sb;
        }

        // landmarks shot <png> <px> <py> <pz> <tx> <ty> <tz> [fov] [width] [height]: one picture of the assembled scene from a pose,
        // through the game camera with its post effects and the game's shadows. The scene is reloaded afterwards if it was touched.
        public static string Shot(string path, Vector3 eye, Vector3 target, float fov, int width, int height)
        {
            var refused = OpenAssembled();
            if (refused != null) return refused;
            var cam = Camera.main;
            if (cam == null) return "ERR no main camera in " + BusanLandmarkScene.ScenePath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var t = cam.transform;
            var savedPos = t.position; var savedRot = t.rotation; float savedFov = cam.fieldOfView; bool savedOrtho = cam.orthographic;
            try
            {
                cam.orthographic = false; cam.fieldOfView = fov;
                t.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                using (BusanLandmarkLook.Shadows()) BusanLandmarkLook.Render(cam, path, width, height);
            }
            finally
            {
                t.SetPositionAndRotation(savedPos, savedRot); cam.fieldOfView = savedFov; cam.orthographic = savedOrtho;
                if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.OpenScene(BusanLandmarkScene.ScenePath, OpenSceneMode.Single);
            }
            return $"{path} {width}x{height} fov={fov:0}";
        }

        // Opens the assembled scene unless it is the active one (refused over unsaved changes elsewhere). Null when it is open.
        static string OpenAssembled()
        {
            if (!File.Exists(Path.GetFullPath(BusanLandmarkScene.ScenePath))) return "ERR no " + BusanLandmarkScene.ScenePath + " (run landmarks build first)";
            if (EditorSceneManager.GetActiveScene().path == BusanLandmarkScene.ScenePath) return null;
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var open = EditorSceneManager.GetSceneAt(i);
                if (open.isDirty) return "REFUSED unsaved changes in " + (string.IsNullOrEmpty(open.path) ? "an untitled scene" : open.path);
            }
            EditorSceneManager.OpenScene(BusanLandmarkScene.ScenePath, OpenSceneMode.Single);
            return null;
        }

        const float EyeHeight = 1.35f, EyeFov = 62f, EyePitchUp = 1.6f;
        const int CoverWidth = 640, CoverHeight = 360;

        // Driver's-eye pictures of a whole route in the assembled scene, without play mode (plan.md 6.1): the eye 1.35 m above the
        // road on the route's lane line, looking along the route (with its grade), fov 62, pitched up 1.6 degrees, 640x360, through
        // the game camera and its post effects with the game's shadows (BusanLandmarkLook.Render). Every `step` m a forward view
        // F_<s>.png and its hole pass F_<s>_hole.png (background solid magenta, fog and post effects off); every 2 `step` the views
        // turned 45 degrees left and right (L_<s>, R_<s>); every 5 `step` the view back along the road (B_<s>). Inside a light zone
        // the zone's lighting at that place is applied (as LightZones does in play, without the 1.5 s ease). The player's car is
        // hidden. Poses go to _views.txt. The scene is reloaded from disk afterwards, so nothing of the pass stays in it.
        public static string Coverage(int routeIndex, string outDir, float step)
        {
            var refused = OpenAssembled();
            if (refused != null) return refused;
            if (step < 1f) return "ERR step " + step;
            var net = BusanLandmarkNetwork.Load();
            if (routeIndex < 0 || routeIndex >= net.routes.Count) return $"ERR no route {routeIndex} (the network has {net.routes.Count})";
            var route = net.routes[routeIndex];
            var cam = Camera.main;
            if (cam == null) return "ERR no main camera in " + BusanLandmarkScene.ScenePath;
            Directory.CreateDirectory(outDir);
            var started = System.DateTime.Now;

            var t = cam.transform;
            var savedPos = t.position; var savedRot = t.rotation; float savedFov = cam.fieldOfView;
            var savedClear = cam.clearFlags; var savedBackground = cam.backgroundColor; bool savedOrtho = cam.orthographic; bool savedFog = RenderSettings.fog;
            var effects = cam.GetComponents<Behaviour>().Where(b => b.enabled && b.GetType().Namespace != "UnityEngine" && !(b is Camera)).ToList();
            var car = Object.FindObjectOfType<Parkside.Vehicle.VehicleController>();
            var carRenderers = car != null ? car.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList() : new List<Renderer>();
            var zones = Object.FindObjectOfType<LightZones>();
            int forward = 0, left = 0, right = 0, back = 0, holes = 0, zoned = 0;
            var poses = new StringBuilder("name s x y z yaw pitch zoneWeight\n");
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                foreach (var r in carRenderers) r.enabled = false;
                cam.orthographic = false;
                cam.fieldOfView = EyeFov;
                if (zones != null) zones.Capture(cam);
                using (BusanLandmarkLook.Shadows())
                {
                    for (int i = 0; ; i++)
                    {
                        float s = i * step;
                        if (s >= route.Length - (route.closed ? 0.01f : 0f)) break;
                        var eye = route.LanePointAt(s) + Vector3.up * EyeHeight;
                        var along = route.PointAt(s + 2f) - route.PointAt(route.closed ? s - 2f : Mathf.Max(0f, s - 2f));
                        if (along.sqrMagnitude < 1e-6f) along = route.TangentAt(s);
                        along.Normalize();
                        float w = zones != null ? zones.WeightAt(eye) : 0f;
                        if (zones != null) zones.Apply(w);
                        if (w > 0f) zoned++;
                        string tag = s.ToString("0000", inv);
                        void Shot(string name, float yaw)
                        {
                            var dir = Quaternion.AngleAxis(yaw, Vector3.up) * along;
                            t.SetPositionAndRotation(eye, Quaternion.LookRotation(dir) * Quaternion.Euler(-EyePitchUp, 0f, 0f));
                            BusanLandmarkLook.Render(cam, Path.Combine(outDir, name + ".png"), CoverWidth, CoverHeight);
                            var e = t.eulerAngles;
                            poses.Append(string.Format(inv, "{0} {1:0.0} {2:0.00} {3:0.00} {4:0.00} {5:0.0} {6:0.0} {7:0.00}\n", name, s, eye.x, eye.y, eye.z, e.y, e.x > 180f ? e.x - 360f : e.x, w));
                        }
                        Shot("F_" + tag, 0f); forward++;
                        // the hole pass: what is not drawn shows as magenta
                        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(1f, 0f, 1f, 1f);
                        RenderSettings.fog = false;
                        foreach (var b in effects) b.enabled = false;
                        Shot("F_" + tag + "_hole", 0f); holes++;
                        cam.clearFlags = savedClear; cam.backgroundColor = savedBackground;
                        RenderSettings.fog = savedFog;
                        foreach (var b in effects) b.enabled = true;
                        if (i % 2 == 0) { Shot("L_" + tag, -45f); left++; Shot("R_" + tag, 45f); right++; }
                        if (i % 5 == 0) { Shot("B_" + tag, 180f); back++; }
                    }
                }
            }
            finally
            {
                if (zones != null) zones.Restore();
                t.SetPositionAndRotation(savedPos, savedRot);
                cam.fieldOfView = savedFov; cam.orthographic = savedOrtho;
                cam.clearFlags = savedClear; cam.backgroundColor = savedBackground;
                RenderSettings.fog = savedFog;
                foreach (var b in effects) b.enabled = true;
                foreach (var r in carRenderers) if (r != null) r.enabled = true;
                File.WriteAllText(Path.Combine(outDir, "_views.txt"), poses.ToString());
                // drop whatever the pass touched in memory (camera, lighting): the scene file is as it was
                if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.OpenScene(BusanLandmarkScene.ScenePath, OpenSceneMode.Single);
            }
            int total = forward + left + right + back + holes;
            return $"route {routeIndex} '{route.name}' length={route.Length:0.0} step={step:0.#} F={forward} L={left} R={right} B={back} hole={holes} " +
                   $"total={total} pictures {CoverWidth}x{CoverHeight} viewsInLightZone={zoned} lightZones={(zones != null ? zones.ZoneCount : 0)} " +
                   $"seconds={(System.DateTime.Now - started).TotalSeconds:0} outDir={outDir}";
        }
    }
}
