using System.Collections.Generic;
using Protobot.InputEvents;
using Protobot.Interface;
using Protobot.Motion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Game {
    /// <summary>V2: the mouse in a game's setup. Move: drag the robot to slide it along its wall, drag the blue ring
    /// to turn it; drag the alignment tool anywhere (it snaps flat to a wall), its ring turns it. Line up: click a face
    /// or point on the robot, then a face (or point) on the tool or the field, and the robot goes against it. Measure:
    /// click two points (robot, tool or field): the distance, with how far apart they are along each axis. Ctrl+Z
    /// takes back the last move. Run from SkillsGame.DrawSetup, after the field's drawings.</summary>
    public class SetupTools {
        public enum Mode { Move, LineUp, Measure }

        public Mode mode;
        public bool toolSelected;
        public string message;          // what the last Line up did (shown in the panel)

        private readonly SkillsGame game;
        private enum Drag { None, Robot, Tool, TurnRobot, TurnTool }
        private Drag drag;
        private Vector2 pressAt, toolStart;
        private float slideStart, turnStart, angleStart, toolHeadingStart;
        private FeaturePicker.Feature first, second;
        private int escFrame = -1;

        private static readonly Color RingColor = new(0.3f, 0.65f, 1f, 0.9f);
        private static readonly Color ToolColor = new(0.45f, 0.9f, 1f, 0.9f);

        public SetupTools(SkillsGame game) => this.game = game;

        /// <summary>Esc was used here this frame (to leave a mode or let go of the tool), so the game keeps it.</summary>
        public bool UsedEscape => escFrame == Time.frameCount;

        public void SetMode(Mode next) {
            mode = next;
            first = second = default;
            message = null;
            drag = Drag.None;
            game.NotifyChanged();
        }

        public string Instructions => mode switch {
            Mode.LineUp => !first.Valid ? "Click a face or point on your robot" : "Now click a face on the alignment tool or the field to put it against (or a point to line up with)",
            Mode.Measure => !first.Valid || second.Valid ? "Click a point on the robot, the tool or the field" : "Click a second point",
            _ => "Drag the robot to slide it along the wall, drag the blue ring to turn it" + (game.tool != null ? ". Drag the tool to move it" : ""),
        };

        /// <summary>The measurement, for the panel (empty when there isn't one).</summary>
        public string Readout {
            get {
                if (mode != Mode.Measure || !first.Valid || !second.Valid) return "";
                var c = Dimension.Components(first.point, second.point, Frame(first, second));
                return "Distance " + Inch.Format((second.point - first.point).magnitude) + "   X " + Inch.Format(Mathf.Abs(c.x)) + "   Y " + Inch.Format(Mathf.Abs(c.z))
                       + "   Up " + Inch.Format(Mathf.Abs(c.y));
            }
        }

        public void Update() {
            var field = game.field;
            if (field == null) return;
            var keyboard = Keyboard.current;
            var typing = TextInput.IsTyping;
            var free = MouseInput.withinScreen && !MouseInput.overUI && Dialog.OpenCount == 0 && !Popup.IsOpen;

            if (keyboard != null && !typing && keyboard.escapeKey.wasPressedThisFrame && Dialog.OpenCount == 0 && !Popup.IsOpen) {
                if (mode != Mode.Move) {
                    SetMode(Mode.Move);
                    escFrame = Time.frameCount;
                }
                else if (toolSelected) {
                    toolSelected = false;
                    escFrame = Time.frameCount;
                }
            }
            if (keyboard != null && !typing && keyboard.ctrlKey.isPressed && keyboard.zKey.wasPressedThisFrame && drag == Drag.None && game.Undo())
                Audio.Sounds.Play(Audio.Sounds.Tick);

            if (game.tool != null) DrawFootprint(game.ToolCorners(), toolSelected ? ToolColor : new Color(ToolColor.r, ToolColor.g, ToolColor.b, 0.45f));
            switch (mode) {
                case Mode.Move: Move(free); break;
                case Mode.LineUp: LineUp(free); break;
                case Mode.Measure: Measure(free); break;
            }
        }

        // ------------------------------------------------------------------ move

        private void Move(bool free) {
            var field = game.field;
            var robotCenter = new Vector2(game.x, game.y);
            var robotRing = DrawRing(robotCenter, RingRadius(game.depth, game.width), !toolSelected || drag == Drag.TurnRobot);
            List<Vector2> toolRing = null;
            if (game.tool != null && toolSelected) toolRing = DrawRing(new Vector2(game.toolX, game.toolY), RingRadius(game.toolSize.y, game.toolSize.x), true);
            var mouse = Overlay.Mouse;
            var onRobotRing = robotRing != null && (!toolSelected) && Overlay.DistanceTo(robotRing, mouse) < 10f;
            var onToolRing = toolRing != null && Overlay.DistanceTo(toolRing, mouse) < 10f;
            var keyboard = Keyboard.current;
            var shift = keyboard != null && keyboard.shiftKey.isPressed;
            var ctrl = keyboard != null && keyboard.ctrlKey.isPressed;

            if (drag == Drag.None) {
                var under = free ? Under() : Drag.None;
                if (onRobotRing || onToolRing) Overlay.MouseText("Drag to turn" + (onRobotRing ? " (the robot stays on the wall)" : ""), Theme.Text);
                else if (under == Drag.Robot) Overlay.MouseText("Drag to slide along the wall", Theme.Text);
                else if (under == Drag.Tool) Overlay.MouseText("Drag to move the alignment tool", Theme.Text);
                if (!free || !MouseInput.LeftButton.wasPressedThisFrame || !Floor(out pressAt)) return;
                if (onRobotRing || onToolRing) {
                    drag = onRobotRing ? Drag.TurnRobot : Drag.TurnTool;
                    var center = onRobotRing ? robotCenter : new Vector2(game.toolX, game.toolY);
                    angleStart = SkillsGame.HeadingOf(pressAt - center);
                    turnStart = game.Turn;
                    toolHeadingStart = game.toolHeading;
                }
                else if (under == Drag.Tool) {
                    drag = Drag.Tool;
                    toolSelected = true;
                    toolStart = new Vector2(game.toolX, game.toolY);
                }
                else if (under == Drag.Robot) {
                    drag = Drag.Robot;
                    toolSelected = false;
                    slideStart = game.Slide;
                }
                else {
                    if (toolSelected) game.NotifyChanged();
                    toolSelected = false;
                    return;
                }
                game.PushUndo();
                game.NotifyChanged();
                return;
            }

            if (!MouseInput.LeftButton.isPressed) {
                drag = Drag.None;
                game.NotifyChanged();
                return;
            }
            if (!Floor(out var at)) return;
            switch (drag) {
                case Drag.Robot: {
                    var slide = slideStart + Vector2.Dot(at - pressAt, game.WallTangent);
                    if (!shift) slide = Mathf.Round(slide * 8f) / 8f;
                    game.SetSlide(slide);
                    Overlay.MouseText(Inch.Format(game.Slide) + " from the wall's middle", Theme.Text);
                    break;
                }
                case Drag.Tool: {
                    var c = toolStart + (at - pressAt);
                    if (!shift) c = new Vector2(Mathf.Round(c.x * 8f) / 8f, Mathf.Round(c.y * 8f) / 8f);
                    game.PlaceTool(c.x, c.y, game.toolHeading, 1f);
                    break;
                }
                case Drag.TurnRobot:
                case Drag.TurnTool: {
                    var center = drag == Drag.TurnRobot ? new Vector2(game.x, game.y) : new Vector2(game.toolX, game.toolY);
                    var turn = Mathf.DeltaAngle(angleStart, SkillsGame.HeadingOf(at - center));
                    var step = shift ? 1f : ctrl ? 15f : 5f;
                    turn = Mathf.Round(turn / step) * step;
                    if (drag == Drag.TurnRobot) {
                        game.SetTurn(turnStart + turn);
                        Overlay.MouseText(Mathf.RoundToInt(game.Turn) + "° from square to the wall", Theme.Text);
                    }
                    else {
                        game.PlaceTool(game.toolX, game.toolY, toolHeadingStart + turn, 1f);
                        Overlay.MouseText(Mathf.RoundToInt(Mathf.Repeat(game.toolHeading, 360f)) + "°", Theme.Text);
                    }
                    break;
                }
            }
        }

        // What a press here would take: the alignment tool, the robot or nothing.
        private Drag Under() {
            var camera = Overlay.ViewCamera;
            if (camera == null) return Drag.None;
            var ray = camera.ScreenPointToRay(MouseInput.Position);
            if (Physics.Raycast(ray, out var hit, Mathf.Infinity, LayerMask.GetMask("Default", "HoleCollisions"), QueryTriggerInteraction.Ignore)) {
                if (game.tool != null && hit.collider.transform.IsChildOf(game.tool)) return Drag.Tool;
                if (hit.collider.GetComponentInParent<SavedObject>() != null) return Drag.Robot;
            }
            if (!Floor(out var p)) return Drag.None;
            if (game.tool != null && Inside(game.ToolCorners(), p)) return Drag.Tool;
            if (Inside(game.Corners(), p)) return Drag.Robot;
            return Drag.None;
        }

        // ------------------------------------------------------------------ line up

        private void LineUp(bool free) {
            DrawRing(new Vector2(game.x, game.y), RingRadius(game.depth, game.width), false);
            var hover = free ? (!first.Valid ? PickRobot() : PickTarget()) : default;
            FeaturePicker.DrawHover(hover, Theme.Accent);
            if (first.Valid) FeaturePicker.Draw(first, Theme.Dimension);
            Overlay.MouseText(Instructions, Theme.Text);
            if (!free || !MouseInput.LeftButton.wasPressedThisFrame) return;
            if (!hover.Valid) {
                Interface.Interface.Say(!first.Valid ? "Click a face or point on your robot" : "Click a face on the alignment tool or the field", false);
                return;
            }
            if (!first.Valid) {
                first = hover;
                game.NotifyChanged();
                return;
            }
            var robotFace = first.kind == FeaturePicker.Kind.Face ? first.normal : (Vector3?)null;
            var targetFace = hover.kind == FeaturePicker.Kind.Face ? hover.normal : (Vector3?)null;
            message = game.LineUp(first.point, robotFace, hover.point, targetFace);
            if (message != null) Interface.Interface.Say(message, false);
            else Audio.Sounds.Play(Audio.Sounds.Pop);
            first = default;
            game.NotifyChanged();
        }

        private FeaturePicker.Feature PickRobot() {
            var f = FeaturePicker.Pick();
            return f.Valid && f.part != null && f.part.GetComponent<SavedObject>() != null ? f : default;
        }

        private FeaturePicker.Feature PickTarget() {
            var f = FeaturePicker.Pick();
            if (f.Valid && f.part != null && f.part.GetComponent<PickablePart>() != null) return f;
            return FieldFace();
        }

        // ------------------------------------------------------------------ measure

        private void Measure(bool free) {
            var hover = free ? PickAny() : default;
            FeaturePicker.DrawHover(hover, Theme.Accent);
            if (!first.Valid) Overlay.MouseText(Instructions, Theme.Text);
            if (free && MouseInput.LeftButton.wasPressedThisFrame && hover.Valid) {
                if (!first.Valid || second.Valid) {
                    first = hover;
                    second = default;
                }
                else second = hover;
                game.NotifyChanged();
            }
            if (!first.Valid) return;
            FeaturePicker.Draw(first, Theme.Dimension);
            var end = second.Valid ? second : hover;
            if (!end.Valid) return;
            if (second.Valid) FeaturePicker.Draw(second, Theme.Dimension);
            var a = first.point;
            var b = end.point;
            Dimension.Legs("game measure", a, b, Frame(first, end), null, 0.9f, i => i == 0 ? "X" : i == 1 ? "Up" : "Y");
            if (Overlay.ToScreen(a, out var sa) && Overlay.ToScreen(b, out var sb)) {
                Overlay.Line(sa, sb, Theme.Dimension, 2f);
                if (Vector2.Distance(sa, sb) > 30f) {
                    Overlay.Arrowhead(sa, sb, Theme.Dimension);
                    Overlay.Arrowhead(sb, sa, Theme.Dimension);
                }
                Overlay.Text((sa + sb) / 2f + new Vector2(0f, 16f), Inch.Format((b - a).magnitude), Theme.Dimension, null, true);
            }
        }

        // The part's own axes when both ends are on one part, else the field's.
        private Quaternion Frame(FeaturePicker.Feature a, FeaturePicker.Feature b) =>
            a.part != null && a.part == b.part ? a.part.transform.rotation : game.field.transform.rotation;

        private FeaturePicker.Feature PickAny() {
            var f = FeaturePicker.Pick();
            return f.Valid ? f : FieldFace();
        }

        // A face of the field (its walls, goals, loaders, the alignment tool's shape) or the tiles.
        private FeaturePicker.Feature FieldFace() {
            var camera = Overlay.ViewCamera;
            if (camera == null) return default;
            var ray = camera.ScreenPointToRay(MouseInput.Position);
            if (Physics.Raycast(ray, out var hit, Mathf.Infinity, 1 << Sandbox.FieldLayer, QueryTriggerInteraction.Ignore))
                return new FeaturePicker.Feature { kind = FeaturePicker.Kind.Face, point = hit.point, normal = hit.normal };
            if (Floor(out var p)) return new FeaturePicker.Feature { kind = FeaturePicker.Kind.Face, point = game.field.ToWorld(p), normal = game.field.transform.up };
            return default;
        }

        // ------------------------------------------------------------------ helpers

        // Where the mouse points on the tiles (field frame).
        private bool Floor(out Vector2 point) {
            point = default;
            var camera = Overlay.ViewCamera;
            if (camera == null) return false;
            var t = game.field.transform;
            var plane = new Plane(t.up, t.position);
            var ray = camera.ScreenPointToRay(MouseInput.Position);
            if (!plane.Raycast(ray, out var distance)) return false;
            point = game.field.ToField(ray.GetPoint(distance));
            return true;
        }

        private static bool Inside(Vector2[] corners, Vector2 p) {
            var sign = 0;
            for (var i = 0; i < corners.Length; i++) {
                var a = corners[i];
                var b = corners[(i + 1) % corners.Length];
                var cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                var s = cross > 0 ? 1 : cross < 0 ? -1 : 0;
                if (s == 0) continue;
                if (sign == 0) sign = s;
                else if (s != sign) return false;
            }
            return true;
        }

        private static float RingRadius(float depth, float width) => 0.5f * Mathf.Sqrt(depth * depth + width * width) + 3f;

        private List<Vector2> DrawRing(Vector2 center, float radius, bool strong) {
            var color = strong ? RingColor : new Color(RingColor.r, RingColor.g, RingColor.b, 0.35f);
            var ring = Overlay.WorldCircle(game.field.ToWorld(center, 0.25f), game.field.transform.up, radius, color, strong ? 3f : 2f);
            // Little turn arrows on it, so it reads as a handle.
            if (strong)
                foreach (var angle in new[] { 45f, 225f }) {
                    var p = center + SkillsGame.Dir(angle) * radius;
                    var ahead = center + SkillsGame.Dir(angle + 8f) * radius;
                    if (Overlay.ToScreen(game.field.ToWorld(ahead, 0.25f), out var tip) && Overlay.ToScreen(game.field.ToWorld(p, 0.25f), out var from))
                        Overlay.Arrowhead(tip, from, color, 12f, 10f);
                }
            return ring;
        }

        private void DrawFootprint(Vector2[] corners, Color color) {
            for (var i = 0; i < corners.Length; i++)
                Overlay.Line(game.field.ToWorld(corners[i], 0.2f), game.field.ToWorld(corners[(i + 1) % corners.Length], 0.2f), color, 2f);
        }
    }
}
