using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Protobot.Builds {
    // V2: what the user did, step by step, so a bug report can be followed and the problem made again. One file per
    // session (persistentDataPath/Logs/Sessions/<start>.log, the last 15 kept), a line per event:
    //   "+12.345 kind  text"   (seconds since the app started)
    // Kinds: start (version, computer, graphics), step (an undo step: each part it touched with id, place, turn and
    // its saved state, so the build can be rebuilt from it), undo/redo, tool, select, place (part being placed), click
    // (where, the part or interface under it), key, camera (every 2 s when it moved), build (opened/new/saved path),
    // sim, game, tool-window (chain/band/tube/cable tools), say (messages shown), log/warn/error (Unity's own,
    // errors with their stack). Bug reports (BugReport) send the session's file.
    public class ActionLog : MonoBehaviour {
        public static string Folder => Path.Combine(Application.persistentDataPath, "Logs", "Sessions");
        public static string CurrentFile { get; private set; }
        public static string PreviousFile { get; private set; }

        private static ActionLog instance;
        private static readonly StringBuilder pending = new();
        private static readonly object gate = new();
        private static float flushAt;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Make() {
            if (instance != null || !Application.isPlaying) return;
            instance = new GameObject("Action Log").AddComponent<ActionLog>();
            DontDestroyOnLoad(instance.gameObject);
        }

        private void Awake() {
            try {
                Directory.CreateDirectory(Folder);
                var old = Directory.GetFiles(Folder, "*.log").OrderBy(f => f).ToList();
                PreviousFile = old.LastOrDefault();
                foreach (var f in old.Take(Math.Max(0, old.Count - 14))) File.Delete(f);
                CurrentFile = Path.Combine(Folder, DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", Inv) + ".log");
            }
            catch (Exception) { }
            clockOffset = Time.realtimeSinceStartupAsDouble - clock.Elapsed.TotalSeconds;
            Application.logMessageReceivedThreaded += OnUnityLog;
            Application.quitting += Flush;
            Write("start", BuildInfo.Full + " | " + SystemInfo.operatingSystem + " | " + SystemInfo.processorType + " x" + SystemInfo.processorCount
                           + " | " + SystemInfo.systemMemorySize + " MB | " + SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + ", "
                           + SystemInfo.graphicsMemorySize + " MB) | " + Screen.width + "x" + Screen.height + " | " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
            StateSystem.OnStateAdded += s => Write("step", Describe(s));
            StateSystem.OnUndo += s => Write("undo", "back to step " + StateSystem.curIndex + ": " + Describe(s));
            StateSystem.OnRedo += s => Write("redo", "to step " + StateSystem.curIndex + ": " + Describe(s));
        }

        private void OnDestroy() {
            Application.logMessageReceivedThreaded -= OnUnityLog;
            Flush();
        }

        // ------------------------------------------------------------------ writing

        private static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private static double clockOffset;   // seconds since start-up when the clock started (set on the main thread)

        /// <summary>Logs an event (any thread).</summary>
        public static void Write(string kind, string text) {
            // V2: Time can only be read on the main thread, and Unity logs from other threads come here too.
            var t = clockOffset + clock.Elapsed.TotalSeconds;
            lock (gate) pending.Append('+').Append(t.ToString("0.000", Inv)).Append(' ').Append(kind.PadRight(6)).Append(' ')
                                .Append((text ?? "").Replace("\r", "").Replace("\n", " ⏎ ")).Append('\n');
        }

        public static void Flush() {
            string text;
            lock (gate) {
                if (pending.Length == 0 || CurrentFile == null) return;
                text = pending.ToString();
                pending.Clear();
            }
            try { File.AppendAllText(CurrentFile, text); }
            catch (Exception) { }
        }

        private static void OnUnityLog(string message, string stack, LogType type) {
            if (message != null && (message.StartsWith("State has changed") || message.StartsWith("[ComponentCacher]"))) return;
            var error = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            Write(error ? "error" : type == LogType.Warning ? "warn" : "log", message + (error && !string.IsNullOrEmpty(stack) ? " | " + stack.Trim() : ""));
            if (error) flushNow = true;
        }

        private static volatile bool flushNow;

        // ------------------------------------------------------------------ describing things

        private static string F(float f) => f.ToString("0.###", Inv);
        private static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
        private static string Q(Quaternion q) => V(q.eulerAngles);

        public static string Part(GameObject part) {
            if (part == null) return "-";
            var saved = part.GetComponentInParent<SavedObject>();
            return saved != null ? (saved.id ?? part.name) + "#" + saved.gameObject.GetInstanceID() : part.name;
        }

        private static string Describe(State state) {
            if (state?.elements == null) return "";
            var sb = new StringBuilder();
            foreach (var e in state.elements) {
                if (sb.Length > 0) sb.Append(" ; ");
                if (e is ObjectElement o && o.obj != null) {
                    var saved = o.obj.GetComponent<SavedObject>();
                    sb.Append(Part(o.obj)).Append(o.existing ? " at " : " deleted at ").Append(V(o.position)).Append(" turn ").Append(Q(o.rotation));
                    if (saved != null && !string.IsNullOrEmpty(saved.state)) sb.Append(" state ").Append(saved.state.Length > 300 ? saved.state.Substring(0, 300) + "…" : saved.state);
                }
                else sb.Append(e.GetType().Name);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ watching

        private string tool, select, placing, buildPath, toolWindow;
        private BuildsManager manager;
        private bool sim, game;
        private Vector3 cameraAt;
        private Quaternion cameraTurn;
        private float cameraLogged;

        private void LateUpdate() {
            try { Watch(); }
            catch (Exception) { }
            if (flushNow || Time.realtimeSinceStartup >= flushAt) {
                flushNow = false;
                flushAt = Time.realtimeSinceStartup + 1f;
                Flush();
            }
        }

        private void Watch() {
            Changed(ref tool, ToolRail.Current, "tool");
            Changed(ref select, Part(Tools.SelectTool.SelectedPart), "select");
            Changed(ref placing, Placement.IsPlacing && Placement.Instance != null ? PlacingName() : null, "place");
            if (manager == null) manager = FindAnyObjectByType<BuildsManager>();
            Changed(ref buildPath, manager != null ? (manager.buildPath ?? "") + " \"" + ModelExport.BuildName() + "\"" : null, "build");
            var window = Motion.TubeTool.Active ? "tube/cable tool" : Motion.StringTool.Active ? "string tool" : Motion.ElasticTool.Active ? "band tool" : Motion.ChainTool.Active ? "chain tool" : null;
            Changed(ref toolWindow, window, "window");
            if (Motion.Sandbox.Running != sim) {
                sim = Motion.Sandbox.Running;
                Write("sim", sim ? "started" : "stopped");
            }
            if (Game.SkillsGame.Active != game) {
                game = Game.SkillsGame.Active;
                Write("game", game ? "opened" : "closed");
            }
            Clicks();
            Keys();
            var cam = Camera.main;
            if (cam != null && Time.realtimeSinceStartup - cameraLogged > 2f
                && ((cam.transform.position - cameraAt).sqrMagnitude > 0.01f || Quaternion.Angle(cam.transform.rotation, cameraTurn) > 1f)) {
                cameraAt = cam.transform.position;
                cameraTurn = cam.transform.rotation;
                cameraLogged = Time.realtimeSinceStartup;
                Write("camera", V(cameraAt) + " turn " + Q(cameraTurn) + (cam.orthographic ? " ortho " + F(cam.orthographicSize) : ""));
            }
        }

        private static string PlacingName() {
            return Placement.Instance.PlacingId ?? "a part";
        }

        private static void Changed(ref string last, string now, string kind) {
            if (now == last) return;
            last = now;
            Write(kind, now ?? "-");
        }

        private static readonly List<RaycastResult> uiHits = new();

        private static void Clicks() {
            var mouse = Mouse.current;
            if (mouse == null) return;
            foreach (var (button, name) in new[] { (mouse.leftButton, "left"), (mouse.rightButton, "right"), (mouse.middleButton, "middle") }) {
                if (!button.wasPressedThisFrame && !button.wasReleasedThisFrame) continue;
                var at = mouse.position.ReadValue();
                var text = name + (button.wasPressedThisFrame ? " down " : " up ") + F(at.x) + "," + F(at.y);
                if (button.wasPressedThisFrame) text += " on " + UnderMouse(at);
                Write("click", text);
            }
        }

        private static string UnderMouse(Vector2 at) {
            var events = EventSystem.current;
            if (events != null) {
                uiHits.Clear();
                events.RaycastAll(new PointerEventData(events) { position = at }, uiHits);
                var ui = uiHits.FirstOrDefault(h => h.gameObject != null && (Overlay.Instance == null || h.gameObject != Overlay.Instance.gameObject));
                if (ui.gameObject != null) return "interface " + PathOf(ui.gameObject.transform);
            }
            var cam = Camera.main;
            if (cam != null && Physics.Raycast(cam.ScreenPointToRay(at), out var hit, 5000f, ~0, QueryTriggerInteraction.Collide))
                return Part(hit.collider.gameObject) + " (" + hit.collider.name + ") at " + V(hit.point);
            return "nothing";
        }

        private static string PathOf(Transform t) {
            var names = new List<string>();
            for (var i = 0; t != null && i < 4; i++, t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static void Keys() {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.anyKey.wasPressedThisFrame) return;
            if (InputEvents.TextInput.IsTyping) {
                Write("key", "(typing in a text box)");
                return;
            }
            var mods = (keyboard.ctrlKey.isPressed ? "Ctrl+" : "") + (keyboard.shiftKey.isPressed ? "Shift+" : "") + (keyboard.altKey.isPressed ? "Alt+" : "");
            foreach (var key in keyboard.allKeys)
                if (key.wasPressedThisFrame && key.keyCode != Key.LeftCtrl && key.keyCode != Key.RightCtrl && key.keyCode != Key.LeftShift
                    && key.keyCode != Key.RightShift && key.keyCode != Key.LeftAlt && key.keyCode != Key.RightAlt)
                    Write("key", mods + key.keyCode);
        }
    }
}
