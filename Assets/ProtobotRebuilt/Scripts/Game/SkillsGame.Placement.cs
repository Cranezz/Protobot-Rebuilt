using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using UnityEngine;

namespace Protobot.Game {
    // V2: placing the robot by the rules (SG1/RSC2): it always touches the wall of its Quadrant, so the only things
    // to choose are where along that wall it sits and which way it's turned. Every change re-seats it against the wall
    // (its footprint's nearest corner or side on the perimeter) and keeps it inside its Quadrant. The alignment tool
    // goes anywhere on the field, flat against a wall when it's brought within an inch of one; Line up puts a face or
    // point of the robot against a face of the tool or the field (like a SolidWorks mate, kept to what the rules allow).
    public partial class SkillsGame {
        public SetupTools setupTools;
        /// <summary>The alignment tool's footprint: across its own X, along its own Z (inches).</summary>
        public Vector2 toolSize;

        public static Vector2 Dir(float heading) => new(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
        public static float HeadingOf(Vector2 d) => Mathf.Repeat(Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, 360f);

        /// <summary>A footprint's corners from its middle, for a heading (depth along the heading, width across).</summary>
        public static Vector2[] Offsets(float heading, float depth, float width) {
            var f = Dir(heading);
            var r = new Vector2(f.y, -f.x);
            return new[] { f * depth / 2f + r * width / 2f, f * depth / 2f - r * width / 2f, -f * depth / 2f - r * width / 2f, -f * depth / 2f + r * width / 2f };
        }

        /// <summary>Where along its wall the robot sits: inches from the wall's middle.</summary>
        public float Slide => ToLeft(StartQuadrant, new Vector2(x, y)).y;

        /// <summary>Which way it's turned: 0 = facing straight into the field, + clockwise (seen from above).</summary>
        public float Turn => Mathf.DeltaAngle(90f, HeadingOf(ToLeft(StartQuadrant, Dir(heading))));

        /// <summary>Along the wall, from the robot's side: the field direction of +Slide.</summary>
        public Vector2 WallTangent => FromLeft(StartQuadrant, Vector2.up);

        public void SetSlide(float slide) {
            var before = Slide;
            var wasClear = !HitsTool();
            stoppedByTool = false;
            MoveAlong(slide);
            // The alignment tool is solid: sliding into it (or past it, in one jump) stops against it.
            if (wasClear && tool != null) {
                var steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(slide - before) / 0.25f));
                float clear = 0f, hit = -1f;
                for (var i = 1; i <= steps; i++) {
                    var share = (float)i / steps;
                    MoveAlong(Mathf.Lerp(before, slide, share));
                    if (HitsTool()) {
                        hit = share;
                        break;
                    }
                    clear = share;
                }
                if (hit > 0f) {
                    for (var i = 0; i < 20; i++) {
                        var mid = (clear + hit) / 2f;
                        MoveAlong(Mathf.Lerp(before, slide, mid));
                        if (HitsTool()) hit = mid;
                        else clear = mid;
                    }
                }
                MoveAlong(Mathf.Lerp(before, slide, hit > 0f ? clear : 1f));
                stoppedByTool = hit > 0f;
            }
            Apply();
        }

        public void SetTurn(float turn) {
            var wasClear = !HitsTool();
            heading = HeadingOf(FromLeft(StartQuadrant, Dir(90f + turn)));
            SnapToWall();
            // Turned into the alignment tool: slid away from it until clear.
            if (wasClear && HitsTool()) {
                var start = Slide;
                var away = Vector2.Dot(new Vector2(x, y) - new Vector2(toolX, toolY), WallTangent) >= 0f ? 1f : -1f;
                for (var step = 0.0625f; step < 40f && HitsTool(); step += 0.0625f) MoveAlong(start + away * step);
            }
            Apply();
        }

        /// <summary>The last slide stopped against the alignment tool.</summary>
        private bool stoppedByTool;

        // Puts the robot at `slide` along its wall (seated on it, inside its Quadrant) without redrawing.
        private void MoveAlong(float slide) {
            var c = ToLeft(StartQuadrant, new Vector2(x, y));
            c.y = slide;
            var p = FromLeft(StartQuadrant, c);
            x = p.x;
            y = p.y;
            SnapToWall();
        }

        /// <summary>Whether the robot's footprint goes into the alignment tool's (touching is fine).</summary>
        public bool HitsTool() => tool != null && toolSize != Vector2.zero && Overlap(Corners(), ToolCorners()) > 0.01f;

        // How deep two convex footprints go into each other (separating axes); 0 or less when apart or touching.
        private static float Overlap(Vector2[] a, Vector2[] b) {
            var least = float.MaxValue;
            foreach (var poly in new[] { a, b })
                for (var i = 0; i < poly.Length; i++) {
                    var edge = poly[(i + 1) % poly.Length] - poly[i];
                    var axis = new Vector2(-edge.y, edge.x).normalized;
                    float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
                    foreach (var p in a) {
                        var d = Vector2.Dot(p, axis);
                        aMin = Mathf.Min(aMin, d);
                        aMax = Mathf.Max(aMax, d);
                    }
                    foreach (var p in b) {
                        var d = Vector2.Dot(p, axis);
                        bMin = Mathf.Min(bMin, d);
                        bMax = Mathf.Max(bMax, d);
                    }
                    least = Mathf.Min(least, Mathf.Min(aMax, bMax) - Mathf.Max(aMin, bMin));
                }
            return least;
        }

        public void SlideBy(float inches) => SetSlide(Slide + inches);

        /// <summary>The robot footprint's gap to each end of its wall: the − end and the + end of Slide (inches).</summary>
        public (float minus, float plus) CornerGaps() {
            var tangent = WallTangent;
            var along = Corners().Select(c => Vector2.Dot(c, tangent)).ToArray();
            return (along.Min() + GameField.Half, GameField.Half - along.Max());
        }
        public void TurnBy(float degrees) => SetTurn(Turn + degrees);

        /// <summary>Seats the robot against its Quadrant's wall and keeps its footprint inside the Quadrant.</summary>
        public void SnapToWall() {
            if (field == null) return;
            var q = StartQuadrant;
            var c = ToLeft(q, new Vector2(x, y));
            var offsets = Offsets(HeadingOf(ToLeft(q, Dir(heading))), depth, width);
            c.x = -GameField.Half + 0.02f - offsets.Min(o => o.x);
            // Inside the Quadrant (|y| <= -x for every corner) and the field.
            float lo = -GameField.Half, hi = GameField.Half;
            foreach (var o in offsets) {
                lo = Mathf.Max(lo, Mathf.Max(c.x + o.x - o.y, -GameField.Half - o.y));
                hi = Mathf.Min(hi, Mathf.Min(-(c.x + o.x) - o.y, GameField.Half - o.y));
            }
            if (lo <= hi) c.y = Mathf.Clamp(c.y, lo, hi);
            var p = FromLeft(q, c);
            x = p.x;
            y = p.y;
        }

        public void NotifyChanged() => Changed?.Invoke();

        // ------------------------------------------------------------------ V2 (build 80): starts as teams set them up

        // Teams line a robot up on the field's tiles: an edge on a tile seam, or centred on a tile, back to the wall.
        // The tiles are 24" and the seams run through the field's middle, so along any wall they're at 0, ±24, ±48.
        private static readonly float[] Seams = { -48f, -24f, 0f, 24f, 48f };
        private static readonly float[] TileMiddles = { -60f, -36f, -12f, 12f, 36f, 60f };
        public const float SnapReach = 1.5f;

        /// <summary>The footprint's ends along its wall (in Slide's inches) at a given slide.</summary>
        private (float lo, float hi) Ends(float slide) {
            var tangent = WallTangent;
            var along = Corners().Select(c => Vector2.Dot(c, tangent)).ToArray();
            var shift = slide - Slide;
            return (along.Min() + shift, along.Max() + shift);
        }

        // Tiles along the wall are counted from the robot's left (seen from behind it, facing into the field).
        private bool LeftIsPlus => Vector2.Dot(FromLeft(StartQuadrant, Vector2.up), WallTangent) > 0f;
        private int TileNumber(float along) {
            var index = Mathf.FloorToInt((along + 72f) / 24f);   // 0 at the − end
            index = Mathf.Clamp(index, 0, 5);
            return LeftIsPlus ? 6 - index : index + 1;
        }
        private string LeftOrRight(bool lowEnd) => (lowEnd ^ LeftIsPlus) ? "left" : "right";

        /// <summary>The nearest team-style spot to `slide` within SnapReach: an edge on a seam or the middle on a tile's
        /// (with what it is, "tile 2 · left edge on the seam"); else `slide` itself and null.</summary>
        public float SnapSlide(float slide, out string what) {
            what = null;
            var (lo, hi) = Ends(slide);
            var best = slide;
            var bestGap = SnapReach;
            foreach (var s in Seams) {
                if (Mathf.Abs(lo - s) < bestGap) { bestGap = Mathf.Abs(lo - s); best = slide + (s - lo); what = $"tile {TileNumber(s + 1f)} · {LeftOrRight(true)} edge on the seam"; }
                if (Mathf.Abs(hi - s) < bestGap) { bestGap = Mathf.Abs(hi - s); best = slide + (s - hi); what = $"tile {TileNumber(s - 1f)} · {LeftOrRight(false)} edge on the seam"; }
            }
            foreach (var m in TileMiddles)
                if (Mathf.Abs(slide - m) < bestGap) { bestGap = Mathf.Abs(slide - m); best = m; what = $"tile {TileNumber(m)} · centered"; }
            return best;
        }

        /// <summary>Every legal team-style spot along the wall at the current angle, in order along it.</summary>
        public List<(float slide, string what)> Spots() {
            var keep = (x, y, heading);
            var list = new List<(float, string)>();
            var (lo, hi) = Ends(Slide);
            var half = (hi - lo) / 2f;
            var candidates = new List<float>();
            foreach (var s in Seams) { candidates.Add(s + half + 0.01f); candidates.Add(s - half - 0.01f); }
            candidates.AddRange(TileMiddles);
            foreach (var c in candidates.OrderBy(v => v)) {
                MoveAlong(c);
                if (Mathf.Abs(Slide - c) > 0.05f || !Legal) continue;
                SnapSlide(c, out var what);
                if (what == null || list.Any(l => Mathf.Abs(l.Item1 - c) < 0.5f)) continue;
                list.Add((c, what));
            }
            (x, y, heading) = keep;
            return LeftIsPlus ? list.OrderByDescending(l => l.Item1).ToList() : list;
        }

        /// <summary>Which spot it's on now (null when between spots).</summary>
        public string SpotNow() {
            SnapSlide(Slide, out var what);
            var snapped = SnapSlide(Slide, out _);
            return Mathf.Abs(snapped - Slide) < 0.02f ? what : null;
        }

        /// <summary>Turns the robot so its front points at a field point (a Goal, a Pin, a spot), still on its wall, and
        /// moves it to the nearest legal spot if the turn made it touch something.</summary>
        public void AimAt(Vector2 target) {
            for (var round = 0; round < 3; round++) {
                for (var i = 0; i < 3; i++) {   // turning moves its middle a little: aim again from where it ends up
                    var want = HeadingOf(target - new Vector2(x, y));
                    var turn = Mathf.DeltaAngle(90f, HeadingOf(ToLeft(StartQuadrant, Dir(want))));
                    if (Mathf.Abs(turn) > 89f) turn = Mathf.Sign(turn) * 89f;   // the front can't face the wall it's on
                    SetTurn(turn);
                }
                if (Legal || !KeepLegal()) break;
            }
        }

        /// <summary>Turns it (0 = square to the wall, + clockwise) and keeps the start legal.</summary>
        public void Face(float turn) {
            SetTurn(turn);
            KeepLegal();
        }

        /// <summary>When the start isn't legal (a turn put it on a Goal or in a Load Zone), the nearest legal team spot
        /// along the wall, else the nearest legal place; false when there's none at this angle (left where it was).</summary>
        public bool KeepLegal() {
            if (Legal) return true;
            var from = Slide;
            foreach (var (slide, _) in Spots().OrderBy(sp => Mathf.Abs(sp.slide - from))) {
                SetSlide(slide);
                if (Legal) return true;
            }
            for (var step = 0.25f; step < 80f; step += 0.25f)
                foreach (var s in new[] { from + step, from - step }) {
                    MoveAlong(s);
                    if (Mathf.Abs(Slide - s) < 0.01f && Legal) {
                        Apply();
                        return true;
                    }
                }
            SetSlide(from);
            return false;
        }

        /// <summary>The middle of the Goal nearest a field point (within 10"), else the point.</summary>
        public Vector2 AimTarget(Vector2 at) {
            if (field == null) return at;
            var best = at;
            var bestDistance = 10f;
            foreach (var g in field.goals) {
                var d = Vector2.Distance(g.position, at);
                if (d < bestDistance) {
                    bestDistance = d;
                    best = g.position;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ undo (setup only)

        private readonly List<(float x, float y, float h, float tx, float ty, float th)> undo = new();

        public void PushUndo() {
            undo.Add((x, y, heading, toolX, toolY, toolHeading));
            if (undo.Count > 40) undo.RemoveAt(0);
        }

        public bool Undo() {
            if (undo.Count == 0) return false;
            var u = undo[^1];
            undo.RemoveAt(undo.Count - 1);
            x = u.x;
            y = u.y;
            heading = u.h;
            toolX = u.tx;
            toolY = u.ty;
            toolHeading = u.th;
            Apply();
            return true;
        }

        // ------------------------------------------------------------------ the alignment tool

        public Vector2[] ToolCorners() => Offsets(toolHeading, toolSize.y, toolSize.x).Select(o => o + new Vector2(toolX, toolY)).ToArray();

        /// <summary>Puts the tool at (x, y, heading), inside the field, flat against any wall within `snap` inches.</summary>
        public void PlaceTool(float nx, float ny, float nh, float snap = 1f) {
            var wasClear = !HitsTool();
            var (oldX, oldY, oldHeading) = (toolX, toolY, toolHeading);
            PlaceToolNow(nx, ny, nh, snap);
            // Not into the robot: it stays where it was.
            if (wasClear && HitsTool()) {
                toolX = oldX;
                toolY = oldY;
                toolHeading = oldHeading;
            }
            Apply();
        }

        private void PlaceToolNow(float nx, float ny, float nh, float snap) {
            toolHeading = Mathf.Repeat(nh, 360f);
            var o = Offsets(toolHeading, toolSize.y, toolSize.x);
            float minX = o.Min(v => v.x), maxX = o.Max(v => v.x), minY = o.Min(v => v.y), maxY = o.Max(v => v.y);
            var h = GameField.Half - 0.02f;
            if (nx + minX < -h + snap) nx = -h - minX;
            else if (nx + maxX > h - snap) nx = h - maxX;
            if (ny + minY < -h + snap) ny = -h - minY;
            else if (ny + maxY > h - snap) ny = h - maxY;
            toolX = nx;
            toolY = ny;
        }

        /// <summary>The tool beside the robot, against the same wall, turned the same way, on the side with more room.</summary>
        public void ToolBesideRobot() {
            if (tool == null) return;
            var q = StartQuadrant;
            var c = ToLeft(q, new Vector2(x, y));
            var hl = HeadingOf(ToLeft(q, Dir(heading)));
            var ro = Offsets(hl, depth, width);
            var to = Offsets(hl, toolSize.y, toolSize.x);
            float rMin = ro.Min(o => o.y), rMax = ro.Max(o => o.y), tMin = to.Min(o => o.y), tMax = to.Max(o => o.y);
            var room = GameField.Half - (c.y + rMax);
            var roomOther = c.y + rMin + GameField.Half;
            var ty = room >= roomOther ? c.y + rMax - tMin + 0.25f : c.y + rMin - tMax - 0.25f;
            var tx = -GameField.Half + 0.02f - to.Min(o => o.x);
            var p = FromLeft(q, new Vector2(tx, ty));
            PlaceTool(p.x, p.y, heading, 0f);
        }

        public void LoadToolGroup(string name) {
            var path = System.IO.Path.Combine(Builds.SavedGroups.Folder, name + ".json");
            Builds.SavedGroups.GroupFile data = null;
            try { data = JsonUtility.FromJson<Builds.SavedGroups.GroupFile>(System.IO.File.ReadAllText(path)); }
            catch (System.Exception e) {
                Interface.Interface.Say("That group couldn't be read: " + e.Message, false);
                return;
            }
            if (data?.parts == null || data.parts.Length == 0) {
                Interface.Interface.Say("That group has no parts", false);
                return;
            }
            LoadToolParts(data.parts, name);
        }

        // ------------------------------------------------------------------ Line up (a simple mate)

        /// <summary>Puts the robot's picked face (turning it to face the target) or point against the target's face, or
        /// lines a point up with a target point along the wall, keeping the robot on its wall. Null when it worked as
        /// asked, else what happened.</summary>
        public string LineUp(Vector3 robotPoint, Vector3? robotNormal, Vector3 targetPoint, Vector3? targetNormal) {
            var t = field.transform;
            Vector2 Flat(Vector3 d) {
                var l = t.InverseTransformDirection(d);
                return new Vector2(l.x, l.z);
            }
            float Up(Vector3 d) => t.InverseTransformDirection(d).normalized.y;

            PushUndo();
            // The robot's point in its own footprint frame (it stays put on the robot as the robot moves).
            var f0 = Dir(heading);
            var r0 = new Vector2(f0.y, -f0.x);
            var d0 = field.ToField(robotPoint) - new Vector2(x, y);
            float u = Vector2.Dot(d0, f0), v = Vector2.Dot(d0, r0);
            Vector2? m = null;
            // An upright face is a plane to go against; a top, the tiles or a point is a spot to line up with along the wall.
            if (targetNormal is Vector3 tn && Mathf.Abs(Up(tn)) <= 0.7f) m = Flat(tn).normalized;
            var turned = false;
            // A side of the robot turns to face the target's face; its top or bottom counts as a point.
            if (robotNormal is Vector3 rn && m != null && Mathf.Abs(Up(rn)) <= 0.7f) {
                var n = Flat(rn).normalized;
                float nu = Vector2.Dot(n, f0), nv = Vector2.Dot(n, r0);
                // At heading 0 the normal points (nv, nu); turning the robot turns it by the same angle.
                var wanted = Mathf.Repeat(HeadingOf(-m.Value) - HeadingOf(new Vector2(nv, nu)), 360f);
                turned = Mathf.Abs(Mathf.DeltaAngle(heading, wanted)) > 0.05f;
                heading = wanted;
                SnapToWall();
            }
            var f = Dir(heading);
            var r = new Vector2(f.y, -f.x);
            var p = new Vector2(x, y) + f * u + r * v;
            var q = field.ToField(targetPoint);
            var tangent = WallTangent;
            float ds;
            if (m != null) {
                var along = Vector2.Dot(tangent, m.Value);
                if (Mathf.Abs(along) < 0.15f) {
                    Apply();
                    return turned
                        ? "Turned to match that face. It faces along the wall, so sliding along the wall can't bring them together"
                        : "That face is parallel to the wall: the robot can only slide along the wall, so it can't move toward it";
                }
                ds = Vector2.Dot(q - p, m.Value) / along;
            }
            else ds = Vector2.Dot(q - p, tangent);
            var want = Slide + ds;
            SetSlide(want);
            var shortBy = Mathf.Abs(want - Slide);
            if (shortBy > 0.02f)
                return (stoppedByTool ? "Stopped against the alignment tool (" : "Lined up as far as your Quadrant allows (") + Inch.Format(shortBy) + " short)";
            return null;
        }
    }
}
