using System.Globalization;
using System.Linq;
using Protobot.Interface;
using Protobot.Motion;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Devices {
    // V2 (build 79): the robot's air (Motion/AirSupply): what its tanks are charged to, the pressure now while simulating,
    // a legacy regulator's setting and about how many moves the tanks give.
    public partial class DevicesPanel {
        private TMPro.TextMeshProUGUI airValue;
        private Image airFill;

        // The Air tile beside the battery (when the build has cylinders).
        private void AirTile() {
            airValue = null;
            if (Controls.Cylinders().Count == 0) return;
            var tile = Ui.Image(tabs, "Air", Theme.PanelRaised, 8f);
            Ui.Column(tile.rectTransform, 2f, new RectOffset(10, 10, 6, 6));
            Ui.Text(tile.transform, "Label", "Air", Theme.FontSmall - 1f, Theme.TextDim).Size(-1, 16f);
            var row = Ui.Rect("Row", tile.transform);
            Ui.Row(row, 6f);
            row.Size(-1, 20f);
            var track = Ui.Image(row, "Track", Theme.Field, 3f);
            track.Size(-1, 10f, 1f);
            airFill = Ui.Image(track.transform, "Fill", Theme.Accent, 3f);
            airFill.rectTransform.anchorMin = Vector2.zero;
            airFill.rectTransform.anchorMax = Vector2.one;
            airFill.rectTransform.offsetMin = airFill.rectTransform.offsetMax = Vector2.zero;
            airValue = Ui.Text(row, "Value", "", Theme.FontSmall, Theme.Text, Theme.Medium);
            airValue.Size(54f, 20f);
            Ui.Tip(tile, "Air", null, "The pressure in the robot's air tanks. Every cylinder move takes some, and a cylinder pushes with what's left (12 lbf at 100 psi).");
        }

        private void UpdateAirTile() {
            if (airValue == null) return;
            var (psi, unlimited) = AirNow();
            airValue.text = unlimited ? "∞" : Mathf.RoundToInt(psi) + " psi";
            airFill.rectTransform.anchorMax = new Vector2(Mathf.Max(0.02f, unlimited ? 1f : psi / AirSupply.MaxPsi), 1f);
            airFill.color = unlimited || psi > 50f ? Theme.Accent : psi > 30f ? Theme.Warning : Theme.Danger;
        }

        // The tank pressure now: this computer's robot's supply while simulating, else what the tanks are charged to.
        private static (float psi, bool unlimited) AirNow() {
            if (Sandbox.Running) {
                var supply = AirSupply.All.FirstOrDefault(s => s.robot == Sandbox.LocalRobot) ?? AirSupply.All.FirstOrDefault();
                return supply == null ? (AirSupply.MaxPsi, true) : (supply.psi, false);
            }
            var (tanks, _) = AirSupply.InBuild();
            return (AirSupply.FillPsi(tanks), tanks.Count == 0);
        }

        private static string AirSignature() {
            var (tanks, regulator) = AirSupply.InBuild();
            return tanks.Count + ":" + AirSupply.FillPsi(tanks) + ":" + (regulator != null ? AirSupply.RegulatorPsi(regulator).ToString(CultureInfo.InvariantCulture) : "-");
        }

        // The card above the solenoids.
        private void AirCard() {
            var (tanks, regulator) = AirSupply.InBuild();
            var cylinders = Controls.Cylinders().Where(c => c != null).ToList();
            if (cylinders.Count == 0 && tanks.Count == 0) return;
            Heading("Air");
            var card = Box(content, "Air", Theme.PanelRaised, 8f).rect;
            Ui.Column(card, 6f, new RectOffset(10, 10, 8, 8));
            if (tanks.Count == 0) {
                Note(card, "No air tank: the cylinders have unlimited air at 100 psi. Add a reservoir (Pneumatics > Reservoirs) and the air runs down as they move, as on a real robot (1 or 2 tanks, charged before the match).", null, 52f);
                return;
            }
            var volume = tanks.Sum(AirSupply.TankMl);
            var fill = AirSupply.FillPsi(tanks);
            var reg = AirSupply.RegulatorPsi(regulator);

            // The pressure now (live while simulating).
            var gauge = ButtonRow(card);
            Ui.Text(gauge, "Label", "Pressure", Theme.FontSmall, Theme.TextDim).Size(80f, 28f);
            var track = Ui.Image(gauge, "Track", Theme.Field, 4f);
            track.Size(-1, 12f, 1f);
            var bar = Ui.Image(track.transform, "Fill", Theme.Accent, 4f);
            bar.rectTransform.anchorMin = Vector2.zero;
            bar.rectTransform.offsetMin = bar.rectTransform.offsetMax = Vector2.zero;
            var value = Ui.Text(gauge, "Value", "", Theme.FontBody, Theme.Text, Theme.Medium);
            value.Size(70f, 28f);
            live.Add(() => {
                var (psi, _) = AirNow();
                bar.rectTransform.anchorMax = new Vector2(Mathf.Max(0.02f, psi / AirSupply.MaxPsi), 1f);
                bar.color = psi > 50f ? Theme.Accent : psi > 30f ? Theme.Warning : Theme.Danger;
                var supply = Sandbox.Running ? AirSupply.All.FirstOrDefault(s => s.robot == Sandbox.LocalRobot) ?? AirSupply.All.FirstOrDefault() : null;
                value.text = Mathf.RoundToInt(psi) + " psi" + (supply != null && supply.moves > 0 ? " · " + supply.moves : "");
            });
            Ui.Tip(gauge, "Pressure", null, "The tanks' pressure: what they're charged to while building, dropping with every cylinder move while simulating (the number after it: moves so far).");

            Stepper(card, "Charged to", fill, v => {
                foreach (var t in tanks) Dynamics.SetToken(t, "psi", Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture));
            }, "What the tanks are pumped to before the match: 100 psi at most (V5RC R25). Applies when the simulation starts.");
            if (regulator != null)
                Stepper(card, "Regulator", reg, v => Dynamics.SetToken(regulator, "reg", Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture)),
                        "The pressure regulator lets through at most this much: the cylinders push the same until the tanks drop below it, and you get more moves.");

            var moves = AirSupply.MovesToHalf(volume, fill, reg, cylinders);
            Note(card, tanks.Count + (tanks.Count == 1 ? " tank" : " tanks") + " · " + Mathf.RoundToInt(volume) + " mL · about " + moves
                       + " cylinder moves before they push with half the force", null, 34f);
            if (tanks.Count > 2) Note(card, "V5RC allows two air tanks at most (R25).", Theme.Warning);
        }

        // A label, − value +, in 5 psi steps (the wheel over it steps too).
        private void Stepper(RectTransform card, string label, float value, System.Action<float> set, string tip) {
            var row = ButtonRow(card);
            Ui.Text(row, "Label", label, Theme.FontSmall, Theme.TextDim).Size(80f, 28f);
            var minus = Ui.Button(row, "Less", "−", () => { set(Mathf.Max(5f, value - 5f)); Audio.Sounds.Play(Audio.Sounds.Tick); shown = null; }, false, 5f, Theme.FontSmall);
            minus.Size(28f, 26f);
            var text = Ui.Text(row, "Value", Mathf.RoundToInt(value) + " psi", Theme.FontBody, Theme.Text, Theme.Medium, TMPro.TextAlignmentOptions.Center);
            text.Size(70f, 26f);
            var plus = Ui.Button(row, "More", "+", () => { set(Mathf.Min(AirSupply.MaxPsi, value + 5f)); Audio.Sounds.Play(Audio.Sounds.Tick); shown = null; }, false, 5f, Theme.FontSmall);
            plus.Size(28f, 26f);
            Ui.Tip(text, label, null, tip);
        }
    }
}
