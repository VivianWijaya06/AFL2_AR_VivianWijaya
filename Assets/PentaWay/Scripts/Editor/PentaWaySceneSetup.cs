using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace PentaWay.EditorTools
{
    public static class PentaWaySceneSetup
    {
        const string NavigationObjectName = "PentaWay Navigation";

        [MenuItem("PentaWay/Set Up Scene")]
        static void SetUpSceneMenu()
        {
            GameObject nav = Run(true);
            if (nav != null) Selection.activeGameObject = nav;
        }

        public static GameObject Run(bool interactive)
        {
            XROrigin origin = Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
            if (origin == null || origin.Camera == null)
            {
                const string message = "No XR Origin found. Add GameObject > XR > XR Origin (Mobile AR), then run PentaWay > Set Up Scene again.";
                if (interactive) EditorUtility.DisplayDialog("PentaWay", message, "OK");
                Debug.LogError("[PentaWay] " + message);
                return null;
            }

            FloorModel floor = Object.FindAnyObjectByType<FloorModel>(FindObjectsInactive.Include);
            if (floor == null) floor = PentaWayFloorMenu.GenerateFloor();

            Camera cam = origin.Camera;
            ARSession session = Object.FindAnyObjectByType<ARSession>(FindObjectsInactive.Include);
            if (session == null)
            {
                var sessionGo = new GameObject("AR Session");
                session = sessionGo.AddComponent<ARSession>();
                sessionGo.AddComponent<ARInputManager>();
                Undo.RegisterCreatedObjectUndo(sessionGo, "Create AR Session");
            }
            ARCameraManager cameraManager = GetOrAdd<ARCameraManager>(cam.gameObject);
            ARCameraBackground cameraBackground = GetOrAdd<ARCameraBackground>(cam.gameObject);
            SetEnabled(session, false);
            SetEnabled(cameraManager, false);
            SetEnabled(cameraBackground, false);

            RemoveComponent<ARTrackedImageManager>(origin.gameObject);
            RemoveComponent<ARRaycastManager>(origin.gameObject);
            RemoveComponent<ARPlaneManager>(origin.gameObject);
            DisableOtherCameras(cam);

            Material occluder = PentaWayFloorMenu.LoadOrCreateMaterial("Occluder", "PentaWay/DepthOnlyOccluder", Color.clear);
            Material arrow = PentaWayFloorMenu.LoadOrCreateMaterial("Arrow", "PentaWay/PathArrow", new Color(0.36f, 0.49f, 0.98f, 0.95f));
            Material ribbon = PentaWayFloorMenu.LoadOrCreateMaterial("PathLine", "PentaWay/PathRibbon", new Color(1f, 1f, 1f, 0.92f));
            Material feed = PentaWayFloorMenu.LoadOrCreateMaterial("CameraFeed", "PentaWay/CameraFeed", Color.white);

            GameObject nav = GameObject.Find(NavigationObjectName);
            if (nav == null)
            {
                nav = new GameObject(NavigationObjectName);
                Undo.RegisterCreatedObjectUndo(nav, "Create PentaWay Navigation");
            }
            RemoveMissingScripts(nav.scene);

            PathArrowRenderer arrows = GetOrAdd<PathArrowRenderer>(nav);
            arrows.arrowMaterial = arrow;
            arrows.ribbonMaterial = ribbon;

            CompassNorth compass = GetOrAdd<CompassNorth>(nav);

            DeadReckoningTracker tracker = GetOrAdd<DeadReckoningTracker>(nav);
            tracker.floorModel = floor;
            tracker.compass = compass;

            CameraFeedView cameraFeed = GetOrAdd<CameraFeedView>(nav);
            cameraFeed.targetCamera = cam;
            cameraFeed.feedMaterial = feed;

            CompassViewRig rig = GetOrAdd<CompassViewRig>(nav);
            rig.targetCamera = cam;
            rig.tracker = tracker;
            rig.floorModel = floor;

            ARNavigator navigator = GetOrAdd<ARNavigator>(nav);
            navigator.floorModel = floor;
            navigator.arCamera = cam;
            navigator.tracker = tracker;
            navigator.pathRenderer = arrows;
            navigator.occluderMaterial = occluder;

            PositioningModeController modes = GetOrAdd<PositioningModeController>(nav);
            modes.navigator = navigator;
            modes.tracker = tracker;
            modes.cameraFeed = cameraFeed;
            modes.compassRig = rig;
            modes.startMode = PositioningModeController.StartMode.Auto;

            NavRuntimeUI ui = GetOrAdd<NavRuntimeUI>(nav);
            ui.navigator = navigator;
            ui.tracker = tracker;
            ui.modeController = modes;
            if (ui.logo == null) ui.logo = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PentaWay/Branding/PentaWay_Logo.png");

            EnsureEventSystem();

            foreach (Object o in new Object[] { arrows, compass, tracker, cameraFeed, rig, navigator, modes, ui })
                EditorUtility.SetDirty(o);
            EditorSceneManager.MarkSceneDirty(nav.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[PentaWay] Scene set up.");
            return nav;
        }

        public static int RemoveMissingScripts(Scene scene)
        {
            int removed = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            if (removed > 0) Debug.Log("[PentaWay] Removed " + removed + " missing script component(s).");
            return removed;
        }

        static void SetEnabled(Behaviour behaviour, bool value)
        {
            if (behaviour == null || behaviour.enabled == value) return;
            behaviour.enabled = value;
            EditorUtility.SetDirty(behaviour);
        }

        static void RemoveComponent<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            if (component == null) return;
            Undo.DestroyObjectImmediate(component);
            Debug.Log("[PentaWay] Removed " + typeof(T).Name + " from " + go.name + ".");
        }

        static void DisableOtherCameras(Camera arCamera)
        {
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            {
                if (c == arCamera || c.transform.IsChildOf(arCamera.transform.root)) continue;
                c.gameObject.SetActive(false);
                Debug.Log("[PentaWay] Disabled extra camera '" + c.name + "'.");
            }
        }

        static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(go);
        }
    }
}
