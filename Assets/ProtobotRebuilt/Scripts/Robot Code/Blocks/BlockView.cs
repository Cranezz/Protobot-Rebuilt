using System.Collections.Generic;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Protobot.RobotCode.Blocks {
    /// <summary>V2 (Blocks): one block on the canvas or in the palette: its words and slots laid out in a row, the
    /// blocks dropped in its slots, the blocks in its mouths and the block under it (children, so a stack moves as
    /// one). Laid out by hand (C mouths and slots grow with what's in them).</summary>
    public class BlockView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler {
        public const float Font = 13f, PadX = 9f, Gap = 5f, SlotH = 22f, StackMinH = 40f, ReporterMinH = 28f, ArmW = 16f, ArmH = 22f, MouthMin = 24f, ElseH = 30f;
        public static readonly Color SlotText = new(0.27f, 0.27f, 0.3f);

        public sealed class Part {
            public TextMeshProUGUI label;
            public int slot = -1;
            public RectTransform rect;
            public TMP_InputField field;
            public TextMeshProUGUI text;
            public BlockView input;
            public Vector2 pos, size;
        }

        public BlockEditor editor;
        public Block block;
        public bool palette;
        public RectTransform rect;
        public BlockGraphic graphic;
        public readonly List<Part> parts = new();
        public BlockView next;
        public readonly List<BlockView> mouths = new();
        public TextMeshProUGUI elseLabel, toggleText;
        public RectTransform toggleRect;
        public float width, height;
        public readonly List<float> mouthTops = new(), mouthHeights = new();
        public int highlight;   // 0 none, 1 running, 2 error

        public BlockDef Def => block.Def;
        public Color Color => BlockDefs.Category(Def.category).color;
        private Color Dark => Color.Lerp(Color, Color.black, 0.22f);

        // ------------------------------------------------------------------ making

        public static BlockView Make(BlockEditor editor, Block block, Transform parent, bool palette) {
            var def = block.Def;
            var rect = Ui.Rect("Block " + block.type, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.gameObject.AddComponent<CanvasRenderer>();
            var view = rect.gameObject.AddComponent<BlockView>();
            view.editor = editor;
            view.block = block;
            view.palette = palette;
            view.rect = rect;
            view.graphic = rect.gameObject.AddComponent<BlockGraphic>();
            view.graphic.raycastTarget = true;
            var slot = 0;
            foreach (var bp in def.parts) {
                var part = new Part();
                if (bp.label != null) {
                    part.label = view.Label(bp.label);
                }
                else {
                    part.slot = slot;
                    view.MakeSlot(part, bp.slot, slot);
                    slot++;
                }
                view.parts.Add(part);
            }
            if (def.shape == BlockShape.CElse) view.elseLabel = view.Label(def.elseLabel);
            if (def.hasToggle) view.MakeToggle();
            // What's in it: slots' blocks, mouths, the block under it.
            foreach (var part in view.parts) {
                if (part.slot < 0 || part.slot >= block.inputs.Count || block.inputs[part.slot] == null) continue;
                part.input = Make(editor, block.inputs[part.slot], rect, palette);
            }
            for (var m = 0; m < def.Mouths; m++)
                view.mouths.Add(m < block.mouths.Count && block.mouths[m] != null ? Make(editor, block.mouths[m], rect, palette) : null);
            if (block.next != null) view.next = Make(editor, block.next, rect, palette);
            if (editor != null) editor.Register(view);
            return view;
        }

        private TextMeshProUGUI Label(string text) {
            var t = Ui.Text(rect, "Label", text, Font, Color.white, Theme.Medium, TextAlignmentOptions.MidlineLeft);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = t.rectTransform.pivot = new Vector2(0f, 1f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private void MakeSlot(Part part, Slot slot, int index) {
            var r = Ui.Rect("Slot " + index, rect);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            part.rect = r;
            var value = index < block.fields.Count ? block.fields[index] : "";
            switch (slot.kind) {
                case SlotKind.Number:
                case SlotKind.Text: {
                    var hit = r.gameObject.AddComponent<Image>();
                    hit.color = new Color(1f, 1f, 1f, 0f);
                    var area = Ui.Rect("Area", r).Fill(5, 0, 5, 0);
                    area.gameObject.AddComponent<RectMask2D>();
                    part.text = Ui.Text(area, "Text", value, Font, SlotText, Theme.Medium, TextAlignmentOptions.Center);
                    part.text.rectTransform.Fill();
                    part.text.textWrappingMode = TextWrappingModes.NoWrap;
                    part.text.overflowMode = TextOverflowModes.Overflow;
                    if (palette) break;   // (a palette block drags from anywhere)
                    var field = r.gameObject.AddComponent<TMP_InputField>();
                    field.textViewport = area;
                    field.textComponent = part.text;
                    field.fontAsset = Theme.Medium;
                    field.pointSize = Font;
                    field.caretColor = SlotText;
                    field.customCaretColor = true;
                    field.selectionColor = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f);
                    field.contentType = slot.kind == SlotKind.Number ? TMP_InputField.ContentType.DecimalNumber : TMP_InputField.ContentType.Standard;
                    field.SetTextWithoutNotify(value);
                    var nav = field.navigation;
                    nav.mode = Navigation.Mode.None;
                    field.navigation = nav;
                    field.onSelect.AddListener(_ => editor?.BeginEdit());
                    field.onValueChanged.AddListener(v => {
                        while (block.fields.Count <= index) block.fields.Add("");
                        block.fields[index] = v;
                        editor?.Relayout(this);
                    });
                    field.onEndEdit.AddListener(_ => {
                        InputEvents.TextInput.EditEndedFrame = Time.frameCount;
                        editor?.Changed();
                    });
                    part.field = field;
                    break;
                }
                case SlotKind.Dropdown:
                case SlotKind.Device:
                case SlotKind.Variable: {
                    var hit = r.gameObject.AddComponent<Image>();
                    hit.color = new Color(1f, 1f, 1f, 0f);
                    hit.raycastTarget = !palette;
                    part.text = Ui.Text(r, "Text", "", Font, Color.white, Theme.Medium, TextAlignmentOptions.MidlineLeft);
                    part.text.rectTransform.Fill(7, 0, 18, 0);
                    part.text.textWrappingMode = TextWrappingModes.NoWrap;
                    part.text.overflowMode = TextOverflowModes.Overflow;
                    part.text.raycastTarget = false;
                    if (palette) break;
                    var button = r.gameObject.AddComponent<Button>();
                    button.transition = Selectable.Transition.None;
                    button.onClick.AddListener(() => editor?.OpenDropdown(this, index, r));
                    break;
                }
                case SlotKind.Name:
                    part.text = Label("");
                    break;
                case SlotKind.Boolean:
                    break;
            }
            RefreshText(part);
        }

        private void MakeToggle() {
            toggleRect = Ui.Rect("Toggle", rect);
            toggleRect.anchorMin = toggleRect.anchorMax = toggleRect.pivot = new Vector2(0f, 1f);
            var hit = toggleRect.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = !palette;
            toggleText = Ui.Text(toggleRect, "Text", "", Font - 1f, Color.white, Theme.Medium, TextAlignmentOptions.MidlineLeft);
            toggleText.rectTransform.Fill(14, 0, 0, 0);
            toggleText.textWrappingMode = TextWrappingModes.NoWrap;
            toggleText.overflowMode = TextOverflowModes.Overflow;
            toggleText.raycastTarget = false;
            if (palette) return;
            var button = toggleRect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => editor?.Toggle(this));
            Ui.Tip(toggleRect, Def.toggleLabel);
        }

        /// <summary>A dropdown's or name's shown text from the block's value.</summary>
        public void RefreshText(Part part) {
            if (part.slot < 0 || part.text == null) return;
            var slot = Def.slots[part.slot];
            var value = part.slot < block.fields.Count ? block.fields[part.slot] : "";
            switch (slot.kind) {
                case SlotKind.Dropdown: part.text.text = BlockDefs.OptionLabel(slot.key, value); break;
                case SlotKind.Device: part.text.text = value.Length > 0 ? value : "(no " + BlockDefs.KindName(slot.key) + ")"; break;
                case SlotKind.Variable:
                case SlotKind.Name: part.text.text = value; break;
                default: if (part.field == null) part.text.text = value; break;
            }
        }

        // ------------------------------------------------------------------ layout

        private static float Width(TextMeshProUGUI t, string text) => t == null ? 0f : t.GetPreferredValues(text ?? "", 0f, 0f).x;

        /// <summary>Lays out this block (its slots' blocks and mouths too, not the block under it); its own size.</summary>
        public void Layout() {
            var def = Def;
            var reporter = def.shape == BlockShape.Reporter;
            var boolean = def.shape == BlockShape.Boolean;
            var x = boolean ? 12f : reporter ? 10f : PadX;
            var rowH = 16f;
            foreach (var p in parts) {
                if (p.label != null) {
                    p.size = new Vector2(Width(p.label, p.label.text), 18f);
                }
                else {
                    var slot = def.slots[p.slot];
                    if (p.input != null) {
                        p.input.Layout();
                        p.size = new Vector2(p.input.width, p.input.height);
                    }
                    else switch (slot.kind) {
                        case SlotKind.Number: p.size = new Vector2(Mathf.Max(28f, Width(p.text, p.text.text) + 16f), SlotH); break;
                        case SlotKind.Text: p.size = new Vector2(Mathf.Max(30f, Width(p.text, p.text.text) + 14f), SlotH); break;
                        case SlotKind.Boolean: p.size = new Vector2(40f, SlotH); break;
                        case SlotKind.Name: p.size = new Vector2(Width(p.text, p.text.text), 18f); break;
                        default: p.size = new Vector2(Width(p.text, p.text.text) + 26f, SlotH); break;
                    }
                }
                p.pos.x = x;
                x += p.size.x + Gap;
                rowH = Mathf.Max(rowH, p.size.y);
            }
            if (toggleRect != null) {
                var w = 14f + (block.toggle ? Width(toggleText, def.toggleLabel) + 4f : 0f);
                toggleText.text = block.toggle ? def.toggleLabel : "";
                toggleRect.anchoredPosition = new Vector2(x, 0f);
                toggleRect.sizeDelta = new Vector2(w, 20f);
                x += w + Gap;
            }
            x += (boolean ? 12f : reporter ? 10f : PadX) - Gap;

            var top = def.shape == BlockShape.Hat ? BlockGraphic.HatBump : 0f;
            float rowArea;
            if (reporter || boolean) {
                rowArea = Mathf.Max(ReporterMinH, rowH + 6f);
                height = rowArea;
                width = Mathf.Max(x, boolean ? 40f : 30f);
            }
            else {
                rowArea = Mathf.Max(StackMinH, rowH + 14f);
                height = top + rowArea;
                width = Mathf.Max(x, def.IsC ? 120f : 40f);
            }
            var center = top + rowArea / 2f;
            foreach (var p in parts) {
                p.pos.y = center - p.size.y / 2f;
                var target = p.label != null ? p.label.rectTransform : p.input != null ? p.input.rect : p.rect;
                if (p.label != null || p.rect != null || p.input != null) {
                    target.anchoredPosition = new Vector2(p.pos.x, -p.pos.y);
                    if (p.input == null) target.sizeDelta = p.size;
                }
                if (p.input != null && p.rect != null) p.rect.sizeDelta = Vector2.zero;
                if (p.slot >= 0 && def.slots[p.slot].kind == SlotKind.Name && p.text != null) {
                    p.text.rectTransform.anchoredPosition = new Vector2(p.pos.x, -p.pos.y);
                    p.text.rectTransform.sizeDelta = p.size;
                }
            }
            if (toggleRect != null) toggleRect.anchoredPosition = new Vector2(toggleRect.anchoredPosition.x, -(center - 10f));

            // C mouths.
            mouthTops.Clear();
            mouthHeights.Clear();
            if (def.IsC) {
                var y = top + rowArea;
                for (var m = 0; m < def.Mouths; m++) {
                    if (m == 1) {
                        if (elseLabel != null) {
                            elseLabel.rectTransform.anchoredPosition = new Vector2(PadX, -(y + ElseH / 2f - 9f));
                            elseLabel.rectTransform.sizeDelta = new Vector2(60f, 18f);
                        }
                        y += ElseH;
                    }
                    var inner = mouths[m];
                    var h = inner != null ? Mathf.Max(MouthMin, inner.LayoutChain()) : MouthMin;
                    if (inner != null) inner.rect.anchoredPosition = new Vector2(ArmW, -y);
                    mouthTops.Add(y);
                    mouthHeights.Add(h);
                    y += h;
                }
                height = y + ArmH;
            }
            rect.sizeDelta = new Vector2(width, height);
            Draw();
        }

        /// <summary>Lays out this block and the ones under it; the stack's height.</summary>
        public float LayoutChain() {
            Layout();
            var total = height;
            if (next != null) {
                next.rect.anchoredPosition = new Vector2(0f, -height);
                total += next.LayoutChain();
            }
            return total;
        }

        public float ChainHeight() => height + (next != null ? next.ChainHeight() : 0f);

        // ------------------------------------------------------------------ drawing

        public void Draw() {
            var def = Def;
            graphic.Clear();
            var edge = highlight == 2 ? Theme.Danger : highlight == 1 ? new Color(1f, 0.9f, 0.35f) : Dark;
            var edgeWidth = highlight > 0 ? 3f : 1f;
            List<Vector2> outline = def.shape switch {
                BlockShape.Reporter => BlockGraphic.Pill(0f, 0f, width, height),
                BlockShape.Boolean => BlockGraphic.Hexagon(0f, 0f, width, height),
                BlockShape.C or BlockShape.CElse or BlockShape.Forever =>
                    BlockGraphic.CShape(width, mouthTops.Count > 0 ? mouthTops[0] : height, ArmH, ArmW, mouthTops, mouthHeights, null, def.shape != BlockShape.Forever),
                _ => BlockGraphic.StackShape(width, height, def.shape != BlockShape.Hat, def.HasNext, def.shape == BlockShape.Hat),
            };
            graphic.Add(outline, Color, edge, edgeWidth);
            var slotEdge = new Color(0f, 0f, 0f, 0.12f);
            foreach (var p in parts) {
                if (p.slot < 0 || p.input != null) continue;
                var s = def.slots[p.slot];
                switch (s.kind) {
                    case SlotKind.Number: graphic.Add(BlockGraphic.Pill(p.pos.x, p.pos.y, p.size.x, p.size.y), Color.white, slotEdge, -1f); break;
                    case SlotKind.Text: graphic.Add(BlockGraphic.RoundRect(p.pos.x, p.pos.y, p.size.x, p.size.y, 3f), Color.white, slotEdge, -1f); break;
                    case SlotKind.Boolean: graphic.Add(BlockGraphic.Hexagon(p.pos.x, p.pos.y, p.size.x, p.size.y), Color.Lerp(Color, Color.black, 0.3f), Color.clear, -1f); break;
                    case SlotKind.Dropdown:
                    case SlotKind.Device:
                    case SlotKind.Variable:
                        graphic.Add(BlockGraphic.RoundRect(p.pos.x, p.pos.y, p.size.x, p.size.y, 4f), Color.Lerp(Color, Color.black, 0.15f), Dark, -1f);
                        graphic.Add(BlockGraphic.Arrow(p.pos.x + p.size.x - 10f, p.pos.y + p.size.y / 2f, 3.5f), Color.white, Color.clear, -1f);
                        break;
                }
            }
            if (toggleRect != null) {
                var tp = new Vector2(toggleRect.anchoredPosition.x + 5f, -toggleRect.anchoredPosition.y + 10f);
                var pts = block.toggle
                    ? new List<Vector2> { new(tp.x + 6f, tp.y - 5f), new(tp.x + 6f, tp.y + 5f), new(tp.x - 1f, tp.y) }
                    : new List<Vector2> { new(tp.x, tp.y - 5f), new(tp.x, tp.y + 5f), new(tp.x + 7f, tp.y) };
                graphic.Add(pts, Color.white, Color.clear, -1f);
            }
        }

        public void SetHighlight(int value) {
            if (highlight == value) return;
            highlight = value;
            Draw();
        }

        // ------------------------------------------------------------------ input

        public void OnBeginDrag(PointerEventData e) {
            if (e.button != PointerEventData.InputButton.Left || editor == null) return;
            editor.BeginDrag(this, e);
        }

        public void OnDrag(PointerEventData e) { }   // (the editor follows the mouse itself: views are remade while dragging)

        public void OnEndDrag(PointerEventData e) { }

        public void OnPointerClick(PointerEventData e) {
            if (editor == null || e.dragging) return;
            if (e.button == PointerEventData.InputButton.Right && !palette) editor.BlockMenu(this, e.position);
        }
    }
}
