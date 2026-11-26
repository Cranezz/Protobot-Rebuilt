using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Protobot.RobotCode {
    /// <summary>
    /// V2 (build 79): a path planner on the field, as teams use path.jerryio for LemLib: click waypoints on a top view of
    /// the field, drag them, set each one's speed; the path is a smooth curve through them, sampled every 2". It writes the
    /// path for the program's library: a LemLib path file (static/NAME.txt, "x, y, speed" lines and endData, read by
    /// ASSET + follow) or a point list (Python's motion.follow, EZ-Template's pid_odom_set), or one move per waypoint.
    /// Coordinates are the game's field frame: inches from the middle, +X toward the blue side, +Y away from the
    /// audience, heading clockwise from +Y (what odometry reports after set_pose). The waypoints are kept in the path
    /// file ("#PROTOBOT-PATH" after endData), so a planned path opens again; the last one is kept between sessions.
    /// After a run, the robot's real path (and where its odometry thought it was) is drawn over the plan.
    /// </summary>
    public class PathPlanner : MonoBehaviour {
        private const float MapInches = 152f;     // the map picture's width (Override Map.png: 152" square)
        private const float FieldHalf = 70.2f;    // inside the walls
        private const string LastKey = "V2.PathPlanner.Last", MetaTag = "#PROTOBOT-PATH ";

        [Serializable] private class Point { public float x, y, speed = 100f; }

        [Serializable]
        private class Plan {
            public string name = "path1";
            public List<Point> points = new();
            public float heading;          // the robot's at the first point (clockwise from +Y)
            public float lookahead = 15f;
            public int timeout = 5000;
            public bool backwards;
            public int format;             // 0 follow (pure pursuit), 1 one move per point
        }

        private static PathPlanner open;
        private Dialog dialog;
        private Plan plan;
        private RectTransform map;
        private ShapeGraphic shapes;
        private readonly List<TextMeshProUGUI> numbers = new();
        private RectTransform list;
        private TextMeshProUGUI preview, summary;
        private int selected = -1, dragging = -1;
        private bool listDirty = true;

        public static bool IsOpen => open != null;

        public static void Open() {
            if (open != null) return;
            var dialog = Dialog.Open("Path planner", new Vector2(1200f, 790f));
            open = dialog.gameObject.AddComponent<PathPlanner>();
            open.dialog = dialog;
            open.plan = Load();
            open.Build();
        }

        // ------------------------------------------------------------------ saving

        private static Plan Load() {
            try {
                var json = PlayerPrefs.GetString(LastKey, "");
                if (json.Length > 0) {
                    var p = JsonUtility.FromJson<Plan>(json);
                    if (p != null && p.points != null) return p;
                }
            }
            catch (Exception) { /* a new one */ }
            return new Plan();
        }

        private void Keep() => PlayerPrefs.SetString(LastKey, JsonUtility.ToJson(plan));

        // ------------------------------------------------------------------ the window

        private void Build() {
            var body = dialog.Body;
            var side = 1200f - 40f - 650f - 16f;
            // The map: a square on the left.
            var mapBox = Ui.Image(body, "Map", Color.black, 8f, true).rectTransform;
            mapBox.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(650f, 650f));
            var picture = Ui.Rect("Picture", mapBox).Fill(2, 2, 2, 2);
            var raw = picture.gameObject.AddComponent<RawImage>();
            raw.texture = Resources.Load<Texture2D>("Games/Override Map");
            raw.raycastTarget = true;
            map = picture;
            shapes = ShapeGraphic.Create(map);
            picture.gameObject.AddComponent<MapInput>().owner = this;

            // The side: name, points, options, code.
            var panel = Ui.Rect("Side", body);
            panel.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(666f, 0f), new Vector2(side, 650f));
            Ui.Column(panel, 8f);

            var nameRow = Row(panel, 30f);
            Ui.Text(nameRow, "Label", "Name", Theme.FontSmall, Theme.TextDim).Size(70f, 28f);
            var name = Ui.Field(nameRow, "Name", plan.name, v => { plan.name = Clean(v); Changed(); }, Theme.FontSmall);
            name.Size(-1, 28f, 1f);
            Ui.Tip(name, "Name", null, "The path file's name: static/NAME.txt (LemLib's ASSET(NAME_txt)).");
            var startRow = Row(panel, 30f);
            Ui.Text(startRow, "Label", "Start heading", Theme.FontSmall, Theme.TextDim).Size(96f, 28f);
            var heading = Ui.Field(startRow, "Heading", F(plan.heading), v => { if (TryF(v, out var h)) plan.heading = Mathf.Repeat(h, 360f); Changed(); }, Theme.FontSmall);
            heading.Size(70f, 28f);
            Ui.Tip(heading, "Start heading", null, "Which way the robot faces at the first point, in degrees clockwise from the far wall (+Y), as odometry counts it.");
            var robot = Ui.Button(startRow, "Robot start", "Use the robot's start", UseRobotStart, false, 5f, Theme.FontSmall);
            robot.Size(-1, 28f, 1f);
            Ui.Tip(robot, "Use the robot's start", null, "Puts the first point where the robot starts in the open game (Play game's setup), facing the way it faces.");

            Ui.Text(panel, "Points Heading", "Points (x, y in inches · speed %)", Theme.FontSmall, Theme.TextDim).Size(-1, 18f);
            var scroll = Ui.Image(panel, "Points", Theme.Field, 6f, true).rectTransform;
            scroll.Size(-1, 200f);
            var viewport = Ui.Rect("Viewport", scroll).Fill(4, 4, 4, 4);
            viewport.gameObject.AddComponent<RectMask2D>();
            list = Ui.Rect("Content", viewport);
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(0.5f, 1f);
            list.sizeDelta = Vector2.zero;
            Ui.Column(list, 2f);
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            sr.viewport = viewport;
            sr.content = list;
            sr.horizontal = false;
            sr.scrollSensitivity = 24f;
            sr.movementType = ScrollRect.MovementType.Clamped;

            var opts = Row(panel, 30f);
            Ui.Text(opts, "Label", "Look ahead", Theme.FontSmall, Theme.TextDim).Size(76f, 28f);
            var look = Ui.Field(opts, "Lookahead", F(plan.lookahead), v => { if (TryF(v, out var l)) plan.lookahead = Mathf.Clamp(l, 2f, 40f); Changed(); }, Theme.FontSmall);
            look.Size(50f, 28f);
            Ui.Tip(look, "Look ahead", null, "Pure pursuit's look-ahead distance (inches): bigger cuts corners and is smoother, smaller follows tighter. LemLib's examples use 15.");
            Ui.Text(opts, "Label2", "Timeout", Theme.FontSmall, Theme.TextDim).Size(56f, 28f);
            var time = Ui.Field(opts, "Timeout", plan.timeout.ToString(CultureInfo.InvariantCulture), v => { if (int.TryParse(v, out var t)) plan.timeout = Mathf.Clamp(t, 100, 60000); Changed(); }, Theme.FontSmall);
            time.Size(62f, 28f);
            Ui.Tip(time, "Timeout", null, "Milliseconds before the motion gives up.");
            var back = Ui.Button(opts, "Backwards", plan.backwards ? "Backwards" : "Forwards", null, plan.backwards, 5f, Theme.FontSmall);
            back.Size(-1, 28f, 1f);
            back.onClick.AddListener(() => {
                plan.backwards = !plan.backwards;
                back.GetComponentInChildren<TextMeshProUGUI>().text = plan.backwards ? "Backwards" : "Forwards";
                back.targetGraphic.color = plan.backwards ? Theme.Accent : Theme.PanelRaised;
                Changed();
            });
            Ui.Tip(back, "Drive direction", null, "Backwards: the robot drives the path with its back first.");

            var formatRow = Row(panel, 30f);
            Ui.Text(formatRow, "Label", "Write it as", Theme.FontSmall, Theme.TextDim).Size(90f, 28f);
            var format = Ui.Button(formatRow, "Format", FormatName(plan.format), null, false, 5f, Theme.FontSmall);
            format.Size(-1, 28f, 1f);
            Popup.Chevron(format.transform);
            format.transform.Find("Chevron").gameObject.AddComponent<LayoutElement>().ignoreLayout = true;   // (placed by hand, as the code window's)
            format.GetComponent<HorizontalLayoutGroup>().padding.right = 24;
            format.onClick.AddListener(() => Popup.Open((RectTransform)format.transform, new[] { FormatName(0), FormatName(1) }, plan.format, i => {
                plan.format = i;
                format.GetComponentInChildren<TextMeshProUGUI>().text = FormatName(i);
                Changed();
            }));
            Ui.Tip(format, "Write it as", null, "Follow: one smooth pure pursuit motion along the curve (LemLib's follow with a path file, motion.follow in Python, EZ-Template's pid_odom_set with the points). One move per point: a move_to_point to each waypoint in turn (stops at each).");

            var code = Ui.Image(panel, "Code", Theme.Field, 6f).rectTransform;
            code.Size(-1, 168f);
            preview = Ui.Text(code, "Text", "", Theme.FontSmall - 1f, Theme.Text, null, TextAlignmentOptions.TopLeft);
            preview.rectTransform.Fill(8, 6, 8, 6);
            preview.textWrappingMode = TextWrappingModes.Normal;
            preview.overflowMode = TextOverflowModes.Ellipsis;
            if (MonoFont.Get != null) preview.font = MonoFont.Get;

            summary = Ui.Text(body, "Summary", "", Theme.FontSmall, Theme.TextDim);
            summary.rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, -2f), new Vector2(650f, 34f));
            summary.textWrappingMode = TextWrappingModes.Normal;

            var help = Ui.Text(dialog.Footer, "Help", "Click: add a point · drag: move · right-click: remove · Shift+click: insert on the path · wheel on a point: speed · Ctrl: no snapping", Theme.FontSmall - 1f, Theme.TextDim);
            help.Size(-1, 30f, 1f);
            var clear = Ui.Button(dialog.Footer, "Clear", "Clear", () => {
                plan.points.Clear();
                selected = -1;
                Changed();
            }, false, 6f, Theme.FontSmall);
            clear.Size(70f, 32f);
            var copy = Ui.Button(dialog.Footer, "Copy", "Copy code", () => {
                GUIUtility.systemCopyBuffer = Code(Language());
                Audio.Sounds.Play(Audio.Sounds.Tick);
            }, false, 6f, Theme.FontSmall);
            copy.Size(96f, 32f);
            Ui.Tip(copy, "Copy code", null, "The lines that run the path, for your own editor (a LemLib path file's text is in Insert's file).");
            var insert = Ui.Button(dialog.Footer, "Insert", "Insert into the program", Insert, true, 6f, Theme.FontSmall);
            insert.Size(190f, 32f);
            Ui.Tip(insert, "Insert into the program", null, "Adds the path file to the program's project (LemLib) and writes the lines that drive it where the code window's cursor is (or at the end).");
            Changed();
        }

        private static string FormatName(int f) => f == 0 ? "Follow the curve (pure pursuit)" : "One move per point";

        private static RectTransform Row(RectTransform parent, float height) {
            var row = Ui.Rect("Row", parent);
            Ui.Row(row, 6f);
            row.Size(-1, height);
            return row;
        }

        private static string Clean(string v) {
            var s = new string((v ?? "").Trim().Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
            if (s.Length == 0) s = "path1";
            if (char.IsDigit(s[0])) s = "path_" + s;
            return s;
        }

        private static string F(float v) => (Mathf.Round(v * 100f) / 100f).ToString("0.##", CultureInfo.InvariantCulture);
        private static bool TryF(string v, out float f) => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f);

        private void Changed() {
            listDirty = true;
            Keep();
        }

        private void UseRobotStart() {
            var game = Game.SkillsGame.Current;
            if (game == null) {
                Interface.Interface.Say("Open a game first (Play game): the robot's start is in its setup", false);
                return;
            }
            var p = new Point { x = game.x, y = game.y, speed = plan.points.Count > 0 ? plan.points[0].speed : 100f };
            if (plan.points.Count == 0) plan.points.Add(p);
            else plan.points[0] = p;
            plan.heading = Mathf.Repeat(game.heading, 360f);
            selected = 0;
            Changed();
            Audio.Sounds.Play(Audio.Sounds.Tick);
        }

        // ------------------------------------------------------------------ the curve

        /// <summary>The path sampled every `spacing` inches: x, y, speed (% of full), the last speed 0.</summary>
        private List<(Vector2 at, float speed)> Sampled(float spacing = 2f) {
            var pts = plan.points;
            var result = new List<(Vector2, float)>();
            if (pts.Count < 2) return result;
            // Catmull-Rom through the points (the ends repeated), finely, then evenly spaced.
            var fine = new List<(Vector2 at, float speed)>();
            for (var i = 0; i < pts.Count - 1; i++) {
                var p0 = V(pts[Mathf.Max(0, i - 1)]);
                var p1 = V(pts[i]);
                var p2 = V(pts[i + 1]);
                var p3 = V(pts[Mathf.Min(pts.Count - 1, i + 2)]);
                const int Steps = 24;
                for (var s = 0; s < Steps; s++) {
                    var t = s / (float)Steps;
                    fine.Add((CatmullRom(p0, p1, p2, p3, t), Mathf.Lerp(pts[i].speed, pts[i + 1].speed, t)));
                }
            }
            fine.Add((V(pts[^1]), pts[^1].speed));
            var total = 0f;
            for (var i = 1; i < fine.Count; i++) total += Vector2.Distance(fine[i - 1].at, fine[i].at);
            result.Add(fine[0]);
            var walked = 0f;
            var next = spacing;
            for (var i = 1; i < fine.Count; i++) {
                var seg = Vector2.Distance(fine[i - 1].at, fine[i].at);
                while (walked + seg >= next && seg > 1e-5f) {
                    var t = (next - walked) / seg;
                    result.Add((Vector2.Lerp(fine[i - 1].at, fine[i].at, t), Mathf.Lerp(fine[i - 1].speed, fine[i].speed, t)));
                    next += spacing;
                }
                walked += seg;
            }
            if (Vector2.Distance(result[^1].Item1, fine[^1].at) > 0.2f) result.Add(fine[^1]);
            // Slow into the end over the last 8" (LemLib's follow stops at the last point).
            var left = 0f;
            for (var i = result.Count - 1; i >= 0; i--) {
                if (i < result.Count - 1) left += Vector2.Distance(result[i].Item1, result[i + 1].Item1);
                if (left >= 8f) break;
                result[i] = (result[i].Item1, result[i].Item2 * Mathf.Clamp01(left / 8f));
            }
            return result;
        }

        private static Vector2 V(Point p) => new(p.x, p.y);

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t) {
            var t2 = t * t;
            var t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        // ------------------------------------------------------------------ the code

        private enum Lang { Python, LemLib, Ez, VexCpp }

        private static Lang Language() {
            var src = CodeStore.Source ?? "";
            if (!Scripting.CppTranslator.LooksLikeCpp(src)) return Lang.Python;
            if (src.Contains("ez::") || src.Contains("EZ-Template")) return Lang.Ez;
            if (src.Contains("lemlib")) return Lang.LemLib;
            return Lang.VexCpp;
        }

        private string FileName => plan.name + ".txt";

        /// <summary>The LemLib path file: path.jerryio's "x, y, speed" (speed 0-127), endData, then the waypoints.</summary>
        private string PathFile() {
            var sb = new StringBuilder();
            foreach (var (at, speed) in Sampled())
                sb.Append(F(at.x)).Append(", ").Append(F(at.y)).Append(", ").Append(F(Mathf.Round(speed * 1.27f * 10f) / 10f)).Append('\n');
            sb.Append("endData\n");
            sb.Append(MetaTag).Append(JsonUtility.ToJson(plan)).Append('\n');
            return sb.ToString();
        }

        private string Code(Lang lang) {
            var pts = plan.points;
            if (pts.Count < 2) return "Click at least two points on the field.";
            var fwd = !plan.backwards;
            var s0 = pts[0];
            var sb = new StringBuilder();
            switch (lang) {
                case Lang.Python:
                    sb.Append("chassis.set_pose(").Append(F(s0.x)).Append(", ").Append(F(s0.y)).Append(", ").Append(F(plan.heading)).Append(")\n");
                    if (plan.format == 0) {
                        sb.Append(plan.name).Append(" = [");
                        var first = true;
                        foreach (var (at, speed) in Sampled(4f)) {
                            if (!first) sb.Append(", ");
                            first = false;
                            sb.Append('(').Append(F(at.x)).Append(", ").Append(F(at.y)).Append(", ").Append(F(Mathf.Round(speed * 1.27f))).Append(')');
                        }
                        sb.Append("]\n");
                        sb.Append("chassis.follow(").Append(plan.name).Append(", ").Append(F(plan.lookahead)).Append(", ").Append(plan.timeout)
                          .Append(fwd ? "" : ", forwards=False").Append(", wait=True)\n");
                    }
                    else
                        foreach (var p in pts.Skip(1))
                            sb.Append("chassis.move_to_point(").Append(F(p.x)).Append(", ").Append(F(p.y)).Append(", ").Append(plan.timeout)
                              .Append(fwd ? "" : ", forwards=False").Append(", max_speed=").Append(Mathf.RoundToInt(p.speed * 1.27f)).Append(", wait=True)\n");
                    break;
                case Lang.Ez:
                    sb.Append("chassis.odom_xyt_set(").Append(F(s0.x)).Append("_in, ").Append(F(s0.y)).Append("_in, ").Append(F(plan.heading)).Append("_deg);\n");
                    if (plan.format == 0) {
                        sb.Append("chassis.pid_odom_set({");
                        var i = 0;
                        foreach (var p in pts.Skip(1)) {
                            if (i++ > 0) sb.Append(",\n                      ");
                            sb.Append("{{").Append(F(p.x)).Append("_in, ").Append(F(p.y)).Append("_in}, ").Append(fwd ? "fwd" : "rev").Append(", ").Append(Mathf.RoundToInt(p.speed * 1.27f)).Append('}');
                        }
                        sb.Append("}, true);\nchassis.pid_wait();\n");
                    }
                    else
                        foreach (var p in pts.Skip(1))
                            sb.Append("chassis.pid_odom_set({{").Append(F(p.x)).Append("_in, ").Append(F(p.y)).Append("_in}, ").Append(fwd ? "fwd" : "rev").Append(", ").Append(Mathf.RoundToInt(p.speed * 1.27f)).Append("});\nchassis.pid_wait();\n");
                    break;
                default:   // LemLib (and VEXcode C++ with LemLib's names)
                    sb.Append("chassis.setPose(").Append(F(s0.x)).Append(", ").Append(F(s0.y)).Append(", ").Append(F(plan.heading)).Append(");\n");
                    if (plan.format == 0)
                        sb.Append("chassis.follow(").Append(plan.name).Append("_txt, ").Append(F(plan.lookahead)).Append(", ").Append(plan.timeout)
                          .Append(fwd ? "" : ", false").Append(");\nchassis.waitUntilDone();\n");
                    else
                        foreach (var p in pts.Skip(1))
                            sb.Append("chassis.moveToPoint(").Append(F(p.x)).Append(", ").Append(F(p.y)).Append(", ").Append(plan.timeout)
                              .Append(", {.forwards = ").Append(fwd ? "true" : "false").Append(", .maxSpeed = ").Append(Mathf.RoundToInt(p.speed * 1.27f)).Append("});\nchassis.waitUntilDone();\n");
                    break;
            }
            return sb.ToString();
        }

        private void Insert() {
            if (plan.points.Count < 2) {
                Interface.Interface.Say("Click at least two points on the field first", false);
                return;
            }
            var lang = Language();
            var usesFile = plan.format == 0 && (lang == Lang.LemLib || lang == Lang.VexCpp);
            var global = usesFile ? "ASSET(" + plan.name + "_txt);" : null;
            var problem = CodePanel.InsertCode(Code(lang), usesFile ? "static/" + FileName : null, usesFile ? PathFile() : null, global);
            if (problem != null) {
                Interface.Interface.Say(problem, false);
                return;
            }
            Audio.Sounds.Play(Audio.Sounds.Tick);
            dialog.Close();
        }

        /// <summary>A planned path kept in a path file (its waypoints after endData), or null.</summary>
        public static bool OpenFile(string text) {
            var line = (text ?? "").Split('\n').FirstOrDefault(l => l.StartsWith(MetaTag));
            if (line == null) return false;
            try {
                var p = JsonUtility.FromJson<Plan>(line.Substring(MetaTag.Length));
                if (p == null || p.points == null) return false;
                PlayerPrefs.SetString(LastKey, JsonUtility.ToJson(p));
                if (open != null) open.dialog.Close();
                Open();
                return true;
            }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------ drawing

        private Vector2 ToMap(Vector2 inches) {
            var size = map.rect.size;
            return new Vector2(inches.x / MapInches * size.x, inches.y / MapInches * size.y);
        }

        private Vector2 ToField(Vector2 local) {
            var size = map.rect.size;
            return new Vector2(local.x / size.x * MapInches, local.y / size.y * MapInches);
        }

        private void LateUpdate() {
            if (map == null) return;
            if (listDirty) {
                listDirty = false;
                RebuildList();
                preview.text = Code(Language());
                var sampled = Sampled();
                var length = 0f;
                for (var i = 1; i < sampled.Count; i++) length += Vector2.Distance(sampled[i - 1].at, sampled[i].at);
                summary.text = plan.points.Count < 2
                    ? "Click on the field to add the first points (the first is where the robot starts)."
                    : plan.points.Count + " points · " + F(length) + "\" long · " + sampled.Count + " path points every 2\" · written for " + Language() switch {
                        Lang.Python => "Python (motion module)", Lang.Ez => "EZ-Template", Lang.LemLib => "LemLib", _ => "C++ (LemLib names)" };
            }
            Draw();
        }

        private void Draw() {
            shapes.Clear();
            var pts = plan.points;
            // The robot's real path in the last run (green) and where its odometry thought it was (orange, dashed).
            var trail = PathTrail.Real;
            if (trail.Count > 1) shapes.Polyline(trail.Select(p => ToMap(p)).ToList(), new Color(0.3f, 0.95f, 0.45f, 0.9f), 2f);
            var odom = PathTrail.Odom;
            for (var i = 1; i < odom.Count; i += 2) shapes.Line(ToMap(odom[i - 1]), ToMap(odom[i]), new Color(1f, 0.6f, 0.15f, 0.9f), 2f);
            if (pts.Count >= 2) {
                var curve = Sampled(1f);
                for (var i = 1; i < curve.Count; i++) {
                    var speed = curve[i].speed / 100f;
                    var color = Color.Lerp(new Color(0.95f, 0.3f, 0.3f), new Color(0.3f, 0.75f, 1f), speed);
                    shapes.Line(ToMap(curve[i - 1].at), ToMap(curve[i].at), color, 3f);
                }
            }
            // The robot at the start (its footprint, front marked) and at the end.
            if (pts.Count > 0) {
                Footprint(V(pts[0]), plan.heading, new Color(1f, 1f, 1f, 0.85f));
                if (pts.Count > 1) {
                    var end = Sampled(1f);
                    if (end.Count > 1) {
                        var d = end[^1].at - end[^2].at;
                        var h = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + (plan.backwards ? 180f : 0f);
                        Footprint(end[^1].at, h, new Color(1f, 1f, 1f, 0.4f));
                    }
                }
            }
            while (numbers.Count < pts.Count) {
                var t = Ui.Text(map, "Number", "", Theme.FontSmall - 2f, Color.white, Theme.Medium, TextAlignmentOptions.Center);
                t.rectTransform.sizeDelta = new Vector2(24f, 16f);
                t.raycastTarget = false;
                numbers.Add(t);
            }
            for (var i = 0; i < numbers.Count; i++) numbers[i].gameObject.SetActive(i < pts.Count);
            for (var i = 0; i < pts.Count; i++) {
                var at = ToMap(V(pts[i]));
                var sel = i == selected;
                shapes.Disc(at, sel ? 8f : 6.5f, Color.black, 20);
                shapes.Disc(at, sel ? 6.5f : 5f, i == 0 ? new Color(0.3f, 0.95f, 0.45f) : sel ? Theme.Accent : Color.white, 20);
                numbers[i].text = (i + 1).ToString();
                numbers[i].rectTransform.anchoredPosition = at + new Vector2(0f, 14f);
            }
        }

        private void Footprint(Vector2 middle, float heading, Color color) {
            var game = Game.SkillsGame.Current;
            var depth = game != null && game.depth > 1f ? game.depth : 18f;
            var width = game != null && game.width > 1f ? game.width : 18f;
            var fwd = new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
            var right = new Vector2(fwd.y, -fwd.x);
            var c = new[] { middle + fwd * depth / 2f + right * width / 2f, middle - fwd * depth / 2f + right * width / 2f,
                            middle - fwd * depth / 2f - right * width / 2f, middle + fwd * depth / 2f - right * width / 2f };
            shapes.Polyline(c.Select(ToMap).ToList(), color, 1.5f, true);
            shapes.Line(ToMap(middle), ToMap(middle + fwd * depth / 2f), color, 2f);
        }

        private void RebuildList() {
            for (var i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);
            for (var i = 0; i < plan.points.Count; i++) {
                var index = i;
                var p = plan.points[i];
                var row = Ui.Image(list, "Point " + (i + 1), i == selected ? Theme.Hover : new Color(0f, 0f, 0f, 0f), 4f, true).rectTransform;
                Ui.Row(row, 4f, new RectOffset(4, 4, 2, 2));
                row.Size(-1, 28f);
                Ui.Text(row, "Number", (i + 1).ToString(), Theme.FontSmall, i == 0 ? Theme.Success : Theme.TextDim).Size(22f, 24f);
                var x = Ui.Field(row, "X", F(p.x), v => { if (TryF(v, out var f)) plan.points[index].x = Mathf.Clamp(f, -FieldHalf, FieldHalf); Changed(); }, Theme.FontSmall);
                x.Size(-1, 24f, 1f);
                var y = Ui.Field(row, "Y", F(p.y), v => { if (TryF(v, out var f)) plan.points[index].y = Mathf.Clamp(f, -FieldHalf, FieldHalf); Changed(); }, Theme.FontSmall);
                y.Size(-1, 24f, 1f);
                var s = Ui.Field(row, "Speed", F(p.speed), v => { if (TryF(v, out var f)) plan.points[index].speed = Mathf.Clamp(f, 5f, 100f); Changed(); }, Theme.FontSmall);
                s.Size(52f, 24f);
                var remove = Ui.Button(row, "Remove", "×", () => Remove(index), false, 4f, Theme.FontSmall);
                remove.Size(24f, 24f);
            }
        }

        private void Remove(int i) {
            if (i < 0 || i >= plan.points.Count) return;
            plan.points.RemoveAt(i);
            selected = Mathf.Min(selected, plan.points.Count - 1);
            Changed();
        }

        // ------------------------------------------------------------------ the mouse on the map

        private int PointAt(Vector2 local) {
            var best = -1;
            var bestDistance = 11f;
            for (var i = 0; i < plan.points.Count; i++) {
                var d = Vector2.Distance(ToMap(V(plan.points[i])), local);
                if (d < bestDistance) {
                    bestDistance = d;
                    best = i;
                }
            }
            return best;
        }

        private static Vector2 Snap(Vector2 inches, bool free) {
            if (!free) inches = new Vector2(Mathf.Round(inches.x * 2f) / 2f, Mathf.Round(inches.y * 2f) / 2f);
            return new Vector2(Mathf.Clamp(inches.x, -FieldHalf, FieldHalf), Mathf.Clamp(inches.y, -FieldHalf, FieldHalf));
        }

        private static bool Ctrl => UnityEngine.InputSystem.Keyboard.current != null && (UnityEngine.InputSystem.Keyboard.current.ctrlKey.isPressed || UnityEngine.InputSystem.Keyboard.current.leftCommandKey.isPressed);
        private static bool Shift => UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.shiftKey.isPressed;

        private void Down(Vector2 local, PointerEventData.InputButton button) {
            var hit = PointAt(local);
            if (button == PointerEventData.InputButton.Right) {
                if (hit >= 0) Remove(hit);
                return;
            }
            if (button != PointerEventData.InputButton.Left) return;
            if (hit >= 0) {
                selected = dragging = hit;
                listDirty = true;
                return;
            }
            var at = Snap(ToField(local), Ctrl);
            var p = new Point { x = at.x, y = at.y, speed = plan.points.Count > 0 ? plan.points[^1].speed : 100f };
            if (Shift && plan.points.Count >= 2) {
                // Insert between the two waypoints whose stretch of curve passes nearest.
                var best = plan.points.Count - 1;
                var bestDistance = float.MaxValue;
                for (var i = 0; i < plan.points.Count - 1; i++) {
                    var mid = (V(plan.points[i]) + V(plan.points[i + 1])) / 2f;
                    var d = SegmentDistance(at, V(plan.points[i]), V(plan.points[i + 1])) + 0.001f * Vector2.Distance(at, mid);
                    if (d < bestDistance) {
                        bestDistance = d;
                        best = i + 1;
                    }
                }
                plan.points.Insert(best, p);
                selected = dragging = best;
            }
            else {
                plan.points.Add(p);
                selected = dragging = plan.points.Count - 1;
            }
            Audio.Sounds.Play(Audio.Sounds.Tick);
            Changed();
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b) {
            var ab = b - a;
            var t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        private void Drag(Vector2 local) {
            if (dragging < 0 || dragging >= plan.points.Count) return;
            var at = Snap(ToField(local), Ctrl);
            plan.points[dragging].x = at.x;
            plan.points[dragging].y = at.y;
            listDirty = true;
        }

        private void Up() {
            if (dragging >= 0) Changed();
            dragging = -1;
        }

        private void Scroll(Vector2 local, float delta) {
            var hit = PointAt(local);
            if (hit < 0) return;
            plan.points[hit].speed = Mathf.Clamp(plan.points[hit].speed + Mathf.Sign(delta) * 5f, 5f, 100f);
            selected = hit;
            Changed();
        }

        private void OnDestroy() {
            if (open == this) open = null;
            Keep();
        }

        private class MapInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IScrollHandler {
            public PathPlanner owner;

            private Vector2 Local(PointerEventData e) {
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out var local);
                return local;
            }

            public void OnPointerDown(PointerEventData e) => owner.Down(Local(e), e.button);
            public void OnDrag(PointerEventData e) => owner.Drag(Local(e));
            public void OnPointerUp(PointerEventData e) => owner.Up();
            public void OnScroll(PointerEventData e) => owner.Scroll(Local(e), e.scrollDelta.y);
        }
    }

    /// <summary>
    /// V2 (build 79): where the robot really went in the last run on the field (its middle), and where its odometry
    /// thought it was (the motion module's pose, when a program runs one): the path planner draws both over the plan.
    /// Field frame inches; up to 3000 points each (every 0.25", ~12 minutes of driving).
    /// </summary>
    public static class PathTrail {
        public static readonly List<Vector2> Real = new(), Odom = new();
        /// <summary>The odometry's pose now (the motion module or EZ-Template sets it), null without a program's odometry.</summary>
        public static Vector2? Odom2;
        private static bool wasRunning;

        public static void Record(Vector2 real, Vector2? odom) {
            if (!wasRunning) {
                Real.Clear();
                Odom.Clear();
                wasRunning = true;
            }
            if (Real.Count == 0 || Vector2.Distance(Real[^1], real) >= 0.25f) {
                if (Real.Count < 3000) Real.Add(real);
            }
            if (odom.HasValue && (Odom.Count == 0 || Vector2.Distance(Odom[^1], odom.Value) >= 0.25f) && Odom.Count < 3000) Odom.Add(odom.Value);
        }

        public static void Ended() {
            wasRunning = false;
            Odom2 = null;
        }
    }
}
