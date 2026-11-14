using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // The assembled landmark map (plan.md 2.6): Landmarks/BusanLandmarks.unity, a file copy of ParkingPractice.unity (new GUID; the
    // original is only read) without its Environment, with every district prefab that exists, the landmark look, and the player's
    // car set up as in BusanDrive (BusanDriveRig).
    // Every route of LandmarkNetwork.json is one of DriveAssist's routes (closed; the two share road up to the fork and after the
    // merge), with every jump on its route; the car spawns on route spawn.route at spawn.s. LightZones darkens the tunnel and
    // DemoCapture waits for its command line. No traffic. The build settings are never touched. Bridge command: landmarks build.
    public static class BusanLandmarkScene
    {
        public const string ScenePath = BusanDistrictBuilder.Root + "/BusanLandmarks.unity";
        const string SourceScene = "Assets/_Parkside/Scenes/ParkingPractice.unity";
        static readonly string[] Districts = { "ring", "gwangalli", "bridge", "haeundae", "seomyeon", "infill", "trees" };
        const float SunYaw = 0f, SunPitch = 38f;     // plan 1.1: one sun, due south, as every district is generated and lit
        const float SpawnLift = 0.15f;               // PracticeSpawn above the road

        [MenuItem("Busan/Landmarks/Build assembled scene")]
        static void BuildMenu() => Debug.Log("LANDMARKS " + Build());

        public static string Build()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var open = EditorSceneManager.GetSceneAt(i);
                if (open.isDirty) return "REFUSED unsaved changes in " + (string.IsNullOrEmpty(open.path) ? "an untitled scene" : open.path);
            }
            var net = BusanLandmarkNetwork.Load();
            if (net.spawnRoute < 0 || net.spawnRoute >= net.routes.Count) return "ERR spawn route " + net.spawnRoute + " not in the network";
            var route = net.routes[net.spawnRoute];
            var notes = new List<string>();

            // ---- the scene: a file copy (no .meta: a new GUID the first time, kept on later builds), opened, Environment removed
            var setup = EditorSceneManager.GetSceneManagerSetup();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // release the old file before overwriting it
            File.Copy(Path.GetFullPath(SourceScene), Path.GetFullPath(ScenePath), true);
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            // the parking lot, and its sun (BusanLandmarkLook makes the map's)
            foreach (var name in new[] { "Environment", "Sun" })
            {
                var old = GameObject.Find(name);
                if (old != null) UnityEngine.Object.DestroyImmediate(old);
                else notes.Add("no " + name + " to remove");
            }

            // ---- the districts
            var placed = new List<string>();
            Cubemap reflection = null;
            foreach (var d in Districts)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BusanDistrictBuilder.PrefabPath(d));
                if (prefab == null) { notes.Add("no district " + d); continue; }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                placed.Add(instance.name);
                if (reflection == null)
                    reflection = instance.GetComponentsInChildren<ReflectionProbe>().Select(p => p.customBakedTexture as Cubemap).FirstOrDefault(c => c != null);
            }

            // ---- the look: sun, sky, trilight, sea haze as the district builders; the default reflection (what renderers outside
            // every probe box get) is the first district's first cubemap, as the Gwangalli builder leaves its scene, because the
            // sky default is black in a scene without baked lighting
            BusanLandmarkLook.Apply(SunYaw, SunPitch);
            if (reflection != null)
            {
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = reflection;
            }
            else notes.Add("no reflection cubemap: default reflection stays the sky");

            // ---- the player's car, camera, keyboard and HUD as in BusanDrive
            var rig = BusanDriveRig.Apply(notes);

            // ---- the courses: every route of the network, as the json has it (both closed), in DriveAssist's routes; the single-course
            // fields stay empty. Every jump goes on its own route. DriveAssist measures distance in 3D, the json flat (spawn, jumps):
            // jump distances are converted along each route (equal where it is level, ~1 m more over the bridge).
            var courses = new List<CourseRoute>();
            var flatAt = new List<float[]>(); var alongAt = new List<float[]>();   // per route: flat and 3D distance at each point, then back at the start
            foreach (var r in net.routes)
            {
                courses.Add(new CourseRoute { name = r.name, closed = r.closed, path = r.path, laneOffsets = r.lane, autoSpeeds = r.speed });
                int n = r.path.Length;
                var flat = new float[n + 1]; var along3 = new float[n + 1];
                for (int i = 1; i < n; i++) { flat[i] = r.cum[i]; along3[i] = along3[i - 1] + Vector3.Distance(r.path[i - 1], r.path[i]); }
                flat[n] = r.Length; along3[n] = along3[n - 1] + (r.closed ? Vector3.Distance(r.path[n - 1], r.path[0]) : 0f);
                flatAt.Add(flat); alongAt.Add(along3);
            }
            var jumpNames = new List<string>();
            var jumpAt = new List<float>();
            var jumpRoutes = new List<int>();
            foreach (var j in net.jumps)
            {
                if (j.route < 0 || j.route >= net.routes.Count) { notes.Add($"jump {j.name} is on route {j.route}, not in the network: left out"); continue; }
                var jr = net.routes[j.route];
                float s = jr.closed ? (j.s % jr.Length + jr.Length) % jr.Length : Mathf.Clamp(j.s, 0f, jr.Length);
                jumpNames.Add(j.name);
                jumpRoutes.Add(j.route);
                jumpAt.Add(Interpolate(flatAt[j.route], alongAt[j.route], s));
            }

            // ---- spawn on the spawn route's lane line at spawn.s (the line DriveAssist follows and the ring paves), facing along it
            var t0 = route.TangentAt(net.spawnS);
            var spawnPos = route.LanePointAt(net.spawnS) + Vector3.up * SpawnLift;
            if (!BusanDriveRig.PlaceSpawn(spawnPos, Quaternion.LookRotation(t0))) notes.Add("no PracticeSpawn");

            // ---- play area: the network's box plus 380 m (190 m each side), 700 m high about y 80; DriveAssist returns the car to
            // the course 40 m inside that on every side, before GameManager would reset it
            net.FlatExtent(out float minX, out float maxX, out float minZ, out float maxZ);
            var practice = new Bounds(new Vector3((minX + maxX) / 2f, 80f, (minZ + maxZ) / 2f), new Vector3(maxX - minX + 380f, 700f, maxZ - minZ + 380f));
            var drive = practice;
            drive.Expand(new Vector3(-80f, 0f, -80f));
            BusanDriveRig.SetPracticeBounds(rig, practice);

            var driveRoot = new GameObject("Drive");
            var assist = BusanDriveRig.AddDriveAssist(rig, driveRoot.transform, new Vector3[0], new float[0], new float[0], drive, jumpNames, jumpAt, null,
                                                      courses, net.spawnRoute, jumpRoutes);
            bool assistMade = assist != null && rig.vehicle != null && rig.gameManager != null;

            // ---- light zones (the tunnel): darker ambient, reflection and a higher exposure while the camera is inside
            var zonesGo = new GameObject("LightZones");
            zonesGo.transform.SetParent(driveRoot.transform, false);
            var zones = zonesGo.AddComponent<LightZones>();
            var zso = new SerializedObject(zones);
            var zp = zso.FindProperty("zones");
            zp.arraySize = net.zones.Count;
            for (int i = 0; i < net.zones.Count; i++)
            {
                var z = net.zones[i];
                var e = zp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = (z.kind + " " + z.segment).Trim();
                e.FindPropertyRelative("centre").vector3Value = z.centre;
                e.FindPropertyRelative("axis").vector2Value = z.axis;
                e.FindPropertyRelative("half").vector3Value = z.half;
                e.FindPropertyRelative("ramp").floatValue = z.ramp;
            }
            zso.ApplyModifiedPropertiesWithoutUndo();

            // ---- demo capture: idle unless the player is started with -democapture (plan.md 10)
            var captureGo = new GameObject("DemoCapture");
            captureGo.transform.SetParent(driveRoot.transform, false);
            var capture = captureGo.AddComponent<DemoCapture>();
            var cso = new SerializedObject(capture);
            cso.FindProperty("assist").objectReferenceValue = assist;
            cso.FindProperty("vehicleCamera").objectReferenceValue = rig.vehicleCamera;
            cso.ApplyModifiedPropertiesWithoutUndo();

            // ---- moving cars (tools/network.py, LandmarkTraffic.json): the baked traffic models and their paints, as RouteTraffic uses them
            string trafficNote = AddTraffic(driveRoot.transform, rig.vehicle != null ? rig.vehicle.transform : null);

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            string validator;
            try { validator = "ok checks=" + Parkside.Editor.PracticeSceneValidator.ValidateScene(scene); }
            catch (Exception e) { validator = "FAILED " + e.Message; }
            string reopened = Restore(setup);
            if (!saved) return "ERR scene not saved: " + ScenePath;

            long bytes = new FileInfo(Path.GetFullPath(ScenePath)).Length;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string Course(int k)
            {
                var r = net.routes[k];
                return $"{k} '{r.name}' {(r.closed ? "closed" : "open")} points={r.path.Length} flat={r.Length:0.0}m (json {r.declaredLength:0.0}) " +
                       $"driveAssist={alongAt[k][alongAt[k].Length - 1]:0.0}m speeds={r.speed.Min():0.0}-{r.speed.Max():0.0}m/s lanes={r.lane.Min():0.00}-{r.lane.Max():0.00}m";
            }
            return $"{ScenePath} {bytes / 1048576.0:0.00}MB districts=[{string.Join(",", placed)}] routes=[{string.Join("; ", Enumerable.Range(0, net.routes.Count).Select(Course))}] " +
                   $"preferred={net.spawnRoute} jumps=[{string.Join(", ", jumpNames.Select((n, i) => n + "@" + jumpRoutes[i] + ":" + jumpAt[i].ToString("0.0", inv)))}] " +
                   $"spawn=route {net.spawnRoute} s={net.spawnS:0.0} ({spawnPos.x:0.0},{spawnPos.y:0.00},{spawnPos.z:0.0}) yaw={Quaternion.LookRotation(t0).eulerAngles.y:0.0} " +
                   $"lightZones=[{string.Join(", ", net.zones.Select(z => $"{z.kind} {z.segment} c({z.centre.x:0.0},{z.centre.y:0.0},{z.centre.z:0.0}) half({z.half.x:0},{z.half.y:0},{z.half.z:0}) ramp {z.ramp:0}"))}] demoCapture=yes " +
                   $"practiceBounds=c({practice.center.x:0},{practice.center.y:0},{practice.center.z:0}) s({practice.size.x:0},{practice.size.y:0},{practice.size.z:0}) " +
                   $"driveBounds=s({drive.size.x:0},{drive.size.y:0},{drive.size.z:0}) reflection={(reflection != null ? reflection.name : "sky")} " +
                   $"driveAssist+vehicle+gameManager={assistMade} traffic={trafficNote} validator={validator} notes=[{string.Join("; ", notes)}] reopened={reopened}";
        }

        const string TrafficDataPath = "Assets/BusanEnvironment/Editor/Data/LandmarkTraffic.json";
        const string TrafficCarDir = "Assets/BusanEnvironment/GeneratedSlice/TrafficCars";
        [Serializable] class TrafficFile { public List<TrafficLine> paths; }
        const string SignalDataPath = "Assets/BusanEnvironment/Editor/Data/LandmarkSignals.json";
        [Serializable] class SignalFile { public float[] x, z, ux, uz, half; }
        [Serializable] class TrafficLine { public string name; public bool closed; public int cars; public float[] x, y, z, speed, wideL, wideR; }

        static string AddTraffic(Transform parent, Transform player)
        {
            string file = Path.GetFullPath(TrafficDataPath);
            if (!File.Exists(file)) return "none (no LandmarkTraffic.json)";
            var data = JsonUtility.FromJson<TrafficFile>(File.ReadAllText(file));
            if (data == null || data.paths == null || data.paths.Count == 0) return "none (empty file)";
            var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { TrafficCarDir })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(g => g != null && g.GetComponent<CarModel>() != null).OrderBy(g => g.name).ToArray();
            if (prefabs.Length == 0) return "none (no baked cars in " + TrafficCarDir + ")";
            var paints = AssetDatabase.FindAssets("mat-car- t:Material", new[] { "Assets/BusanEnvironment" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(m => m != null && m.name.StartsWith("mat-car-")).OrderBy(m => m.name).ToArray();
            var lines = data.paths.Select(t => new LoopTraffic.Line
            {
                name = t.name, closed = t.closed, cars = t.cars, speed = t.speed, wideL = t.wideL, wideR = t.wideR,
                points = Enumerable.Range(0, t.x.Length).Select(i => new Vector3(t.x[i], t.y[i], t.z[i])).ToArray(),
            }).ToArray();
            var go = new GameObject("Traffic");
            go.transform.SetParent(parent, false);
            var traffic = go.AddComponent<LoopTraffic>();
            traffic.Configure(lines, prefabs, paints, player);
            // the zebra crossings (tools/build_district.py, LandmarkSignals.json): the cars stop before them on red; the signal lamps
            string signalFile = Path.GetFullPath(SignalDataPath);
            int zebraCount = 0;
            if (File.Exists(signalFile))
            {
                var z = JsonUtility.FromJson<SignalFile>(File.ReadAllText(signalFile));
                if (z != null && z.x != null)
                {
                    traffic.SetZebras(Enumerable.Range(0, z.x.Length).Select(i => new Vector4(z.x[i], z.z[i], Mathf.Atan2(z.uz[i], z.ux[i]), z.half[i])).ToArray());
                    zebraCount = z.x.Length;
                }
            }
            go.AddComponent<SignalLights>();
            EditorUtility.SetDirty(traffic);
            return $"[{string.Join(", ", lines.Select(l => $"{l.name} {(l.closed ? "closed" : "open")} {l.points.Length}pts {l.cars}cars"))}] models={prefabs.Length} paints={paints.Length} zebras={zebraCount}";
        }

        // y at x on the polyline (xs ascending)
        static float Interpolate(float[] xs, float[] ys, float x)
        {
            if (x <= xs[0]) return ys[0];
            for (int i = 1; i < xs.Length; i++)
                if (x <= xs[i]) return Mathf.Lerp(ys[i - 1], ys[i], (x - xs[i - 1]) / Mathf.Max(1e-4f, xs[i] - xs[i - 1]));
            return ys[ys.Length - 1];
        }

        // the scenes that were open before, put back
        static string Restore(SceneSetup[] setup)
        {
            if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path)))
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
                return string.Join(",", setup.Select(s => s.path));
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return "(untitled)";
        }
    }
}
