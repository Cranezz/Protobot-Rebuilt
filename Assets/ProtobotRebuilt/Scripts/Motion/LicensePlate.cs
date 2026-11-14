using Protobot.Interface;
using Protobot.StateSystems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Motion {
    // V2: a VEX license plate shows the team number, centred on both faces, white and bold like the printed plates
    // teams make (VEX sells blank red and blue plates). Right-click the plate > Team number… to change the text, its
    // size (small / medium / large: how much of the plate's height the text takes) and its font. Kept in the part's
    // saved state ("plate:<text>", "platesize:0-2", "platefont:0-3").
    public class LicensePlate : MonoBehaviour {
        public static readonly string[] SizeNames = { "Small", "Medium", "Large" };
        private static readonly float[] SizeShare = { 0.5f, 0.68f, 0.82f };
        public static readonly string[] FontNames = { "Bold", "Medium", "Regular", "Classic" };

        private TextMeshPro front, back;
        private string shown;

        public static bool Is(GameObject part) => part != null && part.TryGetComponent(out SavedObject saved) && saved.id.Contains("License Plate");

        public string Text => Dynamics.Token(gameObject, "plate") ?? "";
        public int Size => Mathf.Clamp(int.TryParse(Dynamics.Token(gameObject, "platesize"), out var s) ? s : 1, 0, 2);
        public int Font => Mathf.Clamp(int.TryParse(Dynamics.Token(gameObject, "platefont"), out var f) ? f : 0, 0, 3);

        /// <summary>Team numbers: letters, digits, spaces and dashes only (the saved state can't hold ";" or ":").</summary>
        public static string Clean(string text) {
            var sb = new System.Text.StringBuilder();
            foreach (var c in text ?? "")
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-') sb.Append(char.ToUpperInvariant(c));
            return sb.ToString().Trim();
        }

        public void Set(string text, int size, int font) {
            Dynamics.SetToken(gameObject, "plate", string.IsNullOrEmpty(Clean(text)) ? null : Clean(text));
            Dynamics.SetToken(gameObject, "platesize", size == 1 ? null : size.ToString());
            Dynamics.SetToken(gameObject, "platefont", font == 0 ? null : font.ToString());
            Refresh();
        }

        private void LateUpdate() {
            var key = Text + "|" + Size + "|" + Font;
            if (key != shown) Refresh();
            // The text goes with its plate (hidden while it's dragged or deleted).
            if (front != null && front.gameObject.layer != gameObject.layer) {
                front.gameObject.layer = gameObject.layer;
                back.gameObject.layer = gameObject.layer;
            }
        }

        private static TMP_FontAsset FontAsset(int index) => index switch {
            1 => Theme.Medium,
            2 => Theme.Regular,
            3 => TMP_Settings.defaultFontAsset,
            _ => Theme.Bold,
        } ?? TMP_Settings.defaultFontAsset;

        private void Refresh() {
            shown = Text + "|" + Size + "|" + Font;
            if (!TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) return;
            var bounds = filter.sharedMesh.bounds;
            var size = bounds.size;
            // The plate's thin way is its face normal; its long way the text's line.
            var thin = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
            var longWay = Vector3.Dot(size, thin) == size.x ? (size.y >= size.z ? Vector3.up : Vector3.forward)
                : Vector3.Dot(size, thin) == size.y ? (size.x >= size.z ? Vector3.right : Vector3.forward)
                : (size.x >= size.y ? Vector3.right : Vector3.up);
            var upWay = Vector3.Cross(thin, longWay);
            var width = Vector3.Dot(size, longWay) * 0.9f;
            var height = Mathf.Abs(Vector3.Dot(size, upWay)) * SizeShare[Size];
            // V2: on the plate's face itself (it's recessed inside its rim), found by a ray from outside toward the
            // middle; the rim's top (the bounds) left the text floating above the face.
            var half = Vector3.Dot(size, thin) / 2f;
            var frontDepth = FaceDepth(filter.sharedMesh, bounds.center, thin, half) + 0.003f;
            var backDepth = FaceDepth(filter.sharedMesh, bounds.center, -thin, half) + 0.003f;
            front = Face(front, "Plate Text Front", bounds.center + thin * frontDepth, Quaternion.LookRotation(-thin, upWay), width, height);
            back = Face(back, "Plate Text Back", bounds.center - thin * backDepth, Quaternion.LookRotation(thin, upWay), width, height);
        }

        private static readonly System.Collections.Generic.Dictionary<(Mesh, Vector3), float> faceDepths = new();

        // How far the face is from the middle along this way (several rays across the middle, the deepest kept off the
        // rim: the face the text lies on).
        private static float FaceDepth(Mesh mesh, Vector3 center, Vector3 way, float half) {
            if (faceDepths.TryGetValue((mesh, way), out var known)) return known;
            var probe = new GameObject("Plate Probe") { hideFlags = HideFlags.HideAndDontSave };
            var collider = probe.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            var depth = half;
            var found = false;
            var a = Vector3.Cross(way, Mathf.Abs(way.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var b = Vector3.Cross(way, a);
            foreach (var offset in new[] { Vector3.zero, a * 0.3f, -a * 0.3f, b * 0.3f, -b * 0.3f }) {
                var from = center + offset + way * (half + 1f);
                if (!collider.Raycast(new Ray(from, -way), out var hit, half * 2f + 2f)) continue;
                var d = Vector3.Dot(hit.point - center, way);
                if (d <= 0.005f) continue;   // through a hole to the far side
                if (!found || d < depth) depth = d;
                found = true;
            }
            Object.DestroyImmediate(probe);
            if (!found || depth <= 0f) depth = half;
            return faceDepths[(mesh, way)] = depth;
        }

        private TextMeshPro Face(TextMeshPro text, string name, Vector3 position, Quaternion rotation, float width, float height) {
            if (text == null) {
                var go = new GameObject(name) { layer = gameObject.layer };
                go.transform.SetParent(transform, false);
                text = go.AddComponent<TextMeshPro>();
                text.alignment = TextAlignmentOptions.Center;
                text.color = Color.white;
                text.enableAutoSizing = true;
                text.fontSizeMin = 0.1f;
                text.fontSizeMax = 200f;
                text.textWrappingMode = TextWrappingModes.NoWrap;
            }
            text.transform.localPosition = position;
            text.transform.localRotation = rotation;
            text.rectTransform.sizeDelta = new Vector2(width, height);
            text.font = FontAsset(Font);
            text.text = Text;
            text.gameObject.SetActive(Text.Length > 0);
            return text;
        }

        /// <summary>V2: a plate's saved state (its text, size and font tokens), for undo.</summary>
        private class PlateElement : IElement {
            private readonly GameObject part;
            public readonly string State;

            public PlateElement(GameObject part) {
                this.part = part;
                State = part != null && part.TryGetComponent(out SavedObject saved) ? saved.state : null;
            }

            public void Load() {
                if (part != null && part.TryGetComponent(out SavedObject saved)) saved.state = State;   // (LateUpdate redraws it)
            }
        }

        // ------------------------------------------------------------------ the window (right-click > Team number…)

        public static void Edit(GameObject part) {
            var plate = part.GetOrAdd<LicensePlate>();
            var dialog = Dialog.Open("License plate", new Vector2(460f, 330f));
            var column = Ui.Column(dialog.Body, 8f);
            column.childControlHeight = true;
            column.childForceExpandHeight = false;
            string text = plate.Text;
            int size = plate.Size, font = plate.Font;
            void Apply() => plate.Set(text, size, font);

            Dialog.Heading(dialog.Body, "Team number");
            var field = Ui.Field(dialog.Body, "Number", text, value => {
                text = value;
                Apply();
            });
            field.Size(-1, 38f);
            field.characterLimit = 12;
            field.onValueChanged.AddListener(value => { text = value; Apply(); });
            Dialog.Heading(dialog.Body, "Size");
            var sizeRow = Dialog.Row(dialog.Body, "");
            void SizeButtons() {
                foreach (Transform child in sizeRow) Destroy(child.gameObject);
                ToolOptions.Segmented(sizeRow, SizeNames, size, i => { size = i; Apply(); SizeButtons(); });
            }
            SizeButtons();
            Dialog.Heading(dialog.Body, "Font");
            var fontRow = Dialog.Row(dialog.Body, "");
            void FontButtons() {
                foreach (Transform child in fontRow) Destroy(child.gameObject);
                ToolOptions.Segmented(fontRow, FontNames, font, i => { font = i; Apply(); FontButtons(); });
            }
            FontButtons();
            // V2: the window's changes are one undo step (the plate's saved state before and after).
            var before = new PlateElement(part);
            dialog.OnClose = () => {
                var after = new PlateElement(part);
                if (after.State == before.State) return;
                StateSystem.AddElement(before);
                StateSystem.AddState(after);
            };
            var done = Ui.Button(dialog.Footer, "Done", "Done", () => {
                Interface.Interface.Say(plate.Text.Length > 0 ? "License plate: " + plate.Text : "License plate left blank");
                dialog.Close();
            }, true, 6f);
            done.Size(110f, 34f);
        }
    }
}
