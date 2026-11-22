using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Protobot.Interface;
using TMPro;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Online {
    /// <summary>V2: voice chat in online matches (Vivox): each alliance has its own channel, so you hear only your partner.
    /// Push to talk (hold T, Edit > Keyboard shortcuts), open mic, or off; no microphone is needed to play (you still hear
    /// your partner). A small panel top left shows who's in the channel, lights up whoever is talking, and mutes a
    /// player for you alone.</summary>
    public static class Voice {
        public enum Mode { Off, PushToTalk, OpenMic }
        private static readonly string[] ModeNames = { "Off", "Push to talk", "Open mic" };
        private const string ModeKey = "V2.Voice.Mode";

        public static Mode Current {
            get => AgeCheck.Under13 ? Mode.Off : (Mode)Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, (int)Mode.PushToTalk), 0, 2);
            set {
                if (AgeCheck.Under13) return;   // V2 (COPPA): voice chat stays off under 13
                PlayerPrefs.SetInt(ModeKey, (int)value);
                if (value == Mode.Off) Leave();
                else if (channel == null && lastSession != null) _ = JoinAlliance(lastSession, lastRed);
                // V2: teammates see whether this player can hear voice chat (a crossed-out speaker by the name).
                if (MatchSessions.Current != null) MatchSessions.Set(MatchSessions.VoiceKey, CanHear ? "1" : "0");
            }
        }

        /// <summary>V2: whether this player hears voice chat. Players under 13 never do (COPPA: their voice is never sent
        /// and they don't hear strangers); a player can also switch it off.</summary>
        public static bool CanHear => !AgeCheck.Under13 && Current != Mode.Off;

        public static string Problem { get; private set; }
        private static string channel, lastSession;
        private static bool lastRed, initialized;
        private static Panel panel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Keys() => Tools.Keybinds.Add("VoiceTalk", "Online", "Push to talk (voice chat with your alliance partner)", "T");

        /// <summary>Joins this match's channel for an alliance (red or blue).</summary>
        public static async Task JoinAlliance(string sessionId, bool red) {
            lastSession = sessionId;
            lastRed = red;
            Show();
            if (AgeCheck.Under13 || Current == Mode.Off || string.IsNullOrEmpty(sessionId)) return;
            if (!await OnlineServices.Start()) {
                Problem = OnlineServices.Problem;
                return;
            }
            try {
                if (!initialized) {
                    await VivoxService.Instance.InitializeAsync();
                    initialized = true;
                }
                if (!VivoxService.Instance.IsLoggedIn) await VivoxService.Instance.LoginAsync(new LoginOptions { DisplayName = OnlineServices.Name });
                var name = new string(sessionId.Where(char.IsLetterOrDigit).ToArray());
                if (name.Length > 60) name = name.Substring(0, 60);
                var wanted = "pb" + name + (red ? "red" : "blue");
                if (channel != null && channel != wanted) await VivoxService.Instance.LeaveChannelAsync(channel);
                if (channel != wanted) {
                    await VivoxService.Instance.JoinGroupChannelAsync(wanted, ChatCapability.AudioOnly);
                    channel = wanted;
                }
                Problem = null;
                VivoxService.Instance.MuteInputDevice();   // push to talk starts quiet; the panel opens the mic
                Builds.ActionLog.Write("online", "voice: joined " + (red ? "red" : "blue") + " alliance channel");
            }
            catch (Exception e) {
                Problem = "Voice chat isn't available: " + OnlineServices.Explain(e);
                Debug.LogWarning("[Voice] " + e.Message);
            }
        }

        /// <summary>Leaves the channel (the match ended) and hides the panel.</summary>
        public static async void Leave(bool hide = false) {
            var was = channel;
            channel = null;
            if (hide) {
                lastSession = null;
                if (panel != null) UnityEngine.Object.Destroy(panel.gameObject);
                panel = null;
            }
            if (was == null || !initialized) return;
            try { await VivoxService.Instance.LeaveChannelAsync(was); }
            catch (Exception e) { Debug.LogWarning("[Voice] leave: " + e.Message); }
        }

        private static void Show() {
            if (panel != null || Interface.Interface.Root == null) return;
            panel = Ui.Rect("Voice", Interface.Interface.Root).gameObject.AddComponent<Panel>();
            panel.Build();
        }

        private static IReadOnlyList<VivoxParticipant> Others() {
            if (channel == null || !initialized || !VivoxService.Instance.ActiveChannels.TryGetValue(channel, out var list)) return new List<VivoxParticipant>();
            return list.Where(p => !p.IsSelf).ToList();
        }

        private static bool Talking() {
            if (channel == null || !initialized || !VivoxService.Instance.ActiveChannels.TryGetValue(channel, out var list)) return false;
            return list.Any(p => p.IsSelf && p.SpeechDetected);
        }

        // The panel: the mode (click to change), the mic state, and a row per other player (talking light, mute).
        private class Panel : MonoBehaviour {
            private RectTransform rows, deaf;
            private string deafShown;

            // V2: alliance partners who can't hear voice chat (under 13, or voice chat off), with the crossed-out speaker.
            private void ShowDeaf() {
                var session = MatchSessions.Current;
                var mine = session?.CurrentPlayer;
                var team = mine != null ? MatchSessions.Get(mine, MatchSessions.TeamKey) : "";
                var names = session == null ? new List<string>()
                    : session.Players.Where(p => p.Id != mine?.Id && MatchSessions.Get(p, MatchSessions.TeamKey) == team && !MatchSessions.CanHear(p))
                        .Select(p => MatchSessions.Get(p, MatchSessions.NameKey)).ToList();
                var signature = string.Join(",", names);
                if (signature == deafShown) return;
                deafShown = signature;
                for (var i = deaf.childCount - 1; i >= 0; i--) Destroy(deaf.GetChild(i).gameObject);
                foreach (var who in names) {
                    var row = Ui.Rect(who, deaf);
                    Ui.Row(row, 6f);
                    row.Size(-1, 22f);
                    OnlineWindow.CantHearIcon(row, who + " can't hear voice chat", 18f, Theme.TextDim);
                    Ui.Text(row, "Name", who + " can't hear voice chat", Theme.FontSmall - 1f, Theme.TextDim).Size(-1, 20f, 1f);
                }
            }
            private TextMeshProUGUI modeLabel, state;
            private string shown;
            private bool micOpen;

            public void Build() {
                var rect = (RectTransform)transform;
                rect.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -(Layout.TopBarBottom + 40f)), new Vector2(250f, 0f));
                var back = Ui.Panel(rect, "Back", Theme.Panel, 8f);
                back.rectTransform.Fill();
                back.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                Ui.Column(rect, 4f, new RectOffset(10, 10, 8, 8));
                gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var top = Ui.Rect("Top", rect);
                Ui.Row(top, 6f);
                top.Size(-1, 26f);
                Ui.Text(top, "Title", "Voice", Theme.FontSmall, Theme.TextDim, Theme.Medium).Size(42f, 24f);
                var mode = Ui.Button(top, "Mode", AgeCheck.Under13 ? "Off (under 13)" : ModeNames[(int)Current], null, false, 5f, Theme.FontSmall);
                mode.Size(-1, 24f, 1f);
                mode.interactable = !AgeCheck.Under13;
                modeLabel = mode.GetComponentInChildren<TextMeshProUGUI>();
                mode.onClick.AddListener(() => {
                    if (AgeCheck.Under13) return;
                    Current = (Mode)(((int)Current + 1) % 3);
                    modeLabel.text = ModeNames[(int)Current];
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                });
                Ui.Tip(mode, "Voice chat", null, AgeCheck.Under13
                    ? "Voice chat is off for players under 13: you don't hear others and they don't hear you. A crossed-out speaker by your name tells your partner."
                    : "Push to talk (hold " + Tools.Keybinds.MainKey("VoiceTalk") + "), open mic, or off. You hear only your alliance partner.");
                state = Ui.Text(rect, "State", "", Theme.FontSmall - 1f, Theme.TextFaint);
                state.Size(-1, 18f);
                deaf = Ui.Rect("Can't hear", rect);
                Ui.Column(deaf, 2f);
                rows = Ui.Rect("Players", rect);
                Ui.Column(rows, 2f);
            }

            private void Update() {
                ShowDeaf();
                if (AgeCheck.Under13) {
                    state.text = "You don't hear voice chat (under 13)";
                    return;
                }
                if (!initialized || channel == null) {
                    state.text = Problem ?? (Current == Mode.Off ? "Off" : "Connecting…");
                    return;
                }
                // The microphone: open while the key is held (push to talk), always (open mic), never (off).
                var typing = InputEvents.TextInput.IsTyping;
                var open = Current == Mode.OpenMic || (Current == Mode.PushToTalk && !typing && Tools.Keybinds.Held("VoiceTalk"));
                if (open != micOpen) {
                    micOpen = open;
                    try {
                        if (open) VivoxService.Instance.UnmuteInputDevice();
                        else VivoxService.Instance.MuteInputDevice();
                    }
                    catch (Exception e) { Debug.LogWarning("[Voice] mic: " + e.Message); }
                }
                state.text = micOpen ? (Talking() ? "Talking" : "Mic on") : Current == Mode.PushToTalk ? "Hold " + Tools.Keybinds.MainKey("VoiceTalk") + " to talk" : "Mic off";
                state.color = micOpen ? Theme.Success : Theme.TextFaint;

                var others = Others();
                var signature = string.Join(",", others.Select(p => p.PlayerId + p.IsMuted));
                if (signature != shown) {
                    shown = signature;
                    for (var i = rows.childCount - 1; i >= 0; i--) Destroy(rows.GetChild(i).gameObject);
                    if (others.Count == 0) Ui.Text(rows, "Alone", "Nobody else on your alliance's channel", Theme.FontSmall - 1f, Theme.TextFaint).Size(-1, 20f);
                    foreach (var p in others) Row(p);
                }
                foreach (Transform row in rows) {
                    var light = row.Find("Light")?.GetComponent<Image>();
                    var player = others.FirstOrDefault(p => row.name == p.PlayerId);
                    if (light != null && player != null) light.color = player.SpeechDetected ? Theme.Success : Theme.Field;
                }
            }

            private void Row(VivoxParticipant p) {
                var row = Ui.Rect(p.PlayerId, rows);
                Ui.Row(row, 6f);
                row.Size(-1, 24f);
                var light = Ui.Image(row, "Light", Theme.Field, 0f);
                light.sprite = Theme.Circle;
                light.Size(10f, 10f);
                Ui.Text(row, "Name", string.IsNullOrEmpty(p.DisplayName) ? "Player" : NameFilter.Shown(p.DisplayName), Theme.FontSmall, Theme.Text).Size(-1, 22f, 1f);
                var mute = Ui.Button(row, "Mute", p.IsMuted ? "Unmute" : "Mute", () => {
                    try {
                        if (p.IsMuted) p.UnmutePlayerLocally();
                        else p.MutePlayerLocally();
                    }
                    catch (Exception e) { Debug.LogWarning("[Voice] mute: " + e.Message); }
                    shown = null;
                }, p.IsMuted, 5f, Theme.FontSmall - 1f);
                mute.Size(64f, 22f);
                Ui.Tip(mute, "Mute", null, "Mutes this player for you only.");
            }
        }
    }
}
