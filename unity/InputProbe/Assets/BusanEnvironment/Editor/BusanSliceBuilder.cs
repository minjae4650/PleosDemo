using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BusanEnvironment.Editor
{
    // Demo drive slice v2: Marine City -> Gwangan Bridge -> Gwangalli.
    // Everything is generated from the JSON written by tools/prepare-busan-slice.py (OSM-derived,
    // 1 unit = 1 m). Elevations, tower height, cables and every estimated building are design
    // values for the contest demo, not survey data.
    public static partial class BusanSliceBuilder
    {
        const string DataDir = "Assets/BusanEnvironment/Editor/Data/";
        const string SourceScene = "Assets/_Parkside/Scenes/ParkingPractice.unity";
        const string ScenePath = "Assets/_Parkside/Scenes/BusanDrive.unity";
        const string Root = "Assets/BusanEnvironment/GeneratedSlice";
        const string MeshDir = Root + "/Meshes";
        const string TexDir = Root + "/tex";
        const string GeneratedName = "Busan Slice - generated, design values";
        const string Facades = "Assets/ParkingLotEnvironment/KoreanAssets/";

        [Serializable] class Vec { public float x, y, z, s; }
        [Serializable] class RoutePoint { public float x, y, z, w; public int k, l, o, el; }
        [Serializable] class Jump { public string name; public float s; }
        [Serializable]
        class RouteData
        {
            public float totalLengthMetres, spawnHeadingDegrees, seaLevel;
            public List<RoutePoint> points;
            public List<Vec> towers;
            public List<Jump> jumps;
            public Vec marineCityCenter;
        }
        [Serializable] class RoadData { public int cls, lanes, oneway, way, L; public float w; public List<float> p; public List<float> ws; }
        [Serializable] class MouthData { public float x, y, z, half, dx, dz; public int cls, ci; }
        [Serializable] class ClosureData { public float x, y, z, dx, dz, w; }
        [Serializable] class JunctionData { public float x, y, z; public List<float> a; }
        [Serializable] class NetworkData { public List<RoadData> roads; public List<JunctionData> junctions; public List<MouthData> mouths; public List<ClosureData> closures; }
        [Serializable] class GroundData { public float x0, z0, step; public int cols, rows; public List<float> h; public List<int> c; }
        [Serializable] class BuildingData { public float h, b, de; public int real, e, infill; public string st; public List<float> p; }
        [Serializable] class BuildingSet { public string attribution; public List<BuildingData> buildings; }
        [Serializable] class CorridorData { public float lane, w; public List<float> p; }
        [Serializable] class CorridorSet { public List<CorridorData> corridors; }
        [Serializable] class TerrainData { public int columns, rows; public List<Vec> vertices; }

        static RouteData route;
        static NetworkData net;
        static GroundData ground;
        static BuildingSet bld;
        static TerrainData terrain;
        static CorridorSet corridorSet;
        static Sink sink;
        static bool unattended;
        static readonly StringBuilder log = new StringBuilder();

        // course arrays
        static Vector3[] cp, cr;
        static float[] chw, csh, ccum;
        static int[] ckind;
        static bool[] celev, coneway;
        static PointGrid courseGrid;
        static float spanStartS, spanEndS;
        static readonly List<(Vector3 pos, float half, int cls, int ci)> mouths = new List<(Vector3, float, int, int)>();

        const int Kcity = 0, Kapproach = 1, Kbridge = 2, Kbeach = 3;

        static void Log(string line)
        {
            log.AppendLine(line);
            Debug.Log("BUSAN_SLICE: " + line);
        }

        public static string BuildUnattended()
        {
            unattended = true;
            try { return Build(); }
            finally { unattended = false; }
        }

        [MenuItem("Busan/Build drive slice scene")]
        public static string BuildMenu() => Build();

        public static string Build()
        {
            var timer = Stopwatch.StartNew();
            log.Clear();
            if (!Application.isBatchMode && !unattended && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "cancelled";

            LoadData();
            var scene = PrepareScene();
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(MeshDir);
            Directory.CreateDirectory(TexDir);
            EnsureTextures();
            sink = new Sink();
            foreach (var cat in new[] { "Roads", "Roads1", "Roads2", "Roads3" }) sink.ColliderCats.Add(cat); // 1..3: roads that overlap another surface 1-4 m above/below (one raycast hit per collider)
            // the alley apron (asphalt to the building fronts) is not drivable: villas stand on it, and their walls have no colliders
            CreateMaterials();
            BuildCarPrefabs();
            PrepareCourse();
            BuildCourseSurface();
            BuildNetwork();
            BuildGround();
            AddFillerBuildings();
            BuildBuildings();
            BuildBridge();
            BuildPlacementIndices();
            placedProps.Clear();
            BuildStreetProps();
            BuildPolesAndWires();
            BuildSignalsAndCrossings();
            BuildPlanting();
            BuildClosures();
            LogPropViolations();

            var root = new GameObject(GeneratedName).transform;
            int objects = sink.Flush(root, MeshDir, null);
            // Colliders created inside the asset-editing batch can keep a stale (empty) physics shape until the mesh is assigned again;
            // the sweep below would then report phantom holes (one chunk of Roads3 did). Reassigning rebuilds every shape.
            int rebuilt = 0, empty = 0;
            foreach (var mc in root.GetComponentsInChildren<MeshCollider>())
            {
                if (mc.bounds.size == Vector3.zero) empty++;
                var m = mc.sharedMesh; mc.sharedMesh = null; mc.sharedMesh = m; rebuilt++;
            }
            Log($"COLLIDERS rebuilt={rebuilt} emptyBeforeRebuild={empty}");
            Log($"MESH chunks={sink.ChunkCount} objects={objects} triangles={sink.Triangles}");
            BuildParkedCars(root);
            TweakScene(root);
            SaveMapPrefabs(root);

            var sweep = SweepColliders(out var sweepText);
            Log(sweepText);
            var logDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/BusanBridge"));
            if (Directory.Exists(logDir)) File.WriteAllText(Path.Combine(logDir, "build-log.txt"), log.ToString());

            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            RegisterBuildScene();
            var checks = Parkside.Editor.PracticeSceneValidator.ValidateScene(scene);
            Log($"VALIDATOR ok checks={checks}");
            timer.Stop();
            var summary = $"length={route.totalLengthMetres}m points={cp.Length} chunks={sink.ChunkCount} tris={sink.Triangles} sweepHoles={sweep} seconds={timer.Elapsed.TotalSeconds:0}";
            Debug.Log("BUSAN_SLICE_OK: " + summary);
            return summary;
        }

        static void LoadData()
        {
            route = JsonUtility.FromJson<RouteData>(File.ReadAllText(DataDir + "BusanSliceRoute.json"));
            net = JsonUtility.FromJson<NetworkData>(File.ReadAllText(DataDir + "BusanSliceNetwork.json"));
            ground = JsonUtility.FromJson<GroundData>(File.ReadAllText(DataDir + "BusanSliceGround.json"));
            bld = JsonUtility.FromJson<BuildingSet>(File.ReadAllText(DataDir + "BusanSliceBuildings.json"));
            terrain = JsonUtility.FromJson<TerrainData>(File.ReadAllText(DataDir + "BusanTerrain.json"));
            corridorSet = JsonUtility.FromJson<CorridorSet>(File.ReadAllText(DataDir + "BusanSliceCorridors.json"));
            Log($"DATA route={route.points.Count} roads={net.roads.Count} junctions={net.junctions.Count} mouths={(net.mouths != null ? net.mouths.Count : 0)} closures={(net.closures != null ? net.closures.Count : 0)} ground={ground.cols}x{ground.rows} buildings={bld.buildings.Count}");
            LoadSumo();
        }

        static UnityEngine.SceneManagement.Scene PrepareScene()
        {
            // Release the generated scene file before overwriting it (no "modified externally" dialog).
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Overwriting with File.Copy keeps the existing .meta (GUID) so the build list entry stays valid.
            File.Copy(Path.GetFullPath(SourceScene), Path.GetFullPath(ScenePath), true);
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var name in new[] { "Environment", GeneratedName })
            {
                var old = GameObject.Find(name);
                if (old != null) UnityEngine.Object.DestroyImmediate(old);
            }
            // Old v1 chunks and per-object assets must not survive a rebuild.
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { Root }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(p).StartsWith("mesh-")) AssetDatabase.DeleteAsset(p);
            }
            return scene;
        }

        static void RegisterBuildScene()
        {
            // Disabled entry: BusanEnvironment is not tracked in git, so an enabled entry would
            // break a teammate's Android build validation. Validation is called directly instead.
            var scenes = EditorBuildSettings.scenes.ToList();
            var i = scenes.FindIndex(s => s.path == ScenePath);
            if (i < 0) scenes.Add(new EditorBuildSettingsScene(ScenePath, false));
            else scenes[i] = new EditorBuildSettingsScene(ScenePath, false);
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ---------- course preparation ----------

        static void PrepareCourse()
        {
            int n = route.points.Count;
            cp = new Vector3[n]; chw = new float[n]; csh = new float[n];
            ckind = new int[n]; celev = new bool[n]; coneway = new bool[n]; ccum = new float[n];
            for (int i = 0; i < n; i++)
            {
                var q = route.points[i];
                cp[i] = new Vector3(q.x, q.y, q.z);
                chw[i] = q.w * 0.5f;
                ckind[i] = q.k; celev[i] = q.el != 0; coneway[i] = q.o != 0;
                csh[i] = chw[i] + (q.k == Kbridge ? 1.6f : 0f);
                if (i > 0) ccum[i] = ccum[i - 1] + Vector3.Distance(cp[i - 1], cp[i]);
            }
            cr = Rights(cp);
            courseGrid = new PointGrid(cp, 40f);
            spanStartS = route.towers[0].s - 200f;
            spanEndS = route.towers[1].s + 212f;

            // Side-street mouths come from the generator (road ends on the course edge, including the synthetic ones).
            mouths.Clear();
            var sb = new StringBuilder();
            var alleys = new StringBuilder();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (net.mouths != null)
                foreach (var m in net.mouths)
                {
                    var pos = new Vector3(m.x, m.y, m.z);
                    mouths.Add((pos, m.half, m.cls, m.ci));
                    if (celev[Mathf.Clamp(m.ci, 0, n - 1)]) continue;
                    if (m.cls <= 3 && m.half >= 3f)
                        sb.AppendLine(string.Format(inv, "{0:0.00} {1:0.00} {2:0.00} {3:0.000} {4:0.000} {5:0.0} {6}", m.x, m.y, m.z, m.dx, m.dz, m.half, m.ci));
                }
            // Alleys that start on the course: centreline from the mouth inward, for the drive test.
            if (net.mouths != null)
                foreach (var m in net.mouths)
                {
                    if (m.cls != 6 || celev[Mathf.Clamp(m.ci, 0, n - 1)]) continue;
                    var at = new Vector3(m.x, m.y, m.z);
                    foreach (var road in net.roads)
                    {
                        if (road.cls != 6) continue;
                        int cnt = road.p.Count / 3;
                        foreach (int end in new[] { 0, cnt - 1 })
                        {
                            var pos = new Vector3(road.p[end * 3], road.p[end * 3 + 1], road.p[end * 3 + 2]);
                            if ((pos - at).sqrMagnitude > 0.01f) continue;
                            alleys.Append(string.Format(inv, "{0} {1:0.0} {2}", road.way, road.w * 0.5f, m.ci));
                            for (int k = 0; k < cnt; k++)
                            {
                                int i = end == 0 ? k : cnt - 1 - k;
                                alleys.Append(string.Format(inv, " {0:0.00},{1:0.00},{2:0.00}", road.p[i * 3], road.p[i * 3 + 1], road.p[i * 3 + 2]));
                            }
                            alleys.AppendLine();
                        }
                    }
                }
            Log($"COURSE length={ccum[n - 1]:0}m spanS={spanStartS:0}-{spanEndS:0} mouths={mouths.Count} alleyMouths={mouths.Count(m => m.cls == 6)}");
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/BusanBridge"));
            if (Directory.Exists(dir))
            {
                File.WriteAllText(Path.Combine(dir, "mouths.txt"), sb.ToString());
                File.WriteAllText(Path.Combine(dir, "alleys.txt"), alleys.ToString());
            }
        }

        // Alley mouths (class 6) break the kerb and the paint but not the median fence or the lamps.
        static bool NearMouth(Vector3 pos, float extra, bool includeAlleys = true)
        {
            foreach (var m in mouths)
            {
                if (!includeAlleys && m.cls == 6) continue;
                var d = m.pos - pos; d.y = 0f;
                float r = m.half + extra;
                if (d.sqrMagnitude < r * r) return true;
            }
            return false;
        }

        static float CourseS(int i) => ccum[i];
        static bool InSpan(int i) => ccum[i] >= spanStartS && ccum[i] <= spanEndS;

        // ---------- scene state ----------

        static void TweakScene(Transform generatedRoot)
        {
            var sunGo = GameObject.Find("Sun");
            var sun = sunGo != null ? sunGo.GetComponent<Light>() : null;
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(43f, 31.5f, 0f);
                sun.color = new Color(1f, 0.97f, 0.92f);
                sun.intensity = 1.3f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.8f;
                sun.shadowBias = 0.05f;      // 0.025 / 0.15 produced concentric shadow-acne rings on flat asphalt
                sun.shadowNormalBias = 0.45f;
            }

            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/ParkingLotEnvironment/CloudPilot/Materials/Daylight_Sky.mat");
            RenderSettings.sun = sun;
            // No bake in this scene: trilight ambient plus the pre-baked probe from the whole-map scene.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.65f, 0.69f);
            RenderSettings.ambientEquatorColor = new Color(0.54f, 0.54f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.28f, 0.26f);
            RenderSettings.ambientIntensity = 1.3f;   // shaded facades and kerbs kept near-black at 1.0
            var probe = AssetDatabase.LoadAssetAtPath<Texture>("Assets/ParkingLotEnvironment/CloudPilot/WholeMapFinish20261105/WholeMapReview/ReflectionProbe-0.exr");
            if (probe != null)
            {
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = probe;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 250f;
            RenderSettings.fogEndDistance = 5000f;
            RenderSettings.fogColor = new Color(0.72f, 0.77f, 0.82f);

            var cameraGo = GameObject.Find("MainCamera") ?? GameObject.FindWithTag("MainCamera");
            var postEffects = new List<Behaviour>();
            if (cameraGo != null)
            {
                var cam = cameraGo.GetComponent<Camera>();
                cam.farClipPlane = 6500f;
                cam.nearClipPlane = 0.3f;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.allowHDR = true;
                cam.fieldOfView = 62f;
                postEffects.AddRange(AddPostEffects(cameraGo));
            }

            var spawn = GameObject.Find("PracticeSpawn");
            if (spawn != null)
            {
                int i0 = 2;
                var lane = RouteTraffic.PlayerLane(chw[i0] * 2f, coneway[i0]);
                spawn.transform.SetPositionAndRotation(cp[i0] + cr[i0] * lane + Vector3.up * 0.15f, Quaternion.Euler(0f, route.spawnHeadingDegrees, 0f));
            }

            var gm = UnityEngine.Object.FindObjectOfType<Parkside.Core.GameManager>();
            var vc = UnityEngine.Object.FindObjectOfType<Parkside.Vehicle.VehicleController>();

            var panelGo = GameObject.Find("DebugPanel");
            if (panelGo != null && gm != null)
            {
                var group = Ensure<CanvasGroup>(panelGo);
                var canvas = panelGo.GetComponentInParent<Canvas>();
                var hide = Ensure<DriveHudAutoHide>(canvas.gameObject);
                var hideSo = new SerializedObject(hide);
                hideSo.FindProperty("gameManager").objectReferenceValue = gm;
                hideSo.FindProperty("panel").objectReferenceValue = group;
                hideSo.ApplyModifiedPropertiesWithoutUndo();
                if (hide.PanelOrNull == null) throw new InvalidOperationException("DriveHudAutoHide panel not wired");
            }

            // Play area: the whole road network plus 150 m (the drive bounds), practice bounds 40 m wider on every side so
            // DriveAssist returns the car to the course before GameManager would reset it.
            Bounds bounds = default;
            if (gm != null)
            {
                float minX = cp.Min(p => p.x), maxX = cp.Max(p => p.x), minZ = cp.Min(p => p.z), maxZ = cp.Max(p => p.z);
                foreach (var road in net.roads)
                    for (int k = 0; k < road.p.Count; k += 3)
                    {
                        minX = Mathf.Min(minX, road.p[k]); maxX = Mathf.Max(maxX, road.p[k]);
                        minZ = Mathf.Min(minZ, road.p[k + 2]); maxZ = Mathf.Max(maxZ, road.p[k + 2]);
                    }
                bounds = new Bounds(new Vector3((minX + maxX) / 2f, 80f, (minZ + maxZ) / 2f), new Vector3(maxX - minX + 380f, 700f, maxZ - minZ + 380f));
                var so = new SerializedObject(gm);
                so.FindProperty("practiceBounds").boundsValue = bounds;
                so.ApplyModifiedPropertiesWithoutUndo();
                Log($"BOUNDS network x={minX:0}..{maxX:0} z={minZ:0}..{maxZ:0} drive={bounds.size.x - 80f:0}x{bounds.size.z - 80f:0}");
            }

            // Keyboard: the drive map uses W/S/Space with reverse on S. The parking scenes keep KeyboardInputSource (validator contract).
            var driveKeys = default(DriveKeyboardSource);
            var receiver = UnityEngine.Object.FindObjectOfType<Parkside.Input.VehicleInputReceiver>();
            if (receiver != null)
            {
                var oldKeyboard = receiver.DesktopSource != null ? receiver.DesktopSource.gameObject : receiver.gameObject;
                driveKeys = Ensure<DriveKeyboardSource>(oldKeyboard);
                var kso = new SerializedObject(driveKeys);
                kso.FindProperty("vehicle").objectReferenceValue = vc;
                kso.FindProperty("gameManager").objectReferenceValue = gm;
                kso.ApplyModifiedPropertiesWithoutUndo();
                var rso = new SerializedObject(receiver);
                rso.FindProperty("desktopSource").objectReferenceValue = driveKeys;
                rso.ApplyModifiedPropertiesWithoutUndo();
            }

            if (vc != null)
            {
                var so = new SerializedObject(vc);
                so.FindProperty("maximumSpeed").floatValue = 22f;
                so.FindProperty("acceleration").floatValue = 4.5f;
                so.FindProperty("brakeDeceleration").floatValue = 7f;
                so.FindProperty("highSpeedSteeringAngle").floatValue = 10f;
                so.FindProperty("highSpeedAt").floatValue = 20f;
                so.FindProperty("maxLateralAcceleration").floatValue = 8f; // 15 km/h alley entry needs a 4.7 m turn; the cap only bites above 20 km/h
                so.FindProperty("steeringResponse").floatValue = 5f;
                so.FindProperty("groundFollow").boolValue = true;
                so.FindProperty("groundMask").intValue = 1 << RoadLayer;
                so.ApplyModifiedPropertiesWithoutUndo();
                DressPlayerCar(vc);
            }

            // Ambient traffic: baked geometry lives in a ScriptableObject outside the scene file.
            var trafficAsset = BuildTrafficData();
            var trafficGo = new GameObject("Traffic");
            trafficGo.transform.SetParent(generatedRoot, false);
            TrafficSignals signals = null;
            if (UseSumo && trafficAsset.heads != null && trafficAsset.heads.Length > 0)
            {
                signals = trafficGo.AddComponent<TrafficSignals>();
                var sso = new SerializedObject(signals);
                sso.FindProperty("data").objectReferenceValue = trafficAsset;
                sso.FindProperty("lampMaterial").objectReferenceValue = sink.Mats["signalLamps"];
                sso.ApplyModifiedPropertiesWithoutUndo();
            }
            var traffic = trafficGo.AddComponent<RouteTraffic>();
            var tso = new SerializedObject(traffic);
            tso.FindProperty("data").objectReferenceValue = trafficAsset;
            tso.FindProperty("signals").objectReferenceValue = signals;
            var carPrefabs = bakedCars.ToArray();
            Log("TRAFFIC prefabs=" + string.Join(",", carPrefabs.Select(g => g.name + ":" + g.GetComponent<CarModel>().triangles / 1000f + "k")));
            var prefabProp = tso.FindProperty("vehiclePrefabs");
            prefabProp.arraySize = carPrefabs.Length;
            for (int i = 0; i < carPrefabs.Length; i++) prefabProp.GetArrayElementAtIndex(i).objectReferenceValue = carPrefabs[i];
            var paintProp = tso.FindProperty("paintMaterials");
            paintProp.arraySize = paintMaterials.Length;
            for (int i = 0; i < paintMaterials.Length; i++) paintProp.GetArrayElementAtIndex(i).objectReferenceValue = paintMaterials[i];
            SetFloatArray(tso.FindProperty("paintWeights"), paintWeights);
            tso.FindProperty("player").objectReferenceValue = vc != null ? vc.transform : null;
            tso.ApplyModifiedPropertiesWithoutUndo();

            // Drive assist: starts on Play, auto-drive, jumps, R to return, runtime shadows.
            var assistGo = new GameObject("DriveAssist");
            assistGo.transform.SetParent(generatedRoot, false);
            var assist = assistGo.AddComponent<DriveAssist>();
            var aso = new SerializedObject(assist);
            aso.FindProperty("gameManager").objectReferenceValue = gm;
            aso.FindProperty("vehicle").objectReferenceValue = vc;
            aso.FindProperty("vehicleCamera").objectReferenceValue = UnityEngine.Object.FindObjectOfType<Parkside.Cameras.VehicleCamera>();
            SetVectorArray(aso.FindProperty("path"), cp);
            SetFloatArray(aso.FindProperty("laneOffsets"), Enumerable.Range(0, cp.Length).Select(i => RouteTraffic.PlayerLane(chw[i] * 2f, coneway[i])).ToArray());
            var inner = bounds;
            inner.Expand(new Vector3(-80f, 0f, -80f));
            aso.FindProperty("driveBounds").boundsValue = inner;
            var jn = aso.FindProperty("jumpNames");
            var jd = aso.FindProperty("jumpDistances");
            jn.arraySize = jd.arraySize = route.jumps.Count;
            for (int i = 0; i < route.jumps.Count; i++)
            {
                jn.GetArrayElementAtIndex(i).stringValue = route.jumps[i].name;
                jd.GetArrayElementAtIndex(i).floatValue = route.jumps[i].s;
            }
            var pe = aso.FindProperty("postEffects");
            pe.arraySize = postEffects.Count;
            for (int i = 0; i < postEffects.Count; i++) pe.GetArrayElementAtIndex(i).objectReferenceValue = postEffects[i];
            aso.FindProperty("traffic").objectReferenceValue = traffic;
            if (driveKeys != null)
            {
                var kso2 = new SerializedObject(driveKeys);
                kso2.FindProperty("assist").objectReferenceValue = assist;
                kso2.ApplyModifiedPropertiesWithoutUndo();
            }
            SetFloatArray(aso.FindProperty("autoSpeeds"), Enumerable.Range(0, cp.Length).Select(i => ckind[i] == Kbridge ? 22f : ckind[i] == Kbeach ? 14f : 16f).ToArray());
            aso.ApplyModifiedPropertiesWithoutUndo();

            var seaGo = GameObject.Find("Sea");
            if (seaGo != null && seaGo.GetComponent<SeaDrift>() == null) seaGo.AddComponent<SeaDrift>();
        }

        static int TriangleCount(GameObject prefab)
        {
            long t = 0;
            foreach (var f in prefab.GetComponentsInChildren<MeshFilter>()) if (f.sharedMesh != null) t += f.sharedMesh.triangles.Length / 3;
            foreach (var r in prefab.GetComponentsInChildren<SkinnedMeshRenderer>()) if (r.sharedMesh != null) t += r.sharedMesh.triangles.Length / 3;
            return (int)t;
        }

        static TrafficData BuildTrafficData()
        {
            const string path = "Assets/BusanEnvironment/Generated/BusanTrafficData.asset";
            AssetDatabase.DeleteAsset(path);
            var data = ScriptableObject.CreateInstance<TrafficData>();
            data.course = (Vector3[])cp.Clone();
            data.courseWidth = chw.Select(h => h * 2f).ToArray();
            data.courseOneWay = (bool[])coneway.Clone();
            data.courseSpeed = Enumerable.Range(0, cp.Length).Select(i => ckind[i] == Kbridge ? 22f : ckind[i] == Kbeach ? 14f : 16f).ToArray();
            data.corridors = corridorSet.corridors.Select(c =>
            {
                var pts = new Vector3[c.p.Count / 3];
                for (int i = 0; i < pts.Length; i++) pts[i] = new Vector3(c.p[i * 3], c.p[i * 3 + 1], c.p[i * 3 + 2]);
                return new TrafficPath { points = pts, lane = c.lane, width = c.w };
            }).ToArray();
            FillSignalData(data);
            AssetDatabase.CreateAsset(data, path);
            Log($"TRAFFICDATA corridors={data.corridors.Length} corridorPoints={data.corridors.Sum(c => c.points.Length)}");
            return data;
        }

        static void SetVectorArray(SerializedProperty prop, Vector3[] values)
        {
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).vector3Value = values[i];
        }

        static void SetIntArray(SerializedProperty prop, int[] values)
        {
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).intValue = values[i];
        }

        static void SetFloatArray(SerializedProperty prop, float[] values)
        {
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).floatValue = values[i];
        }

        // Not `??`: GetComponent can return Unity's fake null, which `??` treats as a real object.
        static T Ensure<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        static IEnumerable<Behaviour> AddPostEffects(GameObject cameraGo)
        {
            const string grade = "81b03f84ce7749ebba0499d38a071fbb", fxaa = "1ef2c80fab875a95297cc9d480afe44e", ao = "c9e3aff736de322a097281fe883b727a";
            Shader Load(string guid) => AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));

            var g = Ensure<ParkingLotEnvironment.CloudPilot.MapDaylightGrade>(cameraGo);
            g.gradeShader = Load(grade); g.exposureEV = 0.55f; g.contrast = 1.04f; g.saturation = 1.0f; g.enabled = true;
            var f = Ensure<ParkingLotEnvironment.CloudPilot.MapCameraFXAA>(cameraGo);
            f.edgeShader = Load(fxaa); f.enabled = true;
            var o = Ensure<Kino.Obscurance>(cameraGo);
            var so = new SerializedObject(o);
            so.FindProperty("_intensity").floatValue = 1.1f;
            so.FindProperty("_radius").floatValue = 0.75f;
            so.FindProperty("_sampleCount").enumValueIndex = 4; // Custom
            so.FindProperty("_sampleCountValue").intValue = 24;
            so.FindProperty("_downsampling").boolValue = true;
            so.FindProperty("_occlusionSource").enumValueIndex = 1;
            so.FindProperty("_aoShader").objectReferenceValue = Load(ao);
            so.ApplyModifiedPropertiesWithoutUndo();
            o.enabled = true;
            return new Behaviour[] { g, f, o };
        }

        // The cube body stays (colliders/anchors depend on the rig) but is hidden; a real
        // car prefab is parented under it. Wheels keep turning logic but are hidden too.
        // darker, less saturated copy of the model's paint: the original blue reads electric under the sun and the sky reflection
        static Material BonnetMaterial(Material source)
        {
            var path = Root + "/mat-bonnet.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
            m.color = new Color(0.035f, 0.075f, 0.16f);
            m.SetFloat("_Glossiness", 0.78f);
            m.SetFloat("_Metallic", 0.35f);
            return m;
        }

        const float HoodZ0 = 0.85f;   // windscreen base in rig space (the i30 body spans z -2.1..2.1)

        static void DressPlayerCar(Parkside.Vehicle.VehicleController vc)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ParkingLotEnvironment/CloudPilot/ParkingContext20261105/ReusableVehicles/Hyundai_i30_Blue.prefab");
            if (prefab == null) return;
            foreach (var r in vc.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, vc.transform);
            model.name = "i30 model";
            foreach (var c in model.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                // centre over the rig origin, tyres on the road
                model.transform.position += new Vector3(vc.transform.position.x - b.center.x, vc.transform.position.y - b.min.y + 0.02f, vc.transform.position.z - b.center.z);
            }
            var modeFile = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/BusanBridge/driver-mode.txt"));
            var driverMode = File.Exists(modeFile) ? File.ReadAllText(modeFile).Trim() : "hood";
            Log("DRIVERVIEW mode=" + driverMode);
            var modelRenderers = driverMode == "hood" ? model.GetComponentsInChildren<Renderer>() : new Renderer[0];
            var dashMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/mat-dash.mat");
            var driverOnly = new List<Renderer>();
            if (driverMode == "hood")
            {
                // Real bonnet: the upward-facing paint triangles of the i30 in front of the windscreen base (rig space z >= 0.85), with the
                // model's own normals and paint material, so it catches the sky reflection. A dark cowl strip sits at the windscreen base.
                // The eye is moved back into the cabin position, so the bonnet is a band at the bottom of the view instead of a slab.
                var driver = vc.transform.Find("CameraAnchors/Driver");
                if (driver != null) driver.localPosition = new Vector3(-0.37f, 1.38f, 0.36f);
                if (driver != null) driver.localRotation = Quaternion.Euler(-1.6f, 0f, 0f);   // small pitch up: horizon a little lower, bonnet share smaller
                var hood = new MeshBuilder { UseColors = false };
                Material hoodPaint = null;
                int hoodTris = 0;
                foreach (var r in model.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!r.sharedMaterial.name.EndsWith("_paint")) continue;
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    var vs = mesh.vertices; var ts = mesh.triangles; var nm = mesh.normals;
                    var toRig = vc.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    for (int t = 0; t < ts.Length; t += 3)
                    {
                        var a = toRig.MultiplyPoint3x4(vs[ts[t]]); var b = toRig.MultiplyPoint3x4(vs[ts[t + 1]]); var c = toRig.MultiplyPoint3x4(vs[ts[t + 2]]);
                        var gn = Vector3.Cross(b - a, c - a).normalized;
                        var ctr = (a + b + c) / 3f;
                        if (ctr.z < HoodZ0 || ctr.z > 1.85f || ctr.y < 0.55f || gn.y < 0.12f || Mathf.Min(a.z, Mathf.Min(b.z, c.z)) < HoodZ0 - 0.12f) continue;
                        int[] order = { ts[t], ts[t + 1], ts[t + 2] };
                        var avg = nm.Length == vs.Length ? (nm[order[0]] + nm[order[1]] + nm[order[2]]) : Vector3.up;
                        if (Vector3.Dot(gn, toRig.MultiplyVector(avg)) < 0f) { order[1] = ts[t + 2]; order[2] = ts[t + 1]; }
                        int i0 = hood.V.Count;
                        foreach (int vi in order)
                        {
                            var pos = toRig.MultiplyPoint3x4(vs[vi]);
                            hood.V.Add(pos); hood.U.Add(Vector2.zero);
                            hood.N.Add(nm.Length == vs.Length ? toRig.MultiplyVector(nm[vi]).normalized : Vector3.up);
                        }
                        hood.Index(i0, i0 + 1, i0 + 2);
                        hoodTris++;
                    }
                    if (hoodPaint == null) hoodPaint = r.sharedMaterial;
                }
                Log($"BONNET real i30 bonnet triangles={hoodTris} eye={(driver != null ? driver.localPosition.ToString("0.00") : "n/a")}");
                var hoodMesh = hood.Bake("Bonnet");
                var meshPath = MeshDir + "/Bonnet.asset";
                AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(hoodMesh, meshPath);
                var bonnet = new GameObject("Bonnet");
                bonnet.transform.SetParent(vc.transform, false);
                bonnet.AddComponent<MeshFilter>().sharedMesh = hoodMesh;
                var br = bonnet.AddComponent<MeshRenderer>();
                br.sharedMaterial = BonnetMaterial(hoodPaint); br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; br.enabled = false;
                driverOnly.Add(br);

                var cowl = new MeshBuilder();
                float cx = 0.62f;
                // dark cowl panel from the windscreen base forward, slightly under the bonnet, so the shut line between them shows plastic, not the road
                cowl.Quad(new Vector3(-cx, 1.03f, HoodZ0 - 0.18f), new Vector3(cx, 1.03f, HoodZ0 - 0.18f), new Vector3(cx, 0.915f, HoodZ0 + 0.30f), new Vector3(-cx, 0.915f, HoodZ0 + 0.30f), Vector3.up);
                var cowlMesh = cowl.Bake("Cowl");
                var cowlPath = MeshDir + "/Cowl.asset";
                AssetDatabase.DeleteAsset(cowlPath);
                AssetDatabase.CreateAsset(cowlMesh, cowlPath);
                var cowlGo = new GameObject("Cowl");
                cowlGo.transform.SetParent(vc.transform, false);
                cowlGo.AddComponent<MeshFilter>().sharedMesh = cowlMesh;
                var cr2 = cowlGo.AddComponent<MeshRenderer>();
                cr2.sharedMaterial = BonnetMaterial(hoodPaint);   // same paint as the bonnet: the panel between bonnet and windscreen is body colour, not a black plate cr2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; cr2.enabled = false;
                driverOnly.Add(cr2);
            }
            var bodyGo = new GameObject("DriverViewBody");
            bodyGo.transform.SetParent(vc.transform, false);
            var body = bodyGo.AddComponent<DriverViewBody>();
            var bso = new SerializedObject(body);
            bso.FindProperty("vehicleCamera").objectReferenceValue = UnityEngine.Object.FindObjectOfType<Parkside.Cameras.VehicleCamera>();
            var dp = bso.FindProperty("driverOnly"); dp.arraySize = driverOnly.Count;
            for (int i = 0; i < driverOnly.Count; i++) dp.GetArrayElementAtIndex(i).objectReferenceValue = driverOnly[i];
            var hp = bso.FindProperty("hiddenInDriver"); hp.arraySize = modelRenderers.Length;
            for (int i = 0; i < modelRenderers.Length; i++) hp.GetArrayElementAtIndex(i).objectReferenceValue = modelRenderers[i];
            bso.ApplyModifiedPropertiesWithoutUndo();
            var chase = vc.transform.Find("CameraAnchors/Chase");
            if (chase != null) { chase.localPosition = new Vector3(0f, 2.6f, -6.5f); chase.localRotation = Quaternion.Euler(8f, 0f, 0f); }
        }
    }
}
