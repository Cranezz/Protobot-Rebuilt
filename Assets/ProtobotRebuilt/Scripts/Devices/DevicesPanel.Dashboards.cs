using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using Protobot.Motion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Devices {
    // V2: what each device's card shows when it's opened: the Brain's dashboard for it, and its settings.
    public partial class DevicesPanel {
        // ------------------------------------------------------------------ motors

        private void MotorDashboard(RectTransform card, MotorDevice m) {
            var motor = m.motor;
            if (motor == null) return;
            // Gears, revolutions, degrees (the Brain's top row).
            var top = TileRow(card);
            var (gears, gearsText) = Tile(top, "Gears", () => m.Gearing + "  ·  " + motor.Rpm + " rpm", 1.3f,
                motor.HasCartridge ? "The cartridge: red 36:1 (100 rpm, 2.1 N·m: arms, lifts), green 18:1 (200 rpm, 1.05 N·m: drives), blue 6:1 (600 rpm, 0.35 N·m: flywheels, intakes). Click to change it."
                                   : "The 5.5W motor has no cartridge: 200 rpm, 0.5 N·m.");
            gearsText.color = motor.HasCartridge ? Color.Lerp(motor.Color, Color.white, 0.35f) : Theme.Text;
            if (motor.HasCartridge) {
                var button = gears.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                var rect = gears.rectTransform;
                button.onClick.AddListener(() => {
                    var labels = MotorDrive.Cartridges.Select(r => (r == 100 ? "Red 36:1" : r == 600 ? "Blue 6:1" : "Green 18:1") + " · " + r + " rpm").ToArray();
                    Popup.Open(rect, labels, Array.IndexOf(MotorDrive.Cartridges, motor.Rpm), i => {
                        motor.SetCartridge(MotorDrive.Cartridges[i], false);
                        Audio.Sounds.Play(Audio.Sounds.Tick);
                        shown = null;
                    });
                });
            }
            Tile(top, "Revolutions", () => F(m.Revolutions, "0.000"), 1f, "Turns of its shaft since the robot was switched on (or Zero).");
            Tile(top, "Degrees", () => Mathf.RoundToInt((float)m.Degrees).ToString(), 1f, "The same, in degrees.");

            // Velocity, with a bar from -100% to 100%, and the current.
            var middle = TileRow(card, 54f);
            var (velocity, _) = Tile(middle, "Velocity", () => Mathf.RoundToInt(m.Rpm) + " rpm  (" + Mathf.RoundToInt(m.Percent) + "%)", 2.2f);
            var track = Ui.Image(velocity.transform, "Track", Theme.PanelRaised, 3f);
            track.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(0f, 6f));
            track.rectTransform.anchorMin = new Vector2(0f, 0f);
            track.rectTransform.anchorMax = new Vector2(1f, 0f);
            track.rectTransform.offsetMin = new Vector2(8f, 4f);
            track.rectTransform.offsetMax = new Vector2(-8f, 10f);
            track.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var fill = Ui.Image(track.transform, "Fill", Theme.Accent, 2f);
            var middleMark = Ui.Image(track.transform, "Zero", Theme.TextFaint);
            middleMark.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            middleMark.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            middleMark.rectTransform.sizeDelta = new Vector2(2f, 4f);
            live.Add(() => {
                var p = Mathf.Clamp(m.Percent / 100f, -1f, 1f);
                fill.rectTransform.anchorMin = new Vector2(0.5f + Mathf.Min(0f, p) / 2f, 0f);
                fill.rectTransform.anchorMax = new Vector2(0.5f + Mathf.Max(0f, p) / 2f, 1f);
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                fill.color = p >= 0f ? Theme.Accent : Theme.Warning;
            });
            Tile(middle, "Current", () => F(m.Current, "0.00") + " A", 1f, "What it draws (2.5 A at stall for the 11W motor).");

            // Power and torque against speed, the Brain's two graphs.
            var graphs = TileRow(card, 118f);
            Graph(graphs, "Power", () => F(m.Power, "0.0") + " W", x => 4f * x * (1f - x), () => Mathf.Abs(m.Percent) / 100f,
                  () => m.Power / Mathf.Max(0.01f, m.StallTorque * m.FreeRpm * Mathf.PI / 30f / 4f), "W", F(m.StallTorque * m.FreeRpm * Mathf.PI / 30f / 4f, "0.0"));
            Graph(graphs, "Torque", () => F(Mathf.Abs(m.Torque), "0.00") + " N·m", x => 1f - x, () => Mathf.Abs(m.Percent) / 100f,
                  () => Mathf.Abs(m.Torque) / Mathf.Max(0.01f, m.StallTorque), "N·m", F(m.StallTorque, "0.00"));

            // Settings: name, what runs it, reversed.
            var nameRow = ButtonRow(card);
            Ui.Text(nameRow, "Label", "Name", Theme.FontSmall, Theme.TextDim).Size(44f, 28f);
            var field = Ui.Field(nameRow, "Name", Controls.GivenName(motor.gameObject) ?? "", value => Controls.SetName(motor.gameObject, value), Theme.FontSmall);
            field.Size(-1, 26f, 1f);
            Ui.Tip(field, "Name", null, "A name for this motor (\"Left drive\", \"Arm\"); empty: Motor " + motor.Number + ".");
            var reversed = Controls.Reversed(motor.gameObject);
            var rev = Ui.Button(nameRow, "Reversed", reversed ? "Reversed" : "Normal", () => {
                Controls.SetReversed(motor.gameObject, !Controls.Reversed(motor.gameObject));
                Audio.Sounds.Play(Audio.Sounds.Tick);
            }, reversed, 5f, Theme.FontSmall);
            rev.Size(80f, 26f);
            Ui.Tip(rev, "Direction", null, "Reversed: forward turns it the other way (the two sides of a drive face opposite ways).");
            var zero = Ui.Button(nameRow, "Zero", "Zero", () => {
                m.Zero();
                Audio.Sounds.Play(Audio.Sounds.Tick);
            }, false, 5f, Theme.FontSmall);
            zero.Size(56f, 26f);
            Ui.Tip(zero, "Zero", null, "Revolutions and degrees start from 0 here.");

            var runs = ButtonRow(card);
            runs.Size(-1, 30f);
            Ui.Text(runs, "Label", "Runs from", Theme.FontSmall, Theme.TextDim).Size(70f, 28f);
            var side = Controls.Side(motor.gameObject);
            var holder = Ui.Rect("Role", runs);
            Ui.Row(holder, 0f);
            holder.Size(170f, 28f);
            ToolOptions.Segmented(holder, new[] { "Keys", "Left", "Right" }, side == "L" ? 1 : side == "R" ? 2 : 0, i => {
                Controls.SetSide(motor.gameObject, i == 1 ? "L" : i == 2 ? "R" : "");
                Audio.Sounds.Play(Audio.Sounds.Tick);
            });
            holder.Size(-1, 28f, 1f);
            if (side == "") {
                var keys = ButtonRow(card);
                Ui.Text(keys, "Label", "Keys", Theme.FontSmall, Theme.TextDim).Size(70f, 28f);
                Ui.Text(keys, "Forward", "Forward", Theme.FontSmall - 1f, Theme.TextFaint).Size(52f, 28f);
                MotionUi.KeyChip(keys, Controls.ForwardKey(motor.gameObject), key => SetKey(motor, key, true), "Forward key", "Hold it to run " + motor.Label + " forward.").Size(54f, 26f);
                Ui.Text(keys, "Reverse", "Reverse", Theme.FontSmall - 1f, Theme.TextFaint).Size(52f, 28f);
                MotionUi.KeyChip(keys, Controls.ReverseKey(motor.gameObject), key => SetKey(motor, key, false), "Reverse key", "Hold it to run " + motor.Label + " in reverse.").Size(54f, 26f);
                var speedRow = ButtonRow(card);
                Ui.Text(speedRow, "Label", "Key speed", Theme.FontSmall, Theme.TextDim).Size(70f, 28f);
                MotionUi.Slider(speedRow, Controls.Speed(motor.gameObject), 0.05f, 0.05f, v => Mathf.RoundToInt(v * 100f) + "%  ·  " + Mathf.RoundToInt(v * motor.Rpm) + " rpm",
                    v => Controls.SetSpeed(motor.gameObject, v)).Size(-1, 26f, 1f);
            }
            else Ui.Text(runs, "Drive", side == "L" ? "the left stick" : "the right stick", Theme.FontSmall, Theme.TextDim).Size(-1, 28f, 1f);

            // What it turns.
            var system = GearTrain.SystemOf(motor, GearTrain.Current());
            if (system == null) Note(card, "Nothing to turn yet: put a shaft in its square hole.", Theme.TextFaint);
            else if (system.jammed) Note(card, "Can't turn: " + system.jamReason, Theme.Danger, 34f);
            else {
                var shafts = system.rotors.Count;
                var others = system.Motors.Where(o => o != motor).Select(o => o.Label).ToList();
                Note(card, shafts + (shafts == 1 ? " shaft turns" : " shafts turn") + (others.Count > 0 ? ", with " + string.Join(", ", others) : "") + ".", Theme.TextFaint);
            }
        }

        private static void SetKey(MotorDrive motor, Key key, bool forward) {
            var shortcut = key == Key.None ? null : Controls.ShortcutOn(key);
            if (shortcut != null) {
                Interface.Interface.Say(Controls.KeyName(key) + " is the shortcut for " + shortcut + ": pick another key", false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                return;
            }
            if (forward) Controls.SetForwardKey(motor.gameObject, key);
            else Controls.SetReverseKey(motor.gameObject, key);
            if (Controls.DriveKey(key)) Interface.Interface.Say(motor.Label + ": " + Controls.KeyName(key) + " also drives the robot in the simulation", false);
            Audio.Sounds.Play(Audio.Sounds.Tick);
        }

        // A graph of a motor curve (x: speed 0..1 of free speed, y: 0..1 of its top) with a dot where it is now.
        private void Graph(RectTransform row, string title, Func<string> value, Func<float, float> curve, Func<float> x, Func<float> y, string unit, string top) {
            var tile = Box(row, title, Theme.Field, 6f).image;
            ((RectTransform)tile.transform.parent).Size(-1, -1, 1f);
            var label = Ui.Text(tile.transform, "Title", title, Theme.FontSmall - 2f, Theme.TextDim);
            label.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -3f), new Vector2(80f, 15f));
            var text = Ui.Text(tile.transform, "Value", value(), Theme.FontBody, Theme.Text, Theme.Medium, TextAlignmentOptions.MidlineRight);
            text.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -2f), new Vector2(120f, 20f));
            var area = Ui.Rect("Plot", tile.transform);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(30f, 20f);
            area.offsetMax = new Vector2(-10f, -26f);
            var shapes = ShapeGraphic.Create(area);
            var axis = Ui.Text(tile.transform, "Axis", "0        speed (%)        100", Theme.FontSmall - 4f, Theme.TextFaint, null, TextAlignmentOptions.Center);
            axis.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(10f, 3f), new Vector2(200f, 14f));
            var topLabel = Ui.Text(tile.transform, "Top", top, Theme.FontSmall - 4f, Theme.TextFaint, null, TextAlignmentOptions.MidlineRight);
            topLabel.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(2f, -22f), new Vector2(26f, 12f));
            var lastDot = new Vector2(-1f, -1f);
            var lastSize = Vector2.zero;
            live.Add(() => {
                text.text = value();
                var r = area.rect;
                var dot = new Vector2(Mathf.Clamp01(x()), Mathf.Clamp01(y()));
                if ((dot - lastDot).sqrMagnitude < 1e-5f && r.size == lastSize) return;
                lastDot = dot;
                lastSize = r.size;
                shapes.Clear();
                Vector2 P(float u, float v) => new(r.xMin + u * r.width, r.yMin + v * r.height);
                shapes.Line(P(0f, 0f), P(1f, 0f), Theme.TextFaint, 1f);
                shapes.Line(P(0f, 0f), P(0f, 1f), Theme.TextFaint, 1f);
                shapes.Dashed(P(0.5f, 0f), P(0.5f, 1f), new Color(1f, 1f, 1f, 0.08f), 1f, 3f, 3f);
                var points = new List<Vector2>();
                for (var i = 0; i <= 32; i++) points.Add(P(i / 32f, curve(i / 32f)));
                shapes.Polyline(points, Theme.Accent, 2f);
                shapes.Disc(P(dot.x, dot.y), 4.5f, Color.white);
                shapes.Disc(P(dot.x, dot.y), 3f, Theme.Warning);
            });
        }

        // ------------------------------------------------------------------ sensors

        private void DistanceDashboard(RectTransform card, DistanceSensor s) {
            var row = TileRow(card);
            var (big, _) = Tile(row, "Distance", () => s.Found ? DevicesFormat.Distance(s.Mm) : "—", 1.4f, "Click to show it in " + (DevicesFormat.Millimeters ? "inches" : "millimeters") + ". Range 20-2000 mm (0.8\"-78.7\").");
            var button = big.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => {
                DevicesFormat.Millimeters = !DevicesFormat.Millimeters;
                Audio.Sounds.Play(Audio.Sounds.Tick);
            });
            Tile(row, "Size", () => s.Found ? s.Size : "—", 1f, "How big the object looks to it: small, medium or large.");
            Tile(row, "Velocity", () => s.Found ? F(s.Velocity, "0.00") + " m/s" : "—", 1f, "How fast the object comes closer (moving away: below 0).");
            // The Brain's picture: the sensor, a scale to 2 m, the object and an arrow when it's coming closer.
            var picture = Box(card, "Picture", Theme.Field, 6f).image;
            ((RectTransform)picture.transform.parent).Size(-1, 54f);
            var area = Ui.Rect("Plot", picture.transform).Fill(14f, 8f, 14f, 8f);
            var shapes = ShapeGraphic.Create(area);
            var last = float.NaN;
            live.Add(() => {
                var key = s.Found ? s.Mm + s.Velocity * 1000f : -1f;
                if (Mathf.Abs(key - last) < 0.5f) return;
                last = key;
                shapes.Clear();
                var r = area.rect;
                var y = r.yMin + r.height * 0.35f;
                var x0 = r.xMin + 34f;
                var width = r.xMax - x0;
                shapes.Quad(new Vector2(r.xMin, y - 8f), new Vector2(r.xMin, y + 14f), new Vector2(r.xMin + 22f, y + 14f), new Vector2(r.xMin + 22f, y - 8f), Theme.TextDim);
                shapes.Line(new Vector2(x0, y - 12f), new Vector2(r.xMax, y - 12f), Theme.TextFaint, 1f);
                for (var m = 0; m <= 4; m++) shapes.Line(new Vector2(x0 + width * m / 4f, y - 12f), new Vector2(x0 + width * m / 4f, y - 7f), Theme.TextFaint, 1f);
                if (!s.Found) return;
                var at = x0 + width * Mathf.Clamp01(s.Mm / DistanceSensor.MaxMm);
                shapes.Line(new Vector2(r.xMin + 24f, y + 3f), new Vector2(at - 9f, y + 3f), Theme.Accent, 1.5f);
                shapes.Disc(new Vector2(at, y + 3f), 8f, Theme.TextDim, 20);
                if (s.Velocity > 0.03f) {
                    var tip = new Vector2(at + 12f, y + 3f);
                    shapes.Triangle(tip, tip + new Vector2(10f, 7f), tip + new Vector2(10f, -7f), Theme.Danger);
                    shapes.Line(tip + new Vector2(9f, 0f), tip + new Vector2(28f, 0f), Theme.Danger, 4f);
                }
            });
        }

        private void OpticalDashboard(RectTransform card, OpticalSensor s) {
            var row = TileRow(card, 56f);
            var (swatch, _) = Tile(row, "Color", () => s.ColorName, 1f, "The color it sees (its hue, if something is near).");
            var chip = Ui.Image(swatch.transform, "Swatch", Color.black, 4f);
            chip.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, -4f), new Vector2(22f, 22f));
            chip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            live.Add(() => chip.color = s.Seen);
            Tile(row, "Hue", () => s.Near ? F(s.Hue, "0") + "°" : "—", 1f, "0-359.9°: red 0, green 120, blue 240.");
            Tile(row, "Brightness", () => F(s.Brightness, "0") + "%", 1f);
            Tile(row, "Proximity", () => F(s.Proximity, "0") + (s.Near ? "  near" : ""), 1f, "0-255: more as something comes closer (about 4\" at most; shiny things read closer).");
            var leds = ButtonRow(card);
            Ui.Text(leds, "Label", "LED", Theme.FontSmall, Theme.TextDim).Size(44f, 28f);
            var value = Ui.Text(leds, "Value", s.Led + "%", Theme.FontSmall, Theme.Text, Theme.Medium);
            value.Size(48f, 28f);
            foreach (var (label, by) in new[] { ("−", -25), ("+", 25) }) {
                var b = Ui.Button(leds, label, label, () => {
                    s.Led = Mathf.Clamp(s.Led + by, 0, 100);
                    value.text = s.Led + "%";
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                }, false, 5f, Theme.FontBody);
                b.Size(30f, 26f);
            }
            Ui.Tip(leds, "LED", null, "Its white light: colors read better close up with it on.");
        }

        private void ShaftDashboard(RectTransform card, ShaftSensor s) {
            var row = TileRow(card, 70f);
            // A dial: where the shaft is.
            var dial = Ui.Image(row, "Dial", Theme.Field, 6f);
            dial.Size(70f, 70f, 0f);
            var shapes = ShapeGraphic.Create(dial.transform);
            var lastAngle = float.NaN;
            live.Add(() => {
                if (Mathf.Abs(Mathf.DeltaAngle(s.Angle, lastAngle)) < 0.3f) return;
                lastAngle = s.Angle;
                shapes.Clear();
                var r = ((RectTransform)dial.transform).rect;
                var c = r.center;
                shapes.Ring(c, 26f, Theme.TextFaint, 2f, 40);
                var a = (90f - s.Angle) * Mathf.Deg2Rad;
                shapes.Line(c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 24f, s.HasShaft ? Theme.Accent : Theme.TextFaint, 3f);
                shapes.Disc(c, 3.5f, Theme.Text);
            });
            if (s.kind == DeviceKind.Encoder) {
                Tile(row, "Count", () => s.Ticks.ToString(), 1f, "360 ticks a turn, counted since the robot was switched on.");
                Tile(row, "Turns", () => F((float)(s.Position / 360.0), "0.00"), 1f);
            }
            else if (s.kind == DeviceKind.Potentiometer) {
                Tile(row, "Angle", () => F(Mathf.Clamp(s.Angle, 0f, ShaftSensor.PotRange), "0.0") + "°", 1f, "0-333° between its stops (past them it reads the stop).");
                Tile(row, "Value", () => s.PotValue.ToString(), 1f, "Analog in, 0-4095.");
            }
            else {
                Tile(row, "Angle", () => F(s.Angle, "0.00") + "°", 1f, "0-359.99°, from its zero (kept in the sensor, like the real one).");
                Tile(row, "Position", () => F((float)(s.Position / 360.0), "0.000") + " rev", 1f, "Turns since the robot was switched on, not wrapped.");
            }
            Tile(row, "Velocity", () => F(s.Rpm, "0") + " rpm", 1f);
            var buttons = ButtonRow(card);
            if (!s.HasShaft) Note(buttons, "No shaft in it: put one through its square hole.", Theme.Warning).Size(-1, 28f, 1f);
            else Ui.Rect("Space", buttons).Size(-1, 26f, 1f);
            if (s.kind != DeviceKind.Potentiometer) {
                var zero = Ui.Button(buttons, "Zero", s.kind == DeviceKind.Rotation ? "Set zero" : "Reset", () => {
                    s.SetZero();
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                }, false, 5f, Theme.FontSmall);
                zero.Size(80f, 26f);
            }
            if (s.kind == DeviceKind.Rotation) {
                var rev = Ui.Button(buttons, "Reversed", s.IsReversed ? "Reversed" : "Normal", () => {
                    s.ToggleReversed();
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                }, s.IsReversed, 5f, Theme.FontSmall);
                rev.Size(80f, 26f);
            }
        }

        private void InertialDashboard(RectTransform card, InertialSensor s) {
            var row = TileRow(card, 70f);
            var dial = Ui.Image(row, "Dial", Theme.Field, 6f);
            dial.Size(70f, 70f, 0f);
            var shapes = ShapeGraphic.Create(dial.transform);
            var last = float.NaN;
            live.Add(() => {
                if (Mathf.Abs(Mathf.DeltaAngle(s.Heading, last)) < 0.3f) return;
                last = s.Heading;
                shapes.Clear();
                var c = ((RectTransform)dial.transform).rect.center;
                shapes.Ring(c, 26f, Theme.TextFaint, 2f, 40);
                var a = (90f - s.Heading) * Mathf.Deg2Rad;
                var tip = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 24f;
                shapes.Line(c, tip, Theme.Accent, 3f);
                shapes.Disc(tip, 3.5f, Theme.Accent);
            });
            Tile(row, "Heading", () => F(s.Heading, "0.0") + "°", 1f, "0-359.99°, clockwise from where it was calibrated.");
            Tile(row, "Rotation", () => F((float)s.Rotation, "0.0") + "°", 1f, "The same, not wrapped (two turns: 720°).");
            Tile(row, "Turn rate", () => F(s.Rate, "0") + "°/s", 1f);
            var second = TileRow(card);
            Tile(second, "Pitch", () => F(s.Pitch, "0.0") + "°", 1f);
            Tile(second, "Roll", () => F(s.Roll, "0.0") + "°", 1f);
            Tile(second, "Yaw", () => F(s.Yaw, "0.0") + "°", 1f);
            Tile(second, "Accel (g)", () => F(s.Accel.x, "0.00") + " " + F(s.Accel.y, "0.00") + " " + F(s.Accel.z, "0.00"), 1.6f, "x, y, z in its own axes (1 g up when it's still).");
            var buttons = ButtonRow(card);
            Ui.Rect("Space", buttons).Size(-1, 26f, 1f);
            var calibrate = Ui.Button(buttons, "Calibrate", "Calibrate", () => {
                s.Calibrate();
                Audio.Sounds.Play(Audio.Sounds.Tick);
            }, false, 5f, Theme.FontSmall);
            calibrate.Size(90f, 26f);
            Ui.Tip(calibrate, "Calibrate", null, "Heading 0 is where it points now (it calibrates when the robot is switched on).");
        }

        private void ThreeWireDashboard(RectTransform card, V5Device d) {
            var row = TileRow(card);
            for (var i = 0; i < d.ports; i++) {
                var index = i;
                Tile(row, "Port " + (char)('A' + d.port - 1 + i) + " · " + d.PortState(index).mode, () => d.PortState(index).value, 1f);
            }
            switch (d) {
                case LightSensor l: Tile(row, "Light", () => F(l.Percent, "0") + "%", 1f, "0 (bright) to 4095 (dark) on the port."); break;
                case LineTracker t: Tile(row, "Reflected", () => F(t.Reflectivity, "0") + "%", 1f, "Light surfaces close by reflect most (0 on the port); best 1/8\" to 1/4\" away."); break;
                case SwitchSensor sw: Tile(row, "Switch", () => sw.Pressed ? "Pressed" : "Released", 1f); break;
                case UltrasonicSensor u: Tile(row, "Distance", () => u.Inches < 0f ? "No echo" : DevicesFormat.Inches(u.Inches), 1f, "A sound cone, 1.5\" to 115\"."); break;
                case YawGyro g: Tile(row, "Heading", () => F(g.Heading, "0.0") + "°", 1f); break;
            }
        }

        private void SolenoidDashboard(RectTransform card, SolenoidDevice s) {
            var row = TileRow(card);
            Tile(row, "Mode", () => "Digital Out", 1f);
            Tile(row, "Value", () => s.High ? "High (1)" : "Low (0)", 1f, "High: air extends the cylinders; Low (the default when the robot is switched on): retracts them.");
            if (s.cylinders.Count == 0) {
                Note(card, "No cylinder on its tubing: run tubing from this valve to a cylinder's fittings.", Theme.TextFaint, 34f);
                return;
            }
            foreach (var c in s.cylinders.Where(c => c != null)) {
                var line = Ui.Rect(c.Label, card);
                Ui.Row(line, 8f);
                line.Size(-1, 52f);
                var info = Ui.Rect("Info", line);
                Ui.Column(info, 0f);
                info.Size(-1, 50f, 1f);
                Ui.Text(info, "Name", c.Label + "  ·  " + c.Size + " stroke", Theme.FontSmall, Theme.Text, Theme.Medium).Size(-1, 22f);
                var position = Ui.Text(info, "Position", SolenoidDevice.Position(c), Theme.FontSmall, Theme.TextDim);
                position.Size(-1, 22f);
                var cylinder = c;
                live.Add(() => position.text = "Position: " + SolenoidDevice.Position(cylinder));
                MotionUi.KeyChip(line, Controls.CylinderKey(c.gameObject), key => {
                    Controls.SetCylinderKey(cylinder.gameObject, key);
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                }, "Key", "Press it: " + PneumaticCylinder.KeyAction(c.Ports) + ".").Size(54f, 26f);
                var picker = AirPicker.Create(line, c, () => {
                    if (Sandbox.Running && Sandbox.Instance != null) Sandbox.Instance.SetPorts(cylinder);
                    shown = null;
                }, 150f, 50f);
                picker.GetComponent<LayoutElement>().flexibleWidth = 0f;
            }
        }

        // ------------------------------------------------------------------ the 3-wire page

        private void ThreeWirePage() {
            var devices = V5Brain.Devices.Where(d => d.threeWire && d.OnBrain).ToList();
            var grid = Ui.Rect("Ports", content);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2((width - 24f - 6f - 6f) / 2f, 64f);
            layout.spacing = new Vector2(6f, 6f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 2;
            layout.startAxis = GridLayoutGroup.Axis.Vertical;   // A-D down the left, E-H down the right, as the Brain
            grid.Size(-1, 64f * 4f + 18f);
            for (var p = 1; p <= 8; p++) {
                var device = devices.FirstOrDefault(d => p >= d.port && p < d.port + d.ports);
                WireCell(grid, p, device);
            }
            Note(content, "Click a solenoid's port to switch it (Low: retracted, the default; High: extended); click a sensor's to see it.", Theme.TextFaint, 34f);
        }

        private void WireCell(RectTransform grid, int port, V5Device device) {
            var letter = ((char)('A' + port - 1)).ToString();
            var cell = Ui.Image(grid, "Port " + letter, device != null ? Theme.PanelRaised : Theme.Field, 6f, true);
            // The letter in a triangle, as on the Brain.
            var mark = Ui.Rect("Mark", cell.transform);
            mark.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(36f, 34f));
            var shapes = ShapeGraphic.Create(mark);
            var color = device != null ? Theme.Warning : Theme.TextFaint;
            var drawn = false;
            live.Add(() => {
                if (drawn || shapes.rectTransform.rect.width <= 0f) return;
                drawn = true;
                shapes.Clear();
                var r = shapes.rectTransform.rect;
                shapes.Triangle(new Vector2(r.xMin, r.yMin), new Vector2(r.center.x, r.yMax), new Vector2(r.xMax, r.yMin), new Color(color.r, color.g, color.b, 0.22f));
                shapes.Polyline(new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.center.x, r.yMax), new Vector2(r.xMax, r.yMin) }, color, 1.5f, true);
            });
            var letterText = Ui.Text(mark, "Letter", letter, Theme.FontSmall, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
            letterText.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(30f, 20f));
            var mode = Ui.Text(cell.transform, "Mode", "", Theme.FontSmall - 1f, Theme.TextDim);
            mode.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(52f, -6f), new Vector2(170f, 16f));
            var value = Ui.Text(cell.transform, "Value", "", Theme.FontBody, Theme.Text, Theme.Medium);
            value.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(52f, 0f), new Vector2(170f, 22f));
            var name = Ui.Text(cell.transform, "Device", device != null ? device.Name : "Empty", Theme.FontSmall - 2f, Theme.TextFaint);
            name.rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(52f, 4f), new Vector2(170f, 16f));
            if (device == null) {
                mode.text = "Not used";
                return;
            }
            var index = port - device.port;
            live.Add(() => {
                var (m, v) = device.PortState(index);
                mode.text = m;
                value.text = v;
            });
            var button = cell.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => {
                if (device is SolenoidDevice s) s.Set(!s.High);
                else {
                    page = Page.Devices;
                    expanded.Add(device.Key);
                    focusKey = device.Key;
                }
                Audio.Sounds.Play(Audio.Sounds.Tick);
                shown = null;
            });
            Ui.Tip(cell, "Port " + letter + ": " + device.Name, null, device is SolenoidDevice ? "Click to switch the valve." : "Click to see it.");
        }

        // ------------------------------------------------------------------ the controller page

        private void ControllerPage() {
            Heading("Drivetrain");
            var mode = Ui.Rect("Mode", content);
            Ui.Row(mode, 8f);
            mode.Size(-1, 32f);
            ToolOptions.Segmented(mode, Controls.DriveModeNames, (int)Controls.Drive, i => {
                Controls.Drive = (Controls.DriveMode)i;
                Audio.Sounds.Play(Audio.Sounds.Tick);
                shown = null;
            });
            foreach (var (keys, action) in Controls.DriveLayout(Controls.Drive)) {
                var line = Ui.Rect(keys, content);
                Ui.Row(line, 8f);
                line.Size(-1, 24f);
                Ui.Text(line, "Keys", keys, Theme.FontSmall, Theme.Text, Theme.Medium).Size(110f, 22f);
                Ui.Text(line, "Action", action, Theme.FontSmall, Theme.TextDim).Size(-1, 22f, 1f);
            }
            Note(content, Gamepad.current != null ? "A gamepad is connected: its sticks drive too." : "A gamepad's sticks drive too when one is connected.", Theme.TextFaint);
            var motors = MotorDrive.Live();
            var left = motors.Where(m => Controls.Side(m.gameObject) == "L").Select(m => m.Label).ToList();
            var right = motors.Where(m => Controls.Side(m.gameObject) == "R").Select(m => m.Label).ToList();
            Heading("Drive motors");
            Note(content, "Left: " + (left.Count > 0 ? string.Join(", ", left) : "none") + "\nRight: " + (right.Count > 0 ? string.Join(", ", right) : "none"), Theme.TextDim, 40f);
            Note(content, "Open a motor's card (Devices) and pick Left or Right to put it on the drive. If a side drives backward, reverse its motors.", Theme.TextFaint, 34f);
            if (Sandbox.Running && Sandbox.Instance != null) {
                Heading("Camera");
                var cameraRow = Ui.Rect("Camera", content);
                Ui.Row(cameraRow, 8f);
                cameraRow.Size(-1, 32f);
                ToolOptions.Segmented(cameraRow, new[] { "Free", "Follow robot", "Track a part" }, (int)Sandbox.Instance.View, i => {
                    if (Sandbox.Instance != null) Sandbox.Instance.SetCamera((Sandbox.CameraMode)i);
                    shown = null;
                });
                Note(content, "Free: orbit, pan and zoom anywhere. Track a part: click a part of the robot or a game piece.", Theme.TextFaint, 34f);
            }
        }
    }
}
