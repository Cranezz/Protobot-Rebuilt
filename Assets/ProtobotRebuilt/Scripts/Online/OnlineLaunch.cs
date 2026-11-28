using System;
using System.Linq;
using System.Threading.Tasks;
using Protobot.Builds;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Online {
    /// <summary>V2: command-line options for online play (invite links can launch the app this way; it's also how a
    /// second copy is tested): "-open BUILD.pbb" opens that build, "-join CODE" joins that match, "-team red|blue" picks
    /// the alliance and says ready. What happened goes to the log ("[Online] -join ...").</summary>
    public static class OnlineLaunch {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin() {
            // V2: copies started from the command line (testing, several on one computer) run even when not in front.
            Application.runInBackground = true;
            var open = Argument("-open");
            var code = Argument("-join");
            var host = Argument("-host");
            var eventCode = Argument("-event");   // V2 (build 88, testing): join this event and lock in the opened build
            if (string.IsNullOrEmpty(open) && string.IsNullOrEmpty(code) && string.IsNullOrEmpty(host) && string.IsNullOrEmpty(eventCode)) return;
            _ = Run(open, code, Argument("-team"), host, HasFlag("-autostart"), eventCode);
        }

        /// <summary>"-eventauto" (testing): an event match called for this player is joined at once.</summary>
        public static readonly bool EventAuto = Environment.GetCommandLineArgs().Contains("-eventauto");

        private static async Task Run(string open, string code, string team, string host, bool autostart, string eventCode = null) {
            await Task.Delay(3000); // let the app finish starting (menus, part types)
            if (!string.IsNullOrEmpty(open)) {
                CloseLaunchWindow();
                var manager = UnityEngine.Object.FindAnyObjectByType<BuildsManager>();
                var data = BuildsManager.ParsePath(open);
                if (manager != null && data != null) {
                    manager.AttemptLoad(data, open);
                    Debug.Log("[Online] -open " + open + ": " + (data.parts?.Length ?? 0) + " parts");
                }
                else Debug.Log("[Online] -open " + open + ": couldn't read it");
                await Task.Delay(1500);   // screws find their holes
            }
            if (!string.IsNullOrEmpty(eventCode)) {
                for (var i = 0; i < 30 && !(OnlineServices.SignedIn && AgeCheck.Ready); i++) {
                    if (i == 0) _ = OnlineServices.Start();
                    await Task.Delay(1000);
                }
                var joined = await Events.Call("join", eventCode, new System.Collections.Generic.Dictionary<string, object> { { "name", OnlineServices.Name } });
                Debug.Log("[Event] -event " + eventCode + ": join " + (Events.Ok(joined) ? "ok" : Events.Reason(joined)));
                if (Events.Ok(joined)) {
                    Events.Remember(eventCode);
                    var lockProblem = await Events.LockIn(eventCode);
                    Debug.Log("[Event] -event " + eventCode + ": lock in " + (lockProblem ?? "ok"));
                }
                return;
            }
            if (!string.IsNullOrEmpty(host)) {
                await HostAndWait(host, team, autostart);
                return;
            }
            if (string.IsNullOrEmpty(code)) return;
            Debug.Log("[Online] -join " + code + ": joining");
            var problem = await MatchSessions.Join(code, false, null);
            if (problem != null) {
                Debug.Log("[Online] -join " + code + ": failed: " + problem);
                Interface.Interface.Say(problem, false);
                return;
            }
            OnlineWindow.Open();
            var session = MatchSessions.Current;
            Debug.Log("[Online] -join " + code + ": in the lobby \"" + session?.Name + "\" with " + session?.PlayerCount + " players");
            if (team == "red" || team == "blue") {
                // One save for all three (the lobby service limits how often a player's data changes).
                session?.CurrentPlayer?.SetProperty(MatchSessions.TeamKey, new Unity.Services.Multiplayer.PlayerProperty(team));
                session?.CurrentPlayer?.SetProperty(MatchSessions.RobotKey, new Unity.Services.Multiplayer.PlayerProperty(MatchSessions.RobotName()));
                MatchSessions.Set(MatchSessions.ReadyKey, "1");
            }
            // "-vote NAME" (testing): votes to kick that player once they're in the match.
            var vote = Argument("-vote");
            if (!string.IsNullOrEmpty(vote))
                for (var i = 0; i < 60 && MatchSessions.Current == session; i++) {
                    await Task.Delay(1000);
                    var target = session.Players.FirstOrDefault(p => vote == "any" ? MatchSessions.CanVoteKick(p.Id) : MatchSessions.Get(p, MatchSessions.NameKey) == vote);
                    if (target == null || !MatchSessions.CanVoteKick(target.Id)) continue;
                    MatchSessions.VoteKick(target.Id);
                    Debug.Log("[Online] -vote: voted to kick " + vote);
                    break;
                }
            // The Relay connection to the host's computer.
            for (var i = 0; i < 60; i++) {
                var net = NetworkManager.Singleton;
                if (net != null && net.IsConnectedClient) {
                    Debug.Log("[Online] -join " + code + ": connected to the host (client " + net.LocalClientId + ")");
                    return;
                }
                await Task.Delay(250);
            }
            Debug.Log("[Online] -join " + code + ": in the lobby but not connected to the host after 15 s");
        }

        // "-host NAME": hosts a match (its code goes to the log); "-autostart": starts it once 2+ players are ready.
        private static async Task HostAndWait(string name, string team, bool autostart) {
            var problem = await MatchSessions.Host(new MatchSessions.HostOptions { name = name, mode = 0, maxPlayers = 4 });
            if (problem != null) {
                Debug.Log("[Online] -host: failed: " + problem);
                return;
            }
            var session = MatchSessions.Current;
            Debug.Log("[Online] -host: code " + session.Code);
            OnlineWindow.Open();
            if (team == "red" || team == "blue") {
                session.CurrentPlayer?.SetProperty(MatchSessions.TeamKey, new Unity.Services.Multiplayer.PlayerProperty(team));
                session.CurrentPlayer?.SetProperty(MatchSessions.RobotKey, new Unity.Services.Multiplayer.PlayerProperty(MatchSessions.RobotName()));
                MatchSessions.Set(MatchSessions.ReadyKey, "1");
            }
            while (autostart && MatchSessions.Current == session) {
                await Task.Delay(1000);
                var ready = session.Players.Count >= 2;
                foreach (var player in session.Players)
                    if (MatchSessions.Get(player, MatchSessions.ReadyKey) != "1" || MatchSessions.Get(player, MatchSessions.TeamKey) == "") ready = false;
                if (!ready || OnlineMatch.Active) continue;
                var result = OnlineMatch.HostStart();
                Debug.Log("[Online] -autostart: " + (result ?? "started"));
                return;
            }
        }

        private static bool HasFlag(string name) {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Closes the home menu (the launch window) if it's open.</summary>
        // (-autoready: OnlineMatch presses Ready by itself, at the default start.)
        public static void CloseLaunchWindow() {
            var launch = GameObject.Find("Launch Canvas");
            if (launch == null || !launch.activeInHierarchy) return;
            foreach (var b in launch.GetComponentsInChildren<Button>(true))
                if (b.name == "Close Button" && b.transform.parent != null && b.transform.parent.name == "Splash Screen" && b.gameObject.activeInHierarchy) {
                    b.onClick.Invoke();
                    return;
                }
        }

        private static string Argument(string name) {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
