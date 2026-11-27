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
    // V2: the parts menu (replaces the original's add-parts list), laid out like the app's other windows:
    // - Search on top (covers every part), then the categories as pills (Recent, Hardware, Structure, Motion,
    //   Electronics, Pneumatics, Field, Custom).
    // - The chosen category lists its families as rows (a picture, the name, how many parts); click one and it opens
    //   in place with its parts as tiles, three to a row, and its kinds as smaller rows that open in place too
    //   (Spacers > Nylon (3/8" OD) > its sizes). Open families and kinds are remembered. Search and Recent show tiles
    //   straight away. (V2: the two-panel version was tried and taken back out: the user wanted it nested in place.)
    // - Clicking a part starts placing it (click in the build to place, again for another; R turns it, Esc stops); its
    //   sizes show at the bottom. Right-click a custom part to edit or remove it.
    // Parts come from PartsCatalog (its sections and families in the order teams use them).
    public class PartsBrowser : MonoBehaviour {
        private static readonly string[] Categories = { "Recent", "Hardware", "Structure", "Motion", "Electronics", "Pneumatics", "Field", "Custom" };
        private static readonly string[] CategoryIcons = { null, "SCRW", "CCHL", "GEAR", "MOTR", "PNMT", "OVPN", null };
        private const string CategoryKey = "V2.PartsCategory3", FoldedKey = "V2.PartsOpen3";   // V2: open families now
        private const float PanelWidth = 420f, RailWidth = 0f, TileHeight = 104f, PillsHeight = 66f;
        private const int Columns = 3;
        private static readonly Color RailColor = new(0.075f, 0.082f, 0.098f, 1f);

        private RectTransform panel, content, footer, tab, rail;
        private TMP_InputField search;
        private TextMeshProUGUI countLabel;
        private ScrollRect scroll;
        private RectTransform scrollArea;
        private System.Action<int> stepHoles;  // V2: Ctrl+wheel while placing: one hole more or less
        private readonly List<(Button button, Image background, TextMeshProUGUI label)> categoryButtons = new();
        private readonly List<(CatalogNode node, Image tile)> tiles = new();
        private readonly Dictionary<CatalogNode, RectTransform> headings = new();
        private readonly HashSet<string> folded = new();
        private int category;
        private CatalogNode selected;
        private Placement placement;
        private bool collapsed;
        private RectTransform cameraOptions;
        private Vector2 cameraOptionsHome;
        private bool waitingForParts, dirty, railIcons;

        public static PartsBrowser Instance { get; private set; }

        public static void Create(RectTransform root) {
            Instance = Ui.Rect("Parts Browser", root).Fill().gameObject.AddComponent<PartsBrowser>();
            Instance.Build();
        }

        private void Build() {
            placement = FindFirstObjectByType<Placement>(FindObjectsInactive.Include);
            category = Mathf.Clamp(PlayerPrefs.GetInt(CategoryKey, 1), 0, Categories.Length - 1);
            foreach (var key in PlayerPrefs.GetString(FoldedKey, "").Split('|'))
                if (key.Length > 0) folded.Add(key);

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

            // Header: title, count, fold.
            var header = Ui.Rect("Header", panel).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(inner + 2f, 30f));
            var title = Ui.Text(header, "Title", "Parts", Theme.FontTitle + 2f, Theme.Text, Theme.Medium);
            title.rectTransform.Fill(0, 0, 200, 0);
            countLabel = Ui.Text(header, "Count", "", Theme.FontSmall, Theme.TextFaint);
            countLabel.rectTransform.Fill(62, 0, 40, 0);
            var fold = Ui.Button(header, "Fold", ">", () => SetCollapsed(true), false, 6f, Theme.FontBody);
            ((RectTransform)fold.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
            fold.targetGraphic.color = new Color(1f, 1f, 1f, 0f);
            Ui.Tip(fold, "Hide parts");

            search = Ui.Field(panel, "Search", "", null, Theme.FontBody);
            ((RectTransform)search.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -50f), new Vector2(inner, 34f));
            AddPlaceholder(search, "Search every part (spacer 1/4, omni 3.25...)");
            search.onValueChanged.AddListener(_ => Refresh());

            // The categories: two rows of pills under the search box.
            rail = Ui.Rect("Categories", panel).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -92f), new Vector2(inner, PillsHeight));
            var pills = rail.gameObject.AddComponent<GridLayoutGroup>();
            pills.cellSize = new Vector2((inner - 3 * 6f) / 4f, 30f);
            pills.spacing = new Vector2(6f, 6f);
            pills.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            pills.constraintCount = 4;
            for (var i = 0; i < Categories.Length; i++) {
                var index = i;
                var item = Ui.Image(rail, Categories[i], Theme.Field, 15f, true);
                var button = item.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                var nav = button.navigation;
                nav.mode = Navigation.Mode.None;
                button.navigation = nav;
                button.onClick.AddListener(() => SetCategory(index));
                var label = Ui.Text(item.transform, "Label", Categories[i], 12.5f, Theme.TextDim, Theme.Medium, TextAlignmentOptions.Center);
                label.rectTransform.Fill(4, 0, 4, 1);
                label.enableAutoSizing = true;
                label.fontSizeMin = 10f;
                label.fontSizeMax = 12.5f;
                categoryButtons.Add((button, item, label));
                item.gameObject.AddComponent<HoverTint>().target = item;
            }

            // The parts.
            scrollArea = Ui.Rect("Scroll", panel);
            scrollArea.anchorMin = new Vector2(0f, 0f);
            scrollArea.anchorMax = new Vector2(1f, 1f);
            scrollArea.offsetMin = new Vector2(10f, 10f);
            scrollArea.offsetMax = new Vector2(-8f, -(100f + PillsHeight));
            var viewport = Ui.Rect("Viewport", scrollArea).Fill();
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);  // scroll anywhere
            content = Ui.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            Ui.Column(content, 6f, new RectOffset(4, 6, 2, 12));
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = scrollArea.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 34f;

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

        // Shown or folded into its tab: no message (it's only the panel).
        private void SetCollapsed(bool value) {
            collapsed = value;
            PlayerPrefs.SetInt("V2.PartsFolded", value ? 1 : 0);
            panel.gameObject.SetActive(!value);
            tab.gameObject.SetActive(value);
            PartsFlyout.Close();
        }

        private void SetCategory(int index) {
            category = index;
            PlayerPrefs.SetInt(CategoryKey, index);
            for (var i = 0; i < categoryButtons.Count; i++) {
                var on = i == index;
                var (_, background, label) = categoryButtons[i];
                background.GetComponent<HoverTint>().enabled = !on;
                background.color = on ? Theme.Accent : Theme.Field;
                label.color = on ? Color.white : Theme.TextDim;
                var icon = background.transform.Find("Icon")?.GetComponent<Image>();
                if (icon != null && icon.sprite != null) icon.color = on ? Color.white : Theme.TextDim;
                var letter = background.GetComponentsInChildren<TextMeshProUGUI>().FirstOrDefault(t => t.name == "Letter");
                if (letter != null) letter.color = on ? Color.white : Theme.TextDim;
            }
            if (search != null && search.text.Length > 0) search.SetTextWithoutNotify("");
            Refresh();
            if (content != null) content.anchoredPosition = Vector2.zero;
        }

        // ------------------------------------------------------------------ filling

        private bool Visible(CatalogNode node) {
            if (node.action != null || node.customId != null) return true;
            return node.Leaves().Any(l => {
                if (l.action != null) return true;   // V2: bands, tubing, chains (made by their tools)
                var type = PartsCatalog.Type(l.typeId);
                return type != null && GameElementPreferences.IsVisible(type);
            });
        }

        private void Refresh() {
            if (content == null) return;
            // The part types load after the scene wakes up.
            waitingForParts = PartsManager.partTypes == null;
            if (waitingForParts) return;
            railIcons = true;
            PartsFlyout.Close();
            foreach (Transform child in content) Destroy(child.gameObject);
            content.DetachChildren();
            tiles.Clear();
            headings.Clear();

            var nodes = PartsCatalog.Nodes.Where(Visible).ToList();
            var text = search.text.Trim().ToLowerInvariant();
            var shown = 0;
            if (text.Length > 0) {
                // Every part whose name (with its family's) has all the words, under its family's name.
                var words = text.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var node in nodes) {
                    var found = new List<(string path, CatalogNode leaf)>();
                    Collect(node, node.IsGroup ? node.name : "", words, found);
                    if (found.Count == 0) continue;
                    foreach (var group in found.GroupBy(f => f.path)) {
                        Heading(null, group.Key.Length > 0 ? group.Key : node.section, group.Count(), false);
                        Tiles(group.Select(g => g.leaf).ToList());
                    }
                    shown += found.Count;
                    if (shown > 200) break;
                }
                if (shown == 0) Note("No parts match \"" + search.text.Trim() + "\"");
            }
            else if (category == 0) {
                var recent = RecentNodes(nodes);
                Heading(null, "Used lately", recent.Count, false);
                if (recent.Count > 0) Tiles(recent);
                else Note("Parts you place show here, the latest first.");
                shown = recent.Count;
            }
            else {
                var section = Categories[category];
                var list = nodes.Where(n => n.section == section).ToList();
                var families = list.Where(n => n.IsGroup).ToList();
                var loose = list.Where(n => !n.IsGroup).ToList();
                if (loose.Count > 0) {
                    Heading(null, section, loose.Count, false);
                    Tiles(loose);
                }
                foreach (var family in families) Family(family);
                shown = list.Sum(n => n.action != null ? 0 : n.Count);
            }
            countLabel.text = shown + (shown == 1 ? " part" : " parts");
            Highlight();
            dirty = false;
        }

        // The categories' icons: a part each (screw, C-channel, gear, motor, cylinder, pin).
        private void RailIcons() {
            railIcons = true;
            for (var i = 0; i < categoryButtons.Count; i++) {
                if (CategoryIcons[i] == null) continue;
                var icon = categoryButtons[i].background.transform.Find("Icon")?.GetComponent<Image>();
                var sprite = PartsCatalog.Type(CategoryIcons[i])?.icon ?? PartsManager.partTypes.FirstOrDefault(t => t != null && t.id == CategoryIcons[i])?.icon;
                if (icon == null || sprite == null) continue;
                icon.sprite = sprite;
                icon.color = i == category ? Color.white : Theme.TextDim;
            }
        }

        private static void Collect(CatalogNode node, string path, string[] words, List<(string, CatalogNode)> results) {
            if (node.IsGroup) {
                foreach (var child in node.children)
                    Collect(child, child.IsGroup ? (path.Length > 0 ? path + " › " : "") + child.name : path, words, results);
                return;
            }
            var haystack = (path + " " + node.name).ToLowerInvariant().Replace("\"", " ").Replace("›", " ");
            if (words.All(w => haystack.Contains(w.Replace("\"", "")))) results.Add((path, node));
        }

        private List<CatalogNode> RecentNodes(List<CatalogNode> nodes) {
            var leaves = nodes.SelectMany(n => n.Leaves()).ToList();
            var list = new List<CatalogNode>();
            foreach (var id in PartsRecent.All) {
                if (list.Count >= 24) break;
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

        private static string Key(CatalogNode node) => node.section + "/" + node.name;

        // The family names, to jump to one.
        private void JumpRow(List<CatalogNode> families, bool looseFirst) {
            var grid = Ui.Rect("Jump", content);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            var width = (PanelWidth - RailWidth - 40f - 8f) / 3f;
            layout.cellSize = new Vector2(width, 24f);
            layout.spacing = new Vector2(4f, 4f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;
            var rows = (families.Count + 2) / 3;
            grid.Size(-1, rows * 24f + (rows - 1) * 4f);
            foreach (var family in families) {
                var chip = Ui.Button(grid, family.name, family.name, () => JumpTo(family), false, 5f, 11f);
                chip.targetGraphic.color = Theme.Field;
                var label = chip.GetComponentInChildren<TextMeshProUGUI>();
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                Ui.Tip(chip, family.name, null, "Jump to " + family.name + " (" + family.Count + " parts)");
            }
            var gap = Ui.Rect("Gap", content);
            gap.Size(-1, 4f);
        }

        private void JumpTo(CatalogNode family) {
            if (folded.Remove(Key(family))) {
                SaveFolded();
                Refresh();
            }
            if (!headings.TryGetValue(family, out var heading) || heading == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            var y = -heading.anchoredPosition.y - heading.rect.height * (1f - heading.pivot.y);
            var most = Mathf.Max(0f, content.rect.height - scroll.viewport.rect.height);
            content.anchoredPosition = new Vector2(0f, Mathf.Clamp(y, 0f, most));
        }

        private void SaveFolded() => PlayerPrefs.SetString(FoldedKey, string.Join("|", folded));

        // A family: a row with its picture, name and count; click it to open it in place (open ones are remembered).
        private void Family(CatalogNode family) {
            var isOpen = folded.Contains(Key(family));
            var row = Ui.Image(content, "Family " + family.name, isOpen ? Theme.Hover : Theme.PanelRaised, 8f, true);
            var rect = row.rectTransform;
            rect.Size(-1, 50f);
            headings[family] = rect;
            var inner = Ui.Rect("Inner", rect).Fill(8, 0, 12, 0);
            Ui.Row(inner, 10f);
            var picture = Ui.Rect("Picture", inner);
            picture.Size(38f, 38f);
            Icon(picture, family.Leaves().FirstOrDefault() ?? family, 38f);
            var name = Ui.Text(inner, "Name", family.name, Theme.FontBody, Theme.Text, Theme.Medium);
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.Size(-1, 50f, 1f);
            Ui.Text(inner, "Count", family.Count.ToString(), Theme.FontSmall, Theme.TextFaint, null, TextAlignmentOptions.MidlineRight).Size(30f, 50f);
            var chevron = Ui.Image(inner, "Chevron", Theme.TextDim);
            chevron.sprite = Interface.Instance.chevronIcon;
            chevron.preserveAspect = true;
            chevron.Size(10f, 10f);
            chevron.rectTransform.localEulerAngles = new Vector3(0f, 0f, isOpen ? 180f : 270f);   // closed: points right
            if (!isOpen) row.gameObject.AddComponent<HoverTint>().target = row;
            var button = row.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            button.onClick.AddListener(() => {
                var key = Key(family);
                if (!folded.Remove(key)) folded.Add(key);
                SaveFolded();
                var y = content.anchoredPosition.y;
                Refresh();
                content.anchoredPosition = new Vector2(0f, y);
                Audio.Sounds.Play(Audio.Sounds.Tick);
            });
            Ui.Tip(row, family.name, null, family.Count + " parts: click to " + (isOpen ? "close" : "open") + " the family");
            if (!isOpen) return;
            Contents(family, Key(family), 1);
            var gap = Ui.Rect("Gap", content);
            gap.Size(-1, 4f);
        }

        // V2: what an open family or kind holds: its parts as tiles, then its kinds as rows that open in place
        // (Spacers > Nylon (3/8" OD) > its sizes). A group holding only one kind shows that kind's contents at once.
        private void Contents(CatalogNode group, string key, int depth) {
            var children = group.children;
            while (children.Count == 1 && children[0].IsGroup) children = children[0].children;
            var loose = children.Where(c => !c.IsGroup).ToList();
            if (loose.Count > 0) Tiles(loose);
            foreach (var kind in children.Where(c => c.IsGroup)) Kind(kind, key + "/" + kind.name, depth);
        }

        // V2: a kind inside an open family (or inside another kind): a smaller row, indented by how deep it is, that
        // opens in place like the family does (open ones are remembered with the families).
        private void Kind(CatalogNode kind, string key, int depth) {
            var isOpen = folded.Contains(key);
            var row = Ui.Image(content, "Kind " + kind.name, isOpen ? Theme.Hover : Theme.Field, 6f, true);
            var rect = row.rectTransform;
            rect.Size(-1, 36f);
            var inner = Ui.Rect("Inner", rect).Fill(10f + 16f * (depth - 1), 0, 12, 0);
            Ui.Row(inner, 8f);
            var chevron = Ui.Image(inner, "Chevron", Theme.TextDim);
            chevron.sprite = Interface.Instance.chevronIcon;
            chevron.preserveAspect = true;
            chevron.Size(9f, 9f);
            chevron.rectTransform.localEulerAngles = new Vector3(0f, 0f, isOpen ? 180f : 270f);   // closed: points right
            var picture = Ui.Rect("Picture", inner);
            picture.Size(28f, 28f);
            Icon(picture, kind.Leaves().FirstOrDefault() ?? kind, 28f);
            var name = Ui.Text(inner, "Name", kind.name, Theme.FontSmall + 0.5f, Theme.Text, Theme.Medium);
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.Size(-1, 36f, 1f);
            Ui.Text(inner, "Count", kind.Count.ToString(), 11f, Theme.TextFaint, null, TextAlignmentOptions.MidlineRight).Size(30f, 36f);
            if (!isOpen) row.gameObject.AddComponent<HoverTint>().target = row;
            var button = row.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            button.onClick.AddListener(() => {
                if (!folded.Remove(key)) folded.Add(key);
                SaveFolded();
                var y = content.anchoredPosition.y;
                Refresh();
                content.anchoredPosition = new Vector2(0f, y);
                Audio.Sounds.Play(Audio.Sounds.Tick);
            });
            Ui.Tip(row, kind.name, null, kind.Count + " parts: click to " + (isOpen ? "close" : "open") + " them");
            if (isOpen) Contents(kind, key, depth + 1);
        }

        private RectTransform Heading(CatalogNode family, string name, int count, bool foldable, bool isFolded = false) {
            var row = Ui.Image(content, "Heading " + name, foldable ? Theme.Field : new Color(1f, 1f, 1f, 0f), 6f, foldable);
            var rect = row.rectTransform;
            rect.Size(-1, 30f);
            var top = Ui.Rect("Inner", rect).Fill(8, 0, 8, 0);
            Ui.Row(top, 6f);
            if (foldable) {
                var chevron = Ui.Image(top, "Chevron", Theme.TextDim);
                chevron.sprite = Interface.Instance.chevronIcon;
                chevron.preserveAspect = true;
                chevron.Size(10f, 10f);
                chevron.rectTransform.localEulerAngles = new Vector3(0f, 0f, isFolded ? 90f : 180f);
            }
            var title = Ui.Text(top, "Name", name, Theme.FontSmall + 0.5f, Theme.Text, Theme.Medium);
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.Size(-1, 30f, 1f);
            var number = Ui.Text(top, "Count", count.ToString(), 11f, Theme.TextFaint);
            number.Size(-1, 30f);
            if (foldable && family != null) {
                row.gameObject.AddComponent<HoverTint>().target = row;
                var button = row.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                var nav = button.navigation;
                nav.mode = Navigation.Mode.None;
                button.navigation = nav;
                button.onClick.AddListener(() => {
                    var key = Key(family);
                    if (!folded.Remove(key)) folded.Add(key);
                    SaveFolded();
                    var y = content.anchoredPosition.y;
                    Refresh();
                    content.anchoredPosition = new Vector2(0f, y);
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                });
                Ui.Tip(row, family.name, null, count + " parts: click to " + (isFolded ? "show" : "fold") + " them");
            }
            return rect;
        }

        private void Note(string text) {
            var note = Ui.Text(content, "Note", text, Theme.FontSmall, Theme.TextFaint);
            note.textWrappingMode = TextWrappingModes.Normal;
            note.margin = new Vector4(6f, 4f, 6f, 0f);
            note.Size(-1, 40f);
        }

        // Tiles, three to a row, named without the words they all share (1/8" for "1/8" nylon spacer").
        private void Tiles(List<CatalogNode> leaves) {
            var names = Short(leaves.Select(l => l.name).ToList());
            var grid = Ui.Rect("Tiles", content);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            var width = Mathf.Floor((PanelWidth - 28f - 10f - 8f - (Columns - 1) * 6f) / Columns);
            layout.cellSize = new Vector2(width, TileHeight);
            layout.spacing = new Vector2(6f, 6f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = Columns;
            layout.childAlignment = TextAnchor.UpperLeft;
            var rows = (leaves.Count + Columns - 1) / Columns;
            grid.Size(-1, rows * TileHeight + (rows - 1) * 6f + 2f);
            for (var i = 0; i < leaves.Count; i++) tiles.Add((leaves[i], Tile(grid, leaves[i], names[i])));
        }

        // A part's picture (its rendered model, a custom part's thumbnail, or its type's icon), filling `parent`.
        private void Icon(RectTransform parent, CatalogNode node, float size) {
            if (node.customId != null) {
                var part = CustomParts.Find(node.customId);
                var raw = Ui.Rect("Thumbnail", parent).Fill().gameObject.AddComponent<RawImage>();
                raw.raycastTarget = false;
                if (part != null) raw.texture = Thumbnails.Get(part);
                return;
            }
            if (node.action != null && Motion.StrandIcons.For(node.action) is Texture2D drawn) {   // V2: bands, tubing
                var picture = Ui.Rect("Thumbnail", parent).Fill().gameObject.AddComponent<RawImage>();
                picture.raycastTarget = false;
                picture.texture = drawn;
                return;
            }
            var icon = Ui.Image(parent, "Icon", Theme.TextDim);
            icon.rectTransform.Fill(size * 0.12f, size * 0.12f, size * 0.12f, size * 0.12f);
            // V2: the family's Protobot icon; a family without one (VEX-CAD's) gets one made the same way (a flat
            // outline of its first part, square on).
            var sprite = node.typeId != null && !PartsCatalog.IsVex(node.IconType) ? PartsCatalog.Type(node.IconType)?.icon : null;
            if (sprite == null && node.typeId != null) sprite = Thumbnails.IconFor(node.typeId, node.p1, node.p2);
            if (sprite != null) {
                icon.sprite = sprite;
                icon.preserveAspect = true;
            }
            else {
                icon.color = new Color(1f, 1f, 1f, 0f);
                var letter = Ui.Text(icon.transform, "Letter", node.name.Length > 0 ? node.name.Substring(0, 1) : "?", size * 0.5f, Theme.TextDim, Theme.Bold, TextAlignmentOptions.Center);
                letter.rectTransform.Fill();
            }
        }

        private Image Tile(RectTransform grid, CatalogNode node, string shortName) {
            var tile = Ui.Image(grid, node.name, Theme.PanelRaised, 7f, true);
            tile.gameObject.AddComponent<HoverTint>().target = tile;
            var button = tile.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            var rect = tile.rectTransform;
            button.onClick.AddListener(() => Choose(node));

            if (node.action == "import") {
                var plus = Ui.Text(tile.transform, "Plus", "+", 44f, Theme.Accent, Theme.Regular, TextAlignmentOptions.Center);
                plus.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(60f, 56f));
            }
            else if (node.customId != null) {
                var part = CustomParts.Find(node.customId);
                var image = Ui.Rect("Thumbnail", tile.transform).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(58f, 58f))
                    .gameObject.AddComponent<RawImage>();
                image.raycastTarget = false;
                if (part != null) image.texture = Thumbnails.Get(part);
                tile.gameObject.AddComponent<RightClick>().onRightClick = () => CustomMenu(node, rect);
            }
            else if (PartsCatalog.ModelIcon(node) is Texture2D model) {
                var image = Ui.Rect("Thumbnail", tile.transform).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(58f, 58f))
                    .gameObject.AddComponent<RawImage>();
                image.raycastTarget = false;
                image.texture = model;
            }
            else if (node.typeId != null && node.action == null && Thumbnails.ForPart(node.typeId, node.p1, node.p2) is Texture2D picture) {
                // V2: every part's tile is a picture of the part itself.
                var image = Ui.Rect("Thumbnail", tile.transform).Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(58f, 58f))
                    .gameObject.AddComponent<RawImage>();
                image.raycastTarget = false;
                image.texture = picture;
            }
            else {
                var icon = Ui.Image(tile.transform, "Icon", Theme.TextDim);
                icon.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(44f, 44f));
                var sprite = node.action == "polycarb" ? Interface.Instance.polycarbIcon : PartsCatalog.Type(node.IconType)?.icon;
                if (node.action == "library") {
                    icon.color = new Color(1f, 1f, 1f, 0f);
                    for (var i = 2; i >= 0; i--) {
                        var card = Ui.Image(icon.transform, "Card", i == 0 ? Theme.Accent : Color.Lerp(Theme.TextDim, Theme.Field, i * 0.3f), 3f);
                        card.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(i * 3f - 3f, 3f - i * 3f), new Vector2(18f, 22f));
                    }
                }
                else if (sprite != null) {
                    icon.sprite = sprite;
                    icon.preserveAspect = true;
                    if (node.action == "polycarb") icon.color = new Color(0.7f, 0.85f, 1f, 1f);
                }
                else {
                    icon.color = new Color(1f, 1f, 1f, 0f);
                    var letter = Ui.Text(icon.transform, "Letter", node.name.Substring(0, 1), 20f, Theme.TextDim, Theme.Bold, TextAlignmentOptions.Center);
                    letter.rectTransform.Fill();
                }
            }

            var label = Ui.Text(tile.transform, "Name", shortName, 12f, Theme.Text, Theme.Medium, TextAlignmentOptions.Top);
            label.rectTransform.Fill(5, 68, 5, 2);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.lineSpacing = -18f;
            Ui.Tip(tile, node.name, null, node.tip ?? "Click to place it; it stays picked, so click in the build again for another");
            return tile;
        }

        // Names without the words they all share at the start and the end (or most of them share at the end: one odd
        // part among sizes keeps its full name, the sizes still lose "omni wheel").
        private static List<string> Short(List<string> names) {
            if (names.Count < 2) return names;
            var words = names.Select(n => n.Split(' ')).ToList();
            var ends = words.Where(w => w.Length >= 3).GroupBy(w => w[w.Length - 2] + " " + w[w.Length - 1]).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (ends != null && ends.Count() >= 3 && ends.Count() >= names.Count * 0.6f && ends.Count() < names.Count)
                return words.Select(w => w.Length >= 3 && w[w.Length - 2] + " " + w[w.Length - 1] == ends.Key
                    ? string.Join(" ", w.Take(w.Length - 2)) : string.Join(" ", w)).ToList();
            var front = 0;
            while (words.All(w => w.Length > front + 1) && words.All(w => w[front] == words[0][front])) front++;
            var back = 0;
            while (words.All(w => w.Length > front + back + 1) && words.All(w => w[w.Length - 1 - back] == words[0][words[0].Length - 1 - back])) back++;
            return words.Select(w => string.Join(" ", w.Skip(front).Take(w.Length - front - back))).ToList();
        }

        private void CustomMenu(CatalogNode node, RectTransform anchor) {
            var part = CustomParts.Find(node.customId);
            if (part == null) return;
            var options = new List<string> { "Place" };
            if (part.kind == CustomParts.Polycarbonate) options.Add("Edit sketch");
            else options.Add("Edit holes");
            options.Add("Delete from library");
            options.Add("Manage library…");
            Popup.Open(anchor, options.ToArray(), -1, i => {
                switch (options[i]) {
                    case "Place": Choose(node); break;
                    case "Edit sketch": Polycarb.SheetEditor.Edit(part); break;
                    case "Edit holes": ImportDialog.Edit(part); break;
                    case "Manage library…": LibraryDialog.Open(); break;
                    default:
                        // The file moves to the library's Deleted folder (File > Custom parts library restores it).
                        var used = PartsManager.FindLoadedObjects().Any(o => o.GetComponent<SavedObject>().id == part.Id && !o.IsDeleted());
                        if (!CustomParts.Remove(part.key)) {
                            Interface.Say("Couldn't delete " + part.name + " (its file may be open somewhere else)", false);
                            break;
                        }
                        Thumbnails.Forget(part.key);
                        Interface.Say("Deleted " + part.name + " from your library" + (used ? " (the copies in this build stay)" : "") + ": File > Custom parts library can restore it");
                        break;
                }
            });
        }

        private void Highlight() {
            foreach (var (node, tile) in tiles) {
                if (tile == null) continue;
                var on = selected != null && (node == selected
                                              || (node.typeId == selected.typeId && node.p1 == selected.p1 && node.p2 == selected.p2 && node.customId == selected.customId && node.action == selected.action));
                tile.GetComponent<HoverTint>().enabled = !on;
                tile.color = on ? Theme.AccentSoft : Theme.PanelRaised;
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
            if (node.action == "library") {
                LibraryDialog.Open();
                return;
            }
            if (node.action != null && node.action.StartsWith("group:")) {   // V2: a saved group
                Builds.SavedGroups.Place(node.action.Substring("group:".Length));
                return;
            }
            if (node.action == "chain") {
                Motion.ChainTool.Begin();
                return;
            }
            if (node.action != null && node.action.StartsWith("wire:")) {   // V2: a cable, routed port to port
                Motion.TubeTool.BeginWire(node.action == "wire:3W" ? Motion.WireKind.ThreeWire : Motion.WireKind.Smart);
                return;
            }
            if (node.action == "ptube") {   // V2: pneumatic tubing, routed
                Motion.TubeTool.Begin();
                return;
            }
            if (node.action != null && node.action.StartsWith("band:")) {   // V2: a band round its posts
                Motion.ElasticTool.Begin(node.action.Substring("band:".Length));
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
            // Metal starts as full-length stock (the saw cuts it to length); Ctrl+wheel or the field change it.
            if (generator is AluminumPartGenerator) {
                if (generator.UsesParams && generator.param1.custom && generator.param1.customUnit == "Holes")
                    generator.param1.value = ((int)generator.param1.customLimits.y).ToString();
                if (generator.UsesTwoParams && generator.param2.custom && generator.param2.customUnit == "Holes")
                    generator.param2.value = ((int)generator.param2.customLimits.y).ToString();
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
            var node = PartsCatalog.Nodes.SelectMany(n => n.Leaves()).FirstOrDefault(n => n.customId == part.Id);
            if (node != null) Choose(node);
        }

        private PartType SelectedType => selected != null ? PartsCatalog.Type(selected.typeId) : null;
        private PartGenerator Generator => SelectedType != null ? SelectedType.GetComponent<PartGenerator>() : null;

        private void StartPlacing() {
            var generator = Generator;
            if (generator == null || placement == null) return;
            placement.StartPlacing(new PartPlacementData(generator, SelectedType, placement.transform));
        }

        private static bool CtrlHeld() {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
        }

        private void BuildSizes() {
            stepHoles = null;
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
            // A VEX-CAD family's one parameter is which part it is: chosen already.
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
            var holes = stepHoles != null;
            // V2: Ctrl+wheel steps through the part's sizes and kinds when it has no hole count (a screw's lengths; a flex
            // wheel's diameters and hardnesses: the parts listed with it in its family).
            if (!holes) stepHoles = StepSizes(generator, vex);
            if (holes)
                Ui.Hint(footer, "Ctrl+Wheel", "more or fewer holes", "T", "reset turn").Size(-1, 20f);
            else if (stepHoles != null)
                Ui.Hint(footer, "Ctrl+Wheel", "size or kind", "T", "reset turn").Size(-1, 20f);
            else
                Ui.Hint(footer, "Ctrl", "slide freely on a screw", "T", "reset turn").Size(-1, 20f);
        }

        private System.Action<int> StepSizes(PartGenerator generator, bool vex) {
            // The parts listed with it first (sizes and kinds as the list shows them), else its own options.
            var family = FamilyOf(selected);
            if (family.Count >= 2)
                return direction => {
                    var i = Mathf.Max(0, family.IndexOf(selected));
                    Choose(family[(i + direction + family.Count) % family.Count]);
                };
            if (!vex && generator.UsesParams && !generator.param1.custom) {
                var options = generator.GetParam1Options();
                if (options.Count > 1)
                    return direction => {
                        var i = Mathf.Max(0, options.IndexOf(generator.param1.value));
                        generator.param1.value = options[(i + direction + options.Count) % options.Count];
                        if (generator.UsesTwoParams) {
                            var second = generator.GetParam2Options();
                            if (!generator.param2.custom && second.Count > 0 && !second.Contains(generator.param2.value)) generator.param2.value = second[0];
                        }
                        BuildSizes();
                        StartPlacing();
                    };
            }
            return null;
        }

        // The parts listed with this one, of its family: its group's, or the group above when that holds more of the
        // family (flex wheels: one group per diameter, by hardness, under "Flex wheels").
        private static List<CatalogNode> FamilyOf(CatalogNode leaf) {
            var path = new List<CatalogNode>();
            bool Find(CatalogNode node) {
                path.Add(node);
                if (node == leaf) return true;
                if (node.IsGroup)
                    foreach (var child in node.children)
                        if (Find(child)) return true;
                path.RemoveAt(path.Count - 1);
                return false;
            }
            if (leaf == null || leaf.typeId == null) return new List<CatalogNode>();
            foreach (var root in PartsCatalog.Nodes)
                if (Find(root)) break;
            if (path.Count < 2 || !path[path.Count - 2].IsGroup) return new List<CatalogNode>();
            var parent = path[path.Count - 2];
            var family = parent.Leaves().Where(l => l.typeId != null && l.action == null).Distinct().ToList();
            // The group above when the family fills it in groups of two or more (flex wheels: a group per diameter, by
            // hardness): then Ctrl+wheel goes through every size and hardness. Not a whole kit (the advanced gears).
            if (path.Count >= 3 && path[path.Count - 3].IsGroup) {
                var grand = path[path.Count - 3];
                var groups = grand.children.Where(c => c.IsGroup && c.Leaves().Any(l => l.typeId == leaf.typeId)).ToList();
                var all = grand.Leaves().Where(l => l.typeId == leaf.typeId && l.action == null).Distinct().ToList();
                if (groups.Count >= 2 && groups.All(g => g.Leaves().Count(l => l.typeId == leaf.typeId) >= 2)
                    && all.All(l => groups.Any(g => g.Leaves().Contains(l))) && all.Count <= 40)
                    family = all;
            }
            return family;
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
                var wheel = "From " + parameter.customLimits.x + " to " + parameter.customLimits.y + " " + parameter.customUnit.ToLowerInvariant()
                            + ". Ctrl + mouse wheel changes it while you place the part.";
                Ui.Tip(field, parameter.name, "Ctrl+Wheel", wheel);
                Ui.Tip(minus, "Fewer " + parameter.customUnit.ToLowerInvariant(), "Ctrl+Wheel", wheel);
                Ui.Tip(plus, "More " + parameter.customUnit.ToLowerInvariant(), "Ctrl+Wheel", wheel);
                stepHoles = direction => {
                    Apply(Current() + direction * step);
                    Interface.Say(parameter.name + ": " + field.text + " " + parameter.customUnit.ToLowerInvariant());
                };
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
            // Ctrl+wheel while placing a part with a hole count changes it (the camera doesn't zoom then).
            if (MousePivotCameraInput.ScrollClaimed == null)
                MousePivotCameraInput.ScrollClaimed = () => (stepHoles != null && placement != null && placement.placing && CtrlHeld()) || Tools.QuickPlace.ClaimsWheel;
            if (stepHoles != null && placement != null && placement.placing && CtrlHeld() && !MouseInput.overUI && !Tools.QuickPlace.ClaimsWheel) {
                var wheel = MouseInput.scrollAxis;
                if (wheel != 0f) stepHoles(wheel > 0f ? 1 : -1);
            }
            // The size panel sits under the list, not over its last rows.
            if (scrollArea != null) {
                var bottom = footer.gameObject.activeSelf ? footer.rect.height + 18f : 10f;
                if (!Mathf.Approximately(scrollArea.offsetMin.y, bottom)) {
                    scrollArea.offsetMin = new Vector2(scrollArea.offsetMin.x, bottom);
                }
            }
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
