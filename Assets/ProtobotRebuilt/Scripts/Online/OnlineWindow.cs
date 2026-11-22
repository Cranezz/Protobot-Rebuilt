using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Builds;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Online {
    /// <summary>V2: the Multiplayer screen (home menu > Multiplayer, Play game > Play online…, File > Play online…): a
    /// full-screen page like a game's menu, over a blurred picture of the field (Resources/Online/Backgrounds, blurred
    /// when it opens), with see-through "glass" panels: the server list (join with an invite code), the skills
    /// leaderboard, hosting a match, and the match lobby (the invite code to copy, players with their alliance, robot and
    /// ready, the host's Start).</summary>
    public static class OnlineWindow {
        private static OnlineScreen screen;
        public static bool IsOpen => screen != null;

        public static void Open() {
            if (screen == null) screen = OnlineScreen.Create();
            screen.Show(MatchSessions.Current != null ? OnlineScreen.Page.Lobby : OnlineScreen.Page.Servers);
            // V2: the age question and the terms, the first time (closing it goes back to the menu).
            AgeCheck.Ensure(() => { if (screen != null) screen.Refresh(true); }, () => { if (screen != null) screen.BackToMenu(); });
        }

        public static void OpenLobby() => Open();

        /// <summary>V2: straight to the skills leaderboard (back from a replay watched from it).</summary>
        public static void OpenLeaderboard() {
            if (screen == null) screen = OnlineScreen.Create();
            screen.Show(OnlineScreen.Page.Leaderboard);
            AgeCheck.Ensure(() => { if (screen != null) screen.Refresh(true); }, () => { if (screen != null) screen.BackToMenu(); });
        }

        /// <summary>V2: the Profile page (the home menu's profile card).</summary>
        public static void OpenProfile() {
            if (screen == null) screen = OnlineScreen.Create();
            screen.Show(OnlineScreen.Page.Account);
            AgeCheck.Ensure(() => { if (screen != null) screen.Refresh(true); }, () => { if (screen != null) screen.BackToMenu(); });
        }

        public static void Close() {
            if (screen != null) screen.Close();
        }

        // ------------------------------------------------------------------ glass

        public static readonly Color Glass = new(0.07f, 0.08f, 0.1f, 0.5f);
        public static readonly Color GlassDeep = new(0f, 0f, 0f, 0.28f);
        public static readonly Color GlassRow = new(1f, 1f, 1f, 0.05f);
        public static readonly Color GlassEdge = new(1f, 1f, 1f, 0.1f);

        public static Image GlassPanel(Transform parent, string name, Color? color = null, float radius = 14f) {
            var image = Ui.Image(parent, name, color ?? Glass, radius, true);
            var edge = image.gameObject.AddComponent<Outline>();
            edge.effectColor = GlassEdge;
            edge.effectDistance = new Vector2(1f, -1f);
            return image;
        }

        /// <summary>A small window (password, another version) in the screen's glass look.</summary>
        public static Dialog GlassDialog(string title, Vector2 size) {
            var dialog = Dialog.Open(title, size);
            if (IsOpen) {
                dialog.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);
                var panel = dialog.Panel.GetComponent<Image>();
                panel.color = new Color(0.08f, 0.09f, 0.11f, 0.86f);
                var edge = panel.gameObject.AddComponent<Outline>();
                edge.effectColor = GlassEdge;
                edge.effectDistance = new Vector2(1f, -1f);
            }
            return dialog;
        }

        /// <summary>V2: a crossed-out speaker by a player's name: they can't hear voice chat (under 13, or voice chat off),
        /// so teammates know to tell them things another way.</summary>
        public static RectTransform CantHearIcon(Transform parent, string tip, float size = 22f, Color? color = null) {
            var holder = Ui.Image(parent, "Can't hear", new Color(0f, 0f, 0f, 0f), 0f);
            holder.Size(size, size);
            var rect = Ui.Rect("Icon", holder.transform).Fill();
            rect.gameObject.AddComponent<CanvasRenderer>();
            var icon = rect.gameObject.AddComponent<CantHearGraphic>();
            icon.raycastTarget = false;
            icon.color = color ?? new Color(1f, 1f, 1f, 0.75f);
            Ui.Tip(holder, "Can't hear voice chat", null, tip + ": tell them things another way (the lobby, or in person).");
            return holder.rectTransform;
        }

        // ------------------------------------------------------------------ small windows

        public static void PasswordDialog(Action<string> then) {
            var dialog = GlassDialog("Password", new Vector2(420f, 220f));
            Ui.Column(dialog.Body, 8f);
            var password = "";
            var note = Ui.Text(dialog.Body, "Note", "This match has a password. Ask the host for it.", Theme.FontBody, Theme.TextDim);
            note.Size(-1, 30f);
            var field = Ui.Field(dialog.Body, "Password", "", v => password = v);
            field.contentType = TMP_InputField.ContentType.Password;
            field.onValueChanged.AddListener(v => password = v);
            field.Size(-1, 36f);
            void Go() {
                dialog.Close();
                then(password);
            }
            field.onSubmit.AddListener(_ => Go());
            field.ActivateInputField();
            Ui.Button(dialog.Footer, "Cancel", "Cancel", dialog.Close, false, 6f).Size(-1, 34f);
            Ui.Button(dialog.Footer, "Join", "Join", Go, true, 6f).Size(-1, 34f);
        }

        public static void VersionDialog(string problem) {
            var dialog = GlassDialog("Another version", new Vector2(480f, 240f));
            Ui.Column(dialog.Body, 8f);
            var text = Ui.Text(dialog.Body, "Text", problem + "\n\nEveryone in a match needs the same version, so the field, the parts and the physics match.", Theme.FontBody, Theme.Text);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.Size(-1, 120f);
            if (!string.IsNullOrEmpty(MatchSessions.DownloadUrl))
                Ui.Button(dialog.Footer, "Download", "Download the latest version", () => Application.OpenURL(MatchSessions.DownloadUrl), true, 6f).Size(-1, 34f);
            Ui.Button(dialog.Footer, "OK", "OK", dialog.Close, string.IsNullOrEmpty(MatchSessions.DownloadUrl), 6f).Size(-1, 34f);
        }

        public static string Spaced(string code) => string.IsNullOrEmpty(code) ? "…" : code.Length == 6 ? code.Substring(0, 3) + " " + code.Substring(3) : code;

        public static string InviteText(Unity.Services.Multiplayer.ISession session) =>
            "Join my Protobot match \"" + session.Name + "\": open Protobot, Multiplayer, and enter the code " + session.Code + " (" + BuildInfo.Display + ")";
    }

    /// <summary>The screen itself (see OnlineWindow).</summary>
    public class OnlineScreen : MonoBehaviour {
        public enum Page { Servers, Leaderboard, Host, Lobby, Account }

        private Page page;
        private RectTransform root, nav, card, content;
        private RawImage background;
        private TextMeshProUGUI status;
        private bool dirty, busy;

        // The server list.
        private List<MatchInfo> matches = new();
        private string listProblem;
        private bool loading;
        private float refreshAt;
        private string code = "";
        // Hosting.
        private MatchSessions.HostOptions hostOptions;
        // The lobby.
        private float copiedUntil;

        public static OnlineScreen Create() {
            var rect = Ui.Rect("Multiplayer Screen", Interface.Interface.Root).Fill();
            rect.SetAsLastSibling();
            var blocker = rect.gameObject.AddComponent<Image>();
            blocker.color = new Color(0.05f, 0.06f, 0.07f, 1f);
            blocker.raycastTarget = true;
            var screen = rect.gameObject.AddComponent<OnlineScreen>();
            screen.root = rect;
            screen.Build();
            return screen;
        }

        private void Build() {
            // The picture, blurred, slowly drifting; darker on the left where the menu is.
            var holder = Ui.Rect("Background", root).Fill();
            holder.gameObject.AddComponent<RectMask2D>();
            background = Ui.Rect("Picture", holder).gameObject.AddComponent<RawImage>();
            background.raycastTarget = false;
            var picture = Backgrounds.Blurred();
            background.texture = picture;
            var fit = background.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = picture != null ? (float)picture.width / picture.height : 16f / 9f;
            background.gameObject.AddComponent<BackgroundDrift>();
            var shade = Ui.Rect("Shade", root).Fill().gameObject.AddComponent<RawImage>();
            shade.texture = Backgrounds.Shade();
            shade.raycastTarget = false;

            // Left: the title and the pages.
            var left = Ui.Rect("Menu", root);
            left.anchorMin = new Vector2(0f, 0f);
            left.anchorMax = new Vector2(0f, 1f);
            left.pivot = new Vector2(0f, 1f);
            left.offsetMin = new Vector2(56f, 120f);
            left.offsetMax = new Vector2(56f + 300f, -56f);
            Ui.Column(left, 10f);
            var title = Ui.Text(left, "Title", "Multiplayer", 46f, Color.white, Theme.Medium);
            title.Size(-1, 58f);
            var sub = Ui.Text(left, "Sub", "V5RC Override · 2026-27", Theme.FontBody, new Color(1f, 1f, 1f, 0.7f));
            sub.Size(-1, 26f);
            var gap = Ui.Rect("Gap", left);
            gap.Size(-1, 22f);
            nav = Ui.Rect("Pages", left);
            Ui.Column(nav, 8f);
            nav.Size(-1, 300f);

            // Right: the page.
            card = OnlineWindow.GlassPanel(root, "Card").rectTransform;
            card.anchorMin = new Vector2(0f, 0f);
            card.anchorMax = new Vector2(1f, 1f);
            card.offsetMin = new Vector2(56f + 300f + 40f, 104f);
            card.offsetMax = new Vector2(-56f, -56f);
            content = Ui.Rect("Content", card).Fill(28, 24, 28, 24);
            Ui.Column(content, 10f);

            // Bottom: you, and the version.
            var you = OnlineWindow.GlassPanel(root, "You", null, 12f).rectTransform;
            you.anchorMin = you.anchorMax = new Vector2(0f, 0f);
            you.pivot = new Vector2(0f, 0f);
            you.anchoredPosition = new Vector2(56f, 40f);
            you.sizeDelta = new Vector2(300f, 48f);
            Ui.Row(you, 8f, new RectOffset(14, 8, 6, 6));
            var dot = Ui.Image(you, "Status", Theme.TextFaint, 5f);
            dot.Size(10f, 10f);
            var name = Ui.Field(you, "Name", OnlineServices.Name, v => {
                OnlineServices.Name = v;
                if (MatchSessions.Current != null) MatchSessions.Set(MatchSessions.NameKey, OnlineServices.Name);
            });
            name.characterLimit = 24;
            nameField = name;
            name.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            name.Size(-1, 34f, 1f);
            Ui.Tip(name, "Your name", null, "What other players see in the server list and the lobby.");
            name.onEndEdit.AddListener(v => {
                // V2: names with offensive words aren't taken.
                if (!AgeCheck.Under13 && !string.IsNullOrWhiteSpace(v) && !NameFilter.Allowed(v)) {
                    Interface.Interface.Say("That name isn't allowed: choose another", false);
                    name.SetTextWithoutNotify(OnlineServices.Name);
                }
            });
            // V2 (COPPA): players under 13 get a made-up name; this picks another.
            rerollButton = Ui.Button(you, "New name", "New", () => {
                OnlineServices.NewKidName();
                name.SetTextWithoutNotify(OnlineServices.Name);
                if (MatchSessions.Current != null) MatchSessions.Set(MatchSessions.NameKey, OnlineServices.Name);
                Audio.Sounds.Play(Audio.Sounds.Tick);
            }, false, 6f, Theme.FontSmall);
            rerollButton.Size(50f, 30f);
            Ui.Tip(rerollButton, "New name", null, "Players under 13 get a made-up name, so nothing personal is shown. Click for another.");
            name.interactable = !AgeCheck.Under13;
            rerollButton.gameObject.SetActive(AgeCheck.AgeGiven && AgeCheck.Under13);
            status = Ui.Text(you, "State", "", Theme.FontSmall, new Color(1f, 1f, 1f, 0.6f), null, TextAlignmentOptions.MidlineRight);
            status.Size(54f, 34f);
            var version = Ui.Text(root, "Version", BuildInfo.Display, Theme.FontSmall, new Color(1f, 1f, 1f, 0.55f), null, TextAlignmentOptions.BottomRight);
            version.rectTransform.anchorMin = version.rectTransform.anchorMax = new Vector2(1f, 0f);
            version.rectTransform.pivot = new Vector2(1f, 0f);
            version.rectTransform.anchoredPosition = new Vector2(-56f, 52f);
            version.rectTransform.sizeDelta = new Vector2(400f, 24f);

            // Make sure messages show over the screen.
            var feedback = FindFirstObjectByType<Protobot.UI.FeedbackDisplay>(FindObjectsInactive.Include);
            if (feedback != null) {
                var canvas = feedback.gameObject.GetOrAdd<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = 60;
            }

            // The 3D view is covered: draw nothing in it.
            ViewHider.Set(this, true);
            MatchSessions.Changed += MarkDirty;
            MatchSessions.Ended += OnEnded;
            Accounts.Changed += ShowName;
            Badges.Changed += MarkDirty;   // V2: badges loaded or earned
            AgeCheck.Changed += AgeChanged;
            ActionLog.Write("window", "Multiplayer screen");
            _ = OnlineServices.Start();
        }

        private TMP_InputField nameField;
        private Button rerollButton;

        // V2: signing in (or making an account) changes the name to the account's: shown at once bottom left.
        private void ShowName() {
            if (nameField != null && !nameField.isFocused) nameField.SetTextWithoutNotify(OnlineServices.Name);
            if (MatchSessions.Current != null) MatchSessions.Set(MatchSessions.NameKey, OnlineServices.Name);
            if (hostOptions != null && hostOptions.name.EndsWith("'s match")) hostOptions.name = OnlineServices.Name + "'s match";
            MarkDirty();
        }

        private void OnDestroy() {
            Accounts.Changed -= ShowName;
            Badges.Changed -= MarkDirty;
            AgeCheck.Changed -= AgeChanged;
            MatchSessions.Changed -= MarkDirty;
            MatchSessions.Ended -= OnEnded;
            ViewHider.Set(this, false);
        }

        public void Close() {
            if (this != null) Destroy(gameObject);
        }

        private void MarkDirty() => dirty = true;

        private void OnEnded(string reason) {
            if (!string.IsNullOrEmpty(reason)) Interface.Interface.Say(reason, false);
            Show(Page.Servers);
        }

        public void Show(Page next) {
            page = next;
            transform.SetAsLastSibling();
            if (page == Page.Servers && !loading && (refreshAt <= 0f || Time.unscaledTime > refreshAt - 10f)) Refresh();
            Rebuild();
        }

        private void Update() {
            var t = Time.unscaledTime;
            WatchRobot();
            if (status != null) {
                var online = OnlineServices.SignedIn;
                status.text = online ? "Online" : OnlineServices.Problem.Length > 0 ? "Offline" : "…";
                status.transform.parent.Find("Status").GetComponent<Image>().color = online ? Theme.Success : OnlineServices.Problem.Length > 0 ? Theme.Danger : Theme.TextFaint;
            }
            if (page == Page.Servers && !loading && refreshAt > 0f && t > refreshAt && !InputEvents.TextInput.IsTyping) Refresh();
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && Dialog.OpenCount == 0 && !Popup.IsOpen && !InputEvents.TextInput.IsTyping) {
                if (page == Page.Host || page == Page.Leaderboard || page == Page.Account) Show(Page.Servers);
                else if (page == Page.Servers) BackToMenu();
            }
        }

        private void LateUpdate() {
            if (dirty && !InputEvents.TextInput.IsTyping) {
                dirty = false;
                Rebuild();
            }
        }

        /// <summary>V2: Report and Vote kick for another player (the lobby's rows and the match's Players window).</summary>
        public static void PlayerButtons(Transform row, string id, string who) {
            var report = Ui.Button(row, "Report", "Report", () => PlayerReports.Open(id, who), false, 8f, Theme.FontSmall);
            report.targetGraphic.color = new Color(1f, 1f, 1f, 0.08f);
            report.Size(-1, 30f);
            Ui.Tip(report, "Report " + who, null, "Tell the moderators about cheating or unfair play. The match record goes with it.");
            var session = MatchSessions.Current;
            if (session == null || id == session.Host) return;
            var votes = MatchSessions.KickVotes(id);
            var needed = MatchSessions.KickVotesNeeded();
            var voted = MatchSessions.VotedFor(id);
            var vote = Ui.Button(row, "Vote kick", (voted ? "Take back vote" : "Vote kick") + (votes > 0 ? "  " + votes + "/" + needed : ""),
                () => MatchSessions.VoteKick(id), false, 8f, Theme.FontSmall);
            vote.targetGraphic.color = voted ? new Color(0.9f, 0.3f, 0.3f, 0.35f) : new Color(1f, 1f, 1f, 0.08f);
            vote.Size(-1, 30f);
            vote.interactable = MatchSessions.CanVoteKick(id);
            Ui.Tip(vote, "Vote kick", null, session.Players.Count >= 3
                ? "They're removed when " + needed + " players vote (more than half of the others), and can't come back to this match."
                : "Needs 3 players or more (with two, leave the match, or the host can remove them).");
        }

        // V2: the age answer came in or changed (an account's birthday): the name, voice and account page follow.
        private void AgeChanged() {
            if (MatchSessions.Current != null) {
                MatchSessions.Set(MatchSessions.NameKey, OnlineServices.Name);
                MatchSessions.Set(MatchSessions.VoiceKey, Voice.CanHear ? "1" : "0");
            }
            Refresh(false);
            MarkDirty();
        }

        /// <summary>V2: rebuilds the screen after the age question (the name field and the account page change).</summary>
        public void Refresh(bool all) {
            if (nameField != null) {
                nameField.SetTextWithoutNotify(OnlineServices.Name);
                nameField.interactable = !AgeCheck.Under13;
                if (rerollButton != null) rerollButton.gameObject.SetActive(AgeCheck.AgeGiven && AgeCheck.Under13);
            }
            if (all) Rebuild();
        }

        // V2: open a build in the lobby; the robot check below follows whatever build is open.
        private void ChooseRobot() {
            var manager = FindAnyObjectByType<BuildsManager>();
            if (manager == null) return;
            if (MatchSessions.Get(MatchSessions.Current?.CurrentPlayer, MatchSessions.ReadyKey) == "1") MatchSessions.Set(MatchSessions.ReadyKey, "0");
            manager.OpenBuild();
            robotSignature = null;
        }

        private string robotSignature;
        private float robotCheckAt;

        // V2: while in a lobby, the robot shown to the others is the build that's open now (checked when it changes):
        // a build that can't play clears it, and takes back Ready.
        private void WatchRobot() {
            var session = MatchSessions.Current;
            if (session?.CurrentPlayer == null || OnlineMatch.Current != null || Time.unscaledTime < robotCheckAt) return;
            robotCheckAt = Time.unscaledTime + 0.5f;
            var manager = FindAnyObjectByType<BuildsManager>();
            var count = FindObjectsByType<SavedObject>(FindObjectsSortMode.None).Length;
            var signature = (manager != null ? manager.buildPath : "") + "|" + count;
            if (signature == robotSignature) return;
            robotSignature = signature;
            var name = MatchSessions.RobotName();
            var shown = MatchSessions.Get(session.CurrentPlayer, MatchSessions.RobotKey);
            if (name != shown) MatchSessions.Set(MatchSessions.RobotKey, name);
            if (string.IsNullOrEmpty(name) && MatchSessions.Get(session.CurrentPlayer, MatchSessions.ReadyKey) == "1") MatchSessions.Set(MatchSessions.ReadyKey, "0");
            if (string.IsNullOrEmpty(name) && count > 0 && !string.IsNullOrEmpty(shown)) Interface.Interface.Say(Game.SkillsGame.RobotProblem(), false);
        }

        public void BackToMenu() {
            // V2: leaving the Multiplayer screen leaves the lobby too (a match being played closes the screen itself).
            if (MatchSessions.Current != null && OnlineMatch.Current == null) _ = MatchSessions.Leave(null);
            Close();
            var splash = Resources.FindObjectsOfTypeAll<Button>().FirstOrDefault(b => b.name == "Splash Screen Button" && b.gameObject.scene.IsValid());
            if (splash != null) splash.onClick.Invoke();
        }

        // ------------------------------------------------------------------ the pages

        private void Rebuild() {
            for (var i = nav.childCount - 1; i >= 0; i--) Destroy(nav.GetChild(i).gameObject);
            for (var i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
            if (page == Page.Lobby && MatchSessions.Current == null) page = Page.Servers;
            if (page == Page.Lobby) {
                NavButton("Match lobby", true, null);
                NavButton("Leave match", false, () => {
                    _ = MatchSessions.Leave(null);
                    Show(Page.Servers);
                });
                Lobby();
                return;
            }
            NavButton("Server list", page == Page.Servers, () => Show(Page.Servers));
            NavButton("Skills leaderboard", page == Page.Leaderboard, () => {
                board = null;
                Show(Page.Leaderboard);
            });
            NavButton("Profile", page == Page.Account, () => {   // V2: was Account
                stats = null;
                Show(Page.Account);
            });
            NavButton("Host a match", page == Page.Host, () => {
                hostOptions = new MatchSessions.HostOptions { name = OnlineServices.Name + "'s match", mode = 0, maxPlayers = 4 };
                Show(Page.Host);
            });
            var gap = Ui.Rect("Gap", nav);
            gap.Size(-1, 26f);
            NavButton("Back to menu", false, BackToMenu);
            if (page == Page.Servers) Servers();
            else if (page == Page.Leaderboard) Leaderboard();
            else if (page == Page.Account) Account();
            else Host();
        }

        private void NavButton(string label, bool selected, Action click) {
            var button = Ui.Button(nav, label, label, click, false, 10f, Theme.FontTitle);
            button.Size(-1, 50f);
            var image = (Image)button.targetGraphic;
            image.color = selected ? new Color(0.157f, 0.522f, 0.957f, 0.42f) : new Color(1f, 1f, 1f, 0.06f);
            var text = button.GetComponentInChildren<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.color = selected ? Color.white : new Color(1f, 1f, 1f, 0.85f);
            var layout = button.GetComponent<HorizontalLayoutGroup>();
            if (layout != null) {
                layout.padding = new RectOffset(18, 12, 0, 0);
                layout.childAlignment = TextAnchor.MiddleLeft;
            }
            if (selected) {
                var bar = Ui.Image(button.transform, "Bar", Theme.Accent, 2f);
                bar.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                bar.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(4f, 26f));
            }
            if (click == null) button.interactable = false;
        }

        private TextMeshProUGUI Heading(string text, string sub = null) {
            var title = Ui.Text(content, "Heading", text, 28f, Color.white, Theme.Medium);
            title.Size(-1, 38f);
            if (sub != null) {
                var line = Ui.Text(content, "Sub", sub, Theme.FontBody, new Color(1f, 1f, 1f, 0.65f));
                line.textWrappingMode = TextWrappingModes.Normal;
                line.Size(-1, 24f);
            }
            return title;
        }

        private RectTransform Row(float height = 40f) {
            var row = Ui.Rect("Row", content);
            Ui.Row(row, 10f);
            row.Size(-1, height);
            return row;
        }

        // The row's last widget (a segmented choice) keeps its width instead of stretching across the card.
        private static void Fixed(RectTransform row, float width) {
            var last = row.GetChild(row.childCount - 1);
            var layout = last.gameObject.GetOrAdd<LayoutElement>();
            layout.preferredWidth = layout.minWidth = width;
            layout.flexibleWidth = 0f;
        }

        private TMP_InputField DarkField(Transform parent, string name, string text, Action<string> changed, float size = Theme.FontBody) {
            var field = Ui.Field(parent, name, text, changed, size);
            field.onValueChanged.AddListener(v => changed(v));
            field.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);
            return field;
        }

        private RectTransform ScrollList(float height) {
            var frame = Ui.Image(content, "List", OnlineWindow.GlassDeep, 10f, true);
            frame.Size(-1, height);
            frame.GetComponent<LayoutElement>().flexibleHeight = 1f;
            var viewport = frame.rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();
            var column = Ui.Rect("Column", viewport);
            column.anchorMin = new Vector2(0f, 1f);
            column.anchorMax = new Vector2(1f, 1f);
            column.pivot = new Vector2(0.5f, 1f);
            column.sizeDelta = Vector2.zero;
            Ui.Column(column, 4f, new RectOffset(8, 8, 8, 8));
            column.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = column;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            return column;
        }

        // ---- server list

        private async void Refresh() {
            if (loading) return;
            loading = true;
            listProblem = null;
            dirty = true;
            var (list, error) = await MatchSessions.List();
            if (this == null) return;
            loading = false;
            matches = list;
            listProblem = error;
            refreshAt = Time.unscaledTime + 15f;
            dirty = true;
        }

        private void Servers() {
            var head = Row(42f);
            var title = Ui.Text(head, "Heading", "Server list", 28f, Color.white, Theme.Medium);
            title.Size(-1, 40f, 1f);
            var refresh = Ui.Button(head, "Refresh", loading ? "Loading…" : "Refresh", Refresh, false, 8f, Theme.FontSmall);
            refresh.targetGraphic.color = new Color(1f, 1f, 1f, 0.08f);
            refresh.Size(-1, 32f);
            refresh.interactable = !loading;

            var join = Row(44f);
            Ui.Text(join, "Label", "Invite code", Theme.FontBody, new Color(1f, 1f, 1f, 0.7f)).Size(100f, 40f);
            var field = DarkField(join, "Code", code, v => code = v, Theme.FontTitle);
            field.characterLimit = 12;
            field.onSubmit.AddListener(_ => JoinCode());
            field.Size(200f, 40f);
            var joinButton = Ui.Button(join, "Join", "Join", JoinCode, true, 8f);
            joinButton.Size(90f, 40f);
            Ui.Tip(joinButton, "Join with a code", "Enter", "Paste the code a friend sent you (from their match lobby).");

            var columns = Row(22f);
            ColumnTitle(columns, "Match", -1f, 1f);
            ColumnTitle(columns, "Host", 260f);   // V2: room for the host's badges
            ColumnTitle(columns, "Mode", 120f);
            ColumnTitle(columns, "Players", 70f);
            ColumnTitle(columns, "", 150f);
            ((HorizontalLayoutGroup)columns.GetComponent<HorizontalLayoutGroup>()).padding = new RectOffset(22, 18, 0, 0);

            var list = ScrollList(300f);
            var problem = listProblem ?? (OnlineServices.Problem.Length > 0 ? OnlineServices.Problem : null);
            if (problem != null) {
                var text = Ui.Text(list, "Problem", problem, Theme.FontBody, new Color(1f, 0.55f, 0.52f, 1f));
                text.textWrappingMode = TextWrappingModes.Normal;
                text.margin = new Vector4(10, 6, 10, 6);
                text.Size(-1, 64f);
            }
            else if (loading && matches.Count == 0) Ui.Text(list, "Loading", "  Looking for matches…", Theme.FontBody, new Color(1f, 1f, 1f, 0.6f)).Size(-1, 44f);
            else if (matches.Count == 0) {
                var text = Ui.Text(list, "Empty", "No open matches right now. Host one and send your friends its invite code.", Theme.FontBody, new Color(1f, 1f, 1f, 0.6f));
                text.textWrappingMode = TextWrappingModes.Normal;
                text.margin = new Vector4(10, 6, 10, 6);
                text.Size(-1, 60f);
            }
            foreach (var match in matches) MatchRow(list, match);
        }

        private static void ColumnTitle(RectTransform row, string text, float width, float flexible = -1f) {
            var label = Ui.Text(row, text, text.ToUpperInvariant(), 12f, new Color(1f, 1f, 1f, 0.45f), Theme.Medium);
            label.characterSpacing = 4f;
            label.Size(width, 20f, flexible);
        }

        private void MatchRow(RectTransform parent, MatchInfo match) {
            var row = Ui.Image(parent, "Match", OnlineWindow.GlassRow, 8f, true).rectTransform;
            Ui.Row(row, 10f, new RectOffset(14, 10, 4, 4));
            row.Size(-1, 50f);
            var same = match.SameVersion;
            var name = Ui.Text(row, "Name", match.name, Theme.FontBody + 1f, same ? Color.white : new Color(1f, 1f, 1f, 0.45f), Theme.Medium);
            name.Size(-1, 40f, 1f);
            // V2: a match being played right now: a green "In game" badge (players join when it ends).
            if (match.playing) {
                var badge = Ui.Image(row, "In game", new Color(0.19f, 0.64f, 0.42f, 0.9f), 6f);
                Ui.Row(badge.rectTransform, 5f, new RectOffset(8, 10, 2, 2));
                badge.rectTransform.Size(86f, 24f);
                var play = Ui.Rect("Icon", badge.transform);
                play.Size(10f, 12f);
                play.gameObject.AddComponent<CanvasRenderer>();
                play.gameObject.AddComponent<PlayIconGraphic>().raycastTarget = false;
                Ui.Text(badge.transform, "Label", "In game", 12f, Color.white, Theme.Medium).Size(-1, 20f, 1f);
                Ui.Tip(badge, "In game", null, "A match is being played. You can join when it's over and the players are back in the lobby.");
            }
            if (match.password) {
                var locked = Ui.Text(row, "Password", "Password", 12f, Theme.Warning, Theme.Medium, TextAlignmentOptions.Center);
                locked.Size(70f, 40f);
                Ui.Tip(locked, "Password", null, "The host set a password: you'll be asked for it when you join.");
            }
            // V2: the host's badges beside their name.
            var hostCell = Ui.Rect("Host", row);
            hostCell.Size(260f, 40f, 0f);
            Ui.Row(hostCell, 6f, null, TextAnchor.MiddleLeft);
            var hostName = Ui.Text(hostCell, "Name", match.host, Theme.FontSmall, new Color(1f, 1f, 1f, 0.7f));
            hostName.Size(-1, 40f, 1f);
            Badges.Beside(hostName, match.hostBadges);
            Ui.Text(row, "Mode", match.mode, Theme.FontSmall, new Color(1f, 1f, 1f, 0.7f)).Size(120f, 40f);
            Ui.Text(row, "Players", match.players + "/" + match.max, Theme.FontSmall, new Color(1f, 1f, 1f, 0.85f), Theme.Medium, TextAlignmentOptions.Center).Size(70f, 40f);
            if (same) {
                var join = Ui.Button(row, "Join", "Join", () => JoinListed(match), true, 8f, Theme.FontSmall);
                join.Size(150f, 34f);
            }
            else {
                var other = Ui.Button(row, "Version", match.build > BuildInfo.Build ? "Newer version" : "Older version",
                    () => OnlineWindow.VersionDialog(MatchSessions.VersionProblem("Build " + match.build, match.build)), false, 8f, Theme.FontSmall);
                other.targetGraphic.color = new Color(1f, 1f, 1f, 0.08f);
                other.Size(150f, 34f);
            }
        }

        private void JoinCode() {
            var clean = MatchSessions.CleanCode(code);
            if (clean.Length < 4) {
                Interface.Interface.Say("Type or paste the invite code first", false);
                return;
            }
            JoinWith(clean, false, false);
        }

        private void JoinListed(MatchInfo match) {
            if (match.password) OnlineWindow.PasswordDialog(password => JoinWith(match.id, true, false, password));
            else JoinWith(match.id, true, false);
        }

        private async void JoinWith(string key, bool byId, bool retried, string password = null) {
            if (busy) return;
            busy = true;
            var problem = await MatchSessions.Join(key, byId, password);
            busy = false;
            if (this == null) return;
            if (problem == null) {
                Show(Page.Lobby);
                return;
            }
            if (problem == "This match has a password." && !retried) {
                OnlineWindow.PasswordDialog(p => JoinWith(key, byId, true, p));
                return;
            }
            if (problem.StartsWith("That match is on")) {
                OnlineWindow.VersionDialog(problem);
                return;
            }
            Interface.Interface.Say(problem, false);
            if (byId) Refresh();
        }

        // ---- leaderboard

        // V2: the best Robot Skills runs (Online/Accounts: posted by Cloud Code after it checks each run).
        private List<Accounts.Entry> board;
        private Accounts.Entry boardMine;
        private string boardProblem;
        private bool boardLoading;
        private bool boardProgramming;   // V2 (Phase 9): the Programming Skills board

        private async void LoadBoard() {
            if (boardLoading) return;
            boardLoading = true;
            dirty = true;
            var programming = boardProgramming;
            var (top, mine, problem) = await Accounts.Leaderboard(50, programming);
            if (this == null) return;
            if (programming != boardProgramming) {   // switched while it loaded
                boardLoading = false;
                LoadBoard();
                return;
            }
            board = top;
            boardMine = mine;
            boardProblem = problem;
            boardLoading = false;
            dirty = true;
        }

        private void Leaderboard() {
            Heading("Skills leaderboard", (boardProgramming ? "Override Programming Skills: the robot's program drives the whole minute. " : "Override Driving Skills: ")
                                          + "your best run counts. A run is posted when it finishes, after it's checked against the rules (robot size, the scoring adds up).");
            // V2 (Phase 9): Driving Skills and Programming Skills each have their own board.
            var kindRow = Row(44f);
            ToolOptions.Segmented(kindRow, new[] { "Driving Skills", "Programming Skills" }, boardProgramming ? 1 : 0, i => {
                if ((i == 1) == boardProgramming) return;
                boardProgramming = i == 1;
                board = null;
                boardMine = null;
                boardProblem = null;
                Rebuild();
            }, 170f);
            Fixed(kindRow, 360f);
            if (board == null && !boardLoading) LoadBoard();
            if (board == null || boardLoading) {
                Ui.Text(content, "Loading", "Loading…", Theme.FontBody, new Color(1f, 1f, 1f, 0.6f)).Size(-1, 30f);
                return;
            }
            if (boardProblem != null) {
                var p = Ui.Text(content, "Problem", boardProblem, Theme.FontBody, Theme.Warning);
                p.textWrappingMode = TextWrappingModes.Normal;
                p.Size(-1, 50f);
                return;
            }
            if (boardMine != null) {
                var me = Ui.Text(content, "Mine", "Your best: " + boardMine.score + " points, #" + boardMine.rank, Theme.FontBody, Color.white, Theme.Medium);
                me.Size(-1, 30f);
            }
            var list = ScrollList(360f);
            if (board.Count == 0) {
                Ui.Text(list, "Empty", boardProgramming ? "No runs yet: play Programming Skills (Play game) with a robot program and finish a run." : "No runs yet: play Robot Skills (Play game) and finish a run.", Theme.FontBody, new Color(1f, 1f, 1f, 0.6f)).Size(-1, 34f);
                return;
            }
            foreach (var e in board) {
                var row = Ui.Image(list, "Run " + e.rank, e.mine ? new Color(0.16f, 0.52f, 0.96f, 0.25f) : OnlineWindow.GlassRow, 8f);
                var inner = row.rectTransform;
                Ui.Row(inner, 12f, new RectOffset(14, 14, 0, 0));
                inner.Size(-1, 40f);
                Ui.Text(inner, "Rank", "#" + e.rank, Theme.FontBody, new Color(1f, 1f, 1f, 0.6f), Theme.Medium).Size(54f, 38f);
                Ui.Text(inner, "Name", e.name, Theme.FontBody, Color.white).Size(-1, 38f, 1f);
                Ui.Text(inner, "Score", e.score + " pts", Theme.FontBody, Color.white, Theme.Medium, TextAlignmentOptions.MidlineRight).Size(90f, 38f);
                Ui.Text(inner, "When", e.when == default ? "" : e.when.ToLocalTime().ToString("MMM d"), Theme.FontSmall, new Color(1f, 1f, 1f, 0.5f), null, TextAlignmentOptions.MidlineRight).Size(70f, 38f);
                // V2: watch the run, report it (someone else's).
                var entry = e;
                var watch = Ui.Button(inner, "Watch", "Watch", () => WatchRun(entry), false, 6f, Theme.FontSmall);
                watch.targetGraphic.color = new Color(1f, 1f, 1f, 0.1f);
                watch.Size(74f, 28f);
                Ui.Tip(watch, "Watch the run", null, "Plays the replay of this run on the field.");
                if (!e.mine) {
                    var report = Ui.Button(inner, "Report", "Report", () => PlayerReports.OpenRun(entry.playerId, entry.name, entry.score, watched.TryGetValue(WatchKey(entry.playerId), out var r) ? r : null, boardProgramming), false, 6f, Theme.FontSmall);
                    report.targetGraphic.color = new Color(1f, 1f, 1f, 0.1f);
                    report.Size(74f, 28f);
                    Ui.Tip(report, "Report the run", null, "Tell the moderators this run breaks the rules (the replay goes with your report).");
                }
                else Ui.Rect("Space", inner).Size(74f, 28f);
            }
        }

        private readonly Dictionary<string, Game.ReplayFile> watched = new();
        private bool fetching;

        private string WatchKey(string playerId) => (boardProgramming ? "prog:" : "") + playerId;

        private async void WatchRun(Accounts.Entry entry) {
            if (fetching) return;
            fetching = true;
            var key = WatchKey(entry.playerId);
            var replay = await Accounts.LoadReplay(entry.playerId, boardProgramming);
            fetching = false;
            if (this == null) return;
            if (replay == null) {
                Interface.Interface.Say("There's no replay of " + entry.name + "'s run (runs from before replays were added have none)", false);
                return;
            }
            watched[key] = replay;
            Game.ReplayViewer.Open(replay);
        }

        // ---- account (V2: Online/Accounts)

        private Accounts.Stats stats;
        private bool statsLoading;
        private string accountUser = "", accountPassword = "", accountProblem, accountYear = "";
        private int accountMonth = -1;
        private bool accountBusy;

        private async void LoadStats() {
            if (statsLoading) return;
            statsLoading = true;
            var s = await Accounts.LoadStats();
            if (this == null) return;
            stats = s ?? new Accounts.Stats();
            statsLoading = false;
            dirty = true;
        }

        private async void AccountAction(Func<System.Threading.Tasks.Task<string>> action) {
            if (accountBusy) return;
            accountBusy = true;
            accountProblem = null;
            dirty = true;
            var problem = await action();
            if (this == null) return;
            accountBusy = false;
            accountProblem = problem;
            if (problem == null) {
                accountPassword = "";
                stats = null;
            }
            dirty = true;
        }

        private void Account() {
            var guest = Accounts.IsGuest;
            var child = AgeCheck.Under13;
            Heading("Profile", guest
                ? "You're playing as a guest (kept on this computer). Make an account to keep your stats, scores and badges and sign in anywhere."
                : "Signed in as " + Accounts.Username + ". Your stats, skills scores and badges go with your account.");
            // V2: who you are: a round initial, your name, guest or account, your badges.
            var who = Row(64f);
            var circle = Ui.Image(who, "Picture", Theme.Accent, 0f);
            circle.sprite = Theme.Circle;
            circle.Size(56f, 56f, 0f);
            var me = OnlineServices.Name;
            var initial = Ui.Text(circle.transform, "Initial", me.Length > 0 ? char.ToUpperInvariant(me.Trim()[0]).ToString() : "?", 26f, Color.white, Theme.Bold, TextAlignmentOptions.Center);
            initial.rectTransform.Fill();
            var whoText = Ui.Rect("Who", who);
            whoText.Size(-1, 60f, 1f);
            Ui.Column(whoText, 2f);
            var nameLine = Ui.Rect("Name line", whoText);
            nameLine.Size(-1, 32f);
            Ui.Row(nameLine, 8f, null, TextAnchor.MiddleLeft);
            var myName = Ui.Text(nameLine, "Name", me, 24f, Color.white, Theme.Medium);
            myName.Size(-1, 32f, 1f);
            Badges.Beside(myName, Badges.Text);
            Ui.Text(whoText, "Status", guest ? "Guest on this computer" : "Account · " + Accounts.Username, Theme.FontSmall, new Color(1f, 1f, 1f, 0.6f)).Size(-1, 22f);
            // V2: a player under 13 (children's privacy law): what that means here.
            if (child) {
                var note = Ui.Text(content, "Under 13", Accounts.UnderThirteen, Theme.FontSmall, new Color(1f, 1f, 1f, 0.6f));
                note.textWrappingMode = TextWrappingModes.Normal;
                note.Size(-1, 36f);
            }
            // V2: this player's badges.
            // (they're beside your name above; this row only says how to earn the beta one)
            if (Badges.BetaOpen && !Badges.Mine.Contains(Badges.Beta)) {
                var badgeRow = Row(30f);
                Ui.Text(badgeRow, "Label", "Badges", Theme.FontBody, new Color(1f, 1f, 1f, 0.7f)).Size(140f, 30f);
                {
                    var left = Mathf.Max(0f, Badges.BetaSeconds - Badges.BetaPlayed);
                    Ui.Text(badgeRow, "Beta", Badges.BetaEarnedHere ? (guest ? "Beta tester: earned! Make an account below to claim it." : "Beta tester: earned, adding it to your account…")
                        : "Beta tester: play for " + Mathf.CeilToInt(left / 60f) + " more minute" + (left > 60f ? "s" : "") + " during the beta",
                        Theme.FontSmall, new Color(1f, 1f, 1f, 0.6f)).Size(-1, 30f, 1f);
                }
            }
            if (stats == null && !statsLoading) LoadStats();
            var s = stats ?? new Accounts.Stats();
            var grid = Row(70f);
            foreach (var (label, value) in new[] { ("Matches", s.matches.ToString()), ("Won", s.wins.ToString()), ("Lost", s.losses.ToString()), ("Tied", s.ties.ToString()),
                                                    ("Points", s.points.ToString()), ("Skills best", s.skillsBest.ToString()), ("Programming best", s.programmingBest.ToString()) }) {
                var tile = OnlineWindow.GlassPanel(grid, label, OnlineWindow.GlassDeep, 10f);
                tile.Size(-1, 66f, 1f);
                var column = Ui.Rect("Column", tile.transform).Fill(12, 8, 12, 8);
                Ui.Column(column, 0f);
                Ui.Text(column, "Label", label, Theme.FontSmall, new Color(1f, 1f, 1f, 0.6f)).Size(-1, 20f);
                Ui.Text(column, "Value", statsLoading ? "…" : value, 22f, Color.white, Theme.Medium).Size(-1, 30f);
            }
            if (guest) {
                var userRow = Row(44f);
                Ui.Text(userRow, "Label", "Username", Theme.FontBody, new Color(1f, 1f, 1f, 0.7f)).Size(140f, 40f);
                var user = DarkField(userRow, "Username", accountUser, v => accountUser = v);
                user.characterLimit = 20;
                user.Size(320f, 40f);
                var passRow = Row(44f);
                Ui.Text(passRow, "Label", "Password", Theme.FontBody, new Color(1f, 1f, 1f, 0.7f)).Size(140f, 40f);
                var pass = DarkField(passRow, "Password", accountPassword, v => accountPassword = v);
                pass.contentType = TMP_InputField.ContentType.Password;
                pass.ForceLabelUpdate();
                pass.characterLimit = 30;
                pass.Size(320f, 40f);
                // V2: the birthday is required (children's privacy law); see AgeCheck.
                var birthRow = Row(44f);
                Ui.Text(birthRow, "Label", "Birthday", Theme.FontBody, new Color(1f, 1f, 1f, 0.7f)).Size(140f, 40f);
                AgeCheck.Picker(birthRow, (m, y) => {
                    accountMonth = m;
                    accountYear = y;
                }, 40f, accountMonth, accountYear).Size(360f, 40f);
                var whyRow = Row(40f);
                Ui.Rect("Indent", whyRow).Size(140f, 10f);
                var why = Ui.Text(whyRow, "Why", "Required by law: " + AgeCheck.Why.Substring(AgeCheck.Why.IndexOf("players under 13", StringComparison.Ordinal)),
                                  Theme.FontSmall, new Color(1f, 1f, 1f, 0.5f));
                why.textWrappingMode = TextWrappingModes.Normal;
                why.Size(-1, 40f, 1f);
                var buttons = Row(44f);
                Ui.Rect("Indent", buttons).Size(140f, 40f);
                var create = Ui.Button(buttons, "Create", accountBusy ? "Working…" : "Create account", () => AccountAction(() => Accounts.CreateAccount(accountUser.Trim(), accountPassword, accountMonth, accountYear)), true, 8f);
                create.Size(170f, 40f);
                create.interactable = !accountBusy;
                Ui.Tip(create, "Create account", null, "Makes this guest an account: your stats and scores so far stay.");
                var signIn = Ui.Button(buttons, "Sign in", "Sign in", () => AccountAction(() => Accounts.SignIn(accountUser, accountPassword)), false, 8f);
                signIn.targetGraphic.color = new Color(1f, 1f, 1f, 0.08f);
                signIn.Size(140f, 40f);
                signIn.interactable = !accountBusy;
                Ui.Tip(signIn, "Sign in", null, "Signs in to an account you made before (on any computer).");
                var rules = Ui.Text(content, "Rules", "Username: 3-20 letters, digits and . - _ (not your real name or email). Password: 8-30 characters with an upper case letter, a lower case letter, a digit and a symbol. Signing in doesn't need the birthday.",
                    Theme.FontSmall, new Color(1f, 1f, 1f, 0.5f));
                rules.textWrappingMode = TextWrappingModes.Normal;
                rules.Size(-1, 40f);
            }
            else if (!guest) {
                // V2: an account made before birthdays were asked: it needs one (once; it can't be changed after).
                if (Accounts.Birth == "") {
                    var birthRow = Row(44f);
                    Ui.Text(birthRow, "Label", "Add your birthday", Theme.FontBody, new Color(1f, 1f, 1f, 0.7f)).Size(160f, 40f);
                    AgeCheck.Picker(birthRow, (m, y) => {
                        accountMonth = m;
                        accountYear = y;
                    }, 40f, accountMonth, accountYear).Size(360f, 40f);
                    var save = Ui.Button(birthRow, "Save birthday", "Save", () => AccountAction(() => Accounts.SaveBirth(accountMonth, accountYear)), true, 8f);
                    save.Size(100f, 40f);
                    save.interactable = !accountBusy;
                    var why = Ui.Text(content, "Why", "Required by law: " + AgeCheck.Why.Substring(AgeCheck.Why.IndexOf("players under 13", StringComparison.Ordinal)),
                                      Theme.FontSmall, new Color(1f, 1f, 1f, 0.5f));
                    why.textWrappingMode = TextWrappingModes.Normal;
                    why.Size(-1, 40f);
                }
                var buttons = Row(44f);
                var signOut = Ui.Button(buttons, "Sign out", "Sign out", () => AccountAction(async () => {
                    await Accounts.SignOut();
                    return null;
                }), false, 8f);
                signOut.targetGraphic.color = new Color(1f, 1f, 1f, 0.08f);
                signOut.Size(140f, 40f);
            }
            if (accountProblem != null) {
                var problem = Ui.Text(content, "Problem", accountProblem, Theme.FontBody, Theme.Warning);
                problem.textWrappingMode = TextWrappingModes.Normal;
                problem.Size(-1, 50f);
            }
            // V2: appeals go to the moderators (a run taken off the leaderboard, a warning).
            var appealRow = Row(40f);
            var appeal = Ui.Button(appealRow, "Appeal", "Appeal a moderators' decision…", PlayerReports.OpenAppeal, false, 8f, Theme.FontSmall);
            appeal.targetGraphic.color = new Color(1f, 1f, 1f, 0.08f);
            appeal.Size(260f, 34f);
            Ui.Tip(appeal, "Appeal", null, "If a run of yours was taken off the leaderboard or you were warned, and you think it was a mistake.");

            // V2: your data: read the policy and terms, or delete everything kept online about you.
            var dataRow = Row(40f);
            var privacy = Ui.Button(dataRow, "Privacy", "Privacy Policy", () => LegalDocs.Open(LegalDocs.Doc.Privacy), false, 8f, Theme.FontSmall);
            privacy.targetGraphic.color = new Color(1f, 1f, 1f, 0.08f);
            privacy.Size(150f, 34f);
            var terms = Ui.Button(dataRow, "Terms", "Terms of Use", () => LegalDocs.Open(LegalDocs.Doc.Terms), false, 8f, Theme.FontSmall);
            terms.targetGraphic.color = new Color(1f, 1f, 1f, 0.08f);
            terms.Size(150f, 34f);
            var delete = Ui.Button(dataRow, "Delete", "Delete my online data…", () => DeleteDialog(guest), false, 8f, Theme.FontSmall);
            delete.targetGraphic.color = new Color(0.9f, 0.3f, 0.3f, 0.3f);
            delete.Size(220f, 34f);
            delete.interactable = !accountBusy && MatchSessions.Current == null;
            Ui.Tip(delete, "Delete my online data", null, "Deletes your " + (guest ? "guest player" : "account") + " and everything kept online for it: "
                                                          + "stats, leaderboard scores and shared replays. It can't be undone.");
        }

        // V2: deleting the player and their online data (GDPR / CCPA: a way to have it erased).
        private void DeleteDialog(bool guest) {
            var dialog = OnlineWindow.GlassDialog("Delete my online data", new Vector2(520f, 280f));
            Ui.Column(dialog.Body, 8f);
            var text = Ui.Text(dialog.Body, "Text", "This deletes your " + (guest ? "guest player" : "account \"" + Accounts.Username + "\"")
                                                    + " and everything kept online for it: your stats, your skills leaderboard scores and your shared replays. "
                                                    + "Reports you sent stay with the moderators until they're dealt with.\n\nIt can't be undone. Your builds and files on this computer aren't touched.",
                               Theme.FontBody, Theme.Text);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.Size(-1, 150f);
            Ui.Button(dialog.Footer, "Cancel", "Cancel", dialog.Close, true, 6f).Size(-1, 34f);
            var go = Ui.Button(dialog.Footer, "Delete", "Delete everything", () => {
                dialog.Close();
                AccountAction(Accounts.DeleteEverything);
            }, false, 6f);
            go.targetGraphic.color = new Color(0.85f, 0.25f, 0.25f, 0.9f);
            go.Size(-1, 34f);
        }

        // ---- hosting

        private void Host() {
            var o = hostOptions ??= new MatchSessions.HostOptions { name = OnlineServices.Name + "'s match", mode = 0, maxPlayers = 4 };
            Heading("Host a match", "Your computer runs the match; others join through Unity's relay, so nothing needs setting up on your router.");
            var nameRow = Row(44f);
            Ui.Text(nameRow, "Label", "Name", Theme.FontBody, new Color(1f, 1f, 1f, 0.7f)).Size(140f, 40f);
            var name = DarkField(nameRow, "Name", o.name, v => o.name = v);
            name.characterLimit = 40;
            name.Size(360f, 40f);
            var gameRow = Row(44f);
            Ui.Text(gameRow, "Label", "Game", Theme.FontBody, new Color(1f, 1f, 1f, 0.7f)).Size(140f, 40f);
            Ui.Text(gameRow, "Game", "Override (V5RC 2026-27)", Theme.FontBody, Color.white, Theme.Medium).Size(-1, 40f, 1f);
            var modeRow = Row(44f);
            Ui.Text(modeRow, "Label", "Mode", Theme.FontBody, new Color(1f, 1f, 1f, 0.7f)).Size(140f, 40f);
            ToolOptions.Segmented(modeRow, MatchSessions.Modes, o.mode, i => {
                o.mode = i;
                o.maxPlayers = MatchSessions.ModePlayers[i];
                Rebuild();
            }, 130f);
            Fixed(modeRow, 420f);
            var playersRow = Row(44f);
            Ui.Text(playersRow, "Label", "Players", Theme.FontBody, new Color(1f, 1f, 1f, 0.7f)).Size(140f, 40f);
            Ui.Text(playersRow, "Players", o.maxPlayers + (o.mode == 2 ? " (each on their own field)" : ""), Theme.FontBody, Color.white, Theme.Medium).Size(-1, 40f, 1f);
            var passwordRow = Row(44f);
            Ui.Text(passwordRow, "Label", "Password", Theme.FontBody, new Color(1f, 1f, 1f, 0.7f)).Size(140f, 40f);
            var password = DarkField(passwordRow, "Password", o.password ?? "", v => o.password = v);
            password.contentType = TMP_InputField.ContentType.Password;
            password.Size(240f, 40f);
            Ui.Text(passwordRow, "Note", "optional, 8+ characters", Theme.FontSmall, new Color(1f, 1f, 1f, 0.5f)).Size(-1, 40f, 1f);
            var invite = Row(40f);
            var spacer = Ui.Rect("Space", invite);
            spacer.Size(140f, 10f);
            ToolOptions.Switch(invite, "Invite only (not in the server list: friends join with the code)", o.inviteOnly, v => o.inviteOnly = v);
            // V2 (Phase 9): the 0:15 autonomous period, run by each robot's own program.
            var auton = Row(40f);
            Ui.Rect("Space", auton).Size(140f, 10f);
            ToolOptions.Switch(auton, "Autonomous period (0:15 of each robot's own program before driver control)", o.autonomous, v => {
                o.autonomous = v;
                PlayerPrefs.SetInt("V2.Online.Autonomous", v ? 1 : 0);
            });

            var gap = Ui.Rect("Gap", content);
            gap.Size(-1, 10f);
            var go = Row(46f);
            Ui.Rect("Space", go).Size(140f, 10f);
            var start = Ui.Button(go, "Host", busy ? "Starting…" : "Host match", async () => {
                if (busy) return;
                busy = true;
                Rebuild();
                var problem = await MatchSessions.Host(o);
                busy = false;
                if (this == null) return;
                if (problem != null) {
                    Interface.Interface.Say(problem, false);
                    Rebuild();
                    return;
                }
                Show(Page.Lobby);
            }, true, 8f, Theme.FontTitle);
            start.Size(200f, 44f);
            start.interactable = !busy;
        }

        // ---- the lobby

        private void Lobby() {
            var session = MatchSessions.Current;
            var props = session.Properties;
            string Prop(string key) => props != null && props.TryGetValue(key, out var p) ? p.Value : "";
            Heading(NameFilter.Shown(session.Name, "Match"), Prop(MatchSessions.GameKey) + " · " + Prop(MatchSessions.ModeKey) + (Prop(MatchSessions.AutonKey) == "1" ? " · autonomous" : "") + " · " + session.PlayerCount + "/" + session.MaxPlayers + " players"
                                  + (session.HasPassword ? " · password" : "") + (session.IsPrivate ? " · invite only" : ""));

            // The invite code, big, with Copy.
            var invite = Ui.Image(content, "Invite", OnlineWindow.GlassDeep, 12f).rectTransform;
            Ui.Row(invite, 16f, new RectOffset(20, 14, 8, 8));
            invite.Size(-1, 76f);
            Ui.Text(invite, "Label", "Invite code", Theme.FontBody, new Color(1f, 1f, 1f, 0.65f)).Size(110f, 56f);
            var code = Ui.Text(invite, "Code", OnlineWindow.Spaced(session.Code), 36f, Color.white, Theme.Medium);
            code.characterSpacing = 10f;
            code.Size(-1, 56f, 1f);
            var copy = Ui.Button(invite, "Copy", copiedUntil > Time.unscaledTime ? "Copied!" : "Copy invite", () => {
                GUIUtility.systemCopyBuffer = OnlineWindow.InviteText(session);
                Audio.Sounds.Play(Audio.Sounds.Tick);
                copiedUntil = Time.unscaledTime + 2f;
                Rebuild();
                Invoke(nameof(Rebuild), 2.05f);
            }, true, 8f);
            copy.Size(150f, 42f);
            Ui.Tip(copy, "Copy invite", null, "Copies a message with the code to paste in Discord or a chat. Friends type the code under Multiplayer > Invite code.");

            // The players.
            var list = ScrollList(220f);
            var me = session.CurrentPlayer?.Id;
            var everyoneReady = session.Players.Count >= 2;
            foreach (var player in session.Players) {
                var team = MatchSessions.Get(player, MatchSessions.TeamKey);
                var ready = MatchSessions.Get(player, MatchSessions.ReadyKey) == "1";
                if (!ready || team == "" || string.IsNullOrEmpty(MatchSessions.Get(player, MatchSessions.RobotKey))) everyoneReady = false;
                var row = Ui.Image(list, "Player", player.Id == me ? new Color(1f, 1f, 1f, 0.1f) : OnlineWindow.GlassRow, 8f).rectTransform;
                Ui.Row(row, 12f, new RectOffset(14, 10, 2, 2));
                row.Size(-1, 46f);
                var dot = Ui.Image(row, "Team", team == "red" ? Theme.AxisX : team == "blue" ? Theme.AxisZ : new Color(1f, 1f, 1f, 0.3f), 8f);
                dot.Size(16f, 16f);
                var who = MatchSessions.Get(player, MatchSessions.NameKey);
                if (string.IsNullOrEmpty(who)) who = "Player";
                var name = Ui.Text(row, "Name", who + (player.Id == session.Host ? "  · host" : "") + (player.Id == me ? "  · you" : ""), Theme.FontBody + 1f, Color.white, Theme.Medium);
                name.Size(-1, 40f, 1f);
                Badges.Beside(name, MatchSessions.Get(player, MatchSessions.BadgesKey));   // V2
                if (!MatchSessions.CanHear(player))   // just before the name
                    OnlineWindow.CantHearIcon(row, player.Id == me ? "You can't hear voice chat" : who + " can't hear voice chat").SetSiblingIndex(name.transform.GetSiblingIndex());
                var robot = MatchSessions.Get(player, MatchSessions.RobotKey);
                Ui.Text(row, "Robot", string.IsNullOrEmpty(robot) ? "no robot yet" : robot, Theme.FontSmall, new Color(1f, 1f, 1f, string.IsNullOrEmpty(robot) ? 0.4f : 0.7f)).Size(200f, 40f);
                Ui.Text(row, "Ready", ready ? "Ready" : "Not ready", Theme.FontSmall, ready ? Theme.Success : new Color(1f, 1f, 1f, 0.45f), Theme.Medium, TextAlignmentOptions.Center).Size(80f, 40f);
                if (player.Id != me) PlayerButtons(row, player.Id, who);
                if (session.IsHost && player.Id != me) {
                    var id = player.Id;
                    var kick = Ui.Button(row, "Remove", "Remove", () => MatchSessions.Kick(id), false, 8f, Theme.FontSmall);
                    kick.targetGraphic.color = new Color(1f, 1f, 1f, 0.08f);
                    kick.Size(-1, 30f);
                }
            }
            for (var i = session.Players.Count; i < session.MaxPlayers; i++) {
                var empty = Ui.Text(list, "Open", "Open slot", Theme.FontSmall, new Color(1f, 1f, 1f, 0.35f));
                empty.margin = new Vector4(42, 0, 0, 0);
                empty.Size(-1, 34f);
            }

            // Your choices and the buttons.
            var mine = session.CurrentPlayer;
            var myTeam = MatchSessions.Get(mine, MatchSessions.TeamKey);
            var myReady = MatchSessions.Get(mine, MatchSessions.ReadyKey) == "1";
            var choices = Row(44f);
            Ui.Text(choices, "Alliance", "Alliance", Theme.FontBody, new Color(1f, 1f, 1f, 0.7f)).Size(80f, 40f);
            ToolOptions.Segmented(choices, new[] { "Red", "Blue" }, myTeam == "red" ? 0 : myTeam == "blue" ? 1 : -1,
                i => MatchSessions.Set(MatchSessions.TeamKey, i == 0 ? "red" : "blue"), 90f);
            Fixed(choices, 200f);
            // V2: choose the robot right here (a build file); the build that's open is the robot you bring.
            var myRobot = MatchSessions.Get(mine, MatchSessions.RobotKey);
            var use = Ui.Button(choices, "Robot", string.IsNullOrEmpty(myRobot) ? "Choose robot…" : "Robot: " + myRobot + "  (change)", ChooseRobot,
                string.IsNullOrEmpty(myRobot), 8f, Theme.FontSmall);
            if (!string.IsNullOrEmpty(myRobot)) use.targetGraphic.color = new Color(1f, 1f, 1f, 0.08f);
            use.Size(-1, 34f);
            Ui.Tip(use, "Your robot", null, "Open the build of the robot you're bringing. It needs a V5 Brain: on the field the robot is the Brain "
                                            + "and everything connected to it.");
            var space = Ui.Rect("Space", choices);
            space.Size(-1, 10f, 1f);
            var readyButton = Ui.Button(choices, "Ready", myReady ? "Not ready" : "Ready", () => {
                if (!myReady && myTeam == "") {
                    Interface.Interface.Say("Pick an alliance first", false);
                    return;
                }
                if (!myReady && string.IsNullOrEmpty(MatchSessions.RobotName())) {
                    Interface.Interface.Say(Game.SkillsGame.RobotProblem() ?? "Choose your robot first", false);
                    return;
                }
                MatchSessions.Set(MatchSessions.ReadyKey, myReady ? "0" : "1");
            }, !myReady, 8f);
            if (myReady) readyButton.targetGraphic.color = new Color(1f, 1f, 1f, 0.08f);
            readyButton.Size(120f, 40f);
            if (session.IsHost) {
                var start = Ui.Button(choices, "Start", "Start match", () => {
                    var problem = OnlineMatch.HostStart();
                    if (problem != null) Interface.Interface.Say(problem, false);
                }, true, 8f);
                start.Size(150f, 40f);
                start.interactable = everyoneReady;
                Ui.Tip(start, "Start match", null, everyoneReady ? "Everyone is ready." : "Waiting until every player has an alliance, a robot and is ready (2 players at least).");
            }
        }
    }

    /// <summary>A background picture drifting and breathing slowly (the Multiplayer screen, the home menu).</summary>
    public class BackgroundDrift : MonoBehaviour {
        private Canvas canvas;

        // V2: the same on screen on every canvas (in screen pixels): the home menu's and the Multiplayer screen's
        // canvases scale differently, so the picture jumped ~16 px (a small zoom) going from one to the other.
        private void Update() {
            var t = Time.unscaledTime;
            var rect = (RectTransform)transform;
            if (canvas == null) canvas = GetComponentInParent<Canvas>()?.rootCanvas;
            var scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            rect.localScale = Vector3.one * (1.06f + 0.025f * Mathf.Sin(t * 0.07f));
            rect.anchoredPosition = new Vector2(Mathf.Sin(t * 0.045f) * 24f, Mathf.Cos(t * 0.05f) * 13f) / scale;
        }
    }

    /// <summary>Full-screen pages (the home menu, the Multiplayer screen) cover the 3D view: while any is open the main
    /// camera draws nothing (its culling mask is 0; switching the camera off would black out the interface drawn through
    /// its camera stack).</summary>
    public static class ViewHider {
        private static readonly HashSet<object> hiders = new();
        private static int savedMask;
        private static Camera camera;

        public static void Set(object who, bool hide) {
            var before = hiders.Count > 0;
            if (hide) hiders.Add(who);
            else hiders.Remove(who);
            hiders.RemoveWhere(h => h == null || h is UnityEngine.Object o && o == null);
            var after = hiders.Count > 0;
            if (before == after) return;
            if (after) {
                camera = PivotCamera.Main != null ? PivotCamera.Main.camera : null;
                if (camera == null) return;
                savedMask = camera.cullingMask;
                camera.cullingMask = 0;
            }
            else if (camera != null) {
                camera.cullingMask = savedMask;
                camera = null;
            }
        }
    }

    /// <summary>The Multiplayer screen's backgrounds: pictures in Resources/Online/Backgrounds, one picked at random and
    /// blurred once (made small, then a Gaussian blur on the CPU: drawn full screen it's soft).</summary>
    public static class Backgrounds {
        private static Texture2D blurred, shade, sharp;

        /// <summary>V2: the picture the blurred background is made from, sharp (the intro starts on it).</summary>
        public static Texture2D Sharp() {
            if (sharp != null) return sharp;
            var pictures = Resources.LoadAll<Texture2D>("Online/Backgrounds");
            if (pictures.Length == 0) return null;
            return sharp = pictures[UnityEngine.Random.Range(0, pictures.Length)];
        }

        public static Texture2D Blurred() {
            if (blurred != null) return blurred;
            var source = Sharp();
            if (source == null) return null;
            const int width = 400;
            var height = Mathf.Max(1, Mathf.RoundToInt(width * (float)source.height / source.width));
            // Halve it a few times so the small copy keeps every pixel's colour (one big step would skip most).
            var w = source.width;
            var h = source.height;
            RenderTexture current = null;
            Texture from = source;
            while (w / 2 >= width) {
                w /= 2;
                h /= 2;
                var next = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                next.filterMode = FilterMode.Bilinear;
                Graphics.Blit(from, next);
                if (current != null) RenderTexture.ReleaseTemporary(current);
                current = next;
                from = next;
            }
            var small = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(from, small);
            if (current != null) RenderTexture.ReleaseTemporary(current);
            var active = RenderTexture.active;
            RenderTexture.active = small;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Multiplayer background" };
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(small);
            var pixels = texture.GetPixels();
            Blur(pixels, width, height, 3f);
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return blurred = texture;
        }

        private static void Blur(Color[] pixels, int w, int h, float sigma) {
            var radius = Mathf.CeilToInt(sigma * 2.5f);
            var kernel = new float[radius * 2 + 1];
            var sum = 0f;
            for (var i = -radius; i <= radius; i++) sum += kernel[i + radius] = Mathf.Exp(-i * i / (2f * sigma * sigma));
            for (var i = 0; i < kernel.Length; i++) kernel[i] /= sum;
            var temp = new Color[pixels.Length];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++) {
                    var c = new Color(0, 0, 0, 0);
                    for (var k = -radius; k <= radius; k++) c += pixels[y * w + Mathf.Clamp(x + k, 0, w - 1)] * kernel[k + radius];
                    temp[y * w + x] = c;
                }
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++) {
                    var c = new Color(0, 0, 0, 0);
                    for (var k = -radius; k <= radius; k++) c += temp[Mathf.Clamp(y + k, 0, h - 1) * w + x] * kernel[k + radius];
                    c.a = 1f;
                    pixels[y * w + x] = c;
                }
        }

        /// <summary>Darkens the picture, most on the left (behind the menu) and at the bottom.</summary>
        public static Texture2D Shade() {
            if (shade != null) return shade;
            const int w = 128, h = 64;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Multiplayer shade" };
            var pixels = new Color32[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++) {
                    var u = x / (w - 1f);
                    var v = y / (h - 1f);
                    var alpha = 0.12f + 0.4f * Mathf.Clamp01(1f - u / 0.45f) + 0.2f * Mathf.Clamp01(1f - v / 0.3f);
                    pixels[y * w + x] = new Color32(8, 10, 14, (byte)(Mathf.Clamp01(alpha) * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return shade = texture;
        }
    }
}

namespace Protobot.Online {
    /// <summary>V2: the crossed-out speaker drawn in its rect (see OnlineWindow.CantHearIcon).</summary>
    public class CantHearGraphic : MaskableGraphic {
        protected override void OnPopulateMesh(VertexHelper vh) {
            vh.Clear();
            var r = rectTransform.rect;
            var s = Mathf.Min(r.width, r.height);
            var o = r.center - new Vector2(s, s) * 0.5f;
            Vector2 P(float x, float y) => o + new Vector2(x, y) * s;
            void Tri(Vector2 a, Vector2 b, Vector2 c, Color32 col) {
                var i = vh.currentVertCount;
                vh.AddVert(a, col, Vector2.zero);
                vh.AddVert(b, col, Vector2.zero);
                vh.AddVert(c, col, Vector2.zero);
                vh.AddTriangle(i, i + 1, i + 2);
            }
            void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 col) {
                Tri(a, b, c, col);
                Tri(a, c, d, col);
            }
            void Line(Vector2 a, Vector2 b, float w, Color32 col) {
                var n = (b - a).normalized;
                var side = new Vector2(-n.y, n.x) * (w * s * 0.5f);
                Quad(a - side, a + side, b + side, b - side, col);
            }
            Color32 c = color;
            // The speaker: a box and its cone.
            Quad(P(0.14f, 0.38f), P(0.14f, 0.62f), P(0.3f, 0.62f), P(0.3f, 0.38f), c);
            Quad(P(0.3f, 0.38f), P(0.3f, 0.62f), P(0.54f, 0.84f), P(0.54f, 0.16f), c);
            // The slash, in the warning red, with a dark edge so it reads on any background.
            Line(P(0.1f, 0.1f), P(0.9f, 0.9f), 0.17f, new Color32(20, 20, 24, 200));
            Line(P(0.1f, 0.1f), P(0.9f, 0.9f), 0.1f, new Color32(235, 80, 70, 255));
        }
    }
}

namespace Protobot.Online {
    /// <summary>V2: a small play triangle (the server list's In game badge).</summary>
    public class PlayIconGraphic : UnityEngine.UI.MaskableGraphic {
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh) {
            vh.Clear();
            var r = rectTransform.rect;
            UnityEngine.Color32 c = UnityEngine.Color.white;
            vh.AddVert(new UnityEngine.Vector3(r.xMin, r.yMin), c, UnityEngine.Vector2.zero);
            vh.AddVert(new UnityEngine.Vector3(r.xMin, r.yMax), c, UnityEngine.Vector2.zero);
            vh.AddVert(new UnityEngine.Vector3(r.xMax, r.center.y), c, UnityEngine.Vector2.zero);
            vh.AddTriangle(0, 1, 2);
        }
    }
}
