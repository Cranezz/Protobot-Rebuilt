using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Protobot.Motion;
using UnityEngine;

namespace Protobot.Game {
    /// <summary>
    /// V2: measures how smooth the game runs, from the command line (built app):
    ///   Protobot.exe -benchmark-game [-benchmark-build "robot.pbb"] [-benchmark-field full|light] [-benchmark-seconds 20]
    ///                [-benchmark-out "result.txt"]
    /// Loads the build, opens Robot Skills on the field, records the setup screen for a few seconds, then a run with the
    /// robot driving (forward, turning, backing up) and writes frame times (average FPS, 1% low, worst frame, frames
    /// over 10 and 16.7 ms, CPU main thread and GPU from FrameTimingManager) for each part. The settings are the
    /// player's own (no preset is forced), with no frame limit or vSync, so the numbers are what the PC can do.
    /// </summary>
    public class GameBenchmark : MonoBehaviour {
        private string[] args;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void FromCommandLine() {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("-benchmark-game")) return;
            var runner = new GameObject("Game Benchmark").AddComponent<GameBenchmark>();
            runner.args = args;
            DontDestroyOnLoad(runner.gameObject);
        }

        private string Arg(string name) {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private class Part {
            public string name;
            public readonly List<float> frames = new();
            public readonly List<float> cpu = new(), gpu = new();
        }

        private IEnumerator Start() {
            yield return new WaitForSecondsRealtime(4f);   // start-up, the intro
            var launch = GameObject.Find("Launch Canvas");
            if (launch != null)
                foreach (var b in launch.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                    if (b.name == "Close Button" && b.transform.parent.name == "Splash Screen") { b.onClick.Invoke(); break; }
            var build = Arg("-benchmark-build");
            if (!string.IsNullOrEmpty(build) && File.Exists(build)) {
                PartsManager.DestroyLoadedObjects();
                Builds.SceneBuild.GenerateBuild(Builds.BuildFile.Read(build));
                yield return new WaitForSecondsRealtime(4f);   // the screws connect
            }
            var seconds = float.TryParse(Arg("-benchmark-seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? Mathf.Max(5f, s) : 20f;
            var light = Arg("-benchmark-field") == "light";

            // No frame limit: what the PC can do.
            var settings = GraphicsQuality.GraphicsSettingsManager.Current?.Clone();
            if (settings != null) {
                settings.vSync = false;
                settings.frameRateLimit = 0;
                GraphicsQuality.GraphicsSettingsManager.Apply(settings, save: false);
            }
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            var definition = Resources.LoadAll<GameDefinition>("Games").FirstOrDefault();
            SkillsGame.Open(definition, light);
            var game = SkillsGame.Current;
            var parts = new List<Part>();
            if (game == null) {
                Finish(parts, "the game couldn't open (no robot?)");
                yield break;
            }
            yield return new WaitForSecondsRealtime(3f);   // shaders, the first frames
            var setup = new Part { name = "Setup (field, robot, setup panel)" };
            parts.Add(setup);
            yield return Record(setup, 5f, null);

            game.StartRun();
            yield return new WaitForSecondsRealtime(3.5f);   // the countdown
            var run = new Part { name = "Run (driving, " + (light ? "light" : "full") + " field)" };
            parts.Add(run);
            var t0 = Time.realtimeSinceStartup;
            Sandbox.TestSticks = () => {
                // Forward, then a long turn, then back up: the camera follows and pieces get pushed.
                var t = Time.realtimeSinceStartup - t0;
                var phase = t % 9f;
                if (phase < 3f) return (new Vector2(0f, 0.9f), new Vector2(0f, 0.9f));
                if (phase < 6f) return (new Vector2(0f, 0.6f), new Vector2(0.6f, 0f));
                return (new Vector2(0f, -0.7f), new Vector2(-0.3f, -0.7f));
            };
            yield return Record(run, seconds, null);
            Sandbox.TestSticks = null;
            game.EndRun();
            Finish(parts, null);
        }

        private static IEnumerator Record(Part part, float seconds, Action each) {
            var timings = new FrameTiming[1];
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) {
                yield return null;
                each?.Invoke();
                part.frames.Add(Time.unscaledDeltaTime * 1000f);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0) {
                    if (timings[0].cpuMainThreadFrameTime > 0) part.cpu.Add((float)timings[0].cpuMainThreadFrameTime);
                    if (timings[0].gpuFrameTime > 0) part.gpu.Add((float)timings[0].gpuFrameTime);
                }
            }
        }

        private void Finish(List<Part> parts, string problem) {
            var sb = new StringBuilder();
            sb.AppendLine("Protobot game benchmark · " + Builds.BuildInfo.Full + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine(SystemInfo.graphicsDeviceName + " · " + SystemInfo.processorType + " · " + Screen.width + " x " + Screen.height
                          + " · " + SystemInfo.graphicsDeviceType);
            if (problem != null) sb.AppendLine("Problem: " + problem);
            foreach (var p in parts) {
                if (p.frames.Count == 0) continue;
                var sorted = p.frames.OrderBy(f => f).ToList();
                var avg = p.frames.Average();
                var worst1 = sorted.Skip((int)(sorted.Count * 0.99f)).DefaultIfEmpty(sorted.Last()).Average();
                sb.AppendLine();
                sb.AppendLine(p.name + ": " + p.frames.Count + " frames");
                sb.AppendLine($"  average {1000f / avg:0} FPS ({avg:0.00} ms), 1% low {1000f / worst1:0} FPS ({worst1:0.0} ms), worst {sorted.Last():0.0} ms");
                sb.AppendLine($"  frames over 10 ms: {p.frames.Count(f => f > 10f)}, over 16.7 ms: {p.frames.Count(f => f > 16.7f)}, over 33 ms: {p.frames.Count(f => f > 33f)}");
                if (p.cpu.Count > 0) sb.AppendLine($"  CPU main thread {p.cpu.Average():0.00} ms (95%: {Percentile(p.cpu, 0.95f):0.00})");
                if (p.gpu.Count > 0) sb.AppendLine($"  GPU {p.gpu.Average():0.00} ms (95%: {Percentile(p.gpu, 0.95f):0.00})");
            }
            var path = Arg("-benchmark-out") ?? Path.Combine(Application.persistentDataPath, "Game benchmark " + DateTime.Now.ToString("yyyy-MM-dd HH-mm") + ".txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[Benchmark] Wrote " + path + "\n" + sb);
            var builds = FindAnyObjectByType<Builds.BuildsManager>();
            if (builds != null) builds.Quit();
            else Application.Quit();
        }

        private static float Percentile(List<float> values, float p) {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[Mathf.Clamp((int)(sorted.Count * p), 0, sorted.Count - 1)];
        }
    }
}
