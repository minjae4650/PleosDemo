using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // The generated map is saved as prefabs: one prefab per category (roads, sidewalks, walls, signals, trees, parked cars, ...)
    // in GeneratedSlice/Prefabs. The scene holds prefab instances, so a category can be opened and edited in prefab mode or
    // dropped into another scene, and a rebuild rewrites the prefab files (old ones are deleted first). Runtime objects with
    // scene references (traffic, drive assist) stay plain scene objects.
    public static partial class BusanSliceBuilder
    {
        const string PrefabDir = Root + "/Prefabs";

        static void SaveMapPrefabs(Transform root)
        {
            Directory.CreateDirectory(PrefabDir);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            int saved = 0;
            foreach (var child in root.Cast<Transform>().ToList())
            {
                bool runtime = child.GetComponentsInChildren<MonoBehaviour>(true).Any(m => m != null && !(m is CarModel));
                if (runtime) continue;
                var path = PrefabDir + "/" + Sanitize(child.name) + ".prefab";
                PrefabUtility.SaveAsPrefabAssetAndConnect(child.gameObject, path, InteractionMode.AutomatedAction);
                saved++;
            }
            Log($"PREFABS categories={saved} folder={PrefabDir}");
        }

        static string Sanitize(string name) => new string(name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
    }
}
