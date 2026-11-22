using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.Leaderboards;
using UnityEngine;

namespace Protobot.Online {
    /// <summary>V2: accounts, stats and the skills leaderboard (Unity Gaming Services). Everyone starts as a guest (an
    /// anonymous player kept on this computer); creating an account adds a username and password to that same player
    /// (stats and scores stay), signing in on another computer brings them there. Stats live in Cloud Save's protected
    /// data and the skills leaderboard only takes scores through Cloud Code (Assets/Protobot/Cloud), which checks each
    /// run first, so neither can be edited by players.</summary>
    public static class Accounts {
        public const string SkillsBoard = "skills-override";
        /// <summary>V2 (Phase 9): Programming Skills (runs driven only by the robot's program) have their own board.</summary>
        public const string ProgrammingBoard = "programming-skills-override";
        public static string Board(bool programming) => programming ? ProgrammingBoard : SkillsBoard;

        public class Stats {
            public int matches, wins, losses, ties, points, skillsRuns, skillsBest, programmingRuns, programmingBest;
        }

        public class Entry {
            public int rank;
            public string name, playerId;
            public int score;
            public DateTime when;
            public bool mine;
        }

        public static Stats Mine { get; private set; }

        /// <summary>V2 (COPPA): what a player under 13 is told on the Account page.</summary>
        public const string UnderThirteen = "You're under 13, so you play without voice chat and under a made-up name until you turn 13. "
                                            + "Your username is only for signing in: nobody else sees it.";
        public static event Action Changed;

        /// <summary>The account's username, or null for a guest.</summary>
        public static string Username => OnlineServices.SignedIn ? AuthenticationService.Instance.PlayerInfo?.Username : null;
        public static bool IsGuest => string.IsNullOrEmpty(Username);

        // Usernames: 3-20 letters, digits and . - _ ; passwords: 8-30 with an upper and lower case letter, a digit and a
        // symbol (Unity's rules, checked here first so the message is plain). V2: no @ in new usernames, so an email address
        // (personal information) can't be one; older usernames with @ still sign in.
        public static string CheckUsername(string name) {
            if (string.IsNullOrEmpty(name) || name.Length < 3 || name.Length > 20) return "A username is 3 to 20 characters";
            if (name.Contains("@")) return "A username can't be an email address";
            if (!NameFilter.Allowed(name)) return "That username isn't allowed: choose another";
            if (name.Any(c => !(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_'))) return "A username uses letters, digits and . - _ only";
            return null;
        }

        /// <summary>V2: the account's birth month and year, "YYYY-MM" (null for a guest, "" when the account has none yet:
        /// accounts made before Build 70). Kept in the account's protected data by Cloud Code "Birth"; see AgeCheck.</summary>
        public static string Birth { get; private set; }

        /// <summary>V2: reads the account's birth month (signing in, starting with a saved sign-in).</summary>
        public static async Task LoadBirth() {
            if (IsGuest) {
                Birth = null;
                return;
            }
            try {
                var json = await CloudCodeService.Instance.CallEndpointAsync("Birth", new Dictionary<string, object>());
                var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                Birth = result != null && result.TryGetValue("birth", out var b) ? b?.ToString() ?? "" : "";
            }
            catch (Exception e) {
                Debug.LogWarning("[Online] birth: " + e.Message);
            }
            AgeCheck.AccountChanged();
            Changed?.Invoke();
        }

        /// <summary>V2: keeps the birth month (0-11) and year on the account (only once: it can't be changed after).
        /// Null when it's kept, else why not.</summary>
        public static async Task<string> SaveBirth(int month, string year) {
            var wrong = AgeCheck.Check(month, year);
            if (wrong != null) return wrong;
            try {
                var json = await CloudCodeService.Instance.CallEndpointAsync("Birth", new Dictionary<string, object> { { "month", month + 1 }, { "year", int.Parse(year) } });
                var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                Birth = result != null && result.TryGetValue("birth", out var b) ? b?.ToString() ?? "" : "";
                AgeCheck.AccountChanged();
                Changed?.Invoke();
                if (string.IsNullOrEmpty(Birth)) return result != null && result.TryGetValue("reason", out var why) ? "Couldn't keep your birthday: " + why : "Couldn't keep your birthday";
                return null;
            }
            catch (Exception e) { return "Couldn't keep your birthday: " + Explain(e); }
        }

        public static string CheckPassword(string password) {
            if (string.IsNullOrEmpty(password) || password.Length < 8 || password.Length > 30) return "A password is 8 to 30 characters";
            if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || password.All(char.IsLetterOrDigit))
                return "A password needs an upper case letter, a lower case letter, a digit and a symbol";
            return null;
        }

        /// <summary>Turns the guest into an account (same player: its stats and scores stay). V2: the birth month (0-11)
        /// and year are required (children's privacy law) and kept on the account. A player under 13 can have one: they
        /// still play without voice chat and under their made-up name (the username is only for signing in).</summary>
        public static async Task<string> CreateAccount(string username, string password, int birthMonth, string birthYear) {
            var problem = CheckUsername(username) ?? CheckPassword(password) ?? AgeCheck.Check(birthMonth, birthYear);
            if (problem != null) return problem == AgeCheck.Check(birthMonth, birthYear) ? "Your birthday: " + char.ToLower(problem[0]) + problem.Substring(1) : problem;
            if (!await OnlineServices.Start()) return OnlineServices.Problem;
            try {
                await AuthenticationService.Instance.AddUsernamePasswordAsync(username, password);
                OnlineServices.Name = username;   // V2: an account plays under its username (the name bottom left), from 13
                var birth = await SaveBirth(birthMonth, birthYear);
                await SetDisplayName(OnlineServices.Name);
                Builds.ActionLog.Write("online", "account created: " + username);
                _ = Badges.Claim();   // V2: the beta badge waits for an account
                Changed?.Invoke();
                return birth == null ? null : "Your account was made, but: " + birth + ". Add it on this page.";
            }
            catch (Exception e) { return Explain(e); }
        }

        /// <summary>Signs in to an account (the guest on this computer is left).</summary>
        public static async Task<string> SignIn(string username, string password) {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return "Type your username and password";
            if (!await OnlineServices.Start()) return OnlineServices.Problem;
            try {
                if (AuthenticationService.Instance.IsSignedIn) AuthenticationService.Instance.SignOut(true);
                await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username.Trim(), password);
                // V2: an account plays under its username (the name bottom left and "<name>'s match").
                OnlineServices.Name = AuthenticationService.Instance.PlayerInfo?.Username ?? username.Trim();
                await LoadBirth();
                await SetDisplayName(OnlineServices.Name);
                Mine = null;
                Builds.ActionLog.Write("online", "signed in: " + username);
                Badges.Forget();   // V2: the account's own badges (and the beta badge earned here)
                _ = Badges.Claim();
                Changed?.Invoke();
                return null;
            }
            catch (Exception e) {
                // Back to a guest so online play still works.
                try { if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync(); } catch { }
                return Explain(e);
            }
        }

        /// <summary>V2: deletes this player and everything kept online for it (Cloud Code "DeleteMyData": leaderboard
        /// scores, stats, shared replays, report files; then the player itself), and starts a new guest. Null when it
        /// worked, else what went wrong.</summary>
        public static async Task<string> DeleteEverything() {
            if (!await OnlineServices.Start()) return OnlineServices.Problem;
            if (MatchSessions.Current != null) return "Leave the match first.";
            try {
                var json = await CloudCodeService.Instance.CallEndpointAsync("DeleteMyData", new Dictionary<string, object>());
                var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (result == null || !(result.TryGetValue("deleted", out var d) && d is bool deleted && deleted)) {
                    Debug.LogWarning("[Online] delete: " + json);
                    return "Couldn't delete everything (try again in a minute; your player wasn't deleted, so nothing is lost).";
                }
                await AuthenticationService.Instance.DeleteAccountAsync();
                Builds.ActionLog.Write("online", "deleted my online data");
                Mine = null;
                Birth = null;
                PlayerPrefs.DeleteKey("V2.Online.Name");
                PlayerPrefs.DeleteKey("V2.Online.KidName");
                try {
                    if (AuthenticationService.Instance.IsSignedIn) AuthenticationService.Instance.SignOut(true);
                    else AuthenticationService.Instance.ClearSessionToken();
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }
                catch (Exception e) { Debug.LogWarning("[Online] new guest: " + e.Message); }
                Badges.Forget();
                _ = Badges.Claim();
                Changed?.Invoke();
                Interface.Interface.Announce("Your online data was deleted");
                return null;
            }
            catch (Exception e) {
                Debug.LogWarning("[Online] delete: " + e);
                return "Couldn't delete everything: " + Explain(e) + " Try again, or ask on the project's GitHub page.";
            }
        }

        /// <summary>Signs out and starts a new guest on this computer.</summary>
        public static async Task SignOut() {
            if (!OnlineServices.SignedIn) return;
            AuthenticationService.Instance.SignOut(true);
            Mine = null;
            Birth = null;
            AgeCheck.AccountChanged();
            try { await AuthenticationService.Instance.SignInAnonymouslyAsync(); } catch (Exception e) { Debug.LogWarning("[Online] " + e.Message); }
            Badges.Forget();
            _ = Badges.Claim();
            Changed?.Invoke();
        }

        /// <summary>The name on the leaderboard (Unity keeps a "#1234" after it).</summary>
        public static async Task SetDisplayName(string name) {
            if (!OnlineServices.SignedIn) return;
            var clean = new string(OnlineServices.Clean(name).Select(c => char.IsWhiteSpace(c) ? '_' : c).ToArray());
            if (clean.Length == 0) return;
            try { await AuthenticationService.Instance.UpdatePlayerNameAsync(clean.Length > 40 ? clean.Substring(0, 40) : clean); }
            catch (Exception e) { Debug.LogWarning("[Online] name: " + e.Message); }
        }

        public static async Task<Stats> LoadStats() {
            if (!await OnlineServices.Start()) return null;
            try {
                var json = await CloudCodeService.Instance.CallEndpointAsync("GetStats", new Dictionary<string, object>());
                Mine = JsonConvert.DeserializeObject<Stats>(json);
                Changed?.Invoke();
                return Mine;
            }
            catch (Exception e) {
                Debug.LogWarning("[Online] stats: " + e.Message);
                return null;
            }
        }

        /// <summary>An online match's result for this player (each player records their own).</summary>
        public static async void RecordMatch(int mine, int theirs) {
            if (!OnlineServices.SignedIn) return;
            try {
                await CloudCodeService.Instance.CallEndpointAsync("RecordMatch", new Dictionary<string, object> { { "mine", mine }, { "theirs", theirs } });
                Builds.ActionLog.Write("online", "match recorded " + mine + " to " + theirs);
            }
            catch (Exception e) { Debug.LogWarning("[Online] match result: " + e.Message); }
        }

        /// <summary>Sends a finished skills run; Cloud Code checks it and posts it to the leaderboard. Returns the
        /// message for the player (null: nothing to say).</summary>
        public static async Task<string> SubmitSkills(int score, IEnumerable<(string kind, int points)> items, Vector3 footprint, float seconds, string robot, bool programming = false) {
            if (!await OnlineServices.Start()) return null;
            var run = new Dictionary<string, object> {
                { "score", score },
                { "items", items.Select(i => new Dictionary<string, object> { { "kind", i.kind }, { "points", i.points } }).ToList() },
                { "footprint", new[] { footprint.x, footprint.y, footprint.z } },
                { "seconds", seconds },
                { "build", Builds.BuildInfo.Build },
                { "robot", OnlineServices.Shared(robot) },
                { "kind", programming ? "programming" : "driving" },
            };
            try {
                await SetDisplayName(OnlineServices.Name);
                var json = await CloudCodeService.Instance.CallEndpointAsync("SubmitSkillsRun", new Dictionary<string, object> { { "run", run } });
                var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                var accepted = result.TryGetValue("accepted", out var a) && a is bool ok && ok;
                Builds.ActionLog.Write("online", (programming ? "programming " : "") + "skills run " + score + (accepted ? " posted" : " refused: " + (result.TryGetValue("reason", out var r) ? r : "")));
                if (!accepted) return "The run wasn't posted to the leaderboard: " + (result.TryGetValue("reason", out var why) ? why : "it didn't pass the checks");
                return result.TryGetValue("rank", out var rank) && rank != null ? "Posted to the " + (programming ? "programming " : "") + "skills leaderboard: #" + (Convert.ToInt32(rank) + 1) : null;
            }
            catch (Exception e) {
                Debug.LogWarning("[Online] skills run: " + e.Message);
                return null;   // offline: quietly not posted
            }
        }

        // V2: the replay of a player's best skills run, in their public Cloud Save data (anyone signed in can read it).
        private const string ReplayKey = "skills-replay", ProgrammingReplayKey = "programming-replay";
        private const int ReplayLimit = 1_500_000;   // characters of base64

        /// <summary>Keeps this run's replay as the player's best (only when it's what the leaderboard has for them).</summary>
        public static async Task SaveReplay(Game.ReplayFile replay, bool programming = false) {
            if (!OnlineServices.SignedIn || replay == null) return;
            if (AgeCheck.Under13) return;   // V2 (COPPA): an under-13 player's runs aren't kept online (only the score)
            try {
                var mine = await LeaderboardsService.Instance.GetPlayerScoreAsync(Board(programming));
                if (mine == null || (int)Math.Round(mine.Score) != replay.score) return;   // not the best: the best's replay stays
                var text = Convert.ToBase64String(Game.Replays.ToBytes(replay));
                if (text.Length > ReplayLimit) {
                    Debug.LogWarning("[Online] replay too big to share: " + text.Length);
                    return;
                }
                await Unity.Services.CloudSave.CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { { programming ? ProgrammingReplayKey : ReplayKey, text } },
                    new Unity.Services.CloudSave.Models.Data.Player.SaveOptions(new Unity.Services.CloudSave.Models.Data.Player.PublicWriteAccessClassOptions()));
                Builds.ActionLog.Write("online", "replay shared (" + text.Length / 1024 + " KB)");
            }
            catch (Exception e) { Debug.LogWarning("[Online] replay: " + e.Message); }
        }

        /// <summary>A player's best run's replay, or null (none shared, or offline).</summary>
        public static async Task<Game.ReplayFile> LoadReplay(string playerId, bool programming = false) {
            if (!await OnlineServices.Start() || string.IsNullOrEmpty(playerId)) return null;
            try {
                var key = programming ? ProgrammingReplayKey : ReplayKey;
                var items = await Unity.Services.CloudSave.CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { key },
                    new Unity.Services.CloudSave.Models.Data.Player.LoadOptions(new Unity.Services.CloudSave.Models.Data.Player.PublicReadAccessClassOptions(playerId)));
                if (!items.TryGetValue(key, out var item)) return null;
                return Game.Replays.FromBytes(Convert.FromBase64String(item.Value.GetAs<string>()));
            }
            catch (Exception e) {
                Debug.LogWarning("[Online] replay: " + e.Message);
                return null;
            }
        }

        public static async Task<(List<Entry> top, Entry mine, string problem)> Leaderboard(int count = 50, bool programming = false) {
            if (!await OnlineServices.Start()) return (new List<Entry>(), null, OnlineServices.Problem);
            try {
                var page = await LeaderboardsService.Instance.GetScoresAsync(Board(programming), new GetScoresOptions { Limit = count });
                var me = OnlineServices.PlayerId;
                // V2: players who deleted their data are left out at once (the moderators' tool removes their scores).
                var hidden = await Hidden();
                var top = page.Results.Where(e => !hidden.Contains(e.PlayerId)).Select(e => ToEntry(e, me)).ToList();
                for (var i = 0; i < top.Count; i++) top[i].rank = i + 1;
                Entry mine = top.FirstOrDefault(e => e.mine);
                if (mine == null)
                    try { mine = ToEntry(await LeaderboardsService.Instance.GetPlayerScoreAsync(Board(programming)), me); }
                    catch { }   // no score yet
                return (top, mine, null);
            }
            catch (Exception e) {
                Debug.LogWarning("[Online] leaderboard: " + e.Message);
                return (new List<Entry>(), null, Explain(e));
            }
        }

        private static HashSet<string> hiddenPlayers;
        private static float hiddenAt = -999f;

        /// <summary>V2: the players who deleted their online data (Cloud Save custom data "leaderboard", key "hidden",
        /// written by Cloud Code "DeleteMyData"); read at most once a minute.</summary>
        private static async Task<HashSet<string>> Hidden() {
            if (hiddenPlayers != null && Time.realtimeSinceStartup - hiddenAt < 60f) return hiddenPlayers;
            var set = new HashSet<string>();
            try {
                var items = await Unity.Services.CloudSave.CloudSaveService.Instance.Data.Custom.LoadAsync("leaderboard", new HashSet<string> { "hidden" });
                if (items.TryGetValue("hidden", out var item)) foreach (var id in item.Value.GetAs<List<string>>() ?? new List<string>()) set.Add(id);
            }
            catch (Exception e) { Debug.Log("[Online] hidden players: " + e.Message); }
            hiddenPlayers = set;
            hiddenAt = Time.realtimeSinceStartup;
            return set;
        }

        private static Entry ToEntry(Unity.Services.Leaderboards.Models.LeaderboardEntry e, string me) => new() {
            rank = e.Rank + 1,
            name = NameFilter.Shown((e.PlayerName ?? "Player").Split('#')[0].Replace('_', ' ')),   // V2: through the name filter
            playerId = e.PlayerId,
            score = (int)Math.Round(e.Score),
            when = e.UpdatedTime,
            mine = e.PlayerId == me,
        };

        public static string Explain(Exception e) {
            var text = e.Message ?? "";
            if (text.Contains("WRONG_USERNAME_PASSWORD") || text.Contains("Invalid username or password")) return "Wrong username or password";
            if (text.Contains("ENTITY_EXISTS") || text.Contains("already exists") || text.Contains("username already")) return "That username is taken";
            if (text.Contains("USERNAME_PASSWORD") && text.Contains("not enabled")) return "Accounts aren't switched on for Protobot yet";
            if ((text.Contains("eaderboard") && (text.Contains("not found") || text.Contains("not be found")))) return "The skills leaderboard isn't set up yet";
            return OnlineServices.Explain(e);
        }
    }
}
