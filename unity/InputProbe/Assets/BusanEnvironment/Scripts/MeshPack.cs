using UnityEngine;

namespace BusanEnvironment
{
    /// <summary>A file of generated meshes, held as its sub-assets (BusanDistrictBuilder writes Landmarks/&lt;District&gt;/Mesh_near.asset and
    /// the like). The project serialises assets as text, and a mesh saved as a plain .asset would be YAML at about twice the size;
    /// this attribute keeps the whole file, sub-assets included, binary.</summary>
    [PreferBinarySerialization]
    public sealed class MeshPack : ScriptableObject
    {
        /// <summary>The meshes in this file, in the order they were added.</summary>
        public Mesh[] meshes;
    }
}
