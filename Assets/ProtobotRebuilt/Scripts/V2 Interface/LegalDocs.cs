using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Interface {
    /// <summary>V2: the Terms of Use and the Privacy Policy (Help menu, the online age question, the Account page), from
    /// Resources/Legal (the same text as TERMS.md and PRIVACY.md in the repository, which is where they're published).</summary>
    public static class LegalDocs {
        public enum Doc { Terms, Privacy, Credits }
        public const string RepoUrl = "https://github.com/Cranezz/Protobot-Rebuilt";
        /// <summary>V2: the official Protobot Discord server (guides, help, bug reports, announcements).</summary>
        public const string DiscordUrl = "https://discord.gg/Q7dfw9DqXD";
        public const string Disclaimer = "Protobot Rebuilt V2 is a fan-made tool. It is not affiliated with, sponsored by or endorsed by VEX Robotics, "
                                         + "Innovation First or the REC Foundation. VEX, VEX Robotics and V5 are trademarks of Innovation First, Inc.";

        private static string File(Doc doc) => doc == Doc.Terms ? "Terms" : doc == Doc.Privacy ? "Privacy" : "Credits";
        private static string Title(Doc doc) => doc == Doc.Terms ? "Terms of Use" : doc == Doc.Privacy ? "Privacy Policy" : "Credits and licenses";
        public static string Url(Doc doc) => RepoUrl + "/blob/main/" + (doc == Doc.Terms ? "TERMS.md" : doc == Doc.Privacy ? "PRIVACY.md" : "CREDITS.md");

        public static void Open(Doc doc) {
            var file = Resources.Load<TextAsset>("Legal/" + File(doc));
            var dialog = Dialog.Open(Title(doc), new Vector2(780f, 640f));
            var body = dialog.Body;
            Ui.Column(body, 8f);
            var frame = Ui.Image(body, "Text", Theme.Field, 8f);
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
            var text = Ui.Text(content, "Text", file != null ? PatchNotes.Rich(file.text) : "Read it at " + Url(doc), Theme.FontBody, Theme.Text);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = true;
            text.lineSpacing = 4f;
            Ui.Button(dialog.Footer, "Online", "Open on GitHub", () => Application.OpenURL(Url(doc)), false, 6f).Size(150f, 34f);
            Ui.Button(dialog.Footer, "Close", "Close", dialog.Close, true, 6f).Size(110f, 34f);
        }
    }
}
