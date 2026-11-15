using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.Devices {
    // V2: the Devices panel's pictures of its devices: the same picture the parts list shows for that part (a render
    // of the part, in its colors; the panel puts a light backdrop behind it); a part with none gets a gray picture of it
    // seen square-on (its flat outline, like the original's icons), so motors, sensors and cylinders can be told apart.
    public static class DevicePictures {
        private static readonly Dictionary<string, Texture> pictures = new();
        private static readonly Dictionary<string, bool> flat = new();

        // V2: a motor seen from the front (its shaft face, with the cartridge window), the window in its cartridge's
        // color: one picture per model and cartridge.
        private static Texture Motor(Motion.MotorDrive motor) {
            if (!motor.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null || !motor.TryGetComponent(out MeshRenderer renderer)) return null;
            var mesh = filter.sharedMesh;
            var key = "motor|" + mesh.GetInstanceID() + "|" + (motor.HasCartridge ? motor.Rpm : 0);
            if (pictures.TryGetValue(key, out var known)) return known;
            Texture2D texture = null;
            var stage = new GameObject("Motor Picture") { layer = Custom.Thumbnails.PictureLayer };
            try {
                stage.transform.position = new Vector3(0f, -6000f, 0f);
                stage.AddComponent<MeshFilter>().sharedMesh = mesh;
                var materials = renderer.sharedMaterials.ToArray();
                if (motor.HasCartridge && materials.Length > 1 && materials[1] != null) {
                    var window = new Material(materials[1]) { name = "Cartridge (picture)" };
                    var color = Motion.MotorDrive.CartridgeColor(motor.Rpm) * 1.5f;   // brighter: it's small in an icon
                    color.a = 1f;
                    window.color = color;
                    if (window.HasProperty("_BaseColor")) window.SetColor("_BaseColor", color);
                    materials[1] = window;
                }
                stage.AddComponent<MeshRenderer>().sharedMaterials = materials;
                // The front: the shaft's face the window is on (from the window's middle against the motor's).
                var axis = motor.ShaftHole != null ? motor.transform.InverseTransformDirection(motor.ShaftHole.transform.forward).normalized : Vector3.forward;
                var toward = axis;
                if (mesh.subMeshCount > 1 && mesh.isReadable) {
                    var v = mesh.vertices;
                    var t = mesh.GetTriangles(1);
                    if (t.Length > 0) {
                        var middle = Vector3.zero;
                        foreach (var i in t) middle += v[i];
                        middle /= t.Length;
                        if (Vector3.Dot(middle - mesh.bounds.center, axis) < 0f) toward = -axis;
                    }
                }
                // Looking at that face a little from above and the side, so it reads as a box.
                var up = Vector3.ProjectOnPlane(Mathf.Abs(Vector3.Dot(toward, Vector3.up)) < 0.9f ? Vector3.up : Vector3.forward, toward).normalized;
                var look = Quaternion.AngleAxis(-14f, up) * Quaternion.AngleAxis(10f, Vector3.Cross(up, toward)) * -toward;
                texture = Custom.Thumbnails.RenderView(stage, Quaternion.LookRotation(look, up), "Motor " + motor.Rpm);
            }
            catch (System.Exception e) {
                Debug.LogWarning("V2: no motor picture: " + e.Message);
            }
            finally {
                Object.DestroyImmediate(stage);
            }
            pictures[key] = texture;
            return texture;
        }

        /// <summary>The part's picture (null: none could be made), and whether it's the flat gray kind.</summary>
        public static Texture For(GameObject part, out bool gray) {
            gray = false;
            var id = V5Brain.IdOf(part);
            if (string.IsNullOrEmpty(id)) return null;
            if (part.TryGetComponent(out Motion.MotorDrive motor)) {
                var front = Motor(motor);
                if (front != null) return front;
            }
            if (pictures.TryGetValue(id, out var known)) {
                gray = flat.TryGetValue(id, out var g) && g;
                return known;
            }
            var bits = id.Split('-');
            var typeId = bits[0];
            var p1 = bits.Length > 1 ? bits[1] : null;
            var p2 = bits.Length > 2 ? bits[2] : null;
            Texture texture = null;
            try {
                texture = Interface.PartsCatalog.IsVex(typeId) && p1 != null ? Custom.Thumbnails.ForVexPart(typeId, p1) : Custom.Thumbnails.ForPart(typeId, p1, p2);
                if (texture == null) {
                    var sprite = Custom.Thumbnails.IconFor(typeId, p1, p2);
                    if (sprite != null) {
                        texture = sprite.texture;
                        gray = true;
                    }
                }
            }
            catch (System.Exception e) {
                Debug.LogWarning("V2: no picture for " + id + ": " + e.Message);
            }
            pictures[id] = texture;
            flat[id] = gray;
            return texture;
        }
    }
}
