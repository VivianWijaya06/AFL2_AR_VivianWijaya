using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PentaWay
{
    [DefaultExecutionOrder(1000)]
    public class CenterGuideArrow : MonoBehaviour
    {
        public ARNavigator navigator;
        public float distance = 1.6f;
        public float length = 0.42f;
        public float tilt = 1f;
        public float lookAhead = 3f;
        public float turnSmoothing = 8f;
        public float bobAmount = 0.02f;
        public Color topColor = new Color(0.24f, 0.51f, 1f, 1f);
        public Color sideColor = new Color(0.10f, 0.27f, 0.70f, 1f);
        public Color outlineColor = Color.white;

        const string ShaderResource = "GuideArrow";
        const string ShaderName = "PentaWay/GuideArrow";
        const float Thickness = 0.1f;
        const float OutlineWidth = 0.05f;

        static readonly Vector2[] Shape =
        {
            new Vector2(0f, 0.5f),
            new Vector2(-0.36f, 0.04f),
            new Vector2(-0.13f, 0.04f),
            new Vector2(-0.13f, -0.5f),
            new Vector2(0.13f, -0.5f),
            new Vector2(0.13f, 0.04f),
            new Vector2(0.36f, 0.04f)
        };

        MeshRenderer meshRenderer;
        Mesh mesh;
        Material material;
        bool ownsMaterial;
        Vector3 smoothedDirection;
        bool hasDirection;
        float visibility;

        public static CenterGuideArrow Create(ARNavigator navigator)
        {
            var go = new GameObject("CenterGuideArrow");
            var arrow = go.AddComponent<CenterGuideArrow>();
            arrow.navigator = navigator;
            return arrow;
        }

        void Awake()
        {
            mesh = BuildMesh();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.enabled = false;

            Shader shader = Resources.Load<Shader>(ShaderResource);
            if (shader == null) shader = Shader.Find(ShaderName);
            if (shader != null)
            {
                material = new Material(shader);
                ownsMaterial = true;
                meshRenderer.sharedMaterial = material;
            }
        }

        void Start()
        {
            if (material != null || navigator == null || navigator.pathRenderer == null) return;
            material = navigator.pathRenderer.arrowMaterial;
            if (material != null) meshRenderer.sharedMaterial = material;
            else Debug.LogWarning("[PentaWay] Shader panah tengah tidak ditemukan.");
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (ownsMaterial && material != null) Destroy(material);
        }

        void LateUpdate()
        {
            Camera cam = navigator != null && navigator.arCamera != null ? navigator.arCamera : Camera.main;
            Vector3 up = navigator != null && navigator.floorModel != null ? navigator.floorModel.transform.up : Vector3.up;
            Vector3 direction = Vector3.zero;
            bool active = navigator != null && cam != null && material != null &&
                          navigator.State == NavState.Navigating && navigator.Mode == PositioningMode.Compass &&
                          navigator.TryGetGuideDirection(lookAhead, out direction);

            float dt = Time.deltaTime;
            if (active)
            {
                if (!hasDirection)
                {
                    smoothedDirection = direction;
                    hasDirection = true;
                }
                else
                {
                    Vector3 blended = Vector3.Slerp(smoothedDirection, direction, 1f - Mathf.Exp(-turnSmoothing * dt));
                    blended = Vector3.ProjectOnPlane(blended, up);
                    smoothedDirection = blended.sqrMagnitude > 1e-6f ? blended.normalized : direction;
                }
            }

            visibility = Mathf.MoveTowards(visibility, active ? 1f : 0f, dt * 4f);
            if (visibility <= 0f) hasDirection = false;
            bool show = visibility > 0f && hasDirection && cam != null;
            if (meshRenderer.enabled != show) meshRenderer.enabled = show;
            if (!show) return;

            Transform view = cam.transform;
            Vector3 normal = up + (-view.forward) * tilt;
            normal = normal.sqrMagnitude > 1e-4f ? normal.normalized : -view.forward;
            Vector3 pointing = Vector3.ProjectOnPlane(smoothedDirection, normal);
            if (pointing.sqrMagnitude < 1e-4f) pointing = Vector3.ProjectOnPlane(view.up, normal);
            pointing.Normalize();

            float bob = Mathf.Sin(Time.unscaledTime * 3.5f) * bobAmount;
            Vector3 position = view.position + view.forward * distance + pointing * bob;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(pointing, normal));
            float eased = visibility * visibility * (3f - 2f * visibility);
            transform.localScale = Vector3.one * (length * eased);
        }

        Mesh BuildMesh()
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();

            AddCap(Offset(Shape, OutlineWidth), 0f, outlineColor, outlineColor, vertices, colors, triangles);
            AddCap(Shape, -Thickness, sideColor, sideColor, vertices, colors, triangles);
            AddSides(Shape, sideColor, vertices, colors, triangles);
            Color tail = new Color(topColor.r * 0.8f, topColor.g * 0.8f, topColor.b * 0.8f, topColor.a);
            AddCap(Shape, 0f, tail, topColor, vertices, colors, triangles);

            var result = new Mesh { name = "CenterGuideArrow" };
            result.SetVertices(vertices);
            result.SetColors(colors);
            result.SetTriangles(triangles, 0);
            result.RecalculateBounds();
            result.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return result;
        }

        static void AddCap(Vector2[] shape, float y, Color back, Color front, List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            int start = vertices.Count;
            foreach (Vector2 p in shape)
            {
                vertices.Add(new Vector3(p.x, y, p.y));
                colors.Add(Color.Lerp(back, front, Mathf.Clamp01(p.y + 0.5f)));
            }
            triangles.Add(start); triangles.Add(start + 6); triangles.Add(start + 1);
            triangles.Add(start + 2); triangles.Add(start + 4); triangles.Add(start + 3);
            triangles.Add(start + 2); triangles.Add(start + 5); triangles.Add(start + 4);
        }

        static void AddSides(Vector2[] shape, Color color, List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            for (int i = 0; i < shape.Length; i++)
            {
                Vector2 a = shape[i];
                Vector2 b = shape[(i + 1) % shape.Length];
                int start = vertices.Count;
                vertices.Add(new Vector3(a.x, 0f, a.y));
                vertices.Add(new Vector3(b.x, 0f, b.y));
                vertices.Add(new Vector3(b.x, -Thickness, b.y));
                vertices.Add(new Vector3(a.x, -Thickness, a.y));
                for (int k = 0; k < 4; k++) colors.Add(color);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }
        }

        static Vector2[] Offset(Vector2[] shape, float width)
        {
            var result = new Vector2[shape.Length];
            for (int i = 0; i < shape.Length; i++)
            {
                Vector2 previous = shape[(i + shape.Length - 1) % shape.Length];
                Vector2 current = shape[i];
                Vector2 next = shape[(i + 1) % shape.Length];
                Vector2 e1 = (current - previous).normalized;
                Vector2 e2 = (next - current).normalized;
                Vector2 n1 = new Vector2(e1.y, -e1.x);
                Vector2 n2 = new Vector2(e2.y, -e2.x);
                Vector2 miter = (n1 + n2) / Mathf.Max(0.2f, 1f + Vector2.Dot(n1, n2));
                result[i] = current + Vector2.ClampMagnitude(miter, 3f) * width;
            }
            return result;
        }
    }
}
