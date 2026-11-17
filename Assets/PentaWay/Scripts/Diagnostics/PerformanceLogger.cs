using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace PentaWay
{
    public class PerformanceLogger : MonoBehaviour
    {
        const float SampleInterval = 1f;
        const float WarmupSeconds = 3f;

        ProfilerRecorder drawCallsRecorder;
        ProfilerRecorder batchesRecorder;
        ProfilerRecorder setPassRecorder;
        ProfilerRecorder trianglesRecorder;
        ProfilerRecorder verticesRecorder;
        ProfilerRecorder memoryRecorder;

        ARNavigator navigator;
        StreamWriter writer;
        string filePath;

        float sessionTime;
        float windowTime;
        int windowFrames;
        float windowWorstMs;

        int sampleCount;
        double fpsSum;
        float fpsMin = float.MaxValue;
        long drawCallsMax;
        long trianglesMax;

        float shownFps;
        float shownMs;
        long shownDrawCalls;
        long shownTriangles;

        GUIStyle textStyle;
        Texture2D backgroundTexture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateInDevelopmentBuild()
        {
            if (!Debug.isDebugBuild) return;
            if (FindAnyObjectByType<PerformanceLogger>() != null) return;
            var go = new GameObject("PerformanceLogger");
            DontDestroyOnLoad(go);
            go.AddComponent<PerformanceLogger>();
        }

        void OnEnable()
        {
            drawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            batchesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            verticesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            memoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            OpenFile();
        }

        void OnDisable()
        {
            drawCallsRecorder.Dispose();
            batchesRecorder.Dispose();
            setPassRecorder.Dispose();
            trianglesRecorder.Dispose();
            verticesRecorder.Dispose();
            memoryRecorder.Dispose();
            CloseFile();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && writer != null) writer.Flush();
        }

        void OnApplicationQuit()
        {
            CloseFile();
        }

        void OpenFile()
        {
            try
            {
                string name = "perf_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv";
                filePath = Path.Combine(Application.persistentDataPath, name);
                writer = new StreamWriter(filePath, false, new UTF8Encoding(false));
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "# device={0};os={1};unity={2};screen={3}x{4};refresh={5:F0};targetFrameRate={6};vSyncCount={7};quality={8}",
                    Clean(SystemInfo.deviceModel), Clean(SystemInfo.operatingSystem), Application.unityVersion,
                    Screen.width, Screen.height, Screen.currentResolution.refreshRateRatio.value,
                    Application.targetFrameRate, QualitySettings.vSyncCount,
                    Clean(QualitySettings.names[QualitySettings.GetQualityLevel()])));
                writer.WriteLine("time_s,state,fps,frame_ms_avg,frame_ms_worst,draw_calls,batches,setpass_calls,triangles,vertices,memory_mb");
                writer.Flush();
                Debug.Log("[PentaWay] Performance log: " + filePath);
            }
            catch (Exception e)
            {
                writer = null;
                Debug.LogWarning("[PentaWay] Performance log tidak dapat dibuat: " + e.Message);
            }
        }

        void CloseFile()
        {
            if (writer == null) return;
            writer.Flush();
            writer.Dispose();
            writer = null;
        }

        static string Clean(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value.Replace(",", " ").Replace(";", " ");
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            sessionTime += dt;
            windowTime += dt;
            windowFrames++;
            float ms = dt * 1000f;
            if (ms > windowWorstMs) windowWorstMs = ms;
            if (windowTime >= SampleInterval) WriteSample();
        }

        void WriteSample()
        {
            float fps = windowFrames / windowTime;
            float avgMs = windowTime * 1000f / windowFrames;
            long drawCalls = drawCallsRecorder.Valid ? drawCallsRecorder.LastValue : -1;
            long batches = batchesRecorder.Valid ? batchesRecorder.LastValue : -1;
            long setPass = setPassRecorder.Valid ? setPassRecorder.LastValue : -1;
            long triangles = trianglesRecorder.Valid ? trianglesRecorder.LastValue : -1;
            long vertices = verticesRecorder.Valid ? verticesRecorder.LastValue : -1;
            double memoryMb = memoryRecorder.Valid ? memoryRecorder.LastValue / (1024.0 * 1024.0) : -1.0;

            if (navigator == null) navigator = FindAnyObjectByType<ARNavigator>();
            string state = navigator != null ? navigator.State.ToString() : "-";

            if (writer != null)
            {
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F1},{1},{2:F1},{3:F2},{4:F2},{5},{6},{7},{8},{9},{10:F1}",
                    sessionTime, state, fps, avgMs, windowWorstMs, drawCalls, batches, setPass, triangles, vertices, memoryMb));
                writer.Flush();
            }

            if (sessionTime > WarmupSeconds)
            {
                sampleCount++;
                fpsSum += fps;
                if (fps < fpsMin) fpsMin = fps;
                if (drawCalls > drawCallsMax) drawCallsMax = drawCalls;
                if (triangles > trianglesMax) trianglesMax = triangles;
            }

            shownFps = fps;
            shownMs = avgMs;
            shownDrawCalls = drawCalls;
            shownTriangles = triangles;

            windowTime = 0f;
            windowFrames = 0;
            windowWorstMs = 0f;
        }

        void OnGUI()
        {
            if (textStyle == null)
            {
                textStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.014f)),
                    wordWrap = false
                };
                textStyle.normal.textColor = Color.white;
                backgroundTexture = new Texture2D(1, 1);
                backgroundTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
                backgroundTexture.Apply();
            }

            string average = sampleCount > 0 ? (fpsSum / sampleCount).ToString("F1", CultureInfo.InvariantCulture) : "-";
            string minimum = sampleCount > 0 ? fpsMin.ToString("F1", CultureInfo.InvariantCulture) : "-";
            string text = string.Format(CultureInfo.InvariantCulture,
                "FPS {0:F1} ({1:F1} ms)\nRata-rata {2} | Min {3}\nDraw calls {4} (maks {5})\nTriangles {6} (maks {7})\n{8}",
                shownFps, shownMs, average, minimum, shownDrawCalls, drawCallsMax, shownTriangles, trianglesMax,
                filePath != null ? Path.GetFileName(filePath) : "log tidak tersimpan");

            float lineHeight = textStyle.fontSize * 1.35f;
            Rect area = new Rect(8f, Screen.height * 0.40f, Screen.width * 0.55f, lineHeight * 5f + 12f);
            GUI.DrawTexture(area, backgroundTexture);
            GUI.Label(new Rect(area.x + 8f, area.y + 6f, area.width - 16f, area.height - 12f), text, textStyle);
        }

        void OnDestroy()
        {
            if (backgroundTexture != null) Destroy(backgroundTexture);
        }
    }
}
