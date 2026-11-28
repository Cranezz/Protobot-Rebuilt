using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Motion {
    // V2 (build 88): putting string on (Docs/String concept.md). Pick String in the parts list: click where it's tied (a
    // hole, a screw, standoff, shaft, pulley or winch drum, or an edge), then each thing it goes round or through on the
    // way (pulleys, posts, shafts: round the side it bends least; click one again to send it round the other side), then
    // click the last point again (or press Enter) to tie it off there. Ending on a winch drum winds it on. The string
    // follows the mouse with its length; a bar sets its kind and slack. Backspace takes the last point back, Esc lets go,
    // then stops. Once it's on, select it to change it where it lies (StringEditor).
    public class StringTool : MonoBehaviour {
        private static StringTool active;
        public static bool Active => active != null;

        private StringKind kind;
        private readonly List<StringNode> picks = new();
        private static float slack;            // kept for the next string
        private static string lastKind = "VEX";
        private RectTransform bar;
        private readonly StringPreview look = new();

        public static void Begin(string key = null) {
            if (Sandbox.Running) return;
            if (active != null) Destroy(active.gameObject);
            if (Placement.IsPlacing) Placement.Instance.StopPlacing();
            ChainTool.Stop();
            TubeTool.Stop();
            ElasticTool.Stop();
            foreach (var manager in FindObjectsByType<SelectionSystem.SelectionManager>(FindObjectsSortMode.None)) manager.ClearCurrent();
            active = Ui.Rect("String Tool", Interface.Interface.Root).Fill().gameObject.AddComponent<StringTool>();
            active.kind = StringKind.Of(key ?? lastKind);
            ToolBlock.Push();
            active.Rebuild();
        }

        /// <summary>Starts a string tied to (or wound on) a pulley or drum (its right-click menu).</summary>
        public static void BeginFrom(GameObject part) {
            Begin();
            if (active == null || part == null) return;
            var middle = part.TryGetComponent(out Renderer r) ? r.bounds.center : part.transform.position;
            foreach (var dir in new[] { Vector3.back, Vector3.forward, Vector3.down, Vector3.left, Vector3.right, Vector3.up })
                if (StringUi.PostAt(new Ray(middle - dir * 10f, dir), out var post) && post.part == part) {
                    active.picks.Add(new StringNode { post = post });
                    active.Rebuild();
                    return;
                }
        }

        public static void Stop() {
            if (active == null) return;
            Destroy(active.gameObject);
            active = null;
        }

        private void OnDestroy() {
            look.Dispose();
            ToolBlock.Pop();
            if (active == this) active = null;
        }

        // ------------------------------------------------------------------ the bar

        private void Rebuild() {
            if (bar != null) Destroy(bar.gameObject);
            bar = StringUi.Bar(transform, kind, slack, k => {
                kind = k;
                lastKind = k.key;
                Rebuild();
            }, s => {
                slack = s;
                Rebuild();
            }, Stop);
            Ui.Hint(bar, "Click", picks.Count == 0 ? "where it's tied (a hole, post, pulley, drum or edge)" : "each pulley, post or hole it goes round or through");
            if (picks.Count > 0) Ui.Hint(bar, "Click the last point again", "tie it off", "Enter", "tie it off there", "Click a post again", "other side");
            Ui.Hint(bar, "Backspace", "take back a point", "Esc", picks.Count > 0 ? "let go" : "done");
            StringUi.Help(bar, Rebuild);   // V2 (build 88, phase 5)
        }

        // ------------------------------------------------------------------ putting it on

        private void Make() {
            var route = picks.Select(p => p.Copy()).ToList();
            StringUi.MarkSpools(route);
            var path = StringLine.PathOf(route, kind, 3f);
            if (route.Count < 2 || !path.Valid) {
                Interface.Interface.Say(path.problem ?? "Tie it to two things at least", false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                return;
            }
            var type = PartsManager.GetPartType("STRG");
            var generator = type.GetComponent<PartGenerator>();
            generator.param1.value = kind.key;
            var obj = generator.Generate(route[0].post.Center, Quaternion.identity);
            var line = obj.GetComponent<StringLine>();
            line.Set(route, path.tight + slack, 3f);
            StateSystem.AddElement(new ObjectElement(obj) { existing = false });
            StateSystem.AddState(new State(new List<IElement> { new ObjectElement(obj), new StringLine.StringElement(line) }));
            Audio.Sounds.Play(Audio.Sounds.Clack);
            picks.Clear();
            Rebuild();
        }

        // ------------------------------------------------------------------ every frame

        private void Update() {
            var keyboard = Keyboard.current;
            if (Sandbox.Running) {
                Stop();
                return;
            }
            if (keyboard != null && !InputEvents.TextInput.IsTyping && !InputEvents.TextInput.ShortcutsPaused) {
                if (keyboard.escapeKey.wasPressedThisFrame) {
                    if (picks.Count > 0) {
                        picks.Clear();
                        Rebuild();
                    }
                    else Stop();
                    return;
                }
                if ((keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) && picks.Count >= 2) {
                    Make();
                    return;
                }
                if (keyboard.backspaceKey.wasPressedThisFrame && picks.Count > 0) {
                    picks.RemoveAt(picks.Count - 1);
                    Rebuild();
                }
            }
            var camera = Overlay.ViewCamera;
            if (camera == null) return;
            var hover = !MouseInput.overUI && StringUi.PostAt(camera.ScreenPointToRay(MouseInput.Position), out var found) ? found : null;
            if (MouseInput.LeftButton.wasPressedThisFrame && hover != null) Press(hover);
            Draw(camera, hover);
        }

        private void Press(BandPost post) {
            var index = BandUi.IndexOf(picks.Select(p => p.post).ToList(), post);
            if (picks.Count > 0 && index == picks.Count - 1) {
                // The last point again: tie it off there.
                if (picks.Count >= 2) Make();
                return;
            }
            if (index > 0 && picks[index].Round) {
                // A post it already goes round: the other side.
                var path = StringLine.PathOf(picks, kind, 3f);
                var way = index < path.ways.Length ? path.ways[index] : 1;
                picks[index].side = way == 0 ? -1 : -way;
                Audio.Sounds.Play(Audio.Sounds.Tick);
                return;
            }
            if (index == 0) return;
            picks.Add(new StringNode { post = post });
            Audio.Sounds.Play(Audio.Sounds.Tick);
            Rebuild();
        }

        private void Draw(Camera camera, BandPost hover) {
            Overlay.Begin();
            var mouse = Overlay.Mouse;
            foreach (var p in picks) BandUi.Ring(camera, p.post, Theme.Accent);
            if (picks.Count == 0) {
                look.Hide();
                Overlay.Ring(mouse + new Vector2(16f, -14f), 6f, kind.color, 2.5f);
                if (hover != null) {
                    BandUi.Ring(camera, hover, Color.white);
                    Overlay.MouseText("Tie it here: " + StringUi.What(hover, true), Color.white);
                }
                return;
            }
            var index = hover != null ? BandUi.IndexOf(picks.Select(p => p.post).ToList(), hover) : -1;
            var onNew = hover != null && index < 0;
            var list = picks.Select(p => p.Copy()).ToList();
            if (onNew) list.Add(new StringNode { post = hover });
            else if (hover == null) list.Add(new StringNode { post = BandUi.Finger(camera, camera.ScreenPointToRay(MouseInput.Position), picks[^1].post) });
            var ends = list.Select(p => p.Copy()).ToList();
            if (onNew || index == picks.Count - 1) StringUi.MarkSpools(ends);
            var path = StringLine.PathOf(ends, kind, 3f);
            if (hover != null) BandUi.Ring(camera, hover, Color.white);
            if (!path.Valid) {
                look.Hide();
                Overlay.MouseText(path.problem ?? "", Theme.Danger);
                return;
            }
            look.Show(path, path.Hanging(hover == null ? 0f : slack), kind);
            var text = Inch.Format(Mathf.Round((path.tight + (hover == null ? 0f : slack)) * 8f) / 8f) + " of " + kind.shortName.ToLowerInvariant();
            if (index == picks.Count - 1 && picks.Count >= 2) text += "\nClick: tie it off here";
            else if (index > 0 && picks[index].Round) text += "\nClick: round its other side";
            else if (onNew) text += "\n" + StringUi.What(hover, false) + StringUi.DrumNote(hover, kind);
            Overlay.MouseText(text, Color.white);
        }
    }

    /// <summary>V2 (build 88): what the string tool and editor share: picking what it's on, the bar, the words.</summary>
    public static class StringUi {
        /// <summary>What a string would be on along a ray: the rubber band's posts (a hole, a screw/standoff/shaft or
        /// round part, else a point on an edge), with pulleys and winch drums taken at their groove.</summary>
        public static bool PostAt(Ray ray, out BandPost post) {
            if (!ElasticTool.PostAt(ray, out post)) return false;
            if (post.part != null && post.part.GetComponent<StringLine>() != null) {
                post = null;
                return false;
            }
            // V2 (build 88): a sprocket: round its middle on its pitch circle (where a chain's line runs; a string lies
            // on its teeth about there too)
            var family = post.part != null ? ChainFamily.ForSprocket(post.part) : null;
            if (family != null) {
                var (center, axis) = ChainLoop.SprocketFrame(post.part);
                post.local = post.part.transform.InverseTransformPoint(center);
                post.axisLocal = post.part.transform.InverseTransformDirection(axis);
                post.hole = false;
                post.radius = family.PitchRadius(GearTrain.Teeth(post.part, out _));
                post.what = "sprocket";
                return true;
            }
            if (post.part != null && IsGrooved(post.part)) {
                // A pulley or drum is round about its middle hole (the shaft's), whatever part of it the mouse is on.
                if ((!post.HasAxis || post.hole) && MiddleHole(post.part, out var center, out var axis)) {
                    post.local = post.part.transform.InverseTransformPoint(center);
                    post.axisLocal = post.part.transform.InverseTransformDirection(axis);
                    post.hole = false;
                    post.radius = 0.3f;
                }
                if (post.HasAxis && !post.hole) Groove(post);
            }
            return true;
        }

        private static bool MiddleHole(GameObject part, out Vector3 center, out Vector3 axis) {
            center = default;
            axis = default;
            var middle = part.TryGetComponent(out Renderer r) ? r.bounds.center : part.transform.position;
            HoleCollider best = null;
            foreach (var hole in part.GetComponentsInChildren<HoleCollider>(true))
                if (best == null || (hole.transform.position - middle).sqrMagnitude < (best.transform.position - middle).sqrMagnitude) best = hole;
            if (best == null) return false;
            axis = best.transform.forward;
            // Its middle along the axis is the part's middle (a hole's place can be at one face).
            center = best.transform.position + axis * Vector3.Dot(middle - best.transform.position, axis);
            return true;
        }

        public static bool IsGrooved(GameObject part) {
            var id = part.GetComponent<SavedObject>()?.id ?? "";
            return id.Contains("Pulley") || id.Contains("Winch Drum");
        }

        public static bool IsDrum(BandPost post) => post.part != null && (post.part.GetComponent<SavedObject>()?.id ?? "").Contains("Winch Drum");

        // The pulley's or drum's groove: the narrowest place across its width, where the string sits.
        private static void Groove(BandPost post) {
            var axis = post.axisLocal.normalized;
            float best = float.MaxValue, at = 0f;
            for (var s = -0.4f; s <= 0.4f; s += 0.02f) {
                var r = Strands.RadiusAt(post.part, post.local, axis, s);
                if (r > 0.12f && r < best) {
                    best = r;
                    at = s;
                }
            }
            if (best == float.MaxValue) return;
            post.local += axis * at;
            post.radius = best;
        }

        /// <summary>Ends on a winch drum are wound on it.</summary>
        public static void MarkSpools(IList<StringNode> route) {
            if (route.Count < 2) return;
            foreach (var i in new[] { 0, route.Count - 1 })
                if (route[i].Round && IsDrum(route[i].post)) route[i].spool = true;
        }

        public static string What(BandPost post, bool tie) {
            var name = post.part != null ? PartLabels.Short(post.part) : "point";
            if (post.hole) return (tie ? "tied through a hole in the " : "through a hole in the ") + name;
            if (!post.HasAxis) return (tie ? "tied to the " : "over the ") + name + (tie ? "" : "'s edge");
            if (tie && IsDrum(post)) return "wound on the " + name;
            return (tie ? "tied round the " : "round the ") + name;
        }

        public static string DrumNote(BandPost post, StringKind kind) {
            if (!post.HasAxis || post.hole) return "";
            var r = post.radius + kind.PathRadius;
            return "\n1 turn = " + Inch.Format(Mathf.Round(2f * Mathf.PI * r * 16f) / 16f) + " of string";
        }

        public static RectTransform Bar(Transform parent, StringKind kind, float slack, System.Action<StringKind> onKind, System.Action<float> onSlack, System.Action onDone) {
            var bar = Ui.Panel(parent, "String Bar", Theme.Panel, 10f).rectTransform;
            bar.anchorMin = bar.anchorMax = new Vector2(0f, 1f);
            bar.pivot = new Vector2(0f, 1f);
            bar.anchoredPosition = new Vector2(76f, -(Layout.TopBarBottom + 60f));
            bar.sizeDelta = new Vector2(700f, 0f);
            Ui.Column(bar, 8f, new RectOffset(12, 12, 10, 10));
            bar.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            bar.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var row = Ui.Rect("Row", bar);
            Ui.Row(row, 10f);
            row.Size(-1, 30f);
            KindChoice(row, kind, onKind);
            SlackStepper(row, slack, onSlack);
            Ui.Rect("Gap", row).Size(-1, 28f, 1f);
            Ui.Button(row, "Done", "Done", onDone, false, 6f).Size(-1, 28f);
            Spec(bar, kind);
            return bar;
        }

        private const string HelpKey = "V2.StringHelpOpen";

        // V2 (build 88, string phase 5): how string works, folded away once read (like the chain tool's guide).
        public static void Help(Transform bar, System.Action rebuild) {
            var open = PlayerPrefs.GetInt(HelpKey, 1) == 1;
            var toggle = Ui.Button(bar, "Help", open ? "Hide how string works" : "How string works", () => {
                PlayerPrefs.SetInt(HelpKey, PlayerPrefs.GetInt(HelpKey, 1) == 1 ? 0 : 1);
                rebuild?.Invoke();
            }, false, 5f, Theme.FontSmall);
            toggle.Size(-1, 26f);
            if (!open) return;
            const float width = 676f;
            var lines = new[] {
                ("Tie", "Click where it's tied: a hole, a screw, a standoff, a shaft or an edge. A knot is drawn there."),
                ("Round", "Click each pulley, post or shaft it goes round (on the side the mouse is on; click it again for the other side), or a hole it goes through."),
                ("Wind", "End it on a winch drum and it's wound on: a motor or the hand crank turning the drum winds it on or lets it out. One turn of the drum takes up its distance round."),
                ("Pull", "In Simulate it can't stretch: a tight string stops what it's tied to, and doubled back round a pulley on what moves it pulls twice as hard (block and tackle). Point at it to see its pull; past its rating it breaks."),
                ("Release", "Tie an end round a pin (a screw, standoff or shaft) that a piston or motor pulls out of its hole: when the pin's end passes the loop, the string lets go."),
                ("Rules", "Any non-elastic string or rope up to 1/4\" thick is legal (R19d). VEX's rope comes in the Winch and Pulley Kit, 4 ft of it."),
            };
            foreach (var (title, text) in lines) {
                var row = Ui.Rect(title, bar);
                Ui.Row(row, 8f, null, TextAnchor.UpperLeft);
                var head = Ui.Text(row, "Title", title, Theme.FontSmall, Theme.Accent, Theme.Medium);
                head.Size(58f, 18f);
                var words = Ui.Text(row, "Text", text, Theme.FontSmall, Theme.TextDim);
                words.textWrappingMode = TMPro.TextWrappingModes.Normal;
                words.overflowMode = TMPro.TextOverflowModes.Overflow;
                var height = Mathf.Max(18f, words.GetPreferredValues(text, width - 66f, 0f).y + 2f);
                words.Size(width - 66f, height);
                row.Size(-1, height);
            }
        }

        public static void KindChoice(Transform row, StringKind kind, System.Action<StringKind> onKind) {
            var choice = Popup.Choice(row, StringKind.All.Select(k => k.name).ToArray(), Mathf.Max(0, StringKind.All.IndexOf(kind)), i => onKind(StringKind.All[i]), 220f);
            choice.Size(220f, 28f);
            Ui.Tip(choice, "String", null, "What it's made of. Any non-elastic rope or string up to 1/4\" thick is legal (R19d).");
        }

        public static void Spec(Transform bar, StringKind kind) {
            var spec = Ui.Text(bar, "Spec", kind.material + " · " + kind.RatingText + " · " + kind.source, Theme.FontSmall, Theme.TextDim);
            spec.textWrappingMode = TMPro.TextWrappingModes.Normal;
            spec.Size(-1, 34f);
        }

        /// <summary>"Slack − 1/2" +": extra length beyond tight, a quarter inch at a time.</summary>
        public static void SlackStepper(Transform parent, float slack, System.Action<float> changed) {
            var holder = Ui.Rect("Slack", parent);
            Ui.Row(holder, 4f);
            holder.Size(-1, 28f);
            Ui.Text(holder, "Label", "Slack", Theme.FontSmall, Theme.TextDim).Size(40f, 28f);
            var now = Mathf.Round(slack * 4f) / 4f;
            var minus = Ui.Button(holder, "Less", "−", () => changed(Mathf.Max(0f, now - 0.25f)), false, 5f);
            minus.Size(28f, 28f);
            minus.interactable = now > 0f;
            Ui.Text(holder, "Value", now <= 0f ? "tight" : Inch.Format(now), Theme.FontBody, Theme.Text, Theme.Medium, TMPro.TextAlignmentOptions.Center).Size(52f, 28f);
            var plus = Ui.Button(holder, "More", "+", () => changed(Mathf.Min(24f, now + 0.25f)), false, 5f);
            plus.Size(28f, 28f);
            Ui.Tip(holder, "Slack", null, "How much longer it is than the path it takes: tight, or hanging loose by this much.");
        }
    }

    /// <summary>V2 (build 88): the string as it will be, drawn while it's put on (its real thickness and color).</summary>
    public class StringPreview {
        private GameObject go;
        private Mesh mesh;
        private MeshRenderer renderer;
        private string materialKey;

        public void Show(StringPath path, List<Vector3> line, StringKind kind) {
            if (path == null || !path.Valid) {
                Hide();
                return;
            }
            if (go == null) {
                go = new GameObject("String Preview");
                mesh = new Mesh { name = "String preview" };
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                renderer = go.AddComponent<MeshRenderer>();
            }
            if (materialKey != kind.key) {
                materialKey = kind.key;
                renderer.sharedMaterial = StringPartGenerator.MaterialOf(kind);
            }
            var origin = line[0];
            go.transform.position = origin;
            StringLine.BuildMesh(mesh, path, line, kind, origin);
            go.SetActive(true);
        }

        public void Hide() {
            if (go != null) go.SetActive(false);
        }

        public void Dispose() {
            if (go != null) Object.Destroy(go);
            if (mesh != null) Object.Destroy(mesh);
            go = null;
            mesh = null;
        }
    }

    // V2 (build 88): a selected string changed where it lies: a dot on each point (its ends white). Drag one onto another
    // post, hole or edge to move it there; drag a middle one off into the open to take it out (or right-click it); click
    // one it goes round to send it round the other side. A gray dot follows the mouse along the string: drag it onto a
    // post to add one there. A panel sets its kind, slack and the turns on a drum. One undo step per change.
    public class StringEditor : MonoBehaviour {
        private static StringEditor instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            if (instance != null) return;
            instance = new GameObject("String Editor").AddComponent<StringEditor>();
        }

        private StringLine dragging;
        private int dragIndex;
        private bool inserting, moved;
        private Vector2 pressAt;
        private StringNode original;
        private IElement before;

        public static bool Busy => instance != null && (instance.dragging != null || instance.hovered);
        private bool hovered;

        /// <summary>Selects a string (with the Transform tool) so it can be changed.</summary>
        public static void Select(StringLine line) {
            if (line == null) return;
            if (ToolRail.Current != "Transform") ToolRail.Select("Transform");
            var manager = GameObject.Find("Primary Selection Manager")?.GetComponent<SelectionSystem.SelectionManager>();
            manager?.SetCurrent(new SelectionSystem.ObjectSelection { gameObject = line.gameObject });
        }

        private void Update() {
            hovered = false;
            var line = dragging;
            if (line == null) {
                var part = ToolRail.Current == "Transform" && !Placement.IsPlacing && !Sandbox.Running && !StringTool.Active && !ElasticTool.Active && !TubeTool.Active ? Tools.SelectTool.SelectedPart : null;
                line = part != null ? part.GetComponent<StringLine>() : null;
            }
            if (line == null || line.gameObject.IsDeleted() || line.nodes.Count == 0) {
                dragging = null;
                ShowPanel(null);
                return;
            }
            ShowPanel(line);
            var camera = Overlay.ViewCamera;
            if (camera == null) return;
            Overlay.Begin();
            var mouse = Overlay.Mouse;

            if (dragging != null) {
                if ((mouse - pressAt).magnitude > 5f) moved = true;
                var found = StringUi.PostAt(camera.ScreenPointToRay(MouseInput.Position), out var post) && !MouseInput.overUI;
                var middle = dragIndex > 0 && dragIndex < line.nodes.Count - 1;
                if (moved) {
                    if (found) {
                        line.nodes[dragIndex] = new StringNode { post = post, side = 0 };
                        StringUi.MarkSpools(line.nodes);
                        Overlay.MouseText(StringUi.What(post, !middle) + "\n" + line.Describe().Split('\n')[0], Color.white);
                    }
                    else {
                        line.nodes[dragIndex] = original.Copy();
                        Overlay.MouseText(middle || inserting ? "Let go here to take this point out" : "Onto a hole, post or edge", middle || inserting ? Theme.Warning : Color.white);
                    }
                    line.editing = true;
                    line.Refresh(true);
                }
                DrawDots(line, dragIndex);
                if (!MouseInput.LeftButton.isPressed) {
                    if (!moved && !inserting && line.nodes[dragIndex].Round && middle) {
                        // A click on a post it goes round: the other side.
                        var way = dragIndex < line.Path.ways.Length ? line.Path.ways[dragIndex] : 1;
                        line.nodes[dragIndex].side = way == 0 ? -1 : -way;
                    }
                    else if (moved && !found && (middle || inserting)) line.nodes.RemoveAt(dragIndex);
                    else if (!moved && inserting) line.nodes.RemoveAt(dragIndex);
                    line.editing = false;
                    line.Refresh(true);
                    line.Save();
                    if (!(inserting && !moved)) {
                        StateSystem.AddElement(before);
                        StateSystem.AddState(new StringLine.StringElement(line));
                        Audio.Sounds.Play(Audio.Sounds.Tick);
                    }
                    dragging = null;
                }
                return;
            }

            var onInterface = OverInterface();
            var near = -1;
            var best = onInterface ? 0f : 11f;
            for (var i = 0; i < line.nodes.Count; i++)
                if (Overlay.ToScreen(Spot(line, i), out var at) && Vector2.Distance(at, mouse) < best) {
                    best = Vector2.Distance(at, mouse);
                    near = i;
                }
            DrawDots(line, near);
            if (near >= 0) {
                hovered = true;
                var middle = near > 0 && near < line.nodes.Count - 1;
                Overlay.MouseText("Drag onto another post to move it" + (middle ? (line.nodes[near].Round ? "\nClick: round its other side" : "") + "\nRight-click: take it out" : ""), Color.white);
                if (MouseInput.LeftButton.wasPressedThisFrame) StartDrag(line, near, false);
                else if (MouseInput.RightButton.wasPressedThisFrame && middle) {
                    var undo = new StringLine.StringElement(line);
                    line.nodes.RemoveAt(near);
                    line.Refresh(true);
                    line.Save();
                    StateSystem.AddElement(undo);
                    StateSystem.AddState(new StringLine.StringElement(line));
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                }
                return;
            }
            if (onInterface || !NearestOnLine(line, mouse, out var point, out var screen)) return;
            hovered = true;
            Overlay.Disc(screen, 6f, new Color(0.75f, 0.77f, 0.8f, 1f), true);
            Overlay.MouseText("Drag onto a post to send the string round it", Color.white);
            if (MouseInput.LeftButton.wasPressedThisFrame) {
                var index = InsertIndex(line, point);
                var undo = new StringLine.StringElement(line);
                line.nodes.Insert(index, new StringNode { post = new BandPost { part = null, local = point, radius = 0f } });
                StartDrag(line, index, true);
                before = undo;
            }
        }

        private void StartDrag(StringLine line, int index, bool insert) {
            before = new StringLine.StringElement(line);
            dragging = line;
            dragIndex = index;
            inserting = insert;
            moved = false;
            pressAt = Overlay.Mouse;
            original = line.nodes[index].Copy();
        }

        // Where a point's dot is: on the string where it touches (a post's middle is inside it).
        private static Vector3 Spot(StringLine line, int i) {
            var center = line.World(line.nodes[i]).center;
            if (line.Line.Count == 0) return center;
            var best = line.Line[0];
            foreach (var p in line.Line)
                if ((p - center).sqrMagnitude < (best - center).sqrMagnitude) best = p;
            return line.nodes[i].Round ? best : center;
        }

        private static void DrawDots(StringLine line, int lit) {
            for (var i = 0; i < line.nodes.Count; i++) {
                if (!Overlay.ToScreen(Spot(line, i), out var at)) continue;
                var end = i == 0 || i == line.nodes.Count - 1;
                var color = i == lit ? Theme.AccentHover : end ? Color.white : Theme.Accent;
                Overlay.Disc(at, i == lit ? 7.5f : 6f, color, true);
                if (end) Overlay.Ring(at, 9f, color, 1.5f);
            }
        }

        private static readonly List<UnityEngine.EventSystems.RaycastResult> uiHits = new();

        private static bool OverInterface() {
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events == null) return false;
            uiHits.Clear();
            events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events) { position = MouseInput.Position }, uiHits);
            foreach (var hit in uiHits)
                if (Overlay.Instance == null || hit.gameObject != Overlay.Instance.gameObject) return true;
            return false;
        }

        private static bool NearestOnLine(StringLine line, Vector2 mouse, out Vector3 point, out Vector2 screen) {
            point = default;
            screen = default;
            var best = 8f;
            var found = false;
            var l = line.Line;
            for (var i = 0; i + 1 < l.Count; i++) {
                if (!Overlay.ToScreen(l[i], out var a) || !Overlay.ToScreen(l[i + 1], out var b)) continue;
                var ab = b - a;
                var s = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(mouse - a, ab) / ab.sqrMagnitude) : 0f;
                var on = a + ab * s;
                var d = Vector2.Distance(on, mouse);
                if (d < best) {
                    best = d;
                    point = Vector3.Lerp(l[i], l[i + 1], s);
                    screen = on;
                    found = true;
                }
            }
            return found;
        }

        // After the last point that comes before it along the string.
        private static int InsertIndex(StringLine line, Vector3 point) {
            float Along(Vector3 p) {
                var l = line.Line;
                float s = 0f, bestS = 0f, best = float.MaxValue;
                for (var i = 0; i + 1 < l.Count; i++) {
                    var ab = l[i + 1] - l[i];
                    var t = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - l[i], ab) / ab.sqrMagnitude) : 0f;
                    var d = (l[i] + ab * t - p).sqrMagnitude;
                    if (d < best) {
                        best = d;
                        bestS = s + ab.magnitude * t;
                    }
                    s += ab.magnitude;
                }
                return bestS;
            }
            var at = Along(point);
            for (var i = 1; i < line.nodes.Count; i++)
                if (Along(Spot(line, i)) > at) return Mathf.Max(1, i);
            return Mathf.Max(1, line.nodes.Count - 1);
        }

        // ------------------------------------------------------------------ the panel

        private StringLine panelFor;
        private RectTransform panel;
        private TMPro.TextMeshProUGUI info;
        private string panelKey;

        private void ShowPanel(StringLine line) {
            if (info != null && line != null) info.text = line.Describe();
            var key = line == null ? null : line.kind.key + "|" + (Mathf.Round(line.Slack * 4f) / 4f).ToString(CultureInfo.InvariantCulture) + "|" + line.turns + "|" + line.nodes.Any(n => n.spool);
            if (line == panelFor && key == panelKey && (line == null || panel != null)) return;
            if (dragging != null && panel != null) return;
            if (panel != null) Destroy(panel.gameObject);
            panel = null;
            panelFor = line;
            panelKey = key;
            if (line == null || Interface.Interface.Root == null) return;
            panel = Ui.Panel(Interface.Interface.Root, "String Panel", Theme.Panel, 10f).rectTransform;
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(76f, -(Layout.TopBarBottom + 60f));
            panel.sizeDelta = new Vector2(420f, 0f);
            Ui.Column(panel, 8f, new RectOffset(14, 14, 12, 14));
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Text(panel, "Title", "String", Theme.FontTitle, Theme.Text, Theme.Medium).Size(-1, 26f);
            info = Ui.Text(panel, "Info", line.Describe(), Theme.FontSmall, Theme.TextDim);
            info.textWrappingMode = TMPro.TextWrappingModes.Normal;
            info.Size(-1, 36f);
            var row = Ui.Rect("Row", panel);
            Ui.Row(row, 10f);
            row.Size(-1, 30f);
            StringUi.KindChoice(row, line.kind, k => Change(line, () => line.UseKind(k)));
            StringUi.SlackStepper(row, Mathf.Max(0f, line.Slack), s => Change(line, () => line.length = line.Path.tight + s));
            if (line.nodes.Any(n => n.spool)) {
                var turns = Ui.Rect("Turns", panel);
                Ui.Row(turns, 10f);
                turns.Size(-1, 30f);
                BandUi.Stepper(turns, "Turns", Mathf.RoundToInt(line.turns), 1, 30, t => Change(line, () => line.turns = t), "How many times it's wound round the drum already (each turn takes up a drum's round of string).");
            }
            StringUi.Spec(panel, line.kind);
            Ui.Hint(panel, "Drag a dot", "move a point", "Click a post's dot", "other side");
            Ui.Hint(panel, "Drag the string", "onto a post", "Right-click a dot", "take it out");
        }

        private static void Change(StringLine line, System.Action change) {
            var undo = new StringLine.StringElement(line);
            change();
            line.Refresh(true);
            line.Save();
            StateSystem.AddElement(undo);
            StateSystem.AddState(new StringLine.StringElement(line));
            Audio.Sounds.Play(Audio.Sounds.Tick);
        }

        private void OnDestroy() {
            if (panel != null) Destroy(panel.gameObject);
        }
    }
}
