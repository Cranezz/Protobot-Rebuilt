using System;
using TMPro;
using UnityEngine;

namespace Protobot.Interface {
    // V2: CAD-style dimension legs, shared by the saw and the measure tool: from one point to another along three
    // axes (a part's own when both points are on one part, else the world's), each leg in its axis color with its
    // length on it. A leg's label can be clicked to type a new length (the saw moves its plane by it).
    public static class Dimension {
        private static TMP_InputField field;
        private static string fieldKey;
        private static int fieldFrame;

        /// <summary>The axes to measure along: the part's own when both ends are on the same part, else the world's.</summary>
        public static Quaternion FrameFor(GameObject partA, GameObject partB) =>
            partA != null && partA == partB ? partA.transform.rotation : Quaternion.identity;

        /// <summary>How far apart a and b are along each of the frame's axes (signed).</summary>
        public static Vector3 Components(Vector3 a, Vector3 b, Quaternion frame) => Quaternion.Inverse(frame) * (b - a);

        /// <summary>
        /// Draws the legs from a to b (only when they're apart along two or more axes: a straight measurement is
        /// its own leg). With <paramref name="onEdit"/>, clicking a leg's value opens a box to type it; the new
        /// length (inches, positive) goes to onEdit with the axis.
        /// </summary>
        public static void Legs(string key, Vector3 a, Vector3 b, Quaternion frame, Action<int, float> onEdit = null, float alpha = 0.9f) {
            var c = Components(a, b, frame);
            var apart = 0;
            for (var i = 0; i < 3; i++) if (Mathf.Abs(c[i]) > 0.001f) apart++;
            if (apart < 2) {
                if (fieldKey != null && fieldKey.StartsWith(key + ":")) Close();
                return;
            }
            var point = a;
            for (var i = 0; i < 3; i++) {
                if (Mathf.Abs(c[i]) <= 0.001f) continue;
                var next = point + frame * AxisVector(i) * c[i];
                var color = Theme.Axis(i);
                color.a = alpha;
                Overlay.Line(point, next, color, 1.4f);
                if (Overlay.ToScreen(point, out var s0) && Overlay.ToScreen(next, out var s1) && Vector2.Distance(s0, s1) > 24f) {
                    var at = (s0 + s1) / 2f;
                    var axis = i;
                    var legKey = key + ":" + i;
                    if (fieldKey == legKey && field != null) {
                        ((RectTransform)field.transform).anchoredPosition = at;
                        fieldFrame = Time.frameCount;
                    }
                    else {
                        var text = AxisName(i) + " " + Inch.Format(Mathf.Abs(c[i]));
                        var label = Overlay.Text(at, text, Theme.Axis(i), onEdit != null ? () => Open(legKey, at, Mathf.Abs(c[axis]), value => onEdit(axis, value)) : null);
                        if (onEdit != null)
                            Ui.Tip(label, AxisName(i) + " distance", null, "Click to type how far apart along " + AxisName(i) + ".");
                    }
                }
                point = next;
            }
        }

        public static string AxisName(int i) => i == 0 ? "X" : i == 1 ? "Y" : "Z";
        private static Vector3 AxisVector(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        /// <summary>A box over the overlay to type a length (Enter applies it, clicking away cancels).</summary>
        public static void Open(string key, Vector2 at, float value, Action<float> commit) {
            Close();
            fieldKey = key;
            fieldFrame = Time.frameCount;
            field = Ui.Field(Overlay.Instance.transform, "Leg Length", Inch.Format(value, false), text => {
                var keep = field;
                if (Inch.TryParse(text, out var inches)) commit(Mathf.Abs(inches));
                else if (!string.IsNullOrWhiteSpace(text)) Interface.Say("Type a length like 3 1/2 or 3.5", false);
                if (field == keep) Close();
            }, Theme.FontBody, TextAlignmentOptions.Center);
            var rect = (RectTransform)field.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(96f, 28f);
            rect.anchoredPosition = at;
            field.onFocusSelectAll = true;
            field.Select();
            field.ActivateInputField();
        }

        /// <summary>Closes the box if its leg wasn't drawn lately (the measurement went away).</summary>
        public static void Tidy() {
            if (field != null && Time.frameCount - fieldFrame > 2) Close();
        }

        public static void Close() {
            if (field != null) UnityEngine.Object.Destroy(field.gameObject);
            field = null;
            fieldKey = null;
        }
    }
}
