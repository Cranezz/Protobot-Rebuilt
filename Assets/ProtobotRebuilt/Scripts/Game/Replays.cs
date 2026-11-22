using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Protobot.Builds;
using Protobot.Custom;
using Protobot.Motion;
using UnityEngine;

namespace Protobot.Game {
    /// <summary>
    /// V2: a recorded run (Robot Skills or match practice). Every simulated body (the robot's bodies and the game pieces)
    /// is sampled 10 times a second in the field's frame, so a replay is small and plays back on any computer: the file
    /// carries the robot's parts (as a build file does, with its custom parts) and, for each body, what it draws: a part's
    /// renderer (part index + child path) or a field mesh by name. Files are gzipped JSON (.pbr) in Replays/.
    /// </summary>
    [Serializable]
    public class ReplayFile {
        public int version = 1;
        public string game, mode, robot, player, date, app;
        public int score;
        public float seconds, rate = Replays.Rate;
        public bool red = true;
        public float depth, width, height;
        public ReplayItem[] items = new ReplayItem[0];
        public BuildData build;
        public CustomPartData[] customParts = new CustomPartData[0];
        public ReplayObject[] objects = new ReplayObject[0];
        public int frames;
        public string data;   // base64: the samples (Replays.Encode)

        public string Title => (string.IsNullOrEmpty(robot) ? "Robot" : robot) + " · " + score + " points";
    }

    [Serializable]
    public class ReplayItem {
        public string kind, where;
        public int points;
    }

    /// <summary>A body: the frame it appears in, whether it's the robot's, and what it draws (in its own frame).</summary>
    [Serializable]
    public class ReplayObject {
        public int spawn;
        public bool robot;
        public ReplayProxy[] proxies = new ReplayProxy[0];
    }

    [Serializable]
    public class ReplayProxy {
        public int part = -1;          // index into build.parts (-1: a field mesh)
        public string path = "";       // child indexes from the part's root to its renderer ("0/2")
        public string mesh = "";       // its mesh's name (field pieces, or when the path isn't there)
        public string[] materials = new string[0];
        public Vector3 p, s = Vector3.one;
        public Quaternion r = Quaternion.identity;
    }

    public static class Replays {
        public const float Rate = 10f;
        public const float PositionScale = 50f;   // 1/50" steps (int16: ±655")
        public const string Extension = "pbr";
        public static string Folder => Path.Combine(Application.persistentDataPath, "Replays");

        /// <summary>The last run recorded in this session (the results' Watch replay).</summary>
        public static ReplayFile Last { get; set; }

        // ------------------------------------------------------------------ files

        public static byte[] ToBytes(ReplayFile replay) {
            var json = JsonUtility.ToJson(replay);
            using var stream = new MemoryStream();
            using (var gzip = new GZipStream(stream, System.IO.Compression.CompressionLevel.Optimal, true)) {
                var bytes = Encoding.UTF8.GetBytes(json);
                gzip.Write(bytes, 0, bytes.Length);
            }
            return stream.ToArray();
        }

        public static ReplayFile FromBytes(byte[] bytes) {
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            var replay = JsonUtility.FromJson<ReplayFile>(reader.ReadToEnd());
            if (replay == null || replay.build == null || replay.objects == null) throw new Exception("it isn't a Protobot replay");
            return replay;
        }

        /// <summary>Saves a replay in Replays/ (the newest 30 are kept); its path.</summary>
        public static string Save(ReplayFile replay) {
            Directory.CreateDirectory(Folder);
            var name = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " " + Clean(replay.robot) + " " + replay.score + "." + Extension;
            var path = Path.Combine(Folder, name);
            File.WriteAllBytes(path, ToBytes(replay));
            foreach (var old in new DirectoryInfo(Folder).GetFiles("*." + Extension).OrderByDescending(f => f.CreationTimeUtc).Skip(30))
                try { old.Delete(); } catch { }
            return path;
        }

        private static string Clean(string s) {
            if (string.IsNullOrEmpty(s)) return "Robot";
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }

        /// <summary>File > Open replay…: a .pbr file (the Replays folder first).</summary>
        public static void OpenFile() => OpenFile(null);

        /// <summary>V2: `chosen` runs once a file is picked (the home menu closes then, not on Cancel).</summary>
        public static void OpenFile(Action chosen) {
            Directory.CreateDirectory(Folder);
            var paths = SFB.StandaloneFileBrowser.OpenFilePanel("Open a replay", Folder, Extension, false);
            InputEvents.TextInput.AfterFileWindow();
            if (paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
            chosen?.Invoke();
            OpenPath(paths[0]);
        }

        /// <summary>V2: opens a replay file (one someone sent: double-clicked, or File > Open replay…).</summary>
        public static void OpenPath(string path) {
            ReplayFile replay;
            try { replay = FromBytes(File.ReadAllBytes(path)); }
            catch (Exception e) {
                Interface.Interface.Say("That replay couldn't be read: " + e.Message, false);
                return;
            }
            ReplayViewer.Open(replay);
        }

        // ------------------------------------------------------------------ samples

        /// <summary>Packs the samples: per frame, per body that exists: 0 = as before, 1 = a new pose (3 x int16
        /// position, 4 x int16 rotation), 2 = gone.</summary>
        public static string Encode(List<List<(bool gone, Vector3 p, Quaternion r)?>> frames, int objects) {
            using var stream = new MemoryStream();
            using (var gzip = new GZipStream(stream, System.IO.Compression.CompressionLevel.Optimal, true))
            using (var w = new BinaryWriter(gzip)) {
                var last = new short[objects, 7];
                var known = new bool[objects];
                foreach (var frame in frames) {
                    for (var i = 0; i < objects; i++) {
                        var sample = i < frame.Count ? frame[i] : null;
                        if (sample == null) {
                            if (known[i]) w.Write((byte)0);   // (a body that existed and wasn't sampled: as before)
                            continue;
                        }
                        if (sample.Value.gone) {
                            w.Write((byte)2);
                            known[i] = false;
                            continue;
                        }
                        var q = Quantize(sample.Value.p, sample.Value.r);
                        var same = known[i];
                        for (var k = 0; k < 7 && same; k++) same = last[i, k] == q[k];
                        if (same) {
                            w.Write((byte)0);
                            continue;
                        }
                        w.Write((byte)1);
                        for (var k = 0; k < 7; k++) {
                            w.Write(q[k]);
                            last[i, k] = q[k];
                        }
                        known[i] = true;
                    }
                }
            }
            return Convert.ToBase64String(stream.ToArray());
        }

        private static short[] Quantize(Vector3 p, Quaternion r) {
            r = r.normalized;
            if (r.w < 0f) r = new Quaternion(-r.x, -r.y, -r.z, -r.w);
            static short S(float v, float scale) => (short)Mathf.Clamp(Mathf.RoundToInt(v * scale), short.MinValue, short.MaxValue);
            return new[] { S(p.x, PositionScale), S(p.y, PositionScale), S(p.z, PositionScale), S(r.x, 32767f), S(r.y, 32767f), S(r.z, 32767f), S(r.w, 32767f) };
        }

        /// <summary>Unpacks the samples: [frame][object] = pose in the field's frame, or null where it doesn't exist.</summary>
        public static (Vector3 p, Quaternion r)?[][] Decode(ReplayFile replay) {
            var n = replay.objects.Length;
            var result = new (Vector3, Quaternion)?[replay.frames][];
            using var input = new MemoryStream(Convert.FromBase64String(replay.data ?? ""));
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var r = new BinaryReader(gzip);
            var current = new (Vector3, Quaternion)?[n];
            var exists = new bool[n];
            for (var f = 0; f < replay.frames; f++) {
                for (var i = 0; i < n; i++) {
                    // As Encode wrote it: a flag for every body known so far, and for each one appearing in this frame.
                    if (!exists[i] && replay.objects[i].spawn != f) continue;
                    byte flag;
                    try { flag = r.ReadByte(); }
                    catch (EndOfStreamException) { flag = 0; }
                    if (flag == 1) {
                        var q = new short[7];
                        for (var k = 0; k < 7; k++) q[k] = r.ReadInt16();
                        current[i] = (new Vector3(q[0], q[1], q[2]) / PositionScale, new Quaternion(q[3] / 32767f, q[4] / 32767f, q[5] / 32767f, q[6] / 32767f).normalized);
                        exists[i] = true;
                    }
                    else if (flag == 2) {
                        current[i] = null;
                        exists[i] = false;
                    }
                }
                result[f] = (( Vector3, Quaternion)?[])current.Clone();
            }
            return result;
        }
    }

    /// <summary>
    /// V2: records the run that's on (SkillsGame starts it with the simulation, Finish takes the file). Bodies are
    /// found as they appear (Match Loads spawn during the run).
    /// </summary>
    public class ReplayRecorder : MonoBehaviour {
        private SkillsGame game;
        private readonly Dictionary<Rigidbody, int> index = new();
        private readonly List<Rigidbody> bodies = new();
        private readonly List<ReplayObject> objects = new();
        private readonly List<List<(bool gone, Vector3 p, Quaternion r)?>> frames = new();
        private readonly List<GameObject> parts = new();
        private BuildData build;
        private CustomPartData[] custom;
        private float next;
        private bool stopped;

        public static ReplayRecorder Begin(SkillsGame game) {
            var recorder = game.gameObject.GetComponent<ReplayRecorder>();
            if (recorder != null) Destroy(recorder);
            recorder = game.gameObject.AddComponent<ReplayRecorder>();
            recorder.game = game;
            recorder.Snapshot();
            recorder.Sample();
            return recorder;
        }

        // The robot's parts, as a build file has them (the replay remakes them to draw its bodies).
        private void Snapshot() {
            parts.AddRange(PartsManager.FindLoadedObjects().Where(p => p != null && p.activeInHierarchy && !p.IsDeleted()));
            var data = new ObjectData[parts.Count];
            for (var i = 0; i < parts.Count; i++) {
                var t = parts[i].transform;
                var saved = t.GetComponent<SavedObject>();
                var renderer = t.GetComponent<Renderer>();
                var color = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.color : Color.white;
                data[i] = new ObjectData {
                    partId = saved != null ? saved.id : "Error", states = saved != null ? saved.state : null,
                    xPos = t.position.x, yPos = t.position.y, zPos = t.position.z,
                    xRot = t.eulerAngles.x, yRot = t.eulerAngles.y, zRot = t.eulerAngles.z,
                    rColor = color.r, gColor = color.g, bColor = color.b,
                    colors = renderer != null && renderer.sharedMaterials.Length > 1
                        ? string.Join(",", renderer.sharedMaterials.Select(m => m != null ? ColorUtility.ToHtmlStringRGB(m.color) : "FFFFFF")) : null,
                };
            }
            build = new BuildData { version = Application.version, parts = data, drive = Controls.DriveToken,
                // V2 (Phase 9): a Programming Skills run keeps the program that drove it (moderators can read it).
                code = game.mode == SkillsGame.Mode.Programming ? RobotCode.CodeStore.Token : null };
            custom = data.Select(d => d.partId).Distinct().Select(CustomParts.Find).Where(c => c != null).ToArray();
        }

        private void Update() {
            if (stopped || game == null || !Sandbox.Running) return;
            if (Time.time < next) return;
            Sample();
        }

        private void Sample() {
            next = Time.time + 1f / Replays.Rate;
            var sandbox = Sandbox.Instance;
            if (sandbox == null || sandbox.Root == null || game.field == null) return;
            var field = game.field.transform;
            var fieldRotation = Quaternion.Inverse(field.rotation);
            var frame = new List<(bool, Vector3, Quaternion)?>(new (bool, Vector3, Quaternion)?[bodies.Count]);
            foreach (var rb in sandbox.Root.GetComponentsInChildren<Rigidbody>()) {
                if (rb.gameObject.layer != Sandbox.Layer && rb.gameObject.layer != Sandbox.ElementLayer) continue;
                if (!index.TryGetValue(rb, out var i)) {
                    i = bodies.Count;
                    index[rb] = i;
                    bodies.Add(rb);
                    objects.Add(Describe(rb, frames.Count));
                    frame.Add(null);
                }
                var t = rb.transform;
                frame[i] = (false, field.InverseTransformPoint(t.position), fieldRotation * t.rotation);
            }
            for (var i = 0; i < bodies.Count; i++)
                if (bodies[i] == null && frame[i] == null && (frames.Count == 0 || WasThere(i))) frame[i] = (true, Vector3.zero, Quaternion.identity);
            frames.Add(frame);
        }

        private bool WasThere(int i) {
            for (var f = frames.Count - 1; f >= 0; f--) {
                var s = i < frames[f].Count ? frames[f][i] : null;
                if (s != null) return !s.Value.Item1;
            }
            return false;
        }

        // What a body draws, in its own frame.
        private ReplayObject Describe(Rigidbody rb, int spawn) {
            var o = new ReplayObject { spawn = spawn, robot = rb.gameObject.layer == Sandbox.Layer };
            var proxies = new List<ReplayProxy>();
            var inverse = rb.transform.worldToLocalMatrix;
            foreach (var renderer in rb.GetComponentsInChildren<MeshRenderer>()) {
                if (!renderer.enabled || renderer.GetComponentInParent<Rigidbody>() != rb) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var local = inverse * renderer.transform.localToWorldMatrix;
                var proxy = new ReplayProxy {
                    mesh = filter.sharedMesh.name,
                    materials = renderer.sharedMaterials.Select(m => m != null ? m.name.Replace(" (Instance)", "") : "").ToArray(),
                    p = local.GetColumn(3), r = local.rotation, s = renderer.transform.lossyScale,
                };
                var source = Sandbox.SourceOf(renderer);
                if (source != null) {
                    var owner = source.GetComponentInParent<SavedObject>();
                    var at = owner != null ? parts.IndexOf(owner.gameObject) : -1;
                    if (at >= 0) {
                        proxy.part = at;
                        proxy.path = PathOf(owner.transform, source.transform);
                    }
                }
                proxies.Add(proxy);
            }
            o.proxies = proxies.ToArray();
            return o;
        }

        public static string PathOf(Transform root, Transform t) {
            var steps = new List<string>();
            while (t != null && t != root) {
                steps.Add(t.GetSiblingIndex().ToString());
                t = t.parent;
            }
            steps.Reverse();
            return string.Join("/", steps);
        }

        public static Transform Find(Transform root, string path) {
            if (string.IsNullOrEmpty(path)) return root;
            var t = root;
            foreach (var step in path.Split('/')) {
                if (!int.TryParse(step, out var i) || i < 0 || i >= t.childCount) return null;
                t = t.GetChild(i);
            }
            return t;
        }

        /// <summary>Stops recording and makes the file (the score from the finished run).</summary>
        public ReplayFile Finish(SkillsScore score) {
            if (!stopped) Sample();
            stopped = true;
            var replay = new ReplayFile {
                game = game.definition != null ? game.definition.title : "Override",
                mode = game.mode.ToString(),
                robot = game.buildName,
                player = Online.OnlineServices.Name,
                date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                app = Application.version,
                score = score.total,
                seconds = frames.Count / Replays.Rate,
                red = game.Red,
                depth = game.depth, width = game.width, height = game.height,
                items = score.items.Select(i => new ReplayItem { kind = i.kind, where = i.where, points = i.points }).ToArray(),
                build = build,
                customParts = custom,
                objects = objects.ToArray(),
                frames = frames.Count,
                data = Replays.Encode(frames, objects.Count),
            };
            Destroy(this);
            return replay;
        }
    }
}
