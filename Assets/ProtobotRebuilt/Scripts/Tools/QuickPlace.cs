using System.Collections.Generic;
using System.Linq;
using Protobot.InputEvents;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools {
    // V2: building with the keyboard, in any tool with nothing being placed:
    // - C over a hole: a screw through it and every hole touching it behind (hole against hole, a whole stack), with a keps nut on the
    //   end. Shift+C: through every hole lined up behind, gaps and all. The screw is the shortest that goes through
    //   everything and the whole nut (its tip at least at the nut's far end); a threaded hole on the way (a motor's, a
    //   standoff's, a nut already there) takes it instead of a new nut. One undo step; refusals say why.
    // - Hold F over a hole: a screw shows in it, as if brought from the parts menu; the wheel changes its length; let go
    //   of F to place it. Hold F over a screw: a nut shows where the mouse is on the screw and slides along it (snapping
    //   against the parts beside it, Ctrl: not); the wheel changes the kind of nut; let go to place it.
    // - Hold E over a screw or a hole: the same with a 5/16" OD nylon spacer (the wheel changes its length).
    // Moving off the hole or screw while holding takes the part away (coming back brings it again); the sizes last
    // while the app runs, so the next F or E starts with the one used last. Over a hole or screw C and F are these, not
    // the color picker or focus.
    public class QuickPlace : MonoBehaviour {
        private const float KepsLength = 0.141f;     // NUT-Keps along its axis
        private const float ThreadReach = 0.375f;     // how far a screw goes into a threaded hole (a motor's, a standoff's)
        private const float MaxSpan = 3f;
        private const float Touching = 0.03f;          // holes this close count as against each other

        private static readonly (string id, float length)[] FastenScrews = {
            ("SCRW-1/4in", 0.25f), ("SCRW-3/8in", 0.375f), ("SCRW-1/2in", 0.5f), ("SCRW-5/8in", 0.625f), ("SCRW-3/4in", 0.75f),
            ("SCRW-7/8in", 0.875f), ("SCRW-1.00in", 1f), ("SCRW-1.25in", 1.25f), ("SCRW-1.50in", 1.5f), ("SCRW-1.75in", 1.75f),
            ("SCRW-2.00in", 2f), ("SCRW-2.25in", 2.25f), ("SCRW-2.50in", 2.5f),
        };

        /// <summary>For tests run from scripts: where the mouse is (screen pixels) instead of the real one.</summary>
        public static Vector2? TestMouse;

        /// <summary>The wheel is changing a quick part's size (the camera doesn't zoom).</summary>
        public static bool ClaimsWheel => instance != null && instance.mode != Mode.None;

        private enum Mode { None, Screw, Nut, Spacer }

        private static QuickPlace instance;
        private Mode mode;
        private Key heldKey;
        private bool showing;
        // The size picked last for each (kept while the app runs).
        private static int screwIndex = 2, nutIndex = -1, spacerIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create() {
            var go = new GameObject("Quick Place");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<QuickPlace>();
            // Over a hole or a screw, C and F are ours (not the color picker or focus).
            // The app's own shortcut on the same key (C: color picker, F: focus) waits while the mouse is over a hole
            // or screw (whatever the keys are changed to in Edit > Keyboard shortcuts).
            InputEvent.Claimed = e => {
                // V2 (performance): asked by every shortcut every frame: off while a game or the simulation runs
                // (quick placing is too), and each key's answer worked out once a frame (it was 1.7 ms a frame).
                if (Game.SkillsGame.Active || Motion.Sandbox.Running) return false;
                var key = e.ShortcutKey;
                if (key == Key.None) return false;
                if (claimFrame != Time.frameCount) {
                    claimFrame = Time.frameCount;
                    claims.Clear();
                }
                if (claims.TryGetValue((key, e.name == "Focus"), out var known)) return known;
                var claimed = ClaimsKey(e, key);
                claims[(key, e.name == "Focus")] = claimed;
                return claimed;
            };
        }

        private static int claimFrame = -1;
        private static readonly Dictionary<(Key, bool), bool> claims = new();

        private static bool ClaimsKey(InputEvent e, Key key) {
            {
                if (key == Keybinds.MainKey("QuickScrew") || key == Keybinds.MainKey("QuickScrewAll")) { if (HoverHole() != null) return true; }
                if (key == Keybinds.MainKey("HoldScrew")) { if (ClaimsWheel || HoverHole() != null || HoverScrew() != null) return true; }
                if (key == Keybinds.MainKey("HoldSpacer")) { if (ClaimsWheel || HoverHole() != null || HoverScrew() != null) return true; }
                if (key == Keybinds.MainKey("PlaceTubing")) { if (HoverPort() != null || HoverWirePort() != null) return true; }
                return e.name == "Focus" && ClaimsWheel;
            }
        }

        private static bool Blocked(Keyboard keyboard) =>
            TextInput.IsTyping || TextInput.ShortcutsPaused || keyboard.altKey.isPressed || Motion.Sandbox.Running || Game.SkillsGame.Active
            || Camera.main == null || Placement.Instance == null;

        private void Update() {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (mode != Mode.None) {
                Holding(keyboard);
                return;
            }
            if (!Blocked(keyboard) && !Placement.IsPlacing && Keybinds.Pressed("Mirror")) {
                MirrorTool.Open();
                return;
            }
            if (Blocked(keyboard) || Placement.IsPlacing || MouseInput.overUI) return;
            // V2: over a free push-in port of a pneumatic fitting: "E  Place tubing" by the mouse, E starts it there.
            var port = HoverPort();
            if (port != null) {
                Interface.KeyHint.Show(Keybinds.Label("PlaceTubing"), "Place tubing");
                Ring(Motion.PneumaticTube.PortFrame(port).point);
                if (Keybinds.Pressed("PlaceTubing")) {
                    Motion.TubeTool.Begin(port);
                    return;
                }
            }
            // V2: over a free port of the brain, a motor or a sensor: "E  Place wiring", E runs a cable from it.
            var wirePort = port == null ? HoverWirePort() : null;
            if (wirePort != null) {
                Interface.KeyHint.Show(Keybinds.Label("PlaceTubing"), "Place wiring" + (wirePort.brain ? " (" + wirePort.label.Replace("Port ", "port ") + ")" : ""));
                Ring(wirePort.Position);   // which socket it means (the brain's are 0.35" apart)
                if (Keybinds.Pressed("PlaceTubing")) {
                    Motion.TubeTool.BeginWire(wirePort.kind, wirePort);
                    return;
                }
                port = null;
            }
            if (Keybinds.Pressed("QuickScrewAll") && HoverHole() != null) Fasten(true);
            else if (Keybinds.Pressed("QuickScrew") && HoverHole() != null) Fasten(false);
            else if (Keybinds.Pressed("HoldScrew")) {
                if (HoverScrew() != null) Begin(Mode.Nut, Keybinds.MainKey("HoldScrew"));
                else if (HoverHole() != null) Begin(Mode.Screw, Keybinds.MainKey("HoldScrew"));
            }
            else if (Keybinds.Pressed("HoldSpacer") && Motion.MotorControls.Controlled.Count == 0
                     && ((port == null && wirePort == null) || Keybinds.MainKey("PlaceTubing") != Keybinds.MainKey("HoldSpacer"))
                     && (HoverScrew() != null || HoverHole() != null))
                Begin(Mode.Spacer, Keybinds.MainKey("HoldSpacer"));
        }

        // ------------------------------------------------------------------ holding F or E

        private (PartType type, PartGenerator generator, List<string> options, int index) Choice() {
            switch (mode) {
                case Mode.Screw: return Options("SCRW", null, ref screwIndex, 2);
                case Mode.Nut: return Options("NUT", null, ref nutIndex, -1);
                default: return Options("VXSPACERS", o => o.Contains("5/16\" OD") && o.Length > 0 && char.IsDigit(o[0]), ref spacerIndex, 1);
            }
        }

        private static (PartType, PartGenerator, List<string>, int) Options(string typeId, System.Func<string, bool> filter, ref int index, int first) {
            var type = PartsManager.GetPartType(typeId);
            var generator = type != null ? type.GetComponent<PartGenerator>() : null;
            if (generator == null) return (null, null, new List<string>(), 0);
            var options = generator.GetParam1Options().Where(o => filter == null || filter(o)).ToList();
            if (typeId == "VXSPACERS")
                options = options.OrderBy(o => Inch.TryParse(o.Split(' ')[0], out var v) ? v : 99f).ToList();
            if (index < 0) index = typeId == "NUT" ? Mathf.Max(0, options.IndexOf("Keps")) : first;
            index = Mathf.Clamp(index, 0, Mathf.Max(0, options.Count - 1));
            return (type, generator, options, index);
        }

        private void Begin(Mode newMode, Key key) {
            mode = newMode;
            heldKey = key;
            showing = false;
            Show();
        }

        private string ChoiceName() {
            var (_, _, options, index) = Choice();
            if (options.Count == 0) return "";
            var option = options[index];
            return mode switch {
                Mode.Screw => PartsCatalog.Pretty(option) + " screw",
                Mode.Nut => option + " nut",
                _ => option.Replace(" (5/16\" OD)", "").Replace("Nylon Spacer", "nylon spacer"),
            };
        }

        // The part follows the mouse as if it came from the parts menu.
        private void Show() {
            var (type, generator, options, index) = Choice();
            if (generator == null || options.Count == 0) {
                Interface.Interface.Say("That part isn't installed", false);
                mode = Mode.None;
                return;
            }
            generator.param1.value = options[index];
            if (generator.UsesTwoParams) {
                var second = generator.GetParam2Options();
                if (second.Count > 0 && !second.Contains(generator.param2.value)) generator.param2.value = second[0];
            }
            var placement = Placement.Instance;
            placement.StartPlacing(new PartPlacementData(generator, type, placement.transform));
            showing = true;
        }

        private void Hide() {
            if (showing && Placement.IsPlacing) Placement.Instance.StopPlacing();
            showing = false;
        }

        private bool OnTarget() => mode switch {
            Mode.Screw => HoverHole() != null,
            Mode.Nut => HoverScrew() != null,
            _ => HoverScrew() != null || HoverHole() != null,
        };

        private void Holding(Keyboard keyboard) {
            var released = !keyboard[heldKey].isPressed || Blocked(keyboard);
            var onTarget = OnTarget();
            if (released) {
                if (showing && onTarget && Placement.IsPlacing) {
                    var name = ChoiceName();
                    Placement.Instance.PlaceNow();
                    Hide();
                    Interface.Interface.Say("Placed a " + name, true);
                }
                else {
                    Hide();
                    Interface.Interface.Say("Nothing placed: let go over a " + (mode == Mode.Screw ? "hole" : mode == Mode.Nut ? "screw" : "screw or hole"), false);
                }
                mode = Mode.None;
                return;
            }
            // Off the hole or screw: the part goes away (back when the mouse is).
            if (!onTarget && showing) Hide();
            else if (onTarget && !showing) Show();
            // The wheel: the next size or kind.
            var wheel = MouseInput.scrollAxis;
            if (Mathf.Abs(wheel) > 0.01f) {
                var (_, _, options, index) = Choice();
                var next = Mathf.Clamp(index + (wheel > 0f ? 1 : -1), 0, Mathf.Max(0, options.Count - 1));
                if (next != index) {
                    if (mode == Mode.Screw) screwIndex = next;
                    else if (mode == Mode.Nut) nutIndex = next;
                    else spacerIndex = next;
                    if (showing) Show();
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                    Interface.Interface.Say(ChoiceName(), true);
                }
            }
        }

        // ------------------------------------------------------------------ what's under the mouse

        private static int hoverFrame = -1;
        private static HoleCollider hoverHole;
        private static Vector3 hoverFace, hoverInto;
        private static Screw hoverScrew;
        private static GameObject hoverSolid;   // V2: the part the mouse is on, and where
        private static Vector3 hoverPoint;

        private static Ray MouseRay() => Camera.main.ScreenPointToRay(TestMouse ?? (Vector2)MouseInput.Position);

        private static void Look() {
            if (hoverFrame == Time.frameCount && TestMouse == null) return;
            hoverFrame = Time.frameCount;
            hoverHole = null;
            hoverScrew = null;
            hoverSolid = null;
            if (Camera.main == null || MouseInput.overUI && TestMouse == null) return;
            var ray = MouseRay();
            var hits = Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance).ToList();
            var solid = float.MaxValue;
            foreach (var h in hits) {
                if (h.collider.isTrigger || h.collider.gameObject.layer == Placement.PLACEMENT_LAYER) continue;
                var screw = h.collider.GetComponentInParent<Screw>();
                if (screw != null && !screw.gameObject.IsDeleted()) {
                    hoverScrew = screw;
                    hoverSolid = screw.gameObject;
                    hoverPoint = h.point;
                    solid = h.distance;
                    break;
                }
                if (h.collider.gameObject.layer == 0 && h.collider.GetComponentInParent<SavedObject>() != null) {
                    hoverSolid = h.collider.GetComponentInParent<SavedObject>().gameObject;
                    hoverPoint = h.point;
                    solid = h.distance;
                    break;
                }
            }
            if (hoverScrew != null) return;
            foreach (var h in hits) {
                if (h.distance > solid + 0.2f) break;
                if (!h.collider.TryGetComponent(out HoleCollider found) || found.holeData == null || found.holeData.part == null) continue;
                if (found.holeData.part.layer == Placement.PLACEMENT_LAYER || found.holeData.part.IsDeleted()) continue;
                hoverHole = found;
                var axis = found.transform.forward;
                var outward = Vector3.Dot(axis, -ray.direction) >= 0f ? axis : -axis;
                hoverFace = found.transform.position + outward * found.transform.lossyScale.z / 2f;
                hoverInto = -outward;
                break;
            }
        }

        private static HoleCollider HoverHole() {
            Look();
            return hoverHole;
        }

        /// <summary>V2: the free push-in port of a pneumatic fitting under the mouse (none while a tool that picks
        /// things is open).</summary>
        private static HoleCollider HoverPort() {
            if (Motion.TubeTool.Active || Motion.ElasticTool.Active || Motion.ChainTool.Active || ColorPickerOpen() || MotorKeys()) return null;
            var hole = HoverHole();
            if (hole != null) return Motion.PneumaticTube.IsPort(hole) && !Motion.PneumaticTube.PortTaken(hole) ? hole : null;
            // On a fitting (a screw here) or a valve/tee's body: its free port nearest the mouse, close to it.
            if (hoverSolid == null || hoverSolid.IsDeleted()) return null;
            if (!(hoverSolid.GetComponent<SavedObject>()?.id ?? "").StartsWith("VXPNEUMATICS") && !ChildPartGenerator.IsFittingPart(hoverSolid)) return null;
            var near = Motion.TubeTool.PortNear(hoverSolid, hoverPoint);
            return near != null && (near.transform.position - hoverPoint).magnitude < 0.3f ? near : null;
        }

        private static bool ColorPickerOpen() => ToolRail.Current == "Paint";

        // V2: a motor being controlled has keys of its own (E runs one forward by default): no port hints then.
        private static bool MotorKeys() => Motion.MotorControls.Controlled.Count > 0;

        // V2: a ring on the port a key hint is about.
        private static void Ring(Vector3 point) {
            Interface.Overlay.Begin();
            if (Interface.Overlay.ToScreen(point, out var at)) Interface.Overlay.Ring(at, 10f, Interface.Theme.Accent, 2.5f);
        }

        /// <summary>V2: the free port of the brain, a motor or a sensor under the mouse (not over an empty hole:
        /// that's for a spacer).</summary>
        private static Motion.WirePort HoverWirePort() {
            if (Motion.TubeTool.Active || Motion.ElasticTool.Active || Motion.ChainTool.Active || ColorPickerOpen() || MotorKeys() || Camera.main == null) return null;
            var hole = HoverHole();
            if (hole != null && hole.IsEmpty && !Motion.WirePorts.IsBrain(hole.transform.parent != null ? hole.transform.parent.gameObject : null)) return null;
            var ray = MouseRay();
            foreach (var kind in new[] { Motion.WireKind.Smart, Motion.WireKind.ThreeWire }) {
                var port = Motion.WirePorts.Under(ray, kind);
                if (port == null) continue;
                return port;
            }
            return null;
        }

        private static Screw HoverScrew() {
            Look();
            return hoverScrew;
        }

        // ------------------------------------------------------------------ C: screw and nut

        // A hole on the line: where it starts and ends (distance from the face, into the parts).
        private struct Stop {
            public HoleCollider hole;
            public GameObject part;
            public float from, to;
            public bool threaded, taken;
        }

        public static void Fasten(bool all) {
            var hole = HoverHole();
            if (hole == null) {
                Interface.Interface.Say("Point at a hole first: C screws it to the hole touching it, Shift+C through every hole in line", false);
                return;
            }
            var face = hoverFace;
            var into = hoverInto;
            // (A screw just put there is only found by its hole a physics step later: look for it too.)
            if (hole.IsOccupied || FindObjectsByType<Screw>(FindObjectsSortMode.None).Any(s => !s.gameObject.IsDeleted()
                    && Vector3.ProjectOnPlane(s.transform.position - face, into).magnitude < 0.05f && Mathf.Abs(Vector3.Dot(s.transform.position - face, into)) < 0.6f)) {
                Interface.Interface.Say("That hole already has a screw in it", false);
                return;
            }
            FastenAt(face, into, all, hole);
        }

        /// <summary>V2: C's screw and nut along a line (from the face of the first hole, going into the parts); for
        /// builders and tests. False (with a message) when it couldn't.</summary>
        public static bool FastenAt(Vector3 face, Vector3 into, bool all = false, HoleCollider hole = null) {
            Physics.SyncTransforms();
            var stops = Along(face, into);
            if (stops.Count == 0 || (hole != null && stops[0].hole != hole)) {
                Interface.Interface.Say("Couldn't follow the hole into its part", false);
                return false;
            }
            var through = new List<Stop> { stops[0] };
            Stop? threadedEnd = null;
            for (var i = 1; i < stops.Count; i++) {
                var stop = stops[i];
                if (stop.taken) break;   // another screw's there already
                var gap = stop.from - through[through.Count - 1].to;
                // C: every hole touching the one before it (a bracket, a spacer, a flange stacked up), up to the first gap.
                if (!all && gap > Touching) {
                    if (through.Count == 1) {
                        Interface.Interface.Say("The next hole isn't touching this one (" + Inch.Format(gap) + " gap): Shift+C screws through everything in line", false);
                        return false;
                    }
                    if (!stop.threaded && gap < KepsLength) {
                        Interface.Interface.Say("No room for a nut after the last touching hole (" + Inch.Format(gap) + " to the next part): Shift+C screws through everything in line", false);
                        return false;
                    }
                    break;
                }
                if (stop.threaded) {
                    threadedEnd = stop;
                    break;
                }
                through.Add(stop);
            }
            if (through.Count < 2 && threadedEnd == null) {
                Interface.Interface.Say("No other hole lined up behind this one to screw it to", false);
                return false;
            }
            var grip = through[through.Count - 1].to;
            var needed = threadedEnd.HasValue
                ? threadedEnd.Value.from + Mathf.Min(ThreadReach, threadedEnd.Value.to - threadedEnd.Value.from)
                : grip + KepsLength;
            var screw = FastenScrews.FirstOrDefault(s => s.length >= needed - 0.005f);
            if (screw.id == null) {
                Interface.Interface.Say("Too far for the longest screw (2 1/2\"): " + Inch.Format(needed) + " of screw needed", false);
                return false;
            }
            if (PartsManager.GetPartType(screw.id) == null) return false;

            var made = new List<GameObject> { PartsManager.GeneratePart(screw.id, face, Quaternion.LookRotation(-into)) };
            if (!threadedEnd.HasValue)
                made.Add(PartsManager.GeneratePart("NUT-Keps", face + into * grip, Quaternion.LookRotation(-into)));
            foreach (var part in made) StateSystem.AddElement(new ObjectElement(part) { existing = false });
            StateSystem.AddState(new State(made.Select(p => (IElement)new ObjectElement(p)).ToList()));
            FindAnyObjectByType<global::PartListOutput>()?.CalculatePartsList();
            Audio.Sounds.Play(Audio.Sounds.Tick);

            var parts = through.Select(s => s.part).Concat(threadedEnd.HasValue ? new[] { threadedEnd.Value.part } : new GameObject[0]).Distinct().Select(PartLabels.Short).ToList();
            var holes = through.Count + (threadedEnd.HasValue ? 1 : 0);
            var what = parts.Count == 2 ? parts[0] + " to " + parts[1] : parts.Count == 1 ? parts[0] + " (" + holes + " holes)" : parts.Count + " parts";
            Interface.Interface.Say("Screwed " + what + ": " + Inch.Format(screw.length) + " screw" + (threadedEnd.HasValue ? " into its thread" : " and keps nut"), true);
            return true;
        }

        // Every hole on the line, in order, measured from the face.
        private static List<Stop> Along(Vector3 face, Vector3 into) {
            var stops = new List<Stop>();
            var seen = new HashSet<HoleCollider>();
            foreach (var h in Physics.RaycastAll(new Ray(face - into * 0.05f, into), MaxSpan, 1 << HoleCollider.HOLE_COLLISIONS_LAYER, QueryTriggerInteraction.Collide)) {
                if (!h.collider.TryGetComponent(out HoleCollider hole) || hole.holeData == null || hole.holeData.part == null || !seen.Add(hole)) continue;
                if (hole.holeData.part.IsDeleted() || hole.holeData.part.layer == Placement.PLACEMENT_LAYER) continue;
                var t = hole.transform;
                if (Mathf.Abs(Vector3.Dot(t.forward, into)) < 0.98f) continue;
                var offset = t.position - face;
                if (Vector3.ProjectOnPlane(offset, into).magnitude > 0.06f) continue;
                var along = Vector3.Dot(offset, into);
                var half = t.lossyScale.z / 2f;
                stops.Add(new Stop {
                    hole = hole, part = hole.holeData.part, from = along - half, to = along + half,
                    threaded = hole.holeType == HoleCollider.HoleType.Threaded, taken = hole.IsOccupied,
                });
            }
            return stops.Where(s => s.to > -0.01f).OrderBy(s => s.from).ToList();
        }
    }
}
