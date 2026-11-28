using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Protobot.Builds;
using Protobot.Interface;
using TMPro;
using Unity.Services.CloudCode;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Online {
    /// <summary>
    /// V2 (build 88): events and tournaments (Docs/Events concept.md). The event lives on the server (Cloud Code
    /// "Events", Assets/Protobot/Cloud/Events.js: roster, start time, build timer, bracket, match calls, no-shows,
    /// results, champions); this is the app's side: calling it, keeping the events this player joined
    /// (<see cref="JoinedKey"/>), and the robot locked in for each (a copy of its build in persistentDataPath/Events).
    /// <see cref="EventsRunner"/> polls them, says when a match is called, runs the match (an invite-only 1v1 session
    /// hosted by the higher seed) and sends the score.
    /// </summary>
    public static class Events {
        public const string JoinedKey = "V2.Events.Joined";

        /// <summary>The events seen lately, by code (the server's JSON).</summary>
        public static readonly Dictionary<string, JObject> Known = new();
        /// <summary>Raised when an event in <see cref="Known"/> changed.</summary>
        public static event Action Changed;

        private static long offset;   // the server's clock minus this computer's (ms)

        /// <summary>The server's time now (UTC ms), so countdowns are the same for everyone.</summary>
        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + offset;

        public static string Me => OnlineServices.PlayerId;

        // ------------------------------------------------------------------ the server

        /// <summary>Calls the server. Never throws: a problem comes back as { ok: false, reason }.</summary>
        public static async Task<JObject> Call(string action, string code = null, Dictionary<string, object> data = null) {
            try {
                if (!AgeCheck.Ready) return Fail("answer the age question first (Multiplayer)");
                if (!await OnlineServices.Start()) return Fail("you're offline: " + OnlineServices.Problem);
                var args = new Dictionary<string, object> { { "action", action } };
                if (!string.IsNullOrEmpty(code)) args["code"] = code;
                if (data != null) args["data"] = data;
                var json = await CloudCodeService.Instance.CallEndpointAsync("Events", args);
                var result = JObject.Parse(json);
                if (result["now"] != null) offset = (long)result["now"] - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (result["event"] is JObject ev) Store(ev);
                return result;
            }
            catch (Exception e) {
                Debug.LogWarning("V2 events " + action + ": " + e.Message);
                return Fail("the events server didn't answer: try again");
            }
        }

        private static JObject Fail(string reason) => new() { ["ok"] = false, ["reason"] = reason };

        public static bool Ok(JObject result) => result != null && (bool?)result["ok"] == true;
        public static string Reason(JObject result) => (string)result?["reason"] ?? "something went wrong";

        private static void Store(JObject ev) {
            var code = (string)ev["code"];
            if (string.IsNullOrEmpty(code)) return;
            if (Known.TryGetValue(code, out var old) && JToken.DeepEquals(old, ev)) return;
            Known[code] = ev;
            if ((string)ev["state"] == "done" || (bool?)ev["deleted"] == true) {
                if (ev["players"] is JArray players && !players.Any(p => (string)p["id"] == Me)) Forget(code);
            }
            Changed?.Invoke();
        }

        public static async Task<JObject> Get(string code) => await Call("get", code);

        // ------------------------------------------------------------------ joined events

        public static List<string> Joined() =>
            PlayerPrefs.GetString(JoinedKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();

        public static void Remember(string code) {
            var list = Joined();
            if (list.Contains(code)) return;
            list.Add(code);
            PlayerPrefs.SetString(JoinedKey, string.Join(",", list.Skip(Math.Max(0, list.Count - 12))));
            PlayerPrefs.Save();
            EventsRunner.Ensure();
        }

        public static void Forget(string code) {
            var list = Joined();
            if (!list.Remove(code)) return;
            PlayerPrefs.SetString(JoinedKey, string.Join(",", list));
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ reading an event

        public static JObject Player(JObject ev, string id) => (ev?["players"] as JArray)?.OfType<JObject>().FirstOrDefault(p => (string)p["id"] == id);
        public static JObject Mine(JObject ev) => Player(ev, Me);
        public static bool In(JObject ev) => Mine(ev) is JObject p && (bool?)p["out"] != true;
        public static bool IsHost(JObject ev) => ev != null && (string)ev["host"] == Me;
        public static string NameOf(JObject ev, string id) => string.IsNullOrEmpty(id) ? "" : NameFilter.Shown((string)Player(ev, id)?["name"] ?? "?", "Player");
        public static IEnumerable<JObject> Matches(JObject ev) =>
            (ev?["rounds"] as JArray)?.OfType<JArray>().SelectMany(r => r.OfType<JObject>()) ?? Enumerable.Empty<JObject>();

        /// <summary>This player's match on now (called, being played or waiting on the host), if any.</summary>
        public static JObject MyMatch(JObject ev) {
            if ((string)ev?["state"] != "playing") return null;   // an event that ended leaves its matches as they were
            var me = Me;
            return Matches(ev).FirstOrDefault(m => ((string)m["a"] == me || (string)m["b"] == me)
                                                   && ((string)m["state"] == "called" || (string)m["state"] == "playing" || (string)m["state"] == "disputed"));
        }

        public static string Opponent(JObject ev, JObject match) => (string)match["a"] == Me ? (string)match["b"] : (string)match["a"];

        /// <summary>"Round 1", ..., "Semifinal", "Final".</summary>
        public static string RoundName(JObject ev, int round) {
            var count = (ev?["rounds"] as JArray)?.Count ?? 0;
            if (round == count - 1) return "Final";
            if (round == count - 2) return "Semifinal";
            if (round == count - 3) return "Quarterfinal";
            return "Round " + (round + 1);
        }

        public static string MatchLabel(JObject ev, string matchId) {
            var rounds = ev?["rounds"] as JArray;
            if (rounds == null) return "";
            for (var r = 0; r < rounds.Count; r++) {
                var list = (JArray)rounds[r];
                for (var i = 0; i < list.Count; i++)
                    if ((string)list[i]["id"] == matchId) return RoundName(ev, r) + (list.Count > 1 ? " · match " + (i + 1) : "");
            }
            return "";
        }

        /// <summary>The rules in a few words ("Any robot · 1v1 · single elimination").</summary>
        public static string Rules(JObject ev) {
            var s = ev?["settings"];
            if (s == null) return "";
            var robots = (string)s["robots"] == "build" ? "Build a new robot in " + Minutes((int?)s["buildMinutes"] ?? 60) : "Any robot";
            return robots + " · 1v1 · single elimination" + ((bool?)s["autonomous"] == false ? " · driver only" : "");
        }

        public static string Minutes(int minutes) =>
            minutes % 60 == 0 ? (minutes / 60) + (minutes == 60 ? " hour" : " hours") : minutes > 60 ? (minutes / 60) + " h " + (minutes % 60) + " min" : minutes + " min";

        /// <summary>A length of time: "2 h 14 min", "4:05", "12 s".</summary>
        public static string Span(long ms) {
            if (ms < 0) ms = 0;
            var s = ms / 1000;
            if (s >= 86400) return (s / 86400) + " d " + (s % 86400 / 3600) + " h";
            if (s >= 3600) return (s / 3600) + " h " + (s % 3600 / 60) + " min";
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        /// <summary>A moment in the player's own time zone: "Saturday 3:00 PM" (with the date when it's over a week off).</summary>
        public static string When(long utcMs) {
            var local = DateTimeOffset.FromUnixTimeMilliseconds(utcMs).ToLocalTime().DateTime;
            var today = DateTime.Now.Date;
            var day = local.Date == today ? "Today" : local.Date == today.AddDays(1) ? "Tomorrow"
                : (local.Date - today).TotalDays < 7 && local.Date > today ? local.ToString("dddd") : local.ToString("ddd d MMM yyyy");
            return day + " " + local.ToString("h:mm tt");
        }

        // ------------------------------------------------------------------ the robot

        /// <summary>The folder of an event's locked-in robot (its build, copied when it's locked in).</summary>
        public static string RobotFolder(string code) => Path.Combine(Application.persistentDataPath, "Events", code);

        /// <summary>The locked-in robot's build file for this event on this computer, or null.</summary>
        public static string RobotFile(string code) {
            var folder = RobotFolder(code);
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.pbb").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        }

        /// <summary>Locks in the build that's open as this player's robot: checks it can play, copies it, tells the server.
        /// Returns the problem, or null.</summary>
        public static async Task<string> LockIn(string code) {
            var problem = Game.SkillsGame.RobotProblem();
            if (problem != null) return problem;
            var name = MatchSessions.RobotName();
            if (string.IsNullOrEmpty(name)) return "Open the robot's build first";
            var file = Path.GetFileNameWithoutExtension(name);
            foreach (var c in Path.GetInvalidFileNameChars()) file = file.Replace(c, '_');
            if (string.IsNullOrWhiteSpace(file)) file = "Robot";
            var folder = RobotFolder(code);
            try {
                Directory.CreateDirectory(folder);
                foreach (var old in Directory.GetFiles(folder, "*.pbb")) File.Delete(old);
                BuildFile.Write(Path.Combine(folder, file + ".pbb"), SceneBuild.ToBuildData());
            }
            catch (Exception e) {
                Debug.LogWarning("V2 events: the robot couldn't be copied: " + e);
                return "The robot couldn't be saved for the event";
            }
            var result = await Call("robot", code, new Dictionary<string, object> { { "name", name }, { "locked", true } });
            if (!Ok(result)) return Reason(result);
            ActionLog.Write("event", code + " robot locked in: " + name);
            return null;
        }

        /// <summary>Opens the event's locked-in robot when another build is open (asks about unsaved changes as usual).
        /// False when there's no copy on this computer.</summary>
        public static bool OpenRobot(string code) {
            var file = RobotFile(code);
            if (file == null) return false;
            var manager = UnityEngine.Object.FindAnyObjectByType<BuildsManager>();
            if (manager == null) return false;
            if (string.Equals(Path.GetFullPath(manager.buildPath ?? "."), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)) return true;
            var data = BuildsManager.ParsePath(file);
            if (data == null) return false;
            manager.AttemptLoad(data, file);
            return true;
        }

        // ------------------------------------------------------------------ hooks from the online match

        /// <summary>An online match's plan arrived (every computer): an event match has begun.</summary>
        public static void MatchBegan() => EventsRunner.Instance?.Began();

        /// <summary>An online match finished on this computer with this score (mine, theirs).</summary>
        public static void MatchFinished(int mine, int theirs) => EventsRunner.Instance?.Finished(mine, theirs);
    }

    /// <summary>
    /// V2 (build 88): keeps the joined events up to date (every 15 s, every 3 s while a match is on or the event page is
    /// open), shows "your match is up" (a notice card), runs the event match (hosting or joining its session, the
    /// alliance, Ready, starting it, telling the server this player is there) and the build timer at the top.
    /// </summary>
    public class EventsRunner : MonoBehaviour {
        public static EventsRunner Instance { get; private set; }

        /// <summary>Set by the event page while it shows an event (polled faster).</summary>
        public static string Watching;

        private float nextPoll;
        private bool polling;
        private readonly HashSet<string> announced = new();

        // The match being played.
        private string code, matchId;
        private bool hosting, joining;
        private string sessionFor;      // the match a session was made or joined for
        private float nextHere, nextStart;
        private bool began;
        private string reported;
        private float replayExitAt;

        // The build timer.
        private RectTransform timerBar;
        private TextMeshProUGUI timerText;

        public static bool Playing => Instance != null && Instance.matchId != null;
        public static string PlayingCode => Instance?.code;
        public static string PlayingMatch => Instance?.matchId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AtStart() {
            if (!Application.isPlaying || Application.isBatchMode) return;
            if (Events.Joined().Count > 0) Ensure();
        }

        public static void Ensure() {
            if (Instance != null || !Application.isPlaying) return;
            var go = new GameObject("Events") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<EventsRunner>();
        }

        private void Update() {
            if (Time.unscaledTime >= nextPoll && !polling) _ = Poll();
            if (matchId != null) Drive();
            Timer();
        }

        private async Task Poll() {
            polling = true;
            var fast = matchId != null || Watching != null || Events.Known.Values.Any(e => Events.MyMatch(e) != null);
            nextPoll = Time.unscaledTime + (fast ? 3f : 15f);
            try {
                if (!AgeCheck.Ready || !OnlineServices.SignedIn) return;
                var codes = Events.Joined();
                if (Watching != null && !codes.Contains(Watching)) codes.Add(Watching);
                foreach (var c in codes) {
                    var result = await Events.Get(c);
                    if (!Events.Ok(result)) {
                        if (Events.Reason(result).StartsWith("there's no event")) Events.Forget(c);
                        continue;
                    }
                    Look((JObject)result["event"]);
                }
            }
            finally { polling = false; }
        }

        // What changed for this player: a match called, building started.
        private void Look(JObject ev) {
            if (ev == null || !Events.In(ev)) return;
            var c = (string)ev["code"];
            var state = (string)ev["state"];
            var name = NameFilter.Shown((string)ev["name"], "Event");
            var mine = Events.MyMatch(ev);
            if (mine != null && (string)mine["state"] == "called" && matchId == null && OnlineLaunch.EventAuto && !OnlineMatch.Active) {
                Debug.Log("[Event] -eventauto: joining " + c + " " + mine["id"]);
                Join(c, (string)mine["id"]);
            }
            // Cards for calls that are over (or being played now) go.
            var live = mine != null && (string)mine["state"] == "called" && matchId == null ? "event " + c + ":" + mine["id"] + ":" + mine["replays"] : null;
            foreach (var old in announced.Where(a => a.StartsWith(c + ":r")).ToList())
                if ("event " + old != live) Notices.Withdraw("event " + old);
            if (mine != null && (string)mine["state"] == "called" && matchId == null) {
                var key = c + ":" + mine["id"] + ":" + mine["replays"];
                if (announced.Add(key)) {
                    Audio.Sounds.Play(Audio.Sounds.Fanfare, 0.5f);
                    var deadline = (long)mine["deadline"];
                    Notices.Show("event " + key, name, "Your match vs " + Events.NameOf(ev, Events.Opponent(ev, mine)) + " is up (" + Events.MatchLabel(ev, (string)mine["id"])
                                                   + "). Join within " + Events.Span(deadline - Events.Now) + ", or it's a no-show.",
                        "Join now", () => Join(c, (string)mine["id"]), "Event page", () => OnlineWindow.OpenEvent(c));
                }
            }
            // The host's messages (each once; only ones from the last hour).
            foreach (var message in (ev["messages"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                if (Events.Now - ((long?)message["at"] ?? 0) < 3600 * 1000 && announced.Add(c + ":msg" + message["id"]) && !Events.IsHost(ev))
                    Notices.Show("event " + c + ":msg" + message["id"], name + ": from the host", (string)message["text"], "Event page", () => OnlineWindow.OpenEvent(c), null, null);
            if (state == "building" && announced.Add(c + ":building"))
                Notices.Show("event " + c + ":building", name, "Building has started: " + Events.Minutes((int?)ev["settings"]?["buildMinutes"] ?? 60)
                                                              + ". Start a new build now; when time's up the build that's open is locked in as your robot.",
                    "Start a new build", () => FindAnyObjectByType<BuildsManager>()?.CreateNewBuild(), "Event page", () => OnlineWindow.OpenEvent(c));
            // Time's up in a build event: the build that's open is the robot (once).
            if (state == "playing" && (string)ev["settings"]?["robots"] == "build" && Events.RobotFile(c) == null && announced.Add(c + ":autolock"))
                _ = AutoLock(c);
        }

        private async Task AutoLock(string c) {
            var problem = await Events.LockIn(c);
            if (problem != null) Interface.Interface.Say("Your robot couldn't be locked in: " + problem, false);
        }

        // ------------------------------------------------------------------ the match

        /// <summary>Starts playing this event match: the locked-in robot is opened, then the session is hosted or joined.</summary>
        public void Join(string eventCode, string match) {
            if (OnlineMatch.Active) {
                Interface.Interface.Say("Finish the match you're in first", false);
                return;
            }
            if (!Events.Known.TryGetValue(eventCode, out var ev)) return;
            var m = Events.Matches(ev).FirstOrDefault(x => (string)x["id"] == match);
            if (m == null || ((string)m["state"] != "called" && (string)m["state"] != "playing")) {
                Interface.Interface.Say("That match isn't on any more", false);
                return;
            }
            // Leave a different lobby first.
            if (MatchSessions.Current != null && sessionFor != match) _ = MatchSessions.Leave(null);
            if (Game.SkillsGame.Active) Game.SkillsGame.Current.Exit();
            if (!Events.OpenRobot(eventCode) && string.IsNullOrEmpty(MatchSessions.RobotName()))
                Interface.Interface.Say("Open your robot's build: the robot you bring is the build that's open", false);
            code = eventCode;
            matchId = match;
            began = false;
            reported = null;
            nextHere = 0f;
            nextStart = Time.unscaledTime + 4f;
            nextPoll = 0f;
            ActionLog.Write("event", "joining " + eventCode + " " + match);
            OnlineWindow.Open();
        }

        public void Stop() {
            matchId = null;
            code = null;
            hosting = joining = false;
        }

        private void Drive() {
            if (!Events.Known.TryGetValue(code, out var ev)) return;
            var m = Events.Matches(ev).FirstOrDefault(x => (string)x["id"] == matchId);
            var state = (string)m?["state"];
            if (m == null || state == "done" || state == "wait" || state == "ready") {
                // Over: back out of its lobby once the results are closed.
                if (OnlineMatch.Active) return;
                if (MatchSessions.Current != null && sessionFor == matchId) _ = MatchSessions.Leave(null);
                Stop();
                return;
            }
            var me = Events.Me;
            var host = (string)m["a"] == me;
            var session = MatchSessions.Current;
            var wanted = (string)m["session"] ?? "";
            if (OnlineMatch.Active) {
                if (Time.unscaledTime >= nextHere) Here(m, host);
                // A tie (or a replay the host asked for): called again after this computer sent its score. Back to the
                // lobby after a moment, where it starts again by itself.
                var game = Game.SkillsGame.Current;
                if (state == "called" && reported != null && game != null && game.state == Game.SkillsGame.State.Results) {
                    if (replayExitAt <= 0f) {
                        replayExitAt = Time.unscaledTime + 8f;
                        var note = (string)m["note"];
                        Interface.Interface.Announce(string.IsNullOrEmpty(note) ? "The match is played again" : "The match is played again: " + note);
                    }
                    else if (Time.unscaledTime > replayExitAt) {
                        replayExitAt = 0f;
                        reported = null;
                        nextStart = Time.unscaledTime + 6f;
                        game.Exit();
                    }
                }
                return;
            }
            // In a lobby that isn't this match's: leave it.
            if (session != null && sessionFor != matchId) {
                _ = MatchSessions.Leave(null);
                return;
            }
            // The other player's session was replaced (a fresh one after a long wait): follow.
            if (session != null && !host && wanted != "" && session.Code != wanted && !joining) {
                _ = MatchSessions.Leave(null);
                return;
            }
            if (session == null) {
                if (host && !hosting) _ = HostSession(ev, m);
                else if (!host && wanted != "" && !joining) _ = JoinSession(wanted);
                return;
            }
            // In the lobby: the alliance (the higher seed red), the robot (from the build that's open), Ready.
            var mine = session.CurrentPlayer;
            var team = host ? "red" : "blue";
            if (MatchSessions.Get(mine, MatchSessions.TeamKey) != team) MatchSessions.Set(MatchSessions.TeamKey, team);
            else if (!string.IsNullOrEmpty(MatchSessions.Get(mine, MatchSessions.RobotKey)) && MatchSessions.Get(mine, MatchSessions.ReadyKey) != "1")
                MatchSessions.Set(MatchSessions.ReadyKey, "1");
            if (Time.unscaledTime >= nextHere) Here(m, host);
            // The host starts it as soon as both are in and ready.
            if (host && session.Players.Count >= 2 && Time.unscaledTime >= nextStart) {
                nextStart = Time.unscaledTime + 3f;
                var ready = session.Players.All(p => MatchSessions.Get(p, MatchSessions.ReadyKey) == "1"
                                                     && !string.IsNullOrEmpty(MatchSessions.Get(p, MatchSessions.RobotKey)) && MatchSessions.Get(p, MatchSessions.TeamKey) != "");
                if (ready) {
                    var problem = OnlineMatch.HostStart();
                    Debug.Log("[Event] starting " + matchId + ": " + (problem ?? "started"));
                }
            }
        }

        private void Here(JObject m, bool host) {
            nextHere = Time.unscaledTime + 8f;
            var data = new Dictionary<string, object> { { "match", matchId } };
            if (host && MatchSessions.Current != null) data["session"] = MatchSessions.Current.Code;
            _ = Events.Call("here", code, data);
        }

        private async Task HostSession(JObject ev, JObject m) {
            hosting = true;
            try {
                var name = NameFilter.Shown((string)ev["name"], "Event") + " · " + Events.MatchLabel(ev, matchId);
                var options = new MatchSessions.HostOptions {
                    name = name.Length > 40 ? name.Substring(0, 40) : name, mode = 1, maxPlayers = 2, inviteOnly = true,
                    autonomous = (bool?)ev["settings"]?["autonomous"] != false,
                };
                var problem = await MatchSessions.Host(options);
                if (problem != null) {
                    Interface.Interface.Say("The match couldn't be hosted: " + problem, false);
                    await Task.Delay(4000);
                    return;
                }
                sessionFor = matchId;
                nextHere = 0f;
                Debug.Log("[Event] hosting " + matchId + " in session " + MatchSessions.Current?.Code);
                OnlineWindow.Open();
            }
            finally { hosting = false; }
        }

        private async Task JoinSession(string session) {
            joining = true;
            try {
                var problem = await MatchSessions.Join(session, false, null);
                if (problem != null) {
                    Debug.Log("[Event] joining " + session + ": " + problem);
                    await Task.Delay(3000);
                    return;
                }
                sessionFor = matchId;
                nextHere = 0f;
                Debug.Log("[Event] joined " + matchId + " in session " + session);
                OnlineWindow.Open();
            }
            finally { joining = false; }
        }

        public void Began() {
            if (matchId == null || began) return;
            began = true;
            reported = null;   // a new start: its score is sent again
            Debug.Log("[Event] " + matchId + " began");
            _ = Events.Call("started", code, new Dictionary<string, object> { { "match", matchId } });
        }

        public void Finished(int mine, int theirs) {
            if (matchId == null || reported == matchId + ":" + began) return;
            reported = matchId + ":" + began;
            ActionLog.Write("event", code + " " + matchId + " score " + mine + "-" + theirs);
            Debug.Log("[Event] " + matchId + " finished " + mine + "-" + theirs);
            _ = Events.Call("report", code, new Dictionary<string, object> { { "match", matchId }, { "mine", mine }, { "theirs", theirs } });
            began = false;
            nextPoll = Time.unscaledTime + 2f;
        }

        // ------------------------------------------------------------------ the build timer

        private float nextTimer;

        private void Timer() {
            if (Time.unscaledTime < nextTimer) return;
            nextTimer = Time.unscaledTime + 0.25f;
            var ev = Events.Known.Values.FirstOrDefault(e => (string)e["state"] == "building" && Events.In(e) && (bool?)Events.Mine(e)?["locked"] != true);
            var show = ev != null && Interface.Interface.Root != null && !OnlineWindow.IsOpen;
            if (!show) {
                if (timerBar != null) timerBar.gameObject.SetActive(false);
                return;
            }
            if (timerBar == null) MakeTimer();
            timerBar.gameObject.SetActive(true);
            var left = (long)ev["buildEndsAt"] - Events.Now;
            timerText.text = "<b>" + NameFilter.Shown((string)ev["name"], "Event") + "</b>   Building: " + Events.Span(left) + " left";
            timerText.color = left < 5 * 60 * 1000 ? new Color(1f, 0.75f, 0.35f) : Color.white;
            timerCode = (string)ev["code"];
        }

        private string timerCode;

        private void MakeTimer() {
            var root = Ui.Rect("Event Timer", Interface.Interface.Root);
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 30;
            root.gameObject.AddComponent<GraphicRaycaster>();
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.sizeDelta = new Vector2(520f, 40f);
            root.anchoredPosition = new Vector2(0f, -(Layout.TopBarBottom + 8f));
            var card = Ui.Image(root, "Card", new Color(0.1f, 0.105f, 0.13f, 0.95f), 10f);
            card.rectTransform.Fill();
            Ui.Row(card.rectTransform, 10f, new RectOffset(16, 6, 4, 4));
            timerText = Ui.Text(card.rectTransform, "Text", "", Theme.FontBody, Color.white);
            timerText.Size(-1, 32f, 1f);
            var done = Ui.Button(card.rectTransform, "Done", "I'm done", async () => {
                var problem = await Events.LockIn(timerCode);
                if (problem != null) Interface.Interface.Say(problem, false);
                else Audio.Sounds.Play(Audio.Sounds.Tick);
            }, true, 6f, Theme.FontSmall);
            done.Size(96f, 30f);
            Ui.Tip(done, "I'm done", null, "Locks in the build that's open as your robot now (it needs a V5 Brain). Otherwise it's locked in when time's up.");
            var page = Ui.Button(card.rectTransform, "Page", "Event", () => OnlineWindow.OpenEvent(timerCode), false, 6f, Theme.FontSmall);
            page.Size(70f, 30f);
            timerBar = root;
        }
    }
}
