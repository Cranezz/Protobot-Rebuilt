using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.Game {
    // V2: Override's scoring for Robot Skills (game manual RSC3, SC2-SC6), read from where the pieces are:
    // - A Pin is Placed when one of its halves is nested in a Goal, or in a Cup that sits on another Placed Pin (a
    //   stack: Goal, Pin, Cup, Pin, Cup...). A Cup is Placed when it sits on a Placed Pin.
    // - Each visible half of a Placed Pin is Scored: not visible when it's inside the Goal or inside a Cup's opaque half.
    // - Skills: red halves score 5 in a red Quadrant or the Midfield, blue halves 5 in a blue Quadrant or the Midfield,
    //   yellow halves 10 when Owned: in a Quadrant whose Toggle is set to that Quadrant's color, or in the Midfield Goal
    //   with the Robot ending in the Midfield. The Robot ending in the Midfield is worth 8.
    public class SkillsScore {
        public int total, redBlue, yellow, midfieldPoints;
        // V2: a match (online): each Alliance's own score; total is this player's Alliance's.
        public bool match;
        public int red, blue;
        public int scoredRedBlueHalves, scoredYellowHalves, placedPins, placedCups;
        public bool robotInMidfield;
        public readonly List<string> details = new();
        /// <summary>V2: each thing that scored and where (the end-of-run reveal lights them up one by one).</summary>
        public readonly List<ScoredItem> items = new();
    }

    public static class OverrideScoring {
        public const int AllianceHalf = 5, YellowHalf = 10, Midfield = 8;
        private const float Upright = 0.8f;      // cos 37°: a stack's pieces stand up
        private const float Reach = 1.35f;       // how far off a stack's axis a piece may sit (the holes are ~2.35" across)

        private class Stacked {
            public GameField.Goal goal;
            public bool lowerHidden;
            public bool upperHidden;
        }

        public static SkillsScore Evaluate(GameField field, IList<Pin> pins, IList<Cup> cups, IList<Toggle> toggles, bool robotInMidfield) =>
            Evaluate(field, pins, cups, toggles, robotInMidfield, false, 0, 0, true);

        /// <summary>V2: match scoring (SC3-SC6) when match: red halves score for red and blue for blue wherever they're
        /// Placed; yellow halves for the Alliance whose color the Quadrant's Toggle is set to, or (the Midfield Goal) the
        /// Alliance with more Robots in the Midfield; 8 for each Robot ending in the Midfield. total and items are
        /// mineRed's Alliance's.</summary>
        public static SkillsScore Evaluate(GameField field, IList<Pin> pins, IList<Cup> cups, IList<Toggle> toggles, bool robotInMidfield,
                                           bool match, int redInMidfield, int blueInMidfield, bool mineRed) {
            var score = new SkillsScore { robotInMidfield = robotInMidfield && !match, match = match };
            var placedPins = new Dictionary<Pin, Stacked>();
            var placedCups = new Dictionary<Cup, (GameField.Goal goal, bool opaqueDown)>();

            // Pins nested in a Goal.
            foreach (var pin in pins) {
                if (!Ends(pin, out var low, out var high)) continue;
                var tip = pin.End(low);
                var p = field.ToField(tip);
                var h = field.HeightOf(tip);
                foreach (var goal in field.goals) {
                    if ((p - goal.position).magnitude > Reach) continue;
                    if (h < goal.top - 0.15f && h > goal.top - 4.5f) {
                        placedPins[pin] = new Stacked { goal = goal, lowerHidden = true };
                        break;
                    }
                }
            }
            // Stacks: Cups on Placed Pins, Pins in Placed Cups, until nothing changes.
            for (var changed = true; changed;) {
                changed = false;
                foreach (var cup in cups) {
                    if (placedCups.ContainsKey(cup) || !CupEnds(cup, out var bottom, out var top, out var opaqueDown)) continue;
                    foreach (var (pin, stack) in placedPins) {
                        if (!Ends(pin, out _, out var high)) continue;
                        var tip = pin.End(high);
                        if (!Inside(field, tip, bottom, cup.transform.position, 0.1f, 0.6f)) continue;
                        placedCups[cup] = (stack.goal, opaqueDown);
                        if (opaqueDown) stack.upperHidden = true;
                        changed = true;
                        break;
                    }
                }
                foreach (var pin in pins) {
                    if (placedPins.ContainsKey(pin) || !Ends(pin, out var low, out _)) continue;
                    var tip = pin.End(low);
                    foreach (var (cup, info) in placedCups) {
                        if (!CupEnds(cup, out _, out var top, out var opaqueDown)) continue;
                        if (!Inside(field, tip, top, cup.transform.position, 0.1f, 0.6f)) continue;
                        // In the Cup's upper half: hidden if that half is the opaque one.
                        placedPins[pin] = new Stacked { goal = info.goal, lowerHidden = !opaqueDown };
                        changed = true;
                        break;
                    }
                }
            }
            score.placedPins = placedPins.Count;
            score.placedCups = placedCups.Count;

            // Scored halves.
            foreach (var (pin, stack) in placedPins) {
                Ends(pin, out var low, out var high);
                foreach (var (half, hidden) in new[] { (low, stack.lowerHidden), (high, stack.upperHidden) }) {
                    if (hidden) continue;
                    var color = pin.ColorOf(half);
                    var quadrant = stack.goal.quadrant;
                    var where = quadrant == Quadrant.Midfield ? "Midfield Goal" : quadrant + " Quadrant goal" + (stack.goal.alliance ? " (Alliance)" : "");
                    var middle = Vector3.Lerp(pin.transform.position, pin.End(half), 0.5f);
                    if (match) {   // V2
                        var forRed = color == PieceColor.Red;
                        if (color == PieceColor.Yellow) {
                            var owner = quadrant == Quadrant.Midfield
                                ? (redInMidfield > blueInMidfield ? PieceColor.Red : blueInMidfield > redInMidfield ? PieceColor.Blue : PieceColor.Yellow)
                                : toggles.FirstOrDefault(t => t.quadrant == quadrant)?.SetColor ?? PieceColor.Yellow;
                            if (owner != PieceColor.Red && owner != PieceColor.Blue) continue;
                            forRed = owner == PieceColor.Red;
                        }
                        else if (color != PieceColor.Blue && color != PieceColor.Red) continue;
                        var value = color == PieceColor.Yellow ? YellowHalf : AllianceHalf;
                        if (forRed) score.red += value;
                        else score.blue += value;
                        if (forRed != mineRed) continue;
                        if (color == PieceColor.Yellow) { score.scoredYellowHalves++; score.yellow += value; }
                        else { score.scoredRedBlueHalves++; score.redBlue += value; }
                        score.items.Add(new ScoredItem { point = middle, points = value, kind = color == PieceColor.Yellow ? "Yellow pin halves" : (forRed ? "Red" : "Blue") + " pin halves", where = where, pin = pin });
                        continue;
                    }
                    if (color == PieceColor.Yellow) {
                        var owned = quadrant == Quadrant.Midfield
                            ? robotInMidfield
                            : toggles.Any(t => t.quadrant == quadrant && t.SetColor == GameField.ColorOf(quadrant));
                        if (!owned) continue;
                        score.scoredYellowHalves++;
                        score.yellow += YellowHalf;
                        score.items.Add(new ScoredItem { point = middle, points = YellowHalf, kind = "Yellow pin halves", where = where, pin = pin });
                    }
                    else if (color == PieceColor.Red || color == PieceColor.Blue) {
                        if (quadrant != Quadrant.Midfield && GameField.ColorOf(quadrant) != color) continue;
                        score.scoredRedBlueHalves++;
                        score.redBlue += AllianceHalf;
                        score.items.Add(new ScoredItem { point = middle, points = AllianceHalf, kind = (color == PieceColor.Red ? "Red" : "Blue") + " pin halves", where = where, pin = pin });
                    }
                }
            }
            if (match) {   // V2: each Robot in the Midfield, for its Alliance
                score.red += Midfield * redInMidfield;
                score.blue += Midfield * blueInMidfield;
                var mine = mineRed ? redInMidfield : blueInMidfield;
                score.midfieldPoints = Midfield * mine;
                for (var i = 0; i < mine; i++)
                    score.items.Add(new ScoredItem { point = field.ToWorld(Vector2.zero, 8f), points = Midfield, kind = "Robots in the Midfield", where = "Midfield" });
                score.total = mineRed ? score.red : score.blue;
                score.details.Add($"Red {score.red} · Blue {score.blue}");
                if (score.scoredRedBlueHalves > 0) score.details.Add($"Your {(mineRed ? "red" : "blue")} pin halves: {score.scoredRedBlueHalves} × {AllianceHalf} = {score.redBlue}");
                if (score.scoredYellowHalves > 0) score.details.Add($"Yellow pin halves you own: {score.scoredYellowHalves} × {YellowHalf} = {score.yellow}");
                if (mine > 0) score.details.Add($"Your robots in the Midfield: {mine} × {Midfield}");
                return score;
            }
            if (robotInMidfield) score.midfieldPoints = Midfield;
            score.total = score.redBlue + score.yellow + score.midfieldPoints;

            if (score.scoredRedBlueHalves > 0) score.details.Add($"{score.scoredRedBlueHalves} red/blue pin halves × {AllianceHalf} = {score.redBlue}");
            if (score.scoredYellowHalves > 0) score.details.Add($"{score.scoredYellowHalves} yellow pin halves × {YellowHalf} = {score.yellow}");
            if (robotInMidfield) score.details.Add($"Robot in the Midfield = {Midfield}");
            return score;
        }

        // A pin's lower and upper half, if it stands up.
        private static bool Ends(Pin pin, out int low, out int high) {
            low = high = 0;
            if (pin == null || pin.halves.Count < 2) return false;
            var axis = pin.transform.TransformDirection(pin.halves[0].direction);
            var up = Vector3.Dot(axis, Vector3.up);
            if (Mathf.Abs(up) < Upright) return false;
            high = up > 0 ? 0 : 1;
            low = 1 - high;
            return true;
        }

        // A cup's lower and upper end, if it stands up, and whether its opaque half is the lower one.
        private static bool CupEnds(Cup cup, out Vector3 bottom, out Vector3 top, out bool opaqueDown) {
            var a = cup.OpaqueEnd;
            var b = cup.ClearEnd;
            opaqueDown = a.y < b.y;
            bottom = opaqueDown ? a : b;
            top = opaqueDown ? b : a;
            var axis = (top - bottom).normalized;
            return Vector3.Dot(axis, Vector3.up) >= Upright;
        }

        // Whether a point is in the half of a cup between its end `end` and its middle (with some slack), near its axis.
        private static bool Inside(GameField field, Vector3 point, Vector3 end, Vector3 middle, float inset, float slack) {
            var axis = middle - end;
            var length = axis.magnitude;
            if (length < 0.01f) return false;
            axis /= length;
            var along = Vector3.Dot(point - end, axis);
            if (along < inset || along > length + slack) return false;
            var off = (point - end) - axis * along;
            return off.magnitude <= Reach;
        }
    }
}
