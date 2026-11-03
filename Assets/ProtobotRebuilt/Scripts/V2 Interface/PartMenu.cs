using Protobot.Custom;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Interface {
    // V2: right-click (without dragging: a right drag still turns the camera) on a part, in any tool, opens a
    // small menu for it: a polycarbonate piece's sketch or an imported part's holes to edit, a cylinder to extend
    // or retract, a motor to run (and its cartridge), a screw to make a joint, any part to ground.
    public class PartMenu : MonoBehaviour {
        private Vector2 pressedAt;
        private float pressedTime;
        private bool pressed;
        private RectTransform anchor;

        public static void Create(Transform root) => root.gameObject.AddComponent<PartMenu>();

        /// <summary>The custom part (plastic piece or imported model) a scene object is, or null.</summary>
        public static CustomPartData CustomOf(GameObject part) =>
            part != null && part.TryGetComponent(out SavedObject saved) && !string.IsNullOrEmpty(saved.id) ? CustomParts.Find(saved.id) : null;

        public static void Edit(CustomPartData part) {
            if (part == null) return;
            if (part.kind == CustomParts.Polycarbonate)
                Polycarb.SheetEditor.Edit(part);
            else
                ImportDialog.Edit(part);
        }

        private void Update() {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (mouse.rightButton.wasPressedThisFrame && !MouseInput.overUI) {
                pressed = true;
                pressedAt = mouse.position.ReadValue();
                pressedTime = Time.unscaledTime;
            }
            if (!mouse.rightButton.wasReleasedThisFrame || !pressed) return;
            pressed = false;
            if ((mouse.position.ReadValue() - pressedAt).magnitude > 5f || Time.unscaledTime - pressedTime > 0.5f) return;
            if (InputEvents.TextInput.ShortcutsPaused || Motion.Sandbox.Running || Motion.ChainTool.Active) return;

            var camera = Camera.main;
            if (camera == null || !Physics.Raycast(camera.ScreenPointToRay(pressedAt), out var hit, Mathf.Infinity, LayerMask.GetMask("Default", "HoleCollisions")))
                return;
            var saved = hit.collider.GetComponentInParent<SavedObject>();
            if (saved == null) return;
            var options = new System.Collections.Generic.List<(string label, System.Action action)>();
            var custom = CustomOf(saved.gameObject);
            if (custom != null)
                options.Add((custom.kind == CustomParts.Polycarbonate ? "Edit sketch" : "Edit holes", () => Edit(custom)));
            // Moving parts: a cylinder extends and retracts, a motor runs.
            if (saved.TryGetComponent(out Motion.PneumaticCylinder cylinder) && cylinder.enabled)
                options.Add(cylinder.Extended ? ("Retract", () => cylinder.Set(false)) : ("Extend", () => cylinder.Set(true)));
            if (saved.TryGetComponent(out Motion.MotorDrive motor)) {
                options.Add(("Control this motor (keys, speed)", () => Motion.MotorControls.Control(new[] { motor })));
                if (motor.Toggled != 0) options.Add(("Stop", () => motor.Run(0)));
                else {
                    options.Add(("Run forward", () => motor.Run(1)));
                    options.Add(("Run in reverse", () => motor.Run(-1)));
                }
                if (motor.HasCartridge)
                    foreach (var rpm in Motion.MotorDrive.Cartridges)
                        if (rpm != motor.Rpm) options.Add((Motion.MotorDrive.CartridgeName(rpm) + " cartridge (" + rpm + " rpm)", () => motor.SetCartridge(rpm)));
            }
            // V2: chains: made round sprockets, and changed.
            if (saved.TryGetComponent(out Motion.ChainLoop chain))
                options.Add(("Edit chain (sprockets, sides)", () => Motion.ChainTool.Begin(null, chain)));
            else if (Motion.ChainFamily.ForSprocket(saved.gameObject) != null)
                options.Add(("Make a chain from this sprocket", () => Motion.ChainTool.Begin(saved.gameObject)));
            // V2: a screw can be a joint (the parts on it turn about it); any part can be grounded (it stays put).
            var part = saved.gameObject;
            if (part.GetComponent<Screw>() != null && part.GetComponent<Motion.RodThread>() == null)
                options.Add(Motion.Linkage.IsJoint(part) ? ("Fasten tight (not a joint)", () => Motion.Linkage.SetJoint(part, false))
                                                         : ("Make it a joint (parts turn on it)", () => Motion.Linkage.SetJoint(part, true)));
            options.Add(Motion.Linkage.IsGrounded(part) ? ("Unground", () => Motion.Linkage.SetGrounded(part, false))
                                                        : ("Ground (fix in place)", () => Motion.Linkage.SetGrounded(part, true)));
            if (options.Count == 0) return;

            if (anchor == null) anchor = Ui.Rect("Part Menu Anchor", Interface.Root);
            anchor.anchorMin = anchor.anchorMax = Vector2.zero;
            anchor.pivot = new Vector2(0f, 1f);
            anchor.sizeDelta = new Vector2(200f, 0f);
            anchor.anchoredPosition = Layout.FromScreen(pressedAt);
            Popup.Open(anchor, options.ConvertAll(o => o.label).ToArray(), -1, i => options[i].action());
        }
    }
}
