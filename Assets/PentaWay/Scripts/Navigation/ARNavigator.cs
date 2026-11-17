using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PentaWay
{
    public enum NavState { ChoosingDestination, Calibrating, Navigating, Arrived }

    public enum TurnKind { Straight, SlightLeft, SlightRight, Left, Right, UTurn, Arrive }

    public struct NavInfo
    {
        public TurnKind Turn;
        public string Instruction;
        public string Detail;
        public float RemainingMeters;
        public float EtaSeconds;
        public bool HasPath;
    }

    public class ARNavigator : MonoBehaviour
    {
        [Header("References")]
        public FloorModel floorModel;
        public Camera arCamera;
        public DeadReckoningTracker tracker;
        public PathArrowRenderer pathRenderer;
        public Material occluderMaterial;

        [Header("Navigation")]
        public float walkingSpeed = 1.2f;
        public float arrivalDistance = 1.5f;
        public float repathInterval = 0.25f;
        public float guideLookAhead = 2.5f;
        [Tooltip("Jalur terakhir tetap ditampilkan selama ini (detik) bila satu kali hitung ulang gagal, supaya panah tidak berkedip.")]
        public float pathLossGrace = 0.75f;

        [Header("Bentuk jalur panah")]
        [Tooltip("Di koridor lurus, jalur dirapikan ke garis tengah koridor supaya panah sejajar dinding.")]
        public bool alignPathToCorridors = true;
        [Tooltip("Jalur diakhiri tepat di titik pintu ruangan tujuan (bukan di titik Approach 0.5 m di depannya).")]
        public bool endPathAtDoor = true;
        [Tooltip("Jalur utama dimulai di garis tengah koridor (sejajar posisi pengguna), bukan ditarik diagonal dari posisi pengguna.")]
        public bool startPathAtCorridorCenter = true;
        [Tooltip("Pita jalur utama berhenti di titik belok di tengah koridor; belokan ke pintu ditampilkan oleh panah petunjuk pintu.")]
        public bool showDoorTurnAsHint = true;

        public NavState State { get; private set; }
        public PositioningMode Mode { get; private set; }
        public RoomDestination Destination { get; private set; }
        public NavInfo Info { get; private set; }
        public bool IsTestMode { get; private set; }
        public float GuideAngle { get; private set; }
        public IReadOnlyList<Vector3> PathCorners => corners;
        public int PathVersion { get; private set; }
        public bool PathEndsAtDoor { get; private set; }
        public bool IsPositioned => tracker != null && tracker.IsCalibrated;
        public bool CanStepBack => mapHistory.Count > 0;

        public event Action StateChanged;
        public event Action InfoChanged;
        public event Action<string> Message;

        NavMeshPath path;
        readonly Vector3[] cornerBuffer = new Vector3[64];
        readonly List<Vector3> corners = new List<Vector3>(64);
        readonly List<Vector2> planPath = new List<Vector2>(64);
        readonly List<Vector3> displayCorners = new List<Vector3>(64);
        readonly List<Vector3> mainPath = new List<Vector3>(64);
        bool startSnapped;
        Func<Vector2, Vector2, bool> isClearOnNavMesh;
        float doorTail;
        float lastPathTime = float.NegativeInfinity;
        Vector3 guideTarget;
        bool hasGuideTarget;
        float nextRepathTime;
        readonly Stack<KeyValuePair<Vector2, float>> mapHistory = new Stack<KeyValuePair<Vector2, float>>();

        void Awake()
        {
            path = new NavMeshPath();
            isClearOnNavMesh = IsClearOnNavMesh;
        }

        void Start()
        {
            if (tracker != null) tracker.Calibrated += OnPositioned;
            SetState(NavState.ChoosingDestination);
        }

        void OnDestroy()
        {
            if (tracker != null) tracker.Calibrated -= OnPositioned;
        }

        void Update()
        {
            if (State != NavState.Navigating && State != NavState.Arrived) return;
            if (Time.time >= nextRepathTime)
            {
                nextRepathTime = Time.time + repathInterval;
                Repath();
            }
            UpdateGuideAngle();
        }

        public void SetMode(PositioningMode mode)
        {
            Mode = mode;
            IsTestMode = false;
            mapHistory.Clear();

            if (floorModel != null)
            {
                floorModel.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                floorModel.RefreshNavMeshPose();
                if (Application.isMobilePlatform)
                    floorModel.ApplyOcclusionMode(mode == PositioningMode.Map ? null : occluderMaterial);
            }

            SetState(Destination != null ? NavState.Calibrating : NavState.ChoosingDestination);
        }

        public List<RoomDestination> GetDestinations()
        {
            var list = new List<RoomDestination>();
            if (floorModel == null) return list;
            foreach (RoomDestination room in floorModel.Rooms)
                if (room != null && room.IsDestination) list.Add(room);
            list.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
            return list;
        }

        public void ChooseDestination(RoomDestination room)
        {
            Destination = room;
            mapHistory.Clear();
            if (IsPositioned)
            {
                SetState(NavState.Navigating);
                Repath();
                RaiseMessage(NavText.NavigationStarted(room.DisplayName));
            }
            else
            {
                SetState(NavState.Calibrating);
                RaiseMessage(NavText.DestinationChosen(room.DisplayName));
            }
        }

        public void StartTestMode()
        {
            if (tracker == null) return;
            IsTestMode = true;
            tracker.SetPose(FloorPlanData.TestStartPosition, FloorPlanData.ToYaw(FloorPlanData.TestStartFacing), "TEST");
        }

        public void Recalibrate()
        {
            mapHistory.Clear();
            SetState(NavState.Calibrating);
        }

        public void BackToDestinations()
        {
            Destination = null;
            mapHistory.Clear();
            ClearPath();
            SetState(NavState.ChoosingDestination);
        }

        public void MapStep(int direction)
        {
            if (Mode != PositioningMode.Map || !IsPositioned) return;

            if (direction < 0)
            {
                if (mapHistory.Count == 0) return;
                KeyValuePair<Vector2, float> previous = mapHistory.Pop();
                tracker.MoveTo(previous.Key, previous.Value);
            }
            else
            {
                if (corners.Count < 2) return;
                mapHistory.Push(new KeyValuePair<Vector2, float>(tracker.PlanPosition, tracker.HeadingDegrees));
                float stepDistance;
                TurnKind stepTurn;
                int target = NextTurnIndex(InstructionEndIndex(), out stepDistance, out stepTurn);
                Vector3 onward = target + 1 < corners.Count ? corners[target + 1] - corners[target] : corners[target] - corners[target - 1];
                Vector3 onwardLocal = floorModel.transform.InverseTransformDirection(onward);
                tracker.MoveTo(floorModel.WorldToPlan(corners[target]), Mathf.Atan2(onwardLocal.x, onwardLocal.z) * Mathf.Rad2Deg);
            }
            nextRepathTime = 0f;
            Repath();
        }

        public void RaiseMessage(string text)
        {
            if (!string.IsNullOrEmpty(text)) Message?.Invoke(text);
        }

        public bool TryGetUserPlanPose(out Vector2 plan, out float headingDegrees)
        {
            plan = Vector2.zero;
            headingDegrees = 0f;
            if (!IsPositioned) return false;
            plan = tracker.PlanPosition;
            headingDegrees = tracker.HeadingDegrees;
            return true;
        }

        void OnPositioned(string sourceName)
        {
            if (sourceName != "TEST") IsTestMode = false;
            mapHistory.Clear();
            nextRepathTime = 0f;

            string startText = sourceName == "TEST" ? NavText.TestModeOn : NavText.Calibrated(sourceName);
            if (State == NavState.Calibrating && Destination != null)
            {
                SetState(NavState.Navigating);
                Repath();
                RaiseMessage(startText + " " + NavText.NavigationStarted(Destination.DisplayName));
            }
            else
            {
                RaiseMessage(startText);
            }
        }

        void SetState(NavState newState)
        {
            State = newState;
            if (newState == NavState.Navigating) nextRepathTime = 0f;
            if (newState == NavState.ChoosingDestination || newState == NavState.Calibrating) ClearPath();
            StateChanged?.Invoke();
        }

        void ClearPath()
        {
            if (corners.Count > 0) PathVersion++;
            corners.Clear();
            PathEndsAtDoor = false;
            doorTail = 0f;
            startSnapped = false;
            hasGuideTarget = false;
            Info = new NavInfo();
            if (pathRenderer != null) pathRenderer.Clear();
            InfoChanged?.Invoke();
        }

        void GetUserPose(out Vector3 floorPosition, out Vector3 forward)
        {
            floorPosition = floorModel.PlanToWorld(tracker.PlanPosition);
            float rad = tracker.HeadingDegrees * Mathf.Deg2Rad;
            forward = floorModel.transform.TransformDirection(new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)));
        }

        void Repath()
        {
            if (Destination == null || floorModel == null || !IsPositioned) return;

            Vector3 user, forward;
            GetUserPose(out user, out forward);

            NavMeshHit startHit, endHit;
            if (!NavMesh.SamplePosition(user, out startHit, 2.0f, NavMesh.AllAreas))
            {
                ShowProblem(NavText.OffMap);
                return;
            }
            int count = 0;
            if (NavMesh.SamplePosition(Destination.ApproachPoint.position, out endHit, 1.5f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path) &&
                path.status != NavMeshPathStatus.PathInvalid)
                count = path.GetCornersNonAlloc(cornerBuffer);
            if (count < 2)
            {
                ShowProblem(NavText.NoPath);
                return;
            }

            BuildDisplayPath(count);
            SetCorners(displayCorners);
            lastPathTime = Time.time;
            float remaining = Mathf.Max(0f, PathLength() - doorTail) + (startSnapped ? 0f : Flat(corners[0] - user).magnitude);

            if (remaining <= arrivalDistance)
            {
                int side = DoorSide(user, forward);
                ClearPath();
                Info = new NavInfo
                {
                    Turn = TurnKind.Arrive,
                    Instruction = NavText.Arrived(Destination.DisplayName),
                    Detail = NavText.RoomOnSide(Destination.DisplayName, side)
                };
                InfoChanged?.Invoke();
                if (State != NavState.Arrived) SetState(NavState.Arrived);
                return;
            }
            if (State == NavState.Arrived && remaining > arrivalDistance + 1f) SetState(NavState.Navigating);

            if (pathRenderer != null)
            {
                if (Mode == PositioningMode.Compass)
                {
                    pathRenderer.SetPath(BuildMainPath(), PathVersion);
                    UpdateDoorHint(remaining);
                }
                else pathRenderer.Clear();
            }
            guideTarget = PointAlongPath(guideLookAhead);
            hasGuideTarget = true;
            Info = BuildInstruction(remaining, forward);
            InfoChanged?.Invoke();
        }

        void UpdateDoorHint(float remaining)
        {
            if (pathRenderer == null || Destination == null || corners.Count < 2) return;
            int n = corners.Count;
            if (PathEndsAtDoor && n < 3)
            {
                pathRenderer.ClearDoorHint();
                return;
            }
            Vector3 turnPoint = PathEndsAtDoor ? corners[n - 2] : corners[n - 1];
            Vector3 lastLeg = Flat(turnPoint - (PathEndsAtDoor ? corners[n - 3] : corners[n - 2]));
            Vector3 door = Destination.DoorPoint.position;
            Vector3 toDoor = Flat(door - turnPoint);
            if (remaining > pathRenderer.doorHintDistance || lastLeg.sqrMagnitude < 1e-4f || toDoor.sqrMagnitude < 0.04f)
            {
                pathRenderer.ClearDoorHint();
                return;
            }
            float angle = Vector3.SignedAngle(lastLeg, toDoor, Vector3.up);
            if (Mathf.Abs(angle) < 30f) pathRenderer.ClearDoorHint();
            else pathRenderer.SetDoorHint(turnPoint, door);
        }

        bool DoorLegIsTurn()
        {
            int n = corners.Count;
            if (!PathEndsAtDoor || n < 3) return false;
            Vector3 before = Flat(corners[n - 2] - corners[n - 3]);
            Vector3 doorLeg = Flat(corners[n - 1] - corners[n - 2]);
            if (before.sqrMagnitude < 1e-4f || doorLeg.sqrMagnitude < 1e-4f) return false;
            return Mathf.Abs(Vector3.SignedAngle(before, doorLeg, Vector3.up)) >= 30f;
        }

        IReadOnlyList<Vector3> BuildMainPath()
        {
            if (!showDoorTurnAsHint || !DoorLegIsTurn()) return corners;
            mainPath.Clear();
            for (int i = 0; i < corners.Count - 1; i++) mainPath.Add(corners[i]);
            return mainPath;
        }

        void ShowProblem(string text)
        {
            if (corners.Count > 0 && Time.time - lastPathTime < pathLossGrace) return;
            ClearPath();
            Info = new NavInfo { Instruction = text, Detail = NavText.Towards(Destination.DisplayName) };
            InfoChanged?.Invoke();
        }

        void SetCorners(List<Vector3> source)
        {
            int count = source.Count;
            bool changed = count != corners.Count;
            for (int i = 0; i < count && !changed; i++)
                changed = (corners[i] - source[i]).sqrMagnitude > 1e-4f;
            if (!changed) return;

            corners.Clear();
            for (int i = 0; i < count; i++) corners.Add(source[i]);
            PathVersion++;
        }

        void BuildDisplayPath(int count)
        {
            planPath.Clear();
            for (int i = 0; i < count; i++) planPath.Add(floorModel.WorldToPlan(cornerBuffer[i]));
            Vector2 rawStart = planPath[0];
            Vector2 approach = planPath[planPath.Count - 1];
            Vector2 door = floorModel.WorldToPlan(Destination.DoorPoint.position);

            PathEndsAtDoor = PathLaneRefiner.Refine(planPath, door, endPathAtDoor, alignPathToCorridors, isClearOnNavMesh, startPathAtCorridorCenter);
            startSnapped = (planPath[0] - rawStart).sqrMagnitude > 1e-4f;
            doorTail = PathEndsAtDoor ? Vector2.Distance(approach, door) : 0f;

            displayCorners.Clear();
            for (int i = 0; i < planPath.Count; i++) displayCorners.Add(floorModel.PlanToWorld(planPath[i]));
        }

        bool IsClearOnNavMesh(Vector2 a, Vector2 b)
        {
            Vector3 from = floorModel.PlanToWorld(a);
            Vector3 to = floorModel.PlanToWorld(b);
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(from, out hit, 0.5f, NavMesh.AllAreas) || Flat(hit.position - from).sqrMagnitude > 0.15f * 0.15f)
                return false;
            from = hit.position;
            if (!NavMesh.SamplePosition(to, out hit, 0.5f, NavMesh.AllAreas) || Flat(hit.position - to).sqrMagnitude > 0.15f * 0.15f)
                return false;
            to = hit.position;
            return !NavMesh.Raycast(from, to, out hit, NavMesh.AllAreas);
        }

        int InstructionEndIndex()
        {
            int end = corners.Count - 1;
            if (PathEndsAtDoor && corners.Count > 2) end--;
            return Mathf.Max(1, end);
        }

        int NextTurnIndex(int endIndex, out float distance, out TurnKind turn)
        {
            distance = 0f;
            turn = TurnKind.Straight;
            for (int k = 1; k <= endIndex; k++)
            {
                distance += Flat(corners[k] - corners[k - 1]).magnitude;
                if (k == endIndex) return k;
                TurnKind t = Classify(Vector3.SignedAngle(Flat(corners[k] - corners[k - 1]), Flat(corners[k + 1] - corners[k]), Vector3.up));
                if (t != TurnKind.Straight)
                {
                    turn = t;
                    return k;
                }
            }
            return endIndex;
        }

        public bool TryGetGuideDirection(float lookAhead, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!IsPositioned || floorModel == null || corners.Count < 2) return false;
            Vector3 user, forward;
            GetUserPose(out user, out forward);
            direction = Flat(PointAlongPath(lookAhead) - user);
            if (direction.sqrMagnitude < 1e-4f) return false;
            direction.Normalize();
            return true;
        }

        void UpdateGuideAngle()
        {
            if (!hasGuideTarget || !IsPositioned)
            {
                GuideAngle = 0f;
                return;
            }
            Vector3 user, forward;
            GetUserPose(out user, out forward);
            Vector3 toTarget = Flat(guideTarget - user);
            GuideAngle = toTarget.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(forward, toTarget, Vector3.up) : 0f;
        }

        NavInfo BuildInstruction(float remaining, Vector3 heading)
        {
            string room = Destination.DisplayName;
            var info = new NavInfo
            {
                RemainingMeters = remaining,
                EtaSeconds = remaining / Mathf.Max(0.1f, walkingSpeed),
                HasPath = true,
                Detail = NavText.Towards(room)
            };

            Vector3 lead = Flat(PointAlongPath(2f) - corners[0]);
            if (lead.magnitude > 1f && Mathf.Abs(Vector3.SignedAngle(heading, lead, Vector3.up)) > 120f)
            {
                info.Turn = TurnKind.UTurn;
                info.Instruction = NavText.UTurn;
                info.Detail = NavText.FollowArrows;
                return info;
            }

            int endIndex = InstructionEndIndex();
            float toNextCorner;
            TurnKind turn;
            int turnIndex = NextTurnIndex(endIndex, out toNextCorner, out turn);

            if (turn == TurnKind.Straight)
            {
                int side = DoorSide(corners[0], heading);
                TurnKind doorTurn = side < 0 ? TurnKind.Left : side > 0 ? TurnKind.Right : TurnKind.Straight;
                if (toNextCorner < 6f)
                {
                    info.Turn = doorTurn;
                    info.Instruction = NavText.RoomOnSide(room, side);
                    info.Detail = NavText.Meters(toNextCorner);
                }
                else
                {
                    info.Turn = TurnKind.Straight;
                    info.Instruction = NavText.StraightTo(room);
                    info.Detail = NavText.Meters(toNextCorner) + (side != 0 ? ", " + NavText.ThenTurn(doorTurn) : "");
                }
                return info;
            }

            if (toNextCorner < 3f)
            {
                info.Turn = turn;
                info.Instruction = turnIndex == endIndex - 1 ? NavText.TurnTo(turn, room) : NavText.TurnName(turn);
            }
            else if (toNextCorner > 12f)
            {
                info.Turn = TurnKind.Straight;
                info.Instruction = NavText.Straight;
                info.Detail = NavText.Meters(toNextCorner) + ", " + NavText.ThenTurn(turn);
            }
            else
            {
                info.Turn = turn;
                info.Instruction = NavText.TurnIn(turn, toNextCorner);
            }
            return info;
        }

        static TurnKind Classify(float signedAngle)
        {
            float a = Mathf.Abs(signedAngle);
            if (a < 25f) return TurnKind.Straight;
            if (a > 150f) return TurnKind.UTurn;
            bool right = signedAngle > 0f;
            if (a < 60f) return right ? TurnKind.SlightRight : TurnKind.SlightLeft;
            return right ? TurnKind.Right : TurnKind.Left;
        }

        int DoorSide(Vector3 from, Vector3 heading)
        {
            Vector3 facing = Flat(heading);
            if (facing.sqrMagnitude < 1e-4f) return 0;
            Vector2 walkway = FloorPlanData.ToVector(Destination.WalkwaySide);
            Vector3 intoDoor = Flat(floorModel.transform.TransformDirection(new Vector3(-walkway.x, 0f, -walkway.y)));
            if (intoDoor.sqrMagnitude > 1e-4f)
            {
                float doorAngle = Vector3.SignedAngle(facing, intoDoor, Vector3.up);
                if (Mathf.Abs(doorAngle) >= 30f && Mathf.Abs(doorAngle) <= 150f) return doorAngle > 0f ? 1 : -1;
            }
            Vector3 toDoor = Flat(Destination.DoorPoint.position - from);
            if (toDoor.sqrMagnitude < 1e-4f) return 0;
            float angle = Vector3.SignedAngle(facing, toDoor, Vector3.up);
            return angle > 30f ? 1 : angle < -30f ? -1 : 0;
        }

        Vector3 PointAlongPath(float distance)
        {
            for (int i = 1; i < corners.Count; i++)
            {
                float segment = Flat(corners[i] - corners[i - 1]).magnitude;
                if (distance <= segment && segment > 1e-4f)
                    return Vector3.Lerp(corners[i - 1], corners[i], distance / segment);
                distance -= segment;
            }
            return corners[corners.Count - 1];
        }

        float PathLength()
        {
            float length = 0f;
            for (int i = 1; i < corners.Count; i++) length += Flat(corners[i] - corners[i - 1]).magnitude;
            return length;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}