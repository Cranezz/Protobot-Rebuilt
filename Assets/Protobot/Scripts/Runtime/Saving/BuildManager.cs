using System;
using System.Collections.Generic;
using Protobot.Audio;
using Protobot.Core;
using Protobot.Menus;
using Protobot.Parts;
using Protobot.Tools;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Saving
{
    /// <summary>
    /// Undo, redo, saving and loading. Before anything changes the build (picking a part up, a cut, a hole,
    /// paint, a new part) the tool calls <see cref="Record"/>, which keeps how the workshop looked; Ctrl+Z
    /// puts it back and Ctrl+Y (or Ctrl+Shift+Z) redoes it. Ctrl+S saves. The workshop is autosaved every
    /// minute and when the game closes, and the autosave is loaded when the game starts, so a build is never
    /// lost.
    /// </summary>
    public class BuildManager : MonoBehaviour
    {
        const int MaxUndo = 100;
        const float AutosaveInterval = 60f;
        const string DefaultName = "My Robot";

        [SerializeField] PartCatalog catalog;
        [SerializeField] Transform partsParent;
        [SerializeField] Hud hud;

        static BuildManager instance;

        readonly List<(string label, BuildData state)> undo = new();  // oldest first
        readonly List<(string label, BuildData state)> redo = new();
        Dictionary<string, PartEntry> byId;
        BuildData autosaved;
        float nextAutosave;
        bool restoring;

        /// <summary>The name the build was last saved or loaded under (null for a new one).</summary>
        public static string CurrentName { get; private set; }
        public static int UndoCount => instance != null ? instance.undo.Count : 0;
        public static int RedoCount => instance != null ? instance.redo.Count : 0;

        void Awake() => instance = this;

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        void Start()
        {
            nextAutosave = Time.unscaledTime + AutosaveInterval;
            if (!System.IO.File.Exists(BuildFiles.AutosavePath))
                return;
            try
            {
                var data = BuildFiles.Read(BuildFiles.AutosavePath);
                if (data.parts.Count == 0)
                    return;
                var problems = Restore(data);
                CurrentName = string.IsNullOrEmpty(data.name) ? null : data.name;
                autosaved = data;
                hud.ShowToast($"Picked up where you left off: {Count(data.parts.Count)}{Problems(problems)}", 4f, Hud.ToastKind.Success);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                // Keep the bad file for a look, but start clean next time.
                System.IO.File.Copy(BuildFiles.AutosavePath, BuildFiles.AutosavePath + ".bad", true);
                BuildFiles.Delete(BuildFiles.AutosavePath);
                hud.ShowError("Couldn't load the last session's autosave");
            }
        }

        void Update()
        {
            if (Time.unscaledTime >= nextAutosave)
            {
                nextAutosave = Time.unscaledTime + AutosaveInterval;
                Autosave();
            }

            var keyboard = Keyboard.current;
            if (keyboard == null || MenuState.AnyOpen || !keyboard.ctrlKey.isPressed)
                return;
            if (keyboard.zKey.wasPressedThisFrame)
            {
                if (keyboard.shiftKey.isPressed)
                    Redo();
                else
                    Undo();
            }
            else if (keyboard.yKey.wasPressedThisFrame)
            {
                Redo();
            }
            else if (keyboard.sKey.wasPressedThisFrame)
            {
                Save(CurrentName ?? FreeName(DefaultName));
            }
        }

        void OnApplicationQuit() => Autosave();

        // ---------------------------------------------------------------- undo

        /// <summary>Keeps how the build looks now, before an action called <paramref name="label"/> changes it.</summary>
        public static void Record(string label)
        {
            if (instance == null || instance.restoring)
                return;
            var state = BuildSnapshot.Capture();
            var list = instance.undo;
            if (list.Count > 0 && list[^1].state.SameAs(state))
            {
                // Nothing changed since the last record (e.g. a part picked up and put straight back down).
                list[^1] = (label, list[^1].state);
                return;
            }
            list.Add((label, state));
            if (list.Count > MaxUndo)
                list.RemoveAt(0);
            instance.redo.Clear();
        }

        public void Undo()
        {
            StopTools();
            var now = BuildSnapshot.Capture();
            // Skip records that look like now (an action that ended up changing nothing).
            while (undo.Count > 0 && undo[^1].state.SameAs(now))
                undo.RemoveAt(undo.Count - 1);
            if (undo.Count == 0)
            {
                hud.ShowError("Nothing to undo");
                return;
            }
            var (label, state) = undo[^1];
            undo.RemoveAt(undo.Count - 1);
            redo.Add((label, now));
            var problems = Restore(state);
            Sounds.Play(Sounds.Tick, 0.4f);
            hud.ShowToast($"Undid: {label}{Problems(problems)}   ·   Ctrl+Y redoes it", 2f);
        }

        public void Redo()
        {
            StopTools();
            if (redo.Count == 0)
            {
                hud.ShowError("Nothing to redo");
                return;
            }
            var (label, state) = redo[^1];
            redo.RemoveAt(redo.Count - 1);
            undo.Add((label, BuildSnapshot.Capture()));
            var problems = Restore(state);
            Sounds.Play(Sounds.Tick, 0.4f);
            hud.ShowToast($"Redid: {label}{Problems(problems)}", 2f);
        }

        /// <summary>Whatever a tool is doing with a part (holding it, a cut preview) ends first.</summary>
        static void StopTools()
        {
            foreach (var rotator in FindObjectsByType<PartRotator>(FindObjectsSortMode.None))
                rotator.End();
            var belt = FindFirstObjectByType<ToolBelt>();
            if (belt != null && belt.Current != null)
                belt.Current.OnSuspend();
        }

        int Restore(BuildData state)
        {
            restoring = true;
            try
            {
                return BuildSnapshot.Apply(state, Lookup, partsParent);
            }
            finally
            {
                restoring = false;
            }
        }

        PartEntry Lookup(string id)
        {
            if (byId == null)
            {
                byId = new Dictionary<string, PartEntry>();
                foreach (var entry in catalog.parts)
                    byId[entry.id] = entry;
            }
            return byId.TryGetValue(id, out var found) ? found : null;
        }

        // ---------------------------------------------------------------- files

        public static void Save(string name)
        {
            if (instance == null)
                return;
            name = name.Trim();
            var data = BuildSnapshot.Capture();
            data.name = name;
            try
            {
                BuildFiles.Write(BuildFiles.PathFor(name), data);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                instance.hud.ShowError($"Couldn't save \"{name}\": {e.Message}");
                return;
            }
            CurrentName = name;
            instance.hud.ShowSuccess($"Saved \"{name}\" ({Count(data.parts.Count)})");
        }

        public static void Load(BuildFile file)
        {
            if (instance == null)
                return;
            BuildData data;
            try
            {
                data = BuildFiles.Read(file.path);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                instance.hud.ShowError($"Couldn't load \"{file.name}\": {e.Message}");
                return;
            }
            StopTools();
            Record("Load " + file.name);
            var problems = instance.Restore(data);
            if (!file.autosave)
                CurrentName = file.name;
            instance.hud.ShowSuccess($"Loaded \"{file.name}\" ({Count(data.parts.Count)}){Problems(problems)}   ·   Ctrl+Z undoes it");
        }

        /// <summary>Clears the workshop for a new build (undoable).</summary>
        public static void NewBuild()
        {
            if (instance == null)
                return;
            StopTools();
            Record("Clear the workshop");
            instance.Restore(new BuildData());
            CurrentName = null;
            instance.hud.ShowSuccess("New build: the workshop is clear   ·   Ctrl+Z brings it back");
        }

        void Autosave()
        {
            if (restoring)
                return;
            var data = BuildSnapshot.Capture();
            if (autosaved != null ? data.SameAs(autosaved) : data.parts.Count == 0)
                return;
            data.name = CurrentName;
            try
            {
                BuildFiles.Write(BuildFiles.AutosavePath, data);
                autosaved = data;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Build] Autosave failed: " + e.Message);
            }
        }

        /// <summary>"My Robot", or "My Robot 2", 3... if that's taken.</summary>
        public static string FreeName(string baseName)
        {
            if (!BuildFiles.Exists(baseName))
                return baseName;
            for (var i = 2; ; i++)
                if (!BuildFiles.Exists($"{baseName} {i}"))
                    return $"{baseName} {i}";
        }

        static string Count(int parts) => parts == 1 ? "1 part" : $"{parts} parts";

        static string Problems(int problems) =>
            problems == 0 ? "" : $"   ·   {problems} {(problems == 1 ? "part" : "parts")} couldn't be rebuilt";
    }
}
