using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Services.CloudCode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Online {
    /// <summary>
    /// V2 (build 75): the app's heartbeat. While Protobot is in front and being used (input in the last 5 minutes, or a
    /// simulation, game or replay running), it adds up the seconds spent in each part of the app ("Building", "Game",
    /// "Code"...). Once a minute, for a player who agreed to the terms (AgeCheck.Ready), it sends them to Cloud Code
    /// "Presence", which keeps them for the developer (Tools > Protobot > Players) and answers with how many players are
    /// online now: the home menu's profile card shows it (<see cref="Online"/>). Nothing is sent before the terms.
    /// </summary>
    public class Presence : MonoBehaviour {
        public const float Every = 60f;
        private const float Idle = 300f;

        /// <summary>Players online now (the last heartbeat's answer), or -1 before the first.</summary>
        public static int Online { get; private set; } = -1;
        public static event Action Changed;

        private readonly Dictionary<string, float> seconds = new();
        private float lastInput, next = 8f;
        private bool sending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            if (!Application.isPlaying || Application.isBatchMode) return;
            var go = new GameObject("Presence") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            go.AddComponent<Presence>();
        }

        /// <summary>What part of the app is in use now.</summary>
        public static string Area {
            get {
                if (ProfileCard.HomeShown) return "Home menu";
                if (Game.ReplayViewer.Active) return "Replay";
                if (OnlineWindow.IsOpen || OnlineMatch.Active) return "Multiplayer";
                if (Game.SkillsGame.Active) return "Game";
                if (RobotCode.CodePanel.IsOpen) return "Code";
                if (Polycarb.SheetEditor.IsOpen) return "Sketching";
                if (Motion.Sandbox.Running) return "Simulating";
                return "Building";
            }
        }

        private void Update() {
            var now = Time.unscaledTime;
            if (Input()) lastInput = now;
            var running = Motion.Sandbox.Running || Game.ReplayViewer.Active;   // watching a run counts as using it
            if (Application.isFocused && (now - lastInput < Idle || running)) {
                var area = Area;
                seconds[area] = (seconds.TryGetValue(area, out var s) ? s : 0f) + Mathf.Min(Time.unscaledDeltaTime, 1f);
            }
            if (now >= next && !sending) {
                next = now + Every;
                _ = Beat();
            }
        }

        private static bool Input() {
            var mouse = Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 0.01f || mouse.leftButton.isPressed || mouse.rightButton.isPressed
                                  || mouse.scroll.ReadValue().sqrMagnitude > 0f)) return true;
            if (Keyboard.current != null && Keyboard.current.anyKey.isPressed) return true;
            var pad = Gamepad.current;
            return pad != null && (pad.leftStick.ReadValue().sqrMagnitude > 0.04f || pad.rightStick.ReadValue().sqrMagnitude > 0.04f);
        }

        // V2 (build 79): closing the app says so, so the count (and the Discord server's "Players online" channel) drops at
        // once instead of 2.5 minutes later. Best effort: the app may be gone before it's sent.
        private void OnApplicationQuit() {
            if (!AgeCheck.Ready || Online < 0) return;   // never got through: nothing to take back
            try {
                _ = CloudCodeService.Instance.CallEndpointAsync("Presence", new Dictionary<string, object> {
                    { "leaving", "1" }, { "name", OnlineServices.Name }, { "account", Accounts.IsGuest ? "0" : "1" },
                });
            }
            catch (Exception) { /* closing anyway */ }
        }

        private async System.Threading.Tasks.Task Beat() {
            if (!AgeCheck.Ready) return;   // nothing is sent before the age question and the terms
            sending = true;
            var sent = new Dictionary<string, object>();
            foreach (var kv in seconds) sent[kv.Key] = Mathf.Round(kv.Value);
            try {
                if (!await OnlineServices.Start()) return;
                var json = await CloudCodeService.Instance.CallEndpointAsync("Presence", new Dictionary<string, object> {
                    { "areas", sent }, { "name", OnlineServices.Name }, { "account", Accounts.IsGuest ? "0" : "1" },
                });
                foreach (var kv in sent) seconds[kv.Key] = Mathf.Max(0f, (seconds.TryGetValue(kv.Key, out var s) ? s : 0f) - Convert.ToSingle(kv.Value));
                var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (result != null && result.TryGetValue("online", out var o)) {
                    var count = Convert.ToInt32(o);
                    if (count != Online) {
                        Online = count;
                        Changed?.Invoke();
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Online] presence: " + e.Message); }   // offline: kept for the next one
            finally { sending = false; }
        }
    }
}
