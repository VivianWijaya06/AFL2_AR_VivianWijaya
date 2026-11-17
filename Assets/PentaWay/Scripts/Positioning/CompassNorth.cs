using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace PentaWay
{
    public class CompassNorth : MonoBehaviour
    {
        public enum HeadingMode { Auto, GyroAttitude, UnityCompass }

        const string PrefOffset = "PentaWay.CompassOffset";
        const string PrefInvert = "PentaWay.CompassInvert";
        static readonly string[] PlanDirections = { "Utara", "Timur", "Selatan", "Barat" };

        [Header("Sensor")]
        public HeadingMode headingMode = HeadingMode.Auto;
        public float compassToCameraOffset = 0f;
        public float magneticDeclination = 0f;
        public bool checkHeadingAccuracy = true;
        public float maxHeadingAccuracy = 30f;
        public float deviceSmoothingSeconds = 0.25f;

        [Header("Developer tools")]
        public bool showDebugOverlay;
        public bool showCalibrationButtons;

        public HeadingMode ActiveMode { get; private set; }
        public bool HasDeviceHeading { get; private set; }
        public bool UsingTrueNorth { get; private set; }
        public float CurrentDeclination { get; private set; }
        public float UserOffset => userOffset;
        public bool InvertRotation => invertRotation;
        public float DeviceTrueHeading => Mathf.Repeat(Mathf.Atan2(headingVector.x, headingVector.y) * Mathf.Rad2Deg, 360f);
        public float PlanHeading => Mathf.Repeat(DeviceTrueHeading - FloorPlanData.PlanNorthTrueBearing, 360f);

        Vector2 headingVector;
        float userOffset;
        bool invertRotation;
        float lastBaseHeading;
        DeadReckoningTracker tracker;
        GUIStyle labelStyle, buttonStyle, debugStyle;

        public void ToggleDeveloperTools()
        {
            bool on = !(showDebugOverlay || showCalibrationButtons);
            showDebugOverlay = on;
            showCalibrationButtons = on;
        }

        void Awake()
        {
            tracker = FindAnyObjectByType<DeadReckoningTracker>();
            showDebugOverlay = false;
            showCalibrationButtons = false;
            userOffset = PlayerPrefs.GetFloat(PrefOffset, 0f);
            invertRotation = PlayerPrefs.GetInt(PrefInvert, 0) == 1;
        }

        void OnEnable()
        {
            Input.compass.enabled = true;
            if (SystemInfo.supportsGyroscope) Input.gyro.enabled = true;
            HasDeviceHeading = false;
            StartCoroutine(StartLocation());
        }

        void OnDisable()
        {
            Input.compass.enabled = false;
            if (SystemInfo.supportsGyroscope) Input.gyro.enabled = false;
            Input.location.Stop();
        }

        IEnumerator StartLocation()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
                Permission.RequestUserPermission(Permission.FineLocation);
            float waited = 0f;
            while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation) && waited < 30f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
#endif
            if (Input.location.isEnabledByUser) Input.location.Start();
            yield break;
        }

        void Update()
        {
            float heading;
            if (!TryReadHeading(out heading)) return;

            Vector2 sample = new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.01f, deviceSmoothingSeconds));
            headingVector = HasDeviceHeading ? Vector2.Lerp(headingVector, sample, k) : sample;
            HasDeviceHeading = true;
        }

        bool TryReadHeading(out float heading)
        {
            heading = 0f;
            Compass compass = Input.compass;

            bool locationRunning = Input.location.status == LocationServiceStatus.Running;
            UsingTrueNorth = locationRunning;

            float declination = magneticDeclination;
            if (locationRunning && compass.trueHeading >= 0f && compass.magneticHeading >= 0f)
                declination = Mathf.DeltaAngle(compass.magneticHeading, compass.trueHeading);
            CurrentDeclination = declination;

            bool gyroOk = SystemInfo.supportsGyroscope && Input.gyro.enabled;
            HeadingMode mode = headingMode == HeadingMode.Auto ? (gyroOk ? HeadingMode.GyroAttitude : HeadingMode.UnityCompass) : headingMode;
            if (mode == HeadingMode.GyroAttitude && !gyroOk) mode = HeadingMode.UnityCompass;
            if (mode == HeadingMode.GyroAttitude && headingMode == HeadingMode.Auto && IsZero(Input.gyro.attitude)) mode = HeadingMode.UnityCompass;
            ActiveMode = mode;

            if (mode == HeadingMode.GyroAttitude)
            {
                Quaternion q = Input.gyro.attitude;
                if (IsZero(q)) return false;

                Quaternion cam = Quaternion.Euler(90f, 0f, 0f) * new Quaternion(q.x, q.y, -q.z, -q.w);
                Vector3 forward = cam * Vector3.forward;
                Vector3 flat = new Vector3(forward.x, 0f, forward.z);
                if (flat.sqrMagnitude < 0.04f)
                {
                    Vector3 up = cam * Vector3.up;
                    flat = new Vector3(up.x, 0f, up.z);
                    if (forward.y > 0f) flat = -flat;
                    if (flat.sqrMagnitude < 1e-4f) return false;
                }

                heading = Finish(Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg + declination + compassToCameraOffset);
                return true;
            }

            if (!compass.enabled) return false;
            if (checkHeadingAccuracy && (compass.headingAccuracy < 0f || compass.headingAccuracy > maxHeadingAccuracy)) return false;

            float h = locationRunning ? compass.trueHeading : compass.magneticHeading + magneticDeclination;
            if (float.IsNaN(h) || float.IsInfinity(h)) return false;
            heading = Finish(h + compassToCameraOffset);
            return true;
        }

        static bool IsZero(Quaternion q) => q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f;

        float Finish(float baseHeading)
        {
            lastBaseHeading = Mathf.Repeat(invertRotation ? -baseHeading : baseHeading, 360f);
            return Mathf.Repeat(lastBaseHeading + userOffset, 360f);
        }

        public void CalibrateToPlanHeading(float planHeading)
        {
            if (!HasDeviceHeading) return;
            float target = Mathf.Repeat(planHeading + FloorPlanData.PlanNorthTrueBearing, 360f);
            userOffset = Mathf.DeltaAngle(lastBaseHeading, target);
            PlayerPrefs.SetFloat(PrefOffset, userOffset);
            PlayerPrefs.Save();
            headingVector = new Vector2(Mathf.Sin(target * Mathf.Deg2Rad), Mathf.Cos(target * Mathf.Deg2Rad));
        }

        public void ToggleInvertRotation()
        {
            invertRotation = !invertRotation;
            userOffset = 0f;
            PlayerPrefs.SetInt(PrefInvert, invertRotation ? 1 : 0);
            PlayerPrefs.SetFloat(PrefOffset, 0f);
            PlayerPrefs.Save();
            HasDeviceHeading = false;
        }

        public void ResetCalibration()
        {
            invertRotation = false;
            userOffset = 0f;
            PlayerPrefs.DeleteKey(PrefOffset);
            PlayerPrefs.DeleteKey(PrefInvert);
            HasDeviceHeading = false;
        }

        void OnGUI()
        {
            if (!showCalibrationButtons && !showDebugOverlay) return;
            EnsureStyles();
            if (showCalibrationButtons) DrawCalibrationButtons();
            if (showDebugOverlay) DrawDebugOverlay();
        }

        void EnsureStyles()
        {
            if (labelStyle != null) return;
            int size = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.018f));
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = size };
            labelStyle.normal.textColor = Color.cyan;
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = size };
            debugStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.02f)) };
            debugStyle.normal.textColor = Color.yellow;
        }

        void DrawCalibrationButtons()
        {
            float rowHeight = Screen.height * 0.055f;
            float gap = 8f;
            float width = (Screen.width - gap * 5f) / 4f;
            float y = Screen.height * 0.24f;

            GUI.Label(new Rect(gap, y, Screen.width - gap * 2f, rowHeight),
                      "Hadap ke arah ini lalu tekan  (koreksi " + userOffset.ToString("F0") + ", putaran " + (invertRotation ? "dibalik" : "normal") + ")", labelStyle);
            y += rowHeight;

            for (int i = 0; i < 4; i++)
                if (GUI.Button(new Rect(gap + i * (width + gap), y, width, rowHeight), PlanDirections[i], buttonStyle))
                    CalibrateToPlanHeading(i * 90f);
            y += rowHeight + gap;

            if (GUI.Button(new Rect(gap, y, width * 2f + gap, rowHeight), "Balik putaran", buttonStyle)) ToggleInvertRotation();
            if (GUI.Button(new Rect(gap * 3f + width * 2f, y, width * 2f + gap, rowHeight), "Reset kalibrasi", buttonStyle)) ResetCalibration();
        }

        void DrawDebugOverlay()
        {
            string text =
                "Mode sensor : " + ActiveMode + (UsingTrueNorth ? " (utara sebenarnya)" : " (utara magnetis + deklinasi manual)") + "\n" +
                "Heading HP (utara = 0) : " + (HasDeviceHeading ? DeviceTrueHeading.ToString("F0") : "-") + "\n" +
                "Deklinasi : " + CurrentDeclination.ToString("F1");
            if (tracker != null)
                text += "\nHeading denah : " + tracker.HeadingDegrees.ToString("F0") + "  sumber " + tracker.ActiveHeadingSource +
                        "  langkah " + tracker.StepCount;
            GUI.Label(new Rect(20f, 20f, Screen.width - 40f, Screen.height * 0.4f), text, debugStyle);
        }
    }
}
