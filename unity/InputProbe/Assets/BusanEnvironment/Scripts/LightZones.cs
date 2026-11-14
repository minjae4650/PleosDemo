using System;
using ParkingLotEnvironment.CloudPilot;
using UnityEngine;
using UnityEngine.Rendering;

namespace BusanEnvironment
{
    /// <summary>
    /// Places the daylight does not reach (plan.md 4, the tunnel row): boxes given by a centre, the direction of their long side,
    /// half extents and a ramp length. While the main camera is inside one, the ambient light, the default reflection and the
    /// game camera's exposure are blended to the inside values (darker inside, so the exit reads bright), eased in over the ramp
    /// distance outside each end and over about 1.5 s. The outside values are the scene's own, taken when the component is
    /// enabled and put back when it is disabled. Runs after VehicleCamera has moved the camera.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class LightZones : MonoBehaviour
    {
        [Serializable]
        public sealed class Zone
        {
            public string name;
            public Vector3 centre;
            public Vector2 axis = Vector2.up;   // x, z of the long side
            public Vector3 half;                 // half extents: along, up, across
            public float ramp = 30f;             // eased in over this distance outside each end
        }

        [SerializeField] private Zone[] zones;
        [SerializeField] private float ambientInside = 0.35f;
        [SerializeField] private float reflectionInside = 0.4f;
        [SerializeField] private float exposureInside = 1.0f;
        [SerializeField] private float easeSeconds = 1.5f;

        private bool captured;
        private float ambientOutside, reflectionOutside, exposureOutside;
        private Color skyOutside, equatorOutside, groundOutside;
        private MapDaylightGrade grade;
        private Camera viewCamera;
        private float weight;

        /// <summary>How far inside (0 outside, 1 inside) the lighting is now.</summary>
        public float Weight => weight;
        public int ZoneCount => zones != null ? zones.Length : 0;

        private void OnEnable()
        {
            Capture(Camera.main);
            weight = viewCamera != null ? WeightAt(viewCamera.transform.position) : 0f;   // a start inside is dark at once
            Apply(weight);
        }

        private void OnDisable() => Restore();

        private void LateUpdate()
        {
            if (!captured) return;
            if (viewCamera == null || !viewCamera.isActiveAndEnabled) { viewCamera = Camera.main; FindGrade(); }
            float target = viewCamera != null ? WeightAt(viewCamera.transform.position) : 0f;
            weight = Mathf.MoveTowards(weight, target, Time.deltaTime / Mathf.Max(0.01f, easeSeconds));
            Apply(weight);
        }

        /// <summary>0 outside every zone, 1 inside one, eased (smooth step) over the ramp beyond each end.</summary>
        public float WeightAt(Vector3 position)
        {
            float best = 0f;
            if (zones == null) return best;
            foreach (var z in zones)
            {
                if (z == null) continue;
                var along = new Vector3(z.axis.x, 0f, z.axis.y);
                if (along.sqrMagnitude < 1e-8f) continue;
                along.Normalize();
                var across = new Vector3(along.z, 0f, -along.x);
                var d = position - z.centre;
                if (Mathf.Abs(d.y) > z.half.y || Mathf.Abs(Vector3.Dot(d, across)) > z.half.z) continue;
                float beyond = Mathf.Abs(Vector3.Dot(d, along)) - z.half.x;
                float w = beyond <= 0f ? 1f : z.ramp > 0f ? 1f - beyond / z.ramp : 0f;
                best = Mathf.Max(best, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(w)));
            }
            return best;
        }

        /// <summary>Remembers the outside values: the scene's lighting now and `cam`'s exposure (editor renders call this too).</summary>
        public void Capture(Camera cam)
        {
            if (captured) Restore();
            viewCamera = cam;
            FindGrade();
            ambientOutside = RenderSettings.ambientIntensity;
            reflectionOutside = RenderSettings.reflectionIntensity;
            skyOutside = RenderSettings.ambientSkyColor;
            equatorOutside = RenderSettings.ambientEquatorColor;
            groundOutside = RenderSettings.ambientGroundColor;
            exposureOutside = grade != null ? grade.exposureEV : 0f;
            captured = true;
        }

        /// <summary>The lighting `w` of the way from the outside values to the inside ones.</summary>
        public void Apply(float w)
        {
            if (!captured) return;
            float ambient = Mathf.Lerp(ambientOutside, ambientInside, w);
            RenderSettings.ambientIntensity = ambient;
            // the intensity multiplier only reaches a skybox ambient; a gradient or flat ambient is its colours, so those are scaled
            if (RenderSettings.ambientMode != AmbientMode.Skybox)
            {
                float k = ambientOutside > 1e-4f ? ambient / ambientOutside : 1f;
                RenderSettings.ambientSkyColor = skyOutside * k;
                RenderSettings.ambientEquatorColor = equatorOutside * k;
                RenderSettings.ambientGroundColor = groundOutside * k;
            }
            RenderSettings.reflectionIntensity = Mathf.Lerp(reflectionOutside, reflectionInside, w);
            if (grade != null) grade.exposureEV = Mathf.Lerp(exposureOutside, exposureInside, w);
        }

        /// <summary>Puts the outside values back.</summary>
        public void Restore()
        {
            if (!captured) return;
            RenderSettings.ambientIntensity = ambientOutside;
            RenderSettings.reflectionIntensity = reflectionOutside;
            RenderSettings.ambientSkyColor = skyOutside;
            RenderSettings.ambientEquatorColor = equatorOutside;
            RenderSettings.ambientGroundColor = groundOutside;
            if (grade != null) grade.exposureEV = exposureOutside;
            captured = false;
            weight = 0f;
        }

        private void FindGrade()
        {
            var g = viewCamera != null ? viewCamera.GetComponent<MapDaylightGrade>() : null;
            if (g == grade) return;
            // another camera: hand the old one its exposure back and take the new one's
            if (captured && grade != null) grade.exposureEV = exposureOutside;
            grade = g;
            if (captured && grade != null) exposureOutside = grade.exposureEV;
        }
    }
}
