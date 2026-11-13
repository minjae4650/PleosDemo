using Parkside.Input;
using UnityEngine;

namespace Parkside.Vehicle
{
    /// <summary>Simple low-speed preview controller. Replace this component when adding full physics.</summary>
    public sealed class VehicleController : MonoBehaviour
    {
        [SerializeField] private Transform frontLeftWheel;
        [SerializeField] private Transform frontRightWheel;
        [SerializeField, Min(0.1f)] private float maximumSpeed = 2f;
        [SerializeField, Min(0.1f)] private float acceleration = 1.8f;
        [SerializeField, Min(0.1f)] private float wheelbase = 2.7f;
        [SerializeField, Range(1f, 60f)] private float maximumSteeringAngle = 35f;
        // Drive-map extensions. Every default below keeps the original parking behaviour:
        // instant brake stop, flat-plane motion, full steering at any speed, raw steering input.
        [SerializeField, Min(0f)] private float brakeDeceleration = 0f;
        [SerializeField, Min(0f)] private float highSpeedSteeringAngle = 0f; // 0 = off
        [SerializeField, Min(1f)] private float highSpeedAt = 25f;
        [SerializeField, Min(0f)] private float maxLateralAcceleration = 0f; // 0 = off
        [SerializeField, Min(0f)] private float steeringResponse = 0f; // 0 = off
        [SerializeField] private bool groundFollow = false;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float groundClearance = 0f;
        private const float ProbeHeight = 4f;
        private const float ProbeDepth = 10f;
        private const float AlignRate = 8f;
        private const float SnapRate = 12f;
        private const float SurfaceTolerance = 1.2f;
        private static readonly RaycastHit[] HitBuffer = new RaycastHit[16];
        private static readonly float[] SlideAngles = { 10f, -10f, 20f, -20f, 30f, -30f, 40f, -40f, 50f, -50f };
        private int direction = 1;
        private float steeringState;
        private float speedCap = float.PositiveInfinity;

        public float SpeedKph => SpeedMps * 3.6f;
        public float SpeedMps { get; private set; }
        public bool IsReversing => direction < 0;
        public bool IsConfigured => frontLeftWheel != null && frontRightWheel != null;
        /// <summary>Upper bound on forward speed (m/s) so ambient traffic ahead is not rammed; infinity removes it.</summary>
        public float SpeedCap { get => speedCap; set => speedCap = value; }

        public void Simulate(VehicleControlInput input, bool driving, float deltaTime)
        {
            var steerLimit = maximumSteeringAngle;
            if (highSpeedSteeringAngle > 0f)
                steerLimit = Mathf.Lerp(maximumSteeringAngle, highSpeedSteeringAngle, Mathf.Clamp01(SpeedMps / highSpeedAt));
            if (maxLateralAcceleration > 0f && SpeedMps > 1f)
                steerLimit = Mathf.Min(steerLimit, Mathf.Atan(maxLateralAcceleration * wheelbase / (SpeedMps * SpeedMps)) * Mathf.Rad2Deg);
            var steering = input.IsValid ? Mathf.Clamp(input.SteeringDegrees, -steerLimit, steerLimit) : 0f;
            if (steeringResponse > 0f)
            {
                steeringState = Mathf.Lerp(steeringState, steering, 1f - Mathf.Exp(-steeringResponse * Mathf.Clamp(deltaTime, 0f, 0.1f)));
                steering = steeringState;
            }
            if (frontLeftWheel != null) frontLeftWheel.localRotation = Quaternion.Euler(0f, steering, 0f);
            if (frontRightWheel != null) frontRightWheel.localRotation = Quaternion.Euler(0f, steering, 0f);
            if (!driving || !input.IsValid) { Stop(); return; }

            var dt = Mathf.Clamp(deltaTime, 0f, 0.1f);
            if (input.Brake > 0.01f)
                SpeedMps = brakeDeceleration > 0f ? Mathf.MoveTowards(SpeedMps, 0f, dt * brakeDeceleration) : 0f;
            else
                SpeedMps = Mathf.MoveTowards(SpeedMps, input.Throttle * maximumSpeed, dt * acceleration);
            // cap 0 means a stopped car right ahead; any other cap keeps a 2 m/s floor so two cars never wait for each other
            if (direction > 0 && SpeedMps > speedCap) SpeedMps = Mathf.MoveTowards(SpeedMps, speedCap <= 0f ? 0f : Mathf.Max(speedCap, 2f), dt * 9f);
            var yaw = direction * SpeedMps / wheelbase * Mathf.Tan(steering * Mathf.Deg2Rad);
            var before = transform.position;
            var beforeRotation = transform.rotation;
            transform.Rotate(0f, yaw * Mathf.Rad2Deg * dt, 0f);
            transform.position += transform.forward * (direction * SpeedMps * dt);
            if (groundFollow) FollowGround(dt, before, beforeRotation);
        }

        // Arcade ground follow for the drive map: snaps height and pitch to road
        // colliders without physics. Off by default so parking scenes keep the
        // original flat-plane behaviour. Of all surfaces under a probe only the one
        // nearest the current height counts (within SurfaceTolerance), so a car neither
        // jumps onto a ramp above nor drops off a viaduct onto the road below. At a road
        // end the car slides along whichever 10-50 degree heading still has road, else stops.
        private void FollowGround(float dt, Vector3 beforeMove, Quaternion beforeRotation)
        {
            var half = wheelbase * 0.5f;
            var referenceY = beforeMove.y;
            var frontOk = Probe(transform.position + transform.forward * half, referenceY, out var front);
            var rearOk = Probe(transform.position - transform.forward * half, referenceY, out var rear);
            if (frontOk && rearOk)
            {
                Settle(dt, front, rear);
                return;
            }
            var leadingOk = direction >= 0 ? frontOk : rearOk;
            if (leadingOk)
            {
                // the trailing axle may hang over (so reversing back onto the road works); keep following the height of the axle that has road
                var held = transform.position;
                held.y = Mathf.Lerp(held.y, (frontOk ? front : rear) + groundClearance, Mathf.Clamp01(SnapRate * dt));
                transform.position = held;
                return;
            }
            var travel = transform.position - beforeMove;
            var flat = new Vector3(travel.x, 0f, travel.z);
            if (flat.sqrMagnitude > 1e-8f)
            {
                foreach (var angle in SlideAngles)
                {
                    var moved = Quaternion.Euler(0f, angle, 0f) * flat;
                    var candidate = beforeMove + moved;
                    var lead = candidate + moved.normalized * half; // the travel vector already points the way the car is moving, also in reverse
                    if (!Probe(lead, referenceY, out _)) continue;
                    transform.position = candidate;
                    SpeedMps = Mathf.MoveTowards(SpeedMps, 0f, dt * 6f);
                    return;
                }
            }
            transform.SetPositionAndRotation(beforeMove, beforeRotation);
            Stop();
        }

        private void Settle(float dt, float front, float rear)
        {
            var targetPitch = -Mathf.Atan2(front - rear, wheelbase) * Mathf.Rad2Deg;
            var pitch = Mathf.LerpAngle(transform.eulerAngles.x, targetPitch, Mathf.Clamp01(AlignRate * dt));
            transform.rotation = Quaternion.Euler(pitch, transform.eulerAngles.y, 0f);
            var position = transform.position;
            position.y = Mathf.Lerp(position.y, (front + rear) * 0.5f + groundClearance, Mathf.Clamp01(SnapRate * dt));
            transform.position = position;
        }

        private bool Probe(Vector3 at, float referenceY, out float height)
        {
            var count = Physics.RaycastNonAlloc(at + Vector3.up * ProbeHeight, Vector3.down, HitBuffer, ProbeHeight + ProbeDepth, groundMask, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            height = 0f;
            for (var i = 0; i < count; i++)
            {
                var gap = Mathf.Abs(HitBuffer[i].point.y - referenceY);
                if (gap < best) { best = gap; height = HitBuffer[i].point.y; }
            }
            return count > 0 && best <= SurfaceTolerance;
        }

        public void Stop() => SpeedMps = 0f;

        /// <summary>Hands a speed to the vehicle (e.g. when an auto-drive ends) and clears smoothed steering.</summary>
        public void SetSpeed(float mps) { SpeedMps = Mathf.Max(0f, mps); steeringState = 0f; }

        public void ResetSteering() => steeringState = 0f;

        public void ToggleDirection()
        {
            Stop();
            direction *= -1;
        }

        public void ResetToPose(Vector3 position, Quaternion rotation)
        {
            Stop();
            steeringState = 0f;
            transform.SetPositionAndRotation(position, rotation);
            if (frontLeftWheel != null) frontLeftWheel.localRotation = Quaternion.identity;
            if (frontRightWheel != null) frontRightWheel.localRotation = Quaternion.identity;
        }
    }
}
