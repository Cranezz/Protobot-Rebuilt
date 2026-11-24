using System.Collections.Generic;
using System.Linq;
using Protobot.InputEvents;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools {
    /// <summary>
    /// V2: copy and paste parts. Ctrl+C copies the selected part with everything screwed to it (what moving it moves: a
    /// wheel with the gear bolted to it, a bracket with its screws and nuts); Ctrl+V starts placing a copy of the group,
    /// like a part from the parts list: the part that was selected follows the mouse and snaps onto screws, shafts and
    /// holes the same way, the rest come along where they were around it. Every click places another; Esc stops. Each
    /// copy keeps its turn, colors and saved edits (cuts, drilled holes, motor settings...). One undo step per paste.
    /// </summary>
    public class PartClipboard : MonoBehaviour {
        private struct Item {
            public string id, state;
            public Vector3 position;        // in the anchor's frame (unscaled)
            public Quaternion rotation;     // relative to the anchor's
            public List<Color> colors;
        }

        private static Item anchor;
        private static readonly List<Item> rest = new();
        private static Quaternion copiedTurn = Quaternion.identity;
        private static Mesh groupMesh;     // the group in the anchor's frame (the placing ghost)
        private static bool pasting;

        public static bool HasCopy => !string.IsNullOrEmpty(anchor.id);
        /// <summary>How many parts the last copy holds (the part and what's screwed to it).</summary>
        public static int Count => HasCopy ? rest.Count + 1 : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create() {
            Keybinds.Add("CopyPart", "Placing and editing", "Copy the selected part (with what's screwed to it)", "Ctrl+C");
            Keybinds.Add("PastePart", "Placing and editing", "Paste: place a copy of it (click to place, Esc stops)", "Ctrl+V");
            var go = new GameObject("Part Clipboard");
            DontDestroyOnLoad(go);
            go.AddComponent<PartClipboard>();
            Placement.Placed += OnPlaced;
        }

        private static void OnPlaced(GameObject part) {
            if (!pasting || part == null || !part.TryGetComponent(out SavedObject saved) || saved.id != anchor.id) return;
            Apply(part, anchor);
            var t = part.transform;
            foreach (var item in rest) {
                var copy = PartsManager.GeneratePart(item.id, t.position + t.rotation * item.position, t.rotation * item.rotation);
                if (copy == null) continue;
                Apply(copy, item);
                Placement.PlacedWith.Add(copy);
            }
            var list = FindAnyObjectByType<PartListOutput>();
            if (list != null) list.CalculatePartsList();
        }

        private static void Apply(GameObject part, Item item) {
            if (!string.IsNullOrEmpty(item.state) && part.TryGetComponent(out SavedObject saved)) {
                saved.state = item.state;
                PartDrilling.ApplySaved(part);
            }
            if (item.colors != null && part.TryGetComponent(out Renderer renderer)) {
                var materials = renderer.materials;
                for (var i = 0; i < materials.Length && i < item.colors.Count; i++)
                    if (materials[i] != null) materials[i].color = item.colors[i];
            }
        }

        private static bool Blocked(Keyboard keyboard) =>
            TextInput.IsTyping || TextInput.ShortcutsPaused || Motion.Sandbox.Running || Game.SkillsGame.Active || Placement.Instance == null;

        private void Update() {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (pasting && !Placement.IsPlacing) pasting = false;
            if (Blocked(keyboard)) return;
            if (Keybinds.Pressed("CopyPart")) Copy(SelectTool.SelectedPart);
            else if (Keybinds.Pressed("PastePart")) Paste();
        }

        // Made by their own tools (they're shaped round what they go on): not copied.
        private static bool Skipped(string id) =>
            id.StartsWith("CHAN") || id.StartsWith("BAND") || id.StartsWith("PTUB") || id.StartsWith("WIRE");

        private static Item Remember(GameObject part, Transform frame) {
            var saved = part.GetComponent<SavedObject>();
            var item = new Item {
                id = saved.id,
                state = saved.state,
                position = Quaternion.Inverse(frame.rotation) * (part.transform.position - frame.position),
                rotation = Quaternion.Inverse(frame.rotation) * part.transform.rotation,
            };
            if (part.TryGetComponent(out Renderer renderer)) {
                item.colors = new List<Color>();
                foreach (var m in renderer.sharedMaterials) item.colors.Add(m != null ? m.color : Color.white);
            }
            return item;
        }

        /// <summary>Copies a part (the selected one from Ctrl+C) and everything screwed to it.</summary>
        public static void Copy(GameObject part) {
            if (part == null || !part.TryGetComponent(out SavedObject saved)) {
                Interface.Interface.Say("Select a part to copy", false);
                return;
            }
            if (Skipped(saved.id)) {
                Interface.Interface.Say("Chains, bands, tubing and cables are made with their own tools: they can't be copied", false);
                return;
            }
            var group = (part.GetConnectedObjects(true, true) ?? new List<GameObject> { part })
                .Where(o => o != null && o != part && o.activeInHierarchy && !o.IsDeleted() && o.TryGetComponent(out SavedObject s) && !Skipped(s.id))
                .Distinct().ToList();
            anchor = Remember(part, part.transform);
            rest.Clear();
            foreach (var o in group) rest.Add(Remember(o, part.transform));
            copiedTurn = part.transform.rotation;
            if (groupMesh != null) Destroy(groupMesh);
            groupMesh = group.Count > 0 ? MeshCombiner.CombineMeshes(new List<GameObject> { part }.Concat(group).ToList(), part.transform.position, part.transform.rotation) : null;
            Audio.Sounds.Play(Audio.Sounds.Tick);
        }

        /// <summary>Starts placing a copy of what was copied.</summary>
        public static void Paste() {
            if (!HasCopy) {
                Interface.Interface.Say("Nothing copied yet: select a part and press " + Keybinds.Label("CopyPart"), false);
                return;
            }
            var type = PartsManager.GetPartType(anchor.id);
            var generator = type != null ? type.GetComponent<PartGenerator>() : null;
            if (generator == null) {
                Interface.Interface.Say("That part can't be pasted", false);
                return;
            }
            PartsManager.SetParams(generator, anchor.id);
            if (Placement.IsPlacing) Placement.Instance.StopPlacing();
            pasting = true;
            var data = new PartPlacementData(generator, type, Placement.Instance.transform);
            if (groupMesh != null) data.display = () => Instantiate(groupMesh);
            Placement.Instance.StartPlacing(data, copiedTurn);
            Audio.Sounds.Play(Audio.Sounds.Tick);
        }
    }
}
