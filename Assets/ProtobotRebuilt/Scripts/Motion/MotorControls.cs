using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Motion {
    // V2: which motors run from their keys in the build. Click a motor (Transform tool) and it's controlled: the
    // Devices panel opens on it (its Brain-style card: speed, torque, gearbox, keys...) and holding its keys runs it;
    // Ctrl+click controls more of them together; a click elsewhere (or the selection let go of) stops controlling.
    // (Its old window is gone: the Devices panel shows everything.)
    public class MotorControls : MonoBehaviour {
        private static MotorControls instance;
        private static readonly List<MotorDrive> controlled = new();

        public static IReadOnlyList<MotorDrive> Controlled => controlled;
        public static bool IsControlled(MotorDrive motor) => controlled.Contains(motor);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init() {
            controlled.Clear();
            instance = null;
        }

        public static void Create(Transform root) {
            if (instance != null) return;
            instance = new GameObject("Motor Controls").AddComponent<MotorControls>();
            instance.transform.SetParent(root, false);
        }

        /// <summary>Controls these motors (their keys run them) and shows them in the Devices panel.</summary>
        public static void Control(IEnumerable<MotorDrive> motors, bool add = false, bool show = true) {
            if (!add) Release();
            foreach (var m in motors)
                if (m != null && !controlled.Contains(m)) controlled.Add(m);
            if (show)
                foreach (var m in controlled) Devices.DevicesPanel.Focus(m.gameObject, m != controlled[^1]);
        }

        public static void Remove(MotorDrive motor) {
            if (motor == null) return;
            controlled.Remove(motor);
            motor.StopToggle();
        }

        public static void Release() {
            foreach (var m in controlled) if (m != null) m.StopToggle();
            controlled.Clear();
        }

        private SelectionSystem.SelectionManager selection;
        private bool hadSelection;

        private SelectionSystem.SelectionManager Selection {
            get {
                if (selection == null)
                    selection = FindObjectsByType<SelectionSystem.ClickSelector>(FindObjectsSortMode.None)
                                .Select(c => c.GetComponent<SelectionSystem.SelectionManager>()).FirstOrDefault(m => m != null);
                return selection;
            }
        }

        // A click on a motor (in the Transform tool) controls it; Ctrl+click adds or removes one; a click elsewhere
        // lets go.
        private void WatchClicks() {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || MouseInput.overUI || Sandbox.Running) return;
            var tool = ToolRail.Current;
            if (tool != "Select" && tool != "Transform") return;
            if (Placement.IsPlacing || InputEvents.TextInput.ShortcutsPaused) return;
            // Not while a tool with its own clicks has the mouse (a cable plugged into a motor's port).
            if (SelectionSystem.SelectionManager.Blocked != null && SelectionSystem.SelectionManager.Blocked()) return;
            var ctrl = Keyboard.current != null && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
            MotorDrive motor = null;
            var camera = Camera.main;
            if (camera != null && Physics.Raycast(camera.ScreenPointToRay(MouseInput.Position), out var hit, Mathf.Infinity, LayerMask.GetMask("Default", "HoleCollisions"))) {
                var target = SelectAs.Resolve(hit.collider.gameObject);
                var saved = target != null ? target.GetComponentInParent<SavedObject>() : null;
                if (saved != null) motor = saved.GetComponent<MotorDrive>();
            }
            if (motor != null) {
                hadSelection = false;  // the click selects it in a moment
                if (ctrl && controlled.Contains(motor)) Remove(motor);
                else if (ctrl) Control(new[] { motor }, true);
                else if (!(controlled.Count == 1 && controlled[0] == motor)) Control(new[] { motor });
                else Devices.DevicesPanel.Focus(motor.gameObject);
            }
            else if (!ctrl && controlled.Count > 0) Release();
        }

        private void Update() {
            WatchClicks();
            controlled.RemoveAll(m => m == null || m.gameObject.IsDeleted());
            // The selection let go of (Esc, a click on nothing): the motors too.
            if (Selection != null) {
                if (Selection.current != null) hadSelection = true;
                else if (hadSelection) {
                    hadSelection = false;
                    if (controlled.Count > 0 && !MotionUi.Capturing && !Sandbox.Running && !Devices.DevicesPanel.MouseOver) Release();
                }
            }
        }
    }
}
