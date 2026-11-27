using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Protobot.Interface;
using TMPro;
using Unity.Services.CloudCode;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Online {
    /// <summary>
    /// V2 (build 85): cards in the top right corner for what reaches the app while it's open, so nobody has to restart it
    /// to hear of them:
    /// - "Update available": a new release came out (Presence's heartbeat says what the newest build is, within about a
    ///   minute of it being published; <see cref="Builds.Updater.Live"/> checks it and shows the card).
    /// - a message from the developer, sent from the Discord channel #player-messages (`!notify`: text and OK; `!ask`: a
    ///   question with a text box, whose answer goes back to that channel through Cloud Code "Feedback").
    /// One card at a time, never during a game's run or an online match (they wait until it's over); each message is
    /// shown once (its id is kept in PlayerPrefs <see cref="SeenKey"/>).
    /// </summary>
    public class Notices : MonoBehaviour {
        public const string SeenKey = "V2.Notices.Seen";
        public const int MaxAnswer = 1000;
        private static readonly Color Edge = new(0.157f, 0.522f, 0.957f, 0.7f);

        private class Item {
            public string id, kind, text;          // kind: "update", "ask", "notify"
            public string title, primary, secondary;
            public Action onPrimary, onSecondary;
        }

        private static Notices instance;
        private static readonly List<Item> waiting = new();
        private static RectTransform shown;
        private static Item current;

        /// <summary>For tests: the card showing now (null when none) and the last answer's result.</summary>
        public static string Showing => current?.kind;
        public static string LastResult { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            if (!Application.isPlaying || Application.isBatchMode) return;
            var go = new GameObject("Notices") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Notices>();
        }

        // ------------------------------------------------------------------ what came in

        private static HashSet<string> Seen() =>
            new(PlayerPrefs.GetString(SeenKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));

        private static void MarkSeen(string id) {
            if (string.IsNullOrEmpty(id)) return;
            var list = PlayerPrefs.GetString(SeenKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (list.Contains(id)) return;
            list.Add(id);
            PlayerPrefs.SetString(SeenKey, string.Join(",", list.Skip(Math.Max(0, list.Count - 60))));
            PlayerPrefs.Save();
        }

        /// <summary>Presence's answer: the developer's messages up now (an array of { id, kind, text }).</summary>
        public static void Receive(JArray messages) {
            if (messages == null) return;
            var seen = Seen();
            foreach (var m in messages.OfType<JObject>()) {
                var id = "m" + (string)m["id"];
                var kind = (string)m["kind"];
                var text = (string)m["text"];
                if (string.IsNullOrWhiteSpace(text) || (kind != "ask" && kind != "notify")) continue;
                if (seen.Contains(id) || current?.id == id || waiting.Any(w => w.id == id)) continue;
                Message(id, kind, text);
            }
        }

        /// <summary>A message from the developer: "notify" (text and OK) or "ask" (a question with a text box).</summary>
        public static void Message(string id, string kind, string text) {
            waiting.Add(new Item { id = id, kind = kind, text = text, title = "Message from the developer" });
        }

        /// <summary>The update card: `primary` (Update now / Download) and `secondary` (What's new, may be null).</summary>
        public static void ShowUpdate(string version, string text, string primary, Action onPrimary, string secondary, Action onSecondary) {
            waiting.RemoveAll(w => w.kind == "update");
            if (current?.kind == "update") Close();
            waiting.Insert(0, new Item { id = "update " + version, kind = "update", title = "Update available", text = text,
                primary = primary, onPrimary = onPrimary, secondary = secondary, onSecondary = onSecondary });
        }

        // ------------------------------------------------------------------ showing them

        /// <summary>A game's run or an online match is going: cards wait for it to end.</summary>
        private static bool Busy =>
            (Game.SkillsGame.Active && Motion.Sandbox.Running) || OnlineMatch.Active;

        private void Update() {
            if (shown != null || waiting.Count == 0 || Interface.Interface.Root == null || Busy) return;
            current = waiting[0];
            waiting.RemoveAt(0);
            Build(current);
        }

        private static void Close() {
            if (shown != null) Destroy(shown.gameObject);
            shown = null;
            current = null;
        }

        private static void Build(Item item) {
            var root = Ui.Rect("Notice", Interface.Interface.Root);
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 66;   // over the home menu and the Multiplayer screen, under the badge award
            root.gameObject.AddComponent<GraphicRaycaster>();
            root.anchorMin = root.anchorMax = Vector2.one;
            root.pivot = Vector2.one;
            root.sizeDelta = new Vector2(400f, 10f);
            root.anchoredPosition = new Vector2(-16f, -(Layout.TopBarBottom + 12f));
            shown = root;

            var card = Ui.Image(root, "Card", new Color(0.1f, 0.105f, 0.13f, 0.97f), 12f, true);
            card.rectTransform.Fill();
            var edge = card.gameObject.AddComponent<Outline>();
            edge.effectColor = Edge;
            edge.effectDistance = new Vector2(1.5f, -1.5f);
            var column = Ui.Rect("Column", card.transform).Fill(16, 12, 16, 14);
            Ui.Column(column, 8f);

            // Heading: a dot, what it is, a close button.
            var head = Ui.Rect("Head", column);
            Ui.Row(head, 8f);
            head.Size(-1, 24f);
            var dot = Ui.Image(head, "Dot", Theme.Accent);
            dot.sprite = Theme.Circle;
            dot.Size(10f, 10f);
            var title = Ui.Text(head, "Title", item.title, Theme.FontSmall, Theme.TextDim, Theme.Medium);
            title.Size(-1, 24f, 1f);
            var x = Ui.Button(head, "Close", "×", () => Dismiss(item), false, 6f, Theme.FontBody + 2f);
            x.targetGraphic.color = new Color(1f, 1f, 1f, 0f);
            x.Size(26f, 24f);
            Ui.Tip(x, item.kind == "update" ? "Later" : "Close", null, item.kind == "ask" ? "You won't be asked this again." : null);

            var body = Ui.Text(column, "Text", item.text, Theme.FontBody, Theme.Text);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.overflowMode = TextOverflowModes.Overflow;
            body.Size(-1, Mathf.Ceil(body.GetPreferredValues(item.text, 368f, 0f).y) + 2f);

            var buttons = Ui.Rect("Buttons", column);
            Ui.Row(buttons, 8f, null, TextAnchor.MiddleRight).childForceExpandWidth = false;
            buttons.Size(-1, 34f);
            Ui.Rect("Space", buttons).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            if (item.kind == "ask") {
                var field = Ui.Field(column, "Answer", "", null);
                field.lineType = TMP_InputField.LineType.MultiLineNewline;
                field.characterLimit = MaxAnswer;
                field.textComponent.textWrappingMode = TextWrappingModes.Normal;
                field.textComponent.alignment = TextAlignmentOptions.TopLeft;
                field.textViewport.Fill(8, 6, 8, 6);
                var hint = Ui.Text(field.textViewport, "Placeholder", "Type your answer…", Theme.FontBody, Theme.TextFaint, null, TextAlignmentOptions.TopLeft);
                hint.rectTransform.Fill();
                hint.fontStyle = FontStyles.Italic;
                field.placeholder = hint;
                field.Size(-1, 92f);
                field.transform.SetSiblingIndex(buttons.GetSiblingIndex());

                var foot = Ui.Rect("Foot", column);
                Ui.Row(foot, 10f, null, TextAnchor.UpperLeft);
                foot.Size(-1, 34f);
                foot.SetSiblingIndex(buttons.GetSiblingIndex());
                var privacy = Ui.Text(foot, "Privacy", "Your answer and the name you play under go to the developer. Don't add personal details.",
                    Theme.FontSmall - 2f, Theme.TextFaint, null, TextAlignmentOptions.TopLeft);
                privacy.textWrappingMode = TextWrappingModes.Normal;
                privacy.overflowMode = TextOverflowModes.Overflow;
                privacy.Size(-1, 34f, 1f);
                var count = Ui.Text(foot, "Count", "0 / " + MaxAnswer, Theme.FontSmall - 2f, Theme.TextFaint, null, TextAlignmentOptions.TopRight);
                count.Size(64f, 34f);
                count.GetComponent<LayoutElement>().flexibleWidth = 0f;

                Ui.Button(buttons, "Not now", "Not now", () => Dismiss(item), false, 6f).Size(-1, 32f);
                var send = Ui.Button(buttons, "Send", "Send", null, true, 6f);
                send.Size(-1, 32f);
                send.interactable = false;
                field.onValueChanged.AddListener(v => {
                    count.text = v.Length + " / " + MaxAnswer;
                    send.interactable = !string.IsNullOrWhiteSpace(v);
                });
                send.onClick.AddListener(() => _ = Answer(item, field, send, column));
            }
            else if (item.kind == "update") {
                Ui.Button(buttons, "Later", "Later", () => Dismiss(item), false, 6f).Size(-1, 32f);
                if (item.secondary != null)
                    Ui.Button(buttons, "Secondary", item.secondary, () => item.onSecondary?.Invoke(), false, 6f).Size(-1, 32f);
                Ui.Button(buttons, "Primary", item.primary, () => {
                    Close();
                    item.onPrimary?.Invoke();
                }, true, 6f).Size(-1, 32f);
            }
            else {
                Ui.Button(buttons, "OK", "OK", () => Dismiss(item), true, 6f).Size(-1, 32f);
            }

            Fit(root, column);
            root.gameObject.AddComponent<SlideIn>();
            Audio.Sounds.Play(Audio.Sounds.Pop, 0.35f);
            Builds.ActionLog.Write("notice", item.kind + " " + item.id);
        }

        // The card is as tall as what's in it.
        private static void Fit(RectTransform root, RectTransform column) {
            LayoutRebuilder.ForceRebuildLayoutImmediate(column);
            var height = LayoutUtility.GetPreferredHeight(column) + 26f;
            root.sizeDelta = new Vector2(root.sizeDelta.x, height);
        }

        private static void Dismiss(Item item) {
            if (item.kind != "update") MarkSeen(item.id);
            Close();
        }

        private static async Task Answer(Item item, TMP_InputField field, Button send, RectTransform column) {
            var text = field.text.Trim();
            if (text.Length == 0) return;
            send.interactable = false;
            field.interactable = false;
            send.GetComponentInChildren<TextMeshProUGUI>().text = "Sending…";
            string reason = null;
            try {
                if (!await OnlineServices.Start()) reason = "you're offline";
                else {
                    var json = await CloudCodeService.Instance.CallEndpointAsync("Feedback", new Dictionary<string, object> {
                        { "id", item.id.Substring(1) }, { "answer", text }, { "name", OnlineServices.Name },
                        { "build", Builds.BuildInfo.Build.ToString() }, { "area", Presence.Area },
                    });
                    var result = JObject.Parse(json);
                    if (!(bool?)result["sent"] ?? true) reason = (string)result["reason"] ?? "it wasn't sent";
                }
            }
            catch (Exception e) {
                reason = "you're offline or the server didn't answer";
                Debug.LogWarning("[Online] Feedback: " + e.Message);
            }
            LastResult = reason ?? "sent";
            if (current != item || shown == null) return;   // closed meanwhile
            if (reason != null) {
                Interface.Interface.Say("Your answer wasn't sent: " + reason, false);
                field.interactable = true;
                send.interactable = true;
                send.GetComponentInChildren<TextMeshProUGUI>().text = "Send";
                if (reason.Contains("ended") || reason.Contains("already")) Dismiss(item);
                return;
            }
            MarkSeen(item.id);
            // Thanks, in place of the box and buttons, then it goes.
            for (var i = column.childCount - 1; i >= 2; i--) Destroy(column.GetChild(i).gameObject);
            var thanks = Ui.Text(column, "Thanks", "Sent. Thanks for your answer!", Theme.FontBody, Theme.Success, Theme.Medium);
            thanks.Size(-1, 26f);
            var root = shown;
            await Task.Yield();
            if (root == null) return;
            Fit(root, column);
            root.gameObject.AddComponent<CloseAfter>().seconds = 3f;
        }

        private class SlideIn : MonoBehaviour {
            private float t;
            private CanvasGroup group;
            private Vector2 at;

            private void Start() {
                group = gameObject.AddComponent<CanvasGroup>();
                at = ((RectTransform)transform).anchoredPosition;
                Step();
            }

            private void Update() => Step();

            private void Step() {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / 0.25f);
                var e = 1f - Mathf.Pow(1f - t, 3f);
                group.alpha = e;
                ((RectTransform)transform).anchoredPosition = at + new Vector2(40f * (1f - e), 0f);
                if (t >= 1f) enabled = false;
            }
        }

        private class CloseAfter : MonoBehaviour {
            public float seconds;

            private void Update() {
                seconds -= Time.unscaledDeltaTime;
                if (seconds > 0f) return;
                if (shown == transform) Close();
                else Destroy(gameObject);
            }
        }
    }
}
