using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Parkside.Jeju.Editor;
using UnityEngine;
using Geo=Parkside.Jeju.Editor.JejuDriveBuilder.Geo;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        const string BuildingDistrict="Paris_Stone_Blocks";

        sealed class Block
        {
            public readonly Geo background=new Geo(), masonry=new Geo(), roofs=new Geo(), trim=new Geo();
            public readonly Geo panes=new Geo(), shopGlass=new Geo(), interiors=new Geo(), ornament=new Geo(), modernWalls=new Geo();
            public Transform group;
        }

        sealed class BuildingMaterials
        {
            public readonly Material Background=Surface("ParisLimestone",Color.white,"KoreanAssets/concrete_wall_009_diff_2k.jpg",
                shader:"PARKSIDE/Paris/LimestoneFacade");
            public readonly Material Stone=Surface("Paris_masonry",Color.white,"KoreanAssets/concrete_wall_009_diff_2k.jpg",
                smooth:.15f,shader:"PARKSIDE/Paris/BuildingStone");
            public readonly Material Roof=Surface("Zinc_and_slate",Color.white,"KoreanAssets/roof_waterproofing_aged.png",.33f);
            public readonly Material Joinery=Surface("Paris_shop_sign_and_trim",Color.white,smooth:.29f);
            public readonly Material Pane=Surface("Paris_recessed_window_glass",Color.white,smooth:.77f);
            public readonly Material Glazing=Surface("Paris_showroom_glazing",new Color(.54f,.66f,.68f),
                shader:"PARKSIDE/Paris/ShopGlazing");
            public readonly Material Interior=Surface("Paris_shop_interior",Color.white,smooth:.16f);

            public void SaveBlock(Block block)
            {
                var group=block.group;
                Save(block.background,"Limestone_facades",Background,group,true);
                Save(block.masonry,"Masonry_with_openings",Stone,group,true);
                Save(block.roofs,"Mansard_and_zinc_roofs",Roof,group,true);
                Save(block.trim,"Cornices_and_balconies",Stone,group,true);
                Save(block.panes,"Recessed_upper_window_panes",Pane,group);
                Save(block.shopGlass,"Transparent_shop_glazing",Glazing,group);
                Save(block.interiors,"Shop_floors_walls_and_fittings",Interior,group);
                Save(block.modernWalls,"Hotel_windows_and_glass_balconies",Joinery,group,true);
                Save(block.ornament,"Doors_signs_and_ironwork",Joinery,group,true);
            }
        }

        sealed class BuildingCoverage
        {
            public int Buildings, Frontages, DetailedFaces, RecessedWindows, ShopUnits, MappedShops, Dormers;
            public readonly HashSet<string> SignedRoads=new HashSet<string>();

            public void Report()
            {
                if(RecessedWindows<1500||ShopUnits<80||SignedRoads.Count<12||Dormers<30)
                    throw new InvalidOperationException("Paris facade geometry coverage is too sparse.");
                File.WriteAllText(Path.Combine(Output,"facade-coverage.txt"),
                    $"BuildingOutlines={Buildings}\nRoadFacingBuildings={Frontages}\nDetailedWallFaces={DetailedFaces}\n"+
                    $"RecessedUpperWindows={RecessedWindows}\nModeledShopfronts={ShopUnits}\nRoofDormers={Dormers}\n"+
                    $"MappedRetailLabels={MappedShops}\nStreetsWithShops={SignedRoads.Count}\n"+
                    string.Join("\n",SignedRoads.OrderBy(name=>name)));
            }
        }

        static void BuildBuildings(Source source,Transform root,ParisStreetLookup streets)
        {
            var district=Group(BuildingDistrict,root);
            var materials=new BuildingMaterials();
            var font=Asset<Font>("Assets/_Parkside/UI/Fonts/AstaSans-Medium.ttf");
            var coverage=new BuildingCoverage();
            var guide=source.guide.Where((_,i)=>i%6==0).ToArray();
            var placements=source.buildings.Select(building=>(building,outline:JejuDriveBuilder.Outline(building.points)))
                .Where(item=>item.outline.Length>=3)
                .Select(item=>(item.building,item.outline,centre:item.outline.Aggregate(Vector3.zero,(sum,p)=>sum+p)/item.outline.Length))
                .GroupBy(item=>new Vector2Int(Mathf.FloorToInt(item.centre.x/135),Mathf.FloorToInt(item.centre.z/135)));
            foreach(var cell in placements)
            {
                var block=new Block{group=Group($"Paris_Block_{cell.Key.x}_{cell.Key.y}",district)};
                foreach(var item in cell)
                    BuildBuilding(block,item.building,item.outline,item.centre,guide,streets,font,coverage);
                materials.SaveBlock(block);
            }
            coverage.Report();
        }

        static void BuildBuilding(Block block,Building building,Vector3[] outline,Vector3 centre,
            Vector3[] guide,ParisStreetLookup streets,Font font,BuildingCoverage coverage)
        {
            coverage.Buildings++;
            int style=(int)(building.osmId%17);
            var stone=ParisStone[style%ParisStone.Length];
            var zinc=Color.Lerp(new Color(.31f,.35f,.38f),new Color(.54f,.54f,.51f),style/16f);
            bool hotel=IsHotel(building),sports=IsSportsBuilding(building);
            float roofHeight=hotel?.08f:sports?.40f:Mathf.Min(2.2f+(style%5)*.23f,building.height*.20f);
            roofHeight=Mathf.Min(roofHeight,building.height*.27f);
            float eave=building.height-roofHeight;
            int floors=Mathf.Clamp(building.levels,1,Mathf.Max(1,Mathf.FloorToInt(eave/1.85f)));
            float ground=floors==1?eave:Mathf.Min(eave-(floors-1)*1.68f,Mathf.Clamp(eave/floors*1.18f,2.65f,4.25f));

            Road street=null;
            int front=sports?-1:ParisFrontage(outline,streets,hotel,out street);
            bool hasShops=street!=null&&!hotel&&ParisCommercial(building,street);
            bool close=guide.Any(p=>(p-centre).sqrMagnitude<112*112);
            if(front>=0) coverage.Frontages++;
            var rim=RoofRim(outline,centre,hotel?.028f:.11f);
            var crown=RoofRim(outline,centre,hotel?.040f:.20f);
            for(int i=0;i<outline.Length;i++)
            {
                int next=(i+1)%outline.Length;
                var a=outline[i];
                var b=outline[next];
                float length=Vector3.Distance(a,b);
                if(length<.12f) continue;
                var wall=new Elevation(a,b);
                int bays=Mathf.Max(1,Mathf.RoundToInt(length/(2.34f+style%5*.23f)));
                bool streetFacing=length>=2.8f&&
                    (i==front||streets.Nearest((a+b)*.5f,34,out _,out _,out _,facing:wall.Outward));
                bool detail=streetFacing&&!sports;
                bool decorated=close&&streetFacing;
                if(hotel)
                {
                    HotelWall(block.modernWalls,a,b,eave,floors,building.name=="Pullman");
                    if(i==front) HotelEntrance(block.ornament,block.group,font,building,a,b);
                }
                else if(detail)
                {
                    int shops=BuildGroundStorey(block,building,wall,ground,bays,style,stone,i==front,
                        i==front&&hasShops&&length>=5,street,streets,font,decorated);
                    coverage.RecessedWindows+=BuildResidentialStoreys(block,wall,ground,eave,floors,bays,stone,style,decorated);
                    coverage.DetailedFaces++;
                    if(shops>0)
                    {
                        coverage.ShopUnits+=shops;
                        coverage.MappedShops+=Mathf.Min(shops,building.retailUse?.Length??0);
                        if(!string.IsNullOrWhiteSpace(street.name)) coverage.SignedRoads.Add(street.name);
                    }
                }
                else if(length>=3.1f)
                    WallFace(block.background,a,b,eave,bays,floors,stone,style*23);
                else
                    wall.Panel(block.masonry,0,0,length,eave,0,stone);

                coverage.Dormers+=RoofFace(block,wall,rim[i],rim[next],crown[i],crown[next],eave,roofHeight,bays,
                    zinc,style,decorated&&!hotel&&!sports);
            }
            JejuDriveBuilder.RoofPolygon(block.roofs,crown,building.height,zinc);
            if(close&&!hotel&&!sports&&style%3!=2) AddChimneys(block,crown,building.height,style);
        }

        static void WallFace(Geo geo,Vector3 a,Vector3 b,float height,float bays,float storeys,Color colour,int seed)
        {
            int i=geo.v.Count;
            geo.Vertex(a,new Vector2(seed,0),colour);
            geo.Vertex(b,new Vector2(seed+bays,0),colour);
            geo.Vertex(a+Vector3.up*height,new Vector2(seed,storeys),colour);
            geo.Vertex(b+Vector3.up*height,new Vector2(seed+bays,storeys),colour);
            geo.t.Add(i);geo.t.Add(i+2);geo.t.Add(i+1);
            geo.t.Add(i+1);geo.t.Add(i+2);geo.t.Add(i+3);
        }

    }
}
