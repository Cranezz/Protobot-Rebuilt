using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: the parts browser (replaces the original's add-parts list): a search box, categories (Structure,
    // Motion, Electronics, the VEX library, field elements), the parts as tiles with their icons, and for the
    // chosen part its sizes (e.g. a C-channel's 1x2 and hole count). Choosing a part starts placing it, as the
    // original did: click in the view to place, R to turn it, Esc to stop. The panel folds away to the right.
    public class PartsBrowser : MonoBehaviour {
        private static readonly string[] Categories = { "All", "Structure", "Motion", "Electronics", "VEX library", "Field" };
        private const string CategoryKey = "V2.PartsCategory";

        private RectTransform panel, grid, footer, tab;
        private TMP_InputField search;
        private TextMeshProUGUI countLabel;
        private readonly List<(Button button, TextMeshProUGUI label)> categoryButtons = new();
        private readonly List<(PartType type, Image tile)> tiles = new();
        private int category;
        private PartType selected;
        private Placement placement;
        private bool collapsed;
        private RectTransform cameraOptions;
        private Vector2 cameraOptionsHome;

        public static PartsBrowser Instance { get; private set; }

        public static void Create(RectTransform root) {
            Instance = Ui.Rect("Parts Browser", root).Fill().gameObject.AddComponent<PartsBrowser>();
            Instance.Build();
        }

        private void Build() {
            placement = FindFirstObjectByType<Placement>(FindObjectsInactive.Include);
            category = Mathf.Clamp(PlayerPrefs.GetInt(CategoryKey, 0), 0, Categories.Length - 1);

            // The original's add-parts panel stays (placement still reports to it) but out of sight.
            var buildCanvas = GameObject.Find("Build Canvas");
            var oldMenu = buildCanvas != null ? buildCanvas.transform.Find("Add Parts Menu") : null;
            // The original's view controls (orbit, zoom, pan) move left of the open panel.
            cameraOptions = buildCanvas != null ? buildCanvas.transform.Find("Camera Options") as RectTransform : null;
            if (cameraOptions != null) cameraOptionsHome = cameraOptions.anchoredPosition;
            if (oldMenu != null) {
                var group = oldMenu.gameObject.GetOrAdd<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }

            panel = Ui.Panel(transform, "Panel", Theme.Panel, 10f).rectTransform;
            panel.anchorMin = new Vector2(1f, 0f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 1f);
            panel.sizeDelta = new Vector2(326f, 0f);

            // Header: title, count and fold button.
            var header = Ui.Rect("Header", panel).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(294f, 30f));
            var title = Ui.Text(header, "Title", "Parts", Theme.FontTitle + 2f, Theme.Text, Theme.Medium);
            title.rectTransform.Fill(0, 0, 120, 0);
            countLabel = Ui.Text(header, "Count", "", Theme.FontSmall, Theme.TextFaint, null, TextAlignmentOptions.MidlineRight);
            countLabel.rectTransform.Fill(80, 0, 40, 0);
            var fold = Ui.Button(header, "Fold", ">", () => SetCollapsed(true), false, 6f, Theme.FontBody);
            ((RectTransform)fold.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
            fold.targetGraphic.color = new Color(1f, 1f, 1f, 0f);
            Ui.Tip(fold, "Hide parts");

            search = Ui.Field(panel, "Search", "", null, Theme.FontBody);
            ((RectTransform)search.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -50f), new Vector2(298f, 34f));
            AddPlaceholder(search, "Search parts");
            search.onValueChanged.AddListener(_ => Refresh());

            var chips = Ui.Rect("Categories", panel).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -94f), new Vector2(298f, 58f));
            var chipGrid = chips.gameObject.AddComponent<GridLayoutGroup>();
            chipGrid.cellSize = new Vector2(96f, 26f);
            chipGrid.spacing = new Vector2(5f, 6f);
            for (var i = 0; i < Categories.Length; i++) {
                var index = i;
                var button = Ui.Button(chips, Categories[i], Categories[i], () => SetCategory(index), false, 13f, Theme.FontSmall);
                categoryButtons.Add((button, button.GetComponentInChildren<TextMeshProUGUI>()));
            }

            // The tiles.
            var scrollArea = Ui.Rect("Scroll", panel);
            scrollArea.anchorMin = new Vector2(0f, 0f);
            scrollArea.anchorMax = new Vector2(1f, 1f);
            scrollArea.offsetMin = new Vector2(10f, 10f);
            scrollArea.offsetMax = new Vector2(-10f, -162f);
            var viewport = Ui.Rect("Viewport", scrollArea).Fill();
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);  // scroll anywhere
            grid = Ui.Rect("Grid", viewport);
            grid.anchorMin = new Vector2(0f, 1f);
            grid.anchorMax = new Vector2(1f, 1f);
            grid.pivot = new Vector2(0.5f, 1f);
            grid.sizeDelta = Vector2.zero;
            grid.anchoredPosition = Vector2.zero;
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.cellSize = new Vector2(96f, 92f);
            layout.spacing = new Vector2(5f, 5f);
            layout.padding = new RectOffset(4, 4, 4, 8);
            grid.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = scrollArea.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = grid;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            // Sizes of the chosen part.
            footer = Ui.Panel(panel, "Sizes", Theme.PanelRaised, 8f).rectTransform;
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.anchoredPosition = new Vector2(0f, 10f);
            footer.sizeDelta = new Vector2(-20f, 0f);
            footer.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Column(footer, 8f, new RectOffset(12, 12, 10, 12));
            footer.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            footer.gameObject.SetActive(false);

            // The folded tab.
            var tabButton = Ui.Button(transform, "Parts Tab", "Parts", () => SetCollapsed(false), true, 8f, Theme.FontBody);
            tab = (RectTransform)tabButton.transform;
            tab.anchorMin = tab.anchorMax = new Vector2(1f, 1f);
            tab.pivot = new Vector2(1f, 1f);
            tab.sizeDelta = new Vector2(84f, 34f);
            Ui.Tip(tabButton, "Show parts");

            GameElementPreferences.Changed += Refresh;
            SetCollapsed(PlayerPrefs.GetInt("V2.PartsFolded", 0) == 1);
            SetCategory(category);
        }

        private void OnDestroy() => GameElementPreferences.Changed -= Refresh;

        private static void AddPlaceholder(TMP_InputField field, string text) {
            var placeholder = Ui.Text(field.textViewport, "Placeholder", text, field.pointSize, Theme.TextFaint);
            placeholder.rectTransform.Fill();
            placeholder.fontStyle = FontStyles.Italic;
            field.placeholder = placeholder;
        }

        private void SetCollapsed(bool value) {
            collapsed = value;
            PlayerPrefs.SetInt("V2.PartsFolded", value ? 1 : 0);
            panel.gameObject.SetActive(!value);
            tab.gameObject.SetActive(value);
        }

        private void SetCategory(int index) {
            category = index;
            PlayerPrefs.SetInt(CategoryKey, index);
            for (var i = 0; i < categoryButtons.Count; i++) {
                var on = i == index;
                categoryButtons[i].button.targetGraphic.color = on ? Theme.Accent : Theme.Field;
                categoryButtons[i].label.color = on ? Color.white : Theme.TextDim;
            }
            Refresh();
        }

        private static string CategoryOf(PartType type) {
            if (type.id.StartsWith("VX")) return "VEX library";
            return type.group switch {
                PartType.PartGroup.Structure => "Structure",
                PartType.PartGroup.Motion => "Motion",
                PartType.PartGroup.Electronics => "Electronics",
                _ => "Field",
            };
        }

        private bool waitingForParts;

        private void Refresh() {
            if (grid == null) return;
            // The part types load after the scene wakes up.
            waitingForParts = PartsManager.partTypes == null;
            if (waitingForParts) return;
            foreach (Transform child in grid) Destroy(child.gameObject);
            grid.DetachChildren();
            tiles.Clear();

            var text = search.text.Trim().ToLowerInvariant();
            var parts = PartsManager.partTypes.Where(p => p != null && GameElementPreferences.IsVisible(p));
            if (text.Length > 0)
                parts = parts.Where(p => p.name.ToLowerInvariant().Contains(text) && p.group != PartType.PartGroup.None || text.Length > 2 && p.name.ToLowerInvariant().Contains(text));
            else if (category > 0)
                parts = parts.Where(p => CategoryOf(p) == Categories[category]);
            // The original's parts in its order (the list stacked bottom-up), then the VEX families.
            var list = parts.Reverse().OrderBy(p => p.id.StartsWith("VX")).ToList();
            countLabel.text = list.Count + (list.Count == 1 ? " part" : " parts");
            foreach (var type in list)
                tiles.Add((type, Tile(type)));
            if (list.Count == 0) {
                var none = Ui.Text(grid, "None", "No parts match", Theme.FontBody, Theme.TextFaint, null, TextAlignmentOptions.Center);
                none.rectTransform.sizeDelta = new Vector2(300f, 60f);
            }
            Highlight();
        }

        private Image Tile(PartType type) {
            var tile = Ui.Image(grid, type.name, Theme.Field, 8f, true);
            var hover = tile.gameObject.AddComponent<HoverTint>();
            hover.target = tile;
            var button = tile.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => Choose(type));
            var icon = Ui.Image(tile.transform, "Icon", Theme.Text);
            icon.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(38f, 38f));
            if (type.icon != null) {
                icon.sprite = type.icon;
                icon.preserveAspect = true;
            }
            else {
                icon.color = new Color(1f, 1f, 1f, 0f);
                var letter = Ui.Text(icon.transform, "Letter", type.name.Substring(0, 1), 26f, Theme.TextDim, Theme.Bold, TextAlignmentOptions.Center);
                letter.rectTransform.Fill();
            }
            var label = Ui.Text(tile.transform, "Name", type.name, 12.5f, Theme.Text, Theme.Regular, TextAlignmentOptions.Top);
            label.rectTransform.Fill(5, 54, 5, 2);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.lineSpacing = -12f;
            Ui.Tip(tile, type.name, null, CategoryOf(type));
            return tile;
        }

        private void Highlight() {
            foreach (var (type, tile) in tiles) {
                var on = type == selected;
                tile.GetComponent<HoverTint>().enabled = !on;
                tile.color = on ? Theme.AccentSoft : Theme.Field;
                var outline = tile.GetComponent<Outline>();
                if (on && outline == null) {
                    outline = tile.gameObject.AddComponent<Outline>();
                    outline.effectColor = Theme.Accent;
                    outline.effectDistance = new Vector2(1.5f, -1.5f);
                }
                if (outline != null) outline.enabled = on;
            }
        }

        // ------------------------------------------------------------------ choosing and sizes

        private void Choose(PartType type) {
            selected = type;
            Highlight();
            BuildSizes();
            StartPlacing();
        }

        private PartGenerator Generator => selected != null ? selected.GetComponent<PartGenerator>() : null;

        private void StartPlacing() {
            var generator = Generator;
            if (generator == null || placement == null) return;
            placement.StartPlacing(new PartPlacementData(generator, selected, placement.transform));
        }

        private void BuildSizes() {
            foreach (Transform child in footer)
                if (child.GetSiblingIndex() > 0) Destroy(child.gameObject);
            var generator = Generator;
            if (generator == null) {
                footer.gameObject.SetActive(false);
                return;
            }
            footer.gameObject.SetActive(true);
            var name = Ui.Text(footer, "Name", selected.name, Theme.FontTitle, Theme.Text, Theme.Medium);
            name.Size(-1, 22f);
            if (generator.UsesParams)
                ParamRow(generator.param1, generator.GetParam1Options(), value => {
                    generator.param1.value = value;
                    if (generator.UsesTwoParams) {
                        var options = generator.GetParam2Options();
                        if (!generator.param2.custom && options.Count > 0 && !options.Contains(generator.param2.value))
                            generator.param2.value = options[0];
                    }
                    BuildSizes();
                    StartPlacing();
                });
            if (generator.UsesTwoParams)
                ParamRow(generator.param2, generator.GetParam2Options(), value => {
                    generator.param2.value = value;
                    StartPlacing();
                });
            var hint = Ui.Hint(footer, "Click", "place", "R", "turn", "Esc", "stop");
            hint.Size(-1, 20f);
        }

        private void ParamRow(Parameter parameter, List<string> options, System.Action<string> set) {
            var row = Ui.Rect(parameter.name, footer);
            Ui.Row(row, 8f);
            row.Size(-1, 32f);
            var label = Ui.Text(row, "Label", parameter.name, Theme.FontBody, Theme.TextDim);
            label.Size(64f, 32f);
            if (parameter.custom) {
                if (string.IsNullOrEmpty(parameter.value) || !float.TryParse(parameter.value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    parameter.value = parameter.customDefault;
                var integer = parameter.customUnit == "Holes";
                var minus = Ui.Button(row, "Less", "-", null, false, 5f);
                minus.Size(30f, 30f);
                var field = Ui.Field(row, "Value", parameter.value, null, Theme.FontBody, TextAlignmentOptions.Center);
                field.Size(64f, 30f);
                field.contentType = integer ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.DecimalNumber;
                var plus = Ui.Button(row, "More", "+", null, false, 5f);
                plus.Size(30f, 30f);
                var unit = Ui.Text(row, "Unit", parameter.customUnit, Theme.FontSmall, Theme.TextFaint);
                unit.Size(-1, 30f);
                void Apply(float value) {
                    value = Mathf.Clamp(value, parameter.customLimits.x, parameter.customLimits.y);
                    var text = integer ? Mathf.RoundToInt(value).ToString() : value.ToString("0.###", CultureInfo.InvariantCulture);
                    field.SetTextWithoutNotify(text);
                    set(text);
                }
                float Current() => float.TryParse(field.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
                field.onEndEdit.AddListener(text => {
                    if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) Apply(v);
                    else field.SetTextWithoutNotify(parameter.value);
                });
                var step = integer ? 1f : 0.5f;
                minus.onClick.AddListener(() => Apply(Current() - step));
                plus.onClick.AddListener(() => Apply(Current() + step));
                Ui.Tip(field, parameter.name, null, "From " + parameter.customLimits.x + " to " + parameter.customLimits.y + " " + parameter.customUnit.ToLowerInvariant());
            }
            else {
                var labels = options.ToArray();
                var index = Mathf.Max(0, options.IndexOf(parameter.value));
                if (options.Count > 0 && options.IndexOf(parameter.value) < 0) parameter.value = options[0];
                var choice = Popup.Choice(row, labels, index, i => set(labels[i]), 168f);
                choice.interactable = labels.Length > 1;
            }
        }

        private void Update() {
            if (waitingForParts) Refresh();
            var top = Layout.TopBarBottom + 14f;
            if (!collapsed) {
                panel.offsetMax = new Vector2(-14f, -top);
                panel.offsetMin = new Vector2(-14f - 326f, 14f);
            }
            tab.anchoredPosition = new Vector2(-14f, -top);
            if (cameraOptions != null) {
                // The panel's width in the original canvas's units.
                var shift = collapsed ? 0f : (326f + 14f) * Interface.Root.lossyScale.x / cameraOptions.lossyScale.x;
                cameraOptions.anchoredPosition = cameraOptionsHome - new Vector2(shift, 0f);
            }

            // Placing stopped (Esc, or the original's own reasons): the tile isn't chosen any more.
            if (selected != null && placement != null && !placement.placing) {
                selected = null;
                Highlight();
                footer.gameObject.SetActive(false);
            }
        }
    }
}
