using UnityEngine;

namespace BusanEnvironment
{
    /// <summary>
    /// A baked car prefab (one mesh, see BusanSliceVehicles): paint slot, spawn weight and brake lamps.
    /// Traffic and parked cars share these prefabs; only the paint material differs per instance.
    /// </summary>
    public sealed class CarModel : MonoBehaviour
    {
        public string modelName;
        public int triangles;
        public float weight = 1f;
        public int maxCount = 8;
        public int minCount;             // traffic: this many are always in the pool (Porter 2), whatever the random draw
        public bool lightPaintOnly;      // box truck: white or silver only (a black cargo box is not a Korean delivery truck)
        public MeshRenderer body;
        public int paintSlot;
        public bool fixedPaint;          // taxi: always white
        public GameObject brakeLamps;    // two small quads at the tail, on only while the car slows down

        public void SetPaint(Material paint)
        {
            if (body == null || paint == null) return;
            var mats = body.sharedMaterials;
            if (paintSlot >= mats.Length) return;
            mats[paintSlot] = paint;
            body.sharedMaterials = mats;
        }

        public void SetBraking(bool braking)
        {
            if (brakeLamps != null && brakeLamps.activeSelf != braking) brakeLamps.SetActive(braking);
        }
    }
}
