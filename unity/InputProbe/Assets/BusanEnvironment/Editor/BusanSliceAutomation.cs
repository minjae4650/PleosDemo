using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // Dev-only bridge: lets tools drive an already open Editor, because Unity allows
    // one Editor per project. Idle unless Temp/BusanBridge/command.txt exists.
    // Commands, one per line: refresh | build | sweep | treecard | capture <dir> | sheet <png> | play <seconds> <dir> [auto]
    [InitializeOnLoad]
    public static class BusanSliceAutomation
    {
        const string PlayStateKey = "BusanBridge.Play";
        const string PlayStartKey = "BusanBridge.PlayStart";
        const string ScenePath = "Assets/_Parkside/Scenes/BusanDrive.unity";
        static double nextTick;
        static int playErrors, driveStep, shotsTaken;
        static float driveStepStart;
        static int driveLastSecond = -1;
        static string driveLog = "";
        static Vector3 driveMouth, driveMouthDir, driveStart;
        static Transform stoppedCar;
        static float stopMin, stopPassedAt, stopHeldAt, stopGapAtHold;
        static void OnPlayLog(string message, string stack, LogType type)
        {
            // The play session reloads the domain, so the count lives in SessionState and the handler is registered in the static constructor.
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (string.IsNullOrEmpty(SessionState.GetString(PlayStateKey, ""))) return;
            SessionState.SetInt("BusanBridge.PlayErrors", SessionState.GetInt("BusanBridge.PlayErrors", 0) + 1);
            try { File.AppendAllText(Path.Combine(Dir, "play-errors.txt"), type + ": " + message + Environment.NewLine + stack + Environment.NewLine); } catch (Exception) { }
        }
        static double nextHeartbeat;
        static bool awaitingCompile;
        static bool midSampleTaken, camSet;
        static bool gameViewShotTaken;
        static string midSample = "", statsSample = "";

        static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/BusanBridge"));
        static string CommandPath => Path.Combine(Dir, "command.txt");
        static string ResultPath => Path.Combine(Dir, "result.txt");

        [Serializable] class View { public string name; public float px, py, pz, tx, ty, tz, fov = 60f; }
        [Serializable] class ViewSet { public int width = 1280, height = 720; public List<View> views; }

        static BusanSliceAutomation()
        {
            EditorApplication.update += Tick;
            EditorApplication.update -= TraceTick;
            EditorApplication.update += TraceTick;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            Application.logMessageReceived -= OnPlayLog;
            Application.logMessageReceived += OnPlayLog;
            // A play request whose play mode never started (compile errors block it) would otherwise leave the bridge waiting forever.
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !string.IsNullOrEmpty(SessionState.GetString(PlayStateKey, "")))
            {
                SessionState.EraseString(PlayStateKey);
                SessionState.EraseFloat(PlayStartKey);
            }
            if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.EraseString(DemoCapture.EditorArgsKey);
            if (!EditorApplication.isPlayingOrWillChangePlaymode) RestoreSettingsFromDisk(); // heals a leak left by an earlier run
        }

        static void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < nextTick) return;
            nextTick = now + 0.25;
            try
            {
                // Tooling opt-in: with no Temp/BusanBridge folder the bridge stays silent and creates nothing.
                if (!Directory.Exists(Dir)) return;
                if (now >= nextHeartbeat)
                {
                    nextHeartbeat = now + 2.0;
                    File.WriteAllText(Path.Combine(Dir, "heartbeat.txt"),
                        $"{DateTime.Now:O} playing={EditorApplication.isPlaying} compiling={EditorApplication.isCompiling} failed={EditorUtility.scriptCompilationFailed}");
                }
                if (!string.IsNullOrEmpty(SessionState.GetString(PlayStateKey, "")))
                {
                    // A started play that ended without reporting (stopped from outside) would block the bridge for good.
                    if (SessionState.GetFloat(PlayStartKey, -1f) >= 0f && !EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        SessionState.EraseString(PlayStateKey);
                        SessionState.EraseFloat(PlayStartKey);
                        SessionState.EraseString(DemoCapture.EditorArgsKey);
                        RestoreSettingsFromDisk();
                        Report("ERR play ended before its report");
                        return;
                    }
                    TickPlay(now);
                    return;
                }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (awaitingCompile)
                {
                    awaitingCompile = false;
                    if (EditorUtility.scriptCompilationFailed)
                    {
                        File.Delete(CommandPath);
                        Report("ERR compile failed; see Editor.log");
                        return;
                    }
                }
                if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(CommandPath)) return;
                var lines = File.ReadAllLines(CommandPath).Where(l => l.Trim().Length > 0).ToList();
                if (lines.Count == 0) { File.Delete(CommandPath); return; }
                var command = lines[0].Trim();
                lines.RemoveAt(0);
                if (lines.Count > 0) File.WriteAllLines(CommandPath, lines); else File.Delete(CommandPath);
                Run(command);
            }
            catch (Exception ex)
            {
                if (File.Exists(CommandPath)) File.Delete(CommandPath);
                Report("ERR " + ex);
            }
        }

        static void Run(string command)
        {
            var parts = command.Split(new[] { ' ' }, 2);
            var argument = parts.Length > 1 ? parts[1].Trim() : "";
            switch (parts[0])
            {
                case "refresh":
                    AssetDatabase.Refresh();
                    awaitingCompile = true;
                    Report("OK refresh");
                    break;
                case "build":
                    RefuseIfOtherSceneDirty();
                    var summary = BusanSliceBuilder.BuildUnattended();
                    Report("OK build " + summary + " " + BuildStats());
                    break;
                case "sweep":
                    if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    Report("OK sweep " + BusanSliceBuilder.SweepCommand());
                    break;
                case "probe": // probe <x> <z> [halfSize] [step]: ASCII map of the road collider layer around a point (diagnostics)
                    if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    Report("OK probe\n" + Probe(argument));
                    break;
                case "playerbuild": // playerbuild <outDir>: Windows player of BusanDrive alone, outside the project; the build settings list is not touched
                    Directory.CreateDirectory(argument);
                    var built = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                    {
                        scenes = new[] { ScenePath },
                        locationPathName = Path.Combine(argument, "BusanDrive.exe"),
                        target = BuildTarget.StandaloneWindows64,
                        targetGroup = BuildTargetGroup.Standalone,
                        options = BuildOptions.None
                    }).summary;
                    Report($"{(built.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? "OK" : "ERR")} playerbuild result={built.result} seconds={built.totalTime.TotalSeconds:0} sizeMB={built.totalSize / (1024f * 1024f):0.0} errors={built.totalErrors} warnings={built.totalWarnings}");
                    break;
                case "carfront": // where the named front and rear parts of every source car model sit along Z (diagnostics)
                    Report("OK carfront\n" + BusanSliceBuilder.CarFrontReport());
                    break;
                case "prefabinfo": // prefabinfo <asset path>: renderers, triangles and material values (diagnostics)
                    Report("OK prefabinfo\n" + PrefabInfo(argument));
                    break;
                case "meshinfo": // meshinfo <GameObject name>: vertices of a generated mesh with its collider state (diagnostics)
                    if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    {
                        var go = GameObject.Find(argument);
                        var mf = go != null ? go.GetComponent<MeshFilter>() : null;
                        var mc = go != null ? go.GetComponent<MeshCollider>() : null;
                        var info = new StringBuilder();
                        if (mf == null) info.Append("not found");
                        else
                        {
                            var mesh = mf.sharedMesh;
                            info.Append($"verts={mesh.vertexCount} tris={mesh.triangles.Length / 3} bounds={mesh.bounds} collider={(mc != null ? mc.enabled + " mesh=" + (mc.sharedMesh != null) : "none")} layer={go.layer} activeInHierarchy={go.activeInHierarchy}\n");
                            if (mc != null)
                            {
                                var c0 = mesh.bounds.center + go.transform.position;
                                bool direct = mc.Raycast(new Ray(new Vector3(c0.x, 200f, c0.z), Vector3.down), out var dh, 400f);
                                bool world = Physics.Raycast(new Vector3(c0.x, 200f, c0.z), Vector3.down, out var wh, 400f, 1 << 30);
                                info.Append($"rayAtCentre direct={direct} {(direct ? dh.point.y.ToString("0.0") : "")} world={world} {(world ? wh.collider.name + " y=" + wh.point.y.ToString("0.0") : "")} colliderBounds={mc.bounds} readable={mesh.isReadable} cooking={mc.cookingOptions}\n");
                                var temp = new GameObject("tempProbeCollider");
                                temp.layer = 30;
                                var tc = temp.AddComponent<MeshCollider>();
                                tc.sharedMesh = mesh;
                                Physics.SyncTransforms();
                                bool tw = Physics.Raycast(new Vector3(c0.x, 200f, c0.z), Vector3.down, out var th, 400f, 1 << 30);
                                info.Append($"freshCollider bounds={tc.bounds} worldHit={tw} {(tw ? th.collider.name + " y=" + th.point.y.ToString("0.0") : "")}\n");
                                UnityEngine.Object.DestroyImmediate(temp);
                            }
                            var vs = mesh.vertices; var ts = mesh.triangles;
                            for (int i = 0; i < ts.Length; i += 3) info.Append($"tri {vs[ts[i]]:0.0} {vs[ts[i + 1]]:0.0} {vs[ts[i + 2]]:0.0}\n");
                        }
                        Report("OK meshinfo " + info);
                    }
                    break;
                case "lineup": // lineup <outDir>: cars from Temp/BusanBridge/lineup.txt placed in the open scene for the views in views.json, then removed
                    if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    {
                        var spawned = new List<GameObject>();
                        var inv = System.Globalization.CultureInfo.InvariantCulture;
                        foreach (var line in File.ReadAllLines(Path.Combine(Dir, "lineup.txt")).Where(l => l.Trim().Length > 0))
                        {
                            var f = line.Split(' ');
                            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BusanEnvironment/GeneratedSlice/TrafficCars/" + f[0] + ".prefab");
                            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                            go.transform.SetPositionAndRotation(new Vector3(float.Parse(f[1], inv), float.Parse(f[2], inv), float.Parse(f[3], inv)), Quaternion.Euler(0f, float.Parse(f[4], inv), 0f));
                            var model = go.GetComponent<CarModel>();
                            if (!model.fixedPaint) model.SetPaint(AssetDatabase.LoadAssetAtPath<Material>("Assets/BusanEnvironment/GeneratedSlice/mat-car-" + f[5] + ".mat"));
                            spawned.Add(go);
                        }
                        Physics.SyncTransforms();
                        Capture(argument);
                        foreach (var go in spawned) UnityEngine.Object.DestroyImmediate(go);
                        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                    }
                    Report("OK lineup " + argument);
                    break;
                case "treecard":
                    var cardPath = "Assets/BusanEnvironment/GeneratedSlice/tex/tree_card.png";
                    if (File.Exists(cardPath)) File.Delete(cardPath);
                    Report("OK treecard " + BusanSliceTrees.Bake(cardPath));
                    break;
                case "carswap": // carswap <name> [name...]: a built district's standing cars as baked one-mesh cars (BusanDistrictBuilder.SwapCars)
                    {
                        string swapReport;
                        try { swapReport = string.Join(" | ", argument.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(BusanDistrictBuilder.SwapCars)); }
                        catch (Exception e) { swapReport = "ERR " + e.GetType().Name + ": " + e.Message; }
                        Report((swapReport.StartsWith("ERR") ? "" : "OK carswap ") + swapReport);
                    }
                    break;
                case "treebake": // treebake <source dir> <out png>: the landmark map's tree cards (BusanLandmarkTrees)
                    {
                        var treeArgs = argument.Split(new[] { ' ' }, 2);
                        var treeReport = treeArgs.Length < 2 ? "ERR usage: treebake <source dir> <out png>" : BusanLandmarkTrees.Bake(treeArgs[0], treeArgs[1].Trim());
                        Report((treeReport.StartsWith("ERR") ? "" : "OK treebake ") + treeReport);
                    }
                    break;
                case "gwangalli": // gwangalli <outDir>: builds the grey-box scene of the Gwangalli beach road and renders its views
                    Report("OK gwangalli " + BusanGwangalliBlockout.Build(argument));
                    break;
                case "district": // district <name> [dir]: the district's prefab from tools/build_district.py output (default %LOCALAPPDATA%\busan-tools\out\<name>)
                    {
                        var districtArgs = argument.Split(new[] { ' ' }, 2);
                        var districtReport = BusanDistrictBuilder.Build(districtArgs[0], districtArgs.Length > 1 ? districtArgs[1].Trim() : null);
                        Report((districtReport.StartsWith("ERR") || districtReport.StartsWith("REFUSED") ? "ERR district " : "OK district ") + districtReport);
                    }
                    break;
                case "landmarks": // landmarks views <name> <outDir>: renders the district's views from its prefab
                    {             // landmarks build: the assembled scene (BusanLandmarkScene); landmarks sweep: its driving surfaces along every route
                        var landmarkArgs = argument.Split(new[] { ' ' }, 3);
                        if (landmarkArgs.Length == 3 && landmarkArgs[0] == "views")
                        {
                            var landmarkReport = BusanDistrictBuilder.RenderViews(landmarkArgs[1], landmarkArgs[2].Trim());
                            Report((landmarkReport.StartsWith("ERR") || landmarkReport.StartsWith("REFUSED") ? "ERR landmarks views " : "OK landmarks views ") + landmarkReport);
                        }
                        else if (landmarkArgs[0] == "build")
                        {
                            var landmarkReport = BusanLandmarkScene.Build();
                            Report((landmarkReport.StartsWith("ERR") || landmarkReport.StartsWith("REFUSED") ? "ERR landmarks build " : "OK landmarks build ") + landmarkReport);
                        }
                        else if (landmarkArgs[0] == "sweep") // [ignore=<collider>,<collider>]: leaves those colliders' hits out (to see the sweep catch a gap)
                        {
                            var ignore = argument.Split(' ').Where(t => t.StartsWith("ignore=")).SelectMany(t => t.Substring(7).Split(',')).Where(t => t.Length > 0).ToList();
                            var landmarkReport = BusanLandmarkChecks.Sweep(ignore);
                            Report((landmarkReport.StartsWith("ERR") || landmarkReport.StartsWith("REFUSED") ? "ERR landmarks sweep " : "OK landmarks sweep ") + landmarkReport);
                        }
                        else if (landmarkArgs[0] == "coverage") // coverage <route> <outDir> [step=20]: driver's-eye pictures along a route (plan 6.1)
                        {
                            var coverArgs = argument.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            float coverStep = 20f;
                            foreach (var token in coverArgs.Skip(3)) if (token.StartsWith("step=")) coverStep = float.Parse(token.Substring(5), System.Globalization.CultureInfo.InvariantCulture);
                            var landmarkReport = coverArgs.Length >= 3 && int.TryParse(coverArgs[1], out int coverRoute)
                                ? BusanLandmarkChecks.Coverage(coverRoute, coverArgs[2], coverStep)
                                : "ERR use landmarks coverage <route> <outDir> [step=20]";
                            Report((landmarkReport.StartsWith("ERR") || landmarkReport.StartsWith("REFUSED") ? "ERR landmarks coverage " : "OK landmarks coverage ") + landmarkReport);
                        }
                        else if (landmarkArgs[0] == "lanes")
                        {
                            var landmarkReport = BusanLandmarkChecks.Lanes();
                            Report((landmarkReport.StartsWith("ERR") || landmarkReport.StartsWith("REFUSED") ? "ERR landmarks lanes " : "OK landmarks lanes ") + landmarkReport);
                        }
                        else if (landmarkArgs[0] == "shot") // shot <png> <px> <py> <pz> <tx> <ty> <tz> [fov] [width] [height]
                        {
                            var a = argument.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            var ic = System.Globalization.CultureInfo.InvariantCulture;
                            string shotReport;
                            try
                            {
                                float N(int i) => float.Parse(a[i], ic);
                                shotReport = BusanLandmarkChecks.Shot(a[1], new Vector3(N(2), N(3), N(4)), new Vector3(N(5), N(6), N(7)), a.Length > 8 ? N(8) : 62f,
                                    a.Length > 9 ? int.Parse(a[9]) : 1280, a.Length > 10 ? int.Parse(a[10]) : 720);
                            }
                            catch (Exception e) { shotReport = "ERR " + e.GetType().Name + ": " + e.Message; }
                            Report((shotReport.StartsWith("ERR") || shotReport.StartsWith("REFUSED") ? "ERR landmarks shot " : "OK landmarks shot ") + shotReport);
                        }
                        else Report("ERR landmarks: use landmarks views <name> <outDir> | landmarks build | landmarks sweep | landmarks lanes | landmarks shot ... | landmarks coverage <route> <outDir> [step=20]");
                    }
                    break;
                case "capture":
                    if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    Capture(argument);
                    Report("OK capture " + argument);
                    break;
                case "sheet":
                    PrefabSheet(argument);
                    Report("OK sheet " + argument);
                    break;
                case "gameview": // gameview [sharp]: how the Game view draws (its own resolution and zoom); sharp: full resolution
                    Report("OK gameview " + GameViewState(argument.Trim() == "sharp" || argument.Trim() == "soft", argument.Trim() == "soft"));
                    break;
                case "settingscheck":
                    Report($"OK settingscheck runInBackground={Application.runInBackground} vSyncCount={QualitySettings.vSyncCount} targetFrameRate={Application.targetFrameRate} qualityLevel={QualitySettings.GetQualityLevel()} shadows={QualitySettings.shadows} antiAliasing={QualitySettings.antiAliasing}");
                    break;
                case "savecheck":
                    AssetDatabase.SaveAssets();
                    Report("OK savecheck");
                    break;
                case "play":
                    RefuseIfOtherSceneDirty();
                    var wanted = ScenePath;
                    foreach (var token in argument.Split(' ')) if (token.StartsWith("scene=")) wanted = token.Substring(6);
                    if (EditorSceneManager.GetActiveScene().path != wanted)
                        EditorSceneManager.OpenScene(wanted, OpenSceneMode.Single);
                    SessionState.SetInt("BusanBridge.PlayErrors", 0);
                    if (File.Exists(Path.Combine(Dir, "play-errors.txt"))) File.Delete(Path.Combine(Dir, "play-errors.txt"));
                    // democapture=<dir>,<route>,<driver|chase>,<from>,<seconds>,<fps>: DemoCapture runs in this play as with its command line
                    SessionState.EraseString(DemoCapture.EditorArgsKey);
                    foreach (var token in argument.Split(' '))
                        if (token.StartsWith("democapture="))
                        {
                            var d = token.Substring(12).Split(',');
                            var names = new[] { "-democapture", "-route", "-view", "-from", "-seconds", "-fps" };
                            SessionState.SetString(DemoCapture.EditorArgsKey, string.Join("\n", d.Take(names.Length).Select((v, i) => names[i] + "\n" + v)));
                        }
                    driveStep = -1;
                    shotsTaken = 0;
                    SessionState.SetString(PlayStateKey, argument);
                    SessionState.SetFloat(PlayStartKey, -1f);
                    EditorApplication.isPlaying = true;
                    break;
                default:
                    Report("ERR unknown command " + command);
                    break;
            }
        }


        // ---------- unfocused play, trace, uncapped fps ----------

        static bool PlayHas(string token) => SessionState.GetString(PlayStateKey, "").Split(' ').Contains(token);

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                // Runtime setter only (never PlayerSettings): lets the player loop run while the editor window is not focused.
                if (!string.IsNullOrEmpty(SessionState.GetString(PlayStateKey, ""))) Application.runInBackground = true;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
            {
                // Independent of PlayStateKey (erased before the run ends) and of any remembered value (it may have captured a leak).
                RestoreSettingsFromDisk();
            }
        }

        // The editor writes these runtime setters through to ProjectSettings / QualitySettings on save, so the files on disk are the truth.
        static void RestoreSettingsFromDisk()
        {
            try
            {
                var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../ProjectSettings"));
                foreach (var line in File.ReadLines(Path.Combine(root, "ProjectSettings.asset")))
                {
                    var t = line.Trim();
                    if (t.StartsWith("runInBackground:")) { Application.runInBackground = t.Substring(16).Trim() != "0"; break; }
                }
                int level = QualitySettings.GetQualityLevel(), seen = 0;
                foreach (var line in File.ReadLines(Path.Combine(root, "QualitySettings.asset")))
                {
                    var t = line.Trim();
                    if (!t.StartsWith("vSyncCount:")) continue;
                    if (seen++ == level) { QualitySettings.vSyncCount = int.Parse(t.Substring(11).Trim()); break; }
                }
                Application.targetFrameRate = -1; // runtime only, default value
            }
            catch (Exception ex) { Debug.LogWarning("BusanBridge: settings restore failed " + ex.Message); }
        }

        static StringBuilder traceBuffer = new StringBuilder();
        static double nextTrace, lastFrameTime;
        static int lastFrameCount = -1;
        static readonly List<float> frameTimes = new List<float>();
        static readonly List<(float at, float ms)> spikes = new List<(float, float)>();
        static float measureFrom;

        static void TraceTick()
        {
            if (!EditorApplication.isPlaying || string.IsNullOrEmpty(SessionState.GetString(PlayStateKey, ""))) return;
            if (SessionState.GetFloat(PlayStartKey, -1f) < 0f) return;
            bool uncapped = PlayHas("uncapped"), trace = PlayHas("trace");
            if (uncapped)
            {
                Application.targetFrameRate = -1;
                QualitySettings.vSyncCount = 0;
                double nowT = Time.realtimeSinceStartup;
                if (Time.frameCount != lastFrameCount)
                {
                    if (lastFrameCount >= 0 && nowT - SessionState.GetFloat(PlayStartKey, 0f) > measureFrom)
                    {
                        frameTimes.Add((float)(nowT - lastFrameTime));
                        if (nowT - lastFrameTime > 0.04) spikes.Add(((float)(nowT - SessionState.GetFloat(PlayStartKey, 0f)), (float)(nowT - lastFrameTime) * 1000f));
                    }
                    lastFrameCount = Time.frameCount; lastFrameTime = nowT;
                }
            }
            if (!trace) return;
            double now = Time.realtimeSinceStartup;
            if (now < nextTrace) return;
            nextTrace = now + 0.1;
            var vc = UnityEngine.Object.FindObjectOfType<Parkside.Vehicle.VehicleController>();
            var rx = UnityEngine.Object.FindObjectOfType<Parkside.Input.VehicleInputReceiver>();
            var gm = UnityEngine.Object.FindObjectOfType<Parkside.Core.GameManager>();
            if (vc == null) return;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            float thr = rx != null ? rx.Controls.Throttle : 0f, brk = rx != null ? rx.Controls.Brake : 0f;
            float t = (float)(now - SessionState.GetFloat(PlayStartKey, 0f));
            float pitch = vc.transform.eulerAngles.x; if (pitch > 180f) pitch -= 360f;
            traceBuffer.Append(string.Format(inv, "{0:0.00},{1:0.0},{2},{3:0.00},{4:0.00},{5:0.0},{6:0.0},{7},{8:0.000},{9:0.00}\n", t, vc.SpeedKph, vc.IsReversing ? 1 : 0, thr, brk, vc.transform.position.x, vc.transform.position.z, gm != null && gm.IsRunning ? 1 : 0, vc.transform.position.y, pitch));
            if (traceBuffer.Length > 1500) FlushTrace();
        }

        static void FlushTrace()
        {
            if (traceBuffer.Length == 0) return;
            File.AppendAllText(Path.Combine(Dir, "play-trace.csv"), traceBuffer.ToString());
            traceBuffer.Clear();
        }

        static string FpsSummary()
        {
            if (frameTimes.Count < 5) return "fpsSamples=" + frameTimes.Count;
            var sorted = frameTimes.OrderBy(x => x).ToList();
            float avg = frameTimes.Count / frameTimes.Sum();
            int worst = Mathf.Max(1, sorted.Count / 100);
            float low1 = 1f / sorted.Skip(sorted.Count - worst).Average();
            return $"uncapped[frames={frameTimes.Count} avgFps={avg:0.0} low1pct={low1:0.0} medianMs={sorted[sorted.Count / 2] * 1000f:0.0} p99Ms={sorted[Mathf.Min(sorted.Count - 1, sorted.Count * 99 / 100)] * 1000f:0.0} maxMs={sorted[sorted.Count - 1] * 1000f:0} over40ms={spikes.Count} spikes={string.Join(";", spikes.OrderByDescending(x => x.ms).Take(8).Select(x => x.at.ToString("0.0") + "s:" + x.ms.ToString("0")))}]";
        }

        // play debug tokens: noao, nograde, nofxaa, noshadow, nofog, msaa=<0|2|4|8>: isolate the cause of an artefact in the real Game view
        static void ApplyPlayDebugTokens(string[] state)
        {
            var camMain = Camera.main;
            if (camMain != null)
                foreach (var b in camMain.GetComponents<Behaviour>())
                {
                    string n = b.GetType().Name;
                    if ((state.Contains("noao") && n == "Obscurance") || (state.Contains("nograde") && n == "MapDaylightGrade") || (state.Contains("nofxaa") && n == "MapCameraFXAA")) b.enabled = false;
                }
            if (state.Contains("noshadow")) QualitySettings.shadows = ShadowQuality.Disable;
            if (state.Contains("nofog")) RenderSettings.fog = false;
            // notrees: hide every foliage renderer (finds out whether a screen artefact comes from trees)
            if (state.Contains("notrees"))
                foreach (var mr in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                    if (mr.sharedMaterial != null && mr.sharedMaterial.name.Contains("tree")) mr.enabled = false;
            foreach (var token in state) if (token.StartsWith("msaa=")) QualitySettings.antiAliasing = int.Parse(token.Substring(5));
        }

        // The Game view of the editor: on a display scaled above 100% it can draw the game at the unscaled size and stretch it
        // ("Low Resolution Aspect Ratios", zoom 2x at the slider's left end): the whole picture is then soft.
        static string GameViewState(bool sharp, bool soft = false)
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (type == null) return "ERR no GameView type";
            const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var sb = new StringBuilder($"pixelsPerPoint={EditorGUIUtility.pixelsPerPoint:0.##} screen={Screen.currentResolution.width}x{Screen.currentResolution.height}");
            foreach (var view in Resources.FindObjectsOfTypeAll(type))
            {
                object Get(string name)
                {
                    for (var ty = type; ty != null; ty = ty.BaseType)
                    {
                        var p = ty.GetProperty(name, all | System.Reflection.BindingFlags.DeclaredOnly);
                        if (p != null) { try { return p.GetValue(view); } catch (Exception e) { return "ERR " + e.GetType().Name; } }
                        var f = ty.GetField(name, all | System.Reflection.BindingFlags.DeclaredOnly);
                        if (f != null) return f.GetValue(view);
                    }
                    return "?";
                }
                var lowProp = type.GetProperty("lowResolutionForAspectRatios", all);
                var zoom = Get("m_ZoomArea");
                object scale = "?";
                if (zoom != null && !(zoom is string)) { var sp = zoom.GetType().GetProperty("scale", all); if (sp != null) scale = sp.GetValue(zoom); }
                sb.Append($" | view position={((EditorWindow)view).position} lowRes={Get("lowResolutionForAspectRatios")} forceLowRes={Get("forceLowResolutionAspectRatios")} targetSize={Get("targetSize")} targetRenderSize={Get("targetRenderSize")} zoom={scale} minScale={Get("minScale")}");
                if (sharp && lowProp != null && lowProp.CanWrite)
                {
                    lowProp.SetValue(view, soft);
                    var update = type.GetMethod("UpdateZoomAreaAndParent", all);
                    if (update != null) { try { update.Invoke(view, null); } catch (Exception e) { sb.Append(" updateErr=" + e.GetType().Name); } }
                    var snap = type.GetMethod("SnapZoom", all);
                    if (snap != null) { try { snap.Invoke(view, new object[] { soft ? 2f : 1f }); } catch (Exception e) { sb.Append(" snapErr=" + e.GetType().Name); } }
                    ((EditorWindow)view).Repaint();
                    sb.Append($" -> lowRes={Get("lowResolutionForAspectRatios")} targetRenderSize={Get("targetRenderSize")} minScale={Get("minScale")}");
                }
            }
            return sb.ToString();
        }

        static string PrefabInfo(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return "missing " + path;
            var sb = new StringBuilder();
            var rs = prefab.GetComponentsInChildren<Renderer>(true);
            long tris = 0;
            var b = new Bounds(); bool first = true;
            foreach (var r in rs)
            {
                Mesh mesh = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh; else if (r is SkinnedMeshRenderer sk) mesh = sk.sharedMesh;
                int t = mesh != null ? mesh.triangles.Length / 3 : 0;
                tris += t;
                var rb = r.bounds; if (first) { b = rb; first = false; } else b.Encapsulate(rb);
                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) { sb.Append($"{r.name} tris={t} mat=NULL\n"); continue; }
                    string tex = mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null ? mat.GetTexture("_MainTex").name : "-";
                    string col = mat.HasProperty("_Color") ? ColorUtility.ToHtmlStringRGBA(mat.color) : "?";
                    float sm = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : -1f, me = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : -1f;
                    sb.Append($"{GetPath(r.transform, prefab.transform)} tris={t} sub{m} mat={mat.name} shader={mat.shader.name} color={col} sm={sm:0.00} met={me:0.00} tex={tex} q={mat.renderQueue} mesh={(mesh != null ? mesh.name : "-")} loc={r.transform.localPosition.ToString("0.00")}\n");
                }
            }
            sb.Append($"TOTAL tris={tris} renderers={rs.Length} bounds center={b.center.ToString("0.00")} size={b.size.ToString("0.00")} lods={prefab.GetComponentsInChildren<LODGroup>(true).Length}\n");
            return sb.ToString();
        }

        static string GetPath(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static string Probe(string argument)
        {
            var f = argument.Split(' ');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            float x = float.Parse(f[0], inv), z = float.Parse(f[1], inv);
            float half = f.Length > 2 ? float.Parse(f[2], inv) : 6f, step = f.Length > 3 ? float.Parse(f[3], inv) : 1f;
            Physics.SyncTransforms();
            var sb = new StringBuilder();
            sb.AppendLine($"probe ({x:0.0},{z:0.0}) half={half} step={step}; rows north to south, columns west to east; each cell = heights of every road collider hit");
            for (float dz = half; dz >= -half - 1e-3f; dz -= step)
            {
                for (float dx = -half; dx <= half + 1e-3f; dx += step)
                {
                    var hits = Physics.RaycastAll(new Vector3(x + dx, 200f, z + dz), Vector3.down, 400f, 1 << 30).OrderByDescending(h => h.point.y).ToArray();
                    sb.Append(hits.Length == 0 ? "  .  " : string.Join("/", hits.Select(h => h.point.y.ToString("0.0") + (h.collider.transform.parent.name == "Roads" ? "" : "L" + h.collider.transform.parent.name.Substring(5))))).Append(' ');
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static void RefuseIfOtherSceneDirty()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (scene.isDirty && scene.path != ScenePath)
                    throw new InvalidOperationException("Unsaved changes in " + scene.path + "; not touching them");
            }
        }

        // ---------- play-mode measurement ----------

        static void TickPlay(double now)
        {
            var state = SessionState.GetString(PlayStateKey, "").Split(' ');
            float seconds = float.Parse(state[0], System.Globalization.CultureInfo.InvariantCulture);
            string outDir = state[1];
            bool auto = state.Contains("auto");
            bool driveTest = state.Contains("drivetest");
            int shots = 0;
            foreach (var token in state) if (token.StartsWith("shots=")) shots = int.Parse(token.Substring(6));
            if (!EditorApplication.isPlaying) return;
            float start = SessionState.GetFloat(PlayStartKey, -1f);
            if (start < 0f)
            {
                SessionState.SetFloat(PlayStartKey, Time.realtimeSinceStartup);
                Application.runInBackground = true;
                frameTimes.Clear(); spikes.Clear(); lastFrameCount = -1; measureFrom = 2f; nextTrace = 0; traceBuffer.Clear(); statsSample = "";
                if (File.Exists(Path.Combine(Dir, "play-trace.csv"))) File.Delete(Path.Combine(Dir, "play-trace.csv"));
                if (state.Contains("trace")) File.WriteAllText(Path.Combine(Dir, "play-trace.csv"), "seconds,kph,reversing,throttle,brake,x,z,running,y,pitch\n");
                midSampleTaken = false;
                camSet = false;
                var tour = UnityEngine.Object.FindObjectOfType<DriveAssist>();
                float jumpTo = -1f;
                foreach (var token in state) if (token.StartsWith("jump=")) jumpTo = float.Parse(token.Substring(5), System.Globalization.CultureInfo.InvariantCulture);
                if (jumpTo >= 0f && tour != null) tour.JumpTo(jumpTo);
                if (auto)
                {
                    if (tour == null)
                    {
                        Report("ERR play auto: DriveAssist missing in the scene");
                        SessionState.EraseString(PlayStateKey);
                        SessionState.EraseFloat(PlayStartKey);
                        EditorApplication.isPlaying = false;
                        return;
                    }
                    tour.BeginAutoDrive();
                }
                if (driveTest) BeginDriveTest();
                if (state.Contains("courserun")) BeginCourseRun(state);
                rTestCount = 0; rTestDone = false; rTestLog = "";
                foreach (var token in state) if (token.StartsWith("rtest=")) rTestCount = int.Parse(token.Substring(6));
                ApplyPlayDebugTokens(state);
                return;
            }
            float elapsed = Time.realtimeSinceStartup - start;
            // rtest=<n>: R from n places near the routes, once the scene has run a second
            if (rTestCount > 0 && !rTestDone && elapsed >= 1f) { rTestDone = true; RunRTest(rTestCount); }
            // cam=<0..2>: switch the vehicle camera after DriveAssist has chosen the driver view (screenshots of the outside of the car)
            if (!camSet && elapsed >= 1.5f)
            {
                camSet = true;
                foreach (var token in state)
                    if (token.StartsWith("cam="))
                    {
                        var vcam = UnityEngine.Object.FindObjectOfType<Parkside.Cameras.VehicleCamera>();
                        if (vcam != null) vcam.SetView(int.Parse(token.Substring(4)));
                    }
            }
            if (driveTest) StepDriveTest(elapsed);
            if (state.Contains("courserun")) { TakePendingRunShot(outDir); StepCourseRun(elapsed); }
            if (state.Contains("pausetest")) StepPauseTest(elapsed);
            if (shots > 0 && elapsed >= (shotsTaken + 1) * seconds / (shots + 1))
            {
                shotsTaken++;
                var shotCam = Camera.main;
                Directory.CreateDirectory(outDir);
                if (shotCam != null)
                {
                    // aerial=<metres>: oblique view from above and behind the player, to see several cars of the traffic at once
                    float aerial = 0f;
                    foreach (var token in state) if (token.StartsWith("aerial=")) aerial = float.Parse(token.Substring(7), System.Globalization.CultureInfo.InvariantCulture);
                    var shotPlayer = UnityEngine.Object.FindObjectOfType<Parkside.Vehicle.VehicleController>();
                    var camT = shotCam.transform;
                    var keepPos = camT.position; var keepRot = camT.rotation; var keepFov = shotCam.fieldOfView;
                    if (aerial > 0f && shotPlayer != null)
                    {
                        var fwd = shotPlayer.transform.forward; fwd.y = 0f; fwd.Normalize();
                        camT.position = shotPlayer.transform.position - fwd * aerial * 0.5f + Vector3.up * aerial;
                        camT.LookAt(shotPlayer.transform.position + fwd * aerial * 1.2f);
                        shotCam.fieldOfView = 55f;
                    }
                    // followporter: chase view of an active Porter II of the traffic (nearest to the player), to see a real one in the scene
                    if ((state.Contains("followporter") || state.Contains("followbox")) && shotPlayer != null)
                    {
                        Transform best = null; float bd = 1e9f;
                        foreach (var cm in UnityEngine.Object.FindObjectsOfType<CarModel>())
                            if (cm.modelName == (state.Contains("followbox") ? "BoxTruck" : "PorterII") && cm.gameObject.activeInHierarchy && cm.GetComponentInParent<RouteTraffic>() != null)
                            {
                                float d = (cm.transform.position - shotPlayer.transform.position).magnitude;
                                if (d < bd) { bd = d; best = cm.transform; }
                            }
                        if (best != null)
                        {
                            camT.position = best.position - best.forward * 8f + Vector3.up * 3.2f;
                            camT.LookAt(best.position + Vector3.up * 1.2f);
                            shotCam.fieldOfView = 50f;
                            Debug.Log("BUSAN_BRIDGE porter at " + best.position.ToString("0.0") + " distToPlayer=" + bd.ToString("0"));
                        }
                    }
                    RenderToPng(shotCam, Path.Combine(outDir, $"play-{shotsTaken:00}.png"), 1280, 720);
                    camT.SetPositionAndRotation(keepPos, keepRot); shotCam.fieldOfView = keepFov;
                }
            }
            if (!midSampleTaken && elapsed >= seconds * 0.4f)
            {
                midSampleTaken = true;
                midSample = SampleTraffic();
                statsSample = $"triangles={UnityEditor.UnityStats.triangles} batches={UnityEditor.UnityStats.batches} drawCalls={UnityEditor.UnityStats.drawCalls} shadowCasters={UnityEditor.UnityStats.shadowCasters} setPass={UnityEditor.UnityStats.setPassCalls}";
            }
            // The Game view path includes overlay UI and OnGUI, which Camera.Render leaves out.
            if (!gameViewShotTaken && elapsed >= seconds * 0.7f)
            {
                gameViewShotTaken = true;
                Directory.CreateDirectory(outDir);
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "game-view.png"));
            }
            if (elapsed < seconds) return;
            string end = SampleTraffic();
            Directory.CreateDirectory(outDir);
            var cam = Camera.main;
            if (cam != null) RenderToPng(cam, Path.Combine(outDir, "play-end.png"), 1280, 720);
            var report = new StringBuilder();
            report.Append("OK play seconds=").Append(seconds).Append(" fps=").Append((1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f)).ToString("0")).Append(" frames=").Append(Time.frameCount).Append(" gameTime=").Append(Time.timeSinceLevelLoad.ToString("0.0")).Append(" focused=").Append(UnityEditorInternal.InternalEditorUtility.isApplicationActive).Append(" paused=").Append(EditorApplication.isPaused);
            FlushTrace();
            if (state.Contains("uncapped")) report.Append(' ').Append(FpsSummary()).Append(" stats[").Append(statsSample).Append(']');
            report.Append(" runInBackground=").Append(Application.runInBackground);
            report.Append(" traffic[").Append(CompareTraffic(midSample, end)).Append("]");
            var trafficNow = UnityEngine.Object.FindObjectOfType<RouteTraffic>();
            if (trafficNow != null) report.Append(" trafficStats[").Append(trafficNow.StatsLine()).Append("]");
            var loopTraffic = UnityEngine.Object.FindObjectOfType<LoopTraffic>();
            if (loopTraffic != null) report.Append(" ").Append(loopTraffic.StatsLine()).Append(" nearList[").Append(loopTraffic.NearList).Append("]");
            var player = UnityEngine.Object.FindObjectOfType<Parkside.Vehicle.VehicleController>();
            if (player != null) report.Append(" player=").Append(player.transform.position.ToString("0.0")).Append(" kph=").Append(player.SpeedKph.ToString("0"));
            report.Append(" scene=").Append(EditorSceneManager.GetActiveScene().name).Append(" logErrors=").Append(SessionState.GetInt("BusanBridge.PlayErrors", 0));
            if (driveTest) report.Append(" driveTest[").Append(driveLog).Append("]");
            if (state.Contains("courserun")) report.Append(" courseRun[").Append(CourseRunReport()).Append("]");
            if (state.Contains("pausetest")) report.Append(" pauseTest[").Append(pauseLog).Append("]");
            if (rTestCount > 0) report.Append(" rTest[").Append(rTestDone ? rTestLog : "not run").Append("]");
            var manager = UnityEngine.Object.FindObjectOfType<Parkside.Core.GameManager>();
            if (manager != null) report.Append(" running=").Append(manager.IsRunning).Append(" inputReady=").Append(manager.InputReady);
            Report(report.ToString());
            SessionState.EraseString(PlayStateKey);
            SessionState.EraseFloat(PlayStartKey);
            SessionState.EraseString(DemoCapture.EditorArgsKey);
            EditorApplication.isPlaying = false;
        }

        // ---------- course run: a steering bot drives the player's own controller along the course ----------
        // courserun [from=<m>] [warp=<n>]: pure pursuit toward the player's lane 9 m + 0.6 s ahead, 50 km/h, slower in bends. Records what a
        // driver would feel: height jumps in one 20 ms step (bumps), pitch jumps, seconds without road under the car, stalls (held at a
        // standstill for 3 s, then moved 15 m on), and the distance from the lane centre. warp runs n fixed steps per real step.
        // With several routes (the landmark map): route=<i> drives route i (made the preferred one, progress judged on it, the guide
        // shown) and laps=<n> ends a closed route after n laps of unwrapped distance (default 1). lane=<m> drives m metres right (+) or
        // left (-) of the lane line (edge runs). shotsEvery=<m>: a driver picture every m metres of progress, taken once the camera
        // has followed the car (the bot waits a few frames for VehicleCamera's LateUpdate). startOn=<k>: start on route k's lane (so
        // route k is the one shown) and steer route i, to see the shown route change where the car takes the other branch.
        // cruise=<m/s>: the bot's speed on the straight instead of 13.9 (50 km/h), to see the traffic as a faster driver does.
        // Without these tokens the run is as before.

        static float runClock, runS0, runStuckFor, runMaxLat, runSumLat, runOffRoadFor, runQueuedSeconds;
        static int runSteps, runBumps, runPitchJumps, runStalls, runOffRoad, runWarp, runLatSamples, runLat3;
        static Vector3 runLast; static float runLastPitch; static bool runStarted, runDone, runWasOff;
        static readonly List<string> runEvents = new List<string>();
        static int runRoute = -1, runStartOn = -1, runLaps = 1, runShotIndex, runShotFrame, runPendingKind, runActiveSamples, runActiveOn;
        static float runCruise = 13.9f, runLane, runShotsEvery, runProgress, runPrevS, runNextShot, runMinY, runMaxY, runMaxYAt;
        static string runMaxYShown = "", runShotName = "", runLastShown = "", runCarsMode = "";
        static bool runShotPending, runTopShotTaken;
        static readonly List<string> runShown = new List<string>();

        static void BeginCourseRun(string[] state)
        {
            var gm = UnityEngine.Object.FindObjectOfType<Parkside.Core.GameManager>();
            if (gm != null) gm.enabled = false;
            var receiver = UnityEngine.Object.FindObjectOfType<Parkside.Input.VehicleInputReceiver>();
            if (receiver != null) receiver.enabled = false;
            runS0 = 0f; runWarp = 1;
            runRoute = -1; runStartOn = -1; runLaps = 1; runLane = 0f; runShotsEvery = 0f; runCruise = 13.9f; runCarsMode = ""; recs = null;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var token in state)
            {
                if (token.StartsWith("from=")) runS0 = float.Parse(token.Substring(5), inv);
                if (token.StartsWith("warp=")) runWarp = Mathf.Clamp(int.Parse(token.Substring(5)), 1, 6);
                if (token.StartsWith("route=")) runRoute = int.Parse(token.Substring(6));
                if (token.StartsWith("laps=")) runLaps = Mathf.Max(1, int.Parse(token.Substring(5)));
                if (token.StartsWith("lane=")) runLane = float.Parse(token.Substring(5), inv);
                if (token.StartsWith("shotsEvery=")) runShotsEvery = float.Parse(token.Substring(11), inv);
                if (token.StartsWith("startOn=")) runStartOn = int.Parse(token.Substring(8));
                if (token.StartsWith("cruise=")) runCruise = Mathf.Clamp(float.Parse(token.Substring(7), inv), 5f, 30f);
                if (token.StartsWith("cars=")) runCarsMode = token.Substring(5);   // cars=off | noshadow (the moving cars hidden / without shadows) | rec (frame timers)
            }
            runClock = 0f; runStuckFor = 0f; runMaxLat = 0f; runSumLat = 0f; runOffRoadFor = 0f; runQueuedSeconds = 0f;
            runSteps = runBumps = runPitchJumps = runStalls = runOffRoad = runLatSamples = runLat3 = 0;
            runStarted = false; runDone = false; runWasOff = false; runEvents.Clear();
            runProgress = 0f; runPrevS = 0f; runNextShot = 0f; runShotIndex = 0; runShotPending = false; runShotFrame = -1; runPendingKind = 0;
            runActiveSamples = runActiveOn = 0; runMinY = float.MaxValue; runMaxY = float.MinValue; runMaxYAt = 0f; runMaxYShown = ""; runLastShown = "";
            runTopShotTaken = false; runShown.Clear();
        }

        static void StepCourseRun(float t)
        {
            RecordFrame();
            while (runClock < t * runWarp && !runDone && !runShotPending) { runClock += 0.02f; CourseRunOnce(0.02f); }
        }

        // The lane point the bot steers to and measures from: the course's (or route=<i>'s) lane line, lane=<m> to its right.
        static Vector3 BotLane(DriveAssist assist, float s)
        {
            if (runRoute >= 0) return assist.RouteLanePointAt(runRoute, s, runLane);
            if (runLane == 0f) return assist.LanePointAt(s);
            var t = assist.LanePointAt(s + 1f) - assist.LanePointAt(s - 1f); t.y = 0f;
            return assist.LanePointAt(s) + Vector3.Cross(Vector3.up, t).normalized * runLane;
        }

        // A picture asked for by shotsEvery (or the top of the route) once the frames have caught up with the car: VehicleCamera
        // moves the camera in LateUpdate, so a render straight after the bot's steps shows the bonnet where the car was.
        static void TakePendingRunShot(string outDir)
        {
            if (!runShotPending || Time.frameCount < runShotFrame + 3) return;
            Directory.CreateDirectory(outDir);
            if (runPendingKind == 2) ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "game-view-top.png"));   // with the guide (OnGUI)
            else if (Camera.main != null) RenderToPng(Camera.main, Path.Combine(outDir, runShotName), 1280, 720);
            runShotPending = false;
        }

        static void AskRunShot(int kind, string name)
        {
            runShotPending = true; runPendingKind = kind; runShotName = name; runShotFrame = Time.frameCount;
        }

        // cars=rec: milliseconds per frame (sampled each editor step) of the main parts of the frame, written to C:/c/busan-out/play_v43/rec.txt
        static readonly string[] RecNames = { "PlayerLoop", "LoopTraffic.Update", "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate",
            "FixedUpdate.PhysicsFixedUpdate", "FixedUpdate.ScriptRunBehaviourFixedUpdate", "Camera.Render", "Culling", "Render.OpaqueGeometry", "Render.TransparentGeometry",
            "Camera.ImageEffects", "Drawing", "Shadows.RenderShadowMap", "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForGfxCommandsFromMainThread", "Gfx.PresentFrame",
            "PostLateUpdate.FinishFrameRendering", "EditorLoop", "GUI.Repaint", "UGUI.Rendering.RenderOverlays", "Canvas.SendWillRenderCanvases", "GC.Collect", "BatchRenderer.Flush" };
        static UnityEngine.Profiling.Recorder[] recs; static double[] recSum; static int recFrames, recLastFrame = -1; static double recFrameMs;
        static void RecordFrame()
        {
            if (runCarsMode != "rec" || Time.frameCount == recLastFrame) return;
            recLastFrame = Time.frameCount;
            if (recs == null) { recs = RecNames.Select(n => UnityEngine.Profiling.Recorder.Get(n)).ToArray(); foreach (var r in recs) r.enabled = true; recSum = new double[recs.Length]; recFrames = 0; recFrameMs = 0; return; }
            for (int i = 0; i < recs.Length; i++) recSum[i] += recs[i].isValid ? recs[i].elapsedNanoseconds / 1e6 : 0;
            recFrames++; recFrameMs += Time.unscaledDeltaTime * 1000.0;
            if (recFrames % 50 == 0)
            {
                var sb = new StringBuilder($"frames={recFrames} frameMs={recFrameMs / recFrames:0.00}\n");
                sb.Append($"batches={UnityEditor.UnityStats.batches} drawCalls={UnityEditor.UnityStats.drawCalls} setPass={UnityEditor.UnityStats.setPassCalls} tris={UnityEditor.UnityStats.triangles} shadowCasters={UnityEditor.UnityStats.shadowCasters}\n");
                foreach (var cam in Camera.allCameras)
                {
                    string rt = cam.targetTexture != null ? cam.targetTexture.width + "x" + cam.targetTexture.height : "screen";
                    sb.Append($"camera {cam.name} depth={cam.depth} rt={rt} far={cam.farClipPlane:0} mask={cam.cullingMask} path={cam.actualRenderingPath} hdr={cam.allowHDR} msaa={cam.allowMSAA}\n");
                }
                foreach (var b in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()) if (b.enabled && b.GetComponent<Camera>() != null) sb.Append($"camfx {b.gameObject.name}.{b.GetType().Name}\n");
                if (recFrames == 50)
                {
                    // renderers by the scene's top two levels: count, visible now, materials, triangles, static-batched, instancing
                    var groups = new Dictionary<string, (int n, int vis, HashSet<Material> mats, long tris, int batched, int inst)>();
                    foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                    {
                        if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                        var t = r.transform; var chain = new List<string>();
                        while (t != null) { chain.Insert(0, t.name); t = t.parent; }
                        string key = string.Join("/", chain.Take(Mathf.Min(3, chain.Count - 1)));
                        if (!groups.TryGetValue(key, out var g)) g = (0, 0, new HashSet<Material>(), 0, 0, 0);
                        g.n++; if (r.isVisible) g.vis++;
                        foreach (var m in r.sharedMaterials) if (m != null) { g.mats.Add(m); if (m.enableInstancing) g.inst++; }
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) g.tris += mf.sharedMesh.triangles.Length / 3;
                        if (r.isPartOfStaticBatch) g.batched++;
                        groups[key] = g;
                    }
                    var gs = new StringBuilder();
                    foreach (var kv in groups.OrderByDescending(k => k.Value.vis))
                        gs.Append($"{kv.Key} renderers={kv.Value.n} visibleNow={kv.Value.vis} materials={kv.Value.mats.Count} tris={kv.Value.tris} staticBatched={kv.Value.batched} instancingMats={kv.Value.inst}\n");
                    File.WriteAllText("C:/c/busan-out/play_v43/rec_scene.txt", gs.ToString());
                }
                for (int i = 0; i < recs.Length; i++) sb.Append($"{RecNames[i]} valid={recs[i].isValid} ms={recSum[i] / recFrames:0.000}\n");
                Directory.CreateDirectory("C:/c/busan-out/play_v43"); File.WriteAllText("C:/c/busan-out/play_v43/rec.txt", sb.ToString());
            }
        }

        static void CourseRunOnce(float dt)
        {
            var vc = UnityEngine.Object.FindObjectOfType<Parkside.Vehicle.VehicleController>();
            var assist = UnityEngine.Object.FindObjectOfType<DriveAssist>();
            if (vc == null || assist == null) return;
            if (!runStarted)
            {
                if (runRoute >= 0)
                {
                    assist.SetPreferredRoute(runRoute);
                    assist.ShowGuide = true;
                    assist.JumpTo(runStartOn >= 0 ? runStartOn : runRoute, runS0);
                    runPrevS = assist.RouteDistanceNearest(runRoute, vc.transform.position);
                }
                else
                {
                    assist.JumpTo(runS0);
                    if (runShotsEvery > 0f) runPrevS = assist.DistanceNearest(vc.transform.position);
                }
                runLast = vc.transform.position; runLastPitch = 0f; runStarted = true;
                var lt = UnityEngine.Object.FindObjectOfType<LoopTraffic>();
                if (lt != null && runCarsMode.Length > 0)
                    foreach (var r in lt.GetComponentsInChildren<Renderer>())
                    {
                        if (runCarsMode == "off") r.enabled = false;
                        if (runCarsMode == "noshadow") r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                return;
            }
            var pos = vc.transform.position;
            float s;
            if (runRoute >= 0)
            {
                s = assist.RouteDistanceNearest(runRoute, pos);
                float length = assist.RouteLength(runRoute);
                bool closed = assist.RouteClosed(runRoute);
                if (closed)
                {
                    float ds = s - runPrevS;
                    if (ds > length / 2f) ds -= length; else if (ds < -length / 2f) ds += length;
                    runProgress += ds;
                }
                else runProgress = s - runS0;
                runPrevS = s;
                if (closed ? runProgress >= runLaps * length : s >= length - 40f)
                {
                    runDone = true; runEvents.Add($"finished laps={runProgress / length:0.000} progress={runProgress:0} s={s:0}");
                    return;
                }
            }
            else
            {
                s = assist.DistanceNearest(pos);
                if (s >= assist.CourseLength - 40f) { runDone = true; runEvents.Add($"finished s={s:0}"); return; }
                runProgress = s - runS0;
            }
            float speed = vc.SpeedMps;
            var target = BotLane(assist, s + 9f + speed * 0.6f) - pos; target.y = 0f;
            var fwd = vc.transform.forward; fwd.y = 0f;
            float angle = Vector3.SignedAngle(fwd, target, Vector3.up);
            float steer = Mathf.Clamp(angle * 1.4f, -30f, 30f);
            float wanted = Mathf.Lerp(runCruise, 5f, Mathf.Clamp01(Mathf.Abs(angle) / 25f));
            float throttle = speed < wanted ? 0.85f : 0f, brake = speed > wanted + 2f ? 1f : 0f;
            assist.EdgeStep(dt);                 // as DriveAssist.Update does in the game, before the controller moves the car
            vc.Simulate(new Parkside.Input.VehicleControlInput(steer, throttle, brake), true, dt);
            runSteps++;
            var now = vc.transform.position;
            float dy = Mathf.Abs(now.y - runLast.y);
            float pitch = vc.transform.eulerAngles.x; if (pitch > 180f) pitch -= 360f;
            if (dy > 0.06f) { runBumps++; if (runEvents.Count < 40) runEvents.Add($"bump s={s:0} ({now.x:0},{now.z:0}) dy={dy:0.00}"); }
            if (Mathf.Abs(pitch - runLastPitch) > 2.5f) { runPitchJumps++; if (runEvents.Count < 40) runEvents.Add($"pitch s={s:0} ({now.x:0},{now.z:0}) {runLastPitch:0.0}->{pitch:0.0}"); }
            bool road = OnRoad(now);
            if (!road) { runOffRoadFor += dt; if (runOffRoadFor >= 0.5f && runOffRoadFor - dt < 0.5f) { runOffRoad++; if (runEvents.Count < 40) runEvents.Add($"offroad s={s:0} ({now.x:0},{now.z:0})"); } }
            else runOffRoadFor = 0f;
            // held behind a car of the traffic (speed cap 0) is queueing, not a stall of the road surface
            bool queued = vc.SpeedCap <= 0.01f;
            if (queued) runQueuedSeconds += dt;
            if (speed < 0.5f && wanted > 2f && !queued) runStuckFor += dt; else runStuckFor = 0f;
            if (runStuckFor > 3f)
            {
                runStalls++; runStuckFor = 0f;
                if (runEvents.Count < 40) runEvents.Add($"stall s={s:0} ({now.x:0},{now.z:0})");
                if (runRoute >= 0) assist.JumpTo(runRoute, s + 15f); else assist.JumpTo(s + 15f);
                now = vc.transform.position;
                pitch = vc.transform.eulerAngles.x; if (pitch > 180f) pitch -= 360f;
            }
            if (runSteps % 25 == 0)
            {
                // the nearest course point is up to ~6 m off along the road (points ~12 m apart): measure across the lane line instead
                var la = BotLane(assist, s - 12f); var lb = BotLane(assist, s + 12f);
                var lane = BotLane(assist, s); lane.y = now.y;
                var ab = new Vector2(lb.x - la.x, lb.z - la.z); var ap = new Vector2(now.x - la.x, now.z - la.z);
                float lat = ab.sqrMagnitude > 1f ? Mathf.Abs(ab.x * ap.y - ab.y * ap.x) / ab.magnitude : Vector3.Distance(lane, now);
                runMaxLat = Mathf.Max(runMaxLat, lat); runSumLat += lat; runLatSamples++;
                if (lat > 3f)
                {
                    runLat3++;
                    if (!runWasOff && runEvents.Count < 40) runEvents.Add($"laneOff s={s:0} ({now.x:0},{now.z:0}) dev={lat:0.0} roadUnderLane={OnRoad(lane)}");
                    runWasOff = true;
                }
                else runWasOff = false;
                if (runRoute >= 0)
                {
                    // the route the guide shows: how often it is the one driven, and each change
                    runActiveSamples++;
                    if (assist.ActiveRoute == runRoute) runActiveOn++;
                    var shown = assist.RouteName(assist.ActiveRoute);
                    if (shown != runLastShown) { runLastShown = shown; if (runShown.Count < 30) runShown.Add($"{runProgress:0}:{shown}"); }
                }
            }
            if (runRoute >= 0)
            {
                runMinY = Mathf.Min(runMinY, now.y);
                if (now.y > runMaxY) { runMaxY = now.y; runMaxYAt = s; runMaxYShown = assist.RouteName(assist.ActiveRoute); }
                // the first time up on the deck: the game view with the guide, to see the route it shows
                if (!runTopShotTaken && now.y > 29f) { runTopShotTaken = true; AskRunShot(2, ""); }
            }
            if (runShotsEvery > 0f && runProgress >= runNextShot && !runShotPending)
            {
                AskRunShot(1, $"drive-{runShotIndex:000}-p{Mathf.Max(0f, runProgress):00000}.png");
                runShotIndex++;
                runNextShot += runShotsEvery;
            }
            runLast = now; runLastPitch = pitch;
        }

        static string CourseRunReport()
        {
            var vc = UnityEngine.Object.FindObjectOfType<Parkside.Vehicle.VehicleController>();
            var assist = UnityEngine.Object.FindObjectOfType<DriveAssist>();
            if (runRoute >= 0)
            {
                float rs = vc != null && assist != null ? assist.RouteDistanceNearest(runRoute, vc.transform.position) : -1f;
                float length = assist != null ? assist.RouteLength(runRoute) : 0f;
                return $"route={runRoute} '{(assist != null ? assist.RouteName(runRoute) : "")}' {(assist != null && assist.RouteClosed(runRoute) ? "closed" : "open")} length={length:0.0} lane={runLane:+0.0;-0.0;0.0} " +
                       $"from={runS0:0} laps={(length > 0f ? runProgress / length : 0f):0.000} of {runLaps} progress={runProgress:0} reached={rs:0} simSeconds={runSteps * 0.02f:0} " +
                       $"queuedBehindTraffic={runQueuedSeconds:0}s bumps={runBumps} pitchJumps={runPitchJumps} offRoad={runOffRoad} stalls={runStalls} " +
                       $"laneDevMean={(runLatSamples > 0 ? runSumLat / runLatSamples : 0f):0.00} laneDevMax={runMaxLat:0.0} laneDevOver3m={runLat3}/{runLatSamples} " +
                       $"heights=[{runMinY:0.0}..{runMaxY:0.0}, top at s={runMaxYAt:0} shown '{runMaxYShown}'] shownRoute={(runActiveSamples > 0 ? 100f * runActiveOn / runActiveSamples : 0f):0.0}% of {runActiveSamples} samples " +
                       $"shownChanges=[{string.Join(", ", runShown)}] pictures={runShotIndex}{(runTopShotTaken ? "+game-view-top" : "")} events: {string.Join("; ", runEvents)}";
            }
            float s = vc != null && assist != null ? assist.DistanceNearest(vc.transform.position) : -1f;
            return $"from={runS0:0} reached={s:0} of {(assist != null ? assist.CourseLength : 0f):0} simSeconds={runSteps * 0.02f:0} queuedBehindTraffic={runQueuedSeconds:0}s bumps={runBumps} pitchJumps={runPitchJumps} offRoad={runOffRoad} stalls={runStalls} " +
                   $"laneDevMean={(runLatSamples > 0 ? runSumLat / runLatSamples : 0f):0.00} laneDevMax={runMaxLat:0.0} laneDevOver3m={runLat3}/{runLatSamples} events: {string.Join("; ", runEvents)}" +
                   (runLane != 0f ? $" lane={runLane:+0.0;-0.0}" : "") + (runShotsEvery > 0f ? $" pictures={runShotIndex}" : "");
        }

        // ---------- rtest=<n>: the R key from n places near the routes ----------
        // Fixed seed. Each trial puts the car on a random route at a random distance, up to 30 m away in any direction and 3 m up or
        // down, turned any way and sometimes in reverse, then calls DriveAssist.ReturnToCourse (the R key's code). A trial is good
        // when the car ends within 1 m of a lane line (height weighted as the nearest-point search) with a road collider under it.

        static int rTestCount; static bool rTestDone; static string rTestLog = "";

        static void RunRTest(int n)
        {
            var vc = UnityEngine.Object.FindObjectOfType<Parkside.Vehicle.VehicleController>();
            var assist = UnityEngine.Object.FindObjectOfType<DriveAssist>();
            if (vc == null || assist == null) { rTestLog = "ERROR no car or DriveAssist in the scene"; return; }
            const int Seed = 20261111;
            var rnd = new System.Random(Seed);
            int routes = Mathf.Max(1, assist.RouteCount), good = 0, nearLane = 0, onRoad = 0, forward = 0;
            float worst = 0f;
            var lines = new List<string>();
            for (int i = 0; i < n; i++)
            {
                int k = rnd.Next(routes);
                float s = (float)rnd.NextDouble() * assist.RouteLength(k);
                var lane = assist.RouteLanePointAt(k, s);
                float heading = (float)rnd.NextDouble() * 360f, away = 30f * Mathf.Sqrt((float)rnd.NextDouble());
                var from = lane + Quaternion.Euler(0f, heading, 0f) * Vector3.forward * away;
                from.y = lane.y + (float)(rnd.NextDouble() * 6.0 - 3.0);
                vc.transform.SetPositionAndRotation(from, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f));
                bool reversed = rnd.NextDouble() < 0.3;
                if (reversed && !vc.IsReversing) vc.ToggleDirection();
                assist.ReturnToCourse();
                var at = vc.transform.position;
                float d = float.MaxValue;
                for (int r = 0; r < routes; r++) d = Mathf.Min(d, assist.RouteLaneDistance(r, at));
                bool road = OnRoad(at);
                int ar = assist.ActiveRoute;
                float arS = assist.RouteDistanceNearest(ar, at);
                var along = assist.RouteLanePointAt(ar, arS + 1f) - assist.RouteLanePointAt(ar, arS - 1f); along.y = 0f;
                var carForward = vc.transform.forward; carForward.y = 0f;
                bool ahead = !vc.IsReversing && Vector3.Dot(carForward.normalized, along.normalized) > 0.9f;
                if (d <= 1f) nearLane++;
                if (road) onRoad++;
                if (ahead) forward++;
                bool ok = d <= 1f && road;
                if (ok) good++;
                worst = Mathf.Max(worst, d);
                if (lines.Count < 8 || !ok)
                    lines.Add($"#{i} from route {k} s={s:0} {away:0.0}m off ({from.x:0.0},{from.y:0.0},{from.z:0.0}){(reversed ? " reversing" : "")} -> route {ar} '{assist.RouteName(ar)}' s={arS:0} ({at.x:0.0},{at.y:0.00},{at.z:0.0}) lane {d:0.00}m road={road} forward={ahead}{(ok ? "" : " FAIL")}");
            }
            rTestLog = $"n={n} seed={Seed} good={good}/{n} withinLane1m={nearLane}/{n} onRoad={onRoad}/{n} facingAlongRoute={forward}/{n} worstLaneDistance={worst:0.00}m trials: {string.Join("; ", lines)}";
        }

        // ---------- fixed-input driving test (keyboard cannot be simulated, so Simulate is called directly) ----------

        static void BeginDriveTest()
        {
            var gm = UnityEngine.Object.FindObjectOfType<Parkside.Core.GameManager>();
            if (gm != null) gm.enabled = false;           // otherwise GameManager.Update also simulates with real input
            var receiver = UnityEngine.Object.FindObjectOfType<Parkside.Input.VehicleInputReceiver>();
            if (receiver != null) receiver.enabled = false; // the test feeds DriveKeyboardSource.Evaluate directly
            driveLog = "";
            driveStep = 0;
            driveStepStart = -1f;
            driveClock = 0f;
            driveLastSecond = -1;
        }

        static bool OnRoad(Vector3 p) => Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, 4f, 1 << 30);

        static float driveClock;

        // The bridge ticks at 4 Hz, so the test advances its own fixed 50 Hz clock to cover the elapsed real time.
        static void StepDriveTest(float t)
        {
            if (driveStep == 0 && driveStepStart < 0f && driveClock > t) driveClock = 0f;
            while (driveClock < t) { driveClock += 0.02f; DriveOnce(driveClock, 0.02f); }
        }

        // pausetest: a pause from the HUD button must hold, a pause from losing focus must end when focus is back.
        // DriveAssist only resumes with the application focused, so "focused" in the report says whether the run counts.
        static int pauseStep;
        static string pauseLog = "";

        static void StepPauseTest(float t)
        {
            var manager = UnityEngine.Object.FindObjectOfType<Parkside.Core.GameManager>();
            var assist = UnityEngine.Object.FindObjectOfType<DriveAssist>();
            if (manager == null || assist == null) return;
            switch (pauseStep)
            {
                case 0:
                    if (t < 4f) return;
                    pauseLog += $"focused={Application.isFocused} runningBefore={manager.IsRunning} ";
                    manager.TogglePractice();
                    pauseStep = 1;
                    break;
                case 1:
                    if (t < 6f) return;
                    pauseLog += $"hudPauseHolds={!manager.IsRunning} ";
                    manager.TogglePractice();
                    pauseStep = 2;
                    break;
                case 2:
                    if (t < 7f) return;
                    pauseLog += $"hudStartRuns={manager.IsRunning} ";
                    manager.SendMessage("OnApplicationFocus", false);
                    assist.SendMessage("OnApplicationFocus", false);
                    pauseLog += $"pausedByFocus={!manager.IsRunning} ";
                    pauseStep = 3;
                    break;
                case 3:
                    if (t < 8f) return;
                    pauseLog += $"resumedAfterFocus={manager.IsRunning} focusedAtEnd={Application.isFocused} ";
                    pauseStep = 4;
                    break;
            }
        }

        static void DriveOnce(float t, float dt)
        {
            var vc = UnityEngine.Object.FindObjectOfType<Parkside.Vehicle.VehicleController>();
            var assist = UnityEngine.Object.FindObjectOfType<DriveAssist>();
            if (vc == null || assist == null || driveStep < 0) return;
            if (driveStepStart < 0f) driveStepStart = t;
            float local = t - driveStepStart;
            Parkside.Input.VehicleControlInput Input(float steer, float throttle, float brake) => new Parkside.Input.VehicleControlInput(steer, throttle, brake);
            switch (driveStep)
            {
                case 0: // road edge: aim 25 degrees off the carriageway and hold the throttle; the car must slide along or stop on the road
                    if (local < 0.05f)
                    {
                        assist.JumpTo(1500f);
                        vc.transform.Rotate(0f, 25f, 0f);
                        driveStart = vc.transform.position;
                        vc.Simulate(Input(0f, 0f, 0f), true, dt);
                        return;
                    }
                    vc.Simulate(Input(0f, 0.7f, 0f), true, dt);
                    if ((int)local != driveLastSecond) { driveLastSecond = (int)local; driveLog += $"t{driveLastSecond}:({vc.transform.position.x:0},{vc.transform.position.z:0})@{vc.SpeedKph:0} "; }
                    if (local > 6f)
                    {
                        driveLog += $"edge[start={driveStart.ToString("0.0")} end={vc.transform.position.ToString("0.0")} moved={Vector3.Distance(driveStart, vc.transform.position):0}m kph={vc.SpeedKph:0} onRoad={OnRoad(vc.transform.position)}] ";
                        driveStep = 1; driveStepStart = -1f;
                    }
                    break;
                case 1: // side street entry: straight approach, then pure pursuit toward a point 20 m inside the side street
                    if (local < 0.05f)
                    {
                        var lines = File.ReadAllLines(Path.Combine(Dir, "mouths.txt")).Where(l => l.Trim().Length > 0).ToArray();
                        var f = lines[Mathf.Min(lines.Length - 1, lines.Length / 3)].Split(' ');
                        var inv = System.Globalization.CultureInfo.InvariantCulture;
                        driveMouth = new Vector3(float.Parse(f[0], inv), float.Parse(f[1], inv), float.Parse(f[2], inv));
                        driveMouthDir = new Vector3(float.Parse(f[3], inv), 0f, float.Parse(f[4], inv));
                        assist.JumpTo(assist.DistanceNearest(driveMouth) - 22f);
                        vc.Simulate(Input(0f, 0f, 0f), true, dt);
                        return;
                    }
                    {
                        var to = driveMouth - vc.transform.position; to.y = 0f;
                        float along = Vector3.Dot(to, vc.transform.forward);
                        var aim = driveMouth + driveMouthDir * 20f - vc.transform.position; aim.y = 0f;
                        float steer = along < 10f ? Mathf.Clamp(Vector3.SignedAngle(vc.transform.forward, aim, Vector3.up) * 1.2f, -30f, 30f) : 0f;
                        vc.Simulate(Input(steer, 0.3f, 0f), true, dt);
                    }
                    if (local > 9f)
                    {
                        var rel = vc.transform.position - driveMouth; rel.y = 0f;
                        bool inside = Vector3.Dot(rel, driveMouthDir) > 5f && rel.magnitude < 60f;
                        driveLog += $"sidestreet[mouth={driveMouth.ToString("0")} car={vc.transform.position.ToString("0.0")} enteredSideStreet={inside} onRoad={OnRoad(vc.transform.position)} kph={vc.SpeedKph:0}] ";
                        driveStep = 2; driveStepStart = -1f;
                    }
                    break;
                case 2: // return with R
                    assist.ReturnToCourse();
                    {
                        driveLog += $"returnR[car={vc.transform.position.ToString("0.0")} onRoad={OnRoad(vc.transform.position)} kph={vc.SpeedKph:0}] ";
                    }
                    driveStep = 3; driveStepStart = -1f;
                    break;
                case 3: // approach a car standing still in the player's lane
                    if (local < 0.05f)
                    {
                        assist.JumpTo(700f);
                        var tr = UnityEngine.Object.FindObjectOfType<RouteTraffic>();
                        stoppedCar = tr != null ? tr.TestPlaceStoppedAhead(vc.transform.position, vc.transform.forward, 45f) : null;
                        stopMin = 999f; stopPassedAt = -1f; stopHeldAt = -1f; stopGapAtHold = -1f;
                        vc.Simulate(Input(0f, 0f, 0f), true, dt);
                        return;
                    }
                    vc.Simulate(Input(0f, 0.7f, 0f), true, dt);
                    if (stoppedCar != null)
                    {
                        var rel = stoppedCar.position - vc.transform.position; rel.y = 0f;
                        float ahead = Vector3.Dot(rel, vc.transform.forward);
                        stopMin = Mathf.Min(stopMin, ahead - 4.6f);
                        if ((int)local != driveLastSecond && (int)local % 3 == 0) { driveLastSecond = (int)local; driveLog += $"[t{(int)local} gap={ahead - 4.6f:0.0} v={vc.SpeedMps:0.0} cap={vc.SpeedCap:0.0} rev={vc.IsReversing} carActive={stoppedCar.gameObject.activeSelf} fwd={Vector3.Dot(stoppedCar.forward, vc.transform.forward):0.00}] "; }
                        if (stopHeldAt < 0f && local > 3f && vc.SpeedMps < 0.3f) { stopHeldAt = local; stopGapAtHold = ahead - 4.6f; }
                        if (stopPassedAt < 0f && ahead < -1f) stopPassedAt = local;
                    }
                    if (local > 20f)
                    {
                        driveLog += $"stoppedCarAhead[heldAtT={stopHeldAt:0.0} bumperGapWhenHeld={stopGapAtHold:0.0}m minBumperGap={stopMin:0.0}m passedAtT={stopPassedAt:0.0} (bypass after 4 s held) endKph={vc.SpeedKph:0}] ";
                        driveStep = 4; driveStepStart = -1f;
                    }
                    break;
                case 4: // reversing, then R: must come back facing forward with the steering cleared
                    if (local < 0.05f) { assist.JumpTo(2000f); vc.ToggleDirection(); vc.Simulate(Input(0f, 0f, 0f), true, dt); return; }
                    if (local < 1.5f) { vc.Simulate(Input(20f, 0.5f, 0f), true, dt); return; }
                    {
                        bool wasReversing = vc.IsReversing;
                        assist.ReturnToCourse();
                        driveLog += $"reverseThenR[reversingBefore={wasReversing} reversingAfter={vc.IsReversing}] ";
                        driveStep = 5; driveStepStart = -1f;
                    }
                    break;
                case 5: // DriveKeyboardSource rules, called through Evaluate at the receiver's 10 Hz
                    if (local < 0.05f)
                    {
                        assist.JumpTo(2000f);
                        keySrc = UnityEngine.Object.FindObjectOfType<BusanEnvironment.DriveKeyboardSource>();
                        if (keySrc == null) { driveLog += "keys[ERROR no DriveKeyboardSource] "; driveStep = 6; driveStepStart = -1f; return; }
                        keySrc.IgnoreRunState = true;
                        keyStart = vc.transform.position; keyForward = vc.transform.forward;
                        keyPhase = 0; keyT0 = 0f; keyTick = 0; keyResult = ""; keyStopAt = -1f; keyPressAt = -1f;
                        vc.Simulate(Input(0f, 0f, 0f), true, dt);
                        return;
                    }
                    KeyTest(vc, local, dt);
                    if (keyPhase >= 99 || local > 45f)
                    {
                        driveLog += $"keys[{keyResult}{(keyPhase >= 99 ? "" : " TIMEOUT phase=" + keyPhase)}] ";
                        driveStep = 6; driveStepStart = -1f;
                    }
                    break;
                case 6: // 15 km/h from the course into a class 6 alley and to its end
                    AlleyDrive(vc, assist, local, dt);
                    break;

            }
        }

        // ---------- keyboard rule test ----------

        static BusanEnvironment.DriveKeyboardSource keySrc;
        static Vector3 keyStart, keyForward;
        static int keyPhase, keyTick;
        static float keyT0, keyStopAt, keyPressAt;
        static string keyResult = "";
        static Parkside.Input.InputSnapshot keySnap;
        static float keyRevAt = -1f, keyHold1, keyHold2, keyNewW, keyRevKph, keyRevBack, keyFwdKph;
        static bool keySpaceReversed;

        static void KeyPress(Parkside.Vehicle.VehicleController vc, bool accel, bool brake, bool brakeOnly, float dt)
        {
            // the receiver polls at 10 Hz: evaluate every fifth 20 ms tick and hold the snapshot in between
            if (keyTick++ % 5 == 0) keySnap = keySrc.Evaluate(accel, brake, brakeOnly, false, false, 0.1f);
            vc.Simulate(new Parkside.Input.VehicleControlInput(keySnap.steeringDegrees, keySnap.throttle, keySnap.brake), true, dt);
        }

        static void KeyTest(Parkside.Vehicle.VehicleController vc, float local, float dt)
        {
            float t = local - keyT0;
            switch (keyPhase)
            {
                case 0: // standstill, no key
                    KeyPress(vc, false, false, false, dt);
                    if (t > 0.3f) { keyPhase = 1; keyT0 = local; keyPressAt = local; keyRevAt = -1f; }
                    break;
                case 1: // S pressed anew at a standstill: must reverse at once; hold 3 s
                    KeyPress(vc, false, true, true, dt);
                    if (keyRevAt < 0f && vc.IsReversing) keyRevAt = local - keyPressAt;
                    if (t > 3f)
                    {
                        keyRevKph = vc.SpeedKph;
                        keyRevBack = -Vector3.Dot(vc.transform.position - keyStart, keyForward);
                        keyPhase = 2; keyT0 = local; keyStopAt = -1f;
                    }
                    break;
                case 2: // W while reversing: brakes, then after 0.8 s at a standstill the car turns forward
                    KeyPress(vc, true, false, false, dt);
                    if (keyStopAt < 0f && vc.SpeedMps < 0.3f) keyStopAt = local;
                    if (keyStopAt >= 0f && !vc.IsReversing) { keyHold1 = local - keyStopAt; keyPhase = 3; keyT0 = local; }
                    if (t > 12f) keyPhase = 99;
                    break;
                case 3: // W is throttle again going forward
                    KeyPress(vc, true, false, false, dt);
                    if (t > 2.5f) { keyFwdKph = vc.SpeedKph; keyPhase = 4; keyT0 = local; keyStopAt = -1f; }
                    break;
                case 4: // S while rolling forward: brake to a stop, keep holding: reverse after 0.8 s
                    KeyPress(vc, false, true, true, dt);
                    if (keyStopAt < 0f && vc.SpeedMps < 0.3f) keyStopAt = local;
                    if (keyStopAt >= 0f && vc.IsReversing) { keyHold2 = local - keyStopAt; keyPhase = 5; keyT0 = local; }
                    if (t > 12f) keyPhase = 99;
                    break;
                case 5: // keys up, car stops (reversing, standing)
                    KeyPress(vc, false, false, false, dt);
                    if (t > 1.5f) { keyPhase = 6; keyT0 = local; keyPressAt = local; keyNewW = -1f; }
                    break;
                case 6: // W pressed anew while reversing and standing: forward at once
                    KeyPress(vc, true, false, false, dt);
                    if (keyNewW < 0f && !vc.IsReversing) keyNewW = local - keyPressAt;
                    if (t > 1f) { keyPhase = 7; keyT0 = local; }
                    break;
                case 7: // keys up until stopped, forward
                    KeyPress(vc, false, false, false, dt);
                    if (t > 4f) { keyPhase = 8; keyT0 = local; }
                    break;
                case 8: // Space only, 2.5 s: must never reverse
                    KeyPress(vc, false, true, false, dt);
                    if (t > 2.5f)
                    {
                        keySpaceReversed = vc.IsReversing;
                        keyResult = $"immediateReverse={keyRevAt:0.00}s reverseKph={keyRevKph:0.0} movedBack={keyRevBack:0.0}m wBrakesThenForward={keyHold1:0.00}s forwardKph={keyFwdKph:0} sHoldThenReverse={keyHold2:0.00}s newWForward={keyNewW:0.00}s spaceReversed={keySpaceReversed}";
                        keyPhase = 99;
                    }
                    break;
            }
        }

        // ---------- 15 km/h alley entry ----------

        static Vector3[] alleyPath;
        static float alleyLength, alleyHalf, alleyMaxDev, alleyMaxKph, alleyStartedAt;
        static int alleyWay, alleyOffRoad, alleyFrames;
        static Vector3 alleyMouth, alleyDir;
        static bool alleyEntered, alleyDone;

        static void AlleyDrive(Parkside.Vehicle.VehicleController vc, DriveAssist assist, float local, float dt)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (local < 0.05f)
            {
                var file = Path.Combine(Dir, "alleys.txt");
                var best = new List<Vector3>(); int bestWay = 0; float bestHalf = 0f; float bestLen = 0f;
                foreach (var line in File.ReadAllLines(file).Where(l => l.Trim().Length > 0))
                {
                    var f = line.Split(' ');
                    var pts = f.Skip(3).Select(q => { var c = q.Split(','); return new Vector3(float.Parse(c[0], inv), float.Parse(c[1], inv), float.Parse(c[2], inv)); }).ToList();
                    float len = 0f; for (int i = 0; i < pts.Count - 1; i++) len += Vector3.Distance(pts[i], pts[i + 1]);
                    // a gentle first 15 m is needed for a 90 degree entry from the course; prefer the longest alley beyond that
                    if (pts.Count >= 3 && len > bestLen && len < 220f) { best = pts; bestWay = int.Parse(f[0]); bestHalf = float.Parse(f[1], inv); bestLen = len; }
                }
                if (best.Count < 2) { driveLog += "alley[ERROR no alley mouth on the course] "; driveStep = 7; return; }
                alleyWay = bestWay; alleyHalf = bestHalf; alleyLength = bestLen; alleyMouth = best[0];
                alleyDir = best[1] - best[0]; alleyDir.y = 0f; alleyDir.Normalize();
                assist.JumpTo(assist.DistanceNearest(alleyMouth) - 26f);
                var p0 = vc.transform.position; var tan = vc.transform.forward; tan.y = 0f; tan.Normalize();
                // human line: lane -> corner -> alley centre (quadratic bezier through the lane/alley centre lines' crossing)
                float cross = tan.x * alleyDir.z - tan.z * alleyDir.x;
                var m2 = alleyMouth - p0;
                var corner = Mathf.Abs(cross) > 0.2f ? p0 + tan * ((m2.x * alleyDir.z - m2.z * alleyDir.x) / cross) : alleyMouth;
                var inside = new List<Vector3>(); float acc = 0f;
                for (int i = 0; i < best.Count - 1; i++)
                {
                    float seg = Vector3.Distance(best[i], best[i + 1]);
                    if (acc + seg >= 9f) { inside.Add(Vector3.Lerp(best[i], best[i + 1], (9f - acc) / seg)); inside.AddRange(best.Skip(i + 1)); break; }
                    acc += seg;
                }
                var path = new List<Vector3> { p0 };
                var q0 = p0 + tan * Mathf.Max(0f, Vector3.Dot(corner - p0, tan) - 12f);
                for (int k = 0; k <= 16; k++)
                {
                    float u = k / 16f;
                    path.Add((1 - u) * (1 - u) * q0 + 2 * (1 - u) * u * corner + u * u * inside[0]);
                }
                path.AddRange(inside.Skip(1));
                alleyPath = path.ToArray();
                alleyMaxDev = 0f; alleyMaxKph = 0f; alleyOffRoad = 0; alleyFrames = 0; alleyEntered = false; alleyDone = false; alleyStartedAt = local;
                vc.Simulate(new Parkside.Input.VehicleControlInput(0f, 0f, 0f), true, dt);
                return;
            }
            if (alleyDone) return;
            // pure pursuit, 5 m look-ahead, held at 15 km/h
            var pos = vc.transform.position;
            int near = 0; float nearD = float.MaxValue; float along = 0f, nearAlong = 0f;
            for (int i = 0; i < alleyPath.Length - 1; i++)
            {
                var a = alleyPath[i]; var b = alleyPath[i + 1]; var ab = b - a; ab.y = 0f;
                float l2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
                float t = Mathf.Clamp01(Vector3.Dot(new Vector3(pos.x - a.x, 0f, pos.z - a.z), ab) / l2);
                var c = a + (b - a) * t;
                float d = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(c.x, c.z));
                if (d < nearD) { nearD = d; near = i; nearAlong = along + Mathf.Sqrt(l2) * t; }
                along += Mathf.Sqrt(l2);
            }
            float target = nearAlong + 5f, walk = 0f; var aim = alleyPath[alleyPath.Length - 1];
            for (int i = 0; i < alleyPath.Length - 1; i++)
            {
                float seg = Vector3.Distance(alleyPath[i], alleyPath[i + 1]);
                if (walk + seg >= target) { aim = Vector3.Lerp(alleyPath[i], alleyPath[i + 1], (target - walk) / seg); break; }
                walk += seg;
            }
            var toAim = aim - pos; toAim.y = 0f;
            float alpha = Vector3.SignedAngle(vc.transform.forward, toAim, Vector3.up) * Mathf.Deg2Rad;
            float steer = Mathf.Clamp(Mathf.Atan2(2f * 2.7f * Mathf.Sin(alpha), 5f) * Mathf.Rad2Deg, -30f, 30f);
            float throttle = Mathf.Clamp01(0.19f + (4.17f - vc.SpeedMps) * 0.5f);
            bool finalStretch = nearAlong > along - 3f;
            vc.Simulate(new Parkside.Input.VehicleControlInput(steer, finalStretch ? 0f : throttle, finalStretch ? 1f : 0f), true, dt);
            alleyFrames++;
            if (nearAlong > 26f) alleyMaxDev = Mathf.Max(alleyMaxDev, nearD);
            alleyMaxKph = Mathf.Max(alleyMaxKph, vc.SpeedKph);
            if (!OnRoad(pos)) alleyOffRoad++;
            if (Vector3.Dot(new Vector3(pos.x - alleyMouth.x, 0f, pos.z - alleyMouth.z), alleyDir) > 5f) alleyEntered = true;
            float sinceStart = local - alleyStartedAt;
            bool atEnd = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(alleyPath[alleyPath.Length - 1].x, alleyPath[alleyPath.Length - 1].z)) < 4f;
            bool stalled = sinceStart > 6f && vc.SpeedMps < 0.15f && !finalStretch;
            if (atEnd || stalled || sinceStart > 90f)
            {
                alleyDone = true;
                driveLog += $"alley[way={alleyWay} length={alleyLength:0}m halfWidth={alleyHalf:0.0} entered={alleyEntered} reachedEnd={atEnd} progress={nearAlong:0}/{along:0}m maxDevFromLine={alleyMaxDev:0.0}m offRoadFrames={alleyOffRoad}/{alleyFrames} maxKph={alleyMaxKph:0.0} seconds={sinceStart:0}{(stalled ? " STALLED at " + pos.ToString("0") : "")}] ";
                driveStep = 7;
            }
        }

        static string SampleTraffic()
        {
            var traffic = UnityEngine.Object.FindObjectOfType<RouteTraffic>();
            if (traffic == null) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < traffic.transform.childCount; i++)
            {
                var p = traffic.transform.GetChild(i).position;
                sb.Append(p.x.ToString("0.0")).Append(',').Append(p.y.ToString("0.0")).Append(',').Append(p.z.ToString("0.0")).Append(';');
            }
            return sb.ToString();
        }

        static string CompareTraffic(string a, string b)
        {
            var pa = a.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var pb = b.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (pa.Length == 0 || pa.Length != pb.Length) return "cars=" + pb.Length + " nocompare";
            int moved = 0;
            float total = 0f;
            for (int i = 0; i < pa.Length; i++)
            {
                float d = Vector3.Distance(Parse(pa[i]), Parse(pb[i]));
                total += d;
                if (d > 1f) moved++;
            }
            return "cars=" + pa.Length + " moved=" + moved + " meanMetres=" + (total / pa.Length).ToString("0");
        }

        static Vector3 Parse(string s)
        {
            var v = s.Split(',');
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return new Vector3(float.Parse(v[0], c), float.Parse(v[1], c), float.Parse(v[2], c));
        }

        // ---------- offscreen capture ----------

        // Renders through the scene's main camera so post-processing is included, then puts the
        // camera back. Shadows are switched on only for the duration (the quality level ships with
        // shadows off; DriveAssist does the same at runtime).
        static void Capture(string argument)
        {
            var flags = argument.Split(' ');
            string outDir = flags[0];
            var set = JsonUtility.FromJson<ViewSet>(File.ReadAllText(Path.Combine(Dir, "views.json")));
            Directory.CreateDirectory(outDir);
            var cam = Camera.main;
            if (cam == null) throw new InvalidOperationException("No main camera in the open scene");
            var t = cam.transform;
            var savedPos = t.position; var savedRot = t.rotation; var savedFov = cam.fieldOfView;
            var savedTarget = cam.targetTexture; var savedOrtho = cam.orthographic;
            var savedShadows = QualitySettings.shadows; var savedDistance = QualitySettings.shadowDistance;
            int savedCascades = QualitySettings.shadowCascades; var savedResolution = QualitySettings.shadowResolution; var savedProjection = QualitySettings.shadowProjection;
            // debug flags after the directory: noao (Kino AO off), nofx (all post effects off), nofog
            var disabled = new List<Behaviour>();
            bool savedFog = RenderSettings.fog;
            if (flags.Contains("noao") || flags.Contains("nofx"))
                foreach (var b in cam.GetComponents<Behaviour>())
                    if (b.enabled && (flags.Contains("nofx") ? b.GetType().Namespace != "UnityEngine" && !(b is Camera) : b.GetType().Name == "Obscurance")) { b.enabled = false; disabled.Add(b); }
            if (flags.Contains("nofog")) RenderSettings.fog = false;
            // mask: foliage rendered flat magenta (use with nofx nofog) so tools can measure leaf pixels
            var maskMat = flags.Contains("mask") ? AssetDatabase.LoadAssetAtPath<Material>("Assets/BusanEnvironment/GeneratedSlice/mat-tree.mat") : null;
            if (maskMat != null) maskMat.SetFloat("_Mask", 1f);
            try
            {
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowDistance = 300f;
                QualitySettings.shadowCascades = 4;                    // same shadow set-up as DriveAssist applies at runtime
                QualitySettings.shadowResolution = ShadowResolution.High;
                QualitySettings.shadowProjection = ShadowProjection.StableFit;
                cam.orthographic = false;
                foreach (var view in set.views)
                {
                    cam.fieldOfView = view.fov;
                    t.position = new Vector3(view.px, view.py, view.pz);
                    t.LookAt(new Vector3(view.tx, view.ty, view.tz));
                    RenderToPng(cam, Path.Combine(outDir, view.name + ".png"), set.width, set.height);
                }
            }
            finally
            {
                t.SetPositionAndRotation(savedPos, savedRot);
                cam.fieldOfView = savedFov;
                cam.orthographic = savedOrtho;
                cam.targetTexture = savedTarget;
                QualitySettings.shadows = savedShadows;
                QualitySettings.shadowDistance = savedDistance;
                QualitySettings.shadowCascades = savedCascades; QualitySettings.shadowResolution = savedResolution; QualitySettings.shadowProjection = savedProjection;
                foreach (var b in disabled) b.enabled = true;
                RenderSettings.fog = savedFog;
                if (maskMat != null) maskMat.SetFloat("_Mask", 0f);
            }
        }

        static string BuildStats()
        {
            var scene = Path.GetFullPath(Path.Combine(Application.dataPath, "../Assets/_Parkside/Scenes/BusanDrive.unity"));
            long size = File.Exists(scene) ? new FileInfo(scene).Length : -1;
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "BusanEnvironment/GeneratedSlice"));
            int assets = Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Count(f => !f.EndsWith(".meta")) : 0;
            return $"sceneBytes={size} generatedFiles={assets}";
        }

        // Contact sheet of prefabs in an isolated preview scene, so nothing touches the
        // open scene. Reads prefab paths from Temp/BusanBridge/sheet.txt.
        static void PrefabSheet(string outPng)
        {
            var paths = File.ReadAllLines(Path.Combine(Dir, "sheet.txt")).Where(l => l.Trim().Length > 0).ToArray();
            const int cell = 320, columns = 6;
            int rows = Mathf.CeilToInt(paths.Length / (float)columns);
            var sheet = new Texture2D(cell * columns, cell * rows, TextureFormat.RGB24, false);
            var preview = EditorSceneManager.NewPreviewScene();
            var index = new StringBuilder();
            try
            {
                var lightGo = new GameObject("Sun");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, preview);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                lightGo.transform.rotation = Quaternion.Euler(45f, 35f, 0f);
                var camGo = new GameObject("Camera");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, preview);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = preview;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.72f, 0.78f, 0.84f);
                cam.farClipPlane = 5000f;
                var rt = new RenderTexture(cell, cell, 24) { antiAliasing = 4 };
                cam.targetTexture = rt;
                for (int i = 0; i < paths.Length; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i].Trim());
                    if (prefab == null) { index.AppendLine(i + " MISSING " + paths[i]); continue; }
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                    var renderers = instance.GetComponentsInChildren<Renderer>();
                    long tris = instance.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null).Sum(f => (long)f.sharedMesh.triangles.Length / 3);
                    if (renderers.Length > 0)
                    {
                        var bounds = renderers[0].bounds;
                        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                        float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
                        cam.transform.position = bounds.center + new Vector3(0.75f, 0.45f, -0.9f).normalized * radius * 2.7f;
                        cam.transform.LookAt(bounds.center);
                        cam.Render();
                        RenderTexture.active = rt;
                        sheet.ReadPixels(new Rect(0, 0, cell, cell), (i % columns) * cell, (rows - 1 - i / columns) * cell);
                        index.AppendLine($"{i} size={bounds.size.x:0.0}x{bounds.size.y:0.0}x{bounds.size.z:0.0} tris={tris} renderers={renderers.Length} {Path.GetFileNameWithoutExtension(paths[i])}");
                    }
                    else index.AppendLine(i + " NO_RENDERER " + paths[i]);
                    UnityEngine.Object.DestroyImmediate(instance);
                }
                RenderTexture.active = null;
                cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(rt);
                sheet.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(outPng));
                File.WriteAllBytes(outPng, sheet.EncodeToPNG());
                File.WriteAllText(outPng + ".txt", index.ToString());
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                UnityEngine.Object.DestroyImmediate(sheet);
            }
        }

        static void RenderToPng(Camera cam, string path, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24) { antiAliasing = 4 };
            var previousTarget = cam.targetTexture;
            var previousActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            cam.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(image);
        }

        static void Report(string line)
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(ResultPath, $"{DateTime.Now:HH:mm:ss} {line}\n");
            Debug.Log("BUSAN_BRIDGE " + line);
        }
    }
}
