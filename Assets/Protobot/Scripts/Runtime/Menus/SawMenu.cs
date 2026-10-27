using System.Globalization;
using Protobot.Audio;
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
    /// Precise cut menu (hold right click with the saw on a part). The saw stays frozen on the part while you
    /// set exact numbers: where to cut (inches from the part's end, typed as "3 1/2", "3.5" or "7/8", snapped
    /// to the chosen step), the cut's angle about the part's X, Y and Z axes (shown on the part in red, green
    /// and blue; up to 90° each, typed angles over 360° wrap around), and which side to keep. The preview and
    /// the summary line update live.
    /// </summary>
    public class SawMenu : MonoBehaviour
    {
        [SerializeField] UiTheme theme;

        SawTool saw;
        PartBody part;
        StockProfile profile;
        GameObject root;
        TextMeshProUGUI partName;
        TextMeshProUGUI summary;
        Slider position;
        TMP_InputField positionField;
        readonly Slider[] angleSliders = new Slider[3];
        readonly TMP_InputField[] angleFields = new TMP_InputField[3];
        DropdownMenu snap;
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
            position.maxValue = Units.ToInches(profile.Length);
            root.SetActive(true);
            MenuState.Open(this);

            updating = true;
            snap.SetIndex(SawTool.SnapChoice, false);
            SetPosition(Units.ToInches(saw.LockedT - profile.Start), false);
            for (var i = 0; i < 3; i++)
                ShowAngle(i, saw.Angles[i]);
            side.SetIndex(saw.Flip ? 1 : 0, false);
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
            if (!IsOpen)
                return;
            if (Keyboard.current.escapeKey.wasPressedThisFrame && MenuState.ChangedFrame != Time.frameCount)
            {
                if (snap.IsOpen)
                {
                    snap.Close();
                    return;
                }
                saw.CancelMenu();
                Close();
                return;
            }
            var plan = saw.CurrentPlan;
            summary.text = plan.valid
                ? $"Keeps {SawTool.Describe(plan.keptLength, part)}   ·   cuts off {SawTool.Describe(plan.removedLength, part)}"
                : plan.problem;
            summary.color = plan.valid ? theme.textDim : new Color(1f, 0.55f, 0.45f);
        }

        void SetPosition(float inches, bool push)
        {
            var step = SawTool.SnapChoices[snap.Index].inches;
            if (step > 0f)
                inches = Mathf.Round(inches / step) * step;
            inches = Mathf.Clamp(inches, 0f, Units.ToInches(profile.Length));
            position.SetValueWithoutNotify(inches);
            positionField.SetTextWithoutNotify(Units.Inches(Units.FromInches(inches), false));
            if (push)
                Push();
        }

        void SetAngle(int axis, float degrees)
        {
            ShowAngle(axis, Mathf.Clamp(WrapAngle(degrees), -SawTool.MaxAngle, SawTool.MaxAngle));
            Push();
        }

        void ShowAngle(int axis, float degrees)
        {
            angleSliders[axis].SetValueWithoutNotify(degrees);
            angleFields[axis].SetTextWithoutNotify(degrees.ToString("0.#", CultureInfo.InvariantCulture));
        }

        /// <summary>Sends the menu's numbers to the saw, which updates the preview.</summary>
        void Push()
        {
            if (updating || saw == null)
                return;
            var angles = new Vector3(angleSliders[0].value, angleSliders[1].value, angleSliders[2].value);
            saw.SetMenuValues(profile.Start + Units.FromInches(position.value), angles, side.Index == 1);
        }

        static float WrapAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            if (degrees < -180f) degrees += 360f;
            return degrees;
        }

        static bool TryParseAngle(string text, out float value) =>
            float.TryParse(text.Replace("°", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        void Build()
        {
            root = UiFactory.Rect("Root", transform).Fill().gameObject;
            var panel = UiFactory.Panel(root.transform, "Panel", theme, theme.panel, true).rectTransform;
            panel.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-60f, 0f), new Vector2(580f, 720f));

            var stripes = UiFactory.Hazard(panel, "Stripes", theme);
            stripes.rectTransform.anchorMin = new Vector2(0f, 1f);
            stripes.rectTransform.anchorMax = new Vector2(1f, 1f);
            stripes.rectTransform.offsetMin = new Vector2(14f, -24f);
            stripes.rectTransform.offsetMax = new Vector2(-14f, -12f);
            var title = UiFactory.Text(panel, "Title", theme, "PRECISE CUT", 44f, theme.text, true);
            title.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -30f), new Vector2(500f, 56f));
            partName = UiFactory.Text(panel, "Part", theme, "", 22f, theme.textDim);
            partName.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -84f), new Vector2(510f, 30f));

            var list = UiFactory.Rect("Rows", panel).Fill(32f, 110f, 32f, 120f);
            UiFactory.VerticalList(list, 6f);

            (position, positionField) = NumberRow(list, "Position from end (in)", 0f, 17.5f, v => SetPosition(v, true), text =>
            {
                if (Units.TryParseInches(text, out var v))
                    SetPosition(v, true);
                else
                    SetPosition(position.value, false);
            });
            snap = DropdownMenu.Create(UiFactory.Row(list, "Snap", theme), theme, System.Array.ConvertAll(SawTool.SnapChoices, c => c.label),
                SawTool.SnapChoice, i =>
                {
                    SawTool.SnapChoice = i;
                    SetPosition(position.value, true);
                    Sounds.Play(Sounds.Tick, 0.3f);
                });

            string[] names = { "X angle (°)", "Y angle (°)", "Z angle (°)" };
            Color[] colors = { new(0.95f, 0.35f, 0.3f), new(0.4f, 0.85f, 0.4f), new(0.4f, 0.6f, 1f) };
            for (var i = 0; i < 3; i++)
            {
                var axis = i;
                (angleSliders[i], angleFields[i]) = NumberRow(list, names[i], -SawTool.MaxAngle, SawTool.MaxAngle, v => SetAngle(axis, v), text =>
                {
                    if (TryParseAngle(text, out var v))
                        SetAngle(axis, v);
                    else
                        ShowAngle(axis, angleSliders[axis].value);
                });
                angleSliders[i].wholeNumbers = true;
                // The axis colour, matching the lines drawn on the part.
                var label = angleSliders[i].transform.parent.parent.Find("Label").GetComponent<TextMeshProUGUI>();
                label.text = $"<color=#{ColorUtility.ToHtmlStringRGB(colors[i])}>{names[i].Substring(0, 1)}</color>{names[i].Substring(1)}";
            }
            side = OptionSelector.Create(UiFactory.Row(list, "Keep", theme), theme, new[] { "Near side", "Far side" }, 0, _ => Push());

            summary = UiFactory.Text(list, "Summary", theme, "", 21f, theme.textDim, true);
            summary.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            var help = UiFactory.Text(list, "Help", theme, "The red see-through piece is what gets cut off.", 19f, theme.textDim);
            help.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            var cut = UiFactory.Button(panel, "Cut", theme, "CUT", () =>
            {
                if (!saw.CurrentPlan.valid)
                {
                    Sounds.Play(Sounds.Error, 0.5f);
                    return;
                }
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
            field.contentType = TMP_InputField.ContentType.Standard;  // fractions like "3 1/2"
            field.onEndEdit.AddListener(onType);
            return (slider, field);
        }
    }
}
