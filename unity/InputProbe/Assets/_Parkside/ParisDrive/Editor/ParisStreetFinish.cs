using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Geo=Parkside.Jeju.Editor.JejuDriveBuilder.Geo;
using Finish=Parkside.Streets.Editor.StreetFinishGeometry;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        static readonly Color EnamelGreen=new Color(.08f,.25f,.19f);
        static readonly Color EnamelBlue=new Color(.06f,.22f,.39f);
        static readonly Color Cream=new Color(.94f,.87f,.71f);
        static readonly Color Brass=new Color(.63f,.52f,.29f);
        static readonly Color[] ShopPaint=
        {
            new Color(.50f,.36f,.21f), new Color(.37f,.10f,.16f),
            new Color(.07f,.22f,.17f), new Color(.19f,.23f,.29f)
        };
        static readonly string[][] ShopWords=
        {
            new[]{"BOULANGERIE", "MAISON DU PAIN", "PÂTISSERIE", "FOURNIL"},
            new[]{"CAFÉ", "CAFÉ DU QUARTIER", "BISTROT", "BRASSERIE"},
            new[]{"FLEURISTE", "LES FLEURS", "FLEURS ET JARDIN", "L’ATELIER FLORAL"},
            new[]{"ATELIER", "PRESSE", "LIBRAIRIE", "ÉPICERIE"}
        };

        static int ParisFrontage(Vector3[] outline,ParisStreetLookup streets,bool hotel,out Road frontRoad)
        {
            int best=-1;
            float closest=float.PositiveInfinity;
            frontRoad=null;
            for(int i=0;i<outline.Length;i++)
            {
                var a=outline[i];var b=outline[(i+1)%outline.Length];
                if((b-a).sqrMagnitude<17) continue;
                var mid=(a+b)*.5f;
                var outward=Vector3.Cross(Vector3.up,(b-a).normalized);
                if(!streets.Nearest(mid,hotel?55:39,out var at,out _,out var road,
                    hotel?"Rue Jean Rey":null,outward)) continue;
                float distance=Vector3.Distance(mid,at);
                float score=distance-Mathf.Min((b-a).magnitude,24)*.10f;
                if(score>=closest) continue;
                best=i;closest=score;frontRoad=road;
            }
            return best;
        }

        static int ParisShopStyle(Retail shop,long seed)
        {
            if(shop==null) return (int)(seed%4);
            switch(shop.kind)
            {
                case "bakery": case "pastry": return 0;
                case "cafe": case "restaurant": case "bar": case "pub": case "ice_cream": case "fast_food": return 1;
                case "florist": return 2;
                default: return 3;
            }
        }

        static void ParisCafeTable(Geo mesh,Vector3 p)
        {
            p.y=-.018f;
            var wood=new Color(.37f,.24f,.15f);
            mesh.Rod(p,p+Vector3.up*.71f,.042f,EnamelGreen,8);
            mesh.Box(p+Vector3.up*.72f,new Vector3(.61f,.057f,.61f),Quaternion.Euler(0,14,0),wood);
            for(int side=-1;side<=1;side+=2)
            {
                var at=p+Vector3.right*side*.58f;
                mesh.Box(at+Vector3.up*.43f,new Vector3(.31f,.046f,.31f),Quaternion.identity,wood);
                mesh.Box(at+Vector3.up*.68f+Vector3.right*side*.13f,new Vector3(.032f,.36f,.31f),Quaternion.identity,wood);
                mesh.Rod(at+Vector3.forward*.13f,at+Vector3.forward*.13f+Vector3.up*.43f,.025f,EnamelGreen,6);
                mesh.Rod(at-Vector3.forward*.13f,at-Vector3.forward*.13f+Vector3.up*.43f,.025f,EnamelGreen,6);
            }
        }

        static void BuildParisStreetFinish(Source source,Transform root,ParisStreetLookup streets)
        {
            var group=Group("Paris_Street_Furniture_and_Enamel",root);
            var poles=new Geo();var boards=new Geo();var repair=new Geo();var lids=new Geo();
            var claimed=new List<Vector3>();
            var type=Asset<Font>("Assets/_Parkside/UI/Fonts/AstaSans-Medium.ttf");
            var repairMaterial=Surface("Paris_asphalt_repairs",Color.white,shader:"ParkingLot/RoadRepair");
            repairMaterial.mainTexture=Asset<Texture2D>(Env+"/KoreanAssets/asphalt_repair_decal.png");
            repairMaterial.SetFloat("_Opacity",.25f);
            EditorUtility.SetDirty(repairMaterial);
            Physics.SyncTransforms();

            for(int i=18;i<source.guide.Length-8;i+=15)
            {
                var p=source.guide[i];
                var f=(source.guide[i+2]-source.guide[i-2]).normalized;
                var r=Vector3.Cross(Vector3.up,f);
                float half=source.guideWidths[i]*.5f;
                Finish.RoadPatch(repair,p+r*(half*.42f),f,1.36f,4.7f);
                if(Finish.RoadPoint(p-r*(half*.56f),out var cover)) Finish.Cover(lids,cover,.32f,i%3==0);
            }

            var namedStreets=new[]
            {
                ("Rue Jean Rey",0), ("Avenue de Suffren",0), ("Quai Jacques Chirac",0),
                ("Avenue de New York",1), ("Avenue de la Bourdonnais",0),
                ("Avenue Kléber",5), ("Avenue des Champs-Élysées",6),
                ("Place de la Concorde",7), ("Place Vauban",8), ("Avenue de Tourville",8)
            };
            foreach(var (name,station) in namedStreets)
                PlaceParisStreetSign(source,streets,group,type,poles,boards,claimed,name,source.stations[station].index);

            for(int i=13;i<source.guide.Length-10;i+=21)
            {
                if(!ParisSidewalk(source,streets,claimed,i,1.32f,out var at,out var f)) continue;
                ParisLamp(poles,at,f);
                claimed.Add(at);
                var beside=at+f*2.40f;
                if((i/21)%3==0&&!Finish.RoadPoint(beside,out _)&&!streets.InBuilding(beside,.72f))
                    ParisBollard(poles,beside);
            }
            AddAvenueLamps(source,streets,poles,claimed);

            foreach(int i in source.stations.Select(s=>s.index+22))
            {
                if(!ParisSidewalk(source,streets,claimed,i,2.05f,out var p,out var f)) continue;
                MorrisColumn(group,type,poles,boards,p,f);
                claimed.Add(p);
                var bench=p+f*3.6f;
                if(!Finish.RoadPoint(bench,out _)&&!streets.InBuilding(bench,1.04f))
                    ParisBench(poles,bench,f);
            }

            SportsEntrance(streets,group,type,poles,boards);
            int letters=group.GetComponentsInChildren<TextMesh>().Length;
            if(letters<3||claimed.Count<10||repair.v.Count<40)
                throw new InvalidOperationException("Paris street furniture failed to populate.");
            Save(repair,"Paris_pavement_patches",repairMaterial,group);
            Save(lids,"Paris_iron_manhole_covers",Surface("Paris_cast_iron",Color.white,smooth:.24f),group);
            Save(poles,"Lamp_posts_bollards_columns_and_benches",Surface("Paris_painted_iron",Color.white,smooth:.32f),group,true);
            Save(boards,"Enamel_and_poster_panels",Surface("Paris_enamel",Color.white,smooth:.41f),group);
            File.WriteAllText(Path.Combine(Output,"street-finish.txt"),"StreetFurnitureLocations="+claimed.Count+
                "\nAsphaltPatchQuads="+repair.v.Count/4+"\nLettering="+letters);
        }

        static void AddAvenueLamps(Source source,ParisStreetLookup streets,Geo poles,List<Vector3> claimed)
        {
            foreach(var road in source.roads)
            {
                if(road.bridge||road.roadClass!="primary"&&road.roadClass!="secondary") continue;
                const float spacing=43f;
                float offset=22;
                for(int i=0;i<road.points.Length-1;i++)
                {
                    var a=road.points[i];
                    var delta=road.points[i+1]-a;
                    float length=delta.magnitude;
                    if(length<.1f) continue;
                    var forward=delta/length;
                    for(;offset<length;offset+=spacing)
                    {
                        if(!FindSidewalk(streets,claimed,a+forward*offset,forward,road.width,1.26f,9,out var p))
                            continue;
                        ParisLamp(poles,p,forward);
                        claimed.Add(p);
                    }
                    offset-=length;
                }
            }
        }

        static bool ParisSidewalk(Source s,ParisStreetLookup streets,List<Vector3> claimed,int i,float setback,
            out Vector3 p,out Vector3 f)
        {
            i=Mathf.Clamp(i,3,s.guide.Length-4);
            f=(s.guide[i+2]-s.guide[i-2]).normalized;
            return FindSidewalk(streets,claimed,s.guide[i],f,s.guideWidths[i],setback,2.24f,out p);
        }

        static bool FindSidewalk(ParisStreetLookup streets,List<Vector3> claimed,Vector3 centre,Vector3 heading,
            float width,float setback,float spacing,out Vector3 p)
        {
            var right=Vector3.Cross(Vector3.up,heading);
            float spacingSquared=spacing*spacing;
            for(int side=1;side>=-1;side-=2)
            {
                var candidate=centre+right*(side*(width*.5f+setback));
                if(Finish.RoadPoint(candidate,out _)||streets.InBuilding(candidate,.52f)||
                    claimed.Any(q=>(candidate-q).sqrMagnitude<spacingSquared)) continue;
                if(!streets.Nearest(candidate,16,out var closest,out _,out var road)||road.bridge||
                    Vector3.Distance(closest,candidate)<road.width*.5f+.55f) continue;
                p=candidate;
                return true;
            }
            p=default;
            return false;
        }

        static void PlaceParisStreetSign(Source s,ParisStreetLookup streets,Transform group,Font font,Geo poles,Geo boards,
            List<Vector3> claimed,string street,int hint)
        {
            var indices=Enumerable.Range(4,s.guide.Length-8).Where(i=>s.guideNames[i]==street)
                .OrderBy(i=>Math.Abs(i-(hint+16)));
            foreach(var i in indices)
                if(ParisSidewalk(s,streets,claimed,i,1.31f,out var p,out var f))
                {
                    AddStreetPlaque(p,f,street,group,font,poles,boards,claimed);
                    return;
                }

            // The named street may still be drivable when the guide uses a parallel street.
            var near=s.guide[hint];
            var candidates=s.roads.Where(r=>!r.bridge&&r.name==street)
                .SelectMany(r=>Enumerable.Range(0,r.points.Length-1).Select(i=>
                    (road:r,a:r.points[i],b:r.points[i+1])))
                .OrderBy(sg=>((sg.a+sg.b)*.5f-near).sqrMagnitude);
            foreach(var segment in candidates)
            {
                var heading=(segment.b-segment.a).normalized;
                if(heading.sqrMagnitude<.5f) continue;
                if(FindSidewalk(streets,claimed,(segment.a+segment.b)*.5f,heading,segment.road.width,
                    1.31f,2.24f,out var p))
                {
                    AddStreetPlaque(p,heading,street,group,font,poles,boards,claimed);
                    return;
                }
            }
        }

        static void AddStreetPlaque(Vector3 p,Vector3 heading,string street,Transform group,Font font,
            Geo poles,Geo boards,List<Vector3> claimed)
        {
            var at=p+Vector3.up*2.22f;
            var rotation=Quaternion.LookRotation(heading);
            poles.Rod(p,p+Vector3.up*2.56f,.055f,EnamelGreen,9);
            boards.Box(at,new Vector3(1.91f,.49f,.068f),rotation,EnamelGreen);
            boards.Box(at-heading*.039f,new Vector3(1.79f,.39f,.018f),rotation,EnamelBlue);
            Finish.Letter(group,font,street.ToUpperInvariant(),at-heading*.053f,-heading,1.71f,.26f,Cream);
            claimed.Add(p);
        }

        static void ParisLamp(Geo mesh,Vector3 p,Vector3 f)
        {
            var r=Vector3.Cross(Vector3.up,f).normalized;
            mesh.Rod(p,p+Vector3.up*.58f,.164f,EnamelGreen,9);
            var neck=p+Vector3.up*3.82f;
            mesh.Rod(p+Vector3.up*.46f,neck,.072f,EnamelGreen,9);
            var a=neck;
            for(int i=1;i<=8;i++)
            {
                float t=i/8f;
                var b=neck-r*.78f*t+Vector3.up*(.42f*Mathf.Sin(t*Mathf.PI));
                mesh.Rod(a,b,.040f,EnamelGreen,6);a=b;
            }
            var q=Quaternion.LookRotation(f);
            mesh.Box(a-Vector3.up*.14f,new Vector3(.32f,.19f,.34f),q,Cream*.83f);
            mesh.Box(a+Vector3.up*.008f,new Vector3(.42f,.10f,.42f),q,EnamelGreen);
        }

        static void ParisBollard(Geo mesh,Vector3 p)
        {
            mesh.Rod(p,p+Vector3.up*.72f,.074f,EnamelGreen,9);
            mesh.Rock(p+Vector3.up*.74f,new Vector3(.089f,.069f,.089f),.21f,EnamelGreen);
        }

        static void ParisBench(Geo mesh,Vector3 p,Vector3 f)
        {
            var r=Vector3.Cross(Vector3.up,f).normalized;
            var q=Quaternion.LookRotation(f);
            var wood=new Color(.46f,.32f,.20f);
            for(int i=-2;i<=2;i++)
                mesh.Box(p+r*i*.078f+Vector3.up*.49f,new Vector3(.069f,.044f,1.58f),q,wood);
            for(int i=0;i<4;i++)
                mesh.Box(p+r*.19f+Vector3.up*(.65f+i*.065f),new Vector3(.036f,.049f,1.58f),q,wood);
            for(int side=-1;side<=1;side+=2)
            {
                var at=p+f*side*.62f;
                mesh.Rod(at+r*.17f,at+r*.19f+Vector3.up*.90f,.038f,EnamelGreen);
                mesh.Rod(at-r*.16f,at-r*.16f+Vector3.up*.48f,.038f,EnamelGreen);
            }
        }

        static void MorrisColumn(Transform group,Font font,Geo poles,Geo boards,Vector3 p,Vector3 f)
        {
            poles.Rod(p,p+Vector3.up*.32f,.62f,EnamelGreen,18);
            poles.Rod(p+Vector3.up*.24f,p+Vector3.up*2.89f,.50f,EnamelGreen,20);
            poles.Rock(p+Vector3.up*2.89f,new Vector3(.67f,.33f,.67f),.5f,EnamelGreen);
            poles.Rod(p+Vector3.up*3.11f,p+Vector3.up*3.34f,.071f,Brass,8);
            var q=Quaternion.LookRotation(f);
            var poster=p+Vector3.up*1.63f-f*.51f;
            boards.Box(poster,new Vector3(.56f,1.56f,.025f),q,new Color(.65f,.53f,.32f));
            boards.Box(poster+Vector3.up*.19f-f*.014f,new Vector3(.49f,1.10f,.009f),q,new Color(.22f,.34f,.39f));
            Finish.Letter(group,font,"PARIS",poster+Vector3.up*.52f-f*.028f,-f,.45f,.17f,Cream);
            Finish.Letter(group,font,"EXPOSITIONS",poster-Vector3.up*.57f-f*.028f,-f,.50f,.12f,new Color(.19f,.18f,.16f));
        }
    }
}
