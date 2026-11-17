using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Gyroscope = UnityEngine.InputSystem.Gyroscope;

namespace PentaWay
{
    public class DeadReckoningTracker : MonoBehaviour
    {
        public enum HeadingSource { None, Compass, Gyro, Manual }

        public FloorModel floorModel;
        public CompassNorth compass;

        [Header("Steps")]
        public float stepLength = 0.7f;
        public float stepPeak = 1.12f;
        public float stepReset = 1.02f;
        public float minStepInterval = 0.3f;

        [Header("Start point")]
        [FormerlySerializedAs("standDistanceFromMarker")] public float standDistanceFromReference = 1.5f;
        [FormerlySerializedAs("calibrateHeadingAtMarker")] public bool calibrateCompassAtStart = true;
        public float maxHeadingBias = 45f;

        [Header("Heading")]
        public float fallbackHeadingSmoothing = 6f;
        public float gyroSign = 1f;

        [Header("Editor")]
        public bool editorKeyboard = true;
        public float editorWalkSpeed = 1.4f;

        public bool useSensors = true;

        public bool IsCalibrated { get; private set; }
        public Vector2 PlanPosition { get; private set; }
        public float HeadingDegrees { get; private set; }
        public int StepCount { get; private set; }
        public HeadingSource ActiveHeadingSource { get; private set; }
        public bool SensorsAvailable => Accelerometer.current != null;

        public event Action<string> Calibrated;

        Vector3 gravity;
        float smoothedMagnitude = 1f;
        bool stepArmed = true;
        float lastStepTime;
        float headingBias;

        void Awake()
        {
            if (compass == null) compass = FindAnyObjectByType<CompassNorth>();
        }

        void OnEnable()
        {
            if (Accelerometer.current != null) InputSystem.EnableDevice(Accelerometer.current);
            if (Gyroscope.current != null) InputSystem.EnableDevice(Gyroscope.current);
        }

        void OnDisable()
        {
            if (Accelerometer.current != null && Accelerometer.current.enabled) InputSystem.DisableDevice(Accelerometer.current);
            if (Gyroscope.current != null && Gyroscope.current.enabled) InputSystem.DisableDevice(Gyroscope.current);
        }

        public void CalibrateAtReferencePoint(ReferencePoint point)
        {
            Vector2 position = point.WallPoint + FloorPlanData.ToVector(point.Facing) * standDistanceFromReference;
            float heading = FloorPlanData.ToYaw(point.Facing) + 180f;
            SetPose(position, heading, point.Name, MeasureCompassBias(heading, point.Name));
        }

        public void CalibrateAtRoom(RoomDestination room)
        {
            Vector2 position = floorModel.WorldToPlan(room.ApproachPoint.position);
            float heading = FloorPlanData.ToYaw(room.WalkwaySide) + 180f;
            SetPose(position, heading, room.DisplayName, MeasureCompassBias(heading, room.DisplayName));
        }

        public void SetPose(Vector2 planPosition, float headingDegrees, string sourceName, float compassBias = 0f)
        {
            PlanPosition = SnapToNavMesh(planPosition);
            HeadingDegrees = Mathf.Repeat(headingDegrees, 360f);
            headingBias = compassBias;
            ActiveHeadingSource = HeadingSource.Manual;
            IsCalibrated = true;
            gravity = Vector3.zero;
            Calibrated?.Invoke(sourceName);
        }

        public void MoveTo(Vector2 planPosition, float headingDegrees)
        {
            PlanPosition = SnapToNavMesh(planPosition);
            HeadingDegrees = Mathf.Repeat(headingDegrees, 360f);
            ActiveHeadingSource = HeadingSource.Manual;
        }

        float MeasureCompassBias(float knownHeading, string sourceName)
        {
            if (!calibrateCompassAtStart || !useSensors || compass == null || !compass.HasDeviceHeading) return 0f;
            float bias = Mathf.DeltaAngle(compass.PlanHeading, Mathf.Repeat(knownHeading, 360f));
            if (Mathf.Abs(bias) <= maxHeadingBias) return bias;
            Debug.LogWarning("[PentaWay] Compass differs " + bias.ToString("F0") + " degrees from the facing at '" + sourceName + "'. Bias ignored.");
            return 0f;
        }

        void Update()
        {
            if (!IsCalibrated) return;
#if UNITY_EDITOR
            if (editorKeyboard) EditorKeyboard();
#endif
            if (!useSensors || !SensorsAvailable) return;

            float dt = Time.deltaTime;
            Vector3 acceleration = Accelerometer.current.acceleration.ReadValue();
            gravity = gravity == Vector3.zero ? acceleration : Vector3.Lerp(gravity, acceleration, 1f - Mathf.Exp(-dt / 0.25f));

            UpdateHeading(dt);

            smoothedMagnitude = Mathf.Lerp(smoothedMagnitude, acceleration.magnitude, 1f - Mathf.Exp(-dt / 0.05f));
            if (stepArmed && smoothedMagnitude > stepPeak && Time.time - lastStepTime > minStepInterval)
            {
                stepArmed = false;
                lastStepTime = Time.time;
                StepCount++;
                Walk(stepLength);
            }
            else if (!stepArmed && smoothedMagnitude < stepReset)
            {
                stepArmed = true;
            }
        }

        void UpdateHeading(float dt)
        {
            if (compass != null && compass.HasDeviceHeading)
            {
                float target = Mathf.Repeat(compass.PlanHeading + headingBias, 360f);
                float follow = 1f - Mathf.Exp(-dt * fallbackHeadingSmoothing);
                HeadingDegrees = Mathf.Repeat(Mathf.LerpAngle(HeadingDegrees, target, follow), 360f);
                ActiveHeadingSource = HeadingSource.Compass;
                return;
            }

            if (gravity.sqrMagnitude > 0.25f && Gyroscope.current != null)
            {
                Vector3 up = -gravity.normalized;
                float yawRate = Vector3.Dot(Gyroscope.current.angularVelocity.ReadValue(), up) * Mathf.Rad2Deg;
                HeadingDegrees = Mathf.Repeat(HeadingDegrees - gyroSign * yawRate * dt, 360f);
                ActiveHeadingSource = HeadingSource.Gyro;
            }
        }

        public void Walk(float distance)
        {
            float rad = HeadingDegrees * Mathf.Deg2Rad;
            PlanPosition = MoveOnNavMesh(PlanPosition, PlanPosition + new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * distance);
        }

        Vector2 MoveOnNavMesh(Vector2 fromPlan, Vector2 toPlan)
        {
            Vector3 from = floorModel.PlanToWorld(fromPlan);
            Vector3 to = floorModel.PlanToWorld(toPlan);
            NavMeshHit hit;
            if (NavMesh.SamplePosition(from, out hit, 1f, NavMesh.AllAreas)) from = hit.position;
            if (!NavMesh.Raycast(from, to, out hit, NavMesh.AllAreas)) return floorModel.WorldToPlan(to);

            Vector3 move = to - from;
            Vector3 normal = hit.normal;
            normal.y = 0f;
            Vector3 stop = hit.position - move.normalized * 0.05f;
            if (normal.sqrMagnitude < 1e-4f) return floorModel.WorldToPlan(stop);

            float remaining = Mathf.Max(0f, move.magnitude - hit.distance);
            Vector3 slide = Vector3.ProjectOnPlane(move.normalized, normal.normalized) * remaining;
            Vector3 slideTarget = stop + slide;
            if (NavMesh.Raycast(stop, slideTarget, out hit, NavMesh.AllAreas))
                slideTarget = hit.position - slide.normalized * 0.05f;
            return floorModel.WorldToPlan(slideTarget);
        }

        Vector2 SnapToNavMesh(Vector2 plan)
        {
            if (floorModel == null) return plan;
            NavMeshHit hit;
            return NavMesh.SamplePosition(floorModel.PlanToWorld(plan), out hit, 2f, NavMesh.AllAreas)
                ? floorModel.WorldToPlan(hit.position)
                : plan;
        }

#if UNITY_EDITOR
        void EditorKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.qKey.isPressed) HeadingDegrees = Mathf.Repeat(HeadingDegrees - 90f * Time.deltaTime, 360f);
            if (keyboard.eKey.isPressed) HeadingDegrees = Mathf.Repeat(HeadingDegrees + 90f * Time.deltaTime, 360f);
            if (useSensors && keyboard.wKey.isPressed) Walk(editorWalkSpeed * Time.deltaTime);
            if (useSensors && keyboard.sKey.isPressed) Walk(-editorWalkSpeed * Time.deltaTime);
        }
#endif
    }
}
