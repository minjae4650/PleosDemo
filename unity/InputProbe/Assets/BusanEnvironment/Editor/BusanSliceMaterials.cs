using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    public static partial class BusanSliceBuilder
    {
        // Typical real-world sizes of the photo facades (m): used to round bay and floor counts so
        // windows are never cut. See docs/busan-slice-v2-plan.md.
        const float AptBayW = 29f / 6f, AptFloorH = 58f / 20f;
        const float ShopBayW = 15.75f / 5f, ShopH = 10.5f;
        const float VillaTile = 12f;

        static Material[] treeMats;

        static Texture2D Tex(string path, bool srgb = true)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) Debug.LogWarning("BUSAN_SLICE: missing texture " + path);
            return t;
        }

        static void SetImporter(string path, Action<TextureImporter> edit)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            edit(imp);
            imp.SaveAndReimport();
        }

        static void EnsureTextures()
        {
            string[] required = { "apt_ground", "apt_mid", "glass", "villa_mid", "signs", "truss_x", "road_macro", "sidewalk_paving", "sidewalk_paving_nor", "tactile" };
            foreach (var name in required)
            {
                var p = TexDir + "/" + name + ".png";
                if (!File.Exists(p)) throw new FileNotFoundException("Run py -3 tools/make-busan-slice-textures.py first: missing " + p);
            }
            AssetDatabase.Refresh();
            foreach (var name in required)
            {
                var p = TexDir + "/" + name + ".png";
                SetImporter(p, i =>
                {
                    i.mipmapEnabled = true; i.anisoLevel = 8; i.wrapMode = TextureWrapMode.Repeat;
                    i.alphaIsTransparency = name == "truss_x";
                    i.maxTextureSize = 2048;
                });
            }
            // data textures: no sRGB, repeat; the normal map is imported as one
            SetImporter(TexDir + "/road_macro.png", i => { i.sRGBTexture = false; i.mipmapEnabled = true; i.anisoLevel = 4; i.wrapMode = TextureWrapMode.Repeat; i.maxTextureSize = 1024; });
            SetImporter(TexDir + "/sidewalk_paving_nor.png", i => { i.textureType = TextureImporterType.NormalMap; i.anisoLevel = 8; i.wrapMode = TextureWrapMode.Repeat; });
            SetImporter(TexDir + "/sidewalk_paving.png", i => { i.anisoLevel = 8; i.wrapMode = TextureWrapMode.Repeat; });
            SetImporter(TexDir + "/tactile.png", i => { i.anisoLevel = 4; i.wrapMode = TextureWrapMode.Repeat; });
            if (!File.Exists(TexDir + "/tree_card.png")) BusanSliceTrees.Bake(TexDir + "/tree_card.png");
            BusanSliceTrees.Load(TexDir + "/tree_card.png");
            AssetDatabase.Refresh();
            SetImporter(TexDir + "/tree_card.png", i => { i.alphaIsTransparency = true; i.mipmapEnabled = true; i.wrapMode = TextureWrapMode.Clamp; });
            var seaPath = TexDir + "/sea_normal.png";
            if (!File.Exists(seaPath)) WriteSeaNormal(seaPath);
            SetImporter(seaPath, i => { i.textureType = TextureImporterType.NormalMap; i.wrapMode = TextureWrapMode.Repeat; });
            // Facades must repeat, but the shared parking textures are Clamp: work on repeat-wrapped copies
            // so no parking asset or its .meta is touched.
            foreach (var f in new[] { "shop_facade", "villa_facade_brick", "villa_facade_gray", "villa_facade_ivory_v29", "roof_waterproofing_aged", "concrete_pavement_diff_2k", "concrete_wall_009_diff_2k" })
                CopyRepeat(Facades + f + (f.EndsWith("2k") ? ".jpg" : ".png"));
            CopyRepeat("Assets/ParkingLotEnvironment/CloudPilot/PhotoStreetPilot/KoreanBrickFacade_Generated.png");
            CopyRepeat("Assets/ParkingLotEnvironment/CloudPilot/PhotoStreetPilot/KoreanPlasterFacade_Generated.png");
            CopyRepeat("Assets/ParkingLotEnvironment/RealismAssets/clean_asphalt_diff_2k.jpg");
            CopyRepeat("Assets/ParkingLotEnvironment/RealismAssets/clean_asphalt_nor_gl_2k.jpg");
            SetImporter(RepeatCopyPath("Assets/ParkingLotEnvironment/RealismAssets/clean_asphalt_nor_gl_2k.jpg"), i => { i.textureType = TextureImporterType.NormalMap; });
            // Poly Haven CC0 materials (GeneratedSlice/tex/ph): Korean I-shaped concrete pavers for sidewalks, rough concrete for plinths and kerbs
            foreach (var p in Directory.Exists(TexDir + "/ph") ? Directory.GetFiles(TexDir + "/ph", "*.jpg") : new string[0])
            {
                var asset = p.Replace('\\', '/');
                bool normal = asset.Contains("_nor_gl_");
                bool data = asset.Contains("_rough_");
                SetImporter(asset, i =>
                {
                    i.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    i.sRGBTexture = !normal && !data;
                    i.mipmapEnabled = true; i.anisoLevel = 8; i.wrapMode = TextureWrapMode.Repeat; i.maxTextureSize = 2048;
                    var android = i.GetPlatformTextureSettings("Android");
                    android.overridden = true; android.maxTextureSize = 1024; android.format = TextureImporterFormat.Automatic;
                    i.SetPlatformTextureSettings(android);
                });
            }
        }

        static Texture2D PolyHaven(string name, string map) => Tex(TexDir + "/ph/" + name + "_" + map + "_2k.jpg");

        static string RepeatCopyPath(string source) => TexDir + "/copy_" + Path.GetFileName(source);

        static void CopyRepeat(string source)
        {
            var dest = RepeatCopyPath(source);
            if (!File.Exists(dest)) AssetDatabase.CopyAsset(source, dest);
            SetImporter(dest, i => { i.anisoLevel = 8; i.wrapMode = TextureWrapMode.Repeat; });
        }

        static void WriteSeaNormal(string path)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            var h = new float[size * size];
            var waves = new[] { (3, 2, 1.0f, 0.3f), (5, 7, 0.6f, 1.7f), (11, 4, 0.35f, 4.1f), (2, 9, 0.5f, 2.2f), (13, 13, 0.2f, 0.7f) };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float v = 0f;
                    foreach (var (fx, fy, amp, ph) in waves)
                        v += amp * Mathf.Sin((fx * x + fy * y) * (2f * Mathf.PI / size) + ph);
                    h[y * size + x] = v;
                }
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = h[y * size + (x + 1) % size] - h[y * size + (x + size - 1) % size];
                    float dy = h[((y + 1) % size) * size + x] - h[((y + size - 1) % size) * size + x];
                    var n = new Vector3(-dx * 0.5f, -dy * 0.5f, 1f).normalized;
                    px[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            tex.SetPixels(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static Material LoadOrCreate(string key, string shaderName)
        {
            var path = Root + "/mat-" + key + ".mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new Exception("Shader not found: " + shaderName);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;
            sink.Mats[key] = m;
            return m;
        }

        static Material RoadMaterial(string key, Texture diffuse, Texture normal, Texture macro, Color tint, float macroStrength, float smooth)
        {
            var m = LoadOrCreate(key, "BusanEnvironment/SliceRoad");
            m.SetTexture("_MainTex", diffuse);
            m.SetTexture("_BumpMap", normal);
            m.SetTexture("_Macro", macro);
            m.SetColor("_Tint", tint);
            m.SetFloat("_MacroStrength", macroStrength);
            m.SetFloat("_Smooth", smooth);
            m.SetFloat("_Tile", 6f);
            m.SetFloat("_MacroTile", 96f);
            return m;
        }

        static Material Paint(string key, Color color)
        {
            var m = LoadOrCreate(key, "BusanEnvironment/SlicePaint");
            m.SetColor("_Color", color);
            m.SetFloat("_Smooth", 0.1f);
            return m;
        }

        // Vertical black-to-clear gradient (alpha 0.5 at the bottom) for the contact darkening at the foot of walls.
        static Texture2D WallAoTexture()
        {
            var path = TexDir + "/wall_ao.png";
            if (!File.Exists(path))
            {
                var t = new Texture2D(4, 64, TextureFormat.RGBA32, false);
                for (int y = 0; y < 64; y++)
                {
                    float a = 0.5f * Mathf.Pow(1f - y / 63f, 1.6f);
                    for (int x = 0; x < 4; x++) t.SetPixel(x, y, new Color(0f, 0f, 0f, a));
                }
                File.WriteAllBytes(path, t.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.wrapMode = TextureWrapMode.Clamp; imp.alphaIsTransparency = true; imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Std(string key, Color color, Texture tex = null, float smooth = 0.15f, float metallic = 0f)
        {
            var m = LoadOrCreate(key, "Standard");
            m.color = color;
            m.mainTexture = tex;
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", metallic);
            m.mainTextureScale = Vector2.one;
            return m;
        }

        static void Cutout(Material m, float cutoff)
        {
            m.SetFloat("_Mode", 1f);
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            m.SetFloat("_Cutoff", cutoff);
            m.EnableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 2450;
        }

        static Material Facade(string key, Texture tex, float smooth = 0.08f, float metallic = 0f)
        {
            var m = LoadOrCreate(key, "BusanEnvironment/FacadeTint");
            m.color = Color.white;
            m.mainTexture = tex;
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        static Texture2D Copy(string source) => Tex(RepeatCopyPath(source));

        static void CreateMaterials()
        {
            // asphalt: world-XZ shader with diffuse, normal and the 96 m macro texture; alleys get an older, patchier, warmer variant
            var asphalt = Copy("Assets/ParkingLotEnvironment/RealismAssets/clean_asphalt_diff_2k.jpg");
            var asphaltN = Tex(RepeatCopyPath("Assets/ParkingLotEnvironment/RealismAssets/clean_asphalt_nor_gl_2k.jpg"));
            var macro = Tex(TexDir + "/road_macro.png");
            RoadMaterial("road", asphalt, asphaltN, macro, new Color(0.74f, 0.74f, 0.75f), 1.0f, 0.10f);
            RoadMaterial("roadAlley", asphalt, asphaltN, macro, new Color(0.72f, 0.70f, 0.66f), 1.2f, 0.07f).SetFloat("_MacroNear", 1.3f);
            // lane paint: worn, slightly off-white and off-yellow, never a saturated decal
            Paint("white", new Color(0.80f, 0.80f, 0.77f));
            Paint("yellow", new Color(0.80f, 0.62f, 0.13f));
            var sw = LoadOrCreate("sidewalk", "BusanEnvironment/SliceSidewalk");
            var pavers = PolyHaven("concrete_pavers", "diff");
            bool usePavers = UseSumo && pavers != null;
            sw.SetTexture("_MainTex", usePavers ? pavers : Tex(TexDir + "/sidewalk_paving.png"));
            sw.SetTexture("_BumpMap", usePavers ? PolyHaven("concrete_pavers", "nor_gl") : Tex(TexDir + "/sidewalk_paving_nor.png"));
            sw.SetTexture("_Macro", macro);
            sw.SetFloat("_Tile", usePavers ? 2.4f : 1.6f);
            sw.SetColor("_Tint", usePavers ? new Color(1.15f, 1.12f, 1.10f) : new Color(0.60f, 0.60f, 0.60f));
            sw.SetColor("_SideColor", new Color(0.52f, 0.51f, 0.49f));
            sw.SetFloat("_Smooth", 0.08f);
            // kerb face: light grey concrete; the mesh normals are biased towards the sky, so the shaded face keeps its colour
            Std("kerb", new Color(0.64f, 0.63f, 0.61f), null, 0.1f);
            Std("tactile", Color.white, Tex(TexDir + "/tactile.png"), 0.15f);
            Std("manhole", new Color(0.21f, 0.21f, 0.22f), null, 0.12f, 0.05f);   // cast-iron cover (a shinier one mirrored the sky white at low angles)
            // contact darkening at the foot of building walls (shadows are off on Android, so buildings looked pasted on)
            var wallAo = LoadOrCreate("wallAO", "Unlit/Transparent");
            wallAo.mainTexture = WallAoTexture();
            Std("concrete", new Color(0.78f, 0.78f, 0.76f), Copy(Facades + "concrete_wall_009_diff_2k.jpg"), 0.08f);
            // one light grey for the whole bridge (towers, piers, deck, truss, rails) so nothing reads as a dark box
            Std("bridge", new Color(0.80f, 0.81f, 0.82f), null, 0.22f, 0f);
            Std("cable", new Color(0.80f, 0.81f, 0.84f), null, 0.2f, 0f);
            Std("fence", new Color(0.40f, 0.42f, 0.44f), null, 0.35f, 0.25f); // zinc-plated steel
            Std("lamp", new Color(0.25f, 0.26f, 0.28f), null, 0.4f, 0.5f);
            Std("plain", new Color(0.80f, 0.78f, 0.72f), null, 0.05f);
            Std("pole", new Color(0.58f, 0.58f, 0.56f), null, 0.04f);              // concrete utility pole
            Std("poleArm", new Color(0.30f, 0.31f, 0.32f), null, 0.25f, 0.3f);      // steel cross arms and transformer cans
            Std("wire", new Color(0.07f, 0.07f, 0.08f), null, 0.15f);
            Std("signalPole", new Color(0.52f, 0.54f, 0.55f), null, 0.3f, 0.4f);    // galvanised steel
            Std("signalHousing", new Color(0.12f, 0.13f, 0.14f), null, 0.3f);
            var lampMat = LoadOrCreate("signalLamps", "BusanEnvironment/SignalLamp");
            lampMat.SetFloat("_Glow", 2.2f);
            Std("signalRed", new Color(0.30f, 0.04f, 0.04f), null, 0.5f);
            Std("signalYellow", new Color(0.32f, 0.24f, 0.04f), null, 0.5f);
            Std("signalArrow", new Color(0.04f, 0.18f, 0.08f), null, 0.5f);
            var sigGreen = Std("signalGreen", new Color(0.02f, 0.20f, 0.10f), null, 0.5f);
            sigGreen.EnableKeyword("_EMISSION");
            sigGreen.SetColor("_EmissionColor", new Color(0.15f, 1.35f, 0.65f));
            Std("peOrange", new Color(0.92f, 0.38f, 0.07f), null, 0.3f);
            Std("peWhite", new Color(0.88f, 0.88f, 0.86f), null, 0.3f);
            Std("cone", new Color(0.95f, 0.32f, 0.05f), null, 0.3f);
            var dashMaterial = Std("dash", new Color(0.09f, 0.09f, 0.10f), null, 0f, 0f);
            dashMaterial.SetFloat("_SpecularHighlights", 0f);
            dashMaterial.SetFloat("_GlossyReflections", 0f);
            dashMaterial.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            dashMaterial.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            foreach (var (key, emission) in new[] { ("headlight", new Color(1.1f, 1.05f, 0.9f)), ("taillight", new Color(0.9f, 0.08f, 0.06f)) })
            {
                var lamp = Std(key, Color.black, null, 0.3f, 0f);
                lamp.EnableKeyword("_EMISSION");
                lamp.SetColor("_EmissionColor", emission);
            }
            Std("roof", new Color(0.62f, 0.62f, 0.60f), Copy(Facades + "roof_waterproofing_aged.png"), 0.08f);
            Facade("apt_g", Tex(TexDir + "/apt_ground.png"));
            Facade("apt_m", Tex(TexDir + "/apt_mid.png"));
            Facade("shop_g", Copy(Facades + "shop_facade.png"));
            Facade("villa_m", Tex(TexDir + "/villa_mid.png"));
            Facade("villa_brick", Copy(Facades + "villa_facade_brick.png"));
            Facade("villa_gray", Copy(Facades + "villa_facade_gray.png"));
            Facade("villa_ivory", Copy(Facades + "villa_facade_ivory_v29.png"));
            Facade("brick", Copy("Assets/ParkingLotEnvironment/CloudPilot/PhotoStreetPilot/KoreanBrickFacade_Generated.png"));
            Facade("plaster", Copy("Assets/ParkingLotEnvironment/CloudPilot/PhotoStreetPilot/KoreanPlasterFacade_Generated.png"));
            var plinth = Facade("plinth", PolyHaven("rough_concrete", "diff") ?? Copy(Facades + "concrete_wall_009_diff_2k.jpg"));
            plinth.color = new Color(0.66f, 0.65f, 0.62f);
            Facade("glass", Tex(TexDir + "/glass.png"), 0.8f, 0.55f);
            Std("signs", Color.white, Tex(TexDir + "/signs.png"), 0.15f);
            Std("signsv", Color.white, Tex(TexDir + "/signs_v.png"), 0.15f);
            var truss = Std("truss", new Color(0.80f, 0.81f, 0.82f), Tex(TexDir + "/truss_x.png"), 0.25f, 0f);
            Cutout(truss, 0.4f);
            var tree = LoadOrCreate("tree", "BusanEnvironment/SliceFoliage");
            tree.SetTexture("_MainTex", Tex(TexDir + "/tree_card.png"));
            tree.SetColor("_Color", new Color(0.82f, 0.88f, 0.80f));
            tree.SetFloat("_Cutoff", 0.45f);
            tree.SetFloat("_Darken", 1.3f);
            tree.SetFloat("_Sat", 2.0f);
            tree.SetFloat("_Mask", 0f);
            tree.renderQueue = 2450;

            // calm dark teal-grey; one large-scale, weak normal layer so distance never goes moire or white
            var sea = Std("sea", new Color(0.075f, 0.17f, 0.20f), null, 0.55f, 0f);
            sea.SetTexture("_BumpMap", Tex(TexDir + "/sea_normal.png"));
            sea.SetFloat("_BumpScale", 0.06f);
            sea.EnableKeyword("_NORMALMAP");
            sea.SetFloat("_SpecularHighlights", 0f);
            sea.SetFloat("_GlossyReflections", 0f);
            sea.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            sea.SetFloat("_Glossiness", 0.3f);

            var groundMat = LoadOrCreate("ground", "BusanEnvironment/SliceGround");
            groundMat.SetTexture("_Detail", Tex("Assets/ParkingLotEnvironment/KoreanAssets/grass_ground_diff_2k.jpg"));
            groundMat.SetFloat("_Gloss", 0.04f);
            AssetDatabase.SaveAssets();
        }
    }
}
