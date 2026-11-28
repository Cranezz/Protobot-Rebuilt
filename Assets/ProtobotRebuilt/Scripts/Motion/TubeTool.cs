using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Motion {
    /// <summary>Where the mouse would put a tube's next point.</summary>
    public struct TubeTarget {
        public bool valid;
        public int kind;
        public GameObject part;
        public Vector3 point, dir;
        public float depth;
        public string what;
    }

    // V2: running pneumatic tubing. Right-click a fitting > Run tubing from here (or Pneumatics > Tubing in the parts
    // list, then click a fitting's end): the tube starts pushed into that port and follows the mouse, drawn as it will
    // be. Click (or Enter) puts a point down where the mouse is: on a part's face (it lies on it), a corner or edge
    // (snaps), through a hole (goes straight through it), or in the air (level with the last point). Click another
    // fitting's end to push it in and finish; Finish leaves an open end. Backspace takes the last point off, Esc stops
    // without making it.
    public class TubeTool : MonoBehaviour {
        private static TubeTool active;
        public static bool Active => active != null;

        private readonly List<TubeNode> nodes = new();   // a node with no part: local is a world point
        private GameObject preview;
        private Mesh previewMesh;
        private RectTransform window, body;
        private TMPro.TextMeshProUGUI readout;
        private TubeTarget target;
        // How smoothly the next tube bends (kept for the session, like the Bend tool's).
        private static float radius = PneumaticTube.BendRadius;
        // V2: wiring uses this tool too (a Wire is a tube of another kind): which cable, or null for tubing.
        private WireKind? wire;

        /// <summary>Runs a cable from a port (or, with none, from the first port clicked).</summary>
        public static void BeginWire(WireKind kind, WirePort from = null) {
            Begin();
            if (active == null) return;
            active.wire = kind;
            Destroy(active.window.gameObject);
            active.BuildWindow();
            if (from != null)
                active.nodes.Add(new TubeNode { part = from.part, local = from.local, dirLocal = from.outLocal, kind = TubeNode.Port, depth = Wire.PortDepth(kind) });
            active.Refresh();
        }

        public static void Begin(HoleCollider port = null) {
            if (Sandbox.Running) return;
            if (active != null) Destroy(active.gameObject);
            if (Placement.IsPlacing) Placement.Instance.StopPlacing();
            ChainTool.Stop();
            ElasticTool.Stop();
            StringTool.Stop();   // V2 (build 88)
            foreach (var manager in FindObjectsByType<SelectionSystem.SelectionManager>(FindObjectsSortMode.None)) manager.ClearCurrent();
            active = Ui.Rect("Tube Tool", Interface.Interface.Root).Fill().gameObject.AddComponent<TubeTool>();
            active.Build();
            if (port != null) {
                var (point, outward, depth) = PneumaticTube.PortFrame(port);
                active.nodes.Add(new TubeNode { part = port.transform.parent.gameObject, local = port.transform.parent.InverseTransformPoint(point),
                                                dirLocal = port.transform.parent.InverseTransformDirection(outward), kind = TubeNode.Port, depth = depth });
            }
            active.Refresh();
        }

        public static void Stop() {
            if (active == null) return;
            Destroy(active.gameObject);
            active = null;
        }

        private void OnDestroy() {
            ToolBlock.Pop();
            if (preview != null) Destroy(preview);
            if (previewMesh != null) Destroy(previewMesh);
            if (active == this) active = null;
        }

        private void Build() {
            ToolBlock.Push();
            preview = new GameObject("Tube Preview");
            previewMesh = new Mesh { name = "Tube preview" };
            preview.AddComponent<MeshFilter>().sharedMesh = previewMesh;
            preview.AddComponent<MeshRenderer>().sharedMaterial = TubePartGenerator.Material;
            BuildWindow();
        }

        private string Noun => wire == null ? "tubing" : wire == WireKind.Smart ? "smart cable" : "3-wire cable";
        private string EndWord => wire == null ? "fitting" : "port";

        private void BuildWindow() {

            window = Ui.Panel(transform, "Window", Theme.Panel, 10f).rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0f, 1f);
            window.pivot = new Vector2(0f, 1f);
            window.anchoredPosition = new Vector2(76f, -(Layout.TopBarBottom + 60f));
            window.sizeDelta = new Vector2(380f, 0f);
            Ui.Column(window, 10f, new RectOffset(14, 14, 12, 14));
            window.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            window.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            body = Ui.Rect("Body", window);
            Ui.Column(body, 8f);
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Ui.Text(body, "Title", wire == null ? "Pneumatic tubing" : wire == WireKind.Smart ? "V5 Smart Cable" : "3-wire cable", Theme.FontTitle, Theme.Text, Theme.Medium).Size(-1, 28f);
            var spec = Ui.Text(body, "Spec", wire == null ? "4 mm OD x 2.5 mm ID polyurethane (V5 kit)" : wire == WireKind.Smart ? "Flat 4-wire cable, a clear plug on each end: the brain to a motor or V5 sensor"
                                  : "Black, red and white wires: a brain 3-wire port (A-H) to a sensor, switch or solenoid", Theme.FontSmall, Theme.TextDim);
            spec.textWrappingMode = TMPro.TextWrappingModes.Normal;
            spec.Size(-1, wire == null ? 18f : 36f);
            readout = Ui.Text(body, "Readout", "", Theme.FontBody, Theme.Text);
            readout.textWrappingMode = TMPro.TextWrappingModes.Normal;
            readout.Size(-1, 44f);
            Dialog.Heading(body, "How it bends round its points");
            PneumaticTube.BendSlider(body, radius, r => {
                radius = r;
                Refresh();
            });
            Ui.Hint(body, "Click / Enter", "put a point here", "Backspace", "undo a point");
            Ui.Hint(body, wire == null ? "Click a fitting's end" : "Click a port", "finish in it", "Esc", "stop");
            var buttons = Ui.Rect("Buttons", window);
            Ui.Row(buttons, 8f, null, TextAnchor.MiddleRight);
            buttons.Size(-1, 32f);
            Ui.Button(buttons, "Cancel", "Cancel", Stop, false, 6f).Size(-1, 30f);
            var finish = Ui.Button(buttons, "Finish", "Finish (open end)", () => Finish(), true, 6f);
            finish.Size(-1, 30f);
            Ui.Tip(finish, "Finish here", null, "Makes the tubing as far as it goes, with its end open (cut).");
        }

        private void Refresh() {
            if (readout == null) return;
            if (nodes.Count == 0) {
                readout.text = wire == null ? "Click a fitting's push-in end (or a tee's, a valve's) to start the tube there."
                    : wire == WireKind.Smart ? "Click a port on the brain, a motor or a V5 sensor to start the cable there."
                    : "Click a 3-wire port on the brain (A-H), or a sensor, switch or solenoid, to start the cable there.";
                readout.color = Theme.TextDim;
                return;
            }
            var line = PneumaticTube.BuildLine(World(nodes), radius);
            readout.text = (nodes.Count == 1 ? "Started. " : nodes.Count + " points, ") + Inch.Format(Mathf.Round(Strands.Length(line) * 8f) / 8f) + " so far. Put points where it should run, then click the " + EndWord + " it goes to.";
            readout.color = Theme.Text;
        }

        private static List<(Vector3 p, Vector3 d, int kind, float depth)> World(IEnumerable<TubeNode> list) =>
            list.Select(n => (n.part != null ? n.part.transform.TransformPoint(n.local) : n.local,
                              n.dirLocal == Vector3.zero ? Vector3.zero : (n.part != null ? n.part.transform.TransformDirection(n.dirLocal) : n.dirLocal).normalized,
                              n.kind, n.depth)).ToList();

        private static Vector3 WorldOf(TubeNode n) => n.part != null ? n.part.transform.TransformPoint(n.local) : n.local;

        private TubeNode NodeFor(TubeTarget t) {
            var part = t.part != null ? t.part : nodes.Count > 0 ? nodes[^1].part : null;
            return new TubeNode {
                part = part, kind = t.kind, depth = t.depth,
                local = part != null ? part.transform.InverseTransformPoint(t.point) : t.point,
                dirLocal = t.dir == Vector3.zero ? Vector3.zero : part != null ? part.transform.InverseTransformDirection(t.dir) : t.dir,
            };
        }

        private void Update() {
            if (Sandbox.Running) {
                Stop();
                return;
            }
            var keyboard = Keyboard.current;
            var camera = Overlay.ViewCamera;
            if (camera == null) return;
            var enter = false;
            if (keyboard != null && !InputEvents.TextInput.IsTyping && !InputEvents.TextInput.ShortcutsPaused) {
                if (keyboard.escapeKey.wasPressedThisFrame) {
                    Stop();
                    return;
                }
                if (keyboard.backspaceKey.wasPressedThisFrame && nodes.Count > 0) {
                    nodes.RemoveAt(nodes.Count - 1);
                    Refresh();
                }
                enter = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            }
            var last = nodes.Count > 0 ? WorldOf(nodes[^1]) : camera.transform.position + camera.transform.forward * 12f;
            target = MouseInput.overUI ? default : Pick(camera, last, null, true, wire);
            var click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !MouseInput.overUI;
            if ((click || enter) && target.valid) Place(target);
            Draw(camera);
        }

        private void Place(TubeTarget t) {
            if (nodes.Count > 0 && (t.point - WorldOf(nodes[^1])).magnitude < 0.05f) return;
            nodes.Add(NodeFor(t));
            Audio.Sounds.Play(Audio.Sounds.Tick);
            // A fitting's end after the start: pushed in, done.
            if (t.kind == TubeNode.Port && nodes.Count > 1) {
                Finish();
                return;
            }
            Refresh();
        }

        private void Finish() {
            if (nodes.Count < 2) {
                Interface.Interface.Say("Put down at least one more point (or click the " + EndWord + " it goes to)", false);
                return;
            }
            var type = PartsManager.GetPartType(wire == null ? "PTUB" : "WIRE");
            var generator = type.GetComponent<PartGenerator>();
            if (wire != null) generator.param1.value = wire == WireKind.Smart ? "V5" : "3W";
            var start = WorldOf(nodes[0]);
            var obj = generator.Generate(start, Quaternion.identity);
            var tube = obj.GetComponent<PneumaticTube>();
            tube.bendRadius = radius;
            // Nodes on no part were world points: in the tube's own space now (it sits at its start, unturned).
            var route = nodes.Select(n => {
                var c = n.Copy();
                if (c.part == null) c.local = obj.transform.InverseTransformPoint(n.local);
                return c;
            }).ToList();
            tube.Set(route);
            StateSystem.AddElement(new ObjectElement(obj) { existing = false });
            StateSystem.AddState(new State(new List<IElement> { new ObjectElement(obj), new PneumaticTube.TubeElement(tube) }));
            Audio.Sounds.Play(Audio.Sounds.Clack);
            Stop();
        }

        // A port node's plug: its mouth, the way out, and the socket's long side.
        private static Vector3 PlugAt(TubeNode node, (Vector3 p, Vector3 d, int kind, float depth) at, out Vector3 outward, out Vector3 up) {
            outward = at.d.sqrMagnitude > 1e-6f ? at.d : Vector3.up;
            var port = node.part != null ? WirePorts.Near(node.part, at.p, 0.2f) : null;
            up = Vector3.ProjectOnPlane(port != null ? port.Up : Mathf.Abs(outward.y) < 0.9f ? Vector3.up : Vector3.forward, outward).normalized;
            return at.p;
        }

        private void Draw(Camera camera) {
            Overlay.Begin();
            var route = nodes.ToList();
            if (target.valid) route.Add(NodeFor(target));
            var world = World(route);
            var line = PneumaticTube.BuildLine(world, radius);
            previewMesh.Clear();
            if (wire != null && line.Count >= 2) {
                // A cable: drawn as it will be (flat, or three wires), with its plugs in the ports it's in.
                var renderer = preview.GetComponent<MeshRenderer>();
                if (renderer.sharedMaterials.Length != 4) renderer.sharedMaterials = Wire.Materials(wire.Value);
                preview.transform.position = line[0];
                Vector3? start = null, end = null;
                Vector3 startOut = default, startUp = default, endOut = default, endUp = default;
                if (route.Count > 0 && route[0].kind == TubeNode.Port) start = PlugAt(route[0], world[0], out startOut, out startUp);
                if (route.Count > 1 && route[^1].kind == TubeNode.Port) end = PlugAt(route[^1], world[^1], out endOut, out endUp);
                Wire.BuildMesh(previewMesh, line, wire.Value, line[0], start, startOut, startUp, end, endOut, endUp,
                               route.Count > 0 && route[0].kind == TubeNode.Port && WirePorts.IsBrain(route[0].part),
                               route.Count > 1 && route[^1].kind == TubeNode.Port && WirePorts.IsBrain(route[^1].part));
            }
            else if (line.Count >= 2) {
                var v = new List<Vector3>();
                var n = new List<Vector3>();
                var t = new List<int>();
                Strands.Tube(line, false, PneumaticTube.Radius, 10, v, n, t);
                previewMesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                previewMesh.SetVertices(v);
                previewMesh.SetNormals(n);
                previewMesh.SetTriangles(t, 0);
                previewMesh.RecalculateBounds();
            }
            foreach (var node in nodes)
                if (Overlay.ToScreen(WorldOf(node), out var at)) Overlay.Disc(at, 5f, node.kind == TubeNode.Port ? Color.white : Theme.Accent);
            if (!target.valid) return;
            if (Overlay.ToScreen(target.point, out var p)) {
                var color = target.kind == TubeNode.Port ? Theme.Success : target.kind == TubeNode.Hole ? Theme.Warning : Color.white;
                Overlay.Ring(p, 8f, color, 2f);
                if (target.kind == TubeNode.Hole) Overlay.WorldCircle(target.point, target.dir, 0.12f, color, 2f);
            }
            var length = Strands.Length(line);
            var label = target.what + (nodes.Count > 0 ? "\n" + Inch.Format(Mathf.Round(length * 8f) / 8f) + " of " + Noun : "");
            if (nodes.Count > 0) label += target.kind == TubeNode.Port && wire != null ? "\nClick: plug it in and finish" : target.kind == TubeNode.Port ? "\nClick: push it in and finish" : "\nClick or Enter: put a point here";
            Overlay.MouseText(label, Color.white);
        }

        // ------------------------------------------------------------------ what's under the mouse

        /// <summary>The next point under the mouse: a free port (a fitting's push-in end), a hole to go through, a
        /// corner or edge (snapped) or a face (the tube lies on it), else in the air level with the last point.</summary>
        public static TubeTarget Pick(Camera camera, Vector3 level, PneumaticTube self, bool ports, WireKind? wire = null) {
            var ray = camera.ScreenPointToRay(MouseInput.Position);
            // V2: a cable's end: a port of its kind under the mouse (the brain's numbered ones, a device's socket).
            if (wire != null && ports) {
                var port = WirePorts.Under(ray, wire.Value, self as Wire);
                if (port != null)
                    return new TubeTarget { valid = true, kind = TubeNode.Port, part = port.part, point = port.Position, dir = port.Out, depth = Wire.PortDepth(wire.Value),
                                            what = "Into " + (WirePorts.IsBrain(port.part) ? "the brain's " + port.label.Replace("Port", "port")
                                                              : port.part.TryGetComponent(out MotorDrive m) ? m.Label : "the " + PartLabels.Short(port.part)) };
            }
            if (wire != null) ports = false;   // (not pneumatic ports)
            var hits = Physics.RaycastAll(ray, Mathf.Infinity, LayerMask.GetMask("Default", "HoleCollisions"), QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits) {
                if (hit.collider.TryGetComponent(out HoleCollider hole)) {
                    var t = HoleTarget(hole, self, ports);
                    if (t.valid) return t;
                    continue;
                }
                var saved = hit.collider.GetComponentInParent<SavedObject>();
                if (saved == null || saved.gameObject.IsDeleted()) continue;
                var part = SelectAs.Resolve(hit.collider.gameObject) ?? saved.gameObject;
                if (part.GetComponent<PneumaticTube>() != null || part.GetComponent<ElasticBand>() != null) continue;
                // Snapping: a hole (its rim near the mouse), a corner, an edge.
                var feature = FeaturePicker.Pick(part);
                if (feature.Valid && feature.hole != null && (feature.kind == FeaturePicker.Kind.Hole || feature.kind == FeaturePicker.Kind.HoleFace)) {
                    var t = HoleTarget(feature.hole, self, ports);
                    if (t.valid) return t;
                }
                var off = hit.normal * (PneumaticTube.Radius * 1.05f);
                if (feature.Valid && (feature.kind == FeaturePicker.Kind.Corner || feature.kind == FeaturePicker.Kind.Midpoint || feature.kind == FeaturePicker.Kind.Edge))
                    // V2: on a corner or edge the tube's side touches it (it bends round it: BuildLine).
                    return new TubeTarget { valid = true, kind = TubeNode.Edge, part = part, point = feature.point, dir = hit.normal, what = feature.Describe() + " of the " + PartLabels.Short(part) };
                return new TubeTarget { valid = true, kind = TubeNode.Point, part = part, point = hit.point + off, what = "On the " + PartLabels.Short(part) };
            }
            // In the air: as far away as the last point.
            var plane = new Plane(-camera.transform.forward, level);
            if (!plane.Raycast(ray, out var distance)) return default;
            return new TubeTarget { valid = true, kind = TubeNode.Point, point = ray.GetPoint(distance), what = "In the air" };
        }

        private static TubeTarget HoleTarget(HoleCollider hole, PneumaticTube self, bool ports) {
            var owner = hole.transform.parent != null ? hole.transform.parent.gameObject : null;
            if (owner == null || owner.IsDeleted() || owner.GetComponent<SavedObject>() == null) return default;
            if (PneumaticTube.IsPort(hole)) {
                if (!ports || PneumaticTube.PortTaken(hole, self)) return default;
                var (point, outward, depth) = PneumaticTube.PortFrame(hole);
                return new TubeTarget { valid = true, kind = TubeNode.Port, part = owner, point = point, dir = outward, depth = depth, what = "Into the " + PartLabels.Short(owner) };
            }
            if (!hole.IsEmpty) return default;
            return new TubeTarget { valid = true, kind = TubeNode.Hole, part = owner, point = hole.transform.position, dir = hole.transform.forward,
                                    depth = Mathf.Abs(hole.transform.lossyScale.z), what = "Through a hole in the " + PartLabels.Short(owner) };
        }

        /// <summary>The free push-in port of a part nearest a point (right-click > Run tubing from here).</summary>
        public static HoleCollider PortNear(GameObject part, Vector3 point) =>
            part.GetComponentsInChildren<HoleCollider>(true).Where(h => h.transform.parent == part.transform && PneumaticTube.IsPort(h) && !PneumaticTube.PortTaken(h))
                .OrderBy(h => (h.transform.position - point).sqrMagnitude).FirstOrDefault();
    }

    // V2: a selected tube's route, edited where it lies: its points are blue dots (its ends white), drag one onto a
    // face, corner, hole or another fitting's end (the ends); a gray dot follows the mouse along the tube: click it to
    // add a point there and drag it. Right-click a point to take it out. A panel shows its length and how smoothly it
    // bends (the Bend tool's Tight-Gentle slider). One undo step per change.
    public class TubeEditor : MonoBehaviour {
        private static TubeEditor instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            Tools.Keybinds.Add("TubeZipTie", "Tubing and wiring", "Zip tie the point under the mouse (a selected tube or cable)", "Z");   // V2
            if (instance != null) return;
            instance = new GameObject("Tube Editor").AddComponent<TubeEditor>();
        }

        private PneumaticTube dragging;
        private int dragIndex;
        private IElement before;

        /// <summary>The mouse is on one of a selected tube's dots (or dragging one).</summary>
        public static bool Busy => instance != null && (instance.dragging != null || instance.hovered);
        private bool hovered;

        private void Update() {
            hovered = false;
            var tube = dragging;
            if (tube == null) {
                var part = ToolRail.Current == "Transform" && !Placement.IsPlacing && !Sandbox.Running && !TubeTool.Active && !ElasticTool.Active ? Tools.SelectTool.SelectedPart : null;
                tube = part != null ? part.GetComponent<PneumaticTube>() : null;
            }
            if (tube == null || tube.gameObject.IsDeleted() || tube.nodes.Count == 0) {
                dragging = null;
                ShowPanel(null);
                return;
            }
            ShowPanel(tube);
            // The mouse on the interface (this panel, the parts list): the tube's dots leave it alone.
            var onInterface = dragging == null && OverInterface();
            var camera = Overlay.ViewCamera;
            if (camera == null) return;
            Overlay.Begin();
            var mouse = Overlay.Mouse;

            if (dragging != null) {
                var level = tube.nodes[dragIndex].Position(tube.transform);
                var ends = dragIndex == 0 || dragIndex == tube.nodes.Count - 1;
                var t = TubeTool.Pick(camera, level, tube, ends, tube is Wire w ? w.kind : (WireKind?)null);
                if (t.valid && (t.kind != TubeNode.Port || ends)) {
                    var fallback = tube.nodes[dragIndex > 0 ? dragIndex - 1 : Mathf.Min(1, tube.nodes.Count - 1)].part;
                    tube.nodes[dragIndex] = PneumaticTube.Node(tube.transform, t.part != null ? t.part : fallback, t.point, t.dir, t.kind, t.depth);
                    tube.editing = true;
                    tube.Refresh(true);
                    Overlay.MouseText(t.what + "\n" + tube.Describe(tube.Length).Split((char)10)[0], Color.white);
                }
                DrawDots(tube, -1);
                if (!MouseInput.LeftButton.isPressed) {
                    tube.editing = false;
                    tube.Refresh(true);
                    tube.Save();
                    StateSystem.AddElement(before);
                    StateSystem.AddState(new PneumaticTube.TubeElement(tube));
                    dragging = null;
                }
                return;
            }

            // The dot under the mouse, else the nearest point on the tube.
            var near = -1;
            var best = onInterface ? 0f : 11f;
            for (var i = 0; i < tube.nodes.Count; i++)
                if (Overlay.ToScreen(tube.World(tube.nodes[i]).p, out var at) && Vector2.Distance(at, mouse) < best) {
                    best = Vector2.Distance(at, mouse);
                    near = i;
                }
            DrawDots(tube, near);
            if (near >= 0) {
                hovered = true;
                var middle = near > 0 && near < tube.nodes.Count - 1;
                var tieKey = Tools.Keybinds.Label("TubeZipTie");
                var tieable = middle && tube.nodes[near].part != null && tube.nodes[near].kind != TubeNode.Hole;
                Overlay.MouseText("Drag to move this point" + (middle ? " · right-click: take it out" : near == 0 || near == tube.nodes.Count - 1 ? (tube is Wire ? " (onto another port)" : " (onto another fitting's end)") : "")
                                  + (tieable && tieKey != "" ? "\n" + tieKey + ": " + (tube.nodes[near].tie ? "take off the zip tie" : "zip tie it to the part") : ""), Color.white);
                if (tieable && Tools.Keybinds.Pressed("TubeZipTie") && !InputEvents.TextInput.IsTyping) {   // V2: zip ties
                    var undo = new PneumaticTube.TubeElement(tube);
                    tube.nodes[near].tie = !tube.nodes[near].tie;
                    tube.Refresh(true);
                    tube.Save();
                    StateSystem.AddElement(undo);
                    StateSystem.AddState(new PneumaticTube.TubeElement(tube));
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                }
                if (MouseInput.LeftButton.wasPressedThisFrame) StartDrag(tube, near);
                else if (MouseInput.RightButton.wasPressedThisFrame && middle) {
                    var undo = new PneumaticTube.TubeElement(tube);
                    tube.nodes.RemoveAt(near);
                    tube.Refresh(true);
                    tube.Save();
                    StateSystem.AddElement(undo);
                    StateSystem.AddState(new PneumaticTube.TubeElement(tube));
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                }
                return;
            }
            if (onInterface || !NearestOnLine(tube, mouse, out var point, out var screen)) return;
            hovered = true;
            Overlay.Disc(screen, 6f, new Color(0.75f, 0.77f, 0.8f, 1f), true);
            Overlay.MouseText("Click to add a point here and drag it", Color.white);
            if (MouseInput.LeftButton.wasPressedThisFrame) {
                var index = InsertIndex(tube, point);
                var undo = new PneumaticTube.TubeElement(tube);
                var neighbour = tube.nodes[Mathf.Clamp(index - 1, 0, tube.nodes.Count - 1)].part;
                var part = Strands.PartAt(point, 0.2f, p => p.GetComponent<PneumaticTube>() != null || p.GetComponent<ElasticBand>() != null) ?? neighbour;
                tube.nodes.Insert(index, PneumaticTube.Node(tube.transform, part, point, Vector3.zero, TubeNode.Point, 0f));
                StartDrag(tube, index);
                before = undo;
            }
        }

        // ------------------------------------------------------------------ the panel

        private PneumaticTube panelFor;
        private RectTransform panel;
        private TMPro.TextMeshProUGUI lengthLabel;
        private IElement bendBefore;
        private float panelRadius;

        private void ShowPanel(PneumaticTube tube) {
            if (lengthLabel != null && tube != null) lengthLabel.text = tube.Describe(tube.Length);
            // Its radius changed another way (undo, redo): the slider shows it again.
            var stale = tube != null && bendBefore == null && Mathf.Abs(tube.bendRadius - panelRadius) > 1e-4f;
            if (tube == panelFor && (tube == null || panel != null) && !stale) return;
            if (panel != null) Destroy(panel.gameObject);
            panel = null;
            panelFor = tube;
            if (tube == null || Interface.Interface.Root == null) return;
            panel = Ui.Panel(Interface.Interface.Root, "Tubing Panel", Theme.Panel, 10f).rectTransform;
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(76f, -(Layout.TopBarBottom + 60f));
            panel.sizeDelta = new Vector2(360f, 0f);
            Ui.Column(panel, 8f, new RectOffset(14, 14, 12, 14));
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Text(panel, "Title", tube.Title, Theme.FontTitle, Theme.Text, Theme.Medium).Size(-1, 26f);
            lengthLabel = Ui.Text(panel, "Length", "", Theme.FontSmall, Theme.TextDim);
            lengthLabel.Size(-1, 18f);
            Dialog.Heading(panel, "How it bends round its points");
            panelRadius = tube.bendRadius;
            PneumaticTube.BendSlider(panel, tube.bendRadius, r => {
                bendBefore ??= new PneumaticTube.TubeElement(tube);
                tube.bendRadius = panelRadius = r;
                tube.Refresh(true);
            }, r => {
                tube.bendRadius = panelRadius = r;
                tube.Refresh(true);
                tube.Save();
                if (bendBefore != null) {
                    StateSystem.AddElement(bendBefore);
                    StateSystem.AddState(new PneumaticTube.TubeElement(tube));
                }
                bendBefore = null;
            });
            Ui.Hint(panel, "Drag a dot", "move a point", "Click the tube", "add one");
            Ui.Hint(panel, "Right-click a dot", "take it out");
            ShowPanel(tube);
        }

        private void OnDestroy() {
            if (panel != null) Destroy(panel.gameObject);
        }

        private static readonly List<UnityEngine.EventSystems.RaycastResult> uiHits = new();

        // Whether the mouse is on the interface itself (not on the overlay's handles, which are the tube's dots).
        private static bool OverInterface() {
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events == null) return false;
            uiHits.Clear();
            events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events) { position = MouseInput.Position }, uiHits);
            foreach (var hit in uiHits)
                if (Overlay.Instance == null || hit.gameObject != Overlay.Instance.gameObject) return true;
            return false;
        }

        private void StartDrag(PneumaticTube tube, int index) {
            before = new PneumaticTube.TubeElement(tube);
            dragging = tube;
            dragIndex = index;
        }

        private static void DrawDots(PneumaticTube tube, int lit) {
            for (var i = 0; i < tube.nodes.Count; i++) {
                if (!Overlay.ToScreen(tube.World(tube.nodes[i]).p, out var at)) continue;
                var end = tube.nodes[i].kind == TubeNode.Port || i == 0 || i == tube.nodes.Count - 1;
                var color = i == lit ? Theme.AccentHover : end ? Color.white : Theme.Accent;
                Overlay.Disc(at, i == lit ? 7.5f : 6f, color, true);
                if (end) Overlay.Ring(at, 9f, color, 1.5f);
            }
        }

        // The tube's line on screen: the nearest point within a few pixels of the mouse.
        private static bool NearestOnLine(PneumaticTube tube, Vector2 mouse, out Vector3 point, out Vector2 screen) {
            point = default;
            screen = default;
            var best = 8f;
            var found = false;
            var line = tube.Line;
            for (var i = 0; i + 1 < line.Count; i++) {
                if (!Overlay.ToScreen(line[i], out var a) || !Overlay.ToScreen(line[i + 1], out var b)) continue;
                var ab = b - a;
                var s = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(mouse - a, ab) / ab.sqrMagnitude) : 0f;
                var on = a + ab * s;
                var d = Vector2.Distance(on, mouse);
                if (d < best) {
                    best = d;
                    point = Vector3.Lerp(line[i], line[i + 1], s);
                    screen = on;
                    found = true;
                }
            }
            return found;
        }

        // Where along the route a new point goes: after the last node that comes before it along the line.
        private static int InsertIndex(PneumaticTube tube, Vector3 point) {
            float Along(Vector3 p) {
                var line = tube.Line;
                float s = 0f, bestS = 0f, best = float.MaxValue;
                for (var i = 0; i + 1 < line.Count; i++) {
                    var ab = line[i + 1] - line[i];
                    var t = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - line[i], ab) / ab.sqrMagnitude) : 0f;
                    var d = (line[i] + ab * t - p).sqrMagnitude;
                    if (d < best) {
                        best = d;
                        bestS = s + ab.magnitude * t;
                    }
                    s += ab.magnitude;
                }
                return bestS;
            }
            var at = Along(point);
            for (var i = 1; i < tube.nodes.Count; i++)
                if (Along(tube.World(tube.nodes[i]).p) > at) return i;
            return tube.nodes.Count;
        }
    }
}
