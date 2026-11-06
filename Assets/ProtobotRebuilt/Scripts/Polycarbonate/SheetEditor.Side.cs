using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Polycarb {
    // V2: the panel on the right, as SolidWorks' PropertyManager: the tool and how to use it, its options (polygon
    // sides, fillet radius, offset distance, pattern counts, move / rotate / scale and Apply), Convert Entities'
    // choices, what's selected with the relations that suit it (Add Relations) and the ones it has (each removable),
    // the selected dimension, and the sketch: fully or under defined, snapping, open ends, the plastic rules.
    public partial class SheetEditor {
        private RectTransform sidePanel;   // the panel; `side` is its scrolling content

        private void BuildSidePanel() {
            sidePanel = Ui.Panel(transform, "Side", Theme.Panel, 10f).rectTransform;
            sidePanel.anchorMin = new Vector2(1f, 0f);
            sidePanel.anchorMax = new Vector2(1f, 1f);
            sidePanel.pivot = new Vector2(1f, 1f);
            sidePanel.offsetMin = new Vector2(-330f, 40f);
            sidePanel.offsetMax = new Vector2(-14f, -136f);
            var viewport = Ui.Rect("Viewport", sidePanel).Fill(2, 2, 2, 2);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            side = Ui.Rect("Content", viewport);
            side.anchorMin = new Vector2(0f, 1f);
            side.anchorMax = new Vector2(1f, 1f);
            side.pivot = new Vector2(0.5f, 1f);
            side.sizeDelta = Vector2.zero;
            Ui.Column(side, 6f, new RectOffset(12, 12, 10, 12));
            side.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = side;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
        }

        private void BuildSide() {
            if (side == null) return;
            for (var i = side.childCount - 1; i >= 0; i--) Destroy(side.GetChild(i).gameObject);
            if (stage == Stage.Piece) {
                BuildPieceSide();
                return;
            }
            var def = Def(tool);
            Dialog.Heading(side, def.name);
            Wrapped(def.help, Theme.TextDim);
            ToolOptionsPanel();

            if (selectedDimension != null && sketch.dimensions.Contains(selectedDimension)) {
                Dialog.Heading(side, "Dimension");
                var row = Dialog.Row(side, selectedDimension.kind.ToString(), 30f, 110f);
                var keep = selectedDimension;
                Ui.Text(row, "Value", keep.kind == DimensionKind.Angle ? sketch.Measure(keep).ToString("0.##", CultureInfo.InvariantCulture) + "°" : Inch.Format(sketch.Measure(keep)), Theme.FontBody, Theme.Text).Size(-1, 28f, 1f);
                var delete = Ui.Button(side, "Delete dimension", "Delete this dimension", () => DeleteDimension(keep), false, 5f, Theme.FontSmall);
                delete.Size(-1, 28f);
            }

            var count = selectedEntities.Count + selectedPoints.Count;
            if (count > 0 && tool == Tool.Select) {
                Dialog.Heading(side, count == 1 && selectedEntities.Count == 1 ? "Selected " + Describe(selectedEntities[0]) : "Selected: " + count + (count == 1 ? " item" : " items"));
                if (selectedEntities.Count == 1 && selectedPoints.Count == 0) {
                    var selected = selectedEntities[0];
                    foreach (var (label, kind) in SizesOf(selected)) {
                        var row = Dialog.Row(side, label, 32f, 90f);
                        var dimension = new SketchDimension { kind = kind, entity = selected.id };
                        var field = Ui.Field(row, label, Inch.Format(sketch.Measure(dimension), false), text => {
                            if (Inch.TryParse(text, out var value)) {
                                Remember();
                                Resize(dimension, value);
                            }
                            else Interface.Interface.Say("Type a size like 1 1/2 or 1.5", false);
                        }, Theme.FontBody, TextAlignmentOptions.Center);
                        field.Size(110f, 30f);
                        Ui.Text(row, "Unit", "in", Theme.FontSmall, Theme.TextFaint).Size(-1, 30f);
                    }
                }
                else if (selectedPoints.Count == 1 && selectedEntities.Count == 0) {
                    var point = sketch.P(selectedPoints[0]);
                    Wrapped("At X " + Inch.Format(point.x) + ", Y " + Inch.Format(point.y), Theme.Text);
                }
                // Add Relations: the ones that suit what's selected.
                var fits = RelationButtons.Where(r => RelationFits(r.kind, out _)).ToList();
                Ui.Text(side, "Add", "Add relations", Theme.FontSmall, Theme.Text, Theme.Medium).Size(-1, 20f);
                if (fits.Count == 0) Wrapped("None suit this selection: pick two lines, a point and a line, two circles...", Theme.TextFaint);
                else {
                    RectTransform row = null;
                    for (var i = 0; i < fits.Count; i++) {
                        if (i % 3 == 0) {
                            row = Ui.Rect("Relations " + i, side);
                            Ui.Row(row, 4f);
                            row.Size(-1, 30f);
                        }
                        var relation = fits[i];
                        var button = Ui.Button(row, relation.name, relation.name, () => AddRelation(relation.kind), false, 5f, Theme.FontSmall);
                        button.Size(94f, 28f);
                        Ui.Tip(button, relation.name, null, relation.help);
                    }
                }
                // The relations it has, each removable.
                var related = sketch.relations.Where(r => selectedEntities.Any(e => r.e1 == e.id || r.e2 == e.id) || selectedPoints.Any(p => r.p1 == p || r.p2 == p)).ToList();
                if (related.Count > 0) {
                    Ui.Text(side, "Existing", "Existing relations", Theme.FontSmall, Theme.Text, Theme.Medium).Size(-1, 20f);
                    foreach (var relation in related) {
                        var row = Dialog.Row(side, "", 28f, 0f);
                        Ui.Text(row, "Kind", relation.kind.ToString(), Theme.FontSmall, Theme.Text).Size(-1, 28f, 1f);
                        var keep = relation;
                        var remove = Ui.Button(row, "Remove", "Remove", () => {
                            Remember();
                            sketch.relations.Remove(keep);
                            Changed();
                            Interface.Interface.Say("Removed the " + keep.kind.ToString().ToLowerInvariant() + " relation", false);
                        }, false, 5f, Theme.FontSmall);
                        remove.Size(80f, 26f);
                    }
                }
                var construction = Ui.Button(side, "Construction", selectedEntities.Count > 0 && selectedEntities.All(e => e.construction) ? "Make cut lines" : "For construction", ToggleConstruction, false, 5f, Theme.FontSmall);
                construction.Size(-1, 28f);
            }

            Dialog.Heading(side, "Sketch");
            var free = freePoints.Count + freeRadii.Count;
            if (sketch.entities.Count(e => !e.projected) == 0) Wrapped("Empty: pick a sketch tool above to start", Theme.TextFaint);
            else if (fullyDefined) Wrapped("Fully defined: every size and place is set (white)", Theme.Success);
            else Wrapped("Under defined (blue can still move). Add dimensions and relations to the origin and between things to set it, as in SolidWorks.", UnderDefined);
            ToolOptions.Switch(side, "Snap to 1/16\" grid (or hold Ctrl)", gridSnap, on => gridSnap = on);
            var open = sketch.OpenEnds();
            if (open.Count > 0)
                Wrapped(open.Count + (open.Count == 1 ? " open end" : " open ends") + " (red): the outline isn't closed there, so it doesn't make an area yet. Draw to it or drag the end onto the point it should meet.", Theme.Warning);
            else if (regions != null && regions.Areas.Any())
                Wrapped(regions.Areas.Count() + (regions.Areas.Count() == 1 ? " closed area" : " closed areas") + ": Make piece when you're ready.", Theme.Success);
            if (!inPlace) {
                Dialog.Heading(side, "Plastic in this build");
                RulesBlock();
            }
        }

        private string Describe(SketchEntity e) =>
            (e.projected ? "converted " : e.construction ? "construction " : "") + (e.kind == EntityKind.Point ? "point" : e.kind.ToString().ToLowerInvariant());

        // A number field with its label, set when typed.
        private void NumberField(string label, float value, System.Action<float> set, string unit = "in", bool inches = true) {
            var row = Dialog.Row(side, label, 32f, 120f);
            var shown = inches ? Inch.Format(value, false) : value.ToString("0.###", CultureInfo.InvariantCulture);
            var field = Ui.Field(row, label, shown, text => {
                float parsed;
                var ok = inches ? Inch.TryParse(text, out parsed) : float.TryParse(text.Replace("°", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
                if (!ok) {
                    Interface.Interface.Say(inches ? "Type a size like 1 1/2 or 1.5" : "Type a number", false);
                    return;
                }
                set(parsed);
                BuildSide();
            }, Theme.FontBody, TextAlignmentOptions.Center);
            field.Size(90f, 30f);
            Ui.Text(row, "Unit", unit, Theme.FontSmall, Theme.TextFaint).Size(-1, 30f);
        }

        private void ApplyButton(string label) {
            var apply = Ui.Button(side, "Apply", label, ApplyOperation, true, 6f);
            apply.Size(-1, 32f);
            Ui.Tip(apply, label, "Enter");
        }

        private void SelectionCount() {
            var n = OperationSource().Count;
            Wrapped(n == 0 ? "Nothing selected yet: click what it acts on (or select it first with Select)." : n + (n == 1 ? " entity selected" : " entities selected"), n == 0 ? Theme.Warning : Theme.Text);
        }

        private void ToolOptionsPanel() {
            switch (tool) {
                case Tool.Polygon:
                    NumberField("Sides", polygonSides, v => polygonSides = Mathf.Clamp(Mathf.RoundToInt(v), 3, 40), "", false);
                    break;
                case Tool.Fillet:
                    NumberField("Radius", filletRadius, v => filletRadius = Mathf.Max(0.005f, v));
                    break;
                case Tool.Chamfer:
                    NumberField("Distance", chamferDistance, v => chamferDistance = Mathf.Max(0.005f, v));
                    break;
                case Tool.Offset:
                    NumberField("Distance", offsetDistance, v => offsetDistance = Mathf.Max(0.005f, v));
                    ToolOptions.Switch(side, "Select chain (the whole connected outline)", offsetChain, on => offsetChain = on);
                    break;
                case Tool.Mirror: {
                    SelectionCount();
                    var axis = Ui.Button(side, "Axis", mirrorAxis != null ? "Mirror about: the line picked" : opStep == OpStep.Axis ? "Click the line to mirror about…" : "Mirror about (pick the line)", () => {
                        if (OperationSource().Count == 0) {
                            Interface.Interface.Say("Select what to mirror first", false);
                            return;
                        }
                        opStep = OpStep.Axis;
                        BuildSide();
                        Interface.Interface.Say("Click the line to mirror about", true);
                    }, opStep == OpStep.Axis, 6f, Theme.FontSmall);
                    axis.Size(-1, 30f);
                    break;
                }
                case Tool.LinearPattern:
                    SelectionCount();
                    NumberField("Direction", patternAngle, v => patternAngle = v, "°", false);
                    NumberField("Copies", pattern1, v => pattern1 = Mathf.Clamp(Mathf.RoundToInt(v), 1, 100), "", false);
                    NumberField("Spacing", spacing1, v => spacing1 = v);
                    NumberField("Rows (across)", pattern2, v => pattern2 = Mathf.Clamp(Mathf.RoundToInt(v), 1, 100), "", false);
                    NumberField("Row spacing", spacing2, v => spacing2 = v);
                    ApplyButton("Apply the pattern");
                    break;
                case Tool.CircularPattern:
                    SelectionCount();
                    CenterButton("Centre: the origin");
                    NumberField("Copies", circularCount, v => circularCount = Mathf.Clamp(Mathf.RoundToInt(v), 2, 200), "", false);
                    NumberField("Spread over", circularAngle, v => circularAngle = Mathf.Clamp(v, -360f, 360f), "°", false);
                    ApplyButton("Apply the pattern");
                    break;
                case Tool.Move:
                case Tool.Copy:
                    SelectionCount();
                    var from = Ui.Button(side, "From", opStep == OpStep.Base ? "Click the point to move from…" : opStep == OpStep.Target ? "Click where it goes…" : "Pick from and to points", () => {
                        opStep = OpStep.Base;
                        BuildSide();
                    }, opStep == OpStep.Base || opStep == OpStep.Target, 6f, Theme.FontSmall);
                    from.Size(-1, 30f);
                    NumberField("ΔX", moveX, v => moveX = v);
                    NumberField("ΔY", moveY, v => moveY = v);
                    ApplyButton(tool == Tool.Move ? "Move by these" : "Copy by these");
                    break;
                case Tool.Rotate:
                    SelectionCount();
                    CenterButton("Centre: the middle of what's selected");
                    NumberField("Angle", rotateAngle, v => rotateAngle = v, "°", false);
                    ToolOptions.Switch(side, "Keep the original (copy)", transformCopy, on => transformCopy = on);
                    ApplyButton("Rotate");
                    break;
                case Tool.Scale:
                    SelectionCount();
                    CenterButton("Centre: the middle of what's selected");
                    NumberField("Scale", scaleFactor, v => scaleFactor = Mathf.Max(0.001f, v), "×", false);
                    ToolOptions.Switch(side, "Keep the original (copy)", transformCopy, on => transformCopy = on);
                    ApplyButton("Scale");
                    break;
                case Tool.Project: {
                    ToolOptions.Segmented(side, new[] { "Edge or hole", "Face", "Whole part" }, (int)projectMode, i => {
                        projectMode = (ProjectMode)i;
                        BuildSide();
                    });
                    ToolOptions.Switch(side, "Edges as cut lines (else reference)", projectAsCut, on => projectAsCut = on);
                    ToolOptions.Switch(side, "Holes become holes in the piece", !holesAsReference, on => holesAsReference = !on);
                    var face = Ui.Button(side, "Face", "Convert the face I'm sketching on", ProjectFaceSketchedOn, false, 6f, Theme.FontSmall);
                    face.Size(-1, 30f);
                    Ui.Tip(face, "Convert this face", null, "Its outline and holes come into the sketch.");
                    NumberField("Within", projectDepth, v => projectDepth = Mathf.Max(0f, v));
                    var near = Ui.Button(side, "Near", "Convert everything near the sketch", ProjectEverythingNear, false, 6f, Theme.FontSmall);
                    near.Size(-1, 30f);
                    Ui.Tip(near, "Everything near", null, "Every part's edges and holes lying within that distance of the sketch plane come in.");
                    var count = sketch.entities.Count(e => e.projected);
                    if (count > 0) {
                        var clear = Ui.Button(side, "Clear", "Remove converted geometry (" + count + ")", () => {
                            Remember();
                            foreach (var e in sketch.entities.Where(e => e.projected).ToList()) sketch.Remove(e);
                            Changed();
                            Interface.Interface.Say("Removed the converted geometry");
                        }, false, 6f, Theme.FontSmall);
                        clear.Size(-1, 30f);
                    }
                    break;
                }
            }
        }

        private void CenterButton(string fallback) {
            var label = opStep == OpStep.Center ? "Click the centre…" : opCenterSet ? "Centre: X " + Inch.Format(opCenter.x) + ", Y " + Inch.Format(opCenter.y) : fallback;
            var row = Ui.Rect("Centre", side);
            Ui.Row(row, 4f);
            row.Size(-1, 30f);
            var pick = Ui.Button(row, "Pick", label, () => {
                opStep = OpStep.Center;
                BuildSide();
            }, opStep == OpStep.Center, 6f, Theme.FontSmall);
            pick.Size(-1, 30f, 1f);
            if (opCenterSet) {
                var reset = Ui.Button(row, "Reset", "×", () => {
                    opCenterSet = false;
                    BuildSide();
                }, false, 5f, Theme.FontBody);
                reset.Size(30f, 30f);
            }
        }

        private void Wrapped(string text, Color color) {
            var label = Ui.Text(side, "Text", text, Theme.FontSmall, color);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.Size(-1, Mathf.Max(18f, label.GetPreferredValues(text, 280f, 0f).y + 2f));
        }

        // Older calls give a height; it's measured now.
        private void Wrapped(string text, Color color, float height) => Wrapped(text, color);
    }
}
