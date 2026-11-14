using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // Cars for traffic and parked cars. Every model is baked into ONE mesh (vertex colour = the part's own albedo,
    // alpha = paint mask, uv2 = smoothness/metallic) from the low-poly source prefab, with material values taken from the
    // finished ReusableVehicles prefab of the same name. Source prefabs, meshes and materials are only read.
    public static partial class BusanSliceBuilder
    {
        const string CarDir = Root + "/TrafficCars";
        const string Reusable = "Assets/ParkingLotEnvironment/CloudPilot/ParkingContext20261105/ReusableVehicles/";
        const string RealCars = "Assets/ParkingLotEnvironment/RealVehicles/";

        sealed class CarSpec
        {
            public string name, geometry, values;
            public string[] variants = new string[0];        // colour variants of one model: the renderers whose material differs are the paint
            public string[] paintNames = new string[0];      // paint renderers by name when no colour variants exist
            public string[] windowNames = new string[0];     // renderers that carry paint values but are windows (Azera)
            public string[] trimNames = new string[0];
            public float weight = 1f;
            public int maxCount = 8, minCount;
            public bool lightPaintOnly;
            public bool noseAtMinusZ;   // source model faces -Z: baked turned 180 degrees, so +Z is the nose of every baked car (traffic and parking drive along +Z)
            public string[] skipNames = new string[0];   // source parts left out of the bake
            public string plateBase, plateFrom;          // plateBase: blank plate part that gets Korean number plate quads, with the plate material of the plateFrom prefab
        }

        static readonly CarSpec[] CarSpecs =
        {
            new CarSpec { name = "Tucson", geometry = RealCars + "hyundai-tucson-lod1/hyundai-tucson-lod1.prefab", values = Reusable + "Hyundai_Tucson_Black.prefab",
                variants = new[] { Reusable + "Hyundai_Tucson_Black.prefab", Reusable + "Hyundai_Tucson_White.prefab", Reusable + "Hyundai_Tucson_Graphite.prefab" }, weight = 14f, noseAtMinusZ = true },
            new CarSpec { name = "i30", geometry = RealCars + "hyundai-i30-lod1/hyundai-i30-lod1.prefab", values = Reusable + "Hyundai_i30_Black.prefab",
                variants = new[] { Reusable + "Hyundai_i30_Black.prefab", Reusable + "Hyundai_i30_Blue.prefab", Reusable + "Hyundai_i30_Burgundy.prefab" }, weight = 14f, noseAtMinusZ = true },
            new CarSpec { name = "Azera", geometry = RealCars + "hyundai-azera-lod1/hyundai-azera-lod1.prefab", values = Reusable + "Hyundai_Azera_Navy.prefab",
                variants = new[] { Reusable + "Hyundai_Azera_Navy.prefab", Reusable + "Hyundai_Azera_Silver.prefab" },
                windowNames = new[] { "front_doo_1", "right_doo_1", "rear_door_1", "rear_do38_1", "tail_wind", "tail_wi39_1" }, // ParkingLotAzeraFinish
                trimNames = new[] { "tail_wind_1", "tail_wi39" }, weight = 9f, noseAtMinusZ = true },
            new CarSpec { name = "Ioniq5", geometry = RealCars + "hyundai-ioniq5-openx/hyundai-ioniq5-openx.prefab", paintNames = new[] { "Body" }, weight = 8f, noseAtMinusZ = true,   // no named lamps in this model: found on the lineup sheet (rear light bar seen from ahead)
                skipNames = new[] { "License_Plate_Black", "License_Plate_Blue" }, plateBase = "License_Plate_White", plateFrom = Reusable + "Hyundai_Tucson_Graphite.prefab" },   // modelled plates spell the author's name
            new CarSpec { name = "PorterII", geometry = Reusable + "Hyundai_Porter_II_Blue.prefab", paintNames = new[] { "BluePaint" }, weight = 3f, maxCount = 2, minCount = 2 },
            new CarSpec { name = "BoxTruck", geometry = Reusable + "Generic_DeliveryTruck.prefab", paintNames = new[] { "lct3000_Bodymat" }, weight = 6f, maxCount = 3, lightPaintOnly = true },
        };

        // Korean road distribution (percent): white 30, black 20, grey 20, silver 15, navy 7, wine 5, blue 3.
        static readonly (string key, Color color, float weight)[] PaintPalette =
        {
            ("white", new Color(0.92f, 0.92f, 0.91f), 30f), ("black", new Color(0.035f, 0.037f, 0.042f), 20f), ("grey", new Color(0.38f, 0.39f, 0.41f), 20f),
            ("silver", new Color(0.70f, 0.72f, 0.74f), 15f), ("navy", new Color(0.04f, 0.07f, 0.17f), 7f), ("wine", new Color(0.30f, 0.05f, 0.07f), 5f),
            ("blue", new Color(0.08f, 0.22f, 0.55f), 3f),
        };

        static Material[] paintMaterials;
        static float[] paintWeights;
        static readonly List<GameObject> bakedCars = new List<GameObject>();
        static readonly List<string> carReport = new List<string>();

        // "rearlight-reflect_plate" (i30) is a tail light part: taken for a number plate, its red material became the plate material of the whole car.
        static bool IsPlateName(string n) => n.IndexOf("plate", StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("License", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("reflect", StringComparison.OrdinalIgnoreCase) < 0;

        static bool SkipInside(string n, Material m)
        {
            var l = n.ToLowerInvariant();
            if (l.Contains("seat") || l.Contains("sits") || l.Contains("dashboard") || l.Contains("steering") || l.Contains("interior") || l.StartsWith("panel")) return true;
            if (m != null && m.HasProperty("_Color") && m.shader.name.Contains("DoubleSidedPBR"))
            {
                var c = m.color; // beige leather of the legacy Azera cabin
                if (Mathf.Abs(c.r - 0.9f) < 0.02f && Mathf.Abs(c.g - 0.886f) < 0.02f && Mathf.Abs(c.b - 0.859f) < 0.02f) return true;
            }
            return false;
        }

        static bool IsGlassMaterial(Material m, string rendererName)
        {
            if (m == null) return false;
            return m.shader.name.Contains("Glass") || m.renderQueue >= 3000 || m.name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 && !m.name.Contains("Lights_and_Glass");
        }

        static Color AverageColor(Texture tex)
        {
            var rt = RenderTexture.GetTemporary(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            Graphics.Blit(tex, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var t2 = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            t2.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
            t2.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var px = t2.GetPixels();
            UnityEngine.Object.DestroyImmediate(t2);
            float r = 0, g = 0, b = 0;
            foreach (var p in px) { r += p.r; g += p.g; b += p.b; }
            return new Color(r / px.Length, g / px.Length, b / px.Length, 1f);
        }

        static void DescribePart(Material m, string rendererName, CarSpec spec, HashSet<string> paint, out Color color, out float smooth, out float metal, out float mask)
        {
            mask = 0f; metal = 0f; smooth = 0.4f;
            if (spec.windowNames.Contains(rendererName) || IsGlassMaterial(m, rendererName))
            {
                bool lens = rendererName.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0 || rendererName.IndexOf("lamp", StringComparison.OrdinalIgnoreCase) >= 0
                    || rendererName.IndexOf("fog", StringComparison.OrdinalIgnoreCase) >= 0 || rendererName.IndexOf("light", StringComparison.OrdinalIgnoreCase) >= 0;
                // one opaque value for every transparent part: no second render queue, nothing sorted
                color = lens ? new Color(0.50f, 0.53f, 0.55f) : new Color(0.03f, 0.05f, 0.06f);
                smooth = 0.92f;
                return;
            }
            if (spec.trimNames.Contains(rendererName)) { color = new Color(0.035f, 0.04f, 0.045f); smooth = 0.45f; metal = 0.05f; return; }
            color = m != null && m.HasProperty("_Color") ? m.color : new Color(0.5f, 0.5f, 0.5f);
            color.a = 1f;
            if (m != null && m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null)
            {
                var avg = AverageColor(m.GetTexture("_MainTex"));
                color = new Color(color.r * avg.r, color.g * avg.g, color.b * avg.b, 1f);
            }
            if (m != null && m.HasProperty("_Glossiness")) smooth = m.GetFloat("_Glossiness");
            if (m != null && m.HasProperty("_Metallic")) metal = m.GetFloat("_Metallic");
            if (paint.Contains(rendererName)) mask = 1f;
            // Porter II: the cargo bed is the "Steel" renderer (metallic 0.72), which mirrored the whole street; painted steel is matte
            if (spec.name == "PorterII" && mask < 0.5f && metal > 0.4f) { metal = 0.2f; smooth = Mathf.Min(smooth, 0.36f); }
        }

        static void AppendMesh(Mesh mesh, int submesh, Matrix4x4 toBody, Color color, float smooth, float metal, float mask, bool keepUv,
            List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Vector2> uv2, List<Color> col, List<int> tris, ref int mirrored)
        {
            var srcV = mesh.vertices; var srcN = mesh.normals; var srcUv = mesh.uv;
            var idx = mesh.GetTriangles(submesh);
            var remap = new Dictionary<int, int>();
            bool flip = toBody.determinant < 0f;
            if (flip) mirrored++;
            var normalMatrix = toBody.inverse.transpose;
            foreach (int i in idx)
            {
                if (!remap.TryGetValue(i, out int k))
                {
                    k = v.Count;
                    remap[i] = k;
                    v.Add(toBody.MultiplyPoint3x4(srcV[i]));
                    n.Add(srcN.Length > i ? normalMatrix.MultiplyVector(srcN[i]).normalized : Vector3.up);
                    uv.Add(keepUv && srcUv.Length > i ? srcUv[i] : Vector2.zero);
                    uv2.Add(new Vector2(smooth, metal));
                    col.Add(new Color(color.r, color.g, color.b, mask));
                }
            }
            for (int t = 0; t < idx.Length; t += 3)
            {
                if (flip) { tris.Add(remap[idx[t]]); tris.Add(remap[idx[t + 2]]); tris.Add(remap[idx[t + 1]]); }
                else { tris.Add(remap[idx[t]]); tris.Add(remap[idx[t + 1]]); tris.Add(remap[idx[t + 2]]); }
            }
        }

        static GameObject BakeCar(CarSpec spec)
        {
            var geo = AssetDatabase.LoadAssetAtPath<GameObject>(spec.geometry);
            var values = AssetDatabase.LoadAssetAtPath<GameObject>(spec.values ?? spec.geometry);
            if (geo == null || values == null) throw new FileNotFoundException("Vehicle source missing: " + spec.geometry + " / " + spec.values);
            var valueByName = new Dictionary<string, Renderer>();
            foreach (var r in values.GetComponentsInChildren<Renderer>(true)) if (!valueByName.ContainsKey(r.name)) valueByName[r.name] = r;

            // paint = the renderers whose material differs between colour variants of the same model
            var paint = new HashSet<string>(spec.paintNames);
            if (spec.variants.Length >= 2)
            {
                var first = AssetDatabase.LoadAssetAtPath<GameObject>(spec.variants[0]).GetComponentsInChildren<Renderer>(true).Where(r => !IsPlateName(r.name)).ToList();
                foreach (var path in spec.variants.Skip(1))
                {
                    var other = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Renderer>(true).ToDictionary(r => r.name, r => r, StringComparer.Ordinal);
                    foreach (var r in first)
                        if (other.TryGetValue(r.name, out var o) && o.sharedMaterial != r.sharedMaterial) paint.Add(r.name);
                }
            }
            foreach (var w in spec.windowNames) paint.Remove(w);

            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var col = new List<Color>();
            var body = new List<int>(); var plate = new List<int>();
            int mirrored = 0, skipped = 0, parts = 0;
            long sourceTris = 0;
            var geoRoot = geo.transform;
            var turn = spec.noseAtMinusZ ? Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f)) : Matrix4x4.identity;
            foreach (var r in geo.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || IsPlateName(r.name) || spec.skipNames.Contains(r.name)) continue;
                var mesh = mf.sharedMesh;
                sourceTris += mesh.triangles.Length / 3;
                var src = valueByName.TryGetValue(r.name, out var vr) ? vr : r;   // finished material values of the same part
                var mats = src.sharedMaterials;
                var toBody = turn * geoRoot.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null;
                    if (SkipInside(r.name, mat)) { skipped++; continue; }
                    DescribePart(mat, r.name, spec, paint, out var color, out var smooth, out var metal, out var mask);
                    AppendMesh(mesh, s, toBody, color, smooth, metal, mask, false, v, n, uv, uv2, col, body, ref mirrored);
                    parts++;
                }
            }
            // number plates: taken from the finished prefab and kept as their own sub-mesh with the original plate material
            Material plateMat = null;
            var valuesRoot = values.transform;
            foreach (var r in values.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !IsPlateName(r.name)) continue;
                if (plateMat == null) plateMat = r.sharedMaterial;
                AppendMesh(mf.sharedMesh, 0, turn * valuesRoot.worldToLocalMatrix * r.transform.localToWorldMatrix, Color.white, 0.3f, 0f, 0f, true, v, n, uv, uv2, col, plate, ref mirrored);
            }

            if (spec.plateBase != null)
            {
                var from = AssetDatabase.LoadAssetAtPath<GameObject>(spec.plateFrom);
                plateMat = from.GetComponentsInChildren<MeshRenderer>(true).First(r => IsPlateName(r.name)).sharedMaterial;
                var baseRenderer = geo.GetComponentsInChildren<MeshRenderer>(true).First(r => r.name == spec.plateBase);
                var toBody = turn * geoRoot.worldToLocalMatrix * baseRenderer.transform.localToWorldMatrix;
                var corners = baseRenderer.GetComponent<MeshFilter>().sharedMesh.vertices.Select(p => toBody.MultiplyPoint3x4(p)).ToArray();
                foreach (float sign in new[] { 1f, -1f })   // the plate part holds both plates: one at each end of the car
                {
                    var end = corners.Where(p => p.z * sign > 0f).ToArray();
                    float x0 = end.Min(p => p.x), x1 = end.Max(p => p.x), y0 = end.Min(p => p.y), y1 = end.Max(p => p.y);
                    float z = sign > 0f ? end.Max(p => p.z) + 0.004f : end.Min(p => p.z) - 0.004f;
                    float left = sign > 0f ? x1 : x0, right = sign > 0f ? x0 : x1;   // as seen by someone looking at this end of the car
                    int k = v.Count;
                    v.Add(new Vector3(left, y0, z)); v.Add(new Vector3(left, y1, z)); v.Add(new Vector3(right, y1, z)); v.Add(new Vector3(right, y0, z));
                    uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(0f, 1f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(1f, 0f));
                    for (int i = 0; i < 4; i++) { n.Add(new Vector3(0f, 0f, sign)); uv2.Add(new Vector2(0.3f, 0f)); col.Add(new Color(1f, 1f, 1f, 0f)); }
                    plate.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
                }
            }

            var baked = new Mesh { name = "Car_" + spec.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            baked.SetVertices(v); baked.SetNormals(n); baked.SetUVs(0, uv); baked.SetUVs(1, uv2); baked.SetColors(col);
            baked.subMeshCount = plate.Count > 0 ? 2 : 1;
            baked.SetTriangles(body, 0);
            if (plate.Count > 0) baked.SetTriangles(plate, 1);
            baked.RecalculateBounds();
            var meshPath = CarDir + "/Car_" + spec.name + ".asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(baked, meshPath);

            var root = new GameObject(spec.name);
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(root.transform, false);
            bodyGo.AddComponent<MeshFilter>().sharedMesh = baked;
            var mr = bodyGo.AddComponent<MeshRenderer>();
            var shared = new List<Material> { paintMaterials[0] };
            if (plate.Count > 0) shared.Add(plateMat);
            mr.sharedMaterials = shared.ToArray();
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            var lamps = BuildBrakeLamps(root.transform, baked.bounds, spec.name);
            var model = root.AddComponent<CarModel>();
            model.modelName = spec.name; model.triangles = body.Count / 3 + plate.Count / 3; model.weight = spec.weight; model.maxCount = spec.maxCount; model.minCount = spec.minCount; model.lightPaintOnly = spec.lightPaintOnly;
            model.body = mr; model.paintSlot = 0; model.brakeLamps = lamps;
            var lod = root.AddComponent<LODGroup>();
            lod.fadeMode = LODFadeMode.None;
            lod.SetLODs(new[] { new LOD(0.02f, new Renderer[] { mr }) });   // culled below 2 percent of the screen height
            lod.RecalculateBounds();
            carReport.Add($"{spec.name}: source={sourceTris} baked={model.triangles} tris (skipped interior parts={skipped}) parts={parts} paintRenderers={paint.Count} plates={(plate.Count > 0 ? "yes" : "no")} mirroredParts={mirrored} renderers={root.GetComponentsInChildren<Renderer>(true).Length} size={baked.bounds.size.ToString("0.00")}");
            return root;
        }

        // Two small red quads at the tail; the traffic script switches them on while the car slows down.
        static GameObject BuildBrakeLamps(Transform parent, Bounds b, string name)
        {
            var mb = new MeshBuilder();
            float z = b.min.z - 0.012f, y = b.min.y + b.size.y * 0.58f;
            foreach (float sx in new[] { -1f, 1f })
            {
                float cx = sx * b.size.x * 0.31f;
                mb.Quad(new Vector3(cx - 0.15f, y - 0.05f, z), new Vector3(cx - 0.15f, y + 0.05f, z), new Vector3(cx + 0.15f, y + 0.05f, z), new Vector3(cx + 0.15f, y - 0.05f, z), Vector3.back);
            }
            var mesh = mb.Bake("BrakeLamps_" + name);
            var path = CarDir + "/BrakeLamps_" + name + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject("BrakeLamps");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = sink.Mats["taillight"];
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            go.SetActive(false);
            return go;
        }

        static Texture2D TaxiSignTexture()
        {
            var path = TexDir + "/taxi_sign.png";
            if (!File.Exists(path))
            {
                // 5x7 block letters "TAXI" in dark on amber (no real name or logo)
                string[] glyphT = { "11111", "00100", "00100", "00100", "00100", "00100", "00100" };
                string[] glyphA = { "01110", "10001", "10001", "11111", "10001", "10001", "10001" };
                string[] glyphX = { "10001", "10001", "01010", "00100", "01010", "10001", "10001" };
                string[] glyphI = { "01110", "00100", "00100", "00100", "00100", "00100", "01110" };
                var glyphs = new[] { glyphT, glyphA, glyphX, glyphI };
                const int w = 128, h = 32;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var amber = new Color(1f, 0.62f, 0.12f); var ink = new Color(0.12f, 0.07f, 0.02f);
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) tex.SetPixel(x, y, amber);
                for (int g = 0; g < 4; g++)
                    for (int gy = 0; gy < 7; gy++)
                        for (int gx = 0; gx < 5; gx++)
                            if (glyphs[g][6 - gy][gx] == '1')
                                for (int py = 0; py < 3; py++) for (int px = 0; px < 3; px++)
                                    tex.SetPixel(14 + g * 28 + gx * 4 + px, 5 + gy * 3 + py, ink);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Taxi: the Azera body in white plus a roof light (procedural box, amber, glowing, lettered).
        static GameObject BakeTaxi(GameObject azera)
        {
            var root = (GameObject)UnityEngine.Object.Instantiate(azera);
            root.name = "AzeraTaxi";
            var src = azera.GetComponent<CarModel>();
            var model = root.GetComponent<CarModel>();
            model.modelName = "AzeraTaxi"; model.weight = 5f; model.maxCount = 4; model.fixedPaint = true;
            var body = root.transform.Find("Body").GetComponent<MeshRenderer>();
            model.body = body;
            model.SetPaint(paintMaterials[0]);
            var bounds = body.GetComponent<MeshFilter>().sharedMesh.bounds;

            var mb = new MeshBuilder();
            float top = bounds.max.y + 0.01f;
            mb.Box(new Vector3(0f, top + 0.08f, -0.2f), new Vector3(0.62f, 0.16f, 0.24f));
            var mesh = mb.Bake("TaxiRoofSign");
            // letters on the front and back faces: planar uv over the box side faces
            var verts = mesh.vertices; var uvs = new Vector2[verts.Length];
            for (int i = 0; i < verts.Length; i++) uvs[i] = new Vector2((verts[i].x / 0.62f) + 0.5f, (verts[i].y - top) / 0.16f);
            mesh.uv = uvs;
            var path = CarDir + "/TaxiRoofSign.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var sign = new GameObject("RoofSign");
            sign.transform.SetParent(root.transform, false);
            sign.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = sign.AddComponent<MeshRenderer>();
            r.sharedMaterial = LoadOrCreate("taxi-sign", "Standard");
            var m = r.sharedMaterial;
            m.mainTexture = TaxiSignTexture();
            m.color = Color.white;
            m.SetFloat("_Glossiness", 0.2f);
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", TaxiSignTexture());
            m.SetColor("_EmissionColor", new Color(0.9f, 0.55f, 0.1f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var lod = root.GetComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.02f, new Renderer[] { body, r }) });
            model.triangles = src.triangles + mesh.triangles.Length / 3;
            carReport.Add($"AzeraTaxi: baked={model.triangles} tris (Azera mesh shared + roof sign {mesh.triangles.Length / 3}) renderers={root.GetComponentsInChildren<Renderer>(true).Length}");
            return root;
        }

        static void CreatePaintMaterials()
        {
            var shader = Shader.Find("BusanEnvironment/CarBaked");
            if (shader == null) throw new Exception("Shader BusanEnvironment/CarBaked not found");
            paintMaterials = new Material[PaintPalette.Length];
            paintWeights = new float[PaintPalette.Length];
            for (int i = 0; i < PaintPalette.Length; i++)
            {
                var m = LoadOrCreate("car-" + PaintPalette[i].key, "BusanEnvironment/CarBaked");
                m.SetColor("_PaintColor", PaintPalette[i].color);
                m.SetFloat("_PaintSmoothness", 0.74f);
                m.SetFloat("_PaintMetallic", 0.30f);
                m.enableInstancing = true;
                paintMaterials[i] = m;
                paintWeights[i] = PaintPalette[i].weight;
            }
        }

        static Material PickPaint(System.Random rnd, bool lightOnly = false)
        {
            bool Allowed(int i) => !lightOnly || PaintPalette[i].key == "white" || PaintPalette[i].key == "silver";
            float total = 0f;
            for (int i = 0; i < paintWeights.Length; i++) if (Allowed(i)) total += paintWeights[i];
            float pick = (float)rnd.NextDouble() * total;
            for (int i = 0; i < paintWeights.Length; i++) { if (!Allowed(i)) continue; pick -= paintWeights[i]; if (pick <= 0f) return paintMaterials[i]; }
            return paintMaterials[0];
        }

        /// <summary>Diagnostics (bridge "carfront"): position along Z of the named front and rear parts of every source model after the bake turn. A correct car has front parts at +Z.</summary>
        public static string CarFrontReport()
        {
            string[] frontWords = { "head", "front", "grill", "hood", "bonnet", "windshield", "wiper" };
            string[] rearWords = { "tail", "rear", "trunk", "boot", "brake", "back" };
            var sb = new StringBuilder();
            foreach (var spec in CarSpecs)
            {
                var geo = AssetDatabase.LoadAssetAtPath<GameObject>(spec.geometry);
                if (geo == null) { sb.AppendLine(spec.name + ": source missing"); continue; }
                var turn = spec.noseAtMinusZ ? Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f)) : Matrix4x4.identity;
                float frontSum = 0f, rearSum = 0f; int frontN = 0, rearN = 0;
                var named = new List<string>();
                foreach (var r in geo.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var m = turn * geo.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    float z = m.MultiplyPoint3x4(mf.sharedMesh.bounds.center).z;
                    var l = r.name.ToLowerInvariant();
                    bool front = frontWords.Any(w => l.Contains(w)), rear = rearWords.Any(w => l.Contains(w));
                    if (front == rear) continue;   // no hint, or both ("rear_door_front")
                    if (front) { frontSum += z; frontN++; } else { rearSum += z; rearN++; }
                    if (named.Count < 8) named.Add($"{r.name}({z:0.0})");
                }
                string verdict = frontN == 0 || rearN == 0 ? "UNKNOWN (no named parts)" : frontSum / frontN > rearSum / rearN ? "nose at +Z" : "NOSE AT -Z";
                sb.AppendLine($"{spec.name}: flag noseAtMinusZ={spec.noseAtMinusZ} frontParts={frontN} meanZ={(frontN > 0 ? frontSum / frontN : 0f):0.00} rearParts={rearN} meanZ={(rearN > 0 ? rearSum / rearN : 0f):0.00} -> {verdict} | {string.Join(" ", named)}");
                // red parts in the front half of the car would be a mistake in the source model (tail light material on the nose)
                var values = AssetDatabase.LoadAssetAtPath<GameObject>(spec.values ?? spec.geometry);
                var valueByName = new Dictionary<string, Renderer>();
                foreach (var vr in values.GetComponentsInChildren<Renderer>(true)) if (!valueByName.ContainsKey(vr.name)) valueByName[vr.name] = vr;
                var red = new List<string>();
                foreach (var r in geo.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var m = turn * geo.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    var bounds = mf.sharedMesh.bounds;
                    var c = m.MultiplyPoint3x4(bounds.center);
                    var src = valueByName.TryGetValue(r.name, out var found) ? found : r;
                    var mat = src.sharedMaterial;
                    if (mat == null || !mat.HasProperty("_Color")) continue;
                    var col = mat.color;
                    if (col.r > 2f * col.g && col.r > 2f * col.b && c.z > 0.5f)
                        red.Add($"{r.name}(z={c.z:0.00} y={c.y:0.00} x={c.x:0.00} size={bounds.size.x:0.00}x{bounds.size.y:0.00}x{bounds.size.z:0.00} tris={mf.sharedMesh.triangles.Length / 3} mat={mat.name})");
                }
                if (red.Count > 0) sb.AppendLine($"   red parts in the front half: {string.Join("; ", red)}");
            }
            return sb.ToString();
        }

        static void BuildCarPrefabs()
        {
            Directory.CreateDirectory(CarDir);
            bakedCars.Clear(); carReport.Clear();
            CreatePaintMaterials();
            var temps = new List<GameObject>();
            foreach (var spec in CarSpecs) temps.Add(BakeCar(spec));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var azera = temps.First(t => t.name == "Azera");
            temps.Add(BakeTaxi(azera));
            foreach (var t in temps)
            {
                var path = CarDir + "/" + t.name + ".prefab";
                AssetDatabase.DeleteAsset(path);
                bakedCars.Add(PrefabUtility.SaveAsPrefabAsset(t, path));
                UnityEngine.Object.DestroyImmediate(t);
            }
            AssetDatabase.SaveAssets();
            foreach (var line in carReport) Log("CAR " + line);
        }
    }
}
