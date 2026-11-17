using UnityEngine;

namespace PentaWay
{
    public class RoomDestination : MonoBehaviour
    {
        [SerializeField] string roomId;
        [SerializeField] string displayName;
        [SerializeField] string subtitle;
        [SerializeField] bool isDestination = true;
        [SerializeField] RectXZ planRect;
        [SerializeField] Facing walkwaySide;
        [SerializeField] Transform doorPoint;
        [SerializeField] Transform approachPoint;
        [SerializeField] Transform labelAnchor;

        public string RoomId => roomId;
        public string DisplayName => displayName;
        public string Subtitle => subtitle;
        public bool IsDestination => isDestination;
        public RectXZ PlanRect => planRect;
        public Facing WalkwaySide => walkwaySide;
        public Transform DoorPoint => doorPoint != null ? doorPoint : transform;
        public Transform ApproachPoint => approachPoint != null ? approachPoint : DoorPoint;
        public Transform LabelAnchor => labelAnchor != null ? labelAnchor : DoorPoint;

        public void Setup(RoomInfo info, Transform door, Transform approach, Transform label)
        {
            roomId = info.Id;
            displayName = info.DisplayName;
            subtitle = info.Subtitle;
            isDestination = info.IsDestination;
            planRect = info.Rect;
            walkwaySide = info.WalkwaySide;
            doorPoint = door;
            approachPoint = approach;
            labelAnchor = label;
        }

        void OnDrawGizmos()
        {
            if (doorPoint == null || approachPoint == null) return;
            Gizmos.color = isDestination ? new Color(0.1f, 0.7f, 0.5f) : new Color(0.6f, 0.6f, 0.6f);
            Gizmos.DrawLine(doorPoint.position, approachPoint.position);
            Gizmos.DrawSphere(approachPoint.position, 0.15f);
            if (labelAnchor != null) Gizmos.DrawWireCube(labelAnchor.position, new Vector3(0.6f, 0.25f, 0.6f));
        }
    }
}
