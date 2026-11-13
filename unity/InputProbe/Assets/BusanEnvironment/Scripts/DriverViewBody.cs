using Parkside.Cameras;
using UnityEngine;

namespace BusanEnvironment
{
    /// <summary>
    /// In the driver view the exterior model is hidden (the camera sits inside it) and a low-poly dark
    /// bonnet and dashboard top are shown instead; in every other view it is the other way round.
    /// </summary>
    public sealed class DriverViewBody : MonoBehaviour
    {
        [SerializeField] private VehicleCamera vehicleCamera;
        [SerializeField] private Renderer[] driverOnly;
        [SerializeField] private Renderer[] hiddenInDriver;
        private int lastView = -1;

        private void LateUpdate()
        {
            if (vehicleCamera == null) return;
            int view = vehicleCamera.ViewIndex;
            if (view == lastView) return;
            lastView = view;
            bool driver = view == 2;
            if (driverOnly != null) foreach (var r in driverOnly) if (r != null) r.enabled = driver;
            if (hiddenInDriver != null) foreach (var r in hiddenInDriver) if (r != null) r.enabled = !driver;
        }
    }
}
