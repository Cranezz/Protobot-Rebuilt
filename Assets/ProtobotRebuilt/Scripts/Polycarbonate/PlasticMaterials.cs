using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.Polycarb {
    // V2: what a plastic piece can be cut from: the sheets VEX teams buy (V5RC R24/R25 allow polycarbonate, ABS,
    // acetal/Delrin and a few others, any color, up to 0.070" thick). Clear and smoke grey polycarbonate from
    // RoboSource, AndyMark and VEX (217-6627 clear, 217-6626 grey); darker solar-gray, bronze and opal-white Lexan
    // tints from plastic suppliers; velvet (matte) polycarbonate, frosted and grainy; RoboSource's black ABS with
    // its pebbled "haircell" face; white acetal and black Delrin; and colored tints, which sign suppliers cut to
    // order. Each has its color, shine and surface (a texture made here: a fine grain, or pebbles), and its
    // density for the weight.
    public static class PlasticMaterials {
        public enum Surface { Gloss, Velvet, Haircell }

        public class Plastic {
            public string id, name, plastic, sold;
            public Color color;
            public float smoothness;
            public Surface surface;
            public bool Opaque => color.a >= 0.99f;
        }

        public const string Default = "clear";

        public static readonly Plastic[] All = {
            new() { id = "clear", name = "Clear polycarbonate", plastic = "Polycarbonate", color = new Color(0.62f, 0.72f, 0.82f, 0.28f), smoothness = 0.55f, sold = "RoboSource, AndyMark, VEX (217-6627)" },
            new() { id = "smoke", name = "Smoke grey polycarbonate", plastic = "Polycarbonate", color = new Color(0.22f, 0.24f, 0.27f, 0.6f), smoothness = 0.6f, sold = "RoboSource, AndyMark, VEX (217-6626)" },
            new() { id = "solar", name = "Solar gray (dark tint)", plastic = "Polycarbonate", color = new Color(0.1f, 0.11f, 0.12f, 0.8f), smoothness = 0.6f, sold = "Lexan sheet suppliers" },
            new() { id = "bronze", name = "Bronze tint", plastic = "Polycarbonate", color = new Color(0.42f, 0.29f, 0.17f, 0.6f), smoothness = 0.6f, sold = "Lexan sheet suppliers" },
            new() { id = "opal", name = "Opal white", plastic = "Polycarbonate", color = new Color(0.92f, 0.92f, 0.9f, 0.85f), smoothness = 0.45f, sold = "Lexan sheet suppliers" },
            new() { id = "velvet", name = "Velvet matte (frosted)", plastic = "Polycarbonate", color = new Color(0.82f, 0.86f, 0.9f, 0.62f), smoothness = 0.12f, surface = Surface.Velvet, sold = "ePlastics, Canal Plastics (velvet polycarbonate)" },
            new() { id = "haircell", name = "Black ABS, haircell grain", plastic = "ABS", color = new Color(0.07f, 0.07f, 0.075f, 1f), smoothness = 0.22f, surface = Surface.Haircell, sold = "RoboSource (black ABS: pebbled face, smooth back)" },
            new() { id = "acetal", name = "White acetal", plastic = "Acetal", color = new Color(0.93f, 0.93f, 0.9f, 1f), smoothness = 0.3f, sold = "RoboSource" },
            new() { id = "delrin", name = "Black Delrin", plastic = "Delrin", color = new Color(0.09f, 0.09f, 0.09f, 1f), smoothness = 0.35f, sold = "RoboSource" },
            new() { id = "red", name = "Red tint", plastic = "Polycarbonate", color = new Color(0.85f, 0.12f, 0.12f, 0.55f), smoothness = 0.6f, sold = "Special order (sign and plastic suppliers)" },
            new() { id = "blue", name = "Blue tint", plastic = "Polycarbonate", color = new Color(0.12f, 0.35f, 0.9f, 0.55f), smoothness = 0.6f, sold = "Special order (sign and plastic suppliers)" },
            new() { id = "green", name = "Green tint", plastic = "Polycarbonate", color = new Color(0.15f, 0.7f, 0.3f, 0.55f), smoothness = 0.6f, sold = "Special order (sign and plastic suppliers)" },
            new() { id = "yellow", name = "Yellow tint", plastic = "Polycarbonate", color = new Color(0.95f, 0.8f, 0.1f, 0.55f), smoothness = 0.6f, sold = "Special order (sign and plastic suppliers)" },
            new() { id = "orange", name = "Orange tint", plastic = "Polycarbonate", color = new Color(0.95f, 0.45f, 0.08f, 0.55f), smoothness = 0.6f, sold = "Special order (sign and plastic suppliers)" },
        };

        public static Plastic Find(string id) => All.FirstOrDefault(p => p.id == id) ?? All[0];

        private static readonly Dictionary<string, Material> materials = new();
        private static readonly Dictionary<Surface, Texture2D> textures = new();

        /// <summary>The material a piece made of this plastic is drawn with (one per plastic, made once).</summary>
        public static Material MaterialFor(string id) {
            var plastic = Find(id);
            if (materials.TryGetValue(plastic.id, out var material) && material != null) return material;
            var host = Interface.Interface.Instance;
            if (host == null) return null;
            // See-through plastics start from the polycarbonate material (transparent), solid ones from plastic.
            var source = plastic.Opaque ? host.plasticMaterial : host.polycarbMaterial;
            if (source == null) return null;
            material = new Material(source) { name = "Plastic " + plastic.name };
            material.color = plastic.color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", plastic.color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", plastic.smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", plastic.smoothness);
            if (plastic.surface != Surface.Gloss) {
                // The grain is drawn into the color map (shaded pebbles or speckle), one tile per inch of the
                // piece (its mesh has uv = x, z in inches).
                var texture = Texture(plastic.surface);
                material.mainTexture = texture;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                material.mainTextureScale = Vector2.one;
            }
            materials[plastic.id] = material;
            return material;
        }

        /// <summary>A grain texture, white with shading (it's multiplied by the plastic's color).</summary>
        public static Texture2D Texture(Surface surface) {
            if (textures.TryGetValue(surface, out var texture) && texture != null) return texture;
            const int size = 256;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = surface + " grain", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var pixels = new Color32[size * size];
            var random = new System.Random(surface == Surface.Haircell ? 7 : 11);
            if (surface == Surface.Haircell) {
                // Pebbles: bumps around scattered points (wrapping), lit from the top left.
                const int cells = 30;
                var points = new List<Vector2>();
                for (var i = 0; i < cells * cells; i++)
                    points.Add(new Vector2((float)random.NextDouble() * size, (float)random.NextDouble() * size));
                var grid = new List<int>[8, 8];
                for (var gx = 0; gx < 8; gx++)
                    for (var gy = 0; gy < 8; gy++) grid[gx, gy] = new List<int>();
                for (var i = 0; i < points.Count; i++) grid[(int)(points[i].x / 32f) % 8, (int)(points[i].y / 32f) % 8].Add(i);
                var height = new float[size * size];
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++) {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float nearest = float.MaxValue, second = float.MaxValue;
                        int cx = x / 32, cy = y / 32;
                        for (var dx = -1; dx <= 1; dx++)
                            for (var dy = -1; dy <= 1; dy++)
                                foreach (var i in grid[(cx + dx + 8) % 8, (cy + dy + 8) % 8]) {
                                    var d = p - points[i];
                                    if (d.x > size / 2f) d.x -= size;
                                    if (d.x < -size / 2f) d.x += size;
                                    if (d.y > size / 2f) d.y -= size;
                                    if (d.y < -size / 2f) d.y += size;
                                    var distance = d.magnitude;
                                    if (distance < nearest) { second = nearest; nearest = distance; }
                                    else if (distance < second) second = distance;
                                }
                        // High in a pebble's middle, low in the creases between them.
                        height[y * size + x] = Mathf.Clamp01((second - nearest) / 5f);
                    }
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++) {
                        var hx = height[y * size + (x + 1) % size] - height[y * size + (x + size - 1) % size];
                        var hy = height[((y + 1) % size) * size + x] - height[((y + size - 1) % size) * size + x];
                        var light = 0.78f + (-hx + hy) * 0.9f + height[y * size + x] * 0.12f;
                        var v = (byte)(Mathf.Clamp01(light) * 255f);
                        pixels[y * size + x] = new Color32(v, v, v, 255);
                    }
            }
            else {
                // Velvet: a fine, even speckle (a frosted surface), barely darker.
                for (var i = 0; i < pixels.Length; i++) {
                    var v = (byte)(Mathf.Clamp01(0.86f + (float)random.NextDouble() * 0.14f) * 255f);
                    pixels[i] = new Color32(v, v, v, 255);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            textures[surface] = texture;
            return texture;
        }
    }
}
