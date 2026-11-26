using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Motion {
    // V2: the chain generator. Click sprockets in the order the chain goes round them (any number, all of one chain
    // size, axles parallel, in one plane); the chain goes round the outside of the loop, and clicking a sprocket again
    // wraps it from the other side (a serpentine: an idler pushing the chain in, or a sprocket driven backwards). The
    // loop is drawn as you go, with its links, length and slack (a chain is a whole number of links: what's over hangs
    // in the longest span). Enter or Make chain makes it (or changes the chain being edited); Backspace takes the last
    // sprocket off; Esc stops.
    public class ChainTool : MonoBehaviour {
        private static ChainTool active;
        public static bool Active => active != null;

        private class Pick {
            public GameObject part;
            public Vector3 center, axis;
            public int teeth;       // V2: 0 = a smooth idler (a spacer)
            public float surface;   // V2: an idler's outside radius
            public bool flipped;
            public bool Idler => teeth <= 0;
        }

        private readonly List<Pick> picks = new();
        private ChainFamily family;
        private string style = "chain";
        private ChainLoop editing;
        private RectTransform window, body;
        private int shown = -1;
        private ChainPath preview;
        private Quaternion frame;
        private Vector3 origin;

        public static void Begin(GameObject first = null, ChainLoop edit = null) {
            if (Sandbox.Running) return;
            if (active != null) Destroy(active.gameObject);
            if (Placement.IsPlacing) Placement.Instance.StopPlacing();
            MotorControls.Release();
            foreach (var manager in FindObjectsByType<SelectionSystem.SelectionManager>(FindObjectsSortMode.None)) manager.ClearCurrent();
            active = Ui.Rect("Chain Tool", Interface.Interface.Root).Fill().gameObject.AddComponent<ChainTool>();
            active.editing = edit;
            active.Build();
            if (edit != null) {
                active.family = edit.family;
                active.style = edit.style;
                foreach (var (part, wrap) in edit.Sprockets().OrderBy(s => s.wrap)) {
                    var (center, axis) = ChainLoop.SprocketFrame(part);
                    var w = edit.wraps[wrap];
                    active.picks.Add(new Pick { part = part, center = center, axis = axis, teeth = w.teeth, surface = w.teeth > 0 ? 0f : w.radius - edit.family.IdlerRadius(0f) });
                }
                // Keep the ways it had: flipped where it goes the other way from the loop's own.
                active.Refresh();
                var ways = active.Ways();
                foreach (var (part, wrap) in edit.Sprockets()) {
                    var pick = active.picks.FirstOrDefault(p => p.part == part);
                    var index = active.picks.IndexOf(pick);
                    if (pick != null && index < ways.Count && ways[index] != edit.wraps[wrap].way) pick.flipped = true;
                }
                Interface.Interface.Say("Editing the chain: click sprockets to add them or flip their side, Enter to keep it", true);
            }
            else if (first != null) active.Click(first);
            else Interface.Interface.Say("Chain: click the sprockets in the order the chain goes round them", true);
            active.Refresh();
        }

        public static void Stop(string message = "Chain tool closed") {
            if (active == null) return;
            Destroy(active.gameObject);
            active = null;
            Interface.Interface.Say(message, true);
        }

        private void OnDestroy() {
            ToolBlock.Pop();   // V2: counted, so tools opening and closing together don't leave it on
            if (active == this) active = null;
        }

        // ------------------------------------------------------------------ window

        private const float Width = 460f;
        private Button make;

        private void Build() {
            ToolBlock.Push();
            window = Ui.Panel(transform, "Window", Theme.Panel, 10f).rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0f, 1f);
            window.pivot = new Vector2(0f, 1f);
            window.anchoredPosition = new Vector2(76f, -(Layout.TopBarBottom + 60f));
            window.sizeDelta = new Vector2(Width, 0f);
            Ui.Column(window, 10f, new RectOffset(14, 14, 12, 14));
            window.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            window.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            body = Ui.Rect("Body", window);
            Ui.Column(body, 8f);
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var buttons = Ui.Rect("Buttons", window);
            Ui.Row(buttons, 8f, null, TextAnchor.MiddleRight);
            buttons.Size(-1, 32f);
            var cancel = Ui.Button(buttons, "Cancel", "Cancel", () => Stop(), false, 6f);
            cancel.Size(-1, 30f);
            make = Ui.Button(buttons, "Make", editing != null ? "Keep changes" : "Make chain", Make, true, 6f);
            make.Size(-1, 30f);
            Ui.Tip(make, editing != null ? "Keep changes" : "Make chain", "Enter", "Makes the chain round the sprockets picked.");
        }

        private int edits;

        private void Refresh() {
            edits++;
            for (var i = body.childCount - 1; i >= 0; i--) Destroy(body.GetChild(i).gameObject);
            var ways = Ways();
            UpdatePreview(ways);
            var ready = picks.Count >= 2 && preview != null && preview.Valid;
            if (make != null) {
                // Greyed out until the loop can be made (Enter still says why not).
                make.interactable = ready;
                make.gameObject.GetOrAdd<CanvasGroup>().alpha = ready ? 1f : 0.4f;
            }

            // Title, and the chain size once a sprocket is picked.
            var head = Ui.Rect("Head", body);
            Ui.Row(head, 8f);
            head.Size(-1, 28f);
            var title = Ui.Text(head, "Title", editing != null ? "Edit chain" : "Make a chain", Theme.FontTitle, Theme.Text, Theme.Medium);
            title.Size(-1, 28f, 1f);
            if (family != null) Chip(head, family.name + "  ·  " + Inch.Format(family.pitch) + " pitch");

            // Where you are: pick the sprockets, choose their sides, make it.
            var stage = picks.Count < 2 ? 0 : ready ? 2 : 1;
            var steps = Ui.Rect("Steps", body);
            Ui.Row(steps, 4f);
            steps.Size(-1, 26f);
            var names = new[] { "1  Pick sprockets", "2  Choose sides", "3  Make it" };
            for (var i = 0; i < names.Length; i++) {
                var on = i == stage;
                var pill = Ui.Image(steps, names[i], on ? Theme.Accent : i < stage ? Theme.AccentSoft : Theme.Field, 6f);
                pill.Size(-1, 24f, 1f);
                var label = Ui.Text(pill.transform, "Label", names[i], Theme.FontSmall, on ? Color.white : i < stage ? Theme.Text : Theme.TextFaint, Theme.Medium, TextAlignmentOptions.Center);
                label.rectTransform.Fill();
            }

            // What to do next.
            string next;
            if (picks.Count == 0)
                next = "Click the first sprocket the chain goes round (its shaft or hub works too).";
            else if (picks.Count == 1)
                next = "Click the next sprocket round the loop. Hovering one shows the chain it would make.";
            else if (!ready)
                next = picks.Count >= 3
                    ? "This loop doesn't work yet (below). Try Order round the loop, flip a sprocket's side, or take one off."
                    : "This loop doesn't work yet (below): flip a sprocket's side or move one.";
            else
                next = "Add more sprockets, or set one to Inside for an idler. Press Enter or Make chain when it looks right.";
            var hint = Ui.Text(body, "Hint", next, Theme.FontBody, Theme.Text);
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.overflowMode = TextOverflowModes.Overflow;
            hint.Size(-1, Mathf.Max(22f, hint.GetPreferredValues(next, Width - 30f, 0f).y + 2f));

            if (family != null && family.HasTreads) {
                var styles = ChainFamily.Styles;
                ToolOptions.Segmented(body, styles.Select(x => x.name).ToArray(), System.Array.FindIndex(styles, x => x.key == style), i => {
                    style = styles[i].key;
                    Interface.Interface.Say("Made of: " + styles[i].name.ToLower(), true);
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                    Refresh();
                });
            }

            // The sprockets, in the chain's order.
            if (picks.Count > 0) {
                var list = Ui.Image(body, "Sprockets", Theme.Field, 8f);
                Ui.Column(list.rectTransform, 0f, new RectOffset(8, 6, 4, 4));
                for (var i = 0; i < picks.Count; i++) SprocketRow(list.rectTransform, i, ways);
                var tools = Ui.Rect("Tools", body);
                Ui.Row(tools, 6f);
                tools.Size(-1, 28f);
                if (picks.Count >= 3) {
                    var order = Ui.Button(tools, "Order", "Order round the loop", AutoOrder, false, 6f, Theme.FontSmall);
                    order.Size(-1, 26f);
                    Ui.Tip(order, "Order round the loop", null, "Puts the sprockets in the order that makes the shortest chain that works (keeps their sides).");
                }
                var clear = Ui.Button(tools, "Clear", "Start over", () => {
                    picks.Clear();
                    family = null;
                    Interface.Interface.Say("Chain: start again from the first sprocket", true);
                    Refresh();
                }, false, 6f, Theme.FontSmall);
                clear.Size(-1, 26f);
            }

            if (picks.Count >= 2) Summary();
            Help();
        }

        private static void Chip(RectTransform parent, string text) {
            var chip = Ui.Image(parent, "Chip", Theme.AccentSoft, 6f);
            Ui.Row(chip.rectTransform, 0f, new RectOffset(8, 8, 0, 0));
            chip.Size(-1, 22f);
            var label = Ui.Text(chip.transform, "Label", text, Theme.FontSmall, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
            label.Size(label.GetPreferredValues(text).x + 2f, 22f);
        }

        // One sprocket: its place in the chain, size, speed next to the first, teeth in the chain, side.
        private void SprocketRow(RectTransform list, int index, List<int> ways) {
            var pick = picks[index];
            var row = Ui.Rect("Sprocket " + (index + 1), list);
            Ui.Row(row, 8f);
            row.Size(-1, 42f);
            var badge = Ui.Image(row, "Number", pick.flipped ? Theme.Warning : Theme.Accent, 11f);
            badge.Size(22f, 22f);
            var number = Ui.Text(badge.transform, "N", (index + 1).ToString(), Theme.FontSmall, pick.flipped ? Theme.Field : Color.white, Theme.Medium, TextAlignmentOptions.Center);
            number.rectTransform.Fill();
            var words = Ui.Rect("Words", row);
            Ui.Column(words, 0f);
            words.Size(-1, 40f, 1f);
            // Its text never pushes the side buttons: every row lines them up.
            var wordsLayout = words.GetComponent<LayoutElement>();
            wordsLayout.preferredWidth = 0f;
            wordsLayout.minWidth = 0f;
            var name = Ui.Text(words, "Name", pick.Idler ? "Spacer idler (" + Inch.Format(pick.surface * 2f) + " OD)" : pick.teeth + "T sprocket", Theme.FontBody, Theme.Text, Theme.Medium);
            name.Size(-1, 21f);
            var detail = Ui.Text(words, "Detail", Detail(index, ways), Theme.FontSmall, Theme.TextDim);
            detail.Size(-1, 18f);
            ToolOptions.Segmented(row, new[] { "Outside", "Inside" }, pick.flipped ? 1 : 0, side => {
                if ((side == 1) == picks[index].flipped) return;
                picks[index].flipped = side == 1;
                Interface.Interface.Say("Sprocket " + (index + 1) + ": the chain wraps it from " + (picks[index].flipped ? "inside the loop" : "outside"), true);
                Audio.Sounds.Play(Audio.Sounds.Tick);
                Refresh();
            }, 60f);
            // Its own width only (its buttons would stretch and squeeze the name).
            var sides = row.GetChild(row.childCount - 1).gameObject.GetOrAdd<LayoutElement>();
            sides.flexibleWidth = 0f;
            sides.preferredWidth = 132f;
            var remove = Ui.Button(row, "Remove", "×", () => {
                picks.RemoveAt(index);
                if (picks.Count == 0) family = null;
                Interface.Interface.Say("Took sprocket " + (index + 1) + " off the chain", true);
                Refresh();
            }, false, 5f, Theme.FontBody);
            remove.Size(28f, 28f);
            remove.targetGraphic.color = new Color(1f, 1f, 1f, 0f);
            Ui.Tip(remove, "Take it off", null, "Takes this sprocket off the chain.");
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            Ui.Tip(hit, pick.Idler ? "Spacer idler" : pick.teeth + "T sprocket", null,
                "Speed is next to sprocket 1 (" + picks[0].teeth + "T). Teeth in: how many of its teeth are in the chain at once; under 3 and a loaded chain can skip.");
        }

        // "×0.5 speed, backward · 9 teeth in" (speed next to the first sprocket; teeth in the chain at once).
        private string Detail(int index, List<int> ways) {
            var pick = picks[index];
            if (pick.Idler) return "Smooth: the chain slides round it";
            var ratio = (float)picks[0].teeth / Mathf.Max(pick.teeth, 1) * (ways[index] == ways[0] ? 1f : -1f);
            var text = "×" + Mathf.Abs(ratio).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " speed" + (ratio < 0f ? ", backward" : "");
            if (preview == null || !preview.Valid) return text;
            var teeth = preview.TeethIn(index, pick.teeth);
            return teeth < 3 && !pick.flipped
                ? text + "  ·  <color=#FABA3B>" + Teeth(teeth) + " in: may skip</color>"
                : text + "  ·  " + Teeth(teeth) + " in";
        }

        private static string Teeth(int count) => count + (count == 1 ? " tooth" : " teeth");

        // Links, length and weight, and whether it fits.
        private void Summary() {
            var card = Ui.Image(body, "Summary", Theme.Field, 8f);
            Ui.Column(card.rectTransform, 6f, new RectOffset(12, 12, 10, 10));
            if (preview == null || !preview.Valid) {
                var problem = preview?.problem ?? "Pick at least two sprockets";
                var words = Ui.Text(card.transform, "Problem", problem, Theme.FontSmall, Theme.Danger);
                words.textWrappingMode = TextWrappingModes.Normal;
                words.overflowMode = TextOverflowModes.Overflow;
                words.Size(-1, Mathf.Max(20f, words.GetPreferredValues(problem, Width - 54f, 0f).y + 2f));
                return;
            }
            var stats = Ui.Rect("Stats", card.transform);
            Ui.Row(stats, 6f);
            stats.Size(-1, 40f);
            var pounds = preview.links * family.gramsPerLink / 453.592f;
            Stat(stats, preview.links.ToString(), "links");
            Stat(stats, Inch.Format(preview.length), "long");
            Stat(stats, pounds < 0.01f ? "<0.01 lb" : pounds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " lb", "weight");
            var off = preview.length - preview.tight;
            var fits = Mathf.Abs(off) <= family.pitch * 0.3f;
            var fit = fits
                ? "Fits: the links come to the loop's own length, so it runs tight."
                : "The links come to " + Inch.Format(Mathf.Abs(off)) + (off > 0f ? " more" : " less") + " than the loop: move a sprocket about " + Inch.Format(Mathf.Abs(off) / 2f) + (off > 0f ? " farther away" : " closer") + ", or add an idler (Inside), for it to run tight.";
            var line = Ui.Text(card.transform, "Fit", fit, Theme.FontSmall, fits ? Theme.Success : Theme.Warning);
            line.textWrappingMode = TextWrappingModes.Normal;
            line.overflowMode = TextOverflowModes.Overflow;
            line.Size(-1, Mathf.Max(18f, line.GetPreferredValues(fit, Width - 54f, 0f).y + 2f));
        }

        private static void Stat(RectTransform parent, string value, string label) {
            var cell = Ui.Rect(label, parent);
            Ui.Column(cell, 0f);
            cell.Size(-1, 40f, 1f);
            var big = Ui.Text(cell, "Value", value, Theme.FontTitle, Theme.Text, Theme.Medium);
            big.overflowMode = TextOverflowModes.Overflow;
            big.Size(-1, 22f);
            var small = Ui.Text(cell, "Label", label, Theme.FontSmall, Theme.TextFaint);
            small.overflowMode = TextOverflowModes.Overflow;
            small.Size(-1, 18f);
        }

        private const string HelpKey = "V2.ChainHelpOpen";

        // How chains work, folded away once read.
        private void Help() {
            var open = PlayerPrefs.GetInt(HelpKey, 1) == 1;
            var toggle = Ui.Button(body, "Help", open ? "Hide how chains work" : "How chains work", () => {
                PlayerPrefs.SetInt(HelpKey, PlayerPrefs.GetInt(HelpKey, 1) == 1 ? 0 : 1);
                PlayerPrefs.SetInt(HelpKey + "Set", 1);
                Refresh();
            }, false, 5f, Theme.FontSmall);
            toggle.Size(-1, 26f);
            if (!open) return;
            var lines = new[] {
                ("Order", "The chain goes round the sprockets in the order you click them. Hover the next one to see the chain it would make; Order round the loop sorts them for you."),
                ("Sides", "Outside (blue): the chain goes round it with the loop. Inside (yellow): it presses into the loop and turns the other way, for an idler or to turn a shaft backward. Click a sprocket again to switch."),
                ("Sizes", "Normal chain: 10T to 48T sprockets. High strength chain or tank tread: HS sprockets and the tread drive wheel. 6P chain: 6P (low strength) sprockets. Axles parallel, sprockets lined up."),
                ("Fit", "A chain is a whole number of links and runs tight; you're told how far to move a sprocket when it falls between. Keep 3 or more teeth in the chain on every sprocket that drives or is driven."),
                ("Motors", "A motor turning any sprocket on the chain turns them all, at the teeth's ratio (12T to 24T: half speed)."),
                ("Later", "Right-click the chain > Edit chain to change it; delete it like any part."),
            };
            foreach (var (title, text) in lines) {
                var row = Ui.Rect(title, body);
                Ui.Row(row, 8f, null, TextAnchor.UpperLeft);
                var head = Ui.Text(row, "Title", title, Theme.FontSmall, Theme.Accent, Theme.Medium);
                head.Size(54f, 18f);
                var words = Ui.Text(row, "Text", text, Theme.FontSmall, Theme.TextDim);
                words.textWrappingMode = TextWrappingModes.Normal;
                words.overflowMode = TextOverflowModes.Overflow;
                var height = Mathf.Max(18f, words.GetPreferredValues(text, Width - 92f, 0f).y + 2f);
                words.Size(Width - 92f, height);
                row.Size(-1, height);
            }
            var keys = Ui.Hint(body, "Enter", "make it", "Backspace", "take the last off", "Esc", "stop");
            keys.Size(-1, 22f);
        }

        // Each pick's way round: the loop's own way (from the order the sprockets were picked in), flipped ones the other.
        private List<int> Ways() {
            Plane();
            return WaysFor(picks.Select(p => Flat(p.center)).ToList(), picks.Select(p => p.flipped).ToList());
        }

        private static List<int> WaysFor(IList<Vector2> flat, IList<bool> flipped) {
            var area = 0f;
            for (var i = 0; i < flat.Count; i++) {
                var a = flat[i];
                var b = flat[(i + 1) % flat.Count];
                area += a.x * b.y - b.x * a.y;
            }
            var loop = area < 0f ? -1 : 1;
            return flipped.Select(f => f ? -loop : loop).ToList();
        }

        private void Plane() {
            if (picks.Count == 0) return;
            var normal = picks[0].axis.normalized;
            var up = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            frame = Quaternion.LookRotation(normal, up);
            origin = picks[0].center;
        }

        private Vector2 Flat(Vector3 world) {
            var local = Quaternion.Inverse(frame) * (world - origin);
            return new Vector2(local.x, local.y);
        }

        private float RadiusOf(Pick p) => family != null ? family.RadiusOf(p.teeth, p.surface) : 0.5f;

        private ChainPath PathFor(IList<Vector2> flat, IList<Pick> of, IList<int> ways) {
            var wraps = new List<ChainWrap>();
            for (var i = 0; i < flat.Count; i++)
                wraps.Add(new ChainWrap { center = flat[i], radius = RadiusOf(of[i]), way = ways[i], teeth = of[i].teeth });
            var down = Quaternion.Inverse(frame) * Vector3.down;
            return ChainPath.Build(wraps, family.pitch, new Vector2(down.x, down.y));
        }

        private void UpdatePreview(List<int> ways) {
            Plane();
            preview = family == null || picks.Count < 2 ? null : PathFor(picks.Select(p => Flat(p.center)).ToList(), picks, ways);
        }

        // Every order of the sprockets (the first stays first): the one making the shortest chain that works. Past
        // eight sprockets, round their middle by angle.
        private void AutoOrder() {
            if (picks.Count < 3) return;
            Plane();
            List<Pick> best = null;
            var bestLength = float.MaxValue;
            if (picks.Count <= 8) {
                var rest = Enumerable.Range(1, picks.Count - 1).ToList();
                foreach (var order in Permutations(rest)) {
                    if (order.Count > 1 && order[0] > order[^1]) continue;  // the same loop the other way round
                    var tried = new List<Pick> { picks[0] };
                    tried.AddRange(order.Select(i => picks[i]));
                    var flat = tried.Select(p => Flat(p.center)).ToList();
                    var path = PathFor(flat, tried, WaysFor(flat, tried.Select(p => p.flipped).ToList()));
                    if (!path.Valid || path.tight >= bestLength) continue;
                    bestLength = path.tight;
                    best = tried;
                }
            }
            if (best == null) {
                var flat = picks.Select(p => Flat(p.center)).ToList();
                var middle = flat.Aggregate(Vector2.zero, (a, b) => a + b) / flat.Count;
                var start = Mathf.Atan2(flat[0].y - middle.y, flat[0].x - middle.x);
                best = picks.Select((p, i) => (p, angle: Mathf.Repeat(Mathf.Atan2(flat[i].y - middle.y, flat[i].x - middle.x) - start, Mathf.PI * 2f)))
                            .OrderBy(x => x.angle).Select(x => x.p).ToList();
            }
            var changed = !best.SequenceEqual(picks);
            picks.Clear();
            picks.AddRange(best);
            Interface.Interface.Say(changed ? "Sprockets put in order round the loop" : "They're already in the best order", true);
            Audio.Sounds.Play(Audio.Sounds.Tick);
            Refresh();
        }

        private static IEnumerable<List<int>> Permutations(List<int> items) {
            if (items.Count <= 1) {
                yield return new List<int>(items);
                yield break;
            }
            for (var i = 0; i < items.Count; i++) {
                var rest = new List<int>(items);
                rest.RemoveAt(i);
                foreach (var tail in Permutations(rest)) {
                    tail.Insert(0, items[i]);
                    yield return tail;
                }
            }
        }

        // ------------------------------------------------------------------ picking

        private GameObject SprocketUnderMouse() {
            var camera = Camera.main;
            if (camera == null || MouseInput.overUI) return null;
            if (!Physics.Raycast(camera.ScreenPointToRay(MouseInput.Position), out var hit, Mathf.Infinity, LayerMask.GetMask("Default", "HoleCollisions"))) return null;
            var saved = SelectAs.Resolve(hit.collider.gameObject)?.GetComponentInParent<SavedObject>();
            if (saved != null && ChainFamily.ForSprocket(saved.gameObject) != null) return saved.gameObject;
            if (saved != null && ChainFamily.IsIdler(saved.gameObject)) return saved.gameObject;   // V2: a spacer idler
            // On its shaft or hub, or just off its teeth: the sprocket round that point.
            GameObject best = null;
            var nearest = float.MaxValue;
            foreach (var (part, chain, teeth, center, axis) in ChainLoop.AllSprockets()) {
                if (part == null) continue;
                var offset = hit.point - center;
                var along = Vector3.Dot(offset, axis);
                if (Mathf.Abs(along) > 0.7f) continue;
                var radial = Vector3.ProjectOnPlane(offset, axis).magnitude;
                if (radial > chain.PitchRadius(teeth) + 0.2f) continue;
                if (radial + Mathf.Abs(along) < nearest) {
                    nearest = radial + Mathf.Abs(along);
                    best = part;
                }
            }
            return best ?? (saved != null ? saved.gameObject : null);
        }

        private void Click(GameObject part) {
            var chainFamily = ChainFamily.ForSprocket(part);
            var idler = chainFamily == null && ChainFamily.IsIdler(part);   // V2: spacers the chain slides round
            if (idler && family == null) {
                Interface.Interface.Say("Click a sprocket first: the chain's size comes from its teeth. Spacers can be idlers after that", false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                return;
            }
            if (idler) chainFamily = family;
            if (chainFamily == null) {
                Interface.Interface.Say("That's not a sprocket or a spacer: click the sprockets (or spacer idlers) the chain goes round", false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                return;
            }
            var existing = picks.FirstOrDefault(p => p.part == part);
            if (existing != null) {
                existing.flipped = !existing.flipped;
                Interface.Interface.Say("Sprocket " + (picks.IndexOf(existing) + 1) + ": the chain wraps it from " + (existing.flipped ? "inside the loop" : "outside"), true);
                Audio.Sounds.Play(Audio.Sounds.Tick);
                Refresh();
                return;
            }
            var (center, axis) = ChainLoop.SprocketFrame(part);
            if (family != null && chainFamily != family) {
                Interface.Interface.Say("That sprocket takes " + chainFamily.name.ToLower() + ", not " + family.name.ToLower(), false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                return;
            }
            if (picks.Count > 0) {
                var normal = picks[0].axis;
                if (Mathf.Abs(Vector3.Dot(axis, normal)) < 0.98f) {
                    Interface.Interface.Say("That sprocket's axle isn't parallel to the first one's: a chain can't go round it", false);
                    Audio.Sounds.Play(Audio.Sounds.Error);
                    return;
                }
                var off = Vector3.Dot(center - picks[0].center, normal.normalized);
                var room = idler ? ChainLoop.IdlerFrame(part).length / 2f + 0.1f : 0.3f;   // V2: along a spacer
                if (Mathf.Abs(off) > room) {
                    Interface.Interface.Say("That sprocket is " + Inch.Format(Mathf.Abs(off)) + " out of line with the first: line them up along the axle", false);
                    Audio.Sounds.Play(Audio.Sounds.Error);
                    return;
                }
            }
            family = chainFamily;
            if (idler) {
                var frame = ChainLoop.IdlerFrame(part);
                // Its middle along the axle may be off the chain's line (a long spacer): the chain runs at the first sprocket's.
                if (picks.Count > 0) center = frame.center + picks[0].axis.normalized * Vector3.Dot(picks[0].center - frame.center, picks[0].axis.normalized);
                picks.Add(new Pick { part = part, center = center, axis = axis, teeth = 0, surface = frame.radius });
            }
            else picks.Add(new Pick { part = part, center = center, axis = axis, teeth = GearTrain.Teeth(part, out _) });
            Audio.Sounds.Play(Audio.Sounds.Tick);
            Interface.Interface.Say((idler ? "Spacer idler " : "Sprocket ") + picks.Count + " added" + (idler ? "" : ": " + picks[picks.Count - 1].teeth + "T"), true);
            Refresh();
        }

        private void Make() {
            if (picks.Count < 2 || preview == null || !preview.Valid) {
                Interface.Interface.Say(preview != null && !preview.Valid ? preview.problem : "Pick at least two sprockets", false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                return;
            }
            var ways = Ways();
            var world = picks.Select((p, i) => (p.center, p.axis, p.teeth, ways[i], RadiusOf(p))).ToList();
            if (picks[0].Idler) {
                Interface.Interface.Say("The chain has to start on a sprocket: take the spacer off the front, or start over from a sprocket", false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                return;
            }
            if (editing != null) {
                var before = new ChainLoop.LoopElement(editing);
                editing.Set(family, world, style);
                editing.Rebuild();
                editing.Connect();
                ChainTensioner.Begin(editing);   // V2
                StateSystem.AddElement(before);
                StateSystem.AddState(new ChainLoop.LoopElement(editing));
                Stop("Chain changed: " + editing.Path.links + " links");
                return;
            }
            var chain = PartsManager.GeneratePart("CHAN-" + family.key, picks[0].center, Quaternion.identity);
            var loop = chain.GetComponent<ChainLoop>();
            loop.Set(family, world, style);
            loop.Rebuild();
            loop.SeatOn(picks[0].part);
            ChainTensioner.Begin(loop);   // V2: an idler on a band-pulled arm keeps it tight from now on
            var placed = new ObjectElement(chain) { existing = false };
            StateSystem.AddElement(placed);
            StateSystem.AddState(new ObjectElement(chain));
            Audio.Sounds.Play(Audio.Sounds.Clack);
            // The guide folds away once a chain is made (it opens again from its button).
            if (!PlayerPrefs.HasKey(HelpKey + "Set")) {
                PlayerPrefs.SetInt(HelpKey, 0);
                PlayerPrefs.SetInt(HelpKey + "Set", 1);
            }
            Stop("Made a " + (style == "chain" ? family.name.ToLower() : ChainFamily.Styles.First(x => x.key == style).name.ToLower()) + " of " + loop.Path.links + " links (" + Inch.Format(loop.Path.length) + ") round " + picks.Count + " sprockets");
        }

        // ------------------------------------------------------------------ every frame

        private void Update() {
            var keyboard = Keyboard.current;
            var typing = InputEvents.TextInput.IsTyping;
            if (keyboard != null && !typing) {
                if (keyboard.escapeKey.wasPressedThisFrame) {
                    Stop();
                    return;
                }
                if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) {
                    Make();
                    return;
                }
                if (keyboard.backspaceKey.wasPressedThisFrame && picks.Count > 0) {
                    picks.RemoveAt(picks.Count - 1);
                    if (picks.Count == 0) family = null;
                    Interface.Interface.Say("Took the last sprocket off", true);
                    Refresh();
                }
            }
            var hover = SprocketUnderMouse();
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !MouseInput.overUI && hover != null) Click(hover);
            Draw(hover);
        }

        // The chain the hovered sprocket would make if clicked next (drawn faint), worked out when either changes.
        private GameObject ghostFor;
        private int ghostEdits = -1;
        private ChainPath ghost;

        private void UpdateGhost(GameObject hover) {
            if (hover == ghostFor && edits == ghostEdits) return;
            ghostFor = hover;
            ghostEdits = edits;
            ghost = null;
            if (hover == null || picks.Count == 0 || picks.Any(p => p.part == hover)) return;
            var chainFamily = ChainFamily.ForSprocket(hover);
            var idler = chainFamily == null && ChainFamily.IsIdler(hover);
            if (idler) chainFamily = family;
            if (chainFamily == null || chainFamily != family) return;
            var (center, axis) = ChainLoop.SprocketFrame(hover);
            var room = idler ? ChainLoop.IdlerFrame(hover).length / 2f + 0.1f : 0.3f;
            if (Mathf.Abs(Vector3.Dot(axis, picks[0].axis)) < 0.98f || Mathf.Abs(Vector3.Dot(center - picks[0].center, picks[0].axis.normalized)) > room) return;
            Plane();
            var next = new Pick { part = hover, center = center, axis = axis, teeth = idler ? 0 : GearTrain.Teeth(hover, out _), surface = idler ? ChainLoop.IdlerFrame(hover).radius : 0f };
            var flat = picks.Select(p => Flat(p.center)).Append(Flat(center)).ToList();
            ghost = PathFor(flat, picks.Append(next).ToList(), WaysFor(flat, picks.Select(p => p.flipped).Append(false).ToList()));
        }

        private void Draw(GameObject hover) {
            Overlay.Begin();
            var color = Theme.Accent;
            Vector3 World(Vector2 flat) => origin + frame * new Vector3(flat.x, flat.y, 0f);
            UpdateGhost(hover);
            var hoverFamily = hover == null ? null : ChainFamily.ForSprocket(hover) ?? (family != null && ChainFamily.IsIdler(hover) ? family : null);
            if (hoverFamily is ChainFamily f) {
                var (center, axis) = ChainLoop.SprocketFrame(hover);
                var picked = picks.Any(p => p.part == hover);
                var fits = family == null || f == family;
                var hoverRadius = ChainFamily.IsIdler(hover) ? f.IdlerRadius(ChainLoop.IdlerFrame(hover).radius) : f.PitchRadius(GearTrain.Teeth(hover, out _));
                Overlay.WorldCircle(center, axis, hoverRadius, picked ? Theme.Warning : fits ? Color.white : Theme.Danger, 1.5f);
                if (!picked && Overlay.ToScreen(center, out var at))
                    Overlay.Text(at + new Vector2(0f, -22f), fits ? "add as " + (picks.Count + 1) : "takes " + f.name.ToLower(), fits ? Color.white : Theme.Danger, null, true);
            }
            // The chain the hovered sprocket would make: faint, or red where it can't go.
            if (ghost != null && ghost.points.Count > 1) {
                var faint = ghost.Valid ? new Color(1f, 1f, 1f, 0.35f) : new Color(Theme.Danger.r, Theme.Danger.g, Theme.Danger.b, 0.4f);
                for (var i = 0; i < ghost.points.Count; i++)
                    Overlay.Line(World(ghost.points[i]), World(ghost.points[(i + 1) % ghost.points.Count]), faint, 1.5f);
            }
            for (var i = 0; i < picks.Count; i++) {
                var p = picks[i];
                var radius = RadiusOf(p);
                Overlay.WorldCircle(p.center, p.axis, radius, p.flipped ? Theme.Warning : color, 1.5f);
                if (Overlay.ToScreen(p.center, out var at)) Overlay.Text(at, (i + 1) + (p.flipped ? "  inside" : "  outside"), p.flipped ? Theme.Warning : color, null, true);
            }
            if (preview == null || !preview.Valid) return;
            // The chain's line, every link's pin a dot.
            for (var i = 0; i < preview.points.Count; i++)
                Overlay.Line(World(preview.points[i]), World(preview.points[(i + 1) % preview.points.Count]), color, 2.5f);
            var step = preview.length / preview.links;
            for (var k = 0; k < preview.links; k++)
                if (Overlay.ToScreen(World(preview.At(k * step)), out var pin)) Overlay.Disc(pin, 2f, Color.white);
        }
    }
}
