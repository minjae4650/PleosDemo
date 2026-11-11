using UnityEngine;

namespace Parkside.Paris.Editor
{
    public static partial class ParisDriveBuilder
    {
        static readonly Color[] ParisStone =
        {
            new Color(.89f,.84f,.76f),new Color(.79f,.78f,.73f),new Color(.94f,.90f,.81f),
            new Color(.86f,.77f,.69f),new Color(.91f,.87f,.80f),new Color(.83f,.81f,.76f)
        };
        static readonly Color[] ParisJoinery =
        {
            new Color(.23f,.33f,.30f),new Color(.33f,.35f,.36f),new Color(.37f,.27f,.20f),
            new Color(.55f,.56f,.49f),new Color(.29f,.21f,.25f),new Color(.62f,.60f,.52f)
        };

        static bool ParisCommercial(Building building,Road road)
        {
            if(building.retailUse!=null&&building.retailUse.Length>0) return true;
            switch(building.kind)
            {
                case "retail": case "commercial": return true;
                case "church": case "chapel": case "school": case "university": case "garage":
                case "office": case "public": case "civic": case "hospital": case "museum":
                case "train_station": case "industrial": case "service": case "roof":
                case "hotel": return false;
                default:
                    return road.width>=5&&building.levels>=3&&building.osmId%7<(road.width>=8?5:3);
            }
        }

    }
}
