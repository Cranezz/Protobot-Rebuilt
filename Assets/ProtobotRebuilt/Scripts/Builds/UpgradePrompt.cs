using Protobot.Interface;
using TMPro;
using UnityEngine;

namespace Protobot.Builds {
    // V2: opening a build saved by an older Protobot asks whether to upgrade the file to the new format (which
    // can hold custom parts and polycarbonate pieces). Upgrading saves it now and keeps a copy of the old file;
    // "Later" leaves it until the next save, which upgrades it the same way (the old format isn't written any more).
    public static class UpgradePrompt {
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

            var keep = Ui.Button(dialog.Footer, "Later", "Later", () => {
                dialog.Close();
                Interface.Interface.Say(name + " will be upgraded when you save it", false);
            }, false, 6f);
            keep.Size(-1f, 36f);
            var upgrade = Ui.Button(dialog.Footer, "Upgrade", "Upgrade file", () => {
                dialog.Close();
                try {
                    var backup = BuildFile.Upgrade(path, SceneBuild.ToBuildData());
                    builds.MarkSaved();
                    Interface.Interface.Announce("Upgraded " + name + " (the old file is saved as " + System.IO.Path.GetFileName(backup) + ")");
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
