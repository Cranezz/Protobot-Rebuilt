using System;
using System.Collections.Generic;
using System.Linq;
using Parts_List;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Custom {
    // V2: the custom parts library (File > Custom parts library…, the parts browser's "Library" tile, or right-click a
    // custom part > Manage library): every imported part and plastic piece on this computer with its picture, where
    // it came from, its size on disk and whether the open build uses it. Delete one (or tick several, or "Select
    // unused"): its file moves to the Deleted folder and it leaves the parts browser; copies already in the build
    // keep working and are saved with it. "Recently deleted" restores them or deletes them for good.
    public class LibraryDialog : MonoBehaviour {
        private enum Filter { All, Imported, Plastic }

        private Dialog dialog;
        private RectTransform list;
        private TextMeshProUGUI summary;
        private TMP_InputField search;
        private Button deleteSelected;
        private Filter filter;
        private string query = "";
        private bool showDeleted;
        private readonly HashSet<string> ticked = new();
        private static LibraryDialog open;

        public static void Open() {
            if (open != null) {
                open.Rebuild();
                return;
            }
            var dialog = Dialog.Open("Custom parts library", new Vector2(820f, 640f));
            open = dialog.gameObject.AddComponent<LibraryDialog>();
            open.dialog = dialog;
            open.Build();
        }

        private void OnDestroy() {
            CustomParts.Changed -= Rebuild;
            if (open == this) open = null;
        }

        private void Build() {
            var body = dialog.Body;
            Ui.Column(body, 10f);

            // Filters and search.
            var top = Ui.Rect("Top", body);
            Ui.Row(top, 8f);
            top.Size(-1, 34f);
            foreach (Filter f in Enum.GetValues(typeof(Filter))) {
                var chip = Ui.Button(top, f.ToString(), f == Filter.Plastic ? "Plastic pieces" : f.ToString(), () => {
                    filter = f;
                    Rebuild();
                }, false, 6f, Theme.FontSmall);
                chip.Size(-1, 30f);
                chip.gameObject.name = "Filter " + f;
            }
            var spacer = Ui.Rect("Spacer", top);
            spacer.Size(-1, 30f, 1f);
            search = Ui.Field(top, "Search", "", null, Theme.FontSmall);
            search.Size(220f, 30f);
            var placeholder = Ui.Text(search.textViewport, "Placeholder", "Search parts", Theme.FontSmall, Theme.TextFaint);
            placeholder.rectTransform.Fill();
            search.placeholder = placeholder;
            search.onValueChanged.AddListener(text => {
                query = text ?? "";
                Rebuild();
            });

            summary = Ui.Text(body, "Summary", "", Theme.FontSmall, Theme.TextDim);
            summary.Size(-1, 20f);

            // The list.
            var frame = Ui.Image(body, "List", Theme.Field, 8f);
            frame.Size(-1, -1).GetComponent<LayoutElement>().flexibleHeight = 1f;
            var viewport = frame.rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();
            list = Ui.Rect("Rows", viewport);
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(0.5f, 1f);
            list.sizeDelta = Vector2.zero;
            Ui.Column(list, 2f, new RectOffset(6, 6, 6, 6));
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = list;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            // Footer.
            var folder = Ui.Button(dialog.Footer, "Folder", "Open folder", OpenFolder, false, 6f);
            folder.Size(-1f, 36f);
            Ui.Tip(folder, "Open the library folder", null, "Where the parts' files are kept on this computer");
            var unused = Ui.Button(dialog.Footer, "Unused", "Select unused", SelectUnused, false, 6f);
            unused.Size(-1f, 36f);
            Ui.Tip(unused, "Select unused", null, "Tick every part the open build doesn't use");
            deleteSelected = Ui.Button(dialog.Footer, "Delete", "Delete selected", DeleteTicked, false, 6f);
            deleteSelected.Size(-1f, 36f);
            var done = Ui.Button(dialog.Footer, "Done", "Done", dialog.Close, true, 6f);
            done.Size(-1f, 36f);

            CustomParts.Changed += Rebuild;
            Rebuild();
        }

        // ------------------------------------------------------------------ the list

        private bool Shows(CustomPartData part) {
            if (filter == Filter.Imported && part.kind != CustomParts.Imported) return false;
            if (filter == Filter.Plastic && part.kind != CustomParts.Polycarbonate) return false;
            if (query.Length == 0) return true;
            return (part.name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                   || (part.source ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static HashSet<string> UsedIds() {
            var used = new HashSet<string>();
            foreach (var o in PartsManager.FindLoadedObjects()) {
                if (o == null || o.IsDeleted() || !o.TryGetComponent(out SavedObject saved) || string.IsNullOrEmpty(saved.id)) continue;
                used.Add(saved.id);
            }
            return used;
        }

        private void Rebuild() {
            if (list == null) return;
            foreach (Transform child in list) Destroy(child.gameObject);
            var used = UsedIds();
            var all = CustomParts.All.OrderByDescending(p => p.created).ToList();
            ticked.RemoveWhere(k => all.All(p => p.key != k));
            var shown = all.Where(Shows).ToList();

            foreach (var f in Enum.GetValues(typeof(Filter)).Cast<Filter>()) {
                var chip = dialog.Body.Find("Top/Filter " + f);
                if (chip != null) chip.GetComponent<Image>().color = f == filter ? Theme.AccentSoft : Theme.PanelRaised;
            }
            var bytes = all.Sum(CustomParts.FileSize);
            var imported = all.Count(p => p.kind == CustomParts.Imported);
            summary.text = all.Count == 0
                ? "Your library is empty: imported parts and plastic pieces you make show up here."
                : $"{imported} imported, {all.Count - imported} plastic {(all.Count - imported == 1 ? "piece" : "pieces")}  ·  {Size(bytes)} on this computer  ·  {all.Count(p => used.Contains(p.Id))} in this build";

            if (shown.Count == 0 && all.Count > 0) Note("Nothing matches. Clear the search or pick All.");
            foreach (var part in shown) Row(part, used.Contains(part.Id), false);

            var deleted = CustomParts.Deleted.ToList();
            if (deleted.Count > 0) {
                var header = Ui.Button(list, "Deleted Header", "Recently deleted (" + deleted.Count + ")  ·  " + (showDeleted ? "hide" : "show"), () => {
                    showDeleted = !showDeleted;
                    Rebuild();
                }, false, 6f, Theme.FontSmall);
                header.targetGraphic.color = new Color(1f, 1f, 1f, 0f);
                header.Size(-1f, 34f);
                if (header.GetComponentInChildren<TextMeshProUGUI>() is TextMeshProUGUI label) {
                    label.alignment = TextAlignmentOptions.MidlineLeft;
                    label.color = Theme.TextDim;
                }
                Ui.Tip(header, "Recently deleted", null, "Deleted parts stay here until you delete them for good");
                if (showDeleted) {
                    foreach (var part in deleted.Where(Shows)) Row(part, used.Contains(part.Id), true);
                    var empty = Ui.Button(list, "Empty", "Delete all of these for good", EmptyDeleted, false, 6f, Theme.FontSmall);
                    empty.Size(-1f, 32f);
                    if (empty.GetComponentInChildren<TextMeshProUGUI>() is TextMeshProUGUI emptyLabel) emptyLabel.color = Theme.Danger;
                }
            }
            UpdateDeleteButton();
        }

        private void Note(string text) {
            var note = Ui.Text(list, "Note", text, Theme.FontSmall, Theme.TextFaint, null, TextAlignmentOptions.Center);
            note.Size(-1, 60f);
        }

        private void Row(CustomPartData part, bool inBuild, bool isDeleted) {
            var row = Ui.Image(list, part.name, Theme.Panel, 6f, true);
            row.Size(-1f, 64f);
            var hover = row.gameObject.AddComponent<HoverTint>();
            hover.target = row;
            var layout = Ui.Row(row.rectTransform, 10f, new RectOffset(8, 8, 6, 6));
            layout.childAlignment = TextAnchor.MiddleLeft;

            if (!isDeleted) {
                var tick = ticked.Contains(part.key);
                var box = Ui.Image(row.transform, "Tick", tick ? Theme.Accent : Theme.Field, 4f, true);
                box.Size(20f, 20f);
                if (box.gameObject.AddComponent<Outline>() is Outline outline) {
                    outline.effectColor = Theme.Border;
                    outline.effectDistance = new Vector2(1f, -1f);
                }
                if (tick) {
                    // A check mark: a short and a long white stroke (the font has no ✓).
                    var shortStroke = Ui.Image(box.transform, "Check", Color.white);
                    shortStroke.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-3f, -1f), new Vector2(2.5f, 7f));
                    shortStroke.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
                    var longStroke = Ui.Image(box.transform, "Check", Color.white);
                    longStroke.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1.5f, 0.5f), new Vector2(2.5f, 12f));
                    longStroke.rectTransform.localEulerAngles = new Vector3(0f, 0f, -40f);
                }
                var toggle = box.gameObject.AddComponent<Button>();
                toggle.transition = Selectable.Transition.None;
                toggle.onClick.AddListener(() => {
                    if (!ticked.Remove(part.key)) ticked.Add(part.key);
                    Rebuild();
                });
                Ui.Tip(toggle, tick ? "Untick" : "Tick", null, "Tick parts to delete several at once");
            }

            var picture = Ui.Rect("Picture", row.transform).gameObject.AddComponent<RawImage>();
            picture.raycastTarget = false;
            picture.texture = Thumbnails.Get(part);
            picture.color = isDeleted ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            picture.Size(52f, 52f);

            var text = Ui.Rect("Text", row.transform);
            Ui.Column(text, 2f);
            text.Size(-1f, 52f, 1f);
            var name = Ui.Text(text, "Name", part.name, Theme.FontBody, isDeleted ? Theme.TextDim : Theme.Text, Theme.Medium);
            name.Size(-1f, 24f);
            var details = Ui.Text(text, "Details", Details(part), Theme.FontSmall, Theme.TextFaint);
            details.Size(-1f, 20f);

            if (inBuild) {
                var chip = Ui.Image(row.transform, "In build", Theme.AccentSoft, 9f);
                var chipText = Ui.Text(chip.transform, "Label", "In this build", 12.5f, Theme.AccentHover, Theme.Medium, TextAlignmentOptions.Center);
                chipText.rectTransform.Fill(8, 0, 8, 0);
                chip.Size(96f, 22f);
            }

            if (isDeleted) {
                var restore = Ui.Button(row.transform, "Restore", "Restore", () => Restore(part), true, 6f, Theme.FontSmall);
                restore.Size(-1f, 30f);
                var purge = Ui.Button(row.transform, "Purge", "Delete for good", () => ConfirmPurge(new List<CustomPartData> { part }), false, 6f, Theme.FontSmall);
                purge.Size(-1f, 30f);
                return;
            }
            var edit = Ui.Button(row.transform, "Edit", part.kind == CustomParts.Polycarbonate ? "Edit sketch" : "Edit holes", () => {
                dialog.Close();
                if (part.kind == CustomParts.Polycarbonate) Polycarb.SheetEditor.Edit(part);
                else ImportDialog.Edit(part);
            }, false, 6f, Theme.FontSmall);
            edit.Size(-1f, 30f);
            var place = Ui.Button(row.transform, "Place", "Place", () => {
                dialog.Close();
                var browser = FindAnyObjectByType<PartsBrowser>();
                if (browser != null) browser.PlaceCustom(part);
            }, false, 6f, Theme.FontSmall);
            place.Size(-1f, 30f);
            var delete = Ui.Button(row.transform, "Delete", "Delete", () => Delete(new List<CustomPartData> { part }), false, 6f, Theme.FontSmall);
            delete.Size(-1f, 30f);
            if (delete.GetComponentInChildren<TextMeshProUGUI>() is TextMeshProUGUI deleteLabel) deleteLabel.color = Theme.Danger;
            Ui.Tip(delete, "Delete " + part.name, "Delete", "Takes it out of your library (Recently deleted can bring it back)");
        }

        private static string Details(CustomPartData part) {
            var bits = new List<string>();
            if (part.kind == CustomParts.Polycarbonate) {
                bits.Add(Polycarb.PlasticMaterials.Find(part.finish)?.name ?? "Plastic piece");
                if (part.thickness > 0f) bits.Add((Protobot.Core.Units.Fraction(part.thickness) ?? part.thickness.ToString("0.###")) + "\"");
            }
            else bits.Add(string.IsNullOrEmpty(part.source) ? "Imported" : "From " + part.source);
            var holes = part.holes?.Length ?? 0;
            bits.Add(holes == 1 ? "1 hole" : holes + " holes");
            if (part.weightGrams > 0f) bits.Add(part.weightGrams.ToString("0.#") + " g");
            bits.Add(Size(CustomParts.FileSize(part)));
            if (part.created > 0) bits.Add(DateTime.FromBinary(part.created).ToLocalTime().ToString("MMM d, yyyy"));
            return string.Join("  ·  ", bits);
        }

        private static string Size(long bytes) =>
            bytes >= 1 << 20 ? (bytes / 1048576.0).ToString("0.0") + " MB" : Mathf.Max(1, Mathf.CeilToInt(bytes / 1024f)) + " KB";

        private void UpdateDeleteButton() {
            if (deleteSelected == null) return;
            var count = ticked.Count;
            deleteSelected.interactable = count > 0;
            if (deleteSelected.GetComponentInChildren<TextMeshProUGUI>() is TextMeshProUGUI label)
                label.text = count > 0 ? "Delete selected (" + count + ")" : "Delete selected";
        }

        // ------------------------------------------------------------------ actions

        private void SelectUnused() {
            var used = UsedIds();
            var unused = CustomParts.All.Where(p => !used.Contains(p.Id) && Shows(p)).Select(p => p.key).ToList();
            if (unused.Count == 0) {
                Interface.Interface.Say("Every part shown is used in this build", false);
                return;
            }
            ticked.Clear();
            ticked.UnionWith(unused);
            Rebuild();
            Interface.Interface.Say("Selected " + unused.Count + (unused.Count == 1 ? " part" : " parts") + " this build doesn't use");
        }

        private void DeleteTicked() {
            var parts = CustomParts.All.Where(p => ticked.Contains(p.key)).ToList();
            if (parts.Count == 0) {
                Interface.Interface.Say("Tick the parts to delete first", false);
                return;
            }
            if (parts.Count == 1) {
                Delete(parts);
                return;
            }
            Confirm("Delete " + parts.Count + " parts?",
                "They leave your library and the parts browser. Recently deleted (at the bottom of this window) can bring them back.",
                "Delete " + parts.Count, () => Delete(parts));
        }

        private void Delete(List<CustomPartData> parts) {
            var used = UsedIds();
            var done = 0;
            var inBuild = 0;
            foreach (var part in parts) {
                if (!CustomParts.Remove(part.key)) continue;
                Thumbnails.Forget(part.key);
                ticked.Remove(part.key);
                done++;
                if (used.Contains(part.Id)) inBuild++;
            }
            if (done == 0) {
                Interface.Interface.Say("Couldn't delete " + (parts.Count == 1 ? parts[0].name : "those parts") + " (the file may be open somewhere else)", false);
                return;
            }
            var what = done == 1 ? parts[0].name : done + " parts";
            Interface.Interface.Say("Deleted " + what + " from your library" + (inBuild > 0 ? " (the copies in this build stay)" : "") + ": Recently deleted can restore " + (done == 1 ? "it" : "them"));
        }

        private void Restore(CustomPartData part) {
            if (CustomParts.Restore(part.key)) {
                Thumbnails.Forget(part.key);
                Interface.Interface.Say("Restored " + part.name + " to your library");
            }
            else Interface.Interface.Say("Couldn't restore " + part.name, false);
        }

        private void EmptyDeleted() => ConfirmPurge(CustomParts.Deleted.ToList());

        private void ConfirmPurge(List<CustomPartData> parts) {
            if (parts.Count == 0) return;
            var what = parts.Count == 1 ? parts[0].name : parts.Count + " parts";
            Confirm("Delete " + what + " for good?", "Their files are deleted from this computer and can't be brought back. Builds you saved with them still open with them.",
                "Delete for good", () => {
                    var done = parts.Count(p => CustomParts.Purge(p.key));
                    if (done == parts.Count) Interface.Interface.Say("Deleted " + what + " for good");
                    else Interface.Interface.Say("Couldn't delete " + (parts.Count - done) + " of them (a file may be open somewhere else)", false);
                });
        }

        private static void Confirm(string title, string text, string action, Action yes) {
            var confirm = Dialog.Open(title, new Vector2(460f, 210f));
            var message = Ui.Text(confirm.Body, "Message", text, Theme.FontBody, Theme.TextDim, null, TextAlignmentOptions.TopLeft);
            message.textWrappingMode = TextWrappingModes.Normal;
            message.rectTransform.Fill();
            var cancel = Ui.Button(confirm.Footer, "Cancel", "Cancel", () => {
                confirm.Close();
                Interface.Interface.Say("Nothing was deleted", false);
            }, false, 6f);
            cancel.Size(-1f, 36f);
            var ok = Ui.Button(confirm.Footer, "Yes", action, () => {
                confirm.Close();
                yes();
            }, false, 6f);
            ok.targetGraphic.color = Theme.Danger;
            ok.Size(-1f, 36f);
        }

        private static void OpenFolder() {
            try {
                System.IO.Directory.CreateDirectory(CustomParts.LibraryFolder);
                Application.OpenURL("file:///" + CustomParts.LibraryFolder.Replace('\\', '/'));
                Interface.Interface.Say("Opened the library folder (one .json file per part; deleted ones are in Deleted)");
            }
            catch (Exception e) {
                Interface.Interface.Say("Couldn't open the folder: " + e.Message, false);
            }
        }
    }
}
