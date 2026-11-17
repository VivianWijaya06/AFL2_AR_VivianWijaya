using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace PentaWay.EditorTools
{
    public static class PentaWayBuildTools
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string ApkPath = "Builds/PentaWay.apk";

        [MenuItem("PentaWay/Validate Scene")]
        static void ValidateMenu()
        {
            int problems = Validate();
            EditorUtility.DisplayDialog("PentaWay", problems == 0 ? "Scene OK." : problems + " problem(s). See the Console.", "OK");
        }

        [MenuItem("PentaWay/Build Android APK")]
        static void BuildMenu()
        {
            BuildAndroid();
        }

        public static void BatchSetupAndValidate()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            PentaWayFloorMenu.GenerateFloor();
            bool setupOk = PentaWaySceneSetup.Run(false) != null;
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            int problems = Validate() + (setupOk ? 0 : 1);
            Debug.Log("[PentaWay][Validate] " + (problems == 0 ? "PASSED" : "FAILED with " + problems + " problem(s)"));
            EditorApplication.Exit(problems == 0 ? 0 : 1);
        }

        public static void BatchBuildAndroid()
        {
            EditorApplication.Exit(BuildAndroid() ? 0 : 1);
        }

        public static int Validate()
        {
            var problems = new List<string>();

            int missing = 0;
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (missing > 0) problems.Add(missing + " missing script component(s)");

            if (Object.FindAnyObjectByType<ARTrackedImageManager>(FindObjectsInactive.Include) != null) problems.Add("ARTrackedImageManager still present");
            if (Object.FindAnyObjectByType<ARSession>(FindObjectsInactive.Include) == null) problems.Add("AR Session missing");
            if (Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include) == null) problems.Add("XR Origin missing");

            ARNavigator navigator = Object.FindAnyObjectByType<ARNavigator>(FindObjectsInactive.Include);
            if (navigator == null) problems.Add("ARNavigator missing");
            else
            {
                if (navigator.floorModel == null) problems.Add("ARNavigator.floorModel not set");
                if (navigator.tracker == null) problems.Add("ARNavigator.tracker not set");
                if (navigator.pathRenderer == null) problems.Add("ARNavigator.pathRenderer not set");
                if (navigator.arCamera == null) problems.Add("ARNavigator.arCamera not set");
                if (navigator.occluderMaterial == null) problems.Add("ARNavigator.occluderMaterial not set");
            }

            NavRuntimeUI ui = Object.FindAnyObjectByType<NavRuntimeUI>(FindObjectsInactive.Include);
            if (ui == null || ui.navigator == null || ui.modeController == null) problems.Add("NavRuntimeUI missing or not wired");

            PositioningModeController modes = Object.FindAnyObjectByType<PositioningModeController>(FindObjectsInactive.Include);
            if (modes == null || modes.tracker == null || modes.cameraFeed == null || modes.compassRig == null) problems.Add("PositioningModeController missing or not wired");

            CameraFeedView feed = Object.FindAnyObjectByType<CameraFeedView>(FindObjectsInactive.Include);
            if (feed == null || feed.feedMaterial == null || feed.targetCamera == null) problems.Add("CameraFeedView missing or not wired");

            PathArrowRenderer arrows = Object.FindAnyObjectByType<PathArrowRenderer>(FindObjectsInactive.Include);
            if (arrows == null || arrows.ribbonMaterial == null || arrows.arrowMaterial == null) problems.Add("PathArrowRenderer materials not set");

            FloorModel floor = Object.FindAnyObjectByType<FloorModel>(FindObjectsInactive.Include);
            if (floor == null) problems.Add("FloorModel missing");
            else
            {
                var ids = floor.Rooms.Where(r => r != null).Select(r => r.RoomId).ToList();
                foreach (string required in new[] { "A509", "A510" })
                    if (!ids.Contains(required)) problems.Add("Room " + required + " missing");
            }

            foreach (string p in problems) Debug.LogError("[PentaWay][Validate] " + p);
            Debug.Log("[PentaWay][Validate] rooms=" + (floor != null ? floor.Rooms.Count : 0) + " problems=" + problems.Count);
            return problems.Count;
        }

        public static bool BuildAndroid()
        {
            EditorUserBuildSettings.buildAppBundle = false;
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            System.IO.Directory.CreateDirectory("Builds");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            });
            BuildSummary summary = report.summary;
            Debug.Log("[PentaWay][Build] " + summary.result + "  size " + (summary.totalSize / (1024f * 1024f)).ToString("F1") +
                      " MB  errors " + summary.totalErrors + "  warnings " + summary.totalWarnings + "  time " + summary.totalTime);
            return summary.result == BuildResult.Succeeded;
        }
    }
}