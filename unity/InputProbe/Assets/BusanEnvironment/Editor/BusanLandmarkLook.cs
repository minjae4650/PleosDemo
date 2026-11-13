using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // The look of the landmark map, shared by the district builder and the assembled scene: the sun, sky, trilight ambient and sea
    // haze, the game camera's post effects, the shadow-quality switch for editor renders, the reflection cubemap made from six
    // camera renders, and the picture render. Copied from BusanGwangalliBlockout.Build (which stays as the reference) so the new
    // path renders the same pictures.
    public static class BusanLandmarkLook
    {
        public const string SkyPath = "Assets/ParkingLotEnvironment/CloudPilot/Materials/Daylight_Sky.mat";

        // The sun and the scene's render settings. Returns the sun.
        public static Light Apply(float sunYaw, float sunPitch)
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.45f;
            sun.transform.rotation = Quaternion.Euler(sunPitch, sunYaw, 0f);
            RenderSettings.sun = sun;
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
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
            return sun;
        }

        // The project's quality level has shadows switched off (the game switches them on while it runs, in DriveAssist), so
        // everything rendered from the editor came out without a single shadow. `using (BusanLandmarkLook.Shadows()) { ... }` sets
        // the game's shadows for the renders inside and puts the quality settings back afterwards.
        public static ShadowScope Shadows() => new ShadowScope();

        public sealed class ShadowScope : IDisposable
        {
            readonly ShadowQuality shadows; readonly float distance; readonly int cascades;
            readonly ShadowResolution resolution; readonly ShadowProjection projection;
            bool done;

            internal ShadowScope()
            {
                shadows = QualitySettings.shadows; distance = QualitySettings.shadowDistance; cascades = QualitySettings.shadowCascades;
                resolution = QualitySettings.shadowResolution; projection = QualitySettings.shadowProjection;
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowDistance = 300f;
                QualitySettings.shadowCascades = 4;
                QualitySettings.shadowResolution = ShadowResolution.High;
                QualitySettings.shadowProjection = ShadowProjection.StableFit;
            }

            public void Dispose()
            {
                if (done) return;
                done = true;
                QualitySettings.shadows = shadows;
                QualitySettings.shadowDistance = distance;
                QualitySettings.shadowCascades = cascades;
                QualitySettings.shadowResolution = resolution;
                QualitySettings.shadowProjection = projection;
            }
        }

        // The game camera: 0.3-6500 m, sky, HDR, the game's post effects.
        public static Camera MainCamera(List<string> notes)
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 6500f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = true;
            try { AddPostEffects(camGo); } catch (Exception e) { notes?.Add("post " + e.GetType().Name + " " + e.Message); }
            return cam;
        }

        // The same post effects, with the same settings, as the game camera (BusanSliceBuilder.AddPostEffects).
        public static void AddPostEffects(GameObject cameraGo)
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

        // One picture: HDR target with 4x MSAA, through the camera's post effects, saved as PNG.
        public static void Render(Camera cam, string path, int width, int height)
        {
            // Reflection probes made or loaded in this editor tick are not seen by Camera.Render until the culling system is told
            // about them (the editor loop does that between ticks): without this every renderer fell back to the default
            // reflection, and glass, the sea and the asphalt sheen came out black although each renderer reported its probe.
            ReflectionProbe.UpdateCachedState();
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

        // A reflection cubemap rendered with a camera at `position` (so with the sun, the ambient light and, inside a Shadows()
        // scope, the shadows), for glass, car paint and the sea, saved as `stripPath` (a PNG of the six faces side by side,
        // +X -X +Y -Y +Z -Z) and imported as a cube with specular convolution. Returns the imported cubemap, or null (see notes).
        // Rendered twice with the first result as the scene's reflection, so glass shows in glass. Leaves the scene's default
        // reflection on Custom with the result.
        // Lightmapping.BakeReflectionProbe left out the realtime sun: every building in the reflections was black, and so were the
        // windows that mirror the street instead of the sky.
        public static Cubemap CaptureReflection(Vector3 position, string stripPath, List<string> notes)
        {
            Cubemap memoryCube = null, savedCube = null;
            try
            {
                const int size = 256;
                var strip = new Texture2D(size * 6, size, TextureFormat.RGBA32, false);
                var cube = new Cubemap(size, TextureFormat.RGBA32, true);
                var probeCamGo = new GameObject("ProbeCamera");
                probeCamGo.transform.position = position;
                var probeCam = probeCamGo.AddComponent<Camera>();
                probeCam.nearClipPlane = 0.3f; probeCam.farClipPlane = 6500f; probeCam.clearFlags = CameraClearFlags.Skybox;
                probeCam.fieldOfView = 90f; probeCam.aspect = 1f; probeCam.allowHDR = false; probeCam.allowMSAA = false;
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = null;          // the first pass sees what a fresh scene has: nothing
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
                    if (pass == 1) notes.Add(Path.GetFileNameWithoutExtension(stripPath) + " faces mean " + string.Join(" ", means));
                }
                probeCam.targetTexture = null;
                RenderTexture.active = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(face);
                UnityEngine.Object.DestroyImmediate(probeCamGo);
                memoryCube = cube;
                // Kept as an ordinary imported texture: AssetDatabase.CreateAsset on the Cubemap object itself turned it black for
                // everything rendered afterwards, the saved scene included.
                strip.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(stripPath));
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
                if (savedCube == null) notes.Add(Path.GetFileName(stripPath) + " did not import as a cubemap");
                else RenderSettings.customReflectionTexture = savedCube;
            }
            catch (Exception e) { notes.Add("reflection " + e.GetType().Name + " " + e.Message); }
            if (memoryCube != null && savedCube != null) UnityEngine.Object.DestroyImmediate(memoryCube);
            return savedCube;
        }
    }
}
