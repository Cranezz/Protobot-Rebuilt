using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Builds;
using Protobot.InputEvents;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: the top bar in the new interface's style (replaces the original's, which stays hidden and still does
    // the work: these buttons press its buttons and toggles, so saving, preferences, undo and the view toggles
    // behave as before). Menus (File, Edit, Plastic, Help) open like a desktop app's: once one is open, moving
    // over another opens that one. In the middle the build's name; on the right the plastic used, the estimated
    // weight and full screen.
    public class TopBar : MonoBehaviour {
        public const float Height = 44f;
        public static TopBar Instance { get; private set; }

        private Transform old;
        private RectTransform bar, openMenu;
        private string openName;
        private readonly Dictionary<string, RectTransform> menuButtons = new();
        private TextMeshProUGUI title, weight, plastic;
        private Button simulate;  // V2
        private TMP_Text oldTitle, oldWeight;
        private Image plasticChip;
        private readonly List<(Toggle source, Image background, Image icon)> toggles = new();
        private BuildsManager builds;
        private int plasticFrame;

        private struct Item {
            public string label, shortcut;
            public Action action;
            public bool divider;
        }

        public static void Create(RectTransform root) {
            Instance = Ui.Rect("Top Bar", root).Fill().gameObject.AddComponent<TopBar>();
            Instance.Build();
        }

        private Button OldButton(string path) {
            var t = old != null ? old.Find(path) : null;
            return t != null ? t.GetComponent<Button>() : null;
        }

        private Toggle OldToggle(string path) {
            var t = old != null ? old.Find(path) : null;
            return t != null ? t.GetComponent<Toggle>() : null;
        }

        private static void Press(Button button) {
            if (button != null) button.onClick.Invoke();
        }

        private static string Key(string inputEvent) {
            var e = FindObjectsByType<InputEvent>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(i => i.name == inputEvent);
            if (e == null) return "";
            try {
                return e.CurrentKeysText;   // V2: after a change in Edit > Keyboard shortcuts
            }
            catch {
                return "";
            }
        }

        private void Build() {
            var buildCanvas = GameObject.Find("Build Canvas");
            old = buildCanvas != null ? buildCanvas.transform.Find("Top Bar") : null;
            Builds.PartsListWindow.TextExport = () => Press(OldButton("Export Parts List"));   // V2: in the Parts list window now
            builds = FindFirstObjectByType<BuildsManager>(FindObjectsInactive.Include);
            if (old != null) {
                var group = old.gameObject.GetOrAdd<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
                oldTitle = old.Find("Build Title")?.GetComponent<TMP_Text>();
                oldWeight = old.Find("Estimated Weight Label")?.GetComponent<TMP_Text>();
            }

            bar = Ui.Image(transform, "Bar", Theme.Panel).rectTransform;
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(0f, Height);
            bar.anchoredPosition = Vector2.zero;
            bar.GetComponent<Image>().raycastTarget = true;
            var line = Ui.Image(bar, "Border", Theme.Border);
            line.rectTransform.anchorMin = new Vector2(0f, 0f);
            line.rectTransform.anchorMax = new Vector2(1f, 0f);
            line.rectTransform.pivot = new Vector2(0.5f, 0f);
            line.rectTransform.sizeDelta = new Vector2(0f, 1f);

            // Left: logo, menus, quick actions.
            var left = Ui.Rect("Left", bar);
            left.anchorMin = new Vector2(0f, 0f);
            left.anchorMax = new Vector2(0f, 1f);
            left.pivot = new Vector2(0f, 0.5f);
            left.anchoredPosition = new Vector2(8f, 0f);
            Ui.Row(left, 2f);
            left.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var splash = OldButton("Splash Screen Button");
            var logo = Ui.IconButton(left, "Logo", splash != null ? splash.GetComponent<Image>().sprite : null, () => Press(splash), 34f, 22f);
            logo.Size(34f, 34f);
            logo.transform.Find("Icon").GetComponent<Image>().color = Color.white;
            Ui.Tip(logo, "Protobot", null, "The start window: recent builds and news");

            MenuButton(left, "File", FileMenu);
            MenuButton(left, "Edit", EditMenu);
            MenuButton(left, "Plastic", PlasticMenu);
            MenuButton(left, "Help", HelpMenu);
            Ui.Divider(left, true).Size(1f, 22f);

            var undo = OldButton("Functions Bar/Undo");
            var redo = OldButton("Functions Bar/Redo");
            QuickButton(left, "Undo", undo != null ? undo.GetComponent<Image>().sprite : null, () => Press(undo), "Undo", Key("Undo"));
            var redoButton = QuickButton(left, "Redo", redo != null ? redo.GetComponent<Image>().sprite : null, () => Press(redo), "Redo", Key("Redo"));
            // The original draws redo with the undo arrow mirrored.
            var redoIcon = redoButton.transform.Find("Icon");
            if (redoIcon != null && undo != null && redo != null && undo.GetComponent<Image>().sprite == redo.GetComponent<Image>().sprite)
                redoIcon.localScale = new Vector3(-1f, 1f, 1f);
            Ui.Divider(left, true).Size(1f, 22f);
            ToggleButton(left, OldToggle("Functions Bar/Grid Toggle"), "Grid", Key("Toggle Grid"), "Show the grid under the build");
            ToggleButton(left, OldToggle("Functions Bar/Select Hole Mode"), "Holes only", Key("Toggle Holes Only"), "Clicks pick holes, not parts");
            ToggleButton(left, OldToggle("Functions Bar/Toggle Shadows"), "Shadows", null, "Shadows on the build");

            // Middle: the build's name.
            title = Ui.Text(bar, "Build", "", Theme.FontBody, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
            title.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, Height));

            // Right: plastic used, weight, full screen.
            var right = Ui.Rect("Right", bar);
            right.anchorMin = new Vector2(1f, 0f);
            right.anchorMax = new Vector2(1f, 1f);
            right.pivot = new Vector2(1f, 0.5f);
            right.anchoredPosition = new Vector2(-8f, 0f);
            Ui.Row(right, 8f, null, TextAnchor.MiddleRight);
            right.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var chip = Ui.Button(right, "Plastic", null, () => Polycarb.SheetsDialog.Open(), false, 6f);
            plasticChip = (Image)chip.targetGraphic;
            plasticChip.color = Theme.Field;
            Ui.Row((RectTransform)chip.transform, 6f, new RectOffset(10, 12, 0, 0));
            var sheetIcon = Ui.Image(chip.transform, "Icon", new Color(0.7f, 0.85f, 1f, 1f));
            sheetIcon.sprite = Interface.Instance.sheetsIcon;
            sheetIcon.preserveAspect = true;
            sheetIcon.Size(16f, 16f);
            plastic = Ui.Text(chip.transform, "Text", "Plastic", Theme.FontSmall, Theme.TextDim, Theme.Medium);
            plastic.Size(-1, 30f);
            chip.Size(-1, 30f);
            Ui.Tip(chip, "Plastic", null, "The polycarbonate in this build, against the rules: click to see it laid out on sheets.");

            // V2: a picture of the robot (see-through or white background).
            var picture = Ui.Button(right, "Picture", "Picture", Builds.PictureSaver.Open, false, 6f, Theme.FontSmall);
            picture.Size(-1, 30f);
            Ui.Tip(picture, "Save a picture", null, "The robot as you see it, with a see-through or white background, saved in Pictures\\Protobot (for notebooks, shirts and slides).");
            // V2: the simulation sandbox.
            // V2: games on a competition field (Robot Skills for now).
            var play = Ui.Button(right, "Play", "Play game", () => {
                if (Game.SkillsGame.Active) Game.SkillsGame.Current.Exit();
                else Game.GameUi.OpenPlay();
            }, false, 6f, Theme.FontSmall);
            play.Size(-1, 30f);
            Ui.Tip(play, "Play game", null, "Put your robot on this season's field and run Robot Skills: a timed minute, scored as you go.");
            simulate = Ui.Button(right, "Simulate", "Simulate", Motion.Sandbox.Toggle, true, 6f, Theme.FontSmall);
            simulate.Size(-1, 30f);
            Ui.Tip(simulate, "Simulate", null, "Gravity and physics: the robot drives (W/A/S/D, arrows or a gamepad), motors turn, cylinders push; parts not fastened fall off. Stop puts the build back.");

            weight = Ui.Text(right, "Weight", "", Theme.FontSmall, Theme.TextDim, null, TextAlignmentOptions.MidlineRight);
            weight.Size(-1, Height);
            var fullscreen = OldToggle("Fullscreen Toggle");
            ToggleButton(right, fullscreen, "Full screen", null, "Fill the screen");

            Polycarb.SheetUsage.Changed += UpdatePlastic;
            UpdatePlastic();
        }

        private void OnDestroy() => Polycarb.SheetUsage.Changed -= UpdatePlastic;

        // ------------------------------------------------------------------ menus

        private List<Item> FileMenu() => new() {
            new Item { label = "New build", action = () => Press(OldButton("File Menu/New Button")) },
            new Item { label = "Open…", action = () => Press(OldButton("File Menu/Open Button")) },
            new Item { divider = true },
            new Item { label = "Save", shortcut = Key("Save"), action = () => Press(OldButton("File Menu/Save Button")) },
            new Item { label = "Save as…", action = () => Press(OldButton("File Menu/Save As Button")) },
            new Item { label = Builds.AutoSave.Enabled ? "Autosave every minute: on" : "Autosave every minute: off", action = Builds.AutoSave.Toggle },
            new Item { divider = true },
            new Item { label = "Play game…", action = Game.GameUi.OpenPlay },
            new Item { label = "Multiplayer…", action = Online.OnlineWindow.Open },   // V2
            new Item { divider = true },
            new Item { label = "Import part…", action = Custom.ImportDialog.Choose },
            new Item { label = "Custom parts library…", action = Custom.LibraryDialog.Open },
            new Item { label = "Parts list…", action = Builds.PartsListWindow.Open },   // V2: its exports (spreadsheet, text) are in it
            new Item { label = "Export 3D model (OBJ, STL)…", action = Builds.ModelExport.Open },   // V2
            new Item { label = "Save picture…", action = Builds.PictureSaver.Open },   // V2
            new Item { divider = true },
            new Item { label = "Exit", action = () => Press(OldButton("File Menu/Exit Button")) },
        };

        private List<Item> EditMenu() => new() {
            new Item { label = "Undo", shortcut = Key("Undo"), action = () => Press(OldButton("Functions Bar/Undo")) },
            new Item { label = "Redo", shortcut = Key("Redo"), action = () => Press(OldButton("Functions Bar/Redo")) },
            new Item { label = "Look at the origin", action = () => {
                // V2 (the original's issue #93): back to the build's middle.
                if (PivotCamera.Main != null) PivotCamera.Main.MoveFocusPosition(Vector3.zero);
                Interface.Say("Looking at the origin");
            } },
            new Item { label = "Open backups folder", action = () => {
                System.IO.Directory.CreateDirectory(Builds.BuildFile.BackupFolder);
                Application.OpenURL("file:///" + Builds.BuildFile.BackupFolder.Replace('\\', '/'));
                Interface.Say("Backups are kept for every build you save (the last 10, one every 10 minutes)");
            } },
            new Item { label = "Controls (motors, cylinders, drive)…", action = () => Motion.ControlsPanel.Open() },
            new Item { label = "Make a chain…", action = () => Motion.ChainTool.Begin() },
            new Item { label = "Mirror", shortcut = Tools.Keybinds.Label("Mirror"), action = Tools.MirrorTool.Open },
            new Item { divider = true },
            new Item { label = "Graphics…", action = GraphicsWindow.Open },
            new Item { label = "Keyboard shortcuts…", action = Tools.KeybindsWindow.Open },
            new Item { label = "Preferences…", action = PreferencesWindow.Open },   // V2: its own window now
        };

        private List<Item> PlasticMenu() => new() {
            new Item { label = "New polycarbonate piece…", action = Polycarb.SheetEditor.Open },
            new Item { label = "Plastic sheets…", action = () => Polycarb.SheetsDialog.Open() },
        };

        private List<Item> HelpMenu() => new() {
            new Item { label = "Report a bug…", action = () => Builds.CrashGuard.ReportDialog(false) },   // V2: with the bug log
            new Item { label = "Request a part…", action = () => Builds.CrashGuard.ReportDialog(true) },   // V2
            new Item { label = "Open bug log folder", action = () => Application.OpenURL("file:///" + Builds.CrashGuard.LogFolder.Replace('\\', '/')) },
            new Item { label = "Patch notes", action = () => Press(OldButton("Help Menu/Patch Notes Button")) },
            new Item { label = "Official website", action = () => Press(OldButton("Help Menu/Website Button")) },
        };

        private void MenuButton(RectTransform parent, string name, Func<List<Item>> items) {
            var button = Ui.Button(parent, name, name, null, false, 6f, Theme.FontBody);
            button.transition = Selectable.Transition.None;
            button.targetGraphic.color = new Color(1f, 1f, 1f, 0f);
            button.gameObject.AddComponent<HoverTint>().target = (Image)button.targetGraphic;
            button.Size(-1, 32f);
            var rect = (RectTransform)button.transform;
            menuButtons[name] = rect;
            button.onClick.AddListener(() => {
                if (openName == name) CloseMenu();
                else OpenMenu(name, rect, items());
            });
            // Moving onto another menu while one is open opens it.
            button.gameObject.AddComponent<FlyoutItem>().onEnter = () => {
                if (openName != null && openName != name) OpenMenu(name, rect, items());
            };
        }

        private void OpenMenu(string name, RectTransform anchor, List<Item> items) {
            CloseMenu();
            openName = name;
            ((Image)anchor.GetComponent<Button>().targetGraphic).color = Theme.Hover;
            var height = items.Sum(i => i.divider ? 9f : 30f) + 8f;
            // Above everything else (the tool rail and options sit under the menus).
            openMenu = Ui.Panel(Interface.Root, name + " Menu", Theme.PanelRaised, 8f).rectTransform;
            openMenu.SetAsLastSibling();
            var corners = new Vector3[4];
            anchor.GetWorldCorners(corners);
            var bottomLeft = Layout.FromScreen(corners[0]);
            openMenu.Place(Vector2.zero, new Vector2(0f, 1f), bottomLeft + new Vector2(0f, -4f), new Vector2(250f, height));
            var y = -4f;
            foreach (var item in items) {
                if (item.divider) {
                    var line = Ui.Image(openMenu, "Divider", Theme.Divider);
                    line.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, y - 4f), new Vector2(230f, 1f));
                    y -= 9f;
                    continue;
                }
                var row = Ui.Image(openMenu, item.label, new Color(1f, 1f, 1f, 0f), 5f, true);
                row.rectTransform.anchorMin = new Vector2(0f, 1f);
                row.rectTransform.anchorMax = new Vector2(1f, 1f);
                row.rectTransform.pivot = new Vector2(0.5f, 1f);
                row.rectTransform.anchoredPosition = new Vector2(0f, y);
                row.rectTransform.sizeDelta = new Vector2(-8f, 28f);
                row.gameObject.AddComponent<HoverTint>().target = row;
                Ui.Text(row.transform, "Label", item.label, Theme.FontSmall, Theme.Text).rectTransform.Fill(12, 0, string.IsNullOrEmpty(item.shortcut) ? 10 : 80, 0);
                if (!string.IsNullOrEmpty(item.shortcut))
                    Ui.Text(row.transform, "Shortcut", item.shortcut, 12.5f, Theme.TextFaint, null, TextAlignmentOptions.MidlineRight).rectTransform.Fill(0, 0, 12, 0);
                var button = row.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                var nav = button.navigation;
                nav.mode = Navigation.Mode.None;
                button.navigation = nav;
                var action = item.action;
                button.onClick.AddListener(() => {
                    CloseMenu();
                    action?.Invoke();
                });
                y -= 30f;
            }
        }

        private void CloseMenu() {
            if (openName != null && menuButtons.TryGetValue(openName, out var anchor))
                ((Image)anchor.GetComponent<Button>().targetGraphic).color = new Color(1f, 1f, 1f, 0f);
            if (openMenu != null) Destroy(openMenu.gameObject);
            openMenu = null;
            openName = null;
        }

        // ------------------------------------------------------------------ buttons

        private static Button QuickButton(RectTransform parent, string name, Sprite icon, Action action, string tip, string key) {
            var button = Ui.IconButton(parent, name, icon, action, 32f, 16f);
            button.Size(32f, 32f);
            Ui.Tip(button, tip, key);
            return button;
        }

        private void ToggleButton(RectTransform parent, Toggle source, string tip, string key, string description) {
            if (source == null) return;
            var icon = source.GetComponent<Image>().sprite;
            var button = Ui.IconButton(parent, tip, icon, () => source.isOn = !source.isOn, 32f, 16f);
            button.Size(32f, 32f);
            Ui.Tip(button, tip, key, description);
            toggles.Add((source, (Image)button.targetGraphic, button.transform.Find("Icon").GetComponent<Image>()));
        }

        private void UpdatePlastic() {
            if (plastic == null) return;
            var pieces = Polycarb.SheetUsage.PiecesInBuild().Count;
            var current = Polycarb.SheetUsage.CurrentRules == Polycarb.SheetUsage.Rules.Current;
            plastic.text = current ? "Plastic " + pieces + "/" + Polycarb.SheetUsage.MaxPieces : "Plastic " + pieces + (pieces == 1 ? " piece" : " pieces");
            var over = current ? pieces > Polycarb.SheetUsage.MaxPieces : pieces > 0 && Polycarb.SheetUsage.Plan().OverSheet;
            plastic.color = over ? Theme.Danger : pieces > 0 ? Theme.Text : Theme.TextDim;
        }

        private void Update() {
            if (oldTitle != null) title.text = oldTitle.text.Trim();
            if (oldWeight != null) weight.text = oldWeight.text.Trim().Replace("Estimated Weight:", "Weight").Replace("Estimated Weight", "Weight");
            foreach (var (source, background, icon) in toggles) {
                if (source == null) continue;
                background.color = source.isOn ? Theme.AccentSoft : new Color(1f, 1f, 1f, 0f);
                icon.color = source.isOn ? Theme.Text : Theme.TextFaint;
            }
            if (++plasticFrame % 30 == 0) UpdatePlastic();
            if (simulate != null) {
                var label = simulate.GetComponentInChildren<TextMeshProUGUI>();
                var text = Motion.Sandbox.Running ? "Stop simulating" : "Simulate";
                if (label.text != text) label.text = text;
            }

            // Clicking anywhere else closes the menu.
            var mouse = Mouse.current;
            if (openMenu != null && mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) {
                var point = mouse.position.ReadValue();
                var onMenu = RectTransformUtility.RectangleContainsScreenPoint(openMenu, point);
                var onButton = menuButtons.Values.Any(b => RectTransformUtility.RectangleContainsScreenPoint(b, point));
                if (!onMenu && !onButton) CloseMenu();
            }
            if (openMenu != null && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) CloseMenu();
        }
    }
}
