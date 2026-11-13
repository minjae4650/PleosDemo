using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // Street trees of the landmark map as picture cards. A downloaded tree model (CC0, Poly Haven "Jacaranda Tree", 312k
    // triangles) is far too heavy to place hundreds of times, so it is rendered once, in an isolated preview scene, from four
    // directions (0, 45, 90, 135 degrees) into a four-panel atlas with a true alpha (rendered over black and over white).
    // tools/districts/trees.py stands four crossed cards per tree, one panel each.
    //
    // bridge: treebake <source dir> <out png>
    //   <source dir>: a folder with the .fbx and textures/ (outside the project); it is copied into Assets/_TreeSource for the
    //   render and deleted again, so the model never stays in the project. <out png> gets a .txt beside it: panel size in metres
    //   at the model's scale, the trunk base's height in the panel (0..1), and the model's height.
    public static class BusanLandmarkTrees
    {
        const string Temp = "Assets/_TreeSource";
        const int Panel = 1024;

        public static string Bake(string sourceDir, string pngPath)
        {
            var fbx = Directory.GetFiles(sourceDir, "*.fbx").FirstOrDefault();
            if (fbx == null) return "ERR no .fbx in " + sourceDir;
            if (Directory.Exists(Temp)) AssetDatabase.DeleteAsset(Temp);
            Directory.CreateDirectory(Temp + "/textures");
            File.Copy(fbx, Temp + "/tree.fbx", true);
            foreach (var t in Directory.GetFiles(Path.Combine(sourceDir, "textures"), "*.png"))
                File.Copy(t, Temp + "/textures/" + Path.GetFileName(t), true);
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Temp + "/tree.fbx");
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.isReadable = false;
            importer.SaveAndReimport();

            Texture2D Tex(string key)
            {
                var path = Directory.GetFiles(Temp + "/textures", "*" + key + "*.png").Select(p => p.Replace('\\', '/')).FirstOrDefault();
                return path == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            var std = Shader.Find("Standard");
            Material Mat(string diff, bool cutout)
            {
                var m = new Material(std);
                m.SetTexture("_MainTex", Tex(diff));
                m.SetFloat("_Glossiness", 0.1f);
                if (cutout)
                {
                    m.SetFloat("_Mode", 1f);
                    m.SetFloat("_Cutoff", 0.5f);
                    m.EnableKeyword("_ALPHATEST_ON");
                    m.renderQueue = 2450;
                }
                return m;
            }
            var leaves = Mat("leaves_rgba", true);
            var trunk = Mat("trunk_diff", false);
            var branches = Mat("branches_diff", false);

            var preview = EditorSceneManager.NewPreviewScene();
            var atlas = new Texture2D(Panel * 4, Panel, TextureFormat.RGBA32, false);
            string info;
            try
            {
                var lightGo = new GameObject("Sun");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, preview);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.1f; light.color = new Color(1f, 0.97f, 0.92f);
                lightGo.transform.rotation = Quaternion.Euler(55f, 30f, 0f);
                var fill = new GameObject("Fill");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fill, preview);
                var fl = fill.AddComponent<Light>();
                fl.type = LightType.Directional; fl.intensity = 0.45f; fl.color = new Color(0.8f, 0.88f, 1f); fl.shadows = LightShadows.None;
                fill.transform.rotation = Quaternion.Euler(20f, 210f, 0f);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Temp + "/tree.fbx");
                var go = (GameObject)Object.Instantiate(prefab);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, preview);
                Bounds bounds = default; bool first = true; int parts = 0;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials.Length == 0 ? new Material[1] : new Material[r.sharedMaterials.Length];
                    var name = r.name.ToLowerInvariant();
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var smName = (r.sharedMaterials.Length > i && r.sharedMaterials[i] != null ? r.sharedMaterials[i].name : name).ToLowerInvariant();
                        string key = smName.Contains("leaf") || smName.Contains("leaves") || name.Contains("lea") ? "leaves" : smName.Contains("branch") || name.Contains("branch") ? "branches" : "trunk";
                        mats[i] = key == "leaves" ? leaves : key == "branches" ? branches : trunk;
                    }
                    r.sharedMaterials = mats;
                    if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
                    parts++;
                }
                float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 1.04f;
                var camGo = new GameObject("Camera");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, preview);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = preview;
                cam.orthographic = true; cam.orthographicSize = size * 0.5f; cam.nearClipPlane = 0.1f; cam.farClipPlane = size * 4f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                var rt = new RenderTexture(Panel, Panel, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                cam.targetTexture = rt;
                var black = new Texture2D(Panel, Panel, TextureFormat.RGBA32, false);
                var white = new Texture2D(Panel, Panel, TextureFormat.RGBA32, false);
                // the panel is centred on the model's middle in width and height; the trunk base is at bounds.min.y
                var centre = new Vector3(bounds.center.x, bounds.min.y + size * 0.5f, bounds.center.z);
                for (int view = 0; view < 4; view++)
                {
                    var axis = Quaternion.Euler(0f, view * 45f, 0f) * Vector3.back;
                    cam.transform.position = centre + axis * size * 2f;
                    cam.transform.LookAt(centre);
                    for (int pass = 0; pass < 2; pass++)
                    {
                        var tex = pass == 0 ? black : white;
                        cam.backgroundColor = pass == 0 ? Color.black : Color.white;
                        cam.Render();
                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, Panel, Panel), 0, 0);
                        tex.Apply();
                    }
                    var b = black.GetPixels(); var w = white.GetPixels();
                    var outPx = new Color[b.Length];
                    for (int i = 0; i < b.Length; i++)
                    {
                        float a = Mathf.Clamp01(1f - ((w[i].r - b[i].r) + (w[i].g - b[i].g) + (w[i].b - b[i].b)) / 3f);
                        outPx[i] = a > 0.02f ? new Color(Mathf.Clamp01(b[i].r / a), Mathf.Clamp01(b[i].g / a), Mathf.Clamp01(b[i].b / a), a)
                                             : new Color(0.22f, 0.28f, 0.16f, 0f);
                    }
                    atlas.SetPixels(view * Panel, 0, Panel, Panel, outPx);
                }
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt); Object.DestroyImmediate(black); Object.DestroyImmediate(white);
                atlas.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
                File.WriteAllBytes(pngPath, atlas.EncodeToPNG());
                File.WriteAllText(pngPath + ".txt", string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", size, 0.0, bounds.size.y));
                info = string.Format(CultureInfo.InvariantCulture, "{0} panel={1:0.00}m height={2:0.00}m bounds={3} renderers={4}", pngPath, size, bounds.size.y, bounds.size.ToString("0.0"), parts);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                Object.DestroyImmediate(atlas);
                AssetDatabase.DeleteAsset(Temp);
            }
            AssetDatabase.Refresh();
            return info;
        }
    }
}
