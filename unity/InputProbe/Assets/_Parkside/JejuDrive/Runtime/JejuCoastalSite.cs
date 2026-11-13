using UnityEngine;

namespace Parkside.Jeju
{
    /// <summary>Authored scenic stop. Art and public-space layout are scene interpretations.</summary>
    public sealed class JejuCoastalSite : MonoBehaviour
    {
        public string title;
        public Vector3 centre, roadPosition, roadForward, seaDirection;
        public Quaternion orientation;
        public Vector2 halfSize;
        public Vector3 pathStart,pathEnd;
        public float pathWidth;
        public int stationIndex;
        public bool Contains(Vector3 p,float padding=0)
        {
            var local=Quaternion.Inverse(orientation)*(p-centre);
            if(Mathf.Abs(local.x)<halfSize.x+padding&&Mathf.Abs(local.z)<halfSize.y+padding)return true;
            Vector3 segment=pathEnd-pathStart,delta=p-pathStart;segment.y=delta.y=0;
            float t=Mathf.Clamp01(Vector3.Dot(delta,segment)/Mathf.Max(.001f,segment.sqrMagnitude));
            return pathWidth>0&&(delta-segment*t).sqrMagnitude<(pathWidth*.5f+padding)*(pathWidth*.5f+padding);
        }
    }
}
