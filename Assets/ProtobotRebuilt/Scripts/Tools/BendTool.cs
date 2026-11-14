using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools {
    // V2: the Bend tool (key 7): bends high strength shafts and standoffs as teams do, and the rubber link in its
    // rubber. Hover the part: a ring shows where the bend goes (1/16" steps from its end, Shift free); click to put
    // it there (a part already selected starts at its middle). Then two rotation rings sit on the bend: the blue one
    // turns the end (the bend's angle, 5° steps, up to 180°), the green one round the part sets which way it bends (15°
    // steps; Shift free on both), and the yellow ring on the part slides the bend along it. "Aim at a point" bends it
    // so the moving end points straight at a point picked next (a screw's tip, a hole, a corner), however far away.
    // The side that moves is drawn in the accent color with what's fastened to it outlined, and those parts move with
    // the preview while the rings are dragged. The values can be typed in the options, with a slider for how tight
    // the bend is. Enter bends (smooth, PartBend: one undo step, saved with the build). Esc steps back.
    public class BendTool : MonoBehaviour {
        private static BendTool active;

        private GameObject part;
        private float start, angle, toward;
        private static float radiusScale = 1.5f;   // the bend's radius, in part thicknesses
        private const float MinRadiusScale = 0.5f, MaxRadiusScale = 8f;
        private const float AngleLimit = 180f, LinkAngleLimit = 120f;

        private enum Ring { None, Location, Angle, Direction }
        private Ring hovered, dragging;
        private float dragFrom, dragSwept, lastMouseAngle;
        private Vector2 dragPress;
        private Vector3 dragCenter, dragAxis;

        // The side of the bend that moves, and the parts on it, shown moving with the preview.
        private PartBend.Plan plan;
        private bool aiming;
        private const string HighlightOwner = "Bend";

        private static readonly Color AngleColor = new(0.32f, 0.62f, 1f);
        private static readonly Color DirectionColor = new(0.35f, 0.85f, 0.45f);
        private static readonly Color LocationColor = new(1f, 0.82f, 0.25f);
        private static readonly Color MovingColor = new(1f, 0.55f, 0.15f);

        [RuntimeInitializeOnLoadMethod]
        private static void Register() {
            ToolRail.RegisterOptions("Bend", BuildOptions);
            ToolRail.RegisterBusy("Bend", () => active != null && active.part != null);
        }

        private void OnEnable() {
            active = this;
            part = null;
            aiming = false;
            hovered = dragging = Ring.None;
            var selected = SelectTool.SelectedPart;
            if (PartBend.IsBendable(selected)) Begin(selected, null);
        }

        private void OnDisable() {
            Cancel(false);
            if (active == this) active = null;
            RingDial.Clear();
        }

        // ------------------------------------------------------------------ the part

        private bool Link => PartBend.IsLink(part);
        private Mesh Straight => PartBend.Straight(part);
        private float Length => PartBend.Frame(part, Straight).length;
        private float Thickness => PartBend.Thickness(part, Straight);
        private float Radius => Mathf.Max(0.05f, Thickness * radiusScale);
        private float Limit => Link ? LinkAngleLimit : AngleLimit;

        private PartBend.Bend Make(float a, float t) => Link ? PartBend.LinkBend(part, a, t)
            : new PartBend.Bend { start = start, angle = a, toward = t, radius = Radius };

        private PartBend.Bend Current => Make(angle, toward);

        private static float Arc(PartBend.Bend bend) => bend.radius * Mathf.Abs(bend.angle) * Mathf.Deg2Rad;

        private float Snap(float s, bool free) {
            if (!free) s = Mathf.Round(s * 16f) / 16f;
            return Mathf.Clamp(s, 0.05f, Mathf.Max(0.05f, Length - 0.05f));
        }

        private static bool Shift => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

        private void Begin(GameObject target, Vector3? at) {
            part = target;
            angle = 0f;
            toward = 0f;
            aiming = false;
            var saved = PartBend.SavedBends(part);
            start = Link ? PartBend.LinkRubber(part).start
                : Snap(at.HasValue ? PartBend.Along(part, at.Value, saved) : Length / 2f, Shift);
            plan = null;
            Preview();
            ToolOptions.Rebuild();
        }

        private void Cancel(bool sound) {
            if (part != null && !part.IsDeleted()) {
                PartBend.Restore(plan);
                PartBend.Show(part, PartBend.SavedBends(part));
            }
            plan = null;
            Motion.Highlights.Clear(HighlightOwner);
            var had = part != null;
            part = null;
            aiming = false;
            dragging = Ring.None;
            if (sound && had) Audio.Sounds.Play(Audio.Sounds.Tick);
            ToolOptions.Rebuild();
        }

        // The preview: the part bent as set, and the side that moves (with what's fastened to it) moved to match.
        private void Preview() {
            if (part == null) return;
            var saved = PartBend.SavedBends(part);
            var bends = new List<PartBend.Bend>(saved);
            if (Mathf.Abs(angle) > 0.01f) bends.Add(Current);
            if (plan == null || plan.part != part || Mathf.Abs(plan.cut - start) > 1e-4f) {
                PartBend.Restore(plan);
                plan = PartBend.MakePlan(part, start);
            }
            PartBend.Pause(plan);
            PartBend.Pose(plan, saved, bends);
            PartBend.Show(part, bends);
            Motion.Highlights.Set(HighlightOwner, plan.movers, MovingColor, true);
        }

        private void Apply() {
            if (part == null) return;
            if (Mathf.Abs(angle) < 0.5f) {
                Interface.Interface.Say("Drag the blue ring to bend it first (or type an angle)", false);
                return;
            }
            var bend = Current;
            if (bend.start + Arc(bend) > Length - 0.05f) {
                Interface.Interface.Say("The bend runs off the end of the part: move it back or make it tighter", false);
                return;
            }
            var saved = PartBend.SavedBends(part);
            if (saved.Any(b => bend.start < b.start + Arc(b) + 0.02f && b.start < bend.start + Arc(bend) + 0.02f)) {
                Interface.Interface.Say("That runs into a bend already there: move it along the part", false);
                return;
            }
            PartBend.Restore(plan);
            plan = null;
            PartBend.Show(part, saved);
            var after = new List<PartBend.Bend>(saved) { bend };
            PartBend.ChangeBends(part, after, bend.start);
            Audio.Sounds.Play(Audio.Sounds.Clack);
            angle = 0f;
            Preview();
            ToolOptions.Rebuild();
        }

        private void Straighten() {
            if (part == null) return;
            var saved = PartBend.SavedBends(part);
            if (saved.Count == 0) {
                Interface.Interface.Say("It's straight already", false);
                return;
            }
            PartBend.Restore(plan);
            plan = null;
            PartBend.Show(part, saved);
            angle = 0f;
            PartBend.ChangeBends(part, new List<PartBend.Bend>(), saved.Min(b => b.start));
            Audio.Sounds.Play(Audio.Sounds.Clack);
            Preview();
            ToolOptions.Rebuild();
        }

        // ------------------------------------------------------------------ aiming at a point

        // The bend (angle and which way) that points the moving end straight at the target: a coarse search over
        // every way and angle, then finer round the best. Returns how far the end's line passes from the target.
        private float AimAt(Vector3 target) {
            // Tighter if it has to be: the end left after a gentle bend may be too short to turn far enough.
            var original = radiusScale;
            var bestScale = radiusScale;
            float bestMiss = float.MaxValue, bestAngle = angle, bestToward = toward;
            var scales = Link ? new[] { radiusScale } : new[] { radiusScale, 1.5f, 1f, 0.75f, MinRadiusScale }.Where(x => x <= radiusScale + 1e-4f).Distinct().ToArray();
            foreach (var scale in scales) {
                radiusScale = scale;
                var miss = AimWith(target);
                if (miss < bestMiss - 0.005f) {
                    bestMiss = miss;
                    bestScale = scale;
                    bestAngle = angle;
                    bestToward = toward;
                }
                if (bestMiss < 0.02f) break;
            }
            radiusScale = bestMiss < 1e5f ? bestScale : original;
            angle = bestAngle;
            toward = bestToward;
            return bestMiss;
        }

        private float AimWith(Vector3 target) {
            var saved = PartBend.SavedBends(part);
            float Miss(float a, float t) {
                var bend = Make(a, t);
                if (!Link && bend.start + Arc(bend) > Length - 0.05f) return 1e6f;   // runs off the end
                var bends = new List<PartBend.Bend>(saved) { bend };
                var (point, direction) = PartBend.EndRay(plan, saved, bends);
                var to = target - point;
                var ahead = Vector3.Dot(to, direction);
                if (ahead < 0.05f) return 1e6f;
                return Vector3.Cross(to, direction.normalized).magnitude + a * 1e-5f;   // the smaller angle on a tie
            }
            float bestA = 0f, bestT = toward, best = float.MaxValue;
            for (var t = 0f; t < 360f; t += 5f)
                for (var a = 1f; a <= Limit; a += 1f) {
                    var m = Miss(a, t);
                    if (m < best) {
                        best = m;
                        bestA = a;
                        bestT = t;
                    }
                }
            for (var pass = 0; pass < 2; pass++) {
                var tStep = pass == 0 ? 0.5f : 0.05f;
                var aStep = pass == 0 ? 0.1f : 0.01f;
                var (a0, t0) = (bestA, bestT);
                for (var t = t0 - tStep * 10f; t <= t0 + tStep * 10f; t += tStep)
                    for (var a = Mathf.Max(0.5f, a0 - aStep * 10f); a <= Mathf.Min(Limit, a0 + aStep * 10f); a += aStep) {
                        var m = Miss(a, t);
                        if (m < best) {
                            best = m;
                            bestA = a;
                            bestT = Mathf.Repeat(t, 360f);
                        }
                    }
            }
            if (best >= 1e5f) return best;
            angle = bestA;
            toward = bestT;
            return best;
        }

        private void UpdateAiming() {
            if (MouseInput.overUI || !MouseInput.withinScreen) return;
            var feature = FeaturePicker.Pick();
            FeaturePicker.DrawHover(feature, Theme.Accent);
            var (end, direction) = plan != null ? PartBend.EndRay(plan, PartBend.SavedBends(part), PartBend.SavedBends(part)) : (Vector3.zero, Vector3.zero);
            Overlay.Begin();
            if (feature.Valid && plan != null) Overlay.Line(end, feature.point, new Color(MovingColor.r, MovingColor.g, MovingColor.b, 0.7f), 1.5f);
            Overlay.MouseText(feature.Valid ? "Point the end at this " + feature.Describe().ToLowerInvariant() : "Click the point the end should point at (Esc: back)", Color.white);
            if (!MouseInput.LeftButton.wasPressedThisFrame || !feature.Valid) return;
            aiming = false;
            var miss = AimAt(feature.point);
            if (miss >= 1e5f) {
                Interface.Interface.Say("The moving end can't turn to face that point (it's behind it): pick a point ahead of the bend", false);
                ToolOptions.Rebuild();
                return;
            }
            Preview();
            Audio.Sounds.Play(Audio.Sounds.Tick);
            if (miss > 0.05f)
                Interface.Interface.Say("It can only get within " + Inch.Format(miss) + " of pointing at it from here: move the bend or change how tight it is", false);
            ToolOptions.Rebuild();
        }

        // ------------------------------------------------------------------ every frame

        private void Update() {
            if (Motion.Sandbox.Running) return;
            var keyboard = Keyboard.current;
            if (part != null && part.IsDeleted()) {
                part = null;
                plan = null;
                Motion.Highlights.Clear(HighlightOwner);
                ToolOptions.Rebuild();
            }
            if (part == null) {
                Pick();
                return;
            }
            var typing = InputEvents.TextInput.IsTyping;
            if (keyboard != null && !typing) {
                if (keyboard.escapeKey.wasPressedThisFrame) {
                    if (aiming) {
                        aiming = false;
                        ToolOptions.Rebuild();
                    }
                    else if (dragging != Ring.None) {
                        if (dragging == Ring.Angle) angle = dragFrom;
                        else if (dragging == Ring.Direction) toward = dragFrom;
                        else start = dragFrom;
                        dragging = Ring.None;
                        Preview();
                    }
                    else Cancel(true);
                    return;
                }
                if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) {
                    Apply();
                    return;
                }
            }
            DrawMovingSide();
            if (aiming) {
                UpdateAiming();
                return;
            }
            DrawAndDrag();
        }

        // Before a part is chosen: a ring where the mouse is along a bendable part.
        private void Pick() {
            var camera = Overlay.ViewCamera;
            if (camera == null || MouseInput.overUI || !MouseInput.withinScreen) return;
            if (!Physics.Raycast(camera.ScreenPointToRay(MouseInput.Position), out var hit, Mathf.Infinity, LayerMask.GetMask("Default"))) {
                if (MouseInput.LeftButton.wasPressedThisFrame) Interface.Interface.Say("Click an HS shaft, a standoff or a rubber link", false);
                return;
            }
            var target = FeaturePicker.PartOf(hit.collider.gameObject);
            if (!PartBend.IsBendable(target)) {
                if (MouseInput.LeftButton.wasPressedThisFrame)
                    Interface.Interface.Say("Only high strength shafts, standoffs and the rubber link bend", false);
                return;
            }
            var previous = part;
            part = target;   // for the helpers
            var saved = PartBend.SavedBends(part);
            var s = Link ? PartBend.LinkRubber(part).start + PartBend.LinkRubber(part).length / 2f : Snap(PartBend.Along(part, hit.point, saved), Shift);
            var (local, turn) = PartBend.CenterAt(part, s, saved);
            var center = PartBend.ToWorld(part, local);
            var along = part.transform.rotation * (turn * PartBend.AlongLocal(part));
            part = previous;
            Overlay.Begin();
            Overlay.WorldCircle(center, along, ThicknessOf(target) * 0.75f + 0.02f, LocationColor, 2.5f);
            Overlay.MouseText(PartBend.IsLink(target) ? "Bend the rubber link" : "Bend here: " + Inch.Format(s) + " from the end", Color.white);
            if (MouseInput.LeftButton.wasPressedThisFrame) Begin(target, hit.point);
        }

        private static float ThicknessOf(GameObject target) => PartBend.Thickness(target, PartBend.Straight(target));

        // The side that moves: a thick line along the part from the bend to that end, with a label.
        private void DrawMovingSide() {
            if (plan == null || plan.tied) return;
            var saved = PartBend.SavedBends(part);
            var bends = new List<PartBend.Bend>(saved);
            if (Mathf.Abs(angle) > 0.01f) bends.Add(Current);
            var arc = Mathf.Abs(angle) > 0.01f ? Arc(Current) : 0f;
            var from = plan.partMoves ? 0f : Mathf.Min(Length, start + arc);
            var to = plan.partMoves ? start : Length;
            // In the part's current frame (the part itself moves when its near side does).
            Overlay.Begin();
            var points = new List<Vector2>();
            for (var i = 0; i <= 16; i++) {
                var s = Mathf.Lerp(from, to, i / 16f);
                var (local, _) = PartBend.CenterAt(part, s, bends);
                if (Overlay.ToScreen(PartBend.ToWorld(part, local), out var at)) points.Add(at);
            }
            for (var i = 1; i < points.Count; i++) Overlay.Line(points[i - 1], points[i], new Color(MovingColor.r, MovingColor.g, MovingColor.b, 0.85f), 4f);
            if (points.Count > 0) {
                var end = points[plan.partMoves ? 0 : points.Count - 1];
                var moved = plan.movers.Count;
                Overlay.Text(end + new Vector2(0f, 26f), "This end moves" + (moved > 0 ? " (with " + moved + (moved == 1 ? " part)" : " parts)") : ""), MovingColor, null, true);
            }
        }

        private void DrawAndDrag() {
            var camera = Overlay.ViewCamera;
            if (camera == null) return;
            var saved = PartBend.SavedBends(part);
            var (local, turn) = PartBend.CenterAt(part, start, saved);
            // The rings stay where the bend is on the part as it was (the part itself may be swinging round).
            var rotation = plan != null ? plan.rotation : part.transform.rotation;
            var origin = plan != null ? plan.origin : part.transform.position;
            var center = origin + rotation * local;
            var (alongLocal, towardLocal, aboutLocal) = PartBend.Directions(Axis, toward);
            var along = rotation * (turn * alongLocal);
            var towardWorld = rotation * (turn * towardLocal);
            var about = rotation * (turn * aboutLocal);
            var perPixel = MirrorTool.PerPixel(camera, center);
            var thickness = Thickness;
            var angleRadius = Mathf.Max(thickness * 2.4f, 78f * perPixel);
            var directionRadius = Mathf.Max(thickness * 1.6f, 48f * perPixel);
            var locationRadius = thickness * 0.75f + 3f * perPixel;

            Overlay.Begin();
            var mouse = Overlay.Mouse;
            var angleLine = Overlay.WorldCircle(center, about, angleRadius, Lit(Ring.Angle, AngleColor), Lit(Ring.Angle) ? 3.5f : 2.5f, true);
            var directionLine = Overlay.WorldCircle(center, along, directionRadius, Lit(Ring.Direction, DirectionColor), Lit(Ring.Direction) ? 3.5f : 2.5f, true);
            List<Vector2> locationLine = null;
            if (!Link) locationLine = Overlay.WorldCircle(center, along, locationRadius, Lit(Ring.Location, LocationColor), Lit(Ring.Location) ? 3.5f : 2.5f, true);
            // Where it bends toward (a dot on the green ring) and where the end points (a dot on the blue ring).
            var pointing = Quaternion.AngleAxis(angle, about) * along;
            if (Overlay.ToScreen(center + towardWorld * directionRadius, out var towardAt)) Overlay.Disc(towardAt, 6f, DirectionColor);
            if (Overlay.ToScreen(center + pointing * angleRadius, out var pointAt)) Overlay.Disc(pointAt, 6f, AngleColor);
            Overlay.Line(center, center + pointing * angleRadius, new Color(AngleColor.r, AngleColor.g, AngleColor.b, 0.6f), 1.5f);
            if (Overlay.ToScreen(center, out var centerAt))
                Overlay.Text(centerAt + new Vector2(0f, -40f), Mathf.Abs(angle).ToString("0.#", CultureInfo.InvariantCulture) + "° toward " + toward.ToString("0", CultureInfo.InvariantCulture) + "°"
                    + (Link ? "" : " · " + Inch.Format(start) + " from the end"), Theme.Text);

            if (dragging == Ring.None) {
                hovered = Ring.None;
                if (!PartBendMouseBusy()) {
                    var best = 9f;
                    void Try(List<Vector2> line, Ring ring) {
                        if (line == null || line.Count < 2) return;
                        var d = Overlay.DistanceTo(line, mouse);
                        if (d < best) {
                            best = d;
                            hovered = ring;
                        }
                    }
                    Try(locationLine, Ring.Location);
                    Try(directionLine, Ring.Direction);
                    Try(angleLine, Ring.Angle);
                }
                if (hovered != Ring.None) {
                    var tip = hovered == Ring.Angle ? "Drag: bend angle (5° steps, Shift free)" : hovered == Ring.Direction ? "Drag: which way it bends (15° steps, Shift free)" : "Drag: where it bends (1/16\" steps, Shift free)";
                    Overlay.MouseText(tip, Color.white);
                    if (MouseInput.LeftButton.wasPressedThisFrame) StartDrag(hovered, center, hovered == Ring.Angle ? about : along);
                }
                else if (MouseInput.LeftButton.wasPressedThisFrame && !MouseInput.overUI && MouseInput.withinScreen) {
                    // A click elsewhere: another bendable part starts over there; empty space does nothing.
                    if (Physics.Raycast(camera.ScreenPointToRay(MouseInput.Position), out var hit, Mathf.Infinity, LayerMask.GetMask("Default"))) {
                        var target = FeaturePicker.PartOf(hit.collider.gameObject);
                        if (target != part && PartBend.IsBendable(target)) {
                            Cancel(false);
                            Begin(target, hit.point);
                        }
                    }
                }
                return;
            }

            // Dragging a ring.
            if (!MouseInput.LeftButton.isPressed) {
                dragging = Ring.None;
                RingDial.Clear();
                ToolOptions.Rebuild();
                return;
            }
            if (dragging == Ring.Location) {
                var moved = MirrorTool.AlongLine(camera, dragCenter, dragAxis);
                var s = Snap(dragFrom + moved, Shift);
                if (Mathf.Abs(s - start) > 1e-4f) {
                    start = s;
                    Preview();
                }
                Overlay.MouseText(Inch.Format(start) + " from the end", Theme.Accent, null, true);
                return;
            }
            var now = MouseAngle(camera, dragCenter, dragAxis);
            dragSwept += Mathf.DeltaAngle(lastMouseAngle, now);
            lastMouseAngle = now;
            var step = dragging == Ring.Angle ? 5f : 15f;
            var raw = dragFrom + dragSwept;
            var value = Shift ? Mathf.Round(raw * 2f) / 2f : Mathf.Round(raw / step) * step;
            if (dragging == Ring.Angle) {
                value = Mathf.Clamp(value, -Limit, Limit);
                if (Mathf.Abs(value - angle) > 1e-3f) {
                    angle = value;
                    Preview();
                }
            }
            else {
                value = Mathf.Repeat(value, 360f);
                if (Mathf.Abs(Mathf.DeltaAngle(value, toward)) > 1e-3f) {
                    toward = value;
                    Preview();
                }
            }
            RingDial.Draw(dragCenter, dragAxis, dragPress, MouseInput.Position, value - dragFrom, Shift ? 1f : step);
        }

        private bool PartBendMouseBusy() => MouseInput.LeftButton.isPressed && !MouseInput.LeftButton.wasPressedThisFrame;

        private int Axis => PartBend.Frame(part, Straight).axis;

        private bool Lit(Ring ring) => hovered == ring || dragging == ring;
        private Color Lit(Ring ring, Color color) => Lit(ring) ? Color.Lerp(color, Color.white, 0.35f) : color;

        private void StartDrag(Ring ring, Vector3 center, Vector3 axis) {
            dragging = ring;
            dragCenter = center;
            dragAxis = axis.normalized;
            dragPress = MouseInput.Position;
            dragSwept = 0f;
            dragFrom = ring == Ring.Angle ? angle : ring == Ring.Direction ? toward : start;
            lastMouseAngle = MouseAngle(Overlay.ViewCamera, center, dragAxis);
        }

        // The mouse's angle round an axis through a point: on the plane across the axis, or (seen edge-on) on screen.
        private static float MouseAngle(Camera camera, Vector3 center, Vector3 axis) {
            var ray = camera.ScreenPointToRay(MouseInput.Position);
            var plane = new Plane(axis, center);
            var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var v = Vector3.Cross(axis, u);
            if (Mathf.Abs(Vector3.Dot(ray.direction, axis)) > 0.12f && plane.Raycast(ray, out var distance)) {
                var p = ray.GetPoint(distance) - center;
                return Mathf.Atan2(Vector3.Dot(p, v), Vector3.Dot(p, u)) * Mathf.Rad2Deg;
            }
            var c = camera.WorldToScreenPoint(center);
            var m = (Vector2)MouseInput.Position - (Vector2)c;
            var screenAngle = Mathf.Atan2(m.y, m.x) * Mathf.Rad2Deg;
            return Vector3.Dot(axis, camera.transform.forward) > 0f ? screenAngle : -screenAngle;
        }

        // ------------------------------------------------------------------ options

        private static void BuildOptions(RectTransform bar) {
            var tool = active;
            if (tool == null || tool.part == null) {
                ToolOptions.Note(bar, "Click an HS shaft, a standoff or a rubber link where it should bend");
                Ui.Hint(bar, "Shift", "free placing");
                return;
            }
            var name = PartLabels.Short(tool.part);
            ToolOptions.Note(bar, name.Length > 24 ? name.Substring(0, 23) + "…" : name);
            Ui.Divider(bar, true).Size(1f, 22f);
            if (!tool.Link) {
                ToolOptions.Note(bar, "At");
                var at = Ui.Field(bar, "Bend At", Inch.Format(tool.start), text => {
                    if (Inch.TryParse(text, out var inches)) {
                        tool.start = tool.Snap(inches, true);
                        tool.Preview();
                    }
                    else Interface.Interface.Say("Type a distance from the end, like 1 1/4", false);
                    ToolOptions.Rebuild();
                }, Theme.FontBody, TextAlignmentOptions.Center);
                at.Size(70f, 30f);
                at.gameObject.AddComponent<WheelStep>().step = d => {
                    tool.start = tool.Snap(tool.start + d / 16f, true);
                    tool.Preview();
                    ToolOptions.Rebuild();
                };
                Ui.Tip(at, "Where it bends", "Wheel", "From the part's end to where the bend starts (1/16\" steps with the wheel). The yellow ring drags it.");
            }
            ToolOptions.Note(bar, "Angle");
            var angleField = Ui.Field(bar, "Bend Angle", tool.angle.ToString("0.#", CultureInfo.InvariantCulture) + "°", text => {
                if (float.TryParse(text.Replace("°", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees)) {
                    tool.angle = Mathf.Clamp(degrees, -tool.Limit, tool.Limit);
                    tool.Preview();
                }
                else Interface.Interface.Say("Type an angle in degrees, like 45", false);
                ToolOptions.Rebuild();
            }, Theme.FontBody, TextAlignmentOptions.Center);
            angleField.Size(60f, 30f);
            angleField.gameObject.AddComponent<WheelStep>().step = d => {
                tool.angle = Mathf.Clamp(tool.angle + d * 5f, -tool.Limit, tool.Limit);
                tool.Preview();
                ToolOptions.Rebuild();
            };
            Ui.Tip(angleField, "Bend angle", "Wheel", "How far the end turns, up to " + tool.Limit.ToString("0", CultureInfo.InvariantCulture) + "° (5° steps with the wheel). The blue ring drags it.");
            ToolOptions.Note(bar, "Toward");
            var towardField = Ui.Field(bar, "Bend Toward", tool.toward.ToString("0", CultureInfo.InvariantCulture) + "°", text => {
                if (float.TryParse(text.Replace("°", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees)) {
                    tool.toward = Mathf.Repeat(degrees, 360f);
                    tool.Preview();
                }
                ToolOptions.Rebuild();
            }, Theme.FontBody, TextAlignmentOptions.Center);
            towardField.Size(56f, 30f);
            towardField.gameObject.AddComponent<WheelStep>().step = d => {
                tool.toward = Mathf.Repeat(tool.toward + d * 15f, 360f);
                tool.Preview();
                ToolOptions.Rebuild();
            };
            Ui.Tip(towardField, "Which way", "Wheel", "Which way round the part it bends, in degrees (15° steps with the wheel). The green ring drags it.");
            if (!tool.Link) {
                // How tight the bend is: a slider from tight (half the part's thickness) to gentle.
                ToolOptions.Note(bar, "Tight");
                var holder = Ui.Rect("Radius", bar);
                holder.Size(190f, 30f);
                Ui.Row(holder, 0f);
                var value = Mathf.InverseLerp(Mathf.Log(MinRadiusScale), Mathf.Log(MaxRadiusScale), Mathf.Log(radiusScale));
                var slider = Motion.MotionUi.Slider(holder, value, 0f, 0f, v => Inch.Format(ScaleAt(v) * tool.Thickness) + " R", v => {
                    radiusScale = ScaleAt(v);
                    tool.Preview();
                });
                var label = slider.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.Size(56f, 26f);
                ToolOptions.Note(bar, "Gentle");
                Ui.Tip(holder, "How tight", null, "The bend's inside radius: left is as tight as the part allows (half its thickness), right is a long gentle curve.");
            }
            Ui.Divider(bar, true).Size(1f, 22f);
            var aim = Ui.Button(bar, "Aim", tool.aiming ? "Click a point…" : "Aim at a point", () => {
                tool.aiming = !tool.aiming;
                ToolOptions.Rebuild();
            }, tool.aiming, 5f);
            aim.Size(118f, 30f);
            Ui.Tip(aim, "Aim at a point", null, "Then click a point (a screw's tip, a hole, a corner): the bend turns so the end that moves points straight at it, however far away.");
            if (PartBend.SavedBends(tool.part).Count > 0) {
                var straighten = Ui.Button(bar, "Straighten", "Straighten", tool.Straighten, false, 5f);
                straighten.Size(96f, 30f);
                Ui.Tip(straighten, "Straighten", null, "Takes every bend out of this part (one undo step).");
            }
            var apply = Ui.Button(bar, "Apply Bend", "Bend", tool.Apply, true, 5f);
            apply.Size(64f, 30f);
            Ui.Tip(apply, "Bend", "Enter", "Bend the part (one undo step; saved with the build). The orange side moves, with the parts outlined on it.");
            Ui.Hint(bar, "Esc", "back");
        }

        // Slider position (0..1) to radius in thicknesses, on a log scale (fine control where it's tight).
        private static float ScaleAt(float v) => Mathf.Exp(Mathf.Lerp(Mathf.Log(MinRadiusScale), Mathf.Log(MaxRadiusScale), v));
    }
}
