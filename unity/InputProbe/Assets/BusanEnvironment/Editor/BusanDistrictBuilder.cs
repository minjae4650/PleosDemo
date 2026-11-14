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
    // One district of the landmark map as a prefab. The geometry, material settings, cars, views, reflection points and driving
    // surfaces come from tools/build_district.py (district.json + district.bin, outside the repository, default C:\c\busan-out\<name>);
    // the meshes are split into cells there (128 m near, 512 m far), one static child per (material, cell) here, so each cell is
    // culled on its own.
    //   Landmarks/<Name>/Mesh_near.asset, Mesh_far.asset   the meshes, binary MeshPack files under 40 MB each (Mesh_near2... beyond)
    //   Landmarks/<Name>/Colliders.asset                    the driving surfaces (MeshColliders on the road layer, no renderer)
    //   Landmarks/<Name>/Reflection_<probe>.png             the reflection cubemaps (six faces side by side, imported as cubes)
    //   Landmarks/<Name>/<Name>.prefab                      cells, cars, reflection probes, Colliders
    //   Landmarks/Shared/Materials/<key>@<angle>.mat         materials: those laid along the road per district heading, the rest
    //                                                        (`along` false, _Angle 0) as <key>@0, shared by every district
    // Bridge commands: district <name> [dir] -> Build; landmarks views <name> <outDir> -> RenderViews.
    public static class BusanDistrictBuilder
    {
        public const string Root = "Assets/BusanEnvironment/Landmarks";
        public const string MaterialDir = Root + "/Shared/Materials";
        public const int RoadLayer = 30;                     // the only layer VehicleController follows (groundMask = 1 << 30)
        const long FileLimit = 40L * 1024 * 1024;           // GitHub refuses files over 100 MB; we keep each one under 40 MB
        const long PackBudget = 36L * 1024 * 1024;           // what the estimate may reach before the next file is started
        static readonly string[] GeneratedTextureDirs = { "Assets/BusanEnvironment/Blockout", Root };

        [Serializable] class MaterialData
        {
            public string key, layer, kind, tex, nor;
            public float[] tint;
            public float tile, smooth, metal, bump, vary, stain, grime, emit;
            public bool along, shadows, flipg, alpha;
        }
        [Serializable] class CellData { public string mat, layer; public int[] cell; public int nv, nt, v, uv, c, t; }
        [Serializable] class SunData { public float yaw, pitch; }
        // box: the world axis-aligned box the probe serves [cx, cy, cz, sx, sy, sz]; missing = the district's bounds. importance 0 = 1.
        [Serializable] class ProbeData { public string name; public float[] pos; public float[] box; public int importance; }
        [Serializable] class CarData { public string prefab; public float x, y, z, yaw; }
        [Serializable] class ViewData { public string name; public float px, py, pz, tx, ty, tz, fov = 62f; }
        [Serializable] class ColliderData { public string name; public int nv, nt, v, t; }
        [Serializable] class DistrictData
        {
            public string name, bin;
            public float heading;
            public SunData sun;
            public List<MaterialData> materials;
            public List<CellData> cells;
            public List<CarData> cars;
            public List<ViewData> views;
            public List<ProbeData> probes;
            public List<ColliderData> colliders;
        }

        // Outside %LOCALAPPDATA% on purpose: the generator may run inside a packaged (MSIX) app, and Windows redirects what such an
        // app writes there to a folder the editor does not see. The same folder as tools/build_district.py's default.
        public const string OutRoot = @"C:\c\busan-out";

        public static string DefaultDir(string name) => Path.Combine(OutRoot, name);

        // "gwangalli" -> "Gwangalli": the folder, prefab and root object name
        public static string TitleOf(string name) => string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);

        public static string PrefabPath(string name) => $"{Root}/{TitleOf(name)}/{TitleOf(name)}.prefab";

        // ---- standing cars: the source Hyundai models (ReusableVehicles, 30-70 parts each, one draw call per part) are placed as the
        // baked one-mesh car of the same model in the same colour (BusanSliceVehicles, what the moving cars use), set where the
        // source car's body would be (middle and bottom of its box): about 1,600 draw calls fewer with the car park in view
        const string BakedCarDir = "Assets/BusanEnvironment/GeneratedSlice/TrafficCars/";
        static readonly (string source, string baked, string paint)[] BakedFor =
        {
            ("Hyundai_i30_", "i30", null), ("Hyundai_Azera_", "Azera", null), ("Hyundai_Tucson_", "Tucson", null),
            ("Hyundai_Porter_II_", "PorterII", ""), ("Generic_DeliveryTruck", "BoxTruck", "white"),
        };
        static readonly Dictionary<string, string> PaintFor = new Dictionary<string, string>
        {
            { "White", "white" }, { "Silver", "silver" }, { "Blue", "blue" }, { "Graphite", "grey" }, { "Navy", "navy" }, { "Black", "black" }, { "Burgundy", "wine" },
        };

        // the model's box in its own root's space, from its meshes
        static Bounds LocalBox(GameObject prefab)
        {
            bool any = false; var box = new Bounds();
            var toRoot = prefab.transform.worldToLocalMatrix;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || r == null || !r.enabled || mf.name == "BrakeLamps" || mf.name.StartsWith("BodyLod")) continue;
                var m = toRoot * mf.transform.localToWorldMatrix; var b = mf.sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var corner = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)));
                    if (!any) { box = new Bounds(corner, Vector3.zero); any = true; } else box.Encapsulate(corner);
                }
            }
            return box;
        }

        public static GameObject PlaceCar(string prefabPath, Transform parent, Vector3 pos, Quaternion rot, List<string> notes)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (source == null) { notes?.Add("no prefab " + Path.GetFileNameWithoutExtension(prefabPath)); return null; }
            string name = Path.GetFileNameWithoutExtension(prefabPath);
            GameObject baked = null; string paint = null;
            foreach (var (src, b, p) in BakedFor)
                if (name.StartsWith(src))
                {
                    baked = AssetDatabase.LoadAssetAtPath<GameObject>(BakedCarDir + b + ".prefab");
                    paint = p ?? (PaintFor.TryGetValue(name.Substring(src.Length), out var key) ? key : null);
                    break;
                }
            GameObject car;
            if (baked != null && baked.GetComponent<CarModel>() != null)
            {
                var from = LocalBox(source); var to = LocalBox(baked);
                var shift = new Vector3(from.center.x - to.center.x, from.min.y - to.min.y, from.center.z - to.center.z);
                car = (GameObject)PrefabUtility.InstantiatePrefab(baked, parent.gameObject.scene);
                car.transform.SetParent(parent, false);
                car.transform.SetPositionAndRotation(pos + rot * shift, rot);
                if (!string.IsNullOrEmpty(paint))
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/BusanEnvironment/GeneratedSlice/mat-car-" + paint + ".mat");
                    if (mat != null) car.GetComponent<CarModel>().SetPaint(mat);
                }
                car.GetComponent<CarModel>().SetBraking(false);
                car.name = name;
                notes?.Add($"{name}->{baked.name}:{shift.x:0.00},{shift.y:0.00},{shift.z:0.00}");
            }
            else
            {
                car = (GameObject)PrefabUtility.InstantiatePrefab(source, parent.gameObject.scene);
                car.transform.SetParent(parent, false);
                car.transform.SetPositionAndRotation(pos, rot);
            }
            return car;
        }

        // carswap <name>: the standing cars of a built district prefab replaced as PlaceCar places them (nothing else rebuilt)
        public static string SwapCars(string name)
        {
            string path = PrefabPath(name);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return "ERR no " + path;
            var root = PrefabUtility.LoadPrefabContents(path);
            int swapped = 0, kept = 0; var notes = new List<string>();
            try
            {
                foreach (Transform t in root.transform.Cast<Transform>().ToList())
                {
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                    string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                    if (src == null || !src.Contains("/ReusableVehicles/")) { kept++; continue; }
                    var car = PlaceCar(src, root.transform, t.position, t.rotation, notes);
                    if (car == null) { kept++; continue; }
                    car.transform.SetSiblingIndex(t.GetSiblingIndex());
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
                    swapped++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return $"{name}: swapped={swapped} other prefab children={kept} shifts={string.Join(" ", notes.Distinct().Take(12))}";
        }

        static DistrictData Read(string name, string dir, out string jsonPath)
        {
            jsonPath = Path.Combine(string.IsNullOrEmpty(dir) ? DefaultDir(name) : dir, "district.json");
            if (!File.Exists(jsonPath)) return null;
            return JsonUtility.FromJson<DistrictData>(File.ReadAllText(jsonPath));
        }

        static string Missing(string name, string jsonPath) => $"ERR no {jsonPath} (run tools/build_district.py {name} first)";

        static string RefuseIfDirty()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var open = EditorSceneManager.GetSceneAt(i);
                if (open.isDirty) return "REFUSED unsaved changes in " + (string.IsNullOrEmpty(open.path) ? "an untitled scene" : open.path);
            }
            return null;
        }

        // the scenes that were open, put back after the work in a temporary scene
        static string Restore(UnityEditor.SceneManagement.SceneSetup[] setup)
        {
            if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path)))
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
                return string.Join(",", setup.Select(s => s.path));
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return "(untitled)";
        }

        [MenuItem("Busan/Landmarks/Build Gwangalli district")]
        static void BuildMenu() => Debug.Log("DISTRICT " + Build("gwangalli", null));

        public static string Build(string name, string dir)
        {
            var refused = RefuseIfDirty();
            if (refused != null) return refused;
            var data = Read(name, dir, out var jsonPath);
            if (data == null) return Missing(name, jsonPath);
            var ground = Shader.Find("BusanEnvironment/BlockoutGround");
            var surface = Shader.Find("BusanEnvironment/BlockoutSurface");
            var foliage = Shader.Find("BusanEnvironment/FoliageCards");
            if (ground == null || surface == null) return "ERR shaders not compiled yet (run refresh first)";
            string binPath = Path.Combine(Path.GetDirectoryName(jsonPath), string.IsNullOrEmpty(data.bin) ? "district.bin" : data.bin);
            byte[] raw = File.ReadAllBytes(binPath);
            string title = TitleOf(name), dirAsset = $"{Root}/{title}";
            EnsureFolder(dirAsset);
            EnsureFolder(MaterialDir);
            var notes = new List<string>();

            // ---- materials, shared: <key>@<heading in whole degrees> for those laid along the road, <key>@0 for the rest (_Angle 0)
            int angle = Mathf.RoundToInt(data.heading * Mathf.Rad2Deg);
            var materials = new Dictionary<string, Material>();
            var specs = new Dictionary<string, MaterialData>();
            foreach (var m in data.materials)
            {
                specs[m.key] = m;
                string path = $"{MaterialDir}/{m.key}@{(m.along ? angle : 0)}.mat";
                if (!m.along && angle != 0) Retire($"{MaterialDir}/{m.key}@{angle}.mat", path, notes);
                materials[m.key] = SaveMaterial(m, m.kind == "ground" ? ground : m.kind == "foliage" ? foliage : surface, data.heading, path, notes);
            }

            // ---- meshes, one per cell, into binary MeshPack files per layer; the driving surfaces into Colliders.asset
            foreach (var old in Directory.GetFiles(dirAsset, "Mesh_*.asset")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
            string colliderPath = $"{dirAsset}/Colliders.asset";
            AssetDatabase.DeleteAsset(colliderPath);
            var meshes = new List<Mesh>(data.cells.Count);
            var triangles = new Dictionary<string, long>();
            var vertices = new Dictionary<string, long>();
            foreach (var c in data.cells)
            {
                meshes.Add(MakeMesh(c, raw));
                triangles[c.layer] = (triangles.TryGetValue(c.layer, out var t) ? t : 0) + c.nt;
                vertices[c.layer] = (vertices.TryGetValue(c.layer, out var v) ? v : 0) + c.nv;
            }
            var files = new List<string>();
            foreach (var layer in data.cells.Select(c => c.layer).Distinct())
            {
                MeshPack pack = null;
                var inPack = new List<Mesh>();
                long estimate = 0;
                int part = 1;
                for (int i = 0; i < data.cells.Count; i++)
                {
                    if (data.cells[i].layer != layer) continue;
                    long size = Estimate(meshes[i], data.cells[i]);
                    if (pack != null && estimate + size > PackBudget)
                    {
                        pack.meshes = inPack.ToArray(); EditorUtility.SetDirty(pack);
                        pack = null; inPack.Clear(); estimate = 0; part++;
                    }
                    if (pack == null)
                    {
                        pack = ScriptableObject.CreateInstance<MeshPack>();
                        string path = $"{dirAsset}/Mesh_{layer}{(part > 1 ? part.ToString() : "")}.asset";
                        AssetDatabase.CreateAsset(pack, path);
                        files.Add(path);
                    }
                    AssetDatabase.AddObjectToAsset(meshes[i], pack);
                    inPack.Add(meshes[i]);
                    estimate += size;
                }
                if (pack != null) { pack.meshes = inPack.ToArray(); EditorUtility.SetDirty(pack); }
            }
            var colliderMeshes = new List<Mesh>();
            if (data.colliders != null && data.colliders.Count > 0)
            {
                var pack = ScriptableObject.CreateInstance<MeshPack>();
                AssetDatabase.CreateAsset(pack, colliderPath);
                files.Add(colliderPath);
                foreach (var c in data.colliders)
                {
                    var mesh = MakeCollider(c, raw);
                    AssetDatabase.AddObjectToAsset(mesh, pack);
                    colliderMeshes.Add(mesh);
                }
                pack.meshes = colliderMeshes.ToArray();
                EditorUtility.SetDirty(pack);
            }
            AssetDatabase.SaveAssets();
            var sizes = new List<string>();
            foreach (var f in files)
            {
                var full = Path.GetFullPath(f);
                long length = new FileInfo(full).Length;
                var head = new byte[5];
                using (var s = File.OpenRead(full)) s.Read(head, 0, 5);
                bool text = Encoding.ASCII.GetString(head) == "%YAML";
                sizes.Add($"{Path.GetFileName(f)} {length / 1048576.0:0.0}MB {(text ? "TEXT" : "binary")}");
                if (text) return $"ERR {f} was saved as text, not binary ({length / 1048576.0:0.0} MB); files: {string.Join(", ", sizes)}. Not building the prefab.";
                if (length >= FileLimit) return $"ERR {f} is {length / 1048576.0:0.0} MB, over 40 MB; files: {string.Join(", ", sizes)}. Not building the prefab.";
            }

            // ---- the prefab: built in a temporary scene with the look applied, so the reflection can be rendered with it
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BusanLandmarkLook.Apply(data.sun.yaw, data.sun.pitch);
            var root = new GameObject(title);
            for (int i = 0; i < data.cells.Count; i++)
            {
                var c = data.cells[i];
                var go = new GameObject(meshes[i].name) { isStatic = true };
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[c.mat];
                renderer.shadowCastingMode = specs[c.mat].shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            int cars = 0;
            if (data.cars != null)
                foreach (var c in data.cars)
                {
                    var car = PlaceCar(c.prefab, root.transform, new Vector3(c.x, c.y, c.z), Quaternion.Euler(0f, c.yaw, 0f), notes);
                    if (car != null) cars++;
                }
            // the driving surfaces: what the player car follows (VehicleController casts down on the road layer only)
            if (colliderMeshes.Count > 0)
            {
                var holder = new GameObject("Colliders") { isStatic = true, layer = RoadLayer };
                holder.transform.SetParent(root.transform, false);
                foreach (var mesh in colliderMeshes)
                {
                    var go = new GameObject(mesh.name) { isStatic = true, layer = RoadLayer };
                    go.transform.SetParent(holder.transform, false);
                    var mc = go.AddComponent<MeshCollider>();
                    mc.convex = false;
                    mc.isTrigger = false;
                    mc.sharedMesh = mesh;
                }
            }

            // everything the district draws: the box every probe covers
            var bounds = new Bounds();
            bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            bounds.Expand(80f);                                    // 40 m all round, twice the blend distance
            var cubes = new List<Cubemap>();
            using (BusanLandmarkLook.Shadows())
            {
                // all captures first, before any probe exists, so each sees the district as the old builder's capture did
                if (data.probes != null)
                    foreach (var p in data.probes)
                        cubes.Add(BusanLandmarkLook.CaptureReflection(new Vector3(p.pos[0], p.pos[1], p.pos[2]), $"{dirAsset}/Reflection_{p.name}.png", notes));
            }
            int probes = 0;
            var boxes = new List<string>();
            if (data.probes != null)
                for (int i = 0; i < data.probes.Count; i++)
                {
                    var p = data.probes[i];
                    var go = new GameObject("Probe_" + p.name);
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = new Vector3(p.pos[0], p.pos[1], p.pos[2]);
                    var probe = go.AddComponent<ReflectionProbe>();
                    probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
                    probe.customBakedTexture = cubes[i];
                    probe.boxProjection = false;
                    probe.blendDistance = 20f;
                    probe.importance = p.importance > 0 ? p.importance : 1;
                    probe.intensity = 1f;
                    // the box the district gives for this probe (world axes), else everything the district draws
                    bool hasBox = p.box != null && p.box.Length == 6;
                    probe.center = (hasBox ? new Vector3(p.box[0], p.box[1], p.box[2]) : bounds.center) - go.transform.position;
                    probe.size = hasBox ? new Vector3(p.box[3], p.box[4], p.box[5]) : bounds.size;
                    if (cubes[i] != null) probes++;
                    if (hasBox) boxes.Add($"{p.name} {probe.size.x:0}x{probe.size.y:0}x{probe.size.z:0}@{probe.importance}");
                }

            string prefabPath = PrefabPath(name);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
            UnityEngine.Object.DestroyImmediate(root);
            string reopened = Restore(setup);
            if (!saved) return "ERR prefab not saved: " + prefabPath;

            long prefabBytes = new FileInfo(Path.GetFullPath(prefabPath)).Length;
            string layers = string.Join(" ", triangles.Keys.Select(k => $"{k}[cells={data.cells.Count(c => c.layer == k)} tris={triangles[k]} verts={vertices[k]}]"));
            string colliders = string.Join(" ", (data.colliders ?? new List<ColliderData>()).Select(c => $"{c.name}:{c.nt}"));
            return $"{title} cells={data.cells.Count} {layers} materials={data.materials.Count}@{angle} cars={cars} probes={probes}/{data.probes?.Count ?? 0} " +
                   $"probeBoxes=[{string.Join(", ", boxes)}] colliders=[{colliders}] " +
                   $"box={bounds.size.x:0}x{bounds.size.y:0}x{bounds.size.z:0} files=[{string.Join(", ", sizes)}, {Path.GetFileName(prefabPath)} {prefabBytes / 1048576.0:0.0}MB] " +
                   $"notes=[{string.Join("; ", notes)}] reopened={reopened}";
        }

        // Every view of the district's json, rendered from the prefab in a temporary scene with the look applied, to <outDir>\<view>.png
        // at 1280x720 (HDR, MSAA 4, post effects, shadows on). `dir`: where district.json is (default as for Build).
        public static string RenderViews(string name, string outDir, string dir = null)
        {
            var refused = RefuseIfDirty();
            if (refused != null) return refused;
            var data = Read(name, dir, out var jsonPath);
            if (data == null) return Missing(name, jsonPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name));
            if (prefab == null) return "ERR no " + PrefabPath(name) + " (run district " + name + " first)";
            var notes = new List<string>();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BusanLandmarkLook.Apply(data.sun.yaw, data.sun.pitch);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            notes.Add(ProbeCheck(instance));
            var cam = BusanLandmarkLook.MainCamera(notes);
            Directory.CreateDirectory(outDir);
            int rendered = 0;
            using (BusanLandmarkLook.Shadows())
            {
                try
                {
                    foreach (var view in data.views)
                    {
                        cam.transform.position = new Vector3(view.px, view.py, view.pz);
                        cam.transform.LookAt(new Vector3(view.tx, view.ty, view.tz));
                        cam.fieldOfView = view.fov;
                        BusanLandmarkLook.Render(cam, Path.Combine(outDir, view.name + ".png"), 1280, 720);
                        rendered++;
                    }
                }
                catch (Exception e) { notes.Add("views " + e.GetType().Name + " " + e.Message); }
            }
            string reopened = Restore(setup);
            return $"{TitleOf(name)} views={rendered}/{data.views.Count} out={outDir} notes=[{string.Join("; ", notes)}] reopened={reopened}";
        }

        // How many of the district's renderers a reflection probe reaches, and whether every probe has its cubemap: a renderer left
        // to the default reflection (the sky, black in an unbaked editor scene) shows black glass and a black sea.
        static string ProbeCheck(GameObject district)
        {
            var blend = new List<UnityEngine.Rendering.ReflectionProbeBlendInfo>();
            int with = 0, total = 0;
            foreach (var r in district.GetComponentsInChildren<Renderer>())
            {
                total++;
                r.GetClosestReflectionProbes(blend);
                if (blend.Any(b => b.probe != null && b.probe.texture != null && b.weight > 0.99f)) with++;
            }
            var probes = district.GetComponentsInChildren<ReflectionProbe>();
            return $"renderers fully in a probe {with}/{total}, probes {string.Join(" ", probes.Select(p => p.name + "=" + (p.texture != null ? p.texture.name : "NO CUBEMAP")))}";
        }

        static Mesh MakeMesh(CellData c, byte[] raw)
        {
            int n = c.nv;
            var mesh = new Mesh { name = $"{c.mat}_{c.cell[0]}_{c.cell[1]}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var f = new float[n * 3];
            Buffer.BlockCopy(raw, c.v, f, 0, n * 12);
            var verts = new Vector3[n];
            for (int i = 0; i < n; i++) verts[i] = new Vector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2]);
            mesh.vertices = verts;
            bool hasUv = c.uv >= 0;
            if (hasUv)
            {
                var u = new float[n * 2];
                Buffer.BlockCopy(raw, c.uv, u, 0, n * 8);
                var uv = new Vector2[n];
                for (int i = 0; i < n; i++) uv[i] = new Vector2(u[i * 2], u[i * 2 + 1]);
                mesh.uv = uv;
            }
            if (c.c >= 0)
            {
                Buffer.BlockCopy(raw, c.c, f, 0, n * 12);
                var colors = new Color[n];
                for (int i = 0; i < n; i++) colors[i] = new Color(f[i * 3], f[i * 3 + 1], f[i * 3 + 2], 1f);
                mesh.colors = colors;
            }
            var tris = new int[c.nt * 3];
            Buffer.BlockCopy(raw, c.t, tris, 0, c.nt * 12);
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            if (hasUv) mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A driving surface: positions and triangles only (a MeshCollider uses nothing else).
        static Mesh MakeCollider(ColliderData c, byte[] raw)
        {
            var mesh = new Mesh { name = c.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var f = new float[c.nv * 3];
            Buffer.BlockCopy(raw, c.v, f, 0, c.nv * 12);
            var verts = new Vector3[c.nv];
            for (int i = 0; i < c.nv; i++) verts[i] = new Vector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2]);
            mesh.vertices = verts;
            var tris = new int[c.nt * 3];
            Buffer.BlockCopy(raw, c.t, tris, 0, c.nt * 12);
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        // A material that used to be named after the district heading although it is not laid along the road: renamed to its
        // shared name (the GUID, and so every reference to it, stays), or deleted when another district already made the shared one.
        static void Retire(string oldPath, string newPath, List<string> notes)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(oldPath) == null) return;
            if (AssetDatabase.LoadAssetAtPath<Material>(newPath) == null)
            {
                var error = AssetDatabase.MoveAsset(oldPath, newPath);
                notes.Add(string.IsNullOrEmpty(error) ? $"renamed {Path.GetFileName(oldPath)} to {Path.GetFileName(newPath)}" : $"rename {Path.GetFileName(oldPath)} failed: {error}");
            }
            else
            {
                AssetDatabase.DeleteAsset(oldPath);
                notes.Add($"deleted {Path.GetFileName(oldPath)} (shared {Path.GetFileName(newPath)} exists)");
            }
        }

        // bytes the mesh will take in a binary file: position and normal, tangent, colour (4 floats) and uv when it has them, indices
        static long Estimate(Mesh mesh, CellData c) =>
            (long)c.nv * (24 + (c.uv >= 0 ? 24 : 0) + (c.c >= 0 ? 16 : 0)) + (long)c.nt * 12 + 1024;

        // The same shader properties as BusanGwangalliBlockout sets. An existing material is overwritten in place (it keeps its GUID).
        static Material SaveMaterial(MaterialData m, Shader shader, float heading, string path, List<string> notes)
        {
            var mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            var tint = new Color(m.tint[0], m.tint[1], m.tint[2], 1f);
            var tex = Load(m.tex, false, notes);
            var nor = Load(m.nor, true, notes);
            if (tex != null) mat.SetTexture("_MainTex", tex);
            if (nor != null) mat.SetTexture("_BumpMap", nor);
            mat.SetFloat("_BumpScale", nor != null ? m.bump : 0f);
            mat.SetFloat("_Smooth", m.smooth);
            if (m.kind == "foliage")
            {
                mat.SetColor("_Color", tint);
                mat.SetFloat("_Cutoff", 0.45f);
            }
            else if (m.kind == "ground")
            {
                mat.SetColor("_Tint", tint);
                mat.SetFloat("_Tile", m.tile);
                mat.SetFloat("_Angle", m.along ? heading : 0f);
                mat.SetFloat("_Vary", m.vary);
                mat.SetFloat("_Stain", m.stain);
            }
            else
            {
                mat.SetColor("_Color", tint);
                mat.SetFloat("_Metal", m.metal);
                mat.SetFloat("_FlipG", m.flipg ? 1f : 0f);
                mat.SetFloat("_SmoothFromAlpha", m.alpha ? 1f : 0f);
                mat.SetFloat("_Grime", m.grime);
                mat.SetFloat("_Emit", m.emit);
            }
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mat, path); return mat; }
            EditorUtility.CopySerialized(mat, existing);
            existing.name = mat.name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mat);
            return existing;
        }

        static Texture2D Load(string path, bool normal, List<string> notes)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (GeneratedTextureDirs.Any(d => path.StartsWith(d)))      // textures made by the generator: import settings are ours to set
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    var want = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    if (importer.textureType != want || importer.anisoLevel != 8 || importer.maxTextureSize != 2048)
                    {
                        importer.textureType = want;
                        importer.anisoLevel = 8;
                        importer.maxTextureSize = 2048;
                        importer.SaveAndReimport();
                    }
                }
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) notes.Add("no texture " + Path.GetFileName(path));
            return tex;
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
