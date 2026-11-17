using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PentaWay
{
    public enum PathStyle { Ribbon, Chevrons }

    public class PathArrowRenderer : MonoBehaviour
    {
        public PathStyle style = PathStyle.Ribbon;

        [Header("Ribbon")]
        public Material ribbonMaterial;
        public float ribbonWidth = 0.32f;
        public float cornerRadius = 0.9f;
        public float fadeInLength = 0.8f;

        [Header("Chevrons")]
        public Material arrowMaterial;
        public float spacing = 0.7f;
        public float speed = 0.8f;
        public float arrowSize = 0.4f;
        public int maxArrows = 60;

        [Header("Door hint (arrow toward the room's door)")]
        [Tooltip("The arrow to the left/right only appears when the end of the path is closer than this (metres).")]
        public float doorHintDistance = 12f;
        public float doorArrowSize = 0.55f;
        public float doorArrowSpacing = 0.6f;

        [Header("Common")]
        public float heightAboveFloor = 0.03f;
        public float maxDrawDistance = 30f;

        [Header("Floor")]
        public FloorModel floorModel;
        public bool keepCornersOnNavMesh = true;

        const int CornerSteps = 8;
        const int CapSegments = 10;
        const float NavMeshTolerance = 0.03f;

        Mesh ribbonMesh;
        Transform ribbonTransform;
        MeshRenderer ribbonRenderer;
        readonly List<Vector3> corners = new List<Vector3>();
        readonly List<float> cumulative = new List<float>();
        readonly List<Vector3> points = new List<Vector3>();
        readonly List<float> pointDistances = new List<float>();
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();
        readonly List<Transform> pool = new List<Transform>();
        Mesh arrowMesh;
        readonly List<Transform> hintPool = new List<Transform>();
        bool hintOn;
        Vector3 hintStart, hintDirection;
        float hintLength;
        float totalLength;
        int builtVersion = -1;

        void Awake()
        {
            if (floorModel == null) floorModel = FindAnyObjectByType<FloorModel>();
            arrowMesh = BuildChevronMesh();

            var ribbon = new GameObject("Ribbon");
            ribbon.transform.SetParent(transform, false);
            ribbonTransform = ribbon.transform;
            ribbonMesh = new Mesh { name = "PentaWay Path Ribbon" };
            ribbonMesh.MarkDynamic();
            ribbon.AddComponent<MeshFilter>().sharedMesh = ribbonMesh;
            ribbonRenderer = ribbon.AddComponent<MeshRenderer>();
            ribbonRenderer.sharedMaterial = ribbonMaterial != null ? ribbonMaterial : arrowMaterial;
            ribbonRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ribbonRenderer.receiveShadows = false;
            ribbonRenderer.enabled = false;
        }

        public void SetPath(IReadOnlyList<Vector3> pathCorners, int version)
        {
            if (pathCorners == null || pathCorners.Count < 2)
            {
                Clear();
                return;
            }
            if (version == builtVersion && corners.Count > 0) return;
            builtVersion = version;

            corners.Clear();
            cumulative.Clear();
            totalLength = 0f;
            for (int i = 0; i < pathCorners.Count; i++)
            {
                Vector3 p = OnFloor(pathCorners[i]);
                if (i > 0) totalLength += Vector3.Distance(corners[i - 1], p);
                corners.Add(p);
                cumulative.Add(totalLength);
            }

            if (style == PathStyle.Ribbon) BuildRibbon();
            ribbonRenderer.enabled = style == PathStyle.Ribbon;
        }

        public void Clear()
        {
            corners.Clear();
            builtVersion = -1;
            if (ribbonRenderer != null) ribbonRenderer.enabled = false;
            for (int i = 0; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
            ClearDoorHint();
        }

        public void SetDoorHint(Vector3 pathEnd, Vector3 door)
        {
            Vector3 start = OnFloor(pathEnd);
            Vector3 direction = OnFloor(door) - start;
            direction -= FloorUp * Vector3.Dot(direction, FloorUp);
            if (direction.sqrMagnitude < 1e-4f) { ClearDoorHint(); return; }
            hintLength = Mathf.Max(direction.magnitude, doorArrowSpacing * 2f);
            hintDirection = direction.normalized;
            hintStart = start;
            hintOn = true;
        }

        public void ClearDoorHint()
        {
            hintOn = false;
            for (int i = 0; i < hintPool.Count; i++)
                if (hintPool[i].gameObject.activeSelf) hintPool[i].gameObject.SetActive(false);
        }

        Vector3 FloorUp => floorModel != null ? floorModel.transform.up : Vector3.up;

        Vector3 OnFloor(Vector3 world)
        {
            if (floorModel == null) return world;
            return floorModel.PlanToWorld(floorModel.WorldToPlan(world));
        }

        void UpdateDoorHint()
        {
            int used = 0;
            if (hintOn && (arrowMaterial != null || ribbonMaterial != null))
            {
                Vector3 lift = FloorUp * (heightAboveFloor + 0.005f);
                Quaternion rotation = Quaternion.LookRotation(hintDirection, FloorUp);
                float offset = Mathf.Repeat(Time.time * speed, doorArrowSpacing);
                for (float d = offset; d <= hintLength && used < 6; d += doorArrowSpacing)
                {
                    Transform arrow = GetHintArrow(used++);
                    arrow.SetPositionAndRotation(hintStart + hintDirection * d + lift, rotation);
                    float t = Mathf.Clamp01(d / hintLength);
                    arrow.localScale = Vector3.one * doorArrowSize * Mathf.Lerp(0.8f, 1.1f, t) * Mathf.Clamp01((hintLength - d) / 0.4f + 0.3f);
                }
            }
            for (int i = used; i < hintPool.Count; i++)
                if (hintPool[i].gameObject.activeSelf) hintPool[i].gameObject.SetActive(false);
        }

        Transform GetHintArrow(int index)
        {
            while (hintPool.Count <= index)
            {
                var go = new GameObject("DoorArrow_" + hintPool.Count);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = arrowMaterial != null ? arrowMaterial : ribbonMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                hintPool.Add(go.transform);
            }
            Transform arrow = hintPool[index];
            if (!arrow.gameObject.activeSelf) arrow.gameObject.SetActive(true);
            return arrow;
        }

        void BuildRibbon()
        {
            float fullLength = BuildSmoothPoints();
            vertices.Clear();
            uvs.Clear();
            colors.Clear();
            triangles.Clear();
            ribbonMesh.Clear();
            if (points.Count < 2) return;

            Vector3 up = FloorUp;
            Vector3 lift = up * heightAboveFloor;
            float half = ribbonWidth * 0.5f;
            Vector3 lastTangent = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;

            for (int i = 0; i < points.Count; i++)
            {
                float distance = pointDistances[i];
                Vector3 tangent = Tangent(i, up);
                if (tangent.sqrMagnitude < 1e-6f) tangent = lastTangent;
                lastTangent = tangent;
                Vector3 right = Vector3.Cross(up, tangent).normalized;

                float alpha = fadeInLength > 0f ? Mathf.Clamp01(distance / fadeInLength) : 1f;
                float v = (distance - fullLength) / ribbonWidth;
                AddVertex(points[i] + lift - right * half, new Vector2(0f, v), alpha);
                AddVertex(points[i] + lift + right * half, new Vector2(1f, v), alpha);

                if (i > 0)
                {
                    int k = vertices.Count - 4;
                    triangles.Add(k); triangles.Add(k + 2); triangles.Add(k + 1);
                    triangles.Add(k + 1); triangles.Add(k + 2); triangles.Add(k + 3);
                }
            }

            Vector3 end = points[points.Count - 1] + lift;
            float endDistance = pointDistances[points.Count - 1];
            Vector3 endRight = Vector3.Cross(up, lastTangent).normalized;
            int center = vertices.Count;
            AddVertex(end, new Vector2(0.5f, (endDistance - fullLength) / ribbonWidth), 1f);
            for (int s = 0; s <= CapSegments; s++)
            {
                float phi = Mathf.PI * s / CapSegments;
                Vector3 offset = (endRight * Mathf.Cos(phi) + lastTangent * Mathf.Sin(phi)) * half;
                AddVertex(end + offset, new Vector2(0.5f + 0.5f * Mathf.Cos(phi), (endDistance - fullLength + Mathf.Sin(phi) * half) / ribbonWidth), 1f);
                if (s > 0)
                {
                    triangles.Add(center); triangles.Add(center + s + 1); triangles.Add(center + s);
                }
            }

            ribbonMesh.SetVertices(vertices);
            ribbonMesh.SetUVs(0, uvs);
            ribbonMesh.SetColors(colors);
            ribbonMesh.SetTriangles(triangles, 0);
            ribbonMesh.RecalculateBounds();
        }

        void AddVertex(Vector3 position, Vector2 uv, float alpha)
        {
            vertices.Add(ribbonTransform.InverseTransformPoint(position));
            uvs.Add(uv);
            colors.Add(new Color(1f, 1f, 1f, alpha));
        }

        Vector3 Tangent(int i, Vector3 up)
        {
            Vector3 t = i == 0 ? points[1] - points[0]
                : i == points.Count - 1 ? points[i] - points[i - 1]
                : points[i + 1] - points[i - 1];
            return Vector3.ProjectOnPlane(t, up).normalized;
        }

        float BuildSmoothPoints()
        {
            points.Clear();
            pointDistances.Clear();
            points.Add(corners[0]);

            for (int i = 1; i < corners.Count - 1; i++)
            {
                Vector3 a = corners[i - 1], b = corners[i], c = corners[i + 1];
                float cut = Mathf.Min(cornerRadius, Vector3.Distance(a, b) * 0.5f, Vector3.Distance(b, c) * 0.5f);
                Vector3 inDir = (a - b).normalized, outDir = (c - b).normalized;
                while (cut >= 0.05f && keepCornersOnNavMesh && !CurveOnNavMesh(b, inDir, outDir, cut)) cut *= 0.5f;
                if (cut < 0.05f)
                {
                    points.Add(b);
                    continue;
                }

                Vector3 p1 = b + inDir * cut;
                Vector3 p2 = b + outDir * cut;
                for (int s = 0; s <= CornerSteps; s++)
                {
                    float t = s / (float)CornerSteps;
                    points.Add(Bezier(p1, b, p2, t));
                }
            }
            points.Add(corners[corners.Count - 1]);

            float run = 0f;
            pointDistances.Add(0f);
            for (int i = 1; i < points.Count; i++)
            {
                run += Vector3.Distance(points[i - 1], points[i]);
                pointDistances.Add(run);
            }
            float fullLength = run;

            for (int i = 1; i < points.Count; i++)
            {
                if (pointDistances[i] <= maxDrawDistance) continue;
                float segment = pointDistances[i] - pointDistances[i - 1];
                float t = (maxDrawDistance - pointDistances[i - 1]) / Mathf.Max(segment, 1e-4f);
                points[i] = Vector3.Lerp(points[i - 1], points[i], t);
                pointDistances[i] = maxDrawDistance;
                points.RemoveRange(i + 1, points.Count - i - 1);
                pointDistances.RemoveRange(i + 1, pointDistances.Count - i - 1);
                break;
            }
            return fullLength;
        }

        static Vector3 Bezier(Vector3 p1, Vector3 b, Vector3 p2, float t)
        {
            return (1 - t) * (1 - t) * p1 + 2 * (1 - t) * t * b + t * t * p2;
        }

        bool CurveOnNavMesh(Vector3 corner, Vector3 inDir, Vector3 outDir, float cut)
        {
            Vector3 p1 = corner + inDir * cut;
            Vector3 p2 = corner + outDir * cut;
            for (int s = 1; s < 4; s++)
            {
                Vector3 p = Bezier(p1, corner, p2, s * 0.25f);
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(p, out hit, 0.5f, NavMesh.AllAreas)) return false;
                Vector3 offset = Vector3.ProjectOnPlane(hit.position - p, FloorUp);
                if (offset.sqrMagnitude > NavMeshTolerance * NavMeshTolerance) return false;
            }
            return true;
        }

        void LateUpdate()
        {
            UpdateDoorHint();
            if (style != PathStyle.Chevrons) return;

            int used = 0;
            if (corners.Count >= 2 && totalLength > 0.01f)
            {
                Vector3 up = FloorUp;
                Vector3 lift = up * heightAboveFloor;
                float phase = Mathf.Repeat(Time.time * speed, spacing);
                float limit = Mathf.Min(totalLength, maxDrawDistance);
                float first = Mathf.Repeat(totalLength + phase, spacing);
                for (float d = first; d < limit && used < maxArrows; d += spacing)
                {
                    Vector3 position, direction;
                    Sample(d, up, out position, out direction);
                    Transform arrow = GetArrow(used++);
                    arrow.SetPositionAndRotation(position + lift, Quaternion.LookRotation(direction, up));
                    arrow.localScale = Vector3.one * arrowSize;
                }
            }
            for (int i = used; i < pool.Count; i++)
                if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
        }

        void Sample(float distance, Vector3 up, out Vector3 position, out Vector3 direction)
        {
            int i = 1;
            while (i < corners.Count - 1 && cumulative[i] < distance) i++;
            Vector3 a = corners[i - 1], b = corners[i];
            float segment = cumulative[i] - cumulative[i - 1];
            position = Vector3.Lerp(a, b, segment > 1e-4f ? (distance - cumulative[i - 1]) / segment : 0f);
            direction = Vector3.ProjectOnPlane(b - a, up);
            if (direction.sqrMagnitude < 1e-6f) direction = Vector3.ProjectOnPlane(Vector3.forward, up);
        }

        Transform GetArrow(int index)
        {
            while (pool.Count <= index)
            {
                var go = new GameObject("Arrow_" + pool.Count);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = arrowMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                pool.Add(go.transform);
            }
            Transform arrow = pool[index];
            if (!arrow.gameObject.activeSelf) arrow.gameObject.SetActive(true);
            return arrow;
        }

        static Mesh BuildChevronMesh()
        {
            const float t = 0.28f;
            var mesh = new Mesh { name = "PentaWay Chevron" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.25f), new Vector3(0f, 0f, 0.25f), new Vector3(0.5f, 0f, -0.25f),
                new Vector3(-0.5f, 0f, -0.25f - t), new Vector3(0f, 0f, 0.25f - t), new Vector3(0.5f, 0f, -0.25f - t),
            };
            mesh.triangles = new[] { 0, 1, 4, 0, 4, 3, 1, 2, 5, 1, 5, 4 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
