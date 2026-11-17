using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PentaWay
{
    public class RoomLabelOverlay : MonoBehaviour
    {
        public float maxDistance = 15f;
        public float maxDestinationDistance = 35f;
        public float referenceDistance = 4f;
        public float occlusionInterval = 0.1f;

        class Label
        {
            public RoomDestination Room;
            public RectTransform Rect;
            public Image Background;
            public Text Text;
            public CanvasGroup Group;
            public bool Occluded;
            public int ShownMeters = -1;
            public bool WasDestination;
        }

        ARNavigator navigator;
        RectTransform root;
        readonly List<Label> labels = new List<Label>();
        int wallMask;
        float nextOcclusionCheck;

        public void Build(RectTransform parent, ARNavigator nav, Font font)
        {
            navigator = nav;
            root = parent;
            wallMask = 1 << nav.floorModel.WallLayer;

            foreach (RoomDestination room in nav.GetDestinations())
            {
                var go = new GameObject("Label_" + room.RoomId, typeof(RectTransform));
                go.transform.SetParent(root, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0f);

                Image bg = go.AddComponent<Image>();
                UIStyle.Round(bg, 40f);
                bg.color = UIStyle.Card;
                bg.raycastTarget = false;
                UIStyle.AddShadow(bg);

                var textGo = new GameObject("Text", typeof(RectTransform));
                textGo.transform.SetParent(rect, false);
                Text text = textGo.AddComponent<Text>();
                text.font = font;
                text.fontSize = 40;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.raycastTarget = false;
                text.text = room.DisplayName;
                text.color = UIStyle.Text;
                var tr = (RectTransform)textGo.transform;
                tr.anchorMin = Vector2.zero;
                tr.anchorMax = Vector2.one;
                tr.offsetMin = tr.offsetMax = Vector2.zero;
                rect.sizeDelta = new Vector2(text.preferredWidth + 64f, 84f);

                labels.Add(new Label { Room = room, Rect = rect, Background = bg, Text = text, Group = go.AddComponent<CanvasGroup>() });
                go.SetActive(false);
            }
        }

        void LateUpdate()
        {
            if (navigator == null) return;
            Camera cam = navigator.arCamera;
            bool show = cam != null && navigator.Mode == PositioningMode.Compass && navigator.IsPositioned &&
                        (navigator.State == NavState.Navigating || navigator.State == NavState.Arrived);

            bool checkOcclusion = Time.unscaledTime >= nextOcclusionCheck;
            if (checkOcclusion) nextOcclusionCheck = Time.unscaledTime + occlusionInterval;

            foreach (Label label in labels)
            {
                bool visible = show && UpdateLabel(label, cam, checkOcclusion);
                if (label.Rect.gameObject.activeSelf != visible) label.Rect.gameObject.SetActive(visible);
            }
        }

        bool UpdateLabel(Label label, Camera cam, bool checkOcclusion)
        {
            Vector3 anchor = label.Room.LabelAnchor.position;
            Vector3 camPos = cam.transform.position;
            Vector3 toAnchor = anchor - camPos;
            float distance = toAnchor.magnitude;
            bool isDestination = label.Room == navigator.Destination;

            if (distance > (isDestination ? maxDestinationDistance : maxDistance)) return false;

            Vector3 screen = cam.WorldToScreenPoint(anchor);
            if (screen.z <= 0.1f) return false;

            if (checkOcclusion)
                label.Occluded = Physics.Raycast(camPos, toAnchor / distance, distance - 0.15f, wallMask, QueryTriggerInteraction.Ignore);
            if (label.Occluded) return false;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out local)) return false;
            label.Rect.anchoredPosition = local + root.rect.size * root.pivot;

            int meters = isDestination ? Mathf.RoundToInt(new Vector2(toAnchor.x, toAnchor.z).magnitude) : -1;
            if (meters != label.ShownMeters || isDestination != label.WasDestination)
            {
                label.ShownMeters = meters;
                label.WasDestination = isDestination;
                label.Text.text = isDestination ? label.Room.DisplayName + "  ·  " + NavText.Meters(meters) : label.Room.DisplayName;
                label.Background.color = isDestination ? UIStyle.Blue : UIStyle.Card;
                label.Text.color = isDestination ? Color.white : UIStyle.Text;
                label.Rect.sizeDelta = new Vector2(label.Text.preferredWidth + 64f, 84f);
                if (isDestination) label.Rect.SetAsLastSibling();
            }

            label.Rect.localScale = Vector3.one * Mathf.Clamp(referenceDistance / Mathf.Max(distance, 0.5f), 0.5f, 1.15f);
            label.Group.alpha = Mathf.Clamp01((maxDestinationDistance - distance) / 5f);
            return true;
        }
    }
}
