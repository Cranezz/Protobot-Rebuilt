using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Protobot.Builds;
using Protobot.Custom;
using Protobot.Interface;
using Protobot.Motion;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Game {
    /// <summary>
    /// V2: plays a replay (Replays): the field (the game's own when one is open, else a new one), the robot's parts made
    /// again from the file (away from the build, then copied as plain meshes onto its bodies) and the game pieces, moved
    /// through the samples (interpolated). A bar at the bottom: play/pause (Space), the timeline (drag; arrows step 5 s),
    /// speed, follow the robot or a free camera (F), Close (Esc). The open build is hidden while it plays and back after.
    /// </summary>
    public class ReplayViewer : MonoBehaviour {
        public static ReplayViewer Instance { get; private set; }
        public static bool Active => Instance != null;

        private ReplayFile replay;
        private (Vector3 p, Quaternion r)?[][] samples;
        private GameField field;
        private bool ownField, fromOnline;
        private SkillsGame game;
        private Transform root;
        private Transform[] objects;
        private int follow = -1;
        private Vector3 lastFollow;
        private readonly List<Renderer> hiddenBuild = new();
        private readonly List<Renderer> hiddenGrid = new();
        private Func<bool> savedBlocked;
        private Vector3 savedFocus, savedAngles;
        private float savedDistance;

        private float time, speed = 1f;
        private bool playing = true, following = true, ready;
        private RectTransform bar, track, fill, handle;
        private TextMeshProUGUI clock, playLabel, speedLabel, cameraLabel, title;

        private static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f };

        public static void Open(ReplayFile replay) {
            if (replay == null) return;
            if (Active) Instance.Close();
            var definition = GameDefinition.All().FirstOrDefault(d => d.title == replay.game) ?? GameDefinition.All().FirstOrDefault();
            if (definition == null) {
                Interface.Interface.Say("No game is installed to play that replay on", false);
                return;
            }
            if (SkillsGame.Active && SkillsGame.Current.online) {
                Interface.Interface.Say("Replays can't play during an online match", false);
                return;
            }
            var viewer = new GameObject("Replay Viewer").AddComponent<ReplayViewer>();
            Instance = viewer;
            viewer.replay = replay;
            viewer.StartCoroutine(viewer.Load(definition));
        }

        private IEnumerator Load(GameDefinition definition) {
            fromOnline = Online.OnlineWindow.IsOpen;
            if (fromOnline) Online.OnlineWindow.Close();
            Dialog.CloseAll();
            Popup.Close();
            if (Placement.IsPlacing) Placement.Instance.StopPlacing();
            if (PivotCamera.Main != null) {
                savedFocus = PivotCamera.Main.transform.position;
                savedAngles = PivotCamera.Main.transform.eulerAngles;
                savedDistance = PivotCamera.Main.camera.transform.localPosition.z;
            }
            game = SkillsGame.Current;
            if (game != null) {
                // On the game's own field: its run (or setup) steps aside while the replay plays.
                if (game.state != SkillsGame.State.Setup) game.BackToSetup();
                GameUi.CloseAll();
                field = game.field;
                if (game.tool != null) game.tool.gameObject.SetActive(false);
            }
            else {
                if (Sandbox.Running) Sandbox.Stop();
                var light = GraphicsQuality.GraphicsSettingsManager.Current != null && GraphicsQuality.GraphicsSettingsManager.Current.preset <= GraphicsQuality.GraphicsPreset.Low;
                try { field = GameField.Create(definition, light, replay.mode != "Match"); }
                catch (Exception e) {
                    Debug.LogException(e);
                    Interface.Interface.Say("The field couldn't be set up: " + e.Message, false);
                    Instance = null;
                    Destroy(gameObject);
                    yield break;
                }
                ownField = true;
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (r.gameObject.layer == 9 && r.enabled && (r.name.Contains("Perspective Plane") || r.name.Contains("Orthographic Plane") || r.name == "AxisPlane")) {
                        r.enabled = false;
                        hiddenGrid.Add(r);
                    }
                savedBlocked = SelectionSystem.SelectionManager.Blocked;
                SelectionSystem.SelectionManager.Blocked = () => true;
            }
            Sandbox.SetBuildInterface(false);
            field.HidePieces();
            // The open build goes out of sight (the replay's robot is made from the file).
            foreach (var saved in FindObjectsByType<SavedObject>(FindObjectsSortMode.None))
                foreach (var r in saved.GetComponentsInChildren<Renderer>())
                    if (r.enabled) {
                        r.enabled = false;
                        hiddenBuild.Add(r);
                    }
            BuildBar();
            title.text = "Loading the replay…";

            // The robot's parts, made far below everything, then copied as meshes.
            if (replay.customParts != null)
                foreach (var c in replay.customParts)
                    if (c != null && !string.IsNullOrEmpty(c.key)) CustomParts.AddFromBuild(c);
            var made = new GameObject[replay.build.parts.Length];
            var away = new Vector3(0f, -20000f, 0f);
            foreach (var connecting in new[] { false, true })
                for (var i = 0; i < made.Length; i++) {
                    var source = replay.build.parts[i];
                    var type = PartsManager.GetPartType(source.partId);
                    if (type == null || type.connectingPart != connecting) continue;
                    var data = JsonUtility.FromJson<ObjectData>(JsonUtility.ToJson(source));
                    data.xPos += away.x; data.yPos += away.y; data.zPos += away.z;
                    try { made[i] = SceneBuild.GeneratePart(data, replay.build); }
                    catch (Exception e) { Debug.LogWarning("V2 replay: couldn't make " + source.partId + ": " + e.Message); }
                }
            yield return null;   // their Start runs (chains lay their links, cylinders their rods)
            yield return null;

            var meshes = new Dictionary<string, Mesh>();
            var materials = new Dictionary<string, Material>();
            field.Library(meshes, materials);
            foreach (var part in made)
                if (part != null)
                    foreach (var r in part.GetComponentsInChildren<MeshRenderer>(true)) {
                        var f = r.GetComponent<MeshFilter>();
                        if (f != null && f.sharedMesh != null) meshes.TryAdd(f.sharedMesh.name, f.sharedMesh);
                        foreach (var m in r.sharedMaterials) if (m != null) materials.TryAdd(m.name.Replace(" (Instance)", ""), m);
                    }

            root = new GameObject("Replay").transform;
            root.SetParent(field.transform, false);
            objects = new Transform[replay.objects.Length];
            var most = 0;
            for (var i = 0; i < replay.objects.Length; i++) {
                var o = replay.objects[i];
                var holder = new GameObject(o.robot ? "Robot body" : "Piece").transform;
                holder.SetParent(root, false);
                foreach (var proxy in o.proxies) {
                    Mesh mesh = null;
                    Material[] mats = null;
                    if (proxy.part >= 0 && proxy.part < made.Length && made[proxy.part] != null) {
                        var t = ReplayRecorder.Find(made[proxy.part].transform, proxy.path);
                        var r = t != null ? t.GetComponent<MeshRenderer>() : null;
                        var f = t != null ? t.GetComponent<MeshFilter>() : null;
                        if (r != null && f != null && f.sharedMesh != null && (string.IsNullOrEmpty(proxy.mesh) || f.sharedMesh.name == proxy.mesh || o.robot)) {
                            mesh = f.sharedMesh;
                            mats = r.sharedMaterials;
                        }
                    }
                    if (mesh == null && !string.IsNullOrEmpty(proxy.mesh)) meshes.TryGetValue(proxy.mesh, out mesh);
                    if (mesh == null) continue;
                    mats ??= proxy.materials.Select(n => materials.TryGetValue(n, out var m) ? m : null).Where(m => m != null).ToArray();
                    var copy = new GameObject("Mesh");
                    copy.transform.SetParent(holder, false);
                    copy.transform.localPosition = proxy.p;
                    copy.transform.localRotation = proxy.r;
                    copy.transform.localScale = proxy.s;
                    copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                    copy.AddComponent<MeshRenderer>().sharedMaterials = mats;
                }
                objects[i] = holder;
                holder.gameObject.SetActive(false);
                if (o.robot && o.proxies.Length > most) {
                    most = o.proxies.Length;
                    follow = i;
                }
            }
            foreach (var part in made)
                if (part != null) {
                    part.SetActive(false);
                    Destroy(part);
                }

            samples = Replays.Decode(replay);
            title.text = replay.Title + (string.IsNullOrEmpty(replay.player) ? "" : "  ·  " + replay.player) + (string.IsNullOrEmpty(replay.date) ? "" : "  ·  " + replay.date);
            ready = true;
            Show(0f);
            if (follow >= 0 && PivotCamera.Main != null) {
                lastFollow = objects[follow].position;
                PivotCamera.Main.SetTransform(lastFollow, new Vector3(35f, field.transform.eulerAngles.y + 200f, 0f), -110f);
            }
            Builds.ActionLog.Write("game", "replay " + replay.Title);
        }

        private float Length => samples == null || samples.Length == 0 ? 0f : (samples.Length - 1) / Mathf.Max(1f, replay.rate);

        private void Show(float t) {
            if (samples == null || samples.Length == 0) return;
            var at = Mathf.Clamp(t * replay.rate, 0f, samples.Length - 1);
            var f0 = Mathf.FloorToInt(at);
            var f1 = Mathf.Min(f0 + 1, samples.Length - 1);
            var a = at - f0;
            for (var i = 0; i < objects.Length; i++) {
                var o = objects[i];
                if (o == null) continue;
                var s0 = i < samples[f0].Length ? samples[f0][i] : null;
                if (s0 == null) {
                    if (o.gameObject.activeSelf) o.gameObject.SetActive(false);
                    continue;
                }
                var s1 = i < samples[f1].Length ? samples[f1][i] : null;
                var p = s1 != null ? Vector3.Lerp(s0.Value.p, s1.Value.p, a) : s0.Value.p;
                var r = s1 != null ? Quaternion.Slerp(s0.Value.r, s1.Value.r, a) : s0.Value.r;
                o.SetLocalPositionAndRotation(p, r);
                if (!o.gameObject.activeSelf) o.gameObject.SetActive(true);
            }
            clock.text = Clock(t) + " / " + Clock(Length);
            var x = Length > 0f ? t / Length : 0f;
            fill.anchorMax = new Vector2(x, 1f);
            handle.anchorMin = handle.anchorMax = new Vector2(x, 0.5f);
        }

        private static string Clock(float s) => Mathf.FloorToInt(s / 60f) + ":" + Mathf.FloorToInt(s % 60f).ToString("00");

        private void Update() {
            if (!ready) return;
            var k = Keyboard.current;
            var typing = InputEvents.TextInput.IsTyping;
            if (k != null && !typing) {
                if (k.escapeKey.wasPressedThisFrame && Dialog.OpenCount == 0) {
                    Close();
                    return;
                }
                if (k.spaceKey.wasPressedThisFrame) TogglePlay();
                if (k.rightArrowKey.wasPressedThisFrame) Seek(time + 5f);
                if (k.leftArrowKey.wasPressedThisFrame) Seek(time - 5f);
                if (k.fKey.wasPressedThisFrame) ToggleCamera();
            }
            if (playing) {
                time += Time.unscaledDeltaTime * speed;
                if (time >= Length) {
                    time = Length;
                    playing = false;
                    playLabel.text = "Play";
                }
            }
            Show(time);
        }

        private void LateUpdate() {
            if (!ready || follow < 0 || objects[follow] == null || PivotCamera.Main == null) return;
            var now = objects[follow].position;
            if (following && objects[follow].gameObject.activeSelf) PivotCamera.Main.ShiftFocus(now - lastFollow);
            lastFollow = now;
        }

        private void TogglePlay() {
            if (!playing && time >= Length - 0.01f) time = 0f;
            playing = !playing;
            playLabel.text = playing ? "Pause" : "Play";
        }

        private void Seek(float t) {
            time = Mathf.Clamp(t, 0f, Length);
            Show(time);
        }

        private void ToggleCamera() {
            following = !following;
            cameraLabel.text = following ? "Following the robot" : "Free camera";
        }

        public void Close() {
            if (Instance == this) Instance = null;
            if (root != null) Destroy(root.gameObject);
            foreach (var r in hiddenBuild) if (r != null) r.enabled = true;
            hiddenBuild.Clear();
            if (ownField) {
                if (field != null) Destroy(field.gameObject);
                foreach (var r in hiddenGrid) if (r != null) r.enabled = true;
                SelectionSystem.SelectionManager.Blocked = savedBlocked;
                Sandbox.SetBuildInterface(true);
                if (PivotCamera.Main != null) PivotCamera.Main.SetTransform(savedFocus, savedAngles, savedDistance);
            }
            else if (game != null && SkillsGame.Current == game) {
                field.ShowOriginals();
                if (game.tool != null) game.tool.gameObject.SetActive(true);
                GameUi.ShowSetup(game);
                game.FrameCamera();
            }
            Resources.UnloadUnusedAssets();
            if (fromOnline) Online.OnlineWindow.OpenLeaderboard();
            Destroy(gameObject);
        }

        private void OnDestroy() {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ the bar

        private void BuildBar() {
            var canvasObject = new GameObject("Replay Canvas");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            bar = Online.OnlineWindow.GlassPanel(canvasObject.transform, "Replay Bar", new Color(0.07f, 0.08f, 0.1f, 0.85f), 14f).rectTransform;
            bar.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(1100f, 112f));
            Ui.Column(bar, 8f, new RectOffset(20, 20, 12, 14));

            var top = Ui.Rect("Top", bar);
            Ui.Row(top, 10f);
            top.Size(-1, 26f);
            var tag = Ui.Text(top, "Tag", "REPLAY", 13f, Theme.Accent, Theme.Bold);
            tag.characterSpacing = 4f;
            tag.Size(80f, 26f);
            title = Ui.Text(top, "Title", "", Theme.FontBody, Color.white, Theme.Medium);
            title.Size(-1, 26f, 1f);
            clock = Ui.Text(top, "Clock", "0:00 / 0:00", Theme.FontBody, new Color(1f, 1f, 1f, 0.8f), Theme.Medium, TextAlignmentOptions.MidlineRight);
            clock.Size(130f, 26f);

            // The timeline: click or drag anywhere on it.
            var trackImage = Ui.Image(bar, "Timeline", new Color(1f, 1f, 1f, 0.12f), 4f, true);
            track = trackImage.rectTransform;
            track.Size(-1, 10f);
            var fillImage = Ui.Image(track, "Fill", Theme.Accent, 4f);
            fill = fillImage.rectTransform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            var handleImage = Ui.Image(track, "Handle", Color.white, 9f);
            handle = handleImage.rectTransform;
            handle.sizeDelta = new Vector2(18f, 18f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            var scrub = trackImage.gameObject.AddComponent<Scrub>();
            scrub.viewer = this;

            var buttons = Ui.Rect("Buttons", bar);
            Ui.Row(buttons, 8f);
            buttons.Size(-1, 36f);
            var play = Ui.Button(buttons, "Play", "Pause", TogglePlay, true, 6f);
            play.Size(110f, 34f);
            playLabel = play.GetComponentInChildren<TextMeshProUGUI>();
            Ui.Tip(play, "Play / pause", "Space", "Arrows step 5 seconds back or on.");
            var speedButton = Ui.Button(buttons, "Speed", "1x", () => {
                var i = (Array.IndexOf(Speeds, speed) + 1) % Speeds.Length;
                speed = Speeds[i];
                speedLabel.text = Speed(speed);
            }, false, 6f);
            speedButton.Size(70f, 34f);
            speedLabel = speedButton.GetComponentInChildren<TextMeshProUGUI>();
            Ui.Tip(speedButton, "Speed", null, "Click for slower or faster: 1/4, 1/2, 1, 2 or 4 times.");
            var cameraButton = Ui.Button(buttons, "Camera", "Following the robot", ToggleCamera, false, 6f);
            cameraButton.Size(190f, 34f);
            cameraLabel = cameraButton.GetComponentInChildren<TextMeshProUGUI>();
            Ui.Tip(cameraButton, "Camera", "F", "Follow the robot, or a free camera (orbit and pan as usual).");
            var space = Ui.Rect("Space", buttons);
            space.Size(-1, 34f, 1f);
            var save = Ui.Button(buttons, "Save", "Save replay…", SaveCopy, false, 6f);
            save.Size(140f, 34f);
            Ui.Tip(save, "Save replay", null, "Keeps this replay as a file you can open later (File > Open replay…) or send to someone.");
            var close = Ui.Button(buttons, "Close", "Close", Close, false, 6f);
            close.Size(90f, 34f);
            Ui.Tip(close, "Close", "Esc", null);
        }

        private static string Speed(float s) => s >= 1f ? s.ToString("0") + "x" : s == 0.5f ? "1/2x" : "1/4x";

        private void SaveCopy() {
            var path = SFB.StandaloneFileBrowser.SaveFilePanel("Save the replay", Replays.Folder, replay.robot + " " + replay.score, Replays.Extension);
            InputEvents.TextInput.AfterFileWindow();
            if (string.IsNullOrEmpty(path)) return;
            try {
                System.IO.File.WriteAllBytes(path, Replays.ToBytes(replay));
                Interface.Interface.Announce("Saved the replay: " + System.IO.Path.GetFileName(path));
            }
            catch (Exception e) { Interface.Interface.Say("Couldn't save the replay: " + e.Message, false); }
        }

        private class Scrub : MonoBehaviour, IPointerDownHandler, IDragHandler {
            public ReplayViewer viewer;
            public void OnPointerDown(PointerEventData e) => Move(e);
            public void OnDrag(PointerEventData e) => Move(e);

            private void Move(PointerEventData e) {
                var rect = (RectTransform)transform;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out var local)) return;
                var x = Mathf.InverseLerp(rect.rect.xMin, rect.rect.xMax, local.x);
                viewer.Seek(x * viewer.Length);
            }
        }
    }
}
