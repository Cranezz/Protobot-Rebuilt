using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Protobot.GraphicsQuality;
using UnityEngine;

namespace Protobot.Interface {
    // V2: measures how fast the app draws. The camera circles the build (and the field, when it's there) once per
    // test while each preset is drawn at each resolution; the 3D view's resolution is changed through the render
    // scale (GraphicsSettingsManager.RenderScaleMultiplier), so 720p, 1080p and 1440p are tested in one window.
    // Frame times come from Unity's FrameTimingManager (CPU main thread, render thread, GPU) where the graphics
    // driver reports them, else from the frame's own time.
    //
    // In the app: Graphics settings > Benchmark. From the command line (an old PC, a school workstation):
    //   Protobot.exe -benchmark [-benchmark-build "<file>.pbb"] [-benchmark-heights 720,1080] [-benchmark-seconds 5]
    //                [-benchmark-out "<file>.csv"]
    // writes the results (default: Documents next to the app's settings) and quits.
    public class GraphicsBenchmark : MonoBehaviour {
        public struct Result {
            public GraphicsPreset preset;
            public int width, height;
            public float averageFps, lowFps;       // lowFps: the slowest 1% of frames
            public float frameMs, cpuMs, renderThreadMs, gpuMs;
            public bool hasGpuTime;
            public int frames;
        }

        public class Options {
            public List<GraphicsPreset> presets = new() {
                GraphicsPreset.VeryLow, GraphicsPreset.Low, GraphicsPreset.Medium,
                GraphicsPreset.High, GraphicsPreset.Ultra, GraphicsPreset.Extreme,
            };
            /// <summary>Heights of the 3D view to test (16:9 widths); empty = the window as it is.</summary>
            public List<int> heights = new();
            public float seconds = 5f;
            public float warmup = 1f;
        }

        public static bool Running => current != null;
        /// <summary>The last finished run (kept for the graphics window).</summary>
        public static List<Result> Last { get; private set; }
        public static event Action<string, float> Progress;  // message, 0..1
        public static event Action<List<Result>> Finished;

        private static GraphicsBenchmark current;
        private Options options;
        private Action<List<Result>> done;
        private bool cancel;

        public static void Run(Options options = null, Action<List<Result>> done = null) {
            if (Running) return;
            var go = new GameObject("Graphics Benchmark");
            DontDestroyOnLoad(go);
            current = go.AddComponent<GraphicsBenchmark>();
            current.options = options ?? new Options();
            current.done = done;
        }

        public static void Cancel() {
            if (current != null) current.cancel = true;
        }

        private void Start() => StartCoroutine(Measure());

        private void OnDestroy() {
            if (current == this) current = null;
        }

        private IEnumerator Measure() {
            var saved = GraphicsSettingsManager.Current?.Clone();
            if (saved == null) {
                Destroy(gameObject);
                yield break;
            }
            var pivot = PivotCamera.Main;
            var pivotPose = pivot != null ? (pivot.transform.position, pivot.transform.eulerAngles, pivot.camera.transform.localPosition.z) : default;
            var (center, radius) = Framing();
            var heights = options.heights.Count > 0 ? options.heights : new List<int> { 0 };
            var results = new List<Result>();
            var total = options.presets.Count * heights.Count;
            var index = 0;
            var timings = new FrameTiming[1];

            foreach (var height in heights)
                foreach (var preset in options.presets) {
                    if (cancel) break;
                    var settings = saved.Clone();
                    settings.SetQualityFromPreset(preset);
                    settings.vSync = false;
                    settings.frameRateLimit = 0;
                    GraphicsSettingsManager.Apply(settings, save: false);
                    var viewHeight = Camera.main != null ? Camera.main.pixelHeight : Screen.height;
                    GraphicsSettingsManager.RenderScaleMultiplier = height > 0 ? height / (float)Mathf.Max(1, Screen.height) : 1f;
                    var shownHeight = height > 0 ? height : viewHeight;
                    var shownWidth = height > 0 ? Mathf.RoundToInt(height * 16f / 9f) : (Camera.main != null ? Camera.main.pixelWidth : Screen.width);
                    Progress?.Invoke($"{PresetName(preset)} at {shownWidth} x {shownHeight}", index / (float)total);

                    var frameTimes = new List<float>();
                    float cpu = 0, render = 0, gpu = 0;
                    var gpuFrames = 0;
                    var elapsed = -options.warmup - (index == 0 ? 2f : 0f);  // the first test also waits for shaders to compile
                    while (elapsed < options.seconds && !cancel) {
                        var dt = Time.unscaledDeltaTime;
                        elapsed += dt;
                        var turn = Mathf.Clamp01(elapsed / options.seconds);
                        if (pivot != null)
                            pivot.SetTransform(center, new Vector3(24f + 8f * Mathf.Sin(turn * Mathf.PI * 2f), 30f + 360f * turn, 0f), -radius);
                        if (elapsed > 0f) {
                            frameTimes.Add(dt * 1000f);
                            FrameTimingManager.CaptureFrameTimings();
                            if (FrameTimingManager.GetLatestTimings(1, timings) > 0) {
                                cpu += (float)timings[0].cpuMainThreadFrameTime;
                                render += (float)timings[0].cpuRenderThreadFrameTime;
                                if (timings[0].gpuFrameTime > 0 && timings[0].gpuFrameTime < 1000) {  // the driver sometimes reports nonsense
                                    gpu += (float)timings[0].gpuFrameTime;
                                    gpuFrames++;
                                }
                            }
                            Progress?.Invoke($"{PresetName(preset)} at {shownWidth} x {shownHeight}", (index + turn) / total);
                        }
                        yield return null;
                    }
                    if (frameTimes.Count > 0) {
                        var sorted = frameTimes.OrderBy(t => t).ToList();
                        var worst = sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Count * 0.99f) - 1, 0, sorted.Count - 1)];
                        var sum = frameTimes.Sum();
                        results.Add(new Result {
                            preset = preset, width = shownWidth, height = shownHeight,
                            averageFps = frameTimes.Count * 1000f / sum, lowFps = 1000f / worst,
                            frameMs = sum / frameTimes.Count, cpuMs = cpu / frameTimes.Count,
                            renderThreadMs = render / frameTimes.Count,
                            gpuMs = gpuFrames > 0 ? gpu / gpuFrames : 0f, hasGpuTime = gpuFrames > frameTimes.Count / 2,
                            frames = frameTimes.Count,
                        });
                    }
                    index++;
                }

            GraphicsSettingsManager.RenderScaleMultiplier = 1f;
            GraphicsSettingsManager.Apply(saved, save: false);
            if (pivot != null)
                pivot.SetTransform(pivotPose.Item1, pivotPose.Item2, pivotPose.Item3);
            Last = results;
            Progress?.Invoke(cancel ? "Stopped" : "Done", 1f);
            done?.Invoke(results);
            Finished?.Invoke(results);
            Destroy(gameObject);
        }

        /// <summary>The middle of what's drawn (parts and the field, not the grid or the interface) and a distance that shows it all.</summary>
        private static (Vector3, float) Framing() {
            var bounds = new Bounds();
            var any = false;
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) {
                var layer = renderer.gameObject.layer;
                if (!renderer.enabled || layer == 5 || layer == 8 || layer == 9 || layer == 10 || layer == 12 || layer == 15)
                    continue;
                if (renderer.bounds.size.magnitude > 2000f) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!any) return (Vector3.zero, 60f);
            var camera = Camera.main;
            var fov = camera != null && !camera.orthographic ? camera.fieldOfView : 60f;
            var distance = bounds.extents.magnitude / Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad) * 0.85f;
            return (bounds.center, Mathf.Max(distance, 12f));
        }

        public static string PresetName(GraphicsPreset preset) => preset switch {
            GraphicsPreset.VeryLow => "Very Low",
            _ => preset.ToString(),
        };

        public static string Csv(List<Result> results) {
            var csv = new StringBuilder();
            csv.AppendLine("gpu,cpu,preset,width,height,avg_fps,low1_fps,frame_ms,cpu_main_ms,cpu_render_ms,gpu_ms,frames");
            var inv = CultureInfo.InvariantCulture;
            var gpuName = SystemInfo.graphicsDeviceName.Replace(",", " ");
            var cpuName = SystemInfo.processorType.Replace(",", " ").Trim();
            foreach (var r in results)
                csv.AppendLine(string.Join(",", gpuName, cpuName, PresetName(r.preset), r.width, r.height,
                    r.averageFps.ToString("0.0", inv), r.lowFps.ToString("0.0", inv), r.frameMs.ToString("0.00", inv),
                    r.cpuMs.ToString("0.00", inv), r.renderThreadMs.ToString("0.00", inv),
                    r.hasGpuTime ? r.gpuMs.ToString("0.00", inv) : "", r.frames));
            return csv.ToString();
        }

        // ------------------------------------------------------------------ command line

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void FromCommandLine() {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("-benchmark")) return;
            var runner = new GameObject("Benchmark Command Line").AddComponent<CommandLineRun>();
            runner.args = args;
            DontDestroyOnLoad(runner.gameObject);
        }

        private class CommandLineRun : MonoBehaviour {
            public string[] args;

            private string Arg(string name) {
                var i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }

            private IEnumerator Start() {
                yield return new WaitForSecondsRealtime(3f);  // the app finishes starting up
                var build = Arg("-benchmark-build");
                if (!string.IsNullOrEmpty(build) && File.Exists(build)) {
                    var manager = FindAnyObjectByType<Builds.BuildsManager>();
                    if (manager != null) manager.AttemptLoad(Builds.BuildFile.Read(build), build);
                    else Builds.SceneBuild.GenerateBuild(Builds.BuildFile.Read(build));
                    yield return new WaitForSecondsRealtime(2f);
                }
                var options = new Options();
                if (Arg("-benchmark-heights") is string heights)
                    options.heights = heights.Split(',').Select(h => int.TryParse(h, out var v) ? v : 0).Where(v => v > 0).ToList();
                if (float.TryParse(Arg("-benchmark-seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                    options.seconds = Mathf.Max(1f, seconds);
                var finished = false;
                List<Result> results = null;
                Run(options, r => { results = r; finished = true; });
                while (!finished) yield return null;
                var path = Arg("-benchmark-out")
                           ?? Path.Combine(Application.persistentDataPath, "Benchmark " + DateTime.Now.ToString("yyyy-MM-dd HH-mm") + ".csv");
                File.WriteAllText(path, Csv(results));
                Debug.Log("[Benchmark] Wrote " + path);
                var builds = FindAnyObjectByType<Builds.BuildsManager>();
                if (builds != null) builds.Quit();  // nothing to save: skips the unsaved-changes question
                else Application.Quit();
            }
        }
    }
}
