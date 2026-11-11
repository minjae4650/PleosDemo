using UnityEngine;
using Geo=Parkside.Jeju.Editor.JejuDriveBuilder.Geo;
using Finish=Parkside.Streets.Editor.StreetFinishGeometry;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        static bool IsHotel(Building b)=>b.name=="Pullman"||b.name=="Hôtel Mercure";

        // The five building outlines inside the OSM Émile Anthoine parcel (way 240774660).
        static bool IsSportsBuilding(Building b)=>b.osmId==30287534||b.osmId==54031857||
            b.osmId==54108368||b.osmId==1351805020||b.osmId==54108364;

        static void HotelWall(Geo mesh,Vector3 a,Vector3 b,float height,int floors,bool pullman)
        {
            var span=b-a;
            var tangent=span.normalized;
            var outside=Vector3.Cross(Vector3.up,tangent);
            var turn=Quaternion.LookRotation(outside);
            var limestone=new Color(.76f,.75f,.70f);
            var dark=new Color(.14f,.19f,.20f);
            var mid=(a+b)*.5f;
            mesh.Face(a,b,a+Vector3.up*height,b+Vector3.up*height,limestone);
            float length=span.magnitude;
            if(length<1.55f) return;
            int bays=Mathf.Max(1,Mathf.RoundToInt(length/2.85f));
            float level=height/floors, width=length/bays;
            for(int i=0;i<floors;i++)
            {
                float y=i*level;
                for(int j=0;j<bays;j++)
                {
                    var centre=a+tangent*(width*(j+.5f))+Vector3.up*(y+level*.52f)+outside*.049f;
                    float h=level*(pullman?.77f:.62f);
                    var glass=Color.Lerp(new Color(.15f,.27f,.30f),new Color(.41f,.49f,.48f),((i*3+j*5)%11)/10f);
                    mesh.Box(centre,new Vector3(width*.83f,h,.060f),turn,dark);
                    mesh.Box(centre+outside*.038f,new Vector3(width*.78f,h-.095f,.022f),turn,glass);
                    mesh.Box(centre+outside*.065f,new Vector3(.049f,h,.05f),turn,dark);
                    if((i+2*j)%5==0)
                        mesh.Box(centre+tangent*(width*.25f)+outside*.056f,
                            new Vector3(width*.19f,h-.18f,.025f),turn,Color.Lerp(glass,limestone,.24f));
                }
                if(!pullman||i==0||length<4) continue;
                var edge=mid+Vector3.up*y+outside*.32f;
                mesh.Box(edge,new Vector3(length-.1f,.16f,.66f),turn,limestone);
                mesh.Box(edge+outside*.31f+Vector3.up*.51f,
                    new Vector3(length-.17f,.84f,.035f),turn,new Color(.34f,.43f,.44f));
                mesh.Box(edge+outside*.34f+Vector3.up*.94f,new Vector3(length-.1f,.05f,.06f),turn,dark);
            }
            mesh.Box(mid+Vector3.up*(height-.14f)+outside*.05f,
                new Vector3(length,.28f,.22f),turn,limestone);
        }

        static void HotelEntrance(Geo mesh,Transform group,Font font,Building b,Vector3 a,Vector3 end)
        {
            var line=end-a;float span=line.magnitude;
            var tangent=line/span;
            var outward=Vector3.Cross(Vector3.up,tangent);
            var q=Quaternion.LookRotation(outward);
            var centre=(a+end)*.5f;
            bool pullman=b.name=="Pullman";
            var metal=new Color(.18f,.21f,.22f);
            var glass=new Color(.26f,.38f,.40f);
            float width=Mathf.Min(11,span-.12f);
            mesh.Box(centre+outward*.19f+Vector3.up*1.51f,new Vector3(width,3.02f,.24f),q,metal);
            for(int k=-2;k<=2;k++)
            {
                var bay=centre+tangent*(width*k*.19f)+outward*.326f+Vector3.up*1.44f;
                mesh.Box(bay,new Vector3(width*.17f,2.69f,.034f),q,glass);
                mesh.Box(bay+outward*.035f+Vector3.up*.24f,
                    new Vector3(width*.17f,.045f,.033f),q,metal);
            }
            var canopy=centre+outward*.95f+Vector3.up*3.1f;
            mesh.Box(canopy,new Vector3(width,.19f,1.86f),q,metal);
            mesh.Box(canopy+outward*.72f-Vector3.up*.105f,
                new Vector3(width*.91f,.04f,.20f),q,new Color(.88f,.84f,.70f));
            if(pullman)
            {
                Finish.Letter(group,font,"PULLMAN",canopy+outward*.959f+Vector3.up*.028f,outward,width*.68f,.17f,Cream);
                Finish.Letter(group,font,"22",centre+tangent*(width*.45f)+outward*.343f+Vector3.up*2.15f,
                    outward,.30f,.21f,Cream);
            }
            else
            {
                var p=centre+tangent*(width*.35f)+outward*1.65f;
                if(Finish.RoadPoint(p,out _)) p=centre+outward*.42f;
                var plum=new Color(.31f,.16f,.33f);
                mesh.Box(p+Vector3.up*1.30f,new Vector3(.91f,2.60f,.19f),q,plum);
                Finish.Letter(group,font,"MERCURE",p+outward*.108f+Vector3.up*1.99f,outward,.80f,.16f,Color.white);
                Finish.Letter(group,font,"PARIS CENTRE",p+outward*.109f+Vector3.up*1.59f,outward,.75f,.105f,Color.white);
                Finish.Letter(group,font,"20",p+outward*.11f+Vector3.up*.43f,outward,.45f,.30f,Color.white);
            }
        }

        static void SportsEntrance(ParisStreetLookup streets,Transform group,Font font,Geo iron,Geo panels)
        {
            // Frontage on Rue Jean Rey, derived from OSM way 240774660. Local gate detail is modeled.
            var a=new Vector3(-162.08f,.19f,-292.91f);
            var b=new Vector3(-127.40f,.19f,-245.79f);
            var f=(b-a).normalized;
            var roadSide=Vector3.Cross(Vector3.up,f).normalized;
            var turn=Quaternion.LookRotation(f);
            float span=(b-a).magnitude;
            for(float d=.3f;d<span-2.6f;d+=2.6f)
            {
                if(d>12&&d<17) continue;
                var p=a+f*d;
                if(Finish.RoadPoint(p,out _)||Finish.RoadPoint(p+f*2.6f,out _)||streets.InBuilding(p,.18f)) continue;
                iron.Rod(p,p+Vector3.up*1.82f,.048f,EnamelGreen,6);
                var centre=p+f*1.30f;
                iron.Box(centre+Vector3.up*.12f,new Vector3(.19f,.24f,2.6f),turn,new Color(.52f,.53f,.49f));
                foreach(float y in new[]{.29f,1.59f})
                    iron.Box(centre+Vector3.up*y,new Vector3(.047f,.042f,2.60f),turn,EnamelGreen);
                for(float x=.15f;x<2.57f;x+=.18f)
                    iron.Rod(p+f*x+Vector3.up*.24f,p+f*x+Vector3.up*1.74f,.014f,EnamelGreen,5);
            }
            var gate=a+f*12.9f+roadSide*.11f;
            if(Finish.RoadPoint(gate,out _)) return;
            for(int side=-1;side<=1;side+=2)
            {
                var post=gate+f*(side*.88f)-roadSide*.055f;
                iron.Rod(post,post+Vector3.up*1.94f,.043f,EnamelGreen,7);
                iron.Box(post+Vector3.up*.023f,new Vector3(.13f,.052f,.14f),turn,new Color(.40f,.41f,.39f));
            }
            var label=gate+Vector3.up*1.66f;
            panels.Box(label,new Vector3(.13f,.77f,2.25f),turn,EnamelBlue);
            Finish.Letter(group,font,"CENTRE SPORTIF",label+roadSide*.074f+Vector3.up*.14f,
                roadSide,2.08f,.19f,Cream);
            Finish.Letter(group,font,"ÉMILE ANTHOINE",label+roadSide*.074f-Vector3.up*.15f,
                roadSide,2.08f,.23f,Cream);
        }
    }
}
