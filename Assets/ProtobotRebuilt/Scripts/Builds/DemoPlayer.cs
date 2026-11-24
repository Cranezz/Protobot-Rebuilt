using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Protobot.Builds {
    /// <summary>
    /// V2 (tutorial videos, developer use): plays a demo script on the real interface with a virtual mouse and keyboard
    /// (Input System devices of its own; the PC's own mouse and keyboard are switched off for the game while it runs, so
    /// nothing the developer does reaches it), drawing a cursor, click ripples and the keys pressed, and records it with
    /// <see cref="FrameRecorder"/> at a steady 30 fps. Does nothing unless started: DemoPlayer.Run(script, folder).
    ///
    /// Script lines (one command each, # comments; coordinates in a 1920 x 1080 screen; times in seconds):
    ///   move TARGET [secs]          glide the cursor (an eased, slightly curved path; secs from the distance if left out)
    ///   click [TARGET] | rclick | dclick      press and release (moving there first)
    ///   down left|right|middle / up left|right|middle
    ///   drag left|right|middle TARGET [secs]  hold, move, release
    ///   scroll NOTCHES [secs]       the wheel (+ up / zoom in)
    ///   key KEYS                    "C", "Ctrl+Z", "Delete" (Key enum names; Ctrl/Shift/Alt work as modifiers)
    ///   hold KEY / release KEY      keep a key down (Ctrl while scrolling)
    ///   type TEXT                   types into the focused text box, a character at a time
    ///   wait secs | line N | waitline       a narration line starts here (its frame is written to marks.txt) /
    ///                               wait until the current line's audio (durations.txt beside the script) has ended
    ///   caption TEXT / caption      a caption at the bottom (none: hide it)
    ///   do NAME ARGS                set-up actions: newbuild, camera FX FY FZ YAW PITCH DIST, home, hidetarget,
    ///                               showtarget, align TAG
    ///   reveal TAGS [secs]          a time-lapse: the hidden target build's parts with these tags appear one by one
    ///   frame [secs] [fill] [dx,dy] [all]   the camera on every part (all: the whole target build, hidden or not)
    /// TARGET: "x,y" (screen), "ui:Name/Child" (an active interface object whose path ends so: its middle),
    /// "text:Label" (an active text exactly so: its button's middle), "world:x,y,z" (a point in the 3D view),
    /// "part:ID" (the middle of the first part whose id starts so), optionally "+dx,dy" after it.
    /// </summary>
    public class DemoPlayer : MonoBehaviour {
        public static DemoPlayer Current { get; private set; }
        public static string Status { get; private set; } = "idle";

        /// <summary>What "probe" writes (tests may set their own); by default what placing snaps to under the mouse.</summary>
        public static Func<string> Probe = PlacementProbe;

        public static string PlacementProbe() {
            var p = Placement.Instance;
            if (p == null) return "no placement";
            const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("clipboard " + Tools.PartClipboard.Count + " parts");
            sb.AppendLine("placing " + p.placing + " id " + p.PlacingId + " rot " + p.transform.eulerAngles + " pos " + p.transform.position + " mouse " + MouseInput.Position);
            var data = typeof(Placement).GetField("currentPlacementData", Flags)?.GetValue(p) as PlacementData;
            if (typeof(Placement).GetField("placePositions", Flags)?.GetValue(p) is System.Collections.IList list)
                foreach (PlaceDisplacement d in list) {
                    var ok = false;
                    var why = "";
                    try {
                        ok = d.TryGetDisplacement(data, out var disp);
                        if (ok) why = " -> " + disp.translation.Position + " rot " + disp.rotation.Orientation.eulerAngles;
                    }
                    catch (Exception e) { why = " EXC " + e.Message; }
                    sb.AppendLine("  " + d.GetType().Name + " " + ok + why);
                    if (ok) break;
                }
            var cam = Camera.main;
            if (cam != null && Physics.Raycast(cam.ScreenPointToRay(MouseInput.Position), out var hit, 1000f))
            {
                sb.AppendLine("  ray (" + cam.name + ") dist " + hit.distance.ToString("0.00") + " " + hit.collider.GetType().Name);
                sb.AppendLine("  ray hits " + hit.collider.name + " of " + (hit.collider.GetComponentInParent<SavedObject>()?.id ?? "-") + " layer " + LayerMask.LayerToName(hit.collider.gameObject.layer));
                var screw = hit.collider.GetComponentInParent<Screw>();
                if (screw != null) {
                    var head = screw.transform.position;
                    var down = -screw.transform.forward;
                    var used = AxleLine.Used(null, head, down, screw.ScrewLength);
                    sb.AppendLine("  screw length " + screw.ScrewLength + " mouse along " + Vector3.Dot(hit.point - head, down).ToString("0.000") + " used " + string.Join(" ", used.Select(u => u.from.ToString("0.000") + ".." + u.to.ToString("0.000"))));
                    foreach (var h in Physics.RaycastAll(new Ray(head - down * 3f, down), screw.ScrewLength + 3.1f, ~0, QueryTriggerInteraction.Collide))
                        if (h.collider.CompareTag("HoleCollider") && h.transform.parent != null)
                            sb.AppendLine("    hole of " + (h.transform.parent.GetComponent<SavedObject>()?.id ?? h.transform.parent.name) + " at " + Vector3.Dot(h.transform.position - head, down).ToString("0.000") + " depth " + h.transform.lossyScale.z.ToString("0.000"));
                }
            }
            foreach (var axleSnap in p.GetComponents<AxlePlaceDisplacement>())
                if (typeof(AxlePlaceDisplacement).GetField("mouseCast", Flags)?.GetValue(axleSnap) is MouseCast cast)
                    {
                    var c = cast.hit;
                    sb.AppendLine("  axle snap's mouse cast on " + cast.name + " mask " + cast.layerMask + ": " + (c.collider != null ? c.collider.name + " " + c.collider.GetType().Name + " tag " + c.collider.tag + " layer " + LayerMask.LayerToName(c.collider.gameObject.layer) + " dist " + c.distance.ToString("0.00") + " point " + c.point : "none") + " ray " + cast.ray);
                    var mask = cast.layerMask;
                    cast.layerMask = mask | (1 << 30);   // a fresh cast (the hit is kept per frame, mouse and mask)
                    var fresh = cast.hit;
                    cast.layerMask = mask;
                    sb.AppendLine("  fresh cast: " + (fresh.collider != null ? fresh.collider.name + " dist " + fresh.distance.ToString("0.00") : "none"));
                    foreach (var h in Physics.RaycastAll(cast.ray, 100f, mask).OrderBy(h => h.distance).Take(6))
                        sb.AppendLine("    all: " + h.collider.name + " " + h.collider.tag + " " + h.distance.ToString("0.00") + " root layer " + LayerMask.LayerToName(h.collider.transform.root.gameObject.layer));
                }
            if (data != null && data.TryGetPartData(out var pd) && pd != null && AxlePlaceDisplacement.AxleOf(pd.gameObject, true, out var axle))
                sb.AppendLine("  axle low " + axle.low + " high " + axle.high);
            return sb.ToString();
        }

        private Mouse mouse;
        private Keyboard keyboard;
        private readonly List<InputDevice> switchedOff = new();
        private AutoSave autoSave;
        private InputSettings.BackgroundBehavior oldBackground;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode oldEditorBehavior;
#endif
        private Vector2 position, lastSent;
        private bool left, right, middle;
        private float scrollThisFrame;
        private readonly HashSet<Key> held = new();
        private readonly Queue<char> typing = new();

        private string[] lines;
        private string folder, scriptFolder;
        private readonly Dictionary<int, float> durations = new();
        private readonly List<string> marks = new();
        private float lineEnds;

        // the drawn cursor, ripples and key chips
        private Canvas canvas;
        private RectTransform cursor, keysChip, captionBox;
        private TextMeshProUGUI keysText, captionText;
        private float keysShownUntil;
        private readonly List<(RectTransform ring, float start)> ripples = new();

        public static void Run(string script, string recordFolder, float maxSeconds = 600f) {
            if (Current != null) Destroy(Current.gameObject);
            var go = new GameObject("Demo Player") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            Current = go.AddComponent<DemoPlayer>();
            Current.lines = File.ReadAllLines(script);
            Current.scriptFolder = Path.GetDirectoryName(script);
            Current.folder = recordFolder;
            Current.ReadDurations();
            Current.Begin();
            if (!string.IsNullOrEmpty(recordFolder)) FrameRecorder.Record(recordFolder, maxSeconds);
            Current.StartCoroutine(Current.Play());
        }

        public static void Stop() {
            if (Current != null) Destroy(Current.gameObject);
        }

        private void ReadDurations() {
            var file = Path.Combine(scriptFolder, "durations.txt");
            if (!File.Exists(file)) return;
            foreach (var row in File.ReadAllLines(file)) {
                var parts = row.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0], out var n)) durations[n] = F(parts[1]);
            }
        }

        // ------------------------------------------------------------------ devices

        private void Begin() {
            oldBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            oldEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            // Taken out, not just switched off: the app's InputEvents want every control bound to an action pressed
            // (AllControlsPressed), and a switched-off mouse still counts as one of them.
            foreach (var d in InputSystem.devices.ToList())
                if (d is Pointer || d is Keyboard) {
                    InputSystem.RemoveDevice(d);
                    switchedOff.Add(d);
                }
            mouse = InputSystem.AddDevice<Mouse>("Demo Mouse");
            keyboard = InputSystem.AddDevice<Keyboard>("Demo Keyboard");
            mouse.MakeCurrent();
            keyboard.MakeCurrent();
            position = lastSent = new Vector2(Screen.width * 0.5f, Screen.height * 0.45f);
            MakeOverlay();
            // no "save this build?" notice in the middle of a video
            autoSave = FindAnyObjectByType<AutoSave>();
            if (autoSave != null) autoSave.enabled = false;
            Status = "running";
        }

        private void OnDestroy() {
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            foreach (var d in switchedOff)
                if (d != null && !d.added) {
                    try { InputSystem.AddDevice(d); }
                    catch (Exception e) { Debug.LogWarning("[Demo] couldn't put back " + d.name + ": " + e.Message); }
                }
            InputSystem.settings.backgroundBehavior = oldBackground;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorBehavior;
#endif
            if (canvas != null) Destroy(canvas.gameObject);
            if (autoSave != null) autoSave.enabled = true;
            if (Current == this) Current = null;
        }

        private void LateUpdate() {
            if (mouse == null) return;
            var state = new MouseState { position = position, delta = position - lastSent, scroll = new Vector2(0f, scrollThisFrame) }
                .WithButton(MouseButton.Left, left).WithButton(MouseButton.Right, right).WithButton(MouseButton.Middle, middle);
            InputSystem.QueueStateEvent(mouse, state);
            lastSent = position;
            scrollThisFrame = 0f;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(held.ToArray()));
            while (typing.Count > 0) InputSystem.QueueTextEvent(keyboard, typing.Dequeue());
            DrawOverlay();
            if (Time.frameCount % 5 == 0) TrackParts();
            WatchFraming();
        }

        // ------------------------------------------------------------------ the script

        private IEnumerator Play() {
            yield return null;
            for (var i = 0; i < lines.Length; i++) {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                Status = "line " + (i + 1) + ": " + line;
                var sp = line.IndexOf(' ');
                var cmd = sp < 0 ? line : line.Substring(0, sp);
                var rest = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                IEnumerator step = null;
                try { step = Command(cmd, rest); }
                catch (Exception e) {
                    Status = "FAILED line " + (i + 1) + " (" + line + "): " + e.Message;
                    Debug.LogWarning("[Demo] " + Status);
                    Finish();
                    yield break;
                }
                if (step != null) {
                    while (true) {
                        bool more;
                        try { more = step.MoveNext(); }
                        catch (Exception e) {
                            Status = "FAILED line " + (i + 1) + " (" + line + "): " + e.Message;
                            Debug.LogWarning("[Demo] " + Status);
                            Finish();
                            yield break;
                        }
                        if (!more) break;
                        yield return step.Current;
                    }
                }
            }
            for (var k = 0; k < 15; k++) yield return null;   // half a second after the last action
            Status = "done";
            Finish();
        }

        private void Finish() {
            if (!string.IsNullOrEmpty(folder)) {
                Directory.CreateDirectory(folder);
                File.WriteAllLines(Path.Combine(folder, "marks.txt"), marks);
                File.WriteAllLines(Path.Combine(folder, "offscreen.txt"), offscreen);
            }
            if (FrameRecorder.Recording) FrameRecorder.Stop();
            var done = Status;
            Destroy(gameObject);
            Status = done;
        }

        private float Now => Time.time;
        private int FrameNow => FrameRecorder.Recording ? FrameRecorder.Frame : Time.frameCount;

        private IEnumerator Command(string cmd, string rest) {
            switch (cmd) {
                case "wait": return Wait(F(rest));
                case "move": {
                    var (target, secs) = TargetAndSecs(rest);
                    return Glide(target, secs);
                }
                case "click": return Click(rest, MouseButton.Left, 1);
                case "dclick": return Click(rest, MouseButton.Left, 2);
                case "rclick": return Click(rest, MouseButton.Right, 1);
                case "down": SetButton(Button(rest), true); Ripple(); return null;
                case "up": SetButton(Button(rest), false); return null;
                case "drag": {
                    var sp = rest.IndexOf(' ');
                    var button = Button(rest.Substring(0, sp));
                    var (target, secs) = TargetAndSecs(rest.Substring(sp + 1));
                    return Drag(button, target, secs);
                }
                case "scroll": {
                    var p = rest.Split(' ');
                    return Scroll(F(p[0]), p.Length > 1 ? F(p[1]) : 0.4f);
                }
                case "key": return Keys(rest);
                case "hold": foreach (var k in ParseKeys(rest)) held.Add(k); ShowKeys(); return null;
                case "release": foreach (var k in ParseKeys(rest)) held.Remove(k); return null;
                case "type": return Type(rest);
                case "submit": return Submit();
                case "line": {
                    var n = int.Parse(rest);
                    marks.Add(n + " " + FrameNow);
                    // the line's audio plus a breath before the next one
                    lineEnds = Now + (durations.TryGetValue(n, out var d) ? d : 3f) + 0.45f;
                    return null;
                }
                case "waitline": return WaitUntil(() => Now >= lineEnds);
                case "waitfor": return WaitUntil(() => { try { Point(rest); return true; } catch { return false; } });
                case "caption": Caption(rest); return null;
                case "frame": return Frame(rest);
                case "reveal": return Reveal(rest);
                case "lift": return Lift(rest.Length > 0 ? F(rest) : 0.6f);
                case "do": Do(rest); return Wait(0.1f);
                // probe: runs Probe (set by a test script) in the game's frame, where the demo's mouse is (a script run
                // from the editor reads the editor's mouse), and writes its answer to Temp/probe.txt.
                case "probe":
                    File.WriteAllText("Temp/probe.txt", Probe != null ? Probe() : "no probe set");
                    return null;
                default: throw new Exception("unknown command " + cmd);
            }
        }

        private IEnumerator Wait(float secs) {
            var end = Now + secs;
            while (Now < end) yield return null;
        }

        private IEnumerator WaitUntil(Func<bool> done) {
            var limit = Now + 120f;
            while (!done() && Now < limit) yield return null;
        }

        private (string target, float secs) TargetAndSecs(string rest) {
            // the last word is a time when it's a number and there's something before it
            var sp = rest.LastIndexOf(' ');
            if (sp > 0 && float.TryParse(rest.Substring(sp + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                return (rest.Substring(0, sp), s);
            return (rest, -1f);
        }

        private IEnumerator Glide(string target, float secs) {
            var to = Resolve(target);
            var from = position;
            var distance = Vector2.Distance(from, to) / Scale;
            if (secs < 0f) secs = Mathf.Clamp(0.22f + distance / 1500f, 0.25f, 1.1f);
            // a slight bow to the side, as a hand moves
            var side = Vector2.Perpendicular((to - from).normalized) * Mathf.Min(40f * Scale, distance * Scale * 0.08f);
            var start = Now;
            while (true) {
                var t = Mathf.Clamp01((Now - start) / Mathf.Max(secs, 0.01f));
                var e = t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
                position = Vector2.Lerp(from, to, e) + side * Mathf.Sin(Mathf.PI * e);
                if (t >= 1f) break;
                yield return null;
            }
            position = to;
            yield return null;
        }

        private IEnumerator Click(string target, MouseButton button, int count) {
            if (!string.IsNullOrEmpty(target)) {
                var (tgt, secs) = TargetAndSecs(target);
                var glide = Glide(tgt, secs);
                while (glide.MoveNext()) yield return glide.Current;
                yield return Wait(0.12f);
            }
            for (var c = 0; c < count; c++) {
                SetButton(button, true);
                Ripple();
                yield return Wait(0.09f);
                SetButton(button, false);
                yield return Wait(count > 1 ? 0.07f : 0.15f);
            }
        }

        private IEnumerator Drag(MouseButton button, string target, float secs) {
            SetButton(button, true);
            Ripple();
            yield return Wait(0.12f);
            if (secs < 0f) secs = 0.8f;
            var glide = Glide(target, secs);
            while (glide.MoveNext()) yield return glide.Current;
            yield return Wait(0.1f);
            SetButton(button, false);
            yield return Wait(0.1f);
        }

        private IEnumerator Scroll(float notches, float secs) {
            var n = Mathf.RoundToInt(Mathf.Abs(notches));
            var each = secs / Mathf.Max(n, 1);
            for (var i = 0; i < n; i++) {
                scrollThisFrame += Mathf.Sign(notches) * ScrollNotch;
                yield return Wait(each);
            }
        }

        // One wheel notch in the units the Input System reports here (120 on Windows unless uniform).
        private static float ScrollNotch =>
            InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms ? 1f : 120f;

        private IEnumerator Keys(string combo) {
            var keys = ParseKeys(combo);
            foreach (var k in keys) {
                held.Add(k);
                ShowKeys();
                yield return Wait(0.05f);
            }
            yield return Wait(0.12f);
            foreach (var k in keys.AsEnumerable().Reverse()) held.Remove(k);
            yield return Wait(0.1f);
        }

        // The app's text boxes (TMP) read the OS's key events, not the Input System's: a focused box gets the
        // characters put in directly (replacing what was selected when it was clicked, as clicking selects all).
        private IEnumerator Type(string text) {
            var first = true;
            foreach (var ch in text) {
                var field = Focused();
                if (field != null) {
                    if (first && field.onFocusSelectAll) {   // a click selected it all: the first key replaces it
                        field.text = "";
                        field.caretPosition = 0;
                    }
                    var at = Mathf.Clamp(field.caretPosition, 0, field.text.Length);
                    field.text = field.text.Insert(at, ch.ToString());
                    field.caretPosition = field.selectionAnchorPosition = field.selectionFocusPosition = at + 1;
                    field.ForceLabelUpdate();
                }
                else typing.Enqueue(ch);
                first = false;
                yield return Wait(0.07f);
            }
        }

        private static TMP_InputField Focused() {
            var selected = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            var field = selected != null ? selected.GetComponent<TMP_InputField>() : null;
            return field != null && field.isFocused ? field : null;
        }

        // submit: Enter in the focused text box (ends the edit as the app's own Enter would)
        private IEnumerator Submit() {
            var field = Focused();
            if (field != null) {
                var text = field.text;
                field.onSubmit.Invoke(text);
                field.DeactivateInputField();
                field.onEndEdit.Invoke(text);
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }
            held.Add(Key.Enter);
            ShowKeys();
            yield return Wait(0.12f);
            held.Remove(Key.Enter);
            yield return Wait(0.1f);
        }

        private static List<Key> ParseKeys(string combo) {
            var keys = new List<Key>();
            foreach (var raw in combo.Split('+')) {
                var name = raw.Trim();
                switch (name.ToLowerInvariant()) {
                    case "ctrl": keys.Add(Key.LeftCtrl); continue;
                    case "shift": keys.Add(Key.LeftShift); continue;
                    case "alt": keys.Add(Key.LeftAlt); continue;
                    case "esc": keys.Add(Key.Escape); continue;
                }
                if (name.Length == 1 && char.IsDigit(name[0])) { keys.Add((Key)Enum.Parse(typeof(Key), "Digit" + name)); continue; }
                keys.Add((Key)Enum.Parse(typeof(Key), name, true));
            }
            return keys;
        }

        private static MouseButton Button(string word) => word.Trim().ToLowerInvariant() switch {
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left,
        };

        private void SetButton(MouseButton button, bool down) {
            if (button == MouseButton.Left) left = down;
            else if (button == MouseButton.Right) right = down;
            else middle = down;
        }

        // ------------------------------------------------------------------ targets

        private static float Scale => Screen.height / 1080f;

        private Vector2 Resolve(string target) {
            var offset = Vector2.zero;
            var plus = target.LastIndexOf(" +", StringComparison.Ordinal);
            if (plus > 0) {
                var o = target.Substring(plus + 2).Split(',');
                offset = new Vector2(F(o[0]), -F(o[1])) * Scale;   // +dy is down the screen, as the coordinates
                target = target.Substring(0, plus).Trim();
            }
            return Point(target) + offset;
        }

        private Vector2 Point(string target) {
            if (target == "here") return position;   // where the cursor is (with "+dx,dy": a move relative to it)
            if (target.StartsWith("ui:")) return Middle(FindUi(target.Substring(3)));
            if (target.StartsWith("text:")) {
                var label = target.Substring(5);
                var text = FindObjectsByType<TMP_Text>(FindObjectsSortMode.None)
                    .Where(t => t.isActiveAndEnabled && t.text.Trim() == label)
                    .OrderByDescending(t => t.canvas != null ? t.canvas.sortingOrder : 0).FirstOrDefault()
                    ?? throw new Exception("no text \"" + label + "\" on screen");
                var button = text.GetComponentInParent<Selectable>();
                return Middle(button != null ? (RectTransform)button.transform : text.rectTransform);
            }
            if (target.StartsWith("world:")) {
                var p = target.Substring(6).Split(',');
                return Camera.main.WorldToScreenPoint(new Vector3(F(p[0]), F(p[1]), F(p[2])));
            }
            if (target.StartsWith("partn:")) {
                var part = PartN(int.Parse(target.Substring(6)));
                var r = part.GetComponent<Renderer>();
                return Camera.main.WorldToScreenPoint(r != null ? r.bounds.center : part.transform.position);
            }
            if (target.StartsWith("hole:")) {
                // hole:P,I = hole I (0-based, in the part's order) of the P-th part placed (1-based), on its face toward the
                // camera; hole:P,I,far = the other face.
                var p = target.Substring(5).Split(',');
                var holes = PartN(int.Parse(p[0])).GetComponentsInChildren<HoleCollider>();
                var hole = holes[int.Parse(p[1])].transform;
                var half = hole.forward * (Mathf.Abs(hole.lossyScale.z) / 2f);
                var toCam = Vector3.Dot(Camera.main.transform.position - hole.position, hole.forward) >= 0f ? 1f : -1f;
                if (p.Length > 2 && p[2] == "far") toCam = -toCam;
                return Camera.main.WorldToScreenPoint(hole.position + half * toCam);
            }
            if (target.StartsWith("tophole:")) {
                // tophole:P[,k] = the middle hole (k steps along from it) of the ones on part P's upward face.
                var p = target.Substring(8).Split(',');
                var part = int.TryParse(p[0], out var pn) ? PartN(pn) : PartById(p[0]);
                var holes = part.GetComponentsInChildren<HoleCollider>().Select(h => h.transform)
                    .Where(t => Mathf.Abs(t.forward.y) > 0.9f).ToList();
                if (holes.Count == 0) throw new Exception("part " + p[0] + " has no holes facing up");
                var top = holes.Max(t => t.position.y);
                holes = holes.Where(t => t.position.y > top - 0.3f).OrderBy(t => Vector3.Dot(t.position, Camera.main.transform.right)).ToList();
                var hole = holes[Mathf.Clamp(holes.Count / 2 + (p.Length > 1 ? int.Parse(p[1]) : 0), 0, holes.Count - 1)];
                return Camera.main.WorldToScreenPoint(hole.position + Vector3.up * (Mathf.Abs(hole.lossyScale.z) / 2f));
            }
            if (target.StartsWith("pairhole:")) {
                // pairhole:A,B = the middle one of the holes of part A lined up with a hole of part B (touching, same
                // axis): where a screw through both goes. Its face toward the camera.
                var p = target.Substring(9).Split(',');
                var a = PartN(int.Parse(p[0])).GetComponentsInChildren<HoleCollider>();
                var b = PartN(int.Parse(p[1])).GetComponentsInChildren<HoleCollider>();
                var pairs = a.Where(h => b.Any(o => Vector3.Distance(h.transform.position, o.transform.position) < 0.6f
                        && Vector3.Cross(h.transform.forward, (o.transform.position - h.transform.position)).magnitude < 0.06f))
                    .Select(h => h.transform).OrderBy(t => Vector3.Dot(t.position, Camera.main.transform.right)).ToList();
                if (pairs.Count == 0) throw new Exception("parts " + p[0] + " and " + p[1] + " have no holes lined up");
                var hole = pairs[pairs.Count / 2 + (p.Length > 2 ? int.Parse(p[2]) : 0)];
                var half = hole.forward * (Mathf.Abs(hole.lossyScale.z) / 2f);
                var toCam = Vector3.Dot(Camera.main.transform.position - hole.position, hole.forward) >= 0f ? 1f : -1f;
                return Camera.main.WorldToScreenPoint(hole.position + half * toCam);
            }
            if (target.StartsWith("mirror:")) {
                // mirror:x,y,z[,gap] = the Mirror tool's icon on the side facing that world direction (where it is
                // when dragged out for that gap)
                var w = target.Substring(7).Split(',');
                var toward = new Vector3(F(w[0]), F(w[1]), F(w[2]));
                // gap "centre": as far out as puts the mirror plane on the build's centre line
                var gap = w.Length > 3 ? (w[3] == "centre" ? Tools.MirrorTool.CentreGap(toward) : F(w[3])) : 0f;
                return Tools.MirrorTool.IconToward(toward, gap) ?? throw new Exception("the Mirror tool isn't showing");
            }
            if (target.StartsWith("tlocal:")) {
                // tlocal:TAG,x,y,z = a point in a target part's own frame (a motor's shaft face)
                var w = target.Substring(7).Split(',');
                var go = Tagged(w[0]);
                return Camera.main.WorldToScreenPoint(go.transform.TransformPoint(new Vector3(F(w[1]), F(w[2]), F(w[3]))));
            }
            if (target.StartsWith("tpos:") || target.StartsWith("tpart:")) {
                // a part of the target build (hidden or not) by its stage tag: tpos = its origin (a screw's head),
                // tpart = its mesh's middle
                var tag = target.Substring(target.IndexOf(':') + 1);
                var go = Tagged(tag);
                var at = go.transform.position;
                if (target.StartsWith("tpart:")) {
                    var mf = go.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) at = go.transform.TransformPoint(mf.sharedMesh.bounds.center);
                }
                return Camera.main.WorldToScreenPoint(at);
            }
            if (target.StartsWith("part:")) {
                var id = target.Substring(5);
                var part = FindObjectsByType<SavedObject>(FindObjectsSortMode.InstanceID)
                    .FirstOrDefault(s => s.gameObject.activeInHierarchy && s.id != null && s.id.StartsWith(id, StringComparison.OrdinalIgnoreCase))
                    ?? throw new Exception("no part " + id);
                var r = part.GetComponent<Renderer>();
                return Camera.main.WorldToScreenPoint(r != null ? r.bounds.center : part.transform.position);
            }
            var xy = target.Split(',');
            return new Vector2(F(xy[0]), 1080f - F(xy[1])) * Scale;   // written from the top left, as screenshots
        }

        // Parts in the order they appeared while the script ran (placing previews aren't parts yet).
        private readonly List<SavedObject> seenParts = new();

        private void TrackParts() {
            foreach (var saved in FindObjectsByType<SavedObject>(FindObjectsSortMode.InstanceID))
                if (saved.gameObject.activeInHierarchy && saved.gameObject.layer != LayerMask.NameToLayer("Placement")
                    && !saved.gameObject.IsDeleted() && !Target.ContainsKey(saved.gameObject) && !seenParts.Contains(saved))
                    seenParts.Add(saved);
            seenParts.RemoveAll(x => x == null);
        }

        private static GameObject PartById(string id) =>
            (FindObjectsByType<SavedObject>(FindObjectsSortMode.InstanceID)
                .FirstOrDefault(s => s.gameObject.activeInHierarchy && !s.gameObject.IsDeleted() && s.id != null && s.id.StartsWith(id, StringComparison.OrdinalIgnoreCase))
             ?? throw new Exception("no part " + id)).gameObject;

        private GameObject PartN(int n) {
            TrackParts();
            if (n < 1 || n > seenParts.Count) throw new Exception("no part " + n + " (" + seenParts.Count + " placed)");
            return seenParts[n - 1].gameObject;
        }

        private static RectTransform FindUi(string path) {
            var found = FindObjectsByType<RectTransform>(FindObjectsSortMode.None)
                .Where(r => r.gameObject.activeInHierarchy && PathOf(r.transform).EndsWith(path, StringComparison.Ordinal))
                .OrderBy(r => PathOf(r.transform).Length).FirstOrDefault();
            return found ?? throw new Exception("no interface object " + path);
        }

        private static string PathOf(Transform t) {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private static Vector2 Middle(RectTransform rect) {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var c = (corners[0] + corners[2]) / 2f;
            var canvas = rect.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
                return Camera.main.WorldToScreenPoint(c);   // (the Transform tool's arrows and rings)
            if (canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                return RectTransformUtility.WorldToScreenPoint(canvas.rootCanvas.worldCamera, c);
            return c;
        }

        private static float F(string s) => float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ framing

        // The part of the screen the 3D view really shows: right of the tool rail (and its options bar), left of the
        // parts list, under the top bar.
        private static Rect FreeArea() {
            float left = 0f, right = Screen.width, top = Screen.height;
            var corners = new Vector3[4];
            foreach (var rect in SidePanels()) {
                if (rect == null || !rect.gameObject.activeInHierarchy) continue;
                rect.GetWorldCorners(corners);
                if (corners[2].x - corners[0].x > corners[2].y - corners[0].y) continue;   // a bar along the top (tool options)
                var mid = (corners[0].x + corners[2].x) / 2f;
                if (mid < Screen.width / 2f) left = Mathf.Max(left, corners[2].x);
                else right = Mathf.Min(right, corners[0].x);
            }
            top = Screen.height - Interface.Layout.TopBarBottom * (Interface.Interface.Root != null ? Interface.Interface.Root.lossyScale.y : 1f);
            return Rect.MinMaxRect(left, 0f, right, top);
        }

        private static IEnumerable<RectTransform> SidePanels() {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            RectTransform rail = null;
            try { rail = FindUi("Tool Rail/Panel"); } catch { }
            yield return rail;
            var browser = Interface.PartsBrowser.Instance;
            if (browser != null) yield return browser.GetType().GetField("panel", flags)?.GetValue(browser) as RectTransform;
            foreach (var options in FindObjectsByType<Interface.ToolOptions>(FindObjectsSortMode.None))
                yield return options.GetType().GetField("panel", flags)?.GetValue(options) as RectTransform;
        }

        private Bounds? PartsBounds() {
            TrackParts();
            Bounds? all = null;
            foreach (var saved in seenParts) {
                if (saved == null || !saved.gameObject.activeInHierarchy || saved.gameObject.IsDeleted()) continue;
                foreach (var r in saved.GetComponentsInChildren<Renderer>()) {
                    if (!r.enabled) continue;
                    if (all == null) all = r.bounds;
                    else { var b = all.Value; b.Encapsulate(r.bounds); all = b; }
                }
            }
            return all;
        }

        // frame [secs] [fill] [dx,dy]: glides the camera (keeping its turn) so every part is in the middle of the free
        // area, as big as fill (0.5 = half its height), its middle dx,dy pixels (1080p, +dy down) from the area's middle.
        private IEnumerator Frame(string rest) {
            var all = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            // yaw:Y pitch:P glide the camera's turn too; on:TAGS frames those parts of the target (hidden or not)
            float? yaw = null, pitch = null;
            string on = null;
            foreach (var w in all) {
                if (w.StartsWith("yaw:")) yaw = F(w.Substring(4));
                else if (w.StartsWith("pitch:")) pitch = F(w.Substring(6));
                else if (w.StartsWith("on:")) on = w.Substring(3);
            }
            var p = all.Where(w => !w.Contains(":")).ToArray();
            var secs = p.Length > 0 ? F(p[0]) : 1f;
            var fill = p.Length > 1 ? F(p[1]) : 0.5f;
            var shift = Vector2.zero;
            if (p.Length > 2 && p[2].Contains(",")) { var o = p[2].Split(','); shift = new Vector2(F(o[0]), -F(o[1])) * Scale; }
            var cam = PivotCamera.Main;
            var bounds = PartsBounds();
            if (all.Contains("all") && targetBounds is Bounds tb) {
                var lb = bounds ?? tb;
                lb.Encapsulate(tb);
                bounds = lb;
            }
            if (on != null) {
                var tags = new HashSet<string>(on.Split(','));
                Bounds? some = null;
                // (the stage's parts still hidden, and those already shown)
                foreach (var kv in Target.Concat(revealedTags.Where(r => r.Key != null && r.Key.activeInHierarchy)))
                    if (kv.Key != null && tags.Contains(kv.Value)) {
                        var mf = kv.Key.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        var mb = mf.sharedMesh.bounds;
                        for (var c = 0; c < 8; c++) {
                            var corner = kv.Key.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                            if (some == null) some = new Bounds(corner, Vector3.zero);
                            else { var sb = some.Value; sb.Encapsulate(corner); some = sb; }
                        }
                    }
                if (some == null)   // not the target's: live parts whose id starts so
                    foreach (var so in FindObjectsByType<SavedObject>(FindObjectsSortMode.None)) {
                        if (!so.gameObject.activeInHierarchy || so.gameObject.IsDeleted() || so.id == null || !tags.Any(t => so.id.StartsWith(t, StringComparison.OrdinalIgnoreCase))) continue;
                        foreach (var r in so.GetComponentsInChildren<Renderer>()) {
                            if (some == null) some = r.bounds;
                            else { var sb = some.Value; sb.Encapsulate(r.bounds); some = sb; }
                        }
                    }
                if (some != null) bounds = some;
            }
            if (cam == null || bounds == null) yield break;
            DG.Tweening.DOTween.Kill(cam.transform);
            DG.Tweening.DOTween.Kill(cam.camera.transform);
            var area = FreeArea();
            var b = bounds.Value;
            var radius = Mathf.Max(b.extents.magnitude, 0.5f);
            var tan = Mathf.Tan(cam.camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var size = Mathf.Min(area.height, area.width);
            var dist = radius * Screen.height / (fill * size * tan);
            // the parts' middle shows at (area middle + shift): pivot = middle - that offset in the camera's plane
            var offset = area.center + shift - new Vector2(Screen.width, Screen.height) / 2f;
            var perPixel = 2f * dist * tan / Screen.height;
            var fromAngles = cam.transform.eulerAngles;
            var toAngles = new Vector3(pitch ?? fromAngles.x, yaw ?? fromAngles.y, 0f);
            var rot = Quaternion.Euler(toAngles);
            var toPivot = b.center - (rot * Vector3.right) * offset.x * perPixel - (rot * Vector3.up) * offset.y * perPixel;
            var fromPos = cam.transform.position;
            var fromDist = Mathf.Abs(cam.focusDistance);
            var start = Now;
            while (true) {
                var t = secs <= 0f ? 1f : Mathf.Clamp01((Now - start) / secs);
                var e = t * t * (3f - 2f * t);
                var angles = new Vector3(Mathf.LerpAngle(fromAngles.x, toAngles.x, e), Mathf.LerpAngle(fromAngles.y, toAngles.y, e), 0f);
                cam.SetTransform(Vector3.Lerp(fromPos, toPivot, e), angles, -Mathf.Lerp(fromDist, dist, e));
                if (t >= 1f) break;
                yield return null;
            }
        }

        // While recording: notes when a part's box leaves the free area (offscreen.txt), so a take can be checked.
        private readonly List<string> offscreen = new();
        private bool wasOff;

        private void WatchFraming() {
            if (!FrameRecorder.Recording || Time.frameCount % 6 != 0) return;
            var area = FreeArea();
            area.xMin -= 4f; area.xMax += 4f; area.yMin -= 4f; area.yMax += 4f;
            var cam = Camera.main;
            if (cam == null) return;
            string bad = null;
            for (var k = 0; k < seenParts.Count && bad == null; k++) {
                var saved = seenParts[k];
                if (saved == null || !saved.gameObject.activeInHierarchy || saved.gameObject.IsDeleted()) continue;
                var r = saved.GetComponent<Renderer>();
                if (r == null) continue;
                var b = r.bounds;
                for (var c = 0; c < 8; c++) {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var s = cam.WorldToScreenPoint(corner);
                    if (s.z < 0f || !area.Contains(new Vector2(s.x, s.y))) { bad = "part " + (k + 1); break; }
                }
            }
            if (bad != null && !wasOff) offscreen.Add(FrameNow + " " + bad + " leaves the view (" + Status + ")");
            if (bad == null && wasOff) offscreen.Add(FrameNow + " back in view");
            wasOff = bad != null;
        }

        // ------------------------------------------------------------------ the target build (time-lapses)

        /// <summary>A finished build made before the demo (by a test script), each part with the stage it's shown in.
        /// Hidden with "do hidetarget"; "reveal TAG" shows a stage's parts one by one; "do align TAG" moves the whole
        /// hidden build so that stage's part sits where the same part placed live in the demo is (and drops it: the
        /// live one stands in for it).</summary>
        public static readonly Dictionary<GameObject, string> Target = new();
        private static Bounds? targetBounds;

        private static readonly HashSet<GameObject> revealed = new();
        // the stage tag each revealed part had (so "tpart:TAG" still finds it once it's shown: clicking a rail)
        private static readonly Dictionary<GameObject, string> revealedTags = new();

        private static GameObject Tagged(string tag) =>
            Target.FirstOrDefault(kv => kv.Key != null && kv.Value == tag).Key
            ?? revealedTags.FirstOrDefault(kv => kv.Key != null && kv.Key.activeInHierarchy && kv.Value == tag).Key
            ?? throw new Exception("no target part tagged " + tag);

        // parts placed in the demo itself (not the target's, shown or not)
        private static IEnumerable<SavedObject> LiveParts() =>
            FindObjectsByType<SavedObject>(FindObjectsSortMode.InstanceID).Where(s => s.gameObject.activeInHierarchy
                && !Target.ContainsKey(s.gameObject) && !revealed.Contains(s.gameObject) && s.gameObject.layer != LayerMask.NameToLayer("Placement") && !s.gameObject.IsDeleted());

        private static string IdOf(GameObject go) => go.GetComponent<SavedObject>()?.id;

        private IEnumerator Reveal(string rest) {
            var p = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var tags = new HashSet<string>(p[0].Split(','));
            var secs = p.Length > 1 ? F(p[1]) : 2f;
            var parts = Target.Where(kv => kv.Key != null && tags.Contains(kv.Value) && !kv.Key.activeSelf).Select(kv => kv.Key)
                .OrderBy(g => g.transform.position.z).ThenBy(g => g.transform.position.x).ToList();
            var each = secs / Mathf.Max(parts.Count, 1);
            foreach (var part in parts) {
                var id = IdOf(part);
                // the same part, or for screws and nuts the same kind (C and F pick their own lengths)
                var family = id.Split('-')[0];
                var loose = family == "SCRW" || family == "NUT";
                var twin = LiveParts().Where(l => l.id == id || loose && l.id != null && l.id.Split('-')[0] == family).OrderBy(l => Vector3.Distance(l.transform.position, part.transform.position)).FirstOrDefault();
                var d = twin != null ? Vector3.Distance(twin.transform.position, part.transform.position) : float.MaxValue;
                if (d <= 0.08f && Quaternion.Angle(twin.transform.rotation, part.transform.rotation) < 3f) {
                    Target.Remove(part);   // the live one stands in for it
                    Destroy(part);
                    continue;
                }
                if (d <= 0.3f && twin.id == id) {   // (only a live part a little off: 2.5" took a pasted nut for the next nut along)
                    Debug.Log("[Demo] the live " + id + " was " + d.ToString("0.000") + "\" off: replaced by the build's");
                    Destroy(twin.gameObject);
                }
                part.SetActive(true);
                revealed.Add(part);
                revealedTags[part] = Target[part];
                Target.Remove(part);   // a part of the build now (framed and watched like the rest)
                if (each > 0f) yield return Wait(each);
            }
        }

        // lift [secs]: glides the live parts and the whole target build up or down together so the target's lowest
        // point is on the grid (y = 0).
        private IEnumerator Lift(float secs) {
            if (!(targetBounds is Bounds b)) yield break;
            var dy = -b.min.y;
            if (Mathf.Abs(dy) < 0.01f) yield break;
            TrackParts();
            var moving = Target.Keys.Where(g => g != null).Concat(seenParts.Where(x => x != null).Select(x => x.gameObject)).Distinct().ToList();
            var from = moving.Select(g => g.transform.position).ToList();
            var start = Now;
            while (true) {
                var t = secs <= 0f ? 1f : Mathf.Clamp01((Now - start) / secs);
                var e = t * t * (3f - 2f * t);
                for (var i = 0; i < moving.Count; i++) if (moving[i] != null) moving[i].transform.position = from[i] + Vector3.up * dy * e;
                if (t >= 1f) break;
                yield return null;
            }
            targetBounds = new Bounds(b.center + Vector3.up * dy, b.size);
            Physics.SyncTransforms();
        }

        // loadtarget FILE.pbb: opens the build into this one, hidden, its parts tagged from FILE.pbb.tags.txt ("tag x y z
        // id" per part, written with it), and remembers its box.
        private void LoadTarget(string file) {
            var before = new HashSet<SavedObject>(FindObjectsByType<SavedObject>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            var data = BuildFile.Read(file);
            // part by part (GenerateBuild would clear this build and move the camera); connecting parts last
            foreach (var part in data.parts.Where(x => x.partId != "Error").OrderBy(x => PartsManager.GetPartType(x.partId).connectingPart ? 1 : 0))
                SceneBuild.GeneratePart(part, data);
            var tags = new List<(string id, Vector3 at, string tag)>();
            foreach (var row in File.ReadAllLines(file + ".tags.txt")) {
                var w = row.Split(new[] { ' ' }, 5);   // tag x y z id (ids have spaces)
                if (w.Length == 5) tags.Add((w[4], new Vector3(F(w[1]), F(w[2]), F(w[3])), w[0]));
            }
            Target.Clear();
            revealed.Clear();
            revealedTags.Clear();
            Bounds? all = null;
            foreach (var so in FindObjectsByType<SavedObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (before.Contains(so)) continue;
                var at = so.transform.position;
                var best = tags.Where(t => t.id == so.id).OrderBy(t => (t.at - at).sqrMagnitude).Select(t => t.tag).FirstOrDefault() ?? "rest";
                Target[so.gameObject] = best;
                foreach (var r in so.GetComponentsInChildren<Renderer>()) {
                    if (all == null) all = r.bounds;
                    else { var bb = all.Value; bb.Encapsulate(r.bounds); all = bb; }
                }
            }
            targetBounds = all;
            foreach (var go in Target.Keys) go.SetActive(false);
        }

        private void Align(string tag) {
            var tagged = Target.FirstOrDefault(kv => kv.Key != null && kv.Value == tag).Key ?? throw new Exception("no target part tagged " + tag);
            var id = IdOf(tagged);
            TrackParts();
            var live = seenParts.LastOrDefault(s => s != null && s.id == id && !Target.ContainsKey(s.gameObject))
                ?? throw new Exception("no live " + id + " to line the build up with");
            // the tagged part, or else any of the build's parts like it that the live one matches by a turn about the
            // vertical only (a rail can stand with its flanges either way)
            GameObject twin = null;
            var turn = Quaternion.identity;
            foreach (var candidate in new[] { tagged }.Concat(Target.Keys.Where(g => g != null && g != tagged && IdOf(g) == id))) {
                var q = live.transform.rotation * Quaternion.Inverse(candidate.transform.rotation);
                if (Vector3.Dot(q * Vector3.up, Vector3.up) > 0.999f) { twin = candidate; turn = q; break; }
            }
            if (twin == null) {
                twin = tagged;
                var f = live.transform.rotation * Quaternion.Inverse(tagged.transform.rotation) * Vector3.forward; f.y = 0f;
                turn = f.sqrMagnitude > 0.01f ? Quaternion.LookRotation(f, Vector3.up) : Quaternion.identity;
                Debug.LogWarning("[Demo] align: no part of the build stands like the live " + id + "; turned about the vertical only");
            }
            else if (twin != tagged) { Target[tagged] = Target[twin]; Target[twin] = tag; }
            var pivot = twin.transform.position;
            foreach (var go in Target.Keys.ToList()) {
                if (go == null) continue;
                go.transform.SetPositionAndRotation(live.transform.position + turn * (go.transform.position - pivot), turn * go.transform.rotation);
            }
            Target.Remove(twin);
            Destroy(twin);
            if (targetBounds is Bounds b) {
                var c = live.transform.position + turn * (b.center - pivot);
                var size = turn * b.size;
                targetBounds = new Bounds(c, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
            }
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------ set-up actions

        private void Do(string what) {
            var p = what.Split(' ');
            switch (p[0]) {
                case "newbuild":
                    PartsManager.DestroyLoadedObjects();
                    seenParts.Clear();
                    break;
                case "partsreset": {   // the parts list as a first start shows it: Hardware, every family shut, no search
                    var browser = Interface.PartsBrowser.Instance;
                    if (browser == null) break;
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var t = browser.GetType();
                    ((HashSet<string>)t.GetField("folded", flags).GetValue(browser)).Clear();
                    t.GetMethod("SaveFolded", flags).Invoke(browser, null);
                    t.GetMethod("SetCategory", flags).Invoke(browser, new object[] { p.Length > 1 ? int.Parse(p[1]) : 1 });
                    break;
                }
                case "home": {
                    var b = Resources.FindObjectsOfTypeAll<Button>().FirstOrDefault(x => x.name == "Splash Screen Button" && x.gameObject.scene.IsValid());
                    if (b != null) b.onClick.Invoke();
                    break;
                }
                case "hidetarget": {
                    Bounds? all = null;
                    foreach (var go in Target.Keys)
                        if (go != null)
                            foreach (var r in go.GetComponentsInChildren<Renderer>()) {
                                if (all == null) all = r.bounds;
                                else { var bb = all.Value; bb.Encapsulate(r.bounds); all = bb; }
                            }
                    if (all != null) targetBounds = all;
                    // The hidden build lets go of what it was screwed to: its screws held their holes (a revealed bolt
                    // still held the hidden twin wheel's holes, right where the live wheel's are, so it never took the
                    // live ones and the wheel wasn't fastened to its gear). Shown again, they find what's there.
                    foreach (var go in Target.Keys)
                        if (go != null && go.TryGetComponent(out ConnectingPart connecting) && connecting.holeDetector != null)
                            connecting.holeDetector.RemoveAll();
                    foreach (var go in Target.Keys) if (go != null) GroupManager.Remove(go);
                    foreach (var go in Target.Keys) if (go != null) go.SetActive(false);
                    break;
                }
                case "cleartarget":   // (as a new build: the app's own clean-up, so no tool keeps a dead part)
                    foreach (var go in Target.Keys) if (go != null) go.SetActive(true);
                    PartsManager.DestroyLoadedObjects();
                    Target.Clear();
                    seenParts.Clear();
                    break;
                case "showtarget":
                    foreach (var go in Target.Keys) if (go != null) go.SetActive(true);
                    break;
                case "deselect":   // nothing selected (as clicking empty space): no gizmo or outline left on screen
                    foreach (var manager in FindObjectsByType<SelectionSystem.SelectionManager>(FindObjectsSortMode.None))
                        if (manager.current != null)
                            try { manager.ClearCurrent(); }
                            catch (Exception) { manager.current = null; }   // (a hover on a part dropped a moment ago)
                    break;
                case "droptag": {   // droptag TAG: the hidden target parts with that tag are thrown away (a mirror made them)
                    var tag = what.Substring(what.IndexOf(' ') + 1).Trim();
                    foreach (var go in Target.Where(kv => kv.Key != null && kv.Value == tag).Select(kv => kv.Key).ToList()) {
                        Target.Remove(go);
                        go.SetActive(false);
                        Destroy(go);
                    }
                    break;
                }
                case "align": Align(p[1]); break;
                case "simcamera": {   // the simulation's camera: Free (stays put) or Robot (follows)
                    var sandbox = typeof(Motion.Sandbox).GetField("instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(null) as Motion.Sandbox;
                    if (sandbox != null) sandbox.SetCamera((Motion.Sandbox.CameraMode)Enum.Parse(typeof(Motion.Sandbox.CameraMode), p[1]));
                    break;
                }
                case "loadtarget": LoadTarget(what.Substring(what.IndexOf(' ') + 1).Trim()); break;
                case "camera": {
                    var cam = PivotCamera.Main;
                    if (cam != null) cam.SetTransform(new Vector3(F(p[1]), F(p[2]), F(p[3])), new Vector3(F(p[5]), F(p[4]), 0f), -Mathf.Abs(F(p[6])));
                    break;
                }
                default: throw new Exception("unknown set-up " + p[0]);
            }
        }

        // ------------------------------------------------------------------ what's drawn

        private void MakeOverlay() {
            var go = new GameObject("Demo Overlay");
            go.transform.SetParent(transform);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            captionBox = Box("Caption", new Color(0.05f, 0.06f, 0.08f, 0.82f));
            captionBox.anchorMin = captionBox.anchorMax = new Vector2(0.5f, 0f);
            captionBox.pivot = new Vector2(0.5f, 0f);
            captionBox.anchoredPosition = new Vector2(0f, 40f * Scale);
            captionText = Label(captionBox, 30f);
            captionBox.gameObject.SetActive(false);

            keysChip = Box("Keys", new Color(0.05f, 0.06f, 0.08f, 0.85f));
            keysChip.anchorMin = keysChip.anchorMax = new Vector2(0.5f, 0f);
            keysChip.pivot = new Vector2(0.5f, 0f);
            keysChip.anchoredPosition = new Vector2(0f, 130f * Scale);
            keysText = Label(keysChip, 34f);
            keysChip.gameObject.SetActive(false);

            cursor = new GameObject("Cursor", typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
            cursor.SetParent(canvas.transform, false);
            cursor.GetComponent<RawImage>().texture = ArrowTexture();
            cursor.GetComponent<RawImage>().raycastTarget = false;
            cursor.pivot = new Vector2(2f / 48f, 1f - 2f / 48f);
            cursor.anchorMin = cursor.anchorMax = Vector2.zero;
            cursor.sizeDelta = new Vector2(48f, 48f) * Scale * 0.75f;
        }

        private RectTransform Box(string name, Color color) {
            var rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            rect.SetParent(canvas.transform, false);
            var image = rect.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset((int)(26 * Scale), (int)(26 * Scale), (int)(12 * Scale), (int)(12 * Scale));
            return rect;
        }

        private TextMeshProUGUI Label(RectTransform parent, float size) {
            var text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            text.fontSize = size * Scale;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            var font = FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None).Select(t => t.font).FirstOrDefault(f => f != null);
            if (font != null) text.font = font;
            return text;
        }

        private void Caption(string text) {
            captionBox.gameObject.SetActive(!string.IsNullOrEmpty(text));
            captionText.text = text;
        }

        private void ShowKeys() {
            var names = held.OrderBy(k => k is Key.LeftCtrl or Key.LeftShift or Key.LeftAlt ? 0 : 1).Select(KeyName);
            keysText.text = string.Join("  +  ", names);
            keysShownUntil = Now + 0.9f;
        }

        private static string KeyName(Key k) {
            if (k == Key.LeftCtrl) return "Ctrl";
            if (k == Key.LeftShift) return "Shift";
            if (k == Key.LeftAlt) return "Alt";
            if (k == Key.Escape) return "Esc";
            var s = k.ToString();
            return s.StartsWith("Digit") ? s.Substring(5) : s;
        }

        private void Ripple() {
            var ring = new GameObject("Ripple", typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
            ring.SetParent(canvas.transform, false);
            ring.GetComponent<RawImage>().texture = RingTexture();
            ring.GetComponent<RawImage>().raycastTarget = false;
            ring.anchorMin = ring.anchorMax = Vector2.zero;
            ring.anchoredPosition = position;
            ring.SetAsFirstSibling();
            ripples.Add((ring, Now));
        }

        private void DrawOverlay() {
            if (cursor == null) return;
            cursor.anchoredPosition = position;
            cursor.SetAsLastSibling();
            if (held.Count > 0) keysShownUntil = Mathf.Max(keysShownUntil, Now + 0.5f);
            keysChip.gameObject.SetActive(Now < keysShownUntil && keysText.text.Length > 0);
            for (var i = ripples.Count - 1; i >= 0; i--) {
                var (ring, start) = ripples[i];
                var t = (Now - start) / 0.45f;
                if (t >= 1f) {
                    Destroy(ring.gameObject);
                    ripples.RemoveAt(i);
                    continue;
                }
                ring.sizeDelta = Vector2.one * Mathf.Lerp(18f, 70f, 1f - (1f - t) * (1f - t)) * Scale;
                ring.GetComponent<RawImage>().color = new Color(0.35f, 0.75f, 1f, 0.9f * (1f - t));
            }
        }

        private static Texture2D arrow, ringTex;

        // A plain white pointer with a dark outline, like the system's.
        private static Texture2D ArrowTexture() {
            if (arrow != null) return arrow;
            const int n = 48;
            arrow = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            Vector2[] shape = { new(2, 2), new(2, 36), new(10.5f, 28), new(16.5f, 41), new(22, 38.5f), new(16, 26), new(27.5f, 26) };
            var pixels = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++) {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var d = EdgeDistance(p, shape);   // > 0 inside
                    var fill = Mathf.Clamp01(d - 1.2f + 0.5f);
                    var outline = Mathf.Clamp01(d + 1.3f + 0.5f);
                    var c = Color.Lerp(new Color(0.08f, 0.09f, 0.1f, outline), Color.white, fill);
                    c.a = Mathf.Max(outline, fill);
                    pixels[(n - 1 - y) * n + x] = c;
                }
            arrow.SetPixels32(pixels);
            arrow.Apply();
            return arrow;
        }

        private static float EdgeDistance(Vector2 p, Vector2[] poly) {
            var inside = false;
            var best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++) {
                var a = poly[i];
                var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                var ab = b - a;
                var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return inside ? best : -best;
        }

        private static Texture2D RingTexture() {
            if (ringTex != null) return ringTex;
            const int n = 64;
            ringTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++) {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
                    var a = Mathf.Clamp01(1f - Mathf.Abs(d - 27f) / 3.5f);
                    pixels[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            ringTex.SetPixels32(pixels);
            ringTex.Apply();
            return ringTex;
        }
    }
}
