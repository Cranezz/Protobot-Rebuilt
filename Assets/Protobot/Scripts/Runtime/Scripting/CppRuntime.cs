using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Protobot.Scripting {
    /// <summary>
    /// V2 (robot code): what translated C++ needs at run time: C++'s integer division and modulo (__cdiv, __cmod,
    /// __cint), printf formatting (__printf, CFormat), std::cout (__cout), std::to_string, enums (__enum), structs
    /// without a constructor (__aggregate: Pid{1, 0, 2}), std::vector (__vector), and the "std" module plus the C math
    /// functions (sqrt, fabs, atan2, M_PI...). C++ names on lists and strings (push_back, size, empty, back, substr...)
    /// are in Builtins.CppMethod.
    /// </summary>
    public static class CppRuntime {
        public static void Install(Interpreter vm) {
            var g = vm.main.globals;
            void Def(string name, Func<Args, object> fn) => g[name] = new PyBuiltin(name, fn);

            Def("__cdiv", a => {
                var x = a.Need(0, null);
                var y = a.Need(1, null);
                if (x is long lx && y is long ly) {
                    if (ly == 0) throw new PyError("ZeroDivisionError", "division by zero (an int divided by 0)");
                    return lx / ly;   // C++: towards zero
                }
                var dy = Ops.ToDouble(y);
                var dx = Ops.ToDouble(x);
                return dx / dy;   // (doubles: inf or nan, as C++)
            });
            Def("__cmod", a => {
                var x = a.Need(0, null);
                var y = a.Need(1, null);
                if (x is long lx && y is long ly) {
                    if (ly == 0) throw new PyError("ZeroDivisionError", "modulo by zero");
                    return lx % ly;   // C++: the sign of the left side
                }
                return Ops.ToDouble(x) % Ops.ToDouble(y);   // fmod (the sign of the left side)
            });
            Def("__cint", a => ToInt(a.Need(0, null)));
            Def("__cchar", a => {
                var v = a.Need(0, null);
                return v is string s ? s : ((char)ToInt(v)).ToString();
            });
            Def("__printf", a => {
                var text = CFormat(a.vm, Ops.Str(a.Need(0, "format")), a.pos.Skip(1).ToArray());
                a.vm.Output(text);
                return (long)text.Length;
            });
            Def("__cout", a => {
                var sb = new StringBuilder();
                foreach (var o in a.pos) sb.Append(Stream(a.vm, o));
                a.vm.Output(sb.ToString());
                return PyNone.Value;
            });
            Def("__to_string", a => {
                var v = a.Need(0, null);
                return v is double d ? d.ToString("F6", CultureInfo.InvariantCulture) : v is bool b ? (b ? "1" : "0") : a.vm.StrOf(v);
            });
            Def("__enum", a => {
                var cls = new PyClass(Ops.Str(a.Need(0, "name")), null);
                if (a.Need(1, "values") is PyDict d)
                    foreach (var k in d.Keys) cls.dict[Ops.Str(k)] = d.items[k];
                return cls;
            });
            Def("__vector", a => {
                // std::vector<T>(), (n), (n, value), or from a list.
                if (a.Count == 0) return new PyList();
                if (a.pos[0] is PyList l) return new PyList(l.items);
                var n = (int)ToInt(a.pos[0]);
                var v = a.Count > 1 ? a.pos[1] : 0L;
                return new PyList(Enumerable.Repeat(v, Math.Max(0, n)));
            });
            Def("__aggregate", a => {
                // Pid{1, 0.5, 2} / Pid{.kP = 1}: values into the fields in order, or by name.
                var self = a.Need(0, null);
                var fields = (PyList)a.Need(1, null);
                var args = a.Need(2, null) is PyTuple t ? t.items : Array.Empty<object>();
                for (var i = 0; i < args.Length && i < fields.items.Count; i++) a.vm.SetAttr(self, (string)fields.items[i], args[i]);
                if (a.Get(3, null) is PyDict kw)
                    foreach (var k in kw.Keys) a.vm.SetAttr(self, Ops.Str(k), kw.items[k]);
                return PyNone.Value;
            });

            // C math, as globals (sqrt, fabs...) and in std::.
            var std = new PyModule("std");
            var math = new Dictionary<string, Func<double, double>> {
                ["sqrt"] = Math.Sqrt, ["fabs"] = Math.Abs, ["sin"] = Math.Sin, ["cos"] = Math.Cos, ["tan"] = Math.Tan,
                ["asin"] = Math.Asin, ["acos"] = Math.Acos, ["atan"] = Math.Atan, ["exp"] = Math.Exp, ["log"] = Math.Log,
                ["log10"] = Math.Log10, ["log2"] = x => Math.Log(x, 2), ["floor"] = Math.Floor, ["ceil"] = Math.Ceiling,
                ["round"] = x => Math.Round(x, MidpointRounding.AwayFromZero), ["trunc"] = Math.Truncate, ["cbrt"] = x => Math.Pow(x, 1.0 / 3),
                ["sinh"] = Math.Sinh, ["cosh"] = Math.Cosh, ["tanh"] = Math.Tanh,
            };
            foreach (var (name, fn) in math.Select(kv => (kv.Key, kv.Value))) {
                var f = fn;
                var b = new PyBuiltin(name, a => f(Ops.ToDouble(a.Need(0, "x"))));
                g[name] = b;
                std.globals[name] = b;
            }
            PyBuiltin Two(string name, Func<double, double, double> fn) {
                var b = new PyBuiltin(name, a => fn(Ops.ToDouble(a.Need(0, "x")), Ops.ToDouble(a.Need(1, "y"))));
                g[name] = b;
                std.globals[name] = b;
                return b;
            }
            Two("pow", Math.Pow);
            Two("atan2", Math.Atan2);
            Two("fmod", (x, y) => x % y);
            Two("fmin", Math.Min);
            Two("fmax", Math.Max);
            Two("hypot", (x, y) => Math.Sqrt(x * x + y * y));
            Two("copysign", (x, y) => Math.Abs(x) * (y < 0 || (y == 0 && 1 / y < 0) ? -1 : 1));
            g["M_PI"] = Math.PI;
            g["M_E"] = Math.E;
            // std::abs keeps ints ints; std::min/max take two values (or a list).
            std.globals["abs"] = new PyBuiltin("abs", a => a.Need(0, "x") is long l ? Math.Abs(l) : (object)Math.Abs(Ops.ToDouble(a.Need(0, "x"))));
            std.globals["min"] = vm.builtins["min"];
            std.globals["max"] = vm.builtins["max"];
            std.globals["clamp"] = new PyBuiltin("clamp", a => {
                var v = a.Need(0, "v");
                var lo = a.Need(1, "lo");
                var hi = a.Need(2, "hi");
                if (v is long lv && lo is long ll && hi is long lh) return Math.Max(ll, Math.Min(lh, lv));
                return Math.Max(Ops.ToDouble(lo), Math.Min(Ops.ToDouble(hi), Ops.ToDouble(v)));
            });
            std.globals["to_string"] = g["__to_string"];
            std.globals["stoi"] = new PyBuiltin("stoi", a => long.Parse(Ops.Str(a.Need(0, "s")).Trim(), CultureInfo.InvariantCulture));
            std.globals["stod"] = new PyBuiltin("stod", a => double.Parse(Ops.Str(a.Need(0, "s")).Trim(), CultureInfo.InvariantCulture));
            std.globals["swap"] = new PyBuiltin("swap", _ => throw new PyError("NotImplementedError", "std::swap needs references: swap the two values with a temporary"));
            std.globals["endl"] = "\n";
            std.globals["string"] = vm.builtins["str"];
            std.globals["vector"] = g["__vector"];
            std.globals["printf"] = g["__printf"];
            std.globals["cout"] = new PyBuiltin("cout", _ => PyNone.Value);
            std.globals["numbers"] = new PyModule("numbers") { globals = { ["pi"] = Math.PI, ["e"] = Math.E } };
            std.globals["size"] = vm.builtins["len"];
            vm.modules["cpp:std"] = std;
            g["std"] = std;
            g["endl"] = "\n";
            g["abs"] = std.globals["abs"];
            g["true"] = true;
            g["false"] = false;
        }

        public static long ToInt(object v) => v switch {
            long l => l,
            double d => double.IsNaN(d) || double.IsInfinity(d) ? 0 : (long)Math.Truncate(d),
            bool b => b ? 1 : 0,
            string s when s.Length == 1 => s[0],
            PyNone => 0,
            _ => Ops.ToLong(v),
        };

        // How std::cout shows a value: 6 significant digits for doubles, 1/0 for bools.
        private static string Stream(Interpreter vm, object o) => o switch {
            double d => G(d, 6),
            bool b => b ? "1" : "0",
            _ => vm.StrOf(o),
        };

        private static string G(double d, int precision) {
            if (double.IsNaN(d)) return "nan";
            if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
            if (d == 0) return "0";
            var exp = (int)Math.Floor(Math.Log10(Math.Abs(d)));
            if (exp < -4 || exp >= precision) {
                var m = d / Math.Pow(10, exp);
                var ms = m.ToString("F" + Math.Max(0, precision - 1), CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
                return ms + "e" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00");
            }
            var s = d.ToString("F" + Math.Max(0, precision - 1 - exp), CultureInfo.InvariantCulture);
            if (s.Contains(".")) s = s.TrimEnd('0').TrimEnd('.');
            return s;
        }

        /// <summary>printf formatting: %d %i %u %f %e %g %x %o %c %s %p %% with flags (- + 0 space #), width,
        /// precision (* too) and length modifiers (l, ll, h, z) as C does.</summary>
        public static string CFormat(Interpreter vm, string fmt, object[] args) {
            var sb = new StringBuilder();
            var next = 0;
            object Arg() => next < args.Length ? args[next++] : throw new PyError("TypeError", "printf has more %s than values");
            for (var i = 0; i < fmt.Length; i++) {
                var c = fmt[i];
                if (c != '%') { sb.Append(c); continue; }
                if (i + 1 < fmt.Length && fmt[i + 1] == '%') { sb.Append('%'); i++; continue; }
                var j = i + 1;
                bool left = false, plus = false, space = false, zero = false, alt = false;
                for (; j < fmt.Length; j++) {
                    var f = fmt[j];
                    if (f == '-') left = true;
                    else if (f == '+') plus = true;
                    else if (f == ' ') space = true;
                    else if (f == '0') zero = true;
                    else if (f == '#') alt = true;
                    else break;
                }
                var width = 0;
                if (j < fmt.Length && fmt[j] == '*') { width = (int)ToInt(Arg()); j++; }
                else while (j < fmt.Length && char.IsDigit(fmt[j])) width = width * 10 + (fmt[j++] - '0');
                var precision = -1;
                if (j < fmt.Length && fmt[j] == '.') {
                    j++;
                    precision = 0;
                    if (j < fmt.Length && fmt[j] == '*') { precision = (int)ToInt(Arg()); j++; }
                    else while (j < fmt.Length && char.IsDigit(fmt[j])) precision = precision * 10 + (fmt[j++] - '0');
                }
                while (j < fmt.Length && "hlLqjzt".IndexOf(fmt[j]) >= 0) j++;
                if (j >= fmt.Length) { sb.Append(fmt.Substring(i)); break; }
                var spec = fmt[j];
                i = j;
                string body;
                var numeric = true;
                switch (spec) {
                    case 'd': case 'i': case 'u': {
                        var v = ToInt(Arg());
                        body = Math.Abs(v).ToString(CultureInfo.InvariantCulture);
                        if (precision >= 0) body = body.PadLeft(precision, '0');
                        body = Sign(v < 0, plus, space) + body;
                        break;
                    }
                    case 'f': case 'F': {
                        var v = Ops.ToDouble(Arg());
                        body = Sign(v < 0 || (v == 0 && 1 / v < 0), plus, space) + Math.Abs(v).ToString("F" + (precision < 0 ? 6 : precision), CultureInfo.InvariantCulture);
                        break;
                    }
                    case 'e': case 'E': {
                        var v = Ops.ToDouble(Arg());
                        var p = precision < 0 ? 6 : precision;
                        var a = Math.Abs(v);
                        var exp = a == 0 ? 0 : (int)Math.Floor(Math.Log10(a));
                        var m = a == 0 ? 0 : a / Math.Pow(10, exp);
                        if (Math.Round(m, p) >= 10) { m /= 10; exp++; }
                        body = Sign(v < 0, plus, space) + m.ToString("F" + p, CultureInfo.InvariantCulture) + (spec == 'e' ? "e" : "E") + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00");
                        break;
                    }
                    case 'g': case 'G': {
                        var v = Ops.ToDouble(Arg());
                        body = Sign(v < 0, plus, space) + G(Math.Abs(v), precision < 0 ? 6 : Math.Max(1, precision));
                        if (spec == 'G') body = body.ToUpperInvariant();
                        break;
                    }
                    case 'x': case 'X': case 'o': {
                        var v = ToInt(Arg());
                        body = spec == 'o' ? Convert.ToString(v, 8) : v.ToString(spec == 'x' ? "x" : "X", CultureInfo.InvariantCulture);
                        if (alt && v != 0) body = (spec == 'o' ? "0" : spec == 'x' ? "0x" : "0X") + body;
                        break;
                    }
                    case 'c': {
                        var v = Arg();
                        body = v is string s ? s : ((char)ToInt(v)).ToString();
                        numeric = false;
                        break;
                    }
                    case 's': {
                        var v = Arg();
                        body = v is string s ? s : vm.StrOf(v);
                        if (precision >= 0 && body.Length > precision) body = body.Substring(0, precision);
                        numeric = false;
                        break;
                    }
                    case 'p': Arg(); body = "0x0"; break;
                    default: body = "%" + spec; numeric = false; break;
                }
                if (body.Length < width) {
                    if (left) body = body.PadRight(width);
                    else if (zero && numeric) {
                        var signed = body.Length > 0 && (body[0] == '-' || body[0] == '+' || body[0] == ' ');
                        body = signed ? body[0] + body.Substring(1).PadLeft(width - 1, '0') : body.PadLeft(width, '0');
                    }
                    else body = body.PadLeft(width);
                }
                sb.Append(body);
            }
            return sb.ToString();
        }

        private static string Sign(bool negative, bool plus, bool space) => negative ? "-" : plus ? "+" : space ? " " : "";
    }
}
