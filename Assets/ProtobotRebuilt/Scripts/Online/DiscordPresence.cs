using System;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;

namespace Protobot.Online {
    /// <summary>
    /// V2 (build 85): shows what you're doing in Protobot on your Discord profile ("Playing Protobot Rebuilt V2 ·
    /// Building a robot"), like games do. It talks to the Discord app running on this computer through its local
    /// Rich Presence connection (a named pipe "discord-ipc-N" on Windows, a socket in the temp folder on a Mac): nothing
    /// is sent over the internet by Protobot, and nothing happens when Discord isn't running. Settings > Preferences >
    /// Discord turns it off (<see cref="Enabled"/>) or adds the build's name (<see cref="ShowBuildName"/>, off by default;
    /// never for under 13s). A background thread does the connecting and writing so a missing or slow Discord never holds
    /// up a frame; the main thread only builds the activity every second and hands it over when it changed.
    /// </summary>
    public class DiscordPresence : MonoBehaviour {
        /// <summary>The Discord application whose name shows as "Playing ...". Made in the Discord Developer Portal
        /// (discord.com/developers/applications); its Application ID isn't a secret.</summary>
        public const string ApplicationId = "1575668358261506061";   // "Protobot Rebuilt V2"
        public const string ImageUrl = "https://cdn.discordapp.com/icons/1574091581386924132/23b988fc512faac188665f21148522e5.png?size=512";
        private const string EnabledKey = "V2.Discord.Show", BuildNameKey = "V2.Discord.BuildName";

        public static bool Enabled {
            get => PlayerPrefs.GetInt(EnabledKey, 1) == 1;
            set { PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }
        public static bool ShowBuildName {
            get => PlayerPrefs.GetInt(BuildNameKey, 0) == 1;
            set { PlayerPrefs.SetInt(BuildNameKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>For tests: connected to Discord, the last activity sent and Discord's last answer.</summary>
        public static bool Connected => instance != null && instance.connected;
        public static string LastSent => instance?.sentJson;
        public static string LastAnswer => instance?.answer;

        private static DiscordPresence instance;
        private static bool windows;   // read on the main thread: Unity's API isn't for the background one
        private static readonly long started = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private Thread thread;
        private volatile bool quitting, connected;
        private volatile string wantedJson;   // the activity to show ("" = clear it), set by the main thread
        private string sentJson, answer;
        private float nextBuild;
        private readonly AutoResetEvent wake = new(false);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            if (!Application.isPlaying || Application.isBatchMode) return;
            var go = new GameObject("Discord Presence") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            windows = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
            instance = go.AddComponent<DiscordPresence>();
        }

        private void Start() {
            thread = new Thread(Run) { IsBackground = true, Name = "Discord presence" };
            thread.Start();
        }

        private void Update() {
            if (Time.unscaledTime < nextBuild) return;
            nextBuild = Time.unscaledTime + 1f;
            string json;
            try { json = Enabled ? JsonConvert.SerializeObject(Activity(), new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }) : ""; }
            catch (Exception) { return; }   // a window half made: try again next second
            if (json != wantedJson) {
                wantedJson = json;
                wake.Set();
            }
        }

        private void OnApplicationQuit() {
            quitting = true;
            wake.Set();
        }

        // ------------------------------------------------------------------ what it says

        private object Activity() {
            var (details, state) = Describe();
            object[] buttons = {
                new { label = "Get Protobot", url = Builds.Updater.ReleasesUrl },
                new { label = "Join the Protobot Discord", url = Interface.LegalDocs.DiscordUrl },
            };
            return new {
                details, state,
                timestamps = new { start = started },
                assets = new { large_image = ImageUrl, large_text = "Protobot Rebuilt V2 · " + Builds.BuildInfo.Display },
                buttons,
            };
        }

        /// <summary>The two lines under the app's name: what you're doing, and on what.</summary>
        public static (string details, string state) Describe() {
            var area = Presence.Area;
            var robot = RobotLine();
            switch (area) {
                case "Home menu": return ("In the menu", null);
                case "Replay": return ("Watching a replay", null);
                case "Multiplayer":
                    return OnlineMatch.Active ? ("In an online match", GameLine()) : ("In the multiplayer lobby", null);
                case "Game": return (GameLine(), robot);
                case "Code": return ("Writing robot code", robot);
                case "Sketching": return ("Sketching a plastic part", robot);
                case "Simulating": return ("Testing a robot in the simulator", robot);
                default: return ("Building a robot", robot);
            }
        }

        private static string GameLine() {
            var game = Game.SkillsGame.Current;
            if (game == null) return "VEX V5RC Override";
            var title = game.definition != null ? game.definition.title : "Override";
            var what = game.ModeName + " · " + title;
            return game.state == Game.SkillsGame.State.Setup ? "Setting up: " + what : what;
        }

        private static string RobotLine() {
            if (!ShowBuildName || AgeCheck.Under13) return null;
            var name = Builds.ModelExport.BuildName();
            if (string.IsNullOrEmpty(name) || name == "Robot") return null;
            name = NameFilter.Shown(name, null);
            if (name == null) return null;
            return name.Length > 120 ? name.Substring(0, 120) : name;
        }

        // ------------------------------------------------------------------ talking to Discord (background thread)

        private void Run() {
            Stream pipe = null;
            var nextTry = 0L;
            while (!quitting) {
                try {
                    var want = wantedJson;
                    if (pipe == null) {
                        if (string.IsNullOrEmpty(want) || Clock.ElapsedMilliseconds < nextTry) { wake.WaitOne(2000); continue; }
                        pipe = Open();
                        if (pipe == null) { nextTry = Clock.ElapsedMilliseconds + 20000; continue; }   // Discord isn't running
                        Write(pipe, 0, "{\"v\":1,\"client_id\":\"" + ApplicationId + "\"}");
                        var ready = Read(pipe);
                        if (ready == null || !ready.Contains("READY")) throw new IOException("no READY: " + ready);
                        connected = true;
                        sentJson = null;
                    }
                    if (want != sentJson) {
                        var activity = string.IsNullOrEmpty(want) ? "null" : want;
                        Write(pipe, 1, "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + Pid + ",\"activity\":" + activity
                                       + "},\"nonce\":\"" + Guid.NewGuid().ToString("N") + "\"}");
                        answer = Read(pipe);
                        sentJson = want;
                        if (string.IsNullOrEmpty(want)) { Close(ref pipe); continue; }   // switched off: let go of Discord
                        Thread.Sleep(4000);   // Discord takes 5 updates in 20 s at most
                        continue;
                    }
                    wake.WaitOne(2000);
                }
                catch (Exception e) {
                    if (connected) Debug.Log("[Discord] connection closed: " + e.Message);
                    Close(ref pipe);
                    nextTry = Clock.ElapsedMilliseconds + 20000;
                }
            }
            Close(ref pipe);   // closing the connection takes the activity off the profile
        }

        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        private static int Pid => System.Diagnostics.Process.GetCurrentProcess().Id;

        private void Close(ref Stream pipe) {
            connected = false;
            try { pipe?.Dispose(); } catch (Exception) { /* gone already */ }
            pipe = null;
        }

        private static Stream Open() {
            for (var i = 0; i < 10; i++) {
                try {
                    if (windows) {
                        var p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.None);
                        p.Connect(200);
                        return p;
                    }
                    foreach (var folder in TempFolders()) {
                        var path = Path.Combine(folder, "discord-ipc-" + i);
                        if (!File.Exists(path)) continue;
                        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                        socket.Connect(new UnixEndPoint(path));
                        return new NetworkStream(socket, true);
                    }
                }
                catch (Exception) { /* not this one */ }
            }
            return null;
        }

        private static string[] TempFolders() {
            string Env(string n) => Environment.GetEnvironmentVariable(n);
            return new[] { Env("XDG_RUNTIME_DIR"), Env("TMPDIR"), Env("TMP"), Env("TEMP"), "/tmp" }
                .Where(f => !string.IsNullOrEmpty(f)).ToArray();
        }

        // A frame: opcode and length (little-endian 32-bit), then the JSON.
        private static void Write(Stream pipe, int op, string json) {
            var body = Encoding.UTF8.GetBytes(json);
            var frame = new byte[8 + body.Length];
            BitConverter.GetBytes(op).CopyTo(frame, 0);
            BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
            body.CopyTo(frame, 8);
            pipe.Write(frame, 0, frame.Length);
            pipe.Flush();
        }

        private static string Read(Stream pipe) {
            while (true) {
                var head = ReadExactly(pipe, 8);
                var op = BitConverter.ToInt32(head, 0);
                var length = BitConverter.ToInt32(head, 4);
                if (length < 0 || length > 1 << 20) throw new IOException("bad frame");
                var json = Encoding.UTF8.GetString(ReadExactly(pipe, length));
                switch (op) {
                    case 2: throw new IOException("closed by Discord: " + json);
                    case 3: Write(pipe, 4, json); continue;   // ping: pong
                    default: return json;
                }
            }
        }

        private static byte[] ReadExactly(Stream pipe, int count) {
            var buffer = new byte[count];
            var got = 0;
            while (got < count) {
                var n = pipe.Read(buffer, got, count - got);
                if (n <= 0) throw new IOException("closed");
                got += n;
            }
            return buffer;
        }

        /// <summary>A Unix domain socket address (Mono's own is in Mono.Posix, which the player doesn't carry).</summary>
        private class UnixEndPoint : EndPoint {
            private readonly string path;
            public UnixEndPoint(string path) { this.path = path; }
            public override AddressFamily AddressFamily => AddressFamily.Unix;
            public override EndPoint Create(SocketAddress address) => this;
            public override SocketAddress Serialize() {
                var bytes = Encoding.UTF8.GetBytes(path);
                var address = new SocketAddress(AddressFamily.Unix, 2 + bytes.Length + 1);
                for (var i = 0; i < bytes.Length; i++) address[2 + i] = bytes[i];
                address[2 + bytes.Length] = 0;
                return address;
            }
            public override string ToString() => path;
        }
    }
}
