// (Play, host of an online match) Trailer finish: stacks Pins and Cups on the Goals the way a strong alliance would
// (Goal -> Pin -> Cup -> Pin -> Cup -> Pin...), pieces held still (kinematic), then reports the real score.
// Temp/stage.txt: "goals" (list the Goals), "stack L1 L2 ..." (levels = pins per Goal, in the goals' order; a Goal's
// alliance comes from its Quadrant: red Left/Bottom, blue Right/Top, Midfield alternates), "score".
var cmd = System.IO.File.ReadAllText("Temp/stage.txt").Trim().Split(' ');
var game = Protobot.Game.SkillsGame.Current;
if (game == null) return "no game";
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var pins = (System.Collections.Generic.List<Protobot.Game.Pin>)typeof(Protobot.Game.SkillsGame).GetField("pins", flags).GetValue(game);
var cups = (System.Collections.Generic.List<Protobot.Game.Cup>)typeof(Protobot.Game.SkillsGame).GetField("cups", flags).GetValue(game);
var field = game.field;
if (cmd[0] == "goals") {
    var sb = new System.Text.StringBuilder();
    for (int i = 0; i < field.goals.Count; i++) { var g = field.goals[i]; sb.Append(i + ": " + g.name + " " + g.quadrant + " at " + g.position.ToString("F0") + " top " + g.top.ToString("F1") + (g.alliance ? " alliance" : "") + " | "); }
    var colors = new System.Collections.Generic.Dictionary<string, int>();
    foreach (var p in pins) if (p != null && p.halves.Count == 2) { var k = p.halves[0].Item2 + "/" + p.halves[1].Item2; colors[k] = colors.TryGetValue(k, out var n) ? n + 1 : 1; }
    foreach (var kv in colors) sb.Append(kv.Key + " x" + kv.Value + " ");
    return sb + " | cups " + cups.Count;
}
if (cmd[0] == "stack") {
    var used = new System.Collections.Generic.HashSet<UnityEngine.Object>();
    var up = field.transform.up;
    int placed = 0;
    for (int gi = 0; gi < field.goals.Count && gi + 1 < cmd.Length; gi++) {
        var goal = field.goals[gi];
        int levels = int.Parse(cmd[gi + 1]);
        bool red = goal.quadrant == Protobot.Game.Quadrant.Left || goal.quadrant == Protobot.Game.Quadrant.Bottom || (goal.Midfield && gi % 2 == 0);
        var want = red ? Protobot.Game.PieceColor.Red : Protobot.Game.PieceColor.Blue;
        float h = goal.top - 2.0f;   // the bottom pin's low tip, inside the goal
        for (int l = 0; l < levels; l++) {
            // a pin with a half of the alliance's color, that half up (prefer the alliance/yellow pins)
            Protobot.Game.Pin pin = null; int upHalf = 0;
            foreach (var p in pins) {
                if (p == null || used.Contains(p) || p.halves.Count != 2) continue;
                for (int k = 0; k < 2; k++) if (p.halves[k].Item2 == want) { pin = p; upHalf = k; break; }
                if (pin != null) break;
            }
            if (pin == null)   // then a pin with a yellow half, yellow up (the Toggle makes it the alliance's)
                foreach (var p in pins) {
                    if (p == null || used.Contains(p) || p.halves.Count != 2) continue;
                    for (int k = 0; k < 2; k++) if (p.halves[k].Item2 == Protobot.Game.PieceColor.Yellow) { pin = p; upHalf = k; break; }
                    if (pin != null) break;
                }
            if (pin == null) break;
            used.Add(pin);
            var dir = pin.halves[upHalf].Item1;
            var rot = UnityEngine.Quaternion.FromToRotation(pin.transform.rotation * dir, up) * pin.transform.rotation;
            var lowTip = field.ToWorld(goal.position, h);
            if (pin.body != null) { pin.body.isKinematic = true; pin.body.linearVelocity = UnityEngine.Vector3.zero; }
            pin.transform.rotation = rot;
            pin.transform.position = lowTip + up * pin.halfLength;
            if (pin.body != null) { pin.body.position = pin.transform.position; pin.body.rotation = rot; }
            placed++;
            if (l == levels - 1) break;
            // a cup on it, clear half down (the pin's upper half shows through), opaque half up
            Protobot.Game.Cup cup = null;
            foreach (var c in cups) if (c != null && !used.Contains(c)) { cup = c; break; }
            if (cup == null) break;
            used.Add(cup);
            var highTip = lowTip + up * (2f * pin.halfLength);
            var bottom = highTip - up * 2.0f;
            var crot = UnityEngine.Quaternion.FromToRotation(cup.transform.rotation * cup.opaqueDirection, up) * cup.transform.rotation;
            if (cup.body != null) { cup.body.isKinematic = true; cup.body.linearVelocity = UnityEngine.Vector3.zero; }
            cup.transform.rotation = crot;
            cup.transform.position = bottom + up * cup.halfLength;
            if (cup.body != null) { cup.body.position = cup.transform.position; cup.body.rotation = crot; }
            var top = bottom + up * (2f * cup.halfLength);
            h = field.HeightOf(top - up * 2.0f);
        }
    }
    var s = game.Evaluate();
    return "placed " + placed + " pins | red " + s.red + " blue " + s.blue;
}
if (cmd[0] == "toggles") {
    var toggles = (System.Collections.Generic.List<Protobot.Game.Toggle>)typeof(Protobot.Game.SkillsGame).GetField("toggles", flags).GetValue(game);
    var sb = new System.Text.StringBuilder();
    foreach (var t in toggles) {
        if (t == null) continue;
        var red = t.quadrant == Protobot.Game.Quadrant.Left || t.quadrant == Protobot.Game.Quadrant.Bottom;
        var want = red ? Protobot.Game.PieceColor.Red : Protobot.Game.PieceColor.Blue;
        var axis = t.transform.TransformDirection(t.axisLocal).normalized;
        foreach (var f in t.faces) {
            if (f.Item2 != want) continue;
            var n = t.transform.TransformDirection(f.Item1);
            var nP = UnityEngine.Vector3.ProjectOnPlane(n, axis).normalized;
            var upP = UnityEngine.Vector3.ProjectOnPlane(UnityEngine.Vector3.up, axis).normalized;
            var angle = UnityEngine.Vector3.SignedAngle(nP, upP, axis);
            if (t.body != null) { t.body.isKinematic = true; t.body.angularVelocity = UnityEngine.Vector3.zero; }
            t.transform.rotation = UnityEngine.Quaternion.AngleAxis(angle, axis) * t.transform.rotation;
            if (t.body != null) t.body.rotation = t.transform.rotation;
            break;
        }
        sb.Append(t.quadrant + " " + want + " ");
    }
    return sb.ToString();
}
if (cmd[0] == "score") { var s = game.Evaluate(); return "red " + s.red + " blue " + s.blue + " (items " + s.items.Count + ")"; }
return "?";
