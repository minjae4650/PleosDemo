using UnityEngine;

namespace BusanEnvironment
{
    /// <summary>Slowly scrolls the sea normal map so the water is not a static mirror.</summary>
    public sealed class SeaDrift : MonoBehaviour
    {
        [SerializeField] private Vector2 velocity = new Vector2(0.006f, 0.004f);
        private Renderer target;
        private Material material;

        private void Start()
        {
            target = GetComponent<Renderer>();
            if (target != null) material = target.material;
        }

        private void Update()
        {
            if (material == null) return;
            material.mainTextureOffset += velocity * Time.deltaTime;
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
