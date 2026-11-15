using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Protobot.Scripting {
    public sealed class PySet {
        public readonly HashSet<object> items = new(PyEquality.Instance);
        public readonly List<object> order = new();
        public void Add(object o) {
            if (items.Add(o)) order.Add(o);
        }
        public bool Remove(object o) {
            if (!items.Remove(o)) return false;
            for (var i = 0; i < order.Count; i++)
                if (Ops.Eq(order[i], o)) {
                    order.RemoveAt(i);
                    break;
                }
            return true;
        }
    }

    public sealed class PyIterator {
        public readonly IEnumerator<object> e;
        public PyIterator(IEnumerator<object> e) => this.e = e;
    }

    /// <summary>V2 (robot code): Python's operators, conversions and printing on plain values.</summary>
    public static class Ops {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string TypeName(object o) => o switch {
            null => "NoneType",
            PyNone => "NoneType",
            bool => "bool",
            long => "int",
            double => "float",
            string => "str",
            PyList => "list",
            PyTuple => "tuple",
            PyDict => "dict",
            PySet => "set",
            PyRange => "range",
            PyFunction => "function",
            PyBuiltin => "builtin_function",
            PyBoundMethod => "method",
            PyClass => "type",
            PyInstance i => i.cls.name,
            PyModule => "module",
            IPyObject p => p.TypeName,
            PyEnum e => e.type,
            PyIterator => "iterator",
            PySlice => "slice",
            _ => o.GetType().Name,
        };

        // ------------------------------------------------------------------ truth and conversions

        public static bool Truthy(object o) => o switch {
            null => false,
            PyNone => false,
            bool b => b,
            long l => l != 0,
            double d => d != 0,
            string s => s.Length > 0,
            PyList l => l.items.Count > 0,
            PyTuple t => t.items.Length > 0,
            PyDict d => d.items.Count > 0,
            PySet s => s.items.Count > 0,
            PyRange r => r.Count > 0,
            _ => true,
        };

        public static bool IsNumber(object o) => o is long or double or bool;

        public static double ToDouble(object o, string what = null) => o switch {
            long l => l,
            double d => d,
            bool b => b ? 1 : 0,
            PyEnum e => e.value,
            _ => throw new PyError("TypeError", (what != null ? what + ": " : "") + "expected a number, got " + Describe(o)),
        };

        public static long ToLong(object o, string what = null) => o switch {
            long l => l,
            bool b => b ? 1 : 0,
            double d when d == Math.Floor(d) => (long)d,
            double => throw new PyError("TypeError", (what != null ? what + ": " : "") + "expected a whole number, got a float"),
            _ => throw new PyError("TypeError", (what != null ? what + ": " : "") + "expected a whole number, got " + Describe(o)),
        };

        /// <summary>"a str ('hi')" for messages.</summary>
        public static string Describe(object o) {
            var name = TypeName(o);
            var article = "aeiou".IndexOf(char.ToLowerInvariant(name[0])) >= 0 ? "an " : "a ";
            if (o is PyNone || o == null) return "None";
            if (o is string or long or double or bool) return article + name + " (" + Repr(o) + ")";
            return article + name;
        }

        // ------------------------------------------------------------------ printing

        public static string Str(object o) => o switch {
            string s => s,
            PyEnum e => e.name,
            _ => Repr(o),
        };

        public static string Repr(object o) {
            switch (o) {
                case null:
                case PyNone:
                    return "None";
                case bool b:
                    return b ? "True" : "False";
                case long l:
                    return l.ToString(Inv);
                case double d:
                    return FloatRepr(d);
                case string s:
                    return StrRepr(s);
                case PyList l:
                    return "[" + string.Join(", ", l.items.Select(ReprShallow)) + "]";
                case PyTuple t:
                    return t.items.Length == 1 ? "(" + ReprShallow(t.items[0]) + ",)" : "(" + string.Join(", ", t.items.Select(ReprShallow)) + ")";
                case PyDict d:
                    return "{" + string.Join(", ", d.Keys.Select(k => ReprShallow(k) + ": " + ReprShallow(d.items[k]))) + "}";
                case PySet s:
                    return s.order.Count == 0 ? "set()" : "{" + string.Join(", ", s.order.Select(ReprShallow)) + "}";
                case PyRange r:
                    return r.step == 1 ? "range(" + r.start + ", " + r.stop + ")" : "range(" + r.start + ", " + r.stop + ", " + r.step + ")";
                case PyFunction f:
                    return "<function " + f.Name + ">";
                case PyBuiltin b:
                    return "<built-in function " + b.name + ">";
                case PyBoundMethod m:
                    return "<bound method " + (m.function is PyFunction pf ? pf.Name : m.function is PyBuiltin pb ? pb.name : "?") + ">";
                case PyClass c:
                    return "<class '" + c.name + "'>";
                case PyInstance i:
                    if (i.cls.mro.Any(c => c.name == "BaseException")) return i.cls.name + "(" + string.Join(", ", ExceptionArgs(i).Select(ReprShallow)) + ")";
                    return "<" + i.cls.name + " object>";
                case PyModule m:
                    return "<module '" + m.name + "'>";
                case PyEnum e:
                    return e.name;
                case IPyObject p:
                    return "<" + p.TypeName + ">";
                default:
                    return o.ToString();
            }
        }

        [ThreadStatic] private static int reprDepth;

        private static string ReprShallow(object o) {
            if (reprDepth > 20) return "...";
            reprDepth++;
            try {
                return Repr(o);
            }
            finally {
                reprDepth--;
            }
        }

        public static string FloatRepr(double d) {
            if (double.IsNaN(d)) return "nan";
            if (double.IsPositiveInfinity(d)) return "inf";
            if (double.IsNegativeInfinity(d)) return "-inf";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e16) return ((decimal)d).ToString(Inv) + ".0";
            var s = d.ToString("R", Inv);
            if (s.Contains("E")) {
                // 1E-05 -> 1e-05
                var parts = s.Split('E');
                var exp = int.Parse(parts[1], Inv);
                return parts[0] + "e" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00", Inv);
            }
            return s;
        }

        public static string StrRepr(string s) {
            var quote = s.Contains('\'') && !s.Contains('"') ? '"' : '\'';
            var sb = new StringBuilder();
            sb.Append(quote);
            foreach (var c in s) {
                switch (c) {
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (c == quote) sb.Append('\\').Append(c);
                        else if (c < ' ') sb.Append("\\x").Append(((int)c).ToString("x2"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append(quote);
            return sb.ToString();
        }

        public static object[] ExceptionArgs(PyInstance i) =>
            i.dict.TryGetValue("args", out var a) && a is PyTuple t ? t.items : Array.Empty<object>();

        public static string ExceptionMessage(PyInstance i) {
            var args = ExceptionArgs(i);
            return args.Length == 0 ? "" : args.Length == 1 ? Str(args[0]) : Repr(new PyTuple(args));
        }

        // ------------------------------------------------------------------ equality and order

        public static bool Eq(object a, object b) {
            if (ReferenceEquals(a, b)) return true;
            if (a is PyNone || b is PyNone || a == null || b == null) return (a is PyNone || a == null) && (b is PyNone || b == null);
            if (a is long la && b is long lb) return la == lb;
            if (IsNumber(a) && IsNumber(b)) return ToDouble(a) == ToDouble(b);
            switch (a) {
                case string sa: return b is string sb && sa == sb;
                case PyTuple ta when b is PyTuple tb:
                    if (ta.items.Length != tb.items.Length) return false;
                    for (var i = 0; i < ta.items.Length; i++)
                        if (!Eq(ta.items[i], tb.items[i])) return false;
                    return true;
                case PyList xa when b is PyList xb:
                    if (xa.items.Count != xb.items.Count) return false;
                    for (var i = 0; i < xa.items.Count; i++)
                        if (!Eq(xa.items[i], xb.items[i])) return false;
                    return true;
                case PyDict da when b is PyDict db:
                    if (da.items.Count != db.items.Count) return false;
                    foreach (var kv in da.items)
                        if (!db.items.TryGetValue(kv.Key, out var v) || !Eq(kv.Value, v)) return false;
                    return true;
                case PySet sa2 when b is PySet sb2:
                    return sa2.items.SetEquals(sb2.items);
                case PyEnum ea when b is PyEnum eb:
                    return ea.type == eb.type && ea.value == eb.value;
                case PyRange ra when b is PyRange rb:
                    return ra.start == rb.start && ra.stop == rb.stop && ra.step == rb.step;
            }
            return false;
        }

        /// <summary>-1, 0, 1 like Python's &lt; on numbers, strings, lists and tuples.</summary>
        public static int CompareValues(object a, object b, string op) {
            if (a is long la && b is long lb) return la.CompareTo(lb);
            if (IsNumber(a) && IsNumber(b)) {
                var da = ToDouble(a);
                var db = ToDouble(b);
                return da < db ? -1 : da > db ? 1 : 0;
            }
            if (a is string sa && b is string sb) return Math.Sign(string.CompareOrdinal(sa, sb));
            if (a is PyList || a is PyTuple) {
                var xa = a is PyList pl ? pl.items.ToArray() : ((PyTuple)a).items;
                object[] xb = b switch { PyList ql when a is PyList => ql.items.ToArray(), PyTuple qt when a is PyTuple => qt.items, _ => null };
                if (xb != null) {
                    for (var i = 0; i < Math.Min(xa.Length, xb.Length); i++) {
                        if (Eq(xa[i], xb[i])) continue;
                        return CompareValues(xa[i], xb[i], op);
                    }
                    return xa.Length.CompareTo(xb.Length);
                }
            }
            throw new PyError("TypeError", "'" + op + "' can't compare " + Describe(a) + " with " + Describe(b));
        }

        // ------------------------------------------------------------------ arithmetic

        public static object Binary(string op, object a, object b) {
            if (a is bool ba) a = ba ? 1L : 0L;
            if (b is bool bb) b = bb ? 1L : 0L;
            if (a is long x && b is long y) {
                switch (op) {
                    case "+": return checked(x + y);
                    case "-": return checked(x - y);
                    case "*": return checked(x * y);
                    case "/":
                        if (y == 0) throw new PyError("ZeroDivisionError", "division by zero");
                        return (double)x / y;
                    case "//":
                        if (y == 0) throw new PyError("ZeroDivisionError", "integer division by zero");
                        return FloorDiv(x, y);
                    case "%":
                        if (y == 0) throw new PyError("ZeroDivisionError", "integer modulo by zero");
                        return x - FloorDiv(x, y) * y;
                    case "**":
                        if (y < 0) return Math.Pow(x, y);
                        return IntPow(x, y);
                    case "<<": return x << (int)y;
                    case ">>": return x >> (int)y;
                    case "&": return x & y;
                    case "|": return x | y;
                    case "^": return x ^ y;
                }
            }
            else if (IsNumber(a) && IsNumber(b)) {
                var p = ToDouble(a);
                var q = ToDouble(b);
                switch (op) {
                    case "+": return p + q;
                    case "-": return p - q;
                    case "*": return p * q;
                    case "/":
                        if (q == 0) throw new PyError("ZeroDivisionError", "float division by zero");
                        return p / q;
                    case "//":
                        if (q == 0) throw new PyError("ZeroDivisionError", "float floor division by zero");
                        return Math.Floor(p / q);
                    case "%":
                        if (q == 0) throw new PyError("ZeroDivisionError", "float modulo");
                        var r = p - q * Math.Floor(p / q);
                        return r;
                    case "**":
                        if (p < 0 && q != Math.Floor(q)) throw new PyError("ValueError", "a negative number to a fractional power");
                        return Math.Pow(p, q);
                }
            }
            switch (op) {
                case "+":
                    if (a is string s1 && b is string s2) return s1 + s2;
                    if (a is PyList l1 && b is PyList l2) {
                        var l = new PyList(l1.items);
                        l.items.AddRange(l2.items);
                        return l;
                    }
                    if (a is PyTuple t1 && b is PyTuple t2) return new PyTuple(t1.items.Concat(t2.items).ToArray());
                    if (a is string && IsNumber(b) || IsNumber(a) && b is string)
                        throw new PyError("TypeError", "can't add " + Describe(a) + " and " + Describe(b) + ": use str(...) to turn the number into text, or an f-string");
                    break;
                case "*":
                    if (a is string rs && b is long rn) return Repeat(rs, rn);
                    if (a is long rn2 && b is string rs2) return Repeat(rs2, rn2);
                    if (a is PyList rl && b is long rc) return RepeatList(rl, rc);
                    if (a is long rc2 && b is PyList rl2) return RepeatList(rl2, rc2);
                    if (a is PyTuple rt && b is long tc) return new PyTuple(new PyList(Enumerable.Range(0, (int)Math.Max(0, tc)).SelectMany(_ => rt.items)).items.ToArray());
                    break;
                case "%":
                    if (a is string fmt) return PercentFormat(fmt, b);
                    break;
                case "-":
                    if (a is PySet sa && b is PySet sb) {
                        var r = new PySet();
                        foreach (var o in sa.order)
                            if (!sb.items.Contains(o)) r.Add(o);
                        return r;
                    }
                    break;
                case "|":
                    if (a is PySet ua && b is PySet ub) {
                        var r = new PySet();
                        foreach (var o in ua.order) r.Add(o);
                        foreach (var o in ub.order) r.Add(o);
                        return r;
                    }
                    if (a is PyDict da && b is PyDict db) {
                        var r = new PyDict();
                        foreach (var k in da.Keys) r.Set(k, da.items[k]);
                        foreach (var k in db.Keys) r.Set(k, db.items[k]);
                        return r;
                    }
                    break;
                case "&":
                    if (a is PySet ia && b is PySet ib) {
                        var r = new PySet();
                        foreach (var o in ia.order)
                            if (ib.items.Contains(o)) r.Add(o);
                        return r;
                    }
                    break;
            }
            throw new PyError("TypeError", "can't use '" + op + "' on " + Describe(a) + " and " + Describe(b));
        }

        private static long FloorDiv(long x, long y) {
            var q = x / y;
            if (x % y != 0 && (x < 0) != (y < 0)) q--;
            return q;
        }

        private static object IntPow(long x, long y) {
            long result = 1;
            try {
                checked {
                    var b = x;
                    var e = y;
                    while (e > 0) {
                        if ((e & 1) == 1) result *= b;
                        e >>= 1;
                        if (e > 0) b *= b;
                    }
                }
                return result;
            }
            catch (OverflowException) {
                return Math.Pow(x, y);
            }
        }

        private static string Repeat(string s, long n) {
            if (n <= 0) return "";
            if (s.Length * n > 10_000_000) throw new PyError("MemoryError", "that string would be too long");
            var sb = new StringBuilder();
            for (var i = 0; i < n; i++) sb.Append(s);
            return sb.ToString();
        }

        private static PyList RepeatList(PyList l, long n) {
            if (l.items.Count * n > 10_000_000) throw new PyError("MemoryError", "that list would be too long");
            var r = new PyList();
            for (var i = 0; i < n; i++) r.items.AddRange(l.items);
            return r;
        }

        public static object Unary(string op, object a) {
            switch (op) {
                case "not": return !Truthy(a);
                case "-":
                    if (a is long l) return checked(-l);
                    if (a is double d) return -d;
                    if (a is bool b) return b ? -1L : 0L;
                    break;
                case "+":
                    if (a is long or double) return a;
                    if (a is bool b2) return b2 ? 1L : 0L;
                    break;
                case "~":
                    if (a is long l2) return ~l2;
                    if (a is bool b3) return b3 ? -2L : -1L;
                    break;
            }
            throw new PyError("TypeError", "can't use unary '" + op + "' on " + Describe(a));
        }

        public static bool Contains(object container, object item) {
            switch (container) {
                case string s:
                    if (item is string sub) return s.Contains(sub);
                    throw new PyError("TypeError", "'in <string>' needs a string on the left, not " + Describe(item));
                case PyList l:
                    foreach (var o in l.items)
                        if (Eq(o, item)) return true;
                    return false;
                case PyTuple t:
                    foreach (var o in t.items)
                        if (Eq(o, item)) return true;
                    return false;
                case PyDict d: return d.items.ContainsKey(item);
                case PySet st: return st.items.Contains(item);
                case PyRange r:
                    if (!IsNumber(item)) return false;
                    var v = ToDouble(item);
                    if (v != Math.Floor(v)) return false;
                    var n = (long)v;
                    if (r.step > 0 ? n < r.start || n >= r.stop : n > r.start || n <= r.stop) return false;
                    return (n - r.start) % r.step == 0;
            }
            throw new PyError("TypeError", "'in' can't look inside " + Describe(container));
        }

        // ------------------------------------------------------------------ items

        public static long Len(object o) => o switch {
            string s => s.Length,
            PyList l => l.items.Count,
            PyTuple t => t.items.Length,
            PyDict d => d.items.Count,
            PySet s => s.items.Count,
            PyRange r => r.Count,
            _ => throw new PyError("TypeError", Describe(o) + " has no len()"),
        };

        private static int Index(object index, int count, string what) {
            var i = ToLong(index, what + " index");
            if (i < 0) i += count;
            if (i < 0 || i >= count) throw new PyError("IndexError", what + " index " + ToLong(index) + " is out of range (it has " + count + " item" + (count == 1 ? "" : "s") + ")");
            return (int)i;
        }

        public static (int start, int stop, int step) SliceBounds(PySlice s, int count) {
            var step = s.step is PyNone || s.step == null ? 1 : (int)ToLong(s.step, "slice step");
            if (step == 0) throw new PyError("ValueError", "a slice step can't be zero");
            int Clamp(object v, int fallback) {
                if (v is PyNone || v == null) return fallback;
                var i = ToLong(v, "slice");
                if (i < 0) i += count;
                if (step > 0) return (int)Math.Max(0, Math.Min(count, i));
                return (int)Math.Max(-1, Math.Min(count - 1, i));
            }
            var start = Clamp(s.start, step > 0 ? 0 : count - 1);
            var stop = Clamp(s.stop, step > 0 ? count : -1);
            return (start, stop, step);
        }

        private static IEnumerable<int> SliceIndexes(PySlice s, int count) {
            var (start, stop, step) = SliceBounds(s, count);
            if (step > 0)
                for (var i = start; i < stop; i += step) yield return i;
            else
                for (var i = start; i > stop; i += step) yield return i;
        }

        public static object GetItem(object o, object index) {
            switch (o) {
                case PyList l:
                    if (index is PySlice ls) return new PyList(SliceIndexes(ls, l.items.Count).Select(i => l.items[i]));
                    return l.items[Index(index, l.items.Count, "list")];
                case PyTuple t:
                    if (index is PySlice ts) return new PyTuple(SliceIndexes(ts, t.items.Length).Select(i => t.items[i]).ToArray());
                    return t.items[Index(index, t.items.Length, "tuple")];
                case string s:
                    if (index is PySlice ss) return new string(SliceIndexes(ss, s.Length).Select(i => s[i]).ToArray());
                    return s[Index(index, s.Length, "string")].ToString();
                case PyDict d:
                    if (d.items.TryGetValue(index, out var v)) return v;
                    throw new PyError("KeyError", Repr(index));
                case PyRange r:
                    var count = (int)Math.Min(int.MaxValue, r.Count);
                    return r.start + r.step * Index(index, count, "range");
            }
            throw new PyError("TypeError", Describe(o) + " can't be indexed with [...]");
        }

        public static void SetItem(object o, object index, object value) {
            switch (o) {
                case PyList l:
                    if (index is PySlice s) {
                        var (start, stop, step) = SliceBounds(s, l.items.Count);
                        if (step != 1) throw new PyError("ValueError", "only plain slices (a:b) can be assigned to");
                        var items = Iterate(value).ToList();
                        l.items.RemoveRange(start, Math.Max(0, stop - start));
                        l.items.InsertRange(start, items);
                        return;
                    }
                    l.items[Index(index, l.items.Count, "list")] = value;
                    return;
                case PyDict d:
                    d.Set(index, value);
                    return;
                case PyTuple:
                    throw new PyError("TypeError", "a tuple can't be changed: use a list [...] instead");
                case string:
                    throw new PyError("TypeError", "a string can't be changed: make a new one");
            }
            throw new PyError("TypeError", Describe(o) + " doesn't take items with [...] = ");
        }

        public static void DelItem(object o, object index) {
            switch (o) {
                case PyList l:
                    if (index is PySlice s) {
                        foreach (var i in SliceIndexes(s, l.items.Count).OrderByDescending(i => i)) l.items.RemoveAt(i);
                        return;
                    }
                    l.items.RemoveAt(Index(index, l.items.Count, "list"));
                    return;
                case PyDict d:
                    if (!d.Remove(index)) throw new PyError("KeyError", Repr(index));
                    return;
            }
            throw new PyError("TypeError", "can't delete items of " + Describe(o));
        }

        // ------------------------------------------------------------------ iteration

        public static IEnumerable<object> Iterate(object o) {
            switch (o) {
                case PyList l: return ListItems(l);
                case PyTuple t: return t.items;
                case string s: return s.Select(c => (object)c.ToString());
                case PyDict d: return d.order.ToList();
                case PySet st: return st.order.ToList();
                case PyRange r: return RangeItems(r);
                case PyIterator it: return Rest(it);
            }
            throw new PyError("TypeError", Describe(o) + " can't be looped over");
        }

        private static IEnumerable<object> ListItems(PyList l) {
            for (var i = 0; i < l.items.Count; i++) yield return l.items[i];
        }

        private static IEnumerable<object> RangeItems(PyRange r) {
            if (r.step > 0)
                for (var i = r.start; i < r.stop; i += r.step) yield return i;
            else
                for (var i = r.start; i > r.stop; i += r.step) yield return i;
        }

        private static IEnumerable<object> Rest(PyIterator it) {
            while (it.e.MoveNext()) yield return it.e.Current;
        }

        public static PyIterator GetIter(object o) => o as PyIterator ?? new PyIterator(Iterate(o).GetEnumerator());

        // ------------------------------------------------------------------ formatting

        public static string Format(object v, string spec) {
            if (string.IsNullOrEmpty(spec)) return Str(v);
            // [[fill]align][sign][#][0][width][,][.precision][type]
            var i = 0;
            char fill = ' ', align = '\0', sign = '-';
            if (spec.Length >= 2 && "<>^=".IndexOf(spec[1]) >= 0) {
                fill = spec[0];
                align = spec[1];
                i = 2;
            }
            else if (spec.Length >= 1 && "<>^=".IndexOf(spec[0]) >= 0) {
                align = spec[0];
                i = 1;
            }
            if (i < spec.Length && "+- ".IndexOf(spec[i]) >= 0) sign = spec[i++];
            if (i < spec.Length && spec[i] == '#') i++;
            if (i < spec.Length && spec[i] == '0') {
                if (align == '\0') {
                    fill = '0';
                    align = '=';
                }
                i++;
            }
            var width = 0;
            while (i < spec.Length && char.IsDigit(spec[i])) width = width * 10 + (spec[i++] - '0');
            var comma = false;
            if (i < spec.Length && (spec[i] == ',' || spec[i] == '_')) {
                comma = true;
                i++;
            }
            var precision = -1;
            if (i < spec.Length && spec[i] == '.') {
                i++;
                precision = 0;
                while (i < spec.Length && char.IsDigit(spec[i])) precision = precision * 10 + (spec[i++] - '0');
            }
            var type = i < spec.Length ? spec[i++] : '\0';
            if (i < spec.Length) throw new PyError("ValueError", "invalid format '" + spec + "'");

            string body;
            var negative = false;
            if (v is bool vb && type != '\0' && type != 's') v = vb ? 1L : 0L;
            if (type == 's' || v is string) {
                if (!(v is string) && type == 's') v = Str(v);
                if (!(v is string)) throw new PyError("ValueError", "format code '" + type + "' can't be used on " + Describe(v));
                body = (string)v;
                if (precision >= 0 && body.Length > precision) body = body.Substring(0, precision);
                if (align == '\0') align = '<';
            }
            else if (IsNumber(v)) {
                if (type == '\0' && v is long) type = 'd';
                var d = ToDouble(v);
                negative = d < 0 || d == 0 && 1 / d < 0 && v is double;
                var a = Math.Abs(d);
                switch (type) {
                    case 'd':
                    case 'n':
                        if (v is double) throw new PyError("ValueError", "format code 'd' needs a whole number (an int); use '.0f' for a float");
                        body = Math.Abs((long)ToLong(v)).ToString(comma ? "#,0" : "0", Inv);
                        break;
                    case 'f':
                    case 'F':
                        if (precision < 0) precision = 6;
                        if (precision <= 15 && !double.IsInfinity(a) && !double.IsNaN(a)) a = Math.Round(a, precision, MidpointRounding.ToEven);   // Python rounds halves to even
                        body = a.ToString((comma ? "#,0." : "0.") + new string('0', precision < 0 ? 6 : precision), Inv).TrimEnd('.');
                        if (precision == 0) body = Math.Round(a, MidpointRounding.ToEven).ToString(comma ? "#,0" : "0", Inv);
                        break;
                    case 'e':
                    case 'E':
                        body = ExpFormat(a, precision < 0 ? 6 : precision, type == 'E');
                        break;
                    case '%':
                        body = (a * 100).ToString("0." + new string('0', precision < 0 ? 6 : precision), Inv).TrimEnd('.') + "%";
                        break;
                    case 'x':
                    case 'X':
                        body = Convert.ToString(Math.Abs(ToLong(v)), 16);
                        if (type == 'X') body = body.ToUpperInvariant();
                        break;
                    case 'b':
                        body = Convert.ToString(Math.Abs(ToLong(v)), 2);
                        break;
                    case 'o':
                        body = Convert.ToString(Math.Abs(ToLong(v)), 8);
                        break;
                    case 'g':
                    case 'G':
                    case '\0':
                        if (type == '\0' && precision < 0) {
                            body = FloatRepr(a);
                            if (comma) body = a.ToString("#,0.############", Inv);
                        }
                        else {
                            var p = precision < 0 ? 6 : Math.Max(precision, 1);
                            var exp = a == 0 ? 0 : (int)Math.Floor(Math.Log10(a));
                            if (exp < -4 || exp >= p) body = ExpFormat(a, p - 1, type == 'G').Replace("0e", "e");
                            else {
                                body = a.ToString("0." + new string('#', Math.Max(0, p - 1 - exp)), Inv);
                                if (type == '\0' && !body.Contains(".") && v is double) body += ".0";
                            }
                        }
                        break;
                    default:
                        throw new PyError("ValueError", "unknown format code '" + type + "' for " + Describe(v));
                }
                if (align == '\0') align = '>';
            }
            else {
                body = Str(v);
                if (align == '\0') align = '<';
            }
            var signText = negative ? "-" : sign == '+' ? "+" : sign == ' ' ? " " : "";
            if (align == '=') {
                var pad = Math.Max(0, width - signText.Length - body.Length);
                return signText + new string(fill, pad) + body;
            }
            body = signText + body;
            var padding = Math.Max(0, width - body.Length);
            return align switch {
                '<' => body + new string(fill, padding),
                '^' => new string(fill, padding / 2) + body + new string(fill, padding - padding / 2),
                _ => new string(fill, padding) + body,
            };
        }

        private static string ExpFormat(double a, int precision, bool upper) {
            var s = a.ToString((precision > 0 ? "0." + new string('0', precision) : "0") + "e+00", Inv);
            return upper ? s.ToUpperInvariant() : s;
        }

        /// <summary>"x = %d" % value.</summary>
        public static string PercentFormat(string fmt, object args) {
            var values = args is PyTuple t ? t.items : new[] { args };
            var sb = new StringBuilder();
            var n = 0;
            for (var i = 0; i < fmt.Length; i++) {
                var c = fmt[i];
                if (c != '%') {
                    sb.Append(c);
                    continue;
                }
                i++;
                if (i >= fmt.Length) throw new PyError("ValueError", "a '%' at the end of the format");
                if (fmt[i] == '%') {
                    sb.Append('%');
                    continue;
                }
                var start = i;
                while (i < fmt.Length && "-+ 0#.0123456789".IndexOf(fmt[i]) >= 0) i++;
                if (i >= fmt.Length) throw new PyError("ValueError", "an unfinished '%' format");
                var flags = fmt.Substring(start, i - start);
                var type = fmt[i];
                if (n >= values.Length) throw new PyError("TypeError", "not enough values for the format string");
                var v = values[n++];
                var align = flags.StartsWith("-") ? "<" : "";
                var spec = align + flags.TrimStart('-');
                switch (type) {
                    case 's': sb.Append(Format(Str(v), spec)); break;
                    case 'r': sb.Append(Format(Repr(v), spec)); break;
                    case 'd':
                    case 'i': sb.Append(Format(v is double dv ? (long)Math.Truncate(dv) : v, spec + "d")); break;
                    case 'f':
                    case 'F':
                    case 'e':
                    case 'g':
                    case 'x':
                    case 'X':
                        sb.Append(Format(type == 'x' || type == 'X' ? v : ToDouble(v), spec + type));
                        break;
                    default: throw new PyError("ValueError", "unsupported format character '" + type + "'");
                }
            }
            if (n < values.Length && args is PyTuple) throw new PyError("TypeError", "not all values were used in the format string");
            return sb.ToString();
        }

        /// <summary>"{} is {:.1f}".format(...)</summary>
        public static string StrFormat(string fmt, object[] pos, Dictionary<string, object> kw) {
            var sb = new StringBuilder();
            var auto = 0;
            for (var i = 0; i < fmt.Length; i++) {
                var c = fmt[i];
                if (c == '{' && i + 1 < fmt.Length && fmt[i + 1] == '{') {
                    sb.Append('{');
                    i++;
                    continue;
                }
                if (c == '}' && i + 1 < fmt.Length && fmt[i + 1] == '}') {
                    sb.Append('}');
                    i++;
                    continue;
                }
                if (c != '{') {
                    sb.Append(c);
                    continue;
                }
                var end = fmt.IndexOf('}', i);
                if (end < 0) throw new PyError("ValueError", "a '{' without its '}' in the format string");
                var field = fmt.Substring(i + 1, end - i - 1);
                string spec = null, conv = null;
                var colon = field.IndexOf(':');
                if (colon >= 0) {
                    spec = field.Substring(colon + 1);
                    field = field.Substring(0, colon);
                }
                var bang = field.IndexOf('!');
                if (bang >= 0) {
                    conv = field.Substring(bang + 1);
                    field = field.Substring(0, bang);
                }
                object v;
                if (field.Length == 0) {
                    if (auto >= pos.Length) throw new PyError("IndexError", "not enough values for the format string's {}");
                    v = pos[auto++];
                }
                else if (int.TryParse(field, out var idx)) {
                    if (idx >= pos.Length) throw new PyError("IndexError", "format index " + idx + " is out of range");
                    v = pos[idx];
                }
                else if (kw != null && kw.TryGetValue(field, out var named)) v = named;
                else throw new PyError("KeyError", "'" + field + "'");
                if (conv == "r") v = Repr(v);
                else if (conv == "s") v = Str(v);
                sb.Append(Format(v, spec));
                i = end;
            }
            return sb.ToString();
        }
    }
}
