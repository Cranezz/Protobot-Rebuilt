using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using Protobot.Motion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Game {
    // V2: Robot Skills on a competition field (File > Play game…, the launch window's Play game, the top bar's Play).
    // Setup: the field appears round the robot (the build never moves: the field is placed so the robot stands where
    // its start position says), with a panel to set the start exactly (X, Y in inches from the field's middle, heading
    // in degrees), the legal-start checklist (SG1/RSC2), saved starts, the preload and an alignment tool (a build file,
    // removed when the run starts, like a real one). Run: a 3-second countdown, then one minute of driving in the
    // simulation (Sandbox, hosted: its own Esc and messages) with the field's pins, cups and toggles as pieces, Match
    // Loads dropped into the red Loaders from the HUD, the score counted live (OverrideScoring); at 0:00 the robot stops,
    // the score is taken once everything is still (or after 5 s, SC1) and the results show.
    public class SkillsGame : MonoBehaviour {
        public enum State { Setup, Countdown, Running, Ending, Results }

        public static SkillsGame Current { get; private set; }
        public static bool Active => Current != null;

        public GameDefinition definition;
        public GameField field;
        public State state;

        // The robot as built (it never moves): its footprint middle on the floor and which way is its front.
        public Vector3 robotCenter;
        public float robotFloor;
        public Vector3 front = Vector3.forward;
        public float depth, width, height;              // along its front, across, up (inches)
        public string buildName = "Untitled";

        // Where it starts, in the field frame.
        public float x, y, heading = 90f;
        public bool preload = true;

        // The alignment tool (a build file's parts, fixed to the field until the run starts).
        public Transform tool;
        public string toolName;
        public float toolX, toolY, toolHeading;

        // The run.
        public float timeLeft, countdown;
        public SkillsScore score = new();
        public int best;
        public readonly Dictionary<string, int> matchLoads = new();
        public int loader;
        private List<Pin> pins = new();
        private List<Cup> cups = new();
        private List<Toggle> toggles = new();
        private Transform preloadGhost;
        private float endingSince, nextScore;
        private Func<bool> savedBlocked;
        private Vector3 savedFocus, savedAngles;
        private float savedDistance;

        public event Action Changed;

        // ------------------------------------------------------------------ opening and closing

        public static void Open(GameDefinition definition, bool lightField) {
            if (Active) return;
            if (definition == null) {
                Interface.Interface.Say("No game is installed", false);
                return;
            }
            if (Sandbox.Running) Sandbox.Stop();
            MotorRunner.StopAll();
            MotorControls.Release();
            Popup.Close();
            if (Placement.IsPlacing) Placement.Instance.StopPlacing();
            foreach (var manager in FindObjectsByType<SelectionSystem.SelectionManager>(FindObjectsSortMode.None)) manager.ClearCurrent();

            var parts = FindObjectsByType<SavedObject>(FindObjectsSortMode.None)
                .Where(s => s.gameObject.activeInHierarchy && !s.gameObject.IsDeleted()).ToList();
            if (parts.Count == 0) {
                Interface.Interface.Say("Build or open a robot first: the game puts your build on the field", false);
                return;
            }

            var go = new GameObject("Game Session");
            var game = go.AddComponent<SkillsGame>();
            Current = game;
            game.definition = definition;
            try {
                game.MeasureRobot(parts);
                game.field = GameField.Create(definition, lightField);
            }
            catch (Exception e) {
                Debug.LogException(e);
                Current = null;
                if (game.field != null) Destroy(game.field.gameObject);
                Destroy(go);
                Interface.Interface.Say("The field couldn't be set up: " + e.Message, false);
                return;
            }
            game.savedBlocked = SelectionSystem.SelectionManager.Blocked;
            SelectionSystem.SelectionManager.Blocked = () => true;
            Sandbox.SetBuildInterface(false);
            if (PivotCamera.Main != null) {
                game.savedFocus = PivotCamera.Main.transform.position;
                game.savedAngles = PivotCamera.Main.transform.eulerAngles;
                game.savedDistance = PivotCamera.Main.camera.transform.localPosition.z;
            }
            game.HideGrid();
            game.best = PlayerPrefs.GetInt(game.BestKey, 0);
            game.DefaultStart();
            game.state = State.Setup;
            GameUi.ShowSetup(game);
            game.FrameCamera();
            Interface.Interface.Say($"{definition.Name}: Driving Skills. Set your start, then Start run", true);
        }

        public void Exit() {
            if (Sandbox.Running) Sandbox.Stop();
            ClearHosting();
            if (field != null) Destroy(field.gameObject);
            if (tool != null) Destroy(tool.gameObject);
            ShowGrid();
            SelectionSystem.SelectionManager.Blocked = savedBlocked;
            Sandbox.SetBuildInterface(true);
            GameUi.CloseAll();
            if (PivotCamera.Main != null) PivotCamera.Main.SetTransform(savedFocus, savedAngles, savedDistance);
            Current = null;
            Destroy(gameObject);
            Interface.Interface.Say("Back to building", true);
        }

        private static void ClearHosting() {
            Sandbox.Hosted = false;
            Sandbox.FloorHeight = null;
            Sandbox.BuildEnvironment = null;
            Sandbox.DriveEnabled = true;
        }

        private void OnDestroy() {
            ShowGrid();
            if (Current == this) {
                ClearHosting();
                Current = null;
            }
        }

        private string BestKey => $"V2.Game.Best.{definition.title}.{buildName}";
        private string FrontKey => $"V2.Game.Front.{buildName}";

        // ------------------------------------------------------------------ the robot

        private void MeasureRobot(List<SavedObject> parts) {
            var first = true;
            var bounds = new Bounds();
            foreach (var part in parts)
                foreach (var r in part.GetComponentsInChildren<MeshRenderer>()) {
                    if (!r.enabled || r.gameObject.layer == HoleCollider.HOLE_COLLISIONS_LAYER || r.GetComponent<HoleFace>() != null) continue;
                    if (first) { bounds = r.bounds; first = false; }
                    else bounds.Encapsulate(r.bounds);
                }
            if (first) throw new Exception("the build has nothing to draw");
            robotCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            robotFloor = bounds.min.y;
            height = bounds.size.y;
            var manager = FindAnyObjectByType<Builds.BuildsManager>();
            if (manager != null && !string.IsNullOrEmpty(manager.buildPath)) buildName = System.IO.Path.GetFileNameWithoutExtension(manager.buildPath);

            // The front: saved for this build, else from the drive (right side minus left side, turned a quarter), else +Z.
            var saved = PlayerPrefs.GetString(FrontKey, "");
            if (!string.IsNullOrEmpty(saved)) front = AxisOf(saved);
            else {
                var lefts = MotorDrive.Live().Where(m => Controls.Side(m.gameObject) == "L").Select(m => m.transform.position).ToList();
                var rights = MotorDrive.Live().Where(m => Controls.Side(m.gameObject) == "R").Select(m => m.transform.position).ToList();
                if (lefts.Count > 0 && rights.Count > 0) {
                    var right = rights.Aggregate(Vector3.zero, (s, p) => s + p) / rights.Count - lefts.Aggregate(Vector3.zero, (s, p) => s + p) / lefts.Count;
                    front = Snap(Vector3.Cross(right, Vector3.up));
                }
                else front = Vector3.forward;
            }
            Footprint(bounds);
        }

        private Bounds robotBounds;

        private void Footprint(Bounds bounds) {
            robotBounds = bounds;
            var along = Mathf.Abs(front.x) > 0.5f ? bounds.size.x : bounds.size.z;
            var across = Mathf.Abs(front.x) > 0.5f ? bounds.size.z : bounds.size.x;
            depth = along;
            width = across;
        }

        private static Vector3 Snap(Vector3 v) {
            v.y = 0f;
            if (v.sqrMagnitude < 1e-6f) return Vector3.forward;
            return Mathf.Abs(v.x) > Mathf.Abs(v.z) ? new Vector3(Mathf.Sign(v.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        public static readonly string[] FrontNames = { "+Z", "+X", "-Z", "-X" };

        public static Vector3 AxisOf(string name) => name switch {
            "+X" => Vector3.right, "-X" => Vector3.left, "-Z" => Vector3.back, _ => Vector3.forward,
        };

        public string FrontName => front == Vector3.right ? "+X" : front == Vector3.left ? "-X" : front == Vector3.back ? "-Z" : "+Z";

        public void SetFront(string name) {
            front = AxisOf(name);
            PlayerPrefs.SetString(FrontKey, name);
            Footprint(robotBounds);
            Apply();
            Interface.Interface.Say("The robot's front is its " + name + " side (the yellow arrow)", true);
        }

        // ------------------------------------------------------------------ the start

        /// <summary>Back against the red wall in the middle of the red Alliance Station's Quadrant, facing the field.</summary>
        public void DefaultStart() {
            heading = 90f;
            x = -GameField.Half + depth / 2f + 0.02f;
            y = 0f;
            toolX = x - depth / 2f;
            toolY = 0f;
            toolHeading = 90f;
            Apply();
        }

        /// <summary>Places the field so the robot stands at (x, y) facing `heading`.</summary>
        public void Apply() {
            if (field == null) return;
            var local = new Vector3(Mathf.Sin(heading * Mathf.Deg2Rad), 0f, Mathf.Cos(heading * Mathf.Deg2Rad));
            var rotation = Quaternion.FromToRotation(local, front);
            if (Mathf.Abs(Vector3.Dot(local, front) + 1f) < 1e-4f) rotation = Quaternion.AngleAxis(180f, Vector3.up);
            // The build never moves, the field does: the camera goes with the field, so on screen the field holds
            // still and the robot is what moves and turns.
            var t = field.transform;
            var camera = PivotCamera.Main;
            var keepView = camera != null && cameraFollows;
            Vector3 focus = default, look = default;
            if (keepView) {
                focus = t.InverseTransformPoint(camera.transform.position);
                look = (Quaternion.Inverse(t.rotation) * camera.transform.rotation).eulerAngles;
            }
            t.rotation = rotation;
            t.position = new Vector3(robotCenter.x, robotFloor, robotCenter.z) - rotation * new Vector3(x, 0f, y);
            if (keepView) {
                var turn = (rotation * Quaternion.Euler(look)).eulerAngles;
                turn.z = 0f;
                camera.SetTransform(t.TransformPoint(focus), turn, camera.camera.transform.localPosition.z);
            }
            cameraFollows = true;
            if (tool != null) {
                tool.localPosition = new Vector3(toolX, 0f, toolY);
                tool.localRotation = Quaternion.Euler(0f, toolHeading, 0f);
            }
            PlacePreloadGhost();
            Physics.SyncTransforms();
            Changed?.Invoke();
        }

        private bool cameraFollows;

        // The build's grid planes would slide across the field as it moves: hidden while the game is open.
        private readonly List<Renderer> hiddenGrid = new();

        private void HideGrid() {
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (r.gameObject.layer == 9 && r.enabled && (r.name.Contains("Perspective Plane") || r.name.Contains("Orthographic Plane") || r.name == "AxisPlane")) {
                    r.enabled = false;
                    hiddenGrid.Add(r);
                }
        }

        private void ShowGrid() {
            foreach (var r in hiddenGrid)
                if (r != null) r.enabled = true;
            hiddenGrid.Clear();
        }

        public void SetPose(float newX, float newY, float newHeading) {
            x = newX;
            y = newY;
            heading = Mathf.Repeat(newHeading, 360f);
            Apply();
        }

        public void Nudge(float dx, float dy, float dHeading) => SetPose(x + dx, y + dy, heading + dHeading);

        // The robot's footprint corners in the field frame.
        public Vector2[] Corners() {
            var f = new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
            var r = new Vector2(f.y, -f.x);
            var c = new Vector2(x, y);
            return new[] { c + f * depth / 2f + r * width / 2f, c + f * depth / 2f - r * width / 2f, c - f * depth / 2f - r * width / 2f, c - f * depth / 2f + r * width / 2f };
        }

        /// <summary>The legal-start rules (SG1, RSC2.1) and whether each holds.</summary>
        public List<(string rule, bool ok)> Checks() {
            var list = new List<(string, bool)>();
            const float Slack = 0.02f;
            list.Add(($"Fits in 18\" × 18\" × 18\" ({Number(depth)} × {Number(width)} × {Number(height)})", depth <= 18f + Slack && width <= 18f + Slack && height <= 18f + Slack));
            var corners = Corners();
            var edges = corners.Concat(Enumerable.Range(0, 4).Select(i => (corners[i] + corners[(i + 1) % 4]) / 2f)).ToList();
            list.Add(("In the Quadrant by the red Alliance Station", edges.All(p => p.x <= -Mathf.Abs(p.y) + Slack && p.x >= -GameField.Half - Slack)));
            list.Add(("Touching the Field Perimeter", edges.Any(p => p.x <= -GameField.Half + 0.1f)));
            list.Add(("Not in a Load Zone", !edges.Any(GameField.InLoadZone)));
            list.Add(("Not touching a Goal", !field.goals.Any(g => Overlaps(corners, g.position, 5.95f))));
            list.Add(("Clear of the Toggle", height < 11.5f || !edges.Any(p => p.x <= -GameField.Half + 1.5f && Mathf.Abs(p.y) < 14f)));
            return list;
        }

        public bool Legal => Checks().All(c => c.ok);

        // Whether a turned rectangle (corners) overlaps an upright square (center, half size): separating axes.
        private static bool Overlaps(Vector2[] corners, Vector2 center, float half) {
            var square = new[] { center + new Vector2(-half, -half), center + new Vector2(half, -half), center + new Vector2(half, half), center + new Vector2(-half, half) };
            var axes = new List<Vector2> { Vector2.right, Vector2.up, (corners[1] - corners[0]).normalized, (corners[2] - corners[1]).normalized };
            foreach (var axis in axes) {
                var (a0, a1) = Project(corners, axis);
                var (b0, b1) = Project(square, axis);
                if (a1 < b0 || b1 < a0) return false;
            }
            return true;
        }

        private static (float, float) Project(Vector2[] points, Vector2 axis) {
            var lo = float.MaxValue;
            var hi = float.MinValue;
            foreach (var p in points) {
                var d = Vector2.Dot(p, axis);
                lo = Mathf.Min(lo, d);
                hi = Mathf.Max(hi, d);
            }
            return (lo, hi);
        }

        public static string Number(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>Slides the robot straight back until it touches the perimeter, a goal or the alignment tool.</summary>
        public void BackUp() {
            Physics.SyncTransforms();
            var back = -field.Direction(heading);
            var rotation = Quaternion.LookRotation(field.Direction(heading), Vector3.up);
            var center = new Vector3(robotCenter.x, robotFloor + height / 2f + 0.25f, robotCenter.z);
            var half = new Vector3(width / 2f, Mathf.Max(0.1f, height / 2f - 0.3f), depth / 2f);
            var mask = 1 << Sandbox.FieldLayer;
            if (Physics.CheckBox(center, half, rotation, mask, QueryTriggerInteraction.Ignore)) {
                Interface.Interface.Say("The robot already touches something: move it forward first", false);
                return;
            }
            if (!Physics.BoxCast(center, half, back, out var hit, rotation, 60f, mask, QueryTriggerInteraction.Ignore)) {
                Interface.Interface.Say("Nothing behind the robot within 60\"", false);
                return;
            }
            var move = Mathf.Max(0f, hit.distance - 0.01f);
            var step = new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad)) * move;
            SetPose(x - step.x, y - step.y, heading);
            Interface.Interface.Say($"Backed up {Number(move)}\" to {(hit.collider.transform.IsChildOf(tool != null ? tool : transform) ? "the alignment tool" : "the field")}", true);
        }

        private void PlacePreloadGhost() {
            if (preloadGhost == null && field != null) {
                var stock = field.StockPin("Red-Neutral");
                if (stock != null) {
                    preloadGhost = Instantiate(stock, field.transform).transform;
                    preloadGhost.gameObject.SetActive(true);
                    preloadGhost.name = "Preload";
                }
            }
            if (preloadGhost == null) return;
            preloadGhost.gameObject.SetActive(preload && state == State.Setup);
            var (position, rotation) = PreloadPose();
            preloadGhost.SetPositionAndRotation(position, rotation);
        }

        // The Preload: standing on the tiles against the robot's front, alliance-colored half up.
        private (Vector3, Quaternion) PreloadPose() {
            var f = field.Direction(heading);
            var p = new Vector3(robotCenter.x, robotFloor, robotCenter.z) + f * (depth / 2f + 1.65f) + Vector3.up * 3.3f;
            return (p, Quaternion.LookRotation(Vector3.up, f));
        }

        public void SetPreload(bool on) {
            preload = on;
            PlacePreloadGhost();
            Interface.Interface.Say(on ? "Preload: a red/yellow pin in front of the robot" : "No preload on the robot", true);
        }

        // ------------------------------------------------------------------ alignment tool

        public void LoadTool() {
            var paths = SFB.StandaloneFileBrowser.OpenFilePanel("Open an alignment tool (a build file)", "", "pbb", false);
            InputEvents.TextInput.AfterFileWindow();
            if (paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            LoadTool(paths[0]);
        }

        /// <summary>Loads a build file as the alignment tool.</summary>
        public void LoadTool(string path) {
            var paths = new[] { path };
            Builds.BuildData data;
            try {
                data = Builds.BuildFile.Read(paths[0]);
            }
            catch (Exception e) {
                Interface.Interface.Say("That file couldn't be read: " + e.Message, false);
                return;
            }
            if (data?.parts == null || data.parts.Length == 0) {
                Interface.Interface.Say("That build has no parts", false);
                return;
            }
            RemoveTool(false);
            tool = new GameObject("Alignment Tool").transform;
            tool.SetParent(field.transform, false);
            // The parts are copied where the build has them (the holder at the world's origin), then the holder goes
            // into the tool: the tool's own axes are its build's (heading 0 = the build's +Z along the field's +Y).
            var holder = new GameObject("Parts").transform;
            var made = 0;
            foreach (var part in data.parts) {
                if (part.partId == "Error") continue;
                GameObject generated = null;
                try {
                    generated = PartsManager.GeneratePart(part.partId, part.GetPos(), part.GetRot());
                }
                catch (Exception) { }
                if (generated == null) continue;
                foreach (var r in generated.GetComponentsInChildren<MeshRenderer>()) {
                    if (r.gameObject.layer == HoleCollider.HOLE_COLLISIONS_LAYER || r.GetComponent<MeshFilter>() == null) continue;
                    // Drawn on the Default layer; its collision shape (for Back up) on the field's.
                    var copy = new GameObject(r.name);
                    copy.transform.SetParent(holder, true);
                    copy.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                    copy.transform.localScale = r.transform.lossyScale;
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                    copy.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                    if (mesh != null && mesh.isReadable) {
                        var shape = new GameObject("Collider") { layer = Sandbox.FieldLayer };
                        shape.transform.SetParent(copy.transform, false);
                        var mc = shape.AddComponent<MeshCollider>();
                        mc.sharedMesh = ElementShapes.Hull(mesh);
                        mc.convex = true;
                    }
                    made++;
                }
                DestroyImmediate(generated);
            }
            if (made == 0) {
                Destroy(holder.gameObject);
                RemoveTool(false);
                Interface.Interface.Say("Nothing in that build could be made", false);
                return;
            }
            // Its footprint's middle on the floor is the tool's origin.
            var bounds = holder.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            var offset = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            foreach (Transform child in holder) child.position -= offset;
            holder.SetParent(tool, false);
            toolName = System.IO.Path.GetFileNameWithoutExtension(paths[0]);
            // Behind the robot, against the wall.
            var toolDepth = bounds.size.z;
            toolHeading = heading;
            var back = new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
            toolX = x - back.x * (depth / 2f + toolDepth / 2f);
            toolY = y - back.y * (depth / 2f + toolDepth / 2f);
            Apply();
            Interface.Interface.Say($"Alignment tool \"{toolName}\": place it, then Back up to push the robot against it. It's taken away when the run starts", true);
        }

        public void RemoveTool(bool say = true) {
            if (tool != null) Destroy(tool.gameObject);
            tool = null;
            toolName = null;
            Changed?.Invoke();
            if (say) Interface.Interface.Say("Alignment tool removed", true);
        }

        public void SetTool(float newX, float newY, float newHeading) {
            toolX = newX;
            toolY = newY;
            toolHeading = Mathf.Repeat(newHeading, 360f);
            Apply();
        }

        // ------------------------------------------------------------------ saved starts

        [Serializable] private class Start { public string name; public float x, y, heading; }
        [Serializable] private class StartList { public List<Start> starts = new(); }
        private string StartsKey => $"V2.Game.Starts.{definition.title}";

        private StartList LoadStarts() {
            try { return JsonUtility.FromJson<StartList>(PlayerPrefs.GetString(StartsKey, "")) ?? new StartList(); }
            catch (Exception) { return new StartList(); }
        }

        public List<string> StartNames() => LoadStarts().starts.Select(s => s.name).ToList();

        public void SaveStart(string name) {
            var list = LoadStarts();
            list.starts.RemoveAll(s => s.name == name);
            list.starts.Add(new Start { name = name, x = x, y = y, heading = heading });
            PlayerPrefs.SetString(StartsKey, JsonUtility.ToJson(list));
            Changed?.Invoke();
            Interface.Interface.Say($"Saved start \"{name}\"", true);
        }

        public void UseStart(string name) {
            var s = LoadStarts().starts.FirstOrDefault(v => v.name == name);
            if (s == null) return;
            SetPose(s.x, s.y, s.heading);
            Interface.Interface.Say($"Start \"{name}\": X {Number(s.x)}, Y {Number(s.y)}, {Number(s.heading)}°", true);
        }

        public void DeleteStart(string name) {
            var list = LoadStarts();
            list.starts.RemoveAll(s => s.name == name);
            PlayerPrefs.SetString(StartsKey, JsonUtility.ToJson(list));
            Changed?.Invoke();
            Interface.Interface.Say($"Deleted start \"{name}\"", true);
        }

        // ------------------------------------------------------------------ running

        public void StartRun() {
            if (state != State.Setup && state != State.Results) return;
            if (Sandbox.Running) Sandbox.Stop();
            field.ShowOriginals();
            if (!Legal) Interface.Interface.Say("Not a legal start (see the checklist): running it as practice", false);
            if (tool != null) tool.gameObject.SetActive(false);
            if (preloadGhost != null) preloadGhost.gameObject.SetActive(false);
            matchLoads.Clear();
            matchLoads["Red-Neutral"] = 3;   // RSC4: 3 red/yellow and 4 blue/yellow Pins, 7 Cups
            matchLoads["Blue-Neutral"] = 4;
            matchLoads["Cup"] = 7;
            Sandbox.Hosted = true;
            Sandbox.FloorHeight = robotFloor;
            Sandbox.DriveEnabled = false;
            Sandbox.BuildEnvironment = root => {
                (pins, cups, toggles) = field.MakePieces(root);
                if (preload) {
                    var stock = field.StockPin("Red-Neutral");
                    if (stock != null) {
                        var (p, r) = PreloadPose();
                        pins.Add(field.MakePin(stock.transform, root, p, r, false));
                    }
                }
            };
            Sandbox.Begin();
            if (!Sandbox.Running) {
                ClearHosting();
                field.ShowOriginals();
                if (tool != null) tool.gameObject.SetActive(true);
                PlacePreloadGhost();
                return;
            }
            Sandbox.Instance.Follow = true;
            state = State.Countdown;
            countdown = 3f;
            timeLeft = definition.skillsSeconds;
            score = new SkillsScore();
            GameUi.ShowHud(this);
            Audio.Sounds.Play(Audio.Sounds.Tick);
            Changed?.Invoke();
        }

        /// <summary>Ends the run now (the robot stops; the score is taken when everything is still).</summary>
        public void EndRun() {
            if (state != State.Running && state != State.Countdown) return;
            Sandbox.DriveEnabled = false;
            state = State.Ending;
            endingSince = Time.time;
            Interface.Interface.Say(timeLeft > 0.05f ? $"Run ended with {Mathf.CeilToInt(timeLeft)} s left" : "Time! The score counts once everything stops", true);
            Changed?.Invoke();
        }

        public void Retry() {
            StartRun();
        }

        /// <summary>Starts a run once a window that's closing has gone (the simulation won't start under an open window).</summary>
        public void StartRunNextFrame() => StartCoroutine(StartSoon());

        private System.Collections.IEnumerator StartSoon() {
            yield return null;
            yield return null;
            StartRun();
        }

        public void BackToSetup() {
            if (Sandbox.Running) Sandbox.Stop();
            ClearHosting();
            field.ShowOriginals();
            pins.Clear();
            cups.Clear();
            toggles.Clear();
            if (tool != null) tool.gameObject.SetActive(true);
            state = State.Setup;
            PlacePreloadGhost();
            GameUi.ShowSetup(this);
            FrameCamera();
            Changed?.Invoke();
            Interface.Interface.Say("Setup: set your start, then Start run", true);
        }

        /// <summary>Drops the next Match Load of a kind into the chosen red Loader.</summary>
        public void DropMatchLoad(string kind) {
            if (state != State.Running || !Sandbox.Running) {
                Interface.Interface.Say("Match Loads go in while the run is on", false);
                return;
            }
            if (!matchLoads.TryGetValue(kind, out var left) || left <= 0) {
                Interface.Interface.Say("No " + LoadName(kind) + " left", false);
                return;
            }
            var root = Sandbox.Instance.Root;
            // Into the Loader, on top of whatever is already in it (a drive team feeds it from above).
            var at = field.LoaderDrop(loader);
            var floor = field.HeightOf(at) - 15.5f;
            var stack = 0f;
            foreach (var body in root.GetComponentsInChildren<Rigidbody>())
                if (body.gameObject.layer == Sandbox.ElementLayer && Vector2.Distance(field.ToField(body.position), field.ToField(at)) < 2.6f)
                    stack = Mathf.Max(stack, field.HeightOf(body.position) + 3.3f);
            at = field.ToWorld(field.ToField(at), floor + Mathf.Max(stack, 0f) + 3.6f);
            var up = Quaternion.LookRotation(Vector3.up, field.transform.forward);
            if (kind == "Cup") {
                var stock = field.StockCup();
                if (stock == null) return;
                var cup = field.MakeCup(stock.transform, root, at, up, false);
                // Opaque half up, as the cups start on the field.
                if (Vector3.Dot(cup.transform.TransformDirection(cup.opaqueDirection), Vector3.up) < 0f)
                    cup.transform.rotation = Quaternion.AngleAxis(180f, field.transform.right) * cup.transform.rotation;
                cups.Add(cup);
            }
            else {
                var stock = field.StockPin(kind);
                if (stock == null) return;
                var pin = field.MakePin(stock.transform, root, at, up, false);
                // Its alliance color on top.
                var colored = pin.halves.FindIndex(h => h.color != PieceColor.Yellow);
                if (colored >= 0 && Vector3.Dot(pin.transform.TransformDirection(pin.halves[colored].direction), Vector3.up) < 0f)
                    pin.transform.rotation = Quaternion.AngleAxis(180f, field.transform.right) * pin.transform.rotation;
                pins.Add(pin);
            }
            matchLoads[kind] = left - 1;
            Audio.Sounds.Play(Audio.Sounds.Tick);
            Interface.Interface.Say($"Match Load: {LoadName(kind)} into the {(loader == 0 ? "first" : "second")} red Loader ({left - 1} left)", true);
            Changed?.Invoke();
        }

        public static string LoadName(string kind) => kind switch {
            "Red-Neutral" => "red/yellow pin", "Blue-Neutral" => "blue/yellow pin", _ => "cup",
        };

        private void Update() {
            var keyboard = Keyboard.current;
            var typing = InputEvents.TextInput.IsTyping || InputEvents.TextInput.ShortcutsPaused;
            switch (state) {
                case State.Setup:
                    DrawSetup();
                    if (keyboard != null && !typing) SetupKeys(keyboard);
                    break;
                case State.Countdown:
                    countdown -= Time.deltaTime;
                    if (countdown <= 0f) {
                        state = State.Running;
                        Sandbox.DriveEnabled = true;
                        Audio.Sounds.Play(Audio.Sounds.Tick);
                        Interface.Interface.Say("Go!", true);
                    }
                    if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !typing) BackToSetup();
                    break;
                case State.Running:
                    timeLeft -= Time.deltaTime;
                    if (timeLeft <= 0f) {
                        timeLeft = 0f;
                        EndRun();
                    }
                    else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !typing) EndRun();
                    break;
                case State.Ending:
                    // SC1: the score is taken when everything is still, or 5 seconds after the end.
                    if (Time.time - endingSince > 5f || (Time.time - endingSince > 0.5f && Still())) Finish();
                    break;
            }
            if ((state == State.Running || state == State.Ending || state == State.Countdown) && Time.time >= nextScore) {
                nextScore = Time.time + 0.25f;
                score = Evaluate();
                Changed?.Invoke();
            }
        }

        private bool Still() {
            foreach (var p in pins) if (p != null && p.body != null && !p.body.IsSleeping() && p.body.linearVelocity.magnitude > 0.5f) return false;
            foreach (var c in cups) if (c != null && c.body != null && !c.body.IsSleeping() && c.body.linearVelocity.magnitude > 0.5f) return false;
            return true;
        }

        private void Finish() {
            score = Evaluate();
            state = State.Results;
            Sandbox.DriveEnabled = false;
            var newBest = score.total > best;
            if (newBest) {
                best = score.total;
                PlayerPrefs.SetInt(BestKey, best);
            }
            GameUi.ShowResults(this, newBest);
            Audio.Sounds.Play(Audio.Sounds.Tick);
            Interface.Interface.Say($"Score: {score.total}" + (newBest ? " (new best)" : ""), true);
            Changed?.Invoke();
        }

        public SkillsScore Evaluate() => OverrideScoring.Evaluate(field, pins.Where(p => p != null).ToList(), cups.Where(c => c != null).ToList(),
            toggles.Where(t => t != null).ToList(), RobotInMidfield());

        // SC6: any part of the Robot in the Midfield's volume (the 34.05" diamond, as high as anything goes).
        private bool RobotInMidfield() {
            if (field == null) return false;
            var center = field.ToWorld(Vector2.zero, 30f);
            var rotation = field.transform.rotation * Quaternion.Euler(0f, 45f, 0f);
            return Physics.CheckBox(center, new Vector3(17.025f, 30f, 17.025f), rotation, 1 << Sandbox.Layer, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------ setup view

        private void SetupKeys(Keyboard k) {
            if (k.escapeKey.wasPressedThisFrame && !Popup.IsOpen && Dialog.OpenCount == 0) {
                Exit();
                return;
            }
            var step = k.shiftKey.isPressed ? 1f / 16f : 0.25f;
            float dx = 0f, dy = 0f, dh = 0f;
            if (k.leftArrowKey.wasPressedThisFrame) dx -= step;
            if (k.rightArrowKey.wasPressedThisFrame) dx += step;
            if (k.downArrowKey.wasPressedThisFrame) dy -= step;
            if (k.upArrowKey.wasPressedThisFrame) dy += step;
            if (k.qKey.wasPressedThisFrame) dh -= k.shiftKey.isPressed ? 0.5f : 5f;
            if (k.eKey.wasPressedThisFrame) dh += k.shiftKey.isPressed ? 0.5f : 5f;
            if (dx != 0f || dy != 0f || dh != 0f) Nudge(dx, dy, dh);
        }

        private void DrawSetup() {
            if (field == null) return;
            Overlay.Begin();
            var legal = Legal;
            var outline = legal ? Theme.Success : Theme.Danger;
            var corners = Corners();
            for (var i = 0; i < 4; i++) Overlay.Line(field.ToWorld(corners[i], 0.15f), field.ToWorld(corners[(i + 1) % 4], 0.15f), outline, 2.5f);
            // The front arrow.
            var c = new Vector2(x, y);
            var f = new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
            var tail = field.ToWorld(c, height + 1f);
            var tip = field.ToWorld(c + f * (depth / 2f + 4f), height + 1f);
            Overlay.Line(tail, tip, Theme.Dimension, 3f);
            if (Overlay.ToScreen(tip, out var t2) && Overlay.ToScreen(tail, out var t1)) Overlay.Arrowhead(t2, t1, Theme.Dimension, 16f, 12f);
            // The starting Quadrant (red) and the Midfield.
            var red = new Color(Theme.Danger.r, Theme.Danger.g, Theme.Danger.b, 0.85f);
            var a = new Vector2(-GameField.Half, -GameField.Half);
            var b = new Vector2(-GameField.Half, GameField.Half);
            var m = new Vector2(-GameField.MidfieldHalfDiagonal, 0f);
            Overlay.Line(field.ToWorld(a, 0.1f), field.ToWorld(m, 0.1f), red, 2f);
            Overlay.Line(field.ToWorld(b, 0.1f), field.ToWorld(m, 0.1f), red, 2f);
        }

        public void FrameCamera() {
            if (PivotCamera.Main == null || field == null) return;
            var behind = field.Direction(heading);
            var yaw = Mathf.Atan2(behind.x, behind.z) * Mathf.Rad2Deg;
            PivotCamera.Main.SetTransform(field.ToWorld(new Vector2(x, y) + new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad)) * 30f, 2f),
                new Vector3(38f, yaw, 0f), -120f);
        }
    }
}
