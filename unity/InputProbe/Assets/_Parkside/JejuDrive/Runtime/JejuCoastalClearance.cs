using UnityEngine;

namespace Parkside.Jeju
{
    /// <summary>Retains the original roadside arrangement for repeatable additive authoring.</summary>
    public sealed class JejuCoastalClearance : MonoBehaviour
    {
        public Mesh originalMesh;
        public bool hidesObject, priorActive;
    }
}
