using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace PentaWay
{
    public class CameraFeedView : MonoBehaviour
    {
        public Camera targetCamera;
        public Material feedMaterial;

        WebCamTexture webcam;
        Transform quad;
        Material material;
        bool wanted;

        public void Show(bool show)
        {
            wanted = show;
            if (show)
            {
                if (!EnsureQuad()) return;
                quad.gameObject.SetActive(true);
                StopAllCoroutines();
                StartCoroutine(StartCamera());
            }
            else
            {
                if (webcam != null && webcam.isPlaying) webcam.Stop();
                if (quad != null) quad.gameObject.SetActive(false);
            }
        }

        bool EnsureQuad()
        {
            if (quad != null) return true;
            if (targetCamera == null)
            {
                Debug.LogError("[PentaWay] CameraFeedView has no camera.");
                return false;
            }

            Shader shader = feedMaterial != null ? null : Shader.Find("PentaWay/CameraFeed");
            if (feedMaterial == null && shader == null)
            {
                Debug.LogError("[PentaWay] Camera feed material missing. Run PentaWay > Set Up Scene.");
                return false;
            }
            material = feedMaterial != null ? new Material(feedMaterial) : new Material(shader);

            var go = new GameObject("CameraFeedQuad");
            go.transform.SetParent(targetCamera.transform, false);
            var mesh = new Mesh { name = "PentaWay Camera Feed" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            quad = go.transform;
            return true;
        }

        IEnumerator StartCamera()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Permission.RequestUserPermission(Permission.Camera);
                float timeout = Time.realtimeSinceStartup + 20f;
                while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && Time.realtimeSinceStartup < timeout)
                    yield return null;
                if (!Permission.HasUserAuthorizedPermission(Permission.Camera)) yield break;
            }
#endif
            if (!wanted) yield break;
            if (webcam == null)
            {
                string deviceName = null;
                foreach (WebCamDevice device in WebCamTexture.devices)
                {
                    if (!device.isFrontFacing) { deviceName = device.name; break; }
                }
                if (deviceName == null && WebCamTexture.devices.Length > 0) deviceName = WebCamTexture.devices[0].name;
                if (deviceName == null) yield break;
                webcam = new WebCamTexture(deviceName, 1280, 720, 30);
            }
            material.mainTexture = webcam;
            webcam.Play();
        }

        void LateUpdate()
        {
            if (quad == null || !quad.gameObject.activeSelf || targetCamera == null) return;

            float distance = targetCamera.farClipPlane * 0.9f;
            float viewHeight = 2f * distance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float viewWidth = viewHeight * targetCamera.aspect;

            int angle = 0;
            float textureAspect = 16f / 9f;
            bool mirrored = false;
            if (webcam != null && webcam.isPlaying && webcam.width >= 32)
            {
                angle = webcam.videoRotationAngle;
                textureAspect = webcam.width / (float)webcam.height;
                mirrored = webcam.videoVerticallyMirrored;
            }
            bool sideways = angle % 180 != 0;
            float shownAspect = sideways ? 1f / textureAspect : textureAspect;

            float w, h;
            if (viewWidth / viewHeight > shownAspect) { w = viewWidth; h = viewWidth / shownAspect; }
            else { h = viewHeight; w = viewHeight * shownAspect; }

            Vector3 scale = sideways ? new Vector3(h, w, 1f) : new Vector3(w, h, 1f);
            if (mirrored) scale.y = -scale.y;
            quad.localPosition = new Vector3(0f, 0f, distance);
            quad.localRotation = Quaternion.Euler(0f, 0f, -angle);
            quad.localScale = scale;
        }

        void OnDestroy()
        {
            if (webcam != null) webcam.Stop();
        }
    }
}
