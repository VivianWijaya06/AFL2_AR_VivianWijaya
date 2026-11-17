using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PentaWay
{
    public class CompassViewRig : MonoBehaviour
    {
        public Camera targetCamera;
        public DeadReckoningTracker tracker;
        public FloorModel floorModel;

        public float eyeHeight = 1.4f;
        public float verticalFieldOfView = 65f;
        public float tiltSmoothing = 0.1f;
        public float editorPitch = 25f;

        [Header("Stabilisasi tampilan")]
        public bool useGyroAttitude = true;
        public float positionSmoothing = 0.4f;
        public float positionSnapDistance = 2f;
        public float yawCorrectionSeconds = 1.5f;
        public float yawJumpDegrees = 45f;

        public Vector2 DisplayPlanPosition => displayPosition;
        public bool UsingGyroAttitude { get; private set; }

        readonly List<Behaviour> disabledPoseDrivers = new List<Behaviour>();
        float originalFieldOfView;
        Vector3 gravity;
        Vector2 displayPosition;
        Vector2 displayVelocity;
        bool hasDisplayPosition;
        float yawOffset;
        float lastYawTarget;
        bool hasYawOffset;
        Vector2 laggedYawVector;
        float laggedYaw;

        void OnEnable()
        {
            hasDisplayPosition = false;
            hasYawOffset = false;
            if (tracker != null) tracker.Calibrated += OnCalibrated;
            if (targetCamera == null) return;

            disabledPoseDrivers.Clear();
            foreach (Behaviour b in targetCamera.GetComponents<Behaviour>())
            {
                if (b != null && b != this && b.enabled && b.GetType().Name.Contains("TrackedPoseDriver"))
                {
                    b.enabled = false;
                    disabledPoseDrivers.Add(b);
                }
            }

            originalFieldOfView = targetCamera.fieldOfView;
            targetCamera.ResetProjectionMatrix();
            targetCamera.fieldOfView = verticalFieldOfView;
            gravity = Vector3.zero;
            if (Accelerometer.current != null) InputSystem.EnableDevice(Accelerometer.current);
        }

        void OnDisable()
        {
            if (tracker != null) tracker.Calibrated -= OnCalibrated;
            foreach (Behaviour b in disabledPoseDrivers)
                if (b != null) b.enabled = true;
            disabledPoseDrivers.Clear();
            if (targetCamera != null) targetCamera.fieldOfView = originalFieldOfView;
        }

        void OnCalibrated(string source)
        {
            hasDisplayPosition = false;
            hasYawOffset = false;
        }

        void LateUpdate()
        {
            if (targetCamera == null || tracker == null || floorModel == null || !tracker.IsCalibrated) return;
            float dt = Time.deltaTime;

            Quaternion attitude;
            UsingGyroAttitude = TryGetGyroAttitude(out attitude);
            Quaternion tilt = UsingGyroAttitude ? attitude : Quaternion.FromToRotation(CameraLocalDown(), Vector3.down);

            float currentYaw;
            if (!TryGetFacingYaw(tilt, out currentYaw)) return;

            float yaw;
            if (UsingGyroAttitude && CanBlendYaw())
            {
                Vector2 sample = new Vector2(Mathf.Sin(currentYaw * Mathf.Deg2Rad), Mathf.Cos(currentYaw * Mathf.Deg2Rad));
                if (!hasYawOffset)
                {
                    laggedYawVector = sample;
                    laggedYaw = currentYaw;
                }
                else
                {
                    float vectorFollow = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.01f, tracker.compass.deviceSmoothingSeconds));
                    laggedYawVector = Vector2.Lerp(laggedYawVector, sample, vectorFollow);
                    float headingFollow = 1f - Mathf.Exp(-dt * tracker.fallbackHeadingSmoothing);
                    laggedYaw = Mathf.LerpAngle(laggedYaw, Mathf.Atan2(laggedYawVector.x, laggedYawVector.y) * Mathf.Rad2Deg, headingFollow);
                }
                float target = Mathf.DeltaAngle(laggedYaw, tracker.HeadingDegrees);
                if (!hasYawOffset || Mathf.Abs(Mathf.DeltaAngle(lastYawTarget, target)) > yawJumpDegrees)
                    yawOffset = target;
                else
                    yawOffset = Mathf.LerpAngle(yawOffset, target, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, yawCorrectionSeconds)));
                lastYawTarget = target;
                hasYawOffset = true;
                yaw = currentYaw + yawOffset;
            }
            else
            {
                hasYawOffset = false;
                yaw = tracker.HeadingDegrees;
            }
            yaw += floorModel.transform.eulerAngles.y;

            Vector2 targetPosition = tracker.PlanPosition;
            if (!hasDisplayPosition || (targetPosition - displayPosition).sqrMagnitude > positionSnapDistance * positionSnapDistance)
            {
                displayPosition = targetPosition;
                displayVelocity = Vector2.zero;
            }
            else
            {
                displayPosition = Vector2.SmoothDamp(displayPosition, targetPosition, ref displayVelocity, Mathf.Max(0.01f, positionSmoothing), Mathf.Infinity, dt);
            }
            hasDisplayPosition = true;

            targetCamera.transform.SetPositionAndRotation(
                floorModel.PlanToWorld(displayPosition, eyeHeight),
                Quaternion.Euler(0f, yaw - currentYaw, 0f) * tilt);
        }

        bool CanBlendYaw()
        {
            CompassNorth compass = tracker.compass;
            return compass != null && compass.HasDeviceHeading && !compass.InvertRotation &&
                   compass.ActiveMode == CompassNorth.HeadingMode.GyroAttitude &&
                   tracker.ActiveHeadingSource == DeadReckoningTracker.HeadingSource.Compass;
        }

        bool TryGetGyroAttitude(out Quaternion attitude)
        {
            attitude = Quaternion.identity;
            if (!useGyroAttitude || !SystemInfo.supportsGyroscope || !Input.gyro.enabled) return false;
            Quaternion q = Input.gyro.attitude;
            if (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f) return false;
            attitude = Quaternion.Euler(90f, 0f, 0f) * new Quaternion(q.x, q.y, -q.z, -q.w);
            return true;
        }

        static bool TryGetFacingYaw(Quaternion rotation, out float yaw)
        {
            Vector3 forward = rotation * Vector3.forward;
            Vector3 facing = new Vector3(forward.x, 0f, forward.z);
            if (facing.sqrMagnitude < 0.04f)
            {
                Vector3 up = rotation * Vector3.up;
                facing = new Vector3(up.x, 0f, up.z);
                if (forward.y > 0f) facing = -facing;
            }
            yaw = 0f;
            if (facing.sqrMagnitude < 1e-6f) return false;
            yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            return true;
        }

        Vector3 CameraLocalDown()
        {
            Accelerometer accelerometer = Accelerometer.current;
            if (accelerometer == null || !accelerometer.enabled)
                return Quaternion.Inverse(Quaternion.Euler(editorPitch, 0f, 0f)) * Vector3.down;

            Vector3 a = accelerometer.acceleration.ReadValue();
            Vector3 local = new Vector3(a.x, a.y, -a.z);
            if (local.sqrMagnitude < 0.04f) return gravity.sqrMagnitude > 0f ? gravity : Vector3.down;

            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, tiltSmoothing));
            gravity = gravity == Vector3.zero ? local.normalized : Vector3.Slerp(gravity, local.normalized, k);
            return gravity;
        }
    }
}
