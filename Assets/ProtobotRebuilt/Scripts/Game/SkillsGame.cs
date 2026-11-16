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
    public partial class SkillsGame : MonoBehaviour {
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

        // V2: Robot Skills, or a match (practice on your own for now; online matches later): each has its own start
        // (Skills: the Left Quadrant by the red Alliance Station, RSC; a match: one Quadrant on your Alliance's side of
        // the Autonomous Line, SG1), preload (SG5: one Pin of your Alliance's color touching the robot) and timing
        // (a match: 0:15 autonomous, then 1:45 driver control).
        // V2 (Phase 9): Programming Skills = a skills run (same start, Match Loads, 1:00) driven only by the robot's program.
        public enum Mode { Skills, Match, Programming }
        public enum PreloadSpot { Front, Back, Left, Right, Top }
        public Mode mode = Mode.Skills;
        public bool redAlliance = true;
        public Quadrant startQuadrant = Quadrant.Left;
        public PreloadSpot preloadSpot = PreloadSpot.Front;
        public bool skipAutonomous = true;
        public const float AutonomousSeconds = 15f, DriverSeconds = 105f;
        public bool InAutonomous => mode == Mode.Match && !skipAutonomous && timeLeft > DriverSeconds && (state == State.Running || state == State.Countdown);
        /// <summary>Driving or Programming Skills (a Robot Skills run: the skills start, field and Match Loads).</summary>
        public bool SkillsRun => mode != Mode.Match;
        public float RunSeconds => SkillsRun ? definition.skillsSeconds : DriverSeconds + (skipAutonomous ? 0f : AutonomousSeconds);
        public string ModeName => mode == Mode.Skills ? "Driving Skills" : mode == Mode.Programming ? "Programming Skills" : "Match practice";
        public Quadrant StartQuadrant => SkillsRun ? Quadrant.Left : startQuadrant;
        public bool Red => SkillsRun || redAlliance;
        public string PreloadColors => Red ? "Red-Neutral" : "Blue-Neutral";
        private string ghostColors;

        // The alignment tool (a build file's parts, fixed to the field until the run starts).
        public Transform tool;
        public string toolName;
        public float toolX, toolY, toolHeading;

        // The run.
        public float timeLeft, countdown;
        private bool ranCode;   // V2 (Phase 9): the robot's program ran this run (a Programming Skills run only posts then)
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

        // V2: an online match (Online/OnlineMatch) runs this game on every computer: the host simulates, the others show it.
        public bool online, onlineClient, onlineMayStart;
        /// <summary>Online: Ready pressed (the start is sent; it can't change any more), and what Ready does.</summary>
        public bool onlineLocked;
        public Action onlineReady;
        /// <summary>Online: Robots in the Midfield now, per Alliance (match scoring).</summary>
        public Func<(int red, int blue)> onlineMidfield;
        /// <summary>Online: a Match Load asked for (the host drops it); true when it was sent.</summary>
        public Func<string, bool> onlineDrop;
        /// <summary>The score is about to be taken (the host sends the final positions first).</summary>
        public event Action Finishing;
        /// <summary>The game was left (Exit).</summary>
        public event Action Exited;
        public List<Pin> Pins => pins;
        public List<Cup> Cups => cups;
        public List<Toggle> Toggles => toggles;
        /// <summary>Online: the host said the run is over: take the score now (the positions are the host's).</summary>
        public void FinishNow() {
            if (state == State.Running || state == State.Countdown) EndRun();
            if (state == State.Ending) Finish();
        }

        // ------------------------------------------------------------------ opening and closing

        public static void Open(GameDefinition definition, bool lightField, Mode mode = Mode.Skills) {
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
            game.mode = mode;
            try {
                game.MeasureRobot(parts);
                game.field = GameField.Create(definition, lightField, mode != Mode.Match);
            }
            catch (Exception e) {
                Debug.LogException(e);
                Current = null;
                if (game.field != null) Destroy(game.field.gameObject);
                Destroy(go);
                Interface.Interface.Say("The field couldn't be set up: " + e.Message, false);
                return;
            }
            game.PauseHoleTriggers();   // V2 (performance)
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
            game.setupTools = new SetupTools(game);   // V2: drag, Line up, Measure
            game.DefaultStart();
            game.state = State.Setup;
            GameUi.ShowSetup(game);
            game.FrameCamera();
            Interface.Interface.Say($"{definition.Name}: {game.ModeName}. Set your start, then Start run", true);
        }

        public void Exit() {
            Exited?.Invoke();
            if (Sandbox.Running) Sandbox.Stop();
            ResumeHoleTriggers();
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

        // V2 (performance): the build can't change while a game is open, so the screws' hole-finding triggers rest
        // (their OnTriggerStay ran for every screw every physics step: ~1 ms a frame on the demo robot). Switching a
        // trigger off sends no exit: every screw keeps its holes. (The sandbox does the same while it runs.)
        private readonly List<Collider> pausedTriggers = new();

        private readonly List<HoleDetector> pausedDetectors = new();

        private void PauseHoleTriggers() {
            foreach (var detector in FindObjectsByType<HoleDetector>(FindObjectsSortMode.None)) {
                if (detector.enabled) {   // (and their Update: 0.5 ms a frame on the demo robot)
                    detector.enabled = false;
                    pausedDetectors.Add(detector);
                }
                foreach (var trigger in detector.GetComponents<Collider>())
                    if (trigger.enabled && trigger.isTrigger) {
                        trigger.enabled = false;
                        pausedTriggers.Add(trigger);
                    }
            }
        }

        private void ResumeHoleTriggers() {
            foreach (var detector in pausedDetectors)
                if (detector != null) detector.enabled = true;
            pausedDetectors.Clear();
            foreach (var trigger in pausedTriggers)
                if (trigger != null) trigger.enabled = true;
            pausedTriggers.Clear();
        }

        private void OnDestroy() {
            ResumeHoleTriggers();
            ShowGrid();
            if (Current == this) {
                ClearHosting();
                Current = null;
            }
        }

        private string BestKey => $"V2.Game.Best.{definition.title}.{buildName}" + (mode == Mode.Match ? ".Match" : mode == Mode.Programming ? ".Programming" : "");
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
            // Worked out in the Left Quadrant, then turned round the field's middle into the one used.
            var q = StartQuadrant;
            // Beside the Toggle in the middle of the wall (SG1 must not touch it; the manual's Figure SG-1 shows each robot
            // just to its side), turned round the field like the other Quadrants.
            var beside = ToggleHalf + width / 2f + 0.5f;
            var at = FromLeft(q, new Vector2(-GameField.Half + depth / 2f + 0.02f, beside));
            var facing = FromLeft(q, Vector2.right);
            heading = Mathf.Repeat(Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg, 360f);
            x = at.x;
            y = at.y;
            var toolAt = FromLeft(q, new Vector2(-GameField.Half + 0.02f, beside));
            toolX = toolAt.x;
            toolY = toolAt.y;
            toolHeading = heading;
            SnapToWall();
            ClearOfPieces();
            Apply();
        }

        // V2: a deep, wide or tall robot beside the Toggle can start on top of the cups and pins standing by the wall, or
        // touch a Goal or the Toggle: the default start looks along the wall (nearest first), then turned sideways, for
        // the first spot that's legal and clear of the pieces (else one that's at least clear of them).
        private void ClearOfPieces() {
            if (field == null) return;
            var spots = field.PieceSpots();
            bool Clear() {
                var corners = Corners();
                foreach (var (at, reach) in spots)
                    if (Inside(corners, at) || EdgeDistance(corners, at) < reach + 0.25f) return false;
                return true;
            }
            if (Clear() && Legal) return;
            var (keepX, keepY, keepHeading) = (x, y, heading);
            var from = Slide;
            (float x, float y, float h)? fallback = null;
            foreach (var turn in new[] { 0f, 90f, -90f }) {
                heading = HeadingOf(FromLeft(StartQuadrant, Dir(90f + turn)));
                foreach (var step in new[] { 0f }.Concat(Enumerable.Range(1, 120).SelectMany(i => new[] { i * 0.5f, -i * 0.5f }))) {
                    MoveAlong(from + step);
                    if (Mathf.Abs(Slide - (from + step)) > 0.01f || !Clear()) continue;
                    if (Legal) return;
                    fallback ??= (x, y, heading);
                }
            }
            (x, y, heading) = fallback ?? (keepX, keepY, keepHeading);
            SnapToWall();
        }

        private static bool Inside(Vector2[] polygon, Vector2 p) {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if ((polygon[i].y > p.y) != (polygon[j].y > p.y) && p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                    inside = !inside;
            return inside;
        }

        private static float EdgeDistance(Vector2[] polygon, Vector2 p) {
            var best = float.MaxValue;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++) {
                var a = polygon[j];
                var ab = polygon[i] - a;
                var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        // V2: the four Quadrants are the Left one turned round the field's middle (Left: x < -|y|, by the red Alliance
        // Station; Bottom: y < -|x|, by the audience; Right; Top).
        public static Vector2 ToLeft(Quadrant q, Vector2 p) => q switch {
            Quadrant.Bottom => new Vector2(p.y, -p.x),
            Quadrant.Right => -p,
            Quadrant.Top => new Vector2(-p.y, p.x),
            _ => p,
        };

        public static Vector2 FromLeft(Quadrant q, Vector2 p) => q switch {
            Quadrant.Bottom => new Vector2(-p.y, p.x),
            Quadrant.Right => -p,
            Quadrant.Top => new Vector2(p.y, -p.x),
            _ => p,
        };

        public static string QuadrantName(Quadrant q) => q.ToString();

        /// <summary>Half the Toggle's length along the wall (it sits in the wall's middle), with a little room.</summary>
        public const float ToggleHalf = 14f;

        /// <summary>A match: the Alliance (its first Quadrant: Left for red, Right for blue).</summary>
        public void SetAlliance(bool red) {
            redAlliance = red;
            startQuadrant = red ? Quadrant.Left : Quadrant.Right;
            DefaultStart();
        }

        public void SetQuadrant(Quadrant q) {
            startQuadrant = q;
            DefaultStart();
        }

        public void SetPreloadSpot(PreloadSpot spot) {
            preloadSpot = spot;
            preload = true;
            PlacePreloadGhost();
            Changed?.Invoke();
        }

        public void SetSkipAutonomous(bool skip) {
            skipAutonomous = skip;
            Changed?.Invoke();
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
            SnapToWall();   // V2: always against its wall (SG1)
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
            // V2: the Quadrant and wall checks in the start Quadrant's own frame (turned to look like the Left one).
            var q = StartQuadrant;
            var local = edges.Select(p => ToLeft(q, p)).ToList();
            list.Add((SkillsRun ? "In the Quadrant by the red Alliance Station" : $"In the {QuadrantName(q)} Quadrant ({(Red ? "red" : "blue")} side of the Autonomous Line)",
                local.All(p => p.x <= -Mathf.Abs(p.y) + Slack && p.x >= -GameField.Half - Slack)));
            list.Add(("Touching the Field Perimeter", local.Any(p => p.x <= -GameField.Half + 0.1f)));
            list.Add(("Not in a Load Zone", !edges.Any(GameField.InLoadZone)));
            list.Add(("Not touching a Goal", !field.goals.Any(g => Overlaps(corners, g.position, 5.95f))));
            list.Add(("Clear of the Toggle", height < 11.5f || !local.Any(p => p.x <= -GameField.Half + 1.5f && Mathf.Abs(p.y) < ToggleHalf)));
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
            if (preloadGhost != null && ghostColors != PreloadColors) {
                Destroy(preloadGhost.gameObject);
                preloadGhost = null;
            }
            if (preloadGhost == null && field != null) {
                ghostColors = PreloadColors;
                var stock = field.StockPin(PreloadColors);
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

        // The Preload: standing on the tiles against the robot's front (or back or a side), alliance-colored half up;
        // or lying on top of the robot. SG5: it touches the robot.
        private (Vector3, Quaternion) PreloadPose() {
            var f = field.Direction(heading);
            var side = Vector3.Cross(Vector3.up, f).normalized;
            var center = new Vector3(robotCenter.x, robotFloor, robotCenter.z);
            var standing = Quaternion.LookRotation(Vector3.up, f);
            return preloadSpot switch {
                PreloadSpot.Back => (center - f * (depth / 2f + 1.65f) + Vector3.up * 3.3f, standing),
                PreloadSpot.Left => (center - side * (width / 2f + 1.65f) + Vector3.up * 3.3f, standing),
                PreloadSpot.Right => (center + side * (width / 2f + 1.65f) + Vector3.up * 3.3f, standing),
                PreloadSpot.Top => (center + Vector3.up * (height + 1.7f), Quaternion.LookRotation(side, Vector3.up)),
                _ => (center + f * (depth / 2f + 1.65f) + Vector3.up * 3.3f, standing),
            };
        }

        public void SetPreload(bool on) {
            preload = on;
            PlacePreloadGhost();
            Interface.Interface.Say(on ? "Preload: a " + LoadName(PreloadColors) + " touching the robot" : "No preload on the robot", true);
            Changed?.Invoke();
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
            Builds.BuildData data;
            try {
                data = Builds.BuildFile.Read(path);
            }
            catch (Exception e) {
                Interface.Interface.Say("That file couldn't be read: " + e.Message, false);
                return;
            }
            if (data?.parts == null || data.parts.Length == 0) {
                Interface.Interface.Say("That build has no parts", false);
                return;
            }
            LoadToolParts(data.parts, System.IO.Path.GetFileNameWithoutExtension(path));
        }

        /// <summary>V2: the alignment tool from parts (a build file's or a saved group's).</summary>
        public void LoadToolParts(Builds.ObjectData[] parts, string name) {
            RemoveTool(false);
            tool = new GameObject("Alignment Tool").transform;
            tool.SetParent(field.transform, false);
            // The parts are copied where the build has them (the holder at the world's origin), then the holder goes
            // into the tool: the tool's own axes are its build's (heading 0 = the build's +Z along the field's +Y).
            var holder = new GameObject("Parts").transform;
            var made = 0;
            foreach (var part in parts) {
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
                        // V2: its exact shape on the Default layer, so Line up and Measure can pick its faces and corners.
                        copy.AddComponent<MeshCollider>().sharedMesh = mesh;
                        copy.AddComponent<PickablePart>();
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
            toolName = name;
            // V2: beside the robot against its wall, turned the same way (behind it would be outside the field).
            toolSize = new Vector2(bounds.size.x, bounds.size.z);
            setupTools.toolSelected = true;
            ToolBesideRobot();
            Interface.Interface.Say($"Alignment tool \"{toolName}\" is beside your robot: drag it where you want it, then Line up the robot against it. It's taken away when the run starts", true);
        }

        public void RemoveTool(bool say = true) {
            if (tool != null) Destroy(tool.gameObject);
            tool = null;
            toolName = null;
            Changed?.Invoke();
            if (say) Interface.Interface.Say("Alignment tool removed", true);
        }

        public void SetTool(float newX, float newY, float newHeading) => PlaceTool(newX, newY, newHeading, 0f);

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
            if (online && !onlineMayStart) return;   // V2: the host starts an online match
            if (Sandbox.Running) Sandbox.Stop();
            field.ShowOriginals();
            if (!Legal) Interface.Interface.Say("Not a legal start (see the checklist): running it as practice", false);
            if (tool != null) tool.gameObject.SetActive(false);
            if (preloadGhost != null) preloadGhost.gameObject.SetActive(false);
            matchLoads.Clear();
            if (SkillsRun) {
                matchLoads["Red-Neutral"] = 3;   // RSC4: 3 red/yellow and 4 blue/yellow Pins, 7 Cups
                matchLoads["Blue-Neutral"] = 4;
                matchLoads["Cup"] = 7;
            }
            else {
                // V2: a match (Figure FO-2): each Alliance Station has 12 alliance/yellow Pins, 1 yellow/yellow Pin and
                // 10 Cups, through its own Loaders.
                matchLoads[PreloadColors] = 12;
                if (field.StockPin("Neutral-Neutral") != null) matchLoads["Neutral-Neutral"] = 1;
                matchLoads["Cup"] = 10;
            }
            Sandbox.Hosted = true;
            Sandbox.FloorHeight = robotFloor;
            Sandbox.DriveEnabled = false;
            Sandbox.BuildEnvironment = root => {
                (pins, cups, toggles) = field.MakePieces(root);
                if (preload && !online) {   // (online, the host makes every robot's Preload)
                    var stock = field.StockPin(PreloadColors);
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
            if (!online) ReplayRecorder.Begin(this);   // V2: the replay
            state = State.Countdown;
            countdown = 3f;
            ranCode = false;
            timeLeft = RunSeconds;
            score = new SkillsScore();
            GameUi.ShowHud(this);
            Audio.Sounds.Play(Audio.Sounds.Tick);
            Changed?.Invoke();
        }

        /// <summary>V2 (Phase 9): runs the robot's program for a period; false when it has none (or can't start: the
        /// mistake shows in the code window).</summary>
        private bool StartCode(RobotCode.RobotProgram.Period period, float seconds) {
            var source = RobotCode.CodeStore.Source;
            if (string.IsNullOrWhiteSpace(source)) return false;
            if (RobotCode.RobotProgram.Start(source, period, seconds, out var error, out _)) return true;
            RobotCode.CodePanel.Open();
            Interface.Interface.Say("The robot's program has a mistake: " + error, false);
            return false;
        }

        /// <summary>Ends the run now (the robot stops; the score is taken when everything is still).</summary>
        public void EndRun() {
            if (state != State.Running && state != State.Countdown) return;
            Sandbox.DriveEnabled = false;
            RobotCode.RobotProgram.Stop(null);   // V2 (Phase 9): the robot's program stops with the match
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
            if (onlineDrop != null) {   // V2: online, the host drops it (into this player's Alliance's Loader)
                if (onlineDrop(kind)) {
                    matchLoads[kind] = left - 1;
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                    Changed?.Invoke();
                }
                return;
            }
            SpawnMatchLoad(kind, Red, loader);
            matchLoads[kind] = left - 1;
            Audio.Sounds.Play(Audio.Sounds.Tick);
            Interface.Interface.Say($"Match Load: {LoadName(kind)} into the {(loader == 0 ? "first" : "second")} {(Red ? "red" : "blue")} Loader ({left - 1} left)", true);
            Changed?.Invoke();
        }

        /// <summary>V2: a Match Load of a kind into an Alliance's Loader (on top of what's in it); the piece made.</summary>
        public Component SpawnMatchLoad(string kind, bool red, int loaderIndex) {
            var root = Sandbox.Instance.Root;
            // Into the Loader, on top of whatever is already in it (a drive team feeds it from above).
            var at = field.LoaderDrop(loaderIndex, red);
            var floor = field.HeightOf(at) - 15.5f;
            var stack = 0f;
            foreach (var body in root.GetComponentsInChildren<Rigidbody>())
                if (body.gameObject.layer == Sandbox.ElementLayer && Vector2.Distance(field.ToField(body.position), field.ToField(at)) < 2.6f)
                    stack = Mathf.Max(stack, field.HeightOf(body.position) + 3.3f);
            at = field.ToWorld(field.ToField(at), floor + Mathf.Max(stack, 0f) + 3.6f);
            var up = Quaternion.LookRotation(Vector3.up, field.transform.forward);
            if (kind == "Cup") {
                var stock = field.StockCup();
                if (stock == null) return null;
                var cup = field.MakeCup(stock.transform, root, at, up, false);
                // Opaque half up, as the cups start on the field.
                if (Vector3.Dot(cup.transform.TransformDirection(cup.opaqueDirection), Vector3.up) < 0f)
                    cup.transform.rotation = Quaternion.AngleAxis(180f, field.transform.right) * cup.transform.rotation;
                cups.Add(cup);
                return cup;
            }
            else {
                var stock = field.StockPin(kind);
                if (stock == null) return null;
                var pin = field.MakePin(stock.transform, root, at, up, false);
                // Its alliance color on top.
                var colored = pin.halves.FindIndex(h => h.color != PieceColor.Yellow);
                if (colored >= 0 && Vector3.Dot(pin.transform.TransformDirection(pin.halves[colored].direction), Vector3.up) < 0f)
                    pin.transform.rotation = Quaternion.AngleAxis(180f, field.transform.right) * pin.transform.rotation;
                pins.Add(pin);
                return pin;
            }
        }

        public static string LoadName(string kind) => kind switch {
            "Red-Neutral" => "red/yellow pin", "Blue-Neutral" => "blue/yellow pin", "Neutral-Neutral" => "yellow/yellow pin", _ => "cup",
        };

        private void Update() {
            if (ReplayViewer.Active) return;   // V2: a replay plays on this field
            var keyboard = Keyboard.current;
            var typing = InputEvents.TextInput.IsTyping || InputEvents.TextInput.ShortcutsPaused;
            switch (state) {
                case State.Setup:
                    if (online && onlineLocked) break;   // V2: online, the start can't change after Ready
                    DrawSetup();
                    if (keyboard != null && !typing) SetupKeys(keyboard);
                    break;
                case State.Countdown:
                    countdown -= Time.deltaTime;
                    if (countdown <= 0f) {
                        state = State.Running;
                        // V2: a match's autonomous period: no driving; the robot's own program runs (Phase 9).
                        Audio.Sounds.Play(Audio.Sounds.Tick);
                        if (mode == Mode.Programming) {
                            // V2 (Phase 9): Programming Skills: the whole minute is the robot's program (its autonomous
                            // when it's a competition program, as a field controller runs it); the keys don't drive.
                            Sandbox.DriveEnabled = false;
                            ranCode = StartCode(RobotCode.RobotProgram.Period.Autonomous, RunSeconds);
                            if (ranCode) Interface.Interface.Say("Go! The robot's program runs", true);
                            else if (string.IsNullOrWhiteSpace(RobotCode.CodeStore.Source)) Interface.Interface.Say("The robot has no program: it waits (Code in the top bar)", false);
                            break;
                        }
                        Sandbox.DriveEnabled = !InAutonomous;
                        var runsCode = InAutonomous && !online && StartCode(RobotCode.RobotProgram.Period.Autonomous, AutonomousSeconds);
                        Interface.Interface.Say(!InAutonomous ? "Go!" : runsCode ? "Autonomous: 0:15 (the robot's program runs)" : "Autonomous: 0:15 (the robot has no program: it waits; Code in the top bar)", true);
                    }
                    if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !typing && !online) BackToSetup();
                    break;
                case State.Running:
                    var wasAutonomous = InAutonomous;
                    timeLeft -= Time.deltaTime;
                    if (wasAutonomous && !InAutonomous && timeLeft > 0f) {
                        Sandbox.DriveEnabled = true;
                        // V2 (Phase 9): its program's driver control (a Competition's), else the keys drive.
                        RobotCode.RobotProgram.Stop(null);
                        if (!online && RobotCode.CodeStore.Source.Contains("Competition(")) StartCode(RobotCode.RobotProgram.Period.Driver, DriverSeconds);
                        Audio.Sounds.Play(Audio.Sounds.Tick);
                        Interface.Interface.Say("Driver control: 1:45", true);
                        Changed?.Invoke();
                    }
                    if (timeLeft <= 0f) {
                        timeLeft = 0f;
                        EndRun();
                    }
                    else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !typing && !online) EndRun();
                    break;
                case State.Ending:
                    // SC1: the score is taken when everything is still, or 5 seconds after the end.
                    // V2: online, the host decides (its pieces are the real ones) and tells the others (FinishNow).
                    if (!onlineClient && (Time.time - endingSince > 5f || (Time.time - endingSince > 0.5f && Still()))) Finish();
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
            Finishing?.Invoke();   // V2: online, the host sends the final positions first
            if (onlineClient) Physics.SyncTransforms();
            score = Evaluate();
            state = State.Results;
            Sandbox.DriveEnabled = false;
            var newBest = score.total > best;
            if (newBest) {
                best = score.total;
                PlayerPrefs.SetInt(BestKey, best);
            }
            // V2: the score reveal (camera round the field, the points flying into the total, then the breakdown).
            if (score.robotInMidfield) score.items.Add(new ScoredItem { point = RobotPoint(), points = OverrideScoring.Midfield, kind = "Robot in the Midfield", where = "Midfield" });
            // V2: the run's replay (kept for Watch replay and saved in Replays/).
            var recorder = GetComponent<ReplayRecorder>();
            if (recorder != null)
                try {
                    Replays.Last = recorder.Finish(score);
                    Replays.Save(Replays.Last);
                }
                catch (Exception e) { Debug.LogWarning("V2: the replay couldn't be saved: " + e.Message); }
            ScoreReveal.Play(this, newBest);
            Builds.ActionLog.Write("game", $"score {score.total}" + (newBest ? " (new best)" : ""));
            Post();   // V2: the leaderboard and stats
            Changed?.Invoke();
        }

        // V2: a legal skills run goes to the skills leaderboard (checked by Cloud Code first); an online match's result
        // into this player's stats.
        private async void Post() {
            try {
                if (online && score.match) {
                    Online.Accounts.RecordMatch(Red ? score.red : score.blue, Red ? score.blue : score.red);
                    return;
                }
                if (!SkillsRun || online || !Legal) return;
                if (mode == Mode.Programming && !ranCode) return;   // no program: nothing to post
                var items = score.items.Select(i => (i.kind, i.points)).ToList();
                var replay = Replays.Last;
                var programming = mode == Mode.Programming;
                var message = await Online.Accounts.SubmitSkills(score.total, items, new Vector3(depth, width, height), RunSeconds, buildName, programming);
                if (message != null) Interface.Interface.Announce(message);
                // V2: the replay of the player's best run goes with it (others can watch it from the leaderboard).
                if (replay != null && replay.score == score.total) await Online.Accounts.SaveReplay(replay, programming);
            }
            catch (Exception e) { Debug.LogWarning("V2: posting the result: " + e.Message); }
        }

        public SkillsScore Evaluate() {
            if (online && onlineMidfield != null) {   // V2: a match: each Alliance's score
                var (red, blue) = onlineMidfield();
                return OverrideScoring.Evaluate(field, pins.Where(p => p != null).ToList(), cups.Where(c => c != null).ToList(),
                    toggles.Where(t => t != null).ToList(), false, true, red, blue, Red);
            }
            return OverrideScoring.Evaluate(field, pins.Where(p => p != null).ToList(), cups.Where(c => c != null).ToList(),
                toggles.Where(t => t != null).ToList(), RobotInMidfield());
        }

        // V2: the middle of the robot's bodies in the simulation (where the Midfield points rise from).
        private Vector3 RobotPoint() {
            var hits = Physics.OverlapBox(field.ToWorld(Vector2.zero, 30f), new Vector3(17.025f, 30f, 17.025f), field.transform.rotation * Quaternion.Euler(0f, 45f, 0f),
                1 << Sandbox.Layer, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return field.ToWorld(Vector2.zero, 6f);
            var b = hits[0].bounds;
            foreach (var h in hits) b.Encapsulate(h.bounds);
            return b.center + Vector3.up * b.extents.y;
        }

        // SC6: any part of the Robot in the Midfield's volume (the 34.05" diamond, as high as anything goes).
        private bool RobotInMidfield() {
            if (field == null) return false;
            var center = field.ToWorld(Vector2.zero, 30f);
            var rotation = field.transform.rotation * Quaternion.Euler(0f, 45f, 0f);
            return Physics.CheckBox(center, new Vector3(17.025f, 30f, 17.025f), rotation, 1 << Sandbox.Layer, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------ setup view

        private void SetupKeys(Keyboard k) {
            if (k.escapeKey.wasPressedThisFrame && !Popup.IsOpen && Dialog.OpenCount == 0 && !setupTools.UsedEscape && !online) {
                Exit();
                return;
            }
            var step = k.shiftKey.isPressed ? 1f / 16f : 0.25f;
            float dx = 0f, dy = 0f, dh = 0f;
            // V2: keys from Edit > Keyboard shortcuts. Arrows go the way they point on screen.
            if (Tools.Keybinds.Pressed("NudgeLeft")) dx -= 1f;
            if (Tools.Keybinds.Pressed("NudgeRight")) dx += 1f;
            if (Tools.Keybinds.Pressed("NudgeBack")) dy -= 1f;
            if (Tools.Keybinds.Pressed("NudgeAway")) dy += 1f;
            if (Tools.Keybinds.Pressed("TurnLeft")) dh -= k.shiftKey.isPressed ? 0.5f : 5f;
            if (Tools.Keybinds.Pressed("TurnRight")) dh += k.shiftKey.isPressed ? 0.5f : 5f;
            if (dx == 0f && dy == 0f && dh == 0f) return;
            var move = Vector2.zero;
            var camera = PivotCamera.Main != null ? PivotCamera.Main.camera.transform : null;
            if (camera != null) {
                var right = field.transform.InverseTransformDirection(camera.right);
                var ahead = field.transform.InverseTransformDirection(Vector3.ProjectOnPlane(camera.forward, field.transform.up));
                move = new Vector2(right.x, right.z).normalized * dx + new Vector2(ahead.x, ahead.z).normalized * dy;
            }
            PushUndo();
            if (setupTools.toolSelected && tool != null) {
                // The tool along the field's axis nearest the arrow's way.
                var snapped = move == Vector2.zero ? Vector2.zero
                    : Mathf.Abs(move.x) >= Mathf.Abs(move.y) ? new Vector2(Mathf.Sign(move.x), 0f) : new Vector2(0f, Mathf.Sign(move.y));
                PlaceTool(toolX + snapped.x * step, toolY + snapped.y * step, toolHeading + dh, 0f);
                return;
            }
            // The robot only slides along its wall: the arrow that points most along it moves it.
            var along = Vector2.Dot(move, WallTangent);
            if (Mathf.Abs(along) > 0.3f) SlideBy(Mathf.Sign(along) * step);
            if (dh != 0f) TurnBy(dh);
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
            // V2: where the robot may start: its Quadrant (in its Alliance's color, lightly filled), labelled.
            var q = StartQuadrant;
            var zone = Red ? new Color(0.95f, 0.3f, 0.3f, 1f) : new Color(0.3f, 0.58f, 1f, 1f);
            var triangle = new[] {
                FromLeft(q, new Vector2(-GameField.Half, -GameField.Half)), FromLeft(q, new Vector2(-GameField.Half, GameField.Half)),
                Vector2.zero,   // the Quadrant runs along the tape diagonals, corner to the field's middle
            };
            var screen = new List<Vector2>();
            foreach (var p in triangle)
                if (Overlay.ToScreen(field.ToWorld(p, 0.1f), out var s)) screen.Add(s);
            if (screen.Count == 3) Overlay.Fan(screen, new Color(zone.r, zone.g, zone.b, 0.12f));
            for (var i = 0; i < 3; i++) Overlay.Line(field.ToWorld(triangle[i], 0.1f), field.ToWorld(triangle[(i + 1) % 3], 0.1f), new Color(zone.r, zone.g, zone.b, 0.9f), 2.5f);
            var middle = Vector2.Lerp((triangle[0] + triangle[1]) / 2f, triangle[2], 0.45f);   // toward the tip, clear of the robot
            if (Overlay.ToScreen(field.ToWorld(middle, 0.1f), out var label))
                Overlay.Text(label, SkillsRun ? "Start zone (Robot Skills)" : $"Start zone ({QuadrantName(q)} Quadrant)", zone);
            // A match: the Autonomous Line (corner to corner), dashed.
            if (mode == Mode.Match) Dashed(new Vector2(-GameField.Half, GameField.Half), new Vector2(GameField.Half, -GameField.Half), new Color(1f, 1f, 1f, 0.75f), 0.12f, 2f);
            // Where the Preload may be: touching the robot (a band a Pin wide round its footprint), dashed yellow.
            if (preload) {
                var band = 3.3f;
                var grown = Grown(corners, band);
                for (var i = 0; i < 4; i++) Dashed(grown[i], grown[(i + 1) % 4], new Color(Theme.Warning.r, Theme.Warning.g, Theme.Warning.b, 0.9f), 0.14f, 2f);
                var at = (grown[0] + grown[1]) / 2f;
                if (Overlay.ToScreen(field.ToWorld(at, 0.14f), out var preloadLabel))
                    Overlay.Text(preloadLabel + new Vector2(0f, 18f), "Preload: touching the robot", Theme.Warning);
            }
            setupTools?.Update();   // V2: drag, Line up, Measure
        }

        // The footprint grown outward by `by` inches on every side.
        private Vector2[] Grown(Vector2[] corners, float by) {
            var c = (corners[0] + corners[2]) / 2f;
            var f = new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
            var r = new Vector2(f.y, -f.x);
            var d = depth / 2f + by;
            var w = width / 2f + by;
            return new[] { c + f * d + r * w, c + f * d - r * w, c - f * d - r * w, c - f * d + r * w };
        }

        private void Dashed(Vector2 a, Vector2 b, Color color, float height, float width) {
            var length = Vector2.Distance(a, b);
            const float Dash = 3f, Gap = 2f;
            for (var t = 0f; t < length; t += Dash + Gap) {
                var p = Vector2.Lerp(a, b, t / length);
                var q = Vector2.Lerp(a, b, Mathf.Min(length, t + Dash) / length);
                Overlay.Line(field.ToWorld(p, height), field.ToWorld(q, height), color, width);
            }
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
