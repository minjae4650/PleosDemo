using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Parkside.Jeju.Editor
{
 public static partial class JejuDriveBuilder
 {
    static Material bark,needles,palmLeaf,grassBlade,plume,forestCard,wallStone,windowGlass,plaster,fruit,roadPaint,lampMaterial,footpath;
    static GameObject broadTree,shrubPrefab,cedarPrefab,palmPrefab;
    static float[,] originalTerrain;
    static readonly List<RoadSource> detailRoads=new List<RoadSource>();
    static readonly Dictionary<Vector2Int,List<SegmentRef>> roadGrid=new Dictionary<Vector2Int,List<SegmentRef>>();
    static readonly List<Bounds> buildingBounds=new List<Bounds>();
    static Vector3[] signalCentres;
    static RoadSource renderingBranch;
    static int vegetationCount,shrubCount,grassCount,stoneCount,buildingCount;
    sealed class RoadSource {public string name;public Vector3[] p;public float[] w;public Row[] h;public int ordinal;}
    struct SegmentRef {public RoadSource road;public int i;}
    struct GroundSample {public RoadSource road;public int i;public float t,d,lateral;public Vector3 centre,right,forward;}

    static Material CopyMat(string name,string source)
    {
        var copy=new Material(Load<Material>(source)){name=name,enableInstancing=true};
        return SaveMaterial(copy,name);
    }
    static Material SaveMaterial(Material m,string name)
    {
        string path=Folder+"/Generated/Materials/"+name+".mat";var previous=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(previous!=null){EditorUtility.CopySerialized(m,previous);Object.DestroyImmediate(m);EditorUtility.SetDirty(previous);return previous;}
        AssetDatabase.CreateAsset(m,path);return m;
    }
    static void PrepareJejuDetails()
    {
        vegetationCount=shrubCount=grassCount=stoneCount=buildingCount=0;buildingBounds.Clear();
        signalCentres=data.signals?.Select(s=>data.points[Mathf.Clamp(s.index,0,data.points.Length-1)]).ToArray()??Array.Empty<Vector3>();
        PrepareRoadLookup();
        originalTerrain=new float[data.terrainResolution,data.terrainResolution];
        var bytes=File.ReadAllBytes(Folder+"/Data/TerrainOriginal.bytes");Buffer.BlockCopy(bytes,0,originalTerrain,0,bytes.Length);
        wallStone=Material("VolcanicDryStone",new Color(.57f,.59f,.56f),.04f);
        wallStone.mainTexture=Load<Texture2D>(Environment+"/RealismAssets/clean_asphalt_diff_2k.jpg");
        wallStone.SetTexture("_BumpMap",Load<Texture2D>(Environment+"/RealismAssets/clean_asphalt_nor_gl_2k.jpg"));wallStone.EnableKeyword("_NORMALMAP");wallStone.SetFloat("_BumpScale",.8f);
        bark=CopyMat("JejuTreeBark",Environment+"/RealVehicles/tree-small-real/Materials/002_tree_small_02_trunk.mat");
        needles=Material("CedarFoliage",new Color(.23f,.37f,.19f),0,0,"PARKSIDE/Jeju/PlantSurface");
        needles.mainTexture=Load<Texture2D>(Environment+"/KoreanAssets/grass_ground_diff_2k.jpg");
        palmLeaf=Material("PalmFronds",new Color(.29f,.42f,.16f),0,0,"PARKSIDE/Jeju/PlantSurface");palmLeaf.mainTexture=needles.mainTexture;
        grassBlade=Material("RoadsideGrass",new Color(.48f,.49f,.27f),0,0,"PARKSIDE/Jeju/PlantSurface");grassBlade.mainTexture=needles.mainTexture;
        plume=Material("SilverGrassPlumes",new Color(.71f,.66f,.47f),0,0,"PARKSIDE/Jeju/PlantSurface");plume.mainTexture=needles.mainTexture;
        forestCard=Material("LayeredForestCrowns",new Color(.83f,.88f,.77f),0,0,"ParkingLot/LayeredDistantForest");forestCard.mainTexture=Load<Texture2D>(Environment+"/KoreanAssets/forest_crown_cutout.png");forestCard.SetFloat("_Cutoff",.36f);
        plaster=Material("WarmCoastalPlaster",new Color(.82f,.79f,.69f));plaster.mainTexture=Load<Texture2D>(Environment+"/KoreanAssets/concrete_wall_009_diff_2k.jpg");
        footpath=Material("TexturedCoastalPavement",new Color(.71f,.71f,.66f),.05f);footpath.mainTexture=Load<Texture2D>(Environment+"/KoreanAssets/concrete_pavement_diff_2k.jpg");
        windowGlass=Material("CoastalGlazing",new Color(.12f,.23f,.27f),.84f,.25f);
        fruit=Material("CitrusOrange",new Color(.95f,.30f,.025f),.16f);
        roadPaint=Material("WornRoadPaint",new Color(.91f,.89f,.77f),.02f);
        lampMaterial=Material("SignalLens",new Color(.15f,.15f,.15f),.35f);lampMaterial.EnableKeyword("_EMISSION");
        Directory.CreateDirectory(Folder+"/Generated/Prefabs");AssetDatabase.Refresh();
        broadTree=BuildTreePrefab();shrubPrefab=BuildShrubPrefab();cedarPrefab=BuildCedarPrefab();palmPrefab=BuildPalmPrefab();
        foreach(var m in new[]{wallStone,bark,needles,palmLeaf,grassBlade,plume,forestCard,plaster,windowGlass,fruit,roadPaint,lampMaterial})EditorUtility.SetDirty(m);
        if(data.buildings!=null)foreach(var b in data.buildings){var bounds=new Bounds(b.points[0],Vector3.zero);foreach(var p in b.points)bounds.Encapsulate(p);bounds.Expand(new Vector3(4,0,4));buildingBounds.Add(bounds);}
    }
    static void PrepareRoadLookup()
    {
        detailRoads.Clear();roadGrid.Clear();
        detailRoads.Add(new RoadSource{name="한라산·예래 해안",p=data.points,w=data.widths,h=data.corridorHeights});
        if(data.branches!=null)foreach(var b in data.branches)detailRoads.Add(new RoadSource{name=b.name,p=b.points,w=b.widths,h=b.corridorHeights,ordinal=detailRoads.Count});
        foreach(var r in detailRoads)for(int i=0;i<r.p.Length-1;i++)
        {
            var cell=new Vector2Int(Mathf.FloorToInt(r.p[i].x/50),Mathf.FloorToInt(r.p[i].z/50));
            if(!roadGrid.TryGetValue(cell,out var list)){list=new List<SegmentRef>();roadGrid[cell]=list;}list.Add(new SegmentRef{road=r,i=i});
        }
    }
    static bool JunctionGap(Vector3 position)=>signalCentres.Any(p=>new Vector2(position.x-p.x,position.z-p.z).sqrMagnitude<20*20);
    static bool DuplicateBranchSegment(int index,bool marking)
    {
        if(renderingBranch==null||index>=renderingBranch.p.Length-1)return false;
        Vector3 p=(renderingBranch.p[index]+renderingBranch.p[index+1])*.5f,heading=(renderingBranch.p[index+1]-renderingBranch.p[index]).normalized;
        var cell=new Vector2Int(Mathf.FloorToInt(p.x/50),Mathf.FloorToInt(p.z/50));int current=renderingBranch.ordinal;
        for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++)
        {
            if(!roadGrid.TryGetValue(cell+new Vector2Int(x,z),out var refs))continue;
            foreach(var s in refs)
            {
                if(s.road.ordinal>=current||s.road.w[s.i]<renderingBranch.w[index]-.5f)continue;
                Vector3 delta=s.road.p[s.i+1]-s.road.p[s.i],flat=delta;flat.y=0;
                if(Mathf.Abs(Vector3.Dot(delta.normalized,heading))<.85f)continue;
                Vector3 off=p-s.road.p[s.i];off.y=0;float t=Mathf.Clamp01(Vector3.Dot(off,flat)/Mathf.Max(.001f,flat.sqrMagnitude));
                Vector3 projected=s.road.p[s.i]+delta*t;
                float limit=marking?4:Mathf.Max(.025f,(s.road.w[s.i]-renderingBranch.w[index])*.5f);
                if(new Vector2(p.x-projected.x,p.z-projected.z).sqrMagnitude<limit*limit)return true;
            }
        }
        return false;
    }
    static GroundSample ClosestRoad(float x,float z,int cells=2)
    {
        var cell=new Vector2Int(Mathf.FloorToInt(x/50),Mathf.FloorToInt(z/50));float best=float.PositiveInfinity;var result=new GroundSample{d=float.PositiveInfinity};
        for(int dz=-cells;dz<=cells;dz++)for(int dx=-cells;dx<=cells;dx++)
        {
            if(!roadGrid.TryGetValue(cell+new Vector2Int(dx,dz),out var segments))continue;
            foreach(var s in segments)
            {
                var a=s.road.p[s.i];var delta=s.road.p[s.i+1]-a;Vector3 flat=delta;flat.y=0;
                float t=Mathf.Clamp01(((x-a.x)*flat.x+(z-a.z)*flat.z)/Mathf.Max(.001f,flat.sqrMagnitude));var p=a+delta*t;
                float sq=(x-p.x)*(x-p.x)+(z-p.z)*(z-p.z);if(sq>=best)continue;best=sq;
                var right=Vector3.Cross(Vector3.up,delta).normalized;
                result=new GroundSample{road=s.road,i=s.i,t=t,d=Mathf.Sqrt(sq),centre=p,right=right,forward=delta.normalized,lateral=Vector3.Dot(new Vector3(x,p.y,z)-p,right)};
            }
        }
        return result;
    }
    static float RawHeight(float x,float z)
    {
        float fx=Mathf.Clamp01((x-data.terrainMin.x)/data.terrainSize.x)*(data.terrainResolution-1),fz=Mathf.Clamp01((z-data.terrainMin.y)/data.terrainSize.y)*(data.terrainResolution-1);
        int ix=Mathf.Min((int)fx,data.terrainResolution-2),iz=Mathf.Min((int)fz,data.terrainResolution-2);
        return Mathf.Lerp(Mathf.Lerp(originalTerrain[iz,ix],originalTerrain[iz,ix+1],fx-ix),Mathf.Lerp(originalTerrain[iz+1,ix],originalTerrain[iz+1,ix+1],fx-ix),fz-iz);
    }
    static float DetailHeight(float x,float z)
    {
        return GradedGround(new Vector3(x,BaseDetailHeight(x,z),z));
    }
    static float BaseDetailHeight(float x,float z)
    {
        var sample=ClosestRoad(x,z);
        if(sample.road==null||sample.d>85)return RawHeight(x,z);
        int col=0;while(col<data.corridorOffsets.Length-2&&data.corridorOffsets[col+1]<sample.lateral)col++;
        float t=Mathf.InverseLerp(data.corridorOffsets[col],data.corridorOffsets[col+1],sample.lateral);
        int i=sample.i;var h=sample.road.h;
        return Mathf.Lerp(Mathf.Lerp(h[col].values[i],h[col+1].values[i],t),Mathf.Lerp(h[col].values[i+1],h[col+1].values[i+1],t),sample.t);
    }
    static bool Plantable(Vector3 p,float radius=1)
    {
        if(p.y<1.5f)return false;
        if(signalCentres.Any(c=>new Vector2(c.x-p.x,c.z-p.z).sqrMagnitude<(13+radius)*(13+radius)))return false;
        var near=ClosestRoad(p.x,p.z);
        if(near.road!=null&&near.d<near.road.w[near.i]*.5f+radius+1.1f)return false;
        foreach(var b in buildingBounds)if(Mathf.Abs(p.x-b.center.x)<b.extents.x+radius&&Mathf.Abs(p.z-b.center.z)<b.extents.z+radius)return false;
        return true;
    }
    static Transform SceneryGroup(string name,Transform parent,Vector3 centre,float range)
    {
        var g=Group(name,parent,centre);g.gameObject.AddComponent<JejuSceneryChunk>().visibilityDistance=range;chunks.Add(g);return g;
    }
    static GameObject SavePrefab(GameObject rootObject,string name)
    {
        var prefab=PrefabUtility.SaveAsPrefabAsset(rootObject,Folder+"/Generated/Prefabs/"+name+".prefab");Object.DestroyImmediate(rootObject);return prefab;
    }
    static GameObject ModelRoot(string name,GameObject source,float height,Transform parent)
    {
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(source,scene);PrefabUtility.UnpackPrefabInstance(instance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        instance.name=name;instance.transform.SetParent(parent,false);instance.transform.localPosition=Vector3.zero;instance.transform.localRotation=Quaternion.identity;
        var b=Bounds(instance);instance.transform.localScale*=height/Mathf.Max(.01f,b.size.y);b=Bounds(instance);instance.transform.position-=new Vector3(b.center.x,b.min.y,b.center.z);
        foreach(var c in instance.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
        foreach(var t in instance.GetComponentsInChildren<Transform>())t.gameObject.isStatic=false;
        return instance;
    }
    static GameObject BuildTreePrefab()
    {
        var r=Group("Jeju_Broadleaf_LOD").gameObject;
        var high=ModelRoot("Near_scanned_tree",Load<GameObject>(Environment+"/RealVehicles/tree-small-real/tree-small-real.prefab"),9,r.transform);
        var medium=ModelRoot("Middle_scanned_tree",Load<GameObject>(Environment+"/RealVehicles/tree-small-far/tree-small-far.prefab"),9,r.transform);
        var leaf=Load<Material>(Environment+"/CloudPilot/Canopy/LeafDoubleSided.mat");
        var jejuLeaf=CopyMat("CanopyLeaves",Environment+"/CloudPilot/Canopy/LeafDoubleSided.mat");jejuLeaf.enableInstancing=true;jejuLeaf.color=new Color(.76f,.84f,.65f);jejuLeaf.SetFloat("_Cutoff",.24f);
        foreach(var model in new[]{high,medium})foreach(var renderer in model.GetComponentsInChildren<Renderer>())
        {if(renderer.name.Contains("leaves"))renderer.sharedMaterial=jejuLeaf;renderer.lightProbeUsage=LightProbeUsage.Off;renderer.shadowCastingMode=ShadowCastingMode.On;}
        var g=new Geo();for(int f=0;f<3;f++)g.Card(Vector3.zero,13,9,f*Mathf.PI/3,Color.white);
        var far=g.Save("Far_crown",forestCard,r.transform);
        var lod=r.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.22f,high.GetComponentsInChildren<Renderer>()),new LOD(.045f,medium.GetComponentsInChildren<Renderer>()),new LOD(.004f,new[]{far.GetComponent<Renderer>()})});lod.RecalculateBounds();
        return SavePrefab(r,"JejuBroadleaf");
    }
    static GameObject BuildShrubPrefab()
    {
        var r=Group("Jeju_Roadside_Shrub").gameObject;
        var high=ModelRoot("Scanned_shrub",Load<GameObject>("Assets/_Parkside/Lookdev/Models/shrub_01.fbx"),1.6f,r.transform);
        var m=CopyMat("PhotographedShrub","Assets/_Parkside/Lookdev/Materials/Shrub leaves.mat");
        foreach(var rr in high.GetComponentsInChildren<Renderer>())rr.sharedMaterial=m;
        var g=new Geo();for(int f=0;f<3;f++)g.Card(Vector3.zero,2.8f,1.6f,f*Mathf.PI/3,Color.white);
        var far=g.Save("Far_shrub",forestCard,r.transform);
        var lod=r.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.045f,high.GetComponentsInChildren<Renderer>()),new LOD(.002f,new[]{far.GetComponent<Renderer>()})});lod.RecalculateBounds();
        return SavePrefab(r,"JejuShrub");
    }
    static GameObject BuildCedarPrefab()
    {
        var woodland=Group("Jeju_Mixed_Woodland_Tree").gameObject;
        var scanned=(GameObject)PrefabUtility.InstantiatePrefab(broadTree,scene);scanned.transform.SetParent(woodland.transform,false);scanned.transform.localScale=new Vector3(.82f,1.4f,.82f);
        return SavePrefab(woodland,"JejuMixedWoodlandTree");
    }
    static GameObject BuildProceduralCedarStudy()
    {
        var r=Group("Jeju_Cedar").gameObject;var trunk=new Geo();var foliage=new Geo();
        trunk.Rod(Vector3.zero,new Vector3(.15f,14,0),.20f,Color.white,9);
        for(int level=0;level<15;level++)
        {
            float y=3+level*.72f,radius=(14-y)*.35f+.3f;
            for(int branch=0;branch<8;branch++)
            {
                float angle=branch*Mathf.PI/4+level*.73f;Vector3 radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Vector3 origin=new Vector3(0,y,0),tip=origin+radial*radius+Vector3.up*(.4f+level*.015f);
                trunk.Rod(origin,tip,.026f,Color.white,4);
                Vector3 side=Vector3.Cross(Vector3.up,radial);
                for(int spray=0;spray<5;spray++)
                {
                    float f=.25f+spray*.15f;Vector3 mid=Vector3.Lerp(origin,tip,f);float spread=radius*(1-f)*.55f+.15f;
                    foliage.Face(mid-side*spread-radial*.22f,mid+side*spread-radial*.22f,mid+radial*.5f+Vector3.up*.35f,mid+radial*.7f+Vector3.up*.26f,new Color(.84f+level*.008f,1,.9f));
                    foliage.Face(mid-side*spread,mid+side*spread,mid-side*spread+Vector3.up*.42f,mid+side*spread+Vector3.up*.42f,Color.white);
                }
            }
        }
        trunk.Save("Cedar_trunk",bark,r.transform,true);foliage.Save("Cedar_branch_sprays",needles,r.transform,true);
        var card=new Geo();for(int f=0;f<3;f++)card.Card(Vector3.zero,8,14,f*Mathf.PI/3,new Color(.7f,.85f,.72f));
        var distant=card.Save("Cedar_far",forestCard,r.transform);
        var near=r.GetComponentsInChildren<Renderer>().Where(x=>x.gameObject!=distant).ToArray();
        var lod=r.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.04f,near),new LOD(.004f,new[]{distant.GetComponent<Renderer>()})});lod.RecalculateBounds();return SavePrefab(r,"JejuCedar");
    }
    static GameObject BuildPalmPrefab()
    {
        var r=Group("Jeju_Coastal_Palm").gameObject;var trunk=new Geo();var fronds=new Geo();
        for(int i=0;i<19;i++)
        {
            float a=i/19f,b=(i+1)/19f;Vector3 p=new Vector3(a*a*.8f,a*8,0),q=new Vector3(b*b*.8f,b*8,0);
            trunk.Rod(p,q,.19f-a*.045f,Color.white,9);
            if(i%2==0)trunk.Rod(p-Vector3.up*.025f,p+Vector3.up*.025f,.21f-a*.045f,new Color(.7f,.65f,.5f),9);
        }
        Vector3 crown=new Vector3(.8f,8,0);
        for(int f=0;f<17;f++)
        {
            float angle=f*Mathf.PI*2/17;Vector3 dir=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));Vector3 side=Vector3.Cross(Vector3.up,dir);float len=3.6f+(f%3)*.45f;
            for(int k=0;k<14;k++)
            {
                float a=k/14f,b=(k+1)/14f;
                Vector3 p=crown+dir*(a*len)+Vector3.up*(Mathf.Sin(a*Mathf.PI)*1.3f-a*a*1.2f);
                Vector3 q=crown+dir*(b*len)+Vector3.up*(Mathf.Sin(b*Mathf.PI)*1.3f-b*b*1.2f);
                fronds.Rod(p,q,.025f,new Color(.8f,.85f,.55f),4);
                float width=Mathf.Sin(a*Mathf.PI)*.85f+.10f;
                for(int s=-1;s<=1;s+=2)
                {
                    var tip=p+side*width*s+dir*.35f-Vector3.up*(.12f+a*.18f);
                    fronds.Face(p-dir*.025f,p+dir*.025f,tip-dir*.006f,tip+dir*.006f,Color.white);
                }
            }
        }
        trunk.Save("Palm_ringed_trunk",bark,r.transform,true);fronds.Save("Palm_pinnate_fronds",palmLeaf,r.transform,true);return SavePrefab(r,"JejuPalm");
    }
    static GameObject Plant(GameObject prefab,Vector3 p,float scale,Transform parent,string name)
    {
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);go.name=name;go.transform.SetParent(parent,false);go.transform.SetPositionAndRotation(p,Quaternion.Euler(0,Range(0,360),0));go.transform.localScale=Vector3.one*scale;vegetationCount++;return go;
    }

    static void MakeBranchRoads()
    {
        if(data.branches==null)return;var parent=Group("Connected_Coastal_And_Village_Roads",root);
        foreach(var b in data.branches)
        {
            renderingBranch=detailRoads.First(r=>r.p==b.points);
            var saved=data.points;var savedWidths=data.widths;var savedDistances=data.distances;var savedHeights=data.corridorHeights;var savedOneWays=data.oneWays;
            data.points=b.points;data.widths=b.widths;data.corridorHeights=b.corridorHeights;data.oneWays=b.oneWays;
            data.distances=new float[b.points.Length];for(int i=1;i<b.points.Length;i++)data.distances[i]=data.distances[i-1]+Vector3.Distance(b.points[i-1],b.points[i]);
            var branch=Group(b.name,parent);
            for(int start=0;start<b.points.Length-1;start+=50)
            {
                int end=Mathf.Min(start+50,b.points.Length-1);var chunk=Group("Branch_"+start,branch);
                Strip("Asphalt",start,end,i=>-data.widths[i]*.5f,i=>data.widths[i]*.5f,-.012f,asphalt,chunk,true);
                Strip("White_left",start,end,i=>-data.widths[i]*.5f+.16f,i=>-data.widths[i]*.5f+.28f,.025f,white,chunk);
                Strip("White_right",start,end,i=>data.widths[i]*.5f-.28f,i=>data.widths[i]*.5f-.16f,.025f,white,chunk);
                Strip("Yellow_left",start,end,i=>-.18f,i=>-.06f,.035f,yellow,chunk);
                Strip("Yellow_right",start,end,i=>.06f,i=>.18f,.035f,yellow,chunk);
                Strip("Dashed_oneway",start,end,i=>-.07f,i=>.07f,.025f,white,chunk);
                var geo=new Geo();int cols=data.corridorOffsets.Length;
                for(int i=start;i<=end;i++)for(int j=0;j<cols;j++)
                {
                    Vector3 p=data.points[i]+Right(i)*data.corridorOffsets[j];p.y=data.corridorHeights[j].values[i]-.08f;geo.Vertex(p,new Vector2(p.x*.06f,p.z*.06f),Biome(p));
                    if(i<end&&j<cols-1){int a=(i-start)*cols+j;Quad(geo.t,a,a+1,a+cols,a+cols+1);}
                }
                geo.Save("Dense_Roadside_Ground",ground,chunk);
            }
            data.points=saved;data.widths=savedWidths;data.distances=savedDistances;data.corridorHeights=savedHeights;data.oneWays=savedOneWays;
            renderingBranch=null;
        }
    }

    static void MakeJejuScenery()
    {
        var parent=Group("Jeju_Landscape_And_Roadside",root);
        MakeActualBuildings(parent);
        int roadNumber=0;
        foreach(var road in detailRoads)
        {
            renderingBranch=roadNumber==0?null:road;
            for(int start=0;start<road.p.Length-1;start+=50)
            {
                int end=Mathf.Min(start+50,road.p.Length-1);Vector3 centre=road.p[(start+end)/2];
                var close=SceneryGroup("Roadside_"+roadNumber+"_"+start,parent,centre,620);
                var far=SceneryGroup("Forest_depth_"+roadNumber+"_"+start,parent,centre,2500);
                var stones=new Geo();var grasses=new Geo();var plumes=new Geo();var woods=new Geo();var rail=new Geo();var forest=new Geo();
                bool forestZone=centre.y>150&&centre.y<1200;
                for(int i=start;i<end;i+=2)
                {
                    if(DuplicateBranchSegment(i,true))continue;
                    Vector3 p=road.p[i],right=Vector3.Cross(Vector3.up,road.p[i+1]-p).normalized;
                    bool nearCoast=p.y<90&&p.z< -4400;
                    bool city=InSeogwipo(p);
                    for(int side=-1;side<=1;side+=2)
                    {
                        if((i-start)%(city?10:6)==0)
                        {
                            Vector3 treePos=p+right*side*(road.w[i]*.5f+(city?4.4f:Range(5,18)));treePos.y=DetailHeight(treePos.x,treePos.z);
                            if(Plantable(treePos,2.4f))Plant(forestZone&&i%12!=0?cedarPrefab:broadTree,treePos,forestZone?Range(.85f,1.25f):Range(.6f,.95f),close,forestZone?"Woodland_tree":"Village_windbreak_tree");
                        }
                        for(int j=0;j<(city?1:forestZone?4:2);j++)
                        {
                            float offset=side*(road.w[i]*.5f+Range(2.5f,forestZone?29:15));Vector3 pos=p+right*offset;pos.y=DetailHeight(pos.x,pos.z);
                            if(Plantable(pos,.9f)&&Mathf.PerlinNoise(pos.x*.08f+21,pos.z*.08f)>.26f){Plant(shrubPrefab,pos,Range(.50f,1.1f),close,"Native_understorey");shrubCount++;}
                        }
                        for(int j=0;j<(city?1:6);j++)
                        {
                            var pos=p+right*side*(road.w[i]*.5f+Range(.8f,16))+new Vector3(Range(-2,2),0,Range(-2,2));pos.y=DetailHeight(pos.x,pos.z);
                            if(!Plantable(pos,.1f))continue;Grass(grasses,pos,Range(.22f,.65f));grassCount++;
                            if(nearCoast||p.y>900){if(j%3==0)SilverGrass(plumes,pos,Range(.8f,1.4f));}
                        }
                        // Dry stone walls follow rural/coastal land parcels, with gaps at roads.
                        if(!city&&!forestZone&&i%20<16)
                        {
                            for(int k=0;k<9;k++)
                            {
                                var pos=p+right*side*(road.w[i]*.5f+3.5f)+(road.p[i+1]-p).normalized*k*1.15f;pos.y=DetailHeight(pos.x,pos.z);
                                if(!Plantable(pos,.25f))continue;
                                for(int layer=0;layer<2;layer++)
                                {
                                    Vector3 stagger=(road.p[i+1]-p).normalized*layer*.35f;
                                    stones.Rock(pos+stagger+Vector3.up*(.25f+layer*.40f),new Vector3(Range(.44f,.57f),Range(.25f,.34f),Range(.29f,.42f)),i+k+layer,new Color(Range(.84f,1.08f),1,.95f));stoneCount++;
                                }
                            }
                        }
                        if(forestZone&&side==1&&i%4==0&&!JunctionGap(p))
                        {
                            Vector3 a=p+right*(road.w[i]*.5f+1),b=road.p[Mathf.Min(i+4,end)]+Vector3.Cross(Vector3.up,road.p[Mathf.Min(i+4,end)]-road.p[Mathf.Max(i,0)]).normalized*(road.w[i]*.5f+1);
                            rail.Rod(a+Vector3.up*.65f,b+Vector3.up*.65f,.085f,Color.white,5);rail.Rod(a,a+Vector3.up*.9f,.06f,Color.white,5);
                        }
                        // A second and third depth layer removes the empty-field horizon along forest roads.
                        for(int row=0;row<(city?0:forestZone?6:1);row++)
                        {
                            float offset=side*Range(25+row*40,55+row*55);var pos=p+right*offset+new Vector3(Range(-8,8),0,Range(-8,8));pos.y=DetailHeight(pos.x,pos.z);
                            if(!Plantable(pos,3))continue;
                            float h=forestZone?Range(8,14):Range(4,7);float a=Range(0,Mathf.PI);
                            for(int face=0;face<3;face++)forest.Card(pos-Vector3.up*.5f,h*Range(1.2f,1.7f),h,a+face*Mathf.PI/3,new Color(Range(.78f,1.08f),Range(.82f,1.05f),Range(.75f,.95f)));
                        }
                    }
                    if(!city&&nearCoast&&i%16==0)
                    {
                        var pos=p+right*(road.w[i]*.5f+7);pos.y=DetailHeight(pos.x,pos.z);
                        if(Plantable(pos,2))Plant(palmPrefab,pos,Range(.8f,1.15f),close,"Coastal_palm");
                    }
                }
                stones.Save("Porous_basalt_dry_walls",wallStone,close,true);grasses.Save("Mixed_roadside_grasses",grassBlade,close,true);plumes.Save("Silver_grass_sedges",plume,close,true);
                rail.Save("W_beam_guardrail",metal,close,true);forest.Save("Layered_forest_crowns",forestCard,far);
                if(centre.y<230){MakeStreetFurniture(road,start,end,close);if(!InSeogwipo(centre)&&start%150==0&&!DuplicateBranchSegment((start+end)/2,true))MakeOrchard(road,(start+end)/2,close);}
            }
            roadNumber++;
            renderingBranch=null;
        }
        MakeCoastalBasalt(parent);
        File.WriteAllText(Path.Combine(Output,"landscape-report.txt"),"VegetationInstances="+vegetationCount+"\nScannedShrubs="+shrubCount+"\nGrassClumps="+grassCount+"\nBasaltWallStones="+stoneCount+"\nOSMBuildings="+buildingCount+"\nRoadSections="+detailRoads.Count);
    }
    static void Grass(Geo geo,Vector3 p,float h)
    {
        for(int blade=0;blade<7;blade++)
        {
            float a=blade*2.4f;var dir=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));var side=Vector3.Cross(Vector3.up,dir)*.025f;
            var bottom=p+dir*Range(.02f,.15f);var top=bottom+Vector3.up*h*Range(.65f,1.15f)+dir*h*.28f;
            geo.Face(bottom-side,bottom+side,top-side*.2f,top+side*.2f,new Color(Range(.75f,1),Range(.85f,1.05f),.78f));
        }
    }
    static void SilverGrass(Geo geo,Vector3 p,float height)
    {
        for(int stem=0;stem<3;stem++)
        {
            Vector3 top=p+new Vector3(stem*.15f-.15f,height,stem*.08f);geo.Rod(p,top,.012f,new Color(.7f,.7f,.45f),3);
            for(int tuft=0;tuft<7;tuft++)
            {
                float t=tuft/7f;Vector3 c=top-Vector3.up*t*.45f+Vector3.right*t*.14f;float w=.15f*(1-t)+.01f;
                geo.Face(c-Vector3.right*w,c+Vector3.right*w,c+Vector3.up*.13f,c+Vector3.up*.13f,Color.white);
                geo.Face(c-Vector3.forward*w,c+Vector3.forward*w,c+Vector3.up*.13f,c+Vector3.up*.13f,Color.white);
            }
        }
    }

    static void MakeOrchard(RoadSource road,int i,Transform parent)
    {
        if(road.p[i].y>160)return;Vector3 right=Vector3.Cross(Vector3.up,road.p[i+1]-road.p[i]).normalized,forward=(road.p[i+1]-road.p[i]).normalized;
        Vector3 origin=road.p[i]+right*42;var fruitGeo=new Geo();var wall=new Geo();
        for(int row=0;row<4;row++)for(int col=0;col<5;col++)
        {
            var p=origin+right*(row*5)+forward*(col*5-10);p.y=DetailHeight(p.x,p.z);if(!Plantable(p,2.5f))continue;
            Plant(broadTree,p,Range(.30f,.42f),parent,"Citrus_orchard_tree");
            for(int f=0;f<7;f++){float a=f*2.4f;var fp=p+new Vector3(Mathf.Sin(a)*1.1f,Range(1.5f,2.5f),Mathf.Cos(a)*1.1f);fruitGeo.Rock(fp,Vector3.one*.10f,f,Color.white);}
        }
        for(int x=0;x<24;x++)
        {Vector3 p=origin-right*3+forward*(x-12);p.y=DetailHeight(p.x,p.z);wall.Rock(p+Vector3.up*.4f,new Vector3(.55f,.42f,.4f),x,Color.white);}
        fruitGeo.Save("Visible_mandarins",fruit,parent,true);wall.Save("Orchard_basalt_boundary",wallStone,parent,true);
    }
    static void MakeStreetFurniture(RoadSource road,int start,int end,Transform parent)
    {
        var kerbs=new Geo();var paving=new Geo();var poles=new Geo();var wires=new Geo();
        Vector3 last=Vector3.zero;bool haveLast=false;
        for(int i=start;i<end;i+=4)
        {
            if(DuplicateBranchSegment(i,true)||JunctionGap(road.p[i]))continue;
            Vector3 p=road.p[i],forward=(road.p[Mathf.Min(i+1,road.p.Length-1)]-p).normalized,right=Vector3.Cross(Vector3.up,forward).normalized;
            bool city=InSeogwipo(p);
            bool village=p.y<150;
            if(village)
            {
                for(int side=-1;side<=1;side+=2)
                {
                    int next=Mathf.Min(i+4,end);Vector3 nextForward=(road.p[Mathf.Min(next+1,road.p.Length-1)]-road.p[Mathf.Max(0,next-1)]).normalized;
                    Vector3 nextRight=Vector3.Cross(Vector3.up,nextForward).normalized;
                    Vector3 a=p+right*side*(road.w[i]*.5f+.22f),b=road.p[next]+nextRight*side*(road.w[next]*.5f+.22f);
                    Vector3 c=p+right*side*(road.w[i]*.5f+2.05f),d=road.p[next]+nextRight*side*(road.w[next]*.5f+2.05f);
                    var crossing=ClosestRoad(c.x,c.z);if(crossing.road!=null&&crossing.d<crossing.road.w[crossing.i]*.5f-.1f)continue;
                    a.y=c.y=p.y+.10f;b.y=d.y=road.p[next].y+.10f;
                    kerbs.Box((a+b)*.5f-Vector3.up*.04f,new Vector3(.18f,.18f,Vector3.Distance(a,b)),Quaternion.LookRotation(b-a),Color.white);
                    if(side>0)paving.Face(a,c,b,d,Color.white);else paving.Face(c,a,d,b,Color.white);
                    var lowerC=c;var lowerD=d;lowerC.y=DetailHeight(c.x,c.z)-.03f;lowerD.y=DetailHeight(d.x,d.z)-.03f;
                    paving.Face(lowerC,lowerD,c,d,Color.white);paving.Face(lowerD,lowerC,d,c,Color.white);
                }
            }
            if(i%16==0)
            {
                Vector3 a=p+right*(road.w[i]*.5f+3);a.y=DetailHeight(a.x,a.z);if(!Plantable(a,.3f))continue;
                poles.Rod(a,a+Vector3.up*7,.11f,Color.white,7);poles.Rod(a+Vector3.up*6.7f,a+Vector3.up*6.8f-right*1.4f,.07f,Color.white,6);
                poles.Box(a+Vector3.up*6.8f-right*1.5f,new Vector3(.38f,.11f,.85f),Quaternion.LookRotation(forward),Color.white);
                if(!city&&haveLast)for(int line=0;line<2;line++)for(int s=0;s<12;s++)
                {
                    float t=s/12f,u=(s+1)/12f;Vector3 b=Vector3.Lerp(last,a,t)+Vector3.up*(6.5f-Mathf.Sin(t*Mathf.PI)*.55f)+right*line*.22f;
                    Vector3 c=Vector3.Lerp(last,a,u)+Vector3.up*(6.5f-Mathf.Sin(u*Mathf.PI)*.55f)+right*line*.22f;wires.Rod(b,c,.012f,Color.white,3);
                }
                last=a;haveLast=true;
            }
        }
        kerbs.Save("Concrete_kerb_runs",concrete,parent,true);paving.Save("Village_sidewalks",footpath,parent);poles.Save("Street_lights_and_utility_poles",metal,parent,true);wires.Save("Catenary_utility_wires",basalt,parent);
        if(start%200==0&&road.p[start].y<130)MakeBusShelter(road,Mathf.Min(start+15,end-1),parent);
    }
    static void MakeBusShelter(RoadSource road,int i,Transform parent)
    {
        var p=road.p[i];Vector3 forward=(road.p[i+1]-p).normalized,right=Vector3.Cross(Vector3.up,forward).normalized;
        p+=right*(road.w[i]*.5f+4.3f);p.y=DetailHeight(p.x,p.z);if(!Plantable(p,1.8f))return;var q=Quaternion.LookRotation(forward);
        var frame=new Geo();var glass=new Geo();var bench=new Geo();
        for(int side=-1;side<=1;side+=2){var a=p+forward*side*1.7f;frame.Rod(a,a+Vector3.up*2.5f,.055f,Color.white);}
        frame.Box(p+Vector3.up*2.5f,new Vector3(1.8f,.13f,4.1f),q,Color.white);
        glass.Box(p+right*.7f+Vector3.up*1.35f,new Vector3(.035f,2.1f,3.5f),q,Color.white);
        bench.Box(p+Vector3.up*.55f,new Vector3(.48f,.12f,2.7f),q,Color.white);
        frame.Save("Jeju_bus_shelter_frame",metal,parent,true);glass.Save("Shelter_glazing",windowGlass,parent);bench.Save("Timber_waiting_bench",bark,parent,true);
        Label("제주  BUS",p+Vector3.up*2.25f-right*.94f,.035f,parent,Quaternion.LookRotation(right));
    }
    static void MakeCoastalBasalt(Transform parent)
    {
        if(data.coastlines==null)return;int index=0;
        foreach(var line in data.coastlines)
        {
            if(line.points.Length<2)continue;Vector3 mid=line.points[line.points.Length/2];var group=SceneryGroup("Volcanic_shore_"+index++,parent,mid,3000);var rocks=new Geo();
            for(int i=0;i<line.points.Length-1;i++)
            {
                var a=line.points[i];var b=line.points[i+1];float length=Vector3.Distance(a,b);int count=Mathf.Min(150,Mathf.CeilToInt(length/4));
                for(int k=0;k<count;k++)
                {
                    Vector3 p=Vector3.Lerp(a,b,k/(float)Mathf.Max(1,count));var road=ClosestRoad(p.x,p.z,8);if(road.d>550)continue;
                    p.y=Range(.1f,1.5f);rocks.Rock(p,new Vector3(Range(2,4.5f),Range(1.1f,2.5f),Range(2,4.5f)),i+k,Color.white);
                }
            }
            rocks.Save("Basalt_lava_shoreline",wallStone,group,true);
        }
    }
 }
}
