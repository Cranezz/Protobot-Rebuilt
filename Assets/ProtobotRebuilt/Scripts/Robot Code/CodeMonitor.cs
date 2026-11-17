using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Protobot.Scripting;
using UnityEngine;

namespace Protobot.RobotCode {
    /// <summary>
    /// V2 (Phase 9): the code window's Monitor tab, as VEXcode's Monitor: the running program's threads (the line each
    /// is on, what it waits for; one opened to its calls and their local variables), the variables
    /// monitor_variable() asked for and any the user pinned, monitor_sensor() readings, then the rest of the program's
    /// global variables. Values that changed in the last second are lit. Links: "t:N" a thread, "l:N" a line,
    /// "v:name" pin or unpin a variable.
    /// </summary>
    public sealed class CodeMonitor {
        public readonly HashSet<string> pinned = new();
        public int selectedThread;   // index into the running threads (0: the main program)
        private readonly Dictionary<string, (string value, float changed)> seen = new();
        private RobotProgram seenProgram;

        private const string Dim = "#8A93A0", Head = "#6FA8DC", Lit = "#F2C94C", Name = "#C9D1D9", Running = "#6FCF97";

        /// <summary>The lines the threads are on (for the editor's markers), the opened thread's first.</summary>
        public readonly List<int> threadLines = new();

        public string Build(RobotProgram p) {
            threadLines.Clear();
            if (p == null) return "<color=" + Dim + ">Run the program to see its threads and variables here.\n\n"
                                  + "In Python, monitor_variable(\"speed\", \"target\") and monitor_sensor(\"left_motor.position\") put "
                                  + "those at the top (as VEXcode's Monitor). Click a variable's name to pin it.</color>";
            if (p != seenProgram) {
                seenProgram = p;
                seen.Clear();
            }
            var sb = new StringBuilder();
            var now = Time.unscaledTime;

            // ---- threads
            var threads = p.Threads.ToList();
            if (selectedThread >= threads.Count) selectedThread = 0;
            sb.Append("<color=").Append(Head).Append("><b>THREADS</b></color>");
            if (Motion.Sandbox.Instance != null && Motion.Sandbox.Instance.Paused) sb.Append("  <color=").Append(Lit).Append(">paused</color>");
            sb.Append('\n');
            if (threads.Count == 0) sb.Append("<color=").Append(Dim).Append(">  (none running: ").Append(Esc(p.Status)).Append(")</color>\n");
            for (var i = 0; i < threads.Count; i++) {
                var f = threads[i];
                var stack = f.Snapshot();
                var top = stack.Count > 0 ? stack[stack.Count - 1] : (null, f.Line, null);
                var fn = stack.LastOrDefault(s => s.function != null).function;
                if (i == selectedThread) threadLines.Insert(0, top.line);
                else threadLines.Add(top.line);
                sb.Append(i == selectedThread ? "<color=" + Running + "><b>></b></color> " : "  ");
                sb.Append("<link=\"t:").Append(i).Append("\"><color=").Append(Name).Append('>').Append(Esc(ThreadName(f, stack))).Append("</color></link>");
                sb.Append("  <link=\"l:").Append(top.line).Append("\"><u>line ").Append(top.line).Append("</u></link>");
                if (fn != null) sb.Append(" in ").Append(Esc(fn)).Append("()");
                var wait = Waiting(f, p);
                if (wait != null) sb.Append("  <color=").Append(Dim).Append('>').Append(wait).Append("</color>");
                sb.Append('\n');
                if (i != selectedThread) continue;
                // The opened thread: its calls, innermost first, each with its local variables.
                for (var k = stack.Count - 1; k >= 0; k--) {
                    var (function, line, locals) = stack[k];
                    if (function == null) {
                        if (stack.Count > 1) sb.Append("      <color=").Append(Dim).Append(">called from </color><link=\"l:").Append(line).Append("\"><u>line ").Append(line).Append("</u></link><color=").Append(Dim).Append("> (top level)</color>\n");
                        continue;
                    }
                    if (k < stack.Count - 1) sb.Append("      <color=").Append(Dim).Append(">called from </color><link=\"l:").Append(line).Append("\"><u>line ").Append(line).Append("</u></link> in ").Append(Esc(function)).Append("()\n");
                    foreach (var kv in locals.Take(24))
                        Row(sb, "        ", "local:" + i + ":" + k + ":" + kv.Key, kv.Key, Short(kv.Value, 0), now, false);
                    if (locals.Count > 24) sb.Append("        <color=").Append(Dim).Append(">... ").Append(locals.Count - 24).Append(" more</color>\n");
                }
            }

            // ---- watched: monitor_variable() + pinned, then monitor_sensor()
            var globals = p.vm.main.globals;
            var watched = p.monitoredVariables.Concat(pinned.Where(n => !p.monitoredVariables.Contains(n))).ToList();
            if (watched.Count > 0 || p.monitoredSensors.Count > 0) {
                sb.Append("\n<color=").Append(Head).Append("><b>MONITOR</b></color>\n");
                foreach (var name in watched)
                    Row(sb, "  ", "g:" + name, name, globals.TryGetValue(name, out var v) ? Short(v, 0) : "<color=" + Dim + ">(not set yet)</color>", now, true, !p.monitoredVariables.Contains(name));
                foreach (var sensor in p.monitoredSensors) {
                    var label = sensor as string ?? Ops.Repr(sensor);
                    Row(sb, "  ", "s:" + label, label, Reading(p, sensor), now, false);
                }
            }

            // ---- the program's other global variables
            var others = globals.Where(kv => IsVariable(p, kv.Key, kv.Value) && !watched.Contains(kv.Key)).ToList();
            if (others.Count > 0) {
                sb.Append("\n<color=").Append(Head).Append("><b>VARIABLES</b></color>  <color=").Append(Dim).Append(">(click a name to pin it to the top)</color>\n");
                foreach (var kv in others.Take(80)) Row(sb, "  ", "g:" + kv.Key, kv.Key, Short(kv.Value, 0), now, true);
                if (others.Count > 80) sb.Append("  <color=").Append(Dim).Append(">... ").Append(others.Count - 80).Append(" more</color>\n");
            }
            return sb.ToString();
        }

        private void Row(StringBuilder sb, string indent, string key, string name, string value, float now, bool pinnable, bool pinnedMark = false) {
            // A value that changed in the last second is lit.
            var changed = false;
            if (seen.TryGetValue(key, out var old)) {
                if (old.value != value) seen[key] = (value, now);
                changed = now - seen[key].changed < 1f;
            }
            else seen[key] = (value, -10f);
            sb.Append(indent);
            if (pinnable) sb.Append("<link=\"v:").Append(name).Append("\">");
            sb.Append("<color=").Append(Name).Append('>').Append(Esc(name)).Append("</color>");
            if (pinnable) sb.Append("</link>");
            if (pinnedMark) sb.Append("<color=").Append(Dim).Append("> (pinned)</color>");
            sb.Append(" = ");
            if (changed) sb.Append("<color=").Append(Lit).Append('>');
            sb.Append(value);
            if (changed) sb.Append("</color>");
            sb.Append('\n');
        }

        // "main", or what started it and its function: "thread logger()", "Driver control driver()".
        private static string ThreadName(Fiber f, List<(string function, int line, List<KeyValuePair<string, object>> locals)> stack) {
            var first = stack.FirstOrDefault(s => s.function != null).function;
            return f.name == "main" || first == null ? f.name : f.name + " " + first + "()";
        }

        private static string Waiting(Fiber f, RobotProgram p) {
            var w = f.waiting;
            if (w == null) return null;
            if (w.until >= 0) {
                var left = w.until - p.vm.Time;
                return left >= 0.005 ? "waiting " + left.ToString("0.00", CultureInfo.InvariantCulture) + " s" : null;
            }
            return w.done != null ? "waiting (until a motion or event is done)" : null;
        }

        // A program's own variable: not a function, class, module or device, not something imported, not hidden.
        private static bool IsVariable(RobotProgram p, string name, object value) {
            if (name.StartsWith("_") || p.systemNames.Contains(name)) return false;
            if (value is PyModule or PyFunction or PyBuiltin or PyClass or PyBoundMethod or IPyObject) return false;
            foreach (var m in p.vm.modules.Values)
                if (m.globals.TryGetValue(name, out var v) && ReferenceEquals(v, value)) return false;
            return true;
        }

        // monitor_sensor("left_motor.position") / monitor_sensor(imu.heading): read now. Only the API's own readings
        // are called (never the program's functions: the Monitor must not run the program).
        private static string Reading(RobotProgram p, object sensor) {
            try {
                object value;
                if (sensor is string path) {
                    path = path.Trim();
                    if (path.EndsWith("()")) path = path.Substring(0, path.Length - 2);
                    var parts = path.Split('.');
                    if (!p.vm.main.globals.TryGetValue(parts[0], out value)) return "<color=" + Dim + ">(no '" + Esc(parts[0]) + "')</color>";
                    for (var i = 1; i < parts.Length; i++) value = p.vm.GetAttr(value, parts[i]);
                }
                else value = sensor;
                if (value is PyBuiltin || value is PyBoundMethod { function: PyBuiltin }) value = p.vm.CallSync(value);
                else if (value is PyFunction or PyBoundMethod) return "<color=" + Dim + ">(a function of the program: monitor its result in a variable)</color>";
                return Short(value, 0);
            }
            catch (PyError e) {
                return "<color=#EB5757>" + Esc(e.Message) + "</color>";
            }
            catch (Exception e) {
                return "<color=#EB5757>" + Esc(e.Message) + "</color>";
            }
        }

        // ------------------------------------------------------------------ values

        /// <summary>A value in a few characters (rich text, escaped).</summary>
        public static string Short(object o, int depth) {
            switch (o) {
                case null:
                case PyNone:
                    return "None";
                case bool b:
                    return b ? "True" : "False";
                case long l:
                    return l.ToString(CultureInfo.InvariantCulture);
                case double d:
                    return double.IsNaN(d) || double.IsInfinity(d) ? Ops.FloatRepr(d) : Math.Abs(d) >= 1e6 ? d.ToString("0.###e0", CultureInfo.InvariantCulture) : d.ToString("0.0###", CultureInfo.InvariantCulture);
                case string s:
                    return "<color=#CE9178>" + Esc(s.Length > 48 ? Ops.StrRepr(s.Substring(0, 48)) + "..." : Ops.StrRepr(s)) + "</color>";
                case PyEnum e:
                    return Esc(e.name);
            }
            if (depth >= 2) return o is PyList or PyTuple or PyDict ? "[...]" : Esc(Ops.Repr(o).Length > 30 ? Ops.Repr(o).Substring(0, 30) + "..." : Ops.Repr(o));
            switch (o) {
                case PyList l:
                    return "[" + Items(l.items, depth) + "]";
                case PyTuple t:
                    return "(" + Items(t.items, depth) + ")";
                case PyDict d: {
                    var shown = d.Keys.Take(5).Select(k => Short(k, depth + 1) + ": " + Short(d.items[k], depth + 1));
                    return "{" + string.Join(", ", shown) + (d.items.Count > 5 ? ", ... " + d.items.Count + " items" : "") + "}";
                }
                case PyInstance i when !i.cls.mro.Any(c => c.name == "BaseException"): {
                    var fields = i.dict.Where(kv => !kv.Key.StartsWith("_")).Take(5).Select(kv => Esc(kv.Key) + "=" + Short(kv.Value, depth + 1));
                    return Esc(i.cls.name) + "(" + string.Join(", ", fields) + (i.dict.Count > 5 ? ", ..." : "") + ")";
                }
                case IPyObject po:
                    return "<color=" + Dim + ">" + Esc(po.TypeName) + "</color>";
                default:
                    var r = Ops.Repr(o);
                    return Esc(r.Length > 60 ? r.Substring(0, 60) + "..." : r);
            }
        }

        private static string Items(IReadOnlyList<object> items, int depth) {
            var shown = items.Take(8).Select(x => Short(x, depth + 1));
            return string.Join(", ", shown) + (items.Count > 8 ? ", ... " + items.Count + " items" : "");
        }

        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.IndexOf('<') < 0 ? s : "<noparse>" + s.Replace("</noparse>", "") + "</noparse>";
    }
}
