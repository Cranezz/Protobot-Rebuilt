using System;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Online {
    /// <summary>V2: children's privacy law (the US COPPA rule). Before going online the first time, the player gives the
    /// month and year they were born (a neutral question: nothing hints at an answer) and agrees to the Terms and Privacy
    /// Policy; making an account asks again and keeps it on the account (Cloud Code "Birth", so it follows the account to
    /// another computer). Only the month and year are kept, never the day. Players under 13 play online without voice chat
    /// (they don't hear it and aren't heard) and under a made-up name; the age is worked out from the birth month each
    /// time, so voice chat switches on by itself when they turn 13. Neither answer can be changed afterward (so it can't
    /// be moved to get round the rule), and when the computer's and the account's answers differ, the younger one counts.</summary>
    public static class AgeCheck {
        private const string BirthKey = "V2.Birth", TermsKey = "V2.TermsAgreed";
        /// <summary>Raise when the Terms or Privacy Policy change in a way players must agree to again.</summary>
        public const int TermsVersion = 2;   // V2 (build 75): the Privacy Policy now covers usage time (Presence)
        public const string Why = "Children's privacy law (the US COPPA rule) asks us to know if you're under 13: players under 13 play online "
                                  + "without voice chat. Only the month and year are kept (not the day), so voice chat switches on by itself "
                                  + "when you turn 13. It can't be changed later, so check it.";

        public static readonly string[] Months = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };

        /// <summary>This computer's answer, "YYYY-MM" ("" when not asked yet).</summary>
        public static string DeviceBirth => PlayerPrefs.GetString(BirthKey, "");
        public static bool AgeGiven => DeviceBirth.Length > 0;
        public static bool Agreed => PlayerPrefs.GetInt(TermsKey, 0) >= TermsVersion;
        public static bool Ready => AgeGiven && Agreed;

        /// <summary>Under 13 now (by this computer's answer or the signed-in account's, whichever is younger), or not asked
        /// yet (the safe side until the answer is in).</summary>
        public static bool Under13 {
            get {
                if (!AgeGiven) return true;
                if (Age(DeviceBirth) < 13) return true;
                var account = Accounts.Birth;
                return !string.IsNullOrEmpty(account) && Age(account) < 13;
            }
        }

        public static event Action Changed;
        public const string NotReady = "Open Multiplayer and answer the question there first.";

        /// <summary>"YYYY-MM" from a month (0-11) and a year.</summary>
        public static string Birth(int month, int year) => year.ToString("0000") + "-" + (month + 1).ToString("00");

        /// <summary>Whole years old for a "YYYY-MM" birth; the birthday month itself counts as not yet (the safe side:
        /// the day isn't kept). -1 when it can't be read.</summary>
        public static int Age(string birth) {
            if (string.IsNullOrEmpty(birth) || birth.Length < 7 || !int.TryParse(birth.Substring(0, 4), out var year) || !int.TryParse(birth.Substring(5, 2), out var month)) return -1;
            var now = DateTime.Now;
            return now.Year - year - (now.Month <= month ? 1 : 0);
        }

        /// <summary>Problems with a month (0-11, -1 = none chosen) and a year typed; null when it's a possible birth.</summary>
        public static string Check(int month, string year) {
            if (month < 0) return "Choose the month you were born";
            if (!int.TryParse(year, out var y) || y < DateTime.Now.Year - 120 || y > DateTime.Now.Year) return "Type the year you were born (four digits)";
            return null;
        }

        // Testing two copies from the command line: "-age-test adult" or "-age-test kid" answers for this copy.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void FromCommandLine() {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-age-test") {
                    PlayerPrefs.SetString(BirthKey, Birth(0, DateTime.Now.Year - (args[i + 1] == "kid" ? 10 : 30)));
                    PlayerPrefs.SetInt(TermsKey, TermsVersion);
                }
        }

        /// <summary>The month menu and year box (the age question and the account form). <paramref name="changed"/> gets
        /// the month (0-11, -1 = none) and the year typed.</summary>
        public static RectTransform Picker(Transform parent, Action<int, string> changed, float height = 38f, int month = -1, string year = "") {
            var row = Ui.Rect("Birth", parent);
            Ui.Row(row, 10f);
            row.Size(-1, height);
            year ??= "";
            var monthButton = Popup.Choice(row, new[] { month >= 0 && month < 12 ? Months[month] : "Month" }, 0, null, 170f);
            var monthLabel = monthButton.GetComponentInChildren<TextMeshProUGUI>();
            monthButton.onClick.RemoveAllListeners();
            monthButton.onClick.AddListener(() => Popup.Open((RectTransform)monthButton.transform, Months, month, i => {
                month = i;
                monthLabel.text = Months[i];
                changed?.Invoke(month, year);
            }));
            var yearField = Ui.Field(row, "Year", year, v => {
                year = v;
                changed?.Invoke(month, year);
            });
            yearField.contentType = TMP_InputField.ContentType.IntegerNumber;
            yearField.characterLimit = 4;
            yearField.onValueChanged.AddListener(v => {
                year = v;
                changed?.Invoke(month, year);
            });
            // A hint in the empty box (Ui.Field makes none).
            var from = yearField.textComponent.rectTransform;
            var hint = Ui.Text(from.parent, "Placeholder", "Year (like 2010)", Theme.FontBody, Theme.TextFaint);
            hint.rectTransform.anchorMin = from.anchorMin;
            hint.rectTransform.anchorMax = from.anchorMax;
            hint.rectTransform.offsetMin = from.offsetMin;
            hint.rectTransform.offsetMax = from.offsetMax;
            hint.alignment = yearField.textComponent.alignment;
            hint.raycastTarget = false;
            yearField.placeholder = hint;
            yearField.Size(170f, height - 2f);
            return row;
        }

        /// <summary>Runs <paramref name="then"/> once the age is given and the terms agreed (asking first when needed);
        /// <paramref name="cancelled"/> if the player closes the question.</summary>
        public static void Ensure(Action then, Action cancelled = null) {
            if (Ready) {
                then?.Invoke();
                return;
            }
            Ask(then, cancelled);
        }

        private static void Ask(Action then, Action cancelled) {
            var askAge = !AgeGiven;
            var dialog = OnlineWindow.GlassDialog(askAge ? "Before you play online" : "Updated terms", new Vector2(560f, askAge ? 420f : 300f));
            Ui.Column(dialog.Body, 10f);
            var month = -1;
            var year = "";
            if (askAge) {
                var ask = Ui.Text(dialog.Body, "Ask", "When were you born?", Theme.FontTitle, Theme.Text, Theme.Medium);
                ask.Size(-1, 30f);
                Picker(dialog.Body, (m, y) => {
                    month = m;
                    year = y;
                });
                var why = Ui.Text(dialog.Body, "Why", Why, Theme.FontSmall, Theme.TextDim);
                why.textWrappingMode = TextWrappingModes.Normal;
                why.Size(-1, 66f);
            }
            var agree = Ui.Text(dialog.Body, "Agree", askAge
                    ? "By going online you agree to the Terms of Use and the Privacy Policy."
                    : "The Terms of Use or the Privacy Policy changed. Please read them and agree to keep playing online.",
                Theme.FontBody, Theme.Text);
            agree.textWrappingMode = TextWrappingModes.Normal;
            agree.Size(-1, 44f);
            var links = Ui.Rect("Links", dialog.Body);
            Ui.Row(links, 10f);
            links.Size(-1, 34f);
            Ui.Button(links, "Terms", "Read the Terms of Use", () => LegalDocs.Open(LegalDocs.Doc.Terms), false, 6f, Theme.FontSmall).Size(-1, 32f);
            Ui.Button(links, "Privacy", "Read the Privacy Policy", () => LegalDocs.Open(LegalDocs.Doc.Privacy), false, 6f, Theme.FontSmall).Size(-1, 32f);
            var problem = Ui.Text(dialog.Body, "Problem", "", Theme.FontSmall, Theme.Warning);
            problem.Size(-1, 22f);

            var done = false;
            dialog.OnClose += () => {
                if (!done) cancelled?.Invoke();
            };
            Ui.Button(dialog.Footer, "Not now", "Not now", dialog.Close, false, 6f).Size(-1, 34f);
            Ui.Button(dialog.Footer, "Continue", "I agree, continue", () => {
                if (askAge) {
                    var wrong = Check(month, year);
                    if (wrong != null) {
                        problem.text = wrong;
                        return;
                    }
                    var birth = Birth(month, int.Parse(year));
                    PlayerPrefs.SetString(BirthKey, birth);
                    Builds.ActionLog.Write("online", "age check: " + (Age(birth) >= 13 ? "13 or older" : "under 13"));
                }
                PlayerPrefs.SetInt(TermsKey, TermsVersion);
                PlayerPrefs.Save();
                done = true;
                dialog.Close();
                Changed?.Invoke();
                then?.Invoke();
            }, true, 6f).Size(-1, 34f);
        }

        /// <summary>The account's answer arrived or changed (signing in, making an account).</summary>
        public static void AccountChanged() => Changed?.Invoke();
    }
}
