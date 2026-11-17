using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace PentaWay
{
    public class NavRuntimeUI : MonoBehaviour
    {
        public ARNavigator navigator;
        public DeadReckoningTracker tracker;
        public PositioningModeController modeController;
        public Texture2D logo;
        public bool showGuideArrowInCompass = false;
        public bool showPathFinderHint = true;
        public bool showCenterArrow = true;
        public bool vibrateOnArrival = true;

        const float HeaderHeight = 440f;

        Font font;
        RectTransform safeRoot;
        Rect lastSafeArea;
        MiniMapView miniMap;
        CompassNorth compass;

        UIPanelAnimator destinationPanel, calibrationPanel, navigationView, arrivalCard, welcomePanel;
        GameObject guideArrow, toast, topScrim, mapBackdrop, compassBadge;
        RectTransform guideArrowIcon;
        Text guideHintText;
        bool guideAllowed;
        float pathHiddenSince = -1f;
        float nextGuideCheck;
        Vector2 guideScreenDirection = Vector2.up;
        string guideMessage = "";
        const float PathCheckLength = 15f;
        const float PathCheckStep = 0.5f;
        const float ViewMinX = 0.04f, ViewMaxX = 0.96f, ViewMinY = 0.22f, ViewMaxY = 0.85f;
        const float HiddenDelay = 0.25f;
        const float AheadMinDistance = 1f;
        const float AheadMaxAngle = 60f;

        readonly List<KeyValuePair<RoomDestination, GameObject>> roomCards = new List<KeyValuePair<RoomDestination, GameObject>>();
        Text noResultsText;
        GridLayoutGroup roomGrid;
        RectTransform roomGridViewport;
        float lastGridWidth = -1f;
        RectTransform welcomeLogo, welcomeContent;
        CanvasGroup welcomeContentGroup;
        Vector2 welcomeLogoBase;
        float welcomeStart;

        Image instructionCard, turnIcon, tagPill, etaRing, arrivedCheck, progressFill, compassArrow;
        Text instructionText, detailText, tagText, destinationName, distanceText, etaMinutes, etaCaption, modeText, facingText, arrivalSubtitle;
        GameObject previousButton, nextButton, mapButton;
        Button previousButtonComponent;
        float routeLength;
        NavState lastState;
        int shownCardinal = -1;

        Text calibrationHelp, calibrationStatus, toastText;
        GameObject imHereButton, startHereButton;
        float toastHideTime;
        int devTaps;
        float devTapStart;

        string selectedStart;
        readonly List<KeyValuePair<string, Image>> startChips = new List<KeyValuePair<string, Image>>();
        readonly List<KeyValuePair<PositioningMode, Image>> modeChips = new List<KeyValuePair<PositioningMode, Image>>();

        void Start()
        {
            if (navigator == null)
            {
                Debug.LogError("[PentaWay] NavRuntimeUI has no navigator. Run PentaWay > Set Up Scene.");
                enabled = false;
                return;
            }
            if (tracker == null) tracker = navigator.tracker;
            compass = FindAnyObjectByType<CompassNorth>();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildUI();
            if (showCenterArrow) CenterGuideArrow.Create(navigator);

            navigator.StateChanged += Refresh;
            navigator.InfoChanged += UpdateNavigationInfo;
            navigator.Message += ShowToast;
            if (modeController != null) modeController.ModeChanged += OnModeChanged;
            lastState = navigator.State;
            OnModeChanged();
        }

        void OnDestroy()
        {
            if (navigator != null)
            {
                navigator.StateChanged -= Refresh;
                navigator.InfoChanged -= UpdateNavigationInfo;
                navigator.Message -= ShowToast;
            }
            if (modeController != null) modeController.ModeChanged -= OnModeChanged;
        }

        void Update()
        {
            ApplySafeArea();
            FitRoomGrid();
            AnimateWelcome();
            UpdateGuide();
            if (compassBadge.activeInHierarchy) UpdateCompassBadge();
            if (toast.activeSelf && Time.time > toastHideTime) toast.SetActive(false);
        }

        void AnimateWelcome()
        {
            if (welcomePanel == null || !welcomePanel.IsShown || welcomeLogo == null) return;
            float t = Time.unscaledTime - welcomeStart;

            float pop = Mathf.Clamp01(t / 0.7f);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float back = 1f + c3 * Mathf.Pow(pop - 1f, 3f) + c1 * Mathf.Pow(pop - 1f, 2f);
            float breathe = 1f + 0.035f * Mathf.Sin(t * 2.2f);
            welcomeLogo.localScale = Vector3.one * back * breathe;
            welcomeLogo.anchoredPosition = welcomeLogoBase + Vector2.up * (14f * Mathf.Sin(t * 1.8f));

            float reveal = Mathf.Clamp01((t - 0.35f) / 0.6f);
            float eased = 1f - (1f - reveal) * (1f - reveal);
            welcomeContentGroup.alpha = eased;
            welcomeContent.anchoredPosition = Vector2.down * 50f * (1f - eased);
        }

        void FitRoomGrid()
        {
            if (roomGrid == null || roomGridViewport == null) return;
            float width = roomGridViewport.rect.width;
            if (width <= 1f || Mathf.Approximately(width, lastGridWidth)) return;
            lastGridWidth = width;
            float cell = Mathf.Floor((width - roomGrid.padding.horizontal - roomGrid.spacing.x) * 0.5f);
            roomGrid.cellSize = new Vector2(cell, cell);
        }

        void OnModeChanged()
        {
            PositioningMode mode = navigator.Mode;
            foreach (KeyValuePair<PositioningMode, Image> chip in modeChips)
            {
                chip.Value.gameObject.SetActive(modeController != null && modeController.IsReady && modeController.IsAvailable(chip.Key));
                SetPillColors(chip.Value, chip.Key == mode);
            }

            imHereButton.SetActive(mode == PositioningMode.Compass);
            startHereButton.SetActive(mode == PositioningMode.Map);
            previousButton.SetActive(mode == PositioningMode.Map);
            nextButton.SetActive(mode == PositioningMode.Map);
            mapButton.SetActive(mode != PositioningMode.Map);
            modeText.text = "Mode " + NavText.ModeName(mode) + " · " + FloorPlanData.FloorName;
            Refresh();
        }

        void Refresh()
        {
            NavState state = navigator.State;
            PositioningMode mode = navigator.Mode;
            bool navigating = state == NavState.Navigating || state == NavState.Arrived;
            bool cameraView = navigating && mode == PositioningMode.Compass;

            destinationPanel.Show(state == NavState.ChoosingDestination);
            calibrationPanel.Show(state == NavState.Calibrating);
            navigationView.Show(navigating);
            guideAllowed = cameraView && state == NavState.Navigating;
            pathHiddenSince = -1f;
            nextGuideCheck = 0f;
            guideArrow.SetActive(guideAllowed && showGuideArrowInCompass);
            topScrim.SetActive(cameraView);
            mapBackdrop.SetActive(navigating && mode == PositioningMode.Map);
            compassBadge.SetActive(cameraView);

            if (!navigating) miniMap.SetLayout(MapLayout.Hidden);
            else if (mode == PositioningMode.Map) miniMap.SetLayout(MapLayout.Full);
            else if (miniMap.Layout == MapLayout.Hidden || miniMap.Layout == MapLayout.Full) miniMap.SetLayout(MapLayout.Corner);

            if (state == NavState.Arrived && lastState != NavState.Arrived)
            {
                arrivalSubtitle.text = navigator.Info.Detail ?? "";
                arrivalCard.Show(true);
                if (vibrateOnArrival) Vibrate();
            }
            else if (state != NavState.Arrived)
            {
                arrivalCard.Show(false);
            }
            if (state == NavState.ChoosingDestination || state == NavState.Calibrating) routeLength = 0f;
            lastState = state;

            if (state == NavState.Calibrating)
            {
                calibrationHelp.text = mode == PositioningMode.Compass ? NavText.CompassHelp : NavText.MapHelp;
                calibrationStatus.text = "";
                foreach (KeyValuePair<string, Image> chip in startChips) SetPillColors(chip.Value, chip.Key == selectedStart);
            }
            if (navigating) UpdateNavigationInfo();
        }

        void UpdateNavigationInfo()
        {
            if (!navigationView.IsShown) return;
            NavInfo info = navigator.Info;
            bool arrived = navigator.State == NavState.Arrived;
            RoomDestination dest = navigator.Destination;

            instructionCard.color = arrived ? UIStyle.Green : UIStyle.Blue;
            instructionText.text = info.Instruction ?? "";
            detailText.text = info.Detail ?? "";
            turnIcon.sprite = info.Turn == TurnKind.Arrive ? UIStyle.IconCheck : UIStyle.Arrow;
            turnIcon.rectTransform.localEulerAngles = new Vector3(0f, 0f, info.Turn == TurnKind.Arrive ? 0f : IconAngle(info.Turn));

            tagText.text = arrived ? NavText.ArrivedTag : NavText.DestinationTag;
            tagPill.color = arrived ? UIStyle.GreenLight : UIStyle.BlueLight;
            tagText.color = arrived ? UIStyle.Green : UIStyle.Blue;
            tagPill.rectTransform.sizeDelta = new Vector2(tagText.preferredWidth + 40f, 48f);
            destinationName.text = dest != null ? dest.DisplayName : "";
            distanceText.text = info.HasPath
                ? NavText.DistanceLabel(info.RemainingMeters) + " · " + NavText.ArrivalClock(info.EtaSeconds)
                : (dest != null ? dest.Subtitle : "");

            arrivedCheck.gameObject.SetActive(arrived);
            etaMinutes.gameObject.SetActive(!arrived);
            etaRing.color = arrived ? UIStyle.Green : UIStyle.Blue;
            etaMinutes.text = info.HasPath ? Mathf.Max(1, Mathf.CeilToInt(info.EtaSeconds / 60f)).ToString() : "-";
            etaCaption.text = arrived ? NavText.ArrivedTag : NavText.MinutesCaption;

            if (info.HasPath && info.RemainingMeters > routeLength) routeLength = info.RemainingMeters;
            float progress = arrived ? 1f : (routeLength > 0.1f && info.HasPath ? 1f - info.RemainingMeters / routeLength : 0f);
            progressFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            progressFill.color = arrived ? UIStyle.Green : UIStyle.Blue;
            previousButtonComponent.interactable = navigator.CanStepBack;
        }

        void UpdateCompassBadge()
        {
            if (tracker == null || !tracker.IsCalibrated) return;
            float trueHeading = tracker.HeadingDegrees + FloorPlanData.PlanNorthTrueBearing;
            compassArrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, trueHeading);
            int sector = Mathf.RoundToInt(Mathf.Repeat(trueHeading, 360f) / 45f) % 8;
            if (sector == shownCardinal) return;
            shownCardinal = sector;
            facingText.text = NavText.Cardinal(trueHeading);
        }

        static float IconAngle(TurnKind turn)
        {
            switch (turn)
            {
                case TurnKind.SlightRight: return -45f;
                case TurnKind.Right: return -90f;
                case TurnKind.SlightLeft: return 45f;
                case TurnKind.Left: return 90f;
                case TurnKind.UTurn: return 180f;
                default: return 0f;
            }
        }

        void ShowToast(string message)
        {
            toastText.text = message;
            toast.SetActive(true);
            UIPulse.Play(toast.transform);
            toastHideTime = Time.time + 4f;
        }

        static void Vibrate()
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }

        void OnEnterMain(Image button)
        {
            UIPulse.Play(button.transform);
            welcomePanel.Show(false);
        }

        void OnRoomChosen(RoomDestination room, GameObject card)
        {
            UIPulse.Play(card.transform);
            navigator.ChooseDestination(room);
        }

        void OnStartChip(string key, string label, Image chip)
        {
            selectedStart = key;
            calibrationStatus.text = NavText.StartSelected(label);
            foreach (KeyValuePair<string, Image> c in startChips) SetPillColors(c.Value, c.Key == selectedStart);
            UIPulse.Play(chip.transform);
        }

        void OnModeChip(PositioningMode mode)
        {
            if (modeController != null && mode != navigator.Mode) modeController.SetMode(mode);
        }

        void OnStartAtSelected()
        {
            if (tracker == null) return;
            if (selectedStart == null)
            {
                calibrationStatus.text = NavText.ChooseStartFirst;
                return;
            }

            string id = selectedStart.Substring(2);
            if (selectedStart.StartsWith("M:"))
            {
                ReferencePoint point = FloorPlanData.FindReferencePoint(id);
                if (point != null) tracker.CalibrateAtReferencePoint(point);
            }
            else
            {
                RoomDestination room = navigator.floorModel.FindRoom(id);
                if (room != null) tracker.CalibrateAtRoom(room);
            }
        }

        void OnDevTap()
        {
            if (Time.unscaledTime - devTapStart > 3f)
            {
                devTapStart = Time.unscaledTime;
                devTaps = 0;
            }
            if (++devTaps < 5 || compass == null) return;
            devTaps = 0;
            compass.ToggleDeveloperTools();
            ShowToast(compass.showCalibrationButtons ? NavText.DevToolsOn : NavText.DevToolsOff);
        }

        void OnSearch(string query)
        {
            query = (query ?? "").Trim().ToLowerInvariant();
            int shown = 0;
            foreach (KeyValuePair<RoomDestination, GameObject> card in roomCards)
            {
                bool match = query.Length == 0
                    || card.Key.DisplayName.ToLowerInvariant().Contains(query)
                    || card.Key.Subtitle.ToLowerInvariant().Contains(query);
                card.Value.SetActive(match);
                if (match) shown++;
            }
            noResultsText.gameObject.SetActive(shown == 0);
        }

        static void SetPillColors(Image pill, bool selected)
        {
            pill.color = selected ? UIStyle.Blue : UIStyle.BlueLight;
            Text label = pill.GetComponentInChildren<Text>();
            if (label != null) label.color = selected ? Color.white : UIStyle.Blue;
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("PentaWay UI", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
            canvasGo.AddComponent<GraphicRaycaster>();

            Image scrim = CreateImage("TopScrim", canvasGo.transform, new Color(0f, 0f, 0f, 0.45f));
            scrim.sprite = UIStyle.VerticalFade;
            scrim.raycastTarget = false;
            Place(scrim.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 460));
            topScrim = scrim.gameObject;
            topScrim.SetActive(false);

            Image backdrop = CreateImage("MapBackdrop", canvasGo.transform, UIStyle.Page);
            backdrop.raycastTarget = false;
            Fill(backdrop.rectTransform, 0, 0, 0, 0);
            mapBackdrop = backdrop.gameObject;
            mapBackdrop.SetActive(false);

            RectTransform labelsRoot = CreateRect("RoomLabels", canvasGo.transform);
            Fill(labelsRoot, 0, 0, 0, 0);
            canvasGo.AddComponent<RoomLabelOverlay>().Build(labelsRoot, navigator, font);

            safeRoot = CreateRect("SafeArea", canvasGo.transform);
            Fill(safeRoot, 0, 0, 0, 0);

            BuildDestinationPanel();
            miniMap = canvasGo.AddComponent<MiniMapView>();
            miniMap.Build(safeRoot, navigator, font, UIStyle.Arrow, UIStyle.Dot);
            miniMap.SetCornerPlacement(new Vector2(320f, 320f), 230f);
            BuildGuideArrow();
            BuildNavigationView();
            BuildCalibrationPanel();
            BuildArrivalCard();
            BuildToast();
            BuildWelcomePanel(canvasGo.transform);
        }

        void BuildWelcomePanel(Transform canvasRoot)
        {
            Image panel = CreateImage("WelcomePanel", canvasRoot, new Color32(0xD6, 0xE4, 0xFF, 0xFF));
            Fill(panel.rectTransform, 0, 0, 0, 0);

            welcomeLogoBase = new Vector2(0, 150);
            welcomeLogo = CreateCircleLogo("Logo", panel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), welcomeLogoBase, 460f, 20f);

            RectTransform content = CreateRect("Content", panel.transform);
            Fill(content, 0, 0, 0, 0);
            welcomeContent = content;
            welcomeContentGroup = content.gameObject.AddComponent<CanvasGroup>();
            welcomeContentGroup.alpha = 0f;

            Image start = PillButton("EnterMain", content, NavText.WelcomeButton, UIStyle.Blue, Color.white, null, 44, 65f);
            UIStyle.AddShadow(start);
            Place(start.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -260), new Vector2(560, 130));
            start.GetComponent<Button>().onClick.AddListener(() => OnEnterMain(start));

            welcomeStart = Time.unscaledTime;
            welcomePanel = UIPanelAnimator.Attach(panel.gameObject, 0f);
        }

        RectTransform CreateCircleLogo(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, float size, float border)
        {
            Image ring = CreateImage(name, parent, Color.white);
            ring.sprite = UIStyle.Circle;
            ring.raycastTarget = false;
            UIStyle.AddShadow(ring);
            Place(ring.rectTransform, anchor, anchor, pivot, position, new Vector2(size, size));

            if (logo != null)
            {
                Image maskImage = CreateImage("Mask", ring.transform, Color.white);
                maskImage.sprite = UIStyle.Circle;
                maskImage.raycastTarget = false;
                Fill(maskImage.rectTransform, border, border, border, border);
                maskImage.gameObject.AddComponent<Mask>().showMaskGraphic = false;

                var raw = CreateRect("Image", maskImage.transform).gameObject.AddComponent<RawImage>();
                raw.texture = logo;
                raw.raycastTarget = false;
                Fill(raw.rectTransform, 0, 0, 0, 0);

                float aspect = logo.height > 0 ? (float)logo.width / logo.height : 1f;
                Rect uv = new Rect(0f, 0f, 1f, 1f);
                if (aspect > 1f)
                {
                    uv.width = 1f / aspect;
                    uv.x = (1f - uv.width) * 0.5f;
                }
                else if (aspect < 1f)
                {
                    uv.height = aspect;
                    uv.y = (1f - uv.height) * 0.5f;
                }
                raw.uvRect = uv;
            }
            else
            {
                Text letter = CreateText("Letter", ring.transform, "P", Mathf.RoundToInt(size * 0.5f), UIStyle.Blue, TextAnchor.MiddleCenter, FontStyle.Bold);
                letter.horizontalOverflow = HorizontalWrapMode.Overflow;
                Fill(letter.rectTransform, 0, 0, 0, 0);
            }
            return ring.rectTransform;
        }

        void BuildDestinationPanel()
        {
            Image panel = CreateImage("DestinationPanel", safeRoot, UIStyle.Page);
            Fill(panel.rectTransform, 0, 0, 0, 0);

            Image header = CreateImage("Header", panel.transform, UIStyle.Blue);
            UIStyle.Round(header, 80f);
            header.raycastTarget = false;
            Place(header.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, 320), new Vector2(0, HeaderHeight + 320));

            float reserve = 0f;
            if (logo != null)
            {
                CreateCircleLogo("LogoBubble", panel.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -48), 150f, 8f);
                reserve = 190f;
            }

            Text title = CreateText("Title", panel.transform, NavText.ChooseDestinationTitle, 66, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(-reserve * 0.5f, -64), new Vector2(-(112f + reserve), 90));
            Text hint = CreateText("Hint", panel.transform, NavText.ChooseDestinationHint, 32, new Color(1f, 1f, 1f, 0.86f), TextAnchor.MiddleLeft);
            Place(hint.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(-reserve * 0.5f, -158), new Vector2(-(112f + reserve), 50));

            Image floorChip = CreateImage("FloorChip", panel.transform, new Color(1f, 1f, 1f, 0.2f));
            UIStyle.Round(floorChip, 28f);
            floorChip.raycastTarget = false;
            Text floorText = CreateText("Text", floorChip.transform, FloorPlanData.FloorName, 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            floorText.horizontalOverflow = HorizontalWrapMode.Overflow;
            Fill(floorText.rectTransform, 0, 0, 0, 0);
            Place(floorChip.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(56, -236), new Vector2(floorText.preferredWidth + 64f, 56));

            const float searchTop = HeaderHeight - 55f;
            Image box = Card("Search", panel.transform, UIStyle.Card, 55f);
            Place(box.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -searchTop), new Vector2(-96, 110));
            Image searchIcon = CreateImage("Icon", box.transform, UIStyle.Muted);
            searchIcon.sprite = UIStyle.IconSearch;
            Place(searchIcon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(36, 0), new Vector2(56, 56));
            Text input = CreateText("Text", box.transform, "", 36, UIStyle.Text, TextAnchor.MiddleLeft);
            input.supportRichText = false;
            Fill(input.rectTransform, 110, 0, 40, 0);
            Text placeholder = CreateText("Placeholder", box.transform, NavText.SearchPlaceholder, 36, UIStyle.Muted, TextAnchor.MiddleLeft);
            placeholder.fontStyle = FontStyle.Italic;
            Fill(placeholder.rectTransform, 110, 0, 40, 0);
            var field = box.gameObject.AddComponent<InputField>();
            field.targetGraphic = box;
            field.textComponent = input;
            field.placeholder = placeholder;
            field.lineType = InputField.LineType.SingleLine;
            field.onValueChanged.AddListener(OnSearch);

            float listTop = searchTop + 140f;
            RectTransform content = CreateScrollList(panel.transform, 40, 30, 40, listTop, true, true);
            roomCards.Clear();
            foreach (RoomDestination room in navigator.GetDestinations())
            {
                RoomDestination target = room;
                Image card = Card("Room_" + room.RoomId, content, UIStyle.Card, 48f);
                var button = card.gameObject.AddComponent<Button>();
                button.targetGraphic = card;
                GameObject cardObject = card.gameObject;
                button.onClick.AddListener(() => OnRoomChosen(target, cardObject));

                int tone = roomCards.Count % UIStyle.PastelBg.Length;
                Image circle = CreateImage("Icon", card.transform, UIStyle.PastelBg[tone]);
                circle.sprite = UIStyle.Circle;
                circle.raycastTarget = false;
                Place(circle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(188, 188));
                Image icon = CreateImage("Glyph", circle.transform, UIStyle.PastelFg[tone]);
                icon.sprite = UIStyle.IconTarget;
                icon.raycastTarget = false;
                Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(116, 116));

                Text name = CreateText("Name", card.transform, room.DisplayName, 42, UIStyle.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                name.resizeTextForBestFit = true;
                name.resizeTextMinSize = 24;
                name.resizeTextMaxSize = 42;
                Place(name.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -250), new Vector2(-40, 96));
                Text sub = CreateText("Subtitle", card.transform, room.Subtitle, 28, UIStyle.Muted, TextAnchor.UpperCenter);
                sub.resizeTextForBestFit = true;
                sub.resizeTextMinSize = 20;
                sub.resizeTextMaxSize = 28;
                Place(sub.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -352), new Vector2(-40, 80));

                roomCards.Add(new KeyValuePair<RoomDestination, GameObject>(room, cardObject));
            }

            noResultsText = CreateText("NoResults", panel.transform, NavText.NoResults, 34, UIStyle.Muted, TextAnchor.MiddleCenter);
            Place(noResultsText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -(listTop + 60f)), new Vector2(-96, 80));
            noResultsText.gameObject.SetActive(false);

            destinationPanel = UIPanelAnimator.Attach(panel.gameObject, 0f);
        }

        RectTransform AddDeco(Transform parent, Vector2 anchor, Vector2 position, float size, float alpha)
        {
            Image deco = CreateImage("Deco", parent, new Color(1f, 1f, 1f, alpha));
            deco.sprite = UIStyle.Circle;
            deco.raycastTarget = false;
            Place(deco.rectTransform, anchor, anchor, new Vector2(0.5f, 0.5f), position, new Vector2(size, size));
            return deco.rectTransform;
        }

        void BuildNavigationView()
        {
            RectTransform view = CreateRect("NavigationView", safeRoot);
            Fill(view, 0, 0, 0, 0);

            RoundButton("Menu", view, new Vector2(0, 1), new Vector2(28, -36), 120, UIStyle.Card, UIStyle.IconMenu, UIStyle.Text, navigator.Recalibrate);
            RoundButton("Search", view, new Vector2(1, 1), new Vector2(-28, -36), 120, UIStyle.Card, UIStyle.IconSearch, UIStyle.Text, navigator.BackToDestinations);

            instructionCard = Card("InstructionCard", view, UIStyle.Blue, 40f);
            RectTransform ic = instructionCard.rectTransform;
            ic.anchorMin = new Vector2(0, 1);
            ic.anchorMax = new Vector2(1, 1);
            ic.pivot = new Vector2(0.5f, 1);
            ic.offsetMin = new Vector2(168, -196);
            ic.offsetMax = new Vector2(-168, -36);
            Image iconBubble = CreateImage("IconBubble", ic, new Color(1f, 1f, 1f, 0.18f));
            iconBubble.sprite = UIStyle.Circle;
            Place(iconBubble.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(76, 0), new Vector2(116, 116));
            turnIcon = CreateImage("TurnIcon", ic, Color.white);
            turnIcon.sprite = UIStyle.Arrow;
            Place(turnIcon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(76, 0), new Vector2(80, 80));
            instructionText = CreateText("Instruction", ic, "", 38, Color.white, TextAnchor.LowerLeft, FontStyle.Bold);
            Fill(instructionText.rectTransform, 150, 70, 24, 14);
            detailText = CreateText("Detail", ic, "", 26, new Color(1, 1, 1, 0.88f), TextAnchor.UpperLeft);
            Fill(detailText.rectTransform, 150, 12, 24, 96);

            BuildCompassBadge(view);

            Image bar = Card("BottomBar", view, UIStyle.Card, 48f);
            Place(bar.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(-48, 150));
            Text appName = CreateText("AppName", bar.transform, "PentaWay", 38, UIStyle.Text, TextAnchor.LowerLeft, FontStyle.Bold);
            Fill(appName.rectTransform, 44, 72, 420, 18);
            modeText = CreateText("Mode", bar.transform, "", 26, UIStyle.Muted, TextAnchor.UpperLeft);
            Fill(modeText.rectTransform, 44, 18, 420, 82);
            Image devTap = CreateImage("DevTap", bar.transform, new Color(0f, 0f, 0f, 0f));
            Fill(devTap.rectTransform, 20, 10, 540, 10);
            var devButton = devTap.gameObject.AddComponent<Button>();
            devButton.targetGraphic = devTap;
            devButton.transition = Selectable.Transition.None;
            devButton.onClick.AddListener(OnDevTap);

            RectTransform buttons = CreateRect("Buttons", bar.transform);
            Place(buttons, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(500, 110));
            var layout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.spacing = 20;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            Button previous = RoundButton("Previous", buttons, Vector2.zero, Vector2.zero, 104, UIStyle.BlueLight, UIStyle.IconChevronLeft, UIStyle.Blue, () => navigator.MapStep(-1));
            previousButton = previous.gameObject;
            previousButtonComponent = previous;
            nextButton = RoundButton("Next", buttons, Vector2.zero, Vector2.zero, 104, UIStyle.Blue, UIStyle.IconChevronRight, Color.white, () => navigator.MapStep(1)).gameObject;
            mapButton = RoundButton("Map", buttons, Vector2.zero, Vector2.zero, 104, UIStyle.BlueLight, UIStyle.IconMap, UIStyle.Blue, () => miniMap.Toggle()).gameObject;
            RoundButton("Stop", buttons, Vector2.zero, Vector2.zero, 104, UIStyle.Red, UIStyle.IconClose, Color.white, navigator.BackToDestinations);

            BuildDestinationCard(view);

            view.gameObject.SetActive(false);
            navigationView = UIPanelAnimator.Attach(view.gameObject, 0f);
        }

        void BuildCompassBadge(RectTransform view)
        {
            Image badge = CreateImage("CompassBadge", view, UIStyle.Card);
            badge.sprite = UIStyle.Circle;
            badge.raycastTarget = false;
            UIStyle.AddShadow(badge);
            Place(badge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -176), new Vector2(108, 108));
            compassArrow = CreateImage("NorthArrow", badge.transform, UIStyle.Red);
            compassArrow.sprite = UIStyle.Arrow;
            compassArrow.raycastTarget = false;
            Place(compassArrow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64, 64));

            facingText = CreateText("Facing", badge.transform, NavText.NorthLabel, 22, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
            facingText.horizontalOverflow = HorizontalWrapMode.Overflow;
            facingText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
            Place(facingText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0, -2), new Vector2(200, 32));
            compassBadge = badge.gameObject;
            compassBadge.SetActive(false);
        }

        void BuildDestinationCard(RectTransform view)
        {
            Image card = Card("DestinationCard", view, UIStyle.Card, 48f);
            Place(card.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 194), new Vector2(-48, 250));

            Image circle = CreateImage("Icon", card.transform, UIStyle.BlueLight);
            circle.sprite = UIStyle.Circle;
            Place(circle.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(36, 8), new Vector2(150, 150));
            Image glyph = CreateImage("Glyph", circle.transform, UIStyle.Blue);
            glyph.sprite = UIStyle.IconTarget;
            Place(glyph.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(92, 92));

            tagPill = CreateImage("Tag", card.transform, UIStyle.BlueLight);
            UIStyle.Round(tagPill, 24f);
            tagPill.raycastTarget = false;
            Place(tagPill.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(214, -30), new Vector2(160, 48));
            tagText = CreateText("Text", tagPill.transform, NavText.DestinationTag, 24, UIStyle.Blue, TextAnchor.MiddleCenter, FontStyle.Bold);
            tagText.horizontalOverflow = HorizontalWrapMode.Overflow;
            Fill(tagText.rectTransform, 0, 0, 0, 0);

            destinationName = CreateText("Name", card.transform, "", 44, UIStyle.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(destinationName.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(214, -86), new Vector2(-444, 60));
            distanceText = CreateText("Distance", card.transform, "", 28, UIStyle.Muted, TextAnchor.UpperLeft);
            Place(distanceText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(214, -156), new Vector2(-444, 50));

            Image divider = CreateImage("Divider", card.transform, UIStyle.Divider);
            Place(divider.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-200, 10), new Vector2(3, 150));

            etaRing = CreateImage("EtaRing", card.transform, UIStyle.Blue);
            etaRing.sprite = UIStyle.Ring;
            Place(etaRing.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-100, 30), new Vector2(120, 120));
            etaMinutes = CreateText("Minutes", etaRing.transform, "", 46, UIStyle.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            Fill(etaMinutes.rectTransform, 0, 0, 0, 0);
            arrivedCheck = CreateImage("Check", etaRing.transform, UIStyle.Green);
            arrivedCheck.sprite = UIStyle.IconCheck;
            Place(arrivedCheck.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80, 80));
            etaCaption = CreateText("Caption", card.transform, NavText.MinutesCaption, 24, UIStyle.Muted, TextAnchor.MiddleCenter);
            Place(etaCaption.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-100, -60), new Vector2(180, 34));

            Image track = CreateImage("ProgressTrack", card.transform, UIStyle.Divider);
            UIStyle.Round(track, 6f);
            Place(track.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(-72, 12));
            progressFill = CreateImage("ProgressFill", track.transform, UIStyle.Blue);
            UIStyle.Round(progressFill, 6f);
            RectTransform fill = progressFill.rectTransform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
        }

        void BuildGuideArrow()
        {
            RectTransform root = CreateRect("GuideHint", safeRoot);
            Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 460));

            Image arrow = CreateImage("GuideArrow", root, UIStyle.Blue);
            arrow.sprite = UIStyle.Arrow;
            arrow.raycastTarget = false;
            Place(arrow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(260, 260));
            var outline = arrow.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(5, -5);
            guideArrowIcon = arrow.rectTransform;
            if (showCenterArrow && !showGuideArrowInCompass) arrow.gameObject.SetActive(false);

            guideHintText = CreateText("GuideText", root, "", 44, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Place(guideHintText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, showCenterArrow ? -400f : -135f), new Vector2(740, 120));
            var textOutline = guideHintText.gameObject.AddComponent<Outline>();
            textOutline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            textOutline.effectDistance = new Vector2(3, -3);

            guideArrow = root.gameObject;
            guideArrow.SetActive(false);
        }

        void UpdateGuide()
        {
            if (!guideAllowed || guideArrowIcon == null) return;
            if (showGuideArrowInCompass)
            {
                guideArrowIcon.localEulerAngles = new Vector3(0f, 0f, -navigator.GuideAngle);
                guideHintText.text = "";
                return;
            }
            if (!showPathFinderHint)
            {
                if (guideArrow.activeSelf) guideArrow.SetActive(false);
                return;
            }

            if (Time.unscaledTime >= nextGuideCheck)
            {
                nextGuideCheck = Time.unscaledTime + 0.1f;
                if (IsPathOnScreen(out guideScreenDirection, out guideMessage)) pathHiddenSince = -1f;
                else if (pathHiddenSince < 0f) pathHiddenSince = Time.unscaledTime;
            }

            bool show = pathHiddenSince >= 0f && Time.unscaledTime - pathHiddenSince >= HiddenDelay;
            if (guideArrow.activeSelf != show) guideArrow.SetActive(show);
            if (!show) return;
            float angle = Mathf.Atan2(guideScreenDirection.x, guideScreenDirection.y) * Mathf.Rad2Deg;
            guideArrowIcon.localEulerAngles = new Vector3(0f, 0f, -angle);
            guideHintText.text = guideMessage;
        }

        bool IsPathOnScreen(out Vector2 direction, out string message)
        {
            direction = Vector2.up;
            message = NavText.PathAhead;
            Camera cam = navigator.arCamera != null ? navigator.arCamera : Camera.main;
            IReadOnlyList<Vector3> corners = navigator.PathCorners;
            if (cam == null || corners == null || corners.Count < 2) return true;

            FloorModel floor = navigator.floorModel;
            Vector3 up = floor != null ? floor.transform.up : Vector3.up;
            int wallMask = floor != null ? 1 << floor.WallLayer : 0;
            Vector3 eye = cam.transform.position;
            float total = 0f;
            for (int i = 1; i < corners.Count; i++) total += Vector3.Distance(corners[i - 1], corners[i]);
            float limit = Mathf.Min(total, PathCheckLength);

            Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, up);
            if (forward.sqrMagnitude < 0.04f) forward = Vector3.ProjectOnPlane(cam.transform.up, up);

            bool haveFront = false;
            Vector3 frontViewport = Vector3.zero;
            float bestGap = float.MaxValue;
            for (float d = 0f; d <= limit + 1e-3f; d += PathCheckStep)
            {
                Vector3 p = PointOnPath(corners, d) + up * 0.05f;
                Vector3 v = cam.WorldToViewportPoint(p);
                if (v.z <= 0.05f) continue;
                if (wallMask != 0 && Physics.Linecast(eye, p, wallMask, QueryTriggerInteraction.Ignore)) continue;
                if (v.x >= ViewMinX && v.x <= ViewMaxX && v.y >= ViewMinY && v.y <= ViewMaxY) return true;
                Vector3 flat = Vector3.ProjectOnPlane(p - eye, up);
                bool ahead = flat.sqrMagnitude >= AheadMinDistance * AheadMinDistance && forward.sqrMagnitude > 1e-6f &&
                             Mathf.Abs(Vector3.SignedAngle(forward, flat, up)) <= AheadMaxAngle;
                if (!ahead) continue;
                float gapX = Mathf.Max(0f, Mathf.Max(ViewMinX - v.x, v.x - ViewMaxX)) * cam.aspect;
                float gapY = Mathf.Max(0f, Mathf.Max(ViewMinY - v.y, v.y - ViewMaxY));
                float gap = gapX * gapX + gapY * gapY;
                if (gap < bestGap)
                {
                    bestGap = gap;
                    haveFront = true;
                    frontViewport = v;
                }
            }

            if (haveFront)
            {
                float centerY = (ViewMinY + ViewMaxY) * 0.5f;
                direction = new Vector2((frontViewport.x - 0.5f) * cam.aspect, frontViewport.y - centerY);
                if (direction.sqrMagnitude < 1e-6f) direction = Vector2.down;
                bool vertical = Mathf.Abs(direction.x) < Mathf.Abs(direction.y) * 0.6f;
                message = vertical ? (direction.y < 0f ? NavText.LookDownToPath : NavText.LookUpToPath)
                                   : (direction.x < 0f ? NavText.LookLeftToPath : NavText.LookRightToPath);
                return false;
            }

            Vector3 target = Vector3.ProjectOnPlane(PointOnPath(corners, Mathf.Min(3f, total)) - eye, up);
            if (forward.sqrMagnitude < 1e-6f || target.sqrMagnitude < 1e-4f) return true;
            float turn = Vector3.SignedAngle(forward, target, up);
            if (Mathf.Abs(turn) > 135f)
            {
                direction = Vector2.down;
                message = NavText.TurnAroundToPath;
            }
            else if (Mathf.Abs(turn) > 30f)
            {
                direction = turn > 0f ? Vector2.right : Vector2.left;
                message = turn > 0f ? NavText.TurnRightToPath : NavText.TurnLeftToPath;
            }
            else
            {
                direction = Vector2.up;
                message = NavText.PathAhead;
            }
            return false;
        }

        static Vector3 PointOnPath(IReadOnlyList<Vector3> corners, float distance)
        {
            for (int i = 1; i < corners.Count; i++)
            {
                float segment = Vector3.Distance(corners[i - 1], corners[i]);
                if (distance <= segment && segment > 1e-4f) return Vector3.Lerp(corners[i - 1], corners[i], distance / segment);
                distance -= segment;
            }
            return corners[corners.Count - 1];
        }

        void BuildCalibrationPanel()
        {
            Image sheet = Card("CalibrationSheet", safeRoot, UIStyle.Card, 48f);
            Place(sheet.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, -48), new Vector2(0, 860));

            Image handle = CreateImage("Handle", sheet.transform, UIStyle.Divider);
            UIStyle.Round(handle, 6f);
            Place(handle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(120, 12));

            RectTransform modeRow = CreateRow("Modes", sheet.transform, -56, 84);
            modeChips.Clear();
            foreach (PositioningMode mode in new[] { PositioningMode.Compass, PositioningMode.Map })
            {
                PositioningMode m = mode;
                Image pill = PillButton("Mode_" + mode, modeRow, NavText.ModeName(mode), UIStyle.BlueLight, UIStyle.Blue, () => OnModeChip(m), 32, 42f);
                modeChips.Add(new KeyValuePair<PositioningMode, Image>(mode, pill));
            }

            Text title = CreateText("Title", sheet.transform, NavText.CalibrateTitle, 46, UIStyle.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -164), new Vector2(-96, 60));
            calibrationHelp = CreateText("Help", sheet.transform, NavText.CompassHelp, 30, UIStyle.Muted, TextAnchor.UpperLeft);
            Place(calibrationHelp.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -228), new Vector2(-96, 120));

            RectTransform chipContent = CreateScrollList(sheet.transform, 48, 0, 48, 0, false);
            Place((RectTransform)chipContent.parent, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -360), new Vector2(-96, 96));
            startChips.Clear();
            foreach (ReferencePoint point in FloorPlanData.ReferencePoints)
                AddStartChip(chipContent, "M:" + point.Name, NavText.StickerStart(point.Name));
            foreach (RoomDestination room in navigator.GetDestinations())
                AddStartChip(chipContent, "R:" + room.RoomId, NavText.DoorStart(room.DisplayName));

            RectTransform row1 = CreateRow("Row1", sheet.transform, -486, 110);
            imHereButton = PillButton("ImHere", row1, NavText.ImHere, UIStyle.Blue, Color.white, OnStartAtSelected).gameObject;
            startHereButton = PillButton("StartHere", row1, NavText.StartHere, UIStyle.Blue, Color.white, OnStartAtSelected).gameObject;

            RectTransform row2 = CreateRow("Row2", sheet.transform, -616, 110);
            PillButton("TestMode", row2, NavText.TestMode, UIStyle.BlueLight, UIStyle.Blue, navigator.StartTestMode);
            PillButton("Back", row2, NavText.Back, UIStyle.Page, UIStyle.Text, navigator.BackToDestinations);

            calibrationStatus = CreateText("Status", sheet.transform, "", 30, UIStyle.Blue, TextAnchor.UpperLeft, FontStyle.Bold);
            Place(calibrationStatus.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -746), new Vector2(-96, 80));

            sheet.gameObject.SetActive(false);
            calibrationPanel = UIPanelAnimator.Attach(sheet.gameObject, 120f);
        }

        void AddStartChip(RectTransform parent, string key, string label)
        {
            Image chip = PillButton("Start_" + key, parent, label, UIStyle.BlueLight, UIStyle.Blue, null, 30, 40f);
            chip.GetComponent<Button>().onClick.AddListener(() => OnStartChip(key, label, chip));
            chip.gameObject.AddComponent<LayoutElement>().preferredWidth = Mathf.Max(170f, label.Length * 19f + 70f);
            startChips.Add(new KeyValuePair<string, Image>(key, chip));
        }

        void BuildArrivalCard()
        {
            Image dim = CreateImage("ArrivalCard", safeRoot, new Color(0f, 0f, 0f, 0.35f));
            Fill(dim.rectTransform, 0, 0, 0, 0);

            Image card = Card("Card", dim.transform, UIStyle.Card, 56f);
            Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880, 640));

            Image badge = CreateImage("Badge", card.transform, UIStyle.GreenLight);
            badge.sprite = UIStyle.Circle;
            Place(badge.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -50), new Vector2(180, 180));
            Image check = CreateImage("Check", badge.transform, UIStyle.Green);
            check.sprite = UIStyle.IconCheck;
            Place(check.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 120));

            Text title = CreateText("Title", card.transform, NavText.ArrivedTitle, 52, UIStyle.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -260), new Vector2(-80, 70));
            arrivalSubtitle = CreateText("Subtitle", card.transform, "", 34, UIStyle.Muted, TextAnchor.MiddleCenter);
            Place(arrivalSubtitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -340), new Vector2(-80, 60));

            RectTransform row = CreateRow("Buttons", card.transform, -450, 110);
            PillButton("Close", row, NavText.Close, UIStyle.Page, UIStyle.Text, () => arrivalCard.Show(false));
            PillButton("Other", row, NavText.OtherDestination, UIStyle.Blue, Color.white, navigator.BackToDestinations);

            dim.gameObject.SetActive(false);
            arrivalCard = UIPanelAnimator.Attach(dim.gameObject, 0f);
        }

        void BuildToast()
        {
            Image bg = Card("Toast", safeRoot, UIStyle.ToastBg, 40f);
            Place(bg.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 470), new Vector2(940, 150));
            bg.raycastTarget = false;
            toast = bg.gameObject;
            toastText = CreateText("Text", bg.transform, "", 30, Color.white, TextAnchor.MiddleCenter);
            Fill(toastText.rectTransform, 36, 10, 36, 10);
            toast.SetActive(false);
        }

        Image Card(string name, Transform parent, Color color, float radius)
        {
            Image image = CreateImage(name, parent, color);
            UIStyle.Round(image, radius);
            UIStyle.AddShadow(image);
            return image;
        }

        Image PillButton(string name, Transform parent, string label, Color background, Color textColor, UnityAction onClick, int fontSize = 34, float radius = 55f)
        {
            Image image = CreateImage(name, parent, background);
            UIStyle.Round(image, radius);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(onClick);
            Text text = CreateText("Label", image.transform, label, fontSize, textColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Fill(text.rectTransform, 12, 0, 12, 0);
            return image;
        }

        Button RoundButton(string name, Transform parent, Vector2 anchor, Vector2 position, float size, Color background, Sprite icon, Color iconColor, UnityAction onClick)
        {
            Image image = CreateImage(name, parent, background);
            image.sprite = UIStyle.Circle;
            UIStyle.AddShadow(image);
            Place(image.rectTransform, anchor, anchor, anchor, position, new Vector2(size, size));
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            button.onClick.AddListener(() => UIPulse.Play(image));
            Image glyph = CreateImage("Icon", image.transform, iconColor);
            glyph.sprite = icon;
            glyph.raycastTarget = false;
            Place(glyph.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.55f, size * 0.55f));
            return button;
        }

        RectTransform CreateRow(string name, Transform parent, float y, float height)
        {
            RectTransform row = CreateRect(name, parent);
            Place(row, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(-96, height));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 20;
            layout.childForceExpandWidth = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            return row;
        }

        RectTransform CreateScrollList(Transform parent, float left, float bottom, float right, float top, bool vertical, bool grid = false)
        {
            RectTransform viewport = CreateRect("Viewport", parent);
            Fill(viewport, left, bottom, right, top);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0);

            RectTransform content = CreateRect("Content", viewport);
            if (vertical)
            {
                content.anchorMin = new Vector2(0, 1);
                content.anchorMax = new Vector2(1, 1);
                content.pivot = new Vector2(0.5f, 1);
                if (grid)
                {
                    var gridLayout = content.gameObject.AddComponent<GridLayoutGroup>();
                    gridLayout.padding = new RectOffset(4, 4, 8, 24);
                    gridLayout.spacing = new Vector2(24, 24);
                    gridLayout.cellSize = new Vector2(480, 480);
                    gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                    gridLayout.constraintCount = 2;
                    gridLayout.childAlignment = TextAnchor.UpperCenter;
                    gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
                    gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
                    roomGrid = gridLayout;
                    roomGridViewport = viewport;
                }
                else
                {
                    var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
                    layout.spacing = 24;
                    layout.padding = new RectOffset(4, 4, 8, 24);
                    layout.childControlHeight = true;
                    layout.childControlWidth = true;
                    layout.childForceExpandHeight = false;
                }
                content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            else
            {
                content.anchorMin = new Vector2(0, 0);
                content.anchorMax = new Vector2(0, 1);
                content.pivot = new Vector2(0, 0.5f);
                var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 16;
                layout.childControlHeight = true;
                layout.childControlWidth = true;
                layout.childForceExpandWidth = false;
                content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = !vertical;
            scroll.vertical = vertical;
            return content;
        }

        void ApplySafeArea()
        {
            Rect area = Screen.safeArea;
            if (area == lastSafeArea || Screen.width <= 0 || Screen.height <= 0) return;
            lastSafeArea = area;
            safeRoot.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            safeRoot.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        static void Fill(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        static Image CreateImage(string name, Transform parent, Color color)
        {
            Image image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        Text CreateText(string name, Transform parent, string text, int size, Color color, TextAnchor anchor, FontStyle style = FontStyle.Normal)
        {
            Text t = CreateRect(name, parent).gameObject.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }
    }
}