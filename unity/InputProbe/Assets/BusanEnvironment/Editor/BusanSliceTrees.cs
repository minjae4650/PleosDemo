using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // The tree prefabs carry ~60k leaf triangles each, so they are never placed. They are rendered once
    // in an isolated preview scene (black and white backgrounds give a true alpha) into a two-panel
    // card atlas: left panel seen along +Z, right panel along +X. Street trees are crossed cards.
    public static class BusanSliceTrees
    {
        const string Dir = "Assets/ParkingLotEnvironment/CloudPilot/PrefabWorkflow20261105/Prefabs/Environment/";
        static readonly string[] Parts = { "0020_tree_small_02_branches.prefab", "0022_tree_small_02_trunk.prefab", "0024_tree_small_02_leaves.prefab" };

        public static float CardSize = 4.5f;      // world side of one panel at prefab scale
        public static float CardCenterY = 2.2f;   // panel centre above the trunk base

        public static void Load(string pngPath)
        {
            var meta = pngPath + ".txt";
            if (!File.Exists(meta)) return;
            var t = File.ReadAllText(meta).Split(' ');
            CardSize = float.Parse(t[0], CultureInfo.InvariantCulture);
            CardCenterY = float.Parse(t[1], CultureInfo.InvariantCulture);
        }

        public static string Bake(string pngPath)
        {
            const int panel = 1024;   // 2x the stage-1 resolution: trunks next to the kerb were low-res
            var preview = EditorSceneManager.NewPreviewScene();
            var atlas = new Texture2D(panel * 2, panel, TextureFormat.RGBA32, false);
            string info;
            try
            {
                var lightGo = new GameObject("Sun");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, preview);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.25f; light.color = new Color(1f, 0.97f, 0.92f);
                lightGo.transform.rotation = Quaternion.Euler(50f, 25f, 0f);

                Bounds bounds = default; bool first = true;
                foreach (var part in Parts)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + part);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                    if (part.Contains("trunk"))
                    {
                        // slimmer trunk (65 % thickness, same height) around its own axis; the canopy and the branches keep their size
                        var before = go.GetComponentsInChildren<Renderer>()[0].bounds.center;
                        go.transform.localScale = new Vector3(0.65f, 1f, 0.65f);
                        var after = go.GetComponentsInChildren<Renderer>()[0].bounds.center;
                        go.transform.position += new Vector3(before.x - after.x, 0f, before.z - after.z);
                    }
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                    {
                        if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
                    }
                }
                float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 1.12f;
                var camGo = new GameObject("Camera");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, preview);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = preview;
                cam.orthographic = true; cam.orthographicSize = size * 0.5f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                var rt = new RenderTexture(panel, panel, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                cam.targetTexture = rt;
                var black = new Texture2D(panel, panel, TextureFormat.RGBA32, false);
                var white = new Texture2D(panel, panel, TextureFormat.RGBA32, false);
                for (int view = 0; view < 2; view++)
                {
                    var axis = view == 0 ? Vector3.back : Vector3.left;
                    cam.transform.position = bounds.center + axis * 30f;
                    cam.transform.LookAt(bounds.center);
                    for (int pass = 0; pass < 2; pass++)
                    {
                        var tex = pass == 0 ? black : white;
                        cam.backgroundColor = pass == 0 ? Color.black : Color.white;
                        cam.Render();
                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, panel, panel), 0, 0);
                        tex.Apply();
                    }
                    var b = black.GetPixels(); var w = white.GetPixels();
                    var outPx = new Color[b.Length];
                    for (int i = 0; i < b.Length; i++)
                    {
                        float a = 1f - ((w[i].r - b[i].r) + (w[i].g - b[i].g) + (w[i].b - b[i].b)) / 3f;
                        a = Mathf.Clamp01(a);
                        outPx[i] = a > 0.02f ? new Color(Mathf.Clamp01(b[i].r / a), Mathf.Clamp01(b[i].g / a), Mathf.Clamp01(b[i].b / a), a > 0.5f ? 1f : a * 2f)
                                             : new Color(0.2f, 0.3f, 0.15f, 0f);
                    }
                    atlas.SetPixels(view * panel, 0, panel, panel, outPx);
                }
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt); Object.DestroyImmediate(black); Object.DestroyImmediate(white);
                atlas.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
                File.WriteAllBytes(pngPath, atlas.EncodeToPNG());
                CardSize = size; CardCenterY = bounds.center.y - bounds.min.y;
                info = string.Format(CultureInfo.InvariantCulture, "{0} {1} bounds={2}", CardSize, CardCenterY, bounds.size.ToString("0.00"));
                File.WriteAllText(pngPath + ".txt", string.Format(CultureInfo.InvariantCulture, "{0} {1}", CardSize, CardCenterY));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                Object.DestroyImmediate(atlas);
            }
            AssetDatabase.Refresh();
            return info;
        }
    }
}
