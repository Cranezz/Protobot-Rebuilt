using System;
using System.Collections.Generic;
using System.IO;
using Protobot.Interface;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Protobot.Builds {
    // V2: a picture of the robot as the camera sees it (top bar Picture, or File > Save picture…): with a see-through
    // background (for a notebook, a shirt, a slide), a white one, or the scene's; at the screen's size or 2x/4x for
    // print. No interface, grid or hole markers. Saved as PNG in Pictures/Protobot.
    public static class PictureSaver {
        public enum Background { Transparent, White, Scene }

        private static Background background = Background.Transparent;
        private static int scale = 2;
        private static readonly int[] Scales = { 1, 2, 4 };

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Protobot");

        public static void Open() {
            var dialog = Dialog.Open("Save picture", new Vector2(520f, 320f));
            Ui.Column(dialog.Body, 8f);
            void Build() {
                foreach (Transform child in dialog.Body) UnityEngine.Object.Destroy(child.gameObject);
                Dialog.Heading(dialog.Body, "Background");
                ToolOptions.Segmented(Dialog.Row(dialog.Body, ""), new[] { "See-through", "White", "As shown" }, (int)background, i => {
                    background = (Background)i;
                    Build();
                });
                Dialog.Heading(dialog.Body, "Size");
                var camera = Camera.main;
                var w = camera != null ? camera.pixelWidth : Screen.width;
                var h = camera != null ? camera.pixelHeight : Screen.height;
                var labels = Array.ConvertAll(Scales, s => s + "x (" + Mathf.Min(w * s, 8192) + " x " + Mathf.Min(h * s, 8192) + ")");
                ToolOptions.Segmented(Dialog.Row(dialog.Body, ""), labels, Mathf.Max(0, Array.IndexOf(Scales, scale)), i => {
                    scale = Scales[i];
                    Build();
                });
                var note = Ui.Text(dialog.Body, "Note", "Saved in Pictures\\Protobot. The view is taken as you see it now (turn the camera first).", Theme.FontSmall, Theme.TextDim);
                note.textWrappingMode = TMPro.TextWrappingModes.Normal;
                note.Size(-1, 36f);
            }
            Build();
            var folder = Ui.Button(dialog.Footer, "Folder", "Open folder", OpenFolder, false, 6f);
            folder.Size(120f, 34f);
            var save = Ui.Button(dialog.Footer, "Save", "Save picture", () => {
                dialog.Close();
                Save();
            }, true, 6f);
            save.Size(130f, 34f);
        }

        public static void OpenFolder() {
            Directory.CreateDirectory(Folder);
            Application.OpenURL("file:///" + Folder.Replace('\\', '/'));
        }

        public static string Save() {
            var camera = Camera.main;
            if (camera == null) {
                Interface.Interface.Say("No camera to take the picture with", false);
                return null;
            }
            var width = Mathf.Min(camera.pixelWidth * scale, 8192);
            var height = Mathf.Min(camera.pixelHeight * scale, 8192);
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            target.antiAliasing = 4;
            var data = camera.GetUniversalAdditionalCameraData();
            var stack = data != null ? new List<Camera>(data.cameraStack) : null;
            var mask = camera.cullingMask;
            var flags = camera.clearFlags;
            var color = camera.backgroundColor;
            var postProcessing = data != null && data.renderPostProcessing;
            var hidden = new List<Renderer>();
            try {
                // Only the 3D view: no stacked interface camera, no grid (9) or its axis lines (2), interface (5, 8,
                // 10), holes (11) or hole-face markers.
                data?.cameraStack.Clear();
                camera.cullingMask = mask & ~((1 << 2) | (1 << 5) | (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11));
                foreach (var name in new[] { "SelectedHoleFace", "HoverHoleFace" }) {
                    var marker = GameObject.Find(name);
                    if (marker != null && marker.TryGetComponent(out Renderer r) && r.enabled) {
                        r.enabled = false;
                        hidden.Add(r);
                    }
                }
                if (background != Background.Scene) {
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = background == Background.White ? Color.white : new Color(0f, 0f, 0f, 0f);
                    // Post effects write the alpha over: off for a see-through background.
                    if (background == Background.Transparent && data != null) data.renderPostProcessing = false;
                }
                camera.targetTexture = target;
                Protobot.Rendering.OutlineFeature.Suppressed = true;   // no selection or grounded outlines
                camera.Render();
            }
            finally {
                camera.targetTexture = null;
                Protobot.Rendering.OutlineFeature.Suppressed = false;
                foreach (var r in hidden) if (r != null) r.enabled = true;
                camera.cullingMask = mask;
                camera.clearFlags = flags;
                camera.backgroundColor = color;
                if (data != null) {
                    data.renderPostProcessing = postProcessing;
                    if (stack != null) foreach (var c in stack) data.cameraStack.Add(c);
                }
            }
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            if (background != Background.Transparent) {
                var pixels = texture.GetPixels32();
                for (var i = 0; i < pixels.Length; i++) pixels[i].a = 255;
                texture.SetPixels32(pixels);
            }
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, ModelExport.BuildName() + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
            Audio.Sounds.Play(Audio.Sounds.Tick);
            Interface.Interface.Announce("Saved the picture (" + width + " x " + height + ") in Pictures\\Protobot: " + Path.GetFileName(path));
            return path;
        }
    }
}
