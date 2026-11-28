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
            if (InputEvents.TextInput.ShortcutsPaused || Motion.Sandbox.Running || Motion.ChainTool.Active || Motion.ElasticTool.Active || Motion.TubeTool.Active || Motion.StringTool.Active) return;

            var camera = Camera.main;
            if (camera == null || !Physics.Raycast(camera.ScreenPointToRay(pressedAt), out var hit, Mathf.Infinity, LayerMask.GetMask("Default", "HoleCollisions")))
                return;
            var saved = hit.collider.GetComponentInParent<SavedObject>();
            if (saved == null) return;
            // V2: in the Paint tool a right-click lists the part's colors to change.
            if (ToolRail.Current == "Paint") {
                Tools.PaintTool.EditColorsOf(SelectAs.Resolve(hit.collider.gameObject) ?? saved.gameObject, hit.collider.GetComponent<Renderer>());
                return;
            }
            var options = new System.Collections.Generic.List<(string label, System.Action action)>();
            var custom = CustomOf(saved.gameObject);
            if (custom != null)
                options.Add((custom.kind == CustomParts.Polycarbonate ? "Edit sketch" : "Edit holes", () => Edit(custom)));
            // V2: a plastic piece's sketch edited where the piece is, with the robot around it.
            if (custom != null && custom.kind == CustomParts.Polycarbonate) {
                var piece = saved.gameObject;
                options.Add(("Edit sketch on the robot", () => Polycarb.SheetEditor.EditInPlace(custom, piece)));
            }
            if (saved.TryGetComponent(out Motion.UniversalJoint uj) && uj.enabled)
                options.Add(("Bend joint…", () => Motion.UniversalJoint.Edit(uj)));
            // V2: a hinge's angle.
            if (saved.TryGetComponent(out Motion.HingePart hinge) && hinge.enabled)
                options.Add(("Hinge angle…", () => Motion.HingePart.Edit(hinge)));
            // V2: a band's posts, count and wraps; tubing from a fitting's end.
            if (saved.TryGetComponent(out Motion.ElasticBand band))
                options.Add(("Edit band", () => Motion.BandEditor.Select(band)));
            // V2 (build 88): a string's points; a pulley or winch drum starts one.
            if (saved.TryGetComponent(out Motion.StringLine stringLine))
                options.Add(("Edit string", () => Motion.StringEditor.Select(stringLine)));
            if (Motion.StringUi.IsGrooved(saved.gameObject)) {
                var from = saved.gameObject;
                options.Add(("Run string from here", () => Motion.StringTool.BeginFrom(from)));
            }
            var port = Motion.TubeTool.PortNear(saved.gameObject, hit.point);
            if (port != null)
                options.Add(("Run tubing from here", () => Motion.TubeTool.Begin(port)));
            // V2: a license plate's team number.
            if (Motion.LicensePlate.Is(saved.gameObject)) {
                var plate = saved.gameObject;
                options.Add(("Team number…", () => Motion.LicensePlate.Edit(plate)));
            }
            // Moving parts: a cylinder extends and retracts, a motor runs.
            if (saved.TryGetComponent(out Motion.PneumaticCylinder cylinder) && cylinder.enabled) {
                options.Add(cylinder.Extended ? ("Retract", () => cylinder.Set(false)) : ("Extend", () => cylinder.Set(true)));
                options.Add(("Rod position and air…", () => Motion.CylinderDialog.Open(cylinder)));   // V2
            }
            if (saved.TryGetComponent(out Motion.MotorDrive motor)) {
                options.Add(("Device info (speed, torque, keys)", () => Motion.MotorControls.Control(new[] { motor })));
                if (motor.Toggled != 0) options.Add(("Stop", () => motor.Run(0)));
                else {
                    options.Add(("Run forward", () => motor.Run(1)));
                    options.Add(("Run in reverse", () => motor.Run(-1)));
                }
                if (motor.HasCartridge)
                    foreach (var rpm in Motion.MotorDrive.Cartridges)
                        if (rpm != motor.Rpm) options.Add((Motion.MotorDrive.CartridgeName(rpm) + " cartridge (" + rpm + " rpm)", () => motor.SetCartridge(rpm)));
            }
            // V2: a sensor or solenoid: its Brain screen (readings, port).
            else if (Devices.V5Brain.KindOf(saved.gameObject) is Devices.DeviceKind kind && kind != Devices.DeviceKind.Motor)
                options.Add(("Device info (port, readings)", () => Devices.DevicesPanel.Focus(saved.gameObject)));
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
            options.Add(("Mirror…", () => Tools.MirrorTool.OpenFor(part)));   // V2: was in the Select tool
            options.Add(("Save group…", () => Builds.SavedGroups.SaveDialog(part)));   // V2: a sub-assembly to place again
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
