using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Custom;
using Protobot.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: the parts browser (replaces the original's add-parts list). Parts are in sections in the order teams
    // use them (PartsCatalog: hardware first, then structure, motion, electronics, pneumatics, field elements and
    // your own parts), most common first. A tile with a corner badge is a family: clicking it opens a menu of its
    // parts, and kinds open further menus beside it (Wheels > Omni wheels > 4"). Sort by Common, A-Z or Recent;
    // search finds any part by name. Choosing a part starts placing it, as the original did (click to place, R
    // to turn it, Esc to stop), and its sizes show at the bottom (a C-channel's hole count...). The Custom
    // section has "Import part" (+) and "Polycarbonate"; right-click a custom part to remove or edit it.
    public class PartsBrowser : MonoBehaviour {
        private static readonly string[] Categories = { "All", "Hardware", "Structure", "Motion", "Electronics", "Pneumatics", "Field", "Custom" };
        private static readonly string[] Sorts = { "Common", "A-Z", "Recent" };
        private const string CategoryKey = "V2.PartsCategory2", SortKey = "V2.PartsSort";
        private const float PanelWidth = 340f;

        private RectTransform panel, content, footer, tab;
        private TMP_InputField search;
        private TextMeshProUGUI countLabel;
        private ScrollRect scroll;
        private readonly List<(Button button, TextMeshProUGUI label)> categoryButtons = new();
        private readonly List<(CatalogNode node, Image tile)> tiles = new();
        private int category, sort;
        private CatalogNode selected;
        private Placement placement;
        private bool collapsed;
        private RectTransform cameraOptions;
        private Vector2 cameraOptionsHome;
        private bool waitingForParts, dirty;

        public static PartsBrowser Instance { get; private set; }

        public static void Create(RectTransform root) {
            Instance = Ui.Rect("Parts Browser", root).Fill().gameObject.AddComponent<PartsBrowser>();
            Instance.Build();
        }

        private void Build() {
            placement = FindFirstObjectByType<Placement>(FindObjectsInactive.Include);
            category = Mathf.Clamp(PlayerPrefs.GetInt(CategoryKey, 0), 0, Categories.Length - 1);
            sort = Mathf.Clamp(PlayerPrefs.GetInt(SortKey, 0), 0, Sorts.Length - 1);

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
            panel.sizeDelta = new Vector2(PanelWidth, 0f);
            var inner = PanelWidth - 28f;

            // Header: title, count, sort and fold.
            var header = Ui.Rect("Header", panel).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(inner + 2f, 30f));
            var title = Ui.Text(header, "Title", "Parts", Theme.FontTitle + 2f, Theme.Text, Theme.Medium);
            title.rectTransform.Fill(0, 0, 200, 0);
            countLabel = Ui.Text(header, "Count", "", Theme.FontSmall, Theme.TextFaint);
            countLabel.rectTransform.Fill(62, 0, 154, 0);
            var sortButton = Popup.Choice(header, Sorts, sort, i => {
                sort = i;
                PlayerPrefs.SetInt(SortKey, i);
                Refresh();
            }, 112f);
            ((RectTransform)sortButton.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-36f, 0f), new Vector2(112f, 28f));
            Ui.Tip(sortButton, "Sort", null, "Common: the parts teams use most first. A-Z: by name. Recent: what you used lately first.");
            var fold = Ui.Button(header, "Fold", ">", () => SetCollapsed(true), false, 6f, Theme.FontBody);
            ((RectTransform)fold.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
            fold.targetGraphic.color = new Color(1f, 1f, 1f, 0f);
            Ui.Tip(fold, "Hide parts");

            search = Ui.Field(panel, "Search", "", null, Theme.FontBody);
            ((RectTransform)search.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -50f), new Vector2(inner, 34f));
            AddPlaceholder(search, "Search all parts");
            search.onValueChanged.AddListener(_ => Refresh());

            var chips = Ui.Rect("Categories", panel).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -94f), new Vector2(inner, 58f));
            var chipGrid = chips.gameObject.AddComponent<GridLayoutGroup>();
            chipGrid.cellSize = new Vector2((inner - 15f) / 4f, 26f);
            chipGrid.spacing = new Vector2(5f, 6f);
            for (var i = 0; i < Categories.Length; i++) {
                var index = i;
                var button = Ui.Button(chips, Categories[i], Categories[i], () => SetCategory(index), false, 13f, 12.5f);
                categoryButtons.Add((button, button.GetComponentInChildren<TextMeshProUGUI>()));
            }

            // The sections and their tiles.
            var scrollArea = Ui.Rect("Scroll", panel);
            scrollArea.anchorMin = new Vector2(0f, 0f);
            scrollArea.anchorMax = new Vector2(1f, 1f);
            scrollArea.offsetMin = new Vector2(10f, 10f);
            scrollArea.offsetMax = new Vector2(-10f, -162f);
            var viewport = Ui.Rect("Viewport", scrollArea).Fill();
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);  // scroll anywhere
            content = Ui.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            Ui.Column(content, 4f, new RectOffset(4, 4, 2, 10));
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = scrollArea.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
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
            CustomParts.Changed += OnCustomPartsChanged;
            SetCollapsed(PlayerPrefs.GetInt("V2.PartsFolded", 0) == 1);
            SetCategory(category);
        }

        private void OnDestroy() {
            GameElementPreferences.Changed -= Refresh;
            CustomParts.Changed -= OnCustomPartsChanged;
        }

        private void OnCustomPartsChanged() {
            PartsCatalog.Invalidate();
            dirty = true;
        }

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
            if (value) PartsFlyout.Close();
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

        // ------------------------------------------------------------------ filling

        private bool Visible(CatalogNode node) {
            if (node.action != null || node.customId != null) return true;
            return node.Leaves().Any(l => {
                var type = PartsCatalog.Type(l.typeId);
                return type != null && GameElementPreferences.IsVisible(type);
            });
        }

        private void Refresh() {
            if (content == null) return;
            // The part types load after the scene wakes up.
            waitingForParts = PartsManager.partTypes == null;
            if (waitingForParts) return;
            PartsFlyout.Close();
            foreach (Transform child in content) Destroy(child.gameObject);
            content.DetachChildren();
            tiles.Clear();

            var nodes = PartsCatalog.Nodes.Where(Visible).ToList();
            var text = search.text.Trim().ToLowerInvariant();
            var shown = 0;
            if (text.Length > 0) {
                var words = text.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                var results = new List<CatalogNode>();
                foreach (var node in nodes)
                    Collect(node, node.IsGroup ? node.name : "", words, results);
                results = results.Take(150).ToList();
                if (results.Count > 0) Section("Results", results);
                shown = results.Count;
            }
            else {
                if (sort == 2) {
                    var recent = RecentNodes(nodes);
                    if (recent.Count > 0 && (category == 0 || category == 7 || recent.Any(r => r.section == Categories[category]))) {
                        var inCategory = category == 0 ? recent : recent.Where(r => r.section == Categories[category]).ToList();
                        if (inCategory.Count > 0) Section("Recently used", inCategory);
                    }
                }
                foreach (var name in PartsCatalog.Sections) {
                    if (category > 0 && Categories[category] != name) continue;
                    var list = nodes.Where(n => n.section == name).ToList();
                    if (sort == 1) list = list.OrderBy(n => n.action != null ? 0 : 1).ThenBy(n => n.name).ToList();
                    if (list.Count == 0) continue;
                    Section(name, list);
                    shown += list.Sum(n => n.action != null ? 0 : n.Count);
                }
            }
            countLabel.text = shown + (shown == 1 ? " part" : " parts");
            if (tiles.Count == 0) {
                var none = Ui.Text(content, "None", "No parts match", Theme.FontBody, Theme.TextFaint, null, TextAlignmentOptions.Center);
                none.Size(-1, 60f);
            }
            Highlight();
            dirty = false;
        }

        private static void Collect(CatalogNode node, string path, string[] words, List<CatalogNode> results) {
            if (node.IsGroup) {
                foreach (var child in node.children)
                    Collect(child, child.IsGroup ? path + " " + child.name : path, words, results);
                return;
            }
            var haystack = (path + " " + node.name).ToLowerInvariant();
            if (words.All(w => haystack.Contains(w))) results.Add(node);
        }

        private List<CatalogNode> RecentNodes(List<CatalogNode> nodes) {
            var leaves = nodes.SelectMany(n => n.Leaves()).ToList();
            var list = new List<CatalogNode>();
            foreach (var id in PartsRecent.All) {
                if (list.Count >= 9) break;
                var custom = leaves.FirstOrDefault(l => l.customId == id);
                if (custom != null) {
                    list.Add(custom);
                    continue;
                }
                var split = id.Split('-');
                var p1 = split.Length > 1 ? split[1] : null;
                var p2 = split.Length > 2 ? split[2] : null;
                var match = leaves.FirstOrDefault(l => l.typeId == split[0] && l.p1 == p1 && l.p2 == p2)
                            ?? leaves.FirstOrDefault(l => l.typeId == split[0] && l.p1 == p1)
                            ?? leaves.FirstOrDefault(l => l.typeId == split[0]);
                if (match == null) continue;
                var name = match.name;
                var type = PartsCatalog.Type(split[0]);
                var generator = type != null ? type.GetComponent<PartGenerator>() : null;
                // Custom sizes (hole counts, lengths) in the name.
                if (generator != null && generator.UsesTwoParams && generator.param2.custom && p2 != null)
                    name += " (" + p2 + (generator.param2.customUnit == "Holes" ? "" : "\"") + ")";
                list.Add(new CatalogNode { section = match.section, name = name, typeId = match.typeId, p1 = p1, p2 = p2, tip = match.tip });
            }
            return list;
        }

        private void Section(string name, List<CatalogNode> nodes) {
            var heading = Ui.Text(content, "Section " + name, name.ToUpperInvariant(), 12f, Theme.TextFaint, Theme.Medium);
            heading.characterSpacing = 6f;
            heading.margin = new Vector4(4f, 8f, 0f, 0f);
            heading.Size(-1, 28f);
            var grid = Ui.Rect("Grid " + name, content);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(98f, 94f);
            layout.spacing = new Vector2(6f, 6f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;
            layout.childAlignment = TextAnchor.UpperLeft;
            foreach (var node in nodes)
                tiles.Add((node, Tile(grid, node)));
        }

        private Image Tile(RectTransform grid, CatalogNode node) {
            var tile = Ui.Image(grid, node.name, Theme.Field, 8f, true);
            var hover = tile.gameObject.AddComponent<HoverTint>();
            hover.target = tile;
            var button = tile.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            var rect = tile.rectTransform;
            button.onClick.AddListener(() => {
                if (node.IsGroup) PartsFlyout.Toggle(node, rect, Choose, panel);
                else Choose(node);
            });

            if (node.action == "import") {
                var plus = Ui.Text(tile.transform, "Plus", "+", 40f, Theme.Accent, Theme.Regular, TextAlignmentOptions.Center);
                plus.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(50f, 50f));
                Dashed(tile.rectTransform);
            }
            else if (node.customId != null) {
                var part = CustomParts.Find(node.customId);
                var image = Ui.Rect("Thumbnail", tile.transform).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(52f, 52f))
                    .gameObject.AddComponent<RawImage>();
                image.raycastTarget = false;
                if (part != null) image.texture = Thumbnails.Get(part);
                tile.gameObject.AddComponent<RightClick>().onRightClick = () => CustomMenu(node, rect);
            }
            else {
                var icon = Ui.Image(tile.transform, "Icon", Theme.Text);
                icon.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(40f, 40f));
                var sprite = node.action == "polycarb" ? Interface.Instance.polycarbIcon : PartsCatalog.Type(node.IconType)?.icon;
                if (sprite != null) {
                    icon.sprite = sprite;
                    icon.preserveAspect = true;
                    if (node.action == "polycarb") icon.color = new Color(0.7f, 0.85f, 1f, 1f);
                }
                else {
                    icon.color = new Color(1f, 1f, 1f, 0f);
                    var letter = Ui.Text(icon.transform, "Letter", node.name.Substring(0, 1), 26f, Theme.TextDim, Theme.Bold, TextAlignmentOptions.Center);
                    letter.rectTransform.Fill();
                }
            }

            var label = Ui.Text(tile.transform, "Name", node.name, 12.5f, Theme.Text, Theme.Regular, TextAlignmentOptions.Top);
            label.rectTransform.Fill(5, 58, 5, 2);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.lineSpacing = -12f;

            if (node.IsGroup) {
                // The family badge: a corner chevron and how many parts are in it.
                var badge = Ui.Image(tile.transform, "Badge", Theme.Hover, 5f);
                badge.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-4f, -4f), new Vector2(22f, 18f));
                var chevron = Ui.Image(badge.transform, "Chevron", Theme.Text);
                chevron.sprite = Interface.Instance.chevronIcon;
                chevron.preserveAspect = true;
                chevron.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(9f, 9f));
                chevron.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);
                var count = Ui.Text(tile.transform, "Count", node.Count.ToString(), 10.5f, Theme.TextFaint, Theme.Medium, TextAlignmentOptions.TopLeft);
                count.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(6f, -4f), new Vector2(30f, 16f));
                Ui.Tip(tile, node.name, null, node.Count + " parts: click to choose one");
            }
            else
                Ui.Tip(tile, node.name, null, node.tip ?? node.section);
            return tile;
        }

        // A dashed outline (the "+" tile).
        private static void Dashed(RectTransform tile) {
            const float dash = 7f, gap = 5f, inset = 3f;
            var size = new Vector2(98f, 94f);
            for (var x = inset; x < size.x - inset; x += dash + gap) {
                var w = Mathf.Min(dash, size.x - inset - x);
                Ui.Image(tile, "Dash", Theme.TextFaint).rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -inset), new Vector2(w, 1.5f));
                Ui.Image(tile, "Dash", Theme.TextFaint).rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x, inset), new Vector2(w, 1.5f));
            }
            for (var y = inset; y < size.y - inset; y += dash + gap) {
                var h = Mathf.Min(dash, size.y - inset - y);
                Ui.Image(tile, "Dash", Theme.TextFaint).rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(inset, y), new Vector2(1.5f, h));
                Ui.Image(tile, "Dash", Theme.TextFaint).rectTransform.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-inset, y), new Vector2(1.5f, h));
            }
        }

        private void CustomMenu(CatalogNode node, RectTransform anchor) {
            var part = CustomParts.Find(node.customId);
            if (part == null) return;
            var options = new List<string> { "Place" };
            if (part.kind == CustomParts.Polycarbonate) options.Add("Edit sketch");
            options.Add("Remove from library");
            Popup.Open(anchor, options.ToArray(), -1, i => {
                switch (options[i]) {
                    case "Place": Choose(node); break;
                    case "Edit sketch": Polycarb.SheetEditor.Edit(part); break;
                    default:
                        var used = PartsManager.FindLoadedObjects().Any(o => o.GetComponent<SavedObject>().id == part.Id && !o.IsDeleted());
                        CustomParts.Remove(part.key);
                        Thumbnails.Forget(part.key);
                        Interface.Say(used ? "Removed " + part.name + " from your library (the copies in this build stay)" : "Removed " + part.name + " from your library", false);
                        break;
                }
            });
        }

        private void Highlight() {
            foreach (var (node, tile) in tiles) {
                if (tile == null) continue;
                var on = selected != null && (node == selected || (node.IsGroup && node.Leaves().Contains(selected))
                                              || (!node.IsGroup && node.typeId == selected.typeId && node.p1 == selected.p1 && node.p2 == selected.p2 && node.customId == selected.customId));
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

        private void Choose(CatalogNode node) {
            PartsFlyout.Close();
            if (node.action == "import") {
                ImportDialog.Choose();
                return;
            }
            if (node.action == "polycarb") {
                Polycarb.SheetEditor.Open();
                return;
            }
            var type = PartsCatalog.Type(node.typeId);
            var generator = type != null ? type.GetComponent<PartGenerator>() : null;
            if (generator == null) return;
            if (generator.UsesParams && node.p1 != null) generator.param1.value = node.p1;
            if (generator.UsesTwoParams) {
                if (node.p2 != null) generator.param2.value = node.p2;
                else if (!generator.param2.custom) {
                    var options = generator.GetParam2Options();
                    if (options != null && options.Count > 0 && !options.Contains(generator.param2.value)) generator.param2.value = options[0];
                }
            }
            selected = node;
            Highlight();
            BuildSizes();
            StartPlacing();
        }

        /// <summary>Starts placing a custom part (after importing or cutting it).</summary>
        public void PlaceCustom(CustomPartData part) {
            PartsCatalog.Invalidate();
            Refresh();
            var node = PartsCatalog.Nodes.FirstOrDefault(n => n.customId == part.Id);
            if (node != null) Choose(node);
        }

        private PartType SelectedType => selected != null ? PartsCatalog.Type(selected.typeId) : null;
        private PartGenerator Generator => SelectedType != null ? SelectedType.GetComponent<PartGenerator>() : null;

        private void StartPlacing() {
            var generator = Generator;
            if (generator == null || placement == null) return;
            placement.StartPlacing(new PartPlacementData(generator, SelectedType, placement.transform));
            PartsRecent.Use(generator.GetId());
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
            // A VEX-CAD family's one parameter is which part it is: chosen in the menu already.
            var vex = selected.typeId.StartsWith("VX") || generator is CustomPartGenerator;
            if (generator.UsesParams && !vex)
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
            if (generator.UsesTwoParams && !vex)
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
                var shownLabels = labels.Select(PartsCatalog.Pretty).ToArray();
                var index = Mathf.Max(0, options.IndexOf(parameter.value));
                if (options.Count > 0 && options.IndexOf(parameter.value) < 0) parameter.value = options[0];
                var choice = Popup.Choice(row, shownLabels, index, i => set(labels[i]), 180f);
                choice.interactable = labels.Length > 1;
            }
        }

        private void Update() {
            if (waitingForParts || dirty) Refresh();
            var top = Layout.TopBarBottom + 14f;
            if (!collapsed) {
                panel.offsetMax = new Vector2(-14f, -top);
                panel.offsetMin = new Vector2(-14f - PanelWidth, 14f);
            }
            tab.anchoredPosition = new Vector2(-14f, -top);
            if (cameraOptions != null) {
                // The panel's width in the original canvas's units.
                var shift = collapsed ? 0f : (PanelWidth + 14f) * Interface.Root.lossyScale.x / cameraOptions.lossyScale.x;
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

    // A right click on an element.
    public class RightClick : MonoBehaviour, IPointerClickHandler {
        public System.Action onRightClick;

        public void OnPointerClick(PointerEventData eventData) {
            if (eventData.button == PointerEventData.InputButton.Right) onRightClick?.Invoke();
        }
    }
}
