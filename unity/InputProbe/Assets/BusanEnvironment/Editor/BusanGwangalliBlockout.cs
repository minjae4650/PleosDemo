using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // 500 m of 광안해변로 in its own scene. Geometry, material settings, car placements and camera views come ready-made from
    // tools/build-gwangalli-blockout.py (GwangalliBlockout.json); this class turns them into meshes and materials, sets the
    // light, sky and the game camera's post effects, renders the views and puts the editor back on the scene that was open.
    // Bridge command: gwangalli <outDir>. Menu: Busan/Build Gwangalli scene.
    public static class BusanGwangalliBlockout
    {
        const string DataPath = "Assets/BusanEnvironment/Editor/Data/GwangalliBlockout.json";
        const string SceneDir = "Assets/BusanEnvironment/Blockout";
        const string ScenePath = SceneDir + "/GwangalliBlockout.unity";

        [Serializable] class MeshData
        {
            public string name, kind, tex, nor;
            public float[] tint, v, uv, c;
            public int[] t;
            public float tile, smooth, metal, bump, vary, stain, grime, emit;
            public bool along, shadows, flipg, alpha;
        }
        [Serializable] class CarData { public string prefab; public float x, y, z, yaw; }
        [Serializable] class ViewData { public string name; public float px, py, pz, tx, ty, tz, fov = 62f; }
        [Serializable] class SceneData
        {
            public float heading, sunYaw, sunPitch;
            public float[] probe;
            public List<MeshData> meshes;
            public List<CarData> cars;
            public List<ViewData> views;
        }

        [MenuItem("Busan/Build Gwangalli scene")]
        static void BuildMenu() => Debug.Log("GWANGALLI " + Build(Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/Gwangalli"))));

        public static string Build(string outDir)
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var open = EditorSceneManager.GetSceneAt(i);
                if (open.isDirty) return "REFUSED unsaved changes in " + open.path;
            }
            string previous = EditorSceneManager.GetActiveScene().path;
            var data = JsonUtility.FromJson<SceneData>(File.ReadAllText(DataPath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var notes = new List<string>();

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.45f;
            // from the south-south-east, over the sea: the beachfront facades are lit and the lamps and bollards on the sea side
            // throw their shadows across the road
            sun.transform.rotation = Quaternion.Euler(data.sunPitch, data.sunYaw, 0f);
            RenderSettings.sun = sun;
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/ParkingLotEnvironment/CloudPilot/Materials/Daylight_Sky.mat");
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.54f, 0.54f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.28f, 0.26f);
            RenderSettings.ambientIntensity = 1.25f;
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.74f, 0.80f, 0.86f);
            RenderSettings.fogDensity = 0.00030f;      // sea haze: the bridge 1.2 km out is clear, the far shore soft

            var root = new GameObject("Gwangalli");
            var ground = Shader.Find("BusanEnvironment/BlockoutGround");
            var surface = Shader.Find("BusanEnvironment/BlockoutSurface");
            if (ground == null || surface == null) return "ERR shaders not compiled yet (run refresh first)";
            int triangles = 0;
            foreach (var m in data.meshes)
            {
                int n = m.v.Length / 3;
                var mesh = new Mesh { name = m.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                var verts = new Vector3[n];
                for (int i = 0; i < n; i++) verts[i] = new Vector3(m.v[i * 3], m.v[i * 3 + 1], m.v[i * 3 + 2]);
                mesh.vertices = verts;
                bool hasUv = m.uv != null && m.uv.Length == n * 2;
                if (hasUv)
                {
                    var uv = new Vector2[n];
                    for (int i = 0; i < n; i++) uv[i] = new Vector2(m.uv[i * 2], m.uv[i * 2 + 1]);
                    mesh.uv = uv;
                }
                if (m.c != null && m.c.Length == n * 3)
                {
                    var colors = new Color[n];
                    for (int i = 0; i < n; i++) colors[i] = new Color(m.c[i * 3], m.c[i * 3 + 1], m.c[i * 3 + 2], 1f);
                    mesh.colors = colors;
                }
                mesh.triangles = m.t;
                mesh.RecalculateNormals();
                if (hasUv) mesh.RecalculateTangents();
                mesh.RecalculateBounds();

                var mat = new Material(m.kind == "ground" ? ground : surface) { name = m.name };
                var tint = new Color(m.tint[0], m.tint[1], m.tint[2], 1f);
                var tex = Load(m.tex, false, notes);
                var nor = Load(m.nor, true, notes);
                if (tex != null) mat.SetTexture("_MainTex", tex);
                if (nor != null) mat.SetTexture("_BumpMap", nor);
                mat.SetFloat("_BumpScale", nor != null ? m.bump : 0f);
                mat.SetFloat("_Smooth", m.smooth);
                if (m.kind == "ground")
                {
                    mat.SetColor("_Tint", tint);
                    mat.SetFloat("_Tile", m.tile);
                    mat.SetFloat("_Angle", m.along ? data.heading : 0f);
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
                var go = new GameObject(m.name) { isStatic = true };
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = m.shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                triangles += m.t.Length / 3;
            }

            int cars = 0;
            if (data.cars != null)
                foreach (var c in data.cars)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.prefab);
                    if (prefab == null) { notes.Add("no prefab " + Path.GetFileNameWithoutExtension(c.prefab)); continue; }
                    var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    car.transform.SetParent(root.transform, false);
                    car.transform.SetPositionAndRotation(new Vector3(c.x, c.y, c.z), Quaternion.Euler(0f, c.yaw, 0f));
                    cars++;
                }

            // One reflection cubemap, rendered from above the road with a camera (so with the sun and the ambient light), for glass,
            // car paint and the sea. Lightmapping.BakeReflectionProbe left out the realtime sun: every building in the reflections
            // was black, and so were the windows that mirror the street instead of the sky. Rendered twice, so glass shows in glass.
            // The project's quality level has shadows switched off (the game switches them on while it runs, in DriveAssist), so
            // everything rendered from the editor came out without a single shadow: nothing stood on the ground. The same set-up
            // as the game for the pictures and the reflection, put back afterwards.
            var savedShadows = QualitySettings.shadows; var savedDistance = QualitySettings.shadowDistance; int savedCascades = QualitySettings.shadowCascades;
            var savedResolution = QualitySettings.shadowResolution; var savedProjection = QualitySettings.shadowProjection;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = 300f;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;

            Cubemap memoryCube = null, savedCube = null;
            try
            {
                Directory.CreateDirectory(SceneDir);
                string stripPath = SceneDir + "/GwangalliReflection.png";
                AssetDatabase.DeleteAsset(SceneDir + "/GwangalliProbe.exr");
                AssetDatabase.DeleteAsset(SceneDir + "/GwangalliReflection.cubemap");
                const int size = 256;
                var strip = new Texture2D(size * 6, size, TextureFormat.RGBA32, false);
                var cube = new Cubemap(size, TextureFormat.RGBA32, true);
                var probeCamGo = new GameObject("ProbeCamera");
                probeCamGo.transform.position = new Vector3(data.probe[0], data.probe[1], data.probe[2]);
                var probeCam = probeCamGo.AddComponent<Camera>();
                probeCam.nearClipPlane = 0.3f; probeCam.farClipPlane = 6500f; probeCam.clearFlags = CameraClearFlags.Skybox;
                probeCam.fieldOfView = 90f; probeCam.aspect = 1f; probeCam.allowHDR = false; probeCam.allowMSAA = false;
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
                // Six plain camera renders (the same path as the view pictures), read back and written into the cubemap, so the
                // pixels exist on the CPU side and survive Apply() and saving. Camera.RenderToCubemap was not dependable here: into
                // a Cubemap the pixels stayed on the GPU only (Apply() or CreateAsset() then uploaded the empty copy), and into a
                // cube render texture the faces read back black. Either way everything reflective turned black.
                var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
                var face = new Texture2D(size, size, TextureFormat.RGBA32, false);
                // cubemap faces +X -X +Y -Y +Z -Z: where each looks and which way is up in its picture
                Vector3[] look = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                Vector3[] upOf = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };
                probeCam.targetTexture = rt;
                Directory.CreateDirectory(outDir);
                for (int pass = 0; pass < 2; pass++)
                {
                    var means = new List<string>();
                    for (int f = 0; f < 6; f++)
                    {
                        probeCam.transform.rotation = Quaternion.LookRotation(look[f], upOf[f]);
                        probeCam.Render();
                        RenderTexture.active = rt;
                        face.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                        var px = face.GetPixels();
                        var flipped = new Color[px.Length];
                        float sum = 0f;
                        for (int i = 0; i < px.Length; i++) { px[i].a = 1f; sum += px[i].r + px[i].g + px[i].b; }
                        // a Texture2D keeps its rows bottom to top, a cubemap face top to bottom
                        for (int y = 0; y < size; y++) Array.Copy(px, y * size, flipped, (size - 1 - y) * size, size);
                        cube.SetPixels(flipped, (CubemapFace)f);
                        means.Add((sum / (px.Length * 3f)).ToString("0.00"));
                        if (pass == 1) strip.SetPixels(f * size, 0, size, size, px);
                    }
                    cube.Apply(true);
                    RenderSettings.customReflectionTexture = cube;
                    if (pass == 1) notes.Add("cube faces mean " + string.Join(" ", means));
                }
                probeCam.targetTexture = null;
                RenderTexture.active = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(face);
                UnityEngine.Object.DestroyImmediate(probeCamGo);
                memoryCube = cube;
                // Kept as an ordinary imported texture (six faces side by side, +X -X +Y -Y +Z -Z): AssetDatabase.CreateAsset on
                // the Cubemap object itself turned it black for everything rendered afterwards, the saved scene included.
                strip.Apply();
                File.WriteAllBytes(stripPath, strip.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(strip);
                AssetDatabase.ImportAsset(stripPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var importer = AssetImporter.GetAtPath(stripPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureShape = TextureImporterShape.TextureCube;
                    importer.generateCubemap = TextureImporterGenerateCubemap.FullCubemap;
                    importer.sRGBTexture = true;
                    importer.mipmapEnabled = true;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.maxTextureSize = 2048;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    settings.cubemapConvolution = TextureImporterCubemapConvolution.Specular;   // rough surfaces get a blurred sky
                    settings.seamlessCubemap = true;
                    importer.SetTextureSettings(settings);
                    importer.SaveAndReimport();
                }
                savedCube = AssetDatabase.LoadAssetAtPath<Cubemap>(stripPath);
                if (savedCube == null) notes.Add("reflection texture did not import as a cubemap");
                else RenderSettings.customReflectionTexture = savedCube;
            }
            catch (Exception e) { notes.Add("reflection " + e.GetType().Name + " " + e.Message); }

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 6500f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = true;
            try { AddPostEffects(camGo); } catch (Exception e) { notes.Add("post " + e.GetType().Name + " " + e.Message); }
            Directory.CreateDirectory(outDir);
            try
            {
                foreach (var view in data.views)
                {
                    cam.transform.position = new Vector3(view.px, view.py, view.pz);
                    cam.transform.LookAt(new Vector3(view.tx, view.ty, view.tz));
                    cam.fieldOfView = view.fov;
                    Render(cam, Path.Combine(outDir, view.name + ".png"), 1280, 720);
                }
            }
            catch (Exception e) { notes.Add("views " + e.GetType().Name + " " + e.Message); }
            finally
            {
                QualitySettings.shadows = savedShadows;
                QualitySettings.shadowDistance = savedDistance;
                QualitySettings.shadowCascades = savedCascades;
                QualitySettings.shadowResolution = savedResolution;
                QualitySettings.shadowProjection = savedProjection;
            }
            if (memoryCube != null && savedCube != null) UnityEngine.Object.DestroyImmediate(memoryCube);
            // left where the game would start: the first driver's view
            if (data.views.Count > 0)
            {
                var v0 = data.views[0];
                cam.transform.position = new Vector3(v0.px, v0.py, v0.pz);
                cam.transform.LookAt(new Vector3(v0.tx, v0.ty, v0.tz));
                cam.fieldOfView = v0.fov;
            }

            Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!string.IsNullOrEmpty(previous) && previous != ScenePath) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            return $"meshes={data.meshes.Count} triangles={triangles} cars={cars} views={data.views.Count} notes=[{string.Join("; ", notes)}] reopened={previous}";
        }

        static Texture2D Load(string path, bool normal, List<string> notes)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (path.StartsWith(SceneDir))        // textures made by the generator: import settings are ours to set
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

        // The same post effects, with the same settings, as the game camera (BusanSliceBuilder.AddPostEffects).
        static void AddPostEffects(GameObject cameraGo)
        {
            const string grade = "81b03f84ce7749ebba0499d38a071fbb", fxaa = "1ef2c80fab875a95297cc9d480afe44e", ao = "c9e3aff736de322a097281fe883b727a";
            Shader ShaderOf(string guid) => AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
            var g = cameraGo.AddComponent<ParkingLotEnvironment.CloudPilot.MapDaylightGrade>();
            g.gradeShader = ShaderOf(grade); g.exposureEV = 0.55f; g.contrast = 1.04f; g.saturation = 1.0f; g.enabled = true;
            var f = cameraGo.AddComponent<ParkingLotEnvironment.CloudPilot.MapCameraFXAA>();
            f.edgeShader = ShaderOf(fxaa); f.enabled = true;
            var o = cameraGo.AddComponent<Kino.Obscurance>();
            var so = new SerializedObject(o);
            so.FindProperty("_intensity").floatValue = 1.1f;
            so.FindProperty("_radius").floatValue = 0.75f;
            so.FindProperty("_sampleCount").enumValueIndex = 4;
            so.FindProperty("_sampleCountValue").intValue = 24;
            so.FindProperty("_downsampling").boolValue = true;
            so.FindProperty("_occlusionSource").enumValueIndex = 1;
            so.FindProperty("_aoShader").objectReferenceValue = ShaderOf(ao);
            so.ApplyModifiedPropertiesWithoutUndo();
            o.enabled = true;
        }

        static void Render(Camera cam, string path, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 4 };
            var previous = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
