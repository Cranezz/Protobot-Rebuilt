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
            public int teeth;
            public bool flipped;
        }

        private readonly List<Pick> picks = new();
        private ChainFamily family;
        private string style = "chain";
        private ChainLoop editing;
        private RectTransform window, body;
        private TextMeshProUGUI status;
        private System.Func<bool> savedBlocked;
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
                    active.picks.Add(new Pick { part = part, center = center, axis = axis, teeth = edit.wraps[wrap].teeth });
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
            SelectionSystem.SelectionManager.Blocked = savedBlocked;
            if (active == this) active = null;
        }

        // ------------------------------------------------------------------ window

        private void Build() {
            savedBlocked = SelectionSystem.SelectionManager.Blocked;
            SelectionSystem.SelectionManager.Blocked = () => true;
            window = Ui.Panel(transform, "Window", Theme.Panel, 10f).rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0f, 1f);
            window.pivot = new Vector2(0f, 1f);
            window.anchoredPosition = new Vector2(76f, -(Layout.TopBarBottom + 60f));
            window.sizeDelta = new Vector2(400f, 0f);
            Ui.Column(window, 6f, new RectOffset(14, 14, 10, 14));
            window.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            window.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var title = Ui.Text(window, "Title", editing != null ? "Edit chain" : "Chain", Theme.FontTitle, Theme.Text, Theme.Medium);
            title.Size(-1, 30f);
            body = Ui.Rect("Body", window);
            Ui.Column(body, 5f);
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            status = Ui.Text(window, "Status", "", Theme.FontSmall, Theme.TextDim);
            status.textWrappingMode = TextWrappingModes.Normal;
            status.Size(-1, 52f);
            var buttons = Ui.Rect("Buttons", window);
            Ui.Row(buttons, 8f, null, TextAnchor.MiddleRight);
            buttons.Size(-1, 32f);
            var cancel = Ui.Button(buttons, "Cancel", "Cancel", () => Stop(), false, 6f);
            cancel.Size(-1, 30f);
            var make = Ui.Button(buttons, "Make", editing != null ? "Keep changes" : "Make chain", Make, true, 6f);
            make.Size(-1, 30f);
            Ui.Tip(make, "Make chain", "Enter", "Makes the chain round the sprockets picked.");
        }

        private void Refresh() {
            for (var i = body.childCount - 1; i >= 0; i--) Destroy(body.GetChild(i).gameObject);
            var hint = Ui.Text(body, "Hint", picks.Count == 0
                ? "Click the sprockets in the order the chain goes round them. The chain goes round the outside; click a sprocket again to wrap it from the other side."
                : (family != null ? family.name + ", " + Inch.Format(family.pitch) + " pitch. " : "") + "Click more sprockets, or one again to flip its side. Backspace takes the last off.",
                Theme.FontSmall, Theme.TextDim);
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.overflowMode = TextOverflowModes.Overflow;
            hint.Size(-1, 66f);
            if (family != null && family.HasTreads) {
                var styles = ChainFamily.Styles;
                ToolOptions.Segmented(body, styles.Select(x => x.name).ToArray(), System.Array.FindIndex(styles, x => x.key == style), i => {
                    style = styles[i].key;
                    Interface.Interface.Say("Made of: " + styles[i].name.ToLower(), true);
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                    Refresh();
                });
            }
            var ways = Ways();
            for (var i = 0; i < picks.Count; i++) {
                var pick = picks[i];
                var index = i;
                var row = Ui.Rect("Sprocket " + (i + 1), body);
                Ui.Row(row, 6f);
                row.Size(-1, 28f);
                var label = Ui.Text(row, "Name", (i + 1) + ".  " + PartLabels.Short(pick.part) + " (" + pick.teeth + "T)", Theme.FontBody, Theme.Text);
                label.Size(-1, 26f, 1f);
                var side = Ui.Button(row, "Side", pick.flipped ? "inside" : "outside", () => {
                    picks[index].flipped = !picks[index].flipped;
                    Interface.Interface.Say("Sprocket " + (index + 1) + ": the chain wraps it from " + (picks[index].flipped ? "inside the loop" : "outside"), true);
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                    Refresh();
                }, pick.flipped, 5f, Theme.FontSmall);
                side.Size(70f, 24f);
                Ui.Tip(side, "Which side", null, "Outside: the chain goes round it with the loop. Inside: it presses into the loop (an idler) and turns the other way.");
                var remove = Ui.Button(row, "Remove", "×", () => {
                    picks.RemoveAt(index);
                    if (picks.Count == 0) family = null;
                    Refresh();
                }, false, 5f, Theme.FontBody);
                remove.Size(26f, 24f);
            }
            UpdatePreview(ways);
        }

        // Each pick's way round: the loop's own way (from the order the sprockets were picked in), flipped ones the other.
        private List<int> Ways() {
            Plane();
            var flat = picks.Select(p => Flat(p.center)).ToList();
            var area = 0f;
            for (var i = 0; i < flat.Count; i++) {
                var a = flat[i];
                var b = flat[(i + 1) % flat.Count];
                area += a.x * b.y - b.x * a.y;
            }
            var loop = area < 0f ? -1 : 1;
            return picks.Select(p => p.flipped ? -loop : loop).ToList();
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

        private void UpdatePreview(List<int> ways) {
            Plane();
            preview = null;
            if (family == null || picks.Count < 2) {
                status.text = picks.Count == 1 ? "Now the next sprocket the chain goes to." : "";
                return;
            }
            var wraps = picks.Select((p, i) => new ChainWrap { center = Flat(p.center), radius = family.PitchRadius(p.teeth), way = ways[i], teeth = p.teeth }).ToList();
            var down = Quaternion.Inverse(frame) * Vector3.down;
            preview = ChainPath.Build(wraps, family.pitch, new Vector2(down.x, down.y));
            if (!preview.Valid) {
                status.text = "<color=#EE5450>" + preview.problem + "</color>";
                return;
            }
            var slack = preview.length - preview.tight;
            status.text = preview.links + " links, " + Inch.Format(preview.length) + " of chain. Slack " + Inch.Format(slack)
                          + (slack > family.pitch * 0.6f ? ": a little loose (move a sprocket about " + Inch.Format(slack / 2f) + " apart, or add an idler)" : ": snug");
        }

        // ------------------------------------------------------------------ picking

        private GameObject SprocketUnderMouse() {
            var camera = Camera.main;
            if (camera == null || MouseInput.overUI) return null;
            if (!Physics.Raycast(camera.ScreenPointToRay(MouseInput.Position), out var hit, Mathf.Infinity, LayerMask.GetMask("Default", "HoleCollisions"))) return null;
            var saved = SelectAs.Resolve(hit.collider.gameObject)?.GetComponentInParent<SavedObject>();
            if (saved != null && ChainFamily.ForSprocket(saved.gameObject) != null) return saved.gameObject;
            // On its shaft or hub, or just off its teeth: the sprocket round that point.
            GameObject best = null;
            var nearest = float.MaxValue;
            foreach (var other in FindObjectsByType<SavedObject>(FindObjectsSortMode.None)) {
                var chain = ChainFamily.ForSprocket(other.gameObject);
                if (chain == null || other.gameObject.IsDeleted()) continue;
                var (center, axis) = ChainLoop.SprocketFrame(other.gameObject);
                var offset = hit.point - center;
                var along = Vector3.Dot(offset, axis);
                var radial = Vector3.ProjectOnPlane(offset, axis).magnitude;
                if (Mathf.Abs(along) > 0.7f || radial > chain.PitchRadius(GearTrain.Teeth(other.gameObject, out _)) + 0.2f) continue;
                if (radial + Mathf.Abs(along) < nearest) {
                    nearest = radial + Mathf.Abs(along);
                    best = other.gameObject;
                }
            }
            return best ?? (saved != null ? saved.gameObject : null);
        }

        private void Click(GameObject part) {
            var chainFamily = ChainFamily.ForSprocket(part);
            if (chainFamily == null) {
                Interface.Interface.Say("That's not a sprocket: click the sprockets the chain goes round", false);
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
                if (Mathf.Abs(off) > 0.3f) {
                    Interface.Interface.Say("That sprocket is " + Inch.Format(Mathf.Abs(off)) + " out of line with the first: line them up along the axle", false);
                    Audio.Sounds.Play(Audio.Sounds.Error);
                    return;
                }
            }
            family = chainFamily;
            picks.Add(new Pick { part = part, center = center, axis = axis, teeth = GearTrain.Teeth(part, out _) });
            Audio.Sounds.Play(Audio.Sounds.Tick);
            Interface.Interface.Say("Sprocket " + picks.Count + ": " + PartLabels.Short(part), true);
            Refresh();
        }

        private void Make() {
            if (picks.Count < 2 || preview == null || !preview.Valid) {
                Interface.Interface.Say(preview != null && !preview.Valid ? preview.problem : "Pick at least two sprockets", false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                return;
            }
            var ways = Ways();
            var world = picks.Select((p, i) => (p.center, p.axis, p.teeth, ways[i])).ToList();
            if (editing != null) {
                var before = new ChainLoop.LoopElement(editing);
                editing.Set(family, world, style);
                editing.Rebuild();
                editing.Connect();
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
            var placed = new ObjectElement(chain) { existing = false };
            StateSystem.AddElement(placed);
            StateSystem.AddState(new ObjectElement(chain));
            Audio.Sounds.Play(Audio.Sounds.Clack);
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

        private void Draw(GameObject hover) {
            Overlay.Begin();
            var color = Theme.Accent;
            if (hover != null && ChainFamily.ForSprocket(hover) is ChainFamily f) {
                var (center, axis) = ChainLoop.SprocketFrame(hover);
                Overlay.WorldCircle(center, axis, f.PitchRadius(GearTrain.Teeth(hover, out _)), picks.Any(p => p.part == hover) ? Theme.Warning : Color.white, 1.5f);
            }
            var ways = Ways();
            for (var i = 0; i < picks.Count; i++) {
                var p = picks[i];
                var radius = family != null ? family.PitchRadius(p.teeth) : 0.5f;
                Overlay.WorldCircle(p.center, p.axis, radius, p.flipped ? Theme.Warning : color, 1.5f);
                if (Overlay.ToScreen(p.center, out var at)) Overlay.Text(at, (i + 1).ToString(), p.flipped ? Theme.Warning : color, null, true);
            }
            if (preview == null || !preview.Valid) return;
            // The chain's line, every link's pin a dot.
            Vector3 World(Vector2 flat) => origin + frame * new Vector3(flat.x, flat.y, 0f);
            for (var i = 0; i < preview.points.Count; i++)
                Overlay.Line(World(preview.points[i]), World(preview.points[(i + 1) % preview.points.Count]), color, 2.5f);
            var step = preview.length / preview.links;
            for (var k = 0; k < preview.links; k++)
                if (Overlay.ToScreen(World(preview.At(k * step)), out var pin)) Overlay.Disc(pin, 2f, Color.white);
        }
    }
}
