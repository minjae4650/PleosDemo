using System;
using Parkside.Core;
using Parkside.Input;
using Parkside.Vehicle;
using UnityEngine;

namespace BusanEnvironment
{
    /// <summary>
    /// Desktop keyboard for the drive map: W/Up accelerate, S/Down/Space brake, A/D and arrows steer.
    /// S or Down also reverses: pressed anew at a standstill it reverses at once, held down to a stop it
    /// reverses after 0.8 s; in reverse S is a gentle throttle and W brakes, and W takes the car forward
    /// again by the same rule. Space only brakes, so it never puts the car in reverse. The parking scenes keep
    /// <see cref="KeyboardInputSource"/> (W/Space), this source is wired into the Busan scene only.
    /// </summary>
    public sealed class DriveKeyboardSource : VehicleInputSource
    {
        public const float StandstillSpeed = 0.3f;
        public const float HoldSeconds = 0.8f;
        public const float ReverseThrottle = 0.14f;   // about 11 km/h with the drive scene's 22 m/s top speed

        [SerializeField] private VehicleController vehicle;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private DriveAssist assist;
        [SerializeField, Range(1f, 60f)] private float steeringAngle = 35f;

        private bool connected;
        private long sequence;
        private bool previousTrigger;
        private float stoppedHold;
        private float lastRead = -1f;

        public override string Description => "Drive keyboard: W throttle / S brake+reverse / Space brake";

        /// <summary>Tests drive the source without a running GameManager (it is disabled there); this lifts that gate.</summary>
        public bool IgnoreRunState { get; set; }

        public override void Connect()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            throw new InvalidOperationException("Keyboard input is for PC testing. Assign PleosInputSource for the Android APK.");
#else
            connected = true;
            previousTrigger = false;
            stoppedHold = 0f;
            lastRead = -1f;
#endif
        }

        public override InputSnapshot ReadSnapshot()
        {
            if (!connected) throw new InvalidOperationException("Keyboard source is not connected");
            float now = Time.realtimeSinceStartup;
            float dt = lastRead < 0f ? 0.1f : Mathf.Clamp(now - lastRead, 0f, 0.3f);
            lastRead = now;
            bool accel = UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.UpArrow);
            bool brakeOnly = UnityEngine.Input.GetKey(KeyCode.S) || UnityEngine.Input.GetKey(KeyCode.DownArrow);
            bool brake = brakeOnly || UnityEngine.Input.GetKey(KeyCode.Space);
            bool left = UnityEngine.Input.GetKey(KeyCode.LeftArrow) || UnityEngine.Input.GetKey(KeyCode.A);
            bool right = UnityEngine.Input.GetKey(KeyCode.RightArrow) || UnityEngine.Input.GetKey(KeyCode.D);
            return Evaluate(accel, brake, brakeOnly, left, right, dt);
        }

        /// <summary>Key logic without key reads. `brake` is S/Down/Space, `brakeOnly` is S/Down alone (the keys that may reverse).</summary>
        public InputSnapshot Evaluate(bool accel, bool brake, bool brakeOnly, bool left, bool right, float dt)
        {
            bool reversing = vehicle != null && vehicle.IsReversing;
            float speed = vehicle != null ? vehicle.SpeedMps : 0f;
            // the key that asks for the opposite direction: S going forward, W going backward
            bool trigger = reversing ? accel : brakeOnly;
            bool switchNow = false;
            if (trigger && speed < StandstillSpeed)
            {
                if (!previousTrigger) switchNow = true;
                else
                {
                    stoppedHold += dt;
                    if (stoppedHold >= HoldSeconds) switchNow = true;
                }
            }
            else stoppedHold = 0f;
            previousTrigger = trigger;

            if (switchNow && CanSwitch())
            {
                vehicle.ToggleDirection();
                stoppedHold = 0f;
                previousTrigger = false;
                return CreateSnapshot(0f, 1f, left, right);
            }

            float throttle, brakeValue;
            if (reversing)
            {
                // S drives backward gently, W and Space brake
                bool brakeKey = accel || (brake && !brakeOnly);
                throttle = brakeOnly && !brakeKey ? ReverseThrottle : 0f;
                brakeValue = brakeKey ? 1f : 0f;
            }
            else
            {
                brakeValue = brake ? 1f : 0f;
                throttle = accel && !brake ? 1f : 0f;
            }
            return CreateSnapshot(throttle, brakeValue, left, right);
        }

        private bool CanSwitch()
        {
            if (vehicle == null) return false;
            if (IgnoreRunState) return true;
            return gameManager != null && gameManager.IsRunning && (assist == null || !assist.AutoDriving);
        }

        private InputSnapshot CreateSnapshot(float throttle, float brake, bool left, bool right)
        {
            var steering = ((right ? 1f : 0f) - (left ? 1f : 0f)) * steeringAngle;
            return new InputSnapshot
            {
                schemaVersion = 1, sequence = ++sequence, source = "KEYBOARD PREVIEW",
                active = true, allowed = true, realGear = 4, realSpeedMps = 0f,
                steeringDegrees = steering, steeringNormalized = steering / steeringAngle,
                acceleratorPercent = throttle * 100f, throttle = throttle,
                brakePercent = brake * 100f, brake = brake, sdkBrakePressed = brake > 0f,
                steeringAgeMs = 0, pedalAgeMs = 0
            };
        }

        public override void Disconnect() => connected = false;
    }
}
