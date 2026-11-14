using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Builds {
    // V2: saved groups (sub-assemblies): right-click a part > Save group… keeps it and everything fastened to it
    // (a drivetrain module, an intake, a claw) under a name, in persistentDataPath/Saved Groups/<name>.json: each
    // part's id, saved state, colors and its pose from the part that was clicked. The parts list's Custom section
    // lists them ("Saved groups"); picking one puts a copy in the build at the middle of the view, standing on the
    // grid (one undo step), selected to be moved where it goes.
    public static class SavedGroups {
        [Serializable]
        public class GroupFile {
            public string name;
            public string version;
            public ObjectData[] parts;
        }

        public static string Folder => Path.Combine(Application.persistentDataPath, "Saved Groups");

        public static List<string> Names() {
            if (!Directory.Exists(Folder)) return new List<string>();
            return Directory.GetFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();
        }

        public static void SaveDialog(GameObject part) {
            if (part == null) return;
            var parts = (part.GetConnectedObjects(true, true) ?? new List<GameObject> { part })
                .Where(o => o != null && o.GetComponent<SavedObject>() != null && !o.IsDeleted()).Distinct().ToList();
            if (!parts.Contains(part)) parts.Insert(0, part);
            var dialog = Dialog.Open("Save group", new Vector2(460f, 210f));
            Ui.Column(dialog.Body, 8f);   // V2: the body lays its rows out (they overlapped)
            Dialog.Heading(dialog.Body, parts.Count + (parts.Count == 1 ? " part" : " parts") + " (" + PartLabels.Short(part) + " and what's fastened to it)");
            var name = "Group " + (Names().Count + 1);
            var field = Ui.Field(dialog.Body, "Name", name, value => name = value);
            field.Size(-1, 38f);
            field.onValueChanged.AddListener(value => name = value);
            var save = Ui.Button(dialog.Footer, "Save", "Save", () => {
                var clean = new string((name ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
                if (clean.Length == 0) {
                    Interface.Interface.Say("Give the group a name", false);
                    return;
                }
                Save(clean, part, parts);
                dialog.Close();
            }, true, 6f);
            save.Size(110f, 34f);
        }

        public static void Save(string name, GameObject reference, List<GameObject> parts) {
            var inverse = Quaternion.Inverse(reference.transform.rotation);
            var origin = reference.transform.position;
            var data = new GroupFile { name = name, version = Application.version, parts = parts.Select(p => {
                var renderer = p.GetComponent<Renderer>();
                var color = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.color : Color.white;
                var position = inverse * (p.transform.position - origin);
                var rotation = (inverse * p.transform.rotation).eulerAngles;
                return new ObjectData {
                    partId = p.GetComponent<SavedObject>().id, states = p.GetComponent<SavedObject>().state,
                    xPos = position.x, yPos = position.y, zPos = position.z, xRot = rotation.x, yRot = rotation.y, zRot = rotation.z,
                    rColor = color.r, gColor = color.g, bColor = color.b,
                    colors = renderer != null && renderer.sharedMaterials.Length > 1
                        ? string.Join(",", renderer.sharedMaterials.Select(m => m != null ? ColorUtility.ToHtmlStringRGB(m.color) : "FFFFFF")) : null,
                };
            }).ToArray() };
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, name + ".json"), JsonUtility.ToJson(data, true));
            PartsCatalog.Invalidate();
            Interface.Interface.Announce("Saved the group \"" + name + "\" (" + data.parts.Length + " parts): it's in the parts list under Custom > Saved groups");
        }

        public static void Place(string name) {
            var path = Path.Combine(Folder, name + ".json");
            if (!File.Exists(path)) {
                Interface.Interface.Say("That saved group is gone", false);
                return;
            }
            GroupFile data;
            try { data = JsonUtility.FromJson<GroupFile>(File.ReadAllText(path)); }
            catch (Exception e) {
                Interface.Interface.Say("Couldn't read the saved group: " + e.Message, false);
                return;
            }
            if (data?.parts == null || data.parts.Length == 0) return;
            var focus = PivotCamera.Main != null ? PivotCamera.Main.focusPosition : Vector3.zero;
            var build = new BuildData { version = data.version };
            var made = new List<GameObject>();
            // Parts first, then what connects them (screws find their holes), like loading a build.
            foreach (var connecting in new[] { false, true })
                foreach (var part in data.parts) {
                    var type = PartsManager.GetPartType(part.partId);
                    if (type == null || type.connectingPart != connecting) continue;
                    try {
                        var obj = SceneBuild.GeneratePart(part, build);
                        if (obj != null) made.Add(obj);
                    }
                    catch (Exception e) {
                        Debug.LogWarning("Saved group: couldn't make " + part.partId + ": " + e.Message);
                    }
                }
            if (made.Count == 0) return;
            // At the middle of the view, standing on the grid.
            var low = made.Select(o => o.GetComponent<Renderer>()).Where(r => r != null).Select(r => r.bounds.min.y).DefaultIfEmpty(0f).Min();
            var shift = new Vector3(focus.x, 0f, focus.z) + Vector3.up * (0f - low);
            foreach (var o in made) o.transform.position += shift;
            var before = made.Select(o => new ObjectElement(o) { existing = false }).Cast<IElement>().ToList();
            StateSystem.AddElements(before);
            StateSystem.AddState(new State(made.Select(o => (IElement)new ObjectElement(o)).ToList()));
            Interface.Interface.Say("Placed the group \"" + name + "\" (" + made.Count + " parts) in the middle of the view: move it with the Transform tool");
        }

        public static void Delete(string name) {
            var path = Path.Combine(Folder, name + ".json");
            if (File.Exists(path)) File.Delete(path);
            PartsCatalog.Invalidate();
            Interface.Interface.Say("Deleted the saved group \"" + name + "\"");
        }
    }
}
