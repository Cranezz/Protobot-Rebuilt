using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Protobot.Builds;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Protobot.Online {
    /// <summary>A match in the server list (what the lobby service knows about it).</summary>
    public class MatchInfo {
        public string id, name, host, game, mode, hostBadges;
        public int players, max, build;
        public bool password;
        /// <summary>V2: a match is being played in it right now (the server list shows "In game").</summary>
        public bool playing;
        public bool SameVersion => build == BuildInfo.Build;
    }

    /// <summary>V2: online matches through Unity's Multiplayer Services: the lobby service keeps the list of matches
    /// (name, game, mode, players, password, version), and Relay connects the players to the host's computer, which runs
    /// the match (a listen server: no servers of our own). Each player's choices (name, alliance, ready, robot) are
    /// player properties of the session.</summary>
    public static class MatchSessions {
        public const string SessionType = "match";
        // Session property keys (indexed ones can be filtered and sorted in the server list).
        public const string AutonKey = "auton";   // V2 (Phase 9): "1" = matches have the 0:15 autonomous period
        public const string GameKey = "game", ModeKey = "mode", BuildKey = "build", VersionKey = "version", HostKey = "hostname";
        /// <summary>V2: "ingame" while the host's match is being played (the server list's In game badge).</summary>
        public const string StateKey = "state";
        // Player property keys.
        public const string NameKey = "name", TeamKey = "team", ReadyKey = "ready", RobotKey = "robot";
        /// <summary>V2: "0" when this player can't hear voice chat (under 13, or voice chat off): a crossed-out speaker
        /// shows by their name so teammates know to tell them things another way.</summary>
        public const string VoiceKey = "voice";
        /// <summary>V2: the player this player votes to kick ("" none).</summary>
        public const string KickKey = "kick";
        /// <summary>V2: a player's badges ("beta,..."), shown beside their name (Badges); the host's are on the match too.</summary>
        public const string BadgesKey = "badges", HostBadgesKey = "hostbadges";

        public static readonly string[] Modes = { "2v2 match", "1v1 match", "Skills race" };
        public static readonly int[] ModePlayers = { 4, 2, 8 };

        public static ISession Current { get; private set; }
        /// <summary>Something about the current match changed (players, their choices, the match itself).</summary>
        public static event Action Changed;
        /// <summary>The match ended for this player (left, closed by the host, removed); the reason.</summary>
        public static event Action<string> Ended;

        /// <summary>The download page for the latest version, when there is one (shown when versions differ).</summary>
        public const string DownloadUrl = "https://github.com/Cranezz/Protobot-Rebuilt/releases/latest";

        // ------------------------------------------------------------------ the network

        /// <summary>The NetworkManager the sessions start (as host or client).</summary>
        public static NetworkManager Network() {
            if (NetworkManager.Singleton != null) return NetworkManager.Singleton;
            var go = new GameObject("Network");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var transport = go.AddComponent<UnityTransport>();
            var manager = go.AddComponent<NetworkManager>();
            // Approval only to learn which session player each connection is (they send their player id).
            manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, ConnectionApproval = true, TickRate = 30 };
            manager.ConnectionApprovalCallback = (request, response) => {
                var id = request.Payload == null ? "" : System.Text.Encoding.UTF8.GetString(request.Payload);
                if (!string.IsNullOrEmpty(id)) players[request.ClientNetworkId] = id;
                response.Approved = true;
                response.CreatePlayerObject = false;
            };
            manager.OnClientDisconnectCallback += Disconnected;
            manager.RunInBackground = true;
            // V2: the whole app keeps running when its window isn't in front during online play (alt-tabbing froze a
            // player's robot and their connection for everyone).
            Application.runInBackground = true;
            return manager;
        }

        /// <summary>Which session player each network connection is (filled on the host).</summary>
        private static readonly Dictionary<ulong, string> players = new();

        /// <summary>The host: which session player a network connection is ("" when unknown).</summary>
        public static string PlayerOfClient(ulong clientId) => players.TryGetValue(clientId, out var id) ? id : "";

        /// <summary>Tells the host who this is when connecting (the services' player id).</summary>
        private static void SayWhoIAm() =>
            Network().NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(OnlineServices.PlayerId ?? "");

        // A player whose connection dropped without leaving (the app closed or crashed) would stay in the lobby's list
        // until the lobby service times them out: the host takes them off at once. (In a match the rule will be that
        // their robot stays on the field switched off and Rejoin gives it back: that comes with the match flow.)
        private static void Disconnected(ulong clientId) {
            var net = NetworkManager.Singleton;
            if (net == null || !net.IsServer || clientId == NetworkManager.ServerClientId) return;
            if (!players.TryGetValue(clientId, out var playerId)) return;
            players.Remove(clientId);
            var session = Current;
            if (session == null || !session.IsHost || session.Players.All(p => p.Id != playerId)) return;
            // V2: the same player already back on a new connection (their app restarted quickly): keep them.
            if (players.ContainsValue(playerId)) return;
            ActionLog.Write("online", "connection lost: " + playerId);
            _ = RemoveQuietly(session, playerId);
        }

        private static async Task RemoveQuietly(ISession session, string playerId) {
            // V2: a few seconds' grace first, and not if they've connected again meanwhile (a dropped connection is noticed
            // ~10 s late, after a quick rejoin: that player was removed from the lobby while they were back in it).
            await Task.Delay(4000);
            if (players.ContainsValue(playerId) || Current != session) return;
            try { await session.AsHost().RemovePlayerAsync(playerId); }
            catch (Exception e) { Debug.LogWarning("[Online] Removing a disconnected player: " + e.Message); }
        }

        // ------------------------------------------------------------------ hosting and joining

        public class HostOptions {
            public string name;
            public int mode;
            public int maxPlayers;
            public string password;
            public bool inviteOnly;
            public string game = "Override";
            public bool autonomous = PlayerPrefs.GetInt("V2.Online.Autonomous", 1) == 1;   // V2 (Phase 9): robot code runs it
        }

        public static async Task<string> Host(HostOptions o) {
            if (!AgeCheck.Ready) return AgeCheck.NotReady;
            if (!await OnlineServices.Start()) return OnlineServices.Problem;
            if (!string.IsNullOrEmpty(o.password) && (o.password.Length < 8 || o.password.Length > 64)) return "A password needs 8 to 64 characters (or leave it empty).";
            // V2: match names are seen by everyone: no offensive words, and a player under 13 can't type one.
            if (AgeCheck.Under13) o.name = "";
            if (!NameFilter.Allowed(o.name)) return "That match name isn't allowed: choose another.";
            await Leave(null);
            SayWhoIAm();
            try {
                var options = new SessionOptions {
                    Type = SessionType,
                    Name = string.IsNullOrWhiteSpace(o.name) ? OnlineServices.Name + "'s match" : OnlineServices.Clean(o.name),
                    MaxPlayers = Mathf.Clamp(o.maxPlayers, 2, 16),
                    IsPrivate = o.inviteOnly,
                    Password = string.IsNullOrEmpty(o.password) ? null : o.password,
                    SessionProperties = new Dictionary<string, SessionProperty> {
                        [GameKey] = new(o.game, VisibilityPropertyOptions.Public, PropertyIndex.String1),
                        [ModeKey] = new(Modes[Mathf.Clamp(o.mode, 0, Modes.Length - 1)], VisibilityPropertyOptions.Public, PropertyIndex.String2),
                        [HostKey] = new(OnlineServices.Name, VisibilityPropertyOptions.Public, PropertyIndex.String3),
                        [BuildKey] = new(BuildInfo.Build.ToString(), VisibilityPropertyOptions.Public, PropertyIndex.Number1),
                        [VersionKey] = new(BuildInfo.Display, VisibilityPropertyOptions.Public),
                        [AutonKey] = new(o.autonomous ? "1" : "0", VisibilityPropertyOptions.Public),
                        [HostBadgesKey] = new(Badges.Text, VisibilityPropertyOptions.Public),
                    },
                    PlayerProperties = MyProperties(0),
                }.WithRelayNetwork();
                var session = await MultiplayerService.Instance.CreateSessionAsync(options);
                Use(session);
                ActionLog.Write("online", "hosting \"" + session.Name + "\" code " + session.Code + " (" + session.Id + ")");
                return null;
            }
            catch (Exception e) {
                Debug.LogWarning("[Online] Couldn't host: " + e);
                return "Couldn't start the match: " + OnlineServices.Explain(e);
            }
        }

        /// <summary>Joins by invite code (or by the server list's id); null when it worked, else what went wrong.</summary>
        public static async Task<string> Join(string codeOrId, bool byId, string password, int attempt = 0) {
            if (!AgeCheck.Ready) return AgeCheck.NotReady;
            if (!await OnlineServices.Start()) return OnlineServices.Problem;
            await Leave(null);
            SayWhoIAm();
            try {
                var options = new JoinSessionOptions { Type = SessionType, Password = string.IsNullOrEmpty(password) ? null : password, PlayerProperties = MyProperties(-1) };
                var session = byId
                    ? await MultiplayerService.Instance.JoinSessionByIdAsync(codeOrId, options)
                    : await MultiplayerService.Instance.JoinSessionByCodeAsync(CleanCode(codeOrId), options);
                // Another version can't play together (the field, the parts and the physics must be the same).
                var build = Build(session.Properties);
                if (build != BuildInfo.Build) {
                    var theirs = session.Properties.TryGetValue(VersionKey, out var v) ? v.Value : "build " + build;
                    await session.LeaveAsync();
                    return VersionProblem(theirs, build);
                }
                Use(session);
                ActionLog.Write("online", "joined \"" + session.Name + "\" (" + session.Id + ")");
                return null;
            }
            catch (SessionException e) when (e.Error == SessionError.SessionConflict || (e.Message ?? "").Contains("already a member")) {
                // V2: still in it (their app closed before the lobby noticed they'd gone): reconnect, and the match
                // (if one is being played) sends them back in.
                try {
                    foreach (var id in await MultiplayerService.Instance.GetJoinedSessionIdsAsync()) {
                        var session = await MultiplayerService.Instance.ReconnectToSessionAsync(id, new ReconnectSessionOptions { Type = SessionType });
                        Use(session);
                        ActionLog.Write("online", "reconnected to \"" + session.Name + "\" (" + session.Id + ")");
                        return null;
                    }
                }
                catch (Exception again) {
                    Debug.LogWarning("[Online] Couldn't reconnect: " + again.Message);
                }
                // The lobby drops a player whose app closed a few seconds after the host notices: join again then.
                if (attempt < 5) {
                    await Task.Delay(3000);
                    return await Join(codeOrId, byId, password, attempt + 1);
                }
                return "Couldn't get back into that match: try again in a moment.";
            }
            catch (SessionException e) when (e.Error == SessionError.SessionNotFound) {
                return byId ? "That match has closed." : "No match has that code: check it, or ask for a new one.";
            }
            catch (Exception e) {
                Debug.LogWarning("[Online] Couldn't join: " + e);
                // V2: don't stay half in a match that couldn't connect ("not able to join allocation").
                if (Current != null) await Leave(null);
                var text = e.Message ?? "";
                if (text.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 || e is SessionException s && s.Error == SessionError.Forbidden)
                    return string.IsNullOrEmpty(password) ? "This match has a password." : "Wrong password.";
                if (text.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0) return "That match is full.";
                // V2: the host removes a player it kicked as soon as they're in, which breaks the join half way.
                if (text.Contains("23006")) return "That match turned you away (players removed from a match can't join it again).";
                return "Couldn't join: " + OnlineServices.Explain(e);
            }
        }

        public static string VersionProblem(string theirs, int build) =>
            build > BuildInfo.Build
                ? "That match is on a newer version (" + theirs + "). You have " + BuildInfo.Display + ": update Protobot to join it."
                : "That match is on an older version (" + theirs + "). You have " + BuildInfo.Display + ": the host needs to update.";

        /// <summary>Codes are shown and typed in capitals, with or without spaces or dashes.</summary>
        public static string CleanCode(string code) => new string((code ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        public static async Task Leave(string reason) {
            var session = Current;
            if (session == null) return;
            Drop(session);
            try { await session.LeaveAsync(); }
            catch (Exception e) { Debug.LogWarning("[Online] Leaving: " + e.Message); }
            ActionLog.Write("online", "left the match");
            Ended?.Invoke(reason);
        }

        private static void Use(ISession session) {
            Current = session;
            banned.Clear();
            removing.Clear();
            session.Changed += Notify;
            session.PlayerJoined += OnJoined;
            session.PlayerHasLeft += OnLeft;
            session.PlayerPropertiesChanged += Notify;
            session.SessionPropertiesChanged += Notify;
            session.RemovedFromSession += OnRemoved;
            session.Deleted += OnDeleted;
            Notify();
        }

        private static void Drop(ISession session) {
            session.Changed -= Notify;
            session.PlayerJoined -= OnJoined;
            session.PlayerHasLeft -= OnLeft;
            session.PlayerPropertiesChanged -= Notify;
            session.SessionPropertiesChanged -= Notify;
            session.RemovedFromSession -= OnRemoved;
            session.Deleted -= OnDeleted;
            if (Current == session) Current = null;
        }

        private static void Notify() {
            CountVotes();
            Changed?.Invoke();
        }

        private static void OnJoined(string id) {
            ActionLog.Write("online", "player joined " + id);
            if (IsHost && OnlineMatch.Active && !OnlineMatch.HasSeat(id) && !banned.Contains(id)) {   // V2: a match is being played: only its players (coming back)
                ActionLog.Write("online", "turned away " + id + " (a match is being played)");
                _ = RemoveQuietly(Current, id);
                return;
            }
            if (IsHost && banned.Contains(id)) {   // kicked from this match: not back in
                ActionLog.Write("online", "removed " + id + " again (kicked from this match)");
                Kick(id);
                return;
            }
            Audio.Sounds.Play(Audio.Sounds.Tick);
            Notify();
        }
        private static void OnLeft(string id) {
            ActionLog.Write("online", "player left " + id);
            Notify();
        }
        private static void OnRemoved() => End("You were removed from the match.");
        private static void OnDeleted() => End("The host closed the match.");

        private static void End(string reason) {
            var session = Current;
            if (session == null) return;
            Drop(session);
            ActionLog.Write("online", "match ended: " + reason);
            Ended?.Invoke(reason);
        }

        // ------------------------------------------------------------------ the server list

        public static async Task<(List<MatchInfo> list, string problem)> List() {
            if (!await OnlineServices.Start()) return (new List<MatchInfo>(), OnlineServices.Problem);
            try {
                var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions {
                    Count = 50,
                    FilterOptions = new List<FilterOption> { new(FilterField.AvailableSlots, "0", FilterOperation.Greater) },
                    SortOptions = new List<SortOption> { new(SortOrder.Descending, SortField.CreationTime) },
                });
                var list = results.Sessions.Select(s => new MatchInfo {
                    id = s.Id,
                    name = NameFilter.Shown(s.Name, "Match"),   // V2: names other players chose go through the filter
                    host = NameFilter.Shown(Prop(s.Properties, HostKey)),
                    hostBadges = Prop(s.Properties, HostBadgesKey),
                    game = Prop(s.Properties, GameKey),
                    mode = Prop(s.Properties, ModeKey),
                    players = s.MaxPlayers - s.AvailableSlots,
                    max = s.MaxPlayers,
                    password = s.HasPassword,
                    build = Build(s.Properties),
                    playing = Prop(s.Properties, StateKey) == "ingame",
                }).OrderByDescending(m => m.SameVersion).ToList();
                return (list, null);
            }
            catch (Exception e) {
                Debug.LogWarning("[Online] Server list: " + e);
                return (new List<MatchInfo>(), "Couldn't load the server list: " + OnlineServices.Explain(e));
            }
        }

        private static string Prop(IReadOnlyDictionary<string, SessionProperty> props, string key) =>
            props != null && props.TryGetValue(key, out var p) ? p.Value : "";

        private static int Build(IReadOnlyDictionary<string, SessionProperty> props) => int.TryParse(Prop(props, BuildKey), out var n) ? n : -1;

        // ------------------------------------------------------------------ the players

        private static Dictionary<string, PlayerProperty> MyProperties(int team) => new() {
            [NameKey] = new(OnlineServices.Name),
            [TeamKey] = new(team < 0 ? "" : team == 0 ? "red" : "blue"),
            [ReadyKey] = new("0"),
            [RobotKey] = new(RobotName()),
            [KickKey] = new(""),
            [BadgesKey] = new(Badges.Text),
            [VoiceKey] = new(Voice.CanHear ? "1" : "0"),
        };

        public static string RobotName() {
            var manager = UnityEngine.Object.FindAnyObjectByType<BuildsManager>();
            var name = manager != null && !string.IsNullOrEmpty(manager.buildPath) ? System.IO.Path.GetFileNameWithoutExtension(manager.buildPath) : "";
            // V2: only a build that can play counts (it has a Brain); an unsaved one is "Unsaved build".
            if (Game.SkillsGame.RobotProblem() != null) return "";
            return OnlineServices.Shared(string.IsNullOrEmpty(name) ? "Unsaved build" : name);
        }

        public static string Get(IReadOnlyPlayer player, string key) {
            var value = player?.Properties != null && player.Properties.TryGetValue(key, out var p) ? p.Value : "";
            // V2: names other players chose go through the filter (a name from an older or modified app).
            if (key == NameKey || key == RobotKey) value = NameFilter.Shown(value, key == NameKey ? "Player" : "Robot");
            return value;
        }

        /// <summary>V2: whether a player can hear voice chat (an older app that doesn't say counts as yes).</summary>
        public static bool CanHear(IReadOnlyPlayer player) => Get(player, VoiceKey) != "0";

        /// <summary>Changes one of this player's choices (alliance, ready, robot) for everyone in the match.</summary>
        public static async void Set(string key, string value) {
            var session = Current;
            if (session?.CurrentPlayer == null) return;
            session.CurrentPlayer.SetProperty(key, new PlayerProperty(value));
            Notify();
            // V2: one save at a time; changes made meanwhile go in the next one, put back first (a save's answer from the
            // server brings back the values it was sent with: the robot cleared but Ready stayed on).
            unsaved[key] = value;
            if (saving) {
                saveAgain = true;
                return;
            }
            saving = true;
            try {
                do {
                    saveAgain = false;
                    foreach (var change in unsaved) session.CurrentPlayer.SetProperty(change.Key, new PlayerProperty(change.Value));
                    unsaved.Clear();
                    try { await session.SaveCurrentPlayerDataAsync(); }
                    catch (Exception e) {
                        Debug.LogWarning("[Online] Saving player data: " + e.Message);
                        Interface.Interface.Say("Couldn't update the match: " + OnlineServices.Explain(e), false);
                    }
                } while (saveAgain && Current == session);
            }
            finally { saving = false; }
        }

        private static bool saving, saveAgain;
        private static readonly Dictionary<string, string> unsaved = new();

        public static bool IsHost => Current != null && Current.IsHost;

        // ------------------------------------------------------------------ vote kick

        // V2: any player can vote to kick another (not the host: the host can remove players, and leaving the host's
        // match is the way out of it). With 3 players or more, a player is removed when more than half of the others
        // (2 at least) vote for it, and can't come back to that match.
        private static readonly HashSet<string> banned = new(), removing = new();

        public static int KickVotes(string target) => Current == null ? 0 : Current.Players.Count(p => p.Id != target && Get(p, KickKey) == target);
        public static int KickVotesNeeded() => Current == null ? 2 : Math.Max(2, (Current.Players.Count - 1) / 2 + 1);
        public static bool CanVoteKick(string target) => Current != null && Current.Players.Count >= 3 && target != Current.Host && target != Current.CurrentPlayer?.Id;
        public static bool VotedFor(string target) => Current != null && Get(Current.CurrentPlayer, KickKey) == target;

        /// <summary>This player's vote to kick a player (again: takes it back).</summary>
        public static void VoteKick(string target) {
            if (!CanVoteKick(target)) return;
            Set(KickKey, VotedFor(target) ? "" : target);
            ActionLog.Write("online", (VotedFor(target) ? "voted to kick " : "took back the vote to kick ") + target);
        }

        private static void CountVotes() {
            var session = Current;
            if (session == null || !session.IsHost || session.Players.Count < 3) return;
            foreach (var p in session.Players.ToList()) {
                if (p.Id == session.Host || removing.Contains(p.Id) || KickVotes(p.Id) < KickVotesNeeded()) continue;
                removing.Add(p.Id);
                ActionLog.Write("online", "voted out " + Get(p, NameKey) + " (" + p.Id + ")");
                Kick(p.Id);
            }
        }

        /// <summary>The host removes a player (they can't come back to this match).</summary>
        public static async void Kick(string playerId) {
            if (!IsHost) return;
            banned.Add(playerId);
            try { await Current.AsHost().RemovePlayerAsync(playerId); }
            catch (Exception e) { Interface.Interface.Say("Couldn't remove them: " + OnlineServices.Explain(e), false); }
        }

        /// <summary>V2: tells the server list a match is being played (or not any more).</summary>
        public static async void SetPlaying(bool playing) {
            if (!IsHost) return;
            var host = Current.AsHost();
            host.SetProperty(StateKey, new SessionProperty(playing ? "ingame" : "", VisibilityPropertyOptions.Public));
            try { await host.SavePropertiesAsync(); }
            catch (Exception e) { Debug.LogWarning("[Online] Match state: " + e.Message); }
        }

        /// <summary>Closes the match to new players (when it starts) or opens it again.</summary>
        public static async void Lock(bool locked) {
            if (!IsHost) return;
            var host = Current.AsHost();
            host.IsLocked = locked;
            try { await host.SavePropertiesAsync(); }
            catch (Exception e) { Debug.LogWarning("[Online] Lock: " + e.Message); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LeaveOnQuit() {
            Application.quitting += () => {
                if (Current != null) {
                    try { Current.LeaveAsync(); }
                    catch (Exception) { }
                }
            };
        }
    }
}
