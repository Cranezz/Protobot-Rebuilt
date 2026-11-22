using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: the patch notes (home menu > Patch Notes, Help > Patch notes): each release's notes, newest first, from
    // Resources/Version/PatchNotes.txt (gathered from Release/Release 1.9.N.md by Tools > Protobot > Next Build Number).
    // The original's button opened its own project's page.
    public static class PatchNotes {
        public static void Open() {
            var notes = Resources.Load<TextAsset>("Version/PatchNotes");
            var dialog = Dialog.Open("Patch notes", new Vector2(760f, 620f));
            var body = dialog.Body;
            Ui.Column(body, 8f);
            var version = Ui.Text(body, "Version", "You have " + Builds.BuildInfo.Display, Theme.FontBody, Theme.TextDim);
            version.Size(-1, 24f);
            var frame = Ui.Image(body, "Notes", Theme.Field, 8f);
            frame.Size(-1, -1).GetComponent<LayoutElement>().flexibleHeight = 1f;
            var viewport = frame.rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            Ui.Column(content, 4f, new RectOffset(16, 16, 12, 16));
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            var text = Ui.Text(content, "Text", notes != null ? Rich(notes.text) : "No patch notes in this version.", Theme.FontBody, Theme.Text);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = true;
            text.lineSpacing = 6f;
            var close = Ui.Button(dialog.Footer, "Close", "Close", dialog.Close, true, 6f);
            close.Size(110f, 34f);
        }

        // The notes' markdown, as TMP rich text: "# heading", "- item", **bold**, `code`.
        private static string Rich(string markdown) {
            var lines = markdown.Replace("\r", "").Split('\n');
            var sb = new System.Text.StringBuilder();
            var first = true;
            foreach (var raw in lines) {
                var line = raw.Replace("<", "&lt;");
                line = Regex.Replace(line, @"\*\*(.+?)\*\*", "<b>$1</b>");
                line = Regex.Replace(line, @"`(.+?)`", "<color=#9fc6ff>$1</color>");
                if (line.StartsWith("# ")) {
                    if (!first) sb.Append("\n");
                    sb.Append("<size=125%><b>").Append(line.Substring(2)).Append("</b></size>\n");
                }
                else if (line.StartsWith("- ")) sb.Append("<indent=0>•</indent><indent=1em>").Append(line.Substring(2)).Append("</indent>\n");
                else if (line.StartsWith("  ") && line.Trim().Length > 0) {
                    // a list item's next line: join it on
                    if (sb.Length > 0 && sb[sb.Length - 1] == '\n') {
                        var s = sb.ToString();
                        var end = s.LastIndexOf("</indent>\n");
                        if (end >= 0 && end == s.Length - "</indent>\n".Length) {
                            sb.Length = end;
                            sb.Append(" ").Append(line.Trim()).Append("</indent>\n");
                            continue;
                        }
                    }
                    sb.Append(line.Trim()).Append("\n");
                }
                else if (line.Trim().Length > 0) sb.Append(line).Append("\n");
                first = false;
            }
            return sb.ToString().TrimEnd();
        }
    }
}
