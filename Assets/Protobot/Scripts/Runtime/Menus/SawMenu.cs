using System.Globalization;
using Protobot.Core;
using Protobot.Parts;
using Protobot.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Menus
{
    /// <summary>
    /// Precise cut menu (hold right click with the saw on a part). The saw stays frozen on the part while
    /// you set exact numbers: where to cut (mm from the part's end, optionally snapped to half holes), the
    /// cut angle and tilt (typed angles over 360° wrap around), and which side to keep. The preview updates live.
    /// </summary>
    public class SawMenu : MonoBehaviour
    {
        [SerializeField] UiTheme theme;

        SawTool saw;
        PartBody part;
        StockProfile profile;
        GameObject root;
        TextMeshProUGUI partName;
        Slider position;
        TMP_InputField positionField;
        Slider angle;
        TMP_InputField angleField;
        Slider tilt;
        TMP_InputField tiltField;
        ToggleSwitch snap;
        OptionSelector side;
        bool updating;

        public bool IsOpen => root != null && root.activeSelf;

        void Awake()
        {
            UiFactory.Canvas(gameObject, 25);
            Build();
            root.SetActive(false);
        }

        public void Open(SawTool tool, PartBody target)
        {
            saw = tool;
            part = target;
            profile = target.GetComponent<StockProfile>();
            partName.text = target.name;
            position.minValue = 0f;
            position.maxValue = profile.Length * 1000f;
            root.SetActive(true);
            MenuState.Open(this);

            var (a, b, flip) = saw.Angles;
            updating = true;
            SetPosition((saw.LockedT - profile.Start) * 1000f, false);
            angle.SetValueWithoutNotify(a);
            angleField.SetTextWithoutNotify($"{a:0.#}");
            tilt.SetValueWithoutNotify(b);
            tiltField.SetTextWithoutNotify($"{b:0.#}");
            side.SetIndex(flip ? 1 : 0, false);
            updating = false;
            Push();
        }

        void Close()
        {
            root.SetActive(false);
            MenuState.Close(this);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void Update()
        {
            if (IsOpen && Keyboard.current.escapeKey.wasPressedThisFrame && MenuState.ChangedFrame != Time.frameCount)
            {
                saw.CancelMenu();
                Close();
            }
        }

        void SetPosition(float mm, bool push)
        {
            if (snap.On)
                mm = Mathf.Round(mm / (SawTool.HolePitch * 500f)) * (SawTool.HolePitch * 500f);
            mm = Mathf.Clamp(mm, 0f, profile.Length * 1000f);
            position.SetValueWithoutNotify(mm);
            positionField.SetTextWithoutNotify($"{mm:0.#}");
            if (push)
                Push();
        }

        /// <summary>Sends the menu's numbers to the saw, which updates the preview.</summary>
        void Push()
        {
            if (updating || saw == null)
                return;
            saw.SetMenuValues(profile.Start + position.value / 1000f, angle.value, tilt.value, side.Index == 1);
        }

        static float WrapAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            if (degrees < -180f) degrees += 360f;
            return Mathf.Clamp(degrees, -75f, 75f);
        }

        static bool TryParse(string text, out float value) =>
            float.TryParse(text.Replace("°", "").Replace("mm", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        void Build()
        {
            root = UiFactory.Rect("Root", transform).Fill().gameObject;
            var panel = UiFactory.Panel(root.transform, "Panel", theme, theme.panel, true).rectTransform;
            panel.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-60f, 0f), new Vector2(560f, 640f));

            var stripes = UiFactory.Hazard(panel, "Stripes", theme);
            stripes.rectTransform.anchorMin = new Vector2(0f, 1f);
            stripes.rectTransform.anchorMax = new Vector2(1f, 1f);
            stripes.rectTransform.offsetMin = new Vector2(14f, -24f);
            stripes.rectTransform.offsetMax = new Vector2(-14f, -12f);
            var title = UiFactory.Text(panel, "Title", theme, "PRECISE CUT", 44f, theme.text, true);
            title.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -30f), new Vector2(480f, 56f));
            partName = UiFactory.Text(panel, "Part", theme, "", 22f, theme.textDim);
            partName.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -84f), new Vector2(490f, 30f));

            var list = UiFactory.Rect("Rows", panel).Fill(32f, 110f, 32f, 130f);
            UiFactory.VerticalList(list, 6f);

            (position, positionField) = NumberRow(list, "Position from end (mm)", 0f, 100f, v => SetPosition(v, true),
                text => { if (TryParse(text, out var v)) SetPosition(v, true); });
            snap = ToggleSwitch.Create(UiFactory.Row(list, "Snap to 1/4 in", theme), theme, true, _ => SetPosition(position.value, true));
            (angle, angleField) = NumberRow(list, "Angle (°)", -75f, 75f, v => SetAngle(angle, angleField, v),
                text => { if (TryParse(text, out var v)) SetAngle(angle, angleField, v); });
            (tilt, tiltField) = NumberRow(list, "Tilt (°)", -75f, 75f, v => SetAngle(tilt, tiltField, v),
                text => { if (TryParse(text, out var v)) SetAngle(tilt, tiltField, v); });
            side = OptionSelector.Create(UiFactory.Row(list, "Keep", theme), theme, new[] { "Near side", "Far side" }, 0, _ => Push());

            var help = UiFactory.Text(list, "Help", theme, "The red see-through piece is what gets cut off.", 20f, theme.textDim);
            help.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

            var cut = UiFactory.Button(panel, "Cut", theme, "CUT", () =>
            {
                Close();
                saw.CutFromMenu();
            }, true, 28f);
            ((RectTransform)cut.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-32f, 32f), new Vector2(220f, 64f));
            var cancel = UiFactory.Button(panel, "Cancel", theme, "CANCEL", () =>
            {
                saw.CancelMenu();
                Close();
            }, false, 24f);
            ((RectTransform)cancel.transform).Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32f, 32f), new Vector2(200f, 64f));
        }

        void SetAngle(Slider slider, TMP_InputField field, float value)
        {
            value = WrapAngle(value);
            slider.SetValueWithoutNotify(value);
            field.SetTextWithoutNotify($"{value:0.#}");
            Push();
        }

        (Slider, TMP_InputField) NumberRow(RectTransform list, string label, float min, float max,
            UnityEngine.Events.UnityAction<float> onSlide, UnityEngine.Events.UnityAction<string> onType)
        {
            var row = UiFactory.Row(list, label, theme, 56f);
            var slider = UiFactory.Slider(row, "Slider", theme, min, max, 0f, onSlide);
            ((RectTransform)slider.transform).Fill(0f, 0f, 110f, 0f);
            var field = UiFactory.InputField(row, "Value", theme, "", 22f);
            ((RectTransform)field.transform).anchorMin = new Vector2(1f, 0f);
            ((RectTransform)field.transform).anchorMax = new Vector2(1f, 1f);
            ((RectTransform)field.transform).pivot = new Vector2(1f, 0.5f);
            ((RectTransform)field.transform).sizeDelta = new Vector2(96f, 0f);
            field.contentType = TMP_InputField.ContentType.DecimalNumber;
            field.onEndEdit.AddListener(onType);
            return (slider, field);
        }
    }
}
