using System;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace Protobot.Online {
    /// <summary>V2: "-join CODE" on the command line joins that match when the app starts (invite links can launch the
    /// app this way; it's also how a second copy is tested). What happened goes to the log ("[Online] -join ...").</summary>
    public static class OnlineLaunch {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin() {
            var code = Argument("-join");
            if (string.IsNullOrEmpty(code)) return;
            _ = JoinSoon(code);
        }

        private static async Task JoinSoon(string code) {
            await Task.Delay(3000); // let the app finish starting (menus, part types)
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

        private static string Argument(string name) {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
