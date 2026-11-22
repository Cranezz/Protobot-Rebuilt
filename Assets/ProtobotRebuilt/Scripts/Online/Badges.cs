using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Protobot.Interface;
using TMPro;
using Unity.Services.CloudCode;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Online {
    /// <summary>
    /// V2 (v2.0 Beta 1): badges beside a player's name in the server list and lobbies. A badge belongs to the online
    /// player (a guest, or an account: making an account from a guest keeps it) and lives in its protected Cloud Save
    /// data, which only Cloud Code ("Badges") writes.
    /// "beta" Beta tester: Protobot counts the time it's used (its window in front) while it's a beta version; at 10
    /// minutes the badge is earned here at once (an award card). Only an account can claim it (the owner's rule): it's
    /// given to the account signed in (after the age question and terms: AgeCheck.Ready), or, for a guest, the card says
    /// to make an account and it's given as soon as they make one or sign in on this computer.
    /// </summary>
    public class Badges : MonoBehaviour {
        public const string Beta = "beta";
        public const float BetaSeconds = 600f;
        private const string SecondsKey = "V2.Badges.BetaSeconds", EarnedKey = "V2.Badges.BetaEarned";
        /// <summary>The badges known the last time a player was signed in (the home menu's profile card shows them).</summary>
        public const string CachedKey = "V2.Badges.Last";

        /// <summary>The signed-in player's badges (empty until they're loaded).</summary>
        public static IReadOnlyList<string> Mine => mine;
        private static List<string> mine = new();
        public static event Action Changed;

        /// <summary>Badges as a player/session property ("beta,..."), and back.</summary>
        public static string Text => string.Join(",", mine);
        public static IEnumerable<string> Parse(string text) =>
            (text ?? "").Split(',').Select(b => b.Trim().ToLowerInvariant()).Where(Known);

        public static bool Known(string id) => id == Beta;

        public static string Title(string id) => id == Beta ? "Beta tester" : id;
        public static string About(string id) => id == Beta ? "Played Protobot during its beta." : "";

        /// <summary>The beta's still on: this is a beta version.</summary>
        public static bool BetaOpen => Builds.BuildInfo.Beta > 0;
        public static bool BetaEarnedHere => PlayerPrefs.GetInt(EarnedKey, 0) == 1;
        /// <summary>Signed in to an account now, or was the last time (before Protobot has signed in again).</summary>
        public static bool HasAccount => OnlineServices.SignedIn ? !Accounts.IsGuest : PlayerPrefs.GetString("V2.Profile.Username", "") != "";

        public static float BetaPlayed => PlayerPrefs.GetFloat(SecondsKey, 0f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            if (!Application.isPlaying) return;
            var go = new GameObject("Badges") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            go.AddComponent<Badges>();
        }

        private float unsaved;

        // Signs in quietly a moment after start (once the age question and terms are done), so the profile card and the
        // badges are up to date and a badge earned offline is given.
        private System.Collections.IEnumerator Start() {
            yield return new WaitForSecondsRealtime(4f);
            if (AgeCheck.Ready && !Application.isBatchMode) _ = Claim();
        }

        private void Update() {
            if (!BetaOpen || BetaEarnedHere || !Application.isFocused) return;
            unsaved += Mathf.Min(Time.unscaledDeltaTime, 1f);
            if (unsaved < 10f && BetaPlayed + unsaved < BetaSeconds) return;
            var total = BetaPlayed + unsaved;
            unsaved = 0f;
            PlayerPrefs.SetFloat(SecondsKey, total);
            if (total < BetaSeconds) {
                PlayerPrefs.Save();
                return;
            }
            PlayerPrefs.SetInt(EarnedKey, 1);
            PlayerPrefs.Save();
            Builds.ActionLog.Write("badge", "beta tester earned after " + Mathf.RoundToInt(total) + " s");
            ShowAward(Beta);
            _ = Claim();
        }

        private void OnApplicationQuit() {
            if (unsaved <= 0f || BetaEarnedHere) return;
            PlayerPrefs.SetFloat(SecondsKey, BetaPlayed + unsaved);
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ the online player's badges

        private static bool syncing;

        /// <summary>Loads the signed-in player's badges and gives it the ones earned on this computer (OnlineServices
        /// calls it after every sign-in; the award calls it too).</summary>
        public static async Task Claim() {
            if (syncing || !AgeCheck.Ready) return;
            syncing = true;
            try {
                if (!await OnlineServices.Start()) return;
                // V2: only an account claims the beta badge (a guest is told to make one).
                var award = BetaEarnedHere && BetaOpen && !Accounts.IsGuest ? Beta : "";
                var json = await CloudCodeService.Instance.CallEndpointAsync("Badges", new Dictionary<string, object> { { "award", award } });
                var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                var list = new List<string>();
                if (result != null && result.TryGetValue("badges", out var b) && b is Newtonsoft.Json.Linq.JArray array)
                    list.AddRange(array.Select(x => x.ToString()).Where(Known));
                if (result != null && result.TryGetValue("awarded", out var a) && a is bool given && given)
                    Builds.ActionLog.Write("badge", "given to " + OnlineServices.PlayerId + ": " + award);
                Set(list);
            }
            catch (Exception e) { Debug.LogWarning("[Online] Badges: " + e.Message); }
            finally { syncing = false; }
        }

        /// <summary>A sign-out or a new guest: its badges come with the next Claim.</summary>
        public static void Forget() => Set(new List<string>());

        private static void Set(List<string> list) {
            if (list.SequenceEqual(mine)) return;
            mine = list;
            PlayerPrefs.SetString(CachedKey, Text);
            // In a match lobby: the others see it now.
            if (MatchSessions.Current != null) MatchSessions.Set(MatchSessions.BadgesKey, Text);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ drawing them

        private static readonly Color BetaColor = new(0.55f, 0.36f, 0.96f, 1f);

        /// <summary>A small badge chip ("BETA TESTER") for a row beside a name.</summary>
        public static RectTransform Chip(Transform parent, string id, float height = 20f) {
            var chip = Ui.Image(parent, "Badge " + id, BetaColor, height / 2f);
            var label = Ui.Text(chip.transform, "Label", Title(id).ToUpperInvariant(), height * 0.55f, Color.white, Theme.Bold, TextAlignmentOptions.Center);
            label.characterSpacing = 4f;
            label.rectTransform.Fill();
            label.raycastTarget = false;
            chip.Size(label.GetPreferredValues().x + height, height);
            chip.GetComponent<LayoutElement>().flexibleWidth = 0f;
            Ui.Tip(chip, Title(id), null, About(id));
            return chip.rectTransform;
        }

        /// <summary>A player's badges as chips beside their name (none: nothing added).</summary>
        public static void Chips(Transform parent, string text, int afterIndex = -1) {
            var at = afterIndex;
            foreach (var id in Parse(text).Distinct()) {
                var chip = Chip(parent, id);
                if (at >= 0) chip.SetSiblingIndex(++at);
            }
        }

        /// <summary>A player's badges right after their name in a row where the name takes the spare width: the name
        /// gives up the spare width to a spacer after the chips, so they sit beside it.</summary>
        public static void Beside(TMP_Text name, string text) {
            if (name == null || !Parse(text).Any()) return;
            var layout = name.gameObject.GetOrAdd<LayoutElement>();
            var flexible = layout.flexibleWidth;
            layout.flexibleWidth = 0f;
            name.overflowMode = TextOverflowModes.Ellipsis;
            var index = name.transform.GetSiblingIndex();
            Chips(name.transform.parent, text, index);
            var spacer = Ui.Rect("Spacer", name.transform.parent);
            spacer.SetSiblingIndex(index + 1 + Parse(text).Distinct().Count());
            spacer.Size(0f, 1f, flexible > 0f ? flexible : 1f);
        }

        /// <summary>The award card: the badge, what it's for, a chime; it goes by itself (or on a click).</summary>
        public static void ShowAward(string id) {
            if (Interface.Interface.Root == null) return;
            var root = Ui.Rect("Badge Award", Interface.Interface.Root);
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 70;   // over the home menu and the Multiplayer screen
            root.gameObject.AddComponent<GraphicRaycaster>();
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            var claim = id == Beta && !HasAccount;   // a guest: make an account to claim it
            root.sizeDelta = new Vector2(460f, claim ? 196f : 132f);
            root.anchoredPosition = new Vector2(0f, 120f);
            var card = Ui.Image(root, "Card", new Color(0.1f, 0.1f, 0.14f, 0.96f), 14f, true);
            card.rectTransform.Fill();
            var edge = card.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(BetaColor.r, BetaColor.g, BetaColor.b, 0.8f);
            edge.effectDistance = new Vector2(2f, -2f);
            var column = Ui.Rect("Column", card.transform).Fill(20, 14, 20, 14);
            Ui.Column(column, 6f);
            Ui.Text(column, "Heading", "Badge earned", Theme.FontSmall, Theme.TextDim, Theme.Medium, TextAlignmentOptions.Center).Size(-1, 20f);
            var chipRow = Ui.Rect("Chip row", column);
            chipRow.Size(-1, 32f);
            var layout = chipRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            Chip(chipRow, id, 30f);
            var words = Ui.Text(column, "About", claim ? "Thanks for playing Protobot during its beta! Make an account to claim your badge: it stays with your account and shows beside your name online."
                : id == Beta ? "Thanks for playing Protobot during its beta! The badge shows beside your name online."
                : About(id), Theme.FontSmall, Theme.Text, null, TextAlignmentOptions.Center);
            words.textWrappingMode = TextWrappingModes.Normal;
            words.Size(-1, claim ? 58f : 40f);
            var button = card.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var award = root.gameObject.AddComponent<Award>();
            button.onClick.AddListener(award.Close);
            if (claim) {
                award.stay = 14f;
                var buttons = Ui.Rect("Buttons", column);
                buttons.Size(-1, 36f);
                Ui.Row(buttons, 8f, null, TextAnchor.MiddleCenter);
                var make = Ui.Button(buttons, "Make an account", "Make an account", () => {
                    award.Close();
                    OnlineLaunch.CloseLaunchWindow();
                    OnlineWindow.OpenProfile();
                }, true, 8f, Theme.FontSmall);
                make.Size(180f, 34f);
                var later = Ui.Button(buttons, "Later", "Later", award.Close, false, 8f, Theme.FontSmall);
                later.Size(90f, 34f);
            }
            Audio.Sounds.Play(Audio.Sounds.Fanfare, 0.5f);
        }

        // Slides down, waits, slides back up and goes.
        private class Award : MonoBehaviour {
            private float time, leaving = -1f;
            public float stay = 6f;

            public void Close() {
                if (leaving < 0f) leaving = 0f;
            }

            private void Update() {
                var rect = (RectTransform)transform;
                time += Time.unscaledDeltaTime;
                if (time > stay) Close();
                if (leaving >= 0f) {
                    leaving += Time.unscaledDeltaTime;
                    rect.anchoredPosition = new Vector2(0f, Mathf.Lerp(-24f, 140f, Mathf.SmoothStep(0f, 1f, leaving / 0.35f)));
                    if (leaving > 0.35f) Destroy(gameObject);
                    return;
                }
                rect.anchoredPosition = new Vector2(0f, Mathf.Lerp(140f, -24f, Mathf.SmoothStep(0f, 1f, time / 0.45f)));
            }
        }
    }
}
