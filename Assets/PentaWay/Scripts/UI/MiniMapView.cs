using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PentaWay
{
    public enum MapLayout { Hidden, Corner, Expanded, Full }

    public class MiniMapView : MonoBehaviour
    {
        const float BuildScale = 20f;
        const float CornerScale = 9f;

        static readonly Color Background = new Color32(0xFF, 0xFF, 0xFF, 0xF0);
        static readonly Color WalkFill = new Color32(0xF1, 0xF3, 0xF6, 0xFF);
        static readonly Color RoomFill = new Color32(0xDC, 0xE7, 0xF8, 0xFF);
        static readonly Color DestFill = new Color32(0xC8, 0xF0, 0xD2, 0xFF);
        static readonly Color BlockFill = new Color32(0xC3, 0xC9, 0xD2, 0xFF);
        static readonly Color WallColor = new Color32(0x4A, 0x52, 0x5E, 0xFF);
        static readonly Color LabelColor = new Color32(0x30, 0x36, 0x40, 0xFF);

        ARNavigator navigator;
        FloorModel floorModel;
        RectTransform frame, viewport, content, userMarker, userCone, destinationPin;
        readonly List<RectTransform> pathSegments = new List<RectTransform>();
        readonly Dictionary<string, Image> roomImages = new Dictionary<string, Image>();
        Vector2 boundsMin, boundsSize;
        MapLayout layout = MapLayout.Hidden;
        RoomDestination highlightedRoom;
        Vector2 cornerSize = new Vector2(330, 330);
        float cornerTop = 300f;
        int drawnPathVersion = -1;

        public MapLayout Layout => layout;

        public void Build(RectTransform parent, ARNavigator nav, Font font, Sprite arrowSprite, Sprite dotSprite)
        {
            navigator = nav;
            floorModel = nav.floorModel;

            boundsMin = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (RectXZ r in FloorPlanData.FloorSlabs)
            {
                boundsMin = Vector2.Min(boundsMin, new Vector2(r.xMin, r.zMin));
                max = Vector2.Max(max, new Vector2(r.xMax, r.zMax));
            }
            boundsMin -= Vector2.one * 1.5f;
            boundsSize = max + Vector2.one * 1.5f - boundsMin;

            Image frameImage = NewImage("MiniMap", parent, Background);
            UIStyle.Round(frameImage, 36f);
            UIStyle.AddShadow(frameImage);
            frame = frameImage.rectTransform;
            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = frameImage;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Toggle);

            viewport = NewRect("Viewport", frame);
            Stretch(viewport, 14);
            viewport.gameObject.AddComponent<RectMask2D>();

            content = NewRect("Content", viewport);
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
            content.pivot = Vector2.zero;
            content.sizeDelta = boundsSize * BuildScale;

            foreach (RectXZ slab in FloorPlanData.FloorSlabs) AddPlanRect("Floor", slab, WalkFill, false);
            foreach (BlockInfo block in FloorPlanData.Blocks) AddPlanRect("Block_" + block.Name, block.Rect, BlockFill, false);
            foreach (RoomDestination room in floorModel.Rooms)
                if (room != null && room.IsDestination)
                    roomImages[room.RoomId] = AddPlanRect("Room_" + room.RoomId, room.PlanRect, RoomFill, true);
            AddWalls();
            foreach (RoomDestination room in floorModel.Rooms)
                if (room != null && room.IsDestination) AddLabel(room.DisplayName, room.PlanRect.Center, font);

            destinationPin = NewImage("Destination", content, UIStyle.Green).rectTransform;
            destinationPin.GetComponent<Image>().sprite = dotSprite;
            Center(destinationPin, new Vector2(1.6f, 1.6f) * BuildScale);

            Color user = UIStyle.Blue;
            userCone = NewImage("UserCone", content, new Color(user.r, user.g, user.b, 0.35f)).rectTransform;
            userCone.GetComponent<Image>().sprite = arrowSprite;
            Center(userCone, new Vector2(2.6f, 2.6f) * BuildScale);
            userMarker = NewImage("User", content, user).rectTransform;
            userMarker.GetComponent<Image>().sprite = dotSprite;
            Center(userMarker, new Vector2(1.3f, 1.3f) * BuildScale);

            foreach (Graphic g in content.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            AddNorthBadge(font);
            SetLayout(MapLayout.Hidden);
        }

        void AddNorthBadge(Font font)
        {
            Image badge = NewImage("North", frame, UIStyle.Blue);
            badge.sprite = UIStyle.Circle;
            badge.raycastTarget = false;
            RectTransform nb = badge.rectTransform;
            nb.anchorMin = nb.anchorMax = new Vector2(0f, 1f);
            nb.pivot = new Vector2(0.5f, 0.5f);
            nb.anchoredPosition = new Vector2(39f, -39f);
            nb.sizeDelta = new Vector2(46f, 46f);
            nb.localEulerAngles = new Vector3(0f, 0f, FloorPlanData.PlanNorthTrueBearing);

            Text text = NewRect("Text", nb).gameObject.AddComponent<Text>();
            text.font = font;
            text.text = NavText.NorthShort;
            text.fontSize = 28;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            Stretch(text.rectTransform, 0);
        }

        Image AddPlanRect(string name, RectXZ r, Color color, bool inset)
        {
            Image image = NewImage(name, content, color);
            RectTransform rt = image.rectTransform;
            float pad = inset ? 0.1f : 0f;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = (new Vector2(r.xMin + pad, r.zMin + pad) - boundsMin) * BuildScale;
            rt.sizeDelta = new Vector2(r.Size.x - 2 * pad, r.Size.y - 2 * pad) * BuildScale;
            return image;
        }

        void AddWalls()
        {
            Transform walls = floorModel.transform.Find(FloorModel.GeneratedRootName + "/Walls");
            if (walls == null) return;
            foreach (Transform piece in walls)
            {
                if (piece.name.StartsWith("Lintel")) continue;
                Vector3 local = floorModel.transform.InverseTransformPoint(piece.position);
                Vector3 dir = floorModel.transform.InverseTransformDirection(piece.forward);
                RectTransform rt = NewImage("Wall", content, WallColor).rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = PlanToContent(new Vector2(local.x, local.z));
                rt.sizeDelta = new Vector2(0.2f, piece.lossyScale.z) * BuildScale;
                rt.localEulerAngles = new Vector3(0f, 0f, -Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
            }
        }

        void AddLabel(string text, Vector2 plan, Font font)
        {
            RectTransform rt = NewRect("Label_" + text, content);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = PlanToContent(plan);
            rt.sizeDelta = new Vector2(9f, 3f) * BuildScale;
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = 30;
            t.fontStyle = FontStyle.Bold;
            t.color = LabelColor;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }

        public void SetCornerPlacement(Vector2 size, float top)
        {
            cornerSize = size;
            cornerTop = top;
            if (layout == MapLayout.Corner) SetLayout(MapLayout.Corner);
        }

        public void SetLayout(MapLayout newLayout)
        {
            layout = newLayout;
            frame.gameObject.SetActive(newLayout != MapLayout.Hidden);
            if (newLayout == MapLayout.Corner)
            {
                frame.anchorMin = frame.anchorMax = new Vector2(1f, 1f);
                frame.pivot = new Vector2(1f, 1f);
                frame.sizeDelta = cornerSize;
                frame.anchoredPosition = new Vector2(-28f, -cornerTop);
            }
            else if (newLayout != MapLayout.Hidden)
            {
                frame.anchorMin = Vector2.zero;
                frame.anchorMax = Vector2.one;
                frame.pivot = new Vector2(0.5f, 0.5f);
                frame.offsetMin = new Vector2(24f, 470f);
                frame.offsetMax = new Vector2(-24f, -230f);
            }
        }

        public void Toggle()
        {
            if (layout == MapLayout.Corner) SetLayout(MapLayout.Expanded);
            else if (layout == MapLayout.Expanded) SetLayout(MapLayout.Corner);
        }

        void LateUpdate()
        {
            if (layout == MapLayout.Hidden || navigator == null) return;

            Vector2 userPlan;
            float heading;
            bool hasUser = navigator.TryGetUserPlanPose(out userPlan, out heading);
            userMarker.gameObject.SetActive(hasUser);
            userCone.gameObject.SetActive(hasUser);
            if (hasUser)
            {
                userMarker.anchoredPosition = PlanToContent(userPlan);
                userCone.anchoredPosition = userMarker.anchoredPosition;
                userCone.localEulerAngles = new Vector3(0f, 0f, -heading);
            }

            UpdateDestination();
            if (navigator.PathVersion != drawnPathVersion) UpdatePath();

            Rect view = viewport.rect;
            float scale = layout == MapLayout.Corner ? CornerScale : Mathf.Min(view.width / boundsSize.x, view.height / boundsSize.y);
            Vector2 focus = layout == MapLayout.Corner && hasUser ? userPlan : boundsMin + boundsSize * 0.5f;
            content.localScale = Vector3.one * (scale / BuildScale);
            content.anchoredPosition = -(focus - boundsMin) * scale;
        }

        void UpdateDestination()
        {
            RoomDestination dest = navigator.Destination;
            if (dest != highlightedRoom)
            {
                Image img;
                if (highlightedRoom != null && roomImages.TryGetValue(highlightedRoom.RoomId, out img)) img.color = RoomFill;
                if (dest != null && roomImages.TryGetValue(dest.RoomId, out img)) img.color = DestFill;
                highlightedRoom = dest;
                if (dest != null) destinationPin.anchoredPosition = PlanToContent(floorModel.WorldToPlan(dest.DoorPoint.position));
            }
            destinationPin.gameObject.SetActive(dest != null);
        }

        void UpdatePath()
        {
            drawnPathVersion = navigator.PathVersion;
            IReadOnlyList<Vector3> corners = navigator.PathCorners;
            int segments = Mathf.Max(0, corners.Count - 1);
            for (int i = 0; i < segments; i++)
            {
                Vector2 a = floorModel.WorldToPlan(corners[i]);
                Vector2 b = floorModel.WorldToPlan(corners[i + 1]);
                Vector2 d = b - a;
                RectTransform seg = GetSegment(i);
                seg.anchoredPosition = PlanToContent((a + b) * 0.5f);
                seg.sizeDelta = new Vector2(0.45f, d.magnitude + 0.45f) * BuildScale;
                seg.localEulerAngles = new Vector3(0f, 0f, -Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg);
            }
            for (int i = segments; i < pathSegments.Count; i++)
                if (pathSegments[i].gameObject.activeSelf) pathSegments[i].gameObject.SetActive(false);
        }

        RectTransform GetSegment(int index)
        {
            while (pathSegments.Count <= index)
            {
                RectTransform rt = NewImage("Path", content, UIStyle.Blue).rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.GetComponent<Image>().raycastTarget = false;
                rt.SetSiblingIndex(destinationPin.GetSiblingIndex());
                pathSegments.Add(rt);
            }
            RectTransform seg = pathSegments[index];
            if (!seg.gameObject.activeSelf) seg.gameObject.SetActive(true);
            return seg;
        }

        Vector2 PlanToContent(Vector2 plan) => (plan - boundsMin) * BuildScale;

        static void Center(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Image NewImage(string name, Transform parent, Color color)
        {
            Image image = NewRect(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
