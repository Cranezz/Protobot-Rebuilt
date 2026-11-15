using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Devices {
    // V2: the Devices panel's pictures of its devices: the same picture the parts list shows for that part (a render
    // of the part, in its colors; the panel puts a light backdrop behind it); a part with none gets a gray picture of it
    // seen square-on (its flat outline, like the original's icons), so motors, sensors and cylinders can be told apart.
    public static class DevicePictures {
        private static readonly Dictionary<string, Texture> pictures = new();
        private static readonly Dictionary<string, bool> flat = new();

        /// <summary>The part's picture (null: none could be made), and whether it's the flat gray kind.</summary>
        public static Texture For(GameObject part, out bool gray) {
            gray = false;
            var id = V5Brain.IdOf(part);
            if (string.IsNullOrEmpty(id)) return null;
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
