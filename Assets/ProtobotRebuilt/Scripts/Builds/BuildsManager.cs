using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.UI;
using SFB;

using Protobot.UI;
using Protobot.InputEvents;
using UnityEngine.SceneManagement;

namespace Protobot.Builds {
    public class BuildsManager : MonoBehaviour {
        [SerializeField] private InputEvent saveInput;

        [SerializeField] private UnsavedChangesUI unsavedChangesMenu;
        
        /// <summary>
        /// Stores the path where the current build is located
        /// </summary>
        public string buildPath = "";
        
        /// <summary>
        /// Stores the currently saved data for the loaded build
        /// </summary>
        private BuildData savedBuildData;

        private string attemptPath = "";
        private BuildData attemptData;
        
        public BuildDataUnityEvent OnLoadBuild;
        public BuildDataUnityEvent OnSaveBuild;

        private bool avoidingQuit = false;
        private bool forceQuit = false;

        private bool IsNotSaved => buildPath == "";

        private void Awake() {
            saveInput.performed += Save;
        }

        public void Start() {
            buildPath = "";
            
            SceneBuild.OnGenerateBuild += (data) => {
                OnLoadBuild.Invoke(data);
            };
            

            Application.wantsToQuit += () => {
                avoidingQuit = HasUnsavedChanges() && !forceQuit;
                if (avoidingQuit) {
                    unsavedChangesMenu.Enable(IsNotSaved);
                }

                return !avoidingQuit;
            };

            unsavedChangesMenu.OnPressDiscard += () => {
                if (avoidingQuit) 
                    Quit();
                else
                    LoadAttempt();
            };

            unsavedChangesMenu.OnPressSave += () => {
                if (avoidingQuit)
                    SaveAndQuit();
                else
                    SaveAndLoadAttempt();
            };
            
            string[] arguments = Environment.GetCommandLineArgs();
            string initPath = arguments[0];
            var initData = ParsePath(initPath);

            if (initData != null)
                AttemptLoad(initData, initPath);
        }

        public string GetFileName() => PathToFileName(buildPath);
        
        public static string PathToFileName(string path) => (path.Length > 0) ? path.Split('\\')[^1] : "";
        
        public void Save() => SaveThen(null);

        // V2: saving may first ask about custom parts (old-format files): what comes after (quitting, opening
        // another build) waits for the answer.
        private void SaveThen(Action then) {
            if (buildPath == "") {
                SaveAs();
                return;
            }

            var sceneBuildData = SceneBuild.ToBuildData();

            // V2: a file kept in the old format can't hold custom parts: say so before leaving them out.
            if (BuildFile.KeepsOldFormat(buildPath) && BuildFile.CustomPartCount(sceneBuildData) > 0 && !confirmedOldSave) {
                UpgradePrompt.WarnCustomParts(BuildFile.CustomPartCount(sceneBuildData), true,
                    () => { confirmedOldSave = true; SaveThen(then); confirmedOldSave = false; },
                    () => { BuildFile.UseNewFormat(buildPath); SaveThen(then); });
                return;
            }

            // V2: a zip with the build and the custom parts it uses (BuildFile).
            try {
                BuildFile.Write(buildPath, sceneBuildData);
            }
            catch (Exception e) {
                Debug.LogError("Couldn't save " + buildPath + ": " + e);
                Interface.Interface.Say("Couldn't save the build: " + e.Message, false);
                return;
            }
            Interface.Interface.Say("Saved " + PathToFileName(buildPath));

            savedBuildData = sceneBuildData;
            OnSaveBuild?.Invoke(sceneBuildData);
            then?.Invoke();
        }

        private bool confirmedOldSave;

        /// <summary>
        /// V2: File > Save for Older Versions: a copy in the original's format, which Protobot 1.3.7 and before
        /// open. Custom parts can't go in it: a warning says how many are left out first.
        /// </summary>
        public void SaveForOlderVersions() {
            var build = SceneBuild.ToBuildData();
            var custom = BuildFile.CustomPartCount(build);
            void Write() {
                var path = StandaloneFileBrowser.SaveFilePanel("Save for older versions of Protobot", "",
                    Path.GetFileNameWithoutExtension(GetFileName()) + " (old format)", "pbb");
                if (string.IsNullOrEmpty(path)) {
                    Interface.Interface.Say("Not saved", false);
                    return;
                }
                try {
                    BuildFile.WriteOld(path, build);
                    Interface.Interface.Say("Saved " + PathToFileName(path) + " for older versions" + (custom > 0 ? " (without its " + custom + " custom parts)" : ""));
                }
                catch (Exception e) {
                    Debug.LogError("Couldn't save " + path + ": " + e);
                    Interface.Interface.Say("Couldn't save the file: " + e.Message, false);
                }
            }
            if (custom > 0) UpgradePrompt.WarnCustomParts(custom, false, Write, null);
            else Write();
        }

        /// <summary>V2: what's in the scene now counts as saved (after the file was written elsewhere).</summary>
        public void MarkSaved() {
            savedBuildData = SceneBuild.ToBuildData();
            OnSaveBuild?.Invoke(savedBuildData);
        }

        public void SaveAs() {
            var path = StandaloneFileBrowser.SaveFilePanel("Save Build File", "", "", "pbb");

            if (path == "") return;

            buildPath = path;
            Save();
        }

        public void SaveAndQuit() {
            if (buildPath == "") {
                Save();
                Quit();
                return;
            }
            SaveThen(Quit);  // V2
        }

        public void Quit() {
            forceQuit = true;
            Application.Quit();
        }

        public void AttemptQuit() {
            Application.Quit();
        }

        private bool HasUnsavedChanges() {
            var sceneBuild = SceneBuild.ToBuildData();
            
            if (IsNotSaved) {
                return sceneBuild.parts.Length > 0;
            }

            return !sceneBuild.CompareData(savedBuildData);
        }

        public void OpenBuild() {
            var paths = StandaloneFileBrowser.OpenFilePanel("Open Build File", "", "pbb", false);

            if (paths.Length == 0 || paths[0] == "") return;

            var path = paths[0];

            var build = ParsePath(path);
            if (build == null) return;  // V2: ParsePath said why
            AttemptLoad(build, path);
        }
        
        /// <summary>
        /// Converts a given file path to BuildData
        /// </summary>
        public static BuildData ParsePath(string filePath) {
            if (!File.Exists(filePath) || !filePath.Contains(".pbb")) return null;

            // V2: the new zip format, or the original's BinaryFormatter file (BuildFile).
            try {
                return BuildFile.Read(filePath);
            }
            catch (Exception e) {
                Debug.LogError("Couldn't open " + filePath + ": " + e);
                Interface.Interface.Say("Couldn't open " + PathToFileName(filePath) + ": it isn't a build file this version can read", false);
                return null;
            }
        }
        
        /// <summary>
        /// Sets the attempt variables to be ready once a load call is given
        /// </summary>
        /// <param name="newData"></param>
        /// <param name="newPath"></param>
        public void AttemptLoad(BuildData newData, string newPath) {
            attemptData = newData;
            attemptPath = newPath;

            if (HasUnsavedChanges()) {
                unsavedChangesMenu.Enable(IsNotSaved);
            }
            else {
                LoadAttempt();
            }
        }

        public void SaveAndLoadAttempt() {
            if (buildPath == "") {
                Save();
                LoadAttempt();
                return;
            }
            SaveThen(LoadAttempt);  // V2
        }

        public void LoadAttempt() {
            savedBuildData = attemptData;
            buildPath = attemptPath;
            SceneBuild.GenerateBuild(attemptData);

            // V2: a file from an older version: ask whether to upgrade it to the new format.
            if (buildPath != "" && BuildFile.IsOld(buildPath))
                UpgradePrompt.Ask(buildPath, this);

            // Call OutputPartsList to update the weight display after placing a part
                PartListOutput partListOutput = FindObjectOfType<PartListOutput>();
                if (partListOutput != null) {
                    partListOutput.CalculatePartsList(); // Recalculate and notify weight update
                }
        }

        /// <summary>
        /// Loads an empty build with an untitled.pbb
        /// </summary>
        public void CreateNewBuild() {
            AttemptLoad(SceneBuild.DefaultBuild, "");
        }
    }
}