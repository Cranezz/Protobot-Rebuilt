using System.Linq;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Protobot.Online {
    /// <summary>
    /// V2 (v2.0 Beta 1): your profile at the bottom left of the home menu, in its glass look (as the Multiplayer
    /// screen's "You" panel): a round initial, your name, guest or account, your badges. Clicking it opens the Profile
    /// page (Multiplayer > Profile: sign in or make an account, stats, badges). It shows what was known the last time you
    /// were online (kept on this computer) until Protobot signs in again.
    /// </summary>
    public class ProfileCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler {
        private const string UserKey = "V2.Profile.Username";

        private GameObject splash;
        private RectTransform card;
        private Image back;
        private TextMeshProUGUI initial, title, subtitle;
        private RectTransform badges;
        private string shown;

        /// <summary>Made by LaunchRestyle with the home menu's window (shown while it is).</summary>
        public static void Make(GameObject splashPanel) {
            if (Interface.Interface.Root == null || splashPanel == null) return;
            var root = Ui.Rect("Profile Card", Interface.Interface.Root);
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            var home = splashPanel.GetComponentInParent<Canvas>()?.rootCanvas;
            canvas.sortingOrder = (home != null ? home.sortingOrder : 20) + 1;   // over the home menu's canvas
            root.gameObject.AddComponent<GraphicRaycaster>();
            root.anchorMin = root.anchorMax = new Vector2(0f, 0f);
            root.pivot = new Vector2(0f, 0f);
            root.anchoredPosition = new Vector2(40f, 36f);
            root.sizeDelta = new Vector2(330f, 64f);
            var profile = root.gameObject.AddComponent<ProfileCard>();
            profile.splash = splashPanel;
            profile.Build(root);
        }

        private void Build(RectTransform root) {
            back = OnlineWindow.GlassPanel(root, "Card", null, 14f);
            card = back.rectTransform;
            card.Fill();
            var row = Ui.Rect("Row", card).Fill();
            Ui.Row(row, 12f, new RectOffset(12, 14, 8, 8), TextAnchor.MiddleLeft);
            var circle = Ui.Image(row, "Picture", Theme.Accent, 0f);
            circle.sprite = Theme.Circle;
            circle.Size(46f, 46f, 0f);
            initial = Ui.Text(circle.transform, "Initial", "", 22f, Color.white, Theme.Bold, TextAlignmentOptions.Center);
            initial.rectTransform.Fill();
            var column = Ui.Rect("Column", row);
            column.Size(-1, 48f, 1f);
            Ui.Column(column, 2f);
            var top = Ui.Rect("Top", column);
            top.Size(-1, 24f);
            Ui.Row(top, 6f, null, TextAnchor.MiddleLeft);
            title = Ui.Text(top, "Name", "", Theme.FontBody + 1f, Color.white, Theme.Medium);
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.Size(-1, 24f, 0f);
            badges = Ui.Rect("Badges", top);
            badges.Size(-1, 24f, 1f);
            Ui.Row(badges, 4f, null, TextAnchor.MiddleLeft);
            subtitle = Ui.Text(column, "Status", "", Theme.FontSmall - 1f, new Color(1f, 1f, 1f, 0.6f));
            subtitle.Size(-1, 18f);
            Ui.Tip(back, "Profile", null, "Sign in or make an account, and see your stats and badges.");
            Accounts.Changed += Refresh;
            Badges.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy() {
            Accounts.Changed -= Refresh;
            Badges.Changed -= Refresh;
        }

        private void Refresh() {
            if (title == null) return;
            // What we know now (signed in), else what was known the last time.
            if (OnlineServices.SignedIn) {
                PlayerPrefs.SetString(UserKey, Accounts.Username ?? "");
                PlayerPrefs.SetString(Badges.CachedKey, Badges.Text);
            }
            var user = OnlineServices.SignedIn ? Accounts.Username : PlayerPrefs.GetString(UserKey, "");
            var badgeText = OnlineServices.SignedIn ? Badges.Text : PlayerPrefs.GetString(Badges.CachedKey, "");
            var name = OnlineServices.Name;
            var key = name + "|" + user + "|" + badgeText + "|" + Badges.BetaEarnedHere;
            if (key == shown) return;
            shown = key;
            title.text = name;
            initial.text = name.Length > 0 ? char.ToUpperInvariant(name.Trim()[0]).ToString() : "?";
            var list = Badges.Parse(badgeText).ToList();
            // Earned on this computer, not claimed yet: a guest must make an account (an account gets it once online).
            var waiting = Badges.BetaEarnedHere && !list.Contains(Badges.Beta);
            subtitle.text = string.IsNullOrEmpty(user)
                ? (waiting ? "Guest · Make an account to claim your badge" : "Guest · Sign in or make an account")
                : "Signed in · " + user;
            foreach (Transform child in badges) Destroy(child.gameObject);
            foreach (var id in list.Distinct()) Badges.Chip(badges, id, 18f);
            if (waiting) {
                var pending = Badges.Chip(badges, Badges.Beta, 18f);
                foreach (var g in pending.GetComponentsInChildren<Graphic>()) g.color = new Color(g.color.r, g.color.g, g.color.b, 0.45f);
                Ui.Tip(pending.GetComponent<Image>(), "Beta tester", null, string.IsNullOrEmpty(user) ? "Earned! Make an account to claim it." : "Earned! It's added to your account when you're online.");
            }
        }

        private float next;

        private void Update() {
            // Shown with the home menu's window only (not over Credits' recent builds... nor anything else).
            var show = splash != null && splash.activeInHierarchy && !OnlineWindow.IsOpen;
            if (card.gameObject.activeSelf != show) card.gameObject.SetActive(show);
            if (show && Time.unscaledTime > next) {
                next = Time.unscaledTime + 1f;
                Refresh();   // (a name changed elsewhere)
            }
        }

        public void OnPointerEnter(PointerEventData e) => back.color = new Color(0.16f, 0.18f, 0.22f, 0.72f);
        public void OnPointerExit(PointerEventData e) => back.color = OnlineWindow.Glass;

        public void OnPointerClick(PointerEventData e) {
            Audio.Sounds.Play(Audio.Sounds.Tick);
            back.color = OnlineWindow.Glass;
            OnlineLaunch.CloseLaunchWindow();
            OnlineWindow.OpenProfile();
        }
    }
}
