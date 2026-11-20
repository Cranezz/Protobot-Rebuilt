using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.RobotCode.Blocks {
    /// <summary>
    /// V2 (Phase 9, Blocks): the blocks editor in the code window, laid out like VEXcode V5 Blocks: the categories
    /// down the left, their blocks beside them (drag one out), the canvas (drag the background to move round it, the
    /// wheel scrolls, Ctrl+wheel zooms). Blocks snap under each other, into C mouths and (reporters) into slots; a
    /// block dropped back on the palette is deleted. Right-click a block: Duplicate / Delete; the canvas: Clean up.
    /// Every change makes the Python again (<see cref="BlockCode"/>) and hands the whole program to the code window.
    /// </summary>
    public class BlockEditor : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IPointerClickHandler {
        private const float PaletteWidth = 250f, CategoryWidth = 64f, SnapDistance = 28f;
        private const string CategoryKey = "V2.Blocks.Category";

        public BlockProgram program = new();
        public BlockCode code;
        /// <summary>The whole program (blocks line + Python) after each change.</summary>
        public Action<string> onChanged;

        private RectTransform paletteContent, categories, viewport, content, marker;
        private ScrollRect paletteScroll;
        private string category;
        private float zoom = 1f;
        private readonly Dictionary<Block, BlockView> views = new();
        private readonly List<BlockView> paletteViews = new();
        private readonly List<string> undo = new(), redo = new();

        // Dragging.
        private Block dragging;
        private BlockView dragView;
        private Vector2 grab;
        private Target target;

        private struct Target {
            public int kind;        // 0 none, 1 under a block, 2 in a mouth, 3 over a stack's top, 4 in a slot
            public Block block;
            public int index;
            public Vector2 at;
            public BlockView view;
        }

        public static BlockEditor Create(RectTransform parent) {
            var root = Ui.Rect("Blocks", parent).Fill();
            var editor = root.gameObject.AddComponent<BlockEditor>();
            editor.Build(root);
            return editor;
        }

        private void Build(RectTransform root) {
            // Categories (a colored dot and name each), down the left.
            var catBox = Ui.Image(root, "Categories", new Color(0.06f, 0.065f, 0.08f, 1f), 0f, true).rectTransform;
            catBox.anchorMin = Vector2.zero;
            catBox.anchorMax = new Vector2(0f, 1f);
            catBox.pivot = new Vector2(0f, 0.5f);
            catBox.offsetMin = Vector2.zero;
            catBox.offsetMax = new Vector2(CategoryWidth, 0f);
            categories = Ui.Rect("List", catBox).Fill(0, 6, 0, 6);
            Ui.Column(categories, 2f);

            // The palette: the chosen category's blocks.
            var pal = Ui.Image(root, "Palette", new Color(0.1f, 0.105f, 0.125f, 1f), 0f, true).rectTransform;
            pal.anchorMin = Vector2.zero;
            pal.anchorMax = new Vector2(0f, 1f);
            pal.pivot = new Vector2(0f, 0.5f);
            pal.offsetMin = new Vector2(CategoryWidth, 0f);
            pal.offsetMax = new Vector2(CategoryWidth + PaletteWidth, 0f);
            var palViewport = Ui.Rect("Viewport", pal).Fill();
            palViewport.gameObject.AddComponent<RectMask2D>();
            paletteContent = Ui.Rect("Content", palViewport);
            paletteContent.anchorMin = new Vector2(0f, 1f);
            paletteContent.anchorMax = new Vector2(1f, 1f);
            paletteContent.pivot = new Vector2(0.5f, 1f);
            paletteContent.sizeDelta = new Vector2(0f, 100f);
            paletteScroll = pal.gameObject.AddComponent<ScrollRect>();
            paletteScroll.viewport = palViewport;
            paletteScroll.content = paletteContent;
            paletteScroll.horizontal = false;
            paletteScroll.movementType = ScrollRect.MovementType.Clamped;
            paletteScroll.scrollSensitivity = 30f;

            // The canvas.
            var canvasBox = Ui.Image(root, "Canvas", new Color(0.075f, 0.08f, 0.095f, 1f), 0f, true).rectTransform;
            canvasBox.Fill(CategoryWidth + PaletteWidth, 0, 0, 0);
            viewport = canvasBox;
            viewport.gameObject.AddComponent<RectMask2D>();
            // The canvas's events come to this component (scroll, drag on the background, right-click).
            var forward = viewport.gameObject.AddComponent<Forward>();
            forward.editor = this;
            content = Ui.Rect("Content", viewport);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = new Vector2(20f, -20f);
            content.sizeDelta = new Vector2(10f, 10f);
            marker = Ui.Image(content, "Marker", new Color(1f, 1f, 1f, 0.35f), 3f).rectTransform;
            marker.anchorMin = marker.anchorMax = marker.pivot = new Vector2(0f, 1f);
            marker.gameObject.SetActive(false);

            category = PlayerPrefs.GetString(CategoryKey, "drivetrain");
            if (BlockDefs.Categories.All(c => c.id != category)) category = "drivetrain";
            BuildCategories();
        }

        // Shown again (it was hidden while the program was text): text measured while hidden reads 0 wide, so all again.
        private void OnEnable() {
            if (content == null) return;
            Rebuild();
            BuildPalette();
        }

        // The canvas forwards its pointer events here (the editor's own object covers the palette too).
        private class Forward : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IPointerClickHandler {
            public BlockEditor editor;
            public void OnScroll(PointerEventData e) => editor.OnScroll(e);
            public void OnBeginDrag(PointerEventData e) => editor.OnBeginDrag(e);
            public void OnDrag(PointerEventData e) => editor.OnDrag(e);
            public void OnPointerClick(PointerEventData e) => editor.OnPointerClick(e);
        }

        // ------------------------------------------------------------------ loading and saving

        public void Load(BlockProgram p) {
            program = p ?? new BlockProgram();
            undo.Clear();
            redo.Clear();
            Rebuild();
            BuildPalette();
            code = BlockCode.Generate(program);
        }

        /// <summary>The program as saved (the blocks line + its Python).</summary>
        public string Source() => program.ToSource(out code);

        public void BeginEdit() => Snapshot();

        private void Snapshot() {
            var json = program.Json();
            if (undo.Count > 0 && undo[^1] == json) return;
            undo.Add(json);
            if (undo.Count > 100) undo.RemoveAt(0);
            redo.Clear();
        }

        /// <summary>Something changed: the Python again, to the code window.</summary>
        public void Changed() {
            var source = program.ToSource(out code);
            onChanged?.Invoke(source);
        }

        public void Undo() {
            if (undo.Count == 0) return;
            redo.Add(program.Json());
            program = BlockProgram.FromJson(undo[^1]);
            undo.RemoveAt(undo.Count - 1);
            Rebuild();
            BuildPalette();
            Changed();
        }

        public void Redo() {
            if (redo.Count == 0) return;
            undo.Add(program.Json());
            program = BlockProgram.FromJson(redo[^1]);
            redo.RemoveAt(redo.Count - 1);
            Rebuild();
            BuildPalette();
            Changed();
        }

        /// <summary>The robot's configuration again (Insert devices): device names in the dropdowns and the Python.</summary>
        public void RefreshDevices(string config) {
            Snapshot();
            program.config = config;
            BuildPalette();
            Rebuild();
            Changed();
        }

        // ------------------------------------------------------------------ views

        public void Register(BlockView view) {
            if (!view.palette) views[view.block] = view;
        }

        private void Rebuild() {
            views.Clear();
            for (var i = content.childCount - 1; i >= 0; i--) {
                var child = content.GetChild(i);
                if (child != marker) Destroy(child.gameObject);
            }
            foreach (var stack in program.stacks) {
                var view = BlockView.Make(this, stack, content, false);
                view.rect.anchoredPosition = new Vector2(stack.x, -stack.y);
                view.LayoutChain();
            }
            BuildComments();
        }

        /// <summary>A slot's text changed: the stack it's in lays out again.</summary>
        public void Relayout(BlockView view) {
            var t = view.transform;
            while (t.parent != null && t.parent != content && t.parent != paletteContent) t = t.parent;
            if (t.TryGetComponent(out BlockView root)) root.LayoutChain();
        }

        private void BuildCategories() {
            for (var i = categories.childCount - 1; i >= 0; i--) Destroy(categories.GetChild(i).gameObject);
            foreach (var c in BlockDefs.Categories) {
                var cat = c;
                var item = Ui.Image(categories, c.name, cat.id == category ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0f), 0f, true);   // (a rounded Image asks the column for 66 px)
                item.Size(-1, 50f);
                var dot = Ui.Image(item.transform, "Dot", cat.color, 9f);
                dot.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(20f, 20f));
                var name = Ui.Text(item.transform, "Name", cat.name, 10.5f, cat.id == category ? Theme.Text : Theme.TextDim, Theme.Medium, TextAlignmentOptions.Center);
                name.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(CategoryWidth, 16f));
                name.raycastTarget = false;
                var button = item.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => {
                    category = cat.id;
                    PlayerPrefs.SetString(CategoryKey, category);
                    BuildCategories();
                    BuildPalette();
                    paletteContent.anchoredPosition = Vector2.zero;
                });
            }
        }

        private void BuildPalette() {
            paletteViews.Clear();
            for (var i = paletteContent.childCount - 1; i >= 0; i--) Destroy(paletteContent.GetChild(i).gameObject);
            var cat = BlockDefs.Category(category);
            var y = 12f;
            var title = Ui.Text(paletteContent, "Title", cat.name, Theme.FontBody, Theme.Text, Theme.Bold);
            title.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -y), new Vector2(PaletteWidth - 24f, 20f));
            y += 28f;
            void Button(string label, Action click) {
                var b = Ui.Button(paletteContent, label, label, click, false, 5f, Theme.FontSmall);
                var r = (RectTransform)b.transform;
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
                r.anchoredPosition = new Vector2(12f, -y);
                r.sizeDelta = new Vector2(PaletteWidth - 30f, 26f);
                y += 34f;
            }
            void Add(Block b) {
                var view = BlockView.Make(this, b, paletteContent, true);
                view.rect.anchoredPosition = new Vector2(12f, -y);
                view.LayoutChain();
                paletteViews.Add(view);
                y += view.ChainHeight() + 10f;
            }
            var devices = BlockCode.Devices(string.IsNullOrWhiteSpace(program.config) ? BlockCode.DefaultConfig : program.config);
            Block Fresh(BlockDef def) {
                var b = Block.Make(def);
                for (var i = 0; i < def.slots.Count; i++) {
                    var s = def.slots[i];
                    if (s.kind == SlotKind.Device) b.fields[i] = devices.TryGetValue(s.key, out var list) && list.Count > 0 ? list[0] : "";
                    else if (s.kind == SlotKind.Variable) b.fields[i] = program.Names(s.key).FirstOrDefault() ?? "";
                }
                return b;
            }
            void Hint(string text) {
                var note = Ui.Text(paletteContent, "Hint", text, Theme.FontSmall, Theme.TextDim);
                note.textWrappingMode = TextWrappingModes.Normal;
                note.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -y), new Vector2(PaletteWidth - 24f, 50f));
                y += 54f;
            }
            if (category == "myblocks") {
                Button("Make a Block", () => MakeBlockDialog(null));
                if (program.myBlocks.Count == 0)
                    Hint("A My Block is your own block: a name, inputs and labels. Its define hat goes on the canvas; the blocks under it run wherever the block is used.");
                foreach (var m in program.myBlocks) Add(Fresh(BlockDefs.ById["call:" + m.key]));
                paletteContent.sizeDelta = new Vector2(0f, y + 20f);
                return;
            }
            if (category == "variables") {
                Button("Make a Variable", () => AskName("Make a Variable", "New variable name:", program.numbers));
                foreach (var v in program.numbers) {
                    var b = Block.Make(BlockDefs.ById["var"]);
                    b.fields[0] = v;
                    Add(b);
                }
                Add(Fresh(BlockDefs.ById["set_var"]));
                Add(Fresh(BlockDefs.ById["change_var"]));
                y += 8f;
                Button("Make a Boolean", () => AskName("Make a Boolean", "New boolean name:", program.booleans));
                foreach (var v in program.booleans) {
                    var b = Block.Make(BlockDefs.ById["bool"]);
                    b.fields[0] = v;
                    Add(b);
                }
                Add(Fresh(BlockDefs.ById["set_bool"]));
                y += 8f;
                Button("Make a List", () => MakeListDialog(false));
                if (program.lists.Count > 0) {
                    Add(Fresh(BlockDefs.ById["list_item"]));
                    Add(Fresh(BlockDefs.ById["list_replace"]));
                    foreach (var l in program.lists.Where(l => BlockDefs.SetFits(l, false))) Add(Fresh(BlockDefs.ById["setlist:" + l.name]));
                    Add(Fresh(BlockDefs.ById["list_length"]));
                }
                y += 8f;
                Button("Make a 2D List", () => MakeListDialog(true));
                if (program.lists2.Count > 0) {
                    Add(Fresh(BlockDefs.ById["list2_item"]));
                    Add(Fresh(BlockDefs.ById["list2_replace"]));
                    foreach (var l in program.lists2.Where(l => BlockDefs.SetFits(l, true))) Add(Fresh(BlockDefs.ById["setlist:" + l.name]));
                    Add(Fresh(BlockDefs.ById["list2_length"]));
                }
            }
            else {
                if (category == "events") Button("New message", () => AskName("New message", "Message name:", program.events));
                var missing = new HashSet<string>();
                foreach (var def in BlockDefs.All.Where(d => d.category == category && !d.hidden)) {
                    // A device block for a kind the robot doesn't have still shows (VEXcode shows them once configured): faint note once.
                    foreach (var s in def.slots)
                        if (s.kind == SlotKind.Device && (!devices.TryGetValue(s.key, out var list) || list.Count == 0)) missing.Add(BlockDefs.KindName(s.key));
                    if (def.py.Contains("%dt") && devices["drivetrain"].Count == 0) missing.Add("drivetrain");
                    Add(Fresh(def));
                }
                if (missing.Count > 0) {
                    var note = Ui.Text(paletteContent, "Note", "Not on this robot: " + string.Join(", ", missing) + ". Blocks for them do nothing until it has one "
                                                               + "(Edit > Devices sets ports and drive sides; Devices up top reads the robot again).", Theme.FontSmall, Theme.TextDim);
                    note.textWrappingMode = TextWrappingModes.Normal;
                    note.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -y), new Vector2(PaletteWidth - 24f, 60f));
                    y += 66f;
                }
            }
            paletteContent.sizeDelta = new Vector2(0f, y + 20f);
        }

        // ------------------------------------------------------------------ dragging

        private Vector2 ToContent(Vector2 screen) {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screen, null, out var local);
            return new Vector2(local.x, -local.y);   // y down
        }

        private Vector2 PositionOf(BlockView v) {
            var p = content.InverseTransformPoint(v.rect.position);
            return new Vector2(p.x, -p.y);
        }

        public void BeginDrag(BlockView view, PointerEventData e) {
            if (dragging != null) return;
            var mouse = ToContent(e.pressPosition);
            Snapshot();
            Block block;
            Vector2 topLeft;
            if (!view.palette && InDefine(view.block)) {
                // An input's reporter in a define hat: a copy comes out (as from the palette).
                block = view.block.Clone();
                topLeft = PositionOf(view);
            }
            else if (view.palette) {
                block = view.block.Clone();
                // Where the palette block is, in the canvas's terms (it may be dropped straight back: deleted).
                var screenTopLeft = RectTransformUtility.WorldToScreenPoint(null, view.rect.position);
                topLeft = ToContent(screenTopLeft);
            }
            else {
                block = view.block;
                topLeft = PositionOf(view);
                program.Detach(block);
            }
            grab = mouse - topLeft;
            block.x = topLeft.x;
            block.y = topLeft.y;
            program.stacks.Add(block);
            dragging = block;
            Rebuild();
            dragView = views.TryGetValue(block, out var v) ? v : null;
            if (dragView != null) dragView.transform.SetAsLastSibling();
            marker.SetAsLastSibling();
            Audio.Sounds.Play(Audio.Sounds.Tick);
        }

        private void Update() {
            if (dragging != null) FollowMouse();
            else HandleKeys();
        }

        private void FollowMouse() {
            var mouse = Mouse.current;
            if (mouse == null || dragView == null) {
                Drop(false);
                return;
            }
            var at = ToContent(mouse.position.ReadValue()) - grab;
            dragging.x = at.x;
            dragging.y = at.y;
            dragView.rect.anchoredPosition = new Vector2(at.x, -at.y);
            target = FindTarget(at);
            ShowMarker();
            if (!mouse.leftButton.isPressed) Drop(OverPalette(mouse.position.ReadValue()));
        }

        private bool OverPalette(Vector2 screen) {
            var r = (RectTransform)transform;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(r, screen, null, out var local)
                   && local.x - r.rect.xMin < CategoryWidth + PaletteWidth && r.rect.Contains(local);
        }

        private bool Inside(BlockView v, BlockView root) {
            for (var t = v.transform; t != null; t = t.parent)
                if (t == root.transform) return true;
            return false;
        }

        private Target FindTarget(Vector2 at) {
            var best = new Target();
            var bestDistance = SnapDistance;
            var def = dragging.Def;
            if (def == null) return best;
            void Consider(int kind, Block block, int index, Vector2 point, Vector2 from, BlockView view) {
                var d = Vector2.Distance(point, from);
                if (d >= bestDistance) return;
                bestDistance = d;
                best = new Target { kind = kind, block = block, index = index, at = point, view = view };
            }
            if (def.IsReporter) {
                var from = at + new Vector2(0f, dragView.height / 2f);
                foreach (var v in views.Values) {
                    if (Inside(v, dragView) || program.IsDefine(v.block)) continue;   // (a define hat's slots hold its inputs)
                    foreach (var p in v.parts) {
                        if (p.slot < 0) continue;
                        var kind = v.Def.slots[p.slot].kind;
                        var fits = def.shape == BlockShape.Boolean ? kind == SlotKind.Boolean : kind == SlotKind.Number || kind == SlotKind.Text;
                        if (!fits) continue;
                        var tl = PositionOf(v) + p.pos;
                        Consider(4, v.block, p.slot, tl + new Vector2(0f, p.size.y / 2f), from, v);
                    }
                }
                return best;
            }
            if (!def.HasPrevious) return best;   // a hat starts a stack
            var last = dragging.Last();
            var lastDef = last.Def;
            var chain = dragView.ChainHeight();
            foreach (var v in views.Values) {
                if (Inside(v, dragView) || v.Def.IsReporter) continue;
                var tl = PositionOf(v);
                // Under it.
                if (v.Def.HasNext && (v.block.next == null || lastDef.HasNext)) Consider(1, v.block, 0, tl + new Vector2(0f, v.height), at, v);
                // In its mouths.
                for (var m = 0; m < v.mouthTops.Count; m++)
                    if (v.block.mouths[m] == null || lastDef.HasNext)
                        Consider(2, v.block, m, tl + new Vector2(BlockView.ArmW, v.mouthTops[m]), at, v);
            }
            // Over the top of a stack (the dragged stack's bottom meets its top).
            if (lastDef.HasNext)
                foreach (var stack in program.stacks) {
                    if (stack == dragging || stack.Def == null || !stack.Def.HasPrevious || !views.TryGetValue(stack, out var sv)) continue;
                    Consider(3, stack, 0, PositionOf(sv), at + new Vector2(0f, chain), sv);
                }
            return best;
        }

        private void ShowMarker() {
            if (target.kind == 0) {
                marker.gameObject.SetActive(false);
                return;
            }
            marker.gameObject.SetActive(true);
            if (target.kind == 4) {
                var p = target.view.parts.First(x => x.slot == target.index);
                var tl = PositionOf(target.view) + p.pos;
                marker.anchoredPosition = new Vector2(tl.x - 2f, -(tl.y - 2f));
                marker.sizeDelta = p.size + new Vector2(4f, 4f);
            }
            else {
                var at = target.kind == 3 ? target.at - new Vector2(0f, 5f) : target.at;
                marker.anchoredPosition = new Vector2(at.x, -(at.y - 2f));
                marker.sizeDelta = new Vector2(Mathf.Max(60f, dragView.width), 6f);
            }
        }

        private void Drop(bool delete) {
            var block = dragging;
            dragging = null;
            marker.gameObject.SetActive(false);
            if (block == null) return;
            if (delete && program.IsDefine(block) && !CanDeleteDefine(block)) delete = false;
            if (delete) {
                program.stacks.Remove(block);
                if (program.IsDefine(block)) program.myBlocks.Remove(program.MyBlockOf(block));
                Audio.Sounds.Play(Audio.Sounds.Tick);
                if (program.IsDefine(block)) BuildPalette();
            }
            else if (target.kind != 0 && target.block != null) {
                program.stacks.Remove(block);
                var last = block.Last();
                switch (target.kind) {
                    case 1:
                        last.next = target.block.next;
                        target.block.next = block;
                        break;
                    case 2:
                        last.next = target.block.mouths[target.index];
                        target.block.mouths[target.index] = block;
                        break;
                    case 3:
                        var below = target.block;
                        program.stacks.Remove(below);
                        last.next = below;
                        block.x = below.x;
                        block.y = below.y - (dragView != null ? dragView.ChainHeight() : 40f);
                        program.stacks.Add(block);
                        break;
                    case 4:
                        var old = target.block.inputs[target.index];
                        target.block.inputs[target.index] = block;
                        if (old != null) {
                            // The one that was there goes beside it on the canvas.
                            old.x = block.x + 30f;
                            old.y = block.y + 30f;
                            program.stacks.Add(old);
                        }
                        break;
                }
                Audio.Sounds.Play(Audio.Sounds.Tick);
            }
            else if (block.x < 0f || block.y < 0f) {
                // Kept on the canvas: not left of or above where it can be seen.
                block.x = Mathf.Max(block.x, 0f);
                block.y = Mathf.Max(block.y, 0f);
            }
            dragView = null;
            target = default;
            Rebuild();
            Changed();
        }

        // ------------------------------------------------------------------ the canvas

        public void OnScroll(PointerEventData e) {
            var kb = Keyboard.current;
            if (kb != null && (kb.ctrlKey.isPressed || kb.leftCommandKey.isPressed)) {
                var before = ToContent(e.position);
                zoom = Mathf.Clamp(zoom * (e.scrollDelta.y > 0 ? 1.1f : 1f / 1.1f), 0.4f, 2f);
                content.localScale = new Vector3(zoom, zoom, 1f);
                var after = ToContent(e.position);
                content.anchoredPosition += new Vector2(after.x - before.x, -(after.y - before.y)) * zoom;
                return;
            }
            content.anchoredPosition += new Vector2(kb != null && kb.shiftKey.isPressed ? e.scrollDelta.y * 30f : 0f, -e.scrollDelta.y * 30f);
        }

        public void OnBeginDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e) {
            if (e.button != PointerEventData.InputButton.Left && e.button != PointerEventData.InputButton.Middle) return;
            var canvas = GetComponentInParent<Canvas>();
            var scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            content.anchoredPosition += e.delta / scale;
        }

        public void OnPointerClick(PointerEventData e) {
            if (e.button != PointerEventData.InputButton.Right || e.dragging) return;
            var at = ToContent(e.position);
            Menu(e.position, new[] { "Add comment", "Clean up blocks", "Zoom to 100%" }, i => {
                if (i == 0) {
                    Snapshot();
                    program.notes.Add(new Note { text = "", x = Mathf.Max(0f, at.x), y = Mathf.Max(0f, at.y) });
                    Rebuild();
                    focusNote = program.notes[^1];
                    Changed();
                }
                else if (i == 1) CleanUp();
                else {
                    zoom = 1f;
                    content.localScale = Vector3.one;
                    content.anchoredPosition = new Vector2(20f, -20f);
                }
            });
        }

        /// <summary>Stacks in a column down the left, in order (hats first).</summary>
        public void CleanUp() {
            Snapshot();
            var y = 10f;
            foreach (var stack in program.stacks.OrderBy(s => s.Def?.shape == BlockShape.Hat ? 0 : 1).ThenBy(s => s.y).ToList()) {
                stack.x = 10f;
                stack.y = y;
                y += (views.TryGetValue(stack, out var v) ? v.ChainHeight() : 40f) + 24f;
            }
            Rebuild();
            Changed();
        }

        // ------------------------------------------------------------------ menus

        private static void Menu(Vector2 screen, string[] items, Action<int> pick) {
            var anchor = Ui.Rect("Menu Anchor", Interface.Interface.Root);
            anchor.anchorMin = anchor.anchorMax = anchor.pivot = Vector2.zero;
            anchor.anchoredPosition = Layout.FromScreen(screen);
            anchor.sizeDelta = new Vector2(170f, 0f);
            Popup.Open(anchor, items, -1, i => pick(i));
            Destroy(anchor.gameObject, 0.1f);
        }

        public void BlockMenu(BlockView view, Vector2 screen) {
            var block = view.block;
            if (InDefine(block)) return;   // (a define hat's input reporters: drag one out to use it)
            var define = program.IsDefine(block);
            var my = program.MyBlockOf(block);
            var items = new List<(string label, Action act)>();
            if (!define)
                items.Add(("Duplicate", () => {
                    var copy = block.Clone();
                    var at = PositionOf(view);
                    copy.x = at.x + 24f;
                    copy.y = at.y + 24f;
                    foreach (var b in copy.All().ToList())
                        if (program.IsDefine(b)) copy = null;   // (never two definitions of one block)
                    if (copy != null) program.stacks.Add(copy);
                }));
            if (my != null) items.Add(("Edit My Block…", null));
            items.Add((block.comment == null ? "Add comment" : "Remove comment", () => {
                if (block.comment == null) {
                    block.comment = "";
                    focusComment = block;
                }
                else block.comment = null;
            }));
            items.Add(("Delete block", () => {
                if (define && !CanDeleteDefine(block)) return;
                if (define) program.myBlocks.Remove(my);
                Delete(block);
                if (define) BuildPalette();
            }));
            Menu(screen, items.Select(x => x.label).ToArray(), i => {
                if (items[i].act == null) {
                    MakeBlockDialog(my);
                    return;
                }
                Snapshot();
                items[i].act();
                Rebuild();
                Changed();
            });
        }

        /// <summary>The block is an input's reporter held in a define hat.</summary>
        private bool InDefine(Block block) {
            if (block == null || (block.type != "arg" && block.type != "argbool")) return false;
            var (parent, slot, _) = program.Find(block);
            return parent != null && slot >= 0 && program.IsDefine(parent);
        }

        // A define hat goes only when nothing uses its block (VEXcode keeps it too).
        private bool CanDeleteDefine(Block hat) {
            var uses = program.Everything().Count(b => b.type == "call:" + hat.type.Substring("define:".Length));
            if (uses == 0) return true;
            Interface.Interface.Say("This block is used " + uses + (uses == 1 ? " time" : " times") + ": remove those first", false);
            return false;
        }

        /// <summary>Takes one block out (what's in its slots and mouths goes with it); the blocks under it close up.</summary>
        private void Delete(Block block) {
            var (parent, slot, mouth) = program.Find(block);
            var rest = block.Def != null && block.Def.IsReporter ? null : block.next;
            block.next = null;
            if (parent == null) {
                var index = program.stacks.IndexOf(block);
                if (index >= 0) {
                    program.stacks.RemoveAt(index);
                    if (rest != null) {
                        rest.x = block.x;
                        rest.y = block.y;
                        program.stacks.Insert(index, rest);
                    }
                }
                return;
            }
            if (slot >= 0) parent.inputs[slot] = null;
            else if (mouth >= 0) parent.mouths[mouth] = rest;
            else parent.next = rest;
        }

        public void Toggle(BlockView view) {
            Snapshot();
            view.block.toggle = !view.block.toggle;
            Relayout(view);
            Changed();
        }

        public void OpenDropdown(BlockView view, int index, RectTransform anchor) {
            var def = view.Def;
            var slot = def.slots[index];
            var current = index < view.block.fields.Count ? view.block.fields[index] : "";
            string[] labels;
            string[] values;
            Action extra = null;
            switch (slot.kind) {
                case SlotKind.Dropdown:
                    var options = BlockDefs.Options[slot.key];
                    labels = options.Select(o => o.label).ToArray();
                    values = options.Select(o => o.value).ToArray();
                    break;
                case SlotKind.Device:
                    var devices = BlockCode.Devices(string.IsNullOrWhiteSpace(program.config) ? BlockCode.DefaultConfig : program.config);
                    values = devices.TryGetValue(slot.key, out var list) ? list.ToArray() : new string[0];
                    labels = values.Length > 0 ? values : new[] { "(no " + BlockDefs.KindName(slot.key) + " on this robot)" };
                    if (values.Length == 0) values = new[] { "" };
                    break;
                default: {
                    var vars = program.Names(slot.key);
                    var isList = slot.key == "list" || slot.key == "list2";
                    var title = slot.key == "event" ? "New message" : slot.key == "bool" ? "Make a Boolean" : slot.key == "list" ? "Make a List" : slot.key == "list2" ? "Make a 2D List" : "Make a Variable";
                    values = vars.Concat(new[] { "\u0001new" }).ToArray();
                    labels = vars.Concat(new[] { title + "…" }).ToArray();
                    if (isList) extra = () => MakeListDialog(slot.key == "list2", name => view.block.fields[index] = name);
                    else
                        extra = () => AskName(title, slot.key == "event" ? "Message name:" : "New name:", vars, name => {
                            view.block.fields[index] = name;
                        });
                    break;
                }
            }
            var selected = Array.IndexOf(values, current);
            Popup.Open(anchor, labels, selected, i => {
                if (values[i] == "\u0001new") {
                    extra?.Invoke();
                    return;
                }
                Snapshot();
                while (view.block.fields.Count <= index) view.block.fields.Add("");
                view.block.fields[index] = values[i];
                var part = view.parts.FirstOrDefault(p => p.slot == index);
                if (part != null) view.RefreshText(part);
                Relayout(view);
                Changed();
            });
        }

        /// <summary>A name for a new variable, boolean or message (a fitting, unused one).</summary>
        private void AskName(string title, string prompt, List<string> list, Action<string> then = null) {
            var dialog = Dialog.Open(title, new Vector2(420f, 190f));
            Ui.Column(dialog.Body, 8f);
            Dialog.Heading(dialog.Body, prompt);
            var name = "";
            var field = Ui.Field(dialog.Body, "Name", "", v => name = v);
            field.Size(-1, 34f);
            field.onValueChanged.AddListener(v => name = v);
            void Make() {
                var n = BlockCode.Ident((name ?? "").Trim());
                if ((name ?? "").Trim().Length == 0) {
                    Interface.Interface.Say("Type a name first", false);
                    return;
                }
                if (list.Contains(n) || !program.NameFree(n)) {
                    Interface.Interface.Say("\"" + n + "\" is already used: pick another name", false);
                    return;
                }
                Snapshot();
                list.Add(n);
                then?.Invoke(n);
                dialog.Close();
                BuildPalette();
                Rebuild();
                Changed();
            }
            field.onSubmit.AddListener(_ => Make());
            Ui.Button(dialog.Footer, "Cancel", "Cancel", dialog.Close).Size(90f, 34f);
            Ui.Button(dialog.Footer, "OK", "OK", Make, true).Size(90f, 34f);
            field.Select();
            field.ActivateInputField();
        }

        /// <summary>VEXcode's Make a Block: a name, then number / boolean inputs and labels in order; editing an existing
        /// one keeps its calls' values for the inputs that stay.</summary>
        public void MakeBlockDialog(MyBlock existing) {
            var dialog = Dialog.Open(existing == null ? "Make a Block" : "Edit My Block", new Vector2(560f, 520f));
            Ui.Column(dialog.Body, 8f);
            Dialog.Heading(dialog.Body, "Block name");
            var name = existing?.name ?? "";
            var nameField = Ui.Field(dialog.Body, "Name", name, null);
            nameField.Size(-1, 34f);
            var parts = existing?.parts.Select(p => new MyBlockPart { kind = p.kind, text = p.text, id = p.id }).ToList() ?? new List<MyBlockPart>();
            var add = Ui.Rect("Add", dialog.Body);
            Ui.Row(add, 6f);
            add.Size(-1, 30f);
            var list = Ui.Rect("Parts", dialog.Body);
            Ui.Column(list, 4f);
            var preview = Ui.Text(dialog.Body, "Preview", "", Theme.FontSmall, Theme.TextDim);
            preview.textWrappingMode = TextWrappingModes.Normal;
            preview.Size(-1, 36f);

            void Preview() {
                var words = new List<string> { name.Trim().Length > 0 ? name.Trim() : "(name)" };
                foreach (var p in parts) words.Add(p.kind == "number" ? "(" + p.text + ")" : p.kind == "boolean" ? "<" + p.text + ">" : p.text);
                preview.text = "The block: " + string.Join(" ", words);
            }
            void Refresh() {
                for (var i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);
                foreach (var part in parts) {
                    var p = part;
                    var row = Ui.Rect("Part", list);
                    Ui.Row(row, 6f);
                    row.Size(-1, 30f);
                    var tag = Ui.Text(row, "Kind", p.kind == "number" ? "number input" : p.kind == "boolean" ? "boolean input" : "label", Theme.FontSmall, Theme.TextDim);
                    tag.Size(110f, 30f);
                    var f = Ui.Field(row, "Text", p.text, null, Theme.FontSmall);
                    f.Size(-1, 30f, 1f);
                    f.onValueChanged.AddListener(v => {
                        p.text = v;
                        Preview();
                    });
                    Ui.Button(row, "Remove", "×", () => {
                        parts.Remove(p);
                        Refresh();
                    }, false, Theme.RadiusSmall, Theme.FontBody).Size(30f, 30f);
                }
                Preview();
            }
            void Add(string kind) {
                if (parts.Count >= 12 || (kind != "label" && parts.Count(p => p.kind != "label") >= 10)) {
                    Interface.Interface.Say("A block can have up to 10 inputs and 12 parts", false);
                    return;
                }
                var text = kind == "label" ? "label text" : kind;
                for (var n = 2; parts.Any(p => p.kind != "label" && BlockCode.Ident(p.text) == BlockCode.Ident(text)); n++) text = kind + n;
                parts.Add(new MyBlockPart { kind = kind, text = text, id = kind == "label" ? null : Guid.NewGuid().ToString("N").Substring(0, 8) });
                Refresh();
            }
            Ui.Button(add, "Number", "Add an input (number)", () => Add("number"), false, Theme.RadiusSmall, Theme.FontSmall).Size(-1, 30f, 1f);
            Ui.Button(add, "Boolean", "Add an input (boolean)", () => Add("boolean"), false, Theme.RadiusSmall, Theme.FontSmall).Size(-1, 30f, 1f);
            Ui.Button(add, "Label", "Add a label", () => Add("label"), false, Theme.RadiusSmall, Theme.FontSmall).Size(-1, 30f, 1f);
            nameField.onValueChanged.AddListener(v => {
                name = v;
                Preview();
            });

            void Ok() {
                var n = (name ?? "").Trim();
                if (BlockDefs.Words(n).Length == 0) {
                    Interface.Interface.Say("Type the block's name first", false);
                    return;
                }
                if (!program.NameFree(BlockCode.Ident(n), existing)) {
                    Interface.Interface.Say("\"" + n + "\" is already used: pick another name", false);
                    return;
                }
                var args = parts.Where(p => p.kind != "label").Select(p => BlockCode.Ident(p.text.Trim())).ToList();
                if (parts.Any(p => p.kind != "label" && p.text.Trim().Length == 0)) {
                    Interface.Interface.Say("Give every input a name", false);
                    return;
                }
                if (args.Distinct().Count() != args.Count) {
                    Interface.Interface.Say("Two inputs have the same name", false);
                    return;
                }
                foreach (var p in parts) p.text = p.text.Trim();
                Snapshot();
                if (existing == null) {
                    var m = new MyBlock { key = Guid.NewGuid().ToString("N").Substring(0, 8), name = n, parts = parts };
                    program.myBlocks.Add(m);
                    BlockDefs.RegisterMyBlock(m);
                    // Its define hat goes on the canvas below everything else, and the canvas scrolls to it.
                    var bottom = 10f;
                    foreach (var s in program.stacks)
                        bottom = Mathf.Max(bottom, s.y + (views.TryGetValue(s, out var sv) ? sv.ChainHeight() : 60f) + 30f);
                    foreach (var note in program.notes) bottom = Mathf.Max(bottom, note.y + 80f);
                    var hat = Block.Make(BlockDefs.ById["define:" + m.key]);
                    hat.x = 10f;
                    hat.y = bottom;
                    program.stacks.Add(hat);
                    content.anchoredPosition = new Vector2(20f - hat.x * zoom, -40f + hat.y * zoom);
                    program.SyncMyBlocks();
                    category = "myblocks";
                    PlayerPrefs.SetString(CategoryKey, category);
                    BuildCategories();
                }
                else program.EditMyBlock(existing, n, parts);
                dialog.Close();
                BuildPalette();
                Rebuild();
                Changed();
            }
            Refresh();
            nameField.onSubmit.AddListener(_ => Ok());
            Ui.Button(dialog.Footer, "Cancel", "Cancel", dialog.Close).Size(90f, 34f);
            Ui.Button(dialog.Footer, "OK", "OK", Ok, true).Size(90f, 34f);
            nameField.Select();
            nameField.ActivateInputField();
        }

        /// <summary>VEXcode's Make a List / Make a 2D List: a name and how many items (rows and columns), all 0.</summary>
        public void MakeListDialog(bool twoD, Action<string> then = null) {
            var dialog = Dialog.Open(twoD ? "Make a 2D List" : "Make a List", new Vector2(440f, twoD ? 370f : 290f));
            Ui.Column(dialog.Body, 8f);
            Dialog.Heading(dialog.Body, twoD ? "New 2D list name" : "New list name");
            var name = twoD ? "my2DList" : "myList";
            for (var i = 2; !program.NameFree(name); i++) name = (twoD ? "my2DList" : "myList") + i;
            var nameField = Ui.Field(dialog.Body, "Name", name, null);
            nameField.Size(-1, 34f);
            nameField.onValueChanged.AddListener(v => name = v);
            string a = "3", b = "3";
            TMP_InputField Number(string label, string value, Action<string> set) {
                Dialog.Heading(dialog.Body, label);
                var f = Ui.Field(dialog.Body, label, value, null);
                f.contentType = TMP_InputField.ContentType.IntegerNumber;
                f.Size(-1, 34f);
                f.onValueChanged.AddListener(v => set(v));
                return f;
            }
            if (twoD) {
                Number("Rows (1 to 20)", a, v => a = v);
                Number("Columns (1 to 20)", b, v => b = v);
            }
            else Number("Number of items (1 to 100)", a, v => a = v);

            void Ok() {
                var n = BlockCode.Ident((name ?? "").Trim());
                if ((name ?? "").Trim().Length == 0) {
                    Interface.Interface.Say("Type a name first", false);
                    return;
                }
                if (!program.NameFree(n)) {
                    Interface.Interface.Say("\"" + n + "\" is already used: pick another name", false);
                    return;
                }
                int.TryParse(a, out var x);
                int.TryParse(b, out var y);
                if (twoD ? x < 1 || x > 20 || y < 1 || y > 20 : x < 1 || x > 100) {
                    Interface.Interface.Say(twoD ? "Rows and columns go from 1 to 20" : "A list has 1 to 100 items", false);
                    return;
                }
                Snapshot();
                var l = twoD ? new ListVar { name = n, rows = x, cols = y } : new ListVar { name = n, size = x };
                (twoD ? program.lists2 : program.lists).Add(l);
                BlockDefs.RegisterSetList(l, twoD);
                then?.Invoke(n);
                dialog.Close();
                BuildPalette();
                Rebuild();
                Changed();
            }
            nameField.onSubmit.AddListener(_ => Ok());
            Ui.Button(dialog.Footer, "Cancel", "Cancel", dialog.Close).Size(90f, 34f);
            Ui.Button(dialog.Footer, "OK", "OK", Ok, true).Size(90f, 34f);
            nameField.Select();
            nameField.ActivateInputField();
        }

        // ------------------------------------------------------------------ comments

        private static readonly Color NoteFill = new(0.99f, 0.92f, 0.58f), NoteHead = new(0.93f, 0.83f, 0.42f), NoteInk = new(0.22f, 0.19f, 0.1f);
        private const float NoteWidth = 200f;
        private Block focusComment;
        private Note focusNote;

        // The comments: each block's beside its stack (a line to the block), the canvas's where they were put.
        private void BuildComments() {
            foreach (var kv in views) {
                var b = kv.Key;
                if (b.comment == null || (dragging != null && dragging.All().Contains(b))) continue;
                var v = kv.Value;
                var root = v;
                for (var t = v.transform.parent; t != null && t != content; t = t.parent)
                    if (t.TryGetComponent(out BlockView up)) root = up;
                var right = 0f;
                foreach (var w in views.Values) if (Inside(w, root)) right = Mathf.Max(right, PositionOf(w).x + w.width);
                var at = PositionOf(v);
                var x = right + 28f;
                var line = Ui.Image(content, "Comment Line", NoteHead).rectTransform;
                line.anchorMin = line.anchorMax = line.pivot = new Vector2(0f, 1f);
                line.anchoredPosition = new Vector2(at.x + v.width, -(at.y + 14f));
                line.sizeDelta = new Vector2(Mathf.Max(2f, x - at.x - v.width), 2f);
                var block = b;
                var field = CommentBox(new Vector2(x, at.y), b.comment, s => block.comment = s, () => block.comment = null, null);
                if (focusComment == b) Focus(field);
            }
            foreach (var n in program.notes) {
                var note = n;
                var field = CommentBox(new Vector2(n.x, n.y), n.text, s => note.text = s, () => program.notes.Remove(note), p => {
                    note.x = Mathf.Max(0f, p.x);
                    note.y = Mathf.Max(0f, p.y);
                });
                if (focusNote == n) Focus(field);
            }
            focusComment = null;
            focusNote = null;
            marker.SetAsLastSibling();
        }

        private static void Focus(TMP_InputField field) {
            field.Select();
            field.ActivateInputField();
        }

        /// <summary>A yellow note: a strip on top (drag it to move a canvas comment; × removes it) and the text.</summary>
        private TMP_InputField CommentBox(Vector2 at, string text, Action<string> changed, Action remove, Action<Vector2> moved) {
            var box = Ui.Image(content, "Comment", NoteFill, 4f, true).rectTransform;
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, 1f);
            box.anchoredPosition = new Vector2(at.x, -at.y);
            var head = Ui.Image(box, "Head", NoteHead, 4f, true).rectTransform;
            head.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(NoteWidth, 16f));
            if (moved != null) {
                var drag = head.gameObject.AddComponent<NoteDrag>();
                drag.editor = this;
                drag.box = box;
                drag.moved = moved;
            }
            var close = Ui.Text(head, "Close", "×", 13f, NoteInk, Theme.Medium, TextAlignmentOptions.Center);
            close.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-2f, 1f), new Vector2(16f, 16f));
            close.raycastTarget = true;
            var closeButton = close.gameObject.AddComponent<Button>();
            closeButton.transition = Selectable.Transition.None;
            closeButton.onClick.AddListener(() => {
                Snapshot();
                remove();
                Rebuild();
                Changed();
            });
            Ui.Tip(close, "Remove comment");

            var area = Ui.Rect("Area", box).Fill(7, 19, 7, 5);
            area.gameObject.AddComponent<RectMask2D>();
            var label = Ui.Text(area, "Text", text ?? "", 12f, NoteInk, Theme.Regular, TextAlignmentOptions.TopLeft);
            label.rectTransform.Fill();
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            var field = box.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = label;
            field.fontAsset = Theme.Regular;
            field.pointSize = 12f;
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
            label.textWrappingMode = TextWrappingModes.Normal;
            field.caretColor = NoteInk;
            field.customCaretColor = true;
            field.selectionColor = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f);
            var nav = field.navigation;
            nav.mode = Navigation.Mode.None;
            field.navigation = nav;
            field.SetTextWithoutNotify(text ?? "");
            void Size(string s) {
                var h = label.GetPreferredValues(string.IsNullOrEmpty(s) ? "X" : s + (s.EndsWith("\n") ? "X" : ""), NoteWidth - 14f, 0f).y;
                box.sizeDelta = new Vector2(NoteWidth, Mathf.Clamp(h + 26f, 44f, 400f));
            }
            Size(text);
            field.onSelect.AddListener(_ => Snapshot());
            field.onValueChanged.AddListener(s => {
                changed(s);
                Size(s);
            });
            field.onEndEdit.AddListener(_ => {
                InputEvents.TextInput.EditEndedFrame = Time.frameCount;
                Changed();
            });
            return field;
        }

        // Drags a canvas comment by its top strip.
        private class NoteDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler {
            public BlockEditor editor;
            public RectTransform box;
            public Action<Vector2> moved;
            public void OnBeginDrag(PointerEventData e) => editor.Snapshot();
            public void OnDrag(PointerEventData e) {
                var canvas = GetComponentInParent<Canvas>();
                var scale = (canvas != null ? canvas.rootCanvas.scaleFactor : 1f) * editor.zoom;
                box.anchoredPosition += e.delta / scale;
            }
            public void OnEndDrag(PointerEventData e) {
                moved(new Vector2(box.anchoredPosition.x, -box.anchoredPosition.y));
                editor.Rebuild();
                editor.Changed();
            }
        }

        // ------------------------------------------------------------------ keys, highlights

        private void HandleKeys() {
            var kb = Keyboard.current;
            if (kb == null || InputEvents.TextInput.IsTyping || Dialog.OpenCount > 0 || Popup.IsOpen) return;
            var ctrl = kb.ctrlKey.isPressed || kb.leftCommandKey.isPressed;
            if (!ctrl) return;
            if (kb.zKey.wasPressedThisFrame) {
                if (kb.shiftKey.isPressed) Redo();
                else Undo();
            }
            else if (kb.yKey.wasPressedThisFrame) Redo();
        }

        /// <summary>The blocks running now (lit) and the one an error stopped on (red), from the Python lines.</summary>
        public void ShowLines(IEnumerable<int> running, int errorLine) {
            var lit = new HashSet<Block>();
            Block error = null;
            if (code != null) {
                foreach (var line in running) {
                    var b = BlockAt(line);
                    if (b != null) lit.Add(b);
                }
                error = BlockAt(errorLine);
            }
            foreach (var kv in views) kv.Value.SetHighlight(kv.Key == error ? 2 : lit.Contains(kv.Key) ? 1 : 0);
        }

        /// <summary>The block a line of the saved program (header included) came from.</summary>
        public Block BlockAt(int line) {
            var i = line - 1 - BlockCode.HeaderLines;
            return code != null && i >= 0 && i < code.lineBlocks.Count ? code.lineBlocks[i] : null;
        }

        public bool Dragging => dragging != null;
    }
}
