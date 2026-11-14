using Protobot.Custom;
using Protobot.Interface;
using Protobot.SelectionSystem;
using UnityEngine;

namespace Protobot.Tools {
    // V2: the Select tool (the cursor, key V): click parts to select them without the transform gizmo. Esc in any
    // tool or menu comes back here. A selected polycarbonate piece or imported part gets an Edit button in the tool
    // options (its sketch, or its holes).
    public class SelectTool : MonoBehaviour {
        private static SelectTool active;
        private static SelectionManager manager;
        private GameObject shownFor;

        [RuntimeInitializeOnLoadMethod]
        private static void RegisterOptions() => ToolRail.RegisterOptions("Select", BuildOptions);

        /// <summary>The part the main selection is on (a selected hole gives its part), or null.</summary>
        public static GameObject SelectedPart {
            get {
                if (manager == null) {
                    var go = GameObject.Find("Primary Selection Manager");
                    manager = go != null ? go.GetComponent<SelectionManager>() : null;
                }
                var selected = manager != null ? manager.current?.gameObject : null;
                if (selected == null) return null;
                if (selected.TryGetComponent(out HoleFace face) && face.hole != null && face.hole.part != null)
                    return face.hole.part.gameObject;
                var saved = selected.GetComponentInParent<SavedObject>();
                return saved != null ? saved.gameObject : null;
            }
        }

        private static void BuildOptions(RectTransform bar) {
            var part = SelectedPart;
            if (part == null) {
                ToolOptions.Note(bar, "Click a part to select it");
                Ui.Hint(bar, "Right-click", "a plastic or imported part to edit it");
                Ui.Hint(bar, "C", "over a hole: screw + nut", "Hold F", "screw or nut", "Hold E", "spacer");  // V2: QuickPlace
                return;
            }
            var custom = PartMenu.CustomOf(part);
            ToolOptions.Note(bar, custom != null ? custom.name : PartLabels.Short(part)).color = Theme.Text;
            if (custom != null) {
                var label = custom.kind == CustomParts.Polycarbonate ? "Edit sketch" : "Edit holes";
                var edit = Ui.Button(bar, "Edit", label, () => PartMenu.Edit(custom), true, 6f, Theme.FontSmall);
                edit.Size(110f, 30f);
                Ui.Tip(edit, label, null, custom.kind == CustomParts.Polycarbonate
                    ? "Opens this piece's sketch; every copy in the build updates when you make it again."
                    : "Opens this part's model to add, pick or remove holes.");
            }
            var mirror = Ui.Button(bar, "Mirror", "Mirror", MirrorTool.Open, false, 6f, Theme.FontSmall);   // V2
            mirror.Size(80f, 30f);
            Ui.Tip(mirror, "Mirror", Keybinds.Label("Mirror"), "Arrows appear on each side of the part: click one to copy it mirrored to that side, drag it out for a gap.");
            Ui.Hint(bar, "Esc", "clear");
        }

        private void OnEnable() {
            active = this;
            shownFor = null;
            ToolOptions.Rebuild();
        }

        private void OnDisable() {
            if (active == this) active = null;
        }

        private void Update() {
            var part = SelectedPart;
            if (part != shownFor) {
                shownFor = part;
                ToolOptions.Rebuild();
            }
        }
    }
}
