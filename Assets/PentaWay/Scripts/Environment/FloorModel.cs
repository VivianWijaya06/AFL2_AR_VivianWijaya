using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace PentaWay
{
    [DisallowMultipleComponent]
    public class FloorModel : MonoBehaviour
    {
        public const string GeneratedRootName = "Generated";
        const string FloorsName = "Floors";
        const string WallsName = "Walls";
        const string BlocksName = "Blocks";
        const string RoomsName = "Rooms";
        const string ReferencePointsName = "ReferencePoints";
        const string TestStartName = "TestStart";

        [Header("Materials")]
        public Material floorMaterial;
        public Material wallMaterial;
        public Material blockMaterial;
        [FormerlySerializedAs("markerMaterial")] public Material referencePointMaterial;

        [Header("Layers")]
        public string wallLayerName = "FloorWalls";

        [Header("Runtime")]
        public bool generateOnAwakeIfMissing = true;
        public bool buildNavMeshOnAwake = true;

        [Header("NavMesh agent")]
        public float agentRadius = 0.2f;
        public float agentHeight = 1.8f;
        public float agentClimb = 0.25f;
        public float voxelSize = 0.066f;

        NavMeshData navMeshData;
        NavMeshDataInstance navMeshInstance;
        readonly List<RoomDestination> rooms = new List<RoomDestination>();
        Transform testStart;

        public IReadOnlyList<RoomDestination> Rooms
        {
            get
            {
                if (rooms.Count == 0) CacheReferences();
                return rooms;
            }
        }

        public Transform TestStart
        {
            get
            {
                if (testStart == null) CacheReferences();
                return testStart;
            }
        }

        public int WallLayer
        {
            get
            {
                int layer = LayerMask.NameToLayer(wallLayerName);
                return layer < 0 ? 0 : layer;
            }
        }

        void Awake()
        {
            if (transform.Find(GeneratedRootName) == null && generateOnAwakeIfMissing) Generate();
            CacheReferences();
            if (buildNavMeshOnAwake) BuildNavMesh();
        }

        void OnEnable()
        {
            if (navMeshData != null && !navMeshInstance.valid) RefreshNavMeshPose();
        }

        void OnDisable()
        {
            if (navMeshInstance.valid) navMeshInstance.Remove();
        }

        public Vector3 PlanToWorld(Vector2 plan, float height = 0f) => transform.TransformPoint(FloorPlanData.ToLocal(plan, height));

        public Vector2 WorldToPlan(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            return new Vector2(local.x, local.z);
        }

        public RoomDestination FindRoom(string id)
        {
            foreach (RoomDestination room in Rooms)
                if (room != null && room.RoomId == id) return room;
            return null;
        }

        public void ApplyOcclusionMode(Material occluderMaterial)
        {
            Transform root = transform.Find(GeneratedRootName);
            if (root == null) return;
            SetGroupRenderers(root.Find(FloorsName), null);
            SetGroupRenderers(root.Find(ReferencePointsName), null);
            SetGroupRenderers(root.Find(WallsName), occluderMaterial);
            SetGroupRenderers(root.Find(BlocksName), occluderMaterial);
        }

        static void SetGroupRenderers(Transform group, Material material)
        {
            if (group == null) return;
            foreach (Renderer r in group.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = material != null;
                if (material != null) r.sharedMaterial = material;
            }
        }

        [ContextMenu("Generate floor model")]
        public void Generate()
        {
            ClearGenerated();
            EnsureMaterials();

            int wallLayer = LayerMask.NameToLayer(wallLayerName);
            if (wallLayer < 0)
            {
                Debug.LogWarning("[PentaWay] Layer '" + wallLayerName + "' does not exist. Walls use the Default layer.");
                wallLayer = 0;
            }

            Transform root = CreateGroup(transform, GeneratedRootName);
            Transform floors = CreateGroup(root, FloorsName);
            Transform walls = CreateGroup(root, WallsName);
            Transform blocks = CreateGroup(root, BlocksName);
            Transform roomGroup = CreateGroup(root, RoomsName);
            Transform points = CreateGroup(root, ReferencePointsName);

            for (int i = 0; i < FloorPlanData.FloorSlabs.Length; i++)
            {
                RectXZ r = FloorPlanData.FloorSlabs[i];
                float t = FloorPlanData.FloorThickness;
                CreateBox(floors, "Floor_" + i, FloorPlanData.ToLocal(r.Center, -t * 0.5f), Quaternion.identity,
                          new Vector3(r.Size.x, t, r.Size.y), floorMaterial, 0);
            }

            for (int i = 0; i < FloorPlanData.Walls.Length; i++)
                BuildWall(walls, FloorPlanData.Walls[i], i, wallLayer);

            float h = FloorPlanData.WallHeight;
            foreach (BlockInfo b in FloorPlanData.Blocks)
                CreateBox(blocks, "Block_" + b.Name, FloorPlanData.ToLocal(b.Rect.Center, h * 0.5f), Quaternion.identity,
                          new Vector3(b.Rect.Size.x, h, b.Rect.Size.y), blockMaterial, wallLayer);

            foreach (RoomInfo info in FloorPlanData.Rooms)
                CreateRoom(roomGroup, info);

            foreach (ReferencePoint point in FloorPlanData.ReferencePoints)
                CreateReferencePointPreview(points, point);

            Transform start = new GameObject(TestStartName).transform;
            start.SetParent(root, false);
            start.localPosition = FloorPlanData.ToLocal(FloorPlanData.TestStartPosition);
            start.localRotation = Quaternion.Euler(0f, FloorPlanData.ToYaw(FloorPlanData.TestStartFacing), 0f);

            CacheReferences();
            Debug.Log("[PentaWay] Floor model generated: " + rooms.Count + " rooms, " +
                      walls.childCount + " wall pieces, " + blocks.childCount + " blocks.");
        }

        void ClearGenerated()
        {
            Transform old = transform.Find(GeneratedRootName);
            while (old != null)
            {
                old.name = GeneratedRootName + "_old";
                old.SetParent(null);
                SafeDestroy(old.gameObject);
                old = transform.Find(GeneratedRootName);
            }
            rooms.Clear();
            testStart = null;
        }

        void BuildWall(Transform parent, WallInfo wall, int index, int layer)
        {
            Vector2 a = wall.A;
            Vector2 dir = wall.B - wall.A;
            float length = dir.magnitude;
            if (length < 0.01f) return;
            dir /= length;

            var cuts = new List<Vector3>();
            float t;
            foreach (RoomInfo room in FloorPlanData.Rooms)
                if (IsOnWall(a, dir, length, room.Door, out t))
                    cuts.Add(new Vector3(t - room.DoorWidth * 0.5f, t + room.DoorWidth * 0.5f, 1f));
            foreach (OpeningInfo o in FloorPlanData.ExtraOpenings)
                if (IsOnWall(a, dir, length, o.Center, out t))
                    cuts.Add(new Vector3(t - o.Width * 0.5f, t + o.Width * 0.5f, o.HasLintel ? 1f : 0f));
            cuts.Sort((p, q) => p.x.CompareTo(q.x));

            float half = FloorPlanData.WallThickness * 0.5f;
            float cursor = -half;
            float end = length + half;
            int piece = 0;

            foreach (Vector3 cut in cuts)
            {
                float s = Mathf.Clamp(cut.x, cursor, end);
                float e = Mathf.Clamp(cut.y, cursor, end);
                if (s - cursor > 0.01f)
                    CreateWallPiece(parent, "Wall_" + index + "_" + piece++, a, dir, cursor, s, 0f, FloorPlanData.WallHeight, layer);
                if (e - s > 0.01f && cut.z > 0.5f)
                    CreateWallPiece(parent, "Lintel_" + index + "_" + piece++, a, dir, s, e, FloorPlanData.DoorHeight, FloorPlanData.WallHeight, layer);
                cursor = Mathf.Max(cursor, e);
            }
            if (end - cursor > 0.01f)
                CreateWallPiece(parent, "Wall_" + index + "_" + piece, a, dir, cursor, end, 0f, FloorPlanData.WallHeight, layer);
        }

        static bool IsOnWall(Vector2 a, Vector2 dir, float length, Vector2 p, out float t)
        {
            Vector2 v = p - a;
            t = Vector2.Dot(v, dir);
            float distance = Mathf.Abs(v.x * dir.y - v.y * dir.x);
            return distance < 0.05f && t > 0f && t < length;
        }

        void CreateWallPiece(Transform parent, string name, Vector2 a, Vector2 dir, float t0, float t1, float y0, float y1, int layer)
        {
            Vector2 mid = a + dir * ((t0 + t1) * 0.5f);
            Quaternion rot = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
            CreateBox(parent, name, FloorPlanData.ToLocal(mid, (y0 + y1) * 0.5f), rot,
                      new Vector3(FloorPlanData.WallThickness, y1 - y0, t1 - t0), wallMaterial, layer);
        }

        void CreateRoom(Transform parent, RoomInfo info)
        {
            var go = new GameObject("Room_" + info.Id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = FloorPlanData.ToLocal(info.Rect.Center);

            Vector2 side = FloorPlanData.ToVector(info.WalkwaySide);
            Quaternion facing = Quaternion.Euler(0f, FloorPlanData.ToYaw(info.WalkwaySide), 0f);
            Transform door = CreatePoint(go.transform, "Door", FloorPlanData.ToLocal(info.Door), facing);
            Transform approach = CreatePoint(go.transform, "Approach", FloorPlanData.ToLocal(FloorPlanData.ApproachPoint(info)), facing);
            Vector2 labelPlan = info.Door + side * (FloorPlanData.WallThickness * 0.5f + 0.05f);
            Transform label = CreatePoint(go.transform, "Label", FloorPlanData.ToLocal(labelPlan, FloorPlanData.LabelHeight), facing);

            go.AddComponent<RoomDestination>().Setup(info, door, approach, label);
        }

        static Transform CreatePoint(Transform room, string name, Vector3 modelLocal, Quaternion rotation)
        {
            var t = new GameObject(name).transform;
            t.SetParent(room, false);
            t.localPosition = modelLocal - room.localPosition;
            t.localRotation = rotation;
            return t;
        }

        void CreateReferencePointPreview(Transform parent, ReferencePoint point)
        {
            Quaternion rot = Quaternion.Euler(0f, FloorPlanData.ToYaw(point.Facing), 0f);
            GameObject go = CreateBox(parent, "Point_" + point.Name, FloorPlanData.ReferenceSurfacePoint(point), rot,
                                      new Vector3(0.25f, 0.25f, 0.01f), referencePointMaterial, 0);
            go.tag = "EditorOnly";
            Collider c = go.GetComponent<Collider>();
            if (c != null) SafeDestroy(c);
        }

        static GameObject CreateBox(Transform parent, string name, Vector3 localPos, Quaternion localRot, Vector3 size, Material material, int layer)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = size;

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        static Transform CreateGroup(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        void EnsureMaterials()
        {
            if (floorMaterial == null) floorMaterial = MakePreviewMaterial("PW_Floor (runtime)", new Color(0.85f, 0.85f, 0.80f));
            if (wallMaterial == null) wallMaterial = MakePreviewMaterial("PW_Wall (runtime)", new Color(0.55f, 0.70f, 0.85f));
            if (blockMaterial == null) blockMaterial = MakePreviewMaterial("PW_Block (runtime)", new Color(0.55f, 0.55f, 0.55f));
            if (referencePointMaterial == null) referencePointMaterial = MakePreviewMaterial("PW_ReferencePoint (runtime)", new Color(0.85f, 0.20f, 0.20f));
        }

        public static Material MakePreviewMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            return new Material(shader) { name = name, color = color };
        }

        void CacheReferences()
        {
            rooms.Clear();
            Transform root = transform.Find(GeneratedRootName);
            if (root == null) return;
            root.GetComponentsInChildren(true, rooms);
            testStart = root.Find(TestStartName);
        }

        static void SafeDestroy(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        [ContextMenu("Build NavMesh")]
        public bool BuildNavMesh()
        {
            Transform root = transform.Find(GeneratedRootName);
            if (root == null)
            {
                Debug.LogError("[PentaWay] No generated model found. Run PentaWay > Generate Floor Model first.");
                return false;
            }

            int notWalkable = NavMesh.GetAreaFromName("Not Walkable");
            if (notWalkable < 0) notWalkable = 1;

            var sources = new List<NavMeshBuildSource>();
            Matrix4x4 worldToModel = transform.worldToLocalMatrix;
            AddBoxSources(root.Find(FloorsName), 0, worldToModel, sources);
            AddBoxSources(root.Find(WallsName), notWalkable, worldToModel, sources);
            AddBoxSources(root.Find(BlocksName), notWalkable, worldToModel, sources);

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = agentRadius;
            settings.agentHeight = agentHeight;
            settings.agentClimb = agentClimb;
            settings.agentSlope = 45f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = voxelSize;
            settings.minRegionArea = 1f;

            Bounds bounds = LocalBounds();
            foreach (string warning in settings.ValidationReport(bounds))
                Debug.LogWarning("[PentaWay] NavMesh settings: " + warning);

            if (!Application.isPlaying) NavMesh.RemoveAllNavMeshData();

            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null)
            {
                Debug.LogError("[PentaWay] NavMesh bake failed.");
                return false;
            }
            data.name = "PentaWay NavMesh";
            navMeshData = data;
            RefreshNavMeshPose();
            return navMeshInstance.valid;
        }

        public void RefreshNavMeshPose()
        {
            if (navMeshInstance.valid) navMeshInstance.Remove();
            if (navMeshData == null) return;
            navMeshInstance = NavMesh.AddNavMeshData(navMeshData, transform.position, transform.rotation);
            navMeshInstance.owner = this;
        }

        static void AddBoxSources(Transform group, int area, Matrix4x4 worldToModel, List<NavMeshBuildSource> sources)
        {
            if (group == null) return;
            foreach (BoxCollider box in group.GetComponentsInChildren<BoxCollider>(true))
            {
                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = box.size,
                    transform = worldToModel * box.transform.localToWorldMatrix * Matrix4x4.Translate(box.center),
                    area = area
                });
            }
        }

        static Bounds LocalBounds()
        {
            var b = new Bounds(FloorPlanData.ToLocal(FloorPlanData.FloorSlabs[0].Center, 1f), Vector3.zero);
            foreach (RectXZ r in FloorPlanData.FloorSlabs)
            {
                b.Encapsulate(new Vector3(r.xMin, -0.5f, r.zMin));
                b.Encapsulate(new Vector3(r.xMax, FloorPlanData.WallHeight + 0.5f, r.zMax));
            }
            b.Expand(new Vector3(2f, 0f, 2f));
            return b;
        }
    }
}
