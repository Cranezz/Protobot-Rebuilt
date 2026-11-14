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
        public string id, name, host, game, mode;
        public int players, max, build;
        public bool password;
        public bool SameVersion => build == BuildInfo.Build;
    }

    /// <summary>V2: online matches through Unity's Multiplayer Services: the lobby service keeps the list of matches
    /// (name, game, mode, players, password, version), and Relay connects the players to the host's computer, which runs
    /// the match (a listen server: no servers of our own). Each player's choices (name, alliance, ready, robot) are
    /// player properties of the session.</summary>
    public static class MatchSessions {
        public const string SessionType = "match";
        // Session property keys (indexed ones can be filtered and sorted in the server list).
        public const string GameKey = "game", ModeKey = "mode", BuildKey = "build", VersionKey = "version", HostKey = "hostname";
        // Player property keys.
        public const string NameKey = "name", TeamKey = "team", ReadyKey = "ready", RobotKey = "robot";

        public static readonly string[] Modes = { "2v2 match", "1v1 match", "Skills race" };
        public static readonly int[] ModePlayers = { 4, 2, 8 };

        public static ISession Current { get; private set; }
        /// <summary>Something about the current match changed (players, their choices, the match itself).</summary>
        public static event Action Changed;
        /// <summary>The match ended for this player (left, closed by the host, removed); the reason.</summary>
        public static event Action<string> Ended;

        /// <summary>The download page for the latest version, when there is one (shown when versions differ).</summary>
        public const string DownloadUrl = "";

        // ------------------------------------------------------------------ the network

        /// <summary>The NetworkManager the sessions start (as host or client).</summary>
        public static NetworkManager Network() {
            if (NetworkManager.Singleton != null) return NetworkManager.Singleton;
            var go = new GameObject("Network");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var transport = go.AddComponent<UnityTransport>();
            var manager = go.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, ConnectionApproval = false, TickRate = 30 };
            manager.RunInBackground = true;
            return manager;
        }

        // ------------------------------------------------------------------ hosting and joining

        public class HostOptions {
            public string name;
            public int mode;
            public int maxPlayers;
            public string password;
            public bool inviteOnly;
            public string game = "Override";
        }

        public static async Task<string> Host(HostOptions o) {
            if (!await OnlineServices.Start()) return OnlineServices.Problem;
            if (!string.IsNullOrEmpty(o.password) && (o.password.Length < 8 || o.password.Length > 64)) return "A password needs 8 to 64 characters (or leave it empty).";
            await Leave(null);
            Network();
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
        public static async Task<string> Join(string codeOrId, bool byId, string password) {
            if (!await OnlineServices.Start()) return OnlineServices.Problem;
            await Leave(null);
            Network();
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
            catch (SessionException e) when (e.Error == SessionError.SessionNotFound) {
                return byId ? "That match has closed." : "No match has that code: check it, or ask for a new one.";
            }
            catch (Exception e) {
                Debug.LogWarning("[Online] Couldn't join: " + e);
                var text = e.Message ?? "";
                if (text.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 || e is SessionException s && s.Error == SessionError.Forbidden)
                    return string.IsNullOrEmpty(password) ? "This match has a password." : "Wrong password.";
                if (text.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0) return "That match is full.";
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

        private static void Notify() => Changed?.Invoke();
        private static void OnJoined(string id) {
            ActionLog.Write("online", "player joined " + id);
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
                    name = s.Name,
                    host = Prop(s.Properties, HostKey),
                    game = Prop(s.Properties, GameKey),
                    mode = Prop(s.Properties, ModeKey),
                    players = s.MaxPlayers - s.AvailableSlots,
                    max = s.MaxPlayers,
                    password = s.HasPassword,
                    build = Build(s.Properties),
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
        };

        public static string RobotName() {
            var manager = UnityEngine.Object.FindAnyObjectByType<BuildsManager>();
            return manager != null && !string.IsNullOrEmpty(manager.buildPath) ? System.IO.Path.GetFileNameWithoutExtension(manager.buildPath) : "";
        }

        public static string Get(IReadOnlyPlayer player, string key) =>
            player?.Properties != null && player.Properties.TryGetValue(key, out var p) ? p.Value : "";

        /// <summary>Changes one of this player's choices (alliance, ready, robot) for everyone in the match.</summary>
        public static async void Set(string key, string value) {
            var session = Current;
            if (session?.CurrentPlayer == null) return;
            session.CurrentPlayer.SetProperty(key, new PlayerProperty(value));
            Notify();
            try { await session.SaveCurrentPlayerDataAsync(); }
            catch (Exception e) {
                Debug.LogWarning("[Online] Saving player data: " + e.Message);
                Interface.Interface.Say("Couldn't update the match: " + OnlineServices.Explain(e), false);
            }
        }

        public static bool IsHost => Current != null && Current.IsHost;

        /// <summary>The host removes a player.</summary>
        public static async void Kick(string playerId) {
            if (!IsHost) return;
            try { await Current.AsHost().RemovePlayerAsync(playerId); }
            catch (Exception e) { Interface.Interface.Say("Couldn't remove them: " + OnlineServices.Explain(e), false); }
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
