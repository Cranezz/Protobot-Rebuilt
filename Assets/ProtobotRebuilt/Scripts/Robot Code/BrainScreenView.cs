using System.Collections.Generic;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.RobotCode {
    /// <summary>
    /// V2 (Phase 9): shows the running program's Brain screen (BrainCanvas: 480 x 240, drawn pixels plus texts over
    /// them) and takes touches (click/drag on it = a finger on the screen). Beside it the controller's 3-line screen,
    /// which shakes while the program rumbles the controller (a connected gamepad rumbles too).
    /// The texture is uploaded only when the screen changed and this is shown (at most once a frame).
    /// </summary>
    public sealed class BrainScreenView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler {
        private RawImage image;
        private Texture2D texture;
        private RectTransform textLayer, lcdBox;
        private TextMeshProUGUI lcd;
        private readonly List<TextMeshProUGUI> pool = new();
        private BrainCanvas shown;
        private int shownVersion = -1;
        private bool rumbling;
        private float nextLcd;
        private string lcdText;
        private Vector2 lcdHome;

        public static BrainScreenView Create(RectTransform parent) {
            var root = Ui.Rect("Brain Screen View", parent).Fill();
            var view = root.gameObject.AddComponent<BrainScreenView>();
            view.Build(root);
            return view;
        }

        private void Build(RectTransform root) {
            // The Brain's screen on the left (2:1, as large as fits), the controller's screen on the right.
            var screenArea = Ui.Rect("Screen Area", root);
            screenArea.anchorMin = Vector2.zero;
            screenArea.anchorMax = new Vector2(1f, 1f);
            screenArea.offsetMin = new Vector2(8f, 8f);
            screenArea.offsetMax = new Vector2(-196f, -8f);
            var frame = Ui.Rect("Frame", screenArea);
            var fitter = frame.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 2f;
            image = frame.gameObject.AddComponent<RawImage>();
            texture = new Texture2D(BrainCanvas.Width, BrainCanvas.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var black = new Color32[BrainCanvas.Width * BrainCanvas.Height];
            for (var i = 0; i < black.Length; i++) black[i] = new Color32(0, 0, 0, 255);
            texture.SetPixels32(black);
            texture.Apply(false);
            image.texture = texture;
            textLayer = Ui.Rect("Texts", frame);
            textLayer.anchorMin = textLayer.anchorMax = new Vector2(0f, 1f);
            textLayer.pivot = new Vector2(0f, 1f);
            textLayer.sizeDelta = new Vector2(BrainCanvas.Width, BrainCanvas.Height);
            textLayer.anchoredPosition = Vector2.zero;
            frame.gameObject.AddComponent<RectMask2D>();
            Ui.Tip(frame, "The V5 Brain's screen", null, "What the program draws and prints (brain.screen). Click or drag on it to touch the screen.");

            lcdBox = Ui.Image(root, "Controller Screen", new Color(0.62f, 0.7f, 0.6f, 1f), 6f).rectTransform;
            lcdBox.anchorMin = lcdBox.anchorMax = new Vector2(1f, 0.5f);
            lcdBox.pivot = new Vector2(1f, 0.5f);
            lcdBox.sizeDelta = new Vector2(180f, 66f);
            lcdHome = new Vector2(-8f, 6f);
            lcdBox.anchoredPosition = lcdHome;
            lcd = Ui.Text(lcdBox, "Text", "", 13f, new Color(0.1f, 0.14f, 0.1f), MonoFont.Get, TextAlignmentOptions.TopLeft);
            lcd.rectTransform.Fill(8, 6, 6, 4);
            lcd.textWrappingMode = TextWrappingModes.NoWrap;
            lcd.overflowMode = TextOverflowModes.Truncate;
            lcd.richText = false;
            var caption = Ui.Text(root, "Caption", "Controller screen", Theme.FontSmall - 1f, Theme.TextFaint, null, TextAlignmentOptions.Center);
            caption.rectTransform.anchorMin = caption.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            caption.rectTransform.pivot = new Vector2(1f, 1f);
            caption.rectTransform.sizeDelta = new Vector2(180f, 18f);
            caption.rectTransform.anchoredPosition = new Vector2(-8f, -30f);
            Ui.Tip(lcdBox, "The V5 Controller's screen", null, "What the program prints on controller.screen (3 rows of 19). It shakes while the program rumbles the controller.");
        }

        private void LateUpdate() {
            var program = RobotProgram.Current;
            var canvas = program?.Brain.screen;
            if (canvas != null && canvas != shown) {
                shown = canvas;
                shownVersion = -1;
            }
            // (The last program's screen stays up after it ends, as the Brain's does.)
            if (shown != null && shown.Version != shownVersion) Redraw();
            var frame = (RectTransform)image.transform;
            var scale = frame.rect.width / BrainCanvas.Width;
            if (scale > 0f && !Mathf.Approximately(textLayer.localScale.x, scale)) textLayer.localScale = new Vector3(scale, scale, 1f);

            if (Time.unscaledTime >= nextLcd) {
                nextLcd = Time.unscaledTime + 0.1f;
                var text = program != null ? program.Controller(false).screen.Text() : lcdText ?? "";
                if (text != lcdText) {
                    lcdText = text;
                    lcd.text = text;
                }
            }
            var buzz = VexController.Buzzing();
            lcdBox.anchoredPosition = buzz ? lcdHome + new Vector2(Mathf.Sin(Time.unscaledTime * 90f) * 2f, Mathf.Cos(Time.unscaledTime * 70f)) : lcdHome;
            if (buzz != rumbling) {
                rumbling = buzz;
                Gamepad.current?.SetMotorSpeeds(buzz ? 0.35f : 0f, buzz ? 0.6f : 0f);
            }
        }

        private void OnDisable() {
            if (rumbling) Gamepad.current?.SetMotorSpeeds(0f, 0f);
            rumbling = false;
            if (shown != null && shown.Pressing) shown.Touch(false, shown.TouchX, shown.TouchY);
        }

        private void OnDestroy() {
            if (texture != null) Destroy(texture);
        }

        private void Redraw() {
            shownVersion = shown.Version;
            texture.SetPixels32(shown.front.pixels);
            texture.Apply(false);
            var texts = shown.front.texts;
            for (var i = 0; i < texts.Count; i++) {
                if (i == pool.Count) {
                    var made = Ui.Text(textLayer, "Line", "", 20f, Color.white, MonoFont.Get, TextAlignmentOptions.TopLeft);
                    made.rectTransform.anchorMin = made.rectTransform.anchorMax = new Vector2(0f, 1f);
                    made.rectTransform.pivot = new Vector2(0f, 1f);
                    made.textWrappingMode = TextWrappingModes.NoWrap;
                    made.overflowMode = TextOverflowModes.Overflow;
                    made.raycastTarget = false;
                    made.richText = true;
                    pool.Add(made);
                }
                var t = texts[i];
                var label = pool[i];
                label.gameObject.SetActive(true);
                label.font = t.mono ? MonoFont.Get : Theme.Regular;
                label.fontSize = t.size * 0.9f;
                label.color = t.color;
                // Monospaced fonts at the Brain's cell width (half the height), so columns line up as on the robot.
                label.text = t.mono ? "<mspace=" + (t.size * 0.5f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "><noparse>" + t.text + "</noparse>"
                                    : "<noparse>" + t.text + "</noparse>";
                label.rectTransform.anchoredPosition = new Vector2(t.x, -t.y);
                label.rectTransform.sizeDelta = new Vector2(Mathf.Max(4f, t.width + 4f), t.size + 4f);
            }
            for (var i = texts.Count; i < pool.Count; i++)
                if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ touch

        public void OnPointerDown(PointerEventData e) => Touch(e, true);
        public void OnDrag(PointerEventData e) => Touch(e, true);
        public void OnPointerUp(PointerEventData e) => Touch(e, false);

        private void Touch(PointerEventData e, bool down) {
            if (shown == null) return;
            var frame = (RectTransform)image.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, e.position, e.pressEventCamera, out var local)) return;
            var r = frame.rect;
            var x = Mathf.FloorToInt((local.x - r.xMin) / r.width * BrainCanvas.Width);
            var y = Mathf.FloorToInt((r.yMax - local.y) / r.height * BrainCanvas.Height);
            shown.Touch(down, x, y);
        }
    }
}
