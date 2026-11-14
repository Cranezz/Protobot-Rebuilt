using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Protobot.Builds;
using Protobot.Custom;
using Protobot.Game;
using Protobot.Interface;
using Protobot.Motion;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Online {
    /// <summary>V2: which robot (an online match's seat) a part belongs to, and its place in that robot's file.</summary>
    public class NetPart : MonoBehaviour {
        public int robot, index;
    }

    /// <summary>Who plays where: sent by the host when the match starts.</summary>
    [Serializable]
    public class MatchPlan {
        public List<Seat> seats = new();
        public bool skipAutonomous = true;
        public int hostRobot;   // the host's seat (it owns the game pieces nobody touches)
    }

    [Serializable]
    public class Seat {
        public string playerId, name;
        public int robot;
        public bool red;
        public int quadrant;
    }

    /// <summary>A player's robot as everyone else gets it: its build, its custom parts, and where it starts.</summary>
    [Serializable]
    public class RobotPacket {
        public int robot;
        public string name, problem;
        public BuildData build;
        public CustomPartData[] customParts;
        public string front;
        public float cx, cz, floor, depth, width, height, x, y, heading;
        public bool preload;
        public int preloadSpot;
    }

    /// <summary>
    /// V2: an online match (Docs/Multiplayer concept.md, phase 3). Every computer opens the game (Match practice) round
    /// its own robot and loads the other players' robots from the files they send, placed at their starts. The host's
    /// computer simulates everything (one Sandbox with several robots, each driven by its player's controls, which the
    /// others send 30 times a second); the others don't simulate (Sandbox.Puppet) and show where the host has every body
    /// and game piece (sent in the field's frame, 20 times a second: each computer places the field round its own robot).
    /// Match Loads and Preloads are made by the host and announced. The host takes the score when everything is still
    /// and sends the final positions, so every computer shows the same score.
    /// A player who leaves mid-match leaves their robot on the field, switched off (no controls arrive: it gets none).
    /// </summary>
    public class OnlineMatch : MonoBehaviour {
        public static OnlineMatch Current { get; private set; }
        public static bool Active => Current != null;

        private enum Msg : byte {
            Plan = 1, Robot = 2, Loaded = 3, Go = 4, Spawn = 7, Drop = 8, Finish = 9, Abort = 10, Chunk = 11, Final = 12,
            State = 13, Claim = 14, Owner = 15, Release = 16, Collect = 17, Everything = 18, Hold = 19, Disable = 20,
            Rejoin = 21, RejoinReady = 22, Sync = 23,
        }
        private const string Channel = "protobot.match";
        private const int ChunkSize = 1000;

        private MatchPlan plan;
        private int me = -1;
        private SkillsGame game;
        private bool running, leaving, loadStarted;
        private readonly Dictionary<int, RobotPacket> packets = new();
        private readonly HashSet<int> loaded = new();
        private readonly List<GameObject> remoteParts = new();
        private readonly List<NetPart> myTags = new();

        private static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        // ------------------------------------------------------------------ the network

        private static CustomMessagingManager listeningTo;   // (a new one each time the network starts again)

        /// <summary>Registers the match's messages on the running NetworkManager (once per start).</summary>
        private static void Listen() {
            var net = NetworkManager.Singleton;
            if (net == null || !net.IsListening || net.CustomMessagingManager == null || listeningTo == net.CustomMessagingManager) return;
            listeningTo = net.CustomMessagingManager;
            net.CustomMessagingManager.RegisterNamedMessageHandler(Channel, Receive);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() {
            var go = new GameObject("Online Link");
            DontDestroyOnLoad(go);
            go.AddComponent<Link>();
            MatchSessions.Ended += reason => {
                if (Current != null) Current.Abort(string.IsNullOrEmpty(reason) ? "The match ended." : reason, false);
            };
        }

        private class Link : MonoBehaviour {
            private void Update() => Listen();
        }

        private static readonly Dictionary<(ulong, int), byte[][]> partial = new();
        private static int nextChunkId;

        private static void Receive(ulong sender, FastBufferReader reader) {
            reader.ReadByteSafe(out var type);
            reader.ReadValueSafe(out byte[] data);
            if ((Msg)type == Msg.Chunk) {
                // id, index, count, the message's own type, then its piece of the data.
                using var stream = new MemoryStream(data);
                using var r = new BinaryReader(stream);
                var id = r.ReadInt32();
                var index = r.ReadInt32();
                var count = r.ReadInt32();
                var inner = r.ReadByte();
                var piece = r.ReadBytes(data.Length - 13);
                if (!partial.TryGetValue((sender, id), out var pieces)) partial[(sender, id)] = pieces = new byte[count][];
                pieces[index] = piece;
                if (pieces.Any(p => p == null)) return;
                partial.Remove((sender, id));
                Dispatch(sender, (Msg)inner, pieces.SelectMany(p => p).ToArray());
                return;
            }
            Dispatch(sender, (Msg)type, data);
        }

        private static void Dispatch(ulong sender, Msg type, byte[] data) {
            try {
                if (type == Msg.Rejoin) {
                    if (Current != null) Current.Abort(null, false);
                    var go2 = new GameObject("Online Match");
                    DontDestroyOnLoad(go2);
                    Current = go2.AddComponent<OnlineMatch>();
                    Current.BeginRejoin(JsonUtility.FromJson<RejoinInfo>(Unpack(data)));
                    return;
                }
                if (type == Msg.Plan) {
                    if (Current != null) Current.Abort(null, false);
                    var go = new GameObject("Online Match");
                    DontDestroyOnLoad(go);
                    Current = go.AddComponent<OnlineMatch>();
                    Current.Begin(JsonUtility.FromJson<MatchPlan>(Text(data)));
                    return;
                }
                if (Current != null) Current.Handle(sender, type, data);
            }
            catch (Exception e) {
                Debug.LogException(e);
            }
        }

        private static void Send(ulong to, Msg type, byte[] data, bool reliable) {
            var net = NetworkManager.Singleton;
            if (net == null || !net.IsListening) return;
            if (reliable && data.Length > ChunkSize) {
                var id = ++nextChunkId;
                var count = (data.Length + ChunkSize - 1) / ChunkSize;
                for (var i = 0; i < count; i++) {
                    using var stream = new MemoryStream();
                    using (var w = new BinaryWriter(stream)) {
                        w.Write(id);
                        w.Write(i);
                        w.Write(count);
                        w.Write((byte)type);
                        w.Write(data, i * ChunkSize, Math.Min(ChunkSize, data.Length - i * ChunkSize));
                    }
                    SendRaw(net, to, Msg.Chunk, stream.ToArray(), true);
                }
                return;
            }
            SendRaw(net, to, type, data, reliable);
        }

        private static void SendRaw(NetworkManager net, ulong to, Msg type, byte[] data, bool reliable) {
            using var writer = new FastBufferWriter(data.Length + 16, Allocator.Temp);
            writer.WriteByteSafe((byte)type);
            writer.WriteValueSafe(data);
            net.CustomMessagingManager.SendNamedMessage(Channel, to, writer, reliable ? NetworkDelivery.ReliableSequenced : NetworkDelivery.Unreliable);
        }

        /// <summary>To the host (the host handles its own at once).</summary>
        private void ToHost(Msg type, byte[] data, bool reliable) {
            if (IsHost) Handle(NetworkManager.ServerClientId, type, data);
            else Send(NetworkManager.ServerClientId, type, data, reliable);
        }

        /// <summary>From the host to every other computer.</summary>
        private static void ToClients(Msg type, byte[] data, bool reliable) {
            var net = NetworkManager.Singleton;
            if (net == null || !net.IsServer) return;
            foreach (var id in net.ConnectedClientsIds)
                if (id != NetworkManager.ServerClientId) Send(id, type, data, reliable);
        }

        private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text ?? "");
        private static string Text(byte[] data) => Encoding.UTF8.GetString(data);

        private static byte[] Pack(string json) {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal)) {
                var raw = Bytes(json);
                gzip.Write(raw, 0, raw.Length);
            }
            return output.ToArray();
        }

        private static string Unpack(byte[] data) {
            using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            using var output = new MemoryStream();
            input.CopyTo(output);
            return Text(output.ToArray());
        }

        // ------------------------------------------------------------------ starting (the host)

        /// <summary>The host starts the match: seats from the lobby (Alliance, join order), then everyone sets up.</summary>
        public static string HostStart() {
            var session = MatchSessions.Current;
            if (session == null || !session.IsHost) return "Only the host can start the match.";
            if (!IsHost) return "The match isn't connected yet: wait a moment.";
            var newPlan = new MatchPlan();
            int reds = 0, blues = 0;
            foreach (var player in session.Players) {
                var team = MatchSessions.Get(player, MatchSessions.TeamKey);
                var red = team == "red";
                if (team != "red" && team != "blue") return "Every player needs an alliance.";
                var n = red ? reds++ : blues++;
                if (n >= 2) return "An alliance has at most two robots.";
                // SG1: red starts in the Left or Bottom Quadrant, blue in the Right or Top.
                var quadrant = red ? (n == 0 ? Quadrant.Left : Quadrant.Bottom) : (n == 0 ? Quadrant.Right : Quadrant.Top);
                newPlan.seats.Add(new Seat {
                    playerId = player.Id, name = MatchSessions.Get(player, MatchSessions.NameKey), robot = newPlan.seats.Count, red = red, quadrant = (int)quadrant,
                });
            }
            if (newPlan.seats.Count < 2) return "A match needs two players at least.";
            newPlan.hostRobot = newPlan.seats.FirstOrDefault(s => s.playerId == OnlineServices.PlayerId)?.robot ?? 0;
            var data = Bytes(JsonUtility.ToJson(newPlan));
            ToClients(Msg.Plan, data, true);
            Dispatch(NetworkManager.ServerClientId, Msg.Plan, data);
            Builds.ActionLog.Write("online", "match started with " + newPlan.seats.Count + " robots");
            return null;
        }

        // ------------------------------------------------------------------ setting up (everyone)

        private void Begin(MatchPlan newPlan) {
            plan = newPlan;
            var seat = plan.seats.FirstOrDefault(s => s.playerId == OnlineServices.PlayerId);
            if (seat == null) {
                Abort(null, false);
                return;
            }
            me = seat.robot;
            OnlineWindow.Close();
            OnlineLaunch.CloseLaunchWindow();
            Dialog.CloseAll();
            Popup.Close();
            if (SkillsGame.Active) SkillsGame.Current.Exit();
            if (Sandbox.Running) Sandbox.Stop();

            var games = GameDefinition.All();
            var settings = Protobot.GraphicsQuality.GraphicsSettingsManager.Current;
            var light = settings != null && settings.preset <= Protobot.GraphicsQuality.GraphicsPreset.Low;
            SkillsGame.Open(games.Length > 0 ? games[0] : null, light, SkillsGame.Mode.Match);
            game = SkillsGame.Current;
            if (game == null) {
                ToHost(Msg.Robot, Pack(JsonUtility.ToJson(new RobotPacket { robot = me, name = seat.name, problem = seat.name + " has no robot open." })), true);
                return;
            }
            game.online = true;
            game.onlineClient = !IsHost;
            game.SetAlliance(seat.red);
            game.SetQuadrant((Quadrant)seat.quadrant);
            game.SetSkipAutonomous(plan.skipAutonomous);
            game.Apply();
            game.Exited += OnExited;
            game.Finishing += OnFinishing;
            game.onlineMidfield = MidfieldCounts;
            // V2: each player sets their own start (on their wall, in their Quadrant), then Ready sends it.
            game.onlineReady = () => Ready(seat);
            GameUi.ShowSetup(game);
            Debug.Log("[Match] seat " + me + (seat.red ? " red " : " blue ") + (Quadrant)seat.quadrant + ", setting up");
            if (AutoReady) StartCoroutine(ReadySoon(seat));
        }

        /// <summary>Testing: "-autoready" presses Ready by itself (at the default start).</summary>
        private static readonly bool AutoReady = Environment.GetCommandLineArgs().Contains("-autoready");

        /// <summary>Testing only (set by "-autoready", or from a script): Ready accepts a start that isn't legal (test
        /// robots like the samples, with spare parts set aside in their builds).</summary>
        public static bool TestAllowIllegal = AutoReady;

        private IEnumerator ReadySoon(Seat seat) {
            yield return new WaitForSeconds(2f);
            Ready(seat);
        }

        /// <summary>Ready: a legal start (SG1) is sent to the host and stays as it is.</summary>
        private void Ready(Seat seat) {
            if (game == null || game.onlineLocked) return;
            if (!game.Legal && !TestAllowIllegal) {
                Interface.Interface.Say("That start isn't legal yet (see the checklist): fix it, then Ready", false);
                return;
            }
            game.onlineLocked = true;
            var mine = MakePacket(seat, game.preload);
            Debug.Log("[Match] ready: robot " + mine.build.parts.Length + " parts at x " + mine.x.ToString("F1") + ", y " + mine.y.ToString("F1") + ", heading " + mine.heading.ToString("F0"));
            GameUi.ShowSetup(game);
            ToHost(Msg.Robot, Pack(JsonUtility.ToJson(mine)), true);
        }

        /// <summary>This computer's robot: its parts numbered in the order of its file.</summary>
        private RobotPacket MakePacket(Seat seat, bool preload) {
            var parts = PartsManager.FindLoadedObjects().Where(p => p.activeInHierarchy && !p.IsDeleted()).ToList();
            var data = new ObjectData[parts.Count];
            for (var i = 0; i < parts.Count; i++) {
                var t = parts[i].transform;
                var saved = t.GetComponent<SavedObject>();
                var renderer = t.GetComponent<Renderer>();
                var color = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.color : Color.white;
                data[i] = new ObjectData {
                    partId = saved.id, states = saved.state,
                    xPos = t.position.x, yPos = t.position.y, zPos = t.position.z,
                    xRot = t.eulerAngles.x, yRot = t.eulerAngles.y, zRot = t.eulerAngles.z,
                    rColor = color.r, gColor = color.g, bColor = color.b,
                    colors = renderer != null && renderer.sharedMaterials.Length > 1
                        ? string.Join(",", renderer.sharedMaterials.Select(m => m != null ? ColorUtility.ToHtmlStringRGB(m.color) : "FFFFFF")) : null,
                };
                var tag = parts[i].GetOrAdd<NetPart>();
                tag.robot = me;
                tag.index = i;
                myTags.Add(tag);
            }
            var custom = data.Select(d => d.partId).Distinct().Select(CustomParts.Find).Where(c => c != null).ToArray();
            return new RobotPacket {
                robot = me, name = seat.name,
                build = new BuildData { version = Application.version, parts = data, drive = Controls.DriveToken },
                customParts = custom,
                front = game.FrontName, cx = game.robotCenter.x, cz = game.robotCenter.z, floor = game.robotFloor,
                depth = game.depth, width = game.width, height = game.height, x = game.x, y = game.y, heading = game.heading,
                preload = preload, preloadSpot = (int)game.preloadSpot,
            };
        }

        // The pose (in the field's frame) of a point of a robot's build: as SkillsGame.Apply places the field round it.
        private static Quaternion BuildToField(RobotPacket p) {
            var local = new Vector3(Mathf.Sin(p.heading * Mathf.Deg2Rad), 0f, Mathf.Cos(p.heading * Mathf.Deg2Rad));
            var front = SkillsGame.AxisOf(p.front);
            var rotation = Quaternion.FromToRotation(local, front);
            if (Mathf.Abs(Vector3.Dot(local, front) + 1f) < 1e-4f) rotation = Quaternion.AngleAxis(180f, Vector3.up);
            return Quaternion.Inverse(rotation);
        }

        private IEnumerator LoadRobots(RejoinInfo rejoin = null) {
            var field = game.field.transform;
            foreach (var p in packets.Values.OrderBy(p => p.robot)) {
                if (p.robot == me || p.build?.parts == null) continue;
                if (p.customParts != null)
                    foreach (var c in p.customParts)
                        if (c != null && !string.IsNullOrEmpty(c.key)) CustomParts.AddFromBuild(c);
                var turn = BuildToField(p);
                var center = new Vector3(p.cx, p.floor, p.cz);
                // Parts first, then what connects them (screws find their holes), like loading a build.
                foreach (var connecting in new[] { false, true })
                    for (var i = 0; i < p.build.parts.Length; i++) {
                        var source = p.build.parts[i];
                        var type = PartsManager.GetPartType(source.partId);
                        if (type == null || type.connectingPart != connecting) continue;
                        var data = JsonUtility.FromJson<ObjectData>(JsonUtility.ToJson(source));
                        var local = turn * (source.GetPos() - center) + new Vector3(p.x, 0f, p.y);
                        var position = field.TransformPoint(local);
                        var rotation = (field.rotation * turn * source.GetRot()).eulerAngles;
                        data.xPos = position.x; data.yPos = position.y; data.zPos = position.z;
                        data.xRot = rotation.x; data.yRot = rotation.y; data.zRot = rotation.z;
                        GameObject made = null;
                        try { made = SceneBuild.GeneratePart(data, p.build); }
                        catch (Exception e) { Debug.LogWarning("[Online] Couldn't make " + source.partId + ": " + e.Message); }
                        if (made == null) continue;
                        var tag = made.GetOrAdd<NetPart>();
                        tag.robot = p.robot;
                        tag.index = i;
                        remoteParts.Add(made);
                    }
            }
            // Screws find their holes in the physics steps.
            Debug.Log("[Match] loaded " + remoteParts.Count + " parts of the other robots");
            for (var i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            yield return new WaitForSeconds(0.5f);
            if (rejoin != null) GoRejoin(rejoin);
            else ToHost(Msg.Loaded, BitConverter.GetBytes(me), true);
        }

        // ------------------------------------------------------------------ messages

        private void Handle(ulong sender, Msg type, byte[] data) {
            switch (type) {
                case Msg.Robot: {
                    var p = JsonUtility.FromJson<RobotPacket>(Unpack(data));
                    if (!string.IsNullOrEmpty(p.problem)) {
                        if (IsHost) {
                            ToClients(Msg.Abort, Bytes(p.problem), true);
                            Abort(p.problem, false);
                        }
                        return;
                    }
                    packets[p.robot] = p;
                    if (packets.Count < plan.seats.Count) return;
                    if (IsHost)   // everyone's robot to everyone
                        foreach (var each in packets.Values) ToClients(Msg.Robot, Pack(JsonUtility.ToJson(each)), true);
                    if (game != null && !loadStarted) {
                        loadStarted = true;
                        GameUi.CloseAll();   // (everyone is ready: the setup panel goes)
                        Interface.Interface.Say("Everyone is ready: loading the other robots…", false);
                        StartCoroutine(LoadRobots());
                    }
                    break;
                }
                case Msg.Loaded:
                    if (!IsHost) return;
                    loaded.Add(BitConverter.ToInt32(data, 0));
                    if (loaded.Count < plan.seats.Count) return;
                    ToClients(Msg.Go, Array.Empty<byte>(), true);
                    Go();
                    break;
                case Msg.Go:
                    Go();
                    break;
                case Msg.State:
                    ReadState(sender, data, false);
                    break;
                case Msg.Final:   // a player's final positions (the host asked for them)
                    if (!IsHost) return;
                    ReadState(sender, data, true);
                    waitingFor.Remove(BitConverter.ToInt32(data, 0));
                    break;
                case Msg.Claim:
                    if (IsHost) HostClaim(BitConverter.ToInt32(data, 4), BitConverter.ToInt32(data, 0));
                    break;
                case Msg.Release:
                    if (IsHost) HostRelease(BitConverter.ToInt32(data, 4), BitConverter.ToInt32(data, 0));
                    break;
                case Msg.Owner:
                    if (!IsHost) ReadOwner(data);
                    break;
                case Msg.Collect:
                    if (IsHost) return;
                    SendFinal();
                    Freeze();
                    break;
                case Msg.Everything:
                    if (!IsHost) ReadEverything(data);
                    break;
                case Msg.Hold:
                    if (!IsHost) ShowHold(data);
                    break;
                case Msg.RejoinReady:
                    if (IsHost) HostRejoined(sender);
                    break;
                case Msg.Sync:
                    if (!IsHost) ReadEverything(data, false);
                    break;
                case Msg.Disable:
                    if (!IsHost) ApplyDisable(BitConverter.ToInt32(data, 0));
                    break;
                case Msg.Spawn:
                    if (!IsHost) ReadSpawn(data);
                    break;
                case Msg.Drop:
                    if (IsHost) HostDrop(data);
                    break;
                case Msg.Finish:
                    if (game != null && !IsHost) game.FinishNow();
                    break;
                case Msg.Abort:
                    Abort(Text(data), false);
                    break;
            }
        }

        // ------------------------------------------------------------------ the run

        // Ownership (Docs/Multiplayer concept.md §3): every body and game piece has one owner at a time, a seat (a robot's
        // index). A robot's bodies are its player's; a game piece is whoever's robot (or a piece they own) touches it,
        // else the host's. The owner simulates it, so its player's robot answers their keys at once and what they push
        // or pick up reacts at once; every other computer has it kinematic, following the owner's positions (sent with
        // its speeds 30 times a second, carried on by those speeds between them). The host settles who owns a piece.
        // A body's pose is sent as the pose of its key part (the part with the lowest key), not of the body's origin: a
        // body's origin depends on the order its parts were found in, which differs between a robot's own computer (its
        // live build) and the others (loaded from its file). `offset` is the key part in the body's frame.
        private class Keyed {
            public int key;
            public Rigidbody rb;
            public Collider[] colliders;
            public Vector3 offsetPosition;
            public Quaternion offsetRotation = Quaternion.identity;
            public int owner, robot = -1;   // robot: the seat whose robot it's part of (-1: a game piece)
            public CollisionDetectionMode mode;
            public float untouchedSince = -1f;
            // The owner's last report (the key part's pose and speeds, in the field's frame).
            public bool heard;
            public Vector3 p, v, w;
            public Quaternion r = Quaternion.identity;
            public float heardAt;
            public uint tick;
            public Vector3 Position => rb.position + rb.rotation * offsetPosition;
            public Quaternion Rotation => rb.rotation * offsetRotation;
        }

        private readonly Dictionary<int, Keyed> keyed = new();
        private readonly Dictionary<Rigidbody, Keyed> byBody = new();
        private int nextSpawn = 1;

        private Keyed KeyBody(int key, Rigidbody rb, Transform part = null, int robot = -1) {
            if (rb == null) return null;
            var k = new Keyed { key = key, rb = rb, robot = robot, colliders = rb.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).ToArray(),
                                mode = rb.collisionDetectionMode };
            if (part != null) {
                var body = rb.transform;   // (its transform: the physics body may not have its first pose yet)
                var inverse = Quaternion.Inverse(body.rotation);
                k.offsetPosition = inverse * (part.position - body.position);
                k.offsetRotation = inverse * part.rotation;
            }
            k.owner = robot >= 0 ? robot : plan.hostRobot;
            keyed[key] = k;
            byBody[rb] = k;
            Simulate(k, k.owner == me);
            return k;
        }

        /// <summary>This computer simulates the body (it owns it) or follows its owner (kinematic).</summary>
        private void Simulate(Keyed k, bool mine) {
            if (k.rb == null || k.rb.isKinematic == !mine) return;
            if (mine) {
                k.rb.isKinematic = false;
                k.rb.collisionDetectionMode = k.mode;
                if (k.heard) {   // carries on at the speed its last owner gave it
                    var t = game.field.transform;
                    k.rb.linearVelocity = t.TransformDirection(k.v);
                    k.rb.angularVelocity = t.TransformDirection(k.w);
                }
                k.rb.WakeUp();
            }
            else {
                // (a kinematic body can't use continuous collision: speculative)
                if (k.rb.collisionDetectionMode != CollisionDetectionMode.Discrete) k.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                k.rb.isKinematic = true;
            }
        }

        private void SetOwner(Keyed k, int owner) {
            k.owner = owner;
            k.untouchedSince = -1f;
            Simulate(k, owner == me);
        }

        private static int Hash(string s) {
            unchecked {
                var h = (int)2166136261;
                foreach (var c in s) h = (h ^ c) * 16777619;
                return h;
            }
        }

        private static int NodeKey(GameObject node) {
            var tag = node.GetComponentInParent<NetPart>();
            if (tag == null) return Hash("?" + node.name);
            var path = "";
            for (var t = node.transform; t != null && t != tag.transform; t = t.parent) path = t.name + "/" + path;
            return Hash(tag.robot + ":" + tag.index + ":" + path);
        }

        private static readonly Sandbox.DriveInput Off = new() { left = 0f, right = 0f, held = _ => false, pressed = _ => false };

        private float startedAt;
        private readonly Dictionary<int, float> seatHeardAt = new();
        private readonly HashSet<int> departed = new();

        private void Go() {
            if (game == null || running) return;
            Sandbox.RobotOf = part => {
                var tag = part != null ? part.GetComponentInParent<NetPart>() : null;
                return tag != null ? tag.robot : -1;
            };
            // Only this computer's robot takes this computer's keys; a robot whose player left is switched off.
            Sandbox.InputOf = robot => robot == me ? null : Off;
            Sandbox.LocalRobot = me;
            Sandbox.Puppet = false;
            game.onlineClient = true;   // every computer: the host takes the score once it has everyone's final positions
            game.onlineDrop = kind => {
                var w = new StringBuilder().Append(kind).Append('|').Append(game.Red ? 1 : 0).Append('|').Append(game.loader);
                ToHost(Msg.Drop, Bytes(w.ToString()), true);
                return true;
            };
            if (TestDrive != 0f) Sandbox.TestSticks = () => (new Vector2(0f, TestDrive), new Vector2(0f, TestDrive));   // (testing)
            game.onlineMayStart = true;
            game.StartRun();
            game.onlineMayStart = false;
            if (!Sandbox.Running) {
                Abort("The simulation couldn't start.", true);
                return;
            }
            running = true;
            startedAt = Time.time;
            // (The build's own parts stay where they started while copies of them move: they're the key parts' starts.)
            foreach (var (nodes, rb) in Sandbox.Instance.Bodies()) {
                var first = nodes.OrderBy(NodeKey).First();
                var tag = first.GetComponentInParent<NetPart>();
                KeyBody(NodeKey(first), rb, first.transform, tag != null ? tag.robot : plan.hostRobot);
            }
            for (var i = 0; i < game.Pins.Count; i++) if (game.Pins[i] != null) KeyBody(Hash("pin:" + i), game.Pins[i].body);
            for (var i = 0; i < game.Cups.Count; i++) if (game.Cups[i] != null) KeyBody(Hash("cup:" + i), game.Cups[i].body);
            for (var i = 0; i < game.Toggles.Count; i++) if (game.Toggles[i] != null) KeyBody(Hash("toggle:" + i), game.Toggles[i].body);
            if (IsHost) {
                MakePreloads();
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            }
            foreach (var seat in plan.seats) seatHeardAt[seat.robot] = Time.time;
            MakePing();
            Debug.Log("[Match] running: " + keyed.Count + " bodies and pieces keyed, " + keyed.Values.Count(k => k.owner == me) + " simulated here");
            StartCoroutine(CheckFollowing());
        }

        // Every body the others send should be one this computer has (the same parts, joined the same way).
        private IEnumerator CheckFollowing() {
            yield return new WaitForSeconds(4f);
            var others = keyed.Values.Where(k => k.owner != me).ToList();
            var heard = others.Count(k => k.heard);
            Debug.Log("[Match] following " + heard + " of the " + others.Count + " bodies and pieces the others simulate"
                      + (heard < others.Count ? " (" + (others.Count - heard) + " not heard yet: still, or the robots differ)" : "") + ", unknown keys " + unknown);
        }

        /// <summary>Where each robot is (its parts' middle, in the field's frame): logged at the end to compare computers.</summary>
        private void LogRobots() {
            var t = game.field.transform;
            foreach (var group in FindObjectsByType<NetPart>(FindObjectsSortMode.None).GroupBy(n => n.robot).OrderBy(g => g.Key)) {
                Vector3 sum = Vector3.zero, start = Vector3.zero;
                int n = 0, loose = 0;
                foreach (var part in group) {
                    sum += t.InverseTransformPoint(Sandbox.SimPoint(part.gameObject, part.transform.position));
                    start += t.InverseTransformPoint(part.transform.position);
                    if (!Sandbox.Simulated(part.gameObject)) loose++;
                    n++;
                }
                var c = sum / Mathf.Max(1, n);
                var s0 = start / Mathf.Max(1, n);
                Debug.Log("[Match] robot " + group.Key + " ends at x " + c.x.ToString("F2") + ", y " + c.z.ToString("F2") + " (" + n + " parts, "
                          + loose + " not simulated; started at x " + s0.x.ToString("F2") + ", y " + s0.z.ToString("F2") + ")");
            }
        }

        // SG5: each robot's Preload, touching it (SkillsGame.PreloadPose for any robot, in the field's frame).
        private void MakePreloads() {
            var field = game.field;
            foreach (var p in packets.Values.OrderBy(p => p.robot)) {
                if (!p.preload) continue;
                var seat = plan.seats.FirstOrDefault(s => s.robot == p.robot);
                if (seat == null) continue;
                var f = new Vector3(Mathf.Sin(p.heading * Mathf.Deg2Rad), 0f, Mathf.Cos(p.heading * Mathf.Deg2Rad));
                var side = Vector3.Cross(Vector3.up, f).normalized;
                var center = new Vector3(p.x, 0f, p.y);
                var standing = Quaternion.LookRotation(Vector3.up, f);
                var (local, turn) = (SkillsGame.PreloadSpot)p.preloadSpot switch {
                    SkillsGame.PreloadSpot.Back => (center - f * (p.depth / 2f + 1.65f) + Vector3.up * 3.3f, standing),
                    SkillsGame.PreloadSpot.Left => (center - side * (p.width / 2f + 1.65f) + Vector3.up * 3.3f, standing),
                    SkillsGame.PreloadSpot.Right => (center + side * (p.width / 2f + 1.65f) + Vector3.up * 3.3f, standing),
                    SkillsGame.PreloadSpot.Top => (center + Vector3.up * (p.height + 1.7f), Quaternion.LookRotation(side, Vector3.up)),
                    _ => (center + f * (p.depth / 2f + 1.65f) + Vector3.up * 3.3f, standing),
                };
                var kind = seat.red ? "Red-Neutral" : "Blue-Neutral";
                var stock = field.StockPin(kind);
                if (stock == null) continue;
                var pin = field.MakePin(stock.transform, Sandbox.Instance.Root, field.transform.TransformPoint(local), field.transform.rotation * turn, false);
                game.Pins.Add(pin);
                Announce(kind, pin.body);
            }
        }

        /// <summary>The host made a game piece: everyone makes it where it is (the host owns it until it's touched).</summary>
        private void Announce(string kind, Rigidbody body) {
            var key = Hash("spawn:" + nextSpawn++);
            KeyBody(key, body);
            spawned.Add(new SpawnInfo { key = key, kind = kind });
            var t = game.field.transform;
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream)) {
                w.Write(key);
                w.Write(kind);
                WritePose(w, t.InverseTransformPoint(body.position), Quaternion.Inverse(t.rotation) * body.rotation);
            }
            ToClients(Msg.Spawn, stream.ToArray(), true);
        }

        private void ReadSpawn(byte[] data) {
            if (game == null || !Sandbox.Running) return;
            using var r = new BinaryReader(new MemoryStream(data));
            var key = r.ReadInt32();
            var kind = r.ReadString();
            var (local, turn) = ReadPose(r);
            var field = game.field;
            var position = field.transform.TransformPoint(local);
            var rotation = field.transform.rotation * turn;
            Rigidbody body;
            if (kind == "Cup") {
                var stock = field.StockCup();
                if (stock == null) return;
                var cup = field.MakeCup(stock.transform, Sandbox.Instance.Root, position, rotation, false);
                game.Cups.Add(cup);
                body = cup.body;
            }
            else {
                var stock = field.StockPin(kind);
                if (stock == null) return;
                var pin = field.MakePin(stock.transform, Sandbox.Instance.Root, position, rotation, false);
                game.Pins.Add(pin);
                body = pin.body;
            }
            var k = KeyBody(key, body);
            if (k != null) {
                k.p = local;
                k.r = turn;
                k.heard = true;
                k.heardAt = Time.time;
            }
        }

        private void HostDrop(byte[] data) {
            if (game == null || !Sandbox.Running) return;
            var parts = Text(data).Split('|');
            if (parts.Length < 3) return;
            var piece = game.SpawnMatchLoad(parts[0], parts[1] == "1", int.TryParse(parts[2], out var n) ? n : 0);
            var body = piece is Pin pin ? pin.body : piece is Cup cup ? cup.body : null;
            if (body != null) Announce(parts[0] == "Cup" ? "Cup" : parts[0], body);
        }

        // ------------------------------------------------------------------ what each computer simulates, to the others

        private const float StateEvery = 1f / 30f;
        private float nextState, nextFull;
        private uint tick;
        private int unknown;
        private readonly Dictionary<int, (Vector3 p, Quaternion r)> sent = new();

        /// <summary>Testing: "-testdrive 0.5" drives this computer's robot forward at that power once driving starts.</summary>
        private static readonly float TestDrive = TestDrivePower();

        private static float TestDrivePower() {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-testdrive" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    return Mathf.Clamp(v, -1f, 1f);
            return 0f;
        }

        private static void WritePose(BinaryWriter w, Vector3 p, Quaternion r) {
            w.Write(p.x); w.Write(p.y); w.Write(p.z);
            w.Write(r.x); w.Write(r.y); w.Write(r.z); w.Write(r.w);
        }

        private static (Vector3, Quaternion) ReadPose(BinaryReader r) =>
            (new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));

        private static void WriteHalf(BinaryWriter w, Vector3 v) {
            w.Write(Mathf.FloatToHalf(v.x)); w.Write(Mathf.FloatToHalf(v.y)); w.Write(Mathf.FloatToHalf(v.z));
        }

        private static Vector3 ReadHalf(BinaryReader r) =>
            new(Mathf.HalfToFloat(r.ReadUInt16()), Mathf.HalfToFloat(r.ReadUInt16()), Mathf.HalfToFloat(r.ReadUInt16()));

        /// <summary>The bodies this computer simulates that moved (all of them now and then, and all at the end), in the
        /// field's frame, with their speeds: the others carry them on by those speeds until the next packet.</summary>
        private List<byte[]> StatePackets(bool all, bool reliable) {
            var t = game.field.transform;
            var inverse = Quaternion.Inverse(t.rotation);
            var full = all || Time.unscaledTime >= nextFull;
            if (full && !all) nextFull = Time.unscaledTime + 1f;
            tick++;
            var entries = new List<Keyed>();
            foreach (var k in keyed.Values) {
                if (k.rb == null || k.owner != me) continue;
                var p = t.InverseTransformPoint(k.Position);
                var r = inverse * k.Rotation;
                if (!full && sent.TryGetValue(k.key, out var last) && (last.p - p).sqrMagnitude < 0.0001f && Quaternion.Angle(last.r, r) < 0.1f) continue;
                sent[k.key] = (p, r);
                entries.Add(k);
            }
            var packets = new List<byte[]>();
            var perPacket = reliable ? 1000 : 21;   // 44 bytes each: an unreliable packet stays under ~1 KB
            for (var start = 0; start < entries.Count || start == 0; start += perPacket) {
                using var stream = new MemoryStream();
                using (var w = new BinaryWriter(stream)) {
                    w.Write(me);
                    w.Write(tick);
                    w.Write((byte)(Trying() ? 1 : 0));
                    var count = Math.Min(perPacket, Math.Max(0, entries.Count - start));
                    w.Write((ushort)count);
                    for (var i = start; i < start + count; i++) {
                        var k = entries[i];
                        w.Write(k.key);
                        WritePose(w, t.InverseTransformPoint(k.Position), inverse * k.Rotation);
                        WriteHalf(w, t.InverseTransformDirection(k.rb.GetPointVelocity(k.Position)));
                        WriteHalf(w, t.InverseTransformDirection(k.rb.angularVelocity));
                    }
                }
                packets.Add(stream.ToArray());
                if (entries.Count <= perPacket) break;
            }
            return packets;
        }

        private void SendState() {
            foreach (var packet in StatePackets(false, false)) {
                if (IsHost) ToClients(Msg.State, packet, false);
                else Send(NetworkManager.ServerClientId, Msg.State, packet, false);
            }
        }

        /// <summary>An owner's positions: kept for following (or placed at once: the end), the host passes them on.</summary>
        private void ReadState(ulong sender, byte[] data, bool exact) {
            using var r = new BinaryReader(new MemoryStream(data));
            var seat = r.ReadInt32();
            var at = r.ReadUInt32();
            trying[seat] = r.ReadByte() == 1;
            var count = r.ReadUInt16();
            seatHeardAt[seat] = Time.time;
            for (var i = 0; i < count; i++) {
                var key = r.ReadInt32();
                var (p, q) = ReadPose(r);
                var v = ReadHalf(r);
                var w = ReadHalf(r);
                if (!keyed.TryGetValue(key, out var k)) {
                    unknown++;
                    continue;
                }
                if (k.owner != seat || (k.heard && k.tick > at && !exact)) continue;   // (an old owner's, or late)
                if (IsHost && k.robot == seat && k.heard) Check(seat, k, p, v);
                k.p = p;
                k.r = q;
                k.v = v;
                k.w = w;
                k.tick = at;
                k.heard = true;
                k.heardAt = Time.time;
                if (exact) PlaceExact(k);
            }
            // The host passes a player's positions on to everyone else.
            if (IsHost && !exact && sender != NetworkManager.ServerClientId) {
                var net = NetworkManager.Singleton;
                foreach (var id in net.ConnectedClientsIds)
                    if (id != NetworkManager.ServerClientId && id != sender) SendRaw(net, id, Msg.State, data, false);
            }
        }

        /// <summary>Follows the bodies other computers simulate: their last positions carried on by their speeds (a
        /// tenth of a second at most), approached smoothly; kinematic, so they push what they run into here.</summary>
        private void FixedUpdate() {
            if (!running || game == null || !Sandbox.Running || finishing) return;
            var t = game.field.transform;
            foreach (var k in keyed.Values) {
                if (k.owner == me || !k.heard || k.rb == null || !k.rb.isKinematic) continue;
                var age = Mathf.Clamp(Time.time - k.heardAt, 0f, 0.1f);
                var p = k.p + k.v * age;
                var spin = k.w.magnitude * age * Mathf.Rad2Deg;
                var q = spin > 1e-3f ? Quaternion.AngleAxis(spin, k.w.normalized) * k.r : k.r;
                var rotation = t.rotation * q * Quaternion.Inverse(k.offsetRotation);
                var position = t.TransformPoint(p) - rotation * k.offsetPosition;
                if ((k.rb.position - position).sqrMagnitude > 36f) {   // far off (6"): there at once
                    k.rb.position = position;
                    k.rb.rotation = rotation;
                    continue;
                }
                k.rb.MovePosition(Vector3.Lerp(k.rb.position, position, 0.3f));
                k.rb.MoveRotation(Quaternion.Slerp(k.rb.rotation, rotation, 0.3f));
            }
        }

        private void PlaceExact(Keyed k) {
            if (k.rb == null) return;
            var t = game.field.transform;
            var rotation = t.rotation * k.r * Quaternion.Inverse(k.offsetRotation);
            var position = t.TransformPoint(k.p) - rotation * k.offsetPosition;
            k.rb.transform.SetPositionAndRotation(position, rotation);
            k.rb.position = position;
            k.rb.rotation = rotation;
        }

        // ------------------------------------------------------------------ who owns a game piece

        private float nextClaimCheck;
        private static readonly Collider[] overlaps = new Collider[64];

        /// <summary>Whether a seat's robot, or a piece that seat owns, touches the piece (within 0.25").</summary>
        private bool Touching(Keyed piece, int seat) {
            // (a toggle's face strips are cubes whose colliders go a frame after it's made: drop what's gone)
            if (piece.colliders.Any(c => c == null)) piece.colliders = piece.colliders.Where(c => c != null).ToArray();
            if (piece.colliders.Length == 0) return false;
            var bounds = piece.colliders[0].bounds;
            foreach (var c in piece.colliders) bounds.Encapsulate(c.bounds);
            var n = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * 0.25f, overlaps, Quaternion.identity,
                (1 << Sandbox.Layer) | (1 << Sandbox.ElementLayer), QueryTriggerInteraction.Ignore);
            for (var i = 0; i < n; i++) {
                var rb = overlaps[i].attachedRigidbody;
                if (rb == null || rb == piece.rb || !byBody.TryGetValue(rb, out var other)) continue;
                if (other.robot == seat || (other.robot < 0 && other.owner == seat && seat != plan.hostRobot)) return true;
            }
            return false;
        }

        /// <summary>Takes the pieces this computer's robot touches (at once: the host is told, and may give them back),
        /// and gives back the ones it has left alone for a second.</summary>
        private void Claims() {
            if (Time.unscaledTime < nextClaimCheck || game.state != SkillsGame.State.Running) return;
            nextClaimCheck = Time.unscaledTime + 0.05f;
            foreach (var k in keyed.Values) {
                if (k.robot >= 0 || k.rb == null) continue;
                if (k.owner != me) {
                    if (!Touching(k, me)) continue;
                    if (IsHost) HostClaim(me, k.key);   // (the host settles its own claims at once)
                    else {
                        SetOwner(k, me);   // at once: the host may give it back
                        ToHost(Msg.Claim, Pair(k.key, me), true);
                    }
                }
                else if (!IsHost) {
                    if (Touching(k, me)) k.untouchedSince = -1f;
                    else if (k.untouchedSince < 0f) k.untouchedSince = Time.time;
                    else if (Time.time - k.untouchedSince > 1f && k.rb.linearVelocity.magnitude < 3f) {
                        k.untouchedSince = -1f;
                        ToHost(Msg.Release, Pair(k.key, me), true);
                    }
                }
            }
        }

        /// <summary>The host settles a claim: the piece goes to the claimer unless its owner's robot holds it now.</summary>
        private int granted, refused;

        private void HostClaim(int seat, int key) {
            if (!keyed.TryGetValue(key, out var k) || k.robot >= 0) return;
            var ok = k.owner == seat || !Touching(k, k.owner) || departed.Contains(k.owner);
            if (ok) this.granted++;
            else refused++;
            var granted = ok;
            if (granted && k.owner != seat) SetOwner(k, seat);
            BroadcastOwner(k);
        }

        private void HostRelease(int seat, int key) {
            if (!keyed.TryGetValue(key, out var k) || k.owner != seat) return;
            SetOwner(k, plan.hostRobot);
            BroadcastOwner(k);
        }

        private static byte[] Pair(int a, int b) {
            var data = new byte[8];
            BitConverter.GetBytes(a).CopyTo(data, 0);
            BitConverter.GetBytes(b).CopyTo(data, 4);
            return data;
        }

        private void BroadcastOwner(Keyed k) {
            var data = new byte[8];
            BitConverter.GetBytes(k.key).CopyTo(data, 0);
            BitConverter.GetBytes(k.owner).CopyTo(data, 4);
            ToClients(Msg.Owner, data, true);
        }

        private void ReadOwner(byte[] data) {
            for (var i = 0; i + 8 <= data.Length; i += 8)
                if (keyed.TryGetValue(BitConverter.ToInt32(data, i), out var k)) SetOwner(k, BitConverter.ToInt32(data, i + 4));
        }

        /// <summary>The host takes over a robot whose player left (or stopped answering): it stays on the field, switched
        /// off, and can be pushed; the pieces they had go back to the host too.</summary>
        private void TakeOverDeparted() {
            foreach (var seat in plan.seats) {
                if (seat.robot == me || departed.Contains(seat.robot)) continue;
                if (seatHeardAt.TryGetValue(seat.robot, out var heardAt) && Time.time - heardAt < 2.5f) continue;
                departed.Add(seat.robot);
                Debug.Log("[Match] " + seat.name + " stopped answering: their robot stays, switched off");
                Record("left", seat.robot, seat.name + " left or stopped answering");
                using var stream = new MemoryStream();
                foreach (var k in keyed.Values.Where(k => k.owner == seat.robot).ToList()) {
                    SetOwner(k, me);
                    stream.Write(BitConverter.GetBytes(k.key), 0, 4);
                    stream.Write(BitConverter.GetBytes(me), 0, 4);
                }
                if (stream.Length > 0) ToClients(Msg.Owner, stream.ToArray(), true);
            }
        }

        // ------------------------------------------------------------------ robots in the Midfield (scoring)

        /// <summary>SC6: Robots with any part in the Midfield (the 34.05" diamond, any height), per Alliance.</summary>
        private (int red, int blue) MidfieldCounts() {
            if (game == null || game.field == null) return (0, 0);
            var t = game.field.transform;
            var hits = Physics.OverlapBox(game.field.ToWorld(Vector2.zero, 30f), new Vector3(17.025f, 30f, 17.025f), t.rotation * Quaternion.Euler(0f, 45f, 0f),
                1 << Sandbox.Layer, QueryTriggerInteraction.Ignore);
            var robots = new HashSet<int>();
            foreach (var h in hits)
                if (h.attachedRigidbody != null && byBody.TryGetValue(h.attachedRigidbody, out var k) && k.robot >= 0) robots.Add(k.robot);
            int red = 0, blue = 0;
            foreach (var r in robots) {
                var seat = plan.seats.FirstOrDefault(s => s.robot == r);
                if (seat == null) continue;
                if (seat.red) red++;
                else blue++;
            }
            return (red, blue);
        }

        // ------------------------------------------------------------------ holding (GG17)

        // GG17: no Holding (Pinning, Trapping) an opposing Robot for more than a 3-count. The host watches: a robot
        // touching an opponent that is trying to drive but stuck against the field or another robot. The holder gets a
        // red outline and a count on every screen; past 3 it's warned, and still holding at 5 it's disabled for the rest
        // of the match. The count pauses when they're 2 ft apart or either has moved 2 ft from where it started.
        private readonly Dictionary<int, bool> trying = new();
        private readonly HashSet<int> disabled = new();
        private class Hold {
            public int holder, held;
            public float count, quietSince = -1f;
            public Vector3 heldStart, holderStart;
            public bool warned;
        }
        private Hold hold;
        private float nextHoldCheck;
        private string holdShown;

        private bool Trying() {
            if (!Sandbox.DriveEnabled || disabled.Contains(me)) return false;
            var (left, right) = Sandbox.DrivePower();
            return Mathf.Abs(left) > 0.15f || Mathf.Abs(right) > 0.15f;
        }

        private Vector3 RobotMiddle(int seat) {
            var sum = Vector3.zero;
            var n = 0;
            foreach (var k in keyed.Values)
                if (k.robot == seat && k.rb != null) {
                    sum += k.rb.worldCenterOfMass;
                    n++;
                }
            return n > 0 ? sum / n : Vector3.zero;
        }

        /// <summary>How fast a robot's frame (its heaviest body) moves: its wheels spin even when it's stuck.</summary>
        private float RobotSpeed(int seat) {
            Keyed frame = null;
            foreach (var k in keyed.Values)
                if (k.robot == seat && k.rb != null && (frame == null || k.rb.mass > frame.rb.mass)) frame = k;
            if (frame == null) return 0f;
            return frame.owner == me ? frame.rb.linearVelocity.magnitude : frame.v.magnitude;
        }

        /// <summary>Whether a robot's bodies touch (within 0.15") what the test says.</summary>
        private bool RobotTouches(int seat, Func<Collider, bool> what) {
            foreach (var k in keyed.Values) {
                if (k.robot != seat || k.rb == null) continue;
                foreach (var c in k.colliders) {
                    if (c == null) continue;
                    var b = c.bounds;
                    var n = Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * 0.15f, overlaps, Quaternion.identity,
                        (1 << Sandbox.Layer) | (1 << Sandbox.FieldLayer) | (1 << Sandbox.ElementLayer), QueryTriggerInteraction.Ignore);
                    for (var i = 0; i < n; i++)
                        if (overlaps[i].attachedRigidbody != k.rb && what(overlaps[i])) return true;
                }
            }
            return false;
        }

        private int SeatOf(Collider c) => c.attachedRigidbody != null && byBody.TryGetValue(c.attachedRigidbody, out var k) ? k.robot : -1;

        private void CheckHolding() {
            if (game.state != SkillsGame.State.Running || game.InAutonomous || Time.time < nextHoldCheck) return;
            const float Step = 0.1f;
            nextHoldCheck = Time.time + Step;
            if (hold != null) {
                var stillTouching = RobotTouches(hold.held, c => SeatOf(c) == hold.holder);
                var apart = Vector3.Distance(RobotMiddle(hold.held), hold.heldStart) > 24f || Vector3.Distance(RobotMiddle(hold.holder), hold.holderStart) > 24f;
                if (!stillTouching || apart || disabled.Contains(hold.holder)) {
                    if (hold.quietSince < 0f) hold.quietSince = Time.time;
                    if (apart || disabled.Contains(hold.holder) || Time.time - hold.quietSince > 0.5f) EndHold();
                    return;
                }
                hold.quietSince = -1f;
                hold.count += Step;
                if (hold.count > 3f && !hold.warned) {
                    hold.warned = true;
                    var name = SeatName(hold.holder);
                    Record("violation", hold.holder, name + " held " + SeatName(hold.held) + " past a 3-count (GG17): warned");
                }
                if (hold.count > 5f) {
                    Record("violation", hold.holder, SeatName(hold.holder) + " kept holding past 5 s: disabled");
                    Disable(hold.holder, SeatName(hold.holder) + "'s robot is disabled for holding (GG17)");
                    EndHold();
                    return;
                }
                SendHold();
                return;
            }
            // A new hold: a stuck opponent that's trying to drive, touched by a robot of the other Alliance, and pressed
            // against the field or another robot.
            foreach (var held in plan.seats) {
                var stuck = trying.TryGetValue(held.robot, out var t) && t && RobotSpeed(held.robot) < 3f && !disabled.Contains(held.robot);
                if (held.robot == me) stuck = Trying() && RobotSpeed(held.robot) < 3f && !disabled.Contains(me);
                if (!stuck) continue;
                foreach (var holder in plan.seats) {
                    if (holder.red == held.red || disabled.Contains(holder.robot)) continue;
                    if (!RobotTouches(held.robot, c => SeatOf(c) == holder.robot)) continue;
                    if (!RobotTouches(held.robot, c => c.gameObject.layer == Sandbox.FieldLayer || (SeatOf(c) >= 0 && SeatOf(c) != holder.robot && SeatOf(c) != held.robot))) continue;
                    hold = new Hold { holder = holder.robot, held = held.robot, heldStart = RobotMiddle(held.robot), holderStart = RobotMiddle(holder.robot) };
                    SendHold();
                    return;
                }
            }
        }

        private string SeatName(int seat) => plan.seats.FirstOrDefault(s => s.robot == seat)?.name ?? "robot " + seat;

        private void EndHold() {
            hold = null;
            SendHold();
        }

        private void SendHold() {
            var data = hold == null ? Pair(-1, 0) : Pair(hold.holder, Mathf.CeilToInt(hold.count * 10f));
            ToClients(Msg.Hold, data, true);
            ShowHold(data);
        }

        // Everyone: the holding robot outlined red, with its count above it.
        private int holdSeat = -1;
        private float holdCount;

        private void ShowHold(byte[] data) {
            holdSeat = BitConverter.ToInt32(data, 0);
            holdCount = BitConverter.ToInt32(data, 4) / 10f;
            var key = holdSeat + "|" + Mathf.FloorToInt(holdCount);
            if (key == holdShown) return;
            holdShown = key;
            if (holdSeat < 0) {
                Highlights.Clear("holding");
                return;
            }
            var parts = FindObjectsByType<NetPart>(FindObjectsSortMode.None).Where(n => n.robot == holdSeat).Select(n => n.gameObject).ToList();
            Highlights.Set("holding", parts, new Color(0.95f, 0.15f, 0.12f, 1f), true);
        }

        private void DrawHold() {
            if (holdSeat < 0 || game == null) return;
            Overlay.Begin();
            var middle = RobotMiddle(holdSeat) + Vector3.up * 10f;
            if (Overlay.ToScreen(middle, out var at)) {
                var left = Mathf.Max(0f, 3f - holdCount);
                Overlay.Text(at, holdCount <= 3f ? "Holding " + Mathf.CeilToInt(left) : "Holding! Back away", new Color(0.95f, 0.2f, 0.15f), null, true);
            }
        }

        private void Disable(int seat, string why) {
            ToClients(Msg.Disable, BitConverter.GetBytes(seat), true);
            ApplyDisable(seat);
            Interface.Interface.Say(why, false);
        }

        private void ApplyDisable(int seat) {
            disabled.Add(seat);
            if (seat == me) Interface.Interface.Say("Your robot is disabled for the rest of the match (holding an opponent past the 5-second limit)", false);
        }

        // ------------------------------------------------------------------ checks and the match record (for reports)

        private readonly Dictionary<int, float> lastFlag = new();

        /// <summary>The host checks that a robot moves as a robot can: no jumps across the field, no impossible speeds.</summary>
        private void Check(int seat, Keyed k, Vector3 p, Vector3 v) {
            var dt = Time.time - k.heardAt;
            var jump = (p - k.p).magnitude;
            string problem = null;
            if (v.magnitude > 180f) problem = "moving at " + v.magnitude.ToString("0") + " in/s";
            else if (dt > 0f && dt < 0.3f && jump > 18f) problem = "jumped " + jump.ToString("0") + "\" in " + (dt * 1000f).ToString("0") + " ms";
            if (problem == null) return;
            if (lastFlag.TryGetValue(seat, out var last) && Time.time - last < 2f) return;
            lastFlag[seat] = Time.time;
            var name = plan.seats.FirstOrDefault(s => s.robot == seat)?.name ?? "robot " + seat;
            Debug.Log("[Match] check: " + name + "'s robot " + problem);
            Record("check", seat, name + "'s robot " + problem);
        }

        /// <summary>The match so far: each robot's middle 5 times a second, checks and events (a report includes it).</summary>
        public static string LastRecording { get; private set; } = "";
        private readonly StringBuilder recording = new("time,seat,kind,x,y,what\n");
        private float nextRecord;

        private void Record(string kind, int seat, string what, Vector3 at = default) =>
            recording.Append((Time.time - startedAt).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(seat).Append(',')
                     .Append(kind).Append(',').Append(at.x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                     .Append(at.z.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(what.Replace(',', ';')).Append('\n');

        private void RecordRobots() {
            if (Time.time < nextRecord) return;
            nextRecord = Time.time + 0.2f;
            var t = game.field.transform;
            foreach (var group in keyed.Values.Where(k => k.robot >= 0 && k.rb != null).GroupBy(k => k.robot)) {
                var middle = Vector3.zero;
                var n = 0;
                foreach (var k in group) { middle += t.InverseTransformPoint(k.rb.worldCenterOfMass); n++; }
                Record("pos", group.Key, "", middle / Mathf.Max(1, n));
            }
            LastRecording = recording.ToString();
        }

        /// <summary>The match's players, each with Report and Vote kick (the HUD's Players button).</summary>
        public static void PlayersDialog() {
            var match = Current;
            if (match == null || match.plan == null) return;
            var dialog = Dialog.Open("Players", new Vector2(640f, 150f + 54f * match.plan.seats.Count));
            Ui.Column(dialog.Body, 6f);
            var session = MatchSessions.Current;
            foreach (var seat in match.plan.seats) {
                var row = Ui.Rect(seat.name, dialog.Body);
                Ui.Row(row, 10f);
                row.Size(-1, 44f);
                var dot = Ui.Image(row, "Alliance", seat.red ? Theme.AxisX : Theme.AxisZ, 8f);
                dot.Size(14f, 14f);
                var here = session != null && session.Players.Any(p => p.Id == seat.playerId);
                var label = seat.name + (seat.robot == match.me ? "  · you" : "") + (seat.robot == match.plan.hostRobot ? "  · host" : "")
                            + (!here || match.departed.Contains(seat.robot) ? "  · left (robot switched off)" : "");
                var name = Ui.Text(row, "Name", label, Theme.FontBody, Theme.Text, Theme.Medium);
                name.Size(-1, 40f, 1f);
                if (seat.robot != match.me && here) OnlineScreen.PlayerButtons(row, seat.playerId, seat.name);
            }
            var close = Ui.Button(dialog.Footer, "Close", "Close", dialog.Close, true, 6f);
            close.Size(110f, 34f);
            // (votes change while it's open: it shows them as they were when opened)
        }

        /// <summary>Whether a player has a seat in the match being played (others are turned away while it runs).</summary>
        public static bool HasSeat(string playerId) => Current != null && Current.plan != null && Current.plan.seats.Any(s => s.playerId == playerId);

        /// <summary>The players in this match: seat, name, player id.</summary>
        public IEnumerable<Seat> Seats => plan?.seats ?? new List<Seat>();

        // ------------------------------------------------------------------ ping

        private TMPro.TextMeshProUGUI ping;
        private float nextPing;

        private void MakePing() {
            if (Interface.Interface.Root == null) return;
            ping = Ui.Text(Interface.Interface.Root, "Ping", "", Theme.FontSmall, new Color(1f, 1f, 1f, 0.6f));
            ping.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -(Layout.TopBarBottom + 10f)), new Vector2(420f, 22f));
            ping.raycastTarget = false;
        }

        private void UpdatePing() {
            if (ping == null || Time.unscaledTime < nextPing) return;
            nextPing = Time.unscaledTime + 0.5f;
            var net = NetworkManager.Singleton;
            if (net == null || !(net.NetworkConfig.NetworkTransport is Unity.Netcode.Transports.UTP.UnityTransport transport)) return;
            if (!IsHost) {
                ping.text = "Ping " + transport.GetCurrentRtt(NetworkManager.ServerClientId) + " ms";
                return;
            }
            var parts = new List<string>();
            foreach (var id in net.ConnectedClientsIds) {
                if (id == NetworkManager.ServerClientId) continue;
                var player = MatchSessions.PlayerOfClient(id);
                var name = plan.seats.FirstOrDefault(s => s.playerId == player)?.name ?? "player";
                parts.Add(name + " " + transport.GetCurrentRtt(id) + " ms");
            }
            ping.text = "Hosting" + (parts.Count > 0 ? " · " + string.Join(" · ", parts) : "");
        }

        // ------------------------------------------------------------------ the end

        // When time's up the host asks everyone for the final positions of what they simulate, places them, takes the
        // score once and sends everything's final positions, so every computer shows the same field and score.
        private bool finishing;
        private float endedAt = -1f, collectUntil;
        private readonly HashSet<int> waitingFor = new();

        private void HostEnding() {
            if (game.state != SkillsGame.State.Ending) return;
            if (endedAt < 0f) endedAt = Time.time;
            if (finishing) {
                if (waitingFor.Count == 0 || Time.time > collectUntil) FinishAll();
                return;
            }
            // SC1: the score is taken when everything is still (here: what this computer simulates), or 5 s after.
            var still = keyed.Values.All(k => k.rb == null || k.rb.isKinematic || k.rb.IsSleeping() || k.rb.linearVelocity.magnitude < 0.5f);
            if (Time.time - endedAt < 1f || (!still && Time.time - endedAt < 5f)) return;
            finishing = true;
            collectUntil = Time.time + 3f;
            foreach (var seat in plan.seats)
                if (seat.robot != me && !departed.Contains(seat.robot)) waitingFor.Add(seat.robot);
            ToClients(Msg.Collect, Array.Empty<byte>(), true);
        }

        private void SendFinal() {
            foreach (var packet in StatePackets(true, true)) ToHost(Msg.Final, packet, true);
        }

        private void FinishAll() {
            if (game.state != SkillsGame.State.Ending) return;
            Freeze();
            Physics.SyncTransforms();
            game.FinishNow();   // (Finishing sends everyone the final positions of everything, then Finish)
        }

        /// <summary>Nothing moves any more: every computer shows the same final positions.</summary>
        private void Freeze() {
            finishing = true;
            foreach (var k in keyed.Values) Simulate(k, false);
        }

        private void OnFinishing() {
            LogRobots();
            if (IsHost) Debug.Log("[Match] pieces taken by touch: " + granted + " given, " + refused + " refused; checks flagged: " + lastFlag.Count);
            if (!IsHost) return;
            // Everything's final positions, as the host has them now (its own and everyone's last ones), to everyone.
            var t = game.field.transform;
            var inverse = Quaternion.Inverse(t.rotation);
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream)) {
                w.Write(keyed.Count);
                foreach (var k in keyed.Values) {
                    w.Write(k.key);
                    WritePose(w, t.InverseTransformPoint(k.Position), inverse * k.Rotation);
                }
            }
            ToClients(Msg.Everything, stream.ToArray(), true);
            ToClients(Msg.Finish, Array.Empty<byte>(), true);
            LastRecording = recording.ToString();
        }

        private void ReadEverything(byte[] data, bool freeze = true) {
            if (freeze) Freeze();
            using var r = new BinaryReader(new MemoryStream(data));
            var count = r.ReadInt32();
            for (var i = 0; i < count; i++) {
                var key = r.ReadInt32();
                var (p, q) = ReadPose(r);
                if (!keyed.TryGetValue(key, out var k)) continue;
                k.p = p;
                k.r = q;
                k.heard = true;
                k.heardAt = Time.time;
                PlaceExact(k);
            }
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------ rejoining (a player who left comes back)

        [Serializable]
        private class SpawnInfo {
            public int key;
            public string kind;
        }

        [Serializable]
        private class RejoinInfo {
            public MatchPlan plan;
            public List<string> packets = new();   // each robot's packet (JSON)
            public List<SpawnInfo> spawns = new();
            public float timeLeft;
            public bool running;
        }

        private readonly List<SpawnInfo> spawned = new();   // the host: every piece it made (preloads, match loads)

        /// <summary>The host: a player of this match connected again (they left, or their app closed): they get the
        /// whole match to catch up with.</summary>
        private void OnClientConnected(ulong clientId) {
            if (!IsHost || game == null || !running) return;
            var player = MatchSessions.PlayerOfClient(clientId);
            var seat = plan.seats.FirstOrDefault(s => s.playerId == player);
            if (seat == null || seat.robot == me) return;
            var info = new RejoinInfo { plan = plan, timeLeft = game.timeLeft, running = game.state == SkillsGame.State.Running };
            foreach (var p in packets.Values.OrderBy(p => p.robot)) info.packets.Add(JsonUtility.ToJson(p));
            info.spawns.AddRange(spawned);
            Send(clientId, Msg.Rejoin, Pack(JsonUtility.ToJson(info)), true);
            Debug.Log("[Match] " + seat.name + " is back: sending the match");
            Record("rejoin", seat.robot, seat.name + " rejoined");
        }

        /// <summary>The player coming back: the game round their own robot (its start as it was), every other robot
        /// loaded, then straight into the running match.</summary>
        private void BeginRejoin(RejoinInfo info) {
            plan = info.plan;
            var seat = plan.seats.FirstOrDefault(s => s.playerId == OnlineServices.PlayerId);
            if (seat == null) {
                Abort(null, false);
                return;
            }
            me = seat.robot;
            foreach (var json in info.packets) {
                var p = JsonUtility.FromJson<RobotPacket>(json);
                packets[p.robot] = p;
            }
            OnlineWindow.Close();
            OnlineLaunch.CloseLaunchWindow();
            Dialog.CloseAll();
            Popup.Close();
            if (SkillsGame.Active) SkillsGame.Current.Exit();
            if (Sandbox.Running) Sandbox.Stop();
            var games = GameDefinition.All();
            var settings = Protobot.GraphicsQuality.GraphicsSettingsManager.Current;
            var light = settings != null && settings.preset <= Protobot.GraphicsQuality.GraphicsPreset.Low;
            SkillsGame.Open(games.Length > 0 ? games[0] : null, light, SkillsGame.Mode.Match);
            game = SkillsGame.Current;
            if (game == null || !packets.TryGetValue(me, out var mine)) {
                Interface.Interface.Say("Couldn't rejoin the match: open the same robot and try again", false);
                Abort(null, false);
                return;
            }
            game.online = true;
            game.onlineClient = true;
            game.onlineLocked = true;
            game.SetAlliance(seat.red);
            game.SetQuadrant((Quadrant)seat.quadrant);
            game.SetSkipAutonomous(plan.skipAutonomous);
            game.SetPose(mine.x, mine.y, mine.heading);
            game.Exited += OnExited;
            game.Finishing += OnFinishing;
            game.onlineMidfield = MidfieldCounts;
            MakePacket(seat, game.preload);   // (numbers this computer's parts as before)
            GameUi.CloseAll();
            Interface.Interface.Say("Rejoining the match…", false);
            loadStarted = true;
            StartCoroutine(LoadRobots(info));
        }

        private void GoRejoin(RejoinInfo info) {
            Go();
            if (!running) return;
            var field = game.field;
            foreach (var s in info.spawns) {
                Rigidbody body;
                if (s.kind == "Cup") {
                    var stock = field.StockCup();
                    if (stock == null) continue;
                    var cup = field.MakeCup(stock.transform, Sandbox.Instance.Root, field.transform.position, field.transform.rotation, false);
                    game.Cups.Add(cup);
                    body = cup.body;
                }
                else {
                    var stock = field.StockPin(s.kind);
                    if (stock == null) continue;
                    var pin = field.MakePin(stock.transform, Sandbox.Instance.Root, field.transform.position, field.transform.rotation, false);
                    game.Pins.Add(pin);
                    body = pin.body;
                }
                KeyBody(s.key, body);
            }
            // Straight into the match as it is now.
            game.countdown = 0f;
            game.timeLeft = info.timeLeft;
            game.state = info.running ? SkillsGame.State.Running : SkillsGame.State.Countdown;
            Sandbox.DriveEnabled = info.running && !game.InAutonomous;
            ToHost(Msg.RejoinReady, BitConverter.GetBytes(me), true);
        }

        /// <summary>The host: the player is set up again: their robot back to them, everyone's owners, where everything is.</summary>
        private void HostRejoined(ulong sender) {
            var player = MatchSessions.PlayerOfClient(sender);
            var seat = plan.seats.FirstOrDefault(s => s.playerId == player);
            if (seat == null) return;
            departed.Remove(seat.robot);
            seatHeardAt[seat.robot] = Time.time;
            foreach (var k in keyed.Values.Where(k => k.robot == seat.robot)) SetOwner(k, seat.robot);
            using (var owners = new MemoryStream()) {
                foreach (var k in keyed.Values) {
                    owners.Write(BitConverter.GetBytes(k.key), 0, 4);
                    owners.Write(BitConverter.GetBytes(k.owner), 0, 4);
                }
                ToClients(Msg.Owner, owners.ToArray(), true);
            }
            var t = game.field.transform;
            var inverse = Quaternion.Inverse(t.rotation);
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream)) {
                w.Write(keyed.Count);
                foreach (var k in keyed.Values) {
                    w.Write(k.key);
                    WritePose(w, t.InverseTransformPoint(k.Position), inverse * k.Rotation);
                }
            }
            Send(sender, Msg.Sync, stream.ToArray(), true);
            Interface.Interface.Say(seat.name + " rejoined the match", false);
        }

        private void Update() {
            if (!running || game == null || !Sandbox.Running) return;
            var net = NetworkManager.Singleton;
            if (net == null || !net.IsListening) {   // the connection went (left, or the host closed it)
                Abort(game.state == SkillsGame.State.Results ? null : "The connection to the match was lost.", false);
                return;
            }
            UpdatePing();
            if (finishing) {
                if (IsHost) HostEnding();
                return;
            }
            if (disabled.Contains(me)) Sandbox.DriveEnabled = false;   // (switched off for the rest of the match)
            Claims();
            RecordRobots();
            DrawHold();
            if (Time.unscaledTime >= nextState) {
                nextState = Time.unscaledTime + StateEvery;
                SendState();
            }
            if (IsHost) {
                if (Time.time - startedAt > 3f) TakeOverDeparted();
                CheckHolding();
                HostEnding();
            }
        }

        // ------------------------------------------------------------------ ending

        private void OnExited() {
            if (leaving) return;
            Debug.Log("[Match] left the match" + (game != null ? " (" + game.state + ")" : ""));
            leaving = true;
            // The host leaving ends it for everyone; anyone else leaves their robot on the field, switched off.
            if (IsHost && game != null && game.state != SkillsGame.State.Results) ToClients(Msg.Abort, Bytes("The host ended the match."), true);
            Cleanup();
            if (IsHost) MatchSessions.Lock(false);
            if (MatchSessions.Current != null) OnlineWindow.Open();
        }

        /// <summary>Ends the match on this computer (said why, when there's a reason).</summary>
        private void Abort(string reason, bool tellOthers) {
            Debug.Log("[Match] ended: " + (string.IsNullOrEmpty(reason) ? "(no reason)" : reason));
            if (tellOthers && IsHost) ToClients(Msg.Abort, Bytes(reason ?? ""), true);
            if (!string.IsNullOrEmpty(reason)) Interface.Interface.Say(reason, false);
            leaving = true;
            if (game != null && SkillsGame.Current == game) game.Exit();
            Cleanup();
            if (MatchSessions.Current != null) OnlineWindow.Open();
        }

        private void Cleanup() {
            Highlights.Clear("holding");
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            if (ping != null) Destroy(ping.gameObject);
            if (TestDrive != 0f) Sandbox.TestSticks = null;
            Sandbox.RobotOf = null;
            Sandbox.InputOf = null;
            Sandbox.LocalRobot = 0;
            Sandbox.Puppet = false;
            foreach (var part in remoteParts)
                if (part != null) {
                    part.SetActive(false);
                    Destroy(part);
                }
            remoteParts.Clear();
            foreach (var tag in myTags)
                if (tag != null) DestroyImmediate(tag);   // now: a match starting this frame tags the same parts again
            myTags.Clear();
            running = false;
            if (Current == this) Current = null;
            Destroy(gameObject);
        }
    }
}
