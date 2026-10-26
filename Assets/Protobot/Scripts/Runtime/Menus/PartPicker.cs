using System;
using System.Collections.Generic;
using Protobot.Core;
using Protobot.Parts;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Menus
{
    /// <summary>
    /// The parts menu that opens when you click a bin: bin tabs on the left, a scrolling grid of part cards,
    /// a details panel on the right, and a search box that looks through every bin.
    /// Clicking a card puts that part on the workbench under the window.
    ///
    /// The grid is virtualised: the full part list is plain data, and only enough card objects for the
    /// visible rows (plus a buffer row above and below) exist. As you scroll, cards that leave the top are
    /// moved to the bottom and shown the next parts, so 500 parts cost the same as 30.
    /// Nothing about which part is hovered/selected is stored on a card; it's re-applied on every bind.
    /// </summary>
    public class PartPicker : MonoBehaviour
    {
        const float CellWidth = 152f;
        const float CellHeight = 190f;
        const float Spacing = 12f;
        const int BufferRows = 1;
        static readonly Vector2 NormalSize = new(1440f, 820f);
        static readonly Vector2 ExpandedSize = new(1820f, 1000f);

        [SerializeField] UiTheme theme;
        [SerializeField] PartCatalog catalog;
        [SerializeField] PartSpawner spawner;
        [SerializeField] Hud hud;

        // Data
        readonly List<PartEntry> items = new();
        string currentBin;
        string hoveredId;
        string selectedId;

        // Views
        GameObject root;
        RectTransform window;
        TextMeshProUGUI titleText;
        TextMeshProUGUI countText;
        Image titleChip;
        TMP_InputField search;
        ScrollRect scroll;
        RectTransform viewport;
        RectTransform content;
        readonly List<PartCardView> pool = new();
        readonly List<(string id, Image background, TextMeshProUGUI label)> binTabs = new();
        int columns = 1;
        int firstBoundRow = -1;
        bool expanded;

        // Details panel
        Image detailThumb;
        TextMeshProUGUI detailName;
        TextMeshProUGUI detailSize;
        TextMeshProUGUI detailMass;
        TextMeshProUGUI detailBin;
        GameObject detailCuttable;
        Button placeButton;

        public bool IsOpen => root != null && root.activeSelf;

        void Awake()
        {
            UiFactory.Canvas(gameObject, 20);
            Build();
            root.SetActive(false);
        }

        void OnEnable() => PartBin.Opened += OnBinOpened;
        void OnDisable() => PartBin.Opened -= OnBinOpened;

        void OnBinOpened(PartBin bin) => Open(bin.BinId);

        public void Open(string binId)
        {
            root.SetActive(true);
            MenuState.Open(this);
            search.SetTextWithoutNotify("");
            ShowBin(binId);
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            root.SetActive(false);
            MenuState.Close(this);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void Update()
        {
            if (!IsOpen)
                return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && MenuState.ChangedFrame != Time.frameCount)
                Close();
        }

        // ---------------------------------------------------------------- data

        void ShowBin(string binId)
        {
            currentBin = binId;
            items.Clear();
            if (binId == PartBins.Recent)
            {
                foreach (var id in RecentParts.Ids)
                    if (Find(id) is { } part)
                        items.Add(part);
            }
            else
            {
                items.AddRange(catalog.InBin(binId));
                items.Sort((a, b) => NaturalCompare(a.displayName, b.displayName));
            }

            var binIndex = BinIndex(binId);
            titleText.text = binIndex >= 0 ? PartBins.All[binIndex].label.ToUpperInvariant() : binId;
            titleChip.color = binIndex >= 0 ? theme.BinColor(binIndex) : theme.accent;
            HighlightTab(binId);
            Refresh(items.Count == 0 && binId == PartBins.Recent ? "Parts you place will show up here." : null);
        }

        void OnSearchChanged(string query)
        {
            query = query.Trim();
            if (query.Length == 0)
            {
                ShowBin(currentBin);
                return;
            }

            var tokens = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            items.Clear();
            foreach (var part in catalog.parts)
            {
                var name = part.displayName.ToLowerInvariant();
                var match = true;
                foreach (var t in tokens)
                    match &= name.Contains(t) || part.id.ToLowerInvariant().Contains(t);
                if (match)
                    items.Add(part);
            }
            items.Sort((a, b) => NaturalCompare(a.displayName, b.displayName));
            titleText.text = $"SEARCH: \"{query.ToUpperInvariant()}\"";
            titleChip.color = theme.accent;
            HighlightTab(null);
            Refresh(items.Count == 0 ? "No parts match that search." : null);
        }

        /// <summary>Data changed: resize the (faked) content, reset the scroll, rebind every pooled card.</summary>
        void Refresh(string emptyMessage)
        {
            countText.text = emptyMessage ?? $"{items.Count} part{(items.Count == 1 ? "" : "s")}";
            LayoutPool();
            scroll.verticalNormalizedPosition = 1f;
            content.anchoredPosition = Vector2.zero;
            firstBoundRow = -1;
            BindVisible();
            ShowDetails(items.Count > 0 ? items[0] : null);
        }

        PartEntry Find(string id)
        {
            foreach (var part in catalog.parts)
                if (part.id == id)
                    return part;
            return null;
        }

        static int BinIndex(string binId)
        {
            for (var i = 0; i < PartBins.All.Count; i++)
                if (PartBins.All[i].id == binId)
                    return i;
            return -1;
        }

        // ---------------------------------------------------------------- virtualised grid

        /// <summary>Works out columns and how many cards are needed for the current viewport size.</summary>
        void LayoutPool()
        {
            Canvas.ForceUpdateCanvases();
            var viewSize = viewport.rect.size;
            columns = Mathf.Max(1, Mathf.FloorToInt((viewSize.x + Spacing) / (CellWidth + Spacing)));
            var totalRows = Mathf.CeilToInt(items.Count / (float)columns);
            content.sizeDelta = new Vector2(0f, Mathf.Max(0f, totalRows * (CellHeight + Spacing) - Spacing) + Spacing * 2f);

            var visibleRows = Mathf.CeilToInt(viewSize.y / (CellHeight + Spacing));
            var poolSize = (visibleRows + 2 * BufferRows) * columns;
            while (pool.Count < poolSize)
                pool.Add(PartCardView.Create(content, theme, this));
            for (var i = poolSize; i < pool.Count; i++)
                pool[i].gameObject.SetActive(false);
            poolCount = poolSize;
        }

        int poolCount;

        void OnScroll(Vector2 _) => BindVisible();

        /// <summary>Recycling step: give each pooled card the data index it should show at the current scroll offset.</summary>
        void BindVisible(bool force = false)
        {
            var rowHeight = CellHeight + Spacing;
            var firstRow = Mathf.Max(0, Mathf.FloorToInt(content.anchoredPosition.y / rowHeight) - BufferRows);
            if (firstRow == firstBoundRow && !force)
                return;
            firstBoundRow = firstRow;

            var gridWidth = columns * (CellWidth + Spacing) - Spacing;
            var left = Mathf.Max(0f, (viewport.rect.width - gridWidth) / 2f);
            for (var i = 0; i < poolCount; i++)
            {
                var dataIndex = firstRow * columns + i;
                var card = pool[i];
                if (dataIndex >= items.Count)
                {
                    card.gameObject.SetActive(false);
                    continue;
                }
                card.gameObject.SetActive(true);
                var row = dataIndex / columns;
                var col = dataIndex % columns;
                card.Rect.anchoredPosition = new Vector2(left + col * (CellWidth + Spacing), -(Spacing + row * rowHeight));
                var part = items[dataIndex];
                card.Bind(dataIndex, part, theme.BinColor(Mathf.Max(0, BinIndex(part.binId))), part.id == hoveredId || part.id == selectedId);
            }
        }

        internal void OnCardHover(int dataIndex, bool entered)
        {
            if (dataIndex < 0 || dataIndex >= items.Count)
                return;
            hoveredId = entered ? items[dataIndex].id : null;
            if (entered)
                ShowDetails(items[dataIndex]);
            else if (Find(selectedId) is { } selected)
                ShowDetails(selected);
            BindVisible(true);
        }

        internal void OnCardClick(int dataIndex)
        {
            if (dataIndex < 0 || dataIndex >= items.Count)
                return;
            selectedId = items[dataIndex].id;
            Place(items[dataIndex]);
            BindVisible(true);
        }

        void Place(PartEntry part)
        {
            if (part == null)
                return;
            var spawned = spawner.Spawn(part);
            hud?.ShowToast(spawned != null ? $"Placed {part.displayName} on the workbench" : $"{part.displayName} has no model yet");
        }

        // ---------------------------------------------------------------- details

        void ShowDetails(PartEntry part)
        {
            var has = part != null;
            detailThumb.enabled = has && part.thumbnail != null;
            detailThumb.sprite = has ? part.thumbnail : null;
            detailName.text = has ? part.displayName : "";
            detailSize.text = has ? part.SizeLabel : "";
            detailMass.text = has ? part.MassLabel : "";
            var binIndex = has ? BinIndex(part.binId) : -1;
            detailBin.text = binIndex >= 0 ? PartBins.All[binIndex].label : "";
            detailCuttable.SetActive(has && part.cuttable);
            placeButton.interactable = has;
            placeButton.onClick.RemoveAllListeners();
            if (has)
                placeButton.onClick.AddListener(() => Place(part));
        }

        // ---------------------------------------------------------------- building the UI

        void Build()
        {
            var dim = UiFactory.Image(transform, "Dim", new Color(0f, 0f, 0f, 0.45f), null, true);
            dim.rectTransform.Fill();
            root = dim.gameObject;
            var dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(Close);

            window = UiFactory.Panel(dim.transform, "Window", theme, theme.panel, true).rectTransform;
            window.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, NormalSize);
            window.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;  // clicks on the window don't close it

            BuildHeader();
            BuildSidebar();
            BuildDetails();
            BuildGrid();

            var status = UiFactory.Text(window, "Status", theme,
                "Click a part to place it on the workbench under the window   ·   Search looks through every bin   ·   Esc to close",
                20f, theme.textDim, false, TextAlignmentOptions.MidlineLeft);
            status.rectTransform.anchorMin = new Vector2(0f, 0f);
            status.rectTransform.anchorMax = new Vector2(1f, 0f);
            status.rectTransform.offsetMin = new Vector2(300f, 14f);
            status.rectTransform.offsetMax = new Vector2(-370f, 50f);
        }

        void BuildHeader()
        {
            var stripes = UiFactory.Hazard(window, "Stripes", theme);
            stripes.rectTransform.anchorMin = new Vector2(0f, 1f);
            stripes.rectTransform.anchorMax = new Vector2(1f, 1f);
            stripes.rectTransform.offsetMin = new Vector2(14f, -24f);
            stripes.rectTransform.offsetMax = new Vector2(-14f, -12f);

            var caption = UiFactory.Text(window, "Caption", theme, "PARTS BIN", 20f, theme.textDim, true);
            caption.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(64f, -34f), new Vector2(400f, 24f));
            titleChip = UiFactory.Image(window, "Chip", theme.accent, theme.rounded);
            titleChip.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, -40f), new Vector2(26f, 48f));
            titleText = UiFactory.Text(window, "Title", theme, "", 46f, theme.text, true);
            titleText.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(64f, -52f), new Vector2(800f, 60f));
            countText = UiFactory.Text(window, "Count", theme, "", 22f, theme.textDim, false, TextAlignmentOptions.MidlineRight);
            countText.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-330f, -60f), new Vector2(380f, 40f));

            var expand = UiFactory.Button(window, "Expand", theme, "EXPAND", ToggleExpand, false, 22f);
            ((RectTransform)expand.transform).Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-170f, -48f), new Vector2(130f, 50f));
            var close = UiFactory.Button(window, "Close", theme, "CLOSE", Close, true, 22f);
            ((RectTransform)close.transform).Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -48f), new Vector2(130f, 50f));
        }

        void BuildSidebar()
        {
            var side = UiFactory.Image(window, "Sidebar", theme.panelInset, theme.rounded).rectTransform;
            side.anchorMin = new Vector2(0f, 0f);
            side.anchorMax = new Vector2(0f, 1f);
            side.pivot = new Vector2(0f, 0.5f);
            side.offsetMin = new Vector2(24f, 64f);
            side.offsetMax = new Vector2(284f, -116f);
            UiFactory.VerticalList(side, 3f, new RectOffset(8, 8, 8, 8));

            for (var i = 0; i < PartBins.All.Count; i++)
            {
                var bin = PartBins.All[i];
                var tab = UiFactory.Image(side, bin.id, Color.clear, theme.rounded, true);
                tab.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
                var button = tab.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                var id = bin.id;
                button.onClick.AddListener(() =>
                {
                    search.SetTextWithoutNotify("");
                    ShowBin(id);
                });
                var chip = UiFactory.Image(tab.transform, "Chip", theme.BinColor(i), theme.rounded);
                chip.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(12f, 16f));
                var label = UiFactory.Text(tab.transform, "Label", theme, bin.label, 20f, theme.text, false, TextAlignmentOptions.MidlineLeft);
                label.rectTransform.Fill(28f, 0f, 6f, 0f);
                binTabs.Add((bin.id, tab, label));
            }
        }

        void HighlightTab(string binId)
        {
            foreach (var (id, background, label) in binTabs)
            {
                var selected = id == binId;
                background.color = selected ? theme.accent : Color.clear;
                label.color = selected ? theme.accentText : theme.text;
                label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        void BuildGrid()
        {
            search = UiFactory.InputField(window, "Search", theme, "Search all parts…  (e.g. \"36T gear\", \"standoff 1.00\")", 22f);
            var searchRect = (RectTransform)search.transform;
            searchRect.anchorMin = new Vector2(0f, 1f);
            searchRect.anchorMax = new Vector2(1f, 1f);
            searchRect.offsetMin = new Vector2(300f, -166f);
            searchRect.offsetMax = new Vector2(-370f, -116f);
            search.onValueChanged.AddListener(OnSearchChanged);

            var area = UiFactory.Image(window, "Grid", theme.panelInset, theme.rounded).rectTransform;
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(300f, 64f);
            area.offsetMax = new Vector2(-370f, -178f);

            scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.inertia = true;
            viewport = UiFactory.Rect("Viewport", area).Fill(6f, 6f, 22f, 6f);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = UiFactory.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.onValueChanged.AddListener(OnScroll);

            var bar = UiFactory.Rect("Scrollbar", area);
            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(1f, 0.5f);
            bar.offsetMin = new Vector2(-14f, 8f);
            bar.offsetMax = new Vector2(-6f, -8f);
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var handleArea = UiFactory.Rect("Sliding", bar).Fill();
            var handle = UiFactory.Image(handleArea, "Handle", theme.button, theme.rounded, true);
            handle.rectTransform.Fill();
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.targetGraphic = handle;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        void BuildDetails()
        {
            var panel = UiFactory.Image(window, "Details", theme.panelInset, theme.rounded).rectTransform;
            panel.anchorMin = new Vector2(1f, 0f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.offsetMin = new Vector2(-354f, 64f);
            panel.offsetMax = new Vector2(-24f, -116f);

            var thumbBack = UiFactory.Image(panel, "ThumbBack", theme.card, theme.rounded);
            thumbBack.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(300f, 300f));
            detailThumb = UiFactory.Image(thumbBack.transform, "Thumb", Color.white);
            detailThumb.rectTransform.Fill(6f, 6f, 6f, 6f);
            detailThumb.preserveAspect = true;

            detailName = UiFactory.Text(panel, "Name", theme, "", 30f, theme.text, true);
            detailName.textWrappingMode = TextWrappingModes.Normal;
            detailName.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -324f), new Vector2(300f, 76f));
            detailName.alignment = TextAlignmentOptions.TopLeft;

            detailSize = DetailRow(panel, "SIZE", -410f);
            detailMass = DetailRow(panel, "WEIGHT", -444f);
            detailBin = DetailRow(panel, "BIN", -478f);

            var badge = UiFactory.Image(panel, "Cuttable", theme.accent, theme.rounded);
            badge.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -520f), new Vector2(300f, 40f));
            var badgeText = UiFactory.Text(badge.transform, "Text", theme, "FULL LENGTH · CUT TO SIZE WITH THE SAW", 18f, theme.accentText, true, TextAlignmentOptions.Center);
            badgeText.rectTransform.Fill(6f, 0f, 6f, 0f);
            detailCuttable = badge.gameObject;

            placeButton = UiFactory.Button(panel, "Place", theme, "PLACE ON WORKBENCH", null, true, 24f);
            ((RectTransform)placeButton.transform).Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(300f, 56f));
        }

        TextMeshProUGUI DetailRow(RectTransform panel, string label, float y)
        {
            var key = UiFactory.Text(panel, label, theme, label, 20f, theme.textDim, true);
            key.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(300f, 30f));
            var value = UiFactory.Text(panel, label + "Value", theme, "", 22f, theme.text, false, TextAlignmentOptions.MidlineRight);
            value.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(300f, 30f));
            return value;
        }

        void ToggleExpand()
        {
            expanded = !expanded;
            window.sizeDelta = expanded ? ExpandedSize : NormalSize;
            var top = content.anchoredPosition.y;
            LayoutPool();
            content.anchoredPosition = new Vector2(0f, Mathf.Min(top, Mathf.Max(0f, content.sizeDelta.y - viewport.rect.height)));
            firstBoundRow = -1;
            BindVisible();
        }

        /// <summary>Sorts "Shaft 2" before "Shaft 10" and "12T" before "84T".</summary>
        static int NaturalCompare(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    var si = i;
                    var sj = j;
                    while (i < a.Length && (char.IsDigit(a[i]) || a[i] == '.')) i++;
                    while (j < b.Length && (char.IsDigit(b[j]) || b[j] == '.')) j++;
                    double.TryParse(a.Substring(si, i - si), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var na);
                    double.TryParse(b.Substring(sj, j - sj), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nb);
                    var c = na.CompareTo(nb);
                    if (c != 0)
                        return c;
                }
                else
                {
                    var c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                    if (c != 0)
                        return c;
                    i++;
                    j++;
                }
            }
            return a.Length.CompareTo(b.Length);
        }
    }

    /// <summary>A pooled part card. It only remembers which data index it currently shows.</summary>
    public class PartCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        PartPicker picker;
        Image background;
        Image thumbnail;
        Image stripe;
        TextMeshProUGUI nameText;
        TextMeshProUGUI sizeText;
        UiTheme theme;
        int boundIndex = -1;

        public RectTransform Rect { get; private set; }

        public static PartCardView Create(RectTransform parent, UiTheme theme, PartPicker picker)
        {
            var bg = UiFactory.Image(parent, "Card", theme.card, theme.rounded, true);
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(152f, 190f);
            var card = bg.gameObject.AddComponent<PartCardView>();
            card.Rect = rt;
            card.picker = picker;
            card.theme = theme;
            card.background = bg;
            card.thumbnail = UiFactory.Image(rt, "Thumb", Color.white);
            card.thumbnail.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(136f, 120f));
            card.thumbnail.preserveAspect = true;
            card.nameText = UiFactory.Text(rt, "Name", theme, "", 17f, theme.cardText, true, TextAlignmentOptions.TopLeft);
            card.nameText.textWrappingMode = TextWrappingModes.Normal;
            card.nameText.overflowMode = TextOverflowModes.Ellipsis;
            card.nameText.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(136f, 44f));
            card.sizeText = UiFactory.Text(rt, "Size", theme, "", 14f, new Color(0.35f, 0.35f, 0.37f), false, TextAlignmentOptions.BottomLeft);
            card.sizeText.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(136f, 18f));
            card.stripe = UiFactory.Image(rt, "Stripe", Color.white, theme.rounded);
            card.stripe.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(152f, 5f));
            return card;
        }

        public void Bind(int dataIndex, PartEntry part, Color binColor, bool highlighted)
        {
            boundIndex = dataIndex;
            thumbnail.sprite = part.thumbnail;
            thumbnail.enabled = part.thumbnail != null;
            nameText.text = part.displayName;
            var d = new[] { part.size.x, part.size.y, part.size.z };
            System.Array.Sort(d);
            sizeText.text = $"{d[2] * 1000f:0.#} mm";
            stripe.color = binColor;
            background.color = highlighted ? theme.cardHover : theme.card;
            Rect.localScale = highlighted ? new Vector3(1.03f, 1.03f, 1f) : Vector3.one;
        }

        public void OnPointerEnter(PointerEventData eventData) => picker.OnCardHover(boundIndex, true);
        public void OnPointerExit(PointerEventData eventData) => picker.OnCardHover(boundIndex, false);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                picker.OnCardClick(boundIndex);
        }
    }
}
