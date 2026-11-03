using Protobot.Interface;
using TMPro;
using UnityEngine;

namespace Protobot.Builds {
    // V2: opening a build saved by an older Protobot asks whether to upgrade the file to the new format (which
    // can hold custom parts and polycarbonate pieces). Upgrading saves it now and keeps a copy of the old file;
    // "Keep old format" saves it the old way, so older versions can still open it.
    public static class UpgradePrompt {
        /// <summary>
        /// Before saving in the old format: custom parts (imported, polycarbonate) can't go in it. "Save without
        /// them" goes on; <paramref name="useNew"/> (when given) offers the new format instead.
        /// </summary>
        public static void WarnCustomParts(int count, bool sameFile, System.Action saveWithout, System.Action useNew) {
            var dialog = Dialog.Open("Custom parts won't be saved", new Vector2(560f, 270f));
            Ui.Column(dialog.Body, 10f);
            var text = Ui.Text(dialog.Body, "Message",
                "This build has " + count + (count == 1 ? " custom part" : " custom parts") + " (imported models or polycarbonate). " +
                "Files for older versions of Protobot can't hold them, so " + (count == 1 ? "it" : "they") + " won't be saved" +
                (sameFile ? " in this file." : " in the copy."), Theme.FontBody, Theme.Text);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.Size(-1, 76f);
            var hint = Ui.Text(dialog.Body, "Hint",
                useNew != null ? "Use the new format to keep them (older versions won't open it)." : "Your build here keeps them.",
                Theme.FontBody, Theme.Warning);
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.Size(-1, 44f);
            var cancel = Ui.Button(dialog.Footer, "Cancel", "Cancel", () => {
                dialog.Close();
                Interface.Interface.Say("Not saved", false);
            }, false, 6f);
            cancel.Size(-1f, 36f);
            var without = Ui.Button(dialog.Footer, "Without", "Save without them", () => {
                dialog.Close();
                saveWithout();
            }, useNew == null, 6f);
            without.Size(-1f, 36f);
            if (useNew != null) {
                var upgrade = Ui.Button(dialog.Footer, "New format", "Use new format", () => {
                    dialog.Close();
                    useNew();
                }, true, 6f);
                upgrade.Size(-1f, 36f);
            }
        }

        public static void Ask(string path, BuildsManager builds) {
            var dialog = Dialog.Open("Upgrade this build file?", new Vector2(560f, 300f));
            dialog.closeOnEscape = false;
            Ui.Column(dialog.Body, 10f);
            var name = BuildsManager.PathToFileName(path);
            var text = Ui.Text(dialog.Body, "Message",
                name + " was saved by an older version of Protobot. Upgrading saves it in the new format, which can " +
                "hold custom parts and polycarbonate pieces.", Theme.FontBody, Theme.Text);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.Size(-1, 70f);
            var warning = Ui.Text(dialog.Body, "Warning",
                "Older versions of Protobot (1.3.7 and before) won't be able to open the upgraded file. A copy of the " +
                "old file is kept next to it.", Theme.FontBody, Theme.Warning);
            warning.textWrappingMode = TextWrappingModes.Normal;
            warning.Size(-1, 70f);

            var keep = Ui.Button(dialog.Footer, "Keep", "Keep old format", () => {
                BuildFile.KeepOldFormat(path);
                dialog.Close();
                Interface.Interface.Say("Kept " + name + " in the old format", false);
            }, false, 6f);
            keep.Size(-1f, 36f);
            var upgrade = Ui.Button(dialog.Footer, "Upgrade", "Upgrade file", () => {
                dialog.Close();
                try {
                    var backup = BuildFile.Upgrade(path, SceneBuild.ToBuildData());
                    builds.MarkSaved();
                    Interface.Interface.Say("Upgraded " + name + " (the old file is saved as " + System.IO.Path.GetFileName(backup) + ")");
                }
                catch (System.Exception e) {
                    Debug.LogError("Couldn't upgrade " + path + ": " + e);
                    Interface.Interface.Say("Couldn't upgrade the file: " + e.Message, false);
                }
            }, true, 6f);
            upgrade.Size(-1f, 36f);
        }
    }
}
