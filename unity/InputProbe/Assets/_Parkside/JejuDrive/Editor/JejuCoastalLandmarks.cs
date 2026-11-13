using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Parkside.Jeju.Editor
{
 public static partial class JejuDriveBuilder
 {
    const string CoastFolder=Folder+"/Generated/CoastalLandmarks";
    const string CoastRoot="Coastal_Landmark_Districts";
    static Material coastStone,coastPaving,coastWood,coastMetal,coastIvory,coastTeal,coastBronze,coastDark,coastAmber,coastFlower,coastLeaf,coastFoam;
    static readonly List<JejuCoastalSite> coastSites=new List<JejuCoastalSite>();
    static readonly List<Rect> coastBuildings=new List<Rect>();
    static int coastMeshNumber;
    static string CoastOutput=>Path.Combine(Output,"coastal-landmarks");

    [MenuItem("PARKSIDE/Jeju/Upgrade coastal landmarks")]
    public static void UpgradeCoastalLandmarksMenu()
    {
        if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())ApplyCoastalLandmarks(false);
    }
    public static void UpgradeCoastalLandmarksAndVerify()
    {
        try { ApplyCoastalLandmarks(true); }
        catch(Exception e){Directory.CreateDirectory(CoastOutput);File.WriteAllText(Path.Combine(CoastOutput,"error.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void ApplyCoastalLandmarks(bool verify)
    {
        Directory.CreateDirectory(CoastOutput);
        if(SceneManager.GetActiveScene().path!=ScenePath)scene=EditorSceneManager.OpenScene(ScenePath);else scene=SceneManager.GetActiveScene();
        root=scene.GetRootGameObjects().Single(g=>g.name=="Jeju_CoastalDrive").transform;
        var car=Object.FindObjectOfType<JejuDriveController>();
        if(car==null||car.route==null)throw new InvalidOperationException("The saved Jeju driving scene is required.");
        string backup=Path.Combine(CoastOutput,"before-coastal-landmarks.unity");if(!File.Exists(backup))File.Copy(ScenePath,backup);
        foreach(var clearance in Object.FindObjectsOfType<JejuCoastalClearance>(true))
        {
            if(clearance.hidesObject)clearance.gameObject.SetActive(clearance.priorActive);
            else if(clearance.originalMesh!=null)clearance.GetComponent<MeshFilter>().sharedMesh=clearance.originalMesh;
            Object.DestroyImmediate(clearance);
        }
        var prior=root.Find(CoastRoot);if(prior!=null)Object.DestroyImmediate(prior.gameObject);
        PrepareCoastalLandmarkData();RefreshCoastalTerrain();MakeCoastalLandmarks();
        ClearCoastalFootprints();AttachCoastalStations(car);
        car.sceneryChunks=car.sceneryChunks.Where(t=>t!=null).Concat(coastSites.Select(s=>s.transform)).Distinct().ToArray();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        if(verify)
        {
            scene=EditorSceneManager.OpenScene(ScenePath);ValidateCoastalLandmarks();
            Verify();CaptureCoastalLandmarks();
            File.WriteAllText(Path.Combine(CoastOutput,"scene-validation.txt"),"PASS: saved/reopened scene, landmark road clearance, materials, 9207 existing road samples and scenic cameras.\nDate=2026-11-07");
            JejuDriveVerification.BeginPlayCheck();
        }
    }
    static void PrepareCoastalLandmarkData()
    {
        data=JsonUtility.FromJson<Source>(File.ReadAllText(Folder+"/Data/JejuRoadData.json"));PrepareRoadLookup();
        PrepareStreetGrade();
        originalTerrain=new float[data.terrainResolution,data.terrainResolution];var bytes=File.ReadAllBytes(Folder+"/Data/TerrainOriginal.bytes");Buffer.BlockCopy(bytes,0,originalTerrain,0,bytes.Length);
        font=Load<Font>("Assets/_Parkside/UI/Fonts/AstaSans-Medium.ttf");
        coastBuildings.Clear();foreach(var b in data.buildings??Array.Empty<BuildingSource>())coastBuildings.Add(Rect.MinMaxRect(b.points.Min(v=>v.x)-2,b.points.Min(v=>v.z)-2,b.points.Max(v=>v.x)+2,b.points.Max(v=>v.z)+2));
        Directory.CreateDirectory(CoastFolder+"/Meshes");Directory.CreateDirectory(CoastFolder+"/Materials");coastMeshNumber=0;coastSites.Clear();
        coastStone=CoastMaterial("LavaStone",new Color(.64f,.65f,.6f),.08f);
        coastStone.mainTexture=Load<Texture2D>(Environment+"/KoreanAssets/aerial_grass_rock_diff_2k.jpg");
        coastStone.SetTexture("_BumpMap",Load<Texture2D>(Environment+"/KoreanAssets/aerial_grass_rock_nor_gl_2k.jpg"));coastStone.EnableKeyword("_NORMALMAP");coastStone.SetFloat("_BumpScale",.6f);
        coastPaving=CoastMaterial("WarmPaving",new Color(.78f,.74f,.65f),.12f);coastPaving.mainTexture=Load<Texture2D>(Environment+"/KoreanAssets/concrete_pavement_diff_2k.jpg");
        coastWood=CoastMaterial("WeatheredTimber",new Color(.43f,.28f,.15f),.18f);
        coastMetal=CoastMaterial("CharcoalMetal",new Color(.08f,.12f,.12f),.36f,.55f);
        coastIvory=CoastMaterial("ChalkIvory",new Color(.9f,.89f,.79f),.3f);
        coastTeal=CoastMaterial("OceanEnamel",new Color(.08f,.61f,.64f),.42f,.15f);
        coastBronze=CoastMaterial("SeaPatinaBronze",new Color(.28f,.44f,.36f),.35f,.42f);
        coastDark=CoastMaterial("CarvedStoneRecess",new Color(.055f,.065f,.055f),.1f);
        coastAmber=CoastMaterial("WarmBollardLens",new Color(1,.75f,.38f),.2f);coastAmber.EnableKeyword("_EMISSION");coastAmber.SetColor("_EmissionColor",new Color(1,.58f,.2f)*1.1f);
        coastLeaf=CoastMaterial("CoastalPlantLeaves",new Color(.29f,.39f,.24f),.05f);
        coastFlower=CoastMaterial("CoastalFlowerPetals",new Color(.89f,.84f,.7f),.05f);
        coastFoam=CoastMaterial("BreakingFoam",new Color(.88f,.94f,.91f,.62f),0,0,"PARKSIDE/Jeju/CoastFoam");
        foreach(var m in new[]{coastStone,coastPaving,coastWood,coastMetal,coastIvory,coastTeal,coastBronze,coastDark,coastAmber,coastLeaf,coastFlower,coastFoam})EditorUtility.SetDirty(m);
    }
    static Material CoastMaterial(string name,Color color,float smooth=0,float metallic=0,string shader="Standard")
    {
        string path=CoastFolder+"/Materials/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null){m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}
        m.name=name;m.color=color;m.enableInstancing=true;if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",smooth);if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",metallic);return m;
    }
    static GameObject CoastSave(Geo g,string name,Material material,Transform parent)
    {
        if(g.v.Count==0)return null;var vertices=g.v.Select(v=>v-parent.position).ToList();
        string path=CoastFolder+"/Meshes/"+(coastMeshNumber++).ToString("D3")+"_"+name+".asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}mesh.Clear();mesh.name=name;
        mesh.indexFormat=vertices.Count>65534?IndexFormat.UInt32:IndexFormat.UInt16;mesh.SetVertices(vertices);mesh.SetTriangles(g.t,0);mesh.SetUVs(0,g.uv);mesh.SetColors(g.c);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.UploadMeshData(false);EditorUtility.SetDirty(mesh);
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;return go;
    }
    static Vector3 Geographic(float lat,float lon)=>new Vector3((lon-126.46f)*111195.08f*Mathf.Cos(33.29f*Mathf.Deg2Rad),0,(lat-33.29f)*111195.08f);
    static Vector3 SeaNormal(Vector3 p,Vector3 right)
    {
        Vector3 nearest=Vector3.zero;float best=float.PositiveInfinity;
        foreach(var line in data.coastlines)foreach(var v in line.points){float d=(new Vector2(v.x-p.x,v.z-p.z)).sqrMagnitude;if(d<best){best=d;nearest=v;}}
        return Vector3.Dot(nearest-p,right)>=0?right:-right;
    }
    static JejuCoastalSite FindCoastSite(string title,float lat,float lon,Transform parent)
    {
        Vector3 target=Geographic(lat,lon);var near=ClosestRoad(target.x,target.z,300);
        if(near.road==null)throw new InvalidOperationException("No approach road for "+title);
        var road=near.road;float best=float.PositiveInfinity;JejuCoastalSite chosen=null;int chosenIndex=0;
        var go=Group(title,parent);var site=go.gameObject.AddComponent<JejuCoastalSite>();
        foreach(int shift in new[]{0,-5,5,-10,10,-18,18,-25,25})foreach(float offset in new[]{28f,35f,43f})
        {
            int i=Mathf.Clamp(near.i+shift,14,road.p.Length-2);Vector3 forward=(road.p[i+1]-road.p[i-1]);forward.y=0;forward.Normalize();
            Vector3 right=Vector3.Cross(Vector3.up,forward),sea=SeaNormal(road.p[i],right),centre=road.p[i]+sea*offset;Quaternion q=Quaternion.LookRotation(forward);
            bool clear=true;float lowest=float.PositiveInfinity,highest=float.NegativeInfinity;
            for(float z=-23;z<=23;z+=4.6f)for(float x=-10;x<=10;x+=5)
            {
                Vector3 p=centre+q*new Vector3(x,0,z);var sample=ClosestRoad(p.x,p.z);
                if(sample.road!=null&&sample.d<sample.road.w[sample.i]*.5f+2)clear=false;
                if(coastBuildings.Any(b=>b.Contains(new Vector2(p.x,p.z))))clear=false;
                float h=DetailHeight(p.x,p.z);lowest=Mathf.Min(lowest,h);highest=Mathf.Max(highest,h);
            }
            if(!clear||highest-lowest>8)continue;
            float score=Mathf.Abs(shift)*.7f+offset*.3f+(highest-lowest)*2;
            if(score>=best)continue;best=score;chosen=site;chosenIndex=i;site.centre=centre;site.centre.y=highest+.55f;site.orientation=q;site.seaDirection=sea;site.halfSize=new Vector2(10,23);
        }
        if(chosen==null)throw new InvalidOperationException("No road-clear scenic footprint for "+title);
        int start=Mathf.Max(0,chosenIndex-12);Vector3 f=(road.p[start+1]-road.p[start]).normalized;
        site.title=title;site.roadPosition=road.p[start]+Vector3.Cross(Vector3.up,f)*Mathf.Min(1.8f,road.w[start]*.23f);site.roadForward=f;
        site.transform.position=site.centre;site.gameObject.AddComponent<JejuSceneryChunk>().visibilityDistance=1600;coastSites.Add(site);return site;
    }
    static void MakeCoastalLandmarks()
    {
        var parent=Group(CoastRoot,root);
        var wind=FindCoastSite("예래 · 바람의 정원",33.2380f,126.3790f,parent);MakeCoastPlaza(wind,0);
        var haenyeo=FindCoastSite("논짓물 · 해녀의 뜰",33.23725f,126.38949f,parent);MakeCoastPlaza(haenyeo,1);
        var columns=FindCoastSite("대포 · 주상절리 진입정원",33.23834f,126.42586f,parent);MakeCoastPlaza(columns,2);
        var wave=FindCoastSite("월평 · 바다를 담은 마당",33.2340f,126.4877f,parent);MakeCoastPlaza(wave,3);
        MakeActualCoastalColumns(parent);
    }
    static void MakeCoastPlaza(JejuCoastalSite s,int kind)
    {
        var paving=new Geo();var stone=new Geo();var wood=new Geo();var metalwork=new Geo();var lights=new Geo();var leaves=new Geo();var petals=new Geo();
        Vector3 centre=s.centre;var q=s.orientation;Func<float,float,float,Vector3> P=(x,y,z)=>centre+q*new Vector3(x,y,z);
        paving.Box(P(0,-.22f,0),new Vector3(19,.44f,44),q,Color.white);
        // A stepped plinth meets the terrain instead of floating on sloping DEM.
        for(int side=-1;side<=1;side+=2)for(int i=0;i<22;i++)
        {
            var p=P(side*9.25f,0,-21+i*2);float foundationGround=DetailHeight(p.x,p.z);float h=Mathf.Max(.5f,centre.y-foundationGround);
            stone.Box(new Vector3(p.x,centre.y-h*.5f-.2f,p.z),new Vector3(.65f,h+.15f,1.92f),q,new Color(.9f,.94f,.88f));
        }
        for(int i=0;i<45;i++)
        {
            var p=P(0,.016f,-22+i);paving.Box(p,new Vector3(18.9f,.02f,.025f),q,new Color(.82f,.8f,.73f));
        }
        float seaSide=Vector3.Dot(s.seaDirection,q*Vector3.right)>0?1:-1;
        for(int i=0;i<=22;i++)
        {
            var a=P(seaSide*9,.1f,-22+i*2);metalwork.Rod(a,a+Vector3.up*1.12f,.032f,Color.white,8);
            if(i<22)for(int rail=0;rail<3;rail++)metalwork.Rod(a+Vector3.up*(.3f+rail*.36f),P(seaSide*9,.4f+rail*.36f,-20+i*2),.018f,Color.white,6);
        }
        for(int side=-1;side<=1;side+=2)for(int z=-15;z<=15;z+=10)
        {
            var p=P(side*7,.08f,z);var facing=Quaternion.LookRotation(-s.seaDirection);
            Bench(wood,metalwork,p,facing);
            var lamp=P(side*8.25f,.02f,z+3);metalwork.Box(lamp+Vector3.up*.4f,new Vector3(.16f,.8f,.16f),q,Color.white);lights.Box(lamp+Vector3.up*.78f,new Vector3(.19f,.08f,.19f),q,Color.white);
        }
        for(int z=-20;z<=20;z+=8)for(int side=-1;side<=1;side+=2)
        {
            var p=P(side*7.8f,.05f,z);stone.Box(p+Vector3.up*.25f,new Vector3(1.4f,.5f,3.6f),q,Color.white);
            FlowerBed(leaves,petals,p+Vector3.up*.52f,q,z+side*100);
        }
        Pergola(wood,metalwork,P(seaSide*3,.05f,-14),q);
        var face=Quaternion.LookRotation(-s.seaDirection);
        var art=P(0,.08f,5);
        stone.Box(art+Vector3.up*.22f,new Vector3(8,.44f,6),face,Color.white);
        if(kind==0)
        {
            var teal=new Geo();var ivory=new Geo();WindRing(teal,art+Vector3.up*.45f,face);CoastSave(teal,"Wind_frame",coastTeal,s.transform);
            CoastText("JEJU",art+face*new Vector3(0,.45f,1),face,1.65f,coastIvory,s.transform);
            DolHareubang(stone,metalwork,art+face*new Vector3(-5,0,0),face,1.1f);
            DolHareubang(stone,metalwork,art+face*new Vector3(5,0,0),face,1.1f);
        }
        else if(kind==1)
        {
            var bronze=new Geo();var buoy=new Geo();Haenyeo(bronze,buoy,art+Vector3.up*.45f,face,1.45f);
            CoastSave(bronze,"Haenyeo_bronze_and_tewak",coastBronze,s.transform);CoastSave(buoy,"Tewak_float",coastTeal,s.transform);
            DolHareubang(stone,metalwork,P(-5,.08f,15),face,.85f);DolHareubang(stone,metalwork,P(5,.08f,15),face,.85f);
        }
        else if(kind==2)
        {
            for(int i=0;i<25;i++)
            {
                float x=(i%5-2)*1.08f,z=(i/5-2)*1.04f,h=1.6f+2.5f*Mathf.PerlinNoise(i*.31f,3.9f);
                HexColumn(stone,art+face*new Vector3(x,.45f,z),.59f,h,face,i);
            }
            CoastText("돌과 바다",P(0,1.05f,12),face,.7f,coastIvory,s.transform);
        }
        else
        {
            var enamel=new Geo();var foam=new Geo();WaveArt(enamel,foam,art+Vector3.up*.45f,face);
            CoastSave(enamel,"Ocean_wave_sculpture",coastTeal,s.transform);CoastSave(foam,"Wave_crown",coastIvory,s.transform);
            CoastText("JEJU COAST",P(0,1.2f,12),face,.65f,coastIvory,s.transform);
        }
        var plaque=P(-seaSide*7,.7f,-3);stone.Box(plaque,new Vector3(.3f,1.4f,3.2f),q,Color.white);
        CoastText(s.title,plaque-s.seaDirection*.2f+Vector3.up*.25f,face,.3f,coastIvory,s.transform);
        CoastText("바람 · 돌 · 바다",plaque-s.seaDirection*.21f-Vector3.up*.28f,face,.16f,coastIvory,s.transform);
        // Accessible-looking approach stairs remain beyond the asphalt and its shoulder.
        Vector3 roadward=-s.seaDirection;var approach=centre+roadward*13;
        var nearby=ClosestRoad(approach.x,approach.z);float floor=nearby.road!=null?nearby.centre.y:DetailHeight(approach.x,approach.z);
        for(int i=0;i<8;i++)
        {
            float t=i/7f;var p=centre+roadward*(10+i*.65f);float top=Mathf.Lerp(centre.y,floor+.08f,t);
            paving.Box(new Vector3(p.x,top-.22f,p.z),new Vector3(4,.44f,.7f),Quaternion.LookRotation(roadward),Color.white);
        }
        var approachRoad=ClosestRoad(centre.x,centre.z);
        s.pathStart=approachRoad.centre+s.seaDirection*(approachRoad.road.w[approachRoad.i]*.5f+1.5f);
        s.pathEnd=centre+roadward*14.55f;s.pathWidth=3.4f;
        int pathSteps=Mathf.Max(2,Mathf.CeilToInt(Vector3.Distance(s.pathStart,s.pathEnd)));
        var pathRight=Vector3.Cross(Vector3.up,s.pathEnd-s.pathStart).normalized*1.7f;
        for(int i=0;i<pathSteps;i++)
        {
            var a=Vector3.Lerp(s.pathStart,s.pathEnd,i/(float)pathSteps);var b=Vector3.Lerp(s.pathStart,s.pathEnd,(i+1)/(float)pathSteps);
            a.y=DetailHeight(a.x,a.z)+.15f;b.y=DetailHeight(b.x,b.z)+.15f;
            paving.Face(a-pathRight,a+pathRight,b-pathRight,b+pathRight,Color.white);
        }
        CoastSave(paving,"Paving_terrace_and_steps",coastPaving,s.transform);CoastSave(stone,"Basalt_plinths_and_carvings",coastStone,s.transform);
        CoastSave(wood,"Timber_furniture_and_pergola",coastWood,s.transform);CoastSave(metalwork,"Railings_and_carved_details",coastMetal,s.transform);CoastSave(lights,"Warm_path_bollards",coastAmber,s.transform);
        CoastSave(leaves,"Planter_grasses",coastLeaf,s.transform);CoastSave(petals,"Coastal_garden_flowers",coastFlower,s.transform);
    }
    static void Bench(Geo wood,Geo metal,Vector3 p,Quaternion q)
    {
        for(int i=0;i<5;i++)wood.Box(p+q*new Vector3(0,.48f,-.28f+i*.14f),new Vector3(2.6f,.09f,.11f),q,Color.white);
        for(int i=0;i<3;i++)wood.Box(p+q*new Vector3(0,.75f+i*.13f,-.36f),new Vector3(2.6f,.1f,.07f),q,Color.white);
        foreach(float x in new[]{-.92f,.92f}){metal.Box(p+q*new Vector3(x,.24f,0),new Vector3(.09f,.48f,.6f),q,Color.white);metal.Rod(p+q*new Vector3(x,.45f,-.36f),p+q*new Vector3(x,1.08f,-.36f),.035f,Color.white);}
    }
    static void Pergola(Geo wood,Geo metal,Vector3 p,Quaternion q)
    {
        foreach(float x in new[]{-3f,3f})foreach(float z in new[]{-2.3f,2.3f})
        {wood.Box(p+q*new Vector3(x,1.6f,z),new Vector3(.18f,3.2f,.18f),q,Color.white);metal.Box(p+q*new Vector3(x,.12f,z),new Vector3(.25f,.24f,.25f),q,Color.white);}
        foreach(float z in new[]{-2.3f,2.3f})wood.Box(p+q*new Vector3(0,3.25f,z),new Vector3(6.7f,.24f,.18f),q,Color.white);
        for(int i=0;i<17;i++)wood.Box(p+q*new Vector3(-3.2f+i*.4f,3.45f,0),new Vector3(.1f,.15f,5.3f),q,Color.white);
    }
    static void FlowerBed(Geo leaves,Geo petals,Vector3 p,Quaternion q,int seed)
    {
        var rng=new System.Random(seed+1500);
        for(int i=0;i<24;i++)
        {
            var a=p+q*new Vector3((float)rng.NextDouble()-.5f,0,((float)rng.NextDouble()-.5f)*3);
            float h=.25f+(float)rng.NextDouble()*.25f;leaves.Rod(a,a+Vector3.up*h,.012f,Color.white,5);
            for(int k=0;k<4;k++){float angle=k*Mathf.PI*.5f;Vector3 tip=a+Vector3.up*(h*.6f)+new Vector3(Mathf.Cos(angle),.06f,Mathf.Sin(angle))*.16f;leaves.Face(a,a+Vector3.up*.06f,tip,tip+Vector3.up*.04f,Color.white);}
            for(int k=0;k<5;k++){float a0=k*Mathf.PI*.4f,a1=(k+1)*Mathf.PI*.4f;var c=a+Vector3.up*h;petals.Face(c,c+new Vector3(Mathf.Cos(a0),.02f,Mathf.Sin(a0))*.065f,c+new Vector3(Mathf.Cos(a1),.02f,Mathf.Sin(a1))*.065f,c+Vector3.up*.025f,Color.white);}
        }
    }
    static void Lathe(Geo g,Vector3 p,Quaternion q,Vector2[] profile,float depth=1,int sides=32)
    {
        int n=g.v.Count;
        foreach(var ring in profile)for(int k=0;k<=sides;k++){float a=k*Mathf.PI*2/sides;var v=new Vector3(Mathf.Cos(a)*ring.y,ring.x,Mathf.Sin(a)*ring.y*depth);g.Vertex(p+q*v,new Vector2(k/(float)sides,ring.x*.6f),Color.white);}
        for(int j=0;j<profile.Length-1;j++)for(int k=0;k<sides;k++){int a=n+j*(sides+1)+k;Quad(g.t,a,a+1,a+sides+1,a+sides+2);}
    }
    static void Ellipsoid(Geo g,Vector3 p,Quaternion q,Vector3 size,int sides=24,int rings=12)
    {
        int n=g.v.Count;
        for(int j=0;j<=rings;j++)for(int k=0;k<=sides;k++)
        {
            float a=k*Mathf.PI*2/sides,b=j*Mathf.PI/rings;g.Vertex(p+q*Vector3.Scale(size,new Vector3(Mathf.Sin(b)*Mathf.Cos(a),Mathf.Cos(b),Mathf.Sin(b)*Mathf.Sin(a))),new Vector2(k/(float)sides,j/(float)rings),Color.white);
        }
        for(int j=0;j<rings;j++)for(int k=0;k<sides;k++){int a=n+j*(sides+1)+k;Quad(g.t,a,a+sides+1,a+1,a+sides+2);}
    }
    static void DolHareubang(Geo stone,Geo recess,Vector3 p,Quaternion q,float scale)
    {
        Func<float,float,float,Vector3> P=(x,y,z)=>p+q*new Vector3(x,y,z)*scale;
        Lathe(stone,p,q,new[]{new Vector2(0,.48f*scale),new Vector2(.15f*scale,.54f*scale),new Vector2(1.1f*scale,.43f*scale),new Vector2(1.35f*scale,.35f*scale)},.82f);
        Ellipsoid(stone,P(0,1.65f,0),q,new Vector3(.43f,.51f,.37f)*scale);
        Lathe(stone,p,q,new[]{new Vector2(1.98f*scale,.48f*scale),new Vector2(2.06f*scale,.68f*scale),new Vector2(2.18f*scale,.66f*scale),new Vector2(2.26f*scale,.48f*scale),new Vector2(2.57f*scale,.3f*scale),new Vector2(2.62f*scale,0)},.95f);
        foreach(float side in new[]{-1f,1f})
        {
            Ellipsoid(stone,P(side*.21f,1.78f,.32f),q,new Vector3(.17f,.15f,.115f)*scale);
            Ellipsoid(recess,P(side*.21f,1.77f,.424f),q,new Vector3(.052f,.058f,.013f)*scale);
            stone.Rod(P(side*.07f,1.94f,.34f),P(side*.36f,1.94f,.3f),.055f*scale,Color.white,10);
            stone.Rod(P(side*.36f,1.2f,.15f),P(side*.32f,.91f,.4f),.12f*scale,Color.white,12);
            stone.Rod(P(side*.32f,.91f,.4f),P(side*.06f,side>0?1.02f:.84f,.42f),.11f*scale,Color.white,12);
            for(int finger=0;finger<4;finger++)stone.Rod(P(side*.08f,.79f+finger*.055f,.46f),P(side*.27f,.78f+finger*.055f,.45f),.023f*scale,Color.white,7);
        }
        Ellipsoid(stone,P(0,1.6f,.43f),q,new Vector3(.13f,.26f,.18f)*scale);
        recess.Rod(P(-.16f,1.32f,.34f),P(.16f,1.32f,.34f),.018f*scale,Color.white,8);
    }
    static void Haenyeo(Geo bronze,Geo buoy,Vector3 p,Quaternion q,float scale)
    {
        Func<float,float,float,Vector3> P=(x,y,z)=>p+q*new Vector3(x,y,z)*scale;
        Ellipsoid(bronze,P(0,.85f,0),q,new Vector3(.28f,.48f,.19f)*scale);
        Ellipsoid(bronze,P(0,1.49f,.02f),q,new Vector3(.19f,.23f,.18f)*scale);
        foreach(float side in new[]{-1f,1f})
        {
            bronze.Rod(P(side*.14f,.5f,.02f),P(side*.25f,.28f,.5f),.11f*scale,Color.white,12);
            bronze.Rod(P(side*.25f,.28f,.5f),P(side*.26f,.07f,.8f),.075f*scale,Color.white,12);
            Ellipsoid(bronze,P(side*.26f,.05f,.94f),q,new Vector3(.13f,.045f,.28f)*scale);
            bronze.Rod(P(side*.24f,1.17f,.02f),P(side*.38f,.93f,.3f),.072f*scale,Color.white,12);
            bronze.Rod(P(side*.38f,.93f,.3f),P(side*.17f,.85f,.56f),.06f*scale,Color.white,12);
        }
        Ellipsoid(bronze,P(0,1.52f,.19f),q,new Vector3(.16f,.055f,.025f)*scale);
        Ellipsoid(buoy,P(.46f,.45f,.34f),q,new Vector3(.28f,.28f,.28f)*scale);
        for(int i=0;i<9;i++){float a=i*Mathf.PI*2/9;bronze.Rod(P(.46f+Mathf.Cos(a)*.26f,.41f,.34f+Mathf.Sin(a)*.26f),P(.46f,.08f,.34f),.009f*scale,Color.white,5);}
        bronze.Rod(P(-.16f,1.48f,.03f),P(-.23f,1.84f,.03f),.018f*scale,Color.white,8);
    }
    static void Tube(Geo g,Vector3[] path,float radius,Quaternion q,Vector3 origin,float flatten=1)
    {
        int n=g.v.Count;const int sides=12;
        for(int i=0;i<path.Length;i++)
        {
            Vector3 tangent=(path[Mathf.Min(i+1,path.Length-1)]-path[Mathf.Max(0,i-1)]).normalized;
            var right=Vector3.Cross(Vector3.forward,tangent).normalized;
            for(int k=0;k<=sides;k++){float a=k*Mathf.PI*2/sides;var p=path[i]+right*(Mathf.Cos(a)*radius)+Vector3.forward*(Mathf.Sin(a)*radius*flatten);g.Vertex(origin+q*p,new Vector2(k/(float)sides,i*.18f),Color.white);}
        }
        for(int j=0;j<path.Length-1;j++)for(int k=0;k<sides;k++){int a=n+j*(sides+1)+k;Quad(g.t,a,a+sides+1,a+1,a+sides+2);}
    }
    static void WindRing(Geo g,Vector3 p,Quaternion q)
    {
        var curve=new Vector3[97];for(int i=0;i<curve.Length;i++){float a=i*Mathf.PI*2/96;curve[i]=new Vector3(Mathf.Cos(a)*2.5f,3+Mathf.Sin(a)*2.8f,Mathf.Sin(a*2)*.35f);}
        Tube(g,curve,.28f,q,p,1.4f);
    }
    static void WaveArt(Geo enamel,Geo foam,Vector3 p,Quaternion q)
    {
        var curve=new Vector3[70];var crest=new Vector3[45];
        for(int i=0;i<curve.Length;i++){float t=i/(float)(curve.Length-1);curve[i]=new Vector3(-3.4f+6.8f*t,.3f+4.1f*Mathf.Sin(t*Mathf.PI*.87f),Mathf.Sin(t*Mathf.PI)*.7f);}
        for(int i=0;i<crest.Length;i++){float a=i/(float)(crest.Length-1)*Mathf.PI*1.65f;crest[i]=new Vector3(1.35f+1.6f*Mathf.Sin(a),3.15f+1.35f*Mathf.Cos(a),.32f);}
        Tube(enamel,curve,.4f,q,p,.55f);Tube(enamel,crest,.23f,q,p,.8f);
        var stripe=curve.Skip(18).Take(30).Select(v=>v+new Vector3(0,.18f,.19f)).ToArray();Tube(foam,stripe,.065f,q,p);
    }
    static void HexColumn(Geo g,Vector3 p,float radius,float height,Quaternion q,int seed)
    {
        var top=new Vector3[6];var low=new Vector3[6];
        for(int k=0;k<6;k++){float a=k*Mathf.PI/3;var r=q*new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius);low[k]=p+r;top[k]=p+r*.94f+Vector3.up*height;}
        for(int k=0;k<6;k++)g.Face(low[k],low[(k+1)%6],top[k],top[(k+1)%6],new Color(.85f+(seed%4)*.045f,.93f, .86f));
        for(int k=1;k<5;k++){int n=g.v.Count;g.Vertex(top[0],Vector2.zero,Color.white);g.Vertex(top[k+1],Vector2.right,Color.white);g.Vertex(top[k],Vector2.up,Color.white);g.t.AddRange(new[]{n,n+1,n+2});}
    }
    static void CoastText(string text,Vector3 p,Quaternion facing,float height,Material material,Transform parent)
    {
        var go=new GameObject("Coast_sign_"+text);go.transform.SetParent(parent,false);go.transform.SetPositionAndRotation(p,facing);
        font.RequestCharactersInTexture(text,80,FontStyle.Normal);
        var t=go.AddComponent<TextMesh>();t.text=text;t.font=font;t.fontSize=80;t.characterSize=1;t.anchor=TextAnchor.MiddleCenter;t.alignment=TextAlignment.Center;t.color=material.color;
        var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=font.material;go.transform.localRotation=facing*Quaternion.Euler(0,180,0);
        float currentHeight=renderer.bounds.size.y;if(currentHeight>.001f)go.transform.localScale=Vector3.one*(height/currentHeight);
    }
    static void MakeActualCoastalColumns(Transform parent)
    {
        Vector3 reference=Geographic(33.23834f,126.42586f);CoastSource line=null;int closest=0;float distance=float.PositiveInfinity;
        foreach(var candidate in data.coastlines)for(int i=1;i<candidate.points.Length-1;i++)
        {float d=(candidate.points[i]-reference).sqrMagnitude;if(d<distance){distance=d;line=candidate;closest=i;}}
        if(line==null)return;var shore=line.points[closest];var distances=JejuTrafficNetwork.Distances(line.points);
        float along=distances[closest];var g=new Geo();var foam=new Geo();var group=Group("Daepo_Actual_Coastal_Column_Formations",parent,shore);
        float bestView=float.PositiveInfinity;Vector3 eye=shore+Vector3.up*35,focus=shore;
        for(int i=-48;i<=48;i++)
        {
            float d=along+i*2.1f;if(d<0||d>distances[distances.Length-1]-2.1f)continue;
            var edge=JejuTrafficNetwork.Sample(line.points,distances,d,out var tangent);var sea=Vector3.Cross(Vector3.up,tangent).normalized;
            var offshore=edge+sea*35;if(RawHeight(offshore.x,offshore.z)>1.4f)continue;
            var candidate=edge+sea*105+tangent*24+Vector3.up*32;
            if(Mathf.Abs(i)<bestView&&RawHeight(candidate.x,candidate.z)<1){bestView=Mathf.Abs(i);eye=candidate;focus=edge+Vector3.up*7;}
            var inland=edge-sea*16;float ledge=Mathf.Clamp(RawHeight(inland.x,inland.z)+2,4,30);
            for(int row=0;row<5;row++)
            {
                float noise=Mathf.PerlinNoise(i*.29f+31,row*.43f+5);
                var p=edge+sea*(1.3f+row*1.72f)+tangent*((row%2)*.75f);p.y=-1.2f;
                float top=Mathf.Max(2,ledge*(1-row*.145f)+noise*3.1f-1.6f);
                HexColumn(g,p,1.05f+noise*.33f,top+1.2f,Quaternion.Euler(0,noise*27,0),i+row*5+300);
            }
            for(int band=0;band<3;band++)
            {
                Vector3 a=edge+sea*(10+band*1.5f+Mathf.Sin(i*.37f)*.9f);a.y=.08f+band*.025f;Vector3 b=a+tangent*2.2f;
                foam.Face(a,b,a+sea*.86f,b+sea*.86f,Color.white);
            }
        }
        var view=Group("Cliff_Viewpoint",group,eye);view.LookAt(focus);
        CoastSave(g,"Daepo_hexagonal_lava_columns",coastStone,group);var f=CoastSave(foam,"Daepo_breaking_wave_foam",coastFoam,group);if(f!=null)f.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
    }
    static void ClearCoastalFootprints()
    {
        var landscape=root.Find("Jeju_Landscape_And_Roadside");if(landscape==null)return;
        foreach(var t in landscape.GetComponentsInChildren<Transform>(true))
        {
            if(!(t.name.Contains("tree")||t.name.Contains("shrub")||t.name.Contains("palm")))continue;
            if(!coastSites.Any(s=>s.Contains(t.position,2)))continue;
            var c=t.gameObject.AddComponent<JejuCoastalClearance>();c.hidesObject=true;c.priorActive=t.gameObject.activeSelf;t.gameObject.SetActive(false);
        }
        foreach(var f in landscape.GetComponentsInChildren<MeshFilter>(true))
        {
            if(!(f.name=="Mixed_roadside_grasses"||f.name=="Silver_grass_sedges"||f.name=="Porous_basalt_dry_walls"||f.name=="Layered_forest_crowns"))continue;
            var bounds=f.GetComponent<Renderer>().bounds;
            if(!coastSites.Any(s=>bounds.max.x>s.centre.x-35&&bounds.min.x<s.centre.x+35&&bounds.max.z>s.centre.z-35&&bounds.min.z<s.centre.z+35))continue;
            var mesh=f.sharedMesh;var vertices=mesh.vertices;var source=mesh.triangles;var retained=new List<int>();
            for(int i=0;i<source.Length;i+=3)
            {
                var p=f.transform.TransformPoint((vertices[source[i]]+vertices[source[i+1]]+vertices[source[i+2]])/3);
                if(!coastSites.Any(s=>s.Contains(p,1)))retained.AddRange(new[]{source[i],source[i+1],source[i+2]});
            }
            if(retained.Count==source.Length)continue;
            var clearance=f.gameObject.AddComponent<JejuCoastalClearance>();clearance.originalMesh=mesh;
            string path=CoastFolder+"/Meshes/Cleared_"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mesh))+".asset";
            var copy=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(copy==null){copy=new Mesh();AssetDatabase.CreateAsset(copy,path);}
            copy.Clear();copy.indexFormat=mesh.indexFormat;copy.vertices=vertices;copy.uv=mesh.uv;copy.colors=mesh.colors;copy.normals=mesh.normals;
            copy.SetTriangles(retained,0);copy.RecalculateBounds();copy.UploadMeshData(false);f.sharedMesh=copy;EditorUtility.SetDirty(copy);
        }
    }
    static void AttachCoastalStations(JejuDriveController car)
    {
        var stations=car.route.stations.Take(5).ToList();
        foreach(var site in coastSites)
        {site.stationIndex=stations.Count;stations.Add(new JejuStation{name=site.title,subtitle="해안 조형물 · 전망 쉼터",branchSpawn=true,position=site.roadPosition,forward=site.roadForward,index=0});}
        AttachUrbanStation(car,stations);
        car.route.stations=stations.ToArray();
    }
    static void ValidateCoastalLandmarks()
    {
        root=SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name=="Jeju_CoastalDrive").transform;
        var sites=Object.FindObjectsOfType<JejuCoastalSite>(true);if(sites.Length!=4)throw new InvalidOperationException("Expected four coastal scenic stops.");
        int vertices=0,errors=0;
        foreach(var filter in Object.FindObjectsOfType<MeshFilter>(true).Where(f=>f.transform.IsChildOf(root.Find(CoastRoot))))foreach(var v in filter.sharedMesh.vertices)
        {
            var p=filter.transform.TransformPoint(v);var near=ClosestRoad(p.x,p.z);
            if(near.road!=null&&near.d<near.road.w[near.i]*.5f+.65f){errors++;if(errors<5)Debug.LogWarning("Coastal object near road: "+filter.name+" "+p);}
            vertices++;
        }
        if(errors>0)throw new InvalidOperationException("Coastal geometry intrudes on road clearance: "+errors);
        File.WriteAllText(Path.Combine(CoastOutput,"landmark-report.txt"),"Sites="+sites.Length+"\nRoadClearanceVertices="+vertices+"\nRoadIntrusions="+errors+"\n"+string.Join("\n",sites.OrderBy(s=>s.stationIndex).Select(s=>"Key="+(s.stationIndex+1)+" "+s.title+" centre="+s.centre+" road="+s.roadPosition)));
    }
    public static void CaptureCoastalLandmarks()
    {
        var car=Object.FindObjectOfType<JejuDriveController>();var sites=Object.FindObjectsOfType<JejuCoastalSite>(true).OrderBy(s=>s.stationIndex).ToArray();
        foreach(var site in sites)
        {
            car.JumpToStation(site.stationIndex);car.cameraMode=0;car.UpdateCamera(true);site.gameObject.SetActive(true);
            Capture(car.view,Path.Combine(CoastOutput,"coast-"+(site.stationIndex-4)+"-drive.png"));
            car.view.transform.position=site.centre-site.seaDirection*28-site.orientation*Vector3.forward*32+Vector3.up*17;
            car.view.transform.LookAt(site.centre+Vector3.up*1.6f);car.view.fieldOfView=58;
            Capture(car.view,Path.Combine(CoastOutput,"coast-"+(site.stationIndex-4)+"-overview.png"));
        }
        var cliff=GameObject.Find(CoastRoot+"/Daepo_Actual_Coastal_Column_Formations");
        if(cliff==null)cliff=Object.FindObjectsOfType<Transform>(true).FirstOrDefault(t=>t.name=="Daepo_Actual_Coastal_Column_Formations")?.gameObject;
        if(cliff!=null)
        {
            var view=cliff.transform.Find("Cliff_Viewpoint");
            if(view!=null){car.view.transform.SetPositionAndRotation(view.position,view.rotation);car.view.fieldOfView=55;Capture(car.view,Path.Combine(CoastOutput,"daepo-coastal-columns.png"));}
        }
        car.JumpToStation(0);car.cameraMode=0;car.UpdateCamera(true);
    }
    static void RefreshCoastalTerrain()
    {
        var values=new float[data.terrainResolution*data.terrainResolution];var bytes=File.ReadAllBytes(Folder+"/Data/TerrainHeights.bytes");Buffer.BlockCopy(bytes,0,values,0,bytes.Length);
        int changed=0;
        foreach(var f in root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name.StartsWith("DEM_")))
        {
            var mesh=f.sharedMesh;var p=mesh.vertices;bool dirty=false;
            for(int i=0;i<p.Length;i++)
            {
                int x=Mathf.Clamp(Mathf.RoundToInt((p[i].x-data.terrainMin.x)/data.terrainSize.x*(data.terrainResolution-1)),0,data.terrainResolution-1);
                int z=Mathf.Clamp(Mathf.RoundToInt((p[i].z-data.terrainMin.y)/data.terrainSize.y*(data.terrainResolution-1)),0,data.terrainResolution-1);
                float y=values[z*data.terrainResolution+x];if(Mathf.Abs(p[i].y-y)>.001f){p[i].y=y;dirty=true;}
            }
            if(!dirty)continue;mesh.vertices=p;mesh.colors=p.Select(Biome).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.UploadMeshData(false);EditorUtility.SetDirty(mesh);changed++;
        }
        Debug.Log("Updated coastal DEM patches="+changed);
    }
 }
}
