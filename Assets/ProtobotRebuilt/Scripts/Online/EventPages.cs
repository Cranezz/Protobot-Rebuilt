using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Protobot.Builds;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Online {
    /// <summary>V2 (build 88): the Multiplayer screen's events pages (Docs/Events concept.md): Events (join with a code,
    /// your events, open events, champions), Host an event, and an event's own page (countdown, your robot, your match,
    /// the bracket, the players, what happened, the host's controls).</summary>
    public partial class OnlineScreen {
        private JObject eventsList;
        private bool eventsLoading;
        private int eventsTab;           // 0 open events, 1 champions
        private string eventCode = "";
        private string shownEvent;
        private int eventTab;            // 0 bracket, 1 players, 2 what happened
        private EventForm form;
        private float eventCopied;
        private string eventSignature;

        // Texts that count down: refreshed 4 times a second without rebuilding the page.
        private readonly List<(TextMeshProUGUI text, Func<string> value)> live = new();
        private float liveAt;

        private class EventForm {
            public string name = "", desc = "";
            public int when;                 // 0 when I press Start, 1 in some minutes, 2 at a date and time
            public int inMinutes = 30;
            public string date = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd"), time = DateTime.Now.AddHours(1).ToString("HH:00");
            public bool build;
            public int buildMinutes = 60;
            public int callSeconds = 120;
            public bool listed, play = true, autonomous = true;
        }

        private static readonly int[] InMinutes = { 5, 15, 30, 60, 120, 240 };
        private static readonly int[] BuildMinutes = { 15, 30, 45, 60, 90, 120, 180, 240 };
        private static readonly int[] CallSeconds = { 60, 120, 180, 300 };

        public void ShowEvent(string code) {
            shownEvent = code;
            eventTab = 0;
            eventSignature = null;
            Show(Page.Event);
            _ = LoadEvent(code);
        }

        private void EventsChanged() {
            if (page != Page.Event || shownEvent == null || !Online.Events.Known.TryGetValue(shownEvent, out var ev)) return;
            var signature = ev.ToString(Newtonsoft.Json.Formatting.None);
            if (signature == eventSignature) return;
            eventSignature = signature;
            dirty = true;
        }

        private void LiveUpdate() {
            if (live.Count == 0 || Time.unscaledTime < liveAt) return;
            liveAt = Time.unscaledTime + 0.25f;
            foreach (var (text, value) in live)
                if (text != null) text.text = value();
        }

        private TextMeshProUGUI Live(TextMeshProUGUI text, Func<string> value) {
            live.Add((text, value));
            text.text = value();
            return text;
        }

        private async System.Threading.Tasks.Task LoadEvent(string code) {
            var result = await Online.Events.Get(code);
            if (this == null) return;
            if (!Online.Events.Ok(result)) {
                Interface.Interface.Say(Online.Events.Reason(result), false);
                if (page == Page.Event && shownEvent == code) Show(Page.Events);
                return;
            }
            dirty = true;
        }

        private async void LoadEvents() {
            if (eventsLoading) return;
            eventsLoading = true;
            var result = await Online.Events.Call("list");
            // The events this player is in, fresh.
            foreach (var c in Online.Events.Joined()) await Online.Events.Get(c);
            if (this == null) return;
            eventsLoading = false;
            eventsList = result;
            dirty = true;
        }

        private static Color Dim(float a) => new(1f, 1f, 1f, a);

        private Button SmallButton(Transform parent, string label, Action click, float width, bool primary = false) {
            var button = Ui.Button(parent, label, label, click, primary, 6f, Theme.FontSmall);
            if (!primary) button.targetGraphic.color = Dim(0.1f);
            button.Size(width, 30f);
            return button;
        }

        // ------------------------------------------------------------------ the Events page

        private void EventsPage() {
            var head = Row(42f);
            Ui.Text(head, "Heading", "Events", 28f, Color.white, Theme.Medium).Size(-1, 40f, 1f);
            var refresh = SmallButton(head, eventsLoading ? "Loading…" : "Refresh", () => {
                eventsList = null;
                Rebuild();
            }, 90f);
            refresh.interactable = !eventsLoading;
            var sub = Ui.Text(content, "Sub", "Tournaments anyone can host: a bracket of 1v1 matches, with robots you have or ones built against the clock. "
                                             + "Join with the code the host shares.", Theme.FontBody, Dim(0.65f));
            sub.textWrappingMode = TextWrappingModes.Normal;
            sub.Size(-1, 44f);

            var join = Row(44f);
            Ui.Text(join, "Label", "Event code", Theme.FontBody, Dim(0.7f)).Size(100f, 40f);
            var field = DarkField(join, "Code", eventCode, v => eventCode = v, Theme.FontTitle);
            field.characterLimit = 8;
            field.onSubmit.AddListener(_ => JoinEvent(eventCode));
            field.Size(180f, 40f);
            var joinButton = Ui.Button(join, "Join", "Join", () => JoinEvent(eventCode), true, 8f);
            joinButton.Size(90f, 40f);
            Ui.Tip(joinButton, "Join an event", "Enter", "Type the code the host sent you. You can join any time before the event starts, and come back later: it remembers you.");
            Ui.Rect("Space", join).Size(-1, 10f, 1f);
            var host = Ui.Button(join, "Host", "Host an event…", () => {
                form = new EventForm { name = OnlineServices.Name + "'s tournament" };
                Show(Page.EventHost);
            }, false, 8f);
            host.targetGraphic.color = Dim(0.1f);
            host.Size(170f, 40f);

            if (eventsList == null && !eventsLoading) LoadEvents();

            // Your events.
            var mine = Online.Events.Joined().Select(c => Online.Events.Known.TryGetValue(c, out var e) ? e : null).Where(e => e != null).ToList();
            if (eventsList != null && eventsList["events"] is JArray listed)
                foreach (var e in listed.OfType<JObject>().Where(e => (string)e["host"] == Online.Events.Me))   // hosted, not playing
                    if (mine.All(m => (string)m["code"] != (string)e["code"]) && Online.Events.Known.TryGetValue((string)e["code"], out var full)) mine.Add(full);
            if (mine.Count > 0) {
                Ui.Text(content, "Yours", "Your events", Theme.FontTitle, Color.white, Theme.Medium).Size(-1, 28f);
                foreach (var ev in mine.Take(4)) EventRow(content, ev, true);
            }

            var tabRow = Row(40f);
            ToolOptions.Segmented(tabRow, new[] { "Open events", "Champions" }, eventsTab, i => {
                eventsTab = i;
                Rebuild();
            }, 150f);
            Fixed(tabRow, 310f);
            var list = ScrollList(240f);
            if (eventsList == null) {
                Ui.Text(list, "Loading", "Loading…", Theme.FontBody, Dim(0.6f)).Size(-1, 34f);
                return;
            }
            if (!Online.Events.Ok(eventsList)) {
                var problem = Ui.Text(list, "Problem", Online.Events.Reason(eventsList), Theme.FontBody, Theme.Warning);
                problem.textWrappingMode = TextWrappingModes.Normal;
                problem.Size(-1, 44f);
                return;
            }
            if (eventsTab == 0) {
                var open = (eventsList["events"] as JArray)?.OfType<JObject>().Where(e => (bool?)e["listed"] == true).ToList() ?? new List<JObject>();
                if (open.Count == 0) {
                    var empty = Ui.Text(list, "Empty", "No open events right now. Host one, or join with a code (events can be invite only).", Theme.FontBody, Dim(0.6f));
                    empty.textWrappingMode = TextWrappingModes.Normal;
                    empty.Size(-1, 44f);
                }
                foreach (var e in open) EventRow(list, e, false);
            }
            else {
                var champions = (eventsList["champions"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
                if (champions.Count == 0) Ui.Text(list, "Empty", "No champions yet: the first event to finish puts its winner here.", Theme.FontBody, Dim(0.6f)).Size(-1, 34f);
                foreach (var c in champions) ChampionRow(list, c);
            }
        }

        // One event in a list: name, host, rules/state, when, players, Open.
        private void EventRow(Transform parent, JObject ev, bool full) {
            var row = Ui.Image(parent, "Event", OnlineWindow.GlassRow, 8f).rectTransform;
            Ui.Row(row, 12f, new RectOffset(14, 10, 2, 2));
            row.Size(-1, 46f);
            var code = (string)ev["code"];
            Ui.Text(row, "Name", NameFilter.Shown((string)ev["name"], "Event"), Theme.FontBody + 1f, Color.white, Theme.Medium).Size(-1, 40f, 1f);
            Ui.Text(row, "Host", NameFilter.Shown((string)ev["hostName"]), Theme.FontSmall, Dim(0.6f)).Size(140f, 40f);
            var players = full ? ((ev["players"] as JArray)?.OfType<JObject>().Count(p => (bool?)p["out"] != true) ?? 0) : (int?)ev["players"] ?? 0;
            Ui.Text(row, "Players", players + (players == 1 ? " player" : " players"), Theme.FontSmall, Dim(0.6f)).Size(80f, 40f);
            var state = Ui.Text(row, "State", "", Theme.FontSmall, Dim(0.85f), null, TextAlignmentOptions.MidlineRight);
            state.Size(200f, 40f);
            Live(state, () => StateLine(ev, full));
            SmallButton(row, "Open", () => ShowEvent(code), 70f, full);
        }

        // "Starts Saturday 3:00 PM · in 2 h 14 min", "Building · 42:10 left", "Round 2 of 3", "Over".
        private static string StateLine(JObject ev, bool full) {
            var now = Online.Events.Now;
            var state = (string)ev["state"];
            var startAt = full ? (long?)ev["settings"]?["startAt"] ?? 0 : (long?)ev["startAt"] ?? 0;
            switch (state) {
                case "waiting":
                    return startAt > 0 ? (startAt > now ? "Starts in " + Online.Events.Span(startAt - now) : "Starting…") : "Starts when the host says";
                case "building":
                    return full ? "Building · " + Online.Events.Span((long)ev["buildEndsAt"] - now) + " left" : "Building";
                case "playing":
                    if (!full) return "Playing";
                    var rounds = (JArray)ev["rounds"];
                    var current = 0;
                    for (var r = 0; r < rounds.Count; r++)
                        if (((JArray)rounds[r]).Any(m => (string)m["state"] != "done")) {
                            current = r;
                            break;
                        }
                    return Online.Events.RoundName(ev, current) + " (" + (current + 1) + " of " + rounds.Count + ")";
                default:
                    return (bool?)ev["cancelled"] == true ? "Ended" : "Over";
            }
        }

        private void ChampionRow(Transform parent, JObject c) {
            var row = Ui.Image(parent, "Champion", OnlineWindow.GlassRow, 8f).rectTransform;
            Ui.Row(row, 12f, new RectOffset(14, 10, 2, 2));
            row.Size(-1, 52f);
            var cup = Ui.Image(row, "Cup", new Color(1f, 0.78f, 0.3f), 15f);
            cup.Size(30f, 30f);
            var one = Ui.Text(cup.transform, "1", "1", Theme.FontBody, new Color(0.25f, 0.17f, 0f), Theme.Medium, TextAlignmentOptions.Center);
            one.rectTransform.Fill();
            var column = Ui.Rect("Who", row);
            Ui.Column(column, 0f);
            column.Size(-1, 46f, 1f);
            Ui.Text(column, "Champion", NameFilter.Shown((string)c["champion"]) + (string.IsNullOrEmpty((string)c["robot"]) ? "" : "  ·  " + NameFilter.Shown((string)c["robot"], "Robot")),
                Theme.FontBody + 1f, Color.white, Theme.Medium).Size(-1, 24f);
            var how = (string)c["how"];
            Ui.Text(column, "Event", NameFilter.Shown((string)c["name"], "Event") + "  ·  " + (string.IsNullOrEmpty(how) ? "won the final " + (string)c["score"] : "won the final (" + how + ")")
                                     + " vs " + NameFilter.Shown((string)c["runnerUp"]),
                Theme.FontSmall, Dim(0.6f)).Size(-1, 20f);
            var date = (long?)c["date"] ?? 0;
            Ui.Text(row, "Players", ((int?)c["players"] ?? 0) + " players", Theme.FontSmall, Dim(0.6f)).Size(80f, 46f);
            Ui.Text(row, "When", date > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(date).ToLocalTime().ToString("MMM d, yyyy") : "", Theme.FontSmall, Dim(0.5f), null,
                TextAlignmentOptions.MidlineRight).Size(100f, 46f);
        }

        private async void JoinEvent(string raw) {
            var code = new string((raw ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (code.Length < 4) {
                Interface.Interface.Say("Type the event's code (6 letters and numbers)", false);
                return;
            }
            if (busy) return;
            busy = true;
            var result = await Online.Events.Call("join", code, new Dictionary<string, object> { { "name", OnlineServices.Name } });
            busy = false;
            if (this == null) return;
            if (!Online.Events.Ok(result)) {
                Interface.Interface.Say(Online.Events.Reason(result), false);
                return;
            }
            Online.Events.Remember(code);
            Audio.Sounds.Play(Audio.Sounds.Tick);
            ActionLog.Write("event", "joined " + code);
            ShowEvent(code);
        }

        // ------------------------------------------------------------------ Host an event

        private void EventHostPage() {
            var f = form ??= new EventForm { name = OnlineServices.Name + "'s tournament" };
            Heading("Host an event", "A bracket of 1v1 matches played online. Everyone joins with the code; you can change your mind about everything until it starts.");

            var nameRow = Row(44f);
            Ui.Text(nameRow, "Label", "Name", Theme.FontBody, Dim(0.7f)).Size(150f, 40f);
            var name = DarkField(nameRow, "Name", f.name, v => f.name = v);
            name.characterLimit = 40;
            name.Size(380f, 40f);
            var descRow = Row(44f);
            Ui.Text(descRow, "Label", "Description", Theme.FontBody, Dim(0.7f)).Size(150f, 40f);
            var desc = DarkField(descRow, "Desc", f.desc, v => f.desc = v);
            desc.characterLimit = 200;
            desc.Size(-1, 40f, 1f);

            var whenRow = Row(44f);
            Ui.Text(whenRow, "Label", "Starts", Theme.FontBody, Dim(0.7f)).Size(150f, 40f);
            ToolOptions.Segmented(whenRow, new[] { "When I press Start", "In…", "At a time" }, f.when, i => {
                f.when = i;
                Rebuild();
            }, 150f);
            Fixed(whenRow, 470f);
            if (f.when == 1) {
                var inRow = Row(40f);
                Ui.Rect("Space", inRow).Size(150f, 10f);
                ToolOptions.Segmented(inRow, InMinutes.Select(m => Online.Events.Minutes(m)).ToArray(), Array.IndexOf(InMinutes, f.inMinutes), i => {
                    f.inMinutes = InMinutes[i];
                    Rebuild();
                }, 80f);
                Fixed(inRow, 500f);
            }
            else if (f.when == 2) {
                var atRow = Row(40f);
                Ui.Rect("Space", atRow).Size(150f, 10f);
                var date = DarkField(atRow, "Date", f.date, v => f.date = v);
                date.characterLimit = 10;
                date.Size(130f, 38f);
                var time = DarkField(atRow, "Time", f.time, v => f.time = v);
                time.characterLimit = 5;
                time.Size(80f, 38f);
                var zone = Ui.Text(atRow, "Zone", "", Theme.FontSmall, Dim(0.55f));
                zone.Size(-1, 38f, 1f);
                Live(zone, () => {
                    var at = StartAt(f);
                    return at > 0 ? "your time (" + TimeZoneInfo.Local.StandardName + "): " + Online.Events.When(at) + ". Everyone sees it in their own time."
                        : "date as 2026-12-05, time as 15:00 (your time)";
                });
            }

            var robotsRow = Row(44f);
            Ui.Text(robotsRow, "Label", "Robots", Theme.FontBody, Dim(0.7f)).Size(150f, 40f);
            ToolOptions.Segmented(robotsRow, new[] { "Any robot", "Build a new one" }, f.build ? 1 : 0, i => {
                f.build = i == 1;
                Rebuild();
            }, 150f);
            Fixed(robotsRow, 310f);
            var robotsNote = Ui.Text(content, "Note", f.build
                    ? "When it starts, everyone gets the same time to build a new robot; when time's up, the build that's open is locked in."
                    : "Players lock in a saved robot (it needs a V5 Brain) before their first match.",
                Theme.FontSmall, Dim(0.5f));
            robotsNote.margin = new Vector4(160f, 0f, 0f, 0f);
            robotsNote.Size(-1, 20f);
            if (f.build) {
                var buildRow = Row(40f);
                Ui.Text(buildRow, "Label", "Build time", Theme.FontBody, Dim(0.7f)).Size(150f, 38f);
                ToolOptions.Segmented(buildRow, BuildMinutes.Select(m => Online.Events.Minutes(m).Replace(" hours", " h").Replace(" hour", " h")).ToArray(),
                    Array.IndexOf(BuildMinutes, f.buildMinutes), i => {
                        f.buildMinutes = BuildMinutes[i];
                        Rebuild();
                    }, 66f);
                Fixed(buildRow, 560f);
            }

            var callRow = Row(40f);
            Ui.Text(callRow, "Label", "Call time", Theme.FontBody, Dim(0.7f)).Size(150f, 38f);
            ToolOptions.Segmented(callRow, CallSeconds.Select(s => s / 60 + " min").ToArray(), Array.IndexOf(CallSeconds, f.callSeconds), i => {
                f.callSeconds = CallSeconds[i];
                Rebuild();
            }, 70f);
            Fixed(callRow, 300f);
            var callNote = Ui.Text(callRow, "Note", "how long a called match waits; then a player who isn't there is a no-show (VEX's rule)", Theme.FontSmall, Dim(0.5f));
            callNote.textWrappingMode = TextWrappingModes.Normal;
            callNote.Size(-1, 38f, 1f);

            var playRow = Row(36f);
            Ui.Rect("Space", playRow).Size(150f, 10f);
            ToolOptions.Switch(playRow, "I'm playing too", f.play, v => f.play = v);
            var autonRow = Row(36f);
            Ui.Rect("Space", autonRow).Size(150f, 10f);
            ToolOptions.Switch(autonRow, "Autonomous period (0:15 of each robot's program, then 1:45 driver)", f.autonomous, v => f.autonomous = v);
            var listRow = Row(36f);
            Ui.Rect("Space", listRow).Size(150f, 10f);
            ToolOptions.Switch(listRow, "List it in Open events (otherwise only people with the code can find it)", f.listed, v => f.listed = v);

            Ui.Rect("Gap", content).Size(-1, 8f);
            var go = Row(46f);
            Ui.Rect("Space", go).Size(150f, 10f);
            var make = Ui.Button(go, "Create", busy ? "Creating…" : "Create event", CreateEvent, true, 8f, Theme.FontTitle);
            make.Size(210f, 44f);
            make.interactable = !busy;
            var back = Ui.Button(go, "Back", "Back", () => Show(Page.Events), false, 8f);
            back.targetGraphic.color = Dim(0.08f);
            back.Size(100f, 44f);
        }

        // The form's date and time (the player's own time zone) as UTC ms, or 0 when they don't read.
        private static long StartAt(EventForm f) {
            if (!DateTime.TryParseExact(f.date.Trim() + " " + f.time.Trim(), new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-M-d H:mm" },
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local)) return 0;
            return new DateTimeOffset(local).ToUnixTimeMilliseconds();
        }

        private async void CreateEvent() {
            var f = form;
            if (f == null || busy) return;
            if (string.IsNullOrWhiteSpace(f.name)) {
                Interface.Interface.Say("Give the event a name", false);
                return;
            }
            if (!NameFilter.Allowed(f.name) || !NameFilter.Allowed(f.desc)) {
                Interface.Interface.Say("That name isn't allowed: choose another", false);
                return;
            }
            var data = new Dictionary<string, object> {
                { "name", f.name.Trim() }, { "desc", f.desc.Trim() }, { "robots", f.build ? "build" : "any" }, { "buildMinutes", f.buildMinutes },
                { "callSeconds", f.callSeconds }, { "listed", f.listed }, { "autonomous", f.autonomous }, { "play", f.play }, { "playerName", OnlineServices.Name },
            };
            if (f.when == 1) data["startIn"] = f.inMinutes * 60;
            else if (f.when == 2) {
                var at = StartAt(f);
                if (at <= 0) {
                    Interface.Interface.Say("Type the date as 2026-12-05 and the time as 15:00", false);
                    return;
                }
                if (at < Online.Events.Now + 60000) {
                    Interface.Interface.Say("That time has passed: choose a later one", false);
                    return;
                }
                data["startAt"] = at;
            }
            busy = true;
            Rebuild();
            var result = await Online.Events.Call("create", null, data);
            busy = false;
            if (this == null) return;
            if (!Online.Events.Ok(result)) {
                Interface.Interface.Say(Online.Events.Reason(result), false);
                Rebuild();
                return;
            }
            var code = (string)result["code"];
            Online.Events.Remember(code);
            ActionLog.Write("event", "created " + code);
            ShowEvent(code);
        }

        // ------------------------------------------------------------------ an event's page

        private void EventPage() {
            if (shownEvent == null || !Online.Events.Known.TryGetValue(shownEvent, out var ev)) {
                Heading("Event", "Loading…");
                return;
            }
            var code = (string)ev["code"];
            var state = (string)ev["state"];
            var isHost = Online.Events.IsHost(ev);
            var me = Online.Events.Mine(ev);
            var inIt = Online.Events.In(ev);
            var desc = (string)ev["desc"];
            Heading(NameFilter.Shown((string)ev["name"], "Event"), "Hosted by " + NameFilter.Shown((string)ev["hostName"]) + (isHost ? " (you)" : "") + " · " + Online.Events.Rules(ev)
                                                                + (string.IsNullOrEmpty(desc) ? "" : " · " + NameFilter.Shown(desc, "")));

            // The big line, the code and Copy invite.
            var top = Ui.Image(content, "Status", OnlineWindow.GlassDeep, 12f).rectTransform;
            Ui.Row(top, 16f, new RectOffset(20, 14, 6, 6));
            top.Size(-1, 72f);
            var big = Ui.Text(top, "When", "", 24f, Color.white, Theme.Medium);
            big.Size(-1, 60f, 1f);
            Live(big, () => BigLine(ev));
            var codeText = Ui.Text(top, "Code", OnlineWindow.Spaced(code), 26f, Dim(0.9f), Theme.Medium, TextAlignmentOptions.Center);
            codeText.characterSpacing = 8f;
            codeText.Size(150f, 60f);
            var copy = Ui.Button(top, "Copy", eventCopied > Time.unscaledTime ? "Copied!" : "Copy invite", () => {
                GUIUtility.systemCopyBuffer = "Join my Protobot event \"" + NameFilter.Shown((string)ev["name"], "Event") + "\": Multiplayer > Events > code " + code
                                              + ((long?)ev["settings"]?["startAt"] > 0 ? " (starts " + Online.Events.When((long)ev["settings"]["startAt"]) + " my time)" : "");
                Audio.Sounds.Play(Audio.Sounds.Tick);
                eventCopied = Time.unscaledTime + 2f;
                Rebuild();
            }, true, 8f);
            copy.Size(140f, 42f);
            Ui.Tip(copy, "Copy invite", null, "A message with the code, to paste in Discord or a chat.");

            YourPart(ev, me, inIt, state);
            if (isHost) HostControls(ev, state);

            // Bracket / Players / What happened.
            var tabRow = Row(38f);
            var hasBracket = (ev["rounds"] as JArray)?.Count > 0;
            var tabs = new[] { hasBracket ? "Bracket" : "Bracket (when it starts)", "Players (" + ((ev["players"] as JArray)?.OfType<JObject>().Count(p => (bool?)p["out"] != true) ?? 0) + ")", "What happened" };
            ToolOptions.Segmented(tabRow, tabs, eventTab, i => {
                eventTab = i;
                Rebuild();
            }, 170f);
            Fixed(tabRow, 520f);
            if (eventTab == 0 && hasBracket) Bracket(ev, isHost);
            else if (eventTab == 2) History(ev);
            else Roster(ev, isHost, state);
        }

        private static string BigLine(JObject ev) {
            var now = Online.Events.Now;
            var state = (string)ev["state"];
            var startAt = (long?)ev["settings"]?["startAt"] ?? 0;
            switch (state) {
                case "waiting":
                    if (startAt <= 0) return "Starts when the host presses Start";
                    return startAt > now ? "Starts " + Online.Events.When(startAt) + "  ·  in " + Online.Events.Span(startAt - now) : "Starting (it needs 2 players)…";
                case "building":
                    return "Building  ·  " + Online.Events.Span((long)ev["buildEndsAt"] - now) + " left";
                case "playing":
                    return StateLine(ev, true);
                default:
                    if ((bool?)ev["cancelled"] == true) return "The host ended the event";
                    return Online.Events.NameOf(ev, (string)ev["champion"]) + " is the champion!";
            }
        }

        // What this player does now: join, lock in a robot, build, play their match, leave.
        private void YourPart(JObject ev, JObject me, bool inIt, string state) {
            var code = (string)ev["code"];
            var row = Row(46f);
            if (!inIt) {
                if (state == "waiting" || state == "building") {
                    Ui.Text(row, "You", "You aren't in this event.", Theme.FontBody, Dim(0.7f)).Size(-1, 42f, 1f);
                    Ui.Button(row, "Join", "Join event", () => JoinEvent(code), true, 8f).Size(150f, 40f);
                }
                else Ui.Text(row, "You", state == "done" ? "The event is over." : "The event has started: you can follow it here.", Theme.FontBody, Dim(0.7f)).Size(-1, 42f, 1f);
                return;
            }
            var build = (string)ev["settings"]?["robots"] == "build";
            var locked = (bool?)me["locked"] == true;
            var robot = (string)me["robot"];
            var match = Online.Events.MyMatch(ev);
            if (match != null && state == "playing") {
                // Your match is up.
                var opponent = Online.Events.NameOf(ev, Online.Events.Opponent(ev, match));
                var card = Ui.Image(row, "Match", new Color(0.157f, 0.522f, 0.957f, 0.35f), 10f).rectTransform;
                Ui.Row(card, 10f, new RectOffset(14, 8, 4, 4));
                card.Size(-1, 44f, 1f);
                var line = Ui.Text(card, "Line", "", Theme.FontBody + 1f, Color.white, Theme.Medium);
                line.Size(-1, 36f, 1f);
                var m = match;
                Live(line, () => {
                    var s = (string)m["state"];
                    var label = Online.Events.MatchLabel(ev, (string)m["id"]);
                    if (s == "playing") return label + " vs " + opponent + ": playing";
                    if (s == "disputed") return label + " vs " + opponent + ": the scores didn't agree, the host decides";
                    var playing = EventsRunner.Playing && EventsRunner.PlayingMatch == (string)m["id"];
                    return label + " vs " + opponent + (playing ? ": getting the match ready… " : ": join within ") + Online.Events.Span((long)m["deadline"] - Online.Events.Now);
                });
                if ((string)match["state"] == "called" && !(EventsRunner.Playing && EventsRunner.PlayingMatch == (string)match["id"])) {
                    var join = Ui.Button(card, "Join", "Join now", () => {
                        EventsRunner.Ensure();
                        EventsRunner.Instance.Join(code, (string)m["id"]);
                    }, true, 6f);
                    join.Size(110f, 34f);
                    if ((bool?)me["timeout"] != true) {
                        var timeout = SmallButton(card, "Time-out", async () => {
                            var r = await Online.Events.Call("timeout", code, new Dictionary<string, object> { { "match", (string)m["id"] } });
                            if (!Online.Events.Ok(r)) Interface.Interface.Say(Online.Events.Reason(r), false);
                        }, 90f);
                        Ui.Tip(timeout, "Take a time-out", null, "Adds 3 minutes before this match must begin. You get one in the whole event (VEX allows one per alliance in eliminations).");
                    }
                }
                else if (MatchSessions.Current != null && !OnlineMatch.Active) {
                    var lobby = SmallButton(card, "Lobby", () => Show(Page.Lobby), 80f);
                    Ui.Tip(lobby, "Match lobby", null, "The match starts by itself when you're both in and ready.");
                }
            }
            else if (state == "waiting" && build) {
                Ui.Text(row, "You", "When it starts you'll have " + Online.Events.Minutes((int?)ev["settings"]?["buildMinutes"] ?? 60) + " to build a new robot.", Theme.FontBody, Dim(0.8f)).Size(-1, 42f, 1f);
            }
            else if (state == "building") {
                var text = Ui.Text(row, "You", "", Theme.FontBody, Color.white);
                text.Size(-1, 42f, 1f);
                if (locked) Live(text, () => "Your robot is locked in: " + NameFilter.Shown(robot, "Robot") + ". Matches start when building ends.");
                else {
                    Live(text, () => "Build your robot: " + Online.Events.Span((long)ev["buildEndsAt"] - Online.Events.Now) + " left. The build that's open is locked in when time's up.");
                    SmallButton(row, "New build", () => {
                        OnlineWindow.Close();
                        FindAnyObjectByType<BuildsManager>()?.CreateNewBuild();
                    }, 110f);
                    SmallButton(row, "Go build", () => BackToBuild(), 100f);
                    SmallButton(row, "I'm done", () => LockRobot(code), 100f, true);
                }
            }
            else if (state == "waiting" || state == "playing") {
                var file = Online.Events.RobotFile(code);
                var text = Ui.Text(row, "You", "", Theme.FontBody, Color.white);
                text.Size(-1, 42f, 1f);
                if (locked && file != null) {
                    text.text = "Your robot: " + NameFilter.Shown(robot, "Robot") + " (locked in)" + (state == "playing" ? ". Wait for your match to be called." : "");
                    if (state == "waiting")
                        SmallButton(row, "Change", async () => {
                            var r = await Online.Events.Call("robot", code, new Dictionary<string, object> { { "name", robot }, { "locked", false } });
                            if (!Online.Events.Ok(r)) Interface.Interface.Say(Online.Events.Reason(r), false);
                        }, 90f);
                }
                else {
                    var open = MatchSessions.RobotName();
                    text.text = string.IsNullOrEmpty(open) ? "Lock in your robot: open its build (it needs a V5 Brain)." : "Lock in your robot. Open now: " + open;
                    SmallButton(row, "Choose robot…", ChooseRobot, 130f);
                    var lockIn = SmallButton(row, "Lock in", () => LockRobot(code), 100f, true);
                    lockIn.interactable = !string.IsNullOrEmpty(open);
                    Ui.Tip(lockIn, "Lock in", null, "This build becomes your robot for every match of the event (a copy is kept, so later changes don't count).");
                }
            }
            else Ui.Text(row, "You", (string)ev["champion"] == Online.Events.Me ? "You won the event!" : "Thanks for playing.", Theme.FontBody, Dim(0.8f)).Size(-1, 42f, 1f);

            if (state != "done") {
                var leave = SmallButton(row, "Leave", () => {
                    var after = state == "playing" ? "Leaving now forfeits your match. Leave the event?" : "Leave the event?";
                    var dialog = OnlineWindow.GlassDialog("Leave event", new Vector2(420f, 190f));
                    Ui.Column(dialog.Body, 8f);
                    var text = Ui.Text(dialog.Body, "Text", after, Theme.FontBody, Color.white);
                    text.textWrappingMode = TextWrappingModes.Normal;
                    text.Size(-1, 50f);
                    Ui.Button(dialog.Footer, "Stay", "Stay", dialog.Close, false, 6f).Size(-1, 34f);
                    Ui.Button(dialog.Footer, "Leave", "Leave", async () => {
                        dialog.Close();
                        var r = await Online.Events.Call("leave", code);
                        if (!Online.Events.Ok(r)) {
                            Interface.Interface.Say(Online.Events.Reason(r), false);
                            return;
                        }
                        Online.Events.Forget(code);
                        if (EventsRunner.PlayingCode == code) EventsRunner.Instance.Stop();
                        Show(Page.Events);
                    }, true, 6f).Size(-1, 34f);
                }, 70f);
                leave.targetGraphic.color = new Color(0.9f, 0.3f, 0.3f, 0.2f);
            }
        }

        private void BackToBuild() {
            OnlineWindow.Close();
            var splash = Resources.FindObjectsOfTypeAll<Button>().FirstOrDefault(b => b.name == "Close Button" && b.gameObject.scene.IsValid() && b.gameObject.activeInHierarchy);
            if (splash != null) splash.onClick.Invoke();
        }

        private async void LockRobot(string code) {
            if (busy) return;
            busy = true;
            var problem = await Online.Events.LockIn(code);
            busy = false;
            if (this == null) return;
            if (problem != null) Interface.Interface.Say(problem, false);
            else Audio.Sounds.Play(Audio.Sounds.Tick);
            dirty = true;
        }

        private void HostControls(JObject ev, string state) {
            var code = (string)ev["code"];
            var row = Row(40f);
            Ui.Text(row, "Label", "Host", Theme.FontSmall, Dim(0.55f), Theme.Medium).Size(46f, 38f);
            if (state == "waiting") {
                var start = SmallButton(row, "Start now", () => HostAction("start", code, null), 110f, true);
                Ui.Tip(start, "Start now", null, (string)ev["settings"]?["robots"] == "build" ? "Starts the build timer for everyone." : "Makes the bracket (random seeds) and calls the first matches.");
            }
            if (state != "done") {
                var message = DarkField(row, "Message", "", _ => { }, Theme.FontSmall);
                message.characterLimit = 300;
                message.Size(-1, 32f, 1f);
                var hint = Ui.Text(message.textViewport, "Placeholder", "A message for everyone in the event…", Theme.FontSmall, Dim(0.35f));
                hint.rectTransform.Fill();
                message.placeholder = hint;
                SmallButton(row, "Send", () => {
                    if (string.IsNullOrWhiteSpace(message.text)) return;
                    HostAction("message", code, new Dictionary<string, object> { { "text", message.text } });
                }, 70f);
            }
            var end = SmallButton(row, state == "done" ? "Remove event" : "End event", () => {
                var dialog = OnlineWindow.GlassDialog(state == "done" ? "Remove event" : "End event", new Vector2(440f, 190f));
                Ui.Column(dialog.Body, 8f);
                var text = Ui.Text(dialog.Body, "Text", state == "done" ? "Take the event off the list? Its champion stays on the Champions list." : "End the event for everyone now? Matches stop and there's no champion.",
                    Theme.FontBody, Color.white);
                text.textWrappingMode = TextWrappingModes.Normal;
                text.Size(-1, 54f);
                Ui.Button(dialog.Footer, "Cancel", "Cancel", dialog.Close, false, 6f).Size(-1, 34f);
                Ui.Button(dialog.Footer, "End", state == "done" ? "Remove" : "End event", () => {
                    dialog.Close();
                    HostAction("end", code, null);
                }, true, 6f).Size(-1, 34f);
            }, 110f);
            end.targetGraphic.color = new Color(0.9f, 0.3f, 0.3f, 0.2f);
        }

        private async void HostAction(string action, string code, Dictionary<string, object> data) {
            var result = await Online.Events.Call(action, code, data);
            if (!Online.Events.Ok(result)) Interface.Interface.Say(Online.Events.Reason(result), false);
            else Audio.Sounds.Play(Audio.Sounds.Tick);
            if (this != null) dirty = true;
        }

        // The players: name, robot, seed, state; the host can remove one.
        private void Roster(JObject ev, bool isHost, string state) {
            var list = ScrollList(240f);
            var players = (ev["players"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            if (players.Count == 0) Ui.Text(list, "Empty", "Nobody has joined yet: share the code.", Theme.FontBody, Dim(0.6f)).Size(-1, 34f);
            var messages = (ev["messages"] as JArray)?.OfType<JObject>().ToList();
            if (messages != null && messages.Count > 0) {
                var last = messages[messages.Count - 1];
                var note = Ui.Text(list, "Message", "From the host: " + (string)last["text"], Theme.FontBody, new Color(1f, 0.85f, 0.45f));
                note.textWrappingMode = TextWrappingModes.Normal;
                note.Size(-1, 40f);
            }
            foreach (var p in players.OrderBy(p => (bool?)p["out"] == true).ThenBy(p => (int?)p["seed"] ?? 99)) {
                var id = (string)p["id"];
                var row = Ui.Image(list, "Player", id == Online.Events.Me ? Dim(0.1f) : OnlineWindow.GlassRow, 8f).rectTransform;
                Ui.Row(row, 12f, new RectOffset(14, 10, 2, 2));
                row.Size(-1, 40f);
                var locked = (bool?)p["locked"] == true;
                var robot = (string)p["robot"];
                var dot = Ui.Image(row, "State", (bool?)p["out"] == true ? Dim(0.15f) : locked ? Theme.Success : string.IsNullOrEmpty(robot) ? Dim(0.3f) : Theme.Warning, 6f);
                dot.Size(12f, 12f);
                var seed = (int?)p["seed"];
                Ui.Text(row, "Seed", seed.HasValue ? "#" + seed : "", Theme.FontSmall, Dim(0.5f)).Size(32f, 36f);
                var name = Ui.Text(row, "Name", Online.Events.NameOf(ev, id) + (id == (string)ev["host"] ? "  · host" : "") + (id == Online.Events.Me ? "  · you" : ""),
                    Theme.FontBody, (bool?)p["out"] == true ? Dim(0.4f) : Color.white, Theme.Medium);
                name.Size(-1, 36f, 1f);
                var robotLine = (bool?)p["out"] == true ? "out" : locked ? NameFilter.Shown(robot, "Robot") + " · locked in"
                    : state == "building" ? "building" : string.IsNullOrEmpty(robot) ? "no robot yet" : NameFilter.Shown(robot, "Robot");
                Ui.Text(row, "Robot", robotLine, Theme.FontSmall, Dim(0.6f), null, TextAlignmentOptions.MidlineRight).Size(220f, 36f);
                if (isHost && id != Online.Events.Me && (bool?)p["out"] != true && state != "done") {
                    SmallButton(row, "Remove", () => HostAction("remove", (string)ev["code"], new Dictionary<string, object> { { "player", id } }), 80f);
                }
            }
        }

        private void History(JObject ev) {
            var list = ScrollList(240f);
            var log = (ev["log"] as JArray)?.OfType<JObject>().Reverse().ToList() ?? new List<JObject>();
            foreach (var entry in log) {
                var row = Ui.Rect("Entry", list);
                Ui.Row(row, 12f, new RectOffset(6, 6, 0, 0));
                row.Size(-1, 26f);
                var at = (long?)entry["at"] ?? 0;
                Ui.Text(row, "When", DateTimeOffset.FromUnixTimeMilliseconds(at).ToLocalTime().ToString("h:mm tt"), Theme.FontSmall, Dim(0.45f)).Size(70f, 24f);
                Ui.Text(row, "Text", (string)entry["text"], Theme.FontSmall, Dim(0.85f)).Size(-1, 24f, 1f);
            }
        }

        // The bracket: a column per round, each match a box (both players, scores, the winner bright), lines to the next.
        private void Bracket(JObject ev, bool isHost) {
            var rounds = (JArray)ev["rounds"];
            const float boxW = 220f, boxH = 56f, gapX = 46f, slot = 70f, headH = 26f;
            var first = ((JArray)rounds[0]).Count;
            var height = headH + first * slot;
            var width = rounds.Count * (boxW + gapX);

            var frame = Ui.Image(content, "Bracket", OnlineWindow.GlassDeep, 10f, true);
            frame.Size(-1, 260f);
            frame.GetComponent<LayoutElement>().flexibleHeight = 1f;
            var viewport = frame.rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();
            var area = Ui.Rect("Area", viewport);
            area.anchorMin = area.anchorMax = area.pivot = new Vector2(0f, 1f);
            area.sizeDelta = new Vector2(width + 16f, height + 16f);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = area;
            scroll.horizontal = true;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var me = Online.Events.Me;
            for (var r = 0; r < rounds.Count; r++) {
                var list = (JArray)rounds[r];
                var x = 12f + r * (boxW + gapX);
                var head = Ui.Text(area, "Round", Online.Events.RoundName(ev, r), Theme.FontSmall, Dim(0.55f), Theme.Medium);
                Place(head.rectTransform, x, 8f, boxW, 20f);
                var step = first * slot / list.Count;
                for (var i = 0; i < list.Count; i++) {
                    var m = (JObject)list[i];
                    var y = 8f + headH + step * (i + 0.5f) - boxH / 2f;
                    var mState = (string)m["state"];
                    var a = (string)m["a"];
                    var b = (string)m["b"];
                    var winner = (string)m["winner"];
                    var bye = (string)m["note"] == "bye";
                    var fill = mState == "called" ? new Color(0.157f, 0.522f, 0.957f, 0.35f) : mState == "playing" ? new Color(0.2f, 0.7f, 0.4f, 0.35f)
                        : mState == "disputed" ? new Color(0.9f, 0.3f, 0.3f, 0.35f) : Dim(bye ? 0.03f : 0.07f);
                    var box = Ui.Image(area, "Match " + m["id"], fill, 8f, true);
                    Place(box.rectTransform, x, y, boxW, boxH);
                    if (a == me || b == me) {
                        var outline = box.gameObject.AddComponent<Outline>();
                        outline.effectColor = new Color(1f, 1f, 1f, 0.5f);
                        outline.effectDistance = new Vector2(1f, -1f);
                    }
                    var note = (string)m["note"];
                    var played = string.IsNullOrEmpty(note) || note.StartsWith("it was a tie");   // awarded, no-shows, byes: no score
                    Line(box.rectTransform, ev, a, (int?)m["sa"] ?? 0, winner, mState, 0, bye, played);
                    Line(box.rectTransform, ev, b, (int?)m["sb"] ?? 0, winner, mState, 1, bye, played);
                    var tipText = mState == "called" ? "Called: waiting for both players (no-show at the deadline)" : mState == "playing" ? "Being played now"
                        : mState == "disputed" ? "The scores sent in didn't agree: the host decides" : mState == "done" ? (string.IsNullOrEmpty(note) ? "Played" : note) : "Waiting for the matches before";
                    var tip = box.gameObject.AddComponent<Button>();
                    tip.transition = Selectable.Transition.None;
                    Ui.Tip(tip, Online.Events.MatchLabel(ev, (string)m["id"]), null, tipText + (isHost && (mState == "called" || mState == "playing" || mState == "disputed") ? ". Click to award it or play it again." : ""));
                    if (isHost && (mState == "called" || mState == "playing" || mState == "disputed")) {
                        var matchId = (string)m["id"];
                        var nameA = Online.Events.NameOf(ev, a);
                        var nameB = Online.Events.NameOf(ev, b);
                        tip.onClick.AddListener(() => Popup.Open(box.rectTransform, new[] { "Award to " + nameA, "Award to " + nameB, "Play it again" }, -1, choice => {
                            if (choice == 2) HostAction("replay", (string)ev["code"], new Dictionary<string, object> { { "match", matchId } });
                            else HostAction("award", (string)ev["code"], new Dictionary<string, object> { { "match", matchId }, { "winner", choice == 0 ? "a" : "b" } });
                        }));
                    }
                    // A line to the next round.
                    if (r < rounds.Count - 1) {
                        var cy = y + boxH / 2f;
                        var nextStep = first * slot / (list.Count / 2f);
                        var ny = 8f + headH + nextStep * (i / 2 + 0.5f);
                        var h = Ui.Image(area, "Link", Dim(0.18f));
                        Place(h.rectTransform, x + boxW, cy - 1f, gapX / 2f, 2f);
                        var v = Ui.Image(area, "Link", Dim(0.18f));
                        Place(v.rectTransform, x + boxW + gapX / 2f - 1f, Mathf.Min(cy, ny) - 1f, 2f, Mathf.Abs(ny - cy) + 2f);
                        var h2 = Ui.Image(area, "Link", Dim(0.18f));
                        Place(h2.rectTransform, x + boxW + gapX / 2f, ny - 1f, gapX / 2f, 2f);
                    }
                }
            }
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h) {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            var layout = rect.GetComponent<LayoutElement>();
            if (layout != null) layout.ignoreLayout = true;
        }

        // One player's line in a bracket box.
        private static void Line(RectTransform box, JObject ev, string id, int score, string winner, string state, int index, bool bye, bool played) {
            var won = !string.IsNullOrEmpty(winner) && winner == id;
            var lost = !string.IsNullOrEmpty(winner) && winner != id;
            var name = string.IsNullOrEmpty(id) ? (bye ? "bye" : "…") : Online.Events.NameOf(ev, id);
            var color = string.IsNullOrEmpty(id) ? Dim(0.3f) : lost ? Dim(0.45f) : Color.white;
            var text = Ui.Text(box, "Player", name, Theme.FontSmall, color, won ? Theme.Medium : null);
            Place(text.rectTransform, 10f, 4f + index * 25f, 160f, 24f);
            if (state == "done" && played && !bye && !string.IsNullOrEmpty(id)) {
                var s = Ui.Text(box, "Score", score.ToString(), Theme.FontSmall, color, won ? Theme.Medium : null, TextAlignmentOptions.MidlineRight);
                Place(s.rectTransform, 170f, 4f + index * 25f, 40f, 24f);
            }
            if (won) {
                var mark = Ui.Image(box, "Won", Theme.Accent, 2f);
                Place(mark.rectTransform, 2f, 8f + index * 25f, 3f, 16f);
            }
        }
    }
}
