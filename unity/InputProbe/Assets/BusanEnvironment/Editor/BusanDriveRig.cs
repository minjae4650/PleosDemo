using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // The player's side of a drive scene made from Assets/_Parkside/Scenes/ParkingPractice.unity, set up as the BusanDrive scene
    // has it: the game camera (far 6500, fov 62, post effects), the HUD that hides itself, the drive keyboard, the vehicle's drive
    // settings (22 m/s, ground follow on the road layer), the i30 body with the driver-view bonnet, the spawn point, the play
    // bounds and DriveAssist. Copied from BusanSliceBuilder.TweakScene and DressPlayerCar (which stay as they are, for BusanDrive);
    // the bonnet mesh and material are written to Landmarks/Shared/PlayerCar, so nothing under GeneratedSlice changes.
    // The light and sky are not set here: the landmark scene takes them from BusanLandmarkLook.
    public static class BusanDriveRig
    {
        public const string AssetDir = BusanDistrictBuilder.Root + "/Shared/PlayerCar";
        const string CarPrefab = "Assets/ParkingLotEnvironment/CloudPilot/ParkingContext20261105/ReusableVehicles/Hyundai_i30_Blue.prefab";
        const float HoodZ0 = 0.85f;   // windscreen base in rig space (the i30 body spans z -2.1..2.1)

        // What the scene's drive set-up refers to.
        public sealed class Rig
        {
            public Parkside.Core.GameManager gameManager;
            public Parkside.Vehicle.VehicleController vehicle;
            public Parkside.Cameras.VehicleCamera vehicleCamera;
            public DriveKeyboardSource keys;
            public readonly List<Behaviour> postEffects = new List<Behaviour>();
        }

        // Camera, HUD, keyboard, vehicle and its body, in TweakScene's order. Notes collect what BusanSliceBuilder logs.
        public static Rig Apply(List<string> notes)
        {
            var rig = new Rig();
            var cameraGo = GameObject.Find("MainCamera") ?? GameObject.FindWithTag("MainCamera");
            if (cameraGo != null)
            {
                var cam = cameraGo.GetComponent<Camera>();
                cam.farClipPlane = 6500f;
                cam.nearClipPlane = 0.3f;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.allowHDR = true;
                cam.fieldOfView = 62f;
                rig.postEffects.AddRange(AddPostEffects(cameraGo));
            }
            else notes.Add("no MainCamera");

            rig.gameManager = UnityEngine.Object.FindObjectOfType<Parkside.Core.GameManager>();
            rig.vehicle = UnityEngine.Object.FindObjectOfType<Parkside.Vehicle.VehicleController>();
            rig.vehicleCamera = UnityEngine.Object.FindObjectOfType<Parkside.Cameras.VehicleCamera>();
            var gm = rig.gameManager;
            var vc = rig.vehicle;

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

            // Keyboard: the drive map uses W/S/Space with reverse on S. The parking scenes keep KeyboardInputSource (validator contract).
            var receiver = UnityEngine.Object.FindObjectOfType<Parkside.Input.VehicleInputReceiver>();
            if (receiver != null)
            {
                var oldKeyboard = receiver.DesktopSource != null ? receiver.DesktopSource.gameObject : receiver.gameObject;
                rig.keys = Ensure<DriveKeyboardSource>(oldKeyboard);
                var kso = new SerializedObject(rig.keys);
                kso.FindProperty("vehicle").objectReferenceValue = vc;
                kso.FindProperty("gameManager").objectReferenceValue = gm;
                kso.ApplyModifiedPropertiesWithoutUndo();
                var rso = new SerializedObject(receiver);
                rso.FindProperty("desktopSource").objectReferenceValue = rig.keys;
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
                so.FindProperty("groundMask").intValue = 1 << BusanDistrictBuilder.RoadLayer;
                so.ApplyModifiedPropertiesWithoutUndo();
                DressPlayerCar(vc, rig.vehicleCamera, notes);
            }
            else notes.Add("no VehicleController");
            return rig;
        }

        // PracticeSpawn: where GameManager puts the car on start and on reset.
        public static bool PlaceSpawn(Vector3 position, Quaternion rotation)
        {
            var spawn = GameObject.Find("PracticeSpawn");
            if (spawn == null) return false;
            spawn.transform.SetPositionAndRotation(position, rotation);
            return true;
        }

        // GameManager resets the practice outside `practice`; DriveAssist returns the car to the course outside `drive` first.
        public static void SetPracticeBounds(Rig rig, Bounds practice)
        {
            if (rig.gameManager == null) return;
            var so = new SerializedObject(rig.gameManager);
            so.FindProperty("practiceBounds").boundsValue = practice;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Drive assist: starts on Play, auto-drive, jumps, R to return, runtime shadows. One open course: path, lane offsets (metres
        // to the right of the path) and auto-drive speeds (m/s) per point. `traffic` may be null (no traffic).
        // Several routes instead (`routes`, plan.md 2.3): pass empty path, lanes and speeds; `jumpRoutes` gives each jump's route and
        // `preferredRoute` the one auto-drive takes at a fork.
        public static DriveAssist AddDriveAssist(Rig rig, Transform parent, Vector3[] path, float[] laneOffsets, float[] autoSpeeds, Bounds drive,
                                                 IList<string> jumpNames, IList<float> jumpDistances, RouteTraffic traffic,
                                                 IList<CourseRoute> routes = null, int preferredRoute = 0, IList<int> jumpRoutes = null)
        {
            var assistGo = new GameObject("DriveAssist");
            if (parent != null) assistGo.transform.SetParent(parent, false);
            var assist = assistGo.AddComponent<DriveAssist>();
            var aso = new SerializedObject(assist);
            aso.FindProperty("gameManager").objectReferenceValue = rig.gameManager;
            aso.FindProperty("vehicle").objectReferenceValue = rig.vehicle;
            aso.FindProperty("vehicleCamera").objectReferenceValue = rig.vehicleCamera;
            var pathProp = aso.FindProperty("path");
            pathProp.arraySize = path.Length;
            for (int i = 0; i < path.Length; i++) pathProp.GetArrayElementAtIndex(i).vector3Value = path[i];
            SetFloatArray(aso.FindProperty("laneOffsets"), laneOffsets);
            aso.FindProperty("driveBounds").boundsValue = drive;
            var jn = aso.FindProperty("jumpNames");
            var jd = aso.FindProperty("jumpDistances");
            jn.arraySize = jd.arraySize = jumpNames.Count;
            for (int i = 0; i < jumpNames.Count; i++)
            {
                jn.GetArrayElementAtIndex(i).stringValue = jumpNames[i];
                jd.GetArrayElementAtIndex(i).floatValue = jumpDistances[i];
            }
            var pe = aso.FindProperty("postEffects");
            pe.arraySize = rig.postEffects.Count;
            for (int i = 0; i < rig.postEffects.Count; i++) pe.GetArrayElementAtIndex(i).objectReferenceValue = rig.postEffects[i];
            aso.FindProperty("traffic").objectReferenceValue = traffic;
            SetFloatArray(aso.FindProperty("autoSpeeds"), autoSpeeds);
            var rp = aso.FindProperty("routes");
            rp.arraySize = routes != null ? routes.Count : 0;
            for (int k = 0; k < rp.arraySize; k++)
            {
                var r = routes[k];
                var e = rp.GetArrayElementAtIndex(k);
                e.FindPropertyRelative("name").stringValue = r.name;
                e.FindPropertyRelative("closed").boolValue = r.closed;
                var ep = e.FindPropertyRelative("path");
                ep.arraySize = r.path.Length;
                for (int i = 0; i < r.path.Length; i++) ep.GetArrayElementAtIndex(i).vector3Value = r.path[i];
                SetFloatArray(e.FindPropertyRelative("laneOffsets"), r.laneOffsets);
                SetFloatArray(e.FindPropertyRelative("autoSpeeds"), r.autoSpeeds);
            }
            aso.FindProperty("preferredRoute").intValue = preferredRoute;
            var jr = aso.FindProperty("jumpRoutes");
            jr.arraySize = jumpRoutes != null ? jumpRoutes.Count : 0;
            for (int i = 0; i < jr.arraySize; i++) jr.GetArrayElementAtIndex(i).intValue = jumpRoutes[i];
            aso.ApplyModifiedPropertiesWithoutUndo();
            if (rig.keys != null)
            {
                var kso = new SerializedObject(rig.keys);
                kso.FindProperty("assist").objectReferenceValue = assist;
                kso.ApplyModifiedPropertiesWithoutUndo();
            }
            return assist;
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

        // The game camera's post effects with BusanDrive's settings (BusanSliceBuilder.AddPostEffects; BusanLandmarkLook.AddPostEffects
        // has the same values but always adds, and the scene's camera may already carry some of them).
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

        // darker, less saturated copy of the model's paint: the original blue reads electric under the sun and the sky reflection
        static Material BonnetMaterial()
        {
            var path = AssetDir + "/mat-bonnet.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
            m.color = new Color(0.035f, 0.075f, 0.16f);
            m.SetFloat("_Glossiness", 0.78f);
            m.SetFloat("_Metallic", 0.35f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // The cube body stays (colliders/anchors depend on the rig) but is hidden; a real car prefab is parented under it. Wheels keep
        // turning logic but are hidden too. In the driver view (DriverViewBody) the body is hidden and the bonnet shown.
        static void DressPlayerCar(Parkside.Vehicle.VehicleController vc, Parkside.Cameras.VehicleCamera vehicleCamera, List<string> notes)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarPrefab);
            if (prefab == null) { notes.Add("no car prefab " + CarPrefab); return; }
            EnsureFolder(AssetDir);
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
            notes.Add("driverView mode=" + driverMode);
            var modelRenderers = driverMode == "hood" ? model.GetComponentsInChildren<Renderer>() : new Renderer[0];
            var driverOnly = new List<Renderer>();
            if (driverMode == "hood")
            {
                // Real bonnet: the upward-facing paint triangles of the i30 in front of the windscreen base (rig space z >= 0.85), with the
                // model's own normals and paint material, so it catches the sky reflection. A dark cowl strip sits at the windscreen base.
                // The eye is moved back into the cabin position, so the bonnet is a band at the bottom of the view instead of a slab.
                var driver = vc.transform.Find("CameraAnchors/Driver");
                if (driver != null) driver.localPosition = new Vector3(-0.37f, 1.38f, 0.36f);
                if (driver != null) driver.localRotation = Quaternion.Euler(-1.6f, 0f, 0f);   // small pitch up: horizon a little lower, bonnet share smaller
                var hv = new List<Vector3>(); var hn = new List<Vector3>(); var hu = new List<Vector2>(); var ht = new List<int>();
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
                        int i0 = hv.Count;
                        foreach (int vi in order)
                        {
                            hv.Add(toRig.MultiplyPoint3x4(vs[vi])); hu.Add(Vector2.zero);
                            hn.Add(nm.Length == vs.Length ? toRig.MultiplyVector(nm[vi]).normalized : Vector3.up);
                        }
                        ht.Add(i0); ht.Add(i0 + 1); ht.Add(i0 + 2);
                        hoodTris++;
                    }
                }
                notes.Add($"bonnet real i30 bonnet triangles={hoodTris} eye={(driver != null ? driver.localPosition.ToString("0.00") : "n/a")}");
                var hoodMesh = Bake("Bonnet", hv, hu, hn, ht);

                // dark cowl panel from the windscreen base forward, slightly under the bonnet, so the shut line between them shows plastic, not the road
                float cx = 0.62f;
                var cv = new List<Vector3> { new Vector3(-cx, 1.03f, HoodZ0 - 0.18f), new Vector3(cx, 1.03f, HoodZ0 - 0.18f), new Vector3(cx, 0.915f, HoodZ0 + 0.30f), new Vector3(-cx, 0.915f, HoodZ0 + 0.30f) };
                var cu = new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                var cn = Vector3.Cross(cv[1] - cv[0], cv[2] - cv[0]) + Vector3.Cross(cv[2] - cv[0], cv[3] - cv[0]);
                var ct = Vector3.Dot(cn, Vector3.up) >= 0f ? new List<int> { 0, 1, 2, 0, 2, 3 } : new List<int> { 0, 2, 1, 0, 3, 2 };
                var cowlMesh = Bake("Cowl", cv, cu, null, ct);

                // both meshes in one binary file (the project serialises assets as text otherwise)
                var packPath = AssetDir + "/PlayerCar.asset";
                AssetDatabase.DeleteAsset(packPath);
                var pack = ScriptableObject.CreateInstance<MeshPack>();
                AssetDatabase.CreateAsset(pack, packPath);
                AssetDatabase.AddObjectToAsset(hoodMesh, pack);
                AssetDatabase.AddObjectToAsset(cowlMesh, pack);
                pack.meshes = new[] { hoodMesh, cowlMesh };
                EditorUtility.SetDirty(pack);
                AssetDatabase.SaveAssets();

                var bonnet = new GameObject("Bonnet");
                bonnet.transform.SetParent(vc.transform, false);
                bonnet.AddComponent<MeshFilter>().sharedMesh = hoodMesh;
                var br = bonnet.AddComponent<MeshRenderer>();
                br.sharedMaterial = BonnetMaterial(); br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; br.enabled = false;
                driverOnly.Add(br);

                var cowlGo = new GameObject("Cowl");
                cowlGo.transform.SetParent(vc.transform, false);
                cowlGo.AddComponent<MeshFilter>().sharedMesh = cowlMesh;
                var cr2 = cowlGo.AddComponent<MeshRenderer>();
                // same paint as the bonnet: the panel between bonnet and windscreen is body colour, not a black plate. As in BusanDrive the
                // cowl keeps casting shadows and starts enabled (DressPlayerCar's line that would change both sits inside a comment there).
                cr2.sharedMaterial = BonnetMaterial();
                driverOnly.Add(cr2);
            }
            var bodyGo = new GameObject("DriverViewBody");
            bodyGo.transform.SetParent(vc.transform, false);
            var body = bodyGo.AddComponent<DriverViewBody>();
            var bso = new SerializedObject(body);
            bso.FindProperty("vehicleCamera").objectReferenceValue = vehicleCamera;
            var dp = bso.FindProperty("driverOnly"); dp.arraySize = driverOnly.Count;
            for (int i = 0; i < driverOnly.Count; i++) dp.GetArrayElementAtIndex(i).objectReferenceValue = driverOnly[i];
            var hp = bso.FindProperty("hiddenInDriver"); hp.arraySize = modelRenderers.Length;
            for (int i = 0; i < modelRenderers.Length; i++) hp.GetArrayElementAtIndex(i).objectReferenceValue = modelRenderers[i];
            bso.ApplyModifiedPropertiesWithoutUndo();
            var chase = vc.transform.Find("CameraAnchors/Chase");
            if (chase != null) { chase.localPosition = new Vector3(0f, 2.6f, -6.5f); chase.localRotation = Quaternion.Euler(8f, 0f, 0f); }
        }

        // As BusanSliceBuilder's MeshBuilder.Bake: the given normals when there is one per vertex, else recalculated.
        static Mesh Bake(string name, List<Vector3> v, List<Vector2> uv, List<Vector3> n, List<int> t)
        {
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            if (n != null && n.Count == v.Count && n.Count > 0) m.SetNormals(n); else m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
