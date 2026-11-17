using System;
using UnityEngine;

namespace PentaWay
{
    public enum PositioningMode { Compass, Map }

    public class PositioningModeController : MonoBehaviour
    {
        public enum StartMode { Auto = 0, ForceCompass = 2, ForceMap = 3 }

        public StartMode startMode = StartMode.Auto;

        [Header("References")]
        public ARNavigator navigator;
        public DeadReckoningTracker tracker;
        public CameraFeedView cameraFeed;
        public CompassViewRig compassRig;

        public bool IsReady { get; private set; }
        public PositioningMode Mode { get; private set; }

        public event Action ModeChanged;

        void Awake()
        {
            if (compassRig != null) compassRig.enabled = false;
        }

        void Start()
        {
            bool compassAvailable = IsAvailable(PositioningMode.Compass);
            PositioningMode mode = startMode == StartMode.ForceMap || !compassAvailable ? PositioningMode.Map : PositioningMode.Compass;
            IsReady = true;
            SetMode(mode);
            if (startMode == StartMode.Auto && !compassAvailable) navigator.RaiseMessage(NavText.NoSensorsMap);
        }

        public bool IsAvailable(PositioningMode mode)
        {
            if (mode == PositioningMode.Map || Application.isEditor) return true;
            return tracker != null && tracker.SensorsAvailable;
        }

        public void SetMode(PositioningMode mode)
        {
            if (!IsAvailable(mode)) mode = PositioningMode.Map;
            Mode = mode;
            bool compass = mode == PositioningMode.Compass;

            if (tracker != null) tracker.useSensors = compass;
            if (compassRig != null) compassRig.enabled = compass;
            if (cameraFeed != null) cameraFeed.Show(compass);

            navigator.SetMode(mode);
            ModeChanged?.Invoke();
        }
    }
}
