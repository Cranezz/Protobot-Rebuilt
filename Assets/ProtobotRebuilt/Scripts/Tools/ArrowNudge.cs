using System.Linq;
using Protobot.Interface;
using Protobot.Transformations;
using UnityEngine;

namespace Protobot.Tools {
    // V2: precise moves from the keyboard in the Transform tool: with a part selected, each press moves it (and what
    // it's fastened to) one snap step (TransformGizmo.MoveStep) or turns it one turn step (TurnStep; Ctrl+Shift 45°)
    // about the gizmo. The arrows go along the part's axis (or the world's, with Local off) that looks most like
    // that way on screen; Shift+Up/Down toward/away from the camera; Ctrl+Left/Right spin it on screen, Ctrl+Up/Down
    // tip it toward/away (Alt is the original's copy-while-moving key). Each press is one undo step. Keys in Edit > Keyboard shortcuts ("Moving parts").
    public class ArrowNudge : MonoBehaviour {
        private const string Group = "Moving parts";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            Keybinds.Add("NudgePartLeft", Group, "Move the selected part left one step", "LeftArrow");
            Keybinds.Add("NudgePartRight", Group, "Move it right one step", "RightArrow");
            Keybinds.Add("NudgePartUp", Group, "Move it up one step", "UpArrow");
            Keybinds.Add("NudgePartDown", Group, "Move it down one step", "DownArrow");
            Keybinds.Add("NudgePartAway", Group, "Move it away (into the screen)", "Shift+UpArrow");
            Keybinds.Add("NudgePartToward", Group, "Move it toward you", "Shift+DownArrow");
            Keybinds.Add("TurnPartLeft", Group, "Turn it counterclockwise one step (Ctrl+Shift: 45°)", "Ctrl+LeftArrow", true);
            Keybinds.Add("TurnPartRight", Group, "Turn it clockwise one step (Ctrl+Shift: 45°)", "Ctrl+RightArrow", true);
            Keybinds.Add("TipPartAway", Group, "Tip it away one step (Ctrl+Shift: 45°)", "Ctrl+UpArrow", true);
            Keybinds.Add("TipPartToward", Group, "Tip it toward you one step (Ctrl+Shift: 45°)", "Ctrl+DownArrow", true);
            new GameObject("Arrow Nudge").AddComponent<ArrowNudge>();
        }

        private void Update() {
            if (ToolRail.Current != "Transform" || InputEvents.TextInput.IsTyping || InputEvents.TextInput.ShortcutsPaused
                || Motion.Sandbox.Running || Game.SkillsGame.Active || Placement.IsPlacing || Popup.IsOpen) return;
            var camera = Overlay.ViewCamera;
            if (camera == null) return;

            Vector3? move = null;
            (Vector3 axis, float sign)? turn = null;
            var cam = camera.transform;
            if (Pressed("NudgePartRight")) move = Closest(cam.right);
            else if (Pressed("NudgePartLeft")) move = -Closest(cam.right);
            else if (Pressed("NudgePartUp")) move = Closest(cam.up);
            else if (Pressed("NudgePartDown")) move = -Closest(cam.up);
            else if (Pressed("NudgePartAway")) move = Closest(cam.forward);
            else if (Pressed("NudgePartToward")) move = -Closest(cam.forward);
            else if (Pressed("TurnPartRight")) turn = (Closest(cam.forward), 1f);
            else if (Pressed("TurnPartLeft")) turn = (Closest(cam.forward), -1f);
            else if (Pressed("TipPartAway")) turn = (Closest(cam.right), 1f);
            else if (Pressed("TipPartToward")) turn = (Closest(cam.right), -1f);
            if (move == null && turn == null) return;

            var part = SelectTool.SelectedPart;
            var manager = FindObjectsByType<MovementManager>(FindObjectsSortMode.None).FirstOrDefault(m => m.MovingObj != null && (m.MovingObj == part || part == null));
            var obj = manager != null ? manager.MovingObj : null;
            if (obj == null || obj.IsDeleted()) {
                Interface.Interface.Say("Select a part to move it with the arrow keys", false);
                return;
            }
            var t = obj.transform;
            if (move.HasValue) {
                var step = TransformGizmo.MoveStep;
                manager.NextKind = Motion.Linkage.MoveKind.Translate;
                manager.DisplaceTo(new Displacement(t.position + move.Value * step, t.rotation), 0f);
                Interface.Interface.Say("Moved " + Inch.Format(step) + " (arrow keys: one snap step)");
            }
            else {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                var degrees = keyboard != null && keyboard.shiftKey.isPressed ? 45f : TransformGizmo.TurnStep;
                var (axis, sign) = turn.Value;
                // Clockwise as seen on screen: about the axis pointing into the screen.
                var q = Quaternion.AngleAxis(degrees * sign, axis);
                var center = TransformGizmo.PositionFor(obj);
                manager.NextKind = Motion.Linkage.MoveKind.Rotate;
                manager.DisplaceTo(new Displacement(center + q * (t.position - center), q * t.rotation), 0f);
                Interface.Interface.Say("Turned " + degrees.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "°");
            }
            TransformGizmo.MarkDirty();
        }

        private static bool Pressed(string id) => Keybinds.Pressed(id);

        // The gizmo's axis (its frame: the part's own with Local on) that points most like this way.
        private static Vector3 Closest(Vector3 way) {
            var frame = TransformGizmo.Frame;
            var best = Vector3.zero;
            var bestDot = -2f;
            foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward, Vector3.left, Vector3.down, Vector3.back }) {
                var world = frame * axis;
                var dot = Vector3.Dot(world, way);
                if (dot > bestDot) {
                    bestDot = dot;
                    best = world;
                }
            }
            return best;
        }
    }
}
